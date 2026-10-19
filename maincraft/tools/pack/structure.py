"""The look of a material, measured and matched.

A Minecraft texture is recognised by the kind of its drawing as much as by its colours: gravel is a
mosaic of tiny pebbles, sand an even fine grain, stone soft wide blotches. This module measures that
kind on a picture (how big the patches of one tone are, how often the tone changes sideways and
upwards, how rough it is from pixel to pixel, how many lone light and dark specks it has) and finds
the parameters of our own pattern makers (pebbles, clouds, streaks) whose drawing measures the same
on the reference. The pixels are always our own (each variant its own random drawing); only the
kind is matched.

    python3 tools/pack/structure.py JAVA_TEXTURES_DIR      # measure and write structure.json

The parameters found are kept in structure.json, so painting needs no reference pack.
"""
import itertools
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from core import (N, fbm, white, cells, rims, rng, Canvas, lum, Source)  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
STORE = os.path.join(HERE, 'structure.json')


# ---- measuring ----------------------------------------------------------------------------------

def measure(px):
    """the kind of a drawing: patch size, tone changes along x and y, roughness, specks"""
    L = [lum(p) for p in px]
    srt = sorted(L)
    cuts = [srt[len(srt) * k // 4] for k in (1, 2, 3)]
    q = [sum(v >= c for c in cuts) for v in L]

    def nb(i, dx, dy):
        x, y = i % N, i // N
        return ((y + dy) % N) * N + (x + dx) % N
    ex = sum(q[i] != q[nb(i, 1, 0)] for i in range(N * N)) / (N * N)
    ey = sum(q[i] != q[nb(i, 0, 1)] for i in range(N * N)) / (N * N)
    seen, sizes = [False] * (N * N), []
    for s in range(N * N):
        if seen[s]:
            continue
        stack, n = [s], 0
        seen[s] = True
        while stack:
            i = stack.pop()
            n += 1
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                j = nb(i, dx, dy)
                if not seen[j] and q[j] == q[i]:
                    seen[j] = True
                    stack.append(j)
        sizes.append(n)
    patch = sum(n * n for n in sizes) / (N * N)          # mean patch size a pixel sits in
    mean = sum(L) / len(L)
    sd = math.sqrt(sum((v - mean) ** 2 for v in L) / len(L)) or 1
    rough = sum(abs(L[i] - L[nb(i, 1, 0)]) + abs(L[i] - L[nb(i, 0, 1)]) for i in range(N * N)) / (2 * N * N) / sd
    specks = 0
    for i in range(N * N):
        around = [L[nb(i, dx, dy)] for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))]
        if L[i] > max(around) + 0.5 * sd or L[i] < min(around) - 0.5 * sd:
            specks += 1

    def corr(dx, dy):           # how alike a pixel and its neighbour are (a pebble is several pixels)
        return sum((L[i] - mean) * (L[nb(i, dx, dy)] - mean) for i in range(N * N)) / (N * N) / (sd * sd)
    # lit from the top left: inside a patch the tone falls gently, at its edge it jumps back up, so
    # the steps down the diagonal lean one way
    d = [L[i] - L[nb(i, 1, 1)] for i in range(N * N)]
    sk = sum(v ** 3 for v in d) / (N * N) / (sum(v * v for v in d) / (N * N) or 1) ** 1.5
    return [ex, ey, math.log(patch), rough, specks / (N * N), corr(1, 0), corr(0, 1), sk]


WEIGHT = [3.0, 3.0, 1.0, 1.5, 4.0, 3.0, 3.0, 0.6]


def distance(a, b):
    return sum(w * abs(x - y) for w, x, y in zip(WEIGHT, a, b))


# ---- our pattern makers -------------------------------------------------------------------------

def pebbles(rnd, n, gap, rim, grain):
    """stones of their own tones, lit top left, shaded bottom right, dark gaps between"""
    ids, d1, d2, k = cells(rnd, n, 0.95)
    tone = [rnd.random() for _ in range(k)]
    r, w = rims(ids, rim, rim * 1.3), white(rnd)
    return [-1.0 if gap and d2[i] - d1[i] < gap else
            (1 - grain) * tone[ids[i]] + r[i] + grain * w[i] for i in range(N * N)]


