"""Masonry: bricks of all kinds, cobblestone, polished stone, ores, metal and gem blocks, sandstone,
quartz, purpur, prismarine, copper."""
import math
from core import (N, at, fbm, white, mix, cells, rims, rng, Canvas, select, lum)
import natural

# brick layouts: (course height, brick length, row offset, mortar width)
BRICKS = {
    'bricks': (4, 8, 4, 1), 'stone_bricks': (8, 16, 8, 1), 'mossy_stone_bricks': (8, 16, 8, 1),
    'cracked_stone_bricks': (8, 16, 8, 1), 'deepslate_bricks': (4, 8, 4, 1), 'cracked_deepslate_bricks': (4, 8, 4, 1),
    'deepslate_tiles': (4, 4, 0, 1), 'cracked_deepslate_tiles': (4, 4, 0, 1), 'nether_bricks': (4, 8, 4, 1),
    'red_nether_bricks': (4, 8, 4, 1), 'cracked_nether_bricks': (4, 8, 4, 1), 'end_stone_bricks': (8, 8, 4, 1),
    'mud_bricks': (4, 8, 4, 1), 'polished_blackstone_bricks': (4, 8, 4, 1),
    'cracked_polished_blackstone_bricks': (4, 8, 4, 1), 'tuff_bricks': (8, 8, 4, 1), 'quartz_bricks': (8, 8, 4, 1),
    'prismarine_bricks': (4, 8, 4, 1),
}


