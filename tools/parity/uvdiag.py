import sys, collections, numpy as np
exec(open(__import__('os').path.join(__import__('os').path.dirname(__file__),'compare_mesh.py')).read().split('tot = collections.Counter()')[0])
d=sys.argv[1]
js=load_js(d+'/jsmesh_0_0.bin'); cs=load_cs(d+'/csmesh_0_0.bin')
names={}
import json
blocks=json.load(open('../../reference/data/blocks.json'))['blocks']
H=384
b=open(d+'/js_0_0.bin','rb').read(); n=16*16*H
ids=np.frombuffer(b[:2*n],np.uint16).reshape(16,16,H)
def block_at(pos):
    # quad center -> containing cell (approx)
    c=np.mean(np.array(pos),axis=0)
    x,y,z=int(np.floor(c[0]+1e-3)),int(np.floor(c[1]+1e-3))+64,int(np.floor(c[2]+1e-3))
    for dx,dy,dz in [(0,0,0),(-1,0,0),(0,-1,0),(0,0,-1)]:
        xx,yy,zz=x+dx,y+dy,z+dz
        if 0<=xx<16 and 0<=zz<16 and ids[xx,zz,yy]: return blocks[ids[xx,zz,yy]]['key']
    return '?'
for l in range(3):
    qj,qc=quads(*js[l]),quads(*cs[l])
    cat=collections.Counter(); ex={}
    for k in qj:
        if k not in qc: continue
        a,bq=qj[k],qc[k]
        pts=list(a.keys())
        P=np.array(pts); ext=P.max(0)-P.min(0); normal='xyz'[int(np.argmin(ext))]
        au=np.array([a[p][3] for p in pts]); av=np.array([a[p][4] for p in pts])
        bu=np.array([bq[p][3] for p in pts]); bv=np.array([bq[p][4] for p in pts])
        if np.allclose(au,bu,atol=2e-4) and np.allclose(av,bv,atol=2e-4): c='same'
        elif np.allclose(au.min()+au.max()-au,bu,atol=2e-4) and np.allclose(av,bv,atol=2e-4): c='u-flip'
        elif np.allclose(au,bu,atol=2e-4) and np.allclose(av.min()+av.max()-av,bv,atol=2e-4): c='v-flip'
        elif np.allclose(au.min()+au.max()-au,bu,atol=2e-4) and np.allclose(av.min()+av.max()-av,bv,atol=2e-4): c='uv-flip'
        elif abs(au.min()-bu.min())>0.02 or abs(av.min()-bv.min())>0.02: c='other-tile'
        else: c='rotated/other'
        key=(normal,c); cat[key]+=1
        if c!='same' and (normal,c) not in ex: ex[(normal,c)]=block_at(pts)
    print(['opaque','water','transparent'][l])
    for k,v in sorted(cat.items()): print('   ',k,v, ex.get(k,''))
