using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterBenchUpgrade
    {
        [MenuItem("Tools/Underwater/Research/25 Build Two-Target Sand Benches")]
        public static void Apply()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=UnderwaterRadarSurveyTools.ScenePath)throw new Exception("Open survey scene outside Play");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/Survey-beforeBench-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            Undo.RegisterFullObjectHierarchyUndo(GameObject.Find("Marine Grasp Targets"),"Build sand grasp bench");
            var sandMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Underwater/Materials/SeabedSand.mat");
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>())
            {
                var previous=task.GetComponentInChildren<UnderwaterGraspBench>();if(previous)Object.DestroyImmediate(previous.gameObject);
                var keep=task.targets.Where(t=>t && (t.name.Contains("Starfish 1")||t.name.Contains("Squid 2"))).ToArray();
                if(keep.Length!=2)throw new Exception("Expected one original starfish and squid per area");
                foreach(var target in task.targets.Except(keep))if(target)Object.DestroyImmediate(target.gameObject);
                task.targets=keep;
                var arm=task.arm;arm.ResolveBindings();
                // Correct the imported mounting frame without rotating the saved mesh pose.
                arm.UseCalibratedBaseYaw=true;arm.BaseYawAxisLocal=arm.Joints[0].InverseTransformDirection(Vector3.up).normalized;
                arm.SmoothTeleoperation=true;
                var ground=GameObject.Find(arm.SeabedArea?"Sand Terrain - Teleportation Area":"Pool Floor - 25 x 50 m").GetComponent<Collider>();
                Vector3 centre=task.PalmPoint+Vector3.back*.14f;
                float floor=ground.Raycast(new Ray(new Vector3(centre.x,11,centre.z),Vector3.down),out var hit,20)?hit.point.y:0;
                var root=new GameObject("Low sand grasp bench");root.transform.SetParent(task.transform,false);
                var bench=root.AddComponent<UnderwaterGraspBench>();bench.task=task;
                var support=GameObject.CreatePrimitive(PrimitiveType.Cube);support.name="Solid support - HAND BLOCKED";support.layer=8;support.transform.SetParent(root.transform);
                support.transform.position=new Vector3(centre.x,floor+.06f,centre.z);support.transform.localScale=new Vector3(.94f,.12f,.70f);
                support.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Underwater/RadarSurvey/ReefTone2.mat");bench.support=support.GetComponent<Collider>();
                var sand=GameObject.CreatePrimitive(PrimitiveType.Cube);sand.name="Soft sand - HAND PASSES - targets supported";sand.layer=11;sand.transform.SetParent(root.transform);
                sand.transform.position=new Vector3(centre.x,floor+.15f,centre.z);sand.transform.localScale=new Vector3(.90f,.06f,.66f);
                sand.GetComponent<Renderer>().sharedMaterial=sandMaterial;bench.sand=sand.GetComponent<BoxCollider>();
                for(int i=0;i<keep.Length;i++)
                {
                    var t=keep[i];var box=t.GetComponent<BoxCollider>();
                    t.transform.rotation=Quaternion.Euler(0,i==0?18:100,0);
                    t.transform.position=new Vector3(centre.x+(i==0?-.21f:.21f),floor+.18f-box.center.y+box.size.y*.5f+.004f,centre.z);
                }
                // Bind articulation roots, not duplicate visual/collision children sharing a name.
                task.Thumb=task.glove.JointMappings.First(m=>m.LinkName=="Thumb_L3").Joint.transform;
                task.Fingers=task.glove.JointMappings.Where(m=>m.LinkName=="Index_Finger_L2"||m.LinkName=="Middle_Finger_L2").Select(m=>m.Joint.transform).ToArray();
                if(task.deliveryZone)task.deliveryZone.position=new Vector3(centre.x+.68f,floor+.015f,centre.z+.25f);
                EditorUtility.SetDirty(task);EditorUtility.SetDirty(arm);
            }
            // Layer name is descriptive only; no global collision-matrix changes.
            var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers=tags.FindProperty("layers");if(string.IsNullOrEmpty(layers.GetArrayElementAtIndex(11).stringValue))layers.GetArrayElementAtIndex(11).stringValue="GraspSand";
            tags.ApplyModifiedPropertiesWithoutUndo();
            var console=Object.FindObjectOfType<UnderwaterTaskConsole>();foreach(var camera in console.views)camera.cullingMask|=1<<11;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Diagnose();
        }
        [MenuItem("Tools/Underwater/Research/24 Diagnose Grasp Bench and Teleoperation")]
        public static void Diagnose()
        {
            var lines=new System.Collections.Generic.List<string>();
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>())
            {
                var a=task.arm;a.ResolveBindings();a.Contact.Hand.SyncMountedRoot();
                lines.Add(a.name+" base scale="+a.ArmBase.lossyScale+" wrist="+a.Wrist.position.ToString("F4")+" palm="+task.PalmPoint.ToString("F4"));
                for(int i=0;i<4;i++)
                {
                    var j=a.Joints[i];var axis=i==0||i==3?Vector3.up:Vector3.forward;
                    Vector3 before=a.Wrist.position;var q=j.localRotation;j.localRotation=q*Quaternion.AngleAxis(.1f,axis);
                    Vector3 numerical=(a.Wrist.position-before)/(.1f*Mathf.Deg2Rad);j.localRotation=q;
                    var analytic=Vector3.Cross(j.TransformDirection(axis),before-j.position);
                    lines.Add($"joint {i} {j.name} pos={j.position:F4} axis={j.TransformDirection(axis):F4} J={analytic:F4} FD={numerical:F4}");
                }
                lines.Add("hand bounds "+string.Join("; ",a.Contact.Hand.GetComponentsInChildren<Renderer>().Select(r=>r.name+" "+r.bounds)));
                lines.Add("thumb="+(task.Thumb?task.Thumb.name:"NULL")+" fingers="+string.Join(",",task.Fingers.Select(f=>f?f.name:"NULL")));
                foreach(var m in task.glove.JointMappings)if(m!=null && m.Joint)lines.Add($"finger {m.LinkName} {m.LowerLimitDegrees}..{m.UpperLimitDegrees} target {m.CurrentTarget} pos={m.Joint.transform.position:F4}");
            }
            Directory.CreateDirectory("Logs/TeleopReview");File.WriteAllLines("Logs/TeleopReview/diagnostic.txt",lines);
        }
    }
}
