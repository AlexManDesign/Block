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


def fetch(name, kind='structure', ext='.nbt'):
    path = os.path.join(CACHE, (kind + '_' if kind != 'structure' else '') + name.replace('/', '_') + ext)
    if not os.path.exists(path):
        os.makedirs(CACHE, exist_ok=True)
        url = URL.replace('/structure/', '/' + kind + '/') + name + ext
        with urllib.request.urlopen(url) as r:
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
WOODS = {'oak': ('PLANKS', 'OAK_SLAB', 'OAK_STAIRS', 'OAK_FENCE', 'LOG', 'OAK_DOOR', 'OAK_TRAPDOOR', 'OAK_GATE', 'OAK_WOOD', 'PLANKS_PRESSURE_PLATE', 'PLANKS_BUTTON', 'LEAVES', 'OAK_SAPLING'),
         'spruce': ('SPRUCE_PLANKS', 'SPRUCE_SLAB', 'SPRUCE_STAIRS', 'SPRUCE_FENCE', 'SPRUCE_LOG', 'SPRUCE_DOOR', 'SPRUCE_TRAPDOOR', 'SPRUCE_GATE', 'SPRUCE_WOOD', 'SPRUCE_PLANKS_PRESSURE_PLATE', 'SPRUCE_PLANKS_BUTTON', 'SPRUCE_LEAVES', 'SPRUCE_SAPLING'),
         'birch': ('BIRCH_PLANKS', 'BIRCH_SLAB', 'BIRCH_STAIRS', 'BIRCH_FENCE', 'BIRCH_LOG', 'BIRCH_DOOR', 'BIRCH_TRAPDOOR', 'BIRCH_GATE', 'BIRCH_WOOD', 'BIRCH_PLANKS_PRESSURE_PLATE', 'BIRCH_PLANKS_BUTTON', 'BIRCH_LEAVES', 'BIRCH_SAPLING'),
         'jungle': ('JUNGLE_PLANKS', 'JUNGLE_PLANKS_SLAB', 'JUNGLE_PLANKS_STAIRS', 'JUNGLE_PLANKS_FENCE', 'JUNGLE_LOG', 'JUNGLE_DOOR', 'JUNGLE_TRAPDOOR', 'JUNGLE_PLANKS_GATE', 'JUNGLE_WOOD', 'JUNGLE_PLANKS_PRESSURE_PLATE', 'JUNGLE_PLANKS_BUTTON', 'JUNGLE_LEAVES', 'JUNGLE_SAPLING'),
         'dark_oak': ('DARK_PLANKS', 'DARK_PLANKS_SLAB', 'DARK_PLANKS_STAIRS', 'DARK_PLANKS_FENCE', 'DARK_LOG', 'DARK_OAK_DOOR', 'DARK_OAK_TRAPDOOR', 'DARK_PLANKS_GATE', 'DARK_OAK_WOOD', 'DARK_PLANKS_PRESSURE_PLATE', 'DARK_PLANKS_BUTTON', 'DARK_LEAVES', 'DARK_OAK_SAPLING'),
         'acacia': ('ACACIA_PLANKS', 'ACACIA_PLANKS_SLAB', 'ACACIA_PLANKS_STAIRS', 'ACACIA_PLANKS_FENCE', 'ACACIA_LOG', 'ACACIA_DOOR', 'ACACIA_TRAPDOOR', 'ACACIA_PLANKS_GATE', 'ACACIA_WOOD', 'ACACIA_PLANKS_PRESSURE_PLATE', 'ACACIA_PLANKS_BUTTON', 'ACACIA_LEAVES', 'ACACIA_SAPLING')}
