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
  ['PALE_GARDEN', 'Pale Garden', 'Бледный сад'],
  // cave biomes: only underground (caveBiome), the surface map never holds them
  ['LUSH_CAVES', 'Lush Caves', 'Пышные пещеры'], ['DRIPSTONE_CAVES', 'Dripstone Caves', 'Капельниковые пещеры'], ['DEEP_DARK', 'Deep Dark', 'Глубокая тьма'],
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
    DARK_FOREST: 0.7, PALE_GARDEN: 0.7, LUSH_CAVES: 0.5, DRIPSTONE_CAVES: 0.8, DEEP_DARK: 0.8, BIRCH_FOREST: 0.6, OLD_GROWTH_BIRCH_FOREST: 0.6, TAIGA: 0.25, OLD_GROWTH_PINE_TAIGA: 0.3,
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
    PALE_GARDEN: [0x8A9682, 0x939C88],   // own: a washed-out grey green
  };
  const W = {
    SWAMP: 0x617B64, MANGROVE_SWAMP: 0x3A7A6A, WARM_OCEAN: 0x43D5EE, LUKEWARM_OCEAN: 0x45ADF2,
    DEEP_LUKEWARM_OCEAN: 0x45ADF2, COLD_OCEAN: 0x3D57D6, DEEP_COLD_OCEAN: 0x3D57D6, FROZEN_OCEAN: 0x3938C9,
    DEEP_FROZEN_OCEAN: 0x3938C9, FROZEN_RIVER: 0x3938C9, SNOWY_BEACH: 0x3D57D6, SNOWY_TAIGA: 0x3D57D6,
    MEADOW: 0x0E4ECF, CHERRY_GROVE: 0x5DB7EF, PALE_GARDEN: 0x6E8292,
  };
  // everything else (oceans, rivers, caves): temperate defaults
  BIOME_LIST.forEach((b, i) => {
    const c = C[b[0]] || [0x8EB971, 0x71A74D];
    BIOME_TINT[i * 3] = c[0]; BIOME_TINT[i * 3 + 1] = c[1]; BIOME_TINT[i * 3 + 2] = W[b[0]] || 0x3F76E4;
  });
})();

// per-biome properties: surface top / filler / underwater floor and flags (trees: MC_TREES)
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
  def('PALE_GARDEN', {});
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
  def('LUSH_CAVES', {}); def('DRIPSTONE_CAVES', {}); def('DEEP_DARK', {});
})();

const ORIG_BIOME = [];   // biome of the original's set that a biome follows (variants -> base)
// Trees per biome as Minecraft's placed tree features (VegetationPlacements): count attempts a
// chunk plus `extra` more with probability `chance`, each at a random column of the chunk, kept where
// the column's biome is the one the feature belongs to; the type by sequential chances, the last
// entry being the default (RandomFeatureConfiguration). Variant biomes use their own entries.
const MC_TREES = {};
// columns (Chebyshev distance between trunks) a tree needs free of another tree's trunk: 2x2
// trunks and cacti; other trunks may stand side by side as in Minecraft
const TREE_ROOM = { cactus: 2, ice_spike: 2, giant_red_mushroom: 3, giant_brown_mushroom: 3, dark_oak: 2, pale_oak: 2, mega_spruce: 2, mega_pine: 2, mega_jungle: 2 };
// deepest water (blocks above the ground) a tree may stand in, as Minecraft's surface water depth filter
const TREE_WATER = { mangrove: 5, tall_mangrove: 5, swamp_oak: 2 };
const FACE6 = [1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1], FACE4 = [1, 0, -1, 0, 0, 1, 0, -1];
(function () {
  const T = (keys, count, chance, extra, types) => { for (const k of keys.split(' ')) MC_TREES[BI[k]] = { count, chance, extra, types }; };
  T('PLAINS SUNFLOWER_PLAINS', 0, 0.05, 1, [[0.33333334, 'fancy_oak'], [1, 'oak']]);
  T('FOREST', 10, 0.1, 1, [[0.2, 'birch'], [0.1, 'fancy_oak'], [1, 'oak']]);
  T('FLOWER_FOREST', 6, 0.1, 1, [[0.2, 'birch'], [0.1, 'fancy_oak'], [1, 'oak']]);
  T('BIRCH_FOREST', 10, 0.1, 1, [[1, 'birch']]);
  T('OLD_GROWTH_BIRCH_FOREST', 10, 0.1, 1, [[0.5, 'super_birch'], [1, 'birch']]);
  T('DARK_FOREST', 16, 0, 0, [[0.025, 'giant_brown_mushroom'], [0.05, 'giant_red_mushroom'], [0.6666667, 'dark_oak'], [0.2, 'birch'], [0.1, 'fancy_oak'], [1, 'oak']]);
  T('PALE_GARDEN', 14, 0, 0, [[1, 'pale_oak']]);
  T('TAIGA SNOWY_TAIGA', 10, 0.1, 1, [[0.33333334, 'pine'], [1, 'spruce']]);
  T('GROVE', 10, 0.1, 1, [[0.33333334, 'pine'], [1, 'spruce']]);
  T('OLD_GROWTH_SPRUCE_TAIGA', 10, 0.1, 1, [[0.33333334, 'mega_spruce'], [0.33333334, 'pine'], [1, 'spruce']]);
  T('OLD_GROWTH_PINE_TAIGA', 10, 0.1, 1, [[0.025641026, 'mega_spruce'], [0.30769232, 'mega_pine'], [0.33333334, 'pine'], [1, 'spruce']]);
  T('SNOWY_PLAINS', 0, 0.1, 1, [[1, 'spruce']]);
  T('JUNGLE', 50, 0.1, 1, [[0.1, 'fancy_oak'], [0.5, 'jungle_bush'], [0.33333334, 'mega_jungle'], [1, 'jungle']]);
  T('SPARSE_JUNGLE', 2, 0.1, 1, [[0.1, 'fancy_oak'], [0.5, 'jungle_bush'], [1, 'jungle']]);
  T('BAMBOO_JUNGLE', 30, 0.1, 1, [[0.05, 'fancy_oak'], [0.15, 'jungle_bush'], [0.7, 'mega_jungle'], [1, null]]);
  T('SAVANNA SAVANNA_PLATEAU', 1, 0.1, 2, [[0.8, 'acacia'], [1, 'oak']]);
  T('WINDSWEPT_SAVANNA', 2, 0.1, 2, [[0.8, 'acacia'], [1, 'oak']]);
  T('WINDSWEPT_HILLS WINDSWEPT_GRAVELLY_HILLS', 0, 0.1, 1, [[0.666, 'spruce'], [0.1, 'fancy_oak'], [1, 'oak']]);
  T('WINDSWEPT_FOREST', 3, 0.1, 1, [[0.666, 'spruce'], [0.1, 'fancy_oak'], [1, 'oak']]);
  T('MEADOW', 0, 0.01, 1, [[0.5, 'fancy_oak'], [1, 'super_birch']]);
  T('CHERRY_GROVE', 10, 0.1, 1, [[1, 'cherry']]);
  T('SWAMP', 2, 0.1, 1, [[1, 'swamp_oak']]);
  T('MANGROVE_SWAMP', 25, 0, 0, [[0.85, 'mangrove'], [1, 'tall_mangrove']]);
  T('WOODED_BADLANDS', 5, 0.1, 1, [[1, 'oak']]);
  const alias = { SUNFLOWER_PLAINS: 'PLAINS', OLD_GROWTH_BIRCH_FOREST: 'BIRCH_FOREST', OLD_GROWTH_PINE_TAIGA: 'OLD_GROWTH_SPRUCE_TAIGA',
    SAVANNA_PLATEAU: 'SAVANNA', WINDSWEPT_SAVANNA: 'SAVANNA', WINDSWEPT_GRAVELLY_HILLS: 'WINDSWEPT_HILLS', WINDSWEPT_FOREST: 'WINDSWEPT_HILLS',
    ERODED_BADLANDS: 'BADLANDS' };
  BIOME_LIST.forEach((b, i) => { ORIG_BIOME[i] = alias[b[0]] ? BI[alias[b[0]]] : BPROP[i].base; });
})();

// Ground cover as Minecraft's vegetation patches (RandomPatchFeature + VegetationPlacements):
// a patch puts up to `tries` plants around its origin, each at origin + (r(xz) - r(xz), r(y) - r(y),
// r(xz) - r(xz)) where the cell is free and the plant can stay. kind picks the plant.
const PATCHES = {
  grass: { tries: 32, xz: 7, y: 3, kind: 'grass' }, grass_jungle: { tries: 32, xz: 7, y: 3, kind: 'grass_jungle' },
  grass_taiga: { tries: 32, xz: 7, y: 3, kind: 'grass_taiga' }, tall_grass: { tries: 96, xz: 7, y: 3, kind: 'tall_grass' },
  large_fern: { tries: 96, xz: 7, y: 3, kind: 'large_fern' }, dead_bush: { tries: 4, xz: 7, y: 3, kind: 'dead_bush' },
  sugar_cane: { tries: 20, xz: 4, y: 0, kind: 'sugar_cane' }, pumpkin: { tries: 96, xz: 7, y: 3, kind: 'pumpkin' },
  melon: { tries: 64, xz: 7, y: 3, kind: 'melon' }, berry: { tries: 96, xz: 7, y: 3, kind: 'berry' },
  sunflower: { tries: 96, xz: 7, y: 3, kind: 'sunflower' }, brown_mushroom: { tries: 96, xz: 7, y: 3, kind: 'brown_mushroom' },
  red_mushroom: { tries: 96, xz: 7, y: 3, kind: 'red_mushroom' },
  flower_default: { tries: 64, xz: 6, y: 2, kind: 'flower_default' }, flower_plains: { tries: 64, xz: 6, y: 2, kind: 'flower_plains' },
  flower_swamp: { tries: 64, xz: 6, y: 2, kind: 'blue_orchid' }, eyeblossom: { tries: 48, xz: 6, y: 2, kind: 'eyeblossom' }, flower_flower_forest: { tries: 96, xz: 6, y: 2, kind: 'flower_forest' },
  flower_meadow: { tries: 96, xz: 6, y: 2, kind: 'flower_meadow' }, forest_flowers: { tries: 96, xz: 7, y: 3, kind: 'forest_flowers' },
};
// per biome: [patch, placement]; placement: n = count per chunk, r = 1-in-r chance per instance,
// nt = [count below the noise threshold -0.8, count above] (NoiseThresholdCountPlacement),
// rn = [min, max] uniform count clamped at 0
const MC_VEG = {};
(function () {
  const V = (keys, list) => { for (const k of keys.split(' ')) MC_VEG[BI[k]] = list; };
  const mush = [['brown_mushroom', { r: 256 }], ['red_mushroom', { r: 512 }]];
  const extra = [['sugar_cane', { r: 6 }], ['pumpkin', { r: 300 }]];
  const forest = [['forest_flowers', { r: 7, rn: [-1, 3] }], ['flower_default', { r: 32 }], ['grass', { n: 2 }], ...mush, ...extra];
  V('PLAINS', [['tall_grass', { nt: [0, 7], r: 32 }], ['flower_plains', { nt: [15, 4], r: 32 }], ['grass', { nt: [5, 10] }], ...mush, ...extra]);
  V('SUNFLOWER_PLAINS', [['sunflower', { r: 3 }], ['tall_grass', { nt: [0, 7], r: 32 }], ['flower_plains', { nt: [15, 4], r: 32 }], ['grass', { nt: [5, 10] }], ...mush, ...extra]);
  V('FOREST BIRCH_FOREST OLD_GROWTH_BIRCH_FOREST DARK_FOREST', forest);
  V('FLOWER_FOREST', [['flower_flower_forest', { n: 3, r: 2 }], ['forest_flowers', { r: 7, rn: [-3, 1] }], ['grass', { n: 2 }], ...mush, ...extra]);
  V('TAIGA SNOWY_TAIGA', [['large_fern', { r: 5 }], ['grass_taiga', { n: 1 }], ['flower_default', { r: 32 }], ['brown_mushroom', { r: 4 }], ['red_mushroom', { r: 256 }], ['berry', { r: 32 }], ...extra]);
  V('OLD_GROWTH_SPRUCE_TAIGA OLD_GROWTH_PINE_TAIGA', [['large_fern', { r: 5 }], ['grass_taiga', { n: 7 }], ['dead_bush', { n: 1 }], ['flower_default', { r: 32 }],
    ['brown_mushroom', { r: 4 }], ['red_mushroom', { r: 171 }], ['berry', { r: 32 }], ...extra]);
  V('SAVANNA SAVANNA_PLATEAU WINDSWEPT_SAVANNA', [['tall_grass', { r: 5 }], ['flower_default', { r: 16 }], ['grass', { n: 20 }], ...mush, ...extra]);
  V('JUNGLE BAMBOO_JUNGLE', [['flower_default', { r: 16 }], ['grass_jungle', { n: 25 }], ['melon', { r: 6 }], ...mush, ...extra]);
  V('SPARSE_JUNGLE', [['flower_default', { r: 16 }], ['grass_jungle', { n: 25 }], ['melon', { r: 64 }], ...mush, ...extra]);
  V('SWAMP', [['flower_swamp', { r: 32 }], ['grass', { n: 5 }], ['dead_bush', { n: 1 }], ['brown_mushroom', { n: 2 }], ['red_mushroom', { r: 64 }], ['sugar_cane', { n: 3 }], ['pumpkin', { r: 300 }]]);
  V('MANGROVE_SWAMP', [['grass', { n: 5 }], ['dead_bush', { n: 1 }]]);
  V('DESERT', [['dead_bush', { n: 2 }], ['sugar_cane', { n: 1 }], ['pumpkin', { r: 300 }]]);
  V('BADLANDS ERODED_BADLANDS', [['dead_bush', { n: 20 }], ['sugar_cane', { r: 5 }], ...mush]);
  V('WOODED_BADLANDS', [['grass', { n: 1 }], ['dead_bush', { n: 20 }], ['sugar_cane', { r: 5 }], ...mush]);
  V('MEADOW', [['flower_meadow', { n: 1 }], ['grass', { n: 1 }]]);
  V('PALE_GARDEN', [['eyeblossom', { r: 2 }], ['grass', { n: 2 }]]);
  V('CHERRY_GROVE', [['grass', { nt: [5, 10] }], ['flower_plains', { r: 32 }]]);
  V('WINDSWEPT_HILLS WINDSWEPT_GRAVELLY_HILLS WINDSWEPT_FOREST SNOWY_PLAINS STONY_SHORE BEACH RIVER', [['grass', { n: 1 }], ['flower_default', { r: 32 }], ...mush, ...extra]);
})();

