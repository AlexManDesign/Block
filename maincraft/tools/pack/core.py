"""Shared pieces of the pack painter: tileable fields, masks, colour sources and colouring.

A picture is painted as one or more parts. Each part is a field of 'height' values (our own
pattern, higher = lighter) over a mask of pixels, laid by rank onto a colour ramp taken from a
colour source: the pixels of a picture of the style's reference pack (its tones, shadows and
contrast in their amounts), or of our own pack. No pixel is left equal to the reference's at
the same place.
"""
import math
import os
import random
import zlib
from PIL import Image

N = 16
APART = 5           # the least step (most changed channel) a pixel keeps from the reference's at its place
SMOOTH = 4          # half width of the rank window a colour ramp is averaged over


def lum(c):
    return c[0] * 0.3 + c[1] * 0.59 + c[2] * 0.11


# which of our variants is being painted (0: the styles' pack; 1, 2, 3: the variants, each its own
# patterns and shapes)
VARIANT = 0
# while a material is painted to the measured look of its Minecraft texture (tools/pack/structure.py),
# the variants keep their own drawings but not their own scale and contrast
KEEP_SCALE = False


def P(*vals):
    """a pattern parameter by variant: P(v1, v2, v3); the styles' pack takes the first"""
    return vals[max(0, min(len(vals), VARIANT) - 1)]


def rng(*key):
    if VARIANT:
        key = key + ('v%d' % VARIANT,)
    return random.Random(zlib.crc32(':'.join(str(k) for k in key).encode()))


def at(x, y, n=N):
    return (y % n) * n + x % n


def wrapd(a, b, n=N):
    d = abs(a - b) % n
    return min(d, n - d)


# ---- fields ------------------------------------------------------------------------------------

def value_noise(px, py, rnd, w=N, h=N):
    """tileable value noise: a random grid every px by py pixels, smoothly interpolated, 0..1"""
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


def scaled(p, lim):
    """a pattern period in the variant's scale (2: twice as coarse, 3: half as fine), a power of two"""
    k = P(1, 1.5, 0.75)
    v = 1
    while v * 2 <= max(1, p * k) and v * 2 <= lim:
        v *= 2
    return v


def fbm(rnd, octaves, w=N, h=N):
    acc, tot = [0.0] * (w * h), 0.0
    if VARIANT and not KEEP_SCALE:
        octaves = [(scaled(px, w), scaled(py, h), wt) for px, py, wt in octaves]
    for px, py, wt in octaves:
        acc = [a + wt * v for a, v in zip(acc, value_noise(px, py, rnd, w, h))]
        tot += wt
    return [a / tot for a in acc]


def white(rnd, n=N * N):
    return [rnd.random() for _ in range(n)]


def mix(*parts):
    """weighted sum of fields: mix((0.5, f), (0.5, g))"""
    return [sum(w * f[i] for w, f in parts) for i in range(len(parts[0][1]))]


def cells(rnd, n, jitter=0.9, w=N, h=N):
    """tileable Voronoi cells around jittered grid points: cell per pixel, the distances to the two
    nearest points, the number of cells"""
    k = max(2, round(math.sqrt(n) / (1 if KEEP_SCALE else P(1, 1.25, 0.85))))
    pts = [((gx + 0.5 + (rnd.random() - 0.5) * jitter) * w / k, (gy + 0.5 + (rnd.random() - 0.5) * jitter) * h / k)
           for gy in range(k) for gx in range(k)]
    ids, d1, d2 = [], [], []
    for y in range(h):
        for x in range(w):
            ds = sorted((math.hypot(wrapd(x + 0.5, px, w), wrapd(y + 0.5, py, h)), j) for j, (px, py) in enumerate(pts))
            ids.append(ds[0][1]); d1.append(ds[0][0]); d2.append(ds[1][0])
    return ids, d1, d2, len(pts)


