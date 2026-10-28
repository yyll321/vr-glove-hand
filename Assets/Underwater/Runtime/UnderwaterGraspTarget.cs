using System.Collections.Generic;
using UnityEngine;
namespace UnderwaterBaseline
{
    [RequireComponent(typeof(Rigidbody),typeof(BoxCollider))]
    public sealed class UnderwaterGraspTarget : MonoBehaviour
    {
        public string targetId;public bool seabed;public bool Held {get;private set;}
        public bool Delivered {get;private set;}
        public float liftedHeight=.12f;
        public Vector3 InitialPosition {get;private set;}
        public int GraspCount {get;private set;}
        public int DeliveryCount {get;private set;}
        public Rigidbody Body {get;private set;}
        public BoxCollider Shape {get;private set;}
        public int PendingHandPairs=>ignoredHand.Count;
        public float ReleasedAt {get;private set;}=-100;
        Transform home;
        struct Pair{public Collider hand;public bool wasIgnored;}
        readonly List<Pair> ignoredHand=new List<Pair>();
        void Awake(){Body=GetComponent<Rigidbody>();Shape=GetComponent<BoxCollider>();InitialPosition=transform.position;home=transform.parent;}
        public void Attach(Transform wrist)
        {
            Held=true;Delivered=false;GraspCount++;Body.velocity=Body.angularVelocity=Vector3.zero;
            Body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;Body.isKinematic=true;
            transform.SetParent(wrist,true);
        }
        public void IgnoreRobotContacts(Collider[] robot)
        {
            RestorePairs(true);
            foreach(var c in robot)if(c && c!=Shape && !c.transform.IsChildOf(transform))
            {
                ignoredHand.Add(new Pair{hand=c,wasIgnored=Physics.GetIgnoreCollision(c,Shape)});
                Physics.IgnoreCollision(c,Shape,true);
            }
        }
        public void Release()
        {
            Held=false;ReleasedAt=Time.unscaledTime;transform.SetParent(home,true);
            Shape.enabled=true;Shape.isTrigger=false;Body.detectCollisions=true;
            Body.isKinematic=false;Body.useGravity=true;Body.constraints=RigidbodyConstraints.None;
            Body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            Body.velocity=Body.angularVelocity=Vector3.zero;Body.WakeUp();
            // Environment/sand collision is always enabled. Restore each temporary
            // robot pair only after that shape has cleared the released item, so
            // embedded fingers cannot trap or explosively eject it on opening.
            RestorePairs(false);
        }
        void FixedUpdate(){if(!Held && ignoredHand.Count>0)RestorePairs(false);}
        void RestorePairs(bool force)
        {
            if(!Shape)return;
            for(int i=ignoredHand.Count-1;i>=0;i--)
            {
                var pair=ignoredHand[i];var c=pair.hand;
                if(!c){ignoredHand.RemoveAt(i);continue;}
                bool clear=force || !c.enabled || !c.gameObject.activeInHierarchy;
                if(!clear)
                {
                    var padded=Shape.bounds;padded.Expand(.008f);
                    clear=!padded.Intersects(c.bounds);
                    // ClosestPoint does not support imported non-convex meshes.
                    // Their conservative bounds suffice once the dropped object
                    // clears the robot; primitive/convex digits use exact gaps.
                    bool convex=c is BoxCollider || c is SphereCollider || c is CapsuleCollider || c is MeshCollider mesh && mesh.convex;
                    if(!clear && convex)
                    {
                        bool overlap=Physics.ComputePenetration(Shape,Shape.transform.position,Shape.transform.rotation,c,c.transform.position,c.transform.rotation,out _,out _);
                        Vector3 p=c.ClosestPoint(Shape.bounds.center),q=Shape.ClosestPoint(p);
                        clear=!overlap && Vector3.Distance(p,q)>.004f;
                    }
                }
                if(clear){Physics.IgnoreCollision(c,Shape,pair.wasIgnored);ignoredHand.RemoveAt(i);}
            }
        }
        void OnDisable(){RestorePairs(true);}
        public void MarkDelivered(){if(!Held&&!Delivered){Delivered=true;DeliveryCount++;}}
    }
}
