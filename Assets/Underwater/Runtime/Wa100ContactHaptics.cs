using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.InputSystem.XR.Haptics;
using UnityEngine.XR;

namespace UnderwaterBaseline
{
    // Feedback only: never changes collision filters, joint drives or motion guards.
    [DefaultExecutionOrder(260)]
    public sealed class Wa100ContactHaptics : MonoBehaviour
    {
        public Wa100SurfaceContact contact;
        public UnderwaterGraspTask task;
        [Range(0,1)]public float amplitude=.8f;
        [Range(.02f,.5f)]public float pulseSeconds=.12f;
        [Min(.05f)]public float repeatSeconds=.18f;
        [Min(1)]public float queryHz=30;
        [Min(0)]public float contactSkin=.004f;
        public LayerMask feedbackMask=~((1<<2)|(1<<5));
        public bool ContactActive {get;private set;}
        public string ContactName {get;private set;}="none";
        public string Status {get;private set;}="Haptics: waiting for contact";
        public int ContactEpisodes {get;private set;}
        public int PulseRequests {get;private set;}
        public int AcceptedPulses {get;private set;}
        public int RejectedPulses {get;private set;}
        public string TransportDetails {get;private set;}="not attempted";
        readonly List<Collider> handShapes=new List<Collider>();
        readonly Collider[] nearby=new Collider[128];
        float nextQuery,nextPulse,reportedUntil;
        string reportedName;
        bool vibrating;

