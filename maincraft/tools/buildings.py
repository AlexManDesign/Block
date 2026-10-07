#!/usr/bin/env python3
"""Own building designs for the game's structures, written to src/structures.js and src/jigsaw.js.

Every building here is designed in this file (code that lays out blocks); nothing is taken from
Minecraft's data. The output formats are the ones worldgen.js reads:
  structures.js  STRUCT_TPL[name] = {s, p, m, g, d}: igloo, shipwrecks, ocean ruins
  jigsaw.js      JIG_STR, JIG_POOLS, JIG_TPL: villages (5 styles) and pillager outposts
Template: s size, p palette [block key, meta, waterlogged], m per palette: state -> p index + 1
(0 nothing), g base64 run-length pairs (state + 1, count) of the grid (y * sz + z) * sx + x,
d data markers [x, y, z, name], j jigsaws [x, y, z, front, top, name, target, pool, final p index + 1,
rollable] (directions 0 south +z, 1 west -x, 2 north -z, 3 east +x, 4 up, 5 down; strings index
JIG_STR), r processor rules {list: {state: [[probability, spot (0 any, 1 water, 2 ice), p index + 1 or 0 air]]}},
l loot chests [x, y, z, table].

    python3 tools/buildings.py
"""
import base64, json, math, os, random, zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S, W, N, E, UP, DOWN = 0, 1, 2, 3, 4, 5


class Tpl:
    """a block grid with markers, jigsaws and loot chests"""
    def __init__(self, sx, sy, sz):
        self.sx, self.sy, self.sz = sx, sy, sz
        self.b = {}              # (x, y, z) -> (key, meta, wet)
        self.marks, self.jigs, self.loot = [], [], []

    def set(self, x, y, z, key, m=0, wet=0):
        if 0 <= x < self.sx and 0 <= y < self.sy and 0 <= z < self.sz:
            self.b[(x, y, z)] = (key, m, wet)

    def get(self, x, y, z):
        return self.b.get((x, y, z))

    def fill(self, x0, y0, z0, x1, y1, z1, key, m=0, hollow=False):
        for y in range(y0, y1 + 1):
            for z in range(z0, z1 + 1):
                for x in range(x0, x1 + 1):
                    if hollow and x0 < x < x1 and z0 < z < z1 and y0 < y < y1: continue
                    self.set(x, y, z, key, m)

    def clear(self, x0, y0, z0, x1, y1, z1):
        self.fill(x0, y0, z0, x1, y1, z1, 'AIR')

    def chest(self, x, y, z, facing, table):
        self.set(x, y, z, 'CHEST', facing)
        self.loot.append([x, y, z, table])

    def jig(self, x, y, z, front, top, name, target, pool, final=('AIR', 0, 0), roll=False):
        self.jigs.append((x, y, z, front, top, name, target, pool, final, roll))


def encode(t, palettes=None, keep_air=True):
    """palettes: list of {placeholder key: real key} (several wood types for one design)"""
    states, sidx = [], {}
    for v in t.b.values():
        if v[0] == 'AIR' and not keep_air: continue
        if v not in sidx: sidx[v] = len(states); states.append(v)
    pal_list, pidx = [], {}

    def pid(e):
        k = json.dumps(e)
        if k not in pidx: pidx[k] = len(pal_list) + 1; pal_list.append(e)
        return pidx[k]
    maps = []
    for P in (palettes or [{}]):
        maps.append([pid([P.get(s[0], s[0]), s[1], s[2]]) for s in states])
    g = bytearray(t.sx * t.sy * t.sz)
    for (x, y, z), v in t.b.items():
        if v in sidx: g[(y * t.sz + z) * t.sx + x] = sidx[v] + 1
    rle, i = bytearray(), 0
    while i < len(g):
        j = i
        while j < len(g) and g[j] == g[i] and j - i < 255: j += 1
        rle += bytes([g[i], j - i]); i = j
    if len(states) > 254 or len(pal_list) > 254: raise SystemExit('palette too big')
    return {'s': [t.sx, t.sy, t.sz], 'p': pal_list, 'm': maps, 'g': base64.b64encode(bytes(rle)).decode(), 'd': t.marks}, pid


