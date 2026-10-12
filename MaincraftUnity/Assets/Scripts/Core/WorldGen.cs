// Port of worldgen.js (WorldGen class): Minecraft 1.18-style terrain: continentalness / erosion /
// weirdness drive a height field, a 3D density adds overhangs, then surface rules per biome,
// noise caves, ores, trees and vegetation. Everything is deterministic per seed and chunk.
// Runs on the generator threads (one WorldGen per thread, like one per Web Worker).
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static Maincraft.WG;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed class Clim
    {
        public double h, c, e, w, pv, s, mush, k6, factor, t, hu;
        public int ti;
    }

    public sealed class ColInfo
    {
        public double h, factor, amp;
        public int biome;
    }

    public sealed class GenResult
    {
        public ushort[][] secIds = new ushort[SECTIONS][];
        public byte[][] secMeta = new byte[SECTIONS][];
        public byte[] biomes;
        public byte[] caveBiomes;
        public List<int> springs;
        public List<int> spawners;
    }

    public sealed class TreeResult { public int[] w, d; }

    sealed class Geode
    {
        public int ox, oy, oz; public double rx, ry, rz, rmin; public bool crack; public int cdx, cdz; public double nx, nz; public int seed;
    }

    sealed class Beard { public int y0, y1; public float[] A; }

    public sealed partial class WorldGen
    {
        public readonly int seed;
        readonly Simplex nCont, nEro, nWeird, nTemp, nHum, nHill, nDen, nCheese, nSp1, nSp2, nNd1, nNd2, nSurf, nRiver, nCave, nMisc, nOre, nMush, nWarp, nGeode;
        readonly ushort[] ids = new ushort[COL_N];
        readonly byte[] meta = new byte[COL_N];
        readonly Clim clim = new Clim();
        readonly float[] H = new float[18 * 18];       // analytic heights incl. 1-block margin
        readonly byte[] BIO = new byte[256];
        readonly short[] TOP = new short[256];         // top solid after surface
        readonly Dictionary<long, int> bioCache = new Dictionary<long, int>();
        readonly Dictionary<long, ColInfo> colCache = new Dictionary<long, ColInfo>();
        // quart grid cache of biomeAtBlock
        int qgX0 = 1000000000, qgZ0 = 1000000000;
        const int QG_N = 12;
        readonly float[] qgJ = new float[288];
        readonly short[] qgB = new short[144];
        readonly ValueNoise vn;
        readonly Dictionary<long, int> gCache = new Dictionary<long, int>();
        readonly Dictionary<long, string> candCache = new Dictionary<long, string>();
        readonly Dictionary<long, TreeResult> treeCache = new Dictionary<long, TreeResult>();
        readonly Dictionary<(int, int, int), List<Piece>> structCache = new Dictionary<(int, int, int), List<Piece>>();
        readonly Dictionary<long, int> surfCache = new Dictionary<long, int>();
        readonly List<int> deco = new List<int>();     // tree decorations of the chunk being generated
        List<int> treeWrites;                          // queued tree blocks while features() runs
        // tree buffer; id carries the log axis in bits 16+ (0 y, 1 x, 2 z)
        const int TB_MAX = 16384;
        int tbN;
        readonly Dictionary<int, int> tbMap = new Dictionary<int, int>();
        readonly int[] tbX = new int[TB_MAX], tbY = new int[TB_MAX], tbZ = new int[TB_MAX], tbId = new int[TB_MAX], tbQ = new int[TB_MAX];
        readonly byte[] tbKind = new byte[TB_MAX], tbDist = new byte[TB_MAX];

        int cx, cz, x0, z0;
        Beard beard;
        short[] vilGround;
        List<int[]> vilBoxes;
        List<Piece> beardPieces;
        List<int> springList;
        List<int> spawnerList;
        byte[] cbio;
        short[] aqLo;
        Clim _bo, _cbo;
        Dictionary<long, Geode> geodeCache;
        Dictionary<long, Dictionary<long, string>> attemptCache;

        // regex tests of the original, precomputed per block id
        static bool[] LOGKEY, ROOTSKEY, TERRAKEY;
        static readonly object _initLock = new object();

        public WorldGen(int seed)
        {
            lock (_initLock)
            {
                WG.Init();
                if (LOGKEY == null)
                {
                    var lg = new bool[NBX]; var rt = new bool[NBX]; var tc = new bool[NBX];
                    var reLog = new Regex("_LOG$|^LOG$"); var reRoot = new Regex("ROOTS"); var reTc = new Regex("TERRACOTTA$");
                    for (int i = 0; i < NBX; i++)
                    {
                        string k = B_KEY[i] ?? "";
                        lg[i] = reLog.IsMatch(k); rt[i] = reRoot.IsMatch(k); tc[i] = reTc.IsMatch(k);
                    }
                    ROOTSKEY = rt; TERRAKEY = tc; LOGKEY = lg;
                }
            }
            this.seed = seed;
            Simplex S(int k) => new Simplex(unchecked((uint)(this.seed ^ (k * 0x5bd1e995))));
            nCont = S(1); nEro = S(2); nWeird = S(3); nTemp = S(4); nHum = S(5);
            nHill = S(6); nDen = S(7); nCheese = S(8); nSp1 = S(9); nSp2 = S(10);
            nNd1 = S(11); nNd2 = S(12); nSurf = S(13); nRiver = S(14); nCave = S(15);
            nMisc = S(16); nOre = S(17); nMush = S(18); nWarp = S(19); nGeode = S(20);
            vn = new ValueNoise(this.seed);
        }

        static long K2(int x, int z) => (long)x * 131072 + z;
        static long K16(int x, int z) => (long)x * 65536 + z;

        // ------------------------------------------------------------ climate / height
        // fractal noise with Perlin-like amplitude (simplex output is scaled down to match)
        static double Fbm(Simplex n, double x, double z, double scale, int oct) => n.f2(x * scale, z * scale, oct) * 0.72;

        // terrain part of the climate: height + density parameters for column x,z
        public Clim Terrain(double x, double z, Clim o)
        {
            double c = Fbm(nCont, x, z, 1.0 / 2200, 4) * 2.3 + 0.3;
            double e = Fbm(nEro, x, z, 1.0 / 2000, 4) * 2.1;
            double w = Fbm(nWeird, x, z, 1.0 / 850, 5) * 2.2;
            double pv = 1 - Math.Abs(3 * Math.Abs(w) - 2);
            double h = Spline(SPL_BASE, c);
            double s = Spline(SPL_MOUNT, e) * Clamp01((c + 0.25) / 0.4);
            // mountains: low erosion raises the land, the peaks-and-valleys ridge decides how much
            double wk = Math.Pow(Clamp01(pv * 0.5 + 0.5), 2);
            h += Math.Pow(Math.Pow(s, 1.15) * wk, 1.3) * 175;
            if (h > SEA - 3) h += Fbm(nHill, x, z, 1.0 / 22, 3) * (9 + s * 15) * Clamp01((h - (SEA - 2)) / 11);
            else if (h < SEA - 4) h += Fbm(nMisc, x, z, 0.018, 2) * 2.2 * (2.5 + Clamp01((SEA - 4 - h) / 22) * 8);
            // Minecraft's highest erosion zone (6) inland is where swamps and mangrove swamps sit
            double k6 = Sstep(0.5, 0.6, e * 1.5) * Sstep(0.1, 0.22, c);
            if (k6 > 0)
            {
                double t = SEA + 0.6 + Fbm(nHill, x, z, 1.0 / 38, 2) * 2.4;
                if (h > t) h += (t - h) * k6;
            }
            // valleys and rivers where weirdness crosses zero (peaks-and-valleys < -0.35)
            if (c > -0.2)
            {
                double bank = Smooth01((-0.05 - pv) / 0.3);
                if (bank > 0 && h > SEA + 4) h += (SEA + 4 - h) * bank * 0.85;
                if (pv < -0.35 && h > SEA)
                {
                    double t = Smooth01((-0.35 - pv) / 0.35);
                    if (h > SEA + 3) h += (SEA + 3 - h) * t * 0.9;
                    double r = Smooth01((-0.83 - pv) / 0.17);
                    if (r > 0 && h > SEA - 2) h += (SEA - 2 - h) * r;
                }
            }
            // mushroom islands far out in the ocean
            double mush = 0;
            if (c < -0.75)
            {
                double m = nMush.n2(x * 0.0011, z * 0.0011) * 0.5 + 0.5;
                if (m > 0.72)
                {
                    mush = Smooth01((m - 0.72) / 0.12) * Smooth01((-0.75 - c) / 0.12);
                    double k = SEA - 14 + mush * 26 + Fbm(nHill, x, z, 0.02, 2) * 3;
                    if (k > h) h = k;
                }
            }
            if (h < WORLD_MIN_Y + 4) h = WORLD_MIN_Y + 4; if (h > WORLD_MAX_Y - 4) h = WORLD_MAX_Y - 4;
            o.h = h; o.c = c; o.e = e; o.w = w; o.pv = pv; o.s = s; o.mush = mush; o.k6 = k6;
            o.factor = Spline(SPL_FACTOR, e);
            return o;
        }

        // full climate incl. temperature / humidity (domain warped) for biome selection
        public Clim Climate(double x, double z, Clim o)
        {
            Terrain(x, z, o);
            double wx = x + (nWarp.n2(x * 0.008, z * 0.008) * 0.5) * 16, wz = z + (nWarp.n2(x * 0.008 + 300, z * 0.008 - 300) * 0.5) * 16;
            o.t = Clamp1(Fbm(nTemp, wx, wz, 1.0 / 3200, 3) * 2.4);
            o.hu = Clamp1(Fbm(nHum, wx, wz, 1.0 / 1500, 3) * 2.1);
            o.ti = o.t < -0.45 ? 0 : o.t < -0.15 ? 1 : o.t < 0.2 ? 2 : o.t < 0.55 ? 3 : 4;
            return o;
        }

        int BiomeOf(Clim o, double x, double z)
        {
            double h = Math.Floor(o.h + 0.5); int ti = o.ti;
            if (o.mush > 0.25 && h > SEA) return BI.MUSHROOM_FIELDS;
            // flat erosion-6 lowland: water level ground is still land (swamp), not beach or ocean
            bool low6 = o.k6 > 0.5 && h > SEA - 4;
            if (h <= SEA)
            {
                if (o.c > -0.11 && h > SEA - 5 && Math.Abs(o.w) < 0.065) return ti == 0 ? BI.FROZEN_RIVER : BI.RIVER;
            }
            if (h <= SEA && !low6)
            {
                bool deep = o.c < -0.455;
                switch (ti)
                {
                    case 0: return deep ? BI.DEEP_FROZEN_OCEAN : BI.FROZEN_OCEAN;
                    case 1: return deep ? BI.DEEP_COLD_OCEAN : BI.COLD_OCEAN;
                    case 2: return deep ? BI.DEEP_OCEAN : BI.OCEAN;
                    case 3: return deep ? BI.DEEP_LUKEWARM_OCEAN : BI.LUKEWARM_OCEAN;
                    default: return BI.WARM_OCEAN;
                }
            }
            if (h <= SEA + 2 && o.pv > -0.35 && !low6)
            {
                if (ti == 0) return BI.SNOWY_BEACH;
                if (ti == 4) return BI.DESERT;
                if (o.e < -0.18) return BI.STONY_SHORE;
                if (nMisc.n2(x * 0.011 + 401.7, z * 0.011 - 233.1) * 0.5 + 0.5 > 0.35) return BI.BEACH;
            }
            return LandBiome(o);
        }

        // biome at quart resolution (4x4 columns, like Minecraft's biome storage)
        public int BiomeAt(int x, int z)
        {
            int qx = x >> 2, qz = z >> 2; long k = K2(qx, qz);
            if (!bioCache.TryGetValue(k, out int b))
            {
                var o = Climate(qx * 4 + 2, qz * 4 + 2, _bo ?? (_bo = new Clim()));
                b = BiomeOf(o, qx * 4 + 2, qz * 4 + 2);
                if (bioCache.Count > 80000) bioCache.Clear();
                bioCache[k] = b;
            }
            return b;
        }

        // Per-block biome with Minecraft's "fuzzy zoom": every quart cell gets a seeded jittered centre
        // (up to ±1.8 blocks) and each block takes the biome of the nearest centre.
        public int BiomeAtBlock(int x, int z)
        {
            int i = x - 2, k = z - 2, qx = i >> 2, qz = k >> 2;
            double fx = (i & 3) / 4.0, fz = (k & 3) / 4.0;
            int best = 0; double bd = double.PositiveInfinity;
            for (int c = 0; c < 4; c++)
            {
                int a = c & 1, b = c >> 1, ccx = qx + a, ccz = qz + b;
                double jx, jz;
                int gx = ccx - qgX0, gz = ccz - qgZ0;
                if (gx >= 0 && gz >= 0 && gx < QG_N && gz < QG_N) { int o = (gz * QG_N + gx) * 2; jx = qgJ[o]; jz = qgJ[o + 1]; }
                else { jx = (Hash.Hash2(unchecked(seed + 61), ccx, ccz) - 0.5) * 0.9; jz = (Hash.Hash2(unchecked(seed + 62), ccx, ccz) - 0.5) * 0.9; }
                double dx = fx - a - jx, dz = fz - b - jz, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = c; }
            }
            {
                int ccx = qx + (best & 1), ccz = qz + (best >> 1), gx = ccx - qgX0, gz = ccz - qgZ0;
                if (gx >= 0 && gz >= 0 && gx < QG_N && gz < QG_N)
                {
                    int o = gz * QG_N + gx;
                    int b = qgB[o];
                    if (b < 0) { b = BiomeAt(ccx * 4 + 2, ccz * 4 + 2); qgB[o] = (short)b; }
                    return b;
                }
                return BiomeAt(ccx * 4 + 2, ccz * 4 + 2);
            }
        }

        // per-chunk cache of the quart cells around the chunk (jitter + biome) used by biomeAtBlock
        void PrepQuartGrid(int x0, int z0)
        {
            int qx0 = ((x0 - 12) >> 2) - 1, qz0 = ((z0 - 12) >> 2) - 1;
            if (qgX0 == qx0 && qgZ0 == qz0) return;
            qgX0 = qx0; qgZ0 = qz0;
            for (int gz = 0; gz < QG_N; gz++) for (int gx = 0; gx < QG_N; gx++)
            {
                int o = gz * QG_N + gx, ccx = qx0 + gx, ccz = qz0 + gz;
                qgJ[o * 2] = (float)((Hash.Hash2(unchecked(seed + 61), ccx, ccz) - 0.5) * 0.9); qgJ[o * 2 + 1] = (float)((Hash.Hash2(unchecked(seed + 62), ccx, ccz) - 0.5) * 0.9);
                qgB[o] = -1;
            }
        }

        // cached terrain for grid corners & tree candidates (keyed by x,z)
        public ColInfo ColInfoAt(int x, int z)
        {
            long k = K2(x, z);
            if (colCache.TryGetValue(k, out var v)) return v;
            var o = Terrain(x, z, new Clim());
            double near = Math.Abs(o.h - SEA);
            v = new ColInfo { h = o.h, factor = o.factor, amp = (0.3 + o.s * 0.9) * (near < 9 ? 0.35 + 0.65 * near / 9 : 1), biome = 0 };
            v.biome = BiomeAtBlock(x, z);
            if (colCache.Count > 60000) colCache.Clear();
            colCache[k] = v;
            return v;
        }

        // density > 0 is solid. The 3D noise fades out above the surface so nothing floats in the air.
        double Density(double x, double y, double z, ColInfo ci)
        {
            double t = (ci.h - y) / 24 * ci.factor;
            double amp = ci.amp;
            if (y > ci.h - 4) amp *= Clamp01((ci.h + 6 - y) / 10);
            if (t > amp || t < -amp) return t;
            return t + amp * nDen.f3(x * 0.016, y * 0.011, z * 0.016, 3) * 0.9;
        }

        // surface height (top solid) computed purely from density, identical to what chunk fill produces
        public int SurfaceAt(int x, int z)
        {
            int gx = (int)Math.Floor(x / 4.0) * 4, gz = (int)Math.Floor(z / 4.0) * 4;
            double fx = (x - gx) / 4.0, fz = (z - gz) / 4.0;
            ColInfo c00 = ColInfoAt(gx, gz), c10 = ColInfoAt(gx + 4, gz), c01 = ColInfoAt(gx, gz + 4), c11 = ColInfoAt(gx + 4, gz + 4);
            double top = Math.Max(Math.Max(c00.h, c10.h), Math.Max(c01.h, c11.h)) + 8;
            int j = (int)Math.Min(47, Math.Floor((top - WORLD_MIN_Y) / 8));
            double DAt(int jj)
            {
                int y = WORLD_MIN_Y + jj * 8;
                double a = Density(gx, y, gz, c00), b = Density(gx + 4, y, gz, c10);
                double c = Density(gx, y, gz + 4, c01), d = Density(gx + 4, y, gz + 4, c11);
                double ab = a + (b - a) * fx, cd = c + (d - c) * fx;
                return ab + (cd - ab) * fz;
            }
            double dHi = DAt(j + 1);
            for (; j >= 0; j--)
            {
                double dLo = DAt(j);
                if (dLo > 0 || dHi > 0)
                {
                    for (int k = 7; k >= 0; k--)
                    {
                        double v = dLo + (dHi - dLo) * (k / 8.0);
                        if (v > 0) return WORLD_MIN_Y + j * 8 + k;
                    }
                }
                dHi = dLo;
            }
            return WORLD_MIN_Y;
        }

        // ------------------------------------------------------------ main entry
        public GenResult Generate(int cx, int cz)
        {
            Array.Clear(ids, 0, ids.Length); Array.Clear(meta, 0, meta.Length);
            int x0 = cx * 16, z0 = cz * 16;
            this.cx = cx; this.cz = cz; this.x0 = x0; this.z0 = z0;
            var o = clim;
            // heights with margin and biomes
            for (int dz = -1; dz <= 16; dz++) for (int dx = -1; dx <= 16; dx++)
            {
                Terrain(x0 + dx, z0 + dz, o);
                H[(dz + 1) * 18 + dx + 1] = (float)o.h;
            }
            PrepQuartGrid(x0, z0);
            for (int dz = 0; dz < 16; dz++) for (int dx = 0; dx < 16; dx++) BIO[dz * 16 + dx] = (byte)BiomeAtBlock(x0 + dx, z0 + dz);
            vilGround = null;
            beard = VillageBeard();
            StructureTreeBoxes();
            FillTerrain();
            Surface();
            Caves();
            Geodes();
            spawnerList = new List<int>();
            Dungeons();
            Structures();
            Ores();
            Bedrock();
            Springs();
            Features();
            CaveDecor();
            // split into sections
            var res = new GenResult();
            int topSec = -1;
            for (int s = SECTIONS - 1; s >= 0; s--)
            {
                int off = s * 4096;
                bool any = false;
                for (int i = 0; i < 4096; i++) if (ids[off + i] != 0) { any = true; break; }
                if (any) { topSec = s; break; }
            }
            for (int s = 0; s < SECTIONS; s++)
            {
                if (s > topSec) continue;
                int off = s * 4096;
                var si = new ushort[4096]; var sm = new byte[4096];
                Array.Copy(ids, off, si, 0, 4096); Array.Copy(meta, off, sm, 0, 4096);
                res.secIds[s] = si; res.secMeta[s] = sm;
            }
            res.biomes = (byte[])BIO.Clone();
            res.caveBiomes = (byte[])cbio.Clone();
            res.springs = springList;
            res.spawners = spawnerList;
            return res;
        }

        void FillTerrain()
        {
            var ids = this.ids; int x0 = this.x0, z0 = this.z0;
            // grid 5x5 corners x 49 rows
            var cinfo = new ColInfo[25];
            double hmax = -1e9;
            for (int k = 0; k < 5; k++) for (int i = 0; i < 5; i++)
            {
                var ci = ColInfoAt(x0 + i * 4, z0 + k * 4);
                cinfo[k * 5 + i] = ci;
                double t = ci.h + 8;
                if (t > hmax) hmax = t;
            }
            // rows to evaluate: up to the terrain top, but always to sea level so oceans are filled with water
            var BD = beard;
            if (BD != null) hmax = Math.Max(hmax, BD.y1);
            int rows = (int)Math.Min(47, Math.Max(Math.Floor((hmax - WORLD_MIN_Y) / 8) + 1, Math.Ceiling((SEA + 1 - WORLD_MIN_Y) / 8.0)));
            var D = new float[25 * 49];
            for (int k = 0; k < 5; k++) for (int i = 0; i < 5; i++)
            {
                var ci = cinfo[k * 5 + i]; int gx = x0 + i * 4, gz = z0 + k * 4;
                for (int j = 0; j <= 48; j++)
                {
                    int y = WORLD_MIN_Y + j * 8;
                    D[(k * 5 + i) * 49 + j] = j > rows + 1 ? -10f : (float)Density(gx, y, gz, ci);
                }
            }
            int seed = this.seed;
            for (int ck = 0; ck < 4; ck++) for (int cii = 0; cii < 4; cii++)
            {
                int a = (ck * 5 + cii) * 49, b = (ck * 5 + cii + 1) * 49, c = ((ck + 1) * 5 + cii) * 49, d = ((ck + 1) * 5 + cii + 1) * 49;
                for (int cj = 0; cj < 48 && cj <= rows; cj++)
                {
                    double d000 = D[a + cj], d100 = D[b + cj], d001 = D[c + cj], d101 = D[d + cj];
                    double d010 = D[a + cj + 1], d110 = D[b + cj + 1], d011 = D[c + cj + 1], d111 = D[d + cj + 1];
                    int yb = WORLD_MIN_Y + cj * 8;
                    // Beardifier near village pieces: added per block, in this terrain's units
                    bool bd = BD != null && yb + 7 >= BD.y0 && yb <= BD.y1;
                    double g = bd ? (cinfo[ck * 5 + cii].factor + cinfo[ck * 5 + cii + 1].factor + cinfo[(ck + 1) * 5 + cii].factor + cinfo[(ck + 1) * 5 + cii + 1].factor) / 96 : 0;
                    bool allS = !bd && d000 > 0 && d100 > 0 && d001 > 0 && d101 > 0 && d010 > 0 && d110 > 0 && d011 > 0 && d111 > 0;
                    bool allA = !bd && d000 <= 0 && d100 <= 0 && d001 <= 0 && d101 <= 0 && d010 <= 0 && d110 <= 0 && d011 <= 0 && d111 <= 0;
                    if (allA && yb > SEA) continue;
                    for (int ly = 0; ly < 8; ly++)
                    {
                        int y = yb + ly; double ty = ly / 8.0;
                        double e00 = d000 + (d010 - d000) * ty, e10 = d100 + (d110 - d100) * ty;
                        double e01 = d001 + (d011 - d001) * ty, e11 = d101 + (d111 - d101) * ty;
                        int stone = B.STONE;
                        if (y < 0) stone = B.DEEPSLATE;
                        for (int lz = 0; lz < 4; lz++)
                        {
                            double tz = lz / 4.0;
                            double f0 = e00 + (e01 - e00) * tz, f1 = e10 + (e11 - e10) * tz;
                            int z = ck * 4 + lz;
                            for (int lx = 0; lx < 4; lx++)
                            {
                                int x = cii * 4 + lx;
                                double v = allS ? 1 : allA ? -1 : f0 + (f1 - f0) * (lx / 4.0);
                                if (bd && y >= BD.y0 && y <= BD.y1) { double bb = BD.A[(y - BD.y0) * 256 + z * 16 + x]; if (bb != 0) v += bb * g / (v > 0 ? 0.05 : 0.0125); }
                                int id;
                                if (v > 0)
                                {
                                    id = stone;
                                    if (y >= 0 && y < 8 && Hash.Hash3(seed, x0 + x, y, z0 + z) < (8 - y) / 8.0) id = B.DEEPSLATE;
                                }
                                else id = y <= SEA ? B.WATER : 0;
                                if (id != 0) ids[CI(x, y, z)] = (ushort)id;
                            }
                        }
                    }
                }
            }
        }

        static bool IsStoneLike(int id) => id == B.STONE || id == B.DEEPSLATE;

        // ---------------------------------------------------------------- surface rules (maincraft)
        // Top block of a column by the original's rules; y = top solid block, h = analytic height.
        int SurfTop(int ob, int x, int z, int y, double h)
        {
            var V = vn;
            int r;
            switch (ob)
            {
                case BI.DESERT: case BI.BEACH: case BI.WARM_OCEAN: case BI.LUKEWARM_OCEAN: r = B.SAND; break;
                case BI.SNOWY_BEACH: r = B.SAND; break;
                case BI.FROZEN_RIVER: r = y >= SEA ? B.GRASS : FloorMix(x, z, y); break;
                case BI.BADLANDS: r = B.RED_SAND; break;
                case BI.WOODED_BADLANDS: r = h >= SEA + 10 ? B.GRASS : B.RED_SAND; break;
                case BI.MUSHROOM_FIELDS: r = B.MYCELIUM; break;
                case BI.MANGROVE_SWAMP: r = B.MUD; break;
                case BI.STONY_SHORE: case BI.STONY_PEAKS: r = B.STONE; break;
                case BI.COLD_OCEAN: case BI.FROZEN_OCEAN: r = B.GRAVEL; break;
                case BI.OCEAN: case BI.RIVER: r = y >= SEA ? B.GRASS : FloorMix(x, z, y); break;
                case BI.SNOWY_SLOPES: case BI.JAGGED_PEAKS: case BI.GROVE: case BI.ICE_SPIKES: r = B.SNOW; break;
                case BI.FROZEN_PEAKS: r = V.f2(x * 0.1 + 3, z * 0.1 - 3) < 0.46 ? B.PACKED_ICE : B.SNOW; break;
                case BI.WINDSWEPT_HILLS: { double n = Windswept(x, z, y); r = n < 0.55 ? B.GRASS : n < 0.68 ? B.STONE : B.GRAVEL; break; }
                case BI.OLD_GROWTH_SPRUCE_TAIGA: r = V.f2(x * 0.09 - 13, z * 0.09 + 13) < 0.58 ? B.PODZOL : B.GRASS; break;
                case BI.BAMBOO_JUNGLE: r = V.f2(x * 0.12 - 3, z * 0.12 + 7) < 0.4 ? B.PODZOL : B.GRASS; break;
                default: r = B.GRASS; break;
            }
            // soil below sea level (lake / river / ocean floor of a land biome)
            if (y < SEA && (r == B.GRASS || r == B.PODZOL || r == B.MYCELIUM)) r = FloorMix(x, z, y);
            return r;
        }

        // Minecraft's swamp surface rule (see surface()): the top block at the water line is water
        bool PoolAt(int ob, int x, int z, int y)
        {
            return ((ob == BI.SWAMP && y == SEA) || (ob == BI.MANGROVE_SWAMP && y >= SEA - 2 && y <= SEA)) &&
                nSurf.n2(x / 11.0 + 71.3, z / 11.0 - 19.7) > 0;
        }

        int FloorMix(int x, int z, int y)
        {
            var V = vn;
            if (V.f2(x * 0.11 + 311.2, z * 0.11 - 177.6) > 0.7) return B.CLAY;
            if (SEA - y >= 3) { double t = V.f2(x * 0.05 + 777.7, z * 0.05 - 333.3); return t > 0.66 ? B.GRAVEL : t < 0.3 ? B.DIRT : B.SAND; }
            return B.SAND;
        }

        // windswept surface noise: grass < 0.55 < stone < 0.68 < gravel; no trees from 0.53
        double Windswept(int x, int z, double y) => vn.f2(x * 0.11 + 21, z * 0.11 - 21) + (y - SEA) * 0.004;

        // the 3 filler blocks under the top (0 = keep stone)
        int FillBlock(int ob, int top, int x, int y, int z)
        {
            switch (ob)
            {
                case BI.BADLANDS: return Band(x, y, z);
                case BI.WOODED_BADLANDS: return top == B.GRASS ? B.DIRT : Band(x, y, z);
                case BI.DESERT: return B.SANDSTONE;
                case BI.MANGROVE_SWAMP: return B.MUD;
                case BI.BEACH: case BI.SNOWY_BEACH: case BI.OCEAN: case BI.RIVER: case BI.WARM_OCEAN: case BI.LUKEWARM_OCEAN: return B.SAND;
                case BI.FROZEN_RIVER: return top == B.GRASS ? B.DIRT : B.SAND;
                case BI.STONY_PEAKS: case BI.JAGGED_PEAKS: case BI.SNOWY_SLOPES: case BI.FROZEN_PEAKS: case BI.STONY_SHORE:
                case BI.COLD_OCEAN: case BI.FROZEN_OCEAN: return 0;
                default: return B.DIRT;
            }
        }

        int Band(int x, int y, int z)
        {
            int n = TERRA_BANDS.Length;
            return TERRA_BANDS[((y + (int)Math.Floor(nMisc.n2(x / 60.0, z / 60.0) * 3)) % n + n) % n];
        }

        // steep slopes of mountain biomes are bare stone (snowy peaks keep their snow above y 150)
        bool BareSlope(int bio, int x, int z, int y)
        {
            var P = BPROP[bio]; int b = P.@base;
            if (!(P.mountain || b == BI.WINDSWEPT_HILLS || b == BI.MEADOW || b == BI.GROVE || b == BI.CHERRY_GROVE)) return false;
            if (P.snow != 0 && y > 150) return false;
            double s = Math.Max(Math.Abs(ColInfoAt(x + 1, z).h - ColInfoAt(x - 1, z).h), Math.Abs(ColInfoAt(x, z + 1).h - ColInfoAt(x, z - 1).h));
            return s > 3.2;
        }

        void Surface()
        {
            var ids = this.ids; int x0 = this.x0, z0 = this.z0;
            // highest layer holding anything: the column scans start there instead of at the world top
            int hi = WORLD_MAX_Y - 1;
            for (; hi > WORLD_MIN_Y; hi--)
            {
                int o = (hi - WORLD_MIN_Y) << 8;
                int i = 0;
                while (i < 256 && ids[o + i] == 0) i++;
                if (i < 256) break;
            }
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                int bio = BIO[z * 16 + x]; var P = BPROP[bio]; int ob = ORIG_BIOME[bio];
                int y = hi;
                while (y > WORLD_MIN_Y && !IsStoneLike(ids[CI(x, y, z)])) y--;
                TOP[z * 16 + x] = (short)y;
                if (y <= WORLD_MIN_Y) continue;
                bool underwater = ids[CI(x, y + 1, z)] == B.WATER;
                int wx = x0 + x, wz = z0 + z; double h = ColInfoAt(wx, wz).h;
                int top = SurfTop(ob, wx, wz, y, h), fill = -1;
                if (!underwater)
                {
                    double sn = nSurf.n2(wx / 16.0, wz / 16.0);
                    if (BareSlope(bio, wx, wz, y)) top = fill = B.STONE;
                    if (ob == BI.STONY_PEAKS && sn > 0.45) top = fill = B.CALCITE;
                }
                // Minecraft's swamp surface rule: ground at the water line becomes water where the swamp
                // noise is positive (pools); in mangrove swamps from two blocks lower
                if (PoolAt(ob, wx, wz, y))
                {
                    ids[CI(x, y, z)] = B.WATER;
                    y--; TOP[z * 16 + x] = (short)y;
                    top = ob == BI.SWAMP ? B.DIRT : B.MUD;
                }
                ids[CI(x, y, z)] = (ushort)top;
                for (int d = 1; d <= 3; d++)
                {
                    int i = CI(x, y - d, z);
                    if (!IsStoneLike(ids[i])) break;
                    int f = fill >= 0 ? fill : FillBlock(ob, top, wx, y - d, wz);
                    if (f != 0) ids[i] = (ushort)f;
                }
                // badlands: the terracotta bands continue down the exposed cliff, as in Minecraft
                if (P.badlands && !underwater && top != B.STONE)
                {
                    for (int yy = y - 4; yy > SEA - 8 && yy > y - 60; yy--)
                    {
                        int i = CI(x, yy, z);
                        if (ids[i] != B.STONE) break;
                        ids[i] = (ushort)Band(wx, yy, wz);
                    }
                }
            }
        }

        void Caves()
        {
            var ids = this.ids; int x0 = this.x0, z0 = this.z0; var TOP = this.TOP; var BD = beard;
            int maxTop = WORLD_MIN_Y;
            for (int i = 0; i < 256; i++) if (TOP[i] > maxTop) maxTop = TOP[i];
            int rowsN = Math.Min(96, (int)Math.Floor((maxTop + 4 - WORLD_MIN_Y) / 4.0) + 1);
            // 5 channels on 5x(rows+1)x5 grid
            int NY = rowsN + 1;
            var G = new float[25 * NY * 5];
            for (int k = 0; k < 5; k++) for (int i = 0; i < 5; i++) for (int j = 0; j < NY; j++)
            {
                int x = x0 + i * 4, z = z0 + k * 4, y = WORLD_MIN_Y + j * 4;
                int o = ((k * 5 + i) * NY + j) * 5;
                G[o] = (float)nCheese.f3(x / 72.0, y / 40.0, z / 72.0, 2);
                G[o + 1] = (float)nSp1.n3(x / 52.0, y / 34.0, z / 52.0);
                G[o + 2] = (float)nSp2.n3(x / 52.0, y / 34.0, z / 52.0);
                if (y < 40) { G[o + 3] = (float)nNd1.n3(x / 26.0, y / 22.0, z / 26.0); G[o + 4] = (float)nNd2.n3(x / 26.0, y / 22.0, z / 26.0); }
                else { G[o + 3] = 1; G[o + 4] = 1; }
            }
            // a 4x4x4 cell can hold a cave only if its 8 corner values allow one
            var F = new byte[16 * NY]; var mn = new float[5]; var mx = new float[5];
            for (int ck = 0; ck < 4; ck++) for (int ci = 0; ci < 4; ci++) for (int j = 0; j < NY - 1; j++)
            {
                for (int c = 0; c < 5; c++) { mn[c] = float.PositiveInfinity; mx[c] = float.NegativeInfinity; }
                for (int q = 0; q < 8; q++)
                {
                    int o = (((ck + (q >> 2)) * 5 + ci + ((q >> 1) & 1)) * NY + j + (q & 1)) * 5;
                    for (int c = 0; c < 5; c++) { float g = G[o + c]; if (g < mn[c]) mn[c] = g; if (g > mx[c]) mx[c] = g; }
                }
                bool Near0(int c, double t) => mn[c] < t && mx[c] > -t;
                // cheese threshold is at least 0.45; spaghetti radius^2 at most 0.0082; noodles 0.0022
                F[(ck * 4 + ci) * NY + j] = (byte)(mx[0] > 0.449 || (Near0(1, 0.091) && Near0(2, 0.091)) || (Near0(3, 0.047) && Near0(4, 0.047)) ? 1 : 0);
            }
            var v = new float[5];
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                int top = TOP[z * 16 + x];
                if (top <= WORLD_MIN_Y + 5) continue;
                int ci = x >> 2, ck = z >> 2; double fx = (x & 3) / 4.0, fz = (z & 3) / 4.0;
                int b00 = ((ck * 5 + ci) * NY) * 5, b10 = ((ck * 5 + ci + 1) * NY) * 5, b01 = (((ck + 1) * 5 + ci) * NY) * 5, b11 = (((ck + 1) * 5 + ci + 1) * NY) * 5;
                bool wet = ids[CI(x, top + 1, z)] == B.WATER;
                int wx = x0 + x, wz = z0 + z;
                double spR = 0.0042 + 0.004 * (nMisc.n2(wx / 90.0, wz / 90.0) + 1) * 0.5;
                for (int y = Math.Min(top, WORLD_MIN_Y + (NY - 1) * 4 - 1); y > WORLD_MIN_Y + 4; y--)
                {
                    int i = CI(x, y, z);
                    int id = ids[i];
                    if (id == 0 || id == B.WATER || id == B.BEDROCK || id == B.ICE || id == B.PACKED_ICE) continue;
                    int depth = top - y;
                    if (wet && depth < 7) continue;
                    // the beard of a village piece fills caves under it
                    if (BD != null && y >= BD.y0 && y <= BD.y1 && BD.A[(y - BD.y0) * 256 + z * 16 + x] > 0.05) continue;
                    int j = (y - WORLD_MIN_Y) >> 2; double fy = ((y - WORLD_MIN_Y) & 3) / 4.0;
                    if (F[(ck * 4 + ci) * NY + j] == 0) continue;
                    for (int c = 0; c < 5; c++)
                    {
                        double a0 = G[b00 + j * 5 + c], a1 = G[b10 + j * 5 + c], a2 = G[b01 + j * 5 + c], a3 = G[b11 + j * 5 + c];
                        double c0 = G[b00 + (j + 1) * 5 + c], c1 = G[b10 + (j + 1) * 5 + c], c2 = G[b01 + (j + 1) * 5 + c], c3 = G[b11 + (j + 1) * 5 + c];
                        double l0 = a0 + (a1 - a0) * fx, l1 = a2 + (a3 - a2) * fx, u0 = c0 + (c1 - c0) * fx, u1 = c2 + (c3 - c2) * fx;
                        double lo = l0 + (l1 - l0) * fz, hi = u0 + (u1 - u0) * fz;
                        v[c] = (float)(lo + (hi - lo) * fy);
                    }
                    double v0 = v[0], v1 = v[1], v2 = v[2], v3 = v[3], v4 = v[4];
                    bool cave = false;
                    // cheese: big caverns deep down
                    double cheeseT = 0.6 - 0.12 * Math.Min(1, Math.Max(0, (depth - 12) / 70.0)) + (y < 0 ? -0.03 : 0);
                    if (depth > 12 && v0 > cheeseT) cave = true;
                    // spaghetti tunnels (may open to the surface)
                    if (!cave && v1 * v1 + v2 * v2 < spR * (depth < 3 ? 0.6 : 1)) cave = true;
                    // noodles
                    if (!cave && depth > 10 && v3 * v3 + v4 * v4 < 0.0022) cave = true;
                    if (!cave) continue;
                    // water below sea level is decided afterwards by aquifer()
                    ids[i] = (ushort)(y <= -55 ? B.LAVA : 0);
                    meta[i] = 0;
                }
                // floors near the surface (under a thin roof or an overhang) get the biome's top block
                {
                    int bio = BIO[z * 16 + x], ob = ORIG_BIOME[bio];
                    int sd = (int)Math.Floor(nSurf.n2(wx / 64.0 + 311.5, wz / 64.0 - 207.25) * 2.75 + 3 + Hash.Hash2(unchecked(seed + 61), wx, wz) * 0.25);
                    int lo = Math.Max(SEA, top + sd - 8);
                    for (int y = top - 1; y >= lo; y--)
                    {
                        int f = ids[CI(x, y, z)];
                        if (ids[CI(x, y + 1, z)] != 0 || !(IsStoneLike(f) || f == B.DIRT || f == B.SAND || f == B.RED_SAND || f == B.SANDSTONE || f == B.MUD || f == B.GRAVEL)) continue;
                        double h = ColInfoAt(wx, wz).h;
                        int t2 = BareSlope(bio, wx, wz, y) ? B.STONE : SurfTop(ob, wx, wz, y, h);
                        ids[CI(x, y, z)] = (ushort)t2;
                        for (int d = 1; d <= 3; d++)
                        {
                            int i = CI(x, y - d, z);
                            if (!IsStoneLike(ids[i])) break;
                            int fb = FillBlock(ob, t2, wx, y - d, wz);
                            if (fb != 0) ids[i] = (ushort)fb;
                        }
                    }
                }
                // a cave that opened the surface exposes filler (dirt, sand...): the new top gets the biome's surface block
                int ny = WORLD_MAX_Y - 1;
                while (ny > WORLD_MIN_Y && ids[CI(x, ny, z)] == 0) ny--;
                int nt = ids[CI(x, ny, z)];
                if (ny >= SEA && ny < top && (nt == B.DIRT || nt == B.SAND || nt == B.SANDSTONE || nt == B.MUD))
                {
                    int bio = BIO[z * 16 + x];
                    int t2 = BareSlope(bio, wx, wz, ny) ? B.STONE : SurfTop(ORIG_BIOME[bio], wx, wz, ny, ColInfoAt(wx, wz).h);
                    if (t2 != B.STONE || nt == B.DIRT) ids[CI(x, ny, z)] = (ushort)t2;
                }
                TOP[z * 16 + x] = (short)(ids[CI(x, ny, z)] == B.WATER ? top : ny);
            }
            Aquifer();
        }

        // Simplified Minecraft aquifer: open space below sea level holds water only near the surface
        // (within 12 blocks under the column's terrain height); deeper cavities stay dry, with a stone
        // barrier where a dry cave meets that water zone.
        void Aquifer()
        {
            var ids = this.ids; int x0 = this.x0, z0 = this.z0;
            var lo = aqLo ?? (aqLo = new short[18 * 18]);
            for (int z = -1; z <= 16; z++) for (int x = -1; x <= 16; x++) lo[(z + 1) * 18 + x + 1] = (short)(Math.Ceiling(ColInfoAt(x0 + x, z0 + z).h) - 12);
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                int k = (z + 1) * 18 + x + 1, L = lo[k];
                int side = Math.Min(Math.Min(lo[k - 1], lo[k + 1]), Math.Min(lo[k - 18], lo[k + 18]));
                for (int y = SEA; y > -55; y--)
                {
                    int i = CI(x, y, z), id = ids[i];
                    if (id != 0 && id != B.WATER) continue;
                    if (y >= L) { ids[i] = B.WATER; continue; }
                    // below this column's zone: a barrier where the zone above or beside it holds water
                    ids[i] = (ushort)(y == L - 1 || y >= side ? (y < 0 ? B.DEEPSLATE : B.STONE) : 0);
                }
            }
        }

        // ------------------------------------------------------------ amethyst geodes
        Geode GeodeOf(int sx, int sz)
        {
            long key = K16(sx, sz);
            var C = geodeCache ?? (geodeCache = new Dictionary<long, Geode>());
            if (C.TryGetValue(key, out var cached)) return cached;
            Geode G = null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 911), sx, sz));
            if (rnd.Next() < 1.0 / 20)
            {
                int ox = sx * 16 + (int)(rnd.Next() * 16), oz = sz * 16 + (int)(rnd.Next() * 16), oy = -50 + (int)(rnd.Next() * 71);
                double rx = 4.5 + rnd.Next() * 2, ry = 4 + rnd.Next() * 2, rz = 4.5 + rnd.Next() * 2;
                if (oy + ry + 8 < ColInfoAt(ox, oz).h)
                {
                    bool crack = rnd.Next() < 0.85; int side = (int)(rnd.Next() * 4);
                    G = new Geode
                    {
                        ox = ox, oy = oy, oz = oz, rx = rx, ry = ry, rz = rz, rmin = Math.Min(rx, Math.Min(ry, rz)), crack = crack, cdx = FACE4[side * 2], cdz = FACE4[side * 2 + 1],
                    };
                    G.nx = rnd.Next() * 1000; G.nz = rnd.Next() * 1000; G.seed = unchecked((int)Hash.Hash2i(unchecked(seed + 913), sx, sz));
                }
            }
            if (C.Count > 512) C.Clear();
            C[key] = G;
            return G;
        }

        // 0 outside, 1 crack, 2 hollow, 3 amethyst layer, 4 calcite, 5 smooth basalt
        int GeodeCell(Geode G, int x, int y, int z)
        {
            int dx = x - G.ox, dy = y - G.oy, dz = z - G.oz;
            if (Math.Abs(dx) > 8 || Math.Abs(dy) > 8 || Math.Abs(dz) > 8) return 0;
            double ripple = 1 + nGeode.n3((x + G.nx) / 5, y / 5.0, (z + G.nz) / 5) * 0.12;
            double ax = dx / G.rx, ay = dy / G.ry, az = dz / G.rz;
            double d = Math.Sqrt(ax * ax + ay * ay + az * az) * ripple;
            if (d > 1) return 0;
            double r = Math.Sqrt(dx * dx + dy * dy + dz * dz), depth = d > 0 ? r * (1 / d - 1) : 9;   // blocks in from the outline along the ray from the centre
            if (G.crack && depth < 3.2 && Math.Abs(dy) <= 2)
            {
                int outw = dx * G.cdx + dz * G.cdz, across = Math.Abs(dx * G.cdz - dz * G.cdx);
                if (outw > 0 && across <= 1) return 1;
            }
            return depth < 1 ? 5 : depth < 2 ? 4 : depth < 3 ? 3 : 2;
        }

        static readonly int[][] GEODE_DIRS = { new[] { 0, -1, 0, 3 }, new[] { 0, 1, 0, 2 }, new[] { 0, 0, -1, 5 }, new[] { 0, 0, 1, 4 }, new[] { -1, 0, 0, 1 }, new[] { 1, 0, 0, 0 } };

        void Geodes()
        {
            var ids = this.ids; var meta = this.meta; int x0 = this.x0, z0 = this.z0;
            // bud directions (down, up, north, south, west, east) and the face each bud stands on
            int[] BUDS = { B.SMALL_AMETHYST_BUD, B.MEDIUM_AMETHYST_BUD, B.LARGE_AMETHYST_BUD, B.AMETHYST_CLUSTER };
            for (int sz = cz - 1; sz <= cz + 1; sz++) for (int sx = cx - 1; sx <= cx + 1; sx++)
            {
                var G = GeodeOf(sx, sz);
                if (G == null) continue;
                int xa = Math.Max(x0 - 1, G.ox - 8), xb = Math.Min(x0 + 16, G.ox + 8), za = Math.Max(z0 - 1, G.oz - 8), zb = Math.Min(z0 + 16, G.oz + 8);
                if (xa > xb || za > zb) continue;
                int ya = Math.Max(WORLD_MIN_Y + 1, G.oy - 8), yb = G.oy + 8;
                for (int y = ya; y <= yb; y++) for (int z = za; z <= zb; z++) for (int x = xa; x <= xb; x++)
                {
                    int c = GeodeCell(G, x, y, z);
                    if (c == 0) continue;
                    bool inside = x >= x0 && x < x0 + 16 && z >= z0 && z < z0 + 16;
                    bool budding = c == 3 && Hash.Hash3(G.seed, x, y, z) < 0.08;
                    if (inside)
                    {
                        int i = CI(x - x0, y, z - z0);
                        ids[i] = (ushort)(c <= 2 ? 0 : c == 3 ? (budding ? B.BUDDING_AMETHYST : B.AMETHYST_BLOCK) : c == 4 ? B.CALCITE : B.SMOOTH_BASALT);
                        meta[i] = 0;
                    }
                    if (!budding || Hash.Hash3(unchecked(G.seed + 1), x, y, z) >= 0.33) continue;
                    foreach (var d in GEODE_DIRS)
                    {
                        int nx = x + d[0], ny = y + d[1], nz = z + d[2], nc = GeodeCell(G, nx, ny, nz);
                        if (nc != 1 && nc != 2) continue;
                        if (nx >= x0 && nx < x0 + 16 && nz >= z0 && nz < z0 + 16)
                        {
                            int i = CI(nx - x0, ny, nz - z0);
                            ids[i] = (ushort)BUDS[(int)(Hash.Hash3(unchecked(G.seed + 2), x, y, z) * 4)]; meta[i] = (byte)d[3];
                        }
                        break;
                    }
                }
            }
        }

        // ------------------------------------------------------------ fluid springs (waterfalls)
        static readonly HashSet<int> WROCK = new HashSet<int> { B.STONE, B.GRANITE, B.DIORITE, B.ANDESITE, B.DEEPSLATE, B.TUFF, B.CALCITE, B.DIRT, B.SNOW, B.PACKED_ICE };
        static readonly HashSet<int> LROCK = new HashSet<int> { B.STONE, B.GRANITE, B.DIORITE, B.ANDESITE, B.DEEPSLATE, B.TUFF, B.CALCITE };

        void Springs()
        {
            var ids = this.ids; int x0 = this.x0, z0 = this.z0; var list = springList = new List<int>();
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 433), cx, cz));
            int Ni(int a, int b) => a + (int)(rnd.Next() * (b - a + 1));                  // Mth.nextInt(a, b)
            void TryAt(int x, int y, int z, int fluid, HashSet<int> rock)
            {
                if (x < 1 || x > 14 || z < 1 || z > 14 || y <= WORLD_MIN_Y || y >= WORLD_MAX_Y - 1) return;   // sides must be in this chunk
                int i = CI(x, y, z), at = ids[i];
                if (!rock.Contains(ids[i + 256]) || !rock.Contains(ids[i - 256])) return;
                if (at != 0 && !rock.Contains(at)) return;
                int r = 1, open = 0;                                                       // the one below is rock
                foreach (int j in new[] { i - 1, i + 1, i - 16, i + 16 }) { int n = ids[j]; if (rock.Contains(n)) r++; else if (n == 0) open++; }
                if (r != 4 || open != 1) return;
                ids[i] = (ushort)fluid; meta[i] = 0;
                list.Add(x0 + x); list.Add(y); list.Add(z0 + z);
            }
            for (int k = 0; k < 25; k++) { int x = Ni(0, 15), z = Ni(0, 15), y = Ni(WORLD_MIN_Y, 192); TryAt(x, y, z, B.WATER, WROCK); }
            for (int k = 0; k < 20; k++)
            {
                int x = Ni(0, 15), z = Ni(0, 15), lo = WORLD_MIN_Y, hi = WORLD_MAX_Y - 1 - 8;
                int a = Ni(lo + 8, hi), b = Ni(lo, a - 1), y = Ni(lo, b - 1 + 8);
                TryAt(x, y, z, B.LAVA, LROCK);
            }
        }

        // ------------------------------------------------------------ ores
        void Ores()
        {
            var ids = this.ids;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 77), cx, cz));
            void Vein(int ore, int deepOre, int n, int y, bool spread)
            {
                int x = (int)(rnd.Next() * 16), z = (int)(rnd.Next() * 16);
                for (int k = 0; k < n; k++)
                {
                    if (x >= 0 && x < 16 && z >= 0 && z < 16 && y > WORLD_MIN_Y && y < WORLD_MAX_Y)
                    {
                        int i = CI(x, y, z), id = ids[i];
                        if (id == B.STONE || id == B.GRANITE || id == B.DIORITE || id == B.ANDESITE) ids[i] = (ushort)ore;
                        else if ((id == B.DEEPSLATE || id == B.TUFF) && deepOre != 0) ids[i] = (ushort)deepOre;
                    }
                    double r = rnd.Next();
                    if (r < 0.33) x += rnd.Next() < 0.5 ? -1 : 1; else if (r < 0.66) z += rnd.Next() < 0.5 ? -1 : 1; else y += rnd.Next() < 0.5 ? -1 : 1;
                    if (spread && rnd.Next() < 0.3) { x += rnd.Next() < 0.5 ? -1 : 1; }
                }
            }
            int Tri(int a, int b) { double r1 = rnd.Next(); double r2 = rnd.Next(); return (int)Math.Floor(a + (b - a) * (r1 + r2) * 0.5 + 0.5); }
            int Uni(int a, int b) => (int)Math.Floor(a + (b - a) * rnd.Next() + 0.5);
            // stone variety blobs
            void Blob(int id, int host2, int n, int y) => Vein(id, host2, n, y, true);
            for (int i = 0; i < 2; i++) Blob(B.GRANITE, 0, 40, Uni(0, 70));
            for (int i = 0; i < 2; i++) Blob(B.DIORITE, 0, 40, Uni(0, 70));
            for (int i = 0; i < 2; i++) Blob(B.ANDESITE, 0, 40, Uni(0, 70));
            for (int i = 0; i < 2; i++) Blob(B.TUFF, B.TUFF, 40, Uni(-64, 0));
            for (int i = 0; i < 5; i++) Blob(B.GRAVEL, B.GRAVEL, 26, Uni(-64, 120));
            for (int i = 0; i < 4; i++) Blob(B.DIRT, 0, 26, Uni(0, 150));
            // ores
            int b = BPROP[BIO[8 * 16 + 8]].@base;
            bool mountain = BPROP[b].mountain || b == BI.WINDSWEPT_HILLS || b == BI.MEADOW || b == BI.GROVE || b == BI.CHERRY_GROVE;
            for (int i = 0; i < 20; i++) Vein(B.COAL, B.DEEPSLATE_COAL_ORE, 14, Tri(0, 192), false);
            for (int i = 0; i < 12; i++) Vein(B.COAL, 0, 14, Uni(136, 300), false);
            for (int i = 0; i < 10; i++) Vein(B.IRON, B.DEEPSLATE_IRON_ORE, 8, Tri(-24, 56), false);
            for (int i = 0; i < 8; i++) Vein(B.IRON, B.DEEPSLATE_IRON_ORE, 4, Uni(-64, 72), false);
            if (mountain) for (int i = 0; i < 12; i++) Vein(B.IRON, 0, 8, Tri(80, 300), false);
            for (int i = 0; i < 14; i++) Vein(B.COPPER_ORE, B.DEEPSLATE_COPPER_ORE, 9, Tri(-16, 112), false);
            for (int i = 0; i < 4; i++) Vein(B.GOLD, B.DEEPSLATE_GOLD_ORE, 8, Tri(-64, 32), false);
            if (b == BI.BADLANDS || b == BI.WOODED_BADLANDS) for (int i = 0; i < 20; i++) Vein(B.GOLD, 0, 8, Uni(32, 256), false);
            for (int i = 0; i < 4; i++) Vein(B.REDSTONE_ORE, B.DEEPSLATE_REDSTONE_ORE, 7, Uni(-64, 15), false);
            for (int i = 0; i < 8; i++) Vein(B.REDSTONE_ORE, B.DEEPSLATE_REDSTONE_ORE, 7, Tri(-96, -32), false);
            for (int i = 0; i < 2; i++) Vein(B.LAPIS_ORE, B.DEEPSLATE_LAPIS_ORE, 6, Tri(-32, 32), false);
            for (int i = 0; i < 3; i++) Vein(B.LAPIS_ORE, B.DEEPSLATE_LAPIS_ORE, 6, Uni(-64, 64), false);
            for (int i = 0; i < 7; i++) Vein(B.DIAMOND, B.DEEPSLATE_DIAMOND_ORE, 4, Tri(-110, 16), false);
            if (mountain) for (int i = 0; i < 8; i++) Vein(B.EMERALD_ORE, B.DEEPSLATE_EMERALD_ORE, 1, Tri(-16, 300), false);
        }

        void Bedrock()
        {
            var ids = this.ids; int x0 = this.x0, z0 = this.z0;
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                ids[CI(x, WORLD_MIN_Y, z)] = B.BEDROCK;
                for (int k = 1; k < 5; k++) if (Hash.Hash3(unchecked(seed + 3), x0 + x, k, z0 + z) < (5 - k) / 5.0) ids[CI(x, WORLD_MIN_Y + k, z)] = B.BEDROCK;
            }
        }

        // ------------------------------------------------------------ features
        // world coords -> local; mode 0: only air/replaceable, 1: also leaves (for logs), 2: force
        void SetB(int x, int y, int z, int id, int m, int mode)
        {
            int lx = x - x0, lz = z - z0;
            if (lx < 0 || lx > 15 || lz < 0 || lz > 15 || y <= WORLD_MIN_Y || y >= WORLD_MAX_Y) return;
            int i = CI(lx, y, lz), cur = ids[i];
            if (mode != 2)
            {
                if (cur != 0 && (FLAGS[cur] & BF_REPLACE) == 0 && !(mode == 1 && (FLAGS[cur] & BF_LEAVES) != 0)) return;
                if (cur == B.WATER && mode == 0 && (FLAGS[id] & BF_LEAVES) == 0) return;
            }
            ids[i] = (ushort)id; meta[i] = (byte)m;
        }

        int GetB(int x, int y, int z)
        {
            int lx = x - x0, lz = z - z0;
            if (lx < 0 || lx > 15 || lz < 0 || lz > 15 || y <= WORLD_MIN_Y || y >= WORLD_MAX_Y) return -1;
            return ids[CI(lx, y, lz)];
        }

        // ---------------------------------------------------------------- trees
        // Tree attempts of a chunk (Minecraft's count / in-square placement per biome present in the chunk).
        Dictionary<long, string> TreeAttempts(int ccx, int ccz)
        {
            long key = K16(ccx, ccz);
            var C = attemptCache ?? (attemptCache = new Dictionary<long, Dictionary<long, string>>());
            if (C.TryGetValue(key, out var A)) return A;
            A = new Dictionary<long, string>();
            int x0 = ccx * 16, z0 = ccz * 16;
            var present = new List<int>(16);
            for (int i = 0; i < 16; i++) { int b = ColInfoAt(x0 + (i & 3) * 4 + 2, z0 + (i >> 2) * 4 + 2).biome; if (!present.Contains(b)) present.Add(b); }
            foreach (int bio in present)
            {
                var T = MC_TREES[bio];
                if (T == null) continue;
                var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 23 + bio * 7919), ccx, ccz));
                int n = T.count + (rnd.Next() < T.chance ? T.extra : 0);
                for (int k = 0; k < n; k++)
                {
                    int x = x0 + (int)(rnd.Next() * 16), z = z0 + (int)(rnd.Next() * 16);
                    string t = null;
                    foreach (var e in T.types) if (e.p >= 1 || rnd.Next() < e.p) { t = e.t; break; }
                    long ck = K2(x, z);
                    if (t == null || A.ContainsKey(ck) || ColInfoAt(x, z).biome != bio) continue;
                    A[ck] = t;
                }
            }
            if (C.Count > 4096) C.Clear();
            C[key] = A;
            return A;
        }

        string TreeCandidate(int x, int z)
        {
            long k = K2(x, z);
            if (!candCache.TryGetValue(k, out var v)) { v = TreeCandidate0(x, z); if (candCache.Count > 200000) candCache.Clear(); candCache[k] = v; }
            return v;
        }

        string TreeCandidate0(int x, int z)
        {
            var ci = ColInfoAt(x, z); int ob = ORIG_BIOME[ci.biome];
            double r0 = Hash.Hash2(unchecked(seed + 11), x * 3 + 11, z * 3 - 7);
            if (ob == BI.DESERT) return r0 < 0.006 ? "cactus" : null;
            if (ob == BI.MUSHROOM_FIELDS) return r0 < 0.004 ? "giant_red_mushroom" : r0 < 0.008 ? "giant_brown_mushroom" : null;
            if (ob == BI.ICE_SPIKES) return r0 < 0.01 ? "ice_spike" : null;
            TreeAttempts(x >> 4, z >> 4).TryGetValue(K2(x, z), out var t);
            if (t == null) return null;
            if (ci.biome == BI.WOODED_BADLANDS && ci.h < SEA + 10) return null;
            if (ob == BI.WINDSWEPT_HILLS && Windswept(x, z, ci.h) >= 0.53) return null;
            return t;
        }

        static int Room(string t) => t != null && TREE_ROOM.TryGetValue(t, out var r) ? r : 1;

        // Spacing: a candidate gives way to a higher-priority one whose trunk would share its columns
        string TreeAt(int x, int z)
        {
            string t = TreeCandidate(x, z);
            if (t == null) return null;
            double rk = Hash.Hash2(unchecked(seed + 24), x, z); int rt = Room(t);
            for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int d = Math.Max(dx < 0 ? -dx : dx, dz < 0 ? -dz : dz);
                if (d >= Math.Max(rt, 2)) continue;
                string tn = TreeCandidate(x + dx, z + dz);
                if (tn == null || d >= Math.Max(rt, Room(tn))) continue;
                if (Hash.Hash2(unchecked(seed + 24), x + dx, z + dz) < rk) return null;
            }
            return t;
        }

        // exact terrain top (density surface, before caves) with a small cache
        int GroundAt(int x, int z)
        {
            long k = K2(x, z);
            if (!gCache.TryGetValue(k, out int v)) { v = SurfaceAt(x, z); if (gCache.Count > 40000) gCache.Clear(); gCache[k] = v; }
            return v;
        }

        bool TreeSpot(int x, int z, out string t, out int sy)
        {
            t = TreeAt(x, z); sy = 0;
            if (t == null) return false;
            sy = GroundAt(x, z);
            if (sy >= WORLD_MAX_Y - 16) return false;
            var ci = ColInfoAt(x, z); int bio = ci.biome, ob = ORIG_BIOME[bio];
            // the block under the trunk, exactly as the surface rules will make it
            int top;
            if (PoolAt(ob, x, z, sy)) { sy--; top = ob == BI.SWAMP ? B.DIRT : B.MUD; }
            else top = BareSlope(bio, x, z, sy) ? B.STONE : SurfTop(ob, x, z, sy, ci.h);
            // most trees need dry land; swamp oaks and mangroves also grow in shallow water
            if (sy <= SEA && SEA - sy > (TREE_WATER.TryGetValue(t, out var tw) ? tw : -1)) return false;
            if (sy <= SEA && t == "swamp_oak" && top != B.DIRT && top != B.SAND && top != B.CLAY) return false;
            if (t == "cactus") { if (top != B.SAND && top != B.RED_SAND) return false; }
            else if (t == "ice_spike") { if (top != B.SNOW) return false; }
            else if (t == "mangrove" || t == "tall_mangrove") { if (top != B.MUD) return false; }
            else if (sy > SEA && top != B.GRASS && top != B.PODZOL && top != B.SNOW && top != B.MYCELIUM && top != B.MUD && top != B.DIRT) return false;
            if (t == "dark_oak" || t == "pale_oak" || t == "mega_spruce" || t == "mega_pine" || t == "mega_jungle")
            {
                // 2x2 trunk: every column needs ground no more than 3 blocks below the origin
                int g;
                g = GroundAt(x + 1, z); if (g < sy - 3 || g > sy + 3) return false;
                g = GroundAt(x, z + 1); if (g < sy - 3 || g > sy + 3) return false;
                g = GroundAt(x + 1, z + 1); if (g < sy - 3 || g > sy + 3) return false;
            }
            return true;
        }

        void Features()
        {
            int x0 = this.x0, z0 = this.z0; const int M = 9;   // the widest crowns (fancy oak, cherry, jungle branches) reach 9 out
            deco.Clear(); treeWrites = new List<int>();
            for (int z = z0 - M; z < z0 + 16 + M; z++) for (int x = x0 - M; x < x0 + 16 + M; x++)
            {
                // a tree reaches several chunks: build it once, then every chunk takes its part
                long k = K2(x, z);
                if (!treeCache.TryGetValue(k, out var r))
                {
                    r = TreeSpot(x, z, out var t, out int sy) && !InVillage(x, sy + 1, z) ? BuildTree(t, x, sy, z) : null;
                    if (treeCache.Count > 60000) treeCache.Clear();
                    treeCache[k] = r;
                }
                PlaceTree(r);
            }
            FlushTrees();
            Vegetation();
            SnowCover();
        }

        void GrowTree(string type, int x, int sy, int z) => PlaceTree(BuildTree(type, x, sy, z));

        // Builds the whole tree independent of the chunk: {w: [x,y,z,id,kind]..., d: decorations} or
        // null when it cannot grow (mangrove roots finding no ground). Deterministic per seed and spot.
        TreeResult BuildTree(string type, int x, int sy, int z)
        {
            tbN = 0; tbMap.Clear();
            int Key(int xx, int yy, int zz) => ((yy - WORLD_MIN_Y) * 64 + (xx - x + 32)) * 64 + (zz - z + 32);
            void Put(int xx, int yy, int zz, int id, int kind)
            {
                if (yy <= WORLD_MIN_Y || yy >= WORLD_MAX_Y) return;
                int k = Key(xx, yy, zz);
                // forced blocks (0 trunk, 3 roots) win over crown and soil
                if (tbMap.TryGetValue(k, out int j)) { if ((kind == 0 || kind == 3) && tbKind[j] != 0 && tbKind[j] != 3) { tbId[j] = id; tbKind[j] = (byte)kind; } return; }
                if (tbN >= TB_MAX) return;
                int n = tbN++;
                tbX[n] = xx; tbY[n] = yy; tbZ[n] = zz; tbId[n] = id; tbKind[n] = (byte)kind; tbMap[k] = n;
            }
            void Log(int xx, int yy, int zz, int id) => Put(xx, yy, zz, id, 0);          // 0: trunk (forced)
            void Root(int xx, int yy, int zz, int id) => Put(xx, yy, zz, id, 3);         // 3: roots (forced, but leaves do not count them as logs)
            void Soil(int xx, int yy, int zz) => Put(xx, yy, zz, B.DIRT, 2);             // 2: soil under a trunk
            void Layer(int lcx, int yy, int lcz, int r, int id, bool ragged)            // 1: crown
            {
                for (int dx = -r; dx <= r; dx++) for (int dz = -r; dz <= r; dz++)
                {
                    int ax = dx < 0 ? -dx : dx, az = dz < 0 ? -dz : dz;
                    if (r >= 2 && ax == r && az == r && (!ragged || Hash.Hash3(unchecked(seed + 98), lcx + dx, yy, lcz + dz) < 0.6)) continue;
                    if (r >= 3 && ax + az > r + 1) continue;
                    Put(lcx + dx, yy, lcz + dz, id, 1);
                }
            }
            double Gk(int k) => Hash.Hash2(unchecked(seed + 97), x * 31 + k, z * 17 - k);
            // sequential random numbers for the tree designs (seeded by the tree position, so
            // every chunk the tree reaches builds it identically)
            var rng = new Mulberry(Hash.Hash2i(unchecked(seed + 131), x, z));
            double Rnd() => rng.Next();
            int NextInt(int n) => (int)(Rnd() * n);
            double vineChance = 0; bool propagules = false, mossOnRoots = false, trunkVines = false, podzol = false, paleMoss = false, cocoa = false;
            void Straight(int h, int id) { for (int y = sy + 1; y <= sy + h; y++) Log(x, y, z, id); Soil(x, sy, z); }
            // 2x2 trunk, every column down to its own ground
            void Giant(int h, int id)
            {
                for (int d = 0; d < 4; d++)
                {
                    int gcx = x + (d & 1), gcz = z + (d >> 1), gy = d != 0 ? GroundAt(gcx, gcz) : sy;
                    for (int y = gy + 1; y <= sy + h; y++) Log(gcx, y, gcz, id);
                    Soil(gcx, gy, gcz);
                }
            }
            // a crown: the leaf cells of an ellipsoid around (cx, cy, cz), its outer shell thinned at random
            void Ball(double bcx, double bcy, double bcz, double rx, double ry, double rz, int id)
            {
                int X0 = (int)Math.Floor(bcx - rx), X1 = (int)Math.Ceiling(bcx + rx), Y0 = (int)Math.Floor(bcy - ry), Y1 = (int)Math.Ceiling(bcy + ry), Z0 = (int)Math.Floor(bcz - rz), Z1 = (int)Math.Ceiling(bcz + rz);
                for (int yy = Y0; yy <= Y1; yy++) for (int zz = Z0; zz <= Z1; zz++) for (int xx = X0; xx <= X1; xx++)
                {
                    double ex = (xx - bcx) / (rx + 0.5), ey = (yy - bcy) / (ry + 0.5), ez = (zz - bcz) / (rz + 0.5), d = ex * ex + ey * ey + ez * ez;
                    if (d > 1 || (d > 0.7 && Rnd() < 0.35)) continue;
                    Put(xx, yy, zz, id, 1);
                }
            }
            // a branch: logs along the line between two cells, laid along their main axis
            void Line(double ax, double ay, double az, double bx, double by, double bz, int id)
            {
                double dx = bx - ax, dy = by - ay, dz = bz - az, n = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
                int axis = Math.Abs(dy) >= Math.Abs(dx) && Math.Abs(dy) >= Math.Abs(dz) ? 0 : Math.Abs(dx) >= Math.Abs(dz) ? 1 : 2;
                int lcx = (int)Math.Floor(ax + 0.5), lcy = (int)Math.Floor(ay + 0.5), lcz = (int)Math.Floor(az + 0.5);
                Put(lcx, lcy, lcz, id | (axis << 16), 0);
                for (int k = 1; k <= n; k++)
                {
                    int px = (int)Math.Floor(ax + dx * k / n + 0.5), py = (int)Math.Floor(ay + dy * k / n + 0.5), pz = (int)Math.Floor(az + dz * k / n + 0.5);
                    // one coordinate at a time (sideways, then up or down): every log rests on the one before it by a face
                    while (lcx != px || lcy != py || lcz != pz)
                    {
                        int a;
                        if (lcx != px) { lcx += Math.Sign(px - lcx); a = 1; } else if (lcz != pz) { lcz += Math.Sign(pz - lcz); a = 2; } else { lcy += Math.Sign(py - lcy); a = axis; }
                        Put(lcx, lcy, lcz, id | ((axis == 0 ? 0 : a) << 16), 0);
                    }
                }
            }
            switch (type)
            {
                case "cactus":
                {
                    int n = 2 + (int)(Gk(1) * 2);
                    for (int y = sy + 1; y <= sy + n; y++) Log(x, y, z, B.CACTUS);
                    break;
                }
                // ---- own tree designs: trunks of logs, crowns as ragged ellipsoids, branches as log lines ----
                case "oak": case "swamp_oak": case "jungle":
                {
                    // a straight trunk under a round crown; one or two short side limbs with a tuft each
                    double[] P = type == "oak" ? new double[] { 4, 3, 2.6, 2.2, B.LOG, B.LEAVES } : type == "swamp_oak" ? new double[] { 4, 3, 3.4, 1.8, B.LOG, B.LEAVES } : new double[] { 5, 7, 2.6, 2.2, B.JUNGLE_LOG, B.JUNGLE_LEAVES };
                    int lg = (int)P[4], lv = (int)P[5];
                    int h = (int)P[0] + NextInt((int)P[1]), top = sy + h;
                    Straight(h, lg);
                    Ball(x, top, z, P[2], P[3], P[2], lv);
                    for (int k = NextInt(3); k > 0; k--)
                    {
                        int d = NextInt(4), by = sy + 2 + NextInt(Math.Max(1, h - 3)), ex = x + FACE4[d * 2] * 2, ez = z + FACE4[d * 2 + 1] * 2;
                        Line(x, by, z, ex, by + 1, ez, lg);
                        Ball(ex, by + 2, ez, 1.6, 1.2, 1.6, lv);
                    }
                    if (type != "oak") vineChance = 0.25;
                    if (type == "jungle") { trunkVines = true; cocoa = true; }
                    break;
                }
                case "birch": case "super_birch":
                {
                    // a slim white trunk with a tall narrow crown and a single leaf tip
                    int h = (type == "birch" ? 5 : 8) + NextInt(type == "birch" ? 3 : 5), top = sy + h;
                    Straight(h, B.BIRCH_LOG);
                    Ball(x, top - 1, z, 1.9, 2.8, 1.9, B.BIRCH_LEAVES);
                    Put(x, top + 2, z, B.BIRCH_LEAVES, 1);
                    break;
                }
                case "jungle_bush":
                {
                    Straight(1, B.JUNGLE_LOG);
                    Ball(x, sy + 2, z, 2.4, 1.4, 2.4, B.LEAVES);
                    break;
                }
                case "fancy_oak":
                {
                    // a tall oak whose trunk splits into 3-5 limbs climbing outward, each ending in a crown
                    int h = 6 + NextInt(6), top = sy + h;
                    Straight(h, B.LOG);
                    Ball(x, top + 1, z, 2.6, 2, 2.6, B.LEAVES);
                    int n = 3 + NextInt(3); double a0 = Rnd() * Math.PI * 2;
                    for (int k = 0; k < n; k++)
                    {
                        double a = a0 + k * Math.PI * 2 / n + (Rnd() - 0.5) * 0.6; int len = 3 + NextInt(3);
                        int by = sy + (int)Math.Floor(h * (0.45 + Rnd() * 0.4));
                        int ex = x + (int)Math.Floor(Math.Cos(a) * len + 0.5), ez = z + (int)Math.Floor(Math.Sin(a) * len + 0.5), ey = by + 2 + NextInt(3);
                        Line(x, by, z, ex, ey, ez, B.LOG);
                        Ball(ex, ey + 1, ez, 2.3, 1.7, 2.3, B.LEAVES);
                    }
                    break;
                }
                case "spruce": case "pine":
                {
                    // spruce: a cone of tiers down most of the trunk; pine: a small tight cone at the top only
                    bool spruce = type == "spruce"; int h = spruce ? 6 + NextInt(4) : 7 + NextInt(5), top = sy + h;
                    Straight(h, B.SPRUCE_LOG);
                    Put(x, top + 1, z, B.SPRUCE_LEAVES, 1);
                    int low = spruce ? sy + 2 + NextInt(2) : top - 3 - NextInt(2), maxR = spruce ? 3 : 2;
                    for (int y = top; y >= low; y--)
                    {
                        double t = (top - y) / (double)Math.Max(1, top - low); int tier = (top - y) % 2;     // every other row steps in: tiers
                        // at least a ring around the trunk on every row, up to the top: no bare log above the crown
                        int r = (int)Math.Max(1, Math.Floor(t * maxR + 0.3 + 0.5) - tier);
                        for (int dx = -r; dx <= r; dx++) for (int dz = -r; dz <= r; dz++)
                        {
                            int d = Math.Abs(dx) + Math.Abs(dz);
                            if (d > r + (r > 1 ? 1 : 0)) continue;
                            if (d == r + 1 && Rnd() < 0.5) continue;
                            Put(x + dx, y, z + dz, B.SPRUCE_LEAVES, 1);
                        }
                    }
                    break;
                }
                case "mega_spruce": case "mega_pine":
                {
                    // a 2x2 giant: a long cone (spruce) or a short crown at the very top (pine), podzol around
                    int h = 14 + NextInt(14), top = sy + h; bool mega = type == "mega_spruce";
                    Giant(h, B.SPRUCE_LOG);
                    int low = mega ? sy + (int)Math.Floor(h * 0.35) : top - 5 - NextInt(3); double maxR = mega ? 4.5 : 3.5; int cap = top + 2;
                    // the crown closes over the trunk: at least the 2x2 at the top, widening downward in tiers
                    for (int y = cap; y >= low; y--)
                    {
                        double t = (cap - y) / (double)Math.Max(1, cap - low), r = 1 + t * (maxR - 1) - ((cap - y) % 3 == 2 ? 0.8 : 0);
                        for (int dx = -5; dx <= 6; dx++) for (int dz = -5; dz <= 6; dz++)
                        {
                            double ex = dx - 0.5, ez = dz - 0.5;
                            if (ex * ex + ez * ez <= r * r + 0.5) Put(x + dx, y, z + dz, B.SPRUCE_LEAVES, 1);
                        }
                    }
                    podzol = true;
                    break;
                }
                case "mega_jungle":
                {
                    // a 2x2 giant with a broad flat crown and a few side limbs carrying their own crowns
                    int h = 12 + NextInt(16), top = sy + h;
                    Giant(h, B.JUNGLE_LOG);
                    Ball(x + 0.5, top + 1, z + 0.5, 4.5, 1.8, 4.5, B.JUNGLE_LEAVES);
                    for (int yy = top - 4 - NextInt(3); yy > sy + h * 0.5; yy -= 3 + NextInt(4))
                    {
                        double a = Rnd() * Math.PI * 2; int ex = x + (int)Math.Floor(0.5 + Math.Cos(a) * 4 + 0.5), ez = z + (int)Math.Floor(0.5 + Math.Sin(a) * 4 + 0.5);
                        Line(x + (Math.Cos(a) > 0 ? 1 : 0), yy, z + (Math.Sin(a) > 0 ? 1 : 0), ex, yy + 2, ez, B.JUNGLE_LOG);
                        Ball(ex, yy + 3, ez, 2.6, 1.3, 2.6, B.JUNGLE_LEAVES);
                    }
                    vineChance = 0.25; trunkVines = true;
                    break;
                }
                case "dark_oak": case "pale_oak":
                {
                    // a stout 2x2 trunk with root flares, a low wide umbrella crown; the pale oak in pale wood
                    // with moss hanging from the crown and now and then a creaking heart in the trunk
                    bool pale = type == "pale_oak"; int LG = pale ? B.PALE_OAK_LOG : B.DARK_LOG, LV = pale ? B.PALE_OAK_LEAVES : B.DARK_LEAVES;
                    if (pale) paleMoss = true;
                    int h = 5 + NextInt(4), top = sy + h;
                    Giant(h, LG);
                    foreach (var (dx, dz) in new[] { (-1, 0), (2, 1), (0, 2), (1, -1) })
                        if (Rnd() < 0.6) { int gy = GroundAt(x + dx, z + dz); if (gy >= sy - 1 && gy <= sy + 1) Put(x + dx, gy + 1, z + dz, LG, 0); }
                    if (pale && NextInt(4) == 0) { int hx = x + NextInt(2); int hy = sy + 2 + NextInt(Math.Max(1, h - 4)); int hz = z + NextInt(2); Log(hx, hy, hz, B.CREAKING_HEART); }
                    Ball(x + 0.5, top + 1, z + 0.5, 4.4, 1.7, 4.4, LV);
                    for (int k = 0; k < 2; k++)
                    {
                        int d = NextInt(4); double ex = x + 0.5 + FACE4[d * 2] * 3, ez = z + 0.5 + FACE4[d * 2 + 1] * 3;
                        Line(x + (FACE4[d * 2] > 0 ? 1 : 0), top - 1, z + (FACE4[d * 2 + 1] > 0 ? 1 : 0), Math.Floor(ex + 0.5), top, Math.Floor(ez + 0.5), LG);
                    }
                    break;
                }
                case "acacia":
                {
                    // a trunk that leans after a few blocks, a flat two-layer canopy; sometimes a second limb
                    int h = 4 + NextInt(3), lean = 2 + NextInt(2), d = NextInt(4), dx = FACE4[d * 2], dz = FACE4[d * 2 + 1];
                    Straight(h, B.ACACIA_LOG);
                    int tx = x + dx * lean, tz = z + dz * lean, ty = sy + h + lean;
                    Line(x, sy + h, z, tx, ty, tz, B.ACACIA_LOG);
                    void Canopy(int ccx, int ccy, int ccz, int r)
                    {
                        for (int ax = -r; ax <= r; ax++) for (int az = -r; az <= r; az++)
                        {
                            int q = ax * ax + az * az;
                            if (q <= r * r + 1 && !(q > r * r - 2 && Rnd() < 0.3)) Put(ccx + ax, ccy, ccz + az, B.ACACIA_LEAVES, 1);
                            if (q <= (r - 2) * (r - 2) + 1) Put(ccx + ax, ccy + 1, ccz + az, B.ACACIA_LEAVES, 1);
                        }
                    }
                    Canopy(tx, ty + 1, tz, 3);
                    if (Rnd() < 0.6)
                    {
                        int d2 = (d + 1 + NextInt(3)) & 3, bx = x + FACE4[d2 * 2] * 2, bz = z + FACE4[d2 * 2 + 1] * 2, by = sy + h - 1 + NextInt(2);
                        Line(x, by - 1, z, bx, by + 1, bz, B.ACACIA_LOG);
                        Canopy(bx, by + 2, bz, 2);
                    }
                    break;
                }
                case "cherry":
                {
                    // a short trunk forking into two or three arching limbs under one wide, airy pink crown
                    int h = 4 + NextInt(2), top = sy + h;
                    Straight(h, B.CHERRY_LOG);
                    int n = 2 + NextInt(2); double a0 = Rnd() * Math.PI * 2; var tips = new List<(int, int, int)>();
                    for (int k = 0; k < n; k++)
                    {
                        double a = a0 + k * Math.PI * 2 / n; int ex = x + (int)Math.Floor(Math.Cos(a) * 3 + 0.5), ez = z + (int)Math.Floor(Math.Sin(a) * 3 + 0.5), ey = top + 2 + NextInt(2);
                        Line(x, top, z, ex, ey, ez, B.CHERRY_LOG);
                        tips.Add((ex, ey, ez));
                    }
                    foreach (var (ex, ey, ez) in tips) Ball(ex, ey + 1, ez, 2.8, 1.5, 2.8, B.CHERRY_LEAVES);
                    foreach (var (ex, ey, ez) in tips) for (int k = 0; k < 6; k++)
                    {
                        double a = Rnd() * Math.PI * 2; int hx = ex + (int)Math.Floor(Math.Cos(a) * 3 + 0.5), hz = ez + (int)Math.Floor(Math.Sin(a) * 3 + 0.5);
                        if (tbMap.ContainsKey(Key(hx, ey, hz))) { Put(hx, ey - 1, hz, B.CHERRY_LEAVES, 1); if (Rnd() < 0.3) Put(hx, ey - 2, hz, B.CHERRY_LEAVES, 1); }
                    }
                    break;
                }
                case "giant_red_mushroom":
                {
                    int n = 4 + (int)(Gk(11) * 3);
                    for (int y = sy + 1; y <= sy + n; y++) Log(x, y, z, B.MUSHROOM_STEM);
                    Layer(x, sy + n + 1, z, 1, B.RED_MUSHROOM_BLOCK, false);
                    Layer(x, sy + n, z, 2, B.RED_MUSHROOM_BLOCK, true);
                    break;
                }
                case "giant_brown_mushroom":
                {
                    int n = 3 + (int)(Gk(12) * 2);
                    for (int y = sy + 1; y <= sy + n; y++) Log(x, y, z, B.MUSHROOM_STEM);
                    Layer(x, sy + n + 1, z, 3, B.BROWN_MUSHROOM_BLOCK, false);
                    break;
                }
                case "ice_spike":
                {
                    int n = 4 + (int)(Gk(13) * 7);
                    for (int y = sy + 1; y <= sy + n; y++) Log(x, y, z, B.PACKED_ICE);
                    // two base blocks for taller spikes, only where the ground is not lower
                    if (n > 5)
                    {
                        if (GroundAt(x + 1, z) >= sy) Log(x + 1, sy + 1, z, B.PACKED_ICE);
                        if (GroundAt(x, z + 1) >= sy) Log(x, sy + 1, z + 1, B.PACKED_ICE);
                    }
                    break;
                }
                case "mangrove": case "tall_mangrove":
                {
                    // the trunk stands on arched prop roots: from 2-3 blocks up, four to six roots bow outward
                    // and down into the mud; a round crown with hanging propagules and vines
                    bool tall = type == "tall_mangrove";
                    int oy = sy + 2 + NextInt(2);
                    bool RootCell(int xx, int yy, int zz)
                    {
                        int gy = GroundAt(xx, zz);
                        if (yy > gy) return true;
                        return ORIG_BIOME[ColInfoAt(xx, zz).biome] == BI.MANGROVE_SWAMP && yy > gy - 4;
                    }
                    for (int y = sy + 1; y < oy; y++) if (!RootCell(x, y, z)) return null;
                    int nr = 4 + NextInt(3); double a0 = Rnd() * Math.PI * 2;
                    for (int k = 0; k < nr; k++)
                    {
                        double a = a0 + k * Math.PI * 2 / nr, ca = Math.Cos(a), sa = Math.Sin(a);
                        // the root walks from the trunk one face at a time (out, then down), so each piece rests
                        // on the one before it; it ends where it enters the ground
                        int rcx = x, rcy = oy, rcz = z; bool done = false;            // from the side of the lowest trunk log
                        bool Step(int rx, int yy, int rz)
                        {
                            if (!RootCell(rx, yy, rz)) return true;
                            int gy = GroundAt(rx, rz), ob = ORIG_BIOME[ColInfoAt(rx, rz).biome];
                            bool wet = yy <= SEA && (yy > gy || (yy == gy && PoolAt(ob, rx, rz, gy)));
                            Root(rx, yy, rz, yy <= gy && !wet ? B.MUDDY_MANGROVE_ROOTS : wet ? B.MANGROVE_ROOTS_WET : B.MANGROVE_ROOTS);
                            return yy <= gy;
                        }
                        for (int s2 = 1, yy = oy - 1; s2 <= 7 && yy > sy - 4 && !done; s2++)
                        {
                            int rx = x + (int)Math.Floor(ca * Math.Min(s2, 3) + 0.5), rz = z + (int)Math.Floor(sa * Math.Min(s2, 3) + 0.5);
                            while (!done && (rcx != rx || rcy != yy || rcz != rz))
                            {
                                if (rcx != rx) rcx += Math.Sign(rx - rcx); else if (rcz != rz) rcz += Math.Sign(rz - rcz); else rcy += Math.Sign(yy - rcy);
                                done = Step(rcx, rcy, rcz);
                            }
                            if (s2 >= 2) yy--;
                        }
                    }
                    mossOnRoots = true;
                    int height = tall ? 6 + NextInt(6) : 3 + NextInt(4), top = oy + height;
                    for (int y = oy; y < top; y++) Log(x, y, z, B.MANGROVE_LOG);
                    Ball(x, top, z, tall ? 3.2 : 2.7, 2, tall ? 3.2 : 2.7, B.MANGROVE_LEAVES);
                    if (tall) for (int k = 0; k < 2; k++)
                    {
                        int d = NextInt(4), ex = x + FACE4[d * 2] * 3, ez = z + FACE4[d * 2 + 1] * 3, by = oy + 2 + NextInt(Math.Max(1, height - 4));
                        Line(x, by, z, ex, by + 2, ez, B.MANGROVE_LOG);
                        Ball(ex, by + 3, ez, 2, 1.4, 2, B.MANGROVE_LEAVES);
                    }
                    vineChance = 0.125; propagules = true;
                    break;
                }
            }
            // giant spruces: a ragged disc of podzol on the ground around the trunk
            if (podzol)
            {
                for (int dx = -5; dx <= 6; dx++) for (int dz = -5; dz <= 6; dz++)
                {
                    double q = (dx - 0.5) * (dx - 0.5) + (dz - 0.5) * (dz - 0.5);
                    if (q <= 30 * (0.6 + 0.4 * Rnd())) Put(x + dx, GroundAt(x + dx, z + dz), z + dz, B.PODZOL, 4);
                }
            }
            // crown connectivity (face neighbours) from the trunk through free crown cells
            int nT = tbN; var dist = tbDist; var q2 = tbQ;
            int qh = 0, qt = 0;
            for (int j = 0; j < nT; j++)
            {
                if (tbKind[j] == 0) { dist[j] = 0; q2[qt++] = j; }
                else dist[j] = 255;
            }
            while (qh < qt)
            {
                int j = q2[qh++], dj = dist[j];
                if (dj >= 6) continue;
                for (int f = 0; f < 6; f++)
                {
                    int xx = tbX[j] + FACE6[f * 3], yy = tbY[j] + FACE6[f * 3 + 1], zz = tbZ[j] + FACE6[f * 3 + 2];
                    if (!tbMap.TryGetValue(Key(xx, yy, zz), out int m)) continue;
                    if (tbKind[m] != 1 || dist[m] != 255 || (FLAGS[tbId[m] & 0xFFFF] & BF_LEAVES) == 0) continue;
                    if (SolidTerrain(xx, yy, zz)) { dist[m] = 254; continue; }
                    dist[m] = (byte)(dj + 1); q2[qt++] = m;
                }
            }
            // decorations on the surviving leaves: Minecraft's leave vine decorator (vines hanging down
            // up to five blocks) and hanging propagules under mangrove leaves (two free blocks below)
            var dec = new List<int>();
            if (vineChance != 0 || propagules || mossOnRoots || trunkVines || paleMoss || cocoa)
            {
                var taken = new HashSet<int>(); var props = new HashSet<int>();
                bool Free(int xx, int yy, int zz)
                {
                    int k = Key(xx, yy, zz);
                    // air only: below sea level every non-terrain cell is water
                    return yy > SEA && !tbMap.ContainsKey(k) && !taken.Contains(k) && !SolidTerrain(xx, yy, zz);
                }
                int[][] hang = { new[] { -1, 0, 8 }, new[] { 1, 0, 2 }, new[] { 0, -1, 1 }, new[] { 0, 1, 4 } };     // neighbour offset, vine side bit facing the leaf
                // Minecraft's above-root placement: moss carpet on half of the roots that reach the open air
                if (mossOnRoots) for (int j = 0; j < nT; j++)
                {
                    if (tbKind[j] != 3) continue;
                    int rx = tbX[j], ry = tbY[j] + 1, rz = tbZ[j];
                    if (Hash.Hash3(unchecked(seed + 133), rx, ry, rz) < 0.5 && Free(rx, ry, rz)) { dec.AddRange(new[] { rx, ry, rz, B.MOSS_CARPET, 0, rx, ry - 1, rz }); taken.Add(Key(rx, ry, rz)); }
                }
                // CocoaDecorator: one jungle tree in five carries pods on the lowest three logs of its trunk
                if (cocoa && Hash.Hash3(unchecked(seed + 143), x, sy, z) < 0.2)
                {
                    int low = int.MaxValue;
                    for (int j = 0; j < nT; j++) if (tbKind[j] == 0 && tbId[j] == B.JUNGLE_LOG && tbY[j] < low) low = tbY[j];
                    int[] cocoaFace = { 3, 1, 0, 2 };
                    for (int j = 0; j < nT; j++)
                    {
                        if (tbKind[j] != 0 || tbId[j] != B.JUNGLE_LOG || tbY[j] - low > 2) continue;
                        int lx = tbX[j], ly = tbY[j], lz = tbZ[j];
                        for (int s = 0; s < 4; s++)
                        {
                            int px = lx + hang[s][0], pz = lz + hang[s][1];
                            if (Hash.Hash3(unchecked(seed + 144 + s), lx, ly, lz) >= 0.25 || !Free(px, ly, pz)) continue;
                            int age = (int)(Hash.Hash3(unchecked(seed + 148), px, ly, pz) * 3);
                            dec.AddRange(new[] { px, ly, pz, B.COCOA, cocoaFace[s] | (age << 2), lx, ly, lz }); taken.Add(Key(px, ly, pz));
                        }
                    }
                }
                // TrunkVineDecorator: a vine on each free side of every log, two times in three
                if (trunkVines) for (int j = 0; j < nT; j++)
                {
                    if (tbKind[j] != 0) continue;
                    int lx = tbX[j], ly = tbY[j], lz = tbZ[j];
                    for (int s = 0; s < 4; s++)
                    {
                        if (Hash.Hash3(unchecked(seed + 140 + s), lx, ly, lz) < 1.0 / 3) continue;
                        int vx = lx + hang[s][0], vz = lz + hang[s][1];
                        if (!Free(vx, ly, vz)) continue;
                        dec.AddRange(new[] { vx, ly, vz, B.VINE, hang[s][2], lx, ly, lz }); taken.Add(Key(vx, ly, vz));
                    }
                }
                for (int j = 0; j < nT; j++)
                {
                    if (tbKind[j] != 1 || dist[j] > 6 || (FLAGS[tbId[j] & 0xFFFF] & BF_LEAVES) == 0) continue;
                    int lx = tbX[j], ly = tbY[j], lz = tbZ[j];
                    if (vineChance != 0) for (int s = 0; s < 4; s++)
                    {
                        if (Hash.Hash3(unchecked(seed + 134 + s), lx, ly, lz) >= vineChance) continue;
                        int vx = lx + hang[s][0], vz = lz + hang[s][1];
                        for (int yy = ly; yy > ly - 5 && Free(vx, yy, vz); yy--)
                        {
                            if (yy == ly) dec.AddRange(new[] { vx, yy, vz, B.VINE, hang[s][2], lx, ly, lz });   // held by the leaf
                            else dec.AddRange(new[] { vx, yy, vz, B.VINE, hang[s][2], vx, yy + 1, vz });        // by the vine above
                            taken.Add(Key(vx, yy, vz));
                        }
                    }
                    // pale moss: strands of 1-4 under a fifth of the crown's lowest leaves, the last one a tip
                    if (paleMoss && Hash.Hash3(unchecked(seed + 141), lx, ly, lz) < 0.2 && Free(lx, ly - 1, lz))
                    {
                        int nm = 1 + (int)(Hash.Hash3(unchecked(seed + 142), lx, ly, lz) * 4), yy = ly - 1;
                        while (nm > 1 && !Free(lx, yy - nm + 1, lz)) nm--;
                        for (int k = 0; k < nm; k++, yy--)
                        {
                            if (!Free(lx, yy, lz)) break;
                            dec.AddRange(new[] { lx, yy, lz, k == nm - 1 ? B.PALE_HANGING_MOSS_TIP : B.PALE_HANGING_MOSS, 0, lx, yy + 1, lz }); taken.Add(Key(lx, yy, lz));
                        }
                    }
                    if (propagules && Hash.Hash3(unchecked(seed + 139), lx, ly, lz) < 0.14 && Free(lx, ly - 1, lz) && Free(lx, ly - 2, lz))
                    {
                        // no other propagule in the 3x3 around it at the same height
                        bool near = false;
                        for (int dx = -1; dx <= 1 && !near; dx++) for (int dz = -1; dz <= 1; dz++) if (props.Contains(Key(lx + dx, ly - 1, lz + dz))) { near = true; break; }
                        if (!near) { dec.AddRange(new[] { lx, ly - 1, lz, B.MANGROVE_PROPAGULE, 0, lx, ly, lz }); taken.Add(Key(lx, ly - 1, lz)); props.Add(Key(lx, ly - 1, lz)); }
                    }
                }
            }
            var outw = new List<int>(nT * 5);
            for (int j = 0; j < nT; j++)
            {
                int kind = tbKind[j];
                // only leaves need the trunk within reach; other crown blocks (mushroom caps) always stay
                if (kind == 1 && dist[j] > 6 && (FLAGS[tbId[j] & 0xFFFF] & BF_LEAVES) != 0) continue;
                outw.Add(tbX[j]); outw.Add(tbY[j]); outw.Add(tbZ[j]); outw.Add(tbId[j]); outw.Add(kind);
            }
            return new TreeResult { w = outw.ToArray(), d = dec.ToArray() };
        }

        // The part of a built tree inside this chunk: queued while features() runs, so all trees of the
        // chunk are written phase by phase; written at once otherwise.
        void PlaceTree(TreeResult r)
        {
            if (r == null) return;
            var W = treeWrites; var w = r.w;
            for (int i = 0; i < w.Length; i += 5)
            {
                int xx = w[i], zz = w[i + 2];
                if (xx < x0 || xx > x0 + 15 || zz < z0 || zz > z0 + 15) continue;
                if (W != null) { W.Add(xx); W.Add(w[i + 1]); W.Add(zz); W.Add(w[i + 3]); W.Add(w[i + 4]); }
                else WriteTreeBlock(xx, w[i + 1], zz, w[i + 3], w[i + 4]);
            }
            deco.AddRange(r.d);
            if (W == null) { TreeDecorations(); deco.Clear(); }
        }

        // phase order: 0 trunks (forced), 1 crowns (free cells only), 2 soil under trunks, 3 roots, 4 ground cover
        void WriteTreeBlock(int xx, int yy, int zz, int idm, int kind)
        {
            int id = idm & 0xFFFF;
            if (kind == 0) SetB(xx, yy, zz, id, (int)((uint)idm >> 16), 2);
            else if (kind == 1) SetB(xx, yy, zz, id, 0, 0);
            else if (kind == 3)
            {
                int cur = GetB(xx, yy, zz);
                if (cur >= 0 && (FLAGS[cur] & BF_LEAVES) == 0 && !LOGKEY[cur] && !ROOTSKEY[cur]) SetB(xx, yy, zz, id, 0, 2);
            }
            else if (kind == 4)
            {
                // ground cover (podzol): replaces grass and dirt only
                int cur = GetB(xx, yy, zz);
                if (cur == B.GRASS || cur == B.DIRT || cur == B.COARSE_DIRT || cur == B.SNOWY_GRASS || cur == B.PODZOL) SetB(xx, yy, zz, id, 0, 2);
            }
            else
            {
                int cur = GetB(xx, yy, zz);
                if (cur >= 0 && cur != B.GRASS && cur != B.DIRT && cur != B.PODZOL && cur != B.COARSE_DIRT && cur != B.MUD && cur != B.SNOW) SetB(xx, yy, zz, B.DIRT, 0, 2);
            }
        }

        void FlushTrees()
        {
            var W = treeWrites;
            for (int phase = 0; phase <= 4; phase++)
                for (int i = 0; i < W.Count; i += 5) if (W[i + 4] == phase) WriteTreeBlock(W[i], W[i + 1], W[i + 2], W[i + 3], phase);
            treeWrites = null;
            TreeDecorations();
            deco.Clear();
        }

        // Tree decorations (vines, propagules, moss on roots) go in after every tree of the chunk
        void TreeDecorations()
        {
            var d = deco;
            for (int i = 0; i < d.Count; i += 8)
            {
                int sup = GetB(d[i + 5], d[i + 6], d[i + 7]), id = d[i + 3];
                if (sup >= 0)
                {
                    bool moss = id == B.PALE_HANGING_MOSS || id == B.PALE_HANGING_MOSS_TIP;
                    bool ok = id == B.MOSS_CARPET ? ROOTSKEY[sup] : id == B.VINE && d[i + 6] > d[i + 1] ? sup == B.VINE :
                        moss && sup == B.PALE_HANGING_MOSS ? true :
                        (FLAGS[sup] & BF_LEAVES) != 0 || (id == B.VINE && LOGKEY[sup]);
                    if (!ok) continue;
                }
                SetB(d[i], d[i + 1], d[i + 2], id, d[i + 4], 0);
            }
        }

        // terrain occupancy used for crown connectivity (density surface; air well above the height)
        bool SolidTerrain(int x, int y, int z)
        {
            if (y > ColInfoAt(x, z).h + 7) return false;
            return y <= GroundAt(x, z);
        }

        // ---------------------------------------------------------------- vegetation (maincraft rules)
        void Vegetation()
        {
            var ids = this.ids; var meta = this.meta; int x0 = this.x0, z0 = this.z0; var TOP = this.TOP;
            // pale garden floor: drifts of pale moss (a block noise picks the drifts), carpets on most of it
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                if (BIO[z * 16 + x] != BI.PALE_GARDEN) continue;
                int y = TOP[z * 16 + x], wx = x0 + x, wz = z0 + z, i = CI(x, y, z);
                if (y <= SEA || ids[i] != B.GRASS) continue;
                double v = nMisc.n2(wx * 0.06 + 517.3, wz * 0.06 - 291.9) + (Hash.Hash2(unchecked(seed + 143), wx, wz) - 0.5) * 0.3;
                if (v < 0.05) continue;
                ids[i] = B.PALE_MOSS_BLOCK; meta[i] = 0;
                if (ids[i + 256] == 0 && v > 0.15 && Hash.Hash2(unchecked(seed + 144), wx, wz) < 0.55) ids[i + 256] = B.PALE_MOSS_CARPET;
            }
            // underwater decoration, and the original's bamboo and mushroom-field columns
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                int y = TOP[z * 16 + x];
                if (y <= WORLD_MIN_Y + 1 || y >= WORLD_MAX_Y - 3) continue;
                int bio = BIO[z * 16 + x], ob = ORIG_BIOME[bio], wx = x0 + x, wz = z0 + z;
                int above = ids[CI(x, y + 1, z)];
                if (y < SEA) { if (above == B.WATER && !InVillage(wx, y + 1, wz)) WaterDecor(x, z, y, ob); continue; }
                if (above != 0 || (ob != BI.BAMBOO_JUNGLE && ob != BI.MUSHROOM_FIELDS)) continue;
                int plant = PlantAt(bio, ob, x, z, wx, wz, y), ground = ids[CI(x, y, z)];
                if (plant == 0) continue;
                bool mush = plant == B.BROWN_MUSHROOM || plant == B.RED_MUSHROOM;
                if (!(ground == B.GRASS || ground == B.PODZOL || (mush && ground == B.MYCELIUM) || (plant == B.BAMBOO_PLANT && ground == B.DIRT))) continue;
                int i1 = CI(x, y + 1, z);
                if (plant == B.BAMBOO_PLANT)
                {
                    int n = 3 + (int)(Hash.Hash2(unchecked(seed + 36), wx * 11 - 4, wz * 11 + 6) * 4);
                    for (int k = 0; k < n && y + 1 + k < WORLD_MAX_Y - 1 && ids[i1 + k * 256] == 0; k++) ids[i1 + k * 256] = (ushort)plant;
                }
                else if (SHAPE[plant] != SH.TALL) ids[i1] = (ushort)plant;
            }
            // Minecraft's vegetation patches of the biomes, from this chunk and its neighbours (a patch
            // spreads up to 7 blocks); every choice comes from the origin chunk's seed, so parts agree
            for (int sz = cz - 1; sz <= cz + 1; sz++) for (int sx = cx - 1; sx <= cx + 1; sx++)
            {
                var present = new List<int>(16);
                for (int i = 0; i < 16; i++) { int b = ColInfoAt(sx * 16 + (i & 3) * 4 + 2, sz * 16 + (i >> 2) * 4 + 2).biome; if (!present.Contains(b)) present.Add(b); }
                foreach (int bio in present)
                {
                    var list = MC_VEG[bio];
                    if (list == null) continue;
                    for (int f = 0; f < list.Length; f++)
                    {
                        var pl = list[f]; var P = PATCHES[pl.patch];
                        var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 4111 + bio * 131 + f * 7), sx, sz));
                        int n = pl.n >= 0 ? pl.n : 1;
                        if (pl.nt != null) n = nMisc.n2(sx * 16 / 200.0, sz * 16 / 200.0) < -0.8 ? pl.nt[0] : pl.nt[1];
                        if (pl.rn != null) n = Math.Max(0, pl.rn[0] + (int)(rnd.Next() * (pl.rn[1] - pl.rn[0] + 1)));
                        for (int k = 0; k < n; k++)
                        {
                            int ox = sx * 16 + (int)(rnd.Next() * 16), oz = sz * 16 + (int)(rnd.Next() * 16);
                            bool pass = pl.r == 0 || rnd.Next() < 1.0 / pl.r; uint pseed = (uint)(rnd.Next() * 4294967296.0);
                            if (!pass || ColInfoAt(ox, oz).biome != bio) continue;
                            Patch(P, ox, GroundAt(ox, oz) + 1, oz, pseed);
                        }
                    }
                }
            }
        }

        // one RandomPatchFeature: tries at random offsets; only cells of this chunk are written
        void Patch(PatchDef P, int ox, int oy, int oz, uint pseed)
        {
            if (ox < x0 - P.xz - 1 || ox > x0 + 16 + P.xz || oz < z0 - P.xz - 1 || oz > z0 + 16 + P.xz) return;
            var rnd = new Mulberry(pseed);
            int Ri(int n) => (int)(rnd.Next() * (n + 1));
            // forest flowers: one of lilac, rose bush, peony, lily of the valley for the whole patch
            int pickF = P.kind == "forest_flowers" ? new[] { B.LILAC, B.ROSE_BUSH, B.PEONY, B.LILY_OF_THE_VALLEY }[(int)(rnd.Next() * 4)] : 0;
            var ids = this.ids;
            for (int t = 0; t < P.tries; t++)
            {
                int x = ox + Ri(P.xz) - Ri(P.xz), y = oy + Ri(P.y) - Ri(P.y), z = oz + Ri(P.xz) - Ri(P.xz);
                double r1 = rnd.Next(), r2 = rnd.Next();
                int lx = x - x0, lz = z - z0;
                if (lx < 0 || lx > 15 || lz < 0 || lz > 15 || y <= WORLD_MIN_Y + 1 || y >= WORLD_MAX_Y - 3) continue;
                int i = CI(lx, y, lz);
                if (ids[i] != 0) continue;
                int below = ids[i - 256];
                int id = PatchPlant(P.kind, x, z, r1, r2, pickF);
                if (id == 0 || !PlantStays(id, below, lx, y, lz)) continue;
                if (SHAPE[id] == SH.TALL)
                {
                    if (ids[i + 256] != 0) continue;
                    ids[i] = (ushort)id; ids[i + 256] = (ushort)id; meta[i + 256] = 1;
                }
                else if (id == B.SUGAR_CANE)
                {
                    double ra = rnd.Next(); int h = 2 + (int)(ra * ((int)(rnd.Next() * 3) + 1));
                    for (int k = 0; k < h && ids[i + k * 256] == 0; k++) ids[i + k * 256] = (ushort)id;
                }
                else
                {
                    ids[i] = (ushort)id;
                    if (id == B.SWEET_BERRY_BUSH) meta[i] = 3;
                }
            }
        }

        static int[] FLOWER_FOREST_L, FLOWER_MEADOW_L;

        int PatchPlant(string kind, int x, int z, double r1, double r2, int pickF)
        {
            switch (kind)
            {
                case "grass": return B.TALL_GRASS;
                case "grass_jungle": return r1 < 0.75 ? B.TALL_GRASS : B.FERN;
                case "grass_taiga": return r1 < 0.2 ? B.TALL_GRASS : B.FERN;
                case "tall_grass": return B.TALL_GRASS_PLANT;
                case "large_fern": return B.LARGE_FERN;
                case "dead_bush": return B.DEAD_BUSH;
                case "sugar_cane": return B.SUGAR_CANE;
                case "pumpkin": return B.PUMPKIN;
                case "melon": return B.MELON;
                case "berry": return B.SWEET_BERRY_BUSH;
                case "sunflower": return B.SUNFLOWER;
                case "brown_mushroom": return B.BROWN_MUSHROOM;
                case "red_mushroom": return B.RED_MUSHROOM;
                case "blue_orchid": return B.BLUE_ORCHID;
                case "eyeblossom": return r1 < 0.5 ? B.CLOSED_EYEBLOSSOM : B.OPEN_EYEBLOSSOM;
                case "forest_flowers": return pickF;
                case "flower_default": return r1 < 2.0 / 3 ? B.POPPY : B.DANDELION;
                case "flower_plains":
                {
                    // NoiseThresholdProvider: tulips in the low noise areas, else poppy / bluet / daisy / cornflower
                    // one time in three, else dandelion
                    if (nMisc.n2(x * 0.005 + 71.3, z * 0.005 - 19.7) < -0.8) return new[] { B.ORANGE_TULIP, B.RED_TULIP, B.PINK_TULIP, B.WHITE_TULIP }[(int)(r2 * 4)];
                    return r1 < 1.0 / 3 ? new[] { B.POPPY, B.AZURE_BLUET, B.OXEYE_DAISY, B.CORNFLOWER }[(int)(r2 * 4)] : B.DANDELION;
                }
                case "flower_forest":
                {
                    // NoiseProvider(scale 0.020833334): the kind of flower changes smoothly across the forest
                    var L = FLOWER_FOREST_L ?? (FLOWER_FOREST_L = new[] { B.DANDELION, B.POPPY, B.ALLIUM, B.AZURE_BLUET, B.RED_TULIP, B.ORANGE_TULIP, B.WHITE_TULIP, B.PINK_TULIP, B.OXEYE_DAISY, B.CORNFLOWER, B.LILY_OF_THE_VALLEY });
                    double v = (nMisc.n2(x * 0.020833334 - 313.1, z * 0.020833334 + 77.7) + 1) / 2;
                    return L[Math.Max(0, Math.Min(L.Length - 1, (int)(v * L.Length)))];
                }
                case "flower_meadow":
                {
                    var L = FLOWER_MEADOW_L ?? (FLOWER_MEADOW_L = new[] { B.TALL_GRASS_PLANT, B.ALLIUM, B.POPPY, B.AZURE_BLUET, B.DANDELION, B.CORNFLOWER, B.OXEYE_DAISY, B.TALL_GRASS });
                    double v = (nMisc.n2(x * 0.020833334 + 911.1, z * 0.020833334 - 55.5) + 1) / 2;
                    // DualNoiseProvider in Minecraft: a few kinds at a time, changing across the meadow
                    return L[Math.Max(0, Math.Min(L.Length - 1, (int)((v + (r1 - 0.5) * 0.25) * L.Length)))];
                }
            }
            return 0;
        }

        // canSurvive of the plants on the block below (Minecraft's BushBlock and friends)
        bool PlantStays(int id, int below, int lx, int y, int lz)
        {
            bool dirt = below == B.GRASS || below == B.DIRT || below == B.PODZOL || below == B.COARSE_DIRT || below == B.ROOTED_DIRT ||
                below == B.MOSS_BLOCK || below == B.PALE_MOSS_BLOCK || below == B.MUD || below == B.SNOWY_GRASS || below == B.MYCELIUM;
            if (id == B.DEAD_BUSH) return dirt || below == B.SAND || below == B.RED_SAND || below == B.TERRACOTTA || TERRAKEY[below];
            if (id == B.PUMPKIN || id == B.MELON) return below == B.GRASS;
            if (id == B.BROWN_MUSHROOM || id == B.RED_MUSHROOM)
            {
                if (below == B.MYCELIUM || below == B.PODZOL) return true;
                if (OPAQUE[below] == 0) return false;
                // light 12 or less: only under cover (leaves, logs or terrain above)
                for (int yy = y + 1; yy < Math.Min(WORLD_MAX_Y - 1, y + 40); yy++) if (ids[CI(lx, yy, lz)] != 0) return true;
                return false;
            }
            if (id == B.SUGAR_CANE)
            {
                if (!(dirt || below == B.SAND || below == B.RED_SAND)) return false;
                for (int f = 0; f < 4; f++)
                {
                    int nx = lx + FACE4[f * 2], nz = lz + FACE4[f * 2 + 1];
                    if (nx >= 0 && nx < 16 && nz >= 0 && nz < 16) { int n = ids[CI(nx, y - 1, nz)]; if (n == B.WATER || n == B.ICE) return true; continue; }
                    if (y - 1 <= SEA && GroundAt(x0 + nx, z0 + nz) < y - 1) return true;
                }
                return false;
            }
            return dirt && below != B.MYCELIUM;
        }

        // Minecraft's freeze-top-layer step: in cold biomes, and wherever the height-adjusted temperature
        // is below 0.15, the top water freezes and snow settles on the ground and on leaves
        bool FrozenAt(int bio, int x, int y, int z)
        {
            if (BPROP[bio].cold == 2) return true;
            if (y <= 80) return BIOME_TEMP[bio] < 0.15;
            return BIOME_TEMP[bio] - ((vn.f2(x / 8.0, z / 8.0) - 0.5) * 16 + y - 80) * 0.00125 < 0.15;
        }

        void SnowCover()
        {
            var ids = this.ids; var TOP = this.TOP;
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                int bio = BIO[z * 16 + x]; bool cold = BPROP[bio].cold == 2;
                if (!cold && BIOME_TEMP[bio] - (TOP[z * 16 + x] + 48 - 80) * 0.00125 >= 0.15) continue;   // cannot freeze here
                int s = CI(x, SEA, z);
                if (cold && ids[s] == B.WATER && ids[s + 256] == 0) ids[s] = B.ICE;
                int y = Math.Min(WORLD_MAX_Y - 2, TOP[z * 16 + x] + 40);
                while (y > WORLD_MIN_Y && ids[CI(x, y, z)] == 0) y--;
                if (!cold && !FrozenAt(bio, x0 + x, y + 1, z0 + z)) continue;
                int t = ids[CI(x, y, z)];
                if (y >= WORLD_MAX_Y - 2 || ids[CI(x, y + 1, z)] != 0) continue;
                if (t == B.WATER) { ids[CI(x, y, z)] = B.ICE; continue; }           // a frozen lake or pool
                if (t == B.ICE || t == B.PACKED_ICE || t == B.SNOW || IsWaterId(t)) continue;
                if (OPAQUE[t] != 0 || (FLAGS[t] & BF_LEAVES) != 0) ids[CI(x, y + 1, z)] = B.SNOW_LAYER;
            }
        }

        // bamboo of the bamboo jungle and mushrooms of the mushroom fields, per column
        int PlantAt(int bio, int ob, int lx, int lz, int x, int z, int y)
        {
            double r = Hash.Hash2(unchecked(seed + 31), x * 7 + 3, z * 7 - 5), b2 = Hash.Hash2(unchecked(seed + 32), x * 11 - 7, z * 11 + 13);
            if (ob == BI.BAMBOO_JUNGLE) return r < 0.32 ? B.BAMBOO_PLANT : 0;
            if (ob == BI.MUSHROOM_FIELDS) return r < 0.22 ? (b2 < 0.5 ? B.BROWN_MUSHROOM : B.RED_MUSHROOM) : 0;
            return 0;
        }

        // underwater decoration of the original: icebergs and surface ice, coral reefs, kelp forests,
        // seagrass, lily pads. fy = floor block, water above it.
        void WaterDecor(int lx, int lz, int fy, int ob)
        {
            var ids = this.ids; var meta = this.meta; var V = vn; int x = x0 + lx, z = z0 + lz;
            int Col(int y) => CI(lx, y, lz);
            int n = Col(fy + 1), surf = Col(SEA), depth = SEA - fy;
            double i = Hash.Hash2(unchecked(seed + 41), x * 5 + 41, z * 5 - 23), a = Hash.Hash2(unchecked(seed + 42), x * 9 - 17, z * 9 + 31);
            switch (ob)
            {
                case BI.FROZEN_OCEAN:
                {
                    double c = V.f2(x * 0.02 + 911.1, z * 0.02 - 333.3);
                    if (c > 0.72 && depth >= 3)
                    {
                        double f = Math.Min(1, (c - 0.72) / 0.28); int top = SEA + (int)Math.Floor(f * 6 + 0.5);
                        int bot = SEA - 1 - (int)Math.Floor(f * 5 + 0.5);
                        if (bot < fy + 1) bot = fy + 1;
                        for (int y = bot; y <= top && y < WORLD_MAX_Y - 1; y++) ids[Col(y)] = B.PACKED_ICE;
                        if (top > SEA + 2 && ids[Col(top + 1)] == 0) ids[Col(top + 1)] = B.SNOW;
                    }
                    else if (ids[surf] == B.WATER) ids[surf] = B.ICE;
                    return;
                }
                case BI.WARM_OCEAN:
                {
                    if (ids[n] != B.WATER) return;
                    double v = V.f2(x * 0.03 + 123.4, z * 0.03 - 567.8);
                    if (depth >= 4 && v > 0.72)
                    {
                        int d = (int)Math.Min(3, 1 + Math.Floor((v - 0.72) * 14));
                        int L = 0;
                        for (; L < d && ids[n + L * 256] == B.WATER && fy + 2 + L < SEA; L++)
                            ids[n + L * 256] = (ushort)CORAL_BLOCKS[(int)Math.Floor(Hash.Hash2(unchecked(seed + 43), x * 3 + L * 7, z * 3 - L * 5) * 5) % 5];
                        int t = n + L * 256;
                        if (ids[t] == B.WATER)
                        {
                            if (a < 0.4) ids[t] = (ushort)CORAL_FANS[(int)Math.Floor(a * 12) % 5];
                            else if (a < 0.55) { ids[t] = B.SEA_PICKLE; meta[t] = (byte)((int)Math.Floor(i * 4) & 3); }
                        }
                        return;
                    }
                    if (i < 0.06) { ids[n] = B.SEA_PICKLE; meta[n] = (byte)((int)Math.Floor(a * 4) & 3); }
                    else if (i < 0.17) ids[n] = (ushort)CORAL_FANS[(int)Math.Floor(a * 5) % 5];
                    else if (i < 0.34) ids[n] = B.SEAGRASS;
                    return;
                }
                case BI.COLD_OCEAN: case BI.OCEAN: case BI.LUKEWARM_OCEAN:
                {
                    if (ids[n] != B.WATER || (depth >= 3 && Kelp(lx, lz, fy, x, z, i))) return;
                    if (i < (ob == BI.COLD_OCEAN ? 0.14 : ob == BI.LUKEWARM_OCEAN ? 0.3 : 0.24)) ids[n] = B.SEAGRASS;
                    return;
                }
                case BI.RIVER:
                    if (ids[n] == B.WATER && i < 0.12) ids[n] = B.SEAGRASS;
                    return;
                case BI.SWAMP: case BI.MANGROVE_SWAMP:
                    if (ids[n] == B.WATER && i < 0.1) ids[n] = B.SEAGRASS;
                    if (ids[surf] == B.WATER && ids[Col(SEA + 1)] == 0 && Hash.Hash2(unchecked(seed + 44), x * 7 - 9, z * 7 + 19) < 0.05)
                    {
                        ids[Col(SEA + 1)] = B.LILY_PAD; meta[Col(SEA + 1)] = (byte)(int)(a * 4);
                    }
                    return;
            }
        }

        bool Kelp(int lx, int lz, int fy, int x, int z, double i)
        {
            double r = vn.f2(x * 0.025 + 313.7, z * 0.025 - 191.3);
            if (r < 0.6) return false;
            if (i > 0.3 + (r - 0.6) / 0.4 * 0.55) return false;
            int n = SEA - 2 - fy;
            if (n > 25) n = 25;
            if (n < 1) return false;
            int top = Math.Min(SEA - 1, fy + 1 + (int)Math.Floor(Hash.Hash2(unchecked(seed + 45), x * 9 - 3, z * 9 + 7) * n));
            for (int y = fy + 1; y <= top; y++) { int k = CI(lx, y, lz); if (ids[k] != B.WATER) break; ids[k] = B.KELP; }
            return true;
        }

        // Cave biome of a 4x4x4 cell (0 none): own rules on the column climate and the depth below the surface.
        byte[] CaveBiomes()
        {
            var TOP = this.TOP; var G = cbio ?? (cbio = new byte[16 * (WORLD_H >> 2)]);
            Array.Clear(G, 0, G.Length);
            var o = _cbo ?? (_cbo = new Clim());
            for (int qz = 0; qz < 4; qz++) for (int qx = 0; qx < 4; qx++)
            {
                int wx = x0 + qx * 4 + 2, wz = z0 + qz * 4 + 2;
                Climate(wx, wz, o);
                int top = TOP[(qz * 4 + 2) * 16 + qx * 4 + 2];
                for (int qy = 1; qy < (WORLD_H >> 2) - 1; qy++)
                {
                    int y = WORLD_MIN_Y + qy * 4 + 2, depth = top - y;
                    if (depth < 14) continue;
                    double reg = nCave.n3(wx / 80.0, y / 44.0, wz / 80.0), reg2 = nCave.n3(wx / 80.0 + 300, y / 44.0, wz / 80.0 - 200);
                    int b = 0;
                    if (y < -18 && o.e < -0.2) { if (nCave.n3(wx / 96.0 + 700, y / 52.0, wz / 96.0 + 90) > -0.15) b = BI.DEEP_DARK; }
                    else if (o.hu > 0.3 && reg > -0.05) b = BI.LUSH_CAVES;
                    else if (o.c > 0.45 && reg2 > -0.05) b = BI.DRIPSTONE_CAVES;
                    else if (reg > 0.45) b = BI.LUSH_CAVES;              // small pockets anywhere
                    else if (reg2 > 0.48) b = BI.DRIPSTONE_CAVES;
                    G[(qy * 4 + qz) * 4 + qx] = (byte)b;
                }
            }
            return G;
        }

        static bool Stony(int b) => b == B.STONE || b == B.DEEPSLATE || b == B.TUFF || b == B.GRANITE || b == B.DIORITE || b == B.ANDESITE;

        void CaveDecor()
        {
            var ids = this.ids; var meta = this.meta; var TOP = this.TOP; int seed = this.seed;
            var G = CaveBiomes();
            // a pointed dripstone column of n from cell i stepping by d (-256 down from a ceiling, +256 up
            // from a floor): thick at the root, a tip at the free end
            void Spike(int i, int d, int n)
            {
                for (int k = 0; k < n; k++) if (ids[i + k * d] != 0) { n = k; break; }
                for (int k = 0; k < n; k++)
                {
                    int j = i + k * d, left = n - 1 - k;
                    ids[j] = B.POINTED_DRIPSTONE;
                    meta[j] = (byte)((left == 0 ? 1 : left == 1 ? 2 : k == 0 && n >= 4 ? 4 : 3) | (d > 0 ? 8 : 0));
                }
            }
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                int wx = x0 + x, wz = z0 + z, top = TOP[z * 16 + x];
                for (int y = top - 6; y > WORLD_MIN_Y + 6; y--)
                {
                    int i = CI(x, y, z);
                    if (ids[i] != 0) continue;
                    int below = ids[i - 256], above = ids[i + 256];
                    bool floor = Stony(below) || below == B.GRAVEL, ceil = Stony(above);
                    int cb = G[(((y - WORLD_MIN_Y) >> 2) * 4 + (z >> 2)) * 4 + (x >> 2)];
                    double r = Hash.Hash3(unchecked(seed + 51), wx, y, wz);
                    if (!floor && !ceil)
                    {
                        int bits = 0;
                        if (x < 15 && OPAQUE[ids[i + 1]] != 0) bits |= 8; if (x > 0 && OPAQUE[ids[i - 1]] != 0) bits |= 2;
                        if (z < 15 && OPAQUE[ids[i + 16]] != 0) bits |= 1; if (z > 0 && OPAQUE[ids[i - 16]] != 0) bits |= 4;
                        if (bits == 0) continue;
                        if (cb == BI.DEEP_DARK) { if (r < 0.12) { ids[i] = B.SCULK_VEIN; meta[i] = (byte)bits; } }
                        else if (cb == BI.LUSH_CAVES) { if (r < 0.05) { ids[i] = B.GLOW_LICHEN; meta[i] = (byte)bits; } }
                        else if (y < 40 && r < 0.012) { ids[i] = B.GLOW_LICHEN; meta[i] = (byte)bits; }
                        continue;
                    }
                    if (cb == BI.LUSH_CAVES)
                    {
                        if (floor)
                        {
                            ids[i - 256] = (ushort)(r < 0.04 ? B.CLAY : r < 0.07 ? B.ROOTED_DIRT : B.MOSS_BLOCK);
                            double r2 = Hash.Hash3(unchecked(seed + 56), wx, y, wz);
                            if (r2 < 0.04) ids[i] = (ushort)(r2 < 0.015 ? B.FLOWERING_AZALEA : B.AZALEA);
                            else if (r2 < 0.07 && ids[i + 256] == 0)
                            {
                                // big dripleaf: a stem of 0-2 and the leaf on top, facing a random side
                                int n = (int)(Hash.Hash3(unchecked(seed + 57), wx, y, wz) * 3), f = (int)(Hash.Hash3(unchecked(seed + 58), wx, y, wz) * 4);
                                int k = 0;
                                for (; k < n && ids[i + (k + 1) * 256] == 0; k++) { ids[i + k * 256] = B.BIG_DRIPLEAF_STEM; meta[i + k * 256] = (byte)f; }
                                ids[i + k * 256] = B.BIG_DRIPLEAF; meta[i + k * 256] = (byte)f;
                            }
                            else if (r2 < 0.1 && ids[i + 256] == 0)
                            {
                                int f = (int)(Hash.Hash3(unchecked(seed + 58), wx, y, wz) * 4);
                                ids[i] = B.SMALL_DRIPLEAF; meta[i] = (byte)f; ids[i + 256] = B.SMALL_DRIPLEAF; meta[i + 256] = (byte)(f | 4);
                            }
                            else if (r2 < 0.32) ids[i] = B.MOSS_CARPET;
                            else if (r2 < 0.5) ids[i] = (ushort)(r2 < 0.42 ? B.TALL_GRASS : B.FERN);
                        }
                        else
                        {
                            ids[i + 256] = B.MOSS_BLOCK;
                            double r2 = Hash.Hash3(unchecked(seed + 59), wx, y, wz);
                            if (r2 < 0.006) ids[i] = B.SPORE_BLOSSOM;
                            else if (r2 < 0.05) ids[i] = B.HANGING_ROOTS;
                            else if (r2 < 0.28)
                            {
                                int len = 1 + (int)(Hash.Hash3(unchecked(seed + 52), wx, y, wz) * 7);
                                for (int k = 0; k < len; k++)
                                {
                                    int j = i - k * 256;
                                    if (ids[j] != 0) break;
                                    ids[j] = B.CAVE_VINES; meta[j] = (byte)(Hash.Hash3(unchecked(seed + 53), wx, y - k, wz) < 0.18 ? 1 : 0);
                                }
                            }
                        }
                    }
                    else if (cb == BI.DRIPSTONE_CAVES)
                    {
                        if (floor)
                        {
                            if (r < 0.55) ids[i - 256] = B.DRIPSTONE;
                            if (r < 0.06) Spike(i, 256, 1 + (int)(Hash.Hash3(unchecked(seed + 54), wx, y, wz) * 5));
                        }
                        else
                        {
                            if (r < 0.55) ids[i + 256] = B.DRIPSTONE;
                            if (r < 0.09) Spike(i, -256, 1 + (int)(Hash.Hash3(unchecked(seed + 55), wx, y, wz) * 6));
                        }
                    }
                    else if (cb == BI.DEEP_DARK)
                    {
                        // sculk spreads in patches (a block noise), with sensors, shriekers and catalysts on it
                        double v = vn.f2(wx * 0.11 + y * 0.37, wz * 0.11 - y * 0.29);
                        if (floor)
                        {
                            if (v > 0.3)
                            {
                                ids[i - 256] = B.SCULK;
                                double r2 = Hash.Hash3(unchecked(seed + 60), wx, y, wz);
                                if (r2 < 0.02) ids[i] = B.SCULK_SENSOR;
                                else if (r2 < 0.026) ids[i] = B.SCULK_SHRIEKER;
                                else if (r2 < 0.03) ids[i - 256] = B.SCULK_CATALYST;
                            }
                            else if (v > 0.2) { ids[i] = B.SCULK_VEIN; meta[i] = 16; }
                        }
                        else if (v > 0.35) ids[i + 256] = B.SCULK;
                        else if (v > 0.25) { ids[i] = B.SCULK_VEIN; meta[i] = 32; }
                    }
                }
            }
        }

        // ---- the main thread's 'surface' query
        public int SurfaceHeight(int x, int z) => SurfaceAt(x, z);
        public int ColBiome(int x, int z) => ColInfoAt(x, z).biome;
    }
}
