// Voxel Forge — Unity port. Physical dropped items / pickup and their sprite batches.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class WorldDrop
    {
        public string key; public int count, dur; public double x, y, z, vx, vy, vz, age, pickAfter;
    }

    public static partial class VF
    {
        // Source-main algorithm, rewritten for this build: no post-physics inventions/merging.
        public static readonly List<WorldDrop> worldDrops = new List<WorldDrop>();
        public const double DROP_PICK_R = 1.45, DROP_MAGNET_R = DROP_PICK_R * 2.2, DROP_GRAVITY = 18, DROP_DEFAULT_PICK_AFTER = 0.5, DROP_THROW_PICK_AFTER = 2, DROP_LIFE = 300;
        public static void spawnWorldDrop(string key, int count, double x, double y, double z, int dur = 0, double[] velocity = null, double pickAfter = double.NaN)
        {
            if (key == null || count <= 0) return;
            double a = JS.random() * Math.PI * 2; var v = velocity;
            worldDrops.Add(new WorldDrop
            {
                key = key, count = count, dur = dur, x = x, y = y, z = z,
                vx = v != null ? v[0] : Math.Cos(a) * 1.2, vy = v != null ? v[1] : 2.4, vz = v != null ? v[2] : Math.Sin(a) * 1.2,
                age = 0, pickAfter = double.IsNaN(pickAfter) ? DROP_DEFAULT_PICK_AFTER : pickAfter,
            });
        }
        public static void throwWorldDrop(string key, int count, int dur)
        {
            var d = viewDir(); double s = 6;
            spawnWorldDrop(key, count, player.x + d[0] * 0.4, player.y + 1.32 + d[1] * 0.4, player.z + d[2] * 0.4, dur, new[] { d[0] * s, d[1] * s + 1.2, d[2] * s }, DROP_THROW_PICK_AFTER);
        }
        static bool dropCollisionAt(double x, double y, double z) { return pointBlocked(x, y, z); }
        static double dropGroundTop(double x, double y, double z)
        {
            int bx = JS.floor(x), by = JS.floor(y), bz = JS.floor(z), id = getBlock(bx, by, bz);
            var bd = bdef(id);
            if (bd == null || !bd.solid) return by + 1;
            double top = 0;
            foreach (var b in collisionBoxesAt(id, bx, by, bz))
                if (x >= bx + b[0] && x <= bx + b[3] && z >= bz + b[2] && z <= bz + b[5]) top = Math.Max(top, b[4]);
            return by + (top != 0 ? top : 1);
        }
        public static void updateWorldDrops(double dt)
        {
            if (worldDrops.Count == 0) return;
            double px = player.x, py = player.y + 0.8, pz = player.z;
            for (int i = worldDrops.Count - 1; i >= 0; i--)
            {
                var q = worldDrops[i];
                q.age += dt;
                if (q.age > DROP_LIFE || q.y < WORLD_MIN_Y - 8) { worldDrops.RemoveAt(i); continue; }
                q.vy -= DROP_GRAVITY * dt;
                double ny = q.y + q.vy * dt;
                if (q.vy < 0 && dropCollisionAt(q.x, ny - 0.15, q.z))
                {
                    q.y = dropGroundTop(q.x, ny - 0.15, q.z) + 0.15;
                    q.vy = 0;
                    double damp = Math.Max(0, 1 - Math.Min(1, dt * 8));
                    q.vx *= damp; q.vz *= damp;
                }
                else q.y = ny;
                if (isWater(getBlock(q.x, q.y, q.z))) q.vy = Math.Max(q.vy, -0.8);
                double nx = q.x + q.vx * dt;
                if (dropCollisionAt(nx, q.y + 0.2, q.z)) q.vx = 0; else q.x = nx;
                double nz = q.z + q.vz * dt;
                if (dropCollisionAt(q.x, q.y + 0.2, nz)) q.vz = 0; else q.z = nz;
                if (q.age > q.pickAfter && !player.dead)
                {
                    double dx = px - q.x, dy = py - q.y, dz = pz - q.z, l = JS.hypot(dx, dy, dz);
                    if (l < DROP_PICK_R)
                    {
                        int before = q.count, left = addItem(q.key, q.count, q.dur);
                        if (left < before)
                        {
                            sfxPickup();
                            q.count = left;
                            drawHotbar();
                            saveGameSoon();
                            if (left <= 0) { worldDrops.RemoveAt(i); continue; }
                        }
                    }
                    else if (l < DROP_MAGNET_R && l > 0.0001)
                    {
                        q.x += dx / l * dt * 3; q.y += dy / l * dt * 3; q.z += dz / l * dt * 3;
                    }
                }
            }
        }

        // ---------------- drop sprite batches ----------------
        public static readonly FloatList dropBlockV = new FloatList(), dropItemV = new FloatList();
        static readonly List<int> dropBlockI = new List<int>(), dropItemI = new List<int>();
        static void dropRotate(double x, double z, double c, double s, out double rx, out double rz) { rx = x * c - z * s; rz = x * s + z * c; }
        struct DropFace { public double s; public int[][] c; public int t; }
        static readonly DropFace[] DROP_FACES =
        {
            new DropFace { s = 0.72, c = new[] { new[] { -1, 0, -1 }, new[] { -1, 2, -1 }, new[] { -1, 0, 1 }, new[] { -1, 2, 1 } }, t = 0 },
            new DropFace { s = 0.72, c = new[] { new[] { 1, 0, 1 }, new[] { 1, 2, 1 }, new[] { 1, 0, -1 }, new[] { 1, 2, -1 } }, t = 0 },
            new DropFace { s = 1, c = new[] { new[] { -1, 2, 1 }, new[] { 1, 2, 1 }, new[] { -1, 2, -1 }, new[] { 1, 2, -1 } }, t = 1 },
            new DropFace { s = 0.55, c = new[] { new[] { -1, 0, -1 }, new[] { 1, 0, -1 }, new[] { -1, 0, 1 }, new[] { 1, 0, 1 } }, t = 2 },
            new DropFace { s = 0.82, c = new[] { new[] { -1, 0, 1 }, new[] { 1, 0, 1 }, new[] { -1, 2, 1 }, new[] { 1, 2, 1 } }, t = 0 },
            new DropFace { s = 0.82, c = new[] { new[] { 1, 0, -1 }, new[] { -1, 0, -1 }, new[] { 1, 2, -1 }, new[] { -1, 2, -1 } }, t = 0 },
        };
        static double dropLight(WorldDrop q, double sun)
        {
            var L = getWorldLight(q.x, q.y + 0.3, q.z);
            return Math.Max(0.15, Math.Min(1, Math.Max(L[0] * (0.12 + 0.88 * sun), L[1]) * 0.94 + 0.06));
        }
        static readonly double[][] dropPts = { new double[3], new double[3], new double[3], new double[3] };
        static void setPt(int k, double x, double y, double z) { dropPts[k][0] = x; dropPts[k][1] = y; dropPts[k][2] = z; }
        static void addDropBlock(WorldDrop q, double sun)
        {
            int side, top, bottom; bool thin;
            if (isVK(q.key))
            {
                var vd = VirtualByKey(vkey(q.key)); if (vd == null) return;
                side = vd.side; top = vd.top; bottom = vd.bottom; thin = vd.plant || vd.cutout;
            }
            else
            {
                var b = bdef(bid(q.key)); if (b == null) return;
                side = b.side; top = b.top; bottom = b.bottom;
                thin = b.plant || b.cutout || b.special == "torch" || b.special == "ladder" || b.special == "door" || b.special == "doorOpen";
            }
            double bob = Math.Sin(q.age * 2.2) * 0.05, baseY = q.y + bob, a = q.age * 0.9, c = Math.Cos(a), sn = Math.Sin(a), lit = dropLight(q, sun);
            if (thin)
            {
                double d = 0.22, hh = 0.42, p1x, p1z, p2x, p2z;
                dropRotate(-d, 0, c, sn, out p1x, out p1z); dropRotate(d, 0, c, sn, out p2x, out p2z);
                setPt(0, q.x + p1x, baseY + hh, q.z + p1z); setPt(1, q.x + p2x, baseY + hh, q.z + p2z);
                setPt(2, q.x + p1x, baseY, q.z + p1z); setPt(3, q.x + p2x, baseY, q.z + p2z);
                dropPushQuad(dropBlockV, dropBlockI, dropPts, side, lit);
                return;
            }
            double dd = 0.15;
            foreach (var F in DROP_FACES)
            {
                for (int k = 0; k < 4; k++)
                {
                    var P = F.c[k]; double rx, rz; dropRotate(P[0] * dd, P[2] * dd, c, sn, out rx, out rz);
                    setPt(k, q.x + rx, baseY + P[1] * dd, q.z + rz);
                }
                int tile = F.t == 1 ? top : F.t == 2 ? bottom : side;
                dropPushQuad(dropBlockV, dropBlockI, dropPts, tile, lit * F.s);
            }
        }
        static void addDropItem(WorldDrop q, double sun)
        {
            int tile = ItemTile(q.key);
            if (tile < 0) return;
            double bob = Math.Sin(q.age * 2.2) * 0.05, baseY = q.y + bob, a = q.age * 0.9, c = Math.Cos(a), sn = Math.Sin(a), d = 0.22, h = 0.44, lit = dropLight(q, sun), p1x, p1z, p2x, p2z;
            dropRotate(-d, 0, c, sn, out p1x, out p1z); dropRotate(d, 0, c, sn, out p2x, out p2z);
            setPt(0, q.x + p1x, baseY + h, q.z + p1z); setPt(1, q.x + p2x, baseY + h, q.z + p2z);
            setPt(2, q.x + p1x, baseY, q.z + p1z); setPt(3, q.x + p2x, baseY, q.z + p2z);
            dropPushQuad(dropItemV, dropItemI, dropPts, tile, lit);
        }
        /// <summary>CPU part of drawWorldDrops: fills dropBlockV (terrain atlas) and dropItemV (item atlas).</summary>
        public static void buildWorldDropBatches(double fogFar, double sun)
        {
            dropBlockV.Clear(); dropItemV.Clear();
            if (worldDrops.Count == 0) return;
            double max2 = Math.Pow(Math.Min(fogFar, 56) + 4, 2);
            foreach (var q in worldDrops)
            {
                double dx = q.x - player.x, dz = q.z - player.z;
                if (dx * dx + dz * dz > max2) continue;
                if (!aabbVisible(q.x - 0.45, q.y - 0.3, q.z - 0.45, q.x + 0.45, q.y + 0.7, q.z + 0.45)) continue;
                if (isBK(q.key) || isVK(q.key)) addDropBlock(q, sun); else addDropItem(q, sun);
            }
        }
    }
}