def rims(ids, lit, dark, w=N, h=N, wrap=True):
    """light from the top left: + where a region starts above or left of a pixel, - where it ends"""
    out = [0.0] * (w * h)
    if not KEEP_SCALE:
        lit, dark = lit * P(1, 1.5, 0.7), dark * P(1, 1.5, 0.7)

    def get(x, y):
        if wrap:
            return ids[(y % h) * w + x % w]
        return ids[y * w + x] if 0 <= x < w and 0 <= y < h else None
    for i in range(w * h):
        x, y, c = i % w, i // w, ids[i]
        if get(x, y - 1) != c or get(x - 1, y) != c:
            out[i] += lit
        if get(x, y + 1) != c or get(x + 1, y) != c:
            out[i] -= dark
    return out


def blur(f, w=N, h=N, wrap=True):
    out = []
    for i in range(w * h):
        x, y = i % w, i // w
        s = n = 0
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                xx, yy = x + dx, y + dy
                if wrap:
                    xx %= w; yy %= h
                elif not (0 <= xx < w and 0 <= yy < h):
                    continue
                k = (2 - abs(dx)) * (2 - abs(dy))
                s += k * f[yy * w + xx]; n += k
        out.append(s / n)
    return out


# ---- colour sources ----------------------------------------------------------------------------

COLOURS = ['white', 'orange', 'magenta', 'light_blue', 'yellow', 'lime', 'pink', 'gray', 'light_gray', 'cyan',
           'purple', 'blue', 'brown', 'green', 'red', 'black']
WOODS = ['oak', 'spruce', 'birch', 'jungle', 'acacia', 'dark_oak', 'mangrove', 'cherry', 'crimson', 'warped', 'bamboo']
BEDROCK_WOOD = {'oak': 'oak', 'spruce': 'spruce', 'birch': 'birch', 'jungle': 'jungle', 'acacia': 'acacia',
                'dark_oak': 'big_oak'}
