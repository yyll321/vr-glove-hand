# 可编辑水下研究场景

打开 `Assets/Underwater/Scenes/UnderwaterResearchEditable.unity`，或菜单 `Tools > Underwater > Research > 2 Open Editable Scene and Show Objects`。该菜单清空 Hierarchy 引用筛选并选中水池 ROVp。截图中的 `ref:33148:` 会隐藏不匹配对象，也可直接点击搜索框右侧 X 清空。

本 Scene 的对象在编辑状态下已经存在，不依赖 Play 才生成。ROVp 预制体实例已展开为普通层级，可改 Transform、材质、组件和内部关节。两份参考体保留机械臂和 WA100 手部；运动和外部 ROS 驱动仍关闭。原水池基线保留，打开的新 Scene 不会自动重新生成。菜单 3 直接构建当前保存的研究 Scene；如需手调后的版本，先退出 Play 再保存。

## 可编辑对象

| Hierarchy 对象 | 内容 |
| --- | --- |
| ROVp_References / ROVp - Pool | 水池底部完整 ROVp，可逐个修改子物体 |
| ROVp_References / ROVp - Seabed | 自然区底部同一比例的参考体 |
| Pool_Structure | 池底、四壁、池岸和端部平台 |
| Pool_Accessories | 泳道、端标、深度标记、梯子、壁灯外壳、排水格栅、可抓取标记 |
| Pool_50x25x10m | 水池水面及保留设施 |
| NaturalSeabed_50x25x10m | 沙床、岩石、海草、边界和自然区水面 |
| Underwater Environment | 三层微小悬浮颗粒、气泡和性能控制 |
| Research_Systems | 波浪和焦散控制；子对象内有两区声学配置 |

粒子低 / 中 / 高预算为 5940 / 8640 / 10800，粒径约 2–8 mm；最大屏幕比例限制为 0.003，关闭强制最小像素尺寸，避免近处出现大白球。Live Preview 改进后，每层半径分别为 2 / 4.5 / 9 m，高度 3 / 5 / 7 m，将更多颗粒放在能提供双眼视差的近处。透明度分别为 0.65 / 0.45 / 0.3，配合深度软交界和距离衰减。颗粒在世界空间随变化的水流漂移，镜头附近和水域外会淡出。气泡仍为独立事件，不随悬浮颗粒密度成倍增加。

## 焦散论文到 URP 的实现

来源：Thompson、Chalmers、Rhee，2019，*Real-time Underwater Caustics for Mixed Reality 360° Videos*，第 3 节及式 (1)、(2)。原文是两页会议论文，描述方法而不附完整源码；这里重实现方法并适配全三维 URP 14 场景。

1. `UnderwaterWaveSettings` 的八组 Gerstner 波用于水面顶点、表面法线及折射射线，包含波向、幅度、陡峭度、传播速度及独立初相位。另有两组较弱的周期微波法线扰动。`GerstnerWaves.hlsl` 与 C# 参数对应。
2. 每帧在两眼相机渲染前生成 512×512 半精度法线/高度 RenderTexture。波向使用整数周期，使 12 m 纹理无缝平铺，代替原文的 90% 区域缩放和边缘混合。
3. 当前设置为 144×144 核心射线网格，从波面按空气/水折射率 1/1.333 投射到虚拟接收平面。采用折射前三角形面积 / 折射后面积作为亮度，由片元导数直接计算面积比；重叠三角形加法混合。网格四周各增加 20% 周期边界，保留折射进入纹理的邻接光线，以一次绘制代替九份完整周期网格。网格使用 SV_VertexID 程序生成，不依赖 Mesh 包围盒，也不再从 LateUpdate 覆盖相机视图/投影矩阵。焦散为带 mipmap 的 RHalf 纹理，法线为 ARGBHalf。
4. 首个 URP 深度重建 Pass 依据世界坐标和折射主光方向查焦散图，对池底、墙、砂床、岩石和 ROV 的可见不透明面统一施加焦散；接收面朝向、主光阴影、水深和有限水域限制亮度。先处理焦散，再处理水下吸收和雾，因此光斑也随视距衰减。Scene View 同样支持。
5. 水面使用细分网格位移、法线图、Fresnel、Opaque Texture 折射、环境反射、太阳高光与水下全反射。池边位移衰减避免穿墙。当前没有平面反射相机，水下全反射是环境/水色近似，不能视为完整路径追踪。

