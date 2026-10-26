using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnderwaterBaseline
{
    // A shared world-space flow field supplies visible references without moving the head.
    public sealed class UnderwaterPresence : MonoBehaviour
    {
        public UnderwaterMotor motor;
        public UnderwaterEnvironment environment;
        public Vector3 poolCurrent = new Vector3(0.09f,0,0.035f);
        public Vector3 seaCurrent = new Vector3(0.18f,0,0.07f);
        [Range(0,2)] public float flowStrength = 1;
        public bool gentleBodyDrift;
        public Transform[] grass = Array.Empty<Transform>();
        public UnderwaterInteraction interaction;
        Quaternion[] grassRest;
        ParticleSystem.Particle[][] particles;
        float nextParticleUpdate;
        Texture2D surfaceFlow;
        readonly Color[] surfaceFlowPixels=new Color[64*32];
        float nextFlowMapUpdate;
        // Fixed seed offsets keep a continuous field: no frame-by-frame random jitter.
        static float Noise(Vector3 p,float time,float seed)
        {
            return Mathf.PerlinNoise(p.x+seed+time*0.13f,p.z+p.y*0.63f+seed*0.37f-time*0.09f)*2-1;
        }
        public Vector3 CurrentAt(Vector3 world,float time)
        {
            return AmbientCurrentAt(world,time)+(interaction ? interaction.WakeAt(world,time)*flowStrength : Vector3.zero);
        }
        public Vector3 AmbientCurrentAt(Vector3 world,float time)
        {
            Vector3 baseFlow=world.x>31?seaCurrent:poolCurrent;
            float gust=Mathf.Lerp(0.25f,1.9f,Mathf.PerlinNoise(world.x*0.065f+73.1f+time*0.045f,world.z*0.065f+18.7f));
            float turbulence=world.x>31?0.25f:0.14f;
            // Analytic curl of a smooth vector potential, inspired by curl-noise.
            // Incommensurate waves keep a coherent field; no per-frame random vectors.
            Vector3 a=new Vector3(0.41f,0.17f,0.29f), b=new Vector3(-0.23f,0.31f,0.47f),c=new Vector3(0.19f,-0.27f,0.37f);
            Vector3 eddy=Vector3.Cross(a,Vector3.up)*Mathf.Cos(Vector3.Dot(world,a)+time*0.31f+1.7f)*1.5f+
                Vector3.Cross(b,Vector3.right)*Mathf.Cos(Vector3.Dot(world,b)-time*0.23f+4.2f)+
                Vector3.Cross(c,Vector3.forward)*Mathf.Cos(Vector3.Dot(world,c)+time*0.19f+7.1f);
            eddy.y*=0.35f;
            Vector3 flow=Vector3.ClampMagnitude(baseFlow*gust+eddy*turbulence,0.55f);
            float localX=world.x-(world.x>31?62:0);
            flow.x*=Mathf.SmoothStep(0,1,(12.5f-Mathf.Abs(localX))/1.2f);
            flow.z*=Mathf.SmoothStep(0,1,(25-Mathf.Abs(world.z))/1.2f);
            flow.y*=Mathf.SmoothStep(0,1,Mathf.Min(world.y,10-world.y)/0.65f);
            return flow*flowStrength;
        }
        public Vector3 BodyCurrent => gentleBodyDrift && motor.origin.Camera.transform.position.y<9.8f ? CurrentAt(motor.origin.Camera.transform.position,Time.time)*0.35f:Vector3.zero;
        void Start()
        {
            grassRest=new Quaternion[grass.Length];for(int i=0;i<grass.Length;i++)if(grass[i])grassRest[i]=grass[i].localRotation;
            particles=new ParticleSystem.Particle[environment.snow.Length][];
            foreach(var ps in environment.snow)
            {
                // Apply local velocities below. A single emitter-wide velocity made all
                // particles travel together, even when the eye turned or moved regions.
                var velocity=ps.velocityOverLifetime;velocity.enabled=false;
                // Fine-scale variation also comes from the shared field, so particles
                // do not gain an unrelated emitter-wide noise motion.
                var noise=ps.noise;noise.enabled=false;
            }
        }
        void Update()
        {
            var key=Keyboard.current;
            if(key!=null && key.vKey.wasPressedThisFrame)flowStrength=flowStrength>0?0:1;
            if(Time.time>=nextFlowMapUpdate)
            {
                nextFlowMapUpdate=Time.time+0.2f;
                if(!surfaceFlow)surfaceFlow=new Texture2D(64,32,TextureFormat.RGBAHalf,false,true)
                    {name="Local surface currents - pool and seabed",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                for(int z=0;z<32;z++)for(int x=0;x<64;x++)
                {
                    int region=x/32;
                    Vector3 point=new Vector3(region*62+(x%32)/31f*25-12.5f,9.8f,z/31f*50-25);
                    Vector3 flow=CurrentAt(point,Time.time);
                    surfaceFlowPixels[z*64+x]=new Color(flow.x,flow.z,0,1);
                }
                surfaceFlow.SetPixels(surfaceFlowPixels);surfaceFlow.Apply(false,false);
                Shader.SetGlobalTexture("_UWFlowMap",surfaceFlow);Shader.SetGlobalFloat("_UWFlowReady",1);
            }
            // Sample each particle's world position at 10 Hz, with smooth drag. Bounded
            // work and reused buffers keep the dense suspension affordable in stereo.
            if(Time.time>=nextParticleUpdate)
            {
                nextParticleUpdate=Time.time+0.1f;
                for(int i=0;i<environment.snow.Length;i++)
                {
                    var ps=environment.snow[i];int capacity=ps.main.maxParticles;
                    if(particles[i]==null||particles[i].Length<capacity)particles[i]=new ParticleSystem.Particle[capacity];
                    int count=ps.GetParticles(particles[i]);
                    for(int j=0;j<count;j++)
                    {
                        var particle=particles[i][j];
                        float variation=0.65f+(particle.randomSeed%997)/997f*0.7f;
                        Vector3 target=CurrentAt(particle.position,Time.time)*variation-Vector3.up*0.006f;
                        particle.velocity=Vector3.Lerp(particle.velocity,target,0.38f);
                        particles[i][j]=particle;
                    }
                    ps.SetParticles(particles[i],count);
                }
            }
            for(int i=0;i<grass.Length;i++)if(grass[i])
            {
                Vector3 flow=CurrentAt(grass[i].position,Time.time);
                float flutter=Noise(grass[i].position*1.7f,Time.time*2.1f,43.6f)*flowStrength*2;
                grass[i].localRotation=grassRest[i]*Quaternion.Euler(flow.z*65+flutter,0,-flow.x*65+flutter*0.4f);
            }
            Shader.SetGlobalFloat("_UWPresence",flowStrength);
        }
        void OnDisable(){Shader.SetGlobalFloat("_UWPresence",0);Shader.SetGlobalFloat("_UWFlowReady",0);}
        void OnDestroy(){if(surfaceFlow)Destroy(surfaceFlow);}
    }
}
