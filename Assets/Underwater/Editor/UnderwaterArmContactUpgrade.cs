using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterArmContactUpgrade
    {
        [MenuItem("Tools/Underwater/Research/9 Apply WA100 Contact and Damped IK")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play before applying arm contact.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=UnderwaterResearchSceneBuilder.ScenePath)throw new Exception("Open the editable underwater scene first.");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/UnderwaterResearch-beforeContact-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            foreach(var arm in Object.FindObjectsOfType<PicoArmPoseDriver>())
            {
                var guard=arm.Wrist.GetComponentInChildren<Wa100VisibilityGuard>(true);
                if(!guard)throw new Exception("WA100 mounting guard missing: "+arm.name);
                guard.DisableColliders=false;guard.EnableConvexContactColliders=true;
                guard.FollowParent=true;guard.Apply();
                var mapper=guard.GetComponent<Wa100UdexRealArticulationMapper>();
                mapper.DriveStiffness=400;mapper.DriveDamping=30;mapper.DriveForceLimit=2;
                mapper.AutoBind();EditorUtility.SetDirty(mapper);
                var contact=arm.GetComponent<Wa100SurfaceContact>();
                if(!contact)contact=arm.gameObject.AddComponent<Wa100SurfaceContact>();
                contact.Arm=arm;contact.Hand=guard;contact.SurfaceMask=1<<8;contact.Skin=0.003f;
                arm.Contact=contact;arm.ContactMask=1<<8;arm.PreventSurfacePenetration=true;
                arm.Damping=0.035f;arm.PositionGain=0.8f;arm.TargetSmoothingSeconds=0.035f;
                EditorUtility.SetDirty(arm);EditorUtility.SetDirty(guard);EditorUtility.SetDirty(contact);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Audit();
        }

        public static void Audit()
        {
            Directory.CreateDirectory("Logs/UnderwaterArm");
            var lines=Object.FindObjectsOfType<PicoArmPoseDriver>().Select(arm=>
            {
                var guard=arm.Wrist.GetComponentInChildren<Wa100VisibilityGuard>(true);
                var mesh=guard.GetComponentsInChildren<MeshFilter>(true);
                var colliders=guard.GetComponentsInChildren<Collider>(true);
                var report=$"{arm.name}: wrist={arm.Wrist.position:F4}, hand={guard.transform.position:F4}, "+
                    $"visual shapes={mesh.Length}, enabled colliders={colliders.Count(c=>c.enabled)}, "+
                    $"contact={(bool)arm.Contact}, skin={arm.Contact?.Skin}, scale={guard.transform.lossyScale}";
                foreach(var renderer in guard.GetComponentsInChildren<Renderer>(true))
                    report+=$"\n  {renderer.name}: min={renderer.bounds.min:F4}, max={renderer.bounds.max:F4}";
                return report;
            });
            File.WriteAllText("Logs/UnderwaterArm/audit.txt",string.Join("\n",lines));
        }
    }
}
