// Voxel Forge — Unity port. Generation worker (GEN_WORKER_SOURCE): dense generation + saved delta overlay +
// fluid mixing + initial light + simulation/fluid-boundary seeds + section packing. Runs on background threads.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class GenJob { public int id, cx, cz, seed; public string key; public List<int[]> delta; }
    public sealed class GenSectionOut { public int si; public byte[] blocks, light; }
    public sealed class GenResult { public int id, cx, cz; public double ms; public List<GenSectionOut> sections; public List<GenMetaEntry> meta; public uint[] sim, fluidBoundary; }

    public sealed class GenWorker
    {
        const int CHUNK = 16, H = 384, SECTIONS = 24, N = CHUNK * H * CHUNK, SIM_BITS = 17;
        readonly WorldGenKernel K;
        readonly BlockDef[] defs;
        readonly byte[] simClass = new byte[256], fluidClass = new byte[256], fluidOpenClass = new byte[256];
        readonly bool[] stopsT = new bool[256], attenT = new bool[256];

        public GenWorker(int seed, BlockDef[] blockDefs)
        {
            defs = blockDefs;
            K = new WorldGenKernel(seed);
            initSimClass();
            for (int id = 0; id < 256; id++) { stopsT[id] = stops(id); attenT[id] = atten(id); }
        }
        static int idx(int x, int y, int z) { return (y * CHUNK + z) * CHUNK + x; }
        bool stops(int id) { var b = id < defs.Length ? defs[id] : null; return id != B.AIR && b != null && b.solid && !b.transparent && !b.cutout && !b.plant && !b.waterPlant; }
        bool atten(int id)
        {
            var b = id < defs.Length ? defs[id] : null;
            if (b == null || id == B.AIR) return false;
            return id == B.WATER || id == B.WATER_FALLING || id == B.FLOW7 || id == B.FLOW6 || id == B.FLOW5 || id == B.FLOW4 || id == B.FLOW3 || id == B.FLOW2 || id == B.FLOW1 ||
                b.waterPlant || id == B.LEAVES || id == B.BIRCH_LEAVES || id == B.SPRUCE_LEAVES || id == B.DARK_LEAVES || id == B.JUNGLE_LEAVES || id == B.ACACIA_LEAVES;
        }
        void initSimClass()
        {
            simClass[B.FARMLAND] = 3; simClass[B.FARMLAND_MOIST] = 3;
            foreach (var id in new[] { B.WHEAT0, B.WHEAT1, B.WHEAT2, B.WHEAT3, B.CARROTS0, B.CARROTS1, B.CARROTS2, B.CARROTS3, B.POTATOES0, B.POTATOES1, B.POTATOES2, B.POTATOES3,
                B.PUMPKIN_STEM0, B.PUMPKIN_STEM1, B.PUMPKIN_STEM2, B.PUMPKIN_STEM3, B.MELON_STEM0, B.MELON_STEM1, B.MELON_STEM2, B.MELON_STEM3 }) simClass[id] = 4;
            simClass[B.SAPLING] = 5; simClass[B.CACTUS] = 6; simClass[B.FIRE] = 7;
            foreach (var id in new[] { B.WATER, B.WATER_FALLING, B.FLOW7, B.FLOW6, B.FLOW5, B.FLOW4, B.FLOW3, B.FLOW2, B.FLOW1 }) fluidClass[id] = 1;
            foreach (var id in new[] { B.LAVA, B.LAVA_FLOW2, B.LAVA_FLOW1 }) fluidClass[id] = 2;
            fluidOpenClass[B.AIR] = 1;
            for (int id = 0; id < defs.Length && id < 256; id++)
            {
                var b = defs[id];
                if (b != null && b.waterPlant && b.needsWater) fluidClass[id] = 1;
                if (b != null && b.plant && !b.waterPlant && b.special == null) fluidOpenClass[id] = 1;
            }
        }
        int fluidKind(int id) { return fluidClass[id]; }
        bool fluidOpen(int id) { return fluidOpenClass[id] != 0; }
        bool denseWater(int id) { return fluidKind(id) == 1; }
        void mixDenseFluids(byte[] data)
        {
            const int S = CHUNK * CHUNK;
            for (int y = 0; y < H; y++)
                for (int z = 0; z < CHUNK; z++)
                    for (int x = 0; x < CHUNK; x++)
                    {
                        int p = idx(x, y, z), id = data[p], lev = id == B.LAVA ? 3 : id == B.LAVA_FLOW2 ? 2 : id == B.LAVA_FLOW1 ? 1 : 0;
                        if (lev == 0) continue;
                        bool st = false;
                        if (x > 0 && denseWater(data[p - 1])) st = true;
                        else if (x + 1 < CHUNK && denseWater(data[p + 1])) st = true;
                        else if (z > 0 && denseWater(data[p - CHUNK])) st = true;
                        else if (z + 1 < CHUNK && denseWater(data[p + CHUNK])) st = true;
                        else if (y + 1 < H && denseWater(data[p + S])) st = true;
                        if (st) { data[p] = (byte)(id == B.LAVA ? B.OBSIDIAN : B.COBBLE); continue; }
                        if (y > 0 && denseWater(data[p - S])) data[p - S] = (byte)B.STONE;
                    }
        }
        void lightDense(byte[] data, List<GenMetaEntry> meta, out byte[] light, out List<uint> sim, out List<uint> boundary)
        {
            var sky = new byte[N]; var blk = new byte[N]; var q = new List<int>(4096); var extra = new byte[N];
            sim = new List<uint>(); boundary = new List<uint>();
            if (meta != null)
                foreach (var m in meta)
                    if (m.m != null && m.m.Bool("berries")) { int x = m.lx, y = m.iy, z = m.lz; if (x >= 0 && x < CHUNK && y >= 0 && y < H && z >= 0 && z < CHUNK) extra[idx(x, y, z)] = 14; }
            const int S = CHUNK * CHUNK;
            for (int z = 0; z < CHUNK; z++)
                for (int x = 0; x < CHUNK; x++)
                {
                    int sv = 15;
                    for (int y = H - 1; y >= 0; y--)
                    {
                        int p = idx(x, y, z), id = data[p]; var b = defs[id];
                        if (b != null && b.light != 0) blk[p] = (byte)Math.Min(15, b.light);
                        if (extra[p] > blk[p]) blk[p] = extra[p];
                        int fk = fluidKind(id);
                        if (fk != 0)
                        {
                            bool active = false; int nk;
                            if (fk == 1 && id == B.WATER)
                            {
                                bool aboveWater = y + 1 < H && fluidKind(data[p + S]) == 1, sideOpen = false, otherFluid = false, belowOpen = false;
                                if (y > 0) { int n = data[p - S]; nk = fluidKind(n); belowOpen = fluidOpen(n); otherFluid = nk != 0 && nk != fk; }
                                if (x > 0) { int n = data[p - 1]; nk = fluidKind(n); sideOpen = sideOpen || fluidOpen(n); otherFluid = otherFluid || (nk != 0 && nk != fk); }
                                if (x + 1 < CHUNK) { int n = data[p + 1]; nk = fluidKind(n); sideOpen = sideOpen || fluidOpen(n); otherFluid = otherFluid || (nk != 0 && nk != fk); }
                                if (z > 0) { int n = data[p - CHUNK]; nk = fluidKind(n); sideOpen = sideOpen || fluidOpen(n); otherFluid = otherFluid || (nk != 0 && nk != fk); }
                                if (z + 1 < CHUNK) { int n = data[p + CHUNK]; nk = fluidKind(n); sideOpen = sideOpen || fluidOpen(n); otherFluid = otherFluid || (nk != 0 && nk != fk); }
                                if (y + 1 < H) { nk = fluidKind(data[p + S]); otherFluid = otherFluid || (nk != 0 && nk != fk); }
                                active = belowOpen || otherFluid || (!aboveWater && sideOpen);
                            }
                            else
                            {
                                if (y > 0) { int n = data[p - S]; nk = fluidKind(n); active = fluidOpen(n) || (nk != 0 && nk != fk); }
                                if (!active && x > 0) { int n = data[p - 1]; nk = fluidKind(n); active = fluidOpen(n) || (nk != 0 && nk != fk); }
                                if (!active && x + 1 < CHUNK) { int n = data[p + 1]; nk = fluidKind(n); active = fluidOpen(n) || (nk != 0 && nk != fk); }
                                if (!active && z > 0) { int n = data[p - CHUNK]; nk = fluidKind(n); active = fluidOpen(n) || (nk != 0 && nk != fk); }
                                if (!active && z + 1 < CHUNK) { int n = data[p + CHUNK]; nk = fluidKind(n); active = fluidOpen(n) || (nk != 0 && nk != fk); }
                                if (!active && y + 1 < H) { nk = fluidKind(data[p + S]); active = nk != 0 && nk != fk; }
                            }
                            if (active || (fk == 1 && (id == B.WATER_FALLING || id == B.FLOW7 || id == B.FLOW6 || id == B.FLOW5 || id == B.FLOW4 || id == B.FLOW3 || id == B.FLOW2 || id == B.FLOW1)))
                                sim.Add((uint)((fk << SIM_BITS) | p));
                            if (x == 0 || x == CHUNK - 1 || z == 0 || z == CHUNK - 1) boundary.Add((uint)((fk << SIM_BITS) | p));
                        }
                        int sc = simClass[id];
                        if (sc != 0) { if (sc != 6 || y == 0 || data[p - S] != B.CACTUS) sim.Add((uint)((sc << SIM_BITS) | p)); }
                        if (stopsT[id]) { sv = 0; continue; }
                        if (sv > 0 && attenT[id]) sv = Math.Max(0, sv - 2);
                        if (sv > 0) { sky[p] = (byte)sv; q.Add(p); }
                    }
                }
            Action<byte[]> flood = a =>
            {
                int h = 0;
                while (h < q.Count)
                {
                    int p = q[h++], lv = a[p];
                    if (lv <= 1) continue;
                    int x = p % CHUNK, z = (p / CHUNK) % CHUNK, y = p / (CHUNK * CHUNK);
                    for (int k = 0; k < 6; k++)
                    {
                        int n;
                        if (k == 0) { if (x + 1 >= CHUNK) continue; n = p + 1; }
                        else if (k == 1) { if (x <= 0) continue; n = p - 1; }
                        else if (k == 2) { if (z + 1 >= CHUNK) continue; n = p + CHUNK; }
                        else if (k == 3) { if (z <= 0) continue; n = p - CHUNK; }
                        else if (k == 4) { if (y + 1 >= H) continue; n = p + CHUNK * CHUNK; }
                        else { if (y <= 0) continue; n = p - CHUNK * CHUNK; }
                        int id = data[n];
                        if (stopsT[id]) continue;
                        int nv = lv - (attenT[id] ? 2 : 1);
                        if (nv > a[n]) { a[n] = (byte)nv; q.Add(n); }
                    }
                }
            };
            flood(sky);
            q.Clear();
            for (int p = 0; p < N; p++) if (blk[p] != 0) q.Add(p);
            flood(blk);
            light = new byte[N];
            for (int p = 0; p < N; p++) light[p] = (byte)((sky[p] << 4) | blk[p]);
        }
        static List<GenSectionOut> pack(byte[] data, byte[] light)
        {
            var o = new List<GenSectionOut>();
            for (int si = 0; si < SECTIONS; si++)
            {
                int off = si * 4096; bool used = false;
                for (int i = 0; i < 4096; i++) if (data[off + i] != B.AIR || light[off + i] != 240) { used = true; break; }
                if (used)
                {
                    var b = new byte[4096]; var l = new byte[4096];
                    Buffer.BlockCopy(data, off, b, 0, 4096); Buffer.BlockCopy(light, off, l, 0, 4096);
                    o.Add(new GenSectionOut { si = si, blocks = b, light = l });
                }
            }
            return o;
        }
        public static byte[] applyChunkDeltaDense(DenseChunk dense, int cx, int cz, List<int[]> delta)
        {
            var data = dense.data;
            if (delta == null || delta.Count == 0) return data;
            int x0 = cx * CHUNK, z0 = cz * CHUNK, stride = CHUNK * CHUNK; bool changed = false;
            foreach (var e in delta)
            {
                if (e == null || e.Length < 4) continue;
                int x = e[0], y = e[1], z = e[2];
                if (y < VF.WORLD_MIN_Y || y >= VF.WORLD_MAX_Y) continue;
                int lx = x - x0, lz = z - z0;
                if (lx < 0 || lx >= CHUNK || lz < 0 || lz >= CHUNK) continue;
                data[(y - VF.WORLD_MIN_Y) * stride + lz * CHUNK + lx] = (byte)e[3];
                changed = true;
            }
            if (changed && dense.meta != null) dense.meta.dirty = true;
            return data;
        }
        public GenResult Run(GenJob m)
        {
            K.setSeed(m.seed);
            double t0 = JS.now();
            var dense = K.generateDense(m.cx, m.cz);
            var data = applyChunkDeltaDense(dense, m.cx, m.cz, m.delta);
            mixDenseFluids(data);
            var meta = K.structureMetaForChunk(m.cx, m.cz);
            meta.AddRange(K.seaPickleMetaForDense(data, dense.meta, m.cx, m.cz));
            meta.AddRange(K.caveVineMetaForDense(data, dense.meta, m.cx, m.cz));
            byte[] light; List<uint> sim, boundary;
            lightDense(data, meta, out light, out sim, out boundary);
            return new GenResult { id = m.id, cx = m.cx, cz = m.cz, ms = JS.now() - t0, sections = pack(data, light), meta = meta, sim = sim.ToArray(), fluidBoundary = boundary.ToArray() };
        }
    }
}
