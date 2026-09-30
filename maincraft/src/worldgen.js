'use strict';
// World generator (worker side). Minecraft 1.18-style terrain: continentalness / erosion /
// weirdness drive a height field, a 3D density adds overhangs, then surface rules per biome,
// noise caves, ores, trees and vegetation. Everything is deterministic per seed and chunk.

// Biomes: the Minecraft overworld set. Variants point at a "base" biome whose surface / tree /
// vegetation rules they share (e.g. deep_cold_ocean -> cold_ocean).
const BIOME_LIST = [
  ['OCEAN', 'Ocean', 'Океан'], ['DEEP_OCEAN', 'Deep Ocean', 'Глубокий океан'],
  ['FROZEN_OCEAN', 'Frozen Ocean', 'Замёрзший океан'], ['DEEP_FROZEN_OCEAN', 'Deep Frozen Ocean', 'Глубокий замёрзший океан'],
  ['COLD_OCEAN', 'Cold Ocean', 'Холодный океан'], ['DEEP_COLD_OCEAN', 'Deep Cold Ocean', 'Глубокий холодный океан'],
  ['LUKEWARM_OCEAN', 'Lukewarm Ocean', 'Тёплый океан'], ['DEEP_LUKEWARM_OCEAN', 'Deep Lukewarm Ocean', 'Глубокий тёплый океан'],
  ['WARM_OCEAN', 'Warm Ocean', 'Тропический океан'],
  ['RIVER', 'River', 'Река'], ['FROZEN_RIVER', 'Frozen River', 'Замёрзшая река'],
  ['BEACH', 'Beach', 'Пляж'], ['SNOWY_BEACH', 'Snowy Beach', 'Заснеженный пляж'],
  ['STONY_SHORE', 'Stony Shore', 'Каменистый берег'], ['PLAINS', 'Plains', 'Равнина'],
  ['SUNFLOWER_PLAINS', 'Sunflower Plains', 'Подсолнечная равнина'], ['SNOWY_PLAINS', 'Snowy Plains', 'Заснеженная равнина'],
  ['ICE_SPIKES', 'Ice Spikes', 'Ледяные пики'], ['DESERT', 'Desert', 'Пустыня'], ['SAVANNA', 'Savanna', 'Саванна'],
  ['SAVANNA_PLATEAU', 'Savanna Plateau', 'Плато саванны'], ['WINDSWEPT_SAVANNA', 'Windswept Savanna', 'Выветренная саванна'],
  ['FOREST', 'Forest', 'Лес'], ['FLOWER_FOREST', 'Flower Forest', 'Цветочный лес'],
  ['BIRCH_FOREST', 'Birch Forest', 'Березняк'], ['OLD_GROWTH_BIRCH_FOREST', 'Old Growth Birch Forest', 'Старовозрастный березняк'],
  ['DARK_FOREST', 'Dark Forest', 'Тёмный лес'], ['TAIGA', 'Taiga', 'Тайга'],
  ['OLD_GROWTH_PINE_TAIGA', 'Old Growth Pine Taiga', 'Старовозрастная сосновая тайга'],
  ['OLD_GROWTH_SPRUCE_TAIGA', 'Old Growth Spruce Taiga', 'Старовозрастная еловая тайга'],
  ['SNOWY_TAIGA', 'Snowy Taiga', 'Заснеженная тайга'], ['JUNGLE', 'Jungle', 'Джунгли'],
  ['SPARSE_JUNGLE', 'Sparse Jungle', 'Редкие джунгли'], ['BAMBOO_JUNGLE', 'Bamboo Jungle', 'Бамбуковые джунгли'],
  ['SWAMP', 'Swamp', 'Болото'], ['MANGROVE_SWAMP', 'Mangrove Swamp', 'Мангровое болото'],
  ['BADLANDS', 'Badlands', 'Пустоши'], ['ERODED_BADLANDS', 'Eroded Badlands', 'Выветренные пустоши'],
  ['WOODED_BADLANDS', 'Wooded Badlands', 'Лесистые пустоши'],
  ['MEADOW', 'Meadow', 'Луг'], ['CHERRY_GROVE', 'Cherry Grove', 'Вишнёвая роща'], ['GROVE', 'Grove', 'Роща'],
  ['SNOWY_SLOPES', 'Snowy Slopes', 'Заснеженные склоны'], ['JAGGED_PEAKS', 'Jagged Peaks', 'Зубчатые пики'],
  ['FROZEN_PEAKS', 'Frozen Peaks', 'Заледеневшие пики'], ['STONY_PEAKS', 'Stony Peaks', 'Скалистые пики'],
  ['WINDSWEPT_HILLS', 'Windswept Hills', 'Выветренные холмы'], ['WINDSWEPT_GRAVELLY_HILLS', 'Windswept Gravelly Hills', 'Выветренные гравийные холмы'],
  ['WINDSWEPT_FOREST', 'Windswept Forest', 'Выветренный лес'], ['MUSHROOM_FIELDS', 'Mushroom Fields', 'Грибные поля'],
];
const BI = {};
BIOME_LIST.forEach((b, i) => { BI[b[0]] = i; });
// back-compat alias used by feature code
BI.OLD_GROWTH_TAIGA = BI.OLD_GROWTH_SPRUCE_TAIGA;

// per-biome properties: surface top / filler / underwater floor and flags (trees: TREE_TABLE)
const BPROP = [];
(function () {
  const G = B.GRASS, D = B.DIRT, S = B.SAND;
  const def = (k, o) => { BPROP[BI[k]] = Object.assign({ top: G, fill: D, sub: 0, floor: D, snow: 0, cold: 0, base: BI[k] }, o); };
  const like = (k, src, o) => { BPROP[BI[k]] = Object.assign({}, BPROP[BI[src]], { base: BI[src] }, o || {}); };
  def('OCEAN', { top: S, fill: S, floor: -1, ocean: 1 });
  def('FROZEN_OCEAN', { top: B.GRAVEL, fill: B.GRAVEL, floor: B.GRAVEL, cold: 2, ocean: 1 });
  def('COLD_OCEAN', { top: B.GRAVEL, fill: B.GRAVEL, floor: B.GRAVEL, cold: 1, ocean: 1 });
  def('LUKEWARM_OCEAN', { top: S, fill: S, floor: S, ocean: 1 }); def('WARM_OCEAN', { top: S, fill: S, floor: S, ocean: 1 });
  like('DEEP_OCEAN', 'OCEAN', { floor: B.GRAVEL }); like('DEEP_FROZEN_OCEAN', 'FROZEN_OCEAN'); like('DEEP_COLD_OCEAN', 'COLD_OCEAN');
  like('DEEP_LUKEWARM_OCEAN', 'LUKEWARM_OCEAN');
  def('RIVER', { top: S, fill: S, floor: -2, ocean: 1 }); def('FROZEN_RIVER', { top: S, fill: S, floor: -2, cold: 2, ocean: 1 });
  def('BEACH', { top: S, fill: S, sub: B.SANDSTONE, floor: S }); def('SNOWY_BEACH', { top: B.SNOW, fill: S, sub: B.SANDSTONE, floor: S, snow: 1, cold: 2 });
  def('STONY_SHORE', { top: B.STONE, fill: B.STONE, floor: B.GRAVEL });
  def('PLAINS', {});
  def('SUNFLOWER_PLAINS', {});
  def('SNOWY_PLAINS', { top: B.SNOW, snow: 1, cold: 2 });
  def('ICE_SPIKES', { top: B.SNOW, fill: B.SNOW, snow: 1, cold: 2 });
  def('DESERT', { top: S, fill: S, sub: B.SANDSTONE, floor: S });
  def('SAVANNA', {});
  like('SAVANNA_PLATEAU', 'SAVANNA', {}); like('WINDSWEPT_SAVANNA', 'SAVANNA', { mountain: 1 });
  def('FOREST', {});
  def('FLOWER_FOREST', {});
  def('BIRCH_FOREST', {});
  like('OLD_GROWTH_BIRCH_FOREST', 'BIRCH_FOREST', {});
  def('DARK_FOREST', {});
  def('TAIGA', { top: G });
  def('OLD_GROWTH_SPRUCE_TAIGA', {});
  like('OLD_GROWTH_PINE_TAIGA', 'OLD_GROWTH_SPRUCE_TAIGA', {});
  def('SNOWY_TAIGA', { top: B.SNOW, snow: 1, cold: 2 });
  def('JUNGLE', {});
  def('SPARSE_JUNGLE', {});
  def('BAMBOO_JUNGLE', {});
  def('SWAMP', { floor: -3 });
  def('MANGROVE_SWAMP', { top: B.MUD, fill: B.MUD, floor: B.MUD });
  def('BADLANDS', { top: B.RED_SAND, fill: B.TERRACOTTA, floor: B.RED_SAND, badlands: 1 });
  like('ERODED_BADLANDS', 'BADLANDS');
  def('WOODED_BADLANDS', { top: B.RED_SAND, fill: B.TERRACOTTA, floor: B.RED_SAND, badlands: 1 });
  def('MEADOW', {});
  def('CHERRY_GROVE', {});
  def('GROVE', { top: B.SNOW, fill: B.DIRT, snow: 1, cold: 2 });
  def('SNOWY_SLOPES', { top: B.SNOW, fill: B.SNOW, snow: 1, cold: 2, mountain: 1 });
  def('JAGGED_PEAKS', { top: B.SNOW, fill: B.SNOW, snow: 1, cold: 2, mountain: 1 });
  def('FROZEN_PEAKS', { top: B.SNOW, fill: B.PACKED_ICE, snow: 1, cold: 2, mountain: 1 });
  def('STONY_PEAKS', { top: B.STONE, fill: B.STONE, mountain: 1 });
  def('WINDSWEPT_HILLS', { mountain: 1 });
  like('WINDSWEPT_GRAVELLY_HILLS', 'WINDSWEPT_HILLS', { top: B.GRAVEL, fill: B.GRAVEL });
  like('WINDSWEPT_FOREST', 'WINDSWEPT_HILLS', {});
  def('MUSHROOM_FIELDS', { top: B.MYCELIUM, fill: D });
})();

