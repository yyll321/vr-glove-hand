using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnderwaterBaseline
{
    // A virtual position brake, not a passive/underactuated finger model.
    // Native joint limits bound compliance; no Transform teleporting is used.
    [DefaultExecutionOrder(200)]
    public sealed class Wa100JointPoseHold : MonoBehaviour
    {
        public Wa100UdexRealArticulationMapper mapper;
        public UnderwaterGraspBench sand;
        public Wa100SurfaceContact contact;
        public LayerMask blockedLayers=(1<<8)|(1<<10);
        public float stiffness=20000, damping=180,forceLimit=120;
        public float passiveComplianceDegrees=.6f,maximumCommandSpeed=55;
        public float maximumOpeningSpeed=200f;
        public float penetrationTolerance=.0005f;
        public int BlockedCommands {get;private set;}
        public float MaximumHoldError {get;private set;}
        public bool FreshCommand {get;private set;}
        public float RequestedClosure {get;private set;}
        public float AcceptedClosure {get;private set;}
        public float CommandClosure {get;private set;}
        [NonSerialized] public bool TestCommands;
        readonly List<State> joints=new List<State>();
        readonly Collider[] nearby=new Collider[96];
        sealed class State
        {
            public Wa100UdexRealArticulationMapper.JointMapping mapping;
            public ArticulationDrive original;
            public ArticulationDofLock originalLock;
            public Collider[] shapes;
            public float accepted;
        }
        void Start(){Initialize();}
        public void Initialize()
        {
            if(joints.Count>0)return;
            if(!mapper)mapper=GetComponent<Wa100UdexRealArticulationMapper>();
            if(!mapper || mapper.JointMappings==null)return;
            mapper.UseArticulationDriveLimits=false;
            mapper.ConfigureDriveStrength=false;
            foreach(var m in mapper.JointMappings)if(m!=null && m.Joint && m.Joint.dofCount>0)
            {
                var b=m.Joint;
                joints.Add(new State{mapping=m,original=b.xDrive,originalLock=b.twistLock,shapes=b.GetComponentsInChildren<Collider>(),accepted=Mathf.Clamp(b.jointPosition[0]*Mathf.Rad2Deg,m.LowerLimitDegrees,m.UpperLimitDegrees)});
                b.solverIterations=32;b.solverVelocityIterations=12;
                b.maxJointVelocity=Mathf.Max(3,Mathf.Max(maximumCommandSpeed,maximumOpeningSpeed)*Mathf.Deg2Rad*1.2f);
            }
        }
        void FixedUpdate(){Tick(Time.fixedDeltaTime);}
        public void Tick(float dt)
        {
            Initialize();if(dt<=0)return;
            FreshCommand=TestCommands || mapper && mapper.enabled && mapper.Network && DateTime.TryParse(mapper.Network.LastPacketUtc,out var packet) && (DateTime.UtcNow-packet.ToUniversalTime()).TotalSeconds<1;
            MaximumHoldError=0;float requestedSum=0,acceptedSum=0,commandSum=0;int count=0;
            foreach(var s in joints)
            {
                var m=s.mapping;var b=m.Joint;if(!b)continue;
                float requested=FreshCommand?Mathf.Clamp(m.CurrentTarget,m.LowerLimitDegrees,m.UpperLimitDegrees):s.accepted;
                float closingLimit=Mathf.Abs(m.LowerLimitDegrees)>Mathf.Abs(m.UpperLimitDegrees)?m.LowerLimitDegrees:m.UpperLimitDegrees;
                if(m.Enabled && Mathf.Abs(closingLimit)>.01f)
                {
                    requestedSum+=Mathf.Clamp01(requested/closingLimit);
                    commandSum+=Mathf.Clamp01((TestCommands?requested:FreshCommand?m.RequestedTarget:s.accepted)/closingLimit);count++;
                }
                bool opening=Mathf.Abs(requested)<Mathf.Abs(s.accepted);
                float speed=opening?maximumOpeningSpeed:maximumCommandSpeed;
                float step=Mathf.Clamp(requested-s.accepted,-speed*dt,speed*dt);
                if(sand)step*=sand.FingerMobility(b,step/dt);
                // Predict each small FK rotation against solids/targets before
                // moving the joint's allowed window. Sand is excluded explicitly.
                float actual=b.jointPosition[0]*Mathf.Rad2Deg;
                float next=s.accepted+step;
                if(Mathf.Abs(step)>.0001f && WouldWorsenContact(s,next-actual))
                {next=s.accepted;BlockedCommands++;if(contact)contact.Pulse();}
                // Keep command intent separate from the accepted physical pose.
                // Overwriting the mapper's smoothing state here loses close effort
                // at contact and can prevent a stable grasp despite enclosure.
                s.accepted=next;
                if(m.Enabled && Mathf.Abs(closingLimit)>.01f)acceptedSum+=Mathf.Clamp01(next/closingLimit);
                var drive=b.xDrive;
                drive.stiffness=stiffness;drive.damping=damping;drive.forceLimit=forceLimit;
                drive.lowerLimit=Mathf.Max(m.LowerLimitDegrees,next-passiveComplianceDegrees);
                drive.upperLimit=Mathf.Min(m.UpperLimitDegrees,next+passiveComplianceDegrees);
                drive.target=next;drive.targetVelocity=0;
                b.twistLock=ArticulationDofLock.LimitedMotion;b.xDrive=drive;
                MaximumHoldError=Mathf.Max(MaximumHoldError,Mathf.Abs(actual-next));
            }
            RequestedClosure=count>0?requestedSum/count:0;AcceptedClosure=count>0?acceptedSum/count:0;CommandClosure=count>0?commandSum/count:0;
        }
        bool WouldWorsenContact(State state,float deltaDegrees)
        {
            var body=state.mapping.Joint;
            Vector3 pivot=body.transform.TransformPoint(body.anchorPosition);
            Vector3 axis=body.transform.TransformDirection(body.anchorRotation*Vector3.right);
            // Faster servo commands still inspect the swept angular path in small
            // increments; speed increases must not jump through thin obstacles.
            int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Abs(deltaDegrees)/.4f));
            foreach(var c in state.shapes)if(c && c.enabled && !c.isTrigger)
            {
                int count=Physics.OverlapSphereNonAlloc(c.bounds.center,c.bounds.extents.magnitude+.04f,nearby,blockedLayers,QueryTriggerInteraction.Ignore);
                if(count==nearby.Length)return true;
                for(int i=0;i<count;i++)
                {
                    var other=nearby[i];
                    if(!other || other.transform.IsChildOf(transform) || sand && other==sand.sand || Physics.GetIgnoreCollision(c,other))continue;
                    Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,other,other.transform.position,other.transform.rotation,out _,out float before);
                    for(int sample=1;sample<=steps;sample++)
                    {
                        var turn=Quaternion.AngleAxis(deltaDegrees*sample/steps,axis);
                        Vector3 p=pivot+turn*(c.transform.position-pivot);Quaternion q=turn*c.transform.rotation;
                        if(Physics.ComputePenetration(c,p,q,other,other.transform.position,other.transform.rotation,out _,out float after) &&
                           after>Mathf.Max(penetrationTolerance,before+.0001f))return true;
                    }
                }
            }
            return false;
        }
        public float Accepted(ArticulationBody b)
        {foreach(var s in joints)if(s.mapping.Joint==b)return s.accepted;return 0;}
        void OnDisable()
        {
            foreach(var s in joints)if(s.mapping.Joint){s.mapping.Joint.xDrive=s.original;s.mapping.Joint.twistLock=s.originalLock;}
            joints.Clear();
        }
    }
}
