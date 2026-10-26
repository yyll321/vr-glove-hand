"""Read-only extraction of the complete ROVp scene hierarchy from the en project."""
from pathlib import Path
import re, shutil, json

SOURCE = Path('F:/unity/en')
DEST = Path(__file__).resolve().parents[1]
scene_path = SOURCE / 'Assets/shenghao_getcar.unity'
raw = scene_path.read_text(encoding='utf-8-sig')
blocks = {}
for match in re.finditer(r'^--- !u!(\d+) &(-?\d+)(?: stripped)?\n.*?(?=^--- !u!|\Z)', raw, re.M | re.S):
    blocks[int(match[2])] = (int(match[1]), match[0])
root = next(i for i,(kind,text) in blocks.items() if kind == 1001 and '      value: ROVp\n' in text)
selected = set()
pending = [root]
while pending:
    i = pending.pop()
    if i in selected or i not in blocks: continue
    selected.add(i)
    kind, text = blocks[i]
    refs = []
    if kind == 1001:
        refs += re.findall(r'addedObject: \{fileID: (-?\d+)\}', text)
        refs += [str(j) for j,(k,t) in blocks.items() if re.search(r'm_PrefabInstance: \{fileID: '+str(i)+r'\}', t)]
    elif kind == 1:
        refs += re.findall(r'component: \{fileID: (-?\d+)\}', text)
        refs += re.findall(r'm_PrefabInstance: \{fileID: (-?\d+)\}', text)
    elif kind in (4,224):
        children = re.search(r'  m_Children:(.*?)(?=^  \w|\Z)', text, re.M | re.S)
        if children: refs += re.findall(r'fileID: (-?\d+)', children[1])
        refs += re.findall(r'm_GameObject: \{fileID: (-?\d+)\}|m_PrefabInstance: \{fileID: (-?\d+)\}', text)
        refs = [next(v for v in x if v) if isinstance(x,tuple) else x for x in refs]
    pending.extend(int(v) for v in refs)

# Include runtime components on every selected GameObject, including added components.
for i,(kind,text) in blocks.items():
    match = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', text)
    if match and int(match[1]) in selected: selected.add(i)

output = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
removed_refs = []
for i in blocks:
    if i not in selected: continue
    kind,text = blocks[i]
    def clean(m):
        ref = int(m[1])
        if ref and ref not in selected:
            removed_refs.append((i,ref))
            return '{fileID: 0}'
        return m[0]
    output += re.sub(r'\{fileID: (-?\d+)\}', clean, text)
isolated = DEST / 'Assets/Underwater/Imported/ROVpSource.unity'
isolated.parent.mkdir(parents=True, exist_ok=True)
isolated.write_text(output,encoding='utf-8')

guid_paths = {}
for meta in (SOURCE/'Assets').rglob('*.meta'):
    text = meta.read_text(encoding='utf-8-sig',errors='replace')
    m=re.search(r'^guid: ([0-9a-f]{32})',text,re.M)
    if m: guid_paths[m[1]] = Path(str(meta)[:-5])
existing = {}
for meta in (DEST/'Assets').rglob('*.meta'):
    m=re.search(r'^guid: ([0-9a-f]{32})',meta.read_text(encoding='utf-8-sig',errors='replace'),re.M)
    if m: existing[m[1]]=str(meta)
pending=set(re.findall(r'guid: ([0-9a-f]{32})', output))
copied=[]; scripts=[]; missing=[]; visited=set()
while pending:
    guid=pending.pop()
    if guid in visited or guid in existing or guid.startswith('00000000'): continue
    visited.add(guid)
    path=guid_paths.get(guid)
    if path is None: missing.append(guid); continue
    if path.is_dir(): continue
    relative=path.relative_to(SOURCE/'Assets')
    dest=DEST/'Assets/Underwater/Imported/EnAssets'/relative
    dest.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(path,dest); shutil.copy2(str(path)+'.meta',str(dest)+'.meta')
    copied.append(str(relative))
    if path.suffix == '.cs': scripts.append(str(relative))
    if path.suffix.lower() in {'.prefab','.unity','.mat','.asset','.controller','.anim','.fbx'}:
        data=path.read_bytes()
        # Binary FBX can reference assets via its YAML importer metadata.
        if data.startswith(b'%YAML'): pending.update(re.findall(rb'guid: ([0-9a-f]{32})',data)[j].decode() for j in range(len(re.findall(rb'guid: ([0-9a-f]{32})',data))))
        pending.update(re.findall(r'guid: ([0-9a-f]{32})',Path(str(path)+'.meta').read_text(encoding='utf-8-sig',errors='replace')))

# Authored seabed/rock meshes and their texture sources are reused with new URP materials.
for folder in ('FBX','Textures'):
    source=SOURCE/'Assets/Underwater_FX'/folder
    target=DEST/'Assets/Underwater/Imported/Nature'/folder
    shutil.copytree(source,target,dirs_exist_ok=True)

report={'source_scene':str(scene_path),'root_instance':root,'blocks':len(selected),'external_scene_refs_cleared':removed_refs,
        'copied_assets':copied,'scripts':scripts,'missing_guids':missing}
(DEST/'Logs/ROVpMigration.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ('copied_assets','external_scene_refs_cleared')},ensure_ascii=False,indent=2))
