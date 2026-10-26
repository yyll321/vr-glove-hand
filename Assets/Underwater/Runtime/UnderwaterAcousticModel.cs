using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnderwaterBaseline
{
    [DisallowMultipleComponent]
    public sealed class UnderwaterAcousticModel : MonoBehaviour
    {
        public Transform source, receiver;
        public Vector3 volumeCenter = new Vector3(0,5,0), volumeSize = new Vector3(25,10,50);
        public AcousticWaterLayer[] layers = {new AcousticWaterLayer()};
        public UnderwaterAcousticMaterial defaultBottom;
        public LayerMask solidMask = 1 << 8;
        [Range(32,4000)] public int rayCount = 1000;
        [Range(1,100)] public int maxInteractions = 16;
        [Min(1)] public float maxDistance = 250;
        [Min(1)] public int segmentsPerFrame = 192;
        [Min(0.001f)] public float receiverRadius = 0.45f;
        [Range(0.1f,10)] public float receiverEnergyFactor = 1;
        [Min(0)] public float refreshSeconds = 0.5f;
        [Min(0.000001f)] public float pressureCutoff = 0.002f;
        public float sourceLevelDbReMicroPascal = 100;
        public bool diffuseRain = true, sphericalSpreading = true, showRays;
        public bool realtime = true;
        [Serializable]
        public sealed class Arrival
        {
            public float delaySeconds, distance;
            public Vector3 incoming;
            public float[] pressure;
            public int interactions;
            public bool diffuse;
        }
        [Serializable]
        public sealed class Snapshot
        {
            public int rays, tracedSegments, surfaceReflections, solidReflections, layerReflections, refractions, escaped, captured, diffuseConnections;
            public float computeMs, elapsedSeconds, directDelaySeconds = -1;
            public float[] bandLevelsDb = new float[7], paperMeanPressure = new float[7];
            public Arrival[] arrivals = Array.Empty<Arrival>();
            public Vector3 sourcePosition, receiverPosition;
        }
        public Snapshot Latest { get; private set; }
        public bool IsComputing { get; private set; }
        public int Revision { get; private set; }
        public event Action<Snapshot> Completed;
        sealed class RayState
        {
            public Vector3 position, direction;
            public float distance, seconds;
            public float[] pressure;
            public int layer, interactions;
        }
        readonly List<Vector3[]> debugSegments = new List<Vector3[]>();
        readonly List<Arrival> arrivals = new List<Arrival>();
        readonly Queue<RayState> pending = new Queue<RayState>();
        float nextRun;
        const float Epsilon = 0.012f;
        public bool ReceiverInside => receiver && Inside(receiver.position);
        public bool Inside(Vector3 p) { var q = p-volumeCenter; return Mathf.Abs(q.x)<volumeSize.x/2 && Mathf.Abs(q.z)<volumeSize.z/2 && Mathf.Abs(q.y)<volumeSize.y/2; }
        void Update()
        {
            if (!realtime || !source || !receiver || !ReceiverInside || IsComputing || Time.unscaledTime < nextRun) return;
            StartCoroutine(Trace());
        }
        void OnDisable() { StopAllCoroutines(); IsComputing = false; pending.Clear(); }
        public int LayerAt(Vector3 p)
        {
            float depth = volumeCenter.y + volumeSize.y/2 - p.y;
            for (int i=0;i<layers.Length;i++) if(depth>=layers[i].topDepth-0.0001f && depth<=layers[i].bottomDepth+0.0001f) return i;
            return -1;
        }
        public IEnumerator Trace()
        {
            if (IsComputing || !source || !receiver || layers.Length==0) yield break;
            IsComputing = true;
            Vector3 tx = source.position, rx = receiver.position; int sourceLayer = LayerAt(tx);
            if (sourceLayer<0 || !Inside(tx)) { IsComputing=false; yield break; }
            var result = new Snapshot { rays=rayCount, sourcePosition=tx, receiverPosition=rx };
            arrivals.Clear(); pending.Clear(); debugSegments.Clear();
            float started=Time.realtimeSinceStartup, cpuMs=0; int work=0;
            if (LayerAt(rx)==sourceLayer && Visible(tx,rx))
            {
                float d=Vector3.Distance(tx,rx); var p=UnitPressure(); Attenuate(p,d,(tx.y+rx.y)*0.5f,sourceLayer);
                AddArrival(result,p,d,d/layers[sourceLayer].soundSpeed,(rx-tx).normalized,0,false,1);
                result.directDelaySeconds=d/layers[sourceLayer].soundSpeed;
            }
            for(int i=0;i<rayCount;i++) pending.Enqueue(new RayState {position=tx,direction=UnderwaterAcousticsMath.FibonacciDirection(i,rayCount),pressure=UnitPressure(),layer=sourceLayer});
            while(pending.Count>0 && result.tracedSegments<rayCount*maxInteractions*3)
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                var r=pending.Dequeue();
                if(r.interactions>=maxInteractions || r.distance>=maxDistance || Peak(r.pressure)<pressureCutoff) continue;
                var water=layers[r.layer];
                float length=maxDistance-r.distance;
                int boundary=-1; Vector3 normal=Vector3.zero;
                // Analytic layer planes: surface=0, bottom=1, internal boundaries>=2.
                float topY=volumeCenter.y+volumeSize.y/2-water.topDepth;
                float bottomY=volumeCenter.y+volumeSize.y/2-water.bottomDepth;
                if(r.direction.y>0.00001f)
                { float d=(topY-r.position.y)/r.direction.y; if(d>Epsilon*0.1f && d<length) {length=d;boundary=r.layer==0?0:2;normal=Vector3.down;} }
                else if(r.direction.y< -0.00001f)
                { float d=(bottomY-r.position.y)/r.direction.y; if(d>Epsilon*0.1f && d<length) {length=d;boundary=r.layer==layers.Length-1?1:3;normal=Vector3.up;} }
                // Open sides of the natural tank let acoustic energy escape; pool walls are real colliders.
                Vector3 min=volumeCenter-volumeSize/2,max=volumeCenter+volumeSize/2;
                float exit=SideExit(r.position,r.direction,min,max);
                if(exit>0 && exit<length) {length=exit;boundary=-2;}
                bool solid=Physics.Raycast(r.position,r.direction,out RaycastHit hit,length,solidMask,QueryTriggerInteraction.Ignore);
                UnderwaterAcousticMaterial material=null;
                if(solid) {length=hit.distance;normal=hit.normal;boundary=1;material=hit.collider.GetComponentInParent<UnderwaterAcousticMaterial>();}
                var point=r.position+r.direction*length;
                if(showRays && debugSegments.Count<240) debugSegments.Add(new[]{r.position,point});
                if(UnderwaterAcousticsMath.SphereEntry(r.position,r.direction,rx,receiverRadius,length,out float captureDistance))
                {
                    var p=(float[])r.pressure.Clone(); Attenuate(p,captureDistance,(r.position.y+rx.y)/2,r.layer);
                    AddArrival(result,p,r.distance+captureDistance,r.seconds+captureDistance/water.soundSpeed,r.direction,r.interactions,false,1f/rayCount);
                    result.captured++;
                }
                Attenuate(r.pressure,length,(r.position.y+point.y)/2,r.layer);
                r.distance+=length; r.seconds+=length/water.soundSpeed; r.interactions++; result.tracedSegments++;
                if(boundary==-2 || boundary==-1) result.escaped++;
                else if(boundary==0)
                {
                    // Water/air: pressure-release surface, polarity inversion, no transmitted air branch.
                    float reflection=(1.275f*343-water.density*water.soundSpeed)/(1.275f*343+water.density*water.soundSpeed);
                    Scale(r.pressure,reflection); result.surfaceReflections++;
                    Rain(result,r,point,rx,0.12f);
                    Continue(r,point,Vector3.Reflect(r.direction,normal),r.layer);
                }
                else if(boundary==1)
                {
                    material=material?material:defaultBottom;
                    float density=2400,cL=3000,cS=1500,scatter=0.18f;
                    if(material) {material.Parameters(volumeCenter.y+volumeSize.y/2-point.y,out density,out cL,out cS);scatter=material.scattering;}
                    float coefficient=UnderwaterAcousticsMath.ElasticReflection(Mathf.Abs(Vector3.Dot(r.direction,normal)),water.soundSpeed,water.density,density,cL,cS);
                    Scale(r.pressure,coefficient); result.solidReflections++;
                    Rain(result,r,point+normal*Epsilon,rx,scatter);
                    // Specular continuation retains non-diffuse pressure energy.
                    Scale(r.pressure,Mathf.Sqrt(1-scatter));
                    Continue(r,point,Vector3.Reflect(r.direction,normal),r.layer);
                }
                else
                {
                    int next=boundary==2?r.layer-1:r.layer+1; var other=layers[next];
                    bool transmit=UnderwaterAcousticsMath.FluidInterface(r.direction,normal,water.soundSpeed,water.density,other.soundSpeed,other.density,out Vector3 transmitted,out float rc,out float tc);
                    var reflected=new RayState{position=point+normal*Epsilon,direction=Vector3.Reflect(r.direction,normal),distance=r.distance,seconds=r.seconds,interactions=r.interactions,layer=r.layer,pressure=(float[])r.pressure.Clone()};
                    Scale(reflected.pressure,rc);
                    if(Peak(reflected.pressure)>=pressureCutoff) {pending.Enqueue(reflected);result.layerReflections++;}
                    if(transmit) {Scale(r.pressure,tc);Continue(r,point,transmitted,next);result.refractions++;}
                }
                cpuMs+=(float)timer.Elapsed.TotalMilliseconds;
                if(++work>=segmentsPerFrame) {work=0;yield return null;}
            }
            result.computeMs=cpuMs;result.elapsedSeconds=Time.realtimeSinceStartup-started;
            var sum=new double[7];var energy=new double[7];
            foreach(var a in arrivals) for(int band=0;band<7;band++) {sum[band]+=Math.Abs(a.pressure[band]);energy[band]+=a.pressure[band]*a.pressure[band];}
            for(int band=0;band<7;band++)
            {
                result.paperMeanPressure[band]=(float)(receiverEnergyFactor*sum[band]/Math.Max(1,arrivals.Count));
                result.bandLevelsDb[band]=sourceLevelDbReMicroPascal+20*Mathf.Log10(Mathf.Max(1e-9f,(float)Math.Sqrt(energy[band])));
            }
            arrivals.Sort((a,b)=>Peak(b.pressure).CompareTo(Peak(a.pressure)));
            result.arrivals=arrivals.GetRange(0,Math.Min(128,arrivals.Count)).ToArray();
            Latest=result;Revision++;IsComputing=false;nextRun=Time.unscaledTime+refreshSeconds;
            Completed?.Invoke(result);
        }
        void Rain(Snapshot result,RayState r,Vector3 p,Vector3 rx,float scatter)
        {
            if(!diffuseRain || scatter<=0 || LayerAt(rx)!=r.layer || !Visible(p,rx)) return;
            Vector3 direction=rx-p;float d=direction.magnitude;if(d<0.01f)return;
            var pressure=(float[])r.pressure.Clone(); Attenuate(pressure,d,(p.y+rx.y)/2,r.layer);
            float solidAngle=Mathf.Min(1,receiverRadius*receiverRadius/(4*d*d));
            AddArrival(result,pressure,r.distance+d,r.seconds+d/layers[r.layer].soundSpeed,direction/d,r.interactions,true,Mathf.Sqrt(scatter*solidAngle/rayCount));
            result.diffuseConnections++;
        }
        void AddArrival(Snapshot result,float[] pressure,float distance,float seconds,Vector3 direction,int interactions,bool diffuse,float weight)
        {
            float spread=sphericalSpreading?1/Mathf.Max(1,distance):1;Scale(pressure,spread*weight);
            if(arrivals.Count<16000)arrivals.Add(new Arrival{pressure=pressure,distance=distance,delaySeconds=seconds,incoming=direction,interactions=interactions,diffuse=diffuse});
        }
        void Continue(RayState r,Vector3 p,Vector3 direction,int layer)
        {r.position=p+direction*Epsilon;r.direction=direction;r.layer=layer;if(pending.Count<rayCount*4)pending.Enqueue(r);}
        void Attenuate(float[] p,float length,float averageY,int layer)
        {float depth=volumeCenter.y+volumeSize.y/2-averageY;for(int b=0;b<7;b++)p[b]*=UnderwaterAcousticsMath.PressureAttenuation(UnderwaterAcousticsMath.Frequencies[b],length,depth,layers[layer]);}
        bool Visible(Vector3 from,Vector3 to)
        {var delta=to-from;return !Physics.Raycast(from,delta.normalized,Mathf.Max(0,delta.magnitude-Epsilon),solidMask,QueryTriggerInteraction.Ignore);}
        static float SideExit(Vector3 p,Vector3 d,Vector3 min,Vector3 max)
        {float x=Mathf.Abs(d.x)<1e-6f?float.PositiveInfinity:((d.x>0?max.x:min.x)-p.x)/d.x;float z=Mathf.Abs(d.z)<1e-6f?float.PositiveInfinity:((d.z>0?max.z:min.z)-p.z)/d.z;return Mathf.Min(x,z);}
        static float[] UnitPressure(){return new float[]{1,1,1,1,1,1,1};}
        static void Scale(float[] p,float f){for(int i=0;i<p.Length;i++)p[i]*=f;}
        static float Peak(float[] p){float max=0;foreach(float v in p)max=Mathf.Max(max,Mathf.Abs(v));return max;}
        public void Export(string path)
        {if(Latest!=null)File.WriteAllText(path,JsonUtility.ToJson(Latest,true));}
        void OnDrawGizmosSelected()
        {
            Gizmos.color=new Color(0.2f,0.8f,1,0.25f);Gizmos.DrawWireCube(volumeCenter,volumeSize);
            foreach(var layer in layers){float y=volumeCenter.y+volumeSize.y/2-layer.topDepth;Gizmos.DrawWireCube(new Vector3(volumeCenter.x,y,volumeCenter.z),new Vector3(volumeSize.x,0,volumeSize.z));}
            if(source){Gizmos.color=Color.yellow;Gizmos.DrawWireSphere(source.position,0.2f);}
            if(receiver){Gizmos.color=Color.cyan;Gizmos.DrawWireSphere(receiver.position,receiverRadius);}
            if(showRays){Gizmos.color=new Color(0.2f,0.8f,0.9f,0.15f);foreach(var segment in debugSegments)Gizmos.DrawLine(segment[0],segment[1]);}
        }
    }
}
