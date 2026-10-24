"""Re-draw the Maincraft block atlas and mob skins.
usage: main.py <work dir>: reads <work>/orig/{meta.json,atlas.png,ent_*.png}, writes <work>/new/"""
import sys, json, os, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from PIL import Image
from retex import *
from mobs import BOXES, redraw_mob

S = sys.argv[1]
os.makedirs(S + '/new', exist_ok=True)
meta = json.load(open(S + '/orig/meta.json'))
LAY = meta['layers']
ANIM = meta['anim']
ATL = np.array(Image.open(S + '/orig/atlas.png').convert('RGBA'))
OUT = ATL.copy()
INV = {v: k for k, v in LAY.items()}
TINT = {'grass': (0x91, 0xBD, 0x59), 'foliage': (0x77, 0xAB, 0x2F), 'water': (0x3F, 0x76, 0xE4)}


def tile(l, src=ATL):
    if isinstance(l, str):
        l = LAY[l]
    return src[(l // 32) * 16:(l // 32) * 16 + 16, (l % 32) * 16:(l % 32) * 16 + 16].copy()


def put(l, t):
    if isinstance(l, str):
        l = LAY[l]
    OUT[(l // 32) * 16:(l // 32) * 16 + 16, (l % 32) * 16:(l % 32) * 16 + 16] = t


DONE = {}


def R(name, salt=0):
    return np.random.default_rng(seed_of(name, salt))


# ---------------------------------------------------------------- categories
STOCH = set('''dirt coarse_dirt sand red_sand gravel clay snow soul_sand soul_soil netherrack mud end_stone
moss_block pale_moss_block pale_moss_carpet calcite tuff andesite diorite granite stone deepslate blackstone bedrock
obsidian crying_obsidian glowstone nether_wart_block warped_wart_block mycelium_top podzol_top grass_block_top_bt
crimson_nylium warped_nylium packed_ice blue_ice amethyst_block budding_amethyst dripstone_block raw_copper_block
raw_gold_block raw_iron_block rooted_dirt smooth_basalt lava_still terracotta coal_block'''.split())
COLORS = 'white orange magenta light_blue yellow lime pink gray light_gray cyan purple blue brown green red black'.split()
for c in COLORS:
    STOCH |= {c + '_concrete', c + '_concrete_powder', c + '_wool', c + '_terracotta'}
WOODS = 'oak spruce birch jungle acacia dark_oak mangrove cherry pale_oak'.split()
for w in WOODS:
    STOCH |= {w + '_log', 'stripped_' + w + '_log'}
STOCH |= {'stripped_crimson_stem', 'stripped_warped_stem'}
STOCH.discard('birch_log')  # its black marks are drawn features
PLANKS = {w + '_planks' for w in WOODS}
LOGTOP = {w + '_log_top' for w in WOODS} | {'stripped_' + w + '_log_top' for w in WOODS} | \
    {'crimson_stem_top', 'warped_stem_top', 'stripped_crimson_stem_top', 'stripped_warped_stem_top'}
COBBLE = {'cobblestone', 'mossy_cobblestone', 'cobbled_deepslate'}
ORES = {n for n in LAY if n.endswith('_ore')} | {'gilded_blackstone'}
GLASS = {'glass'}
SKIP = lambda n: n.startswith('item_') or n.startswith('destroy_stage_') or n in ('white', 'flower_pot_icon')
# 3D (space + time) re-synthesis for liquids and living surfaces
ANIM3D = {'water_still', 'water_still_bt', 'water_flow', 'water_flow_bt', 'lava_flow', 'magma', 'crimson_stem',
          'warped_stem', 'sculk', 'prismarine'}


# ---------------------------------------------------------------- generators
def g_stoch(name, t=None):
    t = tile(name) if t is None else t
    return stochastic(t, R(name))


def g_regrain(name, t=None, **kw):
    t = tile(name) if t is None else t
    return regrain(t, R(name), **kw)


def g_planks(name):
    t = tile(name)
    rng = R(name)
    L = lum(t)
    rows = L.mean(1)
    thr = np.median(rows) - 0.6 * rows.std()
    seams = [y for y in range(16) if rows[y] < thr]
    if not (3 <= len(seams) <= 6):
        return g_regrain(name)
    # boards between seams
    isseam = np.zeros(16, bool)
    isseam[seams] = True
    prof = rows.copy()
    S_ = np.repeat(prof[:, None], 16, 1)
    dark = L[isseam].mean()
    # fresh butt joints: one per board, at a new column
    y = 0
    boards = []
    while y < 16:
        if isseam[y]:
            y += 1
            continue
        y0 = y
        while y < 16 and not isseam[y]:
            y += 1
        boards.append((y0, y))
    used = set()
    for (y0, y1) in boards:
        for _ in range(20):
            x = int(rng.integers(1, 15))
            if all(abs(x - u) > 2 for u in used):
                break
        used = {x}
        S_[y0:y1, x] = dark
        S_[y0:y1, (x + 1) % 16] += 0.25 * (prof.max() - prof.mean())
    D = L - blur(L, 1.0)
    D[isseam] = 0
    G = phase_randomize(D, rng, smooth=0.6)
    # grain stretched along the boards
    G = 0.6 * G + 0.4 * np.roll(G, 1, 1) + 0.3 * np.roll(G, -1, 1)
    G /= G.std() + 1e-9
    F = S_ + G * D[~isseam].std() * 1.1
    out = t.copy()
    g, ng = hue_groups(t, t[..., 3] > 0, rng, k=4, merge=18)
    for j in range(ng):
        rank_transfer(F, t[g == j], g == j, out)
    return out


def torus_d(a, b):
    d = np.abs(a - b)
    return np.minimum(d, 16 - d)


def g_cobble(name):
    t = tile(name)
    rng = R(name)
    L = lum(t)
    yy, xx = np.mgrid[0:16, 0:16] + 0.5
    # poisson-disc seeds on the torus
    pts = []
    for _ in range(400):
        p = rng.random(2) * 16
        if all(np.hypot(*torus_d(p, q)) > 4.6 for q in pts):
            pts.append(p)
    pts = np.array(pts)
    d = np.stack([np.hypot(torus_d(yy, p[0]), torus_d(xx, p[1])) for p in pts], 0)
    # wobbly cell borders
    warp = phase_randomize(rng.standard_normal((16, 16)), rng) * 0.35
    srt = np.sort(d, 0)
    cell = d.argmin(0)
    edge = srt[1] - srt[0] + warp
    bright = rng.normal(0, 1, len(pts))
    F = np.zeros((16, 16))
    for c, p in enumerate(pts):
        m = cell == c
        dy = (yy - p[0] + 8) % 16 - 8
        dx = (xx - p[1] + 8) % 16 - 8
        rr = np.hypot(dy[m], dx[m])
        F[m] = 0.45 * bright[c] - 0.22 * (dy[m] + dx[m]) - 0.12 * rr
    F += 0.3 * phase_randomize(L - blur(L, 1.0), rng)
    F = np.where(edge < 1.0, -8 + edge, F)
    out = t.copy()
    mask = t[..., 3] > 0
    if name == 'mossy_cobblestone':
        g, ng = hue_groups(t, mask, rng, k=4, merge=12)
        # the larger group is stone, the others moss: moss gets a new layout
        sizes = [(g == j).sum() for j in range(ng)]
        st = int(np.argmax(sizes))
        moss_o = (g != st) & mask
        mf = blur(moss_o.astype(float), 0.8) * 0.4 + phase_randomize(moss_o.astype(float), rng, smooth=0.5)
        k = moss_o.sum()
        thr = np.sort(mf.ravel())[::-1][k - 1]
        moss_n = mf >= thr
        rank_transfer(F, t[~moss_o], ~moss_n, out)
        rank_transfer(F + phase_randomize(L, rng), t[moss_o], moss_n, out)
    else:
        rank_transfer(F, t[mask], mask, out)
    return out


def g_logtop(name):
    t = tile(name)
    rng = R(name)
    L = lum(t)
    yy, xx = np.mgrid[0:16, 0:16]
    cheb = np.maximum(np.abs(xx - 7.5), np.abs(yy - 7.5)) - 0.5
    ring = cheb.astype(int)
    prof = np.array([L[ring == k].mean() for k in range(8)])
    eu = np.hypot(xx - 7.5, yy - 7.5) - 0.5
    warp = blur(rng.standard_normal((16, 16)), 1.6, wrap=False)
    warp = warp / (warp.std() + 1e-9) * 0.35
    r = np.clip(0.8 * cheb + 0.2 * np.minimum(eu, 7) + warp, 0, 6.49)
    r = np.clip(np.round(r), 0, 6).astype(int)
    F = prof[r] + 0.12 * prof[:7].std() * rng.standard_normal((16, 16))
    out = t.copy()
    outer = ring == 7
    rank_transfer(F, t[~outer], ~outer, out)
    Fb = phase_randomize(np.where(outer, L, L[outer].mean()), rng)
    rank_transfer(Fb, t[outer], outer, out)
    return out


def ore_base(name):
    if name.startswith('deepslate_'):
        return 'deepslate'
    if name.startswith('nether_'):
        return 'netherrack'
    if name == 'gilded_blackstone':
        return 'blackstone'
    return 'stone'


def g_ore(name):
    """Ore specks are cut out of the original (each with its dark rim), turned / mirrored
    and scattered over freshly drawn rock."""
    t = tile(name)
    rng = R(name)
    bname = ore_base(name)
    b = tile(bname).reshape(-1, 4)[:, :3].astype(float)
    px = t[..., :3].astype(float)
    dist = np.sqrt(((px[:, :, None, :] - b[None, None]) ** 2).sum(-1)).min(-1)
    ore = dist > 28
    strong = ore & (dist > 60)
    if strong.sum() < 0.2 * ore.sum():
        strong = ore & (dist >= np.percentile(dist[ore], 55))
    if bname == 'netherrack':
        out = NEWT('netherrack').copy()
    else:
        out = t.copy()
        rank_transfer(phase_randomize(lum(tile(bname)), rng), t[~ore], np.ones((16, 16), bool), out)
    comps, _ = components(strong, conn8=True)
    stamps = []
    for c in comps:
        ys = [p[0] for p in c]; xs = [p[1] for p in c]
        y0, y1, x0, x1 = min(ys) - 1, max(ys) + 2, min(xs) - 1, max(xs) + 2
        h, w = y1 - y0, x1 - x0
        pm = np.zeros((h, w), bool)
        pc = np.zeros((h, w, 4), np.uint8)
        cs = set(c)
        for yy in range(h):
            for xx in range(w):
                Y, X = (yy + y0) % 16, (xx + x0) % 16
                own = (Y, X) in cs
                rim = ore[Y, X] and not strong[Y, X] and any(((Y + dy) % 16, (X + dx) % 16) in cs
                                                            for dy in (-1, 0, 1) for dx in (-1, 0, 1))
                if own or rim:
                    pm[yy, xx] = True
                    pc[yy, xx] = t[Y, X]
        stamps.append((pm, pc))
    occ = np.zeros((16, 16), bool)
    stamps.sort(key=lambda s: -s[0].sum())
    for pm, pc in stamps:
        k = int(rng.integers(4))
        pm2, pc2 = np.rot90(pm, k), np.rot90(pc, k)
        if rng.random() < 0.5:
            pm2, pc2 = pm2[:, ::-1], pc2[:, ::-1]
        h, w = pm2.shape
        for _ in range(200):
            oy, ox = int(rng.integers(16)), int(rng.integers(16))
            ys = (np.arange(h) + oy) % 16
            xs = (np.arange(w) + ox) % 16
            if not (occ[np.ix_(ys, xs)] & pm2).any():
                break
        sub = out[np.ix_(ys, xs)]
        sub[pm2] = pc2[pm2]
        out[np.ix_(ys, xs)] = sub
        o2 = occ[np.ix_(ys, xs)]
        o2 |= pm2
        occ[np.ix_(ys, xs)] = o2
    return out


def g_glass(name):
    t = tile(name)
    rng = R(name)
    out = t.copy()
    yy, xx = np.mgrid[0:16, 0:16]
    frame = (yy == 0) | (yy == 15) | (xx == 0) | (xx == 15)
    inner = ~frame
    a = t[..., 3]
    vals, cnt = np.unique(t[inner].reshape(-1, 4), axis=0, return_counts=True)
    bg = vals[cnt.argmax()]
    gl = inner & np.any(t != bg, -1)
    out[inner] = bg
    n = gl.sum()
    # new diagonal glints
    occ = np.zeros((16, 16), bool)
    newm = np.zeros((16, 16), bool)
    F = np.zeros((16, 16))
    tries = 0
    while newm.sum() < n and tries < 200:
        tries += 1
        ln = int(min(n - newm.sum(), rng.integers(3, 6)))
        y0, x0 = int(rng.integers(2 + ln, 14)), int(rng.integers(2, 14 - ln))
        cells = [(y0 - i, x0 + i) for i in range(ln)]
        if any(occ[max(0, y - 1):y + 2, max(0, x - 1):x + 2].any() for y, x in cells):
            continue
        for i, (y, x) in enumerate(cells):
            newm[y, x] = True
            F[y, x] = rng.random() + (1 if i in (0, ln - 1) else 2)
        for y, x in cells:
            occ[y, x] = True
    rank_transfer(F, t[gl], newm, out)
    out[frame] = regrain(t, rng, wrap=False)[frame]
    return out


def leaves_mask(base_name, t):
    rng = R(base_name, 1)
    m = t[..., 3] > 0
    k = m.sum()
    f = phase_randomize(m.astype(float), rng, smooth=0.4)
    thr = np.sort(f.ravel())[::-1][k - 1]
    return f >= thr


def g_leaves_family(x):
    """x: e.g. 'oak_leaves'. Handles x, x_bt, x_opaque, x_opaque_bt consistently."""
    has_bt = x + '_bt' in LAY
    src = x + '_bt' if has_bt else x
    t = tile(src)
    rng = R(x)
    m_new = leaves_mask(x, t)
    m_old = t[..., 3] > 0
    F = phase_randomize(np.where(m_old, lum(t), 0), rng, m_old)
    new = np.zeros_like(t)
    rank_transfer(F, t[m_old], m_new, new)
    new[..., 3] = np.where(m_new, 255, 0)
    res = {src: new}
    if has_bt:
        res[x] = tint_img(new, TINT['foliage'])
    for suf in ('_opaque', '_opaque_bt'):
        on = x + suf
        if on not in LAY:
            continue
        o = tile(on)
        leafsrc = res[x + '_bt'] if suf == '_opaque_bt' else res[x]
        out = leafsrc.copy()
        fill_cols = o[~m_old]
        Ff = phase_randomize(lum(o), R(on))
        rank_transfer(Ff, fill_cols, ~m_new, out)
        out[..., 3] = 255
        res[on] = out
    return res


def tint_img(t, c):
    o = t.copy()
    o[..., :3] = np.clip(np.round(t[..., :3].astype(float) * np.array(c) / 255.0), 0, 255).astype(np.uint8)
    return o


def frames(name):
    st, n, _ = ANIM[name]
    return st, n, np.stack([tile(st + i) for i in range(n)], 0)


def g_anim3d(name):
    st, n, fr = frames(name)
    rng = R(name)
    L = lum(fr)
    F = phase_randomize(L, rng)
    out = fr.copy()
    for i in range(n):
        m = fr[i, ..., 3] > 0
        g, ng = hue_groups(fr[i], m, np.random.default_rng(seed_of(name, 7)), k=3, merge=22)
        o = fr[i].copy()
        for j in range(ng):
            rank_transfer(F[i], fr[i][g == j], g == j, o)
        o[..., 3] = fr[i, ..., 3]
        out[i] = o
    return st, out


def g_anim_regrain(name):
    st, n, fr = frames(name)
    rng = R(name)
    noise = phase_randomize(rng.standard_normal((16, 16)), rng)
    out = fr.copy()
    for i in range(n):
        out[i] = regrain(fr[i], np.random.default_rng(seed_of(name, i)), noise=noise, wrap=False)
    return st, out


def substitute(name, t_new, base):
    """Pixels of the original that were literally the base texture (e.g. the dirt part of a
    grass side) take the re-drawn base texture, so neighbours keep matching."""
    o = tile(name)
    b = tile(base)
    same = np.all(o == b, -1)
    if same.sum() < 40:
        return t_new
    t_new = t_new.copy()
    t_new[same] = NEWT(base)[same]
    return t_new


def NEWT(name):
    if name not in DONE:
        gen(name)
    return DONE[name]


def grass_side_overlay(name):
    """Grass fringe: keep the grass, re-draw the ragged lower edge column by column."""
    t = tile(name)
    rng = R(name)
    a = t[..., 3] > 0
    depth = np.array([a[:, x].nonzero()[0].max() + 1 if a[:, x].any() else 0 for x in range(16)])
    nd = depth.copy()
    for x in range(16):
        nd[x] = depth[(x + int(rng.integers(-2, 3))) % 16] + int(rng.integers(-1, 2))
    nd = np.clip(nd, depth.min(), depth.max())
    m = np.zeros((16, 16), bool)
    for x in range(16):
        m[:nd[x], x] = True
    F = phase_randomize(np.where(a, lum(t), lum(t)[a].mean()), rng)
    yy = np.arange(16)[:, None]
    F = F - np.where(yy == nd[None, :] - 1, 6, 0) - np.where(yy == nd[None, :] - 2, 2.5, 0)
    out = np.zeros_like(t)
    rank_transfer(F, t[a], m, out)
    out[..., 3] = np.where(m, 255, 0)
    return out, nd


def drift(a, b, wrap):
    """How much the picture moved at the 2-3 pixel scale (0 = same look)."""
    m = a[..., 3] > 0
    la, lb = lum(a), lum(b)
    ba = blur(la, 0.8, wrap=wrap, mask=None if wrap else m)
    bb = blur(lb, 0.8, wrap=wrap, mask=None if wrap else m)
    return np.abs(ba - bb)[m].mean() / (la[m].std() + 1e-6)


def changed(a, b):
    vis = (a[..., 3] > 0) | (b[..., 3] > 0)
    return ((np.abs(a.astype(int) - b).sum(-1) > 0) & vis).sum() / max(1, vis.sum())


def gen(name):
    if name in DONE:
        return DONE[name]
    if name.endswith('_bt') and name[:-3] in LAY and not name.startswith('water') and 'leaves' not in name:
        gen(name[:-3])
        return DONE[name]
    base = name[:-3] if name.endswith('_bt') else name
    # leaves families
    lf = re.sub(r'(_opaque)?(_bt)?$', '', name)
    if lf.endswith('_leaves') and lf in LAY:
        for k, v in g_leaves_family(lf).items():
            DONE[k] = v
        return DONE[name]
    if name in ('grass_block_side', 'grass_block_side_bt', 'grass_block_side_overlay'):
        ov, nd = grass_side_overlay('grass_block_side_overlay')
        # dirt with a one-pixel shadow right under the grass fringe
        bt = NEWT('dirt').copy()
        for x in range(16):
            if nd[x] < 16:
                bt[nd[x], x, :3] = np.round(bt[nd[x], x, :3] * 0.4).astype(np.uint8)
        plain = bt.copy()
        tg = tint_img(ov, TINT['grass'])
        m = ov[..., 3] > 0
        plain[m] = tg[m]
        DONE.update({'grass_block_side_bt': bt, 'grass_block_side_overlay': ov, 'grass_block_side': plain})
        return DONE[name]
    # plain + _bt tinted pairs (grass, ferns, vines)
    if name + '_bt' in LAY and not name.startswith('water'):
        kind = 'grass'
        if name.startswith('vine'):
            kind = 'foliage'
        src = name + '_bt'
        t = g_stoch(src) if src in STOCH else g_regrain(src, wrap=False)
        DONE[src] = t
        DONE[name] = tint_img(t, TINT[kind])
        return DONE[name]
    if name in STOCH:
        t = g_stoch(name)
    elif name in PLANKS:
        t = g_planks(name)
    elif name in LOGTOP:
        t = g_logtop(name)
    elif name in COBBLE:
        t = g_cobble(name)
    elif name in ORES:
        t = g_ore(name)
    elif name in GLASS:
        t = g_glass(name)
    elif len(np.unique(tile(name)[tile(name)[..., 3] > 0].reshape(-1, 4), axis=0)) < 7:
        t = tile(name)  # flat-colour pictogram, nothing to re-shade
    else:
        o = tile(name)
        full = (o[..., 3] == 255).all()
        a1 = g_regrain(name, wrap=full)
        a2 = g_regrain(name, wrap=full, amount=0.3, retain=0.85)
        cands = [a1, a2, reshade(o, R(name, 3), out=a1, wrap=full), reshade(o, R(name, 3), out=a2, wrap=full),
                 reshade(o, R(name, 3), wrap=full, amount=0.6)]
        ok = [c for c in cands if drift(o, c, full) <= 0.15]
        good = [c for c in ok if changed(o, c) >= 0.35]
        if good:
            t = good[0]
        elif ok:
            t = max(ok, key=lambda c: changed(o, c))
        else:
            t = min(cands, key=lambda c: drift(o, c, full))
    for b in ('dirt', 'netherrack'):
        if name != b and name not in ORES:
            t = substitute(name, t, b)
    DONE[name] = t
    return t


def main():
    anim_layers = set()
    for name, (st, n, _) in ANIM.items():
        anim_layers |= set(range(st, st + n))
        if name in ANIM3D:
            st, fr = g_anim3d(name)
        else:
            st, fr = g_anim_regrain(name)
        for i in range(n):
            put(st + i, fr[i])
    # water: the plain layers are the tinted _bt layers
    for w in ('water_still', 'water_flow'):
        st, n, _ = ANIM[w + '_bt']
        st2 = ANIM[w][0]
        for i in range(n):
            put(st2 + i, tint_img(tile(st + i, OUT), TINT['water']))
    for name, l in sorted(LAY.items(), key=lambda x: x[1]):
        if SKIP(name) or l in anim_layers:
            continue
        put(l, gen(name))
    Image.fromarray(OUT).save(S + '/new/atlas.png', optimize=True)
    # mob skins
    for f in sorted(os.listdir(S + '/orig')):
        if not f.startswith('ent_'):
            continue
        im = np.array(Image.open(S + '/orig/' + f).convert('RGBA'))
        if f.endswith('_eyes.png'):
            Image.fromarray(im).save(S + '/new/' + f)
            continue
        nm = f[4:-4]
        o = redraw_mob(nm, im, R(f)) if nm in BOXES else im
        Image.fromarray(o).save(S + '/new/' + f, optimize=True)


if __name__ == '__main__':
    main()
