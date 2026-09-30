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
SKIP_PREFIX = ('entity_', 'ui_')
SKIP = {'sun', 'moon_phases', 'bobber'}
FIRST_FRAME_ONLY = {'short_grass', 'seagrass', 'tall_seagrass_top', 'tall_seagrass_bottom', 'kelp', 'kelp_plant'}
ANIM_TICKS = {'water_still': 2, 'seagrass': 3, 'kelp': 3, 'sea_lantern': 5, 'magma': 8,
              'prismarine': 60, 'crimson_stem': 10, 'warped_stem': 10}


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
        rgb = TINT.get(name)
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
