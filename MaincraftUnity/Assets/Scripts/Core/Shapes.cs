// Port of shapes.js: block collision / selection boxes (main thread). Boxes are [x0,y0,z0,x1,y1,z1] in block units.
using System;
using System.Collections.Generic;
using System.Text;
using static Maincraft.Blocks;

namespace Maincraft
{
    /// <summary>Read access to blocks (the World, or anything that looks like it).</summary>
    public interface IBlockAccess
    {
        int GetBlock(int x, int y, int z);
        int GetMeta(int x, int y, int z);
    }

    public sealed class RayHit
    {
        public int x, y, z, face, id, meta;
        public double t, px, py, pz;
    }

    public static class Shapes
    {
        public static readonly int[] DIRX_W = { 0, -1, 0, 1 }, DIRZ_W = { 1, 0, -1, 0 };

        public static double[] RotBoxU(double[] b, int turns)
        {
            double x0 = b[0], z0 = b[2], x1 = b[3], z1 = b[5];
            for (int t = 0; t < turns; t++) { double nx0 = 1 - z1, nx1 = 1 - z0; z0 = x0; z1 = x1; x0 = nx0; x1 = nx1; }
            return new[] { x0, b[1], z0, x1, b[4], z1 };
        }

        public static readonly double[][] FULL_BOX = { new double[] { 0, 0, 0, 1, 1, 1 } };

        // Amethyst buds and cluster (AmethystClusterBlock(height, offset)): a box growing out of the face
        // the block is attached to (meta = face, mesher order +x -x +y -y +z -z).
        public static double[] CrystalBox(int id, int m)
        {
            int[] sz = id == B.AMETHYST_CLUSTER ? new[] { 7, 3 } : id == B.LARGE_AMETHYST_BUD ? new[] { 5, 3 } : id == B.MEDIUM_AMETHYST_BUD ? new[] { 4, 3 } : new[] { 3, 4 };
            double h = sz[0] / 16.0, o = sz[1] / 16.0, a = o, b = 1 - o;
            switch (m % 6)
            {
                case 0: return new[] { 0, a, a, h, b, b };
                case 1: return new[] { 1 - h, a, a, 1, b, b };
                case 2: return new[] { a, 0, a, b, h, b };
                case 3: return new[] { a, 1 - h, a, b, 1, b };
                case 4: return new[] { a, a, 0, b, b, h };
                default: return new[] { a, a, 1 - h, b, b, 1 };
            }
        }

        static double[][] One(params double[] b) => new[] { b };

