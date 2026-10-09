// Voxel Forge — Unity port. World dynamics: crops, fluids, farmland, saplings, cacti, leaf decay,
// fire, falling blocks, pressure plates and furnaces.
using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxelForge
{
    public static partial class VF
    {
        static List<KeyValuePair<string, T>> snapshotSimMap<T>(OrderedMap<string, T> m) { return new List<KeyValuePair<string, T>>(m); }

        // ---------------- crops ----------------
        static bool cropFruitSoil(int id) { return id == B.GRASS || id == B.DIRT || id == B.PODZOL || id == B.FARMLAND || id == B.FARMLAND_MOIST; }
        static bool tryGrowCropFruit(int x, int y, int z, int fruit)
        {
            var dirs = FLUID_XZ_DIRS;
            foreach (var d in dirs) if (getBlock(x + d[0], y, z + d[1]) == fruit) return false;
            int start = (int)Math.Floor(JS.random() * 4);
            for (int n = 0; n < 4; n++)
            {
                var d = dirs[(start + n) % 4]; int nx = x + d[0], nz = z + d[1];
                if (getBlock(nx, y, nz) == B.AIR && cropFruitSoil(getBlock(nx, y - 1, nz))) { setBlock(nx, y, nz, fruit); return true; }
            }
            return false;
        }
        static void tickCrops(double dt)
        {
            cropT += dt;
            if (cropT < 1) return;
            double e = cropT; cropT = 0;
            foreach (var kv in snapshotSimMap(worldSim.crops))
            {
                string k = kv.Key; var st = kv.Value; var q = decodeSimKey(k); int x = q[0], y = q[1], z = q[2];
                if (!chunkLoadedAt(x, z)) continue;
                var ci = cropInfo(getBlock(x, y, z));
                if (ci == null) { worldSim.crops.Delete(k); continue; }
                int farm = getBlock(x, y - 1, z);
                if (farm != B.FARMLAND && farm != B.FARMLAND_MOIST) { setBlock(x, y, z, B.AIR); worldSim.crops.Delete(k); continue; }
                if (ci.idx >= ci.stages.Length - 1 && ci.fruit == 0) { worldSim.crops.Delete(k); continue; }
                st.t += farm == B.FARMLAND_MOIST ? e : e * 0.5;
                if (ci.idx >= ci.stages.Length - 1) { if (st.t >= 45) { st.t = 0; tryGrowCropFruit(x, y, z, ci.fruit); } }
                else if (st.t >= 35) { st.t = 0; setBlock(x, y, z, ci.stages[ci.idx + 1]); }
            }
        }

        // ---------------- fluids ----------------
        static void fluidBreakCrossPlant(int x, int y, int z, int id)
        {
            var b = bdef(id);
            if (id == B.AIR || !mainCrossPlantCell(id) || (b != null && b.waterPlant)) return;
            foreach (var dr in blockDrops(id)) spawnWorldDrop(dr.k, dr.n, x + 0.5, y + 0.5, z + 0.5);
            // The following flow voxel is transient, but the destroyed plant is a real world change.
            addEditIndex(x, y, z, B.AIR, true);
        }
        static readonly List<string> waterQueueScratch = new List<string>(), lavaQueueScratch = new List<string>(), fallingWakeScratch = new List<string>();
        static void processWaterQueue()
        {
            var ws = worldSim;
            if (ws.waterUrgentQueue.Count == 0 && ws.waterQueue.Count == 0 && ws.waterSeedQueue.Count == 0) return;
            var keys = waterQueueScratch; keys.Clear();
            // Player-created holes are always considered before background shoreline/cave work.
            foreach (var k in ws.waterUrgentQueue) { keys.Add(k); if (keys.Count >= 128) break; }
            foreach (var k in keys) ws.waterUrgentQueue.Delete(k);
            int urgentCount = keys.Count;
            foreach (var k in ws.waterQueue) { keys.Add(k); if (keys.Count >= 400) break; }
            for (int i = urgentCount; i < keys.Count; i++) ws.waterQueue.Delete(keys[i]);
            int activeCount = keys.Count;
            foreach (var k in ws.waterSeedQueue) { keys.Add(k); if (keys.Count >= activeCount + 80) break; }
            for (int i = activeCount; i < keys.Count; i++) ws.waterSeedQueue.Delete(keys[i]);
            int work = 120;
            for (int qi = 0; qi < keys.Count; qi++)
            {
                if (work <= 0) { (qi < urgentCount ? ws.waterUrgentQueue : ws.waterQueue).Add(keys[qi]); continue; }
                var dec = decodeSimKey(keys[qi]); int x = dec[0], y = dec[1], z = dec[2];
                if (!simulationBlockActive(x, z)) { ws.waterSeedQueue.Add(keys[qi]); continue; }
                int id = fluidBlockAt(x, y, z);
                bool sourceCell = waterSourceAt(x, y, z, id);
                int lev = waterLevel(id);
                if (lev == 0 && !sourceCell) continue;
                work--;
                // A non-source cell with water directly above is a falling column.
                int aboveId = fluidBlockAt(x, y + 1, z);
                bool fedFromAbove = waterLevel(aboveId) > 0 || waterSourceAt(x, y + 1, z, aboveId);
                if (!sourceCell && fedFromAbove)
                {
                    if (id != B.WATER_FALLING) { setFluidBlock(x, y, z, B.WATER_FALLING); id = B.WATER_FALLING; }
                    lev = 8;
                }
                else if (!sourceCell)
                {
                    // Infinite source rule: two adjacent sources plus a source/solid support.
                    if (waterCanCreateSource(x, y, z)) { if (id != B.WATER) setFluidBlock(x, y, z, B.WATER); id = B.WATER; lev = 8; }
                    else
                    {
                        int best = strongestHorizontalWater(x, y, z), desired = best - 1;
                        if (desired <= 0) { setFluidBlock(x, y, z, B.AIR); continue; }
                        int desiredId = waterBlockForLevel(desired);
                        if (id != desiredId) { setFluidBlock(x, y, z, desiredId); id = desiredId; }
                        lev = desired;
                    }
                }
                else lev = 8;
                int below = fluidBlockAt(x, y - 1, z);
                if (waterReplaceableCell(below))
                {
                    // Gravity wins over horizontal spreading.
                    if (y - 1 >= WORLD_MIN_Y)
                    {
                        fluidBreakCrossPlant(x, y - 1, z, below);
                        if (fluidBlockAt(x, y - 1, z) != B.WATER_FALLING) setFluidBlock(x, y - 1, z, B.WATER_FALLING);
                    }
                    continue;
                }
                // A submerged SOURCE does no background lateral spreading (ravine water walls).
                if (sourceCell && isFluidWater(aboveId) && qi >= urgentCount) continue;
                bool support = id == B.WATER_FALLING ? waterFallingLandingSupport(x, y, z) : waterSolidSupport(below) || waterSourceAt(x, y - 1, z, below);
                if (!support) continue;
                int spreadBase = id == B.WATER_FALLING ? waterFallingStrengthAt(x, y, z) : lev;
                if (spreadBase <= 1) continue;
                int outLevel = spreadBase - 1, outId = waterBlockForLevel(outLevel), spreadMask = minecraftWaterSpreadMask(x, y, z, outLevel);
                for (int k = 0; k < 4; k++)
                {
                    if ((spreadMask & (1 << k)) == 0) continue;
                    int nx = x + FLUID_XZ_DIRS[k][0], nz = z + FLUID_XZ_DIRS[k][1], q = fluidBlockAt(nx, y, nz);
                    if (waterReplaceableCell(q)) { fluidBreakCrossPlant(nx, y, nz, q); setFluidBlock(nx, y, nz, outId); }
                    else if (isFluidWater(q) && q != B.WATER && q != B.WATER_FALLING && waterLevel(q) < outLevel) setFluidBlock(nx, y, nz, outId);
                }
            }
        }
        static bool lavaTouchesWater(int x, int y, int z)
        {
            foreach (var d in FLUID_NEIGHBOR_DIRS)
            {
                int nx = x + d[0], ny = y + d[1], nz = z + d[2], id = fluidBlockAt(nx, ny, nz);
                if (waterLevel(id) > 0 || waterSourceAt(nx, ny, nz, id)) return true;
            }
            return false;
        }
        static void processLavaQueue()
        {
            var ws = worldSim;
            if (ws.lavaQueue.Count == 0 && ws.lavaSeedQueue.Count == 0) return;
            var keys = lavaQueueScratch; keys.Clear();
            foreach (var k in ws.lavaQueue) { keys.Add(k); if (keys.Count >= 240) break; }
            foreach (var k in keys) ws.lavaQueue.Delete(k);
            int activeCount = keys.Count;
            foreach (var k in ws.lavaSeedQueue) { keys.Add(k); if (keys.Count >= activeCount + 40) break; }
            for (int i = activeCount; i < keys.Count; i++) ws.lavaSeedQueue.Delete(keys[i]);
            int work = 60;
            for (int qi = 0; qi < keys.Count; qi++)
            {
                if (work <= 0) { ws.lavaQueue.Add(keys[qi]); continue; }
                var dec = decodeSimKey(keys[qi]); int x = dec[0], y = dec[1], z = dec[2];
                if (!simulationBlockActive(x, z)) { ws.lavaSeedQueue.Add(keys[qi]); continue; }
                int id = fluidBlockAt(x, y, z), lev = lavaLevel(id);
                if (lev == 0) continue;
                work--;
                if (lavaTouchesWater(x, y, z)) { setFluidBlock(x, y, z, id == B.LAVA ? B.OBSIDIAN : B.COBBLE); continue; }
                if (id != B.LAVA)
                {
                    bool fed = lavaLevel(fluidBlockAt(x, y + 1, z)) > 0 || lavaLevel(fluidBlockAt(x + 1, y, z)) > lev || lavaLevel(fluidBlockAt(x - 1, y, z)) > lev
                        || lavaLevel(fluidBlockAt(x, y, z + 1)) > lev || lavaLevel(fluidBlockAt(x, y, z - 1)) > lev;
                    if (!fed) { int nl = lev - 1; setFluidBlock(x, y, z, nl > 0 ? lavaBlockForLevel(nl) : B.AIR); queueLava(x, y, z); continue; }
                }
                int below = fluidBlockAt(x, y - 1, z);
                if (waterReplaceableCell(below))
                {
                    if (y - 1 >= WORLD_MIN_Y) { fluidBreakCrossPlant(x, y - 1, z, below); setFluidBlock(x, y - 1, z, B.LAVA_FLOW2); }
                }
                else if (lev > 1 && (waterSolidSupport(below) || below == B.LAVA))
                {
                    foreach (var d in FLUID_XZ_DIRS)
                    {
                        int nx = x + d[0], nz = z + d[1], q = fluidBlockAt(nx, y, nz);
                        if (waterReplaceableCell(q)) { fluidBreakCrossPlant(nx, y, nz, q); setFluidBlock(nx, y, nz, lavaBlockForLevel(lev - 1)); }
                    }
                }
            }
        }
        static void tickFluids(double now)
        {
            if (now - waterSimAt > 200) { waterSimAt = now; processWaterQueue(); }
            if (now - lavaSimAt > 500) { lavaSimAt = now; processLavaQueue(); }
        }

        // ---------------- farmland ----------------
        static void tickFarmland(double dt)
        {
            farmlandT += dt;
            if (farmlandT < 2) return;
            double e = farmlandT; farmlandT = 0;
            foreach (var kv in snapshotSimMap(worldSim.farmland))
            {
                string k = kv.Key; var st = kv.Value; var q = decodeSimKey(k); int x = q[0], y = q[1], z = q[2];
                if (!chunkLoadedAt(x, z)) continue;
                int id = getBlock(x, y, z);
                if (id != B.FARMLAND && id != B.FARMLAND_MOIST) { worldSim.farmland.Delete(k); continue; }
                bool wet = waterNearbyFarmland(x, y, z);
                if (wet && id == B.FARMLAND) setBlock(x, y, z, B.FARMLAND_MOIST);
                else if (!wet && id == B.FARMLAND_MOIST) setBlock(x, y, z, B.FARMLAND);
                bool crop = isCropBlock(getBlock(x, y + 1, z));
                if (!wet && !crop)
                {
                    st.dryT += e;
                    if (st.dryT >= 90) { setBlock(x, y, z, B.DIRT); worldSim.farmland.Delete(k); }
                }
                else st.dryT = 0;
            }
        }

        // ---------------- saplings ----------------
        static bool treeReplaceableAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z);
            if (id == B.AIR || id == B.SAPLING || isTreeLeaves(id)) return true;
            if (isVirtualId(id)) { var d = virtualDefAt(x, y, z); return d != null && (d.plant || d.shape == "cross" || virtualSaplingSpecies(d) != null); }
            var b = bdef(id); return b != null && b.plant;
        }
        static bool treeVerticalClear(int x, int y, int z, int h)
        {
            if (y < 1 || y + h >= WORLD_MAX_Y) return false;
            for (int n = 0; n < h; n++) if (!treeReplaceableAt(x, y + n, z)) return false;
            return true;
        }
        static bool treePutIfReplaceable(int x, int y, int z, int id)
        {
            if (y <= WORLD_MIN_Y || y >= WORLD_MAX_Y || !treeReplaceableAt(x, y, z)) return false;
            setBlock(x, y, z, id);
            return true;
        }
        static void treeLeafDisc(int x, int y, int z, int r, int id)
        {
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (r >= 2 && Math.Abs(dx) == r && Math.Abs(dz) == r && JS.random() < 0.6) continue;
                    treePutIfReplaceable(x + dx, y, z + dz, id);
                }
        }
        static int rndInt(int n) { return (int)(JS.random() * n); }
        static bool buildSaplingTree(string kind, int x, int y, int z)
        {
            int h;
            if (kind == "dark_oak")
            {
                h = 5 + rndInt(2);
                for (int ox = 0; ox < 2; ox++) for (int oz = 0; oz < 2; oz++) if (!treeVerticalClear(x + ox, y, z + oz, h)) return false;
                for (int dy = -2; dy <= 1; dy++)
                {
                    int yy = y + h + dy;
                    if (yy > y) { treeLeafDisc(x, yy, z, dy <= 0 ? 3 : 2, B.DARK_LEAVES); treeLeafDisc(x + 1, yy, z + 1, dy <= 0 ? 3 : 2, B.DARK_LEAVES); }
                }
                for (int n = 0; n < h; n++) for (int ox = 0; ox < 2; ox++) for (int oz = 0; oz < 2; oz++) setBlock(x + ox, y + n, z + oz, B.DARK_LOG);
                return true;
            }
            if (kind == "spruce")
            {
                bool tall = JS.random() < 0.25;
                h = tall ? 10 + rndInt(4) : 6 + rndInt(3);
                if (!treeVerticalClear(x, y, z, h)) return false;
                treePutIfReplaceable(x, y + h + 1, z, B.SPRUCE_LEAVES);
                int layers = tall ? 8 : 6;
                for (int n = 0; n < layers; n++)
                {
                    int yy = y + h - n;
                    if (yy <= y + 2) break;
                    treeLeafDisc(x, yy, z, n == 0 || n % 2 == 1 ? 1 : 2, B.SPRUCE_LEAVES);
                }
                for (int n = 0; n < h; n++) setBlock(x, y + n, z, B.SPRUCE_LOG);
                return true;
            }
            if (kind == "jungle")
            {
                h = 8 + rndInt(4);
                if (!treeVerticalClear(x, y, z, h)) return false;
                treeLeafDisc(x, y + h + 1, z, 1, B.JUNGLE_LEAVES); treeLeafDisc(x, y + h, z, 2, B.JUNGLE_LEAVES); treeLeafDisc(x, y + h - 1, z, 2, B.JUNGLE_LEAVES);
                for (int n = 0; n < h; n++) setBlock(x, y + n, z, B.JUNGLE_LOG);
                return true;
            }
            if (kind == "acacia")
            {
                h = 5 + rndInt(2);
                if (!treeVerticalClear(x, y, z, h)) return false;
                int dir = rndInt(4), dx = new[] { 1, -1, 0, 0 }[dir], dz = new[] { 0, 0, 1, -1 }[dir], tx = x, tz = z;
                var path = new List<int[]>();
                for (int n = 1; n <= h; n++)
                {
                    if (n > 2 && n % 2 == 1) { tx += dx; tz += dz; }
                    int ty = y + n - 1;
                    if (!treeReplaceableAt(tx, ty, tz)) return false;
                    path.Add(new[] { tx, ty, tz });
                }
                foreach (var q in path) setBlock(q[0], q[1], q[2], B.ACACIA_LOG);
                treeLeafDisc(tx, y + h, tz, 3, B.ACACIA_LEAVES); treeLeafDisc(tx, y + h + 1, tz, 2, B.ACACIA_LEAVES);
                return true;
            }
            if (kind == "oak" && JS.random() < 0.3)
            {
                h = 7 + rndInt(3);
                if (!treeVerticalClear(x, y, z, h)) return false;
                int top = y + h; var radii = new[] { 2, 3, 3, 2, 1 };
                for (int n = 0; n < radii.Length; n++) treeLeafDisc(x, top - 3 + n, z, radii[n], B.LEAVES);
                int dir = rndInt(4);
                treeLeafDisc(x + new[] { 2, -2, 0, 0 }[dir], top - 3, z + new[] { 0, 0, 2, -2 }[dir], 1, B.LEAVES);
                for (int n = 0; n < h; n++) setBlock(x, y + n, z, B.LOG);
                return true;
            }
            bool birch = kind == "birch"; int log = birch ? B.BIRCH_LOG : B.LOG, leaves = birch ? B.BIRCH_LEAVES : B.LEAVES;
            h = (birch ? 5 : 4) + rndInt(3);
            if (!treeVerticalClear(x, y, z, h)) return false;
            for (int dy = -2; dy <= 1; dy++) { int yy = y + h + dy; if (yy > y) treeLeafDisc(x, yy, z, dy <= -1 ? 2 : 1, leaves); }
            for (int n = 0; n < h; n++) setBlock(x, y + n, z, log);
            return true;
        }
        public static bool growSaplingAt(int x, int y, int z)
        {
            var kind = saplingSpeciesAt(x, y, z);
            if (kind == null || !saplingGroundId(getBlock(x, y - 1, z))) return false;
            if (kind == "dark_oak")
            {
                for (int ax = x - 1; ax <= x; ax++)
                    for (int az = z - 1; az <= z; az++)
                    {
                        bool ok = true;
                        for (int ox = 0; ox < 2 && ok; ox++) for (int oz = 0; oz < 2; oz++) if (saplingSpeciesAt(ax + ox, y, az + oz) != "dark_oak") ok = false;
                        if (!ok) continue;
                        var saved = new List<object[]>();
                        for (int ox = 0; ox < 2; ox++)
                            for (int oz = 0; oz < 2; oz++)
                            {
                                int sx = ax + ox, sz = az + oz;
                                saved.Add(new object[] { sx, sz, getBlock(sx, y, sz), getBlockMeta(sx, y, sz) });
                                setBlock(sx, y, sz, B.AIR);
                                worldSim.saplings.Delete(simKey(sx, y, sz));
                            }
                        if (buildSaplingTree(kind, ax, y, az)) return true;
                        foreach (var s in saved) setBlock((int)s[0], y, (int)s[1], (int)s[2], (JObj)s[3]);
                        return false;
                    }
                return false;
            }
            int id = getBlock(x, y, z); var meta = getBlockMeta(x, y, z);
            setBlock(x, y, z, B.AIR);
            worldSim.saplings.Delete(simKey(x, y, z));
            if (buildSaplingTree(kind, x, y, z)) return true;
            setBlock(x, y, z, id, meta);
            return false;
        }
        public static bool growOakAt(int x, int y, int z) { return saplingSpeciesAt(x, y, z) == "oak" && growSaplingAt(x, y, z); }
        static void tickSaplings(double dt)
        {
            saplingT += dt;
            if (saplingT < 1) return;
            double elapsed = saplingT; saplingT = 0;
            foreach (var kv in snapshotSimMap(worldSim.saplings))
            {
                string k = kv.Key; var st = kv.Value; var q = decodeSimKey(k); int x = q[0], y = q[1], z = q[2];
                if (!chunkLoadedAt(x, z)) continue;
                if (saplingSpeciesAt(x, y, z) == null) { worldSim.saplings.Delete(k); continue; }
                st.t += elapsed;
                if (st.t >= st.delay && !growSaplingAt(x, y, z)) { st.t = 0; st.delay = 30; }
                else if (saplingSpeciesAt(x, y, z) == null) worldSim.saplings.Delete(k);
            }
        }
        static void tickColumnGrowth(double dt)
        {
            columnGrowT += dt;
            if (columnGrowT < 2) return;
            double e = columnGrowT; columnGrowT = 0;
            foreach (var kv in snapshotSimMap(worldSim.growColumns))
            {
                string k = kv.Key; var st = kv.Value; var q = decodeSimKey(k); int x = q[0], y = q[1], z = q[2];
                if (!chunkLoadedAt(x, z)) continue;
                int id = getBlock(x, y, z);
                if (id != B.CACTUS) { worldSim.growColumns.Delete(k); continue; }
                int bse = y; while (getBlock(x, bse - 1, z) == id) bse--;
                int top = bse; while (getBlock(x, top + 1, z) == id) top++;
                if (top - bse + 1 >= 3 || getBlock(x, top + 1, z) != B.AIR) { st.t = 0; continue; }
                st.t += e;
                if (st.t >= 60 * (0.75 + JS.random() * 0.5)) { st.t = 0; setBlock(x, top + 1, z, id); }
            }
        }
        static void tickLeafDecay(double dt)
        {
            leafT += dt;
            if (leafT < 0.25 || worldSim.leafDecay.Count == 0) return;
            double e = leafT; leafT = 0;
            int done = 0;
            foreach (var kv in snapshotSimMap(worldSim.leafDecay))
            {
                string k = kv.Key; double nt = kv.Value - e;
                if (nt > 0) { worldSim.leafDecay.Set(k, nt); continue; }
                if (done >= 24) { worldSim.leafDecay.Set(k, 0.3); continue; }
                worldSim.leafDecay.Delete(k);
                var q = decodeSimKey(k); int x = q[0], y = q[1], z = q[2];
                var lm = chunkLoadedAt(x, z) ? getBlockMeta(x, y, z) : null;
                if (!chunkLoadedAt(x, z) || !isTreeLeaves(getBlock(x, y, z)) || (lm != null && lm.Bool("persistent")) || leafConnectedToLog(x, y, z)) continue;
                int leafId = getBlock(x, y, z);
                setBlock(x, y, z, B.AIR);
                foreach (var dr in blockDrops(leafId)) spawnWorldDrop(dr.k, dr.n, x + 0.5, y + 0.5, z + 0.5);
                done++;
            }
        }
        static void tickFire(double dt)
        {
            foreach (var kv in snapshotSimMap(worldSim.fires))
            {
                string k = kv.Key; var st = kv.Value; var q = decodeSimKey(k); int x = q[0], y = q[1], z = q[2];
                if (!chunkLoadedAt(x, z)) continue;
                if (getBlock(x, y, z) != B.FIRE) { worldSim.fires.Delete(k); continue; }
                st.t += dt;
                if (st.t < st.next) continue;
                st.t = 0; st.next = 0.7 + JS.random() * 0.6; st.age = Math.Min(15, st.age + 1);
                bool water = false, fuel = false;
                foreach (var d in MAIN_NEIGHBOR_DIRS)
                {
                    int nx = x + d[0], ny = y + d[1], nz = z + d[2], id = getBlock(nx, ny, nz);
                    if (isWater(id)) water = true;
                    if (flammableAt(nx, ny, nz)) fuel = true;
                }
                if (water || (fuel ? st.age >= 15 && JS.random() < 0.2 : !topSupportAt(x, y - 1, z) || (st.age >= 4 && JS.random() < 0.35)))
                {
                    setBlock(x, y, z, B.AIR);
                    worldSim.fires.Delete(k);
                    continue;
                }
                foreach (var d in MAIN_NEIGHBOR_DIRS)
                {
                    int nx = x + d[0], ny = y + d[1], nz = z + d[2];
                    if (flammableAt(nx, ny, nz) && JS.random() < (d[1] == 1 ? 0.9 : 0.7)) setBlock(nx, ny, nz, JS.random() < 0.8 ? B.FIRE : B.AIR);
                }
                for (int j = 0; j < 3; j++)
                {
                    int nx = x + rndInt(3) - 1, ny = y + rndInt(3) - 1, nz = z + rndInt(3) - 1;
                    if (getBlock(nx, ny, nz) != B.AIR) continue;
                    bool near = false;
                    foreach (var d in MAIN_NEIGHBOR_DIRS) if (flammableAt(nx + d[0], ny + d[1], nz + d[2])) { near = true; break; }
                    if (near && JS.random() < 0.35) setBlock(nx, ny, nz, B.FIRE);
                }
            }
        }

        // ---------------- falling blocks ----------------
        static bool fallingPassable(int id) { var b = bdef(id); return id == B.AIR || isWater(id) || isLava(id) || (b != null && b.plant); }
        public static void queueFalling(int x, int y, int z) { if (y > WORLD_MIN_Y && y < WORLD_MAX_Y) fallingWake.Add(simKey(x, y, z)); }
        static bool startFallingBlock(int x, int y, int z)
        {
            if (FALLING_IDS == null) FALLING_IDS = new HashSet<int> { B.SAND, B.RED_SAND, B.GRAVEL };
            int id = getBlock(x, y, z); var meta = getBlockMeta(x, y, z); var vd = isVirtualId(id) ? virtualDefFromMeta(meta) : null;
            if (!(FALLING_IDS.Contains(id) || (vd != null && vd.falling)) || !fallingPassable(getBlock(x, y - 1, z))) return false;
            setBlock(x, y, z, B.AIR);
            fallingBlocks.Add(new FallingBlock { x = x, z = z, y = y, id = id, meta = vd != null ? meta.Clone() : null, vy = 0 });
            return true;
        }
        static void tickFallingBlocks(double dt)
        {
            fallingTickT += dt;
            if (fallingTickT >= 0.05)
            {
                fallingTickT = 0;
                fallingWakeScratch.Clear();
                foreach (var k in fallingWake) { fallingWakeScratch.Add(k); if (fallingWakeScratch.Count >= 96) break; }
                foreach (var k in fallingWakeScratch)
                {
                    fallingWake.Delete(k);
                    var q = decodeSimKey(k);
                    startFallingBlock(q[0], q[1], q[2]);
                }
            }
            if (fallingBlocks.Count > 1)
            {
                var sorted = fallingBlocks.OrderBy(b => b.y).ToList();
                fallingBlocks.Clear(); fallingBlocks.AddRange(sorted);
            }
            for (int i = 0; i < fallingBlocks.Count; i++)
            {
                var q = fallingBlocks[i];
                q.vy -= 28 * dt;
                double dy = q.vy * dt; int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(dy) / 0.35)); double st = dy / steps;
                bool landed = false; int qx = (int)q.x, qz = (int)q.z;
                for (int k = 0; k < steps; k++)
                {
                    double ny = q.y + st;
                    int yy = JS.floor(ny);
                    while (yy >= WORLD_MIN_Y && fallingPassable(getBlock(qx, yy, qz))) yy--;
                    int landY = yy + 1;
                    if (ny <= landY)
                    {
                        q.y = landY; q.vy = 0;
                        if (getBlock(qx, landY, qz) == B.AIR || fallingPassable(getBlock(qx, landY, qz)))
                        {
                            bool touchedWater = waterVolumeAtCell(qx, landY, qz);
                            setBlock(qx, landY, qz, q.id, q.meta);
                            if (q.meta != null && q.meta.Bool("v")) hardenConcretePowderAt(qx, landY, qz, touchedWater);
                            queueFalling(qx, landY + 1, qz);
                        }
                        else spawnWorldDrop(q.meta != null && q.meta.Bool("v") ? VK(q.meta.Str("v")) : BK(q.id), 1, qx + 0.5, landY + 0.5, qz + 0.5);
                        fallingBlocks.RemoveAt(i); i--;
                        landed = true;
                        break;
                    }
                    else q.y = ny;
                }
                if (!landed && q.y < WORLD_MIN_Y - 8) { fallingBlocks.RemoveAt(i); i--; }
            }
        }

        // ---------------- pressure plates ----------------
        public static readonly OrderedMap<string, double> pressurePlateActive = new OrderedMap<string, double>();
        public static double pressurePlateAccum = 0;
        static readonly HashSet<string> STONE_PLATE_VIRTUAL_KEYS = new HashSet<string> { "STONE_PRESSURE_PLATE", "POLISHED_BLACKSTONE_PRESSURE_PLATE" };
        sealed class PlateInfo { public int id; public JObj m; public bool stone; }
        static PlateInfo pressurePlateInfoAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z);
            if (isVirtualId(id))
            {
                var m = getBlockMeta(x, y, z); var d = virtualDefFromMeta(m);
                if (d == null || d.shape != "plate") return null;
                return new PlateInfo { id = id, m = m ?? new JObj(), stone = m != null && STONE_PLATE_VIRTUAL_KEYS.Contains(m.Str("v") ?? "") };
            }
            var bd = bdef(id);
            if (bd == null || bd.special != "plate") return null;
            return new PlateInfo { id = id, m = getBlockMeta(x, y, z) ?? new JObj(), stone = id == B.STONE_PLATE };
        }
        static bool setPressurePlatePressed(int x, int y, int z, bool on)
        {
            var q = pressurePlateInfoAt(x, y, z);
            if (q == null || q.m.Bool("pressed") == on) return false;
            var nm = q.m.Clone(); nm["pressed"] = on;
            setBlock(x, y, z, q.id, nm);
            return true;
        }
        static int primePlateTNT(int x, int y, int z)
        {
            int n = 0;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -3; dy <= 0; dy++)
                    {
                        int xx = x + dx, yy = y + dy, zz = z + dz;
                        if (getBlock(xx, yy, zz) != B.TNT) continue;
                        setBlock(xx, yy, zz, B.AIR);
                        primedTNT.Add(new PrimedTnt { x = xx + 0.5, y = yy + 0.5, z = zz + 0.5, t = 3 });
                        n++;
                    }
            return n;
        }
        static void pressurePlateNeighbors(int x, int y, int z, bool open)
        {
            setInteractableOpenAt(x + 1, y, z, open); setInteractableOpenAt(x - 1, y, z, open);
            setInteractableOpenAt(x, y, z + 1, open); setInteractableOpenAt(x, y, z - 1, open);
        }
        static void touchPressurePlate(int x, int y, int z, double now)
        {
            var q = pressurePlateInfoAt(x, y, z);
            if (q == null) return;
            var k = key3(x, y, z);
            if (!pressurePlateActive.Has(k))
            {
                setPressurePlatePressed(x, y, z, true);
                pressurePlateNeighbors(x, y, z, true);
                primePlateTNT(x, y, z);
                sfxClick();
            }
            pressurePlateActive.Set(k, now + 1000);
        }
        static void scanPlateUnderEntity(double cx, double cy, double cz, double r, bool living, double now)
        {
            int y = JS.floor(cy + 0.06), x0 = JS.floor(cx - r), x1 = JS.floor(cx + r), z0 = JS.floor(cz - r), z1 = JS.floor(cz + r);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    var q = pressurePlateInfoAt(x, y, z);
                    if (q == null || (!living && q.stone)) continue;
                    touchPressurePlate(x, y, z, now);
                }
        }
        static void tickPressurePlates(double dt)
        {
            pressurePlateAccum += dt;
            if (pressurePlateAccum < 0.05) return;
            pressurePlateAccum %= 0.05;
            double now = JS.now();
            if (!player.dead) scanPlateUnderEntity(player.x, player.y, player.z, PLAYER_COLLISION_RADIUS, true, now);
            foreach (var m in liveMobs) { if (m.dead) continue; scanPlateUnderEntity(m.x, m.y, m.z, mobWidth(m), true, now); }
            foreach (var q in worldDrops) scanPlateUnderEntity(q.x, q.y, q.z, 0.1, false, now);
            foreach (var kv in new List<KeyValuePair<string, double>>(pressurePlateActive))
            {
                if (kv.Value > now) continue;
                pressurePlateActive.Delete(kv.Key);
                var q = decodeSimKey(kv.Key); int x = q[0], y = q[1], z = q[2];
                setPressurePlatePressed(x, y, z, false);
                pressurePlateNeighbors(x, y, z, false);
                sfxClick();
            }
        }
        public static void tickWorldDynamics(double dt, double now)
        {
            tickFluids(now);
            tickFallingBlocks(dt);
            tickFarmland(dt);
            tickCrops(dt);
            tickSaplings(dt);
            tickColumnGrowth(dt);
            tickLeafDecay(dt);
            tickFire(dt);
            tickPressurePlates(dt);
        }

        // ---------------- furnaces ----------------
        static double furnaceUiRefresh = 0;
        public static void tickFurnaces(double dt)
        {
            bool changed = false;
            foreach (var kv in new List<KeyValuePair<string, Furnace>>(furnaces))
            {
                var f = kv.Value;
                if (f == null) continue;
                double oldBurn = f.burn, oldProgress = f.progress;
                if (f.burn > 0) f.burn = Math.Max(0, f.burn - dt);
                string outKey = null; if (f.input != null) SMELT.TryGetValue(f.input.key, out outKey);
                bool can = outKey != null && (f.output == null || (f.output.key == outKey && f.output.count < (idef(outKey).stack != 0 ? idef(outKey).stack : 64)));
                if (!can) f.progress = Math.Max(0, f.progress - dt * 0.3);
                else
                {
                    if (f.burn <= 0)
                    {
                        double fv = fuelValue(f.fuel);
                        if (fv > 0)
                        {
                            f.burnMax = f.burn = fv;
                            bool lava = f.fuel.key == "lava_bucket";
                            f.fuel.count--;
                            if (f.fuel.count <= 0) f.fuel = lava ? new Stack { key = "bucket", count = 1 } : null;
                            changed = true;
                        }
                        else f.progress = Math.Max(0, f.progress - dt * 0.3);
                    }
                    if (f.burn > 0)
                    {
                        f.progress += dt / 10;
                        if (f.progress >= 1)
                        {
                            f.progress = 0;
                            f.input.count--;
                            if (f.input.count <= 0) f.input = null;
                            if (f.output != null) f.output.count++; else f.output = new Stack { key = outKey, count = 1 };
                            inventoryDirty = true; changed = true;
                        }
                    }
                }
                var q = decodeSimKey(kv.Key); int x = q[0], y = q[1], z = q[2], cur = getBlock(x, y, z), want = f.burn > 0 ? B.FURNACE_LIT : B.FURNACE;
                if ((cur == B.FURNACE || cur == B.FURNACE_LIT) && cur != want) { setBlock(x, y, z, want, getBlockMeta(x, y, z)); changed = true; }
                if (Math.Floor(oldBurn) != Math.Floor(f.burn) || Math.Floor(oldProgress * 20) != Math.Floor(f.progress * 20)) changed = true;
            }
            if (changed) saveGameSoon();
            furnaceUiRefresh += dt;
            if (uiOpen && uiMode == "furnace" && furnaceUiRefresh >= 0.2) { furnaceUiRefresh = 0; renderCurrentUI(); }
        }
    }
}
