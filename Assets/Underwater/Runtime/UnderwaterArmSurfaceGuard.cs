using System.Collections.Generic;
using UnityEngine;

namespace UnderwaterBaseline
{
    // Evaluate the actual pose of every mesh under Axis1p, rather than treating
    // all links as a rigid extension of the wrist.
    [DisallowMultipleComponent]
    public sealed class UnderwaterArmSurfaceGuard : MonoBehaviour
    {
        public PicoArmPoseDriver arm;
        public Transform protectedRoot;
        public LayerMask surfaceMask=1<<8;
        public float skin=.003f;
        public int ShapeCount=>shapes.Count;
        public string LastSurface {get;private set;}="";
        public bool QueryOverflow {get;private set;}
        sealed class Shape{public Transform link;public Bounds bounds;public Vector3 previous;public Quaternion rotation;}
        readonly List<Shape> shapes=new List<Shape>();
        readonly Collider[] nearby=new Collider[64];readonly RaycastHit[] hits=new RaycastHit[64];
        readonly Dictionary<long,float> previousDepths=new Dictionary<long,float>(),depths=new Dictionary<long,float>();
        BoxCollider queryBox;
        public void Rebuild()
        {
            shapes.Clear();if(!arm)arm=GetComponent<PicoArmPoseDriver>();
            if(!protectedRoot && arm && arm.Joints.Length>0)protectedRoot=arm.Joints[0];
            if(!protectedRoot)return;
            foreach(var filter in protectedRoot.GetComponentsInChildren<MeshFilter>(true))
                if(filter.sharedMesh && filter.gameObject.activeInHierarchy)shapes.Add(new Shape{link=filter.transform,bounds=filter.sharedMesh.bounds});
        }
        void EnsureBox()
        {
            if(queryBox)return;
            var go=new GameObject("Arm contact query shape"){hideFlags=HideFlags.HideAndDontSave,layer=2};
            go.transform.position=new Vector3(-10000,-10000,-10000);queryBox=go.AddComponent<BoxCollider>();queryBox.isTrigger=true;
        }
        void Pose(Shape s,out Vector3 centre,out Vector3 half,out Quaternion rotation)
        {
            centre=s.link.TransformPoint(s.bounds.center);rotation=s.link.rotation;var scale=s.link.lossyScale;
            half=Vector3.Scale(s.bounds.extents,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)))+Vector3.one*skin;
        }
        bool Surface(Collider c)=>c && c.enabled && !c.isTrigger && !c.transform.IsChildOf(arm.transform) &&
            (!arm.Motor || !c.transform.IsChildOf(arm.Motor.transform));
        public void BeginMotion()
        {
            if(shapes.Count==0)Rebuild();EnsureBox();if(arm.Contact && arm.Contact.Hand)arm.Contact.Hand.SyncMountedRoot();
            QueryOverflow=false;Evaluate(false);Remember();
        }
        void Remember()
        {
            previousDepths.Clear();foreach(var pair in depths)previousDepths[pair.Key]=pair.Value;
            foreach(var s in shapes){Pose(s,out s.previous,out _,out s.rotation);}
        }
        bool Evaluate(bool swept)
        {
            depths.Clear();bool blocked=false;
            foreach(var s in shapes)
            {
                Pose(s,out var centre,out var half,out var rotation);
                int count=Physics.OverlapBoxNonAlloc(centre,half,nearby,rotation,surfaceMask,QueryTriggerInteraction.Ignore);
                if(count==nearby.Length){QueryOverflow=true;return true;}
                for(int i=0;i<count;i++)if(Surface(nearby[i]))
                {
                    var c=nearby[i];queryBox.size=half*2;
                    if(Physics.ComputePenetration(queryBox,centre,rotation,c,c.transform.position,c.transform.rotation,out _,out float depth))
                    {
                        long key=((long)s.link.GetInstanceID()<<32)^(uint)c.GetInstanceID();depths[key]=Mathf.Max(depths.TryGetValue(key,out float old)?old:0,depth);
                        if(depth>skin*.5f && (!previousDepths.TryGetValue(key,out float before) || depth>before+.00005f))
                        {blocked=true;LastSurface=c.name;}
                    }
                }
                var delta=centre-s.previous;
                if(swept && delta.sqrMagnitude>1e-10f)
                {
                    count=Physics.BoxCastNonAlloc(s.previous,half,delta.normalized,hits,s.rotation,delta.magnitude,surfaceMask,QueryTriggerInteraction.Ignore);
                    if(count==hits.Length){QueryOverflow=true;return true;}
                    for(int i=0;i<count;i++)if(Surface(hits[i].collider) && hits[i].distance>1e-5f && !previousDepths.ContainsKey(((long)s.link.GetInstanceID()<<32)^(uint)hits[i].collider.GetInstanceID()))
                    {blocked=true;LastSurface=hits[i].collider.name;}
                }
            }
            return blocked;
        }
        public bool RejectStep()
        {
            if(arm.Contact && arm.Contact.Hand)arm.Contact.Hand.SyncMountedRoot();
            if(Evaluate(true))return true;Remember();return false;
        }
        public bool WouldTranslate(Vector3 delta)
        {
            BeginMotion();var old=arm.transform.position;arm.transform.position=old+delta;
            bool result=RejectStep();arm.transform.position=old;
            if(arm.Contact && arm.Contact.Hand)arm.Contact.Hand.SyncMountedRoot();return result;
        }
        void OnDestroy(){if(queryBox){if(Application.isPlaying)Destroy(queryBox.gameObject);else DestroyImmediate(queryBox.gameObject);}}
    }
}
