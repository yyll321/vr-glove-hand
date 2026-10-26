"""Import source dependency closure, remapping conflicting non-script GUIDs without touching existing assets."""
from pathlib import Path
import re, json, hashlib, uuid
src=Path('F:/unity/en/Assets'); dst=Path('Assets'); imported=dst/'Underwater/Imported/EnAssets'
def index(root):
    result={}
    for meta in root.rglob('*.meta'):
        m=re.search(rb'^guid: ([0-9a-f]{32})',meta.read_bytes(),re.M)
        if m: result[m[1].decode()]=Path(str(meta)[:-5])
    return result
source=index(src); existing=index(dst)
scene=dst/'Underwater/Imported/ROVpSource.unity'
refs=lambda data: set(x.decode() for x in re.findall(rb'guid: ([0-9a-f]{32})',data))
pending=list(refs(scene.read_bytes())); seen=set(); assets={}; remap={}; shared_scripts=[]; missing=[]
while pending:
    guid=pending.pop()
    if guid in seen or guid.startswith('000000'): continue
    seen.add(guid); asset=source.get(guid)
    if asset is None:
        if guid not in existing: missing.append(guid)
        continue
    if asset.is_dir(): continue
    data=asset.read_bytes(); meta=Path(str(asset)+'.meta').read_bytes()
    if data.startswith(b'%YAML'): pending.extend(refs(data))
    pending.extend(refs(meta)-{guid})
    old=existing.get(guid)
    if asset.suffix=='.cs' and old:
        shared_scripts.append(str(asset.relative_to(src))); continue
    if old and old.read_bytes()==data: continue
    if old and 'Underwater' not in old.parts:
        remap[guid]=uuid.uuid5(uuid.NAMESPACE_URL,'underwater-en-'+guid).hex
    assets[guid]=(asset,data,meta)
def rewrite(data):
    for a,b in remap.items(): data=data.replace(a.encode(),b.encode())
    return data
copied=[]
for guid,(asset,data,meta) in assets.items():
    out=imported/asset.relative_to(src); out.parent.mkdir(parents=True,exist_ok=True)
    out.write_bytes(rewrite(data) if data.startswith(b'%YAML') else data)
    Path(str(out)+'.meta').write_bytes(rewrite(meta)); copied.append(str(out))
scene.write_bytes(rewrite(scene.read_bytes()))
report={'source_dependency_count':len(seen),'copied':copied,'remapped_conflicts':remap,'shared_script_types':shared_scripts,'missing_guids':missing}
Path('Logs/ROVpDependencyClosure.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='copied'},ensure_ascii=False,indent=2))
