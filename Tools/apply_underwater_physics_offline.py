"""Create a separately named, editable physics scene without starting Unity.

Used while the local Unity license dialog blocks Editor operations. This is a
deterministic serialization step, not an engine import or runtime validation.
Existing scene blocks are preserved apart from explicit component/mesh links.
"""
from pathlib import Path
import hashlib
import json
import math
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'Assets/Underwater'
SOURCE = ASSETS / 'Scenes/UnderwaterResearchEditable.unity'
TARGET = ASSETS / 'Scenes/UnderwaterHydrodynamicsEditable.unity'
REVIEW = ROOT / 'Logs/PhysicsReview'
FLUID = 'd7bf26c3f11e4559a5d3ba3a3fa3e210'
HYDRO = '6e877f20948349b6ad6d47d4a23dd4c1'
RELIEF = '062c662baf38430dbf7cfd326b31db5c'
BASE = '770ee5ceaa4f4d3c98673b2cc960a973'


def smooth(t):
    t = max(0, min(1, t))
    return t * t * (3 - 2 * t)


def relief(x, z):
    pad = smooth((math.hypot(x, z + 2) - 4) / 3)
    spawn = smooth((math.hypot(x, z + 8) - 2.5) / 2.5)
    edge = smooth(min(12.5 - abs(x), 25 - abs(z)) / 2)
    broad = .55 * (1 + math.sin(x * .38 + z * .15)) + .3 * (1 + math.cos(z * .31 - x * .2))
    ridge = .45 * math.exp(-((x + 6) / 2.5) ** 2) * math.sin(z * .3)
    return pad * spawn * edge * (broad + ridge)


def write_meta(path, guid, mesh=False):
    text = f'fileFormatVersion: 2\nguid: {guid}\n'
    if mesh:
        text += 'NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 4300000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    else:
        text += 'DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    Path(str(path) + '.meta').write_text(text, encoding='utf-8')


