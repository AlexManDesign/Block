"""Mob skins painted anew on our models' layout.

The skin's parts (runs of one colour of our base skin: face, eyes, body, legs, fur, clothes) stay
where the models' UVs read them; each part is painted with a material of its kind: hide (a fine
mottle of hair), wool (curls, each lit on top), cloth (a weave), flesh (soft), bone (smooth with
hairline cracks), gel (slime), and flat marks for eyes and mouths; lit on its top left and shaded
on its bottom right. Colours: the style's skin where its layout is ours (each part takes the tone
of the reference under it), else our own.
"""
import os
import sys
from PIL import Image
from core import (fbm, white, cells, rims, rng, Canvas, lum)

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
import redraw  # noqa: E402
import tone  # noqa: E402

# the main material of each mob's skin
MOB = {'entity_chicken': 'hide', 'entity_cow': 'hide', 'entity_creeper': 'hide', 'entity_enderman': 'hide',
       'entity_enderman_eyes': 'mark', 'entity_pig': 'flesh', 'entity_player': 'cloth', 'entity_salmon': 'scale',
       'entity_shark': 'flesh', 'entity_sheep': 'flesh', 'entity_sheep_fur': 'wool', 'entity_skeleton': 'bone',
       'entity_slime': 'gel', 'entity_spider': 'hide', 'entity_spider_eyes': 'mark', 'entity_zombie': 'cloth'}


def regions(px, w, h):
    pts = [p for p in px if p[3] > 0]
    cent, _, _, _ = redraw.palette(pts, 7, 350)
    lab = [min(range(len(cent)), key=lambda j: redraw.dist2(p, cent[j])) if p[3] > 0 else -1 for p in px]
    for _ in range(2):          # specks of a near colour are grain of the part round them
        for i in range(w * h):
            if lab[i] < 0:
                continue
            x, y = i % w, i // w
            nb = [lab[yy * w + xx] for xx, yy in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1))
                  if 0 <= xx < w and 0 <= yy < h and lab[yy * w + xx] >= 0]
            if nb and lab[i] not in nb:
                best = min(set(nb), key=lambda j: redraw.dist2(cent[j], cent[lab[i]]))
                if redraw.dist2(cent[best], cent[lab[i]]) < 9000:
                    lab[i] = best
    rid, info = [-1] * (w * h), []
    for s in range(w * h):
        if lab[s] < 0 or rid[s] >= 0:
            continue
        k, stack, members = len(info), [s], []
        rid[s] = k
        while stack:
            i = stack.pop()
            members.append(i)
            x, y = i % w, i // w
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                xx, yy = x + dx, y + dy
                j = yy * w + xx
                if 0 <= xx < w and 0 <= yy < h and rid[j] < 0 and lab[j] == lab[i]:
                    rid[j] = k
                    stack.append(j)
        info.append(members)
    return rid, info


def part_kind(main, mean, size):
    ch, l = max(mean[:3]) - min(mean[:3]), lum(mean)
    if size <= 6 or (size <= 24 and (l < 40 or ch > 110)):
        return 'mark'
    if main == 'cloth' and (mean[1] > mean[0] + 15 and mean[1] > mean[2] or (mean[0] > mean[1] > mean[2] and ch < 90
                                                                             and l > 120)):
        return 'flesh'          # a zombie's green or a player's face and hands
    return main


CONTRAST = {'hide': 0.45, 'wool': 0.45, 'cloth': 0.35, 'flesh': 0.3, 'bone': 0.35, 'gel': 0.35, 'scale': 0.45,
            'mark': 0.2}


def fields(rnd, w, h):
    ids, d1, d2, n = cells(rnd, w * h // 6, 1.0, w, h)
    curls, cd1, cd2, cn = cells(rnd, w * h // 10, 1.0, w, h)
    tone_ = [rnd.random() for _ in range(n)]
    ctone = [rnd.uniform(0.4, 1.0) for _ in range(cn)]
    wn = white(rnd, w * h)
    soft = fbm(rnd, [(8, 8, 0.6), (4, 4, 0.4)], w, h)
    cr = rims(curls, 0.35, 0.35, w, h, wrap=False)
    out = {}
    out['hide'] = [0.45 * tone_[ids[i]] + 0.25 * soft[i] + 0.3 * wn[i] for i in range(w * h)]
    out['wool'] = [ctone[curls[i]] + cr[i] - 0.3 * (cd2[i] - cd1[i] < 0.5) + 0.1 * wn[i] for i in range(w * h)]
    out['cloth'] = [0.5 + 0.18 * ((i % w + i // w) % 2) + 0.2 * soft[i] + 0.15 * wn[i] - 0.15 * (i // w % 4 == 3)
                    for i in range(w * h)]
    out['flesh'] = [0.6 * soft[i] + 0.25 * wn[i] for i in range(w * h)]
    out['bone'] = [0.6 + 0.3 * soft[i] + 0.1 * wn[i] - (0.8 if wn[i] > 0.97 else 0) for i in range(w * h)]
    out['gel'] = [0.7 * soft[i] + 0.1 * wn[i] for i in range(w * h)]
    out['scale'] = [0.5 + 0.25 * ((i % w // 2 + i // w // 2) % 2) + 0.2 * soft[i] + 0.1 * wn[i] for i in range(w * h)]
    out['mark'] = [0.5 + 0.1 * wn[i] for i in range(w * h)]
    return out


def paint(style, name, own, ref):
    own = own.convert('RGBA')
    w, h = own.size
    px = list(own.get_flattened_data())
    toned = None
    if ref is not None:
        tone.ALPHA = 1.0
        toned, _ = tone.tone(own, ref, name)
    tp = list(toned.get_flattened_data()) if toned is not None else px
    rid, info = regions(px, w, h)
    rnd = rng(name)
    fl = fields(rnd, w, h)
    edges = rims(rid, 0.2, 0.2, w, h, wrap=False)
    main = MOB.get(name, 'hide')
    c = Canvas(style, name, w, h)
    for members in info:
        mean = tuple(sum(px[i][t] for i in members) / len(members) for t in range(4))
        col = tuple(sum(tp[i][t] for i in members) / len(members) for t in range(3))
        kd = part_kind(main, mean, len(members))
        con = CONTRAST[kd]
        ramp_src = [tuple(max(0, min(255, round(col[t] * (1 - con / 2 + con * j / 15)))) for t in range(3)) + (255,)
                    for j in range(16)]
        mask = [False] * (w * h)
        for i in members:
            mask[i] = True
        f = fl[kd]
        alpha = round(mean[3]) if mean[3] < 250 else 255
        c.part([f[i] + (0 if kd == 'mark' else edges[i]) for i in range(w * h)], ramp_src, mask, alpha=alpha)
    return c.image()


def skin(name):
    def gen(ctx):
        own = ctx.own.image(name)
        if own is None:
            return None
        ref = ctx.src.image(name) if ctx.has_ref else None
        if ref is not None and (ref is own or ref.width != own.width or ref.height < own.height):
            ref = None
        return paint(ctx.style, name, own, ref)
    return gen
