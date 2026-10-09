// Voxel Forge — Unity port. Block ids (B), native block definitions, mining / explosion metadata.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VoxelForge
{
    public static class B
    {
        public const int AIR = 0;
        public const int GRASS = 1;
        public const int DIRT = 2;
        public const int STONE = 3;
        public const int SAND = 4;
        public const int WATER = 5;
        public const int LOG = 6;
        public const int LEAVES = 7;
        public const int PLANKS = 8;
        public const int COBBLE = 9;
        public const int GLASS = 10;
        public const int BRICKS = 11;
        public const int COAL = 12;
        public const int IRON = 13;
        public const int GOLD = 14;
        public const int DIAMOND = 15;
        public const int BEDROCK = 16;
        public const int GRAVEL = 17;
        public const int SNOW = 18;
        public const int CLAY = 19;
        public const int OBSIDIAN = 20;
        public const int STONEBRICK = 21;
        public const int MOSSY = 22;
        public const int CRAFT = 23;
        public const int TNT = 24;
        public const int PUMPKIN = 25;
        public const int MELON = 26;
        public const int BOOKSHELF = 27;
        public const int DANDELION = 28;
        public const int POPPY = 29;
        public const int BLUE_ORCHID = 30;
        public const int ALLIUM = 31;
        public const int AZURE_BLUET = 32;
        public const int CORNFLOWER = 33;
        public const int FERN = 34;
        public const int DEAD_BUSH = 35;
        public const int SEAGRASS = 36;
        public const int KELP = 37;
        public const int RED_MUSHROOM = 38;
        public const int BROWN_MUSHROOM = 39;
        public const int FURNACE = 40;
        public const int FURNACE_LIT = 41;
        public const int CHEST = 42;
        public const int TORCH = 43;
        public const int LADDER = 44;
        public const int DOOR = 45;
        public const int DOOR_TOP = 46;
        public const int DOOR_OPEN = 47;
        public const int DOOR_OPEN_TOP = 48;
        public const int BED = 49;
        public const int FARMLAND = 50;
        public const int WHEAT0 = 51;
        public const int WHEAT1 = 52;
        public const int WHEAT2 = 53;
        public const int WHEAT3 = 54;
        public const int LAVA = 55;
        public const int SAPLING = 56;
        public const int CACTUS = 57;
        public const int SANDSTONE = 58;
        public const int ICE = 59;
        public const int WOOL_WHITE = 60;
        public const int WOOL_BLACK = 61;
        public const int WOOL_GRAY = 62;
        public const int WOOL_LIGHT_GRAY = 63;
        public const int WOOL_BROWN = 64;
        public const int WOOL_PINK = 65;
        public const int FARMLAND_MOIST = 66;
        public const int FLOW3 = 67;
        public const int FLOW2 = 68;
        public const int FLOW1 = 69;
        public const int LAVA_FLOW2 = 70;
        public const int LAVA_FLOW1 = 71;
        public const int FIRE = 72;
        public const int COBWEB = 73;
        public const int RAIL = 74;
        public const int NETHERRACK = 75;
        public const int GOLD_BLOCK = 76;
        public const int CRYING_OBSIDIAN = 77;
        public const int SMOOTH_STONE = 78;
        public const int RED_SAND = 79;
        public const int CARVED_PUMPKIN = 80;
        public const int COAL_BLOCK = 81;
        public const int COPPER = 82;
        public const int SPRUCE_PLANKS = 83;
        public const int BIRCH_PLANKS = 84;
        public const int OAK_SLAB = 85;
        public const int SPRUCE_SLAB = 86;
        public const int BIRCH_SLAB = 87;
        public const int STONE_SLAB = 88;
        public const int COBBLE_SLAB = 89;
        public const int STONEBRICK_SLAB = 90;
        public const int OAK_STAIRS = 91;
        public const int SPRUCE_STAIRS = 92;
        public const int BIRCH_STAIRS = 93;
        public const int STONE_STAIRS = 94;
        public const int COBBLE_STAIRS = 95;
        public const int STONEBRICK_STAIRS = 96;
        public const int OAK_FENCE = 97;
        public const int SPRUCE_FENCE = 98;
        public const int BIRCH_FENCE = 99;
        public const int OAK_GATE = 100;
        public const int SPRUCE_GATE = 101;
        public const int BIRCH_GATE = 102;
        public const int OAK_TRAPDOOR = 103;
        public const int SPRUCE_TRAPDOOR = 104;
        public const int BIRCH_TRAPDOOR = 105;
        public const int GLASS_PANE = 106;
        public const int WHITE_CARPET = 107;
        public const int STONE_PLATE = 108;
        public const int STONE_BUTTON = 109;
        public const int VINE = 110;
        public const int GLOW_LICHEN = 111;
        public const int LILY_PAD = 112;
        public const int SEA_PICKLE = 113;
        public const int BAMBOO = 114;
        public const int COBBLE_WALL = 115;
        public const int FLOWER_POT = 116;
        public const int SPRUCE_LOG = 117;
        public const int SPRUCE_LEAVES = 118;
        public const int BIRCH_LOG = 119;
        public const int BIRCH_LEAVES = 120;
        public const int JUNGLE_LOG = 121;
        public const int JUNGLE_LEAVES = 122;
        public const int ACACIA_LOG = 123;
        public const int ACACIA_LEAVES = 124;
        public const int DARK_LOG = 125;
        public const int DARK_LEAVES = 126;
        public const int CHERRY_LEAVES = 127;
        public const int PODZOL = 128;
        public const int TERRACOTTA = 129;
        public const int TERRA_ORANGE = 130;
        public const int TERRA_WHITE = 131;
        public const int PACKED_ICE = 132;
        public const int MYCELIUM = 133;
        public const int MUD = 134;
        public const int TUBE_CORAL_BLOCK = 135;
        public const int TUBE_CORAL_FAN = 136;
        public const int BRAIN_CORAL_BLOCK = 137;
        public const int BRAIN_CORAL_FAN = 138;
        public const int BUBBLE_CORAL_BLOCK = 139;
        public const int BUBBLE_CORAL_FAN = 140;
        public const int FIRE_CORAL_BLOCK = 141;
        public const int FIRE_CORAL_FAN = 142;
        public const int HORN_CORAL_BLOCK = 143;
        public const int HORN_CORAL_FAN = 144;
        public const int MOSS_BLOCK = 145;
        public const int ROOTED_DIRT = 146;
        public const int DRIPSTONE_BLOCK = 147;
        public const int CAVE_VINES = 148;
        public const int DEEPSLATE = 149;
        public const int TUFF = 150;
        public const int MOSS_CARPET = 151;
        public const int CALCITE = 152;
        public const int AMETHYST_BLOCK = 153;
        public const int BUDDING_AMETHYST = 154;
        public const int AZALEA_LEAVES = 155;
        public const int SHORT_GRASS = 156;
        public const int OXEYE_DAISY = 157;
        public const int LILY_OF_THE_VALLEY = 158;
        public const int ORANGE_TULIP = 159;
        public const int PINK_TULIP = 160;
        public const int RED_TULIP = 161;
        public const int WHITE_TULIP = 162;
        public const int SWEET_BERRY_BUSH = 163;
        public const int SUGAR_CANE = 164;
        public const int LARGE_FERN = 165;
        public const int LARGE_FERN_TOP = 166;
        public const int SUNFLOWER = 167;
        public const int SUNFLOWER_TOP = 168;
        public const int LILAC = 169;
        public const int LILAC_TOP = 170;
        public const int ROSE_BUSH = 171;
        public const int ROSE_BUSH_TOP = 172;
        public const int PEONY = 173;
        public const int PEONY_TOP = 174;
        public const int MUSHROOM_STEM = 175;
        public const int RED_MUSHROOM_BLOCK = 176;
        public const int BROWN_MUSHROOM_BLOCK = 177;
        public const int CUT_SANDSTONE = 178;
        public const int CHISELED_SANDSTONE = 179;
        public const int SANDSTONE_SLAB = 180;
        public const int SANDSTONE_STAIRS = 181;
        public const int MAGMA_BLOCK = 182;
        public const int BLUE_TERRACOTTA = 183;
        public const int DARK_PLANKS = 184;
        public const int DARK_SLAB = 185;
        public const int DARK_STAIRS = 186;
        public const int DARK_FENCE = 187;
        public const int CARROTS0 = 188;
        public const int CARROTS1 = 189;
        public const int CARROTS2 = 190;
        public const int CARROTS3 = 191;
        public const int POTATOES0 = 192;
        public const int POTATOES1 = 193;
        public const int POTATOES2 = 194;
        public const int POTATOES3 = 195;
        public const int PUMPKIN_STEM0 = 196;
        public const int PUMPKIN_STEM1 = 197;
        public const int PUMPKIN_STEM2 = 198;
        public const int PUMPKIN_STEM3 = 199;
        public const int MELON_STEM0 = 200;
        public const int MELON_STEM1 = 201;
        public const int MELON_STEM2 = 202;
        public const int MELON_STEM3 = 203;
        public const int FLOWERING_AZALEA_LEAVES = 204;
        public const int GRANITE = 205;
        public const int DIORITE = 206;
        public const int ANDESITE = 207;
        public const int REDSTONE_ORE = 208;
        public const int LAPIS_ORE = 209;
        public const int EMERALD_ORE = 210;
        public const int DEEPSLATE_COAL_ORE = 211;
        public const int DEEPSLATE_IRON_ORE = 212;
        public const int DEEPSLATE_GOLD_ORE = 213;
        public const int DEEPSLATE_DIAMOND_ORE = 214;
        public const int DEEPSLATE_REDSTONE_ORE = 215;
        public const int DEEPSLATE_LAPIS_ORE = 216;
        public const int DEEPSLATE_EMERALD_ORE = 217;
        public const int DEEPSLATE_COPPER_ORE = 218;
        public const int SHIP_WHEEL = 219;
        public const int FLOW7 = 220;
        public const int FLOW6 = 221;
        public const int FLOW5 = 222;
        public const int FLOW4 = 223;
        public const int WATER_FALLING = 224;
        public const int VIRTUAL_OPAQUE = 225;
        public const int VIRTUAL_TRANSPARENT = 226;
        public const int VIRTUAL_CUTOUT = 227;
        public const int VIRTUAL_PLANT = 228;
        public static readonly System.Collections.Generic.Dictionary<string,int> NAMES = new System.Collections.Generic.Dictionary<string,int> {
            {"AIR",0}, {"GRASS",1}, {"DIRT",2}, {"STONE",3}, {"SAND",4}, {"WATER",5}, {"LOG",6}, {"LEAVES",7}, {"PLANKS",8}, {"COBBLE",9}, {"GLASS",10}, {"BRICKS",11}, {"COAL",12}, {"IRON",13}, {"GOLD",14}, {"DIAMOND",15}, {"BEDROCK",16}, {"GRAVEL",17}, {"SNOW",18}, {"CLAY",19}, {"OBSIDIAN",20}, {"STONEBRICK",21}, {"MOSSY",22}, {"CRAFT",23}, {"TNT",24}, {"PUMPKIN",25}, {"MELON",26}, {"BOOKSHELF",27}, {"DANDELION",28}, {"POPPY",29}, {"BLUE_ORCHID",30}, {"ALLIUM",31}, {"AZURE_BLUET",32}, {"CORNFLOWER",33}, {"FERN",34}, {"DEAD_BUSH",35}, {"SEAGRASS",36}, {"KELP",37}, {"RED_MUSHROOM",38}, {"BROWN_MUSHROOM",39}, {"FURNACE",40}, {"FURNACE_LIT",41}, {"CHEST",42}, {"TORCH",43}, {"LADDER",44}, {"DOOR",45}, {"DOOR_TOP",46}, {"DOOR_OPEN",47}, {"DOOR_OPEN_TOP",48}, {"BED",49}, {"FARMLAND",50}, {"WHEAT0",51}, {"WHEAT1",52}, {"WHEAT2",53}, {"WHEAT3",54}, {"LAVA",55}, {"SAPLING",56}, {"CACTUS",57}, {"SANDSTONE",58}, {"ICE",59}, {"WOOL_WHITE",60}, {"WOOL_BLACK",61}, {"WOOL_GRAY",62}, {"WOOL_LIGHT_GRAY",63}, {"WOOL_BROWN",64}, {"WOOL_PINK",65}, {"FARMLAND_MOIST",66}, {"FLOW3",67}, {"FLOW2",68}, {"FLOW1",69}, {"LAVA_FLOW2",70}, {"LAVA_FLOW1",71}, {"FIRE",72}, {"COBWEB",73}, {"RAIL",74}, {"NETHERRACK",75}, {"GOLD_BLOCK",76}, {"CRYING_OBSIDIAN",77}, {"SMOOTH_STONE",78}, {"RED_SAND",79}, {"CARVED_PUMPKIN",80}, {"COAL_BLOCK",81}, {"COPPER",82}, {"SPRUCE_PLANKS",83}, {"BIRCH_PLANKS",84}, {"OAK_SLAB",85}, {"SPRUCE_SLAB",86}, {"BIRCH_SLAB",87}, {"STONE_SLAB",88}, {"COBBLE_SLAB",89}, {"STONEBRICK_SLAB",90}, {"OAK_STAIRS",91}, {"SPRUCE_STAIRS",92}, {"BIRCH_STAIRS",93}, {"STONE_STAIRS",94}, {"COBBLE_STAIRS",95}, {"STONEBRICK_STAIRS",96}, {"OAK_FENCE",97}, {"SPRUCE_FENCE",98}, {"BIRCH_FENCE",99}, {"OAK_GATE",100}, {"SPRUCE_GATE",101}, {"BIRCH_GATE",102}, {"OAK_TRAPDOOR",103}, {"SPRUCE_TRAPDOOR",104}, {"BIRCH_TRAPDOOR",105}, {"GLASS_PANE",106}, {"WHITE_CARPET",107}, {"STONE_PLATE",108}, {"STONE_BUTTON",109}, {"VINE",110}, {"GLOW_LICHEN",111}, {"LILY_PAD",112}, {"SEA_PICKLE",113}, {"BAMBOO",114}, {"COBBLE_WALL",115}, {"FLOWER_POT",116}, {"SPRUCE_LOG",117}, {"SPRUCE_LEAVES",118}, {"BIRCH_LOG",119}, {"BIRCH_LEAVES",120}, {"JUNGLE_LOG",121}, {"JUNGLE_LEAVES",122}, {"ACACIA_LOG",123}, {"ACACIA_LEAVES",124}, {"DARK_LOG",125}, {"DARK_LEAVES",126}, {"CHERRY_LEAVES",127}, {"PODZOL",128}, {"TERRACOTTA",129}, {"TERRA_ORANGE",130}, {"TERRA_WHITE",131}, {"PACKED_ICE",132}, {"MYCELIUM",133}, {"MUD",134}, {"TUBE_CORAL_BLOCK",135}, {"TUBE_CORAL_FAN",136}, {"BRAIN_CORAL_BLOCK",137}, {"BRAIN_CORAL_FAN",138}, {"BUBBLE_CORAL_BLOCK",139}, {"BUBBLE_CORAL_FAN",140}, {"FIRE_CORAL_BLOCK",141}, {"FIRE_CORAL_FAN",142}, {"HORN_CORAL_BLOCK",143}, {"HORN_CORAL_FAN",144}, {"MOSS_BLOCK",145}, {"ROOTED_DIRT",146}, {"DRIPSTONE_BLOCK",147}, {"CAVE_VINES",148}, {"DEEPSLATE",149}, {"TUFF",150}, {"MOSS_CARPET",151}, {"CALCITE",152}, {"AMETHYST_BLOCK",153}, {"BUDDING_AMETHYST",154}, {"AZALEA_LEAVES",155}, {"SHORT_GRASS",156}, {"OXEYE_DAISY",157}, {"LILY_OF_THE_VALLEY",158}, {"ORANGE_TULIP",159}, {"PINK_TULIP",160}, {"RED_TULIP",161}, {"WHITE_TULIP",162}, {"SWEET_BERRY_BUSH",163}, {"SUGAR_CANE",164}, {"LARGE_FERN",165}, {"LARGE_FERN_TOP",166}, {"SUNFLOWER",167}, {"SUNFLOWER_TOP",168}, {"LILAC",169}, {"LILAC_TOP",170}, {"ROSE_BUSH",171}, {"ROSE_BUSH_TOP",172}, {"PEONY",173}, {"PEONY_TOP",174}, {"MUSHROOM_STEM",175}, {"RED_MUSHROOM_BLOCK",176}, {"BROWN_MUSHROOM_BLOCK",177}, {"CUT_SANDSTONE",178}, {"CHISELED_SANDSTONE",179}, {"SANDSTONE_SLAB",180}, {"SANDSTONE_STAIRS",181}, {"MAGMA_BLOCK",182}, {"BLUE_TERRACOTTA",183}, {"DARK_PLANKS",184}, {"DARK_SLAB",185}, {"DARK_STAIRS",186}, {"DARK_FENCE",187}, {"CARROTS0",188}, {"CARROTS1",189}, {"CARROTS2",190}, {"CARROTS3",191}, {"POTATOES0",192}, {"POTATOES1",193}, {"POTATOES2",194}, {"POTATOES3",195}, {"PUMPKIN_STEM0",196}, {"PUMPKIN_STEM1",197}, {"PUMPKIN_STEM2",198}, {"PUMPKIN_STEM3",199}, {"MELON_STEM0",200}, {"MELON_STEM1",201}, {"MELON_STEM2",202}, {"MELON_STEM3",203}, {"FLOWERING_AZALEA_LEAVES",204}, {"GRANITE",205}, {"DIORITE",206}, {"ANDESITE",207}, {"REDSTONE_ORE",208}, {"LAPIS_ORE",209}, {"EMERALD_ORE",210}, {"DEEPSLATE_COAL_ORE",211}, {"DEEPSLATE_IRON_ORE",212}, {"DEEPSLATE_GOLD_ORE",213}, {"DEEPSLATE_DIAMOND_ORE",214}, {"DEEPSLATE_REDSTONE_ORE",215}, {"DEEPSLATE_LAPIS_ORE",216}, {"DEEPSLATE_EMERALD_ORE",217}, {"DEEPSLATE_COPPER_ORE",218}, {"SHIP_WHEEL",219}, {"FLOW7",220}, {"FLOW6",221}, {"FLOW5",222}, {"FLOW4",223}, {"WATER_FALLING",224}, {"VIRTUAL_OPAQUE",225}, {"VIRTUAL_TRANSPARENT",226}, {"VIRTUAL_CUTOUT",227}, {"VIRTUAL_PLANT",228}
        };
        public static readonly string[] ORDER = { "AIR", "GRASS", "DIRT", "STONE", "SAND", "WATER", "LOG", "LEAVES", "PLANKS", "COBBLE", "GLASS", "BRICKS", "COAL", "IRON", "GOLD", "DIAMOND", "BEDROCK", "GRAVEL", "SNOW", "CLAY", "OBSIDIAN", "STONEBRICK", "MOSSY", "CRAFT", "TNT", "PUMPKIN", "MELON", "BOOKSHELF", "DANDELION", "POPPY", "BLUE_ORCHID", "ALLIUM", "AZURE_BLUET", "CORNFLOWER", "FERN", "DEAD_BUSH", "SEAGRASS", "KELP", "RED_MUSHROOM", "BROWN_MUSHROOM", "FURNACE", "FURNACE_LIT", "CHEST", "TORCH", "LADDER", "DOOR", "DOOR_TOP", "DOOR_OPEN", "DOOR_OPEN_TOP", "BED", "FARMLAND", "WHEAT0", "WHEAT1", "WHEAT2", "WHEAT3", "LAVA", "SAPLING", "CACTUS", "SANDSTONE", "ICE", "WOOL_WHITE", "WOOL_BLACK", "WOOL_GRAY", "WOOL_LIGHT_GRAY", "WOOL_BROWN", "WOOL_PINK", "FARMLAND_MOIST", "FLOW3", "FLOW2", "FLOW1", "LAVA_FLOW2", "LAVA_FLOW1", "FIRE", "COBWEB", "RAIL", "NETHERRACK", "GOLD_BLOCK", "CRYING_OBSIDIAN", "SMOOTH_STONE", "RED_SAND", "CARVED_PUMPKIN", "COAL_BLOCK", "COPPER", "SPRUCE_PLANKS", "BIRCH_PLANKS", "OAK_SLAB", "SPRUCE_SLAB", "BIRCH_SLAB", "STONE_SLAB", "COBBLE_SLAB", "STONEBRICK_SLAB", "OAK_STAIRS", "SPRUCE_STAIRS", "BIRCH_STAIRS", "STONE_STAIRS", "COBBLE_STAIRS", "STONEBRICK_STAIRS", "OAK_FENCE", "SPRUCE_FENCE", "BIRCH_FENCE", "OAK_GATE", "SPRUCE_GATE", "BIRCH_GATE", "OAK_TRAPDOOR", "SPRUCE_TRAPDOOR", "BIRCH_TRAPDOOR", "GLASS_PANE", "WHITE_CARPET", "STONE_PLATE", "STONE_BUTTON", "VINE", "GLOW_LICHEN", "LILY_PAD", "SEA_PICKLE", "BAMBOO", "COBBLE_WALL", "FLOWER_POT", "SPRUCE_LOG", "SPRUCE_LEAVES", "BIRCH_LOG", "BIRCH_LEAVES", "JUNGLE_LOG", "JUNGLE_LEAVES", "ACACIA_LOG", "ACACIA_LEAVES", "DARK_LOG", "DARK_LEAVES", "CHERRY_LEAVES", "PODZOL", "TERRACOTTA", "TERRA_ORANGE", "TERRA_WHITE", "PACKED_ICE", "MYCELIUM", "MUD", "TUBE_CORAL_BLOCK", "TUBE_CORAL_FAN", "BRAIN_CORAL_BLOCK", "BRAIN_CORAL_FAN", "BUBBLE_CORAL_BLOCK", "BUBBLE_CORAL_FAN", "FIRE_CORAL_BLOCK", "FIRE_CORAL_FAN", "HORN_CORAL_BLOCK", "HORN_CORAL_FAN", "MOSS_BLOCK", "ROOTED_DIRT", "DRIPSTONE_BLOCK", "CAVE_VINES", "DEEPSLATE", "TUFF", "MOSS_CARPET", "CALCITE", "AMETHYST_BLOCK", "BUDDING_AMETHYST", "AZALEA_LEAVES", "SHORT_GRASS", "OXEYE_DAISY", "LILY_OF_THE_VALLEY", "ORANGE_TULIP", "PINK_TULIP", "RED_TULIP", "WHITE_TULIP", "SWEET_BERRY_BUSH", "SUGAR_CANE", "LARGE_FERN", "LARGE_FERN_TOP", "SUNFLOWER", "SUNFLOWER_TOP", "LILAC", "LILAC_TOP", "ROSE_BUSH", "ROSE_BUSH_TOP", "PEONY", "PEONY_TOP", "MUSHROOM_STEM", "RED_MUSHROOM_BLOCK", "BROWN_MUSHROOM_BLOCK", "CUT_SANDSTONE", "CHISELED_SANDSTONE", "SANDSTONE_SLAB", "SANDSTONE_STAIRS", "MAGMA_BLOCK", "BLUE_TERRACOTTA", "DARK_PLANKS", "DARK_SLAB", "DARK_STAIRS", "DARK_FENCE", "CARROTS0", "CARROTS1", "CARROTS2", "CARROTS3", "POTATOES0", "POTATOES1", "POTATOES2", "POTATOES3", "PUMPKIN_STEM0", "PUMPKIN_STEM1", "PUMPKIN_STEM2", "PUMPKIN_STEM3", "MELON_STEM0", "MELON_STEM1", "MELON_STEM2", "MELON_STEM3", "FLOWERING_AZALEA_LEAVES", "GRANITE", "DIORITE", "ANDESITE", "REDSTONE_ORE", "LAPIS_ORE", "EMERALD_ORE", "DEEPSLATE_COAL_ORE", "DEEPSLATE_IRON_ORE", "DEEPSLATE_GOLD_ORE", "DEEPSLATE_DIAMOND_ORE", "DEEPSLATE_REDSTONE_ORE", "DEEPSLATE_LAPIS_ORE", "DEEPSLATE_EMERALD_ORE", "DEEPSLATE_COPPER_ORE", "SHIP_WHEEL", "FLOW7", "FLOW6", "FLOW5", "FLOW4", "WATER_FALLING", "VIRTUAL_OPAQUE", "VIRTUAL_TRANSPARENT", "VIRTUAL_CUTOUT", "VIRTUAL_PLANT" };
    }
    public sealed class ArmorInfo { public int slot; public double pts; public string vision; public double tough; }

    public sealed class BlockDef
    {
        public int id;
        public string name;
        public int side = -1, top = -1, bottom = -1;
        public bool solid, transparent, cutout, plant, waterPlant, needsWater;
        public double alpha = 1;
        public string special;
        public int light;
        public int front = -1, frontL = -1, frontR = -1, topL = -1, topR = -1, corner = -1;
        public double hardness = 1;
        public string tool;
        public int tier;
        public ArmorInfo armor;
    }

    /// <summary>Option bag for def()/def2() — mirrors the JS object literal.</summary>
    public sealed class BOpt
    {
        public bool? solid, transparent, cutout, plant, waterPlant, needsWater;
        public double? alpha, hardness;
        public string special, front, tool;
        public int? light, tier;
        public int? frontL, frontR, topL, topR;
    }

    public static partial class VF
    {
        public const int CHUNK = 16, SECTION = 16, WORLD_MIN_Y = -64, WORLD_MAX_Y = 320, WORLD_H = WORLD_MAX_Y - WORLD_MIN_Y,
            SECTION_COUNT = WORLD_H / SECTION, SEA = 63, DEFAULT_RD = 6;
        public const double DPR_CAP = 1.5;
        public static int HW_CORES = Math.Max(1, Environment.ProcessorCount);
        // Render-priority worker budget: do not create N gen + N mesh threads and starve the main/render thread.
        public static int WORKER_BUDGET = Math.Max(2, Math.Min(8, HW_CORES - 1));
        public static int GEN_WORKER_COUNT = Math.Max(1, Math.Min(2, WORKER_BUDGET / 3));
        public static int MESH_WORKER_COUNT = Math.Max(1, WORKER_BUDGET - GEN_WORKER_COUNT);
        public static int GEN_QUEUE_LIMIT = Math.Max(10, GEN_WORKER_COUNT * 6);

        public static readonly BlockDef[] blocks = new BlockDef[256];

        static void def(int id, string name, string side, string top = null, string bottom = null, BOpt opt = null)
        {
            top = top ?? side; bottom = bottom ?? side; opt = opt ?? new BOpt();
            blocks[id] = new BlockDef
            {
                id = id, name = name, side = T(side), top = T(top), bottom = T(bottom),
                solid = opt.solid != false, transparent = opt.transparent == true, alpha = opt.alpha ?? 1, cutout = opt.cutout == true,
                plant = opt.plant == true, waterPlant = opt.waterPlant == true, needsWater = opt.needsWater == true,
            };
        }
        static BlockDef def2(int id, string name, string side, string top = null, string bottom = null, BOpt opt = null)
        {
            top = top ?? side; bottom = bottom ?? side; opt = opt ?? new BOpt();
            blocks[id] = new BlockDef
            {
                id = id, name = name, side = T(side), top = T(top), bottom = T(bottom),
                solid = opt.solid != false, transparent = opt.transparent == true, alpha = opt.alpha ?? 1, cutout = opt.cutout == true,
                plant = opt.plant == true, waterPlant = opt.waterPlant == true, needsWater = opt.needsWater == true,
                special = opt.special, light = opt.light ?? 0, front = opt.front != null ? T(opt.front) : -1,
                hardness = opt.hardness ?? 1, tool = opt.tool, tier = opt.tier ?? 0,
            };
            return blocks[id];
        }
        static void assignBlock(int id, BOpt o)
        {
            var b = blocks[id];
            if (o.hardness.HasValue) b.hardness = o.hardness.Value;
            if (o.tool != null) b.tool = o.tool;
            if (o.tier.HasValue) b.tier = o.tier.Value;
            if (o.frontL.HasValue) b.frontL = o.frontL.Value;
            if (o.frontR.HasValue) b.frontR = o.frontR.Value;
            if (o.topL.HasValue) b.topL = o.topL.Value;
            if (o.topR.HasValue) b.topR = o.topR.Value;
        }
        /// <summary>blocks[id] with JS undefined-safety.</summary>
        public static BlockDef bdef(int id) { return id >= 0 && id < 256 ? blocks[id] : null; }

        static void InitBlocks()
        {
            Array.Clear(blocks, 0, blocks.Length);
            def(B.GRASS, "Трава", "grass_side", "grass_top", "dirt");
            def(B.DIRT, "Земля", "dirt");
            def(B.STONE, "Камень", "stone");
            def(B.SAND, "Песок", "sand");
            def(B.WATER, "Вода", "water", "water", "water", new BOpt { solid = false, transparent = true, alpha = 0.62 });
            def(B.LOG, "Дуб", "oak_log", "oak_log_top", "oak_log_top");
            def(B.LEAVES, "Листва", "oak_leaves", "oak_leaves", "oak_leaves", new BOpt { cutout = true });
            def(B.PLANKS, "Доски", "oak_planks");
            def(B.COBBLE, "Булыжник", "cobblestone");
            def(B.GLASS, "Стекло", "glass", "glass", "glass", new BOpt { transparent = true, alpha = 1 });
            def(B.BRICKS, "Кирпич", "bricks");
            def(B.COAL, "Угольная руда", "coal_ore");
            def(B.IRON, "Железная руда", "iron_ore");
            def(B.GOLD, "Золотая руда", "gold_ore");
            def(B.DIAMOND, "Алмазная руда", "diamond_ore");
            def(B.BEDROCK, "Бедрок", "bedrock");
            def(B.GRAVEL, "Гравий", "gravel");
            def(B.SNOW, "Снег", "snow");
            def(B.CLAY, "Глина", "clay");
            def(B.OBSIDIAN, "Обсидиан", "obsidian");
            def(B.STONEBRICK, "Каменный кирпич", "stone_bricks");
            def(B.MOSSY, "Мшистый булыжник", "mossy_cobble");
            def(B.CRAFT, "Верстак", "craft_side", "craft_top", "oak_planks");
            def(B.TNT, "TNT", "tnt", "tnt_top", "tnt_bottom");
            def(B.PUMPKIN, "Тыква", "pumpkin_side", "pumpkin_top", "pumpkin_top");
            def(B.MELON, "Арбуз", "melon_side", "melon_top", "melon_top");
            def(B.BOOKSHELF, "Книжная полка", "bookshelf", "oak_planks", "oak_planks");
            def(B.DANDELION, "Одуванчик", "dandelion", "dandelion", "dandelion", new BOpt { solid = false, transparent = true, cutout = true, plant = true });
            def(B.POPPY, "Мак", "poppy", "poppy", "poppy", new BOpt { solid = false, transparent = true, cutout = true, plant = true });
            def(B.BLUE_ORCHID, "Синяя орхидея", "blue_orchid", "blue_orchid", "blue_orchid", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true, });
            def(B.ALLIUM, "Лук-батун", "allium", "allium", "allium", new BOpt { solid = false, transparent = true, cutout = true, plant = true });
            def(B.AZURE_BLUET, "Хаустония", "azure_bluet", "azure_bluet", "azure_bluet", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true, });
            def(B.CORNFLOWER, "Василёк", "cornflower", "cornflower", "cornflower", new BOpt { solid = false, transparent = true, cutout = true, plant = true });
            def(B.FERN, "Папоротник", "fern", "fern", "fern", new BOpt { solid = false, transparent = true, cutout = true, plant = true });
            def(B.DEAD_BUSH, "Сухой куст", "dead_bush", "dead_bush", "dead_bush", new BOpt { solid = false, transparent = true, cutout = true, plant = true });
            def(B.SEAGRASS, "Морская трава", "seagrass", "seagrass", "seagrass", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              waterPlant = true,
              needsWater = true, });
            def(B.KELP, "Ламинария", "kelp", "kelp", "kelp", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              waterPlant = true,
              needsWater = true, });
            def(B.RED_MUSHROOM, "Красный гриб", "red_mushroom", "red_mushroom", "red_mushroom", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true, });
            def(B.BROWN_MUSHROOM, "Коричневый гриб", "brown_mushroom", "brown_mushroom", "brown_mushroom", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true, });
            blocks[B.AIR] = new BlockDef { id = 0, name = "Воздух", solid = false, transparent = true, alpha = 0 };
            def2(B.FURNACE, "Печь", "furnace_top", "furnace_top", "furnace_top", new BOpt { front = "furnace_front",
              special = "orientedCube",
              hardness = 3.5,
              tool = "pickaxe",
              tier = 1, });
            def2(B.FURNACE_LIT, "Горящая печь", "furnace_top", "furnace_top", "furnace_top", new BOpt { front = "furnace_front_on",
              special = "orientedCube",
              hardness = 3.5,
              tool = "pickaxe",
              tier = 1,
              light = 13, });
            def2(B.CHEST, "Сундук", "chest_side", "chest_top", "chest_top", new BOpt { front = "chest_front",
              special = "orientedCube",
              hardness = 2.5,
              tool = "axe", });
            assignBlock(B.CHEST, new BOpt { frontL = T("chest_front_l"), frontR = T("chest_front_r"), topL = T("chest_top_l"), topR = T("chest_top_r") });
            def2(B.TORCH, "Факел", "torch", "torch", "torch", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              special = "torch",
              light = 14,
              hardness = 0.05, });
            def2(B.LADDER, "Лестница", "ladder", "ladder", "ladder", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              special = "ladder",
              hardness = 0.4,
              tool = "axe", });
            def2(B.DOOR, "Дверь", "door_bottom", "door_bottom", "door_bottom", new BOpt { solid = true,
              transparent = true,
              cutout = true,
              special = "door",
              hardness = 1.6,
              tool = "axe", });
            def2(B.DOOR_TOP, "Дверь", "door_top", "door_top", "door_top", new BOpt { solid = true,
              transparent = true,
              cutout = true,
              special = "door",
              hardness = 1.6,
              tool = "axe", });
            def2(B.DOOR_OPEN, "Открытая дверь", "door_bottom", "door_bottom", "door_bottom", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              special = "doorOpen",
              hardness = 1.6,
              tool = "axe", });
            def2(B.DOOR_OPEN_TOP, "Открытая дверь", "door_top", "door_top", "door_top", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              special = "doorOpen",
              hardness = 1.6,
              tool = "axe", });
            def2(B.BED, "Кровать", "bed_side", "bed_top", "bed_side", new BOpt { solid = false, special = "bed", hardness = 0.8, tool = "axe" });
            def2(B.FARMLAND, "Грядка", "dirt", "farmland", "dirt", new BOpt { hardness = 0.6, tool = "shovel" });
            def2(B.WHEAT0, "Пшеница", "wheat0", "wheat0", "wheat0", new BOpt { solid = false, transparent = true, cutout = true, plant = true, hardness = 0.05 });
            def2(B.WHEAT1, "Пшеница", "wheat1", "wheat1", "wheat1", new BOpt { solid = false, transparent = true, cutout = true, plant = true, hardness = 0.05 });
            def2(B.WHEAT2, "Пшеница", "wheat2", "wheat2", "wheat2", new BOpt { solid = false, transparent = true, cutout = true, plant = true, hardness = 0.05 });
            def2(B.WHEAT3, "Пшеница", "wheat3", "wheat3", "wheat3", new BOpt { solid = false, transparent = true, cutout = true, plant = true, hardness = 0.05 });
            def2(B.LAVA, "Лава", "lava", "lava", "lava", new BOpt { solid = false, transparent = false, alpha = 1, light = 15, hardness = 999 });
            def2(B.SAPLING, "Саженец дуба", "oak_sapling", "oak_sapling", "oak_sapling", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.CACTUS, "Кактус", "cactus_side", "cactus_top", "cactus_top", new BOpt { hardness = 0.45 });
            def2(B.SANDSTONE, "Песчаник", "sandstone", "sandstone", "sandstone", new BOpt { hardness = 0.8, tool = "pickaxe" });
            def2(B.ICE, "Лёд", "ice", "ice", "ice", new BOpt { transparent = false, alpha = 1, hardness = 0.5, tool = "pickaxe" });
            def2(B.WOOL_WHITE, "Белая шерсть", "white_wool", "white_wool", "white_wool", new BOpt { hardness = 0.8 });
            
            def2(B.FARMLAND_MOIST, "Влажная грядка", "dirt", "farmland_moist", "dirt", new BOpt { hardness = 0.6, tool = "shovel" });
            def2(B.WATER_FALLING, "Падающая вода", "water", "water", "water", new BOpt { solid = false, transparent = true, alpha = 0.62 });
            def2(B.FLOW7, "Текущая вода", "water", "water", "water", new BOpt { solid = false, transparent = true, alpha = 0.62 });
            def2(B.FLOW6, "Текущая вода", "water", "water", "water", new BOpt { solid = false, transparent = true, alpha = 0.62 });
            def2(B.FLOW5, "Текущая вода", "water", "water", "water", new BOpt { solid = false, transparent = true, alpha = 0.62 });
            def2(B.FLOW4, "Текущая вода", "water", "water", "water", new BOpt { solid = false, transparent = true, alpha = 0.62 });
            def2(B.FLOW3, "Текущая вода", "water", "water", "water", new BOpt { solid = false, transparent = true, alpha = 0.62 });
            def2(B.FLOW2, "Текущая вода", "water", "water", "water", new BOpt { solid = false, transparent = true, alpha = 0.62 });
            def2(B.FLOW1, "Текущая вода", "water", "water", "water", new BOpt { solid = false, transparent = true, alpha = 0.62 });
            def2(B.LAVA_FLOW2, "Текущая лава", "lava", "lava", "lava", new BOpt { solid = false, transparent = false, alpha = 1, light = 15, hardness = 999 });
            def2(B.LAVA_FLOW1, "Текущая лава", "lava", "lava", "lava", new BOpt { solid = false, transparent = false, alpha = 1, light = 15, hardness = 999 });
            def2(B.FIRE, "Огонь", "fire", "fire", "fire", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              special = "fire",
              light = 15,
              hardness = 0.01, });
            def2(B.COBWEB, "Паутина", "cobweb", "cobweb", "cobweb", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              special = "web",
              hardness = 4,
              tool = "sword", });
            def2(B.RAIL, "Рельсы", "rail", "rail", "rail", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              special = "rail",
              hardness = 0.7,
              tool = "pickaxe", });
            blocks[B.RAIL].corner = T("rail_corner");
            def2(B.NETHERRACK, "Незерак", "netherrack", "netherrack", "netherrack", new BOpt { hardness = 0.4, tool = "pickaxe" });
            def2(B.GOLD_BLOCK, "Золотой блок", "gold_block", "gold_block", "gold_block", new BOpt { hardness = 3, tool = "pickaxe", tier = 3 });
            def2(B.CRYING_OBSIDIAN, "Плачущий обсидиан", "crying_obsidian", "crying_obsidian", "crying_obsidian", new BOpt { hardness = 50,
              tool = "pickaxe",
              tier = 4,
              light = 10, });
            def2(B.SMOOTH_STONE, "Гладкий камень", "smooth_stone", "smooth_stone", "smooth_stone", new BOpt { hardness = 1.5, tool = "pickaxe" });
            def2(B.RED_SAND, "Красный песок", "red_sand", "red_sand", "red_sand", new BOpt { hardness = 0.5, tool = "shovel" });
            def2(B.CARVED_PUMPKIN, "Вырезанная тыква", "carved_pumpkin", "pumpkin_top", "pumpkin_top", new BOpt { special = "orientedCube",
              hardness = 1,
              tool = "axe", });
            blocks[B.CARVED_PUMPKIN].armor = new ArmorInfo { slot = 0, pts = 0, vision = "pumpkin" };
            def2(B.COAL_BLOCK, "Угольный блок", "coal_block", "coal_block", "coal_block", new BOpt { hardness = 5, tool = "pickaxe", tier = 1 });
            def2(B.COPPER, "Медная руда", "copper_ore", "copper_ore", "copper_ore", new BOpt { hardness = 3, tool = "pickaxe", tier = 2 });
            foreach (var row in new (int[] ids, string prefix, string name)[] {
              (new[] {B.CARROTS0, B.CARROTS1, B.CARROTS2, B.CARROTS3}, "carrots_stage", "Морковь"),
              (new[] {B.POTATOES0, B.POTATOES1, B.POTATOES2, B.POTATOES3}, "potatoes_stage", "Картофель"),
              (new[] {B.PUMPKIN_STEM0, B.PUMPKIN_STEM1, B.PUMPKIN_STEM2, B.PUMPKIN_STEM3}, "pumpkin_stem_stage", "Стебель тыквы"),
              (new[] {B.MELON_STEM0, B.MELON_STEM1, B.MELON_STEM2, B.MELON_STEM3}, "melon_stem_stage", "Стебель арбуза"),
            }) {
              var ids = row.ids; var prefix = row.prefix; var name = row.name;
              for (int stage = 0; stage < 4; stage++)
                def2(ids[stage], name + " · " + (stage + 1), prefix + stage, prefix + stage, prefix + stage, new BOpt { solid = false,
                  transparent = true,
                  cutout = true,
                  plant = true,
                  hardness = 0.05, });
            }
            def2(B.SPRUCE_PLANKS, "Еловые доски", "spruce_planks", "spruce_planks", "spruce_planks", new BOpt { hardness = 2, tool = "axe" });
            def2(B.BIRCH_PLANKS, "Берёзовые доски", "birch_planks", "birch_planks", "birch_planks", new BOpt { hardness = 2, tool = "axe" });
            foreach (var row in new (int id, string name, string tex)[] {
              (B.OAK_SLAB, "Дубовая плита", "oak_planks"),
              (B.SPRUCE_SLAB, "Еловая плита", "spruce_planks"),
              (B.BIRCH_SLAB, "Берёзовая плита", "birch_planks"),
              (B.STONE_SLAB, "Каменная плита", "stone"),
              (B.COBBLE_SLAB, "Булыжная плита", "cobblestone"),
              (B.STONEBRICK_SLAB, "Плита из каменного кирпича", "stone_bricks"),
            }) {
              var id = row.id; var name = row.name; var tex = row.tex;
              def2(id, name, tex, tex, tex, new BOpt { special = "slab", hardness = id <= B.BIRCH_SLAB ? 2 : 2.5, tool = id <= B.BIRCH_SLAB ? "axe" : "pickaxe" });
            }
            foreach (var row in new (int id, string name, string tex)[] {
              (B.OAK_STAIRS, "Дубовые ступени", "oak_planks"),
              (B.SPRUCE_STAIRS, "Еловые ступени", "spruce_planks"),
              (B.BIRCH_STAIRS, "Берёзовые ступени", "birch_planks"),
              (B.STONE_STAIRS, "Каменные ступени", "stone"),
              (B.COBBLE_STAIRS, "Булыжные ступени", "cobblestone"),
              (B.STONEBRICK_STAIRS, "Ступени из каменного кирпича", "stone_bricks"),
            }) {
              var id = row.id; var name = row.name; var tex = row.tex;
              def2(id, name, tex, tex, tex, new BOpt { special = "stairs",
                hardness = id <= B.BIRCH_STAIRS ? 2 : 2.5,
                tool = id <= B.BIRCH_STAIRS ? "axe" : "pickaxe", });
            }
            foreach (var row in new (int id, string name, string tex)[] {
              (B.OAK_FENCE, "Дубовый забор", "oak_planks"),
              (B.SPRUCE_FENCE, "Еловый забор", "spruce_planks"),
              (B.BIRCH_FENCE, "Берёзовый забор", "birch_planks"),
            }) {
              var id = row.id; var name = row.name; var tex = row.tex;
              def2(id, name, tex, tex, tex, new BOpt { special = "fence", hardness = 2, tool = "axe" });
            }
            foreach (var row in new (int id, string name, string tex)[] {
              (B.OAK_GATE, "Дубовая калитка", "oak_planks"),
              (B.SPRUCE_GATE, "Еловая калитка", "spruce_planks"),
              (B.BIRCH_GATE, "Берёзовая калитка", "birch_planks"),
            }) {
              var id = row.id; var name = row.name; var tex = row.tex;
              def2(id, name, tex, tex, tex, new BOpt { special = "gate", hardness = 2, tool = "axe" });
            }
            foreach (var row in new (int id, string name, string tex)[] {
              (B.OAK_TRAPDOOR, "Дубовый люк", "oak_trapdoor"),
              (B.SPRUCE_TRAPDOOR, "Еловый люк", "spruce_trapdoor"),
              (B.BIRCH_TRAPDOOR, "Берёзовый люк", "birch_trapdoor"),
            }) {
              var id = row.id; var name = row.name; var tex = row.tex;
              def2(id, name, tex, tex, tex, new BOpt { special = "trapdoor", transparent = true, cutout = true, hardness = 1.5, tool = "axe" });
            }
            def2(B.GLASS_PANE, "Стеклянная панель", "glass", "glass", "glass", new BOpt { special = "pane", transparent = true, alpha = 0.48, hardness = 0.3 });
            def2(B.WHITE_CARPET, "Белый ковёр", "white_wool", "white_wool", "white_wool", new BOpt { special = "carpet", hardness = 0.1 });
            def2(B.STONE_PLATE, "Каменная нажимная плита", "stone", "stone", "stone", new BOpt { special = "plate", hardness = 0.5, tool = "pickaxe" });
            def2(B.STONE_BUTTON, "Каменная кнопка", "stone", "stone", "stone", new BOpt { special = "button", solid = false, hardness = 0.5, tool = "pickaxe" });
            def2(B.VINE, "Лианы", "vine", "vine", "vine", new BOpt { special = "vine", solid = false, transparent = true, cutout = true, hardness = 0.2 });
            def2(B.GLOW_LICHEN, "Светящийся лишайник", "glow_lichen", "glow_lichen", "glow_lichen", new BOpt { special = "lichen",
              solid = false,
              transparent = true,
              cutout = true,
              light = 7,
              hardness = 0.2, });
            def2(B.LILY_PAD, "Кувшинка", "lily_pad", "lily_pad", "lily_pad", new BOpt { special = "lilypad", transparent = true, cutout = true, hardness = 0.1 });
            def2(B.SEA_PICKLE, "Морской огурец", "sea_pickle", "sea_pickle", "sea_pickle", new BOpt { special = "seapickle",
              solid = false,
              transparent = true,
              cutout = true,
              waterPlant = true,
              light = 6,
              hardness = 0.1, });
            def2(B.BAMBOO, "Бамбук", "bamboo_stalk", "bamboo_stalk", "bamboo_stalk", new BOpt { special = "bamboo",
              solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 1, });
            def2(B.COBBLE_WALL, "Булыжная ограда", "cobblestone", "cobblestone", "cobblestone", new BOpt { special = "wall", hardness = 2, tool = "pickaxe" });
            def2(B.FLOWER_POT, "Цветочный горшок", "flower_pot", "flower_pot", "flower_pot", new BOpt { special = "pot", hardness = 0.2 });
            def2(B.SPRUCE_LOG, "Еловое бревно", "spruce_log", "spruce_log_top", "spruce_log_top", new BOpt { hardness = 2, tool = "axe" });
            def2(B.SPRUCE_LEAVES, "Еловая листва", "spruce_leaves", "spruce_leaves", "spruce_leaves", new BOpt { transparent = true,
              cutout = true,
              hardness = 0.25, });
            def2(B.BIRCH_LOG, "Берёзовое бревно", "birch_log", "birch_log_top", "birch_log_top", new BOpt { hardness = 2, tool = "axe" });
            def2(B.BIRCH_LEAVES, "Берёзовая листва", "birch_leaves", "birch_leaves", "birch_leaves", new BOpt { transparent = true,
              cutout = true,
              hardness = 0.25, });
            def2(B.JUNGLE_LOG, "Тропическое бревно", "jungle_log", "jungle_log_top", "jungle_log_top", new BOpt { hardness = 2, tool = "axe" });
            def2(B.JUNGLE_LEAVES, "Тропическая листва", "jungle_leaves", "jungle_leaves", "jungle_leaves", new BOpt { transparent = true,
              cutout = true,
              hardness = 0.25, });
            def2(B.ACACIA_LOG, "Бревно акации", "acacia_log", "acacia_log_top", "acacia_log_top", new BOpt { hardness = 2, tool = "axe" });
            def2(B.ACACIA_LEAVES, "Листва акации", "acacia_leaves", "acacia_leaves", "acacia_leaves", new BOpt { transparent = true,
              cutout = true,
              hardness = 0.25, });
            def2(B.DARK_LOG, "Бревно тёмного дуба", "dark_oak_log", "dark_oak_log_top", "dark_oak_log_top", new BOpt { hardness = 2, tool = "axe" });
            def2(B.DARK_LEAVES, "Листва тёмного дуба", "dark_oak_leaves", "dark_oak_leaves", "dark_oak_leaves", new BOpt { transparent = true,
              cutout = true,
              hardness = 0.25, });
            def2(B.CHERRY_LEAVES, "Цветущая листва", "flowering_azalea_leaves", "flowering_azalea_leaves", "flowering_azalea_leaves", new BOpt { transparent = true,
              cutout = true,
              hardness = 0.25, });
            def2(B.PODZOL, "Подзол", "podzol_side", "podzol_top", "dirt", new BOpt { hardness = 0.6, tool = "shovel" });
            def2(B.TERRACOTTA, "Терракота", "terracotta", "terracotta", "terracotta", new BOpt { hardness = 1.3, tool = "pickaxe", tier = 1 });
            def2(B.TERRA_ORANGE, "Оранжевая терракота", "orange_terracotta", "orange_terracotta", "orange_terracotta", new BOpt { hardness = 1.3,
              tool = "pickaxe",
              tier = 1, });
            def2(B.TERRA_WHITE, "Белая терракота", "white_terracotta", "white_terracotta", "white_terracotta", new BOpt { hardness = 1.3,
              tool = "pickaxe",
              tier = 1, });
            def2(B.PACKED_ICE, "Плотный лёд", "packed_ice", "packed_ice", "packed_ice", new BOpt { hardness = 0.6, tool = "pickaxe" });
            def2(B.MYCELIUM, "Мицелий", "mycelium_side", "mycelium_top", "dirt", new BOpt { hardness = 0.6, tool = "shovel" });
            def2(B.MUD, "Грязь", "mud", "mud", "mud", new BOpt { hardness = 0.5, tool = "shovel" });
            def2(B.TUBE_CORAL_BLOCK, "Трубчатый коралл", "tube_coral_block", "tube_coral_block", "tube_coral_block", new BOpt { hardness = 1.5,
              tool = "pickaxe", });
            def2(B.TUBE_CORAL_FAN, "Трубчатый веерный коралл", "tube_coral_fan", "tube_coral_fan", "tube_coral_fan", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              waterPlant = true,
              hardness = 0.1, });
            def2(B.BRAIN_CORAL_BLOCK, "Мозговой коралл", "brain_coral_block", "brain_coral_block", "brain_coral_block", new BOpt { hardness = 1.5,
              tool = "pickaxe", });
            def2(B.BRAIN_CORAL_FAN, "Мозговой веерный коралл", "brain_coral_fan", "brain_coral_fan", "brain_coral_fan", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              waterPlant = true,
              hardness = 0.1, });
            def2(B.BUBBLE_CORAL_BLOCK, "Пузырчатый коралл", "bubble_coral_block", "bubble_coral_block", "bubble_coral_block", new BOpt { hardness = 1.5,
              tool = "pickaxe", });
            def2(B.BUBBLE_CORAL_FAN, "Пузырчатый веерный коралл", "bubble_coral_fan", "bubble_coral_fan", "bubble_coral_fan", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              waterPlant = true,
              hardness = 0.1, });
            def2(B.FIRE_CORAL_BLOCK, "Огненный коралл", "fire_coral_block", "fire_coral_block", "fire_coral_block", new BOpt { hardness = 1.5, tool = "pickaxe" });
            def2(B.FIRE_CORAL_FAN, "Огненный веерный коралл", "fire_coral_fan", "fire_coral_fan", "fire_coral_fan", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              waterPlant = true,
              hardness = 0.1, });
            def2(B.HORN_CORAL_BLOCK, "Роговой коралл", "horn_coral_block", "horn_coral_block", "horn_coral_block", new BOpt { hardness = 1.5, tool = "pickaxe" });
            def2(B.HORN_CORAL_FAN, "Роговой веерный коралл", "horn_coral_fan", "horn_coral_fan", "horn_coral_fan", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              waterPlant = true,
              hardness = 0.1, });
            def2(B.MOSS_BLOCK, "Блок мха", "moss_block", "moss_block", "moss_block", new BOpt { hardness = 0.2, tool = "shovel" });
            def2(B.ROOTED_DIRT, "Корневая земля", "rooted_dirt", "rooted_dirt", "rooted_dirt", new BOpt { hardness = 0.6, tool = "shovel" });
            def2(B.DRIPSTONE_BLOCK, "Капельниковый блок", "dripstone_block", "dripstone_block", "dripstone_block", new BOpt { hardness = 1.5,
              tool = "pickaxe",
              tier = 1, });
            def2(B.CAVE_VINES, "Пещерные лианы", "cave_vines", "cave_vines", "cave_vines", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.1, });
            def2(B.DEEPSLATE, "Глубинный сланец", "deepslate", "deepslate", "deepslate", new BOpt { hardness = 3, tool = "pickaxe", tier = 1 });
            def2(B.TUFF, "Туф", "tuff", "tuff", "tuff", new BOpt { hardness = 1.5, tool = "pickaxe", tier = 1 });
            def2(B.MOSS_CARPET, "Моховой ковёр", "moss_block", "moss_block", "moss_block", new BOpt { special = "carpet", hardness = 0.1 });
            def2(B.CALCITE, "Кальцит", "calcite", "calcite", "calcite", new BOpt { hardness = 0.75, tool = "pickaxe" });
            def2(B.AMETHYST_BLOCK, "Аметистовый блок", "amethyst_block", "amethyst_block", "amethyst_block", new BOpt { hardness = 1.5,
              tool = "pickaxe",
              tier = 1, });
            def2(B.BUDDING_AMETHYST, "Почкующийся аметист", "budding_amethyst", "budding_amethyst", "budding_amethyst", new BOpt { hardness = 1.5,
              tool = "pickaxe",
              tier = 1, });
            def2(B.AZALEA_LEAVES, "Листья азалии", "azalea_leaves", "azalea_leaves", "azalea_leaves", new BOpt { transparent = true,
              cutout = true,
              hardness = 0.25, });
            def2(B.FLOWERING_AZALEA_LEAVES, "Цветущая азалия", "flowering_azalea_leaves", "flowering_azalea_leaves", "flowering_azalea_leaves", new BOpt { transparent = true,
              cutout = true,
              hardness = 0.25, });
            def2(B.GRANITE, "Гранит", "granite", "granite", "granite", new BOpt { hardness = 1.5, tool = "pickaxe", tier = 1 });
            def2(B.DIORITE, "Диорит", "diorite", "diorite", "diorite", new BOpt { hardness = 1.5, tool = "pickaxe", tier = 1 });
            def2(B.ANDESITE, "Андезит", "andesite", "andesite", "andesite", new BOpt { hardness = 1.5, tool = "pickaxe", tier = 1 });
            def2(B.REDSTONE_ORE, "Красная руда", "redstone_ore", "redstone_ore", "redstone_ore", new BOpt { hardness = 3, tool = "pickaxe", tier = 3 });
            def2(B.LAPIS_ORE, "Лазуритовая руда", "lapis_ore", "lapis_ore", "lapis_ore", new BOpt { hardness = 3, tool = "pickaxe", tier = 2 });
            def2(B.EMERALD_ORE, "Изумрудная руда", "emerald_ore", "emerald_ore", "emerald_ore", new BOpt { hardness = 3, tool = "pickaxe", tier = 3 });
            def2(B.DEEPSLATE_COAL_ORE, "Сланцевая угольная руда", "deepslate_coal_ore", "deepslate_coal_ore", "deepslate_coal_ore", new BOpt { hardness = 4.5,
              tool = "pickaxe",
              tier = 1, });
            def2(B.DEEPSLATE_IRON_ORE, "Сланцевая железная руда", "deepslate_iron_ore", "deepslate_iron_ore", "deepslate_iron_ore", new BOpt { hardness = 4.5,
              tool = "pickaxe",
              tier = 2, });
            def2(B.DEEPSLATE_GOLD_ORE, "Сланцевая золотая руда", "deepslate_gold_ore", "deepslate_gold_ore", "deepslate_gold_ore", new BOpt { hardness = 4.5,
              tool = "pickaxe",
              tier = 3, });
            def2(B.DEEPSLATE_DIAMOND_ORE, "Сланцевая алмазная руда", "deepslate_diamond_ore", "deepslate_diamond_ore", "deepslate_diamond_ore", new BOpt { hardness = 4.5,
              tool = "pickaxe",
              tier = 3, });
            def2(B.DEEPSLATE_REDSTONE_ORE, "Сланцевая красная руда", "deepslate_redstone_ore", "deepslate_redstone_ore", "deepslate_redstone_ore", new BOpt { hardness = 4.5,
              tool = "pickaxe",
              tier = 3, });
            def2(B.DEEPSLATE_LAPIS_ORE, "Сланцевая лазуритовая руда", "deepslate_lapis_ore", "deepslate_lapis_ore", "deepslate_lapis_ore", new BOpt { hardness = 4.5,
              tool = "pickaxe",
              tier = 2, });
            def2(B.DEEPSLATE_EMERALD_ORE, "Сланцевая изумрудная руда", "deepslate_emerald_ore", "deepslate_emerald_ore", "deepslate_emerald_ore", new BOpt { hardness = 4.5,
              tool = "pickaxe",
              tier = 3, });
            def2(B.DEEPSLATE_COPPER_ORE, "Сланцевая медная руда", "deepslate_copper_ore", "deepslate_copper_ore", "deepslate_copper_ore", new BOpt { hardness = 4.5,
              tool = "pickaxe",
              tier = 2, });
            def2(B.SHIP_WHEEL, "Штурвал корабля", "ship_wheel", "ship_wheel", "ship_wheel", new BOpt { special = "shipwheel", hardness = 2, tool = "axe" });
            def2(B.SHORT_GRASS, "Трава", "short_grass", "short_grass", "short_grass", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.OXEYE_DAISY, "Ромашка", "oxeye_daisy", "oxeye_daisy", "oxeye_daisy", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.LILY_OF_THE_VALLEY, "Ландыш", "lily_of_the_valley", "lily_of_the_valley", "lily_of_the_valley", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.ORANGE_TULIP, "Оранжевый тюльпан", "orange_tulip", "orange_tulip", "orange_tulip", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.PINK_TULIP, "Розовый тюльпан", "pink_tulip", "pink_tulip", "pink_tulip", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.RED_TULIP, "Красный тюльпан", "red_tulip", "red_tulip", "red_tulip", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.WHITE_TULIP, "Белый тюльпан", "white_tulip", "white_tulip", "white_tulip", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.SWEET_BERRY_BUSH, "Куст сладких ягод", "sweet_berry_bush", "sweet_berry_bush", "sweet_berry_bush", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.15, });
            def2(B.SUGAR_CANE, "Сахарный тростник", "sugar_cane", "sugar_cane", "sugar_cane", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.1, });
            def2(B.LARGE_FERN, "Большой папоротник", "large_fern_bottom", "large_fern_bottom", "large_fern_bottom", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.LARGE_FERN_TOP, "Большой папоротник", "large_fern_top", "large_fern_top", "large_fern_top", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.SUNFLOWER, "Подсолнечник", "sunflower_bottom", "sunflower_bottom", "sunflower_bottom", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.SUNFLOWER_TOP, "Подсолнечник", "sunflower_front", "sunflower_front", "sunflower_front", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.LILAC, "Сирень", "lilac_bottom", "lilac_bottom", "lilac_bottom", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.LILAC_TOP, "Сирень", "lilac_top", "lilac_top", "lilac_top", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.ROSE_BUSH, "Розовый куст", "rose_bush_bottom", "rose_bush_bottom", "rose_bush_bottom", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.ROSE_BUSH_TOP, "Розовый куст", "rose_bush_top", "rose_bush_top", "rose_bush_top", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.PEONY, "Пион", "peony_bottom", "peony_bottom", "peony_bottom", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.PEONY_TOP, "Пион", "peony_top", "peony_top", "peony_top", new BOpt { solid = false,
              transparent = true,
              cutout = true,
              plant = true,
              hardness = 0.05, });
            def2(B.MUSHROOM_STEM, "Ножка гриба", "mushroom_stem", "mushroom_stem", "mushroom_stem", new BOpt { hardness = 0.2, tool = "axe" });
            def2(B.RED_MUSHROOM_BLOCK, "Блок красного гриба", "red_mushroom_block", "red_mushroom_block", "red_mushroom_block", new BOpt { hardness = 0.2,
              tool = "axe", });
            def2(B.BROWN_MUSHROOM_BLOCK, "Блок коричневого гриба", "brown_mushroom_block", "brown_mushroom_block", "brown_mushroom_block", new BOpt { hardness = 0.2,
              tool = "axe", });
            def2(B.CUT_SANDSTONE, "Резной песчаник", "cut_sandstone", "cut_sandstone", "cut_sandstone", new BOpt { hardness = 0.8, tool = "pickaxe" });
            def2(B.CHISELED_SANDSTONE, "Точёный песчаник", "chiseled_sandstone", "chiseled_sandstone", "chiseled_sandstone", new BOpt { hardness = 0.8,
              tool = "pickaxe", });
            def2(B.SANDSTONE_SLAB, "Песчаниковая плита", "sandstone", "sandstone", "sandstone", new BOpt { special = "slab", hardness = 0.8, tool = "pickaxe" });
            def2(B.SANDSTONE_STAIRS, "Песчаниковые ступени", "sandstone", "sandstone", "sandstone", new BOpt { special = "stairs",
              hardness = 0.8,
              tool = "pickaxe", });
            def2(B.MAGMA_BLOCK, "Магма", "magma", "magma", "magma", new BOpt { hardness = 0.5, tool = "pickaxe", light = 3 });
            def2(B.BLUE_TERRACOTTA, "Синяя терракота", "blue_terracotta", "blue_terracotta", "blue_terracotta", new BOpt { hardness = 1.25, tool = "pickaxe" });
            def2(B.DARK_PLANKS, "Доски тёмного дуба", "dark_oak_planks", "dark_oak_planks", "dark_oak_planks", new BOpt { hardness = 2, tool = "axe" });
            def2(B.DARK_SLAB, "Плита тёмного дуба", "dark_oak_planks", "dark_oak_planks", "dark_oak_planks", new BOpt { special = "slab",
              hardness = 2,
              tool = "axe", });
            def2(B.DARK_STAIRS, "Ступени тёмного дуба", "dark_oak_planks", "dark_oak_planks", "dark_oak_planks", new BOpt { special = "stairs",
              hardness = 2,
              tool = "axe", });
            def2(B.DARK_FENCE, "Забор тёмного дуба", "dark_oak_planks", "dark_oak_planks", "dark_oak_planks", new BOpt { special = "fence",
              hardness = 2,
              tool = "axe", });
            
            def2(B.WOOL_BLACK, "Чёрная шерсть", "black_wool", "black_wool", "black_wool", new BOpt { hardness = 0.8 });
            def2(B.WOOL_GRAY, "Серая шерсть", "gray_wool", "gray_wool", "gray_wool", new BOpt { hardness = 0.8 });
            def2(B.WOOL_LIGHT_GRAY, "Светло-серая шерсть", "light_gray_wool", "light_gray_wool", "light_gray_wool", new BOpt { hardness = 0.8 });
            def2(B.WOOL_BROWN, "Коричневая шерсть", "brown_wool", "brown_wool", "brown_wool", new BOpt { hardness = 0.8 });
            def2(B.WOOL_PINK, "Розовая шерсть", "pink_wool", "pink_wool", "pink_wool", new BOpt { hardness = 0.8 });
            def2(B.VIRTUAL_OPAQUE, "Virtual Block", "stone", "stone", "stone", new BOpt { special = "virtual", hardness = 1.5 });
            def2(B.VIRTUAL_TRANSPARENT, "Virtual Glass", "glass", "glass", "glass", new BOpt { special = "virtual", hardness = 0.4, transparent = true, alpha = 1 });
            def2(B.VIRTUAL_CUTOUT, "Virtual Cutout", "oak_trapdoor", "oak_trapdoor", "oak_trapdoor", new BOpt { special = "virtual",
              hardness = 0.8,
              transparent = true,
              cutout = true, });
            def2(B.VIRTUAL_PLANT, "Virtual Plant", "short_grass", "short_grass", "short_grass", new BOpt { special = "virtual",
              hardness = 0.2,
              solid = false,
              transparent = true,
              cutout = true,
              plant = true, });
            
            // Mining metadata for the original block set.
            assignBlock(B.GRASS, new BOpt { hardness = 0.6, tool = "shovel" });
            assignBlock(B.DIRT, new BOpt { hardness = 0.5, tool = "shovel" });
            assignBlock(B.STONE, new BOpt { hardness = 1.8, tool = "pickaxe", tier = 1 });
            assignBlock(B.SAND, new BOpt { hardness = 0.6, tool = "shovel" });
            assignBlock(B.LOG, new BOpt { hardness = 2, tool = "axe" });
            assignBlock(B.LEAVES, new BOpt { hardness = 0.25 });
            assignBlock(B.PLANKS, new BOpt { hardness = 2, tool = "axe" });
            assignBlock(B.COBBLE, new BOpt { hardness = 2, tool = "pickaxe", tier = 1 });
            assignBlock(B.GLASS, new BOpt { hardness = 0.45 });
            assignBlock(B.BRICKS, new BOpt { hardness = 2, tool = "pickaxe", tier = 1 });
            assignBlock(B.COAL, new BOpt { hardness = 3, tool = "pickaxe", tier = 1 });
            assignBlock(B.IRON, new BOpt { hardness = 3, tool = "pickaxe", tier = 2 });
            assignBlock(B.GOLD, new BOpt { hardness = 3, tool = "pickaxe", tier = 3 });
            assignBlock(B.DIAMOND, new BOpt { hardness = 3, tool = "pickaxe", tier = 3 });
            assignBlock(B.BEDROCK, new BOpt { hardness = 1e9, tier = 99 });
            assignBlock(B.GRAVEL, new BOpt { hardness = 0.6, tool = "shovel" });
            assignBlock(B.SNOW, new BOpt { hardness = 0.2, tool = "shovel" });
            assignBlock(B.CLAY, new BOpt { hardness = 0.6, tool = "shovel" });
            assignBlock(B.OBSIDIAN, new BOpt { hardness = 15, tool = "pickaxe", tier = 4 });
            assignBlock(B.STONEBRICK, new BOpt { hardness = 2, tool = "pickaxe", tier = 1 });
            assignBlock(B.MOSSY, new BOpt { hardness = 2, tool = "pickaxe", tier = 1 });
            assignBlock(B.CRAFT, new BOpt { hardness = 2, tool = "axe" });
            assignBlock(B.TNT, new BOpt { hardness = 0 });
            assignBlock(B.PUMPKIN, new BOpt { hardness = 0.7 });
            assignBlock(B.MELON, new BOpt { hardness = 0.7 });
            assignBlock(B.BOOKSHELF, new BOpt { hardness = 1.5, tool = "axe" });
            // main.js Z6 mining metadata parity. This runs once at startup; the mining hot path remains table-only.
            applyMainNativeMiningProps();
            compileExplosionResistance();
        }

        static readonly Dictionary<string, string> MAIN_MINING_ALIAS = new Dictionary<string, string> {
            {"BRICKS","BRICK"},{"STONEBRICK","STONE_BRICKS"},{"CRAFT","CRAFTING_TABLE"},{"COPPER","COPPER_ORE"},{"FURNACE_LIT","FURNACE"},
            {"DOOR","OAK_DOOR"},{"DOOR_TOP","OAK_DOOR"},{"DOOR_OPEN","OAK_DOOR"},{"DOOR_OPEN_TOP","OAK_DOOR"},{"FARMLAND_MOIST","FARMLAND"},
            {"TERRA_ORANGE","ORANGE_TERRACOTTA"},{"TERRA_WHITE","WHITE_TERRACOTTA"},{"SHORT_GRASS","TALL_GRASS"},{"DARK_SLAB","DARK_PLANKS_SLAB"},
            {"DARK_STAIRS","DARK_PLANKS_STAIRS"},{"DARK_FENCE","DARK_PLANKS_FENCE"},{"LARGE_FERN_TOP","LARGE_FERN"},{"SUNFLOWER_TOP","SUNFLOWER"},
            {"LILAC_TOP","LILAC"},{"ROSE_BUSH_TOP","ROSE_BUSH"},{"PEONY_TOP","PEONY"},{"CARROTS0","CARROTS_0"},{"CARROTS1","CARROTS_1"},
            {"CARROTS2","CARROTS_2"},{"CARROTS3","CARROTS_3"},{"POTATOES0","POTATOES_0"},{"POTATOES1","POTATOES_1"},{"POTATOES2","POTATOES_2"},
            {"POTATOES3","POTATOES_3"},{"PUMPKIN_STEM0","PUMPKIN_STEM_0"},{"PUMPKIN_STEM1","PUMPKIN_STEM_1"},{"PUMPKIN_STEM2","PUMPKIN_STEM_2"},
            {"PUMPKIN_STEM3","PUMPKIN_STEM_3"},{"MELON_STEM0","MELON_STEM_0"},{"MELON_STEM1","MELON_STEM_1"},{"MELON_STEM2","MELON_STEM_2"},
            {"MELON_STEM3","MELON_STEM_3"} };
        static readonly Dictionary<string, string> MAIN_MINING_BASE = new Dictionary<string, string> {
            {"OAK_SLAB","PLANKS"},{"SPRUCE_SLAB","SPRUCE_PLANKS"},{"BIRCH_SLAB","BIRCH_PLANKS"},{"OAK_STAIRS","PLANKS"},{"SPRUCE_STAIRS","SPRUCE_PLANKS"},{"BIRCH_STAIRS","BIRCH_PLANKS"} };
        static bool rx(string pattern, string s) { return Regex.IsMatch(s, pattern); }
        static int mainMiningTier(string sym) { return rx("DIAMOND|EMERALD|GOLD|REDSTONE", sym) ? 3 : rx("^IRON|IRON_ORE|LAPIS|COPPER", sym) ? 2 : 1; }
        public struct MiningProps { public double hardness; public string tool; public int tier; public MiningProps(double h, string t, int ti) { hardness = h; tool = t; tier = ti; } }
        public static MiningProps mainMiningProps(string sym, string shape, bool plant)
        {
            if (sym == "COBWEB") return new MiningProps(4, null, 0);
            if ((plant && shape != "bamboo" && shape != "web") || shape == "torch" || shape == "vine" || shape == "lichen" || shape == "lilypad" || shape == "seapickle" || shape == "carpet" || shape == "rail" || shape == "tallplant") return new MiningProps(0, null, 0);
            if (sym == "BEDROCK") return new MiningProps(1e9, null, 99);
            if (sym == "TNT" || sym == "FIRE") return new MiningProps(0, null, 0);
            if (sym == "CHEST") return new MiningProps(2.5, "axe", 0);
            if (sym == "BED" || rx("^BED_", sym)) return new MiningProps(0.2, null, 0);
            if (rx("LEAVES", sym)) return new MiningProps(0.25, null, 0);
            if (rx("GLASS|PANE|^ICE$|PACKED_ICE|BLUE_ICE", sym)) return new MiningProps(0.45, null, 0);
            if (sym == "SNOW") return new MiningProps(0.2, "shovel", 0);
            if (rx("OBSIDIAN", sym)) return new MiningProps(15, "pickaxe", 4);
            if (rx("ANCIENT_DEBRIS", sym)) return new MiningProps(10, "pickaxe", 4);
            if (rx("DEEPSLATE.*_ORE", sym)) return new MiningProps(4.5, "pickaxe", mainMiningTier(sym));
            if (rx("_ORE$|^COAL$|^IRON$|^GOLD$|^DIAMOND$", sym)) return new MiningProps(3, "pickaxe", mainMiningTier(sym));
            if (rx("^(IRON|GOLD|DIAMOND|EMERALD|NETHERITE|COAL|LAPIS|REDSTONE|AMETHYST)_BLOCK$|RAW_.*_BLOCK|BUDDING", sym))
                return new MiningProps(5, "pickaxe", rx("IRON|COAL|LAPIS|RAW_IRON|RAW_COPPER|AMETHYST|BUDDING", sym) ? 2 : 3);
            if (rx("COPPER", sym) && !rx("ORE", sym)) return new MiningProps(3, "pickaxe", 2);
            if (rx("NETHERRACK", sym)) return new MiningProps(0.4, "pickaxe", 1);
            if (rx("LOG|WOOD$|PLANKS$|^PLANKS$|BOOKSHELF|CRAFTING_TABLE|STEM$|HYPHAE|BAMBOO_BLOCK|_FENCE|_GATE|LADDER|MUSHROOM_STEM|MUSHROOM_BLOCK", sym)) return new MiningProps(2, "axe", 0);
            if (rx("DOOR|TRAPDOOR", sym)) return rx("IRON|COPPER", sym) ? new MiningProps(5, "pickaxe", 1) : new MiningProps(2, "axe", 0);
            if (rx("^(DIRT|GRASS|PODZOL|MYCELIUM|COARSE_DIRT|ROOTED_DIRT|DIRT_PATH|MUD)$|^SAND$|RED_SAND$|GRAVEL|CLAY|SOUL_|CONCRETE_POWDER|FARM", sym)) return new MiningProps(0.6, "shovel", 0);
            if (rx("WOOL|SPONGE|HAY|CARPET|HONEYCOMB|DRIED_KELP|NETHER_WART_BLOCK|WARPED_WART|MOSS_BLOCK|SHROOMLIGHT|PUMPKIN|MELON|CORAL", sym)) return new MiningProps(0.7, null, 0);
            if (rx("^GLOW$|SEA_LANTERN|JACK", sym)) return new MiningProps(0.4, null, 0);
            if (rx("TERRACOTTA|GLAZED", sym)) return new MiningProps(1.3, "pickaxe", 1);
            if (rx("CONCRETE$", sym)) return new MiningProps(1.8, "pickaxe", 1);
            if (rx("FURNACE|STONE|COBBLE|DEEPSLATE|BRICK|ANDESITE|DIORITE|GRANITE|BASALT|TUFF|CALCITE|DRIPSTONE|SANDSTONE|PRISMARINE|PURPUR|QUARTZ|BLACKSTONE|MAGMA|END_|NETHER|BONE", sym)) return new MiningProps(1.8, "pickaxe", 1);
            if (rx("CACTUS|SUGAR|BAMBOO", sym)) return new MiningProps(0.3, null, 0);
            return new MiningProps(1, null, 0);
        }
        static void applyMainNativeMiningProps()
        {
            foreach (var raw in B.ORDER)
            {
                int id = B.NAMES[raw]; var b = blocks[id];
                if (b == null || id == B.AIR || id >= B.VIRTUAL_OPAQUE || isFluidWater(id) || isLava(id)) continue;
                string sym; if (!MAIN_MINING_BASE.TryGetValue(raw, out sym) && !MAIN_MINING_ALIAS.TryGetValue(raw, out sym)) sym = raw;
                var p = mainMiningProps(sym, b.special, b.plant);
                b.hardness = p.hardness; b.tool = p.tool; b.tier = p.tier;
            }
        }
        // main.js explosion-resistance parity. Values are compiled once; explosions only do table/meta lookups.
        public static double mainExplosionResistance(string sym)
        {
            sym = sym ?? "";
            if (rx("^AIR$", sym)) return 0;
            if (rx("BEDROCK|OBSIDIAN", sym)) return 1200;
            if (rx("WATER|FLOW|LAVA", sym)) return 100;
            if (rx("^TNT$", sym)) return 0;
            if (rx("END_STONE", sym)) return 9;
            if (rx("SANDSTONE", sym)) return 0.8;
            if (rx("IRON|GOLD|DIAMOND|EMERALD|NETHERITE|ANVIL", sym) || rx("STONE|COBBLE|DEEPSLATE|BRICK|ANDESITE|DIORITE|GRANITE|TUFF|BLACKSTONE|TERRA|CONCRETE|QUARTZ|PRISMARINE|CALCITE|PURPUR|NETHERRACK|ORE|FURNACE|MAGMA", sym)) return 6;
            if (rx("GLASS|ICE|LEAVES|WOOL|CARPET|FLOWER|SAPLING|FERN|BUSH|GRASS_|VINE|KELP|SEAGRASS|LILY|TORCH|SNOW", sym)) return 0.3;
            if (rx("DIRT|GRASS|GRAVEL|SAND$|SAND_|SANDSTONE|PODZOL|CLAY|MUD", sym)) return 0.5;
            return 3;
        }
        public static readonly float[] MAIN_EXPLOSION_RESISTANCE = new float[256];
        public static readonly string[] MAIN_BLOCK_SYMBOL = new string[256];
        static void compileExplosionResistance()
        {
            for (int i = 0; i < 256; i++) { MAIN_EXPLOSION_RESISTANCE[i] = 3; MAIN_BLOCK_SYMBOL[i] = null; }
            foreach (var raw in B.ORDER)
            {
                int id = B.NAMES[raw];
                if (id < 0 || id >= 256) continue;
                if (MAIN_BLOCK_SYMBOL[id] == null) { string s; if (!MAIN_MINING_BASE.TryGetValue(raw, out s) && !MAIN_MINING_ALIAS.TryGetValue(raw, out s)) s = raw; MAIN_BLOCK_SYMBOL[id] = s; }
            }
            for (int id = 0; id < 256; id++) MAIN_EXPLOSION_RESISTANCE[id] = (float)mainExplosionResistance(MAIN_BLOCK_SYMBOL[id] ?? "");
            foreach (var kv in VIRTUAL_BLOCKS) kv.Value.explosionResistance = mainExplosionResistance(kv.Key);
        }
        public static double explosionResistanceAt(int x, int y, int z) { return explosionResistanceAt(x, y, z, getBlock(x, y, z)); }
        public static double explosionResistanceAt(int x, int y, int z, int id)
        {
            if (isVirtualId(id)) { var d = virtualDefAt(x, y, z); return d != null ? d.explosionResistance : 3; }
            return id >= 0 && id < MAIN_EXPLOSION_RESISTANCE.Length ? MAIN_EXPLOSION_RESISTANCE[id] : 3;
        }
    }
}