def brick_ids(course, length, offset):
    ids, mortar = [], []
    for i in range(N * N):
        x, y = i % N, i // N
        row = y // course
        xx = (x + (offset if row % 2 else 0)) % N
        ids.append(row * 64 + xx // length)
        mortar.append(y % course == course - 1 or xx % length == length - 1)
    return ids, mortar


def bricks_f(rnd, course, length, offset, mortar_w=1):
    ids, mortar = brick_ids(course, length, offset)
    tone = {k: rnd.uniform(0.35, 0.8) for k in set(ids)}
    lid = [-1 if mortar[i] else ids[i] for i in range(N * N)]
    r = rims(lid, 0.22, 0.22)
    nz, w = fbm(rnd, [(4, 4, 0.5), (2, 2, 0.5)]), white(rnd)
    return [-1.0 + 0.1 * w[i] if mortar[i] else tone[ids[i]] + r[i] + 0.2 * nz[i] + 0.15 * w[i] for i in range(N * N)], mortar


def cracks(rnd, f, count=3):
    """dark cracks running across the bricks"""
    for _ in range(count):
        x, y = rnd.randrange(N), rnd.randrange(N)
        for _ in range(rnd.randrange(4, 9)):
            f[at(x, y)] = -1.2
            x += rnd.choice((-1, 0, 1)); y += rnd.choice((0, 1, 1))
    return f


def moss_mask(rnd, amount):
    nz = fbm(rnd, [(8, 8, 0.6), (4, 4, 0.4)])
    order = sorted(range(N * N), key=lambda i: -nz[i] - 0.4 * (i // N < 6))
    m = set(order[:int(amount * N * N)])
    return [i in m for i in range(N * N)]


def brickwork(name, base_src=None):
    course, length, offset, _ = BRICKS[name]

    def gen(ctx):
        rnd = rng(name)
        f, mortar = bricks_f(rnd, course, length, offset)
        if name.startswith('cracked'):
            f = cracks(rnd, f, 4)
        src = ctx.px(name, base_src or 'stone_bricks')
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        if name.startswith('mossy'):
            moss = moss_mask(rnd, 0.3)
            green = select(src, lambda p: p[1] > p[0] + 6 and p[1] > p[2])
            grey = select(src, lambda p: not (p[1] > p[0] + 6 and p[1] > p[2]))
            c.part(f, grey or src, [not m for m in moss])
            c.part(natural.moss_f(rnd), green or src, moss)
        else:
            c.part(f, src)
        return c.image()
    return gen


def cobble_f(rnd, n=9):
    """round stones set in mortar, each lit on top"""
    ids, d1, d2, k = cells(rnd, n, 0.9)
    tone = [rnd.uniform(0.35, 0.85) for _ in range(k)]
    r, w = rims(ids, 0.3, 0.4), white(rnd)
    inner = [d1[i] / max(0.5, d2[i]) for i in range(N * N)]
    return [-1.0 if d2[i] - d1[i] < 0.9 else tone[ids[i]] + r[i] + 0.15 * w[i] - 0.2 * inner[i] for i in range(N * N)]


def cobblestone(name, n=9):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'cobblestone')
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        f = cobble_f(rnd, n)
        if name.startswith('mossy'):
            moss = moss_mask(rnd, 0.35)
            green = select(src, lambda p: p[1] > p[0] + 6 and p[1] > p[2])
            grey = select(src, lambda p: not (p[1] > p[0] + 6 and p[1] > p[2]))
            c.part(f, grey or src, [not m for m in moss])
            c.part(natural.moss_f(rnd), green or src, moss)
        else:
            c.part(f, src)
        return c.image()
    return gen


def framed_f(rnd, inner_f, border=1, lit=0.4, dark=0.4):
    """a slab with a bevelled rim: light along the top and left, dark along the bottom and right"""
    f = inner_f(rnd)
    for i in range(N * N):
        x, y = i % N, i // N
        if x < border or y < border:
            f[i] += lit
        elif x >= N - border or y >= N - border:
            f[i] -= dark
    return f


def polished(name, inner=None):
    def gen(ctx):
        rnd = rng(name)
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(framed_f(rnd, inner or natural.smooth_f(3, 0.25)), ctx.px(name, 'smooth_stone'))
        return c.image()
    return gen


# ---- ores ----------------------------------------------------------------------------------------

ORE_PICK = {
    'coal': lambda p: lum(p) < 55, 'iron': lambda p: p[0] > p[2] + 25, 'copper': lambda p: p[0] > p[2] + 30 or p[1] > p[0] + 15,
    'gold': lambda p: p[0] > p[2] + 50 and p[1] > p[2] + 30, 'diamond': lambda p: p[2] > p[0] + 30,
    'emerald': lambda p: p[1] > p[0] + 30, 'lapis': lambda p: p[2] > p[0] + 30, 'redstone': lambda p: p[0] > p[1] + 50,
    'nether_gold': lambda p: p[0] > p[2] + 60 and p[1] > p[2] + 40, 'nether_quartz': lambda p: lum(p) > 150,
}


def ore_mask(rnd, clusters=5):
    """nuggets: small round clumps of about 5 to 11 pixels, spread over the face"""
    m = [False] * (N * N)
    pts = []
    for _ in range(clusters * 30):
        if len(pts) == clusters:
            break
        x, y = rnd.randrange(1, N - 2), rnd.randrange(1, N - 2)
        if all(abs(x - a) + abs(y - b) > 6 for a, b in pts):
            pts.append((x, y))
    for x, y in pts:
        r = rnd.uniform(1.3, 2.0)
        for dy in (-2, -1, 0, 1, 2):
            for dx in (-2, -1, 0, 1, 2):
                if math.hypot(dx * 1.1, dy) <= r and rnd.random() > 0.2:
                    m[at(x + dx, y + dy)] = True
    return m


def ore(name, kind, base):
    def gen(ctx):
        rnd = rng(name)
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        base_img = ctx.out(base)
        c.px = list(base_img.get_flattened_data())
        src = ctx.px(name)
        pick = ORE_PICK[kind]
        orepx = select(src, pick) if src else None
        m = ore_mask(rnd)
        ids = [1 if m[i] else 0 for i in range(N * N)]
        r, w = rims(ids, 0.4, 0.4), white(rnd)
        c.part([0.5 + r[i] + 0.25 * w[i] for i in range(N * N)], orepx if orepx and len(orepx) >= 4 else src, m)
        # a dark shade under each nugget so it stands out of the rock
        for i in range(N * N):
            x, y = i % N, i // N
            if not m[i] and m[at(x - 1, y - 1)] and kind != 'coal':
                p = c.px[i]
                c.px[i] = (int(p[0] * 0.7), int(p[1] * 0.7), int(p[2] * 0.7), p[3])
        return c.image()
    return gen


# ---- metal and gem blocks ----------------------------------------------------------------------

def plate_f(kind):
    def gen(rnd):
        w = white(rnd)
        f = [0.5 + 0.1 * w[i] for i in range(N * N)]
        for i in range(N * N):
            x, y = i % N, i // N
            if kind == 'lines':          # iron: a sheet with two pressed seams
                if y in (5, 10):
                    f[i] = 0.1
                elif y in (6, 11):
                    f[i] = 0.9
            elif kind == 'shine':        # gold: a diagonal sheen
                f[i] += 0.3 * math.cos((x + y) / 3.0)
            elif kind == 'facets':       # diamond, emerald: cut facets
                f[i] += 0.35 * (1 if (x // 4 + y // 4) % 2 else -1) * (1 - ((x % 4) + (y % 4)) / 8)
            elif kind == 'rivets':       # netherite: dark plate with rivets
                if x % 8 == 2 and y % 8 == 2:
                    f[i] = 1.2
            elif kind == 'speckle':      # lapis, redstone, coal
                f[i] = 0.4 * w[i] + 0.6 * f[i]
        return f
    return gen


METAL = {'iron_block': 'lines', 'gold_block': 'shine', 'diamond_block': 'facets', 'emerald_block': 'facets',
         'netherite_block': 'rivets', 'lapis_block': 'speckle', 'redstone_block': 'speckle', 'coal_block': 'speckle',
         'copper_block': 'shine', 'exposed_copper': 'shine', 'weathered_copper': 'speckle', 'oxidized_copper': 'speckle'}


def metal(name, kind):
    def gen(ctx):
        rnd = rng(name)
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        f = framed_f(rnd, plate_f(kind), 1, 0.5, 0.5)
        if kind == 'speckle':
            f = mix((0.7, f), (0.3, natural.blotch_f(0.6, 16)(rnd)))
        c.part(f, ctx.px(name, 'iron_block'))
        return c.image()
    return gen


def cut_tiles(name, src_name):
    """four tiles with bevelled rims (cut copper, cut sandstone tops...)"""
    def gen(ctx):
        rnd = rng(name)
        ids = [(i % N) // 8 + 2 * ((i // N) // 8) for i in range(N * N)]
        r, w = rims(ids, 0.4, 0.4), white(rnd)
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part([0.5 + r[i] + 0.2 * w[i] for i in range(N * N)], ctx.px(name, src_name))
        return c.image()
    return gen


def layered(name, src_name, bands=(0, 3, 12, 15)):
    """sandstone sides: smooth layers with a bevelled band at the top and a rough one at the foot"""
    def gen(ctx):
        rnd = rng(name)
        f = natural.streak_f(rnd)
        for i in range(N * N):
            y = i // N
            if y < bands[1]:
                f[i] += 0.3 - 0.15 * y
            elif y >= bands[2]:
                f[i] = 0.2 + 0.6 * white(rnd, 1)[0]
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(f, ctx.px(name, src_name))
        return c.image()
    return gen


def chiseled(name, src_name, motif='ring'):
    """a framed block with a carved motif in the middle"""
    def gen(ctx):
        rnd = rng(name)
        f = framed_f(rnd, natural.smooth_f(2, 0.2), 2, 0.45, 0.45)
        for i in range(N * N):
            x, y = i % N + 0.5, i // N + 0.5
            d = math.hypot(x - 8, y - 8)
            if motif == 'ring' and 3.2 < d < 4.6:
                f[i] -= 0.6
            elif motif == 'cross' and (abs(x - 8) < 1 or abs(y - 8) < 1) and d < 5:
                f[i] -= 0.6
            elif motif == 'lines' and 3 <= i // N <= 12 and (i % N) in (5, 10):
                f[i] -= 0.6
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(f, ctx.px(name, src_name))
        return c.image()
    return gen


def pillar(name, src_name):
    """a pillar's side: fluted lines running up"""
    def gen(ctx):
        rnd = rng(name)
        w = white(rnd)
        f = [0.5 + 0.15 * w[i] + (-0.5 if (i % N) in (0, 15) else 0.25 if (i % N) in (1, 8) else -0.25 if (i % N) in (7, 14) else 0)
             for i in range(N * N)]
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(f, ctx.px(name, src_name))
        return c.image()
    return gen


def register(reg):
    for n in BRICKS:
        reg[n] = brickwork(n)
    reg['cobblestone'] = cobblestone('cobblestone')
    reg['mossy_cobblestone'] = cobblestone('mossy_cobblestone')
    reg['cobbled_deepslate'] = cobblestone('cobbled_deepslate', 12)
    for n in ('polished_andesite', 'polished_diorite', 'polished_granite', 'polished_deepslate', 'polished_tuff',
              'polished_blackstone', 'smooth_stone', 'quartz_block_side', 'quartz_block_bottom', 'purpur_block',
              'cut_copper', 'exposed_cut_copper', 'weathered_cut_copper', 'oxidized_cut_copper'):
        reg[n] = polished(n)
    for n in ('cut_copper', 'exposed_cut_copper', 'weathered_cut_copper', 'oxidized_cut_copper', 'cut_sandstone',
              'cut_red_sandstone'):
        reg[n] = cut_tiles(n, n)
    for kind, base in (('coal', 'stone'), ('iron', 'stone'), ('copper', 'stone'), ('gold', 'stone'),
                       ('diamond', 'stone'), ('emerald', 'stone'), ('lapis', 'stone'), ('redstone', 'stone')):
        reg[kind + '_ore'] = ore(kind + '_ore', kind, base)
        reg['deepslate_%s_ore' % kind] = ore('deepslate_%s_ore' % kind, kind, 'deepslate')
    reg['nether_gold_ore'] = ore('nether_gold_ore', 'nether_gold', 'netherrack')
    reg['nether_quartz_ore'] = ore('nether_quartz_ore', 'nether_quartz', 'netherrack')
    for n, k in METAL.items():
        reg[n] = metal(n, k)
    reg['sandstone'] = layered('sandstone', 'sandstone')
    reg['red_sandstone'] = layered('red_sandstone', 'red_sandstone')
    reg['chiseled_sandstone'] = chiseled('chiseled_sandstone', 'chiseled_sandstone', 'ring')
    reg['chiseled_red_sandstone'] = chiseled('chiseled_red_sandstone', 'chiseled_red_sandstone', 'ring')
    reg['chiseled_stone_bricks'] = chiseled('chiseled_stone_bricks', 'chiseled_stone_bricks', 'ring')
    reg['chiseled_quartz_block'] = chiseled('chiseled_quartz_block', 'chiseled_quartz_block', 'lines')
    reg['chiseled_deepslate'] = chiseled('chiseled_deepslate', 'chiseled_deepslate', 'cross')
    reg['chiseled_nether_bricks'] = chiseled('chiseled_nether_bricks', 'chiseled_nether_bricks', 'cross')
    reg['chiseled_polished_blackstone'] = chiseled('chiseled_polished_blackstone', 'chiseled_polished_blackstone', 'cross')
    reg['chiseled_tuff'] = chiseled('chiseled_tuff', 'chiseled_tuff', 'lines')
    for n, s in (('chiseled_copper_block', 'chiseled_copper'), ('chiseled_exposed_copper', 'exposed_chiseled_copper'),
                 ('chiseled_weathered_copper', 'weathered_chiseled_copper'),
                 ('chiseled_oxidized_copper', 'oxidized_chiseled_copper')):
        reg[n] = chiseled(n, n, 'cross')
    reg['quartz_pillar'] = pillar('quartz_pillar', 'quartz_pillar')
    reg['purpur_pillar'] = pillar('purpur_pillar', 'purpur_pillar')
    reg['quartz_pillar_top'] = chiseled('quartz_pillar_top', 'quartz_pillar_top', 'ring')
    reg['bone_block_side'] = pillar('bone_block_side', 'bone_block_side')
    reg['bone_block_top'] = chiseled('bone_block_top', 'bone_block_top', 'ring')
    reg['polished_basalt_side'] = pillar('polished_basalt_side', 'polished_basalt_side')
    reg['polished_basalt_top'] = polished('polished_basalt_top')
    reg['ancient_debris_side'] = layered('ancient_debris_side', 'ancient_debris_side', (0, 0, 16, 16))
    reg['gilded_blackstone'] = ore('gilded_blackstone', 'nether_gold', 'blackstone')
    reg['dark_prismarine'] = brickwork_like('dark_prismarine', (4, 8, 4, 1))
    reg['prismarine'] = lambda ctx: natural.make_simple('prismarine', natural.blotch_f(0.6, 16))(ctx)


def brickwork_like(name, layout):
    BRICKS[name] = layout
    return brickwork(name, name)
