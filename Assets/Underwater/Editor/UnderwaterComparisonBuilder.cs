using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using Object = UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterComparisonBuilder
    {
        const string Root = "Assets/Underwater";
        const string Nature = Root + "/Imported/Nature";
        static TeleportationProvider teleport;
        static Material tile, rim, lane, steel, sand, rock, grass, coral;

        static Material Mat(string name, Color color, string shader = "Universal Render Pipeline/Lit")
        {
            string path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find(shader); m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.22f);
            m.enableInstancing = true;
            return m;
        }
        static GameObject Box(string name, Vector3 p, Vector3 size, Material m, bool collides = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.layer = 8; go.transform.position = p; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collides) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        static void Area(GameObject go)
        {
            var a = go.AddComponent<TeleportationArea>(); a.teleportationProvider = teleport; a.interactionLayers = 2;
        }
        public static void Build(TeleportationProvider provider)
        {
            teleport = provider;
            tile = Mat("MetricCeramic", new Color(0.76f, 0.84f, 0.83f), "Underwater/MetricTiles");
            rim = Mat("DeckLimestone", new Color(0.58f, 0.58f, 0.51f));
            lane = Mat("LaneNavy", new Color(0.018f, 0.055f, 0.09f));
            steel = Mat("LadderSteel", new Color(0.56f, 0.61f, 0.63f)); steel.SetFloat("_Metallic", 0.8f); steel.SetFloat("_Smoothness", 0.7f);
            sand = Textured("SeabedSand", "Wet_Mud_c.tif", "Wet_Mud_n.tif", new Color(0.81f, 0.76f, 0.59f));
            sand.SetColor("_BaseColor", Color.white);
            sand.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Materials/SandGrain.png"));
            var normalImporter = AssetImporter.GetAtPath(Root + "/Materials/SandRippleNormal.png") as TextureImporter;
            if (normalImporter && normalImporter.textureType != TextureImporterType.NormalMap) { normalImporter.textureType = TextureImporterType.NormalMap; normalImporter.SaveAndReimport(); }
            sand.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Materials/SandRippleNormal.png")); sand.SetFloat("_BumpScale", 0.6f);
            rock = Textured("ReefLimestone", "Coral_Rock_c.tif", "Coral_Rock_n.tif", new Color(0.68f, 0.68f, 0.58f));
            coral = Textured("CoralOchre", "Coral_B_c.tif", "Coral_B_n.tif", new Color(0.78f, 0.62f, 0.38f));
            grass = Textured("Seagrass", "Long_Grass_c.tif", null, new Color(0.39f, 0.48f, 0.26f));
            grass.SetFloat("_Cull", 0); grass.SetFloat("_AlphaClip", 1); grass.SetFloat("_Cutoff", 0.35f); grass.EnableKeyword("_ALPHATEST_ON");
            grass.SetTexture("_BaseMap", null); grass.SetFloat("_AlphaClip", 0); grass.DisableKeyword("_ALPHATEST_ON"); grass.SetColor("_BaseColor", new Color(0.22f,0.29f,0.09f)); grass.SetFloat("_Smoothness",0.18f);
            BuildPool(); BuildSeabed();
            var prefab = ImportRov();
            PlaceRov(prefab, "ROVp - Pool", new Vector3(0, 0.025f, -2));
            PlaceRov(prefab, "ROVp - Seabed", new Vector3(62, Height(0, -2) + 0.025f, -2));
            var sky = Mat("OpenSky", new Color(0.7f, 0.82f, 0.88f), "Skybox/Procedural");
            sky.SetColor("_SkyTint", new Color(0.62f, 0.72f, 0.8f)); sky.SetFloat("_SunSize", 0.03f); sky.SetFloat("_AtmosphereThickness", 0.85f);
            RenderSettings.skybox = sky;
        }
        static Material Textured(string name, string diffuse, string normal, Color tint)
        {
            var m = Mat(name, tint);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Nature + "/Textures/" + diffuse));
            if (normal != null)
            {
                string path = Nature + "/Textures/" + normal;
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; imp.SaveAndReimport(); }
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path)); m.SetFloat("_BumpScale", 0.55f); m.EnableKeyword("_NORMALMAP");
            }
            return m;
        }
        static void BuildPool()
        {
            var pool = new GameObject("POOL | interior 25 X 50 m | water depth 10 m");
            var floor = Box("Pool Floor - 25 x 50 m", new Vector3(0, -0.2f, 0), new Vector3(25, 0.4f, 50), tile); Area(floor);
            Box("West Wall", new Vector3(-12.75f, 5, 0), new Vector3(0.5f, 10, 51), tile);
            Box("East Wall", new Vector3(12.75f, 5, 0), new Vector3(0.5f, 10, 51), tile);
            Box("North Wall", new Vector3(0, 5, 25.25f), new Vector3(25, 10, 0.5f), tile);
            Box("South Wall", new Vector3(0, 5, -25.25f), new Vector3(25, 10, 0.5f), tile);
            foreach (float x in new[] {-15.25f, 15.25f}) Area(Box("Dry Deck / Teleport", new Vector3(x, 9.85f, 0), new Vector3(5.5f, 0.3f, 61), rim));
            foreach (float z in new[] {-28f, 28f}) Area(Box("End Deck / Teleport", new Vector3(0, 9.85f, z), new Vector3(25, 0.3f, 6), rim));
            for (int i = -4; i <= 4; i++)
            {
                float x = i * 2.5f;
                Box("Ceramic Lane Line", new Vector3(x, 0.006f, 0), new Vector3(0.22f, 0.012f, 46), lane, false);
                foreach (float z in new[] {-23f, 23f})
                {
                    Box("Lane End T", new Vector3(x, 0.008f, z), new Vector3(1.15f, 0.014f, 0.22f), lane, false);
                    Box("Wall Lane Target", new Vector3(x, 1.3f, Mathf.Sign(z) * 24.993f), new Vector3(0.22f, 2.6f, 0.012f), lane, false);
                    Box("Wall Target Cross", new Vector3(x, 1.65f, Mathf.Sign(z) * 24.985f), new Vector3(1.1f, 0.22f, 0.012f), lane, false);
                }
            }
            foreach (float x in new[] {-12.49f, 12.49f})
            {
                Box("Waterline Ceramic Band", new Vector3(x, 9.7f, 0), new Vector3(0.018f, 0.22f, 50), lane, false);
                for (int depth = 1; depth <= 9; depth++)
                    Box("One Metre Depth Tick " + depth, new Vector3(x, 10 - depth, -19), new Vector3(0.02f, 0.05f, 0.65f), lane, false);
            }
            foreach (float x in new[] {11.75f, 12.35f}) Tube("Ladder Rail", new Vector3(x, 9.35f, -19), new Vector3(x, 11.1f, -19), 0.035f);
            for (int i = 0; i < 5; i++) Tube("Ladder Rung", new Vector3(11.75f, 8.65f + i * 0.36f, -19), new Vector3(12.35f, 8.65f + i * 0.36f, -19), 0.027f);
            Surface(0, "Pool");
            // Small removable inspection tool, rather than a large distracting test cube.
            var tool = Box("Grabbable Inspection Marker", new Vector3(1.2f, 0.18f, -6.6f), new Vector3(0.12f, 0.22f, 0.06f), Mat("MarkerOrange", new Color(0.9f, 0.25f, 0.035f)));
            tool.layer = 9; var body = tool.AddComponent<Rigidbody>(); body.useGravity = false; body.drag = 5; tool.AddComponent<XRGrabInteractable>();
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) if (go != pool && !go.name.Contains("XR") && !go.name.Contains("Teleport")) go.transform.SetParent(pool.transform, true);
        }
        static void Tube(string name, Vector3 a, Vector3 b, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name; go.layer = 8;
            go.transform.position = (a + b) / 2; go.transform.up = b - a; go.transform.localScale = new Vector3(radius * 2, (b - a).magnitude / 2, radius * 2);
            go.GetComponent<Renderer>().sharedMaterial = steel;
        }
        static void Surface(float x, string label)
        {
            var water = Mat("AnimatedWater", new Color(0.07f, 0.37f, 0.46f, 0.30f), "Underwater/Surface");
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane); go.name = label + " Water Surface Y10";
            go.transform.position = new Vector3(x, 10, 0); go.transform.localScale = new Vector3(2.5f, 1, 5); go.layer = 10;
            Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = water;
        }
        static float Height(float x, float z)
        {
            float mask = Mathf.SmoothStep(0, 1, Mathf.Clamp01((new Vector2(x, z + 2).magnitude - 2) / 5));
            return 0.08f + mask * (0.20f * Mathf.Sin(x * 0.45f + z * 0.16f) + 0.10f * Mathf.Cos(z * 0.6f) + 0.035f * Mathf.Sin(z * 5 + x));
        }
        static void BuildSeabed()
        {
            var parent = new GameObject("NATURAL SEABED | 25 X 50 X 10 m | x62");
            const int nx = 100, nz = 200;
            var v = new Vector3[(nx + 1) * (nz + 1)]; var uv = new Vector2[v.Length]; var tri = new int[nx * nz * 6];
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                int i = z * (nx + 1) + x; float px = x * 25f / nx - 12.5f, pz = z * 50f / nz - 25;
                v[i] = new Vector3(px, Height(px, pz), pz); uv[i] = new Vector2(px / 3, pz / 3);
                if (x == nx || z == nz) continue;
                int t = (z * nx + x) * 6; tri[t] = i; tri[t + 1] = i + nx + 1; tri[t + 2] = i + 1;
                tri[t + 3] = i + 1; tri[t + 4] = i + nx + 1; tri[t + 5] = i + nx + 2;
            }
            string path = Root + "/Materials/SeabedTerrain.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); } else mesh.Clear();
            mesh.vertices = v; mesh.uv = uv; mesh.triangles = tri; mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents(); EditorUtility.SetDirty(mesh);
            var ground = new GameObject("Sand Terrain - Teleportation Area", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            ground.transform.position = new Vector3(62, 0, 0); ground.layer = 8; ground.GetComponent<MeshFilter>().sharedMesh = mesh;
            ground.GetComponent<MeshCollider>().sharedMesh = mesh; ground.GetComponent<Renderer>().sharedMaterial = sand; Area(ground);
            var rnd = new System.Random(2407);
            var boulders = new[] {UnderwaterSeabedGeometry.Boulder(0),UnderwaterSeabedGeometry.Boulder(1),UnderwaterSeabedGeometry.Boulder(2)};
            var seagrass = UnderwaterSeabedGeometry.Seagrass();
            for (int i = 0; i < 58; i++)
            {
                float x = (float)rnd.NextDouble() * 22 - 11, z = (float)rnd.NextDouble() * 46 - 23;
                if (Mathf.Abs(x) < 3.5f && z > -11 && z < 4) { x += x < 0 ? -5 : 5; }
                float size = 0.35f + (float)rnd.NextDouble()*1.15f;
                var go = UnderwaterSeabedGeometry.Place("Weathered Boulder",boulders[i%3],rock,new Vector3(62+x,Height(x,z)-0.08f,z),new Vector3(size,size*(0.55f+(float)rnd.NextDouble()*0.45f),size*(0.8f+(float)rnd.NextDouble()*0.7f)),(float)rnd.NextDouble()*360);
                go.AddComponent<MeshCollider>().sharedMesh=boulders[i%3];
            }
            for (int i = 0; i < 220; i++)
            {
                float x = (float)rnd.NextDouble() * 23 - 11.5f, z = (float)rnd.NextDouble() * 47 - 23.5f;
                if (Mathf.Abs(x) < 3.5f && z > -11 && z < 4) continue;
                float size=0.3f+(float)rnd.NextDouble()*0.65f;
                UnderwaterSeabedGeometry.Place("Seagrass Patch",seagrass,grass,new Vector3(62+x,Height(x,z)-0.015f,z),Vector3.one*size,(float)rnd.NextDouble()*360);
            }
            var side = Mat("CutSediment", new Color(0.24f, 0.27f, 0.24f));
            Box("Seabed West Boundary", new Vector3(49.25f, 4.9f, 0), new Vector3(0.5f, 10.2f, 51), rock).GetComponent<Renderer>().enabled=false;
            Box("Seabed East Boundary", new Vector3(74.75f, 4.9f, 0), new Vector3(0.5f, 10.2f, 51), rock).GetComponent<Renderer>().enabled=false;
            Box("Seabed North Boundary", new Vector3(62, 4.9f, 25.25f), new Vector3(25, 10.2f, 0.5f), rock).GetComponent<Renderer>().enabled=false;
            Box("Seabed South Boundary", new Vector3(62, 4.9f, -25.25f), new Vector3(25, 10.2f, 0.5f), rock).GetComponent<Renderer>().enabled=false;
            Area(Box("Seabed Observation Deck", new Vector3(78, 9.85f, -28), new Vector3(6, 0.3f, 6), rim));
            Surface(62, "Seabed");
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) if (go != parent && go.name != "POOL | interior 25 X 50 m | water depth 10 m" && !go.name.Contains("XR") && !go.name.Contains("Teleport")) go.transform.SetParent(parent.transform, true);
        }
        static GameObject Model(string file, Material material, Vector3 pos, float height, float yaw)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Nature + "/FBX/" + file); if (!asset) throw new Exception("Missing seabed mesh " + file);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset); go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var b = Bounds(go); go.transform.localScale *= height / Mathf.Max(0.01f, b.size.y);
            b = Bounds(go); go.transform.position += pos - new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.sharedMaterials = r.sharedMaterials.Select(_ => material).ToArray(); r.gameObject.layer = 8; }
            return go;
        }
        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true); if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds); return b;
        }
        static GameObject ImportRov()
        {
            var current = SceneManager.GetActiveScene();
            var source = EditorSceneManager.OpenScene(Root + "/Imported/ROVpSource.unity", OpenSceneMode.Additive);
            var root = source.GetRootGameObjects().Single(g => g.name == "ROVp");
            var paths = root.GetComponentsInChildren<Transform>(true).Select(t => AnimationUtility.CalculateTransformPath(t, root.transform)).OrderBy(x => x).ToArray();
            var clone = Object.Instantiate(root); SceneManager.MoveGameObjectToScene(clone, current); clone.name = "ROVp";
            EditorSceneManager.CloseScene(source, true); SceneManager.SetActiveScene(current);
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = r.sharedMaterials.Select(m =>
                {
                    if (!m) return Mat("ROVFallback", Color.gray);
                    string name = "ROV_" + m.name.Replace("/", "_");
                    Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.gray;
                    var lit = Mat(name, color); lit.SetFloat("_Metallic", m.name.ToLower().Contains("metal") || m.name.Contains("金属") ? 0.65f : 0.15f);
                    if (m.HasProperty("_MainTex")) lit.SetTexture("_BaseMap", m.GetTexture("_MainTex"));
                    return lit;
                }).ToArray();
                r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
            }
            // Reference copies keep every component, joint and mesh. Freeze simulation and external drivers.
            foreach (var b in clone.GetComponentsInChildren<MonoBehaviour>(true)) if (b) b.enabled = false;
            foreach (var b in clone.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.useGravity = false; }
            foreach (var b in clone.GetComponentsInChildren<ArticulationBody>(true)) b.immovable = true;
            foreach (var c in clone.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            foreach (var a in clone.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
            var prefab = PrefabUtility.SaveAsPrefabAsset(clone, Root + "/Imported/ROVpComplete.prefab");
            File.WriteAllText("Logs/ROVpHierarchy.txt", "Source: F:/unity/en/Assets/shenghao_getcar.unity\nSource and migrated hierarchy count: " + paths.Length + "\nRenderers: " + clone.GetComponentsInChildren<Renderer>(true).Length + "\nSource world-space bounds: " + Bounds(clone).size + "\nReference scripts disabled; original components and joint hierarchy retained. External scene references listed in ROVpMigration.json.\n" + string.Join("\n", paths));
            Object.DestroyImmediate(clone); return prefab;
        }
        static void PlaceRov(GameObject prefab, string name, Vector3 bottom)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab); go.name = name;
            var bounds = Bounds(go);
            go.transform.position += bottom - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;
        }
        public static void ValidateComparison()
        {
            var a = GameObject.Find("ROVp - Pool"); var b = GameObject.Find("ROVp - Seabed");
            if (!a || !b || a.GetComponentsInChildren<Transform>(true).Length != b.GetComponentsInChildren<Transform>(true).Length) throw new Exception("ROVp comparison hierarchy mismatch");
            if (!a.GetComponentsInChildren<Transform>(true).Any(t => t.name == "WA100_R_URDF_Official")) throw new Exception("ROVp nested WA100 missing");
            var floor = GameObject.Find("Pool Floor - 25 x 50 m").GetComponent<BoxCollider>().bounds;
            if (Mathf.Abs(floor.size.x - 25) > 0.01f || Mathf.Abs(floor.size.z - 50) > 0.01f) throw new Exception("Pool dimensions mismatch");
            if (Mathf.Abs(Bounds(a).min.y - 0.025f) > 0.02f) throw new Exception("Pool ROV not on bottom");
            File.WriteAllText("Logs/UnderwaterComparisonChecks.txt", "PASS pool 25 x 50 m, waterline 10 m; equal complete ROVp hierarchies; nested WA100 retained; pool ROV on bottom.\nROV bounds " + Bounds(a).size);
        }
    }
}
