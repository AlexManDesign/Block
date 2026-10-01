#!/usr/bin/env python3
"""Converts Minecraft block models (assets: blockstates + models with their parents) of the blocks
drawn as models (shape "model" in blockdata.js) into src/models.js.

For every such block and every meta value this game uses, the blockstate's variant / multipart parts
are resolved to [model, x rotation, y rotation]; models are flattened to their elements:
  [from, to, rotation [origin, axis 0 x/1 y/2 z, angle, rescale] or 0, faces[6] (FN order
   +x -x +y -y +z -z) each [texture name, u0, v0, u1, v1, uv rotation, cullface (FN index or -1), shade] or 0]
Also writes the block's selection box per meta (union of its elements).

    python3 tools/models.py
"""
import json, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import structures as S

ASSETS = 'https://raw.githubusercontent.com/misode/mcmeta/assets/assets/minecraft/'
FACES = {'east': 0, 'west': 1, 'up': 2, 'down': 3, 'south': 4, 'north': 5}
HDIR = ['south', 'west', 'north', 'east']


def afetch(path):
    local = os.path.join(S.CACHE, 'assets_' + path.replace('/', '_'))
    if not os.path.exists(local):
        import urllib.request
        os.makedirs(S.CACHE, exist_ok=True)
        with urllib.request.urlopen(ASSETS + path) as r: open(local, 'wb').write(r.read())
    return json.loads(open(local, 'rb').read())


def strip(n): return n.split(':', 1)[1] if ':' in n else n


# blocks drawn from models: key -> (blockstate name, metas, meta -> blockstate properties)
def bits_facing(m): return HDIR[m & 3]
BLOCKS = {
    'BARREL': ('barrel', range(12), lambda m: {'facing': (HDIR + ['up', 'down'])[m & 7] if (m & 7) < 6 else 'up', 'open': 'true' if m & 8 else 'false'}),
    'LECTERN': ('lectern', range(4), lambda m: {'facing': bits_facing(m), 'has_book': 'false', 'powered': 'false'}),
    'GRINDSTONE': ('grindstone', range(12), lambda m: {'facing': bits_facing(m), 'face': ['floor', 'wall', 'ceiling'][min(2, m >> 2)]}),
    'STONECUTTER': ('stonecutter', range(4), lambda m: {'facing': bits_facing(m)}),
    'BELL': ('bell', range(16), lambda m: {'facing': bits_facing(m), 'attachment': ['floor', 'ceiling', 'single_wall', 'double_wall'][m >> 2], 'powered': 'false'}),
    'LANTERN': ('lantern', range(2), lambda m: {'hanging': 'true' if m & 1 else 'false', 'waterlogged': 'false'}),
    'CAMPFIRE': ('campfire', range(8), lambda m: {'facing': bits_facing(m), 'lit': 'false' if m & 4 else 'true', 'signal_fire': 'false', 'waterlogged': 'false'}),
    'BREWING_STAND': ('brewing_stand', range(8), lambda m: {'has_bottle_0': str(bool(m & 1)).lower(), 'has_bottle_1': str(bool(m & 2)).lower(), 'has_bottle_2': str(bool(m & 4)).lower()}),
    'CAULDRON': (None, range(4), lambda m: {'level': str(m)}),
    'COMPOSTER': ('composter', range(9), lambda m: {'level': str(m)}),
}
# BellRenderer's bell (entity/bell/bell_body.png, 32x32): body 6x7x6 at (5..11, 6..13), lip 8x2x8 at (4..12, 4..6)
def bell_body():
    def cube(fx, fy, fz, w, h, d, u, v):
        # standard model box UV layout (texture 32 px -> model uv 16 units: halve)
        def r(a, b, c, e): return [a / 2, b / 2, c / 2, e / 2]
        faces = [0] * 6
        faces[FACES['north']] = ['entity/bell/bell_body'] + r(u + d, v + d, u + d + w, v + d + h) + [0, -1, 1]
        faces[FACES['south']] = ['entity/bell/bell_body'] + r(u + d + w + d, v + d, u + d + w + d + w, v + d + h) + [0, -1, 1]
        faces[FACES['west']] = ['entity/bell/bell_body'] + r(u, v + d, u + d, v + d + h) + [0, -1, 1]
        faces[FACES['east']] = ['entity/bell/bell_body'] + r(u + d + w, v + d, u + d + w + d, v + d + h) + [0, -1, 1]
        faces[FACES['up']] = ['entity/bell/bell_body'] + r(u + d, v, u + d + w, v + d) + [0, -1, 1]
        faces[FACES['down']] = ['entity/bell/bell_body'] + r(u + d + w, v, u + d + w + w, v + d) + [0, -1, 1]
        return [[fx, fy, fz], [fx + w, fy + h, fz + d], 0, faces]
    return [cube(5, 6, 5, 6, 7, 6, 0, 0), cube(4, 4, 4, 8, 2, 8, 0, 13)]