WOOD_KIND = ['planks', 'slab', 'stairs', 'fence', 'log', 'door', 'trapdoor', 'fence_gate', 'wood', 'pressure_plate', 'button', 'leaves', 'sapling']
COLORS = ['white', 'orange', 'magenta', 'light_blue', 'yellow', 'lime', 'pink', 'gray', 'light_gray', 'cyan', 'purple', 'blue', 'brown', 'green', 'red', 'black']
SIMPLE = {'bricks': 'BRICK', 'chiseled_sandstone': 'CHISELED_SANDSTONE', 'chiseled_stone_bricks': 'CHISELED_STONE_BRICKS',
          'cobblestone': 'COBBLE', 'cobweb': 'COBWEB', 'cracked_stone_bricks': 'CRACKED_STONE_BRICKS', 'crafting_table': 'CRAFTING_TABLE',
          'cut_sandstone': 'CUT_SANDSTONE', 'gravel': 'GRAVEL', 'ice': 'ICE', 'iron_bars': 'IRON_BARS', 'magma_block': 'MAGMA_BLOCK',
          'mossy_cobblestone': 'MOSSY', 'mossy_stone_bricks': 'MOSSY_STONE_BRICKS', 'obsidian': 'OBSIDIAN', 'polished_andesite': 'POLISHED_ANDESITE',
          'polished_diorite': 'POLISHED_DIORITE', 'polished_granite': 'POLISHED_GRANITE', 'prismarine': 'PRISMARINE', 'sand': 'SAND',
          'sandstone': 'SANDSTONE', 'sea_lantern': 'SEA_LANTERN', 'snow_block': 'SNOW', 'stone': 'STONE', 'stone_bricks': 'STONE_BRICKS',
          'infested_chiseled_stone_bricks': 'CHISELED_STONE_BRICKS', 'infested_mossy_stone_bricks': 'MOSSY_STONE_BRICKS', 'infested_stone_bricks': 'STONE_BRICKS',
          'chest': 'CHEST', 'furnace': 'FURNACE', 'ladder': 'LADDER', 'redstone_torch': 'REDSTONE_TORCH', 'wall_torch': 'TORCH',
          'redstone_wall_torch': 'REDSTONE_TORCH', 'torch': 'TORCH', 'sandstone_stairs': 'SANDSTONE_STAIRS', 'stone_brick_stairs': 'STONEBRICK_STAIRS',
          'blue_ice': 'BLUE_ICE', 'packed_ice': 'PACKED_ICE', 'bookshelf': 'BOOKSHELF', 'cactus': 'CACTUS', 'clay': 'CLAY', 'dandelion': 'DANDELION',
          'dead_bush': 'DEAD_BUSH', 'diorite': 'DIORITE', 'granite': 'GRANITE', 'dirt': 'DIRT', 'dirt_path': 'DIRT_PATH', 'fern': 'FERN',
          'glass_pane': 'GLASS_PANE', 'grass_block': 'GRASS', 'hay_block': 'HAY_BLOCK', 'melon': 'MELON', 'oxeye_daisy': 'OXEYE_DAISY', 'poppy': 'POPPY',
          'pumpkin': 'PUMPKIN', 'short_grass': 'TALL_GRASS', 'grass': 'TALL_GRASS', 'smooth_sandstone': 'SMOOTH_SANDSTONE', 'smooth_stone': 'SMOOTH_STONE',
          'terracotta': 'TERRACOTTA', 'orange_terracotta': 'TERRA_ORANGE', 'white_terracotta': 'TERRA_WHITE', 'cobblestone_stairs': 'COBBLE_STAIRS',
          'cobblestone_slab': 'COBBLE_SLAB', 'cobblestone_wall': 'COBBLE_WALL', 'diorite_slab': 'DIORITE_SLAB', 'diorite_stairs': 'DIORITE_STAIRS',
          'diorite_wall': 'DIORITE_WALL', 'granite_stairs': 'GRANITE_STAIRS', 'granite_wall': 'GRANITE_WALL', 'sandstone_slab': 'SANDSTONE_SLAB',
          'sandstone_wall': 'SANDSTONE_WALL', 'smooth_sandstone_slab': 'SMOOTH_SANDSTONE_SLAB', 'smooth_sandstone_stairs': 'SMOOTH_SANDSTONE_STAIRS',
          'smooth_stone_slab': 'SMOOTH_STONE_SLAB', 'stone_button': 'STONE_BUTTON', 'stone_pressure_plate': 'STONE_PRESSURE_PLATE',
          'stripped_acacia_log': 'STRIPPED_ACACIA_LOG', 'stripped_oak_log': 'STRIPPED_OAK_LOG', 'stripped_oak_wood': 'STRIPPED_OAK_WOOD',
          'stripped_spruce_log': 'STRIPPED_SPRUCE_LOG', 'stripped_spruce_wood': 'STRIPPED_SPRUCE_WOOD', 'large_fern': 'LARGE_FERN', 'tall_grass': 'TALL_GRASS_PLANT',
          'water': 'WATER', 'lava': 'LAVA', 'sea_pickle': 'SEA_PICKLE', 'snow': 'SNOW_LAYER', 'farmland': 'FARMLAND', 'cave_air': 'AIR', 'air': 'AIR',
          'flower_pot': 'FLOWER_POT', 'tnt': 'TNT', 'carved_pumpkin': 'CARVED_PUMPKIN', 'jack_o_lantern': 'JACK_O_LANTERN'}
