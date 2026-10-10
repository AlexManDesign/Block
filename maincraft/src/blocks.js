'use strict';
// Shared block registry: turns BLOCK_TABLE into flat typed arrays for hot loops.
// Used by the main thread and by the workers (generator + mesher).

const WORLD_MIN_Y = -64, WORLD_H = 384, WORLD_MAX_Y = WORLD_MIN_Y + WORLD_H, SECTIONS = 24, SEA = 63;

// shapes
const SH = {
  AIR: 0, CUBE: 1, CROSS: 2, WATER: 3, LAVA: 4, SLAB: 5, STAIRS: 6, FENCE: 7, GATE: 8, WALL: 9,
  PANE: 10, DOOR: 11, TRAPDOOR: 12, LADDER: 13, TORCH: 14, CARPET: 15, PLATE: 16, BUTTON: 17,
  TALL: 18, VINE: 19, LICHEN: 20, LILY: 21, PICKLE: 22, BAMBOO: 23, POT: 24, RAIL: 25, CACTUS: 26,
  CHEST: 27, BED: 28, FARMLAND: 29, FIRE: 30, SNOWLAYER: 31, CRYSTAL: 32, MODEL: 33, WIRE: 34,
};
const SHAPE_BY_NAME = {
  '': SH.CUBE, x: SH.CROSS, water: SH.WATER, lava: SH.LAVA, slab: SH.SLAB, stairs: SH.STAIRS,
  fence: SH.FENCE, gate: SH.GATE, wall: SH.WALL, pane: SH.PANE, door: SH.DOOR, trapdoor: SH.TRAPDOOR,
  ladder: SH.LADDER, torch: SH.TORCH, carpet: SH.CARPET, plate: SH.PLATE, button: SH.BUTTON,
  tallplant: SH.TALL, vine: SH.VINE, lichen: SH.LICHEN, lilypad: SH.LILY, seapickle: SH.PICKLE,
  bamboo: SH.BAMBOO, pot: SH.POT, rail: SH.RAIL, cactus: SH.CACTUS, chest: SH.CHEST, bed: SH.BED,
  farmland: SH.FARMLAND, fire: SH.FIRE, snowlayer: SH.SNOWLAYER, crystal: SH.CRYSTAL, model: SH.MODEL, wire: SH.WIRE,
};

// render layers
const RL_SOLID = 0, RL_CUTOUT = 1, RL_TRANS = 2;
// block flag bits
const BF_LEAVES = 1, BF_AXIS = 2, BF_GLASS = 4, BF_TRANS = 8, BF_FALL = 16, BF_EMISSIVE = 32,
  BF_AQUATIC = 64, BF_NEEDWATER = 128, BF_HANG = 256, BF_CLIMB = 512, BF_STACK = 1024,
  BF_FACING = 2048, BF_REPLACE = 4096, BF_PLANT = 8192, BF_WET = 16384;

const NB = BLOCK_TABLE.length + 1;
// Waterlogged blocks (Minecraft's waterlogged=true): a twin id WET_BASE + id with the same shape,
// textures and behaviour that also holds a water source. Fixed offset, so saved ids stay valid.
const WET_BASE = 4096, NBX = WET_BASE + NB;
const B = { AIR: 0 };
const B_KEY = ['AIR'], B_EN = ['Air'], B_RU = ['Воздух'];
const SHAPE = new Uint8Array(NBX), OPAQUE = new Uint8Array(NBX), RLAYER = new Uint8Array(NBX);
const LCOST = new Uint8Array(NBX), VDIM = new Uint8Array(NBX), EMIT = new Uint8Array(NBX), SOLID = new Uint8Array(NBX);
// blocks bodies bump into: the solid ones, and ladders (a thin board against the wall, as in Minecraft)
const COLLIDE = new Uint8Array(NBX);
const FLAGS = new Uint16Array(NBX), BASE = new Uint16Array(NBX), TAB = new Uint8Array(NBX);
const HARD = new Float32Array(NBX), TOOL = new Uint8Array(NBX);
// face textures (layer index): 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z ; FRONT = facing face texture
const FTEX = new Uint16Array(NBX * 6), FRONT = new Uint16Array(NBX);
const TEXNAMES = new Array(NBX); // raw texture names per block (for icons / worker tex resolve)
const TABS = ['b', 'c', 'n', 'f'];

for (let i = 0; i < BLOCK_TABLE.length; i++) {
  const r = BLOCK_TABLE[i], id = i + 1;
  B[r[0]] = id; B_KEY[id] = r[0]; B_EN[id] = r[1]; B_RU[id] = r[2];
}

