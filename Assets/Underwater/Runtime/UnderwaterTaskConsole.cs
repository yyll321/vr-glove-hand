using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
namespace UnderwaterBaseline
{
    // Only one small, non-stereo camera is rendered at a limited rate.
    [DefaultExecutionOrder(500)]
    public sealed class UnderwaterTaskConsole : MonoBehaviour
    {
        public UnderwaterMotor motor;public PicoArmPoseDriver[] arms;public UnderwaterGeometryRadar[] radars;
        public UnderwaterGraspTask[] tasks;
        public Camera[] views; // pool grip/arm/front, seabed grip/arm/front
        public bool visible=true;[Range(0,2)]public int selectedView;
        [Range(1,15)]public float cameraHz=8;
        public Vector3 headOffset=new Vector3(-.67f,.38f,1.8f);
        public float panelWidth=.47f;
        public int CaptureCount {get;private set;}
        public string CurrentSummary {get;private set;}="";
        public RenderTexture Preview {get;private set;}
        public Canvas Panel {get;private set;}
        public UnderwaterGeometryRadar ActiveRadar=>radars.FirstOrDefault(r=>r && r.isActiveAndEnabled && r.seabed==motor.InSeabed);
        public UnderwaterGraspTask ActiveTask=>tasks.FirstOrDefault(t=>t && t.arm.SeabedArea==motor.InSeabed);
        RawImage preview,radarImage;Text text,viewName;float nextCapture;
        readonly Vector3[] gripOffsets=new Vector3[2],gripLooks=new Vector3[2],gripUps=new Vector3[2];
        public static readonly string[] ViewNames={"WA100 grasp","Full arm + WA100","ROV front"};
        void Start()
        {
            Preview=new RenderTexture(384,216,24,RenderTextureFormat.ARGB32){name="Task Camera Preview",antiAliasing=1};Preview.Create();
            foreach(var camera in views)if(camera){camera.enabled=false;camera.targetTexture=Preview;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.cullingMask&=~(1<<5);}
            foreach(var arm in arms){int i=arm.SeabedArea?1:0;var inverse=Quaternion.Inverse(arm.Wrist.rotation);gripOffsets[i]=inverse*new Vector3(0,.35f,-.45f);gripLooks[i]=inverse*(Vector3.down*.35f);gripUps[i]=inverse*Vector3.up;}
            Build();
        }
        void Build()
        {
            var root=new GameObject("Compact Task Panel",typeof(RectTransform),typeof(Canvas));root.layer=5;root.transform.SetParent(transform,false);
            Panel=root.GetComponent<Canvas>();Panel.renderMode=RenderMode.WorldSpace;var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(360,334);
            root.transform.localScale=Vector3.one*(panelWidth/360);
            var bg=new GameObject("Background",typeof(RectTransform),typeof(Image));bg.layer=5;bg.transform.SetParent(root.transform,false);
            var r=(RectTransform)bg.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.sizeDelta=Vector2.zero;bg.GetComponent<Image>().color=new Color(.015f,.04f,.055f,.84f);
            viewName=Label("Camera",new Vector2(0,144),new Vector2(344,27),19);
            preview=Picture("Selected camera",new Vector2(0,49),new Vector2(344,194));preview.texture=Preview;
            radarImage=Picture("Radar distance",new Vector2(0,-72),new Vector2(344,42));
            text=Label("Task state",new Vector2(0,-128),new Vector2(344,65),15);
        }
        RawImage Picture(string name,Vector2 at,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(RawImage));go.layer=5;go.transform.SetParent(Panel.transform,false);
            var rect=(RectTransform)go.transform;rect.anchoredPosition=at;rect.sizeDelta=size;return go.GetComponent<RawImage>();
        }
        Text Label(string name,Vector2 at,Vector2 size,int font)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.layer=5;go.transform.SetParent(Panel.transform,false);
            var rect=(RectTransform)go.transform;rect.anchoredPosition=at;rect.sizeDelta=size;
            var label=go.GetComponent<Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=font;label.color=Color.white;label.raycastTarget=false;return label;
        }
        void Update()
        {
            if(motor.Controllers!=null && motor.Controllers.PanelPressed)visible=!visible;
            var key=Keyboard.current;
            if(key!=null)
            {
                if(key.f1Key.wasPressedThisFrame)visible=!visible;
                if(key.f2Key.wasPressedThisFrame)SelectView(0);if(key.f3Key.wasPressedThisFrame)SelectView(1);if(key.f4Key.wasPressedThisFrame)SelectView(2);
            }
            if(Panel)Panel.gameObject.SetActive(visible && motor.XrActive);
            var radar=ActiveRadar;var task=ActiveTask;string centre="--";
            if(radar && radar.Latest!=null)
            {
                var s=radar.Latest;int at=(s.rows/2)*s.columns+s.columns/2;
                // Editor hot reload/profile changes can restore an empty scan.
                // Display -- until a complete frame is available.
                if(s.rows>0 && s.columns>0 && s.hits!=null && s.ranges!=null && at>=0 && at<s.hits.Length && at<s.ranges.Length && s.hits[at])centre=s.ranges[at].ToString("F2")+" m";
            }
            CurrentSummary=(radar?(radar.graspScan?"WRIST ":"FRONT ")+(radar.scanning?"RUN":"STOP")+" #"+radar.Frames+" / C "+centre:"Radar module OFF")+"\n"+
                (task?task.Status+"\nDelivered "+task.DeliveredCount+" / "+task.targets.Length:"No grasp task");
            if(task && task.arm.Contact && task.arm.Contact.Haptics)CurrentSummary+="\n"+task.arm.Contact.Haptics.Status;
            if(text)text.text=CurrentSummary;if(viewName)viewName.text=ViewNames[selectedView]+"  [F2/F3/F4]";
            if(text){text.resizeTextForBestFit=true;text.resizeTextMinSize=10;text.resizeTextMaxSize=15;}
            if(radarImage){radarImage.texture=radar && radar.showPanel?radar.RangeImage:null;radarImage.color=radarImage.texture?Color.white:Color.clear;}
        }
        public void SelectView(int index){selectedView=Mathf.Clamp(index,0,2);nextCapture=0;}
        void LateUpdate()
        {
            if(!visible || !Preview)return;
            if(Panel){var head=motor.origin.Camera.transform;Panel.transform.SetPositionAndRotation(head.TransformPoint(headOffset),head.rotation);}
            if(Time.unscaledTime<nextCapture)return;nextCapture=Time.unscaledTime+1/Mathf.Max(1,cameraHz);
            Capture();
        }
        public void Capture()
        {
            if(!Preview || views==null || views.Length<6)return;
            int region=motor.InSeabed?1:0;var arm=arms.First(a=>a.SeabedArea==motor.InSeabed);var camera=views[region*3+selectedView];
            var hand=arm.Contact.Hand;hand.SyncMountedRoot();
            var filters=(selectedView==2?arm.transform: selectedView==1?arm.WholeArmContact.protectedRoot:hand.transform).GetComponentsInChildren<Renderer>();
            if(filters.Length==0)return;var bounds=filters[0].bounds;foreach(var renderer in filters)bounds.Encapsulate(renderer.bounds);
            Vector3 up=Vector3.up,position,look;
            if(selectedView==0)
            {
                var palm=ActiveTask.PalmPoint;look=palm+arm.Wrist.rotation*gripLooks[region];
                position=palm+arm.Wrist.rotation*gripOffsets[region];up=arm.Wrist.rotation*gripUps[region];
                var bench=ActiveTask.GetComponentInChildren<UnderwaterGraspBench>();
                if(bench && bench.sand && !ActiveTask.Held && Vector3.Distance(palm,bench.sand.bounds.center)<1.2f)
                    look=Vector3.Lerp(palm,bench.sand.bounds.center,.68f);
                if(Physics.Raycast(palm+Vector3.up*.1f,Vector3.down,out var floor,3,1<<8,QueryTriggerInteraction.Ignore))
                {look.y=Mathf.Max(look.y,floor.point.y+.02f);position.y=Mathf.Max(position.y,floor.point.y+.08f);}
            }
            else
            {
                look=bounds.center;float radius=Mathf.Max(bounds.extents.magnitude,.25f);
                float distance=radius/Mathf.Sin(camera.fieldOfView*.5f*Mathf.Deg2Rad)*1.12f;
                position=look+(selectedView==2?Vector3.forward:new Vector3(-.7f,.4f,-1).normalized)*distance;
            }
            camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(look-position,up));
            camera.targetTexture=Preview;camera.Render();CaptureCount++;
        }
        void OnGUI()
        {
            if(!visible || motor.XrActive)return;
            GUI.Box(new Rect(10,10,310,330),"Task console | F1 hide");
            for(int i=0;i<3;i++)if(GUI.Button(new Rect(18+i*99,34,96,24),new[]{"Hand F2","Arm F3","ROV F4"}[i]))SelectView(i);
            if(Preview)GUI.DrawTexture(new Rect(18,64,294,165),Preview,ScaleMode.ScaleToFit);
            var radar=ActiveRadar;if(radar && radar.RangeImage && radar.showPanel)GUI.DrawTexture(new Rect(18,234,294,32),radar.RangeImage);
            GUI.Label(new Rect(18,270,294,58),CurrentSummary);
            if(GUI.Button(new Rect(18,343,140,24),"Radar settings"))settings=!settings;
            if(settings && radar)
            {
                float actionsY=397+radar.profiles.Length*24;
                GUI.Box(new Rect(10,374,310,actionsY+196-374),"Radar parameters");
                for(int i=0;i<radar.profiles.Length;i++)if(GUI.Button(new Rect(18,397+i*24,294,22),radar.profiles[i].name))radar.SelectProfile(i);
                if(GUI.Button(new Rect(18,actionsY,140,24),radar.scanning?"Stop F7":"Start F7"))radar.ToggleScan();
                if(GUI.Button(new Rect(166,actionsY,146,24),"Export F9"))radar.ExportLatest();
                float tilt=GUI.HorizontalSlider(new Rect(22,actionsY+35,286,18),radar.targetTilt,-60,60);
                if(Mathf.Abs(tilt-radar.targetTilt)>.01f){radar.automaticTilt=false;radar.SetTilt(tilt);}
                var system=radar.GetComponentInParent<UnderwaterRadarSystem>();
                if(system && GUI.Button(new Rect(18,actionsY+61,140,24),"Place 5m target"))system.ShowFiveMetreTarget(radar);
                if(system && GUI.Button(new Rect(166,actionsY+61,146,24),"Hide target"))system.HideTarget();
                if(GUI.Button(new Rect(18,actionsY+94,294,24),radar.graspScan?"F11: wrist scan -> front scan":"F11: front scan -> wrist scan"))radar.SetGraspScan(!radar.graspScan);
                int hits=0;float nearest=float.PositiveInfinity;
                if(radar.Latest!=null)for(int i=0;i<radar.Latest.hits.Length;i++)if(radar.Latest.hits[i]){hits++;nearest=Mathf.Min(nearest,radar.Latest.ranges[i]);}
                GUI.Label(new Rect(18,actionsY+120,294,67),"Nearest "+(hits>0?nearest.ToString("F2")+" m":"--")+" | Hits "+hits+"\n"+radar.Status+"\n"+(system?system.CalibrationStatus:""));
            }
        }
        bool settings;
        void OnDisable(){if(Panel)Panel.gameObject.SetActive(false);}
        void OnDestroy(){if(Preview){foreach(var c in views)if(c)c.targetTexture=null;Preview.Release();Destroy(Preview);}if(Panel)Destroy(Panel.gameObject);}
    }
}
