using UnityEngine;

namespace UnderwaterBaseline
{
    /// <summary>
    /// Reduced-order hydrodynamics for interaction and instrumentation.  It uses
    /// body-frame projected areas and a quadratic drag law, with optional buoyancy
    /// and angular damping.  This is intentionally an interactive approximation,
    /// not a substitute for CFD or vehicle certification data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnderwaterHydrodynamics : MonoBehaviour
    {
        public UnderwaterPresence presence;
        public UnderwaterFluidVolume volume;
        public Rigidbody body;
        public Quaternion bodyAxesCorrection=Quaternion.identity;
        public UnderwaterSurfaceBuoyancy surfaceBuoyancy;
        public bool applyForces;
        public bool applyBuoyancy = true;
        [Min(0.01f)] public float displacedVolume = 0.065f;
        [Min(1)] public float fluidDensity = 1025f;
        [Min(0)] public float frontalArea = 0.34f;
        [Min(0)] public float lateralArea = 0.48f;
        [Min(0)] public float verticalArea = 0.30f;
        [Range(0, 3)] public float dragCoefficient = 0.9f;
        [Tooltip("Linear angular resistance in N m s per local axis. Requires calibration for an actual vehicle.")]
        public Vector3 angularResistance = new Vector3(8,10,8);
        public Vector3 centerOfBuoyancy;
        public float surfaceY=10, bottomY=0;
        [Min(0.01f)] public float hullHeight=0.8f;
        public Vector3 EstimatedRelativeVelocity { get; private set; }
        public Vector3 EstimatedDragForce { get; private set; }
        public Vector3 EstimatedBuoyancy { get; private set; }
        public Vector3 EstimatedTorque { get; private set; }
        public float SubmergedFraction { get; private set; }

        void Awake()
        {
            if (!body) body = GetComponent<Rigidbody>();
            if(surfaceBuoyancy){surfaceBuoyancy.evaluatedByHydrodynamics=true;surfaceBuoyancy.body=body;surfaceBuoyancy.applyForces=false;}
            if (!presence) presence = FindObjectOfType<UnderwaterPresence>();
            if (!volume && presence && presence.fluidVolumes != null)
                for (int i = 0; i < presence.fluidVolumes.Length; i++)
                    if (presence.fluidVolumes[i] && presence.fluidVolumes[i].Contains(transform.position)) { volume = presence.fluidVolumes[i]; break; }
        }

        void FixedUpdate()
        {
            float height=Mathf.Max(0.01f,hullHeight);
            Quaternion axes=transform.rotation*bodyAxesCorrection;
            Vector3 buoyancyPoint=transform.position+axes*centerOfBuoyancy;
            SubmergedFraction=Mathf.Clamp01((surfaceY-(buoyancyPoint.y-height*0.5f))/height);
            if(buoyancyPoint.y+height*0.5f<bottomY)SubmergedFraction=0;
            if(volume && !volume.Contains(new Vector3(buoyancyPoint.x,Mathf.Clamp(buoyancyPoint.y,bottomY,surfaceY),buoyancyPoint.z)))SubmergedFraction=0;
            Vector3 current = presence ? presence.CurrentAt(buoyancyPoint, Time.time) :
                volume ? volume.SampleVelocity(buoyancyPoint) : Vector3.zero;
            Vector3 velocity = body ? body.GetPointVelocity(buoyancyPoint) : Vector3.zero;
            Vector3 relative = velocity - current;
            EstimatedRelativeVelocity = relative;
            Vector3 local = Quaternion.Inverse(axes)*relative;
            Vector3 localForce = new Vector3(
                -Drag(local.x, lateralArea),
                -Drag(local.y, verticalArea),
                -Drag(local.z, frontalArea));
            EstimatedDragForce = axes*localForce*SubmergedFraction;
            EstimatedBuoyancy = -Physics.gravity * (fluidDensity * displacedVolume * SubmergedFraction);
            Vector3 angular = body ? body.angularVelocity : Vector3.zero;
            Vector3 localAngular=Quaternion.Inverse(axes)*angular;
            EstimatedTorque=axes*(-Vector3.Scale(localAngular,Vector3.Max(Vector3.zero,angularResistance)))*SubmergedFraction;
            if(surfaceBuoyancy && surfaceBuoyancy.isActiveAndEnabled)
            {
                // This component owns any force application. Never apply surface and
                // lumped buoyancy simultaneously, regardless of script execution order.
                surfaceBuoyancy.applyForces=false;surfaceBuoyancy.Evaluate(false);
                EstimatedBuoyancy=surfaceBuoyancy.Force;
                EstimatedTorque+=surfaceBuoyancy.Torque;
            }
            if (!applyForces || !body || body.isKinematic) return;
            body.AddForce(EstimatedDragForce, ForceMode.Force);
            if (applyBuoyancy)
            {
                if(surfaceBuoyancy && surfaceBuoyancy.isActiveAndEnabled)body.AddForce(EstimatedBuoyancy,ForceMode.Force);
                else body.AddForceAtPosition(EstimatedBuoyancy,buoyancyPoint,ForceMode.Force);
            }
            Vector3 torque=EstimatedTorque;
            if(!applyBuoyancy && surfaceBuoyancy && surfaceBuoyancy.isActiveAndEnabled)torque-=surfaceBuoyancy.Torque;
            body.AddTorque(torque, ForceMode.Force);
        }

        float Drag(float component, float area)
        {
            return 0.5f * fluidDensity * dragCoefficient * Mathf.Max(0, area) * component * Mathf.Abs(component);
        }

        public float DynamicPressure
        {
            get { return 0.5f * fluidDensity * EstimatedRelativeVelocity.sqrMagnitude; }
        }
    }
}
