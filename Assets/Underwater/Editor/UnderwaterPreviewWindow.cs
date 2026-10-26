using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;

namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterPreviewWindow
    {
        static UnderwaterPreviewWindow()
        {EditorApplication.playModeStateChanged+=Mode;EditorApplication.update+=Tick;}
        static void Mode(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode || !UnityEngine.Object.FindObjectOfType<UnderwaterMotor>())return;
            Application.runInBackground=true;
            // XR still needs the Game view's player loop when the Scene tab was selected.
            var type=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if(type!=null){var view=EditorWindow.GetWindow(type);view.Show();view.Focus();view.Repaint();}
        }
        static void Tick()
        {
            if(EditorApplication.isPlaying && XRSettings.isDeviceActive)EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
