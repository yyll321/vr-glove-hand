"""Archive the motion regression and its matching glove/contact regression."""
from pathlib import Path
import hashlib
import json

root = Path(__file__).resolve().parents[1]

def read(relative):
    return json.loads((root / relative).read_text(encoding='utf-8-sig'))

before = read('Logs/ThumbMotion/before.json')
after = read('Logs/ThumbMotion/results.json')
contact = read('Logs/GloveSand/results.json')
assert not before['passed'] and before['errors']
assert after['passed'] and not after['errors'] and len(after['inputRows']) == 2
assert contact['passed'] and not contact['errors']
assert contact['time'] > after['time'], 'Run the contact regression after the motion regression'
sources = [
    'Assets/Runtime/Scripts/Wa100UdexRealArticulationMapper.cs',
    'Assets/Underwater/Editor/UnderwaterGloveSandUpgrade.cs',
    'Assets/Underwater/Editor/UnderwaterThumbMotionChecks.cs',
    'Assets/Underwater/Editor/UnderwaterGloveSandChecks.cs',
    'Assets/Underwater/Runtime/Wa100JointPoseHold.cs',
    'Assets/Underwater/Scenes/UnderwaterRadarSurveyEditable.unity',
]
archive = {
    'before_serial_hinges': before,
    'after_serial_hinges': after,
    'glove_sand_contact_regression': contact,
    'preliminary_dynamic_fixture': {
        'report': read('Logs/ThumbMotion/round-dynamic-fine.json'),
        'explanation': '40 mm master stroke produced 12.17 mm slave descent with the existing precision mapping, below the test minimum of 15 mm. Finger tracking passed its limits. The final fixture uses an 80 mm master stroke; runtime mapping/speed parameters were not changed.',
    },
    'source_sha256': {p: hashlib.sha256((root / p).read_bytes()).hexdigest() for p in sources},
    'limitations': [
        'Editor physics and synthetic input tests; no worn PICO or live glove confirmation.',
        'Motion regression uses constant commands plus isolated UDP port 15557; never injects into live port 5555.',
        'The pitch hinge is a simulation extension, not a claim about the physical WA100 mechanism.',
        'Contact regression includes attach/release fixtures, not a new envelope-grasp success-rate measurement.',
        'Scene hash includes the retained pre-existing manual local-Z edit excluded from this implementation commit.',
    ],
}
path = root / 'Documentation/THUMB_MOTION_VALIDATION.json'
path.write_text(json.dumps(archive, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({
    'motion_checks': len(after['checks']),
    'contact_checks': len(contact['checks']),
    'peak_constant_thumb_error_deg': max(max(r['maxYawError'], r['maxPitchError']) for r in after['rows']),
    'peak_dynamic_servo_error_deg': max(r['maxServoError'] for r in after['inputRows']),
    'errors': after['errors'] + contact['errors'],
}, ensure_ascii=False))
