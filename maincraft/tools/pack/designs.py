"""Blocks known by their drawing, not by their grain alone: bamboo stalks with their joints, birch
bark with its black dashes, a bale bound twice round, a block of metal or gem with its bevel and
shine, the tubes of a TNT block... Each is our own build of the idea (sizes, places and counts are
ours and change with the variant), painted by rank onto the colour source's tones in a few even
steps, as a drawn block has them.

A design here comes before a measured look (tools/pack/structure.py) and before the layered
families (tools/pack/layers.py): a picture whose drawing is what makes it known keeps that drawing.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from core import N, at, fbm, white, rng, Canvas  # noqa: E402

STEP = 0.06          # the tone steps a design is laid in


def lay(ctx, name, f, mask=None, src=None):
    """the field f painted by rank onto the source's tones, equal steps sharing one colour"""
    src = src or ctx.px(name)
    src = [p for p in src if p[3] > 127] or src
    c = Canvas(ctx.style, name, ref=ctx.ref(name))
    c.part([round(v / STEP) for v in f], src, mask, flat=True)
    return c.image()


def wiggle(name, amount=0.08, scale=(4, 4)):
    rnd = rng(name, 'wiggle')
    g = fbm(rnd, [(scale[0], scale[1], 0.6), (2, 2, 0.4)])
    return [amount * (v - 0.5) for v in g]


def splits(rnd, widths, total=N):
    """column widths from the choices, summing to total"""
    out = []
    while sum(out) < total:
        w = rnd.choice(widths)
        out.append(min(w, total - sum(out)))
    if out[-1] < min(widths):            # too thin a last one: given to the one before
        out[-2] += out.pop()
    return out


# ---- stalks, bark, straw -------------------------------------------------------------------------

