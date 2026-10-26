using UnityEngine;

namespace UnderwaterBaseline
{
    [CreateAssetMenu(menuName = "Underwater/Gerstner Wave Settings")]
    public sealed class UnderwaterWaveSettings : ScriptableObject
    {
        [System.Serializable]
        public struct Wave
        {
            public Vector2 cycles;
            [Min(0)] public float amplitude;
            [Range(0, 0.8f)] public float steepness;
            public float speed;
            [Range(0, 6.283185f)] public float phase;
        }
        [Min(2)] public float tileSize = 12;
        [Min(0.1f)] public float projectionDepth = 8;
        [Range(32, 180)] public int grid = 100;
        public int mapResolution = 512;
        [Range(0, 2)] public float causticStrength = 0.35f;
        [Range(0, 2)] public float waveScale = 0.8f;
        [Range(0, 1)] public float microNormalStrength = 0.025f;
        [Tooltip("World-space footprint relative to the simulated map; smaller values give finer caustics.")]
        [Range(0.25f, 1.5f)] public float causticFootprint = 1f;
        [Tooltip("Mix an independently rotated projection to soften obvious tile repetition.")]
        [Range(0, 0.5f)] public float causticVariation = 0.32f;
        public Wave[] waves = {
            new Wave { cycles = new Vector2(4, 1), amplitude = 0.065f, steepness = 0.28f, speed = 0.37f, phase = 0.73f },
            new Wave { cycles = new Vector2(-3, 6), amplitude = 0.041f, steepness = 0.24f, speed = 0.51f, phase = 2.19f },
            new Wave { cycles = new Vector2(7, 5), amplitude = 0.024f, steepness = 0.18f, speed = 0.43f, phase = 4.81f },
            new Wave { cycles = new Vector2(-9, 4), amplitude = 0.018f, steepness = 0.14f, speed = 0.62f, phase = 1.37f },
            new Wave { cycles = new Vector2(11, -7), amplitude = 0.012f, steepness = 0.12f, speed = 0.46f, phase = 5.54f },
            new Wave { cycles = new Vector2(-13, -5), amplitude = 0.009f, steepness = 0.1f, speed = 0.71f, phase = 3.26f },
            new Wave { cycles = new Vector2(17, 9), amplitude = 0.006f, steepness = 0.08f, speed = 0.57f, phase = 0.24f },
            new Wave { cycles = new Vector2(-7, 19), amplitude = 0.005f, steepness = 0.07f, speed = 0.83f, phase = 4.12f }
        };
        public void Evaluate(Vector2 uv, float time, out Vector3 position, out Vector3 normal)
        {
            position = new Vector3(uv.x, 0, uv.y);
            Vector3 dx = Vector3.right, dz = Vector3.forward;
            foreach (var w in waves)
            {
                var k = w.cycles * (2 * Mathf.PI / tileSize); float magnitude = k.magnitude;
                if (magnitude < 0.0001f) continue;
                var d = k / magnitude; float a = w.amplitude * waveScale;
                float phase = Vector2.Dot(k, uv) - Mathf.Sqrt(9.81f * magnitude) * w.speed * time + w.phase;
                float sin = Mathf.Sin(phase), cos = Mathf.Cos(phase), q = w.steepness;
                position += new Vector3(q * a * d.x * cos, a * sin, q * a * d.y * cos);
                dx += new Vector3(-q * a * d.x * k.x * sin, a * k.x * cos, -q * a * d.y * k.x * sin);
                dz += new Vector3(-q * a * d.x * k.y * sin, a * k.y * cos, -q * a * d.y * k.y * sin);
            }
            normal = Vector3.Cross(dz, dx).normalized;
        }
    }
}
