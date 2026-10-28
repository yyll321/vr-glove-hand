using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterGloveSandUpgrade
    {
        const string Folder="Assets/Underwater/SandInteraction";
        [MenuItem("Tools/Underwater/Research/34 HandDriver Mapping and Deformable Sand")]
        public static void Apply()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=UnderwaterRadarSurveyTools.ScenePath)throw new Exception("Open editable survey scene outside Play");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/Survey-beforeGloveSand-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var material=CreateMaterial();
            var visual=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/SandSurface.asset");
            if(!visual){visual=UnderwaterDeformableSand.CreateGrid(.9f,.66f,180,132);AssetDatabase.CreateAsset(visual,Folder+"/SandSurface.asset");}
            var collision=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/SandSupport.asset");
            if(!collision){collision=UnderwaterDeformableSand.CreateGrid(.9f,.66f,60,44);var points=collision.vertices;for(int i=0;i<points.Length;i++)points[i].y=-.012f;collision.vertices=points;collision.RecalculateBounds();AssetDatabase.CreateAsset(collision,Folder+"/SandSupport.asset");}
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>())
            {
                var mapper=task.glove;mapper.UseArticulationDriveLimits=false;mapper.ConfigureDriveStrength=false;
                ConfigureSerialThumb(mapper);
                mapper.UseHandDriverMappings();
                var thumb=mapper.JointMappings[0].Joint;
                foreach(var m in mapper.JointMappings.Take(2))
                {var d=Wa100UdexRealArticulationMapper.GetDrive(m);d.lowerLimit=m.LowerLimitDegrees;d.upperLimit=m.UpperLimitDegrees;d.target=0;d.stiffness=30000;d.damping=220;d.forceLimit=180;Wa100UdexRealArticulationMapper.SetDrive(m,d);}
                var bench=task.GetComponentInChildren<UnderwaterGraspBench>();
                var surface=bench.GetComponentInChildren<UnderwaterDeformableSand>();
                if(!surface)
                {
                    var go=new GameObject("Deformable fine sand - finger cavities"){layer=11};go.transform.SetParent(bench.transform,true);
                    go.transform.position=new Vector3(bench.sand.bounds.center.x,bench.SurfaceHeight,bench.sand.bounds.center.z);
                    surface=go.AddComponent<UnderwaterDeformableSand>();
                }
                surface.bench=bench;surface.visualTemplate=visual;surface.collisionTemplate=collision;surface.edgeWidth=.015f;bench.deformable=surface;
                surface.GetComponent<MeshFilter>().sharedMesh=visual;surface.GetComponent<MeshRenderer>().sharedMaterial=material;
                surface.SurfaceCollider.convex=false;surface.SurfaceCollider.sharedMesh=collision;
                // Retain the volume's side walls but remove the flat rendered lid,
                // which would otherwise cover every newly excavated finger hole.
                var sides=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/SandVolumeSides.asset");
                if(!sides)
                {
                    sides=Object.Instantiate(bench.sand.GetComponent<MeshFilter>().sharedMesh);sides.name="Sand volume sides without lid";
                    var p=sides.vertices;var t=sides.triangles;var keep=new System.Collections.Generic.List<int>();
                    for(int i=0;i<t.Length;i+=3)if(!(p[t[i]].y>.49f && p[t[i+1]].y>.49f && p[t[i+2]].y>.49f)){keep.Add(t[i]);keep.Add(t[i+1]);keep.Add(t[i+2]);}
                    sides.SetTriangles(keep,0);AssetDatabase.CreateAsset(sides,Folder+"/SandVolumeSides.asset");
                }
                bench.sand.GetComponent<MeshFilter>().sharedMesh=sides;bench.sand.GetComponent<MeshRenderer>().sharedMaterial=material;
                foreach(var o in new Object[]{mapper,thumb,bench,surface,bench.sand.GetComponent<MeshFilter>(),bench.sand.GetComponent<MeshRenderer>()})EditorUtility.SetDirty(o);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        public static void ConfigureSerialThumb(Wa100UdexRealArticulationMapper mapper)
        {
            // The two-axis spherical joint was stable at rest but flipped under
            // mounted-root motion, even with constant commands and no contacts.
            // Two serial hinges keep the two freedoms without spherical coupling.
            var thumb=mapper.GetComponentsInChildren<ArticulationBody>(true).First(b=>b.name=="Thumb_L1");
            var anchor=thumb.anchorRotation;var parentAnchor=thumb.parentAnchorRotation;
            thumb.jointType=ArticulationJointType.RevoluteJoint;thumb.matchAnchors=false;
            thumb.anchorRotation=anchor;thumb.parentAnchorRotation=parentAnchor;
            thumb.twistLock=ArticulationDofLock.LimitedMotion;thumb.swingYLock=ArticulationDofLock.LockedMotion;thumb.swingZLock=ArticulationDofLock.LockedMotion;
            var pivot=thumb.transform.Find("Thumb_CMC_PitchPivot");
            if(!pivot)
            {
                var children=Enumerable.Range(0,thumb.transform.childCount).Select(i=>thumb.transform.GetChild(i)).ToArray();
                pivot=new GameObject("Thumb_CMC_PitchPivot").transform;pivot.SetParent(thumb.transform,false);
                // Identity pivot preserves the saved mesh/collider and distal
                // joint frames. Its X hinge represents the former local Z swing.
                foreach(var child in children)child.SetParent(pivot,false);
                var body=pivot.gameObject.AddComponent<ArticulationBody>();body.jointType=ArticulationJointType.RevoluteJoint;
                body.matchAnchors=false;body.anchorPosition=body.parentAnchorPosition=Vector3.zero;
                body.anchorRotation=body.parentAnchorRotation=Quaternion.FromToRotation(Vector3.right,Vector3.forward);
                body.twistLock=ArticulationDofLock.LimitedMotion;body.swingYLock=body.swingZLock=ArticulationDofLock.LockedMotion;
                body.mass=.02f;body.useGravity=false;body.solverIterations=32;body.solverVelocityIterations=12;body.maxJointVelocity=4.2f;
                var drive=body.xDrive;drive.lowerLimit=0;drive.upperLimit=65;drive.stiffness=30000;drive.damping=220;drive.forceLimit=180;body.xDrive=drive;
            }
            EditorUtility.SetDirty(thumb);EditorUtility.SetDirty(pivot.GetComponent<ArticulationBody>());
        }
        [MenuItem("Tools/Underwater/Research/37 Stabilize Thumb During Arm Motion")]
        public static void RepairThumb()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=UnderwaterRadarSurveyTools.ScenePath)throw new Exception("Open survey scene outside Play");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/Survey-beforeThumbMotion-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            foreach(var mapper in Object.FindObjectsOfType<Wa100UdexRealArticulationMapper>())
            {
                ConfigureSerialThumb(mapper);mapper.UseArticulationDriveLimits=false;mapper.UseHandDriverMappings();
                foreach(var m in mapper.JointMappings.Take(2)){var d=Wa100UdexRealArticulationMapper.GetDrive(m);d.lowerLimit=m.LowerLimitDegrees;d.upperLimit=m.UpperLimitDegrees;d.target=0;d.stiffness=30000;d.damping=220;d.forceLimit=180;Wa100UdexRealArticulationMapper.SetDrive(m,d);}
                EditorUtility.SetDirty(mapper);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        static Material CreateMaterial()
        {
            const int size=512;var color=new Color[size*size];var height=new float[color.Length];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float gx=x/3f,gy=y/3f;int ix=(int)gx,iy=(int)gy;float best=10;int grain=0;
                for(int j=-1;j<=1;j++)for(int i=-1;i<=1;i++)
                {
                    int seed=(ix+i)*374761393+(iy+j)*668265263;
                    float px=ix+i+Hash(seed),py=iy+j+Hash(seed+37);float distance=(gx-px)*(gx-px)+(gy-py)*(gy-py);
                    if(distance<best){best=distance;grain=seed;}
                }
                float h=Hash(grain);float edge=Mathf.SmoothStep(.55f,1.1f,Mathf.Sqrt(best));
                float variation=(h-.5f)*.12f-edge*.055f+(Mathf.PerlinNoise(x*.015f,y*.015f)-.5f)*.035f;
                color[y*size+x]=new Color(.52f+variation,.43f+variation*.85f,.29f+variation*.65f,1);
                height[y*size+x]=h*.30f+(1-Mathf.Clamp01(Mathf.Sqrt(best)))*.7f;
            }
            var normal=new Color[color.Length];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=height[y*size+(x+1)%size]-height[y*size+(x+size-1)%size];
                float dz=height[((y+1)%size)*size+x]-height[((y+size-1)%size)*size+x];
                var n=new Vector3(-dx*.65f,-dz*.65f,1).normalized;normal[y*size+x]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
            }
            SaveTexture("FineSandGrains",color,false);SaveTexture("FineSandNormal",normal,true);
            string path=Folder+"/FineWetSand.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader=Shader.Find("Underwater/Fine Wet Sand");
            if(!shader || ShaderUtil.ShaderHasError(shader))throw new Exception("Fine sand shader is unavailable or has errors");
            if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}else material.shader=shader;
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/FineSandGrains.png"));
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/FineSandNormal.png"));
            material.SetColor("_BaseColor",Color.white);material.SetFloat("_BumpScale",.65f);material.SetFloat("_Smoothness",.13f);material.SetFloat("_GrainTileMetres",.12f);
            material.EnableKeyword("_NORMALMAP");material.enableInstancing=true;EditorUtility.SetDirty(material);return material;
        }
        static float Hash(int seed){unchecked{uint h=(uint)seed;h^=h>>13;h*=1274126177;h^=h>>16;return (h&0xffffff)/16777215f;}}
        static void SaveTexture(string name,Color[] pixels,bool normal)
        {
            string path=Folder+"/"+name+".png";var tex=new Texture2D(512,512,TextureFormat.RGBA32,false,normal);tex.SetPixels(pixels);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=!normal;importer.mipmapEnabled=true;importer.anisoLevel=8;importer.wrapMode=TextureWrapMode.Repeat;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        }
        public static void Audit()
        {
            Directory.CreateDirectory("Logs/GloveSand");
            var lines=Object.FindObjectsOfType<Wa100UdexRealArticulationMapper>().Select(map=>
            {
                map.AutoBind();var text=map.name+" role="+map.CharacterName+" autoNeutral="+map.AutoCalibrateNeutralOnFirstPacket;
                foreach(var m in map.JointMappings)
                {
                    var b=m.Joint;var axis=b.transform.TransformDirection(b.anchorRotation*Vector3.right);
                    text+="\n"+m.JointName+" -> "+m.LinkName+" actual="+b.name+" id="+b.GetInstanceID()+" key="+m.PrimaryKey+"+"+m.SecondaryKey+" limit="+m.LowerLimitDegrees+","+m.UpperLimitDegrees+" local="+b.transform.localPosition.ToString("F4")+" axis="+axis.ToString("F3")+" anchorRotation="+b.anchorRotation.eulerAngles;
                    if(b.name=="Thumb_L1")text+=" parentLocalAxis="+b.transform.parent.InverseTransformDirection(axis).ToString("F3")+" rootAxes="+b.transform.InverseTransformDirection(map.transform.right)+"/"+b.transform.InverseTransformDirection(map.transform.up)+"/"+b.transform.InverseTransformDirection(map.transform.forward);
                }
                return text;
            });
            File.WriteAllText("Logs/GloveSand/before-audit.txt",string.Join("\n",lines));
        }
    }
}
