using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using Unity.XR.PXR;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Management;
using Object = UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterBaselineBuilder
    {
        public const string ScenePath = "Assets/Underwater/Scenes/UnderwaterBaseline.unity";
        const string Root = "Assets/Underwater";
        const string Starter = "Assets/Samples/XR Interaction Toolkit/2.5.2/Starter Assets";
        static Material tile, wall, dark, metal, orange, particle, bubble;
        static TeleportationProvider teleport;

        [MenuItem("Tools/Underwater/1 Build or Rebuild Baseline Scene")]
        public static void BuildScene()
        {
            // The command bridge saves dirty scenes as timestamped copies before generation.
            foreach (string folder in new[] { "Settings", "Materials", "Scenes" }) Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            ConfigurePipeline();
            ConfigureXr();
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.graphicsJobs = false;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            var debugger = AssetDatabase.LoadAssetAtPath<Unity.XR.PXR.Debugger.PXR_PicoDebuggerSO>("Assets/Resources/PXR_PicoDebuggerSO.asset");
            if (!debugger)
            {
                debugger = ScriptableObject.CreateInstance<Unity.XR.PXR.Debugger.PXR_PicoDebuggerSO>();
                AssetDatabase.CreateAsset(debugger, "Assets/Resources/PXR_PicoDebuggerSO.asset");
            }
            debugger.isOpen = false;
            var picoConfig = PXR_ProjectSetting.GetProjectConfig();
            picoConfig.adaptiveResolution = true;
            picoConfig.openMRC = false;
            EditorUtility.SetDirty(picoConfig);
            SetLayer(8, "UnderwaterEnvironment");
            SetLayer(9, "UnderwaterProps");
            SetLayer(10, "WaterSurface");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.47f, 0.59f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.31f, 0.39f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.24f, 0.26f);
            RenderSettings.ambientLight = new Color(0.48f, 0.60f, 0.65f);
            RenderSettings.fog = false;
            CreateMaterials();

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Starter + "/Prefabs/XR Origin (XR Rig).prefab"));
            PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rig.name = "Underwater XR Origin";
            rig.transform.position = UnderwaterMotor.PoolSpawn;
            var origin = rig.GetComponent<XROrigin>();
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 1.65f;
            origin.CameraFloorOffsetObject.transform.localPosition = Vector3.up * 1.65f;
            var camera = origin.Camera;
            camera.allowHDR = false;
            camera.nearClipPlane = 0.06f;
            camera.farClipPlane = 180;
            camera.transform.localRotation = Quaternion.Euler(8, 0, 0);
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.26f, 0.48f, 0.57f);
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            var system = rig.GetComponentInChildren<LocomotionSystem>(true);
            if (!system) system = rig.AddComponent<LocomotionSystem>();
            system.xrOrigin = origin;
            // Starter Assets supply the rig, action references and grab interactors. A single water motor owns movement.
            foreach (var provider in rig.GetComponentsInChildren<LocomotionProvider>(true)) provider.enabled = false;
            foreach (var component in rig.GetComponentsInChildren<MonoBehaviour>(true))
                if (component.GetType().Name == "ActionBasedControllerManager" || component is CharacterControllerDriver || component is XRInputModalityManager) component.enabled = false;
            foreach (var cc in rig.GetComponentsInChildren<CharacterController>(true)) Object.DestroyImmediate(cc);
            foreach (var interactor in rig.GetComponentsInChildren<XRRayInteractor>(true))
                if (interactor.name.Contains("Teleport")) interactor.gameObject.SetActive(false);
            foreach (var direct in rig.GetComponentsInChildren<XRDirectInteractor>(true))
            { direct.interactionLayers = 1; direct.physicsLayerMask = (1 << 9); }
            foreach (var ray in rig.GetComponentsInChildren<XRRayInteractor>(true))
            { ray.interactionLayers = 1; ray.raycastMask = (1 << 8) | (1 << 9); }
            teleport = rig.AddComponent<TeleportationProvider>();
            teleport.system = system;
            teleport.delayTime = 0;
            if (!Object.FindObjectOfType<XRInteractionManager>()) new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            BindPicoControllers(rig);
            rig.AddComponent<InputActionManager>().actionAssets = new List<InputActionAsset> {
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(Starter + "/XRI Default Input Actions.inputactions") };
            var motor = rig.AddComponent<UnderwaterMotor>();
            motor.origin = origin;
            motor.teleportation = teleport;
            motor.rightController = rig.GetComponentsInChildren<ActionBasedController>(true).First(c => c.name.Contains("Right")).transform;
            rig.layer = 2;
            var capsule = rig.GetComponent<CapsuleCollider>();
            capsule.radius = 0.28f; capsule.height = 1.7f; capsule.center = Vector3.up * 0.9f;
            motor.teleportLine = new GameObject("Teleport Arc").AddComponent<LineRenderer>();
            motor.teleportLine.sharedMaterial = Material("TeleportLine", "Universal Render Pipeline/Unlit", Color.white);
            motor.teleportLine.widthMultiplier = 0.018f;
            motor.teleportLine.enabled = false;
            motor.teleportLine.shadowCastingMode = ShadowCastingMode.Off;
            motor.teleportMarker = Primitive("Teleport Target", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.6f, 0.006f, 0.6f),
                Material("TeleportTarget", "Universal Render Pipeline/Unlit", new Color(0.3f, 1, 0.75f)), false).transform;
            motor.teleportMarker.gameObject.SetActive(false);
            var pico = camera.gameObject.AddComponent<PXR_Manager>();
            pico.adaptiveResolution = true;
            pico.minEyeTextureScale = 0.7f;
            pico.maxEyeTextureScale = 1.26f;
            pico.adaptiveResolutionPowerSetting = AdaptiveResolutionPowerSetting.BALANCED;
            pico.openMRC = false;

            UnderwaterComparisonBuilder.Build(teleport);
            var environment = new GameObject("Underwater Environment").AddComponent<UnderwaterEnvironment>();
            environment.eye = camera;
            environment.snow = new[] { Snow("Near Snow", 220, 0.025f, 0.055f, 0.18f, 5),
                Snow("Middle Snow", 260, 0.018f, 0.038f, 0.14f, 9),
                Snow("Far Snow", 300, 0.012f, 0.026f, 0.10f, 16) };
            environment.bubbles = Snow("Event Bubbles", 48, 0.03f, 0.08f, 0.45f, 0.4f);
            var bubbleMain = environment.bubbles.main; bubbleMain.startLifetime = 2.4f;
            var bubbleVelocity = environment.bubbles.velocityOverLifetime; bubbleVelocity.x = 0; bubbleVelocity.y = 0.9f; bubbleVelocity.z = 0;
            var bubbleEmission = environment.bubbles.emission; bubbleEmission.rateOverTime = 0;
            environment.bubbles.GetComponent<ParticleSystemRenderer>().sharedMaterial = bubble;
            environment.bubbles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            motor.environment = environment;
            var performance = environment.gameObject.AddComponent<UnderwaterPerformance>();
            performance.motor = motor; performance.environment = environment; performance.pico = pico;
            performance.worldReadout = CreateReadout();
            performance.worldReadout.transform.parent.SetParent(camera.transform, false);
            performance.worldReadout.transform.parent.localPosition = new Vector3(-0.40f, -0.20f, 1.5f);
            performance.worldReadout.transform.parent.localScale = Vector3.one * 0.001f;
            var light = new GameObject("Pool Daylight").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.45f;
            light.color = new Color(0.8f, 0.94f, 1);
            light.transform.rotation = Quaternion.Euler(55, -35, 0);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.65f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.25f;
            light.transform.rotation = Quaternion.Euler(64, -25, 0);
            environment.gameObject.AddComponent<UnderwaterSmokeRun>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Selection.activeObject = environment;
            Debug.Log("[Underwater] Baseline scene created: " + ScenePath);
        }

        static void ConfigurePipeline()
        {
            string pipelinePath = Root + "/Settings/UnderwaterURP.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (!pipeline)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, Root + "/Settings/UnderwaterRenderer.asset");
                var fog = ScriptableObject.CreateInstance<UnderwaterHorizonFeature>();
                fog.name = "Horizon Line Fog and Color";
                fog.shader = Shader.Find("Underwater/DepthFog");
                AssetDatabase.AddObjectToAsset(fog, renderer);
                renderer.rendererFeatures.Add(fog);
                renderer.SetDirty();
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 2;
            pipeline.renderScale = 1;
            pipeline.shadowDistance = 65;
            pipeline.shadowCascadeCount = 2;
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Settings/UnderwaterRenderer.asset");
            rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            ResourceReloader.ReloadAllNullIn(rendererData, "Packages/com.unity.render-pipelines.universal");
            rendererData.copyDepthMode = CopyDepthMode.AfterOpaques;
            EditorUtility.SetDirty(rendererData);
            var serializedPipeline = new SerializedObject(pipeline);
            serializedPipeline.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            serializedPipeline.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
            pipeline.useSRPBatcher = true;
            foreach (var feature in rendererData.rendererFeatures.OfType<UnderwaterHorizonFeature>())
            { feature.surfaceHeight = 10; feature.density = 0.035f; feature.absorption = new Vector3(0.025f, 0.009f, 0.005f); feature.fogColor = new Color(0.08f, 0.34f, 0.40f); EditorUtility.SetDirty(feature); }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(2, false);
            QualitySettings.vSyncCount = 0;
            EditorUtility.SetDirty(pipeline);
        }

        static void ConfigureXr()
        {
            string path = Root + "/Settings/XRGeneralSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (!settings) { settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>(); AssetDatabase.CreateAsset(settings, path); }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                if (!settings.HasManagerSettingsForBuildTarget(group)) settings.CreateDefaultManagerSettingsForBuildTarget(group);
                var general = settings.SettingsForBuildTarget(group);
                general.InitManagerOnStart = true;
                var manager = general.Manager;
                manager.automaticLoading = true; manager.automaticRunning = true;
                string loader = group == BuildTargetGroup.Standalone ? "Unity.XR.PICO.LivePreview.PXR_PTLoader" : "Unity.XR.PXR.PXR_Loader";
                foreach (var old in manager.activeLoaders.ToArray())
                    if (old.GetType().FullName != loader) manager.TryRemoveLoader(old);
                if (!manager.activeLoaders.Any(l => l.GetType().FullName == loader))
                    if (!XRPackageMetadataStore.AssignLoader(manager, loader, group)) throw new Exception("Could not assign " + loader);
                EditorUtility.SetDirty(manager); EditorUtility.SetDirty(general);
            }
            EditorUtility.SetDirty(settings);
            var picoSettings = PXR_Settings.GetSettings();
            if (!picoSettings)
            {
                picoSettings = ScriptableObject.CreateInstance<PXR_Settings>();
                AssetDatabase.CreateAsset(picoSettings, Root + "/Settings/PICOSettings.asset");
                EditorBuildSettings.AddConfigObject("Unity.XR.PXR.Settings", picoSettings, true);
            }
            picoSettings.stereoRenderingModeAndroid = PXR_Settings.StereoRenderingModeAndroid.Multiview;
            picoSettings.systemDisplayFrequency = PXR_Settings.SystemDisplayFrequency.RefreshRate72;
            EditorUtility.SetDirty(picoSettings);
        }

        static void BindPicoControllers(GameObject rig)
        {
            // Integration 3.4 / Live Preview expose XRController devicePosition, gripPressed and triggerPressed.
            // PicoAimHand paths belong to hand tracking and must not replace tracked controller bindings.
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(Starter + "/XRI Default Input Actions.inputactions");
            foreach (var controller in rig.GetComponentsInChildren<ActionBasedController>(true).Where(c => c.enableInputTracking))
            {
                bool left = controller.name.Contains("Left");
                string hand = left ? "LeftHand" : "RightHand";
                controller.modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.unity.xr.picoxr/Assets/Resources/Prefabs/" + (left ? "Left" : "Right") + "ControllerModel.prefab").transform;
                // Existing generic XR bindings already include PICO and Live Preview; explicitly include their named controls.
                string prefix = $"<XRController>{{{hand}}}/";
                AddBinding(actions, "XRI " + hand, "Position", prefix + "devicePosition");
                AddBinding(actions, "XRI " + hand, "Rotation", prefix + "deviceRotation");
                AddBinding(actions, "XRI " + hand + " Interaction", "Select", prefix + "gripPressed");
                AddBinding(actions, "XRI " + hand + " Interaction", "Activate", prefix + "triggerPressed");
                AddBinding(actions, "XRI " + hand + " Interaction", "UI Press", prefix + "triggerPressed");
            }
            File.WriteAllText(AssetDatabase.GetAssetPath(actions), actions.ToJson());
            EditorUtility.SetDirty(actions);
        }

        static void AddBinding(InputActionAsset asset, string mapName, string actionName, string path)
        {
            var action = asset.FindActionMap(mapName, true).FindAction(actionName, true);
            if (!action.bindings.Any(b => b.path == path)) action.AddBinding(path);
        }

        static Material Material(string name, string shader, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.3f);
            return material;
        }

        static void CreateMaterials()
        {
            tile = Material("PoolTiles", "Universal Render Pipeline/Lit", new Color(0.65f, 0.82f, 0.83f));
            wall = Material("WallTiles", "Universal Render Pipeline/Lit", new Color(0.56f, 0.76f, 0.8f));
            dark = Material("LaneBlue", "Universal Render Pipeline/Lit", new Color(0.03f, 0.18f, 0.23f));
            metal = Material("BrushedMetal", "Universal Render Pipeline/Lit", new Color(0.4f, 0.55f, 0.58f));
            orange = Material("SafetyOrange", "Universal Render Pipeline/Lit", new Color(0.96f, 0.43f, 0.08f));
            particle = Material("MarineSnow", "Underwater/Particles", new Color(0.66f, 0.88f, 0.9f, 1));
            bubble = Material("EventBubbles", "Underwater/Particles", new Color(0.74f, 0.95f, 1, 1));
            var texture = new Texture2D(128, 128, TextureFormat.RGB24, false);
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                texture.SetPixel(x, y, x < 3 || y < 3 ? new Color(0.5f, 0.63f, 0.65f) : Color.white);
            texture.Apply();
            string path = Root + "/Materials/PoolTile.png";
            File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            tile.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            tile.SetTextureScale("_BaseMap", new Vector2(44, 64));
            wall.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            wall.SetTextureScale("_BaseMap", new Vector2(44, 10));
        }

        static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            go.transform.position = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            go.layer = 8;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void Floor(string name, Vector3 position, Vector3 size, Material mat)
        {
            var go = Primitive(name, PrimitiveType.Cube, position, size, mat);
            var area = go.AddComponent<TeleportationArea>();
            area.teleportationProvider = teleport;
            area.interactionLayers = 2;
        }

        static void BuildPool()
        {
            Floor("Pool Floor - Teleportation Area", new Vector3(0, -0.15f, 0), new Vector3(22, 0.3f, 32), tile);
            Primitive("Left Pool Wall", PrimitiveType.Cube, new Vector3(-11.2f, 2.1f, 0), new Vector3(0.4f, 4.5f, 32.8f), wall);
            Primitive("Right Pool Wall", PrimitiveType.Cube, new Vector3(11.2f, 2.1f, 0), new Vector3(0.4f, 4.5f, 32.8f), wall);
            Primitive("Far Pool Wall", PrimitiveType.Cube, new Vector3(0, 2.1f, 16.2f), new Vector3(22, 4.5f, 0.4f), wall);
            Primitive("Near Pool Wall", PrimitiveType.Cube, new Vector3(0, 2.1f, -16.2f), new Vector3(22, 4.5f, 0.4f), wall);
            Floor("Right Deck - Teleportation Area", new Vector3(13.5f, 4.05f, 0), new Vector3(4.6f, 0.3f, 37), tile);
            Floor("Left Deck - Teleportation Area", new Vector3(-13.5f, 4.05f, 0), new Vector3(4.6f, 0.3f, 37), tile);
            for (int lane = -2; lane <= 2; lane++)
            {
                Primitive("Lane Marker", PrimitiveType.Cube, new Vector3(lane * 4, 0.012f, 0), new Vector3(0.15f, 0.015f, 29), dark, false);
                Primitive("Lane End", PrimitiveType.Cube, new Vector3(lane * 4, 0.015f, 13), new Vector3(1.4f, 0.018f, 0.15f), dark, false);
            }
            for (int i = 0; i < 4; i++)
            {
                float z = -4 + i * 5;
                Primitive("Distance Reference " + (6 + i * 5) + "m", PrimitiveType.Cylinder, new Vector3(-5, 1.1f, z), new Vector3(0.38f, 1.1f, 0.38f), i % 2 == 0 ? orange : metal);
                var text = new GameObject("Distance Label").AddComponent<TextMesh>();
                text.text = (6 + i * 5) + "m"; text.fontSize = 52; text.characterSize = 0.08f;
                text.anchor = TextAnchor.MiddleCenter; text.color = Color.white;
                text.transform.position = new Vector3(-5, 2.65f, z); text.transform.rotation = Quaternion.identity;
            }
            var water = Material("WaterSurface", "Universal Render Pipeline/Unlit", new Color(0.2f, 0.65f, 0.7f, 0.15f));
            water.SetFloat("_Surface", 1); water.SetFloat("_Blend", 0); water.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            water.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); water.SetFloat("_ZWrite", 0);
            water.SetFloat("_Cull", 0); water.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); water.renderQueue = 3000;
            var surface = Primitive("Water Surface Line Y4", PrimitiveType.Plane, new Vector3(0, 4, 0), new Vector3(2.2f, 1, 3.2f), water, false);
            surface.layer = 10;
            var prop = Primitive("Grab Test - Neutral Buoyancy Tool", PrimitiveType.Cube, new Vector3(0.5f, 1.25f, -8.8f), Vector3.one * 0.23f, orange);
            prop.layer = 9;
            var rb = prop.AddComponent<Rigidbody>(); rb.useGravity = false; rb.drag = 4; rb.angularDrag = 4;
            prop.AddComponent<XRGrabInteractable>();
        }

        static ParticleSystem Snow(string name, int budget, float minSize, float maxSize, float alpha, float radius)
        {
            var ps = new GameObject(name).AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = true; main.startLifetime = 20;
            main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = new Color(0.75f, 0.92f, 1, alpha); main.maxParticles = budget;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.useUnscaledTime = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(radius * 2, 7, radius * 2);
            var emission = ps.emission; emission.rateOverTime = budget / 20f;
            var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.015f, 0.035f); velocity.y = new ParticleSystem.MinMaxCurve(-0.025f, -0.01f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.015f, 0.01f);
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = particle;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            ps.transform.position = new Vector3(0, 1.7f, -10);
            return ps;
        }

        static Text CreateReadout()
        {
            var canvas = new GameObject("World Performance Panel", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.transform.position = new Vector3(3.1f, 2.25f, -5);
            canvas.transform.localScale = Vector3.one * 0.003f;
            var rect = (RectTransform)canvas.transform; rect.sizeDelta = new Vector2(840, 410);
            var panel = new GameObject("Readout", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            panel.transform.SetParent(canvas.transform, false);
            panel.rectTransform.sizeDelta = rect.sizeDelta;
            panel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panel.fontSize = 25; panel.color = new Color(0.75f, 1, 1);
            panel.alignment = TextAnchor.MiddleLeft;
            panel.text = "UNDERWATER BASELINE";
            return panel;
        }

        static void SetLayer(int index, string name)
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            tags.FindProperty("layers").GetArrayElementAtIndex(index).stringValue = name; tags.ApplyModifiedProperties();
        }

        [MenuItem("Tools/Underwater/2 Open Baseline")]
        public static void Open() => EditorSceneManager.OpenScene(ScenePath);

        [MenuItem("Tools/Underwater/3 Validate and Build Windows Preview")]
        public static void ValidateAndBuild()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Validate();
            Directory.CreateDirectory("Builds/UnderwaterBaseline");
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
                target = BuildTarget.StandaloneWindows64, locationPathName = "Builds/UnderwaterBaseline/UnderwaterBaseline.exe",
                options = BuildOptions.Development });
            File.WriteAllText("Logs/UnderwaterBuildResult.txt", $"{result.summary.result}\nErrors: {result.summary.totalErrors}\nWarnings: {result.summary.totalWarnings}\n");
            if (result.summary.result != BuildResult.Succeeded) throw new Exception("Underwater player build failed");
        }

        public static void BuildAll() { BuildScene(); ValidateAndBuild(); }

        public static void Validate()
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            Require(pipeline && pipeline.supportsCameraDepthTexture && pipeline.supportsCameraOpaqueTexture && !pipeline.supportsHDR && pipeline.renderScale == 1, "URP depth / opaque / LDR / scale");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Settings/UnderwaterRenderer.asset");
            Require(renderer.rendererFeatures.FirstOrDefault() is UnderwaterHorizonFeature, "Horizon feature first");
            Require(new SerializedObject(renderer).FindProperty("shaders.coreBlitPS").objectReferenceValue != null, "Renderer shader resources");
            var motor = Object.FindObjectOfType<UnderwaterMotor>();
            Require(motor && motor.teleportation && motor.rightController && motor.origin.Camera, "Rig and locomotion references");
            Require(motor.GetComponent<InputActionManager>().actionAssets.Count > 0, "Input action asset enabled");
            Require(Object.FindObjectsOfType<TeleportationArea>().Length >= 3, "Teleport areas");
            UnderwaterComparisonBuilder.ValidateComparison();
            Require(Object.FindObjectOfType<XRGrabInteractable>(), "Grab test prop");
            var environment = Object.FindObjectOfType<UnderwaterEnvironment>();
            Require(environment && environment.snow.Length == 3 && environment.bubbles, "Three snow layers and event bubbles");
            Require(motor.origin.Camera.GetComponent<PXR_Manager>().adaptiveResolution, "PICO adaptive resolution");
            Require(PXR_Settings.GetSettings() && PXR_Settings.GetSettings().systemDisplayFrequency == PXR_Settings.SystemDisplayFrequency.RefreshRate72, "Native PICO 72Hz configuration");
            var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(Root + "/Settings/XRGeneralSettings.asset");
            Require(settings.SettingsForBuildTarget(BuildTargetGroup.Standalone).Manager.activeLoaders.Single().GetType().FullName == "Unity.XR.PICO.LivePreview.PXR_PTLoader", "PICO preview only");
            Require(settings.SettingsForBuildTarget(BuildTargetGroup.Android).Manager.activeLoaders.Single().GetType().FullName == "Unity.XR.PXR.PXR_Loader", "PICO native only");
            foreach (var component in Object.FindObjectsOfType<Transform>(true)) Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(component.gameObject) == 0, "Missing scripts: " + component.name);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/UnderwaterAssetChecks.txt", "PASS: URP, first fog feature, rig, teleport areas, grab prop, three particle layers, event bubbles, native adaptive resolution, PICO loaders, no missing scripts.\n");
            Debug.Log("[Underwater] Asset validation passed");
        }

        static void Require(bool condition, string message) { if (!condition) throw new Exception("Underwater validation: " + message); }
    }
}
