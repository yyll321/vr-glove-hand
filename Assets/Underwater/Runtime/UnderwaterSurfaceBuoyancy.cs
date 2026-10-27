using UnityEngine;

namespace UnderwaterBaseline
{
    // Hydrostatic integration over a closed, outward-wound displacement proxy.
    // The open-frame visual ROV is NOT a watertight displacement mesh.
    public sealed class UnderwaterSurfaceBuoyancy : MonoBehaviour
    {
        public MeshFilter displacementHull;
        public Rigidbody body;
        public Transform vehicle;
        public Quaternion bodyAxesCorrection=Quaternion.identity;
        public Vector3 hullOffsetMetres=new Vector3(0,1.3f,0);
        public bool applyForces;
        [System.NonSerialized] public bool evaluatedByHydrodynamics;
        public float waterY=10,fluidDensity=1025;
        public Vector3 Force {get;private set;}
        public Vector3 Torque {get;private set;}
        public float WettedArea {get;private set;}
        public string Status {get;private set;}="Closed hull proxy required";
        Mesh cached;Vector3[] vertices;int[] triangles;
        readonly Vector3[] clipped=new Vector3[5];

        public static int ClipBelow(Vector3 a,Vector3 b,Vector3 c,float height,Vector3[] output)
        {
            int count=0;Vector3 previous=c;
            for(int i=0;i<3;i++)
            {
                Vector3 current=i==0?a:i==1?b:c;
                bool inside=current.y<=height,wasInside=previous.y<=height;
                if(inside!=wasInside)
                {
                    float t=(height-previous.y)/(current.y-previous.y);
                    output[count++]=Vector3.LerpUnclamped(previous,current,t);
                }
                if(inside)output[count++]=current;
                previous=current;
            }
            return count;
        }
        // Pressure is linear over each clipped triangle. Its area integral and
        // first moment can be evaluated exactly without the centroid torque bias.
        public static void Triangle(Vector3 a,Vector3 b,Vector3 c,float height,float rho,float gravity,
            out Vector3 force,out Vector3 pressureCentre,out float area)
        {
            Vector3 cross=Vector3.Cross(b-a,c-a);area=cross.magnitude*0.5f;
            float da=Mathf.Max(0,height-a.y),db=Mathf.Max(0,height-b.y),dc=Mathf.Max(0,height-c.y),sum=da+db+dc;
            force=-cross*(0.5f*rho*gravity*sum/3);
            pressureCentre=sum>1e-8f?((a+b+c)*sum+a*da+b*db+c*dc)/(4*sum):(a+b+c)/3;
        }
        void FixedUpdate(){if(!evaluatedByHydrodynamics)Evaluate(applyForces);}
        public void Evaluate(bool apply=false)
        {
            Force=Torque=Vector3.zero;WettedArea=0;
            if(!displacementHull || !displacementHull.sharedMesh){Status="Closed hull proxy required";return;}
            if(vehicle)
            {
                Quaternion axes=vehicle.rotation*bodyAxesCorrection;
                displacementHull.transform.SetPositionAndRotation(vehicle.position+axes*hullOffsetMetres,axes);
            }
            var mesh=displacementHull.sharedMesh;
            if(cached!=mesh)
            {
                if(!mesh.isReadable){Status="Hull mesh must have Read/Write enabled";return;}
                cached=mesh;vertices=mesh.vertices;triangles=mesh.triangles;
            }
            Vector3 centre=body?body.worldCenterOfMass:displacementHull.transform.position;
            float gravity=Mathf.Abs(Physics.gravity.y);bool applyNow=apply && body && !body.isKinematic;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var t=displacementHull.transform;
                int count=ClipBelow(t.TransformPoint(vertices[triangles[i]]),t.TransformPoint(vertices[triangles[i+1]]),t.TransformPoint(vertices[triangles[i+2]]),waterY,clipped);
                for(int j=1;j<count-1;j++)
                {
                    Triangle(clipped[0],clipped[j],clipped[j+1],waterY,fluidDensity,gravity,out var force,out var point,out float area);
                    Force+=force;Torque+=Vector3.Cross(point-centre,force);WettedArea+=area;
                    if(applyNow)body.AddForceAtPosition(force,point,ForceMode.Force);
                }
            }
            Status=applyNow?"Surface hydrostatics applied":"Surface hydrostatics estimate only";
        }
    }
}
