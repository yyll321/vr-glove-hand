using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterEditorBridge
    {
        static bool busy;
        static UnderwaterEditorBridge() { EditorApplication.update += Poll; }
        static void Poll()
        {
            const string request = "Logs/UnderwaterCommand.txt";
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(request)) return;
            busy = true;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if(command=="rigaudit")
                {
                    UnderwaterRigUpgrade.Audit();
                    File.WriteAllText("Logs/UnderwaterCommandResult.txt", "PASS rig audit "+DateTime.Now.ToString("O"));
                    return;
                }
                if(command=="armaudit")
                {
                    UnderwaterArmContactUpgrade.Audit();
                    File.WriteAllText("Logs/UnderwaterCommandResult.txt", "PASS arm audit "+DateTime.Now.ToString("O"));
                    return;
                }
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode before generation.");
                Directory.CreateDirectory("Assets/Underwater/Backups");
                for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                {
                    var scene = EditorSceneManager.GetSceneAt(i);
                    if (scene.isDirty) EditorSceneManager.SaveScene(scene, "Assets/Underwater/Backups/" + scene.name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
                }
                File.WriteAllText("Logs/UnderwaterCommandResult.txt", "RUNNING " + command);
                if (command == "build") UnderwaterBaselineBuilder.BuildAll();
                else if (command == "scene") UnderwaterBaselineBuilder.BuildScene();
                else if (command == "validate") UnderwaterBaselineBuilder.Validate();
                else if (command == "research") UnderwaterResearchSceneBuilder.Create();
                else if (command == "researchbuild") UnderwaterResearchSceneBuilder.Build();
                else if (command == "researchopen") UnderwaterResearchSceneBuilder.Open();
                else if (command == "presence") UnderwaterPresenceUpgrade.Apply();
                else if (command == "audioaudit") UnderwaterPresenceUpgrade.Audit();
                else if (command == "previewcheck") UnderwaterLivePreviewCheck.Run();
                else if (command == "previewstability") UnderwaterLivePreviewCheck.RunStability();
                else if (command == "seabedpreviewcheck") UnderwaterLivePreviewCheck.RunSeabed();
                else if (command == "previewcompatibility") UnderwaterPreviewCompatibility.Verify();
                else if (command == "xrdiagnose") UnderwaterXrSubmissionAudit.Run();
                else if (command == "opticsupgrade") UnderwaterOpticsUpgrade.Apply();
                else if (command == "rigupgrade") UnderwaterRigUpgrade.Apply();
                else if (command == "armcontactupgrade") UnderwaterArmContactUpgrade.Apply();
                else if (command == "flowupgrade") UnderwaterFlowInteractionUpgrade.Apply();
                else if (command == "seabedvisibility") UnderwaterSeabedVisibilityUpgrade.Apply();
                else if (command == "headposeupgrade") UnderwaterHeadPoseUpgrade.Apply();
                else if (command == "physicsupgrade") UnderwaterPhysicsUpgrade.Apply();
                else if (command == "radaropen") UnderwaterRadarSurveyTools.Open();
                else if (command == "radaraudit") UnderwaterRadarSurveyTools.Audit();
                else if (command == "radarsmoke") UnderwaterRadarSurveySmoke.Run();
                else if (command == "radarreflection") UnderwaterRadarSurveyTools.BakeReflection();
                else if (command == "densesurvey") UnderwaterDenseSurveyUpgrade.Apply();
                else if (command == "handreefclearance") UnderwaterDenseSurveyUpgrade.RepairHandClearance();
                else throw new Exception("Unknown command " + command);
                File.WriteAllText("Logs/UnderwaterCommandResult.txt", "PASS " + DateTime.Now.ToString("O"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                File.WriteAllText("Logs/UnderwaterCommandResult.txt", exception.ToString());
            }
            finally { busy = false; }
        }
    }
}
