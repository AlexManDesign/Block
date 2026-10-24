"""Re-draw pixel textures: keep palette, silhouette and large-scale structure,
re-synthesise all fine detail (grain, specks, ore blobs, leaf holes, liquids)."""
import zlib
import numpy as np

LUMW = np.array([0.299, 0.587, 0.114])


def seed_of(name, salt=0):
    return zlib.crc32(f'{name}#{salt}'.encode()) & 0x7fffffff


def lum(img):
    return img[..., :3].astype(np.float64) @ LUMW


# ---------- filters ----------------------------------------------------------

def gauss_k(sigma):
    r = max(1, int(np.ceil(sigma * 2.5)))
    x = np.arange(-r, r + 1)
    k = np.exp(-x * x / (2 * sigma * sigma))
    return k / k.sum()


def blur(f, sigma, wrap=True, mask=None):
    """Separable gaussian; wrap=True tiles the image, mask -> normalised convolution."""
    if sigma <= 0:
        return f.copy()
    k = gauss_k(sigma)
    r = len(k) // 2

    def conv(a, axis):
        if wrap:
            out = np.zeros_like(a)
            for i, w in enumerate(k):
                out += w * np.roll(a, i - r, axis=axis)
            return out
        pad = [(0, 0)] * a.ndim
        pad[axis] = (r, r)
        ap = np.pad(a, pad)
        out = np.zeros_like(a)
        n = a.shape[axis]
        for i, w in enumerate(k):
            sl = [slice(None)] * a.ndim
            sl[axis] = slice(i, i + n)
            out += w * ap[tuple(sl)]
        return out

    if mask is None:
        return conv(conv(f, 0), 1)
    m = mask.astype(np.float64)
    num = conv(conv(f * m, 0), 1)
    den = conv(conv(m, 0), 1)
    return np.where(den > 1e-6, num / np.maximum(den, 1e-6), f)


def phase_randomize(f, rng, mask=None, smooth=0.0):
    """New field with the same amplitude spectrum as f (same grain size / direction),
    but fresh random phases, i.e. a brand-new arrangement of the detail."""
    g = f - (f[mask].mean() if mask is not None and mask.any() else f.mean())
    if mask is not None:
        g = g * mask
    A = np.abs(np.fft.fftn(g))
    if smooth > 0 and f.ndim == 2:
        # spread the spectrum so periodic lines (seams, outlines) do not turn into stripes
        A = np.sqrt(blur(np.fft.fftshift(A * A), smooth, wrap=False))
        A = np.fft.ifftshift(A)
    noise = rng.standard_normal(f.shape)
    N = np.fft.fftn(noise)
    N /= np.maximum(np.abs(N), 1e-9)
    out = np.real(np.fft.ifftn(A * N))
    s = out.std()
    return out / s if s > 1e-9 else out


def soft_threshold(d, t):
    return np.sign(d) * np.maximum(np.abs(d) - t, 0)


# ---------- colour groups -----------------------------------------------------

def rgb2lab(rgb):
    c = rgb / 255.0
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    M = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = c @ M.T / np.array([0.9505, 1.0, 1.089])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    L = 116 * f[..., 1] - 16
    a = 500 * (f[..., 0] - f[..., 1])
    b = 200 * (f[..., 1] - f[..., 2])
    return np.stack([L, a, b], -1)


def kmeans(X, k, rng, it=25):
    k = min(k, len(X))
    C = X[rng.choice(len(X), k, replace=False)].copy()
    for _ in range(it):
        d = ((X[:, None, :] - C[None]) ** 2).sum(-1)
        lab = d.argmin(1)
        for j in range(k):
            if (lab == j).any():
                C[j] = X[lab == j].mean(0)
    d = ((X[:, None, :] - C[None]) ** 2).sum(-1)
    return d.argmin(1), C


