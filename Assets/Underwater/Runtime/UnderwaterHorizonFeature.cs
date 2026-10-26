using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnderwaterBaseline
{
    // First renderer feature: a depth reconstruction pass, compatible with XR texture arrays.
    public sealed class UnderwaterHorizonFeature : ScriptableRendererFeature
    {
        public Shader shader;
        public float surfaceHeight = 4f;
        public Color fogColor = new Color(0.025f, 0.28f, 0.33f);
        public Vector3 absorption = new Vector3(0.20f, 0.065f, 0.035f);
        public float density = 0.105f;
        [Header("Water optics (coefficients per metre)")]
        public Vector3 poolScattering = new Vector3(0.022f,0.026f,0.028f);
        public Vector3 seaAbsorption = new Vector3(0.095f,0.035f,0.018f);
        public Vector3 seaScattering = new Vector3(0.040f,0.048f,0.052f);
        public Color seaScatterColor = new Color(0.065f,0.235f,0.25f);
        [Range(0,3)] public float bottomSediment = 1.1f;
        [Range(0,0.7f)] public float forwardScattering = 0.28f;
        [Header("Seabed visibility")]
        [Range(0,1)] public float seaCausticStrength=0;
        [Range(0,1)] public float seaBottomHaze=0.38f;
        [Range(1,10)] public float seaHazeTop=8;
        [Range(0,6)] public float seaBlurPixels=3.5f;
        Material material;
        WaterPass pass;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            if (shader != null) material = CoreUtils.CreateEngineMaterial(shader);
            pass = new WaterPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
            pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (material == null || (data.cameraData.cameraType != CameraType.Game && data.cameraData.cameraType != CameraType.SceneView)) return;
            // SingleCameraRequest skips frame callbacks. Also update here, before XR
            // drawing starts, so offscreen inspection uses the same simulation path.
            UnderwaterCaustics.Active?.RenderForCamera();
            material.SetFloat("_WaterHeight", surfaceHeight);
            material.SetColor("_WaterFog", fogColor);
            material.SetVector("_Absorption", absorption);
            material.SetFloat("_Density", density);
            // Transparent materials use the same coefficients as the fullscreen pass.
            Shader.SetGlobalFloat("_WaterHeight",surfaceHeight);
            Shader.SetGlobalVector("_UWPoolAbsorption",absorption);
            Shader.SetGlobalVector("_UWPoolScattering",poolScattering);
            Shader.SetGlobalVector("_UWSeaAbsorption",seaAbsorption);
            Shader.SetGlobalVector("_UWSeaScattering",seaScattering);
            Shader.SetGlobalColor("_UWPoolScatterColor",fogColor);
            Shader.SetGlobalColor("_UWSeaScatterColor",seaScatterColor);
            Shader.SetGlobalFloat("_UWSediment",bottomSediment);
            Shader.SetGlobalFloat("_UWAnisotropy",forwardScattering);
            Shader.SetGlobalFloat("_UWSeaCausticStrength",seaCausticStrength);
            Shader.SetGlobalVector("_UWSeaHaze",new Vector4(seaBottomHaze,seaHazeTop,seaBlurPixels,0));
            pass.material = material;
            renderer.EnqueuePass(pass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData data)
        {
            if (data.cameraData.cameraType == CameraType.Game || data.cameraData.cameraType == CameraType.SceneView) pass.source = renderer.cameraColorTargetHandle;
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
        }

        sealed class WaterPass : ScriptableRenderPass
        {
            public Material material;
            public RTHandle source;
            RTHandle temporary;
            readonly ProfilingSampler sampler = new ProfilingSampler("Underwater Fog and Absorption");
            readonly Matrix4x4[] inverseViewProjection=new Matrix4x4[2];
            readonly Vector4[] eyePositions=new Vector4[2];
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
            {
                var descriptor = data.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref temporary, descriptor, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "_UnderwaterColor");
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, sampler))
                {
                    var camera=data.cameraData;
                    int views=camera.camera.stereoEnabled && UnityEngine.XR.XRSettings.stereoRenderingMode!=UnityEngine.XR.XRSettings.StereoRenderingMode.MultiPass?2:1;
                    for(int eye=0;eye<views;eye++)
                    {
                        Matrix4x4 view=camera.GetViewMatrix(eye);
                        inverseViewProjection[eye]=(camera.GetGPUProjectionMatrix(eye)*view).inverse;
                        eyePositions[eye]=view.inverse.GetColumn(3);
                    }
                    if(views==1){inverseViewProjection[1]=inverseViewProjection[0];eyePositions[1]=eyePositions[0];}
                    cmd.SetGlobalMatrixArray("_UWInverseViewProjection",inverseViewProjection);
                    cmd.SetGlobalVectorArray("_UWEyePositions",eyePositions);
                    Blitter.BlitCameraTexture(cmd, source, temporary, material, 0);
                    // URP's opaque copy is made before this pass, so it has no underwater fog.
                    // Water refraction must sample this processed XR texture instead.
                    cmd.SetGlobalTexture("_UWFoggedColor",temporary.nameID);
                    Blitter.BlitCameraTexture(cmd, temporary, source);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
            public void Dispose() => temporary?.Release();
        }
    }
}