for c in COLORS:
    SIMPLE[c + '_carpet'] = c.upper() + '_CARPET'
    SIMPLE[c + '_stained_glass_pane'] = c.upper() + '_STAINED_GLASS_PANE'
    SIMPLE[c + '_wool'] = 'WOOL_' + c.upper()
    SIMPLE[c + '_glazed_terracotta'] = c.upper() + '_GLAZED'
    SIMPLE.setdefault(c + '_terracotta', c.upper() + '_TERRACOTTA')
    SIMPLE[c + '_bed'] = 'BED' if c == 'red' else 'BED_' + c.upper()
# blocks this game does not have yet: left out (Minecraft places them, we place nothing)
MISSING = {'oak_wall_sign', 'spruce_wall_sign', 'brown_wall_banner'}
MODEL_BLOCKS = {'barrel': 'BARREL', 'blast_furnace': 'BLAST_FURNACE', 'smoker': 'SMOKER', 'loom': 'LOOM', 'cartography_table': 'CARTOGRAPHY_TABLE',
                'fletching_table': 'FLETCHING_TABLE', 'smithing_table': 'SMITHING_TABLE', 'lectern': 'LECTERN', 'stonecutter': 'STONECUTTER',
                'grindstone': 'GRINDSTONE', 'bell': 'BELL', 'lantern': 'LANTERN', 'campfire': 'CAMPFIRE', 'brewing_stand': 'BREWING_STAND',
                'cauldron': 'CAULDRON', 'water_cauldron': 'CAULDRON', 'composter': 'COMPOSTER'}
KEYS = None   # block keys of this game (checked when set)


def parse_state(st):
    """'minecraft:oak_stairs[facing=east,half=bottom]' -> (name, props)"""
    if '[' not in st: return st, {}
    n, rest = st.split('[', 1)
    return n, dict(kv.split('=') for kv in rest.rstrip(']').split(',') if kv)


