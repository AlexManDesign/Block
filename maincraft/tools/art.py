"""Own textures for blocks the pack does not have, drawn in code (16x16, RGBA).

Everything here is drawn from scratch or derived from this project's own pack textures (planks,
logs, stone...) by recolouring and compositing. build.py adds the results to the atlas.
"""
import random
from PIL import Image

T = (0, 0, 0, 0)


def new(c=T):
    return Image.new('RGBA', (16, 16), c)


def rng(seed):
    return random.Random(seed)


def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c[:3]) + ((c[3],) if len(c) > 3 else (255,))


def noisy(base, var, seed, alpha=255):
    """flat colour with per-pixel brightness noise"""
    r = rng(seed)
    im = new()
    px = im.load()
    for y in range(16):
        for x in range(16):
            k = 1 + (r.random() - 0.5) * 2 * var
            px[x, y] = shade(base, k)[:3] + (alpha,)
    return im


def recolor(img, rgb, keep=0.35):
    """grey-scale of img mapped onto rgb (keep: how much of the original contrast stays)"""
    out = img.convert('RGBA').copy()
    px = out.load()
    for y in range(out.height):
        for x in range(out.width):
            r, g, b, a = px[x, y]
            l = (r * 0.3 + g * 0.59 + b * 0.11) / 255
            k = 1 - keep + keep * 2 * l
            px[x, y] = (min(255, int(rgb[0] * k)), min(255, int(rgb[1] * k)), min(255, int(rgb[2] * k)), a)
    return out


def rect(im, x0, y0, x1, y1, c):
    px = im.load()
    for y in range(max(0, y0), min(16, y1 + 1)):
        for x in range(max(0, x0), min(16, x1 + 1)):
            px[x, y] = c if len(c) == 4 else c + (255,)


def frame(im, c, w=1):
    for i in range(w):
        rect(im, i, i, 15 - i, i, c); rect(im, i, 15 - i, 15 - i, 15 - i, c)
        rect(im, i, i, i, 15 - i, c); rect(im, 15 - i, i, 15 - i, 15 - i, c)


def dots(im, colors, n, seed, area=(0, 0, 15, 15)):
    r = rng(seed)
    px = im.load()
    for _ in range(n):
        x, y = r.randint(area[0], area[2]), r.randint(area[1], area[3])
        px[x, y] = r.choice(colors) + (255,)


def rot90(im):
    return im.transpose(Image.ROTATE_90)


# ------------------------------------------------------------------ village workstations
def barrel(load):
    side = rot90(load('spruce_planks').convert('RGBA'))          # staves upright
    band = (52, 46, 44)
    for y in (2, 3, 12, 13):
        rect(side, 0, y, 15, y, shade(band, 1.1 if y in (2, 12) else 0.85))
    top = load('spruce_planks').convert('RGBA').copy()
    frame(top, (60, 42, 26), 2)
    rect(top, 2, 2, 13, 2, shade((52, 46, 44), 1)); rect(top, 2, 13, 13, 13, shade((52, 46, 44), 1))
    bottom = top.copy()
    opened = top.copy()
    rect(opened, 3, 3, 12, 12, (34, 24, 15))
    return {'barrel_side': side, 'barrel_top': top, 'barrel_bottom': bottom, 'barrel_top_open': opened}


def blast_furnace(load):
    stone = recolor(load('smooth_stone'), (120, 120, 124), 0.5)
    side = stone.copy(); frame(side, (66, 66, 72))
    rect(side, 0, 7, 15, 8, (78, 78, 86))
    front = side.copy()
    rect(front, 4, 4, 11, 11, (40, 40, 44))
    for x in (5, 7, 9):
        rect(front, x, 4, x, 11, (88, 88, 96))
    rect(front, 4, 12, 11, 12, (70, 70, 78))
    top = stone.copy(); frame(top, (66, 66, 72), 2)
    for i in (5, 10):
        rect(top, 2, i, 13, i, (82, 82, 90)); rect(top, i, 2, i, 13, (82, 82, 90))
    return {'blast_furnace_side': side, 'blast_furnace_front': front, 'blast_furnace_top': top}


