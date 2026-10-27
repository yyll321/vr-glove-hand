using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterRadarSurveyChecks
    {
        public static void Run()
        {
            int checks=0;Action<bool,string> check=(ok,message)=>{if(!ok)throw new Exception(message);checks++;};
            for(int row=0;row<9;row++)for(int col=0;col<121;col++)
            {
                var d=UnderwaterGeometryRadar.BeamDirection(col,row,121,9,90,20);
                check(Mathf.Abs(d.magnitude-1)<1e-5f,"Non-unit radar beam");
            }
            check(UnderwaterGeometryRadar.BeamDirection(60,4,121,9,90,20)==Vector3.forward,"Centre beam not forward");
            var hull=UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Underwater/RadarSurvey/ClosedDisplacementHull65L.asset");
            var vertices=hull.vertices;var triangles=hull.triangles;var clipped=new Vector3[5];
            foreach(float water in new[]{-.5f,0f,.5f})
            {
                Vector3 sum=Vector3.zero,torque=Vector3.zero;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    int n=UnderwaterSurfaceBuoyancy.ClipBelow(vertices[triangles[i]],vertices[triangles[i+1]],vertices[triangles[i+2]],water,clipped);
                    for(int j=1;j<n-1;j++)
                    {
                        UnderwaterSurfaceBuoyancy.Triangle(clipped[0],clipped[j],clipped[j+1],water,1000,9.81f,out var force,out var centre,out _);
                        sum+=force;torque+=Vector3.Cross(centre,force);
                    }
                }
                float expected=water<-.13f?0:water>.13f?637.65f:318.825f;
                check(Mathf.Abs(sum.y-expected)<.02f && new Vector2(sum.x,sum.z).magnitude<.02f,"Hydrostatic force does not equal rho*g*submerged volume");
                check(torque.magnitude<.02f,"Symmetric hull generated spurious torque");
            }
            var root=new GameObject("TEMP Geometry Radar Physics Checks"){hideFlags=HideFlags.HideAndDontSave};
            var origin=new Vector3(10000,10000,10000);
            try
            {
                var target=new GameObject("Near blocker");target.transform.SetParent(root.transform);target.layer=8;target.transform.position=origin+Vector3.forward*5;target.AddComponent<BoxCollider>().size=new Vector3(4,4,1);
                var far=new GameObject("Far target");far.transform.SetParent(root.transform);far.layer=8;far.transform.position=origin+Vector3.forward*9;far.AddComponent<BoxCollider>().size=new Vector3(4,4,1);
                var scanner=new GameObject("Scanner");scanner.transform.SetParent(root.transform);scanner.transform.position=origin;
                var radar=scanner.AddComponent<UnderwaterGeometryRadar>();radar.profiles=new[]{new UnderwaterGeometryRadar.Profile("test",15,1,0,3,1)};radar.raysPerFrame=16;
                var tick=typeof(UnderwaterGeometryRadar).GetMethod("TickScan",BindingFlags.Instance|BindingFlags.NonPublic);
                Physics.SyncTransforms();radar.StartScan();tick.Invoke(radar,null);
                check(radar.Latest!=null && radar.Latest.hits[1] && Mathf.Abs(radar.Latest.ranges[1]-4.5f)<.005f,"Nearest collider range incorrect");
                check(Vector3.Distance(radar.Latest.points[1],new Vector3(4.5f,0,0))<.005f,"FLU point conversion incorrect");
                target.transform.position=origin+Vector3.forward*.55f;target.GetComponent<BoxCollider>().size=new Vector3(4,4,.2f);Physics.SyncTransforms();
                radar.minimumRange=1;radar.SelectProfile(0);radar.StartScan();tick.Invoke(radar,null);
                check(!radar.Latest.hits[1] && radar.Latest.ranges[1]==-1,"Near blind-zone blocker failed to occlude far target");
                radar.ignoreRoot=target.transform;radar.SelectProfile(0);radar.StartScan();tick.Invoke(radar,null);
                check(radar.Latest.hits[1] && Mathf.Abs(radar.Latest.ranges[1]-8.5f)<.005f,"Ignore-root filtering incorrect");
                radar.profiles[0].columns=33;radar.SelectProfile(0);radar.StartScan();tick.Invoke(radar,null);scanner.transform.position+=Vector3.right*2;tick.Invoke(radar,null);tick.Invoke(radar,null);
                check(radar.Latest.origin==origin,"Scan origin changed halfway through budgeted frame");
                bool rejected=false;try{radar.ApplyConfiguration("{}");}catch(ArgumentException){rejected=true;}check(rejected,"Incomplete ROS configuration accepted");
                // Chord wins in either order. Singles are committed only on release.
                using(var controls=new UnderwaterControllerInput())for(int mask=0;mask<256;mask++)
                {
                    controls.ApplyState(default);
                    var state=new UnderwaterControllerInput.State{a=(mask&1)!=0,b=(mask&2)!=0,x=(mask&4)!=0,y=(mask&8)!=0,
                        leftClick=(mask&16)!=0,leftGrip=(mask&32)!=0,leftTrigger=(mask&64)!=0,rightTrigger=(mask&128)!=0};
                    controls.ApplyState(state);bool chord=state.y&&state.leftGrip;
                    check(controls.LightPressed==state.a && controls.WakePressed==state.b && controls.AreaPressed==state.x &&
                        !controls.BubblePressed && controls.QualityPressed==state.leftClick && !controls.RadarPressed && controls.PanelPressed==chord &&
                        controls.MutePressed==state.leftTrigger && controls.SoundTestPressed==state.rightTrigger,"Panel chord did not suppress its two single actions");
                    controls.ApplyState(state);
                    check(!controls.PanelPressed && !controls.RadarPressed && !controls.BubblePressed && !controls.LightPressed,"Held chord/button repeated");
                    controls.ApplyState(default);
                    check(controls.RadarPressed==(state.leftGrip&&!chord) && controls.BubblePressed==(state.y&&!chord),"Single release mapping incorrect");
                }
                using(var controls=new UnderwaterControllerInput())foreach(bool gripFirst in new[]{true,false})
                {
                    controls.ApplyState(default);controls.ApplyState(new UnderwaterControllerInput.State{leftGrip=gripFirst,y=!gripFirst});
                    controls.ApplyState(new UnderwaterControllerInput.State{leftGrip=true,y=true});
                    check(controls.PanelPressed && !controls.RadarPressed && !controls.BubblePressed,"Chord depended on press order");
                    controls.ApplyState(default);check(!controls.RadarPressed&&!controls.BubblePressed,"Chord leaked a release action");
                }
                check(UnderwaterControllerInput.MapMovement(Vector2.up)==Vector3.up && UnderwaterControllerInput.MapMovement(Vector2.down)==Vector3.down &&
                    UnderwaterControllerInput.MapMovement(Vector2.left)==Vector3.forward && UnderwaterControllerInput.MapMovement(Vector2.right)==Vector3.back,"User left-stick mapping changed");
                radar.StopScan();radar.ToggleScan();check(radar.scanning,"Dedicated radar toggle failed to start");
                radar.ToggleScan();check(!radar.scanning,"Dedicated radar toggle failed to stop");
                radar.StartScan();scanner.SetActive(false);check(!radar.isActiveAndEnabled,"Disabling radar object left component active");scanner.SetActive(true);
                // Exercise collision-aware ROV translation away from the real scene.
                var mover=scanner.AddComponent<UnderwaterRovMovement>();mover.seabed=true;scanner.transform.position=new Vector3(62,10000,0);
                var original=scanner.transform.position;mover.Move(Vector2.up,.02f);
                check(scanner.transform.position.z>original.z && scanner.transform.position.y==original.y,"ROV forward command failed / changed depth");
                target.transform.position=scanner.transform.position+Vector3.forward*1.5f+Vector3.up*1.1f;
                target.GetComponent<BoxCollider>().size=new Vector3(5,5,.2f);Physics.SyncTransforms();
                for(int i=0;i<100;i++){mover.Move(Vector2.up,.02f);Physics.SyncTransforms();}
                check(scanner.transform.position.z<.5f && mover.Blocked,"ROV passed through its collision test wall");
            }
            finally{Object.DestroyImmediate(root);Physics.SyncTransforms();}
            Directory.CreateDirectory("Logs/RadarReview");File.WriteAllText("Logs/RadarReview/math-physics-checks.txt","PASS "+checks+" Unity mathematical and Physics.Raycast assertions "+DateTime.Now.ToString("O"));
        }
    }
}
