// Port of the items part of blocks.js: non-block items.
// [key, english, russian, texture, stack, extra]
using System.Collections.Generic;

namespace Maincraft
{
    public sealed class ItemDef
    {
        public string Key, En, Ru, Tex;
        public int Stack;
        public int Food;          // 0 = not food
        public string Tool;       // null, "pickaxe", "axe", "shovel", "sword", "hoe", "shears", "trident"
        public int Tier;
        public int Dur;
        public string Mob;        // spawn eggs
        public ItemDef(string key, string en, string ru, string tex, int stack) { Key = key; En = en; Ru = ru; Tex = tex; Stack = stack; }
    }

    public static class Items
    {
        public const int ITEM_BASE = 1000;
        public static readonly List<ItemDef> ITEM_TABLE = new List<ItemDef>();
        public static readonly Dictionary<string, int> IT = new Dictionary<string, int>();

        // the 16 dyes (Minecraft's colour order)
        public static readonly string[][] DYE_COLORS =
        {
            new[] { "WHITE", "White", "Белый" }, new[] { "ORANGE", "Orange", "Оранжевый" }, new[] { "MAGENTA", "Magenta", "Пурпурный" },
            new[] { "LIGHT_BLUE", "Light Blue", "Голубой" }, new[] { "YELLOW", "Yellow", "Жёлтый" }, new[] { "LIME", "Lime", "Лаймовый" }, new[] { "PINK", "Pink", "Розовый" },
            new[] { "GRAY", "Gray", "Серый" }, new[] { "LIGHT_GRAY", "Light Gray", "Светло-серый" }, new[] { "CYAN", "Cyan", "Бирюзовый" }, new[] { "PURPLE", "Purple", "Фиолетовый" },
            new[] { "BLUE", "Blue", "Синий" }, new[] { "BROWN", "Brown", "Коричневый" }, new[] { "GREEN", "Green", "Зелёный" }, new[] { "RED", "Red", "Красный" }, new[] { "BLACK", "Black", "Чёрный" },
        };

        static void I(string key, string en, string ru, string tex, int stack, int food = 0, string tool = null, int tier = 0, int dur = 0, string mob = null)
        {
            ITEM_TABLE.Add(new ItemDef(key, en, ru, tex, stack) { Food = food, Tool = tool, Tier = tier, Dur = dur, Mob = mob });
        }

        static bool _inited;

