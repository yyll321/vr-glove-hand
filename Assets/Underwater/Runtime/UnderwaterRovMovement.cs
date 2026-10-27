using UnityEngine;
using UnityEngine.InputSystem;

namespace UnderwaterBaseline
{
    // Horizontal inspection motion. This moves the ROV, never the XR origin.
    [DefaultExecutionOrder(40),DisallowMultipleComponent]
    public sealed class UnderwaterRovMovement : MonoBehaviour
    {
        public UnderwaterMotor motor;
        public bool seabed;
        public Quaternion bodyAxesCorrection=Quaternion.identity;
        [Min(.05f)] public float speed=.65f;
        public LayerMask obstacleMask=1<<8;
        public Vector3 collisionCentre=new Vector3(0,1.1f,0);
        public Vector3 collisionHalfSize=new Vector3(.8f,.65f,1.05f);
        public Wa100SurfaceContact handContact;
        public Vector3 Velocity {get;private set;}
        public bool Blocked {get;private set;}
        public string ContactObstacle {get;private set;}="";
        public string Status {get;private set;}="Right stick / arrow keys move ROV";
        Vector2 input;
        readonly RaycastHit[] hits=new RaycastHit[64];
        void Awake()
        {
            if(!handContact)foreach(var arm in FindObjectsOfType<PicoArmPoseDriver>())
                if(arm.SeabedArea==seabed && arm.Contact){handContact=arm.Contact;break;}
        }
        void Update()
        {
            input=Vector2.zero;
            if(!motor || motor.InSeabed!=seabed)return;
            var c=motor.Controllers;
            if(c!=null && !c.RightGrip && !c.TeleportHeld && !c.RadarModifier)input=c.Turn;
            var key=Keyboard.current;
            if(key!=null)
            {
                Vector2 desktop=new Vector2((key.rightArrowKey.isPressed?1:0)-(key.leftArrowKey.isPressed?1:0),
                    (key.upArrowKey.isPressed?1:0)-(key.downArrowKey.isPressed?1:0));
                if(desktop.sqrMagnitude>0)input=desktop;
            }
            input=Vector2.ClampMagnitude(input,1);if(input.magnitude<.2f)input=Vector2.zero;
        }
        void FixedUpdate(){Move(input,Time.fixedDeltaTime);}
        public void Move(Vector2 command,float dt)
        {
            Velocity=Vector3.zero;Blocked=false;ContactObstacle="";
            if(dt<=0 || !isActiveAndEnabled)return;
            Quaternion axes=transform.rotation*bodyAxesCorrection;
            Vector3 forward=Vector3.ProjectOnPlane(axes*Vector3.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
            Vector3 right=Vector3.Cross(Vector3.up,forward);
            Vector3 desired=(right*command.x+forward*command.y)*speed*Mathf.Min(dt,.05f);
            if(desired.sqrMagnitude<1e-10f){Status="ROV holding position";return;}
            var body=GetComponent<Rigidbody>();
            if(body && !body.isKinematic){Status="Dynamic body owns motion; inspection control paused";return;}
            Vector3 old=transform.position;
            Vector3 move=Constrain(desired,axes);
            Vector2 size=UnderwaterRegionSettings.Size(seabed);float centre=seabed?62:0;
            var target=old+move;float margin=1.4f;
            target.x=Mathf.Clamp(target.x,centre-size.x/2+margin,centre+size.x/2-margin);
            target.z=Mathf.Clamp(target.z,-size.y/2+margin,size.y/2-margin);
            if(body)body.position=target;else transform.position=target;
            Velocity=(target-old)/dt;Blocked=(target-old-desired).sqrMagnitude>1e-8f;
            Status=Blocked?"ROV blocked by obstacle / boundary":"ROV translating in body horizontal axes";
        }
        Vector3 Constrain(Vector3 desired,Quaternion axes)
        {
            float distance=desired.magnitude;Vector3 direction=desired/distance;
            Vector3 centre=transform.position+axes*collisionCentre;
            int count=Physics.BoxCastNonAlloc(centre,collisionHalfSize,direction,hits,axes,distance+.025f,obstacleMask,QueryTriggerInteraction.Ignore);
            if(count==hits.Length)return Vector3.zero;
            float allowed=distance;
            for(int i=0;i<count;i++)
            {
                var c=hits[i].collider;if(!c || c.transform==transform || c.transform.IsChildOf(transform))continue;
                allowed=Mathf.Min(allowed,Mathf.Max(0,hits[i].distance-.025f));
            }
            Vector3 accepted=direction*allowed;
            if(handContact && handContact.Arm && handContact.Arm.Wrist && accepted.sqrMagnitude>1e-10f)
            {
                handContact.BeginMotion();var wrist=handContact.Arm.Wrist;
                bool already=handContact.Blocked(wrist.position,wrist.rotation);Vector3 normal=handContact.Normal;
                bool sweep=handContact.SweepBlocked(wrist.position,wrist.rotation,wrist.position+accepted);
                bool end=handContact.Blocked(wrist.position+accepted,wrist.rotation);
                if(sweep || (!already && end) || (already && Vector3.Dot(accepted,normal)<-1e-5f))
                {ContactObstacle=handContact.SurfaceName+" already="+already+" sweep="+sweep+" end="+end+" normal="+normal;handContact.Pulse();return Vector3.zero;}
            }
            return accepted;
        }
        void OnDisable(){input=Vector2.zero;Velocity=Vector3.zero;}
        void OnDrawGizmosSelected()
        {
            var old=Gizmos.matrix;Gizmos.matrix=Matrix4x4.TRS(transform.position,transform.rotation*bodyAxesCorrection,Vector3.one);
            Gizmos.color=Color.yellow;Gizmos.DrawWireCube(collisionCentre,collisionHalfSize*2);Gizmos.matrix=old;
        }
    }
}
