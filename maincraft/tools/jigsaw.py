#!/usr/bin/env python3
"""Converts Minecraft's jigsaw structures (villages, pillager outposts) into src/jigsaw.js.

Reads the vanilla data pack (misode/mcmeta "data" branch, cached in tools/.nbt_cache): the start
pools, every template pool reachable from them through jigsaw blocks, their templates and
processor lists. Output:
  JIG_POOLS[name] = {e: [[kind, template or feature, weight, projection, processors]], f: fallback}
      kind 0 template, 1 feature, 2 empty, 3 list (templates / processors as arrays); projection 0 rigid, 1 terrain matching
  JIG_TPL[name] = template as in structures.js plus
      j: jigsaw blocks [x, y, z, front, top, name, target, pool, final state palette index, rollable]
         (directions 0 south, 1 west, 2 north, 3 east, 4 up, 5 down; names index JIG_STR)
      r: {processor list: {state: [[probability, spot must hold (0 anything, 1 water, 2 ice), palette index or 0 for air], ...], rot: integrity}}
      l: chests with a loot table [x, y, z, table name]

    python3 tools/jigsaw.py
"""
import base64, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import structures as S

TYPES = ('plains', 'desert', 'savanna', 'snowy', 'taiga')
DIRS = {'south': 0, 'west': 1, 'north': 2, 'east': 3, 'up': 4, 'down': 5}
DOORS = ('_door',)


def jfetch(kind, name):
    path = os.path.join(S.CACHE, kind.replace('/', '_') + '_' + name.replace('/', '_') + '.json')
    if not os.path.exists(path):
        import urllib.request
        os.makedirs(S.CACHE, exist_ok=True)
        with urllib.request.urlopen('https://raw.githubusercontent.com/misode/mcmeta/data/data/minecraft/' + kind + '/' + name + '.json') as r:
            open(path, 'wb').write(r.read())
    return json.loads(open(path, 'rb').read())


def strip(n): return n.split(':', 1)[1] if ':' in n else n


def rule_matches(pred, name, props, pos_rand=False):
    t = pred['predicate_type']
    if t == 'minecraft:always_true': return True
    if t in ('minecraft:block_match', 'minecraft:random_block_match'): return name == pred['block']
    if t in ('minecraft:blockstate_match', 'minecraft:random_blockstate_match'):
        bs = pred['block_state']
        return name == (bs.get('Name') or bs.get('id')) and all(props.get(k) == v for k, v in (bs.get('Properties') or bs.get('properties') or {}).items())
    if t == 'minecraft:tag_match':
        if pred['tag'] == 'minecraft:doors': return name.endswith('_door')
        raise SystemExit('tag ' + pred['tag'])
    raise SystemExit('predicate ' + t)


