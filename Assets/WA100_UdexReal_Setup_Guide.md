# WA100 UdexReal Unity Setup Guide

> 2026-10-28 水下抓取场景已改用 HandDriver 一致角度配置，取消第一包校零和旧混合放大，并增加仿真拇指根部弯曲轴。具体通道、重连和沙层配置见 `Documentation/HandDriver一致映射与沙层指痕.md`。下文旧映射表适用于未升级的 Sample/原始预制体，不作为当前水下场景的配置依据。

## 1. HandDriver.exe 到 Unity 的数据链路

1. 启动 `handdriver.exe`，确认手套能在 HandDriver 窗口里正常映射动作。
2. 在 `handdriver.exe` 的输出设置里把 Unity/UDP 目标设为本机：
   - IP: `127.0.0.1`
   - Port: `5555`
3. 打开 Unity 场景 `Assets/HandDriver/Scenes/Sample.unity`。
4. 场景里必须有名为 `Network` 的对象，并挂载 `Network` 脚本。
5. `Network.Port` 保持 `5555`。运行后 Inspector 里这些 Debug 字段应变化：
   - `ReceivedPacketCount` 持续增加
   - `LastEndpoint` 显示 HandDriver 来源
   - `LastRoleName` 显示手套角色名，例如 `UDXST3674R`

Unity 接收的是 HandDriver 发送的 UDP JSON 参数流。`Network.Convert2Angle(characterName, key)` 从最近的数据包中读取某个手指通道角度，例如右手拇指 `r0/r1/r2/r3`。

'interactions hands-righthand-handdriver.cs'- `CharacterName` 写`UDXST3674R`，如果你只想绑定某一只手套，可以填写 HandDriver 里显示的角色名。

## 2. WA100 URDF 导入

1. 把 URDF 放在：
   - `Assets/WA100_R_URDF.urdf`
2. 把 mesh 文件放在 URDF 引用的路径：
   - `Assets/meshes/*.STL`
3. 确认项目安装了 Unity Robotics URDF Importer。
4. 在 Unity 菜单执行：
   - `Tools/WA100/Import Official URDF`
5. 导入后会生成：
   - `Assets/Robots/WA100_R_URDF_Official.prefab`
6. 再执行：
   - `Tools/WA100/Place In Scene With Front Camera`

当前编辑器脚本 `Assets/Editor/Wa100OfficialUrdfImport.cs` 会自动处理：
   - 使用官方 URDF Importer 导入 WA100
   - 给模型挂上 `Wa100UdexRealArticulationMapper`
   - 给模型挂上 `Wa100VisibilityGuard`
   - 放到 `Interactions Hands` 的右侧，形成左手、右手、WA100 并排显示
   - 创建或更新 `WA100 Front Camera`

## 3. WA100 运行时映射脚本

WA100 模型根对象需要挂：

1. `Wa100UdexRealArticulationMapper`
   - 读取 `Network.Convert2Angle()`
   - 自动绑定 WA100 的 `ArticulationBody`
   - 用 `xDrive.target` 驱动关节
   - 第一包手套数据自动校准为中立手势

2. `Wa100VisibilityGuard`
   - 禁用重力
   - 固定根 ArticulationBody
   - 打开所有 Renderer
   - 禁用 Collider
   - 禁用 URDF Importer 自带键盘控制，避免和手套驱动冲突

右手 UdexReal 到 WA100 的当前映射：

| WA100 joint | UdexReal key | 作用 |
| --- | --- | --- |
| `Thumb_J1` | `r3` | 拇指基部偏转/外展内收 |
| `Thumb_J2` | `r2 + r1` | 拇指主弯曲，默认增强到 `1.15 * r2 + 0.5 * r1` |
| `Thumb_J3` | `r0` | 拇指末端弯曲，默认增强到 `1.6 * r0` |
| `Index_Finger_J1` | `r6` | 食指根部弯曲 |
| `Index_Finger_J2` | `r5 + r4` | 食指中/末端联动 |
| `Middle_Finger_J1` | `r10` | 中指根部弯曲 |
| `Middle_Finger_J2` | `r9 + r8` | 中指中/末端联动 |
| `Ring_Finger_J1` | `r14` | 无名指根部弯曲 |
| `Ring_Finger_J2` | `r13 + r12` | 无名指中/末端联动 |
| `Little_Finger_J1` | `r18` | 小指根部弯曲 |
| `Little_Finger_J2` | `r17 + r16` | 小指中/末端联动 |

## 4. 运行顺序

1. 启动 `handdriver.exe`。
2. 确认 HandDriver 里手套动作正常。
3. 打开 Unity 的 `Sample.unity`。
4. 确认 `Network.Port = 5555`。
5. 确认 WA100 根对象上有：
   - `Wa100UdexRealArticulationMapper`
   - `Wa100VisibilityGuard`
6. 点击 Play。
7. 保持手套在自然张开姿态 1 秒左右，让 mapper 用第一包数据校准中立位。
8. 查看 `Wa100UdexRealArticulationMapper.LastStatus`：
   - `Bound 11/11 WA100 joints`
   - `Applied 11/11 WA100 joints`

## 5. TCP 输出手部 21 关节

场景里的 `Network` 对象还可以挂 `HandJointTcpStreamer`。

默认配置：
   - ListenAddress: `127.0.0.1`
   - ListenPort: `5566`
   - SendRateHz: `30`

它会把 Unity 中 Interactions Hands 的 21 个标准关节点以 JSON 行协议输出到 TCP 客户端。每帧包含：
   - `hand`
   - `source`
   - `joints[].name`
   - `position`
   - `rotation`
   - `localPosition`
   - `localRotation`

这个 TCP 输出适合给 Python、ROS、机械臂控制程序或其他 Unity 外部程序读取。WA100 在 Unity 内部驱动时不需要 TCP，直接使用 `Network.Convert2Angle()` 更低延迟。

## 6. 导入自己的灵巧手模型

推荐流程：

1. 优先用 URDF 表达模型关节结构，每个可动关节使用 `revolute`，并写清楚：
   - `joint name`
   - `parent`
   - `child`
   - `axis`
   - `limit lower/upper`
2. 用 Unity Robotics URDF Importer 导入。
3. 确认导入后每个可动 link 上有 `ArticulationBody`。
4. 复制或改写 `Wa100UdexRealArticulationMapper.cs`：
   - 把 `JointName`
   - `LinkName`
   - UdexReal key
   - 正负方向
   - 关节限位
   改成你的模型实际结构。
5. 对弯曲关节可用 `UseAbsoluteDelta = true`，这样只取弯曲幅度。
6. 对偏转/外展内收关节使用 signed mapping，不能取绝对值，否则自由度方向会丢失。
7. 进入 Play 后先张开手保持自然姿态，完成中立校准，再测试弯曲和偏转方向。

如果某个关节方向反了，只改对应 mapping 的 `PrimaryScale` 正负号，不要改 URDF 原始轴，除非 URDF 本身就是错的。

如果某个关节幅度不够，只调整对应 mapping 的 `PrimaryScale` / `SecondaryScale`。不要先改全局 `ResponseScale`，否则会影响其他已经正确的手指。
