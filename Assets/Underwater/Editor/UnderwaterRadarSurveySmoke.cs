using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterRadarSurveySmoke
    {
        const string Key="UnderwaterRadarSurveySmoke";
        static double start;static bool sea,captured;
        static readonly List<string> errors=new List<string>();
        static UnderwaterRadarSurveySmoke(){EditorApplication.playModeStateChanged+=Mode;EditorApplication.update+=Tick;}
        [MenuItem("Tools/Underwater/Research/18 Run Geometry Radar Play Smoke Check")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=UnderwaterRadarSurveyTools.ScenePath)
                throw new Exception("Open the Geometry Radar Survey scene and exit Play first");
            UnderwaterPreviewCompatibility.Verify();SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        static void Mode(PlayModeStateChange mode)
        {
            if(!SessionState.GetBool(Key,false))return;
            if(mode==PlayModeStateChange.EnteredPlayMode){start=EditorApplication.timeSinceStartup;sea=captured=false;errors.Clear();Application.logMessageReceived+=Log;}
            if(mode==PlayModeStateChange.ExitingPlayMode){Application.logMessageReceived-=Log;SessionState.SetBool(Key,false);}
        }
        static void Log(string message,string stack,LogType type){if((type==LogType.Error || type==LogType.Exception) && errors.Count<30)errors.Add(message);}
        [Serializable] sealed class Report
        {
            public bool passed;public string time;public int poolFrames,seabedFrames,seabedHits;public float poolScanMs,seabedScanMs,flowStepMs;public string[] errors;
            public string limitation="Editor Play smoke only; no HMD perception or performance certification";
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || start==0)return;
            double elapsed=EditorApplication.timeSinceStartup-start;var radars=Object.FindObjectsOfType<UnderwaterGeometryRadar>();
            foreach(var radar in radars)if(!radar.scanning)radar.StartScan();
            var motor=Object.FindObjectOfType<UnderwaterMotor>();
            if(elapsed>7 && !sea && motor){if(!motor.InSeabed)motor.SwitchArea();sea=true;}
            if(elapsed>14 && !captured && motor)
            {
                captured=true;Directory.CreateDirectory("Logs/RadarReview");
                var camera=motor.origin.Camera;var target=RenderTexture.GetTemporary(1280,720,24);var previous=camera.targetTexture;var active=RenderTexture.active;
                var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
                try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("Logs/RadarReview/seabed-play.png",image.EncodeToPNG());}
                finally{camera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);Object.Destroy(image);}
            }
            if(elapsed<19)return;
            var pool=radars.FirstOrDefault(r=>!r.seabed);var seabed=radars.FirstOrDefault(r=>r.seabed);var volume=GameObject.Find("Seabed Fluid Volume").GetComponent<UnderwaterFluidVolume>();
            var report=new Report{time=DateTime.Now.ToString("O"),poolFrames=pool?pool.Frames:0,seabedFrames=seabed?seabed.Frames:0,
                poolScanMs=pool?pool.LastScanMs:0,seabedScanMs=seabed?seabed.LastScanMs:0,flowStepMs=volume.LastStepMilliseconds,
                seabedHits=seabed && seabed.Latest!=null?seabed.Latest.hits.Count(h=>h):0,errors=errors.ToArray()};
            report.passed=report.poolFrames>2 && report.seabedFrames>2 && report.seabedHits>0 && report.errors.Length==0;
            File.WriteAllText("Logs/RadarReview/play-smoke.json",JsonUtility.ToJson(report,true));EditorApplication.isPlaying=false;
        }
    }
}
