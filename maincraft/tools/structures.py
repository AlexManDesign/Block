#!/usr/bin/env python3
"""Converts Minecraft structure templates (.nbt from the vanilla data pack) into src/structures.js.

Templates are fetched from the misode/mcmeta "data" branch (cached in tools/.nbt_cache) and stored as
a palette of [block key, meta, waterlogged] in this game's encoding plus a dense base64 grid of
palette indices (0 = nothing placed: structure void, or air for templates placed with
STRUCTURE_AND_AIR ignored). Data markers ('chest', 'supply_chest', ...) are kept by position.

    python3 tools/structures.py
"""
import base64, gzip, json, os, struct, sys, urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(ROOT, 'tools', '.nbt_cache')
URL = 'https://raw.githubusercontent.com/misode/mcmeta/data/data/minecraft/structure/'


def names():
    out = [('igloo/top', True), ('igloo/middle', True), ('igloo/bottom', True)]
    for a in ('rightsideup', 'sideways', 'upsidedown'):
        for b in ('backhalf', 'fronthalf', 'full'):
            out += [('shipwreck/%s_%s' % (a, b), False), ('shipwreck/%s_%s_degraded' % (a, b), False)]
    out += [('shipwreck/with_mast', False), ('shipwreck/with_mast_degraded', False)]
    for k in ('brick', 'cracked', 'mossy', 'warm'):
        out += [('underwater_ruin/%s_%d' % (k, i), False) for i in range(1, 9)]
    for k in ('brick', 'cracked', 'mossy'):
        out += [('underwater_ruin/big_%s_%d' % (k, i), False) for i in range(1, 4)]
    out += [('underwater_ruin/big_warm_%d' % i, False) for i in range(4, 8)]
    return out   # (name, places air)


def fetch(name):
    path = os.path.join(CACHE, name.replace('/', '_') + '.nbt')
    if not os.path.exists(path):
        os.makedirs(CACHE, exist_ok=True)
        with urllib.request.urlopen(URL + name + '.nbt') as r:
            open(path, 'wb').write(r.read())
    return open(path, 'rb').read()


def read_nbt(raw):
    d = gzip.decompress(raw)
    p = [0]

    def u(fmt):
        v = struct.unpack_from('>' + fmt, d, p[0]); p[0] += struct.calcsize('>' + fmt); return v[0]

    def s():
        n = u('H'); v = d[p[0]:p[0] + n].decode('utf-8', 'replace'); p[0] += n; return v

    def pl(t):
        if t in (1, 2, 3, 4, 5, 6): return u('bhiqfd'[t - 1])
        if t == 7: n = u('i'); p[0] += n; return None
        if t == 8: return s()
        if t == 9:
            et = u('b'); n = u('i'); return [pl(et) for _ in range(n)]
        if t == 10:
            o = {}
            while True:
                tt = u('b')
                if tt == 0: return o
                k = s(); o[k] = pl(tt)
        if t == 11: n = u('i'); return [u('i') for _ in range(n)]
        if t == 12: n = u('i'); return [u('q') for _ in range(n)]
        raise ValueError('nbt tag %d' % t)
    t = u('b'); s(); return pl(t)


