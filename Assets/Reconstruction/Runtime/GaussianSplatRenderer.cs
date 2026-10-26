using UnityEngine;
using UnityEngine.Rendering;

namespace UnderwaterReconstruction
{
    [ExecuteAlways]
    public sealed class GaussianSplatRenderer : MonoBehaviour
    {
        public GaussianSplatAsset asset;
        public Material material;
        public ComputeShader sorter;
        [Range(0, 3)] public int shDegree = 3;
        [Range(1, 8)] public int sortEveryFrames = 1;
        [Range(0.2f, 2)] public float footprintScale = 1;
        [Range(0.001f, 0.1f)] public float alphaCutoff = 0.004f;
        ComputeBuffer splats, harmonics, order;
        MaterialPropertyBlock properties;
        GaussianSplatAsset loaded;
        int padded, initialize, bitonic, frame = -1;
        Vector3 lastPosition, lastForward;
        Camera lastCamera;
        bool sorted;
        public int Count => loaded ? loaded.Count : 0;

        void OnEnable() { RenderPipelineManager.beginCameraRendering += BeforeCamera; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= BeforeCamera; Release(); }
        void Release()
        {
            splats?.Release(); harmonics?.Release(); order?.Release();
            splats = harmonics = order = null; loaded = null; sorted = false;
        }
        void EnsureBuffers()
        {
            if (loaded == asset && splats != null) return;
            Release();
            if (!asset || asset.Count == 0 || !sorter || !material || !SystemInfo.supportsComputeShaders) return;
            if (asset.Count > 2000000) { Debug.LogError("Reduce the asset to <= 2 million splats before loading.", this); return; }
            padded = Mathf.NextPowerOfTwo(asset.Count);
            splats = new ComputeBuffer(asset.data.Length, 16); splats.SetData(asset.data);
            bool hasSH = asset.harmonics != null && asset.harmonics.Length == asset.Count * 15;
            harmonics = new ComputeBuffer(hasSH ? asset.harmonics.Length : 1, 16);
            harmonics.SetData(hasSH ? asset.harmonics : new Vector4[1]);
            order = new ComputeBuffer(padded, 8);
            initialize = sorter.FindKernel("Initialize"); bitonic = sorter.FindKernel("Bitonic");
            properties = new MaterialPropertyBlock();
            properties.SetBuffer("_Splats", splats); properties.SetBuffer("_Harmonics", harmonics);
            properties.SetBuffer("_Order", order);
            loaded = asset; sorted = false;
        }
        void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.Preview || !isActiveAndEnabled) return;
            Prepare(camera);
        }
        public void Prepare(Camera camera, bool forceSort = false)
        {
            EnsureBuffers();
            if (splats == null) return;
            // Center-eye sorting is shared across stereo eyes; projection remains per eye.
            bool moved = (camera.transform.position - lastPosition).sqrMagnitude > 0.000001f ||
                         Vector3.Angle(camera.transform.forward, lastForward) > 0.02f;
            if (!forceSort && sorted && lastCamera == camera && !transform.hasChanged && (!moved || Time.frameCount - frame < sortEveryFrames)) return;
            sorter.SetInt("_Count", loaded.Count); sorter.SetInt("_Padded", padded);
            sorter.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
            sorter.SetVector("_CameraPosition", camera.transform.position);
            sorter.SetVector("_CameraForward", camera.transform.forward);
            sorter.SetBuffer(initialize, "_Splats", splats); sorter.SetBuffer(initialize, "_Order", order);
            sorter.SetBuffer(bitonic, "_Order", order);
            sorter.Dispatch(initialize, (padded + 255) / 256, 1, 1);
            for (int k = 2; k <= padded; k <<= 1)
                for (int j = k >> 1; j > 0; j >>= 1)
                {
                    sorter.SetInt("_K", k); sorter.SetInt("_J", j);
                    sorter.Dispatch(bitonic, (padded + 255) / 256, 1, 1);
                }
            lastCamera = camera; lastPosition = camera.transform.position; lastForward = camera.transform.forward;
            frame = Time.frameCount; sorted = true; transform.hasChanged = false;
        }
        void LateUpdate() { Draw(); }
        public void Draw()
        {
            EnsureBuffers();
            if (splats == null) return;
            properties.SetMatrix("_SplatLocalToWorld", transform.localToWorldMatrix);
            properties.SetMatrix("_SplatWorldToLocal", transform.worldToLocalMatrix);
            properties.SetFloat("_FootprintScale", footprintScale); properties.SetFloat("_AlphaCutoff", alphaCutoff);
            properties.SetInt("_SHDegree", Mathf.Min(shDegree, loaded.shDegree));
            var b = loaded.bounds;
            Vector3 s = transform.lossyScale;
            float radius = b.extents.magnitude * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            var world = new Bounds(transform.TransformPoint(b.center), Vector3.one * radius * 2);
            Graphics.DrawProcedural(material, world, MeshTopology.Triangles, 6, loaded.Count, null,
                properties, ShadowCastingMode.Off, false, gameObject.layer);
        }
    }
}