// ---------------------------------------------------------------- land biome rules
// Own rules: the land biome follows the height the terrain reached (peaks, slopes, windswept hills),
// the flat wet lowland (swamps), and otherwise a temperature x humidity table whose cells pick a
// variant by weirdness (a third climate axis independent of the other two).
// Table cell: a biome key, or [key if w > split, split, key otherwise] (a nested cell may follow).
const LAND_TABLE = [
  // humidity: dry ............................................................................ wet
  [['ICE_SPIKES', 0.3, 'SNOWY_PLAINS'], 'SNOWY_PLAINS', ['SNOWY_TAIGA', -0.2, 'SNOWY_PLAINS'], 'SNOWY_TAIGA', 'SNOWY_TAIGA'],          // frozen
  ['PLAINS', 'PLAINS', ['TAIGA', 0, 'FOREST'], 'TAIGA', ['OLD_GROWTH_PINE_TAIGA', 0.3, ['TAIGA', -0.3, 'OLD_GROWTH_SPRUCE_TAIGA']]],   // cold
  [['FLOWER_FOREST', 0.4, 'PLAINS'], ['SUNFLOWER_PLAINS', 0.5, 'PLAINS'], 'FOREST', ['OLD_GROWTH_BIRCH_FOREST', 0.4, 'BIRCH_FOREST'], ['PALE_GARDEN', 0.3, 'DARK_FOREST']],   // temperate
  ['SAVANNA', 'SAVANNA', ['FOREST', 0, 'PLAINS'], 'SPARSE_JUNGLE', ['BAMBOO_JUNGLE', 0.3, 'JUNGLE']],                              // warm
  ['DESERT', 'DESERT', 'DESERT', 'DESERT', ['JUNGLE', 0, 'SAVANNA']],                                                         // hot
];
function landCell(cell, w) {
  while (typeof cell !== 'string') cell = w > cell[1] ? cell[0] : cell[2];
  return BI[cell];
}
function landBiome(o) {
  const ti = o.ti, hs = o.h - SEA, w = o.w, e = o.e, hu = o.hu;
  const hi = hu < -0.4 ? 0 : hu < -0.1 ? 1 : hu < 0.15 ? 2 : hu < 0.4 ? 3 : 4;
  if (hs >= 80) return ti >= 3 ? BI.STONY_PEAKS : w < 0 ? BI.JAGGED_PEAKS : BI.FROZEN_PEAKS;
  if (hs >= 40 && e < -0.1) {
    if (ti <= 1) return hi <= 1 ? BI.SNOWY_SLOPES : BI.GROVE;
    if (ti === 2) return hi <= 1 ? (w > 0.3 ? BI.CHERRY_GROVE : BI.MEADOW) : hi === 2 ? BI.MEADOW : BI.GROVE;
    if (ti === 3) return hi <= 2 ? BI.SAVANNA_PLATEAU : BI.JUNGLE;
    return e < -0.4 ? BI.ERODED_BADLANDS : BI.BADLANDS;
  }
  if (hs >= 30 && e > 0.1) {
    if (ti <= 2) return hi === 0 ? BI.WINDSWEPT_GRAVELLY_HILLS : hi >= 3 ? BI.WINDSWEPT_FOREST : BI.WINDSWEPT_HILLS;
    if (hi <= 2) return BI.WINDSWEPT_SAVANNA;
  }
  if (o.k6 > 0.5 && hs < 6 && ti > 0 && hi >= 2) return ti <= 2 ? BI.SWAMP : BI.MANGROVE_SWAMP;
  // the hot dry belt rises into banded badlands
  if (ti === 4 && hi >= 2 && hi <= 3 && hs >= 15) return hi === 3 ? BI.WOODED_BADLANDS : e > 0.1 ? BI.ERODED_BADLANDS : BI.BADLANDS;
  return landCell(LAND_TABLE[ti][hi], w);
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

// structure sets (worldgen/structure_set): salt, spacing, separation; reach = chunks a start's
// pieces can extend from its chunk
const TPL_CACHE = new Map();
const IGLOO_BIOMES = new Set([BI.SNOWY_TAIGA, BI.SNOWY_PLAINS, BI.SNOWY_SLOPES]);
const RUIN_COLD = new Set([BI.FROZEN_OCEAN, BI.COLD_OCEAN, BI.OCEAN, BI.DEEP_FROZEN_OCEAN, BI.DEEP_COLD_OCEAN, BI.DEEP_OCEAN]);
const RUIN_WARM = new Set([BI.LUKEWARM_OCEAN, BI.WARM_OCEAN, BI.DEEP_LUKEWARM_OCEAN]);
const SHIPWRECK_BEACHED = ['with_mast', 'sideways_full', 'sideways_fronthalf', 'sideways_backhalf', 'rightsideup_full', 'rightsideup_fronthalf',
  'rightsideup_backhalf', 'with_mast_degraded', 'rightsideup_full_degraded', 'rightsideup_fronthalf_degraded', 'rightsideup_backhalf_degraded'];
const SHIPWRECK_OCEAN = ['with_mast', 'upsidedown_full', 'upsidedown_fronthalf', 'upsidedown_backhalf', 'sideways_full', 'sideways_fronthalf',
  'sideways_backhalf', 'rightsideup_full', 'rightsideup_fronthalf', 'rightsideup_backhalf', 'with_mast_degraded', 'upsidedown_full_degraded',
  'upsidedown_fronthalf_degraded', 'upsidedown_backhalf_degraded', 'sideways_full_degraded', 'sideways_fronthalf_degraded',
  'sideways_backhalf_degraded', 'rightsideup_full_degraded', 'rightsideup_fronthalf_degraded', 'rightsideup_backhalf_degraded'];
const SHIP_LOOT = { supply_chest: LOOT.SHIPWRECK_SUPPLY, map_chest: LOOT.SHIPWRECK_MAP, treasure_chest: LOOT.SHIPWRECK_TREASURE };
const STRUCTURE_SETS = [
  { salt: 38810541, spacing: 32, separation: 8, reach: 1, start: function (x, z) { return this.iglooStart(x, z); } },
  { salt: 61294733, spacing: 24, separation: 4, reach: 2, start: function (x, z) { return this.shipwreckStart(x, z); } },
  { salt: 29467103, spacing: 20, separation: 8, reach: 3, start: function (x, z) { return this.oceanRuinStart(x, z); } },
];
// jigsaw structures (worldgen/structure + structure_set): start pools by biome, depth, placement
const STRUCTURE_SETS_VIL = { salt: 52918417, spacing: 34, separation: 8, reach: 7, depth: 6, types: [
  { pool: 'village/plains/town_centers', biomes: [BI.PLAINS, BI.MEADOW] }, { pool: 'village/desert/town_centers', biomes: [BI.DESERT] },
  { pool: 'village/savanna/town_centers', biomes: [BI.SAVANNA] }, { pool: 'village/snowy/town_centers', biomes: [BI.SNOWY_PLAINS] },
  { pool: 'village/taiga/town_centers', biomes: [BI.TAIGA] }], start: function (x, z) { return this.jigsawStart(STRUCTURE_SETS_VIL, x, z); } };
// pillager outposts: frequency 0.2, never within 10 chunks of a village placement chunk
const STRUCTURE_SETS_OUTPOST = { salt: 73810259, spacing: 32, separation: 8, reach: 7, depth: 7, types: [{ pool: 'pillager_outpost/base_plates',
  biomes: [BI.DESERT, BI.PLAINS, BI.SAVANNA, BI.SNOWY_PLAINS, BI.TAIGA, BI.MEADOW, BI.FROZEN_PEAKS, BI.JAGGED_PEAKS, BI.STONY_PEAKS, BI.SNOWY_SLOPES, BI.CHERRY_GROVE, BI.GROVE] }],
  start: function (x, z) {
    if (hash2(this.seed + 73810259 * 7, x, z) >= 0.2) return null;
    const V = STRUCTURE_SETS_VIL;
    for (let rz = Math.floor((z - 10) / V.spacing); rz <= Math.floor((z + 10) / V.spacing); rz++)
      for (let rx = Math.floor((x - 10) / V.spacing); rx <= Math.floor((x + 10) / V.spacing); rx++) {
        const [vx, vz] = this.spreadStart(V, rx, rz);
        if (Math.abs(vx - x) <= 10 && Math.abs(vz - z) <= 10) return null;
      }
    return this.jigsawStart(STRUCTURE_SETS_OUTPOST, x, z);
  } };
const JIGSAW_SETS = [STRUCTURE_SETS_VIL, STRUCTURE_SETS_OUTPOST];
STRUCTURE_SETS.unshift(STRUCTURE_SETS_VIL, STRUCTURE_SETS_OUTPOST);
// Beardifier kernel (24^3): [z][x][y], value at offset (x, y, z) from a piece's ground / a junction
const BEARD_KERNEL = (function () {
  const K = new Float32Array(13824);
  for (let zi = 0; zi < 24; zi++) for (let xi = 0; xi < 24; xi++) for (let yi = 0; yi < 24; yi++) {
    const x = xi - 12, y = yi - 12, z = zi - 12, d0 = x * x + z * z, d1 = y + 0.5, d2 = d1 * d1;
    K[zi * 576 + xi * 24 + yi] = -d1 / Math.sqrt(d2 / 2 + d0 / 2) / 2 * Math.exp(-(d2 / 16 + d0 / 16));
  }
  return K;
})();
const box6 = (b) => [b[0], b[1], b[2], b[3] + 1, b[4] + 1, b[5] + 1];
// block meta turned with a template rotation (0..3 clockwise quarter turns)
function rotMeta(id, m, r) {
  if (!r) return m;
  const sh = SHAPE[id];
  if (sh === SH.STAIRS || sh === SH.DOOR || sh === SH.TRAPDOOR || sh === SH.LADDER || sh === SH.CHEST || sh === SH.BED || sh === SH.GATE ||
    sh === SH.BUTTON || (FLAGS[id] & BF_FACING)) return (m & ~3) | (((m & 3) + r) & 3);
  if (sh === SH.TORCH) return m ? 1 + ((m - 1 + r) & 3) : 0;
  if (sh === SH.RAIL) return r & 1 ? m ^ 1 : m;
  if ((FLAGS[id] & BF_AXIS) && (r & 1) && (m & 3)) return (m & ~3) | (3 - (m & 3));
  return m;
}

class WorldGen {
  constructor(seed) {
    this.seed = seed | 0;
    const S = k => new Simplex((this.seed ^ Math.imul(k, 0x5bd1e995)) >>> 0);
    this.nCont = S(1); this.nEro = S(2); this.nWeird = S(3); this.nTemp = S(4); this.nHum = S(5);
    this.nHill = S(6); this.nDen = S(7); this.nCheese = S(8); this.nSp1 = S(9); this.nSp2 = S(10);
    this.nNd1 = S(11); this.nNd2 = S(12); this.nSurf = S(13); this.nRiver = S(14); this.nCave = S(15);
    this.nMisc = S(16); this.nOre = S(17); this.nMush = S(18); this.nWarp = S(19); this.nGeode = S(20);
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
    this.structCache = new Map();
    this.surfCache = new Map();
    this.deco = [];                              // tree decorations of the chunk being generated
    this.treeWrites = null;                      // queued tree blocks while features() runs
    // tree buffer; id carries the log axis in bits 16+ (0 y, 1 x, 2 z)
    this.tb = { n: 0, map: new Map(), x: new Int32Array(16384), y: new Int32Array(16384), z: new Int32Array(16384), id: new Int32Array(16384),
      kind: new Uint8Array(16384), dist: new Uint8Array(16384), q: new Int32Array(16384) };
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
    return landBiome(o);
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
    this.vilGround = null;
    this.beard = this.villageBeard();
    this.structureTreeBoxes();
    this.fillTerrain();
    this.surface();
    this.caves();
    this.geodes();
    this.spawnerList = [];
    this.dungeons();
    this.structures();
    this.ores();
    this.bedrock();
    this.springs();
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
    return { sections: secs, biomes: bio, caveBiomes: this.cbio.slice(), springs: this.springList, spawners: this.spawnerList };
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
    const BD = this.beard;
    if (BD) hmax = Math.max(hmax, BD.y1);
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
        // Beardifier near village pieces: added per block, in this terrain's units (Minecraft's
        // density changes ~0.05 per block inside the ground and ~0.0125 in the air)
        const bd = BD && yb + 7 >= BD.y0 && yb <= BD.y1;
        const g = bd ? (cinfo[ck * 5 + ci].factor + cinfo[ck * 5 + ci + 1].factor + cinfo[(ck + 1) * 5 + ci].factor + cinfo[(ck + 1) * 5 + ci + 1].factor) / 96 : 0;
        const allS = !bd && d000 > 0 && d100 > 0 && d001 > 0 && d101 > 0 && d010 > 0 && d110 > 0 && d011 > 0 && d111 > 0;
        const allA = !bd && d000 <= 0 && d100 <= 0 && d001 <= 0 && d101 <= 0 && d010 <= 0 && d110 <= 0 && d011 <= 0 && d111 <= 0;
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
              let v = allS ? 1 : allA ? -1 : f0 + (f1 - f0) * (lx / 4);
              if (bd && y >= BD.y0 && y <= BD.y1) { const b = BD.A[(y - BD.y0) * 256 + z * 16 + x]; if (b) v += b * g / (v > 0 ? 0.05 : 0.0125); }
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
    const ids = this.ids, x0 = this.x0, z0 = this.z0, TOP = this.TOP, BD = this.beard;
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
        // the beard of a village piece fills caves under it (it is added after the carvers in Minecraft)
        if (BD && y >= BD.y0 && y <= BD.y1 && BD.A[(y - BD.y0) * 256 + z * 16 + x] > 0.05) continue;
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

  // ------------------------------------------------------------ amethyst geodes
  // Minecraft's amethyst_geode (GeodeFeature): in 1 of 24 chunks, origin at y -58..30; 3-4
  // distribution points 4-6 blocks off the origin on each axis, each with an offset of 1-2. A cell
  // belongs to the geode by d = sum over the points of 1/sqrt(distance^2 + offset) (+ noise x 0.05):
  // filling air from 1/sqrt(1.7), amethyst from 1/sqrt(2.2 + k/6) (8.3% budding amethyst), calcite
  // from 1/sqrt(3.2 + k/6), smooth basalt from 1/sqrt(4.2 + k/6). 95% get a crack to one side, and
  // 35% of the budding blocks grow a bud or cluster into their first open neighbour (down, up,
  // north, south, west, east). A geode is not placed when more than one of its points is open space
  // (here: above the ground). Geodes span chunks: each chunk writes its part of the geodes started
  // up to one chunk away, with every choice made from the position, so the parts agree.
  geodeOf(sx, sz) {
    const key = sx * 65536 + sz, C = this.geodeCache || (this.geodeCache = new Map());
    if (C.has(key)) return C.get(key);
    let G = null;
    const rnd = mulberry(hash2i(this.seed + 911, sx, sz));
    if (rnd() < 1 / 24) {
      const ox = sx * 16 + ((rnd() * 16) | 0), oz = sz * 16 + ((rnd() * 16) | 0), oy = -58 + ((rnd() * 89) | 0);
      const k = 3 + ((rnd() * 2) | 0), pts = [];
      let bad = 0;
      for (let i = 0; i < k; i++) {
        const px = ox + 4 + ((rnd() * 3) | 0), py = oy + 4 + ((rnd() * 3) | 0), pz = oz + 4 + ((rnd() * 3) | 0);
        if (py >= this.colInfo(px, pz).h - 1) bad++;
        pts.push([px, py, pz, 1 + ((rnd() * 2) | 0)]);
      }
      if (bad <= 1) {
        const d0 = k / 6, crack = rnd() < 0.95, side = (rnd() * 4) | 0, j = k * 2 + 1, cr = [];
        if (crack) for (const dy of [7, 5, 1]) cr.push(side === 0 ? [ox + j, oy + dy, oz] : side === 1 ? [ox, oy + dy, oz + j] : side === 2 ? [ox + j, oy + dy, oz + j] : [ox, oy + dy, oz]);
        G = { ox, oy, oz, pts, cr, d1: 1 / Math.sqrt(1.7), d2: 1 / Math.sqrt(2.2 + d0), d3: 1 / Math.sqrt(3.2 + d0), d4: 1 / Math.sqrt(4.2 + d0),
          d5: 1 / Math.sqrt(2 + rnd() * 0.5 + (k > 3 ? d0 : 0)), nx: rnd() * 1000, nz: rnd() * 1000, seed: hash2i(this.seed + 913, sx, sz) };
      }
    }
    if (C.size > 512) C.clear();
    C.set(key, G);
    return G;
  }
  // 0 outside, 1 crack, 2 filling air, 3 amethyst layer, 4 calcite, 5 smooth basalt
  geodeCell(G, x, y, z) {
    if (Math.abs(x - G.ox) > 16 || Math.abs(y - G.oy) > 16 || Math.abs(z - G.oz) > 16) return 0;
    const n = this.nGeode.n3((x + G.nx) / 16, y / 16, (z + G.nz) / 16) * 0.05;
    let d6 = 0;
    for (const p of G.pts) { const dx = x - p[0], dy = y - p[1], dz = z - p[2]; d6 += 1 / Math.sqrt(dx * dx + dy * dy + dz * dz + p[3]) + n; }
    if (d6 < G.d4) return 0;
    if (G.cr.length && d6 < G.d1) {
      let d7 = 0;
      for (const p of G.cr) { const dx = x - p[0], dy = y - p[1], dz = z - p[2]; d7 += 1 / Math.sqrt(dx * dx + dy * dy + dz * dz + 2) + n; }
      if (d7 >= G.d5) return 1;
    }
    return d6 >= G.d1 ? 2 : d6 >= G.d2 ? 3 : d6 >= G.d3 ? 4 : 5;
  }
  geodes() {
    const ids = this.ids, meta = this.meta, x0 = this.x0, z0 = this.z0;
    // bud directions in Minecraft's order (down, up, north, south, west, east) and their faces
    const DIRS = [[0, -1, 0, 3], [0, 1, 0, 2], [0, 0, -1, 5], [0, 0, 1, 4], [-1, 0, 0, 1], [1, 0, 0, 0]];
    const BUDS = [B.SMALL_AMETHYST_BUD, B.MEDIUM_AMETHYST_BUD, B.LARGE_AMETHYST_BUD, B.AMETHYST_CLUSTER];
    for (let sz = this.cz - 1; sz <= this.cz + 1; sz++) for (let sx = this.cx - 1; sx <= this.cx + 1; sx++) {
      const G = this.geodeOf(sx, sz);
      if (!G) continue;
      const xa = Math.max(x0 - 1, G.ox - 16), xb = Math.min(x0 + 16, G.ox + 16), za = Math.max(z0 - 1, G.oz - 16), zb = Math.min(z0 + 16, G.oz + 16);
      if (xa > xb || za > zb) continue;
      const ya = Math.max(WORLD_MIN_Y, G.oy - 16), yb = G.oy + 16;
      for (let y = ya; y <= yb; y++) for (let z = za; z <= zb; z++) for (let x = xa; x <= xb; x++) {
        const c = this.geodeCell(G, x, y, z);
        if (!c) continue;
        const inside = x >= x0 && x < x0 + 16 && z >= z0 && z < z0 + 16;
        const budding = c === 3 && hash3(G.seed, x, y, z) < 0.083;
        if (inside) {
          const i = CI(x - x0, y, z - z0);
          ids[i] = c <= 2 ? 0 : c === 3 ? (budding ? B.BUDDING_AMETHYST : B.AMETHYST_BLOCK) : c === 4 ? B.CALCITE : B.SMOOTH_BASALT;
          meta[i] = 0;
        }
        if (!budding || hash3(G.seed + 1, x, y, z) >= 0.35) continue;
        for (const d of DIRS) {
          const nx = x + d[0], ny = y + d[1], nz = z + d[2], nc = this.geodeCell(G, nx, ny, nz);
          if (nc !== 1 && nc !== 2) continue;
          if (nx >= x0 && nx < x0 + 16 && nz >= z0 && nz < z0 + 16) {
            const i = CI(nx - x0, ny, nz - z0);
            ids[i] = BUDS[(hash3(G.seed + 2, x, y, z) * 4) | 0]; meta[i] = d[3];
          }
          break;
        }
      }
    }
  }

  // ------------------------------------------------------------ fluid springs (waterfalls)
  // Minecraft's spring_water (25 tries a chunk, y uniform from the bottom to 192) and spring_lava
  // (20 tries, very biased toward the bottom), SpringFeature: the block above and the one below are
  // rock, of the four sides plus the one below exactly four are rock and exactly one is open; the
  // spot (air or rock) becomes a source that the game then lets flow (a waterfall from a cliff or
  // a cave wall). The sources are handed to the world, which schedules their first fluid update.
  springs() {
    const ids = this.ids, x0 = this.x0, z0 = this.z0, list = this.springList = [];
    const rnd = mulberry(hash2i(this.seed + 433, this.cx, this.cz));
    const WROCK = new Set([B.STONE, B.GRANITE, B.DIORITE, B.ANDESITE, B.DEEPSLATE, B.TUFF, B.CALCITE, B.DIRT, B.SNOW, B.PACKED_ICE]);
    const LROCK = new Set([B.STONE, B.GRANITE, B.DIORITE, B.ANDESITE, B.DEEPSLATE, B.TUFF, B.CALCITE]);
    const ni = (a, b) => a + ((rnd() * (b - a + 1)) | 0);                  // Mth.nextInt(a, b)
    const tryAt = (x, y, z, fluid, rock) => {
      if (x < 1 || x > 14 || z < 1 || z > 14 || y <= WORLD_MIN_Y || y >= WORLD_MAX_Y - 1) return;   // sides must be in this chunk
      const i = CI(x, y, z), at = ids[i];
      if (!rock.has(ids[i + 256]) || !rock.has(ids[i - 256])) return;
      if (at !== 0 && !rock.has(at)) return;
      let r = 1, open = 0;                                                       // the one below is rock
      for (const j of [i - 1, i + 1, i - 16, i + 16]) { const n = ids[j]; if (rock.has(n)) r++; else if (n === 0) open++; }
      if (r !== 4 || open !== 1) return;
      ids[i] = fluid; this.meta[i] = 0;
      list.push(x0 + x, y, z0 + z);
    };
    for (let k = 0; k < 25; k++) { const x = ni(0, 15), z = ni(0, 15), y = ni(WORLD_MIN_Y, 192); tryAt(x, y, z, B.WATER, WROCK); }
    for (let k = 0; k < 20; k++) {
      const x = ni(0, 15), z = ni(0, 15), lo = WORLD_MIN_Y, hi = WORLD_MAX_Y - 1 - 8;
      const a = ni(lo + 8, hi), b = ni(lo, a - 1), y = ni(lo, b - 1 + 8);
      tryAt(x, y, z, B.LAVA, LROCK);
    }
  }

  // ---------------------------------------------------------------- template structures
  // Minecraft's StructureTemplate pieces (structures.js): placed with a rotation (0 none, 1 clockwise,
  // 2 180, 3 counter-clockwise) about a pivot, block states turned with it; markers handled per kind.
  // Starts follow RandomSpreadStructurePlacement (one candidate chunk per spacing x spacing region,
  // offset by random 0..spacing-separation-1) and the structure's biomes; layouts come from the start
  // chunk's random, heights from the density surface, so every chunk agrees.
  spreadStart(set, rx, rz) {
    const rnd = mulberry(hash2i(this.seed + set.salt, rx, rz)), n = set.spacing - set.separation;
    return [rx * set.spacing + ((rnd() * n) | 0), rz * set.spacing + ((rnd() * n) | 0)];
  }
  structures() {
    const cx = this.cx, cz = this.cz;
    for (const set of STRUCTURE_SETS) {
      const R = set.reach;
      const r0x = Math.floor((cx - R) / set.spacing), r1x = Math.floor((cx + R) / set.spacing);
      const r0z = Math.floor((cz - R) / set.spacing), r1z = Math.floor((cz + R) / set.spacing);
      for (let rz = r0z; rz <= r1z; rz++) for (let rx = r0x; rx <= r1x; rx++) {
        const [sx, sz] = this.spreadStart(set, rx, rz);
        if (Math.abs(sx - cx) > R || Math.abs(sz - cz) > R) continue;
        const st = this.structStart(set, sx, sz);
        if (st) for (const p of st) this.placePiece(p);
      }
    }
  }
  structStart(set, sx, sz) {
    const key = set.salt + ':' + sx + ':' + sz, C = this.structCache;
    let st = C.get(key);
    if (st === undefined) { st = set.start.call(this, sx, sz); if (C.size > 4000) C.clear(); C.set(key, st); }
    return st;
  }
  // decoded template: grid of state + 1, per palette the block id / meta of each state (-1 nothing)
  tpl(name, data) {
    let T = TPL_CACHE.get(name);
    if (T) return T;
    const S = data || STRUCT_TPL[name], [sx, sy, sz] = S.s, g = new Uint8Array(sx * sy * sz);
    const raw = atob(S.g);
    for (let i = 0, o = 0; i < raw.length; i += 2) { const v = raw.charCodeAt(i), n = raw.charCodeAt(i + 1); g.fill(v, o, o + n); o += n; }
    const pals = S.m.map((m) => m.map((e) => {
      if (!e) return [-1, 0];
      const [key, meta, wet] = S.p[e - 1], id = key === 'AIR' ? 0 : B[key];
      return [wet && WET[id] ? WET[id] : id, meta];
    }));
    const palIds = S.p.map(([key, meta, wet]) => { const id = key === 'AIR' ? 0 : B[key]; return [wet && WET[id] ? WET[id] : id, meta]; });
    T = { sx, sy, sz, g, pals, palIds, marks: S.d };
    TPL_CACHE.set(name, T);
    return T;
  }
  // StructureTemplate.transform: template position -> offset from the template origin
  static tplPos(x, z, rot, px, pz) {
    switch (rot) {
      case 1: return [px + pz - z, pz - px + x];
      case 2: return [px + px - x, pz + pz - z];
      case 3: return [px - pz + z, px + pz - x];
      default: return [x, z];
    }
  }
  // placed bounds of a piece (world x0, z0, x1, z1)
  pieceBox(p) {
    const T = this.tpl(p.name), a = WorldGen.tplPos(0, 0, p.rot, p.px, p.pz), b = WorldGen.tplPos(T.sx - 1, T.sz - 1, p.rot, p.px, p.pz);
    return [p.x + Math.min(a[0], b[0]), p.z + Math.min(a[1], b[1]), p.x + Math.max(a[0], b[0]), p.z + Math.max(a[1], b[1])];
  }
  placePiece(p) {
    const x0 = this.x0, z0 = this.z0;
    if (p.vil) {
      const b = p.box;
      if (b[0] > x0 + 15 || b[3] < x0 || b[2] > z0 + 15 || b[5] < z0) return;
      if (!this.vilGround) {
        // WORLD_SURFACE_WG of this chunk's columns before any piece is placed
        const G = this.vilGround = new Int16Array(256);
        for (let i = 0; i < 256; i++) { let y = WORLD_MAX_Y - 1; while (y > WORLD_MIN_Y && !this.ids[CI(i & 15, y, i >> 4)]) y--; G[i] = y; }
      }
      this.placeVillagePiece(p);
      return;
    }
    if (p.build) { const b = p.box; if (b[0] <= x0 + 15 && b[3] >= x0 && b[2] <= z0 + 15 && b[5] >= z0) p.build.call(this, p); return; }
    const bx = p.box || (p.box = this.pieceBox(p));
    if (bx[0] > x0 + 15 || bx[2] < x0 || bx[1] > z0 + 15 || bx[3] < z0) return;
    const T = this.tpl(p.name), pal = T.pals[p.pal || 0], ids = this.ids, meta = this.meta, g = T.g, seed = this.seed;
    const { sx, sy, sz } = T, rot = p.rot, px = p.px, pz = p.pz;
    for (let y = 0; y < sy; y++) {
      const wy = p.y + y;
      if (wy <= WORLD_MIN_Y || wy >= WORLD_MAX_Y) continue;
      for (let z = 0; z < sz; z++) for (let x = 0; x < sx; x++) {
        const v = g[(y * sz + z) * sx + x];
        if (!v) continue;
        const e = pal[v - 1];
        if (e[0] < 0) continue;
        const o = WorldGen.tplPos(x, z, rot, px, pz), wx = p.x + o[0], wz = p.z + o[1];
        if (wx < x0 || wx > x0 + 15 || wz < z0 || wz > z0 + 15) continue;
        // BlockRotProcessor: each block kept with the piece's integrity
        if (p.integrity < 1 && hash3(seed + p.salt, wx, wy, wz) > p.integrity) continue;
        const i = CI(wx - x0, wy, wz - z0), cur = ids[i];
        let id = e[0];
        // a block that can hold water placed into a water source becomes waterlogged
        if (id && WET[id] && (cur === B.WATER && meta[i] === 0 || isWaterId(cur))) id = WET[id];
        ids[i] = id; meta[i] = id ? rotMeta(id, e[1], rot) : 0;
      }
    }
    if (p.marker) for (const [x, y, z, name] of T.marks) {
      const o = WorldGen.tplPos(x, z, rot, px, pz), wx = p.x + o[0], wy = p.y + y, wz = p.z + o[1];
      if (wx < x0 || wx > x0 + 15 || wz < z0 || wz > z0 + 15 || wy <= WORLD_MIN_Y + 1 || wy >= WORLD_MAX_Y) continue;
      p.marker.call(this, name, wx, wy, wz, CI(wx - x0, wy, wz - z0));
    }
    if (p.after) p.after.call(this, p);
  }
  // chest at index i gets a loot table (rolled when opened)
  lootChest(i, table) { if (dryId(this.ids[i]) === B.CHEST) this.meta[i] = (this.meta[i] & 3) | (table << 2); }
  // OCEAN_FLOOR_WG / WORLD_SURFACE_WG heights from the density surface
  floorH(x, z) {
    const k = x * 131072 + z, C = this.surfCache;
    let h = C.get(k);
    if (h === undefined) { h = this.surfaceAt(x, z) + 1; if (C.size > 50000) C.clear(); C.set(k, h); }
    return h;
  }
  surfH(x, z) { return Math.max(this.floorH(x, z), SEA); }

  // IglooStructure / IglooPieces: the igloo on the surface; half of them with a ladder shaft of
  // 4..11 sections down to a basement laboratory
  iglooStart(cx, cz) {
    if (!IGLOO_BIOMES.has(this.biomeAt(cx * 16, cz * 16))) return null;
    const rnd = mulberry(hash2i(this.seed + 38810541 * 3, cx, cz)), rot = (rnd() * 4) | 0;
    const X = cx * 16, Z = cz * 16, out = [];
    const PIV = { 'igloo/top': [3, 5], 'igloo/middle': [1, 1], 'igloo/bottom': [3, 7] };
    const OFF = { 'igloo/top': [0, 0, 0], 'igloo/middle': [2, -3, 4], 'igloo/bottom': [0, -3, -2] };
    const piece = (name, down) => {
      const [ox, oy, oz] = OFF[name], [px, pz] = PIV[name];
      const p = { name, x: X + ox, y: 90 + oy - down, z: Z + oz, rot, px, pz, integrity: 1 };
      // height: WORLD_SURFACE_WG at the template position + rotate(3 - offset x, 0, -offset z) - 91
      const r = WorldGen.tplPos(3 - ox, -oz, rot, px, pz), h = this.surfH(p.x + r[0], p.z + r[1]);
      p.y += h - 90 - 1;
      return p;
    };
    if (rnd() < 0.5) {
      const n = ((rnd() * 8) | 0) + 4;
      const lab = piece('igloo/bottom', n * 3);
      lab.marker = function (name, x, y, z, i) { if (name === 'chest') this.lootChest(i - 256, LOOT.IGLOO); };
      out.push(lab);
      for (let j = 0; j < n - 1; j++) out.push(piece('igloo/middle', j * 3));
    }
    const top = piece('igloo/top', 0);
    // without the shaft the trapdoor spot becomes snow
    top.after = function (p) {
      const r = WorldGen.tplPos(3, 5, p.rot, p.px, p.pz), x = p.x + r[0], z = p.z + r[1], y = p.y;
      if (x < this.x0 || x > this.x0 + 15 || z < this.z0 || z > this.z0 + 15) return;
      const i = CI(x - this.x0, y, z - this.z0), under = this.ids[i - 256];
      if (under && dryId(under) !== B.LADDER) { this.ids[i] = B.SNOW; this.meta[i] = 0; }
    };
    out.push(top);
    return out;
  }

  // ShipwreckStructure / ShipwreckPieces: a random wreck (beached ones on beaches, half buried in
  // the sand) laid on the average ocean floor of its area, loot chests by marker
  shipwreckStart(cx, cz) {
    const bio = this.biomeAt(cx * 16, cz * 16), bp = BPROP[bio];
    const beached = bio === BI.BEACH || bio === BI.SNOWY_BEACH;
    if (!beached && !(bp && bp.ocean && bio !== BI.RIVER && bio !== BI.FROZEN_RIVER)) return null;
    const rnd = mulberry(hash2i(this.seed + 61294733 * 3, cx, cz)), rot = (rnd() * 4) | 0;
    const list = beached ? SHIPWRECK_BEACHED : SHIPWRECK_OCEAN, name = 'shipwreck/' + list[(rnd() * list.length) | 0];
    const T = this.tpl(name), X = cx * 16, Z = cz * 16;
    let sum = 0, mn = 1e9;
    for (let z = 0; z < T.sz; z++) for (let x = 0; x < T.sx; x++) { const h = beached ? this.surfH(X + x, Z + z) : this.floorH(X + x, Z + z); sum += h; mn = Math.min(mn, h); }
    const y = beached ? mn - (T.sy >> 1) - ((rnd() * 3) | 0) : Math.floor(sum / (T.sx * T.sz));
    const p = { name, x: X, y, z: Z, rot, px: 4, pz: 15, integrity: 1, pal: (rnd() * T.pals.length) | 0 };
    p.marker = function (name, x, y, z, i) { const t = SHIP_LOOT[name]; if (t) this.lootChest(i - 256, t); };
    return [p];
  }

  // OceanRuinStructure / OceanRuinPieces: cold ruins (brick with cracked 0.7 and mossy 0.5 overlays)
  // or warm sandstone ones; 30% large, those mostly with 4..8 small ruins around
  oceanRuinStart(cx, cz) {
    const bio = this.biomeAt(cx * 16, cz * 16);
    const warm = RUIN_WARM.has(bio), cold = RUIN_COLD.has(bio);
    if (!warm && !cold) return null;
    const rnd = mulberry(hash2i(this.seed + 29467103 * 3, cx, cz)), ri = (n) => (rnd() * n) | 0, nI = (a, b) => a + ri(b - a + 1);
    const X = cx * 16, Z = cz * 16, out = [];
    let salt = 0;
    const add = (x, z, rot, large, integrity) => {
      const mk = (name, integ) => {
        const p = { name, x, y: 0, z, rot, px: 0, pz: 0, integrity: integ, salt: 900 + (salt++) * 7, large };
        // height: ocean floor at the corner, lowered onto the floor across the footprint (getHeight)
        const T = this.tpl(name), c = WorldGen.tplPos(T.sx - 1, T.sz - 1, rot, 0, 0);
        const yy = this.floorH(x, z);
        let j = 512, l = 0;
        const xa = Math.min(x, x + c[0]), xb = Math.max(x, x + c[0]), za = Math.min(z, z + c[1]), zb = Math.max(z, z + c[1]);
        for (let qz = za; qz <= zb; qz++) for (let qx = xa; qx <= xb; qx++) {
          const k1 = Math.min(yy - 1, this.floorH(qx, qz) - 1);
          j = Math.min(j, k1); if (k1 < yy - 3) l++;
        }
        p.y = (yy - 1 - j > 2 && l > Math.abs(c[0]) - 2) ? j + 1 : yy;
        p.marker = function (name, mx, my, mz, i) {
          if (name === 'chest') { const w = isWaterId(this.ids[i]); this.ids[i] = w ? WET[B.CHEST] : B.CHEST; this.meta[i] = 2 | ((large ? LOOT.UNDERWATER_RUIN_BIG : LOOT.UNDERWATER_RUIN_SMALL) << 2); }
          else if (name === 'drowned') { const w = isWaterId(this.ids[i - 256]); this.ids[i] = w ? B.WATER : 0; this.meta[i] = 0; }
        };
        out.push(p);
      };
      if (warm) mk('underwater_ruin/' + (large ? 'big_warm_' + (4 + ri(4)) : 'warm_' + (1 + ri(8))), integrity);
      else {
        const i = large ? 1 + ri(3) : 1 + ri(8), pre = large ? 'big_' : '';
        mk('underwater_ruin/' + pre + 'brick_' + i, integrity);
        mk('underwater_ruin/' + pre + 'cracked_' + i, 0.7);
        mk('underwater_ruin/' + pre + 'mossy_' + i, 0.5);
      }
    };
    const rot = ri(4), large = rnd() <= 0.3;
    add(X, Z, rot, large, large ? 0.9 : 0.8);
    if (large && rnd() <= 0.9) {
      const c = WorldGen.tplPos(15, 15, rot, 0, 0), bx0 = Math.min(X, X + c[0]), bx1 = Math.max(X, X + c[0]), bz0 = Math.min(Z, Z + c[1]), bz1 = Math.max(Z, Z + c[1]);
      const ax = bx0, az = bz0;
      const spots = [[-16 + nI(1, 8), 16 + nI(1, 7)], [-16 + nI(1, 8), nI(1, 7)], [-16 + nI(1, 8), -16 + nI(4, 8)], [nI(1, 7), 16 + nI(1, 7)],
        [nI(1, 7), -16 + nI(4, 6)], [16 + nI(1, 7), 16 + nI(3, 8)], [16 + nI(1, 7), nI(1, 7)], [16 + nI(1, 7), -16 + nI(4, 8)]].map(([a, b]) => [ax + a, az + b]);
      for (let n = nI(4, 8); n > 0 && spots.length; n--) {
        const [qx, qz] = spots.splice(ri(spots.length), 1)[0], r1 = ri(4), e = WorldGen.tplPos(5, 6, r1, 0, 0);
        const ex0 = Math.min(qx, qx + e[0]), ex1 = Math.max(qx, qx + e[0]), ez0 = Math.min(qz, qz + e[1]), ez1 = Math.max(qz, qz + e[1]);
        if (!(ex0 <= bx1 && ex1 >= bx0 && ez0 <= bz1 && ez1 >= bz0)) add(qx, qz, r1, false, 0.8);
      }
    }
    return out;
  }

  // ---------------------------------------------------------------- villages (jigsaw structures)
  // JigsawPlacement as in Minecraft: a random town centre from the village type's start pool, its
  // centre projected to the surface, then breadth-first up to 6 levels: every jigsaw block of a
  // piece tries the templates of its pool (shuffled by weight, the fallback pool last / alone at the
  // last level) in shuffled rotations until one whose matching jigsaw joins it fits in the free
  // space (80 blocks around the centre minus the pieces placed; inside its parent for jigsaws
  // facing inward). Rigid pieces keep the parent's height, terrain-matching ones (streets) follow
  // the ground. Pieces and junctions also shape the terrain (Beardifier).
  vilPool(name) { return JIG_POOLS[name]; }
  vilTpl(name) {
    let T = TPL_CACHE.get(name);
    if (T) return T;
    T = this.tpl(name, JIG_TPL[name]);
    const S = JIG_TPL[name];
    T.jig = S.j; T.rules = S.r; T.loot = S.l;
    return T;
  }
  // template's box when placed at (x, y, z) with a rotation about its origin
  static tplBox(T, x, y, z, rot) {
    const a = WorldGen.tplPos(0, 0, rot, 0, 0), b = WorldGen.tplPos(T.sx - 1, T.sz - 1, rot, 0, 0);
    return [x + Math.min(a[0], b[0]), y, z + Math.min(a[1], b[1]), x + Math.max(a[0], b[0]), y + T.sy - 1, z + Math.max(a[1], b[1])];
  }
  static rotDir(d, r) { return d < 4 ? (d + r) & 3 : d; }
  // the pool's elements, each repeated by its weight, shuffled
  vilShuffled(pool, rnd) {
    const out = [];
    if (!pool) return out;
    for (const e of pool.e) for (let k = 0; k < e[2]; k++) out.push(e);
    for (let i = out.length - 1; i > 0; i--) { const j = (rnd() * (i + 1)) | 0, t = out[i]; out[i] = out[j]; out[j] = t; }
    return out;
  }
  vilMaxY(name) {
    const P = this.vilPool(name);
    if (!P) return 0;
    if (P.maxY === undefined) {
      P.maxY = 0;
      for (const e of P.e) {
        if (e[0] === 0 || e[0] === 3) { const b = WorldGen.vilElBox(this, e, 0, 0, 0, 0); P.maxY = Math.max(P.maxY, b[4] - b[1] + 1); }
        else if (e[0] === 1) P.maxY = Math.max(P.maxY, 1);
      }
    }
    return P.maxY;
  }
  // jigsaws of an element placed at (x, y, z) with rotation r: [x, y, z, front, top, name, target, pool, final, rollable] in world space, shuffled
  vilJigsaws(e, x, y, z, r, rnd) {
    // FeaturePoolElement: one jigsaw facing down named "bottom"
    if (e[0] === 1) return [[x, y, z, 5, 0, 'bottom', 'empty', 'empty', 0, 1]];
    if (e[0] !== 0 && e[0] !== 3) return [];
    const T = this.vilTpl(e[0] === 3 ? e[1][0] : e[1]), out = [];
    for (const j of T.jig) { const o = WorldGen.tplPos(j[0], j[2], r, 0, 0); out.push([x + o[0], y + j[1], z + o[1], WorldGen.rotDir(j[3], r), WorldGen.rotDir(j[4], r), JIG_STR[j[5]], JIG_STR[j[6]], JIG_STR[j[7]], j[8], j[9]]); }
    for (let i = out.length - 1; i > 0; i--) { const k = (rnd() * (i + 1)) | 0, t = out[i]; out[i] = out[k]; out[k] = t; }
    return out;
  }
  static vilElBox(gen, e, x, y, z, r) {
    if (e[0] === 0) return WorldGen.tplBox(gen.vilTpl(e[1]), x, y, z, r);
    if (e[0] === 3) {   // ListPoolElement: the union of its elements
      const b = WorldGen.tplBox(gen.vilTpl(e[1][0]), x, y, z, r);
      for (let i = 1; i < e[1].length; i++) { const c = WorldGen.tplBox(gen.vilTpl(e[1][i]), x, y, z, r); for (let k = 0; k < 3; k++) { b[k] = Math.min(b[k], c[k]); b[k + 3] = Math.max(b[k + 3], c[k + 3]); } }
      return b;
    }
    return [x, y, z, x, y, z];
  }
  // JigsawStructure start: for structure sets with several structures (the village types) they are
  // tried in a random order and the first whose biome holds at the start's centre generates
  jigsawStart(set, cx, cz) {
    const order = set.types.slice(), r0 = mulberry(hash2i(this.seed + set.salt * 5, cx, cz));
    for (let i = order.length - 1; i > 0; i--) { const j = (r0() * (i + 1)) | 0, t = order[i]; order[i] = order[j]; order[j] = t; }
    for (const type of order) {
      const rnd = mulberry(hash2i(this.seed + set.salt * 3, cx, cz));
      const rot = (rnd() * 4) | 0, start = this.vilPool(type.pool);
      const list = this.vilShuffled(start, rnd), e = list[(rnd() * list.length) | 0];
      if (!e || e[0] === 2) continue;
      const X = cx * 16, Z = cz * 16, box = WorldGen.vilElBox(this, e, X, 0, Z, rot);
      const mx = (box[0] + box[3]) >> 1, mz = (box[2] + box[5]) >> 1;
      if (!type.biomes.includes(this.biomeAt(mx, mz))) continue;
      // projected to WORLD_SURFACE_WG at the centre, the floor (ground level delta 1) one below
      const k = this.surfH(mx, mz);
      const first = { e, x: X, y: k - 1, z: Z, rot, box: WorldGen.vilElBox(this, e, X, k - 1, Z, rot), gd: 1, junctions: [] };
      return this.vilAssemble(first, mx, k, mz, rnd, set.depth);
    }
    return null;
  }
  vilAssemble(first, cx, cy, cz, rnd, MAXD) {
    const D = 80, pieces = [first];
    // a building (rigid template with a real footprint) also claims the ground under it, so no
    // lower piece (a farm on a lower street) ends up beneath its floor
    const claim = (e, b) => {
      if (e[0] !== 0 || e[3] !== 0) return b;
      const T = this.vilTpl(e[1]);
      return T.sx * T.sz >= 9 ? [b[0], b[1] - 32, b[2], b[3], b[4], b[5]] : b;
    };
    // free space: an outer box minus placed boxes (VoxelShape ONLY_FIRST joins), shared as in Minecraft
    const outer = { o: [cx - D, cy - D, cz - D, cx + D + 1, cy + D + 1, cz + D + 1], holes: [box6(claim(first.e, first.box))] };
    const fits = (sh, b) => {
      const a = [b[0] + 0.25, b[1] + 0.25, b[2] + 0.25, b[3] + 0.75, b[4] + 0.75, b[5] + 0.75], o = sh.o;
      if (a[0] < o[0] || a[1] < o[1] || a[2] < o[2] || a[3] > o[3] || a[4] > o[4] || a[5] > o[5]) return false;
      for (const h of sh.holes) if (a[0] < h[3] && a[3] > h[0] && a[1] < h[4] && a[4] > h[1] && a[2] < h[5] && a[5] > h[2]) return false;
      return true;
    };
    const queue = [[first, outer, 0]];
    while (queue.length) {
      const [piece, free, depth] = queue.shift();
      const rigid = piece.e[3] === 0, box = piece.box, i0 = box[1];
      let inner = null;
      for (const jg of this.vilJigsaws(piece.e, piece.x, piece.y, piece.z, piece.rot, rnd)) {
        const fd = jg[3], tx = jg[0] + (fd === 3 ? 1 : fd === 1 ? -1 : 0), ty = jg[1] + (fd === 4 ? 1 : fd === 5 ? -1 : 0), tz = jg[2] + (fd === 0 ? 1 : fd === 2 ? -1 : 0);
        const j = jg[1] - i0;
        let k = -1;
        const poolName = jg[7], pool = this.vilPool(poolName);
        if (!pool && poolName !== 'empty') continue;
        const fb = pool ? this.vilPool(pool.f) : null;
        const inside = tx >= box[0] && tx <= box[3] && ty >= box[1] && ty <= box[4] && tz >= box[2] && tz <= box[5];
        let sh;
        if (inside) { if (!inner) inner = { o: box6(box), holes: [] }; sh = inner; } else sh = free;
        const cands = (depth !== MAXD ? this.vilShuffled(pool, rnd) : []).concat(this.vilShuffled(fb, rnd));
        let placed = false;
        for (const e1 of cands) {
          if (e1[0] === 2) break;
          const rots = [0, 1, 2, 3];
          for (let i = 3; i > 0; i--) { const q = (rnd() * (i + 1)) | 0, t = rots[i]; rots[i] = rots[q]; rots[q] = t; }
          for (const r1 of rots) {
            const js1 = this.vilJigsaws(e1, 0, 0, 0, r1, rnd), b1 = WorldGen.vilElBox(this, e1, 0, 0, 0, r1);
            let exp = 0;
            if ((e1[0] === 0 || e1[0] === 3) && b1[4] - b1[1] + 1 <= 16) {
              for (const q of js1) {
                const f2 = q[3], qx = q[0] + (f2 === 3 ? 1 : f2 === 1 ? -1 : 0), qy = q[1] + (f2 === 4 ? 1 : f2 === 5 ? -1 : 0), qz = q[2] + (f2 === 0 ? 1 : f2 === 2 ? -1 : 0);
                if (!(qx >= b1[0] && qx <= b1[3] && qy >= b1[1] && qy <= b1[4] && qz >= b1[2] && qz <= b1[5])) continue;
                const pn = q[7], P = this.vilPool(pn);
                exp = Math.max(exp, this.vilMaxY(pn), P ? this.vilMaxY(P.f) : 0);
              }
            }
            for (const j1 of js1) {
              // JigsawBlock.canAttach: facing each other, same top unless rollable, target -> name
              if (j1[3] !== ((fd < 4) ? (fd + 2) & 3 : fd === 4 ? 5 : 4)) continue;
              if (!jg[9] && jg[4] !== j1[4]) continue;
              if (jg[6] !== j1[5]) continue;
              const ox = tx - j1[0], oz = tz - j1[2];
              const rigid1 = e1[3] === 0, k1 = j1[1], l1 = j - k1 + (fd === 4 ? 1 : fd === 5 ? -1 : 0);
              let i2;
              if (rigid && rigid1) i2 = i0 + l1;
              else { if (k === -1) k = this.surfH(jg[0], jg[2]); i2 = k - k1; }
              const y1 = i2;      // template minY is its origin: box minY = position y
              const b3 = WorldGen.vilElBox(this, e1, ox, y1, oz, r1);
              const bt = b3.slice();
              if (exp > 0) bt[4] = Math.max(bt[4], bt[1] + Math.max(exp + 1, b3[4] - b3[1]));
              const bc = sh === free ? claim(e1, bt) : bt;
              if (!fits(sh, bc)) continue;
              sh.holes.push(box6(bc));
              const gd = rigid1 ? piece.gd - l1 : 1;   // StructurePoolElement.getGroundLevelDelta() is 1
              const p1 = { e: e1, x: ox, y: y1, z: oz, rot: r1, box: bt, gd, junctions: [] };
              let i3;
              if (rigid) i3 = i0 + j;
              else if (rigid1) i3 = i2 + k1;
              else { if (k === -1) k = this.surfH(jg[0], jg[2]); i3 = k + Math.trunc(l1 / 2); }
              piece.junctions.push([tx, i3 - j + piece.gd, tz]);
              p1.junctions.push([jg[0], i3 - k1 + gd, jg[2]]);
              pieces.push(p1);
              if (depth + 1 <= MAXD) queue.push([p1, sh, depth + 1]);
              placed = true; break;
            }
            if (placed) break;
          }
          if (placed) break;
        }
      }
    }
    return pieces.map((p) => ({ vil: p, box: p.box }));
  }
  // blocks of a village piece inside this chunk: template states through the element's processors,
  // jigsaws replaced by their final state, streets laid on the ground (GravityProcessor -1)
  placeVillagePiece(p) {
    const v = p.vil, e = v.e, x0 = this.x0, z0 = this.z0, ids = this.ids, meta = this.meta, seed = this.seed;
    if (e[0] === 1) { this.villageFeature(e[1], v.x, v.y, v.z); return; }
    if (e[0] === 3) { for (let i = 0; i < e[1].length; i++) this.placeJigsawTemplate(v, e[1][i], e[4][i], e[3] === 1); return; }
    if (e[0] === 0) this.placeJigsawTemplate(v, e[1], e[4], e[3] === 1);
  }
  placeJigsawTemplate(v, name, proc, terrain) {
    const x0 = this.x0, z0 = this.z0, ids = this.ids, meta = this.meta, seed = this.seed;
    const T = this.vilTpl(name), pal = T.pals[0], rules = proc ? T.rules[proc] : null, rot = rules ? rules.rot : undefined;
    const G = this.vilGround;
    const put = (wx, wy, wz, pi, state) => {
      if (wx < x0 || wx > x0 + 15 || wz < z0 || wz > z0 + 15) return;
      if (terrain) wy = G[(wz - z0) * 16 + wx - x0] + (wy - v.y);
      if (wy <= WORLD_MIN_Y || wy >= WORLD_MAX_Y) return;
      // BlockRotProcessor: each block kept with the list's integrity
      if (rot !== undefined && hash3(seed + 4417, wx, wy, wz) >= rot) return;
      const i = CI(wx - x0, wy, wz - z0), cur = ids[i];
      let ent = pi;
      const R = rules && state >= 0 ? rules[state + ''] : null;
      if (R) {
        const rnd = mulberry(hash3(seed + 4409, wx, wy, wz) * 4294967296 | 0);
        for (const [prob, loc, out] of R) {
          if (prob < 1 && rnd() >= prob) continue;
          if (loc === 1 && !isWaterId(cur)) continue;
          if (loc === 2 && cur !== B.ICE) continue;
          ent = out ? T.palIds[out - 1] : [0, 0]; break;
        }
      }
      let id = ent[0];
      if (id < 0) return;
      if (id && WET[id] && (cur === B.WATER && meta[i] === 0 || isWaterId(cur))) id = WET[id];
      ids[i] = id; meta[i] = id ? rotMeta(id, ent[1], v.rot) : 0;
    };
    const { sx, sy, sz, g } = T;
    for (let y = 0; y < sy; y++) for (let z = 0; z < sz; z++) for (let x = 0; x < sx; x++) {
      const st = g[(y * sz + z) * sx + x];
      if (!st) continue;
      const ent = pal[st - 1];
      if (ent[0] < 0) continue;
      const o = WorldGen.tplPos(x, z, v.rot, 0, 0);
      put(v.x + o[0], v.y + y, v.z + o[1], ent, st - 1);
    }
    for (const j of T.jig) {
      if (!j[8]) continue;
      const o = WorldGen.tplPos(j[0], j[2], v.rot, 0, 0);
      put(v.x + o[0], v.y + j[1], v.z + o[1], T.palIds[j[8] - 1], -1);
    }
    for (const [x, y, z, table] of T.loot) {
      const o = WorldGen.tplPos(x, z, v.rot, 0, 0), wx = v.x + o[0], wz = v.z + o[1];
      if (wx < x0 || wx > x0 + 15 || wz < z0 || wz > z0 + 15) continue;
      const wy = terrain ? this.vilGround[(wz - z0) * 16 + wx - x0] + y : v.y + y, t = LOOT[table.split('/').pop().toUpperCase()];
      if (t && wy > WORLD_MIN_Y && wy < WORLD_MAX_Y) this.lootChest(CI(wx - x0, wy, wz - z0), t);
    }
  }
  // the features village decor pools place: trees, flower / grass / berry / cactus patches and
  // block piles (BlockPileFeature), each from its own position's random
  villageFeature(name, x, y, z) {
    const tree = { oak: 'oak', acacia: 'acacia', spruce: 'spruce', pine: 'pine' }[name];
    if (tree) {
      // placed with the sapling's would_survive filter: soil under it (known only inside this chunk,
      // the tree itself is built from its position alone, so the neighbours agree)
      const g = this.floorH(x, z) - 1;
      if (g === y - 1) this.placeTree(this.buildTree(tree, x, y - 1, z));
      return;
    }
    const rnd = mulberry(hash3(this.seed + 4421, x, y, z) * 4294967296 | 0), ri = (n) => (rnd() * n) | 0;
    const x0 = this.x0, z0 = this.z0, ids = this.ids, meta = this.meta;
    const at = (X, Y, Z) => X >= x0 && X < x0 + 16 && Z >= z0 && Z < z0 + 16 && Y > WORLD_MIN_Y && Y < WORLD_MAX_Y ? CI(X - x0, Y, Z - z0) : -1;
    if (name.startsWith('pile_')) {
      const pick = {
        pile_hay: () => [B.HAY_BLOCK, ri(3)], pile_melon: () => [B.MELON, 0], pile_snow: () => [B.SNOW_LAYER, 0],
        pile_ice: () => [rnd() < 1 / 6 ? B.BLUE_ICE : B.PACKED_ICE, 0], pile_pumpkin: () => [rnd() < 0.95 ? B.PUMPKIN : B.JACK_O_LANTERN, ri(4)],
      }[name];
      if (!pick || y < WORLD_MIN_Y + 5) return;
      const i0 = 2 + ri(2), j0 = 2 + ri(2);
      for (let yy = y; yy <= y + 1; yy++) for (let zz = z - j0; zz <= z + j0; zz++) for (let xx = x - i0; xx <= x + i0; xx++) {
        const dx = x - xx, dz = z - zz;
        const ok = dx * dx + dz * dz <= rnd() * 10 - rnd() * 6 || rnd() < 0.031;
        if (!ok) continue;
        const i = at(xx, yy, zz), below = at(xx, yy - 1, zz);
        if (i < 0 || below < 0) { pick(); continue; }
        const b = ids[below];
        if (b === B.DIRT_PATH ? rnd() < 0.5 : OPAQUE[b]) { const [id, m] = pick(); if (!ids[i] || (FLAGS[ids[i]] & BF_REPLACE)) { ids[i] = id; meta[i] = m; } }
      }
      return;
    }
    // RandomPatchFeature: tries at trapezoid offsets (xz -7..7 / -6..6, y -3..3 / -2..2) onto suitable ground
    const P = { flower_plain: [64, 6, 2], patch_taiga_grass: [32, 7, 3], patch_berry_bush: [96, 7, 3], patch_cactus: [10, 7, 3] }[name];
    if (!P) return;
    const tz = (n) => ri(n + 1) - ri(n + 1);
    for (let t = 0; t < P[0]; t++) {
      const X = x + tz(P[1]), Y = y + tz(P[2]), Z = z + tz(P[1]);
      const i = at(X, Y, Z);
      if (i < 0 || ids[i]) continue;
      const below = ids[i - 256];
      if (name === 'patch_cactus') {
        if (below !== B.SAND && below !== B.RED_SAND) continue;
        const h = 1 + ri(ri(3) + 1);
        for (let k = 0; k < h; k++) { const q = at(X, Y + k, Z); if (q < 0 || ids[q]) break; ids[q] = B.CACTUS; meta[q] = 0; }
        continue;
      }
      if (below !== B.GRASS && below !== B.DIRT && below !== B.PODZOL && below !== B.COARSE_DIRT) continue;
      if (name === 'patch_berry_bush') { if (below === B.GRASS) { ids[i] = B.SWEET_BERRY_BUSH; meta[i] = 3; } continue; }
      if (name === 'patch_taiga_grass') { ids[i] = ri(5) === 0 ? B.TALL_GRASS : B.FERN; meta[i] = 0; continue; }
      if (below === B.GRASS) { ids[i] = [B.DANDELION, B.POPPY, B.AZURE_BLUET, B.OXEYE_DAISY, B.RED_TULIP, B.ORANGE_TULIP, B.WHITE_TULIP, B.PINK_TULIP][ri(8)]; meta[i] = 0; }
    }
  }
  // a tree's trunk needs free space (TreeFeature): none grows where a village piece stands
  inVillage(x, y, z) {
    for (const b of this.vilBoxes) if (x >= b[0] && x <= b[3] && z >= b[2] && z <= b[5] && y <= b[4] + 1 && y >= b[1] - 8) return true;
    return false;
  }
  // Beardifier: terrain pushed toward the village: filled below and cleared above each rigid piece's
  // ground level and around junctions, with Minecraft's kernel (radius 12)
  villageBeard() {
    const cx = this.cx, cz = this.cz, x0 = this.x0, z0 = this.z0, near = [], junc = [], all = this.vilBoxes = [];
    for (const set of JIGSAW_SETS) {
      const r0x = Math.floor((cx - 7) / set.spacing), r1x = Math.floor((cx + 7) / set.spacing), r0z = Math.floor((cz - 7) / set.spacing), r1z = Math.floor((cz + 7) / set.spacing);
      for (let rz = r0z; rz <= r1z; rz++) for (let rx = r0x; rx <= r1x; rx++) {
        const [sx, sz] = this.spreadStart(set, rx, rz);
        if (Math.abs(sx - cx) > 7 || Math.abs(sz - cz) > 7) continue;
        const st = this.structStart(set, sx, sz);
        if (!st) continue;
        for (const p of st) {
          const b = p.box;
          if (b[0] > x0 + 27 || b[3] < x0 - 12 || b[2] > z0 + 27 || b[5] < z0 - 12) continue;
          all.push(b);
          if (p.vil.e[3] === 0) near.push(p);
          for (const j of p.vil.junctions) if (j[0] >= x0 - 12 && j[0] <= x0 + 27 && j[2] >= z0 - 12 && j[2] <= z0 + 27) junc.push(j);
        }
      }
    }
    this.beardPieces = near;
    if (!near.length && !junc.length) return null;
    let y0 = 1e9, y1 = -1e9;
    for (const p of near) { y0 = Math.min(y0, p.box[1] - 12); y1 = Math.max(y1, p.box[4] + 12); }
    for (const j of junc) { y0 = Math.min(y0, j[1] - 12); y1 = Math.max(y1, j[1] + 12); }
    y0 = Math.max(y0, WORLD_MIN_Y + 1); y1 = Math.min(y1, WORLD_MAX_Y - 1);
    const H = y1 - y0 + 1, A = new Float32Array(256 * H);
    for (const p of near) {
      const b = p.box, gy = b[1] + p.vil.gd;
      for (let z = 0; z < 16; z++) {
        const dz = Math.max(0, b[2] - (z0 + z), z0 + z - b[5]);
        if (dz >= 12) continue;
        for (let x = 0; x < 16; x++) {
          const dx = Math.max(0, b[0] - (x0 + x), x0 + x - b[3]);
          if (dx >= 12) continue;
          for (let dy = -12; dy < 12; dy++) {
            const y = gy + dy;
            if (y < y0 || y > y1) continue;
            A[(y - y0) * 256 + z * 16 + x] += BEARD_KERNEL[(dz + 12) * 576 + (dx + 12) * 24 + dy + 12] * 0.8;
          }
        }
      }
    }
    for (const j of junc) {
      for (let z = 0; z < 16; z++) {
        const dz = z0 + z - j[2];
        if (dz < -12 || dz >= 12) continue;
        for (let x = 0; x < 16; x++) {
          const dx = x0 + x - j[0];
          if (dx < -12 || dx >= 12) continue;
          for (let dy = -12; dy < 12; dy++) {
            const y = j[1] + dy;
            if (y < y0 || y > y1) continue;
            A[(y - y0) * 256 + z * 16 + x] += BEARD_KERNEL[(dz + 12) * 576 + (dx + 12) * 24 + dy + 12] * 0.4;
          }
        }
      }
    }
    return { y0, y1, A };
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
  // Tree attempts of a chunk (Minecraft's count / in-square placement per biome present in the
  // chunk, a biome filter at the column). Only seed-derived data is used, so every chunk a tree
  // overlaps makes the same decision. Map: column key -> type.
  treeAttempts(cx, cz) {
    const key = cx * 65536 + cz, C = this.attemptCache || (this.attemptCache = new Map());
    let A = C.get(key);
    if (A) return A;
    A = new Map();
    const x0 = cx * 16, z0 = cz * 16, present = new Set();
    for (let i = 0; i < 16; i++) present.add(this.colInfo(x0 + (i & 3) * 4 + 2, z0 + (i >> 2) * 4 + 2).biome);
    for (const bio of present) {
      const T = MC_TREES[bio];
      if (!T) continue;
      const rnd = mulberry(hash2i(this.seed + 23 + bio * 7919, cx, cz));
      const n = T.count + (rnd() < T.chance ? T.extra : 0);
      for (let k = 0; k < n; k++) {
        const x = x0 + ((rnd() * 16) | 0), z = z0 + ((rnd() * 16) | 0);
        let t = null;
        for (const e of T.types) if (e[0] >= 1 || rnd() < e[0]) { t = e[1]; break; }
        const ck = x * 131072 + z;
        if (!t || A.has(ck) || this.colInfo(x, z).biome !== bio) continue;
        A.set(ck, t);
      }
    }
    if (C.size > 4096) C.clear();
    C.set(key, A);
    return A;
  }
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
    const t = this.treeAttempts(x >> 4, z >> 4).get(x * 131072 + z);
    if (!t) return 0;
    if (ci.biome === BI.WOODED_BADLANDS && ci.h < SEA + 10) return 0;
    if (ob === BI.WINDSWEPT_HILLS && this.windswept(x, z, ci.h) >= 0.53) return 0;
    return t;
  }
  // Spacing: a candidate gives way to a higher-priority one whose trunk would share its columns
  // (2x2 trunks, cacti); deterministic.
  treeAt(x, z) {
    const t = this.treeCandidate(x, z);
    if (!t) return 0;
    const rk = hash2(this.seed + 24, x, z), rt = TREE_ROOM[t] || 1;
    for (let dz = -2; dz <= 2; dz++) for (let dx = -2; dx <= 2; dx++) {
      if (!dx && !dz) continue;
      const d = Math.max(dx < 0 ? -dx : dx, dz < 0 ? -dz : dz);
      if (d >= Math.max(rt, 2)) continue;
      const tn = this.treeCandidate(x + dx, z + dz);
      if (!tn || d >= Math.max(rt, TREE_ROOM[tn] || 1)) continue;
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
    if (t === 'dark_oak' || t === 'pale_oak' || t === 'mega_spruce' || t === 'mega_pine' || t === 'mega_jungle') {
      // 2x2 trunk: every column needs ground no more than 3 blocks below the origin
      for (const [dx, dz] of [[1, 0], [0, 1], [1, 1]]) { const g = this.groundAt(x + dx, z + dz); if (g < sy - 3 || g > sy + 3) return null; }
    }
    return { t, sy };
  }

  features() {
    const x0 = this.x0, z0 = this.z0, M = 9;   // the widest crowns (fancy oak, cherry, jungle branches) reach 9 out
    this.deco.length = 0; this.treeWrites = [];
    for (let z = z0 - M; z < z0 + 16 + M; z++) for (let x = x0 - M; x < x0 + 16 + M; x++) {
      // a tree reaches several chunks: build it once, then every chunk takes its part
      const k = x * 131072 + z, TC = this.treeCache;
      let r = TC.get(k);
      if (r === undefined) {
        const spot = this.treeSpot(x, z);
        r = spot && !this.inVillage(x, spot.sy + 1, z) ? this.buildTree(spot.t, x, spot.sy, z) : null;
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
    let vineChance = 0, propagules = false, mossOnRoots = false, trunkVines = false, podzol = false, paleMoss = false;
    // FoliagePlacer.placeLeavesRow: the square of `range` around (cx, cy + yo, cz) (one more column
    // on the + sides for a 2x2 trunk), minus the cells skip(|dx|, yo, |dz|, range, large) drops
    const row = (cx, cy, cz, range, yo, large, id, skip) => {
      if (range < 0) return;
      const e = large ? 1 : 0;
      for (let dx = -range; dx <= range + e; dx++) for (let dz = -range; dz <= range + e; dz++) {
        const ax = large ? Math.min(Math.abs(dx), Math.abs(dx - 1)) : Math.abs(dx), az = large ? Math.min(Math.abs(dz), Math.abs(dz - 1)) : Math.abs(dz);
        if (skip(ax, yo, az, range, large)) continue;
        put(cx + dx, cy + yo, cz + dz, id, 1);
      }
    };
    // the same with the signed offsets (dark oak's wide middle row loses its outermost ring corners)
    const rowSigned = (cx, cy, cz, range, yo, large, id, skip) => {
      const e = large ? 1 : 0;
      for (let dx = -range; dx <= range + e; dx++) for (let dz = -range; dz <= range + e; dz++) {
        if (skip(dx, yo, dz, range)) continue;
        put(cx + dx, cy + yo, cz + dz, id, 1);
      }
    };
    // BlobFoliagePlacer: rows offset .. offset - height, radius shrinking upward
    const blob = (cx, cy, cz, radius, offset, height, id, large) => {
      for (let i = offset; i >= offset - height; i--) {
        const j = Math.max(radius - 1 - Math.trunc(i / 2), 0);
        row(cx, cy, cz, j, i, large, id, (ax, yy, az, r) => ax === r && az === r && (nextInt(2) === 0 || yy === 0));
      }
    };
    const straight = (h, id) => { for (let y = sy + 1; y <= sy + h; y++) log(x, y, z, id); soil(x, sy, z); };
    // GiantTrunkPlacer: 2x2 trunk, every column down to its own ground
    const giant = (h, id) => {
      for (let d = 0; d < 4; d++) {
        const cx = x + (d & 1), cz = z + (d >> 1), gy = d ? this.groundAt(cx, cz) : sy;
        for (let y = gy + 1; y <= sy + h; y++) log(cx, y, cz, id);
        soil(cx, gy, cz);
      }
    };
    switch (type) {
      case 'cactus': {
        const n = 2 + ((g(1) * 2) | 0);
        for (let y = sy + 1; y <= sy + n; y++) log(x, y, z, B.CACTUS);
        break;
      }
      // ---- Minecraft's trunk and foliage placers (TreeFeatures) ----
      case 'oak': case 'birch': case 'super_birch': case 'swamp_oak': case 'jungle': {
        // StraightTrunkPlacer(base, a, b) + BlobFoliagePlacer(radius, 0, 3)
        const P = { oak: [4, 2, 0, 2, B.LOG, B.LEAVES], birch: [5, 2, 0, 2, B.BIRCH_LOG, B.BIRCH_LEAVES], super_birch: [5, 2, 6, 2, B.BIRCH_LOG, B.BIRCH_LEAVES],
          swamp_oak: [5, 3, 0, 3, B.LOG, B.LEAVES], jungle: [4, 8, 0, 2, B.JUNGLE_LOG, B.JUNGLE_LEAVES] }[type];
        const h = P[0] + nextInt(P[1] + 1) + nextInt(P[2] + 1);
        straight(h, P[4]);
        blob(x, sy + 1 + h, z, P[3], 0, 3, P[5], false);
        if (type === 'swamp_oak' || type === 'jungle') vineChance = 0.25;
        if (type === 'jungle') trunkVines = true;
        break;
      }
      case 'jungle_bush': {
        // StraightTrunkPlacer(1, 0, 0) + BushFoliagePlacer(2, 1, 2): one jungle log in an oak-leaf bush
        straight(1, B.JUNGLE_LOG);
        const ay = sy + 2;
        for (let i = 1; i >= -1; i--) row(x, ay, z, 2 - 1 - i, i, false, B.LEAVES, (ax, yy, az, r) => ax === r && az === r && nextInt(2) === 0);
        break;
      }
      case 'fancy_oak': {
        // FancyTrunkPlacer(3, 11, 0) + FancyFoliagePlacer(2, 4, 4)
        const height = 3 + nextInt(12), i = height + 2, j = Math.floor(i * 0.618);
        const by = sy + 1, l = by + j;
        soil(x, sy, z);
        const limb = (ax, ay, az, bx, byy, bz, place) => {
          const dx = bx - ax, dy = byy - ay, dz = bz - az, n = Math.max(Math.abs(dx), Math.abs(dy), Math.abs(dz));
          const axis = Math.abs(dx) > Math.abs(dz) && Math.abs(dx) > Math.abs(dy) ? 1 : Math.abs(dz) > Math.abs(dx) && Math.abs(dz) > Math.abs(dy) ? 2 : 0;
          for (let k = 0; k <= n; k++) {
            const px = ax + Math.floor(0.5 + k * (n ? dx / n : 0)), py = ay + Math.floor(0.5 + k * (n ? dy / n : 0)), pz = az + Math.floor(0.5 + k * (n ? dz / n : 0));
            if (place) put(px, py, pz, B.LOG | (axis << 16), 0);
            else if (this.solidTerrain(px, py, pz)) return false;
          }
          return true;
        };
        const shape = (k) => {
          if (k < i * 0.3) return -1;
          const f = i / 2, f1 = f - k;
          let f2 = Math.sqrt(f * f - f1 * f1);
          if (f1 === 0) f2 = f; else if (Math.abs(f1) >= f) return 0;
          return f2 * 0.5;
        };
        const coords = [[x, by + i - 5, z, l]];
        for (let i1 = i - 5; i1 >= 0; i1--) {
          const f = shape(i1);
          if (f < 0) continue;
          const d1 = f * (rnd() + 0.328), d2 = rnd() * 2 * Math.PI;
          const bx = x + Math.floor(d1 * Math.sin(d2) + 0.5), bz = z + Math.floor(d1 * Math.cos(d2) + 0.5), byy = by + i1 - 1;
          if (!limb(bx, byy, bz, bx, byy + 5, bz, false)) continue;
          const k1 = x - bx, l1 = z - bz, d5 = byy - Math.sqrt(k1 * k1 + l1 * l1) * 0.381;
          const i2 = d5 > l ? l : Math.trunc(d5);
          if (limb(x, i2, z, bx, byy, bz, false)) coords.push([bx, byy, bz, i2]);
        }
        limb(x, by, z, x, by + j, z, true);
        for (const c of coords) if (c[3] - by >= i * 0.2 && !(c[0] === x && c[1] === c[3] && c[2] === z)) limb(x, c[3], z, c[0], c[1], c[2], true);
        for (const c of coords) {
          if (!(c[3] - by >= i * 0.2)) continue;
          for (let yy = 4; yy >= 0; yy--) row(c[0], c[1], c[2], 2 + (yy !== 4 && yy !== 0 ? 1 : 0), yy, false, B.LEAVES, (ax, y2, az, r) => (ax + 0.5) * (ax + 0.5) + (az + 0.5) * (az + 0.5) > r * r);
        }
        break;
      }
      case 'spruce': case 'pine': {
        const spruce = type === 'spruce';
        // spruce: StraightTrunkPlacer(5, 2, 1) + SpruceFoliagePlacer(2..3, 0..2, 1..2);
        // pine: StraightTrunkPlacer(6, 4, 0) + PineFoliagePlacer(1, 1, 3..4)
        const h = spruce ? 5 + nextInt(3) + nextInt(2) : 6 + nextInt(5);
        straight(h, B.SPRUCE_LOG);
        const ay = sy + 1 + h, corner = (ax, yy, az, r) => ax === r && az === r && r > 0;
        if (spruce) {
          const fh = Math.max(4, h - (1 + nextInt(2))), radius = 2 + nextInt(2), offset = nextInt(3);
          let r = nextInt(2), lim = 1, k = 0;
          for (let yy = offset; yy >= -fh; yy--) {
            row(x, ay, z, r, yy, false, B.SPRUCE_LEAVES, corner);
            if (r >= lim) { r = k; k = 1; lim = Math.min(lim + 1, radius); } else r++;
          }
        } else {
          const fh = 3 + nextInt(2), radius = 1 + nextInt(Math.max(h - fh, 1));
          let r = 0;
          for (let yy = 1; yy >= 1 - fh; yy--) {
            row(x, ay, z, r, yy, false, B.SPRUCE_LEAVES, corner);
            if (r >= 1 && yy === 1 - fh + 1) r--; else if (r < radius) r++;
          }
        }
        break;
      }
      case 'mega_spruce': case 'mega_pine': {
        // GiantTrunkPlacer(13, 2, 14) + MegaPineFoliagePlacer(0, 0, 13..17 / 3..7), podzol around
        const h = 13 + nextInt(3) + nextInt(15);
        giant(h, B.SPRUCE_LOG);
        const fh = type === 'mega_spruce' ? 13 + nextInt(5) : 3 + nextInt(5), top = sy + 1 + h;
        let prev = 0;
        for (let yy = top - fh; yy <= top; yy++) {
          const k = top - yy, l = Math.floor(k / fh * 3.5);
          const r = k > 0 && l === prev && (yy & 1) === 0 ? l + 1 : l;
          row(x, yy, z, r, 0, true, B.SPRUCE_LEAVES, (ax, y2, az, rr) => ax + az >= 7 || ax * ax + az * az > rr * rr);
          prev = l;
        }
        podzol = true;
        break;
      }
      case 'mega_jungle': {
        // MegaJungleTrunkPlacer(10, 2, 19) + MegaJungleFoliagePlacer(2, 0, 2), vines
        const h = 10 + nextInt(3) + nextInt(20);
        giant(h, B.JUNGLE_LOG);
        const att = [[x, sy + 1 + h, z, 0, true]];
        for (let i = h - 2 - nextInt(4); i > h / 2; i -= 2 + nextInt(4)) {
          const f = rnd() * Math.PI * 2;
          let bx = 0, bz = 0;
          for (let l = 0; l < 5; l++) {
            bx = Math.trunc(1.5 + Math.cos(f) * l); bz = Math.trunc(1.5 + Math.sin(f) * l);
            put(x + bx, sy + 1 + i - 3 + (l >> 1), z + bz, B.JUNGLE_LOG, 0);
          }
          att.push([x + bx, sy + 1 + i, z + bz, -2, false]);
        }
        const mj = (ax, yy, az, r) => ax + az >= 7 || ax * ax + az * az > r * r;
        for (const [ax, ay, az, ro, dbl] of att) {
          const n = dbl ? 2 : 1 + nextInt(2);
          for (let j = 0; j >= -n; j--) row(ax, ay, az, 2 + ro + 1 - j, j, dbl, B.JUNGLE_LEAVES, mj);
        }
        vineChance = 0.25; trunkVines = true;
        break;
      }
      case 'dark_oak': case 'pale_oak': {
        // DarkOakTrunkPlacer(6, 2, 1) + DarkOakFoliagePlacer(0, 0); the pale oak is the same build in
        // pale wood, with moss hanging from its crown and now and then a creaking heart in the trunk
        const pale = type === 'pale_oak', LG = pale ? B.PALE_OAK_LOG : B.DARK_LOG, LV = pale ? B.PALE_OAK_LEAVES : B.DARK_LEAVES;
        if (pale) paleMoss = true;
        const h = 6 + nextInt(3) + nextInt(2);
        for (let d = 0; d < 4; d++) { const cx = x + (d & 1), cz = z + (d >> 1); soil(cx, this.groundAt(cx, cz), cz); }
        const dir = nextInt(4), sx = [1, -1, 0, 0][dir], sz = [0, 0, 1, -1][dir];
        const bend = h - nextInt(4);
        let steps = 2 - nextInt(3), j1 = x, k1 = z;
        const l1 = sy + 1 + h - 1;
        for (let i2 = 0; i2 < h; i2++) {
          if (i2 >= bend && steps > 0) { j1 += sx; k1 += sz; steps--; }
          const yy = sy + 1 + i2;
          for (let d = 0; d < 4; d++) {
            const cx = j1 + (d & 1), cz = k1 + (d >> 1);
            // the 2x2 trunk reaches down to the ground of each of its columns
            if (i2 === 0 && j1 === x && k1 === z) for (let gy = this.groundAt(cx, cz) + 1; gy < yy; gy++) put(cx, gy, cz, LG, 0);
            put(cx, yy, cz, LG, 0);
          }
        }
        if (pale && nextInt(4) === 0) log(j1 + nextInt(2), sy + 2 + nextInt(Math.max(1, h - 4)), k1 + nextInt(2), B.CREAKING_HEART);
        const att = [[j1, l1, k1, true]];
        for (let l2 = -1; l2 <= 2; l2++) for (let i3 = -1; i3 <= 2; i3++) {
          if ((l2 < 0 || l2 > 1 || i3 < 0 || i3 > 1) && nextInt(3) <= 0) {
            const n = nextInt(3) + 2;
            for (let k2 = 0; k2 < n; k2++) put(x + l2, l1 - k2 - 1, z + i3, LG, 0);
            att.push([j1 + l2, l1, k1 + i3, false]);
          }
        }
        for (const [ax, ay, az, dbl] of att) {
          const sk = (adx, yy, adz, r, large) => {
            if (yy === -1 && !large) return adx === r && adz === r;
            if (yy === 1) return adx + adz > r * 2 - 2;
            return false;
          };
          if (dbl) {
            row(ax, ay, az, 2, -1, true, LV, sk);
            rowSigned(ax, ay, az, 3, 0, true, LV, (dx, yy, dz, r) => (dx === -r || dx >= r) && (dz === -r || dz >= r));
            row(ax, ay, az, 2, 1, true, LV, sk);
            if (nextInt(2)) row(ax, ay, az, 0, 2, true, LV, sk);
          } else {
            row(ax, ay, az, 2, -1, false, LV, sk);
            row(ax, ay, az, 1, 0, false, LV, sk);
          }
        }
        break;
      }
      case 'acacia': {
        // ForkingTrunkPlacer(5, 2, 2) + AcaciaFoliagePlacer(2, 0)
        const h = 5 + nextInt(3) + nextInt(3), by = sy + 1;
        soil(x, sy, z);
        const D = [[1, 0], [-1, 0], [0, 1], [0, -1]];
        const d1 = nextInt(4), i = h - nextInt(4) - 1;
        let j = 3 - nextInt(3), k = x, l = z, top = -1;
        for (let i1 = 0; i1 < h; i1++) {
          const yy = by + i1;
          if (i1 >= i && j > 0) { k += D[d1][0]; l += D[d1][1]; j--; }
          put(k, yy, l, B.ACACIA_LOG, 0); top = yy + 1;
        }
        const att = [[k, top, l, 1]];
        k = x; l = z;
        const d2 = nextInt(4);
        if (d2 !== d1) {
          const k2 = i - nextInt(2) - 1;
          let n = 1 + nextInt(3), t2 = -1;
          for (let i2 = k2; i2 < h && n > 0; n--, i2++) {
            if (i2 >= 1) { const yy = by + i2; k += D[d2][0]; l += D[d2][1]; put(k, yy, l, B.ACACIA_LOG, 0); t2 = yy + 1; }
          }
          if (t2 > 0) att.push([k, t2, l, 0]);
        }
        for (const [ax, ay, az, ro] of att) {
          const sk = (adx, yy, adz, r) => yy === 0 ? (adx > 1 || adz > 1) && adx !== 0 && adz !== 0 : adx === r && adz === r && r > 0;
          row(ax, ay, az, 2 + ro, -1, false, B.ACACIA_LEAVES, sk);
          row(ax, ay, az, 1, 0, false, B.ACACIA_LEAVES, sk);
          row(ax, ay, az, 2 + ro - 1, 0, false, B.ACACIA_LEAVES, sk);
        }
        break;
      }
      case 'cherry': {
        // CherryTrunkPlacer(7, 1, 0; branches 1-3, length 2..4, start -4..-3, end -1..0) +
        // CherryFoliagePlacer(4, 0, 5; holes 0.25 / 0.25, hanging leaves 1/6, extension 1/3)
        const h = 7 + nextInt(2), by = sy + 1;
        soil(x, sy, z);
        const i = Math.max(0, h - 1 + (-4 + nextInt(2)));
        let j = Math.max(0, h - 1 + (-4 + nextInt(1)));
        if (j >= i) j++;
        const cnt = 1 + nextInt(3), three = cnt === 3, two = cnt >= 2;
        const L = three ? h : two ? Math.max(i, j) + 1 : i + 1;
        for (let i1 = 0; i1 < L; i1++) put(x, by + i1, z, B.CHERRY_LOG, 0);
        const att = [];
        if (three) att.push([x, by + L, z]);
        const D = [[1, 0], [-1, 0], [0, 1], [0, -1]], dir = nextInt(4);
        const branch = (d, start, upwards) => {
          const ax = D[d][0] !== 0 ? 1 : 2;
          let mx = x, my = by + start, mz = z;
          const endY = by + h - 1 + (-1 + nextInt(2));
          const flag = upwards || endY < my;
          const len = 2 + nextInt(3) + (flag ? 1 : 0);
          const ex = x + D[d][0] * len, ez = z + D[d][1] * len;
          for (let s2 = 0; s2 < (flag ? 2 : 1); s2++) { mx += D[d][0]; mz += D[d][1]; put(mx, my, mz, B.CHERRY_LOG | (ax << 16), 0); }
          const up = endY > my ? 1 : -1;
          for (let guard = 0; guard < 32; guard++) {
            const dist = Math.abs(ex - mx) + Math.abs(endY - my) + Math.abs(ez - mz);
            if (dist === 0) { att.push([ex, endY + 1, ez]); return; }
            const vert = rnd() < Math.abs(endY - my) / dist;
            if (vert) my += up; else { mx += D[d][0]; mz += D[d][1]; }
            put(mx, my, mz, vert ? B.CHERRY_LOG : B.CHERRY_LOG | (ax << 16), 0);
          }
        };
        branch(dir, i, i < L - 1);
        if (two) branch(dir ^ 1, j, j < L - 1);
        const r = 3;
        const sk = (adx, yy, adz, rr) => {
          if (yy === -1 && (adx === rr || adz === rr) && rnd() < 0.25) return true;
          const c = adx === rr && adz === rr;
          if (rr > 2) return c || (adx + adz > rr * 2 - 2 && rnd() < 0.25);
          return c && rnd() < 0.25;
        };
        for (const [ax, ay, az] of att) {
          row(ax, ay, az, r - 2, 2, false, B.CHERRY_LEAVES, sk);
          row(ax, ay, az, r - 1, 1, false, B.CHERRY_LEAVES, sk);
          row(ax, ay, az, r, 0, false, B.CHERRY_LEAVES, sk);
          for (const [rr, yy] of [[r, -1], [r - 1, -2]]) {
            row(ax, ay, az, rr, yy, false, B.CHERRY_LEAVES, sk);
            // hanging leaves under the rim, one more below sometimes, within 6 of the attachment
            for (let dx = -rr; dx <= rr; dx++) for (let dz = -rr; dz <= rr; dz++) {
              if (Math.abs(dx) !== rr && Math.abs(dz) !== rr) continue;
              if (!TB.map.has(key(ax + dx, ay + yy, az + dz))) continue;
              if (Math.abs(dx) + 1 + Math.abs(dz) >= 7 || rnd() > 1 / 6) continue;
              put(ax + dx, ay + yy - 1, az + dz, B.CHERRY_LEAVES, 1);
              if (Math.abs(dx) + 2 + Math.abs(dz) < 7 && rnd() <= 1 / 3) put(ax + dx, ay + yy - 2, az + dz, B.CHERRY_LEAVES, 1);
            }
          }
        }
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
    }
    // AlterGroundDecorator(podzol) of the giant spruces: circles of podzol on the ground around the
    // trunk (radius 2 without corners) at its four corners and at five random spots of the ring 3 out
    if (podzol) {
      const circle = (cx, cz) => {
        for (let dx = -2; dx <= 2; dx++) for (let dz = -2; dz <= 2; dz++) if (Math.abs(dx) !== 2 || Math.abs(dz) !== 2) put(cx + dx, this.groundAt(cx + dx, cz + dz), cz + dz, B.PODZOL, 4);
      };
      circle(x - 1, z - 1); circle(x + 2, z - 1); circle(x - 1, z + 2); circle(x + 2, z + 2);
      for (let k = 0; k < 5; k++) { const j = nextInt(64), a = j % 8, b = (j / 8) | 0; if (a === 0 || a === 7 || b === 0 || b === 7) circle(x - 3 + a, z - 3 + b); }
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
    if (vineChance || propagules || mossOnRoots || trunkVines || paleMoss) {
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
      // TrunkVineDecorator: a vine on each free side of every log, two times in three
      if (trunkVines) for (let j = 0; j < n; j++) {
        if (TB.kind[j] !== 0) continue;
        const lx = TB.x[j], ly = TB.y[j], lz = TB.z[j];
        for (let s = 0; s < 4; s++) {
          if (hash3(this.seed + 140 + s, lx, ly, lz) < 1 / 3) continue;
          const vx = lx + hang[s][0], vz = lz + hang[s][1];
          if (!free(vx, ly, vz)) continue;
          deco.push(vx, ly, vz, B.VINE, hang[s][2], lx, ly, lz); taken.add(key(vx, ly, vz));
        }
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
        // pale moss: strands of 1-4 under a fifth of the crown's lowest leaves, the last one a tip
        if (paleMoss && hash3(this.seed + 141, lx, ly, lz) < 0.2 && free(lx, ly - 1, lz)) {
          let n = 1 + ((hash3(this.seed + 142, lx, ly, lz) * 4) | 0), yy = ly - 1;
          while (n > 1 && !free(lx, yy - n + 1, lz)) n--;
          for (let k = 0; k < n; k++, yy--) {
            if (!free(lx, yy, lz)) break;
            deco.push(lx, yy, lz, k === n - 1 ? B.PALE_HANGING_MOSS_TIP : B.PALE_HANGING_MOSS, 0, lx, yy + 1, lz); taken.add(key(lx, yy, lz));
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
  writeTreeBlock(xx, yy, zz, idm, kind) {
    const id = idm & 0xFFFF;
    if (kind === 0) this.setB(xx, yy, zz, id, idm >>> 16, 2);
    else if (kind === 1) this.setB(xx, yy, zz, id, 0, 0);
    else if (kind === 3) {
      const cur = this.getB(xx, yy, zz);
      if (cur >= 0 && !(FLAGS[cur] & BF_LEAVES) && !/_LOG$|^LOG$|ROOTS/.test(B_KEY[cur] || '')) this.setB(xx, yy, zz, id, 0, 2);
    } else if (kind === 4) {
      // ground cover (podzol): replaces grass and dirt only
      const cur = this.getB(xx, yy, zz);
      if (cur === B.GRASS || cur === B.DIRT || cur === B.COARSE_DIRT || cur === B.SNOWY_GRASS || cur === B.PODZOL) this.setB(xx, yy, zz, id, 0, 2);
    } else {
      const cur = this.getB(xx, yy, zz);
      if (cur >= 0 && cur !== B.GRASS && cur !== B.DIRT && cur !== B.PODZOL && cur !== B.COARSE_DIRT && cur !== B.MUD && cur !== B.SNOW) this.setB(xx, yy, zz, B.DIRT, 0, 2);
    }
  }
  flushTrees() {
    const W = this.treeWrites;
    for (const phase of [0, 1, 2, 3, 4]) for (let i = 0; i < W.length; i += 5) if (W[i + 4] === phase) this.writeTreeBlock(W[i], W[i + 1], W[i + 2], W[i + 3], phase);
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
        const moss = id === B.PALE_HANGING_MOSS || id === B.PALE_HANGING_MOSS_TIP;
        const ok = id === B.MOSS_CARPET ? /ROOTS/.test(B_KEY[sup] || '') : id === B.VINE && deco[i + 6] > deco[i + 1] ? sup === B.VINE :
          moss && sup === B.PALE_HANGING_MOSS ? true :
          (FLAGS[sup] & BF_LEAVES) !== 0 || (id === B.VINE && /_LOG$|^LOG$/.test(B_KEY[sup] || ''));
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
    // pale garden floor: drifts of pale moss (a block noise picks the drifts), carpets on most of it
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      if (this.BIO[z * 16 + x] !== BI.PALE_GARDEN) continue;
      const y = TOP[z * 16 + x], wx = x0 + x, wz = z0 + z, i = CI(x, y, z);
      if (y <= SEA || ids[i] !== B.GRASS) continue;
      const v = this.nMisc.n2(wx * 0.06 + 517.3, wz * 0.06 - 291.9) + (hash2(this.seed + 143, wx, wz) - 0.5) * 0.3;
      if (v < 0.05) continue;
      ids[i] = B.PALE_MOSS_BLOCK; meta[i] = 0;
      if (ids[i + 256] === 0 && v > 0.15 && hash2(this.seed + 144, wx, wz) < 0.55) ids[i + 256] = B.PALE_MOSS_CARPET;
    }
    // underwater decoration, and the original's bamboo and mushroom-field columns
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const y = TOP[z * 16 + x];
      if (y <= WORLD_MIN_Y + 1 || y >= WORLD_MAX_Y - 3) continue;
      const bio = this.BIO[z * 16 + x], ob = ORIG_BIOME[bio], wx = x0 + x, wz = z0 + z;
      const above = ids[CI(x, y + 1, z)];
      if (y < SEA) { if (above === B.WATER && !this.inVillage(wx, y + 1, wz)) this.waterDecor(x, z, y, ob); continue; }
      if (above !== 0 || (ob !== BI.BAMBOO_JUNGLE && ob !== BI.MUSHROOM_FIELDS)) continue;
      const plant = this.plantAt(bio, ob, x, z, wx, wz, y), ground = ids[CI(x, y, z)];
      if (!plant) continue;
      const mush = plant === B.BROWN_MUSHROOM || plant === B.RED_MUSHROOM;
      if (!(ground === B.GRASS || ground === B.PODZOL || (mush && ground === B.MYCELIUM) || (plant === B.BAMBOO_PLANT && ground === B.DIRT))) continue;
      const i1 = CI(x, y + 1, z);
      if (plant === B.BAMBOO_PLANT) {
        const n = 3 + ((hash2(this.seed + 36, wx * 11 - 4, wz * 11 + 6) * 4) | 0);
        for (let k = 0; k < n && y + 1 + k < WORLD_MAX_Y - 1 && ids[i1 + k * 256] === 0; k++) ids[i1 + k * 256] = plant;
      } else if (SHAPE[plant] !== SH.TALL) ids[i1] = plant;
    }
    // Minecraft's vegetation patches of the biomes, from this chunk and its neighbours (a patch
    // spreads up to 7 blocks); every choice comes from the origin chunk's seed, so parts agree
    for (let sz = this.cz - 1; sz <= this.cz + 1; sz++) for (let sx = this.cx - 1; sx <= this.cx + 1; sx++) {
      const present = new Set();
      for (let i = 0; i < 16; i++) present.add(this.colInfo(sx * 16 + (i & 3) * 4 + 2, sz * 16 + (i >> 2) * 4 + 2).biome);
      for (const bio of present) {
        const list = MC_VEG[bio];
        if (!list) continue;
        for (let f = 0; f < list.length; f++) {
          const [name, pl] = list[f], P = PATCHES[name];
          const rnd = mulberry(hash2i(this.seed + 4111 + bio * 131 + f * 7, sx, sz));
          let n = pl.n ?? 1;
          if (pl.nt) n = this.nMisc.n2(sx * 16 / 200, sz * 16 / 200) < -0.8 ? pl.nt[0] : pl.nt[1];
          if (pl.rn) n = Math.max(0, pl.rn[0] + ((rnd() * (pl.rn[1] - pl.rn[0] + 1)) | 0));
          for (let k = 0; k < n; k++) {
            const ox = sx * 16 + ((rnd() * 16) | 0), oz = sz * 16 + ((rnd() * 16) | 0);
            const pass = !pl.r || rnd() < 1 / pl.r, seed = (rnd() * 4294967296) >>> 0;
            if (!pass || this.colInfo(ox, oz).biome !== bio) continue;
            this.patch(P, ox, this.groundAt(ox, oz) + 1, oz, seed);
          }
        }
      }
    }
  }
  // one RandomPatchFeature: tries at random offsets; only cells of this chunk are written
  patch(P, ox, oy, oz, seed) {
    if (ox < this.x0 - P.xz - 1 || ox > this.x0 + 16 + P.xz || oz < this.z0 - P.xz - 1 || oz > this.z0 + 16 + P.xz) return;
    const rnd = mulberry(seed), ri = (n) => (rnd() * (n + 1)) | 0;
    // forest flowers: one of lilac, rose bush, peony, lily of the valley for the whole patch
    const pickF = P.kind === 'forest_flowers' ? [B.LILAC, B.ROSE_BUSH, B.PEONY, B.LILY_OF_THE_VALLEY][(rnd() * 4) | 0] : 0;
    for (let t = 0; t < P.tries; t++) {
      const x = ox + ri(P.xz) - ri(P.xz), y = oy + ri(P.y) - ri(P.y), z = oz + ri(P.xz) - ri(P.xz);
      const r1 = rnd(), r2 = rnd();
      const lx = x - this.x0, lz = z - this.z0;
      if (lx < 0 || lx > 15 || lz < 0 || lz > 15 || y <= WORLD_MIN_Y + 1 || y >= WORLD_MAX_Y - 3) continue;
      const i = CI(lx, y, lz), ids = this.ids;
      if (ids[i] !== 0) continue;
      const below = ids[i - 256];
      const id = this.patchPlant(P.kind, x, z, r1, r2, pickF);
      if (!id || !this.plantStays(id, below, lx, y, lz)) continue;
      if (SHAPE[id] === SH.TALL) {
        if (ids[i + 256] !== 0) continue;
        ids[i] = id; ids[i + 256] = id; this.meta[i + 256] = 1;
      } else if (id === B.SUGAR_CANE) {
        const h = 2 + ((rnd() * (((rnd() * 3) | 0) + 1)) | 0);
        for (let k = 0; k < h && ids[i + k * 256] === 0; k++) ids[i + k * 256] = id;
      } else {
        ids[i] = id;
        if (id === B.SWEET_BERRY_BUSH) this.meta[i] = 3;
      }
    }
  }
  patchPlant(kind, x, z, r1, r2, pickF) {
    switch (kind) {
      case 'grass': return B.TALL_GRASS;
      case 'grass_jungle': return r1 < 0.75 ? B.TALL_GRASS : B.FERN;
      case 'grass_taiga': return r1 < 0.2 ? B.TALL_GRASS : B.FERN;
      case 'tall_grass': return B.TALL_GRASS_PLANT;
      case 'large_fern': return B.LARGE_FERN;
      case 'dead_bush': return B.DEAD_BUSH;
      case 'sugar_cane': return B.SUGAR_CANE;
      case 'pumpkin': return B.PUMPKIN;
      case 'melon': return B.MELON;
      case 'berry': return B.SWEET_BERRY_BUSH;
      case 'sunflower': return B.SUNFLOWER;
      case 'brown_mushroom': return B.BROWN_MUSHROOM;
      case 'red_mushroom': return B.RED_MUSHROOM;
      case 'blue_orchid': return B.BLUE_ORCHID;
      case 'eyeblossom': return r1 < 0.5 ? B.CLOSED_EYEBLOSSOM : B.OPEN_EYEBLOSSOM;
      case 'forest_flowers': return pickF;
      case 'flower_default': return r1 < 2 / 3 ? B.POPPY : B.DANDELION;
      case 'flower_plains': {
        // NoiseThresholdProvider(scale 0.005, threshold -0.8, high chance 1/3): tulips in the low
        // noise areas, else poppy / bluet / daisy / cornflower one time in three, else dandelion
        if (this.nMisc.n2(x * 0.005 + 71.3, z * 0.005 - 19.7) < -0.8) return [B.ORANGE_TULIP, B.RED_TULIP, B.PINK_TULIP, B.WHITE_TULIP][(r2 * 4) | 0];
        return r1 < 1 / 3 ? [B.POPPY, B.AZURE_BLUET, B.OXEYE_DAISY, B.CORNFLOWER][(r2 * 4) | 0] : B.DANDELION;
      }
      case 'flower_forest': {
        // NoiseProvider(scale 0.020833334): the kind of flower changes smoothly across the forest
        const L = [B.DANDELION, B.POPPY, B.ALLIUM, B.AZURE_BLUET, B.RED_TULIP, B.ORANGE_TULIP, B.WHITE_TULIP, B.PINK_TULIP, B.OXEYE_DAISY, B.CORNFLOWER, B.LILY_OF_THE_VALLEY];
        const v = (this.nMisc.n2(x * 0.020833334 - 313.1, z * 0.020833334 + 77.7) + 1) / 2;
        return L[Math.max(0, Math.min(L.length - 1, (v * L.length) | 0))];
      }
      case 'flower_meadow': {
        const L = [B.TALL_GRASS_PLANT, B.ALLIUM, B.POPPY, B.AZURE_BLUET, B.DANDELION, B.CORNFLOWER, B.OXEYE_DAISY, B.TALL_GRASS];
        const v = (this.nMisc.n2(x * 0.020833334 + 911.1, z * 0.020833334 - 55.5) + 1) / 2;
        // DualNoiseProvider in Minecraft: a few kinds at a time, changing across the meadow
        return L[Math.max(0, Math.min(L.length - 1, ((v + (r1 - 0.5) * 0.25) * L.length) | 0))];
      }
    }
    return 0;
  }
  // canSurvive of the plants on the block below (Minecraft's BushBlock and friends)
  plantStays(id, below, lx, y, lz) {
    const dirt = below === B.GRASS || below === B.DIRT || below === B.PODZOL || below === B.COARSE_DIRT || below === B.ROOTED_DIRT ||
      below === B.MOSS_BLOCK || below === B.PALE_MOSS_BLOCK || below === B.MUD || below === B.SNOWY_GRASS || below === B.MYCELIUM;
    if (id === B.DEAD_BUSH) return dirt || below === B.SAND || below === B.RED_SAND || below === B.TERRACOTTA || /TERRACOTTA$/.test(B_KEY[below] || '');
    if (id === B.PUMPKIN || id === B.MELON) return below === B.GRASS;
    if (id === B.BROWN_MUSHROOM || id === B.RED_MUSHROOM) {
      if (below === B.MYCELIUM || below === B.PODZOL) return true;
      if (!OPAQUE[below]) return false;
      // light 12 or less: only under cover (leaves, logs or terrain above)
      for (let yy = y + 1; yy < Math.min(WORLD_MAX_Y - 1, y + 40); yy++) if (this.ids[CI(lx, yy, lz)] !== 0) return true;
      return false;
    }
    if (id === B.SUGAR_CANE) {
      if (!(dirt || below === B.SAND || below === B.RED_SAND)) return false;
      for (let f = 0; f < 4; f++) {
        const nx = lx + FACE4[f * 2], nz = lz + FACE4[f * 2 + 1];
        if (nx >= 0 && nx < 16 && nz >= 0 && nz < 16) { const n = this.ids[CI(nx, y - 1, nz)]; if (n === B.WATER || n === B.ICE) return true; continue; }
        if (y - 1 <= SEA && this.groundAt(this.x0 + nx, this.z0 + nz) < y - 1) return true;
      }
      return false;
    }
    return dirt && below !== B.MYCELIUM;
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
  // bamboo of the bamboo jungle and mushrooms of the mushroom fields, per column
  plantAt(bio, ob, lx, lz, x, z, y) {
    const r = hash2(this.seed + 31, x * 7 + 3, z * 7 - 5), b2 = hash2(this.seed + 32, x * 11 - 7, z * 11 + 13);
    if (ob === BI.BAMBOO_JUNGLE) return r < 0.32 ? B.BAMBOO_PLANT : 0;
    if (ob === BI.MUSHROOM_FIELDS) return r < 0.22 ? (b2 < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM) : 0;
    return 0;
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
    for (let y = fy + 1; y <= top; y++) { const i = CI(lx, y, lz); if (this.ids[i] !== B.WATER) break; this.ids[i] = B.KELP; }
    return true;
  }

  // Cave biome of a 4x4x4 cell (0 none): own rules on the column climate and the depth below the
  // surface. Lush caves under humid land, dripstone caves under far inland, the deep dark low down
  // under mountains (low erosion); a 3D region noise breaks each into separate cave systems.
  caveBiomes() {
    const x0 = this.x0, z0 = this.z0, TOP = this.TOP, G = this.cbio || (this.cbio = new Uint8Array(16 * (WORLD_H >> 2)));
    G.fill(0);
    const o = this._cbo || (this._cbo = {});
    for (let qz = 0; qz < 4; qz++) for (let qx = 0; qx < 4; qx++) {
      const wx = x0 + qx * 4 + 2, wz = z0 + qz * 4 + 2;
      this.climate(wx, wz, o);
      const top = TOP[(qz * 4 + 2) * 16 + qx * 4 + 2];
      for (let qy = 1; qy < (WORLD_H >> 2) - 1; qy++) {
        const y = WORLD_MIN_Y + qy * 4 + 2, depth = top - y;
        if (depth < 14) continue;
        const reg = this.nCave.n3(wx / 80, y / 44, wz / 80), reg2 = this.nCave.n3(wx / 80 + 300, y / 44, wz / 80 - 200);
        let b = 0;
        if (y < -18 && o.e < -0.2) { if (this.nCave.n3(wx / 96 + 700, y / 52, wz / 96 + 90) > -0.15) b = BI.DEEP_DARK; }
        else if (o.hu > 0.3 && reg > -0.05) b = BI.LUSH_CAVES;
        else if (o.c > 0.45 && reg2 > -0.05) b = BI.DRIPSTONE_CAVES;
        else if (reg > 0.45) b = BI.LUSH_CAVES;              // small pockets anywhere
        else if (reg2 > 0.48) b = BI.DRIPSTONE_CAVES;
        G[(qy * 4 + qz) * 4 + qx] = b;
      }
    }
    return G;
  }
  caveDecor() {
    const ids = this.ids, meta = this.meta, x0 = this.x0, z0 = this.z0, TOP = this.TOP, seed = this.seed;
    const G = this.caveBiomes();
    const STONY = (b) => b === B.STONE || b === B.DEEPSLATE || b === B.TUFF || b === B.GRANITE || b === B.DIORITE || b === B.ANDESITE;
    // a pointed dripstone column of n from cell i stepping by d (-256 down from a ceiling, +256 up
    // from a floor): thick at the root, a tip at the free end
    const spike = (i, d, n) => {
      for (let k = 0; k < n; k++) if (ids[i + k * d] !== 0) { n = k; break; }
      for (let k = 0; k < n; k++) {
        const j = i + k * d, left = n - 1 - k;
        ids[j] = B.POINTED_DRIPSTONE;
        meta[j] = (left === 0 ? 1 : left === 1 ? 2 : k === 0 && n >= 4 ? 4 : 3) | (d > 0 ? 8 : 0);
      }
    };
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const wx = x0 + x, wz = z0 + z, top = TOP[z * 16 + x];
      for (let y = top - 6; y > WORLD_MIN_Y + 6; y--) {
        const i = CI(x, y, z);
        if (ids[i] !== 0) continue;
        const below = ids[i - 256], above = ids[i + 256];
        const floor = STONY(below) || below === B.GRAVEL, ceil = STONY(above);
        const cb = G[(((y - WORLD_MIN_Y) >> 2) * 4 + (z >> 2)) * 4 + (x >> 2)];
        const r = hash3(seed + 51, wx, y, wz);
        if (!floor && !ceil) {
          let bits = 0;
          if (x < 15 && OPAQUE[ids[i + 1]]) bits |= 8; if (x > 0 && OPAQUE[ids[i - 1]]) bits |= 2;
          if (z < 15 && OPAQUE[ids[i + 16]]) bits |= 1; if (z > 0 && OPAQUE[ids[i - 16]]) bits |= 4;
          if (!bits) continue;
          if (cb === BI.DEEP_DARK) { if (r < 0.12) { ids[i] = B.SCULK_VEIN; meta[i] = bits; } }
          else if (cb === BI.LUSH_CAVES) { if (r < 0.05) { ids[i] = B.GLOW_LICHEN; meta[i] = bits; } }
          else if (y < 40 && r < 0.012) { ids[i] = B.GLOW_LICHEN; meta[i] = bits; }
          continue;
        }
        if (cb === BI.LUSH_CAVES) {
          if (floor) {
            ids[i - 256] = r < 0.04 ? B.CLAY : r < 0.07 ? B.ROOTED_DIRT : B.MOSS_BLOCK;
            const r2 = hash3(seed + 56, wx, y, wz);
            if (r2 < 0.04) ids[i] = r2 < 0.015 ? B.FLOWERING_AZALEA : B.AZALEA;
            else if (r2 < 0.07 && ids[i + 256] === 0) {
              // big dripleaf: a stem of 0-2 and the leaf on top, facing a random side
              const n = (hash3(seed + 57, wx, y, wz) * 3) | 0, f = (hash3(seed + 58, wx, y, wz) * 4) | 0;
              let k = 0;
              for (; k < n && ids[i + (k + 1) * 256] === 0; k++) { ids[i + k * 256] = B.BIG_DRIPLEAF_STEM; meta[i + k * 256] = f; }
              ids[i + k * 256] = B.BIG_DRIPLEAF; meta[i + k * 256] = f;
            } else if (r2 < 0.1 && ids[i + 256] === 0) {
              const f = (hash3(seed + 58, wx, y, wz) * 4) | 0;
              ids[i] = B.SMALL_DRIPLEAF; meta[i] = f; ids[i + 256] = B.SMALL_DRIPLEAF; meta[i + 256] = f | 4;
            } else if (r2 < 0.32) ids[i] = B.MOSS_CARPET;
            else if (r2 < 0.5) ids[i] = r2 < 0.42 ? B.TALL_GRASS : B.FERN;
          } else {
            ids[i + 256] = B.MOSS_BLOCK;
            const r2 = hash3(seed + 59, wx, y, wz);
            if (r2 < 0.006) ids[i] = B.SPORE_BLOSSOM;
            else if (r2 < 0.05) ids[i] = B.HANGING_ROOTS;
            else if (r2 < 0.28) {
              const len = 1 + ((hash3(seed + 52, wx, y, wz) * 7) | 0);
              for (let k = 0; k < len; k++) {
                const j = i - k * 256;
                if (ids[j] !== 0) break;
                ids[j] = B.CAVE_VINES; meta[j] = hash3(seed + 53, wx, y - k, wz) < 0.18 ? 1 : 0;
              }
            }
          }
        } else if (cb === BI.DRIPSTONE_CAVES) {
          if (floor) {
            if (r < 0.55) ids[i - 256] = B.DRIPSTONE;
            if (r < 0.06) spike(i, 256, 1 + ((hash3(seed + 54, wx, y, wz) * 5) | 0));
          } else {
            if (r < 0.55) ids[i + 256] = B.DRIPSTONE;
            if (r < 0.09) spike(i, -256, 1 + ((hash3(seed + 55, wx, y, wz) * 6) | 0));
          }
        } else if (cb === BI.DEEP_DARK) {
          // sculk spreads in patches (a block noise), with sensors, shriekers and catalysts on it
          const v = this.vn.f2(wx * 0.11 + y * 0.37, wz * 0.11 - y * 0.29);
          if (floor) {
            if (v > 0.3) {
              ids[i - 256] = B.SCULK;
              const r2 = hash3(seed + 60, wx, y, wz);
              if (r2 < 0.02) ids[i] = B.SCULK_SENSOR;
              else if (r2 < 0.026) ids[i] = B.SCULK_SHRIEKER;
              else if (r2 < 0.03) ids[i - 256] = B.SCULK_CATALYST;
            } else if (v > 0.2) { ids[i] = B.SCULK_VEIN; meta[i] = 16; }
          } else if (v > 0.35) ids[i + 256] = B.SCULK;
          else if (v > 0.25) { ids[i] = B.SCULK_VEIN; meta[i] = 32; }
        }
      }
    }
  }
}
