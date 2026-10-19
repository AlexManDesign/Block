"""Made things: wool, glass, glazed terracotta, beds, TNT, bookshelf, crafting table, furnace, chests,
pumpkins, melon, hay, cactus, ladder, rails, bars, cobweb, torch, flower pot, mushroom blocks, ice."""
import math
from core import (N, at, fbm, white, mix, cells, rims, rng, Canvas, select, lum, COLOURS)
import natural
import wood


def mask_of(rows_):
    rows_ = [r for r in rows_.strip('\n').split('\n')]
    return [rows_[i // N][i % N] != '.' if i // N < len(rows_) and i % N < len(rows_[i // N]) else False
            for i in range(N * N)]


def canvas(ctx, name):
    return Canvas(ctx.style, name, ref=ctx.ref(name))


# ---- wool, glass, glazed terracotta ------------------------------------------------------------

def knit_f(rnd):
    """knitted wool: rows of little V stitches"""
    nz, w = fbm(rnd, [(8, 8, 0.5), (4, 4, 0.5)]), white(rnd)
    f = []
    for i in range(N * N):
        x, y = i % N, i // N
        st = 0.3 if (x + (y // 2) * 2) % 4 in (0, 3) and y % 2 == 0 else 0.3 if (x + (y // 2) * 2) % 4 in (1, 2) and y % 2 else 0
        f.append(0.4 * nz[i] + 0.2 * w[i] + st)
    return f


def wool(name):
    def gen(ctx):
        c = canvas(ctx, name)
        c.part(knit_f(rng(name)), ctx.px(name, 'white_wool'))
        return c.image()
    return gen


def glass(name, stained):
    """a pane: a light rim, a couple of glints, the middle clear (or tinted and see-through)"""
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'glass')
        rim = [i % N in (0, N - 1) or i // N in (0, N - 1) for i in range(N * N)]
        glint = [False] * (N * N)
        for (x0, y0, n) in ((3, 3, 3), (10, 9, 2), (11, 3, 1)):
            for k in range(n):
                glint[at(x0 + k, y0 + k)] = True
        mid = [not rim[i] and not glint[i] for i in range(N * N)]
        c = canvas(ctx, name)
        light = sorted((p for p in src if p[3] > 0), key=lum)
        edge = light[len(light) // 2:] or light
        w = white(rnd)
        c.part([1 - 0.3 * (i % N == N - 1 or i // N == N - 1) + 0.1 * w[i] for i in range(N * N)], edge, rim)
        c.part([0.9 + 0.1 * w[i] for i in range(N * N)], edge, glint)
        if stained:
            alpha = [p[3] for p in src if 0 < p[3] < 255]
            a = round(sum(alpha) / len(alpha)) if alpha else 140
            c.part(mix((0.7, fbm(rnd, [(8, 8, 1.0)])), (0.3, w)), light, mid, alpha=max(60, min(220, a)))
        return c.image()
    return gen


def glazed(name):
    """a tile with a motif of its own in four even tones, turned a quarter at each corner so that
    tiles meet in patterns"""
    def gen(ctx):
        rnd = rng(name)
        q = [[1] * 8 for _ in range(8)]
        for _ in range(2):           # bands of arcs
            cx, cy, r = rnd.choice((0, 8)), rnd.choice((0, 8)), rnd.uniform(3, 7)
            lvl = rnd.choice((0, 2, 3))
            for y in range(8):
                for x in range(8):
                    if abs(math.hypot(x + 0.5 - cx, y + 0.5 - cy) - r) < 1.0:
                        q[y][x] = lvl
        for _ in range(2):           # a bar and a block
            x0, y0 = rnd.randrange(6), rnd.randrange(6)
            lvl = rnd.choice((0, 3))
            for y in range(y0, y0 + rnd.randrange(1, 3)):
                for x in range(x0, x0 + rnd.randrange(2, 4)):
                    q[y % 8][x % 8] = lvl
        f = [0.0] * (N * N)
        for y in range(8):
            for x in range(8):
                v = q[y][x]
                f[at(x, y)] = f[at(15 - y, x)] = f[at(15 - x, 15 - y)] = f[at(y, 15 - x)] = v
        c = canvas(ctx, name)
        c.part(f, ctx.px(name, 'white_glazed_terracotta'), flat=True)
        return c.image()
    return gen


# ---- beds ---------------------------------------------------------------------------------------

def bed(name, colour, part):
    """bed tops: the head with a pillow, the foot with a quilted blanket; the side is the frame"""
    def gen(ctx):
        rnd = rng(name)
        cloth = ctx.px((colour or 'red') + '_wool', 'red_wool')
        planks = ctx.px('oak_planks')
        pillow = ctx.px('white_wool')
        c = canvas(ctx, name)
        w = white(rnd)
        if part == 'side':
            c.part(wood.planks_f(rnd), planks)
            band = [6 <= i // N < 10 for i in range(N * N)]
            c.part([0.3 * w[i] - 0.2 * (i // N == 9) for i in range(N * N)], sorted(planks, key=lum)[:40], band)
            return c.image()
        quilt = [0.5 + 0.2 * w[i] + (0.35 if (i % 4 == 0) != (i // N % 4 == 0) else 0)
                 - (0.4 if i % 4 == 3 or i // N % 4 == 3 else 0) for i in range(N * N)]
        rim = [i % N in (0, N - 1) for i in range(N * N)]
        if part == 'head':
            top = [i // N < 6 and not rim[i] for i in range(N * N)]
            ids = [1 if top[i] else 0 for i in range(N * N)]
            r = rims(ids, 0.3, 0.4, wrap=False)
            c.part(quilt, cloth, [not top[i] and not rim[i] for i in range(N * N)])
            c.part([0.6 + r[i] + 0.1 * w[i] for i in range(N * N)], pillow, top)
        else:
            c.part(quilt, cloth, [not r for r in rim])
        c.part([0.5 + 0.2 * w[i] for i in range(N * N)], cloth, rim)
        return c.image()
    return gen


# ---- TNT, bookshelf, crafting table, furnace, chests ---------------------------------------------

FONT = {'T': ['###', '.#.', '.#.', '.#.', '.#.'], 'N': ['#.#', '###', '###', '#.#', '#.#']}


def tnt(name, part):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'tnt_side')
        red = select(src, lambda p: p[0] > p[1] + 40) or src
        pale = select(src, lambda p: lum(p) > 150) or src
        dark = sorted(src, key=lum)[:12]
        c = canvas(ctx, name)
        w = white(rnd)
        if part == 'side':
            sticks = [0.5 + 0.3 * (i % 4 == 0) - 0.45 * (i % 4 == 3) + 0.15 * w[i] for i in range(N * N)]
            band = [5 <= i // N <= 10 for i in range(N * N)]
            c.part(sticks, red, [not b for b in band])
            c.part([0.6 + 0.2 * w[i] - 0.3 * (i // N in (5, 10)) for i in range(N * N)], pale, band)
            letters = [False] * (N * N)
            for k, ch in enumerate('TNT'):
                for yy, row in enumerate(FONT[ch]):
                    for xx, v in enumerate(row):
                        if v == '#':
                            letters[at(2 + k * 4 + xx, 6 + yy)] = True
            c.part([0.3 * w[i] for i in range(N * N)], dark, letters)
        else:
            ends = [0.0] * (N * N)
            for i in range(N * N):
                x, y = i % N, i // N
                d = math.hypot((x % 5) - 2, (y % 5) - 2)
                ends[i] = 0.9 - 0.25 * d + 0.15 * w[i]
            c.part(ends, red)
            if part == 'top':
                fuse = [abs(i % N - 7.5) < 1 and abs(i // N - 7.5) < 1 for i in range(N * N)]
                c.part([0.2 * w[i] for i in range(N * N)], dark, fuse)
        return c.image()
    return gen


def bookshelf(name):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'bookshelf')
        planks = ctx.px('oak_planks')
        books = select(src, lambda p: max(p[:3]) - min(p[:3]) > 40) or src
        c = canvas(ctx, name)
        shelf = [i // N in (0, 7, 8, 15) for i in range(N * N)]
        c.part(wood.planks_f(rnd, 4), planks, shelf)
        w = white(rnd)
        for row0 in (1, 9):
            x = 0
            while x < N:
                bw = rnd.choice((1, 2, 2, 3))
                top = row0 + rnd.choice((0, 0, 1, 2))
                col = rnd.choice(books)
                for xx in range(x, min(N, x + bw)):
                    for y in range(top, row0 + 6):
                        shade = 1.1 - 0.12 * (xx - x) - 0.03 * (y - top) + 0.06 * w[at(xx, y)]
                        c.px[at(xx, y)] = tuple(max(0, min(255, int(col[t] * shade))) for t in range(3)) + (255,)
                    for y in range(row0, top):
                        c.px[at(xx, y)] = tuple(int(v * 0.35) for v in planks[0][:3]) + (255,)
                x += bw
        return c.image()
    return gen


def table(name, part):
    """crafting table: a framed top with a grid; the front with tools hung on the planks"""
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'crafting_table_front')
        planks = ctx.px('oak_planks')
        c = canvas(ctx, name)
        w = white(rnd)
        if part == 'top':
            f = wood.planks_f(rnd)
            for i in range(N * N):
                x, y = i % N, i // N
                if x in (0, 15) or y in (0, 15):
                    f[i] = -0.6
                elif x in (5, 10) or y in (5, 10):
                    f[i] = -0.3
            c.part(f, src)
            return c.image()
        c.part(wood.planks_f(rnd), planks)
        topband = [i // N < 3 for i in range(N * N)]
        c.part([0.5 + 0.3 * (i // N == 0) - 0.4 * (i // N == 2) + 0.15 * w[i] for i in range(N * N)], src, topband)
        metal = select(src, lambda p: abs(p[0] - p[2]) < 20 and lum(p) > 90) or src
        handle = sorted(planks, key=lum)[:20]
        tools = mask_of('''
................
................
................
....##....###...
....##.....#....
...####....#....
....##.....#....
....#......#....
....#......#....
....#......#....
....#...........
................
................
................
................
................''')
        heads = [tools[i] and (i // N <= 5) for i in range(N * N)]
        sticks = [tools[i] and not heads[i] for i in range(N * N)]
        c.part([0.3 * w[i] for i in range(N * N)], handle, sticks)
        c.part([0.5 + 0.3 * w[i] - 0.05 * (i % N) for i in range(N * N)], metal, heads)
        return c.image()
    return gen


def furnace(name, part):
    def gen(ctx):
        rnd = rng(name)
        stone = ctx.px('furnace_top', 'smooth_stone', 'stone')
        src = ctx.px(name, 'furnace_front')
        c = canvas(ctx, name)
        f = natural.smooth_f(6, 0.35)(rnd)
        for i in range(N * N):
            x, y = i % N, i // N
            if x == 0 or y == 0:
                f[i] += 0.4
            elif x == N - 1 or y == N - 1:
                f[i] -= 0.4
        c.part(f, stone)
        if part == 'top':
            return c.image()
        w = white(rnd)
        grate = [3 <= i % N <= 12 and 2 <= i // N <= 4 for i in range(N * N)]
        mouth = [3 <= i % N <= 12 and 8 <= i // N <= 12 for i in range(N * N)]
        dark = sorted(src, key=lum)[:30]
        c.part([0.3 * w[i] + 0.4 * ((i % N) % 2) for i in range(N * N)], dark, grate)
        if part == 'on':
            fire = select(src, lambda p: p[0] > 180 and p[2] < 120) or dark
            c.part([(i // N) / 16 + 0.3 * w[i] for i in range(N * N)], fire, mouth)
        else:
            c.part([0.2 * w[i] for i in range(N * N)], dark, mouth)
        return c.image()
    return gen


def chest(name, part):
    """wooden chest: planks between dark bands, a latch on the front"""
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'chest_front', 'oak_planks')
        planks = ctx.px('oak_planks')
        c = canvas(ctx, name)
        f = wood.planks_f(rnd)
        c.part(f, planks)
        dark = sorted(src, key=lum)[:max(10, len(src) // 6)]
        w = white(rnd)
        if part in ('front', 'side', 'front_l', 'front_r'):
            band = [i // N in (0, 5, 6, 15) or (i % N in (0, 15) and part == 'side') for i in range(N * N)]
            if part == 'front_l':
                band = [band[i] or i % N == 0 for i in range(N * N)]
            if part == 'front_r':
                band = [band[i] or i % N == 15 for i in range(N * N)]
            if part in ('front', 'side'):
                band = [band[i] or i % N in (0, 15) for i in range(N * N)]
            c.part([0.3 * w[i] for i in range(N * N)], dark, band)
            if part in ('front', 'front_l', 'front_r'):
                lx = {'front': 7, 'front_l': 14, 'front_r': 0}[part]
                metal = select(src, lambda p: abs(p[0] - p[2]) < 25 and lum(p) > 100) or src
                latch = [lx <= i % N <= lx + 1 and 4 <= i // N <= 7 for i in range(N * N)]
                c.part([0.6 - 0.1 * (i // N) + 0.2 * w[i] for i in range(N * N)], metal, latch)
        else:
            rim = [i % N in (0, 15) or i // N in (0, 15) for i in range(N * N)]
            if part == 'top_l':
                rim = [i // N in (0, 15) or i % N == 0 for i in range(N * N)]
            if part == 'top_r':
                rim = [i // N in (0, 15) or i % N == 15 for i in range(N * N)]
            c.part([0.3 * w[i] for i in range(N * N)], dark, rim)
        return c.image()
    return gen


# ---- pumpkin, melon, hay, cactus ---------------------------------------------------------------

def ribs_f(rnd, period=4):
    """ribs running up (pumpkin, melon, cactus)"""
    w = white(rnd)
    nz = fbm(rnd, [(4, 16, 0.6), (2, 8, 0.4)])
    return [0.5 + 0.35 * math.cos(2 * math.pi * ((i % N) + 0.5) / period) + 0.25 * nz[i] + 0.1 * w[i] for i in range(N * N)]


FACE = '''
................
................
................
...##......##...
..####....####..
..####....####..
................
................
...##########...
..############..
..##.##..##.##..
................
................
................
................
................'''


def pumpkin(name, part):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px('pumpkin_side', name)
        c = canvas(ctx, name)
        if part == 'top':
            f = [0.0] * (N * N)
            w = white(rnd)
            for i in range(N * N):
                x, y = i % N + 0.5 - 8, i // N + 0.5 - 8
                a = math.atan2(y, x)
                f[i] = 0.5 + 0.3 * math.cos(a * 6) * min(1, math.hypot(x, y) / 4) + 0.1 * w[i]
            c.part(f, src)
            stem = [abs(i % N - 7.5) < 1.5 and abs(i // N - 7.5) < 1.5 for i in range(N * N)]
            green = select(ctx.px(name, 'pumpkin_top') or src, lambda p: p[1] > p[0]) or src
            c.part([0.2 * w[i] for i in range(N * N)], green, stem)
            return c.image()
        c.part(ribs_f(rnd), src)
        if part in ('carved', 'lit'):
            face = mask_of(FACE)
            fsrc = ctx.px(name)
            if part == 'lit':
                glow = select(fsrc, lambda p: p[0] > 200 and p[1] > 150) if fsrc else None
                c.part([(i // N) / 16 for i in range(N * N)], glow or src, face)
            else:
                c.part([0.1 * (i // N) / 16 for i in range(N * N)], sorted(fsrc or src, key=lum)[:20], face)
        return c.image()
    return gen


def melon(name, part):
    def gen(ctx):
        rnd = rng(name)
        c = canvas(ctx, name)
        if part == 'top':
            f = [0.0] * (N * N)
            w = white(rnd)
            for i in range(N * N):
                x, y = i % N + 0.5 - 8, i // N + 0.5 - 8
                f[i] = 0.5 + 0.35 * math.cos(math.atan2(y, x) * 8) + 0.15 * w[i]
            c.part(f, ctx.px(name, 'melon_side'))
        else:
            c.part(ribs_f(rnd, 5), ctx.px(name))
        return c.image()
    return gen


def hay(name, part):
    def gen(ctx):
        rnd = rng(name)
        c = canvas(ctx, name)
        w = white(rnd)
        if part == 'side':
            straw = fbm(rnd, [(16, 1, 0.5), (8, 1, 0.5)])
            f = [0.4 + 0.4 * straw[i] + 0.2 * w[i] for i in range(N * N)]
            c.part(f, ctx.px(name))
            ropes = [i // N in (3, 4, 11, 12) for i in range(N * N)]
            dark = select(ctx.px(name), lambda p: p[0] > p[2] + 40 and lum(p) < 130) or sorted(ctx.px(name), key=lum)[:30]
            c.part([0.5 + 0.3 * (i // N in (3, 11)) + 0.2 * w[i] for i in range(N * N)], dark, ropes)
        else:
            c.part(mix((0.5, natural.sand_f(rnd)), (0.5, w)), ctx.px(name))
        return c.image()
    return gen


def cactus(name, part):
    def gen(ctx):
        rnd = rng(name)
        c = canvas(ctx, name)
        src = ctx.px(name, 'cactus_side')
        if part == 'side':
            f = ribs_f(rnd, 4)
            for _ in range(10):
                x, y = rnd.randrange(N), rnd.randrange(N)
                f[at(x, y)] = 1.6
            c.part(f, src)
        else:
            w = white(rnd)
            f = [0.5 + 0.3 * math.cos(max(abs(i % N - 7.5), abs(i // N - 7.5)) * 1.2) + 0.1 * w[i] for i in range(N * N)]
            c.part(f, src)
        return c.image()
    return gen


# ---- see-through bits: ladder, rails, bars, cobweb, torch, flower pot ----------------------------

def ladder(name):
    def gen(ctx):
        rnd = rng(name)
        rails = [i % N in (2, 3, 12, 13) for i in range(N * N)]
        rungs = [3 < i % N < 12 and i // N % 4 == 1 for i in range(N * N)]
        c = canvas(ctx, name)
        src = ctx.px(name, 'oak_planks')
        w = white(rnd)
        c.part([0.6 - 0.3 * (i % N in (3, 13)) + 0.2 * w[i] for i in range(N * N)], src, rails)
        c.part([0.6 + 0.2 * w[i] for i in range(N * N)], src, rungs)
        return c.image()
    return gen


def rail(name, corner):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'rail')
        wood_px = select(src, lambda p: p[0] > p[2] + 20) or ctx.px('oak_planks')
        metal = select(src, lambda p: abs(p[0] - p[2]) < 20) or src
        c = canvas(ctx, name)
        w = white(rnd)
        ties = [0] * (N * N)
        steel = [False] * (N * N)
        for i in range(N * N):
            x, y = i % N, i // N
            if not corner:
                ties[i] = y % 4 in (1, 2) and 1 <= x <= 14
                steel[i] = x in (2, 3, 12, 13)
            else:
                d = math.hypot(x + 0.5 - 16, y + 0.5 - 16)
                ties[i] = (int(math.degrees(math.atan2(16 - y, 16 - x)) // 15) % 2 == 0) and 2 <= d <= 15
                steel[i] = abs(d - 4.5) < 0.8 or abs(d - 12.5) < 0.8
        c.part([0.5 + 0.2 * w[i] for i in range(N * N)], wood_px, [ties[i] and not steel[i] for i in range(N * N)])
        c.part([0.7 - 0.3 * ((i % N) % 2) + 0.1 * w[i] for i in range(N * N)], metal, steel)
        return c.image()
    return gen


def bars(name):
    def gen(ctx):
        rnd = rng(name)
        m = [i % N in (1, 2, 6, 7, 11, 12) or i // N in (1, 2, 14) for i in range(N * N)]
        c = canvas(ctx, name)
        w = white(rnd)
        c.part([0.8 - 0.4 * (i % N in (2, 7, 12)) - 0.3 * (i // N == 2) + 0.1 * w[i] for i in range(N * N)],
               ctx.px(name, 'iron_block'), m)
        return c.image()
    return gen


def line(m, x0, y0, x1, y1):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for k in range(n + 1):
        t = k / n
        x, y = round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t)
        if 0 <= x < N and 0 <= y < N:
            m[y * N + x] = True


def cobweb(name):
    """threads: spokes out of the middle and two rings of sagging strands"""
    def gen(ctx):
        m = [False] * (N * N)
        ends = [(0, 0), (8, 0), (15, 0), (15, 8), (15, 15), (8, 15), (0, 15), (0, 8)]
        for x, y in ends:
            line(m, 7.5, 7.5, x, y)
        for r in (3, 6):
            pts = [(7.5 + (x - 7.5) * r / 8, 7.5 + (y - 7.5) * r / 8) for x, y in ends]
            for k in range(8):
                (x0, y0), (x1, y1) = pts[k], pts[(k + 1) % 8]
                line(m, x0, y0, x1, y1)
        c = canvas(ctx, name)
        c.part([0.8 - 0.05 * math.hypot(i % N - 8, i // N - 8) for i in range(N * N)], ctx.px(name, 'white_wool'), m)
        return c.image()
    return gen


def torch(name):
    def gen(ctx):
        src = ctx.px(name, 'torch')
        stick = [i % N in (7, 8) and 8 <= i // N <= 15 for i in range(N * N)]
        head = [i % N in (7, 8) and 6 <= i // N <= 7 for i in range(N * N)]
        tip = [i % N in (7, 8) and i // N == 5 for i in range(N * N)]
        c = canvas(ctx, name)
        brown = select(src, lambda p: p[0] > p[2] + 15 and lum(p) < 140) or ctx.px('oak_planks')
        flame = select(src, lambda p: p[0] > 200 and p[1] > 120) or src
        c.part([-(i // N) * 0.1 + 0.4 * (i % N == 7) for i in range(N * N)], brown, stick)
        c.part([0.5 + 0.3 * (i % N == 7) for i in range(N * N)], flame, head)
        c.part([1.0 for _ in range(N * N)], flame, tip)
        return c.image()
    return gen


def flower_pot(name):
    """the pot: its sides on the bottom six rows (x 5..10), its top round the soil in the middle
    (the spots the pot model reads); the icon is the pot seen from the side, wider at the rim"""
    def gen(ctx):
        src = ctx.px('flower_pot', name)
        soil = ctx.px('dirt')
        w = white(rng(name))
        c = canvas(ctx, name)
        if name.endswith('_icon'):
            pot = [False] * (N * N)
            for y in range(7, 16):
                half = 4 if y <= 8 else 3 if y < 15 else 2
                for x in range(8 - half, 8 + half):
                    pot[at(x, y)] = True
            rim = [pot[i] and i // N in (7, 8) for i in range(N * N)]
            c.part([0.6 - 0.07 * (i % N - 4) + 0.1 * w[i] for i in range(N * N)], src, [p and not r for p, r in zip(pot, rim)])
            c.part([0.8 - 0.05 * (i % N - 4) + 0.1 * w[i] - 0.3 * (i // N == 8) for i in range(N * N)], src, rim)
            return c.image()
        side = [5 <= i % N <= 10 and 10 <= i // N <= 15 for i in range(N * N)]
        top = [5 <= i % N <= 10 and 5 <= i // N <= 9 for i in range(N * N)]
        inner = [6 <= i % N <= 9 and 6 <= i // N <= 9 for i in range(N * N)]
        c.part([0.6 - 0.06 * (i % N - 5) + 0.1 * w[i] + 0.25 * (i // N == 10) for i in range(N * N)], src, side)
        c.part([0.7 + 0.1 * w[i] for i in range(N * N)], src, [t and not n for t, n in zip(top, inner)])
        c.part([0.3 * w[i] for i in range(N * N)], soil, inner)
        return c.image()
    return gen


def mushroom_block(name, kind):
    """mushroom caps with spots, the pale stem, the spongy inside"""
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name)
        c = canvas(ctx, name)
        if kind == 'red':
            red = select(src, lambda p: p[0] > p[1] + 40) or src
            pale = select(src, lambda p: lum(p) > 170) or src
            c.part(mix((0.6, fbm(rnd, [(8, 8, 1.0)])), (0.4, white(rnd))), red)
            ids, d1, d2, n = cells(rnd, 6, 0.9)
            spots = [d1[i] < 1.7 for i in range(N * N)]
            c.part([1 - 0.2 * d1[i] for i in range(N * N)], pale, spots)
        else:
            c.part(natural.smooth_f(10, 0.4)(rnd), src)
        return c.image()
    return gen


def register(reg):
    for col in COLOURS:
        reg[col + '_wool'] = wool(col + '_wool')
        reg[col + '_stained_glass'] = glass(col + '_stained_glass', True)
        reg[col + '_glazed_terracotta'] = glazed(col + '_glazed_terracotta')
        reg['bed_top_' + col] = bed('bed_top_' + col, col, 'head')
        reg['bed_foot_top_' + col] = bed('bed_foot_top_' + col, col, 'foot')
        reg['bed_side_' + col] = bed('bed_side_' + col, col, 'side')
    reg['bed_top'] = bed('bed_top', 'red', 'head')
    reg['bed_foot_top'] = bed('bed_foot_top', 'red', 'foot')
    reg['bed_side'] = bed('bed_side', 'red', 'side')
    reg['glass'] = glass('glass', False)
    reg['tinted_glass'] = glass('tinted_glass', True)
    reg['tnt_side'] = tnt('tnt_side', 'side')
    reg['tnt_top'] = tnt('tnt_top', 'top')
    reg['tnt_bottom'] = tnt('tnt_bottom', 'bottom')
    reg['bookshelf'] = bookshelf('bookshelf')
    reg['bookshelf_end'] = wood.planks('bookshelf_end')
    reg['crafting_table_top'] = table('crafting_table_top', 'top')
    reg['crafting_table_front'] = table('crafting_table_front', 'front')
    reg['furnace_top'] = furnace('furnace_top', 'top')
    reg['furnace_front'] = furnace('furnace_front', 'off')
    reg['furnace_front_on'] = furnace('furnace_front_on', 'on')
    for p in ('front', 'side', 'top', 'front_l', 'front_r', 'top_l', 'top_r'):
        reg['chest_' + p] = chest('chest_' + p, p)
    reg['pumpkin_side'] = pumpkin('pumpkin_side', 'side')
    reg['pumpkin_top'] = pumpkin('pumpkin_top', 'top')
    reg['carved_pumpkin'] = pumpkin('carved_pumpkin', 'carved')
    reg['jack_o_lantern'] = pumpkin('jack_o_lantern', 'lit')
    reg['melon_side'] = melon('melon_side', 'side')
    reg['melon_top'] = melon('melon_top', 'top')
    reg['hay_block_side'] = hay('hay_block_side', 'side')
    reg['hay_block_top'] = hay('hay_block_top', 'top')
    reg['cactus_side'] = cactus('cactus_side', 'side')
    reg['cactus_top'] = cactus('cactus_top', 'top')
    reg['ladder'] = ladder('ladder')
    reg['rail'] = rail('rail', False)
    reg['rail_corner'] = rail('rail_corner', True)
    reg['iron_bars'] = bars('iron_bars')
    reg['cobweb'] = cobweb('cobweb')
    reg['torch'] = torch('torch')
    reg['flower_pot'] = flower_pot('flower_pot')
    reg['flower_pot_icon'] = flower_pot('flower_pot_icon')
    reg['red_mushroom_block'] = mushroom_block('red_mushroom_block', 'red')
    reg['brown_mushroom_block'] = mushroom_block('brown_mushroom_block', 'brown')
    reg['mushroom_stem'] = mushroom_block('mushroom_stem', 'stem')
    reg['ice'] = glass_like_ice('ice')


def glass_like_ice(name):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'packed_ice')
        alpha = [p[3] for p in src if p[3] > 0]
        a = round(sum(alpha) / len(alpha)) if alpha else 200
        f = natural.smooth_f(3, 0.15)(rnd)
        for _ in range(3):
            x, y = rnd.randrange(N), rnd.randrange(N)
            for k in range(rnd.randrange(3, 7)):
                f[at(x + k, y - k)] = 1.4
        c = canvas(ctx, name)
        c.part(f, src, alpha=a)
        return c.image()
    return gen
