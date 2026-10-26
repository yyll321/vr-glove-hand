using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterRigUpgrade
    {
        [MenuItem("Tools/Underwater/Research/7 Apply Head Tracking and Glove Arm Controls")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play before upgrading rig.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=UnderwaterResearchSceneBuilder.ScenePath)throw new Exception("Open the editable underwater scene first.");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/UnderwaterResearch-beforeRig-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            var motor=Object.FindObjectOfType<UnderwaterMotor>();
            var camera=motor.origin.Camera;
            var tracking=camera.GetComponent<UnderwaterHeadTracking>();if(!tracking)tracking=camera.gameObject.AddComponent<UnderwaterHeadTracking>();
            tracking.eye=camera;motor.headTracking=tracking;
            var previous=camera.GetComponent<TrackedPoseDriver>();
            if(previous)
            {
                // Keep the XR provider's pose driver active. Disabling it freezes
                // the PICO Live Preview camera even when the headset is connected.
                previous.enabled=true;
                previous.updateType=TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
                previous.trackingType=TrackedPoseDriver.TrackingType.RotationAndPosition;
            }
            tracking.preferTrackedPoseDriver=true;
            camera.ResetWorldToCameraMatrix();camera.ResetCullingMatrix();camera.ResetProjectionMatrix();camera.stereoTargetEye=StereoTargetEyeMask.Both;camera.useOcclusionCulling=false;
            var receiver=Object.FindObjectOfType<Network>();
            if(!receiver)receiver=new GameObject("HandDriver UDP - UdexReal port 5555").AddComponent<Network>();
            receiver.Port=5555;receiver.LogPackets=false;
            foreach(string name in new[]{"ROVp - Pool","ROVp - Seabed"})
            {
                var root=GameObject.Find(name);if(!root)throw new Exception("Missing "+name);
                var legacy=root.GetComponentInChildren<Pool_JXBControl>(true);if(!legacy)throw new Exception("Missing original JXB joint bindings in "+name);
                var serialized=new SerializedObject(legacy).FindProperty("Joints");
                var joints=Enumerable.Range(0,serialized.arraySize).Select(i=>(Transform)serialized.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
                if(joints.Length!=4 || joints.Any(t=>!t))throw new Exception("Invalid Y/Z/Z/Y joint chain in "+name);
                var arm=root.GetComponent<PicoArmPoseDriver>();if(!arm)arm=root.AddComponent<PicoArmPoseDriver>();
                arm.Motor=motor;arm.SeabedArea=name.EndsWith("Seabed");arm.TrackingOrigin=motor.origin.CameraFloorOffsetObject.transform;
                arm.Joints=joints;arm.ArmBase=joints[0].parent;arm.Wrist=joints[3];
                arm.PreviousControllers=root.GetComponentsInChildren<MonoBehaviour>(true).Where(b=>b is Pool_JXBControl || b is JXB_angles_input).Cast<Behaviour>().ToArray();
                foreach(var old in arm.PreviousControllers)old.enabled=false;
                foreach(var mapper in root.GetComponentsInChildren<Wa100UdexRealArticulationMapper>(true))
                {
                    mapper.Network=receiver;mapper.AutoFindNetwork=false;mapper.AutoBindJoints=true;mapper.enabled=true;mapper.IsNeutralCalibrated=false;
                    mapper.EnsureMappings();mapper.AutoBind();
                    var guard=mapper.GetComponent<Wa100VisibilityGuard>();if(!guard)guard=mapper.gameObject.AddComponent<Wa100VisibilityGuard>();
                    guard.FollowParent=true;guard.ForceDefaultLayer=false;guard.enabled=true;
                    foreach(var body in mapper.GetComponentsInChildren<ArticulationBody>(true))
                    {
                        var ancestor=body.transform.parent?body.transform.parent.GetComponentInParent<ArticulationBody>():null;
                        body.immovable=!ancestor;body.useGravity=false;
                    }
                    guard.Apply();EditorUtility.SetDirty(mapper);EditorUtility.SetDirty(guard);
                }
                EditorUtility.SetDirty(arm);
            }
            var presence=Object.FindObjectOfType<UnderwaterPresence>();if(presence)presence.gentleBodyDrift=false;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Audit();
        }
        [MenuItem("Tools/Underwater/Research/6 Diagnose Head Tracking and Arm")]
        public static void Audit()
        {
            Directory.CreateDirectory("Logs/UnderwaterRig");
            var report=new StringBuilder();
            report.AppendLine("UTC="+DateTime.UtcNow.ToString("O")+" Play="+EditorApplication.isPlaying);
            report.AppendLine("Loader="+XRGeneralSettings.Instance?.Manager?.activeLoader?.name+" XRActive="+XRSettings.isDeviceActive);
            var motor=Object.FindObjectOfType<UnderwaterMotor>();
            if(motor)
            {
                var camera=motor.origin.Camera;var pose=camera.GetComponent<TrackedPoseDriver>();
                report.AppendLine($"MotorXr={motor.XrActive} origin={motor.transform.position}/{motor.transform.eulerAngles} camera={camera.transform.localPosition}/{camera.transform.localEulerAngles} stereo={camera.stereoEnabled} target={camera.targetTexture} culling={camera.cullingMatrix}");
                if(pose)report.AppendLine($"Pose enabled={pose.enabled} update={pose.updateType} trackingType={pose.trackingType} ignoreState={pose.ignoreTrackingState} position={pose.positionInput.action?.enabled}/{pose.positionInput.action?.activeControl?.path} rotation={pose.rotationInput.action?.enabled}/{pose.rotationInput.action?.activeControl?.path} tracking={pose.trackingStateInput.action?.enabled}/{pose.trackingStateInput.action?.activeControl?.path}");
                var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
                head.TryGetFeatureValue(CommonUsages.devicePosition,out Vector3 p);head.TryGetFeatureValue(CommonUsages.deviceRotation,out Quaternion q);head.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked);
                report.AppendLine($"Legacy head={head.name} valid={head.isValid} tracked={tracked} pose={p}/{q.eulerAngles}");
            }
            foreach(var device in UnityEngine.InputSystem.InputSystem.devices.OfType<XRHMD>())
                report.AppendLine($"InputSystem head={device.displayName} tracked={device.isTracked.ReadValue()} state={device.trackingState.ReadValue()} device={device.devicePosition.ReadValue()}/{device.deviceRotation.ReadValue().eulerAngles} centerEye={device.centerEyePosition.ReadValue()}/{device.centerEyeRotation.ReadValue().eulerAngles}");
            foreach(var camera in Object.FindObjectsOfType<Camera>(true))report.AppendLine($"Camera {camera.name} enabled={camera.enabled} active={camera.gameObject.activeInHierarchy} stereo={camera.stereoTargetEye} parent={camera.transform.parent?.name}");
            foreach(var controller in Object.FindObjectsOfType<Pool_JXBControl>(true))
            {
                var joints=new SerializedObject(controller).FindProperty("Joints");
                report.AppendLine($"Arm {controller.name} enabled={controller.enabled} joints="+string.Join(",",Enumerable.Range(0,joints.arraySize).Select(i=>joints.GetArrayElementAtIndex(i).objectReferenceValue?.name)));
            }
            foreach(var hand in Object.FindObjectsOfType<Wa100UdexRealArticulationMapper>(true))report.AppendLine($"Hand {hand.name} enabled={hand.enabled} network={hand.Network} joints={hand.BoundJointCount}");
            if(motor && motor.headTracking)report.AppendLine($"Head bridge={motor.headTracking.Source} active={motor.headTracking.SessionActive} valid={motor.headTracking.PoseValid} applied={motor.headTracking.AppliedPoses} legacyFallback={motor.headTracking.LegacyFallbackPoses} usedFallback={motor.headTracking.UsedLegacyFallback} legacyAngleDelta={motor.headTracking.LegacyRotationDelta:F3} legacyPositionDelta={motor.headTracking.LegacyPositionDelta:F3} renderAngleError={motor.headTracking.RenderPoseAngleError}");
            File.WriteAllText("Logs/UnderwaterRig/audit.txt",report.ToString());
        }
    }
}
