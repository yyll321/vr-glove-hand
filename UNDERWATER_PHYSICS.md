# 水动力与三维地形扩展

新场景：`Assets/Underwater/Scenes/UnderwaterHydrodynamicsEditable.unity`。
原 `UnderwaterResearchEditable.unity` 保留，完整 ROVp、WA100 与 XR 装配组件不重建。

本轮加入：

- 两区各一个 16 × 8 × 32 的体积速度与辅助示踪网格，半拉格朗日输运、黏性平滑、有限次压力投影与静态固体边界；仅当前区域以 4/7/10 Hz 更新。
- 两台 ROV 的相对流速、二次阻力、浸没浮力、线性角阻尼和动压估算。默认不施力，参数未标定。
- 自然区起伏沙床、八件可编辑的管架、通道与巡检障碍，以及匹配的地形和障碍碰撞体。
- 共享流场连接颗粒、海草、水面流图、泥沙与环境声；近底扬沙向网格注入有限冲量。网格示踪量不替代已有光学泥沙浓度团。

打开菜单：`Tools > Underwater > Research > 12 Open Hydrodynamics Scene`。
菜单 11 重新应用升级前会备份 Scene，但会重设部分参数，已有手调版本请先另存副本。
菜单 13 构建独立 Windows Development Player。移动静态障碍或地形后，通过流体组件菜单 `Reset Flow And Rebuild Static Boundaries` 重建边界。

参考 Stam Stable Fluids、GPU Gems 38、Fossen、UUV Simulator 与 Stonefish 的方法或架构。未安装 Crest 商业插件，未移植 ROS/Gazebo 或 Stonefish 源码；粗网格不解析细管边界层或真实涡街，也不是经过验证的完整 CFD 或六自由度水动力模型。

验证：Runtime、Editor 程序集使用 Unity Roslyn 编译通过，5 条既有警告；375 项序列化、引用和网格检查通过。这不是 375 项运行实验。本轮 Unity 被 Sentinel LDK 许可证弹窗阻挡，尚未验证新增场景的导入、物理执行、构建或 PICO 帧率。历史桌面 15.364 ms/帧不能作为新增扩展的性能结果。

完整组会材料：

- [Word 报告](Documentation/水下临场感系统与水动力扩展组会汇报.docx)
- [Markdown 报告](Documentation/水下临场感系统与水动力扩展组会汇报.md)
- [区分历史实测与本轮静态检查的快照](Documentation/validation-snapshot.json)

报告包含 14 页 PPT 分镜与讲稿，非 PPTX 文件。报告资料截止 2026 年 10 月 26 日，整理与推送于 10 月 27 日完成。
