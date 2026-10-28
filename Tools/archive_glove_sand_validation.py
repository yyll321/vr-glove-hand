"""Archive verified glove/soil changes and isolate a pre-existing scene edit."""
from pathlib import Path
import hashlib
import json
import re
import shutil

root = Path(__file__).resolve().parents[1]
logs = root / 'Logs/GloveSand'
doc = root / 'Documentation'
report = json.loads((logs / 'results.json').read_text(encoding='utf-8-sig'))
assert report['passed'] and not report['errors']
sources = [
    'Assets/Runtime/Scripts/Network.cs',
    'Assets/Runtime/Scripts/HandDriver.cs',
    'Assets/Runtime/Scripts/Wa100UdexRealArticulationMapper.cs',
    'Assets/Underwater/Runtime/Wa100JointPoseHold.cs',
    'Assets/Underwater/Runtime/UnderwaterGraspTask.cs',
    'Assets/Underwater/Runtime/UnderwaterGraspBench.cs',
    'Assets/Underwater/Runtime/UnderwaterDeformableSand.cs',
    'Assets/Underwater/Editor/UnderwaterGloveSandChecks.cs',
    'Assets/Underwater/Editor/UnderwaterGloveSandUpgrade.cs',
    'Assets/Underwater/SandInteraction/FineWetSand.shader',
    'Assets/Underwater/Scenes/UnderwaterRadarSurveyEditable.unity',
]
archive = {
    'final': report,
    'source_sha256': {p: hashlib.sha256((root / p).read_bytes()).hexdigest() for p in sources},
    'limitations': [
        'Synthetic isolated UDP and scene physics verification; no live glove or worn PICO measurements.',
        'Extra Thumb_L1 pitch axis is a simulation extension to the original single-axis URDF.',
        'Four fingers have only two driven links; third human finger joint and non-thumb side-splay are not reproduced.',
        'Sand is a heightfield plus command resistance, not DEM, soft-body animals or calibrated terramechanics.',
        'Release fixtures explicitly attach/release targets, and do not validate envelope acquisition success.',
        'Scene hash includes the pre-existing manual local Z edit retained in the working copy, excluded from the implementation commit.',
    ],
    'earlier_rounds': [],
}
for name in ['round1', 'round2', 'round3', 'round4', 'round5']:
    path = logs / (name + '.json')
    if path.exists():
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        archive['earlier_rounds'].append({'time': data['time'], 'passed': data['passed'], 'checks': len(data['checks']), 'errors': data['errors']})
(doc / 'GLOVE_SAND_VALIDATION.json').write_text(json.dumps(archive, ensure_ascii=False, indent=2), encoding='utf-8')
(doc / 'figures').mkdir(exist_ok=True)
for area in ['pool', 'seabed']:
    shutil.copyfile(logs / f'cavity-{area}.png', doc / 'figures' / f'sand-cavity-{area}.png')

# The user's manual edit existed before this task. Save a minimal patch for
# reversing only that edit in Git's index, while keeping the working scene intact.
scene = 'Assets/Underwater/Scenes/UnderwaterRadarSurveyEditable.unity'
content = (root / scene).read_text(encoding='utf-8')
match = re.search(r'  m_GameObject: \{fileID: 1096939470204656444\}\n  serializedVersion: 2\n  m_LocalRotation:.*\n  m_LocalPosition: \{x: -0.0048, y: 0.0626, z: 0.0084\}', content)
assert match, 'Pre-existing manual placement must remain unchanged'
lines = match.group().splitlines()
line = content[:match.start()].count('\n') + 1
after = content[match.end():].splitlines()[1:4]
patch = f'diff --git a/{scene} b/{scene}\n--- a/{scene}\n+++ b/{scene}\n@@ -{line},7 +{line},7 @@\n'
patch += '\n'.join(' ' + item for item in lines[:3]) + '\n'
patch += '-' + lines[3].replace('z: 0.0084', 'z: 0') + '\n+' + lines[3] + '\n'
patch += '\n'.join(' ' + item for item in after) + '\n'
(logs / 'pre-existing-user-scene-edit.patch').write_bytes(patch.encode('utf-8'))
print(json.dumps({'time': report['time'], 'checks': len(report['checks']), 'errors': report['errors'], 'max_physical_joint_error_degrees': max(abs(r['actual']-r['requested']) for r in report['joints'])}, ensure_ascii=False))
