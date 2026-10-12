// Port of the data part of worldgen.js: biomes, biome colours / temperatures / surface properties,
// tree and vegetation placements, land biome rules, terrain splines and structure set definitions.
using System;
using System.Collections.Generic;

namespace Maincraft
{
    /// <summary>Biome ids (BI in the original), in BIOME_LIST order.</summary>
    public static class BI
    {
        public const int OCEAN = 0, DEEP_OCEAN = 1, FROZEN_OCEAN = 2, DEEP_FROZEN_OCEAN = 3, COLD_OCEAN = 4, DEEP_COLD_OCEAN = 5,
            LUKEWARM_OCEAN = 6, DEEP_LUKEWARM_OCEAN = 7, WARM_OCEAN = 8, RIVER = 9, FROZEN_RIVER = 10, BEACH = 11, SNOWY_BEACH = 12,
            STONY_SHORE = 13, PLAINS = 14, SUNFLOWER_PLAINS = 15, SNOWY_PLAINS = 16, ICE_SPIKES = 17, DESERT = 18, SAVANNA = 19,
            SAVANNA_PLATEAU = 20, WINDSWEPT_SAVANNA = 21, FOREST = 22, FLOWER_FOREST = 23, BIRCH_FOREST = 24, OLD_GROWTH_BIRCH_FOREST = 25,
            DARK_FOREST = 26, TAIGA = 27, OLD_GROWTH_PINE_TAIGA = 28, OLD_GROWTH_SPRUCE_TAIGA = 29, SNOWY_TAIGA = 30, JUNGLE = 31,
            SPARSE_JUNGLE = 32, BAMBOO_JUNGLE = 33, SWAMP = 34, MANGROVE_SWAMP = 35, BADLANDS = 36, ERODED_BADLANDS = 37,
            WOODED_BADLANDS = 38, MEADOW = 39, CHERRY_GROVE = 40, GROVE = 41, SNOWY_SLOPES = 42, JAGGED_PEAKS = 43, FROZEN_PEAKS = 44,
            STONY_PEAKS = 45, WINDSWEPT_HILLS = 46, WINDSWEPT_GRAVELLY_HILLS = 47, WINDSWEPT_FOREST = 48, MUSHROOM_FIELDS = 49,
            PALE_GARDEN = 50, LUSH_CAVES = 51, DRIPSTONE_CAVES = 52, DEEP_DARK = 53;
        // back-compat alias used by feature code
        public const int OLD_GROWTH_TAIGA = OLD_GROWTH_SPRUCE_TAIGA;
    }

    public sealed class BProp
    {
        public int top, fill, sub, floor, snow, cold, @base;
        public bool ocean, mountain, badlands;
        public BProp Clone() => (BProp)MemberwiseClone();
    }

    public sealed class TreeSet
    {
        public int count, extra; public double chance;
        public (double p, string t)[] types;
    }

    public sealed class PatchDef { public int tries, xz, y; public string kind; }

    public sealed class VegPlace
    {
        public string patch;
        public int n = -1;        // count per chunk (-1 unset: 1)
        public int r;             // 1-in-r chance per instance (0 none)
        public int[] nt;          // noise threshold counts
        public int[] rn;          // uniform count range
    }

    public sealed class JigType { public string pool; public int[] biomes; }

    public sealed class StructSet
    {
        public int salt, spacing, separation, reach, depth;
        public bool clearTrees;
        public JigType[] types;
        public Func<WorldGen, int, int, List<Piece>> start;
    }

    public static class WG
    {
        public static readonly string[][] BIOME_LIST =
        {
            new[] { "OCEAN", "Ocean", "Океан" }, new[] { "DEEP_OCEAN", "Deep Ocean", "Глубокий океан" },
            new[] { "FROZEN_OCEAN", "Frozen Ocean", "Замёрзший океан" }, new[] { "DEEP_FROZEN_OCEAN", "Deep Frozen Ocean", "Глубокий замёрзший океан" },
            new[] { "COLD_OCEAN", "Cold Ocean", "Холодный океан" }, new[] { "DEEP_COLD_OCEAN", "Deep Cold Ocean", "Глубокий холодный океан" },
            new[] { "LUKEWARM_OCEAN", "Lukewarm Ocean", "Тёплый океан" }, new[] { "DEEP_LUKEWARM_OCEAN", "Deep Lukewarm Ocean", "Глубокий тёплый океан" },
            new[] { "WARM_OCEAN", "Warm Ocean", "Тропический океан" },
            new[] { "RIVER", "River", "Река" }, new[] { "FROZEN_RIVER", "Frozen River", "Замёрзшая река" },
            new[] { "BEACH", "Beach", "Пляж" }, new[] { "SNOWY_BEACH", "Snowy Beach", "Заснеженный пляж" },
            new[] { "STONY_SHORE", "Stony Shore", "Каменистый берег" }, new[] { "PLAINS", "Plains", "Равнина" },
            new[] { "SUNFLOWER_PLAINS", "Sunflower Plains", "Подсолнечная равнина" }, new[] { "SNOWY_PLAINS", "Snowy Plains", "Заснеженная равнина" },
            new[] { "ICE_SPIKES", "Ice Spikes", "Ледяные пики" }, new[] { "DESERT", "Desert", "Пустыня" }, new[] { "SAVANNA", "Savanna", "Саванна" },
            new[] { "SAVANNA_PLATEAU", "Savanna Plateau", "Плато саванны" }, new[] { "WINDSWEPT_SAVANNA", "Windswept Savanna", "Выветренная саванна" },
            new[] { "FOREST", "Forest", "Лес" }, new[] { "FLOWER_FOREST", "Flower Forest", "Цветочный лес" },
            new[] { "BIRCH_FOREST", "Birch Forest", "Березняк" }, new[] { "OLD_GROWTH_BIRCH_FOREST", "Old Growth Birch Forest", "Старовозрастный березняк" },
            new[] { "DARK_FOREST", "Dark Forest", "Тёмный лес" }, new[] { "TAIGA", "Taiga", "Тайга" },
            new[] { "OLD_GROWTH_PINE_TAIGA", "Old Growth Pine Taiga", "Старовозрастная сосновая тайга" },
            new[] { "OLD_GROWTH_SPRUCE_TAIGA", "Old Growth Spruce Taiga", "Старовозрастная еловая тайга" },
            new[] { "SNOWY_TAIGA", "Snowy Taiga", "Заснеженная тайга" }, new[] { "JUNGLE", "Jungle", "Джунгли" },
            new[] { "SPARSE_JUNGLE", "Sparse Jungle", "Редкие джунгли" }, new[] { "BAMBOO_JUNGLE", "Bamboo Jungle", "Бамбуковые джунгли" },
            new[] { "SWAMP", "Swamp", "Болото" }, new[] { "MANGROVE_SWAMP", "Mangrove Swamp", "Мангровое болото" },
            new[] { "BADLANDS", "Badlands", "Пустоши" }, new[] { "ERODED_BADLANDS", "Eroded Badlands", "Выветренные пустоши" },
            new[] { "WOODED_BADLANDS", "Wooded Badlands", "Лесистые пустоши" },
            new[] { "MEADOW", "Meadow", "Луг" }, new[] { "CHERRY_GROVE", "Cherry Grove", "Вишнёвая роща" }, new[] { "GROVE", "Grove", "Роща" },
            new[] { "SNOWY_SLOPES", "Snowy Slopes", "Заснеженные склоны" }, new[] { "JAGGED_PEAKS", "Jagged Peaks", "Зубчатые пики" },
            new[] { "FROZEN_PEAKS", "Frozen Peaks", "Заледеневшие пики" }, new[] { "STONY_PEAKS", "Stony Peaks", "Скалистые пики" },
            new[] { "WINDSWEPT_HILLS", "Windswept Hills", "Выветренные холмы" }, new[] { "WINDSWEPT_GRAVELLY_HILLS", "Windswept Gravelly Hills", "Выветренные гравийные холмы" },
            new[] { "WINDSWEPT_FOREST", "Windswept Forest", "Выветренный лес" }, new[] { "MUSHROOM_FIELDS", "Mushroom Fields", "Грибные поля" },
            new[] { "PALE_GARDEN", "Pale Garden", "Бледный сад" },
            // cave biomes: only underground (caveBiome), the surface map never holds them
            new[] { "LUSH_CAVES", "Lush Caves", "Пышные пещеры" }, new[] { "DRIPSTONE_CAVES", "Dripstone Caves", "Капельниковые пещеры" }, new[] { "DEEP_DARK", "Deep Dark", "Глубокая тьма" },
        };
        public static readonly Dictionary<string, int> BIK = new Dictionary<string, int>();
        public static int NBIO => BIOME_LIST.Length;

