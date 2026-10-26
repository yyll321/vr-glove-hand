using UnityEngine;

namespace UnderwaterReconstruction
{
    [PreferBinarySerialization]
    public sealed class GaussianSplatAsset : ScriptableObject
    {
        public string source;
        public bool synthetic;
        public Bounds bounds;
        // Four float4 per splat: position/opacity, xx/xy/xz/yy, yz/zz/0/0, source RGB/0.
        public Vector4[] data;
        // Optional 15 RGB spherical-harmonic coefficients per splat, in source coordinates.
        public Vector4[] harmonics;
        public int shDegree;
        public int Count => data == null ? 0 : data.Length / 4;
    }
}
