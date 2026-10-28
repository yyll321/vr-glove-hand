using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace UnderwaterBaseline.Editor
{
    public static class UnderwaterPrecisionReleaseUpgrade
    {
        [MenuItem("Tools/Underwater/Research/30 Precision Arm, Faster Fingers and Natural Release")]
        public static void Apply()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=UnderwaterRadarSurveyTools.ScenePath)throw new Exception("Open survey scene outside Play");
            Directory.CreateDirectory("Assets/Underwater/Backups");
            EditorSceneManager.SaveScene(scene,"Assets/Underwater/Backups/Survey-beforePrecisionRelease-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",true);
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>())
            {
                var arm=task.arm;var map=arm.MasterSlave;
                map.slavePositionRange=new Vector3(.05f,.04f,.05f);
                map.maxSpeed=.09f;map.maxAcceleration=.30f;
                map.fineMaxSpeed=.035f;map.fineMaxAcceleration=.14f;
                map.positionResponseSeconds=.16f;map.filterSeconds=.09f;
                map.enterInteractionDistance=.35f;map.exitInteractionDistance=.45f;
                arm.DegreesPerSecond=45;arm.MaximumJointAcceleration=180;
                var hold=task.glove.GetComponent<Wa100JointPoseHold>();
                hold.maximumCommandSpeed=150;hold.maximumOpeningSpeed=200;
                hold.stiffness=30000;hold.damping=220;hold.forceLimit=180;
                task.glove.SmoothingSpeed=28;
                foreach(var body in task.glove.GetComponentsInChildren<ArticulationBody>(true))
                {body.maxJointVelocity=5;EditorUtility.SetDirty(body);}
                // Preserve pressure/depth/area laws; increase active actuator effort.
                var bench=task.GetComponentInChildren<UnderwaterGraspBench>();bench.fingerForceBudget=24;
                task.releaseOpeningDelta=.12f;task.releaseDwellSeconds=.06f;
                foreach(var target in task.targets)
                {
                    var body=target.GetComponent<Rigidbody>();body.useGravity=true;body.detectCollisions=true;
                    body.isKinematic=false;body.constraints=RigidbodyConstraints.None;
                    body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;body.interpolation=RigidbodyInterpolation.Interpolate;
                    var collider=target.GetComponent<Collider>();collider.enabled=true;collider.isTrigger=false;
                    EditorUtility.SetDirty(body);EditorUtility.SetDirty(collider);
                }
                foreach(var o in new Object[]{map,arm,hold,task.glove,bench,task})EditorUtility.SetDirty(o);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
    }
}
