"""Saved-scene integrity checks, complementary to the Unity engine audit."""
from pathlib import Path
import json, math, re, struct
from check_underwater_physics_scene import blocks
ROOT=Path(__file__).resolve().parents[1]
A=ROOT/'Assets/Underwater'

def run():
    old=blocks(A/'Scenes/UnderwaterHydrodynamicsEditable.unity');scene=blocks(A/'Scenes/UnderwaterRadarSurveyEditable.unity');checks=[]
    def check(ok,description):
        if not ok:raise AssertionError(description)
        checks.append(description)
    check(set(old)<=set(scene),'Original scene object IDs retained')
    names={k:re.search(r'^  m_Name: (.+)$',v,re.M)[1] for k,v in scene.items() if v.startswith('--- !u!1 ')}
    def components(go):return [int(c) for c in re.findall(r'component: \{fileID: (\d+)\}',scene[go])]
    for k,n in names.items():
        if n.startswith('ROVp - ') or n.startswith('XR ') or n=='Main Camera':
            for cid in components(k):
                if cid in old and not 'guid: 6e877f20948349b6ad6d47d4a23dd4c1' in old[cid]:check(scene[cid].startswith(old[cid].rstrip()),f'Original ROV/XR fields preserved (new serialized defaults allowed): {n} / {cid}')
    new=[v for k,v in scene.items() if k not in old]
    for text in new:
        check('g<1>' not in text,'Serialized fields have no failed replacement markers')
        for cid in re.findall(r'\{fileID: (-?\d+)\}',text):check(int(cid)==0 or int(cid) in scene,'New internal reference resolves: '+cid)
    for go,n in names.items():
        if n.startswith(('Reef ','Bent Inspection Pipe ')):
            comps=[scene[k] for k in components(go)];mf=next(c for c in comps if c.startswith('--- !u!33 '));mc=next(c for c in comps if c.startswith('--- !u!64 '))
            mesh=lambda t:re.search(r'm_Mesh: \{[^}]+\}',t)[0]
            check(mesh(mf)==mesh(mc) and 'm_Convex: 0' in mc,'Visible mesh equals nonconvex geometric collider: '+n)
    layout=json.loads((A/'RadarSurvey/SurveyLayout.json').read_text());reefs=[i for i in layout['items'] if i['name'].startswith('Reef ')]
    check(layout['seed']==20261027 and len(layout['items'])==60 and len(reefs)==60,'Previous random layout has 60 reefs and no pipes/rings')
    rov=(60.947716,-.34033966)
    check(all(i['scale'][1]<=.720001 for i in reefs),'Low vertical reef scale')
    check('Pool ROV Inspection Obstacles' not in names.values(),'Pool manufactured obstacles removed')
    check('Generated Survey Reefs - random low layout' in names.values(),'Random low reef hierarchy present')
    for i,item in enumerate(reefs):
        x,y,z=item['position'];r=item['radius'];x-=62
        check(abs(x)+r<25 and abs(z)+r<40,'Reef stays inside expanded water volume')
        check(math.hypot(x+62-rov[0],z-rov[1])>r+5-.1 and math.hypot(x,z+8)>r+4-.001,'Reef clears ROV installation and observer spawn')
        for other in reefs[:i]:check(math.hypot(item['position'][0]-other['position'][0],z-other['position'][2])>=r+other['radius']+.65-1e-4,'Reef pair respects spacing')
    for path in (A/'RadarSurvey').glob('*.asset'):
        text=path.read_text()
        if not '--- !u!43 ' in text:continue
        count=int(re.search(r'm_VertexCount: (\d+)',text)[1]);raw=bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)',text)[1]);indices=bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',text)[1])
        check(len(raw)==count*48,'Mesh vertex stream length: '+path.stem)
        values=struct.unpack('<'+'f'*(len(raw)//4),raw);check(all(math.isfinite(v) for v in values),'Finite mesh channels: '+path.stem)
        check(max(struct.unpack('<'+'H'*(len(indices)//2),indices))<count,'Index range valid: '+path.stem)
    check('m_Extent: {x: 25, y: 5, z: 40}' in scene[1700000000000000049],'Expanded flow volume bounds')
    check(all(f'resolution{axis}: {n}' in scene[1700000000000000049] for axis,n in [('X',20),('Y',6),('Z',28)]),'Budgeted seabed flow grid: 3360 cells')
    result=dict(passed=True,count=len(checks),checks=checks,limitation='Saved-scene structure and mesh streams; see separate Unity audit and Play report for execution.')
    path=ROOT/'Logs/RadarReview/structure-checks.json';path.parent.mkdir(exist_ok=True);path.write_text(json.dumps(result,indent=2))
    print(f'PASS {len(checks)} saved radar-survey scene assertions')

if __name__=='__main__':run()