(function initBlocks() {
  LCOST[0] = 1;
  for (let id = 1; id < NB; id++) {
    const r = BLOCK_TABLE[id - 1];
    const key = r[0], flagsS = r[5];
    let sh = SHAPE_BY_NAME[r[4]]; if (sh === undefined) sh = SH.CUBE;
    SHAPE[id] = sh;
    let f = 0;
    for (const c of flagsS) f |= { L: BF_LEAVES, Y: BF_AXIS, G: BF_GLASS, T: BF_TRANS, F: BF_FALL, e: BF_EMISSIVE,
      A: BF_AQUATIC, W: BF_NEEDWATER, h: BF_HANG, c: BF_CLIMB, S: BF_STACK, O: BF_FACING }[c] || 0;
    if (sh === SH.CROSS || sh === SH.TALL || sh === SH.VINE || sh === SH.LICHEN || sh === SH.LILY) f |= BF_PLANT;
    if (sh === SH.CROSS || sh === SH.TALL || sh === SH.WATER || sh === SH.LAVA || sh === SH.VINE ||
        sh === SH.LICHEN || sh === SH.FIRE || key === 'SNOW_LAYER') f |= BF_REPLACE;
    if (/SAPLING|EYEBLOSSOM|WHEAT_|CARROTS_|POTATOES_|BEETROOTS_|STEM_|MUSHROOM$|FUNGUS|ROOTS|SPROUTS|CORAL_FAN|KELP|SEAGRASS|PROPAGULE|SUGAR_CANE|SWEET_BERRY|COBWEB|FLOWER_POT|DANDELION|POPPY|TULIP|ORCHID|ALLIUM|BLUET|CORNFLOWER|DAISY|LILY_OF|WITHER_ROSE/.test(key)) f &= ~BF_REPLACE;
    if (key === 'TALL_GRASS' || key === 'FERN' || key === 'DEAD_BUSH' || key === 'SEAGRASS') f |= BF_REPLACE;
    FLAGS[id] = f;
    EMIT[id] = r[6] | 0;
    TAB[id] = Math.max(0, TABS.indexOf(r[8]));
    const base = r[7] ? B[r[7]] | 0 : 0;
    BASE[id] = base;
    const isCube = sh === SH.CUBE;
    const clear = (f & (BF_LEAVES | BF_GLASS | BF_TRANS)) !== 0;
    OPAQUE[id] = isCube && !clear ? 1 : 0;
    RLAYER[id] = (f & BF_TRANS) || sh === SH.WATER ? RL_TRANS :
      (isCube && !clear && sh !== SH.CACTUS) ? RL_SOLID : RL_CUTOUT;
    if (sh === SH.LAVA) RLAYER[id] = RL_SOLID;
    // light: cost per step (15 = blocks light); VDIM = sky light is dimmed while passing down (MC opacity 1)
    LCOST[id] = OPAQUE[id] ? 15 : 1;
    VDIM[id] = (f & BF_LEAVES) || sh === SH.WATER || (f & BF_AQUATIC) || key === 'ICE' || key === 'COBWEB' ? 1 : 0;
    if (key === 'TINTED_GLASS' || sh === SH.LAVA) LCOST[id] = 15;
    if (sh === SH.SLAB || sh === SH.STAIRS || sh === SH.FARMLAND || sh === SH.CHEST) LCOST[id] = 1;
    SOLID[id] = (sh === SH.CROSS || sh === SH.TALL || sh === SH.WATER || sh === SH.LAVA || sh === SH.TORCH ||
      sh === SH.VINE || sh === SH.LICHEN || sh === SH.RAIL || sh === SH.FIRE || sh === SH.BUTTON ||
      sh === SH.PLATE || sh === SH.LADDER || sh === SH.WIRE) ? 0 : 1;
    if (key === 'COBWEB' || key === 'LEVER') SOLID[id] = 0;
    COLLIDE[id] = SOLID[id] || sh === SH.LADDER ? 1 : 0;
    // hardness / tool heuristics (survival)
    let h = 1.5, tool = 1;
    if (/ORE$|_ORE|^(COAL|IRON|GOLD|DIAMOND)$/.test(key)) { h = 3; }   // the four first ores are keyed without _ORE
    else if (/OBSIDIAN/.test(key)) h = 50;
    else if (key === 'BEDROCK') h = -1;
    else if (/DIRT|GRASS$|^GRASS|PODZOL|MYCELIUM|FARMLAND|^MUD$|CLAY|SOUL|NYLIUM/.test(key)) { h = 0.6; tool = 3; }
    else if (/SAND$|GRAVEL|POWDER|SNOW/.test(key)) { h = 0.5; tool = 3; }
    else if (/LOG|WOOD|PLANKS|STEM$|HYPHAE|BOOKSHELF|CRAFTING|CHEST|FENCE|GATE|DOOR|TRAPDOOR|LADDER|BAMBOO_BLOCK|OAK|SPRUCE|BIRCH|JUNGLE|ACACIA|CHERRY|MANGROVE|CRIMSON|WARPED/.test(key) && !/STONE|BRICK/.test(key)) { h = 2; tool = 2; }
    else if (f & BF_LEAVES) { h = 0.2; tool = 4; }
    else if (/GLASS|PANE|GLOW|SEA_LANTERN|ICE$/.test(key)) { h = 0.3; tool = 0; }
    else if (/WOOL|CARPET/.test(key)) { h = 0.8; tool = 4; }
    else if (/PUMPKIN|MELON|JACK_O/.test(key)) { h = 1; tool = 2; }
    else if (/DEEPSLATE/.test(key)) { h = 3; }
    else if (key === 'NETHERRACK') { h = 0.4; }
    else if (/BED/.test(key)) { h = 0.2; tool = 0; }
    if (f & BF_PLANT || sh === SH.CROSS || sh === SH.TORCH || sh === SH.FIRE || sh === SH.TALL) { h = 0; tool = 0; }
    if (key === 'COBWEB') { h = 4; tool = 4; }
    if (key === 'SPAWNER') { h = 5; tool = 1; }
    if (/^(BARREL|LECTERN|LOOM|COMPOSTER|FLETCHING_TABLE|CARTOGRAPHY_TABLE|SMITHING_TABLE|CAMPFIRE)$/.test(key)) { h = 2.5; tool = 2; }
    if (/^(BLAST_FURNACE|SMOKER|STONECUTTER)$/.test(key)) { h = 3.5; tool = 1; }
    if (/^(GRINDSTONE|CAULDRON|BELL)$/.test(key)) { h = 2; tool = 1; }
    if (key === 'LANTERN') { h = 3.5; tool = 1; }
    if (key === 'BREWING_STAND') { h = 0.5; tool = 1; }
    if (/^BEETROOTS_/.test(key)) { h = 0; tool = 0; }
    if (/^(SCULK|SCULK_VEIN|PALE_MOSS_BLOCK|PALE_MOSS_CARPET|AZALEA|FLOWERING_AZALEA|BIG_DRIPLEAF|BIG_DRIPLEAF_STEM|SMALL_DRIPLEAF|SPORE_BLOSSOM)$/.test(key)) { h = key === 'SCULK' ? 0.2 : 0.1; tool = 0; }
    if (key === 'SCULK_SENSOR' || key === 'SCULK_CATALYST' || key === 'SCULK_SHRIEKER') { h = 1.5; tool = 0; }
    if (key === 'POINTED_DRIPSTONE') { h = 1.5; tool = 1; }
    if (key === 'CREAKING_HEART') { h = 10; tool = 2; }
    if (key === 'COCOA') { h = 0.2; tool = 2; }
    if (/^(REDSTONE_WIRE|REDSTONE_TORCH|REDSTONE_TORCH_OFF|LEVER|REPEATER|COMPARATOR)$/.test(key)) { h = 0; tool = 0; }
    if (/^REDSTONE_LAMP/.test(key)) { h = 0.3; tool = 0; }
    if (/^(PISTON|STICKY_PISTON|PISTON_HEAD)$/.test(key)) { h = 1.5; tool = 0; }
    if (key === 'OBSERVER') { h = 3; tool = 1; }
    if (key === 'SHIP_WHEEL') { h = 2; tool = 2; }
    if (sh === SH.WATER || sh === SH.LAVA) h = -1;
    HARD[id] = h; TOOL[id] = tool;
  }
})();
// Minecraft's explosion resistance (an explosion's ray loses (resistance + 0.3) x 0.3 in each block
// it crosses): the hardness for most blocks, stone-like building blocks and metal 6, obsidian 1200,
// fluids 100, unbreakable blocks out of reach
const BLAST = new Float32Array(NBX);
(function () {
  const V = { FURNACE: 3.5, FURNACE_LIT: 3.5, BLAST_FURNACE: 3.5, SMOKER: 3.5, STONECUTTER: 3.5, LANTERN: 3.5, BOOKSHELF: 1.5,
    ICE: 0.5, PACKED_ICE: 0.5, BLUE_ICE: 2.8, SNOW: 0.2, SNOW_LAYER: 0.1, DIRT: 0.5, COARSE_DIRT: 0.5, ROOTED_DIRT: 0.5, PODZOL: 0.5,
    MUD: 0.5, DIRT_PATH: 0.65, GRAVEL: 0.6, LADDER: 0.4, RAIL: 0.7, TNT: 0, MAGMA_BLOCK: 0.5, SOUL_SAND: 0.5, SOUL_SOIL: 0.5,
    CRIMSON_NYLIUM: 0.4, WARPED_NYLIUM: 0.4, SHROOMLIGHT: 1, NETHER_WART_BLOCK: 1, WARPED_WART_BLOCK: 1, SPONGE: 0.6, WET_SPONGE: 0.6,
    HAY_BLOCK: 0.5, BONE_BLOCK: 2, DRIED_KELP_BLOCK: 2.5, HONEYCOMB_BLOCK: 0.6, BROWN_MUSHROOM_BLOCK: 0.2, RED_MUSHROOM_BLOCK: 0.2,
    MUSHROOM_STEM: 0.2, MOSS_BLOCK: 0.1, PALE_MOSS_BLOCK: 0.1, CALCITE: 0.75, DRIPSTONE: 1, CACTUS: 0.4, COMPOSTER: 0.6, CAMPFIRE: 2,
    CHEST: 2.5, CRAFTING_TABLE: 2.5, CREAKING_HEART: 10, AMETHYST_BLOCK: 1.5, BUDDING_AMETHYST: 1.5, GLOW: 0.3, SEA_LANTERN: 0.3,
    COCOA: 3, VINE: 0.2, GLOW_LICHEN: 0.2 };
  for (let id = 1; id < NB; id++) {
    const k = B_KEY[id], sh = SHAPE[id];
    let r = HARD[id];
    if (sh === SH.WATER || sh === SH.LAVA) r = 100;
    else if (r < 0) r = 3600000;
    else if (V[k] !== undefined) r = V[k];
    else if (/OBSIDIAN|^NETHERITE_BLOCK$|^ANCIENT_DEBRIS$/.test(k)) r = 1200;
    else if (/PRESSURE_PLATE|BUTTON|^LEVER$/.test(k)) r = 0.5;
    else if (/^IRON_(DOOR|TRAPDOOR)$|^BELL$|^SPAWNER$/.test(k)) r = 5;
    else if (/^END_STONE/.test(k)) r = 9;
    else if (/_ORE$|^(COAL|IRON|GOLD|DIAMOND)$|^LAPIS_BLOCK$|^POINTED_DRIPSTONE$|^OBSERVER$|^SCULK_(CATALYST|SHRIEKER)$|^MUD_BRICKS/.test(k)) r = 3;
    else if (/_GLAZED$/.test(k)) r = 1.4;
    else if (/CONCRETE_POWDER/.test(k)) r = 0.5;
    else if (/CONCRETE$/.test(k)) r = 1.8;
    else if (/BASALT|TERRACOTTA|^TERRA_/.test(k)) r = 4.2;
    else if (/^SMOOTH_(RED_)?SANDSTONE|^SMOOTH_QUARTZ/.test(k)) r = 6;
    else if (/SANDSTONE|QUARTZ/.test(k)) r = 0.8;
    else if (/^(STONE|COBBLE|MOSSY|SMOOTH_STONE|ANDESITE|DIORITE|GRANITE|POLISHED_|BRICK|DEEPSLATE|COBBLED_DEEPSLATE|CHISELED_|CRACKED_|TUFF|BLACKSTONE|GILDED_|PRISMARINE|DARK_PRISMARINE|PURPUR|NETHER_BRICK|RED_NETHER|IRON_BARS|GRINDSTONE)|_WALL$|COPPER|^RAW_.*_BLOCK$|^(IRON|GOLD|DIAMOND|EMERALD|REDSTONE|COAL)_BLOCK$|CORAL_BLOCK$/.test(k)) r = 6;
    else if (/PLANKS|_SLAB$|_STAIRS$|FENCE|_GATE$|_DOOR$|_TRAPDOOR$/.test(k)) r = 3;
    else if (/CARPET$/.test(k)) r = 0.1;
    BLAST[id] = r;
  }
})();