def hue_groups(img, mask, rng, k=4, merge=14.0, minfrac=0.0, merge_L=None):
    """Split the opaque pixels into material groups by chroma (a*, b*) so that e.g.
    red brick and grey mortar, or grey stone and blue diamond, are re-coloured separately."""
    H, W = mask.shape
    g = np.full((H, W), -1, int)
    idx = np.argwhere(mask)
    if len(idx) == 0:
        return g, 0
    lab = rgb2lab(img[mask][:, :3].astype(np.float64))
    X = np.stack([lab[:, 1], lab[:, 2], lab[:, 0] * 0.25], 1)
    nuniq = len(np.unique(X.round(1), axis=0))
    lab_, C = kmeans(X, min(k, nuniq), rng)
    # merge clusters with close chroma (union-find)
    n = len(C)
    par = list(range(n))

    def find(a):
        while par[a] != a:
            par[a] = par[par[a]]
            a = par[a]
        return a
    for i in range(n):
        for j in range(i + 1, n):
            if np.hypot(*(C[i, :2] - C[j, :2])) < merge and (merge_L is None or abs(C[i, 2] - C[j, 2]) * 4 < merge_L):
                par[find(i)] = find(j)
    roots = sorted({find(i) for i in range(n)})
    remap = {r: i for i, r in enumerate(roots)}
    lab_ = np.array([remap[find(v)] for v in lab_])
    g[mask] = lab_
    return g, len(roots)


# ---------- rank transfer -----------------------------------------------------

def rank_transfer(field, src_colors, sel, out):
    """Paint pixels `sel` of `out` with `src_colors` (Nx4, any order) so that the
    brightest source colour lands on the highest field value: palette and its
    proportions are kept, the arrangement is entirely the field's."""
    pos = np.argwhere(sel)
    if len(pos) == 0 or len(src_colors) == 0:
        return
    order_c = np.argsort(src_colors[:, :3].astype(np.float64) @ LUMW, kind='stable')
    cs = src_colors[order_c]
    fv = field[sel]
    order_p = np.argsort(fv, kind='stable')
    n = len(pos)
    pick = np.round(np.linspace(0, len(cs) - 1, n)).astype(int) if len(cs) != n else np.arange(n)
    vals = cs[pick]
    sel_pos = pos[order_p]
    out[sel_pos[:, 0], sel_pos[:, 1]] = vals


# ---------- the generic re-draw ------------------------------------------------

def stochastic(img, rng, sigma=3.0, mix=0.35, groups=True):
    """For natural noisy materials (dirt, sand, stone...): a brand-new noise field with
    the original's spectrum (grain size, streak direction), lightly biased by the very
    coarse light/dark layout, coloured with the original palette."""
    a = img[..., 3]
    mask = a > 0
    L = lum(img)
    base = blur(L, sigma, wrap=True)
    R = phase_randomize(L, rng)
    F = R + mix * (base - base.mean()) / (L.std() + 1e-6)
    out = img.copy()
    if groups:
        g, ng = hue_groups(img, mask, rng, merge=18)
    else:
        g, ng = np.where(mask, 0, -1), 1
    for j in range(ng):
        sel = g == j
        rank_transfer(F, img[sel], sel, out)
    return out


# ---------- shapes ---------------------------------------------------------------

def components(m, conn8=False):
    H, W = m.shape
    lab = np.full((H, W), -1, int)
    comps = []
    for y in range(H):
        for x in range(W):
            if m[y, x] and lab[y, x] < 0:
                st = [(y, x)]
                lab[y, x] = len(comps)
                c = []
                while st:
                    cy, cx = st.pop()
                    c.append((cy, cx))
                    nb = ((1, 0), (-1, 0), (0, 1), (0, -1)) + (((1, 1), (1, -1), (-1, 1), (-1, -1)) if conn8 else ())
                    for dy, dx in nb:
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < H and 0 <= nx < W and m[ny, nx] and lab[ny, nx] < 0:
                            lab[ny, nx] = len(comps)
                            st.append((ny, nx))
                comps.append(c)
    return comps, lab


