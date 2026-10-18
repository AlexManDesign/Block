"""Redraws the texture pack: every picture of tex/ painted anew as 16x16 pixel art in the
same look (layout, palette, silhouette), but not pixel for pixel the old one.

    python3 tools/redraw.py SRC_DIR OUT_DIR [name ...]

Each picture is reduced to a small palette of clean colours (k-means over its opaque pixels,
all animation frames together). Then:

- a picture with a drawing (planks, bricks, doors, items, mob skins...) keeps its layout of
  colour regions and silhouette (models and their UVs rely on them); each region is shaded
  afresh the pixel-art way: light from the top left (a lit rim where a region starts above or
  to the left of a pixel, a dark one where it ends below or to the right) and a fine grain of
  this tool's own;
- a noise picture (stone, dirt, sand, gravel, powders...) gets a new pattern: tileable value
  noise of the same graininess, its levels cut to the old palette in the old proportions.

Fully transparent and fully opaque pixels stay so; partly transparent ones take their colour's
mean alpha. Only Pillow is needed.
"""
import os
import math
import random
import sys
import zlib
from fnmatch import fnmatch
from PIL import Image


def lum(c):
    return c[0] * 0.3 + c[1] * 0.59 + c[2] * 0.11


def dist2(a, b):
    # colour distance, weighted roughly by how much the eye sees each channel; alpha counts too
    dr, dg, db, da = a[0] - b[0], a[1] - b[1], a[2] - b[2], a[3] - b[3]
    return 2 * dr * dr + 4 * dg * dg + 3 * db * db + 2 * da * da


def kmeans(pts, k, iters=20, extra=()):
    """deterministic k-means: seeded from luminance quantiles (plus the given extra seeds)"""
    srt = sorted(pts, key=lum)
    cent = [list(srt[min(len(srt) - 1, int((i + 0.5) * len(srt) / k))]) for i in range(k)] + [list(c) for c in extra]
    k = len(cent)
    lab = [0] * len(pts)
    for _ in range(iters):
        moved = False
        for i, p in enumerate(pts):
            best, bd = 0, 1e18
            for j, c in enumerate(cent):
                d = dist2(p, c)
                if d < bd:
                    bd, best = d, j
            if lab[i] != best:
                lab[i] = best; moved = True
        acc = [[0, 0, 0, 0, 0] for _ in range(k)]
        for p, l in zip(pts, lab):
            a = acc[l]
            a[0] += p[0]; a[1] += p[1]; a[2] += p[2]; a[3] += p[3]; a[4] += 1
        for j in range(k):
            if acc[j][4]:
                cent[j] = [acc[j][t] / acc[j][4] for t in range(4)]
        if not moved:
            break
    err = sum(dist2(p, cent[l]) for p, l in zip(pts, lab)) / max(1, len(pts))
    return cent, lab, err


def palette(pts, kmax, tol):
    """the fewest colours (up to kmax) whose mean error stays under tol"""
    uniq = len(set(pts))
    best = None
    for k in range(2, kmax + 1):
        if k > uniq:
            break
        best = kmeans(pts, k) + (k,)
        if best[2] < tol:
            break
    if best is None:
        best = kmeans(pts, 1) + (1,)
    # a small spot of its own colour (an eye, a mouth) must not melt into a big region: a pixel
    # far from its colour seeds one more
    extra = []
    while len(extra) < 4:
        cent, lab, err, k = best
        far = max(range(len(pts)), key=lambda i: dist2(pts[i], cent[lab[i]]))
        if dist2(pts[far], cent[lab[far]]) < FAR:
            break
        extra.append(pts[far])
        best = kmeans(pts, k, extra=extra)[:3] + (k,)
    return best


