using UnityEngine;

[DefaultExecutionOrder(150)]
public class Wa100VisibilityGuard : MonoBehaviour
{
    public bool DisableGravity = true;
    public bool MakeRootImmovable = true;
    public bool FollowParent;
    public bool EnableAllRenderers = true;
    public bool ForceDefaultLayer = true;
    public bool DisableColliders = true;
    public bool EnableConvexContactColliders;
    public bool DisableUrdfKeyboardControl = true;

    private ArticulationBody followedRoot;
    private Transform rootParent;
    private Vector3 rootLocalPosition;
    private Quaternion rootLocalRotation;

    private void Awake()
    {
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void Start() { Apply(); SyncMountedRoot(); }
    private void FixedUpdate() { SyncMountedRoot(); }
    private void LateUpdate() { SyncMountedRoot(); }
    public float MountError => followedRoot != null && rootParent != null
        ? Vector3.Distance(followedRoot.transform.position, rootParent.TransformPoint(rootLocalPosition)) : -1;

    public void SyncMountedRoot()
    {
        if (!FollowParent || followedRoot == null || rootParent == null) return;

        Vector3 position = rootParent.TransformPoint(rootLocalPosition);
        Quaternion rotation = rootParent.rotation * rootLocalRotation;
        if ((position - followedRoot.transform.position).sqrMagnitude < 0.00000001f &&
            Quaternion.Angle(rotation, followedRoot.transform.rotation) < 0.001f) return;

        followedRoot.TeleportRoot(position, rotation);
    }

    [ContextMenu("Apply Visibility Guard")]
    public void Apply()
    {
        if (ForceDefaultLayer)
        {
            SetLayerRecursively(transform, 0);
        }

        if (EnableAllRenderers)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
            }
        }

        if (DisableColliders)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }
        else if (EnableConvexContactColliders)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                // Only convex/primitive shapes can belong to the moving articulation.
                bool supported = !(collider is MeshCollider mesh) || mesh.convex;
                collider.enabled = supported;
                if (supported) collider.isTrigger = false;
            }
            // Imported neighbouring finger/palm meshes overlap at the joint seams.
            // Keep external contacts while excluding these internal collision pairs.
            for(int i=0;i<colliders.Length;i++)for(int j=i+1;j<colliders.Length;j++)
                if(colliders[i].enabled && colliders[j].enabled) Physics.IgnoreCollision(colliders[i],colliders[j]);
        }

        if (DisableUrdfKeyboardControl)
        {
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                // Editor-only URDF components are absent from Android builds.
                if (behaviours[i] == null) continue;
                string typeName = behaviours[i].GetType().FullName;
                if (typeName == "JointControl" ||
                    typeName == "Unity.Robotics.UrdfImporter.Control.Controller" ||
                    typeName == "Unity.Robotics.UrdfImporter.Control.FKRobot")
                {
                    behaviours[i].enabled = false;
                }
            }
        }

        ArticulationBody[] bodies = GetComponentsInChildren<ArticulationBody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if(EnableConvexContactColliders)
            {
                bodies[i].solverIterations=24;
                bodies[i].solverVelocityIterations=8;
                bodies[i].collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                bodies[i].maxDepenetrationVelocity=1;
                bodies[i].maxJointVelocity=3;
            }
            if (DisableGravity)
            {
                bodies[i].useGravity = false;
            }

            var parentBody = bodies[i].transform.parent != null
                ? bodies[i].transform.parent.GetComponentInParent<ArticulationBody>() : null;
            bool isHierarchyRoot = parentBody == null;
            if (MakeRootImmovable && isHierarchyRoot)
            {
                bodies[i].immovable = true;
            }

            if (FollowParent && isHierarchyRoot && followedRoot == null)
            {
                followedRoot = bodies[i];
                rootParent = followedRoot.transform.parent;
                rootLocalPosition = followedRoot.transform.localPosition;
                rootLocalRotation = followedRoot.transform.localRotation;
            }
        }
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;

        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
