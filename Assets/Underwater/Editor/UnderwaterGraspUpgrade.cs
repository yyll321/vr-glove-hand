using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterGraspUpgrade
    {
        const string Root="Assets/Underwater/MarineAssets/";
        [MenuItem("Tools/Underwater/Research/23 Add Compact Cameras and Marine Grasp Task")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!=UnderwaterRadarSurveyTools.ScenePath)throw new Exception("Open survey scene outside Play");
            Directory.CreateDirectory("Assets/Underwater/Backups");EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),"Assets/Underwater/Backups/Survey-beforeGrasp-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            foreach(var name in new[]{"Underwater_TaskConsole","Marine Grasp Targets"}){var old=GameObject.Find(name);if(old)Object.DestroyImmediate(old);}
            var motor=Object.FindObjectOfType<UnderwaterMotor>();var arms=Object.FindObjectsOfType<PicoArmPoseDriver>().OrderBy(a=>a.SeabedArea).ToArray();
            var root=new GameObject("Underwater_TaskConsole");var console=root.AddComponent<UnderwaterTaskConsole>();console.motor=motor;console.arms=arms;
            console.radars=Object.FindObjectsOfType<UnderwaterGeometryRadar>();foreach(var r in console.radars){r.useTaskConsole=true;r.detectionMask=(1<<8)|(1<<10);EditorUtility.SetDirty(r);}
            var performance=Object.FindObjectOfType<UnderwaterPerformance>();performance.showHud=false;performance.useTaskConsole=true;EditorUtility.SetDirty(performance);
            var targetsRoot=new GameObject("Marine Grasp Targets");var tasks=new System.Collections.Generic.List<UnderwaterGraspTask>();var cameras=new System.Collections.Generic.List<Camera>();
            var creatures=new[]{"Starfish","Squid"};
            for(int region=0;region<arms.Length;region++)
            {
                var arm=arms[region];var guard=arm.GetComponent<UnderwaterArmSurfaceGuard>()??arm.gameObject.AddComponent<UnderwaterArmSurfaceGuard>();
                guard.arm=arm;guard.protectedRoot=arm.Joints[0];guard.Rebuild();arm.WholeArmContact=guard;
                EditorUtility.SetDirty(arm);EditorUtility.SetDirty(guard);
                var taskGo=new GameObject((arm.SeabedArea?"Seabed":"Pool")+" Grasp Task");taskGo.transform.SetParent(targetsRoot.transform);
                var task=taskGo.AddComponent<UnderwaterGraspTask>();tasks.Add(task);task.arm=arm;task.glove=arm.GetComponentInChildren<Wa100UdexRealArticulationMapper>();
                var hand=arm.Contact.Hand;
                var palm=hand.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="base_link_3");
                task.palmLocal=hand.transform.InverseTransformPoint(palm.transform.TransformPoint(palm.sharedMesh.bounds.center));
                task.Thumb=hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Thumb_L3");
                task.Fingers=hand.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Index_Finger_L2" || t.name=="Middle_Finger_L2").ToArray();
                var ground=GameObject.Find(arm.SeabedArea?"Sand Terrain - Teleportation Area":"Pool Floor - 25 x 50 m").GetComponent<Collider>();
                var targetList=new System.Collections.Generic.List<UnderwaterGraspTarget>();
                for(int i=0;i<5;i++)
                {
                    string type=creatures[i%2];var model=AssetDatabase.LoadAssetAtPath<GameObject>(Root+type+".obj");if(!model)throw new Exception("Model import missing: "+type);
                    var source=model.GetComponentInChildren<MeshFilter>();var mesh=source.sharedMesh;
                    var material=AssetDatabase.LoadAssetAtPath<Material>(Root+type+".mat");
                    if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=type+" Marine"};AssetDatabase.CreateAsset(material,Root+type+".mat");}
                    material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+type+".png"));material.SetFloat("_Smoothness",.22f);material.SetFloat("_Metallic",0);EditorUtility.SetDirty(material);
                    var go=new GameObject((arm.SeabedArea?"Sea":"Pool")+" "+type+" "+(i+1));go.layer=10;go.transform.SetParent(taskGo.transform);
                    var visual=new GameObject("Visual",typeof(MeshFilter),typeof(MeshRenderer));visual.layer=10;visual.transform.SetParent(go.transform,false);
                    visual.GetComponent<MeshFilter>().sharedMesh=mesh;visual.GetComponent<MeshRenderer>().sharedMaterial=material;
                    var shape=go.AddComponent<BoxCollider>();shape.center=mesh.bounds.center;shape.size=mesh.bounds.size+new Vector3(.003f,.008f,.003f);
                    var body=go.AddComponent<Rigidbody>();body.mass=.08f;body.drag=4;body.angularDrag=8;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                    var target=go.AddComponent<UnderwaterGraspTarget>();target.targetId=go.name;target.seabed=arm.SeabedArea;targetList.Add(target);
                    var offsets=new[]{new Vector2(-.24f,0),new Vector2(.07f,.08f),new Vector2(.32f,.10f),new Vector2(-.32f,-.25f),new Vector2(.23f,-.25f)};
                    Vector3 p=task.PalmPoint+new Vector3(offsets[i].x,0,offsets[i].y);float floor=ground.Raycast(new Ray(new Vector3(p.x,11,p.z),Vector3.down),out var hit,20)?hit.point.y:0;
                    p.y=floor-mesh.bounds.min.y+.012f;go.transform.position=p;go.transform.rotation=Quaternion.Euler(0,i*51,0);
                }
                task.targets=targetList.ToArray();
                var radar=console.radars.First(r=>r.seabed==arm.SeabedArea);radar.graspWrist=arm.Wrist;
                radar.graspPalmLocal=arm.Wrist.InverseTransformPoint(task.PalmPoint);EditorUtility.SetDirty(radar);
                if(!radar.profiles.Any(p=>p.name=="Grasp close"))radar.profiles=radar.profiles.Concat(new[]{new UnderwaterGeometryRadar.Profile("Grasp close",2,90,60,81,21)}).ToArray();
                var zone=GameObject.CreatePrimitive(PrimitiveType.Cylinder);zone.name="Delivery Marker - release inside";zone.transform.SetParent(taskGo.transform);Object.DestroyImmediate(zone.GetComponent<Collider>());
                Vector3 zoneP=task.PalmPoint+new Vector3(.65f,0,.35f);float zoneY=ground.Raycast(new Ray(new Vector3(zoneP.x,11,zoneP.z),Vector3.down),out var groundHit,20)?groundHit.point.y:0;
                zone.transform.position=new Vector3(zoneP.x,zoneY+.015f,zoneP.z);zone.transform.localScale=new Vector3(.55f,.008f,.55f);
                zone.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Underwater/Materials/TeleportValid.mat")??AssetDatabase.LoadAssetAtPath<Material>("Assets/Underwater/RadarSurvey/ReefTone2.mat");task.deliveryZone=zone.transform;
                for(int view=0;view<3;view++)
                {
                    var cameraGo=new GameObject((arm.SeabedArea?"Sea":"Pool")+" Camera - "+UnderwaterTaskConsole.ViewNames[view]);cameraGo.transform.SetParent(root.transform);
                    var camera=cameraGo.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=view==0?75:55;camera.nearClipPlane=.015f;camera.farClipPlane=50;
                    camera.aspect=16f/9;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.cullingMask=(1<<0)|(1<<8)|(1<<9)|(1<<10);camera.allowHDR=false;camera.allowMSAA=false;
                    var data=camera.GetUniversalAdditionalCameraData();data.allowXRRendering=false;data.renderPostProcessing=false;data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.On;
                    cameras.Add(camera);
                }
                EditorUtility.SetDirty(task);
            }
            console.tasks=tasks.ToArray();console.views=cameras.ToArray();EditorUtility.SetDirty(console);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
            Debug.Log("Saved compact task console, 6 selectable cameras, 10 marine targets and whole-arm guards");
        }
    }
}
