using System;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;

namespace UnderwaterBaseline.Editor
{
    // Automated hand/physics fixtures do not need PICO rendering. Avoid repeatedly
    // starting/stopping the native Live Preview texture queues during these tests.
    [InitializeOnLoad]
    public static class UnderwaterPhysicsTestMode
    {
        const string Pending="UnderwaterPhysicsTestMode.Pending",Original="UnderwaterPhysicsTestMode.Original";
        static UnderwaterPhysicsTestMode()
        {
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredEditMode)Restore();};
            EditorApplication.delayCall+=()=>{if(!EditorApplication.isPlayingOrWillChangePlaymode)Restore();};
        }
        static XRGeneralSettings Settings=>XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        public static void Enter()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play before physics test");
            var settings=Settings;
            if(settings)
            {
                if(!SessionState.GetBool(Pending,false)){SessionState.SetBool(Original,settings.InitManagerOnStart);SessionState.SetBool(Pending,true);}
                settings.InitManagerOnStart=false;
            }
            EditorApplication.isPlaying=true;
        }
        public static void Restore()
        {
            if(!SessionState.GetBool(Pending,false))return;
            var settings=Settings;if(settings)settings.InitManagerOnStart=SessionState.GetBool(Original,true);
            SessionState.SetBool(Pending,false);
        }
    }
}