        public static void Init()
        {
            if (_inited) return;
            _inited = true;
            I("STICK", "Stick", "Палка", "item_stick", 64); I("COAL", "Coal", "Уголь", "item_coal", 64);
            I("IRON_INGOT", "Iron Ingot", "Железный слиток", "item_iron_ingot", 64);
            I("GOLD_INGOT", "Gold Ingot", "Золотой слиток", "item_gold_ingot", 64);
            I("COPPER_INGOT", "Copper Ingot", "Медный слиток", "item_copper_ingot", 64);
            I("DIAMOND", "Diamond", "Алмаз", "item_diamond", 64); I("EMERALD", "Emerald", "Изумруд", "item_emerald", 64);
            I("RAW_COPPER", "Raw Copper", "Необработанная медь", "item_raw_copper", 64);
            I("FLINT", "Flint", "Кремень", "item_flint", 64); I("CLAY_BALL", "Clay Ball", "Комок глины", "item_clay_ball", 64);
            I("BRICK_ITEM", "Brick", "Кирпич", "item_brick", 64); I("STRING", "String", "Нить", "item_string", 64);
            I("FEATHER", "Feather", "Перо", "item_feather", 64); I("LEATHER", "Leather", "Кожа", "item_leather", 64);
            I("BONE", "Bone", "Кость", "item_bone", 64); I("BONE_MEAL", "Bone Meal", "Костная мука", "item_bone_meal", 64);
            I("GUNPOWDER", "Gunpowder", "Порох", "item_gunpowder", 64); I("SUGAR", "Sugar", "Сахар", "item_sugar", 64);
            I("WHEAT", "Wheat", "Пшеница", "item_wheat", 64); I("WHEAT_SEEDS", "Wheat Seeds", "Семена пшеницы", "item_wheat_seeds", 64);
            I("PUMPKIN_SEEDS", "Pumpkin Seeds", "Семена тыквы", "item_pumpkin_seeds", 64);
            I("MELON_SEEDS", "Melon Seeds", "Семена арбуза", "item_melon_seeds", 64);
            I("ARROW", "Arrow", "Стрела", "item_arrow", 64); I("BOWL", "Bowl", "Миска", "item_bowl", 64);
            I("APPLE", "Apple", "Яблоко", "item_apple", 64, food: 4); I("BREAD", "Bread", "Хлеб", "item_bread", 64, food: 5);
            I("CARROT", "Carrot", "Морковь", "item_carrot", 64, food: 3); I("POTATO", "Potato", "Картофель", "item_potato", 64, food: 1);
            I("BAKED_POTATO", "Baked Potato", "Печёный картофель", "item_baked_potato", 64, food: 5);
            I("MELON_SLICE", "Melon Slice", "Ломтик арбуза", "item_melon_slice", 64, food: 2);
            I("SWEET_BERRIES", "Sweet Berries", "Сладкие ягоды", "item_sweet_berries", 64, food: 2);
            I("PORKCHOP", "Raw Porkchop", "Сырая свинина", "item_porkchop", 64, food: 3);
            I("COOKED_PORKCHOP", "Cooked Porkchop", "Жареная свинина", "item_cooked_porkchop", 64, food: 8);
            I("BEEF", "Raw Beef", "Сырая говядина", "item_beef", 64, food: 3);
            I("COOKED_BEEF", "Steak", "Стейк", "item_cooked_beef", 64, food: 8);
            I("MUTTON", "Raw Mutton", "Сырая баранина", "item_mutton", 64, food: 2);
            I("COOKED_MUTTON", "Cooked Mutton", "Жареная баранина", "item_cooked_mutton", 64, food: 6);
            I("CHICKEN", "Raw Chicken", "Сырая курица", "item_chicken", 64, food: 2);
            I("COOKED_CHICKEN", "Cooked Chicken", "Жареная курица", "item_cooked_chicken", 64, food: 6);
            I("RAW_COD", "Raw Cod", "Сырая треска", "item_raw_cod", 64, food: 2);
            I("COOKED_COD", "Cooked Cod", "Жареная треска", "item_cooked_cod", 64, food: 5);
            I("RAW_SALMON", "Raw Salmon", "Сырой лосось", "item_raw_salmon", 64, food: 2);
            I("COOKED_SALMON", "Cooked Salmon", "Жареный лосось", "item_cooked_salmon", 64, food: 6);
            I("ROTTEN_FLESH", "Rotten Flesh", "Гнилая плоть", "item_rotten_flesh", 64, food: 4);
            I("SPIDER_EYE", "Spider Eye", "Паучий глаз", "item_spider_eye", 64, food: 2);
            I("GOLDEN_APPLE", "Golden Apple", "Золотое яблоко", "item_golden_apple", 64, food: 4);
            I("GOLDEN_CARROT", "Golden Carrot", "Золотая морковь", "item_golden_carrot", 64, food: 6);
            I("MUSHROOM_STEW", "Mushroom Stew", "Грибной суп", "item_mushroom_stew", 1, food: 6);
            I("BUCKET", "Bucket", "Ведро", "item_bucket", 16);
            I("WATER_BUCKET", "Water Bucket", "Ведро воды", "item_water_bucket", 1);
            I("LAVA_BUCKET", "Lava Bucket", "Ведро лавы", "item_lava_bucket", 1);
            I("SHEARS", "Shears", "Ножницы", "item_shears", 1, tool: "shears", tier: 3);
            I("BOW", "Bow", "Лук", "item_bow", 1);
            I("FLINT_AND_STEEL", "Flint and Steel", "Огниво", "flint_and_steel", 1);
            I("SPAWN_EGG_PIG", "Pig Spawn Egg", "Яйцо призыва свиньи", "item_spawn_egg_pig", 64, mob: "pig");
            I("SPAWN_EGG_COW", "Cow Spawn Egg", "Яйцо призыва коровы", "item_spawn_egg_cow", 64, mob: "cow");
            I("SPAWN_EGG_SHEEP", "Sheep Spawn Egg", "Яйцо призыва овцы", "item_spawn_egg_sheep", 64, mob: "sheep");
            I("SPAWN_EGG_CHICKEN", "Chicken Spawn Egg", "Яйцо призыва курицы", "item_spawn_egg_chicken", 64, mob: "chicken");
            I("SPAWN_EGG_ZOMBIE", "Zombie Spawn Egg", "Яйцо призыва зомби", "item_spawn_egg_zombie", 64, mob: "zombie");
            I("SPAWN_EGG_SKELETON", "Skeleton Spawn Egg", "Яйцо призыва скелета", "item_spawn_egg_skeleton", 64, mob: "skeleton");
            I("SPAWN_EGG_CREEPER", "Creeper Spawn Egg", "Яйцо призыва крипера", "item_spawn_egg_creeper", 64, mob: "creeper");
            I("SPAWN_EGG_SPIDER", "Spider Spawn Egg", "Яйцо призыва паука", "item_spawn_egg_spider", 64, mob: "spider");
            foreach (var (mat, en, ru, tier) in new[] { ("wooden", "Wooden", "Деревянн", 1), ("stone", "Stone", "Каменн", 2),
                ("iron", "Iron", "Железн", 3), ("golden", "Golden", "Золот", 4), ("diamond", "Diamond", "Алмазн", 5) })
            {
                string M = mat.ToUpperInvariant();
                I(M + "_PICKAXE", en + " Pickaxe", ru + "ая кирка", "item_" + mat + "_pickaxe", 1, tool: "pickaxe", tier: tier);
                I(M + "_AXE", en + " Axe", ru + "ый топор", "item_" + mat + "_axe", 1, tool: "axe", tier: tier);
                I(M + "_SHOVEL", en + " Shovel", ru + "ая лопата", "item_" + mat + "_shovel", 1, tool: "shovel", tier: tier);
                I(M + "_SWORD", en + " Sword", ru + "ый меч", "item_" + mat + "_sword", 1, tool: "sword", tier: tier);
                I(M + "_HOE", en + " Hoe", ru + "ая мотыга", "item_" + mat + "_hoe", 1, tool: "hoe", tier: tier);
            }
            // appended later (keeps the ids of older items stable for saved worlds)
            I("ENDER_PEARL", "Ender Pearl", "Жемчуг Эндера", "item_ender_pearl", 16);
            I("SLIME_BALL", "Slimeball", "Слизь", "item_slime_ball", 64);
            I("SPAWN_EGG_ENDERMAN", "Enderman Spawn Egg", "Яйцо призыва эндермена", "item_spawn_egg_enderman", 64, mob: "enderman");
            I("SPAWN_EGG_SLIME", "Slime Spawn Egg", "Яйцо призыва слизня", "item_spawn_egg_slime", 64, mob: "slime");
            I("SPAWN_EGG_SALMON", "Salmon Spawn Egg", "Яйцо призыва лосося", "item_spawn_egg_salmon", 64, mob: "salmon");
            I("SPAWN_EGG_SHARK", "Shark Spawn Egg", "Яйцо призыва акулы", "item_spawn_egg_shark", 64, mob: "shark");
            I("SPAWN_EGG_DROWNED", "Drowned Spawn Egg", "Яйцо призыва утопленника", "item_spawn_egg_drowned", 64, mob: "drowned");
            I("BEETROOT", "Beetroot", "Свёкла", "item_beetroot", 64, food: 1);
            I("BEETROOT_SEEDS", "Beetroot Seeds", "Семена свёклы", "item_beetroot_seeds", 64);
            // the 16 dyes (Minecraft's colour order), nuggets, charcoal, paper, book
            foreach (var d in DYE_COLORS) I(d[0] + "_DYE", d[1] + " Dye", d[2] + " краситель", "item_" + d[0].ToLowerInvariant() + "_dye", 64);
            I("GOLD_NUGGET", "Gold Nugget", "Золотой самородок", "item_gold_nugget", 64);
            I("IRON_NUGGET", "Iron Nugget", "Железный самородок", "item_iron_nugget", 64);
            I("CHARCOAL", "Charcoal", "Древесный уголь", "item_charcoal", 64);
            I("PAPER", "Paper", "Бумага", "item_paper", 64);
            I("BOOK", "Book", "Книга", "item_book", 64);
            I("COCOA_BEANS", "Cocoa Beans", "Какао-бобы", "item_cocoa_beans", 64);
            I("REDSTONE", "Redstone Dust", "Красная пыль", "item_redstone", 64);
            I("BOAT", "Boat", "Лодка", "item_boat", 1);
            I("TRIDENT", "Trident", "Трезубец", "item_trident", 1, tool: "trident", dur: 250);
            for (int i = 0; i < ITEM_TABLE.Count; i++) IT[ITEM_TABLE[i].Key] = ITEM_BASE + i;
        }

        public static bool IsItem(int id) => id >= ITEM_BASE && id < Blocks.WET_BASE;
        public static ItemDef ItemDefOf(int id) { int i = id - ITEM_BASE; return i >= 0 && i < ITEM_TABLE.Count ? ITEM_TABLE[i] : null; }
        public static int Id(string key) => IT.TryGetValue(key, out var v) ? v : 0;

        /// <summary>The B[k] || IT[k] lookup the original uses for loot / recipe keys.</summary>
        public static int AnyId(string key)
        {
            if (Blocks.BK.TryGetValue(key, out var b) && b != 0) return b;
            return IT.TryGetValue(key, out var it) ? it : 0;
        }
    }
}
