// Port of world.js: world storage, worker pool, lighting engine, block updates (main thread).
// The Web Workers of the original are background threads here; each owns a WorldGen and a Mesher.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed class Section
    {
        public ushort[] ids = new ushort[4096];
        public byte[] meta = new byte[4096];
        public byte[] light = new byte[4096];
        public int danger = -1;      // blocks of water, lava, fire, magma or cactus in it (-1: not counted yet)
        public Section(bool skyFill) { if (skyFill) for (int i = 0; i < 4096; i++) light[i] = 0xF0; }
    }

    public sealed class Column
    {
        public readonly int cx, cz; public readonly long key;
        public readonly Section[] secs = new Section[SECTIONS];
        public int state;            // 0 requested, 1 have blocks + sky init, 2 fully lit, -1 unloaded
        public int dirty;            // bitmask of sections that need a remesh
        public readonly uint[] meshVer = new uint[SECTIONS];
        public int meshBusy;         // bitmask of sections with a mesh job in flight
        public int meshed;           // bitmask of sections that have (possibly empty) meshes
        public readonly object[] meshes = new object[SECTIONS];
        public int leafMode = -1;    // leaf mesh mode its sections are built with (-1: not chosen yet)
        public readonly uint[] visFrame = new uint[SECTIONS];   // last frame each section was reached by the visibility walk
        public Dictionary<int, int> edits;   // localIndex -> id | meta<<16 (player modifications)
        public byte[] biomes, caveBiomes;
        public List<int> emitters;
        public readonly short[] hm = new short[256]; // lowest y (exclusive) that still has full sky light
        public List<int> springs, spawners;
        public ushort[] tints;
        public bool editsWait;
        public GenResult genData;
        public object tag;           // renderer / game data
        public Column(int cx, int cz) { this.cx = cx; this.cz = cz; key = World.ColKey(cx, cz); }
    }

    public sealed class GenMsg { public int cx, cz; public GenResult r; }
    public sealed class SurfaceMsg { public int id, x, z, y, biome; }

    /// <summary>The worker pool: background threads, each with its own generator and mesher.</summary>
    public sealed class WorkerPool
    {
        readonly Thread[] threads;
        readonly BlockingCollection<object> jobs = new BlockingCollection<object>(new ConcurrentQueue<object>());
        public readonly ConcurrentQueue<object> results = new ConcurrentQueue<object>();
        int readyCount, busy;
        volatile bool stop;
        public readonly int count;
        public bool Ready => Volatile.Read(ref readyCount) == count;
        public int Load => Volatile.Read(ref busy);

        public WorkerPool(int n, int seed)
        {
            count = n;
            threads = new Thread[n];
            for (int i = 0; i < n; i++)
            {
                var t = new Thread(() => Run(seed)) { IsBackground = true, Name = "maincraft-worker-" + i, Priority = ThreadPriority.BelowNormal };
                threads[i] = t;
                t.Start();
            }
        }

        void Run(int seed)
        {
            WorldGen gen; Mesher mesher;
            try { gen = new WorldGen(seed); mesher = new Mesher(); }
            catch (Exception e) { results.Enqueue(e); return; }
            Interlocked.Increment(ref readyCount);
            while (!stop)
            {
                object job;
                try { job = jobs.Take(); }
                catch (InvalidOperationException) { break; }
                if (stop) break;
                try
                {
                    switch (job)
                    {
                        case GenMsg g: g.r = gen.Generate(g.cx, g.cz); results.Enqueue(g); break;
                        case MeshJob m: mesher.sway = m.sway; results.Enqueue(mesher.Mesh(m)); break;
                        case SurfaceMsg s: s.y = gen.SurfaceAt(s.x, s.z); s.biome = gen.ColInfoAt(s.x, s.z).biome; results.Enqueue(s); break;
                    }
                }
                catch (Exception e) { results.Enqueue(e); }
                finally { Interlocked.Decrement(ref busy); }
            }
        }

        public void Post(object job) { Interlocked.Increment(ref busy); jobs.Add(job); }

        public void Terminate()
        {
            stop = true;
            jobs.CompleteAdding();
        }
    }

    public sealed partial class World : IBlockAccess
    {
        public const int COL_GRID = 64, COL_MASK = 63;
        public static long ColKey(int cx, int cz) => (long)(cx + 32768) * 65536 + (cz + 32768);
        public static int SecIdx(int x, int ly, int z) => (ly << 8) | (z << 4) | x;
        public static readonly int[] DIRX_W = { 0, -1, 0, 1 }, DIRZ_W = { 1, 0, -1, 0 };
        // face directions in the mesher's order: +x, -x, +y, -y, +z, -z
        public static readonly int[][] FACE_DIR = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };

        // the blocks the path finder keeps away from (water, lava, fire, magma, cactus), by id
        static byte[] DANGER_ID;
        public static byte[] DangerIds()
        {
            if (DANGER_ID == null)
            {
                var d = new byte[65536];
                for (int id = 0; id < 65536; id++) if ((id < NBX && IsWaterId(id)) || id == B.LAVA || id == B.FIRE || id == B.MAGMA_BLOCK || id == B.CACTUS) d[id] = 1;
                DANGER_ID = d;
            }
            return DANGER_ID;
        }

        public readonly int seed;
        public readonly Dictionary<long, Column> cols = new Dictionary<long, Column>();
        readonly Column[] grid = new Column[COL_GRID * COL_GRID];
        public readonly Dictionary<long, Dictionary<int, int>> savedEdits = new Dictionary<long, Dictionary<int, int>>();   // edits of unloaded columns not yet in the database
        public readonly HashSet<long> editKeys = new HashSet<long>();     // columns with edits in the database (loaded when requested)
        public Func<long, Dictionary<int, int>> editStore;               // colKey -> its stored edits (or null)
        public readonly HashSet<long> dirtyCols = new HashSet<long>();    // columns with unsaved edits
        public readonly HashSet<Column> pendingLit = new HashSet<Column>();   // columns waiting for the horizontal light pass
        public int genInFlight, meshInFlight;
        readonly Stack<MeshJob> meshPool = new Stack<MeshJob>();         // recycled padded arrays
        public Action<Column, int, MeshResult> onMeshResult;
        public Action<Column> onColumnUnload;
        public Action<int, int, int, int, int> onBlockChanged;           // x, y, z, oldId, newId
        public Action<int, int, int, int, int, bool> onBreak;            // x, y, z, id, meta, byUpdate
        public Action<int, int, int, int, int> onFall;                   // x, y, z, id, meta
        public Action<SurfaceMsg> onSurface;
        public bool ready;
        public bool urgent;
        public readonly WorkerPool pool;
        // light queues (x,y,z,v)
        int[] lq = new int[1 << 20]; int lqh, lqt;
        int[] rq = new int[1 << 20]; int rqh, rqt;
        // scheduled block updates: key -> due tick; buckets by due tick in insertion order
        readonly Dictionary<long, long> sched = new Dictionary<long, long>();
        readonly Dictionary<long, LinkedListNode<(long t, List<long> bk)>> schedB = new Dictionary<long, LinkedListNode<(long, List<long>)>>();
        readonly LinkedList<(long t, List<long> bk)> schedOrder = new LinkedList<(long, List<long>)>();
        public readonly HashSet<Column> springCols = new HashSet<Column>();
        public readonly HashSet<Column> spawnerCols = new HashSet<Column>();   // columns holding generated monster spawners (ticked near the player)
        public long tick;
        public int statGen, statMesh, statLit;
        public Redstone rs;

        public World(int seed, int workers)
        {
            this.seed = seed;
            int nw = Math.Max(1, Math.Min(workers, 8));
            pool = new WorkerPool(nw, seed);
            DangerIds();
        }

        public void Dispose() => pool.Terminate();

        // ------------------------------------------------------------------ access
        public Column Col(int cx, int cz)
        {
            var c = grid[((cx & COL_MASK) << 6) | (cz & COL_MASK)];
            return c != null && c.cx == cx && c.cz == cz ? c : null;
        }

        // how many water, lava, fire, magma or cactus blocks section s of column (cx, cz) holds
        public int SectionDanger(int cx, int s, int cz)
        {
            var c = Col(cx, cz);
            if (c == null || c.state == 0 || s < 0 || s >= SECTIONS) return 0;
            var sec = c.secs[s];
            if (sec == null) return 0;
            if (sec.danger < 0)
            {
                var D = DANGER_ID; var ids = sec.ids;
                int n = 0;
                for (int i = 0; i < 4096; i++) n += D[ids[i]];
                sec.danger = n;
            }
            return sec.danger;
        }

        public int GetBlock(int x, int y, int z)
        {
            if (y < WORLD_MIN_Y) return B.BEDROCK;
            if (y >= WORLD_MAX_Y) return 0;
            var c = Col(x >> 4, z >> 4);
            if (c == null || c.state == 0) return 0;
            var s = c.secs[(y - WORLD_MIN_Y) >> 4];
            return s != null ? s.ids[((y & 15) << 8) | ((z & 15) << 4) | (x & 15)] : 0;
        }

        // height of the fluid surface in a cell as a fraction of the block (FluidState.getHeight): 1 with
        // more of the same fluid above, else its amount / 9; -1 without fluid
        public double FluidHeight(int x, int y, int z)
        {
            int id = GetBlock(x, y, z); bool w = IsWaterId(id);
            if (!w && id != B.LAVA) return -1;
            int up = GetBlock(x, y + 1, z);
            if (w ? IsWaterId(up) : up == B.LAVA) return 1;
            if ((FLAGS[id] & (BF_AQUATIC | BF_WET)) != 0) return 8.0 / 9;
            int m = GetMeta(x, y, z);
            return (m & 8) != 0 ? 8.0 / 9 : (8 - (m & 7)) / 9.0;
        }

        public int GetMeta(int x, int y, int z)
        {
            if (y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return 0;
            var c = Col(x >> 4, z >> 4);
            if (c == null || c.state == 0) return 0;
            var s = c.secs[(y - WORLD_MIN_Y) >> 4];
            return s != null ? s.meta[((y & 15) << 8) | ((z & 15) << 4) | (x & 15)] : 0;
        }

        public int GetLight(int x, int y, int z)
        {
            if (y >= WORLD_MAX_Y) return 0xF0;
            if (y < WORLD_MIN_Y) return 0;
            var c = Col(x >> 4, z >> 4);
            if (c == null || c.state == 0) return 0xF0;
            var s = c.secs[(y - WORLD_MIN_Y) >> 4];
            return s != null ? s.light[((y & 15) << 8) | ((z & 15) << 4) | (x & 15)] : 0xF0;
        }

        public bool IsLoaded(int x, int z) { var c = Col(x >> 4, z >> 4); return c != null && c.state != 0; }

        // biome at a block: the cave biome of its 4x4x4 cell underground, else the surface biome
        public int BiomeAt3(int x, int y, int z)
        {
            var c = Col(x >> 4, z >> 4); var G = c?.caveBiomes;
            if (G != null && y > WORLD_MIN_Y && y < WORLD_MAX_Y) { int b = G[(((y - WORLD_MIN_Y) >> 2) * 4 + ((z & 15) >> 2)) * 4 + ((x & 15) >> 2)]; if (b != 0) return b; }
            return BiomeAt(x, z);
        }
        public int BiomeAt(int x, int z) { var c = Col(x >> 4, z >> 4); return c != null && c.biomes != null ? c.biomes[((z & 15) << 4) | (x & 15)] : 0; }

        // ------------------------------------------------------------------ worker messages
        /// <summary>Handles finished worker jobs (the onmessage of the original); call every frame.</summary>
        public void Poll(int maxMs = 1000)
        {
            if (!ready && pool.Ready) ready = true;
            long start = Environment.TickCount;
            while (pool.results.TryDequeue(out var d))
            {
                OnWorker(d);
                if (Environment.TickCount - start > maxMs) break;
            }
        }

        void OnWorker(object d)
        {
            switch (d)
            {
                case Exception e: throw new Exception("worker error", e);
                case GenMsg g: genInFlight--; OnGenerated(g); return;
                case MeshResult r:
                {
                    meshInFlight--;
                    if (r.job != null) meshPool.Push(r.job);
                    int sy = r.sy;
                    var c = Col(r.cx, r.cz);
                    if (c == null) return;
                    c.meshBusy &= ~(1 << sy);
                    if (r.ver != c.meshVer[sy]) c.dirty |= 1 << sy;
                    c.meshed |= 1 << sy;
                    onMeshResult?.Invoke(c, sy, r);
                    return;
                }
                case SurfaceMsg s: onSurface?.Invoke(s); return;
            }
        }

        public void RequestSurface(int id, int x, int z) => pool.Post(new SurfaceMsg { id = id, x = x, z = z });

        public void RequestColumn(int cx, int cz)
        {
            long k = ColKey(cx, cz);
            if (cols.ContainsKey(k)) return;
            int slot = ((cx & COL_MASK) << 6) | (cz & COL_MASK);
            var old = grid[slot];
            if (old != null) UnloadColumn(old);
            var c = new Column(cx, cz);
            cols[k] = c;
            grid[slot] = c;
            // its stored edits are read before the generated blocks arrive (synchronous file read here)
            if (editStore != null && editKeys.Contains(k) && !savedEdits.ContainsKey(k))
            {
                var m = editStore(k);
                if (m != null && m.Count > 0) savedEdits[k] = m;
            }
            genInFlight++;
            pool.Post(new GenMsg { cx = cx, cz = cz });
        }

        void OnGenerated(GenMsg d)
        {
            var c = Col(d.cx, d.cz);
            if (c == null || c.state != 0) return;
            var r = d.r;
            for (int s = 0; s < SECTIONS; s++)
            {
                if (r.secIds[s] == null) continue;
                var sec = new Section(false) { ids = r.secIds[s], meta = r.secMeta[s] };
                c.secs[s] = sec;
            }
            // allocate air sections below the top so the light engine can store values
            int top = -1;
            for (int s = SECTIONS - 1; s >= 0; s--) if (c.secs[s] != null) { top = s; break; }
            for (int s = 0; s < top; s++) if (c.secs[s] == null) c.secs[s] = new Section(false);
            c.biomes = r.biomes; c.caveBiomes = r.caveBiomes;
            // fluid springs start flowing once the column and its neighbours are lit (see GameTick)
            if (r.springs != null && r.springs.Count > 0) { c.springs = r.springs; springCols.Add(c); }
            if (r.spawners != null && r.spawners.Count > 0) { c.spawners = r.spawners; spawnerCols.Add(c); }
            if (savedEdits.TryGetValue(c.key, out var saved)) { c.edits = saved; savedEdits.Remove(c.key); ApplyEdits(c); }
            // redstone parts among the edits pick up their state again (pending changes are not saved)
            if (c.edits != null && rs != null)
                foreach (var kv in c.edits) if (Redstone.RS_KIND[kv.Value & 0xFFFF] != 0) rs.Mark(c.cx * 16 + (kv.Key & 15), WORLD_MIN_Y + (kv.Key >> 8), c.cz * 16 + ((kv.Key >> 4) & 15));
            SkyInit(c);
            c.state = 1;
            statGen++;
            // this column and its neighbours may now be lightable
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                var n = Col(c.cx + dx, c.cz + dz);
                if (n != null && n.state == 1) pendingLit.Add(n);
            }
        }

        void ApplyEdits(Column c)
        {
            foreach (var kv in c.edits)
            {
                int idx = kv.Key, v = kv.Value;
                int y = (idx >> 8) + WORLD_MIN_Y, s = (y - WORLD_MIN_Y) >> 4;
                var sec = c.secs[s];
                if (sec == null)
                {
                    for (int k = 0; k <= s; k++) if (c.secs[k] == null) c.secs[k] = new Section(false);
                    sec = c.secs[s];
                }
                int li = ((y & 15) << 8) | (idx & 255);
                sec.ids[li] = (ushort)(v & 0xFFFF); sec.meta[li] = (byte)((v >> 16) & 255); sec.danger = -1;
            }
        }

        public void UnloadColumn(Column c)
        {
            if (c.edits != null && c.edits.Count > 0) savedEdits[c.key] = c.edits;
            onColumnUnload?.Invoke(c);
            cols.Remove(c.key);
            int slot = ((c.cx & COL_MASK) << 6) | (c.cz & COL_MASK);
            if (grid[slot] == c) grid[slot] = null;
            pendingLit.Remove(c);
            c.state = -1;
        }

        public IEnumerable<Column> AllColumns => cols.Values;

        // ------------------------------------------------------------------ lighting
        // vertical sky light fill for one column + emitters
        void SkyInit(Column c)
        {
            var secs = c.secs;
            int top = -1;
            for (int s = SECTIONS - 1; s >= 0; s--) if (secs[s] != null) { top = s; break; }
            var em = new List<int>();
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                int sky = 15, hm = WORLD_MIN_Y + (top + 1) * 16;
                int col = (z << 4) | x;
                for (int s = top; s >= 0; s--)
                {
                    var sec = secs[s];
                    var ids = sec.ids; var L = sec.light;
                    for (int ly = 15; ly >= 0; ly--)
                    {
                        int i = (ly << 8) | col, id = ids[i];
                        if (sky > 0)
                        {
                            int cost = LCOST[id];
                            if (cost >= 15) sky = 0;
                            else if (sky < 15 || VDIM[id] != 0) sky = Math.Max(0, sky - cost);
                            if (sky == 15) hm = WORLD_MIN_Y + s * 16 + ly;
                        }
                        int e = EMIT[id];
                        L[i] = (byte)((sky << 4) | e);
                        if (e != 0) { em.Add(c.cx * 16 + x); em.Add(WORLD_MIN_Y + s * 16 + ly); em.Add(c.cz * 16 + z); }
                    }
                }
                c.hm[col] = (short)hm;
            }
            c.emitters = em;
        }

        // horizontal propagation pass; requires the 8 neighbours to have sky init
        public bool LitPass(Column c)
        {
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                var n = Col(c.cx + dx, c.cz + dz);
                if (n == null || n.state < 1) return false;
            }
            int x0 = c.cx * 16, z0 = c.cz * 16;
            lqh = lqt = 0;
            // sky seeds: cells in the band between own heightmap and neighbours' heightmaps (incl. ring)
            for (int z = -1; z <= 16; z++) for (int x = -1; x <= 16; x++)
            {
                int wx = x0 + x, wz = z0 + z;
                var cc = Col(wx >> 4, wz >> 4);
                int hm = cc.hm[((wz & 15) << 4) | (wx & 15)];
                int top = hm;
                for (int k = 0; k < 4; k++)
                {
                    int nx = wx + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = wz + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    var nc = Col(nx >> 4, nz >> 4);
                    if (nc == null || nc.state < 1) continue;
                    int h = nc.hm[((nz & 15) << 4) | (nx & 15)];
                    if (h > top) top = h;
                }
                // scan down from top until sky reaches 0 in this column
                for (int y = top; y >= WORLD_MIN_Y; y--)
                {
                    int l = GetLight(wx, y, wz), s = l >> 4;
                    if (y < hm && s <= 1) break;
                    if (s > 1) PushL(wx, y, wz, s);
                }
            }
            PropagateAdd(true);
            lqh = lqt = 0;
            var em = c.emitters;
            if (em != null) for (int i = 0; i < em.Count; i += 3) PushL(em[i], em[i + 1], em[i + 2], GetLight(em[i], em[i + 1], em[i + 2]) & 15);
            // emitters of neighbours that were lit before this column arrived are already spread into it
            PropagateAdd(false);
            c.state = 2;
            statLit++;
            return true;
        }

        void PushL(int x, int y, int z, int v)
        {
            int t = lqt;
            if (t + 4 > lq.Length)
            {
                if (lqh > 0) { Array.Copy(lq, lqh, lq, 0, t - lqh); t -= lqh; lqh = 0; }
                if (t + 4 > lq.Length) Array.Resize(ref lq, lq.Length * 2);
            }
            var q = lq; q[t] = x; q[t + 1] = y; q[t + 2] = z; q[t + 3] = v; lqt = t + 4;
        }

        void PushR(int x, int y, int z, int v)
        {
            int t = rqt;
            if (t + 4 > rq.Length)
            {
                if (rqh > 0) { Array.Copy(rq, rqh, rq, 0, t - rqh); t -= rqh; rqh = 0; }
                if (t + 4 > rq.Length) Array.Resize(ref rq, rq.Length * 2);
            }
            var q = rq; q[t] = x; q[t + 1] = y; q[t + 2] = z; q[t + 3] = v; rqt = t + 4;
        }

        // raw light set; allocates sections when needed; marks meshes dirty
        void SetLightRaw(Column c, int x, int y, int z, int v)
        {
            int s = (y - WORLD_MIN_Y) >> 4;
            var sec = c.secs[s];
            if (sec == null)
            {
                if (v == 0xF0) return;
                for (int k = 0; k <= s; k++) if (c.secs[k] == null) c.secs[k] = new Section(true);
                sec = c.secs[s];
            }
            sec.light[((y & 15) << 8) | ((z & 15) << 4) | (x & 15)] = (byte)v;
            if (c.state == 2 || c.meshed != 0) MarkDirtyAt(x, y, z);
        }

        static readonly int[] PDX = { 1, -1, 0, 0, 0, 0 }, PDY = { 0, 0, 1, -1, 0, 0 }, PDZ = { 0, 0, 0, 0, 1, -1 };

        void PropagateAdd(bool sky)
        {
            while (lqh < lqt)
            {
                var q = lq; int h = lqh;
                int x = q[h], y = q[h + 1], z = q[h + 2];
                lqh = h + 4;
                int cur = sky ? GetLight(x, y, z) >> 4 : GetLight(x, y, z) & 15;
                if (cur <= 1) continue;
                for (int d = 0; d < 6; d++)
                {
                    int nx = x + PDX[d], ny = y + PDY[d], nz = z + PDZ[d];
                    if (ny < WORLD_MIN_Y || ny >= WORLD_MAX_Y) continue;
                    var c = Col(nx >> 4, nz >> 4);
                    if (c == null || c.state < 1) continue;
                    var sec = c.secs[(ny - WORLD_MIN_Y) >> 4];
                    int li = ((ny & 15) << 8) | ((nz & 15) << 4) | (nx & 15);
                    int id = sec != null ? sec.ids[li] : 0;
                    int cost = LCOST[id];
                    if (cost >= 15) continue;
                    int old = sec != null ? sec.light[li] : 0xF0;
                    int nv;
                    if (sky)
                    {
                        nv = (d == 3 && cur == 15 && VDIM[id] == 0) ? 15 : cur - cost;
                        if (nv > (old >> 4)) { SetLightRaw(c, nx, ny, nz, (nv << 4) | (old & 15)); if (nv > 1) PushL(nx, ny, nz, nv); }
                    }
                    else
                    {
                        nv = cur - cost;
                        if (nv > (old & 15)) { SetLightRaw(c, nx, ny, nz, (old & 0xF0) | nv); if (nv > 1) PushL(nx, ny, nz, nv); }
                    }
                }
            }
            lqh = lqt = 0;
        }

        // removal pass starting from the cells in rq (value = old light); relight sources go to lq
        void PropagateRemove(bool sky)
        {
            while (rqh < rqt)
            {
                var q = rq; int h = rqh;
                int x = q[h], y = q[h + 1], z = q[h + 2], v = q[h + 3];
                rqh = h + 4;
                for (int d = 0; d < 6; d++)
                {
                    int nx = x + PDX[d], ny = y + PDY[d], nz = z + PDZ[d];
                    if (ny < WORLD_MIN_Y || ny >= WORLD_MAX_Y) continue;
                    var c = Col(nx >> 4, nz >> 4);
                    if (c == null || c.state < 1) continue;
                    var sec = c.secs[(ny - WORLD_MIN_Y) >> 4];
                    int li = ((ny & 15) << 8) | ((nz & 15) << 4) | (nx & 15);
                    int old = sec != null ? sec.light[li] : 0xF0;
                    int nv = sky ? old >> 4 : old & 15;
                    if (nv == 0) continue;
                    int id = sec != null ? sec.ids[li] : 0;
                    if (nv < v || (sky && d == 3 && v == 15 && nv == 15))
                    {
                        if (!sky && EMIT[id] != 0) { PushL(nx, ny, nz, nv); continue; }
                        SetLightRaw(c, nx, ny, nz, sky ? (old & 15) : (old & 0xF0));
                        PushR(nx, ny, nz, nv);
                        if (!sky && EMIT[id] != 0) PushL(nx, ny, nz, EMIT[id]);
                    }
                    else
                    {
                        PushL(nx, ny, nz, nv);
                    }
                }
            }
            rqh = rqt = 0;
        }

        // relight after a block change at x,y,z (old/new ids known)
        void RelightAt(int x, int y, int z, int oldId, int newId)
        {
            var c = Col(x >> 4, z >> 4);
            if (c == null || c.state < 1) return;
            int lt = GetLight(x, y, z);
            int oldSky = lt >> 4, oldBlk = lt & 15;
            int newCost = LCOST[newId];
            // ---- block light
            lqh = lqt = 0; rqh = rqt = 0;
            if (oldBlk > 0)
            {
                SetLightRaw(c, x, y, z, lt & 0xF0);
                PushR(x, y, z, oldBlk);
                PropagateRemove(false);
            }
            int e = EMIT[newId];
            if (e != 0)
            {
                int cur = GetLight(x, y, z);
                SetLightRaw(c, x, y, z, (cur & 0xF0) | e);
                PushL(x, y, z, e);
            }
            if (newCost < 15) PushNeighbours(x, y, z, false);
            PropagateAdd(false);
            // ---- sky light
            lqh = lqt = 0; rqh = rqt = 0;
            if (oldSky > 0 && (newCost > LCOST[oldId] || VDIM[newId] > VDIM[oldId]))
            {
                int cur = GetLight(x, y, z);
                SetLightRaw(c, x, y, z, cur & 15);
                PushR(x, y, z, oldSky);
                PropagateRemove(true);
            }
            if (newCost < 15) PushNeighbours(x, y, z, true);
            PropagateAdd(true);
            // heightmap maintenance (approximate: only raises/lowers at this column)
            int hmi = ((z & 15) << 4) | (x & 15);
            bool shades = newCost > 1 || VDIM[newId] != 0;
            if (shades && y >= c.hm[hmi]) c.hm[hmi] = (short)(y + 1);
            else if (!shades && y + 1 == c.hm[hmi])
            {
                int yy = y;
                while (yy > WORLD_MIN_Y && (GetLight(x, yy, z) >> 4) == 15) yy--;
                c.hm[hmi] = (short)(yy + 1);
            }
        }

        void PushNeighbours(int x, int y, int z, bool sky)
        {
            foreach (var n in FACE_DIR)
            {
                int l = GetLight(x + n[0], y + n[1], z + n[2]);
                int v = sky ? l >> 4 : l & 15;
                if (v > 1) PushL(x + n[0], y + n[1], z + n[2], v);
            }
        }

        // ------------------------------------------------------------------ edits
        public void MarkDirtyAt(int x, int y, int z)
        {
            int lx = x & 15, lz = z & 15, ly = (y - WORLD_MIN_Y) & 15, sy = (y - WORLD_MIN_Y) >> 4;
            int cx = x >> 4, cz = z >> 4;
            int dxs = lx == 0 ? -1 : lx == 15 ? 1 : 0, dzs = lz == 0 ? -1 : lz == 15 ? 1 : 0, dys = ly == 0 ? -1 : ly == 15 ? 1 : 0;
            MarkSec(cx, cz, sy);
            if (dxs != 0) MarkSec(cx + dxs, cz, sy);
            if (dzs != 0) MarkSec(cx, cz + dzs, sy);
            if (dys != 0) MarkSec(cx, cz, sy + dys);
            if (dxs != 0 && dzs != 0) MarkSec(cx + dxs, cz + dzs, sy);
            if (dxs != 0 && dys != 0) MarkSec(cx + dxs, cz, sy + dys);
            if (dzs != 0 && dys != 0) MarkSec(cx, cz + dzs, sy + dys);
            if (dxs != 0 && dzs != 0 && dys != 0) MarkSec(cx + dxs, cz + dzs, sy + dys);
        }

        public void MarkSec(int cx, int cz, int sy)
        {
            if (sy < 0 || sy >= SECTIONS) return;
            var c = Col(cx, cz);
            if (c == null) return;
            c.dirty |= 1 << sy;
            c.meshVer[sy]++;
            if (c.state == 2) urgent = true;
        }

        // set a block with full side effects (light, edits, neighbour updates)
        public bool SetBlock(int x, int y, int z, int id, int meta, bool noUpdate = false, bool quiet = false)
        {
            if (y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return false;
            var c = Col(x >> 4, z >> 4);
            if (c == null || c.state < 1) return false;
            int s = (y - WORLD_MIN_Y) >> 4;
            var sec = c.secs[s];
            if (sec == null)
            {
                if (id == 0) return false;
                for (int k = 0; k <= s; k++) if (c.secs[k] == null) c.secs[k] = new Section(true);
                sec = c.secs[s];
            }
            int li = ((y & 15) << 8) | ((z & 15) << 4) | (x & 15);
            int oldId = sec.ids[li], oldMeta = sec.meta[li];
            meta &= 255;
            if (oldId == id && oldMeta == meta) return false;
            sec.ids[li] = (ushort)id; sec.meta[li] = (byte)meta;
            if (sec.danger >= 0) { var D = DANGER_ID; sec.danger += D[id] - D[oldId]; }
            if (c.edits == null) c.edits = new Dictionary<int, int>();
            c.edits[((y - WORLD_MIN_Y) << 8) | ((z & 15) << 4) | (x & 15)] = id | (meta << 16);
            dirtyCols.Add(c.key);
            MarkDirtyAt(x, y, z);
            if (LCOST[oldId] != LCOST[id] || VDIM[oldId] != VDIM[id] || EMIT[oldId] != EMIT[id]) RelightAt(x, y, z, oldId, id);
            if (!noUpdate)
            {
                ScheduleAround(x, y, z);
                NeighbourChanged(x, y, z);
            }
            onBlockChanged?.Invoke(x, y, z, oldId, id);
            if (rs != null && !quiet) rs.BlockChanged(x, y, z, oldId, id);
            return true;
        }

        // ------------------------------------------------------------------ scheduled updates (fluids, falling)
        // Block ticks as Minecraft's LevelTicks: due time per position (the earliest wins), kept in
        // buckets by tick, at most 65536 run per game tick.
        public void Schedule(int x, int y, int z, int delay)
        {
            long k = ((long)(x + 1048576) * 2097152 + (z + 1048576)) * 512 + (y - WORLD_MIN_Y), due = tick + Math.Max(1, delay);
            if (sched.TryGetValue(k, out long cur) && cur <= due) return;
            sched[k] = due;
            if (!schedB.TryGetValue(due, out var node))
            {
                node = schedOrder.AddLast((due, new List<long>()));
                schedB[due] = node;
            }
            node.Value.bk.Add(k);
        }

        int Delay(int xx, int yy, int zz) { int id = GetBlock(xx, yy, zz); return IsWaterId(id) ? 5 : id == B.LAVA ? 30 : (FLAGS[id] & BF_FALL) != 0 ? 2 : 1; }

        // a block changed: it and its six neighbours get a tick, after the delay of what is there
        // (water 5, lava 30, falling blocks 2, anything else 1)
        public void ScheduleAround(int x, int y, int z)
        {
            Schedule(x, y, z, Delay(x, y, z));
            foreach (var d in FACE_DIR) Schedule(x + d[0], y + d[1], z + d[2], Delay(x + d[0], y + d[1], z + d[2]));
        }

        // support rules for attached blocks
        void NeighbourChanged(int x, int y, int z)
        {
            void Check(int xx, int yy, int zz)
            {
                int id = GetBlock(xx, yy, zz);
                if (id == 0) return;
                if (!CanStay(xx, yy, zz, id, GetMeta(xx, yy, zz))) BreakBlock(xx, yy, zz, true);
            }
            Check(x, y + 1, z); Check(x, y - 1, z); Check(x + 1, y, z); Check(x - 1, y, z); Check(x, y, z + 1); Check(x, y, z - 1);
        }

        // sugar cane: on cane, or on soil / sand with water right next to that block (Minecraft rule)
        public bool CaneSupported(int x, int y, int z)
        {
            int b = GetBlock(x, y - 1, z);
            if (b == B.SUGAR_CANE) return true;
            if (b != B.GRASS && b != B.DIRT && b != B.PODZOL && b != B.COARSE_DIRT && b != B.MUD && b != B.SAND && b != B.RED_SAND) return false;
            return IsWaterId(GetBlock(x + 1, y - 1, z)) || IsWaterId(GetBlock(x - 1, y - 1, z)) ||
                IsWaterId(GetBlock(x, y - 1, z + 1)) || IsWaterId(GetBlock(x, y - 1, z - 1));
        }

        public bool CanStay(int x, int y, int z, int id, int m)
        {
            int sh = SHAPE[id], below = GetBlock(x, y - 1, z);
            int fl = FLAGS[id];
            if (sh == SH.WATER || sh == SH.LAVA) return true;
            if ((fl & BF_AQUATIC) != 0) return SOLID[below] != 0 || below == id;
            if (sh == SH.CROSS)
            {
                if ((fl & BF_HANG) != 0) { int a = GetBlock(x, y + 1, z); return SOLID[a] != 0 || a == id || (FLAGS[a] & BF_HANG) != 0; }
                if (id == B.SUGAR_CANE) return CaneSupported(x, y, z);
                if (id == B.CACTUS || id == B.BAMBOO_PLANT) return below == id || SOLID[below] != 0;
                if (id == B.COBWEB) return true;
                return SOLID[below] != 0 && below != B.GLASS;
            }
            if (sh == SH.TALL)
            {
                if ((m & 1) != 0) return GetBlock(x, y - 1, z) == id;
                return GetBlock(x, y + 1, z) == id && SOLID[below] != 0;
            }
            if (sh == SH.DOOR)
            {
                if (((m >> 2) & 1) != 0) return GetBlock(x, y - 1, z) == id;
                return GetBlock(x, y + 1, z) == id && SOLID[below] != 0;
            }
            if (sh == SH.TORCH)
            {
                if (m == 0) return SOLID[below] != 0 && SHAPE[below] != SH.PANE;
                int d = m - 1;
                if (d < 0 || d > 3) return false;
                return OPAQUE[GetBlock(x + DIRX_W[d], y, z + DIRZ_W[d])] == 1;
            }
            if (sh == SH.CARPET || sh == SH.PLATE || sh == SH.RAIL || sh == SH.LILY)
            {
                if (sh == SH.LILY) return below == B.WATER;
                return SOLID[below] == 1;
            }
            // redstone: dust, repeaters and comparators on a full block, a lever on the block it hangs on
            if (id == B.REDSTONE_WIRE || id == B.REPEATER || id == B.COMPARATOR) return OPAQUE[below] == 1;
            if (id == B.LEVER)
            {
                int face = (m >> 2) & 3, d = face == 0 ? 5 : face == 2 ? 4 : RS_OPP[m & 3];
                return OPAQUE[GetBlock(x + RS_DX[d], y + RS_DY[d], z + RS_DZ[d])] == 1;
            }
            // a button hangs on the block on its back side, like a wall torch
            if (sh == SH.BUTTON) { int d = m & 3; return OPAQUE[GetBlock(x + DIRX_W[d], y, z + DIRZ_W[d])] == 1; }
            if (id == B.COCOA) { int d = m & 3; return IsJungleLog(GetBlock(x + DIRX_W[d], y, z + DIRZ_W[d])); }
            if (sh == SH.CRYSTAL)
            {
                var n = FACE_DIR[m % 6];
                return OPAQUE[GetBlock(x - n[0], y - n[1], z - n[2])] == 1;
            }
            if (sh == SH.LADDER)
            {
                int f = m & 3, d = (f + 2) & 3;
                return OPAQUE[GetBlock(x + DIRX_W[d], y, z + DIRZ_W[d])] == 1;
            }
            if (sh == SH.BED)
            {
                int f = m & 3, head = (m >> 2) & 1;
                int ox = head != 0 ? -DIRX_W[f] : DIRX_W[f], oz = head != 0 ? -DIRZ_W[f] : DIRZ_W[f];
                return GetBlock(x + ox, y, z + oz) == id;
            }
            return true;
        }

        // remove block (drops handled by caller via onBreak hook; noDrop: destroyed without drops, e.g. by lava)
        public void BreakBlock(int x, int y, int z, bool byUpdate, bool noDrop = false)
        {
            int id = GetBlock(x, y, z);
            if (id == 0) return;
            int m = GetMeta(x, y, z);
            if (onBreak != null && !noDrop) onBreak(x, y, z, id, m, byUpdate);
            int fill = (FLAGS[id] & (BF_AQUATIC | BF_WET)) != 0 ? B.WATER : 0;
            SetBlock(x, y, z, fill, 0);
            int sh = SHAPE[id];
            if (sh == SH.DOOR || sh == SH.TALL)
            {
                int up = sh == SH.DOOR ? (m >> 2) & 1 : m & 1;
                int oy = up != 0 ? y - 1 : y + 1;
                if (GetBlock(x, oy, z) == id) SetBlock(x, oy, z, 0, 0);
            }
            if (sh == SH.BED)
            {
                int f = m & 3, head = (m >> 2) & 1;
                int ox = head != 0 ? -DIRX_W[f] : DIRX_W[f], oz = head != 0 ? -DIRZ_W[f] : DIRZ_W[f];
                if (GetBlock(x + ox, y, z + oz) == id) SetBlock(x + ox, y, z + oz, 0, 0);
            }
        }

        // one game tick (20 per second)
        public void GameTick()
        {
            tick++;
            // Minecraft schedules a fluid tick for every spring it generates; here once the column and
            // its neighbours are lit, so the water can run over the chunk border
            if (springCols.Count > 0)
            {
                foreach (var c in new List<Column>(springCols))
                {
                    if (!cols.TryGetValue(c.key, out var cc) || cc != c) { springCols.Remove(c); continue; }
                    bool rdy = c.state == 2;
                    for (int dz = -1; dz <= 1 && rdy; dz++) for (int dx = -1; dx <= 1 && rdy; dx++) { var n = Col(c.cx + dx, c.cz + dz); if (n == null || n.state != 2) rdy = false; }
                    if (!rdy) continue;
                    var sp = c.springs;
                    for (int i = 0; i < sp.Count; i += 3) Schedule(sp[i], sp[i + 1], sp[i + 2], 1);
                    c.springs = null; springCols.Remove(c);
                }
            }
            int n2 = 0;
            for (var node = schedOrder.First; node != null;)
            {
                var next = node.Next;
                var (t, bk) = node.Value;
                if (t <= tick)
                {
                    while (bk.Count > 0 && n2 < 65536)
                    {
                        long k = bk[bk.Count - 1]; bk.RemoveAt(bk.Count - 1);
                        if (!sched.TryGetValue(k, out long due) || due != t) continue;      // moved to an earlier tick, or done
                        sched.Remove(k);
                        int y = (int)(k % 512) + WORLD_MIN_Y; long xz = k / 512; int z = (int)(xz % 2097152) - 1048576, x = (int)(xz / 2097152) - 1048576;
                        UpdateBlock(x, y, z);
                        n2++;
                    }
                    if (bk.Count == 0) { schedB.Remove(t); schedOrder.Remove(node); }
                }
                node = next;
            }
            rs?.Tick();
        }

        void UpdateBlock(int x, int y, int z)
        {
            if (!IsLoaded(x, z)) return;
            int id = GetBlock(x, y, z);
            // concrete powder touching water sets into concrete (ConcretePowderBlock)
            int cc = CONCRETE_OF[id];
            if (cc != 0) foreach (var d in FACE_DIR) if (IsWaterId(GetBlock(x + d[0], y + d[1], z + d[2]))) { SetBlock(x, y, z, cc, 0); return; }
            if ((FLAGS[id] & BF_FALL) != 0)
            {
                int b = GetBlock(x, y - 1, z);
                if (b == 0 || SHAPE[b] == SH.WATER || SHAPE[b] == SH.LAVA || (FLAGS[b] & BF_REPLACE) != 0)
                {
                    int m = GetMeta(x, y, z);
                    SetBlock(x, y, z, 0, 0);
                    // the game turns it into a falling block entity (Minecraft's FallingBlockEntity)
                    if (onFall != null) onFall(x, y, z, id, m);
                    else { SetBlock(x, y - 1, z, id, m); Schedule(x, y - 1, z, 2); }
                }
                return;
            }
            if (id == B.WATER || id == B.LAVA) FluidUpdate(x, y, z, id);
            // water plants and waterlogged blocks hold a water source, which spreads like one
            else if ((FLAGS[id] & (BF_AQUATIC | BF_WET)) != 0) Spread(x, y, z, B.WATER, 0);
        }

        // Minecraft's FlowingFluid. meta: bits 0-2 level (0 = source, amount = 8 - level), bit 3 falling
        // (amount 8). A ticked flowing cell recomputes its state from its neighbours (getNewLiquid), then
        // every cell spreads: down when it can (sideways too only with 3+ source neighbours), else, if it
        // is a source or stands on ground, sideways one level lower (lava two) toward the nearest drop
        // within 4 blocks (lava 2).
        void FluidUpdate(int x, int y, int z, int fid)
        {
            bool isW = fid == B.WATER;
            int m = GetMeta(x, y, z);
            // LiquidBlock.shouldSpreadLiquid: lava touching water (beside or above) hardens
            if (!isW) foreach (var d in FACE_DIR)
            {
                if (d[1] < 0 || !IsWaterId(GetBlock(x + d[0], y + d[1], z + d[2]))) continue;
                SetBlock(x, y, z, m == 0 ? B.OBSIDIAN : B.COBBLE, 0);
                return;
            }
            if (m != 0)
            {
                int nw = NewLiquid(x, y, z, fid);
                if (nw < 0) { SetBlock(x, y, z, 0, 0); return; }
                if (nw != m) { SetBlock(x, y, z, fid, nw); m = nw; }
            }
            Spread(x, y, z, fid, m);
        }

        static bool SameFluid(int id, int fid) => fid == B.WATER ? IsWaterId(id) : id == fid;
        int FluidMeta(int x, int y, int z, int id) => id == B.WATER || id == B.LAVA ? GetMeta(x, y, z) : 0;
        // the fluid can flow into this cell: nothing there, or a block the flow washes away
        static bool CanFlowInto(int id) => id == 0 || (FluidBreaks(id) && !IsLiquidId(id));

        int NewLiquid(int x, int y, int z, int fid)
        {
            bool isW = fid == B.WATER;
            int maxA = 0, sources = 0;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DIRX_W[d], nz = z + DIRZ_W[d], n = GetBlock(nx, y, nz);
                if (!SameFluid(n, fid)) continue;
                int nm = FluidMeta(nx, y, nz, n);
                if (nm == 0) sources++;
                maxA = Math.Max(maxA, (nm & 8) != 0 ? 8 : 8 - (nm & 7));
            }
            if (isW && sources >= 2)
            {
                int b = GetBlock(x, y - 1, z);
                if ((SOLID[b] != 0 && !IsLiquidId(b)) || (SameFluid(b, fid) && FluidMeta(x, y - 1, z, b) == 0)) return 0;
            }
            if (SameFluid(GetBlock(x, y + 1, z), fid)) return 8;
            int a = maxA - (isW ? 1 : 2);
            return a <= 0 ? -1 : 8 - a;
        }

        void Spread(int x, int y, int z, int fid, int m)
        {
            bool isW = fid == B.WATER; int below = GetBlock(x, y - 1, z);
            if (!isW && IsWaterId(below)) { SetBlock(x, y - 1, z, B.STONE, 0); return; }   // lava falling onto water
            if (CanFlowInto(below))
            {
                SpreadTo(x, y - 1, z, fid, 8, below);
                int src = 0;
                for (int d = 0; d < 4; d++) { int n = GetBlock(x + DIRX_W[d], y, z + DIRZ_W[d]); if (SameFluid(n, fid) && FluidMeta(x + DIRX_W[d], y, z + DIRZ_W[d], n) == 0) src++; }
                if (src >= 3) SpreadSides(x, y, z, fid, m);
            }
            else if (m == 0 || !SameFluid(below, fid)) SpreadSides(x, y, z, fid, m);
        }

        void SpreadTo(int x, int y, int z, int fid, int meta, int cur)
        {
            if (cur != 0) BreakBlock(x, y, z, true, fid == B.LAVA);   // water drops the block, lava burns it
            SetBlock(x, y, z, fid, meta);
        }

        // a drop below this cell the fluid could fall into
        bool IsHole(int x, int y, int z, int fid) { int b = GetBlock(x, y - 1, z); return CanFlowInto(b) || (SameFluid(b, fid) && b == fid); }

        int SlopeDistance(int x, int y, int z, int depth, int from, int fid, int maxD)
        {
            int best = 1000;
            for (int d = 0; d < 4; d++)
            {
                if (d == from) continue;
                int nx = x + DIRX_W[d], nz = z + DIRZ_W[d], n = GetBlock(nx, y, nz);
                if (!(CanFlowInto(n) || (n == fid && GetMeta(nx, y, nz) != 0))) continue;
                if (IsHole(nx, y, nz, fid)) return depth;
                if (depth < maxD) best = Math.Min(best, SlopeDistance(nx, y, nz, depth + 1, (d + 2) & 3, fid, maxD));
            }
            return best;
        }

        void SpreadSides(int x, int y, int z, int fid, int m)
        {
            bool isW = fid == B.WATER;
            int amount = (m & 8) != 0 ? 7 : 8 - (m & 7) - (isW ? 1 : 2);
            if (amount <= 0) return;
            // FlowingFluid.getSpread: the directions with the shortest way to a drop
            int maxD = isW ? 4 : 2; var dist = new[] { 1000, 1000, 1000, 1000 };
            int min = 1000;
            for (int d = 0; d < 4; d++)
            {
                // flowing fluid of the same kind takes part in choosing the way (it cannot be spread into)
                int nx = x + DIRX_W[d], nz = z + DIRZ_W[d], n = GetBlock(nx, y, nz);
                if (!CanFlowInto(n) && !(n == fid && GetMeta(nx, y, nz) != 0)) continue;
                dist[d] = IsHole(nx, y, nz, fid) ? 0 : SlopeDistance(nx, y, nz, 1, (d + 2) & 3, fid, maxD);
                if (dist[d] < min) min = dist[d];
            }
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DIRX_W[d], nz = z + DIRZ_W[d], n = GetBlock(nx, y, nz);
                if (!CanFlowInto(n) || dist[d] > min) continue;
                SpreadTo(nx, y, nz, fid, 8 - amount, n);
            }
        }

        // ------------------------------------------------------------------ mesh jobs
        readonly Column[] jobCols = new Column[9];

        // build padded 18^3 arrays for section (cx, sy, cz)
        MeshJob BuildMeshJob(Column c, int sy)
        {
            var job = meshPool.Count > 0 ? meshPool.Pop() : new MeshJob { ids = new ushort[5832], meta = new byte[5832], light = new byte[5832] };
            var ids = job.ids; var meta = job.meta; var light = job.light;
            int cx = c.cx, cz = c.cz;
            var cols9 = jobCols;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) cols9[(dz + 1) * 3 + dx + 1] = Col(cx + dx, cz + dz);
            for (int y = -1; y <= 16; y++)
            {
                int wy = sy * 16 + y, s = wy >> 4, ly = wy & 15;
                bool inRange = wy >= 0 && wy < WORLD_H;
                for (int z = -1; z <= 16; z++)
                {
                    int cz3 = z < 0 ? 0 : z > 15 ? 2 : 1, lz = z & 15;
                    int o = ((y + 1) * 18 + (z + 1)) * 18;
                    int row = (ly << 8) | (lz << 4);
                    for (int k = 0; k < 3; k++)
                    {
                        // k=0: x=-1 (west column), k=1: x=0..15, k=2: x=16 (east column)
                        var col = cols9[cz3 * 3 + k];
                        var sec = inRange && col != null ? col.secs[s] : null;
                        int dst = k == 0 ? o : k == 1 ? o + 1 : o + 17, n = k == 1 ? 16 : 1;
                        if (sec == null)
                        {
                            // outside the world is open (below: the void, so the world's underside is drawn as in
                            // Minecraft); missing sections inside it are air with full sky light
                            byte fl = (byte)(inRange && col != null ? 0xF0 : (wy < 0 ? 0 : 0xF0));
                            for (int q = dst; q < dst + n; q++) { ids[q] = 0; meta[q] = 0; light[q] = fl; }
                            continue;
                        }
                        if (k == 1)
                        {
                            Array.Copy(sec.ids, row, ids, dst, 16); Array.Copy(sec.meta, row, meta, dst, 16); Array.Copy(sec.light, row, light, dst, 16);
                        }
                        else
                        {
                            int li = row | (k == 0 ? 15 : 0);
                            ids[dst] = sec.ids[li]; meta[dst] = sec.meta[li]; light[dst] = sec.light[li];
                        }
                    }
                }
            }
            return job;
        }

        public bool SectionEmpty(Column c, int sy)
        {
            var s = c.secs[sy];
            if (s == null) return true;
            var ids = s.ids;
            for (int i = 0; i < 4096; i++) if (ids[i] != 0) return false;
            return true;
        }

        public void SubmitMesh(Column c, int sy, int leaves, bool sway)
        {
            var job = BuildMeshJob(c, sy);
            c.meshBusy |= 1 << sy;
            c.dirty &= ~(1 << sy);
            meshInFlight++;
            job.cx = c.cx; job.cz = c.cz; job.sy = sy; job.ver = (int)c.meshVer[sy]; job.leaves = leaves; job.sway = sway; job.tints = ColumnTints(c);
            pool.Post(job);
        }

        // Grass / foliage / water colour of every column (3 x 256, RGB565), averaged over the 5x5 columns
        // around it like Minecraft's biome blend, so colours fade across biome borders. Cached once all
        // neighbour biomes are known (biomes never change afterwards).
        public ushort[] ColumnTints(Column c)
        {
            if (c.tints != null) return c.tints;
            const int R = 2, N = 16 + 2 * R;
            var bio = new byte[N * N];
            bool complete = true;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                var n = dx != 0 || dz != 0 ? Col(c.cx + dx, c.cz + dz) : c;
                var nb = n?.biomes;
                if (nb == null) complete = false;
                for (int z = 0; z < 16; z++)
                {
                    int gz = dz * 16 + z + R;
                    if (gz < 0 || gz >= N) continue;
                    for (int x = 0; x < 16; x++)
                    {
                        int gx = dx * 16 + x + R;
                        if (gx < 0 || gx >= N) continue;
                        // unknown neighbour: repeat this column's edge biome
                        bio[gz * N + gx] = nb != null ? nb[z * 16 + x] : c.biomes[Math.Min(15, Math.Max(0, gz - R)) * 16 + Math.Min(15, Math.Max(0, gx - R))];
                    }
                }
            }
            var outp = new ushort[768]; var T = WG.BIOME_TINT; const int D = 2 * R + 1, cnt = D * D;
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
            {
                for (int k = 0; k < 3; k++)
                {
                    double r = 0, g = 0, b = 0;
                    for (int j = 0; j < D; j++) for (int i = 0; i < D; i++)
                    {
                        uint v = T[bio[(z + j) * N + x + i] * 3 + k];
                        r += v >> 16; g += (v >> 8) & 255; b += v & 255;
                    }
                    r /= cnt; g /= cnt; b /= cnt;
                    outp[k * 256 + z * 16 + x] = (ushort)(((int)Math.Floor(r * 31 / 255 + 0.5) << 11) | ((int)Math.Floor(g * 63 / 255 + 0.5) << 5) | (int)Math.Floor(b * 31 / 255 + 0.5));
                }
            }
            if (complete) c.tints = outp;
            return outp;
        }
    }
}
