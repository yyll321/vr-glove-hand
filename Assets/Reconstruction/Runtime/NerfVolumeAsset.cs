using UnityEngine;

namespace UnderwaterReconstruction
{
    public sealed class NerfVolumeAsset : ScriptableObject
    {
        public string source;
        public bool synthetic;
        public Texture3D radianceDensity;
        public Bounds bounds;
    }
}
