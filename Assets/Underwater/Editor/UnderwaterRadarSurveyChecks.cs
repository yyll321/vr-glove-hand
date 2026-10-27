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
                radar.StopScan();radar.HandleStick(Vector2.left,false,.02f);check(radar.scanning,"Left stick flick failed to start");
                radar.HandleStick(Vector2.left,false,.02f);check(radar.scanning,"Held stick retriggered start/stop");
                radar.HandleStick(Vector2.zero,false,.02f);radar.HandleStick(Vector2.left,false,.02f);check(!radar.scanning,"Return-to-centre failed to rearm");
                radar.HandleStick(Vector2.up,true,.02f);check(radar.targetTilt==0,"Arm clutch failed to block radar tilt");
                radar.HandleStick(Vector2.up,false,.02f);check(radar.targetTilt>0,"Vertical radar tilt input failed");
            }
            finally{Object.DestroyImmediate(root);Physics.SyncTransforms();}
            Directory.CreateDirectory("Logs/RadarReview");File.WriteAllText("Logs/RadarReview/math-physics-checks.txt","PASS "+checks+" Unity mathematical and Physics.Raycast assertions "+DateTime.Now.ToString("O"));
        }
    }
}
