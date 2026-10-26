# 水流、尾流泥沙与灯光声音联动

日期：2026-10-26。Unity 2022.3.15f1c1 / URP 14，电脑 Play + PICO Live Preview。

## 本次实现

| 改进 | 方法 | 观察效果 |
| --- | --- | --- |
| 水流一致性 | 世界坐标下的公共速度场：背景流、平滑阵风、解析旋度扰动、局部喷流相加。颗粒和水草采样局部流速；水面几何、法线和焦散共用水面流速图与双相位输运 | 不同位置的颗粒有局部旋转和速度差；水面和地面光斑的输运方向有共同来源，转头不拖动世界 |
| 尾流 | 两区各两个可编辑的局部喷流源，有限长度、高斯径向衰减、轴向衰减和轻微旋转；源位于车体后框外侧 | 推进强度增加时，附近已有悬浮颗粒被局部加速，近底喷流激起泥沙 |
| 扬沙 | 喷流撞击地面、手腕近底运动或首次接触触发浓度团；短时向上卷吸，然后随流漂移、扩散、沉降、衰减 | 自然沙底容易扬尘，瓷砖池底响应很弱。2–6 mm 沙粒与连续浓度雾结合，逐渐恢复清晰 |
| 颗粒、焦散、雾的统一 | 同一组浓度团用于雾消光和焦散光路衰减；球形紧支撑浓度的光路线积分解析求解 | 小云团不会因固定步长漏采样；泥沙遮住背景并削弱光斑，避免只增加一层大颗粒 |
| 工作灯 | 两区各一盏 ROV Spot Light，逐像素表面照明及阴影；有限世界坐标光锥散射，颗粒受相同光锥照亮 | 灯照在沙地上，经过泥沙时更容易看见散射；灯锥随 ROV 对象而非相机定位 |
| 声音 | 听者处的公共流速平滑调节环境声；同区推进强度平滑调节 ROV 试听增益和音高 | 视觉流动与声强变化有共同输入，关闭尾流后平滑回到待机声。原声传播路径、延迟和七频带物理报告不改 |

## 操作与编辑

- `N` / 右手柄 `B`：开关**尾流演示**。默认关闭；ROV 保持固定，不代表已有真实推进器遥测。
- `L` / 右手柄 `A`：开关工作灯。右手 A/B 使用已有 Input System 读取并兼容 XR feature fallback。
- `J` / 右扳机：原音频测试；`M` / 左扳机：静音；`H` 显示状态。
- 左摇杆、左 X 切区、右握把机械臂、右摇杆点击传送及 UdexReal 手套保持原操作。
- 场景：`Assets/Underwater/Scenes/UnderwaterResearchEditable.unity`。
- 每个 `ROVp - Pool / Seabed` 下的 `Water Interaction - editable sources` 中可移动两个 `Thruster Water Jet` 和 `ROV Work Light`。喷流局部 +Z 是排水方向，`radius/length/exitSpeed` 按米和米/秒解释，独立于导入模型缩放。
- 若接真实推进器数据：将对应 `UnderwaterWakeSource.demoLinked` 取消，写入 `throttle` 0–1；后续需要按真实推力和布局标定。
- `Research_Systems` 上的 `UnderwaterInteraction` 可调整沙地/池底可蚀性、沉降速度、演示强度；泥沙 ParticleSystem 是保存的可编辑对象。
- 第一次升级前备份：`Assets/Underwater/Backups/UnderwaterResearch-beforeFlow-20261026-191802.unity`。升级菜单：`Tools > Underwater > Research > 10 Apply Shared Flow, Sediment and Work Lights`。重新运行保留已手动移动的源。

## 参考与适配范围

1. [Crest Flow 官方文档](https://docs.crest.waveharmonic.com/Manual/Simulation/Flow.html)：文档明确 Flow 影响水波水平运动、法线、泡沫和焦散，并支持 RG 存储 XZ 流速。项目采用共同速度场和流速贴图输运，没有安装商业 Crest，也没有宣称复现其完整模拟器。
2. Bridson 等，2007，[Curl-noise for procedural fluid flow](https://doi.org/10.1145/1275808.1276435)：核对了 Crossref 文献信息。采用“矢势的旋度产生连续涡动”的思路，以解析多频函数近似；不是论文完整 noise 实现。垂向缩放、速度截断和边界衰减后不保证严格无散度。
3. Harris，[GPU Gems 第 38 章：Fast Fluid Dynamics Simulation on the GPU](https://developer.nvidia.com/gpugems/gpugems/part-vi-beyond-triangles/chapter-38-fast-fluid-dynamics-simulation-gpu)，以及 Stam，1999，[Stable Fluids](https://doi.org/10.1145/311535.311548)：参考共同速度输运浓度的组织方式。此处使用最多十二个浓度团和粒子阻力近似，没有网格压力投影、NS 求解或工程流体精度。
4. Mitchell，[GPU Gems 3 第 13 章：Volumetric Light Scattering as a Post-Process](https://developer.nvidia.com/gpugems/gpugems3/part-ii-light-and-shadows/chapter-13-volumetric-light-scattering-post-process)：参考参与介质的衰减与散射概念。原章屏幕径向采样未移植；为保留双眼与转头一致性，使用世界坐标灯锥，每灯仅在相交区间取三次散射样本。体积散射目前没有体积阴影，表面 Spot Light 有阴影。
5. 原有 Thompson、Chalmers、Rhee 2019 水下焦散论文对应的 Gerstner 法线与折射面积变化贴图继续使用，焦散大小 `causticFootprint=1` 保留。

公开资料快照和场景源位置审计保存在 `Logs/FlowInteractionReview`。没有把泥沙光学浑浊当作声学低通，也没有向水下电推进器自动加入空化气泡。

## 开销与验证

- 浓度团上限 12，只上传活跃项；沙粒随档位限 400 / 800 / 1200；悬浮粒子原预算保留。
- 两盏局部工作灯；射线不穿过灯范围时跳过灯散射；低档关闭工作灯阴影。公共流图 64×32、5 Hz；粒子局部速度 10 Hz，复用缓冲。
- 自动验证入口：Windows player 参数 `-underwater-research-check`，结果与对比截图保存到 `Logs/UnderwaterResearch`。
- 检查覆盖焦散在双眼分离及相机移动后的世界固定性、两区光学差异、N/L 实际输入、喷流范围/缩放/节流、近底扬沙、粒径与预算、浓度消散、真实渲染消光/灯散射、音源待机变化，以及原手套、接触和 IK。
- 数值与最终构建信息见同目录 `results.json`、`checks.txt` 和 `Logs/UnderwaterResearchBuild.txt`。桌面检查不等价于 PICO Live Preview 帧率；头显中仍需确认灯光强度、近底扬沙和转头稳定性。

本实现是有预算的视觉交互近似，不是完整 CFD、标定泥沙输运或多次散射。双相位重置避免贴图无限拉伸，但不是完整时间积分。工作灯体积散射使用有限采样，静态环境反射贴图不包含动态尾流与泥沙。

最终验证：126 项检查全部通过；Windows Development 构建成功，0 错误、1 警告。最终 RTX 4070 Laptop 桌面检查记录 17.28 ms/帧（约 58 FPS），超过 72 FPS 对应的 13.89 ms 预算，不能声称满足头显目标。焦散 CPU 提交约 0.057 ms。真实渲染白背景红通道经泥沙由 0.439 降至 0.251，开启灯散射后为 0.412；相机移动和转动的焦散世界点误差均值约 0.0093。PICO Live Preview 佩戴验收尚待用户完成。
