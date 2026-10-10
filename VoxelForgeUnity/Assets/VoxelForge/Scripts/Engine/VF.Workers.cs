// Voxel Forge — Unity port. Generation / mesh worker engine.
// Web Workers become dedicated background threads; each owns its own WorldGenKernel / MeshCore.
// Results are posted to a concurrent mailbox and integrated on the main thread exactly where the
// reference processed worker onmessage events (between frames), so ordering semantics are kept.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace VoxelForge
{
    public sealed partial class Chunk
    {
        public SectionGeo[] sectionGeo;
        public int sectionGeoEpoch = 1, meshChunkToken;
        public bool meshBuilt, meshComplete;
        public int tris;
        public double last;
    }

    sealed class GenSlot
    {
        public Thread th; public volatile bool ready, stop; public bool busy; public GenJob job;
        public readonly AutoResetEvent ev = new AutoResetEvent(false); public GenJob mailbox;
    }
    sealed class MeshSlot
    {
        public Thread th; public volatile bool ready, stop; public bool busy; public MeshJob job; public bool alive;
        public readonly AutoResetEvent ev = new AutoResetEvent(false); public MeshJob mailbox;
    }
    sealed class WorkerMsg { public int kind; public object slot; public GenResult gen; public MeshResult mesh; public Exception err; public bool ready; }

    public static partial class VF
    {
        static bool workerEngineStarted = false, workerEngineFailed = false;
        static volatile bool workerShutdown = false;
        static int genJobSeq = 1, meshJobSeq = 1;
        static readonly List<GenSlot> genSlots = new List<GenSlot>();
        static readonly List<MeshSlot> meshSlots = new List<MeshSlot>();
        public static readonly List<GenJob> genJobs = new List<GenJob>();
        public static readonly List<MeshJob> meshJobs = new List<MeshJob>();
        static readonly Queue<GenResult> genDone = new Queue<GenResult>();
        static readonly Queue<MeshResult> meshDone = new Queue<MeshResult>();
        public static readonly HashSet<string> genPending = new HashSet<string>(), meshPending = new HashSet<string>();
        static readonly ConcurrentQueue<WorkerMsg> workerInbox = new ConcurrentQueue<WorkerMsg>();
        public static int genDoneCount() { return genDone.Count; }
        public static int meshDoneCount() { return meshDone.Count; }
        public static GenResult takeGenDone() { return genDone.Count > 0 ? genDone.Dequeue() : null; }
        public static MeshResult takeMeshDone() { return meshDone.Count > 0 ? meshDone.Dequeue() : null; }

        public const int SECTION_GEO_KEEP_RADIUS = 2;
        public static readonly int[][] MESH_CARDINAL_DIRS = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
        public static double avgGenWorkerMs = 0, avgMeshWorkerMs = 0;
        public static int meshPartialJobs = 0, meshFullJobs = 0, meshAggregateUploads = 0, integrityGuardFrames = 0;
        static int nextMeshChunkToken = 1;
        public static MDef[] workerBlockCfg;
        public static Dictionary<string, MDef> workerVirtualCfg;
        public static MeshCore mainMeshCore;

        public static bool discardSectionGeo(Chunk c)
        {
            if (c == null || c.sectionGeo == null) return false;
            c.sectionGeo = null; c.sectionGeoEpoch++;
            return true;
        }

        // ---------------- mesher block descriptors ----------------
        static MDef mdefFromBlock(BlockDef b)
        {
            if (b == null) return null;
            return new MDef
            {
                side = b.side, top = b.top, bottom = b.bottom, front = b.front, frontL = b.frontL, frontR = b.frontR, topL = b.topL, topR = b.topR, corner = b.corner,
                solid = b.solid, transparent = b.transparent, alpha = b.alpha, cutout = b.cutout, plant = b.plant, waterPlant = b.waterPlant, needsWater = b.needsWater,
                special = b.special, light = b.light,
            };
        }
        static MDef mdefFromVirtual(VirtualDef v)
        {
            return new MDef
            {
                side = v.side, top = v.top, bottom = v.bottom, partTop = v.hasPartTop ? v.partTop : -1, partBottom = v.hasPartBottom ? v.partBottom : -1,
                solid = v.solid, transparent = v.transparent, cutout = v.cutout, plant = v.plant, waterPlant = v.waterPlant, needsWater = v.needsWater,
                axislog = v.axislog, isVirtual = true, shape = v.shape, light = v.light,
            };
        }
        public static void buildMesherConfig()
        {
            workerBlockCfg = new MDef[blocks.Length];
            for (int i = 0; i < blocks.Length; i++) workerBlockCfg[i] = mdefFromBlock(blocks[i]);
            workerVirtualCfg = new Dictionary<string, MDef>();
            foreach (var kv in VIRTUAL_BLOCKS) workerVirtualCfg[kv.Key] = mdefFromVirtual(kv.Value);
        }

        static void failWorkerEngine(string err)
        {
            if (workerEngineFailed) return;
            workerEngineFailed = true;
            UnityEngine.Debug.LogError("Generation worker engine stopped " + (err ?? ""));
            genSlots.Clear(); genJobs.Clear(); genPending.Clear();
            lastStreamCx = 999; lastStreamCz = 999;
            try { toast("Ошибка generation worker."); } catch { }
        }
        static void restoreFailedMeshJob(MeshJob job)
        {
            if (job == null) return;
            meshPending.Remove(job.key ?? ckey(job.cx, job.cz));
            var c = chunkFastGet(job.cx, job.cz);
            if (c == null || c.meshChunkToken != job.chunkToken) return;
            c.dirty = true; c.fullMeshDirty = true;
            if (c.dirtySections != null) c.dirtySections.Clear();
            c.meshRev++;
        }
        static bool meshWorkerReady() { foreach (var s in meshSlots) if (s.alive && s.ready) return true; return false; }
        static void recoverQueuedMeshesForSync()
        {
            if (meshJobs.Count == 0) return;
            var a = meshJobs.ToArray(); meshJobs.Clear();
            foreach (var j in a) restoreFailedMeshJob(j);
        }

        static void genThreadMain(GenSlot slot, int seed, BlockDef[] defs)
        {
            GenWorker w;
            try { w = new GenWorker(seed, defs); }
            catch (Exception e) { workerInbox.Enqueue(new WorkerMsg { kind = 0, slot = slot, err = e }); return; }
            workerInbox.Enqueue(new WorkerMsg { kind = 0, slot = slot, ready = true });
            while (!slot.stop)
            {
                slot.ev.WaitOne();
                if (slot.stop) break;
                var j = Interlocked.Exchange(ref slot.mailbox, null);
                if (j == null) continue;
                try { workerInbox.Enqueue(new WorkerMsg { kind = 0, slot = slot, gen = w.Run(j) }); }
                catch (Exception e) { workerInbox.Enqueue(new WorkerMsg { kind = 0, slot = slot, err = e }); }
            }
        }
        static void meshThreadMain(MeshSlot slot)
        {
            MeshCore core;
            try { core = new MeshCore(workerBlockCfg, workerVirtualCfg); }
            catch (Exception e) { workerInbox.Enqueue(new WorkerMsg { kind = 1, slot = slot, err = e }); return; }
            workerInbox.Enqueue(new WorkerMsg { kind = 1, slot = slot, ready = true });
            while (!slot.stop)
            {
                slot.ev.WaitOne();
                if (slot.stop) break;
                var j = Interlocked.Exchange(ref slot.mailbox, null);
                if (j == null) continue;
                try { workerInbox.Enqueue(new WorkerMsg { kind = 1, slot = slot, mesh = core.run(j) }); }
                catch (Exception e) { workerInbox.Enqueue(new WorkerMsg { kind = 1, slot = slot, err = e }); }
            }
        }

        public static void initWorkerEngine()
        {
            if (workerEngineStarted) return;
            workerEngineStarted = true; workerShutdown = false;
            buildMesherConfig();
            mainMeshCore = new MeshCore(workerBlockCfg, workerVirtualCfg);
            GenData.Ensure();
            try
            {
                int seed = WORLD_SEED; var defs = blocks;
                for (int i = 0; i < GEN_WORKER_COUNT; i++)
                {
                    var slot = new GenSlot();
                    slot.th = new Thread(() => genThreadMain(slot, seed, defs)) { IsBackground = true, Name = "VF-Gen" + i, Priority = ThreadPriority.BelowNormal };
                    genSlots.Add(slot);
                    slot.th.Start();
                }
                for (int i = 0; i < MESH_WORKER_COUNT; i++)
                {
                    var slot = new MeshSlot { alive = true };
                    slot.th = new Thread(() => meshThreadMain(slot)) { IsBackground = true, Name = "VF-Mesh" + i, Priority = ThreadPriority.BelowNormal };
                    meshSlots.Add(slot);
                    slot.th.Start();
                }
            }
            catch (Exception e) { failWorkerEngine(e.Message); }
        }
        public static void shutdownWorkerEngine()
        {
            workerShutdown = true;
            foreach (var s in genSlots) { s.stop = true; s.ev.Set(); }
            foreach (var s in meshSlots) { s.stop = true; s.ev.Set(); }
            genSlots.Clear(); meshSlots.Clear();
            genJobs.Clear(); meshJobs.Clear(); genPending.Clear(); meshPending.Clear();
            genDone.Clear(); meshDone.Clear();
            WorkerMsg dummy; while (workerInbox.TryDequeue(out dummy)) { }
            workerEngineStarted = false; workerEngineFailed = false;
        }

        /// <summary>Equivalent of the worker onmessage handlers; called on the main thread every frame.</summary>
        public static void drainWorkerMessages()
        {
            WorkerMsg m;
            while (workerInbox.TryDequeue(out m))
            {
                if (m.kind == 0)
                {
                    var slot = (GenSlot)m.slot;
                    if (!genSlots.Contains(slot)) continue;
                    if (m.err != null) { failWorkerEngine("generation worker failed: " + m.err); continue; }
                    if (m.ready) { slot.ready = true; pumpGenWorkers(); continue; }
                    slot.busy = false; slot.job = null;
                    var r = m.gen;
                    genPending.Remove(ckey(r.cx, r.cz));
                    avgGenWorkerMs = avgGenWorkerMs != 0 ? avgGenWorkerMs * 0.9 + r.ms * 0.1 : r.ms;
                    genDone.Enqueue(r);
                    pumpGenWorkers();
                }
                else
                {
                    var slot = (MeshSlot)m.slot;
                    if (!meshSlots.Contains(slot) || !slot.alive) continue;
                    if (m.err != null)
                    {
                        var job = slot.job; slot.job = null; slot.busy = false; slot.ready = false; slot.alive = false;
                        restoreFailedMeshJob(job);
                        UnityEngine.Debug.LogWarning("Mesh worker unavailable; shared-core synchronous path will be used " + m.err);
                        bool any = false; foreach (var s in meshSlots) if (s.alive) any = true;
                        if (!any) recoverQueuedMeshesForSync();
                        continue;
                    }
                    if (m.ready) { slot.ready = true; pumpMeshWorkers(); continue; }
                    var jb = slot.job; slot.busy = false; slot.job = null;
                    var res = m.mesh;
                    if (jb != null) { res.ctx = jb.ctx; res.geoEpoch = jb.geoEpoch; res.chunkToken = jb.chunkToken; res.initial = jb.initial; }
                    meshPending.Remove(ckey(res.cx, res.cz));
                    avgMeshWorkerMs = avgMeshWorkerMs != 0 ? avgMeshWorkerMs * 0.9 + res.ms * 0.1 : res.ms;
                    meshDone.Enqueue(res);
                    pumpMeshWorkers();
                }
            }
        }

        public static void pumpGenWorkers()
        {
            if (workerEngineFailed) return;
            foreach (var s in genSlots)
            {
                if (!s.ready || s.busy || genJobs.Count == 0) continue;
                var j = genJobs[0]; genJobs.RemoveAt(0);
                s.busy = true; s.job = j;
                j.seed = WORLD_SEED;
                s.mailbox = j; s.ev.Set();
            }
        }
        public static void pumpMeshWorkers()
        {
            foreach (var slot in meshSlots)
            {
                if (!slot.alive || !slot.ready || slot.busy || meshJobs.Count == 0) continue;
                var j = meshJobs[0]; meshJobs.RemoveAt(0);
                slot.busy = true; slot.job = j;
                slot.mailbox = j; slot.ev.Set();
            }
        }
        public static int busyGenSlots() { int n = 0; foreach (var s in genSlots) if (s.busy) n++; return n; }
        public static int busyMeshSlots() { int n = 0; foreach (var s in meshSlots) if (s.busy) n++; return n; }
        public static IEnumerable<GenJob> busyGenJobs() { foreach (var s in genSlots) if (s.busy && s.job != null) yield return s.job; }
        public static IEnumerable<MeshJob> busyMeshJobs() { foreach (var s in meshSlots) if (s.busy && s.job != null) yield return s.job; }

        public static bool queueChunkGeneration(int cx, int cz)
        {
            var k = ckey(cx, cz);
            if (chunks.Has(k) || genPending.Contains(k)) return false;
            var es = editsByChunk.Get(k);
            var delta = new List<int[]>();
            if (es != null) foreach (var kv in es) delta.Add((int[])kv.Value.Clone());
            genPending.Add(k);
            genJobs.Add(new GenJob { id = genJobSeq++, cx = cx, cz = cz, key = k, delta = delta, seed = WORLD_SEED });
            pumpGenWorkers();
            return true;
        }

        static bool sectionHasBlocks(Section sec)
        {
            if (sec == null || sec.blocks == null) return false;
            var b = sec.blocks;
            for (int i = 0; i < b.Length; i++) if (b[i] != B.AIR) return true;
            return false;
        }
        static int[] meshTargetSections(Chunk c, bool full)
        {
            var a = new List<int>();
            if (!full && c.sectionGeo != null && c.dirtySections != null && c.dirtySections.Count > 0)
            {
                foreach (var si in c.dirtySections) if (si >= 0 && si < SECTION_COUNT) a.Add(si);
                a.Sort();
                return a.ToArray();
            }
            for (int si = 0; si < SECTION_COUNT; si++) if (sectionHasBlocks(c.sections[si]) || (c.sectionGeo != null && c.sectionGeo[si] != null)) a.Add(si);
            return a.ToArray();
        }
        static List<SecPayload> meshSectionPayload(Chunk c, int[] sis)
        {
            var outp = new List<SecPayload>();
            if (sis.Length == 0) return outp;
            int centerMask = 0, sideMask = 0;
            foreach (var si in sis)
            {
                if (si < 0 || si >= SECTION_COUNT) continue;
                int bit = 1 << si;
                centerMask |= bit; sideMask |= bit;
                if (si > 0) { centerMask |= 1 << (si - 1); sideMask |= 1 << (si - 1); }
                if (si + 1 < SECTION_COUNT) { centerMask |= 1 << (si + 1); sideMask |= 1 << (si + 1); }
            }
            Action<Chunk, int, int, int> add = (n, dx, dz, mask) =>
            {
                if (n == null) return;
                for (int si = 0; si < SECTION_COUNT; si++)
                {
                    if ((mask & (1 << si)) == 0) continue;
                    var sec = n.sections[si];
                    if (sec == null) continue;
                    var lt = ensureSectionLight(sec);
                    outp.Add(new SecPayload { dx = dx, dz = dz, si = si, blocks = (byte[])sec.blocks.Clone(), light = lt != null ? (byte[])lt.Clone() : new byte[4096] });
                }
            };
            add(c, 0, 0, centerMask);
            foreach (var d in MESH_CARDINAL_DIRS) add(chunkFastGet(c.cx + d[0], c.cz + d[1]), d[0], d[1], sideMask);
            return outp;
        }
        static List<MetaEntry> meshMeta(Chunk c)
        {
            var outp = new List<MetaEntry>();
            Action<int, int, int, int> add = (cx, cz, sx, sz) =>
            {
                var cc = chunkFastGet(cx, cz);
                var ck = cc != null ? cc.key : ckey(cx, cz);
                Dictionary<int, JObj> gm, mm; generatedMetaByChunk.TryGetValue(ck, out gm); metaByChunk.TryGetValue(ck, out mm);
                var em = editsByChunk.Get(ck);
                if (gm != null)
                    foreach (var kv in gm)
                    {
                        if (em != null && em.Has(kv.Key)) continue;
                        int iy = kv.Key >> 8, r = kv.Key & 255;
                        outp.Add(new MetaEntry((r & 15) + sx, iy, (r >> 4) + sz, kv.Value != null ? kv.Value.Clone() : null));
                    }
                if (mm != null)
                    foreach (var kv in mm)
                    {
                        int iy = kv.Key >> 8, r = kv.Key & 255;
                        outp.Add(new MetaEntry((r & 15) + sx, iy, (r >> 4) + sz, kv.Value != null ? kv.Value.Clone() : null));
                    }
            };
            add(c.cx, c.cz, 0, 0);
            foreach (var d in MESH_CARDINAL_DIRS) if (chunkFastGet(c.cx + d[0], c.cz + d[1]) != null) add(c.cx + d[0], c.cz + d[1], d[0] * CHUNK, d[1] * CHUNK);
            return outp;
        }
        static int[] meshNeighborContext(Chunk c)
        {
            var outp = new int[4]; int k = 0;
            foreach (var d in MESH_CARDINAL_DIRS) { var n = chunkFastGet(c.cx + d[0], c.cz + d[1]); outp[k++] = n != null ? n.boundaryRev : -1; }
            return outp;
        }
        static bool meshNeighborContextValid(Chunk c, int[] ctx)
        {
            if (ctx == null || ctx.Length != 4) return true;
            int k = 0;
            foreach (var d in MESH_CARDINAL_DIRS)
            {
                var n = chunkFastGet(c.cx + d[0], c.cz + d[1]);
                int rev = n != null ? n.boundaryRev : -1;
                if (rev != ctx[k++]) return false;
            }
            return true;
        }
        static bool runMeshJobSync(MeshJob job)
        {
            if (mainMeshCore == null) return false;
            var outp = mainMeshCore.run(job);
            outp.ctx = job.ctx; outp.geoEpoch = job.geoEpoch; outp.chunkToken = job.chunkToken; outp.initial = job.initial;
            meshPending.Remove(job.key);
            avgMeshWorkerMs = avgMeshWorkerMs != 0 ? avgMeshWorkerMs * 0.9 + outp.ms * 0.1 : outp.ms;
            integrateMeshResult(outp);
            return true;
        }
        public static bool queueChunkMesh(Chunk c, bool forceSync = false)
        {
            var k = ckey(c.cx, c.cz);
            if (meshPending.Contains(k) || !c.dirty) return false;
            int pcx = JS.floor(player.x / CHUNK), pcz = JS.floor(player.z / CHUNK), dist = Math.Max(Math.Abs(c.cx - pcx), Math.Abs(c.cz - pcz));
            if (c.sectionGeo != null && !c.fullMeshDirty && dist > SECTION_GEO_KEEP_RADIUS && !(c.dirtySections != null && c.dirtySections.Count > 0)) discardSectionGeo(c);
            bool full = c.sectionGeo == null || c.fullMeshDirty;
            var sis = meshTargetSections(c, full);
            var job = new MeshJob
            {
                id = meshJobSeq++, cx = c.cx, cz = c.cz, rev = c.meshRev, key = k, sections = meshSectionPayload(c, sis), meta = meshMeta(c), sis = sis, full = full,
                ctx = meshNeighborContext(c), geoEpoch = c.sectionGeoEpoch, chunkToken = c.meshChunkToken, initial = !c.meshBuilt,
            };
            meshPending.Add(k);
            c.dirty = false;
            if (full) { c.fullMeshDirty = false; if (c.dirtySections != null) c.dirtySections.Clear(); }
            else foreach (var si in sis) c.dirtySections.Remove(si);
            if (full) meshFullJobs++; else meshPartialJobs++;
            if (forceSync || !meshWorkerReady())
            {
                if (runMeshJobSync(job)) return true;
                restoreFailedMeshJob(job);
                return false;
            }
            meshJobs.Add(job);
            pumpMeshWorkers();
            return true;
        }
        static bool runDirtyEditMeshSync(Chunk c, string k)
        {
            if (mainMeshCore == null) return false;
            var requested = takeDirtyEditSections(k);
            bool workerReady = meshWorkerReady();
            bool full = !(workerReady && requested != null && requested.Count > 0 && c.sectionGeo != null);
            int[] sis;
            if (full) sis = meshTargetSections(c, true);
            else { var l = new List<int>(); foreach (var si in requested) if (si >= 0 && si < SECTION_COUNT) l.Add(si); l.Sort(); sis = l.ToArray(); }
            if (sis.Length == 0) { clearDirtyEdit(k); return false; }
            bool hadPending = meshPending.Contains(k);
            var job = new MeshJob
            {
                id = meshJobSeq++, cx = c.cx, cz = c.cz, rev = c.meshRev, key = k, sections = meshSectionPayload(c, sis), meta = meshMeta(c), sis = sis, full = full,
                ctx = meshNeighborContext(c), geoEpoch = c.sectionGeoEpoch, chunkToken = c.meshChunkToken, initial = !c.meshBuilt,
            };
            var outp = mainMeshCore.run(job);
            outp.ctx = job.ctx; outp.geoEpoch = job.geoEpoch; outp.chunkToken = job.chunkToken; outp.initial = job.initial;
            if (full) meshFullJobs++; else meshPartialJobs++;
            integrateMeshResult(outp);
            // main keeps the normal dirty flag after an instant partial edit; a stale worker result
            // is therefore never allowed to become authoritative.
            if (!full) { c.dirty = true; c.fullMeshDirty = true; }
            else if (hadPending) { c.dirty = true; c.fullMeshDirty = true; }
            clearDirtyEdit(k);
            return true;
        }
        public static int processDirtyEditMeshes(int pcx, int pcz, double deadline, int maxBuilds = 4)
        {
            if (dirtyEditChunks.Count == 0 || mainMeshCore == null) return 0;
            var picks = new List<KeyValuePair<int, KeyValuePair<Chunk, string>>>();
            foreach (var k in new List<string>(dirtyEditChunks))
            {
                var c = chunks.Get(k);
                if (c == null) { clearDirtyEdit(k); continue; }
                if (!renderChunkTargeted(c.cx, c.cz, pcx, pcz) || !meshNeighborhoodReady(c)) continue;
                int dx = c.cx - pcx, dz = c.cz - pcz;
                picks.Add(new KeyValuePair<int, KeyValuePair<Chunk, string>>(dx * dx + dz * dz, new KeyValuePair<Chunk, string>(c, k)));
            }
            picks = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(picks, p => p.Key));
            int built = 0;
            for (int i = 0; i < picks.Count && built < maxBuilds; i++)
            {
                if (!double.IsInfinity(deadline) && built > 0 && JS.now() > deadline - 1.5) break;
                if (runDirtyEditMeshSync(picks[i].Value.Key, picks[i].Value.Value)) built++;
            }
            return built;
        }
        public static void integrateGenResult(GenResult m)
        {
            var k = ckey(m.cx, m.cz);
            if (chunks.Has(k)) return;
            installGeneratedMeta(m.cx, m.cz, m.meta ?? new List<GenMetaEntry>());
            var sections = emptySections();
            if (m.sections != null)
                foreach (var q in m.sections)
                    if (q != null && q.si >= 0 && q.si < SECTION_COUNT)
                    {
                        var light = q.light;
                        if (light == null) { light = new byte[4096]; for (int i = 0; i < 4096; i++) light[i] = 0xf0; }
                        sections[q.si] = new Section { blocks = q.blocks, light = light };
                    }
            createChunkFromData(m.cx, m.cz, sections, m.sim, m.fluidBoundary);
        }

        // ---------------- mesh result integration ----------------
        public static void integrateMeshResult(MeshResult m)
        {
            var c = chunkFastGet(m.cx, m.cz);
            if (c == null) return;
            if (m.chunkToken != c.meshChunkToken) { c.dirty = true; c.fullMeshDirty = true; return; }
            if (!meshNeighborContextValid(c, m.ctx) || c.meshRev != m.rev)
            {
                c.dirty = true;
                if (m.full) c.fullMeshDirty = true;
                else { var ds = c.dirtySections ?? (c.dirtySections = new HashSet<int>()); if (m.sis != null) foreach (var si in m.sis) ds.Add(si); }
                return;
            }
            if (!m.full && (c.sectionGeo == null || m.geoEpoch != c.sectionGeoEpoch))
            {
                c.dirty = true; c.fullMeshDirty = true;
                if (c.dirtySections != null) c.dirtySections.Clear();
                integrityGuardFrames = Math.Max(integrityGuardFrames, 12);
                return;
            }
            if (m.full)
            {
                c.sectionGeoEpoch++;
                meshAggregateUploads++;
                c.sectionGeo = m.full24 ?? new SectionGeo[SECTION_COUNT];
                recomputeChunkMeshBounds(c);
                uploadChunkBuckets(c, m.aggO ?? GeoBucket.Empty, m.aggC ?? GeoBucket.Empty, m.aggW ?? GeoBucket.Empty, m.aggT ?? GeoBucket.Empty, true, true, true, true);
                c.fullMeshDirty = false;
                if (c.dirtySections != null) c.dirtySections.Clear();
                c.meshComplete = true;
                c.dirty = false;
                return;
            }
            bool opaqueChanged = false, cutoutChanged = false, waterChanged = false, transChanged = false;
            if (m.parts != null)
                foreach (var p in m.parts)
                {
                    int si = p.Key;
                    if (si < 0 || si >= SECTION_COUNT) continue;
                    var old = c.sectionGeo[si]; var q = p.Value;
                    if (!opaqueChanged && !bucketEqual(old != null ? old.o : null, q.o)) opaqueChanged = true;
                    if (!cutoutChanged && !bucketEqual(old != null ? old.c : null, q.c)) cutoutChanged = true;
                    if (!waterChanged && !bucketEqual(old != null ? old.w : null, q.w)) waterChanged = true;
                    if (!transChanged && !bucketEqual(old != null ? old.t : null, q.t)) transChanged = true;
                    c.sectionGeo[si] = q.Any ? q : null;
                }
            GeoBucket o = null, a = null, w = null, t = null;
            if (opaqueChanged) o = combineSectionBucket(c.sectionGeo, 0);
            if (cutoutChanged) a = combineSectionBucket(c.sectionGeo, 1);
            if (waterChanged) w = combineSectionBucket(c.sectionGeo, 2);
            if (transChanged) t = combineSectionBucket(c.sectionGeo, 3);
            recomputeChunkMeshBounds(c);
            uploadChunkBuckets(c, o, a, w, t, opaqueChanged, cutoutChanged, waterChanged, transChanged);
            c.dirty = c.fullMeshDirty || (c.dirtySections != null && c.dirtySections.Count > 0);
        }
        static bool bucketEqual(GeoBucket a, GeoBucket b)
        {
            int an = a != null ? a.VCount : 0, bn = b != null ? b.VCount : 0;
            if (an != bn) return false;
            for (int i = 0; i < an; i++)
            {
                var p = a.v[i]; var q = b.v[i];
                if (p.x != q.x || p.y != q.y || p.z != q.z || p.u != q.u || p.v != q.v || p.sa != q.sa || p.tile != q.tile || p.light != q.light) return false;
            }
            int ain = a != null ? a.ICount : 0, bin = b != null ? b.ICount : 0;
            if (ain != bin) return false;
            for (int i = 0; i < ain; i++) if (a.i[i] != b.i[i]) return false;
            return true;
        }
        static readonly List<GeoBucket> combineScratch = new List<GeoBucket>();
        static GeoBucket combineSectionBucket(SectionGeo[] g, int k)
        {
            combineScratch.Clear();
            if (g != null) foreach (var s in g) if (s != null) combineScratch.Add(s.Get(k));
            return MeshCore.concat(combineScratch);
        }
        static void recomputeChunkMeshBounds(Chunk c)
        {
            var g = c.sectionGeo;
            int lo = SECTION_COUNT, hi = -1;
            var blo = new int[] { SECTION_COUNT, SECTION_COUNT, SECTION_COUNT, SECTION_COUNT }; var bhi = new int[] { -1, -1, -1, -1 };
            if (g != null)
                for (int si = 0; si < g.Length; si++)
                {
                    var q = g[si]; if (q == null) continue;
                    bool any = false;
                    for (int k = 0; k < 4; k++)
                        if (q.Get(k).ICount > 0) { any = true; if (si < blo[k]) blo[k] = si; if (si > bhi[k]) bhi[k] = si; }
                    if (any) { if (si < lo) lo = si; if (si > hi) hi = si; }
                }
            if (hi >= 0) { c.meshMinY = WORLD_MIN_Y + lo * SECTION; c.meshMaxY = Math.Min(WORLD_MAX_Y, WORLD_MIN_Y + (hi + 1) * SECTION); }
            else { c.meshMinY = double.NaN; c.meshMaxY = double.NaN; }
            for (int k = 0; k < 4; k++)
            {
                c.bucketMinY[k] = bhi[k] >= 0 ? WORLD_MIN_Y + blo[k] * SECTION : double.NaN;
                c.bucketMaxY[k] = bhi[k] >= 0 ? Math.Min(WORLD_MAX_Y, WORLD_MIN_Y + (bhi[k] + 1) * SECTION) : double.NaN;
            }
        }

        // ---------------- chunk lifecycle ----------------
        public static Chunk createChunkFromData(int cx, int cz, Section[] sections, uint[] simData = null, uint[] boundaryData = null)
        {
            var key = ckey(cx, cz);
            var c = new Chunk
            {
                cx = cx, cz = cz, key = key, sections = sections ?? emptySections(), dirty = true, fullMeshDirty = true, dirtySections = new HashSet<int>(),
                sectionGeo = null, sectionGeoEpoch = 1, meshChunkToken = nextMeshChunkToken++, meshBuilt = false, meshComplete = false, meshRev = 0, boundaryRev = 1, mapRev = 0,
                last = JS.now(), fluidBoundary = null,
            };
            chunks.Set(key, c);
            chunkFastSet(c);
            stitchChunkLight(c);
            installChunkSimulationSeeds(c, simData, boundaryData);
            installMetadataSimulationSeeds(c);
            spawnChunkMobs(cx, cz);
            var restored = restoreStoredMobs(cx, cz);
            foreach (var m in restored) attachLiveMob(m, c);
            markDirty(cx + 1, cz); markDirty(cx - 1, cz); markDirty(cx, cz + 1); markDirty(cx, cz - 1);
            return c;
        }
        public static void deleteChunk(Chunk c)
        {
            storeChunkMobs(c);
            releaseChunkGpu(c);
            var k = c.key ?? ckey(c.cx, c.cz);
            clearDirtyEdit(k);
            chunks.Delete(k);
            generatedMetaByChunk.Remove(k);
            chunkFastDelete(c);
            minimapCache.Remove(k);
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (dx != 0 || dz != 0) markDirty(c.cx + dx, c.cz + dz);
        }
    }
}