DIR = {'south': 0, 'west': 1, 'north': 2, 'east': 3}
WOOD = {'oak': ('PLANKS', 'OAK_SLAB', 'OAK_STAIRS', 'OAK_FENCE', 'LOG', 'OAK_DOOR', 'OAK_TRAPDOOR'),
        'spruce': ('SPRUCE_PLANKS', 'SPRUCE_SLAB', 'SPRUCE_STAIRS', 'SPRUCE_FENCE', 'SPRUCE_LOG', 'SPRUCE_DOOR', 'SPRUCE_TRAPDOOR'),
        'birch': ('BIRCH_PLANKS', 'BIRCH_SLAB', 'BIRCH_STAIRS', 'BIRCH_FENCE', 'BIRCH_LOG', 'BIRCH_DOOR', 'BIRCH_TRAPDOOR'),
        'jungle': ('JUNGLE_PLANKS', 'JUNGLE_PLANKS_SLAB', 'JUNGLE_PLANKS_STAIRS', 'JUNGLE_PLANKS_FENCE', 'JUNGLE_LOG', 'JUNGLE_DOOR', 'JUNGLE_TRAPDOOR'),
        'dark_oak': ('DARK_PLANKS', 'DARK_PLANKS_SLAB', 'DARK_PLANKS_STAIRS', 'DARK_PLANKS_FENCE', 'DARK_LOG', 'DARK_OAK_DOOR', 'DARK_OAK_TRAPDOOR'),
        'acacia': ('ACACIA_PLANKS', 'ACACIA_PLANKS_SLAB', 'ACACIA_PLANKS_STAIRS', 'ACACIA_PLANKS_FENCE', 'ACACIA_LOG', 'ACACIA_DOOR', 'ACACIA_TRAPDOOR')}
SIMPLE = {'bricks': 'BRICK', 'chiseled_sandstone': 'CHISELED_SANDSTONE', 'chiseled_stone_bricks': 'CHISELED_STONE_BRICKS',
          'cobblestone': 'COBBLE', 'cobweb': 'COBWEB', 'cracked_stone_bricks': 'CRACKED_STONE_BRICKS', 'crafting_table': 'CRAFTING_TABLE',
          'cut_sandstone': 'CUT_SANDSTONE', 'gravel': 'GRAVEL', 'ice': 'ICE', 'iron_bars': 'IRON_BARS', 'light_blue_terracotta': 'LIGHT_BLUE_TERRACOTTA',
          'light_gray_carpet': 'LIGHT_GRAY_CARPET', 'magma_block': 'MAGMA_BLOCK', 'mossy_cobblestone': 'MOSSY', 'mossy_stone_bricks': 'MOSSY_STONE_BRICKS',
          'obsidian': 'OBSIDIAN', 'polished_andesite': 'POLISHED_ANDESITE', 'polished_diorite': 'POLISHED_DIORITE', 'polished_granite': 'POLISHED_GRANITE',
          'potted_cactus': 'FLOWER_POT', 'prismarine': 'PRISMARINE', 'purple_glazed_terracotta': 'PURPLE_GLAZED', 'red_carpet': 'RED_CARPET',
          'sand': 'SAND', 'sandstone': 'SANDSTONE', 'sea_lantern': 'SEA_LANTERN', 'snow_block': 'SNOW', 'stone': 'STONE', 'stone_bricks': 'STONE_BRICKS',
          'white_carpet': 'WHITE_CARPET', 'infested_chiseled_stone_bricks': 'CHISELED_STONE_BRICKS', 'infested_mossy_stone_bricks': 'MOSSY_STONE_BRICKS',
          'infested_stone_bricks': 'STONE_BRICKS', 'chest': 'CHEST', 'furnace': 'FURNACE', 'ladder': 'LADDER', 'red_bed': 'BED',
          'redstone_torch': 'REDSTONE_TORCH', 'wall_torch': 'TORCH', 'redstone_wall_torch': 'REDSTONE_TORCH', 'torch': 'TORCH',
          'sandstone_stairs': 'SANDSTONE_STAIRS', 'stone_brick_stairs': 'STONEBRICK_STAIRS'}
# blocks this game does not have yet: left out (Minecraft places them, we place nothing)
MISSING = {'brewing_stand', 'water_cauldron', 'oak_wall_sign'}


