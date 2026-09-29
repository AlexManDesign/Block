'use strict';
// World generator (worker side). Minecraft 1.18-style terrain: continentalness / erosion /
// weirdness drive a height field, a 3D density adds overhangs, then surface rules per biome,
// noise caves, ores, trees and vegetation. Everything is deterministic per seed and chunk.

const BIOME_LIST = [
  ['OCEAN', 'Ocean', 'Океан'], ['DEEP_OCEAN', 'Deep Ocean', 'Глубокий океан'],
  ['FROZEN_OCEAN', 'Frozen Ocean', 'Замёрзший океан'], ['COLD_OCEAN', 'Cold Ocean', 'Холодный океан'],
  ['LUKEWARM_OCEAN', 'Lukewarm Ocean', 'Тёплый океан'], ['WARM_OCEAN', 'Warm Ocean', 'Тропический океан'],
  ['RIVER', 'River', 'Река'], ['FROZEN_RIVER', 'Frozen River', 'Замёрзшая река'],
  ['BEACH', 'Beach', 'Пляж'], ['SNOWY_BEACH', 'Snowy Beach', 'Заснеженный пляж'],
  ['STONY_SHORE', 'Stony Shore', 'Каменистый берег'], ['PLAINS', 'Plains', 'Равнина'],
  ['SUNFLOWER_PLAINS', 'Sunflower Plains', 'Подсолнечная равнина'], ['SNOWY_PLAINS', 'Snowy Plains', 'Заснеженная равнина'],
  ['ICE_SPIKES', 'Ice Spikes', 'Ледяные пики'], ['DESERT', 'Desert', 'Пустыня'], ['SAVANNA', 'Savanna', 'Саванна'],
  ['FOREST', 'Forest', 'Лес'], ['FLOWER_FOREST', 'Flower Forest', 'Цветочный лес'],
  ['BIRCH_FOREST', 'Birch Forest', 'Березняк'], ['DARK_FOREST', 'Dark Forest', 'Тёмный лес'],
  ['TAIGA', 'Taiga', 'Тайга'], ['OLD_GROWTH_TAIGA', 'Old Growth Taiga', 'Старовозрастная тайга'],
  ['SNOWY_TAIGA', 'Snowy Taiga', 'Заснеженная тайга'], ['JUNGLE', 'Jungle', 'Джунгли'],
  ['SPARSE_JUNGLE', 'Sparse Jungle', 'Редкие джунгли'], ['BAMBOO_JUNGLE', 'Bamboo Jungle', 'Бамбуковые джунгли'],
  ['SWAMP', 'Swamp', 'Болото'], ['MANGROVE_SWAMP', 'Mangrove Swamp', 'Мангровое болото'],
  ['BADLANDS', 'Badlands', 'Пустоши'], ['WOODED_BADLANDS', 'Wooded Badlands', 'Лесистые пустоши'],
  ['MEADOW', 'Meadow', 'Луг'], ['CHERRY_GROVE', 'Cherry Grove', 'Вишнёвая роща'], ['GROVE', 'Grove', 'Роща'],
  ['SNOWY_SLOPES', 'Snowy Slopes', 'Заснеженные склоны'], ['JAGGED_PEAKS', 'Jagged Peaks', 'Зубчатые пики'],
  ['FROZEN_PEAKS', 'Frozen Peaks', 'Заледеневшие пики'], ['STONY_PEAKS', 'Stony Peaks', 'Скалистые пики'],
  ['WINDSWEPT_HILLS', 'Windswept Hills', 'Холмы'], ['MUSHROOM_FIELDS', 'Mushroom Fields', 'Грибные поля'],
];
const BI = {};
BIOME_LIST.forEach((b, i) => { BI[b[0]] = i; });