def smoker(load):
    stone = load('stone').convert('RGBA')
    logs = load('spruce_log').convert('RGBA')
    side = stone.copy()
    side.paste(logs.crop((0, 0, 3, 16)), (0, 0)); side.paste(logs.crop((13, 0, 16, 16)), (13, 0))
    rect(side, 0, 0, 15, 2, (74, 58, 42)); rect(side, 0, 13, 15, 15, (74, 58, 42))
    front = side.copy()
    rect(front, 4, 5, 11, 11, (30, 26, 22)); rect(front, 4, 5, 11, 5, (90, 70, 50))
    top = load('stone').convert('RGBA').copy(); frame(top, (74, 58, 42), 2)
    for x in range(4, 12, 2):
        rect(top, x, 4, x, 11, (40, 36, 32))
    bottom = stone.copy()
    return {'smoker_side': side, 'smoker_front': front, 'smoker_top': top, 'smoker_bottom': bottom}


def cartography_table(load):
    wood = load('dark_oak_planks').convert('RGBA')
    top = wood.copy()
    paper = noisy((214, 198, 160), 0.05, 31)
    top.paste(paper.crop((0, 0, 12, 12)), (2, 2))
    for (x0, y0, x1, y1) in ((4, 5, 9, 5), (9, 5, 9, 9), (5, 9, 12, 9), (6, 7, 7, 7)):
        rect(top, x0, y0, x1, y1, (120, 96, 60))
    rect(top, 11, 3, 12, 4, (160, 40, 40))
    side = wood.copy()
    rect(side, 1, 2, 14, 4, (214, 198, 160))
    rect(side, 3, 3, 12, 3, (150, 130, 96))
    return {'cartography_table_top': top, 'cartography_table_side1': side, 'cartography_table_side3': side.copy()}


def fletching_table(load):
    wood = load('birch_planks').convert('RGBA')
    top = wood.copy()
    rect(top, 3, 7, 12, 8, (110, 80, 50))                       # arrow shaft
    rect(top, 12, 6, 13, 9, (160, 160, 168))                    # head
    for i, x in enumerate((3, 4, 5)):
        rect(top, x, 5 + (i & 1), x, 6 + (i & 1), (230, 230, 230)); rect(top, x, 9 - (i & 1), x, 10 - (i & 1), (230, 230, 230))
    side = wood.copy(); frame(side, (150, 128, 90))
    front = side.copy()
    for r, c in ((5, (200, 50, 40)), (3, (236, 236, 236)), (1, (200, 50, 40))):
        rect(front, 8 - r, 8 - r, 7 + r, 7 + r, c)
    return {'fletching_table_top': top, 'fletching_table_side': side, 'fletching_table_front': front}


def smithing_table(load):
    iron = (58, 60, 68)
    top = noisy((44, 46, 52), 0.08, 41)
    for i in (5, 10):
        rect(top, 0, i, 15, i, iron); rect(top, i, 0, i, 15, iron)
    frame(top, (30, 30, 34))
    wood = load('dark_oak_planks').convert('RGBA')
    side = wood.copy(); rect(side, 0, 0, 15, 3, (44, 46, 52)); rect(side, 0, 3, 15, 3, iron)
    front = side.copy(); rect(front, 5, 7, 6, 13, (120, 124, 132)); rect(front, 9, 7, 10, 13, (120, 124, 132)); rect(front, 5, 7, 10, 8, (120, 124, 132))
    bottom = wood.copy()
    return {'smithing_table_top': top, 'smithing_table_side': side, 'smithing_table_front': front, 'smithing_table_bottom': bottom}


def loom(load):
    wood = load('oak_planks').convert('RGBA')
    side = wood.copy(); frame(side, (120, 90, 50))
    front = side.copy()
    for x in range(3, 13, 2):
        rect(front, x, 2, x, 13, (230, 226, 210))
    rect(front, 2, 6, 13, 7, (150, 60, 50))
    top = wood.copy(); rect(top, 2, 6, 13, 9, (230, 226, 210))
    bottom = wood.copy()
    return {'loom_side': side, 'loom_front': front, 'loom_top': top, 'loom_bottom': bottom}


def lectern(load):
    wood = load('oak_planks').convert('RGBA')
    top = wood.copy(); frame(top, (120, 90, 50))
    sides = rot90(wood.copy()); frame(sides, (120, 90, 50))
    base = wood.copy(); rect(base, 0, 6, 15, 7, (120, 90, 50))
    front = sides.copy()
    return {'lectern_top': top, 'lectern_sides': sides, 'lectern_base': base, 'lectern_front': front}


