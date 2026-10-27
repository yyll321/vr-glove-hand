using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    // The manifest restores the previous random XY layout, with low vertical profiles.
    public static class UnderwaterDenseSurveyUpgrade
    {
        const string LayoutPath="Assets/Underwater/RadarSurvey/SurveyLayout.json";
        const string GeneratedName="Generated Survey Reefs - random low layout";
        [Serializable] public sealed class Item
        {public string name,mesh,material;public float[] position,scale;public float yaw,radius;public int zone;}
        [Serializable] public sealed class Layout
        {public int seed;public float[] seabedSize;public float protectedRovRadius;public Item[] items;}
        [MenuItem("Tools/Underwater/Research/20 Restore Low Random Reefs and Fixed Controls")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!=UnderwaterRadarSurveyTools.ScenePath)
                throw new Exception("Open the radar survey scene and exit Play first");
            var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            if(layout.items.Length!=60 || layout.items.Any(i=>!i.name.StartsWith("Reef ")))
                throw new Exception("Expected the previous 60 random reefs, without manufactured obstacles");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),"Assets/Underwater/Backups/Survey-beforeLowRandom-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            var terrain=GameObject.Find("Sand Terrain - Teleportation Area").GetComponent<Collider>();
            foreach(var item in layout.items)
            {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(item.mesh);
                item.scale[1]=Mathf.Min(item.scale[1],.72f,.95f/mesh.bounds.size.y);
                float floor=terrain.Raycast(new Ray(new Vector3(item.position[0],12,item.position[2]),Vector3.down),out var hit,20)?hit.point.y:0;
                item.position[1]=floor-mesh.bounds.min.y*item.scale[1]-.22f*item.scale[1];
            }
            foreach(string name in new[]{GeneratedName,"Generated Survey Reefs - dense layout","Generated Survey Reefs - seed 20261027","Pool ROV Inspection Obstacles"})
            {var old=GameObject.Find(name);if(old)Undo.DestroyObjectImmediate(old);}
            var root=new GameObject(GeneratedName);Undo.RegisterCreatedObjectUndo(root,"Restore low random reefs");
            foreach(var item in layout.items)
            {
                var go=new GameObject(item.name);Undo.RegisterCreatedObjectUndo(go,"Restore reef");go.layer=8;go.transform.SetParent(root.transform,false);
                go.transform.position=new Vector3(item.position[0],item.position[1],item.position[2]);
                go.transform.rotation=Quaternion.Euler(0,item.yaw,0);go.transform.localScale=new Vector3(item.scale[0],item.scale[1],item.scale[2]);
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(item.mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(item.material);go.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
            var sea=GameObject.Find("Seabed Fluid Volume").GetComponent<UnderwaterFluidVolume>();
            sea.obstacles=root.GetComponentsInChildren<Collider>();EditorUtility.SetDirty(sea);
            var pool=GameObject.Find("Pool Fluid Volume").GetComponent<UnderwaterFluidVolume>();
            pool.obstacles=Array.Empty<Collider>();EditorUtility.SetDirty(pool);
            var motor=Object.FindObjectOfType<UnderwaterMotor>();motor.ConfigureInputOwnership();
            foreach(var b in motor.origin.GetComponentsInChildren<Behaviour>(true))EditorUtility.SetDirty(b);
            File.WriteAllText(LayoutPath,JsonUtility.ToJson(layout,true));
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.Refresh();
            AuditInputs();Debug.Log("Saved 60 low random reefs; pipes/rings removed; fixed single-purpose inputs");
        }
        [MenuItem("Tools/Underwater/Research/22 Audit Fixed Input Ownership")]
        public static void AuditInputs()
        {
            var motor=Object.FindObjectOfType<UnderwaterMotor>();
            var lines=Object.FindObjectsOfType<MonoBehaviour>(true).Where(b=>b && (b.GetType().FullName.Contains("Interaction.Toolkit") || b is PicoArmPoseDriver || b is UnderwaterMotor || b is Wa100UdexRealArticulationMapper))
                .Select(b=>b.GetType().FullName+" | "+b.name+" | enabled="+b.enabled+" | active="+b.gameObject.activeInHierarchy);
            Directory.CreateDirectory("Logs/RadarReview");File.WriteAllText("Logs/RadarReview/input-ownership.txt",string.Join("\n",lines));
            if(motor.origin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.XRBaseInteractor>(true).Any(i=>i.enabled))
                throw new Exception("An XR interactor still shares the reserved hand controls");
        }
        public static void RepairHandClearance(){Apply();}
    }
}
