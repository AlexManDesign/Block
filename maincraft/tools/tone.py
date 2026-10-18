"""Brings the colours of our own pack closer to a reference pack's, keeping our drawings.

    python3 tools/tone.py REF_TEXTURES_DIR SRC_DIR OUT_DIR [name ...]

REF_TEXTURES_DIR is the textures/ folder of a resource pack (block/, item/, entity/...). It is
read for colours only and nothing of it is written out: each of our pictures is split into its
palette colours (as tools/redraw.py does), and every colour is moved part of the way (ALPHA)
towards the mean colour the reference picture has under that colour's pixels. Our layout,
shading and grain stay as they are, pixel by pixel; only the tones shift. A colour whose pixels
cover a muddle of reference colours (the drawings do not line up there) is left alone, and so is
a picture whose tinting differs (grey and tinted in game against coloured, or the other way).
"""
import math
import os
import sys
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from redraw import lum, palette, dist2, frames_of  # noqa: E402

ALPHA = 0.75        # how far a colour moves towards the reference tone
MUDDLE = 38         # luminance spread of the reference under one colour beyond which it is left alone
ALIGNED = 0.35      # lightness correlation with the reference above which the drawings line up
HUE = 45            # hue change (degrees) beyond which a colour is left alone
APART = 5           # the least step (most changed channel) a pixel keeps from the reference's
GREY = 12           # mean (max - min channel) under which a picture counts as grey

# pictures our game tints (build.py TINT) and pictures the reference game tints
TINTED = {'grass_block_top', 'short_grass', 'fern', 'tall_grass_top', 'tall_grass_bottom', 'large_fern_top',
          'large_fern_bottom', 'oak_leaves', 'vine', 'dark_oak_leaves', 'jungle_leaves', 'acacia_leaves',
          'lily_pad', 'water_still'}
REF_TINTED = TINTED | {'birch_leaves', 'spruce_leaves', 'mangrove_leaves', 'melon_stem', 'pumpkin_stem',
                       'attached_melon_stem', 'attached_pumpkin_stem'}

# our skins -> reference skins drawn on the same layout
ENTITY = {
    'entity_creeper': 'entity/creeper/creeper.png', 'entity_enderman': 'entity/enderman/enderman.png',
    'entity_enderman_eyes': 'entity/enderman/enderman_eyes.png', 'entity_skeleton': 'entity/skeleton/skeleton.png',
    'entity_slime': 'entity/slime/slime.png', 'entity_spider': 'entity/spider/spider.png',
    'entity_spider_eyes': 'entity/spider_eyes.png', 'entity_zombie': 'entity/zombie/zombie.png',
    'entity_sheep': 'entity/sheep/sheep.png', 'entity_sheep_fur': 'entity/sheep/sheep_wool.png',
    'entity_player': 'entity/player/wide/steve.png', 'entity_salmon': 'entity/fish/salmon.png',
    'entity_cow': 'entity/cow/temperate_cow.png', 'entity_pig': 'entity/pig/temperate_pig.png',
    'entity_chicken': 'entity/chicken/temperate_chicken.png',
}


def ref_path(ref, name):
    if name in ENTITY:
        return os.path.join(ref, ENTITY[name])
    for p in (os.path.join(ref, 'block', name + '.png'),
              os.path.join(ref, 'item', name[5:] + '.png') if name.startswith('item_') else None):
        if p and os.path.exists(p):
            return p
    return None


def correlation(a, b):
    n = len(a)
    if n < 8:
        return 0
    ma, mb = sum(a) / n, sum(b) / n
    va = sum((x - ma) ** 2 for x in a)
    vb = sum((y - mb) ** 2 for y in b)
    if va == 0 or vb == 0:
        return 0
    return sum((x - ma) * (y - mb) for x, y in zip(a, b)) / math.sqrt(va * vb)


def chroma(c):
    return max(c[:3]) - min(c[:3])


def hue_gap(a, b):
    """angle in degrees between the hues of two colours, 0 when either is near grey"""
    def hue(c):
        if chroma(c) < 24:
            return None
        r, g, b = c[:3]
        return math.degrees(math.atan2(math.sqrt(3) * (g - b), 2 * r - g - b))
    ha, hb = hue(a), hue(b)
    if ha is None or hb is None:
        return 0
    d = abs(ha - hb) % 360
    return min(d, 360 - d)


def pixels(im):
    return list(im.get_flattened_data() if hasattr(im, 'get_flattened_data') else im.getdata())


def saturation(px):
    px = [p for p in px if p[3] > 127]
    return sum(max(p[:3]) - min(p[:3]) for p in px) / max(1, len(px))


