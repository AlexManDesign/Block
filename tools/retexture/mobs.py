"""Mob skins re-drawn face by face, using the models' box UV layout."""
import numpy as np
from retex import *

# (w, h, d, u, v, is_face_part)  -- MC box UV: top/bottom strip above, then right|front|left|back
def humanoid(arm, wide=False, hat=True):
    b = [(8, 8, 8, 0, 0, True), (8, 12, 4, 16, 16, False), (arm, 12, arm, 40, 16, False), (arm, 12, arm, 0, 16, False)]
    if wide:
        b += [(4, 12, 4, 32, 48, False), (4, 12, 4, 16, 48, False)]
    if hat:
        b.append((8, 8, 8, 32, 0, True))
    return b

def opart(b, uv):
    return (b[0], b[1], b[2], uv[0], uv[1], False)

BOXES = {
    'pig': [(8, 8, 8, 0, 0, True), (4, 3, 1, 16, 16, True), (10, 16, 8, 28, 8, False), (4, 6, 4, 0, 16, False)],
    'cow': [(8, 8, 6, 0, 0, True), (1, 3, 1, 22, 0, True), (12, 18, 10, 18, 4, False), (4, 6, 1, 52, 0, True),
            (4, 12, 4, 0, 16, False)],
    'sheep': [(6, 6, 8, 0, 0, True), (8, 16, 6, 28, 8, False), (4, 12, 4, 0, 16, False)],
    'sheep_fur': [(6, 6, 6, 0, 0, False), (8, 16, 6, 28, 8, False), (4, 6, 4, 0, 16, False)],
    'chicken': [(4, 6, 3, 0, 0, True), (4, 2, 2, 14, 0, True), (2, 2, 2, 14, 4, True), (6, 8, 6, 0, 9, False),
                (3, 5, 3, 26, 0, True), (1, 4, 6, 24, 13, False)],
    'zombie': humanoid(4), 'drowned': humanoid(4), 'skeleton': humanoid(2, hat=False),
    'player': humanoid(4, wide=True),
    'creeper': [(8, 8, 8, 0, 0, True), (8, 12, 4, 16, 16, False), (4, 6, 4, 0, 16, False)],
    'spider': [(8, 8, 8, 32, 4, True), (6, 6, 6, 0, 0, False), (10, 8, 12, 0, 12, False), (16, 2, 2, 18, 0, False)],
    'cave_spider': [(8, 8, 8, 32, 4, True), (6, 6, 6, 0, 0, False), (10, 8, 12, 0, 12, False), (16, 2, 2, 18, 0, False)],
    'enderman': [(8, 8, 8, 0, 0, True), (8, 8, 8, 0, 16, True), (8, 12, 4, 32, 16, False), (2, 30, 2, 56, 0, False)],
    'slime': [(6, 6, 6, 0, 16, True), (2, 2, 2, 32, 0, True), (2, 2, 2, 32, 4, True), (1, 1, 1, 32, 8, True),
              (8, 8, 8, 0, 0, False)],
    'salmon': [opart([2, 4, 3], [22, 0]), (3, 5, 8, 0, 0, True), opart([3, 5, 8], [0, 13]), opart([0, 5, 6], [20, 10])],
    'shark': [(8, 7, 13, 0, 0, False), (7, 6, 6, 30, 20, True), (4, 3, 4, 0, 36, True), opart([1, 5, 5], [16, 36]),
              opart([5, 1, 4], [42, 36]), opart([5, 1, 4], [42, 41]), opart([4, 5, 11], [0, 20]), opart([1, 10, 6], [28, 36])],
}


def faces(w, h, d, u, v):
    """name -> (x, y, w, h) of the six faces in the texture."""
    return {'top': (u + d, v, w, d), 'bottom': (u + d + w, v, w, d), 'right': (u, v + d, d, h),
            'front': (u + d, v + d, w, h), 'left': (u + d + w, v + d, d, h), 'back': (u + 2 * d + w, v + d, w, h)}


def redraw_mob(name, img, rng, amt=0.9):
    img = img.copy()
    H, W = img.shape[:2]
    mask = img[..., 3] > 0
    L = lum(img)
    g, ng = hue_groups(img, mask, rng, k=8, merge=12, merge_L=30)
    prot = feature_mask(img, mask, rng, g, ng, small=0.02, size=14)
    out = img.copy()
    rects = []
    for (w, h, d, u, v, feat) in BOXES[name]:
        for fn, (x, y, fw, fh) in faces(w, h, d, u, v).items():
            if fw <= 0 or fh <= 0 or x >= W or y >= H:
                continue
            rects.append((x, y, min(fw, W - x), min(fh, H - y), feat and fn == 'front'))
    seen = np.zeros((H, W), bool)
    for (x, y, fw, fh, featface) in rects:
        R = np.zeros((H, W), bool)
        R[y:y + fh, x:x + fw] = True
        R &= mask & ~seen
        seen |= R
        if R.sum() < 4:
            continue
        if featface:
            # the face: features stay, the rest gets a light new grain
            sub = regrain(img, rng, wrap=False, amount=0.6, retain=0.6, protect=prot | ~R)
            out[R] = sub[R]
            continue
        sub = regrain(img, rng, wrap=False, amount=amt, retain=0.45, protect=prot | ~R, max_step=28, k=8)
        out[R] = sub[R]
    out[..., 3] = img[..., 3]
    return out