def make_relief():
    text = (ASSETS / 'Materials/SeabedTerrain.asset').read_text(encoding='utf-8')
    count = int(re.search(r'm_VertexCount: (\d+)', text)[1])
    assert count == 20301 and '    m_DataSize: 974448' in text
    raw = bytearray.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', text)[1])
    indices = struct.unpack('<120000H', bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)', text)[1]))
    assert len(raw) == count * 48
    verts = [list(struct.unpack_from('<3f', raw, i * 48)) for i in range(count)]
    original_y = [v[1] for v in verts]
    for v in verts:
        v[1] += relief(v[0], v[2])
    normals = [[0., 0., 0.] for _ in verts]
    for i in range(0, len(indices), 3):
        ia, ib, ic = indices[i:i+3]
        a, b, c = verts[ia], verts[ib], verts[ic]
        u = [b[k] - a[k] for k in range(3)]
        v = [c[k] - a[k] for k in range(3)]
        n = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
        for at in (ia, ib, ic):
            for k in range(3):
                normals[at][k] += n[k]
    for i, (p, normal) in enumerate(zip(verts, normals)):
        length = math.sqrt(sum(x*x for x in normal))
        normal = [x / length for x in normal]
        tangent = [1., -normal[0] / max(.001, normal[1]), 0.]
        length = math.sqrt(sum(x*x for x in tangent))
        tangent = [x / length for x in tangent]
        struct.pack_into('<3f3f4f', raw, i * 48, *p, *normal, *tangent, -1.)
    ys = [v[1] for v in verts]
    centre, extent = (max(ys)+min(ys))/2, (max(ys)-min(ys))/2
    text = re.sub(r'_typelessdata: [0-9a-f]+', '_typelessdata: ' + raw.hex(), text)
    text = re.sub(r'm_Center: \{x: 0, y: [^,]+, z: 0\}', f'm_Center: {{x: 0, y: {centre:.8g}, z: 0}}', text)
    text = re.sub(r'm_Extent: \{x: 12.5, y: [^,]+, z: 25\}', f'm_Extent: {{x: 12.5, y: {extent:.8g}, z: 25}}', text)
    target = ASSETS / 'Materials/SeabedTerrain_HydrodynamicRelief.asset'
    target.write_text(text.replace('m_Name: SeabedTerrain', 'm_Name: Seabed Terrain Hydrodynamic Relief'), encoding='utf-8')
    write_meta(target, RELIEF, mesh=True)
    base = ASSETS / 'Materials/SeabedTerrain_HydrodynamicRelief_Base.asset'
    base.write_bytes((ASSETS / 'Materials/SeabedTerrain.asset').read_bytes())
    write_meta(base, BASE, mesh=True)
    return verts, original_y, {'vertices': count, 'triangles': len(indices)//3, 'height_min_m': min(ys), 'height_max_m': max(ys)}


def create_scene():
    if TARGET.exists():
        raise RuntimeError('Destination already exists. Use the Unity menu for further edits; this tool never overwrites a manually edited scene.')
    before = SOURCE.read_bytes()
    text = before.decode('utf-8-sig')
    parts = re.split(r'(?=^--- !u!)', text, flags=re.M)
    prefix, docs = parts[0], parts[1:]
    blocks = {int(re.search(r'^--- !u!\d+ &(\d+)', s)[1]): s for s in docs}
    types = {k: int(re.search(r'^--- !u!(\d+)', v)[1]) for k, v in blocks.items()}
    names = {re.search(r'^  m_Name: (.+)$', v, re.M)[1]: k for k, v in blocks.items() if types[k] == 1}
    sequence = 1700000000000000000

    def next_id():
        nonlocal sequence
        sequence += 1
        assert sequence not in blocks
        return sequence

    def refs(go):
        return [int(x) for x in re.findall(r'component: \{fileID: (\d+)\}', blocks[go])]

    def component(go, kind):
        return next(k for k in refs(go) if types[k] == kind)

    def add(kind, body, name):
        uid = next_id()
        blocks[uid] = f'--- !u!{kind} &{uid}\n{name}:\n{body}'
        types[uid] = kind
        return uid

    common = '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n'
    presence = next(k for k, v in blocks.items() if 'guid: 1f1069e0cf7dfaa4f8b2e4ede8ada742' in v)
    template_go = names['Pool Floor - 25 x 50 m']
    template_renderer = blocks[component(template_go, 23)]
    template_box = blocks[component(template_go, 65)]

    def game_object(name, pos, scale=(1, 1, 1), parent=0, angle=(0, 0, 0), mesh=None, material=None):
        go = add(1, '', 'GameObject')
        angle_r = [math.radians(x) / 2 for x in angle]
        # Course rotations have one nonzero axis.
        q = [math.sin(a) for a in angle_r] + [math.prod(math.cos(a) for a in angle_r)]
        vec = lambda v: '{x: %g, y: %g, z: %g}' % tuple(v)
        t = add(4, common + f'  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n  m_LocalRotation: {{x: {q[0]}, y: {q[1]}, z: {q[2]}, w: {q[3]}}}\n' +
                f'  m_LocalPosition: {vec(pos)}\n  m_LocalScale: {vec(scale)}\n  m_ConstrainProportionsScale: 0\n  m_Children: []\n  m_Father: {{fileID: {parent}}}\n  m_LocalEulerAnglesHint: {vec(angle)}\n', 'Transform')
        components = [t]
        collider = None
        if mesh:
            components.append(add(33, common + f'  m_GameObject: {{fileID: {go}}}\n  m_Mesh: {{fileID: {mesh}, guid: 0000000000000000e000000000000000, type: 0}}\n', 'MeshFilter'))
            renderer = next_id()
            renderer_text = re.sub(r'^--- !u!23 &\d+', f'--- !u!23 &{renderer}', template_renderer)
            renderer_text = re.sub(r'm_GameObject: \{fileID: \d+\}', f'm_GameObject: {{fileID: {go}}}', renderer_text)
            renderer_text = re.sub(r'(m_Materials:\n  - \{fileID: 2100000, guid: )[a-f0-9]+', r'\g<1>' + material, renderer_text)
            blocks[renderer] = renderer_text; types[renderer] = 23; components.append(renderer)
            collider = next_id()
            box_text = re.sub(r'^--- !u!65 &\d+', f'--- !u!65 &{collider}', template_box)
            box_text = re.sub(r'm_GameObject: \{fileID: \d+\}', f'm_GameObject: {{fileID: {go}}}', box_text)
            # Unity's cylinder mesh has a local height of two units.
            collider_size = 'm_Size: {x: 1, y: 2, z: 1}' if mesh == 10206 else 'm_Size: {x: 1, y: 1, z: 1}'
            box_text = re.sub(r'm_Size: \{[^}]+\}', collider_size, box_text)
            box_text = re.sub(r'm_Center: \{[^}]+\}', 'm_Center: {x: 0, y: 0, z: 0}', box_text)
            blocks[collider] = box_text; types[collider] = 65; components.append(collider)
        blocks[go] += common + '  serializedVersion: 6\n  m_Component:\n' + ''.join(f'  - component: {{fileID: {c}}}\n' for c in components) + f'  m_Layer: {8 if mesh else 0}\n  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n'
        if parent:
            if '  m_Children: []' in blocks[parent]:
                blocks[parent] = blocks[parent].replace('  m_Children: []', f'  m_Children:\n  - {{fileID: {t}}}')
            else:
                blocks[parent] = blocks[parent].replace('  m_Father:', f'  - {{fileID: {t}}}\n  m_Father:')
        else:
            roots = next(k for k in blocks if types[k] == 1660057539)
            blocks[roots] += f'  - {{fileID: {t}}}\n'
        return go, t, collider

    def behaviour(go, guid, fields):
        b = add(114, common + f'  m_GameObject: {{fileID: {go}}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}\n  m_Name: \n  m_EditorClassIdentifier: \n' + fields, 'MonoBehaviour')
        blocks[go] = blocks[go].replace('  m_Layer:', f'  - component: {{fileID: {b}}}\n  m_Layer:')
        return b

    verts, original_y, mesh_report = make_relief()
    terrain_go = names['Sand Terrain - Teleportation Area']
    for cid in refs(terrain_go):
        if types[cid] in (33, 64):
            blocks[cid] = re.sub(r'(m_Mesh: \{fileID: 4300000, guid: )[a-f0-9]+', r'\g<1>' + RELIEF, blocks[cid])
    # Preserve the visible placement of all original grass/rocks on the new bed.
    reprojected = 0
    for name, go in [(re.search(r'^  m_Name: (.+)$', v, re.M)[1], k) for k, v in list(blocks.items()) if types[k] == 1]:
        if name not in ('Weathered Boulder', 'Seagrass Patch'):
            continue
        tid = component(go, 4)
        m = re.search(r'm_LocalPosition: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}', blocks[tid])
        x, y, z = map(float, m.groups())
        ix, iz = max(0, min(100, round((x-62+12.5)*4))), max(0, min(200, round((z+25)*4)))
        delta = verts[iz*101+ix][1] - original_y[iz*101+ix]
        blocks[tid] = blocks[tid].replace(m[0], f'm_LocalPosition: {{x: {x:.9g}, y: {y+delta:.9g}, z: {z:.9g}}}')
        reprojected += 1

    _, course_t, _ = game_object('Hydrodynamic Obstacle Course', (62, 0, 0))
    steel, rock = 'a32e72ab048cae74da77009a6018bfe2', '55bd27e84e50bea4496d809c7b5aa51a'
    course = [
        ('Obstacle Pipe Gate Left', (-7,2.2,8), (.22,2.2,.22), (0,0,0), 10206, steel),
        ('Obstacle Pipe Gate Right', (-3,2.2,8), (.22,2.2,.22), (0,0,0), 10206, steel),
        ('Obstacle Pipe Gate Header', (-5,4.3,8), (.22,2,.22), (0,0,90), 10206, steel),
        ('Obstacle Low Tunnel', (6,2.8,7), (5.5,.35,1.2), (0,0,0), 10202, rock),
        ('Obstacle Tunnel Support Left', (3.4,1.4,7), (.3,2.8,1.2), (0,0,0), 10202, rock),
        ('Obstacle Tunnel Support Right', (8.6,1.4,7), (.3,2.8,1.2), (0,0,0), 10202, rock),
        ('Obstacle Survey Marker', (6,.9,12), (.65,.9,.65), (0,0,0), 10202, rock),
        ('Obstacle Pipe Inspection', (0,.75,15), (.65,2.3,.65), (90,0,0), 10206, steel),
    ]
    obstacles = [game_object(name, pos, scale, course_t, angle, mesh, material)[2] for name, pos, scale, angle, mesh, material in course]
    volumes = []
    for sea in (False, True):
        label = 'Seabed' if sea else 'Pool'
        go, _, _ = game_object(label + ' Fluid Volume', (0,0,0))
        ground_id = component(terrain_go, 64) if sea else component(template_go, 65)
        fields = f'  worldBounds:\n    m_Center: {{x: {62 if sea else 0}, y: 5, z: 0}}\n    m_Extent: {{x: 12.5, y: 5, z: 25}}\n  ambientSource: {{fileID: {presence}}}\n  ambientCurrent: {{x: {0.16 if sea else 0.08}, y: 0, z: 0.04}}\n  resolutionX: 16\n  resolutionY: 8\n  resolutionZ: 32\n  viscosity: 0.18\n  densityDecay: 0.06\n  ambientBlend: 0.28\n  simulationHz: 10\n  pressureIterations: 4\n  simulate: 1\n  drawGizmos: 0\n  ground: {{fileID: {ground_id}}}\n'
        fields += ('  obstacles:\n' + ''.join(f'  - {{fileID: {c}}}\n' for c in obstacles)) if sea else '  obstacles: []\n'
        fields += '  maximumSpeed: 1.5\n'
        vol = behaviour(go, FLUID, fields); volumes.append(vol)
        rov = names['ROVp - ' + label]
        behaviour(rov, HYDRO, f'  presence: {{fileID: {presence}}}\n  volume: {{fileID: {vol}}}\n  body: {{fileID: 0}}\n  applyForces: 0\n  applyBuoyancy: 0\n  displacedVolume: 0.065\n  fluidDensity: {1025 if sea else 1000}\n  frontalArea: 0.34\n  lateralArea: 0.48\n  verticalArea: 0.3\n  dragCoefficient: 0.9\n  angularResistance: {{x: 8, y: 10, z: 8}}\n  centerOfBuoyancy: {{x: 0, y: 1.3, z: 0}}\n  surfaceY: 10\n  bottomY: 0\n  hullHeight: 0.8\n')
    blocks[presence] += '  fluidVolumes:\n' + ''.join(f'  - {{fileID: {v}}}\n' for v in volumes)
    roots = next(k for k in blocks if types[k] == 1660057539)
    output = prefix + ''.join(v for k, v in blocks.items() if k != roots) + blocks[roots]
    TARGET.write_text(output, encoding='utf-8')
    write_meta(TARGET, 'b13b9b3d90e749e28d2539a38c9aac91')
    assert SOURCE.read_bytes() == before, 'Original scene was changed'
    assert len(blocks) == len(set(blocks)), 'Duplicate scene file IDs'
    report = dict(source=str(SOURCE), scene=str(TARGET), source_sha256=hashlib.sha256(before).hexdigest(),
                  scene_sha256=hashlib.sha256(TARGET.read_bytes()).hexdigest(), scene_objects=len(blocks),
                  volumes=2, cells_per_volume=4096, simulation_hz=10, collidable_obstacles=len(obstacles),
                  reprojected_grass_and_rocks=reprojected, terrain=mesh_report,
                  validation='Serialized structural checks only. Unity import, runtime, HMD and frame timing are not tested.')
    REVIEW.mkdir(parents=True, exist_ok=True)
    (REVIEW / 'offline-scene.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    create_scene()