        public void Bind()
        {
            if(!contact)contact=GetComponent<Wa100SurfaceContact>();
            if(!contact || !contact.Hand)return;
            contact.Haptics=this;
            handShapes.Clear();
            foreach(var c in contact.Hand.GetComponentsInChildren<Collider>(true))
                if(c && c.enabled && !c.isTrigger && !(c is MeshCollider mesh && !mesh.convex))handShapes.Add(c);
            foreach(var body in contact.Hand.GetComponentsInChildren<ArticulationBody>(true))
            {
                var relay=body.GetComponent<Wa100ContactRelay>()??body.gameObject.AddComponent<Wa100ContactRelay>();
                relay.owner=this;
            }
        }
        void Start(){Bind();}
        public bool IsExternal(Collider c)
        {
            if(!c || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy || !contact || !contact.Arm)return false;
            // A held object remains an external tactile surface even while parented.
            if(task && task.Held && c.transform.IsChildOf(task.Held.transform))return true;
            if(c.transform.IsChildOf(contact.Arm.transform))return false;
            var motor=contact.Arm.Motor;
            return !motor || !c.transform.IsChildOf(motor.transform);
        }
        public void ReportContact(Collider other)
        {
            if(!IsExternal(other))return;
            reportedName=other.name;reportedUntil=Time.unscaledTime+.075f;
        }
        public bool ScanContact(out string surface)
        {
            surface="";if(!contact || !contact.Hand)return false;
            foreach(var shape in handShapes)if(shape && shape.enabled && shape.gameObject.activeInHierarchy)
            {
                var bounds=shape.bounds;
                int count=Physics.OverlapBoxNonAlloc(bounds.center,bounds.extents+Vector3.one*contactSkin,nearby,Quaternion.identity,feedbackMask,QueryTriggerInteraction.Ignore);
                for(int i=0;i<count;i++)
                {
                    var other=nearby[i];if(!IsExternal(other))continue;
                    if(Physics.ComputePenetration(shape,shape.transform.position,shape.transform.rotation,other,other.transform.position,other.transform.rotation,out _,out _))
                    {surface=other.name;return true;}
                    // Existing motion guards stop at a 3 mm skin before a native
                    // collision occurs. Include this narrow band, not mere bounds.
                    if(!(other is MeshCollider m) || m.convex)
                    {
                        Vector3 a=shape.ClosestPoint(other.bounds.center);
                        Vector3 b=other.ClosestPoint(a);
                        a=shape.ClosestPoint(b);
                        if(Vector3.Distance(a,b)<=contactSkin){surface=other.name;return true;}
                    }
                }
                // Sand/robot native collision pairs are ignored deliberately.
                // Query the logical visible sand volume without making it solid.
                var bench=task?task.GetComponentInChildren<UnderwaterGraspBench>():null;
                if(bench && bench.sand && bounds.min.y<=bench.SurfaceHeight && bounds.max.y>=bench.sand.bounds.min.y &&
                   bounds.max.x>bench.sand.bounds.min.x && bounds.min.x<bench.sand.bounds.max.x &&
                   bounds.max.z>bench.sand.bounds.min.z && bounds.min.z<bench.sand.bounds.max.z)
                {surface="sand / inserted finger";return true;}
            }
            return false;
        }
        void LateUpdate()
        {
            if(!contact || !contact.Arm)return;
            var arm=contact.Arm;
            if(arm.Motor && arm.Motor.InSeabed!=arm.SeabedArea){SetContact(false,"inactive area");Stop();return;}
            if(Time.unscaledTime>=nextQuery)
            {
                nextQuery=Time.unscaledTime+1/Mathf.Max(1,queryHz);
                bool found=ScanContact(out string name);
                if(Time.unscaledTime<reportedUntil){found=true;name=reportedName;}
                SetContact(found,name);
            }
            if(ContactActive)RequestPulse();
            else if(vibrating){Stop();Status="No contact | last "+TransportDetails;}
        }
        void SetContact(bool active,string name)
        {
            if(active && !ContactActive){ContactEpisodes++;nextPulse=0;}
            ContactActive=active;ContactName=active?name:"none";
        }
        public void RequestPulse()
        {
            if(!contact || !contact.HapticsEnabled || Time.unscaledTime<nextPulse)return;
            var arm=contact.Arm;
            if(!arm || !arm.Motor || arm.Motor.InSeabed!=arm.SeabedArea)return;
            nextPulse=Time.unscaledTime+Mathf.Max(repeatSeconds,pulseSeconds);
            PulseRequests++;
            bool accepted=SendRight(amplitude,pulseSeconds,out string details);
            TransportDetails=details;
            if(accepted){AcceptedPulses++;vibrating=true;}else RejectedPulses++;
            Status=(accepted?"RIGHT pulse accepted":"RIGHT pulse REJECTED")+" #"+AcceptedPulses+"/"+PulseRequests+" | "+ContactName;
        }
        public static bool SendRight(float strength,float seconds,out string details)
        {
            strength=Mathf.Clamp01(strength);seconds=Mathf.Clamp(seconds,.01f,.5f);
            string inputResult="no InputSystem right controller";
            // Live Preview registers XRControllerWithRumble. ExecuteCommand gives
            // a return code; SendImpulse() alone discards it.
            var device=XRController.rightHand;
            if(device!=null && device.added && device.enabled)
            {
                var command=SendHapticImpulseCommand.Create(0,strength,seconds);
                long code=device.ExecuteCommand(ref command);
                inputResult=device.displayName+" InputSystem code="+code;
                if(code>=0){details=inputResult;return true;}
            }
            var legacy=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool hasCaps=legacy.isValid && legacy.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse;
            // Some preview providers omit capability reporting; still attempt the
            // supported impulse call and report its actual result.
            if(legacy.isValid && legacy.SendHapticImpulse(0,strength,seconds))
            {details=legacy.name+" legacy accepted; caps="+hasCaps+"; "+inputResult;return true;}
            details=inputResult+"; legacy valid="+legacy.isValid+", impulseCaps="+hasCaps+", rejected";
            return false;
        }
        void Stop()
        {
            if(!vibrating)return;
            var device=XRController.rightHand;
            if(device!=null){var zero=SendHapticImpulseCommand.Create(0,0,.01f);device.ExecuteCommand(ref zero);}
            var legacy=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);if(legacy.isValid)legacy.StopHaptics();
            vibrating=false;
        }
        void OnDisable(){Stop();ContactActive=false;}
    }
}
