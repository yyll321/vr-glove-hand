using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using Object=UnityEngine.Object;

namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterHeadPoseUpgrade
    {
        [MenuItem("Tools/Underwater/Research/13 Restore PICO Head Pose Driver")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play before restoring the head pose driver.");
            UnderwaterResearchSceneBuilder.Open();var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/UnderwaterResearch-beforeHeadPose-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            var camera=Object.FindObjectOfType<UnderwaterMotor>().origin.Camera;
            var driver=camera.GetComponent<TrackedPoseDriver>();
            if(!driver)throw new Exception("Main Camera has no TrackedPoseDriver.");
            driver.enabled=true;driver.updateType=TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;driver.trackingType=TrackedPoseDriver.TrackingType.RotationAndPosition;
            var bridge=camera.GetComponent<UnderwaterHeadTracking>();
            if(!bridge)bridge=camera.gameObject.AddComponent<UnderwaterHeadTracking>();bridge.eye=camera;bridge.preferTrackedPoseDriver=true;
            EditorUtility.SetDirty(driver);EditorUtility.SetDirty(bridge);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/UnderwaterHeadPose");File.WriteAllText("Logs/UnderwaterHeadPose/configuration.txt",$"PASS {DateTime.Now:O}\nTPD enabled={driver.enabled}, update={driver.updateType}, tracking={driver.trackingType}\nBridge preferTrackedPoseDriver={bridge.preferTrackedPoseDriver}\n");
        }
    }
}
