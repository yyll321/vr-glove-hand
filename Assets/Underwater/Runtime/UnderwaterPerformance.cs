using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;
using UnityEngine.UI;
using Unity.XR.PXR;

namespace UnderwaterBaseline
{
    public sealed class UnderwaterPerformance : MonoBehaviour
    {
        public UnderwaterMotor motor;
        public UnderwaterEnvironment environment;
        public Text worldReadout;
        public PXR_Manager pico;
        public bool automatic = true;
        public int targetFps = 72;
        public float FrameMs { get; private set; }
        public float GpuMs { get; private set; }
        public float CpuMs { get; private set; }
        public float PreviewScale { get; private set; } = 1;
        public bool NativePico => Application.platform == RuntimePlatform.Android && motor.XrActive;
        readonly FrameTiming[] timings = new FrameTiming[1];
        UniversalRenderPipelineAsset original, runtime;
        float nextSample, nextAdjustment, timeSum;
        int frameCount;
        string readout = "Warming up...";
        public bool showHud = true;
        public UnderwaterSoundscape soundscape;
        public UnderwaterInteraction interaction;
        public float minimumPreviewScale=0.9f;
        public bool useTaskConsole;
        PicoArmPoseDriver[] arms;

        void Awake()
        {
            arms=FindObjectsOfType<PicoArmPoseDriver>();
            if (pico && Application.platform != RuntimePlatform.Android) pico.adaptiveResolution = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFps;
            Time.fixedDeltaTime = 1f / 72;
            original = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (original)
            {
                runtime = Instantiate(original);
                runtime.renderScale = 1;
                QualitySettings.renderPipeline = runtime;
            }
        }

