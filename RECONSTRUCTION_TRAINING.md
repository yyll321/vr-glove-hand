# 水下重建场景与训练流程

## 已交付内容与边界

- 场景：`Assets/Reconstruction/Scenes/UnderwaterReconstruction.unity`。菜单 `Tools > Reconstruction > 1 Create or open reconstruction scene` 打开。
- `1` 切换 3DGS；`2` 切换烘焙 NeRF；WASD 移动，Q/E 升降，右键转头，R 复位。PICO 使用现有 XR loader，左摇杆移动、右摇杆转向。
- 默认展示沙地和环形物体的**合成演示**。这用于验证渲染管线，不是从照片训练得到的模型。
- 3DGS 实现：训练 PLY 导入、log scale / quaternion 转协方差、sigmoid opacity、最多三阶 SH、屏幕空间各向异性 Gaussian、GPU 深度排序、预乘 alpha 混合。支持 Graphdeco 标准和 Nerfstudio `gaussian-splat` 导出格式；普通点云 PLY 会被拒绝。
- NeRF 实现：训练后的 Nerfacto 密度 / 辐射场离线采样为 RGBAHalf Texture3D，Unity 按双眼分别做体积分、透射率累计、提前终止和不透明物体深度截断。**没有在 Unity 内执行 NeRF 神经网络推理**，RGB 是六方向平均，因此会损失镜面、视角相关效果及部分高频细节。
- 复用了项目的头部跟踪组件；新场景在运行期间使用独立 URP 配置，不带原场景的全屏水下雾、焦散、粒子、阴影或后处理。避免训练视频已经包含水下颜色衰减时再次叠加。
- 2026-10-26 已接入 `aras-p/UnityGaussianSplatting` 官方 **v1.0.0**，源代码保存在 `Packages/org.nesnausk.gaussian-splatting`，并为当前 URP14 配置官方 Renderer Feature。官方路径使用 GPU radix sort 和压缩资产，Windows 要求 DX12/Vulkan。原项目内的 bitonic 渲染器保留为兼容路径。最新操作、素材和训练参数以 [下一步训练教程](RECONSTRUCTION_NEXT_STEPS.md) 为准。
- 没有原始视频、标定或真实训练检查点，因此尚未训练真实水下场景。NeRF 的 Nerfstudio 导出器必须在后续训练环境中实测，本机没有 Nerfstudio。
- 75 FPS 预算为 13.33 ms，90 FPS 为 11.11 ms。菜单验证和桌面离屏截图都不构成头显帧率或双眼观感验收。

## 优先路线

最快走通：**视频 → COLMAP 相机求解 → Nerfstudio Splatfacto → PLY → Unity 3DGS**。

先做 PC Windows / PICO 串流，小资产起步。桌面 GPU 的性能不能代表 PICO Android 的独立运行性能。原项目串流链路历史上曾有低帧率，新的重建渲染器不能自动修复编码、传输或姿态时序问题。

NeRF 适合做画质对照：共用同一数据集训练 Nerfacto，烘焙小裁剪区域后比较。SeaThru-NeRF、AquaSplatting、WaterSplatting、AtlantisGS 都不是已安装功能：应在标准流程成功后，再按论文代码的真实导出格式和许可单独评估。水下专用模型的分离水体参数不会自动进入标准 PLY 或本 NRV 格式。

## 1. 采集与数据准备

1. 从一个静态、小范围区域开始。缓慢侧移并环绕拍摄，有视差，避免只站在原地转头；让不同高度、方向覆盖同一表面，相邻视图约 70–80% 重叠。
2. 固定曝光、白平衡、焦距和分辨率，尽量避免运动模糊、镜面反光、漂动水草、悬浮颗粒遮挡及大面积无纹理水体。先清理镜头，保留未经多次转码的原始视频。
3. 标准训练首轮使用 200–300 张清晰图像、约 1080p 以内长边起步。不要把非常相似的连续几千帧全部投入训练。
4. 水下壳体折射会破坏普通针孔相机假设；尽量在相同水体、镜头和壳体条件下标定。COLMAP 注册失败时先修数据和标定，不要盲目增加训练步数。
5. 水下颜色预处理如需进行，必须对整段使用一致参数，并保留原片。不要逐帧自动增强导致闪烁。动态物体 / 颗粒可先遮罩或剔除对应帧。
6. 放置已知长度标尺或测量多个控制点。重建通常只有相对尺度；导入后用已知距离恢复米制，并核对手柄、碰撞体和交互尺寸。