def tone(im, ref, name):
    im = im.convert('RGBA')
    frames = frames_of(im)
    w, h = frames[0].size
    ref = ref.convert('RGBA')
    rf = ref
    if name in ENTITY:
        if rf.width != w or rf.height < h:
            return None, 'layout'
        rf = rf.crop((0, 0, w, h))
    else:
        rf = rf.crop((0, 0, rf.width, min(rf.height, rf.width)))
        if rf.size != (w, h):
            rf = rf.resize((w, h), Image.BOX)
    rp = pixels(rf)
    px = [pixels(f) for f in frames]
    # a picture the game tints (grass, leaves, water) is compared by lightness only, and only with
    # a reference that is tinted too (grey)
    grey_ours = name in TINTED
    if grey_ours != (name in REF_TINTED) or grey_ours and (saturation(px[0]) >= GREY or saturation(rp) >= GREY):
        return None, 'tint'
    pts = [p for fr in px for p in fr if p[3] > 0]
    if not pts:
        return None, 'empty'
    skin = name.startswith('entity_')
    cent, _, _, _ = palette(pts, 16 if skin else 8, 30 if skin else 50)

    def nearest(p):
        return min(range(len(cent)), key=lambda j: dist2(p, cent[j]))
    labs = [[nearest(p) if p[3] > 0 else -1 for p in fr] for fr in px]
    # do the drawings line up (bricks over bricks)? then each colour takes the tone under its own
    # pixels; if not (noise over noise), the tone of the same rank of lightness
    both = [i for i, l in enumerate(labs[0]) if l >= 0 and rp[i][3] > 127]
    aligned = correlation([lum(px[0][i]) for i in both], [lum(rp[i]) for i in both]) > ALIGNED
    by_rank = {}
    if not aligned:
        mine = sorted((i for i, l in enumerate(labs[0]) if l >= 0), key=lambda i: lum(px[0][i]))
        theirs = sorted((q for q in rp if q[3] > 127), key=lum)
        for r, i in enumerate(mine):
            if theirs:
                by_rank.setdefault(labs[0][i], []).append(theirs[r * len(theirs) // len(mine)])
    delta, moved = [], 0
    for j, c in enumerate(cent):
        under = by_rank.get(j, []) if not aligned else [rp[i] for i, l in enumerate(labs[0]) if l == j and rp[i][3] > 127]
        if len(under) < 3:
            delta.append((0, 0, 0)); continue
        ls = [lum(q) for q in under]
        ml = sum(ls) / len(ls)
        if math.sqrt(sum((v - ml) ** 2 for v in ls) / len(ls)) > MUDDLE:
            delta.append((0, 0, 0)); continue
        m = [sum(q[t] for q in under) / len(under) for t in range(3)]
        if grey_ours:                       # tinted in game: only the lightness counts
            m = [ml] * 3
        elif hue_gap(c, m) > HUE or chroma(c) > 40 and chroma(m) < 24:
            # a petal over a stem, a leaf over a grey stalk: the drawings do not line up here
            delta.append((0, 0, 0)); continue
        delta.append(tuple(ALPHA * (m[t] - c[t]) for t in range(3)))
        moved += 1
    out = []
    for fr, lab in zip(px, labs):
        res = []
        for i, (p, l) in enumerate(zip(fr, lab)):
            if l < 0:
                res.append(p)
                continue
            d = delta[l]
            q = [max(0, min(255, round(p[t] + d[t]))) for t in range(3)]
            r = rp[i]
            if r[3] > 0 and max(abs(q[t] - r[t]) for t in range(3)) < APART:
                # a flat colour landed on the reference's own: keep ours a step apart, the same
                # way for the whole colour
                sgn = 1 if sum(c - v for c, v in zip(cent[l][:3], r[:3])) >= 0 else -1
                for _ in range(2):
                    q2 = [max(0, min(255, r[t] + sgn * APART)) for t in range(3)]
                    if max(abs(q2[t] - r[t]) for t in range(3)) >= APART:
                        q = q2
                        break
                    sgn = -sgn
            res.append(tuple(q) + (p[3],))
        f = Image.new('RGBA', (w, h)); f.putdata(res); out.append(f)
    if len(out) == 1:
        return out[0], '%s %d/%d' % ('aligned' if aligned else 'by rank', moved, len(cent))
    strip = Image.new('RGBA', (w, h * len(out)))
    for i, f in enumerate(out):
        strip.paste(f, (0, i * h))
    return strip, '%s %d/%d' % ('aligned' if aligned else 'by rank', moved, len(cent))


def main():
    ref, src, dst = sys.argv[1:4]
    only = set(sys.argv[4:])
    os.makedirs(dst, exist_ok=True)
    for fn in sorted(os.listdir(src)):
        n = fn[:-4]
        if not fn.endswith('.png') or (only and n not in only):
            continue
        im = Image.open(os.path.join(src, fn))
        rp = ref_path(ref, n)
        new, why = tone(im, Image.open(rp), n) if rp else (None, 'no reference')
        (new or im).save(os.path.join(dst, fn))
        print(n, why)


if __name__ == '__main__':
    main()
