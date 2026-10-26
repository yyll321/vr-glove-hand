"""Deterministic physically scaled sand grain and ripple maps for the native Unity scene."""
from pathlib import Path
import numpy as np
from PIL import Image
out=Path('Assets/Underwater/Materials'); out.mkdir(parents=True,exist_ok=True)
rng=np.random.default_rng(421)
n=1024
y,x=np.mgrid[0:n,0:n]/n
ripple=np.sin(2*np.pi*(y*13+0.34*np.sin(x*2*np.pi)+0.10*np.sin(x*8*np.pi)))
fine=rng.normal(0,0.012,(n,n))
coarse=0.012*np.sin(x*18*np.pi)*np.sin(y*10*np.pi)+0.009*np.cos(x*42*np.pi+y*20*np.pi)
value=0.026*ripple+coarse+fine
color=np.stack([0.70+value,0.65+value,0.51+value*0.85],axis=-1)
Image.fromarray(np.uint8(np.clip(color,0,1)*255)).save(out/'SandGrain.png')
h=0.0018*ripple+fine*0.0008
dy,dx=np.gradient(h,3/n)
norm=np.stack([-dx,np.ones_like(dx),-dy],axis=-1)
# Tangent axes are U,V,N, rather than world axes.
norm=np.stack([-dx,-dy,np.ones_like(dx)],axis=-1); norm/=np.linalg.norm(norm,axis=-1)[...,None]
Image.fromarray(np.uint8(np.clip(norm*.5+.5,0,1)*255)).save(out/'SandRippleNormal.png')
