using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace UnderwaterBaseline
{
    // Clutched, normalized Cartesian master/slave interface. Independent of IK.
    public sealed class UnderwaterMasterSlaveMapper : MonoBehaviour
    {
        public enum Mode { Automatic, Position, Velocity }
        public Mode mode;
        public UnderwaterGraspTask task;
        public UnderwaterGraspBench sand;
        public Vector3 masterHalfRange = new Vector3(.16f,.12f,.16f);
        public Vector3 slavePositionRange = new Vector3(.12f,.09f,.12f);
        public Vector3 axisSigns = Vector3.one;
        public Vector3 deadZoneMetres = Vector3.one*.002f;
        public float maxSpeed=.18f,maxAcceleration=.65f,filterSeconds=.075f,positionResponseSeconds=.12f;
        public float enterInteractionDistance=.22f,exitInteractionDistance=.30f,transitionDwell=.2f;
        public bool recordTelemetry=true;
        public string Stage {get;private set;}="Position";
        public Vector3 RawDelta {get;private set;}
        public Vector3 Normalized {get;private set;}
        public Vector3 DesiredVelocity {get;private set;}
        public Vector3 Velocity {get;private set;}
        public Vector3 Target {get;private set;}
        public float ResistanceNewtons {get;private set;}
        public int TransitionCount {get;private set;}
        Vector3 anchor,slaveAnchor,filteredVelocity;
        Quaternion frame;
        bool position=true,ready;
        float dwell,logTime;
        StreamWriter writer;

        public void Begin(Vector3 master,Vector3 slave,Quaternion frozenTrackingRotation)
        {
            anchor=master;slaveAnchor=Target=slave;frame=frozenTrackingRotation;
            Velocity=filteredVelocity=Vector3.zero;RawDelta=Normalized=Vector3.zero;
            position=mode!=Mode.Velocity && (mode==Mode.Position || InteractionDistance()<exitInteractionDistance);
            Stage=position?"Position":"Velocity";ready=true;dwell=0;
        }
        float InteractionDistance()
        {
            if(!task || !task.arm || !task.arm.Contact || !task.arm.Contact.Hand)return float.PositiveInfinity;
            if(task.Held)return 0;
            Vector3 palm=task.PalmPoint;float distance=float.PositiveInfinity;
            if(task.targets!=null)foreach(var t in task.targets)if(t && t.Shape)distance=Mathf.Min(distance,Vector3.Distance(palm,t.Shape.ClosestPoint(palm)));
            if(sand && sand.sand)distance=Mathf.Min(distance,Vector3.Distance(palm,sand.sand.ClosestPoint(palm)));
            return distance;
        }
        public static float ContinuousDeadzone(float value,float threshold)=>Mathf.Sign(value)*Mathf.Max(0,Mathf.Abs(value)-Mathf.Max(0,threshold));
        public Vector3 Step(Vector3 master,Vector3 actual,float dt)
        {
            if(!ready)Begin(master,actual,Quaternion.identity);
            if(!float.IsFinite(dt)||dt<=0)return Target;
            dt=Mathf.Min(dt,.05f);
            bool wanted=mode==Mode.Position || mode==Mode.Automatic && InteractionDistance()<(position?exitInteractionDistance:enterInteractionDistance);
            if(wanted!=position)
            {
                dwell+=dt;
                if(dwell>=transitionDwell)
                {
                    // Rebase the position setpoint at the current command, preserving
                    // velocity through a transition. No coordinate/head-frame jump.
                    position=wanted;anchor=master;slaveAnchor=Target;dwell=0;TransitionCount++;
                }
            }
            else dwell=0;
            Stage=position?"Position":"Velocity";RawDelta=master-anchor;
            Vector3 input=Vector3.zero;
            for(int i=0;i<3;i++)input[i]=Mathf.Clamp(ContinuousDeadzone(RawDelta[i],deadZoneMetres[i])*axisSigns[i]/Mathf.Max(.005f,masterHalfRange[i]-deadZoneMetres[i]),-1,1);
            Normalized=input;
            Vector3 desired;
            if(position)
            {
                var goal=slaveAnchor+frame*Vector3.Scale(input,slavePositionRange);
                desired=(goal-Target)/Mathf.Max(.02f,positionResponseSeconds);
            }
            else desired=frame*Vector3.ClampMagnitude(input,1)*maxSpeed;
            desired=Vector3.ClampMagnitude(desired,Mathf.Max(.001f,maxSpeed));
            ResistanceNewtons=0;
            float force=0;
            if(sand && task && task.arm && task.arm.Contact && task.arm.Contact.Hand)
                desired=sand.ResistTool(task.arm.Contact.Hand,desired,out force); // mobility acts on commands because the mount is immovable
            ResistanceNewtons=force;
            DesiredVelocity=desired;
            filteredVelocity=Vector3.Lerp(filteredVelocity,desired,1-Mathf.Exp(-dt/Mathf.Max(.001f,filterSeconds)));
            Velocity=Vector3.MoveTowards(Velocity,filteredVelocity,Mathf.Max(.001f,maxAcceleration)*dt);
            Target+=Velocity*dt;
            // Bound tracking lead at limits; retain the rate-control centre so a
            // left/right reversal is not swallowed by repeatedly resetting it.
            Target=actual+Vector3.ClampMagnitude(Target-actual,.055f);
            return Target;
        }
        public void Reject(Vector3 master,Vector3 actual)
        {
            Target=actual;Velocity=filteredVelocity=Vector3.zero;
            if(position){anchor=master;slaveAnchor=actual;}
        }
        public void Trace(PicoArmPoseDriver arm,float dt)
        {
            if(!recordTelemetry || !Application.isPlaying || Time.unscaledTime<logTime)return;
            logTime=Time.unscaledTime+.05f;
            try
            {
                if(writer==null)
                {
                    string dir=Application.isEditor?"Logs/HybridReview":Path.Combine(Application.persistentDataPath,"HybridReview");Directory.CreateDirectory(dir);
                    writer=new StreamWriter(Path.Combine(dir,"mapping-"+(arm.SeabedArea?"seabed":"pool")+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv"));
                    writer.WriteLine("time,dt,stage,rawX,rawY,rawZ,normX,normY,normZ,desiredVX,desiredVY,desiredVZ,vX,vY,vZ,targetX,targetY,targetZ,actualX,actualY,actualZ,q0,q1,q2,q3,sigma,damping,contact,sandN");
                }
                var q=arm.CurrentJointAngles;
                writer.WriteLine(string.Join(",",new[]{Time.unscaledTime.ToString("F5",CultureInfo.InvariantCulture),dt.ToString("F5",CultureInfo.InvariantCulture),Stage,V(RawDelta),V(Normalized),V(DesiredVelocity),V(Velocity),V(Target),V(arm.Wrist.position),q.x.ToString("F3",CultureInfo.InvariantCulture),q.y.ToString("F3",CultureInfo.InvariantCulture),q.z.ToString("F3",CultureInfo.InvariantCulture),q.w.ToString("F3",CultureInfo.InvariantCulture),arm.MinimumSingularValue.ToString("F5",CultureInfo.InvariantCulture),arm.EffectiveDamping.ToString("F5",CultureInfo.InvariantCulture),arm.SurfaceContact?"1":"0",ResistanceNewtons.ToString("F4",CultureInfo.InvariantCulture)}));writer.Flush();
            }
            catch(IOException e){recordTelemetry=false;Debug.LogWarning("Teleop telemetry disabled: "+e.Message);}
        }
        static string V(Vector3 v)=>string.Format(CultureInfo.InvariantCulture,"{0:F6},{1:F6},{2:F6}",v.x,v.y,v.z);
        void OnDisable(){writer?.Dispose();writer=null;ready=false;}
    }
}