2026-10-21 针对“波光跟着视角移动、规律明显、光斑偏大”调整：深度重建使用每眼的归一化屏幕坐标，颜色拷贝单独使用 Blit UV，避免把 RTHandle 缩放带入几何位置重建。删除原来沿视线四点采样锐利焦散的散射亮带，改为对有限水域解析积分的柔和散射近似。细小焦散仅投在世界坐标下的接收表面；不再沿视线绘制重复花纹。

`GerstnerWaves.asset` 中 `Caustic Footprint=0.55`，将接收面图案相对原图缩小；`Caustic Variation=0.32`，混合旋转、不同尺度的第二次采样及少量固定世界坐标扭曲，弱化同图平铺。波向扩至八组，初相位和速度各异，主波振幅与微法线扰动减小，焦散强度由 0.35 降为 0.28。焦散纹理生成 mipmap，以未截断的连续 UV 和三线性过滤采样，减少远处及掠射角闪烁。双投影混合与 footprint 是本项目的实时视觉近似，不是额外的物理光线追踪，也不代表论文原算法的严格能量守恒结果。

复查位置：`Research_Systems > Underwater Caustics > Freeze Waves`。开启后固定在 `Frozen Time`：转头、侧移时，同一块瓷砖或 ROV 表面上的光斑应保持对应位置；关闭后，光斑随波浪时间演化。大小、重复感和强度在 `Assets/Underwater/Settings/GerstnerWaves.asset` 调整。更小的 Footprint 对远处像素分辨率要求更高，当前先使用 0.55。

在 `Research_Systems` 的 Underwater Caustics Inspector 中直接展开波浪设置，调整 Wave Scale、Caustic Strength、Projection Depth、Micro Normal Strength、Grid 和 Map Resolution，并预览两张实时图。两区共用波场及贴图，两眼共用一次生成成本。贴图参考接收深度统一为 8 m；不同高度由主光投影校正，而非为每个物体重新追迹焦散。

原文还包含 360 视频光源检测、逆色调映射、IBL 和差分合成。当前没有 360 视频，使用场景太阳和天空环境替代；没有把视频合成部分伪装为本项目已实现功能。论文的每眼 >120 FPS 和约 0.3 ms 焦散结果来自其 GTX1070、特定模型/视频场景，不能直接套到本工程或 PICO。

## 声传播论文到当前水域的实现

来源：de Castro、Malheiros、Bicho，2024，*An Underwater Sound Propagation Model based on Ray Tracing for Virtual Reality*，第 3 节、式 (2)–(11)、表 1。原文主要评估静态声源的传播估计，结论中说明复杂场景实时性及分层欠采样仍有限，音频渲染属于后续工作。

`UnderwaterAcousticModel` 实现：

- Fibonacci 球面发射，默认 1000 条射线；每条射线携带七频带压力、位置、方向、传播距离、累计时间、分层和交互次数。七频带为 31.5、63、125、250、500、1000、2000 Hz。
- 分段按论文式 (5)–(8) 计算温度、盐度、pH、深度相关的 Ainslie-McColm 吸收。频率转换为 kHz、深度转 km；dB/km 除以 8685.889638 得压力衰减 Np/m，段压力按 exp(-alpha*d) 更新。
- 水层平面解析求交；水-水界面使用声学 Snell 关系 sin(theta)/c 和流体阻抗压力反射/透射系数，同时分支，有临界角和全反射判断。每段都在单一水层内计算。
- 水-气界面只保留压力反射，使用空气 343 m/s、1.275 kg/m³，保留负压力的相位反转符号；不模拟空气中的后续传播。
- Collider 阻挡与固体反射；固体阻抗同时考虑纵波与剪切波，临界角使用复数余弦。砂、石灰岩、玄武岩采用论文表 1 数值；混凝土和金属为本工程的可编辑工程近似参数。
- 接收球检测与同层可见性检查的 Diffuse Rain；后者按接收立体角和材料散射分配权重。射线数、阈值、最大距离、最多交互次数、队列大小和每帧段数均设上限。默认每帧处理 192 段，每批结束 0.5 s 后重算，避免一次求解占满 VR 帧。
- 到达时间按各段 d/c 累计。导出包含七频带结果、到达路径、反射/折射次数、直接延迟及 CPU 累计计算时间的 JSON。源对象可直接在 Scene 移动。