# ====================================================================== igloo
def igloo():
    out = {}
    # top: a snow dome (7 x 5 x 8) over an ellipsoid room, entrance tunnel at z 0..2, the trapdoor at (3, 0, 5)
    t = Tpl(7, 5, 8)
    cx, cz = 3, 5
    dome = lambda x, y, z: 0 <= x < 7 and 0 <= z < 8 and ((x - cx) / 3.5) ** 2 + ((z - cz) / 3.5) ** 2 + (y / 4.4) ** 2 <= 1
    room = lambda x, y, z: ((x - cx) / 2.6) ** 2 + ((z - cz) / 2.2) ** 2 + (y / 3.4) ** 2 < 1
    for y in range(5):
        for z in range(8):
            for x in range(7):
                if not dome(x, y, z): continue
                # air only where the whole neighbourhood is still under the dome (no holes in the shell)
                inside = y > 0 and room(x, y, z) and all(dome(x + a, y + b, z + c) for a, b, c in ((1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, 1, 0)))
                t.set(x, y, z, 'AIR' if inside else 'SNOW')
    t.fill(2, 0, 0, 4, 3, 2, 'SNOW')
    t.clear(3, 1, 0, 3, 2, 3)
    t.set(0, 1, 5, 'ICE'); t.set(6, 1, 5, 'ICE')                       # windows
    t.set(cx, 0, cz, 'OAK_TRAPDOOR', N | 4)                           # over the shaft (top half)
    t.set(1, 1, 4, 'BED', S); t.set(1, 1, 5, 'BED', S | 4)
    t.set(2, 1, 6, 'WHITE_CARPET'); t.set(1, 1, 6, 'LIGHT_GRAY_CARPET')
    t.set(5, 1, 6, 'CRAFTING_TABLE'); t.set(5, 1, 4, 'FURNACE', W)
    t.set(5, 1, 5, 'TORCH', 0)
    out['igloo/top'] = encode(t)[0]
    # middle: a 3 x 3 x 3 shaft of stone bricks with a ladder in the middle (on its north wall)
    t = Tpl(3, 3, 3)
    for y in range(3):
        t.fill(0, y, 0, 2, y, 2, 'STONE_BRICKS', hollow=False)
        t.set(1, y, 1, 'LADDER', N)
    t.set(1, 1, 0, 'MOSSY_STONE_BRICKS')
    out['igloo/middle'] = encode(t)[0]
    # bottom: the basement (7 x 6 x 9), the shaft comes down at (3, 5, 7)
    t = Tpl(7, 6, 9)
    t.fill(0, 0, 0, 6, 5, 8, 'STONE_BRICKS')
    t.clear(1, 1, 1, 5, 3, 7)
    for (x, z) in ((1, 1), (5, 1), (1, 7), (5, 7)): t.set(x, 0, z, 'MOSSY_STONE_BRICKS')
    t.fill(1, 0, 2, 5, 0, 6, 'POLISHED_ANDESITE')
    for y in range(1, 6): t.set(3, y, 7, 'LADDER', N)
    t.set(1, 1, 1, 'BREWING_STAND', 0); t.set(5, 1, 1, 'CAULDRON', 3)
    t.set(5, 1, 3, 'CHEST', W); t.marks.append([5, 2, 3, 'chest'])
    t.set(1, 1, 5, 'BOOKSHELF'); t.set(1, 2, 5, 'BOOKSHELF'); t.set(1, 1, 4, 'CRAFTING_TABLE')
    t.set(2, 3, 1, 'TORCH', 1 + N); t.set(4, 3, 1, 'TORCH', 1 + N)
    t.fill(2, 1, 1, 4, 1, 1, 'IRON_BARS')
    t.set(3, 2, 1, 'IRON_BARS')
    out['igloo/bottom'] = encode(t)[0]
    return out


# ====================================================================== shipwrecks
WOODS = [{'@P': 'PLANKS', '@S': 'OAK_STAIRS', '@L': 'LOG', '@F': 'OAK_FENCE', '@T': 'OAK_TRAPDOOR', '@D': 'OAK_DOOR', '@H': 'OAK_SLAB'},
         {'@P': 'SPRUCE_PLANKS', '@S': 'SPRUCE_STAIRS', '@L': 'SPRUCE_LOG', '@F': 'SPRUCE_FENCE', '@T': 'SPRUCE_TRAPDOOR', '@D': 'SPRUCE_DOOR', '@H': 'SPRUCE_SLAB'},
         {'@P': 'DARK_PLANKS', '@S': 'DARK_PLANKS_STAIRS', '@L': 'DARK_LOG', '@F': 'DARK_PLANKS_FENCE', '@T': 'DARK_OAK_TRAPDOOR', '@D': 'DARK_OAK_DOOR', '@H': 'DARK_PLANKS_SLAB'},
         {'@P': 'BIRCH_PLANKS', '@S': 'BIRCH_STAIRS', '@L': 'BIRCH_LOG', '@F': 'BIRCH_FENCE', '@T': 'BIRCH_TRAPDOOR', '@D': 'BIRCH_DOOR', '@H': 'BIRCH_SLAB'},
         {'@P': 'JUNGLE_PLANKS', '@S': 'JUNGLE_PLANKS_STAIRS', '@L': 'JUNGLE_LOG', '@F': 'JUNGLE_PLANKS_FENCE', '@T': 'JUNGLE_TRAPDOOR', '@D': 'JUNGLE_DOOR', '@H': 'JUNGLE_PLANKS_SLAB'},
         {'@P': 'ACACIA_PLANKS', '@S': 'ACACIA_PLANKS_STAIRS', '@L': 'ACACIA_LOG', '@F': 'ACACIA_PLANKS_FENCE', '@T': 'ACACIA_TRAPDOOR', '@D': 'ACACIA_DOOR', '@H': 'ACACIA_PLANKS_SLAB'}]