BEDROCK_CORAL = {'tube': 'blue', 'brain': 'pink', 'bubble': 'purple', 'fire': 'red', 'horn': 'yellow'}
BEDROCK_RENAME = {
    'poppy': 'flower_rose', 'dandelion': 'flower_dandelion', 'blue_orchid': 'flower_blue_orchid',
    'allium': 'flower_allium', 'azure_bluet': 'flower_houstonia', 'red_tulip': 'flower_tulip_red',
    'orange_tulip': 'flower_tulip_orange', 'white_tulip': 'flower_tulip_white', 'pink_tulip': 'flower_tulip_pink',
    'oxeye_daisy': 'flower_oxeye_daisy', 'cornflower': 'flower_cornflower',
    'lily_of_the_valley': 'flower_lily_of_the_valley', 'wither_rose': 'flower_wither_rose',
    'sunflower_front': 'double_plant_sunflower_front', 'sunflower_bottom': 'double_plant_sunflower_bottom',
    'lilac_top': 'double_plant_syringa_top', 'lilac_bottom': 'double_plant_syringa_bottom',
    'rose_bush_top': 'double_plant_rose_top', 'rose_bush_bottom': 'double_plant_rose_bottom',
    'peony_top': 'double_plant_paeonia_top', 'peony_bottom': 'double_plant_paeonia_bottom',
    'tall_grass_top': 'double_plant_grass_top', 'tall_grass_bottom': 'double_plant_grass_bottom',
    'large_fern_top': 'double_plant_fern_top', 'large_fern_bottom': 'double_plant_fern_bottom',
    'short_grass': 'tallgrass', 'sugar_cane': 'reeds', 'dead_bush': 'deadbush', 'cobweb': 'web',
    'lily_pad': 'waterlily', 'torch': 'torch_on', 'brown_mushroom': 'mushroom_brown', 'red_mushroom': 'mushroom_red',
    'brown_mushroom_block': 'mushroom_block_skin_brown', 'red_mushroom_block': 'mushroom_block_skin_red',
    'mushroom_stem': 'mushroom_block_skin_stem', 'andesite': 'stone_andesite', 'diorite': 'stone_diorite',
    'granite': 'stone_granite', 'polished_andesite': 'stone_andesite_smooth',
    'polished_diorite': 'stone_diorite_smooth', 'polished_granite': 'stone_granite_smooth',
    'stone_bricks': 'stonebrick', 'mossy_stone_bricks': 'stonebrick_mossy', 'cracked_stone_bricks': 'stonebrick_cracked',
    'chiseled_stone_bricks': 'stonebrick_carved', 'mossy_cobblestone': 'cobblestone_mossy', 'bricks': 'brick',
    'nether_bricks': 'nether_brick', 'red_nether_bricks': 'red_nether_brick', 'end_stone_bricks': 'end_bricks',
    'smooth_stone': 'stone_slab_top', 'sandstone': 'sandstone_normal', 'chiseled_sandstone': 'sandstone_carved',
    'cut_sandstone': 'sandstone_smooth', 'red_sandstone': 'red_sandstone_normal',
    'chiseled_red_sandstone': 'red_sandstone_carved', 'cut_red_sandstone': 'red_sandstone_smooth',
    'prismarine': 'prismarine_rough', 'dark_prismarine': 'prismarine_dark', 'chiseled_quartz_block': 'quartz_block_chiseled',
    'quartz_pillar': 'quartz_block_lines', 'quartz_pillar_top': 'quartz_block_lines_top',
    'nether_quartz_ore': 'quartz_ore', 'dirt_path_top': 'grass_path_top', 'dirt_path_side': 'grass_path_side',
    'farmland': 'farmland_dry', 'farmland_moist': 'farmland_wet', 'podzol_top': 'dirt_podzol_top',
    'podzol_side': 'dirt_podzol_side', 'rooted_dirt': 'dirt_with_roots', 'grass_block_top': 'grass_top',
    'grass_block_side': 'grass_side_carried', 'packed_ice': 'ice_packed', 'crimson_nylium': 'crimson_nylium_top',
    'warped_nylium': 'warped_nylium_top', 'wet_sponge': 'sponge_wet', 'carved_pumpkin': 'pumpkin_face_off',
    'jack_o_lantern': 'pumpkin_face_on', 'furnace_front': 'furnace_front_off', 'chiseled_copper_block': 'chiseled_copper',
    'chiseled_exposed_copper': 'exposed_chiseled_copper', 'chiseled_weathered_copper': 'weathered_chiseled_copper',
    'chiseled_oxidized_copper': 'oxidized_chiseled_copper', 'rail': 'rail_normal', 'rail_corner': 'rail_normal_turned',
    'dried_kelp_side': 'dried_kelp_side_a', 'kelp': 'kelp_a', 'bamboo_stalk': 'bamboo_stem',
    'cave_vines': 'cave_vines_body', 'twisting_vines': 'twisting_vines_bottom', 'fire': 'fire_0',
    'water_still': 'water_still_grey', 'oak_trapdoor': 'trapdoor', 'mangrove_log': 'mangrove_log_side',
    'cherry_log': 'cherry_log_side', 'sweet_berry_bush': 'sweet_berry_bush_stage3', 'melon_stem_stage0': 'melon_stem_disconnected',
    'pumpkin_stem_stage0': 'pumpkin_stem_disconnected', 'bookshelf_end': 'planks_oak', 'chest_front_l': 'chest_front',
    'chest_front_r': 'chest_front', 'chest_top_l': 'chest_top', 'chest_top_r': 'chest_top', 'flower_pot_icon': 'flower_pot',
    'magma': 'magma', 'sea_pickle': 'sea_pickle',
}