def grindstone(load):
    stone = recolor(load('stone'), (150, 150, 150), 0.6)
    side = stone.copy()
    rnd = new()
    rect(rnd, 0, 0, 15, 15, (136, 136, 136))
    dots(rnd, [(116, 116, 116), (160, 160, 160)], 50, 51)
    pivot = load('dark_oak_planks').convert('RGBA')
    return {'grindstone_side': side, 'grindstone_round': rnd, 'grindstone_pivot': pivot}


def stonecutter(load):
    stone = load('stone').convert('RGBA')
    top = stone.copy(); rect(top, 7, 1, 8, 14, (40, 40, 44))
    side = stone.copy(); rect(side, 0, 0, 15, 1, (90, 90, 96)); rect(side, 0, 7, 15, 7, (70, 70, 76))
    bottom = stone.copy()
    saw = new()
    for x in range(1, 15):
        h = 5 if x % 2 else 6
        rect(saw, x, 16 - h, x, 15, (170, 172, 180))
    rect(saw, 1, 15, 14, 15, (120, 122, 128))
    return {'stonecutter_top': top, 'stonecutter_side': side, 'stonecutter_bottom': bottom, 'stonecutter_saw': saw}


def bell():
    im = new()
    for y in range(16):
        for x in range(16):
            l = 0.75 + 0.35 * (1 - abs(x - 6) / 10) - y * 0.01
            im.load()[x, y] = shade((236, 184, 50), l)
    rect(im, 0, 0, 15, 0, (150, 110, 30))
    return {'bell_body': im}


def lantern():
    im = new()
    iron = (54, 56, 64)
    rect(im, 0, 0, 5, 1, iron)                  # cap (uv 0..6, 0..2)
    rect(im, 0, 2, 5, 8, iron)                  # body sides (uv 0..6, 2..9)
    rect(im, 1, 3, 4, 7, (250, 200, 90)); rect(im, 2, 4, 3, 6, (255, 236, 160))
    rect(im, 0, 9, 5, 14, iron)                 # body top / bottom (uv 0..6, 9..15)
    rect(im, 1, 10, 4, 13, (70, 72, 80))
    rect(im, 11, 1, 13, 2, iron); rect(im, 11, 10, 13, 11, iron)   # handle
    return {'lantern': im}


def campfire(load):
    log = load('oak_log').convert('RGBA')
    lit = log.copy()
    dots(lit, [(255, 140, 40), (255, 190, 60), (210, 70, 20)], 22, 61, (0, 5, 15, 10))
    frames = []
    r = rng(71)
    for f in range(8):
        im = new()
        px = im.load()
        for x in range(16):
            h = 6 + int(5 * abs(((x * 7 + f * 3) % 11) / 11 - 0.5) * 2) + r.randint(0, 3)
            for y in range(16 - h, 16):
                t = (y - (16 - h)) / h
                px[x, y] = (255, int(120 + 120 * t), int(30 + 40 * t), 255)
        frames.append(im)
    return {'campfire_log': log, 'campfire_log_lit': lit}, {'campfire_fire': frames}


def brewing_stand():
    im = new()
    rect(im, 7, 2, 8, 15, (150, 120, 70))                       # rod
    for x0 in (1, 11):                                           # bottles
        rect(im, x0, 9, x0 + 3, 14, (190, 210, 230)); rect(im, x0 + 1, 7, x0 + 2, 8, (190, 210, 230))
        rect(im, x0 + 1, 11, x0 + 2, 13, (140, 60, 170))
    rect(im, 3, 6, 12, 6, (150, 120, 70))
    base = noisy((92, 92, 96), 0.12, 81)
    return {'brewing_stand': im, 'brewing_stand_base': base}


def cauldron():
    side = noisy((62, 62, 68), 0.10, 91)
    frame(side, (44, 44, 50))
    rect(side, 4, 13, 11, 15, T)                                 # gap between the legs
    top = noisy((70, 70, 78), 0.08, 92)
    rect(top, 2, 2, 13, 13, T)
    inner = noisy((48, 48, 54), 0.10, 93)
    bottom = noisy((52, 52, 58), 0.10, 94)
    return {'cauldron_side': side, 'cauldron_top': top, 'cauldron_inner': inner, 'cauldron_bottom': bottom}


