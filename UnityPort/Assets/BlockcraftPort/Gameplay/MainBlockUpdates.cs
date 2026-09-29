using System.Collections.Generic;
using System.Threading;
using static BlockcraftPort.SourceBlockRules;

namespace BlockcraftPort
{
    /// <summary>
    /// main.js et[] edit callbacks that change neighbouring blocks after every runtime edit (nA):
    ///   Qu  - blocks that lost their support pop off (torches, plants, doors, vines, ...);
    ///   Ai  - concrete powder hardens next to water;
    ///   ei  - coral blocks/fans die without adjacent water;
    ///   ri  - cactus breaks without sand/cactus below or with a solid block beside it;
    ///   DI  - sponge absorbs water (on placement, or when water reaches it).
    /// main.js runs them synchronously and recursively inside nA(). Here edits are queued and
    /// drained breadth-first right after the outermost VoxelWorld.SetBlock (and once per frame
    /// for simulation-committed fluid edits), which reaches the same final state.
    /// </summary>
    public static class MainBlockUpdates
    {
        struct Edit { public int X, Y, Z; public BlockId NewId; }
        static readonly Queue<Edit> pending = new Queue<Edit>(64);
        static bool draining, spongeBusy;
        static int mainThreadId = -1;
        const int MaxEditsPerDrain = 16384;

        /// <summary>Called by VoxelWorld on the main thread at startup.</summary>
        public static void BindMainThread() => mainThreadId = Thread.CurrentThread.ManagedThreadId;

        /// <summary>Records a runtime edit (Unity coordinates).</summary>
        public static void Enqueue(int ux, int y, int uz, BlockId newId)
        {
            if (mainThreadId != Thread.CurrentThread.ManagedThreadId) return;
            pending.Enqueue(new Edit { X = ux, Y = y, Z = -uz - 1, NewId = newId });
        }

        /// <summary>Runs queued callbacks. Re-entrant calls (edits made by a callback) just enqueue.</summary>
        public static void Drain(VoxelWorld world)
        {
            if (draining || pending.Count == 0 || world == null) return;
            if (mainThreadId != Thread.CurrentThread.ManagedThreadId) return;
            draining = true;
            try
            {
                int budget = MaxEditsPerDrain;
                while (pending.Count > 0 && budget-- > 0)
                {
                    Edit e = pending.Dequeue();
                    W = world;
                    Run(e.X, e.Y, e.Z, e.NewId);
                }
            }
            finally { draining = false; }
        }

        static void Run(int A, int e, int r, BlockId t)
        {
            // DI(): sponge placement / water arriving next to a sponge.
            if (!spongeBusy)
            {
                if (t == BlockId.Sponge) Absorb(A, e, r);
                else if (EA(t))
                    for (int n = 0; n < 6; n++)
                        if (S(A + Zn[n, 0], e + Zn[n, 1], r + Zn[n, 2]) == BlockId.Sponge) Absorb(A + Zn[n, 0], e + Zn[n, 1], r + Zn[n, 2]);
            }
            // Qu() on the six neighbours.
            Qu(A, e + 1, r); Qu(A, e - 1, r); Qu(A - 1, e, r); Qu(A + 1, e, r); Qu(A, e, r - 1); Qu(A, e, r + 1);
            // Ai() self + six.
            Ai(A, e, r); Ai(A + 1, e, r); Ai(A - 1, e, r); Ai(A, e + 1, r); Ai(A, e - 1, r); Ai(A, e, r + 1); Ai(A, e, r - 1);
            // ei() self + six, then ri() self + six.
            Ei(A, e, r); Ei(A + 1, e, r); Ei(A - 1, e, r); Ei(A, e + 1, r); Ei(A, e - 1, r); Ei(A, e, r + 1); Ei(A, e, r - 1);
            Ri(A, e, r); Ri(A + 1, e, r); Ri(A - 1, e, r); Ri(A, e, r + 1); Ri(A, e, r - 1); Ri(A, e + 1, r); Ri(A, e - 1, r);
        }

        static readonly int[,] Zn = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };

