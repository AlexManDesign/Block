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

// Minecraft (Java) biome temperatures. Above y 80 the temperature drops by 0.00125 per block (with
// a little noise); below 0.15 water freezes and snow settles, so taiga gets snowy tops on high
// ground while a forest or meadow never does.
const BIOME_TEMP = new Float32Array(BIOME_LIST.length);
(function () {
  const T = {
    PLAINS: 0.8, SUNFLOWER_PLAINS: 0.8, BEACH: 0.8, SWAMP: 0.8, MANGROVE_SWAMP: 0.8, FOREST: 0.7, FLOWER_FOREST: 0.7,
    DARK_FOREST: 0.7, BIRCH_FOREST: 0.6, OLD_GROWTH_BIRCH_FOREST: 0.6, TAIGA: 0.25, OLD_GROWTH_PINE_TAIGA: 0.3,
    OLD_GROWTH_SPRUCE_TAIGA: 0.25, MEADOW: 0.5, CHERRY_GROVE: 0.5, WINDSWEPT_HILLS: 0.2, WINDSWEPT_GRAVELLY_HILLS: 0.2,
    WINDSWEPT_FOREST: 0.2, STONY_SHORE: 0.2, STONY_PEAKS: 1.0, JUNGLE: 0.95, SPARSE_JUNGLE: 0.95, BAMBOO_JUNGLE: 0.95,
    DESERT: 2, SAVANNA: 2, SAVANNA_PLATEAU: 2, WINDSWEPT_SAVANNA: 2, BADLANDS: 2, ERODED_BADLANDS: 2, WOODED_BADLANDS: 2,
    MUSHROOM_FIELDS: 0.9, RIVER: 0.5, OCEAN: 0.5, DEEP_OCEAN: 0.5, LUKEWARM_OCEAN: 0.5, DEEP_LUKEWARM_OCEAN: 0.5,
    WARM_OCEAN: 0.5, COLD_OCEAN: 0.5, DEEP_COLD_OCEAN: 0.5, DEEP_FROZEN_OCEAN: 0.5,
  };
  BIOME_LIST.forEach((b, i) => { BIOME_TEMP[i] = T[b[0]] ?? -0.5; });   // the rest are the snowy biomes
})();

// Biome colours as Minecraft (Java) gives them: grass and foliage from the colormaps at the biome's
// temperature / downfall (plus the dark forest, swamp and badlands overrides), and the water colour.
// BIOME_TINT[b * 3 + k]: k = 0 grass, 1 foliage, 2 water (0xRRGGBB).
const BIOME_TINT = new Uint32Array(BIOME_LIST.length * 3);
(function () {
  const C = {
    PLAINS: [0x91BD59, 0x77AB2F], SUNFLOWER_PLAINS: [0x91BD59, 0x77AB2F], BEACH: [0x91BD59, 0x77AB2F],
    DESERT: [0xBFB755, 0xAEA42A], SAVANNA: [0xBFB755, 0xAEA42A], SAVANNA_PLATEAU: [0xBFB755, 0xAEA42A],
    WINDSWEPT_SAVANNA: [0xBFB755, 0xAEA42A],
    BADLANDS: [0x90814D, 0x9E814D], ERODED_BADLANDS: [0x90814D, 0x9E814D], WOODED_BADLANDS: [0x90814D, 0x9E814D],
    FOREST: [0x79C05A, 0x59AE30], FLOWER_FOREST: [0x79C05A, 0x59AE30], DARK_FOREST: [0x507A32, 0x59AE30],
    BIRCH_FOREST: [0x88BB67, 0x6BA941], OLD_GROWTH_BIRCH_FOREST: [0x88BB67, 0x6BA941],
    TAIGA: [0x86B783, 0x68A464], OLD_GROWTH_PINE_TAIGA: [0x86B87F, 0x68A55F], OLD_GROWTH_SPRUCE_TAIGA: [0x86B87F, 0x68A55F],
    SNOWY_TAIGA: [0x80B497, 0x60A17B], SNOWY_PLAINS: [0x80B497, 0x60A17B], ICE_SPIKES: [0x80B497, 0x60A17B],
    SNOWY_BEACH: [0x83B593, 0x64A278], FROZEN_RIVER: [0x80B497, 0x60A17B], FROZEN_OCEAN: [0x80B497, 0x60A17B],
    DEEP_FROZEN_OCEAN: [0x8EB971, 0x71A74D], GROVE: [0x80B497, 0x60A17B], SNOWY_SLOPES: [0x80B497, 0x60A17B],
    JAGGED_PEAKS: [0x80B497, 0x60A17B], FROZEN_PEAKS: [0x80B497, 0x60A17B], STONY_PEAKS: [0x9ABE4B, 0x82AC1E],
    WINDSWEPT_HILLS: [0x8AB689, 0x6DA36B], WINDSWEPT_GRAVELLY_HILLS: [0x8AB689, 0x6DA36B],
    WINDSWEPT_FOREST: [0x8AB689, 0x6DA36B], STONY_SHORE: [0x8AB689, 0x6DA36B],
    MEADOW: [0x83BB6D, 0x63A948], CHERRY_GROVE: [0xB6DB61, 0xB6DB61],
    JUNGLE: [0x59C93C, 0x30BB0B], BAMBOO_JUNGLE: [0x59C93C, 0x30BB0B], SPARSE_JUNGLE: [0x64C73F, 0x3EB80F],
    SWAMP: [0x6A7039, 0x6A7039], MANGROVE_SWAMP: [0x6A7039, 0x8DB127], MUSHROOM_FIELDS: [0x55C93F, 0x2BBB0F],
  };
  const W = {
    SWAMP: 0x617B64, MANGROVE_SWAMP: 0x3A7A6A, WARM_OCEAN: 0x43D5EE, LUKEWARM_OCEAN: 0x45ADF2,
    DEEP_LUKEWARM_OCEAN: 0x45ADF2, COLD_OCEAN: 0x3D57D6, DEEP_COLD_OCEAN: 0x3D57D6, FROZEN_OCEAN: 0x3938C9,
    DEEP_FROZEN_OCEAN: 0x3938C9, FROZEN_RIVER: 0x3938C9, SNOWY_BEACH: 0x3D57D6, SNOWY_TAIGA: 0x3D57D6,
    MEADOW: 0x0E4ECF, CHERRY_GROVE: 0x5DB7EF,
  };
  // everything else (oceans, rivers, caves): temperate defaults
  BIOME_LIST.forEach((b, i) => {
    const c = C[b[0]] || [0x8EB971, 0x71A74D];
    BIOME_TINT[i * 3] = c[0]; BIOME_TINT[i * 3 + 1] = c[1]; BIOME_TINT[i * 3 + 2] = W[b[0]] || 0x3F76E4;
  });
})();

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
const ORIG_BIOME = [];   // biome of the original's set that a biome follows (variants -> base)
// room a tree needs from a higher-priority neighbour (Chebyshev distance between trunks)
const TREE_ROOM = { cactus: 2, ice_spike: 2, giant_red_mushroom: 3, dark_oak: 5, swamp_oak: 5 };
// deepest water (blocks above the ground) a tree may stand in, as Minecraft's surface water depth filter
const TREE_WATER = { mangrove: 5, tall_mangrove: 5, swamp_oak: 2 };
const FACE6 = [1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1], FACE4 = [1, 0, -1, 0, 0, 1, 0, -1];
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
  T('SWAMP', 9, [[0.55, 'swamp_oak']]);
  T('WINDSWEPT_HILLS', 15, [[0.25, 'oak'], [0.4, 'spruce']]);
  T('CHERRY_GROVE', 9, [[0.4, 'cherry']]);
  T('BAMBOO_JUNGLE', 6, [[0.5, 'jungle'], [0.65, 'oak']]);
  T('WOODED_BADLANDS', 10, [[0.35, 'oak']]);
  T('MANGROVE_SWAMP', 5, [[0.85, 'mangrove'], [1, 'tall_mangrove']]);
  const alias = { SUNFLOWER_PLAINS: 'PLAINS', OLD_GROWTH_BIRCH_FOREST: 'BIRCH_FOREST', OLD_GROWTH_PINE_TAIGA: 'OLD_GROWTH_SPRUCE_TAIGA',
    SAVANNA_PLATEAU: 'SAVANNA', WINDSWEPT_SAVANNA: 'SAVANNA', WINDSWEPT_GRAVELLY_HILLS: 'WINDSWEPT_HILLS', WINDSWEPT_FOREST: 'WINDSWEPT_HILLS',
    ERODED_BADLANDS: 'BADLANDS' };
  BIOME_LIST.forEach((b, i) => { ORIG_BIOME[i] = alias[b[0]] ? BI[alias[b[0]]] : BPROP[i].base; });
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

