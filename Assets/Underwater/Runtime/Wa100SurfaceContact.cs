using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

// Contact queries cover the palm and every visible finger link, not just the wrist pivot.
// Oriented overlap boxes and swept boxes query the actual environment colliders.
[DefaultExecutionOrder(250)]
public sealed class Wa100SurfaceContact : MonoBehaviour
{
    public PicoArmPoseDriver Arm;
    public Wa100VisibilityGuard Hand;
    public LayerMask SurfaceMask = 1 << 8;
    [Min(0)] public float Skin = 0.003f;
    public bool HapticsEnabled = true;
    public bool InContact { get; private set; }
    public Vector3 Normal { get; private set; } = Vector3.up;
    public string SurfaceName { get; private set; } = "";
    public string HapticStatus { get; private set; } = "No contact pulse requested";
    public int HapticRequests { get; private set; }
    public int HapticAccepted { get; private set; }
    public int ShapeCount => shapes.Count;
    public bool QueryOverflow { get; private set; }

    struct Shape
    {
        public Transform link;
        public Bounds localBounds;
        public Vector3 wristCenter, halfExtents;
        public Quaternion wristRotation;
    }
    readonly List<Shape> shapes = new List<Shape>();
    readonly Collider[] nearby = new Collider[128];
    readonly RaycastHit[] hits = new RaycastHit[128];
    float nextPulse;
    bool prepared;

    public void Rebuild()
    {
        shapes.Clear();
        if (!Arm) Arm = GetComponent<PicoArmPoseDriver>();
        if (!Hand && Arm && Arm.Wrist) Hand = Arm.Wrist.GetComponentInChildren<Wa100VisibilityGuard>(true);
        if (!Hand) return;
        foreach (var filter in Hand.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<Renderer>();
            if (!filter.sharedMesh || !renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
            shapes.Add(new Shape { link = filter.transform, localBounds = filter.sharedMesh.bounds });
        }
        prepared = false;
    }

    public void BeginMotion()
    {
        if (shapes.Count == 0) Rebuild();
        if (!Hand || !Arm || !Arm.Wrist) return;
        Hand.SyncMountedRoot();
        for (int i = 0; i < shapes.Count; i++)
        {
            var shape = shapes[i];
            Vector3 scale = shape.link.lossyScale;
            Vector3 worldCenter = shape.link.TransformPoint(shape.localBounds.center);
            shape.wristCenter = Quaternion.Inverse(Arm.Wrist.rotation) * (worldCenter - Arm.Wrist.position);
            shape.wristRotation = Quaternion.Inverse(Arm.Wrist.rotation) * shape.link.rotation;
            shape.halfExtents = Vector3.Scale(shape.localBounds.extents,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) + Vector3.one * Skin;
            shapes[i] = shape;
        }
        InContact = false; QueryOverflow = false; prepared = true;
    }

    bool IsSurface(Collider other)
    {
        if (!other || !other.enabled || other.isTrigger) return false;
        if (Arm && other.transform.IsChildOf(Arm.transform)) return false;
        if (Arm && Arm.Motor && other.transform.IsChildOf(Arm.Motor.transform)) return false;
        return true;
    }

    public bool Blocked(Vector3 position, Quaternion rotation)
    {
        if (!prepared) return false;
        foreach (var shape in shapes)
        {
            Vector3 center = position + rotation * shape.wristCenter;
            Quaternion orientation = rotation * shape.wristRotation;
            int count = Physics.OverlapBoxNonAlloc(center, shape.halfExtents, nearby, orientation,
                SurfaceMask, QueryTriggerInteraction.Ignore);
            if (count == nearby.Length) { QueryOverflow = InContact = true; return true; }
            for (int i = 0; i < count; i++)
            {
                var surface = nearby[i];
                if (!IsSurface(surface)) continue;
                // OverlapBox performs the environment shape query directly. A disabled
                // temporary collider is not needed (its cooked shape can be unavailable).
                // ClosestPoint does not support a non-convex terrain mesh. The overlap
                // still provides contact; raycast that collider for its local normal.
                Vector3 separation;
                if(surface is MeshCollider mesh && !mesh.convex)
                {
                    Vector3 direction=surface.bounds.center-center;
                    if(direction.sqrMagnitude<1e-8f)direction=Vector3.down;
                    separation=surface.Raycast(new Ray(center,direction.normalized),out var hit,20)?hit.normal:Vector3.up;
                }
                else separation=center-surface.ClosestPoint(center);
                Normal=separation.sqrMagnitude>1e-10f ? separation.normalized : Vector3.up;
                InContact = true; SurfaceName = surface.name;
                return true;
            }
        }
        return false;
    }

    // Box casts protect thin obstacles during translation; small joint-space steps also
    // check rotating fingers/palm. Imported articulation colliders handle glove flexion.
    public bool SweepBlocked(Vector3 from, Quaternion fromRotation, Vector3 to)
    {
        if (!prepared) return false;
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < 1e-10f) return false;
        foreach (var shape in shapes)
        {
            int count = Physics.BoxCastNonAlloc(from + fromRotation * shape.wristCenter,
                shape.halfExtents, delta.normalized, hits, fromRotation * shape.wristRotation,
                delta.magnitude, SurfaceMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) { QueryOverflow = InContact = true; return true; }
            for (int i = 0; i < count; i++)
            {
                if (!IsSurface(hits[i].collider) || hits[i].distance <= 0.00001f) continue;
                InContact = true; Normal = hits[i].normal; SurfaceName = hits[i].collider.name; return true;
            }
        }
        return false;
    }

    public void Pulse()
    {
        if (!HapticsEnabled || Time.unscaledTime < nextPulse) return;
        if (!Arm || !Arm.Motor || !Arm.Motor.XrActive || Arm.Motor.InSeabed != Arm.SeabedArea) return;
        nextPulse = Time.unscaledTime + 0.12f;
        HapticRequests++;
        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (device.isValid && device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
        {
            bool accepted = device.SendHapticImpulse(0, 0.35f, 0.045f);
            if (accepted) HapticAccepted++;
            HapticStatus = accepted ? "XR provider accepted right-hand contact pulse" : "XR provider rejected contact pulse";
            return;
        }
        // PXR_Input's native implementation is a no-op in the Editor. Do not report
        // success from that API in Live Preview when its XR provider lacks haptics.
        HapticStatus = "Active XR provider does not expose impulse haptics";
    }

    void LateUpdate()
    {
        if (!Arm || !Hand || !Arm.PreventSurfacePenetration) return;
        bool armContact = Arm.SurfaceContact;
        BeginMotion();
        bool fingerContact = Blocked(Arm.Wrist.position, Arm.Wrist.rotation);
        if (armContact || fingerContact) Pulse();
    }

}
