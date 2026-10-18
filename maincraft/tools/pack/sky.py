"""Sky bodies and mob skins.

The sun and the moon phases are drawn from scratch: a square body in rings, a moon with craters
and its phase's shadow, and a glow falling off around the lit pixels (all on black: the sky adds
them). The mob skins keep the layout of our models (the regions of our own skins, which their
UVs rely on) and get their colours from the style's skins (tools/tone.py)."""
import math
import os
import sys
from PIL import Image
from core import lum

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
import tone  # noqa: E402

CELL = 32


def brightest(px, k):
    srt = sorted((p for p in px if p[3] > 0 and lum(p) > 20), key=lum)
    return srt[-k:] if srt else [(255, 230, 120, 255)]


def avg(ps):
    return tuple(sum(p[t] for p in ps) / len(ps) for t in range(3))


def glow(lit, w, h, colour, reach, strength):
    """light round the lit pixels, falling off with distance"""
    out = [[0.0, 0.0, 0.0] for _ in range(w * h)]
    pts = [(i % w, i // w, v) for i, v in lit.items()]
    for i in range(w * h):
        x, y = i % w, i // w
        s = 0.0
        for px, py, v in pts:
            if abs(px - x) < 12 and abs(py - y) < 12:
                s += v * math.exp(-math.hypot(px - x, py - y) / reach)
        for t in range(3):
            out[i][t] = min(255, colour[t] * s * strength)
    return out


def sun(ctx):
    src = ctx.own.px('sun') or []
    im = ctx.own.image('sun')
    hot = brightest(list(im.get_flattened_data()), 40)
    rings = [avg(hot[:12]), avg(hot[12:28]), avg(hot[28:])]
    w = h = CELL
    body = {}
    pix = [(0, 0, 0)] * (w * h)
    for y in range(12, 20):
        for x in range(12, 20):
            r = max(abs(x - 15.5), abs(y - 15.5))
            col = rings[0] if r > 3 else rings[1] if r > 2 else rings[2]
            pix[y * w + x] = col
            body[y * w + x] = 1.0
    g = glow(body, w, h, rings[0], 3.2, 0.09)
    out = Image.new('RGBA', (w, h))
    out.putdata([tuple(int(min(255, (pix[i][t] if i in body else g[i][t]))) for t in range(3)) + (255,)
                 for i in range(w * h)])
    return out


CRATERS = [(13, 13), (17, 14), (14, 17), (18, 18), (16, 12)]


def moon(ctx):
    im = ctx.own.image('moon_phases')
    hot = brightest(list(im.get_flattened_data()), 60)
    light, dark = avg(hot[30:]), avg(hot[:20])
    W, H = CELL * 4, CELL * 2
    out = Image.new('RGBA', (W, H), (0, 0, 0, 255))
    for k in range(8):
        ox, oy = (k % 4) * CELL, (k // 4) * CELL
        theta = k * math.pi / 4
        lit, pix = {}, {}
        for y in range(12, 20):
            for x in range(12, 20):
                xn = (x + 0.5 - 16) / 4
                on = xn <= math.cos(theta) if k <= 4 else xn >= -math.cos(theta)
                crater = any(abs(x - cx) <= 0 and abs(y - cy) <= 0 or (x - cx, y - cy) in ((1, 0), (0, 1)) for cx, cy in CRATERS)
                col = dark if crater else light
                if on and k != 4:
                    pix[(x, y)] = col
                    lit[y * CELL + x] = 1.0
                else:
                    pix[(x, y)] = tuple(v * 0.08 for v in col)
        g = glow(lit, CELL, CELL, light, 2.4, 0.07)
        for y in range(CELL):
            for x in range(CELL):
                c = pix.get((x, y)) or g[y * CELL + x]
                out.putpixel((ox + x, oy + y), tuple(int(min(255, v)) for v in c) + (255,))
    return out


def skin(name):
    def gen(ctx):
        own = ctx.own.image(name)
        if own is None:
            return None
        ref = ctx.src.image(name) if ctx.has_ref else None
        if ref is None or ref is own:
            return own.copy()
        tone.ALPHA = 1.0
        new, why = tone.tone(own, ref, name)
        return new if new is not None else own.copy()
    return gen


def register(reg):
    reg['sun'] = sun
    reg['moon_phases'] = moon
    for n in ('entity_chicken', 'entity_cow', 'entity_creeper', 'entity_enderman', 'entity_enderman_eyes', 'entity_pig',
              'entity_player', 'entity_salmon', 'entity_shark', 'entity_sheep', 'entity_sheep_fur', 'entity_skeleton',
              'entity_slime', 'entity_spider', 'entity_spider_eyes', 'entity_zombie'):
        reg[n] = skin(n)
