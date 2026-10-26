using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace UnderwaterReconstruction
{
    public sealed class ReconstructionLab : MonoBehaviour
    {
        public GaussianSplatRenderer gaussian;
        public NerfVolumeRenderer nerf;
        public Transform rig;
        public Camera eye;
        public UniversalRenderPipelineAsset lightweightPipeline;
        public int targetFps = 90;
        public bool useNerf;
        public OfficialGaussianBackend official;
        public bool preferOfficial = true;
        public float moveSpeed = 1.5f;
        RenderPipelineAsset previousPipeline;
        int oldFps, oldVsync;
        float pitch, sum, nextSample, fps, cpu, gpu;
        int frames;
        readonly FrameTiming[] timing = new FrameTiming[1];
        string readout;
        void OnEnable()
        {
            previousPipeline = QualitySettings.renderPipeline;
            if (lightweightPipeline) QualitySettings.renderPipeline = lightweightPipeline;
            oldFps = Application.targetFrameRate; oldVsync = QualitySettings.vSyncCount;
            Application.targetFrameRate = targetFps; QualitySettings.vSyncCount = 0;
            Select(useNerf);
        }
        void OnDisable()
        {
            QualitySettings.renderPipeline = previousPipeline;
            Application.targetFrameRate = oldFps; QualitySettings.vSyncCount = oldVsync;
            Cursor.lockState = CursorLockMode.None;
        }
        public void Select(bool volume)
        {
            useNerf = volume;
            bool native = !volume && preferOfficial && official && official.HasAsset && OfficialGaussianBackend.SupportedApi;
            if (official) official.SetActive(native);
            if (gaussian) gaussian.gameObject.SetActive(!volume && !native);
            if (nerf) nerf.gameObject.SetActive(volume);
        }
        void Update()
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            Vector3 move = Vector3.zero;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) Select(false);
                if (keyboard.digit2Key.wasPressedThisFrame) Select(true);
                if (keyboard.digit3Key.wasPressedThisFrame) { preferOfficial = !preferOfficial; Select(false); }
                if (keyboard.rKey.wasPressedThisFrame) { rig.position = new Vector3(0,0,-4); rig.rotation = Quaternion.identity; }
                move = new Vector3((keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),
                    (keyboard.eKey.isPressed?1:0)-(keyboard.qKey.isPressed?1:0),
                    (keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0));
            }
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick)) move += new Vector3(stick.x,0,stick.y);
            if (right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 turn)) rig.Rotate(Vector3.up,turn.x*55*Time.unscaledDeltaTime);
            if (mouse != null && mouse.rightButton.isPressed && !XRSettings.isDeviceActive)
            {
                Vector2 d = mouse.delta.ReadValue()*0.12f;
                rig.Rotate(Vector3.up,d.x); pitch=Mathf.Clamp(pitch-d.y,-85,85);
                eye.transform.localRotation=Quaternion.Euler(pitch,0,0);
            }
            Vector3 forward=Vector3.ProjectOnPlane(eye.transform.forward,Vector3.up).normalized;
            move=Vector3.ClampMagnitude(move,1);
            rig.position += (forward*move.z+Vector3.Cross(Vector3.up,forward)*move.x+Vector3.up*move.y)*moveSpeed*Time.unscaledDeltaTime;
            FrameTimingManager.CaptureFrameTimings();sum+=Time.unscaledDeltaTime;frames++;
            if (Time.unscaledTime < nextSample) return;
            fps=frames/Mathf.Max(sum,0.001f);sum=0;frames=0;nextSample=Time.unscaledTime+0.5f;
            if (FrameTimingManager.GetLatestTimings(1,timing)>0) {cpu=(float)timing[0].cpuFrameTime;gpu=(float)timing[0].gpuFrameTime;}
            bool native=official && official.Active;
            bool demo=useNerf ? nerf.asset && nerf.asset.synthetic : native ? official.syntheticDemo : gaussian.asset && gaussian.asset.synthetic;
            string status=useNerf ? (nerf.asset?"Baked NeRF (view-averaged RGB)":"No NeRF asset") : native ? $"Official 3DGS / {official.Count:N0} splats" : (gaussian.asset?$"Compatible 3DGS / {gaussian.asset.Count:N0} splats":"No 3DGS asset");
            string api=SystemInfo.graphicsDeviceType.ToString();
            if(!OfficialGaussianBackend.SupportedApi)api+=" | Official renderer needs DX12/Vulkan";
            readout=$"RECONSTRUCTION LAB    {(demo?"SYNTHETIC DEMO":"IMPORTED DATA")}\n{status} | {api}\n{fps:F1} FPS | frame {1000/Mathf.Max(fps,1):F1} ms | CPU {cpu:F1} ms | GPU {gpu:F1} ms\n1: 3DGS   2: NeRF   3: switch 3DGS renderer\nWASD/QE: move   RMB: look   R: reset";
        }
        void OnGUI() { GUI.Box(new Rect(16,16,585,125),readout ?? "Loading reconstruction..."); }
    }
}
