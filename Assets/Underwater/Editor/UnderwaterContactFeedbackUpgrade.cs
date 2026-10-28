using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterContactFeedbackUpgrade
    {
        [MenuItem("Tools/Underwater/Research/28 Hide Delivery Marker and Enable All Hand Contact Haptics")]
        public static void Apply()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=UnderwaterRadarSurveyTools.ScenePath)throw new Exception("Open survey scene outside Play");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/Survey-beforeContactFeedback-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>())
            {
                task.showDeliveryMarker=false;task.RefreshDeliveryMarker();
                if(task.deliveryZone)foreach(var renderer in task.deliveryZone.GetComponentsInChildren<Renderer>(true))EditorUtility.SetDirty(renderer);
                var c=task.arm.Contact;
                var feedback=c.GetComponent<Wa100ContactHaptics>()??c.gameObject.AddComponent<Wa100ContactHaptics>();
                feedback.contact=c;feedback.task=task;
                feedback.amplitude=.8f;feedback.pulseSeconds=.12f;feedback.repeatSeconds=.18f;
                c.Haptics=feedback;c.HapticsEnabled=true;
                EditorUtility.SetDirty(task);EditorUtility.SetDirty(c);EditorUtility.SetDirty(feedback);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
    }
}