def bedrock_name(n):
    if n in BEDROCK_RENAME:
        return BEDROCK_RENAME[n]
    c = next((c for c in sorted(COLOURS, key=len, reverse=True) if n.startswith(c + '_')), None)
    if c:
        bc, rest = ('silver' if c == 'light_gray' else c), n[len(c) + 1:]
        m = {'wool': 'wool_colored_', 'concrete': 'concrete_', 'concrete_powder': 'concrete_powder_',
             'terracotta': 'hardened_clay_stained_', 'glazed_terracotta': 'glazed_terracotta_', 'stained_glass': 'glass_'}
        if rest in m:
            return m[rest] + bc
    if n == 'terracotta':
        return 'hardened_clay'
    for w, bw in BEDROCK_WOOD.items():
        if n == w + '_planks':
            return 'planks_' + bw
        if n == w + '_log':
            return 'log_' + bw
        if n == w + '_log_top':
            return 'log_' + bw + '_top'
        if n == w + '_leaves':
            return 'leaves_' + bw + ('' if w in ('oak', 'dark_oak', 'jungle', 'acacia') else '_carried')
        if n == w + '_sapling':
            return 'sapling_' + ('roofed_oak' if w == 'dark_oak' else w)
        for part, bpart in (('door_top', 'upper'), ('door_bottom', 'lower')):
            if n == w + '_' + part:
                return 'door_%s_%s' % ('wood' if w == 'oak' else w, bpart)
    for jc, bc in BEDROCK_CORAL.items():
        if n == jc + '_coral_block':
            return 'coral_' + bc
        if n == 'dead_' + jc + '_coral_block':
            return 'coral_%s_dead' % bc
        if n == jc + '_coral_fan':
            return 'coral_fan_' + bc
        if n == 'dead_' + jc + '_coral_fan':
            return 'coral_fan_%s_dead' % bc
    for crop in ('wheat', 'carrots', 'potatoes'):
        if n.startswith(crop + '_stage'):
            return crop + '_stage_' + n[len(crop) + 6:]
    if n.startswith(('melon_stem_stage', 'pumpkin_stem_stage')):
        return n.split('_')[0] + '_stem_disconnected'
    return n


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


HUD_JAVA = {'hud_heart': 'gui/sprites/hud/heart/full.png', 'hud_food': 'gui/sprites/hud/food_full.png',
            'hud_air': 'gui/sprites/hud/air.png'}

BEDROCK_ENTITY = {
    'entity_creeper': 'entity/creeper/creeper', 'entity_enderman': 'entity/enderman/enderman',
    'entity_skeleton': 'entity/skeleton/skeleton', 'entity_slime': 'entity/slime/slime',
    'entity_spider': 'entity/spider/spider', 'entity_zombie': 'entity/zombie/zombie', 'entity_sheep': 'entity/sheep/sheep',
    'entity_cow': 'entity/cow/cow', 'entity_pig': 'entity/pig/pig', 'entity_chicken': 'entity/chicken',
    'entity_salmon': 'entity/fish/salmon', 'entity_player': 'entity/steve',
}


class Source:
    """colour source of a style: a reference pack (java: textures/ folder; bedrock:
    resource_pack/textures folder), falling back to the next source, then to our own pack"""

    def __init__(self, kind, root, fallback=None):
        self.kind, self.root, self.fallback, self.cache = kind, root, fallback, {}

    def path(self, name):
        cands = []
        if self.kind == 'java':
            if name in HUD_JAVA:
                cands.append(HUD_JAVA[name])
            if name in ENTITY:
                cands.append(ENTITY[name])
            cands.append('block/%s.png' % name)
            if name.startswith('item_'):
                cands.append('item/%s.png' % name[5:])
        elif self.kind == 'bedrock':
            b = bedrock_name(name)
            if name in BEDROCK_ENTITY:
                for ext in ('.png', '.tga'):
                    cands.append(BEDROCK_ENTITY[name] + ext)
            for ext in ('.png', '.tga'):
                cands.append('blocks/' + b + ext)
                if name.startswith('item_'):
                    cands.append('items/' + name[5:] + ext)
        else:
            cands.append(name + '.png')
        for c in cands:
            p = os.path.join(self.root, c)
            if os.path.exists(p):
                return p
        return None

    def image(self, name):
        if name not in self.cache:
            p = self.path(name)
            self.cache[name] = Image.open(p).convert('RGBA') if p else None
        im = self.cache[name]
        if im is None and self.fallback:
            return self.fallback.image(name)
        return im

    def px(self, name, box=None):
        """the picture's first 16x16 frame (squeezed to 16x16 if larger), or a box of it"""
        im = self.image(name)
        if im is None:
            return None
        if im.height > im.width:
            im = im.crop((0, 0, im.width, im.width))
        if im.size != (N, N):
            im = im.resize((N, N), Image.BOX)
        if box:
            im = im.crop(box)
        return list(im.get_flattened_data() if hasattr(im, 'get_flattened_data') else im.getdata())