        /// <summary>main Qu().</summary>
        static void Qu(int A, int e, int r)
        {
            if (e < K || e >= VA) return;
            BlockId t = S(A, e, r);
            if (t == BlockId.Air) return;
            if (J1(t) && Shape(t) == BlockShape.FlatFaces)
            {
                int n = RA(A, e, r), a = V4(n, A, e, r);
                if (a == 0) NA(A, e, r, BlockId.Air);
                else if (a != n) NA(A, e, r, t, a);
                return;
            }
            int meta = RA(A, e, r);
            if (!Os(t, meta, A, e, r)) return;
            // L1(): drops r2(t).
            MainBlockDrops.Spawn(A + .5f, e + .5f, -(r + .5f), t);
            if (J1(t) && (Shape(t) == BlockShape.Door || Shape(t) == BlockShape.TallPlant))
            {
                int u = ((meta >> 2) & 1) != 0 ? e - 1 : e + 1;
                if (S(A, u, r) == t) NA(A, u, r, BlockId.Air);
            }
            NA(A, e, r, Qo(t, RA(A, e, r), A, e, r) ? BlockId.Water : BlockId.Air);
        }

        /// <summary>main Ai(): concrete powder touching water becomes concrete.</summary>
        static void Ai(int A, int e, int r)
        {
            BlockId t = ConcreteOf(S(A, e, r));
            if (t == BlockId.Air) return;
            if (EA(S(A + 1, e, r)) || EA(S(A - 1, e, r)) || EA(S(A, e + 1, r)) || EA(S(A, e - 1, r)) || EA(S(A, e, r + 1)) || EA(S(A, e, r - 1)))
                NA(A, e, r, t);
        }

        /// <summary>main ei(): living coral without water or an aquatic neighbour dies.</summary>
        static void Ei(int A, int e, int r)
        {
            BlockId n = DeadCoralOf(S(A, e, r));
            if (n == BlockId.Air) return;
            for (int a = 0; a < 6; a++)
            {
                BlockId u = S(A + Zn[a, 0], e + Zn[a, 1], r + Zn[a, 2]);
                if (EA(u) || Aquatic(u)) return;
            }
            NA(A, e, r, n, RA(A, e, r));
        }

        /// <summary>main ri(): cactus survival rule (no drop, like main).</summary>
        static void Ri(int A, int e, int r)
        {
            if (S(A, e, r) != BlockId.Cactus) return;
            BlockId t = S(A, e - 1, r);
            if (!(t == BlockId.Sand || t == BlockId.RedSand || t == BlockId.Cactus) ||
                Ae(S(A + 1, e, r)) || Ae(S(A - 1, e, r)) || Ae(S(A, e, r + 1)) || Ae(S(A, e, r - 1)))
                NA(A, e, r, BlockId.Air);
        }

        const int SpongeMaxDepth = 7, SpongeMaxBlocks = 65; // main vg, II

        /// <summary>main vI(): breadth-first water removal around a sponge, then SPONGE → WET_SPONGE.</summary>
        static void Absorb(int A, int e, int r)
        {
            if (spongeBusy || S(A, e, r) != BlockId.Sponge) return;
            spongeBusy = true;
            try
            {
                var seen = new HashSet<long>();
                var queue = new Queue<(int x, int y, int z, int d)>();
                var hits = new List<(int x, int y, int z)>();
                void C(int f, int w, int l, int P)
                {
                    long key = ((long)(f & 0x3FFFFF) << 42) | ((long)((w + 1024) & 0xFFFFF) << 22) | (uint)(l & 0x3FFFFF);
                    if (!seen.Add(key)) return;
                    if (EA(S(f, w, l))) { hits.Add((f, w, l)); queue.Enqueue((f, w, l, P)); }
                }
                for (int i = 0; i < 6; i++) C(A + Zn[i, 0], e + Zn[i, 1], r + Zn[i, 2], 1);
                while (queue.Count > 0 && hits.Count < SpongeMaxBlocks)
                {
                    var u = queue.Dequeue();
                    if (u.d >= SpongeMaxDepth) continue;
                    for (int E = 0; E < 6 && hits.Count < SpongeMaxBlocks; E++) C(u.x + Zn[E, 0], u.y + Zn[E, 1], u.z + Zn[E, 2], u.d + 1);
                }
                if (hits.Count == 0) return;
                NA(A, e, r, BlockId.WetSponge);
                for (int s = 0; s < hits.Count; s++) NA(hits[s].x, hits[s].y, hits[s].z, BlockId.Air);
            }
            finally { spongeBusy = false; }
        }
    }
}
