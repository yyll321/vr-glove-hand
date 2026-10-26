# 水池 / 自然水底 ROVp 对比基线

最新可编辑研究版见 `UNDERWATER_RESEARCH.md` 与 `Assets/Underwater/Scenes/UnderwaterResearchEditable.unity`，增加持久可编辑对象、更细密颗粒、Gerstner 焦散与分层射线声学；以下记录上一版基线。

Unity 2022.3.15f1c1。运行 `Builds/UnderwaterBaseline/UnderwaterBaseline.exe`，或打开 `Assets/Underwater/Scenes/UnderwaterBaseline.unity` 后 Play。EXE 的 Data、MonoBleedingEdge 和 DLL 请一起保留。

## 本轮内容

- 水池内部宽 25 m、长 50 m，池底 Y=0，水面 Y=10，深 10 m。25 cm 瓷砖使用世界坐标铺设，保留泳道线、T 形端标、壁面靶标、米制深度刻度和池岸。
- 第二个水域位于 X=62，内部同为 25×50×10 m；包含起伏砂床、细沙颗粒和沙纹法线、风化岩块及弯曲海草。自然区边界碰撞保留，水下不显示方盒墙面。
- 两区使用同一份完整 ROVp，以源工程比例放置在底部。接触阴影、轻微动态焦散、水面高光和波纹、有限水域内的雾和 RGB 吸收提供深度参照。自然区比水池浑浊。
- 三层世界空间悬浮颗粒，高 / 中 / 低档上限 780 / 624 / 429，气泡每次 14、上限 48。传送后重建局部颗粒体积。

## 操作

| 动作 | 桌面 | PICO 手柄 |
| --- | --- | --- |
| 连续移动 | WASD | 左摇杆 |
| 视角 / 转向 | 按住鼠标右键拖动 | 头部追踪；右摇杆左右 30° 转向 |
| 上浮 / 下潜 | E / Q | 右 A / B |
| 传送 | 按住 T 瞄准，松开确认 | 右摇杆前推瞄准，回中确认 |
| 水面 / 水下检查点 | F 或面板按钮 | 左 X |
| 水池 / 自然区 | Tab 或面板按钮 | 按下右摇杆 |
| 气泡 | B | 左 Y |
| 低 / 中 / 高档 | 1 / 2 / 3 | 按下左摇杆循环 |
| 抓放橙色检查标记 | 距离 2.5 m 内对准后 G | 直接或射线指向，握把抓放 |
| 重置所在区域观察位 | R | 左 X 往返检查点 |
| 开关悬浮颗粒 | P | Inspector Snow Enabled |
| 显示 / 隐藏状态和帮助 | H | 世界空间状态面板 |

键鼠始终独立读取，不再因为检测到 XR 而被屏蔽。PICO 摇杆和按钮同时支持 Input System 的 XRController / thumbstick 路径与 Unity XR CommonUsages 回退。状态面板显示左右手输入和最后一次操作。编辑器验收时先点击 Game 视图；独立 EXE 可避免编辑器焦点干扰。

传送绿色表示落点可用，橙色表示不可用。池底、自然砂床和平台可传送，会检查坡度和头部空间。唯一水下运动脚本管理 XR Origin，关闭 Starter Assets 的竞争移动 Provider。中性浮力、主动加速和平方速度阻力生效，松开立即停稳。转向同步 Rigidbody 姿态，防止物理插值覆盖旋转。

## ROVp 来源和保留范围

来源为只读的 `F:/unity/en/Assets/shenghao_getcar.unity` 中的 ROVp 父对象实例。已迁移机械臂、附加关节、嵌套 `WA100_R_URDF_Official` 手部、网格、材质和组件；完整副本位于 `Assets/Underwater/Imported/ROVpComplete.prefab`。

每份有 144 个 Transform 节点、28 个 Renderer，源比例下整体包围盒约 2.10×2.59×3.64 m。两区比例相同，仅平移至各自底面。源工程和目标工程中同 GUID 的 WA100 预制体内容不同，已单独导入源完整网格并重映射 GUID，没有覆盖目标原预制体。

参考体保留层级和组件，关闭原运动脚本、外部驱动、相机和音频监听，固定刚体 / 关节，避免旧 W/S 抓爪和 ROS 驱动干扰游览。跨出 ROVp 层级的场景引用设为空，详见 `Logs/ROVpMigration.json`。`JXB_angles_input` 的外部 ROS 字段改用 MonoBehaviour 引用与反射读取 arm1/2/3，去掉未使用的 OpenCV using，避免为静态参考体安装 ROS/OpenCV。

迁移证据：`Logs/ROVpHierarchy.txt`、`Logs/ROVpDependencyClosure.json`、`Logs/UnderwaterComparisonChecks.txt`。URDF 组件由已安装的 URDF Importer 提供。

## 性能与运行依赖

目标 72 FPS / 13.9 ms。面板显示帧时间、可用 CPU/GPU 时间、粒子数、档位和分辨率。桌面 / Live Preview 使用 0.70–1.00 URP 比例兜底，超预算可降颗粒档位；手动选档关闭自动降档，可从面板重新启用。运行时克隆 URP Asset，退出恢复。

PICO 预览需启动本机 Live Preview 服务并连接头显。Standalone 只启用 PICO Live Preview，Direct3D11；Android 配置 PICO Loader、72 Hz、原生 Adaptive Resolution（0.7–1.26，BALANCED），原生路径固定 URP renderScale=1。本轮交付 Windows EXE，没有生成 APK。

两个包使用本机路径，换电脑时需调整 manifest：

- `F:/UserData/softwaredownload/pico/PICO-Unity-Integration-SDK-release_3.4.0`
- `F:/Browser Download/Unity Live Preview Plugin-1.0.5-20250211`

原工程 Built-in 管线已在上一版切至 URP，原 HandDriver 场景旧材质需另行转换。未保存的 Sample 场景在生成前另存为 `Assets/Underwater/Backups/Sample-20261020-184302.unity`。源 en 工程未修改。

## 已验证与现场验收

Windows 构建成功，0 错误；39 项 Player 检查全部通过。RTX 4070 Laptop 本轮采样约 72 FPS / 13.89 ms，GPU 计时不可用。这是桌面结果，不代表 PICO 真机性能。

测试通过 Input System 事件覆盖键鼠移动、升降、传送、区域和水面切换、G 抓放、画质、颗粒，以及模拟 XRController 的左右摇杆、A/B/X/Y 和摇杆按下。真实握把抓取、头部 / 手柄追踪、双眼画面、串流延迟和 PICO 真机 72 Hz 仍需戴头显确认。

结果：`Logs/UnderwaterBuildResult.txt`、`Logs/UnderwaterSmoke/results.json`。截图：同目录的 `01-underwater.png`、`02-above-water.png`、`03-natural-seabed.png`。

编辑器打开时可用以下方式重建和检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Run-UnderwaterChecks.ps1 -Action Refresh
Set-Content Logs/UnderwaterCommand.txt build
# 等 Logs/UnderwaterCommandResult.txt 显示 PASS：
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Run-UnderwaterChecks.ps1 -Action Smoke
```

命令桥为未保存场景先保存带时间戳的副本。也可使用 `Tools > Underwater` 菜单。`-underwater-smoke` 只在检查时启用，普通运行不会注入按键或自动退出。
