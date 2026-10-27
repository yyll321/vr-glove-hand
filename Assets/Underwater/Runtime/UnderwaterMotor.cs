using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Management;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace UnderwaterBaseline
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class UnderwaterMotor : MonoBehaviour
    {
        public XROrigin origin;
        public TeleportationProvider teleportation;
        public Transform rightController;
        public LineRenderer teleportLine;
        public Transform teleportMarker;
        public UnderwaterEnvironment environment;
        public UnderwaterPresence presence;
        public UnderwaterHeadTracking headTracking;
        public LayerMask environmentMask = 1 << 8;
        public float speed = 1.35f;
        public float acceleration = 2.8f;
        public float quadraticDrag = 1.1f;
        public bool XrActive { get; private set; }
        public bool Aiming { get; private set; }
        public bool ValidTarget { get; private set; }
        public Vector3 Target { get; private set; }
        public int TeleportCount { get; private set; }
        public static readonly Vector3 PoolSpawn = new Vector3(0, 0.1f, -8);
        public static readonly Vector3 SeabedSpawn = new Vector3(62, 0.25f, -8);
        public bool InSeabed => origin.Camera.transform.position.x > 31;
        public string LastInput { get; private set; } = "Ready";
        public UnderwaterControllerInput Controllers { get; private set; }
        bool previousCompare, previousQuality, keyboardAim;
        XRGrabInteractable desktopHeld;
        Rigidbody body;
        CapsuleCollider capsule;
        TrackedPoseDriver pose;
        Vector3 input;
        float pitch;
        bool previousAim;
        bool previousSurfaceButton, previousBubbleButton;
        readonly Vector3[] arc = new Vector3[40];

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            pose = origin.Camera.GetComponent<TrackedPoseDriver>();
            headTracking=origin.Camera.GetComponent<UnderwaterHeadTracking>();
            Controllers = new UnderwaterControllerInput();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            body.mass = 80;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        void OnEnable() { if (teleportation) teleportation.endLocomotion += OnTeleported; }
        void OnDisable() { if (teleportation) teleportation.endLocomotion -= OnTeleported; }
        void OnTeleported(LocomotionSystem _) { body.velocity = Vector3.zero; Physics.SyncTransforms(); }

        void Update()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            XrActive = headTracking ? headTracking.SessionActive : head.isValid && XRSettings.isDeviceActive;
            // UnderwaterHeadTracking leaves the provider-owned TPD enabled for PICO;
            // only desktop/no-bridge mode is toggled here.
            if (pose && !headTracking) pose.enabled = XrActive;
            Controllers.Read();
            input = Vector3.zero;
            var move = Controllers.Move; var turn = Controllers.Turn;
            if (move.sqrMagnitude > 0.03f || turn.sqrMagnitude > 0.03f || Controllers.Up || Controllers.Down)
                LastInput = "Controller input received";
            input = UnderwaterControllerInput.MapMovement(move);
            if (Controllers.Surface && !previousSurfaceButton) ToggleSurface();
            if (!Controllers.RadarModifier && Controllers.Bubbles && !previousBubbleButton && environment) environment.EmitBubbles();
            if (Controllers.Compare && !previousCompare) SwitchArea();
            if (!Controllers.RadarModifier && Controllers.Quality && !previousQuality) FindObjectOfType<UnderwaterPerformance>().SelectQuality((environment.quality + 1) % 3);
            previousSurfaceButton = Controllers.Surface; previousBubbleButton = Controllers.Bubbles;
            previousCompare = Controllers.Compare; previousQuality = Controllers.Quality;
            // Right controller pose belongs to the arm; it must not rotate the world.
            Aiming = Controllers.TeleportHeld && !Controllers.RightGrip;
            // Always read desktop input, even when a preview loader reports an attached XR device.
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            keyboardAim = keyboard != null && keyboard.tKey.isPressed;
            if (keyboard != null)
            {
                var desktop = new Vector3((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                if (desktop != Vector3.zero) { input = desktop; LastInput = "Keyboard movement received"; }
                Aiming |= keyboardAim;
                if (keyboard.fKey.wasPressedThisFrame) { ToggleSurface(); LastInput = "F / surface"; }
                if (keyboard.rKey.wasPressedThisFrame) { ResetView(); LastInput = "R / reset"; }
                if (keyboard.tabKey.wasPressedThisFrame) { SwitchArea(); LastInput = "Tab / compare"; }
                if (keyboard.gKey.wasPressedThisFrame && !XrActive) ToggleDesktopGrab();
            }
            if (mouse != null && mouse.rightButton.isPressed && !XrActive)
            {
                var delta = mouse.delta.ReadValue() * 0.12f;
                Rotate(delta.x);
                pitch = Mathf.Clamp(pitch - delta.y, -75, 75);
                origin.Camera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            if (desktopHeld) desktopHeld.transform.position = origin.Camera.transform.position + origin.Camera.transform.forward * 0.75f;
            if (input.magnitude < 0.18f) input = Vector3.zero;
            input = Vector3.ClampMagnitude(input, 1);
            if (Aiming) { input = Vector3.zero; UpdateTeleportArc(); }
            if (!Aiming && previousAim && ValidTarget) TeleportTo(Target);
            teleportLine.enabled = Aiming;
            teleportMarker.gameObject.SetActive(Aiming && ValidTarget);
            previousAim = Aiming;
        }

        void FixedUpdate()
        {
            // Equivalent volume m/rho gives exactly neutral buoyancy. No unattended rise or sink.
            body.AddForce(-Physics.gravity * body.mass, ForceMode.Force);
            var forward = Vector3.ProjectOnPlane(origin.Camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = transform.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var desired = (forward * input.z + right * input.x + Vector3.up * input.y) * speed;
            var current=presence?presence.BodyCurrent:Vector3.zero;
            desired+=current;
            if (input == Vector3.zero && current==Vector3.zero) body.velocity = Vector3.zero;
            else
            {
                // Acceleration plus v|v| resistance; never drive the tracked camera directly.
                var velocity = body.velocity;
                velocity += (desired - velocity) * (acceleration * Time.fixedDeltaTime);
                velocity /= 1 + quadraticDrag * velocity.magnitude * Time.fixedDeltaTime;
                body.velocity = velocity;
            }
            // Capsule follows head position so room-scale head offsets cannot walk through walls.
            var localHead = transform.InverseTransformPoint(origin.Camera.transform.position);
            capsule.height = Mathf.Clamp(localHead.y + 0.05f, 1.0f, 2.2f);
            capsule.center = new Vector3(localHead.x, capsule.height * 0.5f + 0.04f, localHead.z);
        }

        void UpdateTeleportArc()
        {
            var aim = XrActive && !keyboardAim ? rightController : origin.Camera.transform;
            Vector3 start = aim.position;
            Vector3 velocity = aim.forward * 8;
            ValidTarget = false;
            int count = 1;
            arc[0] = start;
            for (int i = 1; i < arc.Length; i++)
            {
                float t = i * 0.065f;
                Vector3 point = start + velocity * t + Vector3.down * (4.9f * t * t);
                Vector3 segment = point - arc[i - 1];
                if (Physics.Raycast(arc[i - 1], segment.normalized, out RaycastHit hit, segment.magnitude,
                    environmentMask, QueryTriggerInteraction.Ignore))
                {
                    arc[count++] = hit.point;
                    ValidTarget = hit.normal.y > 0.85f && hit.collider.GetComponent<TeleportationArea>() != null
                        && !Physics.CheckCapsule(hit.point + Vector3.up * 0.38f,
                            hit.point + Vector3.up * 1.6f, 0.27f, environmentMask, QueryTriggerInteraction.Ignore);
                    Target = hit.point;
                    break;
                }
                arc[count++] = point;
            }
            teleportLine.positionCount = count;
            for (int i = 0; i < count; i++) teleportLine.SetPosition(i, arc[i]);
            teleportLine.startColor = teleportLine.endColor = ValidTarget ? new Color(0.3f, 1, 0.75f) : new Color(1, 0.35f, 0.2f);
            if (ValidTarget) teleportMarker.position = Target + Vector3.up * 0.015f;
        }

        public void TeleportTo(Vector3 point)
        {
            body.velocity = Vector3.zero;
            var request = new TeleportRequest { destinationPosition = point,
                destinationRotation = transform.rotation, matchOrientation = MatchOrientation.WorldSpaceUp };
            if (teleportation.QueueTeleportRequest(request)) TeleportCount++;
        }

        public void ToggleSurface()
        {
            Vector2 seaSize=UnderwaterRegionSettings.SeaSize;
            TeleportTo(origin.Camera.transform.position.y < 10 ? (InSeabed ? new Vector3(62+seaSize.x/2+3.5f,10.05f,-seaSize.y/2-3.5f) : new Vector3(15,10.05f,-8)) : InSeabed ? SeabedSpawn : PoolSpawn);
        }
        public void SwitchArea() { TeleportTo(InSeabed ? PoolSpawn : SeabedSpawn); ResetRotation(); }
        public void ResetView() { TeleportTo(InSeabed ? SeabedSpawn : PoolSpawn); ResetRotation(); }
        void ResetRotation()
        {
            Rotate(-origin.Camera.transform.eulerAngles.y);
            if (!XrActive) { pitch = 8; origin.Camera.transform.localRotation = Quaternion.Euler(pitch, 0, 0); }
        }
        void Rotate(float degrees)
        {
            origin.RotateAroundCameraUsingOriginUp(degrees);
            // Keep the physics interpolation pose in step with deliberate yaw changes.
            body.position = transform.position; body.rotation = transform.rotation;
        }
        void ToggleDesktopGrab()
        {
            if (desktopHeld) { desktopHeld.GetComponent<Rigidbody>().isKinematic = false; desktopHeld = null; LastInput = "G / released"; return; }
            if (Physics.SphereCast(origin.Camera.transform.position, 0.18f, origin.Camera.transform.forward, out RaycastHit hit, 2.5f, 1 << 9))
            {
                desktopHeld = hit.collider.GetComponentInParent<XRGrabInteractable>();
                if (desktopHeld) { desktopHeld.GetComponent<Rigidbody>().isKinematic = true; LastInput = "G / grabbed"; }
            }
            else LastInput = "G / aim at the orange inspection marker within 2.5 m";
        }
        void OnDestroy() { Controllers?.Dispose(); }
    }
}
