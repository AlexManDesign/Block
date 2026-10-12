// Port of mesher.js: section mesher (worker side).
// Input: padded 18^3 arrays (ids, meta, light) around one 16^3 section.
// Output: packed quads (3 x uint32 per vertex, 4 vertices per quad) for three passes
// (solid, cutout, translucent) + face-to-face visibility graph for occlusion culling.
//
// vertex layout
//   w0: x | y<<10 | z<<20 | flags<<30      (positions in 1/32 block, section-local)
//   w1: layer | u<<11 | v<<16 | shade<<21 | overlay<<29 | uvRot<<30   (u,v in texels 0..16, shade 0..255)
//   w2: sky | blk<<8 | tint<<16              (light*16, 0..240; tint RGB565, 0xFFFF none)
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed class MeshJob
    {
        public int cx, cz, sy, ver;
        public ushort[] ids;     // 18^3 padded
        public byte[] meta;      // 18^3 padded
        public byte[] light;     // 18^3 padded (sky << 4 | block)
        public int leaves = 2;   // 2 fancy, 1 optimized, 0 fast, 3 shell
        public ushort[] tints;   // 768 RGB565 (grass, foliage, water per column)
        public bool sway = true;
        public object tag;       // owner data (the main thread's section)
    }

    public sealed class MeshResult
    {
        public int cx, cz, sy, ver;
        public uint[] data;          // 12 uints per quad
        public int[] counts = new int[3];
        public int[] gc = new int[3 * Mesher.FACE_GROUPS];
        public byte[] vis;
        public bool leafy;
        public MeshJob job;
    }

    sealed class QBuf
    {
        public uint[] d; public byte[] g; public int n;
        public QBuf(int q) { d = new uint[q * 12]; g = new byte[q]; n = 0; }
        public void Ensure()
        {
            if ((n + 2) * 12 > d.Length)
            {
                Array.Resize(ref d, d.Length * 2);
                Array.Resize(ref g, g.Length * 2);
            }
        }
    }

    sealed class BoxExtra
    {
        public bool cullSame;
        public double[] shear;
        public int[] rot;
        public double[][] uvOverride;
        public bool[] flipU;
    }

    public sealed class Mesher
    {
        public const int P = 18, SX = 1, SZ = 18, SY = 324;
        public static int Pidx(int x, int y, int z) => ((y + 1) * P + (z + 1)) * P + (x + 1);

        // face order: 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z
        public static readonly int[][] FN = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        public static readonly double[] FACE_SHADE = { 0.72, 0.72, 1.0, 0.55, 0.82, 0.82 };
        // corners of each face (unit cube), CCW from outside: BL, BR, TR, TL
        public static readonly int[][][] FC =
        {
            new[] { new[] { 1, 0, 1 }, new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 1, 1, 1 } },
            new[] { new[] { 0, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 1, 1 }, new[] { 0, 1, 0 } },
            new[] { new[] { 0, 1, 1 }, new[] { 1, 1, 1 }, new[] { 1, 1, 0 }, new[] { 0, 1, 0 } },
            new[] { new[] { 1, 0, 1 }, new[] { 0, 0, 1 }, new[] { 0, 0, 0 }, new[] { 1, 0, 0 } },
            new[] { new[] { 0, 0, 1 }, new[] { 1, 0, 1 }, new[] { 1, 1, 1 }, new[] { 0, 1, 1 } },
            new[] { new[] { 1, 0, 0 }, new[] { 0, 0, 0 }, new[] { 0, 1, 0 }, new[] { 1, 1, 0 } },
        };
        public static readonly int[] FACE_OF_DIR = { 4, 1, 5, 0 };        // horizontal dir (0 S,1 W,2 N,3 E) -> face index
        public static readonly int[] DIRX = { 0, -1, 0, 1 }, DIRZ = { 1, 0, -1, 0 };
        public static readonly int[] OPP_FACE = { 1, 0, 3, 2, 5, 4 };
        public static readonly int[] FACE_DELTA = new int[6];
        // AO sample offsets per face/vertex: [side1, side2, corner] as padded index deltas (relative to face neighbour)
        static readonly int[][][] AO_OFF = new int[6][][];
        static readonly double[] AO_CURVE = { 0.62, 0.7467, 0.8734, 1.0 };
        // Facing groups for directional culling: 0 -X, 1 +X, 2 -Y, 3 +Y, 4 -Z, 5 +Z, 6 anything else.
        public const int FACE_GROUPS = 7;

        // Minecraft's FaceInfo vertex order per face (FN order: +x -x +y -y +z -z); 1 = element max
        static readonly int[][][] MODEL_FACE_CORNERS =
        {
            new[] { new[] { 1, 1, 1 }, new[] { 1, 0, 1 }, new[] { 1, 0, 0 }, new[] { 1, 1, 0 } },
            new[] { new[] { 0, 1, 0 }, new[] { 0, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 1, 1 } },
            new[] { new[] { 0, 1, 0 }, new[] { 0, 1, 1 }, new[] { 1, 1, 1 }, new[] { 1, 1, 0 } },
            new[] { new[] { 0, 0, 1 }, new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 1, 0, 1 } },
            new[] { new[] { 0, 1, 1 }, new[] { 0, 0, 1 }, new[] { 1, 0, 1 }, new[] { 1, 1, 1 } },
            new[] { new[] { 1, 1, 0 }, new[] { 1, 0, 0 }, new[] { 0, 0, 0 }, new[] { 0, 1, 0 } },
        };

        // ---------------------------------------------------------------- shared texture tables
        public static readonly Dictionary<int, int> LAYER_BED_HEAD = new Dictionary<int, int>();
        public static int BED_LEG;
        // redstone dust layers (dot, line) and its colour per signal strength (RGB565)
        public static readonly int[] WIRE_TEX = { 0, 0 };
        public static readonly ushort[] WIRE_TINT = new ushort[16];
        // Biome-tinted textures: TINT_KIND[layer] = 1 grass, 2 foliage, 3 water, 4 grass side (dirt base +
        // tinted overlay); TINT_LAYER[layer] = the untinted layer the terrain draws instead.
        public static readonly byte[] TINT_KIND = new byte[4096];
        public static readonly ushort[] TINT_LAYER = new ushort[4096];
        static readonly (int, string)[] TINT_NAMES =
        {
            (1, "grass_block_top"), (1, "short_grass"), (1, "fern"), (1, "tall_grass_top"), (1, "tall_grass_bottom"),
            (1, "large_fern_top"), (1, "large_fern_bottom"), (2, "oak_leaves"), (2, "vine"), (2, "dark_oak_leaves"),
            (2, "jungle_leaves"), (2, "acacia_leaves"), (2, "mangrove_leaves"), (3, "water_still"), (3, "water_flow"), (4, "grass_block_side"),
        };
        // default colours (plains grass / foliage, default water) when a job carries no biome tints
        public static readonly ushort[] DEFAULT_TINTS = new ushort[768];
        // fast leaves (Minecraft's fast graphics): leaf layer -> its opaque variant, drawn in the solid pass
        public static readonly ushort[] OPAQUE_LEAF = new ushort[4096];
        // edge texture of each pane block (its glass's '<name>_pane_top'), 0: the pane's own texture
        public static ushort[] PANE_EDGE;
        // the flowing liquids' tiles (sides and sloping tops)
        public static int WATER_FLOW, LAVA_FLOW;

        public static ushort C565(uint v) => (ushort)(((v >> 19) << 11) | (((v >> 10) & 63) << 5) | ((v >> 3) & 31));

        static Mesher()
        {
            for (int f = 0; f < 6; f++) FACE_DELTA[f] = FN[f][0] * SX + FN[f][1] * SY + FN[f][2] * SZ;
            for (int f = 0; f < 6; f++)
            {
                var n = FN[f];
                int[] ax = n[0] != 0 ? new[] { 1, 2 } : n[1] != 0 ? new[] { 0, 2 } : new[] { 0, 1 };
                var arr = new int[4][];
                for (int v = 0; v < 4; v++)
                {
                    var c = FC[f][v];
                    var o1 = new int[3]; var o2 = new int[3];
                    o1[ax[0]] = c[ax[0]] != 0 ? 1 : -1;
                    o2[ax[1]] = c[ax[1]] != 0 ? 1 : -1;
                    int D(int[] o) => o[0] * SX + o[1] * SY + o[2] * SZ;
                    arr[v] = new[] { D(o1), D(o2), D(new[] { o1[0] + o2[0], o1[1] + o2[1], o1[2] + o2[2] }) };
                }
                AO_OFF[f] = arr;
            }
            for (int p = 0; p < 16; p++)
            {
                double f = p / 15.0, r = f * 0.6 + (p != 0 ? 0.4 : 0.3), g = Math.Max(0, f * f * 0.7 - 0.5), bl = Math.Max(0, f * f * 0.6 - 0.7);
                WIRE_TINT[p] = (ushort)(((int)JsMath.Round(r * 31) << 11) | ((int)JsMath.Round(g * 63) << 5) | (int)JsMath.Round(bl * 31));
            }
            for (int i = 0; i < 256; i++) { DEFAULT_TINTS[i] = C565(0x91BD59); DEFAULT_TINTS[256 + i] = C565(0x77AB2F); DEFAULT_TINTS[512 + i] = C565(0x3F76E4); }
        }

        public static void InitMesherTextures(IDictionary<string, int> layers)
        {
            int L(string n) => layers.TryGetValue(n, out var v) ? v : 0;
            Array.Clear(TINT_KIND, 0, TINT_KIND.Length); Array.Clear(OPAQUE_LEAF, 0, OPAQUE_LEAF.Length);
            WATER_FLOW = L("water_flow"); LAVA_FLOW = L("lava_flow");
            foreach (var kv in layers) if (layers.TryGetValue(kv.Key + "_opaque", out var op)) OPAQUE_LEAF[kv.Value] = (ushort)op;
            var names = new List<(int, string)>(TINT_NAMES);
            foreach (var t in TINT_NAMES) if (t.Item1 == 2) names.Add((2, t.Item2 + "_opaque"));
            foreach (var (k, n) in names)
            {
                if (!layers.TryGetValue(n, out var l) || !layers.TryGetValue(n + "_bt", out var u)) continue;
                TINT_KIND[l] = (byte)k; TINT_LAYER[l] = (ushort)u;
            }
            LAYER_BED_HEAD.Clear();
            for (int id = 1; id < NB; id++)
            {
                if (SHAPE[id] != SH.BED) continue;
                string key = B_KEY[id];
                string color = key == "BED" ? "" : "_" + key.Substring(4).ToLowerInvariant();
                if (layers.TryGetValue("bed_top" + color, out var l)) LAYER_BED_HEAD[id] = l;
            }
            BED_LEG = L("oak_planks");
            PANE_EDGE = new ushort[NBX];
            for (int id = 1; id < NB; id++)
            {
                if (SHAPE[id] != SH.PANE || TEXNAMES[id] == null) continue;
                if (layers.TryGetValue(TEXNAMES[id][0] + "_pane_top", out var l)) { PANE_EDGE[id] = (ushort)l; if (WET[id] != 0) PANE_EDGE[WET[id]] = (ushort)l; }
            }
            WIRE_TEX[0] = L("redstone_dust_dot"); WIRE_TEX[1] = L("redstone_dust_line");
        }

        // rotate a box (1/16 units) by quarter turns around the block's vertical axis: north -> east -> south -> west
        public static double[] RotBox(double[] b, int turns)
        {
            double x0 = b[0], z0 = b[2], x1 = b[3], z1 = b[5];
            for (int t = 0; t < turns; t++)
            {
                double nx0 = 16 - z1, nx1 = 16 - z0, nz0 = x0, nz1 = x1;
                x0 = nx0; x1 = nx1; z0 = nz0; z1 = nz1;
            }
            return new[] { x0, b[1], z0, x1, b[4], z1 };
        }

        // ---------------------------------------------------------------- instance
        uint uvRot;          // bit 30 of a vertex's second word: the shader turns its UV by 45 degrees (flowing liquid)
        readonly QBuf[] bufs = { new QBuf(8192), new QBuf(4096), new QBuf(2048) };
        readonly byte[] vis = new byte[4096];
        readonly int[] stack = new int[4096];
        bool fancyLeaves = true, leafCull, leafShell, leafy;
        int forceTint = -1;
        public bool sway = true;
        ushort[] ids; byte[] metaArr; byte[] light;
        ushort[] tints; int tcol;
        readonly float[] tpx = new float[4], tpy = new float[4], tpz = new float[4], tu = new float[4], tv = new float[4];
        readonly int[] sh = new int[4], sk = new int[4], bl = new int[4];
        readonly float[] bright = new float[4];
        readonly float[] MODEL_TMP = new float[12];
        int[] sortKey = new int[8192];
        readonly int[] paneTex = new int[6];
        readonly bool[] paneConn = new bool[4];
        readonly double[] flowV = new double[2];
        readonly int[] texs = new int[6];
        static readonly int[] FL_SWAY = { 0, 0, 1, 1 };

        // push one quad: positions in 1/16 units (converted to 1/32), uv in texels
        void Quad(QBuf buf, float[] px, float[] py, float[] pz, float[] us, float[] vs, int layer, int[] shades, int[] skys, int[] blks, int flagN, int[] flags, bool flip)
        {
            buf.Ensure();
            var d = buf.d;
            int o = buf.n * 12;
            // group from the winding normal of the emitted quad (front face = counter-clockwise)
            int a = flip ? 1 : 0, b = flip ? 2 : 1, c = flip ? 3 : 2;
            double e1x = (double)px[b] - px[a], e1y = (double)py[b] - py[a], e1z = (double)pz[b] - pz[a];
            double e2x = (double)px[c] - px[a], e2y = (double)py[c] - py[a], e2z = (double)pz[c] - pz[a];
            double nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
            int g = 6;
            bool Flat(float[] q) => q[0] == q[1] && q[1] == q[2] && q[2] == q[3] && q[0] >= 0 && q[0] <= 256;   // 1/16-block units, section = 0..256
            if (nx != 0 && ny == 0 && nz == 0 && Flat(px)) g = nx > 0 ? 1 : 0;
            else if (ny != 0 && nx == 0 && nz == 0 && Flat(py)) g = ny > 0 ? 3 : 2;
            else if (nz != 0 && nx == 0 && ny == 0 && Flat(pz)) g = nz > 0 ? 5 : 4;
            buf.g[buf.n] = (byte)g;
            // biome colour of the block's column; the grass side also flags the overlay pass in the shader
            uint tint = 0xFFFF, ovl = 0;
            int tk = layer >= 0 && layer < 4096 ? TINT_KIND[layer] : 0;
            if (forceTint >= 0) tint = (uint)forceTint;
            else if (tk != 0)
            {
                tint = tints[(tk == 4 ? 0 : tk - 1) * 256 + tcol];
                layer = TINT_LAYER[layer];
                if (tk == 4) ovl = 1u << 29;
            }
            for (int k = 0; k < 4; k++)
            {
                int v = flip ? (k + 1) & 3 : k;
                int x = (int)JsMath.Round(px[v] * 2.0), y = (int)JsMath.Round(py[v] * 2.0), z = (int)JsMath.Round(pz[v] * 2.0);
                if (x < 0) x = 0; if (y < 0) y = 0; if (z < 0) z = 0;
                int fl = flags == null ? flagN : flags[v];
                d[o++] = (uint)x | ((uint)y << 10) | ((uint)z << 20) | ((uint)fl << 30);
                double u = us[v], vv = vs[v];
                u = u < 0 ? 0 : u > 16 ? 16 : u; vv = vv < 0 ? 0 : vv > 16 ? 16 : vv;
                d[o++] = (uint)layer | ((uint)JsMath.Round(u) << 11) | ((uint)JsMath.Round(vv) << 16) | ((uint)shades[v] << 21) | ovl | uvRot;
                d[o++] = (uint)skys[v] | ((uint)blks[v] << 8) | (tint << 16);
            }
            buf.n++;
        }

        public MeshResult Mesh(MeshJob job)
        {
            var ids = job.ids; var meta = job.meta; var light = job.light;
            this.ids = ids; metaArr = meta; this.light = light;
            sway = job.sway;
            // leaves: 2 fancy, 1 optimized, 0 fast, 3 shell
            fancyLeaves = job.leaves != 0;
            leafCull = job.leaves == 1;
            leafShell = job.leaves == 3;
            leafy = false;
            foreach (var b in bufs) b.n = 0;
            tints = job.tints ?? DEFAULT_TINTS;
            for (int y = 0; y < 16; y++) for (int z = 0; z < 16; z++)
            {
                int p = Pidx(0, y, z);
                for (int x = 0; x < 16; x++, p++)
                {
                    int id = ids[p];
                    if (id == 0) continue;
                    tcol = z * 16 + x;
                    int shape = SHAPE[id];
                    if (shape == SH.CUBE) { Cube(ids, meta, light, p, id, x, y, z); continue; }
                    Special(ids, meta, light, p, id, shape, x, y, z);
                }
            }
            var visr = Visibility(ids);
            var res = new MeshResult { cx = job.cx, cz = job.cz, sy = job.sy, ver = job.ver, vis = visr, leafy = leafy, job = job };
            for (int i = 0; i < 3; i++) res.counts[i] = bufs[i].n;
            int total = res.counts[0] + res.counts[1] + res.counts[2];
            var outp = new uint[total * 12];
            var gc = res.gc;
            int off = 0;
            // Quads are sorted by facing group, and inside a group from the side the group is seen from
            // (a +X face is only visible from larger x, so +X faces go largest x first). Drawn in that order
            // the nearer layers come first and the depth test rejects the hidden layers behind them.
            const int SK = 520;
            var cnt = new int[FACE_GROUPS * SK];
            for (int pass = 0; pass < 3; pass++)
            {
                var b = bufs[pass]; int G0 = pass * FACE_GROUPS;
                if (sortKey.Length < b.n) sortKey = new int[b.n * 2];
                var K = sortKey;
                Array.Clear(cnt, 0, cnt.Length);
                for (int q = 0; q < b.n; q++)
                {
                    int g = b.g[q]; uint w = b.d[q * 12];
                    int c = g < 2 ? (int)(w & 1023) : g < 4 ? (int)((w >> 10) & 1023) : g < 6 ? (int)((w >> 20) & 1023) : 0;   // plane coordinate (1/32 block)
                    int k = g * SK + (g < 6 && (g & 1) != 0 ? 512 - c : c);
                    K[q] = k; cnt[k]++; gc[G0 + g]++;
                }
                // counting sort by (group, plane position)
                for (int k = 1, s = cnt[0]; k < cnt.Length; k++) { int c = cnt[k]; cnt[k] = s; s += c; }
                cnt[0] = 0;
                for (int q = 0; q < b.n; q++)
                {
                    int dst = (off + cnt[K[q]]++) * 12;
                    Array.Copy(b.d, q * 12, outp, dst, 12);
                }
                off += b.n;
            }
            res.data = outp;
            return res;
        }

        // ---------------------------------------------------------------- full cubes
        void Cube(ushort[] ids, byte[] meta, byte[] light, int p, int id, int x, int y, int z)
        {
            int fl = FLAGS[id];
            bool leaves = (fl & BF_LEAVES) != 0;
            // fast leaves are solid: opaque texture in the solid pass (no alpha test)
            bool fastLeaf = leaves && !fancyLeaves;
            var layerBuf = bufs[fastLeaf ? RL_SOLID : RLAYER[id]];
            bool clear = (fl & (BF_GLASS | BF_TRANS)) != 0;
            bool emissive = (fl & BF_EMISSIVE) != 0;
            int m = meta[p];
            int flagV = leaves && sway ? 2 : 0;
            if (leaves) leafy = true;
            // waterlogged cube (mangrove roots in water): the water of the cell is drawn with it
            if ((fl & BF_AQUATIC) != 0) Fluid(ids, meta, light, p, B.WATER, x, y, z, true);
            for (int f = 0; f < 6; f++)
            {
                int np = p + FACE_DELTA[f];
                int n = ids[np];
                if (OPAQUE[n] != 0) continue;
                if (n == id && (clear || (leaves && !fancyLeaves))) continue;
                if (leafShell && leaves && (FLAGS[n] & BF_LEAVES) != 0) continue;
                // buried leaf face: the neighbour leaf is itself covered by leaves or a solid block behind it
                if (leafCull && leaves && (FLAGS[n] & BF_LEAVES) != 0)
                {
                    var dd = FN[f]; int x2 = x + 2 * dd[0], y2 = y + 2 * dd[1], z2 = z + 2 * dd[2];
                    if (x2 >= -1 && x2 <= 16 && y2 >= -1 && y2 <= 16 && z2 >= -1 && z2 <= 16)
                    {
                        int n2 = ids[np + FACE_DELTA[f]];
                        if (OPAQUE[n2] != 0 || (FLAGS[n2] & BF_LEAVES) != 0) continue;
                    }
                }
                if (clear && n != 0 && (FLAGS[n] & (BF_GLASS | BF_TRANS)) != 0 && SHAPE[n] == SH.CUBE && RLAYER[n] == RLAYER[id] && (fl & BF_GLASS) != 0) continue;
                // texture + uv rotation
                int layer = FTEX[id * 6 + f], rot = 0;
                if ((fl & BF_AXIS) != 0)
                {
                    int ax = m & 3;
                    if (ax == 1)
                    { // x axis
                        if (f == 0 || f == 1) layer = FTEX[id * 6 + 2]; else { layer = FTEX[id * 6]; rot = 1; }
                    }
                    else if (ax == 2)
                    { // z axis
                        if (f == 4 || f == 5) layer = FTEX[id * 6 + 2]; else { layer = FTEX[id * 6]; rot = f == 0 || f == 1 ? 1 : 0; }
                    }
                }
                if ((fl & BF_FACING) != 0 && f == FACE_OF_DIR[m & 3]) layer = FRONT[id];
                if (fastLeaf && OPAQUE_LEAF[layer] != 0) layer = OPAQUE_LEAF[layer];
                // grass under snow shows the snowy side (Minecraft's snowy=true grass block)
                if (id == B.GRASS && f != 2 && f != 3) { int up = ids[p + FACE_DELTA[2]]; if (up == B.SNOW || up == B.SNOW_LAYER) layer = FTEX[B.SNOWY_GRASS * 6 + f]; }
                int nl = light[np];
                int baseSky = nl >> 4, baseBlk = nl & 15;
                var corners = FC[f]; var aoo = AO_OFF[f];
                double fs = FACE_SHADE[f];
                for (int v = 0; v < 4; v++)
                {
                    var c = corners[v];
                    tpx[v] = (x + c[0]) * 16; tpy[v] = (y + c[1]) * 16; tpz[v] = (z + c[2]) * 16;
                    var a = aoo[v];
                    int i1 = np + a[0], i2 = np + a[1], i3 = np + a[2];
                    int s1 = OPAQUE[ids[i1]], s2 = OPAQUE[ids[i2]], s3 = OPAQUE[ids[i3]];
                    int ao = s1 != 0 && s2 != 0 ? 0 : 3 - (s1 + s2 + s3);
                    int ssum = baseSky, bsum = baseBlk, cnt = 1;
                    if (s1 == 0) { int l = light[i1]; ssum += l >> 4; bsum += l & 15; cnt++; }
                    if (s2 == 0) { int l = light[i2]; ssum += l >> 4; bsum += l & 15; cnt++; }
                    if (s3 == 0 && !(s1 != 0 && s2 != 0)) { int l = light[i3]; ssum += l >> 4; bsum += l & 15; cnt++; }
                    int skyv = (int)JsMath.Round(ssum * 16.0 / cnt), blkv = emissive ? 240 : (int)JsMath.Round(bsum * 16.0 / cnt);
                    sk[v] = skyv; bl[v] = blkv;
                    double s = fs * AO_CURVE[ao];
                    sh[v] = (int)JsMath.Round(s * 255);
                    bright[v] = (float)(ao * 4 + (Math.Max(skyv, blkv) / 16.0));
                }
                FaceUV(f, tpx, tpy, tpz, rot, tu, tv, x, y, z);
                bool flip = (double)bright[0] + bright[2] < (double)bright[1] + bright[3];
                Quad(layerBuf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, flagV, null, flip);
            }
        }

        // UV by projecting the (1/16 unit) positions onto the face plane (like MC uv-lock)
        static void FaceUV(int f, float[] px, float[] py, float[] pz, int rot, float[] tu, float[] tv, int bx, int by, int bz)
        {
            double ox = bx * 16, oy = by * 16, oz = bz * 16;
            for (int v = 0; v < 4; v++)
            {
                double x = px[v] - ox, y = py[v] - oy, z = pz[v] - oz;
                double u, w;
                switch (f)
                {
                    case 0: u = 16 - z; w = 16 - y; break;
                    case 1: u = z; w = 16 - y; break;
                    case 2: u = x; w = z; break;
                    case 3: u = x; w = 16 - z; break;
                    case 4: u = x; w = 16 - y; break;
                    default: u = 16 - x; w = 16 - y; break;
                }
                for (int r = 0; r < rot; r++) { double t = u; u = 16 - w; w = t; }
                tu[v] = (float)u; tv[v] = (float)w;
            }
        }

        // ---------------------------------------------------------------- partial shapes
        // emit an axis aligned box (coords in 1/16 inside the block). tex: one layer or one per face.
        // cull faces on the block border against opaque neighbours (always), mask of faces to skip
        void Box(int p, int id, int bx, int by, int bz, double x0, double y0, double z0, double x1, double y1, double z1, int texAll, int[] tex, int skipMask, QBuf buf, BoxExtra extra = null)
        {
            var ids = this.ids; var light = this.light;
            int own = light[p], ownS = own >> 4, ownB = own & 15;
            bool emissive = (FLAGS[id] & BF_EMISSIVE) != 0;
            for (int f = 0; f < 6; f++)
            {
                if ((skipMask & (1 << f)) != 0) continue;
                var n = FN[f];
                bool onEdge = (n[0] == 1 && x1 >= 16) || (n[0] == -1 && x0 <= 0) || (n[1] == 1 && y1 >= 16) ||
                    (n[1] == -1 && y0 <= 0) || (n[2] == 1 && z1 >= 16) || (n[2] == -1 && z0 <= 0);
                int s = ownS, b = ownB;
                if (onEdge)
                {
                    int np = p + FACE_DELTA[f];
                    int nid = ids[np];
                    if (OPAQUE[nid] != 0) continue;
                    if (extra != null && extra.cullSame && nid == id) continue;
                    int l = light[np];
                    if ((l >> 4) > s) s = l >> 4; if ((l & 15) > b) b = l & 15;
                }
                else
                {
                    // interior face: take max with face neighbour anyway for partial blocks touching light
                    int l = light[p + FACE_DELTA[f]];
                    if (OPAQUE[ids[p + FACE_DELTA[f]]] == 0) { if ((l >> 4) > s) s = l >> 4; if ((l & 15) > b) b = l & 15; }
                }
                if (emissive) b = 15;
                var c = FC[f];
                for (int v = 0; v < 4; v++)
                {
                    var cc = c[v];
                    tpx[v] = (float)(bx * 16 + (cc[0] != 0 ? x1 : x0));
                    tpy[v] = (float)(by * 16 + (cc[1] != 0 ? y1 : y0));
                    tpz[v] = (float)(bz * 16 + (cc[2] != 0 ? z1 : z0));
                }
                double[] shear = extra?.shear;
                if (shear != null)
                {
                    for (int v = 0; v < 4; v++) { double ry = tpy[v] - by * 16; tpx[v] = (float)(tpx[v] + shear[0] * ry); tpz[v] = (float)(tpz[v] + shear[1] * ry); }
                }
                int layer = tex == null ? texAll : tex[f];
                FaceUV(f, tpx, tpy, tpz, extra != null && extra.rot != null ? extra.rot[f] : 0, tu, tv, bx, by, bz);
                if (shear != null)
                {
                    // uv from unsheared coordinates
                    for (int v = 0; v < 4; v++) { double ry = tpy[v] - by * 16; tpx[v] = (float)(tpx[v] - shear[0] * ry); tpz[v] = (float)(tpz[v] - shear[1] * ry); }
                    FaceUV(f, tpx, tpy, tpz, 0, tu, tv, bx, by, bz);
                    for (int v = 0; v < 4; v++) { double ry = tpy[v] - by * 16; tpx[v] = (float)(tpx[v] + shear[0] * ry); tpz[v] = (float)(tpz[v] + shear[1] * ry); }
                }
                if (extra != null && extra.uvOverride != null && extra.uvOverride[f] != null)
                {
                    var o = extra.uvOverride[f];
                    tu[0] = (float)o[0]; tv[0] = (float)o[3]; tu[1] = (float)o[2]; tv[1] = (float)o[3]; tu[2] = (float)o[2]; tv[2] = (float)o[1]; tu[3] = (float)o[0]; tv[3] = (float)o[1];
                }
                if (extra != null && extra.flipU != null && extra.flipU[f]) for (int v = 0; v < 4; v++) tu[v] = 16 - tu[v];
                int fsv = (int)JsMath.Round(FACE_SHADE[f] * 255);
                for (int v = 0; v < 4; v++) { sh[v] = fsv; sk[v] = s * 16; bl[v] = b * 16; }
                Quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, null, false);
            }
        }

        void Box(int p, int id, int bx, int by, int bz, double[] r, int[] tex, QBuf buf) => Box(p, id, bx, by, bz, r[0], r[1], r[2], r[3], r[4], r[5], 0, tex, 0, buf);

        // Minecraft block model: the parts of the block's variant, each element's faces with their uv, uv
        // rotation and element rotation, turned by the variant's x / y rotation; faces with a cullface
        // hidden behind an opaque neighbour on that side
        void Model(int p, int id, int m, int x, int y, int z, QBuf buf)
        {
            var V = MODEL_VAR_ID[id];
            if (V == null) return;
            var parts = (m < V.Length && V[m] != null) ? V[m] : V[0]; var ids = this.ids;
            int lt = MaxLight(p), s = lt >> 4, b = (FLAGS[id] & BF_EMISSIVE) != 0 ? 15 : lt & 15;
            var PP = MODEL_TMP;
            foreach (var part in parts)
            {
                int rx = part.Rx, ry = part.Ry;
                double cx = Math.Cos(rx * Math.PI / 180), sx = Math.Sin(rx * Math.PI / 180), cy = Math.Cos(ry * Math.PI / 180), sy = Math.Sin(ry * Math.PI / 180);
                foreach (var E in Models.MODEL_LIST[part.Model])
                {
                    var fr = E.From; var to = E.To; var rot = E.Rot; var faces = E.Faces;
                    int ra = 0; double rc = 1, rs = 0, sc0 = 1, sc1 = 1, sc2 = 1;
                    if (rot != null)
                    {
                        ra = rot.Axis; double a = rot.Angle * Math.PI / 180; rc = Math.Cos(a); rs = Math.Sin(a);
                        if (rot.Rescale != 0)
                        {
                            double k = 1 / Math.Cos(Math.Abs(rot.Angle) == 22.5 ? Math.PI / 8 : Math.PI / 4) - 1;
                            sc0 = ra == 0 ? 1 : 1 + k; sc1 = ra == 1 ? 1 : 1 + k; sc2 = ra == 2 ? 1 : 1 + k;
                        }
                    }
                    for (int f = 0; f < 6; f++)
                    {
                        var F = faces[f];
                        if (F == null) continue;
                        var C = MODEL_FACE_CORNERS[f];
                        for (int v = 0; v < 4; v++)
                        {
                            double px = C[v][0] != 0 ? to[0] : fr[0], py = C[v][1] != 0 ? to[1] : fr[1], pz = C[v][2] != 0 ? to[2] : fr[2];
                            if (rot != null)
                            {
                                double aa = px - rot.Ox, bb2 = py - rot.Oy, cc = pz - rot.Oz, t2;
                                if (ra == 0) { t2 = bb2 * rc - cc * rs; cc = bb2 * rs + cc * rc; bb2 = t2; }
                                else if (ra == 1) { t2 = aa * rc + cc * rs; cc = -aa * rs + cc * rc; aa = t2; }
                                else { t2 = aa * rc - bb2 * rs; bb2 = aa * rs + bb2 * rc; aa = t2; }
                                px = aa * sc0 + rot.Ox; py = bb2 * sc1 + rot.Oy; pz = cc * sc2 + rot.Oz;
                            }
                            // variant rotation about the block centre: x (+y toward north), then y (clockwise from above)
                            double a = px - 8, bb = py - 8, c = pz - 8, t;
                            if (rx != 0) { t = bb * cx + c * sx; c = -bb * sx + c * cx; bb = t; }
                            if (ry != 0) { t = a * cy - c * sy; c = a * sy + c * cy; a = t; }
                            PP[v * 3] = (float)(a + 8); PP[v * 3 + 1] = (float)(bb + 8); PP[v * 3 + 2] = (float)(c + 8);
                        }
                        // cullface, turned with the variant
                        if (F.Cull >= 0)
                        {
                            var n = FN[F.Cull];
                            int nx = n[0], ny = n[1], nz = n[2], t;
                            if (rx != 0) { t = (int)JsMath.Round(ny * cx + nz * sx); nz = (int)JsMath.Round(-ny * sx + nz * cx); ny = t; }
                            if (ry != 0) { t = (int)JsMath.Round(nx * cy - nz * sy); nz = (int)JsMath.Round(nx * sy + nz * cy); nx = t; }
                            if (OPAQUE[ids[p + nx * SX + ny * SY + nz * SZ]] != 0) continue;
                        }
                        int rq = F.UvRot / 90;
                        for (int v = 0; v < 4; v++)
                        {
                            tpx[v] = (float)(x * 16 + (double)PP[v * 3]); tpy[v] = (float)(y * 16 + (double)PP[v * 3 + 1]); tpz[v] = (float)(z * 16 + (double)PP[v * 3 + 2]);
                            int j = (v + rq) & 3;
                            tu[v] = (float)(j == 0 || j == 1 ? F.U0 : F.U1);
                            tv[v] = (float)(j == 0 || j == 3 ? F.V0 : F.V1);
                        }
                        // shade by the face's direction after turning
                        double e1x = (double)PP[3] - PP[0], e1y = (double)PP[4] - PP[1], e1z = (double)PP[5] - PP[2], e2x = (double)PP[6] - PP[0], e2y = (double)PP[7] - PP[1], e2z = (double)PP[8] - PP[2];
                        double fnx = e1y * e2z - e1z * e2y, fny = e1z * e2x - e1x * e2z, fnz = e1x * e2y - e1y * e2x;
                        double ax = Math.Abs(fnx), ay = Math.Abs(fny), az = Math.Abs(fnz);
                        double fs = F.Shade == 0 ? 1 : ay >= ax && ay >= az ? (fny > 0 ? FACE_SHADE[2] : FACE_SHADE[3]) : ax >= az ? FACE_SHADE[0] : FACE_SHADE[4];
                        int shv = (int)JsMath.Round(fs * 255);
                        for (int v = 0; v < 4; v++) { sh[v] = shv; sk[v] = s * 16; bl[v] = b * 16; }
                        Quad(buf, tpx, tpy, tpz, tu, tv, F.Layer, sh, sk, bl, 0, null, false);
                    }
                }
            }
        }

        // flat double sided quad given 4 corner positions (1/16 units, absolute in section) and uv rect
        void Flat(QBuf buf, double[] pts, int layer, double u0, double v0, double u1, double v1, int s, int b, double shade, int flagN, int[] flags, bool twoSided)
        {
            for (int v = 0; v < 4; v++) { tpx[v] = (float)pts[v * 3]; tpy[v] = (float)pts[v * 3 + 1]; tpz[v] = (float)pts[v * 3 + 2]; }
            tu[0] = (float)u0; tv[0] = (float)v1; tu[1] = (float)u1; tv[1] = (float)v1; tu[2] = (float)u1; tv[2] = (float)v0; tu[3] = (float)u0; tv[3] = (float)v0;
            int shv = (int)JsMath.Round(shade * 255);
            for (int v = 0; v < 4; v++) { sh[v] = shv; sk[v] = s * 16; bl[v] = b * 16; }
            Quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, flagN, flags, false);
            if (twoSided)
            {
                // reversed winding: swap v1<->v3
                float t;
                t = tpx[1]; tpx[1] = tpx[3]; tpx[3] = t; t = tpy[1]; tpy[1] = tpy[3]; tpy[3] = t; t = tpz[1]; tpz[1] = tpz[3]; tpz[3] = t;
                t = tu[1]; tu[1] = tu[3]; tu[3] = t; t = tv[1]; tv[1] = tv[3]; tv[3] = t;
                int[] f2 = flags != null ? new[] { flags[0], flags[3], flags[2], flags[1] } : null;
                Quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, flagN, f2, false);
            }
        }

        // redstone dust: a dot, straight or branching lines toward what it links to, and strips up the
        // side of a block it climbs; coloured by its signal strength (Minecraft's dust colour ramp)
        void Wire(ushort[] ids, byte[] meta, int p, int m, int x, int y, int z, QBuf buf)
        {
            int sX = FACE_DELTA[0], sY = FACE_DELTA[2], sZ = FACE_DELTA[4];
            int Get(int dx, int dy, int dz) => ids[p + dx * sX + dy * sY + dz * sZ];
            int GetM(int dx, int dy, int dz) => meta[p + dx * sX + dy * sY + dz * sZ];
            var L = new int[4];
            int n = 0;
            for (int d = 0; d < 4; d++) { L[d] = WireLink(Get, GetM, d); if (L[d] != 0) n++; }
            int lt = MaxLight(p), s = lt >> 4, b = lt & 15;
            double X = x * 16, Y = y * 16 + 0.25, Z = z * 16;
            int dot = WIRE_TEX[0], line = WIRE_TEX[1];
            forceTint = WIRE_TINT[m & 15];
            // a lone line runs straight through
            var draw = (int[])L.Clone();
            if (n == 1) for (int d = 0; d < 4; d++) if (L[d] != 0) draw[(d + 2) & 3] = 1;
            // north / south halves (line along z), west / east halves (line along x)
            if (draw[2] != 0) Flat(buf, new[] { X, Y, Z + 8, X + 16, Y, Z + 8, X + 16, Y, Z, X, Y, Z }, line, 0, 0, 16, 8, s, b, 1, 0, null, false);
            if (draw[0] != 0) Flat(buf, new[] { X, Y, Z + 16, X + 16, Y, Z + 16, X + 16, Y, Z + 8, X, Y, Z + 8 }, line, 0, 8, 16, 16, s, b, 1, 0, null, false);
            if (draw[1] != 0) Flat(buf, new[] { X + 8, Y, Z + 16, X + 8, Y, Z, X, Y, Z, X, Y, Z + 16 }, line, 0, 0, 16, 8, s, b, 1, 0, null, false);
            if (draw[3] != 0) Flat(buf, new[] { X + 16, Y, Z + 16, X + 16, Y, Z, X + 8, Y, Z, X + 8, Y, Z + 16 }, line, 0, 8, 16, 16, s, b, 1, 0, null, false);
            bool straight = n == 2 && ((L[0] != 0 && L[2] != 0) || (L[1] != 0 && L[3] != 0)) || n == 1;
            if (!straight) Flat(buf, new[] { X, Y, Z + 16, X + 16, Y, Z + 16, X + 16, Y, Z, X, Y, Z }, dot, 0, 0, 16, 16, s, b, 1, 0, null, false);
            // up the side of the neighbouring block
            for (int d = 0; d < 4; d++)
            {
                if (L[d] != 2) continue;
                double e = 15.75, Yb = y * 16, Yt = Yb + 16;
                double[] pts;
                if (d == 0) pts = new[] { X, Yb, Z + e, X + 16, Yb, Z + e, X + 16, Yt, Z + e, X, Yt, Z + e };
                else if (d == 2) pts = new[] { X + 16, Yb, Z + 16 - e, X, Yb, Z + 16 - e, X, Yt, Z + 16 - e, X + 16, Yt, Z + 16 - e };
                else if (d == 1) pts = new[] { X + 16 - e, Yb, Z, X + 16 - e, Yb, Z + 16, X + 16 - e, Yt, Z + 16, X + 16 - e, Yt, Z };
                else pts = new[] { X + e, Yb, Z + 16, X + e, Yb, Z, X + e, Yt, Z, X + e, Yt, Z + 16 };
                Flat(buf, pts, line, 0, 0, 16, 16, s, b, 1, 0, null, true);
            }
            forceTint = -1;
        }

        // own light maxed with the 6 neighbours (used for plants / thin shapes)
        int MaxLight(int p)
        {
            var light = this.light; var ids = this.ids;
            int l = light[p], s = l >> 4, b = l & 15;
            for (int f = 0; f < 6; f++)
            {
                int q = p + FACE_DELTA[f];
                if (OPAQUE[ids[q]] != 0) continue;
                int m = light[q];
                if ((m >> 4) > s) s = m >> 4; if ((m & 15) > b) b = m & 15;
            }
            return (s << 4) | b;
        }

        void Cross(QBuf buf, int x, int y, int z, int layer, int lt, double inset, bool swayIt, double hgt = 0)
        {
            double a = inset, c = 16 - inset, H = hgt != 0 ? hgt : 16;
            int s = lt >> 4, b = lt & 15;
            double X = x * 16, Y = y * 16, Z = z * 16;
            int[] fl = swayIt ? FL_SWAY : null;
            Flat(buf, new[] { X + a, Y, Z + a, X + c, Y, Z + c, X + c, Y + H, Z + c, X + a, Y + H, Z + a }, layer, 0, 16 - H, 16, 16, s, b, 0.95, 0, fl, true);
            Flat(buf, new[] { X + c, Y, Z + a, X + a, Y, Z + c, X + a, Y + H, Z + c, X + c, Y + H, Z + a }, layer, 0, 16 - H, 16, 16, s, b, 0.95, 0, fl, true);
        }

        static readonly double[][] TORCH_UVO = { null, null, new double[] { 7, 6, 9, 8 }, new double[] { 7, 14, 9, 16 }, null, null };
        static readonly double[][] PICKLE_POS = { new double[] { 6, 6 }, new double[] { 3, 4 }, new double[] { 10, 9 }, new double[] { 5, 11 } };

        void Special(ushort[] ids, byte[] meta, byte[] light, int p, int id, int shape, int x, int y, int z)
        {
            this.ids = ids; this.light = light;
            int m = meta[p];
            var buf = bufs[RLAYER[id]];
            int T = id * 6;
            var texs = new int[6];
            for (int i = 0; i < 6; i++) texs[i] = FTEX[T + i];
            // waterlogged: the water of the cell is drawn with the block
            if ((FLAGS[id] & BF_WET) != 0) Fluid(ids, meta, light, p, B.WATER, x, y, z, true);
            switch (shape)
            {
                case SH.WATER: case SH.LAVA:
                    Fluid(ids, meta, light, p, id, x, y, z);
                    return;
                case SH.MODEL: Model(p, id, m, x, y, z, buf); return;
                case SH.CROSS:
                {
                    if ((FLAGS[id] & BF_AQUATIC) != 0) Fluid(ids, meta, light, p, B.WATER, x, y, z, true);
                    int lt = MaxLight(p);
                    bool swy = sway && (FLAGS[id] & BF_PLANT) != 0 && (FLAGS[id] & BF_AQUATIC) == 0 && id != B.COBWEB;
                    int layer = texs[2];
                    Cross(buf, x, y, z, layer, lt, 2.4, swy);
                    return;
                }
                case SH.CRYSTAL:
                {
                    // the cross model turned so that its top points out of the face it grows from (meta)
                    int f = m % 6, lt = MaxLight(p); double X = x * 16, Y = y * 16, Z = z * 16, a = 2.4, c = 16 - a;
                    (double, double, double) Rot(double px, double py, double pz) => f == 2 ? (px, py, pz) : f == 3 ? (px, 16 - py, 16 - pz) : f == 0 ? (py, 16 - px, pz) :
                        f == 1 ? (16 - py, px, pz) : f == 4 ? (px, 16 - pz, py) : (px, pz, 16 - py);
                    foreach (var q in new[] { new[] { a, a, c, c }, new[] { c, a, a, c } })
                    {
                        var pts = new double[12]; int k = 0;
                        foreach (var (px, py, pz) in new[] { (q[0], 0.0, q[1]), (q[2], 0.0, q[3]), (q[2], 16.0, q[3]), (q[0], 16.0, q[1]) })
                        {
                            var r = Rot(px, py, pz); pts[k++] = X + r.Item1; pts[k++] = Y + r.Item2; pts[k++] = Z + r.Item3;
                        }
                        Flat(buf, pts, texs[2], 0, 0, 16, 16, lt >> 4, lt & 15, 0.95, 0, null, true);
                    }
                    return;
                }
                case SH.TALL:
                {
                    int lt = MaxLight(p);
                    Cross(buf, x, y, z, (m & 1) != 0 ? texs[2] : texs[3], lt, 2.4, sway && (m & 1) != 0);
                    return;
                }
                case SH.FIRE:
                {
                    int lt = MaxLight(p);
                    Cross(buf, x, y, z, texs[2], lt | 15, 0.5, false);
                    return;
                }
                case SH.SLAB:
                {
                    if ((m & 1) != 0) Box(p, id, x, y, z, 0, 8, 0, 16, 16, 16, 0, texs, 0, buf);
                    else Box(p, id, x, y, z, 0, 0, 0, 16, 8, 16, 0, texs, 0, buf);
                    return;
                }
                case SH.STAIRS: Stairs(p, id, m, x, y, z, texs, buf); return;
                case SH.FENCE: Fence(p, id, x, y, z, texs, buf); return;
                case SH.WALL: Wall(p, id, x, y, z, texs, buf); return;
                case SH.PANE: Pane(p, id, x, y, z, texs, buf); return;
                case SH.GATE: Gate(p, id, m, x, y, z, texs, buf); return;
                case SH.DOOR:
                {
                    int upper = (m >> 2) & 1, open = (m >> 3) & 1, hingeR = (m >> 4) & 1, f = m & 3;
                    int t = upper != 0 ? texs[2] : texs[3];
                    // closed: thin slab on the edge opposite to facing; open: rotated around the hinge
                    double[] bxs = { 0, 0, 13, 16, 16, 16 }; // north-facing closed box (south edge)
                    if (open != 0) bxs = hingeR != 0 ? new double[] { 13, 0, 0, 16, 16, 16 } : new double[] { 0, 0, 0, 3, 16, 16 };
                    var r = RotBox(bxs, (f + 2) & 3);
                    var flipU = new bool[6];
                    if (hingeR != 0) { flipU[0] = flipU[1] = flipU[4] = flipU[5] = true; }
                    Box(p, id, x, y, z, r[0], r[1], r[2], r[3], r[4], r[5], t, null, 0, buf, new BoxExtra { flipU = flipU });
                    return;
                }
                case SH.TRAPDOOR:
                {
                    int f = m & 3, top = (m >> 2) & 1, open = (m >> 3) & 1;
                    double[] bxs;
                    if (open != 0) bxs = RotBox(new double[] { 0, 0, 13, 16, 16, 16 }, (f + 2) & 3);
                    else bxs = top != 0 ? new double[] { 0, 13, 0, 16, 16, 16 } : new double[] { 0, 0, 0, 16, 3, 16 };
                    Box(p, id, x, y, z, bxs[0], bxs[1], bxs[2], bxs[3], bxs[4], bxs[5], texs[2], null, 0, buf);
                    return;
                }
                case SH.LADDER:
                {
                    int f = m & 3; // facing: ladder is on the wall opposite to its facing
                    int lt = MaxLight(p), s = lt >> 4, b = lt & 15;
                    double X = x * 16, Y = y * 16, Z = z * 16, e = 15.2;
                    double[] pts;
                    if (f == 2) pts = new[] { X, Y, Z + e, X + 16, Y, Z + e, X + 16, Y + 16, Z + e, X, Y + 16, Z + e };          // north-facing, on south wall
                    else if (f == 0) pts = new[] { X + 16, Y, Z + 16 - e, X, Y, Z + 16 - e, X, Y + 16, Z + 16 - e, X + 16, Y + 16, Z + 16 - e };
                    else if (f == 1) pts = new[] { X + e, Y, Z, X + e, Y, Z + 16, X + e, Y + 16, Z + 16, X + e, Y + 16, Z };                      // west-facing, on east wall
                    else pts = new[] { X + 16 - e, Y, Z + 16, X + 16 - e, Y, Z, X + 16 - e, Y + 16, Z, X + 16 - e, Y + 16, Z + 16 };        // east-facing, on west wall
                    Flat(buf, pts, texs[0], 0, 0, 16, 16, s, b, 0.9, 0, null, true);
                    return;
                }
                case SH.TORCH:
                {
                    if (m == 0 || m > 4)
                    {
                        Box(p, id, x, y, z, 7, 0, 7, 9, 10, 9, texs[0], null, 0, buf, new BoxExtra { uvOverride = TORCH_UVO });
                    }
                    else
                    {
                        int d = m - 1; // wall direction: 0 S,1 W,2 N,3 E (wall is on that side)
                        int dx = DIRX[d], dz = DIRZ[d];
                        double ox = dx * 5.5, oz = dz * 5.5;
                        Box(p, id, x, y, z, 7 + ox, 3.5, 7 + oz, 9 + ox, 13.5, 9 + oz, texs[0], null, 0, buf,
                            new BoxExtra { uvOverride = TORCH_UVO, shear = new[] { -dx * 0.42, -dz * 0.42 } });
                    }
                    return;
                }
                case SH.CARPET: Box(p, id, x, y, z, 0, 0, 0, 16, 1, 16, 0, texs, 0, buf); return;
                case SH.PLATE: Box(p, id, x, y, z, 1, 0, 1, 15, (m & 1) != 0 ? 0.5 : 1, 15, 0, texs, 0, buf); return;   // pressed: half as thick
                case SH.WIRE: Wire(ids, meta, p, m, x, y, z, buf); return;
                case SH.BUTTON:
                {
                    int d = m & 3;
                    var r = RotBox(new double[] { 5, 6, 0, 11, 10, (m & 8) != 0 ? 1 : 2 }, (d + 2) & 3);   // pressed: pushed in
                    Box(p, id, x, y, z, r, texs, buf);
                    return;
                }
                case SH.FARMLAND: Box(p, id, x, y, z, 0, 0, 0, 16, 15, 16, 0, texs, 0, buf); return;
                case SH.CACTUS:
                {
                    Box(p, id, x, y, z, 0, 0, 0, 16, 16, 16, 0, texs, 0b110011, buf);
                    Box(p, id, x, y, z, 1, 0, 0, 15, 16, 16, 0, texs, 0b111100, buf);
                    Box(p, id, x, y, z, 0, 0, 1, 16, 16, 15, 0, texs, 0b001111, buf);
                    return;
                }
                case SH.CHEST:
                {
                    int f = m & 3; var t = (int[])texs.Clone();
                    t[FACE_OF_DIR[f]] = FRONT[id];
                    Box(p, id, x, y, z, 1, 0, 1, 15, 14, 15, 0, t, 0, buf);
                    return;
                }
                case SH.BED:
                {
                    int f = m & 3, head = (m >> 2) & 1;
                    var t = (int[])texs.Clone();
                    t[2] = head != 0 ? (LAYER_BED_HEAD.TryGetValue(id, out var bh) && bh != 0 ? bh : texs[2]) : texs[2];
                    var rot = new[] { 0, 0, (f + 2) & 3, 0, 0, 0 };
                    Box(p, id, x, y, z, 0, 3, 0, 16, 9, 16, 0, t, 0, buf, new BoxExtra { rot = rot });
                    // legs
                    int lg = BED_LEG;
                    Box(p, id, x, y, z, 0, 0, 0, 3, 3, 3, lg, null, 0, buf); Box(p, id, x, y, z, 13, 0, 0, 16, 3, 3, lg, null, 0, buf);
                    Box(p, id, x, y, z, 0, 0, 13, 3, 3, 16, lg, null, 0, buf); Box(p, id, x, y, z, 13, 0, 13, 16, 3, 16, lg, null, 0, buf);
                    return;
                }
                case SH.BAMBOO: Box(p, id, x, y, z, 7, 0, 7, 9, 16, 9, texs[0], null, 0, buf); return;
                case SH.POT: Box(p, id, x, y, z, 5, 0, 5, 11, 6, 11, 0, texs, 0, buf); return;
                case SH.PICKLE:
                {
                    Fluid(ids, meta, light, p, B.WATER, x, y, z, true);
                    int n = (m & 3) + 1;
                    for (int i = 0; i < n; i++) Box(p, id, x, y, z, PICKLE_POS[i][0], 0, PICKLE_POS[i][1], PICKLE_POS[i][0] + 4, 6, PICKLE_POS[i][1] + 4, 0, texs, 0, buf);
                    return;
                }
                case SH.LILY:
                {
                    int lt = MaxLight(p);
                    double X = x * 16, Y = y * 16 + 0.3, Z = z * 16;
                    int r = m & 3;
                    double[][] P4 = { new[] { X, Z + 16 }, new[] { X + 16, Z + 16 }, new[] { X + 16, Z }, new[] { X, Z } };
                    var q = new double[12];
                    for (int k = 0; k < 4; k++) { var c = P4[(k + r) & 3]; q[k * 3] = c[0]; q[k * 3 + 1] = Y; q[k * 3 + 2] = c[1]; }
                    Flat(buf, q, texs[2], 0, 0, 16, 16, lt >> 4, lt & 15, 1.0, 0, null, true);
                    return;
                }
                case SH.RAIL:
                {
                    int lt = MaxLight(p);
                    double X = x * 16, Y = y * 16 + 1, Z = z * 16;
                    var q = (m & 1) != 0 ? new[] { X, Y, Z, X, Y, Z + 16, X + 16, Y, Z + 16, X + 16, Y, Z } : new[] { X, Y, Z + 16, X + 16, Y, Z + 16, X + 16, Y, Z, X, Y, Z };
                    Flat(buf, q, texs[2], 0, 0, 16, 16, lt >> 4, lt & 15, 1.0, 0, null, true);
                    return;
                }
                case SH.VINE: case SH.LICHEN:
                {
                    int lt = MaxLight(p), s = lt >> 4, b = id == B.GLOW_LICHEN ? Math.Max(lt & 15, 7) : lt & 15;
                    double X = x * 16, Y = y * 16, Z = z * 16, e = 0.8;
                    int t = texs[2];
                    int bits = m != 0 ? m : 1;
                    if ((bits & 1) != 0) Flat(buf, new[] { X + 16, Y, Z + 16 - e, X, Y, Z + 16 - e, X, Y + 16, Z + 16 - e, X + 16, Y + 16, Z + 16 - e }, t, 0, 0, 16, 16, s, b, 0.8, 0, null, true);
                    if ((bits & 4) != 0) Flat(buf, new[] { X, Y, Z + e, X + 16, Y, Z + e, X + 16, Y + 16, Z + e, X, Y + 16, Z + e }, t, 0, 0, 16, 16, s, b, 0.8, 0, null, true);
                    if ((bits & 2) != 0) Flat(buf, new[] { X + e, Y, Z + 16, X + e, Y, Z, X + e, Y + 16, Z, X + e, Y + 16, Z + 16 }, t, 0, 0, 16, 16, s, b, 0.72, 0, null, true);
                    if ((bits & 8) != 0) Flat(buf, new[] { X + 16 - e, Y, Z, X + 16 - e, Y, Z + 16, X + 16 - e, Y + 16, Z + 16, X + 16 - e, Y + 16, Z }, t, 0, 0, 16, 16, s, b, 0.72, 0, null, true);
                    if ((bits & 16) != 0) Flat(buf, new[] { X, Y + e, Z + 16, X + 16, Y + e, Z + 16, X + 16, Y + e, Z, X, Y + e, Z }, t, 0, 0, 16, 16, s, b, 1, 0, null, true);
                    if ((bits & 32) != 0) Flat(buf, new[] { X, Y + 16 - e, Z, X + 16, Y + 16 - e, Z, X + 16, Y + 16 - e, Z + 16, X, Y + 16 - e, Z + 16 }, t, 0, 0, 16, 16, s, b, 0.55, 0, null, true);
                    return;
                }
                case SH.SNOWLAYER: Box(p, id, x, y, z, 0, 0, 0, 16, 2 * ((m & 7) + 1), 16, 0, texs, 0, buf); return;
                default:
                    Box(p, id, x, y, z, 0, 0, 0, 16, 16, 16, 0, texs, 0, buf);
                    return;
            }
        }

        // ---------------------------------------------------------------- stairs
        void Stairs(int p, int id, int m, int x, int y, int z, int[] texs, QBuf buf)
        {
            var ids = this.ids; var meta = metaArr;
            int f = m & 3, top = (m >> 2) & 1;
            bool IsSt(int q) => SHAPE[ids[q]] == SH.STAIRS && ((meta[q] >> 2) & 1) == top;
            int Dq(int d) => DIRX[d] * SX + DIRZ[d] * SZ;
            int shape = 0; // 0 straight, 1 outer_left, 2 outer_right, 3 inner_left, 4 inner_right
            int ccw = (f + 3) & 3;
            int front = p + Dq(f);
            bool CanTake(int d) { int q = p + Dq(d); return !(SHAPE[ids[q]] == SH.STAIRS && (meta[q] & 7) == (m & 7)); }
            if (IsSt(front))
            {
                int f1 = meta[front] & 3;
                if ((f1 & 1) != (f & 1) && CanTake((f1 + 2) & 3)) shape = f1 == ccw ? 1 : 2;
            }
            if (shape == 0)
            {
                int back = p + Dq((f + 2) & 3);
                if (IsSt(back))
                {
                    int f2 = meta[back] & 3;
                    if ((f2 & 1) != (f & 1) && CanTake(f2)) shape = f2 == ccw ? 3 : 4;
                }
            }
            int sy0 = top != 0 ? 8 : 0, sy1 = top != 0 ? 16 : 8, uy0 = top != 0 ? 0 : 8, uy1 = top != 0 ? 8 : 16;
            Box(p, id, x, y, z, 0, sy0, 0, 16, sy1, 16, 0, texs, 0, buf);
            // upper parts in north-facing frame (high part at north: z 0..8), left = west (x<8)
            var parts = new List<double[]>(2);
            if (shape == 0) parts.Add(new double[] { 0, uy0, 0, 16, uy1, 8 });
            else if (shape == 1) parts.Add(new double[] { 0, uy0, 0, 8, uy1, 8 });
            else if (shape == 2) parts.Add(new double[] { 8, uy0, 0, 16, uy1, 8 });
            else if (shape == 3) { parts.Add(new double[] { 0, uy0, 0, 16, uy1, 8 }); parts.Add(new double[] { 0, uy0, 8, 8, uy1, 16 }); }
            else { parts.Add(new double[] { 0, uy0, 0, 16, uy1, 8 }); parts.Add(new double[] { 8, uy0, 8, 16, uy1, 16 }); }
            int turns = (f + 2) & 3;
            foreach (var b in parts) Box(p, id, x, y, z, RotBox(b, turns), texs, buf);
        }

        bool Connects(int q, int kind)
        {
            int n = ids[q];
            if (OPAQUE[n] != 0) return true;
            int s = SHAPE[n];
            if (kind == SH.FENCE) return s == SH.FENCE || s == SH.GATE;
            if (kind == SH.WALL) return s == SH.WALL || s == SH.GATE || s == SH.PANE;
            if (kind == SH.PANE) return s == SH.PANE || s == SH.WALL || (FLAGS[n] & (BF_GLASS | BF_TRANS)) != 0 && s == SH.CUBE;
            return false;
        }

        static readonly double[][] FENCE_ARMS = { new double[] { 7, 12, 0, 9, 15, 6 }, new double[] { 7, 6, 0, 9, 9, 6 } };

        void Fence(int p, int id, int x, int y, int z, int[] texs, QBuf buf)
        {
            Box(p, id, x, y, z, 6, 0, 6, 10, 16, 10, 0, texs, 0, buf);
            for (int d = 0; d < 4; d++)
            {
                if (!Connects(p + DIRX[d] * SX + DIRZ[d] * SZ, SH.FENCE)) continue;
                foreach (var b in FENCE_ARMS) Box(p, id, x, y, z, RotBox(b, (d + 2) & 3), texs, buf);
            }
        }

        void Wall(int p, int id, int x, int y, int z, int[] texs, QBuf buf)
        {
            var c = new bool[4];
            for (int d = 0; d < 4; d++) c[d] = Connects(p + DIRX[d] * SX + DIRZ[d] * SZ, SH.WALL);
            int above = ids[p + SY];
            bool straight = ((c[0] && c[2] && !c[1] && !c[3]) || (c[1] && c[3] && !c[0] && !c[2])) && above == 0;
            if (!straight) Box(p, id, x, y, z, 4, 0, 4, 12, 16, 12, 0, texs, 0, buf);
            for (int d = 0; d < 4; d++)
            {
                if (!c[d]) continue;
                Box(p, id, x, y, z, RotBox(new double[] { 5, 0, 0, 11, 14, straight ? 8 : 4 }, (d + 2) & 3), texs, buf);
            }
        }

        // Glass pane / iron bars as Minecraft's multipart pane model: the post (its top and bottom edge
        // always), toward each connected side an arm (its two flat faces and its top and bottom edge, no
        // faces inside the pane), toward each free side the post's face. Edges use the glass's edge
        // texture. Faces meeting a pane above / below that runs the same way are left out.
        void Pane(int p, int id, int x, int y, int z, int[] texs, QBuf buf)
        {
            var ids = this.ids; int pane = texs[0], edge = PANE_EDGE != null && PANE_EDGE[id] != 0 ? PANE_EDGE[id] : pane;
            int up = p + SY, dn = p - SY; bool isUp = SHAPE[ids[up]] == SH.PANE, isDn = SHAPE[ids[dn]] == SH.PANE;
            var T = paneTex;
            T[0] = T[1] = T[4] = T[5] = pane; T[2] = T[3] = edge;
            const int ALL = 0b111111; var conn = paneConn;
            for (int d = 0; d < 4; d++) conn[d] = Connects(p + DIRX[d] * SX + DIRZ[d] * SZ, SH.PANE);
            // post: top and bottom, and its face toward each free side
            int mask = ALL & ~((isUp ? 0 : 4) | (isDn ? 0 : 8));
            for (int d = 0; d < 4; d++) if (!conn[d]) mask &= ~(1 << FACE_OF_DIR[d]);
            Box(p, id, x, y, z, 7, 0, 7, 9, 16, 9, 0, T, mask, buf);
            for (int d = 0; d < 4; d++)
            {
                if (!conn[d]) continue;
                int o = DIRX[d] * SX + DIRZ[d] * SZ;
                bool hideUp = isUp && Connects(up + o, SH.PANE), hideDn = isDn && Connects(dn + o, SH.PANE);
                // the flat faces are the two across the arm; ends and the inner face are never drawn
                int flat = (d & 1) != 0 ? 0b110000 : 0b000011;
                int m = ALL & ~(flat | (hideUp ? 0 : 4) | (hideDn ? 0 : 8));
                if (d == 0) Box(p, id, x, y, z, 7, 0, 9, 9, 16, 16, 0, T, m, buf);
                else if (d == 1) Box(p, id, x, y, z, 0, 0, 7, 7, 16, 9, 0, T, m, buf);
                else if (d == 2) Box(p, id, x, y, z, 7, 0, 0, 9, 16, 7, 0, T, m, buf);
                else Box(p, id, x, y, z, 9, 0, 7, 16, 16, 9, 0, T, m, buf);
            }
        }

        static readonly double[][] GATE_POSTS = { new double[] { 0, 5, 7, 2, 16, 9 }, new double[] { 14, 5, 7, 16, 16, 9 } };
        static readonly double[][] GATE_SHUT = { new double[] { 2, 6, 7, 14, 9, 9 }, new double[] { 2, 12, 7, 14, 15, 9 }, new double[] { 6, 9, 7, 10, 12, 9 } };
        static readonly double[][] GATE_OPEN = { new double[] { 0, 6, 9, 2, 9, 15 }, new double[] { 0, 12, 9, 2, 15, 15 }, new double[] { 14, 6, 9, 16, 9, 15 }, new double[] { 14, 12, 9, 16, 15, 15 } };

        void Gate(int p, int id, int m, int x, int y, int z, int[] texs, QBuf buf)
        {
            int f = m & 3, open = (m >> 2) & 1;
            foreach (var b in GATE_POSTS) Box(p, id, x, y, z, RotBox(b, (f + 2) & 3), texs, buf);
            foreach (var b in open == 0 ? GATE_SHUT : GATE_OPEN) Box(p, id, x, y, z, RotBox(b, (f + 2) & 3), texs, buf);
        }

        // ---------------------------------------------------------------- fluids
        // height of one block's fluid (0..1), -1 for solid (ignored), 2 when fluid above (forces full)
        double FluidHeightAt(int q, bool isW)
        {
            var ids = this.ids; var meta = metaArr;
            int id = ids[q];
            bool same = isW ? IsWaterId(id) : id == B.LAVA;
            if (same)
            {
                int up = ids[q + SY];
                if (isW ? IsWaterId(up) : up == B.LAVA) return 2;
                if (id != B.WATER && id != B.LAVA) return 8.0 / 9;
                int lv = meta[q] & 7;
                return (meta[q] & 8) != 0 ? 8.0 / 9 : (8 - lv) / 9.0;
            }
            if (SOLID[id] != 0 || OPAQUE[id] != 0) return -1;
            return 0;
        }

        // Minecraft's corner height: the cell and its two side neighbours at the corner (dx,dz in {0,1}),
        // plus the diagonal one only when a side neighbour holds fluid; heights of 0.8 and more weigh ten times
        double CornerHeight(int p, int dx, int dz, bool isW)
        {
            int sx = dx != 0 ? 1 : -1, sz = dz != 0 ? 1 : -1;
            double hx = FluidHeightAt(p + sx * SX, isW), hz = FluidHeightAt(p + sz * SZ, isW);
            if (hx == 2 || hz == 2) return 1;
            double sum = 0, w = 0;
            void Add(double h) { if (h < 0) return; if (h >= 0.8) { sum += h * 10; w += 10; } else { sum += h; w += 1; } }
            if (hx > 0 || hz > 0)
            {
                double hd = FluidHeightAt(p + sx * SX + sz * SZ, isW);
                if (hd == 2) return 1;
                Add(hd);
            }
            Add(FluidHeightAt(p, isW)); Add(hx); Add(hz);
            return w != 0 ? sum / w : 8.0 / 9;
        }

        // FlowingFluid.getOwnHeight: amount / 9 (a source and falling liquid 8 / 9), 0 for no liquid
        double OwnHeight(int q, bool isW)
        {
            int id = ids[q];
            if (!(isW ? IsWaterId(id) : id == B.LAVA)) return 0;
            if (id != B.WATER && id != B.LAVA) return 8.0 / 9;
            int m = metaArr[q];
            return (m & 8) != 0 ? 8.0 / 9 : (8 - (m & 7)) / 9.0;
        }

        static readonly int[] SIDE_FACES = { 0, 1, 4, 5 };

        // FlowingFluid.getFlow: the pull of each side by the height difference; falling liquid against a
        // solid face gets a strong pull down (so a little sideways). Writes [x, z] (only its direction is used)
        void FluidFlow(int p, bool isW, double[] outv)
        {
            var ids = this.ids; double own = OwnHeight(p, isW);
            double fx = 0, fz = 0;
            foreach (int f in SIDE_FACES)
            {
                int q = p + FACE_DELTA[f], n = ids[q];
                bool liq = isW ? IsWaterId(n) : n == B.LAVA;
                if (!liq && IsLiquidId(n)) continue;              // another liquid: no part in the flow
                double h = liq ? OwnHeight(q, isW) : 0, d = 0;
                if (h == 0)
                {
                    if (SOLID[n] == 0) { double hb = OwnHeight(q - SY, isW); if (hb > 0) d = own - (hb - 8.0 / 9); }
                }
                else d = own - h;
                if (d != 0) { var c = FN[f]; fx += c[0] * d; fz += c[2] * d; }
            }
            int id = ids[p];
            if ((id == B.WATER || id == B.LAVA) && (metaArr[p] & 8) != 0)
            {
                foreach (int f in SIDE_FACES)
                {
                    int q = p + FACE_DELTA[f];
                    if ((OPAQUE[ids[q]] != 0 && ids[q] != B.ICE) || (OPAQUE[ids[q + SY]] != 0 && ids[q + SY] != B.ICE))
                    {
                        double l = Math.Sqrt(fx * fx + fz * fz);
                        if (l > 0) { fx /= l * 6; fz /= l * 6; }
                        break;
                    }
                }
            }
            outv[0] = fx; outv[1] = fz;
        }

        static readonly int[][] SIDE_H_IDX = { new[] { 3, 1 }, new[] { 0, 2 }, null, null, new[] { 2, 3 }, new[] { 1, 0 } };   // indices into h00 h10 h01 h11

        void Fluid(ushort[] ids, byte[] meta, byte[] light, int p, int id, int x, int y, int z, bool forceSource = false)
        {
            this.ids = ids; this.light = light;
            bool isW = id != B.LAVA;
            var buf = bufs[isW ? RL_TRANS : RL_SOLID];
            int still = FTEX[(isW ? B.WATER : B.LAVA) * 6 + 2], flowL = isW ? WATER_FLOW : LAVA_FLOW;
            if (flowL == 0) flowL = still;
            int layer = still;
            int up = ids[p + SY];
            bool upSame = isW ? IsWaterId(up) : up == B.LAVA;
            double h00, h10, h01, h11;
            if (upSame) h00 = h10 = h01 = h11 = 1;
            else
            {
                h00 = CornerHeight(p, 0, 0, isW); h10 = CornerHeight(p, 1, 0, isW);
                h01 = CornerHeight(p, 0, 1, isW); h11 = CornerHeight(p, 1, 1, isW);
            }
            int own = light[p];
            double X = x * 16, Y = y * 16, Z = z * 16;
            bool lava = !isW;
            void SetL(int q)
            {
                int l = own;
                if (q >= 0) { int nl = light[q]; if (OPAQUE[ids[q]] == 0) l = (Math.Max(nl >> 4, own >> 4) << 4) | Math.Max(nl & 15, own & 15); }
                int s = (l >> 4) * 16, b = lava ? 240 : (l & 15) * 16;
                for (int v = 0; v < 4; v++) { sk[v] = s; bl[v] = b; }
            }
            void ShadeAll(double f) { int s = (int)JsMath.Round(f * 255); for (int v = 0; v < 4; v++) sh[v] = s; }
            // top
            if (!upSame)
            {
                tpx[0] = (float)X; tpy[0] = (float)(Y + h01 * 16); tpz[0] = (float)(Z + 16);
                tpx[1] = (float)(X + 16); tpy[1] = (float)(Y + h11 * 16); tpz[1] = (float)(Z + 16);
                tpx[2] = (float)(X + 16); tpy[2] = (float)(Y + h10 * 16); tpz[2] = (float)Z;
                tpx[3] = (float)X; tpy[3] = (float)(Y + h00 * 16); tpz[3] = (float)Z;
                // LiquidBlockRenderer: no flow - the still tile; else the flowing one turned so that it runs
                // along the flow. Here in steps of 45 degrees: quarter turns by the corners' UVs, the eighth
                // turn by the shader (uvRot)
                var F = flowV;
                FluidFlow(p, isW, F);
                if (F[0] == 0 && F[1] == 0)
                {
                    tu[0] = 0; tv[0] = 16; tu[1] = 16; tv[1] = 16; tu[2] = 16; tv[2] = 0; tu[3] = 0; tv[3] = 0;
                }
                else
                {
                    layer = flowL;
                    int k = (int)JsMath.Round((Math.Atan2(F[1], F[0]) - Math.PI / 2) / (Math.PI / 4));
                    if ((k & 1) != 0) { uvRot = 1u << 30; k -= 1; }
                    double a = k * Math.PI / 4, sn = JsMath.Round(Math.Sin(a)) * 0.25, cs = JsMath.Round(Math.Cos(a)) * 0.25;
                    // MC's corner UVs (sprite units, a 32-texel sprite) -> texels of our 16-texel tile
                    float Tf(double w) => (float)JsMath.Round((2 * w - 0.5) * 16);
                    tu[0] = Tf(0.5 - cs + sn); tv[0] = Tf(0.5 + cs + sn);   // x 0, z 1
                    tu[1] = Tf(0.5 + cs + sn); tv[1] = Tf(0.5 + cs - sn);   // x 1, z 1
                    tu[2] = Tf(0.5 + cs - sn); tv[2] = Tf(0.5 - cs - sn);   // x 1, z 0
                    tu[3] = Tf(0.5 - cs - sn); tv[3] = Tf(0.5 - cs + sn);   // x 0, z 0
                }
                SetL(p + SY); ShadeAll(1);
                Quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, null, false);
                if (isW)
                {
                    // underside of the surface, seen from below water
                    float t;
                    t = tpx[1]; tpx[1] = tpx[3]; tpx[3] = t; t = tpy[1]; tpy[1] = tpy[3]; tpy[3] = t; t = tpz[1]; tpz[1] = tpz[3]; tpz[3] = t;
                    t = tu[1]; tu[1] = tu[3]; tu[3] = t; t = tv[1]; tv[1] = tv[3]; tv[3] = t;
                    ShadeAll(0.8);
                    Quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, null, false);
                }
                uvRot = 0;
            }
            // bottom (the still tile)
            layer = still;
            int dn = ids[p - SY];
            if (!(isW ? IsWaterId(dn) : dn == B.LAVA) && OPAQUE[dn] == 0)
            {
                tpx[0] = (float)(X + 16); tpy[0] = (float)Y; tpz[0] = (float)(Z + 16); tpx[1] = (float)X; tpy[1] = (float)Y; tpz[1] = (float)(Z + 16);
                tpx[2] = (float)X; tpy[2] = (float)Y; tpz[2] = (float)Z; tpx[3] = (float)(X + 16); tpy[3] = (float)Y; tpz[3] = (float)Z;
                tu[0] = 16; tv[0] = 16; tu[1] = 0; tv[1] = 16; tu[2] = 0; tv[2] = 0; tu[3] = 16; tv[3] = 0;
                SetL(p - SY); ShadeAll(0.55);
                Quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, null, false);
            }
            // sides: the flowing tile (here the whole 16-texel tile from 16 - 16 x height)
            layer = flowL;
            double[] hs = { h00, h10, h01, h11 };
            foreach (int f in SIDE_FACES)
            {
                int q = p + FACE_DELTA[f];
                int n = ids[q];
                if (OPAQUE[n] != 0) continue;
                if (isW ? IsWaterId(n) : n == B.LAVA) continue;
                var c = FC[f]; var hh = SIDE_H_IDX[f];
                for (int v = 0; v < 4; v++)
                {
                    var cc = c[v];
                    tpx[v] = (float)(X + cc[0] * 16); tpz[v] = (float)(Z + cc[2] * 16);
                    tpy[v] = (float)(Y + (cc[1] != 0 ? (v == 2 ? hs[hh[1]] : hs[hh[0]]) * 16 : 0));
                }
                // BL,BR,TR,TL ; top corners: v2 = TR (hh[1]) , v3 = TL (hh[0])
                tu[0] = 0; tv[0] = 16; tu[1] = 16; tv[1] = 16; tu[2] = 16; tv[2] = (float)(16 - (tpy[2] - Y)); tu[3] = 0; tv[3] = (float)(16 - (tpy[3] - Y));
                SetL(q); ShadeAll(FACE_SHADE[f]);
                Quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, null, false);
                if (isW)
                {
                    float t;
                    t = tpx[1]; tpx[1] = tpx[3]; tpx[3] = t; t = tpy[1]; tpy[1] = tpy[3]; tpy[3] = t; t = tpz[1]; tpz[1] = tpz[3]; tpz[3] = t;
                    t = tu[1]; tu[1] = tu[3]; tu[3] = t; t = tv[1]; tv[1] = tv[3]; tv[3] = t;
                    Quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, null, false);
                }
            }
        }

        // ---------------------------------------------------------------- visibility graph
        byte[] Visibility(ushort[] ids)
        {
            var vis = this.vis; var st = stack;
            Array.Clear(vis, 0, vis.Length);
            var res = new byte[6];
            int openCount = 0;
            for (int i = 0; i < 4096; i++)
            {
                int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
                if (OPAQUE[ids[Pidx(x, y, z)]] != 0) vis[i] = 1; else openCount++;
            }
            if (openCount == 0) return res;
            if (openCount == 4096) { for (int a = 0; a < 6; a++) res[a] = 63; return res; }
            for (int s = 0; s < 4096; s++)
            {
                if (vis[s] != 0) continue;
                int sp = 0, faces = 0;
                st[sp++] = s; vis[s] = 1;
                while (sp != 0)
                {
                    int i = st[--sp];
                    int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
                    if (x == 15) faces |= 1; if (x == 0) faces |= 2; if (y == 15) faces |= 4; if (y == 0) faces |= 8; if (z == 15) faces |= 16; if (z == 0) faces |= 32;
                    if (x < 15 && vis[i + 1] == 0) { vis[i + 1] = 1; st[sp++] = i + 1; }
                    if (x > 0 && vis[i - 1] == 0) { vis[i - 1] = 1; st[sp++] = i - 1; }
                    if (z < 15 && vis[i + 16] == 0) { vis[i + 16] = 1; st[sp++] = i + 16; }
                    if (z > 0 && vis[i - 16] == 0) { vis[i - 16] = 1; st[sp++] = i - 16; }
                    if (y < 15 && vis[i + 256] == 0) { vis[i + 256] = 1; st[sp++] = i + 256; }
                    if (y > 0 && vis[i - 256] == 0) { vis[i - 256] = 1; st[sp++] = i - 256; }
                }
                for (int a = 0; a < 6; a++) if ((faces & (1 << a)) != 0) res[a] |= (byte)faces;
            }
            return res;
        }
    }
}
