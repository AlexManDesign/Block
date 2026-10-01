#!/usr/bin/env python3
"""Build maincraft.html: packs textures into one embedded atlas and inlines all sources.

usage: python3 build.py [out.html]
requires: Pillow
"""
import base64, io, json, os, random, re, sys
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.join(ROOT, 'tex')
SRC = os.path.join(ROOT, 'src')
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, '..', 'maincraft.html')

GRASS = (145, 189, 89)
FOLIAGE = (119, 171, 47)
TINT = {
    'grass_block_top': GRASS, 'short_grass': GRASS, 'fern': GRASS, 'tall_grass_top': GRASS,
    'tall_grass_bottom': GRASS, 'large_fern_top': GRASS, 'large_fern_bottom': GRASS,
    'oak_leaves': FOLIAGE, 'vine': FOLIAGE, 'dark_oak_leaves': (64, 130, 42),
    'jungle_leaves': (72, 180, 56), 'acacia_leaves': (174, 164, 60), 'lily_pad': (32, 128, 48),
    'water_still': (63, 118, 228), 'melon_stem_stage0': None,
}
# Textures the terrain tints per biome (grass / foliage / water colour, like Minecraft's colormaps).
# The tinted copy above stays for items, particles and the hand; terrain uses the untinted '<name>_bt'.
BIOME_TINTED = {'grass_block_top', 'short_grass', 'fern', 'tall_grass_top', 'tall_grass_bottom',
                'large_fern_top', 'large_fern_bottom', 'oak_leaves', 'vine', 'dark_oak_leaves',
                'jungle_leaves', 'acacia_leaves', 'water_still'}   # + mangrove_leaves (synthesised)
SKIP_PREFIX = ('entity_', 'ui_')
SKIP = {'sun', 'moon_phases', 'bobber'}
FIRST_FRAME_ONLY = {'short_grass', 'seagrass', 'tall_seagrass_top', 'tall_seagrass_bottom', 'kelp', 'kelp_plant'}
ANIM_TICKS = {'water_still': 2, 'seagrass': 3, 'kelp': 3, 'sea_lantern': 5, 'magma': 8,
              'prismarine': 60, 'crimson_stem': 10, 'warped_stem': 10,
              'lantern': 8, 'campfire_fire': 2, 'campfire_log_lit': 20, 'stonecutter_saw': 1}


