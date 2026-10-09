// Voxel Forge — Unity port. main-inspired persistent packed voxel light.
// Each sparse 16^3 section owns one byte per voxel: high nibble = sky, low nibble = block.
// Meshing only reads these bytes; lighting is generated once and incrementally repaired after edits.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public static partial class VF
    {
        static readonly int[][] LIGHT_DIRS = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        static readonly List<int[]> lightDirtyQueue = new List<int[]>();
        public static readonly HashSet<string> lightDirtyKeys = new HashSet<string>();
        static int lightDirtyHead = 0;
        public static double avgLightRepairMs = 0.35;

        public static Chunk chunkForWorld(int x, int z) { return chunkFastGet(x >> 4, z >> 4); }
        public static bool lightCellLoaded(int x, int z) { return chunkForWorld(x, z) != null; }
        public static int getPackedLightWorld(int x, int y, int z)
        {
            if (y >= WORLD_MAX_Y) return 0xf0;
            if (y < WORLD_MIN_Y) return 0;
            var c = chunkForWorld(x, z);
            if (c == null) return 0xf0;
            var sec = c.sections[sectionSlot(y)];
            if (sec == null) return 0xf0;
            var light = ensureSectionLight(sec);
            return light[sectionIndex(((x % CHUNK) + CHUNK) % CHUNK, y, ((z % CHUNK) + CHUNK) % CHUNK)];
        }
        static void noteLightDirty(Dictionary<string, HashSet<int>> dirty, int cx, int cz, int si)
        {
            if (dirty == null) return;
            var k = ckey(cx, cz); HashSet<int> a;
            if (!dirty.TryGetValue(k, out a)) dirty[k] = a = new HashSet<int>();
            if (si >= 0 && si < SECTION_COUNT) a.Add(si);
        }
        public static bool setPackedLightWorld(int x, int y, int z, int value, Dictionary<string, HashSet<int>> dirty)
        {
            value &= 255;
            if (!yInWorld(y)) return false;
            int cx = x >> 4, cz = z >> 4; var c = chunkFastGet(cx, cz);
            if (c == null) return false;
            int si = sectionSlot(y), lx = x - cx * CHUNK, lz = z - cz * CHUNK, ly = (y - WORLD_MIN_Y) & 15, ii = sectionIndex(lx, y, lz);
            var sec = c.sections[si];
            if (sec == null) { if (value == 0xf0) return false; sec = newSection(); c.sections[si] = sec; }
            else ensureSectionLight(sec);
            if (sec.light[ii] == value) return false;
            sec.light[ii] = (byte)value;
            if (dirty != null)
            {
                noteLightDirty(dirty, cx, cz, si);
                if (ly == 0) noteLightDirty(dirty, cx, cz, si - 1);
                if (ly == 15) noteLightDirty(dirty, cx, cz, si + 1);
                if (lx == 0) noteLightDirty(dirty, cx - 1, cz, si);
                if (lx == 15) noteLightDirty(dirty, cx + 1, cz, si);
                if (lz == 0) noteLightDirty(dirty, cx, cz - 1, si);
                if (lz == 15) noteLightDirty(dirty, cx, cz + 1, si);
            }
            return true;
        }
        public static int skyAt(int x, int y, int z) { return getPackedLightWorld(x, y, z) >> 4; }
        public static int blockAtLight(int x, int y, int z) { return getPackedLightWorld(x, y, z) & 15; }
        static bool setSkyAt(int x, int y, int z, int lv, Dictionary<string, HashSet<int>> dirty) { int p = getPackedLightWorld(x, y, z); return setPackedLightWorld(x, y, z, (p & 15) | ((lv & 15) << 4), dirty); }
        static bool setBlockAtLight(int x, int y, int z, int lv, Dictionary<string, HashSet<int>> dirty) { int p = getPackedLightWorld(x, y, z); return setPackedLightWorld(x, y, z, (p & 0xf0) | (lv & 15), dirty); }
        public static int lightCost(int id) { return lightAttenuation(id) ? 2 : 1; }
        public static int blockEmissionAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z); var m = getBlockMeta(x, y, z);
            var v = isVirtualId(id) ? virtualDefFromMeta(m) : null;
            if (id == B.CAVE_VINES && m != null && m.Bool("berries")) return 14;
            int light = v != null ? v.light : (bdef(id) != null ? bdef(id).light : 0);
            return light != 0 ? Math.Min(15, light) : 0;
        }
        public static void queueLightUpdate(int x, int y, int z)
        {
            var k = key3(x, y, z);
            if (lightDirtyKeys.Contains(k)) return;
            lightDirtyKeys.Add(k);
            lightDirtyQueue.Add(new[] { x, y, z });
        }
        public static void markLightDirtyChunks(Dictionary<string, HashSet<int>> dirty)
        {
            foreach (var kv in dirty)
            {
                var c = chunks.Get(kv.Key);
                if (c == null) continue;
                c.dirty = true; c.meshRev++;
                if (c.sectionGeo == null || c.fullMeshDirty) c.fullMeshDirty = true;
                else
                {
                    var ds = c.dirtySections ?? (c.dirtySections = new HashSet<int>());
                    foreach (var si in kv.Value) for (int q = si - 1; q <= si + 1; q++) if (q >= 0 && q < SECTION_COUNT) ds.Add(q);
                }
            }
        }
        static byte[] directSkyColumn(int x, int z, Dictionary<long, byte[]> cache)
        {
            long key = ((long)x << 32) ^ (uint)z;
            byte[] a;
            if (cache != null && cache.TryGetValue(key, out a)) return a;
            a = new byte[WORLD_H];
            int sv = 15;
            for (int iy = WORLD_H - 1; iy >= 0; iy--)
            {
                int id = getBlock(x, WORLD_MIN_Y + iy, z);
                if (lightStopsState(id, getBlockMeta(x, WORLD_MIN_Y + iy, z))) sv = 0;
                else if (sv > 0 && lightAttenuationState(id, getBlockMeta(x, WORLD_MIN_Y + iy, z))) sv = Math.Max(0, sv - 2);
                a[iy] = (byte)sv;
            }
            if (cache != null) cache[key] = a;
            return a;
        }
        static long encodeLightPos(int x, int y, int z) { return (((long)(x + 65536)) * 131072 + (z + 65536)) * WORLD_H + (y - WORLD_MIN_Y); }
        static void seedLight(List<long> q, int x, int y, int z) { q.Add(encodeLightPos(x, y, z)); }
        static void propagateLightIncrease(List<long> seeds, bool skyMode, Dictionary<string, HashSet<int>> dirty)
        {
            int h = 0;
            while (h < seeds.Count)
            {
                long code = seeds[h++];
                int iy = (int)(code % WORLD_H); long n = (code - iy) / WORLD_H; int zm = (int)(n % 131072);
                int x = (int)((n - zm) / 131072) - 65536, y = iy + WORLD_MIN_Y, z = zm - 65536;
                if (!yInWorld(y) || !lightCellLoaded(x, z)) continue;
                int p = getPackedLightWorld(x, y, z), lv = skyMode ? p >> 4 : p & 15;
                if (lv <= 1) continue;
                int source = getBlock(x, y, z); var sm = getBlockMeta(x, y, z);
                if (lightStopsState(source, sm) && !(!skyMode && blockEmissionAt(x, y, z) > 0)) continue;
                foreach (var d in LIGHT_DIRS)
                {
                    int nx = x + d[0], ny = y + d[1], nz = z + d[2];
                    if (!yInWorld(ny) || !lightCellLoaded(nx, nz)) continue;
                    int id = getBlock(nx, ny, nz); var tm = getBlockMeta(nx, ny, nz);
                    if (lightStopsState(id, tm)) continue;
                    int nv = lv - (lightAttenuationState(id, tm) ? 2 : 1);
                    if (nv <= 0) continue;
                    int op = getPackedLightWorld(nx, ny, nz), old = skyMode ? op >> 4 : op & 15;
                    if (nv > old)
                    {
                        if (skyMode) setSkyAt(nx, ny, nz, nv, dirty); else setBlockAtLight(nx, ny, nz, nv, dirty);
                        if (nv > 1) seedLight(seeds, nx, ny, nz);
                    }
                }
            }
        }
        public static void updateBlockLightAt(int x, int y, int z, Dictionary<string, HashSet<int>> dirty)
        {
            int old = blockAtLight(x, y, z), emit = blockEmissionAt(x, y, z);
            var remove = new List<int[]>(); var inc = new List<long>();
            setBlockAtLight(x, y, z, 0, dirty);
            if (old > 0) remove.Add(new[] { x, y, z, old });
            int h = 0;
            while (h < remove.Count)
            {
                var q = remove[h++]; int px = q[0], py = q[1], pz = q[2], pl = q[3];
                foreach (var d in LIGHT_DIRS)
                {
                    int nx = px + d[0], ny = py + d[1], nz = pz + d[2];
                    if (!yInWorld(ny) || !lightCellLoaded(nx, nz)) continue;
                    int nl = blockAtLight(nx, ny, nz);
                    if (nl == 0) continue;
                    if (nl < pl)
                    {
                        int bs = blockEmissionAt(nx, ny, nz);
                        if (bs >= nl) seedLight(inc, nx, ny, nz);
                        else
                        {
                            setBlockAtLight(nx, ny, nz, bs, dirty);
                            remove.Add(new[] { nx, ny, nz, nl });
                            if (bs > 1) seedLight(inc, nx, ny, nz);
                        }
                    }
                    else seedLight(inc, nx, ny, nz);
                }
            }
            if (emit > 0) { setBlockAtLight(x, y, z, emit, dirty); seedLight(inc, x, y, z); }
            foreach (var d in LIGHT_DIRS)
            {
                int nx = x + d[0], ny = y + d[1], nz = z + d[2];
                if (yInWorld(ny) && lightCellLoaded(nx, nz) && blockAtLight(nx, ny, nz) > 1) seedLight(inc, nx, ny, nz);
            }
            propagateLightIncrease(inc, false, dirty);
        }
        public static void updateSkyLightAt(int x, int y, int z, Dictionary<string, HashSet<int>> dirty)
        {
            var cache = new Dictionary<long, byte[]>();
            var base0 = directSkyColumn(x, z, cache);
            var remove = new List<int[]>(); var inc = new List<long>();
            for (int iy = 0; iy < WORLD_H; iy++)
            {
                int wy = WORLD_MIN_Y + iy, old = skyAt(x, wy, z), bs = base0[iy];
                if (old > bs) { setSkyAt(x, wy, z, bs, dirty); remove.Add(new[] { x, wy, z, old }); if (bs > 1) seedLight(inc, x, wy, z); }
                else if (bs > old) { setSkyAt(x, wy, z, bs, dirty); seedLight(inc, x, wy, z); }
            }
            int h = 0;
            while (h < remove.Count)
            {
                var q = remove[h++]; int px = q[0], py = q[1], pz = q[2], pl = q[3];
                foreach (var d in LIGHT_DIRS)
                {
                    int nx = px + d[0], ny = py + d[1], nz = pz + d[2];
                    if (!yInWorld(ny) || !lightCellLoaded(nx, nz)) continue;
                    int nl = skyAt(nx, ny, nz);
                    if (nl == 0) continue;
                    if (nl < pl)
                    {
                        int bs = directSkyColumn(nx, nz, cache)[ny - WORLD_MIN_Y];
                        if (nl > bs) { setSkyAt(nx, ny, nz, bs, dirty); remove.Add(new[] { nx, ny, nz, nl }); if (bs > 1) seedLight(inc, nx, ny, nz); }
                        else seedLight(inc, nx, ny, nz);
                    }
                    else seedLight(inc, nx, ny, nz);
                }
            }
            foreach (var d in LIGHT_DIRS)
            {
                int nx = x + d[0], ny = y + d[1], nz = z + d[2];
                if (yInWorld(ny) && lightCellLoaded(nx, nz) && skyAt(nx, ny, nz) > 1) seedLight(inc, nx, ny, nz);
            }
            propagateLightIncrease(inc, true, dirty);
        }
        public static void repairLightAt(int x, int y, int z)
        {
            if (!lightCellLoaded(x, z)) return;
            var dirty = new Dictionary<string, HashSet<int>>();
            updateBlockLightAt(x, y, z, dirty);
            updateSkyLightAt(x, y, z, dirty);
            markLightDirtyChunks(dirty);
        }
        public static int lightDirtyCount() { return lightDirtyQueue.Count - lightDirtyHead; }
        static int[] takeLightDirty()
        {
            if (lightDirtyHead >= lightDirtyQueue.Count) return null;
            var p = lightDirtyQueue[lightDirtyHead];
            lightDirtyQueue[lightDirtyHead++] = null;
            if (lightDirtyHead == lightDirtyQueue.Count) { lightDirtyQueue.Clear(); lightDirtyHead = 0; }
            else if (lightDirtyHead >= 64 && lightDirtyHead * 2 >= lightDirtyQueue.Count) { lightDirtyQueue.RemoveRange(0, lightDirtyHead); lightDirtyHead = 0; }
            return p;
        }
        public static int processLightDirty(double deadline)
        {
            // main G4 scheduling semantics: do not begin a lighting batch unless at least ~2 ms of the deadline remains.
            if (lightDirtyCount() == 0) return 0;
            bool timed = !double.IsInfinity(deadline) && !double.IsNaN(deadline);
            if (timed && JS.now() > deadline - 2) return 0;
            var batch = new List<int[]>();
            for (int[] p; (p = takeLightDirty()) != null;) batch.Add(p);
            if (batch.Count == 0) return 0;
            var dirty = new Dictionary<string, HashSet<int>>(); var skyDone = new HashSet<long>();
            int done = 0, i = 0; double tBatch = JS.now();
            for (; i < batch.Count; i++)
            {
                var p = batch[i]; int x = p[0], y = p[1], z = p[2]; var lk = key3(x, y, z);
                if (!lightDirtyKeys.Contains(lk)) { done++; continue; }
                lightDirtyKeys.Remove(lk);
                if (lightCellLoaded(x, z))
                {
                    updateBlockLightAt(x, y, z, dirty);
                    // Repeated edits in the same vertical column need one final skylight solve.
                    long col = ((long)x << 32) ^ (uint)z;
                    if (skyDone.Add(col)) updateSkyLightAt(x, y, z, dirty);
                }
                done++;
                if (timed && i + 1 < batch.Count && JS.now() > deadline) { i++; break; }
            }
            markLightDirtyChunks(dirty);
            double cost = JS.now() - tBatch;
            if (done > 0) avgLightRepairMs += (cost / done - avgLightRepairMs) * 0.2;
            for (; i < batch.Count; i++) lightDirtyQueue.Add(batch[i]);
            return done;
        }
        static int lightNeighborhoodTopY(int cx, int cz)
        {
            int hi = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = chunkFastGet(cx + dx, cz + dz);
                    if (n == null) continue;
                    for (int si = SECTION_COUNT - 1; si > hi; si--) if (n.sections[si] != null) { hi = si; break; }
                }
            return Math.Min(WORLD_MAX_Y - 1, WORLD_MIN_Y + (hi + 1) * 16);
        }
        static readonly int[] lightStitchQueue = new int[CHUNK * WORLD_H * CHUNK];
        static int chunkPackedLocal(Chunk c, int lx, int y, int lz)
        {
            if (!yInWorld(y)) return y >= WORLD_MAX_Y ? 0xf0 : 0;
            var sec = c.sections[sectionSlot(y)];
            if (sec == null) return 0xf0;
            return ensureSectionLight(sec)[sectionIndex(lx, y, lz)];
        }
        static bool setChunkPackedLocal(Chunk c, int lx, int y, int lz, int value)
        {
            int si = sectionSlot(y), ii = sectionIndex(lx, y, lz);
            var sec = c.sections[si];
            if (sec == null) { if ((value & 255) == 0xf0) return false; sec = newSection(); c.sections[si] = sec; }
            else ensureSectionLight(sec);
            value &= 255;
            if (sec.light[ii] == value) return false;
            sec.light[ii] = (byte)value;
            return true;
        }
        public static void stitchChunkLight(Chunk c)
        {
            if (c == null) return;
            int x0 = c.cx * CHUNK, z0 = c.cz * CHUNK, top = lightNeighborhoodTopY(c.cx, c.cz), maxIy = top - WORLD_MIN_Y;
            bool changed = false;
            for (int pass = 0; pass < 2; pass++)
            {
                bool skyMode = pass == 0;
                int head = 0, tail = 0;
                Action<int, int, int> pushLocal = (lx, iy, lz) => { if (tail < lightStitchQueue.Length) lightStitchQueue[tail++] = (iy * CHUNK + lz) * CHUNK + lx; };
                Action<int, int, int, int, int> seedFrom = (wx, y, wz, lx, lz) =>
                {
                    if (!lightCellLoaded(wx, wz)) return;
                    int sp = getPackedLightWorld(wx, y, wz), lv = skyMode ? sp >> 4 : sp & 15;
                    if (lv <= 1) return;
                    int sid = getBlock(wx, y, wz); var sm = getBlockMeta(wx, y, wz);
                    if (lightStopsState(sid, sm) && !(!skyMode && blockEmissionAt(wx, y, wz) > 0)) return;
                    int tx = x0 + lx, tz = z0 + lz, tid = chunkGet(c, lx, y, lz); var tm = getBlockMeta(tx, y, tz);
                    if (lightStopsState(tid, tm)) return;
                    int nv = lv - (lightAttenuationState(tid, tm) ? 2 : 1);
                    if (nv <= 0) return;
                    int op = chunkPackedLocal(c, lx, y, lz), old = skyMode ? op >> 4 : op & 15;
                    if (nv <= old) return;
                    int np = skyMode ? (op & 15) | (nv << 4) : (op & 0xf0) | nv;
                    if (setChunkPackedLocal(c, lx, y, lz, np)) { changed = true; pushLocal(lx, y - WORLD_MIN_Y, lz); }
                };
                for (int iy = 0; iy <= maxIy; iy++)
                {
                    int y = WORLD_MIN_Y + iy;
                    for (int i = 0; i < CHUNK; i++)
                    {
                        seedFrom(x0 - 1, y, z0 + i, 0, i);
                        seedFrom(x0 + CHUNK, y, z0 + i, CHUNK - 1, i);
                        seedFrom(x0 + i, y, z0 - 1, i, 0);
                        seedFrom(x0 + i, y, z0 + CHUNK, i, CHUNK - 1);
                    }
                }
                while (head < tail)
                {
                    int code = lightStitchQueue[head++], lx = code % CHUNK, n = (code - lx) / CHUNK, lz = n % CHUNK, iy = (n - lz) / CHUNK, y = WORLD_MIN_Y + iy,
                        op = chunkPackedLocal(c, lx, y, lz), lv = skyMode ? op >> 4 : op & 15;
                    if (lv <= 1) continue;
                    int sx = x0 + lx, sz = z0 + lz, sid = chunkGet(c, lx, y, lz); var sm = getBlockMeta(sx, y, sz);
                    if (lightStopsState(sid, sm) && !(!skyMode && blockEmissionAt(sx, y, sz) > 0)) continue;
                    foreach (var d in LIGHT_DIRS)
                    {
                        int nx = lx + d[0], ny = y + d[1], nz = lz + d[2];
                        if (nx < 0 || nx >= CHUNK || nz < 0 || nz >= CHUNK || !yInWorld(ny)) continue;
                        int tx = x0 + nx, tz = z0 + nz, tid = chunkGet(c, nx, ny, nz); var tm = getBlockMeta(tx, ny, tz);
                        if (lightStopsState(tid, tm)) continue;
                        int nv = lv - (lightAttenuationState(tid, tm) ? 2 : 1);
                        if (nv <= 0) continue;
                        int tp = chunkPackedLocal(c, nx, ny, nz), old = skyMode ? tp >> 4 : tp & 15;
                        if (nv > old)
                        {
                            int np = skyMode ? (tp & 15) | (nv << 4) : (tp & 0xf0) | nv;
                            if (setChunkPackedLocal(c, nx, ny, nz, np)) { changed = true; if (nv > 1) pushLocal(nx, ny - WORLD_MIN_Y, nz); }
                        }
                    }
                }
            }
            if (changed) { c.dirty = true; c.meshRev++; }
        }
    }
}