def convert(name, props):
    """Minecraft block state -> [key, meta, waterlogged] of this game, or None when it has no such block."""
    n = name.split(':')[1]
    pr = props or {}
    wet = 1 if pr.get('waterlogged') == 'true' else 0
    f = DIR.get(pr.get('facing'), 0)
    half_top = pr.get('half') == 'top'
    if n in MISSING or n.endswith('_banner') or n.endswith('_sign'): return None   # banners / signs: not in this game
    if n.startswith('potted_'): return ['FLOWER_POT', 0, 0]
    if n in MODEL_BLOCKS:
        # meta as models.js / blockdata.js encode them
        k = MODEL_BLOCKS[n]
        if n == 'barrel': m = {'south': 0, 'west': 1, 'north': 2, 'east': 3, 'up': 4, 'down': 5}[pr.get('facing', 'up')] | (8 if pr.get('open') == 'true' else 0)
        elif n in ('blast_furnace', 'smoker', 'loom', 'lectern', 'stonecutter'): m = f
        elif n == 'grindstone': m = f | ({'floor': 0, 'wall': 1, 'ceiling': 2}[pr.get('face', 'floor')] << 2)
        elif n == 'bell': m = f | ({'floor': 0, 'ceiling': 1, 'single_wall': 2, 'double_wall': 3}[pr.get('attachment', 'floor')] << 2)
        elif n == 'lantern': m = 1 if pr.get('hanging') == 'true' else 0
        elif n == 'campfire': m = f | (4 if pr.get('lit') == 'false' else 0)
        elif n == 'brewing_stand': m = sum(1 << i for i in range(3) if pr.get('has_bottle_%d' % i) == 'true')
        elif n == 'water_cauldron': m = int(pr.get('level', 3))
        elif n == 'composter': m = int(pr.get('level', 0))
        else: m = 0
        return [k, m, 0]
    if n == 'vine':   # bit per wall the vine hangs on (S W N E)
        return ['VINE', sum(1 << d for k, d in DIR.items() if pr.get(k) == 'true'), 0]
    if n == 'beetroots': return ['BEETROOTS_%d' % min(3, int(pr.get('age', 0))), 0, 0]
    key = None
    for w, keys in WOODS.items():
        if n.startswith(w + '_'):
            kind = n[len(w) + 1:]
            if kind in WOOD_KIND: key = keys[WOOD_KIND.index(kind)]; break
    if key is None: key = SIMPLE.get(n)
    if key is None and KEYS is not None:
        # same name in this game, or its naming of the stone families' stairs / slabs / walls
        u = n.upper()
        for c in (u, u.replace('MOSSY_COBBLESTONE', 'MOSSY'), u.replace('COBBLESTONE', 'COBBLE'), u.replace('STONE_BRICK_', 'STONEBRICK_'),
                  u.replace('STONE_BRICK_WALL', 'STONE_BRICKS_WALL'), u.replace('MOSSY_STONE_BRICK_', 'MOSSY_STONE_BRICKS_'), u.replace('BRICK_', 'BRICK_')):
            if c in KEYS: key = c; break
    for crop, k in (('wheat', 'WHEAT'), ('carrots', 'CARROTS'), ('potatoes', 'POTATOES'), ('melon_stem', 'MELON_STEM'), ('pumpkin_stem', 'PUMPKIN_STEM')):
        if n == crop:
            a = int(pr.get('age', 0)); mx = 7
            return ['%s_%d' % (k, 3 if a >= mx else min(2, a * 3 // mx)), 0, 0]
    if key is None: raise SystemExit('unmapped block ' + name + ' ' + json.dumps(pr))
    if KEYS is not None and key not in KEYS and key != 'AIR': raise SystemExit('no block ' + key + ' for ' + name)
    m = 0
    if n.endswith('_stairs'): m = f | (4 if half_top else 0)
    elif n.endswith('_slab'):
        t = pr.get('type', 'bottom')
        if t == 'double':
            base = {'COBBLE_SLAB': 'COBBLE', 'SANDSTONE_SLAB': 'SANDSTONE', 'SMOOTH_STONE_SLAB': 'SMOOTH_STONE', 'DIORITE_SLAB': 'DIORITE',
                    'SMOOTH_SANDSTONE_SLAB': 'SMOOTH_SANDSTONE'}.get(key)
            for w, keys in WOODS.items():
                if key == keys[1]: base = keys[0]
            return [base, 0, 0]
        m = 1 if t == 'top' else 0
    elif n.endswith('_door'): m = f | (4 if pr.get('half') == 'upper' else 0) | (8 if pr.get('open') == 'true' else 0) | (16 if pr.get('hinge') == 'right' else 0); wet = 0
    elif n.endswith('_trapdoor'): m = f | (4 if half_top else 0) | (8 if pr.get('open') == 'true' else 0)
    elif n.endswith('_fence_gate'): m = f | (4 if pr.get('open') == 'true' else 0)
    elif n.endswith('_button'): m = (f + 2) & 3
    elif n in ('chest', 'furnace', 'ladder', 'carved_pumpkin', 'jack_o_lantern'): m = f
    elif n.endswith('_bed'): m = f | (4 if pr.get('part') == 'head' else 0)
    elif n.endswith('wall_torch'): m = 1 + ((f + 2) & 3)
    elif 'axis' in pr: m = {'y': 0, 'x': 1, 'z': 2}[pr['axis']]
    elif n in ('large_fern', 'tall_grass'): m = 1 if pr.get('half') == 'upper' else 0
    elif n in ('water', 'lava'): m = int(pr.get('level', 0)) & 15
    elif n == 'snow': m = int(pr.get('layers', 1)) - 1
    elif n == 'sea_pickle': m = int(pr.get('pickles', 1)) - 1; wet = 0
    elif n == 'farmland' and int(pr.get('moisture', 0)) == 7: key = 'FARMLAND_MOIST'
    if n in ('water', 'lava', 'sea_pickle'): wet = 0
    return [key, m, wet]


def load_keys():
    global KEYS
    import re
    KEYS = set(re.findall(r'^\s*\["([A-Z_0-9]+)"', open(os.path.join(ROOT, 'src', 'blockdata.js')).read(), re.M))


def main():
    load_keys()
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
