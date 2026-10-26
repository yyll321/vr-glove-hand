using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace UnderwaterReconstruction.Editor
{
    public static class SyntheticTrainingCapture
    {
        public const string Output="TrainingData/synthetic_sand_torus";
        const int Width=640,Height=480,Views=48;
        static string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        static Vector3 Source(Vector3 p)=>new Vector3(p.x,p.z,p.y);
        [MenuItem("Tools/Reconstruction/7 Export 48-view synthetic training dataset")]
        public static void Export()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play mode before dataset capture.");
            if(File.Exists(Output+"/transforms.json"))
            {
                Debug.Log("Synthetic dataset already exists; preserved "+Output);return;
            }
            if(Directory.Exists(Output)&&Directory.GetFileSystemEntries(Output).Length>0)
                throw new IOException("Capture output is not empty. Preserve or rename it before retrying: "+Output);
            var lab=Object.FindObjectOfType<ReconstructionLab>();if(!lab)throw new Exception("Open reconstruction scene first.");
            var synthetic=AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>("Assets/Reconstruction/Data/SyntheticGaussians.asset");
            if(!synthetic||!synthetic.synthetic)throw new Exception("Synthetic source asset missing.");
            Directory.CreateDirectory(Output+"/images");
            var camera=lab.eye;var previousPipeline=QualitySettings.renderPipeline;
            var oldPosition=camera.transform.localPosition;var oldRotation=camera.transform.localRotation;var oldTarget=camera.targetTexture;
            var oldAsset=lab.gaussian.asset;bool oldPreferred=lab.preferOfficial,oldMode=lab.useNerf;
            var ancillary=Object.FindObjectsOfType<Renderer>();
            var enabledStates=Array.ConvertAll(ancillary,r=>r.enabled);
            var target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32);target.Create();
            var image=new Texture2D(Width,Height,TextureFormat.RGB24,false);
            var frames=new StringBuilder();var train=new StringBuilder();var val=new StringBuilder();
            try
            {
                QualitySettings.renderPipeline=lab.lightweightPipeline;lab.preferOfficial=false;lab.Select(false);lab.gaussian.asset=synthetic;
                foreach(var renderer in ancillary)renderer.enabled=false;
                camera.targetTexture=target;
                for(int i=0;i<Views;i++)
                {
                    float angle=i%16*Mathf.PI*2/16;
                    Vector3 position=new Vector3(Mathf.Sin(angle)*4.5f,1.1f+i/16*0.7f,Mathf.Cos(angle)*4.5f);
                    camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(new Vector3(0,0.55f,0)-position,Vector3.up));
                    lab.gaussian.Prepare(camera,true);lab.gaussian.Draw();
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                    var active=RenderTexture.active;RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();RenderTexture.active=active;
                    string file="images/frame_"+i.ToString("D3")+".png";File.WriteAllBytes(Output+"/"+file,image.EncodeToPNG());
                    Vector3 right=Source(camera.transform.right),up=Source(camera.transform.up),back=-Source(camera.transform.forward),p=Source(camera.transform.position);
                    if(i>0)frames.Append(",\n");
                    frames.Append("{\"file_path\":\""+file+"\",\"transform_matrix\":[");
                    frames.Append("["+F(right.x)+","+F(up.x)+","+F(back.x)+","+F(p.x)+"],");
                    frames.Append("["+F(right.y)+","+F(up.y)+","+F(back.y)+","+F(p.y)+"],");
                    frames.Append("["+F(right.z)+","+F(up.z)+","+F(back.z)+","+F(p.z)+"],[0,0,0,1]]}");
                    var split=i%8==0?val:train;if(split.Length>0)split.Append(",");split.Append("\""+file+"\"");
                }
                WritePoints(synthetic);
                float focal=Height*0.5f/Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*0.5f);
                string metadata="{\"camera_model\":\"OPENCV\",\"w\":"+Width+",\"h\":"+Height+",\"fl_x\":"+F(focal)+",\"fl_y\":"+F(focal)+",\"cx\":"+Width/2+",\"cy\":"+Height/2+",\"k1\":0,\"k2\":0,\"p1\":0,\"p2\":0,\"ply_file_path\":\"points3D.ply\",\"frames\":[\n"+frames+"],\"train_filenames\":["+train+"],\"val_filenames\":["+val+"],\"test_filenames\":["+val+"]}";
                // Write the manifest last so an interrupted export can never look complete.
                File.WriteAllText(Output+"/transforms.json",metadata, new UTF8Encoding(false));
                File.WriteAllText(Output+"/README.txt","Synthetic multi-view renderings generated by this project. NOT real underwater photographs.\n48 views at 640x480; 42 train + 6 held-out views.\nCamera convention: OpenGL, source world Z-up. Intrinsics and point initialization included.\nCan train with ns-train splatfacto --data <this-directory>; no COLMAP required.\nGenerated from a known synthetic Gaussian field; for pipeline testing only, not a real-world reconstruction benchmark.\n");
                Debug.Log("Exported 48 synthetic training views to "+Output);
            }
            finally
            {
                camera.targetTexture=oldTarget;camera.transform.localPosition=oldPosition;camera.transform.localRotation=oldRotation;
                lab.gaussian.asset=oldAsset;lab.preferOfficial=oldPreferred;lab.Select(oldMode);QualitySettings.renderPipeline=previousPipeline;
                for(int i=0;i<ancillary.Length;i++)if(ancillary[i])ancillary[i].enabled=enabledStates[i];
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);
            }
        }
        static void WritePoints(GaussianSplatAsset asset)
        {
            using(var w=new StreamWriter(Output+"/points3D.ply",false,Encoding.ASCII))
            {
                w.WriteLine("ply\nformat ascii 1.0\nelement vertex "+asset.Count+"\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nend_header");
                for(int i=0;i<asset.Count;i++)
                {
                    Vector4 p=asset.data[i*4],c=asset.data[i*4+3];
                    w.WriteLine(F(p.x)+" "+F(p.z)+" "+F(p.y)+" "+Mathf.RoundToInt(Mathf.Clamp01(c.x)*255)+" "+Mathf.RoundToInt(Mathf.Clamp01(c.y)*255)+" "+Mathf.RoundToInt(Mathf.Clamp01(c.z)*255));
                }
            }
        }
    }
}