// per-biome properties: surface top/filler, trees (jittered grid cell + weighted types)
const BPROP = [];
(function () {
  const G = B.GRASS, D = B.DIRT, S = B.SAND;
  const def = (k, o) => { BPROP[BI[k]] = Object.assign({ top: G, fill: D, sub: 0, floor: D, snow: 0, cell: 0, trees: null, cold: 0 }, o); };
  def('OCEAN', { top: S, fill: S, floor: -1 }); def('DEEP_OCEAN', { top: B.GRAVEL, fill: B.GRAVEL, floor: B.GRAVEL });
  def('FROZEN_OCEAN', { top: B.GRAVEL, fill: B.GRAVEL, floor: B.GRAVEL, cold: 2 });
  def('COLD_OCEAN', { top: B.GRAVEL, fill: B.GRAVEL, floor: B.GRAVEL, cold: 1 });
  def('LUKEWARM_OCEAN', { top: S, fill: S, floor: S }); def('WARM_OCEAN', { top: S, fill: S, floor: S });
  def('RIVER', { top: S, fill: S, floor: -2 }); def('FROZEN_RIVER', { top: S, fill: S, floor: -2, cold: 2 });
  def('BEACH', { top: S, fill: S, sub: B.SANDSTONE, floor: S }); def('SNOWY_BEACH', { top: S, fill: S, sub: B.SANDSTONE, floor: S, snow: 1, cold: 2 });
  def('STONY_SHORE', { top: B.STONE, fill: B.STONE, floor: B.GRAVEL });
  def('PLAINS', { cell: 18, trees: [[0.3, 'oak'], [0.42, 'fancy_oak']] });
  def('SUNFLOWER_PLAINS', { cell: 20, trees: [[0.3, 'oak']] });
  def('SNOWY_PLAINS', { snow: 1, cold: 2, cell: 22, trees: [[0.35, 'spruce']] });
  def('ICE_SPIKES', { top: B.SNOW, fill: B.SNOW, snow: 1, cold: 2 });
  def('DESERT', { top: S, fill: S, sub: B.SANDSTONE, floor: S });
  def('SAVANNA', { cell: 13, trees: [[0.55, 'acacia'], [0.62, 'oak']] });
  def('FOREST', { cell: 6, trees: [[0.5, 'oak'], [0.75, 'birch'], [0.87, 'fancy_oak']] });
  def('FLOWER_FOREST', { cell: 9, trees: [[0.45, 'oak'], [0.7, 'birch'], [0.8, 'fancy_oak']] });
  def('BIRCH_FOREST', { cell: 6, trees: [[0.78, 'birch'], [0.9, 'oak']] });
  def('DARK_FOREST', { cell: 6, trees: [[0.62, 'dark_oak'], [0.82, 'oak'], [0.9, 'birch'], [0.93, 'huge_mushroom']] });
  def('TAIGA', { cell: 6, trees: [[0.85, 'spruce']], top: G });
  def('OLD_GROWTH_TAIGA', { cell: 6, top: B.PODZOL, trees: [[0.5, 'mega_spruce'], [0.85, 'spruce']] });
  def('SNOWY_TAIGA', { cell: 8, snow: 1, cold: 2, trees: [[0.7, 'spruce']] });
  def('JUNGLE', { cell: 5, trees: [[0.25, 'mega_jungle'], [0.72, 'jungle'], [0.9, 'jungle_bush']] });
  def('SPARSE_JUNGLE', { cell: 11, trees: [[0.5, 'jungle'], [0.65, 'oak'], [0.8, 'jungle_bush']] });
  def('BAMBOO_JUNGLE', { cell: 7, top: B.PODZOL, trees: [[0.4, 'jungle'], [0.55, 'jungle_bush'], [0.65, 'mega_jungle']] });
  def('SWAMP', { cell: 9, floor: -3, trees: [[0.55, 'swamp_oak']] });
  def('MANGROVE_SWAMP', { top: B.MUD, fill: B.MUD, floor: B.MUD, cell: 7, trees: [[0.45, 'swamp_oak'], [0.65, 'dark_oak']] });
  def('BADLANDS', { top: B.RED_SAND, fill: B.TERRACOTTA, floor: B.RED_SAND, badlands: 1 });
  def('WOODED_BADLANDS', { top: B.RED_SAND, fill: B.TERRACOTTA, floor: B.RED_SAND, badlands: 1, cell: 10, trees: [[0.35, 'oak']] });
  def('MEADOW', { cell: 26, trees: [[0.25, 'oak'], [0.35, 'birch']] });
  def('CHERRY_GROVE', { cell: 9, trees: [[0.45, 'cherry']] });
  def('GROVE', { top: B.SNOW, fill: B.DIRT, snow: 1, cold: 2, cell: 7, trees: [[0.75, 'spruce']] });
  def('SNOWY_SLOPES', { top: B.SNOW, fill: B.SNOW, snow: 1, cold: 2, mountain: 1 });
  def('JAGGED_PEAKS', { top: B.SNOW, fill: B.SNOW, snow: 1, cold: 2, mountain: 1 });
  def('FROZEN_PEAKS', { top: B.SNOW, fill: B.PACKED_ICE, snow: 1, cold: 2, mountain: 1 });
  def('STONY_PEAKS', { top: B.STONE, fill: B.STONE, mountain: 1 });
  def('WINDSWEPT_HILLS', { cell: 15, trees: [[0.25, 'oak'], [0.45, 'spruce']], mountain: 1 });
  def('MUSHROOM_FIELDS', { top: B.MYCELIUM, fill: D, cell: 11, trees: [[0.3, 'huge_mushroom']] });
})();

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
const SPL_BASE = [-1, 24, -0.62, 30, -0.45, 38, -0.3, 47, -0.2, 55, -0.155, 61, -0.115, 64, -0.05, 66, 0.1, 69, 0.3, 74, 0.55, 82, 1, 96];
const SPL_ERO = [-1, 1, -0.6, 0.82, -0.38, 0.58, -0.18, 0.34, 0.08, 0.16, 0.35, 0.07, 1, 0.02];

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
    this.colCache = new Map();
  }

  // ------------------------------------------------------------ climate / height
  climate(x, z, o) {
    const wx = x + this.nWarp.n2(x / 180, z / 180) * 22, wz = z + this.nWarp.n2(x / 180 + 91, z / 180 - 37) * 22;
    let c = this.nCont.f2(wx / 2300, wz / 2300, 6) * 1.9 + 0.1;
    let e = this.nEro.f2(wx / 900, wz / 900, 4) * 1.65;
    let w = this.nWeird.f2(wx / 520, wz / 520, 3) * 1.6;
    c = c < -1 ? -1 : c > 1 ? 1 : c; e = e < -1 ? -1 : e > 1 ? 1 : e; w = w < -1 ? -1 : w > 1 ? 1 : w;
    const pv = 1 - Math.abs(3 * Math.abs(w) - 2);
    const base = spline(SPL_BASE, c);
    const inland = sstep(-0.13, 0.28, c);
    const ef = spline(SPL_ERO, e);
    const pk = smooth01((pv + 1) * 0.5);
    const mount = inland * ef * (0.18 + 0.82 * Math.pow(pk, 1.6)) * 178;
    const hillAmp = 2 + 9 * sstep(-0.16, 0.05, c) * (1 - ef * 0.4);
    let h = base + mount + this.nHill.f2(x / 95, z / 95, 3) * hillAmp;
    // rivers
    const rw = Math.abs(this.nRiver.f2(wx / 520, wz / 520, 3));
    let river = 0;
    if (c > -0.2) {
      const land = sstep(-0.2, -0.1, c);
      river = (1 - sstep(0.012, 0.05, rw)) * land;
      const bank = (1 - sstep(0.012, 0.13, rw)) * land;
      if (h > SEA + 1) {
        const low = Math.min(h, SEA + 3 + (h - SEA) * 0.25);
        h += (low - h) * bank * 0.85;
      }
      if (river > 0 && h > SEA - 5) h += (SEA - 5 - h) * river;
    }
    // mushroom islands in deep ocean
    let mush = 0;
    if (c < -0.55) {
      const mn = this.nMush.n2(x / 700, z / 700);
      if (mn > 0.55) { mush = sstep(0.55, 0.75, mn) * sstep(-0.55, -0.7, c); const ih = SEA + 2 + mush * 7 + this.nHill.n2(x / 40, z / 40) * 3; if (ih > h) h += (ih - h) * mush; }
    }
    const t = this.nTemp.f2(x / 2600, z / 2600, 3) * 1.8;
    const hu = this.nHum.f2(x / 2100 + 50, z / 2100 - 70, 3) * 1.8;
    o.h = h; o.c = c; o.e = e; o.w = w; o.pv = pv; o.t = t; o.hu = hu; o.river = river; o.mush = mush;
    o.m = Math.min(1, mount / 70);
    o.ti = t < -0.45 ? 0 : t < -0.15 ? 1 : t < 0.2 ? 2 : t < 0.55 ? 3 : 4;
    o.hi = hu < -0.35 ? 0 : hu < -0.1 ? 1 : hu < 0.1 ? 2 : hu < 0.32 ? 3 : 4;
    o.mount = mount;
    return o;
  }

  biomeOf(o) {
    const h = o.h, ti = o.ti, hi = o.hi, w = o.w;
    if (o.mush > 0.3 && h > SEA) return BI.MUSHROOM_FIELDS;
    if (o.river > 0.45 && h < SEA + 1.5 && o.c > -0.2) return ti === 0 ? BI.FROZEN_RIVER : BI.RIVER;
    if (h < SEA - 0.5) {
      if (ti === 0) return BI.FROZEN_OCEAN;
      if (ti === 1) return BI.COLD_OCEAN;
      if (ti === 3) return BI.LUKEWARM_OCEAN;
      if (ti === 4) return BI.WARM_OCEAN;
      return o.c < -0.45 ? BI.DEEP_OCEAN : BI.OCEAN;
    }
    if (h < SEA + 2.5 && o.c < -0.08) {
      if (ti === 0) return BI.SNOWY_BEACH;
      if (o.e < -0.35) return BI.STONY_SHORE;
      if (ti === 4) return BI.DESERT;
      return BI.BEACH;
    }
    if (o.e > 0.35 && h < SEA + 5 && hi >= 3 && ti >= 1 && ti <= 4) return ti >= 3 ? BI.MANGROVE_SWAMP : BI.SWAMP;
    if (h > 175 || (o.mount > 70 && o.pv > 0.5)) {
      if (ti <= 1) return w < 0 ? BI.JAGGED_PEAKS : BI.FROZEN_PEAKS;
      if (ti === 2) return h > 190 ? (w < 0 ? BI.JAGGED_PEAKS : BI.FROZEN_PEAKS) : BI.STONY_PEAKS;
      if (ti === 3) return BI.STONY_PEAKS;
      return BI.BADLANDS;
    }
    if (h > 118 || o.mount > 38) {
      if (ti <= 1) return hi >= 2 ? BI.GROVE : BI.SNOWY_SLOPES;
      if (ti === 2) return w > 0.1 && hi >= 1 ? BI.CHERRY_GROVE : BI.MEADOW;
      if (ti === 3) return hi >= 3 ? BI.MEADOW : BI.SAVANNA;
      return hi >= 2 ? BI.WOODED_BADLANDS : BI.BADLANDS;
    }
    if (o.mount > 18 && o.pv > 0.2 && h > 88) return ti === 4 ? BI.BADLANDS : BI.WINDSWEPT_HILLS;
    switch (ti) {
      case 0: return hi === 0 && w > 0.3 ? BI.ICE_SPIKES : hi >= 3 ? BI.SNOWY_TAIGA : BI.SNOWY_PLAINS;
      case 1: return hi <= 1 ? BI.PLAINS : hi === 2 ? BI.FOREST : hi === 3 ? BI.TAIGA : BI.OLD_GROWTH_TAIGA;
      case 2: return hi === 0 ? (w > 0.25 ? BI.FLOWER_FOREST : w < -0.4 ? BI.SUNFLOWER_PLAINS : BI.PLAINS) :
        hi === 1 ? BI.PLAINS : hi === 2 ? BI.FOREST : hi === 3 ? BI.BIRCH_FOREST : BI.DARK_FOREST;
      case 3: return hi <= 1 ? BI.SAVANNA : hi === 2 ? BI.FOREST : hi === 3 ? BI.SPARSE_JUNGLE : (w > 0.3 ? BI.BAMBOO_JUNGLE : BI.JUNGLE);
      default: return hi >= 3 && o.c > 0.3 ? (hi === 3 ? BI.WOODED_BADLANDS : BI.BADLANDS) : BI.DESERT;
    }
  }

  // cached climate for grid corners & tree candidates (keyed by x,z)
  colInfo(x, z) {
    const k = x * 65536 + z;
    let v = this.colCache.get(k);
    if (v) return v;
    const o = this.climate(x, z, {});
    v = { h: o.h, m: o.m, biome: this.biomeOf(o), sq: 0, amp: 0 };
    const near = Math.abs(o.h - SEA);
    v.sq = 9 + 16 * o.m;
    v.amp = (0.14 + 1.05 * o.m) * (near < 9 ? 0.35 + 0.65 * near / 9 : 1);
    if (this.colCache.size > 60000) this.colCache.clear();
    this.colCache.set(k, v);
    return v;
  }

  density(x, y, z, ci) {
    const t = (ci.h - y) / ci.sq;
    if (t > ci.amp || t < -ci.amp) return t;
    return t + ci.amp * this.nDen.f3(x / 62, y / 46, z / 62, 3);
  }

  // surface height (top solid) computed purely from density, identical to what chunk fill produces
  surfaceAt(x, z) {
    const gx = Math.floor(x / 4) * 4, gz = Math.floor(z / 4) * 4;
    const fx = (x - gx) / 4, fz = (z - gz) / 4;
    const c00 = this.colInfo(gx, gz), c10 = this.colInfo(gx + 4, gz), c01 = this.colInfo(gx, gz + 4), c11 = this.colInfo(gx + 4, gz + 4);
    const top = Math.max(c00.h + c00.amp * c00.sq, c10.h + c10.amp * c10.sq, c01.h + c01.amp * c01.sq, c11.h + c11.amp * c11.sq) + 2;
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
      this.climate(x0 + dx, z0 + dz, o);
      H[(dz + 1) * 18 + dx + 1] = o.h;
      if (dx >= 0 && dx < 16 && dz >= 0 && dz < 16) BIO[dz * 16 + dx] = this.biomeOf(o);
    }
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
      const t = ci.h + ci.amp * ci.sq + 2;
      if (t > hmax) hmax = t;
    }
    const rows = Math.min(48, Math.floor((hmax - WORLD_MIN_Y) / 8) + 1);
    const D = new Float32Array(25 * 49);
    for (let k = 0; k < 5; k++) for (let i = 0; i < 5; i++) {
      const ci = cinfo[k * 5 + i], gx = x0 + i * 4, gz = z0 + k * 4;
      for (let j = 0; j <= 48; j++) {
        const y = WORLD_MIN_Y + j * 8;
        D[(k * 5 + i) * 49 + j] = j > rows ? -10 : this.density(gx, y, gz, ci);
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
      const b = this.BIO[z * 16 + x], P = BPROP[b];
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
        if (b === BI.OLD_GROWTH_TAIGA && sn < -0.3) top = B.COARSE_DIRT || B.GRASS;
        if (y >= SEA - 1 && y <= SEA + 1 && (top === B.GRASS) && sn > 0.6 && b !== BI.SWAMP) top = B.SAND;
      }
      // walk down replacing
      let depth = 0;
      for (let yy = y; yy > WORLD_MIN_Y && depth < fillDepth + 4; yy--) {
        const i = CI(x, yy, z);
        if (!this.isStoneLike(ids[i])) break;
        if (P.badlands && !underwater) {
          if (depth === 0) ids[i] = (b === BI.WOODED_BADLANDS && yy > 95) ? (sn > 0 ? B.GRASS : B.COARSE_DIRT || B.DIRT) : B.RED_SAND;
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
            if (b !== BI.FROZEN_OCEAN || hash2(seed + 5, wx, wz) < 0.9) ids[i] = B.ICE;
            break;
          }
        }
      }
      if (b === BI.FROZEN_OCEAN && underwater && this.nMisc.n2(wx / 30, wz / 30) > 0.55) {
        // icebergs
        const hgt = Math.floor((this.nMisc.n2(wx / 30, wz / 30) - 0.55) * 30);
        for (let k = 0; k <= hgt; k++) { const i = CI(x, SEA + k, z); if (ids[i] === 0 || ids[i] === B.ICE) ids[i] = k > hgt - 2 ? B.SNOW : B.PACKED_ICE; }
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
    const b = this.BIO[8 * 16 + 8];
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

  treeAt(x, z) {
    const ci = this.colInfo(x, z);
    const P = BPROP[ci.biome];
    if (ci.biome === BI.DESERT) return hash2(this.seed + 11, x, z) < 0.006 ? 'cactus' : 0;
    if (ci.biome === BI.ICE_SPIKES) return hash2(this.seed + 11, x, z) < 0.012 ? 'ice_spike' : 0;
    if (ci.biome === BI.BADLANDS) return hash2(this.seed + 11, x, z) < 0.004 ? 'cactus' : 0;
    if (!P.trees) return 0;
    const cell = P.cell, gx = Math.floor(x / cell), gz = Math.floor(z / cell);
    const px = gx * cell + ((hash2(this.seed + 21, gx, gz) * cell) | 0);
    const pz = gz * cell + ((hash2(this.seed + 22, gx, gz) * cell) | 0);
    if (px !== x || pz !== z) return 0;
    const r = hash2(this.seed + 23, gx, gz);
    for (const t of P.trees) if (r < t[0]) return t[1];
    return 0;
  }

  features() {
    const x0 = this.x0, z0 = this.z0, M = 9;
    for (let z = z0 - M; z < z0 + 16 + M; z++) for (let x = x0 - M; x < x0 + 16 + M; x++) {
      const t = this.treeAt(x, z);
      if (!t) continue;
      const sy = this.surfaceAt(x, z);
      if (sy < SEA || sy > WORLD_MAX_Y - 40) continue;
      // if this column is inside the chunk make sure the ground is soil
      const g = this.getB(x, sy, z);
      if (g >= 0) {
        if (t === 'cactus') { if (g !== B.SAND && g !== B.RED_SAND) continue; }
        else if (t === 'ice_spike') { if (g !== B.SNOW) continue; }
        else if (g !== B.GRASS && g !== B.DIRT && g !== B.PODZOL && g !== B.MYCELIUM && g !== B.COARSE_DIRT && g !== B.SNOW && g !== B.MUD) continue;
        if (this.getB(x, sy + 1, z) === B.WATER) continue;
      } else {
        const bi = this.colInfo(x, z).biome;
        const top = BPROP[bi].top;
        if (t === 'cactus') { if (top !== B.SAND && top !== B.RED_SAND) continue; }
        else if (top === B.SAND || top === B.RED_SAND || top === B.STONE) continue;
      }
      this.growTree(t, x, sy, z);
    }
    this.vegetation();
  }

  leafBlob(x, y, z, r, id, rnd, cut) {
    for (let dy = -r; dy <= r; dy++) for (let dz = -r; dz <= r; dz++) for (let dx = -r; dx <= r; dx++) {
      const d = dx * dx + dy * dy * 1.6 + dz * dz;
      if (d > r * r + 0.5) continue;
      if (cut && d > (r - 1) * (r - 1) + 1 && rnd() < 0.25) continue;
      this.setB(x + dx, y + dy, z + dz, id, 0, 0);
    }
  }
  leafLayer(x, y, z, r, id, rnd, corners) {
    for (let dz = -r; dz <= r; dz++) for (let dx = -r; dx <= r; dx++) {
      if (Math.abs(dx) === r && Math.abs(dz) === r) { if (!corners || rnd() < 0.5) continue; }
      this.setB(x + dx, y, z + dz, id, 0, 0);
    }
  }
  log(x, y, z, id, axis) { this.setB(x, y, z, id, axis | 0, 1); }

  growTree(type, x, sy, z) {
    const rnd = mulberry(hash2i(this.seed + 99, x, z));
    const y = sy + 1;
    switch (type) {
      case 'oak': case 'birch': case 'swamp_oak': case 'jungle': {
        const logId = type === 'birch' ? B.BIRCH_LOG : type === 'jungle' ? B.JUNGLE_LOG : B.LOG;
        const leafId = type === 'birch' ? B.BIRCH_LEAVES : type === 'jungle' ? B.JUNGLE_LEAVES : B.LEAVES;
        const h = (type === 'birch' ? 5 : type === 'jungle' ? 5 : 4) + ((rnd() * 3) | 0) + (type === 'jungle' ? (rnd() * 4) | 0 : 0);
        const top = y + h - 1;
        for (let dy = -3; dy <= 0; dy++) this.leafLayer(x, top + dy + 1, z, dy >= -1 ? 1 : 2, leafId, rnd, true);
        if (type === 'swamp_oak') { this.leafLayer(x, top - 2, z, 3, leafId, rnd, false); }
        for (let k = 0; k < h; k++) this.log(x, y + k, z, logId, 0);
        if (type === 'swamp_oak' || type === 'jungle') this.vines(x, top, z, type === 'jungle' ? 2 : 3, rnd);
        if (type === 'jungle') this.trunkVines(x, y, z, h, rnd);
        break;
      }
      case 'fancy_oak': {
        const h = 7 + ((rnd() * 5) | 0);
        for (let k = 0; k < h; k++) this.log(x, y + k, z, B.LOG, 0);
        this.leafBlob(x, y + h - 1, z, 2.6, B.LEAVES, rnd, true);
        const nb = 2 + ((rnd() * 3) | 0);
        for (let b = 0; b < nb; b++) {
          const a = rnd() * 6.283, by = y + 3 + ((rnd() * (h - 4)) | 0), len = 2 + ((rnd() * 3) | 0);
          let bx = x, bz = z, yy = by;
          const ax = Math.abs(Math.cos(a)) > Math.abs(Math.sin(a)) ? 1 : 2;
          for (let s = 1; s <= len; s++) {
            bx = x + Math.round(Math.cos(a) * s); bz = z + Math.round(Math.sin(a) * s); yy = by + (s >> 1);
            this.log(bx, yy, bz, B.LOG, ax);
          }
          this.leafBlob(bx, yy + 1, bz, 2.1, B.LEAVES, rnd, true);
        }
        break;
      }
      case 'spruce': {
        const h = 6 + ((rnd() * 4) | 0);
        const start = 1 + ((rnd() * 2) | 0);
        let r = 0, rmax = 2 + (rnd() < 0.4 ? 1 : 0);
        for (let k = h; k >= start; k--) {
          this.leafLayer(x, y + k, z, r, B.SPRUCE_LEAVES, rnd, false);
          r = r >= rmax ? 1 : r + 1;
          if (k === h) r = 1;
        }
        this.setB(x, y + h + 1, z, B.SPRUCE_LEAVES, 0, 0);
        for (let k = 0; k < h; k++) this.log(x, y + k, z, B.SPRUCE_LOG, 0);
        break;
      }
      case 'mega_spruce': {
        const h = 14 + ((rnd() * 10) | 0);
        const crown = Math.floor(h * 0.45);
        for (let k = h - crown; k <= h; k++) {
          const f = (h - k) / crown;
          const r = Math.round(1 + f * 3 + ((k & 1) ? 0 : -0.6));
          this.leafBlob2x2(x, y + k, z, r, B.SPRUCE_LEAVES);
        }
        this.setB(x, y + h + 1, z, B.SPRUCE_LEAVES, 0, 0); this.setB(x + 1, y + h + 1, z + 1, B.SPRUCE_LEAVES, 0, 0);
        for (let k = 0; k < h; k++) for (let d = 0; d < 4; d++) this.log(x + (d & 1), y + k, z + (d >> 1), B.SPRUCE_LOG, 0);
        for (let dz = -2; dz <= 3; dz++) for (let dx = -2; dx <= 3; dx++) {
          if (rnd() < 0.5) { const lx = x + dx, lz = z + dz; const g = this.getB(lx, sy, lz); if (g === B.GRASS || g === B.DIRT) this.setB(lx, sy, lz, B.PODZOL, 0, 2); }
        }
        break;
      }
      case 'dark_oak': {
        const h = 6 + ((rnd() * 3) | 0);
        for (let k = 0; k < h; k++) for (let d = 0; d < 4; d++) this.log(x + (d & 1), y + k, z + (d >> 1), B.DARK_LOG, 0);
        const top = y + h;
        for (let dy = -2; dy <= 1; dy++) {
          const r = dy === 1 ? 2 : dy === -2 ? 3 : 4;
          for (let dz = -r; dz <= r + 1; dz++) for (let dx = -r; dx <= r + 1; dx++) {
            const ex = dx < 0 ? -dx : dx - 1, ez = dz < 0 ? -dz : dz - 1;
            if (ex + ez > r + (dy === 0 ? 1 : 0) || (ex === r && ez === r)) continue;
            if (ex + ez === r + 1 && rnd() < 0.4) continue;
            this.setB(x + dx, top + dy, z + dz, B.DARK_LEAVES, 0, 0);
          }
        }
        // a few side branches
        for (let b = 0; b < 2; b++) {
          const dx = rnd() < 0.5 ? -1 : 2, dz = (rnd() * 2) | 0;
          this.log(x + dx, top - 2, z + dz, B.DARK_LOG, 1);
        }
        break;
      }
      case 'mega_jungle': {
        const h = 12 + ((rnd() * 14) | 0);
        for (let k = 0; k < h; k++) for (let d = 0; d < 4; d++) this.log(x + (d & 1), y + k, z + (d >> 1), B.JUNGLE_LOG, 0);
        const top = y + h;
        for (let dy = -2; dy <= 1; dy++) this.leafBlob2x2(x, top + dy, z, dy === 1 ? 2 : dy === 0 ? 4 : 3, B.JUNGLE_LEAVES);
        const nb = 2 + ((rnd() * 3) | 0);
        for (let b = 0; b < nb; b++) {
          const a = rnd() * 6.283, by = y + Math.floor(h * (0.45 + rnd() * 0.35));
          const bx = x + Math.round(Math.cos(a) * 3), bz = z + Math.round(Math.sin(a) * 3);
          this.log(x + Math.round(Math.cos(a) * 1.5), by, z + Math.round(Math.sin(a) * 1.5), B.JUNGLE_LOG, 1);
          this.log(bx, by + 1, bz, B.JUNGLE_LOG, 0);
          this.leafBlob(bx, by + 2, bz, 2.2, B.JUNGLE_LEAVES, rnd, true);
        }
        this.trunkVines(x - 1, y, z, h, rnd); this.trunkVines(x + 2, y, z + 1, h, rnd);
        break;
      }
      case 'jungle_bush': {
        this.log(x, y, z, B.JUNGLE_LOG, 0);
        this.leafBlob(x, y + 1, z, 2.2, B.LEAVES, rnd, true);
        this.leafLayer(x, y, z, 2, B.LEAVES, rnd, false);
        break;
      }
      case 'acacia': {
        const h = 5 + ((rnd() * 3) | 0);
        const dir = (rnd() * 4) | 0, ddx = [1, -1, 0, 0][dir], ddz = [0, 0, 1, -1][dir];
        const bend = h - 1 - ((rnd() * 3) | 0);
        let cx = x, cz = z, k = 0;
        for (; k < h; k++) {
          if (k >= bend) { cx += ddx; cz += ddz; }
          this.log(cx, y + k, cz, B.ACACIA_LOG, 0);
        }
        const top = y + h;
        this.leafLayer(cx, top - 1, cz, 3, B.ACACIA_LEAVES, rnd, false);
        this.leafLayer(cx, top, cz, 1, B.ACACIA_LEAVES, rnd, true);
        for (let dz = -2; dz <= 2; dz++) for (let dx = -2; dx <= 2; dx++) if (Math.abs(dx) + Math.abs(dz) <= 3) this.setB(cx + dx, top, cz + dz, B.ACACIA_LEAVES, 0, 0);
        if (rnd() < 0.6) {
          const d2 = (dir + 1 + ((rnd() * 2) | 0) * 2) & 3, ex = [1, -1, 0, 0][d2], ez = [0, 0, 1, -1][d2];
          let bx = x, bz = z; const by = y + bend - 1;
          for (let s = 1; s <= 2; s++) { bx += ex; bz += ez; this.log(bx, by + s, bz, B.ACACIA_LOG, 0); }
          this.leafLayer(bx, by + 3, bz, 2, B.ACACIA_LEAVES, rnd, false);
          this.leafLayer(bx, by + 4, bz, 1, B.ACACIA_LEAVES, rnd, false);
        }
        break;
      }
      case 'cherry': {
        const h = 5 + ((rnd() * 2) | 0);
        for (let k = 0; k < h; k++) this.log(x, y + k, z, B.LOG, 0);
        const top = y + h;
        this.leafBlob(x, top, z, 3.1, B.FLOWERING_AZALEA_LEAVES, rnd, true);
        this.leafLayer(x, top - 2, z, 3, rnd() < 0.5 ? B.AZALEA_LEAVES : B.FLOWERING_AZALEA_LEAVES, rnd, false);
        break;
      }
      case 'huge_mushroom': {
        const red = rnd() < 0.5;
        const h = 4 + ((rnd() * 3) | 0);
        for (let k = 0; k < h; k++) this.log(x, y + k, z, B.MUSHROOM_STEM, 0);
        if (red) {
          for (let k = h - 3; k < h; k++) for (let dz = -2; dz <= 2; dz++) for (let dx = -2; dx <= 2; dx++) {
            if (Math.abs(dx) === 2 && Math.abs(dz) === 2) continue;
            if (Math.abs(dx) < 2 && Math.abs(dz) < 2) continue;
            this.setB(x + dx, y + k, z + dz, B.RED_MUSHROOM_BLOCK, 0, 0);
          }
          this.leafLayer(x, y + h, z, 1, B.RED_MUSHROOM_BLOCK, rnd, true);
        } else {
          for (let dz = -3; dz <= 3; dz++) for (let dx = -3; dx <= 3; dx++) {
            if (Math.abs(dx) === 3 && Math.abs(dz) === 3) continue;
            this.setB(x + dx, y + h, z + dz, B.BROWN_MUSHROOM_BLOCK, 0, 0);
          }
        }
        break;
      }
      case 'cactus': {
        const h = 1 + ((rnd() * 3) | 0);
        for (let k = 0; k < h; k++) this.setB(x, y + k, z, B.CACTUS, 0, 0);
        break;
      }
      case 'ice_spike': {
        const h = 5 + ((rnd() * 9) | 0), big = rnd() < 0.1;
        const hh = big ? h * 3 : h;
        for (let k = 0; k < hh; k++) {
          const r = Math.max(0, Math.round((1 - k / hh) * (big ? 3 : 1.4)));
          for (let dz = -r; dz <= r; dz++) for (let dx = -r; dx <= r; dx++) if (dx * dx + dz * dz <= r * r + 0.3) this.setB(x + dx, y + k, z + dz, B.PACKED_ICE, 0, 0);
        }
        break;
      }
    }
  }
  leafBlob2x2(x, y, z, r, id) {
    for (let dz = -r; dz <= r + 1; dz++) for (let dx = -r; dx <= r + 1; dx++) {
      const ex = dx < 0 ? -dx : dx - 1, ez = dz < 0 ? -dz : dz - 1;
      if (ex * ex + ez * ez > r * r + 0.5) continue;
      this.setB(x + dx, y, z + dz, id, 0, 0);
    }
  }
  // vine meta bits: 1 = attached to +z(south) block? We store the side the vine hugs: 1 south,2 west,4 north,8 east
  vines(x, top, z, r, rnd) {
    for (let dz = -r - 1; dz <= r + 1; dz++) for (let dx = -r - 1; dx <= r + 1; dx++) {
      if (rnd() > 0.28) continue;
      const vx = x + dx, vz = z + dz;
      for (let dy = 0; dy >= -3; dy--) {
        const yy = top + dy;
        if (this.getB(vx, yy, vz) !== 0) continue;
        let side = 0;
        if (FLAGS[this.getB(vx, yy, vz + 1)] & BF_LEAVES) side = 1;
        else if (FLAGS[this.getB(vx - 1, yy, vz)] & BF_LEAVES) side = 2;
        else if (FLAGS[this.getB(vx, yy, vz - 1)] & BF_LEAVES) side = 4;
        else if (FLAGS[this.getB(vx + 1, yy, vz)] & BF_LEAVES) side = 8;
        if (!side) continue;
        const len = 1 + ((rnd() * 5) | 0);
        for (let k = 0; k < len; k++) { if (this.getB(vx, yy - k, vz) !== 0) break; this.setB(vx, yy - k, vz, B.VINE, side, 0); }
        break;
      }
    }
  }
  trunkVines(x, y, z, h, rnd) {
    const sides = [[0, -1, 1], [1, 0, 2], [0, 1, 4], [-1, 0, 8]];
    for (const [dx, dz, bit] of sides) {
      if (rnd() < 0.5) continue;
      for (let k = 1; k < h; k++) if (rnd() < 0.7) this.setB(x + dx, y + k, z + dz, B.VINE, bit, 0);
    }
  }

  vegetation() {
    const ids = this.ids, meta = this.meta, x0 = this.x0, z0 = this.z0, seed = this.seed;
    const pick = (arr, r) => arr[Math.floor(r * arr.length) % arr.length];
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      const wx = x0 + x, wz = z0 + z;
      const b = this.BIO[z * 16 + x];
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
        const oceanish = b <= BI.WARM_OCEAN || b === BI.RIVER;
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
