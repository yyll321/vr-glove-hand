using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterTranslationOnlyChecks
    {
        const string Key="UnderwaterTranslationOnlyChecks";
        static UnderwaterTranslationOnlyChecks()
        {
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false))
                {SessionState.SetBool(Key,false);new GameObject("Translation-only verification").AddComponent<UnderwaterTranslationOnlyProbe>();}
            };
        }
        [MenuItem("Tools/Underwater/Research/32 Disable Controller Wrist Twist")]
        public static void Apply()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=UnderwaterRadarSurveyTools.ScenePath)throw new Exception("Open survey scene outside Play");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/Survey-beforeTranslationOnly-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            var arms=Object.FindObjectsOfType<PicoArmPoseDriver>();
            if(arms.Length!=2)throw new Exception("Expected pool and seabed arm drivers");
            foreach(var arm in arms){arm.EnableControllerWristRotation=false;EditorUtility.SetDirty(arm);}
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        [MenuItem("Tools/Underwater/Research/33 Verify Translation Without Wrist Twist")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");
            UnderwaterPreviewCompatibility.Verify();SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
    }
    public sealed class UnderwaterTranslationOnlyProbe:MonoBehaviour
    {
        [Serializable]public class Row{public string area,input;public bool contactProtection;public Vector3 mapped,actual;public float wristJointChangeDegrees,error;}
        [Serializable]public class Report{public string time,scope="Synthetic XR position/rotation through production ApplyControllerPose; worn PICO feel not measured.";public bool passed;public string[] checks,errors;public Row[] axes;}
        readonly List<string> checks=new List<string>(),errors=new List<string>();readonly List<Row> rows=new List<Row>();
        void Check(bool ok,string message){(ok?checks:errors).Add(message);}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
        IEnumerator Start()
        {
            Application.logMessageReceived+=Log;yield return new WaitForSeconds(2);
            Object.FindObjectOfType<UnderwaterMotor>().enabled=false;
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>())
            {
                task.enabled=false;task.glove.enabled=false;var arm=task.arm;arm.enabled=false;arm.ResolveBindings();
                foreach(var old in arm.PreviousControllers)if(old)old.enabled=false;
                var saved=arm.Joints.Select(j=>j.localRotation).ToArray();
                var map=arm.MasterSlave;var mode=map.mode;var sand=map.sand;bool protection=arm.PreventSurfacePenetration;
                map.mode=UnderwaterMasterSlaveMapper.Mode.Position;map.sand=null;
                Check(!arm.EnableControllerWristRotation,arm.name+": saved controller wrist rotation disabled");
                Vector3 origin=new Vector3(.2f,1.1f,.4f);
                // Repeat pure X/Y moves while controller orientation changes by
                // large angles. Their translation must match position-only input.
                foreach(bool guard in new[]{false,true})
                foreach(int axis in new[]{0,1})foreach(int sign in new[]{-1,1})
                {
                    arm.PreventSurfacePenetration=guard;Vector3 expected=Vector3.zero;
                    expected[axis]=sign*(.08f-map.deadZoneMetres[axis])/(map.masterHalfRange[axis]-map.deadZoneMetres[axis])*map.slavePositionRange[axis];
                    Vector3 baseline=Vector3.zero;
                    foreach(bool rotate in new[]{false,true})
                    {
                        Restore(arm,saved);Vector3 start=arm.Wrist.position;
                        arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);
                        float worstTwist=0;
                        for(int f=0;f<450;f++)
                        {
                            Vector3 input=Vector3.zero;input[axis]=sign*.08f*Mathf.Min(1,f/120f);
                            var rotation=rotate?Quaternion.Euler(25*Mathf.Sin(f*.03f),60*Mathf.Sin(f*.02f),35*Mathf.Sin(f*.04f)):Quaternion.identity;
                            arm.ApplyControllerPose(origin+input,rotation,true,1f/90);
                            worstTwist=Mathf.Max(worstTwist,Quaternion.Angle(saved[3],arm.Joints[3].localRotation));
                        }
                        Vector3 actual=arm.Wrist.position-start;
                        Vector3 goal=arm.TrackingOrigin.rotation*expected;
                        rows.Add(new Row{area=arm.name,input=(axis==0?"X":"Y")+(sign<0?"-":"+")+(rotate?" + rotation":""),contactProtection=guard,mapped=map.Target-start,actual=actual,wristJointChangeDegrees=worstTwist,error=Vector3.Distance(goal,actual)});
                        Check(worstTwist<.01f,arm.name+": "+rows.Last().input+" independent wrist joint holds, max="+worstTwist.ToString("F5")+" deg");
                        Check(rows.Last().error<.006f && Vector3.Dot(actual,goal)>0,arm.name+": "+rows.Last().input+" translates in requested direction, error="+rows.Last().error.ToString("F5")+" m, guard="+guard);
                        if(!rotate)baseline=actual;
                        else Check(Vector3.Distance(actual,baseline)<.0001f,arm.name+": controller rotation cannot alter "+rows.Last().input+" translation");
                    }
                }
                Restore(arm,saved);Vector3 still=arm.Wrist.position;
                arm.ApplyControllerPose(origin,Quaternion.identity,true,1f/90);
                for(int f=0;f<200;f++)arm.ApplyControllerPose(origin,Quaternion.Euler(f*.3f,f*.4f,f*.2f),true,1f/90);
                Check(Vector3.Distance(still,arm.Wrist.position)<.00001f && Quaternion.Angle(saved[3],arm.Joints[3].localRotation)<.01f,arm.name+": rotating controller in place produces no arm/wrist movement");
                Restore(arm,saved);arm.PreventSurfacePenetration=protection;map.sand=sand;map.mode=mode;
                var bench=task.GetComponentInChildren<UnderwaterGraspBench>();bench.ConfigureFiltering();arm.Contact.BeginMotion();
                float bottom=arm.Contact.Hand.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                Vector3 inSand=arm.Wrist.position+Vector3.up*(bench.SurfaceHeight-.025f-bottom);
                Check(!arm.Contact.Blocked(inSand,arm.Wrist.rotation),arm.name+": original sand entry retained");
                Check(arm.Contact.Blocked(inSand-Vector3.up*(bench.SurfaceHeight-bench.support.bounds.max.y),arm.Wrist.rotation),arm.name+": original solid support block retained");
                Check(arm.Contact.SweepBlocked(arm.Wrist.position,arm.Wrist.rotation,inSand-Vector3.up*.2f),arm.name+": solid sweep retained");
            }
            Directory.CreateDirectory("Logs/TranslationOnly");
            File.WriteAllText("Logs/TranslationOnly/results.json",JsonUtility.ToJson(new Report{time=DateTime.Now.ToString("O"),passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray(),axes=rows.ToArray()},true));
            Application.logMessageReceived-=Log;EditorApplication.isPlaying=false;
        }
        static void Restore(PicoArmPoseDriver arm,Quaternion[] saved)
        {for(int i=0;i<4;i++)arm.Joints[i].localRotation=saved[i];arm.Contact.Hand.SyncMountedRoot();Physics.SyncTransforms();arm.ApplyControllerPose(Vector3.zero,Quaternion.identity,false,1f/90);}
    }
}
