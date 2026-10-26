using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace UnderwaterReconstruction.Editor
{
    public static class ReconstructionChecks
    {
        static int checks;
        static void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL: "+name);checks++;Debug.Log("PASS: "+name);}
        public static void Run()
        {
            Directory.CreateDirectory("Logs/Reconstruction");
            var lab=Object.FindObjectOfType<ReconstructionLab>();
            Check(lab!=null,"lab exists");
            string path="Logs/Reconstruction/fixture.ply";
            var header="ply\nformat binary_little_endian 1.0\nelement vertex 2\n";
            foreach(string property in new[]{"x","y","z","opacity","scale_0","scale_1","scale_2","rot_0","rot_1","rot_2","rot_3","f_dc_0","f_dc_1","f_dc_2"})header+="property float "+property+"\n";
            using(var w=new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes(header+"end_header\n"));
                foreach(float f in new float[]{1,2,3,0,0,0,0,1,0,0,0,0,0,0})w.Write(f);
                foreach(float f in new float[]{4,5,6,-20,0,0,0,1,0,0,0,0,0,0})w.Write(f);
            }
            var asset=ReconstructionImport.ReadPly(path,10,0.02f,true);
            Check(asset.Count==1,"PLY opacity filtering");
            Check((asset.data[0]-new Vector4(1,3,2,0.5f)).sqrMagnitude<0.00001f,"PLY coordinate mapping and sigmoid");
            Check((asset.data[1]-new Vector4(1,0,0,1)).sqrMagnitude<0.00001f && asset.data[2].y==1,"log-scale to covariance");
            Check(asset.data[3].x==0.5f,"SH DC conversion");Object.DestroyImmediate(asset);
            // Anisotropic, rotated source covariance and channel-major SH must survive ASCII import.
            string asciiHeader=header.Replace("binary_little_endian","ascii").Replace("vertex 2","vertex 1");
            for(int i=0;i<45;i++)asciiHeader+="property float f_rest_"+i+"\n";
            var values=new System.Collections.Generic.List<float>{1,2,3,0,0,Mathf.Log(2),Mathf.Log(3),0.70710678f,0,0,0.70710678f,0,0,0};
            values.AddRange(Enumerable.Range(0,45).Select(i=>(float)i));
            File.WriteAllText(path,asciiHeader+"end_header\n"+string.Join(" ",values.Select(f=>f.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))+"\n");
            asset=ReconstructionImport.ReadPly(path,10,0,true);
            Check(asset.shDegree==3 && asset.harmonics.Length==15 && asset.harmonics[0]==new Vector4(0,15,30,0),"ASCII degree-3 SH channel layout");
            Check(Mathf.Abs(asset.data[1].x-4)<0.0001f && Mathf.Abs(asset.data[1].w-9)<0.0001f && Mathf.Abs(asset.data[2].y-1)<0.0001f,"rotated anisotropic covariance and coordinate mapping");
            Object.DestroyImmediate(asset);
            File.WriteAllText(path,"ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nend_header\n0\n");
            bool rejected=false;try{ReconstructionImport.ReadPly(path,10,0,true);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"point-cloud PLY rejected");
            string nrv="Logs/Reconstruction/fixture.nrv";
            using(var w=new BinaryWriter(File.Create(nrv)))
            {w.Write(Encoding.ASCII.GetBytes("NRV1"));w.Write(8);foreach(float f in new float[]{-1,-2,-3,1,2,3})w.Write(f);w.Write(new byte[8*8*8*8]);}
            var volume=ReconstructionImport.ReadVolume(nrv);Check(volume.radianceDensity.width==8 && volume.bounds.size==new Vector3(2,4,6),"NRV dimensions, bounds and texture");
            Object.DestroyImmediate(volume.radianceDensity);Object.DestroyImmediate(volume);
            using(var f=new FileStream(nrv,FileMode.Append))f.WriteByte(1);
            rejected=false;try{ReconstructionImport.ReadVolume(nrv);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"malformed NRV rejected");
            Check(SystemInfo.supportsComputeShaders,"compute shaders available");
            var original=QualitySettings.renderPipeline;
            var target=new RenderTexture(640,480,24,RenderTextureFormat.ARGB32);target.Create();
            var oldTarget=lab.eye.targetTexture;
            bool previous=lab.useNerf;
            bool preferred=lab.preferOfficial;
            try
            {
                QualitySettings.renderPipeline=lab.lightweightPipeline;
                lab.preferOfficial=false;
                lab.eye.targetTexture=target;
                lab.Select(false);lab.gaussian.Prepare(lab.eye);lab.gaussian.Draw();
                var buffer=(ComputeBuffer)typeof(GaussianSplatRenderer).GetField("order",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(lab.gaussian);
                uint[] order=new uint[buffer.count*2];buffer.GetData(order);
                bool ordered=true;for(int i=1;i<lab.gaussian.asset.Count;i++)if(order[(i-1)*2]<order[i*2])ordered=false;
                Check(ordered,"GPU back-to-front sort");
                RenderPipeline.SubmitRenderRequest(lab.eye,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                Capture(target,"Logs/Reconstruction/3dgs.png");
                lab.Select(true);lab.nerf.Apply();
                RenderPipeline.SubmitRenderRequest(lab.eye,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                Capture(target,"Logs/Reconstruction/nerf.png");
            }
            finally
            {lab.eye.targetTexture=oldTarget;lab.preferOfficial=preferred;lab.Select(previous);QualitySettings.renderPipeline=original;target.Release();Object.DestroyImmediate(target);}
            File.WriteAllText("Logs/Reconstruction/checks.txt",$"PASS {checks} checks. GPU: {SystemInfo.graphicsDeviceName}; API: {SystemInfo.graphicsDeviceType}. XR headset performance not measured.\n");
        }
        static void Capture(RenderTexture target,string path)
        {
            var old=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=old;
            File.WriteAllBytes(path,image.EncodeToPNG());
            var pixels=image.GetPixels32();var first=pixels[0];
            int changed=pixels.Count(c=>Math.Abs(c.r-first.r)+Math.Abs(c.g-first.g)+Math.Abs(c.b-first.b)>25);
            Object.DestroyImmediate(image);
            Check(changed>500,"nonempty render "+path);
        }
    }
}
