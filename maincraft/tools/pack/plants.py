"""Plants: flowers, grass and ferns, crops, stems, vines, water plants, mushrooms and fungi, corals."""
import math
from core import (N, at, fbm, white, mix, cells, rims, rng, Canvas, select, lum, COLOURS)
import natural


def green(p):
    return p[1] > p[0] + 4 and p[1] >= p[2]


def canvas(ctx, name):
    return Canvas(ctx.style, name, ref=ctx.ref(name))


class Sprite:
    """a little drawing of masks: stems, leaves, petals; each part coloured from its own pixels"""

    def __init__(self):
        self.parts = {}

    def put(self, part, x, y, v=0.5):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < N and 0 <= y < N:
            for m in self.parts.values():
                m.pop(y * N + x, None)
            self.parts.setdefault(part, {})[y * N + x] = v

    def stroke(self, part, pts, v0=0.5, v1=0.5):
        for k in range(len(pts) - 1):
            (x0, y0), (x1, y1) = pts[k], pts[k + 1]
            n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
            for j in range(n + 1):
                t = j / n
                self.put(part, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, v0 + (v1 - v0) * (k + t) / max(1, len(pts) - 1))

    def blob(self, part, cx, cy, rx, ry, v=0.6, light=0.35, rnd=None, holes=0.0):
        for y in range(N):
            for x in range(N):
                dx, dy = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
                d = dx * dx + dy * dy
                if d <= 1 and (rnd is None or rnd.random() >= holes):
                    self.put(part, x, y, v + light * (-dx - dy) * 0.5 - 0.15 * d)

    def paint(self, c, colours, w):
        for part, m in self.parts.items():
            mask = [i in m for i in range(N * N)]
            f = [m.get(i, 0) + 0.12 * w[i] for i in range(N * N)]
            c.part(f, colours.get(part) or colours.get('stem'), mask)


def plant_colours(ctx, name, alt=None):
    src = ctx.px(name, alt or 'poppy') or []
    g = select(src, green)
    head = select(src, lambda p: not green(p))
    return {'stem': g or src, 'head': head or src, 'all': src}


def stem_with_leaves(s, rnd, top=6, x=7.5, lean=0.0):
    s.stroke('stem', [(x, 15), (x + lean, top)], 0.3, 0.6)
    for y, side in ((12, -1), (10, 1)):
        xx = x + lean * (15 - y) / (15 - top)
        s.stroke('stem', [(xx, y), (xx + side * 2, y - 1), (xx + side * 3, y - 2)], 0.5, 0.8)


FLOWERS = {
    'poppy': 'round', 'dandelion': 'round_small', 'blue_orchid': 'orchid', 'allium': 'ball', 'azure_bluet': 'cluster',
    'red_tulip': 'cup', 'orange_tulip': 'cup', 'white_tulip': 'cup', 'pink_tulip': 'cup', 'oxeye_daisy': 'daisy',
    'cornflower': 'star', 'lily_of_the_valley': 'bells', 'wither_rose': 'round',
}