// model variants / selection boxes by block id (filled from models.js)
const MODEL_VAR_ID = [], MODEL_BOX_ID = [];

// waterlogged twins of the shapes Minecraft lets hold water
const WET = new Uint16Array(NB);
(function initWet() {
  const ok = new Set([SH.STAIRS, SH.SLAB, SH.FENCE, SH.PANE, SH.WALL, SH.GATE, SH.TRAPDOOR, SH.LADDER, SH.CHEST, SH.RAIL]);
  for (let id = 1; id < NB; id++) {
    if (!ok.has(SHAPE[id])) continue;
    const t = WET_BASE + id;
    WET[id] = t;
    for (const A of [SHAPE, OPAQUE, RLAYER, LCOST, EMIT, SOLID, COLLIDE, BASE, TAB, HARD, TOOL]) A[t] = A[id];
    FLAGS[t] = FLAGS[id] | BF_WET; VDIM[t] = 1;
    B_KEY[t] = B_KEY[id]; B_EN[t] = B_EN[id]; B_RU[t] = B_RU[id];
  }
})();
// the plain block of a waterlogged twin
function dryId(id) { return id >= WET_BASE ? id - WET_BASE : id; }

// horizontal directions (same order as the main thread's DIRX_W / DIRZ_W: +z, -x, -z, +x)
const HDX = [0, -1, 0, 1], HDZ = [1, 0, -1, 0];
// mob of a spawner (spawner meta)
const SPAWNER_MOB = { pig: 0, zombie: 1, skeleton: 2, spider: 3, cave_spider: 4 };
const SPAWNER_TYPES = ['pig', 'zombie', 'skeleton', 'spider', 'cave_spider'];

