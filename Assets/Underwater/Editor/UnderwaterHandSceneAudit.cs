using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterHandSceneAudit
    {
        [Serializable]class AuditReport{public string time,scene,scope;public int objects,components,missingScripts,brokenReferences,renderers,articulations;public bool passed;public string[] checks,errors,warnings;}
        const string Key="UnderwaterHandSceneAudit";
        static UnderwaterHandSceneAudit(){EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)){SessionState.SetBool(Key,false);new GameObject("Full hand scene audit").AddComponent<UnderwaterHandAuditProbe>();}};}
        [MenuItem("Tools/Underwater/Research/38 Audit Full Scene and Hand Drives")]
        public static void Run(){Snapshot();if(EditorApplication.isPlayingOrWillChangePlaymode)return;UnderwaterPreviewCompatibility.Verify();SessionState.SetBool(Key,true);UnderwaterPhysicsTestMode.Enter();}
        public static void Snapshot()
        {
            Directory.CreateDirectory("Logs/HandAudit");var lines=new List<string>();int missing=0,broken=0,components=0;
            var checks=new List<string>();var errors=new List<string>();var warnings=new List<string>();
            void Check(bool ok,string text){(ok?checks:errors).Add(text);}
            var scene=SceneManager.GetActiveScene();lines.Add("Scene="+scene.path+" playing="+Application.isPlaying+" time="+DateTime.Now.ToString("O"));
            var objects=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).ToArray();
            foreach(var go in objects)
            {
                missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                foreach(var c in go.GetComponents<Component>())if(c)
                {
                    components++;
                    var so=new SerializedObject(c);var p=so.GetIterator();
                    while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference && !p.objectReferenceValue && p.objectReferenceInstanceIDValue!=0)
                    {broken++;lines.Add("BROKEN "+Path(go.transform)+" "+c.GetType().Name+"."+p.propertyPath);}
                }
            }
            lines.Add("missingScripts="+missing+" brokenSerializedReferences="+broken);
            Check(missing==0,"All scene objects, including inactive objects, have no missing scripts");
            Check(broken==0,"No broken serialized component/asset references");
            Check(objects.All(g=>float.IsFinite(g.transform.position.sqrMagnitude) && float.IsFinite(g.transform.rotation.x) && float.IsFinite(g.transform.lossyScale.sqrMagnitude)),"All scene transform positions, rotations and scales are finite");
            var bodies=objects.Select(g=>g.GetComponent<ArticulationBody>()).Where(b=>b).ToArray();
            foreach(var b in bodies)
            {
                Check(b.mass>0 && float.IsFinite(b.mass),Path(b.transform)+": positive finite articulation mass");
                if(AnchorGap(b)>.001f)warnings.Add(Path(b.transform)+": articulation anchor separation "+AnchorGap(b)+" m");
            }
            var renderers=objects.SelectMany(g=>g.GetComponents<Renderer>()).ToArray();
            var materials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
            foreach(var mat in materials)Check(mat.shader && !ShaderUtil.ShaderHasError(mat.shader),"Material shader compiles: "+mat.name);
            foreach(var map in Object.FindObjectsOfType<Wa100UdexRealArticulationMapper>(true))
            {
                var hold=map.GetComponent<Wa100JointPoseHold>();var net=map.Network;
                Check(map.enabled && map.MatchHandDriverAngles && map.JointMappings.Length==12 && map.JointMappings.All(m=>m.Joint && m.Joint.transform.IsChildOf(map.transform)),Path(map.transform)+": 12 local signed mappings enabled and bound");
                Check(map.HandDriverFlexionSign==-1 && map.JointMappings.Where(m=>m.CountsForClosure).All(m=>m.PrimaryScale== (m.JointName=="Thumb_J1_Pitch"?-.6f:m.JointName=="Thumb_J3"?1:-1)),Path(map.transform)+": actual vendor negative-flexion convention configured");
                Check(hold && hold.enabled && net && net.enabled && net.Port==5555,Path(map.transform)+": hold/contact pipeline and live port 5555 enabled");
                Check(map.JointMappings.Take(2).All(m=>m.Joint.jointType==ArticulationJointType.RevoluteJoint),Path(map.transform)+": serial thumb hinges retained");
                var writers=map.GetComponentsInChildren<MonoBehaviour>(true).Where(b=>b && b.enabled && (b.GetType().Name=="JointControl"||b.GetType().Name=="Controller"||b.GetType().Name=="HandDriver")).ToArray();
                Check(writers.Length==0,Path(map.transform)+": no competing legacy joint/transform writers enabled");
                lines.Add("HAND "+Path(map.transform)+" enabled="+map.enabled+" fresh="+map.HasFreshFrame+" role="+map.ResolvedCharacterName+" status="+map.LastStatus+" network="+(net?net.name:"NULL")+" packets="+(net?net.ReceivedPacketCount:0)+" lastRole="+(net?net.LastRoleName:"")+" holdFresh="+(hold&&hold.FreshCommand)+" blocked="+(hold?hold.BlockedCommands:0));
                foreach(var m in map.JointMappings)
                {
                    var b=m.Joint;if(!b){lines.Add("MISSING JOINT "+m.LinkName);continue;}
                    Vector3 axis=b.transform.TransformDirection(b.anchorRotation*Wa100UdexRealArticulationMapper.AxisVector(m));
                    lines.Add(m.JointName+" link="+Path(b.transform)+" raw="+(net?net.Convert2Angle(map.ResolvedCharacterName,m.PrimaryKey):0)+" key="+m.PrimaryKey+" scale="+m.PrimaryScale+" requested="+m.RequestedTarget+" smoothed="+m.CurrentTarget+" accepted="+(hold?hold.Accepted(b):0)+" actual="+(Application.isPlaying?Wa100JointPoseHold.ReadAngle(m):0)+" axis="+axis.ToString("F3")+" parentAnchorGap="+AnchorGap(b));
                }
            }
            foreach(var n in Object.FindObjectsOfType<Network>(true))lines.Add("NETWORK "+Path(n.transform)+" port="+n.Port+" enabled="+n.enabled+" packets="+n.ReceivedPacketCount+" endpoint="+n.LastEndpoint);
            foreach(var camera in Object.FindObjectsOfType<Camera>(true))lines.Add("CAMERA "+Path(camera.transform)+" active="+camera.isActiveAndEnabled+" target="+(camera.targetTexture?camera.targetTexture.name:"display")+" stereo="+camera.stereoTargetEye);
            var tasks=Object.FindObjectsOfType<UnderwaterGraspTask>(true);Check(tasks.Length==2,"Pool and seabed both have a grasp task");
            foreach(var task in tasks)
            {
                Check(task.targets!=null && task.targets.Length==2 && task.targets.All(t=>t && t.GetComponent<Collider>() && t.GetComponent<Rigidbody>()),task.name+": exactly two physical targets bound");
                var arm=task.arm;
                Check(arm && arm.Joints.Length==4 && arm.Joints.All(j=>j) && arm.Wrist && arm.TrackingOrigin && arm.MasterSlave && arm.Contact && arm.WholeArmContact,task.name+": arm/IK/master-slave/contact bindings complete");
                Check(arm && !arm.EnableControllerWristRotation,task.name+": controller wrist twist remains disabled");
                var bench=task.GetComponentInChildren<UnderwaterGraspBench>();
                Check(bench && bench.sand && bench.deformable && bench.deformable.SurfaceCollider,task.name+": deformable sand/support bindings complete");
                if(Application.isPlaying && bench)
                    Check(arm.Contact.Hand.GetComponentsInChildren<Collider>().Where(c=>c.enabled).All(c=>Physics.GetIgnoreCollision(c,bench.sand) && Physics.GetIgnoreCollision(c,bench.deformable.SurfaceCollider)),task.name+": hand filters both visual-volume and deformable support sand");
            }
            if(Application.isPlaying)
            {
                Check(Object.FindObjectsOfType<AudioListener>().Count(l=>l.isActiveAndEnabled)==1,"Exactly one active audio listener");
                var main=Camera.main;Check(main && main.isActiveAndEnabled && main.stereoTargetEye==StereoTargetEyeMask.Both && !main.targetTexture,"Main head camera renders both eyes to display");
            }
            InputChecks(Check);
            var invalidScan=new UnderwaterGeometryRadar.Snapshot{columns=121,rows=9,ranges=Array.Empty<float>(),hits=Array.Empty<bool>(),points=Array.Empty<Vector3>()};
            Check(!invalidScan.Complete,"Radar rejects empty arrays with nonzero dimensions");
            var validScan=new UnderwaterGeometryRadar.Snapshot{columns=3,rows=1,ranges=new float[3],hits=new bool[3],points=new Vector3[3]};
            Check(validScan.Complete,"Radar accepts a complete frame with matching arrays");
            File.WriteAllText("Logs/HandAudit/"+(Application.isPlaying?"runtime":"scene")+"-report.json",JsonUtility.ToJson(new AuditReport{time=DateTime.Now.ToString("O"),scene=scene.path,scope="All current scene objects/references/material shaders, arm/hand/sand bindings and input state transitions; not a hardware or exhaustive code-proof audit.",objects=objects.Length,components=components,missingScripts=missing,brokenReferences=broken,renderers=renderers.Length,articulations=bodies.Length,passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray(),warnings=warnings.ToArray()},true));
            File.WriteAllLines("Logs/HandAudit/"+(Application.isPlaying?"runtime":"scene")+".txt",lines);
        }
        static void InputChecks(Action<bool,string> check)
        {
            using(var input=new UnderwaterControllerInput())
            {
                input.ApplyState(default);input.ApplyState(new UnderwaterControllerInput.State{rightGrip=true});
                check(input.RightGrip && !input.RadarPressed && !input.PanelPressed,"Right Grip controls arm independently");
                input.ApplyState(default);input.ApplyState(new UnderwaterControllerInput.State{leftGrip=true});input.ApplyState(default);
                check(input.RadarPressed && !input.PanelPressed,"Left Grip release only toggles radar");
                input.ApplyState(new UnderwaterControllerInput.State{leftGrip=true});input.ApplyState(new UnderwaterControllerInput.State{leftGrip=true,y=true});
                check(input.PanelPressed && !input.RadarPressed && !input.BubblePressed,"Y + left Grip toggles panel without radar/bubble action");
                input.ApplyState(default);check(!input.RadarPressed && !input.BubblePressed,"Releasing panel chord does not fire single-button actions");
                input.ApplyState(new UnderwaterControllerInput.State{x=true});check(input.AreaPressed,"X switches area");
                check(UnderwaterControllerInput.MapMovement(Vector2.up)==Vector3.up && UnderwaterControllerInput.MapMovement(Vector2.down)==Vector3.down && UnderwaterControllerInput.MapMovement(Vector2.left)==Vector3.forward && UnderwaterControllerInput.MapMovement(Vector2.right)==Vector3.back,"Left stick retains rise/descend/approach/retreat mapping");
            }
        }
        [MenuItem("Tools/Underwater/Research/39 Repair Signed Glove Flexion")]
        public static void Repair()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play before saving signed mappings");
            var scene=SceneManager.GetActiveScene();if(scene.path!=UnderwaterRadarSurveyTools.ScenePath)throw new Exception("Open editable survey scene");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/Survey-beforeSignedFlexion-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            foreach(var map in Object.FindObjectsOfType<Wa100UdexRealArticulationMapper>(true))
            {
                map.HandDriverFlexionSign=-1;map.UseHandDriverMappings();EditorUtility.SetDirty(map);
                // Saved finger-root positions were manually moved without their
                // articulation parent anchors. Keep the visible placement and
                // bring the physical anchor to it, rather than snapping on Play.
                foreach(var b in map.GetComponentsInChildren<ArticulationBody>(true))
                {
                    if(AnchorGap(b)<.001f)continue;
                    var parent=b.transform.parent.GetComponentInParent<ArticulationBody>();if(!parent)continue;
                    b.matchAnchors=false;b.parentAnchorPosition=parent.transform.InverseTransformPoint(b.transform.TransformPoint(b.anchorPosition));
                    EditorUtility.SetDirty(b);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Snapshot();
        }
        public static float AnchorGap(ArticulationBody b)
        {var parent=b.transform.parent?b.transform.parent.GetComponentInParent<ArticulationBody>():null;return parent?Vector3.Distance(b.transform.TransformPoint(b.anchorPosition),parent.transform.TransformPoint(b.parentAnchorPosition)):0;}
        public static string Path(Transform t)=>t.parent?Path(t.parent)+"/"+t.name:t.name;
    }
    public sealed class UnderwaterHandAuditProbe:MonoBehaviour
    {
        readonly List<string> runtimeErrors=new List<string>();
        void OnEnable(){Application.logMessageReceived+=Log;}
        void OnDisable(){Application.logMessageReceived-=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error || type==LogType.Exception)runtimeErrors.Add(message+"\n"+stack);}
        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(3);
            // Simulate loss of the nonserialized cache on Editor domain reload.
            // The next real Update must rebuild it without a NullReference.
            var presence=Object.FindObjectOfType<UnderwaterPresence>();
            var field=typeof(UnderwaterPresence).GetField("particles",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            if(presence)field.SetValue(presence,null);
            yield return new WaitForSecondsRealtime(.3f);
            if(presence && field.GetValue(presence)==null)runtimeErrors.Add("Particle buffers not recreated after simulated reload");
            for(int i=0;i<10;i++){UnderwaterHandSceneAudit.Snapshot();File.Copy("Logs/HandAudit/runtime.txt","Logs/HandAudit/live-"+i+".txt",true);yield return new WaitForSecondsRealtime(1);}
            File.WriteAllText("Logs/HandAudit/runtime-errors.json",JsonUtility.ToJson(new ErrorReport{time=DateTime.Now.ToString("O"),passed=runtimeErrors.Count==0,errors=runtimeErrors.ToArray()},true));
            // Initial live-input observation does not inject data or alter fingers.
            EditorApplication.isPlaying=false;
        }
        [Serializable]class ErrorReport{public string time;public bool passed;public string[] errors;}
    }
}
