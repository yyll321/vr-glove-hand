using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnderwaterReconstruction.Editor
{
    public sealed class ReconstructionImport : EditorWindow
    {
        int maxSplats = 100000;
        float minOpacity = 0.02f;
        bool importSH = true;
        [MenuItem("Tools/Reconstruction/2 Import trained data")]
        static void Open() { GetWindow<ReconstructionImport>("Reconstruction Import"); }
        void OnGUI()
        {
            EditorGUILayout.LabelField("3DGS / Graphdeco or Nerfstudio PLY",EditorStyles.boldLabel);
            maxSplats=EditorGUILayout.IntSlider("Splat budget",maxSplats,10000,2000000);
            minOpacity=EditorGUILayout.Slider("Minimum opacity",minOpacity,0,0.2f);
            importSH=EditorGUILayout.Toggle("Import SH (up to degree 3)",importSH);
            EditorGUILayout.HelpBox("Source coordinates: Z up -> Unity (x,z,y). Uses trained scale, quaternion, opacity and SH; ordinary point clouds are rejected. Imports a deterministic subset at the selected budget.",MessageType.Info);
            if(GUILayout.Button("Import trained Gaussian PLY")) ImportSelected(false);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("NeRF baked radiance / density",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select .nrv exported by Tools/Reconstruction/bake_nerf.py. This is a view-averaged baked field, not live neural-network inference.",MessageType.Info);
            if(GUILayout.Button("Import baked NeRF .nrv")) ImportSelected(true);
        }
        void ImportSelected(bool volume)
        {
            string source=EditorUtility.OpenFilePanel(volume?"NeRF volume":"Trained Gaussian PLY","",volume?"nrv":"ply");
            if(string.IsNullOrEmpty(source))return;
            string target=EditorUtility.SaveFilePanelInProject("Save reconstruction asset",Path.GetFileNameWithoutExtension(source),"asset","Choose asset location","Assets/Reconstruction/Data");
            if(string.IsNullOrEmpty(target))return;
            try
            {
                var lab=FindObjectOfType<ReconstructionLab>();
                if(volume)
                {
                    var asset=ReadVolume(source);AssetDatabase.CreateAsset(asset,target);
                    AssetDatabase.AddObjectToAsset(asset.radianceDensity,asset);
                    if(lab){Undo.RecordObject(lab.nerf,"Assign NeRF");lab.nerf.asset=asset;lab.nerf.Apply();EditorUtility.SetDirty(lab.nerf);}
                    Selection.activeObject=asset;
                }
                else
                {
                    var asset=ReadPly(source,maxSplats,minOpacity,importSH);AssetDatabase.CreateAsset(asset,target);
                    if(lab){Undo.RecordObject(lab.gaussian,"Assign 3DGS");lab.gaussian.asset=asset;EditorUtility.SetDirty(lab.gaussian);}
                    Selection.activeObject=asset;
                }
                AssetDatabase.SaveAssets();
                if(lab)UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lab.gameObject.scene);
            }
            catch(Exception e){Debug.LogException(e);EditorUtility.DisplayDialog("Import failed",e.Message,"OK");}
        }
        static string Line(BinaryReader reader)
        {
            var bytes=new List<byte>();
            for(int n=0;n<16384;n++) {byte b=reader.ReadByte();if(b==10)return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');bytes.Add(b);}
            throw new InvalidDataException("PLY header line too long.");
        }
        static float Scalar(BinaryReader r,string type)
        {
            switch(type)
            {
                case "float":case "float32":return r.ReadSingle();
                case "double":case "float64":return (float)r.ReadDouble();
                case "uchar":case "uint8":return r.ReadByte();
                case "char":case "int8":return r.ReadSByte();
                case "short":case "int16":return r.ReadInt16();
                case "ushort":case "uint16":return r.ReadUInt16();
                case "int":case "int32":return r.ReadInt32();
                case "uint":case "uint32":return r.ReadUInt32();
                default:throw new InvalidDataException("Unsupported PLY scalar: "+type);
            }
        }
        public static GaussianSplatAsset ReadPly(string path,int budget,float minAlpha,bool includeSH)
        {
            using(var stream=File.OpenRead(path))using(var r=new BinaryReader(stream))
            {
                if(Line(r)!="ply")throw new InvalidDataException("Not a PLY file.");
                bool ascii=false,format=false,vertex=false,ended=false;int count=0;
                var names=new List<string>();var types=new List<string>();
                for(int line=0;line<1024;line++)
                {
                    string text=Line(r);var p=text.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                    if(p.Length==0)continue;
                    if(p[0]=="end_header"){ended=true;break;}
                    if(p[0]=="format") {ascii=p[1]=="ascii";format=ascii||p[1]=="binary_little_endian";}
                    if(p[0]=="element")
                    {
                        if(p[1]=="vertex"){if(count!=0)throw new InvalidDataException("Duplicate vertex element.");count=int.Parse(p[2],CultureInfo.InvariantCulture);vertex=true;}
                        else {if(count==0 && int.Parse(p[2],CultureInfo.InvariantCulture)>0)throw new InvalidDataException("Vertex element must come first.");vertex=false;}
                    }
                    if(p[0]=="property" && vertex)
                    {if(p[1]=="list")throw new InvalidDataException("Vertex list properties unsupported.");names.Add(p[2]);types.Add(p[1]);}
                }
                if(!ended||!format||count<=0||count>20000000||names.Count>256)throw new InvalidDataException("Unsupported or excessive PLY header.");
                string[] required={"x","y","z","opacity","scale_0","scale_1","scale_2","rot_0","rot_1","rot_2","rot_3","f_dc_0","f_dc_1","f_dc_2"};
                int[] fields=required.Select(n=>names.IndexOf(n)).ToArray();
                if(fields.Any(i=>i<0))throw new InvalidDataException("Expected trained 3DGS PLY with scale, quaternion, opacity and f_dc. Ordinary point clouds cannot be rendered as trained Gaussians.");
                int rest=0;while(names.Contains("f_rest_"+rest))rest++;
                if(rest!=0&&rest!=9&&rest!=24&&rest!=45)throw new InvalidDataException("SH coefficient count must be 9, 24 or 45.");
                int coeff=rest/3;int[] sh=Enumerable.Range(0,rest).Select(i=>names.IndexOf("f_rest_"+i)).ToArray();
                var data=new List<Vector4>();var harmonics=new List<Vector4>();var values=new float[names.Count];
                var bounds=new Bounds();bool first=true;
                int stride=Math.Max(1,(int)Math.Ceiling(count/(double)Math.Max(1,budget)));
                for(int row=0;row<count;row++)
                {
                    if(ascii)
                    {
                        var v=Line(r).Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
                        if(v.Length!=values.Length)throw new InvalidDataException("Unexpected PLY row size.");
                        for(int j=0;j<values.Length;j++)values[j]=float.Parse(v[j],CultureInfo.InvariantCulture);
                    }
                    else for(int j=0;j<values.Length;j++)values[j]=Scalar(r,types[j]);
                    if(row%stride!=0)continue;
                    if(values.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Non-finite Gaussian at row "+row);
                    float alpha=1/(1+Mathf.Exp(-values[fields[3]]));if(alpha<minAlpha)continue;
                    Vector3 p0=new Vector3(values[fields[0]],values[fields[2]],values[fields[1]]);
                    var q=new Quaternion(values[fields[8]],values[fields[9]],values[fields[10]],values[fields[7]]);
                    if(Quaternion.Dot(q,q)<1e-12f)throw new InvalidDataException("Zero Gaussian quaternion.");
                    Vector3 scale=new Vector3(Mathf.Exp(values[fields[4]]),Mathf.Exp(values[fields[5]]),Mathf.Exp(values[fields[6]]));
                    if(!float.IsFinite(scale.sqrMagnitude)||Mathf.Max(scale.x,scale.y,scale.z)>10000)throw new InvalidDataException("Excessive Gaussian scale.");
                    Matrix4x4 m=Matrix4x4.TRS(Vector3.zero,q.normalized,scale);
                    Matrix4x4 c=m*m.transpose;
                    data.Add(new Vector4(p0.x,p0.y,p0.z,alpha));
                    data.Add(new Vector4(c[0,0],c[0,2],c[0,1],c[2,2]));data.Add(new Vector4(c[2,1],c[1,1],0,0));
                    data.Add(new Vector4(0.5f+0.282094792f*values[fields[11]],0.5f+0.282094792f*values[fields[12]],0.5f+0.282094792f*values[fields[13]],0));
                    if(includeSH && rest>0)
                        for(int j=0;j<15;j++)harmonics.Add(j<coeff?new Vector4(values[sh[j]],values[sh[j+coeff]],values[sh[j+coeff*2]],0):Vector4.zero);
                    float radius=3*Mathf.Max(scale.x,scale.y,scale.z);
                    var b=new Bounds(p0,Vector3.one*radius*2);if(first){bounds=b;first=false;}else bounds.Encapsulate(b);
                }
                if(data.Count==0)throw new InvalidDataException("No splats remain after filtering.");
                var asset=CreateInstance<GaussianSplatAsset>();asset.source=Path.GetFileName(path);asset.bounds=bounds;
                asset.data=data.ToArray();asset.harmonics=harmonics.ToArray();asset.shDegree=includeSH?(coeff==15?3:coeff==8?2:coeff==3?1:0):0;
                return asset;
            }
        }
        public static NerfVolumeAsset ReadVolume(string path)
        {
            using(var r=new BinaryReader(File.OpenRead(path)))
            {
                if(Encoding.ASCII.GetString(r.ReadBytes(4))!="NRV1")throw new InvalidDataException("Not an NRV1 volume.");
                int n=r.ReadInt32();if(n<8||n>256)throw new InvalidDataException("Volume resolution must be 8..256.");
                Vector3 lo=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()),hi=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
                if(!float.IsFinite(lo.sqrMagnitude)||!float.IsFinite(hi.sqrMagnitude)||hi.x<=lo.x||hi.y<=lo.y||hi.z<=lo.z)throw new InvalidDataException("Invalid volume bounds.");
                int length=checked(n*n*n*8);byte[] bytes=r.ReadBytes(length);
                if(bytes.Length!=length||r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Unexpected NRV payload length.");
                // Reject NaN/Inf half floats, negative density and negative radiance.
                for(int i=0;i<bytes.Length;i+=2)
                {int h=bytes[i]|bytes[i+1]<<8;if((h&0x7c00)==0x7c00 || (h&0x8000)!=0)throw new InvalidDataException("Invalid half-float radiance/density.");}
                var texture=new Texture3D(n,n,n,TextureFormat.RGBAHalf,false){name="RadianceDensity",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
                texture.SetPixelData(bytes,0);texture.Apply(false,true);
                var asset=CreateInstance<NerfVolumeAsset>();asset.source=Path.GetFileName(path);asset.radianceDensity=texture;
                asset.bounds=new Bounds((lo+hi)*0.5f,hi-lo);return asset;
            }
        }
    }
}
