using System;
using System.IO;
using Unity.Robotics.UrdfImporter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Wa100OfficialUrdfImport
{
    private const string UrdfPath = "Assets/WA100_R_URDF.urdf";
    private const string PrefabFolder = "Assets/Robots";
    private const string PrefabPath = PrefabFolder + "/WA100_R_URDF_Official.prefab";
    private const string HandRigName = "Interactions Hands";
    private static readonly Vector3 FallbackDisplayPosition = new Vector3(0.32f, 0f, 0f);
    private const float DisplayGap = 0.08f;

    [InitializeOnLoadMethod]
    private static void ImportOnEditorLoadIfMissing()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                !File.Exists(UrdfPath))
            {
                return;
            }

            // The underwater acceptance scene owns its camera and rig.
            string activeScenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            if (activeScenePath.StartsWith("Assets/Underwater/Scenes/", StringComparison.Ordinal) ||
                activeScenePath.StartsWith("Assets/Reconstruction/Scenes/", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                if (!File.Exists(PrefabPath))
                {
                    ImportWa100();
                }

                PlaceInSceneWithFrontCamera();
            }
            catch (Exception ex)
            {
                Debug.LogError($"WA100 scene setup failed: {ex}");
            }
        };
    }

    [MenuItem("Tools/WA100/Import Official URDF")]
    public static void ImportWa100()
    {
        string fullUrdfPath = Path.GetFullPath(UrdfPath);
        if (!File.Exists(fullUrdfPath))
        {
            Debug.LogError($"WA100 URDF not found: {fullUrdfPath}");
            return;
        }

        EnsureFolder(PrefabFolder);

        ImportSettings settings = ImportSettings.DefaultSettings();
        settings.chosenAxis = ImportSettings.axisType.yAxis;
        settings.convexMethod = ImportSettings.convexDecomposer.unity;
        settings.OverwriteExistingPrefabs = true;

        GameObject robot = UrdfRobotExtensions.CreateRuntime(fullUrdfPath, settings);
        if (robot == null)
        {
            Debug.LogError("Unity Robotics URDF Importer returned null for WA100.");
            return;
        }

        robot.name = "WA100_R_URDF_Official";
        Component mapper = EnsureComponent(robot, "Wa100UdexRealArticulationMapper");
        InvokeIfExists(mapper, "ResetMappingsToUdexRight");
        InvokeIfExists(mapper, "AutoBind");

        Component guard = EnsureComponent(robot, "Wa100VisibilityGuard");
        InvokeIfExists(guard, "Apply");

        PrefabUtility.SaveAsPrefabAssetAndConnect(robot, PrefabPath, InteractionMode.AutomatedAction);
        Selection.activeObject = robot;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int articulationCount = robot.GetComponentsInChildren<ArticulationBody>(true).Length;
        int meshRendererCount = robot.GetComponentsInChildren<MeshRenderer>(true).Length;
        int mapperCount = ReadIntPropertyOrField(mapper, "BoundJointCount");
        Debug.Log($"WA100 imported with official URDF Importer. ArticulationBodies={articulationCount}, MeshRenderers={meshRendererCount}, MappedJoints={mapperCount}. Prefab={PrefabPath}");
    }

    [MenuItem("Tools/WA100/Place In Scene With Front Camera")]
    public static void PlaceInSceneWithFrontCamera()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("WA100 scene setup skipped during play mode.");
            return;
        }

        GameObject robot = FindSceneRobot();
        if (robot == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                ImportWa100();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            if (prefab == null)
            {
                Debug.LogError($"WA100 prefab not found: {PrefabPath}");
                return;
            }

            robot = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (robot == null)
            {
                Debug.LogError("Failed to instantiate WA100 prefab into the active scene.");
                return;
            }

            Undo.RegisterCreatedObjectUndo(robot, "Place WA100 robot");
        }

        robot.name = "WA100_R_URDF_Official";
        robot.SetActive(true);
        robot.transform.rotation = Quaternion.identity;
        robot.transform.localScale = Vector3.one;
        robot.transform.position = CalculateSideBySidePosition(robot);

        Component guard = EnsureComponent(robot, "Wa100VisibilityGuard");
        InvokeIfExists(guard, "Apply");

        Component mapper = EnsureComponent(robot, "Wa100UdexRealArticulationMapper");
        InvokeIfExists(mapper, "ResetMappingsToUdexRight");
        InvokeIfExists(mapper, "AutoBind");

        Camera camera = FindOrCreateCamera();
        FrameCamera(camera, CalculateDisplayBounds(robot));
        EnsureLight();

        Selection.activeObject = robot;
        if (!EditorApplication.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(robot.scene);
        }

        Debug.Log("WA100 placed to the right of Interactions Hands and WA100 Front Camera is aimed at the display.");
    }

    private static GameObject FindSceneRobot()
    {
        Type mapperType = FindType("Wa100UdexRealArticulationMapper");
        if (mapperType != null)
        {
            Component mapper = UnityEngine.Object.FindObjectOfType(mapperType, true) as Component;
            if (mapper != null)
            {
                return mapper.gameObject;
            }
        }

        GameObject named = GameObject.Find("WA100_R_URDF_Official");
        return named;
    }

    private static Component EnsureComponent(GameObject target, string typeName)
    {
        Type type = FindType(typeName);
        if (type == null)
        {
            Debug.LogWarning($"Cannot find component type {typeName}. Unity may still be compiling runtime scripts.");
            return null;
        }

        Component component = target.GetComponent(type);
        if (component == null)
        {
            component = target.AddComponent(type);
        }

        return component;
    }

    private static Type FindType(string typeName)
    {
        Type type = Type.GetType(typeName);
        if (type != null)
        {
            return type;
        }

        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static void InvokeIfExists(Component component, string methodName)
    {
        if (component == null)
        {
            return;
        }

        System.Reflection.MethodInfo method = component.GetType().GetMethod(methodName, Type.EmptyTypes);
        if (method != null)
        {
            method.Invoke(component, null);
        }
    }

    private static int ReadIntPropertyOrField(Component component, string memberName)
    {
        if (component == null)
        {
            return 0;
        }

        Type type = component.GetType();
        System.Reflection.PropertyInfo property = type.GetProperty(memberName);
        if (property != null && property.PropertyType == typeof(int))
        {
            return (int)property.GetValue(component);
        }

        System.Reflection.FieldInfo field = type.GetField(memberName);
        if (field != null && field.FieldType == typeof(int))
        {
            return (int)field.GetValue(component);
        }

        return 0;
    }

    private static Camera FindOrCreateCamera()
    {
        GameObject cameraObject = GameObject.Find("WA100 Front Camera");
        if (cameraObject == null)
        {
            cameraObject = new GameObject("WA100 Front Camera");
            Undo.RegisterCreatedObjectUndo(cameraObject, "Create WA100 Front Camera");
        }

        Camera camera = cameraObject.GetComponent<Camera>();
        if (camera == null)
        {
            camera = cameraObject.AddComponent<Camera>();
        }

        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.001f;
        camera.farClipPlane = 10f;
        camera.orthographic = true;
        camera.depth = 10f;
        camera.enabled = true;
        return camera;
    }

    private static Vector3 CalculateSideBySidePosition(GameObject robot)
    {
        GameObject hands = GameObject.Find(HandRigName);
        if (hands == null)
        {
            return FallbackDisplayPosition;
        }

        Bounds handBounds = CalculateBounds(hands);
        Bounds robotBounds = CalculateBounds(robot);
        Vector3 robotRootToCenter = robotBounds.center - robot.transform.position;

        float robotHalfWidth = Mathf.Max(robotBounds.extents.x, 0.08f);
        float x = handBounds.max.x + robotHalfWidth + DisplayGap - robotRootToCenter.x;
        float y = handBounds.center.y - robotRootToCenter.y;
        float z = handBounds.center.z - robotRootToCenter.z;

        return new Vector3(x, y, z);
    }

    private static Bounds CalculateDisplayBounds(GameObject robot)
    {
        Bounds bounds = CalculateBounds(robot);
        GameObject hands = GameObject.Find(HandRigName);
        if (hands != null)
        {
            bounds.Encapsulate(CalculateBounds(hands));
        }

        return bounds;
    }

    private static void FrameCamera(Camera camera, Bounds bounds)
    {
        Vector3 center = bounds.center;
        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.12f);
        float distance = Mathf.Max(bounds.size.z * 4f, 0.45f);

        camera.orthographicSize = Mathf.Max(maxSize * 0.62f, 0.12f);
        camera.transform.position = center + new Vector3(0f, 0f, -distance);
        camera.transform.LookAt(center, Vector3.up);
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position + new Vector3(0f, 0.08f, 0f), new Vector3(0.16f, 0.16f, 0.16f));
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        if (bounds.size.sqrMagnitude < 0.0001f)
        {
            bounds = new Bounds(root.transform.position + new Vector3(0f, 0.08f, 0f), new Vector3(0.16f, 0.16f, 0.16f));
        }

        return bounds;
    }

    private static void EnsureLight()
    {
        Light light = UnityEngine.Object.FindObjectOfType<Light>(true);
        if (light != null)
        {
            light.enabled = true;
            return;
        }

        GameObject lightObject = new GameObject("WA100 Key Light");
        Undo.RegisterCreatedObjectUndo(lightObject, "Create WA100 Key Light");
        light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = Path.GetFileName(folder);
        if (string.IsNullOrWhiteSpace(parent))
        {
            parent = "Assets";
        }

        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, name);
    }
}
