// Voxel Forge — Unity port. The per-frame driver (reference render(now) minus GPU submission),
// dynamic resolution, stats, initial spawn and game start.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VoxelForge
{
    public static partial class VF
    {
        public static double streamFrameMs = 16.6, lastFrameInterval = 16.67;
        static double interactionPrevYaw = double.NaN, interactionPrevPitch = double.NaN, interactionPrevX = double.NaN, interactionPrevZ = double.NaN;
        static bool frameInteractionHot(bool control)
        {
            double dy = 0, dp = 0, dm = 0;
            if (JS.isFinite(interactionPrevYaw))
            {
                dy = Math.Abs(player.yaw - interactionPrevYaw);
                while (dy > Math.PI) dy = Math.Abs(dy - Math.PI * 2);
                dp = Math.Abs(player.pitch - interactionPrevPitch);
                double dx = player.x - interactionPrevX, dz = player.z - interactionPrevZ;
                dm = Math.Sqrt(dx * dx + dz * dz);
            }
            bool keyMove = keyDown("KeyW") || keyDown("KeyA") || keyDown("KeyS") || keyDown("KeyD") || keyDown("ArrowUp") || keyDown("ArrowDown") || keyDown("ArrowLeft") || keyDown("ArrowRight") || keyDown("Space") || keyDown("ShiftLeft") || keyDown("ShiftRight");
            interactionPrevYaw = player.yaw; interactionPrevPitch = player.pitch; interactionPrevX = player.x; interactionPrevZ = player.z;
            return control && (keyMove || dy > 0.0015 || dp > 0.0015 || dm > 0.02);
        }
        static double frameBackgroundBudget(bool hot)
        {
            double target = Math.Max(6, Math.Min(20, streamFrameMs)), reserve = hot ? 2.6 : 1.5, spare = target - Math.Max(2.5, avgForegroundCpuMs) - reserve;
            return Math.Max(0.5, Math.Min(hot ? 6 : 9, spare));
        }

        /// <summary>Simulation + streaming part of the reference frame. Rendering commands are recorded by renderWorld.</summary>
        public static double frameSimulate(double now, out double dt)
        {
            double frameStart = JS.now();
            double frameInterval = now - last; if (frameInterval == 0 || double.IsNaN(frameInterval)) frameInterval = 16.67;
            if (frameInterval > 1 && frameInterval < 40) streamFrameMs += (frameInterval - streamFrameMs) * 0.05;
            dt = Math.Min(0.05, frameInterval / 1000); if (dt == 0) dt = 0.016;
            last = now; lastFrameInterval = frameInterval;
            bool worldRunning = started && worldReady && !pauseOpen && !player.dead && !player.sleeping, control = worldRunning && !uiOpen && (locked || touchActive) && !player.sleeping;
            worldRunningNow = worldRunning;
            if (control)
            {
                int moveSteps = dt > 0.025 ? 2 : 1;
                for (int i = 0; i < moveSteps; i++) movePlayer(dt / moveSteps);
                processMining(dt);
            }
            bool interactionHot = frameInteractionHot(control);
            // main.js order: simulation (including fluid ticks) happens before chunk/light/mesh scheduling.
            if (worldRunning)
            {
                refreshSimulationBounds();
                updateVehicles(dt);
                updateFishing(dt);
                updatePearls(dt);
                survivalTick(dt);
                tickMobSimulation(dt, now);
                tickHostileSpawns(dt);
                tickTNT(dt);
                updateWorldDrops(dt);
                updateBreakParticles(dt);
                tickWorldDynamics(dt, now);
                tickFurnaces(dt);
                day = (day + dt / 360) % 1;
            }
            if (started)
            {
                double bgStart = JS.now(), bgBudget = worldReady ? frameBackgroundBudget(interactionHot) : 10, bgDeadline = bgStart + bgBudget;
                stream(bgDeadline, interactionHot);
                pumpRenderRegionBuilds(bgDeadline, worldReady ? (interactionHot ? 1 : 3) : 3);
                lastBackgroundCpuMs = JS.now() - bgStart;
            }
            else lastBackgroundCpuMs = 0;
            return frameStart;
        }
        public static void frameFinish(double now, double frameStart)
        {
            updateMinimap(now);
            lastFrameCpuMs = JS.now() - frameStart;
            double fg = Math.Max(0, lastFrameCpuMs - lastBackgroundCpuMs);
            avgForegroundCpuMs += (fg - avgForegroundCpuMs) * 0.08;
            updateDynamicResolution(now, lastFrameInterval);
            frames++;
            if (now - fpsT > 500) { fps = Math.Round(frames * 1000 / (now - fpsT)); frames = 0; fpsT = now; updateStats(); }
        }

        // ---------------- dynamic resolution ----------------
        static readonly double[] DYN_RES_STEPS = { 1, 0.85, 0.72, 0.6, 0.5 };
        static int dynResIdx = 0;
        static double dynResChangedAt = 0, dynResSlowSince = 0, dynResFastSince = 0, dynResUpWait = 3000, dynResLastUpAt = -1e9, dynFrameEma = 16.7, dynVsyncMs = 16.7;
        static void setDynResIdx(int i, double now)
        {
            i = Math.Max(0, Math.Min(DYN_RES_STEPS.Length - 1, i));
            if (i == dynResIdx) return;
            if (i < dynResIdx) dynResLastUpAt = now;
            else if (now - dynResLastUpAt < 4000) dynResUpWait = Math.Min(30000, dynResUpWait * 2);
            dynResIdx = i; renderScale = DYN_RES_STEPS[i]; dynResChangedAt = now; dynResSlowSince = dynResFastSince = 0;
        }
        static void updateDynamicResolution(double now, double frameInterval)
        {
            if (!gameSettings.dynRes) { if (dynResIdx != 0) setDynResIdx(0, now); return; }
            if (!(frameInterval > 2 && frameInterval < 250)) return;
            dynFrameEma += (frameInterval - dynFrameEma) * 0.1;
            dynVsyncMs = Math.Max(6, Math.Min(34, frameInterval < dynVsyncMs ? frameInterval : dynVsyncMs * 1.0005));
            if (!started || !worldReady || pauseOpen || !Application.isFocused || now - dynResChangedAt < 1500) return;
            double budget = dynVsyncMs, cpu = avgForegroundCpuMs + lastBackgroundCpuMs;
            // No GPU timer query in this port: use the CPU-unexplained frame time signal.
            bool slow = dynFrameEma > budget * 1.25 && cpu < dynFrameEma * 0.6;
            bool fast = dynResIdx > 0 && dynFrameEma <= budget * 1.08;
            if (slow) { if (dynResSlowSince == 0) dynResSlowSince = now; if (now - dynResSlowSince > 1000) setDynResIdx(dynResIdx + 1, now); } else dynResSlowSince = 0;
            if (fast) { if (dynResFastSince == 0) dynResFastSince = now; if (now - dynResFastSince > dynResUpWait) setDynResIdx(dynResIdx - 1, now); } else dynResFastSince = 0;
        }

        // ---------------- stats ----------------
        public static string statsText = "", modeText = "";
        static void updateStats()
        {
            int mobs = 0; foreach (var m in liveMobs) if (!m.dead) mobs++;
            int cb = caveBiomeAt(player.x, JS.floor(player.y), player.z);
            statsText = "FPS " + fps + "\n"
                + "чанки " + chunks.Count + " · visible " + renderVisibleCount + " · RD " + renderDistance + " · SD " + activeSimulationDistance() + " · UW " + (renderUnderwater ? 1 : 0) + "\n"
                + "gen " + genPending.Count + "/" + genJobs.Count + " · mesh " + meshPending.Count + "/" + meshJobs.Count + "/" + meshDoneCount() + " · light " + lightDirtyCount() + "\n"
                + "мобы " + mobs + " · edits " + edits.Count + " · draw " + drawCalls + " · tris " + JS.round(triangles / 1000.0) + "k · O/C/W/T " + opaqueDraws + "/" + cutoutDraws + "/" + waterDraws + "/" + transDraws + "\n"
                + "CPU render " + JS.Fixed(avgRenderCpuMs, 1) + " · res ×" + JS.ToStr(renderScale) + " · vis " + JS.Fixed(avgRenderVisMs, 1) + " · bg " + JS.Fixed(lastBackgroundCpuMs, 1) + "\n"
                + "XYZ " + JS.Fixed(player.x, 1) + " " + JS.Fixed(player.y, 1) + " " + JS.Fixed(player.z, 1) + "\n"
                + "биом " + biomeAt(player.x, player.z) + (cb != 0 ? " · cave " + new[] { "", "LUSH", "DRIPSTONE", "DEEP_DARK" }[cb] : "");
            modeText = "v" + GAME_VERSION + " · " + ENGINE_BUILD + " · SAVE S" + SAVE_SCHEMA_VERSION + "/W" + SAVE_WORLD_VERSION + " · " + (player.creative ? "CREATOR" : "SURVIVAL") + (player.flying ? " · ПОЛЁТ" : "") + (xrayActive ? " · X-RAY" : "");
        }

        // ---------------- start ----------------
        public static bool started = false, pauseOpen = false;
        public static void initSpawn()
        {
            if (!workerEngineStarted) initWorkerEngine();
            int cx = JS.floor(player.x / CHUNK), cz = JS.floor(player.z / CHUNK);
            if (chunkFastGet(cx, cz) == null && !workerEngineFailed) queueChunkGeneration(cx, cz);
            lastStreamCx = 999; lastStreamCz = 999;
            queue.Clear();
            refreshQueue();
            pumpGenWorkers();
        }
        public static string startNote = "Старые сохранения Creator/Survival автоматически показываются как legacy-миры и продолжают использовать прежние данные.";
        public static bool startButtonsDisabled = false;
        public static void startGame(string mode, string worldId)
        {
            if (started) return;
            var rec = loadWorldIndex().FirstOrDefault(w => w.id == worldId);
            if (rec == null) return;
            configureSaveMode(mode, worldId);
            loadOrCreateWorldSeed();
            initWorkerEngine();
            var copiedSpawn = rec.spawn != null && rec.spawn.Length >= 3 && JS.isFinite(rec.spawn[0]) && JS.isFinite(rec.spawn[1]) && JS.isFinite(rec.spawn[2]) ? (double[])rec.spawn.Clone() : null;
            worldSpawnOverride = null;
            if (copiedSpawn != null)
            {
                SPAWN = (double[])copiedSpawn.Clone();
                SPAWN_ANCHOR = new[] { Math.Floor(copiedSpawn[0]), Math.Floor(copiedSpawn[2]) };
            }
            else
            {
                var a = findLandSpawnAnchor(0, 0);
                if (a == null) { toast("Не удалось найти сушу для появления"); return; }
                SPAWN_ANCHOR = new double[] { a[0], a[1] };
                SPAWN = new[] { a[0] + 0.5, heightAt(a[0], a[1]) + 2, a[1] + 0.5 };
            }
            player.creative = GAME_MODE == "creative";
            loadWorldSave();
            loadKilledMobs();
            loadGame();
            player.creative = GAME_MODE == "creative";
            started = true;
            worldReady = false;
            startButtonsDisabled = true;
            if (loadedGameState)
            {
                resumeLoadPending = true; initialSpawnPending = false;
                startNote = "Проверка сохранённой позиции и сухой точки появления…";
                primeInitialSpawnChunks(player.x, player.z);
            }
            else
            {
                resumeLoadPending = false; initialSpawnPending = true;
                worldSpawnOverride = copiedSpawn != null ? (double[])copiedSpawn.Clone() : null;
                player.x = SPAWN[0]; player.y = SPAWN[1]; player.z = SPAWN[2];
                player.spawn = (double[])SPAWN.Clone();
                startNote = copiedSpawn != null ? "Генерация чистой копии у сохранённой точки появления…" : "Генерация стартовой суши и проверка безопасного spawn…";
                primeInitialSpawnChunks(copiedSpawn != null ? copiedSpawn[0] : SPAWN_ANCHOR[0], copiedSpawn != null ? copiedSpawn[2] : SPAWN_ANCHOR[1]);
            }
            pumpGenWorkers();
            syncTouchControls();
        }
        public static void createAndStartWorld(string mode, string name)
        {
            var w = createWorldRecord(mode, name ?? "");
            startGame(mode, w.id);
        }
        /// <summary>"Выбор режима": the reference reloads the page; here the session is torn down in place.</summary>
        public static void returnToTitle()
        {
            if (started) saveGameNow();
            cancelPendingStateSave();
            shutdownWorkerEngine();
            foreach (var c in chunks.Values.ToList()) releaseChunkGpu(c);
            foreach (var r in renderRegions.Values) { if (r.mesh != null) UnityEngine.Object.Destroy(r.mesh); }
            renderRegions.Clear(); dirtyRenderRegions.Clear();
            chunks.Clear(); clearChunkRowsForReset();
            clearRuntimeWorld();
            liveMobs.Clear(); storedMobs.Clear(); seededMobChunks.Clear(); projectiles.Clear(); worldDrops.Clear(); primedTNT.Clear();
            clearVehicles(); resetActiveItems(); minimapCache.Clear();
            breakParticleCount = 0;
            started = false; worldReady = false; pauseOpen = false; uiOpen = false; initialSpawnPending = false; resumeLoadPending = false;
            startScreenVisible = true; hudVisible = false; deathUIVisible = false; startButtonsDisabled = false;
            settingsOpen = false; modsOpen = false; sleepOverlayState = 0; player.sleeping = false; player.dead = false;
            // A page reload re-runs `let day = 0.18` and the player literal; loadGame() only restores what a save contains.
            day = 0.18; player.yaw = 0; player.pitch = 0; player.camMode = 0; player.vx = player.vy = player.vz = 0; player.onGround = false;
            player.swimming = false; player.sneaking = false; player.inWater = false; player.hitWall = false; player.boostUntil = 0;
            interactionPrevYaw = interactionPrevPitch = interactionPrevX = interactionPrevZ = double.NaN;
            streamCleanupIter = null; queue.Clear();
            startNote = "Старые сохранения Creator/Survival автоматически показываются как legacy-миры и продолжают использовать прежние данные.";
            releasePointerLock(false);
        }
    }
}
