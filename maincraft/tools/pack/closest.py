"""The pack nearest our first pack in look: of several paintings of each picture, the one that looks most
like the first pack's.

    python3 tools/pack/closest.py FIRST_TEX_DIR JAVA_TEXTURES_DIR OUT_DIR PAINTED_DIR [PAINTED_DIR ...]

PAINTED_DIRs are our whole pack painted in the first pack's colours, a drawing each (tools/pack/main.py
java DIR --first FIRST_TEX_DIR --structure tools/pack/structure_first.json --layers [--variant K]).
A further candidate for a face with a made-up layout (a bed, a door, a chiseled block, a workstation)
is the first pack's own layout of parts, each part painted anew (tools/pack/relayout.py); but only
where the first pack's picture is not Minecraft's drawing recoloured (its pattern is checked against
the Java pack's: a picture whose lightness runs as Minecraft's does keeps our own drawings only).

A tile is judged by how its pattern looks (tools/pack/structure.py measure: patch size, grain,
specks, streaks) and by its run of tones from dark to light, both against the first pack's picture.
A block built on another (an ore on its rock, a colour on its family's drawing, a mossy or cracked
block on the plain one, a soil's side on the dirt) takes the same painting as its base, so the
layers stay one. No pixel is left as the first pack's at the same place.
"""
import json
import math
import os
import sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import structure  # noqa: E402
import layers  # noqa: E402
import relayout  # noqa: E402
from core import Source, COLOURS, lum  # noqa: E402

DESIGNED = layers.FACES + ('bed_', 'door', 'trapdoor', 'glazed', 'chiseled', 'stem', 'log_top', 'netherite',
                           'tnt', 'bookshelf', 'crafting', 'cartography', 'fletching')


def base_of(n):
    """the picture a layered one is built on, or None"""
    for col in COLOURS:
        for f in ('wool', 'concrete', 'concrete_powder', 'stained_glass', 'stained_glass_pane_top'):
            if n == '%s_%s' % (col, f) and col != 'white':
                return 'white_' + f
        if n == col + '_terracotta':
            return 'terracotta'
    if n in ('mossy_cobblestone',):
        return 'cobblestone'
    if n == 'mossy_stone_bricks':
        return 'stone_bricks'
    if n.startswith('cracked_'):
        return n[len('cracked_'):]
    if n.startswith('deepslate_') and n.endswith('_ore'):
        return 'deepslate'
    if n.endswith('_ore') and not n.startswith(('nether_', 'deepslate_')):
        return 'stone'
    if n in ('grass_block_side', 'podzol_side', 'mycelium_side', 'dirt_path_side', 'grass_block_snow'):
        return 'dirt'
    if n in ('crimson_nylium_side', 'warped_nylium_side'):
        return 'netherrack'
    return None


def tile(im):
    """the first 16x16 frame's pixels when it is an opaque tile, else None"""
    if im.size[0] != 16 or im.size[1] % 16:
        return None
    px = list(im.convert('RGBA').crop((0, 0, 16, 16)).get_flattened_data())
    return px if sum(1 for p in px if p[3] < 128) <= 12 else None


def score(a, b):
    """how unlike two tiles look: pattern measure distance plus the gap of their tone runs"""
    d = structure.distance(structure.measure(a), structure.measure(b))
    la, lb = sorted(lum(p) for p in a), sorted(lum(p) for p in b)
    return d + sum(abs(x - y) for x, y in zip(la, lb)) / len(la) / 10


def corr(a, b):
    la, lb = [lum(p) for p in a], [lum(p) for p in b]
    ma, mb = sum(la) / len(la), sum(lb) / len(lb)
    sa = math.sqrt(sum((v - ma) ** 2 for v in la)) or 1
    sb = math.sqrt(sum((v - mb) ** 2 for v in lb)) or 1
    return sum((x - ma) * (y - mb) for x, y in zip(la, lb)) / sa / sb


def main():
    first_dir, java_dir, out = sys.argv[1:4]
    painted = sys.argv[4:]
    os.makedirs(out, exist_ok=True)
    import classic
    first = {f[:-4]: Image.open(os.path.join(first_dir, f)).convert('RGBA') for f in os.listdir(first_dir) if f.endswith('.png')}
    first.update({n: im.convert('RGBA') for n, im in classic.originals(first_dir).items()})
    mc = Source('java', java_dir)
    names = sorted(set(f[:-4] for f in os.listdir(painted[0]) if f.endswith('.png')))
    choice, why = {}, {}
    order = sorted(names, key=lambda n: base_of(n) is not None)          # the bases first
    for n in order:
        cands = [Image.open(os.path.join(d, n + '.png')).convert('RGBA') for d in painted if os.path.exists(os.path.join(d, n + '.png'))]
        f = first.get(n)
        b = base_of(n)
        if b in choice and isinstance(choice[b], int) and choice[b] < len(cands):
            choice[n] = choice[b]
            why[n] = 'as ' + b
            continue
        ft = tile(f) if f is not None else None
        if ft is None:
            choice[n] = 0
            continue
        scored = [(score(ft, tile(c)), k) for k, c in enumerate(cands) if c.size == f.size and tile(c)]
        if any(k in n for k in DESIGNED):
            r = mc.image(n)
            rt = tile(r) if r is not None else None
            if rt is None or corr(ft, rt) < 0.45:      # not Minecraft's drawing: its layout may be ours
                rp = relayout.repaint('java', n, f, {}, ft)          # every frame of a strip
                cands.append(rp)
                scored.append((score(ft, tile(rp)), len(cands) - 1))
        best = min(scored) if scored else (0, 0)
        choice[n] = best[1] if best[1] < len(painted) else ('relayout', cands[best[1]])
        why[n] = 'painting %s' % best[1] if best[1] < len(painted) else 'first pack layout, parts painted anew'
    made = 0
    for n in names:
        ch = choice[n]
        if isinstance(ch, tuple):
            im = ch[1]
        else:
            im = Image.open(os.path.join(painted[ch], n + '.png')).convert('RGBA')
        f = first.get(n)
        if f is not None and f.size == im.size:
            im = layers.apart(im, list(f.get_flattened_data()), keep_black=n in ('sun', 'moon_phases'))
        im.save(os.path.join(out, n + '.png'))
        made += 1
    json.dump(why, open(os.path.join(out, '..', os.path.basename(out.rstrip('/')) + '_choices.json'), 'w'), indent=1, sort_keys=True)
    print('made', made, ' relayout', sum(1 for v in choice.values() if isinstance(v, tuple)),
          ' by painting', {k: sum(1 for v in choice.values() if v == k) for k in range(len(painted))})


if __name__ == '__main__':
    main()
