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
    public static class UnderwaterHybridChecks
    {
        const string Key="UnderwaterHybridChecks";
        static UnderwaterHybridChecks(){EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)){SessionState.SetBool(Key,false);new GameObject("Hybrid verification runner").AddComponent<UnderwaterHybridProbe>();}};}
        [MenuItem("Tools/Underwater/Research/27 Verify Hybrid Teleoperation and Sand")]
        public static void Run(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");UnderwaterPreviewCompatibility.Verify();SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;}
    }
    public sealed class UnderwaterHybridProbe:MonoBehaviour
    {
        [Serializable]public class AxisResult{public string area,axis;public Vector3 requested,raw,mapped,actual,joints;public float joint3,error,sigma,damping,maximumReverseStep;}
        [Serializable]public class Attempt{public string area,target;public bool attached,lifted,envelope,released,landed,handPairsRestored;public float lift,thumbGap,fingerGap,insertionDepth,supportClearance,closure,drop,releaseSeconds;public Vector3 palm,pinch,targetCentre;public string result;}
        [Serializable]public class Report{public string time,scope="Editor synthetic XR poses and articulation commands; no F5. Worn PICO/glove feel not measured.";public bool passed,graspPassed;public string[] checks,errors;public AxisResult[] axes;public Attempt[] grasps;}
        readonly List<string> checks=new List<string>(),errors=new List<string>();
        readonly List<AxisResult> axes=new List<AxisResult>();readonly List<Attempt> attempts=new List<Attempt>();
        void Check(bool ok,string text){(ok?checks:errors).Add(text);}
        void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error)errors.Add(message);}
        IEnumerator Start()
        {
            Directory.CreateDirectory("Logs/HybridReview");Application.logMessageReceived+=Log;
            yield return new WaitForSeconds(2);
            MappingMath();
            var motor=Object.FindObjectOfType<UnderwaterMotor>();motor.enabled=false;
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>().OrderBy(t=>t.arm.SeabedArea))
            {
                task.enabled=false;task.glove.enabled=false;var arm=task.arm;arm.enabled=false;arm.ResolveBindings();
                foreach(var legacy in arm.PreviousControllers)if(legacy)legacy.enabled=false;
                var hold=task.glove.GetComponent<Wa100JointPoseHold>();hold.TestCommands=true;
                var saved=arm.Joints.Select(j=>j.localRotation).ToArray();
                var bench=task.GetComponentInChildren<UnderwaterGraspBench>();bench.ConfigureFiltering();
                yield return UnderwaterPrecisionReleaseChecks.Verify(task,Check);
                Check(task.targets.Length==2 && task.targets.Count(t=>t.name.Contains("Starfish"))==1 && task.targets.Count(t=>t.name.Contains("Squid"))==1,arm.name+": one starfish and one squid");
                var star=task.targets.First(t=>t.name.Contains("Starfish"));
                var mesh=star.GetComponentInChildren<MeshFilter>();
                Check(Mathf.Abs(mesh.sharedMesh.bounds.size.z-.075f)<.0001f && Mathf.Abs(star.Shape.size.y-.078f)<.0001f,arm.name+": starfish local Z 75 mm / upright collider 78 mm");
                var colliders=arm.Contact.Hand.GetComponentsInChildren<Collider>().Where(c=>c.enabled).ToArray();
                Check(colliders.All(c=>Physics.GetIgnoreCollision(c,bench.sand) && !Physics.GetIgnoreCollision(c,bench.support)),arm.name+": sand collision ignored / solid support not ignored");
                Check(bench.Resistance(.06f,.002f,.04f,false)>bench.Resistance(.02f,.002f,.04f,false) && bench.Resistance(.06f,.004f,.04f,false)>bench.Resistance(.06f,.002f,.04f,false),arm.name+": resistance increases with depth and area");
                var sample=new Bounds(new Vector3(bench.sand.bounds.center.x,bench.SurfaceHeight-.025f,bench.sand.bounds.center.z),new Vector3(.04f,.04f,.04f));
                float resistance=bench.Sample(sample,Vector3.down*.05f,out float depth);
                Check(resistance>0 && depth>.04f,arm.name+": insertion has computed nonzero resistance "+resistance.ToString("F3")+" N");
                TestAxes(arm,saved);
                Restore(arm,saved);
                yield return HoldAndBlock(task,hold);
                Restore(arm,saved);
                foreach(var target in task.targets)
                {
                    yield return Grasp(task,target,hold,bench,saved);
                    Restore(arm,saved);
                }
                Drive(task,0);for(int f=0;f<45;f++){Drive(task,0);yield return new WaitForFixedUpdate();}
                arm.Contact.BeginMotion();
                float minimum=arm.Contact.Hand.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                var inSand=arm.Wrist.position+Vector3.up*(bench.SurfaceHeight-.025f-minimum);
                Check(!arm.Contact.Blocked(inSand,arm.Wrist.rotation),arm.name+": hand can enter sand 25 mm");
                Check(arm.Contact.Blocked(inSand-Vector3.up*(bench.SurfaceHeight-bench.support.bounds.max.y),arm.Wrist.rotation),arm.name+": solid support still blocks same trajectory");
                Check(arm.Contact.SweepBlocked(arm.Wrist.position,arm.Wrist.rotation,inSand-Vector3.up*.2f),arm.name+": original solid sweep retained");
            }
            var console=Object.FindObjectOfType<UnderwaterTaskConsole>();console.SelectView(0);console.Capture();Save(console.Preview,"Logs/HybridReview/bench-close.png");console.SelectView(1);console.Capture();Save(console.Preview,"Logs/HybridReview/bench-arm.png");
            Check(attempts.Count==4 && attempts.All(a=>a.attached && a.lifted && a.envelope && a.released && a.landed && a.handPairsRestored),"All four real envelope grasps lift, open, drop and restore collisions");
            var report=new Report{time=DateTime.Now.ToString("O"),passed=errors.Count==0,graspPassed=attempts.Count==4 && attempts.All(a=>a.attached && a.lifted && a.envelope),checks=checks.ToArray(),errors=errors.ToArray(),axes=axes.ToArray(),grasps=attempts.ToArray()};
            File.WriteAllText("Logs/HybridReview/results.json",JsonUtility.ToJson(report,true));Application.logMessageReceived-=Log;EditorApplication.isPlaying=false;
        }
        void MappingMath()
        {
            Check(UnderwaterMasterSlaveMapper.ContinuousDeadzone(.001f,.002f)==0 && Mathf.Abs(UnderwaterMasterSlaveMapper.ContinuousDeadzone(.00201f,.002f)-.00001f)<1e-7f,"Continuous deadzone: no motion inside 2 mm; 10 um continuous output immediately outside");
            var go=new GameObject("Isolated mapping fixture");var map=go.AddComponent<UnderwaterMasterSlaveMapper>();map.mode=UnderwaterMasterSlaveMapper.Mode.Velocity;map.recordTelemetry=false;
            map.Begin(Vector3.zero,Vector3.zero,Quaternion.identity);float worstSpeed=0,worstAccel=0;Vector3 actual=Vector3.zero,previous=Vector3.zero;
            foreach(float dt in new[]{1f/72,1f/90,1f/120})for(int i=0;i<180;i++)
            {actual=map.Step(Vector3.right*.14f,actual,dt);worstSpeed=Mathf.Max(worstSpeed,map.Velocity.magnitude);worstAccel=Mathf.Max(worstAccel,(map.Velocity-previous).magnitude/dt);previous=map.Velocity;}
            Check(worstSpeed<=map.maxSpeed+.0001f && worstAccel<=map.maxAcceleration+.001f,"72/90/120 Hz speed and acceleration bounded: "+worstSpeed.ToString("F4")+" m/s, "+worstAccel.ToString("F4")+" m/s2");
            var before=actual;map.mode=UnderwaterMasterSlaveMapper.Mode.Position;
            for(int i=0;i<25;i++)actual=map.Step(Vector3.right*.14f,actual,1f/90);
            Check(map.TransitionCount==1 && Vector3.Distance(actual,before)<.05f,"Rate-to-position transition rebases command with bounded displacement");
            map.Begin(Vector3.zero,Vector3.zero,Quaternion.Euler(0,90,0));actual=Vector3.zero;map.mode=UnderwaterMasterSlaveMapper.Mode.Position;
            for(int f=0;f<300;f++)actual=map.Step(Vector3.right*.08f,actual,1f/90);
            Check(actual.z<-.05f && Mathf.Abs(actual.x)<.002f,"Frozen tracking rotation maps raw right axis consistently after 90 degree yaw");
            map.mode=UnderwaterMasterSlaveMapper.Mode.Position;
            float previousDistance=0;
            foreach(float raw in new[]{.002f,.0021f,.012f,.042f,.082f})
            {
                actual=Vector3.zero;map.Begin(Vector3.zero,actual,Quaternion.identity);
                for(int f=0;f<300;f++)actual=map.Step(Vector3.right*raw,actual,1f/90);
                float expected=Mathf.Max(0,raw-map.deadZoneMetres.x)/(map.masterHalfRange.x-map.deadZoneMetres.x)*map.slavePositionRange.x;
                Check(actual.x>=previousDistance-.00001f && Mathf.Abs(actual.x-expected)<.0001f,"Right-axis mapping linear after deadzone: raw="+raw.ToString("F4")+" m output="+actual.x.ToString("F5")+" m");previousDistance=actual.x;
            }
            map.Begin(Vector3.zero,Vector3.zero,Quaternion.identity);actual=Vector3.zero;float sum=0;
            for(int f=0;f<450;f++)
            {actual=map.Step(Vector3.right*(.004f*Mathf.Sin(f*2*Mathf.PI*17/90)),actual,1f/90);if(f>=90)sum+=actual.sqrMagnitude;}
            float rms=Mathf.Sqrt(sum/360);Check(rms<.0007f,"Position mapping attenuates 4 mm / 17 Hz stationary jitter: RMS="+rms.ToString("F6")+" m");
            map.Begin(Vector3.zero,Vector3.zero,Quaternion.identity);actual=Vector3.zero;
            Vector3 command=new Vector3(.05f,-.05f,0);
            for(int i=0;i<20;i++)actual=map.Step(command,actual,1f/90);
            map.Reject(command,Vector3.zero,Vector3.up);
            var next=map.Step(command,Vector3.zero,1f/90);
            Check(next.x>0 && next.y>=-.000001f,"Contact discards inward normal motion while preserving tangential adjustment");
            Object.Destroy(go);
        }
        void TestAxes(PicoArmPoseDriver arm,Quaternion[] saved)
        {
            var map=arm.MasterSlave;var mode=map.mode;var bench=map.sand;map.sand=null;map.mode=UnderwaterMasterSlaveMapper.Mode.Position;
            bool protect=arm.PreventSurfacePenetration;arm.PreventSurfacePenetration=false;
            for(int axis=0;axis<3;axis++)foreach(int sign in new[]{-1,1})
            {
                Restore(arm,saved);
                // Radial translation is tested away from the imported near-straight
                // singular posture; +/- Z cannot both be reached at full extension.
                if(axis==2){arm.Joints[1].localRotation=saved[1]*Quaternion.AngleAxis(-12,arm.JointAxisLocal(1));arm.Joints[2].localRotation=saved[2]*Quaternion.AngleAxis(42,arm.JointAxisLocal(2));}
                var origin=new Vector3(.2f,1.1f,.4f);var start=arm.Wrist.position;
                Vector3 world=Vector3.zero;world[axis]=sign*Mathf.Min(.06f,map.slavePositionRange[axis]*.7f);
                Vector3 input=Quaternion.Inverse(arm.TrackingOrigin.rotation)*world;
                for(int j=0;j<3;j++)input[j]=input[j]/map.slavePositionRange[j]*(map.masterHalfRange[j]-map.deadZoneMetres[j])+(Mathf.Abs(input[j])>1e-6f?Mathf.Sign(input[j])*map.deadZoneMetres[j]:0);
                arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);
                Vector3 previous=arm.Wrist.position;float reverse=0;
                using(var csv=new StreamWriter("Logs/HybridReview/axis-"+(arm.SeabedArea?"sea":"pool")+"-"+axis+"-"+sign+".csv"))
                {
                    csv.WriteLine("sample,rawX,rawY,rawZ,targetX,targetY,targetZ,actualX,actualY,actualZ,q0,q1,q2,q3,sigma");
                    for(int f=1;f<=360;f++)
                    {
                        var raw=input*Mathf.Min(1,f/120f);arm.ApplyControllerPose(origin+raw,Quaternion.identity,true,1f/90);
                        reverse=Mathf.Max(reverse,-Vector3.Dot(arm.Wrist.position-previous,world.normalized));previous=arm.Wrist.position;
                        if(f%9==0){var angles=arm.CurrentJointAngles;csv.WriteLine(f+","+V(raw)+","+V(map.Target-start)+","+V(arm.Wrist.position-start)+","+V(new Vector3(angles.x,angles.y,angles.z))+","+angles.w.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+arm.MinimumSingularValue.ToString(System.Globalization.CultureInfo.InvariantCulture));}
                    }
                }
                var actual=arm.Wrist.position-start;var q=arm.CurrentJointAngles;
                axes.Add(new AxisResult{area=arm.name,axis=(axis==0?"X":axis==1?"Y":"Z")+(sign<0?"-":"+"),requested=world,raw=input,mapped=map.Target-start,actual=actual,joints=new Vector3(q.x,q.y,q.z),joint3=q.w,error=Vector3.Distance(actual,world),sigma=arm.MinimumSingularValue,damping=arm.EffectiveDamping,maximumReverseStep=reverse});
                Check(Vector3.Distance(actual,world)<.012f,arm.name+": isolated "+axes.Last().axis+" error="+Vector3.Distance(actual,world).ToString("F4")+" m");
                if(axis==0)Check(reverse<.0003f,arm.name+": left/right ramp has no reverse step above 0.3 mm ("+reverse.ToString("F6")+" m)");
            }
            arm.PreventSurfacePenetration=protect;map.sand=bench;map.mode=mode;
        }
        IEnumerator HoldAndBlock(UnderwaterGraspTask task,Wa100JointPoseHold hold)
        {
            var joint=task.glove.JointMappings.First(m=>m.LinkName=="Index_Finger_L2").Joint;
            for(int f=0;f<100;f++){Drive(task,.28f);yield return new WaitForFixedUpdate();}
            float accepted=hold.Accepted(joint),worst=0;hold.TestCommands=false;
            for(int f=0;f<90;f++)
            {
                joint.AddTorque(joint.transform.TransformDirection(joint.anchorRotation*Vector3.right)*(f<45?-2f:2f),ForceMode.Force);
                yield return new WaitForFixedUpdate();worst=Mathf.Max(worst,Mathf.Abs(joint.jointPosition[0]*Mathf.Rad2Deg-accepted));
            }
            Check(worst<1.1f && accepted>10 && accepted<70,task.arm.name+": stale/no-command joint at "+accepted.ToString("F2")+" deg under +/-2 Nm external torque holds within "+worst.ToString("F3")+" degrees");
            hold.TestCommands=true;
            for(int f=0;f<100;f++){Drive(task,.45f);yield return new WaitForFixedUpdate();}
            var c=joint.GetComponentsInChildren<Collider>().First(x=>x.enabled);Vector3 goal=c.bounds.center;
            for(int f=0;f<100;f++){Drive(task,0);yield return new WaitForFixedUpdate();}
            var obstacle=new GameObject("Solid finger closure fixture");obstacle.layer=8;obstacle.transform.position=goal;var box=obstacle.AddComponent<BoxCollider>();box.size=Vector3.one*.025f;Physics.SyncTransforms();
            int blocked=hold.BlockedCommands;float maximumPenetration=0;
            for(int f=0;f<140;f++)
            {
                Drive(task,.65f);yield return new WaitForFixedUpdate();
                foreach(var shape in task.arm.Contact.Hand.GetComponentsInChildren<Collider>())if(shape.enabled && Physics.ComputePenetration(shape,shape.transform.position,shape.transform.rotation,box,box.transform.position,box.transform.rotation,out _,out float distance))maximumPenetration=Mathf.Max(maximumPenetration,distance);
            }
            Check(hold.BlockedCommands>blocked && maximumPenetration<.005f,task.arm.name+": commanded closure stops at solid, max penetration="+maximumPenetration.ToString("F4")+" m; rejected commands="+(hold.BlockedCommands-blocked));
            Object.Destroy(obstacle);
            for(int f=0;f<100;f++){Drive(task,0);yield return new WaitForFixedUpdate();}
        }
        IEnumerator Grasp(UnderwaterGraspTask task,UnderwaterGraspTarget target,Wa100JointPoseHold hold,UnderwaterGraspBench bench,Quaternion[] saved)
        {
            var arm=task.arm;var map=arm.MasterSlave;var oldMode=map.mode;map.mode=UnderwaterMasterSlaveMapper.Mode.Position;
            for(int f=0;f<70;f++){Drive(task,0);yield return new WaitForFixedUpdate();}
            var origin=new Vector3(.2f,1.1f,.4f);var command=origin;
            arm.ApplyControllerPose(origin,Quaternion.identity,false,1f/90);arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);task.Process(0,Time.fixedDeltaTime,true);
            Quaternion commandRotation=target.name.Contains("Starfish")?Quaternion.Inverse(arm.TrackingOrigin.rotation)*Quaternion.AngleAxis(-35,Vector3.forward)*arm.TrackingOrigin.rotation:Quaternion.identity;
            Action<Vector3> move=error=>
            {
                Vector3 delta=Quaternion.Inverse(arm.TrackingOrigin.rotation)*Vector3.ClampMagnitude(error*1.2f,.045f)*Time.fixedDeltaTime;
                for(int j=0;j<3;j++)delta[j]*=(map.masterHalfRange[j]-map.deadZoneMetres[j])/map.slavePositionRange[j];
                // Fine mapping has a smaller workspace: let the command settle
                // before re-clutching, otherwise each clutch discards motion still
                // in the acceleration/filter ramp and the test barely advances.
                if(Vector3.Distance(command,origin)>.10f)
                {
                    if(map.Velocity.magnitude<.004f && arm.PositionError<.008f)
                    {arm.ApplyControllerPose(command,commandRotation,false,Time.fixedDeltaTime);arm.ApplyControllerPose(command,commandRotation,true,Time.fixedDeltaTime);origin=command;}
                }
                else command+=delta;
                arm.ApplyControllerPose(command,commandRotation,true,Time.fixedDeltaTime);
            };
            // Align the real opposed-digit midpoint to the object's lower half.
            // The targets remain on the original bench, never teleported into hand.
            float insertion=0,clearance=0;
            for(int f=0;f<1800 && task.Held!=target;f++)
            {
                float fraction=f<800?0:Mathf.Min(.6f,(f-800)/400f);
                Drive(task,fraction);
                Vector3 goal=target.Shape.bounds.center-Vector3.up*target.Shape.bounds.size.y*.18f;
                Vector3 pinch=Pinch(task);
                move(goal-pinch);
                task.Process(task.ReadClosure(),Time.fixedDeltaTime,true,false);
                if(f%180==0)File.AppendAllText("Logs/HybridReview/grasp-progress.txt",target.name+" frame="+f+" pinch="+pinch.ToString("F3")+" goal="+goal.ToString("F3")+" speed="+map.Velocity.magnitude.ToString("F4")+" status="+arm.Status+" surface="+arm.WholeArmContact.LastSurface+"\n");
                if(task.Held==target)
                {
                    float bottom=arm.Contact.Hand.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                    insertion=Mathf.Max(0,bench.SurfaceHeight-bottom);clearance=bottom-bench.support.bounds.max.y;
                }
                yield return new WaitForFixedUpdate();
            }
            // If the initial approach misses, plan actual opposed surface contacts
            // for several preshapes. This searches a wrist translation, not a target
            // teleport or an enlarged attachment threshold.
            foreach(float fraction in new[]{.38f,.46f,.54f})
            {
                if(task.Held)break;
                for(int f=0;f<80;f++){Drive(task,fraction);yield return new WaitForFixedUpdate();}
                Vector3 recoveryGoal=arm.Wrist.position+PlanEnvelopeOffset(task,target);
                for(int f=0;f<650 && !task.Held;f++)
                {
                    Drive(task,fraction);move(recoveryGoal-arm.Wrist.position);
                    task.Process(task.ReadClosure(),Time.fixedDeltaTime,true,false);
                    if(task.Held==target)
                    {
                        float bottom=arm.Contact.Hand.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                        insertion=Mathf.Max(0,bench.SurfaceHeight-bottom);clearance=bottom-bench.support.bounds.max.y;
                    }
                    yield return new WaitForFixedUpdate();
                }
            }
            var attempt=new Attempt{area=arm.name,target=target.name,attached=task.Held==target,envelope=task.EnvelopeReady,thumbGap=task.ThumbGap,fingerGap=task.FingerGap,insertionDepth=insertion,supportClearance=clearance,closure=task.ReadClosure(),palm=task.PalmPoint,pinch=Pinch(task),targetCentre=target.Shape.bounds.center};
            float before=target.transform.position.y;
            if(task.Held==target)
            {
                Vector3 goal=arm.Wrist.position+Vector3.up*.17f;
                for(int f=0;f<480;f++){move(goal-arm.Wrist.position);task.Process(.6f,Time.fixedDeltaTime,true,false);yield return new WaitForFixedUpdate();}
                attempt.lift=target.transform.position.y-before;attempt.lifted=task.LiftVerified;
                float releasedY=target.transform.position.y;float releaseStart=Time.unscaledTime;
                for(int f=0;f<15 && task.Held;f++)
                {
                    // Keep the old physical closure value high deliberately. The
                    // real unsmoothed glove opening command must still release.
                    Drive(task,0);task.Process(.6f,Time.fixedDeltaTime,true,false,0);
                    yield return new WaitForFixedUpdate();
                }
                attempt.releaseSeconds=Time.unscaledTime-releaseStart;
                attempt.released=!task.Held && !target.Held && !target.Body.isKinematic && target.Body.useGravity && !target.transform.IsChildOf(arm.Wrist);
                for(int f=0;f<220;f++){Drive(task,0);yield return new WaitForFixedUpdate();}
                attempt.drop=releasedY-target.transform.position.y;
                attempt.landed=attempt.drop>.08f && target.Body.velocity.magnitude<.1f && target.Shape.bounds.min.y>=bench.sand.bounds.max.y-.008f;
                attempt.handPairsRestored=target.PendingHandPairs==0;
                Check(attempt.released && attempt.releaseSeconds<.25f,attempt.target+": raw glove opening releases despite stale/high physical closure; latency="+attempt.releaseSeconds.ToString("F3")+" s");
                Check(attempt.landed,attempt.target+": naturally falls and rests on sand, drop="+attempt.drop.ToString("F3")+" m");
                Check(attempt.handPairsRestored,attempt.target+": temporary robot collision exclusions restored after clearance");
            }
            attempt.result=attempt.lifted && attempt.released && attempt.landed?"Envelope lift, glove-open release and natural sand landing verified":"Not validated: "+task.Status;
            attempts.Add(attempt);map.mode=oldMode;
        }
        static Vector3 Pinch(UnderwaterGraspTask task)
        {
            Vector3 Center(Transform t)=>t.GetComponentsInChildren<Collider>().First(c=>c.enabled).bounds.center;
            return (Center(task.Thumb)+Center(task.Fingers[0]))*.5f;
        }
        static Vector3 SurfacePinch(UnderwaterGraspTask task,UnderwaterGraspTarget target)
        {
            Vector3 centre=target.Shape.bounds.center;
            Vector3 Nearest(Transform t)=>t.GetComponentsInChildren<Collider>().Where(c=>c.enabled).Select(c=>c.ClosestPoint(centre)).OrderBy(p=>(p-centre).sqrMagnitude).First();
            return (Nearest(task.Thumb)+task.Fingers.Select(Nearest).OrderBy(p=>(p-centre).sqrMagnitude).First())*.5f;
        }
        static Vector3 PlanEnvelopeOffset(UnderwaterGraspTask task,UnderwaterGraspTarget target)
        {
            Vector3 centre=target.Shape.bounds.center;float best=float.PositiveInfinity;Vector3 offset=Vector3.zero;
            var thumbs=task.Thumb.GetComponentsInChildren<Collider>().Where(c=>c.enabled).ToArray();
            var fingers=task.Fingers.SelectMany(t=>t.GetComponentsInChildren<Collider>()).Where(c=>c.enabled).ToArray();
            task.arm.Contact.BeginMotion();
            for(int x=-7;x<=7;x++)for(int y=-8;y<=2;y++)for(int z=-7;z<=7;z++)
            {
                Vector3 delta=new Vector3(x*.015f,y*.012f,z*.015f);
                if(task.arm.Contact.Blocked(task.arm.Wrist.position+delta,task.arm.Wrist.rotation))continue;
                foreach(var a in thumbs)foreach(var b in fingers)
                {
                    Vector3 pa=a.ClosestPoint(centre-delta)+delta,pb=b.ClosestPoint(centre-delta)+delta;
                    float ga=Vector3.Distance(pa,target.Shape.ClosestPoint(pa)),gb=Vector3.Distance(pb,target.Shape.ClosestPoint(pb));
                    Vector3 da=Vector3.ProjectOnPlane(pa-centre,Vector3.up),db=Vector3.ProjectOnPlane(pb-centre,Vector3.up);
                    float opposition=Vector3.Dot(da.normalized,db.normalized);
                    if(da.magnitude<.008f || db.magnitude<.008f || opposition>=-.15f || Mathf.Min(pa.y,pb.y)>=centre.y || Mathf.Max(pa.y,pb.y)>=centre.y+target.Shape.bounds.size.y*.35f)continue;
                    float score=Mathf.Max(ga,gb)+.05f*delta.magnitude;
                    if(score<best){best=score;offset=delta;}
                }
            }
            File.AppendAllText("Logs/HybridReview/grasp-plans.txt",target.name+" correction="+offset.ToString("F4")+" predicted max contact gap + translation cost="+best.ToString("F5")+"\n");
            return offset;
        }
        static void Drive(UnderwaterGraspTask task,float fraction)
        {foreach(var m in task.glove.JointMappings)if(m.Joint)m.CurrentTarget=m.CountsForClosure?(Mathf.Abs(m.LowerLimitDegrees)>Mathf.Abs(m.UpperLimitDegrees)?m.LowerLimitDegrees:m.UpperLimitDegrees)*fraction:0;}
        static void Restore(PicoArmPoseDriver arm,Quaternion[] saved)
        {for(int i=0;i<4;i++)arm.Joints[i].localRotation=saved[i];arm.Contact.Hand.SyncMountedRoot();Physics.SyncTransforms();arm.ApplyControllerPose(Vector3.zero,Quaternion.identity,false,1f/90);}
        static void Save(RenderTexture rt,string path)
        {var previous=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());RenderTexture.active=previous;Object.Destroy(t);}
        static string V(Vector3 v)=>string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:F6},{1:F6},{2:F6}",v.x,v.y,v.z);
    }
}
