"""Archive the two final Editor verification rounds, with source hashes."""
from pathlib import Path
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[1]
log = root / 'Logs/HybridReview'
report = json.loads((log / 'results.json').read_text(encoding='utf-8-sig'))
previous = json.loads((log / 'round8.json').read_text(encoding='utf-8-sig'))
assert report['passed'] and report['graspPassed'], 'Final verification has not passed'
assert previous['passed'] and previous['graspPassed'], 'Previous verification has not passed'
assert report['time'] != previous['time'], 'Wait for the repeat run to finish'
doc = root / 'Documentation'
files = [
    'Assets/Underwater/Runtime/UnderwaterMasterSlaveMapper.cs',
    'Assets/Underwater/Runtime/PicoArmPoseDriver.cs',
    'Assets/Underwater/Runtime/Wa100JointPoseHold.cs',
    'Assets/Underwater/Runtime/UnderwaterGraspBench.cs',
    'Assets/Underwater/Runtime/UnderwaterGraspTask.cs',
    'Assets/Underwater/Runtime/UnderwaterArmSurfaceGuard.cs',
    'Assets/Underwater/Editor/UnderwaterHybridChecks.cs',
    'Assets/Underwater/Editor/UnderwaterHybridUpgrade.cs',
    'Assets/Underwater/Scenes/UnderwaterRadarSurveyEditable.unity',
]
archive = dict(final=report, previous=previous,
               final_source_sha256={p:hashlib.sha256((root/p).read_bytes()).hexdigest() for p in files},
               limitations=['Editor synthetic XR and articulation commands, not worn PICO/glove trials.',
                            'Grasp uses opposed lower-side geometry plus releasable kinematic attachment, not a force-closure/load solver.',
                            'Sand is depth/area-dependent command admittance, not a calibrated granular/DEM solver.'])
(doc / 'HYBRID_GRASP_VALIDATION.json').write_text(json.dumps(archive, ensure_ascii=False, indent=2), encoding='utf-8')
shutil.copyfile(log/'Research/sources.json', doc/'HYBRID_RESEARCH_SOURCES.json')
for name in ['bench-arm','bench-close']:
    shutil.copyfile(log/(name+'.png'), doc/'figures'/('hybrid-'+name+'.png'))

rows = ['| 区域 | 对象 | 包络/拾取 | 抬升 | 陷入沙层 | 台面余隙 |', '| --- | --- | --- | --- | --- | --- |']
for a in report['grasps']:
    rows.append(f"| {'海底' if 'Seabed' in a['area'] else '水池'} | {'海星' if 'Starfish' in a['target'] else '鱿鱼'} | {'通过' if a['attached'] and a['lifted'] and a['envelope'] else '未通过'} | {a['lift']*100:.1f} cm | {a['insertionDepth']*100:.1f} cm | {a['supportClearance']*100:.1f} cm |")
axes = ['| 区域 | 轴 | 目标 | 实际位移 X/Y/Z | 三维误差 | 最大反向微步 |', '| --- | --- | --- | --- | --- | --- |']
for a in report['axes']:
    axes.append(f"| {'海底' if 'Seabed' in a['area'] else '水池'} | {a['axis']} | 60 mm | {a['actual']['x']*1000:.2f} / {a['actual']['y']*1000:.2f} / {a['actual']['z']*1000:.2f} mm | {a['error']*1000:.2f} mm | {a.get('maximumReverseStep',0)*1000:.3f} mm |")
