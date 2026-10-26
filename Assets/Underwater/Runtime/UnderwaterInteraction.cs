using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnderwaterBaseline
{
    [DefaultExecutionOrder(200)]
    public sealed class UnderwaterInteraction : MonoBehaviour
    {
        public UnderwaterPresence presence;
        public UnderwaterMotor motor;
        public UnderwaterWakeSource[] wakes=Array.Empty<UnderwaterWakeSource>();
        public PicoArmPoseDriver[] arms=Array.Empty<PicoArmPoseDriver>();
        public ParticleSystem sedimentParticles;
        public Light[] workLights=Array.Empty<Light>();
        public bool lightsEnabled=true, wakeDemo;
        [Range(0,1)] public float demoThrottle=0.55f;
        public LayerMask groundMask=1<<8;
        [Min(0)] public float seaErodibility=1, poolErodibility=0.055f;
        [Range(0.001f,0.1f)] public float settlingRate=0.025f;
        public float ListenerFlow { get; private set; }
        public float ListenerSediment { get; private set; }
        public int ActivePlumes { get; private set; }
        public int EmittedGrains { get; private set; }
        public float ActiveThrottle { get; private set; }
        public float ListenerLampGain { get; private set; }
        public bool suppressAutomaticInputs;

        struct Plume { public Vector3 position; public float radius,mass,age,lift; }
        const int Capacity=12;
        readonly Plume[] plumes=new Plume[Capacity];
        readonly Vector4[] plumeVectors=new Vector4[Capacity],densityVectors=new Vector4[Capacity];
        readonly Vector4[] lampPositions=new Vector4[2],lampDirections=new Vector4[2],lampColors=new Vector4[2];
        readonly Vector3[] lastWrist=new Vector3[2];
        readonly float[] nextArmEmission=new float[2];
        readonly bool[] previousContact=new bool[2];
        readonly ParticleSystem.Particle[] grains=new ParticleSystem.Particle[1200];
        float nextTick,previousTick;
        int nextPlume;

        void Start()
        {
            if(presence)presence.interaction=this;
            for(int i=0;i<arms.Length && i<2;i++)if(arms[i] && arms[i].Wrist)lastWrist[i]=arms[i].Wrist.position;
            SetWakeDemo(wakeDemo);
            if(sedimentParticles)sedimentParticles.Play();
            previousTick=Time.time;
        }

        public Vector3 WakeAt(Vector3 world,float time)
        {
            Vector3 velocity=Vector3.zero;
            foreach(var wake in wakes)if(wake && wake.enabled && wake.gameObject.activeInHierarchy)velocity+=wake.VelocityAt(world,time);
            return Vector3.ClampMagnitude(velocity,1.2f);
        }

        public float ConcentrationAt(Vector3 world)
        {
            float density=0;
            for(int i=0;i<Capacity;i++)
            {
                var plume=plumes[i];if(plume.mass<=0)continue;
                float q=Mathf.Clamp01(1-(world-plume.position).sqrMagnitude/(plume.radius*plume.radius));
                density+=plume.mass*q*q;
            }
            return Mathf.Min(3,density);
        }

        public void SetWakeDemo(bool enabled)
        {
            wakeDemo=enabled;
            foreach(var wake in wakes)if(wake && wake.demoLinked)wake.throttle=enabled?demoThrottle:0;
        }
        public float ThrottleFor(bool seabed)
        {
            float throttle=0;foreach(var wake in wakes)if(wake && wake.isActiveAndEnabled && wake.seabed==seabed)throttle=Mathf.Max(throttle,wake.throttle);
            return throttle;
        }
        public static float MotorGain(float throttle)=>Mathf.Lerp(0.28f,1,Mathf.Clamp01(throttle));

        // A compact advected concentration puff. Pool and sand use different erosion
        // response; no fake bubbling is injected by a submerged electric thruster.
        public void Disturb(Vector3 bottom,float intensity,bool seabed)
        {
            float amount=Mathf.Clamp01(intensity)*(seabed?seaErodibility:poolErodibility);
            if(amount<0.0001f)return;
            int merge=-1;
            for(int i=0;i<Capacity;i++)if(plumes[i].mass>0 && Vector3.Distance(plumes[i].position,bottom+Vector3.up*0.25f)<0.6f){merge=i;break;}
            int at=merge>=0?merge:nextPlume++%Capacity;
            if(merge<0)plumes[at]=new Plume{position=bottom+Vector3.up*0.25f,radius=0.5f};
            var puff=plumes[at];puff.mass=Mathf.Min(2.5f,puff.mass+amount*0.65f);puff.age=0;
            // Short-lived entrainment at impact lifts grains before gravity dominates.
            puff.lift=Mathf.Max(puff.lift,0.14f+amount*0.08f);plumes[at]=puff;
            ActivePlumes=0;for(int i=0;i<Capacity;i++)if(plumes[i].mass>0)ActivePlumes++;
            if(!sedimentParticles)return;
            int count=Mathf.Clamp(Mathf.RoundToInt(amount*50),1,50);
            for(int i=0;i<count;i++)
            {
                Vector3 jitter=UnityEngine.Random.insideUnitSphere*0.22f;jitter.y=Mathf.Abs(jitter.y);
                var emit=new ParticleSystem.EmitParams
                {
                    position=bottom+Vector3.up*0.025f+jitter,
                    velocity=(presence?presence.CurrentAt(bottom,Time.time):Vector3.zero)+Vector3.up*UnityEngine.Random.Range(0.07f,0.16f),
                    startSize=UnityEngine.Random.Range(0.002f,0.006f),
                    startLifetime=UnityEngine.Random.Range(7,15),
                    startColor=seabed?new Color(0.38f,0.31f,0.20f,0.5f):new Color(0.55f,0.60f,0.53f,0.18f)
                };
                sedimentParticles.Emit(emit,1);EmittedGrains++;
            }
        }

        public void ClearSediment()
        {
            Array.Clear(plumes,0,Capacity);ActivePlumes=0;EmittedGrains=0;
            if(sedimentParticles)sedimentParticles.Clear();Publish();
        }

        public void Simulate(float dt,float time)
        {
            dt=Mathf.Clamp(dt,0,0.2f);ActivePlumes=0;
            for(int i=0;i<Capacity;i++)
            {
                var p=plumes[i];if(p.mass<=0)continue;
                p.age+=dt;
                Vector3 flow=presence?presence.CurrentAt(p.position,time):Vector3.zero;
                p.position+=(flow+Vector3.up*(p.lift-settlingRate))*dt;
                p.lift*=Mathf.Exp(-dt*0.9f);
                if(FindGround(p.position,out var floor))p.position.y=Mathf.Max(floor.point.y+p.radius*0.1f,p.position.y);
                p.position.y=Mathf.Max(p.radius*0.1f,p.position.y);
                float oldRadius=p.radius;p.radius=Mathf.Min(1.8f,p.radius+dt*0.045f);
                p.mass*=Mathf.Pow(oldRadius/p.radius,3)*Mathf.Exp(-dt*(0.065f+settlingRate));
                if(p.mass<0.005f)p.mass=0;
                if(p.mass>0)ActivePlumes++;
                plumes[i]=p;
            }
        }

        void Update()
        {
            var key=Keyboard.current;
            if(!suppressAutomaticInputs && key!=null && key.nKey.wasPressedThisFrame)SetWakeDemo(!wakeDemo);
            if(!suppressAutomaticInputs && key!=null && key.lKey.wasPressedThisFrame)lightsEnabled=!lightsEnabled;
            // Right secondary button is unused by the arm: toggle the same wake demo.
            var right=UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton,out bool button);
            button|=motor && motor.Controllers!=null && motor.Controllers.Down;
            if(!suppressAutomaticInputs && button && !previousDemoButton)SetWakeDemo(!wakeDemo);previousDemoButton=button;
            right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton,out bool lightButton);
            lightButton|=motor && motor.Controllers!=null && motor.Controllers.Up;
            if(!suppressAutomaticInputs && lightButton && !previousLightButton)lightsEnabled=!lightsEnabled;previousLightButton=lightButton;
            if(Time.time<nextTick)return;
            float dt=Mathf.Clamp(Time.time-previousTick,0.01f,0.2f);previousTick=Time.time;nextTick=Time.time+0.1f;
            Simulate(dt,Time.time);
            if(!suppressAutomaticInputs)EmitFromOperations(dt);
            UpdateGrains(dt);
            if(!motor || !motor.origin.Camera){Publish();return;}
            Vector3 eye=motor.origin.Camera.transform.position;
            ListenerFlow=presence?presence.CurrentAt(eye,Time.time).magnitude:0;
            ListenerSediment=ConcentrationAt(eye);ActiveThrottle=0;ListenerLampGain=0;
            ActiveThrottle=ThrottleFor(motor.InSeabed);
            int tier=presence && presence.environment?presence.environment.quality:1;
            if(sedimentParticles){var main=sedimentParticles.main;main.maxParticles=tier==0?400:tier==1?800:1200;}
            for(int i=0;i<workLights.Length && i<2;i++)
            {
                var lamp=workLights[i];if(!lamp)continue;lamp.enabled=lightsEnabled;
                lamp.shadows=tier==0?LightShadows.None:LightShadows.Soft;
                Vector3 delta=eye-lamp.transform.position;
                if(lamp.enabled && delta.magnitude<lamp.range)
                    ListenerLampGain=Mathf.Max(ListenerLampGain,Mathf.InverseLerp(Mathf.Cos(lamp.spotAngle*0.5f*Mathf.Deg2Rad),1,Vector3.Dot(delta.normalized,lamp.transform.forward)));
            }
            Publish();
        }
        bool previousDemoButton,previousLightButton;

        void EmitFromOperations(float dt)
        {
            foreach(var wake in wakes)
            {
                if(!wake || wake.throttle<=0)continue;
                Vector3 end=wake.transform.position+wake.transform.forward*wake.length*0.7f;
                if(FindGround(end,out RaycastHit hit) && end.y-hit.point.y<0.65f)
                    Disturb(hit.point,wake.throttle*dt*1.4f,wake.seabed);
            }
            for(int i=0;i<arms.Length && i<2;i++)
            {
                var arm=arms[i];if(!arm || !arm.Wrist)continue;Vector3 position=arm.Wrist.position;
                float speed=(position-lastWrist[i]).magnitude/dt;lastWrist[i]=position;
                bool entered=arm.SurfaceContact && !previousContact[i];previousContact[i]=arm.SurfaceContact;
                if(!arm.Contact || Time.time<nextArmEmission[i])continue;
                if(entered || (speed>0.025f && (arm.SurfaceContact || (FindGround(position,out var ground) && position.y-ground.point.y<0.22f))))
                {
                    Vector3 probe=position+(arm.Contact.Hand?arm.Contact.Hand.transform.forward*0.14f:Vector3.zero);
                    if(FindGround(probe,out var hit))
                    {Disturb(hit.point,Mathf.Clamp(speed*0.5f+0.08f,0.05f,0.35f),arm.SeabedArea);nextArmEmission[i]=Time.time+0.3f;}
                }
            }
        }

        public bool FindGround(Vector3 point,out RaycastHit result)
        {
            // Cast only the editable ground hierarchy. ROV and hand colliders also use
            // Layer 8, so a single closest Physics.Raycast would hit the robot itself.
            result=default;float closest=float.PositiveInfinity;
            int count=Physics.RaycastNonAlloc(point+Vector3.up,Vector3.down,groundHits,3,groundMask,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var hit=groundHits[i];if(hit.normal.y<0.45f || hit.collider.GetComponentInParent<PicoArmPoseDriver>())continue;
                if(hit.distance<closest){result=hit;closest=hit.distance;}
            }
            return closest<float.PositiveInfinity;
        }
        readonly RaycastHit[] groundHits=new RaycastHit[32];

        void UpdateGrains(float dt)
        {
            if(!sedimentParticles)return;
            int count=sedimentParticles.GetParticles(grains);
            for(int i=0;i<count;i++)
            {
                var p=grains[i];Vector3 flow=presence?presence.CurrentAt(p.position,Time.time):Vector3.zero;
                p.velocity=Vector3.Lerp(p.velocity,flow-Vector3.up*settlingRate,1-Mathf.Exp(-dt*3));
                if(p.position.y<0.015f)p.remainingLifetime=0;
                grains[i]=p;
            }
            sedimentParticles.SetParticles(grains,count);
        }

        public void Publish()
        {
            int slots=0;
            for(int i=0;i<Capacity;i++)
            {
                var p=plumes[i];if(p.mass<=0)continue;
                plumeVectors[slots]=new Vector4(p.position.x,p.position.y,p.position.z,p.radius);
                densityVectors[slots++]=new Vector4(p.mass,0,0,0);
            }
            Shader.SetGlobalVectorArray("_UWPlumes",plumeVectors);Shader.SetGlobalVectorArray("_UWPlumeDensities",densityVectors);
            Shader.SetGlobalInt("_UWPlumeSlots",slots);
            for(int i=0;i<2;i++)
            {
                var lamp=i<workLights.Length?workLights[i]:null;
                if(!lamp || !lightsEnabled){lampPositions[i]=lampDirections[i]=lampColors[i]=Vector4.zero;continue;}
                Vector3 p=lamp.transform.position,d=lamp.transform.forward;
                lampPositions[i]=new Vector4(p.x,p.y,p.z,lamp.range);
                lampDirections[i]=new Vector4(d.x,d.y,d.z,Mathf.Cos(lamp.spotAngle*0.5f*Mathf.Deg2Rad));
                Color c=lamp.color*lamp.intensity;lampColors[i]=new Vector4(c.r,c.g,c.b,1);
            }
            Shader.SetGlobalVectorArray("_UWWorkLampPositions",lampPositions);
            Shader.SetGlobalVectorArray("_UWWorkLampDirections",lampDirections);
            Shader.SetGlobalVectorArray("_UWWorkLampColors",lampColors);
            Shader.SetGlobalInt("_UWLampSlots",lightsEnabled?Mathf.Min(2,workLights.Length):0);
            Shader.SetGlobalFloat("_UWInteractionReady",1);
        }
        void OnDisable(){Shader.SetGlobalFloat("_UWInteractionReady",0);Shader.SetGlobalInt("_UWPlumeSlots",0);Shader.SetGlobalInt("_UWLampSlots",0);}
    }
}