// water / aquatic helpers
// concrete powder -> its concrete; log / wood / stem -> its stripped kind (an axe strips it)
const CONCRETE_OF = new Uint16Array(NBX), STRIPPED_OF = new Uint16Array(NBX);
for (let id = 1; id < B_KEY.length; id++) {
  const k = B_KEY[id] || '', c = /^(.+)_CONCRETE_POWDER$/.exec(k);
  if (c && B[c[1] + '_CONCRETE']) CONCRETE_OF[id] = B[c[1] + '_CONCRETE'];
  const s = k === 'LOG' ? 'STRIPPED_OAK_LOG' : k === 'DARK_LOG' ? 'STRIPPED_DARK_OAK_LOG' : /^(?!STRIPPED_).*_(LOG|WOOD|STEM|HYPHAE)$/.test(k) ? 'STRIPPED_' + k : '';
  if (s && B[s] && k !== 'MUSHROOM_STEM' && k !== 'BIG_DRIPLEAF_STEM') STRIPPED_OF[id] = B[s];
}
// ---- redstone wire connections (shared by the mesher and the redstone engine)
// horizontal directions 0 south (+z), 1 west (-x), 2 north (-z), 3 east (+x); 6-way facings add
// 4 up and 5 down
const RS_DX = [0, -1, 0, 1, 0, 0], RS_DY = [0, 0, 0, 0, 1, -1], RS_DZ = [1, 0, -1, 0, 0, 0], RS_OPP = [2, 3, 0, 1, 5, 4];
// a component the wire turns toward when it lies in direction d of the wire
function rsConnectsTo(id, m, d) {
  if (id === B.REPEATER) return ((m & 3) & 1) === (d & 1);
  if (id === B.OBSERVER) return (m & 7) === d;          // its output side faces the wire
  return id === B.REDSTONE_TORCH || id === B.REDSTONE_TORCH_OFF || id === B.LEVER || id === B.REDSTONE_BLOCK ||
    id === B.COMPARATOR || SHAPE[id] === SH.BUTTON || SHAPE[id] === SH.PLATE;
}
// how the wire at the origin links in direction d: 0 no, 1 flat (or down a step), 2 up the side of
// the next block. get(dx, dy, dz) / getM(...) read the cells around it.
function wireLink(get, getM, d) {
  const dx = RS_DX[d], dz = RS_DZ[d], n = get(dx, 0, dz);
  if (n === B.REDSTONE_WIRE || rsConnectsTo(n, getM(dx, 0, dz), d)) return 1;
  if (!OPAQUE[get(0, 1, 0)] && get(dx, 1, dz) === B.REDSTONE_WIRE) return 2;
  if (!OPAQUE[n] && get(dx, -1, dz) === B.REDSTONE_WIRE) return 1;
  return 0;
}
function isJungleLog(id) { return id === B.JUNGLE_LOG || id === B.JUNGLE_WOOD || id === B.STRIPPED_JUNGLE_LOG || id === B.STRIPPED_JUNGLE_WOOD; }
function isWaterId(id) { return id === B.WATER || (FLAGS[id] & (BF_AQUATIC | BF_WET)) !== 0; }
function isLavaId(id) { return id === B.LAVA; }
function isLiquidId(id) { return id === B.WATER || id === B.LAVA || (FLAGS[id] & (BF_AQUATIC | BF_WET)) !== 0; }
// Blocks that flowing water / lava destroys (Minecraft: not solid and unable to hold a fluid):
// replaceable plants, torches, flowers, saplings, crops, double plants, buttons, fire. Ladders,
// doors, sugar cane, cobwebs and underwater plants stay.
function fluidBreaks(id) {
  if (!id) return false;
  const fl = FLAGS[id];
  if (fl & BF_AQUATIC) return false;
  if (SHAPE[id] === SH.LICHEN) return false;   // glow lichen holds water in Minecraft instead of washing away
  if (fl & BF_REPLACE) return true;
  const sh = SHAPE[id];
  if (sh === SH.TORCH || sh === SH.FIRE || sh === SH.BUTTON || sh === SH.TALL) return true;
  return sh === SH.CROSS && id !== B.SUGAR_CANE && id !== B.COBWEB;
}

