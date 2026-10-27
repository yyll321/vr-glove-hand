using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnderwaterBaseline.Editor
{
    [CustomEditor(typeof(UnderwaterHydrodynamics))]
    public sealed class UnderwaterHydrodynamicsInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();var model=(UnderwaterHydrodynamics)target;
            EditorGUILayout.HelpBox("Reduced-order drag and buoyancy estimates with uncalibrated coefficients. Force application is opt-in. Reference ROVs remain fixed.",MessageType.Info);
            using(new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field("Relative velocity m/s",model.EstimatedRelativeVelocity);
                EditorGUILayout.Vector3Field("Estimated drag N",model.EstimatedDragForce);
                EditorGUILayout.Vector3Field("Estimated buoyancy N",model.EstimatedBuoyancy);
                EditorGUILayout.Vector3Field("Angular resistance Nm",model.EstimatedTorque);
                EditorGUILayout.FloatField("Dynamic pressure Pa",model.DynamicPressure);
                EditorGUILayout.FloatField("Submerged fraction",model.SubmergedFraction);
            }
        }
        public override bool RequiresConstantRepaint()=>Application.isPlaying;
    }
    [CustomEditor(typeof(UnderwaterCaustics))]
    public sealed class UnderwaterCausticsInspector : UnityEditor.Editor
    {
        UnityEditor.Editor waveEditor;
        void OnDisable(){if(waveEditor)DestroyImmediate(waveEditor);}
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();var system=(UnderwaterCaustics)target;
            if(system.settings)
            {
                EditorGUILayout.Space();EditorGUILayout.LabelField("Wave controls",EditorStyles.boldLabel);
                CreateCachedEditor(system.settings,null,ref waveEditor);waveEditor.OnInspectorGUI();
            }
            EditorGUILayout.HelpBox("Same waves drive water geometry, normal map and refracted area caustic map. Maps are generated once per frame and shared by both eyes. CPU submit time excludes GPU execution.",MessageType.Info);
            if(system.NormalMap){EditorGUILayout.LabelField("Surface normals");GUILayout.Label(system.NormalMap,GUILayout.Width(240),GUILayout.Height(180));}
            if(system.CausticMap){EditorGUILayout.LabelField("Area-ratio caustic irradiance");GUILayout.Label(system.CausticMap,GUILayout.Width(240),GUILayout.Height(180));}
            EditorGUILayout.LabelField("CPU submission",system.CpuSubmitMs.ToString("F3")+" ms");
        }
        public override bool RequiresConstantRepaint()=>true;
    }
    [CustomEditor(typeof(UnderwaterAcousticModel))]
    public sealed class UnderwaterAcousticModelInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();var model=(UnderwaterAcousticModel)target;
            EditorGUILayout.HelpBox("M: mute/unmute audition. J: restart motor / ping signal. O: toggle selected ray gizmos. Physical output is relative to 1 µPa; audition is normalized and not SPL calibrated. Seven bands, elastic reflections, layered Snell refraction, Diffuse Rain, frame-budgeted tracing.",MessageType.Info);
            var s=model.Latest;
            if(s!=null)
            {
                EditorGUILayout.LabelField("Trace CPU total / wall time",$"{s.computeMs:F2} ms / {s.elapsedSeconds:F2} s");
                EditorGUILayout.LabelField("Segments / arrivals",$"{s.tracedSegments} / {s.arrivals.Length}");
                EditorGUILayout.LabelField("Direct delay",$"{s.directDelaySeconds*1000:F3} ms");
                for(int i=0;i<7;i++)EditorGUILayout.LabelField(UnderwaterAcousticsMath.Frequencies[i]+" Hz",s.bandLevelsDb[i].ToString("F2")+" dB re 1 µPa (model)");
                if(GUILayout.Button("Export current propagation snapshot"))
                {Directory.CreateDirectory("Logs/UnderwaterResearch");model.Export("Logs/UnderwaterResearch/"+model.name+".json");}
            }
        }
        public override bool RequiresConstantRepaint()=>Application.isPlaying;
    }
}
