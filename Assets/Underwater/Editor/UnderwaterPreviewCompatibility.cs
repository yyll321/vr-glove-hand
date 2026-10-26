using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnderwaterBaseline.Editor
{
    // Live Preview 1.0.5 crashed natively in PopulateNextFrameDesc on DX12.
    // Refuse that combination before runtime XR startup, where C# cannot catch it.
    [InitializeOnLoad]
    public static class UnderwaterPreviewCompatibility
    {
        static UnderwaterPreviewCompatibility()
        {EditorApplication.playModeStateChanged+=BeforePlay;}

        public static bool Unsupported(GraphicsDeviceType api,bool livePreviewEnabled)
        {return livePreviewEnabled && api!=GraphicsDeviceType.Direct3D11;}

        static bool LivePreviewEnabled()
        {
            var settings=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            return settings && settings.InitManagerOnStart && settings.AssignedSettings &&
                settings.AssignedSettings.activeLoaders.Any(l=>l && l.GetType().FullName=="Unity.XR.PICO.LivePreview.PXR_PTLoader");
        }

        static void BeforePlay(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.ExitingEditMode || !Unsupported(SystemInfo.graphicsDeviceType,LivePreviewEnabled()))return;
            EditorApplication.isPlaying=false;
            const string message="PICO Live Preview 1.0.5 Play was stopped before native XR startup: this editor must use Direct3D11. Close Unity and reopen with Tools/Open-UnderwaterUnity.ps1 (-force-d3d11). DX12 previously crashed PxrLivePreview on its graphics thread. For Gaussian DX12 work, disable the Standalone Live Preview loader first.";
            Directory.CreateDirectory("Logs/UnderwaterCrashFix");File.WriteAllText("Logs/UnderwaterCrashFix/blocked-preview.txt",DateTime.Now.ToString("O")+"\nAPI: "+SystemInfo.graphicsDeviceType+"\n"+message);
            Debug.LogWarning(message);
        }

        [MenuItem("Tools/Underwater/Research/11 Verify Live Preview Compatibility")]
        public static void Verify()
        {
            bool policy=Unsupported(GraphicsDeviceType.Direct3D12,true) && !Unsupported(GraphicsDeviceType.Direct3D11,true) && !Unsupported(GraphicsDeviceType.Direct3D12,false);
            if(!policy)throw new Exception("Preview compatibility policy failed.");
            var apis=PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            if(apis.Length==0 || apis[0]!=GraphicsDeviceType.Direct3D11)throw new Exception("Windows graphics API must prefer Direct3D11 for PICO Live Preview.");
            if(Unsupported(SystemInfo.graphicsDeviceType,LivePreviewEnabled()))throw new Exception("Reopen Unity with -force-d3d11 before starting Live Preview.");
            Directory.CreateDirectory("Logs/UnderwaterCrashFix");
            File.WriteAllText("Logs/UnderwaterCrashFix/compatibility.txt",$"PASS {DateTime.Now:O}\nEditor API: {SystemInfo.graphicsDeviceType}\nWindows APIs: {string.Join(", ",apis)}\nLive Preview enabled: {LivePreviewEnabled()}\nPolicy: DX12+Live Preview blocked; DX11 allowed; DX12 without Live Preview allowed.\n");
        }

        public static void OpenAndVerify()
        {
            UnderwaterResearchSceneBuilder.Open();Verify();
            // A file request survives startup modal dialogs and asset refreshes.
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/UnderwaterCommand.txt","previewstability");
        }
    }
}
