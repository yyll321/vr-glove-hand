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
        static double start;static bool sea,captured,calibrated,exported,disabled,reEnabled;
        static bool calibrationPassed,exportsPassed,modulePassed;static Vector3 initialRov;static float movementDistance;static int pausedFrames;
        static bool approachPassed;static float calibrationRange,approachRange,expectedApproach;static int sampleCount;static bool checkedApproach;
        static string movementContact;
        static int controlStep;static double nextControl;static bool controlsDone,controlsPassed;
        static bool oldLights,oldWake,oldMute,oldAutomatic,originalArea;static int oldTier,oldBubbles,oldTests,oldProfile;static float oldTilt;
        static Vector3 controlRov,controlEye,holdRov;
        static bool oldPanelVisible;
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
            if(mode==PlayModeStateChange.EnteredPlayMode)
            {start=EditorApplication.timeSinceStartup;sea=captured=calibrated=exported=disabled=reEnabled=checkedApproach=false;calibrationPassed=exportsPassed=modulePassed=approachPassed=false;movementDistance=0;controlStep=0;nextControl=1;controlsDone=false;controlsPassed=true;errors.Clear();Application.logMessageReceived+=Log;}
            if(mode==PlayModeStateChange.ExitingPlayMode){Application.logMessageReceived-=Log;SessionState.SetBool(Key,false);}
        }
        static void Log(string message,string stack,LogType type){if((type==LogType.Error || type==LogType.Exception) && errors.Count<30)errors.Add(message);}
        [Serializable] sealed class Report
        {
            public bool passed;public string time;public int poolFrames,seabedFrames,seabedHits;public float poolScanMs,seabedScanMs,flowStepMs;public string[] errors;
            public bool calibrationFiveMetres,exportJsonCsvPly,moduleDisableStopsRadar;public float rovMovedMetres;public string movementContact;
            public bool rangeFollowsRovApproach;public float calibrationRangeMetres,approachRangeMetres,expectedApproachMetres;public int samplesPerFrame;
            public bool fixedControlsPlayRegression;
            public string limitation="Editor Play smoke only; no HMD perception or performance certification";
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || start==0)return;
            double elapsed=EditorApplication.timeSinceStartup-start;var radars=Object.FindObjectsOfType<UnderwaterGeometryRadar>(true);
            var system=Object.FindObjectOfType<UnderwaterRadarSystem>(true);
            var activeRadar=radars.FirstOrDefault(r=>r.seabed);
            var mover=activeRadar?activeRadar.vehicle.GetComponent<UnderwaterRovMovement>():null;
            if(elapsed>21 && !disabled && system)
            {system.HideTarget();pausedFrames=activeRadar.Frames;system.gameObject.SetActive(false);disabled=true;}
            if(disabled && !reEnabled && elapsed>22 && system)
            {
                modulePassed=activeRadar.Frames==pausedFrames && !activeRadar.scanning && !UnderwaterGeometryRadar.ActiveForInput &&
                    system.GetComponentsInChildren<Canvas>(true).All(c=>!c.isActiveAndEnabled) && mover.isActiveAndEnabled;
                mover.transform.position=initialRov;Physics.SyncTransforms();
                system.gameObject.SetActive(true);reEnabled=true;
            }
            if(disabled && !reEnabled)return;
            var motor=Object.FindObjectOfType<UnderwaterMotor>();
            if(!controlsDone){CheckControls(elapsed,motor,radars);return;}
            foreach(var radar in radars)if(!radar.scanning)radar.StartScan();
            if(elapsed>7 && !sea && motor){if(!motor.InSeabed)motor.SwitchArea();sea=true;initialRov=mover.transform.position;}
            if(elapsed>10 && elapsed<12 && mover)
            {
                mover.Move(Vector2.right,.02f);
                movementContact=mover.ContactObstacle+" wrist="+(mover.handContact && mover.handContact.Arm?mover.handContact.Arm.Wrist.position.ToString("F3"):"none");
                Physics.SyncTransforms();movementDistance=Vector3.Distance(mover.transform.position,initialRov);
            }
            if(elapsed>14 && !captured && motor)
            {
                captured=true;Directory.CreateDirectory("Logs/RadarReview");
                var camera=motor.origin.Camera;var target=RenderTexture.GetTemporary(1280,720,24);var previous=camera.targetTexture;var active=RenderTexture.active;
                var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
                try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("Logs/RadarReview/seabed-play.png",image.EncodeToPNG());}
                finally{camera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);Object.Destroy(image);}
            }
            if(elapsed>15 && !calibrated && system)
            {
                // Use an unobstructed mid-water test lane. Dense natural reefs
                // can legitimately occlude a target placed from the initial hull.
                mover.transform.position=new Vector3(62,6,-8);Physics.SyncTransforms();
                activeRadar.SetTilt(0);system.ShowFiveMetreTarget(activeRadar);calibrated=true;
            }
            if(elapsed>18 && !exported && activeRadar && activeRadar.Latest!=null)
            {
                var snapshot=activeRadar.Latest;int at=(snapshot.rows/2)*snapshot.columns+snapshot.columns/2;
                calibrationRange=snapshot.ranges[at];sampleCount=snapshot.ranges.Length;
                calibrationPassed=snapshot.hits[at] && Mathf.Abs(calibrationRange-5)<.03f;
                activeRadar.ExportLatest();string path=activeRadar.LastExportPath;
                exportsPassed=!string.IsNullOrEmpty(path) && File.Exists(path) && File.Exists(Path.ChangeExtension(path,"csv")) && File.Exists(Path.ChangeExtension(path,null)+"-local-FLU.ply");
                if(exportsPassed)
                {
                    var saved=JsonUtility.FromJson<UnderwaterGeometryRadar.Snapshot>(File.ReadAllText(path));int valid=saved.hits.Count(h=>h);
                    exportsPassed=saved.ranges.Length==sampleCount && File.ReadAllLines(Path.ChangeExtension(path,"csv")).Length==sampleCount+1 &&
                        File.ReadAllText(Path.ChangeExtension(path,null)+"-local-FLU.ply").Contains("element vertex "+valid+"\n");
                }
                Vector3 before=mover.transform.position;
                for(int i=0;i<20;i++){mover.Move(Vector2.up,.02f);Physics.SyncTransforms();}
                expectedApproach=5-Vector3.Dot(mover.transform.position-before,activeRadar.transform.forward);
                exported=true;
            }
            if(elapsed>20 && exported && !checkedApproach && activeRadar.Latest!=null)
            {
                var s=activeRadar.Latest;int at=(s.rows/2)*s.columns+s.columns/2;approachRange=s.ranges[at];
                approachPassed=s.hits[at] && Mathf.Abs(approachRange-expectedApproach)<.03f;checkedApproach=true;
            }
            if(elapsed<26)return;
            var pool=radars.FirstOrDefault(r=>!r.seabed);var seabed=radars.FirstOrDefault(r=>r.seabed);var volume=GameObject.Find("Seabed Fluid Volume").GetComponent<UnderwaterFluidVolume>();
            var report=new Report{time=DateTime.Now.ToString("O"),poolFrames=pool?pool.Frames:0,seabedFrames=seabed?seabed.Frames:0,
                poolScanMs=pool?pool.LastScanMs:0,seabedScanMs=seabed?seabed.LastScanMs:0,flowStepMs=volume.LastStepMilliseconds,
                seabedHits=seabed && seabed.Latest!=null?seabed.Latest.hits.Count(h=>h):0,errors=errors.ToArray(),
                calibrationFiveMetres=calibrationPassed,exportJsonCsvPly=exportsPassed,moduleDisableStopsRadar=modulePassed,rovMovedMetres=movementDistance,movementContact=movementContact,
                rangeFollowsRovApproach=approachPassed,calibrationRangeMetres=calibrationRange,approachRangeMetres=approachRange,expectedApproachMetres=expectedApproach,samplesPerFrame=sampleCount,fixedControlsPlayRegression=controlsPassed};
            report.passed=controlsPassed && report.poolFrames>2 && report.seabedFrames>2 && report.seabedHits>0 && report.errors.Length==0 && calibrationPassed && exportsPassed && modulePassed && approachPassed && movementDistance>.15f;
            File.WriteAllText("Logs/RadarReview/play-smoke.json",JsonUtility.ToJson(report,true));EditorApplication.isPlaying=false;
        }
        static void VerifyControl(bool ok,string message){if(!ok){controlsPassed=false;errors.Add("Control regression: "+message);}}
        static void CheckControls(double elapsed,UnderwaterMotor motor,UnderwaterGeometryRadar[] radars)
        {
            if(elapsed<nextControl || !motor || motor.Controllers==null)return;nextControl=elapsed+.25;
            var radar=radars.First(r=>r.seabed==motor.InSeabed);var interaction=Object.FindObjectOfType<UnderwaterInteraction>();
            var sound=Object.FindObjectOfType<UnderwaterSoundscape>();var performance=Object.FindObjectOfType<UnderwaterPerformance>();
            var c=motor.Controllers;var neutral=new UnderwaterControllerInput.State();
            switch(controlStep++)
            {
                case 0:
                    oldLights=interaction.lightsEnabled;oldWake=interaction.wakeDemo;oldMute=sound.muted;oldAutomatic=performance.automatic;
                    oldTier=motor.environment.quality;oldBubbles=motor.environment.BubbleBursts;oldTests=sound.TestCount;
                    oldProfile=radar.selectedProfile;oldTilt=radar.targetTilt;originalArea=motor.InSeabed;
                    oldPanelVisible=Object.FindObjectOfType<UnderwaterTaskConsole>().visible;
                    radar.StopScan();c.EditorInputOverride=neutral;break;
                case 1:c.EditorInputOverride=new UnderwaterControllerInput.State{leftGrip=true};break;
                case 2:
                    VerifyControl(!radar.scanning,"left grip fired before chord decision");
                    c.EditorInputOverride=new UnderwaterControllerInput.State{leftGrip=true,a=true,b=true,y=true,leftClick=true};break;
                case 3:case 4:
                    VerifyControl(Object.FindObjectOfType<UnderwaterTaskConsole>().visible!=oldPanelVisible,"Grip+Y did not toggle exactly one panel state");
                    VerifyControl(!radar.scanning && radar.selectedProfile==oldProfile && radar.targetTilt==oldTilt && radar.LastExportPath==null,"old radar combination still active");
                    VerifyControl(interaction.lightsEnabled!=oldLights && interaction.wakeDemo!=oldWake,"A/B changed purpose with left grip");
                    VerifyControl(motor.environment.BubbleBursts==oldBubbles && motor.environment.quality==(oldTier+1)%3,"Y / left click repeated or changed purpose");
                    if(controlStep==5)c.EditorInputOverride=neutral;break;
                case 5:c.EditorInputOverride=new UnderwaterControllerInput.State{leftGrip=true};break;
                case 6:
                    VerifyControl(!radar.scanning,"held left grip fired before release");
                    c.EditorInputOverride=new UnderwaterControllerInput.State{rightTrigger=true};break;
                case 7:
                    VerifyControl(radar.scanning,"single left grip release did not start radar");
                    VerifyControl(sound.TestCount==oldTests+1 && !sound.muted,"right trigger did not produce exactly one audio test");
                    c.EditorInputOverride=new UnderwaterControllerInput.State{rightTrigger=true,leftTrigger=true};break;
                case 8:
                    VerifyControl(sound.TestCount==oldTests+1 && sound.muted,"left trigger / held right trigger conflict");break;
                case 9:c.EditorInputOverride=new UnderwaterControllerInput.State{x=true};break;
                case 10:VerifyControl(motor.InSeabed!=originalArea,"X did not switch region");break;
                case 11:
                    VerifyControl(motor.InSeabed!=originalArea,"held X switched region repeatedly");c.EditorInputOverride=neutral;break;
                case 12:c.EditorInputOverride=new UnderwaterControllerInput.State{x=true};break;
                case 13:
                    VerifyControl(motor.InSeabed==originalArea,"second X press did not return");
                    controlRov=radar.vehicle.position;controlEye=motor.origin.Camera.transform.position;
                    c.EditorInputOverride=new UnderwaterControllerInput.State{rightStick=Vector2.up};break;
                case 14:
                    VerifyControl(Vector3.Distance(radar.vehicle.position,controlRov)>.02f,"right stick did not move ROV through normal input path");
                    VerifyControl(Vector3.Distance(motor.origin.Camera.transform.position,controlEye)<.05f,"right stick also moved observer");
                    holdRov=radar.vehicle.position;c.EditorInputOverride=new UnderwaterControllerInput.State{rightStick=Vector2.up,rightGrip=true};break;
                case 15:holdRov=radar.vehicle.position;break;
                case 16:
                    VerifyControl(Vector3.Distance(radar.vehicle.position,holdRov)<.01f,"ROV moved during wrist grip control");
                    c.EditorInputOverride=neutral;break;
                default:
                    c.EditorInputOverride=null;interaction.lightsEnabled=oldLights;interaction.SetWakeDemo(oldWake);
                    Object.FindObjectOfType<UnderwaterTaskConsole>().visible=oldPanelVisible;
                    if(sound.muted!=oldMute)sound.ToggleMute();performance.SelectQuality(oldTier);performance.automatic=oldAutomatic;
                    radar.vehicle.position=controlRov;Physics.SyncTransforms();controlsDone=true;break;
            }
        }
    }
}
