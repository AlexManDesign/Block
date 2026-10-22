"""Doors and trapdoors drawn from our own plans: each kind of wood (and iron, copper) has its own build,
written below as a 16x16 plan, so a door is known for what it is (an oak door's four panes, an
acacia door's tall slits, a spruce door's solid boards with rails across).

Plan letters: F frame (a bevelled rim), p boards standing up (seams between them), h boards lying
across, r a raised rail (lighter, across), l a raised panel, d a groove, b a bar or slat standing up,
m metal (a handle, a rivet), x a stile running up (darker), . an opening (see-through).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from core import N, at, fbm, white, rims, rng, Canvas, lum  # noqa: E402

DOOR = {
    'oak': ([
        'FFFFFFFFFFFFFFFF',
        'FpppppppppppppppF'[:16],
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'FpppppppppppppppF'[:16],
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'FpppppppppppppppF'[:16],
        'FrrrrrrrrrrrrrrF',
        'FpppppppppppppppF'[:16],
        'FpppppppppppppppF'[:16],
        'FpppppppppppppppF'[:16]], [
        'FpppppppppppppppF'[:16],
        'FpppppppppppppppF'[:16],
        'FlllllllllllllpF'.replace('lllllllllllllp', 'pllllllllllllp')[:16],
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpppppppppppppppF'[:16],
        'FrrrrrrrrrrrrrrF',
        'FpppppppppppppppF'[:16],
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpppppppppppppppF'[:16],
        'FpppppppppppppppF'[:16],
        'FFFFFFFFFFFFFFFF']),
    'spruce': ([
        'FFFFFFFFFFFFFFFF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF'], [
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppmpF',
        'FrrrrrrrrrrrrmrF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FppppdppppdppppF',
        'FFFFFFFFFFFFFFFF']),
    'birch': ([
        'FFFFFFFFFFFFFFFF',
        'FppppppppppppppF',
        'Fpp...b..b...ppF',
        'Fpp...b..b...ppF',
        'Fpp...b..b...ppF',
        'FppbbbbbbbbbbppF',
        'Fpp...b..b...ppF',
        'Fpp...b..b...ppF',
        'Fpp...b..b...ppF',
        'FppbbbbbbbbbbppF',
        'Fpp...b..b...ppF',
        'Fpp...b..b...ppF',
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppppppppppppF',
        'FppppppppppppppF'], [
        'FppppppppppppppF',
        'FppppppppppppmpF',
        'FplllllpplllllpF',
        'FplllllpplllllpF',
        'FplllllpplllllpF',
        'FplllllpplllllpF',
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppppppppppppF',
        'FplllllpplllllpF',
        'FplllllpplllllpF',
        'FplllllpplllllpF',
        'FplllllpplllllpF',
        'FppppppppppppppF',
        'FppppppppppppppF',
        'FFFFFFFFFFFFFFFF']),
    'jungle': ([
        'FFFFFFFFFFFFFFFF',
        'FppppppppppppppF',
        'Fp..b..b..b..ppF'[:16],
        'Fp..b..b..b..pF'.replace('pF', 'ppF'),
        'FpbbbbbbbbbbbbpF',
        'Fp..b..b..b..ppF'[:16],
        'Fp..b..b..b..ppF'[:16],
        'FpbbbbbbbbbbbbpF',
        'Fp..b..b..b..ppF'[:16],
        'Fp..b..b..b..ppF'[:16],
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FppppppppppppppF',
        'FppppppppppppppF'], [
        'FppppppppppppppF',
        'FppppppppppppmpF',
        'FpldlldlldlldlpF',
        'FplldlldlldllpF'.replace('pF', 'lpF'),
        'FplldlldlldlllpF'[:16],
        'FpdlldlldlldlppF',
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppppppppppppF',
        'FpldlldlldlldlpF',
        'FplldlldlldllpF'.replace('pF', 'lpF'),
        'FplldlldlldlllpF'[:16],
        'FpdlldlldlldlppF',
        'FppppppppppppppF',
        'FppppppppppppppF',
        'FFFFFFFFFFFFFFFF']),
    'acacia': ([
        'FFFFFFFFFFFFFFFF',
        'FbbbbbbbbbbbbbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'FbbbbbbbbbbbbbbF',
        'FrrrrrrrrrrrrrrF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF'], [
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbmbF',
        'FrrrrrrrrrrrrrrF',
        'FbbbbbbbbbbbbbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'Fbbb.bbbbbb.bbbF',
        'FbbbbbbbbbbbbbbF',
        'FrrrrrrrrrrrrrrF',
        'FbbbbbbbbbbbbbbF',
        'FFFFFFFFFFFFFFFF']),
    'dark_oak': ([
        'FFFFFFFFFFFFFFFF',
        'FppppppppppppppF',
        'Fppp...xx...pppF',
        'Fppp...xx...pppF',
        'Fppp...xx...pppF',
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FrrrrrrrrrrrrrrF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FrrrrrrrrrrrrrrF',
        'FppppppppppppppF'], [
        'FppppppppppppppF',
        'FphhhhhhhhhhhmpF',
        'FphhhhhhhhhhhhpF',
        'FrrrrrrrrrrrrrrF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FrrrrrrrrrrrrrrF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FrrrrrrrrrrrrrrF',
        'FphhhhhhhhhhhhpF',
        'FphhhhhhhhhhhhpF',
        'FppppppppppppppF',
        'FFFFFFFFFFFFFFFF']),
    'mangrove': ([
        'FFFFFFFFFFFFFFFF',
        'FhhhhhhhhhhhhhhF',
        'Fhh..........hhF',
        'FhhhhhhhhhhhhhhF',
        'Fhh..........hhF',
        'FhhhhhhhhhhhhhhF',
        'Fhh..........hhF',
        'FhhhhhhhhhhhhhhF',
        'FrrrrrrrrrrrrrrF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FrrrrrrrrrrrrrrF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF'], [
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhmhF',
        'FrrrrrrrrrrrrrrF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FrrrrrrrrrrrrrrF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FrrrrrrrrrrrrrrF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FFFFFFFFFFFFFFFF']),
    'cherry': ([
        'FFFFFFFFFFFFFFFF',
        'FppppppppppppppF',
        'Fpppp......ppppF',
        'Fppp........pppF',
        'Fpp....bb....ppF',
        'Fpp....bb....ppF',
        'FppbbbbbbbbbbppF',
        'Fpp....bb....ppF',
        'Fpp....bb....ppF',
        'Fpp....bb....ppF',
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppppppppppppF',
        'FplllllllllllpF'.replace('pF', 'lpF'),
        'FplllllllllllllF'[:15] + 'F',
        'FppppppppppppppF'], [
        'FppppppppppppppF',
        'FppppppppppppmpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppppppppppppF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FppppppppppppppF',
        'FppppppppppppppF',
        'FFFFFFFFFFFFFFFF']),
    'crimson': ([
        'FFFFFFFFFFFFFFFF',
        'FxxxxxxxxxxxxxxF',
        'Fxx.b.b.b.b.bxxF',
        'Fxx.b.b.b.b.bxxF',
        'Fxx.b.b.b.b.bxxF',
        'Fxx.b.b.b.b.bxxF',
        'Fxx.b.b.b.b.bxxF',
        'FxxxxxxxxxxxxxxF',
        'FrrrrrrrrrrrrrrF',
        'FpdppdppdppdppdF'[:15] + 'F',
        'FpdppdppdppdppF'.replace('pF', 'ppF'),
        'FpdppdppdppdppF'.replace('pF', 'ppF'),
        'FpdppdppdppdppF'.replace('pF', 'ppF'),
        'FpdppdppdppdppF'.replace('pF', 'ppF'),
        'FpdppdppdppdppF'.replace('pF', 'ppF'),
        'FpdppdppdppdppF'.replace('pF', 'ppF')], [
        'FpdppdppdppdppF'.replace('pF', 'ppF'),
        'FpdppdppdppdpmF'.replace('mF', 'mpF'),
        'FpdppdppdppdppF'.replace('pF', 'ppF'),
        'FrrrrrrrrrrrrrrF',
        'FxxxxxxxxxxxxxxF',
        'FxllllllllllllxF',
        'FxlddddddddddlxF',
        'FxlllllllllllllF'[:15] + 'F',
        'FxlddddddddddlxF',
        'FxllllllllllllxF',
        'FxlddddddddddlxF',
        'FxllllllllllllxF',
        'FxxxxxxxxxxxxxxF',
        'FrrrrrrrrrrrrrrF',
        'FpdppdppdppdppF'.replace('pF', 'ppF'),
        'FFFFFFFFFFFFFFFF']),
    'bamboo': ([
        'FFFFFFFFFFFFFFFF',
        'FbbbbbbbbbbbbbbF',
        'Fbbb........bbbF',
        'Fbbb........bbbF',
        'Fbbb........bbbF',
        'Fbbb........bbbF',
        'Fbbb........bbbF',
        'Fbbb........bbbF',
        'Fbbb........bbbF',
        'Fbbb........bbbF',
        'FbbbbbbbbbbbbbbF',
        'FrrrrrrrrrrrrrrF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF'], [
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbmbF',
        'FbbbbbbbbbbbbbbF',
        'FrrrrrrrrrrrrrrF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF',
        'FrrrrrrrrrrrrrrF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF',
        'FbbbbbbbbbbbbbbF',
        'FrrrrrrrrrrrrrrF',
        'FbbbbbbbbbbbbbbF',
        'FFFFFFFFFFFFFFFF']),
    'pale_oak': ([
        'FFFFFFFFFFFFFFFF',
        'FppppppppppppppF',
        'Fpp...pppp...ppF',
        'Fpp...pppp...ppF',
        'Fpp...pppp...ppF',
        'Fpp...pppp...ppF',
        'Fpp...pppp...ppF',
        'Fpp...pppp...ppF',
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppppppppppppF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FppppppppppppppF',
        'FppppppppppppppF'], [
        'FppppppppppppppF',
        'FppppppppppppmpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FppppppppppppppF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FpllllllllllllpF',
        'FppppppppppppppF',
        'FppppppppppppppF',
        'FFFFFFFFFFFFFFFF']),
    'iron': ([
        'FFFFFFFFFFFFFFFF',
        'FlllllllllllllllF'[:16],
        'Flm....ll....mlF',
        'Fll....ll....llF',
        'Fll....ll....llF',
        'Fll....ll....llF',
        'Flm....ll....mlF',
        'FllllllllllllllF',
        'FddddddddddddddF',
        'FllllllllllllllF',
        'FlmllllllllllmlF',
        'FllllllllllllllF',
        'FddddddddddddddF',
        'FllllllllllllllF',
        'FllllllllllllllF',
        'FllllllllllllllF'], [
        'FllllllllllllllF',
        'FlmllllllllllmlF',
        'FllllllllllllllF',
        'FddddddddddddddF',
        'FllllllllllllllF',
        'FllllllllllllllF',
        'FlmllllllllllmlF',
        'FllllllllllllllF',
        'FddddddddddddddF',
        'FllllllllllllllF',
        'FllllllllllllllF',
        'FlmllllllllllmlF',
        'FllllllllllllllF',
        'FddddddddddddddF',
        'FllllllllllllllF',
        'FFFFFFFFFFFFFFFF']),
    'copper': ([
        'FFFFFFFFFFFFFFFF',
        'FllllllllllllllF',
        'Fll..llllll..llF',
        'Fl....llll....lF',
        'Fl....llll....lF',
        'Fll..llllll..llF',
        'FllllllllllllllF',
        'FmllllllllllllmF',
        'FddddddddddddddF',
        'FllllllllllllllF',
        'FldllllllllldllF'[:16],
        'FlldllllllldlllF'[:16],
        'FllldllllldllllF'[:16],
        'FlllldllldlllllF'[:16],
        'FllllllllllllllF',
        'FllllllllllllllF'], [
        'FllllllllllllllF',
        'FllllllllllllmlF',
        'FddddddddddddddF',
        'FldllllllllllldF'[:15] + 'F',
        'FlldllllllllldlF'[:15] + 'F',
        'FllldllllllldllF',
        'FlllldllllldlllF',
        'FllllldllldllllF',
        'FlllllldldlllllF',
        'FllllllldllllllF',
        'FllllllllllllllF',
        'FddddddddddddddF',
        'FmllllllllllllmF',
        'FllllllllllllllF',
        'FllllllllllllllF',
        'FFFFFFFFFFFFFFFF']),
}
DOOR['warped'] = ([r.replace('b', 'x').replace('.x.', '.x.') for r in DOOR['crimson'][0][:2]] + [
    'Fxx..........xxF',
    'Fxx.xxx..xxx.xxF',
    'Fxx.x......x.xxF',
    'Fxx.x......x.xxF',
    'Fxx.xxx..xxx.xxF',
    'Fxx..........xxF'] + DOOR['crimson'][0][8:], DOOR['crimson'][1])

TRAP = {
    'oak': [
        'FFFFFFFFFFFFFFFF',
        'FppppppppppppppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'FppppppppppppppF',
        'FrrrrrrrrrrrrrrF',
        'FrrrrrrrrrrrrrrF',
        'FppppppppppppppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'Fpp....pp....ppF',
        'FppppppppppppppF',
        'FFFFFFFFFFFFFFFF'],
    'spruce': [
        'FFFFFFFFFFFFFFFF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FddddddddddddddF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FddddddddddddddF',
        'FrrrrrrrrrrrrrrF',
        'FrrrrrrrrrrrrrrF',
        'FddddddddddddddF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FddddddddddddddF',
        'FhhhhhhhhhhhhhhF',
        'FhhhhhhhhhhhhhhF',
        'FFFFFFFFFFFFFFFF'],
    'birch': [
        'FFFFFFFFFFFFFFFF',
        'FppppppppppppppF',
        'Fp...b..b..b..pF'[:15] + 'F',
        'Fp...b..b..b..pF'[:15] + 'F',
        'FpbbbbbbbbbbbbpF',
        'Fp...b..b..b..pF'[:15] + 'F',
        'Fp...b..b..b..pF'[:15] + 'F',
        'FrrrrrrrrrrrrrrF',
        'FrrrrrrrrrrrrrrF',
        'Fp...b..b..b..pF'[:15] + 'F',
        'Fp...b..b..b..pF'[:15] + 'F',
        'FpbbbbbbbbbbbbpF',
        'Fp...b..b..b..pF'[:15] + 'F',
        'Fp...b..b..b..pF'[:15] + 'F',
        'FppppppppppppppF',
        'FFFFFFFFFFFFFFFF'],
    'acacia': [
        'FFFFFFFFFFFFFFFF',
        'FbbbbbbbbbbbbbbF',
        'Fbb.bb.bb.bb.bbF',
        'Fbb.bb.bb.bb.bbF',
        'Fbb.bb.bb.bb.bbF',
        'Fbb.bb.bb.bb.bbF',
        'Fbb.bb.bb.bb.bbF',
        'FrrrrrrrrrrrrrrF',
        'FrrrrrrrrrrrrrrF',
        'Fbb.bb.bb.bb.bbF',
        'Fbb.bb.bb.bb.bbF',
        'Fbb.bb.bb.bb.bbF',
        'Fbb.bb.bb.bb.bbF',
        'Fbb.bb.bb.bb.bbF',
        'FbbbbbbbbbbbbbbF',
        'FFFFFFFFFFFFFFFF'],
    'iron': [
        'FFFFFFFFFFFFFFFF',
        'FmllllllllllllmF',
        'Fll.ll.ll.ll.llF',
        'Fll.ll.ll.ll.llF',
        'FllllllllllllllF',
        'Fll.ll.ll.ll.llF',
        'Fll.ll.ll.ll.llF',
        'FddddddddddddddF',
        'FllllllllllllllF',
        'Fll.ll.ll.ll.llF',
        'Fll.ll.ll.ll.llF',
        'FllllllllllllllF',
        'Fll.ll.ll.ll.llF',
        'Fll.ll.ll.ll.llF',
        'FmllllllllllllmF',
        'FFFFFFFFFFFFFFFF'],
}
for _k, _src in (('jungle', 'birch'), ('dark_oak', 'spruce'), ('mangrove', 'spruce'), ('cherry', 'oak'),
                 ('pale_oak', 'oak'), ('crimson', 'acacia'), ('warped', 'birch'), ('bamboo', 'acacia'),
                 ('copper', 'iron')):
    TRAP.setdefault(_k, TRAP[_src])



# dark oak: a dark door of tall raised panels, two small panes high in it
DOOR['dark_oak'] = ([
    'FFFFFFFFFFFFFFFF',
    'FppppppppppppppF',
    'Fpp...pppp...ppF',
    'Fpp...pppp...ppF',
    'FppppppppppppppF'] + ['FplllllpplllllpF'] * 8 + [
    'FppppppppppppppF',
    'FrrrrrrrrrrrrrrF',
    'FppppppppppppppF'], [
    'FppppppppppppppF',
    'FppppppppppppmpF'] + ['FplllllpplllllpF'] * 11 + [
    'FppppppppppppppF',
    'FrrrrrrrrrrrrrrF',
    'FFFFFFFFFFFFFFFF'])
# crimson: boards standing up, grooved between, a row of slits high up
DOOR['crimson'] = ([
    'FFFFFFFFFFFFFFFF',
    'FppppppppppppppF'] + ['Fpp.pp.pp.pp.ppF'] * 4 + [
    'FrrrrrrrrrrrrrrF'] + ['FpppdpppdpppdppF'] * 9, ['FpppdpppdpppdppF'] * 6 + [
    'FrrrrrrrrrrrrrrF',
    'FpppdpppdpppdmpF'] + ['FpppdpppdpppdppF'] * 6 + [
    'FrrrrrrrrrrrrrrF',
    'FFFFFFFFFFFFFFFF'])
# warped: boards between dark stiles, two ragged panes over a rail
DOOR['warped'] = ([
    'FFFFFFFFFFFFFFFF',
    'FxppxppxxppxppxF',
    'Fxpp..pxxp..ppxF',
    'Fxp...pxxp...pxF',
    'Fxpp..pxxpp..pxF',
    'FxppxppxxppxppxF',
    'FxppxppxxppxppxF',
    'FrrrrrrrrrrrrrrF'] + ['FxppxppxxppxppxF'] * 8, [
    'FxppxppxxppxppxF',
    'FxppxppxxppxpmxF',
    'FxppxppxxppxppxF',
    'FrrrrrrrrrrrrrrF'] + ['FxppxppxxppxppxF'] * 6 + [
    'FrrrrrrrrrrrrrrF'] + ['FxppxppxxppxppxF'] * 4 + [
    'FFFFFFFFFFFFFFFF'])
# crimson trapdoor: boards lying across, a slit near each edge
TRAP['crimson'] = ['FFFFFFFFFFFFFFFF'] + ['FhhhhhhhhhhhhhhF'] * 2 + ['Fhh..........hhF'] + ['FhhhhhhhhhhhhhhF'] * 3 + \
    ['FrrrrrrrrrrrrrrF'] * 2 + ['FhhhhhhhhhhhhhhF'] * 3 + ['Fhh..........hhF'] + ['FhhhhhhhhhhhhhhF'] * 2 + ['FFFFFFFFFFFFFFFF']
# warped trapdoor: four ragged panes between dark stiles
TRAP['warped'] = [
    'FFFFFFFFFFFFFFFF',
    'FxppxppxxppxppxF',
    'Fxp..pxxxp..ppxF',
    'Fx...pxxp...ppxF',
    'Fxp..ppxpp..pxxF',
    'FxppxppxxppxppxF',
    'FrrrrrrrrrrrrrrF',
    'FxppxppxxppxppxF',
    'Fxpp..pxxp..ppxF',
    'Fxp...pxxp...pxF',
    'Fxpp..pxxpp..pxF',
    'FxppxppxxppxppxF',
    'FrrrrrrrrrrrrrrF',
    'FxppxppxxppxppxF',
    'FxppxppxxppxppxF',
    'FFFFFFFFFFFFFFFF']
for _pl in [TRAP['crimson'], TRAP['warped']] + [h for k in ('dark_oak', 'crimson', 'warped') for h in DOOR[k]]:
    assert len(_pl) == 16 and all(len(r) == 16 for r in _pl)


# jungle: an arched pane high in the door, a narrow slit under it, carved boards below
DOOR['jungle'] = ([
    'FFFFFFFFFFFFFFFF',
    'FppppppppppppppF',
    'Fpppp......ppppF',
    'Fppp........pppF',
    'Fppp........pppF',
    'Fppp........pppF',
    'Fppp........pppF',
    'FppppppppppppppF',
    'FrrrrrrrrrrrrrrF',
    'FpppppppppppppppF'[:16],
    'Fpppppp..pppppF'[:14] + 'pF',
    'Fpppppp..ppppppF',
    'Fpppppp..ppppppF',
    'Fpppppp..ppppppF',
    'FppppppppppppppF',
    'FppppppppppppppF'], [
    'FppppppppppppppF',
    'FppppppppppppmpF',
    'FplllllpplllllpF',
    'FpldddlppldddlpF',
    'FpldddlppldddlpF',
    'FplllllpplllllpF',
    'FppppppppppppppF',
    'FrrrrrrrrrrrrrrF',
    'FppppppppppppppF',
    'FplllllpplllllpF',
    'FpldddlppldddlpF',
    'FpldddlppldddlpF',
    'FplllllpplllllpF',
    'FppppppppppppppF',
    'FrrrrrrrrrrrrrrF',
    'FFFFFFFFFFFFFFFF'])
# mangrove: boards standing up, a square pane high up, rails across
DOOR['mangrove'] = ([
    'FFFFFFFFFFFFFFFF',
    'FpppdpppdpppdppF',
    'Fppp......pppppF'[:16],
    'Fppp......ppdppF',
    'Fppp......ppdppF',
    'Fppp......ppdppF',
    'FpppdpppdpppdppF',
    'FrrrrrrrrrrrrrrF'] + ['FpppdpppdpppdppF'] * 8, ['FpppdpppdpppdppF'] * 2 + [
    'FpppdpppdpppdmpF',
    'FrrrrrrrrrrrrrrF'] + ['FpppdpppdpppdppF'] * 7 + [
    'FrrrrrrrrrrrrrrF'] + ['FpppdpppdpppdppF'] * 3 + [
    'FFFFFFFFFFFFFFFF'])
for _pl in DOOR['jungle'] + DOOR['mangrove']:
    assert len(_pl) == 16 and all(len(r) == 16 for r in _pl), [len(r) for r in _pl]


def fix(rows):
    rows = [(r + 'F' * 16)[:16] if len(r) < 16 else r[:16] for r in rows]
    return [r[:15] + 'F' if r[15] != 'F' else r for r in rows]


def paint_plan(ctx, name, plan, src, dark_src=None):
    """the plan painted in our way: each kind of part with its own field, by rank of src's colours"""
    rows = fix(plan)
    cell = {at(x, y): rows[y][x] for y in range(N) for x in range(N)}
    rnd = rng(name, 'plan')
    w = white(rnd)
    up = fbm(rnd, [(2, 16, 0.6), (1, 8, 0.4)])          # grain running up, in long streaks
    across = fbm(rnd, [(16, 2, 0.6), (8, 1, 0.4)])      # grain running across
    frame = [1 if cell[i] == 'F' else 0 for i in range(N * N)]
    fr = rims(frame, 0.3, 0.3, wrap=False)
    f = [0.0] * (N * N)
    for i, ch in cell.items():
        x, y = i % N, i // N
        if ch == 'F':
            v = 0.5 + fr[i] + 0.05 * w[i]
        elif ch == 'p':
            v = 0.45 + 0.3 * up[i] + 0.04 * w[i] - (0.25 if x in (5, 10) else 0)
        elif ch == 'h':
            v = 0.45 + 0.3 * across[i] + 0.04 * w[i] - (0.25 if y % 3 == 2 else 0)
        elif ch == 'b':
            v = 0.5 + 0.3 * up[i] + 0.04 * w[i] + (0.12 if x % 3 == 1 else -0.08)
        elif ch == 'r':
            v = 0.7 + 0.15 * across[i] + 0.04 * w[i] + (0.1 if cell.get(at(x, y - 1)) != 'r' else -0.1)
        elif ch == 'l':
            edge_lit = cell.get(at(x, y - 1)) not in ('l',) or cell.get(at(x - 1, y)) not in ('l',)
            edge_dark = cell.get(at(x, y + 1)) not in ('l',) or cell.get(at(x + 1, y)) not in ('l',)
            v = 0.6 + 0.1 * up[i] + 0.04 * w[i] + (0.2 if edge_lit else -0.15 if edge_dark else 0)
        elif ch == 'x':
            v = 0.3 + 0.2 * up[i] + 0.04 * w[i]
        elif ch == 'd':
            v = 0.15 + 0.04 * w[i]
        elif ch == 'm':
            v = 0.0
        else:
            v = 0.0
        # a dark line round every opening, as its frame
        if ch != '.' and any(cell.get(at(x + dx, y + dy)) == '.' for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
                             if 0 <= x + dx < N and 0 <= y + dy < N):
            v -= 0.25
        f[i] = round(v / 0.09)      # a few even tones, as a drawn door has
    c = Canvas(ctx.style, name, ref=ctx.ref(name))
    solid = [cell[i] != '.' for i in range(N * N)]
    c.part(f, src, solid, flat=True)
    return c.image()


