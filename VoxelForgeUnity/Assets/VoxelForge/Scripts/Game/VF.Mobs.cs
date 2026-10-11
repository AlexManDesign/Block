// Voxel Forge — Unity port. Mobs: state, persistence, sight, collision, A* pathing, land/swim physics,
// AI, projectiles, fixed-step simulation and natural spawning.
using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxelForge
{
    public class Entity { public double x, y, z, vx, vy, vz; }

    public sealed class Mob : Entity
    {
        public string type;
        public double? ox, oy, oz, oangle, targetAngle;
        public double angle, speed, turn, phase, walkPhase, swimPhase, pitch, hp, hurtT;
        public bool moving, onGround;
        public double wishX, wishY, wishZ, atkT, shootT, aiT, tgtX, tgtZ, stuckT, detourT, detX, detZ, jumpT, fleeT, loveT, breedCd, growT, woolT, eggT, fuse = -1;
        public bool baby, sheared;
        public float[] path; public int pathI; public double pathT, pathTx, pathTy, pathTz;
        public double dryT, swimT; public double? escX; public double escY, escZ, escT, escDX, escDZ;
        public bool fleeing, climbing;
        public double hopT, sqY = 1, leapT, angryT, tpT, stareT, huntT;
        public Entity prey;
        public double lavaT, burnT, age, kbT;
        public bool lastHitByPlayer, persist, dynamicHostile, dead;
        public string spawnId, color;
        public Chunk _owner; public int _ownerIndex = -1, _liveIndex = -1;
        public double threatT, threatX, threatY, threatZ, losT, seenT, lastSeenX, lastSeenY, lastSeenZ;
        public bool losVisible;
        public bool? inWater;
        // render-only state
        public double? _renderX, _renderY, _renderZ, _renderAngle, _frameLight;
        public bool renderSwimming, renderRiding, renderSneaking;
        public double bodyPitch, bodyYOffset;
    }
    public sealed class MobOpts
    {
        public double angle = double.NaN, hp = double.NaN, growT = double.NaN, woolT = double.NaN, loveT = double.NaN, breedCd = double.NaN, eggT = double.NaN, angryT = double.NaN,
            fuse = double.NaN, fleeT = double.NaN, threatT = double.NaN, threatX = double.NaN, threatY = double.NaN, threatZ = double.NaN, tpT = double.NaN, burnT = double.NaN, dryT = double.NaN,
            hopT = double.NaN, leapT = double.NaN, age = double.NaN, seenT = double.NaN, lastSeenX = double.NaN, lastSeenY = double.NaN, lastSeenZ = double.NaN;
        public bool baby, sheared, persist, dynamicHostile, lastHitByPlayer;
        public string color, spawnId;
    }
    public sealed class Projectile
    {
        public double x, y, z, vx, vy, vz, age, damage = 3; public double? ox, oy, oz;
        public Mob src; public bool stuck, fromPlayer; public double[] dir; public int hx, hy, hz;
    }

    public static partial class VF
    {
        public static readonly List<Projectile> projectiles = new List<Projectile>();
        public static double hostileSpawnT = 0, passiveSpawnT = 0, fishSpawnT = 0, sharkSpawnT = 0, mobSimAccumulator = 0, mobRenderAlpha = 1;
        public const double MOB_FIXED_DT = 0.05, MOB_GRAVITY = 23, MOB_PATH_DESCENT = 3, MOB_PATH_REFRESH = 0.6;
        public const int MOB_MAX_STEPS = 3, MOB_PATH_MAX = 320, MOB_PATH_RADIUS = 22;
        static readonly int[][] MOB_DIRS = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 }, new[] { 1, 1 }, new[] { 1, -1 }, new[] { -1, 1 }, new[] { -1, -1 } };
        static readonly double MOB_DIAG = Math.Sqrt(2);
        const int MOB_PATH_D = MOB_PATH_RADIUS * 2 + 1, MOB_PATH_COL_CAP = MOB_PATH_D * MOB_PATH_D, MOB_PATH_NODE_CAP = MOB_PATH_COL_CAP + MOB_PATH_MAX * 8 + 16, MOB_PATH_HEAP_CAP = MOB_PATH_MAX * 9 + 16;
        static readonly uint[] mobPathColStamp = new uint[MOB_PATH_COL_CAP];
        static readonly int[] mobPathNodeNext = new int[MOB_PATH_NODE_CAP], mobPathParent = new int[MOB_PATH_NODE_CAP], mobPathHeap = new int[MOB_PATH_HEAP_CAP];
        static readonly ushort[] mobPathNodeCol = new ushort[MOB_PATH_NODE_CAP], mobPathYQ = new ushort[MOB_PATH_NODE_CAP];
        static readonly byte[] mobPathClosed = new byte[MOB_PATH_NODE_CAP];
        static readonly float[] mobPathG = new float[MOB_PATH_NODE_CAP], mobPathFeet = new float[MOB_PATH_NODE_CAP], mobPathHeapF = new float[MOB_PATH_HEAP_CAP];
        static readonly float[] MOB_NO_PATH = new float[0];
        static uint mobPathEpoch = 1;

        public static readonly List<Mob> liveMobs = new List<Mob>();
        public static readonly OrderedMap<string, List<JObj>> storedMobs = new OrderedMap<string, List<JObj>>();
        public static readonly OrderedSet<string> seededMobChunks = new OrderedSet<string>();

        /// <summary>Returns -1 when the containing chunk is not loaded (reference: null).</summary>
        static int mobLoadedBlockI(int x, int y, int z)
        {
            if (y < WORLD_MIN_Y) return B.BEDROCK;
            if (y >= WORLD_MAX_Y) return B.AIR;
            int cx = x >> 4, cz = z >> 4;
            var c = chunkFastGet(cx, cz);
            if (c == null) return -1;
            return chunkGet(c, x - cx * CHUNK, y, z - cz * CHUNK);
        }
        static int mobLoadedBlock(double x, double y, double z) { return mobLoadedBlockI(JS.floor(x), JS.floor(y), JS.floor(z)); }

        public static Mob makeMob(string type, double x, double y, double z, MobOpts opt = null)
        {
            MobInfo inf;
            if (!MOB_INFO.TryGetValue(type, out inf)) return null;
            opt = opt ?? new MobOpts();
            double angle = JS.isFinite(opt.angle) ? opt.angle : JS.random() * Math.PI * 2;
            var m = new Mob
            {
                type = type, x = x, y = y, z = z, ox = x, oy = y, oz = z, oangle = angle, angle = angle, targetAngle = angle, speed = inf.speed,
                turn = JS.random() * 6, phase = JS.random() * Math.PI * 2, walkPhase = 0, swimPhase = JS.random() * Math.PI * 2,
                hp = Math.Max(1, Math.Min(inf.hp, JS.isFinite(opt.hp) ? JS.toInt32(opt.hp) : inf.hp)),
                tgtX = x, tgtZ = z, detX = x, detZ = z, eggT = 300 + JS.random() * 300, fuse = -1, path = null, pathTy = y,
                escX = null, hopT = JS.random() * 0.8, sqY = 1, leapT = 1.5 + JS.random() * 1.5,
                persist = opt.persist, spawnId = opt.spawnId, dynamicHostile = opt.dynamicHostile,
                threatX = x, threatY = y, threatZ = z, lastSeenX = x, lastSeenY = y, lastSeenZ = z,
            };
            Func<double, double, double> num = (v, d) => JS.isFinite(v) ? v : d;
            if (opt.baby) { m.baby = true; m.growT = Math.Max(0, num(opt.growT, 90)); }
            m.loveT = Math.Max(0, num(opt.loveT, 0));
            m.breedCd = Math.Max(0, num(opt.breedCd, 0));
            m.eggT = Math.Max(0, num(opt.eggT, m.eggT));
            m.angryT = Math.Max(0, num(opt.angryT, 0));
            m.fuse = num(opt.fuse, -1);
            m.fleeT = Math.Max(0, num(opt.fleeT, 0));
            m.threatT = Math.Max(0, num(opt.threatT, 0));
            m.threatX = num(opt.threatX, x); m.threatY = num(opt.threatY, y); m.threatZ = num(opt.threatZ, z);
            m.tpT = Math.Max(0, num(opt.tpT, 0));
            m.burnT = Math.Max(0, num(opt.burnT, 0));
            m.dryT = Math.Max(0, num(opt.dryT, 0));
            m.hopT = Math.Max(0, num(opt.hopT, m.hopT));
            m.leapT = Math.Max(0, num(opt.leapT, m.leapT));
            m.age = Math.Max(0, num(opt.age, 0));
            m.seenT = Math.Max(0, num(opt.seenT, 0));
            m.lastSeenX = num(opt.lastSeenX, x); m.lastSeenY = num(opt.lastSeenY, y); m.lastSeenZ = num(opt.lastSeenZ, z);
            m.lastHitByPlayer = opt.lastHitByPlayer;
            if (type == "sheep")
            {
                m.color = opt.color != null && Array.IndexOf(SHEEP_COLORS, opt.color) >= 0 ? opt.color : randomSheepColorSource();
                if (opt.sheared) { m.sheared = true; m.woolT = Math.Max(0, num(opt.woolT, 90)); }
            }
            return m;
        }
        static readonly MobInfo DEFAULT_MOB_INFO = new MobInfo { w = 0.3, h = 1.7, speed = 1.3 };
        public static MobInfo mobInfo(Mob m) { MobInfo i; return m != null && m.type != null && MOB_INFO.TryGetValue(m.type, out i) ? i : DEFAULT_MOB_INFO; }
        public static double mobWidth(Mob m) { var inf = mobInfo(m); return (inf.w != 0 ? inf.w : 0.3) * (m != null && m.baby ? 0.5 : 1); }
        public static double mobCollisionWidth(Mob m) { var inf = mobInfo(m); double w = !double.IsNaN(inf.cw) ? inf.cw : inf.w; return w * (m != null && m.baby ? 0.5 : 1); }
        public static double mobHeight(Mob m) { var inf = mobInfo(m); return (inf.h != 0 ? inf.h : 1.7) * (m != null && m.baby ? 0.5 : 1); }

        // ---------------- persistence ----------------
        static double r100(double v) { return JS.round(v * 100) / 100; }
        public static JObj serializeMob(Mob m)
        {
            if (m == null || m.dead || !MOB_INFO.ContainsKey(m.type)) return null;
            return new JObj
            {
                { "t", m.type }, { "x", r100(m.x) }, { "y", r100(m.y) }, { "z", r100(m.z) },
                { "hp", JS.isFinite(m.hp) ? Math.Max(0.001, m.hp) : mobInfo(m).hp }, { "a", JS.isFinite(m.angle) ? m.angle : 0 },
                { "b", m.baby ? 1.0 : 0.0 }, { "gt", Math.Max(0, m.growT) }, { "sh", m.sheared ? 1.0 : 0.0 }, { "wt", Math.Max(0, m.woolT) },
                { "co", m.type == "sheep" ? (m.color ?? "white") : null }, { "p", m.persist ? 1.0 : 0.0 }, { "sid", m.spawnId },
                { "lt", Math.Max(0, m.loveT) }, { "bc", Math.Max(0, m.breedCd) }, { "et", Math.Max(0, m.eggT) }, { "an", Math.Max(0, m.angryT) },
                { "fu", JS.isFinite(m.fuse) ? m.fuse : -1 }, { "fl", Math.Max(0, m.fleeT) }, { "th", Math.Max(0, m.threatT) },
                { "tx", JS.isFinite(m.threatX) ? m.threatX : m.x }, { "ty", JS.isFinite(m.threatY) ? m.threatY : m.y }, { "tz", JS.isFinite(m.threatZ) ? m.threatZ : m.z },
                { "tp", Math.Max(0, m.tpT) }, { "bu", Math.Max(0, m.burnT) }, { "dr", Math.Max(0, m.dryT) }, { "ho", Math.Max(0, m.hopT) }, { "le", Math.Max(0, m.leapT) },
                { "ag", Math.Max(0, m.age) }, { "st", Math.Max(0, m.seenT) },
                { "lsx", JS.isFinite(m.lastSeenX) ? m.lastSeenX : m.x }, { "lsy", JS.isFinite(m.lastSeenY) ? m.lastSeenY : m.y }, { "lsz", JS.isFinite(m.lastSeenZ) ? m.lastSeenZ : m.z },
                { "lh", m.lastHitByPlayer ? 1.0 : 0.0 }, { "dh", m.dynamicHostile ? 1.0 : 0.0 },
            };
        }
        static double rn(JObj r, string k) { var v = r.Get(k); return v == null ? double.NaN : Json.ToNum(v); }
        public static Mob deserializeMob(JObj r)
        {
            if (r == null) return null;
            var t = r.Str("t");
            if (t == null || !MOB_INFO.ContainsKey(t)) return null;
            if (!finKey(r, "x") || !finKey(r, "y") || !finKey(r, "z") || !finKey(r, "hp")) return null;
            return makeMob(t, r.Num("x"), r.Num("y"), r.Num("z"), new MobOpts
            {
                hp = r.Num("hp"), angle = rn(r, "a"), baby = r.Bool("b"), growT = rn(r, "gt"), sheared = r.Bool("sh"), woolT = rn(r, "wt"), color = r.Str("co"),
                persist = r.Bool("p"), spawnId = r.Bool("sid") ? JS.ToStr(r.Get("sid")) : null, loveT = rn(r, "lt"), breedCd = rn(r, "bc"), eggT = rn(r, "et"), angryT = rn(r, "an"),
                fuse = JS.isFinite(rn(r, "fu")) ? rn(r, "fu") : -1, fleeT = rn(r, "fl"), threatT = rn(r, "th"), threatX = rn(r, "tx"), threatY = rn(r, "ty"), threatZ = rn(r, "tz"),
                tpT = rn(r, "tp"), burnT = rn(r, "bu"), dryT = rn(r, "dr"), hopT = rn(r, "ho"), leapT = rn(r, "le"), age = rn(r, "ag"), seenT = rn(r, "st"),
                lastSeenX = rn(r, "lsx"), lastSeenY = rn(r, "lsy"), lastSeenZ = rn(r, "lsz"), lastHitByPlayer = r.Bool("lh"), dynamicHostile = r.Bool("dh"),
            });
        }
        static void storeMobRecord(JObj r)
        {
            if (r == null) return;
            var k = ckey(JS.floor(r.Num("x") / CHUNK), JS.floor(r.Num("z") / CHUNK));
            var a = storedMobs.Get(k);
            if (a != null) a.Add(r); else storedMobs.Set(k, new List<JObj> { r });
        }
        static void storeMobForChunk(Mob m) { var r = serializeMob(m); if (r != null) storeMobRecord(r); }
        public static bool isLiveMob(Mob m) { int i = m != null ? m._liveIndex : -1; return i >= 0 && i < liveMobs.Count && liveMobs[i] == m && !m.dead; }
        static void detachMobChunk(Mob m)
        {
            var c = m != null ? m._owner : null;
            if (c == null || c.mobs == null) { if (m != null) { m._owner = null; m._ownerIndex = -1; } return; }
            int i = m._ownerIndex;
            if (i < 0 || i >= c.mobs.Count || c.mobs[i] != m) i = c.mobs.IndexOf(m);
            if (i >= 0)
            {
                var last = c.mobs[c.mobs.Count - 1]; c.mobs.RemoveAt(c.mobs.Count - 1);
                if (last != m) { c.mobs[i] = last; last._ownerIndex = i; }
            }
            m._owner = null; m._ownerIndex = -1;
        }
        static bool attachMobChunk(Mob m, Chunk c)
        {
            if (m == null || c == null) return false;
            if (m._owner == c && m._ownerIndex >= 0 && m._ownerIndex < c.mobs.Count && c.mobs[m._ownerIndex] == m) return true;
            detachMobChunk(m);
            m._owner = c; m._ownerIndex = c.mobs.Count; c.mobs.Add(m);
            return true;
        }
        public static bool attachLiveMob(Mob m, Chunk c)
        {
            if (m == null || m.dead || c == null) return false;
            if (!isLiveMob(m)) { m._liveIndex = liveMobs.Count; liveMobs.Add(m); }
            attachMobChunk(m, c);
            return true;
        }
        public static void removeLiveMob(Mob m, bool store = false)
        {
            if (m == null) return;
            if (store && !m.dead) storeMobForChunk(m);
            detachMobChunk(m);
            int i = m._liveIndex;
            if (i < 0 || i >= liveMobs.Count || liveMobs[i] != m) i = liveMobs.IndexOf(m);
            if (i >= 0)
            {
                var last = liveMobs[liveMobs.Count - 1]; liveMobs.RemoveAt(liveMobs.Count - 1);
                if (last != m) { liveMobs[i] = last; last._liveIndex = i; }
            }
            m._liveIndex = -1;
        }
        static void moveLiveMobChunk(Mob m, Chunk c) { if (m == null || c == null || m._owner == c) return; attachMobChunk(m, c); }
        public static void storeChunkMobs(Chunk c)
        {
            if (c == null || c.mobs == null) return;
            while (c.mobs.Count > 0) { var m = c.mobs[c.mobs.Count - 1]; removeLiveMob(m, !m.dead); }
        }
        public static List<Mob> restoreStoredMobs(int cx, int cz)
        {
            var k = ckey(cx, cz); var a = storedMobs.Get(k); var outp = new List<Mob>();
            if (a == null) return outp;
            storedMobs.Delete(k);
            foreach (var r in a) { var m = deserializeMob(r); if (m != null) outp.Add(m); }
            return outp;
        }
        public static List<object> serializeLiveMobs()
        {
            var a = new List<object>();
            foreach (var m in liveMobs) { var r = serializeMob(m); if (r != null) a.Add(r); }
            return a;
        }
        public static List<object> serializeStoredMobs()
        {
            var a = new List<object>();
            foreach (var kv in storedMobs) if (kv.Value != null && kv.Value.Count > 0) a.Add(new List<object> { kv.Key, kv.Value.Cast<object>().ToList() });
            return a;
        }
        public static void loadMobPersistence(List<object> live, object stored, List<object> seeded)
        {
            storedMobs.Clear(); seededMobChunks.Clear();
            var st = stored as List<object>;
            if (st != null)
                foreach (var rowO in st)
                {
                    var row = rowO as List<object>;
                    if (row == null || row.Count != 2 || !(row[1] is List<object>)) continue;
                    foreach (var r in (List<object>)row[1]) { var m = deserializeMob(r as JObj); if (m != null) storeMobRecord(serializeMob(m)); }
                }
            if (live != null)
                for (int i = 0; i < live.Count && i < 400; i++) { var m = deserializeMob(live[i] as JObj); if (m != null) storeMobRecord(serializeMob(m)); }
            if (seeded != null) foreach (var k in seeded) seededMobChunks.Add(JS.ToStr(k));
        }

        // ---------------- light / sight ----------------
        static bool mobDaylight() { return Math.Sin(day * Math.PI * 2) > 0.05; }
        static bool mobNight() { return Math.Sin(day * Math.PI * 2) < -0.17; }
        static double mobCombinedLightLevel(Mob m)
        {
            var l = getWorldLight(m.x, m.y + 0.5, m.z);
            return Math.Max(JS.round(l[1] * 15), mobNight() ? 0 : JS.round(l[0] * 15));
        }
        static bool mobCanAggroSource(Mob m)
        {
            var inf = mobInfo(m);
            if (m.angryT > 0) return true;
            if (inf.neutralLight != 0) return mobCombinedLightLevel(m) < inf.neutralLight;
            return !inf.neutral;
        }
        static bool mobSightTransparentId(int id)
        {
            if (id < 0) return false;
            if (id == B.AIR || isWater(id) || isLava(id)) return true;
            var b = bdef(id);
            return b == null || b.plant || b.waterPlant || id == B.GLASS || id == B.GLASS_PANE;
        }
        static bool mobSightBlocked(double ax, double ay, double az, double bx, double by, double bz)
        {
            double dx = bx - ax, dy = by - ay, dz = bz - az, d = JS.hypot(dx, dy, dz);
            if (d < 1e-5) return false;
            double rx = dx / d, ry = dy / d, rz = dz / d;
            if (rx == 0) rx = 1e-12; if (ry == 0) ry = 1e-12; if (rz == 0) rz = 1e-12;
            double ix = 1 / rx, iy = 1 / ry, iz = 1 / rz;
            int x = JS.floor(ax), y = JS.floor(ay), z = JS.floor(az), sx = rx > 0 ? 1 : -1, sy = ry > 0 ? 1 : -1, sz = rz > 0 ? 1 : -1;
            double tx = Math.Abs(ix), ty = Math.Abs(iy), tz = Math.Abs(iz), nx = (sx > 0 ? x + 1 - ax : ax - x) * tx, ny = (sy > 0 ? y + 1 - ay : ay - y) * ty, nz = (sz > 0 ? z + 1 - az : az - z) * tz, t = 0;
            for (int step = 0; step < 192 && t <= d; step++)
            {
                int id = mobLoadedBlockI(x, y, z);
                if (!mobSightTransparentId(id))
                    foreach (var q in mobSourceCollisionBoxes(id, x, y, z))
                    {
                        double hit = rayBoxDistanceScalar(ax, ay, az, rx, ry, rz, x + q[0], y + q[1], z + q[2], x + q[3], y + q[4], z + q[5], d);
                        if (!double.IsNaN(hit) && hit > 1e-4 && hit < d - 1e-4) return true;
                    }
                if (nx < ny && nx < nz) { x += sx; t = nx; nx += tx; }
                else if (ny < nz) { y += sy; t = ny; ny += ty; }
                else { z += sz; t = nz; nz += tz; }
            }
            return false;
        }
        static bool mobCanSeePlayerCached(Mob m)
        {
            if (m.losT <= 0)
            {
                double sy = m.y + Math.Min(Math.Max(0.35, mobHeight(m) * 0.82), 2.35), ty = player.y + 1.5;
                m.losVisible = !mobSightBlocked(m.x, sy, m.z, player.x, ty, player.z);
                m.losT = 0.18;
            }
            return m.losVisible;
        }
        static bool playerStaresAtMob(Mob m, double dist)
        {
            if (armor[0] != null) { var ai = idef(armor[0].key).armor; if (ai != null && ai.vision == "pumpkin") return false; }
            if (dist > 64 || dist < 0.5) return false;
            double ex = player.x, ey = player.y + 1.62, ez = player.z, cp = Math.Cos(player.pitch), vx = Math.Sin(player.yaw) * cp, vy = -Math.Sin(player.pitch), vz = -Math.Cos(player.yaw) * cp;
            double tx = m.x, ty = m.y + mobHeight(m) * 0.9, tz = m.z, dx = tx - ex, dy = ty - ey, dz = tz - ez, d = JS.hypot(dx, dy, dz);
            if (d == 0) d = 1;
            double dot = (dx * vx + dy * vy + dz * vz) / d, r = mobWidth(m) * 1.2 / d, threshold = Math.Min(0.999, 1 - r * r * 0.5);
            return dot > threshold && !mobSightBlocked(ex, ey, ez, tx, ty, tz);
        }
        static bool mobTeleportSpot(Mob m, double x, double y, double z)
        {
            if (y < WORLD_MIN_Y + 2 || y > WORLD_MAX_Y - 2 || !mobPointBlocked(x, y - 0.2, z) || !mobSpaceClear(x, y, z, m)) return false;
            int id = mobLoadedBlock(x, y + 0.5, z);
            return id >= 0 && !isWater(id) && !isLava(id);
        }
        static bool teleportMobSource(Mob m, double cx, double cz, double radius)
        {
            for (int n = 0; n < 32; n++)
            {
                double rx = cx + (JS.random() - 0.5) * 2 * radius, rz = cz + (JS.random() - 0.5) * 2 * radius, x = Math.Floor(rx) + 0.5, z = Math.Floor(rz) + 0.5;
                for (int oy = 8; oy >= -16; oy--)
                {
                    double y = Math.Floor(m.y) + oy;
                    if (mobTeleportSpot(m, x, y, z))
                    {
                        m.x = x; m.y = y; m.z = z; m.vx = m.vy = m.vz = 0; m.path = null; m.losT = 0; m.losVisible = false;
                        return true;
                    }
                }
            }
            return false;
        }
        static bool slimeChunkSource(int cx, int cz)
        {
            int n = JS.imul(cx, 522133279) ^ JS.imul(cz, 668265261) ^ WORLD_SEED;
            n = JS.imul(n ^ (int)((uint)n >> 15), unchecked((int)2246822507u));
            n ^= (int)((uint)n >> 13);
            return (uint)n % 10 == 0;
        }

        // ---------------- damage ----------------
        public static DamageResult damageMobSource(Mob m, double damage, double[] hitPos = null, bool fromPlayer = false, bool silent = false, Entity threat = null)
        {
            if (m == null || m.dead) return new DamageResult { dead = false, loot = "" };
            var inf = mobInfo(m);
            m.hp = m.hp - Math.Max(0, double.IsNaN(damage) ? 0 : damage);
            m.hurtT = 0.4;
            m.lastHitByPlayer = fromPlayer;
            if (hitPos != null)
            {
                double dx = m.x - hitPos[0], dz = m.z - hitPos[2], d = JS.hypot(dx, dz);
                if (d == 0) d = 1;
                m.vx += dx / d * 6; m.vz += dz / d * 6; m.vy = 4.5; m.kbT = 0.3;
            }
            if (fromPlayer)
            {
                sfxHurt();
                threat = player;
                m.lastSeenX = player.x; m.lastSeenY = player.y; m.lastSeenZ = player.z;
                m.seenT = Math.Max(m.seenT, 3); m.losT = 0;
            }
            if (threat != null && JS.isFinite(threat.x) && JS.isFinite(threat.z))
            {
                m.threatX = threat.x; m.threatY = JS.isFinite(threat.y) ? threat.y : m.y; m.threatZ = threat.z; m.threatT = 4;
            }
            if (!inf.hostile) m.fleeT = 4;
            if (inf.aquatic) { m.escX = null; m.escT = 0; }
            if (m.hp <= 0) return new DamageResult { dead = true, loot = finishMobDeath(m, silent) };
            if (fromPlayer && (inf.neutral || inf.neutralLight != 0)) m.angryT = 30;
            if (inf.teleports && JS.random() < 0.5 && teleportMobSource(m, m.x, m.z, 24)) m.tpT = 1;
            return new DamageResult { dead = false, loot = "" };
        }
        public static string finishMobDeath(Mob m, bool silent = false)
        {
            if (m == null || m.dead) return "";
            var inf = mobInfo(m);
            m.dead = true;
            if (inf.splits != null && MOB_INFO.ContainsKey(inf.splits))
            {
                int count = 2 + (int)Math.Floor(JS.random() * 3);
                for (int i = 0; i < count; i++)
                {
                    double a = i * Math.PI * 2 / count + JS.random() * 0.5;
                    var child = makeMob(inf.splits, m.x + Math.Cos(a) * mobWidth(m) * 0.8, m.y + 0.1, m.z + Math.Sin(a) * mobWidth(m) * 0.8, new MobOpts { dynamicHostile = true });
                    if (child != null) { child.vx = Math.Cos(a) * 2.5; child.vz = Math.Sin(a) * 2.5; child.vy = 3; child.angryT = m.angryT; queueMobAdd(child); }
                }
            }
            string loot = "";
            if (m.lastHitByPlayer && m.spawnId != null) { killedMobs.Add(m.spawnId); saveKilledMobs(); }
            if (!player.creative && m.lastHitByPlayer) loot = mobDrops(m);
            return loot;
        }

        // ---------------- collision ----------------
        static bool mobFullCubeId(int id)
        {
            var b = bdef(id);
            return b != null && b.solid && !isWater(id) && !isLava(id) && id != B.BED && (b.special == null || b.special == "orientedCube");
        }
        static List<double[]> mobSourceCollisionBoxes(int id, int x, int y, int z)
        {
            if (id < 0 || id == B.AIR || isWater(id) || id == B.LAVA) return COLLISION_EMPTY_BOXES;
            if (id == B.BED) return COLLISION_BED_BOXES;
            return collisionBoxesAt(id, x, y, z);
        }
        static bool mobPointBlocked(double x, double y, double z)
        {
            int bx = JS.floor(x), by = JS.floor(y), bz = JS.floor(z), id = mobLoadedBlockI(bx, by, bz);
            if (id < 0) return true;
            if (mobFullCubeId(id)) return true;
            double lx = x - bx, ly = y - by, lz = z - bz;
            foreach (var b in mobSourceCollisionBoxes(id, bx, by, bz))
                if (lx >= b[0] && lx <= b[3] && ly >= b[1] && ly <= b[4] && lz >= b[2] && lz <= b[5]) return true;
            return false;
        }
        static bool mobCollides(double x, double feet, double z, Mob m)
        {
            double r = mobCollisionWidth(m), h = mobHeight(m), x0 = x - r, x1 = x + r, z0 = z - r, z1 = z + r, top = feet + h;
            int minX = JS.floor(x0), maxX = JS.floor(x1 - 0.000001), minY = JS.floor(feet), maxY = JS.floor(top - 0.000001), minZ = JS.floor(z0), maxZ = JS.floor(z1 - 0.000001);
            for (int xx = minX; xx <= maxX; xx++)
                for (int yy = minY; yy <= maxY; yy++)
                    for (int zz = minZ; zz <= maxZ; zz++)
                    {
                        int id = mobLoadedBlockI(xx, yy, zz);
                        if (id < 0) return true;
                        if (id == B.AIR || isWater(id) || isLava(id)) continue;
                        if (mobFullCubeId(id)) return true;
                        foreach (var b in mobSourceCollisionBoxes(id, xx, yy, zz))
                            if (x1 > xx + b[0] && x0 < xx + b[3] && top > yy + b[1] && feet < yy + b[4] && z1 > zz + b[2] && z0 < zz + b[5]) return true;
                    }
            return false;
        }
        static bool mobSpaceClear(double x, double feet, double z, Mob m) { return !mobCollides(x, feet, z, m); }
        static bool mobWaterLike(int id) { if (id < 0) return false; var b = bdef(id); return isWater(id) || (b != null && b.waterPlant); }
        static bool mobInWater(Mob m) { return mobWaterLike(mobLoadedBlock(m.x, m.y + 0.05, m.z)); }
        static bool mobInLiquid(Mob m) { int id = mobLoadedBlock(m.x, m.y + 0.3, m.z); return id >= 0 && (mobWaterLike(id) || isLava(id)); }
        /// <summary>Returns NaN when unloaded/no support (reference: null).</summary>
        static double mobGroundHeightNear(double x, double z, double baseY, Mob m, double maxUp = 1.05, double maxDown = 3.05)
        {
            double r = mobCollisionWidth(m), x0 = x - r, x1 = x + r, z0 = z - r, z1 = z + r, hi = baseY + maxUp, lo = baseY - maxDown;
            int minX = JS.floor(x0), maxX = JS.floor(x1 - 0.000001), minZ = JS.floor(z0), maxZ = JS.floor(z1 - 0.000001);
            double best = double.NegativeInfinity;
            for (int xx = minX; xx <= maxX; xx++)
                for (int zz = minZ; zz <= maxZ; zz++)
                    for (int yy = JS.floor(hi); yy >= JS.floor(lo) - 1; yy--)
                    {
                        int id = mobLoadedBlockI(xx, yy, zz);
                        if (id < 0) return double.NaN;
                        if (id == B.AIR || isWater(id) || isLava(id)) continue;
                        if (mobFullCubeId(id))
                        {
                            double top = yy + 1;
                            if (top <= hi + 0.001 && top >= lo - 0.001 && top > best && mobSpaceClear(x, top, z, m)) best = top;
                            continue;
                        }
                        foreach (var b in mobSourceCollisionBoxes(id, xx, yy, zz))
                        {
                            if (!(x1 > xx + b[0] && x0 < xx + b[3] && z1 > zz + b[2] && z0 < zz + b[5])) continue;
                            double top = yy + b[4];
                            if (top <= hi + 0.001 && top >= lo - 0.001 && top > best && mobSpaceClear(x, top, z, m)) best = top;
                        }
                    }
            return best > double.NegativeInfinity ? best : double.NaN;
        }
        static double findMobGround(double x, double z, double fromY, Mob m)
        {
            double y = mobGroundHeightNear(x, z, fromY, m, 1.05, MOB_PATH_DESCENT + 0.05);
            return !double.IsNaN(y) && mobSpaceClear(x, y, z, m) ? y : double.NaN;
        }
        static readonly double[] RECOVER_RADII = { 0.3, 0.6, 0.9, 1.3, 1.8, 2.4, 3.2 };
        static bool mobRecoverOverlap(Mob m, double dt)
        {
            if (mobSpaceClear(m.x, m.y, m.z, m)) return false;
            foreach (var r in RECOVER_RADII)
            {
                double dx = 0, dy = 0, dz = 0; bool ok = false;
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4, nx = m.x + Math.Cos(a) * r, nz = m.z + Math.Sin(a) * r;
                    if (mobSpaceClear(nx, m.y, nz, m)) { dx = nx - m.x; dz = nz - m.z; ok = true; break; }
                }
                if (!ok && mobSpaceClear(m.x, m.y + r, m.z, m)) { dy = r; ok = true; }
                if (!ok && mobSpaceClear(m.x, m.y - r, m.z, m)) { dy = -r; ok = true; }
                if (ok)
                {
                    double l = JS.hypot(dx, dy, dz); if (l == 0) l = 1;
                    double step = Math.Min(l, 8 * dt);
                    m.x += dx / l * step; m.y += dy / l * step; m.z += dz / l * step;
                    m.vx = m.vy = m.vz = 0; m.path = null;
                    return true;
                }
            }
            return false;
        }
        static bool mobWishToward(Mob m, double x, double z, double speed)
        {
            double dx = x - m.x, dz = z - m.z, d = JS.hypot(dx, dz);
            if (d < 0.4) { m.wishX = 0; m.wishZ = 0; return true; }
            m.wishX = dx / d * speed; m.wishZ = dz / d * speed;
            return false;
        }
        static void mobStop(Mob m) { m.wishX = 0; m.wishZ = 0; }

        // ---------------- A* pathing ----------------
        static bool mobPathHazardAt(Mob m, int x, double y, int z)
        {
            var inf = mobInfo(m);
            double yy0 = y + 0.15, yy1 = y + Math.Min(1, mobHeight(m) * 0.55);
            int id0 = mobLoadedBlock(x + 0.5, yy0, z + 0.5);
            if (id0 < 0 || isLava(id0) || id0 == B.FIRE || (inf.hatesWater && mobWaterLike(id0))) return true;
            int id1 = mobLoadedBlock(x + 0.5, yy1, z + 0.5);
            return id1 < 0 || isLava(id1) || id1 == B.FIRE || (inf.hatesWater && mobWaterLike(id1));
        }
        static bool mobPathCellClear(Mob m, int x, double y, int z) { return mobSpaceClear(x + 0.5, y, z + 0.5, m) && !mobPathHazardAt(m, x, y, z); }
        static double mobPathFloorY(Mob m, int x, double baseY, int z)
        {
            double y = findMobGround(x + 0.5, z + 0.5, baseY, m);
            return !double.IsNaN(y) && mobPathCellClear(m, x, y, z) ? y : double.NaN;
        }
        static double mobHeuristic(double dx, double dz, double dy = 0)
        {
            dx = Math.Abs(dx); dz = Math.Abs(dz);
            return (dx > dz ? dx - dz : dz - dx) + MOB_DIAG * Math.Min(dx, dz) + Math.Abs(dy) * 0.55;
        }
        static int mobPathHeapPush(int idx, double fd, int size)
        {
            if (size >= MOB_PATH_HEAP_CAP) return size;
            float f = (float)fd;
            int i = size;
            mobPathHeap[i] = idx; mobPathHeapF[i] = f;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (mobPathHeapF[p] <= fd) break;
                mobPathHeap[i] = mobPathHeap[p]; mobPathHeapF[i] = mobPathHeapF[p];
                i = p;
                mobPathHeap[i] = idx; mobPathHeapF[i] = f;
            }
            return size + 1;
        }
        static int mobPathPoppedIdx = -1;
        static int mobPathHeapPop(int size)
        {
            mobPathPoppedIdx = mobPathHeap[0];
            int lastI = mobPathHeap[size - 1]; float lastF = mobPathHeapF[size - 1];
            size--;
            if (size > 0)
            {
                int i = 0;
                while (true)
                {
                    int a = i * 2 + 1;
                    if (a >= size) break;
                    int b = a + 1, k = b < size && mobPathHeapF[b] < mobPathHeapF[a] ? b : a;
                    if (mobPathHeapF[k] >= lastF) break;
                    mobPathHeap[i] = mobPathHeap[k]; mobPathHeapF[i] = mobPathHeapF[k];
                    i = k;
                }
                mobPathHeap[i] = lastI; mobPathHeapF[i] = lastF;
            }
            return size;
        }
        static int mobPathQuantY(double y) { double q = JS.round((y - WORLD_MIN_Y) * 16); return q >= 0 && q <= WORLD_H * 16 ? (int)q : -1; }
        static int mobPathColumnIndex(int lx, int lz) { return lz * MOB_PATH_D + lx; }
        static int mobPathNodeColumn(int n) { return n < MOB_PATH_COL_CAP ? n : mobPathNodeCol[n]; }
        static int mobPathFindNode(int col, int yq, uint epoch)
        {
            if (mobPathColStamp[col] != epoch) return -1;
            for (int n = col; n >= 0; n = mobPathNodeNext[n]) if (mobPathYQ[n] == yq) return n;
            return -1;
        }
        static int mobPathInitNode(int n, int col, double y, int yq, int parent, double g, int next)
        {
            mobPathNodeCol[n] = (ushort)col; mobPathYQ[n] = (ushort)yq; mobPathClosed[n] = 0; mobPathFeet[n] = (float)y; mobPathParent[n] = parent; mobPathG[n] = (float)g; mobPathNodeNext[n] = next;
            return n;
        }
        static uint nextPathEpoch()
        {
            mobPathEpoch++;
            if (mobPathEpoch == 0) { Array.Clear(mobPathColStamp, 0, mobPathColStamp.Length); mobPathEpoch = 1; }
            return mobPathEpoch;
        }
        static float[] mobBuildPathFast2D(Mob m, double targetX, double targetZ, double targetY)
        {
            int sx = JS.floor(m.x), sy = JS.floor(m.y + 0.1), sz = JS.floor(m.z), gx = JS.floor(targetX), gz = JS.floor(targetZ);
            bool hasGY = JS.isFinite(targetY); double gy = hasGY ? targetY : sy;
            int x0 = sx - MOB_PATH_RADIUS, z0 = sz - MOB_PATH_RADIUS;
            uint epoch = nextPathEpoch();
            int start = MOB_PATH_RADIUS * MOB_PATH_D + MOB_PATH_RADIUS;
            mobPathColStamp[start] = epoch; mobPathClosed[start] = 0; mobPathG[start] = 0; mobPathParent[start] = -1; mobPathFeet[start] = sy;
            int heapN = 0; double startH = mobHeuristic(sx - gx, sz - gz, hasGY ? sy - gy : 0);
            heapN = mobPathHeapPush(start, startH, heapN);
            int best = start, expanded = 0; double bestH = startH;
            double goalTolY = Math.Max(0.65, Math.Min(1.25, mobHeight(m) * 0.55));
            while (heapN > 0 && expanded < MOB_PATH_MAX)
            {
                heapN = mobPathHeapPop(heapN);
                int idx = mobPathPoppedIdx;
                if (mobPathClosed[idx] != 0) continue;
                mobPathClosed[idx] = 1;
                expanded++;
                int lz = idx / MOB_PATH_D, lx = idx - lz * MOB_PATH_D, x = x0 + lx, z = z0 + lz;
                double y = mobPathFeet[idx];
                if (x == gx && z == gz && (!hasGY || Math.Abs(y - gy) <= goalTolY)) { best = idx; break; }
                foreach (var d in MOB_DIRS)
                {
                    int ox = d[0], oz = d[1], nlx = lx + ox, nlz = lz + oz;
                    if (nlx < 0 || nlx >= MOB_PATH_D || nlz < 0 || nlz >= MOB_PATH_D) continue;
                    int ni = nlz * MOB_PATH_D + nlx; bool diag = ox != 0 && oz != 0;
                    double baseNg = mobPathG[idx] + (diag ? MOB_DIAG : 1);
                    if (mobPathColStamp[ni] == epoch && (mobPathClosed[ni] != 0 || baseNg >= mobPathG[ni])) continue;
                    int nx = x + ox, nz = z + oz;
                    double ny = mobPathFloorY(m, nx, y, nz);
                    if (double.IsNaN(ny)) continue;
                    if (diag && (Math.Abs(ny - y) > 0.001 || !mobPathCellClear(m, x + ox, y, z) || !mobPathCellClear(m, x, y, z + oz))) continue;
                    double ng = baseNg + Math.Abs(ny - y) * 0.35;
                    if (mobPathColStamp[ni] == epoch && ng >= mobPathG[ni]) continue;
                    if (mobPathColStamp[ni] != epoch) { mobPathColStamp[ni] = epoch; mobPathClosed[ni] = 0; mobPathParent[ni] = -1; }
                    mobPathFeet[ni] = (float)ny; mobPathG[ni] = (float)ng; mobPathParent[ni] = idx;
                    double h = mobHeuristic(nx - gx, nz - gz, hasGY ? ny - gy : 0), f = ng + h;
                    if (h < bestH) { bestH = h; best = ni; }
                    heapN = mobPathHeapPush(ni, f, heapN);
                }
            }
            if (best == start) return MOB_NO_PATH;
            int count = 0;
            for (int n = best; n != start && n >= 0; n = mobPathParent[n]) count++;
            if (count == 0) return MOB_NO_PATH;
            var path = new float[count * 3]; int o = (count - 1) * 3;
            for (int n = best; n != start && n >= 0; n = mobPathParent[n], o -= 3)
            {
                int lz = n / MOB_PATH_D, lx = n - lz * MOB_PATH_D;
                path[o] = (float)(x0 + lx + 0.5); path[o + 1] = mobPathFeet[n]; path[o + 2] = (float)(z0 + lz + 0.5);
            }
            return path;
        }
        static float[] mobBuildPath3D(Mob m, double targetX, double targetZ, double targetY)
        {
            int sx = JS.floor(m.x), sz = JS.floor(m.z), gx = JS.floor(targetX), gz = JS.floor(targetZ);
            double sy = m.y; bool hasGY = JS.isFinite(targetY); double gy = hasGY ? targetY : sy;
            int x0 = sx - MOB_PATH_RADIUS, z0 = sz - MOB_PATH_RADIUS;
            uint epoch = nextPathEpoch();
            int startCol = mobPathColumnIndex(MOB_PATH_RADIUS, MOB_PATH_RADIUS), startYQ = mobPathQuantY(sy);
            if (startYQ < 0) return MOB_NO_PATH;
            mobPathColStamp[startCol] = epoch;
            mobPathInitNode(startCol, startCol, sy, startYQ, -1, 0, -1);
            int overflowN = MOB_PATH_COL_CAP, heapN = 0;
            double startH = mobHeuristic(sx - gx, sz - gz, hasGY ? sy - gy : 0);
            heapN = mobPathHeapPush(startCol, startH, heapN);
            int best = startCol, expanded = 0; double bestH = startH;
            double goalTolY = Math.Max(0.65, Math.Min(1.25, mobHeight(m) * 0.55));
            while (heapN > 0 && expanded < MOB_PATH_MAX)
            {
                heapN = mobPathHeapPop(heapN);
                int idx = mobPathPoppedIdx;
                if (mobPathClosed[idx] != 0) continue;
                mobPathClosed[idx] = 1;
                expanded++;
                int col = mobPathNodeColumn(idx), lz = col / MOB_PATH_D, lx = col - lz * MOB_PATH_D, x = x0 + lx, z = z0 + lz;
                double y = mobPathFeet[idx];
                if (x == gx && z == gz && (!hasGY || Math.Abs(y - gy) <= goalTolY)) { best = idx; break; }
                foreach (var d in MOB_DIRS)
                {
                    int ox = d[0], oz = d[1], nlx = lx + ox, nlz = lz + oz;
                    if (nlx < 0 || nlx >= MOB_PATH_D || nlz < 0 || nlz >= MOB_PATH_D) continue;
                    int nx = x + ox, nz = z + oz;
                    double ny = mobPathFloorY(m, nx, y, nz);
                    if (double.IsNaN(ny)) continue;
                    bool diag = ox != 0 && oz != 0;
                    if (diag && (Math.Abs(ny - y) > 0.001 || !mobPathCellClear(m, x + ox, y, z) || !mobPathCellClear(m, x, y, z + oz))) continue;
                    int yq = mobPathQuantY(ny);
                    if (yq < 0) continue;
                    int nc = mobPathColumnIndex(nlx, nlz);
                    double ng = mobPathG[idx] + (diag ? MOB_DIAG : 1) + Math.Abs(ny - y) * 0.35;
                    int ni = mobPathFindNode(nc, yq, epoch);
                    if (ni >= 0) { if (mobPathClosed[ni] != 0 || ng >= mobPathG[ni]) continue; }
                    else if (mobPathColStamp[nc] != epoch) { mobPathColStamp[nc] = epoch; ni = mobPathInitNode(nc, nc, ny, yq, idx, ng, -1); }
                    else
                    {
                        if (overflowN >= MOB_PATH_NODE_CAP) continue;
                        ni = overflowN++;
                        ni = mobPathInitNode(ni, nc, ny, yq, idx, ng, mobPathNodeNext[nc]);
                        mobPathNodeNext[nc] = ni;
                    }
                    mobPathG[ni] = (float)ng; mobPathParent[ni] = idx;
                    double h = mobHeuristic(nx - gx, nz - gz, hasGY ? ny - gy : 0), f = ng + h;
                    if (h < bestH) { bestH = h; best = ni; }
                    heapN = mobPathHeapPush(ni, f, heapN);
                }
            }
            if (best == startCol) return MOB_NO_PATH;
            int count = 0;
            for (int n = best; n != startCol && n >= 0; n = mobPathParent[n]) count++;
            if (count == 0) return MOB_NO_PATH;
            var path = new float[count * 3]; int o = (count - 1) * 3;
            for (int n = best; n != startCol && n >= 0; n = mobPathParent[n], o -= 3)
            {
                int c = mobPathNodeColumn(n), lz = c / MOB_PATH_D, lx = c - lz * MOB_PATH_D;
                path[o] = (float)(x0 + lx + 0.5); path[o + 1] = mobPathFeet[n]; path[o + 2] = (float)(z0 + lz + 0.5);
            }
            return path;
        }
        static float[] mobBuildPath(Mob m, double targetX, double targetZ, double targetY)
        {
            bool hasY = JS.isFinite(targetY); double goalTol = Math.Max(0.65, Math.Min(1.25, mobHeight(m) * 0.55));
            if (hasY && Math.Abs(targetY - m.y) > goalTol) return mobBuildPath3D(m, targetX, targetZ, targetY);
            var fast = mobBuildPathFast2D(m, targetX, targetZ, targetY);
            return fast.Length > 0 ? fast : mobBuildPath3D(m, targetX, targetZ, targetY);
        }
        static bool mobFollowPath(Mob m, double targetX, double targetZ, double speed, double dt, double targetY = double.NaN)
        {
            m.pathT -= dt;
            bool hasTy = JS.isFinite(targetY);
            double targetMoved = Math.Abs(targetX - m.pathTx) + Math.Abs(targetZ - m.pathTz) + (!hasTy || !JS.isFinite(m.pathTy) ? 0 : Math.Abs(targetY - m.pathTy) * 0.5);
            if (m.path == null || m.pathT <= 0 || targetMoved > 2)
            {
                m.pathT = MOB_PATH_REFRESH; m.pathTx = targetX; m.pathTy = hasTy ? targetY : m.y; m.pathTz = targetZ;
                m.path = mobBuildPath(m, targetX, targetZ, hasTy ? targetY : double.NaN);
                m.pathI = 0;
            }
            var p = m.path;
            if (p == null) { mobStop(m); return false; }
            if (p.Length == 0)
            {
                bool done0 = JS.hypot(targetX - m.x, targetZ - m.z) < 0.45 && (!hasTy || Math.Abs(targetY - m.y) < 0.75);
                mobStop(m);
                return done0;
            }
            int i = m.pathI;
            if (i >= p.Length)
            {
                bool done1 = JS.hypot(targetX - m.x, targetZ - m.z) < 0.7 && (!hasTy || Math.Abs(targetY - m.y) < 1.2);
                if (done1) { mobStop(m); return true; }
                m.pathT = 0; mobStop(m); return false;
            }
            double qx = p[i], qy = p[i + 1], qz = p[i + 2];
            if (JS.hypot(qx - m.x, qz - m.z) < 0.55 && Math.Abs(qy - m.y) < 0.72)
            {
                m.pathI += 3; i = m.pathI;
                if (i >= p.Length)
                {
                    bool done2 = JS.hypot(targetX - m.x, targetZ - m.z) < 0.8 && (!hasTy || Math.Abs(targetY - m.y) < 1.3);
                    if (done2) { mobStop(m); return true; }
                    m.pathT = 0; mobStop(m); return false;
                }
                qx = p[i]; qy = p[i + 1]; qz = p[i + 2];
            }
            mobWishToward(m, qx, qz, speed);
            return false;
        }
        static bool mobCliffAhead(Mob m)
        {
            double len = JS.hypot(m.wishX, m.wishZ);
            if (len < 0.2) return false;
            double x = m.x + m.wishX / len * 0.8, z = m.z + m.wishZ / len * 0.8;
            return double.IsNaN(mobGroundHeightNear(x, z, m.y, m, 1.05, MOB_PATH_DESCENT + 0.05));
        }
        static bool mobLiquidAhead(Mob m)
        {
            double len = JS.hypot(m.wishX, m.wishZ);
            if (len < 0.2) return false;
            int x = JS.floor(m.x + m.wishX / len * 0.9), z = JS.floor(m.z + m.wishZ / len * 0.9), y = JS.floor(m.y + 0.3), id = mobLoadedBlockI(x, y, z);
            if (mobWaterLike(id)) return true;
            if (id < 0 || id == B.AIR) return mobWaterLike(mobLoadedBlockI(x, y - 1, z)) || mobWaterLike(mobLoadedBlockI(x, y - 2, z));
            return false;
        }
        static bool mobDangerAhead(Mob m)
        {
            double len = JS.hypot(m.wishX, m.wishZ);
            if (len < 0.2) return false;
            double x = m.x + m.wishX / len * 0.85, z = m.z + m.wishZ / len * 0.85;
            int id0 = mobLoadedBlock(x, m.y + 0.15, z);
            if (id0 < 0 || isLava(id0) || id0 == B.FIRE) return true;
            int id1 = mobLoadedBlock(x, m.y - 0.15, z);
            return id1 < 0 || isLava(id1) || id1 == B.FIRE;
        }
        static bool mobTryPartialStep(Mob m, double nx, double nz)
        {
            double y = mobGroundHeightNear(nx, nz, m.y, m, 0.62, 0.12);
            if (double.IsNaN(y) || y < m.y - 0.08 || y - m.y > 0.61 || !mobSpaceClear(nx, y, nz, m)) return false;
            m.x = nx; m.z = nz; m.y = y; m.vy = 0; m.onGround = true;
            return true;
        }
        static void turnToward(Mob m, double rate)
        {
            if (m.targetAngle == null) return;
            double d = m.targetAngle.Value - m.angle;
            while (d > Math.PI) d -= Math.PI * 2;
            while (d < -Math.PI) d += Math.PI * 2;
            m.angle += Math.Max(-rate, Math.Min(rate, d));
        }
        static bool mobPhysicsLand(Mob m, double dt)
        {
            if (m.jumpT > 0) m.jumpT -= dt;
            if (mobRecoverOverlap(m, dt)) return false;
            bool wet = mobWaterLike(mobLoadedBlock(m.x, m.y + 0.3, m.z));
            if (wet) m.vy += (2.5 - m.vy) * Math.Min(1, dt * 3);
            else { m.vy -= MOB_GRAVITY * dt; if (m.vy < -40) m.vy = -40; }
            if (m.kbT > 0) m.kbT -= dt;
            double accel = m.kbT > 0 ? 1.5 : m.onGround || wet ? 10 : 2.5;
            m.vx += (m.wishX - m.vx) * Math.Min(1, dt * accel);
            m.vz += (m.wishZ - m.vz) * Math.Min(1, dt * accel);
            if (JS.hypot(m.wishX, m.wishZ) > 0.2) m.targetAngle = Math.Atan2(m.wishX, -m.wishZ);
            turnToward(m, dt * 8);
            double dy = m.vy * dt; int vs = Math.Max(1, (int)Math.Ceiling(Math.Abs(dy) / 0.4)); double vstep = dy / vs;
            bool landed = false;
            for (int k = 0; k < vs; k++)
            {
                double ny = m.y + vstep;
                if (mobSpaceClear(m.x, ny, m.z, m)) { m.y = ny; m.onGround = false; continue; }
                if (vstep <= 0)
                {
                    double gy = mobGroundHeightNear(m.x, m.z, ny, m, 0.8, 1.6);
                    if (!double.IsNaN(gy)) { m.y = gy; m.vy = 0; m.onGround = true; landed = true; }
                    else m.vy = 0;
                }
                else m.vy = 0;
                break;
            }
            if (!landed && dy == 0) m.onGround = !double.IsNaN(mobGroundHeightNear(m.x, m.z, m.y, m, 0.05, 0.12));
            bool blocked = false;
            double nx = m.x + m.vx * dt;
            if (mobSpaceClear(nx, m.y, m.z, m)) m.x = nx;
            else if (m.onGround && mobTryPartialStep(m, nx, m.z)) { }
            else if (m.onGround && m.jumpT <= 0 && mobSpaceClear(nx, m.y + 1, m.z, m)) { m.vy = 8.2; m.jumpT = 0.5; }
            else blocked = true;
            double nz = m.z + m.vz * dt;
            if (mobSpaceClear(m.x, m.y, nz, m)) m.z = nz;
            else if (m.onGround && mobTryPartialStep(m, m.x, nz)) { }
            else if (m.onGround && m.vy <= 0 && m.jumpT <= 0 && mobSpaceClear(m.x, m.y + 1, nz, m)) { m.vy = 8.2; m.jumpT = 0.5; }
            else blocked = true;
            var inf = mobInfo(m);
            if (inf.climber && blocked && JS.hypot(m.wishX, m.wishZ) > 0.3 && !wet)
            {
                m.climbing = true;
                double cy = m.y + 3.4 * dt;
                if (mobSpaceClear(m.x, cy, m.z, m)) { m.y = cy; m.vy = 0; m.onGround = false; }
            }
            else m.climbing = false;
            m.moving = JS.hypot(m.vx, m.vz) > 0.3;
            if (m.moving) m.walkPhase += dt * JS.hypot(m.vx, m.vz) * 1.6;
            return blocked;
        }
        static double mobPlayerDistance(Mob m) { return JS.hypot(player.x - m.x, player.y + 0.9 - (m.y + mobHeight(m) * 0.5), player.z - m.z); }
        static bool mobWaterLine(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            double d = JS.hypot(x2 - x1, y2 - y1, z2 - z1); int steps = Math.Max(2, (int)Math.Ceiling(d / 0.8));
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                if (!mobWaterLike(mobLoadedBlock(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t, z1 + (z2 - z1) * t))) return false;
            }
            return true;
        }
        static readonly int[][] ESCAPE_OPEN = { new[] { 2, 0 }, new[] { -2, 0 }, new[] { 0, 2 }, new[] { 0, -2 } };
        static double[] mobChooseFishEscape(Mob m, double px, double pz)
        {
            double[] best = null; double bestScore = 0, bse = JS.hypot(m.x - px, m.z - pz), oldX = m.escDX, oldZ = m.escDZ, ax = m.x - px, az = m.z - pz, al = JS.hypot(ax, az);
            if (al == 0) al = 1;
            ax /= al; az /= al;
            for (int pass = 0; pass < 2; pass++)
            {
                double near = pass == 0 ? 6 : 2, far = pass == 0 ? 16 : 6;
                for (int k = 0; k < 12; k++)
                {
                    double a = JS.random() * Math.PI * 2, d = near + JS.random() * (far - near), x = m.x + Math.Cos(a) * d, z = m.z + Math.Sin(a) * d, y = m.y + (JS.random() - 0.5) * 4;
                    if (!mobWaterLike(mobLoadedBlock(x, y, z))) continue;
                    double gain = JS.hypot(x - px, z - pz) - bse;
                    if (gain <= 0) continue;
                    double ld = JS.hypot(x - m.x, z - m.z); if (ld == 0) ld = 1;
                    double dx = (x - m.x) / ld, dz = (z - m.z) / ld;
                    if (dx * ax + dz * az < -0.15 || !mobWaterLine(m.x, m.y + 0.2, m.z, x, y, z)) continue;
                    int open = 0;
                    foreach (var o in ESCAPE_OPEN) if (mobWaterLike(mobLoadedBlock(x + o[0], y, z + o[1]))) open++;
                    double score = gain + open * 1.5 + (dx * oldX + dz * oldZ) * 4;
                    if (score > bestScore) { bestScore = score; best = new[] { x, y, z }; }
                }
                if (best != null) break;
            }
            if (best != null) return best;
            double[] fallback = null; double sc = 0;
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8, dx = Math.Cos(a), dz = Math.Sin(a);
                int reach = 0;
                for (int d = 1; d <= 8 && mobWaterLike(mobLoadedBlock(m.x + dx * d, m.y + 0.2, m.z + dz * d)); d++) reach = d;
                if (reach != 0 && dx * ax + dz * az >= -0.15)
                {
                    double x = m.x + dx * reach, z = m.z + dz * reach, s = reach + (JS.hypot(x - px, z - pz) - bse) * 2 + (dx * oldX + dz * oldZ) * 4;
                    if (s > sc) { sc = s; fallback = new[] { x, m.y, z }; }
                }
            }
            return fallback;
        }
        static bool mobSwimCan(Mob m, bool wasWater, double x, double y, double z) { return mobSpaceClear(x, y, z, m) && (!wasWater || mobWaterLike(mobLoadedBlock(x, y + 0.05, z))); }
        static void mobSwimPhysics(Mob m, double dt)
        {
            if (!mobSpaceClear(m.x, m.y, m.z, m))
            {
                for (int k = 0; k <= 6; k++)
                {
                    int off = k <= 3 ? k : 3 - k; double y = Math.Floor(m.y) + off + 0.3;
                    if (mobSpaceClear(m.x, y, m.z, m) && mobWaterLike(mobLoadedBlock(m.x, y + mobHeight(m) * 0.5, m.z))) { m.y = y; break; }
                }
                m.vx = m.vy = m.vz = 0;
            }
            m.inWater = mobWaterLike(mobLoadedBlock(m.x, m.y + 0.05, m.z));
            if (m.inWater == true)
            {
                double k = m.fleeing ? 10 : 4;
                m.vx += (m.wishX - m.vx) * Math.Min(1, dt * k); m.vy += (m.wishY - m.vy) * Math.Min(1, dt * k); m.vz += (m.wishZ - m.vz) * Math.Min(1, dt * k);
            }
            else
            {
                m.vy -= MOB_GRAVITY * dt;
                m.vx *= Math.Max(0, 1 - dt * 4); m.vz *= Math.Max(0, 1 - dt * 4);
            }
            double speed = JS.hypot(m.vx, m.vy, m.vz);
            if (speed > 0.15)
            {
                m.targetAngle = Math.Atan2(m.vx, -m.vz);
                double pitch = -Math.Atan2(m.vy, JS.hypot(m.vx, m.vz));
                m.pitch += (pitch - m.pitch) * Math.Min(1, dt * 4);
            }
            turnToward(m, dt * 4);
            bool wasWater = m.inWater == true;
            double nx = m.x + m.vx * dt;
            if (mobSwimCan(m, wasWater, nx, m.y, m.z)) m.x = nx; else { m.vx = 0; m.wishX = -m.wishX; }
            double nz = m.z + m.vz * dt;
            if (mobSwimCan(m, wasWater, m.x, m.y, nz)) m.z = nz; else { m.vz = 0; m.wishZ = -m.wishZ; }
            double dy = m.vy * dt; int vs = Math.Max(1, (int)Math.Ceiling(Math.Abs(dy) / 0.35)); double vstep = dy / vs;
            bool landed = false;
            for (int k = 0; k < vs; k++)
            {
                double ny = m.y + vstep;
                if (mobSwimCan(m, wasWater, m.x, ny, m.z)) { m.y = ny; m.onGround = false; continue; }
                if (vstep <= 0)
                {
                    double gy = mobGroundHeightNear(m.x, m.z, ny, m, 0.75, 1.5);
                    if (!double.IsNaN(gy)) { m.y = gy; m.vy = 0; m.onGround = true; landed = true; } else m.vy = 0;
                }
                else m.vy = 0;
                break;
            }
            if (!landed && dy == 0 && !wasWater) m.onGround = !double.IsNaN(mobGroundHeightNear(m.x, m.z, m.y, m, 0.05, 0.12));
            m.moving = true;
            m.walkPhase += dt * (0.6 + speed * 0.5);
        }
        static bool mobSourcePlayerInWater() { return mobWaterLike(getBlock(player.x, player.y + 1.38, player.z)) || mobWaterLike(getBlock(player.x, player.y + 0.5, player.z)); }
        static Entity mobNearestSharkPrey(Mob m, double playerDist)
        {
            double detect = mobInfo(m).detect != 0 ? mobInfo(m).detect : 20;
            if (playerDist < detect && !player.creative && !player.dead && mobSourcePlayerInWater() && mobWaterLine(m.x, m.y + 0.2, m.z, player.x, player.y + 0.5, player.z)) return player;
            Mob best = null; double dist = detect;
            foreach (var o in liveMobs)
            {
                if (o == m || o.dead) continue;
                var inf = mobInfo(o);
                if (!inf.aquatic || inf.hunter) continue;
                double d = JS.hypot(o.x - m.x, o.y - m.y, o.z - m.z);
                if (d < dist && mobWaterLine(m.x, m.y + 0.2, m.z, o.x, o.y + 0.2, o.z)) { dist = d; best = o; }
            }
            return best;
        }
        static void updateAquaticSource(Mob m, double dt)
        {
            var inf = mobInfo(m);
            double pd = JS.hypot(player.x - m.x, player.y + 0.9 - (m.y + 0.2), player.z - m.z);
            if (m.inWater == false)
            {
                m.dryT += dt;
                if (m.dryT >= 1) { m.dryT -= 1; damageMobSource(m, 1, null, false, true); if (m.dead) return; }
                m.hopT -= dt;
                if (m.onGround && m.hopT <= 0)
                {
                    m.hopT = 0.5 + JS.random() * 0.5;
                    double a = JS.random() * Math.PI * 2;
                    m.vy = 2.6; m.vx = Math.Cos(a) * 0.7; m.vz = Math.Sin(a) * 0.7;
                    m.angle = Math.Atan2(m.vx, -m.vz); m.targetAngle = m.angle;
                }
                m.wishX = m.wishY = m.wishZ = 0;
                mobSwimPhysics(m, dt);
                return;
            }
            m.dryT = 0;
            m.fleeT = Math.Max(0, m.fleeT - dt);
            m.swimT -= dt;
            if (inf.hunter)
            {
                m.atkT += dt;
                m.huntT -= dt;
                if (m.huntT <= 0) { m.huntT = 0.4; m.prey = mobNearestSharkPrey(m, pd); }
                var prey = m.prey;
                if (prey != player && prey != null && !isLiveMob(prey as Mob)) prey = m.prey = null;
                if (prey != null)
                {
                    bool isP = prey == player;
                    double tx = isP ? player.x : prey.x, ty = isP ? player.y + 0.4 : prey.y, tz = isP ? player.z : prey.z, dx = tx - m.x, dy = ty - m.y, dz = tz - m.z, d = JS.hypot(dx, dy, dz);
                    if (d == 0) d = 1;
                    m.wishX = dx / d * inf.speed; m.wishY = dy / d * inf.speed * 0.6; m.wishZ = dz / d * inf.speed;
                    m.fleeing = false; m.swimT = 0.5;
                    if (d < mobWidth(m) + (isP ? 1.1 : 0.9) && m.atkT > 1.2 && mobWaterLine(m.x, m.y + 0.2, m.z, tx, ty, tz))
                    {
                        m.atkT = 0;
                        if (isP) damagePlayer(inf.melee != 0 ? inf.melee : 6, "акула");
                        else damageMobSource((Mob)prey, inf.melee != 0 ? inf.melee : 6, null, false, false, m);
                    }
                    if (!mobWaterLike(mobLoadedBlock(m.x, m.y + 0.9, m.z))) m.wishY = Math.Min(m.wishY, -0.15);
                    mobSwimPhysics(m, dt);
                    return;
                }
            }
            if (!inf.hunter && (pd < 8 || m.fleeT > 0))
            {
                bool threatActive = m.threatT > 0;
                double px = threatActive ? m.threatX : player.x, pz = threatActive ? m.threatZ : player.z;
                m.escT -= dt;
                bool choose = m.escX == null || m.escT <= 0 || JS.hypot(m.escX.Value - m.x, m.escZ - m.z) < 1.5;
                if (!choose && m.escX != null)
                {
                    double ax = m.x - px, az = m.z - pz, al = JS.hypot(ax, az); if (al == 0) al = 1;
                    double old = JS.hypot(m.escX.Value - px, m.escZ - pz), dx = m.escX.Value - m.x, dz = m.escZ - m.z, dl = JS.hypot(dx, dz); if (dl == 0) dl = 1;
                    if (old < al || dx / dl * ax / al + dz / dl * az / al < -0.15) choose = true;
                }
                if (choose)
                {
                    var q = mobChooseFishEscape(m, px, pz);
                    if (q != null)
                    {
                        double d = JS.hypot(q[0] - m.x, q[2] - m.z); if (d == 0) d = 1;
                        m.escDX = (q[0] - m.x) / d; m.escDZ = (q[2] - m.z) / d; m.escX = q[0]; m.escY = q[1]; m.escZ = q[2]; m.escT = 8;
                    }
                    else { m.escX = null; m.escT = 0.6; }
                }
                if (m.escX != null)
                {
                    double speed = inf.speed * (m.fleeT > 0 ? 3.8 : 3.4), dx = m.escX.Value - m.x, dy = m.escY - m.y, dz = m.escZ - m.z, d = JS.hypot(dx, dy, dz); if (d == 0) d = 1;
                    m.wishX = dx / d * speed; m.wishY = dy / d * speed * 0.5; m.wishZ = dz / d * speed;
                    m.fleeing = true; m.swimT = 0.5;
                }
                else m.fleeing = false;
            }
            else if (m.fleeing) { m.fleeing = false; m.escX = null; m.escT = 0; m.escDX = m.escDZ = 0; m.swimT = 0; }
            if (!m.fleeing && m.swimT <= 0)
            {
                m.escDX = m.escDZ = 0;
                m.swimT = 2.5 + JS.random() * 2.5;
                if (JS.random() < 0.35)
                {
                    double cur = JS.hypot(m.wishX, m.wishZ), a = cur > 0.01 ? Math.Atan2(m.wishX, m.wishZ) : JS.random() * Math.PI * 2;
                    m.wishX = Math.Sin(a) * inf.speed * 0.2; m.wishZ = Math.Cos(a) * inf.speed * 0.2; m.wishY = 0;
                }
                else
                {
                    double a = JS.random() * Math.PI * 2;
                    m.wishX = Math.Cos(a) * inf.speed; m.wishZ = Math.Sin(a) * inf.speed; m.wishY = (JS.random() - 0.5) * inf.speed * 0.2;
                    double sx = 0, sz = 0, rx = 0, rz = 0; int n = 0;
                    foreach (var o in liveMobs)
                    {
                        if (o == m || o.dead || o.type != m.type) continue;
                        double d = JS.hypot(o.x - m.x, o.y - m.y, o.z - m.z);
                        if (d > 8 || d < 0.001) continue;
                        sx += o.x; sz += o.z; n++;
                        if (d < 1.8) { rx += (m.x - o.x) / d; rz += (m.z - o.z) / d; }
                    }
                    if (n != 0)
                    {
                        double dx = sx / n - m.x, dz = sz / n - m.z, d = JS.hypot(dx, dz);
                        if (d > 4) { m.wishX += dx / d * inf.speed * 0.4; m.wishZ += dz / d * inf.speed * 0.4; }
                    }
                    double rd = JS.hypot(rx, rz);
                    if (rd > 0) { m.wishX += rx / rd * inf.speed * 0.8; m.wishZ += rz / rd * inf.speed * 0.8; }
                }
            }
            if (mobWaterLike(mobLoadedBlock(m.x, m.y + 0.9, m.z))) { if (!mobWaterLike(mobLoadedBlock(m.x, m.y - 0.6, m.z))) m.wishY = Math.Max(m.wishY, 0.15); }
            else m.wishY = Math.Min(m.wishY, -0.15);
            mobSwimPhysics(m, dt);
            m.swimPhase += dt * (m.type == "shark" ? 5 : 4);
        }
        static double[] sourceSkeletonHand(Mob m)
        {
            double walk = m.moving ? Math.Sin(m.walkPhase * 3) * 0.6 : 0, arm = -1.25 + walk * 0.08, s = 1 / 16.0, w = 5, l = 13, p = 24, c = Math.Cos(arm), sn = Math.Sin(arm), q = l - p,
                hy = q * c + p, hz = q * sn, D = -Math.Cos(m.angle), T = Math.Sin(m.angle);
            return new[] { m.x + (w * D + hz * T) * s + T * 0.1, m.y + hy * s, m.z + (-w * T + hz * D) * s + D * 0.1 };
        }
        static void sourceShootArrow(Mob m)
        {
            var q = sourceSkeletonHand(m);
            double sx = q[0], sy = q[1], sz = q[2], dx = player.x - sx, dy = player.y + 1.2 - sy, dz = player.z - sz, d = JS.hypot(dx, dy, dz); if (d == 0) d = 1;
            projectiles.Add(new Projectile { x = sx, y = sy, z = sz, vx = dx / d * 16, vy = dy / d * 16 + 1.5, vz = dz / d * 16, age = 0, src = m, damage = 3, stuck = false });
        }
        static readonly List<Mob> mobPendingAdds = new List<Mob>();
        static void queueMobAdd(Mob m) { if (m != null) mobPendingAdds.Add(m); }
        static void flushMobAdds()
        {
            if (mobPendingAdds.Count == 0) return;
            foreach (var m in mobPendingAdds)
            {
                var c = chunkFastGet(JS.floor(m.x / CHUNK), JS.floor(m.z / CHUNK));
                if (c != null) attachLiveMob(m, c); else storeMobRecord(serializeMob(m));
            }
            mobPendingAdds.Clear();
        }
        static void updateLandSource(Mob m, double dt)
        {
            var inf = mobInfo(m);
            bool survival = !player.creative && !player.dead;
            double dist = mobPlayerDistance(m);
            if (m.seenT > 0) m.seenT = Math.Max(0, m.seenT - dt);
            if (m.detourT > 0 && !(inf.creeper && m.fuse >= 0))
            {
                m.detourT -= dt;
                if (m.fleeT > 0) m.fleeT -= dt;
                mobWishToward(m, m.detX, m.detZ, inf.speed * (m.fleeT > 0 ? 2 : 1));
            }
            else if (m.fleeT > 0)
            {
                m.fleeT -= dt;
                double tx = m.threatT > 0 ? m.threatX : player.x, tz = m.threatT > 0 ? m.threatZ : player.z;
                mobFollowPath(m, m.x + (m.x - tx) * 2, m.z + (m.z - tz) * 2, inf.speed * 2, dt, m.y);
            }
            else if (m.loveT > 0)
            {
                m.loveT -= dt;
                Mob mate = null; double best = 12;
                foreach (var o in liveMobs)
                {
                    if (o == m || o.dead || o.type != m.type || o.loveT <= 0 || o.baby) continue;
                    double d = JS.hypot(o.x - m.x, o.z - m.z);
                    if (d < best) { best = d; mate = o; }
                }
                if (mate != null)
                {
                    mobFollowPath(m, mate.x, mate.z, inf.speed, dt, mate.y);
                    if (best < 1.4 && !mobSightBlocked(m.x, m.y + mobHeight(m) * 0.6, m.z, mate.x, mate.y + mobHeight(mate) * 0.6, mate.z))
                    {
                        m.loveT = 0; mate.loveT = 0; m.breedCd = 120; mate.breedCd = 120;
                        var opt = new MobOpts { persist = true, baby = true, growT = 120 };
                        if (m.type == "sheep") opt.color = JS.random() < 0.5 ? m.color : mate.color;
                        var baby = makeMob(m.type, (m.x + mate.x) / 2, Math.Max(m.y, mate.y), (m.z + mate.z) / 2, opt);
                        queueMobAdd(baby);
                        saveGameSoon();
                    }
                }
                else mobStop(m);
            }
            else if (inf.creeper && m.fuse >= 0)
            {
                bool visible = survival && dist < (inf.detect != 0 ? inf.detect : 16) && mobCanAggroSource(m) && mobCanSeePlayerCached(m);
                if (!visible || dist > 5) { m.fuse = -1; mobStop(m); }
                else
                {
                    m.fuse += dt;
                    mobStop(m);
                    if (m.fuse > 1.5) { m.dead = true; explodeAt(m.x, m.y + 0.5, m.z, 3); return; }
                }
            }
            else
            {
                bool canAcquire = inf.hostile && survival && dist < (inf.detect != 0 ? inf.detect : 16) && mobCanAggroSource(m), visible = canAcquire && mobCanSeePlayerCached(m);
                if (visible) { m.lastSeenX = player.x; m.lastSeenY = player.y; m.lastSeenZ = player.z; m.seenT = 3; }
                bool remembered = inf.hostile && survival && !visible && m.seenT > 0 && JS.isFinite(m.lastSeenX) && JS.isFinite(m.lastSeenZ), hasTarget = visible || remembered;
                double tx = visible ? player.x : m.lastSeenX, tz = visible ? player.z : m.lastSeenZ, tdist = visible ? dist : JS.hypot(tx - m.x, tz - m.z);
                double ty = visible ? player.y : m.lastSeenY;
                if (hasTarget)
                {
                    if (inf.leaper)
                    {
                        mobFollowPath(m, tx, tz, inf.speed, dt, ty);
                        m.leapT -= dt;
                        if (m.onGround && m.leapT <= 0 && tdist > 2 && tdist < 8)
                        {
                            m.leapT = 1.5 + JS.random() * 1.5;
                            double dx = tx - m.x, dz = tz - m.z, d = JS.hypot(dx, dz); if (d == 0) d = 1;
                            m.vx = dx / d * 7; m.vz = dz / d * 7; m.vy = 6.5; m.kbT = 0.45;
                        }
                        m.atkT += dt;
                        if (visible && dist < 1.6 && Math.Abs(player.y - m.y) < 2 && m.atkT > 1) { m.atkT = 0; damagePlayer(inf.melee != 0 ? inf.melee : 2, "паук"); }
                    }
                    else if (inf.jumper)
                    {
                        m.hopT -= dt;
                        if (m.onGround)
                        {
                            if (m.hopT <= 0)
                            {
                                m.hopT = 0.6 + JS.random() * 0.6;
                                double dx = tx - m.x, dz = tz - m.z, d = JS.hypot(dx, dz); if (d == 0) d = 1;
                                m.targetAngle = Math.Atan2(dx, -dz);
                                m.wishX = dx / d * inf.speed; m.wishZ = dz / d * inf.speed;
                                m.vx = m.wishX; m.vz = m.wishZ; m.vy = 7.4; m.kbT = 0.4; m.sqY = 1.25;
                            }
                            else mobStop(m);
                        }
                        m.atkT += dt;
                        if (visible && inf.melee != 0 && dist < mobWidth(m) + 1.1 && Math.Abs(player.y - m.y) < mobHeight(m) + 0.6 && m.atkT > 0.5) { m.atkT = 0; damagePlayer(inf.melee, "слизень"); }
                    }
                    else if (inf.teleports)
                    {
                        if (tdist > 3) mobFollowPath(m, tx, tz, inf.speed * 1.25, dt, ty);
                        else { mobStop(m); m.targetAngle = Math.Atan2(tx - m.x, -(tz - m.z)); }
                        if (visible && m.tpT <= 0 && dist > 6 && dist < 32)
                        {
                            m.tpT = 2 + JS.random() * 2;
                            double a = JS.random() * Math.PI * 2;
                            teleportMobSource(m, player.x + Math.Cos(a) * 3, player.z + Math.Sin(a) * 3, 2);
                        }
                        m.atkT += dt;
                        if (visible && dist < 2.2 && Math.Abs(player.y - m.y) < 3 && m.atkT > 1) { m.atkT = 0; damagePlayer(inf.melee != 0 ? inf.melee : 7, "эндермен"); }
                    }
                    else if (inf.ranged)
                    {
                        if (tdist > 9) mobFollowPath(m, tx, tz, inf.speed, dt, ty);
                        else if (visible && dist < 5) mobFollowPath(m, m.x + (m.x - player.x) * 2, m.z + (m.z - player.z) * 2, inf.speed, dt, m.y);
                        else { mobStop(m); m.targetAngle = Math.Atan2(tx - m.x, -(tz - m.z)); }
                        m.shootT += dt;
                        if (visible && m.shootT > 2 && dist < 12) { m.shootT = 0; sourceShootArrow(m); }
                    }
                    else if (inf.creeper)
                    {
                        mobFollowPath(m, tx, tz, inf.speed, dt, ty);
                        if (visible && dist < 2.2) m.fuse = 0;
                    }
                    else
                    {
                        mobFollowPath(m, tx, tz, inf.speed, dt, ty);
                        m.atkT += dt;
                        if (visible && dist < 1.4 && Math.Abs(player.y - m.y) < 2 && m.atkT > 1) { m.atkT = 0; damagePlayer(inf.melee != 0 ? inf.melee : 3, m.type == "zombie" ? "зомби" : m.type); }
                    }
                }
                else
                {
                    m.aiT -= dt;
                    if (m.aiT <= 0)
                    {
                        m.aiT = 3 + JS.random() * 5;
                        if (JS.random() < 0.6) { m.tgtX = m.x + (JS.random() - 0.5) * 12; m.tgtZ = m.z + (JS.random() - 0.5) * 12; }
                        else { m.tgtX = m.x; m.tgtZ = m.z; }
                    }
                    if (inf.jumper)
                    {
                        m.hopT -= dt;
                        if (m.onGround)
                        {
                            if (m.hopT <= 0)
                            {
                                m.hopT = 0.8 + JS.random() * 1.2;
                                double dx = m.tgtX - m.x, dz = m.tgtZ - m.z, d = JS.hypot(dx, dz);
                                if (d > 0.5)
                                {
                                    m.targetAngle = Math.Atan2(dx, -dz);
                                    m.wishX = dx / d * inf.speed * 0.8; m.wishZ = dz / d * inf.speed * 0.8;
                                    m.vx = m.wishX; m.vz = m.wishZ; m.vy = 7.4; m.kbT = 0.4; m.sqY = 1.25;
                                }
                            }
                            else mobStop(m);
                        }
                    }
                    else if (m.tgtX != m.x || m.tgtZ != m.z)
                    {
                        if (mobFollowPath(m, m.tgtX, m.tgtZ, inf.speed * 0.85, dt, m.y)) { m.tgtX = m.x; m.tgtZ = m.z; }
                    }
                    else mobStop(m);
                }
            }
            if (m.onGround && mobCliffAhead(m))
            {
                double len = JS.hypot(m.wishX, m.wishZ); if (len == 0) len = 1;
                double px = m.wishZ / len, pz = -m.wishX / len, sgn = JS.random() < 0.5 ? 1 : -1;
                mobStop(m);
                m.path = null; m.stuckT = 0; m.detourT = 0.5; m.detX = m.x + px * sgn * 3; m.detZ = m.z + pz * sgn * 3;
            }
            if (mobDangerAhead(m))
            {
                double len = JS.hypot(m.wishX, m.wishZ); if (len == 0) len = 1;
                double sgn = JS.random() < 0.5 ? 1 : -1, px = m.wishZ / len, pz = -m.wishX / len;
                mobStop(m);
                m.path = null; m.detourT = 0.7; m.detX = m.x + px * sgn * 3; m.detZ = m.z + pz * sgn * 3;
            }
            else if ((!inf.hostile || inf.hatesWater) && !mobWaterLike(mobLoadedBlock(m.x, m.y + 0.3, m.z)) && mobLiquidAhead(m))
            {
                mobStop(m);
                m.detourT = 0; m.tgtX = m.x; m.tgtZ = m.z; m.aiT = Math.Min(m.aiT, 0.5);
            }
            double desired = JS.hypot(m.wishX, m.wishZ), ox = m.x, oz = m.z; bool wasGround = m.onGround;
            mobPhysicsLand(m, dt);
            if (inf.jumper)
            {
                if (!wasGround && m.onGround && m.sqY > 0.95) m.sqY = 0.72;
                m.sqY += (1 - m.sqY) * Math.Min(1, dt * 6);
            }
            if (desired > 0.5)
            {
                double moved = JS.hypot(m.x - ox, m.z - oz);
                if (moved < desired * dt * 0.3) m.stuckT += dt; else m.stuckT = Math.Max(0, m.stuckT - dt * 2);
                if (m.stuckT > 0.5 && m.detourT <= 0)
                {
                    m.stuckT = 0; m.detourT = 0.7;
                    double sg = JS.random() < 0.5 ? 1 : -1;
                    m.detX = m.x + m.wishZ / desired * 5 * sg; m.detZ = m.z - m.wishX / desired * 5 * sg;
                }
            }
            else m.stuckT = 0;
        }
        static double mobSpawnFar() { return Math.Max(44, Math.Min(128, activeSimulationDistance() * CHUNK - CHUNK)); }
        static double mobDespawnFar() { return Math.Min(128, mobSpawnFar() + 24); }
        static int sourceHostileCap() { double far = mobSpawnFar(), scale = (far * far - 24 * 24) / (44 * 44 - 24 * 24); return (int)Math.Min(24, JS.round(12 * scale)); }
        static void updateEntitiesFixed(double dt, double now)
        {
            int passiveOverflow = Math.Max(0, countMobs((m, inf) => !m.persist && !inf.hostile && !inf.aquatic) - 40);
            for (int i = liveMobs.Count - 1; i >= 0; i--)
            {
                if (i >= liveMobs.Count) continue;
                var m = liveMobs[i];
                if (m == null || m.dead) { removeLiveMob(m, false); continue; }
                var owner = m._owner;
                if (owner != null && !simulationChunkActive(owner.cx, owner.cz)) continue;
                m.age += dt;
                if (m.hurtT > 0) m.hurtT -= dt;
                if (m.breedCd > 0) m.breedCd -= dt;
                if (m.threatT > 0) m.threatT = Math.Max(0, m.threatT - dt);
                if (m.losT > 0) m.losT = Math.Max(0, m.losT - dt);
                if (m.baby) { m.growT -= dt; if (m.growT <= 0) { m.baby = false; m.growT = 0; } }
                if (m.sheared) { m.woolT -= dt; if (m.woolT <= 0) { m.sheared = false; m.woolT = 0; } }
                var inf = mobInfo(m);
                if (m.lavaT > 0) m.lavaT -= dt;
                int hazard = mobLoadedBlock(m.x, m.y + 0.3, m.z);
                if (m.lavaT <= 0 && (isLava(hazard) || hazard == B.FIRE))
                {
                    m.lavaT = 0.7;
                    damageMobSource(m, isLava(hazard) ? 4 : 1, null, false, true);
                    if (m.dead) { removeLiveMob(m, false); continue; }
                }
                double dist = mobPlayerDistance(m);
                if (!m.persist && (inf.hostile || inf.despawns) && dist > mobDespawnFar()) { m.dead = true; removeLiveMob(m, false); continue; }
                if (passiveOverflow > 0 && !m.persist && !inf.hostile && !inf.aquatic && dist > mobDespawnFar()) { passiveOverflow--; m.dead = true; removeLiveMob(m, false); continue; }
                if (!m.persist && (inf.hostile || inf.despawns) && dist > 32 && JS.random() < dt / 25) { m.dead = true; removeLiveMob(m, false); continue; }
                var tc = chunkFastGet(JS.floor(m.x / CHUNK), JS.floor(m.z / CHUNK));
                if (tc == null) { removeLiveMob(m, true); continue; }
                if (m._owner != tc) moveLiveMobChunk(m, tc);
                if (inf.aquatic)
                {
                    updateAquaticSource(m, dt);
                    if (m.dead) { removeLiveMob(m, false); continue; }
                    if (m.y < WORLD_MIN_Y - 8) { m.dead = true; removeLiveMob(m, false); continue; }
                }
                else
                {
                    if (m.angryT > 0) m.angryT -= dt;
                    if (inf.teleports)
                    {
                        if (m.tpT > 0) m.tpT -= dt;
                        if (m.angryT <= 0 && !player.creative && !player.dead)
                        {
                            double sd = mobPlayerDistance(m);
                            if (playerStaresAtMob(m, sd)) { m.stareT += dt; if (m.stareT > 0.25) { m.angryT = 30; m.stareT = 0; } }
                            else m.stareT = 0;
                        }
                        if (inf.hatesWater && mobWaterLike(mobLoadedBlock(m.x, m.y + mobHeight(m) * 0.5, m.z)))
                        {
                            m.dryT -= dt;
                            if (m.dryT <= 0)
                            {
                                m.dryT = 0.5;
                                damageMobSource(m, 1, null, false, true);
                                if (m.dead) { removeLiveMob(m, false); continue; }
                            }
                            if (m.tpT <= 0 && teleportMobSource(m, m.x, m.z, 24)) m.tpT = 1;
                        }
                    }
                    if (inf.burns && mobDaylight() && getWorldLight(m.x, m.y + mobHeight(m), m.z)[0] >= 14 / 15.0)
                    {
                        m.burnT += dt;
                        if (m.burnT >= 1.2)
                        {
                            m.burnT = 0;
                            damageMobSource(m, 2, null, false, true);
                            if (m.dead) { removeLiveMob(m, false); continue; }
                        }
                    }
                    if (m.type == "chicken" && !m.baby)
                    {
                        m.eggT -= dt;
                        if (m.eggT <= 0) { m.eggT = 300 + JS.random() * 300; spawnWorldDrop("egg", 1, m.x, m.y + 0.3, m.z); }
                    }
                    updateLandSource(m, dt);
                    if (m.dead) { removeLiveMob(m, false); continue; }
                    if (m.y < WORLD_MIN_Y - 8) { m.dead = true; removeLiveMob(m, false); continue; }
                }
                var nc = chunkFastGet(JS.floor(m.x / CHUNK), JS.floor(m.z / CHUNK));
                if (nc == null) { removeLiveMob(m, true); continue; }
                if (m._owner != nc) moveLiveMobChunk(m, nc);
            }
            flushMobAdds();
        }
        static bool projectileHitsMob(Projectile p, Mob m)
        {
            double r = mobWidth(m) + 0.15;
            return Math.Abs(p.x - m.x) < r && Math.Abs(p.z - m.z) < r && p.y > m.y - 0.15 && p.y < m.y + mobHeight(m) + 0.15;
        }
        public static void releaseStuckProjectilesAt(int x, int y, int z)
        {
            foreach (var p in projectiles)
                if (p.stuck && p.hx == x && p.hy == y && p.hz == z) { p.stuck = false; p.vx = 0; p.vy = -0.5; p.vz = 0; p.dir = null; p.age = 0; }
        }
        static void updateProjectilesFixed(double dt)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                p.age += dt;
                if (p.stuck)
                {
                    if (!player.creative && !player.dead && JS.hypot(player.x - p.x, player.y + 0.9 - p.y, player.z - p.z) < 2.2)
                    {
                        int left = addItem("arrow", 1);
                        if (left <= 0) { drawHotbar(); saveGameSoon(); projectiles.RemoveAt(i); continue; }
                    }
                    if (p.age > 60) projectiles.RemoveAt(i);
                    continue;
                }
                p.vy -= 12 * dt;
                if (p.age > 8) { projectiles.RemoveAt(i); continue; }
                double speed = JS.hypot(p.vx, p.vy, p.vz); int steps = Math.Max(1, (int)Math.Ceiling(speed * dt / 0.4)); double sd = dt / steps;
                bool remove = false;
                for (int k = 0; k < steps && !remove; k++)
                {
                    double ox = p.x, oy = p.y, oz = p.z;
                    p.x += p.vx * sd; p.y += p.vy * sd; p.z += p.vz * sd;
                    if (mobPointBlocked(p.x, p.y, p.z))
                    {
                        double dl = JS.hypot(p.vx, p.vy, p.vz); if (dl == 0) dl = 1;
                        p.dir = new[] { p.vx / dl, p.vy / dl, p.vz / dl };
                        double lo = 0, hi = 1;
                        for (int n = 0; n < 7; n++)
                        {
                            double t = (lo + hi) / 2;
                            if (mobPointBlocked(ox + (p.x - ox) * t, oy + (p.y - oy) * t, oz + (p.z - oz) * t)) hi = t; else lo = t;
                        }
                        double hx = ox + (p.x - ox) * hi, hy = oy + (p.y - oy) * hi, hz = oz + (p.z - oz) * hi;
                        p.hx = JS.floor(hx + p.dir[0] * 0.05); p.hy = JS.floor(hy + p.dir[1] * 0.05); p.hz = JS.floor(hz + p.dir[2] * 0.05);
                        double back = 0.3 * 1.4142 * 0.8;
                        p.x = hx - p.dir[0] * back; p.y = hy - p.dir[1] * back; p.z = hz - p.dir[2] * back;
                        p.vx = p.vy = p.vz = 0; p.stuck = true; p.age = 0;
                        break;
                    }
                    foreach (var m in liveMobs)
                    {
                        if (m == p.src || m.dead || !projectileHitsMob(p, m)) continue;
                        if (mobInfo(m).teleports)
                        {
                            if (p.fromPlayer) { m.angryT = 30; m.lastSeenX = player.x; m.lastSeenY = player.y; m.lastSeenZ = player.z; m.seenT = Math.Max(m.seenT, 3); m.losT = 0; }
                            teleportMobSource(m, m.x, m.z, 24);
                            m.tpT = 1;
                            remove = true;
                            break;
                        }
                        damageMobSource(m, p.damage != 0 ? p.damage : 3, new[] { p.x - p.vx * 0.05, p.y, p.z - p.vz * 0.05 }, p.fromPlayer, false, p.fromPlayer ? (Entity)player : p.src);
                        remove = true;
                        break;
                    }
                    if (remove) break;
                    if (!p.fromPlayer && !player.creative && !player.dead && JS.hypot(player.x - p.x, player.y + 0.9 - p.y, player.z - p.z) < 0.9) { damagePlayer(p.damage != 0 ? p.damage : 3, "стрела"); remove = true; }
                }
                if (remove) projectiles.RemoveAt(i);
            }
        }
        static void snapshotMobSimulation()
        {
            foreach (var m in liveMobs) { m.ox = m.x; m.oy = m.y; m.oz = m.z; m.oangle = m.angle; }
            foreach (var p in projectiles) { p.ox = p.x; p.oy = p.y; p.oz = p.z; }
        }
        public static double mobLerpAngle(double a, double b, double t)
        {
            if (!JS.isFinite(a)) a = b;
            double d = b - a;
            while (d > Math.PI) d -= Math.PI * 2;
            while (d < -Math.PI) d += Math.PI * 2;
            return a + d * t;
        }
        public static void tickMobSimulation(double frameDt, double now)
        {
            mobSimAccumulator = Math.Min(MOB_FIXED_DT * 6, mobSimAccumulator + frameDt);
            int steps = 0;
            while (mobSimAccumulator >= MOB_FIXED_DT && steps < MOB_MAX_STEPS)
            {
                snapshotMobSimulation();
                updateEntitiesFixed(MOB_FIXED_DT, now);
                updateProjectilesFixed(MOB_FIXED_DT);
                mobSimAccumulator -= MOB_FIXED_DT;
                steps++;
            }
            mobRenderAlpha = Math.Min(1, mobSimAccumulator / MOB_FIXED_DT);
        }

        // ---------------- natural spawning ----------------
        static bool mobSpawnSupportSolid(int id)
        {
            if (id == B.BED) return true;
            var b = bdef(id);
            return b != null && b.solid && !b.plant && !b.waterPlant && !isWater(id) && !isLava(id) && (b.special == null || b.special == "orientedCube");
        }
        static bool mobSpawnDryVolume(string type, double x, double feet, double z)
        {
            MobInfo inf;
            if (!MOB_INFO.TryGetValue(type, out inf) || inf.aquatic) return false;
            double r = !double.IsNaN(inf.cw) ? inf.cw : inf.w, h = inf.h != 0 ? inf.h : 1.7, x0 = x - r, x1 = x + r, z0 = z - r, z1 = z + r, top = feet + h;
            int minX = JS.floor(x0), maxX = JS.floor(x1 - 0.000001), minY = JS.floor(feet), maxY = JS.floor(top - 0.000001), minZ = JS.floor(z0), maxZ = JS.floor(z1 - 0.000001);
            for (int xx = minX; xx <= maxX; xx++)
                for (int yy = minY; yy <= maxY; yy++)
                    for (int zz = minZ; zz <= maxZ; zz++)
                    {
                        int id = mobLoadedBlockI(xx, yy, zz);
                        if (id < 0 || mobWaterLike(id) || isLava(id)) return false;
                    }
            return true;
        }
        static bool spawnHostileAt(string type, int x, int z, bool hostile = true)
        {
            MobInfo inf;
            if (!MOB_INFO.TryGetValue(type, out inf) || inf.aquatic) return false;
            var c = chunkFastGet(JS.floor(x / (double)CHUNK), JS.floor(z / (double)CHUNK));
            if (c == null) return false;
            int py = JS.floor(player.y), top = Math.Min(WORLD_MAX_Y - 3, py + 20), bottom = Math.Max(WORLD_MIN_Y + 1, py - 20), need = inf.h > 1.9 ? 3 : 2;
            bool night = mobNight(), isSlime = inf.slime != 0, slimeChunk = isSlime && slimeChunkSource(JS.floor(x / (double)CHUNK), JS.floor(z / (double)CHUNK));
            string surfaceBiome = isSlime ? biomeAt(x, z) : "";
            bool slimeSwamp = surfaceBiome == "SWAMP" || surfaceBiome == "MANGROVE_SWAMP";
            var candidates = new List<int>();
            var probe = new Mob { type = type };
            for (int y = top + 2, air = 0; y >= bottom; y--)
            {
                int id = mobLoadedBlockI(x, y, z);
                if (id == B.AIR) { air++; continue; }
                if (air >= need && mobSpawnSupportSolid(id))
                {
                    int feet = y + 1; bool ok;
                    if (isSlime) ok = (slimeChunk && feet < 40) || (slimeSwamp && night && feet >= 50 && feet <= 70);
                    else if (hostile) { var light = getWorldLight(x + 0.5, feet, z + 0.5); ok = light[1] <= 7 / 15.0 && (night || light[0] <= 4 / 15.0); }
                    else ok = id == B.GRASS;
                    if (ok && mobSpaceClear(x + 0.5, feet, z + 0.5, probe) && mobSpawnDryVolume(type, x + 0.5, feet, z + 0.5)) candidates.Add(feet);
                }
                air = 0;
            }
            if (candidates.Count == 0) return false;
            int f = candidates[(int)Math.Floor(JS.random() * candidates.Count)];
            var m = makeMob(type, x + 0.5, f, z + 0.5, new MobOpts { dynamicHostile = hostile });
            if (m == null) return false;
            attachLiveMob(m, c);
            return true;
        }
        static int spawnHostileGroup(string type, int x, int z, int maxCount, bool hostile = true)
        {
            int made = 0, px = x, pz = z;
            for (int k = 0; k < 4 && made < maxCount; k++)
            {
                px += (int)Math.Floor(JS.random() * 6) - (int)Math.Floor(JS.random() * 6);
                pz += (int)Math.Floor(JS.random() * 6) - (int)Math.Floor(JS.random() * 6);
                if (spawnHostileAt(type, px, pz, hostile)) made++;
            }
            return made;
        }
        static readonly string[] HOSTILE_TYPES = { "zombie", "zombie", "skeleton", "creeper", "spider", "spider", "enderman" }, PASSIVE_TYPES = { "pig", "cow", "sheep", "chicken" };
        public static void tickHostileSpawns(double dt)
        {
            hostileSpawnT -= dt; passiveSpawnT -= dt; fishSpawnT -= dt; sharkSpawnT -= dt;
            if (player.dead) return;
            if (!modEnabled("sharks"))
                for (int i = liveMobs.Count - 1; i >= 0; i--) { var m = liveMobs[i]; if (m != null && !m.dead && mobInfo(m).hunter) { m.dead = true; removeLiveMob(m, false); } }
            if (sharkSpawnT <= 0)
            {
                sharkSpawnT = 12;
                if (modEnabled("sharks") && countMobs((m, inf) => inf.hunter) < 2)
                    for (int i = 0; i < 6; i++)
                    {
                        double a = JS.random() * Math.PI * 2, d = 18 + JS.random() * 30;
                        if (spawnSharkAt(JS.floor(player.x + Math.Cos(a) * d), JS.floor(player.z + Math.Sin(a) * d))) break;
                    }
            }
            if (fishSpawnT <= 0)
            {
                fishSpawnT = 5;
                int aquatic = countMobs((m, inf) => inf.aquatic);
                for (int i = 0; i < 10 && aquatic < 40; i++)
                {
                    double a = JS.random() * Math.PI * 2, d = 10 + JS.random() * 30;
                    aquatic += spawnFishGroup(JS.floor(player.x + Math.Cos(a) * d), JS.floor(player.z + Math.Sin(a) * d), 40 - aquatic);
                }
            }
            if (hostileSpawnT <= 0)
            {
                int hostile = countMobs((m, inf) => inf.hostile && !inf.aquatic), cap = sourceHostileCap();
                if (hostile < cap)
                {
                    double a = JS.random() * Math.PI * 2, d = 24 + JS.random() * Math.Max(1, mobSpawnFar() - 24);
                    hostile += spawnHostileGroup("slime_big", JS.floor(player.x + Math.Cos(a) * d), JS.floor(player.z + Math.Sin(a) * d), Math.Min(2, cap - hostile), true);
                }
                hostileSpawnT = 2;
                if (hostile < cap)
                    for (int g = 0; g < 4 && hostile < cap; g++)
                    {
                        double a = JS.random() * Math.PI * 2, d = 24 + JS.random() * Math.Max(1, mobSpawnFar() - 24);
                        int x = JS.floor(player.x + Math.Cos(a) * d), z = JS.floor(player.z + Math.Sin(a) * d);
                        var type = HOSTILE_TYPES[(int)Math.Floor(JS.random() * HOSTILE_TYPES.Length)];
                        hostile += spawnHostileGroup(type, x, z, cap - hostile, true);
                    }
            }
            if (passiveSpawnT <= 0)
            {
                passiveSpawnT = 20;
                if (mobDaylight())
                {
                    int passive = countMobs((m, inf) => !inf.hostile && !inf.aquatic), cap = sourcePassiveCap();
                    if (passive < cap)
                    {
                        double a = JS.random() * Math.PI * 2, d = 24 + JS.random() * Math.Max(1, mobSpawnFar() - 24);
                        var type = PASSIVE_TYPES[(int)Math.Floor(JS.random() * PASSIVE_TYPES.Length)];
                        spawnHostileGroup(type, JS.floor(player.x + Math.Cos(a) * d), JS.floor(player.z + Math.Sin(a) * d), cap - passive, false);
                    }
                }
            }
        }
        public static int countMobs(Func<Mob, MobInfo, bool> pred)
        {
            int n = 0;
            foreach (var m in liveMobs) if (!m.dead && pred(m, mobInfo(m))) n++;
            return n;
        }
        static int sourcePassiveCap() { double far = mobSpawnFar(), scale = (far * far - 24 * 24) / (44 * 44 - 24 * 24); return (int)Math.Min(14, JS.round(8 * scale)); }
        static bool spawnSalmonAt(int x, int z)
        {
            var c = chunkFastGet(JS.floor(x / (double)CHUNK), JS.floor(z / (double)CHUNK));
            if (c == null) return false;
            var ys = new List<int>();
            int y0 = Math.Min(WORLD_MAX_Y - 3, SEA), y1 = Math.Max(WORLD_MIN_Y + 1, SEA - 24);
            for (int y = y0; y >= y1; y--) if (mobWaterLike(mobLoadedBlock(x + 0.5, y, z + 0.5)) && mobWaterLike(mobLoadedBlock(x + 0.5, y + 1, z + 0.5))) ys.Add(y);
            if (ys.Count == 0) return false;
            double yy = ys[(int)Math.Floor(JS.random() * ys.Count)] + 0.15 + JS.random() * 0.45;
            var m = makeMob("salmon", x + 0.5, yy, z + 0.5);
            if (m == null) return false;
            attachLiveMob(m, c);
            return true;
        }
        static int spawnFishGroup(int x, int z, int maxCount = 5)
        {
            int target = Math.Min(maxCount, 3 + (int)Math.Floor(JS.random() * 3)), made = 0;
            for (int a = 0; a < target * 4 && made < target; a++)
            {
                double ang = JS.random() * Math.PI * 2, d = 2 + JS.random() * 7;
                int px = (int)JS.round(x + Math.Cos(ang) * d), pz = (int)JS.round(z + Math.Sin(ang) * d);
                if (spawnSalmonAt(px, pz)) made++;
            }
            return made;
        }
        static bool oceanBiomeAt(int x, int z)
        {
            var b = biomeAt(x, z);
            return b == "OCEAN" || b == "COLD_OCEAN" || b == "LUKEWARM_OCEAN" || b == "WARM_OCEAN" || b == "FROZEN_OCEAN";
        }
        static bool spawnSharkAt(int x, int z)
        {
            var c = chunkFastGet(JS.floor(x / (double)CHUNK), JS.floor(z / (double)CHUNK));
            if (c == null || !oceanBiomeAt(x, z)) return false;
            var ys = new List<int>();
            for (int y = Math.Min(WORLD_MAX_Y - 3, SEA); y >= Math.Max(WORLD_MIN_Y + 1, SEA - 24) && mobWaterLike(mobLoadedBlock(x + 0.5, y, z + 0.5)); y--) ys.Add(y);
            if (ys.Count < 6) return false;
            int yy = ys[(int)Math.Floor(ys.Count * (0.4 + JS.random() * 0.5))];
            if (!mobWaterLike(mobLoadedBlock(x + 0.5, yy + 1, z + 0.5))) return false;
            var m = makeMob("shark", x + 0.5, yy + 0.2, z + 0.5);
            if (m == null) return false;
            attachLiveMob(m, c);
            return true;
        }
        public static List<Mob> spawnChunkMobs(int cx, int cz)
        {
            var key = ckey(cx, cz); var c = chunks.Get(key);
            if (c == null || seededMobChunks.Has(key)) return c != null ? c.mobs : new List<Mob>();
            seededMobChunks.Add(key);
            int aquaticLeft = Math.Max(0, 40 - countMobs((m, inf) => inf.aquatic));
            for (int n = 0; n < 3 && aquaticLeft > 0; n++)
                if (JS.random() < 0.4) aquaticLeft -= spawnFishGroup(cx * CHUNK + (int)Math.Floor(JS.random() * CHUNK), cz * CHUNK + (int)Math.Floor(JS.random() * CHUNK), aquaticLeft);
            if (JS.random() < 0.1)
            {
                var type = PASSIVE_TYPES[(int)Math.Floor(JS.random() * PASSIVE_TYPES.Length)];
                int x = cx * CHUNK + (int)Math.Floor(JS.random() * CHUNK), z = cz * CHUNK + (int)Math.Floor(JS.random() * CHUNK);
                spawnHostileGroup(type, x, z, 4, false);
            }
            return c.mobs;
        }
    }
}
