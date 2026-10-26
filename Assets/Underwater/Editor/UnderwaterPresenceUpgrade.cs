using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterPresenceUpgrade
    {
        [MenuItem("Tools/Underwater/Research/4 Apply Live Preview Presence Fixes")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play mode before applying saved scene changes.");
            if(SceneManager.GetActiveScene().path!=UnderwaterResearchSceneBuilder.ScenePath)UnderwaterResearchSceneBuilder.Open();
            var scene=SceneManager.GetActiveScene();
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/UnderwaterResearch-beforePresence-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Improve underwater presence for Live Preview");
            var motor=Object.FindObjectOfType<UnderwaterMotor>();var env=motor.environment;
            var root=Object.FindObjectOfType<UnderwaterCaustics>().gameObject;
            var presence=root.GetComponent<UnderwaterPresence>();if(!presence)presence=Undo.AddComponent<UnderwaterPresence>(root);
            presence.motor=motor;presence.environment=env;motor.presence=presence;
            presence.grass=Object.FindObjectsOfType<MeshFilter>().Where(m=>m.sharedMesh && m.sharedMesh.name=="CurvedSeagrass").Select(m=>m.transform).ToArray();
            float[] radius={2,4.5f,9},height={3,5,7},alpha={0.65f,0.45f,0.3f};
            for(int i=0;i<env.snow.Length;i++)
            {
                var ps=env.snow[i];Undo.RecordObject(ps,"Tune near-field suspended matter");ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.startColor=new Color(0.75f,0.88f,0.86f,alpha[i]);
                var shape=ps.shape;shape.scale=new Vector3(radius[i]*2,height[i],radius[i]*2);
                var velocity=ps.velocityOverLifetime;velocity.x=new ParticleSystem.MinMaxCurve(0.07f,0.14f);velocity.z=new ParticleSystem.MinMaxCurve(0.02f,0.06f);
                var noise=ps.noise;noise.enabled=true;noise.strength=0.025f;noise.frequency=0.35f;noise.scrollSpeed=0.12f;noise.quality=ParticleSystemNoiseQuality.Low;
            }
            var sound=root.GetComponent<UnderwaterSoundscape>();if(!sound)sound=Undo.AddComponent<UnderwaterSoundscape>(root);
            sound.motor=motor;sound.propagated=Object.FindObjectsOfType<UnderwaterAcousticAudio>();
            sound.ambience=Source("Water Ambience - listener local",motor.origin.Camera.transform,"WaterFlowAndBreathing.wav",0.45f,true);
            sound.testTone=Source("Audio Output Test - left right both",motor.origin.Camera.transform,"AudioRouteTest_LeftRightBoth.wav",0.65f,false);
            foreach(var a in sound.propagated){Undo.RecordObject(a,"Repair audition source");a.auditionVolume=0.38f;a.GetComponent<AudioSource>().priority=16;}
            var perf=Object.FindObjectOfType<UnderwaterPerformance>();Undo.RecordObject(perf,"Keep Live Preview sharp");perf.soundscape=sound;perf.minimumPreviewScale=0.95f;perf.showHud=false;
            // Prevent half-resolution refraction from looking conspicuously soft in the HMD.
            var pipeline=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Underwater/Settings/UnderwaterURP.asset");
            var serialized=new SerializedObject(pipeline);serialized.FindProperty("m_OpaqueDownsampling").intValue=0;serialized.ApplyModifiedProperties();
            ImportAudio("ROVMotorAndPing.wav");ImportAudio("WaterFlowAndBreathing.wav");ImportAudio("AudioRouteTest_LeftRightBoth.wav");
            EnableEditorAudio();
            Undo.CollapseUndoOperations(undo);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Audit();Selection.activeGameObject=root;
        }
        static AudioSource Source(string name,Transform parent,string clip,float volume,bool loop)
        {
            var t=parent.Find(name);if(!t){var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Add water audio cue");go.transform.SetParent(parent,false);t=go.transform;}
            var source=t.GetComponent<AudioSource>();if(!source)source=Undo.AddComponent<AudioSource>(t.gameObject);
            source.clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Underwater/Audio/"+clip);source.volume=volume;source.loop=loop;source.spatialBlend=0;source.priority=0;source.playOnAwake=false;
            if(!source.clip)throw new Exception("Missing audio clip "+clip);return source;
        }
        static void ImportAudio(string name)
        {
            var importer=(AudioImporter)AssetImporter.GetAtPath("Assets/Underwater/Audio/"+name);var sample=importer.defaultSampleSettings;
            sample.loadType=AudioClipLoadType.DecompressOnLoad;sample.preloadAudioData=true;importer.defaultSampleSettings=sample;importer.SaveAndReimport();
        }
        static PropertyInfo AudioMuteProperty()=>typeof(EditorUtility).GetProperty("audioMasterMute",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
            ?? typeof(UnityEditorInternal.InternalEditorUtility).GetProperty("audioMasterMute",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        public static void EnableEditorAudio()
        {
            var property=AudioMuteProperty();if(property!=null && property.CanWrite)property.SetValue(null,false);
        }
        [MenuItem("Tools/Underwater/Research/5 Diagnose Preview Audio")]
        public static void Audit()
        {
            Directory.CreateDirectory("Logs/UnderwaterPresence");
            string mute=AudioMuteProperty()?.GetValue(null)?.ToString()??"unknown";
            File.WriteAllText("Logs/UnderwaterPresence/editor-audio.txt","Editor playing: "+EditorApplication.isPlaying+"\nEditor audio master mute: "+mute+"\n"+
                "Listeners: "+Object.FindObjectsOfType<AudioListener>().Length+"\n"+
                string.Join("\n",Object.FindObjectsOfType<AudioSource>().Select(s=>$"{s.name}: enabled={s.enabled}, clip={s.clip}, playing={s.isPlaying}, mute={s.mute}, volume={s.volume}, spatial={s.spatialBlend}"))+"\n"+
                "Live Preview picture is not evidence of headset audio delivery. Unity outputs to the PC audio endpoint. Use the left/right/both cue to verify the selected Windows output / PC headphones.\n");
        }
    }
}