def tint(img, rgb):
    if not rgb:
        return img
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            px[x, y] = (r * rgb[0] // 255, g * rgb[1] // 255, b * rgb[2] // 255, a)
    return img


def load(name):
    return Image.open(os.path.join(TEX, name + '.png')).convert('RGBA')


def framed_planks(planks, holes_from=None, bottom=False):
    """Synthesise a door texture from planks: darker frame and vertical boards."""
    img = planks.copy()
    px = img.load()
    for y in range(16):
        for x in range(16):
            r, g, b, a = px[x, y]
            k = 1.0
            if x in (0, 15) or (bottom and y == 15) or (not bottom and y == 0):
                k = 0.62
            elif x in (7, 8):
                k = 0.85
            elif (not bottom and y in (7, 8)) or (bottom and y in (3, 4)):
                k = 0.78
            px[x, y] = (int(r * k), int(g * k), int(b * k), 255)
    if holes_from is not None:
        hp = holes_from.load()
        for y in range(16):
            for x in range(16):
                if hp[x, y][3] < 128:
                    px[x, y] = (0, 0, 0, 0)
    return img


# spawn egg colours (base, spots) as in Minecraft
SPAWN_EGGS = [('pig', (240, 160, 156), (219, 99, 94)), ('cow', (68, 54, 37), (161, 161, 161)),
              ('sheep', (231, 231, 231), (255, 181, 181)), ('chicken', (161, 161, 161), (255, 0, 0)),
              ('zombie', (0, 175, 175), (121, 153, 80)), ('skeleton', (193, 193, 193), (73, 73, 73)),
              ('creeper', (13, 168, 12), (0, 0, 0)), ('spider', (52, 45, 39), (163, 0, 0)),
              ('enderman', (22, 22, 22), (0, 0, 0)), ('salmon', (160, 20, 20), (14, 143, 104)),
              ('slime', (81, 160, 62), (126, 190, 110)), ('shark', (74, 86, 97), (200, 200, 200))]


def spawn_egg(egg, c1, c2):
    """Grey spawn egg -> base colour with spots of the second colour (shading kept)."""
    out = egg.copy()
    px = out.load()
    spots = {(5, 4), (6, 4), (9, 6), (10, 6), (10, 7), (4, 8), (5, 8), (7, 10), (8, 10), (8, 11), (11, 10), (6, 6)}
    for y in range(16):
        for x in range(16):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            k = r / 255.0
            c = c2 if (x, y) in spots else c1
            px[x, y] = (int(c[0] * k), int(c[1] * k), int(c[2] * k), a)
    return out


def grass_side_split(side, dirt):
    """Minecraft draws the grass block side as dirt plus a biome-tinted overlay. The pack has the
    pre-coloured composite, so split it: green-dominant pixels are grass. Returns the base (dirt
    under the grass fringe) and a greyscale overlay whose alpha marks the grass pixels."""
    base, over = side.copy().convert('RGBA'), Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    sp, dp, bp, op = side.convert('RGBA').load(), dirt.convert('RGBA').load(), base.load(), over.load()
    lum = lambda c: 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]
    ref = lum(GRASS)
    for y in range(16):
        for x in range(16):
            s = sp[x, y]
            if not (s[1] > s[0] + 8 and s[1] > s[2]):
                continue
            g = min(255, int(round(lum(s) / ref * 255)))
            op[x, y] = (g, g, g, 255)
            bp[x, y] = dp[x, y]
    return base, over


def avg_rgb(img):
    im = img.convert('RGBA')
    ld = im.load()
    px = [ld[x, y] for y in range(im.height) for x in range(im.width) if ld[x, y][3] > 0]
    return tuple(sum(p[i] for p in px) / len(px) for i in range(3))


def recolor(img, src_avg, dst_avg, mask=None):
    """Moves pixels from one palette to another keeping their relative brightness."""
    out = img.copy().convert('RGBA')
    px = out.load()
    sl = 0.3 * src_avg[0] + 0.59 * src_avg[1] + 0.11 * src_avg[2]
    for y in range(out.height):
        for x in range(out.width):
            if mask and not mask(x, y):
                continue
            r, g, b, a = px[x, y]
            k = (0.3 * r + 0.59 * g + 0.11 * b) / sl
            px[x, y] = tuple(min(255, int(round(c * k))) for c in dst_avg) + (a,)
    return out


MANGROVE_BARK = (84, 66, 56)


def mangrove_log(oak_log, oak_top, planks, bark=None):
    """Mangrove (or cherry) log from the oak log: the bark colour, the planks' wood inside the rings."""
    MANGROVE_BARK_ = bark or MANGROVE_BARK
    side = recolor(oak_log, avg_rgb(oak_log), MANGROVE_BARK_)
    ring = lambda x, y: x in (0, 15) or y in (0, 15)
    top = recolor(oak_top, avg_rgb(oak_log), MANGROVE_BARK_, ring)
    inner = [(x, y) for y in range(1, 15) for x in range(1, 15)]
    ia = tuple(sum(oak_top.getpixel(p)[i] for p in inner) / len(inner) for i in range(3))
    top = recolor(top, ia, avg_rgb(planks), lambda x, y: not ring(x, y))
    return side, top


