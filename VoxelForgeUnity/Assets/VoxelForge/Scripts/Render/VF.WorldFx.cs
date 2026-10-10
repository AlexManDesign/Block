// Voxel Forge — Unity port. CPU batch builders: sky bodies, TNT/falling/vehicle boxes, arrows, pearls,
// bobber, mining cracks, selection/projectile/fishing lines, clouds and the first-person held item.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class SkyState { public double light, day, sunset; public double[] sky; }

    public static partial class VF
    {
        static readonly SkyState mainSkyScratch = new SkyState { sky = new double[3] };
        /// <summary>Returns a shared instance (main thread, consumed immediately by the renderer).</summary>
        public static SkyState mainSkyState()
        {
            double A = Math.Sin(day * Math.PI * 2), d = Math.Max(0, Math.Min(1, (A + 0.14) / 0.24)), light = 0.22 + 0.78 * d;
            var st = mainSkyScratch; var sky = st.sky;
            sky[0] = 0.02 + (0.47 - 0.02) * d; sky[1] = 0.03 + (0.71 - 0.03) * d; sky[2] = 0.08 + (1 - 0.08) * d;
            double sunset = Math.Max(0, 1 - Math.Abs(A) * 4) * d;
            if (sunset > 0)
            {
                sky[0] += (0.93 - sky[0]) * sunset * 0.55; sky[1] += (0.49 - sky[1]) * sunset * 0.55; sky[2] += (0.27 - sky[2]) * sunset * 0.55;
            }
            st.light = light; st.day = d; st.sunset = sunset;
            return st;
        }
        public static double mainRenderFov = 70, mainCloudDriftX = 0;
        public static void updateMainRenderFov(double dt)
        {
            double target = player.sprinting && !pauseOpen ? 78 : 70;
            mainRenderFov += (target - mainRenderFov) * Math.Min(1, dt * 9);
        }
        static readonly double[][] fxPts = { new double[3], new double[3], new double[3], new double[3] };
        static double[][] P4(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz, double dx, double dy, double dz)
        {
            var p = fxPts;
            p[0][0] = ax; p[0][1] = ay; p[0][2] = az; p[1][0] = bx; p[1][1] = by; p[1][2] = bz;
            p[2][0] = cx; p[2][1] = cy; p[2][2] = cz; p[3][0] = dx; p[3][1] = dy; p[3][2] = dz;
            return p;
        }
        static readonly List<int> fxIdx = new List<int>();
        public static void renderPushQuad(FloatList v, double[][] pts, int tile, double shade = 1) { dropPushQuad(v, fxIdx, pts, tile, shade); }
        public static void renderBox(FloatList v, double x0, double y0, double z0, double x1, double y1, double z1, int side, int top, int bottom, double shade = 1)
        {
            renderPushQuad(v, P4(x0, y0, z0, x0, y1, z0, x0, y0, z1, x0, y1, z1), side, shade * 0.72);
            renderPushQuad(v, P4(x1, y0, z1, x1, y1, z1, x1, y0, z0, x1, y1, z0), side, shade * 0.72);
            renderPushQuad(v, P4(x0, y1, z1, x1, y1, z1, x0, y1, z0, x1, y1, z0), top, shade);
            renderPushQuad(v, P4(x0, y0, z0, x1, y0, z0, x0, y0, z1, x1, y0, z1), bottom, shade * 0.55);
            renderPushQuad(v, P4(x0, y0, z1, x1, y0, z1, x0, y1, z1, x1, y1, z1), side, shade * 0.82);
            renderPushQuad(v, P4(x1, y0, z0, x0, y0, z0, x1, y1, z0, x0, y1, z0), side, shade * 0.82);
        }
        // per-frame scratch (main thread): 8 transformed corners + a 4-ref quad view into them
        static readonly double[][] fxCorners = { new double[3], new double[3], new double[3], new double[3], new double[3], new double[3], new double[3], new double[3] };
        static readonly double[][] fxQuad = new double[4][];
        static void renderVehiclePoint(Vehicle v, double c, double sn, double x, double y, double z, double[] o)
        {
            o[0] = v.x + x * c + z * sn; o[1] = v.y + y; o[2] = v.z - x * sn + z * c;
        }
        static double[][] QC(int a, int b, int c, int d) { var q = fxQuad; var p = fxCorners; q[0] = p[a]; q[1] = p[b]; q[2] = p[c]; q[3] = p[d]; return q; }
        static void renderVehicleBox(FloatList outp, Vehicle v, double x0, double y0, double z0, double x1, double y1, double z1, int tile, double shade = 1)
        {
            // corner k = (x1?4:0)|(y1?2:0)|(z1?1:0)
            double c = Math.Cos(v.yaw), sn = Math.Sin(v.yaw);
            for (int k = 0; k < 8; k++) renderVehiclePoint(v, c, sn, (k & 4) != 0 ? x1 : x0, (k & 2) != 0 ? y1 : y0, (k & 1) != 0 ? z1 : z0, fxCorners[k]);
            renderPushQuad(outp, QC(0, 2, 1, 3), tile, shade * 0.72);
            renderPushQuad(outp, QC(5, 7, 4, 6), tile, shade * 0.72);
            renderPushQuad(outp, QC(3, 7, 2, 6), tile, shade);
            renderPushQuad(outp, QC(0, 4, 1, 5), tile, shade * 0.55);
            renderPushQuad(outp, QC(1, 5, 3, 7), tile, shade * 0.82);
            renderPushQuad(outp, QC(4, 0, 6, 2), tile, shade * 0.82);
        }
        static readonly double[][] PRISM_F = { new double[] { 4, 5, 6, 7, 0.8 }, new double[] { 0, 1, 2, 3, 0.6 }, new double[] { 2, 3, 6, 7, 0.95 }, new double[] { 0, 1, 4, 5, 0.7 }, new double[] { 1, 3, 5, 7, 0.75 }, new double[] { 0, 2, 4, 6, 0.7 } };
        static void renderVehiclePrismMain(FloatList outp, Vehicle v, double cx, double cy, double cz, double ax, double ay, double az, double bx, double by, double bz, double dx, double dy, double dz, int tile)
        {
            double c = Math.Cos(v.yaw), sn = Math.Sin(v.yaw);
            for (int k = 0; k < 8; k++)
            {
                int sa = (k & 4) != 0 ? 1 : -1, sb = (k & 2) != 0 ? 1 : -1, sd = (k & 1) != 0 ? 1 : -1;
                renderVehiclePoint(v, c, sn, cx + sa * ax + sb * bx + sd * dx, cy + sa * ay + sb * by + sd * dy, cz + sa * az + sb * bz + sd * dz, fxCorners[k]);
            }
            foreach (var q in PRISM_F) renderPushQuad(outp, QC((int)q[0], (int)q[1], (int)q[2], (int)q[3]), tile, q[4]);
        }
        static void renderBoatOarMain(FloatList outp, Vehicle v, int side, double amp, double phase, int tile)
        {
            double c00 = side * 0.56, c01 = 0.52, c02 = -0.15;
            double rot = Math.Sin(phase) * amp * 0.55 * side, fx = side * 0.9, fy = -0.3, fz = -0.3, cr = Math.Cos(rot), sr = Math.Sin(rot);
            double dx = fx * cr - fz * sr, dy = fy, dz = fx * sr + fz * cr, L = JS.hypot(dx, dy, dz); if (L == 0) L = 1;
            dx /= L; dy /= L; dz /= L;
            double bx = -dz, by = 0, bz = dx, Bn = JS.hypot(bx, bz); if (Bn == 0) Bn = 1;
            bx /= Bn; bz /= Bn;
            double ix = dy * bz - dz * by, iy = dz * bx - dx * bz, iz = dx * by - dy * bx, shaft = 0.8, blade = 0.32, k1 = shaft / 2, k2 = blade / 2, e = shaft + blade / 2;
            renderVehiclePrismMain(outp, v, c00 + dx * shaft / 2, c01 + dy * shaft / 2, c02 + dz * shaft / 2, dx * k1, dy * k1, dz * k1, bx * 0.03, by * 0.03, bz * 0.03, ix * 0.03, iy * 0.03, iz * 0.03, tile);
            renderVehiclePrismMain(outp, v, c00 + dx * e, c01 + dy * e, c02 + dz * e, dx * k2, dy * k2, dz * k2, bx * 0.13, by * 0.13, bz * 0.13, ix * 0.025, iy * 0.025, iz * 0.025, tile);
        }
        public static readonly FloatList mainActiveTerrainV = new FloatList(), mainActiveItemV = new FloatList(), mainSkyV = new FloatList(), mainCrackV = new FloatList();
        static void addMainArrow(FloatList outp, Projectile p)
        {
            int tile = ItemTile("arrow");
            if (tile < 0) return;
            double I0, I1, I2;
            if (p.stuck && p.dir != null) { I0 = p.dir[0]; I1 = p.dir[1]; I2 = p.dir[2]; } else { I0 = p.vx; I1 = p.vy; I2 = p.vz; }
            double L = JS.hypot(I0, I1, I2); if (L == 0) L = 1;
            double T = I0 / L, R = I1 / L, J = I2 / L, O = J, G = -T, N = JS.hypot(O, G);
            if (N < 1e-4) { O = 1; G = 0; N = 1; }
            O /= N; G /= N;
            double Y = -G * R, Qv = G * T - O * J, h = 0.22;
            for (int bi = 0; bi < 2; bi++)
            {
                double a0 = bi == 0 ? Y : O, a1 = bi == 0 ? Qv : 0, a2 = bi == 0 ? O * R : G;
                var pts = fxPts; int n = 0;
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 2; col++)
                    {
                        double fa = (col - 0.5) * 2 * h, wa = (0.5 - row) * 2 * h, ta = (fa + wa) * 0.7071, aa = (wa - fa) * 0.7071;
                        var q = pts[n++]; q[0] = p.x + ta * T + aa * a0; q[1] = p.y + ta * R + aa * a1; q[2] = p.z + ta * J + aa * a2;
                    }
                renderPushQuad(outp, pts, tile, 1);
            }
        }
        static void addMainPearl(FloatList outp, Pearl p)
        {
            int tile = ItemTile("ender_pearl");
            if (tile < 0) return;
            double n = 0.12, c = Math.Cos(p.spin) * n, f = Math.Sin(p.spin) * n;
            renderPushQuad(outp, P4(p.x - c, p.y + n, p.z - f, p.x + c, p.y + n, p.z + f, p.x - c, p.y - n, p.z - f, p.x + c, p.y - n, p.z + f), tile, 1);
        }
        static void addMainBobber(FloatList outp, Bobber q)
        {
            double u = 0.09; int t = RENDER_TILE.bobber;
            renderPushQuad(outp, P4(q.x - u, q.y + 2 * u, q.z, q.x + u, q.y + 2 * u, q.z, q.x - u, q.y, q.z, q.x + u, q.y, q.z), t, 1);
            renderPushQuad(outp, P4(q.x, q.y + 2 * u, q.z - u, q.x, q.y + 2 * u, q.z + u, q.x, q.y, q.z - u, q.x, q.y, q.z + u), t, 1);
        }
        /// <summary>CPU part of drawMainActiveEntities.</summary>
        public static void buildMainActiveEntities()
        {
            mainActiveTerrainV.Clear(); mainActiveItemV.Clear();
            foreach (var q in primedTNT) { var b = blocks[B.TNT]; renderBox(mainActiveTerrainV, q.x - 0.5, q.y - 0.5, q.z - 0.5, q.x + 0.5, q.y + 0.5, q.z + 0.5, b.side, b.top, b.bottom, 1); }
            foreach (var q in fallingBlocks)
            {
                var vd = q.meta != null && q.meta.Bool("v") ? VirtualByKey(q.meta.Str("v")) : null;
                if (vd != null) renderBox(mainActiveTerrainV, q.x, q.y, q.z, q.x + 1, q.y + 1, q.z + 1, vd.side, vd.top, vd.bottom, 1);
                else { var b = bdef(q.id); if (b != null) renderBox(mainActiveTerrainV, q.x, q.y, q.z, q.x + 1, q.y + 1, q.z + 1, b.side, b.top, b.bottom, 1); }
            }
            foreach (var v in vehicles)
            {
                if (JS.hypot(v.x - player.x, v.z - player.z) > 64) continue;
                if (v.kind == "minecart")
                {
                    int t = RENDER_TILE.iron;
                    renderVehicleBox(mainActiveTerrainV, v, -0.45, 0, -0.62, 0.45, 0.12, 0.62, t);
                    renderVehicleBox(mainActiveTerrainV, v, -0.45, 0.12, 0.52, 0.45, 0.5, 0.62, t);
                    renderVehicleBox(mainActiveTerrainV, v, -0.45, 0.12, -0.62, 0.45, 0.5, -0.52, t);
                    renderVehicleBox(mainActiveTerrainV, v, 0.35, 0.12, -0.62, 0.45, 0.5, 0.62, t);
                    renderVehicleBox(mainActiveTerrainV, v, -0.45, 0.12, -0.62, -0.35, 0.5, 0.62, t);
                }
                else
                {
                    int t = blocks[B.PLANKS].side;
                    renderVehicleBox(mainActiveTerrainV, v, -0.5625, 0, -0.875, 0.5625, 0.1875, 0.875, t);
                    renderVehicleBox(mainActiveTerrainV, v, 0.4375, 0.1875, -0.875, 0.5625, 0.5625, 0.875, t);
                    renderVehicleBox(mainActiveTerrainV, v, -0.5625, 0.1875, -0.875, -0.4375, 0.5625, 0.875, t);
                    renderVehicleBox(mainActiveTerrainV, v, -0.4375, 0.1875, 0.75, 0.4375, 0.5625, 0.875, t);
                    renderVehicleBox(mainActiveTerrainV, v, -0.4375, 0.1875, -0.875, 0.4375, 0.5625, -0.75, t);
                    renderBoatOarMain(mainActiveTerrainV, v, -1, v.ampL, v.oarPhase, t);
                    renderBoatOarMain(mainActiveTerrainV, v, 1, v.ampR, v.oarPhase, t);
                }
            }
            if (fishingBobber != null) addMainBobber(mainActiveTerrainV, fishingBobber);
            foreach (var p in pearlProjectiles) addMainPearl(mainActiveItemV, p);
            foreach (var p in projectiles) addMainArrow(mainActiveItemV, p);
        }
        /// <summary>CPU part of drawMainSkyBodies (drawn without depth, no fog).</summary>
        public static void buildMainSkyBodies(double ex, double ey, double ez)
        {
            mainSkyV.Clear();
            double P = day * Math.PI * 2, sx = Math.Cos(P), sy = Math.Sin(P), sz = 0.25, L = JS.hypot(sx, sy, sz); if (L == 0) L = 1;
            sx /= L; sy /= L; sz /= L;
            skyBodyAdd(ex, ey, ez, sx, sy, sz, 16, RENDER_TILE.sun);
            skyBodyAdd(ex, ey, ez, -sx, -sy, sz, 11, RENDER_TILE.moon);
        }
        static void skyBodyAdd(double ex, double ey, double ez, double dx, double dy, double dz, double size, int tile)
        {
            double ux = dy * 0 - dz * 1, uy = dz * 0 - dx * 0, uz = dx * 1 - dy * 0, l = JS.hypot(ux, uy, uz); if (l == 0) l = 1;
            ux /= l; uy /= l; uz /= l;
            double vx = uy * dz - uz * dy, vy = uz * dx - ux * dz, vz = ux * dy - uy * dx, cx = ex + dx * 220, cy = ey + dy * 220, cz = ez + dz * 220;
            renderPushQuad(mainSkyV, P4(cx + (-ux - vx) * size, cy + (-uy - vy) * size, cz + (-uz - vz) * size, cx + (ux - vx) * size, cy + (uy - vy) * size, cz + (uz - vz) * size,
                cx + (-ux + vx) * size, cy + (-uy + vy) * size, cz + (-uz + vz) * size, cx + (ux + vx) * size, cy + (uy + vy) * size, cz + (uz + vz) * size), tile, 1);
        }
        public static bool buildMainMiningCracks()
        {
            mainCrackV.Clear();
            if (player.creative || string.IsNullOrEmpty(miningTarget) || miningProgress <= 0 || !mouseL) return false;
            var a = miningTarget.Split(',');
            if (a.Length != 3) return false;
            int x, y, z;
            if (!int.TryParse(a[0], out x) || !int.TryParse(a[1], out y) || !int.TryParse(a[2], out z)) return false;
            int stage = Math.Min(9, (int)Math.Floor(miningProgress * 10)); double e = 0.004; int t = RENDER_TILE.cracks[stage];
            renderBox(mainCrackV, x - e, y - e, z - e, x + 1 + e, y + 1 + e, z + 1 + e, t, t, t, 1);
            return true;
        }

        // ---------------- lines ----------------
        public static readonly FloatList selectionLineV = new FloatList(), projLineV = new FloatList(), fishLineV = new FloatList(), primedLineV = new FloatList();
        static void lineSeg(FloatList o, double ax, double ay, double az, double bx, double by, double bz)
        {
            o.Ensure(6); o.Add((float)ax); o.Add((float)ay); o.Add((float)az); o.Add((float)bx); o.Add((float)by); o.Add((float)bz);
        }
        static readonly int[][] BOX_EDGES = { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 0, 4 }, new[] { 1, 3 }, new[] { 1, 5 }, new[] { 2, 3 }, new[] { 2, 6 }, new[] { 3, 7 }, new[] { 4, 5 }, new[] { 4, 6 }, new[] { 5, 7 }, new[] { 6, 7 } };
        static void appendSelectionBox(FloatList o, double x, double y, double z, double[] b)
        {
            double x0 = x + b[0] - 0.002, y0 = y + b[1] - 0.002, z0 = z + b[2] - 0.002, x1 = x + b[3] + 0.002, y1 = y + b[4] + 0.002, z1 = z + b[5] + 0.002;
            // corner k = (z1?4:0)|(y1?2:0)|(x1?1:0)
            var p = fxCorners;
            for (int k = 0; k < 8; k++) { var q = p[k]; q[0] = (k & 1) != 0 ? x1 : x0; q[1] = (k & 2) != 0 ? y1 : y0; q[2] = (k & 4) != 0 ? z1 : z0; }
            foreach (var e in BOX_EDGES) lineSeg(o, p[e[0]][0], p[e[0]][1], p[e[0]][2], p[e[1]][0], p[e[1]][1], p[e[1]][2]);
        }
        public static void buildSelectionOutline(Hit hit)
        {
            selectionLineV.Clear();
            if (hit == null) return;
            var boxes = hit.boxes ?? selectionBoxesAt(hit.id, hit.x, hit.y, hit.z);
            foreach (var b in boxes) appendSelectionBox(selectionLineV, hit.x, hit.y, hit.z, b);
        }
        static readonly double[] PRIMED_BOX = { -0.05, -0.05, -0.05, 1.05, 1.05, 1.05 };
        public static void buildPrimedOutlines()
        {
            primedLineV.Clear();
            foreach (var q in primedTNT) appendSelectionBox(primedLineV, q.x - 0.5, q.y - 0.5, q.z - 0.5, PRIMED_BOX);
        }
        public static void buildProjectileLines()
        {
            projLineV.Clear();
            foreach (var p in projectiles)
            {
                double l = JS.hypot(p.vx, p.vy, p.vz); if (l == 0) l = 1;
                double dx = p.stuck ? (p.dir != null && p.dir[0] != 0 ? p.dir[0] : 0) : p.vx / l, dy = p.stuck ? (p.dir != null && p.dir[1] != 0 ? p.dir[1] : -1) : p.vy / l, dz = p.stuck ? (p.dir != null && p.dir[2] != 0 ? p.dir[2] : 0) : p.vz / l;
                double x2 = p.x - dx * 0.45, y2 = p.y - dy * 0.45, z2 = p.z - dz * 0.45;
                if (!aabbVisible(Math.Min(p.x, x2) - 0.12, Math.Min(p.y, y2) - 0.12, Math.Min(p.z, z2) - 0.12, Math.Max(p.x, x2) + 0.12, Math.Max(p.y, y2) + 0.12, Math.Max(p.z, z2) + 0.12)) continue;
                lineSeg(projLineV, p.x, p.y, p.z, x2, y2, z2);
            }
        }
        public static void buildFishingLine()
        {
            fishLineV.Clear();
            var fb = fishingBobber;
            if (fb == null) return;
            double sx = player.x, sy = player.y + 1.48, sz = player.z, ex = fb.x, ey = fb.y + 0.18, ez = fb.z, dx = ex - sx, dy = ey - sy, dz = ez - sz, dist = JS.hypot(dx, dy, dz);
            double sag = Math.Min(0.45, dist * 0.14) * (fb.state == "fly" ? 0.25 : 1);
            double px = sx, py = sy, pz = sz; // point i = 0
            for (int i = 1; i <= 10; i++)
            {
                double t = i / 10.0, qx = sx + dx * t, qy = sy + dy * t - sag * 4 * t * (1 - t), qz = sz + dz * t;
                lineSeg(fishLineV, px, py, pz, qx, qy, qz); px = qx; py = qy; pz = qz;
            }
        }

        // ---------------- clouds ----------------
        static double renderHash2(int A, int e)
        {
            int r = unchecked(A * 374761393 + e * 668265263 + 527595518);
            r = r ^ JS.ushr(r, 13);
            r = JS.imul(r, 1274126177);
            return (uint)(r ^ JS.ushr(r, 16)) / 4294967296.0;
        }
        public static float[] cloudVerts; public static int[] cloudIdx;
        public static void initMainClouds()
        {
            const int N = 24;
            var g = new bool[N, N];
            for (int x = 0; x < N; x++) for (int z = 0; z < N; z++) g[x, z] = renderHash2(x * 7 + 3, z * 11 + 5) < 0.3;
            for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                    if (!g[x, z])
                    {
                        int n = (g[(x + 1) % N, z] ? 1 : 0) + (g[(x + N - 1) % N, z] ? 1 : 0) + (g[x, (z + 1) % N] ? 1 : 0) + (g[x, (z + N - 1) % N] ? 1 : 0);
                        if (n >= 3) g[x, z] = true;
                    }
            var V = new List<float>(); var I = new List<int>();
            for (int x = -38; x < 39; x++)
                for (int z = -38; z < 39; z++)
                {
                    int px = ((x % N) + N) % N, pz = ((z % N) + N) % N;
                    if (!g[px, pz]) continue;
                    float X = x * 12, Z = z * 12; int b = V.Count / 3;
                    V.AddRange(new[] { X, 192f, Z, X + 12, 192f, Z, X, 192f, Z + 12, X + 12, 192f, Z + 12 });
                    I.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 });
                }
            cloudVerts = V.ToArray(); cloudIdx = I.ToArray();
        }

        // ---------------- first-person held item ----------------
        static double mainHandWalk = 0, mainHandEquip = 0, mainOffEquip = 0, mainHandSwingT = 0;
        static string mainHandPrevKey = null, mainOffPrevKey = null; static bool mainHandPrevSet = false, mainOffPrevSet = false;
        public static void pulseMainHand() { mainHandSwingT = 1; }
        sealed class HeldBounds { public double[] mn, mx; }
        public sealed class HeldGeo { public FloatList v = new FloatList(); public bool terrain; public bool flat; public float[] matrix = new float[16]; }
        public static readonly HeldGeo heldMain = new HeldGeo(), heldOff = new HeldGeo();
        // Allocation-free matrix chain: hCur is the running product, each op fills hOp and right-multiplies (hCur = hCur * op).
        static float[] hCur = new float[16], hTmp = new float[16];
        static readonly float[] hOp = new float[16];
        static float[] hId(float[] m) { Array.Clear(m, 0, 16); m[0] = m[5] = m[10] = m[15] = 1; return m; }
        static void hMulInto(float[] o, float[] a, float[] b)
        {
            for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) o[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
        }
        static void hApply(float[] op) { hMulInto(hTmp, hCur, op); var t = hCur; hCur = hTmp; hTmp = t; }
        static float[] hT(double x, double y, double z) { var m = hId(hOp); m[12] = (float)x; m[13] = (float)y; m[14] = (float)z; return m; }
        static float[] hS(double q) { var m = hId(hOp); m[0] = m[5] = m[10] = (float)q; return m; }
        static float[] hMX() { var m = hId(hOp); m[0] = -1; return m; }
        static float[] hRX(double a) { var m = hId(hOp); float c = (float)Math.Cos(a), q = (float)Math.Sin(a); m[5] = c; m[6] = q; m[9] = -q; m[10] = c; return m; }
        static float[] hRY(double a) { var m = hId(hOp); float c = (float)Math.Cos(a), q = (float)Math.Sin(a); m[0] = c; m[2] = -q; m[8] = q; m[10] = c; return m; }
        static float[] hRZ(double a) { var m = hId(hOp); float c = (float)Math.Cos(a), q = (float)Math.Sin(a); m[0] = c; m[1] = q; m[4] = -q; m[5] = c; return m; }
        static void hStart(float[] op) { Array.Copy(op, hCur, 16); }
        static readonly HashSet<string> HELD_FLAT_SHAPES = new HashSet<string> { "torch", "ladder", "rail", "vine", "lichen", "door", "tallplant", "cross" };
        static void heldDefInfo(string key, out bool exists, out string sp, out bool plantish, out int side, out int top, out int bottom, out int partBottom)
        {
            exists = false; sp = null; plantish = false; side = top = bottom = partBottom = -1;
            if (isVK(key))
            {
                var d = VirtualByKey(vkey(key)); if (d == null) return;
                exists = true; sp = d.shape; plantish = d.plant || d.waterPlant || d.cutout; side = d.side; top = d.top; bottom = d.bottom; partBottom = d.hasPartBottom ? d.partBottom : -1;
            }
            else
            {
                var b = bdef(bid(key)); if (b == null) return;
                exists = true; sp = b.special; plantish = b.plant || b.waterPlant || b.cutout; side = b.side; top = b.top; bottom = b.bottom;
            }
        }
        // constant bounds, shared read-only instances
        static readonly HeldBounds HB_HAND = new HeldBounds { mn = new[] { -0.14, -1, -0.14 }, mx = new[] { 0.14, 0.05, 0.14 } },
            HB_FLAT = new HeldBounds { mn = new[] { -0.5, -0.5, -0.03 }, mx = new[] { 0.5, 0.5, 0.03 } },
            HB_CUBE = new HeldBounds { mn = new double[] { 0, 0, 0 }, mx = new double[] { 1, 1, 1 } },
            HB_HALF = new HeldBounds { mn = new double[] { 0, 0, 0 }, mx = new[] { 1, 0.5, 1 } };
        static HeldBounds heldBoundsForKey(string key)
        {
            if (key == null) return HB_HAND;
            if (!isBK(key) && !isVK(key)) return HB_FLAT;
            bool exists, plantish; string sp; int s, t, b, pb;
            heldDefInfo(key, out exists, out sp, out plantish, out s, out t, out b, out pb);
            if (!exists) return HB_CUBE;
            if (sp == "slab" || sp == "carpet" || sp == "plate" || sp == "rail") return HB_HALF;
            if (plantish || HELD_FLAT_SHAPES.Contains(sp ?? "")) return HB_FLAT;
            return HB_CUBE;
        }
        static bool heldBuild(string key, HeldGeo g)
        {
            g.v.Clear(); g.flat = false; g.terrain = true;
            if (key == null) { renderBox(g.v, -0.14, -1, -0.14, 0.14, 0.05, 0.14, RENDER_TILE.hand, RENDER_TILE.hand, RENDER_TILE.hand, 1); return true; }
            if (isBK(key) || isVK(key))
            {
                bool exists, plantish; string sp; int side, top, bottom, partBottom;
                heldDefInfo(key, out exists, out sp, out plantish, out side, out top, out bottom, out partBottom);
                if (!exists) return false;
                bool flat = plantish || HELD_FLAT_SHAPES.Contains(sp ?? "");
                if (flat)
                {
                    int t = partBottom >= 0 ? partBottom : side; double d = 0.5;
                    renderPushQuad(g.v, P4(-d, d, 0, d, d, 0, -d, -d, 0, d, -d, 0), t, 1);
                    renderPushQuad(g.v, P4(0, d, -d, 0, d, d, 0, -d, -d, 0, -d, d), t, 0.82);
                }
                else if (sp == "slab" || sp == "carpet" || sp == "plate") renderBox(g.v, 0, 0, 0, 1, 0.5, 1, side, top, bottom, 1);
                else if (sp == "stairs") { renderBox(g.v, 0, 0, 0, 1, 0.5, 1, side, top, bottom, 1); renderBox(g.v, 0, 0.5, 0.5, 1, 1, 1, side, top, bottom, 1); }
                else renderBox(g.v, 0, 0, 0, 1, 1, 1, side, top, bottom, 1);
                g.flat = flat;
                return true;
            }
            int it = ItemTile(key);
            if (it < 0) return false;
            double dd = 0.5;
            renderPushQuad(g.v, P4(-dd, dd, 0, dd, dd, 0, -dd, -dd, 0, dd, -dd, 0), it, 1);
            g.terrain = false; g.flat = true;
            return true;
        }
        static double heldEyeShade()
        {
            double y = player.y + 1.62;
            int p = getPackedLightWorld(JS.floor(player.x), JS.floor(y), JS.floor(player.z)), sky = (p >> 4) & 15, blk = Math.Max(p & 15, handLightLevelNow());
            int i = (sky * 16 + Math.Min(15, blk)) * 4;
            return Math.Max(0.28, Math.Max(mainLightmapPixels[i] / 255.0, Math.Max(mainLightmapPixels[i + 1] / 255.0, mainLightmapPixels[i + 2] / 255.0)));
        }
        static void heldApplyShade(FloatList v, double k) { for (int i = 6; i < v.n; i += 7) v.a[i] = (float)(v.a[i] * k); }
        /// <summary>Builds held geometry + model matrix (camera space, -Z forward). Returns false when nothing is drawn.</summary>
        static bool heldDrawOne(string key, bool offhand, double bobX, double bobY, double swing, double equip, double charge, HeldGeo g)
        {
            if (!heldBuild(key, g)) return false;
            var Bb = heldBoundsForKey(key);
            double d = (Bb.mn[0] + Bb.mx[0]) * 0.5, h = (Bb.mn[1] + Bb.mx[1]) * 0.5, I = (Bb.mn[2] + Bb.mx[2]) * 0.5;
            double D = Math.Max(Bb.mx[0] - Bb.mn[0], Math.Max(Bb.mx[1] - Bb.mn[1], Bb.mx[2] - Bb.mn[2])); if (D == 0) D = 1;
            double l = offhand ? -1 : 1, P = -swing * 0.38 - equip * 0.65, c = -swing * 0.16 * l;
            if (key == null)
            {
                hStart(hT(l * (0.5 + bobX * 0.5) + c, -0.35 + bobY * 0.5 + P, -0.7));
                if (offhand) hApply(hMX());
                hApply(hRZ(-0.4)); hApply(hRX(0.15 - swing * 1.1)); hApply(hRY(0.4));
            }
            else if (key == "bow")
            {
                double R = charge, J = R >= 1 ? Math.Sin(JS.now() * 0.035) * 0.005 : 0, O = R >= 1 ? Math.Cos(JS.now() * 0.029) * 0.005 : 0;
                hStart(hT(l * (0.4 - R * 0.08 + bobX * 0.5) + c + J, -0.34 + bobY * 0.5 + P + O, -0.7 + R * 0.06 - swing * 0.06));
                if (offhand) hApply(hMX());
                hApply(hRX(0.08 - swing * 1.1)); hApply(hRY(1.25)); hApply(hS(0.95 / D)); hApply(hRZ(-2.356)); hApply(hT(-d, -h, -I));
            }
            else if (g.flat)
            {
                hStart(hT(l * (0.5 + bobX * 0.5) + c, -0.42 + bobY * 0.5 + P, -0.78 - swing * 0.06));
                if (offhand) hApply(hMX());
                hApply(hRX(-swing * 1.15)); hApply(hRY(-0.55)); hApply(hRZ(0.35)); hApply(hS(0.62 / D));
                if (key == "fishing_rod") hApply(hMX());
                hApply(hT(-d, -h + D * 0.12, -I));
            }
            else
            {
                hStart(hT(l * (0.58 + bobX * 0.5) + c, -0.44 + bobY * 0.5 + P, -0.85 - swing * 0.06));
                if (offhand) hApply(hMX());
                hApply(hRX(-swing * 1.15)); hApply(hRY(0.78)); hApply(hS(0.4 / D)); hApply(hT(-d, -h, -I));
            }
            heldApplyShade(g.v, heldEyeShade());
            Array.Copy(hCur, g.matrix, 16);
            return true;
        }
        public static bool heldMainVisible, heldOffVisible;
        /// <summary>CPU part of drawMainFirstPersonHeld.</summary>
        public static void buildFirstPersonHeld(double dt)
        {
            heldMainVisible = heldOffVisible = false;
            if (player.camMode != 0 || player.dead || uiOpen || pauseOpen) return;
            double speed = JS.hypot(player.vx, player.vz);
            if (speed > 0.5 && (player.onGround || player.inWater)) mainHandWalk += dt * speed * 1.7;
            double bobX = Math.Cos(mainHandWalk) * 0.04, bobY = Math.Abs(Math.Sin(mainHandWalk)) * -0.04;
            var ms = selectedStack(); string main = ms != null ? ms.key : null; string off = offhand[0] != null ? offhand[0].key : null;
            if (!mainHandPrevSet || main != mainHandPrevKey) { mainHandEquip = !mainHandPrevSet || mainHandPrevKey == null ? 0 : 1; mainHandPrevKey = main; mainHandPrevSet = true; }
            if (!mainOffPrevSet || off != mainOffPrevKey) { mainOffEquip = !mainOffPrevSet || mainOffPrevKey == null ? 0 : 1; mainOffPrevKey = off; mainOffPrevSet = true; }
            mainHandEquip = Math.Max(0, mainHandEquip - dt * 5);
            mainOffEquip = Math.Max(0, mainOffEquip - dt * 5);
            mainHandSwingT = Math.Max(0, mainHandSwingT - dt * 4.5);
            double swing = Math.Sin(mainHandSwingT * Math.PI);
            if (mouseL && miningProgress > 0) swing = Math.Max(swing, Math.Sin(Math.Min(1, miningProgress) * Math.PI));
            double charge = bowCharging ? bowCharge : 0;
            heldMainVisible = heldDrawOne(main, false, bobX, bobY, swing, mainHandEquip, charge, heldMain);
            if (off != null) heldOffVisible = heldDrawOne(off, true, bobX, bobY, 0, mainOffEquip, 0, heldOff);
        }

        // ---------------- third-person camera ----------------
        const double MAIN_CAM_MAX = 4, MAIN_CAM_STEP = 0.12, MAIN_CAM_PAD = 0.22;
        public static double mainCameraDistance(double[] eye, double[] dir)
        {
            for (double r = MAIN_CAM_STEP; r <= MAIN_CAM_MAX + 1e-6; r += MAIN_CAM_STEP)
            {
                int x = JS.floor(eye[0] + dir[0] * r), y = JS.floor(eye[1] + dir[1] * r), z = JS.floor(eye[2] + dir[2] * r), id = getBlock(x, y, z);
                var b = bdef(id);
                if (b != null && b.solid && !isWater(id) && !isLava(id)) return Math.Max(0, r - MAIN_CAM_STEP - MAIN_CAM_PAD);
            }
            return MAIN_CAM_MAX;
        }
        public static void cycleMainCamera()
        {
            player.camMode = (player.camMode + 1) % 3;
            toast(player.camMode == 0 ? "Камера: от первого лица" : player.camMode == 1 ? "Камера: сзади" : "Камера: спереди");
        }
    }
}
