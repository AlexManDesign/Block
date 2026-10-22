"""The pack nearest our first pack in look: of several paintings of each picture, the one that looks most
like the first pack's.

    python3 tools/pack/closest.py FIRST_TEX_DIR JAVA_TEXTURES_DIR OUT_DIR PAINTED_DIR [PAINTED_DIR ...]
                                  [--plain PLAIN_DIR]

PAINTED_DIRs are our whole pack painted in the first pack's colours, a drawing each (tools/pack/main.py
java DIR --first FIRST_TEX_DIR --structure tools/pack/structure_first.json --layers [--variant K]).
A further candidate for a face with a made-up layout (a bed, a door, a chiseled block, a workstation)
is the first pack's own layout of parts, each part painted anew (tools/pack/relayout.py); but only
where the first pack's picture is not Minecraft's drawing recoloured, and not for a door, a trapdoor
or a block of tools/pack/designs.py, which keep our own plan of them (its pattern is checked against
the Java pack's: a picture whose lightness runs as Minecraft's does keeps our own drawings only).

A drawn thing (an item, a flower...) whose first-pack shape is Minecraft's own (tested the same way)
is taken from PLAIN_DIR, our pack painted with our own masks for every drawn thing (main.py ...
--no-shapes), so no Minecraft outline comes in by way of our first pack.

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
import designs  # noqa: E402
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


def near(a, b):
    """how far apart two pictures are pixel by pixel: the mean colour gap where both are drawn, the
    full gap where only one is (over the pixels either draws), scaled to about 0..10"""
    pa, pb = list(a.get_flattened_data()), list(b.get_flattened_data())
    t = s = 0
    for p, q in zip(pa, pb):
        if p[3] < 128 and q[3] < 128:
            continue
        t += 1
        s += 255 if (p[3] < 128) != (q[3] < 128) else max(abs(p[c] - q[c]) for c in range(3))
    return s / max(1, t) / 25


CODED = set()     # the first pack's pictures drawn by our own code (tools/classic.py originals)


def from_minecraft(n, f, mc):
    """is our first pack's picture Minecraft's drawing recoloured? (its lightness runs as the Java
    pack's does at the same places, and it is drawn where that is)"""
    if n in CODED:                                 # drawn by our own code, not taken from anywhere
        return False
    r = mc.image(n)
    if r is None or r.size[0] != f.size[0]:
        return False
    a = list(f.convert('RGBA').crop((0, 0, f.size[0], f.size[0])).get_flattened_data())
    b = list(r.convert('RGBA').crop((0, 0, f.size[0], f.size[0])).get_flattened_data())
    both = [(p, q) for p, q in zip(a, b) if p[3] > 127 and q[3] > 127]
    either = sum(1 for p, q in zip(a, b) if p[3] > 127 or q[3] > 127)
    if len(both) < 8:
        return False
    if len(both) / max(1, either) < 0.7:
        return False
    return corr([p for p, _ in both], [q for _, q in both]) >= 0.45


def main():
    args = sys.argv[1:]
    plain = None
    if '--plain' in args:              # a painting with our own masks for every drawn thing
        k = args.index('--plain'); plain = args[k + 1]; del args[k:k + 2]
    first_dir, java_dir, out = args[:3]
    painted = args[3:]
    os.makedirs(out, exist_ok=True)
    import classic
    first = {f[:-4]: Image.open(os.path.join(first_dir, f)).convert('RGBA') for f in os.listdir(first_dir) if f.endswith('.png')}
    coded = classic.originals(first_dir)
    CODED.update(coded)
    first.update({n: im.convert('RGBA') for n, im in coded.items()})
    mc = Source('java', java_dir)
    names = sorted(set(f[:-4] for f in os.listdir(painted[0]) if f.endswith('.png')))
    choice, why = {}, {}
    order = sorted(names, key=lambda n: base_of(n) is not None)          # the bases first
    for n in order:
        cands = [Image.open(os.path.join(d, n + '.png')).convert('RGBA') for d in painted if os.path.exists(os.path.join(d, n + '.png'))]
        f = first.get(n)
        b = base_of(n)
        if f is None:
            choice[n] = choice[b] if isinstance(choice.get(b), int) else 0
            continue
        ft = tile(f)
        if plain and ft is None and from_minecraft(n, f, mc) and os.path.exists(os.path.join(plain, n + '.png')):
            # a drawn thing whose first-pack shape is Minecraft's: our own mask, not that shape
            choice[n] = ('plain', Image.open(os.path.join(plain, n + '.png')).convert('RGBA'))
            why[n] = 'our own mask (the first pack has Minecraft\'s shape)'
            continue
        scored = []
        for k, c in enumerate(cands):
            if c.size == f.size:
                s = near(f, c) + (score(ft, tile(c)) if ft and tile(c) else 0)
                if isinstance(choice.get(b), int) and k == choice[b]:
                    s -= 1.0                     # built on its base's painting: the layers stay one
                scored.append((s, k))
        own_plan = n in designs.DESIGN or n.endswith(('_door_top', '_door_bottom', '_trapdoor'))
        if f.size[0] == 16 and f.size[1] % 16 == 0 and not own_plan and not from_minecraft(n, f, mc):
            # not Minecraft's drawing recoloured: our first pack's own layout, its parts painted anew
            rp = relayout.repaint('java', n, f, {}, list(f.crop((0, 0, 16, 16)).get_flattened_data()))
            cands.append(rp)
            scored.append((near(f, rp) + (score(ft, tile(rp)) if ft and tile(rp) else 0), len(cands) - 1))
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
        r = mc.image(n)                 # nor as Minecraft's own, where a pixel happens to fall on it
        refs = [list(x.convert('RGBA').get_flattened_data()) for x in (f, r) if x is not None and x.size == im.size]
        im = layers.apart_all(im, refs, keep_black=n in ('sun', 'moon_phases'))
        im.save(os.path.join(out, n + '.png'))
        made += 1
    json.dump(why, open(os.path.join(out, '..', os.path.basename(out.rstrip('/')) + '_choices.json'), 'w'), indent=1, sort_keys=True)
    print('made', made, ' relayout', sum(1 for v in choice.values() if isinstance(v, tuple) and v[0] == 'relayout'),
          ' own masks', sum(1 for v in choice.values() if isinstance(v, tuple) and v[0] == 'plain'),
          ' by painting', {k: sum(1 for v in choice.values() if v == k) for k in range(len(painted))})


if __name__ == '__main__':
    main()