// Resolve texture names -> layers once the atlas layer map is known.
function resolveBlockTextures(layerMap) {
  const L = n => { const v = layerMap[n]; return v === undefined ? layerMap['white'] | 0 : v; };
  for (let id = 1; id < NB; id++) {
    const r = BLOCK_TABLE[id - 1];
    let src = r[3];
    const base = BASE[id];
    if (!src && base) src = BLOCK_TABLE[base - 1][3];
    const p = src ? src.split(',') : ['white'];
    TEXNAMES[id] = p;
    let top, side, bottom, front;
    const sh = SHAPE[id];
    if (sh === SH.DOOR || sh === SH.TALL) { top = L(p[0]); bottom = L(p[1] || p[0]); side = top; }
    else if (sh === SH.BED) { top = L(p[0]); side = L(p[1] || p[0]); bottom = L('oak_planks'); }
    else if (p.length === 1) { top = side = bottom = L(p[0]); }
    else if (p.length === 2) { top = bottom = L(p[0]); side = L(p[1]); }
    else { top = L(p[0]); side = L(p[1]); bottom = L(p[2]); }
    front = p.length >= 4 ? L(p[3]) : side;
    const o = id * 6;
    FTEX[o] = side; FTEX[o + 1] = side; FTEX[o + 2] = top; FTEX[o + 3] = bottom; FTEX[o + 4] = side; FTEX[o + 5] = side;
    FRONT[id] = front;
    // base textures of derived shapes: slabs/stairs/walls use base faces, fences/gates use base side
    if (base && !r[3]) {
      for (let k = 0; k < 6; k++) FTEX[o + k] = FTEX[base * 6 + k];
      if (sh === SH.FENCE || sh === SH.GATE) for (let k = 0; k < 6; k++) FTEX[o + k] = FTEX[base * 6];
    }
  }
  // block model faces: texture name -> layer (models.js), models by block id
  if (typeof MODEL_LIST !== 'undefined') {
    for (const M of MODEL_LIST) for (const E of M) for (const F of E[3]) if (F) F[8] = L(F[0].split('/').pop());
    for (const k in MODEL_VAR) if (B[k]) { MODEL_VAR_ID[B[k]] = MODEL_VAR[k]; MODEL_BOX_ID[B[k]] = MODEL_BOX[k]; }
  }
  for (let id = 1; id < NB; id++) {
    const t = WET[id];
    if (!t) continue;
    for (let k = 0; k < 6; k++) FTEX[t * 6 + k] = FTEX[id * 6 + k];
    FRONT[t] = FRONT[id]; TEXNAMES[t] = TEXNAMES[id];
  }
}

