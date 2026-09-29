using System;
using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// main.js block predicates evaluated in SOURCE coordinates (x, y, z_source) over the Unity world.
    /// Every placement/support rule is ported verbatim against these helpers, so the only mirroring
    /// happens here: z_source = -z_unity - 1 and metadata goes through SourceCoords.MirrorMetadata.
    /// Main thread only.
    /// </summary>
    public static class SourceBlockRules
    {
        public const int K = VoxelConstants.MinY;       // main K
        public const int VA = VoxelConstants.MaxY + 1;  // main vA
        public const float Re = .3f, Rt = 1.8f;         // main player half width / height

        /// <summary>main ir[]: horizontal direction order 0=+Z, 1=-X, 2=-Z, 3=+X.</summary>
        public static readonly int[,] Ir = { { 0, 0, 1 }, { -1, 0, 0 }, { 0, 0, -1 }, { 1, 0, 0 } };

        static bool[] leavesKey, j5, stack;
        static BlockId[] bedHead, bedFoot, stripped;
        static BlockId[] concreteOf, deadCoralOf;
        static BlockId[] potPlants;

        static void Init()
        {
            if (leavesKey != null) return;
            int n = SourceBlockData.Count;
            var lk = new bool[n]; var fl = new bool[n]; var st = new bool[n];
            var head = new BlockId[n]; var foot = new BlockId[n]; var strip = new BlockId[n];
            var conc = new BlockId[n]; var coral = new BlockId[n];
            var j5Re = new System.Text.RegularExpressions.Regex(
                "SAPLING$|TULIP$|^ALLIUM$|^AZURE_BLUET$|^BLUE_ORCHID$|^CORNFLOWER$|^LILY_OF_THE_VALLEY$|^OXEYE_DAISY$|^WITHER_ROSE$|^DANDELION$|^POPPY$|^TALL_GRASS$|^FERN$");
            for (int i = 0; i < n; i++)
            {
                string k = SourceBlockData.Key[i];
                lk[i] = k.Contains("LEAVES");                   // main BT (/LEAVES/)
                fl[i] = j5Re.IsMatch(k);                        // main j5()
                if (k == "BED") head[i] = BlockRegistry.FromKey("BED_HEAD");
                else if (k.StartsWith("BED_HEAD", StringComparison.Ordinal))
                    foot[i] = BlockRegistry.FromKey(k == "BED_HEAD" ? "BED" : "BED_" + k.Substring(9));
                else if (k.StartsWith("BED_", StringComparison.Ordinal)) head[i] = BlockRegistry.FromKey("BED_HEAD_" + k.Substring(4));
                if (k.EndsWith("_CONCRETE_POWDER", StringComparison.Ordinal))
                    conc[i] = BlockRegistry.FromKey(k.Substring(0, k.Length - "_POWDER".Length));   // main _C
                if (k.EndsWith("_CORAL_BLOCK", StringComparison.Ordinal) || k.EndsWith("_CORAL_FAN", StringComparison.Ordinal))
                    if (!k.StartsWith("DEAD_", StringComparison.Ordinal)) coral[i] = BlockRegistry.FromKey("DEAD_" + k); // main YC
            }
            st[(int)BlockRegistry.FromKey("KELP")] = true;          // main J0 (Me[].stack)
            st[(int)BlockRegistry.FromKey("SUGAR_CANE")] = true;
            st[0] = false;
            // main L0: axe stripping.
            string[,] l0 = {
                { "LOG", "STRIPPED_OAK_LOG" }, { "BIRCH_LOG", "STRIPPED_BIRCH_LOG" }, { "SPRUCE_LOG", "STRIPPED_SPRUCE_LOG" },
                { "DARK_LOG", "STRIPPED_DARK_OAK_LOG" }, { "JUNGLE_LOG", "STRIPPED_JUNGLE_LOG" }, { "ACACIA_LOG", "STRIPPED_ACACIA_LOG" },
                { "CRIMSON_STEM", "STRIPPED_CRIMSON_STEM" }, { "WARPED_STEM", "STRIPPED_WARPED_STEM" },
                { "CRIMSON_HYPHAE", "STRIPPED_CRIMSON_HYPHAE" }, { "WARPED_HYPHAE", "STRIPPED_WARPED_HYPHAE" } };
            for (int i = 0; i < l0.GetLength(0); i++)
            {
                BlockId a = BlockRegistry.FromKey(l0[i, 0]), b = BlockRegistry.FromKey(l0[i, 1]);
                if (a != BlockId.Air && b != BlockId.Air) strip[(int)a] = b;
            }
            // main vD/b4: flower-pot contents, meta = index + 1.
            string[] vD = { "DANDELION", "POPPY", "FERN", "DEAD_BUSH", "ALLIUM", "AZURE_BLUET", "BLUE_ORCHID", "CORNFLOWER",
                "LILY_OF_THE_VALLEY", "OXEYE_DAISY", "ORANGE_TULIP", "PINK_TULIP", "RED_TULIP", "WHITE_TULIP", "WITHER_ROSE",
                "BROWN_MUSHROOM", "RED_MUSHROOM", "CRIMSON_ROOTS", "WARPED_ROOTS", "CRIMSON_FUNGUS", "WARPED_FUNGUS",
                "OAK_SAPLING", "SPRUCE_SAPLING", "BIRCH_SAPLING", "JUNGLE_SAPLING", "ACACIA_SAPLING", "DARK_OAK_SAPLING" };
            var pots = new BlockId[vD.Length];
            for (int i = 0; i < vD.Length; i++) pots[i] = BlockRegistry.FromKey(vD[i]);
            potPlants = pots; j5 = fl; stack = st; bedHead = head; bedFoot = foot; stripped = strip;
            concreteOf = conc; deadCoralOf = coral; leavesKey = lk;
        }

        // ---- world adapter -------------------------------------------------------------------
        public static VoxelWorld W;
        public static BlockId S(int x, int y, int z) => W.GetBlock(x, y, -z - 1);
        public static int RA(int x, int y, int z)
        {
            int uz = -z - 1; BlockId id = W.GetBlock(x, y, uz);
            return SourceCoords.UnityMetaToSource(id, W.GetMeta(x, y, uz));
        }
        /// <summary>main nA(x,y,z,id,true,meta): a persisted runtime edit (all et[] callbacks run).</summary>
        public static bool NA(int x, int y, int z, BlockId id, int meta = 0)
            => W.SetBlock(x, y, -z - 1, id, SourceCoords.SourceMetaToUnity(id, (byte)meta), true);
        public static Vector3Int ToUnity(int x, int y, int z) => new Vector3Int(x, y, -z - 1);

        // ---- block classes -------------------------------------------------------------------
        static bool F(BlockId id, uint f) => SourceBlockData.Has(id, f);
        public static bool Sn(BlockId id) => F(id, SourceBlockData.FWater);           // main Sn/EA
        public static bool EA(BlockId id) => F(id, SourceBlockData.FWater);
        public static bool Oe(BlockId id) => F(id, SourceBlockData.FLava);
        public static bool BA(BlockId id) => F(id, SourceBlockData.FCross);           // main bA (oo)
        public static bool Aquatic(BlockId id) => F(id, SourceBlockData.FAquatic);    // main NA (p0)
        public static bool NeedsWater(BlockId id) => F(id, SourceBlockData.FNeedsWater); // main y1
        public static bool Hang(BlockId id) => F(id, SourceBlockData.FHang);          // main OC
        public static bool Axis(BlockId id) => F(id, SourceBlockData.FAxis);          // main oi
        public static bool J1(BlockId id) => F(id, SourceBlockData.FShaped);          // main oA[id] defined
        public static bool IsBed(BlockId id) => F(id, SourceBlockData.FBed);          // main Bn
        public static bool Stack(BlockId id) { Init(); return stack[(int)id]; }       // main J0
        public static bool LeavesKey(BlockId id) { Init(); return leavesKey[(int)id]; } // main $E
        public static bool J5(BlockId id) { Init(); return j5[(int)id]; }
        public static bool GroundForFlowers(BlockId id)                                // main $5()
            => id == BlockId.Grass || id == BlockId.Dirt || id == BlockId.Podzol || id == BlockId.Farmland || id == BlockId.FarmlandMoist;
        /// <summary>main h1[foot] (Air when id is not a bed foot).</summary>
        public static BlockId BedHeadOf(BlockId id) { Init(); return bedHead[(int)id]; }
        /// <summary>main H1[head] (Air when id is not a bed head).</summary>
        public static BlockId BedFootOf(BlockId id) { Init(); return bedFoot[(int)id]; }
        public static BlockId StrippedOf(BlockId id) { Init(); return stripped[(int)id]; }  // main L0
        public static BlockId ConcreteOf(BlockId id) { Init(); return concreteOf[(int)id]; } // main _C
        public static BlockId DeadCoralOf(BlockId id) { Init(); return deadCoralOf[(int)id]; } // main YC
        public static BlockId Slab2(BlockId id) => (uint)id < (uint)SourceBlockData.Count ? (BlockId)SourceBlockData.BaseBlock[(int)id] : BlockId.Air; // main Te
        public static BlockShape Shape(BlockId id) => J1(id) ? (BlockShape)SourceBlockData.Shape[(int)id] : BlockShape.Cube;

        /// <summary>main aB(): flower-pot metadata for a held plant (0 = not pottable).</summary>
        public static int PotMetaFor(BlockId id)
        {
            Init(); if (id == BlockId.Air) return 0;
            for (int i = 0; i < potPlants.Length; i++) if (potPlants[i] == id) return i + 1;
            return 0;
        }
        /// <summary>main Ti(): the plant stored in flower-pot metadata.</summary>
        public static BlockId PotPlant(int meta)
        {
            Init(); int i = (meta & 255) - 1;
            return i >= 0 && i < potPlants.Length ? potPlants[i] : BlockId.Air;
        }

        // ---- main.js predicates (verbatim) ----------------------------------------------------
        /// <summary>main Fr(): the cell offers a full top face to stand things on.</summary>
        public static bool Fr(int x, int y, int z)
        {
            BlockId t = S(x, y, z);
            if (t == BlockId.Air || Sn(t) || BA(t)) return false;
            if (!J1(t)) return true;
            switch (Shape(t))
            {
                case BlockShape.Slab: return (RA(x, y, z) & 1) == 1;
                case BlockShape.Stairs: return ((RA(x, y, z) >> 2) & 1) == 1;
                case BlockShape.Fence: case BlockShape.Gate: case BlockShape.Wall: return true;
                default: return false;
            }
        }
        /// <summary>main lt(): a plain full block (side support for torches, ladders, vines).</summary>
        public static bool Lt(int x, int y, int z)
        {
            BlockId t = S(x, y, z);
            return t != BlockId.Air && !Sn(t) && !BA(t) && !J1(t);
        }
        /// <summary>main Ae(): solid plain block (not fluid, not cross, not shaped).</summary>
        public static bool Ae(BlockId t) => t != BlockId.Air && !EA(t) && !Oe(t) && !BA(t) && !J1(t);
        /// <summary>main aC().</summary>
        public static bool AC(BlockId t) => t != BlockId.Air && !EA(t) && !BA(t) && !J1(t);
        /// <summary>main Bi()/T4(): water or an aquatic block next to (or above) the cell.</summary>
        public static bool Bi(int x, int y, int z)
        {
            BlockId a;
            a = S(x, y + 1, z); if (Sn(a) || Aquatic(a)) return true;
            a = S(x + 1, y, z); if (Sn(a) || Aquatic(a)) return true;
            a = S(x - 1, y, z); if (Sn(a) || Aquatic(a)) return true;
            a = S(x, y, z + 1); if (Sn(a) || Aquatic(a)) return true;
            a = S(x, y, z - 1); return Sn(a) || Aquatic(a);
        }
        /// <summary>main Qo(): the block leaves water behind when removed.</summary>
        public static bool Qo(BlockId id, int meta, int x, int y, int z)
            => Aquatic(id) && (NeedsWater(id) || (meta & 4) != 0 || Bi(x, y, z));
        /// <summary>main v4(): the vine/lichen face mask that still has support.</summary>
        public static int V4(int m, int x, int y, int z)
        {
            int n = 0;
            if ((m & 1) != 0 && Lt(x, y, z + 1)) n |= 1;
            if ((m & 2) != 0 && Lt(x - 1, y, z)) n |= 2;
            if ((m & 4) != 0 && Lt(x, y, z - 1)) n |= 4;
            if ((m & 8) != 0 && Lt(x + 1, y, z)) n |= 8;
            if ((m & 16) != 0 && Lt(x, y + 1, z)) n |= 16;
            if ((m & 32) != 0 && Fr(x, y - 1, z)) n |= 32;
            return n;
        }

        /// <summary>main os(): true when the block at (x,y,z) with meta e has lost its support.</summary>
        public static bool Os(BlockId A, int e, int r, int t, int n)
        {
            if (Aquatic(A)) return Stack(A) ? !Fr(r, t - 1, n) && S(r, t - 1, n) != A : !Fr(r, t - 1, n);
            if (A == BlockId.SugarCane)
            {
                if (S(r, t - 1, n) == BlockId.SugarCane) return false;
                if (!Fr(r, t - 1, n)) return true;
                int a = t - 1;
                return !(WaterAt(r + 1, a, n) || WaterAt(r - 1, a, n) || WaterAt(r, a, n + 1) || WaterAt(r, a, n - 1));
            }
            if (BA(A))
            {
                if (Hang(A)) return !Lt(r, t + 1, n);
                BlockId u = S(r, t - 1, n);
                return Fr(r, t - 1, n) ? J5(A) && !GroundForFlowers(u) : !(Stack(A) && u == A);
            }
            BlockShape E = Shape(A);
            if (!J1(A)) E = BlockShape.Cube;
            switch (E)
            {
                case BlockShape.LilyPad:
                {
                    BlockId s = S(r, t - 1, n);
                    return !(Sn(s) || s == BlockId.Ice || s == BlockId.PackedIce || s == BlockId.BlueIce);
                }
                case BlockShape.SeaPickle: return !Fr(r, t - 1, n);
                case BlockShape.Bamboo: return !Fr(r, t - 1, n) && S(r, t - 1, n) != A;
                case BlockShape.FlatFaces: return V4(e, r, t, n) == 0;
                case BlockShape.TallPlant:
                    return ((e >> 2) & 1) != 0 ? S(r, t - 1, n) != A : !Fr(r, t - 1, n) || S(r, t + 1, n) != A;
            }
            // main q5(): torch / ladder / plate / button attachment cell.
            int cx, cy, cz; bool top;
            if (!Q5(A, e, r, t, n, out cx, out cy, out cz, out top)) return false;
            return top ? !Fr(cx, cy, cz) : !Lt(cx, cy, cz);
        }
        static bool WaterAt(int x, int y, int z)
        {
            BlockId l = S(x, y, z);
            return Sn(l) || Qo(l, RA(x, y, z), x, y, z);
        }
        static readonly int[,] TorchQ5 = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
        static bool Q5(BlockId A, int e, int r, int t, int n, out int cx, out int cy, out int cz, out bool top)
        {
            cx = r; cy = t; cz = n; top = false;
            if (!J1(A)) return false;
            switch (Shape(A))
            {
                case BlockShape.Torch:
                    if (e == 0) { cy = t - 1; top = true; return true; }
                    if (e < 1 || e > 4) return false;
                    cx = r + TorchQ5[e - 1, 0]; cz = n + TorchQ5[e - 1, 2]; return true;
                case BlockShape.Ladder:
                    cx = r + Ir[e & 3, 0]; cz = n + Ir[e & 3, 2]; return true;
                case BlockShape.Plate:
                    cy = t - 1; top = true; return true;
                case BlockShape.Button:
                    if (e == 0) { cy = t - 1; top = true; return true; }
                    if (e == 5) { cy = t + 1; return true; }
                    if (e < 1 || e > 4) return false;
                    cx = r + Ir[e - 1, 0]; cz = n + Ir[e - 1, 2]; return true;
            }
            return false;
        }

        /// <summary>main Ao(): the facing index of the player's yaw (source radians).</summary>
        public static int Ao(float yaw)
        {
            float A = Mathf.Sin(yaw), e = -Mathf.Cos(yaw);
            return Mathf.Abs(A) > Mathf.Abs(e) ? (A > 0f ? 3 : 1) : (e > 0f ? 0 : 2);
        }
        /// <summary>main Xw(): horizontal direction index of a face vector (-1 when vertical).</summary>
        public static int Xw(int fx, int fz) => fx > 0 ? 3 : fx < 0 ? 1 : fz > 0 ? 0 : fz < 0 ? 2 : -1;

        /// <summary>main UT(): the other half of a bed, if still present.</summary>
        public static bool BedOtherHalf(int x, int y, int z, BlockId t, out int ox, out int oz)
        {
            int n = (RA(x, y, z) >> 1) & 3;
            BlockId head = BedHeadOf(t);
            int u = head != BlockId.Air ? 1 : -1;
            ox = x + Ir[n, 0] * u; oz = z + Ir[n, 2] * u;
            BlockId C = head != BlockId.Air ? head : BedFootOf(t);
            return C != BlockId.Air && S(ox, y, oz) == C;
        }
    }
}
