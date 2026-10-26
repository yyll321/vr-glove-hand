using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnderwaterBaseline
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class UnderwaterAcousticAudio : MonoBehaviour
    {
        public UnderwaterAcousticModel model;
        [Range(0,1)] public float auditionVolume = 0.2f;
        public bool muted;
        public UnderwaterInteraction interaction;
        public float OperatingGain { get; private set; }=1;
        public AudioClip signal;
        public int maxAudiblePaths = 16;
        public int AudiblePaths { get; private set; }
        public float LastOutputPeak { get; private set; }
        public int ProcessedAudioBlocks { get; private set; }
        sealed class Taps { public int[] delays; public float[][] gains; public float[] left,right; public bool mute; public float volume; }
        volatile Taps taps;
        readonly float[][] history = new float[7][];
        readonly double[] z1 = new double[7], z2 = new double[7];
        readonly double[,] coefficients = new double[7,5];
        int sampleRate, ringSize, cursor, lastRevision = -1;
        float nextState;
        AudioSource output;
        public bool IsPlaying => output && output.isPlaying;
        public bool UsingDirectPreview { get; private set; }
        public void RestartSignal()
        {
            muted=false;
            if(!output)return;
            output.mute=false;
            if(signal)output.clip=signal;
            if(output.clip){output.time=0;output.Play();}
        }
        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate; ringSize = sampleRate * 2;
            for(int b=0;b<7;b++)
            {
                history[b]=new float[ringSize]; double omega=2*Math.PI*UnderwaterAcousticsMath.Frequencies[b]/sampleRate;
                double alpha=Math.Sin(omega)/(2*1.4), a0=1+alpha;
                coefficients[b,0]=alpha/a0;coefficients[b,1]=0;coefficients[b,2]=-alpha/a0;
                coefficients[b,3]=-2*Math.Cos(omega)/a0;coefficients[b,4]=(1-alpha)/a0;
            }
            output=GetComponent<AudioSource>();output.playOnAwake=false;output.loop=true;output.spatialBlend=0;output.volume=1;
            if(signal){output.clip=signal;output.Play();}
        }
        void Update()
        {
            var key=Keyboard.current;
            if(interaction && model)
            {
                float throttle=interaction.ThrottleFor(model.volumeCenter.x>31);
                OperatingGain=Mathf.Lerp(OperatingGain,UnderwaterInteraction.MotorGain(throttle),1-Mathf.Exp(-Time.unscaledDeltaTime*3));
                output.pitch=Mathf.Lerp(output.pitch,Mathf.Lerp(0.9f,1.12f,throttle),1-Mathf.Exp(-Time.unscaledDeltaTime*3));
            }
            if(key!=null && key.oKey.wasPressedThisFrame && model)model.showRays=!model.showRays;
            if(!model || !model.ReceiverInside)
            {taps=new Taps{mute=true,delays=Array.Empty<int>()};return;}
            if(Time.unscaledTime<nextState)return;
            nextState=Time.unscaledTime+0.05f;
            if(signal && !output.isPlaying && !muted)RestartSignal();
            UsingDirectPreview=model.Latest==null || model.Latest.arrivals.Length==0;
            if(UsingDirectPreview)
            {
                // Keep the audition alive while a bounded batch is pending. This fallback
                // is an engineering preview; it never modifies reported propagation data.
                float d=Vector3.Distance(model.source.position,model.receiver.position);
                float pan=Mathf.Clamp(Vector3.Dot((model.source.position-model.receiver.position).normalized,model.receiver.right),-1,1);
                taps=new Taps{mute=muted,volume=auditionVolume*OperatingGain,delays=new[]{Mathf.Clamp(Mathf.RoundToInt(d/1500*sampleRate),0,ringSize-1)},
                    gains=new[]{new float[]{1,1,1,1,1,1,1}},left=new[]{Mathf.Sqrt((1-pan)*0.5f)},right=new[]{Mathf.Sqrt((1+pan)*0.5f)}};
                AudiblePaths=1;return;
            }
            // Recompute pan on head rotation even while a new propagation batch is being traced.
            var arrivals=model.Latest.arrivals;int count=Math.Min(maxAudiblePaths,arrivals.Length);
            var state=new Taps{delays=new int[count],gains=new float[count][],left=new float[count],right=new float[count],mute=muted,volume=auditionVolume*OperatingGain};
            float normalization=0;
            for(int i=0;i<count;i++)
            {
                var a=arrivals[i];state.delays[i]=Mathf.Clamp(Mathf.RoundToInt(a.delaySeconds*sampleRate),0,ringSize-1);state.gains[i]=(float[])a.pressure.Clone();
                float pan=Mathf.Clamp(Vector3.Dot(-a.incoming,model.receiver.right),-1,1);state.left[i]=Mathf.Sqrt((1-pan)*0.5f);state.right[i]=Mathf.Sqrt((1+pan)*0.5f);
                float peak=0;foreach(float p in a.pressure)peak=Mathf.Max(peak,Mathf.Abs(p));normalization+=peak;
            }
            // Audition gain is separate from reported physical band levels; prevent clipping.
            state.volume/=Mathf.Max(0.15f,normalization);taps=state;AudiblePaths=count;lastRevision=model.Revision;
        }
        void OnAudioFilterRead(float[] data,int channels)
        {
            var state=taps;
            if(history[0]==null || state==null || state.mute || state.delays.Length==0){Array.Clear(data,0,data.Length);return;}
            float peak=0;
            for(int frame=0;frame<data.Length;frame+=channels)
            {
                double input=0;for(int c=0;c<channels;c++)input+=data[frame+c];input/=channels;
                for(int band=0;band<7;band++)
                {
                    double value=coefficients[band,0]*input+z1[band];
                    z1[band]=coefficients[band,1]*input-coefficients[band,3]*value+z2[band];
                    z2[band]=coefficients[band,2]*input-coefficients[band,4]*value;
                    history[band][cursor]=(float)value;
                }
                float l=0,r=0;
                for(int tap=0;tap<state.delays.Length;tap++)
                {
                    int at=(cursor-state.delays[tap]+ringSize)%ringSize;float sum=0;
                    for(int band=0;band<7;band++)sum+=history[band][at]*state.gains[tap][band];
                    l+=sum*state.left[tap];r+=sum*state.right[tap];
                }
                data[frame]=l*state.volume;if(channels>1)data[frame+1]=r*state.volume;
                peak=Mathf.Max(peak,Mathf.Max(Mathf.Abs(l*state.volume),Mathf.Abs(r*state.volume)));
                for(int c=2;c<channels;c++)data[frame+c]=(l+r)*0.5f*state.volume;
                cursor=(cursor+1)%ringSize;
            }
            LastOutputPeak=peak;ProcessedAudioBlocks++;
        }
    }
}