// ---------------- items (non-block) ----------------
// [key, english, russian, texture, stack, extra]
const ITEM_BASE = 1000;
const ITEM_TABLE = [
  ['STICK', 'Stick', 'Палка', 'item_stick', 64], ['COAL', 'Coal', 'Уголь', 'item_coal', 64],
  ['IRON_INGOT', 'Iron Ingot', 'Железный слиток', 'item_iron_ingot', 64],
  ['GOLD_INGOT', 'Gold Ingot', 'Золотой слиток', 'item_gold_ingot', 64],
  ['COPPER_INGOT', 'Copper Ingot', 'Медный слиток', 'item_copper_ingot', 64],
  ['DIAMOND', 'Diamond', 'Алмаз', 'item_diamond', 64], ['EMERALD', 'Emerald', 'Изумруд', 'item_emerald', 64],
  ['RAW_COPPER', 'Raw Copper', 'Необработанная медь', 'item_raw_copper', 64],
  ['FLINT', 'Flint', 'Кремень', 'item_flint', 64], ['CLAY_BALL', 'Clay Ball', 'Комок глины', 'item_clay_ball', 64],
  ['BRICK_ITEM', 'Brick', 'Кирпич', 'item_brick', 64], ['STRING', 'String', 'Нить', 'item_string', 64],
  ['FEATHER', 'Feather', 'Перо', 'item_feather', 64], ['LEATHER', 'Leather', 'Кожа', 'item_leather', 64],
  ['BONE', 'Bone', 'Кость', 'item_bone', 64], ['BONE_MEAL', 'Bone Meal', 'Костная мука', 'item_bone_meal', 64],
  ['GUNPOWDER', 'Gunpowder', 'Порох', 'item_gunpowder', 64], ['SUGAR', 'Sugar', 'Сахар', 'item_sugar', 64],
  ['WHEAT', 'Wheat', 'Пшеница', 'item_wheat', 64], ['WHEAT_SEEDS', 'Wheat Seeds', 'Семена пшеницы', 'item_wheat_seeds', 64],
  ['PUMPKIN_SEEDS', 'Pumpkin Seeds', 'Семена тыквы', 'item_pumpkin_seeds', 64],
  ['MELON_SEEDS', 'Melon Seeds', 'Семена арбуза', 'item_melon_seeds', 64],
  ['ARROW', 'Arrow', 'Стрела', 'item_arrow', 64], ['BOWL', 'Bowl', 'Миска', 'item_bowl', 64],
  ['APPLE', 'Apple', 'Яблоко', 'item_apple', 64, { food: 4 }], ['BREAD', 'Bread', 'Хлеб', 'item_bread', 64, { food: 5 }],
  ['CARROT', 'Carrot', 'Морковь', 'item_carrot', 64, { food: 3 }], ['POTATO', 'Potato', 'Картофель', 'item_potato', 64, { food: 1 }],
  ['BAKED_POTATO', 'Baked Potato', 'Печёный картофель', 'item_baked_potato', 64, { food: 5 }],
  ['MELON_SLICE', 'Melon Slice', 'Ломтик арбуза', 'item_melon_slice', 64, { food: 2 }],
  ['SWEET_BERRIES', 'Sweet Berries', 'Сладкие ягоды', 'item_sweet_berries', 64, { food: 2 }],
  ['PORKCHOP', 'Raw Porkchop', 'Сырая свинина', 'item_porkchop', 64, { food: 3 }],
  ['COOKED_PORKCHOP', 'Cooked Porkchop', 'Жареная свинина', 'item_cooked_porkchop', 64, { food: 8 }],
  ['BEEF', 'Raw Beef', 'Сырая говядина', 'item_beef', 64, { food: 3 }],
  ['COOKED_BEEF', 'Steak', 'Стейк', 'item_cooked_beef', 64, { food: 8 }],
  ['MUTTON', 'Raw Mutton', 'Сырая баранина', 'item_mutton', 64, { food: 2 }],
  ['COOKED_MUTTON', 'Cooked Mutton', 'Жареная баранина', 'item_cooked_mutton', 64, { food: 6 }],
  ['CHICKEN', 'Raw Chicken', 'Сырая курица', 'item_chicken', 64, { food: 2 }],
  ['COOKED_CHICKEN', 'Cooked Chicken', 'Жареная курица', 'item_cooked_chicken', 64, { food: 6 }],
  ['RAW_COD', 'Raw Cod', 'Сырая треска', 'item_raw_cod', 64, { food: 2 }],
  ['COOKED_COD', 'Cooked Cod', 'Жареная треска', 'item_cooked_cod', 64, { food: 5 }],
  ['RAW_SALMON', 'Raw Salmon', 'Сырой лосось', 'item_raw_salmon', 64, { food: 2 }],
  ['COOKED_SALMON', 'Cooked Salmon', 'Жареный лосось', 'item_cooked_salmon', 64, { food: 6 }],
  ['ROTTEN_FLESH', 'Rotten Flesh', 'Гнилая плоть', 'item_rotten_flesh', 64, { food: 4 }],
  ['SPIDER_EYE', 'Spider Eye', 'Паучий глаз', 'item_spider_eye', 64, { food: 2 }],
  ['GOLDEN_APPLE', 'Golden Apple', 'Золотое яблоко', 'item_golden_apple', 64, { food: 4 }],
  ['GOLDEN_CARROT', 'Golden Carrot', 'Золотая морковь', 'item_golden_carrot', 64, { food: 6 }],
  ['MUSHROOM_STEW', 'Mushroom Stew', 'Грибной суп', 'item_mushroom_stew', 1, { food: 6 }],
  ['BUCKET', 'Bucket', 'Ведро', 'item_bucket', 16],
  ['WATER_BUCKET', 'Water Bucket', 'Ведро воды', 'item_water_bucket', 1],
  ['LAVA_BUCKET', 'Lava Bucket', 'Ведро лавы', 'item_lava_bucket', 1],
  ['SHEARS', 'Shears', 'Ножницы', 'item_shears', 1, { tool: 'shears', tier: 3 }],
  ['BOW', 'Bow', 'Лук', 'item_bow', 1],
  ['FLINT_AND_STEEL', 'Flint and Steel', 'Огниво', 'flint_and_steel', 1],
  ['SPAWN_EGG_PIG', 'Pig Spawn Egg', 'Яйцо призыва свиньи', 'item_spawn_egg_pig', 64, { mob: 'pig' }],
  ['SPAWN_EGG_COW', 'Cow Spawn Egg', 'Яйцо призыва коровы', 'item_spawn_egg_cow', 64, { mob: 'cow' }],
  ['SPAWN_EGG_SHEEP', 'Sheep Spawn Egg', 'Яйцо призыва овцы', 'item_spawn_egg_sheep', 64, { mob: 'sheep' }],
  ['SPAWN_EGG_CHICKEN', 'Chicken Spawn Egg', 'Яйцо призыва курицы', 'item_spawn_egg_chicken', 64, { mob: 'chicken' }],
  ['SPAWN_EGG_ZOMBIE', 'Zombie Spawn Egg', 'Яйцо призыва зомби', 'item_spawn_egg_zombie', 64, { mob: 'zombie' }],
  ['SPAWN_EGG_SKELETON', 'Skeleton Spawn Egg', 'Яйцо призыва скелета', 'item_spawn_egg_skeleton', 64, { mob: 'skeleton' }],
  ['SPAWN_EGG_CREEPER', 'Creeper Spawn Egg', 'Яйцо призыва крипера', 'item_spawn_egg_creeper', 64, { mob: 'creeper' }],
  ['SPAWN_EGG_SPIDER', 'Spider Spawn Egg', 'Яйцо призыва паука', 'item_spawn_egg_spider', 64, { mob: 'spider' }],
];
for (const [mat, en, ru, tier] of [['wooden', 'Wooden', 'Деревянн', 1], ['stone', 'Stone', 'Каменн', 2],
  ['iron', 'Iron', 'Железн', 3], ['golden', 'Golden', 'Золот', 4], ['diamond', 'Diamond', 'Алмазн', 5]]) {
  ITEM_TABLE.push([mat.toUpperCase() + '_PICKAXE', en + ' Pickaxe', ru + 'ая кирка', 'item_' + mat + '_pickaxe', 1, { tool: 'pickaxe', tier }]);
  ITEM_TABLE.push([mat.toUpperCase() + '_AXE', en + ' Axe', ru + 'ый топор', 'item_' + mat + '_axe', 1, { tool: 'axe', tier }]);
  ITEM_TABLE.push([mat.toUpperCase() + '_SHOVEL', en + ' Shovel', ru + 'ая лопата', 'item_' + mat + '_shovel', 1, { tool: 'shovel', tier }]);
  ITEM_TABLE.push([mat.toUpperCase() + '_SWORD', en + ' Sword', ru + 'ый меч', 'item_' + mat + '_sword', 1, { tool: 'sword', tier }]);
  ITEM_TABLE.push([mat.toUpperCase() + '_HOE', en + ' Hoe', ru + 'ая мотыга', 'item_' + mat + '_hoe', 1, { tool: 'hoe', tier }]);
}
// appended later (keeps the ids of older items stable for saved worlds)
ITEM_TABLE.push(['ENDER_PEARL', 'Ender Pearl', 'Жемчуг Эндера', 'item_ender_pearl', 16]);
ITEM_TABLE.push(['SLIME_BALL', 'Slimeball', 'Слизь', 'item_slime_ball', 64]);
ITEM_TABLE.push(['SPAWN_EGG_ENDERMAN', 'Enderman Spawn Egg', 'Яйцо призыва эндермена', 'item_spawn_egg_enderman', 64, { mob: 'enderman' }]);
ITEM_TABLE.push(['SPAWN_EGG_SLIME', 'Slime Spawn Egg', 'Яйцо призыва слизня', 'item_spawn_egg_slime', 64, { mob: 'slime' }]);
ITEM_TABLE.push(['SPAWN_EGG_SALMON', 'Salmon Spawn Egg', 'Яйцо призыва лосося', 'item_spawn_egg_salmon', 64, { mob: 'salmon' }]);
ITEM_TABLE.push(['SPAWN_EGG_SHARK', 'Shark Spawn Egg', 'Яйцо призыва акулы', 'item_spawn_egg_shark', 64, { mob: 'shark' }]);
ITEM_TABLE.push(['SPAWN_EGG_DROWNED', 'Drowned Spawn Egg', 'Яйцо призыва утопленника', 'item_spawn_egg_drowned', 64, { mob: 'drowned' }]);
ITEM_TABLE.push(['BEETROOT', 'Beetroot', 'Свёкла', 'item_beetroot', 64, { food: 1 }]);
ITEM_TABLE.push(['BEETROOT_SEEDS', 'Beetroot Seeds', 'Семена свёклы', 'item_beetroot_seeds', 64]);
// the 16 dyes (Minecraft's colour order), nuggets, charcoal, paper, book
const DYE_COLORS = [['WHITE', 'White', 'Белый'], ['ORANGE', 'Orange', 'Оранжевый'], ['MAGENTA', 'Magenta', 'Пурпурный'],
  ['LIGHT_BLUE', 'Light Blue', 'Голубой'], ['YELLOW', 'Yellow', 'Жёлтый'], ['LIME', 'Lime', 'Лаймовый'], ['PINK', 'Pink', 'Розовый'],
  ['GRAY', 'Gray', 'Серый'], ['LIGHT_GRAY', 'Light Gray', 'Светло-серый'], ['CYAN', 'Cyan', 'Бирюзовый'], ['PURPLE', 'Purple', 'Фиолетовый'],
  ['BLUE', 'Blue', 'Синий'], ['BROWN', 'Brown', 'Коричневый'], ['GREEN', 'Green', 'Зелёный'], ['RED', 'Red', 'Красный'], ['BLACK', 'Black', 'Чёрный']];
