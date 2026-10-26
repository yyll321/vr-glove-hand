using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace UnderwaterReconstruction.Editor
{
    public static class OfficialGaussianChecks
    {
        public static void Run()
        {
            var lab=Object.FindObjectOfType<ReconstructionLab>();
            if(!lab||!lab.official||!lab.official.HasAsset)throw new Exception("Official asset not configured.");
            if(Object.FindObjectsOfType<Camera>().Count(c=>c.enabled && c.targetTexture==null && c.gameObject.scene==lab.gameObject.scene)!=1)
                throw new Exception("Reconstruction must have one enabled driving camera.");
            var native=lab.official.rendererComponent;
            if(!native.m_ShaderSplats||!native.m_ShaderComposite||!native.m_ShaderDebugBoxes||!native.m_ShaderDebugPoints||!native.m_CSSplatUtilities)
                throw new Exception("Official renderer shader references missing.");
            var rd=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Reconstruction/Settings/ReconstructionRenderer.asset");
            if(!rd.rendererFeatures.Any(f=>f&&f.GetType().FullName=="GaussianSplatting.Runtime.GaussianSplatURPFeature"))throw new Exception("Official URP feature missing.");
            if(!OfficialGaussianBackend.SupportedApi)
            {
                if(native.gameObject.activeInHierarchy)throw new Exception("Official renderer incorrectly active on unsupported API.");
                File.WriteAllText("Logs/Reconstruction/official-checks.txt","PASS asset and URP wiring; runtime safely disabled on "+SystemInfo.graphicsDeviceType+". Official GPU render NOT tested.\n");return;
            }
            var pipeline=QualitySettings.renderPipeline;
            var oldTarget=lab.eye.targetTexture;bool preferred=lab.preferOfficial,mode=lab.useNerf;
            var target=new RenderTexture(640,480,24,RenderTextureFormat.ARGB32);target.Create();
            try
            {
                QualitySettings.renderPipeline=lab.lightweightPipeline;lab.preferOfficial=true;lab.Select(false);
                lab.eye.targetTexture=target;
                RenderPipeline.SubmitRenderRequest(lab.eye,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderPipeline.SubmitRenderRequest(lab.eye,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                var old=RenderTexture.active;RenderTexture.active=target;
                var image=new Texture2D(640,480,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();RenderTexture.active=old;
                File.WriteAllBytes("Logs/Reconstruction/official-3dgs.png",image.EncodeToPNG());
                var pixels=image.GetPixels32();var first=pixels[0];
                int changed=pixels.Count(c=>Math.Abs(c.r-first.r)+Math.Abs(c.g-first.g)+Math.Abs(c.b-first.b)>25);
                Object.DestroyImmediate(image);
                if(changed<500)throw new Exception("Official GPU output is empty.");
                File.WriteAllText("Logs/Reconstruction/official-checks.txt",$"PASS upstream asset, URP feature, shader references and nonempty actual GPU output.\nGPU: {SystemInfo.graphicsDeviceName}; API: {SystemInfo.graphicsDeviceType}; splats: {native.asset.splatCount}; changed pixels: {changed}.\nHeadset stereo and FPS not measured.\n");
            }
            finally
            {lab.eye.targetTexture=oldTarget;lab.preferOfficial=preferred;lab.Select(mode);QualitySettings.renderPipeline=pipeline;target.Release();Object.DestroyImmediate(target);}
        }
    }
}
