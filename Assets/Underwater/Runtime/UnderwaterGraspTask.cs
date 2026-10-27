using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
namespace UnderwaterBaseline
{
    [DefaultExecutionOrder(280)]
    public sealed class UnderwaterGraspTask : MonoBehaviour
    {
        public PicoArmPoseDriver arm;public Wa100UdexRealArticulationMapper glove;
        public UnderwaterGraspTarget[] targets;public Transform deliveryZone;
        public float closeThreshold=.32f,openThreshold=.16f,stableSeconds=.22f;
        public float nearDistance=.065f;
        public UnderwaterGraspTarget Held {get;private set;}
        public UnderwaterGraspTarget Candidate {get;private set;}
        public string Status {get;private set;}="Open hand; approach a target";
        public float Closure {get;private set;}
        public bool LiftVerified {get;private set;}
        public int DeliveredCount=>targets.Count(t=>t && t.Delivered);
        public Vector3 PalmPoint=>arm.Contact.Hand.transform.TransformPoint(palmLocal);
        public Vector3 palmLocal;
        public Transform Thumb;public Transform[] Fingers;
        float stable;bool armed;UnderwaterGraspTarget stableCandidate;
        bool desktopClosed,desktopAssist;readonly Collider[] overlaps=new Collider[32];
        public bool GloveFresh {get;private set;}
        void LateUpdate()
        {
            if(!arm || !arm.Motor || arm.Motor.InSeabed!=arm.SeabedArea){if(Held)Release("region changed");return;}
            GloveFresh=glove && glove.Network && DateTime.TryParse(glove.Network.LastPacketUtc,out var packet) && (DateTime.UtcNow-packet.ToUniversalTime()).TotalSeconds<1;
            var key=Keyboard.current;
            if(GloveFresh)desktopAssist=false;
            else if(key!=null && key.f5Key.wasPressedThisFrame){desktopAssist=true;desktopClosed=!desktopClosed;}
            Closure=GloveFresh?ReadClosure():desktopAssist?(desktopClosed?1:0):0;
            // A stale glove must not create or maintain a grasp.
            Process(Closure,Time.unscaledDeltaTime,GloveFresh||desktopAssist,desktopAssist);
        }
        float ReadClosure()
        {
            float sum=0;int count=0;
            foreach(var m in glove.JointMappings)if(m!=null && m.Enabled && m.Joint && m.UpperLimitDegrees>m.LowerLimitDegrees)
            {sum+=Mathf.Clamp01((m.CurrentTarget-m.LowerLimitDegrees)/(m.UpperLimitDegrees-m.LowerLimitDegrees));count++;}
            return count>0?sum/count:0;
        }
        bool Touch(UnderwaterGraspTarget target,Transform link)
        {
            if(!link)return false;
            foreach(var c in link.GetComponentsInChildren<Collider>())if(c.enabled && !c.isTrigger &&
                Vector3.Distance(c.bounds.ClosestPoint(target.Shape.bounds.center),target.Shape.ClosestPoint(c.bounds.center))<nearDistance)return true;
            return false;
        }
        public void Process(float closure,float dt,bool fresh,bool desktopAssist=false)
        {
            Closure=closure;
            if(Held && !fresh){Release("glove data stale");return;}
            if(fresh && closure<=openThreshold){armed=true;stable=0;if(Held)Release("hand opened");}
            if(Held)
            {
                if(Held.transform.position.y-Held.InitialPosition.y>=Held.liftedHeight)LiftVerified=true;
                Status=LiftVerified?"HOLD / lifted: move to delivery marker; open to release":"HOLD: lift at least 0.12 m";return;
            }
            Candidate=null;float nearest=.4f;
            foreach(var target in targets)if(target && !target.Held && !target.Delivered)
            {
                float d=Vector3.Distance(PalmPoint,target.Shape.ClosestPoint(PalmPoint));
                if(d<nearest){nearest=d;Candidate=target;}
            }
            if(!Candidate){stable=0;Status="SEARCH: open hand, approach starfish / squid";return;}
            bool opposing=desktopAssist || Touch(Candidate,Thumb)&&Fingers.Any(f=>Touch(Candidate,f));
            bool close=nearest<nearDistance && closure>=closeThreshold && fresh && armed && opposing;
            if(stableCandidate!=Candidate){stable=0;stableCandidate=Candidate;}
            stable=close?stable+dt:0;
            Status=!armed?"OPEN hand before grasp":"APPROACH "+Candidate.targetId+" | palm "+nearest.ToString("F2")+" m | "+(opposing?"contacts ready":"need thumb + finger contact");
            if(stable>=stableSeconds)TryAttach(Candidate);
        }
        void TryAttach(UnderwaterGraspTarget target)
        {
            Held=target;LiftVerified=false;armed=false;target.Attach(arm.Wrist);
            foreach(var h in arm.GetComponentsInChildren<Collider>())if(h!=target.Shape && h.transform!=target.transform && !h.transform.IsChildOf(target.transform))Physics.IgnoreCollision(h,target.Shape,true);
            if(arm.WholeArmContact)arm.WholeArmContact.Rebuild();arm.Contact.Pulse();Log("attach",target,false);
        }
        public void Release(string reason)
        {
            if(!Held)return;var target=Held;bool success=reason=="hand opened" && LiftVerified && deliveryZone && Vector3.Distance(target.transform.position,deliveryZone.position)<.4f;
            foreach(var h in arm.GetComponentsInChildren<Collider>())if(h!=target.Shape && !h.transform.IsChildOf(target.transform))Physics.IgnoreCollision(h,target.Shape,false);
            target.Release();Held=null;if(success)target.MarkDelivered();
            if(arm.WholeArmContact)arm.WholeArmContact.Rebuild();Log("release: "+reason,target,success);
            Status=success?"DELIVERED: "+target.targetId:"RELEASED: "+reason;
        }
        [Serializable] class Entry{public string time,action,target;public bool delivered;public Vector3 position;public float closure;}
        void Log(string action,UnderwaterGraspTarget target,bool delivered)
        {
            string dir=Application.isEditor?"Logs/GraspTask":Path.Combine(Application.persistentDataPath,"GraspTask");Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir,"events.jsonl"),JsonUtility.ToJson(new Entry{time=DateTime.UtcNow.ToString("O"),action=action,target=target.targetId,delivered=delivered,position=target.transform.position,closure=Closure})+"\n");
        }
        void OnDisable(){if(Held)Release("task disabled");}
    }
}