水池默认单层淡水，25°C、盐度 0、密度 997 kg/m³、声速 1497 m/s。自然区默认三层：0–3、3–7、7–10 m，声速 1520、1508、1495 m/s，温度 20、16、12°C。它们是用于展示分层现象的可调配置，不是现场水文测量值。

原文压力均值估计与本项目试听输出的用途不同。项目额外启用了可关闭的 1/r 球面扩散、Diffuse Rain 权重及非相干到达能量合成，用于短距离水池中的稳定试听；不宣称与论文 4 km dBSea 实验具有同一数值结果。没有衍射、完整弹性透射、频率相关相干干涉或经过校准的 SPL。

`UnderwaterAcousticAudio` 是本项目扩展：七带滤波、路径分数近似为整数采样延时、最强 16 条到达和随头朝向变化的立体声方向。试听信号为生成的推进器谐波及短啁啾。不是 Resonance Audio HRTF，试听音量也没有声压标定。最远 2 s 延迟环形缓冲适用于当前小水域，禁止据此外推大范围海洋声学。

操作：M 或左手扳机静音/恢复；J 或右手扳机恢复音源并播放独立的左耳、右耳、双耳提示音；O 显示/隐藏选中声学模型的射线路径。Inspector 的 Export 按钮导出当前数据。水池和自然区各有一套声源与材料，接收点跟随 XR 相机，只试听当前水域。首次传播批次尚未完成或无到达时，提供直达延迟近似试听，物理传播 JSON 不受此工程试听回退影响。

## Live Preview 水流与音频修复

`Research_Systems` 新增 `UnderwaterPresence` 与 `UnderwaterSoundscape`，并在相机下面保存了 `Water Ambience - listener local` 和 `Audio Output Test - left right both`。ROVp、池体和用户手调的机械臂层级未重新生成。修改前备份在 `Assets/Underwater/Backups/UnderwaterResearch-beforePresence-*.unity`。

原 Live Preview 日志在焦散命令提交处持续出现 `Converting invalid MinMaxAABB`。焦散贴图现在使用明确的单眼离屏视图/投影、分辨率视口和紧凑网格包围盒，暂时关闭并恢复立体关键字，避免继承 XR 相机状态。水面折射改为读取吸收/雾处理后的颜色纹理，修复 URP 原 Opaque Texture 在雾之前复制导致水面下方过于清澈、发亮的问题。折射纹理使用完整分辨率，Live Preview 自动缩放下限为 0.95。

流场默认水池约 0.09 / 0.035 m/s（X/Z），自然区约 0.18 / 0.07 m/s，叠加缓慢变化的流速。海草随同一时间/位置场摆动；低成本四点水体光散射提供随波变化的空间线索。V 暂停/恢复水流表现。`Gentle Body Drift` 可在 Inspector 开启，默认关闭；头部跟踪保持直接响应。

水流、呼吸和细小气泡底噪在进入 Play 后即循环播放。J 会调用 Play，恢复脚本静音、AudioSource 静音、Listener 暂停和 Listener 音量，再播放不依赖传播批次的独立测试音。音频配置切换后会重新启动音源。试听底噪是程序合成效果，不是水流传感器数据或经过标定的水声记录。

