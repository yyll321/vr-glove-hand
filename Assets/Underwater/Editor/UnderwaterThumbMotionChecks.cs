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
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterThumbMotionChecks
    {
        const string Key="UnderwaterThumbMotionChecks";
        static UnderwaterThumbMotionChecks(){EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)){SessionState.SetBool(Key,false);new GameObject("Thumb motion verification").AddComponent<UnderwaterThumbMotionProbe>();}};}
        [MenuItem("Tools/Underwater/Research/36 Verify Thumb During Arm Descent")]
        public static void Run(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");UnderwaterPreviewCompatibility.Verify();SessionState.SetBool(Key,true);UnderwaterPhysicsTestMode.Enter();}
    }
    public sealed class UnderwaterThumbMotionProbe:MonoBehaviour
    {
        [Serializable]class Row{public string area,scenario;public float wristDrop,maxYawError,maxPitchError,maxJointError,maxRelativeThumbAngle,maxRootError;public int contactFrames;}
        [Serializable]class InputRow{public string area;public float yawTravel,pitchTravel,maxServoError,staleDrift,wristDrop;public bool freshDuringGrip,staleHeld;}
        [Serializable]class Report{public string time,scope="Synthetic fixed finger commands, isolated UDP 15557 glove commands and real ApplyControllerPose with physics steps; no live glove/PICO measurements.";public bool passed;public Row[] rows;public InputRow[] inputRows;public string[] checks,errors;}
        readonly List<Row> rows=new();readonly List<string> checks=new(),errors=new();
        readonly List<InputRow> inputRows=new();
        void Check(bool ok,string text){(ok?checks:errors).Add(text);}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
        IEnumerator Start()
        {
            Directory.CreateDirectory("Logs/ThumbMotion");Application.logMessageReceived+=Log;yield return new WaitForSecondsRealtime(2);
            Object.FindObjectOfType<UnderwaterMotor>().enabled=false;
            var receiverObject=new GameObject("Isolated thumb motion UDP receiver");
            var receiver=receiverObject.AddComponent<Network>();receiver.Port=15557;
            using(var udp=new UdpClient())
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>().OrderBy(t=>t.arm.SeabedArea))
            {
                task.enabled=false;var arm=task.arm;arm.enabled=false;arm.ResolveBindings();
                foreach(var old in arm.PreviousControllers)if(old)old.enabled=false;
                var mapper=task.glove;mapper.enabled=false;var hold=mapper.GetComponent<Wa100JointPoseHold>();hold.TestCommands=true;
                foreach(var m in mapper.JointMappings)m.CurrentTarget=m.LinkName.Contains("Finger")?35:0;
                mapper.JointMappings[0].CurrentTarget=-12;mapper.JointMappings[1].CurrentTarget=20;
                for(int i=0;i<150;i++)yield return new WaitForFixedUpdate();
                var saved=arm.Joints.Select(j=>j.localRotation).ToArray();var thumb=mapper.JointMappings[0].Joint;
                var local=thumb.transform.localRotation;var expected=mapper.JointMappings.Select(Wa100JointPoseHold.ReadAngle).ToArray();
                Vector3 origin=new Vector3(.2f,1.1f,.4f);
                foreach(string scenario in new[]{"rigid mount descent","right grip descent"})
                {
                    var row=new Row{area=arm.name,scenario=scenario};rows.Add(row);
                    Vector3 rootStart=arm.transform.position,wristStart=arm.Wrist.position;
                    arm.ApplyControllerPose(origin,Quaternion.identity,false,1f/90);arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);
                    using(var csv=new StreamWriter("Logs/ThumbMotion/"+(arm.SeabedArea?"sea":"pool")+"-"+(scenario.StartsWith("rigid")?"mount":"grip")+".csv"))
                    {
                        csv.WriteLine("sample,wristY,requestedYaw,requestedPitch,acceptedYaw,actualYaw,acceptedPitch,actualPitch,localAngle,rootError,contact");
                        for(int f=0;f<720;f++)
                        {
                            if(scenario.StartsWith("rigid")){arm.transform.position=rootStart+Vector3.down*(.12f*Mathf.Min(1,f/600f));arm.Contact.Hand.SyncMountedRoot();}
                            else
                            {
                                // 80 mm downward position input, settle, then
                                // re-clutch for another stroke while fingers stay.
                                int stroke=f%240;if(stroke==0){arm.ApplyControllerPose(origin,Quaternion.identity,false,Time.fixedDeltaTime);arm.ApplyControllerPose(origin,Quaternion.identity,true,Time.fixedDeltaTime);}
                                arm.ApplyControllerPose(origin+Vector3.down*(.08f*Mathf.Min(1,stroke/90f)),Quaternion.identity,true,Time.fixedDeltaTime);
                            }
                            yield return new WaitForFixedUpdate();
                            float yaw=Wa100JointPoseHold.ReadAngle(mapper.JointMappings[0]),pitch=Wa100JointPoseHold.ReadAngle(mapper.JointMappings[1]);
                            row.maxYawError=Mathf.Max(row.maxYawError,Mathf.Abs(yaw-expected[0]));row.maxPitchError=Mathf.Max(row.maxPitchError,Mathf.Abs(pitch-expected[1]));
                            for(int j=0;j<expected.Length;j++)row.maxJointError=Mathf.Max(row.maxJointError,Mathf.Abs(Wa100JointPoseHold.ReadAngle(mapper.JointMappings[j])-expected[j]));
                            row.maxRelativeThumbAngle=Mathf.Max(row.maxRelativeThumbAngle,Quaternion.Angle(local,thumb.transform.localRotation));
                            row.maxRootError=Mathf.Max(row.maxRootError,arm.Contact.Hand.MountError);if(arm.SurfaceContact)row.contactFrames++;
                            if(f%5==0)csv.WriteLine(string.Join(",",new[]{f.ToString(),arm.Wrist.position.y.ToString("F5"),mapper.JointMappings[0].CurrentTarget.ToString("F3"),mapper.JointMappings[1].CurrentTarget.ToString("F3"),hold.Accepted(thumb).ToString("F3"),yaw.ToString("F3"),Wa100UdexRealArticulationMapper.GetDrive(mapper.JointMappings[1]).target.ToString("F3"),pitch.ToString("F3"),Quaternion.Angle(local,thumb.transform.localRotation).ToString("F3"),arm.Contact.Hand.MountError.ToString("F5"),arm.SurfaceContact?"1":"0"}));
                        }
                    }
                    row.wristDrop=wristStart.y-arm.Wrist.position.y;
                    Check(row.maxYawError<1.2f && row.maxPitchError<1.2f && row.maxJointError<1.2f && row.maxRelativeThumbAngle<2,arm.name+" "+scenario+": constant commands preserve thumb and four bent fingers; yaw error="+row.maxYawError+", pitch="+row.maxPitchError+", all joints="+row.maxJointError+", local rotation="+row.maxRelativeThumbAngle);
                    Check(row.wristDrop>.02f,arm.name+" "+scenario+": wrist actually moves down "+row.wristDrop+" m");
                    arm.transform.position=rootStart;for(int j=0;j<4;j++)arm.Joints[j].localRotation=saved[j];arm.Contact.Hand.SyncMountedRoot();
                    for(int f=0;f<120;f++)yield return new WaitForFixedUpdate();
                }
                // Keep the real mapper active during Grip: a repair must not
                // merely freeze the thumb or drop incoming glove packets.
                mapper.Network=receiver;mapper.AutoFindNetwork=false;mapper.CharacterName="ThumbMotionRight";
                mapper.ReconnectHandDriverRole();hold.TestCommands=false;mapper.enabled=true;
                var input=new InputRow{area=arm.name};inputRows.Add(input);
                float minYaw=float.PositiveInfinity,maxYaw=float.NegativeInfinity;
                float minPitch=float.PositiveInfinity,maxPitch=float.NegativeInfinity;
                Vector3 startWrist=arm.Wrist.position;
                arm.ApplyControllerPose(origin,Quaternion.identity,false,Time.fixedDeltaTime);
                arm.ApplyControllerPose(origin,Quaternion.identity,true,Time.fixedDeltaTime);
                for(int f=0;f<360;f++)
                {
                    float t=Mathf.Min(1,f/240f);
                    if(f%4==0)Send(udp,12-20*t,(20+20*t)/.6f);
                    // 80 mm master input clears the existing fine-position
                    // workspace scaling while keeping the hand well in the air.
                    arm.ApplyControllerPose(origin+Vector3.down*(.08f*t),Quaternion.identity,true,Time.fixedDeltaTime);
                    yield return new WaitForFixedUpdate();
                    float yaw=Wa100JointPoseHold.ReadAngle(mapper.JointMappings[0]);
                    float pitch=Wa100JointPoseHold.ReadAngle(mapper.JointMappings[1]);
                    minYaw=Mathf.Min(minYaw,yaw);maxYaw=Mathf.Max(maxYaw,yaw);
                    minPitch=Mathf.Min(minPitch,pitch);maxPitch=Mathf.Max(maxPitch,pitch);
                    foreach(var m in mapper.JointMappings.Take(2))
                        input.maxServoError=Mathf.Max(input.maxServoError,Mathf.Abs(Wa100JointPoseHold.ReadAngle(m)-Wa100UdexRealArticulationMapper.GetDrive(m).target));
                }
                input.yawTravel=maxYaw-minYaw;input.pitchTravel=maxPitch-minPitch;
                input.wristDrop=startWrist.y-arm.Wrist.position.y;
                input.freshDuringGrip=mapper.HasFreshFrame && Mathf.Abs(mapper.JointMappings[0].RequestedTarget-8)<.01f && Mathf.Abs(mapper.JointMappings[1].RequestedTarget-40)<.01f;
                Check(input.freshDuringGrip && input.yawTravel>15 && input.pitchTravel>15 && input.maxServoError<1.2f && input.wristDrop>.015f,
                    arm.name+": real UDP mapping stays active during Grip descent; yaw/pitch travel="+input.yawTravel+"/"+input.pitchTravel+", peak servo error="+input.maxServoError);
                yield return new WaitForSecondsRealtime(1.25f);
                var stale=mapper.JointMappings.Take(2).Select(Wa100JointPoseHold.ReadAngle).ToArray();
                arm.ApplyControllerPose(origin,Quaternion.identity,false,Time.fixedDeltaTime);
                arm.ApplyControllerPose(origin,Quaternion.identity,true,Time.fixedDeltaTime);
                for(int f=0;f<180;f++)
                {
                    arm.ApplyControllerPose(origin+Vector3.down*(.03f*f/180),Quaternion.identity,true,Time.fixedDeltaTime);
                    yield return new WaitForFixedUpdate();
                    for(int j=0;j<2;j++)input.staleDrift=Mathf.Max(input.staleDrift,Mathf.Abs(Wa100JointPoseHold.ReadAngle(mapper.JointMappings[j])-stale[j]));
                }
                input.staleHeld=!mapper.HasFreshFrame && input.staleDrift<1.2f;
                Check(input.staleHeld,arm.name+": stale glove preserves thumb while arm continues down; drift="+input.staleDrift);
                Check(rows.Where(r=>r.area==arm.name).All(r=>r.contactFrames==0),arm.name+": fixed-command reproduction stays in air without contact");
                mapper.enabled=false;
            }
            File.WriteAllText("Logs/ThumbMotion/results.json",JsonUtility.ToJson(new Report{time=DateTime.Now.ToString("O"),passed=errors.Count==0,rows=rows.ToArray(),inputRows=inputRows.ToArray(),checks=checks.ToArray(),errors=errors.ToArray()},true));
            Application.logMessageReceived-=Log;EditorApplication.isPlaying=false;
        }
        static void Send(UdpClient udp,float yawSensor,float pitchSensor)
        {
            var values=new float[23];values[3]=yawSensor;values[2]=-pitchSensor;
            string parameters=string.Join(",",values.Select((v,i)=>"{\"Name\":\"r"+i+"\",\"Value\":"+v.ToString(CultureInfo.InvariantCulture)+"}"));
            byte[] bytes=Encoding.UTF8.GetBytes("{\"ThumbMotionRight\":{\"Parameter\":["+parameters+"]}}");
            udp.Send(bytes,bytes.Length,new IPEndPoint(IPAddress.Loopback,15557));
        }
    }
}