// Trees per biome as in maincraft: [grid cell, [[cumulative chance, type], ...]]. Variant biomes use
// the table of the biome the original maps them to.
const TREE_TABLE = {};
const TREE_BASE = [];
(function () {
  const T = (k, cell, types) => { TREE_TABLE[BI[k]] = [cell, types]; };
  T('FOREST', 6, [[0.5, 'oak'], [0.75, 'birch'], [0.87, 'fancy_oak']]);
  T('BIRCH_FOREST', 6, [[0.78, 'birch'], [0.9, 'oak']]);
  T('FLOWER_FOREST', 9, [[0.45, 'oak'], [0.7, 'birch']]);
  T('DARK_FOREST', 6, [[0.62, 'dark_oak'], [0.82, 'oak'], [0.9, 'birch']]);
  T('JUNGLE', 5, [[0.72, 'jungle'], [0.85, 'oak']]);
  T('SPARSE_JUNGLE', 11, [[0.5, 'jungle'], [0.65, 'oak']]);
  T('TAIGA', 6, [[0.85, 'spruce']]);
  T('OLD_GROWTH_SPRUCE_TAIGA', 6, [[0.55, 'tall_spruce'], [0.85, 'spruce']]);
  T('SNOWY_TAIGA', 8, [[0.7, 'spruce']]);
  T('SNOWY_PLAINS', 18, [[0.4, 'spruce']]);
  T('PLAINS', 18, [[0.3, 'oak'], [0.42, 'fancy_oak']]);
  T('SAVANNA', 13, [[0.55, 'acacia']]);
  T('MEADOW', 26, [[0.25, 'oak']]);
  T('GROVE', 7, [[0.75, 'spruce']]);
  T('SWAMP', 9, [[0.55, 'oak']]);
  T('WINDSWEPT_HILLS', 15, [[0.25, 'oak'], [0.4, 'spruce']]);
  T('CHERRY_GROVE', 9, [[0.4, 'cherry']]);
  T('BAMBOO_JUNGLE', 6, [[0.5, 'jungle'], [0.65, 'oak']]);
  T('WOODED_BADLANDS', 10, [[0.35, 'oak']]);
  T('MANGROVE_SWAMP', 7, [[0.45, 'oak'], [0.65, 'dark_oak']]);
  const alias = { SUNFLOWER_PLAINS: 'PLAINS', OLD_GROWTH_BIRCH_FOREST: 'BIRCH_FOREST', OLD_GROWTH_PINE_TAIGA: 'OLD_GROWTH_SPRUCE_TAIGA',
    SAVANNA_PLATEAU: 'SAVANNA', WINDSWEPT_SAVANNA: 'SAVANNA', WINDSWEPT_GRAVELLY_HILLS: 'WINDSWEPT_HILLS', WINDSWEPT_FOREST: 'WINDSWEPT_HILLS',
    ERODED_BADLANDS: 'BADLANDS' };
  BIOME_LIST.forEach((b, i) => { TREE_BASE[i] = alias[b[0]] ? BI[alias[b[0]]] : BPROP[i].base; });
})();

// ---------------------------------------------------------------- multi-noise biome lookup
// Nearest parameter point (sum of squared distances to each parameter interval), searched with a
// small bounding-box tree like Minecraft's Climate.RTree.
const MC_TO_BIOME = {};
const BIOME_TREE = (function () {
  if (typeof MC_BIOME_POINTS === 'undefined') return null;
  const bin = atob(MC_BIOME_POINTS), u8 = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) u8[i] = bin.charCodeAt(i);
  const a = new Int16Array(u8.buffer);
  const N = a.length / 11, D = 5;
  const leaves = [];
  for (let i = 0; i < N; i++) {
    const box = new Float64Array(D * 2);
    for (let d = 0; d < D * 2; d++) box[d] = a[i * 11 + 1 + d] / 10000;
    const name = MC_BIOME_NAMES[a[i * 11]].toUpperCase();
    leaves.push({ box, biome: BI[name] !== undefined ? BI[name] : BI.PLAINS, kids: null });
  }
  const center = (n, d) => (n.box[d * 2] + n.box[d * 2 + 1]) / 2;
  const bound = (kids) => {
    const box = new Float64Array(D * 2);
    for (let d = 0; d < D; d++) { box[d * 2] = Infinity; box[d * 2 + 1] = -Infinity; }
    for (const k of kids) for (let d = 0; d < D; d++) { if (k.box[d * 2] < box[d * 2]) box[d * 2] = k.box[d * 2]; if (k.box[d * 2 + 1] > box[d * 2 + 1]) box[d * 2 + 1] = k.box[d * 2 + 1]; }
    return { box, biome: -1, kids };
  };
  const cost = (b) => { let s = 0; for (let d = 0; d < D; d++) s += b[d * 2 + 1] - b[d * 2]; return s; };
  const bucket = (nodes) => {
    const size = Math.pow(10, Math.floor(Math.log(nodes.length - 0.01) / Math.LN10));
    const out = [];
    for (let i = 0; i < nodes.length; i += size) out.push(bound(nodes.slice(i, i + size)));
    return out;
  };
  const build = (nodes) => {
    if (nodes.length === 1) return nodes[0];
    if (nodes.length <= 10) return bound(nodes.slice().sort((p, q) => {
      let sp = 0, sq = 0; for (let d = 0; d < D; d++) { sp += Math.abs(center(p, d)); sq += Math.abs(center(q, d)); } return sp - sq;
    }));
    let best = Infinity, bestD = 0;
    for (let d = 0; d < D; d++) {
      const sorted = nodes.slice().sort((p, q) => center(p, d) - center(q, d));
      let c = 0; for (const b of bucket(sorted)) c += cost(b.box);
      if (c < best) { best = c; bestD = d; }
    }
    const sorted = nodes.slice().sort((p, q) => center(p, bestD) - center(q, bestD));
    const groups = bucket(sorted).sort((p, q) => Math.abs(center(p, bestD)) - Math.abs(center(q, bestD)));
    return bound(groups.map(g => build(g.kids)));
  };
  return build(leaves);
})();
function biomeDist(box, q) {
  let s = 0;
  for (let d = 0; d < 5; d++) {
    const v = q[d], lo = box[d * 2], hi = box[d * 2 + 1];
    const e = v < lo ? lo - v : v > hi ? v - hi : 0;
    s += e * e;
  }
  return s;
}
let _biomeLast = null;
function biomeSearch(node, q, best, bestD) {
  if (!node.kids) return node;
  for (const k of node.kids) {
    const dk = biomeDist(k.box, q);
    if (dk >= bestD) continue;
    const r = k.kids ? biomeSearch(k, q, best, bestD) : k;
    const dr = r === k && !k.kids ? dk : biomeDist(r.box, q);
    if (dr < bestD) { bestD = dr; best = r; if (dr === 0) return best; }
  }
  return best;
}
function lookupBiome(q) {
  if (!BIOME_TREE) return BI.PLAINS;
  let best = _biomeLast, bestD = best ? biomeDist(best.box, q) : Infinity;
  if (bestD === 0) return best.biome;
  best = biomeSearch(BIOME_TREE, q, best, bestD);
  _biomeLast = best;
  return best.biome;
}

const TERRA_BANDS = (function () {
  const k = ['TERRACOTTA', 'ORANGE_TERRACOTTA', 'TERRACOTTA', 'YELLOW_TERRACOTTA', 'BROWN_TERRACOTTA', 'TERRACOTTA',
    'RED_TERRACOTTA', 'TERRACOTTA', 'WHITE_TERRACOTTA', 'LIGHT_GRAY_TERRACOTTA', 'TERRACOTTA', 'ORANGE_TERRACOTTA',
    'TERRACOTTA', 'TERRACOTTA', 'BROWN_TERRACOTTA', 'RED_TERRACOTTA', 'ORANGE_TERRACOTTA', 'TERRACOTTA',
    'YELLOW_TERRACOTTA', 'TERRACOTTA', 'WHITE_TERRACOTTA', 'TERRACOTTA'];
  return k.map(n => B[n] || B[n.replace('ORANGE_TERRACOTTA', 'TERRA_ORANGE').replace('WHITE_TERRACOTTA', 'TERRA_WHITE')] || B.TERRACOTTA);
})();

function smooth01(t) { return t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t); }
function sstep(a, b, x) { return smooth01((x - a) / (b - a)); }
function spline(pts, x) {
  if (x <= pts[0]) return pts[1];
  for (let i = 2; i < pts.length; i += 2) {
    if (x <= pts[i]) { const t = (x - pts[i - 2]) / (pts[i] - pts[i - 2]); return pts[i - 1] + (pts[i + 1] - pts[i - 1]) * t; }
  }
  return pts[pts.length - 1];
}
// terrain splines (continentalness -> base height, erosion -> mountain amplitude / density slope)
const SPL_BASE = [-1, 5, -0.6, 12, -0.42, 20, -0.3, 30, -0.2, 40, -0.1, 48, 0, 55, 0.12, 61, 0.24, 64, 0.4, 71, 0.6, 84, 1, 104];
const SPL_MOUNT = [-1, 1, -0.58, 0.88, -0.35, 0.62, -0.18, 0.36, 0.05, 0.16, 0.4, 0.06, 1, 0];
const SPL_FACTOR = [-1, 1.3, -0.5, 2.2, -0.2, 3.6, 0.05, 5, 0.45, 6.5, 1, 7.5];
const SPL_CONT_PARAM = [-1, -1, -0.325, -0.8, -0.11, -0.19, -0.077, -0.13, -0.04, -0.06, 0.05, 0.05, 0.37, 0.25, 0.8, 0.7, 1.2, 1];
const clamp1 = v => v < -1 ? -1 : v > 1 ? 1 : v;
const clamp01 = v => v < 0 ? 0 : v > 1 ? 1 : v;

const CI = (x, y, z) => ((y - WORLD_MIN_Y) << 8) | (z << 4) | x;
const COL_N = WORLD_H * 256;

