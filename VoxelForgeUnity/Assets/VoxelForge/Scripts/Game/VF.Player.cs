// Voxel Forge — Unity port. Player state, spawn search and ray casting.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class Player : Entity
    {
        public double yaw, pitch;
        public bool onGround, flying;
        public double hp = 20, hunger = 20, air = 10;
        public bool dead, creative;
        public double[] spawn;
        public double fallDist, invuln, regenT, starveT, hungerT, drownT, exh, fireT;
        public bool sprinting, swimming, sneaking, inWater, headInWater, inLava, hitWall;
        public double h = 1.8, boostUntil, voidT, fireDamageT, cactusT;
        public double? lastSurvY;
        public Vehicle riding;
        public int camMode;
        public bool sleeping;
        public string deathReason;
    }
    public sealed class Hit
    {
        public int x, y, z, id; public Hit prev; public double t; public List<double[]> boxes;
        public Hit() { }
        public Hit(int x, int y, int z, int id = 0) { this.x = x; this.y = y; this.z = z; this.id = id; }
    }
    public sealed class MobHit { public double t; public Mob m; public Chunk owner; }

    public static partial class VF
    {
        public static double[] SPAWN_ANCHOR = { 0, 0 }, SPAWN = { 0.5, SEA + 3, 0.5 };
        public static bool worldReady = false, initialSpawnPending = false, resumeLoadPending = false;
        public static int initialSpawnCX = 0, initialSpawnCZ = 0;
        public static double[] worldSpawnOverride = null;
        public static readonly int INITIAL_GEN_RADIUS = Math.Min(DEFAULT_RD, 4);
        public static readonly Player player = new Player { x = 0.5, y = SEA + 3, z = 0.5, spawn = new double[] { 0.5, SEA + 3, 0.5 } };

        // ---------------- spawn ----------------
        public static bool spawnPassable(int id)
        {
            var b = bdef(id);
            return id == B.AIR || (b != null && (b.plant || b.special == "torch" || b.special == "ladder") && !isWater(id) && !isLava(id) && id != B.FIRE && id != B.COBWEB);
        }
        public static bool spawnGroundOK(int id)
        {
            var b = bdef(id);
            return b != null && b.solid && !b.transparent && !b.plant && id != B.ICE && id != B.PACKED_ICE && id != B.CACTUS && !isTreeLeaves(id) && !isTreeLog(id) && id != B.LAVA && id != B.WATER;
        }
        public static double[] safeSpawnAt(double fx, double fz)
        {
            int x = JS.floor(fx), z = JS.floor(fz);
            int y = heightAt(x, z);
            if (y < SEA) return null;
            int g = baseBlock(x, y, z), a = baseBlock(x, y + 1, z), h = baseBlock(x, y + 2, z);
            if (spawnGroundOK(g) && spawnPassable(a) && spawnPassable(h) && !isWater(a) && !isWater(h) && a != B.LAVA && h != B.LAVA) return new[] { x + 0.5, y + 1.05, z + 0.5 };
            return null;
        }
        public static double[] findSpawnNear(double x = 0, double z = 0, int maxR = 48)
        {
            for (int r = 0; r <= maxR; r++)
            {
                int steps = Math.Max(1, r * 8);
                for (int n = 0; n < steps; n++)
                {
                    double a = n * Math.PI * 2 / steps;
                    var s = safeSpawnAt(x + JS.round(Math.Cos(a) * r), z + JS.round(Math.Sin(a) * r));
                    if (s != null) return s;
                }
            }
            return null;
        }
        // Original BlockCraft spawn phase 1 (Bl): require both the climate/base surface and the coarse 3D-density surface to be land.
        public static int[] findLandSpawnAnchor(int x = 0, int z = 0)
        {
            for (int r = 0; r <= 3000; r += r < 200 ? 1 : 12)
            {
                int stride = r < 200 ? 1 : 12, steps = Math.Max(1, (int)Math.Ceiling(2 * Math.PI * r / stride));
                for (int n = 0; n < steps; n++)
                {
                    double a = n * 2 * Math.PI / steps;
                    int px = x + (int)JS.round(Math.Cos(a) * r), pz = z + (int)JS.round(Math.Sin(a) * r);
                    var col = columnInfo(px, pz);
                    if (col.origH >= SEA && heightAt(px, pz) >= SEA) return new[] { px, pz };
                }
            }
            return null;
        }
        public static double[] findSpawn()
        {
            var p = findLandSpawnAnchor(0, 0) ?? new[] { 0, 0 };
            return new[] { p[0] + 0.5, Math.Max(columnInfo(p[0], p[1]).origH, SEA) + 2, p[1] + 0.5 };
        }
        static bool safeWorldSpawnCell(int x, int y, int z)
        {
            int g = getBlock(x, y - 1, z), a = getBlock(x, y, z), h = getBlock(x, y + 1, z);
            return spawnGroundOK(g) && spawnPassable(a) && spawnPassable(h) && !isWater(a) && !isWater(h) && a != B.LAVA && h != B.LAVA;
        }
        static int loadedSpawnBlock(int x, int y, int z)
        {
            if (y < WORLD_MIN_Y) return B.BEDROCK;
            if (y >= WORLD_MAX_Y) return B.AIR;
            int ed; if (edits.TryGetValue(key3(x, y, z), out ed)) return ed;
            int cx = JS.floor(x / (double)CHUNK), cz = JS.floor(z / (double)CHUNK);
            var c = chunkFastGet(cx, cz);
            if (c == null) return -1;
            return chunkGet(c, x - cx * CHUNK, y, z - cz * CHUNK);
        }
        static bool safeLoadedSpawnCell(int x, int y, int z)
        {
            int g = loadedSpawnBlock(x, y - 1, z), a = loadedSpawnBlock(x, y, z), h = loadedSpawnBlock(x, y + 1, z);
            if (g < 0 || a < 0 || h < 0) return false;
            return spawnGroundOK(g) && spawnPassable(a) && spawnPassable(h) && !isWater(a) && !isWater(h) && a != B.LAVA && h != B.LAVA;
        }
        // Original BlockCraft spawn phase 2 (pg): scan the actual voxel columns, reject cave floors >6 blocks below the coarse density surface.
        static bool spawnNaturalLandColumn(int x, int z) { var c = columnInfo(x, z); return c.origH >= SEA && heightAt(x, z) >= SEA; }
        static bool startupSupportEdited(int x, double feetY, int z) { return edits.ContainsKey(key3(x, JS.floor(feetY) - 1, z)); }
        public static bool startupPositionValidLoaded(double x, double y, double z, bool requireNaturalLand = false)
        {
            if (!JS.isFinite(x) || !JS.isFinite(y) || !JS.isFinite(z)) return false;
            if (y < WORLD_MIN_Y + 1 || y + 1.78 >= WORLD_MAX_Y) return false;
            int fx = JS.floor(x), fy = JS.floor(y), fz = JS.floor(z);
            if (!safeLoadedSpawnCell(fx, fy, fz) || collides(x, y, z)) return false;
            if (requireNaturalLand) return spawnNaturalLandColumn(fx, fz) && fy - 1 >= SEA;
            // A loaded game may legitimately stand on a player-built ocean platform.
            return spawnNaturalLandColumn(fx, fz) || startupSupportEdited(fx, fy, fz);
        }
        // Startup spawn scans the ACTUAL generated chunks. Natural new-world spawn is never accepted below sea level.
        public static double[] resolveInitialSpawnLoaded(double fx, double fz)
        {
            int x = JS.floor(fx), z = JS.floor(fz);
            for (int r = 0; r <= 64; r++)
            {
                int steps = Math.Max(1, r * 8);
                for (int n = 0; n < steps; n++)
                {
                    double a = n * 2 * Math.PI / steps;
                    int px = x + (int)JS.round(Math.Cos(a) * r), pz = z + (int)JS.round(Math.Sin(a) * r);
                    if (!spawnNaturalLandColumn(px, pz)) continue;
                    int surface = heightAt(px, pz);
                    for (int gy = WORLD_MAX_Y - 3; gy >= Math.Max(WORLD_MIN_Y + 1, SEA); gy--)
                        if (safeLoadedSpawnCell(px, gy + 1, pz))
                        {
                            if (gy < surface - 6) break;
                            return new[] { px + 0.5, gy + 1.05, pz + 0.5 };
                        }
                }
            }
            return null;
        }
        public static double[] findSafeWorldSpawnNear(double fx, double fy, double fz, int maxR = 10)
        {
            int x = JS.floor(fx), z = JS.floor(fz), y = Math.Max(WORLD_MIN_Y + 2, Math.Min(WORLD_MAX_Y - 3, JS.floor(fy)));
            for (int r = 0; r <= maxR; r++)
            {
                var pts = new List<int[]>();
                if (r == 0) pts.Add(new[] { x, z });
                else for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++) if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == r) pts.Add(new[] { x + dx, z + dz });
                foreach (var p in pts)
                    for (int dy = 0; dy <= 6; dy++)
                    {
                        if (dy == 0) { if (y > WORLD_MIN_Y + 1 && y < WORLD_MAX_Y - 2 && safeWorldSpawnCell(p[0], y, p[1])) return new[] { p[0] + 0.5, y + 0.05, p[1] + 0.5 }; continue; }
                        foreach (var yy in new[] { y + dy, y - dy })
                            if (yy > WORLD_MIN_Y + 1 && yy < WORLD_MAX_Y - 2 && safeWorldSpawnCell(p[0], yy, p[1])) return new[] { p[0] + 0.5, yy + 0.05, p[1] + 0.5 };
                    }
            }
            return null;
        }

        // ---------------- ray casting ----------------
        static double? rayAABB(double[] ro, double[] rd, Mob m, double maxD)
        {
            if (!MOB_INFO.ContainsKey(m.type)) return null;
            double r = mobWidth(m);
            var mn = new[] { m.x - r, m.y, m.z - r }; var mx = new[] { m.x + r, m.y + mobHeight(m), m.z + r };
            double t0 = 0, t1 = maxD;
            for (int a = 0; a < 3; a++)
            {
                double o = ro[a], d = rd[a];
                if (Math.Abs(d) < 1e-8) { if (o < mn[a] || o > mx[a]) return null; continue; }
                double q0 = (mn[a] - o) / d, q1 = (mx[a] - o) / d;
                if (q0 > q1) { var q = q0; q0 = q1; q1 = q; }
                t0 = Math.Max(t0, q0); t1 = Math.Min(t1, q1);
                if (t0 > t1) return null;
            }
            return t0 >= 0 && t0 <= maxD ? (double?)t0 : null;
        }
        public static MobHit raycastMob(double max = 4)
        {
            var rd = viewDir(); var ro = new[] { player.x, player.y + 1.62, player.z };
            MobHit best = null;
            foreach (var m in liveMobs)
            {
                if (m.dead) continue;
                double dx = m.x - ro[0], dz = m.z - ro[2];
                if (dx * dx + dz * dz > (max + 2) * (max + 2)) continue;
                var t = rayAABB(ro, rd, m, max);
                if (t != null && (best == null || t.Value < best.t)) best = new MobHit { t = t.Value, m = m, owner = m._owner };
            }
            return best;
        }
        public static string mobLootName(Mob m) { MobInfo i; return MOB_INFO.TryGetValue(m.type, out i) && i.loot != null ? i.loot : m.type; }
        public static bool intersectsPlayer(int x, int y, int z)
        {
            return x + 1 > player.x - 0.31 && x < player.x + 0.31 && y + 1 > player.y && y < player.y + 1.78 && z + 1 > player.z - 0.31 && z < player.z + 0.31;
        }
        public static double[] viewDir()
        {
            double cp = Math.Cos(player.pitch);
            return new[] { Math.Sin(player.yaw) * cp, -Math.Sin(player.pitch), -Math.Cos(player.yaw) * cp };
        }
        public static double? rayBoxDistance(double[] ro, double[] rd, double[] mn, double[] mx, double maxD)
        {
            double t0 = 0, t1 = maxD;
            for (int a = 0; a < 3; a++)
            {
                double o = ro[a], d = rd[a];
                if (Math.Abs(d) < 1e-9) { if (o < mn[a] || o > mx[a]) return null; continue; }
                double q0 = (mn[a] - o) / d, q1 = (mx[a] - o) / d;
                if (q0 > q1) { var q = q0; q0 = q1; q1 = q; }
                t0 = Math.Max(t0, q0); t1 = Math.Min(t1, q1);
                if (t0 > t1) return null;
            }
            return t0 >= 0 && t0 <= maxD ? (double?)t0 : null;
        }
        public static double rayBoxDistanceScalar(double ox, double oy, double oz, double dx, double dy, double dz, double x0, double y0, double z0, double x1, double y1, double z1, double maxD)
        {
            double t0 = 0, t1 = maxD, q0, q1;
            if (Math.Abs(dx) < 1e-9) { if (ox < x0 || ox > x1) return double.NaN; }
            else { q0 = (x0 - ox) / dx; q1 = (x1 - ox) / dx; if (q0 > q1) { var q = q0; q0 = q1; q1 = q; } if (q0 > t0) t0 = q0; if (q1 < t1) t1 = q1; if (t0 > t1) return double.NaN; }
            if (Math.Abs(dy) < 1e-9) { if (oy < y0 || oy > y1) return double.NaN; }
            else { q0 = (y0 - oy) / dy; q1 = (y1 - oy) / dy; if (q0 > q1) { var q = q0; q0 = q1; q1 = q; } if (q0 > t0) t0 = q0; if (q1 < t1) t1 = q1; if (t0 > t1) return double.NaN; }
            if (Math.Abs(dz) < 1e-9) { if (oz < z0 || oz > z1) return double.NaN; }
            else { q0 = (z0 - oz) / dz; q1 = (z1 - oz) / dz; if (q0 > q1) { var q = q0; q0 = q1; q1 = q; } if (q0 > t0) t0 = q0; if (q1 < t1) t1 = q1; if (t0 > t1) return double.NaN; }
            return t0 >= 0 && t0 <= maxD ? t0 : double.NaN;
        }
        static double nz9(double v) { return v != 0 && !double.IsNaN(v) ? v : 1e-9; }
        public static Hit raycast(double max = 7)
        {
            double cp = Math.Cos(player.pitch), dx = nz9(Math.Sin(player.yaw) * cp), dy = nz9(-Math.Sin(player.pitch)), dz = nz9(-Math.Cos(player.yaw) * cp);
            double ex = player.x, ey = player.y + 1.62, ez = player.z, ix = 1 / dx, iy = 1 / dy, iz = 1 / dz;
            int x = JS.floor(ex), y = JS.floor(ey), z = JS.floor(ez), sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            double tx = Math.Abs(ix), ty = Math.Abs(iy), tz = Math.Abs(iz);
            double nx = (sx > 0 ? x + 1 - ex : ex - x) * tx, ny = (sy > 0 ? y + 1 - ey : ey - y) * ty, nzv = (sz > 0 ? z + 1 - ez : ez - z) * tz, t = 0;
            int px = 0, py = 0, pz = 0; bool hasPrev = false;
            for (int step = 0; step < 96 && t <= max; step++)
            {
                int id = getBlock(x, y, z);
                if (id != B.AIR && !isFluidWater(id) && !isLava(id))
                {
                    double best = double.NaN;
                    var boxes = selectionBoxesAt(id, x, y, z);
                    for (int bi = 0; bi < boxes.Count; bi++)
                    {
                        var b = boxes[bi];
                        double q = rayBoxDistanceScalar(ex, ey, ez, dx, dy, dz, x + b[0], y + b[1], z + b[2], x + b[3], y + b[4], z + b[5], max);
                        if (!double.IsNaN(q) && (double.IsNaN(best) || q < best)) best = q;
                    }
                    if (!double.IsNaN(best)) return new Hit { x = x, y = y, z = z, id = id, prev = hasPrev ? new Hit(px, py, pz) : null, t = best, boxes = boxes };
                }
                px = x; py = y; pz = z; hasPrev = true;
                if (nx < ny && nx < nzv) { x += sx; t = nx; nx += tx; }
                else if (ny < nzv) { y += sy; t = ny; ny += ty; }
                else { z += sz; t = nzv; nzv += tz; }
            }
            return null;
        }
    }
}
