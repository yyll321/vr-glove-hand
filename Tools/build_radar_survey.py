"""Deterministic asset/scene serialization; no Unity runtime claims.
Creates a new scene once and refuses to overwrite later manual scene edits.
"""
from pathlib import Path
import hashlib, json, math, random, re, struct

ROOT=Path(__file__).resolve().parents[1]
A=ROOT/'Assets/Underwater'
OUT=A/'RadarSurvey'
SCENE=A/'Scenes/UnderwaterRadarSurveyEditable.unity'
SOURCE=A/'Scenes/UnderwaterHydrodynamicsEditable.unity'
SEED=20261027

def guid(path): return hashlib.md5(('underwater-radar-survey:'+str(path.relative_to(ROOT)).replace('\\','/')).encode()).hexdigest()
def meta(path,kind='DefaultImporter',fileid=0):
    target=Path(str(path)+'.meta')
    if target.exists():return re.search(r'guid: (\w+)',target.read_text())[1]
    data=f'fileFormatVersion: 2\nguid: {guid(path)}\n{kind}:\n  externalObjects: {{}}\n'
    if fileid:data+=f'  mainObjectFileID: {fileid}\n'
    if kind=='MonoImporter':data+='  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n'
    target.write_text(data+'  userData: \n  assetBundleName: \n  assetBundleVariant: \n',encoding='utf-8')
    return guid(path)

def add(a,b):return tuple(x+y for x,y in zip(a,b))
def sub(a,b):return tuple(x-y for x,y in zip(a,b))
def mul(a,s):return tuple(x*s for x in a)
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def cross(a,b):return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def unit(a):return mul(a,1/max(1e-12,math.sqrt(dot(a,a))))
def smooth(t):t=max(0,min(1,t));return t*t*(3-2*t)
def noise(x,z,seed=0):
    ix,iz=math.floor(x),math.floor(z);tx,tz=smooth(x-ix),smooth(z-iz)
    def h(a,b):
        n=(a*374761393+b*668265263+seed*1442695041)&0xffffffff;n=((n^(n>>13))*1274126177)&0xffffffff
        return (n^(n>>16))/4294967295
    return (h(ix,iz)*(1-tx)+h(ix+1,iz)*tx)*(1-tz)+(h(ix,iz+1)*(1-tx)+h(ix+1,iz+1)*tx)*tz
def height(x,z):
    pad=smooth((math.hypot(x+1,z)-4)/3)*smooth((math.hypot(x,z+8)-3)/3)
    edge=smooth(min(25-abs(x),40-abs(z))/3)
    terrain=1.4*noise(x*.08,z*.08,11)+.65*noise(x*.19,z*.19,17)+.2*noise(x*.52,z*.52,37)
    return max(0,pad*edge*(terrain-.35))

TEMPLATE=(A/'Materials/SeabedTerrain.asset').read_text(encoding='utf-8')
mesh_stats=[]
def mesh_asset(name,verts,faces,uv=None,closed=False):
    assert len(verts)<65536
    normals=[(0,0,0) for _ in verts]
    for a,b,c in faces:
        n=cross(sub(verts[b],verts[a]),sub(verts[c],verts[a]));assert dot(n,n)>1e-16,(name,a,b,c)
        for i in (a,b,c):normals[i]=add(normals[i],n)
    if closed:
        edges={}
        for f in faces:
            for a,b in zip(f,(f[1],f[2],f[0])):
                key=tuple(sorted((a,b)));edges[key]=edges.get(key,0)+1
        assert all(v==2 for v in edges.values()),name
        volume=sum(dot(verts[a],cross(verts[b],verts[c])) for a,b,c in faces)/6
        assert volume>0,(name,volume)
    else:volume=None
    raw=bytearray()
    for i,p in enumerate(verts):
        n=unit(normals[i]);t=unit(cross((0,0,1) if abs(n[2])<.9 else (0,1,0),n))
        coord=uv[i] if uv else (p[0]*.5,p[2]*.5)
        raw+=struct.pack('<3f3f4f2f',*p,*n,*t,-1,*coord)
    indices=[i for f in faces for i in f]
    lo=[min(v[k] for v in verts) for k in range(3)];hi=[max(v[k] for v in verts) for k in range(3)]
    vec=lambda v:'{x: %.9g, y: %.9g, z: %.9g}'%tuple(v)
    text=re.sub(r'm_Name: SeabedTerrain','m_Name: '+name,TEMPLATE)
    text=re.sub(r'indexCount: \d+',f'indexCount: {len(indices)}',text)
    text=re.sub(r'vertexCount: \d+',f'vertexCount: {len(verts)}',text)
    text=re.sub(r'm_VertexCount: \d+',f'm_VertexCount: {len(verts)}',text)
    text=re.sub(r'm_DataSize: \d+',f'm_DataSize: {len(raw)}',text)
    text=re.sub(r'_typelessdata: [0-9a-f]+','_typelessdata: '+raw.hex(),text)
    text=re.sub(r'm_IndexBuffer: [0-9a-f]+','m_IndexBuffer: '+struct.pack('<'+'H'*len(indices),*indices).hex(),text)
    text=re.sub(r'm_Center: \{[^}]+\}','m_Center: '+vec([(a+b)/2 for a,b in zip(lo,hi)]),text)
    text=re.sub(r'm_Extent: \{[^}]+\}','m_Extent: '+vec([(b-a)/2 for a,b in zip(lo,hi)]),text)
    path=OUT/(name+'.asset');path.write_text(text,encoding='utf-8');g=meta(path,'NativeFormatImporter',4300000)
    mesh_stats.append(dict(name=name,vertices=len(verts),triangles=len(faces),closed=closed,volume=volume,bounds=[lo,hi]))
    return g

