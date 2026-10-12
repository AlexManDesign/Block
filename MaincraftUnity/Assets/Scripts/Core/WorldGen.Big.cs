// Port of bigstructures.js: own structures built in code: jungle temple, ruined portals, woodland
// mansion, ocean monument, stronghold, sand temple, bog shack, old mines and dungeons. Each is designed
// here (layout, materials, rooms); only the placement scheme (one start per region of a structure set,
// pieces clipped to the chunk being generated) is shared with the other structures.
using System;
using System.Collections.Generic;
using static Maincraft.WG;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed class ShCell
    {
        public int i, j, d; public string kind;
        public List<(int, int)> links = new List<(int, int)>();
    }

    public sealed class MineSeg
    {
        public string kind;
        public int[] box, core;
        public int x, y, z, d, len;
        public bool rails, spider, chest;
    }

    public sealed partial class WorldGen
    {
        // a piece built by code: origin (x, y, z), rotation about the origin (TplPos), size; local
        // facings are world facings at rotation 0 (0 S +z, 1 W -x, 2 N -z, 3 E +x)
        static Piece OwnPiece(Action<WorldGen, Piece> build, int x, int y, int z, int rot, int sx, int sy, int sz, Action<Piece> extra = null)
        {
            var a = TplPos(0, 0, rot, 0, 0); var b = TplPos(sx - 1, sz - 1, rot, 0, 0);
            var p = new Piece { build = build, x = x, y = y, z = z, rot = rot, sx = sx, sy = sy, sz = sz };
            extra?.Invoke(p);
            int m = p.margin;
            p.box = new[] { x + Math.Min(a.Item1, b.Item1) - m, y, z + Math.Min(a.Item2, b.Item2) - m, x + Math.Max(a.Item1, b.Item1) + m, y + sy - 1, z + Math.Max(a.Item2, b.Item2) + m };
            return p;
        }

        /// <summary>Block writer for a piece in the chunk being generated (cells outside the chunk are skipped).</summary>
        sealed class OwnBuilder
        {
            readonly WorldGen g; readonly Piece p; readonly int rot;
            public OwnBuilder(WorldGen g, Piece p) { this.g = g; this.p = p; rot = p.rot; }

            public int At(int x, int y, int z)
            {
                var o = TplPos(x, z, rot, 0, 0); int X = p.x + o.Item1, Z = p.z + o.Item2, Y = p.y + y;
                return X >= g.x0 && X < g.x0 + 16 && Z >= g.z0 && Z < g.z0 + 16 && Y > WORLD_MIN_Y && Y < WORLD_MAX_Y ? CI(X - g.x0, Y, Z - g.z0) : -1;
            }
            public (int, int, int) World(int x, int y, int z) { var o = TplPos(x, z, rot, 0, 0); return (p.x + o.Item1, p.y + y, p.z + o.Item2); }
            // a block that can hold water (ladder, stairs, fence...) set into water is waterlogged
            public void Set(int x, int y, int z, int id, int m = 0)
            {
                int i = At(x, y, z);
                if (i < 0) return;
                if (id < NB && WET[id] != 0 && IsWaterId(g.ids[i])) id = WET[id];
                g.ids[i] = (ushort)id; g.meta[i] = (byte)(id != 0 ? RotMeta(DryId(id), m, rot) : 0);
            }
            public int Get(int x, int y, int z) { int i = At(x, y, z); return i >= 0 ? g.ids[i] : -1; }
            // a random 0..1 for a cell, the same in every chunk
            public double R(int x, int y, int z, int k = 0) { var w = World(x, y, z); return Hash.Hash3(unchecked(g.seed + 7001 + k * 13), w.Item1, w.Item2, w.Item3); }
            public void Fill(int ax, int ay, int az, int bx, int by, int bz, int id, int m = 0) { for (int y = ay; y <= by; y++) for (int z = az; z <= bz; z++) for (int x = ax; x <= bx; x++) Set(x, y, z, id, m); }
            // id from pick(x, y, z) for every cell of the box
            public void FillBy(int ax, int ay, int az, int bx, int by, int bz, Func<int, int, int, int> pick) { for (int y = ay; y <= by; y++) for (int z = az; z <= bz; z++) for (int x = ax; x <= bx; x++) Set(x, y, z, pick(x, y, z)); }
            // the four side walls of a box
            public void Walls(int ax, int ay, int az, int bx, int by, int bz, Func<int, int, int, int> pick)
            {
                for (int y = ay; y <= by; y++) for (int z = az; z <= bz; z++) for (int x = ax; x <= bx; x++)
                    if (x == ax || x == bx || z == az || z == bz) Set(x, y, z, pick(x, y, z));
            }
            public void Walls(int ax, int ay, int az, int bx, int by, int bz, int id) => Walls(ax, ay, az, bx, by, bz, (x, y, z) => id);
            // a foundation column: the cell at y always, then down through air, liquids and plants to
            // solid ground (at most 24 more, so a column over a big cave stops instead of reaching its floor)
            public void Down(int x, int y, int z, int id)
            {
                for (int yy = y; yy > y - 25 && p.y + yy > WORLD_MIN_Y + 1; yy--)
                {
                    int i = At(x, yy, z); if (i < 0) return;
                    int c = g.ids[i];
                    if (yy < y && c != 0 && !IsLiquidId(c) && (FLAGS[c] & BF_REPLACE) == 0 && (FLAGS[c] & BF_LEAVES) == 0) return;
                    g.ids[i] = (ushort)id; g.meta[i] = 0;
                }
            }
            public void Chest(int x, int y, int z, int facing, int table)
            {
                int i = At(x, y, z);
                if (i >= 0) { g.ids[i] = (ushort)(IsWaterId(g.ids[i]) ? WET[B.CHEST] : B.CHEST); g.meta[i] = (byte)(RotMeta(B.CHEST, facing, rot) | (table << 2)); }
            }
            // a vine on the side d (local facing toward the wall it hangs on)
            public void Vine(int x, int y, int z, int d) { int i = At(x, y, z); if (i >= 0 && g.ids[i] == 0) { g.ids[i] = B.VINE; g.meta[i] = (byte)(1 << ((d + rot) & 3)); } }
            public void Spawner(int x, int y, int z, string mob)
            {
                int i = At(x, y, z);
                if (i >= 0) { g.ids[i] = B.SPAWNER; g.meta[i] = (byte)SPAWNER_MOB[mob]; var w = World(x, y, z); g.spawnerList.Add(w.Item1); g.spawnerList.Add(w.Item2); g.spawnerList.Add(w.Item3); }
            }
        }

        // lowest / average surface over a footprint (world box corners)
        (int lo, int hi, int avg) FootprintH(int[] box, string mode = null)
        {
            int lo = 1000000000, hi = -1000000000, n = 0; double sum = 0;
            for (int z = box[2]; z <= box[5]; z += 2) for (int x = box[0]; x <= box[3]; x += 2)
            {
                int h = mode == "floor" ? FloorH(x, z) : SurfH(x, z);
                lo = Math.Min(lo, h); hi = Math.Max(hi, h); sum += h; n++;
            }
            return (lo, hi, (int)JsMath.Round(sum / n));
        }

        // ===================================================================== jungle temple
        // A stepped stone shrine: a pillared hall (mossy cobble outside, stone bricks inside), a terrace
        // with an upper hall, a little open shrine on top, and a crypt under the hall reached by a ladder.
        const int JT_SX = 15, JT_SY = 19, JT_SZ = 17;
        public List<Piece> JungleTempleStart(int cx, int cz)
        {
            int bio = BiomeAt(cx * 16 + 8, cz * 16 + 8);
            if (bio != BI.JUNGLE && bio != BI.BAMBOO_JUNGLE && bio != BI.SPARSE_JUNGLE) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 61210487 * 3), cx, cz)); int rot = (int)(rnd.Next() * 4);
            var p = OwnPiece((g, pp) => g.JungleTemple(pp), cx * 16, 0, cz * 16, rot, JT_SX, JT_SY, JT_SZ, q => { q.noTrees = true; q.margin = 1; });
            var f = FootprintH(p.box);
            if (f.hi - f.lo > 8 || f.lo <= SEA) return null;
            int y = f.avg - 1 - 6;            // the crypt (6 below the hall floor) sits in the ground
            p.y = y; p.box[1] = y; p.box[4] = y + JT_SY - 1;
            return new List<Piece> { p };
        }

        void JungleTemple(Piece p)
        {
            var W = new OwnBuilder(this, p); const int F = 6;     // F: hall floor level inside the piece
            int Rough(int x, int y, int z) => W.R(x, y, z) < 0.35 ? B.MOSSY : B.COBBLE;
            int Brick(int x, int y, int z) { double r = W.R(x, y, z, 1); return r < 0.2 ? B.MOSSY_STONE_BRICKS : r < 0.3 ? B.CRACKED_STONE_BRICKS : B.STONE_BRICKS; }
            // clear the hill above, then the foundation down to the ground
            W.Fill(0, F + 1, 0, 14, 18, 16, 0);
            for (int z = 0; z < 17; z++) for (int x = 0; x < 15; x++) W.Down(x, F, z, B.COBBLE);
            // crypt: 11 x 5 x 8 stone-brick vault under the back half
            W.FillBy(2, 0, 8, 12, F - 1, 15, Brick);
            W.Fill(3, 1, 9, 11, F - 2, 14, 0);
            W.FillBy(3, 0, 9, 11, 0, 14, (x, y, z) => W.R(x, y, z, 2) < 0.5 ? B.MOSSY_STONE_BRICKS : B.STONE_BRICKS);
            W.Chest(4, 1, 13, 3, Loot.Get("JUNGLE_TEMPLE"));
            W.Set(10, 1, 9, B.COBWEB); W.Set(3, F - 2, 9, B.COBWEB); W.Set(11, F - 2, 14, B.COBWEB); W.Set(3, 1, 14, B.COBWEB);
            W.Set(7, 1, 14, B.CHISELED_STONE_BRICKS); W.Set(7, 2, 14, B.LANTERN, 0);
            for (int y = 1; y <= F; y++) W.Set(11, y, 14, B.LADDER, 2);     // up into the hall (the crypt wall on its south)
            // ground hall 15 x 17, walls 5 high
            W.FillBy(0, F, 0, 14, F, 16, Brick);
            W.Walls(0, F + 1, 0, 14, F + 4, 16, Rough);
            W.FillBy(0, F + 5, 0, 14, F + 5, 16, Rough);
            foreach (int x in new[] { 4, 10 }) foreach (int z in new[] { 4, 8, 12 }) W.Fill(x, F + 1, z, x, F + 4, z, B.CHISELED_STONE_BRICKS);
            W.Fill(6, F + 1, 0, 8, F + 3, 0, 0);                              // entrance
            W.Set(5, F + 3, 0, B.COBBLE_STAIRS, 3 | 4); W.Set(9, F + 3, 0, B.COBBLE_STAIRS, 1 | 4);
            W.Set(7, F + 4, 8, B.LANTERN, 1); W.Set(7, F + 4, 3, B.LANTERN, 1);
            // the altar at the back with a chest
            W.Fill(5, F + 1, 15, 9, F + 1, 15, B.CHISELED_STONE_BRICKS);
            W.Chest(7, F + 2, 15, 2, Loot.Get("JUNGLE_TEMPLE"));
            W.Set(5, F + 2, 15, B.TORCH, 1 + 0); W.Set(9, F + 2, 15, B.TORCH, 1 + 0);
            // windows
            foreach (int z in new[] { 5, 11 }) { W.Set(0, F + 2, z, 0); W.Set(14, F + 2, z, 0); }
            // stairs inside along the east wall up through the roof onto the terrace
            for (int k = 0; k < 5; k++) { W.Set(13, F + 1 + k, 2 + k, B.COBBLE_STAIRS, 0); W.Set(13, F + 2 + k, 2 + k, 0); W.Set(13, F + 3 + k, 2 + k, 0); }
            W.Set(13, F + 5, 6, B.COBBLE_STAIRS, 0);
            // upper hall on the terrace: 11 x 11, door at the front
            W.Walls(2, F + 6, 3, 12, F + 9, 13, Rough);
            W.Fill(3, F + 6, 4, 11, F + 9, 12, 0);
            W.FillBy(2, F + 10, 3, 12, F + 10, 13, Rough);
            W.Fill(6, F + 6, 3, 8, F + 8, 3, 0);
            W.Set(7, F + 9, 8, B.LANTERN, 1);
            foreach (var (x, z) in new[] { (3, 4), (11, 4), (3, 12), (11, 12) }) W.Fill(x, F + 6, z, x, F + 9, z, B.CHISELED_STONE_BRICKS);
            // the shrine on top: four pillars and a cap
            foreach (var (x, z) in new[] { (5, 6), (9, 6), (5, 10), (9, 10) }) W.Fill(x, F + 11, z, x, F + 12, z, B.CHISELED_STONE_BRICKS);
            W.FillBy(5, F + 13, 6, 9, F + 13, 10, Rough);
            W.Fill(6, F + 13, 7, 8, F + 13, 9, B.MOSSY_STONE_BRICKS);
            W.Set(7, F + 11, 8, B.CHISELED_STONE_BRICKS);
            // vines on the outer walls
            for (int y = F + 1; y <= F + 9; y++) for (int k = 0; k < 17; k++)
            {
                if (W.R(0, y, k, 3) < 0.3) W.Vine(-1, y, k, 3);
                if (W.R(14, y, k, 4) < 0.3) W.Vine(15, y, k, 1);
                if (k < 15 && W.R(k, y, 16, 5) < 0.3) W.Vine(k, y, 17, 2);
            }
        }

        // ===================================================================== ruined portal
        // A fallen gate of obsidian on a cracked stone floor, the ground around scorched to netherrack with
        // a few magma blocks; some frame blocks lie broken on the ground. Standing or toppled.
        public List<Piece> RuinedPortalStart(int cx, int cz)
        {
            int bio = BiomeAt(cx * 16 + 8, cz * 16 + 8); var bp = BPROP[bio];
            if (bp != null && bp.ocean) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 33590147 * 3), cx, cz)); int rot = (int)(rnd.Next() * 4);
            bool fallen = rnd.Next() < 0.35; bool big = rnd.Next() < 0.3; int salt = (int)(rnd.Next() * 1e6);
            var p = OwnPiece((g, pp) => g.RuinedPortal(pp), cx * 16 + 2, 0, cz * 16 + 2, rot, 11, 9, 11, q => { q.noTrees = true; q.fallen = fallen; q.big = big; q.salt = salt; });
            var f = FootprintH(p.box); var fl = FootprintH(p.box, "floor");
            if (f.hi - f.lo > 6 || fl.lo < SEA) return null;
            p.y = f.avg - 2; p.box[1] = p.y; p.box[4] = p.y + 8;
            return new List<Piece> { p };
        }

        void RuinedPortal(Piece p)
        {
            var W = new OwnBuilder(this, p); const int G = 1;          // G: floor level
            double R(int x, int y, int z, int k) => W.R(x, y, z, 20 + k + p.salt % 97);
            // round scorched patch, the floor in the middle
            for (int z = 0; z < 11; z++) for (int x = 0; x < 11; x++)
            {
                double d = Math.Sqrt((x - 5) * (x - 5) + (z - 5) * (z - 5));
                if (d > 5.2 + R(x, 0, z, 0) * 0.8) continue;
                // find the ground in this column (within the piece)
                int y = 8; while (y > 0 && W.Get(x, y, z) == 0) y--;
                int cur = W.Get(x, y, z);
                if (d < 3.6)
                {
                    W.Set(x, G, z, R(x, G, z, 1) < 0.25 ? B.CRACKED_STONE_BRICKS : R(x, G, z, 1) < 0.4 ? B.MOSSY_STONE_BRICKS : B.STONE_BRICKS);
                    W.Down(x, G - 1, z, B.STONE_BRICKS);
                    for (int yy = G + 1; yy < 9; yy++) W.Set(x, yy, z, 0);
                }
                else if (cur >= 0 && OPAQUE[cur] != 0 && Opq(W.Get(x, y - 1, z)) && R(x, y, z, 2) < 0.75) W.Set(x, y, z, R(x, y, z, 3) < 0.12 ? B.MAGMA_BLOCK : B.NETHERRACK);   // only real ground
            }
            void Frame(int x, int y, int z)
            {
                double k = R(x, y, z, 4);
                if (k < 0.2) return;                                              // broken off
                W.Set(x, y, z, k < 0.45 ? B.CRYING_OBSIDIAN : B.OBSIDIAN);
            }
            int w = p.big ? 5 : 4, h = p.big ? 7 : 5, fx0 = 5 - (w >> 1);
            if (!p.fallen)
            {
                for (int x = fx0; x < fx0 + w; x++) { Frame(x, G + 1, 5); Frame(x, G + h, 5); }
                for (int y = G + 2; y < G + h; y++) { Frame(fx0, y, 5); Frame(fx0 + w - 1, y, 5); }
            }
            else
            {
                for (int x = fx0; x < fx0 + w; x++) { Frame(x, G + 1, 2); Frame(x, G + 1, 1 + h); }
                for (int z = 3; z < 1 + h; z++) { Frame(fx0, G + 1, z); Frame(fx0 + w - 1, G + 1, z); }
            }
            // broken pieces, gold, a chest
            for (int k = 0; k < 4; k++) { int x = 1 + (int)(R(k, 0, 0, 5) * 9), z = 1 + (int)(R(0, 0, k, 6) * 9); if (W.Get(x, G + 1, z) == 0 && W.Get(x, G, z) > 0) W.Set(x, G + 1, z, B.OBSIDIAN); }
            if (R(1, 1, 1, 7) < 0.6) W.Set(8, G + 1, 5, B.GOLD_BLOCK);
            W.Chest(2, G + 1, 5, 3, Loot.Get("RUINED_PORTAL"));
            W.Set(3, G + 1, 3, B.NETHERRACK); W.Set(3, G + 2, 3, B.FIRE);
        }

        // OPAQUE[-1] is undefined (falsy) in the original
        static bool Opq(int id) => id >= 0 && OPAQUE[id] != 0;

        // ===================================================================== woodland mansion
        // A dark-wood manor: cobblestone plinth, dark oak frame with birch walls and glass windows, two
        // floors of four rooms around a middle hall with a staircase, a steep dark roof with dormer ridge.
        const int MAN_SX = 35, MAN_SY = 30, MAN_SZ = 27, MAN_FLOOR = 5;
        public List<Piece> MansionStart(int cx, int cz)
        {
            int bio = BiomeAt(cx * 16 + 8, cz * 16 + 8);
            if (bio != BI.DARK_FOREST && bio != BI.PALE_GARDEN) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 10387319 * 3), cx, cz)); int rot = (int)(rnd.Next() * 4);
            int salt = (int)(rnd.Next() * 1e6);
            var p = OwnPiece((g, pp) => g.Mansion(pp), cx * 16, 0, cz * 16, rot, MAN_SX, MAN_SY, MAN_SZ, q => { q.noTrees = true; q.margin = 1; q.salt = salt; });
            var f = FootprintH(p.box);
            if (f.lo <= SEA) return null;
            p.y = f.avg - MAN_FLOOR; p.box[1] = p.y; p.box[4] = p.y + MAN_SY - 1;
            return new List<Piece> { p };
        }

        void Mansion(Piece p)
        {
            var W = new OwnBuilder(this, p); const int F = MAN_FLOOR, SX = MAN_SX, SZ = MAN_SZ;
            double Rr(int x, int y, int z, int k) => W.R(x, y, z, 40 + k);
            // ground cleared over the footprint (and a 1 block margin), plinth down to the ground
            W.Fill(0, F + 1, 0, SX - 1, MAN_SY - 1, SZ - 1, 0);
            for (int z = 0; z < SZ; z++) for (int x = 0; x < SX; x++) W.Down(x, F, z, B.COBBLE);
            W.FillBy(0, F, 0, SX - 1, F, SZ - 1, (x, y, z) => (x == 0 || z == 0 || x == SX - 1 || z == SZ - 1) ? (Rr(x, y, z, 0) < 0.3 ? B.MOSSY : B.COBBLE) : B.DARK_PLANKS);
            bool WallAt(int x, int z) => x == 0 || z == 0 || x == SX - 1 || z == SZ - 1;
            bool Post(int x, int z) => (x % 6 == 0 || x == SX - 1) && (z % 6 == 0 || z == SZ - 1) || ((x == 0 || x == SX - 1) && z % 6 == 0) || ((z == 0 || z == SZ - 1) && x % 6 == 0);
            // two storeys: walls 5 high, a dark band between them
            foreach (int bas in new[] { F + 1, F + 7 })
            {
                for (int y = bas; y < bas + 5; y++) for (int z = 0; z < SZ; z++) for (int x = 0; x < SX; x++)
                {
                    if (!WallAt(x, z)) continue;
                    if (Post(x, z)) { W.Set(x, y, z, B.DARK_LOG); continue; }
                    bool win = (y == bas + 1 || y == bas + 2) && ((x % 3 == 0 && (z == 0 || z == SZ - 1)) || (z % 3 == 0 && (x == 0 || x == SX - 1)));
                    W.Set(x, y, z, win ? B.GLASS_PANE : B.BIRCH_PLANKS);
                }
                W.Fill(1, bas + 5, 1, SX - 2, bas + 5, SZ - 2, B.DARK_PLANKS);                // ceiling / floor above
                W.Walls(0, bas + 5, 0, SX - 1, bas + 5, SZ - 1, B.STRIPPED_DARK_OAK_LOG);
                // inner walls: a hall along z in the middle (x 14..20), each side split in two rooms at z 13
                for (int y = bas; y < bas + 5; y++) for (int z = 1; z < SZ - 1; z++) { W.Set(14, y, z, B.DARK_PLANKS); W.Set(20, y, z, B.DARK_PLANKS); }
                for (int y = bas; y < bas + 5; y++) for (int x = 1; x < SX - 1; x++) if (x < 14 || x > 20) W.Set(x, y, 13, B.DARK_PLANKS);
                // doors from the hall into the four rooms
                foreach (int z in new[] { 6, 20 }) foreach (int x in new[] { 14, 20 }) { W.Set(x, bas, z, 0); W.Set(x, bas + 1, z, 0); }
                // hall: red carpet and torches
                for (int z = 1; z < SZ - 1; z++) for (int x = 15; x <= 19; x++) W.Set(x, bas, z, x == 17 || x == 16 || x == 18 ? B.RED_CARPET : 0);
                foreach (int z in new[] { 4, 10, 16, 22 }) { W.Set(15, bas + 2, z, B.TORCH, 1 + 1); W.Set(19, bas + 2, z, B.TORCH, 1 + 3); }
            }
            // front door (double) and porch
            W.Set(17, F + 1, 0, B.DARK_OAK_DOOR, 2); W.Set(17, F + 2, 0, B.DARK_OAK_DOOR, 2 | 4);
            W.Set(16, F + 1, 0, B.DARK_OAK_DOOR, 2 | 16); W.Set(16, F + 2, 0, B.DARK_OAK_DOOR, 2 | 4 | 16);
            W.Set(18, F + 1, 0, B.DARK_OAK_DOOR, 2); W.Set(18, F + 2, 0, B.DARK_OAK_DOOR, 2 | 4);
            for (int x = 15; x <= 19; x++) { W.Set(x, F, -1, B.COBBLE_STAIRS, 0); }
            // staircase at the back of the hall (z 15..20 rising toward +z), the opening above it
            for (int k = 0; k < 6; k++) { W.Set(16, F + 1 + k, 15 + k, B.DARK_PLANKS_STAIRS, 0); W.Set(17, F + 1 + k, 15 + k, B.DARK_PLANKS_STAIRS, 0); W.Set(16, F + 2 + k, 15 + k, 0); W.Set(17, F + 2 + k, 15 + k, 0); }
            for (int z = 15; z <= 20; z++) { W.Set(16, F + 6, z, 0); W.Set(17, F + 6, z, 0); W.Set(16, F + 7, z, z > 19 ? B.RED_CARPET : 0); W.Set(17, F + 7, z, z > 19 ? B.RED_CARPET : 0); }
            for (int z = 15; z <= 20; z++) { W.Set(15, F + 7, z, B.DARK_PLANKS_FENCE); }
            // the rooms
            string[] kinds = { "library", "bedroom", "dining", "storage", "bedroom", "library", "storage", "web" };
            int n = 0;
            foreach (int bas in new[] { F + 1, F + 7 })
                foreach (var (ax, bx) in new[] { (1, 13), (21, 33) })
                    foreach (var (az, bz) in new[] { (1, 12), (14, 25) })
                    {
                        string kind = kinds[(n + (p.salt >> 3)) % kinds.Length]; n++;
                        MansionRoom(W, kind, ax, bas, az, bx, bz, ax == 1 ? 3 : 1, Rr);
                    }
            // roof: a steep gable along x, overhanging by one
            const int R = MAN_FLOOR + 12;
            for (int k = 0; k <= 13; k++)
            {
                int y = R + k;
                for (int x = -1; x <= SX; x++)
                {
                    W.Set(x, y, k - 1, B.DARK_PLANKS_STAIRS, 0); W.Set(x, y, SZ - k, B.DARK_PLANKS_STAIRS, 2);
                    if (k > 0) for (int z = k; z < SZ - k; z++) if (x == 0 || x == SX - 1) W.Set(x, y, z, B.DARK_PLANKS);
                }
                if (SZ - k - (k - 1) <= 2) { for (int x = -1; x <= SX; x++) for (int z = k - 1; z <= SZ - k; z++) W.Set(x, y + 1, z, B.DARK_PLANKS_SLAB); break; }
            }
            // gable windows and chimneys
            foreach (int x in new[] { 0, SX - 1 }) { W.Set(x, R + 3, 12, B.GLASS_PANE); W.Set(x, R + 3, 14, B.GLASS_PANE); }
            foreach (int x in new[] { 6, 28 }) { W.Fill(x, R + 4, 12, x + 1, R + 13, 13, B.COBBLE); W.Set(x, R + 14, 12, B.COBBLE_WALL); }
        }

        void MansionRoom(OwnBuilder W, string kind, int ax, int y, int az, int bx, int bz, int wallSide, Func<int, int, int, int, double> rr)
        {
            int cxm = (ax + bx) >> 1, czm = (az + bz) >> 1;
            int lootM = Loot.Get("WOODLAND_MANSION");
            W.Set(cxm, y + 4, czm, B.LANTERN, 1);
            if (kind == "library")
            {
                for (int x = ax; x <= bx; x++) foreach (int z in new[] { az, bz }) for (int yy = y; yy < y + 3; yy++) if (x != (wallSide == 3 ? bx : ax)) W.Set(x, yy, z, B.BOOKSHELF);
                for (int z = az + 3; z <= bz - 3; z += 2) for (int x = ax + 2; x <= bx - 2; x++) if (x != cxm) W.Set(x, y, z, B.BOOKSHELF);
                W.Chest(cxm, y, czm, 0, lootM);
            }
            else if (kind == "bedroom")
            {
                W.Fill(ax + 2, y, az + 2, bx - 2, y, bz - 2, B.BLUE_CARPET);
                W.Set(ax + 1, y, czm, B.BED, 1 | 4); W.Set(ax + 2, y, czm, B.BED, 1);
                W.Set(ax + 1, y, czm + 2, B.BED, 1 | 4); W.Set(ax + 2, y, czm + 2, B.BED, 1);
                W.Chest(bx, y, az, 2, lootM);
                W.Set(bx, y, bz, B.CRAFTING_TABLE);
            }
            else if (kind == "dining")
            {
                for (int x = ax + 3; x <= bx - 3; x++) { W.Set(x, y, czm, B.DARK_PLANKS_FENCE); W.Set(x, y + 1, czm, B.WHITE_CARPET); }
                for (int x = ax + 3; x <= bx - 3; x += 2) { W.Set(x, y, czm - 1, B.DARK_PLANKS_STAIRS, 0); W.Set(x, y, czm + 1, B.DARK_PLANKS_STAIRS, 2); }
                W.Set(ax, y, az, B.FURNACE, 0); W.Set(ax + 1, y, az, B.SMOKER, 0); W.Set(ax + 2, y, az, B.BARREL, 4);
            }
            else if (kind == "storage")
            {
                for (int x = ax; x <= bx; x += 2) { W.Set(x, y, az, B.BARREL, 4); W.Set(x, y, bz, B.BARREL, 4); if (rr(x, y, az, 1) < 0.5) W.Set(x, y + 1, az, B.BARREL, 4); }
                W.Chest(cxm, y, czm, 0, lootM); W.Chest(cxm + 1, y, czm, 0, lootM);
                W.Set(ax + 1, y, czm, B.HAY_BLOCK); W.Set(ax + 1, y + 1, czm, B.HAY_BLOCK);
            }
            else
            {
                for (int z = az; z <= bz; z++) for (int x = ax; x <= bx; x++) for (int yy = y; yy < y + 4; yy++) if (rr(x, yy, z, 2) < 0.12) W.Set(x, yy, z, B.COBWEB);
                W.Chest(cxm, y, czm, 0, lootM);
            }
        }

        // ===================================================================== ocean monument
        // A sunken citadel of prismarine: a broad plinth on the sea floor, four corner towers joined by
        // arcaded walls, a stepped keep with a treasure chamber at its heart, sea lanterns for light.
        const int MON_S = 41, MON_SY = 24;
        public List<Piece> MonumentStart(int cx, int cz)
        {
            int bio = BiomeAt(cx * 16 + 8, cz * 16 + 8);
            if (bio != BI.DEEP_OCEAN && bio != BI.DEEP_COLD_OCEAN && bio != BI.DEEP_LUKEWARM_OCEAN && bio != BI.DEEP_FROZEN_OCEAN) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 10387313 * 3), cx, cz)); int rot = (int)(rnd.Next() * 4);
            var p = OwnPiece((g, pp) => g.Monument(pp), cx * 16 - 12, 0, cz * 16 - 12, rot, MON_S, MON_SY, MON_S, q => q.noTrees = true);
            foreach (var (x, z) in new[] { (p.box[0], p.box[2]), (p.box[3], p.box[2]), (p.box[0], p.box[5]), (p.box[3], p.box[5]) })
            {
                int b = BiomeAt(x, z); if (BPROP[b] == null || !BPROP[b].ocean || b == BI.RIVER) return null;
            }
            var f = FootprintH(p.box, "floor");
            p.y = Math.Min(f.avg, SEA - MON_SY - 1); p.box[1] = p.y; p.box[4] = p.y + MON_SY - 1;
            return new List<Piece> { p };
        }

        void Monument(Piece p)
        {
            var W = new OwnBuilder(this, p); const int S = MON_S, M = (S - 1) >> 1;
            int Prism(int x, int y, int z) => W.R(x, y, z, 60) < 0.3 ? B.PRISMARINE : B.PRISMARINE_BRICKS;
            void WATER(int x, int y, int z) => W.Set(x, y, z, B.WATER, 0);
            // everything in the box is water first (cuts the sea floor), then the plinth with legs down
            for (int y = 1; y < MON_SY; y++) for (int z = 0; z < S; z++) for (int x = 0; x < S; x++) WATER(x, y, z);
            W.FillBy(0, 0, 0, S - 1, 0, S - 1, Prism);
            for (int z = 0; z < S; z += 4) for (int x = 0; x < S; x += 4) W.Down(x, -1, z, B.PRISMARINE_BRICKS);
            // four corner towers 7 x 7, 16 high, dark bands, lantern crowns
            foreach (var (tx, tz) in new[] { (0, 0), (S - 7, 0), (0, S - 7), (S - 7, S - 7) })
            {
                for (int y = 1; y <= 16; y++) { int yy = y; W.Walls(tx, y, tz, tx + 6, y, tz + 6, (x, y2, z) => yy % 5 == 0 ? B.DARK_PRISMARINE : Prism(x, y2, z)); }
                W.Fill(tx + 1, 17, tz + 1, tx + 5, 17, tz + 5, B.DARK_PRISMARINE);
                W.Set(tx + 3, 18, tz + 3, B.SEA_LANTERN);
                foreach (var (ox, oz) in new[] { (1, 1), (5, 1), (1, 5), (5, 5) }) W.Set(tx + ox, 18, tz + oz, B.PRISMARINE_WALL);
                W.Set(tx + 3, 8, tz, 0); W.Set(tx + 3, 8, tz + 6, 0); W.Set(tx, 8, tz + 3, 0); W.Set(tx + 6, 8, tz + 3, 0);
                foreach (int y in new[] { 8, 9 }) { WATER(tx + 3, y, tz); WATER(tx + 3, y, tz + 6); WATER(tx, y, tz + 3); WATER(tx + 6, y, tz + 3); }
                W.Set(tx + 3, 15, tz + 3, B.SEA_LANTERN);
            }
            // arcaded walls between the towers: piers every 4, arches over the gaps
            for (int k = 7; k < S - 7; k++) foreach (var (x, z) in new[] { (k, 2), (k, S - 3), (2, k), (S - 3, k) })
            {
                bool pier = (k - 7) % 4 == 0;
                for (int y = 1; y <= 9; y++)
                {
                    if (!pier && y < 6) continue;
                    W.Set(x, y, z, y == 9 ? B.DARK_PRISMARINE : Prism(x, y, z));
                }
                if (pier) W.Set(x, 10, z, B.PRISMARINE_WALL);
            }
            // the keep: 19 x 19, 11 high, stepped roof
            const int K0 = M - 9, K1 = M + 9;
            for (int y = 1; y <= 11; y++) { int yy = y; W.Walls(K0, y, K0, K1, y, K1, (x, y2, z) => yy == 6 ? B.DARK_PRISMARINE : Prism(x, y2, z)); }
            for (int s = 0; s < 5; s++)
            {
                int y = 12 + s, a = K0 + s * 2, b = K1 - s * 2;
                if (a > b) break;
                W.FillBy(a, y, a, b, y, b, Prism);
                if (b - a > 2) W.Fill(a + 1, y, a + 1, b - 1, y, b - 1, B.PRISMARINE_BRICKS);
                foreach (var (x, z) in new[] { (a, a), (b, a), (a, b), (b, b) }) W.Set(x, y + 1, z, B.SEA_LANTERN);
            }
            W.Set(M, 22, M, B.SEA_LANTERN);
            // gates on all four sides, the front one wide
            for (int y = 1; y <= 4; y++) for (int k = -1; k <= 1; k++) { WATER(M + k, y, K0); WATER(M + k, y, K1); WATER(K0, y, M + k); WATER(K1, y, M + k); }
            for (int y = 1; y <= 5; y++) for (int k = -2; k <= 2; k++) WATER(M + k, y, 2);
            // inner ring of pillars with lanterns
            foreach (var (x, z) in new[] { (K0 + 3, K0 + 3), (K1 - 3, K0 + 3), (K0 + 3, K1 - 3), (K1 - 3, K1 - 3) }) { W.Fill(x, 1, z, x, 10, z, B.DARK_PRISMARINE); W.Set(x, 6, z, B.SEA_LANTERN); }
            // treasure chamber: dark prismarine room with one door, a dais, gold and a chest
            const int T0 = M - 3, T1 = M + 3;
            for (int y = 1; y <= 5; y++) W.Walls(T0, y, T0, T1, y, T1, B.DARK_PRISMARINE);
            W.Fill(T0, 6, T0, T1, 6, T1, B.DARK_PRISMARINE);
            for (int y = 1; y <= 3; y++) WATER(M, y, T0);
            W.Fill(M - 1, 1, M - 1, M + 1, 1, M + 1, B.PRISMARINE_BRICKS);
            W.Set(M, 2, M, B.GOLD_BLOCK);
            W.Set(M - 1, 2, M - 1, B.SEA_LANTERN); W.Set(M + 1, 2, M + 1, B.SEA_LANTERN);
            W.Chest(M + 1, 2, M - 1, 2, Loot.Get("MONUMENT_TREASURE"));
            W.Set(M, 5, M, B.SEA_LANTERN);
            // a few wet sponges in the corners of the keep, like forgotten stores
            foreach (var (x, z) in new[] { (K0 + 1, K1 - 1), (K1 - 1, K0 + 1) }) if (W.R(x, 1, z, 61) < 0.7) W.Set(x, 1, z, B.WET_SPONGE);
        }

        // ===================================================================== stronghold
        // An underground warren on a grid of 15-block cells: from a fountain hub a random tree of rooms
        // (library, prison, storeroom, pillar crossing, well) joined by stone-brick corridors, and at the
        // far end the sealed gate chamber over a lava moat with a skeleton spawner.
        const int SH_CELL = 15, SH_N = 7;
        public List<Piece> StrongholdStart(int cx, int cz)
        {
            int bio = BiomeAt(cx * 16 + 8, cz * 16 + 8);
            if (BPROP[bio] != null && BPROP[bio].ocean) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 72411097 * 3), cx, cz));
            int ground = SurfH(cx * 16 + 8, cz * 16 + 8);
            int y = Math.Max(WORLD_MIN_Y + 12, Math.Min(ground - 30, 10 + (int)(rnd.Next() * 25)));
            // random spanning tree over a 7 x 7 grid from the centre, up to 16 cells
            const int C = SH_N >> 1;
            var cell = new Dictionary<int, ShCell>(); var order = new List<ShCell>();
            int Key(int i, int j) => i * 16 + j;
            var hub = new ShCell { i = C, j = C, kind = "hub", d = 0 };
            cell[Key(C, C)] = hub; order.Add(hub);
            var open = new List<ShCell> { hub };
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (open.Count > 0 && cell.Count < 16)
            {
                var a = open[(int)(rnd.Next() * open.Count)];
                var free = new List<(int, int)>();
                foreach (var (di, dj) in dirs) { int i = a.i + di, j = a.j + dj; if (i >= 0 && j >= 0 && i < SH_N && j < SH_N && !cell.ContainsKey(Key(i, j))) free.Add((di, dj)); }
                if (free.Count == 0) { open.Remove(a); continue; }
                var (fdi, fdj) = free[(int)(rnd.Next() * free.Count)];
                var bb = new ShCell { i = a.i + fdi, j = a.j + fdj, kind = "", d = a.d + 1 };
                a.links.Add((fdi, fdj)); bb.links.Add((-fdi, -fdj));
                cell[Key(bb.i, bb.j)] = bb; order.Add(bb); open.Add(bb);
            }
            var far = order[0]; foreach (var c in order) if (c.d > far.d) far = c;
            far.kind = "gate";
            string[] kinds = { "library", "prison", "store", "crossing", "well", "crossing", "store", "prison" };
            foreach (var c in order) if (c.kind.Length == 0) c.kind = kinds[(int)(rnd.Next() * kinds.Length)];
            int X = cx * 16 - C * SH_CELL, Z = cz * 16 - C * SH_CELL;
            var res = new List<Piece>(order.Count);
            foreach (var c in order)
            {
                int salt = (int)(rnd.Next() * 1e6); var cc = c;
                res.Add(OwnPiece((g, pp) => g.StrongholdCell(pp), X + c.i * SH_CELL, y, Z + c.j * SH_CELL, 0, SH_CELL, 11, SH_CELL, q => { q.cell = cc; q.salt = salt; q.underground = true; }));
            }
            return res;
        }

        void StrongholdCell(Piece p)
        {
            var W = new OwnBuilder(this, p); var c = p.cell; const int M = 7;
            int Brick(int x, int y, int z) { double r = W.R(x, y, z, 80); return r < 0.15 ? B.CRACKED_STONE_BRICKS : r < 0.3 ? B.MOSSY_STONE_BRICKS : B.STONE_BRICKS; }
            // corridors to the linked neighbours: 5 wide, 5 high, from the room edge to the cell edge
            foreach (var (di, dj) in c.links)
            {
                bool vert = di == 0; const int len = M;
                for (int t = 0; t <= len; t++) for (int k = -2; k <= 2; k++) for (int y = 0; y <= 4; y++)
                {
                    int along = di > 0 || dj > 0 ? M + t : M - t, x = vert ? M + k : along, z = vert ? along : M + k;
                    bool edge = k == -2 || k == 2 || y == 0 || y == 4;
                    W.Set(x, y, z, edge ? Brick(x, y, z) : 0);
                }
                // a torch now and then
                {
                    const int t = 7; int along = di > 0 || dj > 0 ? M + t : M - t;
                    if (vert) W.Set(M - 1, 2, along, B.TORCH, 1 + 1); else W.Set(along, 2, M - 1, B.TORCH, 1 + 2);
                }
            }
            void Room(int r, int h)
            {
                for (int y = 0; y <= h; y++) for (int z = M - r; z <= M + r; z++) for (int x = M - r; x <= M + r; x++)
                {
                    bool edge = x == M - r || x == M + r || z == M - r || z == M + r || y == 0 || y == h;
                    W.Set(x, y, z, edge ? Brick(x, y, z) : 0);
                }
                // doorways where corridors arrive
                foreach (var (di, dj) in c.links)
                {
                    int x = M + di * r, z = M + dj * r;
                    for (int k = -1; k <= 1; k++) for (int y = 1; y <= 3; y++) W.Set(di != 0 ? x : M + k, y, dj != 0 ? z : M + k, 0);
                }
            }
            if (c.kind == "hub")
            {
                Room(6, 7);
                W.Fill(M - 2, 1, M - 2, M + 2, 1, M + 2, B.STONE_BRICKS); W.Fill(M - 1, 1, M - 1, M + 1, 1, M + 1, B.WATER);
                W.Fill(M, 1, M, M, 3, M, B.CHISELED_STONE_BRICKS); W.Set(M, 4, M, B.LANTERN, 0);
                foreach (var (x, z) in new[] { (M - 5, M - 5), (M + 5, M - 5), (M - 5, M + 5), (M + 5, M + 5) }) { W.Fill(x, 1, z, x, 6, z, B.CHISELED_STONE_BRICKS); W.Set(x, 5, z + (z < M ? 1 : -1), B.TORCH, 1 + (z < M ? 2 : 0)); }
            }
            else if (c.kind == "library")
            {
                Room(6, 10);
                for (int y = 1; y <= 9; y++) for (int k = M - 5; k <= M + 5; k++) foreach (var (x, z) in new[] { (M - 5, k), (M + 5, k) }) if (y != 5 && !(Math.Abs(k - M) <= 1 && y <= 3)) W.Set(x, y, z, B.BOOKSHELF);
                for (int x = M - 4; x <= M + 4; x++) foreach (int z in new[] { M - 5, M + 5 }) for (int y = 1; y <= 4; y++) if (Math.Abs(x - M) > 1) W.Set(x, y, z, B.BOOKSHELF);
                W.Fill(M - 4, 5, M - 4, M + 4, 5, M + 4, B.DARK_PLANKS); W.Fill(M - 2, 5, M - 2, M + 2, 5, M + 2, 0);   // gallery
                for (int k = M - 3; k <= M + 3; k++) { W.Set(k, 6, M - 3, B.OAK_FENCE); W.Set(k, 6, M + 3, B.OAK_FENCE); W.Set(M - 3, 6, k, B.OAK_FENCE); W.Set(M + 3, 6, k, B.OAK_FENCE); }
                // the ladder up to the gallery, on bookshelves all the way (a ladder needs a block behind it)
                for (int y = 1; y <= 5; y++) { W.Set(M + 4, y, M + 4, B.LADDER, 2); W.Set(M + 4, y, M + 5, B.BOOKSHELF); }
                W.Set(M, 9, M, B.LANTERN, 1);
                W.Chest(M - 4, 1, M - 3, 3, Loot.Get("STRONGHOLD_LIBRARY")); W.Chest(M - 4, 6, M + 3, 3, Loot.Get("STRONGHOLD_LIBRARY"));
            }
            else if (c.kind == "prison")
            {
                Room(6, 6);
                foreach (int zs in new[] { -1, 1 }) for (int x = M - 5; x <= M + 5; x++) for (int y = 1; y <= 5; y++)
                {
                    int z = M + zs * 2;
                    if (Math.Abs(x - M) <= 1) continue;
                    W.Set(x, y, z, (x - M) % 3 == 0 ? B.STONE_BRICKS : B.IRON_BARS);
                }
                W.Set(M - 4, 1, M - 4, B.COBWEB); W.Set(M + 4, 1, M + 4, B.COBWEB);
                W.Set(M, 5, M, B.LANTERN, 1);
            }
            else if (c.kind == "store")
            {
                Room(5, 5);
                W.Chest(M - 4, 1, M, 3, Loot.Get("STRONGHOLD_STORE"));
                for (int z = M - 4; z <= M + 4; z += 2) { W.Set(M + 4, 1, z, B.BARREL, 4); }
                W.Set(M - 4, 1, M - 4, B.CRAFTING_TABLE); W.Set(M - 4, 1, M + 4, B.FURNACE, 3);
                W.Set(M, 4, M, B.LANTERN, 1);
            }
            else if (c.kind == "well")
            {
                Room(5, 6);
                W.Fill(M - 1, 0, M - 1, M + 1, 0, M + 1, B.WATER); W.Walls(M - 2, 1, M - 2, M + 2, 1, M + 2, B.STONEBRICK_SLAB);
                W.Fill(M - 1, 1, M - 1, M + 1, 1, M + 1, 0);
                foreach (var (x, z) in new[] { (M - 2, M - 2), (M + 2, M + 2) }) W.Fill(x, 2, z, x, 5, z, B.STONE_BRICKS_WALL);
                W.Set(M, 5, M, B.LANTERN, 1);
            }
            else if (c.kind == "crossing")
            {
                Room(5, 7);
                foreach (var (x, z) in new[] { (M - 2, M - 2), (M + 2, M - 2), (M - 2, M + 2), (M + 2, M + 2) }) { W.Fill(x, 1, z, x, 6, z, B.STONE_BRICKS); W.Set(x, 3, z + (z < M ? -1 : 1), B.TORCH, 1 + (z < M ? 0 : 2)); }
            }
            else if (c.kind == "gate")
            {
                Room(6, 8);
                // lava moat round a raised dais, the sealed gate of end stone bricks and obsidian, a stair up
                W.Fill(M - 4, 0, M - 4, M + 4, 0, M + 4, B.LAVA);
                W.Fill(M - 2, 0, M - 2, M + 2, 1, M + 2, B.STONE_BRICKS);
                for (int k = -2; k <= 2; k++) foreach (var (x, z) in new[] { (M + k, M - 2), (M + k, M + 2), (M - 2, M + k), (M + 2, M + k) }) W.Set(x, 2, z, (k + 2) % 2 != 0 ? B.END_STONE_BRICKS : B.OBSIDIAN);
                W.Fill(M - 1, 1, M - 1, M + 1, 1, M + 1, B.CHISELED_STONE_BRICKS);
                for (int k = M - 4; k < M - 2; k++) W.Set(M, 0, k, B.STONE_BRICKS);
                W.Set(M, 1, M - 3, B.STONEBRICK_STAIRS, 0);
                W.Spawner(M, 2, M, "skeleton");
                foreach (var (x, z) in new[] { (M - 5, M - 5), (M + 5, M + 5), (M - 5, M + 5), (M + 5, M - 5) }) W.Set(x, 6, z, B.LANTERN, 1);
            }
        }

        // ===================================================================== sand temple (desert)
        // A walled courtyard with sandstone obelisks at the corners; in it a square temple behind a portico
        // of two columns, a stepped crown on its flat roof, an inlaid floor, and under it a burial vault
        // with a sarcophagus and three chests, reached by a ladder.
        public List<Piece> DesertPyramidStart(int cx, int cz)
        {
            if (BiomeAt(cx * 16 + 8, cz * 16 + 8) != BI.DESERT) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 47120389 * 3), cx, cz)); int rot = (int)(rnd.Next() * 4);
            var p = OwnPiece((g, pp) => g.SandTemple(pp), cx * 16, 0, cz * 16, rot, 19, 19, 19, q => q.noTrees = true);
            var f = FootprintH(p.box);
            if (f.hi - f.lo > 7) return null;
            p.y = f.lo - 8; p.box[1] = p.y; p.box[4] = p.y + 18;
            return new List<Piece> { p };
        }

        void SandTemple(Piece p)
        {
            var W = new OwnBuilder(this, p); const int F = 8;
            int Sandy(int x, int y, int z) => W.R(x, y, z, 90) < 0.25 ? B.SANDSTONE : B.CUT_SANDSTONE;
            W.Fill(0, F + 1, 0, 18, 18, 18, 0);
            for (int z = 0; z < 19; z++) for (int x = 0; x < 19; x++) W.Down(x, F, z, B.SANDSTONE);
            W.Fill(0, F, 0, 18, F, 18, B.SMOOTH_SANDSTONE);
            // courtyard wall (2 high) with a gate, obelisks at the corners
            W.Walls(0, F + 1, 0, 18, F + 2, 18, Sandy);
            W.Fill(8, F + 1, 0, 10, F + 2, 0, 0);
            foreach (var (x, z) in new[] { (0, 0), (18, 0), (0, 18), (18, 18) }) { W.Fill(x, F + 1, z, x, F + 6, z, B.CHISELED_SANDSTONE); W.Set(x, F + 7, z, B.SMOOTH_SANDSTONE_SLAB); }
            // the temple 11 x 11, walls 6 high, a portico of two columns with a lintel
            W.Walls(4, F + 1, 5, 14, F + 6, 15, Sandy);
            W.Fill(5, F + 1, 6, 13, F + 6, 14, 0);
            W.Fill(8, F + 1, 5, 10, F + 4, 5, 0);
            foreach (int x in new[] { 7, 11 }) W.Fill(x, F + 1, 3, x, F + 5, 3, B.CHISELED_SANDSTONE);
            W.Fill(6, F + 6, 2, 12, F + 6, 4, B.SMOOTH_SANDSTONE_SLAB);
            foreach (int z in new[] { 8, 12 }) foreach (int x in new[] { 4, 14 }) W.Set(x, F + 3, z, 0);
            // roof and a stepped crown
            W.Fill(4, F + 7, 5, 14, F + 7, 15, B.SMOOTH_SANDSTONE);
            for (int s = 1; s <= 3; s++) W.Fill(4 + s * 2 - 1, F + 7 + s, 5 + s * 2 - 1, 14 - s * 2 + 1, F + 7 + s, 15 - s * 2 + 1, s == 3 ? B.CHISELED_SANDSTONE : B.CUT_SANDSTONE);
            // inlaid floor: an orange border, a blue diamond, white corners
            for (int z = 6; z <= 14; z++) for (int x = 5; x <= 13; x++)
            {
                int dx = Math.Abs(x - 9), dz = Math.Abs(z - 10), d = dx + dz;
                int id = x == 5 || x == 13 || z == 6 || z == 14 ? B.TERRA_ORANGE : d == 3 ? B.BLUE_TERRACOTTA : d == 0 ? B.YELLOW_TERRACOTTA : (dx == 3 && dz == 3) ? B.TERRA_WHITE : B.SMOOTH_SANDSTONE;
                W.Set(x, F, z, id);
            }
            // the vault 7 x 7 below, ladder in the back corner
            W.Walls(5, F - 6, 6, 13, F - 1, 14, B.SANDSTONE);
            W.Fill(5, F - 7, 6, 13, F - 7, 14, B.SANDSTONE);
            W.Fill(6, F - 6, 7, 12, F - 2, 13, 0);
            W.Fill(6, F - 1, 7, 12, F - 1, 13, B.SANDSTONE);
            for (int y = F - 6; y <= F; y++) W.Set(12, y, 13, B.LADDER, 2);
            W.Fill(8, F - 6, 9, 10, F - 6, 11, B.CUT_SANDSTONE);
            W.Set(9, F - 5, 10, B.SMOOTH_SANDSTONE); W.Set(9, F - 5, 11, B.SMOOTH_SANDSTONE_SLAB);
            int lt = Loot.Get("DESERT_PYRAMID");
            W.Chest(6, F - 6, 10, 3, lt); W.Chest(12, F - 6, 9, 1, lt); W.Chest(9, F - 6, 7, 0, lt);
            W.Set(6, F - 4, 7, B.TORCH, 1 + 2); W.Set(12, F - 4, 7, B.TORCH, 1 + 2);
        }

        // ===================================================================== bog shack (swamp)
        // A mud-brick shack on a plank deck raised on mangrove stilts with braces, a porch with a ladder,
        // a thatched hay roof, and inside a cauldron, a brewing stand, a barrel and a potted mushroom.
        public List<Piece> SwampHutStart(int cx, int cz)
        {
            int bio = BiomeAt(cx * 16 + 8, cz * 16 + 8);
            if (bio != BI.SWAMP && bio != BI.MANGROVE_SWAMP) return null;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 55301927 * 3), cx, cz)); int rot = (int)(rnd.Next() * 4);
            var p = OwnPiece((g, pp) => g.BogShack(pp), cx * 16 + 3, 0, cz * 16 + 3, rot, 9, 8, 9, q => { q.noTrees = true; q.margin = 1; });
            var f = FootprintH(p.box);
            p.y = Math.Max(f.avg, SEA + 1) + 2; p.box[1] = p.y - 4; p.box[4] = p.y + 7;
            return new List<Piece> { p };
        }

        void BogShack(Piece p)
        {
            var W = new OwnBuilder(this, p);
            W.Fill(0, 1, 0, 8, 7, 8, 0);
            W.Fill(0, 0, 0, 8, 0, 8, B.SPRUCE_PLANKS);
            foreach (var (x, z) in new[] { (0, 0), (8, 0), (0, 8), (8, 8), (4, 8) }) { W.Down(x, -1, z, B.MANGROVE_LOG); }
            foreach (var (x, z) in new[] { (1, 0), (7, 0), (1, 8), (7, 8) }) W.Set(x, -1, z, B.MANGROVE_PLANKS_FENCE);
            // walls 3 high, mangrove log corners, a door and fence windows
            W.Walls(1, 1, 2, 7, 3, 7, (x, y, z) => (x == 1 || x == 7) && (z == 2 || z == 7) ? B.MANGROVE_LOG : B.MUD_BRICKS);
            W.Set(4, 1, 2, B.SPRUCE_DOOR, 2); W.Set(4, 2, 2, B.SPRUCE_DOOR, 2 | 4);
            foreach (int z in new[] { 4, 5 }) { W.Set(1, 2, z, B.MANGROVE_PLANKS_FENCE); W.Set(7, 2, z, B.MANGROVE_PLANKS_FENCE); }
            // porch railing and a ladder down from it
            for (int x = 0; x <= 8; x++) if (x != 4) W.Set(x, 1, 0, B.MANGROVE_PLANKS_FENCE);
            W.Set(0, 1, 1, B.MANGROVE_PLANKS_FENCE); W.Set(8, 1, 1, B.MANGROVE_PLANKS_FENCE);
            // the ladder hangs on a post under the deck edge and reaches down to the ground
            W.Down(4, -1, 1, B.MANGROVE_LOG);
            for (int y = 0; y >= -8; y--)
            {
                int c = W.Get(4, y, 0);
                if (y < 0 && c > 0 && !IsLiquidId(c) && (FLAGS[c] & BF_REPLACE) == 0) break;
                W.Set(4, y, 0, B.LADDER, 2);
            }
            W.Set(4, 0, 1, B.SPRUCE_PLANKS);
            // thatched roof: three stepped layers of hay
            W.Fill(0, 4, 1, 8, 4, 8, B.HAY_BLOCK); W.Fill(1, 5, 2, 7, 5, 7, B.HAY_BLOCK); W.Fill(2, 6, 3, 6, 6, 6, B.HAY_BLOCK); W.Fill(3, 7, 4, 5, 7, 5, B.HAY_BLOCK);
            // inside
            W.Set(2, 1, 6, B.CAULDRON, 3); W.Set(3, 1, 6, B.BREWING_STAND, 0); W.Set(6, 1, 6, B.BARREL, 4);
            W.Set(6, 1, 3, B.CRAFTING_TABLE); W.Set(2, 1, 3, B.FLOWER_POT); W.Set(4, 3, 5, B.LANTERN, 1);
        }

        // ===================================================================== old mines
        // A hub cavern with a dirt floor; from it timbered tunnels grow: straight runs (with rails one time
        // in three), junction chambers, turns, ramps down a level, dead ends with a supply chest. Posts and
        // beams every four blocks, plank bridges over gaps, cobwebs; one tunnel in fifteen is a spider den.
        const int MINE_MAX = 40;
        static readonly int[] MDX = { 0, -1, 0, 1 }, MDZ = { 1, 0, -1, 0 };

        public List<Piece> MineStart(int cx, int cz)
        {
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 22037 * 3), cx, cz));
            int Ri(int n) => (int)(rnd.Next() * n);
            int ground = FloorH(cx * 16 + 8, cz * 16 + 8);
            int y = Math.Min(ground - 16, 6 + Ri(36));
            if (y < WORLD_MIN_Y + 12) return null;
            var bp = BPROP[BiomeAt(cx * 16 + 8, cz * 16 + 8)];
            bool mesa = bp != null && bp.badlands;
            var segs = new List<MineSeg>(); var boxes = new List<int[]>();
            // overlap is tested on the dug cores only (a tunnel's walls may touch its neighbours)
            bool Free(int[] b) { foreach (var a in boxes) if (a[0] <= b[3] && a[3] >= b[0] && a[1] <= b[4] && a[4] >= b[1] && a[2] <= b[5] && a[5] >= b[2]) return false; return true; }
            void Add(MineSeg s) { segs.Add(s); boxes.Add(s.core ?? s.box); }
            int X = cx * 16 + 4, Z = cz * 16 + 4;
            Add(new MineSeg { kind = "hub", box = new[] { X - 4, y, Z - 4, X + 4, y + 5, Z + 4 }, y = y });
            var queue = new Queue<(int, int, int, int, int)>();
            queue.Enqueue((X, y, Z - 5, 2, 0)); queue.Enqueue((X, y, Z + 5, 0, 0)); queue.Enqueue((X - 5, y, Z, 1, 0)); queue.Enqueue((X + 5, y, Z, 3, 0));
            while (queue.Count > 0 && segs.Count < MINE_MAX)
            {
                var (x, yy, z, d, depth) = queue.Dequeue();
                if (depth > 9 || Math.Abs(x - X) > 72 || Math.Abs(z - Z) > 72) continue;
                double r = rnd.Next();
                if (r < 0.12)
                {                                         // ramp: 8 long, 4 down
                    int ex = x + MDX[d] * 7, ez = z + MDZ[d] * 7;
                    var box = new[] { Math.Min(x, ex) - 1, yy - 5, Math.Min(z, ez) - 1, Math.Max(x, ex) + 1, yy + 3, Math.Max(z, ez) + 1 };
                    if (!Free(box)) continue;
                    Add(new MineSeg { kind = "ramp", box = box, x = x, y = yy, z = z, d = d });
                    queue.Enqueue((ex + MDX[d], yy - 4, ez + MDZ[d], d, depth + 1));
                    continue;
                }
                {
                    int len = 4 * (2 + Ri(4)), ex = x + MDX[d] * (len - 1), ez = z + MDZ[d] * (len - 1);
                    int lx = (d & 1) != 0 ? 0 : 1, lz = (d & 1) != 0 ? 1 : 0;      // sideways extent of the dug part
                    var core = new[] { Math.Min(x, ex) - lx, yy - 1, Math.Min(z, ez) - lz, Math.Max(x, ex) + lx, yy + 3, Math.Max(z, ez) + lz };
                    var box = new[] { core[0] - lx, core[1], core[2] - lz, core[3] + lx, core[4], core[5] + lz };
                    if (!Free(core)) continue;
                    double kr = rnd.Next();
                    bool chest = rnd.Next() < 0.15;
                    Add(new MineSeg { kind = "tunnel", box = box, core = core, x = x, y = yy, z = z, d = d, len = len, rails = kr < 0.33, spider = kr > 0.93, chest = chest });
                    // side branches every 8 blocks
                    for (int k = 4; k < len - 2; k += 8) if (rnd.Next() < 0.3)
                    {
                        int sx = x + MDX[d] * k, sz = z + MDZ[d] * k, sd = rnd.Next() < 0.5 ? (d + 1) & 3 : (d + 3) & 3;
                        queue.Enqueue((sx + MDX[sd] * 2, yy, sz + MDZ[sd] * 2, sd, depth + 1));
                    }
                    double e = rnd.Next(); int nx = ex + MDX[d], nz = ez + MDZ[d];
                    if (e < 0.3)
                    {                                          // junction chamber 5 x 5
                        int jx = nx + MDX[d] * 2, jz = nz + MDZ[d] * 2; var jb = new[] { jx - 2, yy - 1, jz - 2, jx + 2, yy + 4, jz + 2 };
                        if (Free(jb))
                        {
                            Add(new MineSeg { kind = "junction", box = jb, x = jx, y = yy, z = jz });
                            foreach (int nd in new[] { d, (d + 1) & 3, (d + 3) & 3 }) queue.Enqueue((jx + MDX[nd] * 3, yy, jz + MDZ[nd] * 3, nd, depth + 1));
                        }
                    }
                    else if (e < 0.75)
                    {
                        int nd = rnd.Next() < 0.5 ? d : (rnd.Next() < 0.5 ? (d + 1) & 3 : (d + 3) & 3);
                        queue.Enqueue((nx, yy, nz, nd, depth + 1));
                    }
                }
            }
            var res = new List<Piece>(segs.Count);
            foreach (var sg in segs)
            {
                var b = sg.box;
                res.Add(new Piece { build = (g, pp) => g.MineSegment(pp), x = b[0], y = b[1], z = b[2], rot = 0, box = (int[])b.Clone(), seg = sg, mesa = mesa });
            }
            return res;
        }

        void MineSegment(Piece p)
        {
            var ids = this.ids; var meta = this.meta; int x0 = this.x0, z0 = this.z0; var sg = p.seg; int seed = this.seed;
            int PL = p.mesa ? B.DARK_PLANKS : B.PLANKS, LG = p.mesa ? B.DARK_LOG : B.LOG;
            int At(int x, int y, int z) => x >= x0 && x < x0 + 16 && z >= z0 && z < z0 + 16 && y > WORLD_MIN_Y + 1 && y < WORLD_MAX_Y ? CI(x - x0, y, z - z0) : -1;
            bool Liquid(int i) { int c = ids[i]; return IsLiquidId(c) || (FLAGS[c] & BF_AQUATIC) != 0; }
            void Set(int x, int y, int z, int id, int m = 0) { int i = At(x, y, z); if (i >= 0 && !Liquid(i)) { ids[i] = (ushort)id; meta[i] = (byte)m; } }
            int Get(int x, int y, int z) { int i = At(x, y, z); return i >= 0 ? ids[i] : -1; }
            double R(int x, int y, int z, int k) => Hash.Hash3(unchecked(seed + 2300 + k), x, y, z);
            // MineshaftPiece.isInInvalidLocation: no part of a piece is built in a chunk where liquid touches
            // the shell one block outside its dug box (no tunnels opened under the sea floor or into lava)
            {
                var c = sg.core ?? sg.box; int ax = c[0] - 1, ay = c[1] - 1, az = c[2] - 1, bx = c[3] + 1, by = c[4] + 1, bz = c[5] + 1;
                int lx = Math.Max(ax, x0), hx = Math.Min(bx, x0 + 15), lz = Math.Max(az, z0), hz = Math.Min(bz, z0 + 15);
                bool Wet(int x, int y, int z) { int i = At(x, y, z); return i >= 0 && Liquid(i); }
                for (int x = lx; x <= hx; x++) for (int z = lz; z <= hz; z++)
                {
                    if (x != ax && x != bx && z != az && z != bz) { if (Wet(x, ay, z) || Wet(x, by, z)) return; continue; }
                    for (int y = ay; y <= by; y++) if (Wet(x, y, z)) return;
                }
            }
            // air for a walkway cell column (floor at y), bridging the floor with planks over gaps
            void Walk(int x, int y, int z, int h)
            {
                if (At(x, y, z) < 0) return;
                for (int k = 0; k < h; k++) Set(x, y + k, z, 0);
                int u = Get(x, y - 1, z);
                if (u >= 0 && OPAQUE[u] == 0) Set(x, y - 1, z, PL);
            }
            if (sg.kind == "hub" || sg.kind == "junction")
            {
                int ax = sg.box[0], ay = sg.box[1], az = sg.box[2], bx = sg.box[3], by = sg.box[4], bz = sg.box[5], fy = sg.y;
                for (int z = az; z <= bz; z++) for (int x = ax; x <= bx; x++)
                {
                    Walk(x, fy, z, by - fy);
                    if (sg.kind == "hub") Set(x, fy - 1, z, R(x, fy, z, 1) < 0.15 ? B.GRAVEL : B.DIRT);
                }
                foreach (var (x, z) in new[] { (ax, az), (bx, az), (ax, bz), (bx, bz) }) for (int y = fy; y < by; y++) Set(x, y, z, LG);
                for (int x = ax; x <= bx; x++) { Set(x, by - 1, az, PL); Set(x, by - 1, bz, PL); }
                Set((ax + bx) >> 1, by - 2, (az + bz) >> 1, B.LANTERN, 1);
                Set((ax + bx) >> 1, by - 1, (az + bz) >> 1, PL);
                return;
            }
            int d = sg.d, sxd = MDZ[d], szd = -MDX[d];   // (sxd, szd): sideways
            if (sg.kind == "ramp")
            {
                for (int k = 0; k < 8; k++)
                {
                    int fy = sg.y - (k >> 1), ccx = sg.x + MDX[d] * k, ccz = sg.z + MDZ[d] * k;
                    for (int c = -1; c <= 1; c++) Walk(ccx + sxd * c, fy, ccz + szd * c, 3 + (k & 1));
                }
                return;
            }
            // tunnel
            for (int k = 0; k < sg.len; k++)
            {
                int ccx = sg.x + MDX[d] * k, ccz = sg.z + MDZ[d] * k, fy = sg.y;
                for (int c = -1; c <= 1; c++)
                {
                    int x = ccx + sxd * c, z = ccz + szd * c;
                    Walk(x, fy, z, 3);
                    if (sg.spider ? R(x, fy, z, 2) < 0.35 : (c != 0 && R(x, fy + 2, z, 3) < 0.05)) Set(x, fy + (sg.spider ? (int)(R(x, fy, z, 4) * 3) : 2), z, B.COBWEB);
                }
                if (sg.rails && Get(ccx, fy - 1, ccz) > 0) Set(ccx, fy, ccz, B.RAIL, (d & 1) != 0 ? 1 : 0);
                if (k % 4 == 2)
                {
                    // timber frame: two posts and a beam (now and then fallen: only the posts)
                    foreach (int c in new[] { -1, 1 }) { Set(ccx + sxd * c, fy, ccz + szd * c, LG); Set(ccx + sxd * c, fy + 1, ccz + szd * c, B.OAK_FENCE); }
                    if (R(ccx, fy, ccz, 5) > 0.15) for (int c = -1; c <= 1; c++) Set(ccx + sxd * c, fy + 2, ccz + szd * c, PL);
                    else Set(ccx, fy, ccz, B.GRAVEL);
                    // a torch on the beam, where the beam is there (a wall torch needs the block it hangs on)
                    if (R(ccx, fy, ccz, 6) < 0.12 && Get(ccx, fy + 2, ccz) == PL) { int tx = ccx - MDX[d], tz = ccz - MDZ[d]; Set(tx, fy + 2, tz, B.TORCH, 1 + d); }
                }
            }
            int mx = sg.x + MDX[d] * (sg.len >> 1), mz = sg.z + MDZ[d] * (sg.len >> 1);
            if (sg.spider)
            {
                int i = At(mx, sg.y, mz);
                if (i >= 0) { ids[i] = B.SPAWNER; meta[i] = (byte)SPAWNER_MOB["cave_spider"]; spawnerList.Add(mx); spawnerList.Add(sg.y); spawnerList.Add(mz); }
            }
            if (sg.chest)
            {
                // a niche in the side wall with a supply chest facing the tunnel
                int nx = mx + sxd * 2, nz = mz + szd * 2, i = At(nx, sg.y, nz);
                if (i >= 0 && !Liquid(i)) { ids[i] = B.CHEST; meta[i] = (byte)(((d + 1) & 3) | (Loot.Get("ABANDONED_MINESHAFT") << 2)); Set(nx, sg.y + 1, nz, 0); }
            }
        }

        // ===================================================================== dungeons
        // A monster den dug into the rock: a cobblestone room with rounded corners, a vaulted ceiling, a floor
        // of mossy cobble, cobble and gravel, the spawner in the middle, chests against the walls, bones and
        // cobwebs. Kept only where the rock around is solid and a cave opens into it at one to five places.
        void Dungeons()
        {
            var ids = this.ids; var meta = this.meta;
            var rnd = new Mulberry(Hash.Hash2i(unchecked(seed + 1301), cx, cz));
            int Ri(int n) => (int)(rnd.Next() * n);
            bool Solid(int x, int y, int z) => OPAQUE[ids[CI(x, y, z)]] == 1;
            bool Air(int x, int y, int z) => ids[CI(x, y, z)] == 0;
            int[] mobs = { SPAWNER_MOB["skeleton"], SPAWNER_MOB["zombie"], SPAWNER_MOB["zombie"], SPAWNER_MOB["spider"] };
            void Room(int yLo, int yHi)
            {
                int xr = 2 + Ri(2), zr = 2 + Ri(2);
                int ox = xr + 1 + Ri(14 - 2 * xr), oz = zr + 1 + Ri(14 - 2 * zr), oy = yLo + Ri(yHi - yLo + 1);
                if (oy - 1 <= WORLD_MIN_Y || oy + 5 >= WORLD_MAX_Y) return;
                int ax = ox - xr - 1, bx = ox + xr + 1, az = oz - zr - 1, bz = oz + zr + 1; const int H = 4;
                int open = 0;
                for (int x = ax; x <= bx; x++) for (int z = az; z <= bz; z++)
                {
                    if (!Solid(x, oy - 1, z) || !Solid(x, oy + H, z)) return;
                    if ((x == ax || x == bx || z == az || z == bz) && Air(x, oy, z) && Air(x, oy + 1, z)) open++;
                }
                if (open < 1 || open > 5) return;
                int Stone() => rnd.Next() < 0.4 ? B.MOSSY : B.COBBLE;
                int Floor() { double r = rnd.Next(); return r < 0.55 ? B.MOSSY : r < 0.88 ? B.COBBLE : B.GRAVEL; }
                for (int x = ax; x <= bx; x++) for (int z = az; z <= bz; z++) for (int y = oy - 1; y <= oy + H; y++)
                {
                    int i = CI(x, y, z); bool wall = x == ax || x == bx || z == az || z == bz;
                    // rounded corners: the cells next to the corners stay solid inside the room
                    bool corner = (x == ax + 1 || x == bx - 1) && (z == az + 1 || z == bz - 1);
                    // the vault: the outer ring of the top layer is solid
                    bool vault = y == oy + H - 1 && (x == ax + 1 || x == bx - 1 || z == az + 1 || z == bz - 1);
                    if (!wall && y >= oy && y < oy + H && !corner && !vault) { ids[i] = 0; meta[i] = 0; continue; }
                    if (wall && y >= oy && y < oy + 2 && ids[i] == 0) continue;       // the openings stay open
                    ids[i] = (ushort)(y == oy - 1 ? Floor() : Stone()); meta[i] = 0;
                }
                // chests on the floor against a wall, facing into the room
                int chests = 1 + Ri(2);
                for (int t = 0; t < 10 && chests > 0; t++)
                {
                    int side = Ri(4), k = side < 2 ? ax + 2 + Ri(bx - ax - 3) : az + 2 + Ri(bz - az - 3);
                    int x, z, face;
                    if (side == 0) { x = k; z = az + 1; face = 0; } else if (side == 1) { x = k; z = bz - 1; face = 2; } else if (side == 2) { x = ax + 1; z = k; face = 3; } else { x = bx - 1; z = k; face = 1; }
                    if (Math.Abs(x - ox) + Math.Abs(z - oz) < 2) continue;
                    int i = CI(x, oy, z);
                    if (ids[i] != 0) continue;
                    ids[i] = B.CHEST; meta[i] = (byte)(face | (Loot.Get("SIMPLE_DUNGEON") << 2)); chests--;
                }
                // a heap of bones beside a corner column, cobwebs under the vault
                int cx2 = rnd.Next() < 0.5 ? ax + 2 : bx - 2, cz2 = rnd.Next() < 0.5 ? az + 1 : bz - 1;
                if (ids[CI(cx2, oy, cz2)] == 0 && rnd.Next() < 0.6) ids[CI(cx2, oy, cz2)] = B.BONE_BLOCK;
                for (int x = ax + 2; x <= bx - 2; x++) for (int z = az + 2; z <= bz - 2; z++)
                {
                    int i = CI(x, oy + H - 2, z);
                    if (ids[i] == 0 && (x == ax + 2 || x == bx - 2 || z == az + 2 || z == bz - 2) && rnd.Next() < 0.15) ids[i] = B.COBWEB;
                }
                {
                    int i = CI(ox, oy, oz);
                    ids[i] = B.SPAWNER; meta[i] = (byte)mobs[Ri(4)];
                    spawnerList.Add(x0 + ox); spawnerList.Add(oy); spawnerList.Add(z0 + oz);
                }
            }
            for (int k = 0; k < 10; k++) Room(0, WORLD_MAX_Y - 1);
            for (int k = 0; k < 4; k++) Room(WORLD_MIN_Y + 6, -1);
        }

        // boxes of nearby structures that keep trees out (added to the village boxes the tree check uses)
        void StructureTreeBoxes()
        {
            var outp = vilBoxes ?? (vilBoxes = new List<int[]>());
            foreach (var set in OWN_TREELESS_SETS)
            {
                int R = set.reach + 2;
                for (int rz = FloorDiv(cz - R, set.spacing); rz <= FloorDiv(cz + R, set.spacing); rz++)
                    for (int rx = FloorDiv(cx - R, set.spacing); rx <= FloorDiv(cx + R, set.spacing); rx++)
                    {
                        var (sx, sz) = SpreadStart(set, rx, rz);
                        if (Math.Abs(sx - cx) > R || Math.Abs(sz - cz) > R) continue;
                        var st = StructStart(set, sx, sz);
                        if (st != null) foreach (var p in st) if (p.noTrees) outp.Add(new[] { p.box[0] - 9, p.box[1], p.box[2] - 9, p.box[3] + 9, p.box[4], p.box[5] + 9 });   // crowns reach up to 9 out
                    }
            }
        }
    }
}
