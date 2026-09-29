# Lists mesher parity differences grouped by the block (and meta) that produced them.
import sys, os, collections, json, numpy as np
exec(open(os.path.join(os.path.dirname(__file__), 'compare_mesh.py')).read().split('tot = collections.Counter()')[0])
d = sys.argv[1]; H = 384
blocks = json.load(open(os.path.join(os.path.dirname(__file__), '../../reference/data/blocks.json')))['blocks']
b = open(d + '/js_0_0.bin', 'rb').read(); n = 16 * 16 * H
ids = np.frombuffer(b[:2 * n], np.uint16).reshape(16, 16, H); metas = np.frombuffer(b[2 * n:3 * n], np.uint8).reshape(16, 16, H)
def owner(pos):
    P = np.array(pos); c = P.mean(0)
    best = None
    for dx in (0, -1, 1):
        for dy in (0, -1, 1):
            for dz in (0, -1, 1):
                x, y, z = int(np.floor(c[0])) + dx, int(np.floor(c[1])) + dy + 64, int(np.floor(c[2])) + dz
                if 0 <= x < 16 and 0 <= z < 16 and 0 <= y < H and ids[x, z, y]:
                    dist = abs(x + .5 - c[0]) + abs(y - 64 + .5 - c[1]) + abs(z + .5 - c[2])
                    if best is None or dist < best[0]: best = (dist, blocks[ids[x, z, y]]['key'], int(metas[x, z, y]))
    return best[1:] if best else ('?', 0)
js, cs = load_js(d + '/jsmesh_0_0.bin'), load_cs(d + '/csmesh_0_0.bin')
for l in range(3):
    qj, qc = quads(*js[l]), quads(*cs[l])
    groups = collections.defaultdict(collections.Counter)
    for k in qj:
        if k not in qc: groups['only-ref'][owner(k[:4])] += 1
        else:
            a, bq = qj[k], qc[k]
            for p in a:
                if abs(a[p][0] - bq[p][0]) > .011: groups['shade'][owner(k[:4])] += 1; break
            for p in a:
                if abs(a[p][1] - bq[p][1]) > .011 or abs(a[p][2] - bq[p][2]) > .011: groups['light'][owner(k[:4])] += 1; break
            for p in a:
                if abs(a[p][3] - bq[p][3]) > 2e-4 or abs(a[p][4] - bq[p][4]) > 2e-4: groups['uv'][owner(k[:4])] += 1; break
    for k in qc:
        if k not in qj: groups['only-port'][owner(k[:4])] += 1
    for g, c in groups.items():
        print(['opaque', 'water', 'transparent'][l], g, dict(c.most_common(40)))
