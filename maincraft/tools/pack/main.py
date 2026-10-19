"""Paints our whole texture pack anew in one of three styles.

    python3 tools/pack/main.py STYLE OUT_DIR [--java DIR] [--bedrock DIR] [--self DIR] [--first DIR]
                               [--structure FILE] [name ...]

STYLE is java (tones of the Java pack), bedrock (tones of the Bedrock pack) or clean (flat, few
tones, soft shapes, like the simplified packs players like). --java is a Java pack's
assets/minecraft/textures folder, --bedrock a Bedrock pack's resource_pack/textures folder; they
give colours only. --self (our tex/ folder by default) is the last colour source, for pictures
the reference packs do not have. --first (the tex/ folder of our first pack) takes the colours from
our first pack instead, and keeps every pixel off it; --structure is the store of measured looks
the materials are painted to (structure.json by default; tools/pack/mid.py keeps ones between our
first pack's look and Minecraft's). Every picture is drawn by our own generators.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from core import Source  # noqa: E402
import natural  # noqa: E402
import wood  # noqa: E402
import masonry  # noqa: E402
import deco  # noqa: E402
import plants  # noqa: E402
import anim  # noqa: E402
import items  # noqa: E402
import sky  # noqa: E402
import extras  # noqa: E402
import structure  # noqa: E402

FAMILIES = [natural, wood, masonry, deco, plants, anim, items, sky, extras]

# pictures build.py makes itself when the pack lacks them, and the drawings of tools/art.py: painted
# here too, so each style has them in its own tones
EXTRA = ['oak_door_bottom', 'spruce_door_top', 'spruce_door_bottom', 'mangrove_log', 'mangrove_log_top',
         'cherry_log', 'cherry_log_top', 'pale_oak_log', 'pale_oak_log_top', 'pale_oak_planks', 'pale_oak_leaves',
         'small_amethyst_bud', 'medium_amethyst_bud', 'large_amethyst_bud', 'amethyst_cluster', 'spawner',
         'mangrove_roots', 'mangrove_roots_top', 'muddy_mangrove_roots_side', 'muddy_mangrove_roots_top',
         'mangrove_propagule_hanging', 'item_ender_pearl', 'item_slime_ball', 'grass_block_snow', 'redstone_torch',
         'hud_heart', 'hud_heart_empty', 'hud_food', 'hud_food_empty', 'hud_air'] + ['destroy_stage_%d' % _s for _s in range(10)]

# leaves our game shows as painted while a reference pack has them grey (tinted by its game): the
# source colours take that game's fixed tint
GREY_TINT = {'birch_leaves': (128, 167, 85), 'spruce_leaves': (97, 153, 97), 'pale_oak_leaves': (160, 166, 160)}
for _p in ('helmet', 'chestplate', 'leggings', 'boots'):     # leather armour: the leather's own brown
    GREY_TINT['item_leather_' + _p] = (160, 101, 64)
for _k in range(4):     # melon and pumpkin stems ripen from green to yellow-brown
    _t = _k / 3
    for _p in ('melon', 'pumpkin'):
        GREY_TINT['%s_stem_stage%d' % (_p, _k)] = (round(80 + 120 * _t), round(190 - 30 * _t), round(40 + 10 * _t))


# drawn pictures whose painting does not follow the first pack's shape: tiles with holes, flat
# panels, animated ones
SHAPE_SKIP = ('leaves', 'glass', 'pane', 'door', 'rail', 'ladder', 'vine', 'lichen', 'water', 'lava', 'fire',
              'sun', 'moon', 'iron_bars', 'scaffolding')


class Ctx:
    def __init__(self, style, src, own, has_ref):
        self.style, self.src, self.own, self.has_ref = style, src, own, has_ref
        self.reg, self.done = {}, {}
        self.drawn = {}             # the drawings of tools/art.py: the last colour source
        self.shapes = None          # with --first: drawn pictures keep the first pack's shapes (name -> picture)
        for fam in FAMILIES:
            fam.register(self.reg)

    def ref(self, name):
        """the reference picture a painted one must not equal pixel for pixel"""
        return self.src.px(name) if self.has_ref else None

    def px(self, *names):
        """the colour source pixels of the first of names that has a picture"""
        for n in names:
            p = self.src.px(n)
            if p is None:
                continue
            t = GREY_TINT.get(n)
            if t and sum(max(q[:3]) - min(q[:3]) for q in p) / len(p) < 12:
                p = [(q[0] * t[0] // 255, q[1] * t[1] // 255, q[2] * t[2] // 255, q[3]) for q in p]
            return p
        for n in names:
            if n in self.drawn and self.drawn[n].size == (16, 16):
                return list(self.drawn[n].get_flattened_data())
        return None

    def shape_source(self, name):
        """with --first, the first pack's picture of a drawn thing (an item, a flower, a crack, a HUD
        icon: mostly see-through, one frame) whose shape the painting keeps, else None"""
        if self.shapes is None or name in self.drawn or any(k in name for k in SHAPE_SKIP):
            return None
        im = self.src.image(name)
        if im is None:
            im = self.shapes.get(name)
        if im is None or im.size not in ((16, 16), (9, 9)):
            return None
        clear = sum(1 for p in im.get_flattened_data() if p[3] < 128)
        return im if clear > 0.25 * im.size[0] * im.size[1] else None

    def out(self, name):
        if name not in self.done:
            gen = self.reg.get(name)
            im = None
            shaped = self.shape_source(name)
            if shaped is not None:
                import items
                self.done[name] = items.from_shape(self, name, shaped)
                return self.done[name]
            if name in structure.PLAIN:          # a plain material: painted to its measured look
                im = structure.paint(self, name, self.px(name))
            self.done[name] = im or (gen(self) if gen else None)
        return self.done[name]


def main():
    args, opts = [], {}
    it = iter(sys.argv[1:])
    for a in it:
        if a.startswith('--'):
            opts[a[2:]] = next(it)
        else:
            args.append(a)
    style, out = args[0], args[1]
    if 'structure' in opts:
        structure.STORE = opts['structure']
    if 'variant' in opts:
        import core
        core.VARIANT = int(opts['variant'])
    only = set(args[2:])
    own_dir = opts.get('self', os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'tex'))
    own = Source('self', own_dir)
    if style == 'bedrock':
        src = Source('bedrock', opts['bedrock'], Source('java', opts['java'], own) if 'java' in opts else own)
    elif 'first' in opts:
        src = Source('self', opts['first'], own)
    elif 'java' in opts:
        src = Source('java', opts['java'], own)
    else:
        src = own
    ctx = Ctx(style, src, own, src is not own)
    # the drawings of tools/art.py (blocks whose models read set spots of the picture), from the
    # pictures of the colour source where it has them
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
    import art
    import relayout
    from PIL import Image
    if 'first' in opts:
        load = lambda n: src.image(n).copy()   # noqa: E731
    else:
        load = lambda n: Image.open(os.path.join(own_dir, n + '.png')).convert('RGBA')   # noqa: E731
    drawn, drawn_anim = art.all_textures(load)
    for k, frames in drawn_anim.items():
        s = Image.new('RGBA', (16, 16 * len(frames)))
        for j, f in enumerate(frames):
            s.paste(f, (0, 16 * j))
        drawn[k] = s
    ctx.drawn = drawn
    if 'first' in opts:
        import classic
        ctx.shapes = classic.originals(opts['first'])
    os.makedirs(out, exist_ok=True)
    names = sorted(set(f[:-4] for f in os.listdir(own_dir) if f.endswith('.png')) | set(EXTRA))
    made = missing = 0
    for n in names:
        if only and n not in only:
            continue
        im = ctx.out(n)
        if im is None:
            missing += 1
            continue
        im.save(os.path.join(out, n + '.png'))
        made += 1
    # the drawings of tools/art.py: redrawn region by region in this style (tools/pack/relayout.py)
    ratios = relayout.kind_ratios(ctx)
    for n, im in sorted(drawn.items()):
        if (only and n not in only) or n in names:
            continue
        if n in ctx.reg:                # drawn by a generator of ours
            ctx.out(n).save(os.path.join(out, n + '.png'))
            made += 1
            continue
        ref = ctx.ref(n)
        relayout.repaint(style, n, im, ratios, ref if ref and len(ref) == 256 else None).save(
            os.path.join(out, n + '.png'))
        made += 1
    print('painted', made, 'not yet drawn', missing)


if __name__ == '__main__':
    main()