const CORAL_BLOCKS = [B.TUBE_CORAL_BLOCK, B.BRAIN_CORAL_BLOCK, B.BUBBLE_CORAL_BLOCK, B.FIRE_CORAL_BLOCK, B.HORN_CORAL_BLOCK];
const CORAL_FANS = [B.TUBE_CORAL_FAN, B.BRAIN_CORAL_FAN, B.BUBBLE_CORAL_FAN, B.FIRE_CORAL_FAN, B.HORN_CORAL_FAN];
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
    this.vn = new ValueNoise(this.seed);
    this.gCache = new Map();
    this.candCache = new Map();
    this.treeCache = new Map();
    this.deco = [];                              // tree decorations of the chunk being generated
    this.treeWrites = null;                      // queued tree blocks while features() runs
    this.tb = { n: 0, map: new Map(), x: new Int32Array(4096), y: new Int32Array(4096), z: new Int32Array(4096), id: new Uint16Array(4096),
      kind: new Uint8Array(4096), dist: new Uint8Array(4096), q: new Int32Array(4096) };
  }

  // ------------------------------------------------------------ climate / height
  // fractal noise with Perlin-like amplitude (simplex output is scaled down to match)
  fbm(n, x, z, scale, oct) { return n.f2(x * scale, z * scale, oct) * 0.72; }
  // terrain part of the climate: height + density parameters for column x,z
  terrain(x, z, o) {
    const c = this.fbm(this.nCont, x, z, 1 / 2200, 4) * 2.3 + 0.3;
    const e = this.fbm(this.nEro, x, z, 1 / 2000, 4) * 2.1;
    const w = this.fbm(this.nWeird, x, z, 1 / 850, 5) * 2.2;
    const pv = 1 - Math.abs(3 * Math.abs(w) - 2);
    let h = spline(SPL_BASE, c);
    const s = spline(SPL_MOUNT, e) * clamp01((c + 0.25) / 0.4);
    // mountains: low erosion raises the land, the peaks-and-valleys ridge decides how much. The
    // power above 1 keeps high ground rare and the tops pointed (no clipping, which would leave
    // flat mesas), and the amplitude keeps peaks below ~y 255 as in Minecraft
    const wk = Math.pow(clamp01(pv * 0.5 + 0.5), 2);
    h += Math.pow(Math.pow(s, 1.15) * wk, 1.3) * 175;
    if (h > SEA - 3) h += this.fbm(this.nHill, x, z, 1 / 22, 3) * (9 + s * 15) * clamp01((h - (SEA - 2)) / 11);
    else if (h < SEA - 4) h += this.fbm(this.nMisc, x, z, 0.018, 2) * 2.2 * (2.5 + clamp01((SEA - 4 - h) / 22) * 8);
    // Minecraft's highest erosion zone (6) inland is where swamps and mangrove swamps (snowy plains
    // when frozen) sit: the terrain there is flat around sea level, its dips fill as pools
    const k6 = sstep(0.5, 0.6, e * 1.5) * sstep(0.1, 0.22, c);
    if (k6 > 0) {
      const t = SEA + 0.6 + this.fbm(this.nHill, x, z, 1 / 38, 2) * 2.4;
      if (h > t) h += (t - h) * k6;
    }
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
    o.h = h; o.c = c; o.e = e; o.w = w; o.pv = pv; o.s = s; o.mush = mush; o.k6 = k6;
    o.factor = spline(SPL_FACTOR, e);
    return o;
  }
  // full climate incl. temperature / humidity (domain warped) for biome selection
  climate(x, z, o) {
    this.terrain(x, z, o);
    const wx = x + (this.nWarp.n2(x * 0.008, z * 0.008) * 0.5) * 16, wz = z + (this.nWarp.n2(x * 0.008 + 300, z * 0.008 - 300) * 0.5) * 16;
    o.t = clamp1(this.fbm(this.nTemp, wx, wz, 1 / 3200, 3) * 2.4);
    o.hu = clamp1(this.fbm(this.nHum, wx, wz, 1 / 1500, 3) * 2.1);
    o.ti = o.t < -0.45 ? 0 : o.t < -0.15 ? 1 : o.t < 0.2 ? 2 : o.t < 0.55 ? 3 : 4;
    return o;
  }

  biomeOf(o, x, z) {
    const h = Math.round(o.h), ti = o.ti;
    if (o.mush > 0.25 && h > SEA) return BI.MUSHROOM_FIELDS;
    // flat erosion-6 lowland: water level ground is still land (swamp), not beach or ocean
    const low6 = o.k6 > 0.5 && h > SEA - 4;
    if (h <= SEA) {
      if (o.c > -0.11 && h > SEA - 5 && Math.abs(o.w) < 0.065) return ti === 0 ? BI.FROZEN_RIVER : BI.RIVER;
    }
    if (h <= SEA && !low6) {
      const deep = o.c < -0.455;
      return [deep ? BI.DEEP_FROZEN_OCEAN : BI.FROZEN_OCEAN, deep ? BI.DEEP_COLD_OCEAN : BI.COLD_OCEAN,
        deep ? BI.DEEP_OCEAN : BI.OCEAN, deep ? BI.DEEP_LUKEWARM_OCEAN : BI.LUKEWARM_OCEAN, BI.WARM_OCEAN][ti];
    }
    if (h <= SEA + 2 && o.pv > -0.35 && !low6) {
      if (ti === 0) return BI.SNOWY_BEACH;
      if (ti === 4) return BI.DESERT;
      if (o.e < -0.18) return BI.STONY_SHORE;
      if (this.nMisc.n2(x * 0.011 + 401.7, z * 0.011 - 233.1) * 0.5 + 0.5 > 0.35) return BI.BEACH;
    }
    // the same weirdness the terrain was shaped with (Minecraft: peaks / valleys and the biome
    // slices come from one value); the valley slice itself is the river, decided above
    let sw = clamp1(o.w);
    if (sw > -0.06 && sw < 0.06) sw = sw < 0 ? -0.06 : 0.06;
    const q = this._q || (this._q = new Float64Array(5));
    q[0] = o.t; q[1] = clamp1(o.hu * 0.82); q[2] = spline(SPL_CONT_PARAM, o.c < 0.05 ? 0.05 : o.c);
    // hot land is shifted toward higher erosion (deserts over badlands), but never into zone 6:
    // swamps / mangroves only where the terrain above really is the flat erosion-6 lowland
    const e15 = o.e * 1.5;
    q[3] = clamp1(o.t > 0.5 ? Math.min(e15 + 0.42, Math.max(e15, 0.5)) : e15); q[4] = sw;
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

  // ---------------------------------------------------------------- surface rules (maincraft)
  // Top block of a column by the original's rules; y = top solid block, h = analytic height.
  surfTop(ob, x, z, y, h) {
    const V = this.vn;
    let r;
    switch (ob) {
      case BI.DESERT: case BI.BEACH: case BI.WARM_OCEAN: case BI.LUKEWARM_OCEAN: r = B.SAND; break;
      case BI.SNOWY_BEACH: r = B.SAND; break;
      case BI.FROZEN_RIVER: r = y >= SEA ? B.GRASS : this.floorMix(x, z, y); break;
      case BI.BADLANDS: r = B.RED_SAND; break;
      case BI.WOODED_BADLANDS: r = h >= SEA + 10 ? B.GRASS : B.RED_SAND; break;
      case BI.MUSHROOM_FIELDS: r = B.MYCELIUM; break;
      case BI.MANGROVE_SWAMP: r = B.MUD; break;
      case BI.STONY_SHORE: case BI.STONY_PEAKS: r = B.STONE; break;
      case BI.COLD_OCEAN: case BI.FROZEN_OCEAN: r = B.GRAVEL; break;
      case BI.OCEAN: case BI.RIVER: r = y >= SEA ? B.GRASS : this.floorMix(x, z, y); break;
      case BI.SNOWY_SLOPES: case BI.JAGGED_PEAKS: case BI.GROVE: case BI.ICE_SPIKES: r = B.SNOW; break;
      case BI.FROZEN_PEAKS: r = V.f2(x * 0.1 + 3, z * 0.1 - 3) < 0.46 ? B.PACKED_ICE : B.SNOW; break;
      case BI.WINDSWEPT_HILLS: { const n = this.windswept(x, z, y); r = n < 0.55 ? B.GRASS : n < 0.68 ? B.STONE : B.GRAVEL; break; }
      case BI.OLD_GROWTH_SPRUCE_TAIGA: r = V.f2(x * 0.09 - 13, z * 0.09 + 13) < 0.58 ? B.PODZOL : B.GRASS; break;
      case BI.BAMBOO_JUNGLE: r = V.f2(x * 0.12 - 3, z * 0.12 + 7) < 0.4 ? B.PODZOL : B.GRASS; break;
      default: r = B.GRASS;
    }
    // soil below sea level (lake / river / ocean floor of a land biome)
    if (y < SEA && (r === B.GRASS || r === B.PODZOL || r === B.MYCELIUM)) r = this.floorMix(x, z, y);
    return r;
  }
  // Minecraft's swamp surface rule (see surface()): the top block at the water line is water
  poolAt(ob, x, z, y) {
    return ((ob === BI.SWAMP && y === SEA) || (ob === BI.MANGROVE_SWAMP && y >= SEA - 2 && y <= SEA)) &&
      this.nSurf.n2(x / 11 + 71.3, z / 11 - 19.7) > 0;
  }
  floorMix(x, z, y) {
    const V = this.vn;
    if (V.f2(x * 0.11 + 311.2, z * 0.11 - 177.6) > 0.7) return B.CLAY;
    if (SEA - y >= 3) { const t = V.f2(x * 0.05 + 777.7, z * 0.05 - 333.3); return t > 0.66 ? B.GRAVEL : t < 0.3 ? B.DIRT : B.SAND; }
    return B.SAND;
  }
  // windswept surface noise: grass < 0.55 < stone < 0.68 < gravel; no trees from 0.53
  windswept(x, z, y) { return this.vn.f2(x * 0.11 + 21, z * 0.11 - 21) + (y - SEA) * 0.004; }
  // the 3 filler blocks under the top (0 = keep stone)
  fillBlock(ob, top, x, y, z) {
    switch (ob) {
      case BI.BADLANDS: return this.band(x, y, z);
      case BI.WOODED_BADLANDS: return top === B.GRASS ? B.DIRT : this.band(x, y, z);
      case BI.DESERT: return B.SANDSTONE;
      case BI.MANGROVE_SWAMP: return B.MUD;
      case BI.BEACH: case BI.SNOWY_BEACH: case BI.OCEAN: case BI.RIVER: case BI.WARM_OCEAN: case BI.LUKEWARM_OCEAN: return B.SAND;
      case BI.FROZEN_RIVER: return top === B.GRASS ? B.DIRT : B.SAND;
      case BI.STONY_PEAKS: case BI.JAGGED_PEAKS: case BI.SNOWY_SLOPES: case BI.FROZEN_PEAKS: case BI.STONY_SHORE:
      case BI.COLD_OCEAN: case BI.FROZEN_OCEAN: return 0;
      default: return B.DIRT;
    }
  }
  band(x, y, z) {
    const n = TERRA_BANDS.length;
    return TERRA_BANDS[((y + Math.floor(this.nMisc.n2(x / 60, z / 60) * 3)) % n + n) % n];
  }
  // Minecraft-style refinement kept on top of the original rules: steep slopes of mountain biomes
  // are bare stone (snowy peaks keep their snow above y 150). Trees use the same predicate.
  bareSlope(bio, x, z, y) {
    const P = BPROP[bio], b = P.base;
    if (!(P.mountain || b === BI.WINDSWEPT_HILLS || b === BI.MEADOW || b === BI.GROVE || b === BI.CHERRY_GROVE)) return false;
    if (P.snow && y > 150) return false;
    const s = Math.max(Math.abs(this.colInfo(x + 1, z).h - this.colInfo(x - 1, z).h), Math.abs(this.colInfo(x, z + 1).h - this.colInfo(x, z - 1).h));
    return s > 3.2;
  }

  surface() {
    const ids = this.ids, x0 = this.x0, z0 = this.z0;
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const bio = this.BIO[z * 16 + x], P = BPROP[bio], ob = ORIG_BIOME[bio];
      let y = WORLD_MAX_Y - 1;
      while (y > WORLD_MIN_Y && !this.isStoneLike(ids[CI(x, y, z)])) y--;
      this.TOP[z * 16 + x] = y;
      if (y <= WORLD_MIN_Y) continue;
      const underwater = ids[CI(x, y + 1, z)] === B.WATER;
      const wx = x0 + x, wz = z0 + z, h = this.colInfo(wx, wz).h;
      let top = this.surfTop(ob, wx, wz, y, h), fill = -1;
      if (!underwater) {
        const sn = this.nSurf.n2(wx / 16, wz / 16);
        if (this.bareSlope(bio, wx, wz, y)) top = fill = B.STONE;
        if (ob === BI.STONY_PEAKS && sn > 0.45) top = fill = B.CALCITE;
      }
      // Minecraft's swamp surface rule: ground at the water line becomes water where the swamp
      // noise is positive (pools); in mangrove swamps from two blocks lower
      if (this.poolAt(ob, wx, wz, y)) {
        ids[CI(x, y, z)] = B.WATER;
        y--; this.TOP[z * 16 + x] = y;
        top = ob === BI.SWAMP ? B.DIRT : B.MUD;
      }
      ids[CI(x, y, z)] = top;
      for (let d = 1; d <= 3; d++) {
        const i = CI(x, y - d, z);
        if (!this.isStoneLike(ids[i])) break;
        const f = fill >= 0 ? fill : this.fillBlock(ob, top, wx, y - d, wz);
        if (f) ids[i] = f;
      }
      // badlands: the terracotta bands continue down the exposed cliff, as in Minecraft
      if (P.badlands && !underwater && top !== B.STONE) {
        for (let yy = y - 4; yy > SEA - 8 && yy > y - 60; yy--) {
          const i = CI(x, yy, z);
          if (ids[i] !== B.STONE) break;
          ids[i] = this.band(wx, yy, wz);
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
        // water below sea level is decided afterwards by aquifer()
        ids[i] = y <= -55 ? B.LAVA : 0;
        this.meta[i] = 0;
      }
      // a cave that opened the surface exposes filler (dirt, sand...): the new top gets the biome's
      // surface block, as if the surface rules had run after carving (Minecraft's order)
      let ny = WORLD_MAX_Y - 1;
      while (ny > WORLD_MIN_Y && (ids[CI(x, ny, z)] === 0)) ny--;
      const nt = ids[CI(x, ny, z)];
      if (ny >= SEA && ny < top && (nt === B.DIRT || nt === B.SAND || nt === B.SANDSTONE || nt === B.MUD)) {
        const bio = this.BIO[z * 16 + x], wx = x0 + x, wz = z0 + z;
        const t2 = this.bareSlope(bio, wx, wz, ny) ? B.STONE : this.surfTop(ORIG_BIOME[bio], wx, wz, ny, this.colInfo(wx, wz).h);
        if (t2 !== B.STONE || nt === B.DIRT) ids[CI(x, ny, z)] = t2;
      }
      TOP[z * 16 + x] = ids[CI(x, ny, z)] === B.WATER ? top : ny;
    }
    this.aquifer();
  }

  // Simplified Minecraft aquifer: open space below sea level holds water only near the surface
  // (within 12 blocks under the column's terrain height, which covers seas, shores and the caves
  // just under them); deeper cavities stay dry. Where a dry cave meets that water zone a stone
  // barrier is left, so water never stands against air. Depends only on coordinates, so
  // neighbouring chunks agree.
  aquifer() {
    const ids = this.ids, x0 = this.x0, z0 = this.z0;
    const lo = this.aqLo || (this.aqLo = new Int16Array(18 * 18));
    for (let z = -1; z <= 16; z++) for (let x = -1; x <= 16; x++) lo[(z + 1) * 18 + x + 1] = Math.ceil(this.colInfo(x0 + x, z0 + z).h) - 12;
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const k = (z + 1) * 18 + x + 1, L = lo[k];
      const side = Math.min(lo[k - 1], lo[k + 1], lo[k - 18], lo[k + 18]);
      for (let y = SEA; y > -55; y--) {
        const i = CI(x, y, z), id = ids[i];
        if (id !== 0 && id !== B.WATER) continue;
        if (y >= L) { ids[i] = B.WATER; continue; }
        // below this column's zone: a barrier where the zone above or beside it holds water
        ids[i] = y === L - 1 || y >= side ? (y < 0 ? B.DEEPSLATE : B.STONE) : 0;
      }
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

  // ---------------------------------------------------------------- trees
  // Tree candidate at column x,z from the original's per-biome table (jittered grid cell + weighted
  // types). Only seed-derived data is used, so every chunk a tree overlaps makes the same decision.
  // The spacing check asks every column for its neighbours' candidates, so results are cached.
  treeCandidate(x, z) {
    const k = x * 131072 + z, C = this.candCache;
    let v = C.get(k);
    if (v === undefined) { v = this.treeCandidate0(x, z); if (C.size > 200000) C.clear(); C.set(k, v); }
    return v;
  }
  treeCandidate0(x, z) {
    const ci = this.colInfo(x, z), ob = ORIG_BIOME[ci.biome];
    const r0 = hash2(this.seed + 11, x * 3 + 11, z * 3 - 7);
    if (ob === BI.DESERT) return r0 < 0.006 ? 'cactus' : 0;
    if (ob === BI.MUSHROOM_FIELDS) return r0 < 0.004 ? 'giant_red_mushroom' : r0 < 0.008 ? 'giant_brown_mushroom' : 0;
    if (ob === BI.ICE_SPIKES) return r0 < 0.01 ? 'ice_spike' : 0;
    const T = TREE_TABLE[ob];
    if (!T) return 0;
    const cell = T[0], gx = Math.floor(x / cell), gz = Math.floor(z / cell);
    if (gx * cell + ((hash2(this.seed + 21, gx, gz) * cell) | 0) !== x || gz * cell + ((hash2(this.seed + 22, gx, gz) * cell) | 0) !== z) return 0;
    if (ob === BI.WOODED_BADLANDS && ci.h < SEA + 10) return 0;
    if (ob === BI.WINDSWEPT_HILLS && this.windswept(x, z, ci.h) >= 0.53) return 0;
    const r = hash2(this.seed + 23, gx, gz);
    for (const t of T[1]) if (r < t[0]) return t[1];
    return 0;
  }
  // Spacing: a candidate gives way to a higher-priority candidate closer than the room both
  // need (trunks never grow through a neighbour's crown, cacti never touch). Deterministic.
  treeAt(x, z) {
    const t = this.treeCandidate(x, z);
    if (!t) return 0;
    const rk = hash2(this.seed + 24, x, z), rt = TREE_ROOM[t] || 4;
    for (let dz = -4; dz <= 4; dz++) for (let dx = -4; dx <= 4; dx++) {
      if (!dx && !dz) continue;
      const d = Math.max(dx < 0 ? -dx : dx, dz < 0 ? -dz : dz);
      const tn = this.treeCandidate(x + dx, z + dz);
      if (!tn || d >= Math.max(rt, TREE_ROOM[tn] || 4)) continue;
      if (hash2(this.seed + 24, x + dx, z + dz) < rk) return 0;
    }
    return t;
  }
  // exact terrain top (density surface, before caves) with a small cache
  groundAt(x, z) {
    const k = x * 131072 + z;
    let v = this.gCache.get(k);
    if (v === undefined) { v = this.surfaceAt(x, z); if (this.gCache.size > 40000) this.gCache.clear(); this.gCache.set(k, v); }
    return v;
  }
  treeSpot(x, z) {
    const t = this.treeAt(x, z);
    if (!t) return null;
    let sy = this.groundAt(x, z);
    if (sy >= WORLD_MAX_Y - 16) return null;
    const ci = this.colInfo(x, z), bio = ci.biome, ob = ORIG_BIOME[bio];
    // the block under the trunk, exactly as the surface rules will make it
    let top;
    if (this.poolAt(ob, x, z, sy)) { sy--; top = ob === BI.SWAMP ? B.DIRT : B.MUD; }
    else top = this.bareSlope(bio, x, z, sy) ? B.STONE : this.surfTop(ob, x, z, sy, ci.h);
    // most trees need dry land; swamp oaks and mangroves also grow in shallow water
    if (sy <= SEA && SEA - sy > (TREE_WATER[t] ?? -1)) return null;
    if (sy <= SEA && t === 'swamp_oak' && top !== B.DIRT && top !== B.SAND && top !== B.CLAY) return null;
    if (t === 'cactus') { if (top !== B.SAND && top !== B.RED_SAND) return null; }
    else if (t === 'ice_spike') { if (top !== B.SNOW) return null; }
    else if (t === 'mangrove' || t === 'tall_mangrove') { if (top !== B.MUD) return null; }
    else if (sy > SEA && top !== B.GRASS && top !== B.PODZOL && top !== B.SNOW && top !== B.MYCELIUM && top !== B.MUD && top !== B.DIRT) return null;
    if (t === 'dark_oak') {
      // 2x2 trunk: every column needs ground no more than 3 blocks below the origin
      for (const [dx, dz] of [[1, 0], [0, 1], [1, 1]]) { const g = this.groundAt(x + dx, z + dz); if (g < sy - 3 || g > sy + 3) return null; }
    }
    return { t, sy };
  }

  features() {
    const x0 = this.x0, z0 = this.z0, M = 6;
    this.deco.length = 0; this.treeWrites = [];
    for (let z = z0 - M; z < z0 + 16 + M; z++) for (let x = x0 - M; x < x0 + 16 + M; x++) {
      // a tree reaches several chunks: build it once, then every chunk takes its part
      const k = x * 131072 + z, TC = this.treeCache;
      let r = TC.get(k);
      if (r === undefined) {
        const spot = this.treeSpot(x, z);
        r = spot ? this.buildTree(spot.t, x, spot.sy, z) : null;
        if (TC.size > 60000) TC.clear();
        TC.set(k, r);
      }
      this.placeTree(r);
    }
    this.flushTrees();
    this.vegetation();
    this.snowCover();
  }

  // Trees are assembled in a buffer first (logs forced, crown blocks only where free), then crown
  // blocks that the terrain cuts off from the trunk (face path through the crown longer than 6,
  // Minecraft's leaf distance) are dropped, so nothing hangs in the air. Terrain is taken from the
  // density surface, which every chunk computes identically.
  growTree(type, x, sy, z) { this.placeTree(this.buildTree(type, x, sy, z)); }
  // Builds the whole tree independent of the chunk: {w: [x,y,z,id,kind]..., d: decorations} or
  // null when it cannot grow (mangrove roots finding no ground). Deterministic per seed and spot.
  buildTree(type, x, sy, z) {
    const TB = this.tb;
    TB.n = 0; TB.map.clear();
    const key = (xx, yy, zz) => ((yy - WORLD_MIN_Y) * 64 + (xx - x + 32)) * 64 + (zz - z + 32);
    const put = (xx, yy, zz, id, kind) => {
      if (yy <= WORLD_MIN_Y || yy >= WORLD_MAX_Y) return;
      const k = key(xx, yy, zz), j = TB.map.get(k);
      // forced blocks (0 trunk, 3 roots) win over crown and soil
      if (j !== undefined) { if ((kind === 0 || kind === 3) && TB.kind[j] !== 0 && TB.kind[j] !== 3) { TB.id[j] = id; TB.kind[j] = kind; } return; }
      const n = TB.n++;
      TB.x[n] = xx; TB.y[n] = yy; TB.z[n] = zz; TB.id[n] = id; TB.kind[n] = kind; TB.map.set(k, n);
    };
    const log = (xx, yy, zz, id) => put(xx, yy, zz, id, 0);          // 0: trunk (forced)
    const root = (xx, yy, zz, id) => put(xx, yy, zz, id, 3);         // 3: roots (forced, but leaves do not count them as logs)
    const soil = (xx, yy, zz) => put(xx, yy, zz, B.DIRT, 2);         // 2: soil under a trunk
    const layer = (cx, yy, cz, r, id, ragged) => {                    // 1: crown
      for (let dx = -r; dx <= r; dx++) for (let dz = -r; dz <= r; dz++) {
        const ax = dx < 0 ? -dx : dx, az = dz < 0 ? -dz : dz;
        if (r >= 2 && ax === r && az === r && (!ragged || hash3(this.seed + 98, cx + dx, yy, cz + dz) < 0.6)) continue;
        if (r >= 3 && ax + az > r + 1) continue;
        put(cx + dx, yy, cz + dz, id, 1);
      }
    };
    const g = (k) => hash2(this.seed + 97, x * 31 + k, z * 17 - k);
    // sequential random numbers for the ported Minecraft placers (seeded by the tree position, so
    // every chunk the tree reaches builds it identically)
    let rs = (hash2(this.seed + 131, x, z) * 4294967296) >>> 0;
    const rnd = () => { rs = (rs + 0x6D2B79F5) | 0; let t = Math.imul(rs ^ (rs >>> 15), 1 | rs); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
    const nextInt = (n) => (rnd() * n) | 0;
    let vineChance = 0, propagules = false, mossOnRoots = false;
    switch (type) {
      case 'cactus': {
        const n = 2 + ((g(1) * 2) | 0);
        for (let y = sy + 1; y <= sy + n; y++) log(x, y, z, B.CACTUS);
        break;
      }
      case 'spruce': case 'tall_spruce': {
        const tall = type === 'tall_spruce';
        const top = sy + (tall ? 10 + ((g(2) * 4) | 0) : 6 + ((g(3) * 3) | 0));
        for (let y = sy + 1; y <= top; y++) log(x, y, z, B.SPRUCE_LOG);
        put(x, top + 1, z, B.SPRUCE_LEAVES, 1);
        const n = tall ? 8 : 5;
        for (let u = 0; u < n; u++) {
          const y = top - u;
          if (y <= sy + 2) break;
          layer(x, y, z, u === 0 || u % 2 === 1 ? 1 : 2, B.SPRUCE_LEAVES, false);
        }
        soil(x, sy, z);
        break;
      }
      case 'dark_oak': {
        const top = sy + 7 + ((g(4) * 3) | 0);
        // each trunk column reaches down to its own ground
        for (let d = 0; d < 4; d++) {
          const cx = x + (d & 1), cz = z + (d >> 1), gy = d ? this.groundAt(cx, cz) : sy;
          for (let y = gy + 1; y <= top; y++) log(cx, y, cz, B.DARK_LOG);
          soil(cx, gy, cz);
        }
        for (let u = -2; u <= 1; u++) if (top + u > sy) layer(x, top + u, z, u <= 0 ? 3 : 2, B.DARK_LEAVES, true);
        break;
      }
      case 'jungle': {
        const top = sy + 8 + ((g(5) * 5) | 0);
        for (let y = sy + 1; y <= top; y++) log(x, y, z, B.JUNGLE_LOG);
        layer(x, top + 1, z, 1, B.JUNGLE_LEAVES, false);
        layer(x, top, z, 2, B.JUNGLE_LEAVES, true);
        layer(x, top - 1, z, 2, B.JUNGLE_LEAVES, true);
        soil(x, sy, z);
        break;
      }
      case 'acacia': {
        const n = 5 + ((g(6) * 2) | 0), dir = (g(7) * 4) | 0;
        const ddx = [1, -1, 0, 0][dir], ddz = [0, 0, 1, -1][dir];
        let cx = x, cz = z;
        for (let y = sy + 1; y <= sy + n; y++) {
          if (y > sy + 2 && (y - sy) % 2 === 1) { cx += ddx; cz += ddz; }
          log(cx, y, cz, B.ACACIA_LOG);
        }
        layer(cx, sy + n, cz, 3, B.ACACIA_LEAVES, false);
        layer(cx, sy + n + 1, cz, 2, B.ACACIA_LEAVES, true);
        soil(x, sy, z);
        break;
      }
      case 'fancy_oak': {
        const top = sy + 7 + ((g(8) * 3) | 0), R = [2, 3, 3, 2, 1];
        for (let y = sy + 1; y <= top; y++) log(x, y, z, B.LOG);
        for (let u = 0; u < R.length; u++) { const y = top - 3 + u; if (y > sy) layer(x, y, z, R[u], B.LEAVES, true); }
        const k = (g(9) * 4) | 0;
        layer(x + [2, -2, 0, 0][k], top - 3, z + [0, 0, 2, -2][k], 1, B.LEAVES, false);
        soil(x, sy, z);
        break;
      }
      case 'cherry': {
        const top = sy + 4 + ((g(10) * 2) | 0), R = [2, 3, 2];
        for (let y = sy + 1; y <= top; y++) log(x, y, z, B.LOG);
        layer(x, top + 1, z, 1, B.FLOWERING_AZALEA_LEAVES, false);
        for (let u = 0; u < R.length; u++) {
          const y = top - u;
          if (y <= sy + 1) break;
          layer(x, y, z, R[u], hash3(this.seed + 96, x, y, z) < 0.25 ? B.AZALEA_LEAVES : B.FLOWERING_AZALEA_LEAVES, true);
        }
        soil(x, sy, z);
        break;
      }
      case 'giant_red_mushroom': {
        const n = 4 + ((g(11) * 3) | 0);
        for (let y = sy + 1; y <= sy + n; y++) log(x, y, z, B.MUSHROOM_STEM);
        layer(x, sy + n + 1, z, 1, B.RED_MUSHROOM_BLOCK, false);
        layer(x, sy + n, z, 2, B.RED_MUSHROOM_BLOCK, true);
        break;
      }
      case 'giant_brown_mushroom': {
        const n = 3 + ((g(12) * 2) | 0);
        for (let y = sy + 1; y <= sy + n; y++) log(x, y, z, B.MUSHROOM_STEM);
        layer(x, sy + n + 1, z, 3, B.BROWN_MUSHROOM_BLOCK, false);
        break;
      }
      case 'ice_spike': {
        const n = 4 + ((g(13) * 7) | 0);
        for (let y = sy + 1; y <= sy + n; y++) log(x, y, z, B.PACKED_ICE);
        // two base blocks for taller spikes, only where the ground is not lower
        if (n > 5) {
          if (this.groundAt(x + 1, z) >= sy) log(x + 1, sy + 1, z, B.PACKED_ICE);
          if (this.groundAt(x, z + 1) >= sy) log(x, sy + 1, z + 1, B.PACKED_ICE);
        }
        break;
      }
      case 'swamp_oak': {
        // Minecraft: straight trunk 5 + 0..3, blob foliage radius 3 / height 3, vines on the leaves
        const top = sy + 5 + nextInt(4);
        for (let y = sy + 1; y <= top; y++) log(x, y, z, B.LOG);
        for (let i = 0; i >= -3; i--) {
          const r = Math.max(2 - Math.trunc(i / 2), 0), yy = top + 1 + i;
          for (let dx = -r; dx <= r; dx++) for (let dz = -r; dz <= r; dz++) {
            if (Math.abs(dx) === r && Math.abs(dz) === r && (i === 0 || nextInt(2) === 0)) continue;
            put(x + dx, yy, z + dz, B.LEAVES, 1);
          }
        }
        soil(x, sy, z);
        vineChance = 0.25;
        break;
      }
      case 'mangrove': case 'tall_mangrove': {
        // Minecraft's mangrove: MangroveRootPlacer + UpwardsBranchingTrunkPlacer +
        // RandomSpreadFoliagePlacer(3, 0, 2, 70), vines and hanging propagules
        const tall = type === 'tall_mangrove';
        const oy = sy + 1 + 1 + nextInt(3);                     // trunk origin 1..3 above the sapling spot
        // terrain the roots may grow into: air, water and the mud layers of a mangrove swamp
        const rootCell = (xx, yy, zz) => {
          const gy = this.groundAt(xx, zz);
          if (yy > gy) return 1;
          return ORIG_BIOME[this.colInfo(xx, zz).biome] === BI.MANGROVE_SWAMP && yy > gy - 4 ? 1 : 0;
        };
        for (let y = sy + 1; y < oy; y++) if (!rootCell(x, y, z)) return null;
        const roots = [[x, oy - 1, z]];
        const simulate = (px, py, pz, dx, dz, list, depth) => {
          if (depth === 15 || list.length > 15) return false;
          const dist = Math.abs(px - x) + Math.abs(py - oy) + Math.abs(pz - z);
          let next;
          if (dist > 5 && dist <= 8) next = rnd() < 0.2 ? [[px, py - 1, pz], [px + dx, py - 1, pz + dz]] : [[px, py - 1, pz]];
          else if (dist > 8) next = [[px, py - 1, pz]];
          else if (rnd() < 0.2) next = [[px, py - 1, pz]];
          else next = rnd() < 0.5 ? [[px + dx, py, pz + dz]] : [[px, py - 1, pz]];
          for (const p of next) {
            if (!rootCell(p[0], p[1], p[2])) continue;
            list.push(p);
            if (!simulate(p[0], p[1], p[2], dx, dz, list, depth + 1)) return false;
          }
          return true;
        };
        for (const [dx, dz] of [[0, -1], [0, 1], [-1, 0], [1, 0]]) {
          const list = [];
          if (!simulate(x + dx, oy, z + dz, dx, dz, list, 0)) return null;
          roots.push(...list, [x + dx, oy, z + dz]);
        }
        for (const [rx, ry, rz] of roots) {
          const gy = this.groundAt(rx, rz), ob = ORIG_BIOME[this.colInfo(rx, rz).biome];
          const wet = ry <= SEA && (ry > gy || (ry === gy && this.poolAt(ob, rx, rz, gy)));
          root(rx, ry, rz, ry <= gy && !wet ? B.MUDDY_MANGROVE_ROOTS : wet ? B.MANGROVE_ROOTS_WET : B.MANGROVE_ROOTS);
        }
        mossOnRoots = true;
        // trunk with upward branches; every branch log and the trunk top carry foliage
        const height = tall ? 4 + nextInt(2) + nextInt(10) : 2 + nextInt(2) + nextInt(5);
        const attach = [];
        for (let i = 0; i < height; i++) {
          const y = oy + i;
          log(x, y, z, B.MANGROVE_LOG);
          if (i < height - 1 && rnd() < 0.5) {
            const d = nextInt(4), bx = [0, 0, -1, 1][d], bz = [-1, 1, 0, 0][d];
            const k = nextInt(2), off = Math.max(0, k - nextInt(2) - 1), steps0 = 1 + nextInt(4);
            let cx = x, cz = z, top = y + off;
            for (let l = off, steps = steps0; l < height && steps > 0; l++, steps--) {
              if (l >= 1) {
                cx += bx; cz += bz;
                log(cx, y + l, cz, B.MANGROVE_LOG);
                top = y + l + 1;
                attach.push([cx, y + l, cz]);
              }
            }
            if (top - y > 1) attach.push([cx, top, cz], [cx, top - 2, cz]);
          }
        }
        attach.push([x, oy + height, z]);
        for (const [ax, ay, az] of attach) put(ax, ay, az, B.MANGROVE_LEAVES, 1);   // no bare trunk / branch tips
        for (const [ax, ay, az] of attach) for (let n = 0; n < 70; n++) {
          put(ax + nextInt(3) - nextInt(3), ay + nextInt(2) - nextInt(2), az + nextInt(3) - nextInt(3), B.MANGROVE_LEAVES, 1);
        }
        vineChance = 0.125; propagules = true;
        break;
      }
      default: { // oak / birch
        const birch = type === 'birch';
        const logId = birch ? B.BIRCH_LOG : B.LOG, leafId = birch ? B.BIRCH_LEAVES : B.LEAVES;
        const top = sy + 4 + ((g(14) * 3) | 0) + (birch ? 1 : 0);
        for (let y = sy + 1; y <= top; y++) log(x, y, z, logId);
        for (let u = -2; u <= 1; u++) { const y = top + u; if (y > sy) layer(x, y, z, u <= -1 ? 2 : 1, leafId, true); }
        soil(x, sy, z);
      }
    }
    // crown connectivity (face neighbours) from the trunk through free crown cells
    const n = TB.n, dist = TB.dist, q = TB.q;
    let qh = 0, qt = 0;
    for (let j = 0; j < n; j++) {
      if (TB.kind[j] === 0) { dist[j] = 0; q[qt++] = j; }
      else dist[j] = 255;
    }
    while (qh < qt) {
      const j = q[qh++], dj = dist[j];
      if (dj >= 6) continue;
      for (let f = 0; f < 6; f++) {
        const xx = TB.x[j] + FACE6[f * 3], yy = TB.y[j] + FACE6[f * 3 + 1], zz = TB.z[j] + FACE6[f * 3 + 2];
        const m = TB.map.get(key(xx, yy, zz));
        if (m === undefined || TB.kind[m] !== 1 || dist[m] !== 255 || !(FLAGS[TB.id[m]] & BF_LEAVES)) continue;
        if (this.solidTerrain(xx, yy, zz)) { dist[m] = 254; continue; }
        dist[m] = dj + 1; q[qt++] = m;
      }
    }
    // decorations on the surviving leaves: Minecraft's leave vine decorator (vines hanging down
    // up to five blocks) and hanging propagules under mangrove leaves (two free blocks below)
    const deco = [];
    if (vineChance || propagules || mossOnRoots) {
      const taken = new Set(), props = new Set();
      const free = (xx, yy, zz) => {
        const k = key(xx, yy, zz);
        // air only: below sea level every non-terrain cell is water
        return yy > SEA && !TB.map.has(k) && !taken.has(k) && !this.solidTerrain(xx, yy, zz);
      };
      const hang = [[-1, 0, 8], [1, 0, 2], [0, -1, 1], [0, 1, 4]];     // neighbour offset, vine side bit facing the leaf
      // Minecraft's above-root placement: moss carpet on half of the roots that reach the open air
      if (mossOnRoots) for (let j = 0; j < n; j++) {
        if (TB.kind[j] !== 3) continue;
        const rx = TB.x[j], ry = TB.y[j] + 1, rz = TB.z[j];
        if (hash3(this.seed + 133, rx, ry, rz) < 0.5 && free(rx, ry, rz)) { deco.push(rx, ry, rz, B.MOSS_CARPET, 0, rx, ry - 1, rz); taken.add(key(rx, ry, rz)); }
      }
      for (let j = 0; j < n; j++) {
        if (TB.kind[j] !== 1 || dist[j] > 6 || !(FLAGS[TB.id[j]] & BF_LEAVES)) continue;
        const lx = TB.x[j], ly = TB.y[j], lz = TB.z[j];
        if (vineChance) for (let s = 0; s < 4; s++) {
          if (hash3(this.seed + 134 + s, lx, ly, lz) >= vineChance) continue;
          const vx = lx + hang[s][0], vz = lz + hang[s][1];
          for (let yy = ly; yy > ly - 5 && free(vx, yy, vz); yy--) {
            if (yy === ly) deco.push(vx, yy, vz, B.VINE, hang[s][2], lx, ly, lz);   // held by the leaf
            else deco.push(vx, yy, vz, B.VINE, hang[s][2], vx, yy + 1, vz);        // by the vine above
            taken.add(key(vx, yy, vz));
          }
        }
        if (propagules && hash3(this.seed + 139, lx, ly, lz) < 0.14 && free(lx, ly - 1, lz) && free(lx, ly - 2, lz)) {
          // no other propagule in the 3x3 around it at the same height
          let near = false;
          for (let dx = -1; dx <= 1 && !near; dx++) for (let dz = -1; dz <= 1; dz++) if (props.has(key(lx + dx, ly - 1, lz + dz))) { near = true; break; }
          if (!near) { deco.push(lx, ly - 1, lz, B.MANGROVE_PROPAGULE, 0, lx, ly, lz); taken.add(key(lx, ly - 1, lz)); props.add(key(lx, ly - 1, lz)); }
        }
      }
    }
    const out = [];
    for (let j = 0; j < n; j++) {
      const kind = TB.kind[j];
      if (kind === 1 && dist[j] > 6) continue;
      out.push(TB.x[j], TB.y[j], TB.z[j], TB.id[j], kind);
    }
    return { w: Int32Array.from(out), d: Int32Array.from(deco) };
  }
  // The part of a built tree inside this chunk: queued while features() runs, so all trees of the
  // chunk are written phase by phase; written at once otherwise.
  placeTree(r) {
    if (!r) return;
    const W = this.treeWrites, w = r.w, x0 = this.x0, z0 = this.z0;
    for (let i = 0; i < w.length; i += 5) {
      const xx = w[i], zz = w[i + 2];
      if (xx < x0 || xx > x0 + 15 || zz < z0 || zz > z0 + 15) continue;
      if (W) W.push(xx, w[i + 1], zz, w[i + 3], w[i + 4]); else this.writeTreeBlock(xx, w[i + 1], zz, w[i + 3], w[i + 4]);
    }
    for (let i = 0; i < r.d.length; i++) this.deco.push(r.d[i]);
    if (!W) { this.treeDecorations(); this.deco.length = 0; }
  }
  // phase order: 0 trunks (forced), 1 crowns (free cells only; a leaf next to any tree's log is
  // held), 2 soil under trunks, 3 roots (never over a tree's logs or leaves)
  writeTreeBlock(xx, yy, zz, id, kind) {
    if (kind === 0) this.setB(xx, yy, zz, id, 0, 2);
    else if (kind === 1) this.setB(xx, yy, zz, id, 0, 0);
    else if (kind === 3) {
      const cur = this.getB(xx, yy, zz);
      if (cur >= 0 && !(FLAGS[cur] & BF_LEAVES) && !/_LOG$|^LOG$|ROOTS/.test(B_KEY[cur] || '')) this.setB(xx, yy, zz, id, 0, 2);
    } else {
      const cur = this.getB(xx, yy, zz);
      if (cur >= 0 && cur !== B.GRASS && cur !== B.DIRT && cur !== B.PODZOL && cur !== B.COARSE_DIRT && cur !== B.MUD && cur !== B.SNOW) this.setB(xx, yy, zz, B.DIRT, 0, 2);
    }
  }
  flushTrees() {
    const W = this.treeWrites;
    for (const phase of [0, 1, 2, 3]) for (let i = 0; i < W.length; i += 5) if (W[i + 4] === phase) this.writeTreeBlock(W[i], W[i + 1], W[i + 2], W[i + 3], phase);
    this.treeWrites = null;
    this.treeDecorations();
    this.deco.length = 0;
  }
  // Tree decorations (vines, propagules, moss on roots) go in after every tree of the chunk, so one
  // tree's decoration never takes a cell another tree's crown needs; each only where its support
  // really ended up (a support in a neighbour chunk was decided from the same tree data there).
  treeDecorations() {
    const deco = this.deco;
    for (let i = 0; i < deco.length; i += 8) {
      const sup = this.getB(deco[i + 5], deco[i + 6], deco[i + 7]), id = deco[i + 3];
      if (sup >= 0) {
        const ok = id === B.MOSS_CARPET ? /ROOTS/.test(B_KEY[sup] || '') : id === B.VINE && deco[i + 6] > deco[i + 1] ? sup === B.VINE : (FLAGS[sup] & BF_LEAVES) !== 0;
        if (!ok) continue;
      }
      this.setB(deco[i], deco[i + 1], deco[i + 2], id, deco[i + 4], 0);
    }
  }
  // terrain occupancy used for crown connectivity (density surface; air well above the height)
  solidTerrain(x, y, z) {
    if (y > this.colInfo(x, z).h + 7) return false;
    return y <= this.groundAt(x, z);
  }

  // ---------------------------------------------------------------- vegetation (maincraft rules)
  vegetation() {
    const ids = this.ids, meta = this.meta, x0 = this.x0, z0 = this.z0, TOP = this.TOP;
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const y = TOP[z * 16 + x];
      if (y <= WORLD_MIN_Y + 1 || y >= WORLD_MAX_Y - 3) continue;
      const bio = this.BIO[z * 16 + x], ob = ORIG_BIOME[bio], wx = x0 + x, wz = z0 + z;
      const above = ids[CI(x, y + 1, z)];
      if (y < SEA) { if (above === B.WATER) this.waterDecor(x, z, y, ob); continue; }
      if (above !== 0) continue;
      const ground = ids[CI(x, y, z)];
      if (ground === B.GRASS && hash2(this.seed + 33, wx * 17 + 101, wz * 17 - 59) < 13e-5) { ids[CI(x, y + 1, z)] = B.PUMPKIN; continue; }
      if (ground === B.GRASS && (ob === BI.JUNGLE || ob === BI.SPARSE_JUNGLE || ob === BI.BAMBOO_JUNGLE) &&
        hash2(this.seed + 34, wx * 19 - 71, wz * 19 + 43) < 0.0016) { ids[CI(x, y + 1, z)] = B.MELON; continue; }
      const plant = this.plantAt(bio, ob, x, z, wx, wz, y);
      if (!plant) continue;
      const mush = plant === B.BROWN_MUSHROOM || plant === B.RED_MUSHROOM;
      const ok = ground === B.GRASS || ground === B.PODZOL ||
        ((ground === B.SAND || ground === B.RED_SAND) && (plant === B.DEAD_BUSH || plant === B.SUGAR_CANE)) ||
        (mush && (ground === B.MYCELIUM || ground === B.MUD)) ||
        ((plant === B.TALL_GRASS || plant === B.FERN) && ground === B.MUD) ||
        (plant === B.BAMBOO_PLANT && ground === B.DIRT);
      if (!ok) continue;
      const i1 = CI(x, y + 1, z);
      if (SHAPE[plant] === SH.TALL) {
        if (ids[CI(x, y + 2, z)] !== 0) continue;
        ids[i1] = plant; ids[i1 + 256] = plant; meta[i1 + 256] = 1;
      } else if (plant === B.SUGAR_CANE || plant === B.BAMBOO_PLANT) {
        const n = plant === B.SUGAR_CANE ? 2 + (hash2(this.seed + 35, wx * 13 + 5, wz * 13 - 5) < 0.4 ? 1 : 0) : 3 + ((hash2(this.seed + 36, wx * 11 - 4, wz * 11 + 6) * 4) | 0);
        for (let k = 0; k < n && y + 1 + k < WORLD_MAX_Y - 1 && ids[i1 + k * 256] === 0; k++) ids[i1 + k * 256] = plant;
      } else {
        ids[i1] = plant;
      }
    }
  }
  // Cold biomes (Minecraft's freeze-top-layer): open water at sea level freezes, and every top block
  // that can hold snow (ground, leaves, logs) under open sky gets a snow layer.
  // Minecraft's freeze-top-layer step: in cold biomes, and wherever the height-adjusted temperature
  // is below 0.15, the top water freezes and snow settles on the ground and on leaves
  frozenAt(bio, x, y, z) {
    if (BPROP[bio].cold === 2) return true;
    if (y <= 80) return BIOME_TEMP[bio] < 0.15;
    return BIOME_TEMP[bio] - ((this.vn.f2(x / 8, z / 8) - 0.5) * 16 + y - 80) * 0.00125 < 0.15;
  }
  snowCover() {
    const ids = this.ids, TOP = this.TOP;
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const bio = this.BIO[z * 16 + x], cold = BPROP[bio].cold === 2;
      if (!cold && BIOME_TEMP[bio] - (TOP[z * 16 + x] + 48 - 80) * 0.00125 >= 0.15) continue;   // cannot freeze here
      const s = CI(x, SEA, z);
      if (cold && ids[s] === B.WATER && ids[s + 256] === 0) ids[s] = B.ICE;
      let y = Math.min(WORLD_MAX_Y - 2, TOP[z * 16 + x] + 40);
      while (y > WORLD_MIN_Y && ids[CI(x, y, z)] === 0) y--;
      if (!cold && !this.frozenAt(bio, this.x0 + x, y + 1, this.z0 + z)) continue;
      const t = ids[CI(x, y, z)];
      if (y >= WORLD_MAX_Y - 2 || ids[CI(x, y + 1, z)] !== 0) continue;
      if (t === B.WATER) { ids[CI(x, y, z)] = B.ICE; continue; }           // a frozen lake or pool
      if (t === B.ICE || t === B.PACKED_ICE || t === B.SNOW || isWaterId(t)) continue;
      if (OPAQUE[t] || (FLAGS[t] & BF_LEAVES)) ids[CI(x, y + 1, z)] = B.SNOW_LAYER;
    }
  }
  // land plant for a column (the original's per-biome table)
  plantAt(bio, ob, lx, lz, x, z, y) {
    const r = hash2(this.seed + 31, x * 7 + 3, z * 7 - 5), b2 = hash2(this.seed + 32, x * 11 - 7, z * 11 + 13);
    const pick = a => a[Math.floor(b2 * a.length) % a.length];
    if (y === SEA && r < 0.32 && ob !== BI.DESERT && ob !== BI.BADLANDS && this.waterBeside(lx, lz, x, z)) return B.SUGAR_CANE;
    if (bio === BI.SUNFLOWER_PLAINS) {
      // the sunflower variant keeps its sunflowers (the original merges it into plains)
      if (r < 0.1) return B.TALL_GRASS;
      if (r < 0.14) return pick([B.DANDELION, B.POPPY, B.CORNFLOWER, B.OXEYE_DAISY, B.AZURE_BLUET]);
      if (r < 0.19) return B.SUNFLOWER;
      return 0;
    }
    switch (ob) {
      case BI.PLAINS:
        if (r < 0.1) return B.TALL_GRASS;
        if (r < 0.14) return pick([B.DANDELION, B.POPPY, B.CORNFLOWER, B.OXEYE_DAISY, B.AZURE_BLUET]);
        break;
      case BI.MEADOW:
        if (r < 0.14) return B.TALL_GRASS;
        if (r < 0.205) return pick([B.DANDELION, B.CORNFLOWER, B.OXEYE_DAISY, B.POPPY]);
        if (r < 0.21) return B.SUNFLOWER;
        break;
      case BI.FLOWER_FOREST:
        if (r < 0.07) return B.TALL_GRASS;
        if (r < 0.26) return pick([B.DANDELION, B.POPPY, B.ALLIUM, B.CORNFLOWER, B.OXEYE_DAISY, B.AZURE_BLUET, B.LILY_OF_THE_VALLEY,
          B.ORANGE_TULIP, B.PINK_TULIP, B.RED_TULIP, B.WHITE_TULIP]);
        if (r < 0.275) return b2 < 0.5 ? B.LILAC : B.ROSE_BUSH;
        break;
      case BI.FOREST:
        if (r < 0.06) return B.TALL_GRASS;
        if (r < 0.08) return B.FERN;
        if (r < 0.092) return b2 < 0.5 ? B.DANDELION : B.POPPY;
        if (r < 0.098) return pick([B.LILAC, B.ROSE_BUSH, B.PEONY]);
        break;
      case BI.BIRCH_FOREST:
        if (r < 0.06) return B.TALL_GRASS;
        if (r < 0.086) return b2 < 0.5 ? B.LILY_OF_THE_VALLEY : B.DANDELION;
        if (r < 0.093) return b2 < 0.5 ? B.LILAC : B.PEONY;
        break;
      case BI.DARK_FOREST:
        if (r < 0.05) return B.TALL_GRASS;
        if (r < 0.075) return b2 < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM;
        if (r < 0.09) return B.LARGE_FERN;
        if (r < 0.097) return b2 < 0.5 ? B.ROSE_BUSH : B.PEONY;
        break;
      case BI.JUNGLE:
        if (r < 0.12) return B.TALL_GRASS;
        if (r < 0.22) return B.FERN;
        break;
      case BI.SPARSE_JUNGLE:
        if (r < 0.14) return B.TALL_GRASS;
        if (r < 0.2) return B.FERN;
        break;
      case BI.TAIGA: case BI.OLD_GROWTH_SPRUCE_TAIGA: case BI.SNOWY_TAIGA: case BI.GROVE:
        if (r < 0.08) return B.FERN;
        if (r < 0.12) return b2 < 0.5 ? B.TALL_GRASS : B.LARGE_FERN;
        if (r < 0.135) return B.SWEET_BERRY_BUSH;
        break;
      case BI.SAVANNA:
        if (r < 0.25) return B.TALL_GRASS;
        break;
      case BI.SWAMP:
        if (r < 0.1) return B.TALL_GRASS;
        if (r < 0.13) return B.FERN;
        if (r < 0.145) return B.BLUE_ORCHID;
        break;
      case BI.WINDSWEPT_HILLS:
        if (r < 0.05) return B.TALL_GRASS;
        break;
      case BI.DESERT:
        if (r < 0.016) return B.DEAD_BUSH;
        break;
      case BI.BADLANDS:
        if (r < 0.02) return B.DEAD_BUSH;
        break;
      case BI.WOODED_BADLANDS:
        if (r < 0.04) return B.DEAD_BUSH;
        if (r < 0.08) return B.TALL_GRASS;
        break;
      case BI.CHERRY_GROVE:
        if (r < 0.14) return B.TALL_GRASS;
        if (r < 0.34) return pick([B.PINK_TULIP, B.ALLIUM, B.OXEYE_DAISY, B.DANDELION, B.PINK_TULIP]);
        break;
      case BI.BAMBOO_JUNGLE:
        if (r < 0.32) return B.BAMBOO_PLANT;
        if (r < 0.42) return B.FERN;
        if (r < 0.48) return B.TALL_GRASS;
        break;
      case BI.MUSHROOM_FIELDS:
        if (r < 0.22) return b2 < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM;
        break;
      case BI.MANGROVE_SWAMP:
        if (r < 0.08) return B.TALL_GRASS;
        if (r < 0.12) return B.FERN;
        break;
    }
    return 0;
  }
  // Sugar cane needs water right next to its ground block (Minecraft's rule, also the one the
  // game checks later). Ground is at sea level; a neighbour column is water there when its
  // density surface is below sea level and its biome is not a freezing one.
  waterBeside(lx, lz, x, z) {
    for (let f = 0; f < 4; f++) {
      const dx = FACE4[f * 2], dz = FACE4[f * 2 + 1], nx = lx + dx, nz = lz + dz;
      // water in a cold biome freezes over (snowCover), so it doesn't count
      if (nx >= 0 && nx < 16 && nz >= 0 && nz < 16) { if (this.ids[CI(nx, SEA, nz)] === B.WATER && BPROP[this.BIO[nz * 16 + nx]].cold !== 2) return true; continue; }
      if (this.groundAt(x + dx, z + dz) >= SEA) continue;
      if (BPROP[this.biomeAtBlock(x + dx, z + dz)].cold !== 2) return true;
    }
    return false;
  }
  // underwater decoration of the original (gw): icebergs and surface ice, coral reefs, kelp
  // forests, seagrass, lily pads. fy = floor block, water above it.
  waterDecor(lx, lz, fy, ob) {
    const ids = this.ids, meta = this.meta, V = this.vn, x = this.x0 + lx, z = this.z0 + lz;
    const col = (y) => CI(lx, y, lz), n = col(fy + 1), surf = col(SEA), depth = SEA - fy;
    const i = hash2(this.seed + 41, x * 5 + 41, z * 5 - 23), a = hash2(this.seed + 42, x * 9 - 17, z * 9 + 31);
    switch (ob) {
      case BI.FROZEN_OCEAN: {
        const c = V.f2(x * 0.02 + 911.1, z * 0.02 - 333.3);
        if (c > 0.72 && depth >= 3) {
          const f = Math.min(1, (c - 0.72) / 0.28), top = SEA + Math.round(f * 6);
          let bot = SEA - 1 - Math.round(f * 5);
          if (bot < fy + 1) bot = fy + 1;
          for (let y = bot; y <= top && y < WORLD_MAX_Y - 1; y++) ids[col(y)] = B.PACKED_ICE;
          if (top > SEA + 2 && ids[col(top + 1)] === 0) ids[col(top + 1)] = B.SNOW;
        } else if (ids[surf] === B.WATER) ids[surf] = B.ICE;
        return;
      }
      case BI.WARM_OCEAN: {
        if (ids[n] !== B.WATER) return;
        const v = V.f2(x * 0.03 + 123.4, z * 0.03 - 567.8);
        if (depth >= 4 && v > 0.72) {
          const d = Math.min(3, 1 + Math.floor((v - 0.72) * 14));
          let L = 0;
          for (; L < d && ids[n + L * 256] === B.WATER && fy + 2 + L < SEA; L++)
            ids[n + L * 256] = CORAL_BLOCKS[Math.floor(hash2(this.seed + 43, x * 3 + L * 7, z * 3 - L * 5) * 5) % 5];
          const t = n + L * 256;
          if (ids[t] === B.WATER) {
            if (a < 0.4) ids[t] = CORAL_FANS[Math.floor(a * 12) % 5];
            else if (a < 0.55) { ids[t] = B.SEA_PICKLE; meta[t] = Math.floor(i * 4) & 3; }
          }
          return;
        }
        if (i < 0.06) { ids[n] = B.SEA_PICKLE; meta[n] = Math.floor(a * 4) & 3; }
        else if (i < 0.17) ids[n] = CORAL_FANS[Math.floor(a * 5) % 5];
        else if (i < 0.34) ids[n] = B.SEAGRASS;
        return;
      }
      case BI.COLD_OCEAN: case BI.OCEAN: case BI.LUKEWARM_OCEAN: {
        if (ids[n] !== B.WATER || (depth >= 3 && this.kelp(lx, lz, fy, x, z, i))) return;
        if (i < (ob === BI.COLD_OCEAN ? 0.14 : ob === BI.LUKEWARM_OCEAN ? 0.3 : 0.24)) ids[n] = B.SEAGRASS;
        return;
      }
      case BI.RIVER:
        if (ids[n] === B.WATER && i < 0.12) ids[n] = B.SEAGRASS;
        return;
      case BI.SWAMP: case BI.MANGROVE_SWAMP:
        if (ids[n] === B.WATER && i < 0.1) ids[n] = B.SEAGRASS;
        if (ids[surf] === B.WATER && ids[col(SEA + 1)] === 0 && hash2(this.seed + 44, x * 7 - 9, z * 7 + 19) < 0.05) {
          ids[col(SEA + 1)] = B.LILY_PAD; meta[col(SEA + 1)] = (a * 4) | 0;
        }
        return;
    }
  }
  kelp(lx, lz, fy, x, z, i) {
    const r = this.vn.f2(x * 0.025 + 313.7, z * 0.025 - 191.3);
    if (r < 0.6) return false;
    if (i > 0.3 + (r - 0.6) / 0.4 * 0.55) return false;
    let n = SEA - 2 - fy;
    if (n > 25) n = 25;
    if (n < 1) return false;
    const top = Math.min(SEA - 1, fy + 1 + Math.floor(hash2(this.seed + 45, x * 9 - 3, z * 9 + 7) * n));
    for (let y = fy + 1; y <= top; y++) this.ids[CI(lx, y, lz)] = B.KELP;
    return true;
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
