using UnityEngine;

namespace UnderwaterBaseline
{
    // +Z is expelled water direction. This is a local velocity input, not vehicle thrust.
    public sealed class UnderwaterWakeSource : MonoBehaviour
    {
        [Range(0,1)] public float throttle;
        [Min(0.05f)] public float radius=0.16f;
        [Min(0.2f)] public float length=2.8f;
        [Min(0)] public float exitSpeed=0.65f;
        public bool seabed;
        public bool demoLinked=true;
        public Vector3 VelocityAt(Vector3 world,float time)
        {
            // Metres stay metres even when the imported ROV parent is scaled.
            Vector3 p=Quaternion.Inverse(transform.rotation)*(world-transform.position);
            if(throttle<=0 || p.z<0 || p.z>length)return Vector3.zero;
            float spread=radius+p.z*0.20f;
            float gaussian=Mathf.Exp(-(p.x*p.x+p.y*p.y)/(spread*spread));
            float falloff=Mathf.Pow(radius/spread,1.1f)*Mathf.SmoothStep(1,0,p.z/length);
            float flutter=1+0.12f*Mathf.Sin(time*2.3f+p.z*4.7f);
            Vector3 swirl=new Vector3(-p.y,p.x,0)*(0.15f/(spread+0.01f));
            return transform.TransformDirection(Vector3.forward+swirl)*(exitSpeed*throttle*gaussian*falloff*flutter);
        }
    }
}
