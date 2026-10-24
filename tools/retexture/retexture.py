"""Re-draw the block atlas and mob skins embedded in a Maincraft HTML build.

usage: python3 tools/retexture/retexture.py <in.html> <out.html>

Every block texture keeps its palette, silhouette and large-scale structure (seams,
outlines, faces, eyes) while the fine detail is drawn anew: natural materials get a
fresh noise with the original's grain, ore specks are scattered anew, leaves get new
holes, log ends new rings, liquids new animated ripples, mobs a new fur / skin grain.
Items, crack overlays, sun and moon are left as they are. The output is deterministic.
"""
import sys, os, json, base64, tempfile, subprocess

here = os.path.dirname(os.path.abspath(__file__))
src, dst = sys.argv[1], sys.argv[2]
work = tempfile.mkdtemp(prefix='retexture-')
os.makedirs(work + '/orig')
line = next(l for l in open(src, encoding='utf-8') if l.startswith('const ASSETS = '))
d = json.loads(line[len('const ASSETS = '):].rstrip().rstrip(';'))
json.dump({k: d[k] for k in ('layers', 'anim', 'cols', 'count')}, open(work + '/orig/meta.json', 'w'))
open(work + '/orig/atlas.png', 'wb').write(base64.b64decode(d['atlas']))
for k, v in d['entities'].items():
    open(f'{work}/orig/ent_{k}.png', 'wb').write(base64.b64decode(v))
subprocess.check_call([sys.executable, here + '/main.py', work])
subprocess.check_call([sys.executable, here + '/assemble.py', work, src, dst])