def resolve_model(name, cache):
    name = strip(name)
    if name in cache: return cache[name]
    d = afetch('models/' + name + '.json')
    tex, elements = {}, None
    chain = [d]
    while 'parent' in chain[-1]:
        p = strip(chain[-1]['parent'])
        if p in ('block/block', 'builtin/generated', 'item/generated'): break
        chain.append(afetch('models/' + p + '.json'))
    for m in reversed(chain):
        tex.update(m.get('textures', {}))
        if 'elements' in m: elements = m['elements']
    def tx(v):
        seen = 0
        while v.startswith('#') and seen < 10: v = tex.get(v[1:], v); seen += 1
        return strip(v)
    out = []
    for e in elements or []:
        fr, to = e['from'], e['to']
        rot = 0
        if 'rotation' in e:
            r = e['rotation']
            rot = [r['origin'], 'xyz'.index(r['axis']), r['angle'], 1 if r.get('rescale') else 0]
        faces = [0] * 6
        for fn, f in e['faces'].items():
            i = FACES[fn]
            uv = f.get('uv')
            if uv is None:
                uv = {'down': [fr[0], 16 - to[2], to[0], 16 - fr[2]], 'up': [fr[0], fr[2], to[0], to[2]],
                      'north': [16 - to[0], 16 - to[1], 16 - fr[0], 16 - fr[1]], 'south': [fr[0], 16 - to[1], to[0], 16 - fr[1]],
                      'west': [fr[2], 16 - to[1], to[2], 16 - fr[1]], 'east': [16 - to[2], 16 - to[1], 16 - fr[2], 16 - fr[1]]}[fn]
            faces[i] = [tx(f['texture'])] + [round(v, 4) for v in uv] + [f.get('rotation', 0), FACES.get(f.get('cullface'), -1), 0 if e.get('shade') is False else 1]
        out.append([[round(v, 4) for v in fr], [round(v, 4) for v in to], rot, faces])
    cache[name] = out
    return out


def parts_for(bs, props):
    """blockstate -> [(model, x, y, uvlock)] for these properties"""
    def match(cond):
        if 'OR' in cond: return any(match(c) for c in cond['OR'])
        if 'AND' in cond: return all(match(c) for c in cond['AND'])
        return all(props.get(k) in str(v).split('|') for k, v in cond.items())
    out = []
    if 'variants' in bs:
        for key, v in bs['variants'].items():
            conds = dict(kv.split('=') for kv in key.split(',') if kv)
            if all(props.get(k) == val for k, val in conds.items()):
                v = v[0] if isinstance(v, list) else v
                return [(v['model'], v.get('x', 0), v.get('y', 0))]
        raise SystemExit('no variant for ' + json.dumps(props))
    for part in bs['multipart']:
        if 'when' not in part or match(part['when']):
            v = part['apply']; v = v[0] if isinstance(v, list) else v
            out.append((v['model'], v.get('x', 0), v.get('y', 0)))
    return out


def main():
    cache, models, midx = {}, [], {}
    out_var, out_box = {}, {}

    def model_index(name):
        if name not in midx:
            midx[name] = len(models); models.append(resolve_model(name, cache) if name != '#bell' else bell_body())
        return midx[name]
    for key, (bsname, metas, fn) in BLOCKS.items():
        var, boxes = [], []
        for m in metas:
            props = fn(m)
            if key == 'CAULDRON':
                lv = int(props['level'])
                parts = [('block/cauldron', 0, 0)] if lv == 0 else parts_for(afetch('blockstates/water_cauldron.json'), {'level': str(lv)})
            else:
                parts = parts_for(afetch('blockstates/' + bsname + '.json'), props)
            if key == 'BELL':
                y = parts[0][2]
                parts.append(('#bell', 0, y))
            var.append([[model_index(p[0]), p[1], p[2]] for p in parts])
            # selection box: union of the elements, turned like the parts
            lo, hi = [16, 16, 16], [0, 0, 0]
            for mi, rx, ry in var[-1]:
                for e in models[mi]:
                    for c in range(8):
                        pt = [e[0][0] if c & 1 == 0 else e[1][0], e[0][1] if c & 2 == 0 else e[1][1], e[0][2] if c & 4 == 0 else e[1][2]]
                        for _ in range((rx // 90) % 4): pt = [pt[0], 16 - pt[2], pt[1]]      # x rotation (MC: about x, clockwise looking -x)
                        for _ in range((ry // 90) % 4): pt = [16 - pt[2], pt[1], pt[0]]      # y rotation (clockwise from above)
                        for k in range(3): lo[k] = min(lo[k], pt[k]); hi[k] = max(hi[k], pt[k])
            boxes.append([round(v / 16, 4) for v in lo + hi])
        out_var[key] = var; out_box[key] = boxes
    texs = sorted({f[0] for m in models for e in m for f in e[3] if f})
    js = ('\'use strict\';\n// Minecraft block models of the blocks drawn as models, converted by tools/models.py.\n'
          '// MODEL_LIST[i] = elements [from, to, rotation, faces (+x -x +y -y +z -z): [texture, u0, v0, u1, v1, uv rotation, cullface, shade]]\n'
          '// MODEL_VAR[key][meta] = parts [model, x rotation, y rotation]; MODEL_BOX[key][meta] = selection box\n'
          'const MODEL_LIST = ' + json.dumps(models, separators=(',', ':')) + ';\n'
          'const MODEL_VAR = ' + json.dumps(out_var, separators=(',', ':')) + ';\n'
          'const MODEL_BOX = ' + json.dumps(out_box, separators=(',', ':')) + ';\n')
    open(os.path.join(S.ROOT, 'src', 'models.js'), 'w').write(js)
    print('models', len(models), len(js) // 1024, 'KB; textures:', ' '.join(texs))


if __name__ == '__main__':
    main()
