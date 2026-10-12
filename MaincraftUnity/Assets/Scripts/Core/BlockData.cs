// Generated from BLOCK_TABLE of maincraft_90.html (blockdata.js) by tools/gen_blockdata.py.
// [key, english, russian, textures(top,side[,bottom[,front]]), shape, flags, light, base, tab]
// flags: L leaves, Y axis-rotatable, G clear glass, T translucent, F falling, e emissive,
//        A aquatic, W needs water, h hanging, c climbable, S stackable plant, O facing
namespace Maincraft
{
    public readonly struct BlockRow
    {
        public readonly string Key, En, Ru, Tex, Shape, Flags, Base, Tab;
        public readonly int Light;
        public BlockRow(string key, string en, string ru, string tex, string shape, string flags, int light, string bas, string tab)
        { Key = key; En = en; Ru = ru; Tex = tex; Shape = shape; Flags = flags; Light = light; Base = bas; Tab = tab; }
    }

    public static class BlockData
    {
        public static readonly BlockRow[] BLOCK_TABLE =
        {
            new BlockRow("GRASS", "Grass", "Трава", "grass_block_top,grass_block_side,dirt", "", "", 0, "", "n"),
            new BlockRow("DIRT", "Dirt", "Земля", "dirt", "", "", 0, "", "n"),
            new BlockRow("STONE", "Stone", "Камень", "stone", "", "", 0, "", "n"),
            new BlockRow("COBBLE", "Cobblestone", "Булыжник", "cobblestone", "", "", 0, "", "b"),
            new BlockRow("SAND", "Sand", "Песок", "sand", "", "F", 0, "", "n"),
            new BlockRow("GRAVEL", "Gravel", "Гравий", "gravel", "", "F", 0, "", "n"),
            new BlockRow("LOG", "Oak Log", "Бревно", "oak_log_top,oak_log", "", "Y", 0, "", "n"),
            new BlockRow("LEAVES", "Leaves", "Листва", "oak_leaves", "", "L", 0, "", "n"),
            new BlockRow("PLANKS", "Planks", "Доски", "oak_planks", "", "", 0, "", "b"),
            new BlockRow("GLASS", "Glass", "Стекло", "glass", "", "G", 0, "", "f"),
            new BlockRow("WATER", "Water", "Вода", "water_still", "water", "", 0, "", "n"),
            new BlockRow("BRICK", "Bricks", "Кирпичи", "bricks", "", "", 0, "", "b"),
            new BlockRow("SNOW", "Snow", "Снег", "snow", "", "", 0, "", "n"),
            new BlockRow("ICE", "Ice", "Лёд", "ice", "", "T", 0, "", "n"),
            new BlockRow("SANDSTONE", "Sandstone", "Песчаник", "sandstone_top,sandstone", "", "", 0, "", "b"),
            new BlockRow("BEDROCK", "Bedrock", "Бедрок", "bedrock", "", "", 0, "", "n"),
            new BlockRow("COAL", "Coal Ore", "Угольная руда", "coal_ore", "", "", 0, "", "n"),
            new BlockRow("IRON", "Iron Ore", "Железная руда", "iron_ore", "", "", 0, "", "n"),
            new BlockRow("GOLD", "Gold Ore", "Золотая руда", "gold_ore", "", "", 0, "", "n"),
            new BlockRow("DIAMOND", "Diamond Ore", "Алмазная руда", "diamond_ore", "", "", 0, "", "n"),
            new BlockRow("MOSSY", "Mossy Cobblestone", "Замшелый булыжник", "mossy_cobblestone", "", "", 0, "", "b"),
            new BlockRow("OBSIDIAN", "Obsidian", "Обсидиан", "obsidian", "", "", 0, "", "n"),
            new BlockRow("GLOW", "Glowstone", "Светокамень", "glowstone", "", "e", 15, "", "f"),
            new BlockRow("BOOKSHELF", "Bookshelf", "Книжный шкаф", "oak_planks,bookshelf", "", "", 0, "", "f"),
            new BlockRow("TNT", "TNT", "ТНТ", "tnt_top,tnt_side,tnt_bottom", "", "", 0, "", "f"),
            new BlockRow("WOOL_WHITE", "White Wool", "Белая шерсть", "white_wool", "", "", 0, "", "b"),
            new BlockRow("WOOL_RED", "Red Wool", "Красная шерсть", "red_wool", "", "", 0, "", "b"),
            new BlockRow("WOOL_ORANGE", "Orange Wool", "Оранжевая шерсть", "orange_wool", "", "", 0, "", "b"),
            new BlockRow("WOOL_YELLOW", "Yellow Wool", "Жёлтая шерсть", "yellow_wool", "", "", 0, "", "b"),
            new BlockRow("WOOL_GREEN", "Green Wool", "Зелёная шерсть", "green_wool", "", "", 0, "", "b"),
            new BlockRow("WOOL_BLUE", "Blue Wool", "Синяя шерсть", "blue_wool", "", "", 0, "", "b"),
            new BlockRow("WOOL_PURPLE", "Purple Wool", "Фиолетовая шерсть", "purple_wool", "", "", 0, "", "b"),
            new BlockRow("WOOL_BLACK", "Black Wool", "Чёрная шерсть", "black_wool", "", "", 0, "", "b"),
            new BlockRow("BIRCH_LOG", "Birch Log", "Берёзовое бревно", "birch_log_top,birch_log", "", "Y", 0, "", "n"),
            new BlockRow("BIRCH_LEAVES", "Birch Leaves", "Берёзовая листва", "birch_leaves", "", "L", 0, "", "n"),
            new BlockRow("SPRUCE_LOG", "Spruce Log", "Еловое бревно", "spruce_log_top,spruce_log", "", "Y", 0, "", "n"),
            new BlockRow("SPRUCE_LEAVES", "Spruce Leaves", "Еловая хвоя", "spruce_leaves", "", "L", 0, "", "n"),
            new BlockRow("BIRCH_PLANKS", "Birch Planks", "Берёзовые доски", "birch_planks", "", "", 0, "", "b"),
            new BlockRow("SPRUCE_PLANKS", "Spruce Planks", "Еловые доски", "spruce_planks", "", "", 0, "", "b"),
            new BlockRow("CACTUS", "Cactus", "Кактус", "cactus_top,cactus_side", "cactus", "", 0, "", "n"),
            new BlockRow("TALL_GRASS", "Grass", "Трава-куст", "short_grass", "x", "", 0, "", "n"),
            new BlockRow("DANDELION", "Dandelion", "Одуванчик", "dandelion", "x", "", 0, "", "n"),
            new BlockRow("POPPY", "Poppy", "Мак", "poppy", "x", "", 0, "", "n"),
            new BlockRow("DEAD_BUSH", "Dead Bush", "Сухой куст", "dead_bush", "x", "", 0, "", "n"),
            new BlockRow("DARK_LOG", "Dark Oak Log", "Бревно тёмного дуба", "dark_oak_log_top,dark_oak_log", "", "Y", 0, "", "n"),
            new BlockRow("DARK_LEAVES", "Dark Oak Leaves", "Листва тёмного дуба", "dark_oak_leaves", "", "L", 0, "", "n"),
            new BlockRow("JUNGLE_LOG", "Jungle Log", "Тропическое бревно", "jungle_log_top,jungle_log", "", "Y", 0, "", "n"),
            new BlockRow("JUNGLE_LEAVES", "Jungle Leaves", "Тропическая листва", "jungle_leaves", "", "L", 0, "", "n"),
            new BlockRow("ACACIA_LOG", "Acacia Log", "Бревно акации", "acacia_log_top,acacia_log", "", "Y", 0, "", "n"),
            new BlockRow("ACACIA_LEAVES", "Acacia Leaves", "Листва акации", "acacia_leaves", "", "L", 0, "", "n"),
            new BlockRow("PODZOL", "Podzol", "Подзол", "podzol_top,podzol_side,dirt", "", "", 0, "", "n"),
            new BlockRow("RED_SAND", "Red Sand", "Красный песок", "red_sand", "", "F", 0, "", "n"),
            new BlockRow("TERRACOTTA", "Terracotta", "Терракота", "terracotta", "", "", 0, "", "n"),
            new BlockRow("TERRA_ORANGE", "Orange Terracotta", "Оранжевая терракота", "orange_terracotta", "", "", 0, "", "b"),
            new BlockRow("TERRA_WHITE", "White Terracotta", "Белая терракота", "white_terracotta", "", "", 0, "", "b"),
            new BlockRow("FERN", "Fern", "Папоротник", "fern", "x", "", 0, "", "n"),
            new BlockRow("ANDESITE", "Andesite", "Андезит", "andesite", "", "", 0, "", "n"),
            new BlockRow("POLISHED_ANDESITE", "Polished Andesite", "Полированный андезит", "polished_andesite", "", "", 0, "", "b"),
            new BlockRow("DIORITE", "Diorite", "Диорит", "diorite", "", "", 0, "", "n"),
            new BlockRow("POLISHED_DIORITE", "Polished Diorite", "Полированный диорит", "polished_diorite", "", "", 0, "", "b"),
            new BlockRow("GRANITE", "Granite", "Гранит", "granite", "", "", 0, "", "n"),
            new BlockRow("POLISHED_GRANITE", "Polished Granite", "Полированный гранит", "polished_granite", "", "", 0, "", "b"),
            new BlockRow("STONE_BRICKS", "Stone Bricks", "Каменный кирпич", "stone_bricks", "", "", 0, "", "b"),
            new BlockRow("MOSSY_STONE_BRICKS", "Mossy Stone Bricks", "Замшелый каменный кирпич", "mossy_stone_bricks", "", "", 0, "", "b"),
            new BlockRow("CRACKED_STONE_BRICKS", "Cracked Stone Bricks", "Потрескавшийся кирпич", "cracked_stone_bricks", "", "", 0, "", "b"),
            new BlockRow("CHISELED_STONE_BRICKS", "Chiseled Stone Bricks", "Резной каменный кирпич", "chiseled_stone_bricks", "", "", 0, "", "b"),
            new BlockRow("SMOOTH_STONE", "Smooth Stone", "Гладкий камень", "smooth_stone", "", "", 0, "", "b"),
            new BlockRow("CALCITE", "Calcite", "Кальцит", "calcite", "", "", 0, "", "n"),
            new BlockRow("DEEPSLATE", "Deepslate", "Глубинный сланец", "deepslate", "", "", 0, "", "n"),
            new BlockRow("COBBLED_DEEPSLATE", "Cobbled Deepslate", "Булыжник глубинного сланца", "cobbled_deepslate", "", "", 0, "", "b"),
            new BlockRow("POLISHED_DEEPSLATE", "Polished Deepslate", "Полированный сланец", "polished_deepslate", "", "", 0, "", "b"),
            new BlockRow("DEEPSLATE_BRICKS", "Deepslate Bricks", "Кирпич из сланца", "deepslate_bricks", "", "", 0, "", "b"),
            new BlockRow("NETHERRACK", "Netherrack", "Адский камень", "netherrack", "", "", 0, "", "n"),
            new BlockRow("NETHER_BRICKS", "Nether Bricks", "Адский кирпич", "nether_bricks", "", "", 0, "", "b"),
            new BlockRow("RED_NETHER_BRICKS", "Red Nether Bricks", "Красный адский кирпич", "red_nether_bricks", "", "", 0, "", "b"),
            new BlockRow("BLACKSTONE", "Blackstone", "Чёрный камень", "blackstone", "", "", 0, "", "n"),
            new BlockRow("QUARTZ", "Quartz Block", "Кварцевый блок", "quartz_block_side", "", "", 0, "", "b"),
            new BlockRow("QUARTZ_BRICKS", "Quartz Bricks", "Кварцевый кирпич", "quartz_bricks", "", "", 0, "", "b"),
            new BlockRow("CHISELED_QUARTZ", "Chiseled Quartz", "Резной кварц", "chiseled_quartz_block", "", "", 0, "", "b"),
            new BlockRow("PRISMARINE", "Prismarine", "Призмарин", "prismarine", "", "", 0, "", "b"),
            new BlockRow("PRISMARINE_BRICKS", "Prismarine Bricks", "Призмариновый кирпич", "prismarine_bricks", "", "", 0, "", "b"),
            new BlockRow("DARK_PRISMARINE", "Dark Prismarine", "Тёмный призмарин", "dark_prismarine", "", "", 0, "", "b"),
            new BlockRow("END_STONE", "End Stone", "Эндернит", "end_stone", "", "", 0, "", "n"),
            new BlockRow("END_STONE_BRICKS", "End Stone Bricks", "Эндернитовый кирпич", "end_stone_bricks", "", "", 0, "", "b"),
            new BlockRow("PURPUR", "Purpur Block", "Пурпурный блок", "purpur_block", "", "", 0, "", "b"),
            new BlockRow("PURPUR_PILLAR", "Purpur Pillar", "Пурпурная колонна", "purpur_pillar", "", "Y", 0, "", "b"),
            new BlockRow("CLAY", "Clay", "Глина", "clay", "", "", 0, "", "n"),
            new BlockRow("PACKED_ICE", "Packed Ice", "Плотный лёд", "packed_ice", "", "", 0, "", "n"),
            new BlockRow("BLUE_ICE", "Blue Ice", "Синий лёд", "blue_ice", "", "", 0, "", "n"),
            new BlockRow("TUFF", "Tuff", "Туф", "tuff", "", "", 0, "", "n"),
            new BlockRow("WHITE_CONCRETE", "White Concrete", "Белый бетон", "white_concrete", "", "", 0, "", "b"),
            new BlockRow("ORANGE_CONCRETE", "Orange Concrete", "Оранжевый бетон", "orange_concrete", "", "", 0, "", "b"),
            new BlockRow("MAGENTA_CONCRETE", "Magenta Concrete", "Сиреневый бетон", "magenta_concrete", "", "", 0, "", "b"),
            new BlockRow("LIGHT_BLUE_CONCRETE", "Light Blue Concrete", "Голубой бетон", "light_blue_concrete", "", "", 0, "", "b"),
            new BlockRow("YELLOW_CONCRETE", "Yellow Concrete", "Жёлтый бетон", "yellow_concrete", "", "", 0, "", "b"),
            new BlockRow("LIME_CONCRETE", "Lime Concrete", "Лаймовый бетон", "lime_concrete", "", "", 0, "", "b"),
            new BlockRow("PINK_CONCRETE", "Pink Concrete", "Розовый бетон", "pink_concrete", "", "", 0, "", "b"),
            new BlockRow("GRAY_CONCRETE", "Gray Concrete", "Серый бетон", "gray_concrete", "", "", 0, "", "b"),
            new BlockRow("LIGHT_GRAY_CONCRETE", "Light Gray Concrete", "Светло-серый бетон", "light_gray_concrete", "", "", 0, "", "b"),
            new BlockRow("CYAN_CONCRETE", "Cyan Concrete", "Бирюзовый бетон", "cyan_concrete", "", "", 0, "", "b"),
            new BlockRow("PURPLE_CONCRETE", "Purple Concrete", "Фиолетовый бетон", "purple_concrete", "", "", 0, "", "b"),
            new BlockRow("BLUE_CONCRETE", "Blue Concrete", "Синий бетон", "blue_concrete", "", "", 0, "", "b"),
            new BlockRow("BROWN_CONCRETE", "Brown Concrete", "Коричневый бетон", "brown_concrete", "", "", 0, "", "b"),
            new BlockRow("GREEN_CONCRETE", "Green Concrete", "Зелёный бетон", "green_concrete", "", "", 0, "", "b"),
            new BlockRow("RED_CONCRETE", "Red Concrete", "Красный бетон", "red_concrete", "", "", 0, "", "b"),
            new BlockRow("BLACK_CONCRETE", "Black Concrete", "Чёрный бетон", "black_concrete", "", "", 0, "", "b"),
            new BlockRow("OAK_SLAB", "Oak Slab", "Дубовая плита", "", "slab", "", 0, "PLANKS", "b"),
            new BlockRow("SPRUCE_SLAB", "Spruce Slab", "Еловая плита", "", "slab", "", 0, "SPRUCE_PLANKS", "b"),
            new BlockRow("BIRCH_SLAB", "Birch Slab", "Берёзовая плита", "", "slab", "", 0, "BIRCH_PLANKS", "b"),
            new BlockRow("STONE_SLAB", "Stone Slab", "Каменная плита", "", "slab", "", 0, "STONE", "b"),
            new BlockRow("COBBLE_SLAB", "Cobblestone Slab", "Булыжная плита", "", "slab", "", 0, "COBBLE", "b"),
            new BlockRow("STONEBRICK_SLAB", "Stone Brick Slab", "Плита из кам. кирпича", "", "slab", "", 0, "STONE_BRICKS", "b"),
            new BlockRow("OAK_STAIRS", "Oak Stairs", "Дубовые ступени", "", "stairs", "", 0, "PLANKS", "b"),
            new BlockRow("SPRUCE_STAIRS", "Spruce Stairs", "Еловые ступени", "", "stairs", "", 0, "SPRUCE_PLANKS", "b"),
            new BlockRow("BIRCH_STAIRS", "Birch Stairs", "Берёзовые ступени", "", "stairs", "", 0, "BIRCH_PLANKS", "b"),
            new BlockRow("STONE_STAIRS", "Stone Stairs", "Каменные ступени", "", "stairs", "", 0, "STONE", "b"),
            new BlockRow("COBBLE_STAIRS", "Cobblestone Stairs", "Булыжные ступени", "", "stairs", "", 0, "COBBLE", "b"),
            new BlockRow("STONEBRICK_STAIRS", "Stone Brick Stairs", "Ступени из кам. кирпича", "", "stairs", "", 0, "STONE_BRICKS", "b"),
            new BlockRow("OAK_FENCE", "Oak Fence", "Дубовый забор", "", "fence", "", 0, "PLANKS", "f"),
            new BlockRow("SPRUCE_FENCE", "Spruce Fence", "Еловый забор", "", "fence", "", 0, "SPRUCE_PLANKS", "f"),
            new BlockRow("BIRCH_FENCE", "Birch Fence", "Берёзовый забор", "", "fence", "", 0, "BIRCH_PLANKS", "f"),
            new BlockRow("OAK_GATE", "Oak Fence Gate", "Дубовая калитка", "", "gate", "", 0, "PLANKS", "f"),
            new BlockRow("SPRUCE_GATE", "Spruce Fence Gate", "Еловая калитка", "", "gate", "", 0, "SPRUCE_PLANKS", "f"),
            new BlockRow("BIRCH_GATE", "Birch Fence Gate", "Берёзовая калитка", "", "gate", "", 0, "BIRCH_PLANKS", "f"),
            new BlockRow("OAK_DOOR", "Oak Door", "Дубовая дверь", "oak_door_top,oak_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("SPRUCE_DOOR", "Spruce Door", "Еловая дверь", "spruce_door_top,spruce_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("BIRCH_DOOR", "Birch Door", "Берёзовая дверь", "birch_door_top,birch_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("OAK_TRAPDOOR", "Oak Trapdoor", "Дубовый люк", "oak_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("SPRUCE_TRAPDOOR", "Spruce Trapdoor", "Еловый люк", "spruce_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("BIRCH_TRAPDOOR", "Birch Trapdoor", "Берёзовый люк", "birch_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("LADDER", "Ladder", "Лестница", "ladder", "ladder", "", 0, "", "f"),
            new BlockRow("TORCH", "Torch", "Факел", "torch", "torch", "", 14, "", "f"),
            new BlockRow("WOOL_MAGENTA", "Magenta Wool", "Шерсть: сиреневый", "magenta_wool", "", "", 0, "", "c"),
            new BlockRow("WOOL_LIGHT_BLUE", "Light Blue Wool", "Шерсть: голубой", "light_blue_wool", "", "", 0, "", "c"),
            new BlockRow("WOOL_LIME", "Lime Wool", "Шерсть: лаймовый", "lime_wool", "", "", 0, "", "c"),
            new BlockRow("WOOL_PINK", "Pink Wool", "Шерсть: розовый", "pink_wool", "", "", 0, "", "c"),
            new BlockRow("WOOL_GRAY", "Gray Wool", "Шерсть: серый", "gray_wool", "", "", 0, "", "c"),
            new BlockRow("WOOL_LIGHT_GRAY", "Light Gray Wool", "Шерсть: светло-серый", "light_gray_wool", "", "", 0, "", "c"),
            new BlockRow("WOOL_CYAN", "Cyan Wool", "Шерсть: бирюзовый", "cyan_wool", "", "", 0, "", "c"),
            new BlockRow("WOOL_BROWN", "Brown Wool", "Шерсть: коричневый", "brown_wool", "", "", 0, "", "c"),
            new BlockRow("WHITE_STAINED_GLASS", "White Stained Glass", "Стекло: белый", "white_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("ORANGE_STAINED_GLASS", "Orange Stained Glass", "Стекло: оранжевый", "orange_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("MAGENTA_STAINED_GLASS", "Magenta Stained Glass", "Стекло: сиреневый", "magenta_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("LIGHT_BLUE_STAINED_GLASS", "Light Blue Stained Glass", "Стекло: голубой", "light_blue_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("YELLOW_STAINED_GLASS", "Yellow Stained Glass", "Стекло: жёлтый", "yellow_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("LIME_STAINED_GLASS", "Lime Stained Glass", "Стекло: лаймовый", "lime_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("PINK_STAINED_GLASS", "Pink Stained Glass", "Стекло: розовый", "pink_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("GRAY_STAINED_GLASS", "Gray Stained Glass", "Стекло: серый", "gray_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("LIGHT_GRAY_STAINED_GLASS", "Light Gray Stained Glass", "Стекло: светло-серый", "light_gray_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("CYAN_STAINED_GLASS", "Cyan Stained Glass", "Стекло: бирюзовый", "cyan_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("PURPLE_STAINED_GLASS", "Purple Stained Glass", "Стекло: фиолетовый", "purple_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("BLUE_STAINED_GLASS", "Blue Stained Glass", "Стекло: синий", "blue_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("BROWN_STAINED_GLASS", "Brown Stained Glass", "Стекло: коричневый", "brown_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("GREEN_STAINED_GLASS", "Green Stained Glass", "Стекло: зелёный", "green_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("RED_STAINED_GLASS", "Red Stained Glass", "Стекло: красный", "red_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("BLACK_STAINED_GLASS", "Black Stained Glass", "Стекло: чёрный", "black_stained_glass", "", "T", 0, "", "c"),
            new BlockRow("TINTED_GLASS", "Tinted Glass", "Тонированное стекло", "tinted_glass", "", "T", 0, "", "c"),
            new BlockRow("WHITE_CONCRETE_POWDER", "White Concrete Powder", "Бетонная пыль: белый", "white_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("ORANGE_CONCRETE_POWDER", "Orange Concrete Powder", "Бетонная пыль: оранжевый", "orange_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("MAGENTA_CONCRETE_POWDER", "Magenta Concrete Powder", "Бетонная пыль: сиреневый", "magenta_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("LIGHT_BLUE_CONCRETE_POWDER", "Light Blue Concrete Powder", "Бетонная пыль: голубой", "light_blue_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("YELLOW_CONCRETE_POWDER", "Yellow Concrete Powder", "Бетонная пыль: жёлтый", "yellow_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("LIME_CONCRETE_POWDER", "Lime Concrete Powder", "Бетонная пыль: лаймовый", "lime_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("PINK_CONCRETE_POWDER", "Pink Concrete Powder", "Бетонная пыль: розовый", "pink_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("GRAY_CONCRETE_POWDER", "Gray Concrete Powder", "Бетонная пыль: серый", "gray_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("LIGHT_GRAY_CONCRETE_POWDER", "Light Gray Concrete Powder", "Бетонная пыль: светло-серый", "light_gray_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("CYAN_CONCRETE_POWDER", "Cyan Concrete Powder", "Бетонная пыль: бирюзовый", "cyan_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("PURPLE_CONCRETE_POWDER", "Purple Concrete Powder", "Бетонная пыль: фиолетовый", "purple_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("BLUE_CONCRETE_POWDER", "Blue Concrete Powder", "Бетонная пыль: синий", "blue_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("BROWN_CONCRETE_POWDER", "Brown Concrete Powder", "Бетонная пыль: коричневый", "brown_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("GREEN_CONCRETE_POWDER", "Green Concrete Powder", "Бетонная пыль: зелёный", "green_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("RED_CONCRETE_POWDER", "Red Concrete Powder", "Бетонная пыль: красный", "red_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("BLACK_CONCRETE_POWDER", "Black Concrete Powder", "Бетонная пыль: чёрный", "black_concrete_powder", "", "F", 0, "", "c"),
            new BlockRow("WHITE_GLAZED", "White Glazed Terracotta", "Глазурь: белый", "white_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("ORANGE_GLAZED", "Orange Glazed Terracotta", "Глазурь: оранжевый", "orange_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("MAGENTA_GLAZED", "Magenta Glazed Terracotta", "Глазурь: сиреневый", "magenta_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("LIGHT_BLUE_GLAZED", "Light Blue Glazed Terracotta", "Глазурь: голубой", "light_blue_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("YELLOW_GLAZED", "Yellow Glazed Terracotta", "Глазурь: жёлтый", "yellow_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("LIME_GLAZED", "Lime Glazed Terracotta", "Глазурь: лаймовый", "lime_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("PINK_GLAZED", "Pink Glazed Terracotta", "Глазурь: розовый", "pink_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("GRAY_GLAZED", "Gray Glazed Terracotta", "Глазурь: серый", "gray_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("LIGHT_GRAY_GLAZED", "Light Gray Glazed Terracotta", "Глазурь: светло-серый", "light_gray_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("CYAN_GLAZED", "Cyan Glazed Terracotta", "Глазурь: бирюзовый", "cyan_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("PURPLE_GLAZED", "Purple Glazed Terracotta", "Глазурь: фиолетовый", "purple_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("BLUE_GLAZED", "Blue Glazed Terracotta", "Глазурь: синий", "blue_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("BROWN_GLAZED", "Brown Glazed Terracotta", "Глазурь: коричневый", "brown_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("GREEN_GLAZED", "Green Glazed Terracotta", "Глазурь: зелёный", "green_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("RED_GLAZED", "Red Glazed Terracotta", "Глазурь: красный", "red_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("BLACK_GLAZED", "Black Glazed Terracotta", "Глазурь: чёрный", "black_glazed_terracotta", "", "", 0, "", "c"),
            new BlockRow("MAGENTA_TERRACOTTA", "Magenta Terracotta", "Терракота: сиреневый", "magenta_terracotta", "", "", 0, "", "c"),
            new BlockRow("LIGHT_BLUE_TERRACOTTA", "Light Blue Terracotta", "Терракота: голубой", "light_blue_terracotta", "", "", 0, "", "c"),
            new BlockRow("YELLOW_TERRACOTTA", "Yellow Terracotta", "Терракота: жёлтый", "yellow_terracotta", "", "", 0, "", "c"),
            new BlockRow("LIME_TERRACOTTA", "Lime Terracotta", "Терракота: лаймовый", "lime_terracotta", "", "", 0, "", "c"),
            new BlockRow("PINK_TERRACOTTA", "Pink Terracotta", "Терракота: розовый", "pink_terracotta", "", "", 0, "", "c"),
            new BlockRow("GRAY_TERRACOTTA", "Gray Terracotta", "Терракота: серый", "gray_terracotta", "", "", 0, "", "c"),
            new BlockRow("LIGHT_GRAY_TERRACOTTA", "Light Gray Terracotta", "Терракота: светло-серый", "light_gray_terracotta", "", "", 0, "", "c"),
            new BlockRow("CYAN_TERRACOTTA", "Cyan Terracotta", "Терракота: бирюзовый", "cyan_terracotta", "", "", 0, "", "c"),
            new BlockRow("PURPLE_TERRACOTTA", "Purple Terracotta", "Терракота: фиолетовый", "purple_terracotta", "", "", 0, "", "c"),
            new BlockRow("BLUE_TERRACOTTA", "Blue Terracotta", "Терракота: синий", "blue_terracotta", "", "", 0, "", "c"),
            new BlockRow("BROWN_TERRACOTTA", "Brown Terracotta", "Терракота: коричневый", "brown_terracotta", "", "", 0, "", "c"),
            new BlockRow("GREEN_TERRACOTTA", "Green Terracotta", "Терракота: зелёный", "green_terracotta", "", "", 0, "", "c"),
            new BlockRow("RED_TERRACOTTA", "Red Terracotta", "Терракота: красный", "red_terracotta", "", "", 0, "", "c"),
            new BlockRow("BLACK_TERRACOTTA", "Black Terracotta", "Терракота: чёрный", "black_terracotta", "", "", 0, "", "c"),
            new BlockRow("DARK_PLANKS", "Dark Oak Planks", "Доски тёмного дуба", "dark_oak_planks", "", "", 0, "", "b"),
            new BlockRow("JUNGLE_PLANKS", "Jungle Planks", "Доски тропич. дерева", "jungle_planks", "", "", 0, "", "b"),
            new BlockRow("ACACIA_PLANKS", "Acacia Planks", "Доски акации", "acacia_planks", "", "", 0, "", "b"),
            new BlockRow("MANGROVE_PLANKS", "Mangrove Planks", "Доски мангрового дерева", "mangrove_planks", "", "", 0, "", "b"),
            new BlockRow("CHERRY_PLANKS", "Cherry Planks", "Доски вишни", "cherry_planks", "", "", 0, "", "b"),
            new BlockRow("CRIMSON_PLANKS", "Crimson Planks", "Багровые доски", "crimson_planks", "", "", 0, "", "b"),
            new BlockRow("WARPED_PLANKS", "Warped Planks", "Искажённые доски", "warped_planks", "", "", 0, "", "b"),
            new BlockRow("CUT_SANDSTONE", "Cut Sandstone", "Резаный песчаник", "cut_sandstone", "", "", 0, "", "b"),
            new BlockRow("CHISELED_SANDSTONE", "Chiseled Sandstone", "Точёный песчаник", "chiseled_sandstone", "", "", 0, "", "b"),
            new BlockRow("RED_SANDSTONE", "Red Sandstone", "Красный песчаник", "red_sandstone_top,red_sandstone", "", "", 0, "", "b"),
            new BlockRow("CUT_RED_SANDSTONE", "Cut Red Sandstone", "Резаный кр. песчаник", "cut_red_sandstone", "", "", 0, "", "b"),
            new BlockRow("QUARTZ_PILLAR", "Quartz Pillar", "Кварцевая колонна", "quartz_pillar_top,quartz_pillar", "", "Y", 0, "", "b"),
            new BlockRow("BASALT", "Basalt", "Базальт", "basalt_top,basalt_side", "", "Y", 0, "", "b"),
            new BlockRow("POLISHED_BASALT", "Polished Basalt", "Полир. базальт", "polished_basalt_top,polished_basalt_side", "", "Y", 0, "", "b"),
            new BlockRow("SMOOTH_BASALT", "Smooth Basalt", "Гладкий базальт", "smooth_basalt", "", "", 0, "", "b"),
            new BlockRow("DRIPSTONE", "Dripstone Block", "Капельник", "dripstone_block", "", "", 0, "", "b"),
            new BlockRow("CHISELED_NETHER_BRICKS", "Chiseled Nether Bricks", "Точёный незер-кирпич", "chiseled_nether_bricks", "", "", 0, "", "b"),
            new BlockRow("CRACKED_NETHER_BRICKS", "Cracked Nether Bricks", "Трескан. незер-кирпич", "cracked_nether_bricks", "", "", 0, "", "b"),
            new BlockRow("CHISELED_DEEPSLATE", "Chiseled Deepslate", "Точёный сланец", "chiseled_deepslate", "", "", 0, "", "b"),
            new BlockRow("CRACKED_DEEPSLATE_BRICKS", "Cracked Deepslate Bricks", "Трескан. сланц. кирпич", "cracked_deepslate_bricks", "", "", 0, "", "b"),
            new BlockRow("DEEPSLATE_TILES", "Deepslate Tiles", "Сланцевая плитка", "deepslate_tiles", "", "", 0, "", "b"),
            new BlockRow("POLISHED_BLACKSTONE", "Polished Blackstone", "Полир. чёрный камень", "polished_blackstone", "", "", 0, "", "b"),
            new BlockRow("POLISHED_BLACKSTONE_BRICKS", "Polished Blackstone Bricks", "Кирпич из чёрн. камня", "polished_blackstone_bricks", "", "", 0, "", "b"),
            new BlockRow("CRACKED_POLISHED_BLACKSTONE_BRICKS", "Cracked Polished Blackstone Bricks", "Трескан. кирпич чёрн. камня", "cracked_polished_blackstone_bricks", "", "", 0, "", "b"),
            new BlockRow("CHISELED_POLISHED_BLACKSTONE", "Chiseled Polished Blackstone", "Точёный чёрный камень", "chiseled_polished_blackstone", "", "", 0, "", "b"),
            new BlockRow("GILDED_BLACKSTONE", "Gilded Blackstone", "Позолоч. чёрный камень", "gilded_blackstone", "", "", 0, "", "b"),
            new BlockRow("MUD_BRICKS", "Mud Bricks", "Грязевой кирпич", "mud_bricks", "", "", 0, "", "b"),
            new BlockRow("COPPER_BLOCK", "Block of Copper", "Медный блок", "copper_block", "", "", 0, "", "b"),
            new BlockRow("EXPOSED_COPPER", "Exposed Copper", "Окислен. медь", "exposed_copper", "", "", 0, "", "b"),
            new BlockRow("WEATHERED_COPPER", "Weathered Copper", "Выветр. медь", "weathered_copper", "", "", 0, "", "b"),
            new BlockRow("OXIDIZED_COPPER", "Oxidized Copper", "Окислен. медь", "oxidized_copper", "", "", 0, "", "b"),
            new BlockRow("CUT_COPPER", "Cut Copper", "Резаная медь", "cut_copper", "", "", 0, "", "b"),
            new BlockRow("EXPOSED_CUT_COPPER", "Exposed Cut Copper", "Резаная окисл. медь", "exposed_cut_copper", "", "", 0, "", "b"),
            new BlockRow("WEATHERED_CUT_COPPER", "Weathered Cut Copper", "Резаная выветр. медь", "weathered_cut_copper", "", "", 0, "", "b"),
            new BlockRow("OXIDIZED_CUT_COPPER", "Oxidized Cut Copper", "Резаная окисл. медь", "oxidized_cut_copper", "", "", 0, "", "b"),
            new BlockRow("STRIPPED_OAK_LOG", "Stripped Oak Log", "Очищ. дубовое бревно", "stripped_oak_log_top,stripped_oak_log", "", "Y", 0, "", "n"),
            new BlockRow("STRIPPED_BIRCH_LOG", "Stripped Birch Log", "Очищ. берёзовое бревно", "stripped_birch_log_top,stripped_birch_log", "", "Y", 0, "", "n"),
            new BlockRow("STRIPPED_SPRUCE_LOG", "Stripped Spruce Log", "Очищ. еловое бревно", "stripped_spruce_log_top,stripped_spruce_log", "", "Y", 0, "", "n"),
            new BlockRow("STRIPPED_DARK_OAK_LOG", "Stripped Dark Oak Log", "Очищ. бревно тёмн. дуба", "stripped_dark_oak_log_top,stripped_dark_oak_log", "", "Y", 0, "", "n"),
            new BlockRow("STRIPPED_JUNGLE_LOG", "Stripped Jungle Log", "Очищ. тропич. бревно", "stripped_jungle_log_top,stripped_jungle_log", "", "Y", 0, "", "n"),
            new BlockRow("STRIPPED_ACACIA_LOG", "Stripped Acacia Log", "Очищ. бревно акации", "stripped_acacia_log_top,stripped_acacia_log", "", "Y", 0, "", "n"),
            new BlockRow("CRIMSON_STEM", "Crimson Stem", "Багровый стебель", "crimson_stem_top,crimson_stem", "", "Y", 0, "", "n"),
            new BlockRow("WARPED_STEM", "Warped Stem", "Искажённый стебель", "warped_stem_top,warped_stem", "", "Y", 0, "", "n"),
            new BlockRow("BAMBOO_BLOCK", "Block of Bamboo", "Бамбуковый блок", "bamboo_block", "", "", 0, "", "n"),
            new BlockRow("AZALEA_LEAVES", "Azalea Leaves", "Листва азалии", "azalea_leaves", "", "L", 0, "", "n"),
            new BlockRow("FLOWERING_AZALEA_LEAVES", "Flowering Azalea Leaves", "Цветущая листва азалии", "flowering_azalea_leaves", "", "L", 0, "", "n"),
            new BlockRow("REDSTONE_ORE", "Redstone Ore", "Красная руда", "redstone_ore", "", "", 0, "", "n"),
            new BlockRow("LAPIS_ORE", "Lapis Lazuli Ore", "Лазуритовая руда", "lapis_ore", "", "", 0, "", "n"),
            new BlockRow("EMERALD_ORE", "Emerald Ore", "Изумрудная руда", "emerald_ore", "", "", 0, "", "n"),
            new BlockRow("COPPER_ORE", "Copper Ore", "Медная руда", "copper_ore", "", "", 0, "", "n"),
            new BlockRow("NETHER_QUARTZ_ORE", "Nether Quartz Ore", "Незер-кварц. руда", "nether_quartz_ore", "", "", 0, "", "n"),
            new BlockRow("NETHER_GOLD_ORE", "Nether Gold Ore", "Незер-золотая руда", "nether_gold_ore", "", "", 0, "", "n"),
            new BlockRow("DEEPSLATE_COAL_ORE", "Deepslate Coal Ore", "Сланц. угольная руда", "deepslate_coal_ore", "", "", 0, "", "n"),
            new BlockRow("DEEPSLATE_IRON_ORE", "Deepslate Iron Ore", "Сланц. железная руда", "deepslate_iron_ore", "", "", 0, "", "n"),
            new BlockRow("DEEPSLATE_GOLD_ORE", "Deepslate Gold Ore", "Сланц. золотая руда", "deepslate_gold_ore", "", "", 0, "", "n"),
            new BlockRow("DEEPSLATE_DIAMOND_ORE", "Deepslate Diamond Ore", "Сланц. алмазная руда", "deepslate_diamond_ore", "", "", 0, "", "n"),
            new BlockRow("DEEPSLATE_REDSTONE_ORE", "Deepslate Redstone Ore", "Сланц. красная руда", "deepslate_redstone_ore", "", "", 0, "", "n"),
            new BlockRow("DEEPSLATE_LAPIS_ORE", "Deepslate Lapis Ore", "Сланц. лазурит. руда", "deepslate_lapis_ore", "", "", 0, "", "n"),
            new BlockRow("DEEPSLATE_EMERALD_ORE", "Deepslate Emerald Ore", "Сланц. изумр. руда", "deepslate_emerald_ore", "", "", 0, "", "n"),
            new BlockRow("DEEPSLATE_COPPER_ORE", "Deepslate Copper Ore", "Сланц. медная руда", "deepslate_copper_ore", "", "", 0, "", "n"),
            new BlockRow("ANCIENT_DEBRIS", "Ancient Debris", "Древние обломки", "ancient_debris_top,ancient_debris_side", "", "", 0, "", "n"),
            new BlockRow("REDSTONE_BLOCK", "Block of Redstone", "Блок красного камня", "redstone_block", "", "", 0, "", "n"),
            new BlockRow("LAPIS_BLOCK", "Block of Lapis Lazuli", "Лазуритовый блок", "lapis_block", "", "", 0, "", "n"),
            new BlockRow("EMERALD_BLOCK", "Block of Emerald", "Изумрудный блок", "emerald_block", "", "", 0, "", "n"),
            new BlockRow("COAL_BLOCK", "Block of Coal", "Угольный блок", "coal_block", "", "", 0, "", "n"),
            new BlockRow("IRON_BLOCK", "Block of Iron", "Железный блок", "iron_block", "", "", 0, "", "n"),
            new BlockRow("GOLD_BLOCK", "Block of Gold", "Золотой блок", "gold_block", "", "", 0, "", "n"),
            new BlockRow("DIAMOND_BLOCK", "Block of Diamond", "Алмазный блок", "diamond_block", "", "", 0, "", "n"),
            new BlockRow("NETHERITE_BLOCK", "Block of Netherite", "Незеритовый блок", "netherite_block", "", "", 0, "", "n"),
            new BlockRow("RAW_IRON_BLOCK", "Block of Raw Iron", "Блок сырого железа", "raw_iron_block", "", "", 0, "", "n"),
            new BlockRow("RAW_COPPER_BLOCK", "Block of Raw Copper", "Блок сырой меди", "raw_copper_block", "", "", 0, "", "n"),
            new BlockRow("RAW_GOLD_BLOCK", "Block of Raw Gold", "Блок сырого золота", "raw_gold_block", "", "", 0, "", "n"),
            new BlockRow("AMETHYST_BLOCK", "Block of Amethyst", "Аметистовый блок", "amethyst_block", "", "", 0, "", "n"),
            new BlockRow("BUDDING_AMETHYST", "Budding Amethyst", "Прорастающий аметист", "budding_amethyst", "", "", 0, "", "n"),
            new BlockRow("COARSE_DIRT", "Coarse Dirt", "Грубая земля", "coarse_dirt", "", "", 0, "", "n"),
            new BlockRow("ROOTED_DIRT", "Rooted Dirt", "Корневая земля", "rooted_dirt", "", "", 0, "", "n"),
            new BlockRow("MUD", "Mud", "Грязь", "mud", "", "", 0, "", "n"),
            new BlockRow("MYCELIUM", "Mycelium", "Мицелий", "mycelium_top,mycelium_side,dirt", "", "", 0, "", "n"),
            new BlockRow("DIRT_PATH", "Dirt Path", "Тропинка", "dirt_path_top,dirt_path_side,dirt", "farmland", "", 0, "", "n"),
            new BlockRow("BONE_BLOCK", "Bone Block", "Костяной блок", "bone_block_top,bone_block_side", "", "Y", 0, "", "n"),
            new BlockRow("MOSS_BLOCK", "Moss Block", "Блок мха", "moss_block", "", "", 0, "", "n"),
            new BlockRow("SOUL_SAND", "Soul Sand", "Песок душ", "soul_sand", "", "", 0, "", "n"),
            new BlockRow("SOUL_SOIL", "Soul Soil", "Почва душ", "soul_soil", "", "", 0, "", "n"),
            new BlockRow("MAGMA_BLOCK", "Magma Block", "Магмовый блок", "magma", "", "e", 3, "", "n"),
            new BlockRow("SHROOMLIGHT", "Shroomlight", "Грибной светильник", "shroomlight", "", "", 15, "", "n"),
            new BlockRow("NETHER_WART_BLOCK", "Nether Wart Block", "Блок незер-нароста", "nether_wart_block", "", "", 0, "", "n"),
            new BlockRow("WARPED_WART_BLOCK", "Warped Wart Block", "Блок искаж. нароста", "warped_wart_block", "", "", 0, "", "n"),
            new BlockRow("SEA_LANTERN", "Sea Lantern", "Морской фонарь", "sea_lantern", "", "", 15, "", "n"),
            new BlockRow("SPONGE", "Sponge", "Губка", "sponge", "", "", 0, "", "n"),
            new BlockRow("WET_SPONGE", "Wet Sponge", "Мокрая губка", "wet_sponge", "", "", 0, "", "n"),
            new BlockRow("DRIED_KELP_BLOCK", "Dried Kelp Block", "Блок сухих водорослей", "dried_kelp_top,dried_kelp_side", "", "", 0, "", "n"),
            new BlockRow("CRYING_OBSIDIAN", "Crying Obsidian", "Плачущий обсидиан", "crying_obsidian", "", "", 10, "", "n"),
            new BlockRow("CRIMSON_NYLIUM", "Crimson Nylium", "Багровый мицелий", "crimson_nylium,crimson_nylium_side,netherrack", "", "", 0, "", "n"),
            new BlockRow("WARPED_NYLIUM", "Warped Nylium", "Искажённый мицелий", "warped_nylium,warped_nylium_side,netherrack", "", "", 0, "", "n"),
            new BlockRow("BROWN_MUSHROOM_BLOCK", "Brown Mushroom Block", "Бурый грибной блок", "brown_mushroom_block", "", "", 0, "", "n"),
            new BlockRow("RED_MUSHROOM_BLOCK", "Red Mushroom Block", "Красный грибной блок", "red_mushroom_block", "", "", 0, "", "n"),
            new BlockRow("MUSHROOM_STEM", "Mushroom Stem", "Грибная ножка", "mushroom_stem", "", "", 0, "", "n"),
            new BlockRow("BRAIN_CORAL_BLOCK", "Brain Coral Block", "Коралл: мозговой", "brain_coral_block", "", "", 0, "", "n"),
            new BlockRow("DEAD_BRAIN_CORAL_BLOCK", "Dead Brain Coral Block", "Мёртвый коралл: мозговой", "dead_brain_coral_block", "", "", 0, "", "n"),
            new BlockRow("BUBBLE_CORAL_BLOCK", "Bubble Coral Block", "Коралл: пузырчатый", "bubble_coral_block", "", "", 0, "", "n"),
            new BlockRow("DEAD_BUBBLE_CORAL_BLOCK", "Dead Bubble Coral Block", "Мёртвый коралл: пузырчатый", "dead_bubble_coral_block", "", "", 0, "", "n"),
            new BlockRow("FIRE_CORAL_BLOCK", "Fire Coral Block", "Коралл: огненный", "fire_coral_block", "", "", 0, "", "n"),
            new BlockRow("DEAD_FIRE_CORAL_BLOCK", "Dead Fire Coral Block", "Мёртвый коралл: огненный", "dead_fire_coral_block", "", "", 0, "", "n"),
            new BlockRow("HORN_CORAL_BLOCK", "Horn Coral Block", "Коралл: рогатый", "horn_coral_block", "", "", 0, "", "n"),
            new BlockRow("DEAD_HORN_CORAL_BLOCK", "Dead Horn Coral Block", "Мёртвый коралл: рогатый", "dead_horn_coral_block", "", "", 0, "", "n"),
            new BlockRow("TUBE_CORAL_BLOCK", "Tube Coral Block", "Коралл: трубчатый", "tube_coral_block", "", "", 0, "", "n"),
            new BlockRow("DEAD_TUBE_CORAL_BLOCK", "Dead Tube Coral Block", "Мёртвый коралл: трубчатый", "dead_tube_coral_block", "", "", 0, "", "n"),
            new BlockRow("PUMPKIN", "Pumpkin", "Тыква", "pumpkin_top,pumpkin_side", "", "", 0, "", "n"),
            new BlockRow("CARVED_PUMPKIN", "Carved Pumpkin", "Резная тыква", "pumpkin_top,carved_pumpkin", "", "O", 0, "", "n"),
            new BlockRow("JACK_O_LANTERN", "Jack o'Lantern", "Светильник Джека", "pumpkin_top,jack_o_lantern", "", "O", 15, "", "n"),
            new BlockRow("MELON", "Melon", "Арбуз", "melon_top,melon_side", "", "", 0, "", "n"),
            new BlockRow("HAY_BLOCK", "Hay Bale", "Стог сена", "hay_block_top,hay_block_side", "", "Y", 0, "", "n"),
            new BlockRow("HONEYCOMB_BLOCK", "Honeycomb Block", "Соты", "honeycomb_block", "", "", 0, "", "n"),
            new BlockRow("OAK_WOOD", "Oak Wood", "Дубовое дерево", "oak_log", "", "", 0, "", "n"),
            new BlockRow("STRIPPED_OAK_WOOD", "Stripped Oak Wood", "Очищ. дубовое дерево", "stripped_oak_log", "", "", 0, "", "n"),
            new BlockRow("BIRCH_WOOD", "Birch Wood", "Берёзовое дерево", "birch_log", "", "", 0, "", "n"),
            new BlockRow("STRIPPED_BIRCH_WOOD", "Stripped Birch Wood", "Очищ. берёзовое дерево", "stripped_birch_log", "", "", 0, "", "n"),
            new BlockRow("SPRUCE_WOOD", "Spruce Wood", "Еловое дерево", "spruce_log", "", "", 0, "", "n"),
            new BlockRow("STRIPPED_SPRUCE_WOOD", "Stripped Spruce Wood", "Очищ. еловое дерево", "stripped_spruce_log", "", "", 0, "", "n"),
            new BlockRow("DARK_OAK_WOOD", "Dark Oak Wood", "Тёмного дуба дерево", "dark_oak_log", "", "", 0, "", "n"),
            new BlockRow("STRIPPED_DARK_OAK_WOOD", "Stripped Dark Oak Wood", "Очищ. тёмного дуба дерево", "stripped_dark_oak_log", "", "", 0, "", "n"),
            new BlockRow("JUNGLE_WOOD", "Jungle Wood", "Тропическое дерево", "jungle_log", "", "", 0, "", "n"),
            new BlockRow("STRIPPED_JUNGLE_WOOD", "Stripped Jungle Wood", "Очищ. тропическое дерево", "stripped_jungle_log", "", "", 0, "", "n"),
            new BlockRow("ACACIA_WOOD", "Acacia Wood", "Акации дерево", "acacia_log", "", "", 0, "", "n"),
            new BlockRow("STRIPPED_ACACIA_WOOD", "Stripped Acacia Wood", "Очищ. акации дерево", "stripped_acacia_log", "", "", 0, "", "n"),
            new BlockRow("CRIMSON_HYPHAE", "Crimson Hyphae", "Багровые гифы", "crimson_stem", "", "", 0, "", "n"),
            new BlockRow("WARPED_HYPHAE", "Warped Hyphae", "Искажённые гифы", "warped_stem", "", "", 0, "", "n"),
            new BlockRow("STRIPPED_CRIMSON_STEM", "Stripped Crimson Stem", "Очищ. багровый стебель", "stripped_crimson_stem_top,stripped_crimson_stem", "", "Y", 0, "", "n"),
            new BlockRow("STRIPPED_WARPED_STEM", "Stripped Warped Stem", "Очищ. искаж. стебель", "stripped_warped_stem_top,stripped_warped_stem", "", "Y", 0, "", "n"),
            new BlockRow("STRIPPED_CRIMSON_HYPHAE", "Stripped Crimson Hyphae", "Очищ. багровые гифы", "stripped_crimson_stem", "", "", 0, "", "n"),
            new BlockRow("STRIPPED_WARPED_HYPHAE", "Stripped Warped Hyphae", "Очищ. искаж. гифы", "stripped_warped_stem", "", "", 0, "", "n"),
            new BlockRow("SMOOTH_SANDSTONE", "Smooth Sandstone", "Гладкий песчаник", "sandstone_top", "", "", 0, "", "b"),
            new BlockRow("SMOOTH_RED_SANDSTONE", "Smooth Red Sandstone", "Гладкий кр. песчаник", "red_sandstone_top", "", "", 0, "", "b"),
            new BlockRow("CHISELED_RED_SANDSTONE", "Chiseled Red Sandstone", "Точёный кр. песчаник", "red_sandstone_top,chiseled_red_sandstone", "", "", 0, "", "b"),
            new BlockRow("SMOOTH_QUARTZ", "Smooth Quartz Block", "Гладкий кварц", "quartz_block_bottom", "", "", 0, "", "b"),
            new BlockRow("POLISHED_TUFF", "Polished Tuff", "Полир. туф", "polished_tuff", "", "", 0, "", "b"),
            new BlockRow("TUFF_BRICKS", "Tuff Bricks", "Туфовый кирпич", "tuff_bricks", "", "", 0, "", "b"),
            new BlockRow("CHISELED_TUFF", "Chiseled Tuff", "Точёный туф", "chiseled_tuff", "", "", 0, "", "b"),
            new BlockRow("CHISELED_COPPER", "Chiseled Copper", "Точёная медь", "chiseled_copper_block", "", "", 0, "", "b"),
            new BlockRow("EXPOSED_CHISELED_COPPER", "Exposed Chiseled Copper", "Точёная окисл. медь", "chiseled_exposed_copper", "", "", 0, "", "b"),
            new BlockRow("WEATHERED_CHISELED_COPPER", "Weathered Chiseled Copper", "Точёная выветр. медь", "chiseled_weathered_copper", "", "", 0, "", "b"),
            new BlockRow("OXIDIZED_CHISELED_COPPER", "Oxidized Chiseled Copper", "Точёная окисл. медь", "chiseled_oxidized_copper", "", "", 0, "", "b"),
            new BlockRow("CRACKED_DEEPSLATE_TILES", "Cracked Deepslate Tiles", "Трескан. сланц. плитка", "cracked_deepslate_tiles", "", "", 0, "", "b"),
            new BlockRow("ALLIUM", "Allium", "Аллиум", "allium", "x", "", 0, "", "n"),
            new BlockRow("AZURE_BLUET", "Azure Bluet", "Лазоревка", "azure_bluet", "x", "", 0, "", "n"),
            new BlockRow("BLUE_ORCHID", "Blue Orchid", "Голубая орхидея", "blue_orchid", "x", "", 0, "", "n"),
            new BlockRow("CORNFLOWER", "Cornflower", "Василёк", "cornflower", "x", "", 0, "", "n"),
            new BlockRow("LILY_OF_THE_VALLEY", "Lily of the Valley", "Ландыш", "lily_of_the_valley", "x", "", 0, "", "n"),
            new BlockRow("OXEYE_DAISY", "Oxeye Daisy", "Маргаритка", "oxeye_daisy", "x", "", 0, "", "n"),
            new BlockRow("ORANGE_TULIP", "Orange Tulip", "Оранжевый тюльпан", "orange_tulip", "x", "", 0, "", "n"),
            new BlockRow("PINK_TULIP", "Pink Tulip", "Розовый тюльпан", "pink_tulip", "x", "", 0, "", "n"),
            new BlockRow("RED_TULIP", "Red Tulip", "Красный тюльпан", "red_tulip", "x", "", 0, "", "n"),
            new BlockRow("WHITE_TULIP", "White Tulip", "Белый тюльпан", "white_tulip", "x", "", 0, "", "n"),
            new BlockRow("WITHER_ROSE", "Wither Rose", "Иссушающая роза", "wither_rose", "x", "", 0, "", "n"),
            new BlockRow("BROWN_MUSHROOM", "Brown Mushroom", "Бурый гриб", "brown_mushroom", "x", "", 0, "", "n"),
            new BlockRow("RED_MUSHROOM", "Red Mushroom", "Красный гриб", "red_mushroom", "x", "", 0, "", "n"),
            new BlockRow("CRIMSON_ROOTS", "Crimson Roots", "Багровые корни", "crimson_roots", "x", "", 0, "", "n"),
            new BlockRow("WARPED_ROOTS", "Warped Roots", "Искажённые корни", "warped_roots", "x", "", 0, "", "n"),
            new BlockRow("NETHER_SPROUTS", "Nether Sprouts", "Незер-поросль", "nether_sprouts", "x", "", 0, "", "n"),
            new BlockRow("CRIMSON_FUNGUS", "Crimson Fungus", "Багровый грибок", "crimson_fungus", "x", "", 0, "", "n"),
            new BlockRow("WARPED_FUNGUS", "Warped Fungus", "Искажённый грибок", "warped_fungus", "x", "", 0, "", "n"),
            new BlockRow("OAK_SAPLING", "Oak Sapling", "Дубовый саженец", "oak_sapling", "x", "", 0, "", "n"),
            new BlockRow("SPRUCE_SAPLING", "Spruce Sapling", "Еловый саженец", "spruce_sapling", "x", "", 0, "", "n"),
            new BlockRow("BIRCH_SAPLING", "Birch Sapling", "Берёзовый саженец", "birch_sapling", "x", "", 0, "", "n"),
            new BlockRow("JUNGLE_SAPLING", "Jungle Sapling", "Тропический саженец", "jungle_sapling", "x", "", 0, "", "n"),
            new BlockRow("ACACIA_SAPLING", "Acacia Sapling", "Саженец акации", "acacia_sapling", "x", "", 0, "", "n"),
            new BlockRow("DARK_OAK_SAPLING", "Dark Oak Sapling", "Саженец тёмного дуба", "dark_oak_sapling", "x", "", 0, "", "n"),
            new BlockRow("SUGAR_CANE", "Sugar Cane", "Сахарный тростник", "sugar_cane", "x", "S", 0, "", "n"),
            new BlockRow("KELP", "Kelp", "Ламинария", "kelp", "x", "AWS", 0, "", "n"),
            new BlockRow("SEAGRASS", "Seagrass", "Морская трава", "seagrass", "x", "AW", 0, "", "n"),
            new BlockRow("SWEET_BERRY_BUSH", "Sweet Berry Bush", "Куст сладких ягод", "sweet_berry_bush", "x", "", 0, "", "n"),
            new BlockRow("BRAIN_CORAL_FAN", "Brain Coral Fan", "Коралл-веер: мозговой", "brain_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("DEAD_BRAIN_CORAL_FAN", "Dead Brain Coral Fan", "Мёртвый веер: мозговой", "dead_brain_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("BUBBLE_CORAL_FAN", "Bubble Coral Fan", "Коралл-веер: пузырчатый", "bubble_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("DEAD_BUBBLE_CORAL_FAN", "Dead Bubble Coral Fan", "Мёртвый веер: пузырчатый", "dead_bubble_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("FIRE_CORAL_FAN", "Fire Coral Fan", "Коралл-веер: огненный", "fire_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("DEAD_FIRE_CORAL_FAN", "Dead Fire Coral Fan", "Мёртвый веер: огненный", "dead_fire_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("HORN_CORAL_FAN", "Horn Coral Fan", "Коралл-веер: рогатый", "horn_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("DEAD_HORN_CORAL_FAN", "Dead Horn Coral Fan", "Мёртвый веер: рогатый", "dead_horn_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("TUBE_CORAL_FAN", "Tube Coral Fan", "Коралл-веер: трубчатый", "tube_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("DEAD_TUBE_CORAL_FAN", "Dead Tube Coral Fan", "Мёртвый веер: трубчатый", "dead_tube_coral_fan", "x", "A", 0, "", "n"),
            new BlockRow("CAVE_VINES", "Cave Vines", "Пещерные лианы", "cave_vines", "x", "hc", 0, "", "n"),
            new BlockRow("WEEPING_VINES", "Weeping Vines", "Плакучие лозы", "weeping_vines", "x", "h", 0, "", "n"),
            new BlockRow("TWISTING_VINES", "Twisting Vines", "Скрученные лозы", "twisting_vines", "x", "c", 0, "", "n"),
            new BlockRow("CRAFTING_TABLE", "Crafting Table", "Верстак", "crafting_table_top,crafting_table_front,oak_planks", "", "O", 0, "", "f"),
            new BlockRow("FURNACE", "Furnace", "Печь", "furnace_top,furnace_front,furnace_top", "", "O", 0, "", "f"),
            new BlockRow("MOSSY_SLAB", "Mossy Cobblestone Slab", "Замшелый булыжник: плита", "", "slab", "", 0, "MOSSY", "b"),
            new BlockRow("MOSSY_STAIRS", "Mossy Cobblestone Stairs", "Замшелый булыжник: ступени", "", "stairs", "", 0, "MOSSY", "b"),
            new BlockRow("ANDESITE_SLAB", "Andesite Slab", "Андезит: плита", "", "slab", "", 0, "ANDESITE", "b"),
            new BlockRow("ANDESITE_STAIRS", "Andesite Stairs", "Андезит: ступени", "", "stairs", "", 0, "ANDESITE", "b"),
            new BlockRow("POLISHED_ANDESITE_SLAB", "Polished Andesite Slab", "Полир. андезит: плита", "", "slab", "", 0, "POLISHED_ANDESITE", "b"),
            new BlockRow("POLISHED_ANDESITE_STAIRS", "Polished Andesite Stairs", "Полир. андезит: ступени", "", "stairs", "", 0, "POLISHED_ANDESITE", "b"),
            new BlockRow("DIORITE_SLAB", "Diorite Slab", "Диорит: плита", "", "slab", "", 0, "DIORITE", "b"),
            new BlockRow("DIORITE_STAIRS", "Diorite Stairs", "Диорит: ступени", "", "stairs", "", 0, "DIORITE", "b"),
            new BlockRow("POLISHED_DIORITE_SLAB", "Polished Diorite Slab", "Полир. диорит: плита", "", "slab", "", 0, "POLISHED_DIORITE", "b"),
            new BlockRow("POLISHED_DIORITE_STAIRS", "Polished Diorite Stairs", "Полир. диорит: ступени", "", "stairs", "", 0, "POLISHED_DIORITE", "b"),
            new BlockRow("GRANITE_SLAB", "Granite Slab", "Гранит: плита", "", "slab", "", 0, "GRANITE", "b"),
            new BlockRow("GRANITE_STAIRS", "Granite Stairs", "Гранит: ступени", "", "stairs", "", 0, "GRANITE", "b"),
            new BlockRow("POLISHED_GRANITE_SLAB", "Polished Granite Slab", "Полир. гранит: плита", "", "slab", "", 0, "POLISHED_GRANITE", "b"),
            new BlockRow("POLISHED_GRANITE_STAIRS", "Polished Granite Stairs", "Полир. гранит: ступени", "", "stairs", "", 0, "POLISHED_GRANITE", "b"),
            new BlockRow("MOSSY_STONE_BRICKS_SLAB", "Mossy Stone Brick Slab", "Замшел. кам. кирпич: плита", "", "slab", "", 0, "MOSSY_STONE_BRICKS", "b"),
            new BlockRow("MOSSY_STONE_BRICKS_STAIRS", "Mossy Stone Brick Stairs", "Замшел. кам. кирпич: ступени", "", "stairs", "", 0, "MOSSY_STONE_BRICKS", "b"),
            new BlockRow("SMOOTH_STONE_SLAB", "Smooth Stone Slab", "Гладкий камень: плита", "", "slab", "", 0, "SMOOTH_STONE", "b"),
            new BlockRow("SMOOTH_STONE_STAIRS", "Smooth Stone Stairs", "Гладкий камень: ступени", "", "stairs", "", 0, "SMOOTH_STONE", "b"),
            new BlockRow("SANDSTONE_SLAB", "Sandstone Slab", "Песчаник: плита", "", "slab", "", 0, "SANDSTONE", "b"),
            new BlockRow("SANDSTONE_STAIRS", "Sandstone Stairs", "Песчаник: ступени", "", "stairs", "", 0, "SANDSTONE", "b"),
            new BlockRow("CUT_SANDSTONE_SLAB", "Cut Sandstone Slab", "Резаный песчаник: плита", "", "slab", "", 0, "CUT_SANDSTONE", "b"),
            new BlockRow("CUT_SANDSTONE_STAIRS", "Cut Sandstone Stairs", "Резаный песчаник: ступени", "", "stairs", "", 0, "CUT_SANDSTONE", "b"),
            new BlockRow("RED_SANDSTONE_SLAB", "Red Sandstone Slab", "Красный песчаник: плита", "", "slab", "", 0, "RED_SANDSTONE", "b"),
            new BlockRow("RED_SANDSTONE_STAIRS", "Red Sandstone Stairs", "Красный песчаник: ступени", "", "stairs", "", 0, "RED_SANDSTONE", "b"),
            new BlockRow("BRICK_SLAB", "Brick Slab", "Кирпич: плита", "", "slab", "", 0, "BRICK", "b"),
            new BlockRow("BRICK_STAIRS", "Brick Stairs", "Кирпич: ступени", "", "stairs", "", 0, "BRICK", "b"),
            new BlockRow("NETHER_BRICKS_SLAB", "Nether Brick Slab", "Незер-кирпич: плита", "", "slab", "", 0, "NETHER_BRICKS", "b"),
            new BlockRow("NETHER_BRICKS_STAIRS", "Nether Brick Stairs", "Незер-кирпич: ступени", "", "stairs", "", 0, "NETHER_BRICKS", "b"),
            new BlockRow("RED_NETHER_BRICKS_SLAB", "Red Nether Brick Slab", "Красн. незер-кирпич: плита", "", "slab", "", 0, "RED_NETHER_BRICKS", "b"),
            new BlockRow("RED_NETHER_BRICKS_STAIRS", "Red Nether Brick Stairs", "Красн. незер-кирпич: ступени", "", "stairs", "", 0, "RED_NETHER_BRICKS", "b"),
            new BlockRow("QUARTZ_SLAB", "Quartz Slab", "Кварц: плита", "", "slab", "", 0, "QUARTZ", "b"),
            new BlockRow("QUARTZ_STAIRS", "Quartz Stairs", "Кварц: ступени", "", "stairs", "", 0, "QUARTZ", "b"),
            new BlockRow("BLACKSTONE_SLAB", "Blackstone Slab", "Чёрный камень: плита", "", "slab", "", 0, "BLACKSTONE", "b"),
            new BlockRow("BLACKSTONE_STAIRS", "Blackstone Stairs", "Чёрный камень: ступени", "", "stairs", "", 0, "BLACKSTONE", "b"),
            new BlockRow("POLISHED_BLACKSTONE_SLAB", "Polished Blackstone Slab", "Полир. чёрн. камень: плита", "", "slab", "", 0, "POLISHED_BLACKSTONE", "b"),
            new BlockRow("POLISHED_BLACKSTONE_STAIRS", "Polished Blackstone Stairs", "Полир. чёрн. камень: ступени", "", "stairs", "", 0, "POLISHED_BLACKSTONE", "b"),
            new BlockRow("POLISHED_BLACKSTONE_BRICKS_SLAB", "Polished Blackstone Brick Slab", "Кирпич чёрн. камня: плита", "", "slab", "", 0, "POLISHED_BLACKSTONE_BRICKS", "b"),
            new BlockRow("POLISHED_BLACKSTONE_BRICKS_STAIRS", "Polished Blackstone Brick Stairs", "Кирпич чёрн. камня: ступени", "", "stairs", "", 0, "POLISHED_BLACKSTONE_BRICKS", "b"),
            new BlockRow("DEEPSLATE_BRICKS_SLAB", "Deepslate Brick Slab", "Сланц. кирпич: плита", "", "slab", "", 0, "DEEPSLATE_BRICKS", "b"),
            new BlockRow("DEEPSLATE_BRICKS_STAIRS", "Deepslate Brick Stairs", "Сланц. кирпич: ступени", "", "stairs", "", 0, "DEEPSLATE_BRICKS", "b"),
            new BlockRow("DEEPSLATE_TILES_SLAB", "Deepslate Tile Slab", "Сланц. плитка: плита", "", "slab", "", 0, "DEEPSLATE_TILES", "b"),
            new BlockRow("DEEPSLATE_TILES_STAIRS", "Deepslate Tile Stairs", "Сланц. плитка: ступени", "", "stairs", "", 0, "DEEPSLATE_TILES", "b"),
            new BlockRow("COBBLED_DEEPSLATE_SLAB", "Cobbled Deepslate Slab", "Булыжный сланец: плита", "", "slab", "", 0, "COBBLED_DEEPSLATE", "b"),
            new BlockRow("COBBLED_DEEPSLATE_STAIRS", "Cobbled Deepslate Stairs", "Булыжный сланец: ступени", "", "stairs", "", 0, "COBBLED_DEEPSLATE", "b"),
            new BlockRow("POLISHED_DEEPSLATE_SLAB", "Polished Deepslate Slab", "Полир. сланец: плита", "", "slab", "", 0, "POLISHED_DEEPSLATE", "b"),
            new BlockRow("POLISHED_DEEPSLATE_STAIRS", "Polished Deepslate Stairs", "Полир. сланец: ступени", "", "stairs", "", 0, "POLISHED_DEEPSLATE", "b"),
            new BlockRow("PRISMARINE_SLAB", "Prismarine Slab", "Призмарин: плита", "", "slab", "", 0, "PRISMARINE", "b"),
            new BlockRow("PRISMARINE_STAIRS", "Prismarine Stairs", "Призмарин: ступени", "", "stairs", "", 0, "PRISMARINE", "b"),
            new BlockRow("PRISMARINE_BRICKS_SLAB", "Prismarine Brick Slab", "Призмар. кирпич: плита", "", "slab", "", 0, "PRISMARINE_BRICKS", "b"),
            new BlockRow("PRISMARINE_BRICKS_STAIRS", "Prismarine Brick Stairs", "Призмар. кирпич: ступени", "", "stairs", "", 0, "PRISMARINE_BRICKS", "b"),
            new BlockRow("DARK_PRISMARINE_SLAB", "Dark Prismarine Slab", "Тёмный призмарин: плита", "", "slab", "", 0, "DARK_PRISMARINE", "b"),
            new BlockRow("DARK_PRISMARINE_STAIRS", "Dark Prismarine Stairs", "Тёмный призмарин: ступени", "", "stairs", "", 0, "DARK_PRISMARINE", "b"),
            new BlockRow("END_STONE_BRICKS_SLAB", "End Stone Brick Slab", "Кирпич Энда: плита", "", "slab", "", 0, "END_STONE_BRICKS", "b"),
            new BlockRow("END_STONE_BRICKS_STAIRS", "End Stone Brick Stairs", "Кирпич Энда: ступени", "", "stairs", "", 0, "END_STONE_BRICKS", "b"),
            new BlockRow("PURPUR_SLAB", "Purpur Slab", "Пурпур: плита", "", "slab", "", 0, "PURPUR", "b"),
            new BlockRow("PURPUR_STAIRS", "Purpur Stairs", "Пурпур: ступени", "", "stairs", "", 0, "PURPUR", "b"),
            new BlockRow("MUD_BRICKS_SLAB", "Mud Brick Slab", "Грязевой кирпич: плита", "", "slab", "", 0, "MUD_BRICKS", "b"),
            new BlockRow("MUD_BRICKS_STAIRS", "Mud Brick Stairs", "Грязевой кирпич: ступени", "", "stairs", "", 0, "MUD_BRICKS", "b"),
            new BlockRow("CUT_COPPER_SLAB", "Cut Copper Slab", "Резаная медь: плита", "", "slab", "", 0, "CUT_COPPER", "b"),
            new BlockRow("CUT_COPPER_STAIRS", "Cut Copper Stairs", "Резаная медь: ступени", "", "stairs", "", 0, "CUT_COPPER", "b"),
            new BlockRow("TUFF_SLAB", "Tuff Slab", "Туф: плита", "", "slab", "", 0, "TUFF", "b"),
            new BlockRow("TUFF_STAIRS", "Tuff Stairs", "Туф: ступени", "", "stairs", "", 0, "TUFF", "b"),
            new BlockRow("DARK_PLANKS_SLAB", "Dark Oak Slab", "Тёмный дуб: плита", "", "slab", "", 0, "DARK_PLANKS", "b"),
            new BlockRow("DARK_PLANKS_STAIRS", "Dark Oak Stairs", "Тёмный дуб: ступени", "", "stairs", "", 0, "DARK_PLANKS", "b"),
            new BlockRow("JUNGLE_PLANKS_SLAB", "Jungle Slab", "Тропич. дерево: плита", "", "slab", "", 0, "JUNGLE_PLANKS", "b"),
            new BlockRow("JUNGLE_PLANKS_STAIRS", "Jungle Stairs", "Тропич. дерево: ступени", "", "stairs", "", 0, "JUNGLE_PLANKS", "b"),
            new BlockRow("ACACIA_PLANKS_SLAB", "Acacia Slab", "Акация: плита", "", "slab", "", 0, "ACACIA_PLANKS", "b"),
            new BlockRow("ACACIA_PLANKS_STAIRS", "Acacia Stairs", "Акация: ступени", "", "stairs", "", 0, "ACACIA_PLANKS", "b"),
            new BlockRow("MANGROVE_PLANKS_SLAB", "Mangrove Slab", "Мангровое дерево: плита", "", "slab", "", 0, "MANGROVE_PLANKS", "b"),
            new BlockRow("MANGROVE_PLANKS_STAIRS", "Mangrove Stairs", "Мангровое дерево: ступени", "", "stairs", "", 0, "MANGROVE_PLANKS", "b"),
            new BlockRow("CHERRY_PLANKS_SLAB", "Cherry Slab", "Вишня: плита", "", "slab", "", 0, "CHERRY_PLANKS", "b"),
            new BlockRow("CHERRY_PLANKS_STAIRS", "Cherry Stairs", "Вишня: ступени", "", "stairs", "", 0, "CHERRY_PLANKS", "b"),
            new BlockRow("CRIMSON_PLANKS_SLAB", "Crimson Slab", "Багряное дерево: плита", "", "slab", "", 0, "CRIMSON_PLANKS", "b"),
            new BlockRow("CRIMSON_PLANKS_STAIRS", "Crimson Stairs", "Багряное дерево: ступени", "", "stairs", "", 0, "CRIMSON_PLANKS", "b"),
            new BlockRow("WARPED_PLANKS_SLAB", "Warped Slab", "Искажённое дерево: плита", "", "slab", "", 0, "WARPED_PLANKS", "b"),
            new BlockRow("WARPED_PLANKS_STAIRS", "Warped Stairs", "Искажённое дерево: ступени", "", "stairs", "", 0, "WARPED_PLANKS", "b"),
            new BlockRow("COBBLE_WALL", "Cobblestone Wall", "Булыжник: стена", "", "wall", "", 0, "COBBLE", "b"),
            new BlockRow("MOSSY_WALL", "Mossy Cobblestone Wall", "Замшелый булыжник: стена", "", "wall", "", 0, "MOSSY", "b"),
            new BlockRow("STONE_BRICKS_WALL", "Stone Brick Wall", "Каменный кирпич: стена", "", "wall", "", 0, "STONE_BRICKS", "b"),
            new BlockRow("MOSSY_STONE_BRICKS_WALL", "Mossy Stone Brick Wall", "Замшел. кам. кирпич: стена", "", "wall", "", 0, "MOSSY_STONE_BRICKS", "b"),
            new BlockRow("ANDESITE_WALL", "Andesite Wall", "Андезит: стена", "", "wall", "", 0, "ANDESITE", "b"),
            new BlockRow("DIORITE_WALL", "Diorite Wall", "Диорит: стена", "", "wall", "", 0, "DIORITE", "b"),
            new BlockRow("GRANITE_WALL", "Granite Wall", "Гранит: стена", "", "wall", "", 0, "GRANITE", "b"),
            new BlockRow("SANDSTONE_WALL", "Sandstone Wall", "Песчаник: стена", "", "wall", "", 0, "SANDSTONE", "b"),
            new BlockRow("RED_SANDSTONE_WALL", "Red Sandstone Wall", "Красный песчаник: стена", "", "wall", "", 0, "RED_SANDSTONE", "b"),
            new BlockRow("BRICK_WALL", "Brick Wall", "Кирпич: стена", "", "wall", "", 0, "BRICK", "b"),
            new BlockRow("NETHER_BRICKS_WALL", "Nether Brick Wall", "Незер-кирпич: стена", "", "wall", "", 0, "NETHER_BRICKS", "b"),
            new BlockRow("RED_NETHER_BRICKS_WALL", "Red Nether Brick Wall", "Красн. незер-кирпич: стена", "", "wall", "", 0, "RED_NETHER_BRICKS", "b"),
            new BlockRow("BLACKSTONE_WALL", "Blackstone Wall", "Чёрный камень: стена", "", "wall", "", 0, "BLACKSTONE", "b"),
            new BlockRow("POLISHED_BLACKSTONE_WALL", "Polished Blackstone Wall", "Полир. чёрн. камень: стена", "", "wall", "", 0, "POLISHED_BLACKSTONE", "b"),
            new BlockRow("POLISHED_BLACKSTONE_BRICKS_WALL", "Polished Blackstone Brick Wall", "Кирпич чёрн. камня: стена", "", "wall", "", 0, "POLISHED_BLACKSTONE_BRICKS", "b"),
            new BlockRow("DEEPSLATE_BRICKS_WALL", "Deepslate Brick Wall", "Сланц. кирпич: стена", "", "wall", "", 0, "DEEPSLATE_BRICKS", "b"),
            new BlockRow("DEEPSLATE_TILES_WALL", "Deepslate Tile Wall", "Сланц. плитка: стена", "", "wall", "", 0, "DEEPSLATE_TILES", "b"),
            new BlockRow("COBBLED_DEEPSLATE_WALL", "Cobbled Deepslate Wall", "Булыжный сланец: стена", "", "wall", "", 0, "COBBLED_DEEPSLATE", "b"),
            new BlockRow("POLISHED_DEEPSLATE_WALL", "Polished Deepslate Wall", "Полир. сланец: стена", "", "wall", "", 0, "POLISHED_DEEPSLATE", "b"),
            new BlockRow("PRISMARINE_WALL", "Prismarine Wall", "Призмарин: стена", "", "wall", "", 0, "PRISMARINE", "b"),
            new BlockRow("END_STONE_BRICKS_WALL", "End Stone Brick Wall", "Кирпич Энда: стена", "", "wall", "", 0, "END_STONE_BRICKS", "b"),
            new BlockRow("MUD_BRICKS_WALL", "Mud Brick Wall", "Грязевой кирпич: стена", "", "wall", "", 0, "MUD_BRICKS", "b"),
            new BlockRow("TUFF_WALL", "Tuff Wall", "Туф: стена", "", "wall", "", 0, "TUFF", "b"),
            new BlockRow("DARK_PLANKS_FENCE", "Dark Oak Fence", "Тёмный дуб: забор", "", "fence", "", 0, "DARK_PLANKS", "f"),
            new BlockRow("DARK_PLANKS_GATE", "Dark Oak Fence Gate", "Тёмный дуб: калитка", "", "gate", "", 0, "DARK_PLANKS", "f"),
            new BlockRow("JUNGLE_PLANKS_FENCE", "Jungle Fence", "Тропич. дерево: забор", "", "fence", "", 0, "JUNGLE_PLANKS", "f"),
            new BlockRow("JUNGLE_PLANKS_GATE", "Jungle Fence Gate", "Тропич. дерево: калитка", "", "gate", "", 0, "JUNGLE_PLANKS", "f"),
            new BlockRow("ACACIA_PLANKS_FENCE", "Acacia Fence", "Акация: забор", "", "fence", "", 0, "ACACIA_PLANKS", "f"),
            new BlockRow("ACACIA_PLANKS_GATE", "Acacia Fence Gate", "Акация: калитка", "", "gate", "", 0, "ACACIA_PLANKS", "f"),
            new BlockRow("MANGROVE_PLANKS_FENCE", "Mangrove Fence", "Мангровое дерево: забор", "", "fence", "", 0, "MANGROVE_PLANKS", "f"),
            new BlockRow("MANGROVE_PLANKS_GATE", "Mangrove Fence Gate", "Мангровое дерево: калитка", "", "gate", "", 0, "MANGROVE_PLANKS", "f"),
            new BlockRow("CHERRY_PLANKS_FENCE", "Cherry Fence", "Вишня: забор", "", "fence", "", 0, "CHERRY_PLANKS", "f"),
            new BlockRow("CHERRY_PLANKS_GATE", "Cherry Fence Gate", "Вишня: калитка", "", "gate", "", 0, "CHERRY_PLANKS", "f"),
            new BlockRow("CRIMSON_PLANKS_FENCE", "Crimson Fence", "Багряное дерево: забор", "", "fence", "", 0, "CRIMSON_PLANKS", "f"),
            new BlockRow("CRIMSON_PLANKS_GATE", "Crimson Fence Gate", "Багряное дерево: калитка", "", "gate", "", 0, "CRIMSON_PLANKS", "f"),
            new BlockRow("WARPED_PLANKS_FENCE", "Warped Fence", "Искажённое дерево: забор", "", "fence", "", 0, "WARPED_PLANKS", "f"),
            new BlockRow("WARPED_PLANKS_GATE", "Warped Fence Gate", "Искажённое дерево: калитка", "", "gate", "", 0, "WARPED_PLANKS", "f"),
            new BlockRow("NETHER_BRICK_FENCE", "Nether Brick Fence", "Незер-кирпичный забор", "", "fence", "", 0, "NETHER_BRICKS", "f"),
            new BlockRow("WHITE_CARPET", "White Carpet", "Ковёр: белый", "", "carpet", "", 0, "WOOL_WHITE", "c"),
            new BlockRow("ORANGE_CARPET", "Orange Carpet", "Ковёр: оранжевый", "", "carpet", "", 0, "WOOL_ORANGE", "c"),
            new BlockRow("MAGENTA_CARPET", "Magenta Carpet", "Ковёр: сиреневый", "", "carpet", "", 0, "WOOL_MAGENTA", "c"),
            new BlockRow("LIGHT_BLUE_CARPET", "Light Blue Carpet", "Ковёр: голубой", "", "carpet", "", 0, "WOOL_LIGHT_BLUE", "c"),
            new BlockRow("YELLOW_CARPET", "Yellow Carpet", "Ковёр: жёлтый", "", "carpet", "", 0, "WOOL_YELLOW", "c"),
            new BlockRow("LIME_CARPET", "Lime Carpet", "Ковёр: лаймовый", "", "carpet", "", 0, "WOOL_LIME", "c"),
            new BlockRow("PINK_CARPET", "Pink Carpet", "Ковёр: розовый", "", "carpet", "", 0, "WOOL_PINK", "c"),
            new BlockRow("GRAY_CARPET", "Gray Carpet", "Ковёр: серый", "", "carpet", "", 0, "WOOL_GRAY", "c"),
            new BlockRow("LIGHT_GRAY_CARPET", "Light Gray Carpet", "Ковёр: светло-серый", "", "carpet", "", 0, "WOOL_LIGHT_GRAY", "c"),
            new BlockRow("CYAN_CARPET", "Cyan Carpet", "Ковёр: бирюзовый", "", "carpet", "", 0, "WOOL_CYAN", "c"),
            new BlockRow("PURPLE_CARPET", "Purple Carpet", "Ковёр: фиолетовый", "", "carpet", "", 0, "WOOL_PURPLE", "c"),
            new BlockRow("BLUE_CARPET", "Blue Carpet", "Ковёр: синий", "", "carpet", "", 0, "WOOL_BLUE", "c"),
            new BlockRow("BROWN_CARPET", "Brown Carpet", "Ковёр: коричневый", "", "carpet", "", 0, "WOOL_BROWN", "c"),
            new BlockRow("GREEN_CARPET", "Green Carpet", "Ковёр: зелёный", "", "carpet", "", 0, "WOOL_GREEN", "c"),
            new BlockRow("RED_CARPET", "Red Carpet", "Ковёр: красный", "", "carpet", "", 0, "WOOL_RED", "c"),
            new BlockRow("BLACK_CARPET", "Black Carpet", "Ковёр: чёрный", "", "carpet", "", 0, "WOOL_BLACK", "c"),
            new BlockRow("MOSS_CARPET", "Moss Carpet", "Моховой ковёр", "", "carpet", "", 0, "MOSS_BLOCK", "n"),
            new BlockRow("GLASS_PANE", "Glass Pane", "Стеклянная панель", "", "pane", "G", 0, "GLASS", "b"),
            new BlockRow("WHITE_STAINED_GLASS_PANE", "White Stained Glass Pane", "Стеклянная панель: белый", "", "pane", "T", 0, "WHITE_STAINED_GLASS", "c"),
            new BlockRow("ORANGE_STAINED_GLASS_PANE", "Orange Stained Glass Pane", "Стеклянная панель: оранжевый", "", "pane", "T", 0, "ORANGE_STAINED_GLASS", "c"),
            new BlockRow("MAGENTA_STAINED_GLASS_PANE", "Magenta Stained Glass Pane", "Стеклянная панель: сиреневый", "", "pane", "T", 0, "MAGENTA_STAINED_GLASS", "c"),
            new BlockRow("LIGHT_BLUE_STAINED_GLASS_PANE", "Light Blue Stained Glass Pane", "Стеклянная панель: голубой", "", "pane", "T", 0, "LIGHT_BLUE_STAINED_GLASS", "c"),
            new BlockRow("YELLOW_STAINED_GLASS_PANE", "Yellow Stained Glass Pane", "Стеклянная панель: жёлтый", "", "pane", "T", 0, "YELLOW_STAINED_GLASS", "c"),
            new BlockRow("LIME_STAINED_GLASS_PANE", "Lime Stained Glass Pane", "Стеклянная панель: лаймовый", "", "pane", "T", 0, "LIME_STAINED_GLASS", "c"),
            new BlockRow("PINK_STAINED_GLASS_PANE", "Pink Stained Glass Pane", "Стеклянная панель: розовый", "", "pane", "T", 0, "PINK_STAINED_GLASS", "c"),
            new BlockRow("GRAY_STAINED_GLASS_PANE", "Gray Stained Glass Pane", "Стеклянная панель: серый", "", "pane", "T", 0, "GRAY_STAINED_GLASS", "c"),
            new BlockRow("LIGHT_GRAY_STAINED_GLASS_PANE", "Light Gray Stained Glass Pane", "Стеклянная панель: светло-серый", "", "pane", "T", 0, "LIGHT_GRAY_STAINED_GLASS", "c"),
            new BlockRow("CYAN_STAINED_GLASS_PANE", "Cyan Stained Glass Pane", "Стеклянная панель: бирюзовый", "", "pane", "T", 0, "CYAN_STAINED_GLASS", "c"),
            new BlockRow("PURPLE_STAINED_GLASS_PANE", "Purple Stained Glass Pane", "Стеклянная панель: фиолетовый", "", "pane", "T", 0, "PURPLE_STAINED_GLASS", "c"),
            new BlockRow("BLUE_STAINED_GLASS_PANE", "Blue Stained Glass Pane", "Стеклянная панель: синий", "", "pane", "T", 0, "BLUE_STAINED_GLASS", "c"),
            new BlockRow("BROWN_STAINED_GLASS_PANE", "Brown Stained Glass Pane", "Стеклянная панель: коричневый", "", "pane", "T", 0, "BROWN_STAINED_GLASS", "c"),
            new BlockRow("GREEN_STAINED_GLASS_PANE", "Green Stained Glass Pane", "Стеклянная панель: зелёный", "", "pane", "T", 0, "GREEN_STAINED_GLASS", "c"),
            new BlockRow("RED_STAINED_GLASS_PANE", "Red Stained Glass Pane", "Стеклянная панель: красный", "", "pane", "T", 0, "RED_STAINED_GLASS", "c"),
            new BlockRow("BLACK_STAINED_GLASS_PANE", "Black Stained Glass Pane", "Стеклянная панель: чёрный", "", "pane", "T", 0, "BLACK_STAINED_GLASS", "c"),
            new BlockRow("IRON_BARS", "Iron Bars", "Решётка", "iron_bars", "pane", "G", 0, "", "b"),
            new BlockRow("STONE_PRESSURE_PLATE", "Stone Pressure Plate", "Нажимная плита: каменная", "", "plate", "", 0, "STONE", "f"),
            new BlockRow("POLISHED_BLACKSTONE_PRESSURE_PLATE", "Polished Blackstone Pressure Plate", "Нажимная плита: из чёрн. камня", "", "plate", "", 0, "POLISHED_BLACKSTONE", "f"),
            new BlockRow("PLANKS_PRESSURE_PLATE", "Oak Pressure Plate", "Нажимная плита: дубовая", "", "plate", "", 0, "PLANKS", "f"),
            new BlockRow("SPRUCE_PLANKS_PRESSURE_PLATE", "Spruce Pressure Plate", "Нажимная плита: еловая", "", "plate", "", 0, "SPRUCE_PLANKS", "f"),
            new BlockRow("BIRCH_PLANKS_PRESSURE_PLATE", "Birch Pressure Plate", "Нажимная плита: берёзовая", "", "plate", "", 0, "BIRCH_PLANKS", "f"),
            new BlockRow("DARK_PLANKS_PRESSURE_PLATE", "Dark Oak Pressure Plate", "Нажимная плита: тёмного дуба", "", "plate", "", 0, "DARK_PLANKS", "f"),
            new BlockRow("JUNGLE_PLANKS_PRESSURE_PLATE", "Jungle Pressure Plate", "Нажимная плита: тропич.", "", "plate", "", 0, "JUNGLE_PLANKS", "f"),
            new BlockRow("ACACIA_PLANKS_PRESSURE_PLATE", "Acacia Pressure Plate", "Нажимная плита: акации", "", "plate", "", 0, "ACACIA_PLANKS", "f"),
            new BlockRow("MANGROVE_PLANKS_PRESSURE_PLATE", "Mangrove Pressure Plate", "Нажимная плита: мангровая", "", "plate", "", 0, "MANGROVE_PLANKS", "f"),
            new BlockRow("CHERRY_PLANKS_PRESSURE_PLATE", "Cherry Pressure Plate", "Нажимная плита: вишнёвая", "", "plate", "", 0, "CHERRY_PLANKS", "f"),
            new BlockRow("CRIMSON_PLANKS_PRESSURE_PLATE", "Crimson Pressure Plate", "Нажимная плита: багровая", "", "plate", "", 0, "CRIMSON_PLANKS", "f"),
            new BlockRow("WARPED_PLANKS_PRESSURE_PLATE", "Warped Pressure Plate", "Нажимная плита: искажённая", "", "plate", "", 0, "WARPED_PLANKS", "f"),
            new BlockRow("POLISHED_TUFF_SLAB", "Polished Tuff Slab", "Полир. туф: плита", "", "slab", "", 0, "POLISHED_TUFF", "b"),
            new BlockRow("POLISHED_TUFF_STAIRS", "Polished Tuff Stairs", "Полир. туф: ступени", "", "stairs", "", 0, "POLISHED_TUFF", "b"),
            new BlockRow("POLISHED_TUFF_WALL", "Polished Tuff Wall", "Полир. туф: стена", "", "wall", "", 0, "POLISHED_TUFF", "b"),
            new BlockRow("TUFF_BRICKS_SLAB", "Tuff Brick Slab", "Туфовый кирпич: плита", "", "slab", "", 0, "TUFF_BRICKS", "b"),
            new BlockRow("TUFF_BRICKS_STAIRS", "Tuff Brick Stairs", "Туфовый кирпич: ступени", "", "stairs", "", 0, "TUFF_BRICKS", "b"),
            new BlockRow("TUFF_BRICKS_WALL", "Tuff Brick Wall", "Туфовый кирпич: стена", "", "wall", "", 0, "TUFF_BRICKS", "b"),
            new BlockRow("SMOOTH_QUARTZ_SLAB", "Smooth Quartz Slab", "Гладкий кварц: плита", "", "slab", "", 0, "SMOOTH_QUARTZ", "b"),
            new BlockRow("SMOOTH_QUARTZ_STAIRS", "Smooth Quartz Stairs", "Гладкий кварц: ступени", "", "stairs", "", 0, "SMOOTH_QUARTZ", "b"),
            new BlockRow("SMOOTH_SANDSTONE_SLAB", "Smooth Sandstone Slab", "Гладкий песчаник: плита", "", "slab", "", 0, "SMOOTH_SANDSTONE", "b"),
            new BlockRow("SMOOTH_SANDSTONE_STAIRS", "Smooth Sandstone Stairs", "Гладкий песчаник: ступени", "", "stairs", "", 0, "SMOOTH_SANDSTONE", "b"),
            new BlockRow("SMOOTH_RED_SANDSTONE_SLAB", "Smooth Red Sandstone Slab", "Гладкий кр. песчаник: плита", "", "slab", "", 0, "SMOOTH_RED_SANDSTONE", "b"),
            new BlockRow("SMOOTH_RED_SANDSTONE_STAIRS", "Smooth Red Sandstone Stairs", "Гладкий кр. песчаник: ступени", "", "stairs", "", 0, "SMOOTH_RED_SANDSTONE", "b"),
            new BlockRow("STONE_BUTTON", "Stone Button", "Кнопка: каменная", "", "button", "", 0, "STONE", "f"),
            new BlockRow("POLISHED_BLACKSTONE_BUTTON", "Polished Blackstone Button", "Кнопка: из чёрн. камня", "", "button", "", 0, "POLISHED_BLACKSTONE", "f"),
            new BlockRow("PLANKS_BUTTON", "Oak Button", "Кнопка: дубовая", "", "button", "", 0, "PLANKS", "f"),
            new BlockRow("SPRUCE_PLANKS_BUTTON", "Spruce Button", "Кнопка: еловая", "", "button", "", 0, "SPRUCE_PLANKS", "f"),
            new BlockRow("BIRCH_PLANKS_BUTTON", "Birch Button", "Кнопка: берёзовая", "", "button", "", 0, "BIRCH_PLANKS", "f"),
            new BlockRow("DARK_PLANKS_BUTTON", "Dark Oak Button", "Кнопка: тёмного дуба", "", "button", "", 0, "DARK_PLANKS", "f"),
            new BlockRow("JUNGLE_PLANKS_BUTTON", "Jungle Button", "Кнопка: тропич.", "", "button", "", 0, "JUNGLE_PLANKS", "f"),
            new BlockRow("ACACIA_PLANKS_BUTTON", "Acacia Button", "Кнопка: акациевая", "", "button", "", 0, "ACACIA_PLANKS", "f"),
            new BlockRow("MANGROVE_PLANKS_BUTTON", "Mangrove Button", "Кнопка: мангровая", "", "button", "", 0, "MANGROVE_PLANKS", "f"),
            new BlockRow("CHERRY_PLANKS_BUTTON", "Cherry Button", "Кнопка: вишнёвая", "", "button", "", 0, "CHERRY_PLANKS", "f"),
            new BlockRow("CRIMSON_PLANKS_BUTTON", "Crimson Button", "Кнопка: багровая", "", "button", "", 0, "CRIMSON_PLANKS", "f"),
            new BlockRow("WARPED_PLANKS_BUTTON", "Warped Button", "Кнопка: искажённая", "", "button", "", 0, "WARPED_PLANKS", "f"),
            new BlockRow("ACACIA_DOOR", "Acacia Door", "Дверь: акациевая", "acacia_door_top,acacia_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("JUNGLE_DOOR", "Jungle Door", "Дверь: тропическая", "jungle_door_top,jungle_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("DARK_OAK_DOOR", "Dark Oak Door", "Дверь: из тёмного дуба", "dark_oak_door_top,dark_oak_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("CRIMSON_DOOR", "Crimson Door", "Дверь: багровая", "crimson_door_top,crimson_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("WARPED_DOOR", "Warped Door", "Дверь: искажённая", "warped_door_top,warped_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("BAMBOO_DOOR", "Bamboo Door", "Дверь: бамбуковая", "bamboo_door_top,bamboo_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("COPPER_DOOR", "Copper Door", "Дверь: медная", "copper_door_top,copper_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("IRON_DOOR", "Iron Door", "Дверь: железная", "iron_door_top,iron_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("MANGROVE_DOOR", "Mangrove Door", "Дверь: мангровая", "mangrove_door_top,mangrove_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("CHERRY_DOOR", "Cherry Door", "Дверь: вишнёвая", "cherry_door_top,cherry_door_bottom", "door", "", 0, "", "f"),
            new BlockRow("ACACIA_TRAPDOOR", "Acacia Trapdoor", "Люк: акациевый", "acacia_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("JUNGLE_TRAPDOOR", "Jungle Trapdoor", "Люк: тропический", "jungle_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("DARK_OAK_TRAPDOOR", "Dark Oak Trapdoor", "Люк: из тёмного дуба", "dark_oak_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("CRIMSON_TRAPDOOR", "Crimson Trapdoor", "Люк: багровый", "crimson_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("WARPED_TRAPDOOR", "Warped Trapdoor", "Люк: искажённый", "warped_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("IRON_TRAPDOOR", "Iron Trapdoor", "Люк: железный", "iron_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("MANGROVE_TRAPDOOR", "Mangrove Trapdoor", "Люк: мангровый", "mangrove_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("CHERRY_TRAPDOOR", "Cherry Trapdoor", "Люк: вишнёвый", "cherry_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("BAMBOO_TRAPDOOR", "Bamboo Trapdoor", "Люк: бамбуковый", "bamboo_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("COPPER_TRAPDOOR", "Copper Trapdoor", "Люк: медный", "copper_trapdoor", "trapdoor", "", 0, "", "f"),
            new BlockRow("SUNFLOWER", "Sunflower", "Подсолнух", "sunflower_front,sunflower_bottom", "tallplant", "", 0, "", "n"),
            new BlockRow("LILAC", "Lilac", "Сирень", "lilac_top,lilac_bottom", "tallplant", "", 0, "", "n"),
            new BlockRow("ROSE_BUSH", "Rose Bush", "Розовый куст", "rose_bush_top,rose_bush_bottom", "tallplant", "", 0, "", "n"),
            new BlockRow("PEONY", "Peony", "Пион", "peony_top,peony_bottom", "tallplant", "", 0, "", "n"),
            new BlockRow("TALL_GRASS_PLANT", "Tall Grass", "Высокая трава", "tall_grass_top,tall_grass_bottom", "tallplant", "", 0, "", "n"),
            new BlockRow("LARGE_FERN", "Large Fern", "Большой папоротник", "large_fern_top,large_fern_bottom", "tallplant", "", 0, "", "n"),
            new BlockRow("VINE", "Vines", "Лианы", "vine", "vine", "", 0, "", "n"),
            new BlockRow("GLOW_LICHEN", "Glow Lichen", "Светящийся лишайник", "glow_lichen", "lichen", "", 7, "", "n"),
            new BlockRow("LILY_PAD", "Lily Pad", "Кувшинка", "lily_pad", "lilypad", "", 0, "", "n"),
            new BlockRow("SEA_PICKLE", "Sea Pickle", "Морской огурец", "sea_pickle", "seapickle", "A", 0, "", "n"),
            new BlockRow("BAMBOO_PLANT", "Bamboo", "Бамбук", "bamboo_stalk", "bamboo", "", 0, "", "n"),
            new BlockRow("BED", "Red Bed", "Кровать: красная", "bed_foot_top,bed_side", "bed", "", 0, "", "f"),
            new BlockRow("BED_WHITE", "White Bed", "Кровать: белый", "bed_foot_top_white,bed_side_white", "bed", "", 0, "", "f"),
            new BlockRow("BED_ORANGE", "Orange Bed", "Кровать: оранжевый", "bed_foot_top_orange,bed_side_orange", "bed", "", 0, "", "f"),
            new BlockRow("BED_MAGENTA", "Magenta Bed", "Кровать: сиреневый", "bed_foot_top_magenta,bed_side_magenta", "bed", "", 0, "", "f"),
            new BlockRow("BED_LIGHT_BLUE", "Light Blue Bed", "Кровать: голубой", "bed_foot_top_light_blue,bed_side_light_blue", "bed", "", 0, "", "f"),
            new BlockRow("BED_YELLOW", "Yellow Bed", "Кровать: жёлтый", "bed_foot_top_yellow,bed_side_yellow", "bed", "", 0, "", "f"),
            new BlockRow("BED_LIME", "Lime Bed", "Кровать: лаймовый", "bed_foot_top_lime,bed_side_lime", "bed", "", 0, "", "f"),
            new BlockRow("BED_PINK", "Pink Bed", "Кровать: розовый", "bed_foot_top_pink,bed_side_pink", "bed", "", 0, "", "f"),
            new BlockRow("BED_GRAY", "Gray Bed", "Кровать: серый", "bed_foot_top_gray,bed_side_gray", "bed", "", 0, "", "f"),
            new BlockRow("BED_LIGHT_GRAY", "Light Gray Bed", "Кровать: светло-серый", "bed_foot_top_light_gray,bed_side_light_gray", "bed", "", 0, "", "f"),
            new BlockRow("BED_CYAN", "Cyan Bed", "Кровать: бирюзовый", "bed_foot_top_cyan,bed_side_cyan", "bed", "", 0, "", "f"),
            new BlockRow("BED_PURPLE", "Purple Bed", "Кровать: фиолетовый", "bed_foot_top_purple,bed_side_purple", "bed", "", 0, "", "f"),
            new BlockRow("BED_BLUE", "Blue Bed", "Кровать: синий", "bed_foot_top_blue,bed_side_blue", "bed", "", 0, "", "f"),
            new BlockRow("BED_BROWN", "Brown Bed", "Кровать: коричневый", "bed_foot_top_brown,bed_side_brown", "bed", "", 0, "", "f"),
            new BlockRow("BED_GREEN", "Green Bed", "Кровать: зелёный", "bed_foot_top_green,bed_side_green", "bed", "", 0, "", "f"),
            new BlockRow("BED_BLACK", "Black Bed", "Кровать: чёрный", "bed_foot_top_black,bed_side_black", "bed", "", 0, "", "f"),
            new BlockRow("CHEST", "Chest", "Сундук", "chest_top,chest_side,chest_top,chest_front", "chest", "O", 0, "", "f"),
            new BlockRow("FARMLAND", "Farmland", "Грядка", "farmland,dirt,dirt", "farmland", "", 0, "", "n"),
            new BlockRow("WHEAT_0", "Wheat", "Пшеница", "wheat_stage0", "x", "", 0, "", "n"),
            new BlockRow("WHEAT_1", "Wheat", "Пшеница", "wheat_stage2", "x", "", 0, "", "n"),
            new BlockRow("WHEAT_2", "Wheat", "Пшеница", "wheat_stage4", "x", "", 0, "", "n"),
            new BlockRow("WHEAT_3", "Wheat", "Пшеница", "wheat_stage7", "x", "", 0, "", "n"),
            new BlockRow("LAVA", "Lava", "Лава", "lava_still", "lava", "e", 14, "", "n"),
            new BlockRow("FURNACE_LIT", "Furnace", "Печь", "furnace_top,furnace_front_on,furnace_top", "", "eO", 13, "", "b"),
            new BlockRow("FARMLAND_MOIST", "Farmland", "Грядка (влажная)", "farmland_moist,dirt,dirt", "farmland", "", 0, "", "b"),
            new BlockRow("CARROTS_0", "Carrots", "Морковь", "carrots_stage0", "x", "", 0, "", "n"),
            new BlockRow("CARROTS_1", "Carrots", "Морковь", "carrots_stage1", "x", "", 0, "", "n"),
            new BlockRow("CARROTS_2", "Carrots", "Морковь", "carrots_stage2", "x", "", 0, "", "n"),
            new BlockRow("CARROTS_3", "Carrots", "Морковь", "carrots_stage3", "x", "", 0, "", "n"),
            new BlockRow("POTATOES_0", "Potatoes", "Картофель", "potatoes_stage0", "x", "", 0, "", "n"),
            new BlockRow("POTATOES_1", "Potatoes", "Картофель", "potatoes_stage1", "x", "", 0, "", "n"),
            new BlockRow("POTATOES_2", "Potatoes", "Картофель", "potatoes_stage2", "x", "", 0, "", "n"),
            new BlockRow("POTATOES_3", "Potatoes", "Картофель", "potatoes_stage3", "x", "", 0, "", "n"),
            new BlockRow("FIRE", "Fire", "Огонь", "fire", "fire", "e", 15, "", "n"),
            new BlockRow("PUMPKIN_STEM_0", "Pumpkin Stem", "Стебель тыквы", "pumpkin_stem_stage0", "x", "", 0, "", "n"),
            new BlockRow("PUMPKIN_STEM_1", "Pumpkin Stem", "Стебель тыквы", "pumpkin_stem_stage1", "x", "", 0, "", "n"),
            new BlockRow("PUMPKIN_STEM_2", "Pumpkin Stem", "Стебель тыквы", "pumpkin_stem_stage2", "x", "", 0, "", "n"),
            new BlockRow("PUMPKIN_STEM_3", "Pumpkin Stem", "Стебель тыквы", "pumpkin_stem_stage3", "x", "", 0, "", "n"),
            new BlockRow("MELON_STEM_0", "Melon Stem", "Стебель арбуза", "melon_stem_stage0", "x", "", 0, "", "n"),
            new BlockRow("MELON_STEM_1", "Melon Stem", "Стебель арбуза", "melon_stem_stage1", "x", "", 0, "", "n"),
            new BlockRow("MELON_STEM_2", "Melon Stem", "Стебель арбуза", "melon_stem_stage2", "x", "", 0, "", "n"),
            new BlockRow("MELON_STEM_3", "Melon Stem", "Стебель арбуза", "melon_stem_stage3", "x", "", 0, "", "n"),
            new BlockRow("FLOWER_POT", "Flower Pot", "Цветочный горшок", "flower_pot", "pot", "", 0, "", "f"),
            new BlockRow("RAIL", "Rail", "Рельсы", "rail", "rail", "", 0, "", "f"),
            new BlockRow("COBWEB", "Cobweb", "Паутина", "cobweb", "x", "", 0, "", "n"),
            new BlockRow("SNOW_LAYER", "Snow", "Слой снега", "snow", "snowlayer", "", 0, "", "n"),
            new BlockRow("SNOWY_GRASS", "Snowy Grass Block", "Заснеженный дёрн", "snow,grass_block_snow,dirt", "", "", 0, "", "n"),
            new BlockRow("MANGROVE_LOG", "Mangrove Log", "Мангровое бревно", "mangrove_log_top,mangrove_log", "", "Y", 0, "", "n"),
            new BlockRow("MANGROVE_LEAVES", "Mangrove Leaves", "Мангровая листва", "mangrove_leaves", "", "L", 0, "", "n"),
            new BlockRow("MANGROVE_ROOTS", "Mangrove Roots", "Мангровые корни", "mangrove_roots_top,mangrove_roots", "", "G", 0, "", "n"),
            new BlockRow("MANGROVE_ROOTS_WET", "Mangrove Roots", "Мангровые корни", "mangrove_roots_top,mangrove_roots", "", "GA", 0, "MANGROVE_ROOTS", "n"),
            new BlockRow("MUDDY_MANGROVE_ROOTS", "Muddy Mangrove Roots", "Грязные мангровые корни", "muddy_mangrove_roots_top,muddy_mangrove_roots_side", "", "Y", 0, "", "n"),
            new BlockRow("MANGROVE_PROPAGULE", "Mangrove Propagule", "Мангровая пропагула", "mangrove_propagule_hanging", "x", "h", 0, "", "n"),
            new BlockRow("SMALL_AMETHYST_BUD", "Small Amethyst Bud", "Маленький аметистовый бутон", "small_amethyst_bud", "crystal", "", 1, "", "n"),
            new BlockRow("MEDIUM_AMETHYST_BUD", "Medium Amethyst Bud", "Средний аметистовый бутон", "medium_amethyst_bud", "crystal", "", 2, "", "n"),
            new BlockRow("LARGE_AMETHYST_BUD", "Large Amethyst Bud", "Большой аметистовый бутон", "large_amethyst_bud", "crystal", "", 4, "", "n"),
            new BlockRow("AMETHYST_CLUSTER", "Amethyst Cluster", "Друза аметиста", "amethyst_cluster", "crystal", "", 5, "", "n"),
            new BlockRow("CHERRY_LOG", "Cherry Log", "Вишнёвое бревно", "cherry_log_top,cherry_log", "", "Y", 0, "", "n"),
            new BlockRow("CHERRY_LEAVES", "Cherry Leaves", "Вишнёвая листва", "cherry_leaves", "", "L", 0, "", "n"),
            new BlockRow("SPAWNER", "Monster Spawner", "Рассадник монстров", "spawner", "", "G", 0, "", "n"),
            new BlockRow("REDSTONE_TORCH", "Redstone Torch", "Красный факел", "redstone_torch", "torch", "", 7, "", "f"),
            new BlockRow("BARREL", "Barrel", "Бочка", "barrel_top,barrel_side,barrel_bottom", "model", "", 0, "", "f"),
            new BlockRow("BLAST_FURNACE", "Blast Furnace", "Плавильная печь", "blast_furnace_top,blast_furnace_side,blast_furnace_top,blast_furnace_front", "", "O", 0, "", "f"),
            new BlockRow("SMOKER", "Smoker", "Коптильня", "smoker_top,smoker_side,smoker_bottom,smoker_front", "", "O", 0, "", "f"),
            new BlockRow("CARTOGRAPHY_TABLE", "Cartography Table", "Стол картографа", "cartography_table_top,cartography_table_side3,dark_oak_planks,cartography_table_side1", "", "O", 0, "", "f"),
            new BlockRow("FLETCHING_TABLE", "Fletching Table", "Стол лучника", "fletching_table_top,fletching_table_side,birch_planks,fletching_table_front", "", "O", 0, "", "f"),
            new BlockRow("SMITHING_TABLE", "Smithing Table", "Кузнечный стол", "smithing_table_top,smithing_table_side,smithing_table_bottom,smithing_table_front", "", "O", 0, "", "f"),
            new BlockRow("LOOM", "Loom", "Ткацкий станок", "loom_top,loom_side,loom_bottom,loom_front", "", "O", 0, "", "f"),
            new BlockRow("LECTERN", "Lectern", "Кафедра", "lectern_top,lectern_sides,oak_planks", "model", "", 0, "", "f"),
            new BlockRow("GRINDSTONE", "Grindstone", "Точило", "grindstone_side", "model", "", 0, "", "f"),
            new BlockRow("STONECUTTER", "Stonecutter", "Камнерез", "stonecutter_top,stonecutter_side,stonecutter_bottom", "model", "", 0, "", "f"),
            new BlockRow("BELL", "Bell", "Колокол", "bell_body", "model", "", 0, "", "f"),
            new BlockRow("LANTERN", "Lantern", "Фонарь", "lantern", "model", "e", 15, "", "f"),
            new BlockRow("CAMPFIRE", "Campfire", "Костёр", "campfire_log", "model", "", 15, "", "f"),
            new BlockRow("BREWING_STAND", "Brewing Stand", "Варочная стойка", "brewing_stand", "model", "", 1, "", "f"),
            new BlockRow("CAULDRON", "Cauldron", "Котёл", "cauldron_top,cauldron_side,cauldron_bottom", "model", "", 0, "", "f"),
            new BlockRow("COMPOSTER", "Composter", "Компостер", "composter_top,composter_side,composter_bottom", "model", "", 0, "", "f"),
            new BlockRow("BEETROOTS_0", "Beetroots", "Свёкла", "beetroots_stage0", "x", "", 0, "", "n"),
            new BlockRow("BEETROOTS_1", "Beetroots", "Свёкла", "beetroots_stage1", "x", "", 0, "", "n"),
            new BlockRow("BEETROOTS_2", "Beetroots", "Свёкла", "beetroots_stage2", "x", "", 0, "", "n"),
            new BlockRow("BEETROOTS_3", "Beetroots", "Свёкла", "beetroots_stage3", "x", "", 0, "", "n"),
            new BlockRow("POINTED_DRIPSTONE", "Pointed Dripstone", "Острый капельник", "pointed_dripstone_down_tip", "model", "", 0, "", "n"),
            new BlockRow("AZALEA", "Azalea", "Азалия", "azalea_side", "model", "", 0, "", "n"),
            new BlockRow("FLOWERING_AZALEA", "Flowering Azalea", "Цветущая азалия", "flowering_azalea_side", "model", "", 0, "", "n"),
            new BlockRow("BIG_DRIPLEAF", "Big Dripleaf", "Большой капельный лист", "big_dripleaf_top", "model", "", 0, "", "n"),
            new BlockRow("BIG_DRIPLEAF_STEM", "Big Dripleaf Stem", "Стебель капельного листа", "big_dripleaf_stem", "model", "", 0, "", "n"),
            new BlockRow("SMALL_DRIPLEAF", "Small Dripleaf", "Малый капельный лист", "small_dripleaf_top", "model", "", 0, "", "n"),
            new BlockRow("SPORE_BLOSSOM", "Spore Blossom", "Споровый цветок", "spore_blossom", "model", "h", 0, "", "n"),
            new BlockRow("HANGING_ROOTS", "Hanging Roots", "Свисающие корни", "hanging_roots", "x", "h", 0, "", "n"),
            new BlockRow("SCULK", "Sculk", "Скалк", "sculk", "", "", 0, "", "n"),
            new BlockRow("SCULK_VEIN", "Sculk Vein", "Скалковая жила", "sculk_vein", "lichen", "", 0, "", "n"),
            new BlockRow("SCULK_SENSOR", "Sculk Sensor", "Скалковый сенсор", "sculk_sensor_side", "model", "", 1, "", "n"),
            new BlockRow("SCULK_SHRIEKER", "Sculk Shrieker", "Скалковый крикун", "sculk_shrieker_side", "model", "", 0, "", "n"),
            new BlockRow("SCULK_CATALYST", "Sculk Catalyst", "Скалковый катализатор", "sculk_catalyst_top,sculk_catalyst_side,sculk_catalyst_bottom", "", "", 6, "", "n"),
            new BlockRow("PALE_OAK_LOG", "Pale Oak Log", "Бревно бледного дуба", "pale_oak_log_top,pale_oak_log", "", "Y", 0, "", "n"),
            new BlockRow("PALE_OAK_LEAVES", "Pale Oak Leaves", "Листва бледного дуба", "pale_oak_leaves", "", "L", 0, "", "n"),
            new BlockRow("PALE_OAK_PLANKS", "Pale Oak Planks", "Доски бледного дуба", "pale_oak_planks", "", "", 0, "", "b"),
            new BlockRow("PALE_MOSS_BLOCK", "Pale Moss Block", "Блок бледного мха", "pale_moss_block", "", "", 0, "", "n"),
            new BlockRow("PALE_MOSS_CARPET", "Pale Moss Carpet", "Ковёр бледного мха", "pale_moss_carpet", "carpet", "", 0, "", "n"),
            new BlockRow("PALE_HANGING_MOSS", "Pale Hanging Moss", "Свисающий бледный мох", "pale_hanging_moss", "x", "h", 0, "", "n"),
            new BlockRow("PALE_HANGING_MOSS_TIP", "Pale Hanging Moss", "Свисающий бледный мох", "pale_hanging_moss_tip", "x", "h", 0, "", "n"),
            new BlockRow("OPEN_EYEBLOSSOM", "Open Eyeblossom", "Открытый глазоцвет", "open_eyeblossom", "x", "", 0, "", "n"),
            new BlockRow("CLOSED_EYEBLOSSOM", "Closed Eyeblossom", "Закрытый глазоцвет", "closed_eyeblossom", "x", "", 0, "", "n"),
            new BlockRow("CREAKING_HEART", "Creaking Heart", "Сердце скрипуна", "creaking_heart_top,creaking_heart", "", "Y", 0, "", "n"),
            new BlockRow("SHIP_WHEEL", "Ship Wheel", "Штурвал", "spruce_planks", "model", "O", 0, "", "f"),
            new BlockRow("COCOA", "Cocoa", "Какао", "cocoa_stage2", "model", "", 0, "", ""),
            new BlockRow("REDSTONE_WIRE", "Redstone Wire", "Красная пыль", "redstone_dust_dot", "wire", "", 0, "", ""),
            new BlockRow("REDSTONE_TORCH_OFF", "Redstone Torch", "Красный факел", "redstone_torch_off", "torch", "", 0, "", ""),
            new BlockRow("LEVER", "Lever", "Рычаг", "cobblestone", "model", "", 0, "", "f"),
            new BlockRow("REDSTONE_LAMP", "Redstone Lamp", "Лампа", "redstone_lamp", "", "", 0, "", "f"),
            new BlockRow("REDSTONE_LAMP_ON", "Redstone Lamp", "Лампа", "redstone_lamp_on", "", "", 15, "", ""),
            new BlockRow("REPEATER", "Redstone Repeater", "Повторитель", "repeater", "model", "", 0, "", "f"),
            new BlockRow("COMPARATOR", "Redstone Comparator", "Компаратор", "comparator", "model", "", 0, "", "f"),
            new BlockRow("OBSERVER", "Observer", "Наблюдатель", "observer_side", "model", "", 0, "", "f"),
            new BlockRow("PISTON", "Piston", "Поршень", "piston_side", "model", "", 0, "", "f"),
            new BlockRow("STICKY_PISTON", "Sticky Piston", "Липкий поршень", "piston_side", "model", "", 0, "", "f"),
            new BlockRow("PISTON_HEAD", "Piston Head", "Головка поршня", "piston_top", "model", "", 0, "", ""),
        };
    }

    /// <summary>Block ids (B in the original): index in BLOCK_TABLE + 1, AIR = 0.</summary>
    public static class B
    {
        public const int AIR = 0;
        public const int GRASS = 1;
        public const int DIRT = 2;
        public const int STONE = 3;
        public const int COBBLE = 4;
        public const int SAND = 5;
        public const int GRAVEL = 6;
        public const int LOG = 7;
        public const int LEAVES = 8;
        public const int PLANKS = 9;
        public const int GLASS = 10;
        public const int WATER = 11;
        public const int BRICK = 12;
        public const int SNOW = 13;
        public const int ICE = 14;
        public const int SANDSTONE = 15;
        public const int BEDROCK = 16;
        public const int COAL = 17;
        public const int IRON = 18;
        public const int GOLD = 19;
        public const int DIAMOND = 20;
        public const int MOSSY = 21;
        public const int OBSIDIAN = 22;
        public const int GLOW = 23;
        public const int BOOKSHELF = 24;
        public const int TNT = 25;
        public const int WOOL_WHITE = 26;
        public const int WOOL_RED = 27;
        public const int WOOL_ORANGE = 28;
        public const int WOOL_YELLOW = 29;
        public const int WOOL_GREEN = 30;
        public const int WOOL_BLUE = 31;
        public const int WOOL_PURPLE = 32;
        public const int WOOL_BLACK = 33;
        public const int BIRCH_LOG = 34;
        public const int BIRCH_LEAVES = 35;
        public const int SPRUCE_LOG = 36;
        public const int SPRUCE_LEAVES = 37;
        public const int BIRCH_PLANKS = 38;
        public const int SPRUCE_PLANKS = 39;
        public const int CACTUS = 40;
        public const int TALL_GRASS = 41;
        public const int DANDELION = 42;
        public const int POPPY = 43;
        public const int DEAD_BUSH = 44;
        public const int DARK_LOG = 45;
        public const int DARK_LEAVES = 46;
        public const int JUNGLE_LOG = 47;
        public const int JUNGLE_LEAVES = 48;
        public const int ACACIA_LOG = 49;
        public const int ACACIA_LEAVES = 50;
        public const int PODZOL = 51;
        public const int RED_SAND = 52;
        public const int TERRACOTTA = 53;
        public const int TERRA_ORANGE = 54;
        public const int TERRA_WHITE = 55;
        public const int FERN = 56;
        public const int ANDESITE = 57;
        public const int POLISHED_ANDESITE = 58;
        public const int DIORITE = 59;
        public const int POLISHED_DIORITE = 60;
        public const int GRANITE = 61;
        public const int POLISHED_GRANITE = 62;
        public const int STONE_BRICKS = 63;
        public const int MOSSY_STONE_BRICKS = 64;
        public const int CRACKED_STONE_BRICKS = 65;
        public const int CHISELED_STONE_BRICKS = 66;
        public const int SMOOTH_STONE = 67;
        public const int CALCITE = 68;
        public const int DEEPSLATE = 69;
        public const int COBBLED_DEEPSLATE = 70;
        public const int POLISHED_DEEPSLATE = 71;
        public const int DEEPSLATE_BRICKS = 72;
        public const int NETHERRACK = 73;
        public const int NETHER_BRICKS = 74;
        public const int RED_NETHER_BRICKS = 75;
        public const int BLACKSTONE = 76;
        public const int QUARTZ = 77;
        public const int QUARTZ_BRICKS = 78;
        public const int CHISELED_QUARTZ = 79;
        public const int PRISMARINE = 80;
        public const int PRISMARINE_BRICKS = 81;
        public const int DARK_PRISMARINE = 82;
        public const int END_STONE = 83;
        public const int END_STONE_BRICKS = 84;
        public const int PURPUR = 85;
        public const int PURPUR_PILLAR = 86;
        public const int CLAY = 87;
        public const int PACKED_ICE = 88;
        public const int BLUE_ICE = 89;
        public const int TUFF = 90;
        public const int WHITE_CONCRETE = 91;
        public const int ORANGE_CONCRETE = 92;
        public const int MAGENTA_CONCRETE = 93;
        public const int LIGHT_BLUE_CONCRETE = 94;
        public const int YELLOW_CONCRETE = 95;
        public const int LIME_CONCRETE = 96;
        public const int PINK_CONCRETE = 97;
        public const int GRAY_CONCRETE = 98;
        public const int LIGHT_GRAY_CONCRETE = 99;
        public const int CYAN_CONCRETE = 100;
        public const int PURPLE_CONCRETE = 101;
        public const int BLUE_CONCRETE = 102;
        public const int BROWN_CONCRETE = 103;
        public const int GREEN_CONCRETE = 104;
        public const int RED_CONCRETE = 105;
        public const int BLACK_CONCRETE = 106;
        public const int OAK_SLAB = 107;
        public const int SPRUCE_SLAB = 108;
        public const int BIRCH_SLAB = 109;
        public const int STONE_SLAB = 110;
        public const int COBBLE_SLAB = 111;
        public const int STONEBRICK_SLAB = 112;
        public const int OAK_STAIRS = 113;
        public const int SPRUCE_STAIRS = 114;
        public const int BIRCH_STAIRS = 115;
        public const int STONE_STAIRS = 116;
        public const int COBBLE_STAIRS = 117;
        public const int STONEBRICK_STAIRS = 118;
        public const int OAK_FENCE = 119;
        public const int SPRUCE_FENCE = 120;
        public const int BIRCH_FENCE = 121;
        public const int OAK_GATE = 122;
        public const int SPRUCE_GATE = 123;
        public const int BIRCH_GATE = 124;
        public const int OAK_DOOR = 125;
        public const int SPRUCE_DOOR = 126;
        public const int BIRCH_DOOR = 127;
        public const int OAK_TRAPDOOR = 128;
        public const int SPRUCE_TRAPDOOR = 129;
        public const int BIRCH_TRAPDOOR = 130;
        public const int LADDER = 131;
        public const int TORCH = 132;
        public const int WOOL_MAGENTA = 133;
        public const int WOOL_LIGHT_BLUE = 134;
        public const int WOOL_LIME = 135;
        public const int WOOL_PINK = 136;
        public const int WOOL_GRAY = 137;
        public const int WOOL_LIGHT_GRAY = 138;
        public const int WOOL_CYAN = 139;
        public const int WOOL_BROWN = 140;
        public const int WHITE_STAINED_GLASS = 141;
        public const int ORANGE_STAINED_GLASS = 142;
        public const int MAGENTA_STAINED_GLASS = 143;
        public const int LIGHT_BLUE_STAINED_GLASS = 144;
        public const int YELLOW_STAINED_GLASS = 145;
        public const int LIME_STAINED_GLASS = 146;
        public const int PINK_STAINED_GLASS = 147;
        public const int GRAY_STAINED_GLASS = 148;
        public const int LIGHT_GRAY_STAINED_GLASS = 149;
        public const int CYAN_STAINED_GLASS = 150;
        public const int PURPLE_STAINED_GLASS = 151;
        public const int BLUE_STAINED_GLASS = 152;
        public const int BROWN_STAINED_GLASS = 153;
        public const int GREEN_STAINED_GLASS = 154;
        public const int RED_STAINED_GLASS = 155;
        public const int BLACK_STAINED_GLASS = 156;
        public const int TINTED_GLASS = 157;
        public const int WHITE_CONCRETE_POWDER = 158;
        public const int ORANGE_CONCRETE_POWDER = 159;
        public const int MAGENTA_CONCRETE_POWDER = 160;
        public const int LIGHT_BLUE_CONCRETE_POWDER = 161;
        public const int YELLOW_CONCRETE_POWDER = 162;
        public const int LIME_CONCRETE_POWDER = 163;
        public const int PINK_CONCRETE_POWDER = 164;
        public const int GRAY_CONCRETE_POWDER = 165;
        public const int LIGHT_GRAY_CONCRETE_POWDER = 166;
        public const int CYAN_CONCRETE_POWDER = 167;
        public const int PURPLE_CONCRETE_POWDER = 168;
        public const int BLUE_CONCRETE_POWDER = 169;
        public const int BROWN_CONCRETE_POWDER = 170;
        public const int GREEN_CONCRETE_POWDER = 171;
        public const int RED_CONCRETE_POWDER = 172;
        public const int BLACK_CONCRETE_POWDER = 173;
        public const int WHITE_GLAZED = 174;
        public const int ORANGE_GLAZED = 175;
        public const int MAGENTA_GLAZED = 176;
        public const int LIGHT_BLUE_GLAZED = 177;
        public const int YELLOW_GLAZED = 178;
        public const int LIME_GLAZED = 179;
        public const int PINK_GLAZED = 180;
        public const int GRAY_GLAZED = 181;
        public const int LIGHT_GRAY_GLAZED = 182;
        public const int CYAN_GLAZED = 183;
        public const int PURPLE_GLAZED = 184;
        public const int BLUE_GLAZED = 185;
        public const int BROWN_GLAZED = 186;
        public const int GREEN_GLAZED = 187;
        public const int RED_GLAZED = 188;
        public const int BLACK_GLAZED = 189;
        public const int MAGENTA_TERRACOTTA = 190;
        public const int LIGHT_BLUE_TERRACOTTA = 191;
        public const int YELLOW_TERRACOTTA = 192;
        public const int LIME_TERRACOTTA = 193;
        public const int PINK_TERRACOTTA = 194;
        public const int GRAY_TERRACOTTA = 195;
        public const int LIGHT_GRAY_TERRACOTTA = 196;
        public const int CYAN_TERRACOTTA = 197;
        public const int PURPLE_TERRACOTTA = 198;
        public const int BLUE_TERRACOTTA = 199;
        public const int BROWN_TERRACOTTA = 200;
        public const int GREEN_TERRACOTTA = 201;
        public const int RED_TERRACOTTA = 202;
        public const int BLACK_TERRACOTTA = 203;
        public const int DARK_PLANKS = 204;
        public const int JUNGLE_PLANKS = 205;
        public const int ACACIA_PLANKS = 206;
        public const int MANGROVE_PLANKS = 207;
        public const int CHERRY_PLANKS = 208;
        public const int CRIMSON_PLANKS = 209;
        public const int WARPED_PLANKS = 210;
        public const int CUT_SANDSTONE = 211;
        public const int CHISELED_SANDSTONE = 212;
        public const int RED_SANDSTONE = 213;
        public const int CUT_RED_SANDSTONE = 214;
        public const int QUARTZ_PILLAR = 215;
        public const int BASALT = 216;
        public const int POLISHED_BASALT = 217;
        public const int SMOOTH_BASALT = 218;
        public const int DRIPSTONE = 219;
        public const int CHISELED_NETHER_BRICKS = 220;
        public const int CRACKED_NETHER_BRICKS = 221;
        public const int CHISELED_DEEPSLATE = 222;
        public const int CRACKED_DEEPSLATE_BRICKS = 223;
        public const int DEEPSLATE_TILES = 224;
        public const int POLISHED_BLACKSTONE = 225;
        public const int POLISHED_BLACKSTONE_BRICKS = 226;
        public const int CRACKED_POLISHED_BLACKSTONE_BRICKS = 227;
        public const int CHISELED_POLISHED_BLACKSTONE = 228;
        public const int GILDED_BLACKSTONE = 229;
        public const int MUD_BRICKS = 230;
        public const int COPPER_BLOCK = 231;
        public const int EXPOSED_COPPER = 232;
        public const int WEATHERED_COPPER = 233;
        public const int OXIDIZED_COPPER = 234;
        public const int CUT_COPPER = 235;
        public const int EXPOSED_CUT_COPPER = 236;
        public const int WEATHERED_CUT_COPPER = 237;
        public const int OXIDIZED_CUT_COPPER = 238;
        public const int STRIPPED_OAK_LOG = 239;
        public const int STRIPPED_BIRCH_LOG = 240;
        public const int STRIPPED_SPRUCE_LOG = 241;
        public const int STRIPPED_DARK_OAK_LOG = 242;
        public const int STRIPPED_JUNGLE_LOG = 243;
        public const int STRIPPED_ACACIA_LOG = 244;
        public const int CRIMSON_STEM = 245;
        public const int WARPED_STEM = 246;
        public const int BAMBOO_BLOCK = 247;
        public const int AZALEA_LEAVES = 248;
        public const int FLOWERING_AZALEA_LEAVES = 249;
        public const int REDSTONE_ORE = 250;
        public const int LAPIS_ORE = 251;
        public const int EMERALD_ORE = 252;
        public const int COPPER_ORE = 253;
        public const int NETHER_QUARTZ_ORE = 254;
        public const int NETHER_GOLD_ORE = 255;
        public const int DEEPSLATE_COAL_ORE = 256;
        public const int DEEPSLATE_IRON_ORE = 257;
        public const int DEEPSLATE_GOLD_ORE = 258;
        public const int DEEPSLATE_DIAMOND_ORE = 259;
        public const int DEEPSLATE_REDSTONE_ORE = 260;
        public const int DEEPSLATE_LAPIS_ORE = 261;
        public const int DEEPSLATE_EMERALD_ORE = 262;
        public const int DEEPSLATE_COPPER_ORE = 263;
        public const int ANCIENT_DEBRIS = 264;
        public const int REDSTONE_BLOCK = 265;
        public const int LAPIS_BLOCK = 266;
        public const int EMERALD_BLOCK = 267;
        public const int COAL_BLOCK = 268;
        public const int IRON_BLOCK = 269;
        public const int GOLD_BLOCK = 270;
        public const int DIAMOND_BLOCK = 271;
        public const int NETHERITE_BLOCK = 272;
        public const int RAW_IRON_BLOCK = 273;
        public const int RAW_COPPER_BLOCK = 274;
        public const int RAW_GOLD_BLOCK = 275;
        public const int AMETHYST_BLOCK = 276;
        public const int BUDDING_AMETHYST = 277;
        public const int COARSE_DIRT = 278;
        public const int ROOTED_DIRT = 279;
        public const int MUD = 280;
        public const int MYCELIUM = 281;
        public const int DIRT_PATH = 282;
        public const int BONE_BLOCK = 283;
        public const int MOSS_BLOCK = 284;
        public const int SOUL_SAND = 285;
        public const int SOUL_SOIL = 286;
        public const int MAGMA_BLOCK = 287;
        public const int SHROOMLIGHT = 288;
        public const int NETHER_WART_BLOCK = 289;
        public const int WARPED_WART_BLOCK = 290;
        public const int SEA_LANTERN = 291;
        public const int SPONGE = 292;
        public const int WET_SPONGE = 293;
        public const int DRIED_KELP_BLOCK = 294;
        public const int CRYING_OBSIDIAN = 295;
        public const int CRIMSON_NYLIUM = 296;
        public const int WARPED_NYLIUM = 297;
        public const int BROWN_MUSHROOM_BLOCK = 298;
        public const int RED_MUSHROOM_BLOCK = 299;
        public const int MUSHROOM_STEM = 300;
        public const int BRAIN_CORAL_BLOCK = 301;
        public const int DEAD_BRAIN_CORAL_BLOCK = 302;
        public const int BUBBLE_CORAL_BLOCK = 303;
        public const int DEAD_BUBBLE_CORAL_BLOCK = 304;
        public const int FIRE_CORAL_BLOCK = 305;
        public const int DEAD_FIRE_CORAL_BLOCK = 306;
        public const int HORN_CORAL_BLOCK = 307;
        public const int DEAD_HORN_CORAL_BLOCK = 308;
        public const int TUBE_CORAL_BLOCK = 309;
        public const int DEAD_TUBE_CORAL_BLOCK = 310;
        public const int PUMPKIN = 311;
        public const int CARVED_PUMPKIN = 312;
        public const int JACK_O_LANTERN = 313;
        public const int MELON = 314;
        public const int HAY_BLOCK = 315;
        public const int HONEYCOMB_BLOCK = 316;
        public const int OAK_WOOD = 317;
        public const int STRIPPED_OAK_WOOD = 318;
        public const int BIRCH_WOOD = 319;
        public const int STRIPPED_BIRCH_WOOD = 320;
        public const int SPRUCE_WOOD = 321;
        public const int STRIPPED_SPRUCE_WOOD = 322;
        public const int DARK_OAK_WOOD = 323;
        public const int STRIPPED_DARK_OAK_WOOD = 324;
        public const int JUNGLE_WOOD = 325;
        public const int STRIPPED_JUNGLE_WOOD = 326;
        public const int ACACIA_WOOD = 327;
        public const int STRIPPED_ACACIA_WOOD = 328;
        public const int CRIMSON_HYPHAE = 329;
        public const int WARPED_HYPHAE = 330;
        public const int STRIPPED_CRIMSON_STEM = 331;
        public const int STRIPPED_WARPED_STEM = 332;
        public const int STRIPPED_CRIMSON_HYPHAE = 333;
        public const int STRIPPED_WARPED_HYPHAE = 334;
        public const int SMOOTH_SANDSTONE = 335;
        public const int SMOOTH_RED_SANDSTONE = 336;
        public const int CHISELED_RED_SANDSTONE = 337;
        public const int SMOOTH_QUARTZ = 338;
        public const int POLISHED_TUFF = 339;
        public const int TUFF_BRICKS = 340;
        public const int CHISELED_TUFF = 341;
        public const int CHISELED_COPPER = 342;
        public const int EXPOSED_CHISELED_COPPER = 343;
        public const int WEATHERED_CHISELED_COPPER = 344;
        public const int OXIDIZED_CHISELED_COPPER = 345;
        public const int CRACKED_DEEPSLATE_TILES = 346;
        public const int ALLIUM = 347;
        public const int AZURE_BLUET = 348;
        public const int BLUE_ORCHID = 349;
        public const int CORNFLOWER = 350;
        public const int LILY_OF_THE_VALLEY = 351;
        public const int OXEYE_DAISY = 352;
        public const int ORANGE_TULIP = 353;
        public const int PINK_TULIP = 354;
        public const int RED_TULIP = 355;
        public const int WHITE_TULIP = 356;
        public const int WITHER_ROSE = 357;
        public const int BROWN_MUSHROOM = 358;
        public const int RED_MUSHROOM = 359;
        public const int CRIMSON_ROOTS = 360;
        public const int WARPED_ROOTS = 361;
        public const int NETHER_SPROUTS = 362;
        public const int CRIMSON_FUNGUS = 363;
        public const int WARPED_FUNGUS = 364;
        public const int OAK_SAPLING = 365;
        public const int SPRUCE_SAPLING = 366;
        public const int BIRCH_SAPLING = 367;
        public const int JUNGLE_SAPLING = 368;
        public const int ACACIA_SAPLING = 369;
        public const int DARK_OAK_SAPLING = 370;
        public const int SUGAR_CANE = 371;
        public const int KELP = 372;
        public const int SEAGRASS = 373;
        public const int SWEET_BERRY_BUSH = 374;
        public const int BRAIN_CORAL_FAN = 375;
        public const int DEAD_BRAIN_CORAL_FAN = 376;
        public const int BUBBLE_CORAL_FAN = 377;
        public const int DEAD_BUBBLE_CORAL_FAN = 378;
        public const int FIRE_CORAL_FAN = 379;
        public const int DEAD_FIRE_CORAL_FAN = 380;
        public const int HORN_CORAL_FAN = 381;
        public const int DEAD_HORN_CORAL_FAN = 382;
        public const int TUBE_CORAL_FAN = 383;
        public const int DEAD_TUBE_CORAL_FAN = 384;
        public const int CAVE_VINES = 385;
        public const int WEEPING_VINES = 386;
        public const int TWISTING_VINES = 387;
        public const int CRAFTING_TABLE = 388;
        public const int FURNACE = 389;
        public const int MOSSY_SLAB = 390;
        public const int MOSSY_STAIRS = 391;
        public const int ANDESITE_SLAB = 392;
        public const int ANDESITE_STAIRS = 393;
        public const int POLISHED_ANDESITE_SLAB = 394;
        public const int POLISHED_ANDESITE_STAIRS = 395;
        public const int DIORITE_SLAB = 396;
        public const int DIORITE_STAIRS = 397;
        public const int POLISHED_DIORITE_SLAB = 398;
        public const int POLISHED_DIORITE_STAIRS = 399;
        public const int GRANITE_SLAB = 400;
        public const int GRANITE_STAIRS = 401;
        public const int POLISHED_GRANITE_SLAB = 402;
        public const int POLISHED_GRANITE_STAIRS = 403;
        public const int MOSSY_STONE_BRICKS_SLAB = 404;
        public const int MOSSY_STONE_BRICKS_STAIRS = 405;
        public const int SMOOTH_STONE_SLAB = 406;
        public const int SMOOTH_STONE_STAIRS = 407;
        public const int SANDSTONE_SLAB = 408;
        public const int SANDSTONE_STAIRS = 409;
        public const int CUT_SANDSTONE_SLAB = 410;
        public const int CUT_SANDSTONE_STAIRS = 411;
        public const int RED_SANDSTONE_SLAB = 412;
        public const int RED_SANDSTONE_STAIRS = 413;
        public const int BRICK_SLAB = 414;
        public const int BRICK_STAIRS = 415;
        public const int NETHER_BRICKS_SLAB = 416;
        public const int NETHER_BRICKS_STAIRS = 417;
        public const int RED_NETHER_BRICKS_SLAB = 418;
        public const int RED_NETHER_BRICKS_STAIRS = 419;
        public const int QUARTZ_SLAB = 420;
        public const int QUARTZ_STAIRS = 421;
        public const int BLACKSTONE_SLAB = 422;
        public const int BLACKSTONE_STAIRS = 423;
        public const int POLISHED_BLACKSTONE_SLAB = 424;
        public const int POLISHED_BLACKSTONE_STAIRS = 425;
        public const int POLISHED_BLACKSTONE_BRICKS_SLAB = 426;
        public const int POLISHED_BLACKSTONE_BRICKS_STAIRS = 427;
        public const int DEEPSLATE_BRICKS_SLAB = 428;
        public const int DEEPSLATE_BRICKS_STAIRS = 429;
        public const int DEEPSLATE_TILES_SLAB = 430;
        public const int DEEPSLATE_TILES_STAIRS = 431;
        public const int COBBLED_DEEPSLATE_SLAB = 432;
        public const int COBBLED_DEEPSLATE_STAIRS = 433;
        public const int POLISHED_DEEPSLATE_SLAB = 434;
        public const int POLISHED_DEEPSLATE_STAIRS = 435;
        public const int PRISMARINE_SLAB = 436;
        public const int PRISMARINE_STAIRS = 437;
        public const int PRISMARINE_BRICKS_SLAB = 438;
        public const int PRISMARINE_BRICKS_STAIRS = 439;
        public const int DARK_PRISMARINE_SLAB = 440;
        public const int DARK_PRISMARINE_STAIRS = 441;
        public const int END_STONE_BRICKS_SLAB = 442;
        public const int END_STONE_BRICKS_STAIRS = 443;
        public const int PURPUR_SLAB = 444;
        public const int PURPUR_STAIRS = 445;
        public const int MUD_BRICKS_SLAB = 446;
        public const int MUD_BRICKS_STAIRS = 447;
        public const int CUT_COPPER_SLAB = 448;
        public const int CUT_COPPER_STAIRS = 449;
        public const int TUFF_SLAB = 450;
        public const int TUFF_STAIRS = 451;
        public const int DARK_PLANKS_SLAB = 452;
        public const int DARK_PLANKS_STAIRS = 453;
        public const int JUNGLE_PLANKS_SLAB = 454;
        public const int JUNGLE_PLANKS_STAIRS = 455;
        public const int ACACIA_PLANKS_SLAB = 456;
        public const int ACACIA_PLANKS_STAIRS = 457;
        public const int MANGROVE_PLANKS_SLAB = 458;
        public const int MANGROVE_PLANKS_STAIRS = 459;
        public const int CHERRY_PLANKS_SLAB = 460;
        public const int CHERRY_PLANKS_STAIRS = 461;
        public const int CRIMSON_PLANKS_SLAB = 462;
        public const int CRIMSON_PLANKS_STAIRS = 463;
        public const int WARPED_PLANKS_SLAB = 464;
        public const int WARPED_PLANKS_STAIRS = 465;
        public const int COBBLE_WALL = 466;
        public const int MOSSY_WALL = 467;
        public const int STONE_BRICKS_WALL = 468;
        public const int MOSSY_STONE_BRICKS_WALL = 469;
        public const int ANDESITE_WALL = 470;
        public const int DIORITE_WALL = 471;
        public const int GRANITE_WALL = 472;
        public const int SANDSTONE_WALL = 473;
        public const int RED_SANDSTONE_WALL = 474;
        public const int BRICK_WALL = 475;
        public const int NETHER_BRICKS_WALL = 476;
        public const int RED_NETHER_BRICKS_WALL = 477;
        public const int BLACKSTONE_WALL = 478;
        public const int POLISHED_BLACKSTONE_WALL = 479;
        public const int POLISHED_BLACKSTONE_BRICKS_WALL = 480;
        public const int DEEPSLATE_BRICKS_WALL = 481;
        public const int DEEPSLATE_TILES_WALL = 482;
        public const int COBBLED_DEEPSLATE_WALL = 483;
        public const int POLISHED_DEEPSLATE_WALL = 484;
        public const int PRISMARINE_WALL = 485;
        public const int END_STONE_BRICKS_WALL = 486;
        public const int MUD_BRICKS_WALL = 487;
        public const int TUFF_WALL = 488;
        public const int DARK_PLANKS_FENCE = 489;
        public const int DARK_PLANKS_GATE = 490;
        public const int JUNGLE_PLANKS_FENCE = 491;
        public const int JUNGLE_PLANKS_GATE = 492;
        public const int ACACIA_PLANKS_FENCE = 493;
        public const int ACACIA_PLANKS_GATE = 494;
        public const int MANGROVE_PLANKS_FENCE = 495;
        public const int MANGROVE_PLANKS_GATE = 496;
        public const int CHERRY_PLANKS_FENCE = 497;
        public const int CHERRY_PLANKS_GATE = 498;
        public const int CRIMSON_PLANKS_FENCE = 499;
        public const int CRIMSON_PLANKS_GATE = 500;
        public const int WARPED_PLANKS_FENCE = 501;
        public const int WARPED_PLANKS_GATE = 502;
        public const int NETHER_BRICK_FENCE = 503;
        public const int WHITE_CARPET = 504;
        public const int ORANGE_CARPET = 505;
        public const int MAGENTA_CARPET = 506;
        public const int LIGHT_BLUE_CARPET = 507;
        public const int YELLOW_CARPET = 508;
        public const int LIME_CARPET = 509;
        public const int PINK_CARPET = 510;
        public const int GRAY_CARPET = 511;
        public const int LIGHT_GRAY_CARPET = 512;
        public const int CYAN_CARPET = 513;
        public const int PURPLE_CARPET = 514;
        public const int BLUE_CARPET = 515;
        public const int BROWN_CARPET = 516;
        public const int GREEN_CARPET = 517;
        public const int RED_CARPET = 518;
        public const int BLACK_CARPET = 519;
        public const int MOSS_CARPET = 520;
        public const int GLASS_PANE = 521;
        public const int WHITE_STAINED_GLASS_PANE = 522;
        public const int ORANGE_STAINED_GLASS_PANE = 523;
        public const int MAGENTA_STAINED_GLASS_PANE = 524;
        public const int LIGHT_BLUE_STAINED_GLASS_PANE = 525;
        public const int YELLOW_STAINED_GLASS_PANE = 526;
        public const int LIME_STAINED_GLASS_PANE = 527;
        public const int PINK_STAINED_GLASS_PANE = 528;
        public const int GRAY_STAINED_GLASS_PANE = 529;
        public const int LIGHT_GRAY_STAINED_GLASS_PANE = 530;
        public const int CYAN_STAINED_GLASS_PANE = 531;
        public const int PURPLE_STAINED_GLASS_PANE = 532;
        public const int BLUE_STAINED_GLASS_PANE = 533;
        public const int BROWN_STAINED_GLASS_PANE = 534;
        public const int GREEN_STAINED_GLASS_PANE = 535;
        public const int RED_STAINED_GLASS_PANE = 536;
        public const int BLACK_STAINED_GLASS_PANE = 537;
        public const int IRON_BARS = 538;
        public const int STONE_PRESSURE_PLATE = 539;
        public const int POLISHED_BLACKSTONE_PRESSURE_PLATE = 540;
        public const int PLANKS_PRESSURE_PLATE = 541;
        public const int SPRUCE_PLANKS_PRESSURE_PLATE = 542;
        public const int BIRCH_PLANKS_PRESSURE_PLATE = 543;
        public const int DARK_PLANKS_PRESSURE_PLATE = 544;
        public const int JUNGLE_PLANKS_PRESSURE_PLATE = 545;
        public const int ACACIA_PLANKS_PRESSURE_PLATE = 546;
        public const int MANGROVE_PLANKS_PRESSURE_PLATE = 547;
        public const int CHERRY_PLANKS_PRESSURE_PLATE = 548;
        public const int CRIMSON_PLANKS_PRESSURE_PLATE = 549;
        public const int WARPED_PLANKS_PRESSURE_PLATE = 550;
        public const int POLISHED_TUFF_SLAB = 551;
        public const int POLISHED_TUFF_STAIRS = 552;
        public const int POLISHED_TUFF_WALL = 553;
        public const int TUFF_BRICKS_SLAB = 554;
        public const int TUFF_BRICKS_STAIRS = 555;
        public const int TUFF_BRICKS_WALL = 556;
        public const int SMOOTH_QUARTZ_SLAB = 557;
        public const int SMOOTH_QUARTZ_STAIRS = 558;
        public const int SMOOTH_SANDSTONE_SLAB = 559;
        public const int SMOOTH_SANDSTONE_STAIRS = 560;
        public const int SMOOTH_RED_SANDSTONE_SLAB = 561;
        public const int SMOOTH_RED_SANDSTONE_STAIRS = 562;
        public const int STONE_BUTTON = 563;
        public const int POLISHED_BLACKSTONE_BUTTON = 564;
        public const int PLANKS_BUTTON = 565;
        public const int SPRUCE_PLANKS_BUTTON = 566;
        public const int BIRCH_PLANKS_BUTTON = 567;
        public const int DARK_PLANKS_BUTTON = 568;
        public const int JUNGLE_PLANKS_BUTTON = 569;
        public const int ACACIA_PLANKS_BUTTON = 570;
        public const int MANGROVE_PLANKS_BUTTON = 571;
        public const int CHERRY_PLANKS_BUTTON = 572;
        public const int CRIMSON_PLANKS_BUTTON = 573;
        public const int WARPED_PLANKS_BUTTON = 574;
        public const int ACACIA_DOOR = 575;
        public const int JUNGLE_DOOR = 576;
        public const int DARK_OAK_DOOR = 577;
        public const int CRIMSON_DOOR = 578;
        public const int WARPED_DOOR = 579;
        public const int BAMBOO_DOOR = 580;
        public const int COPPER_DOOR = 581;
        public const int IRON_DOOR = 582;
        public const int MANGROVE_DOOR = 583;
        public const int CHERRY_DOOR = 584;
        public const int ACACIA_TRAPDOOR = 585;
        public const int JUNGLE_TRAPDOOR = 586;
        public const int DARK_OAK_TRAPDOOR = 587;
        public const int CRIMSON_TRAPDOOR = 588;
        public const int WARPED_TRAPDOOR = 589;
        public const int IRON_TRAPDOOR = 590;
        public const int MANGROVE_TRAPDOOR = 591;
        public const int CHERRY_TRAPDOOR = 592;
        public const int BAMBOO_TRAPDOOR = 593;
        public const int COPPER_TRAPDOOR = 594;
        public const int SUNFLOWER = 595;
        public const int LILAC = 596;
        public const int ROSE_BUSH = 597;
        public const int PEONY = 598;
        public const int TALL_GRASS_PLANT = 599;
        public const int LARGE_FERN = 600;
        public const int VINE = 601;
        public const int GLOW_LICHEN = 602;
        public const int LILY_PAD = 603;
        public const int SEA_PICKLE = 604;
        public const int BAMBOO_PLANT = 605;
        public const int BED = 606;
        public const int BED_WHITE = 607;
        public const int BED_ORANGE = 608;
        public const int BED_MAGENTA = 609;
        public const int BED_LIGHT_BLUE = 610;
        public const int BED_YELLOW = 611;
        public const int BED_LIME = 612;
        public const int BED_PINK = 613;
        public const int BED_GRAY = 614;
        public const int BED_LIGHT_GRAY = 615;
        public const int BED_CYAN = 616;
        public const int BED_PURPLE = 617;
        public const int BED_BLUE = 618;
        public const int BED_BROWN = 619;
        public const int BED_GREEN = 620;
        public const int BED_BLACK = 621;
        public const int CHEST = 622;
        public const int FARMLAND = 623;
        public const int WHEAT_0 = 624;
        public const int WHEAT_1 = 625;
        public const int WHEAT_2 = 626;
        public const int WHEAT_3 = 627;
        public const int LAVA = 628;
        public const int FURNACE_LIT = 629;
        public const int FARMLAND_MOIST = 630;
        public const int CARROTS_0 = 631;
        public const int CARROTS_1 = 632;
        public const int CARROTS_2 = 633;
        public const int CARROTS_3 = 634;
        public const int POTATOES_0 = 635;
        public const int POTATOES_1 = 636;
        public const int POTATOES_2 = 637;
        public const int POTATOES_3 = 638;
        public const int FIRE = 639;
        public const int PUMPKIN_STEM_0 = 640;
        public const int PUMPKIN_STEM_1 = 641;
        public const int PUMPKIN_STEM_2 = 642;
        public const int PUMPKIN_STEM_3 = 643;
        public const int MELON_STEM_0 = 644;
        public const int MELON_STEM_1 = 645;
        public const int MELON_STEM_2 = 646;
        public const int MELON_STEM_3 = 647;
        public const int FLOWER_POT = 648;
        public const int RAIL = 649;
        public const int COBWEB = 650;
        public const int SNOW_LAYER = 651;
        public const int SNOWY_GRASS = 652;
        public const int MANGROVE_LOG = 653;
        public const int MANGROVE_LEAVES = 654;
        public const int MANGROVE_ROOTS = 655;
        public const int MANGROVE_ROOTS_WET = 656;
        public const int MUDDY_MANGROVE_ROOTS = 657;
        public const int MANGROVE_PROPAGULE = 658;
        public const int SMALL_AMETHYST_BUD = 659;
        public const int MEDIUM_AMETHYST_BUD = 660;
        public const int LARGE_AMETHYST_BUD = 661;
        public const int AMETHYST_CLUSTER = 662;
        public const int CHERRY_LOG = 663;
        public const int CHERRY_LEAVES = 664;
        public const int SPAWNER = 665;
        public const int REDSTONE_TORCH = 666;
        public const int BARREL = 667;
        public const int BLAST_FURNACE = 668;
        public const int SMOKER = 669;
        public const int CARTOGRAPHY_TABLE = 670;
        public const int FLETCHING_TABLE = 671;
        public const int SMITHING_TABLE = 672;
        public const int LOOM = 673;
        public const int LECTERN = 674;
        public const int GRINDSTONE = 675;
        public const int STONECUTTER = 676;
        public const int BELL = 677;
        public const int LANTERN = 678;
        public const int CAMPFIRE = 679;
        public const int BREWING_STAND = 680;
        public const int CAULDRON = 681;
        public const int COMPOSTER = 682;
        public const int BEETROOTS_0 = 683;
        public const int BEETROOTS_1 = 684;
        public const int BEETROOTS_2 = 685;
        public const int BEETROOTS_3 = 686;
        public const int POINTED_DRIPSTONE = 687;
        public const int AZALEA = 688;
        public const int FLOWERING_AZALEA = 689;
        public const int BIG_DRIPLEAF = 690;
        public const int BIG_DRIPLEAF_STEM = 691;
        public const int SMALL_DRIPLEAF = 692;
        public const int SPORE_BLOSSOM = 693;
        public const int HANGING_ROOTS = 694;
        public const int SCULK = 695;
        public const int SCULK_VEIN = 696;
        public const int SCULK_SENSOR = 697;
        public const int SCULK_SHRIEKER = 698;
        public const int SCULK_CATALYST = 699;
        public const int PALE_OAK_LOG = 700;
        public const int PALE_OAK_LEAVES = 701;
        public const int PALE_OAK_PLANKS = 702;
        public const int PALE_MOSS_BLOCK = 703;
        public const int PALE_MOSS_CARPET = 704;
        public const int PALE_HANGING_MOSS = 705;
        public const int PALE_HANGING_MOSS_TIP = 706;
        public const int OPEN_EYEBLOSSOM = 707;
        public const int CLOSED_EYEBLOSSOM = 708;
        public const int CREAKING_HEART = 709;
        public const int SHIP_WHEEL = 710;
        public const int COCOA = 711;
        public const int REDSTONE_WIRE = 712;
        public const int REDSTONE_TORCH_OFF = 713;
        public const int LEVER = 714;
        public const int REDSTONE_LAMP = 715;
        public const int REDSTONE_LAMP_ON = 716;
        public const int REPEATER = 717;
        public const int COMPARATOR = 718;
        public const int OBSERVER = 719;
        public const int PISTON = 720;
        public const int STICKY_PISTON = 721;
        public const int PISTON_HEAD = 722;
    }
}
