// Port of blocks.js: shared block registry, turns BLOCK_TABLE into flat arrays for hot loops.
// Used by the main thread and by the worker threads (generator + mesher).
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Maincraft
{
    /// <summary>Block shapes.</summary>
    public static class SH
    {
        public const int AIR = 0, CUBE = 1, CROSS = 2, WATER = 3, LAVA = 4, SLAB = 5, STAIRS = 6, FENCE = 7, GATE = 8, WALL = 9,
            PANE = 10, DOOR = 11, TRAPDOOR = 12, LADDER = 13, TORCH = 14, CARPET = 15, PLATE = 16, BUTTON = 17,
            TALL = 18, VINE = 19, LICHEN = 20, LILY = 21, PICKLE = 22, BAMBOO = 23, POT = 24, RAIL = 25, CACTUS = 26,
            CHEST = 27, BED = 28, FARMLAND = 29, FIRE = 30, SNOWLAYER = 31, CRYSTAL = 32, MODEL = 33, WIRE = 34;
    }

    public delegate int CellGet(int dx, int dy, int dz);

    public static class Blocks
    {
        public const int WORLD_MIN_Y = -64, WORLD_H = 384, WORLD_MAX_Y = WORLD_MIN_Y + WORLD_H, SECTIONS = 24, SEA = 63;

        static readonly Dictionary<string, int> SHAPE_BY_NAME = new Dictionary<string, int>
        {
            [""] = SH.CUBE, ["x"] = SH.CROSS, ["water"] = SH.WATER, ["lava"] = SH.LAVA, ["slab"] = SH.SLAB, ["stairs"] = SH.STAIRS,
            ["fence"] = SH.FENCE, ["gate"] = SH.GATE, ["wall"] = SH.WALL, ["pane"] = SH.PANE, ["door"] = SH.DOOR, ["trapdoor"] = SH.TRAPDOOR,
            ["ladder"] = SH.LADDER, ["torch"] = SH.TORCH, ["carpet"] = SH.CARPET, ["plate"] = SH.PLATE, ["button"] = SH.BUTTON,
            ["tallplant"] = SH.TALL, ["vine"] = SH.VINE, ["lichen"] = SH.LICHEN, ["lilypad"] = SH.LILY, ["seapickle"] = SH.PICKLE,
            ["bamboo"] = SH.BAMBOO, ["pot"] = SH.POT, ["rail"] = SH.RAIL, ["cactus"] = SH.CACTUS, ["chest"] = SH.CHEST, ["bed"] = SH.BED,
            ["farmland"] = SH.FARMLAND, ["fire"] = SH.FIRE, ["snowlayer"] = SH.SNOWLAYER, ["crystal"] = SH.CRYSTAL, ["model"] = SH.MODEL, ["wire"] = SH.WIRE,
        };

        // render layers
        public const int RL_SOLID = 0, RL_CUTOUT = 1, RL_TRANS = 2;
        // block flag bits
        public const int BF_LEAVES = 1, BF_AXIS = 2, BF_GLASS = 4, BF_TRANS = 8, BF_FALL = 16, BF_EMISSIVE = 32,
            BF_AQUATIC = 64, BF_NEEDWATER = 128, BF_HANG = 256, BF_CLIMB = 512, BF_STACK = 1024,
            BF_FACING = 2048, BF_REPLACE = 4096, BF_PLANT = 8192, BF_WET = 16384;

        public static readonly int NB = BlockData.BLOCK_TABLE.Length + 1;
        // Waterlogged blocks (Minecraft's waterlogged=true): a twin id WET_BASE + id with the same shape,
        // textures and behaviour that also holds a water source. Fixed offset, so saved ids stay valid.
        public const int WET_BASE = 4096;
        public static readonly int NBX = WET_BASE + NB;

        public static readonly Dictionary<string, int> BK = new Dictionary<string, int> { ["AIR"] = 0 };
        public static readonly string[] B_KEY = new string[NBX], B_EN = new string[NBX], B_RU = new string[NBX];
        public static readonly byte[] SHAPE = new byte[NBX], OPAQUE = new byte[NBX], RLAYER = new byte[NBX];
        public static readonly byte[] LCOST = new byte[NBX], VDIM = new byte[NBX], EMIT = new byte[NBX], SOLID = new byte[NBX];
        // blocks bodies bump into: the solid ones, and ladders (a thin board against the wall, as in Minecraft)
        public static readonly byte[] COLLIDE = new byte[NBX];
        public static readonly ushort[] FLAGS = new ushort[NBX], BASE = new ushort[NBX];
        public static readonly byte[] TAB = new byte[NBX];
        public static readonly float[] HARD = new float[NBX];
        public static readonly byte[] TOOL = new byte[NBX];
        // face textures (layer index): 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z ; FRONT = facing face texture
        public static readonly ushort[] FTEX = new ushort[NBX * 6], FRONT = new ushort[NBX];
        public static readonly string[][] TEXNAMES = new string[NBX][]; // raw texture names per block (for icons / worker tex resolve)
        public static readonly string[] TABS = { "b", "c", "n", "f" };

        // Minecraft's explosion resistance
        public static readonly float[] BLAST = new float[NBX];

        // model variants / selection boxes by block id (filled from Models)
        public static readonly MPart[][][] MODEL_VAR_ID = new MPart[NBX][][];
        public static readonly double[][][] MODEL_BOX_ID = new double[NBX][][];

        // waterlogged twins of the shapes Minecraft lets hold water
        public static readonly ushort[] WET = new ushort[NB];

        // horizontal directions (same order as the main thread's DIRX_W / DIRZ_W: +z, -x, -z, +x)
        public static readonly int[] HDX = { 0, -1, 0, 1 }, HDZ = { 1, 0, -1, 0 };
        // mob of a spawner (spawner meta)
        public static readonly Dictionary<string, int> SPAWNER_MOB = new Dictionary<string, int> { ["pig"] = 0, ["zombie"] = 1, ["skeleton"] = 2, ["spider"] = 3, ["cave_spider"] = 4 };
        public static readonly string[] SPAWNER_TYPES = { "pig", "zombie", "skeleton", "spider", "cave_spider" };

        // concrete powder -> its concrete; log / wood / stem -> its stripped kind (an axe strips it)
        public static readonly ushort[] CONCRETE_OF = new ushort[NBX], STRIPPED_OF = new ushort[NBX];

        // ---- redstone wire connections (shared by the mesher and the redstone engine)
        // horizontal directions 0 south (+z), 1 west (-x), 2 north (-z), 3 east (+x); 6-way facings add 4 up and 5 down
        public static readonly int[] RS_DX = { 0, -1, 0, 1, 0, 0 }, RS_DY = { 0, 0, 0, 0, 1, -1 }, RS_DZ = { 1, 0, -1, 0, 0, 0 }, RS_OPP = { 2, 3, 0, 1, 5, 4 };

        static bool _inited;

        /// <summary>Key lookup: the B[key] of the original (0 / AIR for unknown keys as `B[k] | 0`).</summary>
        public static int Id(string key) => key != null && BK.TryGetValue(key, out var v) ? v : 0;
        public static bool Has(string key) => key != null && BK.ContainsKey(key);

        static bool M(string s, string re) => Regex.IsMatch(s, re);

        public static void Init()
        {
            if (_inited) return;
            _inited = true;
            var T = BlockData.BLOCK_TABLE;
            B_KEY[0] = "AIR"; B_EN[0] = "Air"; B_RU[0] = "Воздух";
            for (int i = 0; i < T.Length; i++)
            {
                var r = T[i]; int id = i + 1;
                BK[r.Key] = id; B_KEY[id] = r.Key; B_EN[id] = r.En; B_RU[id] = r.Ru;
            }

            LCOST[0] = 1;
            for (int id = 1; id < NB; id++)
            {
                var r = T[id - 1];
                string key = r.Key, flagsS = r.Flags;
                if (!SHAPE_BY_NAME.TryGetValue(r.Shape, out int sh)) sh = SH.CUBE;
                SHAPE[id] = (byte)sh;
                int f = 0;
                foreach (char c in flagsS)
                {
                    switch (c)
                    {
                        case 'L': f |= BF_LEAVES; break;
                        case 'Y': f |= BF_AXIS; break;
                        case 'G': f |= BF_GLASS; break;
                        case 'T': f |= BF_TRANS; break;
                        case 'F': f |= BF_FALL; break;
                        case 'e': f |= BF_EMISSIVE; break;
                        case 'A': f |= BF_AQUATIC; break;
                        case 'W': f |= BF_NEEDWATER; break;
                        case 'h': f |= BF_HANG; break;
                        case 'c': f |= BF_CLIMB; break;
                        case 'S': f |= BF_STACK; break;
                        case 'O': f |= BF_FACING; break;
                    }
                }
                if (sh == SH.CROSS || sh == SH.TALL || sh == SH.VINE || sh == SH.LICHEN || sh == SH.LILY) f |= BF_PLANT;
                if (sh == SH.CROSS || sh == SH.TALL || sh == SH.WATER || sh == SH.LAVA || sh == SH.VINE ||
                    sh == SH.LICHEN || sh == SH.FIRE || key == "SNOW_LAYER") f |= BF_REPLACE;
                if (M(key, "SAPLING|EYEBLOSSOM|WHEAT_|CARROTS_|POTATOES_|BEETROOTS_|STEM_|MUSHROOM$|FUNGUS|ROOTS|SPROUTS|CORAL_FAN|KELP|SEAGRASS|PROPAGULE|SUGAR_CANE|SWEET_BERRY|COBWEB|FLOWER_POT|DANDELION|POPPY|TULIP|ORCHID|ALLIUM|BLUET|CORNFLOWER|DAISY|LILY_OF|WITHER_ROSE")) f &= ~BF_REPLACE;
                if (key == "TALL_GRASS" || key == "FERN" || key == "DEAD_BUSH" || key == "SEAGRASS") f |= BF_REPLACE;
                FLAGS[id] = (ushort)f;
                EMIT[id] = (byte)r.Light;
                TAB[id] = (byte)Math.Max(0, Array.IndexOf(TABS, r.Tab));
                int bas = r.Base.Length > 0 ? Id(r.Base) : 0;
                BASE[id] = (ushort)bas;
                bool isCube = sh == SH.CUBE;
                bool clear = (f & (BF_LEAVES | BF_GLASS | BF_TRANS)) != 0;
                OPAQUE[id] = (byte)(isCube && !clear ? 1 : 0);
                RLAYER[id] = (byte)(((f & BF_TRANS) != 0 || sh == SH.WATER) ? RL_TRANS :
                    (isCube && !clear && sh != SH.CACTUS) ? RL_SOLID : RL_CUTOUT);
                if (sh == SH.LAVA) RLAYER[id] = RL_SOLID;
                // light: cost per step (15 = blocks light); VDIM = sky light is dimmed while passing down (MC opacity 1)
                LCOST[id] = (byte)(OPAQUE[id] != 0 ? 15 : 1);
                VDIM[id] = (byte)((f & BF_LEAVES) != 0 || sh == SH.WATER || (f & BF_AQUATIC) != 0 || key == "ICE" || key == "COBWEB" ? 1 : 0);
                if (key == "TINTED_GLASS" || sh == SH.LAVA) LCOST[id] = 15;
                if (sh == SH.SLAB || sh == SH.STAIRS || sh == SH.FARMLAND || sh == SH.CHEST) LCOST[id] = 1;
                SOLID[id] = (byte)((sh == SH.CROSS || sh == SH.TALL || sh == SH.WATER || sh == SH.LAVA || sh == SH.TORCH ||
                    sh == SH.VINE || sh == SH.LICHEN || sh == SH.RAIL || sh == SH.FIRE || sh == SH.BUTTON ||
                    sh == SH.PLATE || sh == SH.LADDER || sh == SH.WIRE) ? 0 : 1);
                if (key == "COBWEB" || key == "LEVER") SOLID[id] = 0;
                COLLIDE[id] = (byte)(SOLID[id] != 0 || sh == SH.LADDER ? 1 : 0);
                // hardness / tool heuristics (survival)
                double h = 1.5; int tool = 1;
                if (M(key, "ORE$|_ORE|^(COAL|IRON|GOLD|DIAMOND)$")) { h = 3; }   // the four first ores are keyed without _ORE
                else if (M(key, "OBSIDIAN")) h = 50;
                else if (key == "BEDROCK") h = -1;
                else if (M(key, "DIRT|GRASS$|^GRASS|PODZOL|MYCELIUM|FARMLAND|^MUD$|CLAY|SOUL|NYLIUM")) { h = 0.6; tool = 3; }
                else if (M(key, "SAND$|GRAVEL|POWDER|SNOW")) { h = 0.5; tool = 3; }
                else if (M(key, "LOG|WOOD|PLANKS|STEM$|HYPHAE|BOOKSHELF|CRAFTING|CHEST|FENCE|GATE|DOOR|TRAPDOOR|LADDER|BAMBOO_BLOCK|OAK|SPRUCE|BIRCH|JUNGLE|ACACIA|CHERRY|MANGROVE|CRIMSON|WARPED") && !M(key, "STONE|BRICK")) { h = 2; tool = 2; }
                else if ((f & BF_LEAVES) != 0) { h = 0.2; tool = 4; }
                else if (M(key, "GLASS|PANE|GLOW|SEA_LANTERN|ICE$")) { h = 0.3; tool = 0; }
                else if (M(key, "WOOL|CARPET")) { h = 0.8; tool = 4; }
                else if (M(key, "PUMPKIN|MELON|JACK_O")) { h = 1; tool = 2; }
                else if (M(key, "DEEPSLATE")) { h = 3; }
                else if (key == "NETHERRACK") { h = 0.4; }
                else if (M(key, "BED")) { h = 0.2; tool = 0; }
                if ((f & BF_PLANT) != 0 || sh == SH.CROSS || sh == SH.TORCH || sh == SH.FIRE || sh == SH.TALL) { h = 0; tool = 0; }
                if (key == "COBWEB") { h = 4; tool = 4; }
                if (key == "SPAWNER") { h = 5; tool = 1; }
                if (M(key, "^(BARREL|LECTERN|LOOM|COMPOSTER|FLETCHING_TABLE|CARTOGRAPHY_TABLE|SMITHING_TABLE|CAMPFIRE)$")) { h = 2.5; tool = 2; }
                if (M(key, "^(BLAST_FURNACE|SMOKER|STONECUTTER)$")) { h = 3.5; tool = 1; }
                if (M(key, "^(GRINDSTONE|CAULDRON|BELL)$")) { h = 2; tool = 1; }
                if (key == "LANTERN") { h = 3.5; tool = 1; }
                if (key == "BREWING_STAND") { h = 0.5; tool = 1; }
                if (M(key, "^BEETROOTS_")) { h = 0; tool = 0; }
                if (M(key, "^(SCULK|SCULK_VEIN|PALE_MOSS_BLOCK|PALE_MOSS_CARPET|AZALEA|FLOWERING_AZALEA|BIG_DRIPLEAF|BIG_DRIPLEAF_STEM|SMALL_DRIPLEAF|SPORE_BLOSSOM)$")) { h = key == "SCULK" ? 0.2 : 0.1; tool = 0; }
                if (key == "SCULK_SENSOR" || key == "SCULK_CATALYST" || key == "SCULK_SHRIEKER") { h = 1.5; tool = 0; }
                if (key == "POINTED_DRIPSTONE") { h = 1.5; tool = 1; }
                if (key == "CREAKING_HEART") { h = 10; tool = 2; }
                if (key == "COCOA") { h = 0.2; tool = 2; }
                if (M(key, "^(REDSTONE_WIRE|REDSTONE_TORCH|REDSTONE_TORCH_OFF|LEVER|REPEATER|COMPARATOR)$")) { h = 0; tool = 0; }
                if (M(key, "^REDSTONE_LAMP")) { h = 0.3; tool = 0; }
                if (M(key, "^(PISTON|STICKY_PISTON|PISTON_HEAD)$")) { h = 1.5; tool = 0; }
                if (key == "OBSERVER") { h = 3; tool = 1; }
                if (key == "SHIP_WHEEL") { h = 2; tool = 2; }
                if (sh == SH.WATER || sh == SH.LAVA) h = -1;
                HARD[id] = (float)h; TOOL[id] = (byte)tool;
            }

            // Minecraft's explosion resistance (an explosion's ray loses (resistance + 0.3) x 0.3 in each block
            // it crosses): the hardness for most blocks, stone-like building blocks and metal 6, obsidian 1200,
            // fluids 100, unbreakable blocks out of reach
            {
                var Vb = new Dictionary<string, double>
                {
                    ["FURNACE"] = 3.5, ["FURNACE_LIT"] = 3.5, ["BLAST_FURNACE"] = 3.5, ["SMOKER"] = 3.5, ["STONECUTTER"] = 3.5, ["LANTERN"] = 3.5, ["BOOKSHELF"] = 1.5,
                    ["ICE"] = 0.5, ["PACKED_ICE"] = 0.5, ["BLUE_ICE"] = 2.8, ["SNOW"] = 0.2, ["SNOW_LAYER"] = 0.1, ["DIRT"] = 0.5, ["COARSE_DIRT"] = 0.5, ["ROOTED_DIRT"] = 0.5, ["PODZOL"] = 0.5,
                    ["MUD"] = 0.5, ["DIRT_PATH"] = 0.65, ["GRAVEL"] = 0.6, ["LADDER"] = 0.4, ["RAIL"] = 0.7, ["TNT"] = 0, ["MAGMA_BLOCK"] = 0.5, ["SOUL_SAND"] = 0.5, ["SOUL_SOIL"] = 0.5,
                    ["CRIMSON_NYLIUM"] = 0.4, ["WARPED_NYLIUM"] = 0.4, ["SHROOMLIGHT"] = 1, ["NETHER_WART_BLOCK"] = 1, ["WARPED_WART_BLOCK"] = 1, ["SPONGE"] = 0.6, ["WET_SPONGE"] = 0.6,
                    ["HAY_BLOCK"] = 0.5, ["BONE_BLOCK"] = 2, ["DRIED_KELP_BLOCK"] = 2.5, ["HONEYCOMB_BLOCK"] = 0.6, ["BROWN_MUSHROOM_BLOCK"] = 0.2, ["RED_MUSHROOM_BLOCK"] = 0.2,
                    ["MUSHROOM_STEM"] = 0.2, ["MOSS_BLOCK"] = 0.1, ["PALE_MOSS_BLOCK"] = 0.1, ["CALCITE"] = 0.75, ["DRIPSTONE"] = 1, ["CACTUS"] = 0.4, ["COMPOSTER"] = 0.6, ["CAMPFIRE"] = 2,
                    ["CHEST"] = 2.5, ["CRAFTING_TABLE"] = 2.5, ["CREAKING_HEART"] = 10, ["AMETHYST_BLOCK"] = 1.5, ["BUDDING_AMETHYST"] = 1.5, ["GLOW"] = 0.3, ["SEA_LANTERN"] = 0.3,
                    ["COCOA"] = 3, ["VINE"] = 0.2, ["GLOW_LICHEN"] = 0.2,
                };
                for (int id = 1; id < NB; id++)
                {
                    string k = B_KEY[id]; int sh = SHAPE[id];
                    double r = HARD[id];
                    if (sh == SH.WATER || sh == SH.LAVA) r = 100;
                    else if (r < 0) r = 3600000;
                    else if (Vb.TryGetValue(k, out var vv)) r = vv;
                    else if (M(k, "OBSIDIAN|^NETHERITE_BLOCK$|^ANCIENT_DEBRIS$")) r = 1200;
                    else if (M(k, "PRESSURE_PLATE|BUTTON|^LEVER$")) r = 0.5;
                    else if (M(k, "^IRON_(DOOR|TRAPDOOR)$|^BELL$|^SPAWNER$")) r = 5;
                    else if (M(k, "^END_STONE")) r = 9;
                    else if (M(k, "_ORE$|^(COAL|IRON|GOLD|DIAMOND)$|^LAPIS_BLOCK$|^POINTED_DRIPSTONE$|^OBSERVER$|^SCULK_(CATALYST|SHRIEKER)$|^MUD_BRICKS")) r = 3;
                    else if (M(k, "_GLAZED$")) r = 1.4;
                    else if (M(k, "CONCRETE_POWDER")) r = 0.5;
                    else if (M(k, "CONCRETE$")) r = 1.8;
                    else if (M(k, "BASALT|TERRACOTTA|^TERRA_")) r = 4.2;
                    else if (M(k, "^SMOOTH_(RED_)?SANDSTONE|^SMOOTH_QUARTZ")) r = 6;
                    else if (M(k, "SANDSTONE|QUARTZ")) r = 0.8;
                    else if (M(k, "^(STONE|COBBLE|MOSSY|SMOOTH_STONE|ANDESITE|DIORITE|GRANITE|POLISHED_|BRICK|DEEPSLATE|COBBLED_DEEPSLATE|CHISELED_|CRACKED_|TUFF|BLACKSTONE|GILDED_|PRISMARINE|DARK_PRISMARINE|PURPUR|NETHER_BRICK|RED_NETHER|IRON_BARS|GRINDSTONE)|_WALL$|COPPER|^RAW_.*_BLOCK$|^(IRON|GOLD|DIAMOND|EMERALD|REDSTONE|COAL)_BLOCK$|CORAL_BLOCK$")) r = 6;
                    else if (M(k, "PLANKS|_SLAB$|_STAIRS$|FENCE|_GATE$|_DOOR$|_TRAPDOOR$")) r = 3;
                    else if (M(k, "CARPET$")) r = 0.1;
                    BLAST[id] = (float)r;
                }
            }

            // waterlogged twins
            {
                var ok = new HashSet<int> { SH.STAIRS, SH.SLAB, SH.FENCE, SH.PANE, SH.WALL, SH.GATE, SH.TRAPDOOR, SH.LADDER, SH.CHEST, SH.RAIL };
                for (int id = 1; id < NB; id++)
                {
                    if (!ok.Contains(SHAPE[id])) continue;
                    int t = WET_BASE + id;
                    WET[id] = (ushort)t;
                    SHAPE[t] = SHAPE[id]; OPAQUE[t] = OPAQUE[id]; RLAYER[t] = RLAYER[id]; LCOST[t] = LCOST[id]; EMIT[t] = EMIT[id];
                    SOLID[t] = SOLID[id]; COLLIDE[t] = COLLIDE[id]; BASE[t] = BASE[id]; TAB[t] = TAB[id]; HARD[t] = HARD[id]; TOOL[t] = TOOL[id];
                    FLAGS[t] = (ushort)(FLAGS[id] | BF_WET); VDIM[t] = 1;
                    B_KEY[t] = B_KEY[id]; B_EN[t] = B_EN[id]; B_RU[t] = B_RU[id];
                }
            }

            // concrete powder -> its concrete; log / wood / stem -> its stripped kind
            {
                var powder = new Regex("^(.+)_CONCRETE_POWDER$");
                var logRe = new Regex("^(?!STRIPPED_).*_(LOG|WOOD|STEM|HYPHAE)$");
                for (int id = 1; id < B_KEY.Length; id++)
                {
                    string k = B_KEY[id] ?? "";
                    var c = powder.Match(k);
                    if (c.Success && Id(c.Groups[1].Value + "_CONCRETE") != 0) CONCRETE_OF[id] = (ushort)Id(c.Groups[1].Value + "_CONCRETE");
                    string s = k == "LOG" ? "STRIPPED_OAK_LOG" : k == "DARK_LOG" ? "STRIPPED_DARK_OAK_LOG" : logRe.IsMatch(k) ? "STRIPPED_" + k : "";
                    if (s.Length > 0 && Id(s) != 0 && k != "MUSHROOM_STEM" && k != "BIG_DRIPLEAF_STEM") STRIPPED_OF[id] = (ushort)Id(s);
                }
            }

            Models.Build();
            Items.Init();
            Loot.Init();
        }

        // the plain block of a waterlogged twin
        public static int DryId(int id) => id >= WET_BASE ? id - WET_BASE : id;

        // a component the wire turns toward when it lies in direction d of the wire
        public static bool RsConnectsTo(int id, int m, int d)
        {
            if (id == B.REPEATER) return ((m & 3) & 1) == (d & 1);
            if (id == B.OBSERVER) return (m & 7) == d;          // its output side faces the wire
            return id == B.REDSTONE_TORCH || id == B.REDSTONE_TORCH_OFF || id == B.LEVER || id == B.REDSTONE_BLOCK ||
                id == B.COMPARATOR || SHAPE[id] == SH.BUTTON || SHAPE[id] == SH.PLATE;
        }

        // how the wire at the origin links in direction d: 0 no, 1 flat (or down a step), 2 up the side of
        // the next block. get(dx, dy, dz) / getM(...) read the cells around it.
        public static int WireLink(CellGet get, CellGet getM, int d)
        {
            int dx = RS_DX[d], dz = RS_DZ[d], n = get(dx, 0, dz);
            if (n == B.REDSTONE_WIRE || RsConnectsTo(n, getM(dx, 0, dz), d)) return 1;
            if (OPAQUE[get(0, 1, 0)] == 0 && get(dx, 1, dz) == B.REDSTONE_WIRE) return 2;
            if (OPAQUE[n] == 0 && get(dx, -1, dz) == B.REDSTONE_WIRE) return 1;
            return 0;
        }

        public static bool IsJungleLog(int id) => id == B.JUNGLE_LOG || id == B.JUNGLE_WOOD || id == B.STRIPPED_JUNGLE_LOG || id == B.STRIPPED_JUNGLE_WOOD;
        public static bool IsWaterId(int id) => id == B.WATER || (FLAGS[id] & (BF_AQUATIC | BF_WET)) != 0;
        public static bool IsLavaId(int id) => id == B.LAVA;
        public static bool IsLiquidId(int id) => id == B.WATER || id == B.LAVA || (FLAGS[id] & (BF_AQUATIC | BF_WET)) != 0;

        // Blocks that flowing water / lava destroys (Minecraft: not solid and unable to hold a fluid):
        // replaceable plants, torches, flowers, saplings, crops, double plants, buttons, fire. Ladders,
        // doors, sugar cane, cobwebs and underwater plants stay.
        public static bool FluidBreaks(int id)
        {
            if (id == 0) return false;
            int fl = FLAGS[id];
            if ((fl & BF_AQUATIC) != 0) return false;
            if (SHAPE[id] == SH.LICHEN) return false;   // glow lichen holds water in Minecraft instead of washing away
            if ((fl & BF_REPLACE) != 0) return true;
            int sh = SHAPE[id];
            if (sh == SH.TORCH || sh == SH.FIRE || sh == SH.BUTTON || sh == SH.TALL) return true;
            return sh == SH.CROSS && id != B.SUGAR_CANE && id != B.COBWEB;
        }

        // Resolve texture names -> layers once the atlas layer map is known.
        public static void ResolveBlockTextures(IDictionary<string, int> layerMap)
        {
            int L(string n)
            {
                if (layerMap.TryGetValue(n, out var v)) return v;
                return layerMap.TryGetValue("white", out var w) ? w : 0;
            }
            for (int id = 1; id < NB; id++)
            {
                var r = BlockData.BLOCK_TABLE[id - 1];
                string src = r.Tex;
                int bas = BASE[id];
                if (string.IsNullOrEmpty(src) && bas != 0) src = BlockData.BLOCK_TABLE[bas - 1].Tex;
                string[] p = !string.IsNullOrEmpty(src) ? src.Split(',') : new[] { "white" };
                TEXNAMES[id] = p;
                int top, side, bottom, front;
                int sh = SHAPE[id];
                if (sh == SH.DOOR || sh == SH.TALL) { top = L(p[0]); bottom = L(p.Length > 1 ? p[1] : p[0]); side = top; }
                else if (sh == SH.BED) { top = L(p[0]); side = L(p.Length > 1 ? p[1] : p[0]); bottom = L("oak_planks"); }
                else if (p.Length == 1) { top = side = bottom = L(p[0]); }
                else if (p.Length == 2) { top = bottom = L(p[0]); side = L(p[1]); }
                else { top = L(p[0]); side = L(p[1]); bottom = L(p[2]); }
                front = p.Length >= 4 ? L(p[3]) : side;
                int o = id * 6;
                FTEX[o] = (ushort)side; FTEX[o + 1] = (ushort)side; FTEX[o + 2] = (ushort)top; FTEX[o + 3] = (ushort)bottom; FTEX[o + 4] = (ushort)side; FTEX[o + 5] = (ushort)side;
                FRONT[id] = (ushort)front;
                // base textures of derived shapes: slabs/stairs/walls use base faces, fences/gates use base side
                if (bas != 0 && string.IsNullOrEmpty(r.Tex))
                {
                    for (int k = 0; k < 6; k++) FTEX[o + k] = FTEX[bas * 6 + k];
                    if (sh == SH.FENCE || sh == SH.GATE) for (int k = 0; k < 6; k++) FTEX[o + k] = FTEX[bas * 6];
                }
            }
            // block model faces: texture name -> layer (models), models by block id
            foreach (var Mo in Models.MODEL_LIST)
                foreach (var E in Mo)
                    foreach (var F in E.Faces)
                        if (F != null)
                        {
                            string n = F.Tex; int sl = n.LastIndexOf('/');
                            F.Layer = L(sl >= 0 ? n.Substring(sl + 1) : n);
                        }
            foreach (var kv in Models.MODEL_VAR)
            {
                int id = Id(kv.Key);
                if (id != 0) { MODEL_VAR_ID[id] = kv.Value; MODEL_BOX_ID[id] = Models.MODEL_BOX[kv.Key]; }
            }
            for (int id = 1; id < NB; id++)
            {
                int t = WET[id];
                if (t == 0) continue;
                for (int k = 0; k < 6; k++) FTEX[t * 6 + k] = FTEX[id * 6 + k];
                FRONT[t] = FRONT[id]; TEXNAMES[t] = TEXNAMES[id];
            }
        }
    }
}
