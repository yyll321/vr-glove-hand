using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline
{
    static class UnderwaterOpticsChecks
    {
        public static void Run(Camera camera,UnderwaterCaustics caustics,string output,Action<bool,string> check)
        {
            Vector3 position=camera.transform.position;Quaternion rotation=camera.transform.rotation;
            bool frozen=caustics.freezeWaves;float frozenTime=caustics.frozenTime;
            float strength=caustics.settings.causticStrength;
            var renderers=Object.FindObjectsOfType<ParticleSystemRenderer>();var states=new bool[renderers.Length];
            for(int i=0;i<renderers.Length;i++){states[i]=renderers[i].enabled;renderers[i].enabled=false;}
            var panel=GameObject.CreatePrimitive(PrimitiveType.Cube);panel.name="Optics white receiver fixture";panel.layer=8;
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",Color.white);
            panel.GetComponent<Renderer>().sharedMaterial=material;panel.transform.localScale=new Vector3(5,5,0.05f);
            try
            {
                caustics.freezeWaves=true;caustics.frozenTime=7.5f;caustics.settings.causticStrength=0;
                Color[] near=new Color[2],far=new Color[2];
                for(int region=0;region<2;region++)
                {
                    float x=region*62;panel.transform.position=new Vector3(x,5,0);
                    camera.transform.SetPositionAndRotation(new Vector3(x,5,-2),Quaternion.identity);
                    var image=Capture(camera);near[region]=image.GetPixel(image.width/2,image.height/2);Object.Destroy(image);
                    camera.transform.position=new Vector3(x,5,-12);
                    image=Capture(camera);far[region]=image.GetPixel(image.width/2,image.height/2);Object.Destroy(image);
                }
                check(far[0].r<near[0].r-0.12f && far[0].r/far[0].b<near[0].r/near[0].b,
                    $"Rendered water progressively absorbs red over distance (pool red {near[0].r:F3}->{far[0].r:F3})");
                check(far[1].r<far[0].r-0.1f && far[1].r/far[1].b<far[0].r/far[0].b,
                    $"Natural water has distinct spectral extinction (12 m red: pool {far[0].r:F3}, sea {far[1].r:F3})");
                float bottom=0,top=0;
                foreach(float height in new[]{1.2f,4.5f,8.5f})
                {
                    panel.transform.position=new Vector3(62,height,0);
                    camera.transform.SetPositionAndRotation(new Vector3(62,height,-6),Quaternion.identity);
                    var image=Capture(camera);float value=image.GetPixel(image.width/2,image.height/2).r;Object.Destroy(image);
                    if(height<2)bottom=value;if(height>8)top=value;
                }
                check(top>bottom+0.15f,$"Seabed visibility increases with height at equal six-metre range (red {bottom:F3}->{top:F3})");
                panel.transform.position=new Vector3(62,3,0);camera.transform.SetPositionAndRotation(new Vector3(62,3,-4),Quaternion.identity);
                caustics.settings.causticStrength=0;var noCaustics=Capture(camera);
                caustics.settings.causticStrength=strength;var enabledCaustics=Capture(camera);
                var off=noCaustics.GetPixels();var on=enabledCaustics.GetPixels();float difference=0;
                for(int i=0;i<off.Length;i++)difference+=Mathf.Abs(off[i].r-on[i].r);
                check(difference/off.Length<0.001f,$"Seabed receivers have no caustic modulation (image difference {difference/off.Length:F6})");
                Object.Destroy(noCaustics);Object.Destroy(enabledCaustics);
                panel.SetActive(false);caustics.settings.causticStrength=strength;
                foreach(float height in new[]{1.2f,7.5f})
                {
                    camera.transform.SetPositionAndRotation(new Vector3(62,height,-12),Quaternion.LookRotation(new Vector3(0,-0.05f,1)));
                    var image=Capture(camera);
                    File.WriteAllBytes(Path.Combine(output,height<2?"11-seabed-low-visibility.png":"12-seabed-upper-visibility.png"),image.EncodeToPNG());Object.Destroy(image);
                }
                for(int region=0;region<2;region++)
                {
                    float x=region*62;
                    Material surface=null;
                    foreach(var r in Object.FindObjectsOfType<MeshRenderer>())
                        if(r.sharedMaterial && r.sharedMaterial.shader.name=="Underwater/Surface" && Mathf.Abs(r.transform.position.x-x)<1)surface=r.sharedMaterial;
                    var cube=surface?surface.GetTexture("_EnvironmentCube") as Cubemap:null;
                    check(cube && cube.width==256 && surface.GetFloat("_EnvironmentReady")>0.5f,
                        (region==0?"Pool":"Sea")+" has a saved world environment cubemap for underwater reflections");
                    camera.transform.SetPositionAndRotation(new Vector3(x,8.5f,-6),Quaternion.LookRotation(new Vector3(1,0.42f,0.1f)));
                    var reflected=Capture(camera);
                    File.WriteAllBytes(Path.Combine(output,region==0?"04-pool-under-surface.png":"05-sea-under-surface.png"),reflected.EncodeToPNG());
                    if(surface)
                    {
                        float ready=surface.GetFloat("_EnvironmentReady");surface.SetFloat("_EnvironmentReady",0);
                        Texture2D fallback;
                        try{fallback=Capture(camera);}finally{surface.SetFloat("_EnvironmentReady",ready);}
                        var a=reflected.GetPixels();var b=fallback.GetPixels();double delta=0;
                        for(int i=0;i<a.Length;i++)delta+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);
                        check(delta/a.Length>0.015,$"{(region==0?"Pool":"Sea")} surface uses the world environment instead of copying the eye image (difference {delta/a.Length:F3})");
                        Object.Destroy(fallback);
                    }
                    Object.Destroy(reflected);
                }
            }
            finally
            {
                Object.Destroy(panel);Object.Destroy(material);caustics.freezeWaves=frozen;caustics.frozenTime=frozenTime;caustics.settings.causticStrength=strength;
                camera.transform.SetPositionAndRotation(position,rotation);
                for(int i=0;i<renderers.Length;i++)if(renderers[i])renderers[i].enabled=states[i];
            }
        }
        static Texture2D Capture(Camera camera)
        {
            var target=RenderTexture.GetTemporary(960,600,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
                var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false,true);
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();return image;
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
    }
}