def flower(name, kind):
    def gen(ctx):
        rnd = rng(name)
        col = plant_colours(ctx, name)
        s = Sprite()
        if kind == 'ball':
            s.stroke('stem', [(7.5, 15), (7.5, 6)], 0.3, 0.6)
            s.stroke('stem', [(7.5, 12), (5, 10)], 0.5, 0.7)
            s.blob('head', 7.5, 4, 3.2, 3.2, rnd=rnd, holes=0.08)
        elif kind == 'cluster':
            for x in (4, 7, 11):
                s.stroke('stem', [(x, 15), (x + rnd.choice((-1, 0, 1)), 9)], 0.3, 0.6)
            for x, y in ((3, 8), (7, 6), (11, 8), (5, 10), (10, 11)):
                s.blob('head', x + 0.5, y + 0.5, 1.6, 1.6, 0.8)
                s.put('eye', x, y, 0.9)
        elif kind == 'daisy':
            stem_with_leaves(s, rnd, 8)
            s.blob('head', 7.5, 5.5, 3.6, 3.6, 0.8)
            s.blob('eye', 7.5, 5.5, 1.3, 1.3, 0.8)
        elif kind == 'bells':
            s.stroke('stem', [(6.5, 15), (6.5, 5), (9, 3)], 0.3, 0.6)
            s.stroke('stem', [(6.5, 13), (3, 10), (2, 7)], 0.5, 0.8)
            for x, y in ((9, 5), (11, 6), (8, 8), (10, 9)):
                s.blob('head', x + 0.5, y + 0.5, 1.2, 1.3, 0.8)
        elif kind == 'cup':
            stem_with_leaves(s, rnd, 7)
            for y in range(3, 8):
                for x in range(6, 10):
                    if not (y == 3 and x in (7, 8)):
                        s.put('head', x, y, 0.5 + 0.1 * (9 - x) - 0.05 * y)
        elif kind == 'star':
            stem_with_leaves(s, rnd, 7)
            for a in range(6):
                ang = a * math.pi / 3
                s.stroke('head', [(7.5, 4.5), (7.5 + 3 * math.cos(ang), 4.5 + 3 * math.sin(ang))], 0.9, 0.4)
        elif kind == 'orchid':
            s.stroke('stem', [(7.5, 15), (7.5, 9), (5, 6)], 0.3, 0.6)
            s.stroke('stem', [(7.5, 11), (10, 8)], 0.4, 0.6)
            s.blob('head', 4.5, 5, 2.5, 2.2, 0.7)
            s.blob('head', 10.5, 7, 2.2, 2.0, 0.6)
        else:                                    # round heads: poppy, dandelion, wither rose
            r = 2.2 if kind == 'round_small' else 3.0
            stem_with_leaves(s, rnd, 8)
            s.blob('head', 7.5, 5.5, r, r, 0.6, rnd=rnd, holes=0.05)
            s.put('eye', 7, 5, 0.1 if kind == 'round' else 0.9)
            s.put('eye', 8, 5, 0.1 if kind == 'round' else 0.9)
        c = canvas(ctx, name)
        heads = col['head']
        eye = [p for p in heads if lum(p) < 80] if kind == 'round' else [p for p in col['all'] if p[0] > 180 and p[1] > 150 and p[2] < 120]
        s.paint(c, {'stem': col['stem'], 'head': heads, 'eye': eye or heads}, white(rnd))
        return c.image()
    return gen


def blades(name, count=7, tall=False, top=False, fern=False):
    """grass: thin blades fanning up out of the ground; ferns: fronds with leaflets"""
    def gen(ctx):
        rnd = rng(name)
        s = Sprite()
        for k in range(count):
            x0 = 1.5 + k * 13 / max(1, count - 1) + rnd.uniform(-0.6, 0.6)
            h = rnd.uniform(6, 13) if not tall or top else 16
            if top:
                h = rnd.uniform(5, 14)
            lean = (x0 - 7.5) * rnd.uniform(0.15, 0.45)
            y1 = 15.5 - h
            pts = [(x0, 15.5), (x0 + lean * 0.5, 15.5 - h * 0.5), (x0 + lean, y1)]
            s.stroke('stem', pts, 0.2, 0.9)
            if fern:
                for j in range(2, int(h), 2):
                    t = j / h
                    px, py = x0 + lean * t, 15.5 - j
                    s.put('stem', px - 1, py + 0.5, 0.6)
                    s.put('stem', px + 1, py + 0.5, 0.6)
        c = canvas(ctx, name)
        s.paint(c, {'stem': ctx.px(name, 'short_grass')}, white(rnd))
        return c.image()
    return gen