class WorldGen {
  constructor(seed) {
    this.seed = seed | 0;
    const S = k => new Simplex((this.seed ^ Math.imul(k, 0x5bd1e995)) >>> 0);
    this.nCont = S(1); this.nEro = S(2); this.nWeird = S(3); this.nTemp = S(4); this.nHum = S(5);
    this.nHill = S(6); this.nDen = S(7); this.nCheese = S(8); this.nSp1 = S(9); this.nSp2 = S(10);
    this.nNd1 = S(11); this.nNd2 = S(12); this.nSurf = S(13); this.nRiver = S(14); this.nCave = S(15);
    this.nMisc = S(16); this.nOre = S(17); this.nMush = S(18); this.nWarp = S(19);
    this.ids = new Uint16Array(COL_N);
    this.meta = new Uint8Array(COL_N);
    this.clim = { h: 0, c: 0, e: 0, w: 0, pv: 0, t: 0, hu: 0, m: 0, river: 0, mush: 0, ti: 0, hi: 0 };
    this.H = new Float32Array(18 * 18);       // analytic heights incl. 1-block margin
    this.BIO = new Uint8Array(256);
    this.TOP = new Int16Array(256);            // top solid after surface
    this.dcache = new Map();
    this.bioCache = new Map();
    this.colCache = new Map();
    this.qgrid = { x0: 1e9, z0: 1e9, n: 12, j: new Float32Array(288), b: new Int16Array(144) };
  }

  // ------------------------------------------------------------ climate / height
  // fractal noise with Perlin-like amplitude (simplex output is scaled down to match)
  fbm(n, x, z, scale, oct) { return n.f2(x * scale, z * scale, oct) * 0.72; }
  // terrain part of the climate: height + density parameters for column x,z
  terrain(x, z, o) {
    const c = this.fbm(this.nCont, x, z, 1 / 2200, 4) * 2.3 + 0.24;
    const e = this.fbm(this.nEro, x, z, 1 / 1300, 4) * 2.1;
    const w = this.fbm(this.nWeird, x, z, 1 / 850, 5) * 2.2;
    const pv = 1 - Math.abs(3 * Math.abs(w) - 2);
    let h = spline(SPL_BASE, c);
    const s = spline(SPL_MOUNT, e) * clamp01((c + 0.25) / 0.4);
    const wk = Math.pow(clamp01(pv * 0.5 + 0.5), 1.4);
    h += Math.pow(s, 1.15) * wk * 235;
    if (h > SEA - 3) h += this.fbm(this.nHill, x, z, 1 / 22, 3) * (9 + s * 15) * clamp01((h - (SEA - 2)) / 11);
    else if (h < SEA - 4) h += this.fbm(this.nMisc, x, z, 0.018, 2) * 2.2 * (2.5 + clamp01((SEA - 4 - h) / 22) * 8);
    // valleys and rivers where weirdness crosses zero (peaks-and-valleys < -0.35)
    if (c > -0.2) {
      const bank = smooth01((-0.05 - pv) / 0.3);
      if (bank > 0 && h > SEA + 4) h += (SEA + 4 - h) * bank * 0.85;
      if (pv < -0.35 && h > SEA) {
        const t = smooth01((-0.35 - pv) / 0.35);
        if (h > SEA + 3) h += (SEA + 3 - h) * t * 0.9;
        const r = smooth01((-0.83 - pv) / 0.17);
        if (r > 0 && h > SEA - 2) h += (SEA - 2 - h) * r;
      }
    }
    // mushroom islands far out in the ocean
    let mush = 0;
    if (c < -0.85) {
      const m = this.nMush.n2(x * 0.0011, z * 0.0011) * 0.5 + 0.5;
      if (m > 0.8) { mush = smooth01((m - 0.8) / 0.12) * smooth01((-0.85 - c) / 0.12); const k = SEA - 14 + mush * 26 + this.fbm(this.nHill, x, z, 0.02, 2) * 3; if (k > h) h = k; }
    }
    if (h < WORLD_MIN_Y + 4) h = WORLD_MIN_Y + 4; if (h > WORLD_MAX_Y - 4) h = WORLD_MAX_Y - 4;
    o.h = h; o.c = c; o.e = e; o.w = w; o.pv = pv; o.s = s; o.mush = mush;
    o.factor = spline(SPL_FACTOR, e);
    return o;
  }
  // full climate incl. temperature / humidity (domain warped) for biome selection
  climate(x, z, o) {
    this.terrain(x, z, o);
    const wx = x + (this.nWarp.n2(x * 0.008, z * 0.008) * 0.5) * 16, wz = z + (this.nWarp.n2(x * 0.008 + 300, z * 0.008 - 300) * 0.5) * 16;
    o.t = clamp1(this.fbm(this.nTemp, wx, wz, 1 / 1600, 3) * 2.4);
    o.hu = clamp1(this.fbm(this.nHum, wx, wz, 1 / 1500, 3) * 2.1);
    o.ti = o.t < -0.45 ? 0 : o.t < -0.15 ? 1 : o.t < 0.2 ? 2 : o.t < 0.55 ? 3 : 4;
    return o;
  }

  biomeOf(o, x, z) {
    const h = Math.round(o.h), ti = o.ti;
    if (o.mush > 0.25 && h > SEA) return BI.MUSHROOM_FIELDS;
    if (h <= SEA) {
      if (o.c > -0.11 && h > SEA - 5 && Math.abs(o.w) < 0.065) return ti === 0 ? BI.FROZEN_RIVER : BI.RIVER;
      const deep = o.c < -0.455;
      return [deep ? BI.DEEP_FROZEN_OCEAN : BI.FROZEN_OCEAN, deep ? BI.DEEP_COLD_OCEAN : BI.COLD_OCEAN,
        deep ? BI.DEEP_OCEAN : BI.OCEAN, deep ? BI.DEEP_LUKEWARM_OCEAN : BI.LUKEWARM_OCEAN, BI.WARM_OCEAN][ti];
    }
    if (h <= SEA + 2 && o.pv > -0.35) {
      if (ti === 0) return BI.SNOWY_BEACH;
      if (ti === 4) return BI.DESERT;
      if (o.e < -0.18) return BI.STONY_SHORE;
      if (this.nMisc.n2(x * 0.011 + 401.7, z * 0.011 - 233.1) * 0.5 + 0.5 > 0.35) return BI.BEACH;
    }
    let sw = clamp1(o.w * 3.5);
    if (sw > -0.12 && sw < 0.12) sw = sw < 0 ? -0.12 : 0.12;
    const q = this._q || (this._q = new Float64Array(5));
    q[0] = o.t; q[1] = clamp1(o.hu * 0.82); q[2] = spline(SPL_CONT_PARAM, o.c < 0.05 ? 0.05 : o.c);
    q[3] = clamp1(o.e * 1.5 + (o.t > 0.5 ? 0.42 : 0)); q[4] = sw;
    return lookupBiome(q);
  }
  // biome at quart resolution (4x4 columns, like Minecraft's biome storage)
  biomeAt(x, z) {
    const qx = x >> 2, qz = z >> 2, k = qx * 131072 + qz;
    let b = this.bioCache.get(k);
    if (b === undefined) {
      const o = this.climate(qx * 4 + 2, qz * 4 + 2, this._bo || (this._bo = {}));
      b = this.biomeOf(o, qx * 4 + 2, qz * 4 + 2);
      if (this.bioCache.size > 80000) this.bioCache.clear();
      this.bioCache.set(k, b);
    }
    return b;
  }

  // Per-block biome with Minecraft's "fuzzy zoom": every quart cell gets a seeded jittered centre
  // (up to ±1.8 blocks) and each block takes the biome of the nearest centre. Borders become ragged
  // instead of following the 4x4 grid in long straight lines.
  biomeAtBlock(x, z) {
    const i = x - 2, k = z - 2, qx = i >> 2, qz = k >> 2, fx = (i & 3) / 4, fz = (k & 3) / 4;
    const G = this.qgrid;
    let best = 0, bd = Infinity;
    for (let c = 0; c < 4; c++) {
      const a = c & 1, b = c >> 1, cx = qx + a, cz = qz + b;
      let jx, jz;
      const gx = cx - G.x0, gz = cz - G.z0;
      if (gx >= 0 && gz >= 0 && gx < G.n && gz < G.n) { const o = (gz * G.n + gx) * 2; jx = G.j[o]; jz = G.j[o + 1]; }
      else { jx = (hash2(this.seed + 61, cx, cz) - 0.5) * 0.9; jz = (hash2(this.seed + 62, cx, cz) - 0.5) * 0.9; }
      const dx = fx - a - jx, dz = fz - b - jz, d = dx * dx + dz * dz;
      if (d < bd) { bd = d; best = c; }
    }
    const cx = qx + (best & 1), cz = qz + (best >> 1), gx = cx - G.x0, gz = cz - G.z0;
    if (gx >= 0 && gz >= 0 && gx < G.n && gz < G.n) {
      const o = gz * G.n + gx;
      let b = G.b[o];
      if (b < 0) b = G.b[o] = this.biomeAt(cx * 4 + 2, cz * 4 + 2);
      return b;
    }
    return this.biomeAt(cx * 4 + 2, cz * 4 + 2);
  }
  // per-chunk cache of the quart cells around the chunk (jitter + biome) used by biomeAtBlock
  prepQuartGrid(x0, z0) {
    const G = this.qgrid, qx0 = ((x0 - 12) >> 2) - 1, qz0 = ((z0 - 12) >> 2) - 1;
    if (G.x0 === qx0 && G.z0 === qz0) return;
    G.x0 = qx0; G.z0 = qz0;
    for (let gz = 0; gz < G.n; gz++) for (let gx = 0; gx < G.n; gx++) {
      const o = gz * G.n + gx, cx = qx0 + gx, cz = qz0 + gz;
      G.j[o * 2] = (hash2(this.seed + 61, cx, cz) - 0.5) * 0.9; G.j[o * 2 + 1] = (hash2(this.seed + 62, cx, cz) - 0.5) * 0.9;
      G.b[o] = -1;
    }
  }