        // selection boxes (what the outline / raycast uses)
        public static double[][] BlockBoxes(IBlockAccess world, int id, int m, int x, int y, int z)
        {
            int sh = SHAPE[id];
            switch (sh)
            {
                case SH.CUBE: return FULL_BOX;
                case SH.SLAB: return (m & 1) != 0 ? One(0, 0.5, 0, 1, 1, 1) : One(0, 0, 0, 1, 0.5, 1);
                case SH.STAIRS:
                {
                    int top = (m >> 2) & 1, f = m & 3;
                    var s = top != 0 ? new double[] { 0, 0.5, 0, 1, 1, 1 } : new double[] { 0, 0, 0, 1, 0.5, 1 };
                    var u = RotBoxU(top != 0 ? new double[] { 0, 0, 0, 1, 0.5, 0.5 } : new double[] { 0, 0.5, 0, 1, 1, 0.5 }, (f + 2) & 3);
                    return new[] { s, u };
                }
                case SH.FENCE: case SH.WALL:
                {
                    double w = sh == SH.FENCE ? 0.375 : 0.25;
                    var bx = new List<double[]> { new[] { w, 0, w, 1 - w, 1, 1 - w } };
                    for (int d = 0; d < 4; d++)
                    {
                        int n = world.GetBlock(x + DIRX_W[d], y, z + DIRZ_W[d]);
                        bool ok = OPAQUE[n] != 0 || SHAPE[n] == SH.FENCE || SHAPE[n] == SH.GATE || (sh == SH.WALL && SHAPE[n] == SH.WALL);
                        if (ok) bx.Add(RotBoxU(new[] { w + 0.0625, 0, 0, 1 - w - 0.0625, sh == SH.FENCE ? 1 : 0.875, w }, (d + 2) & 3));
                    }
                    return bx.ToArray();
                }
                case SH.PANE:
                {
                    var bx = new List<double[]> { new[] { 7 / 16.0, 0, 7 / 16.0, 9 / 16.0, 1, 9 / 16.0 } };
                    for (int d = 0; d < 4; d++)
                    {
                        int n = world.GetBlock(x + DIRX_W[d], y, z + DIRZ_W[d]);
                        if (OPAQUE[n] != 0 || SHAPE[n] == SH.PANE || (FLAGS[n] & (BF_GLASS | BF_TRANS)) != 0) bx.Add(RotBoxU(new[] { 7 / 16.0, 0, 0, 9 / 16.0, 1, 7 / 16.0 }, (d + 2) & 3));
                    }
                    return bx.ToArray();          // a pane with nothing to join is just its post (CrossCollisionBlock)
                }
                case SH.GATE:
                {
                    int f = m & 3;
                    return new[] { RotBoxU(new[] { 0, 0.3125, 0.4375, 1, 1, 0.5625 }, (f + 2) & 3) };
                }
                case SH.DOOR:
                {
                    int open = (m >> 3) & 1, hingeR = (m >> 4) & 1, f = m & 3;
                    double[] b = { 0, 0, 13 / 16.0, 1, 1, 1 };
                    if (open != 0) b = hingeR != 0 ? new[] { 13 / 16.0, 0, 0, 1, 1, 1 } : new[] { 0, 0, 0, 3 / 16.0, 1, 1 };
                    return new[] { RotBoxU(b, (f + 2) & 3) };
                }
                case SH.TRAPDOOR:
                {
                    int f = m & 3, top = (m >> 2) & 1, open = (m >> 3) & 1;
                    if (open != 0) return new[] { RotBoxU(new[] { 0, 0, 13 / 16.0, 1, 1, 1 }, (f + 2) & 3) };
                    return top != 0 ? One(0, 13 / 16.0, 0, 1, 1, 1) : One(0, 0, 0, 1, 3 / 16.0, 1);
                }
                case SH.LADDER: return new[] { RotBoxU(new[] { 0, 0, 13 / 16.0, 1, 1, 1 }, ((m & 3) + 2) & 3) };
                case SH.TORCH:
                {
                    if (m == 0) return One(6 / 16.0, 0, 6 / 16.0, 10 / 16.0, 10 / 16.0, 10 / 16.0);
                    int d = m - 1;
                    if (d < 0 || d > 3) return One(6 / 16.0, 0, 6 / 16.0, 10 / 16.0, 10 / 16.0, 10 / 16.0);
                    return One(0.5 - 0.15 + DIRX_W[d] * 0.3, 0.2, 0.5 - 0.15 + DIRZ_W[d] * 0.3, 0.5 + 0.15 + DIRX_W[d] * 0.3, 0.8, 0.5 + 0.15 + DIRZ_W[d] * 0.3);
                }
                case SH.CARPET: case SH.LILY: return One(0, 0, 0, 1, 1 / 16.0, 1);
                case SH.PLATE: return One(1 / 16.0, 0, 1 / 16.0, 15 / 16.0, 1 / 16.0, 15 / 16.0);
                case SH.WIRE: return One(0, 0, 0, 1, 1 / 16.0, 1);
                case SH.RAIL: return One(0, 0, 0, 1, 2 / 16.0, 1);
                case SH.BUTTON: return new[] { RotBoxU(new[] { 5 / 16.0, 6 / 16.0, 0, 11 / 16.0, 10 / 16.0, 2 / 16.0 }, ((m & 3) + 2) & 3) };
                case SH.FARMLAND: return One(0, 0, 0, 1, 15 / 16.0, 1);
                case SH.CACTUS: return One(1 / 16.0, 0, 1 / 16.0, 15 / 16.0, 1, 15 / 16.0);
                case SH.CHEST: return One(1 / 16.0, 0, 1 / 16.0, 15 / 16.0, 14 / 16.0, 15 / 16.0);
                case SH.BED: return One(0, 0, 0, 1, 9 / 16.0, 1);
                case SH.BAMBOO: return One(6 / 16.0, 0, 6 / 16.0, 10 / 16.0, 1, 10 / 16.0);
                case SH.POT: return One(5 / 16.0, 0, 5 / 16.0, 11 / 16.0, 6 / 16.0, 11 / 16.0);
                case SH.PICKLE: return One(0.2, 0, 0.2, 0.8, 0.4, 0.8);
                case SH.CROSS: case SH.TALL: case SH.FIRE: return One(0.15, 0, 0.15, 0.85, (id == B.TALL_GRASS ? 0.8 : 1), 0.85);
                case SH.VINE: case SH.LICHEN: return One(0, 0, 0, 1, 1, 1);
                case SH.SNOWLAYER: return One(0, 0, 0, 1, ((m & 7) + 1) / 8.0, 1);
                case SH.CRYSTAL: return new[] { CrystalBox(id, m) };
                case SH.MODEL:
                {
                    var bx = MODEL_BOX_ID[id];
                    if (bx == null) return FULL_BOX;
                    return new[] { m < bx.Length && bx[m] != null ? bx[m] : bx[0] };
                }
                case SH.WATER: case SH.LAVA: return FULL_BOX;
                default: return FULL_BOX;
            }
        }

