using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    // Inspect the actual provider eye targets, not a second desktop camera.
    [InitializeOnLoad]
    public static class UnderwaterXrSubmissionAudit
    {
        const string Active="UnderwaterSubmissionAudit";
        static double started;
        static int stage;
        static UnderwaterCaustics caustics;
        static bool originalCaustics;
        static readonly List<UnderwaterHorizonFeature> features=new List<UnderwaterHorizonFeature>();
        static readonly List<bool> originalFeatures=new List<bool>();
        static readonly List<XRDisplaySubsystem> displays=new List<XRDisplaySubsystem>();
        static readonly StringBuilder report=new StringBuilder();
        static UnderwaterXrSubmissionAudit()
        {EditorApplication.playModeStateChanged+=Mode;EditorApplication.update+=Tick;RenderPipelineManager.endContextRendering+=EndFrame;}
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play before submission audit.");
            SessionState.SetBool(Active,true);EditorApplication.isPlaying=true;
        }
        static void Mode(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Active,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                started=EditorApplication.timeSinceStartup;stage=0;report.Clear();
                features.Clear();originalFeatures.Clear();
                foreach(var feature in Resources.FindObjectsOfTypeAll<UnderwaterHorizonFeature>())
                {features.Add(feature);originalFeatures.Add(feature.isActive);}
                caustics=Object.FindObjectOfType<UnderwaterCaustics>();originalCaustics=caustics&&caustics.enabled;
                Directory.CreateDirectory("Logs/UnderwaterXR");
            }
            if(state==PlayModeStateChange.ExitingPlayMode){Restore();SessionState.SetBool(Active,false);}
        }
        static void Restore()
        {
            for(int i=0;i<features.Count;i++)if(features[i])features[i].SetActive(originalFeatures[i]);
            if(caustics)caustics.enabled=originalCaustics;
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Active,false)||!EditorApplication.isPlaying||started==0)return;
            if(EditorApplication.timeSinceStartup-started<32)return;
            Restore();File.WriteAllText("Logs/UnderwaterXR/submission.txt",report.ToString());
            SessionState.SetBool(Active,false);EditorApplication.isPlaying=false;started=0;
        }
        static void EndFrame(ScriptableRenderContext context,List<Camera> cameras)
        {
            if(!SessionState.GetBool(Active,false)||!EditorApplication.isPlaying||started==0)return;
            double elapsed=EditorApplication.timeSinceStartup-started;
            if(elapsed<6+stage*8 || stage>=3)return;
            string label=stage==0?"water":stage==1?"no-fog":"no-water-passes";
            var motor=Object.FindObjectOfType<UnderwaterMotor>();if(!motor)return;
            Camera eye=motor.origin.Camera;
            report.AppendLine($"{DateTime.Now:O} {label} camera={eye.transform.position}/{eye.transform.eulerAngles} matrix={eye.worldToCameraMatrix} stereoMode={XRSettings.stereoRenderingMode}");
            displays.Clear();SubsystemManager.GetInstances(displays);
            foreach(var display in displays)if(display.running)
            {
                report.AppendLine($"Display={display.SubsystemDescriptor.id} passes={display.GetRenderPassCount()} layout={display.textureLayout}");
                for(int p=0;p<display.GetRenderPassCount();p++)
                {
                    display.GetRenderPass(p,out var pass);var d=pass.renderTargetDesc;
                    report.AppendLine($"Pass{p} target={pass.renderTarget} desc={d.width}x{d.height}x{d.volumeDepth} {d.dimension} msaa={d.msaaSamples}");
                    for(int e=0;e<pass.GetRenderParameterCount();e++)
                    {
                        pass.GetRenderParameter(eye,e,out var param);
                        report.AppendLine($"Eye{e} slice={param.textureArraySlice} viewport={param.viewport} view={param.view} projection={param.projection} angle={Vector3.Angle(-param.view.inverse.GetColumn(2),eye.transform.forward)}");
                    }
                    var rt=display.GetRenderTextureForRenderPass(p);
                    if(rt)
                    {
                        string path=$"Logs/UnderwaterXR/{label}-pass{p}";int width=rt.width,height=rt.height;
                        AsyncGPUReadback.Request(rt,0,TextureFormat.RGBA32,request=>
                        {
                            if(request.hasError){File.WriteAllText(path+"-error.txt","XR target readback failed");return;}
                            for(int slice=0;slice<request.layerCount;slice++)
                            {
                                var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
                                texture.SetPixelData(request.GetData<byte>(slice),0);texture.Apply();
                                File.WriteAllBytes(path+$"-eye{slice}.png",texture.EncodeToPNG());Object.DestroyImmediate(texture);
                            }
                        });
                    }
                }
            }
            File.WriteAllText("Logs/UnderwaterXR/submission.txt",report.ToString());
            stage++;
            if(stage==1)foreach(var feature in features)feature.SetActive(false);
            if(stage==2 && caustics)caustics.enabled=false;
        }
    }
}
