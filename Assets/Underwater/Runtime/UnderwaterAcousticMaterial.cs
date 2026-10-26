using UnityEngine;

namespace UnderwaterBaseline
{
    public sealed class UnderwaterAcousticMaterial : MonoBehaviour
    {
        public enum Preset { Sand, Limestone, Basalt, Concrete, Metal, Custom }
        public Preset material = Preset.Limestone;
        [Min(1)] public float density = 2400;
        [Min(1)] public float longitudinalSpeed = 3000;
        [Min(0)] public float shearSpeed = 1500;
        [Range(0,1)] public float scattering = 0.18f;
        public void Parameters(float depth, out float rho, out float longitudinal, out float shear)
        {
            switch (material)
            {
                case Preset.Sand: rho = 1900; longitudinal = 1650; shear = 110 * Mathf.Pow(Mathf.Max(0.01f,depth),0.3f); break;
                case Preset.Limestone: rho = 2400; longitudinal = 3000; shear = 1500; break;
                case Preset.Basalt: rho = 2700; longitudinal = 5250; shear = 2500; break;
                case Preset.Concrete: rho = 2400; longitudinal = 3200; shear = 1700; break;
                case Preset.Metal: rho = 7850; longitudinal = 5900; shear = 3200; break;
                default: rho = density; longitudinal = longitudinalSpeed; shear = shearSpeed; break;
            }
        }
    }
}
