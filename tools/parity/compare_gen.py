#!/usr/bin/env python3
"""Compare reference (js_*.bin) vs port (cs_*.bin) chunk dumps: blocks, meta, light."""
import sys, json, os, glob, collections
import numpy as np
H = 384
names = {v: k for k, v in json.load(open(os.path.join(os.path.dirname(__file__), 'source_block_ids.json'))).items()}
def load(p):
    b = open(p, 'rb').read(); n = 16 * 16 * H
    return (np.frombuffer(b[:2 * n], np.uint16).reshape(16, 16, H), np.frombuffer(b[2 * n:3 * n], np.uint8).reshape(16, 16, H),
            np.frombuffer(b[3 * n:4 * n], np.uint8).reshape(16, 16, H))
d = sys.argv[1]; check_light = len(sys.argv) > 2 and sys.argv[2] == 'light'
tot = collections.Counter(); totcells = 0; totdiff = 0; metadiff = 0; lightdiff = 0
for jp in sorted(glob.glob(os.path.join(d, 'js_*.bin'))):
    cp = jp.replace('js_', 'cs_')
    if not os.path.exists(cp): continue
    jb, jm, jl = load(jp); cb, cm, cl = load(cp)
    diff = jb != cb; nd = int(diff.sum()); totdiff += nd; totcells += jb.size
    same = ~diff; md = int(((jm != cm) & same).sum()); metadiff += md
    ld = int((jl != cl).sum()) if check_light else 0; lightdiff += ld
    pairs = collections.Counter(zip(jb[diff].tolist(), cb[diff].tolist()))
    tot.update(pairs)
    print(f"{os.path.basename(jp)[3:-4]:>12}: block diffs {nd:6d} ({100*nd/jb.size:.3f}%)  meta diffs {md}  light diffs {ld}")
    if nd:
        idx = np.argwhere(diff)
        ys = idx[:, 2] - 64
        print(f"   y range of diffs: {ys.min()}..{ys.max()}")
print(f"TOTAL block mismatch {totdiff}/{totcells} = {100*totdiff/max(1,totcells):.4f}%  meta {metadiff}  light {lightdiff}")
for (a, b), n in tot.most_common(25): print(f"   ref {names.get(a,a):>24} -> port {names.get(b,b):<24} x{n}")