def frames_of(im):
    """animation strips: a column of square frames"""
    w, h = im.size
    if w == 16 and h > 16 and h % 16 == 0:
        return [im.crop((0, i * 16, 16, i * 16 + 16)) for i in range(h // 16)]
    return [im]


def edge_ratios(lab, w, h, wrap):
    """shares of side-by-side (x) and stacked (y) pixel pairs in different colours: high for
    noise, low for drawings; unequal for streaky noise like stone"""
    out = []
    for dx, dy in ((1, 0), (0, 1)):
        diff = tot = 0
        for y in range(h):
            for x in range(w):
                a = lab[y * w + x]
                xx, yy = x + dx, y + dy
                if wrap:
                    xx %= w; yy %= h
                elif xx >= w or yy >= h:
                    continue
                b = lab[yy * w + xx]
                if a < 0 or b < 0:
                    continue
                tot += 1
                diff += a != b
        out.append(diff / max(1, tot))
    return out


def edge_ratio(lab, w, h, wrap):
    ex, ey = edge_ratios(lab, w, h, wrap)
    return (ex + ey) / 2


def value_noise(w, h, px, py, rnd):
    """tileable value noise: a random grid every px by py pixels, smoothly interpolated"""
    gw, gh = max(1, w // px), max(1, h // py)
    g = [[rnd.random() for _ in range(gw)] for _ in range(gh)]
    out = []
    for y in range(h):
        fy = y / py; y0 = int(fy); ty = fy - y0; ty = ty * ty * (3 - 2 * ty)
        for x in range(w):
            fx = x / px; x0 = int(fx); tx = fx - x0; tx = tx * tx * (3 - 2 * tx)
            a = g[y0 % gh][x0 % gw]; b = g[y0 % gh][(x0 + 1) % gw]
            c = g[(y0 + 1) % gh][x0 % gw]; d = g[(y0 + 1) % gh][(x0 + 1) % gw]
            out.append((a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty)
    return out


def noise_layout(lab, order, w, h, seed, target):
    """a new layout of the same colours in the same amounts, as grainy (and as streaky) as the old one"""
    counts = [0] * (max(lab) + 1)
    for l in lab:
        counts[l] += 1
    best = None
    for sx, sy in ((1, 1), (2, 1), (1, 2), (4, 1), (1, 4)):   # streaks along x or y
        for white in (0.0, 0.15, 0.3, 0.45, 0.6, 0.8, 1.0):
            rnd = random.Random(seed)
            o8, o4, o2 = (value_noise(w, h, min(w, p * sx), min(h, p * sy), rnd) for p in (8, 4, 2))
            wn = [rnd.random() for _ in range(w * h)]
            f = [(0.5 * a + 0.3 * b + 0.2 * c) * (1 - white) + n * white for a, b, c, n in zip(o8, o4, o2, wn)]
            idx = sorted(range(w * h), key=lambda i: f[i])
            new = [0] * (w * h)
            at = 0
            for l in order:                                  # darkest colour on the lowest values
                for i in idx[at:at + counts[l]]:
                    new[i] = l
                at += counts[l]
            ex, ey = edge_ratios(new, w, h, True)
            d = abs(ex - target[0]) + abs(ey - target[1])
            if best is None or d < best[0]:
                best = (d, new)
    return best[1]


def grain(seed, i):
    """this tool's own fine grain, -1..1, fixed per picture and pixel (a splitmix64 hash: no stripes)"""
    z = (seed * 0x9E3779B97F4A7C15 + i * 0xBF58476D1CE4E5B9 + 0x94D049BB133111EB) & 0xFFFFFFFFFFFFFFFF
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & 0xFFFFFFFFFFFFFFFF
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & 0xFFFFFFFFFFFFFFFF
    z ^= z >> 31
    return (z & 0xffff) / 32767.5 - 1


def redraw(im, name):
    im = im.convert('RGBA')
    frames = frames_of(im)
    w, h = frames[0].size
    seed = zlib.crc32(name.encode())
    px = [list(f.get_flattened_data() if hasattr(f, 'get_flattened_data') else f.getdata()) for f in frames]
    opaque = all(p[3] == 255 for fr in px for p in fr)
    skin = name.startswith('entity_') or w > 16 or h > 16 and len(frames) == 1
    pts = [p for fr in px for p in fr if p[3] > 0]
    if not pts:
        return im.copy(), 'empty', 0
    cent, _, _, k = palette(pts, 24 if skin else 8, 40 if skin else 70)
    pal = [tuple(c) for c in cent]

    def nearest(p):
        best, bd = 0, 1e18
        for j, c in enumerate(pal):
            d = dist2(p, c)
            if d < bd:
                bd, best = d, j
        return best
    labs = [[nearest(p) if p[3] > 0 else -1 for p in fr] for fr in px]
    # each colour's grain no stronger than the spread of the old pixels it stands for: a flat
    # colour (concrete, wool) stays flat
    spread = [[0.0, 0] for _ in pal]
    for fr, lab in zip(px, labs):
        for p, l in zip(fr, lab):
            if l >= 0:
                spread[l][0] += abs(lum(p) - lum(pal[l])); spread[l][1] += 1
    amp = [min(0.03 if skin else 0.045, max(0.01, s / max(1, n) / max(1.0, lum(c)))) for (s, n), c in zip(spread, pal)]
    wrap = opaque and w == 16 and h == 16
    e = edge_ratio(labs[0], w, h, wrap)
    mode = 'noise' if opaque and len(frames) == 1 and w == 16 and h == 16 and any(fnmatch(name, n) for n in NOISE) and not any(fnmatch(name, n) for n in EXCEPT) else 'drawn'
    if mode == 'noise':
        order = sorted(range(len(pal)), key=lambda j: lum(pal[j]))
        labs = [noise_layout(labs[0], order, w, h, seed, edge_ratios(labs[0], w, h, True))]

    def edge(o, l):
        # the rim of a shape: another colour, and a clearly different one (not mere grain)
        return o != l and (o < 0 or abs(lum(pal[o]) - lum(pal[l])) > EDGE)
    out = []
    for fi, (fr, lab) in enumerate(zip(px, labs)):
        res = []
        for i, p in enumerate(fr):
            l = lab[i]
            if l < 0:
                res.append((0, 0, 0, 0))
                continue
            x, y = i % w, i // w

            def at(xx, yy):
                if wrap:
                    return lab[(yy % h) * w + xx % w]
                return lab[yy * w + xx] if 0 <= xx < w and 0 <= yy < h else -1
            k = 1.0
            if not skin and mode == 'drawn':
                lit = edge(at(x - 1, y), l) or edge(at(x, y - 1), l)
                dark = edge(at(x + 1, y), l) or edge(at(x, y + 1), l)
                if lit and not dark:
                    k *= 1.09
                elif dark and not lit:
                    k *= 0.91
            k *= 1 + amp[l] * grain(seed, fi * w * h + i)
            c = pal[l]
            a = p[3] if p[3] in (0, 255) else max(1, min(254, round(c[3])))
            rgb = [max(0, min(255, round(c[t] * k))) for t in range(3)]
            if max(abs(rgb[t] - p[t]) for t in range(3)) < MIN_STEP:
                # a flat or dark colour the shading hardly moves: step it off the old value, the same
                # way for the whole colour so that a flat region stays flat
                s = 1 if grain(seed ^ 0x5bd1, l) >= 0 else -1
                for _ in range(2):
                    q = [max(0, min(255, p[t] + s * MIN_STEP)) for t in range(3)]
                    if max(abs(q[t] - p[t]) for t in range(3)) >= MIN_STEP:
                        rgb = q
                        break
                    s = -s
            res.append((rgb[0], rgb[1], rgb[2], a))
        f = Image.new('RGBA', (w, h)); f.putdata(res); out.append(f)
    if len(out) == 1:
        return out[0], mode, e
    strip = Image.new('RGBA', (w, h * len(out)))
    for i, f in enumerate(out):
        strip.paste(f, (0, i * h))
    return strip, mode, e


def redraw_glow(im, name, cell):
    """a sky body (sun, moon phases) on black, drawn added onto the sky: the bright body is
    painted like a skin; its halo is made anew as the body's own light falling off with
    distance, fitted to the old halo's strength, reach and tint. Black stays black."""
    im = im.convert('RGBA')
    w, h = im.size
    px = list(im.get_flattened_data() if hasattr(im, 'get_flattened_data') else im.getdata())
    top = {}
    for i, p in enumerate(px):
        c = (i % w // cell, i // w // cell)
        top[c] = max(top.get(c, 0), lum(p))
    dim = {c for c, t in top.items() if t < 64}          # a new moon: no body, only a faint disc
    body = [(c := (i % w // cell, i // w // cell)) not in dim and lum(p) > 0.35 * top[c] for i, p in enumerate(px)]
    only = Image.new('RGBA', (w, h))
    only.putdata([p if b else (0, 0, 0, 0) for p, b in zip(px, body)])
    painted, _, _ = redraw(only, name + ':body')
    pb = list(painted.get_flattened_data() if hasattr(painted, 'get_flattened_data') else painted.getdata())
    # light of every halo pixel from the body pixels of its own cell: sum of lum * exp(-d / reach)
    src = {}
    for i, b in enumerate(body):
        if b:
            x, y = i % w, i // w
            src.setdefault((x // cell, y // cell), []).append((x, y, lum(px[i]) / 255))
    halo = [i for i, b in enumerate(body) if not b]

    def light(reach):
        out = []
        for i in halo:
            x, y = i % w, i // w
            s = 0.0
            for bx, by, l in src.get((x // cell, y // cell), ()):
                s += l * math.exp(-math.hypot(x - bx, y - by) / reach)
            out.append(s)
        return out
    # each channel its own strength and reach: the halo whitens near the body, takes its tint far off
    lights = {reach: light(reach) for reach in [0.6 + 0.2 * j for j in range(30)]}
    chan = []
    for t in range(3):
        old = [px[i][t] for i in halo]
        best = None
        for reach, g in lights.items():
            gg = sum(v * v for v in g)
            a = sum(v * o for v, o in zip(g, old)) / gg if gg else 0
            err = sum((a * v - o) ** 2 for v, o in zip(g, old))
            if best is None or err < best[0]:
                best = (err, reach, a)
        chan.append(best[1:])
    res = list(pb)
    for k, i in enumerate(halo):
        res[i] = tuple(max(0, min(255, round(a * lights[reach][k]))) for reach, a in chan) + (255,)
    for i in halo:                                       # a faint disc drawn softer, 1-2-1 blur
        x, y = i % w, i // w
        if (x // cell, y // cell) not in dim:
            continue
        acc, n = [0, 0, 0], 0
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                xx, yy = x + dx, y + dy
                if xx // cell == x // cell and yy // cell == y // cell and 0 <= xx < w and 0 <= yy < h:
                    k = (2 - abs(dx)) * (2 - abs(dy))
                    q = px[yy * w + xx]
                    acc = [acc[t] + k * q[t] for t in range(3)]; n += k
        res[i] = tuple(round(acc[t] / n) for t in range(3)) + (255,)
    out = Image.new('RGBA', (w, h)); out.putdata(res)
    return out, 'glow ' + '/'.join('%.1f' % r for r, _ in chan), 0


# sky bodies: name -> cell size
GLOW = {'sun': 32, 'moon_phases': 32}

# how far (dist2) a pixel may lie from its palette colour before it gets its own
FAR = 3500

# the least luminance step between two colours that makes a shape's rim (shaded lit / dark)
EDGE = 16

# the least step of a painted pixel off the old one, in levels of its most changed channel
MIN_STEP = 5

# the pictures with no drawing in them, only grain: these get a new pattern
NOISE = ['stone', 'dirt', 'coarse_dirt', 'sand', 'red_sand', 'gravel', 'clay', 'grass_block_top', 'snow', 'soul_soil',
         'netherrack', 'end_stone', 'andesite', 'diorite', 'granite', 'tuff', 'calcite', '*_concrete_powder', '*_concrete',
         'terracotta', '*_terracotta', 'moss_block', 'bedrock', 'mycelium_top', 'podzol_top', 'crimson_nylium', 'warped_nylium',
         'nether_wart_block', 'warped_wart_block', 'sandstone_top', 'red_sandstone_top', 'mud', 'smooth_basalt',
         'dripstone_block', 'soul_sand', 'obsidian', 'dirt_path_top', 'rooted_dirt']
EXCEPT = ['*_glazed_terracotta']


def main():
    src, dst = sys.argv[1], sys.argv[2]
    only = set(sys.argv[3:])
    os.makedirs(dst, exist_ok=True)
    for fn in sorted(os.listdir(src)):
        if not fn.endswith('.png') or (only and fn[:-4] not in only):
            continue
        im = Image.open(os.path.join(src, fn))
        n = fn[:-4]
        new, mode, e = redraw_glow(im, n, GLOW[n]) if n in GLOW else redraw(im, n)
        new.save(os.path.join(dst, fn))
        print(n, mode, round(e, 3))


if __name__ == '__main__':
    main()