def ship_hull(length, mast):
    """an upright hull 9 wide along z (bow at z = length - 1), deck at y 4; returns the template and chest spots"""
    t = Tpl(9, 21 if mast else 9, length)
    for z in range(length):
        f = z / (length - 1)
        # half width at the deck: a square stern, the full beam amidships, a pointed bow
        hw = 3 if f < 0.12 else 4 if f < 0.68 else max(0, int(round(4 * (1 - (f - 0.68) / 0.32))))
        keel = 0 if f < 0.86 else int((f - 0.86) / 0.14 * 3 + 0.5)     # the bow's bottom curves up
        for y in range(keel, 5):
            w = max(0, hw - max(0, 2 - y))                              # rounded bilge: narrower low down
            for x in range(4 - w, 4 + w + 1):
                shell = x in (4 - w, 4 + w) or y == keel or z == 0 or y == 4
                t.set(x, y, z, '@P' if shell else 'AIR')
        if hw > 0:
            t.set(4 - hw, 5, z, '@F'); t.set(4 + hw, 5, z, '@F')     # railing
        t.set(4, keel, z, '@L', 2)                                       # keel
    for y in range(3, 7): t.set(4, y, length - 1, '@L', 2 if y < 5 else 0)   # stem post
    # stern cabin with a door to the deck and two windows
    for z in range(1, 6):
        for x in range(1, 8):
            for y in range(5, 8):
                edge = x in (1, 7) or z in (1, 5)
                t.set(x, y, z, '@P' if edge else 'AIR')
        t.set(1, 8, z, '@S', E); t.set(7, 8, z, '@S', W)
        for x in range(2, 7): t.set(x, 8, z, '@H', 0)
    t.set(4, 5, 5, '@D', N); t.set(4, 6, 5, '@D', N | 4)
    t.set(2, 6, 1, 'GLASS_PANE'); t.set(6, 6, 1, 'GLASS_PANE')
    t.set(3, 5, 2, 'CRAFTING_TABLE')
    # a hatch to the hold right above a ladder down (an open trapdoor over a ladder climbs like one)
    hz = length // 2 - 4
    t.set(4, 4, hz + 1, '@T', N)
    for y in range(1, 4): t.set(4, y, hz + 1, 'LADDER', N)
    t.set(4, 1, hz + 2, '@P'); t.set(4, 2, hz + 2, '@P'); t.set(4, 3, hz + 2, '@P')
    if mast:
        mz = length // 2
        for y in range(1, 20): t.set(4, y, mz, '@L')
        for x in range(1, 8): t.set(x, 15, mz, '@L', 1)
        for x in range(2, 7):
            for y in range(9, 15):
                billow = 3 <= x <= 5 and 10 <= y <= 13                  # the sail bulges forward in the middle
                t.set(x, y, mz + (2 if billow else 1), 'WOOL_WHITE')
        for x in range(3, 6): t.set(x, 20, mz, 'WOOL_RED')                # pennant
    spots = {'supply': (4, 1, length - 7), 'map': (5, 5, 3), 'treasure': (4, 1, 4)}
    return t, spots


