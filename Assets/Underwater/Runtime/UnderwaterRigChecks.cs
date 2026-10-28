using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace UnderwaterBaseline
{
    static class UnderwaterRigChecks
    {
        public static IEnumerator Run(UnderwaterMotor motor,string output,Action<bool,string> check)
        {
            check(UnderwaterControllerInput.MapMovement(Vector2.up)==Vector3.up,"Left stick up raises observer");
            check(UnderwaterControllerInput.MapMovement(Vector2.down)==Vector3.down,"Left stick down lowers observer");
            check(UnderwaterControllerInput.MapMovement(Vector2.left)==Vector3.forward,"Left stick left approaches scene");
            check(UnderwaterControllerInput.MapMovement(Vector2.right)==Vector3.back,"Left stick right retreats from scene");
            InputSystem.RegisterLayout("{\"name\":\"UnderwaterCheckController\",\"extend\":\"XRController\",\"controls\":[{\"name\":\"primaryButton\",\"layout\":\"Button\"},{\"name\":\"thumbstick\",\"layout\":\"Stick\"}]}");
            var controller=InputSystem.AddDevice("UnderwaterCheckController");InputSystem.SetDeviceUsage(controller,UnityEngine.InputSystem.CommonUsages.LeftHand);
            var x=controller.GetChildControl<ButtonControl>("primaryButton");bool initialArea=motor.InSeabed;
            InputSystem.QueueDeltaStateEvent(x,(byte)1);yield return new WaitForSecondsRealtime(0.2f);
            check(motor.InSeabed!=initialArea,"Left X action switches active water area");
            InputSystem.QueueDeltaStateEvent(x,(byte)0);yield return new WaitForSecondsRealtime(0.1f);
            InputSystem.QueueDeltaStateEvent(x,(byte)1);yield return new WaitForSecondsRealtime(0.2f);
            check(motor.InSeabed==initialArea,"Second X press switches back to original water area");
            InputSystem.RemoveDevice(controller);
            var camera=motor.origin.Camera;var tracking=motor.headTracking;
            var position=camera.transform.localPosition;var rotation=camera.transform.localRotation;
            var rootPosition=motor.transform.position;var rootRotation=motor.transform.rotation;
            var world=GameObject.Find("ROVp - Pool").transform;
            var worldPosition=world.position;var worldRotation=world.rotation;
            check(tracking && camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>().enabled,"PICO center-eye TrackedPoseDriver remains enabled for head rotation");
            for(int area=0;area<2;area++)
            {
                camera.transform.position=new Vector3(area*62,2.8f,-8);
                var local=camera.transform.localPosition;
                for(int angle=0;angle<360;angle+=45)
                {
                    tracking.ApplyPose(local,Quaternion.Euler(12,angle,0));
                    var image=Capture(camera);var pixels=image.GetPixels();float sum=0;int black=0;
                    foreach(var pixel in pixels){sum+=pixel.grayscale;if(pixel.maxColorComponent<0.015f)black++;}
                    check(sum/pixels.Length>0.025f && black/(float)pixels.Length<0.3f,$"{(area==0?"Pool":"Seabed")} renders at yaw {angle} degrees (mean={sum/pixels.Length:F3}, black={100f*black/pixels.Length:F1}%)");
                    File.WriteAllBytes(Path.Combine(output,$"360-{area}-{angle:D3}.png"),image.EncodeToPNG());UnityEngine.Object.Destroy(image);
                }
            }
            check(Vector3.Distance(world.position,worldPosition)<0.0001f && Quaternion.Angle(world.rotation,worldRotation)<0.001f &&
                Vector3.Distance(motor.transform.position,rootPosition)<0.0001f && Quaternion.Angle(motor.transform.rotation,rootRotation)<0.001f,
                "360-degree head pose changes move only the camera; world and locomotion origin remain fixed");
            camera.transform.SetLocalPositionAndRotation(position,rotation);
            foreach(var arm in UnityEngine.Object.FindObjectsOfType<PicoArmPoseDriver>())
            {
                arm.ResolveBindings();var rotations=arm.Joints.Select(j=>j.localRotation).ToArray();
                try
                {
                    var start=new Vector3(0.2f,1.2f,0.4f);
                    arm.ApplyControllerPose(start,Quaternion.identity,false,1f/72);
                    arm.ApplyControllerPose(start,Quaternion.identity,true,1f/72);
                    Vector3 before=arm.Wrist.position;
                    arm.Joints[1].localRotation*=Quaternion.AngleAxis(8,Vector3.forward);
                    Vector3 target=arm.Wrist.position;arm.Joints[1].localRotation=rotations[1];
                    Vector3 offset=arm.TrackingOrigin.InverseTransformVector(target-before)/arm.PositionScale;
                    for(int frame=1;frame<=180;frame++)arm.ApplyControllerPose(start+offset*Mathf.Min(1,frame/72f),Quaternion.identity,true,1f/72);
                    float initial=Vector3.Distance(before,target),final=Vector3.Distance(arm.Wrist.position,target);
                    check(initial>0.001f && final<initial*0.3f,$"{arm.name}: controller-relative reachable wrist position converges ({initial:F4} to {final:F4} m)");
                    arm.ApplyControllerPose(start,Quaternion.identity,false,1f/72);arm.ApplyControllerPose(start,Quaternion.identity,true,1f/72);
                    var beforeRotation=arm.Joints[3].localRotation;
                    var twist=Quaternion.AngleAxis(30,arm.TrackingOrigin.InverseTransformDirection(arm.Joints[3].up));
                    for(int frame=0;frame<90;frame++)arm.ApplyControllerPose(start,twist,true,1f/72);
                    float rotated=Quaternion.Angle(beforeRotation,arm.Joints[3].localRotation);
                    check(arm.EnableControllerWristRotation ? rotated>25 && rotated<35 : rotated<.01f,
                        $"{arm.name}: wrist rotation {(arm.EnableControllerWristRotation?"enabled":"locked")} ({rotated:F2} degrees)");
                    var held=arm.Joints.Select(j=>j.localRotation).ToArray();
                    arm.ApplyControllerPose(start+Vector3.one,Quaternion.Euler(50,40,30),false,1f/72);
                    check(arm.Joints.All(j=>Quaternion.Angle(j.localRotation,held[Array.IndexOf(arm.Joints,j)])<0.01f),arm.name+": releasing right grip holds arm pose");
                    foreach(var guard in arm.GetComponentsInChildren<Wa100VisibilityGuard>())
                    {guard.SyncMountedRoot();check(guard.FollowParent && guard.MountError>=0 && guard.MountError<0.001f,arm.name+": WA100 articulation root follows wrist mounting");}
                }
                finally
                {
                    for(int i=0;i<rotations.Length;i++)arm.Joints[i].localRotation=rotations[i];
                    arm.ApplyControllerPose(Vector3.zero,Quaternion.identity,false,1f/72);
                    foreach(var guard in arm.GetComponentsInChildren<Wa100VisibilityGuard>())guard.SyncMountedRoot();
                }
            }
            // Exercise the production JSON receiver over loopback on a dedicated port.
            // Do not inject fabricated samples into the user's live port 5555.
            var fixture=new GameObject("Opt-in HandDriver UDP fixture");var receiver=fixture.AddComponent<Network>();receiver.Port=15555;
            yield return null;
            using(var udp=new UdpClient())
            {
                Send(udp,0);yield return new WaitForSecondsRealtime(0.2f);
                check(receiver.ReceivedPacketCount>0 && receiver.LastRoleName=="UnderwaterCheckRight","HandDriver UDP JSON receiver accepts a real loopback packet");
                var mappers=UnityEngine.Object.FindObjectsOfType<Wa100UdexRealArticulationMapper>();
                var original=mappers.Select(m=>m.Network).ToArray();
                foreach(var mapper in mappers){mapper.Network=receiver;mapper.AutoBind();mapper.CalibrateNeutral();}
                Send(udp,35);yield return new WaitForSecondsRealtime(0.25f);
                foreach(var mapper in mappers)
                {
                    mapper.Apply();
                    check(mapper.enabled && mapper.BoundJointCount==11 && mapper.JointMappings.Count(m=>m.Joint && Mathf.Abs(m.Joint.xDrive.target)>2)>=8,
                        mapper.transform.root.name+": UDP glove channels drive WA100 finger targets");
                }
                for(int i=0;i<mappers.Length;i++){mappers[i].Network=original[i];mappers[i].IsNeutralCalibrated=false;}
            }
            UnityEngine.Object.Destroy(fixture);
        }
        static void Send(UdpClient udp,float bend)
        {
            string parameters=string.Join(",",Enumerable.Range(0,20).Select(i=>"{\"Name\":\"r"+i+"\",\"Value\":"+bend.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}"));
            var bytes=Encoding.UTF8.GetBytes("{\"UnderwaterCheckRight\":{\"Parameter\":["+parameters+"]}}");
            udp.Send(bytes,bytes.Length,new IPEndPoint(IPAddress.Loopback,15555));
        }
        static Texture2D Capture(Camera camera)
        {
            var target=RenderTexture.GetTemporary(640,400,24,RenderTextureFormat.ARGB32);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(640,400,TextureFormat.RGB24,false,true);
            image.ReadPixels(new Rect(0,0,640,400),0,0);image.Apply();RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);return image;
        }
    }
}
