using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterLivePreviewCheck
    {
        const string Active="UnderwaterPreviewCheck";
        static double started;
        static bool sounded;
        static readonly List<string> problems=new List<string>();
        static UnderwaterLivePreviewCheck(){EditorApplication.playModeStateChanged+=Mode;EditorApplication.update+=Tick;}
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Preview check requires stopped Play mode.");
            UnderwaterPreviewCompatibility.Verify();
            UnderwaterPresenceUpgrade.EnableEditorAudio();
            SessionState.SetBool(Active,true);EditorApplication.isPlaying=true;
        }
        public static void RunStability()
        {
            SessionState.SetInt(Active+"Cycles",3);SessionState.SetInt(Active+"Cycle",1);
            SessionState.SetFloat(Active+"Duration",20);SessionState.SetBool(Active+"AllPassed",true);
            Directory.CreateDirectory("Logs/UnderwaterCrashFix");
            File.WriteAllText("Logs/UnderwaterCrashFix/stability.json","{\"status\":\"running\",\"passed\":false}");
            Run();
        }
        public static void RunSeabed()
        {SessionState.SetBool(Active+"Seabed",true);Run();}
        static void Mode(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Active,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){started=EditorApplication.timeSinceStartup;sounded=false;problems.Clear();Application.logMessageReceived+=Log;}
            if(state==PlayModeStateChange.ExitingPlayMode){Application.logMessageReceived-=Log;SessionState.SetBool(Active,false);}
        }
        static void Log(string message,string stack,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || message.Contains("invalid MinMaxAABB"))if(problems.Count<30)problems.Add(message);}
        static void Tick()
        {
            if(SessionState.GetBool(Active+"Restart",false) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
            {
                if(EditorApplication.timeSinceStartup<SessionState.GetFloat(Active+"RestartAt",0))return;
                SessionState.SetBool(Active+"Restart",false);Run();return;
            }
            if(!SessionState.GetBool(Active,false)||!EditorApplication.isPlaying||started==0)return;
            double elapsed=EditorApplication.timeSinceStartup-started;
            var sound=Object.FindObjectOfType<UnderwaterSoundscape>();
            var activeMotor=Object.FindObjectOfType<UnderwaterMotor>();
            if(SessionState.GetBool(Active+"Seabed",false) && activeMotor && !activeMotor.InSeabed)activeMotor.SwitchArea();
            if(elapsed>4 && !sounded && sound){sound.TestSound();sounded=true;}
            bool stability=SessionState.GetInt(Active+"Cycles",0)>0;
            var interaction=Object.FindObjectOfType<UnderwaterInteraction>();
            if(stability && interaction)
            {interaction.SetWakeDemo(elapsed>5 && elapsed<16);interaction.lightsEnabled=elapsed<9 || elapsed>12;}
            if(elapsed<SessionState.GetFloat(Active+"Duration",10))return;
            var motor=Object.FindObjectOfType<UnderwaterMotor>();var caustic=Object.FindObjectOfType<UnderwaterCaustics>();
            var displays=new List<XRDisplaySubsystem>();SubsystemManager.GetInstances(displays);
            var report=new Report{loader=XRGeneralSettings.Instance?.Manager?.activeLoader?.name??"none",headsetActive=XRSettings.isDeviceActive,
                stereoEnabled=motor && motor.origin.Camera.stereoEnabled,eyeWidth=XRSettings.eyeTextureWidth,eyeHeight=XRSettings.eyeTextureHeight,
                causticFrames=caustic?caustic.GeneratedFrames:0,testCount=sound?sound.TestCount:0,testPcmPeak=sound?sound.LastTestPeak:0,
                errors=problems.ToArray(),status=sound?sound.OutputStatus:"soundscape missing",displayCount=displays.Count,
                graphicsApi=SystemInfo.graphicsDeviceType.ToString(),elapsedSeconds=(float)elapsed};
            report.passed=report.graphicsApi=="Direct3D11" && report.stereoEnabled && report.eyeWidth>0 && report.causticFrames>30 && report.errors.Length==0;
            if(stability)
            {
                int cycle=SessionState.GetInt(Active+"Cycle",1),total=SessionState.GetInt(Active+"Cycles",3);
                File.WriteAllText($"Logs/UnderwaterCrashFix/preview-cycle-{cycle}.json",JsonUtility.ToJson(report,true));
                bool passed=SessionState.GetBool(Active+"AllPassed",true) && report.passed;SessionState.SetBool(Active+"AllPassed",passed);
                if(cycle<total)
                {SessionState.SetInt(Active+"Cycle",cycle+1);SessionState.SetBool(Active+"Restart",true);SessionState.SetFloat(Active+"RestartAt",(float)EditorApplication.timeSinceStartup+2);}
                else
                {File.WriteAllText("Logs/UnderwaterCrashFix/stability.json",$"{{\"status\":\"complete\",\"passed\":{passed.ToString().ToLowerInvariant()},\"cycles\":{total},\"graphicsApi\":\"{report.graphicsApi}\"}}");SessionState.SetInt(Active+"Cycles",0);SessionState.SetFloat(Active+"Duration",10);}
            }
            Directory.CreateDirectory("Logs/UnderwaterPresence");File.WriteAllText("Logs/UnderwaterPresence/live-preview-check.json",JsonUtility.ToJson(report,true));
            if(SessionState.GetBool(Active+"Seabed",false))
            {Directory.CreateDirectory("Logs/UnderwaterSeabed");File.WriteAllText("Logs/UnderwaterSeabed/live-preview-check.json",JsonUtility.ToJson(report,true));SessionState.SetBool(Active+"Seabed",false);}
            UnderwaterPresenceUpgrade.Audit();UnderwaterRigUpgrade.Audit();SessionState.SetBool(Active,false);Application.logMessageReceived-=Log;EditorApplication.isPlaying=false;started=0;
        }
        [Serializable] sealed class Report
        {public string loader,status,graphicsApi;public bool headsetActive,stereoEnabled,passed;public int displayCount,eyeWidth,eyeHeight,causticFrames,testCount;public float testPcmPeak,elapsedSeconds;public string[] errors;}
    }
}
