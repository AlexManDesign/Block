// Voxel Forge — Unity port. Chunk streaming, startup spawn resolution, frustum/visibility, matrices, toast.
using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxelForge
{
    public static partial class VF
    {
        // ---------------- startup spawn ----------------
        public static void primeInitialSpawnChunks(double centerX, double centerZ)
        {
            initialSpawnCX = JS.floor(centerX / CHUNK); initialSpawnCZ = JS.floor(centerZ / CHUNK);
            queue.Clear();
            for (int dz = -INITIAL_GEN_RADIUS; dz <= INITIAL_GEN_RADIUS; dz++)
                for (int dx = -INITIAL_GEN_RADIUS; dx <= INITIAL_GEN_RADIUS; dx++)
                {
                    int cx = initialSpawnCX + dx, cz = initialSpawnCZ + dz; var k = ckey(cx, cz);
                    if (!chunks.Has(k) && !genPending.Contains(k)) queue.Add(new[] { cx, cz, dx * dx + dz * dz });
                }
            var sorted = queue.OrderBy(q => q[2]).ToList(); queue.Clear(); queue.AddRange(sorted);
            lastStreamCx = initialSpawnCX; lastStreamCz = initialSpawnCZ;
        }
        static bool initialSpawnChunksReady()
        {
            for (int dz = -INITIAL_GEN_RADIUS; dz <= INITIAL_GEN_RADIUS; dz++)
                for (int dx = -INITIAL_GEN_RADIUS; dx <= INITIAL_GEN_RADIUS; dx++)
                    if (chunkFastGet(initialSpawnCX + dx, initialSpawnCZ + dz) == null) return false;
            int meshR = Math.Max(1, INITIAL_GEN_RADIUS - 1);
            for (int dz = -meshR; dz <= meshR; dz++)
                for (int dx = -meshR; dx <= meshR; dx++)
                {
                    var c = chunkFastGet(initialSpawnCX + dx, initialSpawnCZ + dz);
                    if (c == null || !c.meshBuilt || !c.meshComplete) return false;
                }
            return true;
        }
        // HUD / start-screen visibility flags (DOM classes in the reference)
        public static bool startScreenVisible = true, hudVisible = false;
        public static bool finishInitialSpawnIfReady()
        {
            if ((!initialSpawnPending && !resumeLoadPending) || !initialSpawnChunksReady()) return false;
            bool resumed = resumeLoadPending;
            double[] safe = null; bool copiedSpawnStart = false;
            if (initialSpawnPending)
            {
                if (worldSpawnOverride != null)
                {
                    copiedSpawnStart = true;
                    var q = worldSpawnOverride;
                    safe = startupPositionValidLoaded(q[0], q[1], q[2], false) ? (double[])q.Clone() : findSafeWorldSpawnNear(q[0], q[1], q[2], 16);
                    worldSpawnOverride = null;
                }
                if (safe == null) safe = resolveInitialSpawnLoaded(SPAWN_ANCHOR[0], SPAWN_ANCHOR[1]);
                if (safe == null)
                {
                    var next = findLandSpawnAnchor((int)SPAWN_ANCHOR[0] + 65, (int)SPAWN_ANCHOR[1]) ?? findLandSpawnAnchor(0, 0);
                    if (next == null) return false;
                    SPAWN_ANCHOR = new double[] { next[0], next[1] };
                    primeInitialSpawnChunks(next[0], next[1]);
                    pumpGenWorkers();
                    return false;
                }
                player.x = safe[0]; player.y = safe[1]; player.z = safe[2];
                player.spawn = (double[])safe.Clone(); SPAWN = (double[])safe.Clone();
            }
            else
            {
                // A saved rider may legitimately resume over water/rails. Ordinary saves still require dry support.
                var rv = player.riding;
                bool validRide = rv != null && vehicles.Contains(rv) && JS.isFinite(rv.x) && JS.isFinite(rv.y) && JS.isFinite(rv.z);
                if (validRide) { player.x = rv.x; player.z = rv.z; player.y = rv.y + (rv.kind == "minecart" ? -0.15 : -0.45); }
                bool valid = validRide || startupPositionValidLoaded(player.x, player.y, player.z, false);
                if (!valid)
                {
                    int fx = JS.isFinite(player.x) ? JS.floor(player.x) : initialSpawnCX * CHUNK + 8, fz = JS.isFinite(player.z) ? JS.floor(player.z) : initialSpawnCZ * CHUNK + 8;
                    safe = resolveInitialSpawnLoaded(fx, fz);
                    if (safe == null)
                    {
                        var next = findLandSpawnAnchor(fx, fz) ?? findLandSpawnAnchor(0, 0);
                        if (next == null) return false;
                        SPAWN_ANCHOR = new double[] { next[0], next[1] };
                        resumeLoadPending = true;
                        primeInitialSpawnChunks(next[0], next[1]);
                        pumpGenWorkers();
                        return false;
                    }
                    player.x = safe[0]; player.y = safe[1]; player.z = safe[2];
                    // Repair an invalid old spawn point as well, otherwise death would send the player back to water.
                    player.spawn = (double[])safe.Clone(); SPAWN = (double[])safe.Clone();
                }
            }
            bool mustBeNatural = !copiedSpawnStart && (initialSpawnPending || safe != null);
            if (player.riding == null && !startupPositionValidLoaded(player.x, player.y, player.z, mustBeNatural)) return false;
            player.vx = player.vy = player.vz = 0;
            player.lastSurvY = player.y;
            initialSpawnPending = false; resumeLoadPending = false; worldReady = true;
            if (pendingSavedDrop != null) { spawnWorldDrop(pendingSavedDrop.key, pendingSavedDrop.count, player.x, player.y + 1, player.z, pendingSavedDrop.dur); pendingSavedDrop = null; }
            lastStreamCx = 999; lastStreamCz = 999;
            queue.Clear();
            refreshQueue();
            startScreenVisible = false; hudVisible = true;
            drawHotbar();
            updateVitals();
            syncTouchControls();
            touchWorldRecord(true);
            saveGameNow();
            if (player.dead && !player.creative)
            {
                deathReasonText = "Причина: " + (string.IsNullOrEmpty(player.deathReason) ? "неизвестно" : player.deathReason);
                deathUIVisible = true;
                toast("Сначала возродитесь");
            }
            else toast((player.creative ? "Creator: бесконечные ресурсы и полёт" : "Survival: добывайте ресурсы и выживайте") + " · seed " + (uint)WORLD_SEED + (resumed ? " · spawn/позиция проверены" : " · сухой spawn проверен"));
            return true;
        }

        // ---------------- distances ----------------
        public static int renderDistance = DEFAULT_RD, simulationDistance = 6, lastStreamCx = 999, lastStreamCz = 999;
        public static readonly List<int[]> queue = new List<int[]>();
        public static double lastStreamCost = 0;
        static int simulationCenterCX = 0, simulationCenterCZ = 0, simulationRadiusNow = 0; static double simulationBlockRadiusNow = 96;
        public static int activeSimulationDistance() { return Math.Max(1, Math.Min(simulationDistance, renderDistance)); }
        public static void refreshSimulationBounds()
        {
            simulationCenterCX = JS.floor(player.x / CHUNK); simulationCenterCZ = JS.floor(player.z / CHUNK);
            simulationRadiusNow = activeSimulationDistance(); simulationBlockRadiusNow = simulationRadiusNow * CHUNK;
        }
        public static bool simulationChunkActive(int cx, int cz) { return Math.Abs(cx - simulationCenterCX) <= simulationRadiusNow && Math.Abs(cz - simulationCenterCZ) <= simulationRadiusNow; }
        public static bool simulationBlockActive(double x, double z) { return Math.Abs(x - player.x) <= simulationBlockRadiusNow && Math.Abs(z - player.z) <= simulationBlockRadiusNow; }
        static int streamLeadChunks = 1, streamCleanupTick = 0;
        public static double avgGenIntegrateMs = 5, avgMeshIntegrateMs = 0.8, avgMeshQueueMs = 0.45, avgForegroundCpuMs = 6, lastBackgroundCpuMs = 0, lastFrameCpuMs = 0;
        public static bool renderChunkTargeted(int cx, int cz, int pcx, int pcz, int extra = 0) { return Math.Max(Math.Abs(cx - pcx), Math.Abs(cz - pcz)) <= renderDistance + extra; }
        static bool genChunkTargeted(int cx, int cz, int pcx, int pcz, int extra = 0) { return Math.Max(Math.Abs(cx - pcx), Math.Abs(cz - pcz)) <= renderDistance + 1 + extra; }
        static int streamPriority(int cx, int cz, int pcx, int pcz)
        {
            int dx = cx - pcx, dz = cz - pcz, ax = Math.Abs(dx), az = Math.Abs(dz), d = dx * dx + dz * dz, r = Math.Max(ax, az);
            if (r <= renderDistance) return d;
            if (r == renderDistance + 1)
            {
                if (ax == r && az == r) return 1000000 + d;
                int qx = Math.Min(ax, renderDistance), qz = Math.Min(az, renderDistance);
                return qx * qx + qz * qz + renderDistance * 2 + 1;
            }
            return 2000000 + d;
        }
        public static bool meshNeighborhoodReady(Chunk c) { foreach (var d in MESH_CARDINAL_DIRS) if (chunkFastGet(c.cx + d[0], c.cz + d[1]) == null) return false; return true; }
        public static bool lightNeighborhoodReady(Chunk c)
        {
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if ((dx != 0 || dz != 0) && chunkFastGet(c.cx + dx, c.cz + dz) == null) return false;
            return true;
        }
        static void prioritizeWorkerQueues(int pcx, int pcz)
        {
            if (genJobs.Count > 1) { var s = genJobs.OrderBy(a => streamPriority(a.cx, a.cz, pcx, pcz)).ToList(); genJobs.Clear(); genJobs.AddRange(s); }
            if (meshJobs.Count > 1) { var s = meshJobs.OrderBy(a => streamPriority(a.cx, a.cz, pcx, pcz)).ToList(); meshJobs.Clear(); meshJobs.AddRange(s); }
        }
        static int[] genPickDist = new int[0], genPickCX = new int[0], genPickCZ = new int[0];
        static void fillGenJobsMain(int pcx, int pcz, double deadline)
        {
            int need = GEN_QUEUE_LIMIT - genPending.Count;
            if (need <= 0) return;
            if (genPickDist.Length < GEN_QUEUE_LIMIT) { genPickDist = new int[GEN_QUEUE_LIMIT]; genPickCX = new int[GEN_QUEUE_LIMIT]; genPickCZ = new int[GEN_QUEUE_LIMIT]; }
            int count = 0, r = renderDistance + 1;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = pcx + dx, cz = pcz + dz;
                    if (chunkFastGet(cx, cz) != null) continue;
                    if (genPending.Contains(ckey(cx, cz))) continue;
                    int d = streamPriority(cx, cz, pcx, pcz);
                    int pos = count < need ? count : need - 1;
                    if (count < need) count++;
                    else if (d >= genPickDist[need - 1]) continue;
                    while (pos > 0 && d < genPickDist[pos - 1]) { genPickDist[pos] = genPickDist[pos - 1]; genPickCX[pos] = genPickCX[pos - 1]; genPickCZ[pos] = genPickCZ[pos - 1]; pos--; }
                    genPickDist[pos] = d; genPickCX[pos] = cx; genPickCZ[pos] = cz;
                }
            for (int i = 0; i < count && genPending.Count < GEN_QUEUE_LIMIT; i++)
            {
                if (!double.IsInfinity(deadline) && JS.now() > deadline - 1) break;
                queueChunkGeneration(genPickCX[i], genPickCZ[i]);
            }
            prioritizeWorkerQueues(pcx, pcz);
            pumpGenWorkers();
        }
        static void rebuildGenQueue(int pcx, int pcz)
        {
            queue.Clear();
            int r = renderDistance + 1;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = pcx + dx, cz = pcz + dz; var k = ckey(cx, cz);
                    if (!chunks.Has(k) && !genPending.Contains(k)) queue.Add(new[] { cx, cz, dx * dx + dz * dz });
                }
            var s = queue.OrderBy(q => q[2]).ToList(); queue.Clear(); queue.AddRange(s);
        }
        public static void refreshQueue(bool force = false)
        {
            int pcx = JS.floor(player.x / CHUNK), pcz = JS.floor(player.z / CHUNK);
            prioritizeWorkerQueues(pcx, pcz);
            if (force || pcx != lastStreamCx || pcz != lastStreamCz || queue.Count == 0) { lastStreamCx = pcx; lastStreamCz = pcz; rebuildGenQueue(pcx, pcz); }
            else { var s = queue.OrderBy(a => streamPriority(a[0], a[1], pcx, pcz)).ToList(); queue.Clear(); queue.AddRange(s); }
            streamLeadChunks = 1;
        }
        static IEnumerator<Chunk> streamCleanupIter = null;
        static void cleanupStreamCache(int pcx, int pcz, double deadline = double.PositiveInfinity, int maxItems = 48)
        {
            if (streamCleanupIter == null)
            {
                if (++streamCleanupTick % 30 != 0) return;
                streamCleanupIter = chunks.Values.GetEnumerator();
            }
            int meshKeep = renderDistance + 3, chunkKeep = renderDistance + 8, n = 0;
            while (streamCleanupIter != null && n < maxItems)
            {
                if (!double.IsInfinity(deadline) && JS.now() + 0.15 >= deadline) break;
                if (!streamCleanupIter.MoveNext()) { streamCleanupIter = null; cleanupRenderRegions(pcx, pcz); break; }
                var c = streamCleanupIter.Current;
                int d = Math.Max(Math.Abs(c.cx - pcx), Math.Abs(c.cz - pcz));
                if (d > chunkKeep) { deleteChunk(c); n++; continue; }
                if (d > SECTION_GEO_KEEP_RADIUS && c.sectionGeo != null && !c.dirty && !meshPending.Contains(c.key ?? ckey(c.cx, c.cz))) discardSectionGeo(c);
                if (d > meshKeep && c.meshBuilt)
                {
                    releaseChunkGpu(c);
                    c.meshBuilt = false; c.meshComplete = false; c.dirty = true; c.fullMeshDirty = true;
                }
                n++;
            }
        }
        public static void stream(double frameDeadline, bool interactionHot = false)
        {
            double t0 = JS.now();
            int pcx = JS.floor(player.x / CHUNK), pcz = JS.floor(player.z / CHUNK);
            if (workerEngineFailed) refreshQueue();
            else { lastStreamCx = pcx; lastStreamCz = pcz; streamLeadChunks = 1; prioritizeWorkerQueues(pcx, pcz); }
            double deadline = JS.isFinite(frameDeadline) ? frameDeadline : t0 + 5;
            bool startup = !worldReady || initialSpawnPending || resumeLoadPending;
            int integrated = 0, maxGen = startup ? 2 : interactionHot ? 1 : 2;
            while (genDoneCount() > 0 && integrated < maxGen)
            {
                double est = integrated > 0 ? Math.Max(0.3, avgGenIntegrateMs * 1.15) : 0;
                if (!startup && JS.now() + est >= deadline) break;
                var done = takeGenDone();
                if (done == null) break;
                double ts = JS.now();
                integrateGenResult(done);
                avgGenIntegrateMs += (JS.now() - ts - avgGenIntegrateMs) * 0.2;
                integrated++;
            }
            if (initialSpawnPending || resumeLoadPending) finishInitialSpawnIfReady();
            if (!workerEngineStarted) initWorkerEngine();
            if (!workerEngineFailed && JS.now() < deadline - 0.35) fillGenJobsMain(pcx, pcz, deadline);
            // Leave part of the budget for mesh uploads/submits.
            processLightDirty(startup ? deadline : deadline - Math.Min(3, Math.Max(0, deadline - JS.now()) * 0.4));
            int uploaded = 0, maxUploads = startup ? 6 : interactionHot ? 1 : 3;
            while (meshDoneCount() > 0 && uploaded < maxUploads)
            {
                double est = uploaded > 0 ? Math.Max(0.15, avgMeshIntegrateMs * 1.2) : 0;
                if (!startup && JS.now() + est >= deadline) break;
                var done = takeMeshDone();
                if (done == null) break;
                double ts = JS.now();
                integrateMeshResult(done);
                avgMeshIntegrateMs += (JS.now() - ts - avgMeshIntegrateMs) * 0.2;
                uploaded++;
            }
            if (dirtyEditChunks.Count > 0 && JS.now() < deadline - 0.25) processDirtyEditMeshes(pcx, pcz, deadline, startup ? 6 : 4);
            if (JS.now() < deadline - 0.25)
            {
                prioritizeWorkerQueues(pcx, pcz);
                Chunk c0 = null, c1 = null, c2 = null; double p0 = double.PositiveInfinity, p1 = double.PositiveInfinity, p2 = double.PositiveInfinity;
                for (int dz = -renderDistance; dz <= renderDistance; dz++)
                    for (int dx = -renderDistance; dx <= renderDistance; dx++)
                    {
                        var c = chunkFastGet(pcx + dx, pcz + dz);
                        if (c == null || !c.dirty || meshPending.Contains(c.key ?? ckey(c.cx, c.cz)) || !meshNeighborhoodReady(c)) continue;
                        double p = (c.meshBuilt ? 1000000 : 0) + dx * dx + dz * dz;
                        if (p < p0) { c2 = c1; p2 = p1; c1 = c0; p1 = p0; c0 = c; p0 = p; }
                        else if (p < p1) { c2 = c1; p2 = p1; c1 = c; p1 = p; }
                        else if (p < p2) { c2 = c; p2 = p; }
                    }
                int idleMeshes = 0; foreach (var s in meshSlots) if (s.alive && s.ready && !s.busy) idleMeshes++;
                int maxSubmit = startup ? Math.Max(3, idleMeshes) : interactionHot ? 1 : Math.Max(2, Math.Min(4, idleMeshes + 1)), sent = 0;
                for (int mi = 0; mi < 3 && sent < maxSubmit; mi++)
                {
                    var c = mi == 0 ? c0 : mi == 1 ? c1 : c2;
                    if (c == null) continue;
                    double est = sent > 0 ? Math.Max(0.12, avgMeshQueueMs * 1.2) : 0;
                    if (!startup && JS.now() + est >= deadline) break;
                    double ts = JS.now();
                    if (queueChunkMesh(c)) sent++;
                    avgMeshQueueMs += (JS.now() - ts - avgMeshQueueMs) * 0.2;
                }
                if (meshJobs.Count > 1) { var s = meshJobs.OrderBy(a => streamPriority(a.cx, a.cz, pcx, pcz)).ToList(); meshJobs.Clear(); meshJobs.AddRange(s); }
                pumpMeshWorkers();
            }
            cleanupStreamCache(pcx, pcz, deadline, interactionHot ? 12 : 48);
            lastStreamCost = JS.now() - t0;
        }

        // ---------------- render bookkeeping / visibility ----------------
        public static double day = 0.18, fps = 0, fpsT = 0, last = 0;
        public static int frames = 0, drawCalls = 0, triangles = 0;
        public static readonly List<Chunk> renderVisibleChunks = new List<Chunk>();
        public static bool renderUnderwater = false;
        public static double lastFogNear = 0, lastFogFar = 0;
        public const int VIS_CELL_SIZE = 4;
        public static void collectVisibleChunksHierarchical(double ex, double ey, double ez, double fogFar, int pcx, int pcz, List<Chunk> visible)
        {
            int minCx = pcx - renderDistance, maxCx = pcx + renderDistance, minCz = pcz - renderDistance, maxCz = pcz + renderDistance;
            double fogCull = fogFar + 1, fog2 = fogCull * fogCull;
            int vcx0 = JS.floor((double)minCx / VIS_CELL_SIZE), vcx1 = JS.floor((double)maxCx / VIS_CELL_SIZE), vcz0 = JS.floor((double)minCz / VIS_CELL_SIZE), vcz1 = JS.floor((double)maxCz / VIS_CELL_SIZE);
            for (int vz = vcz0; vz <= vcz1; vz++)
                for (int vx = vcx0; vx <= vcx1; vx++)
                {
                    int ca = Math.Max(minCx, vx * VIS_CELL_SIZE), cb = Math.Min(maxCx, vx * VIS_CELL_SIZE + VIS_CELL_SIZE - 1), za = Math.Max(minCz, vz * VIS_CELL_SIZE), zb = Math.Min(maxCz, vz * VIS_CELL_SIZE + VIS_CELL_SIZE - 1);
                    double x0 = ca * CHUNK, z0 = za * CHUNK, x1 = (cb + 1) * CHUNK, z1 = (zb + 1) * CHUNK;
                    double qx = ex < x0 ? x0 - ex : ex > x1 ? ex - x1 : 0, qz = ez < z0 ? z0 - ez : ez > z1 ? ez - z1 : 0;
                    if (qx * qx + qz * qz > fog2) continue;
                    if (!aabbVisible(x0, WORLD_MIN_Y, z0, x1, WORLD_MAX_Y, z1)) continue;
                    for (int cz = za; cz <= zb; cz++)
                        for (int cx = ca; cx <= cb; cx++)
                        {
                            var c = chunkFastGet(cx, cz);
                            if (c == null || !c.meshBuilt || !c.meshComplete || (c.opaque == null && c.cutout == null && c.water == null && c.trans == null)) continue;
                            double cx0 = cx * CHUNK, cz0 = cz * CHUNK, cx1 = cx0 + CHUNK, cz1 = cz0 + CHUNK;
                            double dx = ex < cx0 ? cx0 - ex : ex > cx1 ? ex - cx1 : 0, dz = ez < cz0 ? cz0 - ez : ez > cz1 ? ez - cz1 : 0, hd2 = dx * dx + dz * dz;
                            if (hd2 > fog2) continue;
                            double y0 = double.IsNaN(c.meshMinY) ? WORLD_MIN_Y : c.meshMinY, y1 = double.IsNaN(c.meshMaxY) ? WORLD_MAX_Y : c.meshMaxY;
                            if (!aabbVisible(cx0, y0, cz0, cx1, y1, cz1)) continue;
                            c._renderDist2 = hd2;
                            visible.Add(c);
                        }
                }
        }

        // ---------------- matrices / frustum (column-major, WebGL convention) ----------------
        public static readonly float[] proj = new float[16], view = new float[16], vp = new float[16];
        public static float[] perspective(float[] o, double fovy, double aspect, double near, double far)
        {
            double f = 1 / Math.Tan(fovy / 2), nf = 1 / (near - far);
            Array.Clear(o, 0, 16);
            o[0] = (float)(f / aspect); o[5] = (float)f; o[10] = (float)((far + near) * nf); o[11] = -1; o[14] = (float)(2 * far * near * nf);
            return o;
        }
        public static float[] lookAt(float[] o, double[] e, double[] c, double[] u)
        {
            double zx = e[0] - c[0], zy = e[1] - c[1], zz = e[2] - c[2], l = 1 / JS.hypot(zx, zy, zz);
            zx *= l; zy *= l; zz *= l;
            double xx = u[1] * zz - u[2] * zy, xy = u[2] * zx - u[0] * zz, xz = u[0] * zy - u[1] * zx;
            l = 1 / JS.hypot(xx, xy, xz); xx *= l; xy *= l; xz *= l;
            double yx = zy * xz - zz * xy, yy = zz * xx - zx * xz, yz = zx * xy - zy * xx;
            o[0] = (float)xx; o[1] = (float)yx; o[2] = (float)zx; o[3] = 0;
            o[4] = (float)xy; o[5] = (float)yy; o[6] = (float)zy; o[7] = 0;
            o[8] = (float)xz; o[9] = (float)yz; o[10] = (float)zz; o[11] = 0;
            o[12] = (float)-(xx * e[0] + xy * e[1] + xz * e[2]); o[13] = (float)-(yx * e[0] + yy * e[1] + yz * e[2]); o[14] = (float)-(zx * e[0] + zy * e[1] + zz * e[2]); o[15] = 1;
            return o;
        }
        static readonly float[] mulScratch = new float[16];
        public static float[] mul(float[] o, float[] a, float[] b)
        {
            var s = mulScratch;
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                    s[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
            Array.Copy(s, o, 16);
            return o;
        }
        static readonly float[] frustumPlanes = new float[24];
        static readonly byte[] frustumSigns = new byte[6];
        static readonly double[] frustumTols = new double[6];
        public static void updateFrustum(float[] m)
        {
            var p = frustumPlanes;
            p[0] = m[3] + m[0]; p[1] = m[7] + m[4]; p[2] = m[11] + m[8]; p[3] = m[15] + m[12];
            p[4] = m[3] - m[0]; p[5] = m[7] - m[4]; p[6] = m[11] - m[8]; p[7] = m[15] - m[12];
            p[8] = m[3] + m[1]; p[9] = m[7] + m[5]; p[10] = m[11] + m[9]; p[11] = m[15] + m[13];
            p[12] = m[3] - m[1]; p[13] = m[7] - m[5]; p[14] = m[11] - m[9]; p[15] = m[15] - m[13];
            p[16] = m[3] + m[2]; p[17] = m[7] + m[6]; p[18] = m[11] + m[10]; p[19] = m[15] + m[14];
            p[20] = m[3] - m[2]; p[21] = m[7] - m[6]; p[22] = m[11] - m[10]; p[23] = m[15] - m[14];
            for (int i = 0, o = 0; i < 6; i++, o += 4)
            {
                float a = p[o], b = p[o + 1], c = p[o + 2];
                frustumSigns[i] = (byte)((a > 0 ? 1 : 0) | (b > 0 ? 2 : 0) | (c > 0 ? 4 : 0));
                frustumTols[i] = 0.35 * (Math.Abs(a) + Math.Abs(b) + Math.Abs(c));
            }
        }
        public static bool aabbVisible(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var p = frustumPlanes;
            for (int i = 0, o = 0; i < 6; i++, o += 4)
            {
                int s = frustumSigns[i];
                double x = (s & 1) != 0 ? x1 : x0, y = (s & 2) != 0 ? y1 : y0, z = (s & 4) != 0 ? z1 : z0;
                if (p[o] * x + p[o + 1] * y + p[o + 2] * z + p[o + 3] < -frustumTols[i]) return false;
            }
            return true;
        }

        // ---------------- toast ----------------
        public static string toastText = ""; public static double toastUntil = 0;
        public static void toast(string t) { toastText = t; toastUntil = JS.now() + 900; }
    }
}