def main():
    S.load_keys()
    strs, sidx = [], {}

    def si(v):
        if v not in sidx: sidx[v] = len(strs); strs.append(v)
        return sidx[v]
    pools, tpls, procs = {}, {}, {}
    queue = ['minecraft:village/%s/town_centers' % t for t in TYPES] + ['minecraft:pillager_outpost/base_plates']
    seen = set()
    while queue:
        pn = queue.pop()
        if pn in seen or pn == 'minecraft:empty': continue
        seen.add(pn)
        d = jfetch('worldgen/template_pool', strip(pn))
        out = []
        for e in d['elements']:
            el, w = e['element'], e['weight']
            et = el['element_type']
            if et == 'minecraft:empty_pool_element': out.append([2, '', w, 0, '']); continue
            proj = 1 if el.get('projection') == 'terrain_matching' else 0
            if et == 'minecraft:feature_pool_element': out.append([1, strip(el['feature']), w, proj, '']); continue
            if et == 'minecraft:list_pool_element':
                # ListPoolElement: its elements placed together (jigsaws of the first)
                locs, prs = [], []
                for sub in el['elements']:
                    if sub['element_type'] != 'minecraft:legacy_single_pool_element': raise SystemExit('list element ' + sub['element_type'])
                    spr = sub.get('processors')
                    if isinstance(spr, dict):
                        if spr.get('processors'): raise SystemExit('inline processors in ' + pn)
                        spr = ''
                    spr = strip(spr or '');  spr = '' if spr == 'empty' else spr
                    loc = strip(sub['location']); locs.append(loc); prs.append(spr)
                    tpls.setdefault(loc, set()).add(spr)
                    if spr and spr not in procs: procs[spr] = jfetch('worldgen/processor_list', spr)
                out.append([3, locs, w, proj, prs]); continue
            if et not in ('minecraft:legacy_single_pool_element', 'minecraft:single_pool_element'): raise SystemExit('element ' + et)
            pr = el.get('processors')
            if isinstance(pr, dict):
                if pr.get('processors'): raise SystemExit('inline processors in ' + pn)
                pr = ''
            pr = strip(pr or '')
            if pr == 'empty': pr = ''
            loc = strip(el['location'])
            out.append([0, loc, w, proj, pr])
            tpls.setdefault(loc, set()).add(pr)
            if pr and pr not in procs: procs[pr] = jfetch('worldgen/processor_list', pr)
            if et != 'minecraft:legacy_single_pool_element': raise SystemExit('non-legacy element (would place air) ' + loc)
        fb = d.get('fallback', 'minecraft:empty')
        pools[strip(pn)] = {'e': out, 'f': strip(fb)}
        if fb not in seen: queue.append(fb)
        # pools named by the jigsaw blocks of these templates
        for loc in list(tpls):
            n = S.read_nbt(S.fetch(loc))
            for b in n['blocks']:
                nb = b.get('nbt')
                if nb and nb.get('id') == 'minecraft:jigsaw' and nb.get('pool') and nb['pool'] not in seen: queue.append(nb['pool'])
    out_t = {}
    for loc, plist in sorted(tpls.items()):
        n = S.read_nbt(S.fetch(loc))
        sx, sy, sz = n['size']
        pal = n['palette']
        palette, idx_of = [None], {}

        def pidx(c):
            if c is None: return -1
            k = json.dumps(c)
            if k not in idx_of: idx_of[k] = len(palette); palette.append(c)
            return idx_of[k]
        states = []          # template state -> palette index (0 nothing / ignored air, -1 jigsaw)
        mc = []
        for e in pal:
            name = e.get('Name') or e.get('id'); props = e.get('Properties') or e.get('properties') or {}
            mc.append((name, props))
            if name == 'minecraft:jigsaw': states.append(-1); continue
            if name in ('minecraft:structure_block', 'minecraft:air', 'minecraft:structure_void'): states.append(0); continue   # STRUCTURE_AND_AIR ignored
            c = S.convert(name, props)
            states.append(0 if c is None else pidx(c))
        if len(pal) > 255: raise SystemExit('too many states ' + loc)
        g = bytearray(sx * sy * sz)
        jig, loot = [], []
        for b in n['blocks']:
            x, y, z = b['pos']
            nb = b.get('nbt') or {}
            if states[b['state']] < 0:
                o = mc[b['state']][1].get('orientation', 'north_up').split('_')
                fn, fp = S.parse_state(nb.get('final_state', 'minecraft:air'))
                fc = S.convert(fn, fp) if fn not in ('minecraft:air', 'minecraft:structure_void') else ['AIR', 0, 0]
                fi = pidx(fc) if fc is not None else 0
                jig.append([x, y, z, DIRS[o[0]], DIRS[o[1]], si(strip(nb.get('name', ''))), si(strip(nb.get('target', ''))), si(strip(nb.get('pool', 'minecraft:empty'))),
                            fi, 1 if nb.get('joint', 'rollable') == 'rollable' else 0])
                continue
            g[(y * sz + z) * sx + x] = b['state'] + 1
            if nb.get('LootTable'): loot.append([x, y, z, strip(nb['LootTable'])])
        rules = {}
        for pr in plist:
            if not pr: continue
            rm = {}
            for proc in procs[pr]['processors']:
                if proc['processor_type'] == 'minecraft:block_rot':
                    if proc.get('rottable_blocks'): raise SystemExit('rottable_blocks')
                    rm['rot'] = proc['integrity']; continue
                if proc['processor_type'] != 'minecraft:rule': raise SystemExit('processor ' + proc['processor_type'])
                for si_, (name, props) in enumerate(mc):
                    if states[si_] <= 0: continue
                    for r in proc['rules']:
                        ip = r['input_predicate']
                        if not rule_matches(ip, name, props): continue
                        lp = r['location_predicate']['predicate_type']
                        if lp not in ('minecraft:always_true', 'minecraft:block_match'): raise SystemExit('location ' + lp)
                        water = 0   # 1: the spot must hold water, 2: ice
                        if lp == 'minecraft:block_match':
                            water = {'minecraft:water': 1, 'minecraft:ice': 2}[r['location_predicate']['block']]
                        os_ = r['output_state']
                        on, op = (os_, {}) if isinstance(os_, str) else ((os_.get('Name') or os_.get('id')), (os_.get('Properties') or os_.get('properties') or {}))
                        oc = S.convert(on, op) if on != 'minecraft:air' else ['AIR', 0, 0]
                        prob = ip.get('probability', 1.0) if ip['predicate_type'].startswith('minecraft:random') else 1.0
                        rm.setdefault(str(si_), []).append([round(prob, 4), water, pidx(oc) if oc is not None else 0])
            rules[pr] = rm
        if len(palette) > 255: raise SystemExit('palette too big ' + loc)
        rle, i = bytearray(), 0
        while i < len(g):
            j = i
            while j < len(g) and g[j] == g[i] and j - i < 255: j += 1
            rle += bytes([g[i], j - i]); i = j
        out_t[loc] = {'s': [sx, sy, sz], 'p': palette[1:], 'm': [[max(0, v) for v in states]], 'g': base64.b64encode(bytes(rle)).decode(),
                      'd': [], 'j': jig, 'r': rules, 'l': loot}
    js = ('\'use strict\';\n// Minecraft jigsaw structures (vanilla data pack: template pools, templates, processor lists), converted by tools/jigsaw.py.\n'
          'const JIG_STR = ' + json.dumps(strs, separators=(',', ':')) + ';\n'
          'const JIG_POOLS = ' + json.dumps(pools, separators=(',', ':')) + ';\n'
          'const JIG_TPL = ' + json.dumps(out_t, separators=(',', ':')) + ';\n')
    open(os.path.join(S.ROOT, 'src', 'jigsaw.js'), 'w').write(js)
    print('pools', len(pools), 'templates', len(out_t), len(js) // 1024, 'KB')


if __name__ == '__main__':
    main()
