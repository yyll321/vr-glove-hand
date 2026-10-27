using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TrackedPoseDriver=UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace UnderwaterBaseline
{
    [DefaultExecutionOrder(-100)]
    public sealed class UnderwaterHeadTracking : MonoBehaviour
    {
        public Camera eye;
        public bool SessionActive { get; private set; }
        public bool StereoFramesAvailable {get;private set;}
        public bool PoseValid { get; private set; }
        public int AppliedPoses { get; private set; }
        public int LegacyFallbackPoses { get; private set; }
        public float LegacyRotationDelta { get; private set; }
        public float LegacyPositionDelta { get; private set; }
        public bool UsedLegacyFallback { get; private set; }
        public string Source { get; private set; }="Waiting for HMD";
        public float RenderPoseAngleError { get; private set; }
        [Tooltip("Use the XR provider's TrackedPoseDriver. The legacy pose is only a diagnostic fallback when that component is absent.")]
        public bool preferTrackedPoseDriver=true;
        [Tooltip("Live Preview can expose a valid legacy XR head pose while its Input System trackingState stays zero. Use the legacy pose only when the pose driver is visibly stalled.")]
        public bool useLegacyFallbackWhenDriverStalled=true;
        [Min(0.05f)] public float fallbackAngleThreshold=0.5f;
        [Min(0.001f)] public float fallbackPositionThreshold=0.01f;
        readonly List<XRDisplaySubsystem> displays=new List<XRDisplaySubsystem>();
        TrackedPoseDriver previousDriver;
        bool previousEnabled;
        void Awake()
        {
            if(!eye)eye=GetComponent<Camera>();
            previousDriver=eye.GetComponent<TrackedPoseDriver>();
            if(previousDriver)
            {
                previousEnabled=previousDriver.enabled;
                // PICO Live Preview supplies its center-eye pose through the XR
                // pose driver. Disabling it makes the view appear frozen even though
                // the HMD reports a valid legacy XR device.
                if(preferTrackedPoseDriver)previousDriver.enabled=true;
            }
            eye.ResetWorldToCameraMatrix();eye.ResetCullingMatrix();eye.ResetProjectionMatrix();
            eye.stereoTargetEye=StereoTargetEyeMask.Both;
            eye.useOcclusionCulling=false;
        }
        void OnEnable(){Application.onBeforeRender+=BeforeRender;RenderPipelineManager.beginCameraRendering+=BeforeCamera;RenderPipelineManager.beginContextRendering+=BeforeContext;}
        void OnDisable(){Application.onBeforeRender-=BeforeRender;RenderPipelineManager.beginCameraRendering-=BeforeCamera;RenderPipelineManager.beginContextRendering-=BeforeContext;if(previousDriver)previousDriver.enabled=previousEnabled;}
        void BeforeContext(ScriptableRenderContext context,List<Camera> cameras)
        {
#if UNITY_EDITOR
            if(!cameras.Contains(eye))return;
            bool valid=false;displays.Clear();SubsystemManager.GetInstances(displays);
            foreach(var display in displays)if(display.running)
            {
                try
                {
                    int count=display.GetRenderPassCount();bool readable=count>0;
                    for(int i=0;i<count;i++){display.GetRenderPass(i,out var pass);readable&=pass.GetRenderParameterCount()>0;}
                    valid|=readable;
                }
                catch(System.IndexOutOfRangeException){/* Preview provider has not supplied this advertised pass yet. */}
            }
            StereoFramesAvailable=valid;eye.GetUniversalAdditionalCameraData().allowXRRendering=valid;
#endif
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(camera!=eye || !SessionActive)return;
            Sample();
            var rendered=eye.stereoEnabled?eye.GetStereoViewMatrix(Camera.StereoscopicEye.Left):eye.worldToCameraMatrix;
            Vector3 forward=-rendered.inverse.GetColumn(2);
            RenderPoseAngleError=Vector3.Angle(forward,eye.transform.forward);
        }
        void Update(){Sample();}
        [BeforeRenderOrder(100)] void BeforeRender(){Sample();}
        public void Sample()
        {
            displays.Clear();SubsystemManager.GetInstances(displays);
            SessionActive=XRSettings.isDeviceActive;
            StereoFramesAvailable=false;
            foreach(var display in displays)
            {
                SessionActive|=display.running;
                if(display.running && display.GetRenderPassCount()>0)StereoFramesAvailable=true;
            }
#if UNITY_EDITOR
            // Live Preview may report running with zero render passes while the HMD
            // sleeps/disconnects. URP 14 otherwise calls GetRenderPass(0) and throws.
            // Preserve tracking and restore stereo automatically when frames resume.
            var data=eye.GetUniversalAdditionalCameraData();if(data)data.allowXRRendering=StereoFramesAvailable;
#endif
            if(!SessionActive){PoseValid=false;return;}
            if(preferTrackedPoseDriver && previousDriver && previousDriver.enabled)
            {
                UsedLegacyFallback=false;
                PoseValid=true;Source="PICO XR TrackedPoseDriver (center eye)";
                // PICO Live Preview 3.4 can leave the Input System HMD at
                // trackingState=0 while the legacy XR device continues to carry
                // the real center-eye pose. Keep the provider as the primary
                // driver, but recover only when its camera has clearly stalled.
                if(useLegacyFallbackWhenDriverStalled && TryGetLegacyPose(out var fallbackPosition,out var fallbackRotation,out var fallbackName))
                {
                    LegacyRotationDelta=Quaternion.Angle(eye.transform.localRotation,fallbackRotation);
                    LegacyPositionDelta=Vector3.Distance(eye.transform.localPosition,fallbackPosition);
                    if(LegacyRotationDelta>fallbackAngleThreshold || LegacyPositionDelta>fallbackPositionThreshold)
                    {
                        if(ApplyPose(fallbackPosition,fallbackRotation))
                        {
                            UsedLegacyFallback=true;LegacyFallbackPoses++;
                            Source=fallbackName+" legacy fallback";
                        }
                    }
                }
                return;
            }
            // Prefer the same center-eye pose exposed by the active XR provider. An
            // unavailable legacy Head device must not disable a valid InputSystem HMD.
            if(TryGetLegacyPose(out var legacyPosition,out var legacyRotation,out var legacyName))
            {
                if(ApplyPose(legacyPosition,legacyRotation)){Source=legacyName+" XR pose";return;}
            }
            foreach(var input in InputSystem.devices)
            {
                if(!(input is XRHMD hmd) || !hmd.added)continue;
                bool tracked=hmd.isTracked.ReadValue()>0.5f;
                int state=hmd.trackingState.ReadValue();
                if(!tracked && (state&2)==0)continue;
                Vector3 p=hmd.centerEyePosition.ReadValue();Quaternion q=hmd.centerEyeRotation.ReadValue();
                if(!Finite(q)) {p=hmd.devicePosition.ReadValue();q=hmd.deviceRotation.ReadValue();}
                if(ApplyPose(p,q)){Source=hmd.displayName+" center eye";return;}
            }
            PoseValid=false;Source="HMD pose unavailable; last pose held";
        }
        bool TryGetLegacyPose(out Vector3 position,out Quaternion rotation,out string deviceName)
        {
            var head=InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if(!head.isValid)head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            position=eye.transform.localPosition;rotation=eye.transform.localRotation;deviceName=head.name;
            if(!head.isValid || !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation,out rotation) || !Finite(rotation))return false;
            if(head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition,out var measured) &&
               float.IsFinite(measured.x) && float.IsFinite(measured.y) && float.IsFinite(measured.z))position=measured;
            return true;
        }
        public bool ApplyPose(Vector3 position,Quaternion rotation)
        {
            if(!Finite(rotation) || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))return false;
            eye.transform.SetLocalPositionAndRotation(position,rotation.normalized);
            PoseValid=true;AppliedPoses++;return true;
        }
        static bool Finite(Quaternion q)
        {return float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)&&q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>0.5f;}
    }
}