  // cached terrain for grid corners & tree candidates (keyed by x,z)
  colInfo(x, z) {
    const k = x * 131072 + z;
    let v = this.colCache.get(k);
    if (v) return v;
    const o = this.terrain(x, z, {});
    const near = Math.abs(o.h - SEA);
    v = { h: o.h, factor: o.factor, amp: (0.3 + o.s * 0.9) * (near < 9 ? 0.35 + 0.65 * near / 9 : 1), biome: 0 };
    v.biome = this.biomeAtBlock(x, z);
    if (this.colCache.size > 60000) this.colCache.clear();
    this.colCache.set(k, v);
    return v;
  }

  // density > 0 is solid. The 3D noise fades out above the surface so nothing floats in the air.
  density(x, y, z, ci) {
    const t = (ci.h - y) / 24 * ci.factor;
    let amp = ci.amp;
    if (y > ci.h - 4) amp *= clamp01((ci.h + 6 - y) / 10);
    if (t > amp || t < -amp) return t;
    return t + amp * this.nDen.f3(x * 0.016, y * 0.011, z * 0.016, 3) * 0.9;
  }

  // surface height (top solid) computed purely from density, identical to what chunk fill produces
  surfaceAt(x, z) {
    const gx = Math.floor(x / 4) * 4, gz = Math.floor(z / 4) * 4;
    const fx = (x - gx) / 4, fz = (z - gz) / 4;
    const c00 = this.colInfo(gx, gz), c10 = this.colInfo(gx + 4, gz), c01 = this.colInfo(gx, gz + 4), c11 = this.colInfo(gx + 4, gz + 4);
    const top = Math.max(c00.h, c10.h, c01.h, c11.h) + 8;
    let j = Math.min(47, Math.floor((top - WORLD_MIN_Y) / 8));
    const dAt = (jj) => {
      const y = WORLD_MIN_Y + jj * 8;
      const a = this.density(gx, y, gz, c00), b = this.density(gx + 4, y, gz, c10);
      const c = this.density(gx, y, gz + 4, c01), d = this.density(gx + 4, y, gz + 4, c11);
      const ab = a + (b - a) * fx, cd = c + (d - c) * fx;
      return ab + (cd - ab) * fz;
    };
    let dHi = dAt(j + 1);
    for (; j >= 0; j--) {
      const dLo = dAt(j);
      if (dLo > 0 || dHi > 0) {
        for (let k = 7; k >= 0; k--) {
          const v = dLo + (dHi - dLo) * (k / 8);
          if (v > 0) return WORLD_MIN_Y + j * 8 + k;
        }
      }
      dHi = dLo;
    }
    return WORLD_MIN_Y;
  }

  // ------------------------------------------------------------ main entry
  generate(cx, cz) {
    const ids = this.ids, meta = this.meta;
    ids.fill(0); meta.fill(0);
    const x0 = cx * 16, z0 = cz * 16;
    this.cx = cx; this.cz = cz; this.x0 = x0; this.z0 = z0;
    const o = this.clim, H = this.H, BIO = this.BIO;
    // heights with margin and biomes
    for (let dz = -1; dz <= 16; dz++) for (let dx = -1; dx <= 16; dx++) {
      this.terrain(x0 + dx, z0 + dz, o);
      H[(dz + 1) * 18 + dx + 1] = o.h;
    }
    this.prepQuartGrid(x0, z0);
    for (let dz = 0; dz < 16; dz++) for (let dx = 0; dx < 16; dx++) BIO[dz * 16 + dx] = this.biomeAtBlock(x0 + dx, z0 + dz);
    this.fillTerrain();
    this.surface();
    this.caves();
    this.ores();
    this.bedrock();
    this.features();
    this.caveDecor();
    // split into sections
    const secs = new Array(SECTIONS);
    let topSec = -1;
    for (let s = SECTIONS - 1; s >= 0; s--) {
      const off = s * 4096;
      let any = false;
      for (let i = 0; i < 4096; i++) if (ids[off + i] !== 0) { any = true; break; }
      if (any) { topSec = s; break; }
    }
    for (let s = 0; s < SECTIONS; s++) {
      if (s > topSec) { secs[s] = null; continue; }
      const off = s * 4096;
      secs[s] = { ids: ids.slice(off, off + 4096), meta: meta.slice(off, off + 4096) };
    }
    const bio = new Uint8Array(BIO);
    return { sections: secs, biomes: bio };
  }

  fillTerrain() {
    const ids = this.ids, x0 = this.x0, z0 = this.z0;
    // grid 5x5 corners x 49 rows
    const cinfo = new Array(25);
    let hmax = -1e9;
    for (let k = 0; k < 5; k++) for (let i = 0; i < 5; i++) {
      const ci = this.colInfo(x0 + i * 4, z0 + k * 4);
      cinfo[k * 5 + i] = ci;
      const t = ci.h + 8;
      if (t > hmax) hmax = t;
    }
    // rows to evaluate: up to the terrain top, but always to sea level so oceans are filled with water
    const rows = Math.min(47, Math.max(Math.floor((hmax - WORLD_MIN_Y) / 8) + 1, Math.ceil((SEA + 1 - WORLD_MIN_Y) / 8)));
    const D = new Float32Array(25 * 49);
    for (let k = 0; k < 5; k++) for (let i = 0; i < 5; i++) {
      const ci = cinfo[k * 5 + i], gx = x0 + i * 4, gz = z0 + k * 4;
      for (let j = 0; j <= 48; j++) {
        const y = WORLD_MIN_Y + j * 8;
        D[(k * 5 + i) * 49 + j] = j > rows + 1 ? -10 : this.density(gx, y, gz, ci);
      }
    }
    const seed = this.seed;
    for (let ck = 0; ck < 4; ck++) for (let ci = 0; ci < 4; ci++) {
      const a = (ck * 5 + ci) * 49, b = (ck * 5 + ci + 1) * 49, c = ((ck + 1) * 5 + ci) * 49, d = ((ck + 1) * 5 + ci + 1) * 49;
      for (let cj = 0; cj < 48 && cj <= rows; cj++) {
        const d000 = D[a + cj], d100 = D[b + cj], d001 = D[c + cj], d101 = D[d + cj];
        const d010 = D[a + cj + 1], d110 = D[b + cj + 1], d011 = D[c + cj + 1], d111 = D[d + cj + 1];
        const yb = WORLD_MIN_Y + cj * 8;
        const allS = d000 > 0 && d100 > 0 && d001 > 0 && d101 > 0 && d010 > 0 && d110 > 0 && d011 > 0 && d111 > 0;
        const allA = d000 <= 0 && d100 <= 0 && d001 <= 0 && d101 <= 0 && d010 <= 0 && d110 <= 0 && d011 <= 0 && d111 <= 0;
        if (allA && yb > SEA) continue;
        for (let ly = 0; ly < 8; ly++) {
          const y = yb + ly, ty = ly / 8;
          const e00 = d000 + (d010 - d000) * ty, e10 = d100 + (d110 - d100) * ty;
          const e01 = d001 + (d011 - d001) * ty, e11 = d101 + (d111 - d101) * ty;
          let stone = B.STONE;
          if (y < 0) stone = B.DEEPSLATE;
          for (let lz = 0; lz < 4; lz++) {
            const tz = lz / 4;
            const f0 = e00 + (e01 - e00) * tz, f1 = e10 + (e11 - e10) * tz;
            const z = ck * 4 + lz;
            for (let lx = 0; lx < 4; lx++) {
              const x = ci * 4 + lx;
              const v = allS ? 1 : allA ? -1 : f0 + (f1 - f0) * (lx / 4);
              let id;
              if (v > 0) {
                id = stone;
                if (y >= 0 && y < 8 && hash3(seed, x0 + x, y, z0 + z) < (8 - y) / 8) id = B.DEEPSLATE;
              } else id = y <= SEA ? B.WATER : 0;
              if (id) ids[CI(x, y, z)] = id;
            }
          }
        }
      }
    }
  }

  isStoneLike(id) { return id === B.STONE || id === B.DEEPSLATE; }

