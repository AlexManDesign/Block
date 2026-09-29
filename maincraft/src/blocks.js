'use strict';
// Shared block registry: turns BLOCK_TABLE into flat typed arrays for hot loops.
// Used by the main thread and by the workers (generator + mesher).

const WORLD_MIN_Y = -64, WORLD_H = 384, WORLD_MAX_Y = WORLD_MIN_Y + WORLD_H, SECTIONS = 24, SEA = 63;

// shapes
const SH = {
  AIR: 0, CUBE: 1, CROSS: 2, WATER: 3, LAVA: 4, SLAB: 5, STAIRS: 6, FENCE: 7, GATE: 8, WALL: 9,
  PANE: 10, DOOR: 11, TRAPDOOR: 12, LADDER: 13, TORCH: 14, CARPET: 15, PLATE: 16, BUTTON: 17,
  TALL: 18, VINE: 19, LICHEN: 20, LILY: 21, PICKLE: 22, BAMBOO: 23, POT: 24, RAIL: 25, CACTUS: 26,
  CHEST: 27, BED: 28, FARMLAND: 29, FIRE: 30, SNOWLAYER: 31,
};
const SHAPE_BY_NAME = {
  '': SH.CUBE, x: SH.CROSS, water: SH.WATER, lava: SH.LAVA, slab: SH.SLAB, stairs: SH.STAIRS,
  fence: SH.FENCE, gate: SH.GATE, wall: SH.WALL, pane: SH.PANE, door: SH.DOOR, trapdoor: SH.TRAPDOOR,
  ladder: SH.LADDER, torch: SH.TORCH, carpet: SH.CARPET, plate: SH.PLATE, button: SH.BUTTON,
  tallplant: SH.TALL, vine: SH.VINE, lichen: SH.LICHEN, lilypad: SH.LILY, seapickle: SH.PICKLE,
  bamboo: SH.BAMBOO, pot: SH.POT, rail: SH.RAIL, cactus: SH.CACTUS, chest: SH.CHEST, bed: SH.BED,
  farmland: SH.FARMLAND, fire: SH.FIRE, snowlayer: SH.SNOWLAYER,
};

// render layers
const RL_SOLID = 0, RL_CUTOUT = 1, RL_TRANS = 2;
// block flag bits
const BF_LEAVES = 1, BF_AXIS = 2, BF_GLASS = 4, BF_TRANS = 8, BF_FALL = 16, BF_EMISSIVE = 32,
  BF_AQUATIC = 64, BF_NEEDWATER = 128, BF_HANG = 256, BF_CLIMB = 512, BF_STACK = 1024,
  BF_FACING = 2048, BF_REPLACE = 4096, BF_PLANT = 8192;

const NB = BLOCK_TABLE.length + 1;
const B = { AIR: 0 };
const B_KEY = ['AIR'], B_EN = ['Air'], B_RU = ['Воздух'];
const SHAPE = new Uint8Array(NB), OPAQUE = new Uint8Array(NB), RLAYER = new Uint8Array(NB);
const LCOST = new Uint8Array(NB), EMIT = new Uint8Array(NB), SOLID = new Uint8Array(NB);
const FLAGS = new Uint16Array(NB), BASE = new Uint16Array(NB), TAB = new Uint8Array(NB);
const HARD = new Float32Array(NB), TOOL = new Uint8Array(NB);
// face textures (layer index): 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z ; FRONT = facing face texture
const FTEX = new Uint16Array(NB * 6), FRONT = new Uint16Array(NB);
const TEXNAMES = new Array(NB); // raw texture names per block (for icons / worker tex resolve)
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
        sh === SH.LICHEN || sh === SH.FIRE || key === 'SNOWLAYER') f |= BF_REPLACE;
    if (/SAPLING|WHEAT_|CARROTS_|POTATOES_|STEM_|MUSHROOM$|FUNGUS|ROOTS|SPROUTS|CORAL_FAN|KELP|SEAGRASS|SUGAR_CANE|SWEET_BERRY|COBWEB|FLOWER_POT|DANDELION|POPPY|TULIP|ORCHID|ALLIUM|BLUET|CORNFLOWER|DAISY|LILY_OF|WITHER_ROSE/.test(key)) f &= ~BF_REPLACE;
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
    LCOST[id] = OPAQUE[id] ? 15 : (f & BF_LEAVES) || sh === SH.WATER || (f & BF_AQUATIC) || key === 'ICE' ? 2 : 1;
    if (key === 'TINTED_GLASS' || sh === SH.LAVA) LCOST[id] = 15;
    if (sh === SH.SLAB || sh === SH.STAIRS || sh === SH.FARMLAND || sh === SH.CHEST) LCOST[id] = 1;
    SOLID[id] = (sh === SH.CROSS || sh === SH.TALL || sh === SH.WATER || sh === SH.LAVA || sh === SH.TORCH ||
      sh === SH.VINE || sh === SH.LICHEN || sh === SH.RAIL || sh === SH.FIRE || sh === SH.BUTTON ||
      sh === SH.PLATE || sh === SH.LADDER) ? 0 : 1;
    if (key === 'COBWEB') SOLID[id] = 0;
    // hardness / tool heuristics (survival)
    let h = 1.5, tool = 1;
    if (/ORE$|_ORE/.test(key)) { h = 3; }
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
    if (sh === SH.WATER || sh === SH.LAVA) h = -1;
    HARD[id] = h; TOOL[id] = tool;
  }
})();

// water / aquatic helpers
function isWaterId(id) { return id === B.WATER || (FLAGS[id] & BF_AQUATIC) !== 0; }
function isLavaId(id) { return id === B.LAVA; }
function isLiquidId(id) { return id === B.WATER || id === B.LAVA || (FLAGS[id] & BF_AQUATIC) !== 0; }

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
];
for (const [mat, en, ru, tier] of [['wooden', 'Wooden', 'Деревянн', 1], ['stone', 'Stone', 'Каменн', 2],
  ['iron', 'Iron', 'Железн', 3], ['golden', 'Golden', 'Золот', 4], ['diamond', 'Diamond', 'Алмазн', 5]]) {
  ITEM_TABLE.push([mat.toUpperCase() + '_PICKAXE', en + ' Pickaxe', ru + 'ая кирка', 'item_' + mat + '_pickaxe', 1, { tool: 'pickaxe', tier }]);
  ITEM_TABLE.push([mat.toUpperCase() + '_AXE', en + ' Axe', ru + 'ый топор', 'item_' + mat + '_axe', 1, { tool: 'axe', tier }]);
  ITEM_TABLE.push([mat.toUpperCase() + '_SHOVEL', en + ' Shovel', ru + 'ая лопата', 'item_' + mat + '_shovel', 1, { tool: 'shovel', tier }]);
  ITEM_TABLE.push([mat.toUpperCase() + '_SWORD', en + ' Sword', ru + 'ый меч', 'item_' + mat + '_sword', 1, { tool: 'sword', tier }]);
  ITEM_TABLE.push([mat.toUpperCase() + '_HOE', en + ' Hoe', ru + 'ая мотыга', 'item_' + mat + '_hoe', 1, { tool: 'hoe', tier }]);
}
const IT = {};
for (let i = 0; i < ITEM_TABLE.length; i++) IT[ITEM_TABLE[i][0]] = ITEM_BASE + i;
function isItem(id) { return id >= ITEM_BASE; }
function itemDef(id) { return ITEM_TABLE[id - ITEM_BASE]; }
