using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline
{
    static class UnderwaterInteractionChecks
    {
        public static IEnumerator Run(UnderwaterMotor motor,string output,Action<bool,string> check)
        {
            var system=Object.FindObjectOfType<UnderwaterInteraction>();
            check(system && system.wakes.Length==4 && system.workLights.Length==2 && system.arms.Length==2,"Saved flow interaction has four editable jets, two work lights and two arm contacts");
            if(!system)yield break;
            var presence=system.presence;bool auto=system.suppressAutomaticInputs,demo=system.wakeDemo,lamps=system.lightsEnabled;
            var camera=motor.origin.Camera;Vector3 cameraPosition=camera.transform.position;Quaternion cameraRotation=camera.transform.rotation;
            var perf=Object.FindObjectOfType<UnderwaterPerformance>();bool hud=perf.showHud;perf.showHud=false;
            bool automatic=perf.automatic;perf.automatic=false;
            var particleRenderers=Object.FindObjectsOfType<ParticleSystemRenderer>();var particleStates=particleRenderers.Select(r=>r.enabled).ToArray();
            var lights=system.workLights;bool systemEnabled=system.enabled;
            var caustics=Object.FindObjectOfType<UnderwaterCaustics>();bool freeze=caustics.freezeWaves;float frozenTime=caustics.frozenTime;
            var fixture=new GameObject("Flow interaction test fixtures");
            try
            {
                system.suppressAutomaticInputs=true;system.SetWakeDemo(false);system.ClearSediment();
                var keyboard=InputSystem.AddDevice<Keyboard>("Flow interaction fixture keyboard");
                try
                {
                    system.suppressAutomaticInputs=false;
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.N,Key.L));yield return new WaitForSecondsRealtime(0.15f);
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(0.15f);
                    check(system.wakeDemo && system.wakes.All(w=>w.throttle>0) && system.lightsEnabled!=lamps,"N and L input toggles actual wake and lamp state");
                }
                finally{InputSystem.RemoveDevice(keyboard);system.suppressAutomaticInputs=true;system.SetWakeDemo(false);system.lightsEnabled=lamps;system.ClearSediment();}
                Vector3 point=new Vector3(62,2,0);
                check((presence.CurrentAt(point,7)-presence.AmbientCurrentAt(point,7)).magnitude<1e-6,"Idle thrusters add no phantom wake to the shared current");
                var jetGo=new GameObject("Jet fixture");jetGo.transform.SetParent(fixture.transform);jetGo.transform.position=point;
                var jet=jetGo.AddComponent<UnderwaterWakeSource>();jet.throttle=1;
                Vector3 centre=jet.VelocityAt(point+Vector3.forward*0.4f,3);
                check(centre.z>0.1f && jet.VelocityAt(point-Vector3.forward,3)==Vector3.zero && jet.VelocityAt(point+Vector3.forward*4,3)==Vector3.zero,"Jet has downstream velocity and finite support, with no upstream flow");
                check(jet.VelocityAt(point+new Vector3(2,0,0.4f),3).magnitude<centre.magnitude*0.01f,"Jet decays away from its axis");
                jet.transform.localScale=Vector3.one*3;
                check((jet.VelocityAt(point+Vector3.forward*0.4f,3)-centre).magnitude<1e-6,"Jet metre scale is independent of imported ROV parent scaling");
                jet.throttle=0.5f;check(Mathf.Abs(jet.VelocityAt(point+Vector3.forward*0.4f,3).magnitude/centre.magnitude-0.5f)<0.001f,"Jet strength responds proportionally to throttle");
                system.Disturb(new Vector3(0,0,0),1,false);float pool=system.ConcentrationAt(new Vector3(0,0.25f,0));
                system.Disturb(new Vector3(62,0,0),1,true);float sea=system.ConcentrationAt(new Vector3(62,0.25f,0));
                check(sea>pool*10 && pool>0,$"Sand resuspends more than the tiled pool (density {pool:F3} vs {sea:F3})");
                check(system.ConcentrationAt(new Vector3(62,3,0))==0,"Sediment remains local rather than turning the entire water body muddy");
                system.ClearSediment();system.SetWakeDemo(true);
                system.suppressAutomaticInputs=false;
                yield return new WaitForSeconds(1.4f);
                check(system.ActivePlumes>0 && system.sedimentParticles.particleCount>0,"Saved near-bottom thrusters raise moving sediment through actual ground queries");
                var grains=new ParticleSystem.Particle[1200];int count=system.sedimentParticles.GetParticles(grains);
                check(count>0 && grains.Take(count).All(p=>p.startSize>=0.002f && p.startSize<=0.0061f) && count<=system.sedimentParticles.main.maxParticles,"Resuspended grains stay 2-6 mm inside their quality budget");
                var acoustic=Object.FindObjectsOfType<UnderwaterAcousticAudio>().First(a=>a.interaction);
                float runningGain=acoustic.OperatingGain;system.SetWakeDemo(false);system.suppressAutomaticInputs=true;
                yield return new WaitForSeconds(1.4f);
                check(acoustic.OperatingGain<runningGain-0.1f && acoustic.IsPlaying,$"Thruster sound smoothly falls back to idle without stopping the acoustic source ({runningGain:F2}->{acoustic.OperatingGain:F2})");
                system.enabled=false;
                foreach(var r in particleRenderers)r.enabled=false;
                system.ClearSediment();
                system.Disturb(new Vector3(62,3,0),1,true);system.Disturb(new Vector3(62,3,0),1,true);
                float start=system.ConcentrationAt(new Vector3(62,3.25f,0));
                Vector3 expected=new Vector3(62,3.25f,0);
                for(int i=0;i<100;i++){expected+=(presence.CurrentAt(expected,8+i*0.1f)-Vector3.up*system.settlingRate)*0.1f;system.Simulate(0.1f,8+i*0.1f);}
                check(system.ConcentrationAt(expected)>0 && system.ConcentrationAt(expected)<start*0.45f,"Sediment advects with the common field, settles and dilutes over ten simulated seconds");
                for(int i=0;i<600;i++)system.Simulate(0.2f,18+i*0.2f);
                check(system.ActivePlumes==0,"Sediment eventually clears instead of accumulating indefinitely");
                for(int i=0;i<40;i++)system.Disturb(new Vector3(53+i%10*1.7f,2,i/10*2),1,true);
                check(system.ActivePlumes<=12,"Repeated disturbance remains bounded to twelve concentration puffs");
                system.ClearSediment();caustics.freezeWaves=true;caustics.frozenTime=7.5f;
                var panel=GameObject.CreatePrimitive(PrimitiveType.Cube);panel.transform.SetParent(fixture.transform);panel.transform.position=new Vector3(62,3.25f,3);panel.transform.localScale=new Vector3(5,5,0.05f);
                var white=new Material(Shader.Find("Universal Render Pipeline/Unlit"));white.SetColor("_BaseColor",Color.white);panel.GetComponent<Renderer>().sharedMaterial=white;
                camera.transform.SetPositionAndRotation(new Vector3(62,3.25f,-3),Quaternion.identity);
                system.lightsEnabled=false;system.Publish();
                var clean=Capture(camera,output,"06-silt-clear.png");
                system.Disturb(new Vector3(62,3,0),1,true);system.Disturb(new Vector3(62,3,0),1,true);system.Publish();
                var dirty=Capture(camera,output,"07-silt-cloud.png");
                float clearValue=Centre(clean).r,dirtyValue=Centre(dirty).r;
                check(dirtyValue<clearValue-0.025f,$"Rendered compact silt cloud attenuates the same background (red {clearValue:F3}->{dirtyValue:F3})");
                var lampObject=new GameObject("World lamp fixture");lampObject.transform.SetParent(fixture.transform);lampObject.transform.position=new Vector3(62,3.25f,-2);
                var lamp=lampObject.AddComponent<Light>();lamp.type=LightType.Spot;lamp.range=8;lamp.spotAngle=58;lamp.intensity=3;lamp.color=Color.white;
                system.workLights=new[]{lamp};system.lightsEnabled=true;system.Publish();
                var lit=Capture(camera,output,"08-silt-lit.png");float litValue=Centre(lit).r;
                check(litValue>dirtyValue+0.006f,$"World-space work light scatters through local silt (red {dirtyValue:F3}->{litValue:F3})");
                Object.Destroy(clean);Object.Destroy(dirty);Object.Destroy(lit);Object.Destroy(white);
                panel.SetActive(false);lampObject.SetActive(false);
                system.workLights=lights;system.lightsEnabled=lamps;system.ClearSediment();system.Publish();
                for(int i=0;i<particleRenderers.Length;i++)if(particleRenderers[i])particleRenderers[i].enabled=particleStates[i];
                camera.transform.SetPositionAndRotation(new Vector3(65,2.4f,-10),Quaternion.LookRotation(new Vector3(-3,-1.1f,5)));
                CaptureAndDispose(camera,output,"09-seabed-work-lights.png");
                camera.transform.SetPositionAndRotation(new Vector3(65,1.4f,5),Quaternion.LookRotation(new Vector3(-3,-0.9f,-3)));
                system.SetWakeDemo(true);system.enabled=true;system.suppressAutomaticInputs=false;
                yield return new WaitForSeconds(6);
                CaptureAndDispose(camera,output,"10-seabed-wake-silt.png");
            }
            finally
            {
                system.workLights=lights;system.lightsEnabled=lamps;system.SetWakeDemo(demo);system.ClearSediment();system.enabled=systemEnabled;system.suppressAutomaticInputs=auto;system.Publish();
                caustics.freezeWaves=freeze;caustics.frozenTime=frozenTime;
                camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);perf.showHud=hud;perf.automatic=automatic;
                for(int i=0;i<particleRenderers.Length;i++)if(particleRenderers[i])particleRenderers[i].enabled=particleStates[i];
                Object.Destroy(fixture);
            }
        }
        static Color Centre(Texture2D image)
        {Color sum=Color.clear;for(int x=-3;x<=3;x++)for(int y=-3;y<=3;y++)sum+=image.GetPixel(image.width/2+x,image.height/2+y);return sum/49;}
        static void CaptureAndDispose(Camera camera,string output,string name){var image=Capture(camera,output,name);Object.Destroy(image);}
        static Texture2D Capture(Camera camera,string output,string name)
        {
            var target=RenderTexture.GetTemporary(960,600,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);var old=RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
                var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false,true);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(output,name),image.EncodeToPNG());return image;
            }
            finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(target);}
        }
    }
}
