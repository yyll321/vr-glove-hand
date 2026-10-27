"""Convert the two attributed public GLB downloads to metre-scale Unity OBJ assets.
Original downloads are kept in Logs/GraspResearch; no network or arbitrary extraction here.
"""
from pathlib import Path
import json,struct,numpy as np
root=Path(__file__).resolve().parents[1]
out=root/'Assets/Underwater/MarineAssets';out.mkdir(exist_ok=True)
for name,primitive,size in [('Starfish',2,.22),('Squid',0,.28)]:
 b=(root/'Logs/GraspResearch'/f'{name}.glb').read_bytes();n=struct.unpack_from('<I',b,12)[0]
 g=json.loads(b[20:20+n]);binary=b[28+n:]
 def read(index):
  a=g['accessors'][index];v=g['bufferViews'][a['bufferView']];types={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}
  width={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']];dtype=np.dtype(types[a['componentType']]);stride=v.get('byteStride',width*dtype.itemsize)
  return np.ndarray((a['count'],width),dtype=dtype,buffer=binary,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(stride,dtype.itemsize)).copy()
 p=g['meshes'][0]['primitives'][primitive];verts=read(p['attributes']['POSITION']);uv=read(p['attributes']['TEXCOORD_0']);tri=read(p['indices']).reshape(-1,3)
 if name=='Squid':verts=verts[:,[0,2,1]];tri=tri[:,[0,2,1]] # lie the mantle along Z
 lo=verts.min(0);hi=verts.max(0);verts-=(lo+hi)/2;verts*=size/max(hi-lo)
 lines=[f'mtllib {name}.mtl',f'o {name}']
 lines += ['v '+' '.join(f'{v:.7f}' for v in point) for point in verts]
 lines += ['vt '+' '.join(f'{v:.7f}' for v in point) for point in uv]
 lines += ['usemtl Marine']
 lines += ['f '+' '.join(f'{i+1}/{i+1}' for i in face) for face in tri]
 (out/f'{name}.obj').write_text('\n'.join(lines)+'\n')
 tex=g['textures'][g['materials'][p.get('material',0)]['pbrMetallicRoughness']['baseColorTexture']['index']]['source']
 image=g['images'][tex];v=g['bufferViews'][image['bufferView']];start=v.get('byteOffset',0)
 (out/f'{name}.png').write_bytes(binary[start:start+v['byteLength']])
 (out/f'{name}.mtl').write_text(f'newmtl Marine\nKd 1 1 1\nmap_Kd {name}.png\n')
 print(name,len(verts),'vertices',len(tri),'triangles',verts.min(0),verts.max(0))