## 2. 建立独立训练环境

推荐 Linux 或 WSL2 + NVIDIA CUDA，训练文件放在 Linux 文件系统中以减少 I/O 开销。不要在 Unity 的 Python / 包目录里安装训练依赖。

下面是环境搭建骨架；PyTorch 的 wheel 必须按照训练时 NVIDIA 驱动、CUDA toolkit 和 Nerfstudio 版本的官方兼容表选择，不能把本机 Unity 支持 DirectX 视为 CUDA 训练已配置：

```bash
conda create -n underwater-recon python=3.10 -y
conda activate underwater-recon
# 安装匹配 CUDA 的 PyTorch + torchvision，然后安装 Nerfstudio。
pip install nerfstudio
# 另外安装 ffmpeg、COLMAP；Splatfacto 的 gsplat 需要匹配 CUDA 的编译工具链。
python -c "import torch; print(torch.__version__, torch.cuda.is_available()); print(torch.cuda.get_device_name(0))"
ffmpeg -version
colmap -h
ns-train splatfacto --help
ns-train nerfacto --help
ns-export gaussian-splat --help
pip freeze > environment-lock.txt
```

如果某个 CLI 选项随版本变化，以安装版本的 `--help` 为准，连同 `environment-lock.txt` 和训练生成的 `config.yml` 一起保留。

参考文档：