def composter(load):
    wood = load('oak_planks').convert('RGBA')
    side = rot90(wood.copy())
    for x in (4, 8, 12):
        rect(side, x, 0, x, 15, (70, 50, 30))
    top = wood.copy(); rect(top, 2, 2, 13, 13, T)
    bottom = wood.copy()
    compost = noisy((92, 64, 36), 0.25, 101)
    ready = compost.copy(); dots(ready, [(230, 230, 220), (200, 210, 190)], 24, 102)
    return {'composter_side': side, 'composter_top': top, 'composter_bottom': bottom, 'composter_compost': compost, 'composter_ready': ready}


def beetroots():
    out = {}
    green, dark, red = (70, 150, 40), (40, 100, 28), (150, 30, 40)
    for s in range(4):
        im = new()
        h = 4 + s * 3
        for i, x in enumerate((3, 7, 11)):
            hh = h - (i & 1)
            rect(im, x, 16 - hh, x, 15, dark)
            rect(im, x - 1, 16 - hh, x + 1, 16 - hh + 1, green)
            if s >= 2: rect(im, x + 1, 16 - hh + 2, x + 2, 16 - hh + 3, green)
        if s == 3:
            for x in (3, 11):
                rect(im, x - 1, 13, x + 1, 15, red)
        out['beetroots_stage%d' % s] = im
    item = new()
    rect(item, 5, 7, 10, 12, red); rect(item, 6, 13, 9, 13, red); rect(item, 7, 14, 8, 14, (110, 20, 28))
    rect(item, 6, 3, 6, 6, green); rect(item, 9, 2, 9, 6, green); rect(item, 7, 4, 8, 6, dark)
    seeds = new()
    for (x, y) in ((4, 6), (9, 4), (6, 10), (11, 9), (8, 13)):
        rect(seeds, x, y, x + 1, y + 1, (120, 70, 50))
    out['item_beetroot'] = item; out['item_beetroot_seeds'] = seeds
    return out