        // collision boxes: none for non-solid blocks, fences/walls are 1.5 high
        public static double[][] CollisionBoxes(IBlockAccess world, int id, int m, int x, int y, int z)
        {
            if (COLLIDE[id] == 0) return null;
            int sh = SHAPE[id];
            if (sh == SH.CUBE) return FULL_BOX;
            if (sh == SH.FENCE || sh == SH.WALL)
            {
                var b = BlockBoxes(world, id, m, x, y, z);
                var r = new double[b.Length][];
                for (int i = 0; i < b.Length; i++) { var q = b[i]; r[i] = new[] { q[0], q[1], q[2], q[3], 1.5, q[5] }; }
                return r;
            }
            if (sh == SH.GATE)
            {
                if (((m >> 2) & 1) != 0) return null;
                int f = m & 3;
                return new[] { RotBoxU(new[] { 0, 0, 0.375, 1, 1.5, 0.625 }, (f + 2) & 3) };
            }
            if (sh == SH.CARPET || sh == SH.LILY) return One(0, 0, 0, 1, 1 / 16.0, 1);
            if (id == B.COBWEB) return null;
            return BlockBoxes(world, id, m, x, y, z);
        }

        // Entity.getOnPos (Level.findSupportingBlock): of the blocks whose collision meets the thin slab
        // just under the feet of a body bw wide, the one with its centre nearest the position; the block
        // 0.2 under the feet in that column (with none under the body, in the position's column)
        public static int OnPosBlock(IBlockAccess world, double x, double y, double z, double bw)
        {
            double r = bw / 2; int by = (int)Math.Floor(y - 1e-6);
            int bx = (int)Math.Floor(x), bz = (int)Math.Floor(z); double best = double.PositiveInfinity;
            for (int cx = (int)Math.Floor(x - r); cx <= (int)Math.Floor(x + r - 1e-9); cx++)
                for (int cz = (int)Math.Floor(z - r); cz <= (int)Math.Floor(z + r - 1e-9); cz++)
                {
                    int id = world.GetBlock(cx, by, cz);
                    var bs = id != 0 ? CollisionBoxes(world, id, world.GetMeta(cx, by, cz), cx, by, cz) : null;
                    if (bs == null) continue;
                    bool hit = false;
                    foreach (var b in bs) if (by + b[1] < y && by + b[4] > y - 1e-6 && cx + b[0] < x + r && cx + b[3] > x - r && cz + b[2] < z + r && cz + b[5] > z - r) { hit = true; break; }
                    if (!hit) continue;
                    double dx = cx + 0.5 - x, dy = by + 0.5 - y, dz = cz + 0.5 - z, d = dx * dx + dy * dy + dz * dz;
                    if (d < best) { best = d; bx = cx; bz = cz; }
                }
            return world.GetBlock(bx, (int)Math.Floor(y - 0.2), bz);
        }

