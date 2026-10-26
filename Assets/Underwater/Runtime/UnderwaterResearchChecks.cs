using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace UnderwaterBaseline
{
    public sealed class UnderwaterResearchChecks : MonoBehaviour
    {
        public UnderwaterCaustics caustics;
        [Serializable] sealed class Report
        {
            public bool passed;
            public string[] checks;
            public float frameMs, causticCpuSubmitMs;
            public string gpu, limitation="Software checks and desktop timings only; no calibrated SPL, dBSea equivalence, HRTF or HMD 120 FPS claim.";
        }
        readonly List<string> checks=new List<string>();
        bool failed,done;
        string output;
        void Start()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-underwater-research-check")<0)return;
            output=Path.Combine(Directory.GetCurrentDirectory(),"Logs","UnderwaterResearch");Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"results.json"),"{\"passed\":false,\"status\":\"running\"}");
            File.WriteAllText(Path.Combine(output,"checks.txt"),"");
            Application.logMessageReceived+=OnLog;StartCoroutine(Run());StartCoroutine(Timeout());
        }
        void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Check(false,message);}
        void Check(bool condition,string message){string line=(condition?"PASS: ":"FAIL: ")+message;checks.Add(line);if(output!=null)File.AppendAllText(Path.Combine(output,"checks.txt"),line+"\n");if(!condition)failed=true;}
        IEnumerator Timeout(){yield return new WaitForSecondsRealtime(100);if(!done){Check(false,"Timed out");Finish();}}
        void Finish()
        {
            done=true;var perf=FindObjectOfType<UnderwaterPerformance>();
            File.WriteAllText(Path.Combine(output,"results.json"),JsonUtility.ToJson(new Report{passed=!failed,checks=checks.ToArray(),frameMs=perf.FrameMs,causticCpuSubmitMs=caustics.CpuSubmitMs,gpu=SystemInfo.graphicsDeviceName},true));
            Application.logMessageReceived-=OnLog;Application.Quit(failed?1:0);
        }
        IEnumerator Run()
        {
            yield return new WaitForSeconds(4);
            var motor=FindObjectOfType<UnderwaterMotor>();var env=FindObjectOfType<UnderwaterEnvironment>();
            Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="UnderwaterResearchEditable","New saved scene is loaded");
            Check(GameObject.Find("ROVp - Pool") && GameObject.Find("ROVp - Seabed") && GameObject.Find("Pool_Accessories"),"ROVp and pool accessories exist in the saved hierarchy");
            int budget=0;foreach(var ps in env.snow)budget+=ps.main.maxParticles;
            Check(env.budgets.Sum()==10800 && budget>=5900 && env.ParticleCount>4000,"Suspended particle density increased within a fixed budget (including automatic quality fallback)");
            Check(env.snow.All(p=>p.main.startSize.constantMax<=0.0081f),"Suspended particles are no larger than 8 mm");
            var presence=FindObjectOfType<UnderwaterPresence>();
            Check(presence && presence.grass.Length>0,"Water current and animated seagrass references are saved");
            var particles=new ParticleSystem.Particle[64];int count=env.snow[0].GetParticles(particles);
            float near=0;for(int i=0;i<count;i++)if(Vector3.Distance(particles[i].position,motor.origin.Camera.transform.position)<3)near++;
            Check(near>5 && particles.Take(count).Any(p=>p.velocity.magnitude>0.015f),"Fine moving particles populate the near field for binocular parallax");
            float spatial=0,temporal=0,maximum=0,step=0;
            for(int i=0;i<80;i++)
            {
                Vector3 point=new Vector3(i%10*2-10,1+i%3,i/10*3-12);
                Vector3 flow=presence.CurrentAt(point,5);
                spatial+=Vector3.Distance(flow,presence.CurrentAt(Vector3.zero,5));
                temporal+=Vector3.Distance(flow,presence.CurrentAt(point,19));
                maximum=Mathf.Max(maximum,flow.magnitude);
                step=Mathf.Max(step,Vector3.Distance(flow,presence.CurrentAt(point,5.01f)));
            }
            Check(spatial/80>0.025f && temporal/80>0.025f && maximum<0.56f && step<0.01f,$"Local currents vary across space and time without frame jitter (space {spatial/80:F3}, time {temporal/80:F3}, max {maximum:F3}, step {step:F4})");
            var sound=FindObjectOfType<UnderwaterSoundscape>();
            Check(sound && sound.ambience.isPlaying && sound.ambience.clip.loadState==AudioDataLoadState.Loaded,"Listener-local water ambience starts without waiting for acoustic tracing");
            var keyboard=InputSystem.AddDevice<Keyboard>("Underwater Audio Test Keyboard");
            foreach(var emitter in sound.propagated){emitter.muted=true;emitter.GetComponent<AudioSource>().Stop();}
            sound.muted=true;AudioListener.pause=true;AudioListener.volume=0;
            int previousTest=sound.TestCount;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.J));yield return new WaitForSecondsRealtime(0.15f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(0.18f);
            float[] cue=new float[512];sound.testTone.GetOutputData(cue,0);float cuePeak=cue.Max(v=>Mathf.Abs(v));
            Check(sound.TestCount==previousTest+1 && !sound.muted && !AudioListener.pause && sound.propagated.All(a=>a.IsPlaying && !a.muted),$"J restores stopped/muted audio (tests={sound.TestCount}, mute={sound.muted}, pause={AudioListener.pause}, sources={sound.propagated.Count(a=>a.IsPlaying)})");
            Check(cuePeak>0.01f,$"Independent route-test cue produces measurable PCM ({cuePeak:F3} peak); speaker delivery needs listening verification");
            // The opt-in player window is hidden: Unity may skip its automatic camera
            // rendering. Explicitly render before checking the camera-driven simulation.
            var warmup=Capture(motor.origin.Camera,null);Destroy(warmup);
            Check(caustics.GeneratedFrames>0 && caustics.NormalMap && caustics.CausticMap,"Gerstner normal and area-caustic maps generated for a rendered camera");
            var first=Read(caustics.CausticMap);float min=100,max=-1,mean=0;
            foreach(var color in first.GetPixels()){min=Mathf.Min(min,color.r);max=Mathf.Max(max,color.r);mean+=color.r;}mean/=first.width*first.height;
            Check(mean>0.45f && mean<2.5f && max-min>0.35f,$"Caustic energy and spatial contrast (mean {mean:F3}, range {min:F3}..{max:F3})");
            File.WriteAllBytes(Path.Combine(output,"caustic-map.png"),MapPreview(first).EncodeToPNG());
            var normals=Read(caustics.NormalMap);File.WriteAllBytes(Path.Combine(output,"surface-normal-map.png"),normals.EncodeToPNG());Destroy(normals);
            yield return new WaitForSeconds(0.8f);
            warmup=Capture(motor.origin.Camera,null);Destroy(warmup);
            var second=Read(caustics.CausticMap);var a=first.GetPixels();var b=second.GetPixels();double change=0;
            for(int i=0;i<a.Length;i++)change+=Math.Abs(a[i].r-b[i].r);
            Check(change/a.Length>0.02,"Caustic pattern changes with simulated waves");Destroy(first);Destroy(second);
            var settings=caustics.settings;
            settings.Evaluate(new Vector2(1.3f,2.7f),2,out Vector3 p1,out Vector3 n1);
            settings.Evaluate(new Vector2(1.3f+settings.tileSize,2.7f),2,out Vector3 p2,out Vector3 n2);
            Check(Vector3.Distance(n1,n2)<0.0001f && Mathf.Abs(p1.y-p2.y)<0.0001f,"Gerstner waves tile without discontinuity");
            UnderwaterCausticReceiverChecks.Run(motor.origin.Camera,caustics,output,Check);
            UnderwaterOpticsChecks.Run(motor.origin.Camera,caustics,output,Check);
            Capture(motor.origin.Camera,"01-pool.png");
            var colored=Capture(motor.origin.Camera,null);float strength=settings.causticStrength;settings.causticStrength=0;
            yield return new WaitForSeconds(0.1f);var uncolored=Capture(motor.origin.Camera,null);
            var cp=colored.GetPixels32();var up=uncolored.GetPixels32();long difference=0;
            for(int i=0;i<cp.Length;i++)difference+=Math.Abs(cp[i].r-up[i].r)+Math.Abs(cp[i].g-up[i].g)+Math.Abs(cp[i].b-up[i].b);
            Check(difference/(float)cp.Length>0.1f,"Generated caustic map changes receiver surfaces in the rendered scene");settings.causticStrength=strength;Destroy(colored);Destroy(uncolored);
            motor.SwitchArea();yield return new WaitForSeconds(2);Capture(motor.origin.Camera,"02-seabed.png");
            int seabedBudget=env.snow.Sum(ps=>ps.main.maxParticles);
            float tierFactor=env.quality==0?0.55f:env.quality==1?0.8f:1;
            Check(seabedBudget>env.budgets.Sum()*tierFactor*1.6f && env.snow.All(ps=>ps.main.startSize.constantMax<=0.0081f),"Seabed adds at least 60 percent fine suspension at the same quality tier");
            motor.SwitchArea();yield return new WaitForSeconds(0.4f);
            motor.ToggleSurface();yield return new WaitForSeconds(0.5f);motor.origin.RotateAroundCameraUsingOriginUp(-70);motor.origin.Camera.transform.localRotation=Quaternion.Euler(20,0,0);Capture(motor.origin.Camera,"03-water-surface.png");
            motor.ToggleSurface();yield return new WaitForSeconds(1);
            yield return UnderwaterRigChecks.Run(motor,output,Check);
            yield return UnderwaterArmChecks.Run(Check);
            yield return UnderwaterInteractionChecks.Run(motor,output,Check);
            var layer=new AcousticWaterLayer();
            double coefficient=UnderwaterAcousticsMath.AbsorptionDbPerKm(1000,5,layer);
            Check(coefficient>0.04 && coefficient<0.09,$"Ainslie-McColm 1 kHz absorption in dB/km ({coefficient:F5})");
            float factor=UnderwaterAcousticsMath.PressureAttenuation(1000,1000,5,layer);
            Check(Math.Abs(20*Math.Log10(factor)+coefficient)<0.00001,"dB/km to pressure nepers/metre conversion");
            UnderwaterAcousticsMath.FluidInterface(Vector3.down,Vector3.up,1500,1000,1600,1040,out Vector3 transmitted,out float r,out float t);
            float expected=(1040f*1600-1000f*1500)/(1040f*1600+1000f*1500);
            Check(Mathf.Abs(r-expected)<0.00001f && Mathf.Abs(t-(1+r))<0.00001f,"Normal-incidence pressure reflection and transmission");
            Vector3 incident=new Vector3(0.5f,-Mathf.Sqrt(0.75f),0);
            UnderwaterAcousticsMath.FluidInterface(incident,Vector3.up,1500,1000,1600,1040,out transmitted,out r,out t);
            Check(Mathf.Abs(transmitted.x/1600-incident.x/1500)<1e-7f,"Acoustic Snell invariant sin(theta)/c");
            bool refracted=UnderwaterAcousticsMath.FluidInterface(new Vector3(0.99f,-Mathf.Sqrt(1-0.99f*0.99f),0),Vector3.up,1500,1000,1800,1040,out transmitted,out r,out t);
            Check(!refracted && r==1 && t==0,"Total internal reflection terminates transmitted branch");
            float solid=UnderwaterAcousticsMath.ElasticReflection(1,1500,1000,2400,3000,1500);
            Check(Math.Abs(solid-(7200000f-1500000)/(7200000+1500000))<0.00001,"Elastic solid normal-incidence impedance");
            yield return TraceFixtures();
            foreach(var model in FindObjectsOfType<UnderwaterAcousticModel>().Where(m=>m.name!="AcousticFixture"))
            {
                if(model.Latest!=null){model.Export(Path.Combine(output,model.name+".json"));Check(model.Latest.tracedSegments>0 && model.Latest.arrivals.Length>0,model.name+" produced bounded audible arrivals");}
                else Check(false,model.name+" has no trace snapshot");
            }
            var audio=FindObjectsOfType<UnderwaterAcousticAudio>().First(m=>m.model.ReceiverInside);
            Check(audio.ProcessedAudioBlocks>0 && audio.LastOutputPeak>0,"Seven-band delay renderer produces nonzero audio samples");
            Finish();
        }
        IEnumerator TraceFixtures()
        {
            var go=new GameObject("AcousticFixture");var model=go.AddComponent<UnderwaterAcousticModel>();model.realtime=false;
            model.volumeCenter=new Vector3(1000,5,0);model.rayCount=64;model.maxInteractions=6;model.maxDistance=80;model.segmentsPerFrame=128;
            var source=new GameObject("Fixture Source");var receiver=new GameObject("Fixture Receiver");source.transform.position=new Vector3(1000,2,0);receiver.transform.position=new Vector3(1004,2,0);model.source=source.transform;model.receiver=receiver.transform;
            var audition=go.AddComponent<UnderwaterAcousticAudio>();audition.model=model;audition.signal=FindObjectOfType<UnderwaterSoundscape>().propagated[0].signal;audition.RestartSignal();
            yield return new WaitForSeconds(0.2f);
            Check(audition.UsingDirectPreview && audition.ProcessedAudioBlocks>0 && audition.LastOutputPeak>0,"Audition remains audible before the first ray-tracing snapshot");
            yield return model.Trace();
            Check(Mathf.Abs(model.Latest.directDelaySeconds-4f/1500)<0.00001f,"Ray tracer direct delay equals distance/sound speed");
            Check(model.Latest.surfaceReflections>0 && model.Latest.solidReflections>0,"Ray tracer reflects at water-air and elastic bottom boundaries");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Fixture Occluder";wall.layer=8;wall.transform.position=new Vector3(1002,4,0);wall.transform.localScale=new Vector3(0.3f,8,8);Physics.SyncTransforms();
            yield return model.Trace();Check(model.Latest.directDelaySeconds<0,"Scene collider blocks direct acoustic path");Destroy(wall);yield return null;
            model.layers=new[]{new AcousticWaterLayer{topDepth=0,bottomDepth=5,soundSpeed=1550},new AcousticWaterLayer{topDepth=5,bottomDepth=10,soundSpeed=1450}};
            receiver.transform.position=new Vector3(1001,7,0);yield return model.Trace();
            Check(model.Latest.refractions>0 && model.Latest.layerReflections>0,"Layer intersections branch into reflected and refracted rays");
            Check(model.Latest.diffuseConnections>0,"Diffuse Rain connects visible same-layer receiver paths");
            Check(model.Latest.bandLevelsDb.All(v=>!float.IsNaN(v)&&!float.IsInfinity(v)),"All seven receive bands are finite");
            model.Export(Path.Combine(output,"layer-fixture.json"));Destroy(source);Destroy(receiver);Destroy(go);
        }
        static Texture2D Read(RenderTexture target)
        {
            var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();RenderTexture.active=old;return pixels;
        }
        static Texture2D MapPreview(Texture2D data)
        {
            var preview=new Texture2D(data.width,data.height,TextureFormat.RGB24,false);var pixels=data.GetPixels();
            for(int i=0;i<pixels.Length;i++)pixels[i]=Color.white*Mathf.Clamp01(pixels[i].r/4);preview.SetPixels(pixels);preview.Apply();return preview;
        }
        Texture2D Capture(Camera camera,string name)
        {
            var target=new RenderTexture(1440,900,24,RenderTextureFormat.ARGB32);target.Create();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            var read=Read(target);var pixels=read.GetPixels();var converted=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            converted.SetPixels(pixels);converted.Apply();Destroy(read);target.Release();Destroy(target);
            if(name!=null)
            {
                int magenta=0;foreach(var p in pixels)if(p.r>0.8f&&p.b>0.8f&&p.g<0.1f)magenta++;
                Check(magenta<pixels.Length/100,name+" has no missing shader surfaces");File.WriteAllBytes(Path.Combine(output,name),converted.EncodeToPNG());
            }
            return converted;
        }
    }
}