# ---- colouring ---------------------------------------------------------------------------------

def opaque(px, a=127):
    return [p for p in px if p[3] > a]


def ramp(src_px, size, bands=0):
    """the source colours sorted by lightness, averaged over a small rank window, resampled to size;
    bands > 0 flattens it to that many even tones (the clean style)"""
    srt = sorted(opaque(src_px), key=lum) or [(128, 128, 128, 255)]
    out = []
    for r in range(size):
        c = r * len(srt) // size
        win = srt[max(0, c - SMOOTH):c + SMOOTH + 1]
        out.append(tuple(sum(p[t] for p in win) / len(win) for t in range(3)))
    if bands:
        k = max(1, size // bands)
        flat = []
        for b in range(0, size, k):
            seg = out[b:b + k]
            m = tuple(sum(c[t] for c in seg) / len(seg) for t in range(3))
            flat += [m] * len(seg)
        out = flat[:size]
    return out


def select(px, pred):
    return [p for p in px if p[3] > 127 and pred(p)]


class Canvas:
    """a picture being painted part by part"""

    def __init__(self, style, name, w=N, h=N, ref=None):
        self.style, self.name, self.w, self.h = style, name, w, h
        self.px = [(0, 0, 0, 0)] * (w * h)
        self.ref = ref                   # the reference picture's pixels at the same size, if any
        self.seed = zlib.crc32((name + ('#%d' % VARIANT if VARIANT else '')).encode())

    def part(self, field, src_px, mask=None, alpha=255, grain=0.02, flat=False):
        """lays field (over mask) onto the ramp of src_px by rank; flat: pixels of one field value
        share one colour (a drawing in a few even tones)"""
        idx = [i for i in range(self.w * self.h) if mask is None or mask[i]]
        if not idx or not src_px:
            return self
        clean = self.style == 'clean'
        if clean and not flat:
            if not KEEP_SCALE:         # a material keeps its measured look; only its tones are fewer
                field = blur(field, self.w, self.h, wrap=False)
            grain = 0
        order = sorted(idx, key=lambda i: field[i])
        rp = ramp(src_px, len(order), 4 if clean and not flat else 0)
        k = 1.0 if KEEP_SCALE else P(1.0, 1.25, 0.85)
        if k != 1.0:                       # the variant's contrast, round the mean tone
            mean = [sum(c[t] for c in rp) / len(rp) for t in range(3)]
            rp = [tuple(max(0, min(255, mean[t] + (c[t] - mean[t]) * k)) for t in range(3)) for c in rp]
        if flat:
            groups = {}
            for r, i in enumerate(order):
                groups.setdefault(field[i], []).append(r)
            for rs in groups.values():
                m = tuple(sum(rp[r][t] for r in rs) / len(rs) for t in range(3))
                for r in rs:
                    rp[r] = m
        for r, i in enumerate(order):
            g = 1 + grain * (((zlib.crc32(b'%d:%d' % (self.seed, i)) >> 8) & 0xff) / 127.5 - 1)
            self.px[i] = tuple(max(0, min(255, round(v * g))) for v in rp[r]) + (alpha,)
        return self

    def fill(self, colour, mask):
        for i in range(self.w * self.h):
            if mask[i]:
                self.px[i] = tuple(colour[:3]) + ((colour[3],) if len(colour) > 3 else (255,))
        return self

    def image(self):
        px = list(self.px)
        if self.ref and len(self.ref) == len(px):
            for i, (p, r) in enumerate(zip(px, self.ref)):
                if p[3] == 0 or r[3] == 0 or max(abs(p[t] - r[t]) for t in range(3)) >= APART:
                    continue
                s = -1 if lum(r) > 128 else 1
                for _ in range(2):
                    q = [max(0, min(255, r[t] + s * APART)) for t in range(3)]
                    if max(abs(q[t] - r[t]) for t in range(3)) >= APART:
                        px[i] = tuple(q) + (p[3],)
                        break
                    s = -s
        im = Image.new('RGBA', (self.w, self.h))
        im.putdata(px)
        return im
