using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterHybridUpgrade
    {
        [MenuItem("Tools/Underwater/Research/26 Hybrid Teleoperation and Compressible Sand")]
        public static void Apply()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=UnderwaterRadarSurveyTools.ScenePath)throw new Exception("Open survey scene outside Play");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/Survey-beforeHybrid-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            const string meshPath="Assets/Underwater/MarineAssets/StarfishThickZ.asset";
            var thick=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(!thick)
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Underwater/MarineAssets/Starfish.obj").GetComponentInChildren<MeshFilter>().sharedMesh;
                thick=Object.Instantiate(source);thick.name="Starfish - 75 mm local Z thickness";
                float gain=.075f/source.bounds.size.y;
                thick.vertices=source.vertices.Select(v=>new Vector3(v.x,-v.z,v.y*gain)).ToArray();
                thick.RecalculateNormals();thick.RecalculateBounds();thick.RecalculateTangents();
                AssetDatabase.CreateAsset(thick,meshPath);
            }
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>())
            {
                if(task.targets.Length!=2)throw new Exception("Preserve exactly two existing targets per area");
                var arm=task.arm;arm.ResolveBindings();
                var bench=task.GetComponentInChildren<UnderwaterGraspBench>();
                if(!bench || !bench.sand || !bench.support)throw new Exception("Existing bench required");
                // The old bench was centred 140 mm beyond the open palm. A
                // stiff, closing hand has less forward reach than a folding hand.
                // Bring the entire fixture into its lower-side grasp workspace.
                Vector3 approachShift=Vector3.forward*(task.PalmPoint.z-bench.sand.bounds.center.z);
                bench.transform.position+=approachShift;
                foreach(var t in task.targets)t.transform.position+=approachShift;
                float supportTop=bench.support.bounds.max.y;
                var sand=bench.sand;var oldScale=sand.transform.localScale;
                const float depth=.16f;
                sand.transform.localScale=new Vector3(oldScale.x,depth,oldScale.z);
                var p=sand.transform.position;p.y=supportTop+depth*.5f;sand.transform.position=p;
                // Physical support surface sits 12 mm below the visible sand.
                // Robot/sand pairs are ignored; the logical layer supplies drag.
                sand.center=new Vector3(0,-.006f/depth,0);sand.size=new Vector3(1,1-.012f/depth,1);
                bench.sandDepth=depth;bench.visualSurfaceOffset=.012f;
                sand.name="Compressible sand - finger resistance - targets supported";
                foreach(var t in task.targets)
                {
                    var box=t.GetComponent<BoxCollider>();
                    if(t.name.Contains("Starfish"))
                    {
                        var visual=t.GetComponentInChildren<MeshFilter>();visual.sharedMesh=thick;
                        visual.transform.localRotation=Quaternion.Euler(-90,0,0);
                        visual.transform.localScale=Vector3.one;
                        box.center=Vector3.zero;box.size=new Vector3(thick.bounds.size.x+.003f,.078f,thick.bounds.size.y+.003f);
                    }
                    var point=t.transform.position;point.y=supportTop+depth-.012f+box.size.y*.5f-box.center.y+.002f;t.transform.position=point;
                    EditorUtility.SetDirty(t);EditorUtility.SetDirty(box);
                }
                var mapping=arm.GetComponent<UnderwaterMasterSlaveMapper>()??arm.gameObject.AddComponent<UnderwaterMasterSlaveMapper>();
                mapping.task=task;mapping.sand=bench;arm.MasterSlave=mapping;
                arm.SmoothTeleoperation=true; // Retain damped IK, joint weighting and acceleration limits.
                var hold=task.glove.GetComponent<Wa100JointPoseHold>()??task.glove.gameObject.AddComponent<Wa100JointPoseHold>();
                hold.mapper=task.glove;hold.sand=bench;hold.contact=arm.Contact;
                task.glove.UseArticulationDriveLimits=false;task.glove.ConfigureDriveStrength=false;
                task.requireEnvelope=true;
                if(task.deliveryZone)task.deliveryZone.position=new Vector3(bench.support.bounds.center.x+.68f,bench.support.bounds.min.y+.015f,bench.support.bounds.center.z+.25f);
                EditorUtility.SetDirty(bench);EditorUtility.SetDirty(arm);EditorUtility.SetDirty(mapping);EditorUtility.SetDirty(hold);EditorUtility.SetDirty(task.glove);EditorUtility.SetDirty(task);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("Saved hybrid mapping, 75 mm starfish, configuration brake and 160 mm resistive sand. Existing contact/haptic components retained.");
        }
    }
}
