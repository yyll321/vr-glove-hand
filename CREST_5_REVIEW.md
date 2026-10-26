# Crest Water 5 调研及本项目改进

2026-10-21 查阅 Wave Harmonic 官方在线文档；Introduction 当时标注文档版本 **5.10.2**。以下指 Crest 5 系列，部分功能是 5.3、5.6、5.7 之后加入，不能理解为全部属于最初的 5.0。

| 官方优势 | 对本项目的用途 | 本轮落实 |
| --- | --- | --- |
| 分离的吸收、散射，可按空间/深度变化 | 避免水池和自然区只换一层蓝色滤镜 | 两区各自的每米 RGB 吸收/散射系数；自然区近底悬浮物变化；同一 Beer–Lambert 积分用于不透明接收面、颗粒与水下反射 |
| 环境反射与水下全反射 TIR | 抬头能看出水面存在，斜视看到周围场景 | 两区 256² 六面环境图，按固定世界包围盒校正反射方向；精确介质 Fresnel；移除把当前视野图像当水下反射的做法 |
| Flow 同时运输法线、泡沫与焦散等细节 | 流动线索应互相呼应 | 原随机三维流场继续驱动颗粒/水草；新增 64×32 两区表面流图，5 Hz 从相同流场采样，水面法线以双相位混合平流，避免无限拉伸和周期跳变 |
| 可控的波谱/局部波形覆盖 | 水池不应和自然区一样起伏 | 水池局部水面波强度 0.55，自然区 1；第二层细法线；原焦散 footprint=1 保留 |
| GPU 模拟、LOD、按功能关闭/降频 | 控制双眼成本 | 本轮复用现有焦散贴图和单次体积后处理；反射图离线保存，无运行时每眼反射相机；流图低分辨率、降频、复用内存 |
| Multi-Pass、Multi-View、Single Pass Instanced 支持 | 当前 PICO Live Preview 使用 Multi-Pass | 保留当前 XR 纹理、视图矩阵和双眼宏，不把桌面截图当作 HMD 修复证据 |

## 实现范围

本轮为基于公开功能说明的现有渲染器改进。**未购买、下载或导入 Crest 商业源码/插件**，没有声称完整复刻其 FFT、LOD、动态浅水模拟或 VR 性能。现有 Unity 2022.3.15f1 + URP 14 工程保留；官方当前最低条件为 Unity 2022.3 或更新的最新补丁、Shader Graph、shader target 4.5、非 OpenGL/WebGL。正式导入商业包前还应核对具体包版本及授权。

官方反射文档提醒：同时启用水上和水下平面反射代价很高。这里选用固定环境图，适合静态池体/海底参考场景。**它不实时反映机械臂动作、移动物体或后来修改的布景**，也不是逐像素准确的平面反射；修改布景后可重烘焙。默认六面各 256 像素，内存固定，不增加每帧六面绘制。环境资产已保存到 `Assets/Underwater/Materials/PoolWaterEnvironment.asset` 与 `SeaWaterEnvironment.asset`。

场景仍为 `Assets/Underwater/Scenes/UnderwaterResearchEditable.unity`。菜单 **Tools > Underwater > Research > 8 Bake Water Environment and Apply Optics** 会备份当前场景、重新采样两区反射并保存材质和光学参数；该菜单会应用推荐参数，手动调参后不应无意重复覆盖。改变颜色可在 `UnderwaterRenderer.asset` 的 Horizon Line Fog and Color 中分别调整两区吸收、散射、水色与沉积物强度。旧 `density` 字段为兼容旧资产保留，新版以每米散射系数控制雾。

此前用户反馈的 **世界随头转动、侧后方黑屏** 尚未通过佩戴验收。上一轮设备记录只有 5% 电量、串流约 1–6 FPS；本轮视觉升级不能自动解决 Live Preview 提交和编码瓶颈。官方 XR 支持也不能证明本机链路正常。

## 官方来源

- [产品与版本说明](https://docs.crest.waveharmonic.com/About/Introduction.html)
- [水下渲染、透明物体集成、URP 禁用 Opaque Downsampling 的建议](https://docs.crest.waveharmonic.com/Manual/Basics/Underwater.html)
- [水体吸收与散射](https://docs.crest.waveharmonic.com/Manual/Appearance/Color.html)
- [反射与 TIR、平面反射成本](https://docs.crest.waveharmonic.com/Manual/Appearance/Reflections.html)
- [流场](https://docs.crest.waveharmonic.com/Manual/Simulation/Flow.html)
- [波浪与局部覆盖](https://docs.crest.waveharmonic.com/Manual/Simulation/Waves.html)
- [性能指南](https://docs.crest.waveharmonic.com/Manual/Guides/PerformanceGuide.html)
- [平台及 XR 支持](https://docs.crest.waveharmonic.com/Manual/Guides/Platforms.html)
- [要求](https://docs.crest.waveharmonic.com/Manual/GettingStarted/Requirements.html)

原始文档摘录在 `Logs/CrestReview/`，改进前图片在其 `before/` 子目录。构建/实际渲染结果在 `Logs/UnderwaterResearch/`。

## 本轮验证

最终 Windows 构建成功，0 错误、18 警告；79 项软件回归通过。新增检查用真实白色接收面比较 2 米/12 米水中路径：水池红通道从约 0.871 降到 0.569，自然区 12 米红通道约 0.239；两区环境图均已绑定，启用后水面像素与原回退反射有明确差异。检查还保留相机改变时固定世界焦散位置、随机流场连续性、两区移动/切换、机械臂和隔离 UDP 手指驱动。人工查图后修正了环境图垂直方向并重烘焙、重构建、重测。

桌面耗时约 14 ms；这不代表 Live Preview 实际串流帧率或 PICO 独立运行帧率。对比图：`Logs/CrestReview/comparison.png`；水下仰视：`Logs/UnderwaterResearch/04-pool-under-surface.png` 与 `05-sea-under-surface.png`。场景、反射、吸收和散射仍为近似实时图形方案，后续可通过 Inspector 调整。
