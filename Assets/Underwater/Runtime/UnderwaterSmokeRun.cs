using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;

namespace UnderwaterBaseline
{
    // Opt-in player smoke test: no test components execute during normal acceptance sessions.
    public sealed class UnderwaterSmokeRun : MonoBehaviour
    {
        [Serializable] sealed class Result
        {
            public bool passed;
            public string[] checks;
            public float frameMs, cpuMs, gpuMs;
            public string device;
            public string limitation = "Desktop smoke test. PICO headset, streaming latency and 72 Hz device performance require hardware acceptance.";
        }
        readonly List<string> checks = new List<string>();
        string output;
        bool failed;

        void Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-underwater-smoke") < 0) return;
            output = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "UnderwaterSmoke");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "results.json"), "{\"passed\":false,\"status\":\"running\"}");
            Application.logMessageReceived += LogError;
            StartCoroutine(Run());
            StartCoroutine(Timeout());
        }
        IEnumerator Timeout()
        {
            yield return new WaitForSecondsRealtime(55);
            checks.Add("FAIL: Test did not complete before timeout");
            File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(new Result { passed = false, checks = checks.ToArray() }, true));
            Application.Quit(1);
        }

        void LogError(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            { failed = true; checks.Add("ERROR: " + condition); }
        }

        void Check(bool condition, string message)
        {
            checks.Add((condition ? "PASS: " : "FAIL: ") + message);
            if (!condition) failed = true;
        }

        IEnumerator Press(Keyboard keyboard, Key key, float seconds = 0.15f)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return new WaitForSeconds(seconds);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.25f);
        }
        void Axis(UnityEngine.InputSystem.InputDevice device, string control, Vector2 value)
        { InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.Vector2Control)device[control], value); }
        IEnumerator Button(UnityEngine.InputSystem.InputDevice device, string control)
        {
            InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.ButtonControl)device[control], 1f);
            yield return new WaitForSeconds(0.2f);
            InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.ButtonControl)device[control], 0f);
            yield return new WaitForSeconds(0.25f);
        }
        IEnumerator Run()
        {
            var motor = FindObjectOfType<UnderwaterMotor>();
            var environment = FindObjectOfType<UnderwaterEnvironment>();
            var performance = FindObjectOfType<UnderwaterPerformance>();
            yield return new WaitForSeconds(3);
            Check(!motor.XrActive, "Desktop fallback active");
            Check(motor.origin.Camera.transform.position.y < 10, "Spawn below 10 m waterline");
            var floor = GameObject.Find("Pool Floor - 25 x 50 m").GetComponent<Collider>().bounds;
            Check(Mathf.Abs(floor.size.x - 25) < 0.01f && Mathf.Abs(floor.size.z - 50) < 0.01f, "Pool interior 25 x 50 m");
            var rovA = GameObject.Find("ROVp - Pool"); var rovB = GameObject.Find("ROVp - Seabed");
            Check(rovA && rovB && rovA.GetComponentsInChildren<Transform>(true).Length == rovB.GetComponentsInChildren<Transform>(true).Length, "Both ROVp copies retain the same hierarchy");
            var bounds = rovA.GetComponentInChildren<Renderer>().bounds;
            foreach (var r in rovA.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(r.bounds);
            Check(Mathf.Abs(bounds.min.y - 0.025f) < 0.04f, "ROVp sits on pool bottom");
            Check(environment.ParticleCount > 50 && environment.ParticleCount <= 780, "Particles alive and bounded");
            Capture(motor.origin.Camera, "01-underwater.png");
            var keyboard = InputSystem.AddDevice<Keyboard>("Underwater Smoke Keyboard");
            Vector3 before = motor.transform.position;
            yield return Press(keyboard, Key.W, 1.5f);
            Check(motor.transform.position.z - before.z > 0.5f, "W input moves forward");
            Vector3 stopped = motor.transform.position;
            yield return new WaitForSeconds(0.5f);
            Check(Vector3.Distance(stopped, motor.transform.position) < 0.03f, "No unattended camera drift");
            float height = motor.transform.position.y;
            yield return Press(keyboard, Key.E, 0.9f);
            Check(motor.transform.position.y > height + 0.4f, "E input rises");
            height = motor.transform.position.y;
            yield return Press(keyboard, Key.Q, 0.6f);
            Check(motor.transform.position.y < height - 0.2f, "Q input dives");
            yield return Press(keyboard, Key.R);
            Check(Vector2.Distance(new Vector2(motor.transform.position.x, motor.transform.position.z), new Vector2(0, -8)) < 0.1f, "R resets via input");
            motor.origin.Camera.transform.localRotation = Quaternion.Euler(35, 0, 0);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.T));
            yield return new WaitForSeconds(0.2f);
            Check(motor.Aiming && motor.ValidTarget, "T hold produces a valid floor arc");
            int teleports = motor.TeleportCount;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.4f);
            Check(motor.TeleportCount > teleports, "T release confirms teleport");
            yield return Press(keyboard, Key.F);
            Check(motor.origin.Camera.transform.position.y > 10, "F reaches observation deck above water");
            motor.origin.RotateAroundCameraUsingOriginUp(-70);
            motor.origin.Camera.transform.localRotation = Quaternion.Euler(28, 0, 0);
            Capture(motor.origin.Camera, "02-above-water.png");
            yield return Press(keyboard, Key.F);
            yield return Press(keyboard, Key.Tab);
            Check(motor.InSeabed && motor.origin.Camera.transform.position.y < 10, "Tab switches to natural seabed");
            yield return new WaitForSeconds(1);
            Capture(motor.origin.Camera, "03-natural-seabed.png");
            yield return Press(keyboard, Key.Tab);
            Check(!motor.InSeabed, "Tab returns to pool");
            var marker = GameObject.Find("Grabbable Inspection Marker");
            Vector3 markerHome = marker.transform.position;
            marker.transform.position = motor.origin.Camera.transform.position + motor.origin.Camera.transform.forward * 1.2f;
            Physics.SyncTransforms();
            yield return Press(keyboard, Key.G);
            Check(marker.GetComponent<Rigidbody>().isKinematic && Vector3.Distance(marker.transform.position, motor.origin.Camera.transform.position) < 1, "G grabs the inspection marker through input");
            yield return Press(keyboard, Key.G);
            Check(!marker.GetComponent<Rigidbody>().isKinematic, "G releases the inspection marker through input");
            marker.transform.position = markerHome;
            yield return Press(keyboard, Key.Digit1);
            int low = 0; foreach (var ps in environment.snow) low += ps.main.maxParticles;
            Check(environment.quality == 0 && low <= 430, "1 selects lower particle budget");
            yield return Press(keyboard, Key.Digit3);
            Check(environment.quality == 2, "3 selects high quality");
            yield return Press(keyboard, Key.P);
            Check(!environment.snowEnabled, "P disables suspension particles");
            yield return Press(keyboard, Key.P);
            yield return Press(keyboard, Key.B);
            Check(environment.bubbles.particleCount > 0 && environment.bubbles.particleCount <= 48, "B emits bounded bubbles");
            // Feed controller events through the same Input System paths used by native PICO and Live Preview.
            InputSystem.RegisterLayout(@"{
  ""name"":""UnderwaterTestController"", ""extend"":""XRController"",
  ""controls"":[
    {""name"":""thumbstick"",""layout"":""Vector2""},
    {""name"":""primaryButton"",""layout"":""Button"",""format"":""FLT""},
    {""name"":""secondaryButton"",""layout"":""Button"",""format"":""FLT""},
    {""name"":""thumbstickClicked"",""layout"":""Button"",""format"":""FLT""}
  ]
}");
            var left = InputSystem.AddDevice("UnderwaterTestController"); var right = InputSystem.AddDevice("UnderwaterTestController");
            InputSystem.SetDeviceUsage(left, UnityEngine.InputSystem.CommonUsages.LeftHand);
            InputSystem.SetDeviceUsage(right, UnityEngine.InputSystem.CommonUsages.RightHand);
            before = motor.transform.position;
            Axis(left, "thumbstick", Vector2.up);
            yield return new WaitForSeconds(1.2f);
            Axis(left, "thumbstick", Vector2.zero);
            yield return new WaitForSeconds(0.2f);
            Check(motor.transform.position.z > before.z + 0.4f, "Left controller stick moves through action binding");
            before = motor.transform.position;
            yield return Press(keyboard, Key.D, 0.7f);
            Check(motor.transform.position.x > before.x + 0.25f, "Keyboard remains responsive with XR controllers connected");
            height = motor.transform.position.y;
            InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.ButtonControl)right["primaryButton"],1f);
            yield return new WaitForSeconds(0.8f);
            InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.ButtonControl)right["primaryButton"],0f);
            yield return new WaitForSeconds(0.1f);
            Check(motor.transform.position.y > height + 0.3f, "Right A rises through action binding");
            height = motor.transform.position.y;
            InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.ButtonControl)right["secondaryButton"],1f);
            yield return new WaitForSeconds(0.6f);
            InputSystem.QueueDeltaStateEvent((UnityEngine.InputSystem.Controls.ButtonControl)right["secondaryButton"],0f);
            yield return new WaitForSeconds(0.1f);
            Check(motor.transform.position.y < height - 0.2f, "Right B dives through action binding");
            yield return Button(left,"primaryButton");
            Check(motor.origin.Camera.transform.position.y > 10, "Left X surfaces through action binding");
            yield return Button(left,"primaryButton");
            yield return Button(right,"thumbstickClicked");
            Check(motor.InSeabed, "Right stick click switches reference areas");
            yield return Button(left,"thumbstickClicked");
            Check(environment.quality == 0, "Left stick click cycles quality");
            motor.origin.Camera.transform.localRotation = Quaternion.Euler(35,0,0);
            Axis(right,"thumbstick",Vector2.up);
            yield return new WaitForSeconds(0.2f);
            Check(motor.Aiming && motor.ValidTarget, "Right stick forward aims teleport");
            teleports = motor.TeleportCount;
            Axis(right,"thumbstick",Vector2.zero);
            yield return new WaitForSeconds(0.4f);
            Check(motor.TeleportCount > teleports,"Right stick release teleports");
            float yaw = motor.origin.Camera.transform.eulerAngles.y;
            Axis(right,"thumbstick",Vector2.right);
            yield return new WaitForSeconds(0.15f);
            Axis(right,"thumbstick",Vector2.zero);
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw,motor.origin.Camera.transform.eulerAngles.y)) > 25,"Right stick snap turns");
            yield return Button(left,"secondaryButton");
            Check(environment.bubbles.particleCount > 0,"Left Y emits bubbles");
            InputSystem.RemoveDevice(left); InputSystem.RemoveDevice(right); InputSystem.RemoveDevice(keyboard);
            var result = new Result { passed = !failed, checks = checks.ToArray(), frameMs = performance.FrameMs,
                cpuMs = performance.CpuMs, gpuMs = performance.GpuMs, device = SystemInfo.graphicsDeviceName };
            File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(result, true));
            Application.logMessageReceived -= LogError;
            Application.Quit(failed ? 1 : 0);
        }

        void Capture(Camera camera, string filename)
        {
            var target = new RenderTexture(1440, 900, 24, RenderTextureFormat.ARGB32);
            target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            var colors = pixels.GetPixels32();
            int lit = 0, magenta = 0;
            foreach (var color in colors)
            {
                if (color.r + color.g + color.b > 30) lit++;
                if (color.r > 210 && color.b > 210 && color.g < 30) magenta++;
            }
            Check(lit > colors.Length / 2, filename + " renders a visible scene");
            Check(magenta < colors.Length / 100, filename + " has no missing shader surfaces");
            File.WriteAllBytes(Path.Combine(output, filename), pixels.EncodeToPNG());
            RenderTexture.active = previous;
            target.Release(); Destroy(target); Destroy(pixels);
        }
    }
}
