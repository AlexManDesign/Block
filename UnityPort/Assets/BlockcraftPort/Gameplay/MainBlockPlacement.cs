using System;
using UnityEngine;
using static BlockcraftPort.SourceBlockRules;

namespace BlockcraftPort
{
    /// <summary>
    /// main.js kT() right-click on a world block: door/gate/trapdoor toggles (yL), flower pots (HL)
    /// and block placement with orientation metadata (zL stairs/doors/torches/..., VL slabs,
    /// FL beds, cross plants, cactus, logs, leaves). Rules run in source coordinates via
    /// SourceBlockRules, so metadata is exactly what main.js writes (mirrored once on store).
    /// </summary>
    public static class MainBlockPlacement
    {
        public enum Result { None, Used, Consumed }

        /// <summary>main raycast target (n1()) in source coordinates. Face points INTO the hit block.</summary>
        public struct Target
        {
            public BlockId Id; public int X, Y, Z, PX, PY, PZ, FX, FY, FZ; public float HX, HY, HZ;
            public static Target From(VoxelHit h)
            {
                Target t;
                t.Id = h.Id; t.X = h.Block.x; t.Y = h.Block.y; t.Z = -h.Block.z - 1;
                // main k = outward normal (Unity normal with z mirrored); face = -k.
                int kx = h.Normal.x, ky = h.Normal.y, kz = -h.Normal.z;
                t.FX = -kx; t.FY = -ky; t.FZ = -kz;
                t.PX = t.X + kx; t.PY = t.Y + ky; t.PZ = t.Z + kz;
                t.HX = h.Point.x; t.HY = h.Point.y; t.HZ = -h.Point.z;
                return t;
            }
        }

        public struct Context
        {
            public VoxelWorld World;
            public float Yaw;          // main Q.yaw
            public bool Survival;      // main Je()
            public Vector3 PlayerFeet; // Unity position of the player's feet
        }

        static Context ctx;

        /// <summary>main LT(): the cell overlaps the player's AABB (source coordinates).</summary>
        static bool LT(int x, int y, int z)
        {
            float px = ctx.PlayerFeet.x, py = ctx.PlayerFeet.y, pz = -ctx.PlayerFeet.z;
            return x < px + Re && x + 1 > px - Re && y < py + Rt && y + 1 > py && z < pz + Re && z + 1 > pz - Re;
        }
        static bool Replaceable(BlockId b) => b == BlockId.Air || EA(b) || BA(b);

        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// main kT() block-targeted uses that precede the held item: R4 → yL (doors, gates,
        /// trapdoors) and FLOWER_POT → HL. <paramref name="giveBack"/> is the plant returned to a
        /// survival player when a pot is emptied.
        /// </summary>
        public static Result UseTarget(in Context c, VoxelHit hit, BlockId held, out BlockId giveBack)
        {
            ctx = c; W = c.World; giveBack = BlockId.Air;
            Target A = Target.From(hit);
            if (A.Id == BlockId.FlowerPot)
            {
                int e = RA(A.X, A.Y, A.Z) & 255;
                if (e > 0)
                {
                    BlockId r = PotPlant(e);
                    NA(A.X, A.Y, A.Z, BlockId.FlowerPot, 0);
                    if (c.Survival) giveBack = r;
                    return Result.Used;
                }
                int t = PotMetaFor(held);
                if (t > 0) { NA(A.X, A.Y, A.Z, BlockId.FlowerPot, t); return c.Survival ? Result.Consumed : Result.Used; }
            }
            BlockShape s = Shape(A.Id);
            if (J1(A.Id) && (s == BlockShape.Door || s == BlockShape.Gate || s == BlockShape.Trapdoor))
            {
                ToggleOpen(A);
                return Result.Used;
            }
            return Result.None;
        }