        // Outline of a shape made of boxes, as Minecraft draws it (VoxelShape.forAllEdges): the boxes'
        // bounds cut the block into a grid of cells, a cell is full when a box covers it, and a grid line
        // along an axis is an edge where the four cells around it make a crease. Returns [x0,y0,z0,x1,y1,z1, ...]
        // in block units. Cached per shape.
        static readonly Dictionary<string, double[]> SHAPE_EDGES = new Dictionary<string, double[]>();

        public static double[] ShapeEdges(double[][] boxes)
        {
            var kb = new StringBuilder();
            foreach (var b in boxes) { for (int i = 0; i < 6; i++) { kb.Append(b[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)); kb.Append(','); } kb.Append(';'); }
            string key = kb.ToString();
            if (SHAPE_EDGES.TryGetValue(key, out var outp)) return outp;
            var C = new[] { new SortedSet<double>(), new SortedSet<double>(), new SortedSet<double>() };
            foreach (var b in boxes) for (int a = 0; a < 3; a++) { C[a].Add(b[a]); C[a].Add(b[a + 3]); }
            var X = new List<double>(C[0]).ToArray(); var Y = new List<double>(C[1]).ToArray(); var Z = new List<double>(C[2]).ToArray();
            int nx = X.Length - 1, ny = Y.Length - 1, nz = Z.Length - 1;
            var full = new byte[Math.Max(0, nx * ny * nz)];
            for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) for (int k = 0; k < nz; k++)
            {
                double cx = (X[i] + X[i + 1]) / 2, cy = (Y[j] + Y[j + 1]) / 2, cz = (Z[k] + Z[k + 1]) / 2;
                foreach (var b in boxes) if (cx > b[0] && cx < b[3] && cy > b[1] && cy < b[4] && cz > b[2] && cz < b[5]) { full[(i * ny + j) * nz + k] = 1; break; }
            }
            int At(int i, int j, int k) => i >= 0 && j >= 0 && k >= 0 && i < nx && j < ny && k < nz ? full[(i * ny + j) * nz + k] : 0;
            var G = new[] { X, Y, Z };
            var res = new List<double>();
            // edges along axis a: the grid line at (u, v) of the other two axes, cell runs along a
            for (int a = 0; a < 3; a++)
            {
                int ua = (a + 1) % 3, va = (a + 2) % 3; var A = G[a]; var U = G[ua]; var V = G[va];
                int Cell(int s, int u, int v) { var q = new int[3]; q[a] = s; q[ua] = u; q[va] = v; return At(q[0], q[1], q[2]); }
                for (int u = 0; u < U.Length; u++) for (int v = 0; v < V.Length; v++)
                {
                    int start = -1;
                    for (int s = 0; s <= A.Length - 1; s++)
                    {
                        bool on = false;
                        if (s < A.Length - 1)
                        {
                            int c00 = Cell(s, u - 1, v - 1), c10 = Cell(s, u, v - 1), c01 = Cell(s, u - 1, v), c11 = Cell(s, u, v);
                            int n = c00 + c10 + c01 + c11;
                            on = n == 1 || n == 3 || (n == 2 && c00 == c11);
                        }
                        if (on && start < 0) start = s;
                        if (!on && start >= 0)
                        {
                            var p0 = new double[3]; var p1 = new double[3];
                            p0[a] = A[start]; p1[a] = A[s]; p0[ua] = p1[ua] = U[u]; p0[va] = p1[va] = V[v];
                            res.Add(p0[0]); res.Add(p0[1]); res.Add(p0[2]); res.Add(p1[0]); res.Add(p1[1]); res.Add(p1[2]);
                            start = -1;
                        }
                    }
                }
            }
            outp = res.ToArray();
            if (SHAPE_EDGES.Count > 512) SHAPE_EDGES.Clear();
            SHAPE_EDGES[key] = outp;
            return outp;
        }