def rock(index):
    p=(1+math.sqrt(5))/2
    verts=[unit(v) for v in [(-1,p,0),(1,p,0),(-1,-p,0),(1,-p,0),(0,-1,p),(0,1,p),(0,-1,-p),(0,1,-p),(p,0,-1),(p,0,1),(-p,0,-1),(-p,0,1)]]
    faces=[(0,11,5),(0,5,1),(0,1,7),(0,7,10),(0,10,11),(1,5,9),(5,11,4),(11,10,2),(10,7,6),(7,1,8),(3,9,4),(3,4,2),(3,2,6),(3,6,8),(3,8,9),(4,9,5),(2,4,11),(6,2,10),(8,6,7),(9,8,1)]
    for _ in range(3):
        cache={};new=[]
        def mid(a,b):
            key=tuple(sorted((a,b)))
            if key not in cache:cache[key]=len(verts);verts.append(unit(add(verts[a],verts[b])))
            return cache[key]
        for a,b,c in faces:
            ab,bc,ca=mid(a,b),mid(b,c),mid(c,a);new.extend([(a,ab,ca),(b,bc,ab),(c,ca,bc),(ab,bc,ca)])
        faces=new
    deformed=[]
    for x,y,z in verts:
        f=.79+.13*math.sin(4*x+index)*math.cos(5*z-index*.7)+.1*math.sin(8*y+3*z+index)+.04*math.sin(17*x-9*z+7*y)
        # Asymmetric lobes, small fissures and a sloping crown break ellipsoid silhouettes.
        xx=x*f+.13*y*z;zz=z*f+.1*x*y;yy=y*f+.09*x-.07*z*z
        if yy>.32:yy=.32+(yy-.32)*(.6+.25*math.sin(index+x*4))
        deformed.append((xx,yy,zz))
    extent=max(math.hypot(v[0],v[2]) for v in deformed)
    deformed=[mul(v,1/extent) for v in deformed]
    uv=[(math.atan2(z,x)/(2*math.pi)+.5,math.asin(max(-1,min(1,y)))/math.pi+.5) for x,y,z in verts]
    return mesh_asset(f'ReefVariant{index:02}',deformed,faces,uv,True)

def tube():
    # A bent, closed inspection pipe with a genuine circular cross section.
    verts=[];faces=[];uv=[];n=24;segments=32
    for j in range(segments+1):
        z=j*.3;x=.7*math.sin(j/segments*math.pi);dx=.7*math.pi/(segments*.3)*math.cos(j/segments*math.pi)
        tangent=unit((dx,0,1));right=unit(cross((0,1,0),tangent))
        for i in range(n):
            a=i*math.tau/n;p=add((x,0,z),add(mul(right,.24*math.cos(a)),(0,.24*math.sin(a),0)))
            verts.append(p);uv.append((i/n,j*.3))
    for j in range(segments):
        for i in range(n):
            a=j*n+i;b=j*n+(i+1)%n;c=(j+1)*n+i;d=(j+1)*n+(i+1)%n
            faces.extend([(a,b,c),(b,d,c)])
    for row,reverse in [(0,True),(segments,False)]:
        centre=len(verts);verts.append((.7*math.sin(row/segments*math.pi),0,row*.3));uv.append((.5,.5))
        for i in range(n):
            a=row*n+i;b=row*n+(i+1)%n;faces.append((centre,b,a) if reverse else (centre,a,b))
    return mesh_asset('BentInspectionPipe',verts,faces,uv,True)

