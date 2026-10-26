# Unity / PICO Live Preview 退出修复

2026-10-26。

## 日志证据

本次原始日志保留在 `Logs/UnderwaterCrashFix/crash-20261026-1952-dx12-editor.log`。
19:52 进入 Play、Live Preview 连接后，编辑器发生原生崩溃。栈顶是 `PxrLivePreview.dll`，下层为 Unity 的 `UnityXRDisplayGraphicsThreadProvider::Adapt_PopulateNextFrameDesc`、`XRDisplaySubsystem::GfxThread::FrameBoundary` 和 `GfxDeviceD3D12::InsertCustomMarkerCallbackAndDataWithFlags`。

编辑器是通过重建项目的 DX12 启动方式打开的，PICO Live Preview 包版本为 1.0.5。日志中的 `LP_GetHandTrackerJointLocations` 是无私有符号时打印的最近导出符号，不能据此认定手套 JSON、机械臂 IK 或 PICO 手追踪代码是根因。可以确认的是本次退出发生在 Live Preview 的原生显示提交路径，无法由 C# try/catch 修复。

## 修改

- Windows 图形接口优先级从 DX12、DX11 改成 DX11、DX12。保留 DX12 供另一个重建场景使用。
- 新增 `Tools/Open-UnderwaterUnity.ps1`，明确传入 `-force-d3d11`。脚本不会结束已有编辑器，也不会删除工程锁文件。
- 新增 `UnderwaterPreviewCompatibility`。如果 Standalone 自动启动 Live Preview 且当前编辑器不是 DX11，在进入 Play 前取消启动并写明原因，避免再次进入已发生原生崩溃的组合。关闭该 XR loader 的 DX12 重建工作仍可使用。
- `previewcompatibility` 命令检查运行图形接口和配置；`previewstability` 命令进行三轮、每轮 20 秒的编辑器 Play 测试，期间开关尾流、工作灯和播放测试音，自动退回编辑模式。
- 新增稳定性报告，不将独立 Windows player 的检查视为 Live Preview 插件验证。

## 启动

正常关闭编辑器后，在项目目录运行：

```powershell
& .\Tools\Open-UnderwaterUnity.ps1
```

窗口标题应显示 `<DX11>`。如运行重建用的 `Restart-UnityDX12.ps1`，不要在启用 Standalone Live Preview 的状态进入 Play；需要先关闭该 loader，或重新使用水下 DX11 启动脚本。

场景、ROV 位置、手套、机械臂、水流、泥沙和灯光资产没有回滚。

## 验证记录

`Logs/UnderwaterCrashFix/compatibility.txt` 记录实际 API；`preview-cycle-1/2/3.json` 和 `stability.json` 记录真实编辑器测试，不代表用户已确认头显体验。

已完成真实 PICO Live Preview 三轮启停，每轮运行 20 秒。三轮均为 DX11、头显处于激活状态、双眼渲染开启，每眼纹理为 1920×1920，分别生成 1217、1194、1130 帧焦散图，运行错误列表均为空，测试音 PCM 峰值约 0.262。测试中切换尾流与灯光，退出 Play 后编辑器仍正常运行。此结果验证了本机本次 DX11 规避路径，不代表已修补闭源插件的 DX12 原生实现。
