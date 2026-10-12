// Port of the template / jigsaw structure part of worldgen.js: Minecraft's StructureTemplate pieces
// placed with a rotation about a pivot, RandomSpreadStructurePlacement starts, igloos, shipwrecks,
// ocean ruins, villages and pillager outposts (JigsawPlacement) and the Beardifier.
using System;
using System.Collections.Generic;
using static Maincraft.WG;
using static Maincraft.Blocks;

namespace Maincraft
{
    /// <summary>A structure piece: a template piece, a jigsaw (village) piece or a piece built by code.</summary>
    public sealed class Piece
    {
        // template pieces
        public string name;
        public int x, y, z, rot, px, pz, pal, salt;
        public double integrity = 1;
        public bool large;
        public int[] box;
        public Action<WorldGen, string, int, int, int, int> marker;
        public Action<WorldGen, Piece> after;
        // jigsaw pieces
        public VilPiece vil;
        // pieces built by code (bigstructures)
        public Action<WorldGen, Piece> build;
        public int sx, sy, sz, margin;
        public bool noTrees, fallen, big, underground, mesa;
        public ShCell cell;
        public MineSeg seg;
    }

    public sealed class VilPiece
    {
        public JigEl e;
        public int x, y, z, rot;
        public int[] box;
        public int gd;
        public List<int[]> junctions = new List<int[]>();
    }

    /// <summary>Decoded template: grid of state + 1, per palette the block id / meta of each state (-1 nothing).</summary>
    public sealed class Tpl
    {
        public int sx, sy, sz;
        public byte[] g;
        public int[][][] pals;   // pals[palette][state] = {id, meta}
        public int[][] palIds;   // palIds[entry] = {id, meta}
        public (int x, int y, int z, string name)[] marks;
        public int[][] jig;
        public Dictionary<string, Dictionary<string, double[][]>> rules;
        public Dictionary<string, double> rulesRot;
        public (int x, int y, int z, string table)[] loot;
    }

    struct Jig
    {
        public int x, y, z, front, top, fin, roll;
        public string name, target, pool;
    }

    sealed class FreeShape { public int[] o; public List<int[]> holes = new List<int[]>(); }

    public sealed partial class WorldGen
    {
        readonly Dictionary<string, Tpl> TPL_CACHE = new Dictionary<string, Tpl>();
        readonly Dictionary<string, int> poolMaxY = new Dictionary<string, int>();

