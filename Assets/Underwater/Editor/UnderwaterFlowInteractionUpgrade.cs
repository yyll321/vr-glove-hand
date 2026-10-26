using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterFlowInteractionUpgrade
    {
        [MenuItem("Tools/Underwater/Research/10 Apply Shared Flow, Sediment and Work Lights")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play before upgrading flow.");
            UnderwaterResearchSceneBuilder.Open();
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/UnderwaterResearch-beforeFlow-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            var presence=Object.FindObjectOfType<UnderwaterPresence>();
            if(!presence)throw new Exception("Saved shared flow system missing.");
            var interaction=presence.GetComponent<UnderwaterInteraction>();
            if(!interaction)interaction=presence.gameObject.AddComponent<UnderwaterInteraction>();
            interaction.presence=presence;interaction.motor=presence.motor;presence.interaction=interaction;
            interaction.arms=Object.FindObjectsOfType<PicoArmPoseDriver>().OrderBy(a=>a.SeabedArea).ToArray();
            var wakes=new System.Collections.Generic.List<UnderwaterWakeSource>();var lights=new System.Collections.Generic.List<Light>();
            foreach(var model in Object.FindObjectsOfType<UnderwaterAcousticModel>().OrderBy(m=>m.volumeCenter.x))
            {
                bool sea=model.volumeCenter.x>31;var rov=GameObject.Find(sea?"ROVp - Seabed":"ROVp - Pool").transform;
                var group=Child(rov,"Water Interaction - editable sources");
                var frame=rov.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="ROV框架p").bounds;
                for(int i=0;i<2;i++)
                {
                    string name="Thruster Water Jet "+(i+1);var existing=group.Find(name);var t=existing?existing:Child(group,name);
                    Vector3 oldPosition=model.source.position+new Vector3(i==0?-0.45f:0.45f,0,0.35f);
                    bool originalPlacement=existing && Mathf.Abs(existing.position.x-oldPosition.x)<0.01f && Mathf.Abs(existing.position.z-oldPosition.z)<0.01f;
                    if(!existing || originalPlacement)
                    {
                        // Expel water outside the rear frame, rather than through its hull.
                        Vector3 p=new Vector3(frame.center.x+(i==0?-0.65f:0.65f),0.55f,frame.max.z+0.12f);
                        if(interaction.FindGround(p,out var floor))p.y=floor.point.y+0.55f;
                        t.SetPositionAndRotation(p,Quaternion.LookRotation(new Vector3(i==0?-0.14f:0.14f,-0.22f,1)));
                    }
                    var wake=t.GetComponent<UnderwaterWakeSource>();if(!wake)wake=t.gameObject.AddComponent<UnderwaterWakeSource>();
                    wake.seabed=sea;wakes.Add(wake);EditorUtility.SetDirty(wake);
                }
                var existingLamp=group.Find("ROV Work Light");var lampT=existingLamp?existingLamp:Child(group,"ROV Work Light");
                if(!existingLamp)lampT.SetPositionAndRotation(model.source.position+new Vector3(0,0.2f,-0.3f),Quaternion.LookRotation(new Vector3(0,-0.22f,-1)));
                var lamp=lampT.GetComponent<Light>();if(!lamp)lamp=lampT.gameObject.AddComponent<Light>();
                lamp.type=LightType.Spot;lamp.color=new Color(0.82f,0.91f,1);lamp.intensity=4;lamp.range=10;lamp.spotAngle=58;lamp.innerSpotAngle=34;
                lamp.shadows=LightShadows.Soft;lamp.shadowStrength=0.75f;lamp.shadowBias=0.02f;lights.Add(lamp);
                var audio=model.GetComponent<UnderwaterAcousticAudio>();if(audio){audio.interaction=interaction;EditorUtility.SetDirty(audio);}
            }
            interaction.wakes=wakes.ToArray();interaction.workLights=lights.ToArray();
            var grainsT=Child(presence.transform,"Sediment Grains - world space");
            var ps=grainsT.GetComponent<ParticleSystem>();if(!ps)ps=grainsT.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.playOnAwake=true;main.loop=true;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=1200;main.startSize=new ParticleSystem.MinMaxCurve(0.002f,0.006f);main.startSpeed=0;main.startLifetime=12;
            var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
            var fade=ps.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,0.08f),new GradientAlphaKey(0.6f,0.7f),new GradientAlphaKey(0,1)});fade.color=gradient;
            var collision=ps.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;collision.mode=ParticleSystemCollisionMode.Collision3D;
            collision.collidesWith=1<<8;collision.enableDynamicColliders=false;collision.lifetimeLoss=1;collision.quality=ParticleSystemCollisionQuality.Low;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.maxParticleSize=0.003f;renderer.minParticleSize=0;
            const string path="Assets/Underwater/Materials/SedimentGrains.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Underwater/Particles"));material.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(material,path);}renderer.sharedMaterial=material;
            interaction.sedimentParticles=ps;
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Underwater/Settings/UnderwaterURP.asset");
            var serialized=new SerializedObject(pipeline);serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue=2;
            serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue=true;serialized.ApplyModifiedProperties();
            var sound=Object.FindObjectOfType<UnderwaterSoundscape>();sound.interaction=interaction;EditorUtility.SetDirty(sound);
            var performance=Object.FindObjectOfType<UnderwaterPerformance>();performance.interaction=interaction;EditorUtility.SetDirty(performance);
            EditorUtility.SetDirty(presence);EditorUtility.SetDirty(interaction);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            UnderwaterResearchSceneBuilder.Validate();Selection.activeGameObject=interaction.gameObject;
            Directory.CreateDirectory("Logs/FlowInteractionReview");
            var report=new System.Text.StringBuilder();
            foreach(var arm in interaction.arms)
            {
                report.AppendLine($"{arm.name}: root {arm.transform.position}, scale {arm.transform.lossyScale}");
                foreach(var rendererPart in arm.GetComponentsInChildren<MeshRenderer>())
                    report.AppendLine($"  {rendererPart.name}: {rendererPart.bounds.min:F3} to {rendererPart.bounds.max:F3}");
            }
            foreach(var source in interaction.wakes)report.AppendLine($"JET {source.name}: {source.transform.position:F3}, forward {source.transform.forward:F3}");
            foreach(var lamp in interaction.workLights)report.AppendLine($"LAMP {lamp.name}: {lamp.transform.position:F3}, forward {lamp.transform.forward:F3}");
            File.WriteAllText("Logs/FlowInteractionReview/scene-audit.txt",report.ToString());
        }
        static Transform Child(Transform parent,string name)
        {var t=parent.Find(name);if(t)return t;var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;}
        public static void BatchApplyAndBuild(){Apply();UnderwaterResearchSceneBuilder.Build();}
    }
}