def double_flower(name, kind, top):
    """two-block tall flowers: leafy stems below, the bloom above"""
    def gen(ctx):
        rnd = rng(name)
        col = plant_colours(ctx, name, kind + '_top')
        s = Sprite()
        if not top:
            for x, lean in ((5, -2), (8, 0), (11, 2)):
                s.stroke('stem', [(x, 15.5), (x + lean * 0.5, 8), (x + lean, 0)], 0.3, 0.7)
            for y in (4, 9, 13):
                s.blob('stem', 4 + (y % 3), y, 2.2, 1.2, 0.6)
                s.blob('stem', 12 - (y % 2), y - 2, 2.2, 1.2, 0.7)
        else:
            s.stroke('stem', [(7.5, 15.5), (7.5, 8)], 0.3, 0.6)
            if kind == 'sunflower':
                s.blob('stem', 5, 12, 2.5, 1.2, 0.6)
            elif kind == 'lilac':
                for x, y in ((5, 6), (8, 3), (11, 6), (7, 9), (10, 10), (4, 10)):
                    s.blob('head', x + 0.5, y + 0.5, 2.3, 2.3, 0.6, rnd=rnd, holes=0.15)
            elif kind in ('peony', 'rose_bush'):
                for x, y in ((5, 6), (10, 5), (8, 9), (4, 11), (12, 10)):
                    s.blob('head', x + 0.5, y + 0.5, 2.5, 2.3, 0.6, rnd=rnd, holes=0.08)
                    s.blob('stem', x + 2, y + 2.5, 1.5, 0.8, 0.5)
        c = canvas(ctx, name)
        s.paint(c, {'stem': col['stem'], 'head': col['head']}, white(rnd))
        return c.image()
    return gen