        void Update()
        {
            if (worldReadout) worldReadout.transform.parent.gameObject.SetActive(motor.XrActive && showHud && !useTaskConsole);
            FrameTimingManager.CaptureFrameTimings();
            timeSum += Time.unscaledDeltaTime;
            frameCount++;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) SetQuality(0);
                if (keyboard.digit2Key.wasPressedThisFrame) SetQuality(1);
                if (keyboard.digit3Key.wasPressedThisFrame) SetQuality(2);
                if (keyboard.pKey.wasPressedThisFrame) { environment.snowEnabled = !environment.snowEnabled; environment.ApplyQuality(environment.quality); }
                if (keyboard.bKey.wasPressedThisFrame) environment.EmitBubbles();
                if (keyboard.hKey.wasPressedThisFrame) showHud = !showHud;
            }
            if (Time.unscaledTime < nextSample) return;
            nextSample = Time.unscaledTime + 0.5f;
            FrameMs = timeSum * 1000 / Mathf.Max(1, frameCount);
            timeSum = 0; frameCount = 0;
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                CpuMs = (float)timings[0].cpuFrameTime;
                GpuMs = (float)timings[0].gpuFrameTime;
            }
            float budget = 1000f / targetFps;
            if (automatic && Time.unscaledTime > nextAdjustment && Time.unscaledTime > 6)
            {
                nextAdjustment = Time.unscaledTime + 2;
                // Native PICO owns its adaptive resolution; URP renderScale must stay at one.
                if (NativePico) { if (runtime) runtime.renderScale = 1; PreviewScale = 1; }
                else if (runtime)
                {
                    float measured = GpuMs > 0 ? GpuMs : FrameMs;
                    if (measured > budget * 1.12f) PreviewScale = Mathf.Max(minimumPreviewScale, PreviewScale - 0.05f);
                    else if (measured < budget * 0.8f) PreviewScale = Mathf.Min(1, PreviewScale + 0.025f);
                    runtime.renderScale = PreviewScale;
                }
                if (FrameMs > budget * 1.25f && environment.quality > 0) environment.ApplyQuality(environment.quality - 1);
            }
            string loader = XRGeneralSettings.Instance?.Manager?.activeLoader?.name ?? "Desktop";
            readout = $"{(motor.InSeabed ? "NATURAL SEABED" : "TILED POOL")}  |  {(motor.origin.Camera.transform.position.y < 10 ? "SUBMERGED" : "ABOVE WATER")}\n" +
                $"{1000 / Mathf.Max(0.01f, FrameMs):F0} FPS  |  Frame {FrameMs:F1} / {budget:F1} ms\n" +
                $"CPU {(CpuMs > 0 ? CpuMs.ToString("F1") : "N/A")} ms  GPU {(GpuMs > 0 ? GpuMs.ToString("F1") : "N/A")} ms\n" +
                $"Particles {environment.ParticleCount}  |  Tier {environment.quality + 1}  |  Scale {(NativePico ? "PICO adaptive" : PreviewScale.ToString("F2"))}\n" +
                $"{loader}  |  Teleports {motor.TeleportCount}\n" +
                motor.Controllers.Status + "\n" + motor.LastInput + (soundscape?"\n"+soundscape.OutputStatus:"");
            if(arms!=null)foreach(var arm in arms)
            {
                if(arm.SeabedArea!=motor.InSeabed)continue;
                readout+=$"\nArm error {arm.RequestedPositionError*1000:F0} mm | orientation residual {arm.OrientationErrorDegrees:F0} deg (4 DOF)\n"+arm.Status;
                if(arm.Contact)readout+="\n"+arm.Contact.HapticStatus;
            }
            if(interaction)readout+=$"\nFlow {interaction.ListenerFlow:F2} m/s | Silt {interaction.ActivePlumes}/12 | Grains {interaction.sedimentParticles.particleCount}\nWake demo {(interaction.wakeDemo?"ON":"OFF")} [N / R B] | Lamps {(interaction.lightsEnabled?"ON":"OFF")} [L / R A]";
            if(motor.presence)foreach(var volume in motor.presence.fluidVolumes)
                if(volume && volume.Contains(motor.origin.Camera.transform.position))
                    readout+=$"\nFluid grid {volume.CellCount} cells | {volume.EffectiveHz:F0} Hz | CPU last step {volume.LastStepMilliseconds:F2} ms";
            if (worldReadout) worldReadout.text = readout;
        }

        void SetQuality(int tier) { automatic = false; environment.ApplyQuality(tier); }
        public void SelectQuality(int tier) { SetQuality(tier); }

        void OnGUI()
        {
            if(useTaskConsole)return;
            if (motor.XrActive) return;
            if (!showHud)
            {
                GUI.Label(new Rect(18, Screen.height - 30, 550, 26), $"{(motor.InSeabed ? "SEABED" : "POOL")}  | Tab compare | H controls | {1000 / Mathf.Max(1, FrameMs):F0} FPS");
                return;
            }
            GUILayout.BeginArea(new Rect(18, 18, 500, 510), GUI.skin.box);
            GUILayout.Label(readout);
            Vector2 area=UnderwaterRegionSettings.Size(motor.InSeabed);
            GUILayout.Label($"{area.x:F0} x {area.y:F0} m / 10 m deep\nWASD observer | RMB look | Q/E dive/rise\nArrow keys / R stick: move active ROV horizontally\nHold T aim, release teleport | F waterline | R reset\nTab / X compare | H hide panel | 1/2/3 quality\nPICO L stick: up/down rise/dive; left/right approach/retreat\nRight grip + hand translation: arm XYZ (wrist twist locked)\nRight stick hold-click aim / release teleport\nLeft grip: radar start/stop (no combinations)\nA lights | B wake | Y bubbles | L click quality");
            automatic = GUILayout.Toggle(automatic, "Automatic performance fallback");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Surface / Underwater [F]")) motor.ToggleSurface();
            if (GUILayout.Button("Bubbles [B]")) environment.EmitBubbles();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Switch Pool / Natural Seabed [Tab]")) motor.SwitchArea();
            if(soundscape)
            {
                GUILayout.Label("J / R trigger: sound test | M / L trigger: mute\nLive Preview audio: Windows output / PC headphones");
                if(GUILayout.Button("Play sound test [J]"))soundscape.TestSound();
            }
            GUILayout.EndArea();
        }

        void OnDestroy()
        {
            if (runtime) { QualitySettings.renderPipeline = original; Destroy(runtime); }
        }
    }
}
