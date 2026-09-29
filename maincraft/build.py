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
