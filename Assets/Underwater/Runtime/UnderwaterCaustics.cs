using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace UnderwaterBaseline
{
    [ExecuteAlways]
    public sealed class UnderwaterCaustics : MonoBehaviour
    {
        public static UnderwaterCaustics Active { get; private set; }
        public UnderwaterWaveSettings settings;
        public Light sun;
        public Shader normalShader, mapShader;
        public float surfaceHeight = 10;
        public bool animateInEditMode = true;
        [Tooltip("Freeze waves to check that caustics stay on surfaces when moving the camera.")]
        public bool freezeWaves;
        public float frozenTime = 7.5f;
        public RenderTexture NormalMap { get; private set; }
        public RenderTexture CausticMap { get; private set; }
        public int GeneratedFrames { get; private set; }
        public float CpuSubmitMs { get; private set; }
        Material normalMaterial, mapMaterial;
        int lastGrid, lastResolution;
        int simulationFrame=-1;
        readonly Vector4[] waves = new Vector4[8];
        readonly Vector4[] speeds = new Vector4[2];
        readonly Vector4[] phases = new Vector4[2];
        void OnEnable()
        {
            Active=this;
            RenderPipelineManager.beginContextRendering+=BeforeFrame;
            if(Application.isPlaying)Debug.Log($"[WaterResearch] Caustics enabled: settings={settings}, normals={normalShader}, caustics={mapShader}");
        }
        void OnDisable()
        {
            if(Active==this)Active=null;
            RenderPipelineManager.beginContextRendering-=BeforeFrame;
            Shader.SetGlobalFloat("_UWCausticsEnabled", 0);
            Release();
        }
        void Release()
        {
            if (NormalMap) { NormalMap.Release(); CoreUtils.Destroy(NormalMap); }
            if (CausticMap) { CausticMap.Release(); CoreUtils.Destroy(CausticMap); }
            CoreUtils.Destroy(normalMaterial); CoreUtils.Destroy(mapMaterial);
            NormalMap = null; CausticMap = null;
        }
        void Ensure()
        {
            int resolution = Mathf.Clamp(settings.mapResolution, 128, 1024), grid = Mathf.Clamp(settings.grid, 32, 180);
            if (normalMaterial && mapMaterial && lastGrid == grid && lastResolution == resolution) return;
            Release(); lastGrid = grid; lastResolution = resolution;
            NormalMap = Target("Gerstner Surface Normals", resolution, RenderTextureFormat.ARGBHalf);
            CausticMap = Target("Refracted Triangle Area Caustics", resolution, RenderTextureFormat.RHalf);
            normalMaterial = CoreUtils.CreateEngineMaterial(normalShader); mapMaterial = CoreUtils.CreateEngineMaterial(mapShader);
        }
        static RenderTexture Target(string name, int resolution, RenderTextureFormat format)
        {
            var rt = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear)
            { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear,
                useMipMap = format == RenderTextureFormat.RHalf, autoGenerateMips = false, hideFlags = HideFlags.HideAndDontSave };
            rt.Create(); return rt;
        }
        void BeforeFrame(ScriptableRenderContext context,List<Camera> cameras)
        { RenderForCamera(); }
        public void RenderForCamera()
        {
            if(simulationFrame==Time.frameCount && Application.isPlaying)return;
            if(Application.isPlaying || animateInEditMode)RenderMaps(freezeWaves ? frozenTime : Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
        }
        public void RenderMaps(float time)
        {
            if (!settings || !normalShader || !mapShader) return;
            var timer = System.Diagnostics.Stopwatch.StartNew(); Ensure();
            int count = Mathf.Min(8, settings.waves.Length); speeds[0] = speeds[1] = phases[0] = phases[1] = Vector4.zero;
            for (int i = 0; i < count; i++)
            {
                var w = settings.waves[i]; var k = w.cycles * (2 * Mathf.PI / settings.tileSize);
                waves[i] = new Vector4(k.x, k.y, w.amplitude * settings.waveScale, w.steepness);
                var vector = speeds[i/4]; vector[i%4] = w.speed; speeds[i/4] = vector;
                vector = phases[i/4]; vector[i%4] = w.phase; phases[i/4] = vector;
            }
            Vector3 light = sun ? sun.transform.forward : new Vector3(0.2f,-1,0.15f).normalized;
            var cmd = CommandBufferPool.Get("Gerstner normal map + area-ratio caustic map");
            // Clip-space procedural draws need no camera matrices or mesh bounds. Do not
            // overwrite the XR camera's view/projection globals from LateUpdate.
            bool instancing = Shader.IsKeywordEnabled("STEREO_INSTANCING_ON");
            bool multiview = Shader.IsKeywordEnabled("STEREO_MULTIVIEW_ON");
            cmd.DisableShaderKeyword("STEREO_INSTANCING_ON");
            cmd.DisableShaderKeyword("STEREO_MULTIVIEW_ON");
            cmd.SetGlobalVectorArray("_UWWaves", waves); cmd.SetGlobalVectorArray("_UWSpeeds", speeds); cmd.SetGlobalVectorArray("_UWPhases", phases); cmd.SetGlobalInt("_UWWaveCount", count);
            cmd.SetGlobalFloat("_UWTime", time); cmd.SetGlobalFloat("_UWTileSize", settings.tileSize);
            cmd.SetGlobalFloat("_UWProjectionDepth", settings.projectionDepth); cmd.SetGlobalFloat("_UWMicroStrength", settings.microNormalStrength);
            cmd.SetGlobalFloat("_UWGrid", lastGrid); cmd.SetGlobalVector("_UWLightTravel", light);
            cmd.SetRenderTarget(NormalMap); cmd.ClearRenderTarget(false,true,Color.clear);
            cmd.SetViewport(new Rect(0,0,lastResolution,lastResolution));
            cmd.DrawProcedural(Matrix4x4.identity, normalMaterial, 0, MeshTopology.Triangles, 3);
            cmd.SetGlobalTexture("_UWWaterNormalMap", NormalMap);
            cmd.SetRenderTarget(CausticMap); cmd.ClearRenderTarget(false,true,Color.black);
            int guard = Mathf.CeilToInt(lastGrid * 0.2f), extent = lastGrid + guard * 2;
            cmd.SetGlobalInt("_UWGridGuard", guard);
            cmd.DrawProcedural(Matrix4x4.identity, mapMaterial, 0, MeshTopology.Triangles, extent * extent * 6);
            cmd.GenerateMips(CausticMap);
            cmd.SetGlobalTexture("_UWCausticMap", CausticMap);
            cmd.SetGlobalFloat("_UWCausticStrength", settings.causticStrength); cmd.SetGlobalFloat("_UWSurfaceHeight", surfaceHeight);
            cmd.SetGlobalFloat("_UWCausticFootprint", settings.causticFootprint);
            cmd.SetGlobalFloat("_UWCausticVariation", settings.causticVariation);
            cmd.SetGlobalColor("_UWCausticColor", sun ? sun.color * sun.intensity : Color.white);
            cmd.SetGlobalFloat("_UWCausticsEnabled", 1);
            if(instancing)cmd.EnableShaderKeyword("STEREO_INSTANCING_ON");
            if(multiview)cmd.EnableShaderKeyword("STEREO_MULTIVIEW_ON");
            Graphics.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            simulationFrame=Time.frameCount;
            GeneratedFrames++; CpuSubmitMs = (float)timer.Elapsed.TotalMilliseconds;
        }
    }
}
