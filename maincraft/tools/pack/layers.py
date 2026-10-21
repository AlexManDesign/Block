"""Blocks put together in layers, the way Minecraft builds its textures (python3 tools/pack/main.py
STYLE OUT_DIR --layers ...).

Minecraft draws a family of blocks once and builds the rest from it: every wool, concrete, powder,
terracotta and stained glass colour is one drawing in other colours; mossy cobblestone is the
cobblestone with moss laid over it, cracked stone bricks the stone bricks with cracks; an ore is
the stone (or deepslate) with the ore's nuggets over it, the same nuggets in both; a grass block's
side is the dirt with the grass hanging over its top. Here each of those is painted that way from
our own base drawings (the bases themselves are our generators'), so a family reads as one.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from core import N, at, rng, white, rims, lum, select, Canvas, COLOURS, P  # noqa: E402


def recolour(base, src):
    """the base drawing in the colours of src: each pixel takes the source colour of the same rank
    of lightness (a small window of them averaged), the base's see-through kept"""
    px = list(base.get_flattened_data())
    drawn = sorted((i for i in range(len(px)) if px[i][3] > 0), key=lambda i: lum(px[i]))
    cols = sorted((p for p in (src or []) if p[3] > 127), key=lum)
    if not drawn or not cols:
        return base.copy()
    out = list(px)
    win = max(1, len(cols) // 24)
    for r, i in enumerate(drawn):
        k = r * len(cols) // len(drawn)
        w = cols[max(0, k - win):k + win + 1]
        out[i] = tuple(round(sum(p[t] for p in w) / len(w)) for t in range(3)) + (px[i][3],)
    im = base.copy()
    im.putdata(out)
    return im


def family(name, canon):
    def gen(ctx):
        return recolour(ctx.out(canon), ctx.px(name))
    return gen


def mossy(name, base):
    """the base block with moss grown over it, thickest at the top"""
    def gen(ctx):
        import masonry
        import natural
        rnd = rng(name)
        src = ctx.px(name) or []
        green = select(src, lambda p: p[1] > p[0] + 6 and p[1] > p[2])
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.px = list(ctx.out(base).get_flattened_data())
        c.part(natural.moss_f(rnd), green or src, masonry.moss_mask(rnd, 0.32))
        return c.image()
    return gen


def cracked(name, base):
    """the base block with dark cracks running across it"""
    def gen(ctx):
        rnd = rng(name)
        im = ctx.out(base).copy()
        px = list(im.get_flattened_data())
        dark = sorted((p for p in px if p[3] > 127), key=lum)[:max(1, len(px) // 16)]
        d = tuple(sum(p[t] for p in dark) // len(dark) for t in range(3))
        for _ in range(4):
            x, y = rnd.randrange(N), rnd.randrange(N)
            for _ in range(rnd.randrange(4, 9)):
                i = at(x, y)
                px[i] = tuple(int(v * 0.75) for v in d) + (px[i][3],)
                x += rnd.choice((-1, 0, 1))
                y += rnd.choice((0, 1, 1))
        im.putdata(px)
        return im
    return gen


def nuggets(rnd, clusters):
    """Minecraft-sized ore clumps: a few irregular lumps of about 6 to 14 pixels, spread over the face"""
    import math
    m = [False] * (N * N)
    pts = []
    for _ in range(clusters * 40):
        if len(pts) == clusters:
            break
        x, y = rnd.randrange(1, N - 2), rnd.randrange(1, N - 2)
        if all(abs(x - a) + abs(y - b) > 6 for a, b in pts):
            pts.append((x, y))
    for x, y in pts:
        r = rnd.uniform(1.7, 2.5)
        sx = rnd.uniform(0.8, 1.3)
        for dy in (-3, -2, -1, 0, 1, 2, 3):
            for dx in (-3, -2, -1, 0, 1, 2, 3):
                if math.hypot(dx * sx, dy) <= r and rnd.random() > 0.12 and 0 <= x + dx < N and 0 <= y + dy < N:
                    m[at(x + dx, y + dy)] = True
    return m


def ore(name, kind, base):
    """the base rock with the ore's nuggets over it: the nuggets of a kind are the same in stone and
    in deepslate (one overlay, two rocks)"""
    def gen(ctx):
        import masonry
        rnd = rng(kind + '_ore', 'nuggets')
        c = Canvas(ctx.style, name, ref=ctx.ref(name))
        c.px = list(ctx.out(base).get_flattened_data())
        src = ctx.px(name, kind + '_ore')
        orepx = select(src, masonry.ORE_PICK[kind]) if src else None
        m = nuggets(rnd, P(5, 4, 6))
        ids = [1 if m[i] else 0 for i in range(N * N)]
        r, w = rims(ids, 0.4, 0.4), white(rnd)
        c.part([0.5 + r[i] + 0.25 * w[i] for i in range(N * N)], orepx if orepx and len(orepx) >= 4 else src, m)
        for i in range(N * N):                 # the rock darkened under each nugget
            x, y = i % N, i // N
            if not m[i] and m[at(x - 1, y - 1)] and kind != 'coal':
                p = c.px[i]
                c.px[i] = (int(p[0] * 0.7), int(p[1] * 0.7), int(p[2] * 0.7), p[3])
        return c.image()
    return gen


def apart(im, ref, step=5):
    """no pixel left as the reference pack has it at the same place: one within step of it is moved
    step away (a dark colour lighter, a light one darker)"""
    if not ref or len(ref) != im.size[0] * im.size[1]:
        return im
    px = list(im.get_flattened_data())
    for i, (p, q) in enumerate(zip(px, ref)):
        if p[3] and q[3] and max(abs(p[t] - q[t]) for t in range(3)) < step:
            d = step if lum(q) < 128 else -step
            px[i] = tuple(min(255, max(0, q[t] + d)) for t in range(3)) + (p[3],)
    out = im.copy()
    out.putdata(px)
    return out


LAYERED = {}
for _col in COLOURS:
    if _col != 'white':
        for _f in ('wool', 'concrete', 'concrete_powder', 'stained_glass', 'stained_glass_pane_top'):
            LAYERED['%s_%s' % (_col, _f)] = family('%s_%s' % (_col, _f), 'white_' + _f)
    LAYERED[_col + '_terracotta'] = family(_col + '_terracotta', 'terracotta')
LAYERED['mossy_cobblestone'] = mossy('mossy_cobblestone', 'cobblestone')
LAYERED['mossy_stone_bricks'] = mossy('mossy_stone_bricks', 'stone_bricks')
for _b in ('stone_bricks', 'deepslate_bricks', 'deepslate_tiles', 'nether_bricks', 'polished_blackstone_bricks'):
    LAYERED['cracked_' + _b] = cracked('cracked_' + _b, _b)
for _k in ('coal', 'iron', 'copper', 'gold', 'diamond', 'emerald', 'lapis', 'redstone'):
    LAYERED[_k + '_ore'] = ore(_k + '_ore', _k, 'stone')
    LAYERED['deepslate_%s_ore' % _k] = ore('deepslate_%s_ore' % _k, _k, 'deepslate')


def _grass_side():
    import natural
    # Minecraft's grass hangs one to four pixels down the side, a few blades lower
    return natural.side_with_top('grass_block_side', 'dirt', 'grass_block_top', (2, 4), 4)


LAYERED['grass_block_side'] = lambda ctx: _grass_side()(ctx)


# ---- seamless tiles ---------------------------------------------------------------------------------
# Minecraft's materials tile without a seam: the edge of one block runs on into the next as smoothly
# as the pattern runs inside it. A picture of ours is made so along each way its reference is.

FACES = ('furnace', 'smithing', 'loom', 'stonecutter', 'bookshelf', 'barrel', 'composter', 'trapdoor', 'table',
         'chiseled', 'pillar_top', 'bone_block_top', 'log_top', 'glazed', 'door', 'tnt', 'pumpkin', 'melon',
         'lamp', 'observer', 'piston', 'lectern', 'smoker', 'jukebox', 'note_block', 'target', 'honey',
         'shulker', 'portal_frame', 'respawn', 'lodestone', 'beehive', 'bee_nest', 'mushroom_block',
         'command_block', 'dispenser', 'dropper', 'hopper', 'bed_', 'cake', 'sculk_s', 'crafter', 'vault',
         'spawner', 'trial', 'decorated', 'bell', 'campfire', 'repeater', 'comparator', 'daylight', 'enchanting',
         'anvil', 'lectern', 'grindstone', 'cauldron', 'brewing', 'cactus', 'carved')


def seam_ratio(px, w):
    """(sideways, upwards): the step across the tile's edge over the step between neighbours inside"""
    def step(a, b):
        return max(abs(a[t] - b[t]) for t in range(3))
    ix = sum(step(px[y * w + x], px[y * w + x + 1]) for y in range(w) for x in range(w - 1)) / (w * (w - 1))
    iy = sum(step(px[y * w + x], px[(y + 1) * w + x]) for y in range(w - 1) for x in range(w)) / (w * (w - 1))
    ex = sum(step(px[y * w + w - 1], px[y * w]) for y in range(w)) / w
    ey = sum(step(px[(w - 1) * w + x], px[x]) for x in range(w)) / w
    return ex / max(ix, 1.0), ey / max(iy, 1.0)


def _frame_even(px, w, sx, sy):
    """a face with a frame: the opposite rims made one, so two such faces side by side meet evenly"""
    px = list(px)
    if sx:
        for y in range(w):
            a, b = px[y * w], px[y * w + w - 1]
            m = tuple((a[t] + b[t]) // 2 for t in range(3)) + (a[3],)
            px[y * w] = px[y * w + w - 1] = m
    if sy:
        for x in range(w):
            a, b = px[x], px[(w - 1) * w + x]
            m = tuple((a[t] + b[t]) // 2 for t in range(3)) + (a[3],)
            px[x] = px[(w - 1) * w + x] = m
    return px


def _roll(px, w, dx, dy):
    return [px[((y + dy) % w) * w + (x + dx) % w] for y in range(w) for x in range(w)]


def _smoothest(px, w, axis):
    """where the tile changes least from one column (axis 0) or row (axis 1) to the next: the shift
    that puts that place on the tile's edge"""
    def step(a, b):
        return max(abs(a[t] - b[t]) for t in range(3))
    best = None
    for s in range(1, w):
        if axis == 0:
            v = sum(step(px[y * w + s - 1], px[y * w + s]) for y in range(w))
        else:
            v = sum(step(px[(s - 1) * w + x], px[s * w + x]) for x in range(w))
        if best is None or v < best[0]:
            best = (v, s)
    return best[1]


def _wrap(px, w, sx, sy):
    """a material: first the tile shifted round so its smoothest column and row fall on its edges
    (a tile of ours runs on across its edges, so this changes nothing but where they are); if that
    still leaves a seam, near the edges the tile's own pattern shifted that way, which runs on across
    the edge, blended in towards the edge, each pixel snapped back to the tile's own colours"""
    import math
    dx = _smoothest(px, w, 0) if sx else 0
    dy = _smoothest(px, w, 1) if sy else 0
    rolled = _roll(px, w, dx, dy)
    rx, ry = seam_ratio(rolled, w)
    if (not sx or rx <= 1.3) and (not sy or ry <= 1.3):
        return rolled
    pal = sorted(set(p[:3] for p in px if p[3] > 127))
    out = []
    for i, (p, q) in enumerate(zip(px, rolled)):
        x, y = i % w, i // w
        # the weight of the tile itself: 1 in the middle, 0 at the edges it is made seamless along
        wx = math.sin(math.pi * (x + 0.5) / w) if sx else 1.0
        wy = math.sin(math.pi * (y + 0.5) / w) if sy else 1.0
        k = min(1.0, max(0.0, 1.6 * wx * wy - 0.25))
        c = tuple(p[t] * k + q[t] * (1 - k) for t in range(3))
        best = min(pal, key=lambda s: (s[0] - c[0]) ** 2 + (s[1] - c[1]) ** 2 + (s[2] - c[2]) ** 2) if pal else c
        out.append(tuple(int(v) for v in best) + (p[3] if k >= 0.5 else q[3],))
    return out


def seamless(im, name, ref, face_only=False):
    """the picture tiling without a seam along each way its reference (Minecraft's own) does
    (face_only: only the frame's rims evened, nothing moved: a drawing models read by set spots)"""
    if ref is None:
        return im
    w = im.size[0]
    if im.size[1] % w or ref.size[0] != w or ref.size[1] % w:
        return im
    rp = list(ref.convert('RGBA').crop((0, 0, w, w)).get_flattened_data())
    if sum(1 for p in rp if p[3] < 128) > w * w // 20:
        return im                                      # a see-through drawing: not a tile
    rx, ry = seam_ratio(rp, w)
    out = im.copy()
    face = face_only or any(k in name for k in FACES)
    for f in range(im.size[1] // w):
        fr = im.crop((0, f * w, w, (f + 1) * w)).convert('RGBA')
        px = list(fr.get_flattened_data())
        if sum(1 for p in px if p[3] < 128) > w * w // 20:
            return im
        ox, oy = seam_ratio(px, w)
        sx = rx < 1.7 and ox > max(1.3, rx + 0.3)
        sy = ry < 1.7 and oy > max(1.3, ry + 0.3)
        if not (sx or sy):
            continue
        new = _frame_even(px, w, sx, sy) if face else _wrap(px, w, sx, sy)
        fr.putdata(new)
        out.paste(fr, (0, f * w))
    return out