def root_strands(seed, count, light, dark):
    """Tileable tangle of thin roots on a transparent tile (Minecraft's mangrove roots look)."""
    rnd = random.Random(seed)
    im = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    px = im.load()
    for s in range(count):
        vertical = s % 2 == 0
        a = rnd.randrange(16)
        drift = rnd.choice((-1, 1))
        for t in range(16):
            if rnd.random() < 0.35:
                a = (a + drift) % 16
            if rnd.random() < 0.12:
                drift = -drift
            x, y = (a, t) if vertical else (t, a)
            px[x, y] = light + (255,)
            x2, y2 = ((a + 1) % 16, t) if vertical else (t, (a + 1) % 16)
            px[x2, y2] = dark + (255,)
    return im


def mangrove_propagule():
    """Hanging propagule: a small leaf tuft with a long green pod turning brown at the tip."""
    im = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    px = im.load()
    leaf, leaf_d = (104, 150, 46), (70, 110, 34)
    for x, y in [(6, 0), (7, 0), (8, 0), (9, 0), (5, 1), (6, 1), (7, 1), (8, 1), (9, 1), (10, 1), (6, 2), (9, 2), (7, 2), (8, 2)]:
        px[x, y] = (leaf if (x + y) % 3 else leaf_d) + (255,)
    for y in range(3, 15):
        t = (y - 3) / 11
        c = tuple(int(a + (b - a) * t) for a, b in zip((112, 146, 52), (92, 70, 40)))
        px[7, y] = c + (255,)
        px[8, y] = tuple(int(v * 0.78) for v in c) + (255,)
    px[7, 15] = (70, 52, 30, 255)
    return im


def amethyst_shards(shards):
    """Amethyst bud / cluster: pointed crystal shards growing from the bottom edge (x centre,
    height, half width), light along the middle, dark edges, as the shards of Minecraft's."""
    im = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    px = im.load()
    light, mid, dark, edge = (244, 206, 252), (198, 146, 234), (148, 98, 204), (96, 62, 150)
    for cx, h, hw in shards:
        for k in range(h):
            y = 15 - k
            w = hw * min(1.0, (h - k) / 2.5)
            for x in range(16):
                d = abs(x + 0.5 - cx)
                if d > w + 0.35:
                    continue
                r = d / max(w, 0.5)
                c = light if r < 0.3 and k > 0 else mid if r < 0.65 else dark if r < 0.95 else edge
                px[x, y] = c + (255,)
    return im


def spawner_cage():
    """Monster spawner: a dark iron cage (frame and a grid of bars) with see-through holes."""
    im = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    px = im.load()
    dark, mid, light = (22, 27, 33), (44, 54, 64), (82, 98, 112)
    bars = {0, 1, 5, 10, 14, 15}
    for y in range(16):
        for x in range(16):
            if x in bars or y in bars:
                c = mid
                if x in (0, 15) or y in (0, 15):
                    c = dark
                elif (x in (1, 5, 10) and y not in bars) or (y in (1, 5, 10) and x not in bars):
                    c = light if (x + y) % 3 == 0 else mid
                elif x in bars and y in bars:
                    c = light
                px[x, y] = c + (255,)
    return im


LEAF_TEXTURES = {'oak_leaves', 'birch_leaves', 'spruce_leaves', 'jungle_leaves', 'acacia_leaves', 'dark_oak_leaves',
                 'mangrove_leaves', 'azalea_leaves', 'flowering_azalea_leaves', 'cherry_leaves'}


