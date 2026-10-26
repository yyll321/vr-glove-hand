using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using Object = UnityEngine.Object;

namespace UnderwaterReconstruction.Editor
{
    public static class ReconstructionSceneBuilder
    {
        public const string Root="Assets/Reconstruction";
        public const string ScenePath=Root+"/Scenes/UnderwaterReconstruction.unity";
        [MenuItem("Tools/Reconstruction/1 Create or open reconstruction scene")]
        public static void Create()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode first.");
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Directory.CreateDirectory(Root+"/Scenes");Directory.CreateDirectory(Root+"/Data");Directory.CreateDirectory(Root+"/Settings");
            if(File.Exists(ScenePath)){EnsureConfiguration();EditorSceneManager.OpenScene(ScenePath);return;}
            AssetDatabase.Refresh();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.fog=false;RenderSettings.ambientLight=new Color(0.2f,0.25f,0.28f);
            // Independent URP assets: no underwater fullscreen passes, shadows or post-processing.
            var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.name="ReconstructionRenderer";AssetDatabase.CreateAsset(renderer,Root+"/Settings/ReconstructionRenderer.asset");
            var pipeline=UniversalRenderPipelineAsset.Create(renderer);pipeline.name="ReconstructionURP";
            pipeline.msaaSampleCount=1;pipeline.renderScale=1;pipeline.supportsHDR=false;pipeline.supportsCameraDepthTexture=true;
            pipeline.supportsCameraOpaqueTexture=false;
            var pipelineSettings=new SerializedObject(pipeline);
            pipelineSettings.FindProperty("m_MainLightShadowsSupported").boolValue=false;
            pipelineSettings.FindProperty("m_AdditionalLightShadowsSupported").boolValue=false;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(pipeline,Root+"/Settings/ReconstructionURP.asset");
            var gaussianMaterial=Material("GaussianSplat","Reconstruction/GaussianSplat");
            gaussianMaterial.enableInstancing=true;
            var nerfMaterial=Material("BakedNeRF","Reconstruction/BakedNeRF");
            var gs=DemoGaussians();AssetDatabase.CreateAsset(gs,Root+"/Data/SyntheticGaussians.asset");
            var nv=DemoVolume();AssetDatabase.CreateAsset(nv,Root+"/Data/SyntheticNeRFVolume.asset");AssetDatabase.AddObjectToAsset(nv.radianceDensity,nv);
            var systems=new GameObject("Reconstruction Lab (1 = 3DGS, 2 = baked NeRF)");
            var lab=systems.AddComponent<ReconstructionLab>();lab.lightweightPipeline=pipeline;
            var gsRoot=new GameObject("3DGS - replace asset with trained PLY");
            var splats=gsRoot.AddComponent<GaussianSplatRenderer>();splats.asset=gs;splats.material=gaussianMaterial;
            splats.sorter=AssetDatabase.LoadAssetAtPath<ComputeShader>(Root+"/Shaders/GaussianSort.compute");lab.gaussian=splats;
            var volumeRoot=new GameObject("NeRF - replace baked NRV asset (uniform scale only)");
            var volume=GameObject.CreatePrimitive(PrimitiveType.Cube);volume.name="Baked Radiance Volume";volume.transform.SetParent(volumeRoot.transform,false);
            Object.DestroyImmediate(volume.GetComponent<Collider>());
            volume.GetComponent<MeshRenderer>().sharedMaterial=nerfMaterial;
            var nerf=volume.AddComponent<NerfVolumeRenderer>();nerf.asset=nv;nerf.Apply();lab.nerf=nerf;volume.SetActive(false);
            var rig=new GameObject("XR Origin - desktop and PICO");rig.transform.position=new Vector3(0,0,-4);
            var origin=rig.AddComponent<XROrigin>();var offset=new GameObject("Camera Offset");offset.transform.SetParent(rig.transform,false);
            var head=new GameObject("Main Camera");head.tag="MainCamera";head.transform.SetParent(offset.transform,false);head.transform.localPosition=new Vector3(0,1.6f,0);
            var camera=head.AddComponent<Camera>();camera.nearClipPlane=0.05f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.015f,0.045f,0.06f);
            camera.allowHDR=false;camera.allowMSAA=false;camera.useOcclusionCulling=false;head.AddComponent<AudioListener>();
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            head.AddComponent<UnderwaterBaseline.UnderwaterHeadTracking>().eye=camera;
            origin.Camera=camera;origin.CameraFloorOffsetObject=offset;origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;
            lab.rig=rig.transform;lab.eye=camera;
            EditorSceneManager.SaveScene(scene,ScenePath);EnsureConfiguration();AssetDatabase.SaveAssets();
            Selection.activeGameObject=gsRoot;
            if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(new Vector3(0,0.8f,0),Quaternion.Euler(12,0,0),5);
            Debug.Log("Created "+ScenePath+". Synthetic demos only; import trained data through Tools/Reconstruction.");
        }
        static void EnsureConfiguration()
        {
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root+"/Settings/ReconstructionRenderer.asset");
            if(!renderer)throw new Exception("Missing reconstruction renderer asset.");
            ResourceReloader.ReloadAllNullIn(renderer,UniversalRenderPipelineAsset.packagePath);
            // NeRF must sample opaque depth before its transparent volume pass.
            renderer.copyDepthMode=CopyDepthMode.AfterOpaques;
            EditorUtility.SetDirty(renderer);
            var scenes=EditorBuildSettings.scenes.ToList();
            if(!scenes.Any(s=>s.path==ScenePath))
            {scenes.Add(new EditorBuildSettingsScene(ScenePath,false));EditorBuildSettings.scenes=scenes.ToArray();}
            AssetDatabase.SaveAssets();
        }
        static Material Material(string name,string shaderName)
        {
            var shader=Shader.Find(shaderName);if(!shader)throw new Exception("Shader missing: "+shaderName);
            var material=new Material(shader){name=name};AssetDatabase.CreateAsset(material,Root+"/Settings/"+name+".mat");return material;
        }
        static GaussianSplatAsset DemoGaussians()
        {
            var asset=ScriptableObject.CreateInstance<GaussianSplatAsset>();asset.synthetic=true;asset.source="Procedural demonstration; NOT trained reconstruction";
            var data=new List<Vector4>();var random=new System.Random(42);
            for(int z=0;z<100;z++)for(int x=0;x<100;x++)
            {
                float px=(x-50)*0.055f,pz=(z-50)*0.055f,y=0.035f*Mathf.Sin(px*3)*Mathf.Cos(pz*2);
                float shade=0.65f+(float)random.NextDouble()*0.25f;
                Add(data,new Vector3(px,y,pz),new Vector3(0.032f,0.012f,0.032f),new Color(0.48f,0.55f,0.45f)*shade);
            }
            for(int i=0;i<6400;i++)
            {
                float a=(i%160)*Mathf.PI*2/160,b=(i/160)*Mathf.PI*2/40;
                float r=0.8f+0.18f*Mathf.Cos(b);
                Add(data,new Vector3(r*Mathf.Cos(a),0.85f+0.18f*Mathf.Sin(b),r*Mathf.Sin(a)),Vector3.one*0.026f,new Color(0.72f,0.4f,0.16f));
            }
            asset.data=data.ToArray();asset.bounds=new Bounds(new Vector3(0,0.5f,0),new Vector3(6,2,6));return asset;
        }
        static void Add(List<Vector4> data,Vector3 p,Vector3 s,Color c)
        {
            data.Add(new Vector4(p.x,p.y,p.z,0.85f));data.Add(new Vector4(s.x*s.x,0,0,s.y*s.y));
            data.Add(new Vector4(0,s.z*s.z,0,0));data.Add(new Vector4(c.r,c.g,c.b,0));
        }
        static NerfVolumeAsset DemoVolume()
        {
            const int n=64;var colors=new Color[n*n*n];
            var bounds=new Bounds(new Vector3(0,0.65f,0),new Vector3(6,1.7f,6));
            for(int z=0;z<n;z++)for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                Vector3 p=bounds.min+Vector3.Scale(new Vector3((x+0.5f)/n,(y+0.5f)/n,(z+0.5f)/n),bounds.size);
                float torus=Mathf.Sqrt(Mathf.Pow(Mathf.Sqrt(p.x*p.x+p.z*p.z)-0.8f,2)+Mathf.Pow(p.y-0.85f,2))-0.18f;
                float ground=Mathf.Abs(p.y-0.035f*Mathf.Sin(p.x*3)*Mathf.Cos(p.z*2));
                float shell=55*Mathf.Exp(-torus*torus/0.0012f),floor=50*Mathf.Exp(-ground*ground/0.0008f);
                Color c=Color.Lerp(new Color(0.38f,0.44f,0.36f),new Color(0.72f,0.4f,0.16f),shell/(floor+shell+0.0001f));
                c.a=shell+floor;colors[x+n*(y+n*z)]=c;
            }
            var texture=new Texture3D(n,n,n,TextureFormat.RGBAHalf,false){name="Synthetic RadianceDensity",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
            texture.SetPixels(colors);texture.Apply(false,true);
            var asset=ScriptableObject.CreateInstance<NerfVolumeAsset>();asset.synthetic=true;asset.source="Procedural demonstration; NOT trained NeRF";asset.bounds=bounds;asset.radianceDensity=texture;return asset;
        }
        [MenuItem("Tools/Reconstruction/3 Validate scene and shaders")]
        public static void Validate()
        {
            var lab=Object.FindObjectOfType<ReconstructionLab>();if(!lab)throw new Exception("Open reconstruction scene first.");
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                    if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script: "+t.name);
            foreach(var name in new[]{"Reconstruction/GaussianSplat","Reconstruction/BakedNeRF"})
            {
                var shader=Shader.Find(name);if(!shader||!shader.isSupported)throw new Exception("Shader unavailable: "+name);
                var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
                if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(e=>e.message)));
            }
            if(!lab.gaussian.asset||!lab.gaussian.material||!lab.gaussian.sorter||!lab.nerf.asset||!lab.nerf.asset.radianceDensity||!lab.lightweightPipeline)throw new Exception("Missing reconstruction resources.");
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root+"/Settings/ReconstructionRenderer.asset");
            if(!renderer.shaders.copyDepthPS||!renderer.shaders.blitPS||!renderer.xrSystemData)throw new Exception("Missing URP depth, blit or XR resources.");
            Directory.CreateDirectory("Logs/Reconstruction");File.WriteAllText("Logs/Reconstruction/validation.txt","PASS scene references, scripts and shader import. Hardware XR performance is not validated.\n");
            Debug.Log("Reconstruction validation passed.");
        }
        public static void CreateAndValidate() {Create();Validate();ReconstructionChecks.Run();Validate();}
    }
}