# ------------------------------------------------------------------ caves
def pointed_dripstone(load):
    base = recolor(load('dripstone_block'), (134, 107, 92), 0.6)
    out = {}
    # half-widths per row (top of the texture = attached end for "down")
    widths = {'base': [4] * 16, 'middle': [3] * 16, 'frustum': [3 - y // 6 for y in range(16)],
              'tip': [max(0, 2 - y // 6) for y in range(16)], 'tip_merge': [max(0, 2 - abs(y - 8) // 3) for y in range(16)]}
    for name, ws in widths.items():
        im = new()
        bp, px = base.load(), im.load()
        for y in range(16):
            w = ws[y]
            for x in range(8 - w - 1, 8 + w + 1):
                if 0 <= x < 16 and (w > 0 or x in (7, 8)) and (y < 15 or name not in ('tip',)):
                    px[x, y] = bp[x, y]
        out['pointed_dripstone_down_' + name] = im
        out['pointed_dripstone_up_' + name] = im.transpose(Image.FLIP_TOP_BOTTOM)
    return out


def azalea(load):
    leaves = load('azalea_leaves').convert('RGBA')
    fl = load('flowering_azalea_leaves').convert('RGBA')
    plant = new()
    rect(plant, 7, 0, 8, 15, (96, 76, 46)); rect(plant, 5, 3, 6, 4, (96, 76, 46)); rect(plant, 9, 6, 10, 7, (96, 76, 46))
    return {'azalea_top': leaves, 'azalea_side': leaves.copy(), 'azalea_plant': plant, 'flowering_azalea_top': fl, 'flowering_azalea_side': fl.copy()}


def dripleaf():
    leaf = new()
    for y in range(16):
        for x in range(16):
            d = abs(x - 7.5) + abs(y - 7.5) * 0.6
            if d < 9:
                leaf.load()[x, y] = shade((92, 160, 52), 1.05 - d * 0.03)
    for i in range(16):
        rect(leaf, 7, i, 8, i, (70, 128, 40))
    for y in (4, 8, 12):
        rect(leaf, 3, y, 12, y, (78, 140, 44))
    stem = new(); rect(stem, 7, 0, 8, 15, (80, 130, 50))
    side = new(); rect(side, 0, 14, 15, 15, (78, 140, 44))
    small = leaf.resize((10, 10), Image.NEAREST)
    s = new(); s.paste(small, (3, 0))
    sstem = new(); rect(sstem, 7, 0, 8, 15, (80, 130, 50)); rect(sstem, 4, 9, 7, 10, (92, 160, 52)); rect(sstem, 8, 5, 11, 6, (92, 160, 52))
    return {'big_dripleaf_top': leaf, 'big_dripleaf_side': side, 'big_dripleaf_stem': stem, 'big_dripleaf_tip': side.copy(),
            'small_dripleaf_top': s, 'small_dripleaf_side': side.copy(), 'small_dripleaf_stem_top': sstem, 'small_dripleaf_stem_bottom': stem.copy()}


def spore_blossom():
    im = new()
    for a in range(8):
        import math
        for r in range(2, 8):
            x = int(7.5 + math.cos(a * math.pi / 4) * r); y = int(7.5 + math.sin(a * math.pi / 4) * r)
            im.load()[x, y] = shade((236, 120, 180), 1.1 - r * 0.06)
    rect(im, 6, 6, 9, 9, (250, 200, 220))
    base = noisy((70, 130, 50), 0.15, 111)
    return {'spore_blossom': im, 'spore_blossom_base': base}


def strands(seed, n, light, dark, length=16):
    r = rng(seed)
    im = new()
    for _ in range(n):
        x = r.randint(1, 14)
        top = r.randint(0, 3)
        for y in range(top, min(16, top + r.randint(length // 2, length))):
            if r.random() < 0.25: x = max(0, min(15, x + r.choice((-1, 1))))
            im.load()[x, y] = (light if r.random() < 0.6 else dark) + (255,)
    return im


def sculk():
    frames = []
    for f in range(2):
        im = noisy((14, 36, 44), 0.25, 121)
        dots(im, [(30, 120, 140) if f == 0 else (60, 190, 210), (20, 80, 96)], 26, 122 + f)
        frames.append(im)
    vein = new()
    r = rng(131)
    for _ in range(4):
        x, y = r.randint(0, 15), r.randint(0, 15)
        for _ in range(14):
            vein.load()[x, y] = (24, 70, 84, 255)
            x = max(0, min(15, x + r.choice((-1, 0, 1)))); y = max(0, min(15, y + r.choice((-1, 0, 1))))
    sensor_side = frames[0].copy(); rect(sensor_side, 0, 0, 15, 7, T); rect(sensor_side, 0, 8, 15, 8, (50, 160, 180))
    sensor_bottom = frames[0].copy()
    tendril = new(); rect(tendril, 7, 4, 8, 15, (40, 150, 170)); rect(tendril, 6, 2, 9, 4, (90, 220, 240))
    shr_side = frames[0].copy(); rect(shr_side, 0, 0, 15, 4, (220, 214, 194)); rect(shr_side, 2, 1, 13, 1, (190, 182, 160))
    shr_top = new((220, 214, 194, 255)); rect(shr_top, 3, 3, 12, 12, (18, 40, 48))
    shr_inner = frames[1].copy()
    cat_side = frames[0].copy(); frame(cat_side, (220, 214, 194)); rect(cat_side, 5, 5, 10, 10, (60, 190, 210))
    cat_top = frames[0].copy(); frame(cat_top, (220, 214, 194), 2)
    out = {'sculk_vein': vein, 'sculk_sensor_side': sensor_side, 'sculk_sensor_bottom': sensor_bottom, 'sculk_sensor_top': frames[0].copy(),
           'sculk_sensor_tendril_inactive': tendril, 'sculk_shrieker_side': shr_side, 'sculk_shrieker_top': shr_top,
           'sculk_shrieker_inner_top': shr_inner, 'sculk_shrieker_bottom': frames[0].copy(),
           'sculk_catalyst_side': cat_side, 'sculk_catalyst_top': cat_top, 'sculk_catalyst_bottom': frames[0].copy(), 'hanging_roots': strands(141, 6, (128, 96, 72), (90, 66, 50), 12)}
    return out, {'sculk': frames}


# ------------------------------------------------------------------ pale garden
def pale_garden(load):
    out = {}
    out['pale_oak_log'] = recolor(load('dark_oak_log'), (180, 168, 160), 0.6)
    out['pale_oak_log_top'] = recolor(load('dark_oak_log_top'), (210, 196, 186), 0.5)
    out['pale_oak_planks'] = recolor(load('oak_planks'), (228, 216, 212), 0.45)
    out['pale_oak_leaves'] = recolor(load('dark_oak_leaves'), (120, 130, 116), 0.7)
    out['pale_moss_block'] = recolor(load('moss_block'), (140, 148, 132), 0.6)
    out['pale_moss_carpet'] = out['pale_moss_block'].copy()
    out['pale_hanging_moss'] = strands(151, 7, (150, 158, 142), (110, 118, 104))
    out['pale_hanging_moss_tip'] = strands(152, 5, (150, 158, 142), (110, 118, 104), 8)
    closed = new(); rect(closed, 7, 6, 8, 15, (110, 116, 100)); rect(closed, 6, 3, 9, 6, (120, 124, 112)); rect(closed, 7, 2, 8, 2, (90, 96, 84))
    opened = new(); rect(opened, 7, 8, 8, 15, (110, 116, 100))
    for (x, y) in ((4, 4), (10, 4), (4, 8), (10, 8), (7, 2)):
        rect(opened, x, y, x + 1, y + 1, (236, 140, 40))
    rect(opened, 6, 5, 9, 7, (250, 220, 120)); rect(opened, 7, 6, 8, 6, (40, 30, 20))
    out['closed_eyeblossom'] = closed; out['open_eyeblossom'] = opened
    heart = out['pale_oak_log'].copy(); rect(heart, 5, 5, 10, 10, (80, 60, 50)); rect(heart, 6, 7, 9, 8, (240, 150, 40))
    out['creaking_heart'] = heart
    htop = out['pale_oak_log_top'].copy(); rect(htop, 6, 6, 9, 9, (80, 60, 50))
    out['creaking_heart_top'] = htop
    return out


# ------------------------------------------------------------------ small items
def nugget(base, dark, light):
    im = new()
    # three lumps of different sizes
    for (x, y, w, h) in ((3, 8, 5, 4), (8, 5, 4, 4), (9, 10, 4, 3)):
        rect(im, x, y, x + w - 1, y + h - 1, base)
        rect(im, x, y + h - 1, x + w - 1, y + h - 1, dark)
        rect(im, x + w - 1, y, x + w - 1, y + h - 1, dark)
        rect(im, x, y, x, y, light)
    return im


def small_items(load):
    out = {}
    out['item_charcoal'] = recolor(load('item_coal'), (92, 68, 50), 0.55)
    out['item_gold_nugget'] = nugget((236, 196, 52), (176, 128, 24), (255, 246, 170))
    out['item_iron_nugget'] = nugget((196, 196, 200), (128, 128, 136), (244, 244, 248))
    paper = new()
    rect(paper, 3, 2, 12, 13, (234, 230, 214)); rect(paper, 13, 3, 13, 14, (170, 164, 146)); rect(paper, 4, 14, 13, 14, (170, 164, 146))
    for y in (5, 7, 9, 11):
        rect(paper, 5, y, 10, y, (200, 196, 182))
    out['item_paper'] = paper
    book = new()
    rect(book, 3, 2, 12, 13, (124, 72, 40))
    rect(book, 3, 2, 4, 13, (88, 50, 28))            # spine
    rect(book, 12, 3, 12, 13, (236, 230, 210))        # page edges
    rect(book, 3, 13, 11, 13, (236, 230, 210))
    rect(book, 6, 5, 10, 7, (196, 160, 90))           # label
    rect(book, 6, 5, 10, 5, (220, 190, 120))
    out['item_book'] = book
    return out


def all_textures(load):
    """name -> image, and name -> [frames] for animated ones"""
    static, anim = {}, {}
    for f in (barrel, blast_furnace, smoker, cartography_table, fletching_table, smithing_table, loom, lectern, grindstone, stonecutter,
              composter, pointed_dripstone, azalea, pale_garden, small_items):
        static.update(f(load))
    for f in (bell, lantern, brewing_stand, cauldron, beetroots, dripleaf, spore_blossom):
        static.update(f())
    s, a = campfire(load); static.update(s); anim.update(a)
    s, a = sculk(); static.update(s); anim.update(a)
    return static, anim
