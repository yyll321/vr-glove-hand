using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterResearchSceneBuilder
    {
        public const string ScenePath="Assets/Underwater/Scenes/UnderwaterResearchEditable.unity";
        const string Root="Assets/Underwater";
        [MenuItem("Tools/Underwater/Research/1 Create or Open Editable Research Scene")]
        public static void Create()
        {
            if(File.Exists(ScenePath)){Open();return;}
            var scene=EditorSceneManager.OpenScene(UnderwaterBaselineBuilder.ScenePath);
            EditorSceneManager.SaveScene(scene,ScenePath);
            // Unpack scene instances once. All child transforms, geometry and material slots remain editable.
            foreach(var go in scene.GetRootGameObjects())
                if(PrefabUtility.IsPartOfPrefabInstance(go))PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            Group("Pool_Structure",t=>new[]{"Pool Floor","West Wall","East Wall","North Wall","South Wall","Dry Deck","End Deck"}.Any(s=>t.name.StartsWith(s)));
            Group("Pool_Accessories",t=>new[]{"Ceramic Lane","Lane End","Wall Lane","Wall Target","Waterline","One Metre","Ladder","Grabbable"}.Any(s=>t.name.StartsWith(s)));
            var pool=GameObject.Find("POOL | interior 25 X 50 m | water depth 10 m");if(pool)pool.name="Pool_50x25x10m";
            var nature=GameObject.Find("NATURAL SEABED | 25 X 50 X 10 m | x62");if(nature)nature.name="NaturalSeabed_50x25x10m";
            var roots=new GameObject("ROVp_References");
            foreach(var go in scene.GetRootGameObjects().Where(g=>g.name.StartsWith("ROVp -")))go.transform.SetParent(roots.transform,true);
            var env=Object.FindObjectOfType<UnderwaterEnvironment>();
            env.budgets=new[]{2400,3600,4800};env.quality=1;
            float[] min={0.002f,0.0025f,0.003f},max={0.006f,0.007f,0.008f},radius={3.5f,7,12};
            for(int i=0;i<env.snow.Length;i++)
            {
                var ps=env.snow[i];ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.startSize=new ParticleSystem.MinMaxCurve(min[i],max[i]);main.maxParticles=env.budgets[i];main.startColor=new Color(0.75f,0.88f,0.86f,i==0?0.28f:0.18f);
                var shape=ps.shape;shape.scale=new Vector3(radius[i]*2,8,radius[i]*2);
                var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.maxParticleSize=0.003f;renderer.minParticleSize=0;
            }
            var research=new GameObject("Research_Systems (waves, caustics, acoustics)");
            var settings=AssetDatabase.LoadAssetAtPath<UnderwaterWaveSettings>(Root+"/Settings/GerstnerWaves.asset");
            if(!settings){settings=ScriptableObject.CreateInstance<UnderwaterWaveSettings>();AssetDatabase.CreateAsset(settings,Root+"/Settings/GerstnerWaves.asset");}
            var caustics=research.AddComponent<UnderwaterCaustics>();caustics.settings=settings;caustics.sun=Object.FindObjectsOfType<Light>().First(l=>l.type==LightType.Directional);
            caustics.normalShader=Shader.Find("Underwater/GerstnerNormal");caustics.mapShader=Shader.Find("Underwater/AreaCausticMap");
            foreach(var surface in Object.FindObjectsOfType<MeshRenderer>().Where(r=>r.name.Contains("Water Surface")))
            {
                surface.GetComponent<MeshFilter>().sharedMesh=WaterMesh();
                // Dedicated mesh uses metres, so reset the inherited primitive plane scaling.
                surface.transform.localScale=Vector3.one;
                var material=new Material(surface.sharedMaterial);string name=surface.name.StartsWith("Pool")?"PoolGerstnerWater":"SeaGerstnerWater";
                AssetDatabase.CreateAsset(material,Root+"/Materials/"+name+".mat");surface.sharedMaterial=material;
                material.SetFloat("_WaveCenter",surface.transform.position.x);material.SetFloat("_Refraction",0.012f);
            }
            var camera=Object.FindObjectOfType<UnderwaterMotor>().origin.Camera;
            CreateAcoustics("Pool_Acoustics",0,camera.transform,research.transform,false);
            CreateAcoustics("Seabed_Acoustics",62,camera.transform,research.transform,true);
            AddPoolAssets();
            var tests=research.AddComponent<UnderwaterResearchChecks>();tests.caustics=caustics;
            var smoke=Object.FindObjectOfType<UnderwaterSmokeRun>();if(smoke)smoke.enabled=false;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
            var scenes=EditorBuildSettings.scenes.Where(s=>s.path!=ScenePath).ToList();scenes.Insert(0,new EditorBuildSettingsScene(ScenePath,true));EditorBuildSettings.scenes=scenes.ToArray();
            AssetDatabase.SaveAssets();Open();Validate();
        }
        static void Group(string name,Func<Transform,bool> predicate)
        {
            var group=new GameObject(name);
            foreach(var t in Object.FindObjectsOfType<Transform>().Where(predicate).ToArray())t.SetParent(group.transform,true);
        }
        static Mesh WaterMesh()
        {
            string path=Root+"/Materials/GerstnerSurfaceGrid.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh)return mesh;
            const int nx=100,nz=200;var vertices=new Vector3[(nx+1)*(nz+1)];var indices=new int[nx*nz*6];
            for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
            {int i=z*(nx+1)+x;vertices[i]=new Vector3(x*25f/nx-12.5f,0,z*50f/nz-25);if(x==nx||z==nz)continue;int t=(z*nx+x)*6;indices[t]=i;indices[t+1]=i+nx+1;indices[t+2]=i+1;indices[t+3]=i+1;indices[t+4]=i+nx+1;indices[t+5]=i+nx+2;}
            mesh=new Mesh{name="Editable Gerstner water grid"};mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateNormals();mesh.bounds=new Bounds(Vector3.zero,new Vector3(28,2,53));AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static void CreateAcoustics(string name,float x,Transform listener,Transform parent,bool sea)
        {
            var go=new GameObject(name);go.transform.SetParent(parent);
            var model=go.AddComponent<UnderwaterAcousticModel>();model.volumeCenter=new Vector3(x,5,0);model.receiver=listener;
            model.layers=sea?new[]{new AcousticWaterLayer{topDepth=0,bottomDepth=3,temperature=20,soundSpeed=1520},new AcousticWaterLayer{topDepth=3,bottomDepth=7,temperature=16,soundSpeed=1508},new AcousticWaterLayer{topDepth=7,bottomDepth=10,temperature=12,soundSpeed=1495}}:new[]{new AcousticWaterLayer{temperature=25,salinity=0,soundSpeed=1497,density=997}};
            var material=go.AddComponent<UnderwaterAcousticMaterial>();material.material=sea?UnderwaterAcousticMaterial.Preset.Sand:UnderwaterAcousticMaterial.Preset.Concrete;model.defaultBottom=material;
            string rov=sea?"ROVp - Seabed":"ROVp - Pool";var root=GameObject.Find(rov);
            var source=new GameObject("Acoustic_Source (move to test propagation)");source.transform.SetParent(root.transform,true);source.transform.position=new Vector3(x-0.7f,1.6f,-4.05f);model.source=source.transform;
            foreach(var collider in root.GetComponentsInChildren<Collider>(true))if(!collider.GetComponent<UnderwaterAcousticMaterial>()){var m=collider.gameObject.AddComponent<UnderwaterAcousticMaterial>();m.material=UnderwaterAcousticMaterial.Preset.Metal;}
            if(sea)
            {
                var terrain=GameObject.Find("Sand Terrain - Teleportation Area").AddComponent<UnderwaterAcousticMaterial>();terrain.material=UnderwaterAcousticMaterial.Preset.Sand;
                foreach(var boulder in Object.FindObjectsOfType<MeshRenderer>().Where(r=>r.name=="Weathered Boulder")){var m=boulder.gameObject.AddComponent<UnderwaterAcousticMaterial>();m.material=UnderwaterAcousticMaterial.Preset.Limestone;}
            }
            var audio=go.AddComponent<UnderwaterAcousticAudio>();audio.model=model;audio.signal=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/ROVMotorAndPing.wav");
        }
        static void AddPoolAssets()
        {
            var parent=GameObject.Find("Pool_Accessories").transform;
            var steel=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/LadderSteel.mat");
            foreach(float z in new[]{-16f,0,16})foreach(float x in new[]{-12.45f,12.45f})
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name="Pool Wall Lamp Housing";go.transform.SetParent(parent,true);go.transform.position=new Vector3(x,6.5f,z);go.transform.rotation=Quaternion.Euler(0,0,90);go.transform.localScale=new Vector3(0.4f,0.035f,0.4f);go.layer=8;go.GetComponent<Renderer>().sharedMaterial=steel;
            }
            foreach(float x in new[]{-10f,-5f,0f,5f,10f})
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Floor Drain Grating";go.transform.SetParent(parent,true);go.transform.position=new Vector3(x,0.009f,24.3f);go.transform.localScale=new Vector3(0.45f,0.018f,0.18f);go.layer=8;go.GetComponent<Renderer>().sharedMaterial=steel;
            }
        }
        [MenuItem("Tools/Underwater/Research/2 Open Editable Scene and Show Objects")]
        public static void Open()
        {
            if(SceneManager.GetActiveScene().path!=ScenePath)EditorSceneManager.OpenScene(ScenePath);
            ClearHierarchyFilter();
            var rov=GameObject.Find("ROVp - Pool");Selection.activeGameObject=rov;
            if(SceneView.lastActiveSceneView && rov)SceneView.lastActiveSceneView.LookAt(new Vector3(0,1.3f,-2),Quaternion.Euler(18,25,0),8);
            EditorApplication.RepaintHierarchyWindow();
        }
        static void ClearHierarchyFilter()
        {
            var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
            foreach(var window in Resources.FindObjectsOfTypeAll(type))
            {
                var method=type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).FirstOrDefault(m=>m.Name=="SetSearchFilter" && m.GetParameters().Length>0 && m.GetParameters()[0].ParameterType==typeof(string));
                if(method==null)continue;
                var args=method.GetParameters().Select(p=>p.ParameterType==typeof(string)?(object)"":p.ParameterType.IsValueType?Activator.CreateInstance(p.ParameterType):null).ToArray();
                try{method.Invoke(window,args);}catch(Exception e){Debug.LogWarning("Clear Hierarchy search with its X button: "+e.Message);}
            }
        }
        [MenuItem("Tools/Underwater/Research/3 Validate and Build Current Editable Scene")]
        public static void Build()
        {
            Open();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());Validate();
            Directory.CreateDirectory("Builds/UnderwaterResearch");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},target=BuildTarget.StandaloneWindows64,locationPathName="Builds/UnderwaterResearch/UnderwaterResearch.exe",options=BuildOptions.Development});
            File.WriteAllText("Logs/UnderwaterResearchBuild.txt",$"{result.summary.result}\nErrors: {result.summary.totalErrors}\nWarnings: {result.summary.totalWarnings}\n");
            if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Research build failed");
        }
        public static void Validate()
        {
            var scene=SceneManager.GetActiveScene();
            foreach(var t in Object.FindObjectsOfType<Transform>(true))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script "+t.name);
            foreach(var name in new[]{"ROVp - Pool","ROVp - Seabed","Pool_Structure","Pool_Accessories","NaturalSeabed_50x25x10m"})if(!GameObject.Find(name))throw new Exception("Missing editable object "+name);
            foreach(var r in Object.FindObjectsOfType<MeshFilter>(true).Where(r=>r.transform.root.name=="ROVp_References"))if(!r.sharedMesh)throw new Exception("Missing ROV mesh "+r.name);
            var caustic=Object.FindObjectOfType<UnderwaterCaustics>();if(!caustic || !caustic.settings || !caustic.mapShader || !caustic.normalShader)throw new Exception("Caustic shader configuration");
            foreach(var shader in new[]{caustic.normalShader,caustic.mapShader,Shader.Find("Underwater/Surface"),Shader.Find("Underwater/DepthFog")})
                if(UnityEditor.ShaderUtil.ShaderHasError(shader))throw new Exception("Shader compilation failed: "+shader.name);
            var models=Object.FindObjectsOfType<UnderwaterAcousticModel>();if(models.Length!=2 || models.Any(m=>!m.source||!m.receiver))throw new Exception("Acoustic reference configuration");
            foreach(var model in models)
            {float depth=0;foreach(var layer in model.layers){if(Mathf.Abs(layer.topDepth-depth)>0.001f || layer.bottomDepth<=layer.topDepth)throw new Exception("Non-contiguous acoustic layers");depth=layer.bottomDepth;}if(Mathf.Abs(depth-10)>0.001f)throw new Exception("Acoustic layers must span 10 m");}
            File.WriteAllText("Logs/UnderwaterResearchObjects.txt",scene.path+"\n"+string.Join("\n",scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>AnimationUtility.CalculateTransformPath(t,null)))+"\nPASS: persistent editable hierarchy, ROV meshes, no missing scripts, wave shaders, two acoustic volumes and contiguous layers.");
        }
    }
}
