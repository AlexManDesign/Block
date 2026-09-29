#!/usr/bin/env python3
"""Diff meshWorker output (jsmesh_*.bin) vs port ChunkMesher output (csmesh_*.bin), quad by quad."""
import sys, os, glob, collections
import numpy as np
LN = ['opaque', 'water', 'transparent']
def load_js(p):
    b = open(p, 'rb').read(); h = np.frombuffer(b[:24], np.uint32); o = 24; out = []
    for l in range(3):
        nv, ni = int(h[2*l]), int(h[2*l+1])
        v = np.frombuffer(b[o:o+4*nv], np.float32).reshape(-1, 8); o += 4*nv
        i = np.frombuffer(b[o:o+4*ni], np.uint32); o += 4*ni
        out.append((v, i))
    return out
def load_cs(p):
    b = open(p, 'rb').read(); h = np.frombuffer(b[:24], np.uint32); o = 24; out = []
    for l in range(3):
        nf, ni = int(h[2*l]), int(h[2*l+1])
        v = np.frombuffer(b[o:o+4*nf], np.float32).reshape(-1, 8); o += 4*nf
        i = np.frombuffer(b[o:o+4*ni], np.uint32); o += 4*ni
        out.append((v, i))
    return out
def quads(v, idx):
    q = {}
    seen = collections.Counter()
    for k in range(0, len(idx), 6):
        vs = sorted(set(idx[k:k+6].tolist()))
        pos = tuple(sorted(tuple(np.round(v[j, :3].astype(np.float64), 3)) for j in vs))
        attrs = {tuple(np.round(v[j, :3].astype(np.float64), 3)): tuple(np.round(v[j, 5:8].astype(np.float64), 3)) + tuple(np.round(v[j, 3:5].astype(np.float64), 5)) for j in vs}
        seen[pos] += 1
        if seen[pos] > 1: pos = pos + (seen[pos],)
        q[pos] = attrs
    return q
tot = collections.Counter()
for jp in sorted(glob.glob(os.path.join(sys.argv[1], 'jsmesh_*.bin'))):
    cp = jp.replace('jsmesh_', 'csmesh_')
    if not os.path.exists(cp): continue
    js, cs = load_js(jp), load_cs(cp)
    print(os.path.basename(jp)[7:-4])
    for l in range(3):
        qj, qc = quads(*js[l]), quads(*cs[l])
        only_j = [k for k in qj if k not in qc]; only_c = [k for k in qc if k not in qj]
        common = [k for k in qj if k in qc]
        attr_bad = 0; shade_bad = 0; light_bad = 0; uv_bad = 0; ex = None; uvex = None
        for k in common:
            a, b = qj[k], qc[k]
            bad = False
            for p in a:
                if abs(a[p][0] - b[p][0]) > 0.011: shade_bad += 1; bad = True; ex = ex or (k, a, b)
                if abs(a[p][1] - b[p][1]) > 0.011 or abs(a[p][2] - b[p][2]) > 0.011: light_bad += 1; bad = True
                if abs(a[p][3] - b[p][3]) > 2e-4 or abs(a[p][4] - b[p][4]) > 2e-4: uv_bad += 1; bad = True; uvex = uvex or (k, a, b)
            attr_bad += bad
        print(f"  {LN[l]:>11}: ref quads {len(qj):6d}  port quads {len(qc):6d}  only-ref {len(only_j):5d}  only-port {len(only_c):5d}  attr-mismatch quads {attr_bad} (shade vtx {shade_bad}, light vtx {light_bad}, uv vtx {uv_bad})")
        tot[LN[l]+'_ref'] += len(qj); tot[LN[l]+'_only_ref'] += len(only_j); tot[LN[l]+'_only_port'] += len(only_c); tot[LN[l]+'_attr'] += attr_bad
        if len(sys.argv) > 2:
            for k in only_j[:int(sys.argv[2])]: print('     ref-only', k[:4])
            for k in only_c[:int(sys.argv[2])]: print('     port-only', k[:4])
            if ex: print('     attr example', ex)
            if uvex: print('     uv example', uvex)
print(dict(tot))
