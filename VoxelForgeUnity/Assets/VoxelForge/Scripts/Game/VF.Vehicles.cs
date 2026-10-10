// Voxel Forge — Unity port. Minecarts, boats, bow, fishing rod and ender pearls.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class Vehicle
    {
        public string kind; public double x, y, z, yaw, pitch, vx, vy, vz, vyaw, speed;
        public int railDX, railDZ, railDY; public int cellX = 1000000000, cellY = 1000000000, cellZ = 1000000000;
        public double oarPhase, ampL, ampR;
    }
    public sealed class Bobber { public double x, y, z, vx, vy, vz, surfY, bobT, biteT, biteWin, landT; public string state; }
    public sealed class Pearl { public double x, y, z, vx, vy, vz, age, spin; }
    public sealed class VehicleHit { public Vehicle v; public double t; }

    public static partial class VF
    {
        public static readonly List<Vehicle> vehicles = new List<Vehicle>();
        public static readonly List<Pearl> pearlProjectiles = new List<Pearl>();
        static bool vehicleShiftLatch = false;
        public static Bobber fishingBobber = null;
        static double lastFishingUseAt = -1e9, enderPearlReadyAt = 0, lastPearlThrowAt = -1e9;
        const int VEHICLE_MAX = 128, PEARL_MAX = 24;
        const double CART_RAIL_Y = 0.08, CART_MAX = 8, CART_ACCEL = 12, CART_SLOPE_G = 10;
        sealed class BoatMode { public double acc, max, drag, idle, turnAcc, turnDrag, turnMax; }
        static readonly Dictionary<string, BoatMode> BOAT_MODE = new Dictionary<string, BoatMode>
        {
            { "water", new BoatMode { acc = 16, max = 8, drag = 2.1, idle = 2.1, turnAcc = 12, turnDrag = 5, turnMax = 2.4 } },
            { "ice", new BoatMode { acc = 16, max = 20, drag = 0.4, idle = 0.4, turnAcc = 9, turnDrag = 1.1, turnMax = 2.8 } },
            { "land", new BoatMode { acc = 16, max = 1, drag = 24, idle = 10, turnAcc = 8, turnDrag = 8, turnMax = 1.6 } },
        };
        public static void clearVehicles() { vehicles.Clear(); player.riding = null; vehicleShiftLatch = false; }
        public static void resetActiveItems()
        {
            fallingBlocks.Clear(); fallingWake.Clear();
            fishingBobber = null; lastFishingUseAt = -1e9;
            pearlProjectiles.Clear(); enderPearlReadyAt = 0; lastPearlThrowAt = -1e9;
            bowCharging = false; bowCharge = 0; bowChargeStartedAt = 0;
            player.riding = null;
        }
        public static List<object> serializeVehicles()
        {
            var a = new List<object>();
            for (int i = 0; i < vehicles.Count && i < VEHICLE_MAX; i++)
            {
                var v = vehicles[i];
                a.Add(new List<object> { v.kind == "minecart" ? 0.0 : 1.0, JS.round(v.x * 100) / 100, JS.round(v.y * 100) / 100, JS.round(v.z * 100) / 100, JS.round(v.yaw * 1000) / 1000 });
            }
            return a;
        }
        public static void restoreVehicles(List<object> a)
        {
            clearVehicles();
            if (a == null) return;
            for (int i = 0; i < a.Count && vehicles.Count < VEHICLE_MAX; i++)
            {
                var q = a[i] as List<object>;
                if (q == null || q.Count < 4 || !Json.IsFinite(q[1]) || !Json.IsFinite(q[2]) || !Json.IsFinite(q[3])) continue;
                double yaw = q.Count > 4 && Json.IsFinite(q[4]) ? Json.ToNum(q[4]) : 0;
                spawnVehicle(Json.ToNum(q[0]) == 0 ? "minecart" : "boat", Json.ToNum(q[1]), Json.ToNum(q[2]), Json.ToNum(q[3]), yaw, false);
            }
        }
        public static Vehicle spawnVehicle(string kind, double x, double y, double z, double yaw = 0, bool save = true)
        {
            if (vehicles.Count >= VEHICLE_MAX) return null;
            var v = new Vehicle { kind = kind, x = x, y = y, z = z, yaw = yaw };
            vehicles.Add(v);
            if (save) saveGameSoon();
            return v;
        }
        static readonly int[] RAIL_NEAR_DY = { 0, 1, -1, 2, -2 }, RAIL_CONN_DY = { 0, 1, -1 };
        static readonly double[] BOAT_BLOCK_FW = { -0.8, 0.8 }, BOAT_BLOCK_SIDE = { -0.5, 0.5 };
        static readonly double[][] CART_BLOCK_CORNERS = { new[] { -0.45, -0.45 }, new[] { 0.45, -0.45 }, new[] { -0.45, 0.45 }, new[] { 0.45, 0.45 } };
        /// <summary>Reused main-thread scratch for railConnections (pickRailDirection copies the chosen entry out immediately).</summary>
        static readonly List<int[]> RAIL_CONN_BUF = new List<int[]>(4);
        static readonly int[][] RAIL_CONN_POOL = { new int[3], new int[3], new int[3], new int[3] };
        static int railYNear(double fx, double fz, double fy)
        {
            int x = JS.floor(fx), z = JS.floor(fz), by = JS.floor(fy);
            foreach (var d in RAIL_NEAR_DY) if (getBlock(x, by + d, z) == B.RAIL) return by + d;
            return int.MinValue;
        }
        static List<int[]> railConnections(int x, int y, int z)
        {
            var outp = RAIL_CONN_BUF; outp.Clear();
            foreach (var d in ARM_DIRS)
                foreach (var dy in RAIL_CONN_DY)
                    if (getBlock(x + d[0], y + dy, z + d[1]) == B.RAIL) { var e = RAIL_CONN_POOL[outp.Count]; e[0] = d[0]; e[1] = d[1]; e[2] = dy; outp.Add(e); break; }
            return outp;
        }
        static void pickRailDirection(Vehicle v, int cx, int cy, int cz, List<int[]> con)
        {
            if (con.Count == 0) { v.railDX = v.railDZ = v.railDY = 0; return; }
            double fx = v.railDX, fz = v.railDZ;
            if (fx == 0 && fz == 0)
            {
                double sp = JS.hypot(v.vx, v.vz);
                if (sp > 0.05) { fx = v.vx / sp; fz = v.vz / sp; } else { fx = Math.Sin(v.yaw); fz = -Math.Cos(v.yaw); }
            }
            var best = con[0]; double score = -9;
            foreach (var q in con)
            {
                double d = q[0] * fx + q[1] * fz;
                if (q[0] == -v.railDX && q[1] == -v.railDZ && con.Count > 1) d -= 1.5;
                if (d > score) { score = d; best = q; }
            }
            v.railDX = best[0]; v.railDZ = best[1]; v.railDY = best[2];
            v.cellX = cx; v.cellY = cy; v.cellZ = cz;
        }
        static bool vehicleBlocked(Vehicle v, double nx, double nz)
        {
            if (v.kind == "boat")
            {
                double sy = Math.Sin(v.yaw), cy = Math.Cos(v.yaw), fx = sy, fz = -cy, rx = cy, rz = sy;
                foreach (var fw in BOAT_BLOCK_FW)
                    foreach (var side in BOAT_BLOCK_SIDE)
                    {
                        double x = nx + fx * fw + rx * side, z = nz + fz * fw + rz * side;
                        if (mobPointBlocked(x, v.y + 0.05, z) || mobPointBlocked(x, v.y + 0.25, z)) return true;
                    }
                return false;
            }
            foreach (var d in CART_BLOCK_CORNERS)
                if (mobPointBlocked(nx + d[0], v.y + 0.2, nz + d[1]) || mobPointBlocked(nx + d[0], v.y + 0.65, nz + d[1])) return true;
            return false;
        }
        static void updateMinecart(Vehicle v, double dt, bool mounted)
        {
            int cx = JS.floor(v.x), cz = JS.floor(v.z), ry = railYNear(v.x, v.z, v.y);
            if (ry == int.MinValue)
            {
                v.speed *= Math.Max(0, 1 - dt * 3);
                v.vy -= 24 * dt;
                double ny = v.y + v.vy * dt;
                if (mobPointBlocked(v.x, ny - 0.04, v.z)) { v.y = dropGroundTop(v.x, ny - 0.04, v.z); v.vy = 0; } else v.y = ny;
                double nx0 = v.x + v.vx * dt, nz0 = v.z + v.vz * dt;
                if (!vehicleBlocked(v, nx0, v.z)) v.x = nx0; else v.vx = 0;
                if (!vehicleBlocked(v, v.x, nz0)) v.z = nz0; else v.vz = 0;
                return;
            }
            var con = railConnections(cx, ry, cz);
            if (v.cellX != cx || v.cellY != ry || v.cellZ != cz || (v.railDX == 0 && v.railDZ == 0)) pickRailDirection(v, cx, ry, cz, con);
            if (mounted)
            {
                int f = (keyDown("KeyW") || keyDown("ArrowUp") ? 1 : 0) - (keyDown("KeyS") || keyDown("ArrowDown") ? 1 : 0);
                if (f != 0) v.speed += f * CART_ACCEL * dt; else v.speed *= Math.Pow(0.35, dt);
            }
            else v.speed *= Math.Pow(0.7, dt);
            if (v.railDY != 0) v.speed += -v.railDY * CART_SLOPE_G * dt;
            v.speed = Math.Max(-CART_MAX, Math.Min(CART_MAX, v.speed));
            if (Math.Abs(v.speed) < 0.01) v.speed = 0;
            int dx = v.railDX, dz = v.railDZ, dy = v.railDY;
            if (v.speed < 0) { dx = -dx; dz = -dz; dy = -dy; }
            double mag = JS.hypot(dx, dz); if (mag == 0) mag = 1;
            double spd = Math.Abs(v.speed), vx = dx / mag * spd, vz = dz / mag * spd, nx = v.x + vx * dt, nz = v.z + vz * dt;
            if (vehicleBlocked(v, nx, nz)) { v.speed = 0; v.vx = v.vz = 0; }
            else { v.x = nx; v.z = nz; v.vx = vx; v.vz = vz; }
            // Snap to rail center on the perpendicular axis; interpolate one-block slopes.
            if (Math.Abs(v.railDX) > 0) v.z += (cz + 0.5 - v.z) * Math.Min(1, dt * 10);
            else if (Math.Abs(v.railDZ) > 0) v.x += (cx + 0.5 - v.x) * Math.Min(1, dt * 10);
            double along = Math.Abs(v.railDX) > 0 ? Math.Abs(v.x - (cx + 0.5)) : Math.Abs(v.z - (cz + 0.5));
            v.y = ry + CART_RAIL_Y + dy * Math.Min(0.5, along);
            v.vy = 0;
            v.pitch = dy != 0 ? -Math.Sign((v.speed != 0 ? v.speed : 1) * dy) * Math.PI / 4 : 0;
            if (JS.hypot(v.vx, v.vz) > 0.05) v.yaw = Math.Atan2(v.vx, -v.vz);
        }
        static int waterSurfaceY(double x, double z, double y)
        {
            int bx = JS.floor(x), bz = JS.floor(z), top = JS.floor(y + 0.5);
            for (int yy = top; yy > top - 4 && yy >= WORLD_MIN_Y; yy--)
            {
                int id = getBlock(bx, yy, bz);
                if (isWater(id)) { while (yy + 1 < WORLD_MAX_Y && isWater(getBlock(bx, yy + 1, bz))) yy++; return yy; }
                var b = bdef(id);
                if (b != null && b.solid) return int.MinValue;
            }
            return int.MinValue;
        }
        static void updateBoat(Vehicle v, double dt, bool mounted)
        {
            int sy = waterSurfaceY(v.x, v.z, v.y); bool inWater = sy != int.MinValue;
            string mode = "land";
            if (inWater) { v.y += (sy + 0.82 - v.y) * Math.Min(1, dt * 8); v.vy = 0; mode = "water"; }
            else
            {
                v.vy -= 24 * dt;
                v.y += v.vy * dt;
                int g = getBlock(v.x, v.y - 0.08, v.z);
                if (g == B.ICE || g == B.PACKED_ICE) mode = "ice";
                if (mobPointBlocked(v.x, v.y - 0.08, v.z)) { v.y = dropGroundTop(v.x, v.y - 0.08, v.z); v.vy = 0; }
            }
            var c = BOAT_MODE[mode];
            int turn = 0, drive = 0;
            if (mounted)
            {
                turn = (keyDown("KeyD") || keyDown("ArrowRight") ? 1 : 0) - (keyDown("KeyA") || keyDown("ArrowLeft") ? 1 : 0);
                drive = (keyDown("KeyW") || keyDown("ArrowUp") ? 1 : 0) - (keyDown("KeyS") || keyDown("ArrowDown") ? 1 : 0);
                if (turn != 0) v.vyaw += turn * c.turnAcc * dt;
                double sx = Math.Sin(v.yaw), sz = -Math.Cos(v.yaw), along = v.vx * sx + v.vz * sz, acc = c.acc * (drive >= 0 ? drive : -drive * 0.125);
                if (drive > 0 && along < c.max) { v.vx += sx * acc * dt; v.vz += sz * acc * dt; }
                else if (drive < 0 && along > -c.max * 0.125) { v.vx -= sx * acc * dt; v.vz -= sz * acc * dt; }
            }
            v.ampL += ((drive != 0 || turn > 0 ? 1 : 0) - v.ampL) * Math.Min(1, dt * 6);
            v.ampR += ((drive != 0 || turn < 0 ? 1 : 0) - v.ampR) * Math.Min(1, dt * 6);
            if (v.ampL > 0.02 || v.ampR > 0.02) v.oarPhase += dt * 7;
            v.vyaw = Math.Max(-c.turnMax, Math.Min(c.turnMax, v.vyaw));
            if (v.vyaw != 0)
            {
                v.yaw += v.vyaw * dt;
                if (mounted) player.yaw += v.vyaw * dt;
                v.vyaw *= Math.Max(0, 1 - dt * c.turnDrag);
                if (Math.Abs(v.vyaw) < 0.01) v.vyaw = 0;
            }
            double drag = mounted ? c.drag : c.idle;
            v.vx *= Math.Max(0, 1 - dt * drag); v.vz *= Math.Max(0, 1 - dt * drag);
            double sp = JS.hypot(v.vx, v.vz);
            if (sp > c.max) { v.vx *= c.max / sp; v.vz *= c.max / sp; }
            double nx = v.x + v.vx * dt, nz = v.z + v.vz * dt;
            if (vehicleBlocked(v, nx, v.z)) v.vx = 0; else v.x = nx;
            if (vehicleBlocked(v, v.x, nz)) v.vz = 0; else v.z = nz;
        }
        static readonly double[][] DISMOUNT_OFF = { new[] { 0.9, 0 }, new[] { -0.9, 0 }, new[] { 0, 0.9 }, new[] { 0, -0.9 }, new[] { 0.9, 0.9 }, new[] { 0.9, -0.9 }, new[] { -0.9, 0.9 }, new[] { -0.9, -0.9 }, new[] { 1.4, 0 }, new[] { -1.4, 0 }, new[] { 0, 1.4 }, new[] { 0, -1.4 } };
        static double[] safeDismountSpot(Vehicle v)
        {
            foreach (var o in DISMOUNT_OFF)
            {
                double x = v.x + o[0], z = v.z + o[1];
                for (int oy = 2; oy >= -3; oy--)
                {
                    double y = Math.Floor(v.y) + oy + 0.02;
                    if (y <= WORLD_MIN_Y || y + 1.8 >= WORLD_MAX_Y || playerCollisionAt(x, y, z, 1.8) != null) continue;
                    int feet = getBlock(x, y + 0.05, z); bool support = mobPointBlocked(x, y - 0.08, z);
                    if (support || isWater(feet)) return new[] { x, y, z };
                }
            }
            return null;
        }
        public static bool dismountVehicle()
        {
            var v = player.riding;
            if (v == null) return false;
            var q = safeDismountSpot(v);
            if (q == null) { toast("Нет безопасного места для выхода"); return false; }
            player.riding = null;
            player.x = q[0]; player.y = q[1]; player.z = q[2];
            player.vx = player.vy = player.vz = 0;
            saveGameSoon();
            return true;
        }
        public static bool forceDismountVehicle() { if (player.riding == null) return false; player.riding = null; vehicleShiftLatch = false; return true; }
        public static void updateVehicles(double dt)
        {
            bool shift = keyDown("ShiftLeft") || keyDown("ShiftRight");
            if (player.riding != null && shift && !vehicleShiftLatch) dismountVehicle();
            vehicleShiftLatch = shift;
            foreach (var v in vehicles)
            {
                bool mounted = player.riding == v;
                if (!mounted && chunkFastGet(JS.floor(v.x / CHUNK), JS.floor(v.z / CHUNK)) == null) continue;
                if (v.kind == "minecart") updateMinecart(v, dt, mounted); else updateBoat(v, dt, mounted);
                if (mounted)
                {
                    player.x = v.x; player.z = v.z; player.y = v.y + (v.kind == "minecart" ? -0.15 : -0.45);
                    player.vx = player.vy = player.vz = 0;
                    player.onGround = true; player.inWater = false; player.inLava = false;
                }
            }
        }
        static double rayAabbVehicle(double[] o, double[] d, Vehicle v)
        {
            double hx = v.kind == "minecart" ? 0.6 : 0.82, hy = v.kind == "minecart" ? 0.7 : 0.62, hz = v.kind == "minecart" ? 0.6 : 1.02;
            var min = new[] { v.x - hx, v.y - 0.2, v.z - hz }; var max = new[] { v.x + hx, v.y + hy, v.z + hz };
            double lo = 0, hi = 6;
            for (int a = 0; a < 3; a++)
            {
                double oo = o[a], dd = d[a];
                if (Math.Abs(dd) < 1e-8) { if (oo < min[a] || oo > max[a]) return -1; continue; }
                double t0 = (min[a] - oo) / dd, t1 = (max[a] - oo) / dd;
                if (t0 > t1) { var t = t0; t0 = t1; t1 = t; }
                lo = Math.Max(lo, t0); hi = Math.Min(hi, t1);
                if (lo > hi) return -1;
            }
            return lo;
        }
        public static VehicleHit raycastVehicleHit()
        {
            var d = viewDir(); var o = new[] { player.x, player.y + 1.62, player.z };
            Vehicle best = null; double t = 6;
            foreach (var v in vehicles)
            {
                if (v == player.riding) continue;
                double q = rayAabbVehicle(o, d, v);
                if (q >= 0 && q < t) { t = q; best = v; }
            }
            return best != null ? new VehicleHit { v = best, t = t } : null;
        }
        public static Vehicle raycastVehicle() { var h = raycastVehicleHit(); return h != null ? h.v : null; }
        static bool interactVehicle(Hit blockHit = null)
        {
            var q = raycastVehicleHit();
            if (q == null || (blockHit != null && JS.isFinite(blockHit.t) && blockHit.t < q.t)) return false;
            var v = q.v;
            if (v.kind == "boat") v.yaw = player.yaw;
            player.riding = v; player.flying = false; player.vx = player.vy = player.vz = 0;
            saveGameSoon();
            toast(v.kind == "minecart" ? "Вы сели в вагонетку · Shift — выйти" : "Вы сели в лодку · Shift — выйти");
            return true;
        }
        static bool destroyVehicle(Vehicle v)
        {
            if (v == null) return false;
            if (player.riding == v && !dismountVehicle()) return false;
            int i = vehicles.IndexOf(v);
            if (i < 0) return false;
            vehicles.RemoveAt(i);
            if (!player.creative) spawnWorldDrop(v.kind == "minecart" ? "minecart" : "boat", 1, v.x, v.y + 0.35, v.z);
            saveGameSoon();
            toast(v.kind == "minecart" ? "Вагонетка сломана" : "Лодка сломана");
            return true;
        }
        static bool useVehicleItem(Hit h)
        {
            var st = selectedStack();
            if (st == null) return false;
            if (st.key == "minecart")
            {
                double x, z, y, yaw;
                if (h != null && h.id == B.RAIL) { x = h.x + 0.5; z = h.z + 0.5; y = h.y + CART_RAIL_Y; yaw = 0; }
                else if (h != null && h.prev != null) { x = h.prev.x + 0.5; z = h.prev.z + 0.5; y = h.prev.y + 0.08; yaw = JS.round(player.yaw / (Math.PI / 2)) * (Math.PI / 2); }
                else return false;
                if (spawnVehicle("minecart", x, y, z, yaw) != null) { consumeSelectedOne(); toast("Вагонетка установлена"); return true; }
            }
            if (st.key == "boat")
            {
                var f = raycastSourceFluid(); double x, z, y;
                if (f != null && isWater(f.id))
                {
                    x = f.x + 0.5; z = f.z + 0.5; int sy = f.y;
                    while (sy + 1 < WORLD_MAX_Y && isWater(getBlock(f.x, sy + 1, f.z))) sy++;
                    y = sy + 0.82;
                }
                else if (h != null && h.prev != null) { x = h.prev.x + 0.5; z = h.prev.z + 0.5; y = h.prev.y + 0.82; }
                else return false;
                if (spawnVehicle("boat", x, y, z, player.yaw) != null) { consumeSelectedOne(); toast("Лодка установлена"); return true; }
            }
            return false;
        }

        // ---------------- bow ----------------
        public static bool beginBowCharge()
        {
            if (bowCharging) return true;
            if (!player.creative && countItem("arrow") <= 0) { toast("Нужны стрелы"); return false; }
            bowCharging = true; bowChargeStartedAt = JS.now(); bowCharge = 0;
            updateMineBar();
            return true;
        }
        public static void cancelBowCharge() { bowCharging = false; bowCharge = 0; bowChargeStartedAt = 0; updateMineBar(); }
        public static bool releaseBowShot()
        {
            if (!bowCharging) return false;
            double held = Math.Max(0, (JS.now() - bowChargeStartedAt) / 1000);
            bowCharging = false; bowCharge = 0; bowChargeStartedAt = 0;
            updateMineBar();
            var ss = selectedStack();
            if (ss == null || ss.key != "bow") return false;
            double power = (held * held + 2 * held) / 3;
            if (power > 1) power = 1;
            if (power < 0.1) return true;
            if (!player.creative && !removeItem("arrow", 1)) { toast("Нужны стрелы"); return false; }
            var d = viewDir(); double speed = 50 * power, eyeY = player.y + (player.h != 0 ? player.h : 1.8) - 0.18;
            if (projectiles.Count >= 160) projectiles.RemoveRange(0, projectiles.Count - 159);
            projectiles.Add(new Projectile
            {
                x = player.x + d[0] * 0.4, y = eyeY + d[1] * 0.4 - 0.12, z = player.z + d[2] * 0.4, vx = d[0] * speed, vy = d[1] * speed, vz = d[2] * speed,
                age = 0, src = null, damage = Math.Max(1, JS.round(9 * power)), stuck = false, fromPlayer = true,
            });
            damageHeldTool(1);
            drawHotbar();
            saveGameSoon();
            return true;
        }

        // ---------------- fishing ----------------
        static void castFishing()
        {
            var d = viewDir();
            fishingBobber = new Bobber { x = player.x + d[0] * 0.4, y = player.y + 1.52 + d[1] * 0.4, z = player.z + d[2] * 0.4, vx = d[0] * 13, vy = d[1] * 13 + 3.2, vz = d[2] * 13, state = "fly" };
            toast("Удочка заброшена");
        }
        static string fishingLoot()
        {
            double a = JS.random(), b = JS.random();
            if (a < 0.6) return "fish";
            if (a < 0.85) return "salmon";
            if (a < 0.95) return new[] { "stick", "bone", "clay_ball", "leather", "string" }[Math.Min(4, (int)Math.Floor(b * 5))];
            return new[] { "diamond", "gold_ingot", "emerald" }[Math.Min(2, (int)Math.Floor(b * 3))];
        }
        static bool reelFishing()
        {
            var q = fishingBobber;
            if (q == null) return false;
            if (q.state == "float" && q.biteWin > 0)
            {
                var key = fishingLoot();
                double dx = player.x - q.x, dy = player.y + 1 - q.y, dz = player.z - q.z, dist = JS.hypot(dx, dy, dz); if (dist == 0) dist = 1;
                double k = Math.Min(1.6, Math.Max(0.7, 0.5 + dist * 0.06));
                spawnWorldDrop(key, 1, q.x, q.y + 0.2, q.z, 0, new[] { dx / k, dy / k + 9 * k, dz / k });
                damageHeldTool(1);
                toast("Поймано: " + idef(key).name);
            }
            else toast("Клёва нет");
            fishingBobber = null;
            return true;
        }
        static bool useFishingRod()
        {
            double now = JS.now();
            if (now - lastFishingUseAt < 300) return true;
            lastFishingUseAt = now;
            if (fishingBobber != null) return reelFishing();
            castFishing();
            return true;
        }
        public static void updateFishing(double dt)
        {
            var q = fishingBobber;
            if (q == null) return;
            double dx = q.x - player.x, dy = q.y - player.y, dz = q.z - player.z;
            int here = getBlock(JS.floor(q.x), JS.floor(q.y), JS.floor(q.z));
            var ss = selectedStack();
            if (ss == null || ss.key != "fishing_rod" || dx * dx + dy * dy + dz * dz > 1024 || q.y < WORLD_MIN_Y - 8 || isLava(here)) { fishingBobber = null; return; }
            if (q.state == "land") { q.landT -= dt; if (q.landT <= 0) fishingBobber = null; return; }
            if (q.state == "fly")
            {
                q.vy -= 16 * dt;
                double nx = q.x + q.vx * dt, ny = q.y + q.vy * dt, nz = q.z + q.vz * dt;
                int id = getBlock(JS.floor(nx), JS.floor(ny), JS.floor(nz));
                var bd = bdef(id);
                if (isWater(id))
                {
                    int sy = JS.floor(ny);
                    while (sy + 1 < WORLD_MAX_Y && isWater(getBlock(JS.floor(nx), sy + 1, JS.floor(nz)))) sy++;
                    q.x = nx; q.z = nz; q.surfY = sy + 0.85; q.y = q.surfY; q.state = "float"; q.vx = q.vy = q.vz = 0; q.bobT = 0; q.biteT = 4 + JS.random() * 10;
                }
                else if (bd != null && bd.solid)
                {
                    q.vx = q.vz = 0;
                    if (q.vy > 0) q.vy = 0;
                    int bx = JS.floor(q.x), by = JS.floor(q.y - 0.1), bz = JS.floor(q.z);
                    var bb = bdef(getBlock(bx, by, bz));
                    if (bb != null && bb.solid) { q.y = by + 1.05; q.state = "land"; q.landT = 0.6; } else q.y = ny;
                }
                else { q.x = nx; q.y = ny; q.z = nz; }
            }
            else if (q.state == "float")
            {
                q.bobT += dt;
                if (q.biteWin > 0)
                {
                    q.biteWin -= dt;
                    q.y = q.surfY - 0.22 + Math.Sin(q.bobT * 16) * 0.03;
                    if (q.biteWin <= 0) q.biteT = 4 + JS.random() * 10;
                }
                else
                {
                    q.y = q.surfY + Math.Sin(q.bobT * 2.3) * 0.04;
                    q.biteT -= dt;
                    if (q.biteT <= 0) { q.biteWin = 1.4; toast("Клюёт!"); }
                }
            }
        }

        // ---------------- ender pearls ----------------
        static double[] teleportSafeNear(double[] pos, double[] dir)
        {
            foreach (var vo in new[] { 0, 0.5, 1, -0.5, 2, -1 })
                foreach (var back in new[] { 0, 0.15, 0.35, 0.6, 0.9 })
                {
                    double x = pos[0] - dir[0] * back, y = pos[1] - dir[1] * back + vo, z = pos[2] - dir[2] * back;
                    if (y < WORLD_MIN_Y + 1 || y > WORLD_MAX_Y - 2) continue;
                    if (playerCollisionAt(x, y, z, 1.8) == null) return new[] { x, y, z, 1.8 };
                    if (playerCollisionAt(x, y, z, 0.85) == null) return new[] { x, y, z, 0.85 };
                }
            return null;
        }
        static bool throwEnderPearl()
        {
            double now = JS.now();
            if (now - lastPearlThrowAt < 100 || now < enderPearlReadyAt) return true;
            lastPearlThrowAt = now; enderPearlReadyAt = now + 1000;
            var d = viewDir(); const double j = 0.0172275;
            Func<double> r = () => JS.random() - JS.random();
            double vx = d[0] + r() * j, vy = d[1] + r() * j, vz = d[2] + r() * j, L = JS.hypot(vx, vy, vz); if (L == 0) L = 1;
            if (pearlProjectiles.Count >= PEARL_MAX) pearlProjectiles.RemoveAt(0);
            pearlProjectiles.Add(new Pearl
            {
                x = player.x + d[0] * 0.4, y = player.y + 1.5 + d[1] * 0.4 - 0.12, z = player.z + d[2] * 0.4,
                vx = vx / L * 30 + player.vx, vy = vy / L * 30 + (player.onGround ? 0 : player.vy), vz = vz / L * 30 + player.vz, age = 0, spin = 0,
            });
            consumeSelectedOne();
            return true;
        }
        static bool pointHitsMob(double x, double y, double z, Mob m)
        {
            double r = mobWidth(m) + 0.15;
            return Math.Abs(x - m.x) < r && Math.Abs(z - m.z) < r && y > m.y - 0.15 && y < m.y + mobHeight(m) + 0.15;
        }
        public static void updatePearls(double dt)
        {
            for (int i = pearlProjectiles.Count - 1; i >= 0; i--)
            {
                var p = pearlProjectiles[i];
                p.age += dt; p.spin += dt * 6;
                if (p.age > 20 || p.y < WORLD_MIN_Y) { pearlProjectiles.RemoveAt(i); continue; }
                p.vy -= 12 * dt;
                double drag = Math.Pow(isWater(getBlock(p.x, p.y, p.z)) ? 0.0115 : 0.818, dt);
                p.vx *= drag; p.vy *= drag; p.vz *= drag;
                double speed = JS.hypot(p.vx, p.vy, p.vz); int steps = Math.Max(1, (int)Math.Ceiling(speed * dt / 0.3)); double sd = dt / steps;
                double[] hit = null; var dir = new double[] { 0, -1, 0 };
                for (int k = 0; k < steps && hit == null; k++)
                {
                    double ox = p.x, oy = p.y, oz = p.z;
                    p.x += p.vx * sd; p.y += p.vy * sd; p.z += p.vz * sd;
                    double L = JS.hypot(p.vx, p.vy, p.vz); if (L == 0) L = 1;
                    dir = new[] { p.vx / L, p.vy / L, p.vz / L };
                    if (mobPointBlocked(p.x, p.y, p.z))
                    {
                        double lo = 0, hi = 1;
                        for (int n = 0; n < 7; n++) { double t = (lo + hi) / 2; if (mobPointBlocked(ox + (p.x - ox) * t, oy + (p.y - oy) * t, oz + (p.z - oz) * t)) hi = t; else lo = t; }
                        hit = new[] { ox + (p.x - ox) * lo, oy + (p.y - oy) * lo, oz + (p.z - oz) * lo };
                        break;
                    }
                    foreach (var m in liveMobs) if (!m.dead && pointHitsMob(p.x, p.y, p.z, m)) { hit = new[] { p.x, p.y, p.z }; break; }
                }
                if (hit != null)
                {
                    var q = teleportSafeNear(hit, dir);
                    if (q != null && !player.dead)
                    {
                        if (player.riding != null) forceDismountVehicle();
                        player.x = q[0]; player.y = q[1]; player.z = q[2]; player.h = q[3] != 0 ? q[3] : 1.8;
                        player.vx = player.vy = player.vz = 0;
                        if (!player.creative) damagePlayer(5, "жемчуг Эндера");
                        saveGameSoon();
                    }
                    pearlProjectiles.RemoveAt(i);
                }
            }
        }
        static bool useActiveItem(Hit h)
        {
            var st = selectedStack();
            if (st == null) return false;
            if (st.key == "minecart" || st.key == "boat") return useVehicleItem(h);
            if (st.key == "fishing_rod") return useFishingRod();
            if (st.key == "ender_pearl") return throwEnderPearl();
            return false;
        }
    }
}