        // Minecraft (Java) biome temperatures. Above y 80 the temperature drops by 0.00125 per block (with
        // a little noise); below 0.15 water freezes and snow settles.
        public static readonly float[] BIOME_TEMP = new float[BIOME_LIST.Length];
        // BIOME_TINT[b * 3 + k]: k = 0 grass, 1 foliage, 2 water (0xRRGGBB).
        public static readonly uint[] BIOME_TINT = new uint[BIOME_LIST.Length * 3];
        // per-biome properties: surface top / filler / underwater floor and flags
        public static readonly BProp[] BPROP = new BProp[BIOME_LIST.Length];
        public static readonly int[] ORIG_BIOME = new int[BIOME_LIST.Length];   // biome of the original's set that a biome follows (variants -> base)
        public static readonly TreeSet[] MC_TREES = new TreeSet[BIOME_LIST.Length];
        // columns (Chebyshev distance between trunks) a tree needs free of another tree's trunk
        public static readonly Dictionary<string, int> TREE_ROOM = new Dictionary<string, int>
        { ["cactus"] = 2, ["ice_spike"] = 2, ["giant_red_mushroom"] = 3, ["giant_brown_mushroom"] = 3, ["dark_oak"] = 2, ["pale_oak"] = 2, ["mega_spruce"] = 2, ["mega_pine"] = 2, ["mega_jungle"] = 2 };
        // deepest water (blocks above the ground) a tree may stand in
        public static readonly Dictionary<string, int> TREE_WATER = new Dictionary<string, int> { ["mangrove"] = 5, ["tall_mangrove"] = 5, ["swamp_oak"] = 2 };
        public static readonly int[] FACE6 = { 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1 }, FACE4 = { 1, 0, -1, 0, 0, 1, 0, -1 };

        public static readonly Dictionary<string, PatchDef> PATCHES = new Dictionary<string, PatchDef>();
        public static readonly VegPlace[][] MC_VEG = new VegPlace[BIOME_LIST.Length][];

        // ---------------------------------------------------------------- land biome rules
        // Table cell: a biome key, or [key if w > split, split, key otherwise] (a nested cell may follow).
        public sealed class Cell
        {
            public string key; public Cell hi, lo; public double split;
            public static implicit operator Cell(string k) => new Cell { key = k };
        }
        static Cell C(Cell hi, double split, Cell lo) => new Cell { hi = hi, lo = lo, split = split };
        public static readonly Cell[][] LAND_TABLE =
        {
            // humidity: dry ............................................................................ wet
            new[] { C("ICE_SPIKES", 0.3, "SNOWY_PLAINS"), "SNOWY_PLAINS", C("SNOWY_TAIGA", -0.2, "SNOWY_PLAINS"), "SNOWY_TAIGA", "SNOWY_TAIGA" },   // frozen
            new[] { "PLAINS", "PLAINS", C("TAIGA", 0, "FOREST"), "TAIGA", C("OLD_GROWTH_PINE_TAIGA", 0.3, C("TAIGA", -0.3, "OLD_GROWTH_SPRUCE_TAIGA")) },   // cold
            new[] { C("FLOWER_FOREST", 0.4, "PLAINS"), C("SUNFLOWER_PLAINS", 0.5, "PLAINS"), "FOREST", C("OLD_GROWTH_BIRCH_FOREST", 0.4, "BIRCH_FOREST"), C("PALE_GARDEN", 0.3, "DARK_FOREST") },   // temperate
            new[] { "SAVANNA", "SAVANNA", C("FOREST", 0, "PLAINS"), "SPARSE_JUNGLE", C("BAMBOO_JUNGLE", 0.3, "JUNGLE") },   // warm
            new[] { "DESERT", "DESERT", "DESERT", "DESERT", C("JUNGLE", 0, "SAVANNA") },   // hot
        };

        public static int LandCell(Cell cell, double w)
        {
            while (cell.key == null) cell = w > cell.split ? cell.hi : cell.lo;
            return BIK[cell.key];
        }