def opaque_leaves(img):
    """Minecraft's fast leaves: drawn solid, the see-through pixels filled with a dark shade of the leaf."""
    src = img.convert('RGBA')
    px = src.load()
    solid = [px[x, y] for y in range(src.height) for x in range(src.width) if px[x, y][3] >= 128]
    avg = tuple(sum(p[i] for p in solid) // max(1, len(solid)) for i in range(3))
    dark = tuple(int(c * 0.4) for c in avg) + (255,)
    out = Image.new('RGBA', src.size)
    op = out.load()
    for y in range(src.height):
        for x in range(src.width):
            p = px[x, y]
            op[x, y] = (p[0], p[1], p[2], 255) if p[3] >= 128 else dark
    return out


def snowy_side(dirt, snow):
    """Side of a snow-covered grass block: dirt with a ragged snow band on top (as in Minecraft)."""
    out = dirt.copy().convert('RGBA')
    sp, op = snow.convert('RGBA').load(), out.load()
    depth = [3, 4, 3, 3, 4, 3, 2, 3, 4, 4, 3, 3, 2, 3, 4, 3]
    for x in range(16):
        for y in range(depth[x]):
            op[x, y] = sp[x, y]
    return out


def orb(dark, mid, light, rad):
    """Small shaded ball item (ender pearl, slimeball)."""
    im = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    px = im.load()
    cx = cy = 7.5
    for y in range(16):
        for x in range(16):
            dx, dy = x - cx, y - cy
            d = (dx * dx + dy * dy) ** 0.5
            if d > rad + 0.3:
                continue
            l = max(0.0, 1 - (((x - (cx - 2)) ** 2 + (y - (cy - 2)) ** 2) ** 0.5) / (rad * 1.6))
            c = light if l > 0.72 else mid if l > 0.3 else dark
            if d > rad - 0.7:
                c = dark
            px[x, y] = c + (255,)
    return im


def crack_stage(stage):
    rnd = random.Random(1234)
    img = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    segs = []
    # build a deterministic crack tree; stage n draws the first part of it
    pts = [(8, 8)]
    for i in range(60):
        x0, y0 = pts[rnd.randrange(len(pts))]
        ang = rnd.random() * 6.283
        ln = 1 + rnd.random() * 3
        import math
        x1 = max(0, min(15, round(x0 + math.cos(ang) * ln)))
        y1 = max(0, min(15, round(y0 + math.sin(ang) * ln)))
        segs.append((x0, y0, x1, y1))
        pts.append((x1, y1))
    n = int(len(segs) * (stage + 1) / 10)
    for s in segs[:n]:
        d.line(s, fill=(20, 20, 20, 200), width=1)
    return img


def no_cones(img):
    """The pack's spruce leaves carry orange cones that Minecraft's needles do not have: each
    reddish pixel takes the mean colour of its needle neighbours."""
    out = img.copy().convert('RGBA')
    px = out.load()
    red = lambda p: p[3] >= 128 and p[0] > p[1]
    for _ in range(3):
        for y in range(16):
            for x in range(16):
                if not red(px[x, y]):
                    continue
                nb = [px[(x + dx) % 16, (y + dy) % 16] for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1))]
                nb = [q for q in nb if q[3] >= 128 and not red(q)]
                if nb:
                    px[x, y] = tuple(sum(q[i] for q in nb) // len(nb) for i in range(3)) + (255,)
    return out


def collect():
    tiles = []   # list of (name, [Image frames], ticks)
    for fn in sorted(os.listdir(TEX)):
        if not fn.endswith('.png'):
            continue
        name = fn[:-4]
        if name.startswith(SKIP_PREFIX) or name in SKIP:
            continue
        try:
            img = load(name)
        except Exception as e:
            print('skip', fn, e)
            continue
        if img.width != 16:
            img = img.resize((16, img.height * 16 // img.width), Image.NEAREST)
        nfr = max(1, img.height // 16)
        frames = [img.crop((0, i * 16, 16, i * 16 + 16)) for i in range(nfr)]
        if name in FIRST_FRAME_ONLY:
            frames = frames[:1]
        if name == 'water_still':
            frames = frames[::3]
        if name == 'spruce_leaves':
            frames = [no_cones(f) for f in frames]
        rgb = TINT.get(name)
        if name in BIOME_TINTED:
            tiles.append((name + '_bt', [f.copy() for f in frames], ANIM_TICKS.get(name, 4) if len(frames) > 1 else 0))
        frames = [tint(f, rgb) for f in frames]
        tiles.append((name, frames, ANIM_TICKS.get(name, 4) if len(frames) > 1 else 0))
    names = {t[0] for t in tiles}
    # synthesised textures missing from the pack
    oak = load('oak_planks'); spruce = load('spruce_planks')
    extra = {}
    if 'oak_door_bottom' not in names:
        extra['oak_door_bottom'] = framed_planks(oak, None, True)
    if 'spruce_door_top' not in names:
        extra['spruce_door_top'] = framed_planks(spruce, load('oak_door_top'), False)
    if 'spruce_door_bottom' not in names:
        extra['spruce_door_bottom'] = framed_planks(spruce, None, True)
    extra['grass_block_side_bt'], extra['grass_block_side_overlay'] = grass_side_split(load('grass_block_side'), load('dirt'))
    # mangrove: the pack has planks, door and trapdoor only
    mplanks = load('mangrove_planks')
    extra['mangrove_log'], extra['mangrove_log_top'] = mangrove_log(load('oak_log'), load('oak_log_top'), mplanks)
    ml = load('oak_leaves').transpose(Image.FLIP_LEFT_RIGHT)
    extra['mangrove_leaves_bt'] = ml.copy()
    extra['mangrove_leaves'] = tint(ml, (146, 198, 72))
    extra['mangrove_roots'] = root_strands(7, 6, (106, 82, 64), (62, 46, 38))
    extra['mangrove_roots_top'] = root_strands(11, 6, (106, 82, 64), (62, 46, 38))
    for side, seed in (('muddy_mangrove_roots_side', 13), ('muddy_mangrove_roots_top', 17)):
        m = load('mud').copy()
        m.alpha_composite(root_strands(seed, 5, (112, 88, 68), (66, 50, 40)))
        extra[side] = m
    extra['mangrove_propagule_hanging'] = mangrove_propagule()
    # cherry: dark purple-brown bark, pink planks inside, pink leaves (not biome tinted)
    extra['cherry_log'], extra['cherry_log_top'] = mangrove_log(load('oak_log'), load('oak_log_top'), load('cherry_planks'), (58, 36, 46))
    extra['cherry_leaves'] = tint(load('azalea_leaves').convert('L').convert('RGBA'), (238, 168, 204))
    cl, al = extra['cherry_leaves'].load(), load('azalea_leaves').convert('RGBA').load()
    for yy in range(16):
        for xx in range(16):
            r, g, b, _ = cl[xx, yy]
            cl[xx, yy] = (min(255, r * 3 // 2), min(255, g * 3 // 2), min(255, b * 3 // 2), al[xx, yy][3])
    # amethyst buds and cluster (the pack has the block only)
    extra['small_amethyst_bud'] = amethyst_shards([(8, 4, 1.6), (5.5, 3, 1.0), (10.5, 2, 0.9)])
    extra['medium_amethyst_bud'] = amethyst_shards([(8, 7, 1.8), (5, 4, 1.2), (11, 5, 1.2)])
    extra['large_amethyst_bud'] = amethyst_shards([(8, 10, 2.0), (4.5, 6, 1.3), (11.5, 7, 1.3)])
    extra['amethyst_cluster'] = amethyst_shards([(8, 13, 2.2), (4, 9, 1.5), (12, 10, 1.5), (6, 6, 1.1), (10.5, 6, 1.1)])
    extra['spawner'] = spawner_cage()
    # redstone torch: the torch with a red, dimmer head (the pack has the torch only)
    rt = load('torch').convert('RGBA')
    rp = rt.load()
    for yy in range(16):
        for xx in range(16):
            r, g, b, a = rp[xx, yy]
            if a and r > 150 and g > 100 and b < 120:   # flame pixels
                l = (r + g) / 510
                rp[xx, yy] = (int(150 + 105 * l), int(10 + 30 * l), int(8 + 20 * l), a)
    extra['redstone_torch'] = rt
    if 'grass_block_snow' not in names:
        extra['grass_block_snow'] = snowy_side(load('dirt'), load('snow'))
    extra['item_ender_pearl'] = orb((12, 59, 55), (46, 143, 134), (160, 230, 220), 6)
    extra['item_slime_ball'] = orb((74, 140, 58), (127, 196, 106), (210, 245, 200), 5)
    egg = load('item_spawn_egg')
    for mob, c1, c2 in SPAWN_EGGS:
        extra['item_spawn_egg_' + mob] = spawn_egg(egg, c1, c2)
    for k, v in extra.items():
        tiles.append((k, [v], 0))
    for s in range(10):
        tiles.append(('destroy_stage_%d' % s, [crack_stage(s)], 0))
    # opaque variants of the leaves (and of their untinted copies) for the fast leaves setting
    for name, frames, _ in list(tiles):
        base = name[:-3] if name.endswith('_bt') else name
        if base in LEAF_TEXTURES:
            tiles.append((base + '_opaque' + ('_bt' if name.endswith('_bt') else ''), [opaque_leaves(frames[0])], 0))
    # white tile used for untextured geometry (outline, clouds, particles)
    tiles.append(('white', [Image.new('RGBA', (16, 16), (255, 255, 255, 255))], 0))
    return tiles


def pack(tiles):
    count = sum(len(f) for _, f, _ in tiles)
    cols = 32
    rows = (count + cols - 1) // cols
    atlas = Image.new('RGBA', (cols * 16, rows * 16), (0, 0, 0, 0))
    layers = {}
    anim = {}
    i = 0
    for name, frames, ticks in tiles:
        layers[name] = i
        if len(frames) > 1:
            anim[name] = [i, len(frames), ticks]
        for f in frames:
            atlas.paste(f, ((i % cols) * 16, (i // cols) * 16))
            i += 1
    return atlas, layers, anim, count


def b64png(img):
    bio = io.BytesIO()
    img.save(bio, 'PNG', optimize=True)
    return base64.b64encode(bio.getvalue()).decode()


def main():
    tiles = collect()
    atlas, layers, anim, count = pack(tiles)
    assets = {
        'atlas': b64png(atlas), 'cols': 32, 'count': count, 'layers': layers, 'anim': anim,
        'sun': b64png(load('sun')), 'moon': b64png(load('moon_phases')),
        'entities': {},
    }
    for fn in sorted(os.listdir(TEX)):
        if fn.startswith('entity_') and fn.endswith('.png'):
            assets['entities'][fn[7:-4]] = b64png(load(fn[:-4]))
    # cave spider: the spider skin in the cave spider's dark blue-green, same red eyes
    cs = load('entity_spider').convert('RGBA')
    cp = cs.load()
    for yy in range(cs.height):
        for xx in range(cs.width):
            r, g, b, a = cp[xx, yy]
            l = (r * 0.3 + g * 0.59 + b * 0.11) / 255
            cp[xx, yy] = (int(18 + 60 * l), int(40 + 120 * l), int(48 + 128 * l), a)
    assets['entities']['cave_spider'] = b64png(cs)
    assets['entities']['cave_spider_eyes'] = b64png(load('entity_spider_eyes'))
    html = open(os.path.join(SRC, 'index.html'), encoding='utf-8').read()

    def inc(m):
        parts = []
        for f in m.group(1).split(','):
            f = f.strip()
            code = open(os.path.join(SRC, f), encoding='utf-8').read()
            if '</script' in code:
                raise SystemExit('forbidden </script in ' + f)
            parts.append('// ---- ' + f + ' ----\n' + code)
        return '\n'.join(parts)
    html = re.sub(r'/\*@INCLUDE ([^*]+)\*/', inc, html)
    html = html.replace('"@ASSETS@"', json.dumps(assets, separators=(',', ':')))
    open(OUT, 'w', encoding='utf-8').write(html)
    print('wrote', OUT, len(html) // 1024, 'KB,', count, 'texture layers')


if __name__ == '__main__':
    main()
