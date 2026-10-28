using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterBenchChecks
    {
        const string Key="UnderwaterBenchChecks";
        static UnderwaterBenchChecks(){EditorApplication.playModeStateChanged+=Mode;}
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");
            UnderwaterPreviewCompatibility.Verify();SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        public static void RunControls(){SessionState.SetBool(Key+"Controls",true);Run();}
        static void Mode(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false))
            {SessionState.SetBool(Key,false);var probe=new GameObject("Bench automated test runner").AddComponent<UnderwaterBenchPlayProbe>();probe.controlsOnly=SessionState.GetBool(Key+"Controls",false);SessionState.SetBool(Key+"Controls",false);}
        }
    }
    public sealed class UnderwaterBenchPlayProbe:MonoBehaviour
    {
        public bool controlsOnly;
        [Serializable]public class Motion
        {
            public string area,direction,mode;public Vector3 requested,actual;
            public float error,maxJointStep,maxJointAcceleration,jitterRms;
            public float totalJointTravel,maximumOffAxisDeviation;
        }
        [Serializable]public class Attempt
        {
            public string area,target,method="Synthetic articulation drives + real opposing digit geometry; no desktop-assist bypass";
            public float closure,thumbGap,fingerGap,pinchGap,lift;public bool attached,lifted;
            public Vector3 targetPosition,palm;public string armStatus;public bool contact;
            public float attachClosure,sandPenetrationAtAttach,supportClearanceAtAttach;
        }
        [Serializable]public class Report
        {
            public string time;public bool passed;public string[] checks,errors;
            public Motion[] motion;public Attempt[] attempts;
            public bool graspFeasibilityPassed;
            public string scope="Unity Editor local simulation. Synthetic XR poses and joint drives, not live glove or worn PICO verification.";
        }
        readonly List<string> checks=new List<string>(),errors=new List<string>();
        readonly List<Motion> motions=new List<Motion>();readonly List<Attempt> attempts=new List<Attempt>();
        void Check(bool ok,string text){if(ok)checks.Add(text);else errors.Add(text);}
        void Log(string text,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(text);}
        IEnumerator Start()
        {
            Directory.CreateDirectory("Logs/TeleopReview");Application.logMessageReceived+=Log;
            yield return new WaitForSeconds(2);
            var motor=Object.FindObjectOfType<UnderwaterMotor>();motor.enabled=false;
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>().OrderBy(t=>t.arm.SeabedArea))
            {
                task.enabled=false;var arm=task.arm;arm.enabled=false;arm.ResolveBindings();
                task.glove.enabled=false;
                var rotations=arm.Joints.Select(j=>j.localRotation).ToArray();
                foreach(var bench in task.GetComponentsInChildren<UnderwaterGraspBench>())
                {
                    bench.ConfigureFiltering();Check(task.targets.Length==2 && task.targets.Count(t=>t.name.Contains("Starfish"))==1 && task.targets.Count(t=>t.name.Contains("Squid"))==1,arm.name+": exactly one starfish + one squid");
                    var colliders=arm.Contact.Hand.GetComponentsInChildren<Collider>().Where(c=>c.enabled).ToArray();
                    Check(colliders.All(c=>Physics.GetIgnoreCollision(c,bench.sand)) && colliders.All(c=>!Physics.GetIgnoreCollision(c,bench.support)),arm.name+": explicit sand ignore pairs, solid support collision pairs retained");
                    arm.Contact.BeginMotion();var bounds=arm.Contact.Hand.GetComponentsInChildren<Renderer>();float bottom=bounds.Min(r=>r.bounds.min.y);
                    float sandTop=bench.sand.bounds.max.y,supportTop=bench.support.bounds.max.y;
                    var throughSand=arm.Wrist.position+Vector3.up*(sandTop-.025f-bottom);
                    Check(!arm.Contact.Blocked(throughSand,arm.Wrist.rotation),arm.name+": hand enters sand by 25 mm without query obstruction");
                    Check(arm.Contact.Blocked(throughSand-Vector3.up*(sandTop-supportTop),arm.Wrist.rotation),arm.name+": same hand trajectory blocked at solid support");
                    Check(arm.Contact.SweepBlocked(arm.Wrist.position,arm.Wrist.rotation,throughSand-Vector3.up*.2f),arm.name+": swept contact cannot tunnel through bench support");
                }
                TestMotion(arm,rotations);
                ExtraControlChecks(arm,rotations);
                Restore(arm,rotations);
                if(controlsOnly)
                {
                    // Reuse the pre-existing physical finger-contact regression,
                    // including its original drive strength and 5 mm penetration bound.
                    var type=typeof(UnderwaterMotor).Assembly.GetType("UnderwaterBaseline.UnderwaterArmChecks");
                    var test=type.GetMethod("FingerContact",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                    yield return (IEnumerator)test.Invoke(null,new object[]{arm,(Action<bool,string>)Check});
                    continue;
                }
                // Sample real closed-finger geometry before choosing grasp depth.
                Vector3 closedPinchLocal=Vector3.zero;
                foreach(float fraction in new[]{0f,.4f,.65f})
                {
                    Drive(task,fraction);for(int f=0;f<45;f++)yield return new WaitForFixedUpdate();
                    File.AppendAllText("Logs/TeleopReview/finger-poses.txt",arm.name+" closure "+fraction+" palm "+task.PalmPoint.ToString("F4")+"\n"+
                        string.Join("\n",new[]{task.Thumb}.Concat(task.Fingers).Select(t=>t.name+" "+string.Join(";",t.GetComponentsInChildren<Collider>().Where(c=>c.enabled).Select(c=>c.bounds.ToString()))))+"\n");
                    if(fraction>.6f)closedPinchLocal=Quaternion.Inverse(arm.Wrist.rotation)*(Pinch(task)-arm.Wrist.position);
                }
                foreach(var target in task.targets)
                {
                    // First round: use the actual bench placements and approach through
                    // the production controller path. Targets are not teleported to palm.
                    Restore(arm,rotations);Drive(task,0);for(int f=0;f<35;f++)yield return new WaitForFixedUpdate();
                    var origin=new Vector3(.2f,1.1f,.4f);arm.ApplyControllerPose(origin,Quaternion.identity,false,1f/90);arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);
                    Vector3 command=origin;
                    float attachClosure=0,attachDepth=-1,attachClearance=-1;
                    Action rememberAttach=()=>
                    {
                        if(attachDepth>=0 || task.Held!=target)return;
                        var bench=task.GetComponentInChildren<UnderwaterGraspBench>();
                        float minimum=arm.Contact.Hand.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                        attachClosure=task.ReadClosure();attachDepth=Mathf.Max(0,bench.sand.bounds.max.y-minimum);attachClearance=minimum-bench.support.bounds.max.y;
                    };
                    for(int f=1;f<=220;f++)
                    {
                        var predicted=arm.Wrist.position+arm.Wrist.rotation*closedPinchLocal;
                        var error=target.Shape.bounds.center-predicted;
                        command+=arm.TrackingOrigin.InverseTransformDirection(Vector3.ClampMagnitude(error*1.6f,.10f))/arm.PositionScale/90;
                        arm.ApplyControllerPose(command,Quaternion.identity,true,1f/90);
                        task.Process(0,1f/90,true,false);yield return new WaitForFixedUpdate();
                    }
                    for(int f=0;f<90;f++)
                    {
                        Drive(task,Mathf.Min(.65f,f/90f));
                        if(!task.Held)
                        {
                            var error=target.Shape.bounds.center-(arm.Wrist.position+arm.Wrist.rotation*closedPinchLocal);
                            command+=arm.TrackingOrigin.InverseTransformDirection(Vector3.ClampMagnitude(error,.04f))/arm.PositionScale/90;
                            arm.ApplyControllerPose(command,Quaternion.identity,true,1f/90);
                        }
                        task.Process(task.ReadClosure(),Time.fixedDeltaTime,true,false);rememberAttach();yield return new WaitForFixedUpdate();
                    }
                    // Flat objects need a second, slow under-edge alignment after
                    // flexion. Use actual digit geometry, not an assumed palm offset.
                    for(int f=0;f<180 && !task.Held;f++)
                    {
                        var error=target.Shape.bounds.center-Pinch(task);
                        command+=arm.TrackingOrigin.InverseTransformDirection(Vector3.ClampMagnitude(error*1.4f,.05f))/arm.PositionScale/90;
                        arm.ApplyControllerPose(command,Quaternion.identity,true,1f/90);
                        task.Process(task.ReadClosure(),Time.fixedDeltaTime,true,false);rememberAttach();yield return new WaitForFixedUpdate();
                    }
                    var attempt=new Attempt{area=arm.name,target=target.name,closure=task.ReadClosure(),thumbGap=task.ThumbGap,fingerGap=task.FingerGap,pinchGap=task.PinchGap,attached=task.Held==target,targetPosition=target.transform.position,palm=task.PalmPoint,attachClosure=attachClosure,sandPenetrationAtAttach=attachDepth,supportClearanceAtAttach=attachClearance};
                    float oldY=target.transform.position.y;
                    if(task.Held==target)
                    {
                        arm.ApplyControllerPose(command,Quaternion.identity,false,1f/90);arm.ApplyControllerPose(command,Quaternion.identity,true,1f/90);
                        Vector3 liftedCommand=command;
                        for(int f=1;f<=160;f++)
                        {
                            arm.ApplyControllerPose(liftedCommand+arm.TrackingOrigin.InverseTransformDirection(Vector3.up*.22f)*Mathf.Min(1,f/100f),Quaternion.identity,true,1f/90);
                            task.Process(.65f,Time.fixedDeltaTime,true,false);yield return new WaitForFixedUpdate();
                        }
                        attempt.lift=target.transform.position.y-oldY;attempt.lifted=task.LiftVerified;
                    }
                    attempt.armStatus=arm.Status;attempt.contact=arm.SurfaceContact;
                    attempts.Add(attempt);if(task.Held)task.Release("test cleanup");
                }
                Restore(arm,rotations);
            }
            var console=Object.FindObjectOfType<UnderwaterTaskConsole>();console.SelectView(0);console.Capture();
            Save(console.Preview,"Logs/TeleopReview/bench-close.png");console.SelectView(1);console.Capture();Save(console.Preview,"Logs/TeleopReview/bench-arm.png");
            var report=new Report{time=DateTime.Now.ToString("O"),passed=errors.Count==0,graspFeasibilityPassed=attempts.Count==4 && attempts.All(a=>a.lifted),checks=checks.ToArray(),errors=errors.ToArray(),motion=motions.ToArray(),attempts=attempts.ToArray()};
            File.WriteAllText(controlsOnly?"Logs/TeleopReview/control-tests.json":"Logs/TeleopReview/bench-tests.json",JsonUtility.ToJson(report,true));Application.logMessageReceived-=Log;EditorApplication.isPlaying=false;
        }
        void TestMotion(PicoArmPoseDriver arm,Quaternion[] rotations)
        {
            bool protect=arm.PreventSurfacePenetration;arm.PreventSurfacePenetration=false;
            foreach(bool improved in new[]{false,true})foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down})
            {
                Restore(arm,rotations);arm.UseCalibratedBaseYaw=arm.SmoothTeleoperation=improved;
                var origin=new Vector3(.2f,1.1f,.4f);var start=arm.Wrist.position;var delta=direction*.08f;
                arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);
                float maxStep=0,maxAcceleration=0,travel=0,offAxis=0;var velocity=new float[4];
                for(int f=1;f<=300;f++)
                {
                    var before=arm.Joints.Select(j=>j.localRotation).ToArray();
                    arm.ApplyControllerPose(origin+arm.TrackingOrigin.InverseTransformDirection(delta)*Mathf.Min(1,f/120f),Quaternion.identity,true,1f/90);
                    maxStep=Mathf.Max(maxStep,arm.MaximumAppliedJointStep);
                    for(int j=0;j<4;j++)
                    {
                        var dq=Quaternion.Inverse(before[j])*arm.Joints[j].localRotation;
                        float v=Mathf.DeltaAngle(0,2*Mathf.Atan2(Vector3.Dot(new Vector3(dq.x,dq.y,dq.z),arm.JointAxisLocal(j)),dq.w)*Mathf.Rad2Deg)*90;
                        maxAcceleration=Mathf.Max(maxAcceleration,Mathf.Abs(v-velocity[j])*90);velocity[j]=v;
                        travel+=Mathf.Abs(v)/90;
                    }
                    offAxis=Mathf.Max(offAxis,Vector3.ProjectOnPlane(arm.Wrist.position-start,direction).magnitude);
                }
                var actual=arm.Wrist.position-start;float error=Vector3.Distance(actual,delta);
                motions.Add(new Motion{area=arm.name,direction=direction.ToString(),mode=improved?"improved":"legacy",requested=delta,actual=actual,error=error,maxJointStep=maxStep,maxJointAcceleration=maxAcceleration,totalJointTravel=travel,maximumOffAxisDeviation=offAxis});
                if(improved){Check(error<.012f,arm.name+": improved direction "+direction+" error="+error.ToString("F4"));Check(maxStep<=arm.DegreesPerSecond/90+.02f && maxAcceleration<310,arm.name+": joint speed and acceleration limits "+direction);}
            }
            arm.UseCalibratedBaseYaw=arm.SmoothTeleoperation=true;arm.PreventSurfacePenetration=protect;
        }
        void ExtraControlChecks(PicoArmPoseDriver arm,Quaternion[] rotations)
        {
            bool protect=arm.PreventSurfacePenetration;arm.PreventSurfacePenetration=false;
            Vector3 origin=new Vector3(.2f,1.1f,.4f);
            try
            {
                Restore(arm,rotations);
                Check(Vector3.Dot(arm.Joints[0].TransformDirection(arm.JointAxisLocal(0)),Vector3.up)>.999f,arm.name+": calibrated base axis is vertical");
                for(int j=0;j<3;j++)
                {
                    var t=arm.Joints[j];var before=arm.Wrist.position;var q=t.localRotation;
                    t.localRotation=q*Quaternion.AngleAxis(.25f,arm.JointAxisLocal(j));
                    var difference=(arm.Wrist.position-before)/(.25f*Mathf.Deg2Rad);t.localRotation=q;
                    var analytic=Vector3.Cross(t.TransformDirection(arm.JointAxisLocal(j)),before-t.position);
                    Check(Vector3.Distance(difference,analytic)<.006f,arm.name+": calibrated Jacobian agrees with actual FK joint "+j);
                }
                // Rotate the XR origin while keeping the commanded world direction.
                var originalRotation=arm.TrackingOrigin.rotation;arm.TrackingOrigin.rotation=Quaternion.Euler(0,90,0)*originalRotation;
                var start=arm.Wrist.position;arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);
                for(int f=1;f<=300;f++)arm.ApplyControllerPose(origin+Quaternion.Inverse(arm.TrackingOrigin.rotation)*(Vector3.right*.06f)*Mathf.Min(1,f/120f),Quaternion.identity,true,1f/90);
                Check(Vector3.Distance(arm.Wrist.position-start,Vector3.right*.06f)<.012f,arm.name+": rotated tracking frame preserves world-right mapping");
                arm.TrackingOrigin.rotation=originalRotation;
                foreach(bool improved in new[]{false,true})
                {
                    Restore(arm,rotations);arm.UseCalibratedBaseYaw=arm.SmoothTeleoperation=improved;start=arm.Wrist.position;arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);
                    float sum=0;
                    for(int f=1;f<=450;f++)
                    {arm.ApplyControllerPose(origin+Vector3.right*(.004f*Mathf.Sin(f*2*Mathf.PI*17/90)),Quaternion.identity,true,1f/90);if(f>90)sum+=(arm.Wrist.position-start).sqrMagnitude;}
                    float rms=Mathf.Sqrt(sum/360);motions.Add(new Motion{area=arm.name,direction="stationary 4 mm 17 Hz noise",mode=improved?"improved":"legacy",jitterRms=rms});
                    if(improved)Check(rms<.0007f,arm.name+": stationary jitter below 0.7 mm RMS");
                }
                arm.UseCalibratedBaseYaw=arm.SmoothTeleoperation=true;Restore(arm,rotations);
                var limitsLow=arm.MinimumAngles;var limitsHigh=arm.MaximumAngles;
                arm.MinimumAngles=new Vector4(-3,-170,-170,-170);arm.MaximumAngles=new Vector4(3,170,170,170);
                arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);
                bool limited=true;
                for(int f=1;f<=450;f++)
                {
                    arm.ApplyControllerPose(origin+Vector3.right*Mathf.Min(.3f,f*.002f),Quaternion.identity,true,1f/90);
                    var q=Quaternion.Inverse(rotations[0])*arm.Joints[0].localRotation;
                    float angle=Mathf.DeltaAngle(0,2*Mathf.Atan2(Vector3.Dot(new Vector3(q.x,q.y,q.z),arm.JointAxisLocal(0)),q.w)*Mathf.Rad2Deg);
                    limited&=Mathf.Abs(angle)<3.02f && float.IsFinite(arm.PositionError) && arm.MaximumAppliedJointStep<1.02f;
                }
                Check(limited,arm.name+": tight joint limit and unreachable request stay finite and rate-limited");
                arm.MinimumAngles=limitsLow;arm.MaximumAngles=limitsHigh;
                var hold=arm.Joints.Select(j=>j.localRotation).ToArray();arm.ApplyControllerPose(Vector3.one,Quaternion.Euler(50,30,10),false,1f/90);
                Check(arm.Joints.Select((j,i)=>Quaternion.Angle(j.localRotation,hold[i])).All(v=>v<.02f),arm.name+": release holds joints");
                arm.ApplyControllerPose(new Vector3(float.NaN,0,0),Quaternion.identity,true,1f/90);
                Check(arm.Joints.Select((j,i)=>Quaternion.Angle(j.localRotation,hold[i])).All(v=>v<.02f),arm.name+": invalid tracking holds joints");
                arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);arm.ApplyControllerPose(origin+Vector3.one,Quaternion.identity,true,1f/90);
                Check(arm.Joints.Select((j,i)=>Quaternion.Angle(j.localRotation,hold[i])).All(v=>v<.02f),arm.name+": tracking discontinuity reanchors without jump");
                var eigen=Matrix4x4.identity;eigen.m00=.0004f;eigen.m11=.04f;
                Check(Mathf.Abs(UnderwaterTeleopFilter.SmallestSingularValue(eigen)-.02f)<.0001f,arm.name+": singular-value estimator matches known matrix");
            }
            finally{arm.UseCalibratedBaseYaw=arm.SmoothTeleoperation=true;arm.PreventSurfacePenetration=protect;Restore(arm,rotations);}
        }
        static void Restore(PicoArmPoseDriver arm,Quaternion[] rotations)
        {for(int i=0;i<4;i++)arm.Joints[i].localRotation=rotations[i];arm.Contact.Hand.SyncMountedRoot();Physics.SyncTransforms();arm.ApplyControllerPose(Vector3.zero,Quaternion.identity,false,1f/90);}
        static void Drive(UnderwaterGraspTask task,float fraction)
        {
            foreach(var m in task.glove.JointMappings)if(m.Joint)
            {
                float limit=Mathf.Abs(m.LowerLimitDegrees)>Mathf.Abs(m.UpperLimitDegrees)?m.LowerLimitDegrees:m.UpperLimitDegrees;
                m.CurrentTarget=m.CountsForClosure?limit*fraction:0;var d=Wa100UdexRealArticulationMapper.GetDrive(m);d.target=m.CurrentTarget;Wa100UdexRealArticulationMapper.SetDrive(m,d);
            }
        }
        static Vector3 Pinch(UnderwaterGraspTask task)
        {
            Vector3 Centre(Transform t)=>t.GetComponentsInChildren<Collider>().First(c=>c.enabled).bounds.center;
            return (Centre(task.Thumb)+Centre(task.Fingers[0]))*.5f;
        }
        static void Save(RenderTexture target,string path)
        {var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());RenderTexture.active=previous;Object.Destroy(texture);}
    }
}
