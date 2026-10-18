"""Ground and rock: gravel, sand, dirt, grass, stone and its kin, clay, snow, concrete..."""
from core import (N, at, fbm, white, mix, cells, rims, rng, COLOURS, Canvas, select, lum)


def gravel_f(rnd):
    """loose pebbles: each its own tone, lit on top, shaded below, dark gaps between"""
    ids, d1, d2, n = cells(rnd, 12, 0.95)
    tone = [rnd.uniform(0.3, 0.9) for _ in range(n)]
    r, w = rims(ids, 0.35, 0.45), white(rnd)
    return [-1.0 if d2[i] - d1[i] < 1.0 else tone[ids[i]] + r[i] + 0.1 * (w[i] - 0.5) for i in range(N * N)]


def sand_f(rnd):
    """fine even grain with a few clearly darker and lighter grains"""
    f = mix((0.45, fbm(rnd, [(8, 8, 0.3), (4, 4, 0.3), (2, 2, 0.4)])), (0.55, white(rnd)))
    for i in rnd.sample(range(N * N), 14):
        f[i] = -0.5
    for i in rnd.sample(range(N * N), 10):
        f[i] = 1.5
    return f


def dirt_f(rnd):
    """earth: a fine mottle of crumbs, a few dark pits and light grains"""
    f = mix((0.5, fbm(rnd, [(4, 4, 0.5), (2, 2, 0.5)])), (0.5, white(rnd)))
    for i in rnd.sample(range(N * N), 10):
        f[i] = -0.5
    for i in rnd.sample(range(N * N), 6):
        f[i] = 1.5
    return f


def coarse_f(rnd):
    f = dirt_f(rnd)
    for _ in range(6):
        x, y = rnd.randrange(N), rnd.randrange(N)
        f[at(x, y)] = f[at(x + 1, y)] = f[at(x, y + 1)] = 1.5
        f[at(x + 1, y + 1)] = 1.1
        f[at(x, y + 2)] = f[at(x + 1, y + 2)] = f[at(x + 2, y + 1)] = -0.4
    return f


def blades_f(rnd, count, h=N):
    """short grass blades standing up: a light stroke with its shadow at the foot"""
    f = mix((0.55, fbm(rnd, [(8, 8, 0.4), (4, 4, 0.3), (2, 2, 0.3)])), (0.3, white(rnd)))
    for _ in range(count):
        x, y, n, lean = rnd.randrange(N), rnd.randrange(h), rnd.choice((2, 2, 3)), rnd.choice((-1, 0, 0, 1))
        for k in range(n):
            f[at(x + (lean if k == n - 1 else 0), y - k)] = 1.0 + 0.1 * k
        f[at(x, y + 1)] = -0.3
    return f


def stone_f(rnd):
    """soft blotches drawn out sideways, a fine grain over them"""
    return mix((0.75, fbm(rnd, [(16, 4, 0.35), (8, 2, 0.3), (4, 2, 0.2), (2, 1, 0.15)])), (0.25, white(rnd)))


def blotch_f(contrast, n=20):
    """patches of a few tones (andesite, diorite, granite, tuff)"""
    def gen(rnd):
        ids, _, _, k = cells(rnd, n, 1.0)
        tone = [rnd.random() for _ in range(k)]
        nz, w = fbm(rnd, [(4, 4, 0.5), (2, 2, 0.5)]), white(rnd)
        return [contrast * tone[ids[i]] + (1 - contrast) * (0.5 * nz[i] + 0.5 * w[i]) for i in range(N * N)]
    return gen


def smooth_f(specks, grain=0.3):
    """an even surface, gently clouded, with a few specks (clay, snow, concrete, terracotta)"""
    def gen(rnd):
        f = mix((1 - grain, fbm(rnd, [(8, 8, 0.5), (4, 4, 0.3), (2, 2, 0.2)])), (grain, white(rnd)))
        for i in rnd.sample(range(N * N), specks):
            f[i] = rnd.choice((-0.5, 1.5))
        return f
    return gen


def rough_f(rnd):
    """lumpy rock with dark cracks between the lumps (netherrack, soul soil)"""
    ids, d1, d2, n = cells(rnd, 25, 1.0)
    tone = [rnd.uniform(0.3, 1.0) for _ in range(n)]
    r, w = rims(ids, 0.12, 0.2), white(rnd)
    return [-1.0 if d2[i] - d1[i] < 0.45 else 0.6 * tone[ids[i]] + r[i] + 0.4 * w[i] for i in range(N * N)]


def pitted_f(rnd):
    """even stone with small dark pits, each lit on its lower right rim (end stone)"""
    f = smooth_f(0)(rnd)
    for _ in range(9):
        x, y = rnd.randrange(N), rnd.randrange(N)
        f[at(x, y)] = -1.0
        if rnd.random() < 0.5:
            f[at(x + 1, y)] = -0.8
        f[at(x + 1, y + 1)] = 1.3
    return f


def streak_f(rnd):
    """layers lying flat (deepslate, basalt top, dripstone)"""
    f = fbm(rnd, [(8, 4, 0.35), (8, 2, 0.35), (4, 1, 0.3)])
    return mix((0.75, f), (0.25, white(rnd)))


def vertical_f(rnd):
    """layers standing up (basalt side)"""
    return mix((0.8, fbm(rnd, [(2, 16, 0.5), (1, 8, 0.3), (1, 4, 0.2)])), (0.2, white(rnd)))


