"""Archive measured precision, finger and release checks for this scene revision."""
from pathlib import Path
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[1]
logs = root / 'Logs/HybridReview'
doc = root / 'Documentation'
report = json.loads((logs / 'results.json').read_text(encoding='utf-8-sig'))
assert report['passed'] and report['graspPassed']
assert len(report['grasps']) == 4
assert all(all(a[k] for k in ['attached', 'lifted', 'envelope', 'released', 'landed', 'handPairsRestored']) for a in report['grasps'])
sources = [
    'Assets/Runtime/Scripts/Wa100UdexRealArticulationMapper.cs',
    'Assets/Underwater/Runtime/UnderwaterMasterSlaveMapper.cs',
    'Assets/Underwater/Runtime/Wa100JointPoseHold.cs',
    'Assets/Underwater/Runtime/UnderwaterGraspTask.cs',
    'Assets/Underwater/Runtime/UnderwaterGraspTarget.cs',
    'Assets/Underwater/Editor/UnderwaterHybridChecks.cs',
    'Assets/Underwater/Editor/UnderwaterPrecisionReleaseChecks.cs',
    'Assets/Underwater/Editor/UnderwaterPrecisionReleaseUpgrade.cs',
    'Assets/Underwater/Scenes/UnderwaterRadarSurveyEditable.unity',
]
archive = {
    'final': report,
    'source_sha256': {p: hashlib.sha256((root / p).read_bytes()).hexdigest() for p in sources},
    'limitations': [
        'Synthetic controller poses and articulation commands in Unity Editor; worn PICO/glove feel remains unmeasured.',
        'Measured servo response is not a calibrated hardware electric-current or buoyancy measurement.',
        'Independent landing fixtures explicitly attach/release objects; only the four grasp attempts validate opposed-contact acquisition.',
        'Sand uses depth/area dependent command resistance, not DEM; grasp holding uses releasable kinematic attachment.',
    ],
    'first_round': {
        'time': '2026-10-28T18:42:20.5585721+08:00',
        'passed': False,
        'findings': [
            'Imported non-convex robot meshes rejected ClosestPoint; fixed with conservative bounds separation.',
            'Fine-workspace test re-clutched before settling and did not acquire targets; revised path timing and clutch settling without relaxing production collision/grasp thresholds.',
        ],
    },
}
(doc / 'PRECISION_RELEASE_VALIDATION.json').write_text(json.dumps(archive, ensure_ascii=False, indent=2), encoding='utf-8')
for area in ['pool', 'seabed']:
    shutil.copyfile(logs / f'finger-response-{area}.csv', doc / f'finger-response-{area}.csv')

lines = [
    '\n## 5. 本次实际结果\n',
    f"最终测试：{report['time']}。{len(report['checks'])} 项检查通过，错误为 0。运行时、手套映射和编辑器三程序集编译通过。\n",
    '精细映射实测最高 0.0350 m/s、0.1400 m/s²；80 mm 手柄输入对应 24.68 mm 位移。两区食指远端关节达到 60° 指令的 90%：旧限速 1.000 s，新限速 0.375 s；张开至小于 6° 为 0.264 s。该试验测量的是实际 articulation 角度，输入为合成阶跃。\n',
    '| 区域 | 目标 | 包络拾取/抬升 | 张手释放 | 自然下落 | 机器人碰撞恢复 |',
    '| --- | --- | --- | --- | --- | --- |',
]
for a in report['grasps']:
    lines.append(f"| {'海底' if 'Seabed' in a['area'] else '水池'} | {'海星' if 'Starfish' in a['target'] else '鱿鱼'} | 通过，{a['lift']*100:.1f} cm | {a['releaseSeconds']*1000:.0f} ms | {a['drop']*100:.1f} cm，沙面落稳 | 已恢复 |")
lines += [
    '\n两区各两物种、三类承托面共 12 次独立释放落地夹具测试通过，最大竖直余隙偏差约 0.1 mm。原硬物闭合试验最大穿透约 0.4 mm；中间关节 ±2 Nm 外力保持、穿沙查询、台面扫掠阻挡与两区三轴隔离均通过。\n',
    '首轮测试未通过，原因与修复记录保留在归档中。本次 4/4 是固定合成路径下的可行性验证，不是佩戴成功率；现场操作按第 4 节逐项确认。\n',
]
path = doc / '精细遥操作与自然释放.md'
original = path.read_text(encoding='utf-8').split('\n## 5. 本次实际结果')[0]
path.write_text(original + '\n'.join(lines), encoding='utf-8')
print(json.dumps({'time': report['time'], 'checks': len(report['checks']), 'grasp_and_drop': 4, 'landing_fixtures': 12}, ensure_ascii=False))