def regrain(img, rng, radius=2, amount=0.5, keep=1.0, k=6, wrap=True, smooth=1.5,
            sigma=1.0, protect=None, noise=None, retain=0.7, min_t=7.0, max_step=22.0):
    """For drawn, structured tiles (doors, furnaces, chests, plants, mob skins): every
    outline / seam / feature stays where it is, but the shading grain inside each area
    is re-painted with fresh noise, using only colours found nearby in the same area."""
    img = img.copy()
    a = img[..., 3]
    mask = a > 0
    if not mask.any():
        return img
    H, W = mask.shape
    L = lum(img)
    full = mask.all()
    base = blur(L, sigma, wrap=wrap and full, mask=None if full else mask)
    D = L - base
    dm = D[mask]
    mad = np.median(np.abs(dm - np.median(dm))) * 1.4826 + 1e-6
    t = max(keep * mad, min_t)
    strong = (np.abs(D) > t) & mask
    if protect is not None:
        strong |= protect
    weak = np.where(strong | ~mask, 0, D)
    lvl = np.sqrt(blur(weak * weak, 1.5, wrap=wrap and full, mask=None if full else (mask & ~strong)))
    if noise is None:
        noise = phase_randomize(np.where(mask, D, 0), rng, None if full else mask, smooth=smooth)
    F = base + retain * weak + noise * lvl * amount
    g, ng = hue_groups(img, mask, rng, k=k)
    out = img.copy()
    Lr = L
    for y in range(H):
        for x in range(W):
            if not mask[y, x] or strong[y, x]:
                continue
            best = None
            bd = 1e9
            for dy in range(-radius, radius + 1):
                for dx in range(-radius, radius + 1):
                    yy, xx = y + dy, x + dx
                    if wrap and full:
                        yy %= H
                        xx %= W
                    elif not (0 <= yy < H and 0 <= xx < W):
                        continue
                    if not mask[yy, xx] or strong[yy, xx] or g[yy, xx] != g[y, x]:
                        continue
                    if abs(Lr[yy, xx] - Lr[y, x]) > max_step:
                        continue
                    dd = abs(Lr[yy, xx] - F[y, x]) + 0.15 * (abs(dy) + abs(dx)) + rng.random() * 0.5
                    if dd < bd:
                        bd = dd
                        best = (yy, xx)
            if best is not None:
                out[y, x, :3] = img[best[0], best[1], :3]
    out[..., 3] = a
    return out


def reshade(img, rng, out=None, amount=0.75, noise=None, wrap=False, max_step=12.0):
    """Re-shade a small sprite: each pixel moves up or down its material's colour ramp
    following a new light/shadow pattern (outline and silhouette stay)."""
    img = img.copy()
    out = img.copy() if out is None else out.copy()
    mask = img[..., 3] > 0
    if mask.sum() < 4:
        return out
    full = mask.all()
    L = lum(img)
    g, ng = hue_groups(img, mask, rng, k=6)
    if noise is None:
        noise = phase_randomize(np.where(mask, L, 0), rng, None if full else mask, smooth=1.0)
        noise = 0.7 * noise + 0.3 * blur(noise, 1.0, wrap=wrap and full)
        noise /= noise[mask].std() + 1e-9
    H, W = mask.shape
    for j in range(ng):
        sel = g == j
        cols = np.unique(img[sel].reshape(-1, 4), axis=0)
        if len(cols) < 2:
            continue
        order = np.argsort(cols[:, :3].astype(float) @ LUMW)
        cols = cols[order]
        lut = {tuple(c): i for i, c in enumerate(cols)}
        cl = cols[:, :3].astype(float) @ LUMW
        darkest = 0
        for y, x in np.argwhere(sel):
            i = lut[tuple(img[y, x])]
            if i == darkest and len(cols) > 2:
                continue  # outlines stay
            d = noise[y, x] * amount
            ni = int(np.clip(i + (1 if d > 0.55 else -1 if d < -0.55 else 0), 1 if len(cols) > 2 else 0, len(cols) - 1))
            if ni != i and abs(cl[ni] - cl[i]) <= max_step:
                out[y, x] = cols[ni]
    out[..., 3] = img[..., 3]
    return out


def feature_mask(img, mask, rng, g=None, ng=None, small=0.03, size=10):
    """Eyes, nostrils, mouths, buttons: small high-contrast spots and rare colours."""
    L = lum(img)
    base = blur(L, 1.2, wrap=False, mask=mask)
    D = L - base
    dm = D[mask]
    mad = np.median(np.abs(dm - np.median(dm))) * 1.4826 + 1e-6
    hot = (np.abs(D) > max(2.2 * mad, 18)) & mask
    prot = np.zeros_like(mask)
    comps, _ = components(hot, conn8=True)
    for c in comps:
        if len(c) <= size:
            for p in c:
                prot[p] = True
    if g is not None:
        tot = mask.sum()
        for j in range(ng):
            if (g == j).sum() < small * tot:
                prot |= g == j
    return prot
