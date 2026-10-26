using UnityEngine;

namespace UnderwaterReconstruction
{
    [ExecuteAlways, RequireComponent(typeof(MeshRenderer))]
    public sealed class NerfVolumeRenderer : MonoBehaviour
    {
        public NerfVolumeAsset asset;
        [Range(24,384)] public int steps = 128;
        MaterialPropertyBlock properties;
        void LateUpdate() { Apply(); }
        public void Apply()
        {
            var renderer = GetComponent<MeshRenderer>();
            renderer.enabled = asset && asset.radianceDensity;
            if (!renderer.enabled) return;
            transform.localPosition = asset.bounds.center;
            transform.localScale = asset.bounds.size;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetTexture("_Volume", asset.radianceDensity);
            properties.SetFloat("_Steps", steps);
            Vector3 parentScale = transform.parent ? transform.parent.lossyScale : Vector3.one;
            properties.SetFloat("_TrainingUnitsPerWorldUnit", 1 / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)));
            renderer.SetPropertyBlock(properties);
        }
    }
}
