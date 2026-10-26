# 3DGS 接入、素材与下一步训练（2026-10-26）

## 当前项目

打开 `Assets/Reconstruction/Scenes/UnderwaterReconstruction.unity`，或菜单 `Tools > Reconstruction > 0 Open reconstruction workbench`。

点击 **Play** 使用该场景的独立轻量管线。编辑模式下项目仍保留原水下场景的全局管线，因此 Scene 视图中的预览不代表运行时效果。

- 已安装官方 [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting)，固定 **v1.0.0 / 0a30eb3139ed4b0f86ad5402fb452469da17b247**。代码与 MIT 许可完整保留在 `Packages/org.nesnausk.gaussian-splatting/`，改动在本项目 `Assets/Reconstruction/` 中。
- 固定旧版的理由：实际读取的 v1.1.1/v1.1.2 的 URP 功能要求 Unity 6；当前项目是 Unity 2022.3.15f1c1 / URP14，v1.0.0 有对应的 URP 实现。没有升级整个 Unity 项目。
- 官方渲染器支持压缩资产和 GPU radix 排序；已在独立 URP Renderer 中加好官方 Feature，保留原 NeRF 和兼容 3DGS 路径。
- Windows 官方路径要求 **DX12 或 Vulkan**，不支持 DX11。Windows Player 配置提供 DX12 优先、DX11 兼容的菜单；编辑器需用 `-force-d3d12` 重启。DX11 下不会强行启动官方组件，而是显示兼容路径及 API 提示。
- 按 `1` 显示 3DGS；`2` 显示烘焙 NeRF；`3` 切换官方/兼容 3DGS 偏好。WASD/QE 移动，右键观察，R 回到初始观察位。左手柄摇杆移动，右手柄摇杆转向。
- 场景默认含 **16,400 个合成 Gaussian**，用于确认接入。官方资产与兼容资产分别加载，不会把合成内容称为水下实拍重建。NeRF 仍是烘焙密度体，不是实时网络推理。
- 模型初次导入后，在工作台按“相机对准官方模型”；再以已知尺度校准根节点。默认源 Z-up → Unity `(x,z,y)`，已通过父节点变换处理。其他来源模型如采用不同坐标系，需人工校准。
- 已修正原机械臂编辑器脚本在重建场景中的自动放置行为，防止它新增 depth=10 的 `MainCamera` 覆盖 XR 相机；场景里已有的机械臂对象保留，已有预览相机停用。

## 已找到的训练素材