def stalks(name, widths=(4, 5, 6), nodes=2):
    """stalks standing side by side: lit on the left, shaded on the right, a joint (a light ring over
    a dark one) at their own heights"""
    def gen(ctx):
        rnd = rng(name, 'stalks')
        f = [0.0] * (N * N)
        x0 = rnd.randrange(N)
        wg = wiggle(name, 0.06, (16, 4))
        for w in splits(rnd, widths):
            y0 = rnd.randrange(N)
            joints = [(y0 + k * N // nodes) % N for k in range(nodes)]
            for dx in range(w):
                x = (x0 + dx) % N
                base = 0.85 if dx == 0 else 0.25 if dx == w - 1 else 0.68 - 0.18 * dx / max(1, w - 2)
                for y in range(N):
                    v = base + wg[at(x, y)]
                    if y in joints:
                        v = 0.95 if dx < w - 1 else 0.4
                    elif (y - 1) % N in joints:
                        v = 0.12 if dx < w - 1 else 0.05
                    f[at(x, y)] = v
            x0 += w
        return lay(ctx, name, f)
    return gen


def bamboo_stalk(ctx):
    """one stalk across the whole picture (its middle two columns are what the plant shows)"""
    name = 'bamboo_stalk'
    rnd = rng(name)
    joints = (rnd.randrange(2, 6), )
    joints = (joints[0], joints[0] + 8)
    wg = wiggle(name, 0.05, (16, 4))
    f = []
    for y in range(N):
        for x in range(N):
            d = abs(x - 7.5)
            v = 0.75 - 0.05 * d + (0.15 if x == 7 else 0) + wg[at(x, y)]
            if x in (4, 11):
                v = 0.2
            if y in joints:
                v = 0.95 if x != 8 else 0.6
            elif y - 1 in joints:
                v = 0.15
            f.append(v)
    return lay(ctx, name, f)


def birch_bark(name):
    """white bark: faint upright grain, black dashes lying across (short and long), now and then a
    dark knot of two rows"""
    def gen(ctx):
        rnd = rng(name, 'bark')
        grain = fbm(rnd, [(2, 16, 0.6), (1, 8, 0.4)])
        f = [0.72 + 0.08 * g for g in grain]
        taken = set()
        marks = 0
        tries = 0
        while marks < 11 and tries < 400:
            tries += 1
            x, y = rnd.randrange(N), rnd.randrange(N)
            knot = marks < 2
            ln = rnd.choice((4, 5, 6)) if knot else rnd.choice((2, 2, 3, 3, 4, 5))
            rows = (y, y + 1) if knot else (y,)
            cells_ = [((x + dx) % N, yy % N) for dx in range(ln) for yy in rows]
            if any((cx + ex) % N + ((cy + ey) % N) * N in taken for cx, cy in cells_ for ex in (-1, 0, 1) for ey in (-1, 0, 1)):
                continue
            for k, (cx, cy) in enumerate(cells_):
                end = k % ln in (0, ln - 1)
                f[at(cx, cy)] = (0.18 if end else 0.0) if not knot else (0.05 if cy == rows[0] % N else 0.22)
                taken.add(at(cx, cy))
            marks += 1
        # the dashes in the bark's darkest tones (few as they are in it), the bark round them in the rest
        src = sorted([p for p in ctx.px(name) if p[3] > 127], key=lambda p: p[0] + p[1] + p[2])
        dark = src[:max(4, len(src) // 12)]
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part([round(v / STEP) for v in f], src[len(dark):], [i not in taken for i in range(N * N)], flat=True)
        c.part([round(v / STEP) for v in f], dark, [i in taken for i in range(N * N)], flat=True)
        return c.image()
    return gen


def bale(name):
    """straws standing up, each column its own tone, bound round twice by a dark band"""
    def gen(ctx):
        rnd = rng(name, 'bale')
        tone = [rnd.choice((0.55, 0.65, 0.75, 0.85, 0.95)) for _ in range(N)]
        wg = wiggle(name, 0.12, (1, 6))
        y1 = rnd.randrange(2, 5)
        bands = (y1, y1 + 8)
        f = []
        for y in range(N):
            for x in range(N):
                if y in bands:
                    v = 0.02
                elif y - 1 in bands:
                    v = 0.12 + (0.08 if x % 4 == 1 else 0)
                elif y + 1 in bands:
                    v = 0.4
                else:
                    v = tone[x] + wg[at(x, y)]
                f.append(v)
        return lay(ctx, name, f)
    return gen


# ---- metal and gem blocks ---------------------------------------------------------------------------

def bevel(x, y):
    """the block's rim lit from the top left: a tone, or None inside"""
    if y == 0 or x == 0:
        return 0.98 if not (x == 0 and y == N - 1) else 0.5
    if y == N - 1 or x == N - 1:
        return 0.05
    if y == 1 or x == 1:
        return 0.82
    if y == N - 2 or x == N - 2:
        return 0.25
    return None


def metal(name, kind):
    def gen(ctx):
        rnd = rng(name, 'metal')
        w = white(rnd)
        f = []
        a = rnd.randrange(3, 7)                 # where the shine runs across the face
        b = a + rnd.randrange(9, 13)
        for y in range(N):
            for x in range(N):
                r = bevel(x, y)
                if r is not None:
                    f.append(r)
                    continue
                if kind == 'shine':             # gold: an even face with two bands of shine slanting down
                    s = x + y
                    v = 0.55 + 0.04 * w[at(x, y)]
                    if a <= s <= a + 2 or b <= s <= b + 1:
                        v = 0.88 if s != a + 2 else 0.75
                    elif s == a - 1 or s == b - 1:
                        v = 0.45
                elif kind == 'facets':           # diamond: four slanting faces meeting at the middle
                    u, t = x - 7.5, y - 7.5
                    if abs(abs(u) - abs(t)) < 0.6:
                        v = 0.35
                    elif abs(t) > abs(u):
                        v = 0.85 if t < 0 else 0.45
                    else:
                        v = 0.68 if u < 0 else 0.55
                    if (x, y) in ((4, 4), (5, 4), (4, 5), (11, 6)):
                        v = 1.0
                elif kind == 'steps':            # emerald: squares set in, each lit on its top left
                    k = min(x, y, N - 1 - x, N - 1 - y)
                    lit = x - k == min(x, y) - k and (x == k or y == k)
                    shade = x == N - 1 - k or y == N - 1 - k
                    base = (0.55, 0.3, 0.7, 0.5, 0.85, 0.95)[min(5, k - 2)]
                    v = base + (0.12 if lit else -0.12 if shade else 0)
                else:                            # netherite: plates lying across, riveted at the ends
                    band = (y - 2) // 4
                    top = (y - 2) % 4 == 0
                    v = 0.28 + 0.06 * band % 0.12 + 0.08 * w[at(x, y)]
                    if top:
                        v = 0.55
                    elif (y - 2) % 4 == 3:
                        v = 0.12
                    if x in (3, 12) and (y - 2) % 4 == 1:
                        v = 0.8
                f.append(v)
        return lay(ctx, name, f)
    return gen


# ---- TNT, kelp, carved tuff --------------------------------------------------------------------

def tubes(name, fuse):
    """the ends of the tubes packed in a grid, and (on the top) the fuse in the middle"""
    def gen(ctx):
        rnd = rng(name, 'tubes')
        w = white(rnd)
        f = []
        for y in range(N):
            for x in range(N):
                cx, cy = x % 4, y % 4
                if cx == 3 or cy == 3:
                    v = 0.35 + 0.05 * w[at(x, y)]           # between the tubes
                elif cx in (1, 2) and cy in (1, 2):
                    v = 0.75 if (cx, cy) == (1, 1) else 0.6     # the paper end
                else:
                    v = 0.5 + (0.1 if cx == 0 or cy == 0 else 0)
                if fuse:
                    d = abs(x - 7) + abs(y - 7)
                    if (x in (6, 7) and 5 <= y <= 9) or (y in (6, 7) and 5 <= x <= 9) or d <= 1:
                        v = 0.0 if d <= 2 else 0.08
                f.append(v)
        return lay(ctx, name, f)
    return gen


def rolled(name):
    """a roll of dried leaves seen from its end: rings wound round, tied across with a string"""
    def gen(ctx):
        f = []
        for y in range(N):
            for x in range(N):
                u, t = x - 7.5, y - 7.5
                ang = math.atan2(t, u) / (2 * math.pi)
                r = max(abs(u), abs(t))
                v = 0.25 + 0.3 * (((r / 2.2 + ang) % 1) > 0.6)
                if x in (7, 8) or y in (7, 8):
                    v = 0.9 if (x + y) % 3 else 0.75
                if r > 7:
                    v = 0.4 if x and y else 0.5
                f.append(v)
        return lay(ctx, name, f)
    return gen


def carved(name, motif):
    """two panels, one over the other, each framed, a motif cut into it"""
    def gen(ctx):
        f = []
        for y in range(N):
            for x in range(N):
                py = y % 8
                if py == 0 or x == 0:
                    v = 0.85
                elif py == 7 or x == N - 1:
                    v = 0.2
                else:
                    u, t = x - 1, py - 1             # 14 x 6 inside
                    v = 0.55
                    if motif == 'key':               # a square spiral cut in, turned the other way below
                        uu = u if y < 8 else 13 - u
                        groove = ((t == 0 and 1 <= uu <= 12) or (uu == 12 and t <= 5) or (t == 5 and 3 <= uu <= 12)
                                  or (uu == 3 and 2 <= t <= 5) or (t == 2 and 3 <= uu <= 9) or (uu == 9 and 2 <= t <= 3))
                        if groove:
                            v = 0.12
                        elif (t == 1 and 2 <= uu <= 11) or (uu == 2 and 1 <= t <= 4):
                            v = 0.38                     # the groove's shaded side
                f.append(v)
        return lay(ctx, name, f)
    return gen


# ---- vines -----------------------------------------------------------------------------------------

def vine(name, berries=False):
    """a stem winding down with leaves on both sides, now and then a berry"""
    def gen(ctx):
        rnd = rng(name, 'vine')
        f = [0.0] * (N * N)
        m = [False] * (N * N)
        x = 7
        side = 1
        for y in range(N):
            if y % 3 == 0:
                x += rnd.choice((-1, 0, 1))
                x = max(5, min(10, x))
            f[at(x, y)] = 0.15
            m[at(x, y)] = True
            if y % 3 == 1:                   # a leaf of three pixels off the stem
                for k, dy in ((1, 0), (2, 0), (3, 0), (2, -1), (3, -1), (4, -1), (2, 1), (3, 1)):
                    lx = x + side * k
                    if 0 <= lx < N and 0 <= y + dy < N:
                        f[at(lx, y + dy)] = 0.45 + 0.12 * k / 4 + (0.25 if dy < 0 else 0)
                        m[at(lx, y + dy)] = True
                side = -side
        src = ctx.px(name)
        return lay(ctx, name, f, m, src)
    return gen


# ---- drawn by plan (tools/pack/plans.py) -------------------------------------------------------------

def palette(src, k):
    """the source's colours gathered into k groups (k-means from its tones), darkest first: each
    group's mean colour and its members"""
    px = sorted([p[:3] for p in src if p[3] > 127] or [p[:3] for p in src], key=lambda p: p[0] * 0.3 + p[1] * 0.59 + p[2] * 0.11)
    cs = [px[min(len(px) - 1, (2 * j + 1) * len(px) // (2 * k))] for j in range(k)]
    groups = []
    for _ in range(10):
        groups = [[] for _ in range(k)]
        for p in px:
            j = min(range(k), key=lambda j: sum((p[t] - cs[j][t]) ** 2 for t in range(3)))
            groups[j].append(p)
        cs = [tuple(sum(q[t] for q in g) / len(g) for t in range(3)) if g else cs[j] for j, g in enumerate(groups)]
    order = sorted(range(k), key=lambda j: cs[j][0] * 0.3 + cs[j][1] * 0.59 + cs[j][2] * 0.11)
    return [cs[j] for j in order], [groups[j] or [tuple(round(v) for v in cs[j])] for j in order]


def planned(name, frames=1):
    """the texture's own plan in the source's colours (frames > 1: an animated strip, the plan's
    lightest tones breathing from frame to frame)"""
    import plans
    rows = [r for r in plans.PLANS[name].strip().split('\n')]

    def gen(ctx):
        from PIL import Image
        levels = sorted(set(ch for r in rows for ch in r if ch != '.'))
        src = ctx.px(name)
        cs, groups = palette(src, len(levels))
        rnd = rng(name, 'plan')
        im = Image.new('RGBA', (N, N * frames))
        for fr in range(frames):
            for y in range(N):
                for x in range(N):
                    ch = rows[y][x]
                    if ch == '.':
                        continue
                    j = levels.index(ch)
                    if frames > 1 and j == len(levels) - 1 and (x + y + fr) % frames >= frames // 2:
                        j -= 1
                    q = rnd.choice(groups[j])
                    c = tuple(max(0, min(255, round(0.7 * cs[j][t] + 0.3 * q[t]))) for t in range(3))
                    im.putpixel((x, y + N * fr), c + (255,))
        return im
    return gen


DESIGN = {
    'bamboo_block': stalks('bamboo_block', (4, 5, 6)),
    'bamboo_stalk': bamboo_stalk,
    'birch_log': birch_bark('birch_log'),
    'hay_block_side': bale('hay_block_side'),
    'gold_block': metal('gold_block', 'shine'),
    'diamond_block': metal('diamond_block', 'facets'),
    'emerald_block': metal('emerald_block', 'steps'),
    'netherite_block': metal('netherite_block', 'plates'),
    'tnt_top': tubes('tnt_top', True),
    'tnt_bottom': tubes('tnt_bottom', False),
    'dried_kelp_top': rolled('dried_kelp_top'),
    'chiseled_tuff': carved('chiseled_tuff', 'key'),
    'cave_vines': vine('cave_vines'),
}


def ore(name):
    """the ore's own nuggets (tools/pack/plans.py ORE_AT) laid over its rock as this pack paints it, in
    the ore's colours (the source's pixels that are not its rock's)"""
    import plans
    rows = plans.ore_plan(name)
    base = plans.ORE_BASE[name]

    def gen(ctx):
        im = ctx.out(base).copy().convert('RGBA').crop((0, 0, N, N))
        rock = [p[:3] for p in (ctx.px(base) or []) if p[3] > 127]
        src = [p for p in ctx.px(name) if p[3] > 127]
        far = [p for p in src if min(sum((p[t] - q[t]) ** 2 for t in range(3)) for q in rock) > 900] or src
        levels = sorted(set(c for r in rows for c in r if c != '.'))
        cs, groups = palette(far, len(levels))
        rnd = rng(name, 'ore')
        for y in range(N):
            for x in range(N):
                c = rows[y][x]
                if c == '.':
                    continue
                j = levels.index(c)
                q = rnd.choice(groups[j])
                im.putpixel((x, y), tuple(max(0, min(255, round(0.7 * cs[j][t] + 0.3 * q[t]))) for t in range(3)) + (255,))
        return im
    return gen


def _plans():
    import plans
    for n in plans.PLANS:
        DESIGN[n] = planned(n, plans.FRAMES.get(n, 1))
    for n in plans.ORE_AT:
        DESIGN[n] = ore(n)


_plans()


def register(reg):
    reg.update(DESIGN)
