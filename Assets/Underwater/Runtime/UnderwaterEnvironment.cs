using UnityEngine;
using UnityEngine.Rendering;

namespace UnderwaterBaseline
{
    public sealed class UnderwaterEnvironment : MonoBehaviour
    {
        public Camera eye;
        public ParticleSystem[] snow;
        public ParticleSystem bubbles;
        public int[] budgets = { 220, 260, 300 };
        public bool snowEnabled = true;
        public int quality = 1;
        [Range(1,2.5f)] public float seabedParticleMultiplier=1.7f;
        Vector3 lastVolume;
        bool inSeabed;

        void Start()
        {
            lastVolume = eye.transform.position;
            inSeabed=eye.transform.position.x>31;
            foreach (var layer in snow)
            {
                layer.transform.position = lastVolume;
            }
            ApplyQuality(quality);
        }

        void LateUpdate()
        {
            // World-space particles retain parallax; only the emitter volume follows the user.
            foreach (var layer in snow) layer.transform.position = eye.transform.position;
            bool sea=eye.transform.position.x>31;
            if(sea!=inSeabed){inSeabed=sea;ApplyQuality(quality);lastVolume=eye.transform.position;}
            if (Vector3.Distance(lastVolume, eye.transform.position) > 4)
            {
                foreach (var layer in snow) { layer.Clear(); layer.Simulate(20, true, true); layer.Play(); }
            }
            lastVolume = eye.transform.position;
        }

        public void ApplyQuality(int value)
        {
            quality = Mathf.Clamp(value, 0, 2);
            float factor = quality == 0 ? 0.55f : quality == 1 ? 0.8f : 1;
            inSeabed=eye && eye.transform.position.x>31;
            factor*=inSeabed?seabedParticleMultiplier:1;
            for (int i = 0; i < snow.Length; i++)
            {
                var main = snow[i].main;
                main.maxParticles = Mathf.RoundToInt(budgets[i] * factor);
                var emission = snow[i].emission;
                emission.enabled = snowEnabled;
                emission.rateOverTime = main.maxParticles / 20f;
                // Apply a lower budget immediately, rather than waiting for existing particles to expire.
                snow[i].Clear();
                if (snowEnabled) { snow[i].Simulate(20, true, true); snow[i].Play(); }
            }
        }

        public void EmitBubbles()
        {
            BubbleBursts++;
            bubbles.transform.position = eye.transform.position + eye.transform.forward * 0.5f - Vector3.up * 0.3f;
            bubbles.Emit(14);
        }
        public int BubbleBursts {get;private set;}

        public int ParticleCount
        {
            get { int count = bubbles.particleCount; foreach (var p in snow) count += p.particleCount; return count; }
        }
    }
}
