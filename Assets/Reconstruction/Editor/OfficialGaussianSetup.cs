using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GaussianSplatting.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OfficialAsset=GaussianSplatting.Runtime.GaussianSplatAsset;
using OfficialRenderer=GaussianSplatting.Runtime.GaussianSplatRenderer;
using Object=UnityEngine.Object;

namespace UnderwaterReconstruction.Editor
{
    public static class OfficialGaussianSetup
    {
        public const string DataRoot="Assets/Reconstruction/Data/OfficialDemo";
        const string Package="Packages/org.nesnausk.gaussian-splatting";
        [MenuItem("Tools/Reconstruction/4 Upgrade scene with official 3DGS")]
        public static void Upgrade()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode before editing the scene.");
            BackupDirtyScenes();
            if(File.Exists(ReconstructionSceneBuilder.ScenePath))
            {
                if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=ReconstructionSceneBuilder.ScenePath)
                    EditorSceneManager.OpenScene(ReconstructionSceneBuilder.ScenePath);
            }
            else ReconstructionSceneBuilder.Create();
            var lab=Object.FindObjectOfType<ReconstructionLab>();
            if(!lab)throw new Exception("Missing reconstruction lab.");
            // The existing URDF editor utility used to inject a depth-10 MainCamera into every
            // new scene. Preserve its objects, but keep this lab's XR camera as the driving view.
            var preview=GameObject.Find("WA100 Front Camera");
            if(preview)
            {
                var previewCamera=preview.GetComponent<Camera>();
                if(previewCamera){previewCamera.enabled=false;preview.tag="Untagged";EditorUtility.SetDirty(previewCamera);}
                var listener=preview.GetComponent<AudioListener>();if(listener)listener.enabled=false;
            }
            var rendererData=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Reconstruction/Settings/ReconstructionRenderer.asset");
            Type featureType=typeof(OfficialRenderer).Assembly.GetType("GaussianSplatting.Runtime.GaussianSplatURPFeature",true);
            if(!rendererData.rendererFeatures.Any(f=>f && f.GetType()==featureType))
            {
                var feature=(ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
                feature.name="Official Gaussian Splatting (URP14)";feature.Create();
                AssetDatabase.AddObjectToAsset(feature,rendererData);rendererData.rendererFeatures.Add(feature);EditorUtility.SetDirty(rendererData);
            }
            var demo=AssetDatabase.LoadAssetAtPath<OfficialAsset>(DataRoot+"/synthetic_demo.asset");
            if(!demo)
            {
                Directory.CreateDirectory("Logs/Reconstruction");
                WriteSyntheticPly(lab.gaussian.asset,"Logs/Reconstruction/synthetic_demo.ply");
                demo=ImportOfficialPly("Logs/Reconstruction/synthetic_demo.ply",DataRoot);
            }
            var backend=lab.GetComponent<OfficialGaussianBackend>();
            if(!backend)backend=lab.gameObject.AddComponent<OfficialGaussianBackend>();
            if(!backend.rendererComponent)
            {
                var root=new GameObject("Official 3DGS - compressed assets and GPU radix sort");root.SetActive(false);
                var renderer=root.AddComponent<OfficialRenderer>();
                renderer.m_Asset=demo;renderer.m_SHOrder=1;renderer.m_SortNthFrame=1;
                renderer.m_ShaderSplats=Shader.Find("Gaussian Splatting/Render Splats");
                renderer.m_ShaderComposite=Shader.Find("Hidden/Gaussian Splatting/Composite");
                renderer.m_ShaderDebugPoints=Shader.Find("Gaussian Splatting/Debug/Render Points");
                renderer.m_ShaderDebugBoxes=Shader.Find("Gaussian Splatting/Debug/Render Boxes");
                renderer.m_CSSplatUtilities=AssetDatabase.LoadAssetAtPath<ComputeShader>(Package+"/Shaders/SplatUtilities.compute");
                // Both import routes use original Z-up source coordinates mapped to Unity (x,z,y).
                root.transform.localRotation=Quaternion.Euler(-90,0,0);root.transform.localScale=new Vector3(1,-1,1);
                backend.rendererComponent=renderer;backend.syntheticDemo=true;
            }
            lab.official=backend;lab.preferOfficial=true;lab.Select(false);
            EditorUtility.SetDirty(lab);EditorUtility.SetDirty(backend);
            EditorSceneManager.MarkSceneDirty(lab.gameObject.scene);
            EditorSceneManager.SaveScene(lab.gameObject.scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/Reconstruction");
            File.WriteAllText("Logs/Reconstruction/official-integration.txt",$"Installed upstream v1.0.0 (0a30eb3139ed4b0f86ad5402fb452469da17b247).\nOfficial asset: {demo.splatCount} splats.\nURP feature installed. Graphics API: {SystemInfo.graphicsDeviceType}. Official API supported: {OfficialGaussianBackend.SupportedApi}.\nDemo is synthetic, not trained underwater data.\n");
            Selection.activeGameObject=lab.gameObject;
        }
        public static void BackupDirtyScenes()
        {
            for(int i=0;i<EditorSceneManager.sceneCount;i++)
            {
                var scene=EditorSceneManager.GetSceneAt(i);if(!scene.isDirty)continue;
                Directory.CreateDirectory("Logs/Reconstruction/SceneBackups");
                EditorSceneManager.SaveScene(scene,"Logs/Reconstruction/SceneBackups/"+scene.name+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            }
        }
        // Upstream's creator has no public import API in this pinned version. Keep the adapter here,
        // check all reflected members, and validate the resulting asset rather than modifying upstream.
        public static OfficialAsset ImportOfficialPly(string path,string output)
        {
            Directory.CreateDirectory(output);AssetDatabase.Refresh();
            var creator=ScriptableObject.CreateInstance<GaussianSplatAssetCreator>();
            Type type=creator.GetType();const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            try
            {
                var quality=type.GetField("m_Quality",flags)??throw new MissingFieldException("Upstream creator quality API changed.");
                quality.SetValue(creator,Enum.Parse(quality.FieldType,"Medium"));
                type.GetField("m_InputFile",flags).SetValue(creator,Path.GetFullPath(path));
                type.GetField("m_OutputFolder",flags).SetValue(creator,output);
                type.GetField("m_ImportCameras",flags).SetValue(creator,false);
                type.GetMethod("ApplyQualityLevel",flags).Invoke(creator,null);
                type.GetMethod("CreateAsset",flags).Invoke(creator,null);
                string error=(string)type.GetField("m_ErrorMessage",flags).GetValue(creator);
                if(!string.IsNullOrEmpty(error))throw new InvalidDataException(error);
                string target=output+"/"+Path.GetFileNameWithoutExtension(path)+".asset";
                var asset=AssetDatabase.LoadAssetAtPath<OfficialAsset>(target);
                if(!asset||asset.splatCount==0)throw new Exception("Upstream creator did not produce an asset: "+target);
                return asset;
            }
            finally{EditorUtility.ClearProgressBar();Object.DestroyImmediate(creator);}
        }
        static void WriteSyntheticPly(GaussianSplatAsset asset,string path)
        {
            var header=new StringBuilder($"ply\nformat binary_little_endian 1.0\nelement vertex {asset.Count}\n");
            foreach(string n in new[]{"x","y","z","nx","ny","nz","f_dc_0","f_dc_1","f_dc_2"})header.Append("property float "+n+"\n");
            for(int j=0;j<45;j++)header.Append("property float f_rest_"+j+"\n");
            foreach(string n in new[]{"opacity","scale_0","scale_1","scale_2","rot_0","rot_1","rot_2","rot_3"})header.Append("property float "+n+"\n");
            header.Append("end_header\n");
            using(var w=new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes(header.ToString()));
                for(int i=0;i<asset.Count;i++)
                {
                    Vector4 p=asset.data[i*4],cov=asset.data[i*4+1],cov2=asset.data[i*4+2],rgb=asset.data[i*4+3];
                    w.Write(p.x);w.Write(p.z);w.Write(p.y);w.Write(0f);w.Write(0f);w.Write(0f);
                    w.Write((rgb.x-0.5f)/0.282094792f);w.Write((rgb.y-0.5f)/0.282094792f);w.Write((rgb.z-0.5f)/0.282094792f);
                    for(int j=0;j<45;j++)w.Write(0f);
                    w.Write(Mathf.Log(p.w/(1-p.w)));
                    w.Write(Mathf.Log(Mathf.Sqrt(cov.x)));w.Write(Mathf.Log(Mathf.Sqrt(cov2.y)));w.Write(Mathf.Log(Mathf.Sqrt(cov.w)));
                    w.Write(1f);w.Write(0f);w.Write(0f);w.Write(0f);
                }
            }
        }
        [MenuItem("Tools/Reconstruction/5 Assign selected official asset")]
        public static void AssignSelected()
        {
            var asset=Selection.activeObject as OfficialAsset;
            var lab=Object.FindObjectOfType<ReconstructionLab>();
            if(!asset||!lab||!lab.official)throw new Exception("Open upgraded scene and select an official GaussianSplatAsset first.");
            Undo.RecordObject(lab.official.rendererComponent,"Assign official Gaussian asset");
            Undo.RecordObject(lab.official,"Assign official Gaussian asset");
            lab.official.SetActive(false);lab.official.rendererComponent.m_Asset=asset;
            lab.official.syntheticDemo=AssetDatabase.GetAssetPath(asset).StartsWith(DataRoot+"/");
            lab.Select(false);EditorUtility.SetDirty(lab.official);EditorUtility.SetDirty(lab.official.rendererComponent);
            EditorSceneManager.MarkSceneDirty(lab.gameObject.scene);
        }
        [MenuItem("Tools/Reconstruction/6 Configure Windows DX12 (restart editor required)")]
        public static void ConfigureDX12()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D12,GraphicsDeviceType.Direct3D11});
            AssetDatabase.SaveAssets();Debug.Log("Windows Player now prefers DX12. Current editor API is "+SystemInfo.graphicsDeviceType+". Restart the editor using -force-d3d12 to test official rendering.");
        }
        public static void UpgradeAndCheck()
        {
            Upgrade();ReconstructionChecks.Run();ReconstructionSceneBuilder.Validate();
            OfficialGaussianChecks.Run();
            SyntheticTrainingCapture.Export();
        }
    }
    [InitializeOnLoad]
    public static class ReconstructionEditorBridge
    {
        static ReconstructionEditorBridge(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            const string path="Logs/Reconstruction/command.txt";
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(path))return;
            string command=File.ReadAllText(path).Trim();File.Delete(path);
            try
            {
                if(command=="upgrade")OfficialGaussianSetup.UpgradeAndCheck();
                else if(command=="capture")SyntheticTrainingCapture.Export();
                else if(command=="dx12")OfficialGaussianSetup.ConfigureDX12();
                else if(command=="verify") {OfficialGaussianChecks.Run();ReconstructionChecks.Run();}
                else if(command=="prepare-restart-dx12")
                {
                    if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play mode first.");
                    OfficialGaussianSetup.BackupDirtyScenes();
                    OfficialGaussianSetup.ConfigureDX12();
                    EditorSceneManager.SaveOpenScenes();
                }
                else throw new Exception("Unknown reconstruction command: "+command);
                File.WriteAllText("Logs/Reconstruction/command-result.txt","PASS "+command+" "+DateTime.Now.ToString("O"));
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText("Logs/Reconstruction/command-result.txt",e.ToString());}
        }
    }
}
