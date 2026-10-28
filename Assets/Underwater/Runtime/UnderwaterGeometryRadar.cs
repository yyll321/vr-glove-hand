using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UnderwaterBaseline
{
    // Ideal first-intersection geometry, not electromagnetic propagation.
    [DisallowMultipleComponent]
    public sealed class UnderwaterGeometryRadar : MonoBehaviour
    {
        [Serializable] public sealed class Profile
        {
            public string name; public float range, azimuth, elevation; public int columns, rows;
            public Profile(string n,float r,float a,float e,int c,int v){name=n;range=r;azimuth=a;elevation=e;columns=c;rows=v;}
        }
        public Profile[] profiles = {
            new Profile("Near structure",15,90,20,121,9),
            new Profile("Wide survey",30,120,30,161,13),
            new Profile("Long corridor",60,60,12,121,7),
            new Profile("Vertical wall",25,70,60,101,21)};
        public Transform vehicle;
        public Quaternion vehicleAxesCorrection=Quaternion.identity;
        public Vector3 mountOffsetMetres=new Vector3(0,1.2f,2);
        public Transform ignoreRoot;
        public UnderwaterMotor motor;
        public bool seabed=true;
        public LayerMask detectionMask=1<<8;
        [Min(0.01f)] public float minimumRange=0.15f;
        [Range(1,10)] public float scanHz=5;
        [Range(16,1024)] public int raysPerFrame=256;
        public float targetTilt, currentTilt;
        public float tiltSpeed=25;
        public bool automaticTilt, scanning, showPanel=true, showRays;
        public bool useTaskConsole;
        public Texture RangeImage=>Latest!=null?image:null;
        public Transform graspWrist;
        public Vector3 graspPalmLocal;
        public bool graspScan;
        int frontProfile;
        public int selectedProfile;
        public int Frames {get;private set;}
        public float LastScanMs {get;private set;}
        public string Status {get;private set;}="Select a geometry profile then Start";
        static readonly HashSet<UnderwaterGeometryRadar> enabledRadars=new HashSet<UnderwaterGeometryRadar>();
        public static bool ActiveForInput
        {
            get{foreach(var r in enabledRadars)if(r && r.isActiveAndEnabled && (!r.motor ||
                (r.motor.origin && r.motor.origin.Camera && r.motor.InSeabed==r.seabed)))return true;return false;}
        }
        void OnEnable(){enabledRadars.Add(this);pending=null;Latest=null;rayIndex=0;}
        void OnDisable(){enabledRadars.Remove(this);StopScan();if(panel)panel.gameObject.SetActive(false);previousActive=false;}
        public event Action<Snapshot> ScanCompleted;
        [Serializable] public sealed class Snapshot
        {
            public int sequence,profile,columns,rows;
            public string model,frame="radar_flu";
            public string scanMode;
            public double time; public float minimumRange,range,azimuthDegrees,elevationDegrees,tiltDegrees;
            public Vector3 origin; public Quaternion rotation;
            public float[] ranges; public bool[] hits; public Vector3[] points;
            public bool Complete=>columns>0 && rows>0 && ranges!=null && hits!=null && points!=null && (long)columns*rows==ranges.Length && hits.Length==ranges.Length && points.Length==ranges.Length;
        }
        public Snapshot Latest {get;private set;}
        public string LastExportPath {get;private set;}
        Snapshot pending; int rayIndex; float nextScan,workMs;
        Texture2D image; Color32[] pixels;
        RawImage display; Text label; Canvas panel; bool previousActive;
        readonly RaycastHit[] hitBuffer=new RaycastHit[128];
        void EnsureProfiles()
        {
            if(profiles==null || profiles.Length==0)profiles=new[]{new Profile("Near structure",15,90,20,121,9)};
            for(int i=0;i<profiles.Length;i++)if(profiles[i]==null)profiles[i]=new Profile("Geometry preset",15,90,20,121,9);
        }
        public Profile Selected {get{EnsureProfiles();return profiles[Mathf.Clamp(selectedProfile,0,profiles.Length-1)];}}

        void Start(){EnsureProfiles();BuildPanel();UpdateMount();}
        void OnValidate(){EnsureProfiles();minimumRange=Mathf.Clamp(minimumRange,0.01f,99);scanHz=Mathf.Clamp(scanHz,1,10);raysPerFrame=Mathf.Clamp(raysPerFrame,16,1024);}
        public void SelectProfile(int index){EnsureProfiles();selectedProfile=Mathf.Clamp(index,0,profiles.Length-1);pending=null;Latest=null;nextScan=0;Status=scanning?"Profile selected; scanning":"Profile selected; press Start";}
        public void StartScan(){scanning=true;nextScan=0;}
        public void StopScan(){scanning=false;pending=null;}
        public void SetTilt(float degrees){targetTilt=Mathf.Clamp(degrees,-60,60);}
        void UpdateMount()
        {
            if(graspScan && graspWrist)
            {
                // A separate, explicitly selected ideal geometry scan beside the wrist.
                // The saved snapshot contains this origin; it is not fused with the front scan.
                var palm=graspWrist.TransformPoint(graspPalmLocal);
                transform.SetPositionAndRotation(palm+Vector3.up*.20f,Quaternion.LookRotation(Vector3.down,Vector3.forward));return;
            }
            if(!vehicle)return;
            Quaternion axes=vehicle.rotation*vehicleAxesCorrection;
            transform.SetPositionAndRotation(vehicle.position+axes*mountOffsetMetres,axes*Quaternion.Euler(-currentTilt,0,0));
        }
        public void RefreshMount(){UpdateMount();}
        void Update()
        {
            bool active=!motor || motor.InSeabed==seabed;
            if(panel)panel.gameObject.SetActive(active && showPanel && !useTaskConsole);
            if(!active){pending=null;previousActive=false;return;}
            if(!previousActive){nextScan=0;previousActive=true;}
            var key=Keyboard.current;
            // Left grip has one fixed purpose. All parameters use desktop/Inspector.
            if(motor && motor.Controllers!=null)
            {
                var controls=motor.Controllers;
                if(controls.RadarPressed)ToggleScan();
            }
            if(key!=null)
            {
                if(key.f6Key.wasPressedThisFrame)showPanel=!showPanel;
                if(key.f7Key.wasPressedThisFrame){if(scanning)StopScan();else StartScan();}
                if(key.f8Key.wasPressedThisFrame)automaticTilt=!automaticTilt;
                if(key.f9Key.wasPressedThisFrame)ExportLatest();
                if(key.f10Key.wasPressedThisFrame)SelectProfile((selectedProfile+1)%profiles.Length);
                if(key.f11Key.wasPressedThisFrame)SetGraspScan(!graspScan);
                if(key.leftBracketKey.isPressed){automaticTilt=false;SetTilt(targetTilt-tiltSpeed*Time.deltaTime);}
                if(key.rightBracketKey.isPressed){automaticTilt=false;SetTilt(targetTilt+tiltSpeed*Time.deltaTime);}
            }
            if(automaticTilt)targetTilt=Mathf.Sin(Time.time*0.25f)*45;
            currentTilt=Mathf.MoveTowards(currentTilt,targetTilt,tiltSpeed*Time.deltaTime);UpdateMount();
            if(scanning)TickScan();
            if(label)
            {
                int hits=0;float nearest=float.PositiveInfinity;string centre="--";
                if(Latest!=null && Latest.Complete)
                {
                    for(int i=0;i<Latest.hits.Length;i++)if(Latest.hits[i]){hits++;nearest=Mathf.Min(nearest,Latest.ranges[i]);}
                    int at=(Latest.rows/2)*Latest.columns+Latest.columns/2;if(Latest.hits[at])centre=Latest.ranges[at].ToString("F2")+" m";
                }
                label.text=$"GEOMETRY RADAR | {Selected.name} | {(scanning?"RUN":"STOP")}\n{Selected.range:F0} m | Tilt {currentTilt:F0} deg (positive = up)\nCentre {centre} | Nearest {(hits>0?nearest.ToString("F2")+" m":"--")} | Hits {hits}\nLeft grip: start/stop | PC: F10 preset, [ ] tilt, F9 export\n{Status}";
            }
        }
        public void ToggleScan(){if(scanning)StopScan();else StartScan();}
        public void SetGraspScan(bool enabled)
        {
            enabled=enabled && graspWrist;if(enabled==graspScan)return;
            graspScan=enabled;
            if(enabled){frontProfile=selectedProfile;for(int i=0;i<profiles.Length;i++)if(profiles[i].name=="Grasp close")SelectProfile(i);}
            else SelectProfile(frontProfile);
            pending=null;Latest=null;nextScan=0;
        }
        public static Vector3 BeamDirection(int column,int row,int columns,int rows,float azimuth,float elevation)
        {
            float a=(columns>1?(float)column/(columns-1)-0.5f:0)*azimuth*Mathf.Deg2Rad;
            float e=(rows>1?(float)row/(rows-1)-0.5f:0)*elevation*Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a)*Mathf.Cos(e),Mathf.Sin(e),Mathf.Cos(a)*Mathf.Cos(e));
        }
        void TickScan()
        {
            if(pending==null)
            {
                if(Time.unscaledTime<nextScan)return;
                var p=Selected;int columns=Mathf.Clamp(p.columns,3,241),rows=Mathf.Clamp(p.rows,1,31),n=columns*rows;
                pending=new Snapshot{sequence=Frames+1,profile=selectedProfile,model=p.name,time=Time.timeAsDouble,
                    columns=columns,rows=rows,minimumRange=minimumRange,range=Mathf.Clamp(p.range,minimumRange+0.01f,100),
                    azimuthDegrees=Mathf.Clamp(p.azimuth,1,170),elevationDegrees=Mathf.Clamp(p.elevation,0,120),tiltDegrees=graspScan?-90:currentTilt,scanMode=graspScan?"wrist_down":"front",
                    origin=transform.position,rotation=transform.rotation,ranges=new float[n],hits=new bool[n],points=new Vector3[n]};
                rayIndex=0;workMs=0; // Freeze one pose for a complete, budgeted scan.
            }
            float started=Time.realtimeSinceStartup;int end=Mathf.Min(rayIndex+raysPerFrame,pending.ranges.Length);
            for(;rayIndex<end;rayIndex++)
            {
                var local=BeamDirection(rayIndex%pending.columns,rayIndex/pending.columns,pending.columns,pending.rows,pending.azimuthDegrees,pending.elevationDegrees);
                Vector3 direction=pending.rotation*local;
                // Cast from origin: a blocker inside range_min must occlude farther objects.
                int count=Physics.RaycastNonAlloc(pending.origin,direction,hitBuffer,pending.range,detectionMask,QueryTriggerInteraction.Ignore);
                float nearest=float.PositiveInfinity;
                if(count==hitBuffer.Length)
                {
                    foreach(var h in Physics.RaycastAll(pending.origin,direction,pending.range,detectionMask,QueryTriggerInteraction.Ignore))
                        if(!IsSelf(h.collider))nearest=Mathf.Min(nearest,h.distance);
                }
                else for(int h=0;h<count;h++)if(!IsSelf(hitBuffer[h].collider))nearest=Mathf.Min(nearest,hitBuffer[h].distance);
                bool valid=nearest>=pending.minimumRange && nearest<=pending.range;
                pending.hits[rayIndex]=valid;pending.ranges[rayIndex]=valid?nearest:-1;
                // Local ROS FLU: forward, left, up. Invalid points require the hit mask.
                pending.points[rayIndex]=valid?new Vector3(local.z,-local.x,local.y)*nearest:Vector3.zero;
            }
            workMs+=(Time.realtimeSinceStartup-started)*1000;
            if(rayIndex<pending.ranges.Length){Status=$"Scanning {rayIndex}/{pending.ranges.Length}";return;}
            Latest=pending;pending=null;Frames++;LastScanMs=workMs;nextScan=Time.unscaledTime+1/Mathf.Max(1,scanHz);
            UpdateImage();Status=$"Frame {Frames} | {rayIndex} rays | CPU {workMs:F2} ms";ScanCompleted?.Invoke(Latest);
        }
        bool IsSelf(Collider c)=>ignoreRoot && (c.transform==ignoreRoot || c.transform.IsChildOf(ignoreRoot));
        void UpdateImage()
        {
            if(Latest==null || !Latest.Complete)return;
            if(!image || image.width!=Latest.columns || image.height!=Latest.rows)
            {if(image)Release(image);image=new Texture2D(Latest.columns,Latest.rows,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point};pixels=new Color32[Latest.ranges.Length];if(display)display.texture=image;}
            for(int i=0;i<pixels.Length;i++)pixels[i]=Latest.hits[i]?(Color32)Color.Lerp(new Color(1,0.64f,0.1f),new Color(0.05f,0.4f,0.7f),Latest.ranges[i]/Latest.range):new Color32(5,12,20,255);
            image.SetPixels32(pixels);image.Apply(false,false);
        }
        [Serializable] public sealed class Configuration {public int profile,columns,rows;public string model;public float range,azimuth,elevation,tilt;}
        public string ConfigurationJson()=>JsonUtility.ToJson(new Configuration{profile=selectedProfile,model=Selected.name,range=Selected.range,azimuth=Selected.azimuth,elevation=Selected.elevation,columns=Selected.columns,rows=Selected.rows,tilt=targetTilt});
        public void ApplyConfiguration(string json)
        {
            var c=JsonUtility.FromJson<Configuration>(json);
            if(c==null || !Finite(c.range) || !Finite(c.azimuth) || !Finite(c.elevation) || !Finite(c.tilt) ||
                c.range<=minimumRange || c.azimuth<=0 || c.elevation<0 || c.columns<3 || c.rows<1)
                throw new ArgumentException("A complete finite geometry radar configuration is required");
            SelectProfile(c.profile);var p=Selected;
            p.range=Mathf.Clamp(c.range,0.2f,100);p.azimuth=Mathf.Clamp(c.azimuth,1,170);p.elevation=Mathf.Clamp(c.elevation,0,120);
            p.columns=Mathf.Clamp(c.columns,3,241);p.rows=Mathf.Clamp(c.rows,1,31);SetTilt(c.tilt);
        }
        static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);
        [ContextMenu("Export Latest Geometry Radar Frame")]
        public void ExportLatest()
        {
            if(Latest==null || !Latest.Complete){Status="No completed frame to export";return;}
            string dir=Path.Combine(Application.persistentDataPath,"GeometryRadar");Directory.CreateDirectory(dir);
            string stem=Path.Combine(dir,$"radar-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Latest.sequence}");
            File.WriteAllText(stem+".json",JsonUtility.ToJson(Latest,true));
            var csv=new StringBuilder("row,column,azimuth_deg,elevation_deg,hit,range_m,forward_m,left_m,up_m\n");
            var cloud=new StringBuilder();int count=0;var ci=CultureInfo.InvariantCulture;
            for(int i=0;i<Latest.ranges.Length;i++)
            {
                int row=i/Latest.columns,column=i%Latest.columns;var p=Latest.points[i];
                float a=((float)column/(Latest.columns-1)-.5f)*Latest.azimuthDegrees;
                float e=Latest.rows>1?((float)row/(Latest.rows-1)-.5f)*Latest.elevationDegrees:0;
                csv.AppendFormat(ci,"{0},{1},{2:F4},{3:F4},{4},{5:F5},{6:F5},{7:F5},{8:F5}\n",row,column,a,e,Latest.hits[i]?1:0,Latest.ranges[i],p.x,p.y,p.z);
                if(Latest.hits[i]){count++;cloud.AppendFormat(ci,"{0:F5} {1:F5} {2:F5}\n",p.x,p.y,p.z);}
            }
            File.WriteAllText(stem+".csv",csv.ToString());
            File.WriteAllText(stem+"-local-FLU.ply",$"ply\nformat ascii 1.0\ncomment local radar FLU metres; x forward y left z up\nelement vertex {count}\nproperty float x\nproperty float y\nproperty float z\nend_header\n"+cloud);
            LastExportPath=stem+".json";Status=$"Exported frame {Latest.sequence}: JSON / CSV / PLY";
            Debug.Log("[GeometryRadar] Exported "+stem+" (.json, .csv, -local-FLU.ply)");
        }
        void BuildPanel()
        {
            if(useTaskConsole)return;
            if(!motor || !motor.origin || !motor.origin.Camera)return;
            var root=new GameObject("Geometry Radar Display",typeof(RectTransform),typeof(Canvas));root.transform.SetParent(transform,false);
            root.transform.localScale=Vector3.one*0.0014f;
            panel=root.GetComponent<Canvas>();panel.renderMode=RenderMode.WorldSpace;root.GetComponent<RectTransform>().sizeDelta=new Vector2(560,400);
            var state=new GameObject("State",typeof(RectTransform),typeof(Text));state.transform.SetParent(root.transform,false);
            var rect=state.GetComponent<RectTransform>();rect.anchoredPosition=new Vector2(0,135);rect.sizeDelta=new Vector2(550,130);
            label=state.GetComponent<Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=18;label.color=Color.white;
            var go=new GameObject("Angular range image",typeof(RectTransform),typeof(RawImage));go.transform.SetParent(root.transform,false);
            var rt=go.GetComponent<RectTransform>();rt.anchoredPosition=new Vector2(0,-40);rt.sizeDelta=new Vector2(530,210);display=go.GetComponent<RawImage>();
            var legend=new GameObject("Distance legend",typeof(RectTransform),typeof(Text));legend.transform.SetParent(root.transform,false);
            var lr=legend.GetComponent<RectTransform>();lr.anchoredPosition=new Vector2(0,-170);lr.sizeDelta=new Vector2(540,45);
            var text=legend.GetComponent<Text>();text.font=label.font;text.fontSize=17;text.color=Color.white;
            text.text="LEFT < azimuth > RIGHT | TOP = UP\nOrange = near | Blue = far | Dark = no valid hit";
            FollowDisplay();
        }
        void LateUpdate(){FollowDisplay();}
        void FollowDisplay()
        {
            if(!panel || !motor || !motor.origin || !motor.origin.Camera)return;
            var head=motor.origin.Camera.transform;
            panel.transform.SetPositionAndRotation(head.TransformPoint(new Vector3(.48f,-.12f,1.65f)),head.rotation);
        }
        void OnGUI()
        {
            if(useTaskConsole)return;
            if(!showPanel || (motor && motor.InSeabed!=seabed))return;
            float x=Mathf.Max(5,Screen.width-350);GUI.Box(new Rect(x,12,340,460),"Geometry RADAR | F7 start | F10 preset");
            for(int i=0;i<profiles.Length;i++)if(GUI.Button(new Rect(x+10,40+i*27,320,25),profiles[i].name+(i==selectedProfile?" [selected]":"")))SelectProfile(i);
            if(GUI.Button(new Rect(x+10,156,155,28),scanning?"Stop":"Start")){if(scanning)StopScan();else StartScan();}
            if(GUI.Button(new Rect(x+175,156,155,28),"Export 3 files"))ExportLatest();
            SetTilt(GUI.HorizontalSlider(new Rect(x+15,197,310,18),targetTilt,-60,60));
            string centre="--";
            if(Latest!=null){int at=(Latest.rows/2)*Latest.columns+Latest.columns/2;if(Latest.hits[at])centre=Latest.ranges[at].ToString("F2")+" m";}
            GUI.Label(new Rect(x+10,215,320,40),$"Centre {centre} | Tilt {currentTilt:F1} deg\n{Status}");
            if(image)GUI.DrawTexture(new Rect(x+10,260,320,120),image,ScaleMode.StretchToFill);
            var system=GetComponentInParent<UnderwaterRadarSystem>();
            if(system)
            {
                if(GUI.Button(new Rect(x+10,392,155,28),"Place 5 m check target"))system.ShowFiveMetreTarget(this);
                if(GUI.Button(new Rect(x+175,392,155,28),"Hide check target"))system.HideTarget();
                GUI.Label(new Rect(x+10,425,320,40),system.CalibrationStatus);
            }
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color=Color.cyan;
            if(showRays && Latest!=null)
            {
                for(int i=0;i<Latest.ranges.Length;i+=Mathf.Max(1,Latest.ranges.Length/160))
                    if(Latest.hits[i]){var p=Latest.points[i];Gizmos.DrawLine(Latest.origin,Latest.origin+Latest.rotation*new Vector3(-p.y,p.z,p.x));}
            }
            else Gizmos.DrawRay(transform.position,transform.forward*3);
        }
        static void Release(UnityEngine.Object obj){if(Application.isPlaying)Destroy(obj);else DestroyImmediate(obj);}
        void OnDestroy(){if(image)Release(image);if(panel)Release(panel.gameObject);}
    }
}
