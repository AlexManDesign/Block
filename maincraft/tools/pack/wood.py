"""Wood: planks, bark, log ends, stripped wood, leaves, saplings, doors and trapdoors."""
import math
from core import (N, at, fbm, white, mix, cells, rims, rng, Canvas, select, lum, value_noise)

TREES = ['oak', 'spruce', 'birch', 'jungle', 'acacia', 'dark_oak', 'mangrove', 'cherry', 'pale_oak']
STEMS = ['crimson', 'warped']
PLANKS = TREES + STEMS + ['bamboo']


def planks_f(rnd, boards=4):
    """boards laid flat: each its own tone, a lit top edge, a dark seam under it, one butt joint
    per board (staggered), fine grain running along"""
    h = N // boards
    f = [0.0] * (N * N)
    grain = fbm(rnd, [(16, 1, 0.4), (8, 1, 0.35), (4, 1, 0.25)])
    w = white(rnd)
    joints = []
    for b in range(boards):
        tone = rnd.uniform(0.35, 0.7)
        j = (rnd.randrange(3, 13) + b * 8) % N
        joints.append(j)
        for y in range(b * h, b * h + h):
            for x in range(N):
                i = at(x, y)
                v = tone + 0.35 * grain[i] + 0.12 * w[i]
                if y == b * h:
                    v += 0.18
                if y == b * h + h - 1:
                    v = -1.0
                elif x == j:
                    v = -0.8
                elif x == (j - 1) % N:
                    v -= 0.15
                elif x == (j + 1) % N:
                    v += 0.1
                f[i] = v
    return f


def bark_f(rnd):
    """bark: ridges running up the trunk, dark furrows between them"""
    cols = value_noise(2, 16, rnd)
    fine = fbm(rnd, [(1, 8, 0.5), (1, 4, 0.5)])
    w = white(rnd)
    f = []
    for i in range(N * N):
        v = 0.55 * cols[i] + 0.3 * fine[i] + 0.15 * w[i]
        f.append(-0.6 if fine[i] < 0.28 else v)
    return f


def birch_f(rnd):
    """pale bark with dark marks running across"""
    f = mix((0.7, fbm(rnd, [(4, 8, 0.5), (2, 4, 0.5)])), (0.3, white(rnd)))
    f = [0.6 + 0.4 * v for v in f]
    for _ in range(7):
        x, y, n = rnd.randrange(N), rnd.randrange(N), rnd.randrange(2, 6)
        for k in range(n):
            f[at(x + k, y)] = -1.0 + 0.1 * k
        if rnd.random() < 0.4:
            f[at(x + 1, y + 1)] = -0.6
    return f


def fibre_f(rnd):
    """stripped wood: smooth fibres running up"""
    return mix((0.7, fbm(rnd, [(4, 16, 0.4), (2, 8, 0.35), (1, 4, 0.25)])), (0.3, white(rnd)))


def rings_f(rnd, cx=7.5, cy=7.5):
    """growth rings of a cut log, round-cornered squares"""
    w = white(rnd)
    wob = fbm(rnd, [(8, 8, 1.0)])
    f = []
    for i in range(N * N):
        x, y = i % N + 0.5, i // N + 0.5
        dx, dy = abs(x - cx - 0.5), abs(y - cy - 0.5)
        d = 0.65 * max(dx, dy) + 0.35 * math.hypot(dx, dy) + 0.8 * wob[i]
        ring = (d % 2.0) < 1.0
        f.append((0.7 if ring else 0.35) + 0.15 * w[i] - 0.02 * d)
    return f


def log_side(name, bark):
    def gen(ctx):
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part((birch_f if bark == 'birch' else bark_f)(rng(name)), ctx.px(name, 'oak_log'))
        return c.image()
    return gen