def moss_f(rnd):
    """soft tufts of moss"""
    ids, _, _, n = cells(rnd, 20, 1.0)
    tone = [rnd.uniform(0.3, 0.9) for _ in range(n)]
    r, w = rims(ids, 0.15, 0.2), white(rnd)
    return [0.6 * tone[ids[i]] + r[i] + 0.3 * w[i] for i in range(N * N)]


FIELDS = {
    'gravel': gravel_f, 'sand': sand_f, 'red_sand': sand_f, 'dirt': dirt_f, 'coarse_dirt': coarse_f,
    'grass_block_top': lambda r: blades_f(r, 34), 'stone': stone_f, 'andesite': blotch_f(0.55),
    'diorite': blotch_f(0.7, 25), 'granite': blotch_f(0.6, 30), 'tuff': blotch_f(0.5, 16), 'clay': smooth_f(6),
    'snow': smooth_f(5), 'calcite': blotch_f(0.35, 12), 'netherrack': rough_f, 'end_stone': pitted_f,
    'sandstone_top': sand_f, 'red_sandstone_top': sand_f, 'mud': smooth_f(10), 'soul_soil': rough_f,
    'soul_sand': rough_f, 'terracotta': smooth_f(12), 'deepslate': streak_f, 'basalt_top': rough_f,
    'basalt_side': vertical_f, 'smooth_basalt': smooth_f(8), 'blackstone': rough_f, 'dripstone_block': streak_f,
    'moss_block': moss_f, 'bedrock': rough_f, 'obsidian': blotch_f(0.6, 16), 'crying_obsidian': blotch_f(0.6, 16),
    'packed_ice': smooth_f(4, 0.15), 'blue_ice': smooth_f(4, 0.15), 'magma': rough_f, 'nether_wart_block': moss_f,
    'warped_wart_block': moss_f, 'mycelium_top': lambda r: blades_f(r, 20), 'podzol_top': lambda r: blades_f(r, 26),
    'crimson_nylium': moss_f, 'warped_nylium': moss_f, 'dirt_path_top': smooth_f(16, 0.45),
    'farmland': streak_f, 'farmland_moist': streak_f, 'rooted_dirt': dirt_f, 'smooth_stone': smooth_f(0, 0.15),
    'glowstone': rough_f, 'sponge': pitted_f, 'wet_sponge': pitted_f, 'amethyst_block': blotch_f(0.6, 16),
    'budding_amethyst': blotch_f(0.6, 16), 'ancient_debris_top': rough_f, 'raw_iron_block': rough_f,
    'raw_gold_block': rough_f, 'raw_copper_block': rough_f, 'honeycomb_block': rough_f, 'shroomlight': rough_f,
    'cobbled_deepslate': gravel_f, 'dried_kelp_top': rough_f, 'sea_lantern': blotch_f(0.6, 9),
}
for c in COLOURS:
    FIELDS[c + '_concrete'] = smooth_f(0, 0.2)
    FIELDS[c + '_concrete_powder'] = sand_f
    FIELDS[c + '_terracotta'] = smooth_f(12)


def make_simple(name, fieldf):
    def gen(ctx):
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(fieldf(rng(name)), ctx.src.px(name))
        return c.image()
    return gen


def side_with_top(name, base, topname, depth=(3, 5), drips=4, green=True):
    """a side face: the base block below and a fringe of the top's cover hanging down unevenly
    (grass, mycelium, podzol, nylium, snowy grass); the fringe colours are the source side's
    pixels that are not the base's"""
    def gen(ctx):
        rnd = rng(name)
        side = ctx.src.px(name)
        base_px = ctx.src.px(base)
        edge = fbm(rnd, [(8, 16, 0.6), (4, 16, 0.4)])
        dep = [depth[0] + round((depth[1] - depth[0]) * edge[x]) for x in range(N)]
        for x in rnd.sample(range(N), drips):
            dep[x] += rnd.choice((1, 1, 2))
        mask = [(i // N) < dep[i % N] for i in range(N * N)]
        if green:
            cover = select(side, lambda p: p[1] > p[0] + 8 and p[1] > p[2])
        else:
            top = side[:N * 2]
            tl = sum(lum(p) for p in top) / len(top)
            cover = [p for p in top if abs(lum(p) - tl) < 40]
        f = blades_f(rnd, 10)
        f = [f[i] - (0.6 if i // N == dep[i % N] - 1 else 0) for i in range(N * N)]
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.px = list(ctx.out(base).get_flattened_data())
        c.part(f, cover or side, mask)
        return c.image()
    return gen


def register(reg):
    for n, f in FIELDS.items():
        reg[n] = make_simple(n, f)
    reg['grass_block_side'] = side_with_top('grass_block_side', 'dirt', 'grass_block_top')
    reg['mycelium_side'] = side_with_top('mycelium_side', 'dirt', 'mycelium_top', (2, 4), 5, green=False)
    reg['podzol_side'] = side_with_top('podzol_side', 'dirt', 'podzol_top', (2, 4), 4, green=False)
    reg['crimson_nylium_side'] = side_with_top('crimson_nylium_side', 'netherrack', 'crimson_nylium', (3, 5), 5, False)
    reg['warped_nylium_side'] = side_with_top('warped_nylium_side', 'netherrack', 'warped_nylium', (3, 5), 5, False)
    reg['dirt_path_side'] = side_with_top('dirt_path_side', 'dirt', 'dirt_path_top', (1, 2), 0, False)
