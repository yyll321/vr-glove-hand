using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnderwaterBaseline
{
    // Render the real depth-reconstruction shader with frozen waves. Compare identical
    // visible world points, not identical screen pixels, across different camera poses.
    static class UnderwaterCausticReceiverChecks
    {
        public static void Run(Camera camera, UnderwaterCaustics caustics, string output, Action<bool,string> check)
        {
            Vector3 position=camera.transform.position; Quaternion rotation=camera.transform.rotation;
            bool frozen=caustics.freezeWaves;float frozenTime=caustics.frozenTime;
            caustics.freezeWaves=true;caustics.frozenTime=7.5f;
            float aspect=camera.aspect;camera.aspect=1440f/900;
            var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            float scale=pipeline.renderScale;
            var particles=UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>();
            var particleStates=new bool[particles.Length];
            for(int i=0;i<particles.Length;i++){particleStates[i]=particles[i].enabled;particles[i].enabled=false;}
            var images=new List<Texture2D>();
            try
            {
                Shader.SetGlobalFloat("_UWCausticDebug",1);
                caustics.RenderMaps(7.5f);
                camera.transform.SetPositionAndRotation(new Vector3(0,4,-8),Quaternion.LookRotation(new Vector3(0,-0.4f,1)));
                pipeline.renderScale=1;
                var reference=Capture(camera);images.Add(reference);
                File.WriteAllBytes(Path.Combine(output,"receiver-fixed-time-A.png"),reference.EncodeToPNG());
                var points=new List<Vector3>();var values=new List<float>();
                for(float x=-9;x<=9;x+=0.31f)for(float z=-5;z<16;z+=0.37f)
                {
                    if(!Physics.Raycast(new Vector3(x,3,z),Vector3.down,out var hit,4,1<<8) || hit.point.y>0.1f || hit.normal.y<0.98f)continue;
                    if(Visible(camera,hit.point,out Vector3 uv))
                    {points.Add(hit.point);values.Add(reference.GetPixelBilinear(uv.x,uv.y).r);}
                }
                var baselinePosition=camera.transform.position;var baselineRotation=camera.transform.rotation;
                for(int view=0;view<3;view++)
                {
                    camera.transform.SetPositionAndRotation(view==0?baselinePosition+Vector3.right*0.064f:new Vector3(1.1f,4.3f,-7.5f),
                        view==0?baselineRotation:Quaternion.LookRotation(new Vector3(0.18f,-0.46f,1)));
                    pipeline.renderScale=view==2?0.65f:1;
                    var moved=Capture(camera);images.Add(moved);
                    if(view==1)File.WriteAllBytes(Path.Combine(output,"receiver-fixed-time-B.png"),moved.EncodeToPNG());
                    var differences=new List<float>();
                    for(int i=0;i<points.Count;i++)if(Visible(camera,points[i],out Vector3 uv))
                        differences.Add(Mathf.Abs(moved.GetPixelBilinear(uv.x,uv.y).r-values[i]));
                    differences.Sort();float sum=0;foreach(float d in differences)sum+=d;
                    float mean=sum/Mathf.Max(1,differences.Count),p95=differences.Count>0?differences[(int)((differences.Count-1)*0.95f)]:1;
                    string label=view==0?"64 mm eye separation":view==1?"camera translation and rotation":"camera motion with render scale 0.65";
                    check(differences.Count>200 && mean<0.035f && p95<0.10f,
                        $"Frozen caustics remain on the same receiver points under {label} ({differences.Count} points, mean {mean:F4}, p95 {p95:F4})");
                }
                // Sample a top-down receiver image to detect an exact repeated map at
                // the original projected tile spacing. This tests the actual shader.
                camera.transform.SetPositionAndRotation(new Vector3(0,8,0),Quaternion.Euler(90,0,0));
                bool orthographic=camera.orthographic;float size=camera.orthographicSize;
                camera.orthographic=true;camera.orthographicSize=11;pipeline.renderScale=1;
                var top=Capture(camera);images.Add(top);
                camera.orthographic=orthographic;camera.orthographicSize=size;
                float repetition=0;int pairs=0;float period=caustics.settings.tileSize*caustics.settings.causticFootprint;
                for(float x=-9;x<1;x+=0.19f)for(float z=-8;z<8;z+=0.23f)
                {
                    // Top-down viewport mapping is retained explicitly after restoring projection settings.
                    Vector2 a=new Vector2(0.5f+x/(22*1440f/900),0.5f+z/22);
                    Vector2 b=a+Vector2.right*(period/(22*1440f/900));
                    repetition+=Mathf.Abs(top.GetPixelBilinear(a.x,a.y).r-top.GetPixelBilinear(b.x,b.y).r);pairs++;
                }
                check(repetition/Mathf.Max(1,pairs)>0.018f,$"Receiver pattern avoids identical adjacent tiles (mean difference {repetition/Mathf.Max(1,pairs):F4})");
            }
            finally
            {
                Shader.SetGlobalFloat("_UWCausticDebug",0);
                caustics.freezeWaves=frozen;caustics.frozenTime=frozenTime;
                camera.transform.SetPositionAndRotation(position,rotation);camera.aspect=aspect;pipeline.renderScale=scale;
                for(int i=0;i<particles.Length;i++)if(particles[i])particles[i].enabled=particleStates[i];
                foreach(var image in images)UnityEngine.Object.Destroy(image);
            }
        }
        static bool Visible(Camera camera,Vector3 point,out Vector3 uv)
        {
            uv=camera.WorldToViewportPoint(point);
            if(uv.z<0 || uv.x<0.03f || uv.x>0.97f || uv.y<0.03f || uv.y>0.97f)return false;
            Vector3 delta=point-camera.transform.position;
            return Physics.Raycast(camera.transform.position,delta.normalized,out var hit,delta.magnitude+0.05f,1<<8)
                && Vector3.Distance(hit.point,point)<0.06f;
        }
        static Texture2D Capture(Camera camera)
        {
            var target=RenderTexture.GetTemporary(1440,900,24,RenderTextureFormat.ARGB32);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false,true);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);return image;
        }
    }
}
