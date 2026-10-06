'use strict';
// Block models of this game (own designs): boxes and planes in 1/16 block units, built in code.
// MODEL_LIST[i] = elements [from, to, rotation [origin, axis 0 x / 1 y / 2 z, angle, rescale] or 0,
//   faces (+x -x +y -y +z -z) each [texture, u0, v0, u1, v1, uv rotation, cullface (face index or -1), shade] or 0]
// MODEL_VAR[key][meta] = parts [model, x rotation, y rotation]; MODEL_BOX[key][meta] = selection box.
// Models face north (-z); a facing meta (0 south, 1 west, 2 north, 3 east) turns them about y.
const MODEL_LIST = [], MODEL_VAR = {}, MODEL_BOX = {};
(function () {
  // planar uv of a face from the element's coordinates (u along the face, v downward)
  const uvOf = (f, a, b) => {
    switch (f) {
      case 0: return [16 - b[2], 16 - b[1], 16 - a[2], 16 - a[1]];   // +x
      case 1: return [a[2], 16 - b[1], b[2], 16 - a[1]];             // -x
      case 2: return [a[0], a[2], b[0], b[2]];                       // +y
      case 3: return [a[0], 16 - b[2], b[0], 16 - a[2]];             // -y
      case 4: return [a[0], 16 - b[1], b[0], 16 - a[1]];             // +z
      default: return [16 - b[0], 16 - b[1], 16 - a[0], 16 - a[1]];  // -z
    }
  };
  const onEdge = (f, a, b) => (f === 0 && b[0] >= 16) || (f === 1 && a[0] <= 0) || (f === 2 && b[1] >= 16) || (f === 3 && a[1] <= 0) || (f === 4 && b[2] >= 16) || (f === 5 && a[2] <= 0);
  const FACE = { e: 0, w: 1, u: 2, d: 3, s: 4, n: 5 };
  // box: tex is one texture or {e, w, u, d, s, n, side, all}; opt.uv overrides per face; opt.skip faces
  const box = (a, b, tex, opt) => {
    opt = opt || {};
    const faces = [0, 0, 0, 0, 0, 0];
    for (const k in FACE) {
      const f = FACE[k];
      if (opt.skip && opt.skip.includes(k)) continue;
      const t = typeof tex === 'string' ? tex : tex[k] || (f !== 2 && f !== 3 ? tex.side : null) || tex.all;
      if (!t) continue;
      const uv = (opt.uv && opt.uv[k]) || uvOf(f, a, b);
      faces[f] = [t, uv[0], uv[1], uv[2], uv[3], (opt.rot && opt.rot[k]) || 0, onEdge(f, a, b) ? f : -1, 1];
    }
    return [a, b, opt.r || 0, faces];
  };
  // a flat plane at z = 8 seen from both sides, turned by `angle` about y around the block centre
  const plane = (x0, y0, x1, y1, tex, angle, uv) => {
    const u = uv || [x0, 16 - y1, x1, 16 - y0];
    return [[x0, y0, 8], [x1, y1, 8], angle ? [[8, 8, 8], 1, angle, 0] : 0,
      [0, 0, 0, 0, [tex, u[0], u[1], u[2], u[3], 0, -1, 1], [tex, u[2], u[1], u[0], u[3], 0, -1, 1]]];
  };
  const cross = (x0, y0, x1, y1, tex, uv) => [plane(x0, y0, x1, y1, tex, 45, uv), plane(x0, y0, x1, y1, tex, -45, uv)];
  const add = (els) => { MODEL_LIST.push(els); return MODEL_LIST.length - 1; };
  const FACING_Y = [180, 270, 0, 90];
  const facing = (key, model, metas) => { MODEL_VAR[key] = []; for (let m = 0; m < (metas || 4); m++) MODEL_VAR[key][m] = [[model, 0, FACING_Y[m & 3]]]; };
  const single = (key, model) => { MODEL_VAR[key] = [[[model, 0, 0]]]; };

  // barrel: a full block turned toward its facing (meta 0-3 horizontal, 4 up, 5 down; +8 open)
  {
    const shut = add([box([0, 0, 0], [16, 16, 16], { u: 'barrel_top', d: 'barrel_bottom', side: 'barrel_side' })]);
    const open = add([box([0, 0, 0], [16, 16, 16], { u: 'barrel_top_open', d: 'barrel_bottom', side: 'barrel_side' })]);
    MODEL_VAR.BARREL = [];
    for (let m = 0; m < 16; m++) {
      const d = m & 7, M = m & 8 ? open : shut;
      MODEL_VAR.BARREL[m] = [d === 4 || d > 5 ? [M, 0, 0] : d === 5 ? [M, 180, 0] : [M, 90, FACING_Y[d]]];
    }
  }
  // lectern: base, post and a sloped reading board
  facing('LECTERN', add([
    box([0, 0, 0], [16, 2, 16], { u: 'lectern_base', d: 'oak_planks', side: 'lectern_base' }),
    box([4, 2, 4], [12, 13, 12], { side: 'lectern_sides', n: 'lectern_front' }, { skip: ['u', 'd'] }),
    box([0.5, 12, 2], [15.5, 15, 15], { u: 'lectern_top', d: 'oak_planks', side: 'lectern_sides' }, { r: [[8, 13, 8], 0, -22.5, 0] }),
  ]));
  // grindstone: two legs with pivots holding the wheel (meta: facing, +4 wall, +8 ceiling)
  {
    const parts = (y) => [
      box([2, y, 6], [4, y + 7, 10], 'grindstone_pivot'), box([12, y, 6], [14, y + 7, 10], 'grindstone_pivot'),
      box([2, y + 7, 5], [4, y + 13, 11], 'grindstone_pivot'), box([12, y + 7, 5], [14, y + 13, 11], 'grindstone_pivot'),
      box([4, y + 4, 2], [12, y + 16, 14], { side: 'grindstone_round', e: 'grindstone_side', w: 'grindstone_side', u: 'grindstone_round', d: 'grindstone_round' }),
    ];
    const floor = add(parts(0));
    MODEL_VAR.GRINDSTONE = [];
    for (let m = 0; m < 12; m++) {
      const face = m >> 2, y = FACING_Y[m & 3];
      MODEL_VAR.GRINDSTONE[m] = [face === 0 ? [floor, 0, y] : face === 2 ? [floor, 180, (y + 180) % 360] : [floor, 90, y]];
    }
  }
  // stonecutter: a low block with the saw blade standing in the middle
  facing('STONECUTTER', add([
    box([0, 0, 0], [16, 9, 16], { u: 'stonecutter_top', d: 'stonecutter_bottom', side: 'stonecutter_side' }),
    plane(1, 9, 15, 16, 'stonecutter_saw', 0, [1, 0, 15, 7]),
  ]));
  // bell: stone posts and a beam (floor), a short hanger (ceiling) or a wall bracket, and the bell
  {
    const bell = [box([5, 6, 5], [11, 13, 11], 'bell_body'), box([4, 4, 4], [12, 6, 12], 'bell_body')];
    const floor = add([box([0, 0, 6], [2, 16, 10], 'stone'), box([14, 0, 6], [16, 16, 10], 'stone'),
      box([2, 13, 7], [14, 15, 9], 'dark_oak_planks'), ...bell]);
    const ceiling = add([box([7, 13, 7], [9, 16, 9], 'dark_oak_planks'), ...bell]);
    const wall = add([box([7, 13, 3], [9, 15, 16], 'dark_oak_planks'), ...bell]);
    const both = add([box([7, 13, 0], [9, 15, 16], 'dark_oak_planks'), ...bell]);
    MODEL_VAR.BELL = [];
    for (let m = 0; m < 16; m++) {
      const at = m >> 2, y = FACING_Y[m & 3];
      MODEL_VAR.BELL[m] = [[[floor, ceiling, wall, both][at], 0, at === 0 ? (y + 90) % 360 : y]];
    }
  }
  // lantern: iron cage with a cap and a handle; hanging (meta 1) one block higher on a chain
  {
    const L = (y, chain) => {
      const els = [
        box([5, y, 5], [11, y + 7, 11], { side: 'lantern', u: 'lantern', d: 'lantern' }, { uv: { e: [0, 2, 6, 9], w: [0, 2, 6, 9], s: [0, 2, 6, 9], n: [0, 2, 6, 9], u: [0, 9, 6, 15], d: [0, 9, 6, 15] } }),
        box([6, y + 7, 6], [10, y + 9, 10], 'lantern', { uv: { e: [0, 0, 4, 2], w: [0, 0, 4, 2], s: [0, 0, 4, 2], n: [0, 0, 4, 2], u: [0, 9, 4, 13], d: [0, 9, 4, 13] } }),
      ];
      els.push(...cross(6.5, y + 9, 9.5, y + (chain ? 16 - y : 11), 'lantern', [11, 1, 14, 3]));
      return els;
    };
    MODEL_VAR.LANTERN = [[[add(L(0, false)), 0, 0]], [[add(L(1, true)), 0, 0]]];
  }
  // campfire: two logs on the ground, two crossing on top, embers and flames (meta: facing, +4 out)
  {
    const logs = (lit) => [
      box([1, 0, 0], [5, 4, 16], { side: 'campfire_log', u: 'campfire_log' }), box([11, 0, 0], [15, 4, 16], { side: 'campfire_log', u: 'campfire_log' }),
      box([0, 3, 1], [16, 7, 5], { side: lit ? 'campfire_log_lit' : 'campfire_log', u: 'campfire_log' }),
      box([0, 3, 11], [16, 7, 15], { side: lit ? 'campfire_log_lit' : 'campfire_log', u: 'campfire_log' }),
      box([5, 0, 0], [11, 1, 16], lit ? 'campfire_log_lit' : 'campfire_log'),
    ];
    const lit = add([...logs(true), ...cross(1, 1, 15, 16, 'campfire_fire', [0, 0, 16, 15])]);
    const out = add(logs(false));
    MODEL_VAR.CAMPFIRE = [];
    for (let m = 0; m < 8; m++) MODEL_VAR.CAMPFIRE[m] = [[m & 4 ? out : lit, 0, FACING_Y[m & 3]]];
  }
  // brewing stand: rod on three feet with up to three bottles (meta bits)
  {
    const base = add([
      box([7, 0, 7], [9, 14, 9], 'brewing_stand', { uv: { e: [7, 2, 9, 16], w: [7, 2, 9, 16], s: [7, 2, 9, 16], n: [7, 2, 9, 16], u: [7, 2, 9, 4], d: [7, 2, 9, 4] } }),
      box([9, 0, 5], [15, 2, 11], 'brewing_stand_base'), box([1, 0, 1], [7, 2, 7], 'brewing_stand_base'), box([1, 0, 9], [7, 2, 15], 'brewing_stand_base'),
    ]);
    const bottle = (a) => add([plane(8, 0, 16, 16, 'brewing_stand', a, [8, 0, 16, 16])]);
    const b = [bottle(0), bottle(120), bottle(240)];
    MODEL_VAR.BREWING_STAND = [];
    for (let m = 0; m < 8; m++) { const parts = [[base, 0, 0]]; for (let i = 0; i < 3; i++) if (m & (1 << i)) parts.push([b[i], 0, 0]); MODEL_VAR.BREWING_STAND[m] = parts; }
  }
  // cauldron: four walls on corner legs; water at three levels (meta 1-3)
  {
    const S = { side: 'cauldron_side', u: 'cauldron_top', d: 'cauldron_bottom' };
    const shell = [
      box([0, 3, 0], [2, 16, 16], S), box([14, 3, 0], [16, 16, 16], S), box([2, 3, 0], [14, 16, 2], S), box([2, 3, 14], [14, 16, 16], S),
      box([2, 3, 2], [14, 4, 14], { u: 'cauldron_inner', d: 'cauldron_bottom' }, { skip: ['e', 'w', 's', 'n'] }),
      box([0, 0, 0], [4, 3, 2], S), box([0, 0, 2], [2, 3, 4], S), box([12, 0, 0], [16, 3, 2], S), box([14, 0, 2], [16, 3, 4], S),
      box([0, 0, 14], [4, 3, 16], S), box([0, 0, 12], [2, 3, 14], S), box([12, 0, 14], [16, 3, 16], S), box([14, 0, 12], [16, 3, 14], S),
    ];
    MODEL_VAR.CAULDRON = [0, 9, 12, 15].map((h) => [[add(h ? [...shell, box([2, 4, 2], [14, h, 14], { u: 'water_still' }, { skip: ['e', 'w', 's', 'n', 'd'] })] : shell), 0, 0]]);
  }
  // composter: a box of planks, filled by level (meta 0-8)
  {
    const W = { side: 'composter_side', u: 'composter_top', d: 'composter_bottom' };
    const shell = [box([0, 0, 0], [16, 2, 16], W, { skip: ['e', 'w', 's', 'n'] }), box([0, 0, 0], [2, 16, 16], W), box([14, 0, 0], [16, 16, 16], W),
      box([2, 0, 0], [14, 16, 2], W), box([2, 0, 14], [14, 16, 16], W)];
    MODEL_VAR.COMPOSTER = [];
    for (let m = 0; m < 9; m++) {
      const h = m === 0 ? 0 : Math.min(15, 1 + m * 2);
      MODEL_VAR.COMPOSTER[m] = [[add(h ? [...shell, box([2, 2, 2], [14, h, 14], { u: m === 8 ? 'composter_ready' : 'composter_compost' }, { skip: ['e', 'w', 's', 'n', 'd'] })] : shell), 0, 0]];
    }
  }
  // pointed dripstone: crossed planes, a sprite per thickness (meta 0-4) and direction (+8 up)
  {
    const T = ['tip_merge', 'tip', 'frustum', 'middle', 'base'];
    MODEL_VAR.POINTED_DRIPSTONE = [];
    for (let m = 0; m < 16; m++) MODEL_VAR.POINTED_DRIPSTONE[m] = [[add(cross(0, 0, 16, 16, 'pointed_dripstone_' + (m & 8 ? 'up_' : 'down_') + T[Math.min(4, m & 7)])), 0, 0]];
  }
  // azaleas: a leafy cap on a woody stem
  for (const [k, t] of [['AZALEA', 'azalea'], ['FLOWERING_AZALEA', 'flowering_azalea']])
    single(k, add([box([0, 8, 0], [16, 16, 16], { u: t + '_top', side: t + '_side', d: t + '_top' }), ...cross(2, 0, 14, 8, 'azalea_plant', [2, 8, 14, 16])]));
  // dripleaves: a flat leaf on a stem (big), a pair of small crossed leaves (small, meta: facing, +4 upper)
  facing('BIG_DRIPLEAF', add([box([0, 15, 0], [16, 15.5, 16], { u: 'big_dripleaf_top', d: 'big_dripleaf_top', side: 'big_dripleaf_side' }), ...cross(5, 0, 11, 15, 'big_dripleaf_stem')]));
  facing('BIG_DRIPLEAF_STEM', add(cross(5, 0, 11, 16, 'big_dripleaf_stem')));
  {
    const lower = add(cross(2, 0, 14, 16, 'small_dripleaf_stem_bottom')), upper = add([...cross(2, 0, 14, 12, 'small_dripleaf_stem_top'),
      box([2, 11, 2], [14, 11.5, 14], { u: 'small_dripleaf_top', d: 'small_dripleaf_top' }, { skip: ['e', 'w', 's', 'n'] })]);
    MODEL_VAR.SMALL_DRIPLEAF = [];
    for (let m = 0; m < 8; m++) MODEL_VAR.SMALL_DRIPLEAF[m] = [[m & 4 ? upper : lower, 0, FACING_Y[m & 3]]];
  }
  // spore blossom: petals spread just under the ceiling around a green base
  single('SPORE_BLOSSOM', add([box([1, 15, 1], [15, 15.2, 15], { d: 'spore_blossom', u: 'spore_blossom' }, { skip: ['e', 'w', 's', 'n'] }),
    box([5, 13, 5], [11, 16, 11], 'spore_blossom_base')]));
  // sculk sensor: a half slab with four tendrils; shrieker: a slab with a bone jaw on top
  single('SCULK_SENSOR', add([box([0, 0, 0], [16, 8, 16], { u: 'sculk_sensor_top', d: 'sculk_sensor_bottom', side: 'sculk_sensor_side' }),
    plane(3, 8, 7, 16, 'sculk_sensor_tendril_inactive', 45, [4, 0, 12, 16]), plane(9, 8, 13, 16, 'sculk_sensor_tendril_inactive', -45, [4, 0, 12, 16])]));
  single('SCULK_SHRIEKER', add([box([0, 0, 0], [16, 8, 16], { u: 'sculk_shrieker_inner_top', d: 'sculk_shrieker_bottom', side: 'sculk_shrieker_side' }),
    box([1, 8, 1], [15, 15, 15], { u: 'sculk_shrieker_top', side: 'sculk_shrieker_side' }, { skip: ['d'] })]));

  // ship wheel: a turned post with a spoked wheel on its north face, handles past the rim
  {
    const W = 'spruce_planks', P = 'stripped_spruce_log';
    facing('SHIP_WHEEL', add([
      box([6, 0, 8], [10, 11, 12], P),
      box([7, 9, 5], [9, 11, 8], W),
      box([3, 14, 6], [13, 15, 7], W), box([3, 5, 6], [13, 6, 7], W),
      box([3, 6, 6], [4, 14, 7], W), box([12, 6, 6], [13, 14, 7], W),
      box([7.5, 6, 6], [8.5, 14, 7], W), box([4, 9.5, 6], [12, 10.5, 7], W),
      box([7.5, 15, 6], [8.5, 16, 7], P), box([7.5, 4, 6], [8.5, 5, 7], P),
      box([1, 9.5, 6], [3, 10.5, 7], P), box([13, 9.5, 6], [15, 10.5, 7], P),
    ]));
  }

  // cocoa: a ribbed pod hanging from the log on its north side by a short stalk, bigger with age
  // (meta: the log's direction 0-3 | age 0-2 << 2)
  {
    const pods = [[4, 5], [6, 7], [8, 9]].map(([w, h], age) => {
      const t = 'cocoa_stage' + age, x0 = 8 - w / 2, y1 = 12, y0 = y1 - h, z0 = 1;
      return add([
        box([x0, y0, z0], [x0 + w, y1, z0 + w], t),
        box([7.5, y1, 0], [8.5, 15, 2], t, { uv: { e: [0, 0, 2, 3], w: [0, 0, 2, 3], u: [0, 0, 1, 2], d: [0, 0, 1, 2], s: [0, 0, 1, 3], n: [0, 0, 1, 3] } }),
      ]);
    });
    MODEL_VAR.COCOA = [];
    for (let m = 0; m < 12; m++) MODEL_VAR.COCOA[m] = [[pods[Math.min(2, m >> 2)], 0, FACING_Y[m & 3]]];
  }

  // selection boxes: the bounds of each variant's elements, turned like the parts
  const turn = (p, rx, ry) => {
    let [x, y, z] = [p[0] - 8, p[1] - 8, p[2] - 8], t;
    const cx = Math.cos(rx * Math.PI / 180), sx = Math.sin(rx * Math.PI / 180), cy = Math.cos(ry * Math.PI / 180), sy = Math.sin(ry * Math.PI / 180);
    t = y * cx + z * sx; z = -y * sx + z * cx; y = t;
    t = x * cy - z * sy; z = x * sy + z * cy; x = t;
    return [x + 8, y + 8, z + 8];
  };
  for (const k in MODEL_VAR) {
    MODEL_BOX[k] = MODEL_VAR[k].map((parts) => {
      const lo = [16, 16, 16], hi = [0, 0, 0];
      for (const [mi, rx, ry] of parts) for (const E of MODEL_LIST[mi]) for (let c = 0; c < 8; c++) {
        const q = turn([c & 1 ? E[1][0] : E[0][0], c & 2 ? E[1][1] : E[0][1], c & 4 ? E[1][2] : E[0][2]], rx, ry);
        for (let i = 0; i < 3; i++) { lo[i] = Math.min(lo[i], Math.max(0, q[i])); hi[i] = Math.max(hi[i], Math.min(16, q[i])); }
      }
      return [lo[0] / 16, lo[1] / 16, lo[2] / 16, hi[0] / 16, hi[1] / 16, hi[2] / 16].map((v) => Math.round(v * 1e4) / 1e4);
    });
  }
})();
