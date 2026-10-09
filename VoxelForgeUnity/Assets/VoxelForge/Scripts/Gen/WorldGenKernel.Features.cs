// Voxel Forge — Unity port. World generator: trees / plants / aquatic decorators / analytic baseBlock().
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed partial class WorldGenKernel
    {
        sealed class TreeCfg { public int cell; public double[] th; public string[] kind; }
        static TreeCfg TC(int cell, params object[] a)
        {
            var c = new TreeCfg { cell = cell, th = new double[a.Length / 2], kind = new string[a.Length / 2] };
            for (int i = 0; i < a.Length / 2; i++) { c.th[i] = Convert.ToDouble(a[i * 2]); c.kind[i] = (string)a[i * 2 + 1]; }
            return c;
        }
        static readonly Dictionary<string, TreeCfg> TREE_CFG = new Dictionary<string, TreeCfg> {
            { "FOREST", TC(6, 0.5, "oak", 0.75, "birch", 0.87, "fancy_oak") }, { "BIRCH_FOREST", TC(6, 0.78, "birch", 0.9, "oak") },
            { "FLOWER_FOREST", TC(9, 0.45, "oak", 0.7, "birch") }, { "DARK_FOREST", TC(6, 0.62, "dark_oak", 0.82, "oak", 0.9, "birch") },
            { "JUNGLE", TC(5, 0.72, "jungle", 0.85, "oak") }, { "SPARSE_JUNGLE", TC(11, 0.5, "jungle", 0.65, "oak") }, { "TAIGA", TC(6, 0.85, "spruce") },
            { "OLD_GROWTH_TAIGA", TC(6, 0.55, "tall_spruce", 0.85, "spruce") }, { "SNOWY_TAIGA", TC(8, 0.7, "spruce") }, { "SNOWY", TC(18, 0.4, "spruce") },
            { "PLAINS", TC(18, 0.3, "oak", 0.42, "fancy_oak") }, { "SAVANNA", TC(13, 0.55, "acacia") }, { "MEADOW", TC(26, 0.25, "oak") }, { "GROVE", TC(7, 0.75, "spruce") },
            { "SWAMP", TC(9, 0.55, "oak") }, { "WINDSWEPT", TC(15, 0.25, "oak", 0.4, "spruce") }, { "CHERRY_GROVE", TC(9, 0.4, "cherry") },
            { "BAMBOO_JUNGLE", TC(6, 0.5, "jungle", 0.65, "oak") }, { "WOODED_BADLANDS", TC(10, 0.35, "oak") }, { "MANGROVE_SWAMP", TC(7, 0.45, "oak", 0.65, "dark_oak") } };
        int baseTreeHeight(int x, int z) { return 4 + (int)Math.Floor(hash2Main(x + 77, z - 77) * 3); }
        public string featureKindAt(int x, int z)
        {
            int slot = hotSlot(x, z, FEATURE_KIND_HOT_MASK);
            if (featureKindHotValid[slot] && featureKindHotX[slot] == x && featureKindHotZ[slot] == z) return featureKindHotValue[slot];
            var c = columnInfo(x, z); double h = c.origH; var bi = c.biome;
            string o = null;
            if (h > SEA && h < WORLD_MAX_Y - 14)
            {
                if (bi == "DESERT") { if (hash2Main(x * 3 + 11, z * 3 - 7) < 0.006) o = "cactus"; }
                else if (bi != "BADLANDS" && !(bi == "WOODED_BADLANDS" && h < SEA + 10) && !(bi == "WINDSWEPT" && surfaceRuggedness(x, z, h) >= 0.53))
                {
                    if (bi == "MUSHROOM_FIELDS")
                    {
                        double q = hash2Main(x * 3 + 11, z * 3 - 7);
                        if (q < 0.004) o = "giant_red_mushroom"; else if (q < 0.008) o = "giant_brown_mushroom";
                    }
                    else if (bi == "ICE_SPIKES") { if (hash2Main(x * 3 + 11, z * 3 - 7) < 0.01) o = "ice_spike"; }
                    else
                    {
                        TreeCfg cfg;
                        if (TREE_CFG.TryGetValue(bi, out cfg))
                        {
                            int cell = cfg.cell, cx = ffloor(x / (double)cell), cz = ffloor(z / (double)cell),
                                px = cx * cell + (int)Math.Floor(hash2Main(cx * 17 + 3, cz * 13 - 5) * cell),
                                pz = cz * cell + (int)Math.Floor(hash2Main(cx * 7 - 11, cz * 23 + 9) * cell);
                            if (x == px && z == pz)
                            {
                                double q = hash2Main(cx * 31 + 1, cz * 37 - 1);
                                for (int i = 0; i < cfg.th.Length; i++) if (q < cfg.th[i]) { o = cfg.kind[i]; break; }
                            }
                        }
                    }
                }
                if (o != null && ravineContains2D(x, z)) o = null;
            }
            featureKindHotX[slot] = x; featureKindHotZ[slot] = z; featureKindHotValue[slot] = o; featureKindHotValid[slot] = true;
            return o;
        }
        FeatureInfo featureInfoFromKind(int x, int z, string kind)
        {
            if (kind == null) return null;
            int height = baseTreeHeight(x, z), log = B.LOG, leaves = B.LEAVES;
            switch (kind)
            {
                case "birch": height++; log = B.BIRCH_LOG; leaves = B.BIRCH_LEAVES; break;
                case "spruce": height += 2; log = B.SPRUCE_LOG; leaves = B.SPRUCE_LEAVES; break;
                case "tall_spruce": height = 10 + (int)Math.Floor(hash2Main(x + 7, z) * 4); log = B.SPRUCE_LOG; leaves = B.SPRUCE_LEAVES; break;
                case "dark_oak": height = 7 + (int)Math.Floor(hash2Main(x + 3, z - 3) * 3); log = B.DARK_LOG; leaves = B.DARK_LEAVES; break;
                case "jungle": height = 8 + (int)Math.Floor(hash2Main(x - 9, z + 9) * 5); log = B.JUNGLE_LOG; leaves = B.JUNGLE_LEAVES; break;
                case "acacia": height = 5 + (int)Math.Floor(hash2Main(x + 1, z + 1) * 2); log = B.ACACIA_LOG; leaves = B.ACACIA_LEAVES; break;
                case "fancy_oak": height = 7 + (int)Math.Floor(hash2Main(x + 19, z - 19) * 3); break;
                case "cherry": height = 4 + (int)Math.Floor(hash2Main(x + 5, z - 5) * 2); leaves = B.FLOWERING_AZALEA_LEAVES; break;
                case "cactus": height = 2 + (int)Math.Floor(hash2Main(x + 33, z - 33) * 2); break;
                case "giant_red_mushroom": height = 4 + (int)Math.Floor(hash2Main(x, z) * 3); log = B.MUSHROOM_STEM; leaves = B.RED_MUSHROOM_BLOCK; break;
                case "giant_brown_mushroom": height = 3 + (int)Math.Floor(hash2Main(x + 1, z - 1) * 2); log = B.MUSHROOM_STEM; leaves = B.BROWN_MUSHROOM_BLOCK; break;
                case "ice_spike": height = 4 + (int)Math.Floor(hash2Main(x + 2, z - 2) * 7); log = B.PACKED_ICE; leaves = B.PACKED_ICE; break;
            }
            return new FeatureInfo { kind = kind, height = height, log = log, leaves = leaves };
        }
        public FeatureInfo featureInfoAt(int x, int z, int? surfaceY = null) { var q = featurePlacementAt(x, z); return q != null ? q.f : null; }
        static bool isActualTreeKind(string k) { return k != null && k != "cactus" && k != "giant_red_mushroom" && k != "giant_brown_mushroom" && k != "ice_spike"; }
        public FeatureInfo treeInfoAt(int x, int z, int? surfaceY = null) { var f = featureInfoAt(x, z, surfaceY); return f != null && isActualTreeKind(f.kind) ? f : null; }
        public bool treeAt(int x, int z, int? surfaceY = null) { return treeInfoAt(x, z, surfaceY) != null; }
        public bool cactusAt(int x, int z, int? surfaceY = null) { var f = featureInfoAt(x, z, surfaceY); return f != null && f.kind == "cactus"; }
        public int bambooInfoAt(int x, int z, int? surfaceY = null) { var p = landPlantInfoAt(x, z, surfaceY); return p != null && p.id == B.BAMBOO ? p.height : 0; }
        public int iceSpikeInfoAt(int x, int z, int? surfaceY = null) { var f = featureInfoAt(x, z, surfaceY); return f != null && f.kind == "ice_spike" ? f.height : 0; }
        static readonly byte[] TALL_PLANT_TOP = BuildTallTop();
        static byte[] BuildTallTop()
        {
            var t = new byte[256];
            t[B.LARGE_FERN] = (byte)B.LARGE_FERN_TOP; t[B.SUNFLOWER] = (byte)B.SUNFLOWER_TOP; t[B.LILAC] = (byte)B.LILAC_TOP; t[B.ROSE_BUSH] = (byte)B.ROSE_BUSH_TOP; t[B.PEONY] = (byte)B.PEONY_TOP;
            return t;
        }
        // main g1/C1 semantics: leaves may replace cross-shaped one-block vegetation only.
        static readonly bool[] LEAF_REPLACEABLE = MakeMask(B.SHORT_GRASS, B.DANDELION, B.POPPY, B.BLUE_ORCHID, B.ALLIUM, B.AZURE_BLUET, B.CORNFLOWER, B.FERN, B.DEAD_BUSH,
            B.RED_MUSHROOM, B.BROWN_MUSHROOM, B.OXEYE_DAISY, B.LILY_OF_THE_VALLEY, B.ORANGE_TULIP, B.PINK_TULIP, B.RED_TULIP, B.WHITE_TULIP, B.SWEET_BERRY_BUSH, B.SUGAR_CANE,
            B.BAMBOO, B.LARGE_FERN, B.LARGE_FERN_TOP, B.SUNFLOWER, B.SUNFLOWER_TOP, B.LILAC, B.LILAC_TOP, B.ROSE_BUSH, B.ROSE_BUSH_TOP, B.PEONY, B.PEONY_TOP);
        static bool sugarCaneGroundSupported(int ground) { return ground == B.SAND || ground == B.RED_SAND; }
        static bool vegetationSupported(int id, int ground)
        {
            if (id == B.SUGAR_CANE) return sugarCaneGroundSupported(ground);
            if (ground == B.GRASS || ground == B.PODZOL) return true;
            if (id == B.DEAD_BUSH) return ground == B.SAND || ground == B.RED_SAND;
            if (id == B.RED_MUSHROOM || id == B.BROWN_MUSHROOM) return ground == B.MYCELIUM || ground == B.PODZOL || ground == B.MUD;
            if ((id == B.SHORT_GRASS || id == B.FERN) && ground == B.MUD) return true;
            if (id == B.BAMBOO) return ground == B.GRASS || ground == B.PODZOL || ground == B.DIRT;
            return false;
        }
        static bool treeGroundSupported(int ground) { return ground == B.GRASS || ground == B.DIRT || ground == B.PODZOL || ground == B.MYCELIUM || ground == B.MUD; }
        static bool cactusGroundSupported(int ground) { return ground == B.SAND || ground == B.RED_SAND; }
        static bool landPlantSupported(int id, int ground) { return vegetationSupported(id, ground); }
        static bool featureColumnSupportsTree(SurfaceInfo s) { return treeGroundSupported(s.ground) || (s.ground == B.SNOW && treeGroundSupported(s.below)); }
        bool cactusSideClearAt(int x, int z, int h, int height)
        {
            for (int y = h + 1; y <= h + height; y++)
                foreach (var d in new[] { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } })
                {
                    int nx = x + d[0], nz = z + d[1];
                    if (ravineContains2D(nx, nz)) continue;
                    if (postCarverSurfaceInfoAt(nx, nz).y >= y) return false;
                }
            return true;
        }
        sealed class Placement { public int h; public FeatureInfo f; }
        Placement featurePlacementAt(int x, int z)
        {
            var kind = featureKindAt(x, z);
            if (kind == null || surfaceStructureReservedAt(x, z)) return null;
            int h = generatedSurfaceAt(x, z);
            return new Placement { h = h, f = featureInfoFromKind(x, z, kind) };
        }
        static int chooseBy(double q, int[] a) { return a[(int)Math.Floor(q * a.Length) % a.Length]; }
        static readonly int[] PLAINS_FLOWERS = { B.DANDELION, B.POPPY, B.CORNFLOWER, B.OXEYE_DAISY, B.AZURE_BLUET };
        static readonly int[] MEADOW_FLOWERS = { B.DANDELION, B.CORNFLOWER, B.OXEYE_DAISY, B.POPPY };
        static readonly int[] FLOWER_FOREST_FLOWERS = { B.DANDELION, B.POPPY, B.ALLIUM, B.CORNFLOWER, B.OXEYE_DAISY, B.AZURE_BLUET, B.LILY_OF_THE_VALLEY, B.ORANGE_TULIP, B.PINK_TULIP, B.RED_TULIP, B.WHITE_TULIP };
        static readonly int[] FOREST_TALL = { B.LILAC, B.ROSE_BUSH, B.PEONY };
        static readonly int[] CHERRY_FLOWERS = { B.PINK_TULIP, B.ALLIUM, B.OXEYE_DAISY, B.DANDELION, B.PINK_TULIP };
        public LandPlant landPlantInfoAt(int x, int z, int? surfaceY = null, int? ground = null)
        {
            var c = columnInfo(x, z); double ch = c.origH; var bi = c.biome; int h = surfaceY ?? generatedSurfaceAt(x, z);
            if (ch >= WORLD_MAX_Y - 4 || ravineContains2D(x, z) || featureKindAt(x, z) != null) return null;
            double a = hash2Main(x * 7 + 3, z * 7 - 5), q = hash2Main(x * 11 - 7, z * 11 + 13);
            int id = B.AIR;
            if (h >= SEA && ch <= SEA + 2 && a < 0.32 && bi != "DESERT" && bi != "BADLANDS" &&
                (columnInfo(x + 1, z).origH <= SEA || columnInfo(x - 1, z).origH <= SEA || columnInfo(x, z + 1).origH <= SEA || columnInfo(x, z - 1).origH <= SEA))
                id = B.SUGAR_CANE;
            else
            {
                if (ch <= SEA) return null;
                switch (bi)
                {
                    case "PLAINS": if (a < 0.1) id = B.SHORT_GRASS; else if (a < 0.14) id = chooseBy(q, PLAINS_FLOWERS); break;
                    case "MEADOW": if (a < 0.14) id = B.SHORT_GRASS; else if (a < 0.205) id = chooseBy(q, MEADOW_FLOWERS); else if (a < 0.21) id = B.SUNFLOWER; break;
                    case "FLOWER_FOREST": if (a < 0.07) id = B.SHORT_GRASS; else if (a < 0.26) id = chooseBy(q, FLOWER_FOREST_FLOWERS); else if (a < 0.275) id = q < 0.5 ? B.LILAC : B.ROSE_BUSH; break;
                    case "FOREST": if (a < 0.06) id = B.SHORT_GRASS; else if (a < 0.08) id = B.FERN; else if (a < 0.092) id = q < 0.5 ? B.DANDELION : B.POPPY; else if (a < 0.098) id = chooseBy(q, FOREST_TALL); break;
                    case "BIRCH_FOREST": if (a < 0.06) id = B.SHORT_GRASS; else if (a < 0.086) id = q < 0.5 ? B.LILY_OF_THE_VALLEY : B.DANDELION; else if (a < 0.093) id = q < 0.5 ? B.LILAC : B.PEONY; break;
                    case "DARK_FOREST": if (a < 0.05) id = B.SHORT_GRASS; else if (a < 0.075) id = q < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM; else if (a < 0.09) id = B.LARGE_FERN; else if (a < 0.097) id = q < 0.5 ? B.ROSE_BUSH : B.PEONY; break;
                    case "JUNGLE": if (a < 0.12) id = B.SHORT_GRASS; else if (a < 0.22) id = B.FERN; break;
                    case "SPARSE_JUNGLE": if (a < 0.14) id = B.SHORT_GRASS; else if (a < 0.2) id = B.FERN; break;
                    case "TAIGA": case "OLD_GROWTH_TAIGA": case "SNOWY_TAIGA": case "GROVE":
                        if (a < 0.08) id = B.FERN; else if (a < 0.12) id = q < 0.5 ? B.SHORT_GRASS : B.LARGE_FERN; else if (a < 0.135) id = B.SWEET_BERRY_BUSH; break;
                    case "SAVANNA": if (a < 0.25) id = B.SHORT_GRASS; break;
                    case "SWAMP": if (a < 0.1) id = B.SHORT_GRASS; else if (a < 0.13) id = B.FERN; else if (a < 0.145) id = B.BLUE_ORCHID; break;
                    case "WINDSWEPT": if (a < 0.05) id = B.SHORT_GRASS; break;
                    case "DESERT": if (a < 0.016) id = B.DEAD_BUSH; break;
                    case "BADLANDS": if (a < 0.02) id = B.DEAD_BUSH; break;
                    case "WOODED_BADLANDS": if (a < 0.04) id = B.DEAD_BUSH; else if (a < 0.08) id = B.SHORT_GRASS; break;
                    case "CHERRY_GROVE": if (a < 0.14) id = B.SHORT_GRASS; else if (a < 0.34) id = chooseBy(q, CHERRY_FLOWERS); break;
                    case "BAMBOO_JUNGLE": if (a < 0.32) id = B.BAMBOO; else if (a < 0.42) id = B.FERN; else if (a < 0.48) id = B.SHORT_GRASS; break;
                    case "MUSHROOM_FIELDS": if (a < 0.22) id = q < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM; break;
                    case "MANGROVE_SWAMP": if (a < 0.08) id = B.SHORT_GRASS; else if (a < 0.12) id = B.FERN; break;
                }
            }
            if (id == 0) return null;
            int g = ground ?? surfaceBlockForBiome(bi, x, z, h, c.origH);
            if (!vegetationSupported(id, g)) return null;
            int height = 1, top = TALL_PLANT_TOP[id];
            if (id == B.SUGAR_CANE) height = 2 + (hash2Main(x * 13 + 5, z * 13 - 5) < 0.4 ? 1 : 0);
            else if (id == B.BAMBOO) height = 3 + (int)Math.Floor(hash2Main(x * 11 - 4, z * 11 + 6) * 4);
            else if (top != 0) height = 2;
            return new LandPlant { id = id, height = height, top = top };
        }
        public int landPlantAt(int x, int z, int? surfaceY = null, int? ground = null) { var p = landPlantInfoAt(x, z, surfaceY, ground); return p != null ? p.id : B.AIR; }
        int rareCropAt(int x, int z, int h, int ground)
        {
            if (ground != B.GRASS) return B.AIR;
            if (hash2Main(x * 17 + 101, z * 17 - 59) < 0.00013) return B.PUMPKIN;
            var bi = biomeAt(x, z);
            if ((bi == "JUNGLE" || bi == "SPARSE_JUNGLE" || bi == "BAMBOO_JUNGLE") && hash2Main(x * 19 - 71, z * 19 + 43) < 0.0016) return B.MELON;
            return B.AIR;
        }
        static readonly int[] CORAL_BLOCKS = { B.TUBE_CORAL_BLOCK, B.BRAIN_CORAL_BLOCK, B.BUBBLE_CORAL_BLOCK, B.FIRE_CORAL_BLOCK, B.HORN_CORAL_BLOCK };
        static readonly int[] CORAL_FANS = { B.TUBE_CORAL_FAN, B.BRAIN_CORAL_FAN, B.BUBBLE_CORAL_FAN, B.FIRE_CORAL_FAN, B.HORN_CORAL_FAN };
        static readonly bool[] CORAL_BLOCK_MASK = MakeMask(CORAL_BLOCKS);
        static readonly bool[] AQUATIC_PLANT_MASK = MakeMask(B.KELP, B.SEAGRASS, B.SEA_PICKLE, B.TUBE_CORAL_FAN, B.BRAIN_CORAL_FAN, B.BUBBLE_CORAL_FAN, B.FIRE_CORAL_FAN, B.HORN_CORAL_FAN);
        static bool aquaticGroundSupport(int id)
        {
            if (id == B.AIR || id == B.WATER || id == B.WATER_FALLING || id == B.FLOW7 || id == B.FLOW6 || id == B.FLOW5 || id == B.FLOW4 || id == B.FLOW3 || id == B.FLOW2 || id == B.FLOW1 || id == B.LAVA || id == B.LAVA_FLOW2 || id == B.LAVA_FLOW1) return false;
            return !(id >= 0 && id < 256 && AQUATIC_PLANT_MASK[id]);
        }
        int kelpTopAt(int x, int z, int floor)
        {
            double shape = valueFbm4Main(x * 0.025 + 313.7, z * 0.025 - 191.3);
            if (shape < 0.6) return floor;
            double strength = (shape - 0.6) / 0.4, r = hash2Main(x * 5 + 41, z * 5 - 23);
            if (r > 0.3 + strength * 0.55) return floor;
            int room = SEA - 2 - floor;
            if (room > 25) room = 25;
            if (room < 1) return floor;
            int top = floor + 1 + (int)Math.Floor(hash2Main(x * 9 - 3, z * 9 + 7) * room);
            if (top > SEA - 1) top = SEA - 1;
            return top;
        }
        int frozenOceanVoxelAt(int x, int y, int z, int floor, string bi)
        {
            if (bi == "FROZEN_RIVER") return y == SEA && floor < SEA ? B.ICE : B.AIR;
            if (bi != "FROZEN_OCEAN" || floor >= SEA) return B.AIR;
            int depth = SEA - floor; double shape = valueFbm4Main(x * 0.02 + 911.1, z * 0.02 - 333.3);
            if (shape > 0.72 && depth >= 3)
            {
                double strength = Math.Min(1, (shape - 0.72) / 0.28);
                int top = SEA + (int)round(strength * 6), bottom = Math.Max(floor + 1, SEA - 1 - (int)round(strength * 5));
                if (y >= bottom && y <= top && y < WORLD_MAX_Y - 1) return B.PACKED_ICE;
                if (top > SEA + 2 && y == top + 1 && y < WORLD_MAX_Y) return B.SNOW;
                return B.AIR;
            }
            return y == SEA ? B.ICE : B.AIR;
        }
        public int aquaPlantAt(int x, int y, int z, int? surfaceY = null)
        {
            if (y > SEA || y < WORLD_MIN_Y + 2) return B.AIR;
            var bi = biomeAt(x, z); int floor = surfaceY ?? generatedSurfaceAt(x, z), depth = SEA - floor;
            if (depth < 1 || y <= floor) return B.AIR;
            double f = hash2Main(x * 5 + 41, z * 5 - 23), w = hash2Main(x * 9 - 17, z * 9 + 31);
            if (bi == "WARM_OCEAN")
            {
                if (y > SEA) return B.AIR;
                double reef = valueFbm4Main(x * 0.03 + 123.4, z * 0.03 - 567.8);
                if (depth >= 4 && reef > 0.72)
                {
                    int count = 1 + (int)Math.Floor((reef - 0.72) * 14);
                    if (count > 3) count = 3;
                    count = Math.Min(count, Math.Max(0, depth - 2));
                    int d = y - (floor + 1);
                    if (d >= 0 && d < count) return CORAL_BLOCKS[(int)Math.Floor(hash2Main(x * 3 + d * 7, z * 3 - d * 5) * CORAL_BLOCKS.Length) % CORAL_BLOCKS.Length];
                    if (d == count && y <= SEA)
                    {
                        if (w < 0.4) return CORAL_FANS[(int)Math.Floor(w * 12) % CORAL_FANS.Length];
                        if (w < 0.55) return B.SEA_PICKLE;
                    }
                    return B.AIR;
                }
                if (y != floor + 1) return B.AIR;
                if (f < 0.06) return B.SEA_PICKLE;
                if (f < 0.17) return CORAL_FANS[(int)Math.Floor(w * CORAL_FANS.Length) % CORAL_FANS.Length];
                if (f < 0.34) return B.SEAGRASS;
                return B.AIR;
            }
            if (bi == "COLD_OCEAN" || bi == "OCEAN" || bi == "LUKEWARM_OCEAN")
            {
                if (depth >= 3) { int top = kelpTopAt(x, z, floor); if (top > floor) return y <= top ? B.KELP : B.AIR; }
                if (y != floor + 1) return B.AIR;
                double chance = bi == "LUKEWARM_OCEAN" ? 0.3 : bi == "OCEAN" ? 0.24 : 0.14;
                return f < chance ? B.SEAGRASS : B.AIR;
            }
            if (bi == "RIVER") return y == floor + 1 && f < 0.12 ? B.SEAGRASS : B.AIR;
            if (bi == "SWAMP" || bi == "MANGROVE_SWAMP") return y == floor + 1 && f < 0.1 ? B.SEAGRASS : B.AIR;
            return B.AIR;
        }
        bool leafShapeContains(int cx, int cy, int cz, int r, bool ragged, int x, int y, int z)
        {
            int dx = Math.Abs(x - cx), dy = y - cy, dz = Math.Abs(z - cz);
            if (dy != 0 || dx > r || dz > r) return false;
            if (r >= 2 && dx == r && dz == r && (!ragged || hash3Main(x, cy, z) < 0.6)) return false;
            if (r >= 3 && dx + dz > r + 1) return false;
            return true;
        }
        static readonly int[] DIR4X = { 1, -1, 0, 0 }, DIR4Z = { 0, 0, 1, -1 }, FANCY_BX = { 2, -2, 0, 0 }, FANCY_BZ = { 0, 0, 2, -2 };
        int featureVoxelAt(int ax, int az, int h, FeatureInfo f, int x, int y, int z)
        {
            var k = f.kind;
            if (k == "cactus") return x == ax && z == az && y >= h + 1 && y <= h + f.height ? B.CACTUS : B.AIR;
            if (k == "ice_spike")
            {
                if (x == ax && z == az && y >= h + 1 && y <= h + f.height) return B.PACKED_ICE;
                if (f.height > 5 && y == h + 1 && ((x == ax + 1 && z == az) || (x == ax && z == az + 1))) return B.PACKED_ICE;
                return B.AIR;
            }
            if (k == "giant_red_mushroom")
            {
                if (x == ax && z == az && y >= h + 1 && y <= h + f.height) return B.MUSHROOM_STEM;
                if (leafShapeContains(ax, h + f.height + 1, az, 1, false, x, y, z) || leafShapeContains(ax, h + f.height, az, 2, true, x, y, z)) return B.RED_MUSHROOM_BLOCK;
                return B.AIR;
            }
            if (k == "giant_brown_mushroom")
            {
                if (x == ax && z == az && y >= h + 1 && y <= h + f.height) return B.MUSHROOM_STEM;
                if (leafShapeContains(ax, h + f.height + 1, az, 3, false, x, y, z)) return B.BROWN_MUSHROOM_BLOCK;
                return B.AIR;
            }
            if (k == "spruce" || k == "tall_spruce")
            {
                if (x == ax && z == az && y >= h + 1 && y <= h + f.height) return B.SPRUCE_LOG;
                int top = h + f.height;
                if (x == ax && z == az && y == top + 1) return B.SPRUCE_LEAVES;
                int layers = k == "tall_spruce" ? 8 : 5, C = top - y;
                if (C >= 0 && C < layers && y > h + 2)
                {
                    int r = C == 0 || C % 2 == 1 ? 1 : 2;
                    if (leafShapeContains(ax, y, az, r, false, x, y, z)) return B.SPRUCE_LEAVES;
                }
                return B.AIR;
            }
            if (k == "dark_oak")
            {
                if (y >= h + 1 && y <= h + f.height && x >= ax && x <= ax + 1 && z >= az && z <= az + 1) return B.DARK_LOG;
                int top = h + f.height, C = y - top;
                if (C >= -2 && C <= 1 && leafShapeContains(ax, y, az, C <= 0 ? 3 : 2, true, x, y, z)) return B.DARK_LEAVES;
                return B.AIR;
            }
            if (k == "jungle")
            {
                if (x == ax && z == az && y >= h + 1 && y <= h + f.height) return B.JUNGLE_LOG;
                int top = h + f.height;
                if (leafShapeContains(ax, top + 1, az, 1, false, x, y, z) || leafShapeContains(ax, top, az, 2, true, x, y, z) || leafShapeContains(ax, top - 1, az, 2, true, x, y, z)) return B.JUNGLE_LEAVES;
                return B.AIR;
            }
            if (k == "acacia")
            {
                int dir = (int)Math.Floor(hash2Main(ax - 5, az + 5) * 4), sx = DIR4X[dir], sz = DIR4Z[dir], tx = ax, tz = az;
                for (int yy = h + 1; yy <= h + f.height; yy++)
                {
                    if (yy > h + 2 && (yy - h) % 2 == 1) { tx += sx; tz += sz; }
                    if (x == tx && z == tz && y == yy) return B.ACACIA_LOG;
                }
                int top = h + f.height;
                if (leafShapeContains(tx, top, tz, 3, false, x, y, z) || leafShapeContains(tx, top + 1, tz, 2, true, x, y, z)) return B.ACACIA_LEAVES;
                return B.AIR;
            }
            if (k == "fancy_oak")
            {
                if (x == ax && z == az && y >= h + 1 && y <= h + f.height) return B.LOG;
                int top = h + f.height; int[] R = { 2, 3, 3, 2, 1 };
                for (int i = 0; i < R.Length; i++) if (leafShapeContains(ax, top - 3 + i, az, R[i], true, x, y, z)) return B.LEAVES;
                int d = (int)Math.Floor(hash2Main(ax, az + 77) * 4), bx = ax + FANCY_BX[d], bz = az + FANCY_BZ[d];
                if (leafShapeContains(bx, top - 3, bz, 1, false, x, y, z)) return B.LEAVES;
                return B.AIR;
            }
            if (k == "cherry")
            {
                if (x == ax && z == az && y >= h + 1 && y <= h + f.height) return B.LOG;
                int top = h + f.height;
                if (leafShapeContains(ax, top + 1, az, 1, false, x, y, z)) return B.FLOWERING_AZALEA_LEAVES;
                int[] R = { 2, 3, 2 };
                for (int i = 0; i < R.Length; i++)
                {
                    int yy = top - i;
                    if (yy <= h + 1) break;
                    if (leafShapeContains(ax, yy, az, R[i], true, x, y, z)) return hash3Main(ax, yy, az) < 0.25 ? B.AZALEA_LEAVES : B.FLOWERING_AZALEA_LEAVES;
                }
                return B.AIR;
            }
            {
                int log = k == "birch" ? B.BIRCH_LOG : B.LOG, leaves = k == "birch" ? B.BIRCH_LEAVES : B.LEAVES;
                if (x == ax && z == az && y >= h + 1 && y <= h + f.height) return log;
                int top = h + f.height;
                for (int d = -2; d <= 1; d++)
                {
                    int yy = top + d;
                    if (yy > h && yy < WORLD_MAX_Y && leafShapeContains(ax, yy, az, d <= -1 ? 2 : 1, true, x, y, z)) return leaves;
                }
                return B.AIR;
            }
        }
        static readonly bool[] FEATURE_SOFT_MASK = MakeMask(B.LEAVES, B.BIRCH_LEAVES, B.SPRUCE_LEAVES, B.DARK_LEAVES, B.JUNGLE_LEAVES, B.ACACIA_LEAVES, B.AZALEA_LEAVES, B.FLOWERING_AZALEA_LEAVES, B.RED_MUSHROOM_BLOCK, B.BROWN_MUSHROOM_BLOCK);
        bool preFeatureReplaceableAt(int x, int y, int z)
        {
            var ci = columnInfo(x, z); int h = generatedSurfaceAt(x, z); var bi = ci.biome;
            if (y <= h) return false;
            if (h < SEA)
            {
                if (y <= SEA) return false;
                if ((bi == "SWAMP" || bi == "MANGROVE_SWAMP") && y == SEA + 1 && hash2Main(x * 7 - 9, z * 7 + 19) < 0.05) return false;
                return true;
            }
            int ground = surfaceBlockForBiome(bi, x, z, h, ci.origH);
            if (y == h + 1)
            {
                int crop = rareCropAt(x, z, h, ground);
                if (crop != 0) return LEAF_REPLACEABLE[crop];
                var p = landPlantInfoAt(x, z, h, ground);
                return p == null || LEAF_REPLACEABLE[p.id];
            }
            if (y > h + 1)
            {
                var p = landPlantInfoAt(x, z, h, ground);
                if (p != null && y <= h + p.height) { int id = p.top != 0 && y == h + 2 ? p.top : p.id; return LEAF_REPLACEABLE[id]; }
                return true;
            }
            return false;
        }
        int generatedFeatureVoxelAt(int x, int y, int z)
        {
            if (y <= SEA) return B.AIR;
            int o = B.AIR; bool softLocked = false; int underReplaceable = -1;
            for (int ax = x - 5; ax <= x + 5; ax++)
                for (int az = z - 5; az <= z + 5; az++)
                {
                    var fp = featurePlacementAt(ax, az);
                    if (fp == null) continue;
                    int v = featureVoxelAt(ax, az, fp.h, fp.f, x, y, z);
                    if (v == 0) continue;
                    if (v < 256 && FEATURE_SOFT_MASK[v])
                    {
                        if (o != 0 || softLocked) continue;
                        if (underReplaceable < 0) underReplaceable = preFeatureReplaceableAt(x, y, z) ? 1 : 0;
                        if (underReplaceable == 0) continue;
                        o = v; softLocked = true;
                    }
                    else { o = v; softLocked = true; }
                }
            return o;
        }
        int generatedLandDecorationAt(int x, int y, int z, ColumnInfo ci, int surface)
        {
            if (surface < SEA || y <= surface) return B.AIR;
            int ground = surfaceBlockForBiome(ci.biome, x, z, surface, ci.origH);
            if (y == surface + 1)
            {
                int crop = rareCropAt(x, z, surface, ground);
                if (crop != 0) return crop;
                var p0 = landPlantInfoAt(x, z, surface, ground);
                return p0 != null ? p0.id : B.AIR;
            }
            var p = landPlantInfoAt(x, z, surface, ground);
            if (p != null && y <= surface + p.height) { if (p.top != 0 && y == surface + 2) return p.top; return p.id; }
            return B.AIR;
        }
        int baseBlockRaw(int x, int y, int z)
        {
            var ci = columnInfo(x, z); var bi = ci.biome; int surface = generatedSurfaceAt(x, z);
            bool wet = bi == "OCEAN" || bi == "COLD_OCEAN" || bi == "LUKEWARM_OCEAN" || bi == "WARM_OCEAN" || bi == "FROZEN_OCEAN" || bi == "RIVER" || bi == "FROZEN_RIVER" || bi == "SWAMP" || bi == "MANGROVE_SWAMP";
            if (y > surface && y > SEA)
            {
                int feature = generatedFeatureVoxelAt(x, y, z);
                if (feature != 0) return feature;
                int plant = generatedLandDecorationAt(x, y, z, ci, surface);
                if (plant != 0) return plant;
            }
            if (wet && surface < SEA)
            {
                int ice = frozenOceanVoxelAt(x, y, z, surface, bi);
                if (ice != 0) return ice;
                if (y == surface) return surfaceBlockForBiome(bi, x, z, surface, ci.origH);
                if (y > surface && y <= SEA) { int p = aquaPlantAt(x, y, z, surface); return p != 0 ? p : B.WATER; }
                if (y > SEA)
                {
                    if ((bi == "SWAMP" || bi == "MANGROVE_SWAMP") && y == SEA + 1 && hash2Main(x * 7 - 9, z * 7 + 19) < 0.05) return B.LILY_PAD;
                    return B.AIR;
                }
            }
            int id = terrainBlock(x, y, z);
            if (id == B.WATER) { int p = aquaPlantAt(x, y, z, surface); return p != 0 ? p : id; }
            return id;
        }
        public int baseBlock(int x, int y, int z)
        {
            int slot = hotSlot3(x, y, z, BASE_BLOCK_HOT_MASK);
            if (baseBlockHotValid[slot] && baseBlockHotX[slot] == x && baseBlockHotY[slot] == y && baseBlockHotZ[slot] == z) return baseBlockHotValue[slot];
            int v = baseBlockRaw(x, y, z);
            baseBlockHotX[slot] = x; baseBlockHotY[slot] = (short)y; baseBlockHotZ[slot] = z; baseBlockHotValue[slot] = (byte)v; baseBlockHotValid[slot] = true;
            return v;
        }
        static void put(byte[] data, int cx, int cz, int x, int y, int z, int id, bool waterOnly = false)
        {
            if (y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return;
            int lx = x - cx * CHUNK, lz = z - cz * CHUNK;
            if (lx < 0 || lx >= 16 || lz < 0 || lz >= 16) return;
            int i = idx(lx, y, lz);
            if (waterOnly ? data[i] == B.WATER : data[i] == B.AIR) data[i] = (byte)id;
        }
    }
}