        /// <summary>main yL(): flip the open bit (3); a door flips its other half too.</summary>
        static void ToggleOpen(in Target A)
        {
            int r = RA(A.X, A.Y, A.Z), t = ((r >> 3) & 1) != 0 ? 0 : 1;
            NA(A.X, A.Y, A.Z, A.Id, (r & ~8) | (t << 3));
            if (Shape(A.Id) == BlockShape.Door)
            {
                int a = ((r >> 2) & 1) != 0 ? A.Y - 1 : A.Y + 1;
                if (S(A.X, a, A.Z) == A.Id)
                {
                    int i = RA(A.X, a, A.Z);
                    NA(A.X, a, A.Z, A.Id, (i & ~8) | (t << 3));
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        /// <summary>main kT(): placing held block <paramref name="E"/> against <paramref name="hit"/>.</summary>
        public static Result Place(in Context c, VoxelHit hit, BlockId E)
        {
            ctx = c; W = c.World;
            if (E == BlockId.Air) return Result.None;
            Target A = Target.From(hit);
            if (A.Id == BlockId.CaveVines)
            {
                int O = RA(A.X, A.Y, A.Z) & 1;
                NA(A.X, A.Y, A.Z, BlockId.CaveVines, O ^ 1);
                return Result.Used;
            }
            if (A.Id == BlockId.SeaPickle && E == BlockId.SeaPickle)
            {
                int G = RA(A.X, A.Y, A.Z), N = G & 3;
                if (N >= 3) return Result.None;
                NA(A.X, A.Y, A.Z, BlockId.SeaPickle, (G & 4) | (N + 1));
                return Result.Consumed;
            }
            if (BedHeadOf(E) != BlockId.Air) return PlaceBed(A, E) ? Result.Consumed : Result.None;
            if (J1(E) && Shape(E) == BlockShape.Slab) return PlaceSlab(E, A) ? Result.Consumed : Result.None;

            bool Y = BA(A.Id);
            int _x = Y ? A.X : A.PX, k = Y ? A.Y : A.PY, y = Y ? A.Z : A.PZ;
            if (k < K + 1 || k >= VA) return Result.None;
            BlockId b = S(_x, k, y);
            if (!(b == BlockId.Air || EA(b) || Oe(b) || BA(b))) return Result.None;
            BlockShape es = Shape(E);
            if (J1(E) && es == BlockShape.Torch && EA(b))
            {
                // main L1(): a torch placed into water is consumed and pops off as an item.
                MainBlockDrops.Spawn(_x + .5f, k + .5f, -(y + .5f), E);
                return Result.Consumed;
            }
            bool iA = J1(E) && (es == BlockShape.Ladder || es == BlockShape.Torch || es == BlockShape.Button || es == BlockShape.FlatFaces || es == BlockShape.Rail);
            if (E != BlockId.Water && !Oe(E) && !iA && LT(_x, k, y)) return Result.None;

            Cell c0, c1; int count;
            if (J1(E))
            {
                if (!ZL(E, A, _x, k, y, out c0, out c1, out count)) return Result.None;
            }
            else if (BA(E))
            {
                int QA = E == BlockId.CaveVines ? 1 : 0;
                if (Aquatic(E))
                {
                    bool GA = b == BlockId.Water || Aquatic(b);
                    if (NeedsWater(E) && !GA) return Result.None;
                    if (GA) QA |= 4;
                }
                if (Os(E, QA, _x, k, y)) return Result.None;
                c0 = new Cell(_x, k, y, E, QA); c1 = default; count = 1;
            }
            else if (E == BlockId.Cactus)
            {
                BlockId YA = S(_x, k - 1, y);
                if (!(YA == BlockId.Sand || YA == BlockId.RedSand || YA == BlockId.Cactus) ||
                    Ae(S(_x + 1, k, y)) || Ae(S(_x - 1, k, y)) || Ae(S(_x, k, y + 1)) || Ae(S(_x, k, y - 1))) return Result.None;
                c0 = new Cell(_x, k, y, E, 0); c1 = default; count = 1;
            }
            else if (E == BlockId.Chest)
            {
                // (Ao()+2)%4 plus q8() double-chest pairing.
                return MainWorldFunctionalBlocks.PlaceChestWorld(ToUnity(_x, k, y), c.Yaw, true) ? Result.Consumed : Result.None;
            }
            else if (LeavesKey(E)) { c0 = new Cell(_x, k, y, E, 1); c1 = default; count = 1; }
            else if (Axis(E))
            {
                int m = A.FY != 0 ? 0 : A.FX != 0 ? 1 : 2;
                c0 = new Cell(_x, k, y, E, m); c1 = default; count = 1;
            }
            else { c0 = new Cell(_x, k, y, E, 0); c1 = default; count = 1; }

            NA(c0.X, c0.Y, c0.Z, c0.Id, c0.Meta);
            if (count > 1) NA(c1.X, c1.Y, c1.Z, c1.Id, c1.Meta);
            return Result.Consumed;
        }

        struct Cell
        {
            public int X, Y, Z, Meta; public BlockId Id;
            public Cell(int x, int y, int z, BlockId id, int meta) { X = x; Y = y; Z = z; Id = id; Meta = meta; }
        }

        /// <summary>main zL(): cells and metadata for a shaped block.</summary>
        static bool ZL(BlockId A, in Target e, int r, int t, int n, out Cell c0, out Cell c1, out int count)
        {
            c0 = default; c1 = default; count = 1;
            int u = e.FY;
            switch (Shape(A))
            {
                case BlockShape.Torch:
                    if (u < 0 && Fr(r, t - 1, n)) { c0 = new Cell(r, t, n, A, 0); return true; }
                    if (u == 0)
                    {
                        int E = Xw(e.FX, e.FZ);
                        if (E >= 0 && Lt(e.X, e.Y, e.Z)) { c0 = new Cell(r, t, n, A, E + 1); return true; }
                    }
                    return false;
                case BlockShape.Ladder:
                    if (u == 0)
                    {
                        int s = Xw(e.FX, e.FZ);
                        if (s >= 0 && Lt(e.X, e.Y, e.Z)) { c0 = new Cell(r, t, n, A, s); return true; }
                    }
                    return false;
                case BlockShape.FlatFaces:
                {
                    int C = 0;
                    if (Lt(r, t, n + 1)) C |= 1;
                    if (Lt(r - 1, t, n)) C |= 2;
                    if (Lt(r, t, n - 1)) C |= 4;
                    if (Lt(r + 1, t, n)) C |= 8;
                    if (A != BlockId.Vine) // main oA "lichen" also takes ceiling/floor faces
                    {
                        if (Lt(r, t + 1, n)) C |= 16;
                        if (Fr(r, t - 1, n)) C |= 32;
                    }
                    if (C == 0) return false;
                    c0 = new Cell(r, t, n, A, C); return true;
                }
                case BlockShape.Stairs:
                {
                    int f = u < 0 ? 0 : (u > 0 || e.HY - t > .5f) ? 1 : 0;
                    c0 = new Cell(r, t, n, A, Ao(ctx.Yaw) | (f != 0 ? 4 : 0)); return true;
                }
                case BlockShape.Fence: c0 = new Cell(r, t, n, A, 0); return true;
                case BlockShape.Gate:
                {
                    int w = Ao(ctx.Yaw);
                    c0 = new Cell(r, t, n, A, w == 1 || w == 3 ? 0 : 1); return true;
                }
                case BlockShape.Trapdoor:
                {
                    int l = u < 0 ? 0 : (u > 0 || e.HY - t > .5f) ? 1 : 0;
                    c0 = new Cell(r, t, n, A, ((Ao(ctx.Yaw) + 2) % 4) | (l != 0 ? 4 : 0)); return true;
                }
                case BlockShape.Door:
                {
                    if (t + 1 >= VA || !Fr(r, t - 1, n)) return false;
                    BlockId P = S(r, t + 1, n);
                    if (!(P == BlockId.Air || EA(P) || BA(P))) return false;
                    int c = Ao(ctx.Yaw), B = GL(c, r, t, n, e);
                    c0 = new Cell(r, t, n, A, c | (B << 4)); c1 = new Cell(r, t + 1, n, A, c | 4 | (B << 4)); count = 2; return true;
                }
                case BlockShape.Button:
                    if (u < 0 && Fr(r, t - 1, n)) { c0 = new Cell(r, t, n, A, 0); return true; }
                    if (u > 0 && Lt(e.X, e.Y, e.Z)) { c0 = new Cell(r, t, n, A, 5); return true; }
                    if (u == 0)
                    {
                        int d = Xw(e.FX, e.FZ);
                        if (d >= 0 && Lt(e.X, e.Y, e.Z)) { c0 = new Cell(r, t, n, A, d + 1); return true; }
                    }
                    return false;
                case BlockShape.TallPlant:
                {
                    if (t + 1 >= VA || !Fr(r, t - 1, n)) return false;
                    BlockId h = S(r, t + 1, n);
                    if (!(h == BlockId.Air || EA(h) || BA(h))) return false;
                    c0 = new Cell(r, t, n, A, 0); c1 = new Cell(r, t + 1, n, A, 4); count = 2; return true;
                }
                case BlockShape.LilyPad:
                {
                    int I = t;
                    while (I < VA && EA(S(r, I, n))) I++;
                    BlockId D = S(r, I - 1, n);
                    if (S(r, I, n) == BlockId.Air && (EA(D) || D == BlockId.Ice || D == BlockId.PackedIce || D == BlockId.BlueIce))
                    { c0 = new Cell(r, I, n, A, 0); return true; }
                    return false;
                }
                case BlockShape.SeaPickle:
                    if (!Fr(r, t - 1, n)) return false;
                    c0 = new Cell(r, t, n, A, S(r, t, n) == BlockId.Water ? 4 : 0); return true;
                case BlockShape.Bamboo:
                    if (!(Fr(r, t - 1, n) || S(r, t - 1, n) == A)) return false;
                    c0 = new Cell(r, t, n, A, 0); return true;
                case BlockShape.Pot:
                    if (!Fr(r, t - 1, n)) return false;
                    c0 = new Cell(r, t, n, A, 0); return true;
                case BlockShape.ShipWheel: c0 = new Cell(r, t, n, A, Ao(ctx.Yaw)); return true;
                case BlockShape.Rail:
                {
                    if (!Fr(r, t - 1, n)) return false;
                    int T = Ao(ctx.Yaw);
                    c0 = new Cell(r, t, n, A, T == 1 || T == 3 ? 1 : 0); return true;
                }
            }
            c0 = new Cell(r, t, n, A, 0); return true;
        }

        /// <summary>main GL(): door hinge side (0/1) from neighbouring doors, walls, then the hit point.</summary>
        static int GL(int A, int e, int r, int t, in Target n)
        {
            int ax = Ir[(A + 1) % 4, 0], az = Ir[(A + 1) % 4, 2], ix = Ir[(A + 3) % 4, 0], iz = Ir[(A + 3) % 4, 2];
            BlockId u = S(e + ax, r, t + az), s = S(e + ix, r, t + iz);
            int E = RA(e + ax, r, t + az), C = RA(e + ix, r, t + iz);
            bool f = J1(u) && Shape(u) == BlockShape.Door && (E & 3) == A;
            bool w = J1(s) && Shape(s) == BlockShape.Door && (C & 3) == A;
            if (f && !w) return 1;
            if (w && !f) return 0;
            int l = (AC(u) ? 1 : 0) + (AC(S(e + ax, r + 1, t + az)) ? 1 : 0);
            int P = (AC(s) ? 1 : 0) + (AC(S(e + ix, r + 1, t + iz)) ? 1 : 0);
            if (l > P) return 0;
            if (P > l) return 1;
            float c = (n.HX - (e + .5f)) * ix + (n.HZ - (t + .5f)) * iz;
            return c > 0f ? 1 : 0;
        }

        /// <summary>main VL(): slab placement, merging two halves into the full block (Te).</summary>
        static bool PlaceSlab(BlockId A, in Target e)
        {
            int r1 = e.FY;
            BlockId full = Slab2(A);
            bool t = full != BlockId.Air;
            if (t && e.Id == A)
            {
                int n = RA(e.X, e.Y, e.Z);
                if (((n & 1) == 0 && r1 == -1) || ((n & 1) == 1 && r1 == 1)) { NA(e.X, e.Y, e.Z, full, 0); return true; }
            }
            int a = e.PX, i = e.PY, u = e.PZ;
            if (i < K + 1 || i >= VA) return false;
            int E = r1 == -1 ? 0 : (r1 == 1 || e.HY - i > .5f) ? 1 : 0;
            BlockId s = S(a, i, u); int C = RA(a, i, u);
            if (t && s == A && (C & 1) != E) { NA(a, i, u, full, 0); return true; }
            if (!(s == BlockId.Air || EA(s) || BA(s)) || LT(a, i, u)) return false;
            NA(a, i, u, A, E); return true;
        }

        /// <summary>main FL(): bed foot at the target, head one cell ahead in the facing direction.</summary>
        static bool PlaceBed(in Target A, BlockId e)
        {
            int r = A.PX, t = A.PY, n = A.PZ;
            if (t < K + 1 || t >= VA) return false;
            int a = Ao(ctx.Yaw), i = r + Ir[a, 0], u = n + Ir[a, 2];
            bool Ok(int x, int z) => Replaceable(S(x, t, z)) && !LT(x, t, z);
            if (!Ok(r, n) || !Ok(i, u) || !Fr(r, t - 1, n) || !Fr(i, t - 1, u)) return false;
            NA(r, t, n, e, a << 1);
            NA(i, t, u, BedHeadOf(e), a << 1);
            return true;
        }

        // ---------------------------------------------------------------------------------------
        /// <summary>
        /// main gT()/eJ() block removal side effects for a world block about to be broken at
        /// <paramref name="unityBlock"/>: the other door/bed half, a flower pot's plant, and whether
        /// the cell refills with water (Qo). Returns the block the broken cell becomes.
        /// </summary>
        public static BlockId BreakCompanions(VoxelWorld world, Vector3Int unityBlock, BlockId id, bool dropPotPlant)
        {
            W = world;
            int x = unityBlock.x, y = unityBlock.y, z = -unityBlock.z - 1;
            int meta = RA(x, y, z);
            if (J1(id) && Shape(id) == BlockShape.Door)
            {
                int a = ((meta >> 2) & 1) != 0 ? y - 1 : y + 1;
                if (S(x, a, z) == id) NA(x, a, z, BlockId.Air);
            }
            if (IsBed(id) && BedOtherHalf(x, y, z, id, out int ox, out int oz)) NA(ox, y, oz, BlockId.Air);
            if (dropPotPlant && id == BlockId.FlowerPot)
            {
                BlockId d = PotPlant(meta);
                if (d != BlockId.Air) MainTransientRenderer.SpawnDroppedBlock(x + .5f, y + .5f, -(z + .5f), d);
            }
            return Qo(id, meta, x, y, z) ? BlockId.Water : BlockId.Air;
        }
    }
}
