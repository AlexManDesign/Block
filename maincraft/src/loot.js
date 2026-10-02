'use strict';
// Chest loot of this game's structures (own tables, written by hand).
// A table is a list of pools [min rolls, max rolls, entries]; an entry is [item or block key, weight,
// min count, max count], an empty key rolls nothing. LOOT names a table by the index chests store.
const LOOT_TABLES = {
  // food and odds a careless explorer left behind
  SIMPLE_DUNGEON: [
    [1, 3, [['GOLDEN_APPLE', 4, 1, 1], ['IRON_INGOT', 10, 1, 4], ['GOLD_INGOT', 6, 1, 3], ['BREAD', 14, 1, 3], ['BUCKET', 6, 1, 1], ['', 10, 1, 1]]],
    [2, 5, [['BONE', 12, 1, 8], ['ROTTEN_FLESH', 12, 1, 8], ['STRING', 10, 1, 6], ['GUNPOWDER', 10, 1, 6], ['WHEAT', 8, 1, 4], ['COAL', 8, 2, 6],
      ['MELON_SEEDS', 4, 2, 4], ['PUMPKIN_SEEDS', 4, 2, 4], ['BEETROOT_SEEDS', 4, 2, 4]]],
  ],
  ABANDONED_MINESHAFT: [
    [1, 2, [['IRON_PICKAXE', 4, 1, 1], ['DIAMOND', 3, 1, 2], ['GOLD_INGOT', 6, 1, 3], ['IRON_INGOT', 10, 1, 5], ['GOLDEN_APPLE', 3, 1, 1], ['', 8, 1, 1]]],
    [2, 4, [['RAIL', 14, 4, 8], ['TORCH', 12, 2, 10], ['COAL', 12, 3, 8], ['BREAD', 10, 1, 3], ['RAW_COPPER', 8, 2, 6], ['LADDER', 6, 2, 6],
      ['MELON_SEEDS', 4, 2, 4], ['PUMPKIN_SEEDS', 4, 2, 4]]],
  ],
  DESERT_PYRAMID: [
    [2, 4, [['GOLD_INGOT', 14, 2, 6], ['EMERALD', 10, 1, 3], ['DIAMOND', 4, 1, 2], ['IRON_INGOT', 10, 1, 5], ['GOLDEN_APPLE', 6, 1, 1], ['GOLDEN_CARROT', 4, 1, 3]]],
    [3, 5, [['BONE', 12, 2, 6], ['ROTTEN_FLESH', 12, 2, 6], ['SPIDER_EYE', 8, 1, 3], ['STRING', 8, 1, 4], ['GUNPOWDER', 8, 1, 5], ['SAND', 10, 2, 8], ['LEATHER', 6, 1, 3]]],
  ],
  JUNGLE_TEMPLE: [
    [2, 5, [['GOLD_INGOT', 12, 2, 6], ['EMERALD', 6, 1, 3], ['DIAMOND', 3, 1, 2], ['IRON_INGOT', 10, 1, 4], ['BONE', 14, 2, 6], ['ROTTEN_FLESH', 12, 2, 6],
      ['BAMBOO_PLANT', 10, 2, 8], ['ARROW', 8, 2, 8]]],
  ],
  // ship's stores: food, paper, wood and something to burn
  SHIPWRECK_SUPPLY: [
    [3, 8, [['BREAD', 10, 1, 4], ['POTATO', 8, 2, 6], ['CARROT', 8, 2, 6], ['WHEAT', 8, 4, 10], ['COAL', 8, 2, 8], ['ROTTEN_FLESH', 6, 3, 10],
      ['PUMPKIN', 3, 1, 1], ['GUNPOWDER', 4, 1, 4], ['TNT', 1, 1, 2], ['SUGAR', 8, 2, 10], ['STRING', 4, 1, 4], ['LEATHER', 4, 1, 3],
      ['MOSS_BLOCK', 4, 1, 3], ['SPRUCE_LOG', 4, 2, 6], ['', 8, 1, 1]]],
  ],
  SHIPWRECK_TREASURE: [
    [3, 6, [['IRON_INGOT', 14, 2, 6], ['GOLD_INGOT', 10, 1, 5], ['EMERALD', 8, 1, 5], ['DIAMOND', 2, 1, 1], ['COPPER_INGOT', 8, 2, 6], ['RAW_COPPER', 6, 2, 6]]],
    [1, 3, [['FLINT_AND_STEEL', 2, 1, 1], ['ENDER_PEARL', 3, 1, 1], ['ARROW', 6, 2, 8], ['', 6, 1, 1]]],
  ],
  SHIPWRECK_MAP: [
    [1, 1, [['SUGAR_CANE', 1, 2, 6]]],
    [3, 5, [['FEATHER', 10, 1, 5], ['BOOKSHELF', 1, 1, 1], ['SUGAR_CANE', 14, 1, 10], ['GOLD_INGOT', 2, 1, 2], ['EMERALD', 4, 1, 2], ['', 6, 1, 1]]],
  ],
  UNDERWATER_RUIN_SMALL: [
    [2, 6, [['COAL', 12, 1, 4], ['STONE_AXE', 3, 1, 1], ['ROTTEN_FLESH', 6, 1, 3], ['EMERALD', 3, 1, 1], ['WHEAT', 10, 2, 4], ['RAW_COD', 8, 1, 3],
      ['KELP', 8, 2, 6], ['', 6, 1, 1]]],
    [0, 1, [['GOLDEN_APPLE', 1, 1, 1], ['', 9, 1, 1]]],
  ],
  UNDERWATER_RUIN_BIG: [
    [2, 8, [['COAL', 12, 1, 4], ['GOLD_INGOT', 4, 1, 2], ['EMERALD', 4, 1, 2], ['WHEAT', 10, 2, 4], ['IRON_INGOT', 4, 1, 2], ['RAW_SALMON', 6, 1, 3],
      ['KELP', 6, 2, 6], ['GOLDEN_APPLE', 2, 1, 1]]],
    [1, 1, [['DIAMOND', 1, 1, 1], ['GOLD_INGOT', 3, 1, 3], ['', 6, 1, 1]]],
  ],
  // the basement of a snow hut: someone's supplies and a precious apple
  IGLOO: [
    [2, 6, [['APPLE', 14, 1, 3], ['COAL', 14, 1, 4], ['GOLD_INGOT', 4, 1, 1], ['STONE_AXE', 2, 1, 1], ['ROTTEN_FLESH', 10, 1, 1], ['EMERALD', 2, 1, 1],
      ['WHEAT', 10, 2, 3], ['BREAD', 6, 1, 2], ['SNOW', 6, 1, 3]]],
    [1, 1, [['GOLDEN_APPLE', 1, 1, 1]]],
  ],
  PILLAGER_OUTPOST: [
    [0, 1, [['BOW', 3, 1, 1], ['', 2, 1, 1]]],
    [2, 3, [['WHEAT', 8, 3, 5], ['POTATO', 6, 2, 5], ['CARROT', 6, 2, 5], ['ARROW', 6, 2, 10], ['STRING', 4, 1, 4]]],
    [1, 3, [['DARK_LOG', 1, 2, 3], ['IRON_INGOT', 2, 1, 3], ['EMERALD', 1, 1, 2], ['GUNPOWDER', 1, 1, 3]]],
  ],
  // village houses: what each trade keeps at home
  VILLAGE_ARMORER: [[1, 4, [['IRON_INGOT', 4, 1, 3], ['BREAD', 6, 1, 4], ['COAL', 4, 1, 3], ['EMERALD', 1, 1, 1], ['', 2, 1, 1]]]],
  VILLAGE_BUTCHER: [[1, 4, [['EMERALD', 1, 1, 1], ['PORKCHOP', 6, 1, 3], ['BEEF', 6, 1, 3], ['MUTTON', 6, 1, 3], ['CHICKEN', 4, 1, 3], ['COAL', 3, 1, 3]]]],
  VILLAGE_CARTOGRAPHER: [[1, 5, [['SUGAR_CANE', 15, 1, 6], ['FLINT_AND_STEEL', 2, 1, 1], ['BREAD', 6, 1, 3], ['STICK', 5, 1, 2], ['FEATHER', 4, 1, 3]]]],
  VILLAGE_FISHER: [[1, 4, [['EMERALD', 1, 1, 1], ['RAW_COD', 4, 1, 4], ['RAW_SALMON', 3, 1, 3], ['WATER_BUCKET', 1, 1, 1], ['BARREL', 1, 1, 1], ['WHEAT_SEEDS', 3, 1, 4], ['COAL', 2, 1, 3]]]],
  VILLAGE_FLETCHER: [[1, 5, [['EMERALD', 1, 1, 1], ['ARROW', 3, 1, 6], ['FEATHER', 6, 1, 4], ['STRING', 4, 1, 4], ['FLINT', 6, 1, 4], ['STICK', 6, 1, 4]]]],
  VILLAGE_MASON: [[1, 5, [['CLAY_BALL', 2, 1, 4], ['STONE', 4, 1, 3], ['STONE_BRICKS', 4, 1, 3], ['BREAD', 4, 1, 3], ['TERRACOTTA', 2, 1, 2], ['EMERALD', 1, 1, 1]]]],
  VILLAGE_SHEPHERD: [[1, 5, [['WOOL_WHITE', 8, 1, 6], ['WOOL_BLACK', 3, 1, 3], ['WOOL_GRAY', 3, 1, 3], ['WOOL_BROWN', 3, 1, 3], ['SHEARS', 1, 1, 1], ['WHEAT', 6, 1, 6], ['EMERALD', 1, 1, 1]]]],
  VILLAGE_TANNERY: [[1, 5, [['LEATHER', 6, 1, 3], ['BREAD', 4, 1, 4], ['STRING', 3, 1, 2], ['EMERALD', 1, 1, 2], ['', 2, 1, 1]]]],
  VILLAGE_TEMPLE: [[3, 8, [['REDSTONE_TORCH', 4, 1, 4], ['BREAD', 7, 1, 4], ['ROTTEN_FLESH', 7, 1, 4], ['LAPIS_BLOCK', 1, 1, 1], ['GOLD_INGOT', 2, 1, 4], ['EMERALD', 1, 1, 4], ['TORCH', 3, 1, 3]]]],
  VILLAGE_TOOLSMITH: [[3, 8, [['DIAMOND', 1, 1, 3], ['IRON_INGOT', 6, 1, 5], ['GOLD_INGOT', 2, 1, 3], ['BREAD', 12, 1, 3], ['IRON_PICKAXE', 2, 1, 1], ['COAL', 2, 1, 3], ['STICK', 10, 1, 3], ['IRON_SHOVEL', 4, 1, 1]]]],
  VILLAGE_WEAPONSMITH: [[3, 8, [['DIAMOND', 2, 1, 3], ['IRON_INGOT', 6, 1, 5], ['GOLD_INGOT', 3, 1, 3], ['BREAD', 12, 1, 3], ['APPLE', 12, 1, 3], ['IRON_SWORD', 3, 1, 1], ['OBSIDIAN', 4, 3, 6], ['OAK_SAPLING', 4, 3, 6]]]],
  VILLAGE_PLAINS_HOUSE: [[3, 8, [['GOLD_INGOT', 1, 1, 1], ['DANDELION', 2, 1, 1], ['POPPY', 1, 1, 1], ['POTATO', 8, 1, 6], ['BREAD', 12, 1, 4], ['APPLE', 8, 1, 5], ['BOOKSHELF', 1, 1, 1], ['FEATHER', 1, 1, 1], ['EMERALD', 2, 1, 4], ['OAK_SAPLING', 4, 1, 2]]]],
  VILLAGE_DESERT_HOUSE: [[3, 8, [['CLAY_BALL', 1, 1, 1], ['CACTUS', 10, 1, 4], ['WHEAT', 10, 1, 7], ['BREAD', 10, 1, 4], ['BOOKSHELF', 1, 1, 1], ['DEAD_BUSH', 2, 1, 3], ['EMERALD', 1, 1, 3]]]],
  VILLAGE_SAVANNA_HOUSE: [[3, 8, [['GOLD_INGOT', 1, 1, 1], ['TALL_GRASS', 5, 1, 1], ['BREAD', 10, 1, 4], ['WHEAT_SEEDS', 10, 1, 5], ['EMERALD', 2, 1, 4], ['ACACIA_SAPLING', 10, 1, 2], ['TORCH', 1, 1, 2], ['BUCKET', 1, 1, 1]]]],
  VILLAGE_SNOWY_HOUSE: [[3, 8, [['BLUE_ICE', 1, 1, 1], ['SNOW', 4, 1, 1], ['POTATO', 2, 1, 7], ['BREAD', 4, 1, 4], ['BEETROOT_SEEDS', 5, 1, 5], ['BEETROOT', 5, 1, 5], ['FURNACE', 1, 1, 1], ['EMERALD', 1, 1, 4], ['SNOW', 10, 1, 3], ['COAL', 5, 1, 4]]]],
  VILLAGE_TAIGA_HOUSE: [[3, 8, [['IRON_INGOT', 1, 1, 2], ['FERN', 2, 1, 1], ['LARGE_FERN', 2, 1, 1], ['POTATO', 10, 1, 7], ['SWEET_BERRIES', 5, 1, 7], ['BREAD', 6, 1, 4], ['PUMPKIN_SEEDS', 5, 1, 5], ['PUMPKIN', 1, 1, 1], ['EMERALD', 1, 1, 4], ['SPRUCE_SAPLING', 5, 1, 5], ['SPRUCE_LOG', 10, 1, 5]]]],
};
const LOOT = {}, LOOT_DATA = [null];
for (const k in LOOT_TABLES) { LOOT[k] = LOOT_DATA.length; LOOT_DATA.push(LOOT_TABLES[k]); }
