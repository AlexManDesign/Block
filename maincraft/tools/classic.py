"""The classic pack: our first pack drawn anew in its own look (tools/redraw.py), everything the game
shows included.

    python3 tools/classic.py FIRST_TEX_DIR OUT_DIR

FIRST_TEX_DIR is the tex/ folder of our first pack (git archive 8bcf596 maincraft/tex). Every picture
of it is redrawn by tools/redraw.py: the same layout, palette and silhouette, shaded and grained
anew, no pixel left as it was. So are the drawings of tools/art.py and the pictures build.py draws
in code (amethyst, spawner, roots, propagule, ender pearl, slimeball, cracks), and the HUD icons
the game drew in code (hearts, food, air), so that the whole game looks like the first pack.
"""
import os
import sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import redraw  # noqa: E402

# the HUD icons as the game drew them (src/menus.js pixelIcon): mask, colour, highlight
HUD = {
    'hud_heart': (['..##.##..', '.#######.', '#########', '#########', '.#######.', '..#####..', '...###...', '....#....'],
                  (0xee, 0x22, 0x22), (0xff, 0xb0, 0xb0)),
    'hud_heart_empty': (['..##.##..', '.#######.', '#########', '#########', '.#######.', '..#####..', '...###...',
                         '....#....'], (0x3a, 0x10, 0x10), (0x3a, 0x10, 0x10)),
    'hud_food': (['.....##..', '....####.', '...######', '..######.', '.#####...', '.###.....', '#.#......', '##.......'],
                 (0xc8, 0x74, 0x2c), (0xf0, 0xc0, 0x80)),
    'hud_food_empty': (['.....##..', '....####.', '...######', '..######.', '.#####...', '.###.....', '#.#......',
                        '##.......'], (0x3a, 0x24, 0x10), (0x3a, 0x24, 0x10)),
    'hud_air': (['..####..', '.#....#.', '#..#...#', '#.#....#', '#......#', '#......#', '.#....#.', '..####..'],
                (0x66, 0xaa, 0xff), (0xff, 0xff, 0xff)),
}


def hud_icon(rows, col, hi):
    im = Image.new('RGBA', (9, 9))
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch == '#':
                im.putpixel((x, y), (hi if (y < 3 and x < 4) else col) + (255,))
    return im


def main():
    src, out = sys.argv[1:3]
    os.makedirs(out, exist_ok=True)
    load = lambda n: Image.open(os.path.join(src, n + '.png')).convert('RGBA')   # noqa: E731
    made = 0
    for fn in sorted(os.listdir(src)):
        if not fn.endswith('.png'):
            continue
        n = fn[:-4]
        im = Image.open(os.path.join(src, fn))
        new = redraw.redraw_glow(im, n, redraw.GLOW[n])[0] if n in redraw.GLOW else redraw.redraw(im, n)[0]
        new.save(os.path.join(out, fn))
        made += 1
    # the drawings of tools/art.py, as the first pack's game had them
    import art
    drawn, drawn_anim = art.all_textures(load)
    for k, frames in drawn_anim.items():
        s = Image.new('RGBA', (16, 16 * len(frames)))
        for j, f in enumerate(frames):
            s.paste(f, (0, 16 * j))
        drawn[k] = s
    # the pictures build.py draws in code
    sys.argv = [sys.argv[0]]
    sys.path.insert(0, os.path.dirname(HERE))
    import build
    coded = {
        'small_amethyst_bud': build.amethyst_shards([(8, 4, 1.6), (5.5, 3, 1.0), (10.5, 2, 0.9)]),
        'medium_amethyst_bud': build.amethyst_shards([(8, 7, 1.8), (5, 4, 1.2), (11, 5, 1.2)]),
        'large_amethyst_bud': build.amethyst_shards([(8, 10, 2.0), (4.5, 6, 1.3), (11.5, 7, 1.3)]),
        'amethyst_cluster': build.amethyst_shards([(8, 13, 2.2), (4, 9, 1.5), (12, 10, 1.5), (6, 6, 1.1), (10.5, 6, 1.1)]),
        'spawner': build.spawner_cage(),
        'mangrove_roots': build.root_strands(7, 6, (106, 82, 64), (62, 46, 38)),
        'mangrove_roots_top': build.root_strands(11, 6, (106, 82, 64), (62, 46, 38)),
        'mangrove_propagule_hanging': build.mangrove_propagule(),
        'item_ender_pearl': build.orb((12, 59, 55), (46, 143, 134), (160, 230, 220), 6),
        'item_slime_ball': build.orb((74, 140, 58), (127, 196, 106), (210, 245, 200), 5),
    }
    for side, seed in (('muddy_mangrove_roots_side', 13), ('muddy_mangrove_roots_top', 17)):
        m = load('mud').copy()
        m.alpha_composite(build.root_strands(seed, 5, (112, 88, 68), (66, 50, 40)))
        coded[side] = m
    for s in range(10):
        coded['destroy_stage_%d' % s] = build.crack_stage(s)
    for n, rows in HUD.items():
        coded[n] = hud_icon(*rows)
    for n, im in sorted(list(drawn.items()) + list(coded.items())):
        if os.path.exists(os.path.join(src, n + '.png')):
            continue
        if n.startswith('destroy_stage_'):
            # a crack keeps its see-through dark lines; the stage's line tone is stepped off the old
            px = [(28, 26, 24, 190) if p[3] else p for p in im.get_flattened_data()]
            new = Image.new('RGBA', im.size)
            new.putdata(px)
        else:
            new = redraw.redraw(im, n)[0]
        new.save(os.path.join(out, n + '.png'))
        made += 1
    print('redrew', made)


if __name__ == '__main__':
    main()
