using UnityEngine;
using UnityEngine.Rendering;
using OfficialRenderer = GaussianSplatting.Runtime.GaussianSplatRenderer;

namespace UnderwaterReconstruction
{
    public sealed class OfficialGaussianBackend : MonoBehaviour
    {
        public OfficialRenderer rendererComponent;
        public bool syntheticDemo = true;
        public static bool SupportedApi => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 ||
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;
        public bool HasAsset => rendererComponent && rendererComponent.HasValidAsset;
        public bool Active => rendererComponent && rendererComponent.gameObject.activeInHierarchy;
        public int Count => HasAsset ? rendererComponent.asset.splatCount : 0;
        public void SetActive(bool value)
        {
            if (rendererComponent) rendererComponent.gameObject.SetActive(value && SupportedApi && HasAsset);
        }
    }
}
