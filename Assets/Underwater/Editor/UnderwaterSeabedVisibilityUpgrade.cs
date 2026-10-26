using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterSeabedVisibilityUpgrade
    {
        [MenuItem("Tools/Underwater/Research/12 Apply Seabed Haze and Suspension")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play before saving seabed visibility.");
            UnderwaterResearchSceneBuilder.Open();
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/UnderwaterResearch-beforeSeabedHaze-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            var environment=Object.FindObjectOfType<UnderwaterEnvironment>();
            environment.seabedParticleMultiplier=1.7f;EditorUtility.SetDirty(environment);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Underwater/Settings/UnderwaterRenderer.asset");
            foreach(var feature in renderer.rendererFeatures)if(feature is UnderwaterHorizonFeature water)
            {
                water.seaCausticStrength=0;water.seaBottomHaze=0.38f;water.seaHazeTop=8;water.seaBlurPixels=3.5f;
                EditorUtility.SetDirty(water);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            UnderwaterResearchSceneBuilder.Validate();
        }
    }
}