def sunflower_front(name):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'sunflower_front')
        petals = select(src, lambda p: p[0] > 180 and p[1] > 130) or src
        middle = select(src, lambda p: lum(p) < 110) or src
        s = Sprite()
        s.blob('head', 7.5, 7.5, 7, 7, 0.6, rnd=rnd, holes=0.0)
        s.blob('eye', 7.5, 7.5, 3.6, 3.6, 0.5)
        c = canvas(ctx, name)
        s.paint(c, {'head': petals, 'eye': middle, 'stem': petals}, white(rnd))
        for i in range(N * N):
            x, y = i % N + 0.5 - 8, i // N + 0.5 - 8
            if math.hypot(x, y) > 6.2 and (int(math.degrees(math.atan2(y, x)) // 30) % 2):
                c.px[i] = (0, 0, 0, 0)
        return c.image()
    return gen


def crop(name, kind, stage, stages):
    """crops growing: shoots that get taller and fuller; the last stage shows the harvest"""
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, kind + '_stage0') or []
        s = Sprite()
        t = (stage + 1) / stages
        ripe = stage == stages - 1
        for x in (2, 5, 8, 11, 14):
            h = 3 + 11 * t * rnd.uniform(0.8, 1.0)
            s.stroke('stem', [(x, 15.5), (x + rnd.uniform(-1, 1), 15.5 - h)], 0.3, 0.8)
            if kind == 'wheat' and ripe:
                for y in range(int(15.5 - h), int(15.5 - h) + 4):
                    s.put('head', x, y, 0.7)
                    s.put('head', x + 1, y + 1, 0.5)
            elif kind in ('carrots', 'potatoes') and stage >= 1:
                s.put('stem', x - 1, 15.5 - h + 1, 0.7)
                s.put('stem', x + 1, 15.5 - h + 2, 0.7)
        if ripe and kind in ('carrots', 'potatoes'):
            for x in (2, 8, 14):
                s.put('head', x, 15, 0.6)
                s.put('head', x + 1, 15, 0.4)
        c = canvas(ctx, name)
        g = select(src, green)
        other = select(src, lambda p: not green(p))
        if kind == 'wheat':
            g = select(src, lambda p: True)
        s.paint(c, {'stem': g or src, 'head': other or src}, white(rnd))
        return c.image()
    return gen


def stem_plant(name, stage):
    def gen(ctx):
        rnd = rng(name)
        s = Sprite()
        h = 3 + 3 * stage
        s.stroke('stem', [(7.5, 15.5), (7.5, 15.5 - h)], 0.3, 0.8)
        for y in range(int(16 - h), 15, 3):
            s.put('stem', 6.5 if y % 2 else 8.5, y, 0.6)
        c = canvas(ctx, name)
        s.paint(c, {'stem': ctx.px(name, 'melon_stem_stage0') or ctx.px('short_grass')}, white(rnd))
        return c.image()
    return gen


def vine_like(name, strands=3, leaves=True, hang=True, density=0.5):
    """hanging or climbing strands with leaves along them"""
    def gen(ctx):
        rnd = rng(name)
        s = Sprite()
        for k in range(strands):
            x = 2 + k * 12 / max(1, strands - 1) + rnd.uniform(-1, 1)
            pts = [(x + math.sin(y * 0.6 + k) * 1.2, y) for y in range(0, 17, 2)]
            s.stroke('stem', pts, 0.4, 0.6)
            if leaves:
                for y in range(1, 16, 3):
                    if rnd.random() < density:
                        xx = x + math.sin(y * 0.6 + k) * 1.2
                        s.blob('leaf', xx + rnd.choice((-1.5, 1.5)), y + 0.5, 1.6, 1.1, 0.7)
        c = canvas(ctx, name)
        src = ctx.px(name, 'vine')
        s.paint(c, {'stem': src, 'leaf': src}, white(rnd))
        return c.image()
    return gen


def lichen(name):
    def gen(ctx):
        rnd = rng(name)
        ids, d1, d2, n = cells(rnd, 9, 1.0)
        keep = {k for k in range(n) if rnd.random() < 0.55}
        m = [ids[i] in keep and d2[i] - d1[i] > 0.6 for i in range(N * N)]
        c = canvas(ctx, name)
        r, w = rims(ids, 0.3, 0.3), white(rnd)
        c.part([0.5 + r[i] + 0.3 * w[i] for i in range(N * N)], ctx.px(name), m)
        return c.image()
    return gen


def pad(name):
    def gen(ctx):
        rnd = rng(name)
        s = Sprite()
        s.blob('leaf', 7.5, 7.5, 7, 7, 0.6)
        for k in range(8):
            s.parts['leaf'].pop(at(7 + k // 2, 7 - k), None)
            s.parts['leaf'].pop(at(8 + k // 2, 7 - k), None)
        for k in range(2, 7):
            s.put('vein', 7.5 - k * 0.7, 7.5 + k * 0.5, 0.2)
        c = canvas(ctx, name)
        src = ctx.px(name, 'lily_pad')
        s.paint(c, {'leaf': src, 'vein': sorted(src, key=lum)[:20]}, white(rnd))
        return c.image()
    return gen


def mushroom(name, kind):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name, 'red_mushroom')
        cap = select(src, lambda p: max(p[:3]) - min(p[:3]) > 40) or src
        stalk = select(src, lambda p: max(p[:3]) - min(p[:3]) <= 40 and lum(p) > 100) or src
        s = Sprite()
        s.stroke('stalk', [(7.5, 15), (7.5, 9)], 0.6, 0.6)
        s.put('stalk', 8.5, 13, 0.4)
        if kind == 'flat':
            s.blob('cap', 7.5, 8.5, 5, 2, 0.6)
        else:
            s.blob('cap', 7.5, 7.5, 4, 3, 0.6)
            for x, y in ((6, 6), (9, 7)):
                if kind == 'spotted':
                    s.put('dot', x, y, 0.9)
        c = canvas(ctx, name)
        s.paint(c, {'cap': cap, 'stalk': stalk, 'dot': stalk, 'stem': cap}, white(rnd))
        return c.image()
    return gen


def roots(name):
    def gen(ctx):
        rnd = rng(name)
        s = Sprite()
        for k in range(6):
            x0 = 2 + k * 2.4
            s.stroke('stem', [(x0, 15.5), (x0 + rnd.uniform(-2, 2), 15.5 - rnd.uniform(4, 11))], 0.3, 0.8)
        c = canvas(ctx, name)
        s.paint(c, {'stem': ctx.px(name)}, white(rnd))
        return c.image()
    return gen


def dead_bush(name):
    def gen(ctx):
        rnd = rng(name)
        s = Sprite()
        s.stroke('stem', [(7.5, 15.5), (7.5, 10)], 0.4, 0.6)
        for ang, ln in ((-60, 7), (-100, 8), (-130, 6), (-30, 6), (-80, 5)):
            a = math.radians(ang)
            x1, y1 = 7.5 + ln * math.cos(a), 10 + ln * math.sin(a)
            s.stroke('stem', [(7.5, 10), (x1, y1)], 0.5, 0.8)
            s.stroke('stem', [((7.5 + x1) / 2, (10 + y1) / 2), ((7.5 + x1) / 2 + 2, (10 + y1) / 2 - 2)], 0.6, 0.8)
        c = canvas(ctx, name)
        s.paint(c, {'stem': ctx.px(name)}, white(rnd))
        return c.image()
    return gen


def cane(name, stalks=(2, 7, 12)):
    """sugar cane and bamboo: jointed stalks"""
    def gen(ctx):
        rnd = rng(name)
        s = Sprite()
        for k, x in enumerate(stalks):
            off = rnd.randrange(8)
            for y in range(N):
                node = (y + off) % 6 == 0
                s.put('stem', x, y, 0.2 if node else 0.7)
                s.put('stem', x + 1, y, 0.1 if node else 0.45)
            s.put('stem', x + 2, (off + 3) % N, 0.6)
        c = canvas(ctx, name)
        s.paint(c, {'stem': ctx.px(name)}, white(rnd))
        return c.image()
    return gen


def bamboo(name):
    def gen(ctx):
        rnd = rng(name)
        w = white(rnd)
        f = [0.6 + 0.25 * math.cos(2 * math.pi * ((i % N) % 4) / 4) + 0.1 * w[i] - (0.6 if i // N in (7,) else 0)
             for i in range(N * N)]
        c = canvas(ctx, name)
        c.part(f, ctx.px(name))
        return c.image()
    return gen


def water_plant(name, frames=1, kind='seagrass'):
    """seagrass and kelp: wavy strands; frames sway a little"""
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name)
        out = []
        for fr in range(frames):
            s = Sprite()
            phase = fr * 2 * math.pi / max(1, frames)
            xs = (4, 8, 12) if kind == 'seagrass' else (7,)
            for k, x in enumerate(xs):
                pts = [(x + 1.3 * math.sin(y * 0.5 + phase + k), y) for y in range(16, -1, -2)]
                s.stroke('stem', pts, 0.3, 0.8)
                if kind == 'kelp':
                    for y in range(2, 16, 4):
                        s.blob('stem', x + 2.5 + math.sin(y + phase), y, 2, 1, 0.6)
                        s.blob('stem', x - 2.5 + math.sin(y + phase + 1), y + 2, 2, 1, 0.5)
            c = canvas(ctx, name)
            s.paint(c, {'stem': src}, white(rnd))
            out.append(c.image())
        if frames == 1:
            return out[0]
        from PIL import Image
        strip = Image.new('RGBA', (N, N * frames))
        for k, im in enumerate(out):
            strip.paste(im, (0, k * N))
        return strip
    return gen


def berry_bush(name):
    def gen(ctx):
        rnd = rng(name)
        src = ctx.px(name)
        s = Sprite()
        ids, d1, d2, n = cells(rnd, 12, 1.0)
        for i in range(N * N):
            if d2[i] - d1[i] > 0.5 and rnd.random() > 0.2 and i // N > 2:
                s.put('leaf', i % N, i // N, 0.3 + 0.5 * (1 - d1[i] / 4))
        for _ in range(6):
            x, y = rnd.randrange(2, 14), rnd.randrange(4, 14)
            s.put('berry', x, y, 0.8)
            s.put('berry', x + 1, y, 0.6)
        c = canvas(ctx, name)
        s.paint(c, {'leaf': select(src, green) or src, 'berry': select(src, lambda p: p[0] > p[1] + 50) or src}, white(rnd))
        return c.image()
    return gen


def coral_fan(name):
    def gen(ctx):
        rnd = rng(name)
        s = Sprite()
        for ang in range(-150, -20, 18):
            a = math.radians(ang)
            ln = rnd.uniform(5, 8)
            s.stroke('stem', [(7.5, 15), (7.5 + ln * math.cos(a), 15 + ln * math.sin(a) * 1.3)], 0.7, 0.4)
        c = canvas(ctx, name)
        s.paint(c, {'stem': ctx.px(name)}, white(rnd))
        return c.image()
    return gen


def pickle(name):
    def gen(ctx):
        rnd = rng(name)
        w = white(rnd)
        f = [0.5 + 0.3 * math.cos(math.pi * ((i % N) % 4) / 2) - 0.02 * (i // N) + 0.15 * w[i] for i in range(N * N)]
        for i in range(N * N):
            if i // N < 2 and (i % N) % 4 == 1:
                f[i] = 1.2
        c = canvas(ctx, name)
        c.part(f, ctx.px(name))
        return c.image()
    return gen


def register(reg):
    for n, k in FLOWERS.items():
        reg[n] = flower(n, k)
    reg['short_grass'] = blades('short_grass', 8)
    reg['fern'] = blades('fern', 5, fern=True)
    reg['tall_grass_bottom'] = blades('tall_grass_bottom', 7, tall=True)
    reg['tall_grass_top'] = blades('tall_grass_top', 7, tall=True, top=True)
    reg['large_fern_bottom'] = blades('large_fern_bottom', 5, tall=True, fern=True)
    reg['large_fern_top'] = blades('large_fern_top', 5, tall=True, top=True, fern=True)
    for kind in ('lilac', 'peony', 'rose_bush'):
        reg[kind + '_top'] = double_flower(kind + '_top', kind, True)
        reg[kind + '_bottom'] = double_flower(kind + '_bottom', kind, False)
    reg['sunflower_bottom'] = double_flower('sunflower_bottom', 'sunflower', False)
    reg['sunflower_front'] = sunflower_front('sunflower_front')
    for st in (0, 2, 4, 7):
        reg['wheat_stage%d' % st] = crop('wheat_stage%d' % st, 'wheat', [0, 2, 4, 7].index(st), 4)
    for kind in ('carrots', 'potatoes'):
        for st in range(4):
            reg['%s_stage%d' % (kind, st)] = crop('%s_stage%d' % (kind, st), kind, st, 4)
    for kind in ('melon', 'pumpkin'):
        for st in range(4):
            reg['%s_stem_stage%d' % (kind, st)] = stem_plant('%s_stem_stage%d' % (kind, st), st)
    reg['vine'] = vine_like('vine', 4, True, density=0.7)
    reg['cave_vines'] = vine_like('cave_vines', 2, True, density=0.8)
    reg['weeping_vines'] = vine_like('weeping_vines', 3, False)
    reg['twisting_vines'] = vine_like('twisting_vines', 3, False)
    reg['glow_lichen'] = lichen('glow_lichen')
    reg['lily_pad'] = pad('lily_pad')
    reg['red_mushroom'] = mushroom('red_mushroom', 'spotted')
    reg['brown_mushroom'] = mushroom('brown_mushroom', 'flat')
    reg['crimson_fungus'] = mushroom('crimson_fungus', 'spotted')
    reg['warped_fungus'] = mushroom('warped_fungus', 'round')
    reg['crimson_roots'] = roots('crimson_roots')
    reg['warped_roots'] = roots('warped_roots')
    reg['nether_sprouts'] = roots('nether_sprouts')
    reg['dead_bush'] = dead_bush('dead_bush')
    reg['sugar_cane'] = cane('sugar_cane')
    reg['bamboo_stalk'] = bamboo('bamboo_stalk')
    reg['seagrass'] = water_plant('seagrass', 1, 'seagrass')
    reg['kelp'] = water_plant('kelp', 1, 'kelp')
    reg['sweet_berry_bush'] = berry_bush('sweet_berry_bush')
    reg['sea_pickle'] = pickle('sea_pickle')
    for cname in ('tube', 'brain', 'bubble', 'fire', 'horn'):
        reg[cname + '_coral_fan'] = coral_fan(cname + '_coral_fan')
        reg['dead_%s_coral_fan' % cname] = coral_fan('dead_%s_coral_fan' % cname)
        reg[cname + '_coral_block'] = natural.make_simple(cname + '_coral_block', natural.blotch_f(0.6, 20))
        reg['dead_%s_coral_block' % cname] = natural.make_simple('dead_%s_coral_block' % cname, natural.blotch_f(0.5, 20))
    reg['azalea_plant'] = blades('azalea_plant', 4)
