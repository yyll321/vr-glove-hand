using UnityEngine;
namespace UnderwaterBaseline
{
    // Articulation collisions arrive at individual link bodies, not the ROV root.
    public sealed class Wa100ContactRelay:MonoBehaviour
    {
        public Wa100ContactHaptics owner;
        void OnCollisionEnter(Collision collision){if(owner)owner.ReportContact(collision.collider);}
        void OnCollisionStay(Collision collision){if(owner)owner.ReportContact(collision.collider);}
    }
}