本机检查发现默认 Windows 输出原为 `Realtek(R) Audio`，已为本次 PICO 测试切到 `Pico Streaming Speaker`（console/media 两项），通信输出保留。原路由保存在 `Logs/UnderwaterPresence/audio-route-before-switch.json`。恢复电脑扬声器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Set-UnderwaterAudioRoute.ps1 -Action Restore
```

Live Preview 的画面连接成功不能证明声音送达头显。先用 J / 右扳机的左右提示音确认。运行时音频状态写入 `Logs/UnderwaterPresence/audio-status.json`，编辑器菜单 `Tools > Underwater > Research > 5 Diagnose Preview Audio` 可另行记录源与 Listener 状态。

2026-10-20 用户最终确认“头显有声音了”。对应串流服务日志显示，20:52:22 已从 `Pico Streaming Speaker` 初始化并启动 loopback 采集，格式为双声道、48 kHz、16 bit。此前 20:41 会话因输出模式为 `kComputer` 且会话读取的默认设备不是 PICO，跳过了音频源启动。当前保留已可用路由；不能仅凭 `kComputer` 字样推断没有转发，必须同时检查实际捕获设备和 `StartCapturing`。若日后切换输出设备后再次无声，应先选定 PICO 输出，再重新进入 Play / 建立预览会话，使串流服务重新选择捕获设备。没有为本次确认继续更改系统音频设置。

头显音频通过不代表画质验收通过：同一串流会话报告 Unity 渲染尺寸为每眼 1920×1920，但 `effective_res` 为 1280×1280，视频为 72 Hz、约 30 Mbps；Unity 渲染尺寸不能直接代表头显收到的有效分辨率。后续用户运行日志仍出现 `Converting invalid MinMaxAABB`，以及 invalid world/local AABB，先前两次短时检查未报错不足以证明该问题已修复。头显画质、水流临场感和持续运行稳定性仍待进一步验证。

## 2026-10-21 头显观察与交互接入

当前场景保存了 `UnderwaterHeadTracking`（Main Camera）、两套 `PicoArmPoseDriver`（各 ROV 根对象）、一个 `HandDriver UDP - UdexReal port 5555` 接收器，以及已启用的两套 WA100 mapper/guard。接入前完整场景备份在 `Assets/Underwater/Backups/UnderwaterResearch-beforeRig-*.unity`。无需重新生成场景。

Live Preview 诊断曾发现：Legacy XR 接口报告有效头部位姿，而 Input System HMD 的部分同名控件返回零。新头部驱动优先读取 active XR provider 的 CenterEye/Head，Input System 作为后备，在 Update 和 BeforeRender 更新相机本地位姿；旧 TrackedPoseDriver 停用，由一个组件写相机，不写世界根节点。右摇杆的原地转向已取消，身体漂流默认关闭。水下深度后处理明确从 URP 当前相机数据上传每眼 inverse GPU view-projection 和眼位置，减少全局 XR 矩阵状态不一致。主相机重置自定义矩阵、开启双眼、停用遮挡剔除。

焦散生成移至 SRP 渲染之前；离屏 SingleCameraRequest 跳过帧回调时，由 renderer feature 的同一入口补充更新，每个运行帧共享一次。隐藏桌面测试窗口可能不自动绘制，所以检查先显式请求渲染，再读取贴图。没有以提高 camera FOV 来模拟 360 度；头部转动应让相机看到真实场景的其他方向。

| 输入 | 行为 |
| --- | --- |
| 左摇杆 ↑ / ↓ | 世界竖直方向上升 / 下降 |
| 左摇杆 ← / → | 沿观察方向的水平投影拉近 / 推远 |
| 左手 X | 水池 / 自然海底互切 |
| 右手握持键 + 手柄位姿 | 以按下时的腕部姿态校准，移动控制末端位置，转动控制腕部可用的扭转轴 |
| 松开右握持键 / 丢失追踪 | 保持机械臂姿态，再次握持重新建立相对关系 |
| 右摇杆按住点击 / 松开 | 瞄准传送 / 传送，握持机械臂控制时不激活 |
| UdexReal 手套 | WA100 手指关节 |
| J / 右扳机；M / 左扳机 | 测试音；静音 / 恢复 |

`PicoArmPoseDriver` 沿用 `F:/unity/en/Assets/Scripts/PicoArmPoseDriver.cs` 的相对位姿标定、实际连杆 CCD 和腕部 swing/twist 投影，绑定本场景 `Pool_JXBControl` 已保存的 Axis1p–Axis4p。轴顺序参考 `JXB_angles_input.SetAngle` 的 Y/Z/Z/Y。旧脚本固定连杆长度在 FK/IK 中不一致，未启用其固定长度求解或按键复位。四关节机械臂不能完整实现任意六自由度姿态：位置按实际可达范围求解，旋转只跟随腕部现有轴，误差显示在 `PositionError`。两区仅当前区域接管右手柄，保留各自姿态。

手套沿用原链路：HandDriver.exe → UDP JSON → `Network.Convert2Angle` → `Wa100UdexRealArticulationMapper` → 11 个 ArticulationBody 的 xDrive。Unity Play + Live Preview 的 HandDriver 目标仍为电脑 `127.0.0.1:5555`。启动 HandDriver、连接右手套，先自然张开手，第一包数据校准；Inspector 查看接收计数、角色、11 个关节绑定和 mapper 状态。当前两套手使用同一个接收器，切区无需改 UDP 目标。guard 还同步 WA100 固定物理根与机械臂腕部安装关系，避免手指关节的物理根停留在旧位置。

编辑器菜单 `Tools > Underwater > Research > 6 Diagnose Head Tracking and Arm` 写入 `Logs/UnderwaterRig/audit.txt`；菜单 7 是一次性场景升级入口，已执行，日常使用直接 Play。菜单 7 会备份并保存现有场景，不重建 ROV 或池体。

本次 Windows 构建成功，0 错误、2 警告；72 项桌面回归全部通过。包括两区各八个朝向（0–315°）无黑屏、改变头部姿态不移动世界根节点、合成左手 X 实际切区及切回、机械臂可达目标误差约 3 mm、腕部 30° 扭转、松开保持、WA100 跟随安装点，以及独立 UDP 15555 的 JSON→手指驱动检查。测试数据不发送至实际手套端口 5555。结果见 `Logs/UnderwaterResearch/results.json`、`checks.txt` 和 `Logs/UnderwaterRig/360-contact-sheet.png`。

另一次约 10 秒 Live Preview 检查确认 PXR_PTLoader、双眼激活、头部桥读取有效 XR 位姿，累计应用 130 次，渲染方向与相机方向差为 0°，检查期间 errors 为空。头显当时未执行人工完整转头验收；这些静态记录不能证明佩戴时侧后方黑屏或世界滑动已彻底消失。实际 UdexReal 手套未在此次检查中做佩戴弯指验收，UDP 驱动通过的是隔离端口的真实回环数据包。重新进入 Play 后，先确认头显左右及后方观察稳定，再按上述表格确认移动、切区、右握持机械臂与手套手指。

## 2026-10-21 随机水流与头显提交复查

用户再次确认：世界随转头移动、侧后方黑屏仍然存在。先前桌面八方向检查和 XR 激活记录不能作为头显问题已解决的证据。

`UnderwaterPresence` 改为固定种子的连续噪声流场，包含空间涡流、慢速阵流、竖向扰动。每个颗粒按自己的世界位置采样，10 Hz 更新并平滑趋近目标速度，复用粒子缓冲区，保留原密度和粒径预算；不同颗粒按稳定种子改变响应。水草按各自位置的流场弯曲，不再整排同步正弦摆动。默认不推动身体或场景根节点。`GerstnerWaves.causticFootprint` 从 0.55 恢复到 1，其他波浪参数保留。

新增编辑器桥命令 `xrdiagnose`：在停止 Play 时运行约 32 秒，记录真实 XR provider 的 render pass、双眼 viewport/view/projection，读取 provider 实际眼纹理；依次对比水下特效开启、关闭雾、关闭雾与焦散生成，退出时恢复设置。它会临时切换效果，仅用于诊断。`UnderwaterPreviewWindow` 在本项目进入 Play 时打开 Game 视图，并在 XR 活动时保持 player loop 更新。此次置前复测没有解决低帧率，不能宣称这是黑屏修复。

原始记录见 `Logs/UnderwaterXR/`。PICO LP provider 为两个 1920×1920 的 MultiPass，viewport 完整，渲染方向与当前相机一致；头显截图包含左右眼串流图像。但设备日志实际仅收到约 1–6 FPS，编码统计约 130–891 ms；关闭水下特效后也低。已确认初始化的是 RTX 4070 的 MFT 硬件编码器，不能仅凭 `mft` 字样认定软件编码。串流低帧率及投影姿态时序仍需继续核查。

20:23 左右读到 PICO 电量 **5%**、正在充电、温度 **25°C**、系统 `low_power=0`。低电量可能影响稳定性，目前没有证据证明它是唯一根因。先停止额外头显预览消耗，充电后重新验收实际左右/后方观察和转头稳定性。水流和波光修改可以先通过独立 Windows 回归检查。

20:25 本轮 Windows 构建成功（0 错误、18 警告），独立回归 73 项全部通过，包括局部流场时空差异、连续性、速度上限及现有焦散/交互检查。它不构成头显世界稳定性或 360° 观察已修复的证明。

## 构建与验证

2026-10-21 根据 Crest Water 5 官方资料进一步改进两区吸收/散射、透明颗粒的光学一致性、随机流场驱动水面法线、局部水池波强度和固定世界环境反射。未导入商业 Crest 插件。实现、来源及局限见 [CREST_5_REVIEW.md](CREST_5_REVIEW.md)。最终 Windows 构建 0 错误、18 警告，79 项检查通过；不代表此前头显跟随/黑屏问题已解决。

运行 `Builds/UnderwaterResearch/UnderwaterResearch.exe`。按 Tab 在两区切换，WASD 和右键游览，Q/E 升降，T 按住瞄准松开传送，F 水面/水下，H 隐藏帮助；PICO 操作使用上一节的新映射。

编辑器已打开时：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Run-UnderwaterResearchChecks.ps1 -Action Refresh
Set-Content Logs/UnderwaterCommand.txt researchbuild
# 等 Logs/UnderwaterCommandResult.txt 为 PASS
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Run-UnderwaterResearchChecks.ps1 -Action Check
```

