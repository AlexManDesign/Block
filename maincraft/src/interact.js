'use strict';
// Block placement / breaking rules, drops and tool logic.

// horizontal dir of a face index (faces 0 +x,1 -x,4 +z,5 -z) -> 0 S,1 W,2 N,3 E ; -1 for up/down
const FACE_HDIR = [3, 1, -1, -1, 0, 2];
const FACE_N = [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]];

function isSoil(id) {
  return id === B.GRASS || id === B.DIRT || id === B.PODZOL || id === B.COARSE_DIRT || id === B.FARMLAND ||
    id === B.FARMLAND_MOIST || id === B.MYCELIUM || id === B.MOSS_BLOCK || id === B.MUD || id === B.ROOTED_DIRT;
}

// compute block id + meta to place, or null. hit = raycast result, player facing dir fd
function placementFor(world, heldId, hit, fd, player) {
  const n = FACE_N[hit.face];
  let x = hit.x, y = hit.y, z = hit.z;
  const tid = hit.id;
  // clicking into a replaceable block places there
  const replaceTarget = (FLAGS[tid] & BF_REPLACE) && tid !== heldId && SHAPE[tid] !== SH.WATER && SHAPE[tid] !== SH.LAVA;
  if (!replaceTarget) { x += n[0]; y += n[1]; z += n[2]; }
  if (y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return null;
  const cur = world.getBlock(x, y, z);
  const fy = hit.py - Math.floor(hit.py);
  const sh = SHAPE[heldId];
  const out = [];
  // slab onto a matching slab -> full block
  if (sh === SH.SLAB) {
    const tm = world.getMeta(hit.x, hit.y, hit.z);
    if (tid === heldId && ((hit.face === 2 && !(tm & 1)) || (hit.face === 3 && (tm & 1)))) {
      const base = BASE[heldId] || heldId;
      return [{ x: hit.x, y: hit.y, z: hit.z, id: base === heldId ? heldId : base, m: 0 }];
    }
    if (cur === heldId && BASE[heldId]) return [{ x, y, z, id: BASE[heldId], m: 0 }];
  }
  if (cur && !(FLAGS[cur] & BF_REPLACE)) return null;
  // what water or lava would wash away cannot be placed into it (torches, flowers, saplings...)
  if ((isWaterId(cur) || cur === B.LAVA) && fluidBreaks(heldId)) return null;
  // mangrove roots placed into water stay waterlogged
  if (heldId === B.MANGROVE_ROOTS && isWaterId(cur)) return [{ x, y, z, id: B.MANGROVE_ROOTS_WET, m: 0 }];
  const below = world.getBlock(x, y - 1, z);
  let m = 0;
  const fl = FLAGS[heldId];
  const hdir = FACE_HDIR[hit.face];
  switch (sh) {
    case SH.SLAB: m = hit.face === 3 || (hit.face !== 2 && fy > 0.5) ? 1 : 0; break;
    case SH.STAIRS: m = fd | ((hit.face === 3 || (hit.face !== 2 && fy > 0.5)) ? 4 : 0); break;
    case SH.GATE: m = fd; break;
    case SH.DOOR: {
      if (!SOLID[below] || world.getBlock(x, y + 1, z) !== 0 && !(FLAGS[world.getBlock(x, y + 1, z)] & BF_REPLACE)) return null;
      // hinge: right if the clicked point is on the right half relative to facing
      const lx = hit.px - x, lz = hit.pz - z;
      const rightHalf = [lx < 0.5, lz < 0.5, lx > 0.5, lz > 0.5][fd];
      const hinge = rightHalf ? 16 : 0;
      out.push({ x, y, z, id: heldId, m: fd | hinge }, { x, y: y + 1, z, id: heldId, m: fd | 4 | hinge });
      return out;
    }
    case SH.TRAPDOOR:
      if (hdir >= 0) m = hdir | (fy > 0.5 ? 4 : 0);   // TrapDoorBlock: facing = clicked face, hinged on that block
      else m = ((fd + 2) & 3) | (hit.face === 3 ? 4 : 0);
      break;
    case SH.LADDER:
      if (hdir < 0) return null;
      if (!OPAQUE[world.getBlock(x - n[0], y, z - n[2])]) return null;
      m = hdir; break;
    case SH.TORCH:
      if (hit.face === 3) return null;
      if (hdir >= 0) { if (!OPAQUE[world.getBlock(x - n[0], y, z - n[2])]) return null; m = 1 + ((hdir + 2) & 3); }
      else { if (!SOLID[below]) return null; m = 0; }
      break;
    case SH.BUTTON:
      if (hdir < 0) return null;
      m = (hdir + 2) & 3; break;
    case SH.CRYSTAL:
      // grows out of the clicked face (meta = face, FN order), needs a full block behind
      if (!OPAQUE[world.getBlock(x - n[0], y - n[1], z - n[2])]) return null;
      m = hit.face; break;
    case SH.BED: {
      const hx = x + DIRX_W[fd], hz = z + DIRZ_W[fd];
      const hc = world.getBlock(hx, y, hz);
      if ((hc && !(FLAGS[hc] & BF_REPLACE)) || !SOLID[below] || !SOLID[world.getBlock(hx, y - 1, hz)]) return null;
      return [{ x, y, z, id: heldId, m: fd }, { x: hx, y, z: hz, id: heldId, m: fd | 4 }];
    }
    case SH.TALL: {
      const up = world.getBlock(x, y + 1, z);
      if (!isSoil(below) || (up && !(FLAGS[up] & BF_REPLACE))) return null;
      return [{ x, y, z, id: heldId, m: 0 }, { x, y: y + 1, z, id: heldId, m: 1 }];
    }
    case SH.CROSS:
      if (fl & BF_AQUATIC) { if (!isWaterId(cur) && cur !== B.WATER) return null; if (!SOLID[below] && below !== heldId) return null; }
      else if (fl & BF_HANG) { const a = world.getBlock(x, y + 1, z); if (!SOLID[a] && a !== heldId) return null; }
      else if (heldId === B.SUGAR_CANE) { if (!world.caneSupported(x, y, z)) return null; }
      else if (heldId === B.DEAD_BUSH) { if (!isSoil(below) && below !== B.SAND && below !== B.RED_SAND && below !== B.TERRACOTTA) return null; }
      else if (/MUSHROOM|FUNGUS|ROOTS|SPROUTS/.test(B_KEY[heldId])) { if (!SOLID[below]) return null; }
      else if (heldId === B.COBWEB) { /* anywhere */ }
      else if (/^(WHEAT|CARROTS|POTATOES|PUMPKIN_STEM|MELON_STEM)_/.test(B_KEY[heldId])) { if (below !== B.FARMLAND && below !== B.FARMLAND_MOIST) return null; }
      else if (!isSoil(below)) return null;
      break;
    case SH.LILY:
      if (below !== B.WATER || cur !== 0) return null;
      m = (Math.random() * 4) | 0; break;
    case SH.PICKLE: case SH.CARPET: case SH.PLATE: case SH.RAIL:
      if (!SOLID[below]) return null;
      m = sh === SH.RAIL ? (fd & 1) : 0; break;
    case SH.CACTUS:
      if (below !== B.SAND && below !== B.RED_SAND && below !== B.CACTUS) return null; break;
    case SH.BAMBOO:
      if (!isSoil(below) && below !== B.BAMBOO_PLANT && below !== B.SAND) return null; break;
    case SH.VINE:
      if (hdir < 0) return null;
      m = 1 << ((hdir + 2) & 3); break;
    case SH.LICHEN:
      m = hdir >= 0 ? 1 << ((hdir + 2) & 3) : hit.face === 2 ? 16 : 32; break;
    case SH.CHEST: m = (fd + 2) & 3; break;
    default:
      if (fl & BF_AXIS) m = hit.face <= 1 ? 1 : hit.face >= 4 ? 2 : 0;
      else if (fl & BF_FACING) m = (fd + 2) & 3;
  }
  // aquatic plants keep water around them; blocks that can hold water placed into a water source
  // become waterlogged; other blocks in water displace it
  const wet = WET[heldId] && ((cur === B.WATER && world.getMeta(x, y, z) === 0) || (FLAGS[cur] & (BF_AQUATIC | BF_WET)));
  out.push({ x, y, z, id: wet ? WET[heldId] : heldId, m });
  return out;
}

// ---------------- survival: tools, hardness, drops
function toolInfo(item) {
  if (!item || !isItem(item.id)) return null;
  const d = itemDef(item.id);
  return d && d[5] && d[5].tool ? d[5] : null;
}
// attack damage / attack speed of the held item (vanilla item attributes; tier 1 wood, 2 stone, 3 iron, 4 gold, 5 diamond)
const ATK_TABLE = {
  sword: [[4, 5, 6, 4, 7], [1.6, 1.6, 1.6, 1.6, 1.6]],
  axe: [[7, 9, 9, 7, 9], [0.8, 0.8, 0.9, 1, 1]],
  pickaxe: [[2, 3, 4, 2, 5], [1.2, 1.2, 1.2, 1.2, 1.2]],
  shovel: [[2.5, 3.5, 4.5, 2.5, 5.5], [1, 1, 1, 1, 1]],
  hoe: [[1, 1, 1, 1, 1], [1, 2, 3, 1, 4]],
};
function attackStats(item) {
  const t = toolInfo(item), e = t && ATK_TABLE[t.tool];
  if (!e) return { dmg: 1, speed: 4 };
  const i = Math.min(4, Math.max(0, t.tier - 1));
  return { dmg: e[0][i], speed: e[1][i] };
}
const TIER_SPEED = [1, 2, 4, 6, 12, 8];
function needTier(id) {
  const k = B_KEY[id];
  if (k === 'OBSIDIAN' || k === 'CRYING_OBSIDIAN' || k === 'ANCIENT_DEBRIS' || k === 'NETHERITE_BLOCK') return 5;
  if (/DIAMOND|GOLD|EMERALD|REDSTONE/.test(k) && (/ORE|BLOCK/.test(k))) return 3;
  if (/IRON|LAPIS|COPPER/.test(k) && (/ORE|BLOCK/.test(k))) return 2;
  if (TOOL[id] === 1) return 1;
  return 0;
}
// seconds to break id with held item
function breakTime(id, item) {
  const h = HARD[id];
  if (h < 0) return Infinity;
  if (h === 0) return 0.05;
  const t = toolInfo(item);
  const kind = ['', 'pickaxe', 'axe', 'shovel', 'hoe'][TOOL[id]] || '';
  let speed = 1;
  const right = t && (t.tool === kind || (t.tool === 'shears' && (FLAGS[id] & BF_LEAVES || B_KEY[id].includes('WOOL') || id === B.COBWEB)) || (t.tool === 'sword' && id === B.COBWEB));
  if (right) speed = t.tool === 'shears' ? 5 : t.tool === 'sword' ? 15 : TIER_SPEED[t.tier];
  const need = needTier(id);
  const harvest = need === 0 || (t && t.tool === 'pickaxe' && t.tier >= (need === 5 ? 5 : need === 3 ? 3 : need === 2 ? 2 : 1) && (need !== 5 || t.tier === 5));
  return h * (harvest ? 1.5 : 5) / speed;
}
function canHarvest(id, item) {
  const need = needTier(id);
  if (!need) return true;
  const t = toolInfo(item);
  if (!t || t.tool !== 'pickaxe') return false;
  const tier = t.tier === 4 ? 1 : t.tier; // gold = wood level
  return tier >= need;
}
// list of {id,count}
function dropsFor(id, m, item) {
  id = dryId(id);
  if (!canHarvest(id, item)) return [];
  const k = B_KEY[id], r = Math.random();
  const t = toolInfo(item);
  const shears = t && t.tool === 'shears';
  if (FLAGS[id] & BF_LEAVES) {
    if (shears) return [{ id, count: 1 }];
    const out = [];
    const sap = { LEAVES: 'OAK_SAPLING', BIRCH_LEAVES: 'BIRCH_SAPLING', SPRUCE_LEAVES: 'SPRUCE_SAPLING', DARK_LEAVES: 'DARK_OAK_SAPLING', JUNGLE_LEAVES: 'JUNGLE_SAPLING', ACACIA_LEAVES: 'ACACIA_SAPLING' }[k];
    if (sap && r < 0.05) out.push({ id: B[sap], count: 1 });
    if ((k === 'LEAVES' || k === 'DARK_LEAVES') && Math.random() < 0.005) out.push({ id: IT.APPLE, count: 1 });
    if (Math.random() < 0.02) out.push({ id: IT.STICK, count: 1 });
    return out;
  }
  const map = {
    STONE: [B.COBBLE], GRASS: [B.DIRT], PODZOL: [B.DIRT], MYCELIUM: [B.DIRT], DIRT_PATH: [B.DIRT], FARMLAND: [B.DIRT], FARMLAND_MOIST: [B.DIRT],
    DEEPSLATE: [B.COBBLED_DEEPSLATE], COAL: [IT.COAL], DEEPSLATE_COAL_ORE: [IT.COAL], DIAMOND: [IT.DIAMOND], DEEPSLATE_DIAMOND_ORE: [IT.DIAMOND],
    EMERALD_ORE: [IT.EMERALD], DEEPSLATE_EMERALD_ORE: [IT.EMERALD], COPPER_ORE: [IT.RAW_COPPER, 2, 5], DEEPSLATE_COPPER_ORE: [IT.RAW_COPPER, 2, 5],
    CLAY: [IT.CLAY_BALL, 4, 4], GLOWSTONE: [B.GLOW], MELON: [IT.MELON_SLICE, 3, 7], BOOKSHELF: [B.PLANKS, 3, 3],
    FURNACE_LIT: [B.FURNACE], BUDDING_AMETHYST: [], ICE: [], GLASS: [], GLASS_PANE: [], FIRE: [],
    SNOW: [B.SNOW], TALL_GRASS: [], FERN: [], DEAD_BUSH: [IT.STICK, 0, 2], SEAGRASS: [], TALL_GRASS_PLANT: [], LARGE_FERN: [],
    SWEET_BERRY_BUSH: [IT.SWEET_BERRIES, 1, 3], MANGROVE_ROOTS_WET: [B.MANGROVE_ROOTS], CAVE_VINES: [], KELP: [B.KELP], COBWEB: [IT.STRING], SPAWNER: [],
    WHEAT_0: [IT.WHEAT_SEEDS], WHEAT_1: [IT.WHEAT_SEEDS], WHEAT_2: [IT.WHEAT_SEEDS], WHEAT_3: [IT.WHEAT],
    CARROTS_0: [IT.CARROT], CARROTS_1: [IT.CARROT], CARROTS_2: [IT.CARROT], CARROTS_3: [IT.CARROT, 2, 4],
    POTATOES_0: [IT.POTATO], POTATOES_1: [IT.POTATO], POTATOES_2: [IT.POTATO], POTATOES_3: [IT.POTATO, 2, 4],
  };
  if ((k === 'TALL_GRASS' || k === 'FERN' || k === 'TALL_GRASS_PLANT') && !shears) return r < 0.125 ? [{ id: IT.WHEAT_SEEDS, count: 1 }] : [];
  // snow and snow layers need a shovel (Minecraft: requiresCorrectToolForDrops), so snow washed
  // away by water or broken by hand drops nothing
  if (k === 'SNOW' || k === 'SNOW_LAYER') return t && t.tool === 'shovel' ? [{ id, count: 1 }] : [];
  if (k === 'GRAVEL') return r < 0.1 ? [{ id: IT.FLINT, count: 1 }] : [{ id, count: 1 }];
  if (k === 'WHEAT_3') return [{ id: IT.WHEAT, count: 1 }, { id: IT.WHEAT_SEEDS, count: 1 + ((Math.random() * 3) | 0) }];
  if (/LAPIS_ORE|REDSTONE_ORE/.test(k)) return [{ id, count: 1 }];
  if (SHAPE[id] === SH.WATER || SHAPE[id] === SH.LAVA) return [];
  if (map[k] && !(shears && (k === 'TALL_GRASS' || k === 'FERN'))) {
    const e = map[k];
    if (!e.length) return [];
    const n = e.length === 3 ? e[1] + Math.floor(Math.random() * (e[2] - e[1] + 1)) : 1;
    return n > 0 ? [{ id: e[0], count: n }] : [];
  }
  if (FLAGS[id] & BF_AQUATIC) return shears ? [{ id, count: 1 }] : [];
  if (SHAPE[id] === SH.TALL || SHAPE[id] === SH.DOOR || SHAPE[id] === SH.BED) return (m & (SHAPE[id] === SH.DOOR ? 4 : SHAPE[id] === SH.TALL ? 1 : 4)) ? [] : [{ id, count: 1 }];
  return [{ id, count: 1 }];
}

// ---------------- loot tables of generated chests (Minecraft 1.18 data/minecraft/loot_tables/chests).
// Pool: [minRolls, maxRolls, entries], entry: [item, weight, min, max]. Items this game does not have
// (saddles, name tags, discs, enchanted books, horse armour, redstone, lapis, nuggets, paper...) stay
// in as empty results, so the odds of the others are Minecraft's.
const LOOT_TABLES = (function () {
  const N = 0;
  const T = {};
  T[LOOT.SIMPLE_DUNGEON] = [
    [1, 3, [[N, 20], [IT.GOLDEN_APPLE, 15], [N, 2], [N, 2], [N, 15], [N, 15], [N, 20], [N, 10], [N, 15], [N, 5], [N, 10]]],
    [1, 4, [[IT.IRON_INGOT, 10, 1, 4], [IT.GOLD_INGOT, 5, 1, 4], [IT.BREAD, 20], [IT.WHEAT, 20, 1, 4], [IT.BUCKET, 10], [N, 15], [IT.COAL, 15, 1, 4], [IT.MELON_SEEDS, 10, 2, 4], [IT.PUMPKIN_SEEDS, 10, 2, 4], [N, 10]]],
    [3, 3, [[IT.BONE, 10, 1, 8], [IT.GUNPOWDER, 10, 1, 8], [IT.ROTTEN_FLESH, 10, 1, 8], [IT.STRING, 10, 1, 8]]],
  ];
  T[LOOT.ABANDONED_MINESHAFT] = [
    [1, 1, [[IT.GOLDEN_APPLE, 20], [N, 1], [N, 30], [N, 10], [IT.IRON_PICKAXE, 5], [N, 5]]],
    [2, 4, [[IT.IRON_INGOT, 10, 1, 5], [IT.GOLD_INGOT, 5, 1, 3], [N, 5], [N, 5], [IT.DIAMOND, 3, 1, 2], [IT.COAL, 10, 3, 8], [IT.BREAD, 15, 1, 3], [N, 15], [IT.MELON_SEEDS, 10, 2, 4], [IT.PUMPKIN_SEEDS, 10, 2, 4], [N, 10]]],
    [3, 3, [[B.RAIL, 20, 4, 8], [N, 5], [N, 5], [N, 5], [B.TORCH, 15, 1, 16]]],
  ];
  T[LOOT.DESERT_PYRAMID] = [
    [2, 4, [[IT.DIAMOND, 5, 1, 3], [IT.IRON_INGOT, 15, 1, 5], [IT.GOLD_INGOT, 15, 2, 7], [IT.EMERALD, 15, 1, 3], [IT.BONE, 25, 4, 6], [IT.SPIDER_EYE, 25, 1, 3], [IT.ROTTEN_FLESH, 25, 3, 7], [N, 20], [N, 15], [N, 10], [N, 5], [N, 20], [IT.GOLDEN_APPLE, 20], [N, 2], [N, 15]]],
    [4, 4, [[IT.BONE, 10, 1, 8], [IT.GUNPOWDER, 10, 1, 8], [IT.ROTTEN_FLESH, 10, 1, 8], [IT.STRING, 10, 1, 8], [B.SAND, 10, 1, 8]]],
  ];
  T[LOOT.JUNGLE_TEMPLE] = [
    [2, 6, [[IT.DIAMOND, 3, 1, 3], [IT.IRON_INGOT, 10, 1, 5], [IT.GOLD_INGOT, 15, 2, 7], [IT.EMERALD, 2, 1, 3], [IT.BONE, 20, 4, 6], [IT.ROTTEN_FLESH, 16, 3, 7], [N, 3], [N, 1], [N, 1], [N, 1], [N, 1]]],
  ];
  T[LOOT.SHIPWRECK_SUPPLY] = [
    [3, 10, [[N, 8], [IT.POTATO, 7, 2, 6], [N, 7], [IT.CARROT, 7, 4, 8], [IT.WHEAT, 7, 8, 21], [N, 10], [IT.COAL, 6, 2, 8], [IT.ROTTEN_FLESH, 5, 5, 24], [B.PUMPKIN, 2, 1, 3], [N, 2], [IT.GUNPOWDER, 3, 1, 5], [B.TNT, 1, 1, 2], [N, 3], [N, 3], [N, 3], [N, 3]]],
  ];
  T[LOOT.SHIPWRECK_TREASURE] = [
    [3, 6, [[IT.IRON_INGOT, 90, 1, 5], [IT.GOLD_INGOT, 10, 1, 5], [IT.EMERALD, 40, 1, 5], [IT.DIAMOND, 5], [N, 5]]],
    [2, 5, [[N, 50], [N, 10], [N, 20]]],
  ];
  T[LOOT.SHIPWRECK_MAP] = [
    [1, 1, [[N, 1]]],
    [3, 3, [[N, 1], [N, 1], [N, 1], [N, 20], [IT.FEATHER, 10, 1, 5], [N, 5]]],
  ];
  T[LOOT.UNDERWATER_RUIN_SMALL] = [
    [2, 8, [[IT.COAL, 10, 1, 4], [IT.STONE_AXE, 2], [IT.ROTTEN_FLESH, 5], [IT.EMERALD, 1], [IT.WHEAT, 10, 2, 3]]],
    [1, 1, [[N, 1], [N, 1], [N, 5], [N, 5]]],
  ];
  T[LOOT.UNDERWATER_RUIN_BIG] = [
    [2, 8, [[IT.COAL, 10, 1, 4], [N, 10], [IT.EMERALD, 1], [IT.WHEAT, 10, 2, 3]]],
    [1, 1, [[IT.GOLDEN_APPLE, 1], [N, 5], [N, 1], [N, 1], [N, 5], [N, 10]]],
  ];
  T[LOOT.IGLOO] = [
    [2, 8, [[IT.APPLE, 15, 1, 3], [IT.COAL, 15, 1, 4], [N, 10], [IT.STONE_AXE, 2], [IT.ROTTEN_FLESH, 10], [IT.EMERALD, 1], [IT.WHEAT, 10, 2, 3]]],
    [1, 1, [[IT.GOLDEN_APPLE, 1]]],
  ];
  return T;
})();
// LootTable.fill: roll every pool, then spread the stacks over random free slots of the chest
function rollLoot(table) {
  const out = new Array(27).fill(null), T = LOOT_TABLES[table];
  if (!T) return out;
  const ri = (a, b) => a + Math.floor(Math.random() * (b - a + 1));
  const stacks = [];
  for (const [r0, r1, entries] of T) {
    let total = 0;
    for (const e of entries) total += e[1];
    for (let n = ri(r0, r1); n > 0; n--) {
      let w = Math.random() * total, e = entries[0];
      for (const c of entries) { w -= c[1]; if (w < 0) { e = c; break; } }
      if (!e[0]) continue;
      const count = e.length > 2 ? ri(e[2], e[3]) : 1;
      for (let c = count; c > 0;) { const k = Math.min(c, maxStack(e[0])); stacks.push({ id: e[0], count: k }); c -= k; }
    }
  }
  const free = []; for (let i = 0; i < 27; i++) free.push(i);
  for (const s of stacks) { if (!free.length) break; out[free.splice(Math.floor(Math.random() * free.length), 1)[0]] = s; }
  return out;
}
// contents of a chest (rolling its loot table the first time; the block keeps only its facing then)
function chestContents(game, x, y, z) {
  const C = game.chests = game.meta.chests || (game.meta.chests = {}), k = `${x},${y},${z}`;
  const m = game.world.getMeta(x, y, z), id = game.world.getBlock(x, y, z);
  if (dryId(id) === B.CHEST && (m >> 2)) {
    if (!C[k]) C[k] = rollLoot(m >> 2);
    game.world.setBlock(x, y, z, id, m & 3);
  }
  return C[k] || null;
}
