"""Archive the real-input diagnosis and current full-scene regressions."""
from pathlib import Path
import hashlib
import json
import re

root = Path(__file__).resolve().parents[1]
logs = root / 'Logs/HandAudit'

def read(path):
    return json.loads((root / path).read_text(encoding='utf-8-sig'))

glove = read('Logs/GloveSand/results.json')
motion = read('Logs/ThumbMotion/results.json')
scene = read('Logs/HandAudit/scene-report.json')
runtime = read('Logs/HandAudit/runtime-report.json')
runtime_errors = read('Logs/HandAudit/runtime-errors.json')
for report in [glove, motion, scene, runtime, runtime_errors]:
    assert report['passed'] and not report['errors'], report.get('errors')
assert len(glove['checks']) >= 263
assert len(motion['checks']) == 14
assert runtime['time'] > motion['time']

sources = [
    'Assets/Runtime/Scripts/Wa100UdexRealArticulationMapper.cs',
    'Assets/Underwater/Runtime/Wa100JointPoseHold.cs',
    'Assets/Underwater/Runtime/UnderwaterRigChecks.cs',
    'Assets/Underwater/Editor/UnderwaterHandSceneAudit.cs',
    'Assets/Underwater/Editor/UnderwaterGloveSandChecks.cs',
    'Assets/Underwater/Editor/UnderwaterThumbMotionChecks.cs',
    'Assets/Underwater/Editor/UnderwaterPhysicsTestMode.cs',
    'Assets/Underwater/Runtime/UnderwaterPresence.cs',
    'Assets/Underwater/Runtime/UnderwaterTaskConsole.cs',
    'Assets/Underwater/Runtime/UnderwaterGeometryRadar.cs',
    'Assets/Underwater/Scenes/UnderwaterRadarSurveyEditable.unity',
]
result = {
    'diagnosis': 'Live role 3674 sends negative flexion. Prior positive-only fixtures missed that clamping signed negative inputs to positive URDF ranges zeroed four fingers and thumb flexion.',
    'before_live_snapshot': (logs / 'before-runtime.txt').read_text(encoding='utf-8-sig'),
    'after_live_snapshots': [p.read_text(encoding='utf-8-sig') for p in sorted(logs.glob('live-*.txt'))],
    'glove_sand_contact': glove,
    'mounted_motion_with_bent_fingers': motion,
    'scene_audit': scene,
    'runtime_audit': runtime,
    'runtime_errors': runtime_errors,
    'source_sha256': {p: hashlib.sha256((root / p).read_bytes()).hexdigest() for p in sources},
    'limitations': [
        'Live UDP reception and joint response are measured; no claim about subjective worn-PICO feel.',
        'HandDriver human skeleton and WA100 URDF axes differ: signed coordinate conversion preserves motion, not same numeric sign.',
        'The two-finger-link URDF does not reproduce human third-link motion or four-finger side-splay.',
        'Thumb roll r20 remains unsupported; two requested root freedoms and distal flexion are retained.',
        'Source/scene audit and current regressions do not prove all possible code paths or hardware behavior are error-free.',
        'Release regression attaches/releases targets as fixtures; it is not a new envelope-grasp success-rate trial.',
        'Scene hash includes an unrelated pre-existing thumb visual local-Z edit retained locally and excluded from the commit.',
        'A native Live Preview texture-queue shutdown crash occurred during repeated Play cycles; automated fixtures now disable XR startup temporarily and restore it afterward. This is mitigation, not a fix to the proprietary native plugin.',
    ],
}
(root / 'Documentation/HAND_SCENE_REPAIR_VALIDATION.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'glove_checks': len(glove['checks']), 'motion_checks': len(motion['checks']), 'scene_checks': len(scene['checks']), 'runtime_checks': len(runtime['checks']), 'objects': scene['objects'], 'components': scene['components'], 'errors': [], 'warnings': runtime['warnings']}, ensure_ascii=False))
