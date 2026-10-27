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
        Transform home;
        void Awake(){Body=GetComponent<Rigidbody>();Shape=GetComponent<BoxCollider>();InitialPosition=transform.position;home=transform.parent;}
        public void Attach(Transform wrist)
        {
            Held=true;Delivered=false;GraspCount++;Body.velocity=Body.angularVelocity=Vector3.zero;Body.isKinematic=true;
            transform.SetParent(wrist,true);
        }
        public void Release()
        {
            Held=false;transform.SetParent(home,true);Body.isKinematic=false;Body.velocity=Body.angularVelocity=Vector3.zero;
        }
        public void MarkDelivered(){if(!Held&&!Delivered){Delivered=true;DeliveryCount++;}}
    }
}