  surface() {
    const ids = this.ids, meta = this.meta, H = this.H, x0 = this.x0, z0 = this.z0, seed = this.seed;
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const P = BPROP[this.BIO[z * 16 + x]], b = P.base;
      let y = WORLD_MAX_Y - 1;
      while (y > WORLD_MIN_Y && !this.isStoneLike(ids[CI(x, y, z)])) y--;
      this.TOP[z * 16 + x] = y;
      if (y <= WORLD_MIN_Y) continue;
      const underwater = ids[CI(x, y + 1, z)] === B.WATER;
      const wx = x0 + x, wz = z0 + z;
      const sn = this.nSurf.n2(wx / 16, wz / 16);
      const fillDepth = 3 + ((sn + 1) * 1.5 | 0);
      const hc = (H[(z + 1) * 18 + x + 1]);
      const slope = Math.max(Math.abs(H[(z + 1) * 18 + x + 2] - H[(z + 1) * 18 + x]), Math.abs(H[(z + 2) * 18 + x + 1] - H[z * 18 + x + 1]));
      let top = P.top, fill = P.fill;
      if (underwater) {
        let fl = P.floor;
        if (fl === -1) fl = sn > 0.2 ? B.GRAVEL : B.SAND;
        else if (fl === -2) fl = sn > 0.35 ? B.GRAVEL : sn < -0.55 ? B.CLAY : B.SAND;
        else if (fl === -3) fl = sn > 0.3 ? B.CLAY : B.DIRT;
        if (top === B.GRASS || top === B.PODZOL || top === B.MYCELIUM || top === B.SNOW) fl = y < SEA - 5 ? (sn > 0 ? B.GRAVEL : B.SAND) : B.DIRT;
        top = fl; fill = fl === B.CLAY ? B.DIRT : fl;
        if (b === BI.SWAMP || b === BI.MANGROVE_SWAMP) { top = sn > 0.3 ? B.CLAY : (b === BI.MANGROVE_SWAMP ? B.MUD : B.DIRT); fill = B.DIRT; }
      } else {
        if (P.mountain || b === BI.WINDSWEPT_HILLS || b === BI.MEADOW || b === BI.GROVE || b === BI.CHERRY_GROVE) {
          if (slope > 3.2 && !(P.snow && y > 150)) { top = B.STONE; fill = B.STONE; }
          if (b === BI.STONY_PEAKS && sn > 0.45) { top = B.CALCITE; fill = B.CALCITE; }
          if (b === BI.WINDSWEPT_HILLS && sn > 0.35) { top = sn > 0.6 ? B.GRAVEL : B.STONE; fill = B.STONE; }
        }
        if (!P.snow && y > 168 + sn * 12 && this.clim && hc > 150 && b !== BI.BADLANDS && b !== BI.STONY_PEAKS) { top = B.SNOW; fill = B.SNOW; }
        if (b === BI.SAVANNA && slope > 3 && sn > 0.2) { top = B.COARSE_DIRT || B.DIRT; }
        if ((b === BI.TAIGA || b === BI.DARK_FOREST) && sn > 0.55) top = B.PODZOL;
        // podzol / packed-ice patches like the original surface rules
        const n01 = (f, ox, oz) => this.nMisc.n2(wx * f + ox, wz * f + oz) * 0.5 + 0.5;
        if (b === BI.OLD_GROWTH_SPRUCE_TAIGA && top === B.GRASS) top = n01(0.09, -13, 13) < 0.58 ? B.PODZOL : sn < -0.55 ? B.COARSE_DIRT : B.GRASS;
        if (b === BI.BAMBOO_JUNGLE && top === B.GRASS && n01(0.12, -3, 7) < 0.4) top = B.PODZOL;
        if (b === BI.FROZEN_PEAKS && n01(0.1, 3, -3) < 0.46) { top = B.PACKED_ICE; fill = B.PACKED_ICE; }
        if (y >= SEA - 1 && y <= SEA + 1 && (top === B.GRASS) && sn > 0.6 && b !== BI.SWAMP) top = B.SAND;
      }
      // walk down replacing
      let depth = 0;
      for (let yy = y; yy > WORLD_MIN_Y && depth < fillDepth + 4; yy--) {
        const i = CI(x, yy, z);
        if (!this.isStoneLike(ids[i])) break;
        if (P.badlands && !underwater) {
          if (depth === 0) ids[i] = (b === BI.WOODED_BADLANDS && yy >= SEA + 10) ? (sn > 0 ? B.GRASS : B.COARSE_DIRT || B.DIRT) : B.RED_SAND;
          else if (depth <= 1 && b !== BI.WOODED_BADLANDS) ids[i] = B.RED_SAND;
          else ids[i] = TERRA_BANDS[((yy + Math.floor(this.nMisc.n2(wx / 60, wz / 60) * 3)) % TERRA_BANDS.length + TERRA_BANDS.length) % TERRA_BANDS.length];
          depth++;
          continue;
        }
        if (depth === 0) ids[i] = top;
        else if (depth <= fillDepth) ids[i] = fill;
        else if (P.sub && depth <= fillDepth + 3) ids[i] = P.sub;
        else break;
        depth++;
      }
      // badlands: bands further down into cliffs
      if (P.badlands && !underwater) {
        for (let yy = y - depth; yy > SEA - 8 && yy > y - 60; yy--) {
          const i = CI(x, yy, z);
          if (ids[i] !== B.STONE) break;
          ids[i] = TERRA_BANDS[((yy + Math.floor(this.nMisc.n2(wx / 60, wz / 60) * 3)) % TERRA_BANDS.length + TERRA_BANDS.length) % TERRA_BANDS.length];
        }
      }
      // frozen water
      if (P.cold === 2 && underwater) {
        for (let yy = SEA; yy >= SEA - 1; yy--) {
          const i = CI(x, yy, z);
          if (ids[i] === B.WATER && ids[CI(x, yy + 1, z)] === 0) {
            if (b !== BI.FROZEN_OCEAN || this.nMisc.n2(wx / 24, wz / 24) > -0.45) ids[i] = B.ICE;
            break;
          }
        }
      }
    }
  }

  caves() {
    const ids = this.ids, x0 = this.x0, z0 = this.z0, TOP = this.TOP;
    let maxTop = WORLD_MIN_Y;
    for (let i = 0; i < 256; i++) if (TOP[i] > maxTop) maxTop = TOP[i];
    const rowsN = Math.min(96, Math.floor((maxTop + 4 - WORLD_MIN_Y) / 4) + 1);
    // 5 channels on 5x(rows+1)x5 grid
    const NY = rowsN + 1;
    const G = new Float32Array(25 * NY * 5);
    for (let k = 0; k < 5; k++) for (let i = 0; i < 5; i++) for (let j = 0; j < NY; j++) {
      const x = x0 + i * 4, z = z0 + k * 4, y = WORLD_MIN_Y + j * 4;
      const o = ((k * 5 + i) * NY + j) * 5;
      G[o] = this.nCheese.f3(x / 72, y / 40, z / 72, 2);
      G[o + 1] = this.nSp1.n3(x / 52, y / 34, z / 52);
      G[o + 2] = this.nSp2.n3(x / 52, y / 34, z / 52);
      if (y < 40) { G[o + 3] = this.nNd1.n3(x / 26, y / 22, z / 26); G[o + 4] = this.nNd2.n3(x / 26, y / 22, z / 26); }
      else { G[o + 3] = 1; G[o + 4] = 1; }
    }
    const v = new Float32Array(5);
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const top = TOP[z * 16 + x];
      if (top <= WORLD_MIN_Y + 5) continue;
      const ci = x >> 2, ck = z >> 2, fx = (x & 3) / 4, fz = (z & 3) / 4;
      const b00 = ((ck * 5 + ci) * NY) * 5, b10 = ((ck * 5 + ci + 1) * NY) * 5, b01 = (((ck + 1) * 5 + ci) * NY) * 5, b11 = (((ck + 1) * 5 + ci + 1) * NY) * 5;
      const wet = ids[CI(x, top + 1, z)] === B.WATER;
      const colOcean = wet;
      const wx = x0 + x, wz = z0 + z;
      const spR = 0.0042 + 0.004 * (this.nMisc.n2(wx / 90, wz / 90) + 1) * 0.5;
      for (let y = Math.min(top, WORLD_MIN_Y + (NY - 1) * 4 - 1); y > WORLD_MIN_Y + 4; y--) {
        const i = CI(x, y, z);
        const id = ids[i];
        if (id === 0 || id === B.WATER || id === B.BEDROCK || id === B.ICE || id === B.PACKED_ICE) continue;
        const depth = top - y;
        if (wet && depth < 7) continue;
        const j = (y - WORLD_MIN_Y) >> 2, fy = ((y - WORLD_MIN_Y) & 3) / 4;
        for (let c = 0; c < 5; c++) {
          const a0 = G[b00 + j * 5 + c], a1 = G[b10 + j * 5 + c], a2 = G[b01 + j * 5 + c], a3 = G[b11 + j * 5 + c];
          const c0 = G[b00 + (j + 1) * 5 + c], c1 = G[b10 + (j + 1) * 5 + c], c2 = G[b01 + (j + 1) * 5 + c], c3 = G[b11 + (j + 1) * 5 + c];
          const l0 = a0 + (a1 - a0) * fx, l1 = a2 + (a3 - a2) * fx, u0 = c0 + (c1 - c0) * fx, u1 = c2 + (c3 - c2) * fx;
          const lo = l0 + (l1 - l0) * fz, hi = u0 + (u1 - u0) * fz;
          v[c] = lo + (hi - lo) * fy;
        }
        let cave = false;
        // cheese: big caverns deep down
        const cheeseT = 0.6 - 0.12 * Math.min(1, Math.max(0, (depth - 12) / 70)) + (y < 0 ? -0.03 : 0);
        if (depth > 12 && v[0] > cheeseT) cave = true;
        // spaghetti tunnels (may open to the surface)
        if (!cave && v[1] * v[1] + v[2] * v[2] < spR * (depth < 3 ? 0.6 : 1)) cave = true;
        // noodles
        if (!cave && depth > 10 && v[3] * v[3] + v[4] * v[4] < 0.0022) cave = true;
        if (!cave) continue;
        if (y <= -55) ids[i] = B.LAVA;
        else if (colOcean && y <= SEA) ids[i] = B.WATER;
        else ids[i] = 0;
        this.meta[i] = 0;
      }
      // fix grass exposed without soil above: dirt under carved top keeps; grass whose block was removed ok
      // surface block that lost support: if top was carved and block below top is dirt -> make grass
      let ny = WORLD_MAX_Y - 1;
      while (ny > WORLD_MIN_Y && (ids[CI(x, ny, z)] === 0)) ny--;
      if (ids[CI(x, ny, z)] === B.DIRT && ny >= SEA && ny < top && BPROP[this.BIO[z * 16 + x]].top === B.GRASS) ids[CI(x, ny, z)] = B.GRASS;
      TOP[z * 16 + x] = ids[CI(x, ny, z)] === B.WATER ? top : ny;
    }
  }

  ores() {
    const ids = this.ids, meta = this.meta;
    const rnd = mulberry(hash2i(this.seed + 77, this.cx, this.cz));
    const vein = (ore, deepOre, n, y, spread) => {
      let x = (rnd() * 16) | 0, z = (rnd() * 16) | 0;
      for (let k = 0; k < n; k++) {
        if (x >= 0 && x < 16 && z >= 0 && z < 16 && y > WORLD_MIN_Y && y < WORLD_MAX_Y) {
          const i = CI(x, y, z), id = ids[i];
          if (id === B.STONE || id === B.GRANITE || id === B.DIORITE || id === B.ANDESITE) ids[i] = ore;
          else if ((id === B.DEEPSLATE || id === B.TUFF) && deepOre) ids[i] = deepOre;
        }
        const r = rnd();
        if (r < 0.33) x += rnd() < 0.5 ? -1 : 1; else if (r < 0.66) z += rnd() < 0.5 ? -1 : 1; else y += rnd() < 0.5 ? -1 : 1;
        if (spread && rnd() < 0.3) { x += rnd() < 0.5 ? -1 : 1; }
      }
    };
    const tri = (a, b) => Math.round(a + (b - a) * (rnd() + rnd()) * 0.5);
    const uni = (a, b) => Math.round(a + (b - a) * rnd());
    // stone variety blobs
    const blob = (id, host2, n, y) => vein(id, host2, n, y, true);
    for (let i = 0; i < 2; i++) blob(B.GRANITE, 0, 40, uni(0, 70));
    for (let i = 0; i < 2; i++) blob(B.DIORITE, 0, 40, uni(0, 70));
    for (let i = 0; i < 2; i++) blob(B.ANDESITE, 0, 40, uni(0, 70));
    for (let i = 0; i < 2; i++) blob(B.TUFF, B.TUFF, 40, uni(-64, 0));
    for (let i = 0; i < 5; i++) blob(B.GRAVEL, B.GRAVEL, 26, uni(-64, 120));
    for (let i = 0; i < 4; i++) blob(B.DIRT, 0, 26, uni(0, 150));
    // ores
    const b = BPROP[this.BIO[8 * 16 + 8]].base;
    const mountain = BPROP[b].mountain || b === BI.WINDSWEPT_HILLS || b === BI.MEADOW || b === BI.GROVE || b === BI.CHERRY_GROVE;
    for (let i = 0; i < 20; i++) vein(B.COAL, B.DEEPSLATE_COAL_ORE, 14, tri(0, 192), false);
    for (let i = 0; i < 12; i++) vein(B.COAL, 0, 14, uni(136, 300), false);
    for (let i = 0; i < 10; i++) vein(B.IRON, B.DEEPSLATE_IRON_ORE, 8, tri(-24, 56), false);
    for (let i = 0; i < 8; i++) vein(B.IRON, B.DEEPSLATE_IRON_ORE, 4, uni(-64, 72), false);
    if (mountain) for (let i = 0; i < 12; i++) vein(B.IRON, 0, 8, tri(80, 300), false);
    for (let i = 0; i < 14; i++) vein(B.COPPER_ORE, B.DEEPSLATE_COPPER_ORE, 9, tri(-16, 112), false);
    for (let i = 0; i < 4; i++) vein(B.GOLD, B.DEEPSLATE_GOLD_ORE, 8, tri(-64, 32), false);
    if (b === BI.BADLANDS || b === BI.WOODED_BADLANDS) for (let i = 0; i < 20; i++) vein(B.GOLD, 0, 8, uni(32, 256), false);
    for (let i = 0; i < 4; i++) vein(B.REDSTONE_ORE, B.DEEPSLATE_REDSTONE_ORE, 7, uni(-64, 15), false);
    for (let i = 0; i < 8; i++) vein(B.REDSTONE_ORE, B.DEEPSLATE_REDSTONE_ORE, 7, tri(-96, -32), false);
    for (let i = 0; i < 2; i++) vein(B.LAPIS_ORE, B.DEEPSLATE_LAPIS_ORE, 6, tri(-32, 32), false);
    for (let i = 0; i < 3; i++) vein(B.LAPIS_ORE, B.DEEPSLATE_LAPIS_ORE, 6, uni(-64, 64), false);
    for (let i = 0; i < 7; i++) vein(B.DIAMOND, B.DEEPSLATE_DIAMOND_ORE, 4, tri(-110, 16), false);
    if (mountain) for (let i = 0; i < 8; i++) vein(B.EMERALD_ORE, B.DEEPSLATE_EMERALD_ORE, 1, tri(-16, 300), false);
    void meta;
  }

  bedrock() {
    const ids = this.ids, seed = this.seed, x0 = this.x0, z0 = this.z0;
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      ids[CI(x, WORLD_MIN_Y, z)] = B.BEDROCK;
      for (let k = 1; k < 5; k++) if (hash3(seed + 3, x0 + x, k, z0 + z) < (5 - k) / 5) ids[CI(x, WORLD_MIN_Y + k, z)] = B.BEDROCK;
    }
  }

  // ------------------------------------------------------------ features
  setB(x, y, z, id, m, mode) {
    // world coords -> local; mode 0: only air/replaceable, 1: also leaves (for logs), 2: force
    const lx = x - this.x0, lz = z - this.z0;
    if (lx < 0 || lx > 15 || lz < 0 || lz > 15 || y <= WORLD_MIN_Y || y >= WORLD_MAX_Y) return;
    const i = CI(lx, y, lz), cur = this.ids[i];
    if (mode !== 2) {
      if (cur !== 0 && !(FLAGS[cur] & BF_REPLACE) && !(mode === 1 && (FLAGS[cur] & BF_LEAVES))) return;
      if (cur === B.WATER && mode === 0 && !(FLAGS[id] & BF_LEAVES)) return;
    }
    this.ids[i] = id; this.meta[i] = m | 0;
  }
  getB(x, y, z) {
    const lx = x - this.x0, lz = z - this.z0;
    if (lx < 0 || lx > 15 || lz < 0 || lz > 15 || y <= WORLD_MIN_Y || y >= WORLD_MAX_Y) return -1;
    return this.ids[CI(lx, y, lz)];
  }

  // Tree decision for column x,z: the original's per-biome table (jittered grid cell + weighted
  // types). Only seed-derived data is used, so every chunk a tree overlaps makes the same decision.
  treeAt(x, z) {
    const bio = this.colInfo(x, z).biome, b = TREE_BASE[bio];
    const r0 = hash2(this.seed + 11, x * 3 + 11, z * 3 - 7);
    if (b === BI.DESERT) return r0 < 0.006 ? 'cactus' : 0;
    if (b === BI.MUSHROOM_FIELDS) return r0 < 0.004 ? 'giant_red_mushroom' : r0 < 0.008 ? 'giant_brown_mushroom' : 0;
    if (b === BI.ICE_SPIKES) return r0 < 0.01 ? 'ice_spike' : 0;
    const T = TREE_TABLE[b];
    if (!T) return 0;
    const cell = T[0], gx = Math.floor(x / cell), gz = Math.floor(z / cell);
    const px = gx * cell + ((hash2(this.seed + 21, gx, gz) * cell) | 0);
    const pz = gz * cell + ((hash2(this.seed + 22, gx, gz) * cell) | 0);
    if (px !== x || pz !== z) return 0;
    const r = hash2(this.seed + 23, gx, gz);
    for (const t of T[1]) if (r < t[0]) return t[1];
    return 0;
  }

  treeSpot(x, z) {
    const t = this.treeAt(x, z);
    if (!t) return null;
    const sy = this.surfaceAt(x, z);
    if (sy <= SEA || sy >= WORLD_MAX_Y - 14) return null;
    const P = BPROP[this.colInfo(x, z).biome], b = TREE_BASE[this.colInfo(x, z).biome];
    if (b === BI.WOODED_BADLANDS) { if (sy < SEA + 10) return null; }
    else if (t !== 'cactus' && (P.top === B.SAND || P.top === B.RED_SAND || P.top === B.STONE || P.top === B.GRAVEL)) return null;
    if (P.mountain || P.base === BI.WINDSWEPT_HILLS || P.base === BI.MEADOW || P.base === BI.GROVE || P.base === BI.CHERRY_GROVE) {
      const s = Math.max(Math.abs(this.colInfo(x + 1, z).h - this.colInfo(x - 1, z).h), Math.abs(this.colInfo(x, z + 1).h - this.colInfo(x, z - 1).h));
      if (s > 3.2) return null;
      // windswept stone / gravel patches stay bare
      if (P.base === BI.WINDSWEPT_HILLS && this.nSurf.n2(x / 16, z / 16) > 0.3) return null;
    }
    return { t, sy };
  }

  features() {
    const x0 = this.x0, z0 = this.z0, M = 5;
    for (let z = z0 - M; z < z0 + 16 + M; z++) for (let x = x0 - M; x < x0 + 16 + M; x++) {
      const spot = this.treeSpot(x, z);
      if (!spot) continue;
      const { t, sy } = spot;
      // the trunk stands on soil (or sand for a cactus)
      const g = this.getB(x, sy, z);
      if (g >= 0) {
        if (t === 'cactus') { if (g !== B.SAND && g !== B.RED_SAND) this.setB(x, sy, z, B.SAND, 0, 2); }
        else if (t === 'giant_red_mushroom' || t === 'giant_brown_mushroom') { if (g !== B.MYCELIUM && g !== B.PODZOL) this.setB(x, sy, z, B.MYCELIUM, 0, 2); }
        else if (t !== 'ice_spike' && g !== B.GRASS && g !== B.PODZOL && g !== B.COARSE_DIRT && g !== B.MUD && g !== B.DIRT) this.setB(x, sy, z, B.DIRT, 0, 2);
      }
      this.growTree(t, x, sy, z);
    }
    this.vegetation();
  }

  // square leaf layer of radius r (the original's shape): corners of r>=2 layers are dropped
  // (kept 40% of the time when "ragged"), r>=3 layers are rounded further. Leaves go only into
  // air / replaceable blocks; the per-block noise is positional, so chunks agree.
  layer(x, y, z, r, id, ragged) {
    for (let dx = -r; dx <= r; dx++) for (let dz = -r; dz <= r; dz++) {
      const ax = dx < 0 ? -dx : dx, az = dz < 0 ? -dz : dz;
      if (r >= 2 && ax === r && az === r && (!ragged || hash3(this.seed + 98, x + dx, y, z + dz) < 0.6)) continue;
      if (r >= 3 && ax + az > r + 1) continue;
      this.setB(x + dx, y, z + dz, id, 0, 0);
    }
  }
  // trunks are forced (like the original), so a canopy never hangs above a blocked trunk
  log(x, y, z, id) { this.setB(x, y, z, id, 0, 2); }

  growTree(type, x, sy, z) {
    const g = (k) => hash2(this.seed + 97, x * 31 + k, z * 17 - k);
    const h3 = (yy) => hash3(this.seed + 96, x, yy, z);
    switch (type) {
      case 'cactus': {
        const n = 2 + ((g(1) * 2) | 0);
        for (let y = sy + 1; y <= sy + n; y++) this.setB(x, y, z, B.CACTUS, 0, 0);
        return;
      }
      case 'spruce': case 'tall_spruce': {
        const tall = type === 'tall_spruce';
        const top = sy + (tall ? 10 + ((g(2) * 4) | 0) : 6 + ((g(3) * 3) | 0));
        this.setB(x, top + 1, z, B.SPRUCE_LEAVES, 0, 0);
        const n = tall ? 8 : 5;
        for (let u = 0; u < n; u++) {
          const y = top - u;
          if (y <= sy + 2) break;
          this.layer(x, y, z, u === 0 || u % 2 === 1 ? 1 : 2, B.SPRUCE_LEAVES, false);
        }
        for (let y = sy + 1; y <= top; y++) this.log(x, y, z, B.SPRUCE_LOG);
        return;
      }
      case 'dark_oak': {
        const top = sy + 7 + ((g(4) * 3) | 0);
        for (let u = -2; u <= 1; u++) if (top + u > sy) this.layer(x, top + u, z, u <= 0 ? 3 : 2, B.DARK_LEAVES, true);
        for (let y = sy + 1; y <= top; y++) for (let d = 0; d < 4; d++) this.log(x + (d & 1), y, z + (d >> 1), B.DARK_LOG);
        return;
      }
      case 'jungle': {
        const top = sy + 8 + ((g(5) * 5) | 0);
        this.layer(x, top + 1, z, 1, B.JUNGLE_LEAVES, false);
        this.layer(x, top, z, 2, B.JUNGLE_LEAVES, true);
        this.layer(x, top - 1, z, 2, B.JUNGLE_LEAVES, true);
        for (let y = sy + 1; y <= top; y++) this.log(x, y, z, B.JUNGLE_LOG);
        return;
      }
      case 'acacia': {
        const n = 5 + ((g(6) * 2) | 0), dir = (g(7) * 4) | 0;
        const ddx = [1, -1, 0, 0][dir], ddz = [0, 0, 1, -1][dir];
        let cx = x, cz = z;
        for (let y = sy + 1; y <= sy + n; y++) {
          if (y > sy + 2 && (y - sy) % 2 === 1) { cx += ddx; cz += ddz; }
          this.log(cx, y, cz, B.ACACIA_LOG);
        }
        this.layer(cx, sy + n, cz, 3, B.ACACIA_LEAVES, false);
        this.layer(cx, sy + n + 1, cz, 2, B.ACACIA_LEAVES, true);
        return;
      }
      case 'fancy_oak': {
        const top = sy + 7 + ((g(8) * 3) | 0), R = [2, 3, 3, 2, 1];
        for (let u = 0; u < R.length; u++) { const y = top - 3 + u; if (y > sy) this.layer(x, y, z, R[u], B.LEAVES, true); }
        const k = (g(9) * 4) | 0;
        this.layer(x + [2, -2, 0, 0][k], top - 3, z + [0, 0, 2, -2][k], 1, B.LEAVES, false);
        for (let y = sy + 1; y <= top; y++) this.log(x, y, z, B.LOG);
        return;
      }
      case 'cherry': {
        const top = sy + 4 + ((g(10) * 2) | 0), R = [2, 3, 2];
        this.layer(x, top + 1, z, 1, B.FLOWERING_AZALEA_LEAVES, false);
        for (let u = 0; u < R.length; u++) {
          const y = top - u;
          if (y <= sy + 1) break;
          this.layer(x, y, z, R[u], h3(y) < 0.25 ? B.AZALEA_LEAVES : B.FLOWERING_AZALEA_LEAVES, true);
        }
        for (let y = sy + 1; y <= top; y++) this.log(x, y, z, B.LOG);
        return;
      }
      case 'giant_red_mushroom': {
        const n = 4 + ((g(11) * 3) | 0);
        for (let y = sy + 1; y <= sy + n; y++) this.log(x, y, z, B.MUSHROOM_STEM);
        this.layer(x, sy + n + 1, z, 1, B.RED_MUSHROOM_BLOCK, false);
        this.layer(x, sy + n, z, 2, B.RED_MUSHROOM_BLOCK, true);
        return;
      }
      case 'giant_brown_mushroom': {
        const n = 3 + ((g(12) * 2) | 0);
        for (let y = sy + 1; y <= sy + n; y++) this.log(x, y, z, B.MUSHROOM_STEM);
        this.layer(x, sy + n + 1, z, 3, B.BROWN_MUSHROOM_BLOCK, false);
        return;
      }
      case 'ice_spike': {
        const n = 4 + ((g(13) * 7) | 0);
        for (let y = sy + 1; y <= sy + n; y++) this.setB(x, y, z, B.PACKED_ICE, 0, 0);
        if (n > 5) { this.setB(x + 1, sy + 1, z, B.PACKED_ICE, 0, 0); this.setB(x, sy + 1, z + 1, B.PACKED_ICE, 0, 0); }
        return;
      }
      default: { // oak / birch
        const birch = type === 'birch';
        const logId = birch ? B.BIRCH_LOG : B.LOG, leafId = birch ? B.BIRCH_LEAVES : B.LEAVES;
        const top = sy + 4 + ((g(14) * 3) | 0) + (birch ? 1 : 0);
        for (let u = -2; u <= 1; u++) { const y = top + u; if (y > sy && y < WORLD_MAX_Y) this.layer(x, y, z, u <= -1 ? 2 : 1, leafId, true); }
        for (let y = sy + 1; y <= top; y++) this.log(x, y, z, logId);
      }
    }
  }

  vegetation() {
    const ids = this.ids, meta = this.meta, x0 = this.x0, z0 = this.z0, seed = this.seed;
    const pick = (arr, r) => arr[Math.floor(r * arr.length) % arr.length];
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const wx = x0 + x, wz = z0 + z;
      const b = BPROP[this.BIO[z * 16 + x]].base;
      let y = WORLD_MAX_Y - 2;
      while (y > WORLD_MIN_Y && ids[CI(x, y, z)] === 0) y--;
      const g = ids[CI(x, y, z)];
      const r = hash2(seed + 31, wx, wz), r2 = hash2(seed + 32, wx, wz);
      if (g === B.WATER) {
        // underwater plants
        let fy = y; while (fy > WORLD_MIN_Y && ids[CI(x, fy, z)] === B.WATER) fy--;
        const floor = ids[CI(x, fy, z)], depth = y - fy;
        if (b === BI.SWAMP || b === BI.MANGROVE_SWAMP) { if (depth === 1 && r < 0.08 && y === SEA) ids[CI(x, y + 1, z)] = B.LILY_PAD, meta[CI(x, y + 1, z)] = (r2 * 4) | 0; }
        if (floor === 0 || floor === B.ICE || depth < 2) continue;
        const oceanish = !!BPROP[b].ocean;
        if (b === BI.WARM_OCEAN && depth >= 3 && this.nMisc.n2(wx / 12, wz / 12) > 0.15) {
          const coral = [B.TUBE_CORAL_BLOCK, B.BRAIN_CORAL_BLOCK, B.BUBBLE_CORAL_BLOCK, B.FIRE_CORAL_BLOCK, B.HORN_CORAL_BLOCK];
          const fans = [B.TUBE_CORAL_FAN, B.BRAIN_CORAL_FAN, B.BUBBLE_CORAL_FAN, B.FIRE_CORAL_FAN, B.HORN_CORAL_FAN];
          const ci = Math.floor(hash2(seed + 40, wx >> 2, wz >> 2) * 5);
          if (r < 0.55) {
            const hh = 1 + ((r2 * 3) | 0);
            for (let k = 1; k <= hh && fy + k < y; k++) ids[CI(x, fy + k, z)] = coral[ci];
            if (fy + hh + 1 < y) ids[CI(x, fy + hh + 1, z)] = fans[(ci + (r2 > 0.5 ? 1 : 0)) % 5];
          } else if (r < 0.75) ids[CI(x, fy + 1, z)] = fans[(r2 * 5) | 0];
          else if (r < 0.8) { ids[CI(x, fy + 1, z)] = B.SEA_PICKLE; meta[CI(x, fy + 1, z)] = (r2 * 4) | 0; }
          continue;
        }
        if (oceanish && b !== BI.FROZEN_OCEAN && b !== BI.WARM_OCEAN && r < 0.09 && depth >= 4 && b !== BI.RIVER) {
          const len = 1 + Math.floor(r2 * (depth - 2));
          for (let k = 1; k <= len; k++) ids[CI(x, fy + k, z)] = B.KELP;
          continue;
        }
        if (r < (b === BI.RIVER ? 0.25 : 0.35) && b !== BI.FROZEN_OCEAN) ids[CI(x, fy + 1, z)] = B.SEAGRASS;
        continue;
      }
      if (y < SEA || y >= WORLD_MAX_Y - 3 || ids[CI(x, y + 1, z)] !== 0) continue;
      const above = CI(x, y + 1, z);
      const setTall = (id) => { if (ids[CI(x, y + 2, z)] === 0) { ids[above] = id; ids[CI(x, y + 2, z)] = id; meta[CI(x, y + 2, z)] = 1; } };
      // sugar cane next to water
      if ((g === B.GRASS || g === B.SAND || g === B.DIRT || g === B.RED_SAND) && r < 0.12 && y === SEA) {
        const wn = (x < 15 && ids[CI(x + 1, y, z)] === B.WATER) || (x > 0 && ids[CI(x - 1, y, z)] === B.WATER) ||
          (z < 15 && ids[CI(x, y, z + 1)] === B.WATER) || (z > 0 && ids[CI(x, y, z - 1)] === B.WATER);
        if (wn) { const hh = 2 + ((r2 * 3) | 0); for (let k = 1; k <= hh; k++) ids[CI(x, y + k, z)] = B.SUGAR_CANE; continue; }
      }
      if (g === B.GRASS && r < 0.00018) { ids[above] = B.PUMPKIN; continue; }
      if (g === B.SAND || g === B.RED_SAND) {
        if ((b === BI.DESERT || b === BI.BADLANDS || b === BI.WOODED_BADLANDS) && r < 0.015) ids[above] = B.DEAD_BUSH;
        continue;
      }
      if (g === B.MYCELIUM) { if (r < 0.05) ids[above] = r2 < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM; continue; }
      if (g !== B.GRASS && g !== B.PODZOL && g !== B.COARSE_DIRT && g !== B.MUD) {
        continue;
      }
      switch (b) {
        case BI.PLAINS:
          if (r < 0.2) ids[above] = B.TALL_GRASS;
          else if (r < 0.215) setTall(B.TALL_GRASS_PLANT);
          else if (r < 0.235) ids[above] = pick([B.DANDELION, B.POPPY, B.CORNFLOWER, B.OXEYE_DAISY, B.AZURE_BLUET, B.DANDELION], r2);
          break;
        case BI.SUNFLOWER_PLAINS:
          if (r < 0.18) ids[above] = B.TALL_GRASS;
          else if (r < 0.23) setTall(B.SUNFLOWER);
          else if (r < 0.25) ids[above] = pick([B.DANDELION, B.POPPY, B.OXEYE_DAISY], r2);
          break;
        case BI.MEADOW:
          if (r < 0.3) ids[above] = B.TALL_GRASS;
          else if (r < 0.34) setTall(B.TALL_GRASS_PLANT);
          else if (r < 0.42) ids[above] = pick([B.DANDELION, B.CORNFLOWER, B.OXEYE_DAISY, B.POPPY, B.ALLIUM, B.AZURE_BLUET], r2);
          break;
        case BI.FLOWER_FOREST:
          if (r < 0.06) ids[above] = B.TALL_GRASS;
          else if (r < 0.28) ids[above] = pick([B.DANDELION, B.POPPY, B.ALLIUM, B.CORNFLOWER, B.OXEYE_DAISY, B.AZURE_BLUET,
            B.LILY_OF_THE_VALLEY, B.ORANGE_TULIP, B.PINK_TULIP, B.RED_TULIP, B.WHITE_TULIP], this.nMisc.n2(wx / 24, wz / 24) * 0.5 + 0.5);
          else if (r < 0.295) setTall(r2 < 0.5 ? B.LILAC : B.ROSE_BUSH);
          break;
        case BI.FOREST: case BI.WINDSWEPT_HILLS:
          if (r < 0.1) ids[above] = B.TALL_GRASS;
          else if (r < 0.12) ids[above] = B.FERN;
          else if (r < 0.132) ids[above] = r2 < 0.5 ? B.DANDELION : B.POPPY;
          else if (r < 0.137) setTall(pick([B.LILAC, B.ROSE_BUSH, B.PEONY], r2));
          break;
        case BI.BIRCH_FOREST:
          if (r < 0.1) ids[above] = B.TALL_GRASS;
          else if (r < 0.125) ids[above] = r2 < 0.5 ? B.LILY_OF_THE_VALLEY : B.DANDELION;
          else if (r < 0.13) setTall(r2 < 0.5 ? B.LILAC : B.PEONY);
          break;
        case BI.DARK_FOREST:
          if (r < 0.08) ids[above] = B.TALL_GRASS;
          else if (r < 0.1) ids[above] = r2 < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM;
          else if (r < 0.11) setTall(B.LARGE_FERN);
          else if (r < 0.114) setTall(r2 < 0.5 ? B.ROSE_BUSH : B.PEONY);
          break;
        case BI.JUNGLE: case BI.SPARSE_JUNGLE:
          if (r < 0.22) ids[above] = B.TALL_GRASS;
          else if (r < 0.32) ids[above] = B.FERN;
          else if (r < 0.34) setTall(B.LARGE_FERN);
          else if (r < 0.342) ids[above] = B.MELON;
          break;
        case BI.BAMBOO_JUNGLE:
          if (r < 0.25) { const hh = 4 + ((r2 * 8) | 0); for (let k = 1; k <= hh; k++) if (ids[CI(x, y + k, z)] === 0) ids[CI(x, y + k, z)] = B.BAMBOO_PLANT; }
          else if (r < 0.4) ids[above] = B.FERN;
          else if (r < 0.5) ids[above] = B.TALL_GRASS;
          break;
        case BI.TAIGA: case BI.OLD_GROWTH_TAIGA: case BI.SNOWY_TAIGA: case BI.GROVE:
          if (g === B.SNOW) break;
          if (r < 0.1) ids[above] = B.FERN;
          else if (r < 0.15) { if (r2 < 0.5) ids[above] = B.TALL_GRASS; else setTall(B.LARGE_FERN); }
          else if (r < 0.158) ids[above] = B.SWEET_BERRY_BUSH;
          else if (r < 0.162 && b === BI.OLD_GROWTH_TAIGA) ids[above] = r2 < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM;
          break;
        case BI.SAVANNA:
          if (r < 0.3) ids[above] = B.TALL_GRASS;
          else if (r < 0.33) setTall(B.TALL_GRASS_PLANT);
          break;
        case BI.SWAMP: case BI.MANGROVE_SWAMP:
          if (r < 0.1) ids[above] = B.TALL_GRASS;
          else if (r < 0.13) ids[above] = B.FERN;
          else if (r < 0.145) ids[above] = B.BLUE_ORCHID;
          else if (r < 0.16) ids[above] = r2 < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM;
          break;
        case BI.CHERRY_GROVE:
          if (r < 0.14) ids[above] = B.TALL_GRASS;
          else if (r < 0.3) ids[above] = pick([B.PINK_TULIP, B.ALLIUM, B.OXEYE_DAISY, B.DANDELION, B.PINK_TULIP], r2);
          break;
        case BI.WOODED_BADLANDS:
          if (r < 0.06) ids[above] = B.TALL_GRASS; else if (r < 0.08) ids[above] = B.DEAD_BUSH;
          break;
        case BI.SNOWY_PLAINS:
          if (r < 0.01) ids[above] = B.TALL_GRASS;
          break;
        default:
          if (r < 0.06) ids[above] = B.TALL_GRASS;
      }
    }
  }

  caveDecor() {
    const ids = this.ids, meta = this.meta, x0 = this.x0, z0 = this.z0, TOP = this.TOP, seed = this.seed;
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const wx = x0 + x, wz = z0 + z, top = TOP[z * 16 + x];
      for (let y = top - 6; y > WORLD_MIN_Y + 6; y--) {
        const i = CI(x, y, z);
        if (ids[i] !== 0) continue;
        const below = ids[i - 256], above = ids[i + 256];
        const floor = below === B.STONE || below === B.DEEPSLATE || below === B.TUFF || below === B.GRANITE || below === B.DIORITE || below === B.ANDESITE || below === B.GRAVEL;
        const ceil = above === B.STONE || above === B.DEEPSLATE || above === B.TUFF || above === B.GRANITE || above === B.DIORITE || above === B.ANDESITE;
        if (!floor && !ceil) {
          // glow lichen on walls
          if (y < 40 && hash3(seed + 50, wx, y, wz) < 0.012) {
            let bits = 0;
            if (x < 15 && OPAQUE[ids[i + 1]]) bits |= 8; if (x > 0 && OPAQUE[ids[i - 1]]) bits |= 2;
            if (z < 15 && OPAQUE[ids[i + 16]]) bits |= 1; if (z > 0 && OPAQUE[ids[i - 16]]) bits |= 4;
            if (bits) { ids[i] = B.GLOW_LICHEN; meta[i] = bits; }
          }
          continue;
        }
        const reg = this.nCave.n3(wx / 70, y / 40, wz / 70), reg2 = this.nCave.n3(wx / 70 + 300, y / 40, wz / 70 - 200);
        const r = hash3(seed + 51, wx, y, wz);
        if (reg > 0.42 && y < top - 14) {
          // lush cave
          if (floor) {
            ids[i - 256] = r < 0.05 ? B.CLAY : B.MOSS_BLOCK;
            if (r < 0.1) ids[i] = B.FLOWERING_AZALEA_LEAVES;
            else if (r < 0.28) ids[i] = B.MOSS_CARPET;
            else if (r < 0.45) ids[i] = r < 0.36 ? B.TALL_GRASS : B.FERN;
          } else if (ceil) {
            ids[i + 256] = B.MOSS_BLOCK;
            if (r < 0.3) {
              const len = 1 + ((hash3(seed + 52, wx, y, wz) * 6) | 0);
              for (let k = 0; k < len; k++) {
                const j = i - k * 256;
                if (ids[j] !== 0) break;
                ids[j] = B.CAVE_VINES; meta[j] = hash3(seed + 53, wx, y - k, wz) < 0.18 ? 1 : 0;
              }
            }
          }
        } else if (reg2 > 0.45) {
          // dripstone cave
          if (floor) {
            if (r < 0.4) ids[i - 256] = B.DRIPSTONE;
            if (r < 0.07) { const len = 1 + ((hash3(seed + 54, wx, y, wz) * 3) | 0); for (let k = 0; k < len; k++) { const j = i + k * 256; if (ids[j] !== 0) break; ids[j] = B.DRIPSTONE; } }
          } else if (ceil) {
            if (r < 0.4) ids[i + 256] = B.DRIPSTONE;
            if (r < 0.07) { const len = 1 + ((hash3(seed + 55, wx, y, wz) * 3) | 0); for (let k = 0; k < len; k++) { const j = i - k * 256; if (ids[j] !== 0) break; ids[j] = B.DRIPSTONE; } }
          }
        } else if (y < -20 && reg < -0.5 && floor && r < 0.03) {
          ids[i - 256] = r < 0.01 ? B.BUDDING_AMETHYST : B.AMETHYST_BLOCK;
        }
      }
    }
  }
}
