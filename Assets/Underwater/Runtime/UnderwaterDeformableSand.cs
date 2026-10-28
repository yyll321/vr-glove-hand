using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnderwaterBaseline
{
    // Local heightfield approximation: persistent finger furrows, a small spoil
    // rim and slow granular slumping. No particles or per-grain rigid bodies.
    [DefaultExecutionOrder(265)]
    [RequireComponent(typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider))]
    public sealed class UnderwaterDeformableSand:MonoBehaviour
    {
        public UnderwaterGraspBench bench;
        public Mesh visualTemplate,collisionTemplate;
        public int columns=180,rows=132,collisionColumns=60,collisionRows=44;
        public float width=.9f,length=.66f,maximumDepth=.135f,edgeWidth=.008f;
        public float slumpSpeed=.0015f,updateHz=20,collisionHz=5;
        public bool automaticStamping=true;
        public MeshCollider SurfaceCollider=>GetComponent<MeshCollider>();
        public float DeepestDent {get;private set;}
        public int ActiveSamples {get;private set;}
        public float LastUpdateMilliseconds {get;private set;}
        public int ColliderUpdates {get;private set;}
        Mesh visual,collision;
        Vector3[] vertices,collisionVertices;
        Vector3[] normals;Vector4[] tangents;
        float[] depths,rims;
        Collider[] fingers;
        float nextUpdate,nextCollision;
        int dirtyX0,dirtyX1,dirtyZ0,dirtyZ1;
        public void Initialize()
        {
            if(visual || !visualTemplate || !collisionTemplate)return;
            visual=Instantiate(visualTemplate);visual.name="Live sand finger impressions";visual.MarkDynamic();
            collision=Instantiate(collisionTemplate);collision.name="Live sand target support";collision.MarkDynamic();
            vertices=visual.vertices;collisionVertices=collision.vertices;depths=new float[vertices.Length];rims=new float[vertices.Length];
            normals=new Vector3[vertices.Length];tangents=new Vector4[vertices.Length];
            for(int i=0;i<normals.Length;i++){normals[i]=Vector3.up;tangents[i]=new Vector4(1,0,0,-1);}
            GetComponent<MeshFilter>().sharedMesh=visual;SurfaceCollider.sharedMesh=collision;
            fingers=bench && bench.task?bench.task.glove.GetComponentsInChildren<Collider>():new Collider[0];
            if(bench){bench.deformable=this;bench.ConfigureFiltering();}
        }
        void OnEnable(){if(Application.isPlaying)Initialize();}
        void Update()
        {
            if(!automaticStamping || Time.unscaledTime<nextUpdate)return;
            if(bench && bench.task && bench.task.arm.Motor && bench.task.arm.Motor.InSeabed!=bench.task.arm.SeabedArea)return;
            float dt=1/Mathf.Max(1,updateHz);nextUpdate=Time.unscaledTime+dt;
            Simulate(fingers,dt);
        }
        public void Simulate(Collider[] contacts,float dt)
        {
            Initialize();if(!visual)return;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            dirtyX0=columns;dirtyX1=0;dirtyZ0=rows;dirtyZ1=0;
            bool dirty=false;
            for(int i=0;i<depths.Length;i++)
            {
                float old=depths[i];depths[i]=Mathf.MoveTowards(old,0,slumpSpeed*dt);
                float oldRim=rims[i];rims[i]=Mathf.MoveTowards(rims[i],0,slumpSpeed*.35f*dt);
                if(old!=depths[i] || oldRim!=rims[i]){dirty=true;MarkDirty(i);}
            }
            if(contacts!=null)foreach(var shape in contacts)
                if(shape && shape.enabled && !shape.isTrigger && shape.gameObject.activeInHierarchy &&
                   (!(shape is MeshCollider mesh) || mesh.convex))dirty|=Stamp(shape);
            DeepestDent=0;ActiveSamples=0;
            if(dirty)
            {
                for(int i=0;i<vertices.Length;i++)
                {
                    vertices[i].y=-depths[i]+rims[i];DeepestDent=Mathf.Max(DeepestDent,depths[i]);if(depths[i]>.001f)ActiveSamples++;
                }
                UpdateVisual();
                if(Time.unscaledTime>=nextCollision){RefreshCollision();nextCollision=Time.unscaledTime+1/Mathf.Max(1,collisionHz);}
            }
            timer.Stop();LastUpdateMilliseconds=(float)timer.Elapsed.TotalMilliseconds;
        }
        bool Stamp(Collider shape)
        {
            var b=shape.bounds;float top=transform.position.y;
            if(b.min.y>=top || b.max.y<top-maximumDepth)return false;
            var localMin=transform.InverseTransformPoint(b.min);var localMax=transform.InverseTransformPoint(b.max);
            int x0=Mathf.Clamp(Mathf.FloorToInt((localMin.x-edgeWidth+width*.5f)/width*columns),1,columns-1);
            int x1=Mathf.Clamp(Mathf.CeilToInt((localMax.x+edgeWidth+width*.5f)/width*columns),1,columns-1);
            int z0=Mathf.Clamp(Mathf.FloorToInt((localMin.z-edgeWidth+length*.5f)/length*rows),1,rows-1);
            int z1=Mathf.Clamp(Mathf.CeilToInt((localMax.z+edgeWidth+length*.5f)/length*rows),1,rows-1);
            bool dirty=false;
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
            {
                int i=z*(columns+1)+x;Vector3 sample=transform.TransformPoint(new Vector3(vertices[i].x,0,vertices[i].z));
                Vector3 near=shape.ClosestPoint(new Vector3(sample.x,b.min.y,sample.z));
                // Sample the underside of the actual convex shape at this XZ.
                // A closest-point query at the global minimum Y alone collapses
                // tapered STL fingers onto a single extremal point.
                var ray=new Ray(new Vector3(sample.x,b.min.y-.002f,sample.z),Vector3.up);
                if(shape.Raycast(ray,out var hit,b.size.y+.004f))near=hit.point;
                float horizontal=new Vector2(near.x-sample.x,near.z-sample.z).magnitude;
                if(horizontal>edgeWidth)continue;
                float raw=Mathf.Clamp(top-near.y,0,maximumDepth);
                float weight=1-Mathf.SmoothStep(0,edgeWidth,horizontal);
                float dent=raw*weight;
                if(dent>depths[i]){depths[i]=dent;dirty=true;MarkDirty(i);}
                float rim=.0035f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(horizontal/edgeWidth))*Mathf.Clamp01(raw/.025f);
                if(rim>rims[i]){rims[i]=rim;dirty=true;MarkDirty(i);}
                if(horizontal<edgeWidth*.25f && rims[i]>0){rims[i]=0;dirty=true;MarkDirty(i);}
            }
            return dirty;
        }
        void UpdateVisual()
        {
            // Analytic grid derivatives avoid Unity's general triangle/tangent
            // rebuild (several milliseconds per update on this 24k vertex patch).
            for(int z=Mathf.Max(0,dirtyZ0-1);z<=Mathf.Min(rows,dirtyZ1+1);z++)for(int x=Mathf.Max(0,dirtyX0-1);x<=Mathf.Min(columns,dirtyX1+1);x++)
            {
                int a=Mathf.Max(0,x-1),b=Mathf.Min(columns,x+1),c=Mathf.Max(0,z-1),d=Mathf.Min(rows,z+1),i=z*(columns+1)+x;
                float dx=(vertices[z*(columns+1)+b].y-vertices[z*(columns+1)+a].y)/((b-a)*width/columns);
                float dz=(vertices[d*(columns+1)+x].y-vertices[c*(columns+1)+x].y)/((d-c)*length/rows);
                normals[i]=new Vector3(-dx,1,-dz).normalized;var t=new Vector3(1,dx,0).normalized;tangents[i]=new Vector4(t.x,t.y,t.z,-1);
            }
            visual.vertices=vertices;visual.normals=normals;visual.tangents=tangents;
            visual.bounds=new Bounds(new Vector3(0,-maximumDepth*.5f,0),new Vector3(width,maximumDepth+.02f,length));
        }
        void MarkDirty(int index)
        {int x=index%(columns+1),z=index/(columns+1);dirtyX0=Mathf.Min(dirtyX0,x);dirtyX1=Mathf.Max(dirtyX1,x);dirtyZ0=Mathf.Min(dirtyZ0,z);dirtyZ1=Mathf.Max(dirtyZ1,z);}
        public float HeightAt(Vector3 world)
        {
            if(depths==null)return transform.position.y;
            Vector3 p=transform.InverseTransformPoint(world);float gx=Mathf.Clamp((p.x/width+.5f)*columns,0,columns),gz=Mathf.Clamp((p.z/length+.5f)*rows,0,rows);
            int x=Mathf.Min(columns-1,(int)gx),z=Mathf.Min(rows-1,(int)gz);float fx=gx-x,fz=gz-z;
            float H(int a,int b)=>-depths[b*(columns+1)+a]+rims[b*(columns+1)+a];
            return transform.position.y+Mathf.Lerp(Mathf.Lerp(H(x,z),H(x+1,z),fx),Mathf.Lerp(H(x,z+1),H(x+1,z+1),fx),fz);
        }
        public void RefreshCollision()
        {
            if(!collision)return;
            for(int i=0;i<collisionVertices.Length;i++)
            {
                var p=transform.TransformPoint(collisionVertices[i]);
                collisionVertices[i].y=HeightAt(p)-transform.position.y-(bench?bench.visualSurfaceOffset:0);
            }
            collision.vertices=collisionVertices;collision.RecalculateBounds();
            SurfaceCollider.sharedMesh=null;SurfaceCollider.sharedMesh=collision;ColliderUpdates++;
            // Recooking a mesh may clear collision-pair state in PhysX.
            if(bench)bench.ConfigureFiltering();
        }
        [ContextMenu("Reset Finger Impressions")]
        public void ResetImpressions()
        {
            if(depths==null)return;
            System.Array.Clear(depths,0,depths.Length);System.Array.Clear(rims,0,rims.Length);
            for(int i=0;i<vertices.Length;i++)vertices[i].y=0;
            dirtyX0=dirtyZ0=0;dirtyX1=columns;dirtyZ1=rows;
            UpdateVisual();RefreshCollision();DeepestDent=0;ActiveSamples=0;
        }
        public static Mesh CreateGrid(float width,float length,int columns,int rows)
        {
            var points=new Vector3[(columns+1)*(rows+1)];var uv=new Vector2[points.Length];var triangles=new List<int>(columns*rows*6);
            for(int z=0;z<=rows;z++)for(int x=0;x<=columns;x++)
            {int i=z*(columns+1)+x;points[i]=new Vector3((float)x/columns*width-width*.5f,0,(float)z/rows*length-length*.5f);uv[i]=new Vector2(points[i].x/.12f,points[i].z/.12f);}
            for(int z=0;z<rows;z++)for(int x=0;x<columns;x++)
            {int a=z*(columns+1)+x,b=a+columns+1;triangles.Add(a);triangles.Add(b);triangles.Add(a+1);triangles.Add(a+1);triangles.Add(b);triangles.Add(b+1);}
            var mesh=new Mesh{name="Sand heightfield "+columns+"x"+rows,indexFormat=IndexFormat.UInt32};
            mesh.vertices=points;mesh.uv=uv;mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
        }
        void OnDestroy(){if(visual)Destroy(visual);if(collision)Destroy(collision);}
    }
}