- [Nerfstudio 安装](https://docs.nerf.studio/quickstart/installation.html)
- [自有数据](https://docs.nerf.studio/quickstart/custom_dataset.html)
- [Splatfacto](https://docs.nerf.studio/nerfology/methods/splat.html)
- [UnityGaussianSplatting 项目](https://github.com/aras-p/UnityGaussianSplatting)

## 3. 用同一个数据集训练 3DGS 和 NeRF

在训练环境中执行，下面使用示例路径，换成你的实际路径。`train.py` 只是命令封装，不会隐藏安装环境或下载训练数据。

```bash
python Tools/Reconstruction/train.py prepare \
  --input /data/pool.mp4 --data /data/pool --frames 250
```

检查 `/data/pool/transforms.json`、COLMAP 注册数量、相机轨迹和稀疏点云。若明显错位或相机只注册很少，先重做采集或 COLMAP。保留少量未用于训练的图像做新视角验证。

先用 7,000 步快速检查 3DGS，再按验证结果决定是否训练到 30,000 步。训练耗时取决于显卡、图片尺寸、数量与 Gaussian 数量，本文不承诺固定分钟数。

```bash
python Tools/Reconstruction/train.py train \
  --method splatfacto --data /data/pool --output /data/outputs --steps 7000

# config.yml 使用上一步实际输出的路径，不要照抄占位时间戳。
python Tools/Reconstruction/train.py export-gs \
  --config /data/outputs/pool/splatfacto/实际时间/config.yml \
  --output /data/export/pool-gs
```

如需 NeRF 对照：

```bash
python Tools/Reconstruction/train.py train \
  --method nerfacto --data /data/pool --output /data/outputs --steps 15000

python Tools/Reconstruction/bake_nerf.py \
  --config /data/outputs/pool/nerfacto/实际时间/config.yml \
  --output /data/export/pool.nrv --resolution 128 \
  --bounds -1 -1 -1 1 1 1
```

`--bounds` 是**Nerfstudio 训练坐标的 Z-up 包围盒**，顺序为 xmin ymin zmin xmax ymax zmax，必须按模型实际区域调整。`[-1,1]` 只是示例，错误裁剪会使导出变空或漏掉场景。导出器自动换成 Unity `(x,z,y)`，不要再次旋转纹理。

128³ RGBAHalf 原始纹理约 16 MiB，256³ 约 128 MiB，另有运行显存开销。先用 64³ 检查方向和范围，再提高到 128³；大场景应分块烘焙，当前实现一次显示一个体积，不包含流式分块系统。高反光目标不能指望六方向平均烘焙保留原 NeRF 效果。

## 4. 导入 Unity

1. 打开新场景，菜单 `Tools > Reconstruction > 2 Import trained data`。
2. 3DGS 选择导出的 `.ply`，初次设 **50,000–100,000** Gaussian，SH 可先关闭。保存生成的 `.asset` 后会自动赋给打开场景的 3DGS 组件；保存场景。
3. NeRF 选择导出的 `.nrv`，保存 `.asset`，会自动赋给 NeRF 组件；按 `2` 对照。体积的父物体仅使用**等比缩放**。
4. PLY 导入中的预算是均匀索引抽样，可能丢细节。质量不足时，优先在训练 / 导出阶段做有质量依据的裁剪、剪枝或降预算，再逐步增加导入数量。不要把本抽样器称为 LOD 或最优压缩。
5. 坐标自动从源 Z-up 交换成 Unity Y-up。用根节点调整朝向 / 平移 / 统一尺度，先检查模型正反、标尺长度、初始观察点与近裁剪面。
6. 3DGS 与 NeRF 本身不提供碰撞。需要抓取、ROV 碰撞或声学反射时，另建简化 MeshCollider / 基础碰撞体；不要把每个 Gaussian 变成 GameObject 或 Collider。
7. 新场景已追加到 Build Settings，默认不启用，避免替换原启动场景。构建时明确只选此场景或调整场景顺序。

## 5. 性能与验收

- 在实际 Windows Player 和 PICO 串流头显分别测量，记录模型数量、分辨率、SH、排序频率、CPU/GPU 时间和传输帧率。编辑器小窗口 FPS 不能代表头显。
- 先关 NeRF，只加载一份 3DGS。排序静止时复用，运动时默认每帧更新；`Sort Every Frames` 可调为 2，但快速转头可能出现透明层次跳变。
- 第一轮先用 50k–100k，SH 0–1；如果 GPU 在 13.33ms 以内并且串流稳定，再增加数量与 SH。NeRF 先用 64³、64–96 步验证，再逐步提高。
- 大量重叠 / 特别大的 splat 会产生严重填充开销；应裁剪远处、漂浮及不可信 Gaussian。当前全局中心眼深度排序在复杂互相穿插表面与大 IPD 近景可能出现混合误差；它不等同于每眼精确逐像素排序。
- 不把 Inspector 中 90 FPS 目标当实测，不自动切换头显刷新率。根据设备实际支持的 72/90Hz 等模式选择目标；满足 75 FPS 通常应选择 90Hz 并验证。
- PICO Android 独立机需单独做 Vulkan、compute、纹理、内存与热稳定性测试。此次主要验证目标是当前 Windows 图形路径。
- 水下质量检查：用留出视图看颜色、漂浮点、薄结构、边缘重影；若需要定量几何，使用已知标尺 / 控制点比较误差，PSNR/SSIM 不能代替米制几何验证。
- 训练模型与视频静态背景只在拍摄覆盖区域可信。超出覆盖区域的新视角不能保证真实。

编译 / 场景 / 导入 / GPU 检查入口：

```powershell
& 'F:/UserData/softwaredownload/unity/2022.3.15f1/Editor/Unity.exe' `
  -batchmode -quit -projectPath 'F:/unity/f1/My project' `
  -executeMethod UnderwaterReconstruction.Editor.ReconstructionSceneBuilder.CreateAndValidate `
  -logFile 'F:/unity/f1/My project/Logs/Reconstruction/editor.log'
```

结果在 `Logs/Reconstruction/`。该检查使用本机 GPU，验证训练格式解析、无效文件拒绝、GPU 排序和两条渲染路径的非空输出；不启动真实训练，也不替代头显验收。

本次验证：Unity 2022.3.15f1c1 批处理编译与场景重开成功，退出码 0；RTX 4070 Laptop / Direct3D11 下 **14 项检查通过**，两条路径均生成非空截图（`3dgs.png`、`nerf.png`）。Python 脚本语法和 prepare / train / export 命令预览通过。Nerfstudio 未安装，尚未验证真实检查点的训练与烘焙；PICO 实际双眼观感、75/90 FPS 和 Android 构建未验收。
