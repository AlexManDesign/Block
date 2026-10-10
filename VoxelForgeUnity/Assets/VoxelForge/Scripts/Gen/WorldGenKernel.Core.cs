// Voxel Forge — Unity port. Reconstructed world generator kernel (thread-confined instance, one per generator thread).
// Architecture preserved from the reference engine: 16x16 chunks, -64..320 world, main-compatible climate + biome
// decorators, branch-pruned selection, 4x4x8 density lattice, deterministic caves/fluids/ores and cell based features.
// All integer/double arithmetic mirrors JS semantics exactly (ToInt32 of doubles, Math.imul, >>>) for seed parity.
using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxelForge
{
    public sealed class ColumnInfo
    {
        public double origH; public string biome; public double temp, moist, continental, erosion, weird, peak, detail, factor, mAmp;
    }
    public struct SurfaceInfo { public int y, ground, below; }
    public sealed class FeatureInfo { public string kind; public int height, log, leaves; }
    public sealed class LandPlant { public int id, height, top; }
    public sealed class WgMeta { public int cx, cz; public List<int> sea = new List<int>(), cave = new List<int>(); public bool dirty, seaSorted, caveSorted; }
    public sealed class DenseChunk { public byte[] data; public WgMeta meta; }

    /// <summary>Immutable shared generator data (climate tree, structure templates). Built once.</summary>
    public static class GenData
    {
        public sealed class ClimateIndex
        {
            public int root; public double[] bounds; public int[] childStart; public byte[] childCount; public int[] children; public short[] biome; public double[] zero4, zero6;
        }
        public sealed class ShipTemplate
        {
            public string name; public int sx, sy, sz, axo, azo; public bool beachable;
            public string[] palKey; public int[] palMeta, palTransform; public int[] blocks;
            public List<int[]> chestPos = new List<int[]>(); public List<string> chestKind = new List<string>();
            public int[] helm, mount; public HashSet<string> cells;
        }
        public sealed class PyramidEntry { public int x, y, z, meta; public string key; }
        public static ClimateIndex climate;
        public static ShipTemplate[] ships;
        public static PyramidEntry[] pyramid;
        public static int[] outpost;
        static readonly object initLock = new object();
        static bool inited;

        public static void Ensure()
        {
            if (inited) return;
            lock (initLock)
            {
                if (inited) return;
                var climateBytes = UnityEngine.Resources.Load<UnityEngine.TextAsset>("VoxelForge/main_climate").bytes;
                var shipsJson = UnityEngine.Resources.Load<UnityEngine.TextAsset>("VoxelForge/main_ships").text;
                var pyrJson = UnityEngine.Resources.Load<UnityEngine.TextAsset>("VoxelForge/main_pyramid_blocks").text;
                var outJson = UnityEngine.Resources.Load<UnityEngine.TextAsset>("VoxelForge/main_outpost_blocks").text;
                Build(climateBytes, shipsJson, pyrJson, outJson);
                inited = true;
            }
        }
        /// <summary>Must be called from the main thread first (Resources API); generator threads then only read.</summary>
        public static void Build(byte[] climateBytes, string shipsJson, string pyrJson, string outJson)
        {
            climate = WorldGenKernel.BuildClimateIndex(climateBytes);
            var sl = (List<object>)Json.Parse(shipsJson);
            ships = new ShipTemplate[sl.Count];
            for (int i = 0; i < sl.Count; i++)
            {
                var o = (JObj)sl[i];
                var t = new ShipTemplate { name = o.Str("name"), sx = o.Int("sx"), sy = o.Int("sy"), sz = o.Int("sz"), axo = o.Int("axo"), azo = o.Int("azo"), beachable = o.Bool("beachable") };
                var pal = o.Arr("pal");
                t.palKey = new string[pal.Count]; t.palMeta = new int[pal.Count]; t.palTransform = new int[pal.Count];
                for (int k = 0; k < pal.Count; k++) { var p = (List<object>)pal[k]; t.palKey[k] = (string)p[0]; t.palMeta[k] = (int)Json.ToNum(p[1]); t.palTransform[k] = (int)Json.ToNum(p[2]); }
                var bl = o.Arr("blocks"); t.blocks = new int[bl.Count];
                for (int k = 0; k < bl.Count; k++) t.blocks[k] = (int)Json.ToNum(bl[k]);
                var ch = o.Arr("chests");
                if (ch != null) foreach (var co in ch) { var c = (List<object>)co; t.chestPos.Add(new[] { (int)Json.ToNum(c[0]), (int)Json.ToNum(c[1]), (int)Json.ToNum(c[2]) }); t.chestKind.Add((string)c[3]); }
                // precompute helm / cells / mount (deterministic, shared)
                int[] best = null; double score = -1e9;
                for (int k = 0; k < t.blocks.Length; k += 4)
                {
                    int x = t.blocks[k], y = t.blocks[k + 1], z = t.blocks[k + 2];
                    if (y > 8) continue;
                    double q = y * 100 + (z - t.azo) * 4 - Math.Abs(x - t.axo) * 3;
                    if (q > score) { score = q; best = new[] { x, y, z }; }
                }
                t.helm = best ?? new[] { t.axo, 0, t.azo };
                t.cells = new HashSet<string>();
                for (int k = 0; k < t.blocks.Length; k += 4) t.cells.Add(t.blocks[k] + "," + t.blocks[k + 1] + "," + t.blocks[k + 2]);
                t.mount = null;
                for (int d = 6; d >= 0; d--)
                {
                    int z = t.helm[2] - d;
                    if (t.cells.Contains(t.helm[0] + "," + t.helm[1] + "," + z) && !t.cells.Contains(t.helm[0] + "," + (t.helm[1] + 1) + "," + z)) { t.mount = new[] { t.helm[0], t.helm[1], z }; break; }
                }
                if (t.mount == null) t.mount = new[] { t.helm[0], t.helm[1] + 1, t.helm[2] };
                ships[i] = t;
            }
            var pl = (List<object>)Json.Parse(pyrJson);
            pyramid = new PyramidEntry[pl.Count];
            for (int i = 0; i < pl.Count; i++) { var e = (List<object>)pl[i]; pyramid[i] = new PyramidEntry { x = (int)Json.ToNum(e[0]), y = (int)Json.ToNum(e[1]), z = (int)Json.ToNum(e[2]), key = (string)e[3], meta = (int)Json.ToNum(e[4]) }; }
            var ol = (List<object>)Json.Parse(outJson);
            outpost = new int[ol.Count];
            for (int i = 0; i < ol.Count; i++) outpost[i] = (int)Json.ToNum(ol[i]);
        }
    }

    public sealed partial class WorldGenKernel
    {
        const int CHUNK = 16, WORLD_MIN_Y = -64, WORLD_MAX_Y = 320, WORLD_H = 384, SEA = 63;
        const int OCEAN_FLOOD_MIN_Y = WORLD_MIN_Y + 1, OCEAN_FLOOD_TOP = SEA - 1, OCEAN_FLOOD_CAP = CHUNK * CHUNK * (OCEAN_FLOOD_TOP - OCEAN_FLOOD_MIN_Y + 1);
        readonly int[] oceanFloodStack = new int[OCEAN_FLOOD_CAP];
        int seed;
        List<byte[]> perms = new List<byte[]>();
        // JS keys these caches with `${seed}:${x},${z}` strings; setSeed clears them on every seed change, so an allocation-free
        // (x,z) long key is equivalent (lookups only, never iterated). The comparer mixes both halves (long.GetHashCode is lo^hi).
        sealed class XZKeyCmp : IEqualityComparer<long>
        {
            public static readonly XZKeyCmp I = new XZKeyCmp();
            public bool Equals(long a, long b) { return a == b; }
            public int GetHashCode(long k) { return unchecked((int)k * -1640531535 ^ (int)(k >> 32) * 668265261); }
        }
        static long xzKey(int x, int z) { return ((long)x << 32) | (uint)z; }
        readonly Dictionary<long, RavinePlan> ravinePlanCache = new Dictionary<long, RavinePlan>(XZKeyCmp.I);
        readonly Dictionary<long, List<RavinePlan>> ravineChunkCache = new Dictionary<long, List<RavinePlan>>(XZKeyCmp.I);
        readonly Dictionary<long, MinePlan> mineshaftPlanCache = new Dictionary<long, MinePlan>(XZKeyCmp.I);
        readonly Dictionary<long, List<MinePlan>> mineshaftChunkCache = new Dictionary<long, List<MinePlan>>(XZKeyCmp.I);

        // Fixed direct caches; every hit verifies x/z, so collisions only evict.
        const int COLUMN_HOT_SIZE = 65536, COLUMN_HOT_MASK = COLUMN_HOT_SIZE - 1;
        readonly int[] columnHotX = new int[COLUMN_HOT_SIZE], columnHotZ = new int[COLUMN_HOT_SIZE]; readonly bool[] columnHotValid = new bool[COLUMN_HOT_SIZE]; readonly ColumnInfo[] columnHotValue = new ColumnInfo[COLUMN_HOT_SIZE];
        const int HEIGHT_HOT_SIZE = 65536, HEIGHT_HOT_MASK = HEIGHT_HOT_SIZE - 1;
        readonly int[] heightHotX = new int[HEIGHT_HOT_SIZE], heightHotZ = new int[HEIGHT_HOT_SIZE]; readonly bool[] heightHotValid = new bool[HEIGHT_HOT_SIZE]; readonly short[] heightHotValue = new short[HEIGHT_HOT_SIZE];
        const int SURFACE_HOT_SIZE = 32768, SURFACE_HOT_MASK = SURFACE_HOT_SIZE - 1;
        readonly int[] surfaceHotX = new int[SURFACE_HOT_SIZE], surfaceHotZ = new int[SURFACE_HOT_SIZE]; readonly bool[] surfaceHotValid = new bool[SURFACE_HOT_SIZE]; readonly short[] surfaceHotValue = new short[SURFACE_HOT_SIZE];
        const int FEATURE_SURFACE_HOT_SIZE = 32768, FEATURE_SURFACE_HOT_MASK = FEATURE_SURFACE_HOT_SIZE - 1;
        readonly int[] featureSurfaceHotX = new int[FEATURE_SURFACE_HOT_SIZE], featureSurfaceHotZ = new int[FEATURE_SURFACE_HOT_SIZE]; readonly bool[] featureSurfaceHotValid = new bool[FEATURE_SURFACE_HOT_SIZE]; readonly SurfaceInfo[] featureSurfaceHotValue = new SurfaceInfo[FEATURE_SURFACE_HOT_SIZE];
        const int FEATURE_KIND_HOT_SIZE = 32768, FEATURE_KIND_HOT_MASK = FEATURE_KIND_HOT_SIZE - 1;
        readonly int[] featureKindHotX = new int[FEATURE_KIND_HOT_SIZE], featureKindHotZ = new int[FEATURE_KIND_HOT_SIZE]; readonly bool[] featureKindHotValid = new bool[FEATURE_KIND_HOT_SIZE]; readonly string[] featureKindHotValue = new string[FEATURE_KIND_HOT_SIZE];
        const int BASE_BLOCK_HOT_SIZE = 65536, BASE_BLOCK_HOT_MASK = BASE_BLOCK_HOT_SIZE - 1;
        readonly int[] baseBlockHotX = new int[BASE_BLOCK_HOT_SIZE], baseBlockHotZ = new int[BASE_BLOCK_HOT_SIZE]; readonly short[] baseBlockHotY = new short[BASE_BLOCK_HOT_SIZE]; readonly bool[] baseBlockHotValid = new bool[BASE_BLOCK_HOT_SIZE]; readonly byte[] baseBlockHotValue = new byte[BASE_BLOCK_HOT_SIZE];
        const int WEIRD_HOT_SIZE = 262144, WEIRD_HOT_MASK = WEIRD_HOT_SIZE - 1;
        readonly int[] weirdHotX = new int[WEIRD_HOT_SIZE], weirdHotZ = new int[WEIRD_HOT_SIZE]; readonly double[] weirdHotV = new double[WEIRD_HOT_SIZE]; readonly bool[] weirdHotValid = new bool[WEIRD_HOT_SIZE];

        static int imul(int a, int b) { unchecked { return a * b; } }
        static int ushr(int x, int n) { return (int)((uint)x >> n); }
        static int toInt32(double v) { return JS.toInt32(v); }
        static int hotSlot(int x, int z, int mask) { return (int)((uint)(imul(x, 73856093) ^ imul(z, 19349663)) & (uint)mask); }
        static int hotSlot3(int x, int y, int z, int mask) { return (int)((uint)(imul(x, 73856093) ^ imul(y, 83492791) ^ imul(z, 19349663)) & (uint)mask); }
        static double clamp(double v, double a, double b) { return Math.Max(a, Math.Min(b, v)); }
        static double lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double fade(double t) { return t * t * t * (t * (t * 6 - 15) + 10); }
        static int floorDiv(int v, int d) { return (int)Math.Floor(v / (double)d); }
        static int ffloor(double v) { return (int)Math.Floor(v); }
        static double round(double v) { return JsMath.round(v); }
        static uint mix32(int v)
        {
            v ^= ushr(v, 16); v = imul(v, 0x7feb352d); v ^= ushr(v, 15); v = imul(v, unchecked((int)0x846ca68b)); v ^= ushr(v, 16);
            return (uint)v;
        }
        double hash3i(int x, int y, int z, int salt = 0) { return mix32(imul(x, 0x1f123bb5) ^ imul(y, 0x05491333) ^ imul(z, 0x6c8e9cf5) ^ seed ^ salt) / 4294967296.0; }
        static Func<double> rng32(int s)
        {
            int x = s != 0 ? s : 1;
            return () =>
            {
                x = unchecked(x + 0x6d2b79f5);
                int t = x;
                t = imul(t ^ ushr(t, 15), t | 1);
                t ^= unchecked(t + imul(t ^ ushr(t, 7), t | 61));
                return (uint)(t ^ ushr(t, 14)) / 4294967296.0;
            };
        }
        byte[] makePerm(int salt)
        {
            var r = rng32(seed ^ salt); var p = new byte[256];
            for (int i = 0; i < 256; i++) p[i] = (byte)i;
            for (int i = 255; i > 0; i--) { int j = (int)(r() * (i + 1)); byte t = p[i]; p[i] = p[j]; p[j] = t; }
            var q = new byte[512];
            for (int i = 0; i < 512; i++) q[i] = p[i & 255];
            return q;
        }
        static readonly double[][] G2 = { new[] { 1.0, 0 }, new[] { -1.0, 0 }, new[] { 0.0, 1 }, new[] { 0.0, -1 }, new[] { 0.707, 0.707 }, new[] { -0.707, 0.707 }, new[] { 0.707, -0.707 }, new[] { -0.707, -0.707 } };
        static readonly double[][] G3 = { new[] { 1.0, 1, 0 }, new[] { -1.0, 1, 0 }, new[] { 1.0, -1, 0 }, new[] { -1.0, -1, 0 }, new[] { 1.0, 0, 1 }, new[] { -1.0, 0, 1 }, new[] { 1.0, 0, -1 }, new[] { -1.0, 0, -1 }, new[] { 0.0, 1, 1 }, new[] { 0.0, -1, 1 }, new[] { 0.0, 1, -1 }, new[] { 0.0, -1, -1 } };
        void ensurePerms()
        {
            if (perms.Count > 0) return;
            for (int i = 0; i < 12; i++) perms.Add(makePerm(unchecked((int)0x9e3779b9) ^ (i * 0x45d9f3b)));
        }
        static double perlin2(byte[] p, double x, double z)
        {
            int ix = ffloor(x), iz = ffloor(z); double fx = x - ix, fz = z - iz, u = fade(fx), v = fade(fz); int X = ix & 255, Z = iz & 255;
            Func<int, int, double> g = (dx, dz) => { int a = p[p[(X + dx) & 255] + ((Z + dz) & 255)] & 7; var d = G2[a]; return d[0] * (fx - dx) + d[1] * (fz - dz); };
            return lerp(lerp(g(0, 0), g(1, 0), u), lerp(g(0, 1), g(1, 1), u), v);
        }
        static double perlin3(byte[] p, double x, double y, double z)
        {
            int ix = ffloor(x), iy = ffloor(y), iz = ffloor(z); double fx = x - ix, fy = y - iy, fz = z - iz, u = fade(fx), v = fade(fy), w = fade(fz);
            int X = ix & 255, Y = iy & 255, Z = iz & 255;
            Func<int, int, int, double> g = (dx, dy, dz) => { int h = p[p[p[(X + dx) & 255] + ((Y + dy) & 255)] + ((Z + dz) & 255)] % 12; var d = G3[h]; return d[0] * (fx - dx) + d[1] * (fy - dy) + d[2] * (fz - dz); };
            double x00 = lerp(g(0, 0, 0), g(1, 0, 0), u), x10 = lerp(g(0, 1, 0), g(1, 1, 0), u), x01 = lerp(g(0, 0, 1), g(1, 0, 1), u), x11 = lerp(g(0, 1, 1), g(1, 1, 1), u);
            return lerp(lerp(x00, x10, v), lerp(x01, x11, v), w);
        }
        public double fbm2(int pi, double x, double z, double scale, int oct = 4, double lac = 2.02, double gain = 0.5)
        {
            ensurePerms();
            double a = 1, f = scale, sum = 0, n = 0;
            for (int i = 0; i < oct; i++) { sum += perlin2(perms[pi], x * f + i * 31.7, z * f - i * 17.9) * a; n += a; a *= gain; f *= lac; }
            return sum / n;
        }
        public double fbm3(int pi, double x, double y, double z, double scale, int oct = 3, double lac = 2.03, double gain = 0.5)
        {
            ensurePerms();
            double a = 1, f = scale, sum = 0, n = 0;
            for (int i = 0; i < oct; i++) { sum += perlin3(perms[pi], x * f + i * 13.1, y * f - i * 27.4, z * f + i * 41.3) * a; n += a; a *= gain; f *= lac; }
            return sum / n;
        }
        static double ridge(double v) { v = 1 - Math.Abs(v); return v * v; }

        public WorldGenKernel(int initialSeed)
        {
            GenData.Ensure();
            seed = initialSeed;
            climateIndex = GenData.climate;
        }
        public int Seed { get { return seed; } }
        public void setSeed(int v)
        {
            if (v == seed && perms.Count > 0 && Array.Exists(mainClimatePerms, q => q != null)) return;
            seed = v;
            perms = new List<byte[]>();
            Array.Clear(mainClimatePerms, 0, mainClimatePerms.Length);
            climateLastLeaf = -1;
            Array.Clear(columnHotValid, 0, columnHotValid.Length); Array.Clear(columnHotValue, 0, columnHotValue.Length);
            Array.Clear(weirdHotValid, 0, weirdHotValid.Length);
            Array.Clear(heightHotValid, 0, heightHotValid.Length);
            Array.Clear(surfaceHotValid, 0, surfaceHotValid.Length);
            Array.Clear(featureSurfaceHotValid, 0, featureSurfaceHotValid.Length);
            Array.Clear(featureKindHotValid, 0, featureKindHotValid.Length); Array.Clear(featureKindHotValue, 0, featureKindHotValue.Length);
            Array.Clear(baseBlockHotValid, 0, baseBlockHotValid.Length);
            ravinePlanCache.Clear(); ravineChunkCache.Clear(); mineshaftPlanCache.Clear(); mineshaftChunkCache.Clear();
            foreach (var sc in structureCache) sc.Clear();
        }

        // ---------------- main-compatible biome climate layer ----------------
        static readonly string[] MAIN_BIOME_NAMES = {
            "BADLANDS","BAMBOO_JUNGLE","BEACH","BIRCH_FOREST","CHERRY_GROVE","COLD_OCEAN","DARK_FOREST","COLD_OCEAN","STONY_PEAKS","FROZEN_OCEAN","LUKEWARM_OCEAN",
            "OCEAN","DESERT","STONY_PEAKS","BADLANDS","FLOWER_FOREST","FOREST","FROZEN_OCEAN","FROZEN_PEAKS","FROZEN_RIVER","GROVE","ICE_SPIKES","JAGGED_PEAKS","JUNGLE",
            "LUKEWARM_OCEAN","FOREST","MANGROVE_SWAMP","MEADOW","MUSHROOM_FIELDS","OCEAN","BIRCH_FOREST","OLD_GROWTH_TAIGA","OLD_GROWTH_TAIGA","PLAINS","RIVER","SAVANNA",
            "SAVANNA","SNOWY_BEACH","SNOWY","SNOWY_SLOPES","SNOWY_TAIGA","SPARSE_JUNGLE","STONY_PEAKS","STONY_SHORE","PLAINS","SWAMP","TAIGA","WARM_OCEAN","WINDSWEPT",
            "WINDSWEPT","WINDSWEPT","SAVANNA","WOODED_BADLANDS" };
        const int MAIN_CLIMATE_DIMS = 7, MAIN_CLIMATE_LEAF_MAX = 10;
        readonly byte[][] mainClimatePerms = new byte[16][];
        int climateLastLeaf = -1;
        static readonly double[][] MAIN_G2 = { new[] { 1.0, 1 }, new[] { -1.0, 1 }, new[] { 1.0, -1 }, new[] { -1.0, -1 }, new[] { 1.0, 0 }, new[] { -1.0, 0 }, new[] { 0.0, 1 }, new[] { 0.0, -1 } };
        static Func<double> mainRng32(int s)
        {
            int x = s;
            return () =>
            {
                x = unchecked(x + 0x6d2b79f5);
                int t = x;
                t = imul(t ^ ushr(t, 15), t | 1);
                t ^= unchecked(t + imul(t ^ ushr(t, 7), t | 61));
                return (uint)(t ^ ushr(t, 14)) / 4294967296.0;
            };
        }
        byte[] makeMainPerm(int channel)
        {
            var r = mainRng32(seed ^ imul(channel, unchecked((int)0x9e3779b1))); var p = new byte[256];
            for (int i = 0; i < 256; i++) p[i] = (byte)i;
            for (int i = 255; i > 0; i--) { int j = (int)Math.Floor(r() * (i + 1)); byte q = p[i]; p[i] = p[j]; p[j] = q; }
            var o = new byte[512];
            for (int i = 0; i < 512; i++) o[i] = p[i & 255];
            return o;
        }
        byte[] mainPerm(int channel)
        {
            var p = mainClimatePerms[channel];
            if (p == null) mainClimatePerms[channel] = p = makeMainPerm(channel);
            return p;
        }
        static double mainPerlin2(byte[] p, double x, double z)
        {
            int ix = ffloor(x), iz = ffloor(z); double fx = x - ix, fz = z - iz, u = fade(fx), v = fade(fz); int X = ix & 255, Z = iz & 255;
            int h00 = p[p[X] + Z] & 7, h10 = p[p[X + 1] + Z] & 7, h01 = p[p[X] + Z + 1] & 7, h11 = p[p[X + 1] + Z + 1] & 7;
            double[] g00 = MAIN_G2[h00], g10 = MAIN_G2[h10], g01 = MAIN_G2[h01], g11 = MAIN_G2[h11];
            double a0 = g00[0] * fx + g00[1] * fz, a1 = g10[0] * (fx - 1) + g10[1] * fz, b0 = g01[0] * fx + g01[1] * (fz - 1), b1 = g11[0] * (fx - 1) + g11[1] * (fz - 1);
            return lerp(lerp(a0, a1, u), lerp(b0, b1, u), v);
        }
        double mainFbm2(int channel, double x, double z, double scale, int oct)
        {
            var p = mainPerm(channel);
            double sum = 0, amp = 1, f = scale, norm = 0;
            for (int i = 0; i < oct; i++) { sum += mainPerlin2(p, x * f + i * 131.7, z * f - i * 57.3) * amp; norm += amp; amp *= 0.5; f *= 2; }
            return sum / norm;
        }
        /// <summary>hash2Main: world seed applied before ToInt32; initial multiply/add in double arithmetic, then 32-bit avalanche.</summary>
        public double hash2Main(double x, double z)
        {
            int xi = toInt32(x + seed), zi = toInt32(z - seed);
            int n = toInt32((double)xi * 374761393.0 + (double)zi * 668265263.0 + 527595518.0);
            n = n ^ ushr(n, 13);
            n = imul(n, 1274126177);
            return (uint)(n ^ ushr(n, 16)) / 4294967296.0;
        }
        double valueNoise2Main(double x, double z)
        {
            int ix = ffloor(x), iz = ffloor(z); double fx = x - ix, fz = z - iz, u = fx * fx * (3 - 2 * fx), v = fz * fz * (3 - 2 * fz),
                a = hash2Main(ix, iz), b = hash2Main(ix + 1, iz), c = hash2Main(ix, iz + 1), d = hash2Main(ix + 1, iz + 1);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }
        static readonly double[][] MAIN_ROT = { new[] { 1.0, 0 }, new[] { 0.866, 0.5 }, new[] { 0.5, 0.866 }, new[] { 0.0, 1 } };
        double valueFbm4Main(double x, double z)
        {
            double sum = 0, amp = 0.5, f = 1, norm = 0;
            for (int i = 0; i < 4; i++)
            {
                var q = MAIN_ROT[i]; double rx = x * q[0] - z * q[1], rz = x * q[1] + z * q[0];
                sum += valueNoise2Main(rx * f + i * 119.7, rz * f - i * 53.3) * amp; norm += amp; amp *= 0.5; f *= 2.17;
            }
            return sum / norm;
        }
        static double curveSample(double[][] points, double x)
        {
            if (x <= points[0][0]) return points[0][1];
            for (int i = 1; i < points.Length; i++)
                if (x <= points[i][0]) { var a = points[i - 1]; var b = points[i]; double t = (x - a[0]) / (b[0] - a[0]); return lerp(a[1], b[1], t); }
            return points[points.Length - 1][1];
        }
        static readonly double[][] MAIN_CONT_TO_CLIMATE = { new[] { -1.0, -1 }, new[] { -0.325, -0.8 }, new[] { -0.11, -0.19 }, new[] { -0.077, -0.13 }, new[] { -0.04, -0.06 }, new[] { 0.05, 0.05 }, new[] { 0.37, 0.25 }, new[] { 0.8, 0.7 }, new[] { 1.2, 1 } };

        // --- climate tree construction (static, shared) ---
        sealed class CNode { public double[] bounds; public int biome; public List<CNode> children; }
        static double climateMid(double[] b, int axis) { return (b[axis * 2] + b[axis * 2 + 1]) * 0.5; }
        static double[] unionClimateBounds(List<CNode> items)
        {
            var b = new double[14];
            for (int k = 0; k < 7; k++) { b[k * 2] = double.PositiveInfinity; b[k * 2 + 1] = double.NegativeInfinity; }
            foreach (var it in items)
                for (int k = 0; k < 7; k++) { double lo = it.bounds[k * 2], hi = it.bounds[k * 2 + 1]; if (lo < b[k * 2]) b[k * 2] = lo; if (hi > b[k * 2 + 1]) b[k * 2 + 1] = hi; }
            return b;
        }
        static double climateWidth(double[] b) { double s = 0; for (int k = 0; k < 7; k++) s += Math.Abs(b[k * 2 + 1] - b[k * 2]); return s; }
        static double climateCenterScore(CNode it) { double s = 0; for (int k = 0; k < 7; k++) s += Math.Abs(climateMid(it.bounds, k)); return s; }
        static List<CNode> climateGroups(List<CNode> items, int axis)
        {
            var sorted = items.OrderBy(a => climateMid(a.bounds, axis)).ToList(); // stable, like V8 TimSort
            int groupSize = (int)JsMath.pow(10, Math.Floor(JsMath.log10(items.Count - 0.01)));
            var o = new List<CNode>();
            for (int i = 0; i < sorted.Count; i += groupSize)
            {
                var children = sorted.GetRange(i, Math.Min(groupSize, sorted.Count - i));
                o.Add(new CNode { bounds = unionClimateBounds(children), children = children });
            }
            return o;
        }
        static CNode buildMainClimateTree(List<CNode> items)
        {
            if (items.Count == 1) return items[0];
            var bounds = unionClimateBounds(items);
            if (items.Count <= MAIN_CLIMATE_LEAF_MAX)
                return new CNode { bounds = bounds, children = items.OrderBy(climateCenterScore).ToList() };
            int bestAxis = 0; double bestCost = double.PositiveInfinity; List<CNode> bestGroups = null;
            for (int axis = 0; axis < 7; axis++)
            {
                var groups = climateGroups(items, axis); double cost = 0;
                foreach (var g in groups) cost += climateWidth(g.bounds);
                if (cost < bestCost) { bestCost = cost; bestAxis = axis; bestGroups = groups; }
            }
            int ba = bestAxis;
            bestGroups = bestGroups.OrderBy(g => Math.Abs(climateMid(g.bounds, ba))).ToList();
            return new CNode { bounds = bounds, children = bestGroups.Select(g => buildMainClimateTree(g.children)).ToList() };
        }
        public static GenData.ClimateIndex BuildClimateIndex(byte[] bytes)
        {
            int count = bytes.Length / 30; var items = new List<CNode>(count);
            for (int n = 0; n < count; n++)
            {
                int off = n * 30; int srcBiome = BitConverter.ToInt16(bytes, off);
                var bounds = new double[14];
                for (int k = 0; k < 14; k++) bounds[k] = BitConverter.ToInt16(bytes, off + 2 + k * 2) / 10000.0;
                items.Add(new CNode { bounds = bounds, biome = srcBiome, children = null });
            }
            var root = buildMainClimateTree(items);
            // flatten
            var nodes = new List<CNode>(); var links = new List<int[]>();
            Func<CNode, int> visit = null;
            visit = node =>
            {
                int id = nodes.Count; nodes.Add(node); links.Add(null);
                if (node.children != null)
                {
                    var a = new int[node.children.Count]; links[id] = a;
                    for (int i = 0; i < node.children.Count; i++) a[i] = visit(node.children[i]);
                }
                return id;
            };
            int rootId = visit(root), N = nodes.Count;
            var ix = new GenData.ClimateIndex { root = rootId, bounds = new double[N * 14], childStart = new int[N], childCount = new byte[N], biome = new short[N], zero4 = new double[N], zero6 = new double[N] };
            for (int i = 0; i < N; i++) ix.biome[i] = -1;
            int total = 0; for (int i = 0; i < N; i++) if (links[i] != null) total += links[i].Length;
            ix.children = new int[total];
            int pos = 0;
            for (int i = 0; i < N; i++)
            {
                int off = i * 14;
                Array.Copy(nodes[i].bounds, 0, ix.bounds, off, 14);
                double lo = ix.bounds[off + 8], hi = ix.bounds[off + 9], q = 0 < lo ? lo : 0 > hi ? -hi : 0;
                ix.zero4[i] = q * q;
                lo = ix.bounds[off + 12]; hi = ix.bounds[off + 13]; q = 0 < lo ? lo : 0 > hi ? -hi : 0;
                ix.zero6[i] = q * q;
                var a = links[i];
                if (a != null) { ix.childStart[i] = pos; ix.childCount[i] = (byte)a.Length; Array.Copy(a, 0, ix.children, pos, a.Length); pos += a.Length; }
                else ix.biome[i] = (short)nodes[i].biome;
            }
            return ix;
        }
        readonly GenData.ClimateIndex climateIndex;
        readonly double[] mainClimatePointBuf = new double[7];
        readonly int[] climateSearchStack = new int[256];
        readonly double[] climatePointScratch = new double[5], terrainShapeScratch = new double[5];
        double climateRangeDistFlat(int node, double[] p)
        {
            var b = climateIndex.bounds; int off = node * 14;
            double lo = b[off], hi = b[off + 1], v = p[0], q = v < lo ? lo - v : v > hi ? v - hi : 0, d = q * q;
            lo = b[off + 2]; hi = b[off + 3]; v = p[1]; q = v < lo ? lo - v : v > hi ? v - hi : 0; d += q * q;
            lo = b[off + 4]; hi = b[off + 5]; v = p[2]; q = v < lo ? lo - v : v > hi ? v - hi : 0; d += q * q;
            lo = b[off + 6]; hi = b[off + 7]; v = p[3]; q = v < lo ? lo - v : v > hi ? v - hi : 0; d += q * q;
            d += climateIndex.zero4[node];
            lo = b[off + 10]; hi = b[off + 11]; v = p[5]; q = v < lo ? lo - v : v > hi ? v - hi : 0; d += q * q;
            d += climateIndex.zero6[node];
            return d;
        }
        string nearestMainBiome(double t, double h, double c, double e, double w)
        {
            double pv = clamp(w * 3.5, -1, 1);
            if (pv > -0.12 && pv < 0.12) pv = pv < 0 ? -0.12 : 0.12;
            var P = mainClimatePointBuf;
            P[0] = clamp(t, -1, 1); P[1] = clamp(h * 0.82, -1, 1); P[2] = curveSample(MAIN_CONT_TO_CLIMATE, c < 0.05 ? 0.05 : c);
            P[3] = clamp(e * 1.5 + (t > 0.5 ? 0.42 : 0), -1, 1); P[4] = 0; P[5] = pv; P[6] = 0;
            int best = climateLastLeaf; double bestD = best >= 0 ? climateRangeDistFlat(best, P) : double.PositiveInfinity; int sp = 0;
            climateSearchStack[sp++] = climateIndex.root;
            while (sp > 0)
            {
                int node = climateSearchStack[--sp]; double nd = climateRangeDistFlat(node, P);
                if (nd >= bestD) continue;
                int count = climateIndex.childCount[node];
                if (count == 0) { best = node; bestD = nd; continue; }
                int start = climateIndex.childStart[node];
                for (int i = count - 1; i >= 0; i--) climateSearchStack[sp++] = climateIndex.children[start + i];
            }
            climateLastLeaf = best;
            int bi = best >= 0 ? climateIndex.biome[best] : -1;
            return bi >= 0 && bi < MAIN_BIOME_NAMES.Length ? MAIN_BIOME_NAMES[bi] : "PLAINS";
        }
        static int tempBand(double t) { return t < -0.45 ? 0 : t < -0.15 ? 1 : t < 0.2 ? 2 : t < 0.55 ? 3 : 4; }
        static readonly string[] MAIN_OCEANS = { "FROZEN_OCEAN", "COLD_OCEAN", "OCEAN", "LUKEWARM_OCEAN", "WARM_OCEAN" };
        double mushroomStrength(double cont, double x, double z)
        {
            if (cont >= -0.85) return 0;
            double n = valueNoise2Main(x * 0.0011 + 912.3, z * 0.0011 - 377.1);
            if (n <= 0.9) return 0;
            double s = Math.Min(1, (n - 0.9) / 0.1), deep = clamp((-0.85 - cont) / 0.12, 0, 1);
            s = s * s * (3 - 2 * s); deep = deep * deep * (3 - 2 * deep);
            return s * deep;
        }
        string selectMainBiome(double height, double[] p, double x, double z, double mush)
        {
            double t = p[0], h = p[1], cont = p[2], eros = p[3], weird = p[4]; int tb = tempBand(t);
            if (mush > 0.25 && height > SEA) return "MUSHROOM_FIELDS";
            if (height <= SEA)
            {
                if (cont > -0.11 && height > SEA - 5 && Math.Abs(weird) < 0.065) return tb == 0 ? "FROZEN_RIVER" : "RIVER";
                return MAIN_OCEANS[tb];
            }
            if (height <= SEA + 2 && cont < -0.04)
            {
                if (tb == 0) return "SNOWY_BEACH";
                if (tb == 4) return "DESERT";
                if (eros < -0.18) return "STONY_SHORE";
                if (valueNoise2Main(x * 0.011 + 401.7, z * 0.011 - 233.1) > 0.52) return "BEACH";
            }
            return nearestMainBiome(t, h, cont, eros, weird);
        }
        double[] climatePoint(double x, double z)
        {
            double c = mainFbm2(1, x, z, 1 / 2200.0, 4) * 2.3 + 0.24, e = mainFbm2(2, x, z, 1 / 1300.0, 4) * 2.1, w = mainFbm2(3, x, z, 1 / 850.0, 5) * 2.2,
                wx = x + (valueNoise2Main(x * 0.008 + 431.7, z * 0.008 - 271.3) - 0.5) * 16, wz = z + (valueNoise2Main(x * 0.008 - 118.4, z * 0.008 + 542.9) - 0.5) * 16,
                t = clamp(mainFbm2(5, wx, wz, 1 / 1600.0, 3) * 2.4, -1, 1), h = clamp(mainFbm2(6, wx, wz, 1 / 1500.0, 3) * 2.1, -1, 1);
            var p = climatePointScratch;
            p[0] = t; p[1] = h; p[2] = c; p[3] = e; p[4] = w;
            return p;
        }
        static readonly double[][] MAIN_HEIGHT_CONT = { new[] { -1.0, 5 }, new[] { -0.6, 12 }, new[] { -0.42, 20 }, new[] { -0.3, 30 }, new[] { -0.2, 40 }, new[] { -0.1, 48 }, new[] { 0.0, 55 }, new[] { 0.12, 61 }, new[] { 0.24, 64 }, new[] { 0.4, 71 }, new[] { 0.6, 84 }, new[] { 1.0, 104 } };
        static readonly double[][] MAIN_HEIGHT_EROS = { new[] { -1.0, 1 }, new[] { -0.58, 0.88 }, new[] { -0.35, 0.62 }, new[] { -0.18, 0.36 }, new[] { 0.05, 0.16 }, new[] { 0.4, 0.06 }, new[] { 1.0, 0 } };
        static readonly double[][] MAIN_DENSITY_FACTOR = { new[] { -1.0, 1.3 }, new[] { -0.5, 2.2 }, new[] { -0.2, 3.6 }, new[] { 0.05, 5 }, new[] { 0.45, 6.5 }, new[] { 1.0, 7.5 } };
        static readonly double[][] MAIN_VALLEY_OFFSETS = BuildValleyOffsets();
        static double[][] BuildValleyOffsets()
        {
            var a = new List<double[]>();
            for (int i = 0; i < 8; i++)
            {
                double ang = (i * Math.PI) / 4;
                for (int r = 6; r <= 20; r += 7) a.Add(new[] { round(JsMath.cos(ang) * r), round(JsMath.sin(ang) * r), 1 - (r - 6) / 20.0 });
            }
            return a.ToArray();
        }
        static double peakValley(double w) { return 1 - Math.Abs(3 * Math.Abs(w) - 2); }
        double weirdAtMain(int x, int z)
        {
            int hs = hotSlot(x, z, WEIRD_HOT_MASK);
            if (weirdHotValid[hs] && weirdHotX[hs] == x && weirdHotZ[hs] == z) return weirdHotV[hs];
            double w = mainFbm2(3, x, z, 1 / 850.0, 5) * 2.2;
            weirdHotValid[hs] = true; weirdHotX[hs] = x; weirdHotZ[hs] = z; weirdHotV[hs] = w;
            return w;
        }
        double[] terrainShapeMain(double[] p, int x, int z)
        {
            double cont = p[2], eros = p[3], weird = p[4], pv = clamp(peakValley(weird), -1, 1), bs = curveSample(MAIN_HEIGHT_CONT, cont),
                land = clamp((cont + 0.25) / 0.4, 0, 1), mount = curveSample(MAIN_HEIGHT_EROS, eros) * land, rdg = JsMath.pow(pv * 0.5 + 0.5, 1.4);
            double h = bs + JsMath.pow(mount, 1.15) * rdg * 235;
            if (h > SEA - 3) { double d = mainFbm2(4, x, z, 1 / 22.0, 3); h += d * (9 + mount * 15) * clamp((h - (SEA - 2)) / 11, 0, 1); }
            else if (h < SEA - 4)
            {
                double f = clamp((SEA - 4 - h) / 22, 0, 1), n = (valueFbm4Main(x * 0.018 + 88.8, z * 0.018 - 44.4) * 2 - 1) * 2.2;
                h += n * (2.5 + f * 8);
            }
            if (cont > -0.2)
            {
                double best = 0;
                for (int i = 0; i < MAIN_VALLEY_OFFSETS.Length; i++)
                {
                    var q = MAIN_VALLEY_OFFSETS[i];
                    if (peakValley(weirdAtMain(x + (int)q[0], z + (int)q[1])) < -0.35 && q[2] > best) { best = q[2]; if (best == 1) break; }
                }
                if (best > 0) { double s = best * best * (3 - 2 * best), target = SEA + 4; if (h > target) h += (target - h) * s * 0.85; }
            }
            if (cont > -0.2 && pv < -0.35 && h > SEA)
            {
                double t = clamp((-0.35 - pv) / 0.35, 0, 1); t = t * t * (3 - 2 * t);
                double target = SEA + 3;
                if (h > target) h += (target - h) * t * 0.9;
                double f = clamp((-0.83 - pv) / 0.17, 0, 1); f = f * f * (3 - 2 * f);
                if (f > 0 && h > SEA - 2) h += (SEA - 2 - h) * f;
            }
            double mush = mushroomStrength(cont, x, z);
            if (mush > 0)
            {
                double island = SEA - 14 + mush * 26 + (valueFbm4Main(x * 0.02 + 51.5, z * 0.02 - 42.2) - 0.5) * 6;
                if (island > h) h = island;
            }
            h = round(clamp(h, WORLD_MIN_Y + 4, WORLD_MAX_Y - 4));
            var o = terrainShapeScratch;
            o[0] = h; o[1] = mush; o[2] = curveSample(MAIN_DENSITY_FACTOR, eros); o[3] = mount; o[4] = pv;
            return o;
        }
        public ColumnInfo columnInfo(double xd, double zd) { return columnInfo(ffloor(xd), ffloor(zd)); }
        public ColumnInfo columnInfo(int x, int z)
        {
            int hs = hotSlot(x, z, COLUMN_HOT_MASK);
            if (columnHotValid[hs] && columnHotX[hs] == x && columnHotZ[hs] == z) return columnHotValue[hs];
            var p = climatePoint(x, z);
            double p0 = p[0], p1 = p[1], p2 = p[2], p3 = p[3], p4 = p[4];
            var shape = terrainShapeMain(p, x, z);
            double origH = shape[0], mush = shape[1], factor = shape[2], mAmp = shape[3], peak = shape[4];
            var biome = selectMainBiome(origH, p, x, z, mush);
            var c = new ColumnInfo { origH = origH, biome = biome, temp = p0, moist = p1, continental = p2, erosion = p3, weird = p4, peak = peak, detail = 0, factor = factor, mAmp = mAmp };
            columnHotValid[hs] = true; columnHotX[hs] = x; columnHotZ[hs] = z; columnHotValue[hs] = c;
            return c;
        }
        public string biomeAt(int x, int z) { return columnInfo(x, z).biome; }
        public double dryAt(int x, int z) { return clamp((1 - columnInfo(x, z).moist) * 0.5, 0, 1); }
        public double hash3Main(double x, double y, double z)
        {
            int xi = toInt32(x + seed), yi = toInt32(y), zi = toInt32(z - seed);
            int n = toInt32((double)xi * 374761393.0 + (double)yi * 668265263.0 + (double)zi * 2147483423.0);
            n = n ^ ushr(n, 13);
            n = imul(n, 1274126177);
            return (uint)(n ^ ushr(n, 16)) / 4294967296.0;
        }
        double valueNoise3Main(double x, double y, double z)
        {
            int ix = ffloor(x), iy = ffloor(y), iz = ffloor(z);
            double fx = x - ix, fy = y - iy, fz = z - iz, u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy), w = fz * fz * (3 - 2 * fz),
                a = hash3Main(ix, iy, iz), b = hash3Main(ix + 1, iy, iz), c = hash3Main(ix, iy + 1, iz), d = hash3Main(ix + 1, iy + 1, iz),
                e = hash3Main(ix, iy, iz + 1), f = hash3Main(ix + 1, iy, iz + 1), g = hash3Main(ix, iy + 1, iz + 1), h = hash3Main(ix + 1, iy + 1, iz + 1),
                x0 = lerp(a, b, u), x1 = lerp(c, d, u), x2 = lerp(e, f, u), x3 = lerp(g, h, u);
            return lerp(lerp(x0, x1, v), lerp(x2, x3, v), w);
        }
        double valueFbm3Main(double x, double y, double z)
        {
            double sum = 0, amp = 0.5, f = 1, norm = 0;
            for (int i = 0; i < 4; i++) { sum += valueNoise3Main(x * f + i * 71.3, y * f - i * 113.1, z * f + i * 191.7) * amp; norm += amp; amp *= 0.5; f *= 2; }
            return sum / norm;
        }
        public double terrainDensity(int x, int y, int z)
        {
            var c = columnInfo(x, z);
            double bs = (c.origH - y) / 24, noise = valueFbm3Main(x * 0.016, y * 0.011, z * 0.016) * 2 - 1,
                seaFade = c.origH > SEA - 7 && c.origH < SEA + 9 ? 0.35 + 0.65 * clamp(Math.Abs(c.origH - SEA) / 9, 0, 1) : 1;
            return bs * c.factor + noise * (0.3 + c.mAmp * 0.9) * seaFade;
        }
        public int heightAt(int x, int z)
        {
            int slot = hotSlot(x, z, HEIGHT_HOT_MASK);
            if (heightHotValid[slot] && heightHotX[slot] == x && heightHotZ[slot] == z) return heightHotValue[slot];
            int h0 = (int)round(columnInfo(x, z).origH);
            int h = (int)clamp(h0 + 12, WORLD_MIN_Y, WORLD_MAX_Y - 1);
            for (; h >= Math.Max(WORLD_MIN_Y, h0 - 24); h--) if (terrainDensity(x, h, z) > 0) break;
            heightHotX[slot] = x; heightHotZ[slot] = z; heightHotValue[slot] = (short)h; heightHotValid[slot] = true;
            return h;
        }
        // Main-style feature surface: the dense generator samples a global 4x8x4 density lattice.
        double generatedColumnDensity(int x, int z, int gy)
        {
            int gx = floorDiv(x, 4) * 4, gz = floorDiv(z, 4) * 4; double fx = (x - gx) / 4.0, fz = (z - gz) / 4.0; int y = WORLD_MIN_Y + gy * 8;
            double a = lerp(terrainDensity(gx, y, gz), terrainDensity(gx + 4, y, gz), fx), b = lerp(terrainDensity(gx, y, gz + 4), terrainDensity(gx + 4, y, gz + 4), fx);
            return lerp(a, b, fz);
        }
        public int generatedSurfaceAt(int x, int z)
        {
            int slot = hotSlot(x, z, SURFACE_HOT_MASK);
            if (surfaceHotValid[slot] && surfaceHotX[slot] == x && surfaceHotZ[slot] == z) return surfaceHotValue[slot];
            int guess = heightAt(x, z), startGy = (int)clamp(ffloor((guess - WORLD_MIN_Y) / 8.0) + 3, 0, WORLD_H / 8 - 1);
            double hi = generatedColumnDensity(x, z, startGy + 1); int h = WORLD_MIN_Y;
            for (int gy = startGy; gy >= 0; gy--)
            {
                double lo = generatedColumnDensity(x, z, gy); int y0 = WORLD_MIN_Y + gy * 8;
                for (int dy = 7; dy >= 0; dy--)
                    if (lerp(lo, hi, dy / 8.0) > 0)
                    {
                        h = y0 + dy;
                        surfaceHotX[slot] = x; surfaceHotZ[slot] = z; surfaceHotValue[slot] = (short)h; surfaceHotValid[slot] = true;
                        return h;
                    }
                hi = lo;
            }
            surfaceHotX[slot] = x; surfaceHotZ[slot] = z; surfaceHotValue[slot] = (short)h; surfaceHotValid[slot] = true;
            return h;
        }
        public double heightAtRaw(int x, int z) { return columnInfo(x, z).origH; }

        // Main-compatible cave geometry and fluid thresholds.
        static double signed(double v) { return v * 2 - 1; }
        public int caveKind(int x, int y, int z, int surface, ColumnInfo ci)
        {
            if (y <= WORLD_MIN_Y + 5 || y > surface || y == surface) return 0;
            int depth = surface - y;
            double edge = depth < 8 ? depth / 8.0 : 1;
            if (y < WORLD_MIN_Y + 14) edge = Math.Min(edge, (y - (WORLD_MIN_Y + 5)) / 9.0);
            if (edge <= 0) return 0;
            double width = 1 + signed(valueNoise3Main(x * 0.09 + 2.2, y * 0.09 - 4.4, z * 0.09 + 6.6)) * 0.85;
            int kind = 0;
            if (y < 110)
            {
                double cheese = signed(valueNoise3Main(x * 0.012 + 11.1, y * 0.018 - 7.7, z * 0.012 + 4.4));
                if (cheese * cheese < 0.026 * 0.026 * edge * edge * width) kind = 1;
            }
            if (kind == 0)
            {
                double f = 0.045, wx = x + signed(valueNoise3Main(x * f + 61.1, y * f - 22.2, z * f + 13.3)) * 15,
                    wy = y + signed(valueNoise3Main(x * f + 9.9, y * f + 2.2, z * f - 51.1)) * 10,
                    wz = z + signed(valueNoise3Main(x * f - 31.7, y * f + 44.4, z * f - 8.8)) * 15;
                double n = valueNoise3Main(x * 0.02 + 5.5, y * 0.02 - 3.3, z * 0.02 + 7.7), radius = (0.3 + 1.8 * n * n) * width;
                if (y < 90)
                {
                    double a = signed(valueNoise3Main(wx * 0.022 + 12.3, wy * 0.022 - 7.7, wz * 0.022 + 4.1)), b = signed(valueNoise3Main(wx * 0.022 - 41.9, wy * 0.022 + 23.3, wz * 0.022 - 9.5));
                    if (a * a + b * b < 0.016 * edge * radius) kind = 2;
                }
                if (kind == 0 && valueNoise3Main(x * 0.006 + 90.1, y * 0.006 + 5.5, z * 0.006 - 30.2) > 0.5)
                {
                    double a = signed(valueNoise3Main(wx * 0.04 + 5.1, wy * 0.04 - 8.2, wz * 0.04 + 3.3)), b = signed(valueNoise3Main(wx * 0.04 - 7.7, wy * 0.04 + 9.9, wz * 0.04 - 2.1));
                    if (a * a + b * b < 0.009 * edge * radius) kind = 3;
                }
            }
            if (kind == 0) return 0;
            // main HQ() deepslate path: fluid state is then selected by the low-lava + flooded-cave noises.
            if (y <= WORLD_MIN_Y + 12) return 3;
            if (depth > 12)
            {
                double flooded = valueNoise3Main(x * 0.012 + 401.1, y * 0.005 - 77.7, z * 0.012 + 220.2);
                if (flooded > 0.52)
                {
                    double q = valueNoise3Main(x * 0.013 + 11.3, 0.5, z * 0.013 - 9.9);
                    double level = Math.Min(SEA, Math.Floor(-14 + (q - 0.5) * 44));
                    if (y <= level) return 2;
                }
            }
            return 1;
        }
        public const int CAVE_NONE = 0, CAVE_LUSH = 1, CAVE_DRIPSTONE = 2, CAVE_DEEP_DARK = 3;
        public int caveBiomeAt(int x, int y, int z, int? surface = null)
        {
            int h = surface ?? generatedSurfaceAt(x, z); double n = (h - y) / 128.0;
            if (n < 0.2) return CAVE_NONE;
            if (n > 0.9 && valueNoise3Main(x * 0.018 + 303.3, y * 0.02 - 44.4, z * 0.018 - 101.1) < 0.4) return CAVE_DEEP_DARK;
            if (n <= 0.96)
            {
                double lush = valueNoise3Main(x * 0.011 + 700.5, y * 0.012 + 55.5, z * 0.011 - 200.2), drip = valueNoise3Main(x * 0.011 - 410.7, y * 0.012 - 66.6, z * 0.011 + 330.3);
                if (lush > 0.64 && lush >= drip) return CAVE_LUSH;
                if (drip > 0.64) return CAVE_DRIPSTONE;
            }
            return CAVE_NONE;
        }
        /// <summary>Unfloored x/z variant (debug overlay calls caveBiomeAt(player.x, floor(y), player.z)); generatedSurfaceAt floors internally.</summary>
        public int caveBiomeAt(double x, int y, double z)
        {
            int h = generatedSurfaceAt((int)Math.Floor(x), (int)Math.Floor(z)); double n = (h - y) / 128.0;
            if (n < 0.2) return CAVE_NONE;
            if (n > 0.9 && valueNoise3Main(x * 0.018 + 303.3, y * 0.02 - 44.4, z * 0.018 - 101.1) < 0.4) return CAVE_DEEP_DARK;
            if (n <= 0.96)
            {
                double lush = valueNoise3Main(x * 0.011 + 700.5, y * 0.012 + 55.5, z * 0.011 - 200.2), drip = valueNoise3Main(x * 0.011 - 410.7, y * 0.012 - 66.6, z * 0.011 + 330.3);
                if (lush > 0.64 && lush >= drip) return CAVE_LUSH;
                if (drip > 0.64) return CAVE_DRIPSTONE;
            }
            return CAVE_NONE;
        }
        public int baseStoneAt(int x, int y, int z)
        {
            if (y <= 0) return valueNoise3Main(x * 0.07 + 71.7, y * 0.07 - 51.5, z * 0.07 + 31.3) > 0.86 ? B.TUFF : B.DEEPSLATE;
            if (y < 8 && hash3Main(x + 7, y - 3, z + 5) < (8 - y) / 8.0) return B.DEEPSLATE;
            if (y < 84)
            {
                if (valueNoise3Main(x * 0.085 + 5.1, y * 0.085 - 3.3, z * 0.085 + 9.7) > 0.85) return B.GRANITE;
                if (valueNoise3Main(x * 0.085 - 8.8, y * 0.085 + 6.6, z * 0.085 - 2.2) > 0.85) return B.DIORITE;
                if (valueNoise3Main(x * 0.085 + 14.4, y * 0.085 + 11.1, z * 0.085 + 4.4) > 0.85) return B.ANDESITE;
            }
            return B.STONE;
        }
        static readonly bool[] MAIN_CARVABLE = MakeMask(B.STONE, B.DIRT, B.GRASS, B.SAND, B.RED_SAND, B.SANDSTONE, B.GRAVEL, B.SNOW, B.PODZOL, B.TERRACOTTA, B.TERRA_ORANGE, B.TERRA_WHITE, B.MYCELIUM, B.MUD, B.PACKED_ICE);
        static bool[] MakeMask(params int[] ids) { var m = new bool[256]; foreach (var id in ids) if (id >= 0 && id < 256) m[id] = true; return m; }
        static bool caveDecorSolid(int id) { return id == B.STONE || id == B.DEEPSLATE || id == B.TUFF || id == B.GRANITE || id == B.DIORITE || id == B.ANDESITE || id == B.DIRT || id == B.GRAVEL; }
        // [ore, deepslateOre, min, peak, max, attempts, lenMin, lenMax, salt, mountainOnly]
        static readonly object[][] MAIN_ORE_CFG = {
            new object[] { B.COAL, B.DEEPSLATE_COAL_ORE, 0, 48, 132, 16, 8, 16, 0, false },
            new object[] { B.COPPER, B.DEEPSLATE_COPPER_ORE, -16, 48, 112, 18, 5, 10, 1, false },
            new object[] { B.IRON, B.DEEPSLATE_IRON_ORE, -24, 16, 80, 10, 4, 9, 2, false },
            new object[] { B.IRON, B.DEEPSLATE_IRON_ORE, -64, 8, 72, 8, 2, 4, 12, false },
            new object[] { B.LAPIS_ORE, B.DEEPSLATE_LAPIS_ORE, -32, 4, 40, 2, 3, 6, 3, false },
            new object[] { B.GOLD, B.DEEPSLATE_GOLD_ORE, -64, -16, 32, 2, 3, 7, 4, false },
            new object[] { B.REDSTONE_ORE, B.DEEPSLATE_REDSTONE_ORE, -64, -55, 16, 5, 4, 8, 5, false },
            new object[] { B.DIAMOND, B.DEEPSLATE_DIAMOND_ORE, -64, -55, 16, 1, 3, 5, 6, false },
            new object[] { B.EMERALD_ORE, B.DEEPSLATE_EMERALD_ORE, 60, 110, 200, 3, 1, 1, 7, true } };
        static readonly HashSet<string> MAIN_MOUNTAIN_ORE_BIOMES = new HashSet<string> { "MOUNTAIN", "PEAKS", "MEADOW", "GROVE", "SNOWY_SLOPES", "JAGGED_PEAKS", "STONY_PEAKS", "WINDSWEPT" };
        static readonly bool[] MAIN_ORE_HOSTS = MakeMask(B.STONE, B.GRANITE, B.DIORITE, B.ANDESITE, B.DEEPSLATE, B.TUFF);
        Func<double> oreRngMain(int cx, int cz, int salt)
        {
            int s = toInt32(hash2Main(cx * 3 + salt * 17 + 0.31, cz * 5 - salt * 7 + 0.77) * 4294967296.0);
            return () =>
            {
                s = unchecked(s + 1831565813);
                int t = imul(s ^ ushr(s, 15), 1 | s);
                t = unchecked(t + imul(t ^ ushr(t, 7), 61 | t)) ^ t;
                return (uint)(t ^ ushr(t, 14)) / 4294967296.0;
            };
        }
        static int oreYMain(Func<double> rng, int min, int peak, int max)
        {
            bool low = rng() * (max - min) < peak - min; double a = 1 - Math.Sqrt(rng());
            return (int)round(low ? peak - a * (peak - min) : peak + a * (max - peak));
        }
        static int idx(int lx, int y, int lz) { return ((y - WORLD_MIN_Y) * CHUNK + lz) * CHUNK + lx; }
        void placeMainOres(byte[] data, int cx, int cz)
        {
            int x0 = cx * CHUNK, z0 = cz * CHUNK;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int scx = cx + dx, scz = cz + dz, bx = scx * CHUNK, bz = scz * CHUNK;
                    foreach (var cfg in MAIN_ORE_CFG)
                    {
                        int ore = (int)cfg[0], dore = (int)cfg[1], mn = (int)cfg[2], pk = (int)cfg[3], mx = (int)cfg[4], att = (int)cfg[5], l0 = (int)cfg[6], l1 = (int)cfg[7], salt = (int)cfg[8]; bool mountain = (bool)cfg[9];
                        var rng = oreRngMain(scx, scz, salt);
                        for (int attempt = 0; attempt < att; attempt++)
                        {
                            int wx = bx + (int)Math.Floor(rng() * CHUNK), wz = bz + (int)Math.Floor(rng() * CHUNK), y = oreYMain(rng, mn, pk, mx);
                            int len = l0 + (int)Math.Floor(rng() * (l1 - l0 + 1));
                            if (mountain && !MAIN_MOUNTAIN_ORE_BIOMES.Contains(columnInfo(wx, wz).biome)) continue;
                            for (int k = 0; k < len; k++)
                            {
                                orePut(data, x0, z0, wx, y, wz, ore, dore);
                                if (rng() < 0.35) orePut(data, x0, z0, wx + (rng() < 0.5 ? 1 : -1), y, wz, ore, dore);
                                double q = rng();
                                if (q < 0.4) wx += rng() < 0.5 ? 1 : -1;
                                else if (q < 0.8) wz += rng() < 0.5 ? 1 : -1;
                                else y += rng() < 0.5 ? 1 : -1;
                            }
                        }
                    }
                }
        }
        static void orePut(byte[] data, int x0, int z0, int wx, int y, int wz, int ore, int dore)
        {
            if (wx < x0 || wx >= x0 + CHUNK || wz < z0 || wz >= z0 + CHUNK || y <= WORLD_MIN_Y || y >= WORLD_MAX_Y) return;
            int ii = idx(wx - x0, y, wz - z0);
            if (MAIN_ORE_HOSTS[data[ii]]) data[ii] = (byte)(y < 0 ? dore : ore);
        }

        // Main-compatible deepslate ravines.
        const double RAVINE_CHANCE = 1.0 / 110, RAVINE_STEP = 1.7; const int RAVINE_NEAR = 6;
        sealed class RavinePlan { public List<double[]> nodes; public int depth; public double waterFrac, minx, maxx, minz, maxz; public bool? wet; }
        Func<double> ravineRng(int cellX, int cellZ)
        {
            int r = imul(unchecked(cellX + seed), 461845907) ^ imul(unchecked(cellZ - seed), unchecked((int)3432918353u)) ^ 1597334677;
            return () =>
            {
                r = unchecked(r + 1831565813);
                int t = imul(r ^ ushr(r, 15), 1 | r);
                t = unchecked(t + imul(t ^ ushr(t, 7), 61 | t)) ^ t;
                return (uint)(t ^ ushr(t, 14)) / 4294967296.0;
            };
        }
        RavinePlan planRavine(int cellX, int cellZ)
        {
            var key = xzKey(cellX, cellZ); RavinePlan cached;
            if (ravinePlanCache.TryGetValue(key, out cached)) return cached;
            if (ravinePlanCache.Count > 20000) ravinePlanCache.Clear();
            var rng = ravineRng(cellX, cellZ);
            if (rng() >= RAVINE_CHANCE) { ravinePlanCache[key] = null; return null; }
            double x = cellX * CHUNK + rng() * CHUNK, z = cellZ * CHUNK + rng() * CHUNK, ang = rng() * Math.PI * 2;
            int steps = 44 + (int)Math.Floor(rng() * 40); double baseR = 3.6 + rng() * 4.4; int depth = 18 + (int)Math.Floor(rng() * 46); double waterFrac = rng() * rng() * 0.72;
            var nodes = new List<double[]>();
            double minx = double.PositiveInfinity, maxx = double.NegativeInfinity, minz = double.PositiveInfinity, maxz = double.NegativeInfinity;
            for (int i = 0; i < steps; i++)
            {
                x += JsMath.cos(ang) * RAVINE_STEP; z += JsMath.sin(ang) * RAVINE_STEP; ang += (rng() - 0.5) * 0.55;
                double p = steps > 1 ? i / (double)(steps - 1) : 0.5, r = baseR * JsMath.sin(p * Math.PI) + 0.5;
                nodes.Add(new[] { x, z, r });
                if (x - r < minx) minx = x - r; if (x + r > maxx) maxx = x + r; if (z - r < minz) minz = z - r; if (z + r > maxz) maxz = z + r;
            }
            var o = new RavinePlan { nodes = nodes, depth = depth, waterFrac = waterFrac, minx = minx, maxx = maxx, minz = minz, maxz = maxz, wet = null };
            ravinePlanCache[key] = o;
            return o;
        }
        List<RavinePlan> ravinesForChunk(int cx, int cz)
        {
            var key = xzKey(cx, cz); List<RavinePlan> cached;
            if (ravineChunkCache.TryGetValue(key, out cached)) return cached;
            if (ravineChunkCache.Count > 6000) ravineChunkCache.Clear();
            var o = new List<RavinePlan>(); int x0 = cx * CHUNK, z0 = cz * CHUNK;
            for (int dz = -RAVINE_NEAR; dz <= RAVINE_NEAR; dz++)
                for (int dx = -RAVINE_NEAR; dx <= RAVINE_NEAR; dx++)
                {
                    var r = planRavine(cx + dx, cz + dz);
                    if (r == null) continue;
                    if (r.maxx < x0 - 6 || r.minx > x0 + CHUNK + 6 || r.maxz < z0 - 6 || r.minz > z0 + CHUNK + 6) continue;
                    o.Add(r);
                }
            ravineChunkCache[key] = o;
            return o;
        }
        bool ravineContains2D(int x, int z)
        {
            var rs = ravinesForChunk(x >> 4, z >> 4);
            foreach (var r in rs)
                foreach (var n in r.nodes) { double dx = x + 0.5 - n[0], dz = z + 0.5 - n[1]; if (dx * dx + dz * dz < n[2] * n[2]) return true; }
            return false;
        }
        void carveRavines(byte[] data, int cx, int cz)
        {
            var rs = ravinesForChunk(cx, cz);
            if (rs.Count == 0) return;
            int x0 = cx * CHUNK, z0 = cz * CHUNK; var tops = new short[CHUNK * CHUNK];
            for (int lx = 0; lx < CHUNK; lx++)
                for (int lz = 0; lz < CHUNK; lz++)
                {
                    int top = WORLD_MIN_Y;
                    for (int y = WORLD_MAX_Y - 1; y > WORLD_MIN_Y; y--) { int id = data[idx(lx, y, lz)]; if (id != B.AIR && id != B.WATER) { top = y; break; } }
                    tops[lx * CHUNK + lz] = (short)top;
                }
            foreach (var r in rs)
            {
                bool wet;
                if (!r.wet.HasValue)
                {
                    wet = false;
                    foreach (var n in r.nodes) if (heightAtRaw((int)round(n[0]), (int)round(n[1])) <= SEA) { wet = true; break; }
                    r.wet = wet;
                }
                wet = r.wet.Value;
                foreach (var n in r.nodes)
                {
                    double nx = n[0], nz = n[1], rad = n[2], rad2 = rad * rad;
                    int lx0 = Math.Max(0, ffloor(nx - rad) - x0), lx1 = Math.Min(CHUNK - 1, (int)Math.Ceiling(nx + rad) - x0);
                    int lz0 = Math.Max(0, ffloor(nz - rad) - z0), lz1 = Math.Min(CHUNK - 1, (int)Math.Ceiling(nz + rad) - z0);
                    for (int lx = lx0; lx <= lx1; lx++)
                        for (int lz = lz0; lz <= lz1; lz++)
                        {
                            double dx = x0 + lx + 0.5 - nx, dz = z0 + lz + 0.5 - nz, d2 = dx * dx + dz * dz;
                            if (d2 >= rad2) continue;
                            int top = tops[lx * CHUNK + lz];
                            if (top <= WORLD_MIN_Y + 10) continue;
                            int floor = top - r.depth;
                            if (floor < WORLD_MIN_Y + 9) floor = WORLD_MIN_Y + 9;
                            int span = top - floor;
                            if (span < 1) continue;
                            int waterTop = wet ? SEA - 1 : floor + (int)round(r.waterFrac * span);
                            if (!wet && waterTop > top - 2) waterTop = top - 2;
                            for (int y = floor; y <= top; y++)
                            {
                                int ii = idx(lx, y, lz), id = data[ii];
                                if (id == B.BEDROCK || id == B.AIR) continue;
                                double p = (y - floor) / (double)span, shape = (p < 0.4 ? 0.22 + 0.78 * (p / 0.4) : p > 0.85 ? 1 - 0.35 * ((p - 0.85) / 0.15) : 1) * rad;
                                if (d2 < shape * shape) data[ii] = (byte)(y <= WORLD_MIN_Y + 12 ? B.LAVA : y <= waterTop ? B.WATER : B.AIR);
                            }
                        }
                }
            }
        }
        public int oreAt(int x, int y, int z) { return baseStoneAt(x, y, z); }
        public bool bedrockAt(int x, int y, int z)
        {
            if (y <= WORLD_MIN_Y) return true;
            if (y >= WORLD_MIN_Y + 5) return false;
            return hash3Main(x, y, z) < (WORLD_MIN_Y + 5 - y) / 5.0;
        }
        double surfaceRuggedness(int x, int z, double h) { return valueFbm4Main(x * 0.11 + 21, z * 0.11 - 21) + (h - SEA) * 0.004; }
        public int surfaceBlockForBiome(string bi, int x, int z, double h, double climateH)
        {
            int top;
            switch (bi)
            {
                case "DESERT": case "BEACH": top = B.SAND; break;
                case "SNOWY_BEACH": top = B.SNOW; break;
                case "BADLANDS": top = B.RED_SAND; break;
                case "WOODED_BADLANDS": top = climateH >= SEA + 10 ? B.GRASS : B.RED_SAND; break;
                case "MUSHROOM_FIELDS": top = B.MYCELIUM; break;
                case "MANGROVE_SWAMP": top = B.MUD; break;
                case "STONY_SHORE": top = B.STONE; break;
                case "WARM_OCEAN": case "LUKEWARM_OCEAN": top = B.SAND; break;
                case "COLD_OCEAN": case "FROZEN_OCEAN": top = B.GRAVEL; break;
                case "FROZEN_RIVER": top = B.SNOW; break;
                case "OCEAN":
                case "RIVER":
                    {
                        double depth = SEA - h;
                        if (depth < 1) { top = B.GRASS; break; }
                        if (valueFbm4Main(x * 0.11 + 311.2, z * 0.11 - 177.6) > 0.7) { top = B.CLAY; break; }
                        if (depth >= 3) { double q = valueFbm4Main(x * 0.05 + 777.7, z * 0.05 - 333.3); top = q > 0.66 ? B.GRAVEL : q < 0.3 ? B.DIRT : B.SAND; }
                        else top = B.SAND;
                        break;
                    }
                case "SNOWY": case "PEAKS": case "SNOWY_TAIGA": case "SNOWY_SLOPES": case "JAGGED_PEAKS": case "GROVE": case "ICE_SPIKES": top = B.SNOW; break;
                case "FROZEN_PEAKS": top = valueFbm4Main(x * 0.1 + 3, z * 0.1 - 3) < 0.46 ? B.PACKED_ICE : B.SNOW; break;
                case "STONY_PEAKS": top = B.STONE; break;
                case "WINDSWEPT": { double q = surfaceRuggedness(x, z, h); top = q < 0.55 ? B.GRASS : q < 0.68 ? B.STONE : B.GRAVEL; break; }
                case "OLD_GROWTH_TAIGA": top = valueFbm4Main(x * 0.09 - 13, z * 0.09 + 13) < 0.58 ? B.PODZOL : B.GRASS; break;
                case "BAMBOO_JUNGLE": top = valueFbm4Main(x * 0.12 - 3, z * 0.12 + 7) < 0.4 ? B.PODZOL : B.GRASS; break;
                default: top = B.GRASS; break;
            }
            if (h < SEA && (top == B.GRASS || top == B.PODZOL || top == B.MYCELIUM))
            {
                double depth = SEA - h;
                if (valueFbm4Main(x * 0.11 + 311.2, z * 0.11 - 177.6) > 0.7) top = B.CLAY;
                else if (depth >= 3) { double q = valueFbm4Main(x * 0.05 + 777.7, z * 0.05 - 333.3); top = q > 0.66 ? B.GRAVEL : q < 0.3 ? B.DIRT : B.SAND; }
                else top = B.SAND;
            }
            return top;
        }
        static readonly int[] BADLANDS_PALETTE = { B.TERRA_ORANGE, B.TERRACOTTA, B.TERRA_ORANGE, B.RED_SAND, B.TERRA_WHITE, B.TERRACOTTA, B.TERRA_ORANGE, B.TERRACOTTA };
        int badlandsLayer(int y)
        {
            if (y < 57) return B.STONE;
            int L = BADLANDS_PALETTE.Length;
            int k = (((y + (int)Math.Floor(hash2Main(0, y >> 3) * 3)) % L) + L) % L;
            return BADLANDS_PALETTE[k];
        }
        static readonly HashSet<string> SUB_SAND = new HashSet<string> { "BEACH", "SNOWY_BEACH", "OCEAN", "RIVER", "WARM_OCEAN", "LUKEWARM_OCEAN" };
        static readonly HashSet<string> SUB_NONE = new HashSet<string> { "STONY_PEAKS", "JAGGED_PEAKS", "SNOWY_SLOPES", "FROZEN_PEAKS", "STONY_SHORE", "COLD_OCEAN", "FROZEN_OCEAN", "FROZEN_RIVER" };
        /// <summary>Returns -1 for JS null.</summary>
        public int subsurfaceBlockForBiome(string bi, int top, int depth, int y = 0)
        {
            if (depth > 3) return -1;
            if (bi == "BADLANDS") return badlandsLayer(y);
            if (bi == "WOODED_BADLANDS") return top == B.GRASS ? B.DIRT : badlandsLayer(y);
            if (bi == "DESERT") return B.SANDSTONE;
            if (bi == "MANGROVE_SWAMP") return B.MUD;
            if (SUB_SAND.Contains(bi)) return B.SAND;
            if (SUB_NONE.Contains(bi)) return -1;
            return B.DIRT;
        }
        public int terrainBlock(int x, int y, int z)
        {
            if (y < WORLD_MIN_Y) return B.BEDROCK;
            if (y >= WORLD_MAX_Y) return B.AIR;
            if (bedrockAt(x, y, z)) return B.BEDROCK;
            var ci = columnInfo(x, z); int h = heightAt(x, z); var bi = ci.biome; double d = terrainDensity(x, y, z);
            if (d > 0)
            {
                int ck = caveKind(x, y, z, h, ci);
                if (ck == 1) return B.AIR;
                if (ck == 2) return B.WATER;
                if (ck == 3) return B.LAVA;
                int dep = h - y, top = surfaceBlockForBiome(bi, x, z, h, ci.origH);
                if (dep == 0) return top;
                if (dep > 0 && dep <= 3) { int q = subsurfaceBlockForBiome(bi, top, dep, y); if (q >= 0) return q; }
                return baseStoneAt(x, y, z);
            }
            if ((bi == "FROZEN_OCEAN" || bi == "FROZEN_RIVER") && y == SEA && h < SEA) return B.ICE;
            return y <= SEA ? B.WATER : B.AIR;
        }
        // Minecraft ordering: surface rules run before carvers, while vegetation/features run after carvers.
        int surfaceRuleBlockAt(int x, int y, int z, int top, ColumnInfo ci)
        {
            int topId = surfaceBlockForBiome(ci.biome, x, z, top, ci.origH), dep = top - y;
            if (dep == 0) return topId;
            if (dep > 0 && dep <= 3) { int q = subsurfaceBlockForBiome(ci.biome, topId, dep, y); if (q >= 0) return q; }
            return baseStoneAt(x, y, z);
        }
        public SurfaceInfo postCarverSurfaceInfoAt(int x, int z)
        {
            int slot = hotSlot(x, z, FEATURE_SURFACE_HOT_MASK);
            if (featureSurfaceHotValid[slot] && featureSurfaceHotX[slot] == x && featureSurfaceHotZ[slot] == z) return featureSurfaceHotValue[slot];
            var ci = columnInfo(x, z); int top = generatedSurfaceAt(x, z);
            var o = new SurfaceInfo { y = WORLD_MIN_Y, ground = B.BEDROCK, below = B.BEDROCK };
            for (int y = top; y > WORLD_MIN_Y; y--)
            {
                if (bedrockAt(x, y, z)) { o = new SurfaceInfo { y = y, ground = B.BEDROCK, below = B.BEDROCK }; break; }
                if (caveKind(x, y, z, top, ci) != 0) continue;
                int ground = surfaceRuleBlockAt(x, y, z, top, ci), below = B.BEDROCK;
                if (y - 1 > WORLD_MIN_Y)
                {
                    int ck = caveKind(x, y - 1, z, top, ci);
                    below = ck == 0 ? surfaceRuleBlockAt(x, y - 1, z, top, ci) : ck == 2 ? B.WATER : ck == 3 ? B.LAVA : B.AIR;
                }
                o = new SurfaceInfo { y = y, ground = ground, below = below };
                break;
            }
            featureSurfaceHotX[slot] = x; featureSurfaceHotZ[slot] = z; featureSurfaceHotValue[slot] = o; featureSurfaceHotValid[slot] = true;
            return o;
        }
    }
}