def door(name, kind, top):
    def gen(ctx):
        src = ctx.px(kind + '_planks', 'oak_planks')     # the wood's, not the door's (no glass in it)
        if kind in ('iron', 'copper'):
            src = ctx.px(name, kind + '_block' if kind == 'iron' else 'copper_block', 'iron_block')
        plan = DOOR.get(kind, DOOR['oak'])[0 if top else 1]
        return paint_plan(ctx, name, plan, [p for p in src if p[3] > 127] or src)
    return gen


def trapdoor(name, kind):
    def gen(ctx):
        src = ctx.px(kind + '_planks', 'oak_planks')     # the wood's, not the door's (no glass in it)
        if kind in ('iron', 'copper'):
            src = ctx.px(name, kind + '_block' if kind == 'iron' else 'copper_block', 'iron_block')
        return paint_plan(ctx, name, TRAP.get(kind, TRAP['oak']), [p for p in src if p[3] > 127] or src)
    return gen


def register(reg):
    for w in ('oak', 'spruce', 'birch', 'jungle', 'acacia', 'dark_oak', 'mangrove', 'cherry', 'pale_oak',
              'crimson', 'warped', 'bamboo', 'iron', 'copper'):
        reg[w + '_door_top'] = door(w + '_door_top', w, True)
        reg[w + '_door_bottom'] = door(w + '_door_bottom', w, False)
        reg[w + '_trapdoor'] = trapdoor(w + '_trapdoor', w)