def clouds(rnd, period, grain, specks):
    """soft blotches of a given size with a fine grain and lone specks over them"""
    octs = [(p, p, 1.0 / (1 + j)) for j, p in enumerate((period, max(1, period // 2), max(1, period // 4)))]
    f = fbm(rnd, octs)
    w = white(rnd)
    f = [(1 - grain) * a + grain * b for a, b in zip(f, w)]
    for i in rnd.sample(range(N * N), specks):
        f[i] = rnd.choice((-0.6, 1.6))
    return f


def streaks(rnd, px, py, grain):
    """blotches drawn out along x (layers) or y (fibres)"""
    f = fbm(rnd, [(px, py, 0.5), (max(1, px // 2), max(1, py // 2), 0.3), (max(1, px // 4), max(1, py // 4), 0.2)])
    w = white(rnd)
    return [(1 - grain) * a + grain * b for a, b in zip(f, w)]


def bark(rnd, period, furrow, grain):
    """ridges running up with dark furrows between them"""
    from core import value_noise
    cols = value_noise(period, N, rnd)
    fine = fbm(rnd, [(1, 8, 0.5), (1, 4, 0.5)])
    w = white(rnd)
    return [-0.6 if fine[i] < furrow else (1 - grain) * (0.6 * cols[i] + 0.4 * fine[i]) + grain * w[i]
            for i in range(N * N)]


def plate(rnd, border, bevel, seams, grain):
    """a slab with a bevelled rim (light top left, dark bottom right), pressed seams across, fine grain"""
    f = clouds(rnd, 4, grain, 0)
    rows = [round(N * (k + 1) / (seams + 1)) for k in range(seams)]
    for i in range(N * N):
        x, y = i % N, i // N
        if x < border or y < border:
            f[i] += bevel
        elif x >= N - border or y >= N - border:
            f[i] -= bevel
        elif y in rows:
            f[i] -= bevel * 0.8
        elif y - 1 in rows:
            f[i] += bevel * 0.5
    return f


MAKERS = {'pebbles': pebbles, 'clouds': clouds, 'streaks': streaks, 'bark': bark, 'plate': plate}

GRID = {
    'pebbles': [dict(n=n, gap=g, rim=r, grain=gr) for n, g, r, gr in
                itertools.product((9, 16, 25, 36, 49, 64, 100), (0, 0.5, 0.9), (0, 0.15, 0.3), (0, 0.2, 0.45))],
    'clouds': [dict(period=p, grain=g, specks=s) for p, g, s in
               itertools.product((2, 4, 8, 16), (0, 0.15, 0.3, 0.5, 0.7, 0.9), (0, 6, 14, 24))],
    'bark': [dict(period=p, furrow=f, grain=g) for p, f, g in
             itertools.product((1, 2, 4), (0.0, 0.2, 0.3, 0.4), (0.1, 0.25, 0.45))],
    'plate': [dict(border=b, bevel=v, seams=s, grain=g) for b, v, s, g in
              itertools.product((1, 2), (0.2, 0.4, 0.7), (0, 1, 2, 3), (0.1, 0.3, 0.6))],
    'streaks': [dict(px=a, py=b, grain=g) for (a, b), g in
                itertools.product(((16, 2), (16, 4), (8, 2), (8, 1), (2, 16), (4, 16), (2, 8), (1, 8)),
                                  (0.1, 0.25, 0.4, 0.6))],
}


def field(name, params):
    kind, p = params['kind'], params['p']
    return MAKERS[kind](rng(name, 'structure'), **p)


def render(field_, src_px):
    c = Canvas('java', 'x')
    c.part(field_, src_px, grain=0)
    return list(c.image().get_flattened_data())


def fit(ref_px, name, kinds=('pebbles', 'clouds', 'streaks')):
    """the maker and parameters whose drawing measures most like the reference"""
    target = measure(ref_px)
    best = None
    for kind in kinds:
        grid = GRID[kind]
        for p in grid:
            score = 0.0
            for seed in (0, 1):          # judged on two drawings, so a lucky one does not win
                f = MAKERS[kind](rng(name, 'fit', seed), **p)
                score += distance(measure(render(f, ref_px)), target)
            if best is None or score < best[0]:
                best = (score, kind, p)
    return {'kind': best[1], 'p': best[2], 'score': round(best[0] / 2, 3)}


# pictures of other families that are a plain material, painted to the measured look too: name -> makers
PLAIN = {}
for _w in ('oak', 'spruce', 'birch', 'jungle', 'acacia', 'dark_oak', 'mangrove', 'cherry', 'pale_oak'):
    PLAIN[_w + '_log'] = PLAIN['stripped_%s_log' % _w] = ('bark', 'streaks')
for _s in ('crimson', 'warped'):     # the stems themselves move (tools/pack/anim.py)
    PLAIN['stripped_%s_stem' % _s] = ('bark', 'streaks')
for _n in ('iron_block', 'gold_block', 'diamond_block', 'emerald_block', 'netherite_block', 'lapis_block',
           'redstone_block', 'coal_block', 'copper_block', 'exposed_copper', 'weathered_copper', 'oxidized_copper',
           'quartz_block_side', 'quartz_block_bottom', 'smooth_stone', 'polished_andesite', 'polished_diorite',
           'polished_granite', 'polished_deepslate', 'polished_tuff', 'polished_blackstone', 'purpur_block',
           'polished_basalt_top'):
    PLAIN[_n] = ('plate', 'clouds')
for _n in ('cobblestone', 'cobbled_deepslate'):
    PLAIN[_n] = ('pebbles',)
for _n in ('hay_block_side', 'bamboo_block', 'mushroom_stem', 'bone_block_side', 'quartz_pillar', 'purpur_pillar',
           'polished_basalt_side', 'dried_kelp_side'):
    PLAIN[_n] = ('streaks', 'bark', 'plate')


def paint(ctx, name, src_px):
    """the picture painted to its measured look, or None when it has none"""
    params = load().get(name)
    if not params:
        return None
    import core
    c = Canvas(ctx.style, name, ref=ctx.ref(name))
    core.KEEP_SCALE = True
    try:
        c.part(field(name, params), src_px)
    finally:
        core.KEEP_SCALE = False
    return c.image()


_STORE = None


def load():
    global _STORE
    if _STORE is not None:
        return _STORE
    _STORE = {}
    if os.path.exists(STORE):
        with open(STORE) as fh:
            _STORE = json.load(fh)
    return _STORE


def main():
    import natural
    import plants
    import masonry
    for fam in (natural, plants, masonry):     # registering fills natural.MATCHED
        fam.register({})
    src = Source('java', sys.argv[1])
    names = sys.argv[2:] or sorted(natural.MATCHED | set(PLAIN))
    store = dict(load())
    for n in names:
        ref = src.px(n)
        if ref is None or sum(1 for p in ref if p[3] > 127) < 200:
            print(n, 'no reference')
            continue
        store[n] = fit(ref, n, PLAIN.get(n, ('pebbles', 'clouds', 'streaks')))
        print(n, store[n])
        with open(STORE, 'w') as fh:
            json.dump(store, fh, indent=1, sort_keys=True)


if __name__ == '__main__':
    main()
