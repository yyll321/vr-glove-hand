using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterGraspChecks
    {
        const string Key="MarineGraspSmoke";static double began;static readonly List<string> errors=new List<string>();
        static UnderwaterGraspChecks(){EditorApplication.playModeStateChanged+=Mode;EditorApplication.update+=Tick;}
        public static void Run(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");UnderwaterPreviewCompatibility.Verify();SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;}
        static void Mode(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){began=EditorApplication.timeSinceStartup;errors.Clear();Application.logMessageReceived+=Log;}
            if(state==PlayModeStateChange.ExitingPlayMode){SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;}
        }
        static void Log(string message,string stack,LogType type){if(type==LogType.Error || type==LogType.Exception)errors.Add(message);}
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||began==0||EditorApplication.timeSinceStartup-began<3)return;
            began=0;Directory.CreateDirectory("Logs/GraspReview");var checks=new List<string>();
            Action<bool,string> check=(ok,message)=>{if(ok)checks.Add(message);else errors.Add(message);};
            try
            {
                var console=Object.FindObjectOfType<UnderwaterTaskConsole>();var motor=console.motor;
                check(console.views.Length==6 && console.views.All(c=>!c.enabled && c.stereoTargetEye==StereoTargetEyeMask.None),"Six cameras remain manual, non-stereo; one shared preview");
                check(console.tasks.Length==2 && console.tasks.All(t=>t.targets.Length==5),"Five marine targets in each area");
                for(int region=0;region<2;region++)
                {
                    motor.transform.position=region==0?new Vector3(0,0,-8):new Vector3(62,.2f,-8);Physics.SyncTransforms();
                    for(int view=0;view<3;view++)
                    {
                        console.SelectView(view);console.Capture();
                        Save(console.Preview,"Logs/GraspReview/"+(region==0?"pool":"sea")+"-"+view+".png",out float variance);
                        check(variance>.00001f,"Camera image has nonuniform scene content: region "+region+", view "+view);
                    }
                    var radar=console.ActiveRadar;radar.SetGraspScan(true);radar.RefreshMount();
                    check(radar.graspScan && radar.Selected.name=="Grasp close" && Vector3.Dot(radar.transform.forward,Vector3.down)>.999f,"Wrist range mode uses its own downward origin");
                    radar.StartScan();for(int i=0;i<16 && radar.Latest==null;i++)radar.SendMessage("TickScan");
                    check(radar.Latest!=null && radar.Latest.scanMode=="wrist_down" && radar.Latest.tiltDegrees==-90 && Vector3.Distance(radar.Latest.origin,radar.transform.position)<.0001f,"Wrist scan exports its actual origin, downward tilt and distinct mode");
                    radar.StopScan();
                    radar.SetGraspScan(false);
                    check(radar.Latest==null && radar.RangeImage==null,"Mode switch hides the old image until a complete fresh scan");
                    var task=console.ActiveTask;TestGrasp(task,check);TestArm(task.arm,check);
                }
                check(console.Panel!=null && console.panelWidth<=.5f && console.headOffset.x<0 && console.headOffset.y>0,"Panel stays compact in upper-left peripheral view");
                var c=motor.Controllers;bool before=console.visible;int count=console.CaptureCount;
                console.visible=false;console.SendMessage("LateUpdate");check(console.CaptureCount==count,"Hidden panel stops extra camera renders");console.visible=before;
                // Keep an overview image for review without changing the saved scene.
                console.SelectView(2);console.Capture();
            }
            catch(Exception exception){errors.Add(exception.ToString());}
            var result=new Report{passed=errors.Count==0,time=DateTime.Now.ToString("O"),checks=checks.ToArray(),errors=errors.ToArray()};
            File.WriteAllText("Logs/GraspReview/play-checks.json",JsonUtility.ToJson(result,true));EditorApplication.isPlaying=false;
        }
        static void TestGrasp(UnderwaterGraspTask task,Action<bool,string> check)
        {
            var t=task.targets[0];var old=t.transform.position;var rotation=t.transform.rotation;bool enabled=task.enabled;task.enabled=false;
            try
            {
                t.Body.isKinematic=true;t.transform.position=task.PalmPoint;Physics.SyncTransforms();
                task.Process(0,.02f,true,true);task.Process(1,.10f,true,true);check(!task.Held,"Near target requires stable closure interval");
                task.Process(1,.15f,true,true);check(task.Held==t && t.Held && t.transform.IsChildOf(task.arm.Wrist),"Near closed hand attaches target with preserved pose");
                t.transform.position+=Vector3.up*.25f;task.Process(1,.02f,true,true);check(task.LiftVerified,"Lift threshold verified independently of attach");
                t.transform.position=task.deliveryZone.position+Vector3.up*.10f;task.Process(0,.02f,true,true);
                check(!task.Held && !t.Body.isKinematic && t.Delivered && t.DeliveryCount==1,"Opening releases dynamic object and marks delivery after verified lift");
                var other=task.targets[1];other.Body.isKinematic=true;other.transform.position=task.PalmPoint+Vector3.right*.8f;Physics.SyncTransforms();
                task.Process(0,.02f,true,true);task.Process(1,1,true,true);check(!task.Held,"Far closure cannot grasp remotely");
                other.transform.position=task.PalmPoint;Physics.SyncTransforms();task.Process(0,.02f,true,true);task.Process(1,1,false,true);check(!task.Held,"Stale glove cannot attach a target");
                task.Process(1,1,true,false);check(!task.Held,"Glove mode requires opposing thumb and finger contact");
                task.Process(1,1,true,true);other.transform.position=task.PalmPoint+Vector3.up*.25f;task.Process(1,.02f,true,true);
                other.transform.position=task.deliveryZone.position+Vector3.up*.1f;task.Process(0,.02f,false,false);
                check(!task.Held && !other.Delivered && !other.Body.isKinematic,"Stale data releases a lifted target without awarding delivery");
                other.transform.position=task.PalmPoint;Physics.SyncTransforms();task.Process(0,.02f,true,true);task.Process(1,1,true,true);
                other.transform.position=task.PalmPoint+Vector3.up*.25f;task.Process(1,.02f,true,true);other.transform.position=task.deliveryZone.position+Vector3.up*.1f;
                task.Release("region changed");check(!task.Held && !other.Delivered,"Region-change release cannot award delivery");
                other.Body.isKinematic=false;
            }
            finally{if(task.Held)task.Release("test cleanup");t.transform.SetPositionAndRotation(old,rotation);t.Body.isKinematic=false;task.enabled=enabled;}
        }
        static void TestArm(PicoArmPoseDriver arm,Action<bool,string> check)
        {
            var guard=arm.WholeArmContact;var rotations=arm.Joints.Select(j=>j.localRotation).ToArray();var oldMask=guard.surfaceMask;var handMask=arm.Contact.SurfaceMask;
            var fixture=new GameObject("Arm-only thin floor regression"){layer=12};var slab=fixture.AddComponent<BoxCollider>();slab.size=new Vector3(1,.02f,1);
            var capture=typeof(PicoArmPoseDriver).GetMethod("CaptureJointState",BindingFlags.Instance|BindingFlags.NonPublic);
            var constrain=typeof(PicoArmPoseDriver).GetMethod("ApplyContactConstraint",BindingFlags.Instance|BindingFlags.NonPublic);
            try
            {
                arm.ResolveBindings();guard.Rebuild();check(guard.ShapeCount>arm.Contact.ShapeCount,"Whole-arm proxy set includes links beyond WA100");
                var filters=guard.protectedRoot.GetComponentsInChildren<MeshFilter>().Where(f=>!f.transform.IsChildOf(arm.Contact.Hand.transform) && f.sharedMesh).ToArray();
                var starts=filters.Select(f=>f.GetComponent<Renderer>().bounds).ToArray();float best=0,angle=0;int chosen=-1;
                foreach(float a in new[]{-65f,65f})
                {
                    arm.Joints[1].localRotation=rotations[1]*Quaternion.AngleAxis(a,Vector3.forward);
                    for(int i=0;i<filters.Length;i++)
                    {
                        float drop=starts[i].min.y-filters[i].GetComponent<Renderer>().bounds.min.y;
                        if(drop>best){best=drop;chosen=i;angle=a;}
                    }
                }
                for(int i=0;i<4;i++)arm.Joints[i].localRotation=rotations[i];
                check(chosen>=0 && best>.05f,"Unconstrained arm-only link has a downward trajectory");if(chosen<0)return;
                var selected=starts[chosen];fixture.transform.position=new Vector3(selected.center.x,selected.min.y-.04f,selected.center.z);Physics.SyncTransforms();
                guard.surfaceMask=1<<12;arm.Contact.SurfaceMask=1<<12;arm.Contact.BeginMotion();guard.BeginMotion();capture.Invoke(arm,null);
                arm.Joints[1].localRotation=rotations[1]*Quaternion.AngleAxis(angle,Vector3.forward);constrain.Invoke(arm,null);
                float actual=Quaternion.Angle(rotations[1],arm.Joints[1].localRotation);
                check(arm.SurfaceContact && actual<Mathf.Abs(angle)-.1f && guard.LastSurface==fixture.name,"Elbow/link motion is blocked by thin floor without relying on wrist-only collision");
            }
            finally
            {
                for(int i=0;i<4;i++)arm.Joints[i].localRotation=rotations[i];guard.surfaceMask=oldMask;arm.Contact.SurfaceMask=handMask;
                arm.Contact.Hand.SyncMountedRoot();Object.DestroyImmediate(fixture);guard.BeginMotion();
            }
        }
        static void Save(RenderTexture target,string path,out float variance)
        {
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            var values=image.GetPixels();float mean=values.Average(c=>c.grayscale);variance=values.Average(c=>(c.grayscale-mean)*(c.grayscale-mean));
            RenderTexture.active=previous;Object.DestroyImmediate(image);
        }
        [Serializable]class Report{public bool passed;public string time;public string[] checks,errors;public string scope="Unity Editor geometry / task / render regression. No PICO wearing or live glove verification.";}
    }
}
