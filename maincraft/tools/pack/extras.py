"""The rest of the game's pictures: block cracks, amethyst buds, the spawner cage, mangrove roots and
propagule, the ender pearl and slimeball, the snowy grass side, the redstone torch, and the HUD
icons (hearts, food, air)."""
import math
from core import (N, at, fbm, white, rng, Canvas, select, lum)
import natural


def canvas(ctx, name, w=N, h=N):
    return Canvas(ctx.style, name, w, h, ref=ctx.ref(name) if (w, h) == (N, N) else None)


def crack(stage):
    """block breaking: a crack tree out of the middle; each stage draws more of the same tree"""
    def gen(ctx):
        rnd = rng('destroy')
        segs, pts = [], [(7.5, 7.5)]
        for _ in range(70):
            x0, y0 = pts[rnd.randrange(len(pts))]
            a = rnd.random() * 2 * math.pi
            ln = 1 + rnd.random() * 2.5
            x1, y1 = max(0, min(15, x0 + math.cos(a) * ln)), max(0, min(15, y0 + math.sin(a) * ln))
            segs.append((x0, y0, x1, y1))
            pts.append((x1, y1))
        m = [False] * (N * N)
        for x0, y0, x1, y1 in segs[:int(len(segs) * (stage + 1) / 10)]:
            n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
            for k in range(n + 1):
                t = k / n
                m[at(round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t))] = True
        from PIL import Image
        im = Image.new('RGBA', (N, N))
        im.putdata([(24, 22, 20, 190) if m[i] else (0, 0, 0, 0) for i in range(N * N)])
        return im
    return gen


def shards(name, spec):
    """amethyst: pointed crystals out of the bottom edge, light down the middle"""
    def gen(ctx):
        f, m = [0.0] * (N * N), [False] * (N * N)
        for cx, h, hw in spec:
            for k in range(h):
                y = 15 - k
                w = hw * min(1.0, (h - k) / 2.5)
                for x in range(N):
                    d = abs(x + 0.5 - cx)
                    if d <= w + 0.35:
                        m[at(x, y)] = True
                        f[at(x, y)] = 1 - d / max(w, 0.5) + 0.02 * k
        c = canvas(ctx, name)
        c.part(f, ctx.px(name, 'amethyst_block'), m)
        return c.image()
    return gen


