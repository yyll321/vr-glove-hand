using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterDenseSurveyUpgrade
    {
        const string AssetRoot="Assets/Underwater/RadarSurvey/";
        const string GeneratedName="Generated Survey Reefs - dense layout";
        [Serializable] public sealed class Item
        {
            public string name,mesh,material;public float[] position,scale;public float yaw,radius;public int zone;
        }
        [Serializable] public sealed class Layout
        {
            public int seed=20261028;public float[] seabedSize={50,80};public float protectedRovRadius=3;public Item[] items;
        }
        [MenuItem("Tools/Underwater/Research/20 Apply Dense Reefs, ROV Motion and Radar Module")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!=UnderwaterRadarSurveyTools.ScenePath)
                throw new Exception("Open the radar survey scene and exit Play first");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),"Assets/Underwater/Backups/Survey-beforeDense-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            var motor=Object.FindObjectOfType<UnderwaterMotor>();
            var rovs=new[]{GameObject.Find("ROVp - Pool"),GameObject.Find("ROVp - Seabed")};
            if(rovs.Any(r=>!r))throw new Exception("Reference ROV missing");
            foreach(var rov in rovs)
            {
                var movement=rov.GetComponent<UnderwaterRovMovement>()??rov.AddComponent<UnderwaterRovMovement>();
                movement.motor=motor;movement.seabed=rov.name.Contains("Seabed");
                movement.bodyAxesCorrection=rov.GetComponent<UnderwaterHydrodynamics>().bodyAxesCorrection;EditorUtility.SetDirty(movement);
            }
            var existing=GameObject.Find(GeneratedName)??GameObject.Find("Generated Survey Reefs - seed 20261027");if(existing)Object.DestroyImmediate(existing);
            var root=new GameObject(GeneratedName);var random=new System.Random(20261028);var items=new List<Item>();var circles=new List<Vector3>();
            var rovPosition=rovs[1].transform.position;
            HandFootprint(rovs[1],out var handCentre,out float handRadius);
            var manufactured=new List<Vector3>();
            for(int i=0;i<18;i++)
            {
                float angle=((i/3)*60+(i%3)*19+27)*Mathf.Deg2Rad,distance=6+(i%3)*4;
                var p=rovPosition+new Vector3(Mathf.Cos(angle)*distance,0,Mathf.Sin(angle)*distance);
                if(Mathf.Abs(p.x-rovPosition.x)<2.7f && p.z>rovPosition.z-11 && p.z<rovPosition.z+8)p.x+=p.x>rovPosition.x?3:-3;
                manufactured.Add(p);
            }
            var terrain=GameObject.Find("Sand Terrain - Teleportation Area").GetComponent<Collider>();
            Mesh hoop=ArcMesh("SurveyHoop",Mathf.PI*2,1.35f,.14f);
            Mesh arch=ArcMesh("SurveyArch",Mathf.PI,1.85f,.18f);
            Mesh bollard=ArcMesh("SurveyBollard",Mathf.PI*2,.32f,.24f);
            for(int i=0;i<180;i++)
            {
                bool near=i<140;float x=0,z=0,radius=0,sx=0,sy=0,sz=0;bool found=false;
                for(int attempt=0;attempt<15000;attempt++)
                {
                    float angle=Range(random,0,Mathf.PI*2),distance=near?Mathf.Sqrt(Range(random,16,324)):Range(random,19,35);
                    x=rovPosition.x+Mathf.Cos(angle)*distance;z=rovPosition.z+Mathf.Sin(angle)*distance;
                    sx=near?Range(random,.4f,1.05f):Range(random,.8f,1.65f);sz=sx*Range(random,.65f,1.4f);sy=Range(random,.65f,2.1f);radius=Mathf.Max(sx,sz);
                    if(x-radius<38 || x+radius>86 || Mathf.Abs(z)+radius>39)continue;
                    if(Vector2.Distance(new Vector2(x,z),new Vector2(rovPosition.x,rovPosition.z))<radius+3)continue;
                    if(Vector2.Distance(new Vector2(x,z),new Vector2(62,-8))<radius+2.5f)continue;
                    if(Vector2.Distance(new Vector2(x,z),handCentre)<radius+handRadius)continue;
                    // Preserve approach lane behind ROV and a short calibration lane ahead.
                    if(Mathf.Abs(x-rovPosition.x)<radius+1.3f && z>rovPosition.z-11 && z<rovPosition.z+8)continue;
                    if(manufactured.Any(p=>Vector2.Distance(new Vector2(x,z),new Vector2(p.x,p.z))<radius+2.1f))continue;
                    if(circles.Any(c=>Vector2.Distance(new Vector2(x,z),new Vector2(c.x,c.y))<radius+c.z+.18f))continue;
                    found=true;break;
                }
                if(!found)throw new Exception("Could not pack dense reef layout; original saved in backup");
                circles.Add(new Vector3(x,z,radius));int variant=random.Next(12);var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(AssetRoot+$"ReefVariant{variant:00}.asset");
                float floor=Floor(terrain,x,z),y=floor-mesh.bounds.min.y*sy-.2f*sy;
                var item=MakeItem($"Reef {i+1:000} - {(near?"ROV near field":"outer field")}",mesh,AssetRoot+$"ReefTone{random.Next(3)}.mat",new Vector3(x,y,z),new Vector3(sx,sy,sz),Range(random,0,360));
                item.zone=near?1:2;item.radius=radius;items.Add(item);InstantiateItem(item,root.transform);
            }
            // A mix of recognisable manufactured shapes around, not under, the ROV.
            // Stagger their polar positions; no repeated row of cube obstacles.
            for(int i=0;i<18;i++)
            {
                float x=manufactured[i].x,z=manufactured[i].z;
                Mesh shape=i%4==0?AssetDatabase.LoadAssetAtPath<Mesh>(AssetRoot+"BentInspectionPipe.asset"):i%4==1?hoop:i%4==2?arch:bollard;
                var scale=i%4==0?new Vector3(.7f,.7f,.35f):Vector3.one;
                float floor=Floor(terrain,x,z),y=floor-shape.bounds.min.y*scale.y+.03f;
                string name=new[]{"Bent pipeline","Inspection hoop","Pipe arch","Mooring ring"}[i%4];
                float yaw=Range(random,0,360);var offset=Quaternion.Euler(0,yaw,0)*Vector3.Scale(shape.bounds.center,scale);offset.y=0;
                var item=MakeItem(name+" "+(i+1),shape,"Assets/Underwater/Materials/LadderSteel.mat",new Vector3(x,y,z)-offset,scale,yaw);
                items.Add(item);InstantiateItem(item,root.transform);
            }
            // Put original pool ROV within reach of a small reference inspection course.
            var oldPool=GameObject.Find("Pool ROV Inspection Obstacles");if(oldPool)Object.DestroyImmediate(oldPool);
            var pool=new GameObject("Pool ROV Inspection Obstacles");
            for(int i=0;i<8;i++)
            {
                float angle=(i*45+22)*Mathf.Deg2Rad;var p=rovs[0].transform.position+new Vector3(Mathf.Cos(angle)*6,0,Mathf.Sin(angle)*6);
                var shape=i%2==0?hoop:arch;p.y=-shape.bounds.min.y+.03f;
                InstantiateItem(MakeItem("Pool inspection "+(i+1),shape,"Assets/Underwater/Materials/LadderSteel.mat",p,Vector3.one,i*29),pool.transform);
            }
            var seaVolume=GameObject.Find("Seabed Fluid Volume").GetComponent<UnderwaterFluidVolume>();seaVolume.obstacles=root.GetComponentsInChildren<Collider>();
            var poolVolume=GameObject.Find("Pool Fluid Volume").GetComponent<UnderwaterFluidVolume>();poolVolume.obstacles=pool.GetComponentsInChildren<Collider>();
            EditorUtility.SetDirty(seaVolume);EditorUtility.SetDirty(poolVolume);
            File.WriteAllText(AssetRoot+"SurveyLayout.json",JsonUtility.ToJson(new Layout{items=items.ToArray()},true));
            var systemGo=GameObject.Find("GeometryRadar_System")??new GameObject("GeometryRadar_System");
            var system=systemGo.GetComponent<UnderwaterRadarSystem>()??systemGo.AddComponent<UnderwaterRadarSystem>();
            system.radars=Object.FindObjectsOfType<UnderwaterGeometryRadar>(true);
            foreach(var radar in system.radars){radar.transform.SetParent(systemGo.transform,true);radar.StopScan();EditorUtility.SetDirty(radar);}
            if(!system.calibrationTarget)
            {
                var board=GameObject.CreatePrimitive(PrimitiveType.Cube);board.name="CalibrationTarget_5m - enable using radar panel";board.layer=8;
                board.transform.SetParent(systemGo.transform,false);board.transform.localScale=new Vector3(2.4f,2.4f,.12f);
                board.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(AssetRoot+"ReefTone2.mat");
                system.calibrationTarget=board.transform;board.SetActive(false);
            }
            EditorUtility.SetDirty(system);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.Refresh();AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/RadarReview");File.WriteAllText("Logs/RadarReview/dense-upgrade.txt","Saved 180 reefs (140 within 18 m of ROV), 18 manufactured targets and 8 pool targets. GeometryRadar_System owns both scanners, ROS and displays. "+DateTime.Now.ToString("O"));
        }
        static float Range(System.Random r,float min,float max)=>Mathf.Lerp(min,max,(float)r.NextDouble());
        static void HandFootprint(GameObject rov,out Vector2 centre,out float radius)
        {
            var guard=rov.GetComponentInChildren<Wa100VisibilityGuard>(true);
            var meshes=guard?guard.GetComponentsInChildren<MeshFilter>(true):Array.Empty<MeshFilter>();
            Bounds bounds=new Bounds();bool first=true;
            foreach(var filter in meshes)if(filter.sharedMesh)
            {
                var b=filter.sharedMesh.bounds;
                for(int i=0;i<8;i++)
                {
                    var p=filter.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                    if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
                }
            }
            centre=first?new Vector2(rov.transform.position.x,rov.transform.position.z):new Vector2(bounds.center.x,bounds.center.z);
            radius=first?1:new Vector2(bounds.extents.x,bounds.extents.z).magnitude+.45f;
        }
        [MenuItem("Tools/Underwater/Research/21 Repair Initial Hand Reef Clearance")]
        public static void RepairHandClearance()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!=UnderwaterRadarSurveyTools.ScenePath)
                throw new Exception("Open the radar survey scene and exit Play first");
            var rov=GameObject.Find("ROVp - Seabed");HandFootprint(rov,out var hand,out float clearance);
            var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(AssetRoot+"SurveyLayout.json"));
            var terrain=GameObject.Find("Sand Terrain - Teleportation Area").GetComponent<Collider>();
            var root=GameObject.Find(GeneratedName);var random=new System.Random(layout.seed+1);int moved=0;
            foreach(var item in layout.items.Where(i=>i.name.StartsWith("Reef ")))
            {
                if(Vector2.Distance(new Vector2(item.position[0],item.position[2]),hand)>=item.radius+clearance)continue;
                bool found=false;
                for(int attempt=0;attempt<20000;attempt++)
                {
                    float angle=Range(random,0,Mathf.PI*2),distance=Mathf.Sqrt(Range(random,16,324));
                    float x=rov.transform.position.x+Mathf.Cos(angle)*distance,z=rov.transform.position.z+Mathf.Sin(angle)*distance,r=item.radius;
                    var p=new Vector2(x,z);
                    if(x-r<38 || x+r>86 || Mathf.Abs(z)+r>39 || Vector2.Distance(p,hand)<r+clearance ||
                        Vector2.Distance(p,new Vector2(62,-8))<r+2.5f || Mathf.Abs(x-rov.transform.position.x)<r+1.3f && z>rov.transform.position.z-11 && z<rov.transform.position.z+8)continue;
                    if(layout.items.Any(other=>other!=item && Vector2.Distance(p,new Vector2(other.position[0],other.position[2]))<r+(other.name.StartsWith("Reef ")?other.radius+.18f:2.1f)))continue;
                    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(item.mesh);
                    item.position=new[]{x,Floor(terrain,x,z)-mesh.bounds.min.y*item.scale[1]-.2f*item.scale[1],z};found=true;break;
                }
                if(!found)throw new Exception("No free location for "+item.name);
                var child=root.transform.Find(item.name);Undo.RecordObject(child,"Clear initial WA100 hand footprint");
                child.position=new Vector3(item.position[0],item.position[1],item.position[2]);moved++;
            }
            File.WriteAllText(AssetRoot+"SurveyLayout.json",JsonUtility.ToJson(layout,true));
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.Refresh();Debug.Log("Initial hand reef clearance repaired: "+moved+" relocated, density unchanged");
        }
        static float Floor(Collider ground,float x,float z)=>ground.Raycast(new Ray(new Vector3(x,12,z),Vector3.down),out var hit,20)?hit.point.y:0;
        static Item MakeItem(string name,Mesh mesh,string mat,Vector3 p,Vector3 s,float yaw)=>new Item{name=name,mesh=AssetDatabase.GetAssetPath(mesh),material=mat,position=new[]{p.x,p.y,p.z},scale=new[]{s.x,s.y,s.z},yaw=yaw};
        static void InstantiateItem(Item item,Transform parent)
        {
            var go=new GameObject(item.name);go.layer=8;go.transform.SetParent(parent,false);go.transform.position=new Vector3(item.position[0],item.position[1],item.position[2]);
            go.transform.rotation=Quaternion.Euler(0,item.yaw,0);go.transform.localScale=new Vector3(item.scale[0],item.scale[1],item.scale[2]);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(item.mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(item.material);go.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        static Mesh ArcMesh(string name,float arc,float radius,float tube)
        {
            string path=AssetRoot+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved)return saved;
            const int sides=12,segments=40;bool closed=arc>6;var vertices=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
            int rings=closed?segments:segments+1;
            for(int j=0;j<rings;j++)
            {
                float a=arc*j/segments;var radial=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                for(int i=0;i<sides;i++)
                {float b=2*Mathf.PI*i/sides;vertices.Add(radial*(radius+Mathf.Cos(b)*tube)+Vector3.forward*(Mathf.Sin(b)*tube));uv.Add(new Vector2((float)i/sides,(float)j/segments*radius*arc));}
            }
            for(int j=0;j<segments;j++)for(int i=0;i<sides;i++)
            {
                int a=j*sides+i,b=j*sides+(i+1)%sides,c=((j+1)%rings)*sides+i,d=((j+1)%rings)*sides+(i+1)%sides;
                indices.AddRange(new[]{a,c,b,b,c,d});
            }
            if(!closed)foreach(int j in new[]{0,segments})
            {
                float a=arc*j/segments;int centre=vertices.Count;vertices.Add(new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0));uv.Add(Vector2.zero);
                for(int i=0;i<sides;i++){int x=j*sides+i,y=j*sides+(i+1)%sides;indices.AddRange(j==0?new[]{centre,x,y}:new[]{centre,y,x});}
            }
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
    }
}