def generate():
    if SCENE.exists():raise RuntimeError('Scene exists; refusing to overwrite manual edits. Use the Unity manifest restore menu for generated obstacles only.')
    OUT.mkdir(exist_ok=True);meta(OUT)
    for path in list((A/'Runtime').glob('*.cs'))+list((A/'Shaders').glob('*.hlsl')):
        meta(path,'MonoImporter' if path.suffix=='.cs' else 'ShaderIncludeImporter')
    verts=[(x*.5-25,height(x*.5-25,z*.5-40),z*.5-40) for z in range(161) for x in range(101)]
    faces=[]
    for z in range(160):
        for x in range(100):
            a=z*101+x;faces.extend([(a,a+101,a+1),(a+1,a+101,a+102)])
    terrain=mesh_asset('SurveySeabed80x50',verts,faces)
    # Preserve the original surface grid density while expanding its world extents.
    original=(A/'Materials').glob('*.asset')
    water_path=next(p for p in original if Path(str(p)+'.meta').exists() and '06e6f7b6c34bf3a45b574b948c21bcea' in Path(str(p)+'.meta').read_text())
    wt=water_path.read_text();raw=bytes.fromhex(re.search('_typelessdata: ([0-9a-f]+)',wt)[1]);count=int(re.search(r'm_VertexCount: (\d+)',wt)[1]);stride=len(raw)//count
    wv=[struct.unpack_from('<3f',raw,i*stride) for i in range(count)]
    wi=struct.unpack('<'+'H'*(len(re.search('m_IndexBuffer: ([0-9a-f]+)',wt)[1])//4),bytes.fromhex(re.search('m_IndexBuffer: ([0-9a-f]+)',wt)[1]))
    water=mesh_asset('SurveyWater80x50',[(x*2,y,z*1.6) for x,y,z in wv],list(zip(wi[::3],wi[1::3],wi[2::3])))
    variants=[rock(i) for i in range(12)];pipe=tube()
    hull_verts=[(x*.25,y*.13,z*.25) for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    hull_faces=[(0,2,1),(0,3,2),(4,5,6),(4,6,7),(0,1,5),(0,5,4),(3,6,2),(3,7,6),(0,4,7),(0,7,3),(1,2,6),(1,6,5)]
    hull=mesh_asset('ClosedDisplacementHull65L',hull_verts,hull_faces,closed=True)
    materials=[]
    for i,color in enumerate([(0.62,.65,.56),(.42,.48,.45),(.72,.66,.54)]):
        path=OUT/f'ReefTone{i}.mat';mt=(A/'Materials/ReefLimestone.mat').read_text()
        mt=mt.replace('m_Name: ReefLimestone',f'm_Name: Survey Reef Tone {i}')
        mt=re.sub(r'(- _BaseColor:) \{[^}]+\}',lambda m:m[1]+' '+('{r: %g, g: %g, b: %g, a: 1}'%color),mt)
        path.write_text(mt);materials.append(meta(path,'NativeFormatImporter',2100000))
    watermat=OUT/'SurveyWater.mat';mt=(A/'Materials/SeaGerstnerWater.mat').read_text().replace('m_Name: SeaGerstnerWater','m_Name: Survey Water')
    # The old baked cube describes a smaller scene. Use the analytic fallback until recaptured.
    mt=mt.replace('_EnvironmentReady: 1','_EnvironmentReady: 0').replace('{r: 74.5, g: 10, b: 25, a: 0}','{r: 87, g: 10, b: 40, a: 0}').replace('{r: 49.5, g: 0, b: -25, a: 0}','{r: 37, g: 0, b: -40, a: 0}')
    watermat.write_text(mt);watermatguid=meta(watermat,'NativeFormatImporter',2100000)
    rng=random.Random(SEED);items=[];circles=[]
    zones=[(-22,-7,-33,29),(7,22,-32,33),(-7,7,20,35),(-8,9,-35,-21)]
    for i in range(60):
        zone=zones[i%4];small=i>=40
        for attempt in range(1000):
            x=rng.uniform(zone[0],zone[1]);z=rng.uniform(zone[2],zone[3]);sx=rng.uniform(.55,1.1) if small else rng.uniform(1.1,2.9)
            sz=sx*rng.uniform(.6,1.65);sy=rng.uniform(.45,1) if small else rng.uniform(1,3.3);radius=max(sx,sz)
            if abs(x)+radius>24 or abs(z)+radius>39:continue
            if math.hypot(x+1,z)<radius+5 or math.hypot(x,z+8)<radius+4:continue
            if abs(x)<radius+3 and -17-radius<z<17+radius:continue
            if any(math.hypot(x-a,z-b)<radius+r+.65 for a,b,r in circles):continue
            break
        else:raise AssertionError('Unable to pack reef layout')
        circles.append((x,z,radius));variant=rng.randrange(12);yaw=rng.uniform(0,360)
        # A shallow burial hides the tapered underside, without suspending the rock.
        min_y=mesh_stats[2+variant]['bounds'][0][1]
        pos=[62+x,height(x,z)-min_y*sy-.22*sy,z]
        items.append(dict(name=f'Reef {i+1:02} - Zone {i%4+1}',mesh=f'Assets/Underwater/RadarSurvey/ReefVariant{variant:02}.asset',material=f'Assets/Underwater/RadarSurvey/ReefTone{rng.randrange(3)}.mat',position=pos,scale=[sx,sy,sz],yaw=yaw,zone=i%4+1,radius=radius))
    # Two recognisable survey targets, outside the protected installation area.
    for i,(x,z,yaw) in enumerate([(3.8,15,24),(-12,-16,-35)]):
        items.append(dict(name=f'Bent Inspection Pipe {i+1}',mesh='Assets/Underwater/RadarSurvey/BentInspectionPipe.asset',material='Assets/Underwater/Materials/LadderSteel.mat',position=[62+x,height(x,z)+.3,z],scale=[1,1,1],yaw=yaw,zone=0,radius=0))
    manifest=dict(seed=SEED,seabedSize=[50,80],protectedRovRadius=5,items=items)
    (OUT/'SurveyLayout.json').write_text(json.dumps(manifest,indent=2));meta(OUT/'SurveyLayout.json')
    text=SOURCE.read_text(encoding='utf-8-sig');parts=re.split(r'(?=^--- !u!)',text,flags=re.M);prefix=parts[0]
    blocks={int(re.search(r'^--- !u!\d+ &(\d+)',s)[1]):s for s in parts[1:]}
    types={k:int(re.search(r'^--- !u!(\d+)',v)[1]) for k,v in blocks.items()}
    names={re.search(r'^  m_Name: (.+)$',v,re.M)[1]:k for k,v in blocks.items() if types[k]==1}
    ids=1800000000000000000
    def newid():
        nonlocal ids
        ids+=1;assert ids not in blocks;return ids
    def refs(go):return [int(x) for x in re.findall(r'component: \{fileID: (\d+)\}',blocks[go])]
    def comp(go,kind):return next(k for k in refs(go) if types[k]==kind)
    common='  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n'
    def doc(kind,name,body):
        uid=newid();blocks[uid]=f'--- !u!{kind} &{uid}\n{name}:\n'+common+body;types[uid]=kind;return uid
    roots=next(k for k,v in types.items() if v==1660057539)
    vec=lambda v:'{x: %.9g, y: %.9g, z: %.9g}'%tuple(v)
    def setvec(t,field,v):blocks[t]=re.sub(field+r': \{[^}]+\}',field+': '+vec(v),blocks[t])
    def attach(go,cid):blocks[go]=blocks[go].replace('  m_Layer:',f'  - component: {{fileID: {cid}}}\n  m_Layer:')
    def game(name,pos=(0,0,0),scale=(1,1,1),yaw=0,parent=0):
        go=doc(1,'GameObject','');a=math.radians(yaw)/2
        t=doc(4,'Transform',f'  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n  m_LocalRotation: {{x: 0, y: {math.sin(a)}, z: 0, w: {math.cos(a)}}}\n  m_LocalPosition: {vec(pos)}\n  m_LocalScale: {vec(scale)}\n  m_ConstrainProportionsScale: 0\n  m_Children: []\n  m_Father: {{fileID: {parent}}}\n  m_LocalEulerAnglesHint: {{x: 0, y: {yaw}, z: 0}}\n')
        blocks[go]+=f'  serializedVersion: 6\n  m_Component:\n  - component: {{fileID: {t}}}\n  m_Layer: 8\n  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n'
        if parent:
            if '  m_Children: []' in blocks[parent]:blocks[parent]=blocks[parent].replace('  m_Children: []',f'  m_Children:\n  - {{fileID: {t}}}')
            else:blocks[parent]=blocks[parent].replace('  m_Father:',f'  - {{fileID: {t}}}\n  m_Father:')
        else:blocks[roots]+=f'  - {{fileID: {t}}}\n'
        return go,t
    def behaviour(go,script,fields):
        sg=meta(A/'Runtime'/script,'MonoImporter')
        c=doc(114,'MonoBehaviour',f'  m_GameObject: {{fileID: {go}}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {sg}, type: 3}}\n  m_Name: \n  m_EditorClassIdentifier: \n'+fields);attach(go,c);return c
    terrain_go=names['Sand Terrain - Teleportation Area'];terrain_c=comp(terrain_go,64)
    renderer_template=blocks[comp(terrain_go,23)];collider_template=blocks[terrain_c]
    def replace_mesh(cid,g):blocks[cid]=re.sub(r'(m_Mesh: \{fileID: 4300000, guid: )[a-f0-9]+',lambda m:m[1]+g,blocks[cid])
    def mesh(go,g,mat=None,collide=True):
        mf=doc(33,'MeshFilter',f'  m_GameObject: {{fileID: {go}}}\n  m_Mesh: {{fileID: 4300000, guid: {g}, type: 2}}\n');attach(go,mf)
        for kind,template in ([(23,renderer_template)] if mat else [])+([(64,collider_template)] if collide else []):
            c=newid();body=re.sub(r'^--- !u!\d+ &\d+',f'--- !u!{kind} &{c}',template);body=re.sub(r'm_GameObject: \{fileID: \d+\}',f'm_GameObject: {{fileID: {go}}}',body)
            blocks[c]=body;types[c]=kind;attach(go,c)
            if kind==23:blocks[c]=re.sub(r'(m_Materials:\n  - \{fileID: 2100000, guid: )[a-f0-9]+',lambda m:m[1]+mat,body)
            else:replace_mesh(c,g);blocks[c]=re.sub(r'm_Convex: \d+','m_Convex: 0',blocks[c])
        return mf, c if collide else None
    for c in refs(terrain_go):
        if types[c] in (33,64):replace_mesh(c,terrain)
    blocks[names['NaturalSeabed_50x25x10m']]=blocks[names['NaturalSeabed_50x25x10m']].replace('NaturalSeabed_50x25x10m','NaturalSeabed_80x50x10m')
    blocks[names['Hydrodynamic Obstacle Course']]=blocks[names['Hydrodynamic Obstacle Course']].replace('m_IsActive: 1','m_IsActive: 0')
    for name,pos,scale in [('Seabed South Boundary',(62,4.9,-40.25),(50,10.2,.5)),('Seabed North Boundary',(62,4.9,40.25),(50,10.2,.5)),('Seabed East Boundary',(87.25,4.9,0),(.5,10.2,81)),('Seabed West Boundary',(36.75,4.9,0),(.5,10.2,81))]:
        go=names[name];t=comp(go,4);setvec(t,'m_LocalPosition',pos);setvec(t,'m_LocalScale',scale)
    deck=names['Seabed Observation Deck'];setvec(comp(deck,4),'m_LocalPosition',(90.5,9.85,-43.5))
    surface=names['Seabed Water Surface Y10'];replace_mesh(comp(surface,33),water)
    c=comp(surface,23);blocks[c]=re.sub(r'(m_Materials:\n  - \{fileID: 2100000, guid: )[a-f0-9]+',lambda m:m[1]+watermatguid,blocks[c])
    for k,v in list(blocks.items()):
        if types[k]==114 and 'volumeCenter: {x: 62' in v:blocks[k]=v.replace('volumeSize: {x: 25, y: 10, z: 50}','volumeSize: {x: 50, y: 10, z: 80}')
        if types[k]==1 and 'm_Name: Weathered Boulder\n' in v:blocks[k]=v.replace('m_IsActive: 1','m_IsActive: 0')
        if types[k]==1 and 'm_Name: Seagrass Patch\n' in v:
            t=comp(k,4);m=re.search(r'm_LocalPosition: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}',blocks[t]);x,y,z=map(float,m.groups());setvec(t,'m_LocalPosition',(x,height(x-62,z)+.01,z))
    system=2070406193;behaviour(system,'UnderwaterRegionSettings.cs','  seabedSize: {x: 50, y: 80}\n')
    gen,gen_t=game('Generated Survey Reefs - seed 20261027');colliders=[]
    for item in items:
        go,t=game(item['name'],item['position'],item['scale'],item['yaw'],gen_t)
        mg=re.search(r'guid: (\w+)',Path(str(ROOT/item['mesh'])+'.meta').read_text())[1];matg=re.search(r'guid: (\w+)',Path(str(ROOT/item['material'])+'.meta').read_text())[1]
        _,c=mesh(go,mg,matg);colliders.append(c)
    sea_fluid=1700000000000000049
    blocks[sea_fluid]=blocks[sea_fluid].replace('m_Extent: {x: 12.5, y: 5, z: 25}','m_Extent: {x: 25, y: 5, z: 40}').replace('resolutionX: 16','resolutionX: 20').replace('resolutionY: 8','resolutionY: 6').replace('resolutionZ: 32','resolutionZ: 28')
    blocks[1700000000000000045]=blocks[1700000000000000045].replace('resolutionY: 8','resolutionY: 6').replace('resolutionZ: 32','resolutionZ: 24')
    blocks[sea_fluid]=re.sub(r'  obstacles:\n(?:  - \{fileID: \d+\}\n)+','  obstacles:\n'+''.join(f'  - {{fileID: {c}}}\n' for c in colliders),blocks[sea_fluid])
    motor=6760376107593058603
    for sea in (False,True):
        label='Seabed' if sea else 'Pool';rov=names['ROVp - '+label];rt=comp(rov,4);q=[float(x) for x in re.search(r'm_LocalRotation: \{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\}',blocks[rt]).groups()]
        # Imported roots have a scale of 0.002 and a nonstandard local axis frame.
        # Freeze initial world +Z as the survey direction; exposed correction permits calibration.
        correction='{x: %.9g, y: %.9g, z: %.9g, w: %.9g}'%(-q[0],-q[1],-q[2],q[3])
        proxy,pt=game(label+' Closed Displacement Proxy - invisible');mf,_=mesh(proxy,hull,collide=False)
        buoy=behaviour(proxy,'UnderwaterSurfaceBuoyancy.cs',f'  displacementHull: {{fileID: {mf}}}\n  body: {{fileID: 0}}\n  vehicle: {{fileID: {rt}}}\n  bodyAxesCorrection: {correction}\n  hullOffsetMetres: {{x: 0, y: 1.3, z: 0}}\n  applyForces: 0\n  waterY: 10\n  fluidDensity: {1025 if sea else 1000}\n')
        hydro=1700000000000000050 if sea else 1700000000000000046
        blocks[hydro]+=f'  bodyAxesCorrection: {correction}\n  surfaceBuoyancy: {{fileID: {buoy}}}\n'
        scanner,st=game(label+' Ideal Geometry Radar')
        radar=behaviour(scanner,'UnderwaterGeometryRadar.cs',f'  vehicle: {{fileID: {rt}}}\n  vehicleAxesCorrection: {correction}\n  mountOffsetMetres: {{x: 0, y: 1.2, z: 2}}\n  ignoreRoot: {{fileID: {rt}}}\n  motor: {{fileID: {motor}}}\n  seabed: {int(sea)}\n  detectionMask:\n    serializedVersion: 2\n    m_Bits: 256\n  minimumRange: 0.15\n  scanHz: 5\n  raysPerFrame: 256\n  tiltSpeed: 25\n  scanning: 0\n  showPanel: 1\n  selectedProfile: 0\n')
        behaviour(scanner,'UnderwaterRadarRosInterface.cs',f'  radar: {{fileID: {radar}}}\n  configurationTopic: /geometry_radar/config\n  scanTopic: /geometry_radar/frame\n  requestTopic: /geometry_radar/config_request\n')
    SCENE.write_text(prefix+''.join(v for k,v in blocks.items() if k!=roots)+blocks[roots],encoding='utf-8');meta(SCENE)
    (OUT/'MeshValidation.json').write_text(json.dumps(mesh_stats,indent=2));meta(OUT/'MeshValidation.json')
    report=dict(scene=str(SCENE),seed=SEED,generatedObstacles=len(items),reefCount=60,terrainTriangles=32000,meshValidation=mesh_stats,validation='Offline serialization and mesh topology only. Engine import, physics and HMD rendering require Unity.')
    review=ROOT/'Logs/RadarReview';review.mkdir(exist_ok=True);(review/'generation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps({k:v for k,v in report.items() if k!='meshValidation'},ensure_ascii=False,indent=2))

if __name__=='__main__':generate()
