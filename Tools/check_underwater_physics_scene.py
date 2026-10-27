"""Structural regression of the saved physics scene; does not replace Unity QA."""
from pathlib import Path
import json
import math
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'Assets/Underwater'


def blocks(path):
    sections = re.split(r'(?=^--- !u!)', path.read_text(encoding='utf-8-sig'), flags=re.M)[1:]
    ids = [int(re.search(r'^--- !u!\d+ &(\d+)', s)[1]) for s in sections]
    assert len(ids) == len(set(ids)), 'Duplicate serialized file ID'
    return dict(zip(ids, sections))


def run():
    base = blocks(ASSETS / 'Scenes/UnderwaterResearchEditable.unity')
    scene = blocks(ASSETS / 'Scenes/UnderwaterHydrodynamicsEditable.unity')
    checks = []

    def check(value, description):
        if not value:
            raise AssertionError(description)
        checks.append(description)

    check(set(base) <= set(scene), 'All original scene object IDs are retained')
    names = {k: re.search(r'^  m_Name: (.+)$', v, re.M)[1] for k,v in scene.items() if v.startswith('--- !u!1 ')}
    protected_objects = {k for k,n in names.items() if n.startswith('ROVp - ') or n.startswith('XR ') or n=='Main Camera'}
    for go in protected_objects:
        for ref in re.findall(r'component: \{fileID: (\d+)\}', base[go]):
            cid = int(ref)
            check(base[cid] == scene[cid], 'Preserved original component ' + names[go] + ' ' + ref)
    check(sum('guid: d7bf26c3f11e4559a5d3ba3a3fa3e210' in v for v in scene.values())==2, 'Two fluid volumes are serialized')
    check(sum('guid: 6e877f20948349b6ad6d47d4a23dd4c1' in v for v in scene.values())==2, 'Two hydrodynamic estimates are serialized')
    check(len([n for n in names.values() if n.startswith('Obstacle ')])==8, 'Eight editable obstacle pieces exist')
    for go, name in names.items():
        if not name.startswith('Obstacle '):
            continue
        components = [scene[int(ref)] for ref in re.findall(r'component: \{fileID: (\d+)\}', scene[go])]
        if any('m_Mesh: {fileID: 10206,' in item for item in components):
            check(any('m_Size: {x: 1, y: 2, z: 1}' in item for item in components),
                  'Cylinder collider covers the complete mesh height ' + name)
    for cid in set(scene)-set(base):
        for ref in re.findall(r'\{fileID: (-?\d+)\}', scene[cid]):
            check(int(ref)==0 or int(ref) in scene, 'New internal reference resolves ' + str(cid) + ' to ' + ref)
        if 'guid: 6e877f20948349b6ad6d47d4a23dd4c1' in scene[cid]:
            check('  applyForces: 0' in scene[cid], 'ROV physics is estimate-only by default ' + str(cid))
    roots = re.search(r'SceneRoots:\n[\s\S]+', next(v for v in scene.values() if v.startswith('--- !u!1660057539 ')))[0]
    root_ids = [int(x) for x in re.findall(r'fileID: (\d+)', roots)]
    check(len(root_ids)==len(set(root_ids)), 'Scene roots contain no duplicate entries')
    for root in root_ids:
        check('  m_Father: {fileID: 0}' in scene[root], 'Root transform is unparented ' + str(root))
    original = (ASSETS / 'Materials/SeabedTerrain.asset').read_text(encoding='utf-8')
    mesh = (ASSETS / 'Materials/SeabedTerrain_HydrodynamicRelief.asset').read_text(encoding='utf-8')
    data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', mesh)[1])
    original_data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', original)[1])
    check(len(data)==20301*48, 'Terrain vertex data length matches its declared layout')
    check(re.search(r'm_IndexBuffer: (\w+)',mesh)[1]==re.search(r'm_IndexBuffer: (\w+)',original)[1], 'Terrain topology and winding are preserved')
    normals, deltas, pad = [], [], []
    for i in range(20301):
        x,y,z,nx,ny,nz = struct.unpack_from('<6f',data,48*i)
        old_y = struct.unpack_from('<f',original_data,48*i+4)[0]
        normals.append((nx*nx+ny*ny+nz*nz,ny))
        deltas.append(y-old_y)
        if math.hypot(x,z+2)<=4 or math.hypot(x,z+8)<=2.5:
            pad.append(abs(y-old_y))
    check(max(pad)<1e-5, 'ROV pad and observer spawn retain their original elevation')
    check(max(deltas)>1, 'Surrounding three-dimensional relief exceeds one metre')
    check(all(abs(length-1)<1e-5 and y>0 for length,y in normals), 'Every terrain normal is finite normalized and upward')
    check(all(math.isfinite(v) for v in struct.unpack('<'+'f'*(len(data)//4),data)), 'All terrain vertex channels are finite')
    result = dict(passed=True, count=len(checks), checks=checks,
                  limitation='Serialization, reference integrity and mesh geometry only. No Unity import, collision execution, CFD accuracy or HMD performance claim.')
    output = ROOT / 'Logs/PhysicsReview/scene-checks.json'
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf-8')
    print(f'PASS {len(checks)} saved-scene structural checks. Unity runtime verification remains pending.')


if __name__ == '__main__':
    run()
