using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    /// <summary>
    /// Adds the bounded flow volumes, hydrodynamic proxies and a reproducible
    /// obstacle course to the editable research scene.  The ROV references remain
    /// kinematic; setting applyForces on a separately controlled vehicle enables
    /// the same approximation without moving the comparison models.
    /// </summary>
    public static class UnderwaterPhysicsUpgrade
    {
        public const string ScenePath = "Assets/Underwater/Scenes/UnderwaterHydrodynamicsEditable.unity";
        const string Root = "Assets/Underwater";

        [MenuItem("Tools/Underwater/Research/11 Add Fluid Volumes, Hydrodynamics and Obstacle Course")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play before adding the physics layer.");
            if(SceneManager.GetActiveScene().path!=ScenePath)
            {
                if(File.Exists(ScenePath))EditorSceneManager.OpenScene(ScenePath);
                else UnderwaterResearchSceneBuilder.Open();
            }
            var scene = SceneManager.GetActiveScene();
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene, "Assets/Underwater/Backups/UnderwaterResearch-beforePhysics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            var presence = Object.FindObjectOfType<UnderwaterPresence>();
            if (!presence) throw new Exception("UnderwaterPresence is missing from the editable scene.");

            var pool = Volume("Pool Fluid Volume", new Bounds(new Vector3(0, 5, 0), new Vector3(25, 10, 50)), presence,
                new Vector3(0.08f, 0, 0.025f), 16, 8, 32);
            var sea = Volume("Seabed Fluid Volume", new Bounds(new Vector3(62, 5, 0), new Vector3(25, 10, 50)), presence,
                new Vector3(0.16f, 0, 0.055f), 16, 8, 32);
            presence.fluidVolumes = new[] { pool, sea };
            EditorUtility.SetDirty(presence);

            AddHydrodynamics("ROVp - Pool", pool, presence);
            AddHydrodynamics("ROVp - Seabed", sea, presence);
            BuildReliefAndObstacles();
            pool.ground=GameObject.Find("Pool Floor - 25 x 50 m").GetComponent<Collider>();
            sea.ground=GameObject.Find("Sand Terrain - Teleportation Area").GetComponent<Collider>();
            sea.obstacles=GameObject.Find("Hydrodynamic Obstacle Course").GetComponentsInChildren<Collider>();
            EditorUtility.SetDirty(pool);EditorUtility.SetDirty(sea);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            UnderwaterResearchSceneBuilder.Validate();
            File.WriteAllText("Logs/UnderwaterPhysicsUpgrade.txt",
                "Scene saved and structural validation passed: two bounded semi-Lagrangian fluid volumes, two hydrodynamic proxies, relief mesh and collidable obstacle course. Runtime/HMD validation remains separate.\n" +
                "Reference ROVs remain kinematic; enable applyForces only on a dynamic vehicle.\n");
        }

        [MenuItem("Tools/Underwater/Research/12 Open Hydrodynamics Scene")]
        public static void Open(){EditorSceneManager.OpenScene(ScenePath);}

        [MenuItem("Tools/Underwater/Research/13 Build Hydrodynamics Scene Windows")]
        public static void Build()
        {
            Open();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());UnderwaterResearchSceneBuilder.Validate();
            Directory.CreateDirectory("Builds/UnderwaterHydrodynamics");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},target=BuildTarget.StandaloneWindows64,
                locationPathName="Builds/UnderwaterHydrodynamics/UnderwaterHydrodynamics.exe",options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Hydrodynamics scene build failed");
        }

        static UnderwaterFluidVolume Volume(string name, Bounds bounds, UnderwaterPresence presence, Vector3 current, int x, int y, int z)
        {
            var go = GameObject.Find(name);
            if (!go) go = new GameObject(name);
            var volume = go.GetComponent<UnderwaterFluidVolume>();
            if (!volume) volume = go.AddComponent<UnderwaterFluidVolume>();
            volume.worldBounds = bounds;
            volume.ambientSource = presence;
            volume.ambientCurrent = current;
            volume.resolutionX = x; volume.resolutionY = y; volume.resolutionZ = z;
            volume.viscosity = 0.18f; volume.densityDecay = 0.06f; volume.ambientBlend = 0.28f;
            volume.simulationHz = 10; volume.pressureIterations = 4; volume.drawGizmos = false;
            EditorUtility.SetDirty(volume);
            return volume;
        }

        static void AddHydrodynamics(string objectName, UnderwaterFluidVolume volume, UnderwaterPresence presence)
        {
            var go = GameObject.Find(objectName);
            if (!go) throw new Exception("Missing comparison object " + objectName);
            var model = go.GetComponent<UnderwaterHydrodynamics>();
            if (!model) model = go.AddComponent<UnderwaterHydrodynamics>();
            model.presence = presence; model.volume = volume; model.body = go.GetComponent<Rigidbody>();
            model.applyForces = false; model.applyBuoyancy = false;
            model.displacedVolume = 0.065f; model.fluidDensity = objectName.Contains("Seabed") ? 1025 : 1000;
            model.frontalArea = 0.34f; model.lateralArea = 0.48f; model.verticalArea = 0.30f;
            model.centerOfBuoyancy = new Vector3(0, 1.3f, 0);
            EditorUtility.SetDirty(model);
        }

        static void BuildReliefAndObstacles()
        {
            var terrain = GameObject.Find("Sand Terrain - Teleportation Area");
            if (terrain)
            {
                var source = terrain.GetComponent<MeshFilter>().sharedMesh;
                string path = Root + "/Materials/SeabedTerrain_HydrodynamicRelief.asset";
                string basePath = Root + "/Materials/SeabedTerrain_HydrodynamicRelief_Base.asset";
                var baseMesh = AssetDatabase.LoadAssetAtPath<Mesh>(basePath);
                if (!baseMesh)
                {
                    baseMesh = UnityEngine.Object.Instantiate(source); baseMesh.name = "Seabed Terrain Base for Relief Upgrade";
                    AssetDatabase.CreateAsset(baseMesh, basePath);
                }
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (!mesh)
                {
                    mesh = UnityEngine.Object.Instantiate(baseMesh); mesh.name = "Seabed Terrain Hydrodynamic Relief";
                    AssetDatabase.CreateAsset(mesh, path);
                }
                var previousVertices=mesh.vertices;var vertices = baseMesh.vertices;
                // Keep the ROV installation and observer spawn level while making
                // the surrounding dunes readable through near-field parallax.
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = vertices[i];
                    float pad=Mathf.SmoothStep(0,1,(new Vector2(p.x,p.z+2).magnitude-4)/3);
                    float spawn=Mathf.SmoothStep(0,1,(new Vector2(p.x,p.z+8).magnitude-2.5f)/2.5f);
                    float edge=Mathf.SmoothStep(0,1,Mathf.Min(12.5f-Mathf.Abs(p.x),25-Mathf.Abs(p.z))/2);
                    float broad=0.55f*(1+Mathf.Sin(p.x*0.38f+p.z*0.15f))+0.3f*(1+Mathf.Cos(p.z*0.31f-p.x*0.2f));
                    float ridge=0.45f*Mathf.Exp(-Mathf.Pow((p.x+6)/2.5f,2))*Mathf.Sin(p.z*0.3f);
                    vertices[i].y=p.y+pad*spawn*edge*(broad+ridge);
                }
                foreach(var item in Object.FindObjectsOfType<Transform>().Where(t=>t.name=="Weathered Boulder"||t.name=="Seagrass Patch"))
                {
                    Vector3 p=terrain.transform.InverseTransformPoint(item.position);
                    int ix=Mathf.Clamp(Mathf.RoundToInt((p.x+12.5f)*4),0,100),iz=Mathf.Clamp(Mathf.RoundToInt((p.z+25)*4),0,200);
                    int at=iz*101+ix;
                    if(at<vertices.Length && at<previousVertices.Length)item.position+=Vector3.up*(vertices[at].y-previousVertices[at].y);
                }
                mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
                terrain.GetComponent<MeshFilter>().sharedMesh = mesh;
                var collider = terrain.GetComponent<MeshCollider>(); if (collider) { collider.sharedMesh = null; collider.sharedMesh = mesh; }
            }
            var root = GameObject.Find("Hydrodynamic Obstacle Course");
            if (!root) root = new GameObject("Hydrodynamic Obstacle Course");
            root.transform.position = new Vector3(62, 0, 0);
            var rock = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/ReefLimestone.mat");
            var metal = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/LadderSteel.mat");
            AddPrimitive(root.transform, "Obstacle Pipe Gate Left", PrimitiveType.Cylinder, new Vector3(-7.0f, 2.2f, 8), new Vector3(0.22f, 2.2f, 0.22f), metal, Vector3.zero);
            AddPrimitive(root.transform, "Obstacle Pipe Gate Right", PrimitiveType.Cylinder, new Vector3(-3.0f, 2.2f, 8), new Vector3(0.22f, 2.2f, 0.22f), metal, Vector3.zero);
            AddPrimitive(root.transform, "Obstacle Pipe Gate Header", PrimitiveType.Cylinder, new Vector3(-5.0f, 4.3f, 8), new Vector3(0.22f, 2.0f, 0.22f), metal, new Vector3(0, 0, 90));
            AddPrimitive(root.transform, "Obstacle Low Tunnel", PrimitiveType.Cube, new Vector3(6, 2.8f, 7), new Vector3(5.5f, 0.35f, 1.2f), rock, Vector3.zero);
            AddPrimitive(root.transform, "Obstacle Tunnel Support Left", PrimitiveType.Cube, new Vector3(3.4f, 1.4f, 7), new Vector3(0.3f, 2.8f, 1.2f), rock, Vector3.zero);
            AddPrimitive(root.transform, "Obstacle Tunnel Support Right", PrimitiveType.Cube, new Vector3(8.6f, 1.4f, 7), new Vector3(0.3f, 2.8f, 1.2f), rock, Vector3.zero);
            AddPrimitive(root.transform, "Obstacle Survey Marker", PrimitiveType.Cube, new Vector3(6, 0.9f, 12), new Vector3(0.65f, 0.9f, 0.65f), rock, Vector3.zero);
            AddPrimitive(root.transform, "Obstacle Pipe Inspection", PrimitiveType.Cylinder, new Vector3(0, 0.75f, 15), new Vector3(0.65f, 2.3f, 0.65f), metal, new Vector3(90, 0, 0));
        }

        static GameObject AddPrimitive(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 scale, Material material, Vector3 euler)
        {
            var existing = parent.Find(name);
            if (existing) return existing.gameObject;
            var go = GameObject.CreatePrimitive(type); go.name = name; go.layer = 8; go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition; go.transform.localScale = scale; go.transform.localRotation = Quaternion.Euler(euler);
            var renderer = go.GetComponent<Renderer>(); if (renderer && material) renderer.sharedMaterial = material;
            return go;
        }
    }
}
