"""Compares port MainIconPainter output with the reference main.js Hn() canvases.

Interior pixels (opaque in both) must match exactly (tolerance `tol` per channel). Edge pixels are
reported separately: the browser anti-aliases drawImage edges and, with its fast source-rect mode,
samples texels of the NEIGHBOURING atlas tile there; that depends on the browser/GPU rasteriser, so
the port only matches the silhouette (coverage) there, not the bled colour.
"""
import base64
import json
import sys

ref = json.load(open(sys.argv[1]))
port = json.load(open(sys.argv[2]))
tol = int(sys.argv[3]) if len(sys.argv) > 3 else 2
n_icons = len(ref['ids'])
interior = interior_bad = edge = edge_alpha_bad = shape_bad = exact_interior_icons = size_bad = 0
worst = []
for sid in ref['ids']:
    r = ref['icons'][str(sid)]
    p = port.get(str(sid))
    if p is None or p['s'] != r['s']:
        size_bad += 1
        worst.append((1.0, sid, 'size %s vs %s' % (r['s'], p and p['s'])))
        continue
    a = base64.b64decode(r['px']); b = base64.b64decode(p['px'])
    n = r['s'] * r['s']; bad = 0
    for i in range(n):
        ra, pa = a[i * 4 + 3], b[i * 4 + 3]
        if ra == 0 and pa == 0:
            continue
        if ra == 255 and pa == 255:
            interior += 1
            if any(abs(a[i * 4 + k] - b[i * 4 + k]) > tol for k in range(3)):
                interior_bad += 1; bad += 1
        elif (ra == 0) != (pa == 0) and max(ra, pa) >= 128:
            shape_bad += 1; bad += 1
        else:
            edge += 1
            if abs(ra - pa) > 40:
                edge_alpha_bad += 1
    if bad == 0:
        exact_interior_icons += 1
    else:
        worst.append((bad / n, sid, '%d px' % bad))
worst.sort(reverse=True)
for w in worst[:10]:
    print('  differs: id %s  %.2f%%  %s' % (w[1], w[0] * 100, w[2]))
print('icons: %d/%d identical interior + silhouette (size mismatches %d)' % (exact_interior_icons, n_icons, size_bad))
print('interior pixels differing: %d/%d (%.4f%%); silhouette pixels differing: %d; edge pixels %d, edge alpha off by >40: %d' % (
    interior_bad, interior, 100.0 * interior_bad / max(1, interior), shape_bad, edge, edge_alpha_bad))
