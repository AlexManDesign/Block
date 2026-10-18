"""Moving pictures: water, lava, fire, sea lantern, magma, prismarine, the nether stems; and a few
odd ones (dried kelp, bamboo block)."""
import math
from PIL import Image
from core import (N, at, fbm, white, mix, cells, rims, rng, Canvas, select, lum)
import natural
import wood


def looped(rnd, octaves, frames):
    """fields that move in a loop: two noises crossfaded round a circle, so the last frame meets the first"""
    a, b = fbm(rnd, octaves), fbm(rnd, octaves)
    out = []
    for k in range(frames):
        t = 2 * math.pi * k / frames
        out.append([0.5 + (a[i] - 0.5) * math.cos(t) + (b[i] - 0.5) * math.sin(t) for i in range(N * N)])
    return out


def strip(images):
    if len(images) == 1:
        return images[0]
    s = Image.new('RGBA', (N, N * len(images)))
    for k, im in enumerate(images):
        s.paste(im, (0, k * N))
    return s


def animated(name, frames, field_frames, alpha_from_src=False):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name)
        a = 255
        if alpha_from_src and src:
            al = [p[3] for p in src if p[3] > 0]
            a = round(sum(al) / len(al)) if al else 255
        out = []
        for k, f in enumerate(field_frames(rnd, frames)):
            c = Canvas(ctx.style, name, ref=ctx.ref(name) if k == 0 else None)
            c.part(f, src, alpha=a)
            out.append(c.image())
        return strip(out)
    return gen


def water_f(rnd, frames):
    """ripples: soft bands drifting diagonally, a fine glitter over them"""
    loops = looped(rnd, [(8, 4, 0.5), (4, 4, 0.3), (4, 2, 0.2)], frames)
    out = []
    for k, f in enumerate(loops):
        s = k * N // frames
        out.append([f[at(i % N + s, i // N + s)] for i in range(N * N)])
    return out


def lava_f(rnd, frames):
    loops = looped(rnd, [(8, 8, 0.5), (4, 4, 0.3), (2, 2, 0.2)], frames)
    ids, d1, d2, n = cells(rnd, 9, 1.0)
    crust = [d2[i] - d1[i] < 1.0 for i in range(N * N)]
    return [[f[i] - (0.5 if crust[i] else 0) for i in range(N * N)] for f in loops]


def glow_f(rnd, frames):
    """sea lantern: a bright core in a framed lamp, pulsing"""
    loops = looped(rnd, [(8, 8, 0.6), (4, 4, 0.4)], frames)
    out = []
    for f in loops:
        g = []
        for i in range(N * N):
            x, y = i % N, i // N
            edge = min(x, y, N - 1 - x, N - 1 - y)
            core = 1 - math.hypot(x - 7.5, y - 7.5) / 10
            g.append((0.2 if edge == 0 else 0.4 if edge == 1 else 0.6 + 0.4 * core) + 0.25 * (f[i] - 0.5))
        out.append(g)
    return out


def magma_f(rnd, frames):
    ids, d1, d2, n = cells(rnd, 12, 1.0)
    loops = looped(rnd, [(4, 4, 1.0)], frames)
    return [[(1.0 + 0.3 * f[i]) if d2[i] - d1[i] < 0.9 else 0.2 + 0.3 * f[i] for i in range(N * N)] for f in loops]


def prismarine_f(rnd, frames):
    base = natural.blotch_f(0.6, 16)(rnd)
    loops = looped(rnd, [(8, 8, 1.0)], frames)
    return [[0.7 * base[i] + 0.3 * f[i] for i in range(N * N)] for f in loops]


def stem_f(rnd, frames):
    base = wood.bark_f(rnd)
    loops = looped(rnd, [(4, 8, 1.0)], frames)
    return [[base[i] + 0.25 * (f[i] - 0.5) for i in range(N * N)] for f in loops]


def fire(name):
    """flames: tongues of uneven height, hottest at the foot"""
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'fire')
        heights = fbm(rnd, [(4, 16, 0.6), (2, 16, 0.4)])
        w = white(rnd)
        mask, f = [False] * (N * N), [0.0] * (N * N)
        for x in range(N):
            h = 6 + 9 * heights[x]
            for y in range(N):
                up = N - y
                if up <= h:
                    mask[at(x, y)] = True
                    f[at(x, y)] = 1 - up / h + 0.2 * w[at(x, y)]
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(f, src, mask)
        return c.image()
    return gen


def dried_kelp(name):
    def gen(ctx):
        rnd = rng(name)
        w = white(rnd)
        f = [0.5 + 0.3 * math.cos(2 * math.pi * (i % N) / 4) - (0.6 if i // N in (3, 12) else 0) + 0.15 * w[i]
             for i in range(N * N)]
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.part(f, ctx.px(name))
        return c.image()
    return gen


def register(reg):
    reg['water_still'] = animated('water_still', 96, water_f, alpha_from_src=True)
    reg['lava_still'] = animated('lava_still', 1, lava_f)
    reg['sea_lantern'] = animated('sea_lantern', 10, glow_f)
    reg['magma'] = animated('magma', 3, magma_f)
    reg['prismarine'] = animated('prismarine', 4, prismarine_f)
    reg['crimson_stem'] = animated('crimson_stem', 5, stem_f)
    reg['warped_stem'] = animated('warped_stem', 5, stem_f)
    reg['fire'] = fire('fire')
    reg['dried_kelp_side'] = dried_kelp('dried_kelp_side')
    reg['bamboo_block'] = dried_kelp('bamboo_block')