def convert(name, props):
    n = name.split(':')[1]
    pr = props or {}
    wet = 1 if pr.get('waterlogged') == 'true' else 0
    f = DIR.get(pr.get('facing'), 0)
    if n == 'air': return ['AIR', 0, 0]
    if n in MISSING: return None
    for w, keys in WOOD.items():
        if n == w + '_planks': return [keys[0], 0, 0]
        if n == w + '_slab':
            t = pr.get('type', 'bottom')
            return [keys[0], 0, 0] if t == 'double' else [keys[1], 1 if t == 'top' else 0, wet]
        if n == w + '_stairs': return [keys[2], f | (4 if pr.get('half') == 'top' else 0), wet]
        if n == w + '_fence': return [keys[3], 0, wet]
        if n == w + '_log': return [keys[4], {'y': 0, 'x': 1, 'z': 2}[pr.get('axis', 'y')], 0]
        if n == w + '_door':
            return [keys[5], f | (4 if pr.get('half') == 'upper' else 0) | (8 if pr.get('open') == 'true' else 0) | (16 if pr.get('hinge') == 'right' else 0), 0]
        if n == w + '_trapdoor':
            return [keys[6], f | (4 if pr.get('half') == 'top' else 0) | (8 if pr.get('open') == 'true' else 0), wet]
    k = SIMPLE.get(n)
    if k is None: raise SystemExit('unmapped block ' + name + ' ' + json.dumps(pr))
    m = 0
    if n.endswith('_stairs'): m = f | (4 if pr.get('half') == 'top' else 0)
    elif n in ('chest', 'furnace', 'ladder'): m = f
    elif n == 'red_bed': m = f | (4 if pr.get('part') == 'head' else 0)
    elif n.endswith('wall_torch'): m = 1 + ((f + 2) & 3)
    return [k, m, wet if n in ('chest', 'ladder', 'iron_bars') or n.endswith('_stairs') else 0]


def main():
    out = {}
    for name, keep_air in names():
        n = read_nbt(fetch(name))
        sx, sy, sz = n['size']
        pals = n.get('palettes') or [n['palette']]
        idx_of = {}
        palette = [None]                     # 0: nothing
        pal_maps = []
        for pal in pals:
            m = []
            for e in pal:
                bn = e.get('Name') or e.get('id')
                if bn == 'minecraft:structure_block':
                    m.append(-1); continue
                c = convert(bn, e.get('Properties') or e.get('properties'))
                if c is None or (c[0] == 'AIR' and not keep_air):
                    m.append(0); continue
                key = json.dumps(c)
                if key not in idx_of:
                    idx_of[key] = len(palette); palette.append(c)
                m.append(idx_of[key])
            pal_maps.append(m)
        if len(palette) > 255: raise SystemExit('palette too big ' + name)
        if len(pals[0]) > 255: raise SystemExit('too many states ' + name)
        g, marks = bytearray(sx * sy * sz), []
        for b in n['blocks']:
            x, y, z = b['pos']
            if pal_maps[0][b['state']] < 0:
                marks.append([x, y, z, b['nbt'].get('metadata', '')]); continue
            g[(y * sz + z) * sx + x] = b['state'] + 1
        # run-length (value, count < 256) pairs: templates are mostly empty space
        rle, i = bytearray(), 0
        while i < len(g):
            j = i
            while j < len(g) and g[j] == g[i] and j - i < 255: j += 1
            rle += bytes([g[i], j - i]); i = j
        out[name] = {'s': [sx, sy, sz], 'p': palette[1:], 'm': [[max(0, v) for v in m] for m in pal_maps],
                     'g': base64.b64encode(bytes(rle)).decode(), 'd': marks}
    js = ('\'use strict\';\n// Minecraft structure templates (vanilla data pack), converted by tools/structures.py.\n'
          '// s: size, p: blocks [key, meta, waterlogged], m: per palette, template state -> p index + 1 (0 = nothing),\n'
          '// g: base64 run-length pairs (state + 1, count) of the grid (y * sz + z) * sx + x, d: data markers [x, y, z, name].\n'
          'const STRUCT_TPL = ' + json.dumps(out, separators=(',', ':')) + ';\n')
    open(os.path.join(ROOT, 'src', 'structures.js'), 'w').write(js)
    print('wrote', len(out), 'templates', len(js) // 1024, 'KB')


if __name__ == '__main__':
    main()