def transform(t, kind, half):
    """rightsideup / sideways (on its side) / upsidedown; fronthalf / backhalf / full"""
    L = t.sz
    z0, z1 = (0, L - 1) if half == 'full' else ((L // 2 - 2, L - 1) if half == 'fronthalf' else (0, L // 2 + 1))
    out = Tpl(t.sx if kind != 'sideways' else 9, 9 if kind != 'rightsideup' else t.sy, z1 - z0 + 1)
    if kind == 'rightsideup' and t.sy > 9: out.sy = t.sy
    for (x, y, z), v in t.b.items():
        if not z0 <= z <= z1: continue
        nz = z - z0
        if kind == 'rightsideup': out.set(x, y, nz, *v)
        elif kind == 'upsidedown':
            if y < 9: out.set(x, 8 - y, nz, v[0], v[1] ^ 4 if 'STAIRS' in v[0] or v[0] == '@S' else (1 - v[1] if v[0] == '@H' else v[1]), v[2])
        else:
            if y < 9: out.set(8 - y, x, nz, v[0], 1 if v[0] == '@L' else v[1], v[2])
    return out, z0


def shipwrecks():
    out = {}
    for kind in ('rightsideup', 'sideways', 'upsidedown'):
        for half in ('backhalf', 'fronthalf', 'full'):
            for deg in (False, True):
                L = 28
                hull, spots = ship_hull(L, False)
                t, z0 = transform(hull, kind, half)
                r = random.Random(zlib.crc32(('%s %s %s' % (kind, half, deg)).encode()))   # the same on every run
                if deg:
                    for k in list(t.b):
                        if r.random() < 0.12: del t.b[k]
                add_ship_markers(t, spots, kind, z0)
                out['shipwreck/%s_%s%s' % (kind, half, '_degraded' if deg else '')] = encode(t, WOODS, keep_air=False)[0]
    for deg in (False, True):
        t, spots = ship_hull(28, True)
        r = random.Random(77 if deg else 78)
        if deg:
            for k in list(t.b):
                if r.random() < 0.12: del t.b[k]
        add_ship_markers(t, spots, 'rightsideup', 0)
        out['shipwreck/with_mast' + ('_degraded' if deg else '')] = encode(t, WOODS, keep_air=False)[0]
    return out


def add_ship_markers(t, spots, kind, z0):
    for name, (x, y, z) in spots.items():
        z -= z0
        if not 0 <= z < t.sz: continue
        if kind == 'upsidedown': y = 8 - y - 1 if y < 8 else y
        elif kind == 'sideways': x, y = 8 - y, x
        if not (0 <= x < t.sx and 0 <= y + 1 < t.sy): continue
        t.set(x, y, z, 'CHEST', N)
        t.marks.append([x, y + 1, z, name + '_chest'])
        t.b.pop((x, y + 1, z), None)


# ====================================================================== ocean ruins
def ruins():
    out = {}
    for style, mat in (('brick', 'STONE_BRICKS'), ('cracked', 'CRACKED_STONE_BRICKS'), ('mossy', 'MOSSY_STONE_BRICKS'), ('warm', 'SANDSTONE')):
        for i in range(1, 9):
            r = random.Random(i * 31)                       # the same layout for brick / cracked / mossy of one number
            t = Tpl(6, 7, 7)
            t.fill(0, 0, 0, 5, 0, 6, mat if style != 'warm' else 'SMOOTH_SANDSTONE')
            for (x, z) in ((0, 0), (5, 0), (0, 6), (5, 6)):
                for y in range(1, 2 + r.randint(1, 5)): t.set(x, y, z, mat if style != 'warm' else 'CUT_SANDSTONE')
            for x in range(6):
                for y in range(1, r.randint(1, 4)): t.set(x, y, 0, mat)
            if i % 2:
                for z in range(7):
                    for y in range(1, r.randint(1, 3)): t.set(0, y, z, mat)
            if i % 3 == 0:
                t.fill(1, 4, 0, 4, 4, 0, (mat if style != 'warm' else 'CHISELED_SANDSTONE'))
            t.set(3, 1, 3, 'AIR')
            t.marks.append([3, 1, 3, 'chest'])
            if i % 2 == 0: t.marks.append([2, 1, 4, 'drowned'])
            out['underwater_ruin/%s_%d' % (style, i)] = encode(t, keep_air=False)[0]
    bigs = [('brick', 'STONE_BRICKS', range(1, 4)), ('cracked', 'CRACKED_STONE_BRICKS', range(1, 4)), ('mossy', 'MOSSY_STONE_BRICKS', range(1, 4)), ('warm', 'SANDSTONE', range(4, 8))]
    for style, mat, nums in bigs:
        for i in nums:
            r = random.Random(i * 57)
            t = Tpl(16, 9, 16)
            t.fill(0, 0, 0, 15, 0, 15, mat)
            # a walled court with a ruined hall in the middle
            for k in range(16):
                for side in ((k, 0), (k, 15), (0, k), (15, k)):
                    for y in range(1, 1 + r.randint(0, 4)): t.set(side[0], y, side[1], mat)
            hall = (4, 4, 11, 11)
            for x in range(hall[0], hall[2] + 1):
                for z in range(hall[1], hall[3] + 1):
                    if x in hall[::2] or z in hall[1::2]:
                        for y in range(1, 2 + r.randint(1, 6)): t.set(x, y, z, mat)
            for x in range(hall[0], hall[2] + 1, 2):
                if r.random() < 0.6: t.set(x, 7, hall[1], mat if style != 'warm' else 'CHISELED_SANDSTONE')
            t.marks.append([8, 1, 8, 'chest'])
            t.marks.append([6, 1, 9, 'drowned']); t.marks.append([10, 1, 6, 'drowned'])
            out['underwater_ruin/big_%s_%d' % (style, i)] = encode(t, keep_air=False)[0]
    return out


# ====================================================================== villages
STYLES = {
    'plains': dict(planks='PLANKS', log='LOG', stairs='OAK_STAIRS', slab='OAK_SLAB', fence='OAK_FENCE', door='OAK_DOOR', found='COBBLE',
                   wall='PLANKS', roof='OAK_STAIRS', path='DIRT_PATH', glass='GLASS_PANE', flat=False, trees=['oak'], piles=['pile_hay'],
                   patch='flower_plain', bed='BED', light='TORCH'),
    'desert': dict(planks='SMOOTH_SANDSTONE', log='CUT_SANDSTONE', stairs='SMOOTH_SANDSTONE_STAIRS', slab='SMOOTH_SANDSTONE_SLAB', fence='SANDSTONE_WALL',
                   door='JUNGLE_DOOR', found='SANDSTONE', wall='SMOOTH_SANDSTONE', roof=None, path='SMOOTH_SANDSTONE', glass='AIR', flat=True,
                   trees=[], piles=['pile_hay'], patch='patch_cactus', bed='BED_YELLOW', light='TORCH'),
    'savanna': dict(planks='ACACIA_PLANKS', log='ACACIA_LOG', stairs='ACACIA_PLANKS_STAIRS', slab='ACACIA_PLANKS_SLAB', fence='ACACIA_PLANKS_FENCE',
                    door='ACACIA_DOOR', found='TERRACOTTA', wall='ACACIA_PLANKS', roof='ACACIA_PLANKS_STAIRS', path='DIRT_PATH', glass='GLASS_PANE',
                    flat=True, trees=['acacia'], piles=['pile_hay', 'pile_melon'], patch='flower_plain', bed='BED_ORANGE', light='TORCH'),
    'snowy': dict(planks='SPRUCE_PLANKS', log='STRIPPED_SPRUCE_LOG', stairs='SPRUCE_STAIRS', slab='SPRUCE_SLAB', fence='SPRUCE_FENCE', door='SPRUCE_DOOR',
                  found='SNOW', wall='SNOW', roof='SPRUCE_STAIRS', path='DIRT_PATH', glass='GLASS_PANE', flat=False, trees=['spruce'],
                  piles=['pile_snow', 'pile_ice'], patch='flower_plain', bed='BED_BLUE', light='LANTERN'),
    'taiga': dict(planks='SPRUCE_PLANKS', log='SPRUCE_LOG', stairs='SPRUCE_STAIRS', slab='SPRUCE_SLAB', fence='SPRUCE_FENCE', door='SPRUCE_DOOR',
                  found='COBBLE', wall='SPRUCE_PLANKS', roof='SPRUCE_STAIRS', path='DIRT_PATH', glass='GLASS_PANE', flat=False,
                  trees=['spruce', 'pine'], piles=['pile_pumpkin'], patch='patch_taiga_grass', bed='BED_GREEN', light='LANTERN'),
}
JOBS = [('SMOKER', 'butcher'), ('BLAST_FURNACE', 'armorer'), ('CARTOGRAPHY_TABLE', 'cartographer'), ('FLETCHING_TABLE', 'fletcher'),
        ('SMITHING_TABLE', 'toolsmith'), ('LOOM', 'shepherd'), ('LECTERN', 'temple'), ('STONECUTTER', 'mason'), ('GRINDSTONE', 'weaponsmith'),
        ('COMPOSTER', 'plains_house'), ('BARREL', 'fisher'), ('CAULDRON', 'tannery'), ('BREWING_STAND', 'temple')]


def house(st, w, d, h, job, seed, style):
    """a house w x d (outer, odd w), door in the middle of the north side at z 0; jigsaw in front of it"""
    r = random.Random(seed)
    roof_h = 0 if st['flat'] else (w + 1) // 2
    t = Tpl(w, h + roof_h + 2, d + 1)
    zz = 1                                    # the building starts one row behind the doorstep
    t.fill(0, 0, zz, w - 1, 0, zz + d - 1, st['found'])
    for y in range(1, h + 1):
        for z in range(zz, zz + d):
            for x in range(w):
                edge = x in (0, w - 1) or z in (zz, zz + d - 1)
                corner = x in (0, w - 1) and z in (zz, zz + d - 1)
                t.set(x, y, z, (st['log'] if corner else st['wall']) if edge else 'AIR')
    # windows
    for y in (2,):
        for x in range(2, w - 2, 2):
            if x != w // 2: t.set(x, y, zz, st['glass']); t.set(x, y, zz + d - 1, st['glass'])
        for z in range(zz + 2, zz + d - 2, 2): t.set(0, y, z, st['glass']); t.set(w - 1, y, z, st['glass'])
    # door
    dx = w // 2
    t.set(dx, 1, zz, st['door'], N); t.set(dx, 2, zz, st['door'], N | 4)
    if st['light'] == 'TORCH': t.set(dx + 1, 2, zz - 1, 'TORCH', 1 + S) if dx + 1 < w - 1 else None
    else: t.set(dx + 1, h + 1, zz - 1, 'LANTERN', 0) if dx + 1 < w else None
    # roof
    top = h + 1
    if st['flat']:
        t.fill(0, top, zz, w - 1, top, zz + d - 1, st['planks'])
        for x in range(w):
            t.set(x, top + 1, zz, st['slab']); t.set(x, top + 1, zz + d - 1, st['slab'])
        for z in range(zz, zz + d):
            t.set(0, top + 1, z, st['slab']); t.set(w - 1, top + 1, z, st['slab'])
    else:
        for i in range(roof_h):
            y = top + i
            for z in range(zz - 1, zz + d + 1):
                if 0 <= z <= zz + d:
                    t.set(i, y, z, st['roof'], E); t.set(w - 1 - i, y, z, st['roof'], W)
            for z in (zz, zz + d - 1):
                for x in range(i + 1, w - 1 - i): t.set(x, y, z, st['planks'] if st['wall'] != 'SNOW' else 'SPRUCE_PLANKS')
        if w % 2:
            for z in range(zz - 1, zz + d + 1):
                if 0 <= z <= zz + d: t.set(w // 2, top + roof_h - (1 if (w + 1) // 2 > roof_h else 0), z, st['slab'])
    # inside: bed, light, job block, chest
    t.set(1, 1, zz + d - 2, st['bed'], W | 4); t.set(2, 1, zz + d - 2, st['bed'], W) if w > 5 else None
    if job:
        t.set(w - 2, 1, zz + d - 2, job[0], E if job[0] in ('LECTERN', 'STONECUTTER', 'GRINDSTONE', 'SMOKER', 'BLAST_FURNACE', 'LOOM', 'BARREL') else 0)
        table = job[1]
    else:
        table = style + '_house'
    if r.random() < 0.6:
        t.chest(w - 2, 1, zz + 1, W, 'chests/village/village_' + (table if table != 'plains_house' else style + '_house'))
    t.set(1, h, zz + 1, 'TORCH', 1 + W) if st['light'] == 'TORCH' else t.set(w // 2, h, zz + d // 2, 'LANTERN', 1)
    # entrance jigsaw: on the doorstep, facing the street
    t.jig(dx, 1, 0, N, UP, 'building_entrance', 'street_side', 'empty', ('AIR', 0, 0))
    return t


def farm(st, seed, style):
    t = Tpl(9, 2, 10)
    for z in range(1, 10):
        for x in range(9):
            edge = x in (0, 8) or z in (1, 9)
            t.set(x, 0, z, st['log'] if edge else ('WATER' if x == 4 else 'FARMLAND_MOIST'))
            if not edge and x != 4: t.set(x, 1, z, random.Random(seed + x * 13 + z).choice(['WHEAT_3', 'WHEAT_2', 'WHEAT_3']))
    t.jig(4, 1, 0, N, UP, 'building_entrance', 'street_side', 'empty', ('AIR', 0, 0))
    return t


def pen(st, seed):
    t = Tpl(9, 3, 10)
    for z in range(1, 10):
        for x in range(9):
            if x in (0, 8) or z in (1, 9): t.set(x, 1, z, st['fence'])
    t.set(4, 1, 1, 'OAK_GATE' if st['fence'] == 'OAK_FENCE' else 'SPRUCE_GATE' if 'SPRUCE' in st['fence'] else 'ACACIA_PLANKS_GATE', N)
    t.set(2, 1, 7, 'HAY_BLOCK'); t.set(6, 1, 3, 'HAY_BLOCK', 1)
    t.jig(4, 1, 0, N, UP, 'building_entrance', 'street_side', 'empty', ('AIR', 0, 0))
    return t


def tower(st, seed):
    """a tall meeting hall / chapel"""
    t = house(st, 7, 9, 7, ('LECTERN', 'temple'), seed, 'tower')
    for y in range(8, 13):
        for x in (2, 3, 4):
            for z in (6, 7, 8):
                edge = x in (2, 4) or z in (6, 8)
                if edge: t.set(x, y, z, st['wall'] if st['wall'] != 'SNOW' else 'SPRUCE_PLANKS')
    t.sy = max(t.sy, 14)
    t.set(3, 13, 7, st['slab'])
    return t


def street(st, length, kind):
    """a street 5 wide (path in the middle three) along z, built from z 0 (where it joins) to length - 1"""
    t = Tpl(5, 1, length)
    def path(x, z): t.set(x, 0, z, st['path'])
    if kind == 'straight':
        for z in range(length):
            for x in (1, 2, 3): path(x, z)
        t.jig(2, 0, 0, N, UP, 'street', 'street', 'empty', (st['path'], 0, 0))
        t.jig(2, 0, length - 1, S, UP, 'street', 'street', 'streets', (st['path'], 0, 0))
        for z in range(3, length - 3, 6):
            t.jig(0, 0, z, W, UP, 'street_side', 'building_entrance', 'houses', (st['path'], 0, 0))
            t.jig(4, 0, z + 2, E, UP, 'street_side', 'building_entrance', 'houses', (st['path'], 0, 0))
            t.jig(0, 0, z + 2, UP, N, 'decor', 'bottom', 'decor', ('GRASS', 0, 0), roll=True)
    elif kind == 'cross':
        t = Tpl(9, 1, 9)
        for i in range(9):
            for k in (3, 4, 5): t.set(k, 0, i, st['path']); t.set(i, 0, k, st['path'])
        t.jig(4, 0, 0, N, UP, 'street', 'street', 'empty', (st['path'], 0, 0))
        t.jig(4, 0, 8, S, UP, 'street', 'street', 'streets', (st['path'], 0, 0))
        t.jig(0, 0, 4, W, UP, 'street', 'street', 'streets', (st['path'], 0, 0))
        t.jig(8, 0, 4, E, UP, 'street', 'street', 'streets', (st['path'], 0, 0))
    elif kind == 'corner':
        t = Tpl(9, 1, 9)
        for i in range(6):
            for k in (3, 4, 5): t.set(k, 0, i, st['path'])
        for i in range(3, 9):
            for k in (3, 4, 5): t.set(i, 0, k, st['path'])
        t.jig(4, 0, 0, N, UP, 'street', 'street', 'empty', (st['path'], 0, 0))
        t.jig(8, 0, 4, E, UP, 'street', 'street', 'streets', (st['path'], 0, 0))
        t.jig(2, 0, 7, S, UP, 'street_side', 'building_entrance', 'houses', ('GRASS', 0, 0))
    elif kind == 'end':
        t = Tpl(5, 1, 3)
        for z in range(3):
            for x in (1, 2, 3): path(x, z) if z < 2 else None
        t.jig(2, 0, 0, N, UP, 'street', 'street', 'empty', (st['path'], 0, 0))
    return t


def center(st, style, kind):
    t = Tpl(13, 6, 13)
    for x in range(13):
        for z in range(13):
            t.set(x, 0, z, st['path'] if 2 <= x <= 10 and 2 <= z <= 10 else st['found'] if style != 'snowy' else 'DIRT_PATH')
    if kind == 'well':
        t.fill(4, 0, 4, 8, 1, 8, st['found'] if st['found'] != 'SNOW' else 'COBBLE')
        t.fill(5, 1, 5, 7, 1, 7, 'WATER')
        t.fill(5, 0, 5, 7, 0, 7, 'WATER')
        for (x, z) in ((4, 4), (8, 4), (4, 8), (8, 8)):
            for y in (2, 3): t.set(x, y, z, st['fence'])
        t.fill(4, 4, 4, 8, 4, 8, st['slab'])
    else:
        t.set(6, 1, 6, st['log']); t.set(6, 2, 6, st['log']); t.set(6, 3, 6, 'BELL', 1 << 2)
        t.fill(5, 4, 5, 7, 4, 7, st['slab'])
        for (x, z) in ((5, 5), (7, 5), (5, 7), (7, 7)): t.set(x, 1, z, st['fence']); t.set(x, 2, z, st['fence']); t.set(x, 3, z, st['fence'])
    for (x, z) in ((2, 2), (10, 2), (2, 10), (10, 10)):
        t.set(x, 1, z, st['fence']); t.set(x, 2, z, st['fence']); t.set(x, 3, z, 'TORCH' if st['light'] == 'TORCH' else 'LANTERN')
    for (x, z, f) in ((6, 0, N), (6, 12, S), (0, 6, W), (12, 6, E)):
        t.jig(x, 0, z, f, UP, 'street', 'street', 'streets', (st['path'], 0, 0))
    return t


def lamp(st):
    t = Tpl(1, 4, 1)
    t.set(0, 0, 0, st['fence']); t.set(0, 1, 0, st['fence']); t.set(0, 2, 0, st['fence'])
    t.set(0, 3, 0, 'TORCH' if st['light'] == 'TORCH' else 'LANTERN')
    t.jig(0, 0, 0, DOWN, S, 'bottom', 'empty', 'empty', (st['fence'], 0, 0), roll=True)
    return t


def outposts():
    pools, tpls = {}, {}
    # base plate: an empty 16 x 1 x 16 frame with the tower in the middle (up) and plates around
    t = Tpl(16, 1, 16)
    t.jig(8, 0, 8, UP, N, 'tower_base', 'tower', 'pillager_outpost/towers', ('GRASS', 0, 0), roll=True)
    for (x, z, f) in ((8, 0, N), (15, 8, E), (0, 8, W), (8, 15, S)):
        t.jig(x, 0, z, f, UP, 'plate', 'plate', 'pillager_outpost/feature_plates', ('GRASS', 0, 0))
    tpls['pillager_outpost/base_plate'] = t
    # watchtower: dark oak frame, birch walls, four floors with ladders, a lookout with a chest
    t = Tpl(11, 22, 11)
    for y in range(0, 17):
        for x in range(1, 10):
            for z in range(1, 10):
                edge = x in (1, 9) or z in (1, 9)
                corner = x in (1, 9) and z in (1, 9)
                if corner: t.set(x, y, z, 'DARK_LOG')
                elif edge: t.set(x, y, z, 'BIRCH_PLANKS' if y % 5 else 'DARK_PLANKS')
                elif y % 5 == 0: t.set(x, y, z, 'DARK_PLANKS')
                else: t.set(x, y, z, 'AIR')
    for y in range(1, 17): t.set(5, y, 8, 'LADDER', N)
    for y in (5, 10, 15): t.set(5, y, 8, 'LADDER', N)
    t.set(5, 1, 1, 'AIR'); t.set(5, 2, 1, 'AIR')
    for y in (3, 8, 13):
        for (x, z) in ((5, 1), (1, 5), (9, 5)): t.set(x, y, z, 'AIR')
    t.fill(0, 17, 0, 10, 17, 10, 'DARK_PLANKS')
    for x in range(11):
        for z in range(11):
            if x in (0, 10) or z in (0, 10): t.set(x, 18, z, 'DARK_PLANKS_FENCE')
    for (x, z) in ((0, 0), (10, 0), (0, 10), (10, 10)):
        for y in (19, 20): t.set(x, y, z, 'DARK_LOG')
    t.fill(0, 21, 0, 10, 21, 10, 'DARK_PLANKS_SLAB')
    t.set(5, 17, 8, 'LADDER', N)
    t.chest(3, 18, 3, S, 'chests/pillager_outpost')
    t.set(7, 18, 3, 'CARVED_PUMPKIN', N)
    t.set(5, 0, 5, 'DARK_PLANKS')
    t.jig(5, 0, 5, DOWN, N, 'tower', 'empty', 'empty', ('DARK_PLANKS', 0, 0), roll=True)
    tpls['pillager_outpost/watchtower'] = t
    # feature plates and features
    t = Tpl(16, 1, 16)
    t.jig(8, 0, 15, S, UP, 'plate', 'plate', 'empty', ('GRASS', 0, 0))
    t.jig(8, 0, 7, UP, N, 'feature_base', 'feature', 'pillager_outpost/features', ('GRASS', 0, 0), roll=True)
    tpls['pillager_outpost/feature_plate'] = t
    cage = Tpl(5, 5, 5)
    for x in range(5):
        for z in range(5):
            for y in range(1, 4):
                if x in (0, 4) or z in (0, 4): cage.set(x, y, z, 'IRON_BARS')
            cage.set(x, 0, z, 'DARK_PLANKS'); cage.set(x, 4, z, 'DARK_PLANKS')
    cage.jig(2, 0, 2, DOWN, N, 'feature', 'empty', 'empty', ('DARK_PLANKS', 0, 0), roll=True)
    tent = Tpl(5, 4, 6)
    for z in range(6):
        for i in range(3):
            tent.set(i, i + 1, z, 'WOOL_WHITE'); tent.set(4 - i, i + 1, z, 'WOOL_WHITE')
    tent.set(2, 0, 2, 'DARK_PLANKS'); tent.jig(2, 0, 2, DOWN, N, 'feature', 'empty', 'empty', ('DARK_PLANKS', 0, 0), roll=True)
    logs = Tpl(5, 3, 5)
    for x in range(5): logs.set(x, 1, 1, 'DARK_LOG', 1); logs.set(x, 1, 3, 'DARK_LOG', 1)
    for z in range(5): logs.set(2, 2, z, 'DARK_LOG', 2)
    logs.set(2, 0, 2, 'DIRT'); logs.jig(2, 0, 2, DOWN, N, 'feature', 'empty', 'empty', ('DIRT', 0, 0), roll=True)
    targets = Tpl(3, 3, 3)
    targets.set(1, 1, 1, 'HAY_BLOCK'); targets.set(1, 2, 1, 'CARVED_PUMPKIN', N)
    targets.set(1, 0, 1, 'DIRT'); targets.jig(1, 0, 1, DOWN, N, 'feature', 'empty', 'empty', ('DIRT', 0, 0), roll=True)
    for n, tt in (('cage', cage), ('tent', tent), ('logs', logs), ('targets', targets)): tpls['pillager_outpost/feature_' + n] = tt
    pools['pillager_outpost/base_plates'] = {'e': [[0, 'pillager_outpost/base_plate', 1, 0, '']], 'f': 'empty'}
    pools['pillager_outpost/towers'] = {'e': [[0, 'pillager_outpost/watchtower', 1, 0, '']], 'f': 'empty'}
    pools['pillager_outpost/feature_plates'] = {'e': [[0, 'pillager_outpost/feature_plate', 1, 1, '']], 'f': 'empty'}
    pools['pillager_outpost/features'] = {'e': [[0, 'pillager_outpost/feature_' + n, w, 0, ''] for n, w in (('cage', 1), ('tent', 2), ('logs', 2), ('targets', 2))] + [[2, '', 6, 0, '']], 'f': 'empty'}
    return pools, tpls


def villages():
    pools, tpls = {}, {}
    for style, st in STYLES.items():
        P = 'village/%s/' % style
        names = []
        # town centres
        cs = []
        for kind in ('well', 'bell'):
            n = P + 'town_centers/' + kind
            tpls[n] = center(st, style, kind); cs.append([0, n, 1, 0, 'mossy'])
        pools[P + 'town_centers'] = {'e': cs, 'f': 'empty'}
        # streets
        ss = []
        for L in (12, 16, 20):
            n = P + 'streets/straight_%d' % L; tpls[n] = street(st, L, 'straight'); ss.append([0, n, 4, 1, 'street'])
        for kind, w in (('cross', 2), ('corner', 2)):
            n = P + 'streets/' + kind; tpls[n] = street(st, 9, kind); ss.append([0, n, w, 1, 'street'])
        pools[P + 'streets'] = {'e': ss, 'f': P + 'terminators'}
        n = P + 'terminators/end'; tpls[n] = street(st, 3, 'end')
        pools[P + 'terminators'] = {'e': [[0, n, 1, 1, 'street']], 'f': 'empty'}
        # houses
        hs = []
        seed = sum(map(ord, style))
        for i, (w, d, h) in enumerate(((5, 5, 3), (5, 6, 3), (7, 6, 3), (7, 8, 4), (5, 7, 3), (9, 7, 4))):
            n = P + 'houses/house_%d' % (i + 1); tpls[n] = house(st, w, d, h, None, seed + i, style); hs.append([0, n, 3, 0, 'mossy'])
        r = random.Random(seed)
        for i, job in enumerate(JOBS):
            w, d = r.choice(((5, 6), (7, 6), (7, 7)))
            n = P + 'houses/%s_%d' % (job[1], i + 1); tpls[n] = house(st, w, d, 3, job, seed + 100 + i, style); hs.append([0, n, 2, 0, 'mossy'])
        for i in range(2):
            n = P + 'houses/farm_%d' % (i + 1); tpls[n] = farm(st, seed + 200 + i, style); hs.append([0, n, 4, 0, 'farm'])
        n = P + 'houses/pen'; tpls[n] = pen(st, seed); hs.append([0, n, 2, 0, ''])
        n = P + 'houses/hall'; tpls[n] = tower(st, seed + 300); hs.append([0, n, 1, 0, 'mossy'])
        hs.append([2, '', 6, 0, ''])
        pools[P + 'houses'] = {'e': hs, 'f': 'empty'}
        # decor: lamps, trees, piles and patches on the street sides
        n = P + 'decor/lamp'; tpls[n] = lamp(st)
        dec = [[0, n, 3, 0, '']] + [[1, f, 2, 0, ''] for f in st['trees'] + st['piles'] + [st['patch']]] + [[2, '', 4, 0, '']]
        pools[P + 'decor'] = {'e': dec, 'f': 'empty'}
        # pools named relative to the style
        for t in tpls.values():
            for k, j in enumerate(t.jigs):
                if j[7] in ('streets', 'houses', 'decor'): t.jigs[k] = j[:7] + (P + j[7],) + j[8:]
    return pools, tpls


RULES = {   # own processors: block -> [(probability, spot, output)]
    'mossy': {'COBBLE': [(0.1, 0, 'MOSSY')], 'STONE_BRICKS': [(0.1, 0, 'MOSSY_STONE_BRICKS')]},
    'street': {'DIRT_PATH': [(1.0, 1, 'PLANKS'), (0.08, 0, 'GRASS')], 'SMOOTH_SANDSTONE': [(1.0, 1, 'BIRCH_PLANKS')]},
    'farm': {'WHEAT_3': [(0.3, 0, 'CARROTS_3'), (0.2, 0, 'POTATOES_3'), (0.1, 0, 'BEETROOTS_3')], 'WHEAT_2': [(0.3, 0, 'CARROTS_2'), (0.2, 0, 'POTATOES_2')]},
}


def jig_encode(t, proc_names, strs):
    def si(v):
        if v not in strs: strs.append(v)
        return strs.index(v)
    d, pid = encode(t)
    # state index -> its block (encode numbers the distinct values of t.b in insertion order)
    seen = []
    for v in t.b.values():
        if v not in seen: seen.append(v)
    jig = []
    for (x, y, z, f, top, name, target, pool, final, roll) in t.jigs:
        fi = pid(list(final))
        jig.append([x, y, z, f, top, si(name), si(target), si(pool), fi, 1 if roll else 0])
        t.b.pop((x, y, z), None)
    rules = {}
    for pn in proc_names:
        if not pn: continue
        rm = {}
        for si_, v in enumerate(seen):
            for prob, spot, outk in RULES[pn].get(v[0], []):
                rm.setdefault(str(si_), []).append([prob, spot, pid([outk, 0, 0]) if outk != 'AIR' else 0])
        rules[pn] = rm
    d['j'] = jig; d['r'] = rules; d['l'] = t.loot
    return d


def check_keys(templates):
    import re
    keys = set(re.findall(r'^\s*\["([A-Z_0-9]+)"', open(os.path.join(ROOT, 'src', 'blockdata.js')).read(), re.M)) | {'AIR'}
    bad = {}
    for n, d in templates.items():
        for p in d['p']:
            if p[0] not in keys: bad.setdefault(p[0], n)
    if bad: raise SystemExit('unknown blocks: ' + ', '.join('%s (%s)' % kv for kv in sorted(bad.items())))


def main():
    out = {}
    out.update(igloo()); out.update(shipwrecks()); out.update(ruins())
    check_keys(out)
    js = ('\'use strict\';\n// Structure templates of this game (own designs), generated by tools/buildings.py.\n'
          'const STRUCT_TPL = ' + json.dumps(out, separators=(',', ':')) + ';\n')
    open(os.path.join(ROOT, 'src', 'structures.js'), 'w').write(js)
    pools, tpls = villages()
    p2, t2 = outposts(); pools.update(p2); tpls.update(t2)
    strs = ['empty', 'bottom']
    procs = {}
    for P in pools.values():
        for e in P['e']:
            if e[0] == 0: procs.setdefault(e[1], set()).add(e[4])
    jt = {}
    for n, t in tpls.items():
        # jigsaw cells must not be in the grid: take them out before encoding
        for j in t.jigs: t.b.pop((j[0], j[1], j[2]), None)
        jt[n] = jig_encode(t, sorted(procs.get(n, {''})), strs)
    check_keys(jt)
    js = ('\'use strict\';\n// Jigsaw structures of this game (own designs): villages and pillager outposts, generated by tools/buildings.py.\n'
          'const JIG_STR = ' + json.dumps(strs, separators=(',', ':')) + ';\n'
          'const JIG_POOLS = ' + json.dumps(pools, separators=(',', ':')) + ';\n'
          'const JIG_TPL = ' + json.dumps(jt, separators=(',', ':')) + ';\n')
    open(os.path.join(ROOT, 'src', 'jigsaw.js'), 'w').write(js)
    print('structures', len(out), 'jigsaw templates', len(jt), 'pools', len(pools))


if __name__ == '__main__':
    main()
