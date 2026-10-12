// Port of loot.js: chest loot of this game's structures (own tables, written by hand).
// A table is a list of pools [min rolls, max rolls, entries]; an entry is [item or block key, weight,
// min count, max count], an empty key rolls nothing. LOOT names a table by the index chests store.
using System.Collections.Generic;

namespace Maincraft
{
    public struct LootEntry
    {
        public string Key; public int Weight, Min, Max;
        public LootEntry(string key, int weight, int min, int max) { Key = key; Weight = weight; Min = min; Max = max; }
    }

    public sealed class LootPool
    {
        public int MinRolls, MaxRolls;
        public LootEntry[] Entries;
        public LootPool(int min, int max, params LootEntry[] entries) { MinRolls = min; MaxRolls = max; Entries = entries; }
    }

    public static class Loot
    {
        public static readonly Dictionary<string, int> LOOT = new Dictionary<string, int>();
        public static readonly List<LootPool[]> LOOT_DATA = new List<LootPool[]> { null };

        static LootEntry E(string k, int w, int a, int b) => new LootEntry(k, w, a, b);
        static LootPool P(int a, int b, params LootEntry[] e) => new LootPool(a, b, e);
        static void T(string name, params LootPool[] pools) { LOOT[name] = LOOT_DATA.Count; LOOT_DATA.Add(pools); }

        static bool _inited;

        public static int Get(string name) => LOOT.TryGetValue(name, out var v) ? v : 0;