        public static int LandBiome(Clim o)
        {
            int ti = o.ti; double hs = o.h - Blocks.SEA, w = o.w, e = o.e, hu = o.hu;
            int hi = hu < -0.4 ? 0 : hu < -0.1 ? 1 : hu < 0.15 ? 2 : hu < 0.4 ? 3 : 4;
            if (hs >= 80) return ti >= 3 ? BI.STONY_PEAKS : w < 0 ? BI.JAGGED_PEAKS : BI.FROZEN_PEAKS;
            if (hs >= 40 && e < -0.1)
            {
                if (ti <= 1) return hi <= 1 ? BI.SNOWY_SLOPES : BI.GROVE;
                if (ti == 2) return hi <= 1 ? (w > 0.3 ? BI.CHERRY_GROVE : BI.MEADOW) : hi == 2 ? BI.MEADOW : BI.GROVE;
                if (ti == 3) return hi <= 2 ? BI.SAVANNA_PLATEAU : BI.JUNGLE;
                return e < -0.4 ? BI.ERODED_BADLANDS : BI.BADLANDS;
            }
            if (hs >= 30 && e > 0.1)
            {
                if (ti <= 2) return hi == 0 ? BI.WINDSWEPT_GRAVELLY_HILLS : hi >= 3 ? BI.WINDSWEPT_FOREST : BI.WINDSWEPT_HILLS;
                if (hi <= 2) return BI.WINDSWEPT_SAVANNA;
            }
            if (o.k6 > 0.5 && hs < 6 && ti > 0 && hi >= 2) return ti <= 2 ? BI.SWAMP : BI.MANGROVE_SWAMP;
            // the hot dry belt rises into banded badlands
            if (ti == 4 && hi >= 2 && hi <= 3 && hs >= 15) return hi == 3 ? BI.WOODED_BADLANDS : e > 0.1 ? BI.ERODED_BADLANDS : BI.BADLANDS;
            return LandCell(LAND_TABLE[ti][hi], w);
        }

        public static int[] CORAL_BLOCKS, CORAL_FANS, TERRA_BANDS;

        public static double Smooth01(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);
        public static double Sstep(double a, double b, double x) => Smooth01((x - a) / (b - a));
        public static double Spline(double[] pts, double x)
        {
            if (x <= pts[0]) return pts[1];
            for (int i = 2; i < pts.Length; i += 2)
            {
                if (x <= pts[i]) { double t = (x - pts[i - 2]) / (pts[i] - pts[i - 2]); return pts[i - 1] + (pts[i + 1] - pts[i - 1]) * t; }
            }
            return pts[pts.Length - 1];
        }
        // terrain splines (continentalness -> base height, erosion -> mountain amplitude / density slope)
        public static readonly double[] SPL_BASE = { -1, 5, -0.6, 12, -0.42, 20, -0.3, 30, -0.2, 40, -0.1, 48, 0, 55, 0.12, 61, 0.24, 64, 0.4, 71, 0.6, 84, 1, 104 };
        public static readonly double[] SPL_MOUNT = { -1, 1, -0.58, 0.88, -0.35, 0.62, -0.18, 0.36, 0.05, 0.16, 0.4, 0.06, 1, 0 };
        public static readonly double[] SPL_FACTOR = { -1, 1.3, -0.5, 2.2, -0.2, 3.6, 0.05, 5, 0.45, 6.5, 1, 7.5 };
        public static readonly double[] SPL_CONT_PARAM = { -1, -1, -0.325, -0.8, -0.11, -0.19, -0.077, -0.13, -0.04, -0.06, 0.05, 0.05, 0.37, 0.25, 0.8, 0.7, 1.2, 1 };
        public static double Clamp1(double v) => v < -1 ? -1 : v > 1 ? 1 : v;
        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public static int CI(int x, int y, int z) => ((y - Blocks.WORLD_MIN_Y) << 8) | (z << 4) | x;
        public const int COL_N = Blocks.WORLD_H * 256;

        public static HashSet<int> IGLOO_BIOMES, RUIN_COLD, RUIN_WARM;
        public static readonly string[] SHIPWRECK_BEACHED = { "with_mast", "sideways_full", "sideways_fronthalf", "sideways_backhalf", "rightsideup_full", "rightsideup_fronthalf",
            "rightsideup_backhalf", "with_mast_degraded", "rightsideup_full_degraded", "rightsideup_fronthalf_degraded", "rightsideup_backhalf_degraded" };
        public static readonly string[] SHIPWRECK_OCEAN = { "with_mast", "upsidedown_full", "upsidedown_fronthalf", "upsidedown_backhalf", "sideways_full", "sideways_fronthalf",
            "sideways_backhalf", "rightsideup_full", "rightsideup_fronthalf", "rightsideup_backhalf", "with_mast_degraded", "upsidedown_full_degraded",
            "upsidedown_fronthalf_degraded", "upsidedown_backhalf_degraded", "sideways_full_degraded", "sideways_fronthalf_degraded",
            "sideways_backhalf_degraded", "rightsideup_full_degraded", "rightsideup_fronthalf_degraded", "rightsideup_backhalf_degraded" };
        public static Dictionary<string, int> SHIP_LOOT;

        // structure sets (worldgen/structure_set): salt, spacing, separation; reach = chunks a start's pieces can extend from its chunk
        public static List<StructSet> STRUCTURE_SETS;
        public static StructSet STRUCTURE_SETS_VIL, STRUCTURE_SETS_OUTPOST;
        public static StructSet[] JIGSAW_SETS;
        public static StructSet[] OWN_TREELESS_SETS;

        // Beardifier kernel (24^3): [z][x][y], value at offset (x, y, z) from a piece's ground / a junction
        public static readonly float[] BEARD_KERNEL = new float[13824];

        public static int[] Box6(int[] b) => new[] { b[0], b[1], b[2], b[3] + 1, b[4] + 1, b[5] + 1 };

        // block meta turned with a template rotation (0..3 clockwise quarter turns)
        public static int RotMeta(int id, int m, int r)
        {
            if (r == 0) return m;
            int sh = Blocks.SHAPE[id];
            if (sh == SH.STAIRS || sh == SH.DOOR || sh == SH.TRAPDOOR || sh == SH.LADDER || sh == SH.CHEST || sh == SH.BED || sh == SH.GATE ||
                sh == SH.BUTTON || (Blocks.FLAGS[id] & Blocks.BF_FACING) != 0) return (m & ~3) | (((m & 3) + r) & 3);
            if (sh == SH.TORCH) return m != 0 ? 1 + ((m - 1 + r) & 3) : 0;
            if (sh == SH.RAIL) return (r & 1) != 0 ? m ^ 1 : m;
            if ((Blocks.FLAGS[id] & Blocks.BF_AXIS) != 0 && (r & 1) != 0 && (m & 3) != 0) return (m & ~3) | (3 - (m & 3));
            return m;
        }