        // ray vs world: returns {x,y,z, face (0..5 per FN order), id, meta, px,py,pz (hit point)}
        public static RayHit Raycast(IBlockAccess world, double ox, double oy, double oz, double dx, double dy, double dz, double maxDist, bool fluids = false, bool collider = false)
        {
            int x = (int)Math.Floor(ox), y = (int)Math.Floor(oy), z = (int)Math.Floor(oz);
            int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            double tdx = Math.Abs(1 / dx), tdy = Math.Abs(1 / dy), tdz = Math.Abs(1 / dz);
            double tmx = dx != 0 ? ((sx > 0 ? x + 1 - ox : ox - x) * tdx) : double.PositiveInfinity;
            double tmy = dy != 0 ? ((sy > 0 ? y + 1 - oy : oy - y) * tdy) : double.PositiveInfinity;
            double tmz = dz != 0 ? ((sz > 0 ? z + 1 - oz : oz - z) * tdz) : double.PositiveInfinity;
            for (int i = 0; i < 256; i++)
            {
                int id = world.GetBlock(x, y, z);
                // collider: against the collision shapes only, fluids passed (mobs' line of sight, arrows)
                if (id != 0 && (!collider || COLLIDE[id] != 0))
                {
                    int sh = SHAPE[id];
                    bool isFluid = sh == SH.WATER || sh == SH.LAVA;
                    if (!isFluid || (fluids && (world.GetMeta(x, y, z) & 15) == 0))
                    {
                        int m = world.GetMeta(x, y, z);
                        var boxes = isFluid ? FULL_BOX : collider ? (CollisionBoxes(world, id, m, x, y, z) ?? Array.Empty<double[]>()) : BlockBoxes(world, id, m, x, y, z);
                        double bt = 0; int bf = -1; bool has = false;
                        foreach (var b in boxes)
                        {
                            if (RayBox(ox, oy, oz, dx, dy, dz, x + b[0], y + b[1], z + b[2], x + b[3], y + b[4], z + b[5], out double t, out int face) && (!has || t < bt)) { bt = t; bf = face; has = true; }
                        }
                        if (has && bt <= maxDist) return new RayHit { x = x, y = y, z = z, face = bf, id = id, meta = m, t = bt, px = ox + dx * bt, py = oy + dy * bt, pz = oz + dz * bt };
                    }
                }
                if (tmx < tmy && tmx < tmz) { if (tmx > maxDist) break; x += sx; tmx += tdx; }
                else if (tmy < tmz) { if (tmy > maxDist) break; y += sy; tmy += tdy; }
                else { if (tmz > maxDist) break; z += sz; tmz += tdz; }
            }
            return null;
        }

        public static bool RayBox(double ox, double oy, double oz, double dx, double dy, double dz, double x0, double y0, double z0, double x1, double y1, double z1, out double t, out int face)
        {
            double tmin = double.NegativeInfinity, tmax = double.PositiveInfinity; face = -1; t = 0;
            for (int a = 0; a < 3; a++)
            {
                double ax = a == 0 ? ox : a == 1 ? oy : oz, ad = a == 0 ? dx : a == 1 ? dy : dz, lo = a == 0 ? x0 : a == 1 ? y0 : z0, hi = a == 0 ? x1 : a == 1 ? y1 : z1;
                if (Math.Abs(ad) < 1e-9) { if (ax < lo || ax > hi) return false; continue; }
                double t1 = (lo - ax) / ad, t2 = (hi - ax) / ad;
                int f1 = a * 2 + 1, f2 = a * 2; // entering through min side -> face "-a", via max side -> "+a"
                if (t1 > t2) { double tt = t1; t1 = t2; t2 = tt; int ff = f1; f1 = f2; f2 = ff; }
                if (t1 > tmin) { tmin = t1; face = f1; }
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }
            if (tmax < 0) return false;
            // face indices: 0 +x,1 -x,2 +y,3 -y,4 +z,5 -z
            t = Math.Max(0, tmin);
            return true;
        }
    }
}