        // ---------------------------------------------------------------- template structures
        public (int, int) SpreadStart(StructSet set, int rx, int rz)
        {
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + set.salt), rx, rz)); int n = set.spacing - set.separation;
            int a = rx * set.spacing + (int)(rnd.Next() * n);
            int b = rz * set.spacing + (int)(rnd.Next() * n);
            return (a, b);
        }

        static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

        void Structures()
        {
            foreach (var set in STRUCTURE_SETS)
            {
                int R = set.reach;
                int r0x = FloorDiv(cx - R, set.spacing), r1x = FloorDiv(cx + R, set.spacing);
                int r0z = FloorDiv(cz - R, set.spacing), r1z = FloorDiv(cz + R, set.spacing);
                for (int rz = r0z; rz <= r1z; rz++) for (int rx = r0x; rx <= r1x; rx++)
                {
                    var (sx, sz) = SpreadStart(set, rx, rz);
                    if (Math.Abs(sx - cx) > R || Math.Abs(sz - cz) > R) continue;
                    var st = StructStart(set, sx, sz);
                    if (st != null) foreach (var p in st) PlacePiece(p);
                }
            }
        }

        List<Piece> StructStart(StructSet set, int sx, int sz)
        {
            var key = (set.salt, sx, sz);
            if (!structCache.TryGetValue(key, out var st)) { st = set.start(this, sx, sz); if (structCache.Count > 4000) structCache.Clear(); structCache[key] = st; }
            return st;
        }

        // decoded template: grid of state + 1, per palette the block id / meta of each state (-1 nothing)
        public Tpl GetTpl(string name, TplSrc data = null)
        {
            if (TPL_CACHE.TryGetValue(name, out var T)) return T;
            var S = data ?? StructData.STRUCT_TPL[name];
            int sx = S.sx, sy = S.sy, sz = S.sz;
            var g = new byte[sx * sy * sz];
            var raw = Convert.FromBase64String(S.g);
            for (int i = 0, o = 0; i + 1 < raw.Length; i += 2)
            {
                byte v = raw[i]; int n = raw[i + 1];
                int end = Math.Min(g.Length, o + n);
                for (int q = o; q < end; q++) g[q] = v;
                o += n;
            }
            int[] Ent(int e)
            {
                string key = S.pKey[e]; int id = key == "AIR" ? 0 : Blocks.Id(key);
                return new[] { S.pWet[e] != 0 && id < NB && WET[id] != 0 ? WET[id] : id, S.pMeta[e] };
            }
            var pals = new int[S.m.Length][][];
            for (int p = 0; p < S.m.Length; p++)
            {
                pals[p] = new int[S.m[p].Length][];
                for (int s = 0; s < S.m[p].Length; s++) { int e = S.m[p][s]; pals[p][s] = e == 0 ? new[] { -1, 0 } : Ent(e - 1); }
            }
            var palIds = new int[S.pKey.Length][];
            for (int e = 0; e < S.pKey.Length; e++) palIds[e] = Ent(e);
            T = new Tpl { sx = sx, sy = sy, sz = sz, g = g, pals = pals, palIds = palIds, marks = S.d, jig = S.j, rules = S.r, rulesRot = S.rRot, loot = S.l };
            TPL_CACHE[name] = T;
            return T;
        }

        // StructureTemplate.transform: template position -> offset from the template origin
        public static (int, int) TplPos(int x, int z, int rot, int px, int pz)
        {
            switch (rot)
            {
                case 1: return (px + pz - z, pz - px + x);
                case 2: return (px + px - x, pz + pz - z);
                case 3: return (px - pz + z, px + pz - x);
                default: return (x, z);
            }
        }

        // placed bounds of a piece (world x0, z0, x1, z1)
        int[] PieceBox(Piece p)
        {
            var T = GetTpl(p.name); var a = TplPos(0, 0, p.rot, p.px, p.pz); var b = TplPos(T.sx - 1, T.sz - 1, p.rot, p.px, p.pz);
            return new[] { p.x + Math.Min(a.Item1, b.Item1), p.z + Math.Min(a.Item2, b.Item2), p.x + Math.Max(a.Item1, b.Item1), p.z + Math.Max(a.Item2, b.Item2) };
        }

        void PlacePiece(Piece p)
        {
            int x0 = this.x0, z0 = this.z0;
            if (p.vil != null)
            {
                var b = p.box;
                if (b[0] > x0 + 15 || b[3] < x0 || b[2] > z0 + 15 || b[5] < z0) return;
                if (vilGround == null)
                {
                    // WORLD_SURFACE_WG of this chunk's columns before any piece is placed
                    var G = vilGround = new short[256];
                    for (int i = 0; i < 256; i++) { int y = WORLD_MAX_Y - 1; while (y > WORLD_MIN_Y && ids[CI(i & 15, y, i >> 4)] == 0) y--; G[i] = (short)y; }
                }
                PlaceVillagePiece(p);
                return;
            }
            if (p.build != null) { var b = p.box; if (b[0] <= x0 + 15 && b[3] >= x0 && b[2] <= z0 + 15 && b[5] >= z0) p.build(this, p); return; }
            var bx = p.box ?? (p.box = PieceBox(p));
            if (bx[0] > x0 + 15 || bx[2] < x0 || bx[1] > z0 + 15 || bx[3] < z0) return;
            var T = GetTpl(p.name); var pal = T.pals[p.pal]; var g = T.g; int seed = this.seed;
            int sx = T.sx, sy = T.sy, sz = T.sz, rot = p.rot, px = p.px, pz = p.pz;
            for (int y = 0; y < sy; y++)
            {
                int wy = p.y + y;
                if (wy <= WORLD_MIN_Y || wy >= WORLD_MAX_Y) continue;
                for (int z = 0; z < sz; z++) for (int x = 0; x < sx; x++)
                {
                    int v = g[(y * sz + z) * sx + x];
                    if (v == 0) continue;
                    var e = pal[v - 1];
                    if (e[0] < 0) continue;
                    var o = TplPos(x, z, rot, px, pz); int wx = p.x + o.Item1, wz = p.z + o.Item2;
                    if (wx < x0 || wx > x0 + 15 || wz < z0 || wz > z0 + 15) continue;
                    // BlockRotProcessor: each block kept with the piece's integrity
                    if (p.integrity < 1 && Hash.Hash3(unchecked(seed + p.salt), wx, wy, wz) > p.integrity) continue;
                    int i = CI(wx - x0, wy, wz - z0), cur = ids[i];
                    int id = e[0];
                    // a block that can hold water placed into a water source becomes waterlogged
                    if (id != 0 && id < NB && WET[id] != 0 && (cur == B.WATER && meta[i] == 0 || IsWaterId(cur))) id = WET[id];
                    ids[i] = (ushort)id; meta[i] = (byte)(id != 0 ? RotMeta(id, e[1], rot) : 0);
                }
            }
            if (p.marker != null) foreach (var (mx, my, mz, nm) in T.marks)
            {
                var o = TplPos(mx, mz, rot, px, pz); int wx = p.x + o.Item1, wy = p.y + my, wz = p.z + o.Item2;
                if (wx < x0 || wx > x0 + 15 || wz < z0 || wz > z0 + 15 || wy <= WORLD_MIN_Y + 1 || wy >= WORLD_MAX_Y) continue;
                p.marker(this, nm, wx, wy, wz, CI(wx - x0, wy, wz - z0));
            }
            p.after?.Invoke(this, p);
        }

        // chest at index i gets a loot table (rolled when opened)
        void LootChest(int i, int table) { if (DryId(ids[i]) == B.CHEST) meta[i] = (byte)((meta[i] & 3) | (table << 2)); }

        // OCEAN_FLOOR_WG / WORLD_SURFACE_WG heights from the density surface
        int FloorH(int x, int z)
        {
            long k = K2(x, z);
            if (!surfCache.TryGetValue(k, out int h)) { h = SurfaceAt(x, z) + 1; if (surfCache.Count > 50000) surfCache.Clear(); surfCache[k] = h; }
            return h;
        }
        int SurfH(int x, int z) => Math.Max(FloorH(x, z), SEA);
        // ChunkGenerator.getFirstFreeHeight(WORLD_SURFACE_WG): over the sea the first free cell is the one above the water
        int SurfWG(int x, int z) => Math.Max(FloorH(x, z), SEA + 1);

        // IglooStructure / IglooPieces: the igloo on the surface; half of them with a ladder shaft of
        // 4..11 sections down to a basement laboratory
        public List<Piece> IglooStart(int cx, int cz)
        {
            if (!IGLOO_BIOMES.Contains(BiomeAt(cx * 16, cz * 16))) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 38810541 * 3), cx, cz)); int rot = (int)(rnd.Next() * 4);
            int X = cx * 16, Z = cz * 16; var outp = new List<Piece>();
            Piece MakePiece(string name, int down)
            {
                int ox, oy, oz, ppx, ppz;
                if (name == "igloo/top") { ox = 0; oy = 0; oz = 0; ppx = 3; ppz = 5; }
                else if (name == "igloo/middle") { ox = 2; oy = -3; oz = 4; ppx = 1; ppz = 1; }
                else { ox = 0; oy = -3; oz = -2; ppx = 3; ppz = 7; }
                var p = new Piece { name = name, x = X + ox, y = 90 + oy - down, z = Z + oz, rot = rot, px = ppx, pz = ppz, integrity = 1 };
                // height: WORLD_SURFACE_WG at the template position + rotate(3 - offset x, 0, -offset z) - 91
                var r = TplPos(3 - ox, -oz, rot, ppx, ppz); int h = SurfH(p.x + r.Item1, p.z + r.Item2);
                p.y += h - 90 - 1;
                return p;
            }
            if (rnd.Next() < 0.5)
            {
                int n = (int)(rnd.Next() * 8) + 4;
                var lab = MakePiece("igloo/bottom", n * 3);
                lab.marker = (g, name, x, y, z, i) => { if (name == "chest") g.LootChest(i - 256, Loot.Get("IGLOO")); };
                outp.Add(lab);
                for (int j = 0; j < n - 1; j++) outp.Add(MakePiece("igloo/middle", j * 3));
            }
            var top = MakePiece("igloo/top", 0);
            // without the shaft the trapdoor spot becomes snow
            top.after = (g, p) =>
            {
                var r = TplPos(3, 5, p.rot, p.px, p.pz); int x = p.x + r.Item1, z = p.z + r.Item2, y = p.y;
                if (x < g.x0 || x > g.x0 + 15 || z < g.z0 || z > g.z0 + 15) return;
                int i = CI(x - g.x0, y, z - g.z0), under = g.ids[i - 256];
                if (under != 0 && DryId(under) != B.LADDER) { g.ids[i] = B.SNOW; g.meta[i] = 0; }
            };
            outp.Add(top);
            return outp;
        }

        // ShipwreckStructure / ShipwreckPieces: a random wreck (beached ones on beaches, half buried in
        // the sand) laid on the average ocean floor of its area, loot chests by marker
        public List<Piece> ShipwreckStart(int cx, int cz)
        {
            int bio = BiomeAt(cx * 16, cz * 16); var bp = BPROP[bio];
            bool beached = bio == BI.BEACH || bio == BI.SNOWY_BEACH;
            if (!beached && !(bp != null && bp.ocean && bio != BI.RIVER && bio != BI.FROZEN_RIVER)) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 61294733 * 3), cx, cz)); int rot = (int)(rnd.Next() * 4);
            var list = beached ? SHIPWRECK_BEACHED : SHIPWRECK_OCEAN; string name = "shipwreck/" + list[(int)(rnd.Next() * list.Length)];
            var T = GetTpl(name); int X = cx * 16, Z = cz * 16;
            double sum = 0; int mn = 1000000000;
            for (int z = 0; z < T.sz; z++) for (int x = 0; x < T.sx; x++) { int h = beached ? SurfH(X + x, Z + z) : FloorH(X + x, Z + z); sum += h; mn = Math.Min(mn, h); }
            int y = beached ? mn - (T.sy >> 1) - (int)(rnd.Next() * 3) : (int)Math.Floor(sum / (T.sx * T.sz));
            var p = new Piece { name = name, x = X, y = y, z = Z, rot = rot, px = 4, pz = 15, integrity = 1, pal = (int)(rnd.Next() * T.pals.Length) };
            p.marker = (g, nm, x, yy, z, i) => { if (SHIP_LOOT.TryGetValue(nm, out int t) && t != 0) g.LootChest(i - 256, t); };
            return new List<Piece> { p };
        }

        // OceanRuinStructure / OceanRuinPieces: cold ruins (brick with cracked 0.7 and mossy 0.5 overlays)
        // or warm sandstone ones; 30% large, those mostly with 4..8 small ruins around
        public List<Piece> OceanRuinStart(int cx, int cz)
        {
            int bio = BiomeAt(cx * 16, cz * 16);
            bool warm = RUIN_WARM.Contains(bio), cold = RUIN_COLD.Contains(bio);
            if (!warm && !cold) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 29467103 * 3), cx, cz));
            int Ri(int n) => (int)(rnd.Next() * n);
            int NI(int a, int b) => a + Ri(b - a + 1);
            int X = cx * 16, Z = cz * 16; var outp = new List<Piece>();
            int salt = 0;
            void Add(int x, int z, int rot, bool large, double integrity)
            {
                void Mk(string name, double integ)
                {
                    var p = new Piece { name = name, x = x, y = 0, z = z, rot = rot, px = 0, pz = 0, integrity = integ, salt = 900 + (salt++) * 7, large = large };
                    // height: ocean floor at the corner, lowered onto the floor across the footprint (getHeight)
                    var T = GetTpl(name); var c = TplPos(T.sx - 1, T.sz - 1, rot, 0, 0);
                    int yy = FloorH(x, z);
                    int j = 512, l = 0;
                    int xa = Math.Min(x, x + c.Item1), xb = Math.Max(x, x + c.Item1), za = Math.Min(z, z + c.Item2), zb = Math.Max(z, z + c.Item2);
                    for (int qz = za; qz <= zb; qz++) for (int qx = xa; qx <= xb; qx++)
                    {
                        int k1 = Math.Min(yy - 1, FloorH(qx, qz) - 1);
                        j = Math.Min(j, k1); if (k1 < yy - 3) l++;
                    }
                    p.y = (yy - 1 - j > 2 && l > Math.Abs(c.Item1) - 2) ? j + 1 : yy;
                    p.marker = (g, nm, mx, my, mz, i) =>
                    {
                        if (nm == "chest") { bool w = IsWaterId(g.ids[i]); g.ids[i] = (ushort)(w ? WET[B.CHEST] : B.CHEST); g.meta[i] = (byte)(2 | ((large ? Loot.Get("UNDERWATER_RUIN_BIG") : Loot.Get("UNDERWATER_RUIN_SMALL")) << 2)); }
                        else if (nm == "drowned") { bool w = IsWaterId(g.ids[i + 256]) || IsWaterId(g.ids[i]); g.ids[i] = (ushort)(w ? B.WATER : 0); g.meta[i] = 0; }   // water when it is under water
                    };
                    outp.Add(p);
                }
                if (warm) Mk("underwater_ruin/" + (large ? "big_warm_" + (4 + Ri(4)) : "warm_" + (1 + Ri(8))), integrity);
                else
                {
                    int i = large ? 1 + Ri(3) : 1 + Ri(8); string pre = large ? "big_" : "";
                    Mk("underwater_ruin/" + pre + "brick_" + i, integrity);
                    Mk("underwater_ruin/" + pre + "cracked_" + i, 0.7);
                    Mk("underwater_ruin/" + pre + "mossy_" + i, 0.5);
                }
            }
            int rot0 = Ri(4); bool large0 = rnd.Next() <= 0.3;
            Add(X, Z, rot0, large0, large0 ? 0.9 : 0.8);
            if (large0 && rnd.Next() <= 0.9)
            {
                var c = TplPos(15, 15, rot0, 0, 0); int bx0 = Math.Min(X, X + c.Item1), bx1 = Math.Max(X, X + c.Item1), bz0 = Math.Min(Z, Z + c.Item2), bz1 = Math.Max(Z, Z + c.Item2);
                int ax = bx0, az = bz0;
                var spots = new List<(int, int)>();
                // the offsets are drawn in the original's literal order (a, b of each pair, pair by pair)
                { int a = -16 + NI(1, 8), b = 16 + NI(1, 7); spots.Add((ax + a, az + b)); }
                { int a = -16 + NI(1, 8), b = NI(1, 7); spots.Add((ax + a, az + b)); }
                { int a = -16 + NI(1, 8), b = -16 + NI(4, 8); spots.Add((ax + a, az + b)); }
                { int a = NI(1, 7), b = 16 + NI(1, 7); spots.Add((ax + a, az + b)); }
                { int a = NI(1, 7), b = -16 + NI(4, 6); spots.Add((ax + a, az + b)); }
                { int a = 16 + NI(1, 7), b = 16 + NI(3, 8); spots.Add((ax + a, az + b)); }
                { int a = 16 + NI(1, 7), b = NI(1, 7); spots.Add((ax + a, az + b)); }
                { int a = 16 + NI(1, 7), b = -16 + NI(4, 8); spots.Add((ax + a, az + b)); }
                for (int n = NI(4, 8); n > 0 && spots.Count > 0; n--)
                {
                    int si = Ri(spots.Count); var (qx, qz) = spots[si]; spots.RemoveAt(si);
                    int r1 = Ri(4); var e = TplPos(5, 6, r1, 0, 0);
                    int ex0 = Math.Min(qx, qx + e.Item1), ex1 = Math.Max(qx, qx + e.Item1), ez0 = Math.Min(qz, qz + e.Item2), ez1 = Math.Max(qz, qz + e.Item2);
                    if (!(ex0 <= bx1 && ex1 >= bx0 && ez0 <= bz1 && ez1 >= bz0)) Add(qx, qz, r1, false, 0.8);
                }
            }
            return outp;
        }

        // ---------------------------------------------------------------- villages (jigsaw structures)
        static JigPool VilPool(string name) => name != null && StructData.JIG_POOLS.TryGetValue(name, out var p) ? p : null;

        Tpl VilTpl(string name)
        {
            if (TPL_CACHE.TryGetValue(name, out var T)) return T;
            return GetTpl(name, StructData.JIG_TPL[name]);
        }

        // template's box when placed at (x, y, z) with a rotation about its origin
        static int[] TplBox(Tpl T, int x, int y, int z, int rot)
        {
            var a = TplPos(0, 0, rot, 0, 0); var b = TplPos(T.sx - 1, T.sz - 1, rot, 0, 0);
            return new[] { x + Math.Min(a.Item1, b.Item1), y, z + Math.Min(a.Item2, b.Item2), x + Math.Max(a.Item1, b.Item1), y + T.sy - 1, z + Math.Max(a.Item2, b.Item2) };
        }

        static int RotDir(int d, int r) => d < 4 ? (d + r) & 3 : d;

        // the pool's elements, each repeated by its weight, shuffled
        static List<JigEl> VilShuffled(JigPool pool, Mulberry rnd)
        {
            var outp = new List<JigEl>();
            if (pool == null) return outp;
            foreach (var e in pool.e) for (int k = 0; k < e.Weight; k++) outp.Add(e);
            for (int i = outp.Count - 1; i > 0; i--) { int j = (int)(rnd.Next() * (i + 1)); var t = outp[i]; outp[i] = outp[j]; outp[j] = t; }
            return outp;
        }

        int VilMaxY(string name)
        {
            var P = VilPool(name);
            if (P == null) return 0;
            if (!poolMaxY.TryGetValue(name, out int maxY))
            {
                maxY = 0;
                foreach (var e in P.e)
                {
                    if (e.Type == 0 || e.Type == 3) { var b = VilElBox(e, 0, 0, 0, 0); maxY = Math.Max(maxY, b[4] - b[1] + 1); }
                    else if (e.Type == 1) maxY = Math.Max(maxY, 1);
                }
                poolMaxY[name] = maxY;
            }
            return maxY;
        }

        // jigsaws of an element placed at (x, y, z) with rotation r in world space, shuffled
        List<Jig> VilJigsaws(JigEl e, int x, int y, int z, int r, Mulberry rnd)
        {
            var outp = new List<Jig>();
            // FeaturePoolElement: one jigsaw facing down named "bottom"
            if (e.Type == 1) { outp.Add(new Jig { x = x, y = y, z = z, front = 5, top = 0, name = "bottom", target = "empty", pool = "empty", fin = 0, roll = 1 }); return outp; }
            if (e.Type != 0 && e.Type != 3) return outp;
            var T = VilTpl(e.Type == 3 ? e.Names[0] : e.Name);
            var S = StructData.JIG_STR;
            foreach (var j in T.jig)
            {
                var o = TplPos(j[0], j[2], r, 0, 0);
                outp.Add(new Jig { x = x + o.Item1, y = y + j[1], z = z + o.Item2, front = RotDir(j[3], r), top = RotDir(j[4], r), name = S[j[5]], target = S[j[6]], pool = S[j[7]], fin = j[8], roll = j[9] });
            }
            for (int i = outp.Count - 1; i > 0; i--) { int k = (int)(rnd.Next() * (i + 1)); var t = outp[i]; outp[i] = outp[k]; outp[k] = t; }
            return outp;
        }

        int[] VilElBox(JigEl e, int x, int y, int z, int r)
        {
            if (e.Type == 0) return TplBox(VilTpl(e.Name), x, y, z, r);
            if (e.Type == 3)
            {   // ListPoolElement: the union of its elements
                var b = TplBox(VilTpl(e.Names[0]), x, y, z, r);
                for (int i = 1; i < e.Names.Length; i++) { var c = TplBox(VilTpl(e.Names[i]), x, y, z, r); for (int k = 0; k < 3; k++) { b[k] = Math.Min(b[k], c[k]); b[k + 3] = Math.Max(b[k + 3], c[k + 3]); } }
                return b;
            }
            return new[] { x, y, z, x, y, z };
        }

        // pillager outposts: frequency 0.2, never within 10 chunks of a village placement chunk
        public List<Piece> OutpostStart(int x, int z)
        {
            if (Hash.Hash2(unchecked(seed + 73810259 * 7), x, z) >= 0.2) return null;
            var V = STRUCTURE_SETS_VIL;
            for (int rz = FloorDiv(z - 10, V.spacing); rz <= FloorDiv(z + 10, V.spacing); rz++)
                for (int rx = FloorDiv(x - 10, V.spacing); rx <= FloorDiv(x + 10, V.spacing); rx++)
                {
                    var (vx, vz) = SpreadStart(V, rx, rz);
                    if (Math.Abs(vx - x) <= 10 && Math.Abs(vz - z) <= 10) return null;
                }
            return JigsawStart(STRUCTURE_SETS_OUTPOST, x, z);
        }

        // JigsawStructure start: for structure sets with several structures (the village types) they are
        // tried in a random order and the first whose biome holds at the start's centre generates
        public List<Piece> JigsawStart(StructSet set, int cx, int cz)
        {
            var order = (JigType[])set.types.Clone(); var r0 = new Mulberry(Hash.Hash2i(unchecked(seed + set.salt * 5), cx, cz));
            for (int i = order.Length - 1; i > 0; i--) { int j = (int)(r0.Next() * (i + 1)); var t = order[i]; order[i] = order[j]; order[j] = t; }
            foreach (var type in order)
            {
                var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + set.salt * 3), cx, cz));
                int rot = (int)(rnd.Next() * 4); var start = VilPool(type.pool);
                var list = VilShuffled(start, rnd); int ei = (int)(rnd.Next() * list.Count);
                var e = ei < list.Count ? list[ei] : null;
                if (e == null || e.Type == 2) continue;
                int X = cx * 16, Z = cz * 16; var box = VilElBox(e, X, 0, Z, rot);
                int mx = (box[0] + box[3]) >> 1, mz = (box[2] + box[5]) >> 1;
                if (Array.IndexOf(type.biomes, BiomeAt(mx, mz)) < 0) continue;
                // projected to WORLD_SURFACE_WG at the centre, the floor (ground level delta 1) one below
                int k = SurfWG(mx, mz);
                var first = new VilPiece { e = e, x = X, y = k - 1, z = Z, rot = rot, box = VilElBox(e, X, k - 1, Z, rot), gd = 1 };
                return VilAssemble(first, mx, k, mz, rnd, set.depth);
            }
            return null;
        }

        List<Piece> VilAssemble(VilPiece first, int cx, int cy, int cz, Mulberry rnd, int MAXD)
        {
            const int D = 80; var pieces = new List<VilPiece> { first };
            // a building (rigid template with a real footprint) also claims the ground under it
            int[] Claim(JigEl e, int[] b)
            {
                if (e.Type != 0 || e.Proj != 0) return b;
                var T = VilTpl(e.Name);
                return T.sx * T.sz >= 9 ? new[] { b[0], b[1] - 32, b[2], b[3], b[4], b[5] } : b;
            }
            // free space: an outer box minus placed boxes (VoxelShape ONLY_FIRST joins), shared as in Minecraft
            var outer = new FreeShape { o = new[] { cx - D, cy - D, cz - D, cx + D + 1, cy + D + 1, cz + D + 1 } };
            outer.holes.Add(Box6(Claim(first.e, first.box)));
            bool Fits(FreeShape sh, int[] b)
            {
                double a0 = b[0] + 0.25, a1 = b[1] + 0.25, a2 = b[2] + 0.25, a3 = b[3] + 0.75, a4 = b[4] + 0.75, a5 = b[5] + 0.75; var o = sh.o;
                if (a0 < o[0] || a1 < o[1] || a2 < o[2] || a3 > o[3] || a4 > o[4] || a5 > o[5]) return false;
                foreach (var h in sh.holes) if (a0 < h[3] && a3 > h[0] && a1 < h[4] && a4 > h[1] && a2 < h[5] && a5 > h[2]) return false;
                return true;
            }
            var queue = new Queue<(VilPiece, FreeShape, int)>();
            queue.Enqueue((first, outer, 0));
            while (queue.Count > 0)
            {
                var (piece, free, depth) = queue.Dequeue();
                bool rigid = piece.e.Proj == 0; var box = piece.box; int i0 = box[1];
                FreeShape inner = null;
                foreach (var jg in VilJigsaws(piece.e, piece.x, piece.y, piece.z, piece.rot, rnd))
                {
                    int fd = jg.front, tx = jg.x + (fd == 3 ? 1 : fd == 1 ? -1 : 0), ty = jg.y + (fd == 4 ? 1 : fd == 5 ? -1 : 0), tz = jg.z + (fd == 0 ? 1 : fd == 2 ? -1 : 0);
                    int j = jg.y - i0;
                    int k = -1;
                    string poolName = jg.pool; var pool = VilPool(poolName);
                    if (pool == null && poolName != "empty") continue;
                    var fb = pool != null ? VilPool(pool.f) : null;
                    bool inside = tx >= box[0] && tx <= box[3] && ty >= box[1] && ty <= box[4] && tz >= box[2] && tz <= box[5];
                    FreeShape sh;
                    if (inside) { if (inner == null) inner = new FreeShape { o = Box6(box) }; sh = inner; } else sh = free;
                    var cands = depth != MAXD ? VilShuffled(pool, rnd) : new List<JigEl>();
                    cands.AddRange(VilShuffled(fb, rnd));
                    bool placed = false;
                    foreach (var e1 in cands)
                    {
                        if (e1.Type == 2) break;
                        int[] rots = { 0, 1, 2, 3 };
                        for (int i = 3; i > 0; i--) { int q = (int)(rnd.Next() * (i + 1)); int t = rots[i]; rots[i] = rots[q]; rots[q] = t; }
                        foreach (int r1 in rots)
                        {
                            var js1 = VilJigsaws(e1, 0, 0, 0, r1, rnd); var b1 = VilElBox(e1, 0, 0, 0, r1);
                            int exp = 0;
                            if ((e1.Type == 0 || e1.Type == 3) && b1[4] - b1[1] + 1 <= 16)
                            {
                                foreach (var q in js1)
                                {
                                    int f2 = q.front, qx = q.x + (f2 == 3 ? 1 : f2 == 1 ? -1 : 0), qy = q.y + (f2 == 4 ? 1 : f2 == 5 ? -1 : 0), qz = q.z + (f2 == 0 ? 1 : f2 == 2 ? -1 : 0);
                                    if (!(qx >= b1[0] && qx <= b1[3] && qy >= b1[1] && qy <= b1[4] && qz >= b1[2] && qz <= b1[5])) continue;
                                    string pn = q.pool; var P = VilPool(pn);
                                    exp = Math.Max(exp, Math.Max(VilMaxY(pn), P != null ? VilMaxY(P.f) : 0));
                                }
                            }
                            foreach (var j1 in js1)
                            {
                                // JigsawBlock.canAttach: facing each other, same top unless rollable, target -> name
                                if (j1.front != ((fd < 4) ? (fd + 2) & 3 : fd == 4 ? 5 : 4)) continue;
                                if (jg.roll == 0 && jg.top != j1.top) continue;
                                if (jg.target != j1.name) continue;
                                int ox = tx - j1.x, oz = tz - j1.z;
                                bool rigid1 = e1.Proj == 0; int k1 = j1.y, l1 = j - k1 + (fd == 4 ? 1 : fd == 5 ? -1 : 0);
                                int i2;
                                if (rigid && rigid1) i2 = i0 + l1;
                                else { if (k == -1) k = SurfWG(jg.x, jg.z); i2 = k - k1; }
                                int y1 = i2;      // template minY is its origin: box minY = position y
                                var b3 = VilElBox(e1, ox, y1, oz, r1);
                                var bt = (int[])b3.Clone();
                                if (exp > 0) bt[4] = Math.Max(bt[4], bt[1] + Math.Max(exp + 1, b3[4] - b3[1]));
                                var bc = sh == free ? Claim(e1, bt) : bt;
                                if (!Fits(sh, bc)) continue;
                                sh.holes.Add(Box6(bc));
                                int gd = rigid1 ? piece.gd - l1 : 1;   // StructurePoolElement.getGroundLevelDelta() is 1
                                var p1 = new VilPiece { e = e1, x = ox, y = y1, z = oz, rot = r1, box = bt, gd = gd };
                                int i3;
                                if (rigid) i3 = i0 + j;
                                else if (rigid1) i3 = i2 + k1;
                                else { if (k == -1) k = SurfWG(jg.x, jg.z); i3 = k + l1 / 2; }
                                piece.junctions.Add(new[] { tx, i3 - j + piece.gd, tz });
                                p1.junctions.Add(new[] { jg.x, i3 - k1 + gd, jg.z });
                                pieces.Add(p1);
                                if (depth + 1 <= MAXD) queue.Enqueue((p1, sh, depth + 1));
                                placed = true; break;
                            }
                            if (placed) break;
                        }
                        if (placed) break;
                    }
                }
            }
            var res = new List<Piece>(pieces.Count);
            foreach (var p in pieces) res.Add(new Piece { vil = p, box = p.box });
            return res;
        }

        // blocks of a village piece inside this chunk: template states through the element's processors,
        // jigsaws replaced by their final state, streets laid on the ground (GravityProcessor -1)
        void PlaceVillagePiece(Piece p)
        {
            var v = p.vil; var e = v.e;
            if (e.Type == 1) { VillageFeature(e.Name, v.x, v.y, v.z); return; }
            if (e.Type == 3) { for (int i = 0; i < e.Names.Length; i++) PlaceJigsawTemplate(v, e.Names[i], e.Procs != null && i < e.Procs.Length ? e.Procs[i] : null, e.Proj == 1); return; }
            if (e.Type == 0) PlaceJigsawTemplate(v, e.Name, e.Proc, e.Proj == 1);
        }

        void PlaceJigsawTemplate(VilPiece v, string name, string proc, bool terrain)
        {
            int x0 = this.x0, z0 = this.z0; int seed = this.seed;
            var T = VilTpl(name); var pal = T.pals[0];
            Dictionary<string, double[][]> rules = null;
            if (!string.IsNullOrEmpty(proc)) T.rules.TryGetValue(proc, out rules);
            double rot = double.NaN;
            if (rules != null && T.rulesRot.TryGetValue(proc, out var rr)) rot = rr;
            var G = vilGround;
            void Put(int wx, int wy, int wz, int[] pi, int state)
            {
                if (wx < x0 || wx > x0 + 15 || wz < z0 || wz > z0 + 15) return;
                if (terrain) wy = G[(wz - z0) * 16 + wx - x0] + (wy - v.y);
                if (wy <= WORLD_MIN_Y || wy >= WORLD_MAX_Y) return;
                // BlockRotProcessor: each block kept with the list's integrity
                if (!double.IsNaN(rot) && Hash.Hash3(unchecked(seed + 4417), wx, wy, wz) >= rot) return;
                int i = CI(wx - x0, wy, wz - z0), cur = ids[i];
                var ent = pi;
                double[][] R = null;
                if (rules != null && state >= 0) rules.TryGetValue(state.ToString(), out R);
                if (R != null)
                {
                    var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 4409 + wy * 0x3C6EF372), wx, wz));
                    foreach (var rl in R)
                    {
                        double prob = rl[0]; int loc = (int)rl[1], outi = (int)rl[2];
                        if (prob < 1 && rnd.Next() >= prob) continue;
                        if (loc == 1 && !IsWaterId(cur)) continue;
                        if (loc == 2 && cur != B.ICE) continue;
                        ent = outi != 0 ? T.palIds[outi - 1] : new[] { 0, 0 }; break;
                    }
                }
                int id = ent[0];
                if (id < 0) return;
                if (id != 0 && id < NB && WET[id] != 0 && (cur == B.WATER && meta[i] == 0 || IsWaterId(cur))) id = WET[id];
                ids[i] = (ushort)id; meta[i] = (byte)(id != 0 ? RotMeta(id, ent[1], v.rot) : 0);
            }
            int sx = T.sx, sy = T.sy, sz = T.sz; var g = T.g;
            for (int y = 0; y < sy; y++) for (int z = 0; z < sz; z++) for (int x = 0; x < sx; x++)
            {
                int st = g[(y * sz + z) * sx + x];
                if (st == 0) continue;
                var ent = pal[st - 1];
                if (ent[0] < 0) continue;
                var o = TplPos(x, z, v.rot, 0, 0);
                Put(v.x + o.Item1, v.y + y, v.z + o.Item2, ent, st - 1);
            }
            foreach (var j in T.jig)
            {
                if (j[8] == 0) continue;
                var o = TplPos(j[0], j[2], v.rot, 0, 0);
                Put(v.x + o.Item1, v.y + j[1], v.z + o.Item2, T.palIds[j[8] - 1], -1);
            }
            foreach (var (lx, ly, lz, table) in T.loot)
            {
                var o = TplPos(lx, lz, v.rot, 0, 0); int wx = v.x + o.Item1, wz = v.z + o.Item2;
                if (wx < x0 || wx > x0 + 15 || wz < z0 || wz > z0 + 15) continue;
                int sl = table.LastIndexOf('/');
                int wy = terrain ? vilGround[(wz - z0) * 16 + wx - x0] + ly : v.y + ly, t = Loot.Get((sl >= 0 ? table.Substring(sl + 1) : table).ToUpperInvariant());
                if (t != 0 && wy > WORLD_MIN_Y && wy < WORLD_MAX_Y) LootChest(CI(wx - x0, wy, wz - z0), t);
            }
        }

        // the features village decor pools place: trees, flower / grass / berry / cactus patches and
        // block piles (BlockPileFeature), each from its own position's random
        void VillageFeature(string name, int x, int y, int z)
        {
            string tree = name == "oak" ? "oak" : name == "acacia" ? "acacia" : name == "spruce" ? "spruce" : name == "pine" ? "pine" : null;
            if (tree != null)
            {
                // placed with the sapling's would_survive filter: soil under it
                int gg = FloorH(x, z) - 1;
                if (gg == y - 1) PlaceTree(BuildTree(tree, x, y - 1, z));
                return;
            }
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 4421 + y * 0x3C6EF372), x, z));
            int Ri(int n) => (int)(rnd.Next() * n);
            int x0 = this.x0, z0 = this.z0;
            int At(int X, int Y, int Z) => X >= x0 && X < x0 + 16 && Z >= z0 && Z < z0 + 16 && Y > WORLD_MIN_Y && Y < WORLD_MAX_Y ? CI(X - x0, Y, Z - z0) : -1;
            if (name.StartsWith("pile_"))
            {
                Func<(int, int)> pick = null;
                switch (name)
                {
                    case "pile_hay": pick = () => (B.HAY_BLOCK, Ri(3)); break;
                    case "pile_melon": pick = () => (B.MELON, 0); break;
                    case "pile_snow": pick = () => (B.SNOW_LAYER, 0); break;
                    case "pile_ice": pick = () => (rnd.Next() < 1.0 / 6 ? B.BLUE_ICE : B.PACKED_ICE, 0); break;
                    case "pile_pumpkin": pick = () => { int id = rnd.Next() < 0.95 ? B.PUMPKIN : B.JACK_O_LANTERN; return (id, Ri(4)); }; break;
                }
                if (pick == null || y < WORLD_MIN_Y + 5) return;
                int i0 = 2 + Ri(2), j0 = 2 + Ri(2);
                for (int yy = y; yy <= y + 1; yy++) for (int zz = z - j0; zz <= z + j0; zz++) for (int xx = x - i0; xx <= x + i0; xx++)
                {
                    int dx = x - xx, dz = z - zz;
                    double ra = rnd.Next() * 10; double rb = rnd.Next() * 6;
                    bool ok = dx * dx + dz * dz <= ra - rb || rnd.Next() < 0.031;
                    if (!ok) continue;
                    int i = At(xx, yy, zz), below = At(xx, yy - 1, zz);
                    if (i < 0 || below < 0) { pick(); continue; }
                    int b = ids[below];
                    if (b == B.DIRT_PATH ? rnd.Next() < 0.5 : OPAQUE[b] != 0)
                    {
                        var (id, m) = pick();
                        if (ids[i] == 0 || (FLAGS[ids[i]] & BF_REPLACE) != 0) { ids[i] = (ushort)id; meta[i] = (byte)m; }
                    }
                }
                return;
            }
            // RandomPatchFeature: tries at trapezoid offsets onto suitable ground
            int[] P = name == "flower_plain" ? new[] { 64, 6, 2 } : name == "patch_taiga_grass" ? new[] { 32, 7, 3 } : name == "patch_berry_bush" ? new[] { 96, 7, 3 } : name == "patch_cactus" ? new[] { 10, 7, 3 } : null;
            if (P == null) return;
            int Tz(int n) { int a = Ri(n + 1); return a - Ri(n + 1); }
            int[] FL = { B.DANDELION, B.POPPY, B.AZURE_BLUET, B.OXEYE_DAISY, B.RED_TULIP, B.ORANGE_TULIP, B.WHITE_TULIP, B.PINK_TULIP };
            for (int t = 0; t < P[0]; t++)
            {
                int X = x + Tz(P[1]), Y = y + Tz(P[2]), Z = z + Tz(P[1]);
                int i = At(X, Y, Z);
                if (i < 0 || ids[i] != 0) continue;
                int below = ids[i - 256];
                if (name == "patch_cactus")
                {
                    if (below != B.SAND && below != B.RED_SAND) continue;
                    int h = 1 + Ri(Ri(3) + 1);
                    for (int k = 0; k < h; k++) { int q = At(X, Y + k, Z); if (q < 0 || ids[q] != 0) break; ids[q] = B.CACTUS; meta[q] = 0; }
                    continue;
                }
                if (below != B.GRASS && below != B.DIRT && below != B.PODZOL && below != B.COARSE_DIRT) continue;
                if (name == "patch_berry_bush") { if (below == B.GRASS) { ids[i] = B.SWEET_BERRY_BUSH; meta[i] = 3; } continue; }
                if (name == "patch_taiga_grass") { ids[i] = (ushort)(Ri(5) == 0 ? B.TALL_GRASS : B.FERN); meta[i] = 0; continue; }
                if (below == B.GRASS) { ids[i] = (ushort)FL[Ri(8)]; meta[i] = 0; }
            }
        }

        // a tree's trunk needs free space (TreeFeature): none grows where a village piece stands
        bool InVillage(int x, int y, int z)
        {
            foreach (var b in vilBoxes) if (x >= b[0] && x <= b[3] && z >= b[2] && z <= b[5] && y <= b[4] + 1 && y >= b[1] - 8) return true;
            return false;
        }

        // Beardifier: terrain pushed toward the village: filled below and cleared above each rigid piece's
        // ground level and around junctions, with Minecraft's kernel (radius 12)
        Beard VillageBeard()
        {
            var near = new List<Piece>(); var junc = new List<int[]>(); var all = vilBoxes = new List<int[]>();
            foreach (var set in JIGSAW_SETS)
            {
                int r0x = FloorDiv(cx - 7, set.spacing), r1x = FloorDiv(cx + 7, set.spacing), r0z = FloorDiv(cz - 7, set.spacing), r1z = FloorDiv(cz + 7, set.spacing);
                for (int rz = r0z; rz <= r1z; rz++) for (int rx = r0x; rx <= r1x; rx++)
                {
                    var (sx, sz) = SpreadStart(set, rx, rz);
                    if (Math.Abs(sx - cx) > 7 || Math.Abs(sz - cz) > 7) continue;
                    var st = StructStart(set, sx, sz);
                    if (st == null) continue;
                    foreach (var p in st)
                    {
                        var b = p.box;
                        if (b[0] > x0 + 27 || b[3] < x0 - 12 || b[2] > z0 + 27 || b[5] < z0 - 12) continue;
                        all.Add(b);
                        if (p.vil.e.Proj == 0) near.Add(p);
                        foreach (var j in p.vil.junctions) if (j[0] >= x0 - 12 && j[0] <= x0 + 27 && j[2] >= z0 - 12 && j[2] <= z0 + 27) junc.Add(j);
                    }
                }
            }
            beardPieces = near;
            if (near.Count == 0 && junc.Count == 0) return null;
            int y0 = 1000000000, y1 = -1000000000;
            foreach (var p in near) { y0 = Math.Min(y0, p.box[1] - 12); y1 = Math.Max(y1, p.box[4] + 12); }
            foreach (var j in junc) { y0 = Math.Min(y0, j[1] - 12); y1 = Math.Max(y1, j[1] + 12); }
            y0 = Math.Max(y0, WORLD_MIN_Y + 1); y1 = Math.Min(y1, WORLD_MAX_Y - 1);
            int H = y1 - y0 + 1; var A = new float[256 * Math.Max(0, H)];
            foreach (var p in near)
            {
                var b = p.box; int gy = b[1] + p.vil.gd;
                for (int z = 0; z < 16; z++)
                {
                    int dz = Math.Max(0, Math.Max(b[2] - (z0 + z), z0 + z - b[5]));
                    if (dz >= 12) continue;
                    for (int x = 0; x < 16; x++)
                    {
                        int dx = Math.Max(0, Math.Max(b[0] - (x0 + x), x0 + x - b[3]));
                        if (dx >= 12) continue;
                        for (int dy = -12; dy < 12; dy++)
                        {
                            int y = gy + dy;
                            if (y < y0 || y > y1) continue;
                            int ai = (y - y0) * 256 + z * 16 + x;
                            A[ai] = (float)(A[ai] + BEARD_KERNEL[(dz + 12) * 576 + (dx + 12) * 24 + dy + 12] * 0.8);
                        }
                    }
                }
            }
            foreach (var j in junc)
            {
                for (int z = 0; z < 16; z++)
                {
                    int dz = z0 + z - j[2];
                    if (dz < -12 || dz >= 12) continue;
                    for (int x = 0; x < 16; x++)
                    {
                        int dx = x0 + x - j[0];
                        if (dx < -12 || dx >= 12) continue;
                        for (int dy = -12; dy < 12; dy++)
                        {
                            int y = j[1] + dy;
                            if (y < y0 || y > y1) continue;
                            int ai = (y - y0) * 256 + z * 16 + x;
                            A[ai] = (float)(A[ai] + BEARD_KERNEL[(dz + 12) * 576 + (dx + 12) * 24 + dy + 12] * 0.4);
                        }
                    }
                }
            }
            return new Beard { y0 = y0, y1 = y1, A = A };
        }
    }
}