        static bool _inited;

        public static void Init()
        {
            if (_inited) return;
            _inited = true;
            Blocks.Init();
            for (int i = 0; i < BIOME_LIST.Length; i++) BIK[BIOME_LIST[i][0]] = i;
            BIK["OLD_GROWTH_TAIGA"] = BI.OLD_GROWTH_SPRUCE_TAIGA;

            {
                var T = new Dictionary<string, double>
                {
                    ["PLAINS"] = 0.8, ["SUNFLOWER_PLAINS"] = 0.8, ["BEACH"] = 0.8, ["SWAMP"] = 0.8, ["MANGROVE_SWAMP"] = 0.8, ["FOREST"] = 0.7, ["FLOWER_FOREST"] = 0.7,
                    ["DARK_FOREST"] = 0.7, ["PALE_GARDEN"] = 0.7, ["LUSH_CAVES"] = 0.5, ["DRIPSTONE_CAVES"] = 0.8, ["DEEP_DARK"] = 0.8, ["BIRCH_FOREST"] = 0.6, ["OLD_GROWTH_BIRCH_FOREST"] = 0.6, ["TAIGA"] = 0.25, ["OLD_GROWTH_PINE_TAIGA"] = 0.3,
                    ["OLD_GROWTH_SPRUCE_TAIGA"] = 0.25, ["MEADOW"] = 0.5, ["CHERRY_GROVE"] = 0.5, ["WINDSWEPT_HILLS"] = 0.2, ["WINDSWEPT_GRAVELLY_HILLS"] = 0.2,
                    ["WINDSWEPT_FOREST"] = 0.2, ["STONY_SHORE"] = 0.2, ["STONY_PEAKS"] = 1.0, ["JUNGLE"] = 0.95, ["SPARSE_JUNGLE"] = 0.95, ["BAMBOO_JUNGLE"] = 0.95,
                    ["DESERT"] = 2, ["SAVANNA"] = 2, ["SAVANNA_PLATEAU"] = 2, ["WINDSWEPT_SAVANNA"] = 2, ["BADLANDS"] = 2, ["ERODED_BADLANDS"] = 2, ["WOODED_BADLANDS"] = 2,
                    ["MUSHROOM_FIELDS"] = 0.9, ["RIVER"] = 0.5, ["OCEAN"] = 0.5, ["DEEP_OCEAN"] = 0.5, ["LUKEWARM_OCEAN"] = 0.5, ["DEEP_LUKEWARM_OCEAN"] = 0.5,
                    ["WARM_OCEAN"] = 0.5, ["COLD_OCEAN"] = 0.5, ["DEEP_COLD_OCEAN"] = 0.5, ["DEEP_FROZEN_OCEAN"] = 0.5,
                };
                for (int i = 0; i < BIOME_LIST.Length; i++) BIOME_TEMP[i] = (float)(T.TryGetValue(BIOME_LIST[i][0], out var t) ? t : -0.5);   // the rest are the snowy biomes
            }
            {
                var Cc = new Dictionary<string, uint[]>
                {
                    ["PLAINS"] = new uint[] { 0x91BD59, 0x77AB2F }, ["SUNFLOWER_PLAINS"] = new uint[] { 0x91BD59, 0x77AB2F }, ["BEACH"] = new uint[] { 0x91BD59, 0x77AB2F },
                    ["DESERT"] = new uint[] { 0xBFB755, 0xAEA42A }, ["SAVANNA"] = new uint[] { 0xBFB755, 0xAEA42A }, ["SAVANNA_PLATEAU"] = new uint[] { 0xBFB755, 0xAEA42A },
                    ["WINDSWEPT_SAVANNA"] = new uint[] { 0xBFB755, 0xAEA42A },
                    ["BADLANDS"] = new uint[] { 0x90814D, 0x9E814D }, ["ERODED_BADLANDS"] = new uint[] { 0x90814D, 0x9E814D }, ["WOODED_BADLANDS"] = new uint[] { 0x90814D, 0x9E814D },
                    ["FOREST"] = new uint[] { 0x79C05A, 0x59AE30 }, ["FLOWER_FOREST"] = new uint[] { 0x79C05A, 0x59AE30 }, ["DARK_FOREST"] = new uint[] { 0x507A32, 0x59AE30 },
                    ["BIRCH_FOREST"] = new uint[] { 0x88BB67, 0x6BA941 }, ["OLD_GROWTH_BIRCH_FOREST"] = new uint[] { 0x88BB67, 0x6BA941 },
                    ["TAIGA"] = new uint[] { 0x86B783, 0x68A464 }, ["OLD_GROWTH_PINE_TAIGA"] = new uint[] { 0x86B87F, 0x68A55F }, ["OLD_GROWTH_SPRUCE_TAIGA"] = new uint[] { 0x86B87F, 0x68A55F },
                    ["SNOWY_TAIGA"] = new uint[] { 0x80B497, 0x60A17B }, ["SNOWY_PLAINS"] = new uint[] { 0x80B497, 0x60A17B }, ["ICE_SPIKES"] = new uint[] { 0x80B497, 0x60A17B },
                    ["SNOWY_BEACH"] = new uint[] { 0x83B593, 0x64A278 }, ["FROZEN_RIVER"] = new uint[] { 0x80B497, 0x60A17B }, ["FROZEN_OCEAN"] = new uint[] { 0x80B497, 0x60A17B },
                    ["DEEP_FROZEN_OCEAN"] = new uint[] { 0x8EB971, 0x71A74D }, ["GROVE"] = new uint[] { 0x80B497, 0x60A17B }, ["SNOWY_SLOPES"] = new uint[] { 0x80B497, 0x60A17B },
                    ["JAGGED_PEAKS"] = new uint[] { 0x80B497, 0x60A17B }, ["FROZEN_PEAKS"] = new uint[] { 0x80B497, 0x60A17B }, ["STONY_PEAKS"] = new uint[] { 0x9ABE4B, 0x82AC1E },
                    ["WINDSWEPT_HILLS"] = new uint[] { 0x8AB689, 0x6DA36B }, ["WINDSWEPT_GRAVELLY_HILLS"] = new uint[] { 0x8AB689, 0x6DA36B },
                    ["WINDSWEPT_FOREST"] = new uint[] { 0x8AB689, 0x6DA36B }, ["STONY_SHORE"] = new uint[] { 0x8AB689, 0x6DA36B },
                    ["MEADOW"] = new uint[] { 0x83BB6D, 0x63A948 }, ["CHERRY_GROVE"] = new uint[] { 0xB6DB61, 0xB6DB61 },
                    ["JUNGLE"] = new uint[] { 0x59C93C, 0x30BB0B }, ["BAMBOO_JUNGLE"] = new uint[] { 0x59C93C, 0x30BB0B }, ["SPARSE_JUNGLE"] = new uint[] { 0x64C73F, 0x3EB80F },
                    ["SWAMP"] = new uint[] { 0x6A7039, 0x6A7039 }, ["MANGROVE_SWAMP"] = new uint[] { 0x6A7039, 0x8DB127 }, ["MUSHROOM_FIELDS"] = new uint[] { 0x55C93F, 0x2BBB0F },
                    ["PALE_GARDEN"] = new uint[] { 0x8A9682, 0x939C88 },   // own: a washed-out grey green
                };
                var W = new Dictionary<string, uint>
                {
                    ["SWAMP"] = 0x617B64, ["MANGROVE_SWAMP"] = 0x3A7A6A, ["WARM_OCEAN"] = 0x43D5EE, ["LUKEWARM_OCEAN"] = 0x45ADF2,
                    ["DEEP_LUKEWARM_OCEAN"] = 0x45ADF2, ["COLD_OCEAN"] = 0x3D57D6, ["DEEP_COLD_OCEAN"] = 0x3D57D6, ["FROZEN_OCEAN"] = 0x3938C9,
                    ["DEEP_FROZEN_OCEAN"] = 0x3938C9, ["FROZEN_RIVER"] = 0x3938C9, ["SNOWY_BEACH"] = 0x3D57D6, ["SNOWY_TAIGA"] = 0x3D57D6,
                    ["MEADOW"] = 0x0E4ECF, ["CHERRY_GROVE"] = 0x5DB7EF, ["PALE_GARDEN"] = 0x6E8292,
                };
                // everything else (oceans, rivers, caves): temperate defaults
                for (int i = 0; i < BIOME_LIST.Length; i++)
                {
                    var c = Cc.TryGetValue(BIOME_LIST[i][0], out var cc) ? cc : new uint[] { 0x8EB971, 0x71A74D };
                    BIOME_TINT[i * 3] = c[0]; BIOME_TINT[i * 3 + 1] = c[1]; BIOME_TINT[i * 3 + 2] = W.TryGetValue(BIOME_LIST[i][0], out var w) ? w : 0x3F76E4;
                }
            }
            {
                int G = B.GRASS, D = B.DIRT, S = B.SAND;
                void def(string k, Action<BProp> o) { var p = new BProp { top = G, fill = D, sub = 0, floor = D, snow = 0, cold = 0, @base = BIK[k] }; o?.Invoke(p); BPROP[BIK[k]] = p; }
                void like(string k, string src, Action<BProp> o = null) { var p = BPROP[BIK[src]].Clone(); p.@base = BIK[src]; o?.Invoke(p); BPROP[BIK[k]] = p; }
                def("OCEAN", p => { p.top = S; p.fill = S; p.floor = -1; p.ocean = true; });
                def("FROZEN_OCEAN", p => { p.top = B.GRAVEL; p.fill = B.GRAVEL; p.floor = B.GRAVEL; p.cold = 2; p.ocean = true; });
                def("COLD_OCEAN", p => { p.top = B.GRAVEL; p.fill = B.GRAVEL; p.floor = B.GRAVEL; p.cold = 1; p.ocean = true; });
                def("LUKEWARM_OCEAN", p => { p.top = S; p.fill = S; p.floor = S; p.ocean = true; }); def("WARM_OCEAN", p => { p.top = S; p.fill = S; p.floor = S; p.ocean = true; });
                like("DEEP_OCEAN", "OCEAN", p => p.floor = B.GRAVEL); like("DEEP_FROZEN_OCEAN", "FROZEN_OCEAN"); like("DEEP_COLD_OCEAN", "COLD_OCEAN");
                like("DEEP_LUKEWARM_OCEAN", "LUKEWARM_OCEAN");
                def("RIVER", p => { p.top = S; p.fill = S; p.floor = -2; p.ocean = true; }); def("FROZEN_RIVER", p => { p.top = S; p.fill = S; p.floor = -2; p.cold = 2; p.ocean = true; });
                def("BEACH", p => { p.top = S; p.fill = S; p.sub = B.SANDSTONE; p.floor = S; }); def("SNOWY_BEACH", p => { p.top = B.SNOW; p.fill = S; p.sub = B.SANDSTONE; p.floor = S; p.snow = 1; p.cold = 2; });
                def("STONY_SHORE", p => { p.top = B.STONE; p.fill = B.STONE; p.floor = B.GRAVEL; });
                def("PLAINS", null);
                def("SUNFLOWER_PLAINS", null);
                def("SNOWY_PLAINS", p => { p.top = B.SNOW; p.snow = 1; p.cold = 2; });
                def("ICE_SPIKES", p => { p.top = B.SNOW; p.fill = B.SNOW; p.snow = 1; p.cold = 2; });
                def("DESERT", p => { p.top = S; p.fill = S; p.sub = B.SANDSTONE; p.floor = S; });
                def("SAVANNA", null);
                like("SAVANNA_PLATEAU", "SAVANNA"); like("WINDSWEPT_SAVANNA", "SAVANNA", p => p.mountain = true);
                def("FOREST", null);
                def("FLOWER_FOREST", null);
                def("BIRCH_FOREST", null);
                like("OLD_GROWTH_BIRCH_FOREST", "BIRCH_FOREST");
                def("DARK_FOREST", null);
                def("PALE_GARDEN", null);
                def("TAIGA", p => p.top = G);
                def("OLD_GROWTH_SPRUCE_TAIGA", null);
                like("OLD_GROWTH_PINE_TAIGA", "OLD_GROWTH_SPRUCE_TAIGA");
                def("SNOWY_TAIGA", p => { p.top = B.SNOW; p.snow = 1; p.cold = 2; });
                def("JUNGLE", null);
                def("SPARSE_JUNGLE", null);
                def("BAMBOO_JUNGLE", null);
                def("SWAMP", p => p.floor = -3);
                def("MANGROVE_SWAMP", p => { p.top = B.MUD; p.fill = B.MUD; p.floor = B.MUD; });
                def("BADLANDS", p => { p.top = B.RED_SAND; p.fill = B.TERRACOTTA; p.floor = B.RED_SAND; p.badlands = true; });
                like("ERODED_BADLANDS", "BADLANDS");
                def("WOODED_BADLANDS", p => { p.top = B.RED_SAND; p.fill = B.TERRACOTTA; p.floor = B.RED_SAND; p.badlands = true; });
                def("MEADOW", null);
                def("CHERRY_GROVE", null);
                def("GROVE", p => { p.top = B.SNOW; p.fill = B.DIRT; p.snow = 1; p.cold = 2; });
                def("SNOWY_SLOPES", p => { p.top = B.SNOW; p.fill = B.SNOW; p.snow = 1; p.cold = 2; p.mountain = true; });
                def("JAGGED_PEAKS", p => { p.top = B.SNOW; p.fill = B.SNOW; p.snow = 1; p.cold = 2; p.mountain = true; });
                def("FROZEN_PEAKS", p => { p.top = B.SNOW; p.fill = B.PACKED_ICE; p.snow = 1; p.cold = 2; p.mountain = true; });
                def("STONY_PEAKS", p => { p.top = B.STONE; p.fill = B.STONE; p.mountain = true; });
                def("WINDSWEPT_HILLS", p => p.mountain = true);
                like("WINDSWEPT_GRAVELLY_HILLS", "WINDSWEPT_HILLS", p => { p.top = B.GRAVEL; p.fill = B.GRAVEL; });
                like("WINDSWEPT_FOREST", "WINDSWEPT_HILLS");
                def("MUSHROOM_FIELDS", p => { p.top = B.MYCELIUM; p.fill = D; });
                def("LUSH_CAVES", null); def("DRIPSTONE_CAVES", null); def("DEEP_DARK", null);
            }
            {
                void T(string keys, int count, double chance, int extra, params (double, string)[] types)
                {
                    foreach (var k in keys.Split(' ')) MC_TREES[BIK[k]] = new TreeSet { count = count, chance = chance, extra = extra, types = types };
                }
                T("PLAINS SUNFLOWER_PLAINS", 0, 0.05, 1, (0.33333334, "fancy_oak"), (1, "oak"));
                T("FOREST", 10, 0.1, 1, (0.2, "birch"), (0.1, "fancy_oak"), (1, "oak"));
                T("FLOWER_FOREST", 6, 0.1, 1, (0.2, "birch"), (0.1, "fancy_oak"), (1, "oak"));
                T("BIRCH_FOREST", 10, 0.1, 1, (1, "birch"));
                T("OLD_GROWTH_BIRCH_FOREST", 10, 0.1, 1, (0.5, "super_birch"), (1, "birch"));
                T("DARK_FOREST", 16, 0, 0, (0.025, "giant_brown_mushroom"), (0.05, "giant_red_mushroom"), (0.6666667, "dark_oak"), (0.2, "birch"), (0.1, "fancy_oak"), (1, "oak"));
                T("PALE_GARDEN", 14, 0, 0, (1, "pale_oak"));
                T("TAIGA SNOWY_TAIGA", 10, 0.1, 1, (0.33333334, "pine"), (1, "spruce"));
                T("GROVE", 10, 0.1, 1, (0.33333334, "pine"), (1, "spruce"));
                T("OLD_GROWTH_SPRUCE_TAIGA", 10, 0.1, 1, (0.33333334, "mega_spruce"), (0.33333334, "pine"), (1, "spruce"));
                T("OLD_GROWTH_PINE_TAIGA", 10, 0.1, 1, (0.025641026, "mega_spruce"), (0.30769232, "mega_pine"), (0.33333334, "pine"), (1, "spruce"));
                T("SNOWY_PLAINS", 0, 0.1, 1, (1, "spruce"));
                T("JUNGLE", 50, 0.1, 1, (0.1, "fancy_oak"), (0.5, "jungle_bush"), (0.33333334, "mega_jungle"), (1, "jungle"));
                T("SPARSE_JUNGLE", 2, 0.1, 1, (0.1, "fancy_oak"), (0.5, "jungle_bush"), (1, "jungle"));
                T("BAMBOO_JUNGLE", 30, 0.1, 1, (0.05, "fancy_oak"), (0.15, "jungle_bush"), (0.7, "mega_jungle"), (1, null));
                T("SAVANNA SAVANNA_PLATEAU", 1, 0.1, 2, (0.8, "acacia"), (1, "oak"));
                T("WINDSWEPT_SAVANNA", 2, 0.1, 2, (0.8, "acacia"), (1, "oak"));
                T("WINDSWEPT_HILLS WINDSWEPT_GRAVELLY_HILLS", 0, 0.1, 1, (0.666, "spruce"), (0.1, "fancy_oak"), (1, "oak"));
                T("WINDSWEPT_FOREST", 3, 0.1, 1, (0.666, "spruce"), (0.1, "fancy_oak"), (1, "oak"));
                T("MEADOW", 0, 0.01, 1, (0.5, "fancy_oak"), (1, "super_birch"));
                T("CHERRY_GROVE", 10, 0.1, 1, (1, "cherry"));
                T("SWAMP", 2, 0.1, 1, (1, "swamp_oak"));
                T("MANGROVE_SWAMP", 25, 0, 0, (0.85, "mangrove"), (1, "tall_mangrove"));
                T("WOODED_BADLANDS", 5, 0.1, 1, (1, "oak"));
                var alias = new Dictionary<string, string>
                {
                    ["SUNFLOWER_PLAINS"] = "PLAINS", ["OLD_GROWTH_BIRCH_FOREST"] = "BIRCH_FOREST", ["OLD_GROWTH_PINE_TAIGA"] = "OLD_GROWTH_SPRUCE_TAIGA",
                    ["SAVANNA_PLATEAU"] = "SAVANNA", ["WINDSWEPT_SAVANNA"] = "SAVANNA", ["WINDSWEPT_GRAVELLY_HILLS"] = "WINDSWEPT_HILLS", ["WINDSWEPT_FOREST"] = "WINDSWEPT_HILLS",
                    ["ERODED_BADLANDS"] = "BADLANDS",
                };
                for (int i = 0; i < BIOME_LIST.Length; i++) ORIG_BIOME[i] = alias.TryGetValue(BIOME_LIST[i][0], out var a) ? BIK[a] : BPROP[i].@base;
            }
            {
                void P(string name, int tries, int xz, int y, string kind) => PATCHES[name] = new PatchDef { tries = tries, xz = xz, y = y, kind = kind };
                P("grass", 32, 7, 3, "grass"); P("grass_jungle", 32, 7, 3, "grass_jungle");
                P("grass_taiga", 32, 7, 3, "grass_taiga"); P("tall_grass", 96, 7, 3, "tall_grass");
                P("large_fern", 96, 7, 3, "large_fern"); P("dead_bush", 4, 7, 3, "dead_bush");
                P("sugar_cane", 20, 4, 0, "sugar_cane"); P("pumpkin", 96, 7, 3, "pumpkin");
                P("melon", 64, 7, 3, "melon"); P("berry", 96, 7, 3, "berry");
                P("sunflower", 96, 7, 3, "sunflower"); P("brown_mushroom", 96, 7, 3, "brown_mushroom");
                P("red_mushroom", 96, 7, 3, "red_mushroom");
                P("flower_default", 64, 6, 2, "flower_default"); P("flower_plains", 64, 6, 2, "flower_plains");
                P("flower_swamp", 64, 6, 2, "blue_orchid"); P("eyeblossom", 48, 6, 2, "eyeblossom"); P("flower_flower_forest", 96, 6, 2, "flower_forest");
                P("flower_meadow", 96, 6, 2, "flower_meadow"); P("forest_flowers", 96, 7, 3, "forest_flowers");
            }
            {
                VegPlace V(string p, int n = -1, int r = 0, int[] nt = null, int[] rn = null) => new VegPlace { patch = p, n = n, r = r, nt = nt, rn = rn };
                void Set(string keys, VegPlace[] list) { foreach (var k in keys.Split(' ')) MC_VEG[BIK[k]] = list; }
                VegPlace[] Cat(params object[] parts)
                {
                    var l = new List<VegPlace>();
                    foreach (var p in parts) { if (p is VegPlace v) l.Add(v); else l.AddRange((VegPlace[])p); }
                    return l.ToArray();
                }
                var mush = new[] { V("brown_mushroom", r: 256), V("red_mushroom", r: 512) };
                var extra = new[] { V("sugar_cane", r: 6), V("pumpkin", r: 300) };
                var forest = Cat(V("forest_flowers", r: 7, rn: new[] { -1, 3 }), V("flower_default", r: 32), V("grass", n: 2), mush, extra);
                Set("PLAINS", Cat(V("tall_grass", nt: new[] { 0, 7 }, r: 32), V("flower_plains", nt: new[] { 15, 4 }, r: 32), V("grass", nt: new[] { 5, 10 }), mush, extra));
                Set("SUNFLOWER_PLAINS", Cat(V("sunflower", r: 3), V("tall_grass", nt: new[] { 0, 7 }, r: 32), V("flower_plains", nt: new[] { 15, 4 }, r: 32), V("grass", nt: new[] { 5, 10 }), mush, extra));
                Set("FOREST BIRCH_FOREST OLD_GROWTH_BIRCH_FOREST DARK_FOREST", forest);
                Set("FLOWER_FOREST", Cat(V("flower_flower_forest", n: 3, r: 2), V("forest_flowers", r: 7, rn: new[] { -3, 1 }), V("grass", n: 2), mush, extra));
                Set("TAIGA SNOWY_TAIGA", Cat(V("large_fern", r: 5), V("grass_taiga", n: 1), V("flower_default", r: 32), V("brown_mushroom", r: 4), V("red_mushroom", r: 256), V("berry", r: 32), extra));
                Set("OLD_GROWTH_SPRUCE_TAIGA OLD_GROWTH_PINE_TAIGA", Cat(V("large_fern", r: 5), V("grass_taiga", n: 7), V("dead_bush", n: 1), V("flower_default", r: 32),
                    V("brown_mushroom", r: 4), V("red_mushroom", r: 171), V("berry", r: 32), extra));
                Set("SAVANNA SAVANNA_PLATEAU WINDSWEPT_SAVANNA", Cat(V("tall_grass", r: 5), V("flower_default", r: 16), V("grass", n: 20), mush, extra));
                Set("JUNGLE BAMBOO_JUNGLE", Cat(V("flower_default", r: 16), V("grass_jungle", n: 25), V("melon", r: 6), mush, extra));
                Set("SPARSE_JUNGLE", Cat(V("flower_default", r: 16), V("grass_jungle", n: 25), V("melon", r: 64), mush, extra));
                Set("SWAMP", Cat(V("flower_swamp", r: 32), V("grass", n: 5), V("dead_bush", n: 1), V("brown_mushroom", n: 2), V("red_mushroom", r: 64), V("sugar_cane", n: 3), V("pumpkin", r: 300)));
                Set("MANGROVE_SWAMP", Cat(V("grass", n: 5), V("dead_bush", n: 1)));
                Set("DESERT", Cat(V("dead_bush", n: 2), V("sugar_cane", n: 1), V("pumpkin", r: 300)));
                Set("BADLANDS ERODED_BADLANDS", Cat(V("dead_bush", n: 20), V("sugar_cane", r: 5), mush));
                Set("WOODED_BADLANDS", Cat(V("grass", n: 1), V("dead_bush", n: 20), V("sugar_cane", r: 5), mush));
                Set("MEADOW", Cat(V("flower_meadow", n: 1), V("grass", n: 1)));
                Set("PALE_GARDEN", Cat(V("eyeblossom", r: 2), V("grass", n: 2)));
                Set("CHERRY_GROVE", Cat(V("grass", nt: new[] { 5, 10 }), V("flower_plains", r: 32)));
                Set("WINDSWEPT_HILLS WINDSWEPT_GRAVELLY_HILLS WINDSWEPT_FOREST SNOWY_PLAINS STONY_SHORE BEACH RIVER", Cat(V("grass", n: 1), V("flower_default", r: 32), mush, extra));
            }

            CORAL_BLOCKS = new[] { B.TUBE_CORAL_BLOCK, B.BRAIN_CORAL_BLOCK, B.BUBBLE_CORAL_BLOCK, B.FIRE_CORAL_BLOCK, B.HORN_CORAL_BLOCK };
            CORAL_FANS = new[] { B.TUBE_CORAL_FAN, B.BRAIN_CORAL_FAN, B.BUBBLE_CORAL_FAN, B.FIRE_CORAL_FAN, B.HORN_CORAL_FAN };
            {
                string[] k = { "TERRACOTTA", "ORANGE_TERRACOTTA", "TERRACOTTA", "YELLOW_TERRACOTTA", "BROWN_TERRACOTTA", "TERRACOTTA",
                    "RED_TERRACOTTA", "TERRACOTTA", "WHITE_TERRACOTTA", "LIGHT_GRAY_TERRACOTTA", "TERRACOTTA", "ORANGE_TERRACOTTA",
                    "TERRACOTTA", "TERRACOTTA", "BROWN_TERRACOTTA", "RED_TERRACOTTA", "ORANGE_TERRACOTTA", "TERRACOTTA",
                    "YELLOW_TERRACOTTA", "TERRACOTTA", "WHITE_TERRACOTTA", "TERRACOTTA" };
                TERRA_BANDS = new int[k.Length];
                for (int i = 0; i < k.Length; i++)
                {
                    int id = Blocks.Id(k[i]);
                    if (id == 0) id = Blocks.Id(k[i].Replace("ORANGE_TERRACOTTA", "TERRA_ORANGE").Replace("WHITE_TERRACOTTA", "TERRA_WHITE"));
                    if (id == 0) id = B.TERRACOTTA;
                    TERRA_BANDS[i] = id;
                }
            }
            IGLOO_BIOMES = new HashSet<int> { BI.SNOWY_TAIGA, BI.SNOWY_PLAINS, BI.SNOWY_SLOPES };
            RUIN_COLD = new HashSet<int> { BI.FROZEN_OCEAN, BI.COLD_OCEAN, BI.OCEAN, BI.DEEP_FROZEN_OCEAN, BI.DEEP_COLD_OCEAN, BI.DEEP_OCEAN };
            RUIN_WARM = new HashSet<int> { BI.LUKEWARM_OCEAN, BI.WARM_OCEAN, BI.DEEP_LUKEWARM_OCEAN };
            SHIP_LOOT = new Dictionary<string, int> { ["supply_chest"] = Loot.Get("SHIPWRECK_SUPPLY"), ["map_chest"] = Loot.Get("SHIPWRECK_MAP"), ["treasure_chest"] = Loot.Get("SHIPWRECK_TREASURE") };

            // beard kernel
            for (int zi = 0; zi < 24; zi++) for (int xi = 0; xi < 24; xi++) for (int yi = 0; yi < 24; yi++)
            {
                int x = xi - 12, y = yi - 12, z = zi - 12; double d0 = x * x + z * z, d1 = y + 0.5, d2 = d1 * d1;
                BEARD_KERNEL[zi * 576 + xi * 24 + yi] = (float)(-d1 / Math.Sqrt(d2 / 2 + d0 / 2) / 2 * Math.Exp(-(d2 / 16 + d0 / 16)));
            }

            // structure sets: the two jigsaw sets first (STRUCTURE_SETS.unshift), then the template ones,
            // then the code-built ones of bigstructures.js (STRUCTURE_SETS.push)
            STRUCTURE_SETS_VIL = new StructSet
            {
                salt = 52918417, spacing = 34, separation = 8, reach = 7, depth = 6,
                types = new[] {
                    new JigType { pool = "village/plains/town_centers", biomes = new[] { BI.PLAINS, BI.MEADOW } }, new JigType { pool = "village/desert/town_centers", biomes = new[] { BI.DESERT } },
                    new JigType { pool = "village/savanna/town_centers", biomes = new[] { BI.SAVANNA } }, new JigType { pool = "village/snowy/town_centers", biomes = new[] { BI.SNOWY_PLAINS } },
                    new JigType { pool = "village/taiga/town_centers", biomes = new[] { BI.TAIGA } } },
                start = (g, x, z) => g.JigsawStart(STRUCTURE_SETS_VIL, x, z),
            };
            // pillager outposts: frequency 0.2, never within 10 chunks of a village placement chunk
            STRUCTURE_SETS_OUTPOST = new StructSet
            {
                salt = 73810259, spacing = 32, separation = 8, reach = 7, depth = 7,
                types = new[] { new JigType { pool = "pillager_outpost/base_plates",
                    biomes = new[] { BI.DESERT, BI.PLAINS, BI.SAVANNA, BI.SNOWY_PLAINS, BI.TAIGA, BI.MEADOW, BI.FROZEN_PEAKS, BI.JAGGED_PEAKS, BI.STONY_PEAKS, BI.SNOWY_SLOPES, BI.CHERRY_GROVE, BI.GROVE } } },
                start = (g, x, z) => g.OutpostStart(x, z),
            };
            JIGSAW_SETS = new[] { STRUCTURE_SETS_VIL, STRUCTURE_SETS_OUTPOST };
            STRUCTURE_SETS = new List<StructSet>
            {
                STRUCTURE_SETS_VIL, STRUCTURE_SETS_OUTPOST,
                new StructSet { salt = 38810541, spacing = 32, separation = 8, reach = 1, start = (g, x, z) => g.IglooStart(x, z) },
                new StructSet { salt = 61294733, spacing = 24, separation = 4, reach = 2, start = (g, x, z) => g.ShipwreckStart(x, z) },
                new StructSet { salt = 29467103, spacing = 20, separation = 8, reach = 3, start = (g, x, z) => g.OceanRuinStart(x, z) },
                // bigstructures.js (spacing / separation in chunks, own salts)
                new StructSet { salt = 47120389, spacing = 32, separation = 8, reach = 2, clearTrees = true, start = (g, x, z) => g.DesertPyramidStart(x, z) },
                new StructSet { salt = 55301927, spacing = 32, separation = 8, reach = 1, clearTrees = true, start = (g, x, z) => g.SwampHutStart(x, z) },
                new StructSet { salt = 22037, spacing = 16, separation = 4, reach = 6, start = (g, x, z) => g.MineStart(x, z) },
                new StructSet { salt = 61210487, clearTrees = true, spacing = 32, separation = 8, reach = 1, start = (g, x, z) => g.JungleTempleStart(x, z) },
                new StructSet { salt = 33590147, clearTrees = true, spacing = 28, separation = 10, reach = 1, start = (g, x, z) => g.RuinedPortalStart(x, z) },
                new StructSet { salt = 10387319, clearTrees = true, spacing = 48, separation = 16, reach = 3, start = (g, x, z) => g.MansionStart(x, z) },
                new StructSet { salt = 10387313, clearTrees = true, spacing = 32, separation = 5, reach = 3, start = (g, x, z) => g.MonumentStart(x, z) },
                new StructSet { salt = 72411097, spacing = 64, separation = 24, reach = 4, start = (g, x, z) => g.StrongholdStart(x, z) },
            };
            OWN_TREELESS_SETS = STRUCTURE_SETS.FindAll(s => s.clearTrees).ToArray();
        }
    }
}
