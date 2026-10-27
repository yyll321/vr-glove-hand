using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterRadarSurveyTools
    {
        public const string ScenePath="Assets/Underwater/Scenes/UnderwaterRadarSurveyEditable.unity";
        const string LayoutPath="Assets/Underwater/RadarSurvey/SurveyLayout.json";
        const string GeneratedName="Generated Survey Reefs - seed 20261027";
        [Serializable] sealed class Item {public string name,mesh,material;public float[] position,scale;public float yaw;}
        [Serializable] sealed class Layout {public int seed;public Item[] items;}

        [MenuItem("Tools/Underwater/Research/14 Open Geometry Radar Survey Scene")]
        public static void Open()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play before opening a scene");
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Tools/Underwater/Research/15 Restore Generated Reefs From Manifest")]
        public static void RestoreLayout()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!=ScenePath)
                throw new Exception("Open the Geometry Radar Survey scene and exit Play first");
            var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            if(layout==null || layout.items==null || layout.items.Length!=62)throw new Exception("Invalid survey manifest");
            // Resolve every asset before touching the current layout.
            foreach(var item in layout.items)
                if(!AssetDatabase.LoadAssetAtPath<Mesh>(item.mesh) || !AssetDatabase.LoadAssetAtPath<Material>(item.material))
                    throw new Exception("Missing asset for "+item.name);
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),"Assets/Underwater/Backups/RadarSurvey-beforeLayout-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Restore generated survey reefs");
            var old=GameObject.Find(GeneratedName);if(old)Undo.DestroyObjectImmediate(old);
            var root=new GameObject(GeneratedName);Undo.RegisterCreatedObjectUndo(root,"Restore survey root");
            foreach(var item in layout.items)
            {
                var go=new GameObject(item.name);Undo.RegisterCreatedObjectUndo(go,"Restore reef");go.layer=8;go.transform.SetParent(root.transform,false);
                go.transform.position=new Vector3(item.position[0],item.position[1],item.position[2]);
                go.transform.rotation=Quaternion.Euler(0,item.yaw,0);go.transform.localScale=new Vector3(item.scale[0],item.scale[1],item.scale[2]);
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(item.mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(item.material);
                go.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
            var volume=GameObject.Find("Seabed Fluid Volume").GetComponent<UnderwaterFluidVolume>();
            Undo.RecordObject(volume,"Restore flow obstacle references");volume.obstacles=root.GetComponentsInChildren<Collider>();EditorUtility.SetDirty(volume);
            Undo.CollapseUndoOperations(group);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("Restored 62 generated targets from seed "+layout.seed+"; scene remains editable and unsaved");
        }
        [MenuItem("Tools/Underwater/Research/16 Validate Geometry Radar Survey")]
        public static void Audit()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play before the audit");
            if(SceneManager.GetActiveScene().path!=ScenePath)throw new Exception("Open the Geometry Radar Survey scene first");
            int checks=0;Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception(message);checks++;};
            var region=Object.FindObjectOfType<UnderwaterRegionSettings>();check(region && region.seabedSize==new Vector2(50,80),"Region size mismatch");
            var root=GameObject.Find(GeneratedName);check(root,"Missing generated reef root");
            var colliders=root.GetComponentsInChildren<MeshCollider>();check(colliders.Length==62,"Expected 62 geometric targets");
            foreach(var collider in colliders)check(collider.sharedMesh==collider.GetComponent<MeshFilter>().sharedMesh && !collider.convex,"Radar collider differs from visible reef");
            var radars=Object.FindObjectsOfType<UnderwaterGeometryRadar>();check(radars.Length==2,"Expected one radar per reference ROV");
            foreach(var radar in radars)
            {
                check(radar.vehicle && radar.ignoreRoot==radar.vehicle && radar.motor,"Radar references missing");
                check(!radar.scanning,"Radar should await local profile selection / Start");
                check(radar.GetComponent<UnderwaterRadarRosInterface>().radar==radar,"ROS contract missing");
            }
            foreach(var h in Object.FindObjectsOfType<UnderwaterHydrodynamics>())
                check(!h.applyForces && h.surfaceBuoyancy && h.surfaceBuoyancy.displacementHull,"Unsafe or incomplete displacement model");
            UnderwaterRadarSurveyChecks.Run();
            foreach(string path in AssetDatabase.FindAssets("t:Shader",new[]{"Assets/Underwater/Shaders"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
                check(shader && !ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Shader import error: "+path);
            }
            Directory.CreateDirectory("Logs/RadarReview");File.WriteAllText("Logs/RadarReview/editor-audit.txt","PASS "+checks+" scene/import checks; mathematical and Physics.Raycast checks passed. "+DateTime.Now.ToString("O")+"\nPlay mode, HMD rendering and performance are separate validations.");
        }
        [MenuItem("Tools/Underwater/Research/17 Build Geometry Radar Survey Windows")]
        public static void Build()
        {
            Open();if(SceneManager.GetActiveScene().path!=ScenePath)return;Audit();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Directory.CreateDirectory("Builds/UnderwaterRadarSurvey");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},target=BuildTarget.StandaloneWindows64,
                locationPathName="Builds/UnderwaterRadarSurvey/UnderwaterRadarSurvey.exe",options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Radar survey build failed");
        }
        [MenuItem("Tools/Underwater/Research/19 Bake Expanded Seabed Water Reflection")]
        public static void BakeReflection()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!=ScenePath)throw new Exception("Open the radar survey scene and exit Play first");
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Underwater/RadarSurvey/SurveyWater.mat");
            var caustics=Object.FindObjectOfType<UnderwaterCaustics>();if(caustics)caustics.RenderMaps(7.5f);
            var go=new GameObject("Temporary survey reflection capture"){hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();camera.enabled=false;camera.cameraType=CameraType.Reflection;
            camera.stereoTargetEye=StereoTargetEyeMask.None;camera.fieldOfView=90;camera.aspect=1;camera.nearClipPlane=.03f;camera.farClipPlane=150;
            camera.clearFlags=CameraClearFlags.Skybox;camera.cullingMask=(1<<0)|(1<<8)|(1<<9);camera.allowHDR=false;camera.allowMSAA=false;camera.useOcclusionCulling=false;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;data.allowXRRendering=false;
            var previous=RenderTexture.active;var target=RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var cube=new Cubemap(256,TextureFormat.RGBAHalf,true){name="Expanded survey seabed water environment"};
            Vector3[] forward={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            Vector3[] up={Vector3.up,Vector3.up,Vector3.back,Vector3.forward,Vector3.up,Vector3.up};
            var centre=new Vector3(62,9.6f,0);
            try
            {
                for(int face=0;face<6;face++)
                {
                    camera.transform.SetPositionAndRotation(centre,Quaternion.LookRotation(forward[face],up[face]));
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
                    var image=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
                    try
                    {
                        image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();var pixels=image.GetPixels();var flipped=new Color[pixels.Length];
                        for(int row=0;row<256;row++)Array.Copy(pixels,row*256,flipped,(255-row)*256,256);
                        cube.SetPixels(flipped,(CubemapFace)face);
                    }
                    finally{Object.DestroyImmediate(image);}
                }
                cube.Apply(true,false);const string path="Assets/Underwater/RadarSurvey/SurveyWaterEnvironment.asset";
                var saved=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if(saved){EditorUtility.CopySerialized(cube,saved);Object.DestroyImmediate(cube);EditorUtility.SetDirty(saved);}else{AssetDatabase.CreateAsset(cube,path);saved=cube;}
                material.SetTexture("_EnvironmentCube",saved);material.SetFloat("_EnvironmentReady",1);material.SetVector("_ReflectionCenter",centre);
                material.SetVector("_ReflectionMin",new Vector3(37,0,-40));material.SetVector("_ReflectionMax",new Vector3(87,10,40));
                EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(go);}
        }
    }
}
