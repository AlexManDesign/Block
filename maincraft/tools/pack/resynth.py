"""A material drawn anew in its own look: the same kinds of specks, stones, grains and seams, put in
other places (non-parametric texture synthesis on the tile, wrapping, so the new tile tiles too).

    resynth(im, name) -> new picture, or None when the picture is not a tiling material

Each new pixel is copied from the place in the old tile whose already drawn neighbourhood looks most
like the new pixel's own (random among the near-best, never from its own place), so whole stones,
specks and seams come out again, moved and rearranged. A tile that tiles only sideways (a grass
block's side: grass on top, dirt under it) keeps its rows: a pixel is taken from about its own height.
"""
import os
import random
import sys
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from core import rng  # noqa: E402

R = 2                         # the neighbourhood: 5 x 5
EPS = 0.12                    # how far from the best a candidate may be and still be taken


def lum(p):
    return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]


def tiling(px, n):
    """(sideways, upwards): whether the tile runs on across its edges as smoothly as inside it"""
    def mean(pairs):
        return sum(max(abs(px[a][t] - px[b][t]) for t in range(3)) for a, b in pairs) / len(pairs)
    inside_x = mean([(y * n + x, y * n + x + 1) for y in range(n) for x in range(n - 1)])
    inside_y = mean([(y * n + x, (y + 1) * n + x) for y in range(n - 1) for x in range(n)])
    edge_x = mean([(y * n + n - 1, y * n) for y in range(n)])
    edge_y = mean([((n - 1) * n + x, x) for x in range(n)])
    return edge_x <= 1.5 * inside_x + 4, edge_y <= 1.5 * inside_y + 4


def resynth(im, name, seed=0):
    im = im.convert('RGBA')
    n = im.size[0]
    if im.size != (n, n) or n < 8:
        return None
    src = list(im.get_flattened_data())
    if sum(1 for p in src if p[3] < 128) > n * n // 10:      # see-through: a cut-out drawing
        return None
    side, up = tiling(src, n)
    if not side:
        return None
    rows = not up                                            # keep each pixel near its own height
    rnd = rng(name, 'resynth', seed) if seed is not None else random.Random(0)
    S = [(p[0], p[1], p[2]) for p in src]
    out = [None] * (n * n)
    # the order: row by row; the first row's left part is seeded from a random place
    for y in range(n):
        for x in range(n):
            known = []
            for dy in range(-R, R + 1):
                for dx in range(-R, R + 1):
                    if dx == 0 and dy == 0:
                        continue
                    j = ((y + dy) % n) * n + (x + dx) % n
                    if out[j] is not None:
                        known.append((dx, dy, out[j]))
            best, cands = None, []
            for sy in range(n):
                if rows and abs(sy - y) > 1:
                    continue
                for sx in range(n):
                    if sx == x and sy == y:
                        continue
                    if known:
                        d = 0.0
                        for dx, dy, c in known:
                            q = S[((sy + dy) % n) * n + (sx + dx) % n]
                            d += (q[0] - c[0]) ** 2 + (q[1] - c[1]) ** 2 + (q[2] - c[2]) ** 2
                        d /= len(known)
                    else:
                        d = 0.0
                    if rows:
                        d += 400 * abs(sy - y)
                    cands.append((d, sy * n + sx))
                    if best is None or d < best:
                        best = d
            lim = best * (1 + EPS) + 30
            pick = [i for d, i in cands if d <= lim]
            out[y * n + x] = S[rnd.choice(pick)]
    res = Image.new('RGBA', (n, n))
    res.putdata([c + (src[i][3],) for i, c in enumerate(out)])
    return res