        public static void Init()
        {
            if (_inited) return;
            _inited = true;
            // food and odds a careless explorer left behind
            T("SIMPLE_DUNGEON",
                P(1, 3, E("GOLDEN_APPLE", 4, 1, 1), E("IRON_INGOT", 10, 1, 4), E("GOLD_INGOT", 6, 1, 3), E("BREAD", 14, 1, 3), E("BUCKET", 6, 1, 1), E("", 10, 1, 1)),
                P(2, 5, E("BONE", 12, 1, 8), E("ROTTEN_FLESH", 12, 1, 8), E("STRING", 10, 1, 6), E("GUNPOWDER", 10, 1, 6), E("WHEAT", 8, 1, 4), E("COAL", 8, 2, 6),
                    E("MELON_SEEDS", 4, 2, 4), E("PUMPKIN_SEEDS", 4, 2, 4), E("BEETROOT_SEEDS", 4, 2, 4)));
            T("ABANDONED_MINESHAFT",
                P(1, 2, E("IRON_PICKAXE", 4, 1, 1), E("DIAMOND", 3, 1, 2), E("GOLD_INGOT", 6, 1, 3), E("IRON_INGOT", 10, 1, 5), E("GOLDEN_APPLE", 3, 1, 1), E("", 8, 1, 1)),
                P(2, 4, E("RAIL", 14, 4, 8), E("TORCH", 12, 2, 10), E("COAL", 12, 3, 8), E("BREAD", 10, 1, 3), E("RAW_COPPER", 8, 2, 6), E("LADDER", 6, 2, 6),
                    E("MELON_SEEDS", 4, 2, 4), E("PUMPKIN_SEEDS", 4, 2, 4)));
            T("DESERT_PYRAMID",
                P(2, 4, E("GOLD_INGOT", 14, 2, 6), E("EMERALD", 10, 1, 3), E("DIAMOND", 4, 1, 2), E("IRON_INGOT", 10, 1, 5), E("GOLDEN_APPLE", 6, 1, 1), E("GOLDEN_CARROT", 4, 1, 3)),
                P(3, 5, E("BONE", 12, 2, 6), E("ROTTEN_FLESH", 12, 2, 6), E("SPIDER_EYE", 8, 1, 3), E("STRING", 8, 1, 4), E("GUNPOWDER", 8, 1, 5), E("SAND", 10, 2, 8), E("LEATHER", 6, 1, 3)));
            T("JUNGLE_TEMPLE",
                P(2, 5, E("GOLD_INGOT", 12, 2, 6), E("EMERALD", 6, 1, 3), E("DIAMOND", 3, 1, 2), E("IRON_INGOT", 10, 1, 4), E("BONE", 14, 2, 6), E("ROTTEN_FLESH", 12, 2, 6),
                    E("BAMBOO_PLANT", 10, 2, 8), E("ARROW", 8, 2, 8)));
            // ship's stores: food, paper, wood and something to burn
            T("SHIPWRECK_SUPPLY",
                P(3, 8, E("BREAD", 10, 1, 4), E("POTATO", 8, 2, 6), E("CARROT", 8, 2, 6), E("WHEAT", 8, 4, 10), E("COAL", 8, 2, 8), E("ROTTEN_FLESH", 6, 3, 10),
                    E("PUMPKIN", 3, 1, 1), E("GUNPOWDER", 4, 1, 4), E("TNT", 1, 1, 2), E("SUGAR", 8, 2, 10), E("STRING", 4, 1, 4), E("LEATHER", 4, 1, 3),
                    E("MOSS_BLOCK", 4, 1, 3), E("SPRUCE_LOG", 4, 2, 6), E("", 8, 1, 1)));
            T("SHIPWRECK_TREASURE",
                P(3, 6, E("IRON_INGOT", 14, 2, 6), E("GOLD_INGOT", 10, 1, 5), E("EMERALD", 8, 1, 5), E("DIAMOND", 2, 1, 1), E("COPPER_INGOT", 8, 2, 6), E("RAW_COPPER", 6, 2, 6)),
                P(1, 3, E("FLINT_AND_STEEL", 2, 1, 1), E("ENDER_PEARL", 3, 1, 1), E("ARROW", 6, 2, 8), E("", 6, 1, 1)));
            T("SHIPWRECK_MAP",
                P(1, 1, E("SUGAR_CANE", 1, 2, 6)),
                P(3, 5, E("FEATHER", 10, 1, 5), E("BOOKSHELF", 1, 1, 1), E("SUGAR_CANE", 14, 1, 10), E("GOLD_INGOT", 2, 1, 2), E("EMERALD", 4, 1, 2), E("", 6, 1, 1)));
            T("UNDERWATER_RUIN_SMALL",
                P(2, 6, E("COAL", 12, 1, 4), E("STONE_AXE", 3, 1, 1), E("ROTTEN_FLESH", 6, 1, 3), E("EMERALD", 3, 1, 1), E("WHEAT", 10, 2, 4), E("RAW_COD", 8, 1, 3),
                    E("KELP", 8, 2, 6), E("", 6, 1, 1)),
                P(0, 1, E("GOLDEN_APPLE", 1, 1, 1), E("", 9, 1, 1)));
            T("UNDERWATER_RUIN_BIG",
                P(2, 8, E("COAL", 12, 1, 4), E("GOLD_INGOT", 4, 1, 2), E("EMERALD", 4, 1, 2), E("WHEAT", 10, 2, 4), E("IRON_INGOT", 4, 1, 2), E("RAW_SALMON", 6, 1, 3),
                    E("KELP", 6, 2, 6), E("GOLDEN_APPLE", 2, 1, 1)),
                P(1, 1, E("DIAMOND", 1, 1, 1), E("GOLD_INGOT", 3, 1, 3), E("", 6, 1, 1)));
            // the basement of a snow hut: someone's supplies and a precious apple
            T("IGLOO",
                P(2, 6, E("APPLE", 14, 1, 3), E("COAL", 14, 1, 4), E("GOLD_INGOT", 4, 1, 1), E("STONE_AXE", 2, 1, 1), E("ROTTEN_FLESH", 10, 1, 1), E("EMERALD", 2, 1, 1),
                    E("WHEAT", 10, 2, 3), E("BREAD", 6, 1, 2), E("SNOW", 6, 1, 3)),
                P(1, 1, E("GOLDEN_APPLE", 1, 1, 1)));
            T("PILLAGER_OUTPOST",
                P(0, 1, E("BOW", 3, 1, 1), E("", 2, 1, 1)),
                P(2, 3, E("WHEAT", 8, 3, 5), E("POTATO", 6, 2, 5), E("CARROT", 6, 2, 5), E("ARROW", 6, 2, 10), E("STRING", 4, 1, 4)),
                P(1, 3, E("DARK_LOG", 1, 2, 3), E("IRON_INGOT", 2, 1, 3), E("EMERALD", 1, 1, 2), E("GUNPOWDER", 1, 1, 3)));
            // village houses: what each trade keeps at home
            T("VILLAGE_ARMORER", P(1, 4, E("IRON_INGOT", 4, 1, 3), E("BREAD", 6, 1, 4), E("COAL", 4, 1, 3), E("EMERALD", 1, 1, 1), E("", 2, 1, 1)));
            T("VILLAGE_BUTCHER", P(1, 4, E("EMERALD", 1, 1, 1), E("PORKCHOP", 6, 1, 3), E("BEEF", 6, 1, 3), E("MUTTON", 6, 1, 3), E("CHICKEN", 4, 1, 3), E("COAL", 3, 1, 3)));
            T("VILLAGE_CARTOGRAPHER", P(1, 5, E("SUGAR_CANE", 15, 1, 6), E("FLINT_AND_STEEL", 2, 1, 1), E("BREAD", 6, 1, 3), E("STICK", 5, 1, 2), E("FEATHER", 4, 1, 3)));
            T("VILLAGE_FISHER", P(1, 4, E("EMERALD", 1, 1, 1), E("RAW_COD", 4, 1, 4), E("RAW_SALMON", 3, 1, 3), E("WATER_BUCKET", 1, 1, 1), E("BARREL", 1, 1, 1), E("WHEAT_SEEDS", 3, 1, 4), E("COAL", 2, 1, 3)));
            T("VILLAGE_FLETCHER", P(1, 5, E("EMERALD", 1, 1, 1), E("ARROW", 3, 1, 6), E("FEATHER", 6, 1, 4), E("STRING", 4, 1, 4), E("FLINT", 6, 1, 4), E("STICK", 6, 1, 4)));
            T("VILLAGE_MASON", P(1, 5, E("CLAY_BALL", 2, 1, 4), E("STONE", 4, 1, 3), E("STONE_BRICKS", 4, 1, 3), E("BREAD", 4, 1, 3), E("TERRACOTTA", 2, 1, 2), E("EMERALD", 1, 1, 1)));
            T("VILLAGE_SHEPHERD", P(1, 5, E("WOOL_WHITE", 8, 1, 6), E("WOOL_BLACK", 3, 1, 3), E("WOOL_GRAY", 3, 1, 3), E("WOOL_BROWN", 3, 1, 3), E("SHEARS", 1, 1, 1), E("WHEAT", 6, 1, 6), E("EMERALD", 1, 1, 1)));
            T("VILLAGE_TANNERY", P(1, 5, E("LEATHER", 6, 1, 3), E("BREAD", 4, 1, 4), E("STRING", 3, 1, 2), E("EMERALD", 1, 1, 2), E("", 2, 1, 1)));
            T("VILLAGE_TEMPLE", P(3, 8, E("REDSTONE_TORCH", 4, 1, 4), E("BREAD", 7, 1, 4), E("ROTTEN_FLESH", 7, 1, 4), E("LAPIS_BLOCK", 1, 1, 1), E("GOLD_INGOT", 2, 1, 4), E("EMERALD", 1, 1, 4), E("TORCH", 3, 1, 3)));
            T("VILLAGE_TOOLSMITH", P(3, 8, E("DIAMOND", 1, 1, 3), E("IRON_INGOT", 6, 1, 5), E("GOLD_INGOT", 2, 1, 3), E("BREAD", 12, 1, 3), E("IRON_PICKAXE", 2, 1, 1), E("COAL", 2, 1, 3), E("STICK", 10, 1, 3), E("IRON_SHOVEL", 4, 1, 1)));
            T("VILLAGE_WEAPONSMITH", P(3, 8, E("DIAMOND", 2, 1, 3), E("IRON_INGOT", 6, 1, 5), E("GOLD_INGOT", 3, 1, 3), E("BREAD", 12, 1, 3), E("APPLE", 12, 1, 3), E("IRON_SWORD", 3, 1, 1), E("OBSIDIAN", 4, 3, 6), E("OAK_SAPLING", 4, 3, 6)));
            T("VILLAGE_PLAINS_HOUSE", P(3, 8, E("GOLD_INGOT", 1, 1, 1), E("DANDELION", 2, 1, 1), E("POPPY", 1, 1, 1), E("POTATO", 8, 1, 6), E("BREAD", 12, 1, 4), E("APPLE", 8, 1, 5), E("BOOKSHELF", 1, 1, 1), E("FEATHER", 1, 1, 1), E("EMERALD", 2, 1, 4), E("OAK_SAPLING", 4, 1, 2)));
            T("VILLAGE_DESERT_HOUSE", P(3, 8, E("CLAY_BALL", 1, 1, 1), E("CACTUS", 10, 1, 4), E("WHEAT", 10, 1, 7), E("BREAD", 10, 1, 4), E("BOOKSHELF", 1, 1, 1), E("DEAD_BUSH", 2, 1, 3), E("EMERALD", 1, 1, 3)));
            T("VILLAGE_SAVANNA_HOUSE", P(3, 8, E("GOLD_INGOT", 1, 1, 1), E("TALL_GRASS", 5, 1, 1), E("BREAD", 10, 1, 4), E("WHEAT_SEEDS", 10, 1, 5), E("EMERALD", 2, 1, 4), E("ACACIA_SAPLING", 10, 1, 2), E("TORCH", 1, 1, 2), E("BUCKET", 1, 1, 1)));
            T("VILLAGE_SNOWY_HOUSE", P(3, 8, E("BLUE_ICE", 1, 1, 1), E("SNOW", 4, 1, 1), E("POTATO", 2, 1, 7), E("BREAD", 4, 1, 4), E("BEETROOT_SEEDS", 5, 1, 5), E("BEETROOT", 5, 1, 5), E("FURNACE", 1, 1, 1), E("EMERALD", 1, 1, 4), E("SNOW", 10, 1, 3), E("COAL", 5, 1, 4)));
            T("VILLAGE_TAIGA_HOUSE", P(3, 8, E("IRON_INGOT", 1, 1, 2), E("FERN", 2, 1, 1), E("LARGE_FERN", 2, 1, 1), E("POTATO", 10, 1, 7), E("SWEET_BERRIES", 5, 1, 7), E("BREAD", 6, 1, 4), E("PUMPKIN_SEEDS", 5, 1, 5), E("PUMPKIN", 1, 1, 1), E("EMERALD", 1, 1, 4), E("SPRUCE_SAPLING", 5, 1, 5), E("SPRUCE_LOG", 10, 1, 5)));
            // added with the jungle temple, ruined portals, mansion, monument and stronghold (appended: older chests keep their tables)
            T("RUINED_PORTAL",
                P(4, 8, E("OBSIDIAN", 8, 1, 2), E("FLINT", 8, 1, 4), E("IRON_INGOT", 6, 1, 3), E("FLINT_AND_STEEL", 4, 1, 1), E("GOLD_INGOT", 10, 2, 8),
                    E("GOLDEN_APPLE", 2, 1, 1), E("GOLDEN_CARROT", 4, 4, 12), E("GOLDEN_SWORD", 3, 1, 1), E("GOLDEN_AXE", 3, 1, 1), E("GOLDEN_PICKAXE", 3, 1, 1),
                    E("GOLD_BLOCK", 1, 1, 2)));
            T("WOODLAND_MANSION",
                P(1, 3, E("GOLDEN_APPLE", 3, 1, 1), E("DIAMOND", 2, 1, 2), E("IRON_PICKAXE", 3, 1, 1), E("DIAMOND_HOE", 2, 1, 1), E("BOOKSHELF", 4, 1, 2), E("", 6, 1, 1)),
                P(2, 6, E("IRON_INGOT", 10, 1, 4), E("GOLD_INGOT", 6, 1, 4), E("BREAD", 14, 1, 3), E("WHEAT", 14, 1, 4), E("BUCKET", 6, 1, 1), E("COAL", 10, 1, 4),
                    E("MELON_SEEDS", 6, 2, 4), E("PUMPKIN_SEEDS", 6, 2, 4), E("BEETROOT_SEEDS", 6, 2, 4), E("EMERALD", 3, 1, 2)),
                P(3, 4, E("BONE", 10, 1, 8), E("GUNPOWDER", 10, 1, 8), E("ROTTEN_FLESH", 10, 1, 8), E("STRING", 10, 1, 8)));
            T("MONUMENT_TREASURE",
                P(3, 5, E("GOLD_INGOT", 10, 3, 8), E("PRISMARINE", 8, 2, 6), E("SEA_LANTERN", 6, 1, 3), E("DIAMOND", 3, 1, 2), E("EMERALD", 5, 1, 4), E("RAW_SALMON", 6, 2, 5), E("SPONGE", 4, 1, 2)));
            T("STRONGHOLD_LIBRARY",
                P(2, 6, E("BOOKSHELF", 10, 1, 3), E("SUGAR_CANE", 14, 2, 7), E("FEATHER", 8, 1, 4), E("LEATHER", 6, 1, 3), E("ENDER_PEARL", 3, 1, 1), E("GOLDEN_APPLE", 1, 1, 1)));
            T("STRONGHOLD_STORE",
                P(2, 4, E("IRON_INGOT", 10, 1, 5), E("GOLD_INGOT", 5, 1, 3), E("REDSTONE_TORCH", 6, 1, 4), E("BREAD", 15, 1, 3), E("APPLE", 15, 1, 3), E("IRON_PICKAXE", 3, 1, 1),
                    E("IRON_SWORD", 3, 1, 1), E("ENDER_PEARL", 4, 1, 2), E("DIAMOND", 2, 1, 2), E("COAL", 10, 3, 8)));
        }
    }
}
