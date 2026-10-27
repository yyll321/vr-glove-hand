using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;

namespace UnderwaterBaseline
{
    // These listener-local cues remain audible while the propagation solver warms up.
    public sealed class UnderwaterSoundscape : MonoBehaviour
    {
        public UnderwaterMotor motor;
        public AudioSource ambience, testTone;
        public UnderwaterAcousticAudio[] propagated;
        [Range(0,1)] public float ambienceVolume=0.45f, testVolume=0.65f;
        public bool muted;
        public UnderwaterInteraction interaction;
        public float FlowGain { get; private set; }=0.65f;
        public int TestCount { get; private set; }
        public float LastTestPeak { get; private set; }
        public string OutputStatus { get; private set; }="Waiting for audio";
        float nextDiagnostics;
        readonly float[] meter=new float[256];
        void Start()
        {
            if(ambience && ambience.clip){ambience.loop=true;ambience.playOnAwake=false;ambience.spatialBlend=0;ambience.priority=0;ambience.Play();}
            if(testTone){testTone.playOnAwake=false;testTone.spatialBlend=0;testTone.priority=0;testTone.ignoreListenerPause=true;}
            AudioSettings.OnAudioConfigurationChanged+=OnAudioChanged;
        }
        void OnDestroy(){AudioSettings.OnAudioConfigurationChanged-=OnAudioChanged;}
        void OnAudioChanged(bool changed)
        {if(ambience && ambience.clip && !ambience.isPlaying)ambience.Play();foreach(var a in propagated)if(a)a.RestartSignal();}
        public void ToggleMute(){muted=!muted;foreach(var a in propagated)if(a)a.muted=muted;if(muted && testTone)testTone.Stop();}
        public void TestSound()
        {
            muted=false;AudioListener.pause=false;AudioListener.volume=1;
            foreach(var a in propagated)if(a)a.RestartSignal();
            if(testTone && testTone.clip){testTone.volume=testVolume;testTone.Stop();testTone.Play();TestCount++;}
            OutputStatus="Sound test playing on the PC Unity audio output";
            Debug.Log("[WaterAudio] J / right trigger: test signal started. Live Preview picture does not establish the headset audio route; check the Windows output device or use headphones connected to the PC.");
        }
        void Update()
        {
            var key=Keyboard.current;
            if(key!=null && key.jKey.wasPressedThisFrame)TestSound();
            if(key!=null && key.mKey.wasPressedThisFrame)ToggleMute();
            var c=motor.Controllers;
            if(c!=null)
            {
                if(c.SoundTestPressed)TestSound();
                if(c.MutePressed)ToggleMute();
            }
            bool submerged=motor.origin.Camera.transform.position.y<10;
            float flow=interaction?interaction.ListenerFlow:0.1f;
            FlowGain=Mathf.Lerp(FlowGain,Mathf.Lerp(0.55f,1.15f,Mathf.Clamp01(flow/0.5f)),1-Mathf.Exp(-Time.unscaledDeltaTime*2));
            if(ambience)
            {
                ambience.volume=Mathf.MoveTowards(ambience.volume,muted?0:(submerged?ambienceVolume*FlowGain:ambienceVolume*0.08f),Time.unscaledDeltaTime);
                ambience.pitch=Mathf.Lerp(0.94f,1.04f,Mathf.Clamp01(flow/0.5f));
            }
            if(testTone && testTone.isPlaying){testTone.GetOutputData(meter,0);float peak=0;foreach(float x in meter)peak=Mathf.Max(peak,Mathf.Abs(x));LastTestPeak=Mathf.Max(LastTestPeak,peak);}
            if(Time.unscaledTime<nextDiagnostics)return;nextDiagnostics=Time.unscaledTime+2;
            string loader=XRGeneralSettings.Instance?.Manager?.activeLoader?.name??"Desktop";
            OutputStatus=$"{(muted?"MUTED":"Audio ON")} | {(ambience && ambience.isPlaying?"water bed playing":"no ambient clip")} | J / R trigger test {TestCount}";
            var report=new AudioReport{loader=loader,sampleRate=AudioSettings.outputSampleRate,listenerVolume=AudioListener.volume,listenerPaused=AudioListener.pause,
                ambiencePlaying=ambience && ambience.isPlaying,testPlaying=testTone && testTone.isPlaying,testCount=TestCount,status=OutputStatus,
                acousticPeak=0,warning="Unity DSP output only; this does not prove sound reached the headset speakers. Live Preview uses PC audio output."};
            foreach(var a in propagated)if(a)report.acousticPeak=Mathf.Max(report.acousticPeak,a.LastOutputPeak);
            string dir=Application.isEditor?"Logs/UnderwaterPresence":Path.Combine(Application.persistentDataPath,"UnderwaterPresence");
            Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"audio-status.json"),JsonUtility.ToJson(report,true));
        }
        [Serializable] sealed class AudioReport
        {public string loader,status,warning;public int sampleRate,testCount;public bool listenerPaused,ambiencePlaying,testPlaying;public float listenerVolume,acousticPeak;}
    }
}
