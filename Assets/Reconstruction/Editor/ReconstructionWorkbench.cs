using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace UnderwaterReconstruction.Editor
{
    public sealed class ReconstructionWorkbench : EditorWindow
    {
        [MenuItem("Tools/Reconstruction/0 Open reconstruction workbench")]
        static void Open(){GetWindow<ReconstructionWorkbench>("3DGS 工作台");}
        void OnGUI()
        {
            EditorGUILayout.LabelField("水下 3DGS / NeRF 重建",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("图形 API",SystemInfo.graphicsDeviceType.ToString());
            if(!OfficialGaussianBackend.SupportedApi)
                EditorGUILayout.HelpBox("官方渲染器需要 DX12/Vulkan。当前使用兼容渲染器；重启编辑器加 -force-d3d12 后可使用官方路径。",MessageType.Info);
            if(GUILayout.Button("打开 / 完善重建场景"))OfficialGaussianSetup.Upgrade();
            var lab=Object.FindObjectOfType<ReconstructionLab>();
            if(lab && lab.official)
            {
                EditorGUILayout.ObjectField("官方模型",lab.official.rendererComponent.asset,typeof(GaussianSplatting.Runtime.GaussianSplatAsset),false);
                EditorGUILayout.LabelField("Gaussian 数量",lab.official.Count.ToString("N0"));
                if(GUILayout.Button("创建官方模型资产（先转换为标准 PLY）"))EditorApplication.ExecuteMenuItem("Tools/Gaussian Splats/Create GaussianSplatAsset");
                if(GUILayout.Button("把 Project 中选中的官方资产赋给场景"))OfficialGaussianSetup.AssignSelected();
                if(GUILayout.Button("相机对准官方模型"))FrameOfficial();
                EditorGUILayout.HelpBox("1：3DGS；2：NeRF；3：切换官方/兼容路径。官方资产与兼容资产需要分别导入同一份数据。默认内容是合成演示。",MessageType.None);
            }
            if(GUILayout.Button("生成 48 视角合成训练素材"))SyntheticTrainingCapture.Export();
            if(GUILayout.Button("打开训练与素材教程"))Application.OpenURL(System.IO.Path.GetFullPath("RECONSTRUCTION_NEXT_STEPS.md"));
            if(GUILayout.Button("打开 3DGS GitHub 项目"))Application.OpenURL("https://github.com/aras-p/UnityGaussianSplatting");
        }
        [MenuItem("Tools/Reconstruction/8 Frame official reconstruction")]
        static void FrameOfficial()
        {
            var lab=Object.FindObjectOfType<ReconstructionLab>();
            if(!lab||!lab.official||!lab.official.HasAsset)return;
            var renderer=lab.official.rendererComponent;var asset=renderer.asset;
            Vector3 center=renderer.transform.TransformPoint((asset.boundsMin+asset.boundsMax)*0.5f);
            float radius=(asset.boundsMax-asset.boundsMin).magnitude*renderer.transform.lossyScale.magnitude/Mathf.Sqrt(3)*0.5f;
            Undo.RecordObject(lab.rig,"Frame reconstruction");
            Vector3 desired=center-new Vector3(0,-0.25f,1).normalized*Mathf.Max(radius*1.8f,1.5f);
            lab.rig.position+=desired-lab.eye.transform.position;
            Undo.RecordObject(lab.eye.transform,"Frame reconstruction");lab.eye.transform.LookAt(center);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lab.gameObject.scene);
            if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(center,Quaternion.Euler(15,0,0),Mathf.Max(2,radius*2));
        }
    }
}
