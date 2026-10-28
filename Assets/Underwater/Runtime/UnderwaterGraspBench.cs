using UnityEngine;

namespace UnderwaterBaseline
{
    // Local collision-pair filtering: leave all existing floor/hand layer rules intact.
    [DefaultExecutionOrder(270)]
    public sealed class UnderwaterGraspBench : MonoBehaviour
    {
        public UnderwaterGraspTask task;
        public BoxCollider sand;
        public Collider support;
        public float sandDepth=.06f;
        [Header("Compressible layer approximation (not a DEM soil solver)")]
        public float pressurePerDepth=18000f,viscousPressure=500f;
        public float toolForceBudget=35f,fingerForceBudget=9f,maximumResistance=200f;
        public float visualSurfaceOffset;
        public float LastToolResistance {get;private set;}
        public float LastFingerResistance {get;private set;}
        public float LastInsertionDepth {get;private set;}
        public float SurfaceHeight=>sand?sand.bounds.max.y+visualSurfaceOffset:0;
        public int IgnoredHandPairs {get;private set;}
        void Start(){ConfigureFiltering();}
        void OnEnable(){if(Application.isPlaying)ConfigureFiltering();}
        [ContextMenu("Refresh sand / robot collision pairs")]
        public void ConfigureFiltering()
        {
            if(!task || !task.arm || !sand)return;
            IgnoredHandPairs=0;
            foreach(var c in task.arm.GetComponentsInChildren<Collider>(true))
                if(c && c.enabled){Physics.IgnoreCollision(c,sand,true);IgnoredHandPairs++;}
            // Query protection sees layer 8 support, but never the layer 11 sand.
            // Targets keep normal collisions with sand and the support below it.
        }

        public float Resistance(float depth,float area,float speed,bool withdrawing)
        {
            return Mathf.Clamp((pressurePerDepth*Mathf.Max(0,depth)+viscousPressure*Mathf.Max(0,speed))*Mathf.Max(0,area)*(withdrawing?.25f:1),0,maximumResistance);
        }
        public float Sample(Bounds shape,Vector3 velocity,out float depth)
        {
            depth=0;if(!sand || velocity.sqrMagnitude<1e-10f)return 0;
            var volume=sand.bounds;float top=SurfaceHeight;
            float x=Mathf.Max(0,Mathf.Min(shape.max.x,volume.max.x)-Mathf.Max(shape.min.x,volume.min.x));
            float z=Mathf.Max(0,Mathf.Min(shape.max.z,volume.max.z)-Mathf.Max(shape.min.z,volume.min.z));
            if(x<=0 || z<=0 || shape.max.y<volume.min.y || shape.min.y>=top)return 0;
            depth=Mathf.Clamp(top-shape.min.y,0,sandDepth);
            float submergedHeight=Mathf.Min(shape.size.y,depth);
            Vector3 dir=velocity.normalized;
            float area=Mathf.Abs(dir.y)*x*z+Mathf.Abs(dir.x)*z*submergedHeight+Mathf.Abs(dir.z)*x*submergedHeight;
            return Resistance(depth,area,velocity.magnitude,velocity.y>0);
        }
        public Vector3 ResistTool(Wa100VisibilityGuard hand,Vector3 velocity,out float force)
        {
            force=0;LastInsertionDepth=0;
            // Each collider belongs to one articulation link; avoid overlapping
            // renderer duplicates and use the physical collision geometry.
            foreach(var c in hand.GetComponentsInChildren<Collider>())if(c.enabled && !c.isTrigger)
            {force+=Sample(c.bounds,velocity,out float depth);LastInsertionDepth=Mathf.Max(depth,LastInsertionDepth);}
            LastToolResistance=force=Mathf.Min(force,maximumResistance);
            return velocity/(1+force/Mathf.Max(1,toolForceBudget));
        }
        public float FingerMobility(ArticulationBody joint,float angularVelocity)
        {
            float force=0;Vector3 pivot=joint.transform.TransformPoint(joint.anchorPosition);
            Vector3 axis=joint.transform.TransformDirection(joint.anchorRotation*Vector3.right);
            foreach(var c in joint.GetComponentsInChildren<Collider>())if(c.enabled && !c.isTrigger)
            {
                Vector3 v=Vector3.Cross(axis,c.bounds.center-pivot)*angularVelocity*Mathf.Deg2Rad;
                force+=Sample(c.bounds,v,out float depth);LastInsertionDepth=Mathf.Max(LastInsertionDepth,depth);
            }
            LastFingerResistance=Mathf.Min(force,maximumResistance);
            return 1/(1+LastFingerResistance/Mathf.Max(1,fingerForceBudget));
        }
    }
}