for (const [k, en, ru] of DYE_COLORS) ITEM_TABLE.push([k + '_DYE', en + ' Dye', ru + ' краситель', 'item_' + k.toLowerCase() + '_dye', 64]);
ITEM_TABLE.push(['GOLD_NUGGET', 'Gold Nugget', 'Золотой самородок', 'item_gold_nugget', 64]);
ITEM_TABLE.push(['IRON_NUGGET', 'Iron Nugget', 'Железный самородок', 'item_iron_nugget', 64]);
ITEM_TABLE.push(['CHARCOAL', 'Charcoal', 'Древесный уголь', 'item_charcoal', 64]);
ITEM_TABLE.push(['PAPER', 'Paper', 'Бумага', 'item_paper', 64]);
ITEM_TABLE.push(['BOOK', 'Book', 'Книга', 'item_book', 64]);
ITEM_TABLE.push(['COCOA_BEANS', 'Cocoa Beans', 'Какао-бобы', 'item_cocoa_beans', 64]);
ITEM_TABLE.push(['REDSTONE', 'Redstone Dust', 'Красная пыль', 'item_redstone', 64]);
ITEM_TABLE.push(['BOAT', 'Boat', 'Лодка', 'item_boat', 1]);
const IT = {};
for (let i = 0; i < ITEM_TABLE.length; i++) IT[ITEM_TABLE[i][0]] = ITEM_BASE + i;
function isItem(id) { return id >= ITEM_BASE && id < WET_BASE; }
function itemDef(id) { return ITEM_TABLE[id - ITEM_BASE]; }
