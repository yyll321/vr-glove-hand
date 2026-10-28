using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterGloveSandChecks
    {
        const string Key="UnderwaterGloveSandChecks";
        static UnderwaterGloveSandChecks(){EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)){SessionState.SetBool(Key,false);new GameObject("Glove and sand verification").AddComponent<UnderwaterGloveSandProbe>();}};}
        [MenuItem("Tools/Underwater/Research/35 Verify Glove Mapping and Sand Cavities")]
        public static void Run(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");UnderwaterPreviewCompatibility.Verify();SessionState.SetBool(Key,true);UnderwaterPhysicsTestMode.Enter();}
    }
    public sealed class UnderwaterGloveSandProbe:MonoBehaviour
    {
        [Serializable]class JointRow{public string area,command,joint,axis;public float requested,actual;}
        [Serializable]class SandRow{public string area;public float centreDepth,retainedDepth,farDepth,meanUpdateMs,maxUpdateMs,landingClearance;public int vertices,colliderTriangles;}
        [Serializable]class Report{public string time,scope="Isolated UDP port 15556; synthetic glove commands and contact trajectories on actual scene articulations/sand. Human glove parity and worn PICO performance need confirmation.";public bool passed;public string[] checks,errors;public JointRow[] joints;public SandRow[] sand;}
        readonly List<string> checks=new(),errors=new();readonly List<JointRow> joints=new();readonly List<SandRow> sand=new();
        void Check(bool ok,string message){(ok?checks:errors).Add(message);}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
        IEnumerator Start()
        {
            Directory.CreateDirectory("Logs/GloveSand");Application.logMessageReceived+=Log;yield return new WaitForSecondsRealtime(2);
            var sandShader=Shader.Find("Underwater/Fine Wet Sand");Check(sandShader && !ShaderUtil.ShaderHasError(sandShader),"Sand world-projected shader compiles without errors");
            var motor=Object.FindObjectOfType<UnderwaterMotor>();motor.enabled=false;
            foreach(var arm in Object.FindObjectsOfType<PicoArmPoseDriver>()){arm.enabled=false;foreach(var old in arm.PreviousControllers)if(old)old.enabled=false;}
            var receiverObject=new GameObject("Isolated glove packet receiver");var receiver=receiverObject.AddComponent<Network>();receiver.Port=15556;
            // Actual vendor HandDriver component, independent transforms and no
            // automatic live-network binding. Feed identical UDP data to both.
            var referenceObject=new GameObject("HandDriver comparison fixture");var reference=referenceObject.AddComponent<HandDriver>();
            Transform Link(string name){var g=new GameObject(name);g.transform.SetParent(referenceObject.transform);return g.transform;}
            reference.Thumb1=Link("Thumb1");reference.Thumb2=Link("Thumb2");reference.Thumb3=Link("Thumb3");
            reference.Index1=Link("Index1");reference.Index2=Link("Index2");reference.Index3=Link("Index3");
            reference.Middle1=Link("Middle1");reference.Middle2=Link("Middle2");reference.Middle3=Link("Middle3");
            reference.Ring1=Link("Ring1");reference.Ring2=Link("Ring2");reference.Ring3=Link("Ring3");
            reference.Pinky1=Link("Pinky1");reference.Pinky2=Link("Pinky2");reference.Pinky3=Link("Pinky3");reference.Wrist=Link("Wrist");
            reference.Hand=HandDriver.HandType.Right;reference.Pitch=HandDriver.Axis.z;reference.Yaw=HandDriver.Axis.y;reference.Roll=HandDriver.Axis.x;reference.NeedRealTransfrom=true;reference.Network=receiver;reference.CharacterName="GloveSandRight";
            yield return null;reference.UsingNetwork=true;
            using(var udp=new UdpClient())
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>().OrderBy(t=>t.arm.SeabedArea))
            {
                task.enabled=false;var mapper=task.glove;mapper.Network=receiver;mapper.CharacterName="";mapper.ResolvedCharacterName="";
                var hold=mapper.GetComponent<Wa100JointPoseHold>();hold.TestCommands=false;
                var values=new float[23];values[10]=-31;values[9]=-27;values[14]=-18;values[13]=-13;
                // First frame is deliberately not an open hand.
                Send(udp,values);yield return new WaitForSecondsRealtime(.35f);
                Check(mapper.HasFreshFrame && mapper.ResolvedCharacterName=="GloveSandRight",task.name+": one complete role pinned from isolated UDP stream");
                Check(Mathf.Abs(mapper.JointMappings.First(m=>m.LinkName=="Middle_Finger_L1").RequestedTarget-31)<.01f,task.name+": bent first packet remains 31 degrees, never zero-calibrated");
                foreach(string digit in new[]{"Index","Middle","Ring","Little"})
                {
                    Array.Clear(values,0,values.Length);Send(udp,values);yield return new WaitForSecondsRealtime(.4f);
                    var distal=mapper.JointMappings.First(m=>m.LinkName==digit+"_Finger_L2").Joint;
                    var tip=distal.GetComponentsInChildren<Renderer>().First();
                    float openPalmDistance=Vector3.Distance(tip.bounds.center,task.PalmPoint);
                    Array.Clear(values,0,values.Length);int root=digit=="Index"?6:digit=="Middle"?10:digit=="Ring"?14:18;values[root]=-40;values[root-1]=-30;
                    Send(udp,values);yield return new WaitForSecondsRealtime(.8f);
                    var a=mapper.JointMappings.First(m=>m.LinkName==digit+"_Finger_L1");var b=mapper.JointMappings.First(m=>m.LinkName==digit+"_Finger_L2");
                    float referenceRoot=digit=="Index"?reference.index1.z:digit=="Middle"?reference.middle1.z:digit=="Ring"?reference.ring1.z:reference.pinky1.z;
                    float referenceDistal=digit=="Index"?reference.index2.z:digit=="Middle"?reference.middle2.z:digit=="Ring"?reference.ring2.z:reference.pinky2.z;
                    Check(Mathf.Abs(a.RequestedTarget+referenceRoot)<.01f && Mathf.Abs(b.RequestedTarget+referenceDistal)<.01f,task.name+": "+digit+" vendor negative flexion -40/-30 converts to URDF closing +40/+30");
                    float closedPalmDistance=Vector3.Distance(tip.bounds.center,task.PalmPoint);
                    Check(closedPalmDistance<openPalmDistance-.005f,task.name+": "+digit+" physically curls toward palm "+openPalmDistance.ToString("F4")+" -> "+closedPalmDistance.ToString("F4")+" m");
                    foreach(var m in mapper.JointMappings)
                    {
                        float actual=Wa100JointPoseHold.ReadAngle(m);joints.Add(new JointRow{area=task.name,command=digit,joint=m.LinkName,axis=m.Axis.ToString(),requested=m.RequestedTarget,actual=actual});
                        Check(Mathf.Abs(actual-m.RequestedTarget)<3,task.name+": isolated "+digit+" channel -> "+m.JointName+" actual="+actual.ToString("F2")+" requested="+m.RequestedTarget.ToString("F2"));
                    }
                    // Unrelated distal sensor must not leak into the second joint.
                    values[root-2]=-75;Send(udp,values);yield return new WaitForSecondsRealtime(.1f);
                    Check(Mathf.Abs(b.RequestedTarget-30)<.01f,task.name+": "+digit+" distal sensor does not amplify middle joint");
                }
                foreach(var command in new[]{new Vector2(0,25),new Vector2(0,-25),new Vector2(-45,0),new Vector2(-35,20)})
                {
                    Array.Clear(values,0,values.Length);values[2]=command.x;values[3]=command.y;values[1]=-24;values[0]=-29;
                    Send(udp,values);yield return new WaitForSecondsRealtime(.8f);
                    foreach(var m in mapper.JointMappings.Where(m=>m.LinkName.StartsWith("Thumb")))
                    {
                        float actual=Wa100JointPoseHold.ReadAngle(m);joints.Add(new JointRow{area=task.name,command="thumb "+command,joint=m.LinkName,axis=m.Axis.ToString(),requested=m.RequestedTarget,actual=actual});
                        Check(Mathf.Abs(actual-m.RequestedTarget)<3,task.name+": physical "+m.JointName+" requested="+m.RequestedTarget.ToString("F2")+" actual="+actual.ToString("F2"));
                    }
                    var yaw=mapper.JointMappings[0];var pitch=mapper.JointMappings[1];
                    Check(Mathf.Abs(yaw.RequestedTarget+reference.thumb1.y)<.01f && Mathf.Abs(pitch.RequestedTarget+reference.thumb1.z)<.01f && Mathf.Abs(mapper.JointMappings[2].RequestedTarget+reference.thumb2.z)<.01f && Mathf.Abs(mapper.JointMappings[3].RequestedTarget-reference.thumb3.z)<.01f,task.name+": signed thumb pitch/yaw/IP channels match vendor motion with URDF axis conversion");
                }
                // Replay the actual live negative-flexion sample which the old
                // positive-only fixtures missed. Never inject into live port 5555.
                Array.Clear(values,0,values.Length);
                values[2]=-14.3f;values[3]=-23.4f;values[6]=-21.8f;values[5]=-24.9f;
                values[10]=-80;values[9]=-100;values[14]=-30;values[13]=-42.4f;values[18]=-9.8f;values[17]=-11.6f;
                Send(udp,values);yield return new WaitForSecondsRealtime(1);
                foreach(var m in mapper.JointMappings)
                {
                    float expected=Mathf.Clamp(values[int.Parse(m.PrimaryKey.Substring(1))]*m.PrimaryScale,m.LowerLimitDegrees,m.UpperLimitDegrees);
                    Check(Mathf.Abs(m.RequestedTarget-expected)<.01f && Mathf.Abs(Wa100JointPoseHold.ReadAngle(m)-expected)<3,task.name+": real role 3674 sample -> "+m.JointName+" expected="+expected+" actual="+Wa100JointPoseHold.ReadAngle(m));
                }
                // Positive extension must stop at zero, not be reflected into a
                // closing command by Abs(). Full-fist input exercises all digits.
                foreach(int sign in new[]{-1,1})
                {
                    Array.Clear(values,0,values.Length);
                    foreach(int channel in new[]{0,1,2,5,6,9,10,13,14,17,18})values[channel]=sign*55;
                    Send(udp,values);yield return new WaitForSecondsRealtime(.8f);
                    foreach(var m in mapper.JointMappings.Where(m=>m.CountsForClosure))
                    {
                        float expected=sign<0?Mathf.Clamp(values[int.Parse(m.PrimaryKey.Substring(1))]*m.PrimaryScale,m.LowerLimitDegrees,m.UpperLimitDegrees):0;
                        Check(Mathf.Abs(Wa100JointPoseHold.ReadAngle(m)-expected)<3,task.name+": "+(sign<0?"all-digit fist":"positive extension stays open")+" "+m.JointName);
                    }
                }
                Array.Clear(values,0,values.Length);Send(udp,values);yield return new WaitForSecondsRealtime(.4f);
                mapper.UseHandDriverMappings();mapper.CharacterName="GloveSandRight";
                values[6]=-32;values[5]=-26;Send(udp,values);yield return new WaitForSecondsRealtime(.65f);
                Check(Mathf.Abs(Wa100JointPoseHold.ReadAngle(mapper.JointMappings.First(m=>m.LinkName=="Index_Finger_L1"))-32)<3 && Mathf.Abs(Wa100JointPoseHold.ReadAngle(mapper.JointMappings.First(m=>m.LinkName=="Index_Finger_L2"))-26)<3,task.name+": replacing mapping array while live rebuilds hold bindings without freezing commands");
                Array.Clear(values,0,values.Length);Send(udp,values);yield return new WaitForSecondsRealtime(.4f);
                float[] requested=mapper.JointMappings.Select(m=>m.RequestedTarget).ToArray();
                Send(udp,Enumerable.Repeat(70f,23).ToArray(),"AnotherGlove");yield return new WaitForSecondsRealtime(.15f);
                Check(mapper.JointMappings.Select((m,i)=>Mathf.Abs(m.RequestedTarget-requested[i])<.01f).All(v=>v),task.name+": unrelated glove role cannot change pinned hand");
                Send(udp,Enumerable.Repeat(70f,4).ToArray());yield return new WaitForSecondsRealtime(.15f);
                Check(!mapper.HasFreshFrame && mapper.JointMappings.Select((m,i)=>Mathf.Abs(m.RequestedTarget-requested[i])<.01f).All(v=>v),task.name+": incomplete frame holds all joints, no per-channel zero substitution");
                Send(udp,values);yield return new WaitForSecondsRealtime(.15f);
                mapper.enabled=false;hold.TestCommands=true;
                foreach(var m in mapper.JointMappings)m.CurrentTarget=0;
                for(int i=0;i<60;i++)yield return new WaitForFixedUpdate();
                var index=mapper.JointMappings.First(m=>m.LinkName=="Index_Finger_L2");
                for(int i=0;i<90;i++){index.CurrentTarget=40;yield return new WaitForFixedUpdate();}
                var solidPoint=index.Joint.GetComponentsInChildren<Collider>().First(c=>c.enabled).bounds.center;
                for(int i=0;i<90;i++){index.CurrentTarget=0;yield return new WaitForFixedUpdate();}
                var obstruction=new GameObject("Finger hard stop fixture"){layer=8};obstruction.transform.position=solidPoint;
                var obstacle=obstruction.AddComponent<BoxCollider>();obstacle.size=Vector3.one*.025f;Physics.SyncTransforms();
                int blockedBefore=hold.BlockedCommands;float penetration=0;
                for(int i=0;i<130;i++)
                {
                    index.CurrentTarget=60;yield return new WaitForFixedUpdate();
                    foreach(var c in index.Joint.GetComponentsInChildren<Collider>())if(c.enabled && Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,obstacle,obstacle.transform.position,obstacle.transform.rotation,out _,out float d))penetration=Mathf.Max(penetration,d);
                }
                Check(hold.BlockedCommands>blockedBefore && penetration<.005f,task.name+": original hard finger stop retained; penetration="+penetration.ToString("F4")+" m");Object.Destroy(obstruction);
                foreach(var m in mapper.JointMappings)m.CurrentTarget=0;
                for(int i=0;i<90;i++)yield return new WaitForFixedUpdate();
                hold.TestCommands=false;float[] thumbAngles=mapper.JointMappings.Take(2).Select(Wa100JointPoseHold.ReadAngle).ToArray();float holdError=0;
                for(int i=0;i<90;i++)
                {
                    foreach(var m in mapper.JointMappings.Take(2))m.Joint.AddTorque(m.Joint.transform.TransformDirection(m.Joint.anchorRotation*Wa100UdexRealArticulationMapper.AxisVector(m))*(i<45?2:-2),ForceMode.Force);
                    yield return new WaitForFixedUpdate();for(int j=0;j<2;j++)holdError=Mathf.Max(holdError,Mathf.Abs(Wa100JointPoseHold.ReadAngle(mapper.JointMappings[j])-thumbAngles[j]));
                }
                Check(holdError<1.1f,task.name+": both thumb axes retain rigid hold under +/-2 Nm, max error="+holdError.ToString("F3")+" deg");hold.TestCommands=true;
                var bench=task.GetComponentInChildren<UnderwaterGraspBench>();var terrain=bench.deformable;terrain.automaticStamping=false;terrain.ResetImpressions();
                var fixture=GameObject.CreatePrimitive(PrimitiveType.Capsule);fixture.name="Finger-sized sand insertion fixture";fixture.layer=2;
                fixture.transform.localScale=new Vector3(.022f,.028f,.022f);
                Vector3 point=terrain.transform.position+new Vector3(.08f,-.018f,.06f);fixture.transform.position=point;Physics.SyncTransforms();
                var shape=fixture.GetComponent<Collider>();
                float sum=0,max=0;
                for(int i=0;i<30;i++){terrain.Simulate(new[]{shape},.05f);sum+=terrain.LastUpdateMilliseconds;max=Mathf.Max(max,terrain.LastUpdateMilliseconds);}
                terrain.RefreshCollision();float depth=terrain.transform.position.y-terrain.HeightAt(point);
                float far=terrain.transform.position.y-terrain.HeightAt(point+Vector3.right*.2f);
                Check(depth>.025f && depth<.075f && Mathf.Abs(far)<.001f,task.name+": fingertip-sized local cavity depth="+depth.ToString("F4")+" m, far surface unchanged");
                fixture.transform.position+=Vector3.up*.2f;shape.enabled=false;Physics.SyncTransforms();
                for(int i=0;i<20;i++)terrain.Simulate(Array.Empty<Collider>(),.05f);
                float retained=terrain.transform.position.y-terrain.HeightAt(point);
                Check(retained>depth*.8f && retained<depth,task.name+": withdrawal leaves persistent cavity with slow settling");
                terrain.RefreshCollision();
                var ball=GameObject.CreatePrimitive(PrimitiveType.Sphere);ball.name="Released item support fixture";ball.layer=10;ball.transform.localScale=Vector3.one*.012f;ball.transform.position=new Vector3(point.x,terrain.transform.position.y+.18f,point.z);
                var body=ball.AddComponent<Rigidbody>();body.mass=.02f;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                Physics.IgnoreCollision(ball.GetComponent<Collider>(),bench.sand,true);
                foreach(var c in task.arm.GetComponentsInChildren<Collider>())Physics.IgnoreCollision(ball.GetComponent<Collider>(),c,true);
                foreach(var t in task.targets)Physics.IgnoreCollision(ball.GetComponent<Collider>(),t.Shape,true);
                for(int i=0;i<100;i++)yield return new WaitForFixedUpdate();
                float clearance=ball.GetComponent<Collider>().bounds.min.y-(terrain.HeightAt(point)-bench.visualSurfaceOffset);
                Check(Mathf.Abs(clearance)<.014f && body.velocity.magnitude<.1f && ball.transform.position.y<terrain.transform.position.y-.008f,task.name+": item falls into cavity onto deformed support, clearance="+clearance.ToString("F4")+" m");
                Check(task.arm.Contact.Hand.GetComponentsInChildren<Collider>().Where(c=>c.enabled).All(c=>Physics.GetIgnoreCollision(c,terrain.SurfaceCollider)),task.name+": fingers ignore sand support mesh while targets collide");
                task.arm.Contact.BeginMotion();float bottom=task.arm.Contact.Hand.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                Vector3 inSand=task.arm.Wrist.position+Vector3.up*(bench.SurfaceHeight-.025f-bottom);
                Check(!task.arm.Contact.Blocked(inSand,task.arm.Wrist.rotation) && task.arm.Contact.Blocked(inSand-Vector3.up*.16f,task.arm.Wrist.rotation),task.name+": soft layer entry and hard support blocking retained");
                sand.Add(new SandRow{area=task.name,centreDepth=depth,retainedDepth=retained,farDepth=far,meanUpdateMs=sum/30,maxUpdateMs=max,landingClearance=clearance,vertices=terrain.visualTemplate.vertexCount,colliderTriangles=terrain.collisionTemplate.triangles.Length/3});
                var renderers=task.arm.GetComponentsInChildren<Renderer>().Concat(task.targets.SelectMany(t=>t.GetComponentsInChildren<Renderer>())).Where(r=>r.enabled).Distinct().ToArray();foreach(var r in renderers)r.enabled=false;
                Screenshot(terrain,"Logs/GloveSand/cavity-"+(task.arm.SeabedArea?"seabed":"pool")+".png");foreach(var r in renderers)r.enabled=true;
                Object.Destroy(ball);Object.Destroy(fixture);terrain.ResetImpressions();
                // Exercise an actual WA100 articulation collider footprint as well.
                var digitShape=task.glove.JointMappings.First(m=>m.LinkName=="Index_Finger_L2").Joint.GetComponentsInChildren<Collider>().First(c=>c.enabled);
                Vector3 old=task.arm.transform.position;Vector3 targetPoint=terrain.transform.position-Vector3.up*.02f;
                task.arm.transform.position+=targetPoint-digitShape.bounds.center;task.arm.Contact.Hand.SyncMountedRoot();Physics.SyncTransforms();yield return new WaitForFixedUpdate();
                task.arm.Contact.Hand.SyncMountedRoot();Physics.SyncTransforms();
                terrain.Simulate(new[]{digitShape},.05f);Check(terrain.DeepestDent>.005f,task.name+": actual WA100 finger collider excavates visible geometry");
                File.AppendAllText("Logs/GloveSand/actual-finger.txt",task.name+" position="+digitShape.bounds.center+" min="+digitShape.bounds.min+" surface="+terrain.transform.position+" dent="+terrain.DeepestDent+" enabled="+digitShape.enabled+"\n");
                task.arm.transform.position=old;task.arm.Contact.Hand.SyncMountedRoot();terrain.ResetImpressions();
                foreach(var target in task.targets)
                {
                    Vector3 home=target.transform.position;Quaternion rotation=target.transform.rotation;
                    target.Attach(task.arm.Wrist);target.IgnoreRobotContacts(task.arm.GetComponentsInChildren<Collider>());
                    bench.ConfigureFiltering();terrain.RefreshCollision();
                    Check(!Physics.GetIgnoreCollision(target.Shape,terrain.SurfaceCollider),target.name+": sand collider recook while held preserves target/environment collision");
                    target.transform.SetPositionAndRotation(home+Vector3.up*.25f,rotation);Physics.SyncTransforms();target.Release();
                    for(int i=0;i<160;i++)yield return new WaitForFixedUpdate();
                    float supported=terrain.HeightAt(target.Shape.bounds.center)-bench.visualSurfaceOffset;
                    Check(!target.Body.isKinematic && target.Body.useGravity && target.Shape.bounds.min.y>=supported-.01f && target.Shape.bounds.min.y<supported+.01f && target.Body.velocity.magnitude<.1f,target.name+": natural release still lands on sand after collider recooking during hold");
                    target.Body.isKinematic=true;target.transform.SetPositionAndRotation(home,rotation);Physics.SyncTransforms();target.Body.isKinematic=false;target.Body.velocity=target.Body.angularVelocity=Vector3.zero;
                }
            }
            File.WriteAllText("Logs/GloveSand/results.json",JsonUtility.ToJson(new Report{time=DateTime.Now.ToString("O"),passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray(),joints=joints.ToArray(),sand=sand.ToArray()},true));
            reference.UsingNetwork=false;Application.logMessageReceived-=Log;EditorApplication.isPlaying=false;
        }
        static void Send(UdpClient udp,float[] values,string role="GloveSandRight")
        {
            string parameters=string.Join(",",values.Select((v,i)=>"{\"Name\":\"r"+i+"\",\"Value\":"+v.ToString(CultureInfo.InvariantCulture)+"}"));
            var bytes=Encoding.UTF8.GetBytes("{\""+role+"\":{\"Parameter\":["+parameters+"]}}");udp.Send(bytes,bytes.Length,new IPEndPoint(IPAddress.Loopback,15556));
        }
        static void Screenshot(UnderwaterDeformableSand terrain,string path)
        {
            var go=new GameObject("Sand detail verification camera");var camera=go.AddComponent<Camera>();camera.enabled=false;
            var centre=terrain.transform.position+new Vector3(.08f,0,.06f);go.transform.position=centre+new Vector3(.18f,.23f,-.22f);go.transform.LookAt(centre);camera.fieldOfView=42;camera.nearClipPlane=.01f;camera.farClipPlane=5;
            var rt=RenderTexture.GetTemporary(960,720,24,RenderTextureFormat.ARGB32);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(960,720,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,960,720),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.Destroy(tex);Object.Destroy(go);
        }
    }
}