构建输出 `Logs/UnderwaterResearchBuild.txt`。检查结果与截图在 `Logs/UnderwaterResearch/`，涵盖持久层级、粒径/数量、真实生成的焦散贴图、波浪平铺/变化、场景接收面差异、声学吸收单位、Snell 不变量、全反射、弹性固体、直达延迟、遮挡和分层射线行为。CPU submission 是向 GPU 提交命令的耗时，不是 GPU 生成焦散的耗时；真实 HMD 双眼显示、GPU 成本和帧率需硬件验收。

2026-10-20 最终构建成功，0 错误、1 警告；27 项运行检查全部通过。RTX 4070 Laptop GPU 的本次桌面测试帧耗时约 14.98 ms（约 67 FPS），焦散 CPU 命令提交约 0.092 ms；后者不代表焦散 GPU 渲染耗时，也不能据此推算头显每眼帧率。原始结果见 `Logs/UnderwaterResearch/results.json`。

同日 Live Preview 修复后的构建成功，0 错误、12 警告；新增水流/近场颗粒、J 按键恢复、独立测试音 PCM、首次传播完成前试听的检查后，共 33 项全部通过。桌面帧耗时约 14.94 ms。连接中的 PICO Live Preview 另做两次短时运行，确认 XR 激活、双眼渲染、每眼 1920×1920，焦散分别运行 649 / 646 帧，检查期间错误列表为空；独立测试音 PCM 峰值约 0.263。最新记录在 `Logs/UnderwaterPresence/live-preview-check.json`。这些数据验证程序路径，头显扬声器是否可听及临场感仍需佩戴试听/观察确认。

2026-10-21 焦散调整后 Windows 构建成功，0 错误、18 警告，37 项检查全部通过。新增固定波浪时间的真实接收面渲染比较：64 mm 相机间距（2809 点）、相机平移转动（2438 点）、相机移动且渲染缩放至 0.65（2438 点），对应平均亮度差为 0.0014 / 0.0113 / 0.0129（0–1 范围，包含过滤和采样误差）；相邻图块不再相同。64 mm 检查是桌面相机位姿模拟，不是实际 XR 双眼测试。桌面帧耗时约 13.89 ms（受 72 FPS 目标限制），焦散 CPU 提交约 0.126 ms，不代表 GPU 耗时。新版头显观感及 Live Preview 包围盒错误是否消失尚需硬件复查。原始数据、固定时间诊断图和场景截图位于 `Logs/UnderwaterResearch/`；旧版参考保存在 `Logs/CausticReview/before/`。