def spawner(ctx):
    bars = {0, 1, 5, 10, 14, 15}
    m = [i % N in bars or i // N in bars for i in range(N * N)]
    f = [0.2 if i % N in (0, 15) or i // N in (0, 15) else 0.9 if (i % N in bars and i // N in bars) else 0.55
         for i in range(N * N)]
    w = white(rng('spawner'))
    c = canvas(ctx, 'spawner')
    c.part([f[i] + 0.1 * w[i] for i in range(N * N)], ctx.px('spawner', 'iron_bars'), m)
    return c.image()


def roots(name, seed, count, base=None):
    def gen(ctx):
        rnd = rng(name, seed)
        m, f = [False] * (N * N), [0.0] * (N * N)
        for s in range(count):
            vertical = s % 2 == 0
            a, drift = rnd.randrange(N), rnd.choice((-1, 1))
            for t in range(N):
                if rnd.random() < 0.35:
                    a = (a + drift) % N
                if rnd.random() < 0.12:
                    drift = -drift
                for off, v in ((0, 0.9), (1, 0.3)):
                    x, y = ((a + off) % N, t) if vertical else (t, (a + off) % N)
                    m[at(x, y)] = True
                    f[at(x, y)] = v
        c = canvas(ctx, name)
        if base:
            c.px = list(ctx.out(base).get_flattened_data())
        c.part(f, ctx.px(name, 'mangrove_roots', 'mangrove_log'), m)
        return c.image()
    return gen


def propagule(ctx):
    name = 'mangrove_propagule_hanging'
    src = ctx.px(name, 'mangrove_leaves', 'oak_leaves')
    leaf = select(src, lambda p: p[1] > p[0]) or src
    pod = select(src, lambda p: p[0] >= p[1]) or src
    tuft = [i // N <= 2 and 5 <= i % N <= 10 and not (i // N == 0 and i % N in (5, 10)) for i in range(N * N)]
    stem = [i % N in (7, 8) and 3 <= i // N <= 15 for i in range(N * N)]
    w = white(rng(name))
    c = canvas(ctx, name)
    c.part([0.5 + 0.3 * w[i] for i in range(N * N)], leaf, tuft)
    c.part([0.9 - (i // N) / 16 - 0.3 * (i % N == 8) for i in range(N * N)], leaf + pod, stem)
    return c.image()


def orb(name, rad):
    def gen(ctx):
        f, m = [0.0] * (N * N), [False] * (N * N)
        for i in range(N * N):
            x, y = i % N, i // N
            d = math.hypot(x - 7.5, y - 7.5)
            if d <= rad + 0.3:
                m[i] = True
                f[i] = 0 if d > rad - 0.7 else 1 - math.hypot(x - 5.5, y - 5.5) / (rad * 1.6)
        c = canvas(ctx, name)
        c.part(f, ctx.px(name), m)
        return c.image()
    return gen


def snowy_side(ctx):
    name = 'grass_block_snow'
    rnd = rng(name)
    edge = fbm(rnd, [(8, 16, 0.6), (4, 16, 0.4)])
    dep = [2 + round(2 * edge[x]) for x in range(N)]
    mask = [(i // N) < dep[i % N] for i in range(N * N)]
    c = canvas(ctx, name)
    c.px = list(ctx.out('dirt').get_flattened_data())
    c.part(natural.smooth_f(4)(rnd), ctx.px('snow'), mask)
    return c.image()


def redstone_torch(ctx):
    name = 'redstone_torch'
    src = ctx.px(name, 'torch')
    stick = [i % N in (7, 8) and 8 <= i // N <= 15 for i in range(N * N)]
    head = [i % N in (7, 8) and 5 <= i // N <= 7 for i in range(N * N)]
    brown = select(src, lambda p: p[0] > p[2] + 15 and lum(p) < 140 and p[0] - p[1] < 60) or ctx.px('oak_planks')
    red = select(src, lambda p: p[0] > p[1] + 60) or [(200, 20, 10, 255), (255, 60, 40, 255), (120, 10, 8, 255)]
    c = canvas(ctx, name)
    c.part([-(i // N) * 0.1 + 0.4 * (i % N == 7) for i in range(N * N)], brown, stick)
    c.part([1 - (i // N - 5) * 0.3 + 0.2 * (i % N == 7) for i in range(N * N)], red, head)
    return c.image()


# HUD icons: 9 by 9 marks (our own shapes), coloured from the style's HUD where it has one
HUD = {
    'hud_heart': (['..##.##..', '.#######.', '#########', '#########', '.#######.', '..#####..', '...###...',
                   '....#....', '.........'], [(226, 34, 34, 255), (255, 176, 176, 255), (120, 10, 10, 255)]),
    'hud_food': (['.....##..', '....####.', '...######', '..######.', '.#####...', '.###.....', '#.#......',
                  '##.......', '.........'], [(200, 116, 44, 255), (240, 192, 128, 255), (110, 60, 20, 255)]),
    'hud_air': (['..####...', '.#....#..', '#..#...#.', '#.#....#.', '#......#.', '#......#.', '.#....#..',
                 '..####...', '.........'], [(102, 170, 255, 255), (255, 255, 255, 255), (40, 80, 160, 255)]),
}


def hud(name, empty=False):
    def gen(ctx):
        rows, fallback = HUD[name]
        src = (ctx.px(name) if name != 'hud_air' else None) or fallback      # a bubble: ours, see-through
        if empty:
            src = [(58, 16, 16, 255), (40, 10, 10, 255)] if name == 'hud_heart' else [(58, 36, 16, 255), (40, 24, 10, 255)]
        m = [rows[i // 9][i % 9] == '#' for i in range(81)]
        f = []
        for i in range(81):
            x, y = i % 9, i // 9
            edge = not all(0 <= xx < 9 and 0 <= yy < 9 and m[yy * 9 + xx]
                           for xx, yy in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
            f.append(0.0 if edge and name != 'hud_air' else 1.0 if (y < 3 and x < 4) else 0.55 - 0.03 * (x + y))
        c = Canvas(ctx.style, name + ('_empty' if empty else ''), 9, 9)
        c.part(f, src, m, grain=0, flat=True)
        return c.image()
    return gen


def register(reg):
    for s in range(10):
        reg['destroy_stage_%d' % s] = crack(s)
    reg['small_amethyst_bud'] = shards('small_amethyst_bud', [(8, 4, 1.6), (5.5, 3, 1.0), (10.5, 2, 0.9)])
    reg['medium_amethyst_bud'] = shards('medium_amethyst_bud', [(8, 7, 1.8), (5, 4, 1.2), (11, 5, 1.2)])
    reg['large_amethyst_bud'] = shards('large_amethyst_bud', [(8, 10, 2.0), (4.5, 6, 1.3), (11.5, 7, 1.3)])
    reg['amethyst_cluster'] = shards('amethyst_cluster', [(8, 13, 2.2), (4, 9, 1.5), (12, 10, 1.5), (6, 6, 1.1),
                                                          (10.5, 6, 1.1)])
    reg['spawner'] = spawner
    reg['mangrove_roots'] = roots('mangrove_roots', 7, 6)
    reg['mangrove_roots_top'] = roots('mangrove_roots_top', 11, 6)
    reg['muddy_mangrove_roots_side'] = roots('muddy_mangrove_roots_side', 13, 5, 'mud')
    reg['muddy_mangrove_roots_top'] = roots('muddy_mangrove_roots_top', 17, 5, 'mud')
    reg['mangrove_propagule_hanging'] = propagule
    reg['item_ender_pearl'] = orb('item_ender_pearl', 6)
    reg['item_slime_ball'] = orb('item_slime_ball', 5)
    reg['grass_block_snow'] = snowy_side
    reg['redstone_torch'] = redstone_torch
    for n in HUD:
        reg[n] = hud(n)
        if n != 'hud_air':
            reg[n + '_empty'] = hud(n, True)
