using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterOpticsUpgrade
    {
        [MenuItem("Tools/Underwater/Research/8 Bake Water Environment and Apply Optics")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play before baking water optics.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=UnderwaterResearchSceneBuilder.ScenePath)throw new Exception("Open the editable underwater scene first.");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/UnderwaterResearch-beforeOptics-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            var caustics=Object.FindObjectOfType<UnderwaterCaustics>();
            caustics.RenderMaps(7.5f);
            var oldTarget=RenderTexture.active;
            var captureObject=new GameObject("Temporary water environment capture"){hideFlags=HideFlags.HideAndDontSave};
            var camera=captureObject.AddComponent<Camera>();camera.enabled=false;camera.cameraType=CameraType.Reflection;
            camera.stereoTargetEye=StereoTargetEyeMask.None;camera.fieldOfView=90;camera.aspect=1;camera.nearClipPlane=0.03f;camera.farClipPlane=130;
            camera.clearFlags=CameraClearFlags.Skybox;camera.cullingMask=(1<<0)|(1<<8)|(1<<9); // No water, particles, HUD or controller lines.
            camera.allowHDR=false;camera.allowMSAA=false;camera.useOcclusionCulling=false;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;data.allowXRRendering=false;
            var target=RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            Vector3[] forward={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            Vector3[] up={Vector3.up,Vector3.up,Vector3.back,Vector3.forward,Vector3.up,Vector3.up};
            try
            {
                foreach(int region in new[]{0,1})
                {
                    float center=region*62;var position=new Vector3(center,9.6f,0);
                    var cube=new Cubemap(256,TextureFormat.RGBAHalf,true){name=region==0?"Pool water environment":"Seabed water environment"};
                    for(int face=0;face<6;face++)
                    {
                        camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(forward[face],up[face]));
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                        RenderTexture.active=target;
                        var image=new Texture2D(256,256,TextureFormat.RGBA32,false,true);
                        image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                        // A normal URP 2D camera target is bottom-up after ReadPixels;
                        // cubemap face coordinates use the opposite vertical direction.
                        // Without this conversion, downward reflections sample the sky.
                        var pixels=image.GetPixels();var facePixels=new Color[pixels.Length];
                        for(int row=0;row<256;row++)Array.Copy(pixels,row*256,facePixels,(255-row)*256,256);
                        cube.SetPixels(facePixels,(CubemapFace)face);Object.DestroyImmediate(image);
                    }
                    cube.Apply(true,false);
                    string path="Assets/Underwater/Materials/"+(region==0?"Pool":"Sea")+"WaterEnvironment.asset";
                    var saved=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                    if(saved){EditorUtility.CopySerialized(cube,saved);Object.DestroyImmediate(cube);EditorUtility.SetDirty(saved);}else{AssetDatabase.CreateAsset(cube,path);saved=cube;}
                    var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Underwater/Materials/"+(region==0?"Pool":"Sea")+"GerstnerWater.mat");
                    material.SetTexture("_EnvironmentCube",saved);material.SetFloat("_EnvironmentReady",1);
                    material.SetVector("_ReflectionCenter",position);material.SetVector("_ReflectionMin",new Vector3(center-12.5f,0,-25));material.SetVector("_ReflectionMax",new Vector3(center+12.5f,10,25));
                    material.SetFloat("_SurfaceWaveScale",region==0?0.55f:1);EditorUtility.SetDirty(material);
                }
            }
            finally{RenderTexture.active=oldTarget;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(captureObject);}
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Underwater/Settings/UnderwaterRenderer.asset");
            foreach(var feature in renderer.rendererFeatures)if(feature is UnderwaterHorizonFeature water)
            {
                water.absorption=new Vector3(0.025f,0.009f,0.005f);water.poolScattering=new Vector3(0.022f,0.026f,0.028f);
                water.seaAbsorption=new Vector3(0.095f,0.035f,0.018f);water.seaScattering=new Vector3(0.040f,0.048f,0.052f);
                water.seaScatterColor=new Color(0.065f,0.235f,0.25f);water.bottomSediment=1.1f;water.forwardScattering=0.28f;EditorUtility.SetDirty(water);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
            Debug.Log("[WaterResearch] Separate water optics and fixed world environment captures saved. No per-eye reflection camera added.");
        }
    }
}