| 素材 | 入口与获取方式 | 用途 / 注意事项 |
|---|---|---|
| **本项目合成 sand/torus** | 本地 `TrainingData/synthetic_sand_torus/`；菜单 `Tools > Reconstruction > 7 Export 48-view synthetic training dataset` 可生成 | **现在即可开始试训**：48 张 640×480 PNG，42 训练 + 6 留出，含 `transforms.json` 内外参及 `points3D.ply`。无需再跑 COLMAP。素材由合成 Gaussian 场生成，只用于流程验证，不是实拍水下数据或独立画质基准。 |
| **SeaThru-NeRF 原作者水下数据** | [项目页](https://sea-thru-nerf.github.io/) · [原作者仓库与格式说明](https://github.com/deborahLevy130/seathru_NeRF#dataset---photos-credit---matan-yuval) · [作者公开下载入口](https://drive.google.com/uc?export=download&id=1RzojBFvBWjUUhuJb95xJPSNP3nJwZWaT) | **真实水下首选**。作者说明包括 `images_wb/`、`sparse/0/`、`poses_bounds.npy`。相机数据可避免重复 COLMAP；解压后先核对实际文件。照片署名 Matan Yuval。数据采用作者页面的使用条款。 |
| **Nerfstudio poster** | [官方数据下载代码](https://github.com/nerfstudio-project/nerfstudio/blob/main/nerfstudio/scripts/downloads/download_data.py) · [该代码列出的 poster 下载入口](https://drive.google.com/file/d/1FceQ5DX7bbTbHeL26t0x6ku56cwsRs6t/view) | 小场景通用流程对照，**不是水下素材**。安装 Nerfstudio 后可用 `ns-download-data nerfstudio --capture-name poster --save-dir TrainingData` 获取。 |

联网核实情况：已下载并读取原作者 SeaThru-NeRF README、Nerfstudio 数据下载源码及官方 Unity 仓库。SeaThru Drive 入口会重定向到 Google 下载页，本次**没有下载完整水下压缩包或核实全部图像**；Google Drive 可能要求确认大文件下载或遇到配额。没有把 HTML 确认页当成已下载的数据集，也没有默认下载官方 14GB 预训练模型包。

不建议用 UIEB/EUVP 等单帧水下增强数据集直接替代多视图训练集：重建需要同一静态场景的连续重叠视图和可靠位姿，随机收集的海底图片通常不满足条件。

## 先修训练环境

本机已读取到：**RTX 4070 Laptop / 8GB 显存 / NVIDIA 驱动 572.42**，但现有 Python 是 **PyTorch 2.13.0 CPU 版**，`torch.cuda.is_available()` 为 False；当前 PATH 下没有 `ns-train`、`ns-export`、`nvcc`、COLMAP 或 ffmpeg。

因此目前 Unity 可以渲染，Python 训练环境尚未就绪。此次没有改动原有 Conda 环境，以免影响手套 / ROV 的现有 Python 依赖。

推荐使用 **WSL2/Linux 的独立 Conda 环境**，按 [Nerfstudio 官方安装说明](https://docs.nerf.studio/quickstart/installation.html) 安装匹配的 CUDA PyTorch、CUDA toolkit 编译器及 Nerfstudio / gsplat。**不要只安装 CUDA runtime**：gsplat 首次运行可能需要 `nvcc` 和 C++ 编译器。也不要直接在当前 CPU 环境中重复 `pip install torch`。

环境骨架如下；PyTorch/CUDA 的具体版本组合按该环境的官方安装说明选择，安装完成再锁定版本。本项目没有声称这套新环境已经安装或经过真实训练验证：

```bash
conda create -n underwater-gs python=3.10 -y
conda activate underwater-gs
# 按 Nerfstudio 官方安装说明安装 CUDA 版 PyTorch、toolkit、编译器。
pip install nerfstudio

python -c "import torch; print(torch.__version__, torch.version.cuda); assert torch.cuda.is_available(); print(torch.cuda.get_device_name(0))"
nvcc --version
ns-train splatfacto --help
ns-export gaussian-splat --help
python Tools/Reconstruction/train.py preflight
pip freeze > underwater-gs-environment-lock.txt
```

从 Windows 启动 WSL 时，需要把 `Tools/Reconstruction/` 与训练素材复制到 Linux 路径，例如 `~/underwater-gs/`；训练输出也放在那里以减少 `/mnt/f` 的大量小文件 I/O。只在训练完成后把 PLY 拷回 Unity 项目。预检查只读取环境，不会安装或更改软件。

**优先在 Unity 停止 Play、关闭额外 GPU 程序后训练**，8GB 显存也要容纳桌面和驱动。先用小图、SH1、7k 步，显存不足先降图片尺寸 / 数据量，再检查 densification 数量，不要一上来用大场景或 SeaThru 完整大模型。

## 第一步：先用已生成素材试训

在已激活的 CUDA 环境、含 `Tools/` 的工作目录执行：

```bash
python Tools/Reconstruction/train.py train \
  --method splatfacto \
  --data TrainingData/synthetic_sand_torus \
  --output TrainingData/outputs \
  --steps 7000 --downscale-factor 1 --sh-degree 1 --stop-split-at 5000
```

该封装实际调用 `ns-train splatfacto`，用 640×480 原图试训、最大 SH1，并在 5,000 步后停止新增 Gaussian。它不会把训练产生的 splat 数量硬性限制为 100k；训练密度和 Unity 导入预算是两件不同的事。第一次 CUDA kernel 编译可能比后续启动慢。

`train.py ... --dry-run` 可以先看实际 CLI；训练时不需要 Unity 保持运行，也不需要 COLMAP。检查训练和留出视角，确认方向、颜色、地面和环形物体合理，再继续真实水下数据。

## 第二步：换真实水下数据，复用已有相机

下载并解压 SeaThru 原作者数据，**选择其中一个场景**，不要把所有场景的图片混在一起。核对目录：

```text
TrainingData/seathru/SCENE/
  images_wb/            相互重叠的同场景照片
  colmap/sparse/0/      cameras.bin、images.bin、points3D.bin（或对应 .txt）
  poses_bounds.npy     原始 LLFF 数据；Splatfacto 的 colmap parser 不直接用它
```

如果解压后是根目录 `sparse/0`，保留原包，另建上述工作副本或将稀疏重建复制到 `colmap/sparse/0`。不要重新跑 SfM 覆盖作者的位姿。`images_wb` 中的文件名必须与 COLMAP 中记录匹配，不能随意重命名。

```bash
python Tools/Reconstruction/train.py train \
  --method splatfacto --parser colmap \
  --data TrainingData/seathru/SCENE --images-path images_wb \
  --output TrainingData/outputs \
  --steps 7000 --downscale-factor 2 --sh-degree 1 --stop-split-at 5000
```

先看水下实拍基线，再决定是否训练到 15k–30k 步。训练时长与场景、分辨率、显卡功耗和 splat 数量有关，不保证固定分钟数。普通 Splatfacto 仍会把拍摄时水体散射 / 颜色衰减包含在模型里；它不自动变成 AquaSplatting 或恢复真实无水颜色。

若以后用自己的水下视频：先固定曝光 / 白平衡，缓慢环绕、增加视差，提取 200–300 张清晰帧，再运行：

```bash
python Tools/Reconstruction/train.py prepare \
  --input /data/pool.mp4 --data /data/pool --frames 250
```

这个分支需要 ffmpeg 与 COLMAP。相机注册数量、轨迹和稀疏点云正常后，才开始训练。水下平面舷窗折射可能影响普通相机模型，需关注水下标定与几何误差。

## 第三步：导出并进入 Unity 官方渲染器

```bash
# config.yml 换成这次训练真实生成的路径。
python Tools/Reconstruction/train.py export-gs \
  --config TrainingData/outputs/实际场景/splatfacto/实际时间/config.yml \
  --output TrainingData/export/raw

# 实际输出 PLY 文件名以 ns-export 结果为准；通常是 splat.ply。
python Tools/Reconstruction/prepare_ply.py \
  --input TrainingData/export/raw/splat.ply \
  --output TrainingData/export/unity/pool_unity.ply \
  --max-splats 250000 --min-opacity 0.02
```

`prepare_ply.py` 会检查是训练 Gaussian，补法线 / 缺少的 SH 系数、恢复官方 v1.0 固定的 **62 个 float32 字段顺序**、归一化 quaternion，并筛掉低 opacity / 非有限值。支持 SH0/1/2/3；较低阶按每个颜色通道正确补零，不是把整个数组直接追加零。它保留源坐标，不会再次换轴。已有输出文件会被拒绝以保留旧结果。

250k 是初始**导入预算**，不是 75 FPS 的保证。超预算按索引均匀抽样，可能损失细节；高质量场景应优先使用有质量依据的训练剪枝、裁剪或后处理。DX11 兼容路径建议 50k–100k。

回到 Unity：

1. 确认已使用 DX12，打开工作台与重建场景。
2. `Tools > Gaussian Splats > Create GaussianSplatAsset`，输入 `pool_unity.ply`，输出选 `Assets/Reconstruction/Data/Trained/`。首轮使用 **Medium**；先不要选择带 SH 聚类的 VeryLow，以免增加导入计算量。
3. 在 Project 选中新生成的官方 `.asset`，执行 `Tools > Reconstruction > 5 Assign selected official asset`，或工作台同名按钮。
4. 点击工作台“相机对准官方模型”，校准比例、朝向和初始位置，保存场景。按 `1` 查看。
5. 若想按 `3` 对照兼容路径，需要用旧的 `Tools > Reconstruction > 2 Import trained data` 把**同一份标准 PLY**再导入兼容资产；仅给官方组件赋值，不会自动更新兼容资产。

不要删除官方 `.asset` 旁边的 `*_chk/pos/oth/col/shs.bytes` 文件，它们是压缩资产的一部分。Gaussian 不自带 Collider；碰撞 / 抓取需要独立的简化碰撞体。

## 水下 NeRF 的后续研究路线

[SeaThru-NeRF 原始实现](https://github.com/deborahLevy130/seathru_NeRF) 是 JAX / MultiNeRF 路线，前向拍摄场景为主要目标。为了训练效率，可另外评估 [AkerBP 的 Nerfstudio 移植](https://github.com/AkerBP/seathru_nerf)，注意它是**非原作者的扩展实现**。

该移植 README 列出：完整版约 23GB、lite 约 7GB 显存。本机 8GB 应从 lite 或更小 batch 起步，而且 Unity 同时占显存时可能仍不够。按照其 README 单独安装插件后，例如：

```bash
ns-train seathru-nerf-lite --vis tensorboard \
  colmap --data TrainingData/seathru/SCENE --images-path images_wb
```

`seathru-nerf-lite` 的水体 / 物体分离参数 **不能直接导入当前 Gaussian 渲染器**。本项目现有 `bake_nerf.py` 面向普通 Nerfacto field，没有验证该水下插件的专用 field，因此不能直接宣称支持 SeaThru 烘焙。先完成 Splatfacto PLY 流程，再为水下模型单独开发和验证导出器。

## 验收顺序

1. 环境预检查：CUDA、`nvcc`、`ns-train` 都正常。
2. 合成数据完成一次 7k 试训，检查 6 张留出视图。
3. 导出 → PLY 标准化 → 官方资产创建 → 新场景成功显示。
4. 一个真实水下场景完成同样流程，检查漂浮点、近景、视角外空洞、颜色和已知标尺。
5. Windows Player 与实际 PICO 串流分别记录 CPU/GPU 帧时间和实际收到的帧率。75 FPS 预算 13.33ms；90 FPS 预算 11.11ms。官方 README 的桌面测试成绩不是本机双眼 VR 的验收数据。

运行验证与素材位置：

- `Logs/Reconstruction/official-integration.txt`：官方版本、资源与 API 状态。
- `Logs/Reconstruction/official-checks.txt`、`official-3dgs.png`：官方路径实际验证结果 / 截图。
- `Logs/Reconstruction/checks.txt`：原兼容渲染器 / NeRF 检查。
- `Logs/Reconstruction/training-environment.json`：当前训练环境读取结果。
- `python Tools/Reconstruction/test_pipeline.py`：标准 PLY 转换、48 张图像、相机坐标和留出拆分的检查。

当前没有执行 CUDA 训练，没有拿合成画面当水下实拍结果，也没有宣称头显 75/90 FPS 已达标。

本次实际验证：官方 v1.0.0 的 16,400-splat 合成资产已在 **RTX 4070 Laptop / Direct3D12** 上成功渲染，已查看 `official-3dgs.png`；兼容渲染器与 NeRF 的 **14 项 Unity 检查通过**。Python 的 **5 项检查通过**，涵盖低阶 SH 补齐、字段顺序、坏输入拒绝以及全部 48 张训练图像、相机旋转 / 光轴和 42/6 拆分。相机单独出图和文件格式通过，不等于 CUDA 训练、真实水下质量或实际头显验收已经完成。
