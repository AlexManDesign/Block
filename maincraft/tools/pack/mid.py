"""A pack between our first pack (packs/classic) and Minecraft's look.

    python3 tools/pack/mid.py CLASSIC_DIR JAVA_TEXTURES_DIR OUT_DIR [--mc SHARE] [--fit] [name ...]

SHARE is how far towards Minecraft (0.5, the default: halfway; 0.1: nine tenths our first pack).

- drawn pictures (planks, doors, items, mobs, made things) keep the classic pack's drawing and
  have their colours moved SHARE of the way to the Java pack's (tools/tone.py at ALPHA SHARE);
- materials (the ones tools/pack/structure.py paints to a measured look) are painted to the look
  SHARE of the way from the classic picture's to the Java one's: the two measures weighed, our
  pattern maker fitted to that (--fit measures again; kept in structure_mid.json, or
  structure_mid_SHARE.json), and its colours weighed the same between the two pictures' tones,
  rank by rank.
Every pixel is our own; nothing of the reference pack is written out.
"""
import json
import os
import sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import core  # noqa: E402
import structure  # noqa: E402
import tone  # noqa: E402
from core import Canvas, Source, lum, N  # noqa: E402

SHARE = 0.5


def store_path():
    return os.path.join(HERE, 'structure_mid.json' if SHARE == 0.5 else 'structure_mid_%g.json' % SHARE)


def opaque(px):
    return [p for p in px if p[3] > 127]


def mid_colours(a_px, b_px, n=64):
    """the two pictures' colours sorted by lightness and weighed rank by rank"""
    a, b = sorted(opaque(a_px), key=lum), sorted(opaque(b_px), key=lum)
    if not a or not b:
        return a_px or b_px
    out = []
    for r in range(n):
        p, q = a[r * len(a) // n], b[r * len(b) // n]
        out.append(tuple(round(p[t] * (1 - SHARE) + q[t] * SHARE) for t in range(3)) + (255,))
    return out


def fit_mid(a_px, b_px, name, kinds):
    target = [x * (1 - SHARE) + y * SHARE for x, y in zip(structure.measure(a_px), structure.measure(b_px))]
    cols = mid_colours(a_px, b_px)
    best = None
    for kind in kinds:
        for p in structure.GRID[kind]:
            score = 0.0
            for seed in (0, 1):
                f = structure.MAKERS[kind](core.rng(name, 'fit', seed), **p)
                score += structure.distance(structure.measure(structure.render(f, cols)), target)
            if best is None or score < best[0]:
                best = (score, kind, p)
    return {'kind': best[1], 'p': best[2], 'score': round(best[0] / 2, 3)}


def main():
    global SHARE
    argv = sys.argv[1:]
    if '--mc' in argv:
        k = argv.index('--mc')
        SHARE = float(argv[k + 1])
        del argv[k:k + 2]
    args = [a for a in argv if not a.startswith('--')]
    refit = '--fit' in argv
    STORE = store_path()
    classic_dir, java_dir, out = args[:3]
    only = set(args[3:])
    java = Source('java', java_dir)
    classic = Source('self', classic_dir)
    os.makedirs(out, exist_ok=True)
    store = {}
    if os.path.exists(STORE):
        with open(STORE) as fh:
            store = json.load(fh)
    measured = structure.load()                 # the materials painted to a look
    tone.ALPHA = SHARE
    made = 0
    for fn in sorted(os.listdir(classic_dir)):
        if not fn.endswith('.png'):
            continue
        n = fn[:-4]
        if only and n not in only:
            continue
        im = Image.open(os.path.join(classic_dir, fn)).convert('RGBA')
        ref = java.image(n)
        a_px, b_px = classic.px(n), java.px(n)
        drawn_closer = False
        if n in measured and b_px and im.size == (N, N):
            kinds = structure.PLAIN.get(n, ('pebbles', 'clouds', 'streaks'))
            if refit or n not in store:
                store[n] = fit_mid(a_px, b_px, n, kinds)
                with open(STORE, 'w') as fh:
                    json.dump(store, fh, indent=1, sort_keys=True)
                print(n, store[n])
            # the first pack's own drawing, toned, when it is nearer the wanted look than our maker
            ma, mb = structure.measure(a_px), structure.measure(b_px)
            target = [x * (1 - SHARE) + y * SHARE for x, y in zip(ma, mb)]
            drawn_closer = structure.distance(ma, target) <= store[n]['score']
        if n in measured and b_px and im.size == (N, N) and not drawn_closer:
            core.KEEP_SCALE = True
            try:
                c = Canvas('java', n, ref=b_px)
                c.part(structure.field(n, store[n]), mid_colours(a_px, b_px))
                new = c.image()
            finally:
                core.KEEP_SCALE = False
        elif ref is not None:
            new = tone.tone(im, ref, n)[0] or im
        else:
            new = im
        new.save(os.path.join(out, fn))
        made += 1
    print('painted', made)


if __name__ == '__main__':
    main()