metrics = '\n'.join('- '+c for c in report['checks'] if any(k in c for k in ['stale/no-command','commanded closure','jitter','72/90','Contact discards']))
rows_text = '\n'.join(rows)
axes_text = '\n'.join(axes)
section = f'''## 6. 本轮实际结果

最终测试时间：{report['time']}。上一成功轮：{previous['time']}。两次均全部通过，两区共 **8/8 次合成输入包络拾取与抬升**。这是按脚本执行的两轮可行性复现，不是统计意义的真人抓取成功率。

{rows_text}

### 左右方向及其他轴的隔离结果

{axes_text}

X/Y 在原始安装姿态测试；Z 在肩部 -12°、肘部 +42° 的弯肘姿态测试。最初近伸直姿态的 Z 向试验出现 54–60 mm 误差，原始记录保留在 `Logs/HybridReview/round1.json`。它表明径向可达性/奇异位形限制，不能通过无限放大手柄倍率解决。新映射限制前导并保持有限输出，不会凭空增加机构自由度或可达范围。

### 约束、滤波与接触实际测量

{metrics}

| 检查 | 预期 | 实际 | 尚未覆盖的风险 |
| --- | --- | --- | --- |
| 左右映射 | 单轴同向、连续、平滑，上下不退化 | 两区 X/Y 单轴 60 mm 试验通过；连续死区、比例线性、反向微步与抖动检查通过 | PICO/真人姿态、追踪丢失恢复手感需佩戴确认 |
| 海星 | 足够的可见厚度和夹持高度，仍为一只 | 模型局部 Z 75 mm，碰撞代理竖直 78 mm，两区均完成包络抬升 | 盒体代理未刻画海星各腕的凹部；未模拟生物软组织 |
| 无命令构型保持 | 中间关节不被外力推成大角度后弯 | 约 25° 中间姿态施加 ±2 Nm，偏差在测试容差内；硬物闭合命令被拒绝，穿透小于 5 mm | 虚拟制动参数未进行实机标定；硬件零电流是否自锁需实机确认 |
| 穿沙与包络 | 指节可进入，阻力随深度/面积增加，硬台面阻挡 | 25 mm 穿沙查询、非零阻力、深度/面积递增检查、台面重叠/扫掠阻挡通过；抓取插入和台面余隙见上表 | 当前为运动导纳近似；不能用于预测真实沙土载荷 |

验收脚本禁用真人输入驱动，合成 XR 位姿送入生产 `ApplyControllerPose` 链路，合成关节闭合指令经过同一 `Wa100JointPoseHold`。目标始终留在沙台上，通过真实指节表面的接近/对向/下半部判定；未用 F5，未把目标传送进手心。海星使用约 -35° 的手腕倾斜，并在初次对位失败后尝试 0.38/0.46/0.54 的预构型和表面接触规划；因此结果验证可行姿态存在，并不证明随机姿态都能抓。

早期多轮失败均保留在 `Logs/HybridReview/round1.json` 至 `round7.json`。最终提高成功率的配置是：75 mm 海星、16 cm 沙层、整体移近 14 cm 的沙台、刚性构型保持、闭合意图与接受角度分离，以及硬接触时保留切向微调。没有靠手指被动后弯、放宽包络阈值或远程吸附获得成功。

归档：`HYBRID_GRASP_VALIDATION.json` 包含两轮明细及最终源文件 SHA-256；`HYBRID_RESEARCH_SOURCES.json` 保存本轮来源检索结果（包括失败请求）。运行中 CSV 在 `Logs/HybridReview`，包含 raw/normalized/期望速度/限速结果/目标/实际末端/四关节角/奇异值/接触/阻力。`axis-*.csv` 记录隔离轴逐段数据，不依赖同一渲染帧中的日志限频。

![沙台与整臂实际编辑器截图](figures/hybrid-bench-arm.png)

## 7. 改动文件和回退

- `UnderwaterMasterSlaveMapper.cs`：独立主从映射、阶段切换、死区、滤波、限速/限加速度与遥测。
- `PicoArmPoseDriver.cs`：接入新映射，保留原 IK 与阻挡；输出实际接受关节角。
- `UnderwaterArmSurfaceGuard.cs`：仅补充接触法向输出，原判定阈值与阻挡逻辑保留。
- `Wa100JointPoseHold.cs`：命令/实际构型分离、制动窗口、主动指节碰撞预检与沙内速度修正。
- `UnderwaterGraspBench.cs`：碰撞过滤、深度/面积/方向阻力、工具和关节命令导纳。
- `UnderwaterGraspTask.cs`：闭合意图、相向下侧几何接触判定与一致的间距诊断。
- `UnderwaterHybridUpgrade.cs`：保存备份后更新已有沙台和两只目标、加厚网格与场景组件配置；不重建整个环境。
- `UnderwaterHybridChecks.cs`：编辑器轴隔离、物理保持、硬阻挡、沙层与四次抓取试验。
- `StarfishThickZ.asset`、`ATTRIBUTION.md`、`UnderwaterRadarSurveyEditable.unity`：模型、授权修改记录、实际可编辑场景。

场景升级前备份位于 `Assets/Underwater/Backups/Survey-beforeHybrid-*.unity`。新旧控制可通过 `PicoArmPoseDriver.MasterSlave` 组件启用状态对照；回退整套物理配置应恢复场景备份或对应 Git 提交，不应只关闭手指保持后把高驱动力直接作用于障碍物。已有 UserSettings 不纳入提交。
'''
path = doc/'混合遥操作与沙中包络抓取.md'
original = path.read_text(encoding='utf-8')
path.write_text(original.split('## 6. 本轮实际结果')[0]+section, encoding='utf-8')
print(json.dumps(dict(final=report['time'],previous=previous['time'],checks=len(report['checks']),errors=report['errors'],grasp_trials=8),ensure_ascii=False))
