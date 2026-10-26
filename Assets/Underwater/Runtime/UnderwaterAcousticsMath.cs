using System;
using UnityEngine;
using Complex = System.Numerics.Complex;

namespace UnderwaterBaseline
{
    [Serializable]
    public sealed class AcousticWaterLayer
    {
        [Min(0)] public float topDepth;
        [Min(0)] public float bottomDepth = 10;
        [Min(1)] public float soundSpeed = 1500;
        [Min(1)] public float density = 1035;
        public float temperature = 8;
        [Min(0)] public float salinity = 35;
        public float pH = 8;
    }
    public static class UnderwaterAcousticsMath
    {
        public static readonly float[] Frequencies = {31.5f,63,125,250,500,1000,2000};
        // Castro et al. 2024, Eqs. 5-8: kHz, km, Celsius, ppt; dB/km -> Np/m.
        public static double AbsorptionDbPerKm(double hz, double depthMetres, AcousticWaterLayer water)
        {
            double f = hz / 1000, d = depthMetres / 1000, t = water.temperature, s = water.salinity;
            double eta1 = Math.Sqrt(0.78 * s / 35) * Math.Exp(t / 26), eta2 = 42 * Math.Exp(t / 17);
            double f2 = f * f;
            double boric = eta1 <= 0 ? 0 : 0.106 * eta1 * f2 / (f2 + eta1 * eta1) * Math.Exp((water.pH - 8) / 0.56);
            double magnesium = 0.52 * (1 + t / 43) * (s / 35) * eta2 * f2 / (f2 + eta2 * eta2) * Math.Exp(-d / 6);
            double pureWater = 0.00049 * f2 * Math.Exp(-(t / 27 + d / 17));
            return Math.Max(0, boric + magnesium + pureWater);
        }
        public static float PressureAttenuation(float hz, float metres, float depth, AcousticWaterLayer layer)
        { return (float)Math.Exp(-AbsorptionDbPerKm(hz, depth, layer) * metres / 8685.889638); }
        public static bool FluidInterface(Vector3 incident, Vector3 normalIntoFirst, float c1, float rho1, float c2, float rho2,
            out Vector3 transmitted, out float reflection, out float transmission)
        {
            incident.Normalize(); normalIntoFirst.Normalize();
            float cosI = Mathf.Clamp01(-Vector3.Dot(incident,normalIntoFirst));
            float ratio = c2 / c1; // Acoustic Snell law: sin(theta)/c remains constant.
            float sinT2 = ratio * ratio * (1 - cosI * cosI);
            if (sinT2 >= 1) { transmitted = Vector3.zero; reflection = 1; transmission = 0; return false; }
            float cosT = Mathf.Sqrt(Mathf.Max(0,1 - sinT2));
            transmitted = (ratio * incident + (ratio * cosI - cosT) * normalIntoFirst).normalized;
            float z1 = rho1 * c1, z2 = rho2 * c2;
            float denominator = Mathf.Max(0.001f,z2*cosI+z1*cosT);
            reflection = (z2*cosI-z1*cosT)/denominator;
            transmission = 2*z2*cosI/denominator; // Pressure, not energy; can exceed one.
            return true;
        }
        // Fluid / elastic solid impedance includes longitudinal AND shear waves (paper Sec. 3.4.3).
        // Complex cosines handle critical angles. Magnitude is used by the seven-band estimator.
        public static float ElasticReflection(float cosI, float waterSpeed, float waterDensity, float solidDensity, float cL, float cS)
        {
            double ci = Math.Max(0.0001,Math.Min(1,cosI)), sinI2 = 1-ci*ci;
            Complex cosL = Complex.Sqrt(new Complex(1-cL*cL/(waterSpeed*waterSpeed)*sinI2,0));
            Complex cosS = Complex.Sqrt(new Complex(1-cS*cS/(waterSpeed*waterSpeed)*sinI2,0));
            double sinS2 = cS*cS/(waterSpeed*waterSpeed)*sinI2;
            if (cosL.Magnitude < 1e-8) cosL += new Complex(1e-8,0);
            if (cosS.Magnitude < 1e-8) cosS += new Complex(1e-8,0);
            Complex solidZ = solidDensity*(cL*Math.Pow(1-2*sinS2,2)/cosL+cS*(4*sinS2*(1-sinS2))/cosS);
            double fluidZ = waterDensity*waterSpeed/ci;
            Complex r = (solidZ-fluidZ)/(solidZ+fluidZ);
            if (double.IsNaN(r.Magnitude) || double.IsInfinity(r.Magnitude)) return 1;
            return (float)Math.Min(1,r.Magnitude);
        }
        public static bool SphereEntry(Vector3 p, Vector3 direction, Vector3 center, float radius, float segmentLength, out float distance)
        {
            Vector3 q = p-center; float b = Vector3.Dot(q,direction), c = q.sqrMagnitude-radius*radius;
            float delta = b*b-c;
            distance = delta < 0 ? -1 : -b-Mathf.Sqrt(delta);
            return distance >= 0 && distance <= segmentLength;
        }
        public static Vector3 FibonacciDirection(int index, int count)
        {
            float y = 1 - 2*(index+0.5f)/count, radius = Mathf.Sqrt(1-y*y), angle = index*2.39996323f;
            return new Vector3(Mathf.Cos(angle)*radius,y,Mathf.Sin(angle)*radius);
        }
    }
}
