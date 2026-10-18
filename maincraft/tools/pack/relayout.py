"""Redraws the pictures of tools/art.py (blocks whose models read set spots of the picture: pistons,
grindstones, lecterns, brewing stands, dripleaves...) in the style of the painted pack.

The drawing's regions (runs of one colour) stay where they are, so the models' UVs still meet the
right parts; each region is painted anew with our material for its kind (boards with seams along
its length for wood, blotchy rock for stone, a sheen for metal, clusters for leaves, a soft core
for glowing bits, flat dark for holes), lit on its top left and shaded on its bottom right. The
colours are the drawing's own, shifted for each kind of region the way the style's matching
picture (planks for wood, stone, iron, leaves...) differs from our base pack's; vivid marks keep
their colour.
"""
import math
import os
import sys
from PIL import Image
from core import (N, at, fbm, white, mix, cells, rims, rng, Canvas, lum)
import natural

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
import redraw  # noqa: E402


def regions(px):
    """palette clusters split into 4-connected runs: region id per pixel (-1 transparent)"""
    pts = [p for p in px if p[3] > 0]
    if not pts:
        return [-1] * len(px), []
    cent, _, _, _ = redraw.palette(pts, 6, 160)
    lab = [min(range(len(cent)), key=lambda j: redraw.dist2(p, cent[j])) if p[3] > 0 else -1 for p in px]
    # a speck of a colour close to its neighbour's is grain of that region, not a part of its own
    for _ in range(2):
        for i in range(N * N):
            if lab[i] < 0:
                continue
            x, y = i % N, i // N
            nb = [lab[yy * N + xx] for xx, yy in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1))
                  if 0 <= xx < N and 0 <= yy < N and lab[yy * N + xx] >= 0]
            if nb and lab[i] not in nb:
                best = min(set(nb), key=lambda j: redraw.dist2(cent[j], cent[lab[i]]))
                if redraw.dist2(cent[best], cent[lab[i]]) < 6000:
                    lab[i] = best
    rid = [-1] * (N * N)
    info = []
    for s in range(N * N):
        if lab[s] < 0 or rid[s] >= 0:
            continue
        k = len(info)
        stack, members = [s], []
        rid[s] = k
        while stack:
            i = stack.pop()
            members.append(i)
            x, y = i % N, i // N
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                xx, yy = x + dx, y + dy
                j = yy * N + xx
                if 0 <= xx < N and 0 <= yy < N and rid[j] < 0 and lab[j] == lab[i]:
                    rid[j] = k
                    stack.append(j)
        xs, ys = [i % N for i in members], [i // N for i in members]
        mean = tuple(sum(px[i][t] for i in members) / len(members) for t in range(4))
        info.append({'px': members, 'mean': mean, 'w': max(xs) - min(xs) + 1, 'h': max(ys) - min(ys) + 1,
                     'x0': min(xs), 'y0': min(ys)})
    return rid, info


def kind(c, size):
    r, g, b = c[:3]
    ch, l = max(r, g, b) - min(r, g, b), lum(c)
    if l < 45:
        return 'dark'
    if ch > 120 or (l > 200 and ch > 60):
        return 'glow'
    if g > r + 8 and g >= b:
        return 'leaf'
    if r > g >= b and 25 < ch <= 140 and l < 190:
        return 'wood'
    if ch < 22:
        return 'metal' if l > 175 or size < 10 else 'stone'
    return 'tint'


def field(kind_, reg, rnd, base):
    """the material's height field for one region"""
    w = base['white']
    f = {}
    if kind_ == 'wood':
        along_x = reg['w'] >= reg['h']
        for i in reg['px']:
            x, y = i % N, i // N
            across = (y - reg['y0']) if along_x else (x - reg['x0'])
            grain = base['grain_x'][i] if along_x else base['grain_y'][i]
            v = 0.5 + 0.35 * grain + 0.12 * w[i]
            if across % 4 == 3:
                v = -0.6
            elif across % 4 == 0:
                v += 0.15
            f[i] = v
    elif kind_ == 'stone':
        for i in reg['px']:
            f[i] = 0.6 * base['blotch'][i] + 0.4 * w[i]
    elif kind_ == 'metal':
        for i in reg['px']:
            x, y = i % N, i // N
            f[i] = 0.55 + 0.25 * math.cos((x + y) / 3.0) + 0.08 * w[i]
    elif kind_ == 'leaf':
        for i in reg['px']:
            f[i] = 0.55 * base['leaf'][i] + 0.45 * w[i]
    elif kind_ == 'glow':
        cx = reg['x0'] + reg['w'] / 2
        cy = reg['y0'] + reg['h'] / 2
        for i in reg['px']:
            d = math.hypot(i % N + 0.5 - cx, i // N + 0.5 - cy) / max(1, max(reg['w'], reg['h']) / 2)
            f[i] = 1 - 0.6 * d + 0.08 * w[i]
    elif kind_ == 'dark':
        for i in reg['px']:
            f[i] = 0.5 + 0.15 * w[i]
    else:
        for i in reg['px']:
            f[i] = 0.5 * base['blotch'][i] + 0.3 * w[i]
    return f


CONTRAST = {'wood': 0.45, 'stone': 0.4, 'metal': 0.45, 'leaf': 0.5, 'glow': 0.5, 'dark': 0.25, 'tint': 0.4}


def repaint_frame(style, name, im, ratios, seed_key, ref_px=None):
    """im: the drawing; ratios: kind of region -> the style's colour shift for it"""
    px = list(im.convert('RGBA').get_flattened_data())
    rid, info = regions(px)
    rnd = rng(name, seed_key)
    base = {'white': white(rnd), 'grain_x': fbm(rnd, [(16, 1, 0.5), (8, 1, 0.5)]),
            'grain_y': fbm(rnd, [(1, 16, 0.5), (1, 8, 0.5)]), 'blotch': natural.blotch_f(0.6, 16)(rnd),
            'leaf': natural.moss_f(rnd)}
    edges = rims([r if r >= 0 else -1 for r in rid], 0.25, 0.25, wrap=False)
    c = Canvas(style, name, ref=ref_px)
    for k, reg in enumerate(info):
        kd = kind(reg['mean'], len(reg['px']))
        f = field(kd, reg, rnd, base)
        # vivid marks (a red signal, a lit lamp) keep their meaning: our colour, not the reference's
        m = reg['mean']
        r = ratios.get(kd) if max(m[:3]) - min(m[:3]) <= 90 else None
        col = tuple(min(255, m[t] * r[t]) for t in range(3)) if r else m[:3]
        con = CONTRAST[kd]
        ramp_src = [tuple(max(0, min(255, round(col[t] * (1 - con / 2 + con * j / 15)))) for t in range(3)) + (255,)
                    for j in range(16)]
        mask = [False] * (N * N)
        for i in reg['px']:
            mask[i] = True
        alpha = round(reg['mean'][3]) if reg['mean'][3] < 250 else 255
        small = len(reg['px']) <= 3
        c.part([f.get(i, 0) + (0 if small else edges[i]) for i in range(N * N)], ramp_src, mask, alpha=alpha)
    return c.image()


# the picture each kind of region takes its style's tone from
KIND_SOURCE = {'wood': 'oak_planks', 'stone': 'stone', 'metal': 'iron_block', 'leaf': 'azalea_leaves',
               'tint': 'terracotta'}


def kind_ratios(ctx):
    """per kind of region, how the style's colours differ from our base pack's (per channel)"""
    out = {}
    for k, n in KIND_SOURCE.items():
        a, b = ctx.own.px(n), ctx.px(n)
        if not a or not b:
            continue
        ma = [sum(p[t] for p in a if p[3] > 127) / max(1, sum(1 for p in a if p[3] > 127)) for t in range(3)]
        mb = [sum(p[t] for p in b if p[3] > 127) / max(1, sum(1 for p in b if p[3] > 127)) for t in range(3)]
        out[k] = [max(0.6, min(1.6, mb[t] / max(1.0, ma[t]))) for t in range(3)]
    return out


def repaint(style, name, drawing, ratios, ref_px):
    """a whole drawing, still or a strip of frames; ratios from kind_ratios"""
    drawing = drawing.convert('RGBA')
    frames = [drawing.crop((0, k * N, N, k * N + N)) for k in range(drawing.height // N)]
    out = []
    for k, fr in enumerate(frames):
        out.append(repaint_frame(style, name, fr, ratios, k, ref_px if k == 0 else None))
    if len(out) == 1:
        return out[0]
    s = Image.new('RGBA', (N, N * len(out)))
    for k, f in enumerate(out):
        s.paste(f, (0, k * N))
    return s