def log_top(name, side_name):
    """rings inside, a one pixel rim of bark (the side's colours)"""
    def gen(ctx):
        src = ctx.px(name, 'oak_log_top')
        inner = [p for i, p in enumerate(src) if 2 <= i % N < 14 and 2 <= i // N < 14]
        rim = [i % N in (0, N - 1) or i // N in (0, N - 1) for i in range(N * N)]
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(rings_f(rng(name)), inner, [not r for r in rim])
        c.part(bark_f(rng(name, 'rim')), ctx.px(side_name, 'oak_log'), rim)
        return c.image()
    return gen


def stripped_side(name):
    def gen(ctx):
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(fibre_f(rng(name)), ctx.px(name, 'stripped_oak_log'))
        return c.image()
    return gen


def planks(name):
    def gen(ctx):
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(planks_f(rng(name)), ctx.px(name, 'oak_planks'))
        return c.image()
    return gen


def leaves_mask(rnd, holes=0.2):
    ids, d1, d2, n = cells(rnd, 14, 1.0)
    w = white(rnd)
    order = sorted(range(N * N), key=lambda i: (d2[i] - d1[i]) + 1.5 * w[i])
    gone = set(order[:int(holes * N * N)])
    return ids, [i not in gone for i in range(N * N)]


def leaves(name, holes=0.2, flowers=False):
    """clusters of leaves, each lit on its top left, with gaps showing through"""
    def gen(ctx):
        rnd = rng(name)
        ids, mask = leaves_mask(rnd, holes)
        tone = [rnd.uniform(0.3, 0.9) for _ in range(max(ids) + 1)]
        r, w = rims(ids, 0.25, 0.3), white(rnd)
        f = [tone[ids[i]] + r[i] + 0.25 * w[i] for i in range(N * N)]
        src = ctx.px(name, 'oak_leaves')
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        if flowers:
            pink = select(src, lambda p: p[0] > p[1] + 30)
            leaf = select(src, lambda p: not p[0] > p[1] + 30)
            spots = set()
            for _ in range(7):
                x, y = rnd.randrange(N), rnd.randrange(N)
                spots |= {at(x, y), at(x + 1, y), at(x, y + 1)}
            fm = [mask[i] and i not in spots for i in range(N * N)]
            c.part(f, leaf or src, fm)
            c.part([-(i // N) + 0.3 * w[i] for i in range(N * N)], pink or src, [i in spots for i in range(N * N)])
        else:
            c.part(f, src, mask)
        return c.image()
    return gen


# sapling crowns: (centre y, half width, half height, shape)
CROWN = {'oak': (6, 5, 4, 'round'), 'spruce': (6, 5, 6, 'cone'), 'birch': (5, 4, 5, 'round'),
         'jungle': (6, 6, 4, 'round'), 'acacia': (4, 6, 2, 'flat'), 'dark_oak': (6, 6, 4, 'round'),
         'cherry': (5, 5, 4, 'round'), 'pale_oak': (6, 5, 4, 'round')}


def sapling(name, kind):
    """a young tree: a thin trunk and a small crown of leaf clusters"""
    def gen(ctx):
        rnd = rng(name)
        cy, hw, hh, shape = CROWN[kind]
        trunk = [False] * (N * N)
        crown = [False] * (N * N)
        for y in range(cy, N):
            trunk[at(7, y)] = True
            if y > 12:
                trunk[at(8, y)] = True
        for i in range(N * N):
            x, y = i % N + 0.5, i // N + 0.5
            dx, dy = (x - 8) / hw, (y - cy - 0.5) / hh
            if shape == 'cone':
                inside = 0 <= (y - (cy - hh)) <= 2 * hh and abs(x - 8) <= (y - (cy - hh)) * hw / (2 * hh) + 0.5
            elif shape == 'flat':
                inside = abs(dx) <= 1 and abs(dy) <= 1
            else:
                inside = dx * dx + dy * dy <= 1
            if inside and rnd.random() > 0.12:
                crown[i] = True
        for i in range(N * N):
            if crown[i]:
                trunk[i] = False
        src = ctx.px(name, 'oak_sapling')
        brown = select(src, lambda p: p[0] >= p[1])
        green = select(src, lambda p: p[1] > p[0]) or select(src, lambda p: True)
        ids, _, _, n = cells(rnd, 9, 1.0)
        r, w = rims(ids, 0.25, 0.3), white(rnd)
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part([0.6 * (i % N == 7) + 0.3 * w[i] for i in range(N * N)], brown or src, trunk)
        c.part([0.5 + r[i] + 0.3 * w[i] - 0.03 * (i // N) for i in range(N * N)], green, crown)
        return c.image()
    return gen


# doors: an outer frame, boards inside, a window of a shape of each wood's own in the top half
WINDOWS = {
    'oak': ['..........', '.##..##...'], 'birch': ['.#.#.#'], 'jungle': ['grid'], 'acacia': ['diamond'],
    'dark_oak': ['small'], 'mangrove': ['arch'], 'cherry': ['round'], 'crimson': ['slits'], 'warped': ['slits'],
    'bamboo': ['slats'], 'spruce': ['none'], 'pale_oak': ['two'], 'iron': ['two'], 'copper': ['round'],
}


def window_mask(kind):
    m = [False] * (N * N)

    def box(x0, y0, x1, y1):
        for y in range(y0, y1):
            for x in range(x0, x1):
                m[at(x, y)] = True
    if kind in ('oak', 'pale_oak', 'iron', 'two'):
        box(3, 3, 7, 9); box(9, 3, 13, 9)
    elif kind == 'birch':
        box(3, 3, 6, 7); box(7, 3, 10, 7); box(11, 3, 14, 7); box(3, 9, 6, 12); box(11, 9, 14, 12)
    elif kind == 'jungle':
        for yy in (3, 6, 9):
            for xx in (3, 6, 9, 12):
                box(xx, yy, xx + 2, yy + 2)
    elif kind == 'acacia':
        for y in range(3, 12):
            k = 4 - abs(y - 7)
            box(8 - k, y, 8 + k, y + 1)
    elif kind == 'dark_oak':
        box(5, 4, 11, 8)
    elif kind in ('mangrove', 'cherry', 'copper'):
        for y in range(3, 11):
            for x in range(4, 12):
                if (x - 7.5) ** 2 / 16 + (y - 6.5) ** 2 / (16 if kind == 'cherry' else 25) <= 1:
                    m[at(x, y)] = True
    elif kind in ('crimson', 'warped'):
        for xx in (4, 7, 10):
            box(xx, 3, xx + 1, 11)
    elif kind == 'bamboo':
        for yy in (3, 6, 9):
            box(3, yy, 13, yy + 1)
    return m


def door(name, kind, top):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, kind + '_planks', 'oak_planks')
        frame = [i % N in (0, 1, N - 2, N - 1) or (top and i // N in (0, 1)) or (not top and i // N in (N - 2, N - 1))
                 for i in range(N * N)]
        win = window_mask(kind) if top else [False] * (N * N)
        boards = fbm(rnd, [(4, 16, 0.5), (2, 8, 0.5)])
        w = white(rnd)
        f = [0.45 + 0.35 * boards[i] + 0.15 * w[i] - (0.5 if i % N in (5, 10) else 0) for i in range(N * N)]
        if not top:     # a cross brace on the lower half
            for y in range(2, 14):
                for x in (y, N - 1 - y):
                    if 2 <= x < 14:
                        f[at(x, y)] += 0.35
        inner = [not frame[i] and not win[i] for i in range(N * N)]
        fr = rims([1 if frame[i] else 0 for i in range(N * N)], 0.3, 0.3, wrap=False)
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(f, src, inner)
        c.part([0.5 + fr[i] + 0.2 * w[i] for i in range(N * N)], src, frame)
        # the window's rim (a darker frame line round the opening)
        if kind not in ('spruce',) and top:
            edge = [not win[i] and not frame[i] and any(win[at(i % N + dx, i // N + dy)]
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))) for i in range(N * N)]
            c.part([0.1 * w[i] for i in range(N * N)], sorted(src, key=lum)[:max(8, len(src) // 5)], edge)
        if not top:     # the handle
            hx = 12
            c.px[at(hx, 2)] = c.px[at(hx, 3)] = tuple(int(v * 0.5) for v in c.px[at(hx, 2)][:3]) + (255,)
        return c.image()
    return gen


def trapdoor(name, kind):
    """a hatch: a frame, boards, and openings of the wood's own kind"""
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, kind + '_planks', 'oak_planks')
        frame = [i % N in (0, 1, N - 2, N - 1) or i // N in (0, 1, N - 2, N - 1) for i in range(N * N)]
        holes = [False] * (N * N)
        if kind not in ('spruce', 'iron', 'copper'):
            for yy in (4, 9):
                for xx in (4, 9):
                    for y in range(yy, yy + 3):
                        for x in range(xx, xx + 3):
                            holes[at(x, y)] = True
        bar = [not frame[i] and i // N in (7, 8) for i in range(N * N)]
        w = white(rnd)
        boards = fbm(rnd, [(16, 4, 0.5), (8, 2, 0.5)])
        inner = [not frame[i] and not holes[i] and not bar[i] for i in range(N * N)]
        fr = rims([1 if frame[i] else 0 for i in range(N * N)], 0.3, 0.3, wrap=False)
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part([0.4 + 0.4 * boards[i] + 0.2 * w[i] - (0.4 if i // N in (5, 10) else 0) for i in range(N * N)], src, inner)
        c.part([0.6 + fr[i] + 0.2 * w[i] for i in range(N * N)], src, frame)
        c.part([0.7 - 0.3 * (i // N == 8) + 0.2 * w[i] for i in range(N * N)], src, bar)
        return c.image()
    return gen


def register(reg):
    for w in PLANKS:
        reg[w + '_planks'] = planks(w + '_planks')
    for w in TREES:
        reg[w + '_log'] = log_side(w + '_log', 'birch' if w == 'birch' else 'bark')
        reg[w + '_log_top'] = log_top(w + '_log_top', w + '_log')
        reg['stripped_%s_log' % w] = stripped_side('stripped_%s_log' % w)
        reg['stripped_%s_log_top' % w] = log_top('stripped_%s_log_top' % w, 'stripped_%s_log' % w)
    for s in STEMS:
        reg[s + '_stem'] = log_side(s + '_stem', 'bark')
        reg[s + '_stem_top'] = log_top(s + '_stem_top', s + '_stem')
        reg['stripped_%s_stem' % s] = stripped_side('stripped_%s_stem' % s)
        reg['stripped_%s_stem_top' % s] = log_top('stripped_%s_stem_top' % s, 'stripped_%s_stem' % s)
    for w in ('oak', 'spruce', 'birch', 'jungle', 'acacia', 'dark_oak', 'mangrove', 'cherry', 'pale_oak'):
        reg[w + '_leaves'] = leaves(w + '_leaves', 0.22 if w != 'spruce' else 0.28)
    reg['azalea_leaves'] = leaves('azalea_leaves', 0.2)
    reg['flowering_azalea_leaves'] = leaves('flowering_azalea_leaves', 0.2, flowers=True)
    for w in CROWN:
        reg[w + '_sapling'] = sapling(w + '_sapling', w)
    for w in TREES + STEMS + ['bamboo', 'iron', 'copper']:
        reg[w + '_door_top'] = door(w + '_door_top', w, True)
        reg[w + '_door_bottom'] = door(w + '_door_bottom', w, False)
        reg[w + '_trapdoor'] = trapdoor(w + '_trapdoor', w)
