using UnityEngine;

public class Wa100VisibilityGuard : MonoBehaviour
{
    public bool DisableGravity = true;
    public bool MakeRootImmovable = true;
    public bool EnableAllRenderers = true;
    public bool ForceDefaultLayer = true;
    public bool DisableColliders = true;
    public bool DisableUrdfKeyboardControl = true;

    private void Awake()
    {
        Apply();
    }

    private void OnEnable()
    {
        Apply();
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

        if (DisableUrdfKeyboardControl)
        {
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
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
            if (DisableGravity)
            {
                bodies[i].useGravity = false;
            }

            if (MakeRootImmovable && bodies[i].isRoot)
            {
                bodies[i].immovable = true;
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
