// Voxel Forge — Unity port. Collision / selection shapes and player movement.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class BoxHit { public double bx0, by0, bz0, bx1, by1, bz1; }

    public static partial class VF
    {
        static List<double[]> L(params double[][] a) { return new List<double[]>(a); }
        public static double[] virtualRotateBox(double[] b, string f)
        {
            var r = (double[])b.Clone();
            int n = f == "west" ? 1 : f == "north" ? 2 : f == "east" ? 3 : 0;
            while (n-- > 0) r = new[] { 1 - r[5], r[1], r[0], 1 - r[2], r[4], r[3] };
            return r;
        }
        static bool virtualConnectsAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z);
            if (id == B.AIR || isWater(id) || isLava(id)) return false;
            if (isVirtualId(id))
            {
                var d = virtualDefAt(x, y, z);
                if (d == null || d.plant || d.waterPlant) return false;
                return d.solid || d.shape == "fence" || d.shape == "wall" || d.shape == "pane" || d.shape == "gate";
            }
            var b = bdef(id);
            if (b == null || b.plant || b.waterPlant) return false;
            return b.solid || b.special == "fence" || b.special == "wall" || b.special == "pane" || b.special == "gate";
        }
        static readonly double[][] FENCE_ARMS =
        {
            new[] { 0.625, 0.375, 0.4375, 1, 0.5625, 0.5625 }, new[] { 0.625, 0.75, 0.4375, 1, 0.9375, 0.5625 },
            new[] { 0, 0.375, 0.4375, 0.375, 0.5625, 0.5625 }, new[] { 0, 0.75, 0.4375, 0.375, 0.9375, 0.5625 },
            new[] { 0.4375, 0.375, 0.625, 0.5625, 0.5625, 1 }, new[] { 0.4375, 0.75, 0.625, 0.5625, 0.9375, 1 },
            new[] { 0.4375, 0.375, 0, 0.5625, 0.5625, 0.375 }, new[] { 0.4375, 0.75, 0, 0.5625, 0.9375, 0.375 },
        };
        static readonly int[][] ARM_DIRS = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
        static readonly double[][] WALL_ARMS = { new[] { 0.6875, 0, 0.3125, 1, 1, 0.6875 }, new[] { 0, 0, 0.3125, 0.3125, 1, 0.6875 }, new[] { 0.3125, 0, 0.6875, 0.6875, 1, 1 }, new[] { 0.3125, 0, 0, 0.6875, 1, 0.3125 } };
        static readonly double[][] PANE_ARMS = { new[] { 0.5625, 0, 0.4375, 1, 1, 0.5625 }, new[] { 0, 0, 0.4375, 0.4375, 1, 0.5625 }, new[] { 0.4375, 0, 0.5625, 0.5625, 1, 1 }, new[] { 0.4375, 0, 0, 0.5625, 1, 0.4375 } };
        static List<double[]> fenceBoxes(int x, int y, int z, bool collision)
        {
            var a = L(new[] { 0.375, 0, 0.375, 0.625, collision ? 1.5 : 1, 0.625 });
            for (int k = 0; k < 4; k++)
                if (virtualConnectsAt(x + ARM_DIRS[k][0], y, z + ARM_DIRS[k][1]))
                {
                    var q1 = (double[])FENCE_ARMS[k * 2].Clone(); var q2 = (double[])FENCE_ARMS[k * 2 + 1].Clone();
                    if (collision) q1[4] = q2[4] = 1.5;
                    a.Add(q1); a.Add(q2);
                }
            return a;
        }
        static List<double[]> wallBoxes(int x, int y, int z, bool collision)
        {
            var a = L(new[] { 0.25, 0, 0.25, 0.75, collision ? 1.5 : 1, 0.75 });
            for (int k = 0; k < 4; k++)
                if (virtualConnectsAt(x + ARM_DIRS[k][0], y, z + ARM_DIRS[k][1])) { var q = (double[])WALL_ARMS[k].Clone(); if (collision) q[4] = 1.5; a.Add(q); }
            return a;
        }
        static List<double[]> paneBoxes(int x, int y, int z)
        {
            var a = L(new[] { 0.4375, 0, 0.4375, 0.5625, 1, 0.5625 });
            for (int k = 0; k < 4; k++) if (virtualConnectsAt(x + ARM_DIRS[k][0], y, z + ARM_DIRS[k][1])) a.Add((double[])PANE_ARMS[k].Clone());
            return a;
        }
        static string rotOpenDoor(string f) { return f == "north" ? "east" : f == "east" ? "south" : f == "south" ? "west" : f == "west" ? "north" : null; }
        public static List<double[]> virtualShapeBoxesAt(VirtualDef d, JObj m, int x, int y, int z, bool collision = false)
        {
            if (d == null) return new List<double[]>();
            var sp = d.shape;
            m = m ?? new JObj();
            if (m.Bool("bedPart")) return L(new[] { 0, 0, 0, 1, 0.56, 1 });
            if (sp == "slab") return L(m.Bool("upper") ? new[] { 0, 0.5, 0, 1, 1, 1 } : new[] { 0, 0, 0, 1, 0.5, 1 });
            if (sp == "stairs")
            {
                bool up = m.Bool("upper"); double lo = up ? 0 : 0.5, hi = up ? 0.5 : 1;
                return L(up ? new[] { 0, 0.5, 0, 1, 1, 1 } : new[] { 0, 0, 0, 1, 0.5, 1 }, virtualRotateBox(new[] { 0, lo, 0.5, 1, hi, 1 }, m.Str("facing") ?? "south"));
            }
            if (sp == "fence") return fenceBoxes(x, y, z, collision);
            if (sp == "wall")
            {
                var a = L(new[] { 0.25, 0, 0.25, 0.75, collision ? 1.5 : 1, 0.75 });
                for (int k = 0; k < 4; k++) if (virtualConnectsAt(x + ARM_DIRS[k][0], y, z + ARM_DIRS[k][1])) { var q = (double[])WALL_ARMS[k].Clone(); if (collision) q[4] = 1.5; a.Add(q); }
                return a;
            }
            if (sp == "pane") return paneBoxes(x, y, z);
            if (sp == "gate")
            {
                if (m.Bool("open") && collision) return new List<double[]>();
                var f = m.Str("facing") ?? "south"; bool axis = f == "east" || f == "west";
                if (collision) return L(axis ? new[] { 0, 0, 0.375, 1, 1.5, 0.625 } : new[] { 0.375, 0, 0, 0.625, 1.5, 1 });
                var posts = axis ? L(new[] { 0.05, 0, 0.36, 0.18, 1, 0.64 }, new[] { 0.82, 0, 0.36, 0.95, 1, 0.64 }) : L(new[] { 0.36, 0, 0.05, 0.64, 1, 0.18 }, new[] { 0.36, 0, 0.82, 0.64, 1, 0.95 });
                if (m.Bool("open")) return posts;
                if (axis) { posts.Add(new[] { 0.18, 0.34, 0.43, 0.82, 0.5, 0.57 }); posts.Add(new[] { 0.18, 0.7, 0.43, 0.82, 0.86, 0.57 }); }
                else { posts.Add(new[] { 0.43, 0.34, 0.18, 0.57, 0.5, 0.82 }); posts.Add(new[] { 0.43, 0.7, 0.18, 0.57, 0.86, 0.82 }); }
                return posts;
            }
            if (sp == "trapdoor")
            {
                if (m.Bool("open")) return L(virtualRotateBox(new double[] { 0, 0, 0.8125, 1, 1, 1 }, m.Str("facing") ?? "south"));
                return L(m.Bool("upper") ? new[] { 0, 0.8125, 0, 1, 1, 1 } : new[] { 0, 0, 0, 1, 0.1875, 1 });
            }
            if (sp == "carpet" || sp == "lilypad" || sp == "rail") return L(new[] { 0, 0, 0, 1, 0.0625, 1 });
            if (sp == "plate")
            {
                if (collision) return new List<double[]>();
                double ph = m.Bool("pressed") ? 0.03125 : 0.0625;
                return L(new[] { 0.0625, 0, 0.0625, 0.9375, ph, 0.9375 });
            }
            if (sp == "button") return collision ? new List<double[]>() : L(buttonBoxFromMeta(m));
            if (sp == "seapickle") return L(new[] { 0.375, 0, 0.375, 0.625, 0.375, 0.625 });
            if (sp == "bamboo") return collision ? new List<double[]>() : L(new[] { 0.4375, 0, 0.4375, 0.5625, 1, 0.5625 });
            if (sp == "pot") return L(new[] { 0.3125, 0, 0.3125, 0.6875, 0.375, 0.6875 });
            if (sp == "door")
            {
                var f = m.Str("facing") ?? "south";
                if (m.Bool("open")) f = rotOpenDoor(f) ?? f;
                double t = 0.12;
                return f == "north" || f == "south" ? L(new[] { 0, 0, f == "south" ? 1 - t : 0, 1, 1, f == "south" ? 1 : t }) : L(new[] { f == "east" ? 1 - t : 0, 0, 0, f == "east" ? 1 : t, 1, 1 });
            }
            if (sp == "vine" || sp == "lichen") return collision ? new List<double[]>() : multiFaceBoxes(attachmentFaceMask(m, sp == "vine" ? 15 : 63));
            if (sp == "tallplant" || sp == "cross") return new List<double[]>();
            return d.solid ? L(new double[] { 0, 0, 0, 1, 1, 1 }) : new List<double[]>();
        }
        public static double[] facingRotBox(double[] b, string f)
        {
            if (f == "south") return (double[])b.Clone();
            var r = (double[])b.Clone();
            int rot = f == "west" ? 1 : f == "north" ? 2 : f == "east" ? 3 : 0;
            for (int n = 0; n < rot; n++) r = new[] { 1 - r[5], r[1], r[0], 1 - r[2], r[4], r[3] };
            return r;
        }
        public static int facingIndexMain(string f) { return f == "west" ? 1 : f == "north" ? 2 : f == "east" ? 3 : 0; }
        static readonly int[][] MAIN_STAIR_DIR_SYNC = { new[] { 0, 1 }, new[] { -1, 0 }, new[] { 0, -1 }, new[] { 1, 0 } };
        static List<double[]> stairsBoxesMainSync(int id, int x, int y, int z, JObj m)
        {
            string facing = m.Str("facing") ?? "south"; int dir = facingIndexMain(facing); bool up = m.Bool("upper");
            double lo = up ? 0 : 0.5, hi = up ? 0.5 : 1;
            var outp = L(up ? new[] { 0, 0.5, 0, 1, 1, 1 } : new[] { 0, 0, 0, 1, 0.5, 1 });
            Func<int, int, bool> isStair = (xx, zz) => { var b = bdef(getBlock(xx, y, zz)); return b != null && b.special == "stairs"; };
            Func<int, int, JObj> mm = (xx, zz) => getBlockMeta(xx, y, zz) ?? new JObj();
            Func<int, bool, int, bool> guard = (wantDir, wantUp, testDir) =>
            {
                var dd = MAIN_STAIR_DIR_SYNC[testDir]; int gx = x + dd[0], gz = z + dd[1]; var gq = mm(gx, gz);
                return !isStair(gx, gz) || facingIndexMain(gq.Str("facing") ?? "south") != wantDir || gq.Bool("upper") != wantUp;
            };
            string cornerType = null; bool cornerLeft = false;
            var d = MAIN_STAIR_DIR_SYNC[dir]; int x1 = x + d[0], z1 = z + d[1]; var q = mm(x1, z1);
            if (isStair(x1, z1) && q.Bool("upper") == up)
            {
                int nd = facingIndexMain(q.Str("facing") ?? "south");
                if ((nd & 1) != (dir & 1) && guard(dir, up, (nd + 2) & 3)) { cornerType = "outer"; cornerLeft = nd == ((dir + 1) & 3); }
            }
            if (cornerType == null)
            {
                d = MAIN_STAIR_DIR_SYNC[(dir + 2) & 3]; x1 = x + d[0]; z1 = z + d[1]; q = mm(x1, z1);
                if (isStair(x1, z1) && q.Bool("upper") == up)
                {
                    int nd = facingIndexMain(q.Str("facing") ?? "south");
                    if ((nd & 1) != (dir & 1) && guard(dir, up, nd)) { cornerType = "inner"; cornerLeft = nd == ((dir + 1) & 3); }
                }
            }
            List<double[]> step;
            if (cornerType == "outer") step = L(cornerLeft ? new[] { 0, lo, 0.5, 0.5, hi, 1 } : new[] { 0.5, lo, 0.5, 1, hi, 1 });
            else if (cornerType == "inner") step = L(new[] { 0, lo, 0.5, 1, hi, 1 }, cornerLeft ? new[] { 0, lo, 0, 0.5, hi, 0.5 } : new[] { 0.5, lo, 0, 1, hi, 0.5 });
            else step = L(new[] { 0, lo, 0.5, 1, hi, 1 });
            foreach (var b in step) outp.Add(facingRotBox(b, facing));
            return outp;
        }
        static readonly List<double[]> COLLISION_EMPTY_BOXES = new List<double[]>(), COLLISION_FULL_BOXES = L(new double[] { 0, 0, 0, 1, 1, 1 }), COLLISION_BED_BOXES = L(new[] { 0, 0, 0, 1, 0.56, 1 }),
            COLLISION_DOOR_N = L(new[] { 0, 0, 0, 1, 1, 0.12 }), COLLISION_DOOR_S = L(new[] { 0, 0, 0.88, 1, 1, 1 }), COLLISION_DOOR_W = L(new[] { 0, 0, 0, 0.12, 1, 1 }), COLLISION_DOOR_E = L(new[] { 0.88, 0, 0, 1, 1, 1 });
        /// <summary>Returns null for "use default box" (reference returns null).</summary>
        static List<double[]> specialShapeBoxes(int id, int x, int y, int z, bool collision = false)
        {
            var b = bdef(id); var sp = b != null ? b.special : null;
            if (sp == null) return b != null && b.solid ? COLLISION_FULL_BOXES : COLLISION_EMPTY_BOXES;
            var m = getBlockMeta(x, y, z) ?? new JObj();
            switch (sp)
            {
                case "slab": return L(m.Bool("upper") ? new[] { 0, 0.5, 0, 1, 1, 1 } : new[] { 0, 0, 0, 1, 0.5, 1 });
                case "stairs": return stairsBoxesMainSync(id, x, y, z, m);
                case "shipwheel": { var f = m.Str("facing") ?? "south"; return L(facingRotBox(new[] { 0.125, 0, 0.1875, 0.875, 0.62, 0.8125 }, f), facingRotBox(new[] { 0.14, 0.62, 0.4, 0.86, 0.98, 0.6 }, f)); }
                case "fence": return fenceBoxes(x, y, z, collision);
                case "gate":
                    {
                        if (m.Bool("open") && collision) return new List<double[]>();
                        var f = m.Str("facing") ?? "south"; bool axis = f == "east" || f == "west";
                        if (collision) return L(axis ? new[] { 0, 0, 0.375, 1, 1.5, 0.625 } : new[] { 0.375, 0, 0, 0.625, 1.5, 1 });
                        var posts = axis ? L(new[] { 0.05, 0, 0.36, 0.18, 1, 0.64 }, new[] { 0.82, 0, 0.36, 0.95, 1, 0.64 }) : L(new[] { 0.36, 0, 0.05, 0.64, 1, 0.18 }, new[] { 0.36, 0, 0.82, 0.64, 1, 0.95 });
                        if (m.Bool("open"))
                        {
                            if (axis) { posts.Add(new[] { 0.05, 0.34, 0.18, 0.42, 0.9, 0.34 }); posts.Add(new[] { 0.58, 0.34, 0.66, 0.95, 0.9, 0.82 }); }
                            else { posts.Add(new[] { 0.18, 0.34, 0.05, 0.34, 0.9, 0.42 }); posts.Add(new[] { 0.66, 0.34, 0.58, 0.82, 0.9, 0.95 }); }
                            return posts;
                        }
                        if (axis) { posts.Add(new[] { 0.18, 0.34, 0.43, 0.82, 0.5, 0.57 }); posts.Add(new[] { 0.18, 0.7, 0.43, 0.82, 0.86, 0.57 }); }
                        else { posts.Add(new[] { 0.43, 0.34, 0.18, 0.57, 0.5, 0.82 }); posts.Add(new[] { 0.43, 0.7, 0.18, 0.57, 0.86, 0.82 }); }
                        return posts;
                    }
                case "wall": return wallBoxes(x, y, z, collision);
                case "pane": return paneBoxes(x, y, z);
                case "carpet": case "lilypad": return L(new[] { 0, 0, 0, 1, 0.0625, 1 });
                case "plate": { if (collision) return new List<double[]>(); double ph = m.Bool("pressed") ? 0.03125 : 0.0625; return L(new[] { 0.0625, 0, 0.0625, 0.9375, ph, 0.9375 }); }
                case "button": return collision ? new List<double[]>() : L(buttonBoxFromMeta(m));
                case "seapickle":
                    {
                        if (collision) return new List<double[]>();
                        int n = (int)Math.Max(1, Math.Min(4, m.Num("count", 0) != 0 ? m.Num("count") : 1));
                        var P = new[] { new[] { 0.375, 0, 0.375, 0.625, 0.375, 0.625 }, new[] { 0.18, 0, 0.22, 0.43, 0.3125, 0.47 }, new[] { 0.6, 0, 0.5, 0.85, 0.4375, 0.75 }, new[] { 0.32, 0, 0.62, 0.57, 0.3125, 0.87 } };
                        var a = new List<double[]>(); for (int i = 0; i < n; i++) a.Add(P[i]); return a;
                    }
                case "bamboo": return collision ? new List<double[]>() : L(new[] { 0.4375, 0, 0.4375, 0.5625, 1, 0.5625 });
                case "pot": return L(new[] { 0.3125, 0, 0.3125, 0.6875, 0.375, 0.6875 });
                case "trapdoor":
                    if (m.Bool("open")) return L(facingRotBox(new double[] { 0, 0, 0.8125, 1, 1, 1 }, m.Str("facing") ?? "south"));
                    return L(m.Bool("upper") ? new[] { 0, 0.8125, 0, 1, 1, 1 } : new[] { 0, 0, 0, 1, 0.1875, 1 });
                case "vine": case "lichen": return collision ? new List<double[]>() : multiFaceBoxes(attachmentFaceMask(m, sp == "vine" ? 15 : 63));
                case "door": case "doorOpen": case "bed": return null;
                case "ladder": case "torch": case "rail": return collision ? new List<double[]>() : null;
            }
            return b.solid ? L(new double[] { 0, 0, 0, 1, 1, 1 }) : new List<double[]>();
        }
        public static List<double[]> collisionBoxesAt(int id, int x, int y, int z)
        {
            if (isVirtualId(id)) return virtualShapeBoxesAt(virtualDefAt(x, y, z), getBlockMeta(x, y, z) ?? new JObj(), x, y, z, true);
            if (id == B.DOOR || id == B.DOOR_TOP || id == B.DOOR_OPEN || id == B.DOOR_OPEN_TOP)
            {
                var mt = getBlockMeta(x, y, z); var f = (mt != null ? mt.Str("facing") : null) ?? "south";
                if (id == B.DOOR_OPEN || id == B.DOOR_OPEN_TOP) f = rotOpenDoor(f) ?? "east";
                return f == "north" ? COLLISION_DOOR_N : f == "south" ? COLLISION_DOOR_S : f == "west" ? COLLISION_DOOR_W : COLLISION_DOOR_E;
            }
            if (id == B.BED) return COLLISION_BED_BOXES;
            var b = bdef(id);
            if (b == null) return COLLISION_EMPTY_BOXES;
            if (b.special == null) return b.solid ? COLLISION_FULL_BOXES : COLLISION_EMPTY_BOXES;
            var q = specialShapeBoxes(id, x, y, z, true);
            return q ?? (b.solid ? COLLISION_FULL_BOXES : COLLISION_EMPTY_BOXES);
        }
        public static bool pointBlocked(double x, double y, double z)
        {
            int bx = JS.floor(x), by = JS.floor(y), bz = JS.floor(z), id = getBlock(bx, by, bz);
            double lx = x - bx, ly = y - by, lz = z - bz;
            foreach (var b in collisionBoxesAt(id, bx, by, bz))
                if (lx >= b[0] && lx <= b[3] && ly >= b[1] && ly <= b[4] && lz >= b[2] && lz <= b[5]) return true;
            return false;
        }
        static readonly List<double[]> SELECT_FULL_BOXES = L(new double[] { 0, 0, 0, 1, 1, 1 }), SELECT_RAIL_BOXES = L(new[] { 0, 0, 0, 1, 0.08, 1 }),
            SELECT_LADDER_NORTH = L(new[] { 0, 0, 0, 1, 1, 0.08 }), SELECT_LADDER_SOUTH = L(new[] { 0, 0, 0.92, 1, 1, 1 }), SELECT_LADDER_WEST = L(new[] { 0, 0, 0, 0.08, 1, 1 }), SELECT_LADDER_EAST = L(new[] { 0.92, 0, 0, 1, 1, 1 });
        public static List<double[]> selectionBoxesAt(int id, int x, int y, int z)
        {
            if (isVirtualId(id))
            {
                var q = virtualShapeBoxesAt(virtualDefAt(x, y, z), getBlockMeta(x, y, z) ?? new JObj(), x, y, z, false);
                return q.Count > 0 ? q : SELECT_FULL_BOXES;
            }
            if (id == B.DOOR || id == B.DOOR_TOP || id == B.DOOR_OPEN || id == B.DOOR_OPEN_TOP || id == B.BED) return collisionBoxesAt(id, x, y, z);
            if (id == B.LADDER)
            {
                var mt = getBlockMeta(x, y, z); var f = (mt != null ? mt.Str("facing") : null) ?? "south";
                return f == "north" ? SELECT_LADDER_NORTH : f == "south" ? SELECT_LADDER_SOUTH : f == "west" ? SELECT_LADDER_WEST : SELECT_LADDER_EAST;
            }
            if (id == B.TORCH) return L(torchBoxFromMeta(getBlockMeta(x, y, z) ?? new JObj()));
            if (id == B.RAIL) return SELECT_RAIL_BOXES;
            var s = specialShapeBoxes(id, x, y, z, false);
            return s == null ? SELECT_FULL_BOXES : s.Count > 0 ? s : SELECT_FULL_BOXES;
        }

        // ---------------- player collision ----------------
        public const double PLAYER_COLLISION_RADIUS = 0.3, PLAYER_COLLISION_HEIGHT = 1.8;
        static readonly BoxHit boxHitScratch = new BoxHit();
        static double playerH() { return player.h != 0 ? player.h : PLAYER_COLLISION_HEIGHT; }
        public static BoxHit playerCollisionAt(double x, double y, double z, double h = double.NaN)
        {
            if (double.IsNaN(h)) h = playerH();
            int minX = JS.floor(x - PLAYER_COLLISION_RADIUS), maxX = JS.floor(x + PLAYER_COLLISION_RADIUS), minY = JS.floor(y) - 1, maxY = JS.floor(y + h - 0.001),
                minZ = JS.floor(z - PLAYER_COLLISION_RADIUS), maxZ = JS.floor(z + PLAYER_COLLISION_RADIUS);
            double x0 = x - PLAYER_COLLISION_RADIUS, x1 = x + PLAYER_COLLISION_RADIUS, z0 = z - PLAYER_COLLISION_RADIUS, z1 = z + PLAYER_COLLISION_RADIUS, top = y + h;
            for (int xx = minX; xx <= maxX; xx++)
                for (int yy = minY; yy <= maxY; yy++)
                    for (int zz = minZ; zz <= maxZ; zz++)
                    {
                        int id = getBlock(xx, yy, zz);
                        if (id == B.AIR) continue;
                        foreach (var b in collisionBoxesAt(id, xx, yy, zz))
                            if (x1 > xx + b[0] && x0 < xx + b[3] && top > yy + b[1] && y < yy + b[4] && z1 > zz + b[2] && z0 < zz + b[5])
                                return new BoxHit { bx0 = xx + b[0], by0 = yy + b[1], bz0 = zz + b[2], bx1 = xx + b[3], by1 = yy + b[4], bz1 = zz + b[5] };
                    }
            return null;
        }
        static void movePlayerAxisSource(int axis, double delta)
        {
            if (delta == 0) return;
            if (axis == 0) player.x += delta; else if (axis == 1) player.y += delta; else player.z += delta;
            var hit = playerCollisionAt(player.x, player.y, player.z);
            if (hit == null) return;
            const double eps = 0.001;
            if (axis == 0 || axis == 2)
            {
                if (!player.flying && player.vy <= 0.08)
                {
                    double oldY = player.y;
                    player.y += 0.6;
                    if (playerCollisionAt(player.x, player.y, player.z) == null)
                    {
                        double y = player.y;
                        while (y - 0.05 > oldY && playerCollisionAt(player.x, y - 0.05, player.z) == null) y -= 0.05;
                        player.y = y;
                        player.onGround = true;
                        return;
                    }
                    player.y = oldY;
                }
                if (axis == 0) player.x = delta > 0 ? hit.bx0 - PLAYER_COLLISION_RADIUS - eps : hit.bx1 + PLAYER_COLLISION_RADIUS + eps;
                else player.z = delta > 0 ? hit.bz0 - PLAYER_COLLISION_RADIUS - eps : hit.bz1 + PLAYER_COLLISION_RADIUS + eps;
                if (axis == 0) player.vx = 0; else player.vz = 0;
                player.hitWall = true;
            }
            else
            {
                if (delta > 0) player.y = hit.by0 - playerH() - eps;
                else { player.y = hit.by1 + eps; player.onGround = true; }
                player.vy = 0;
            }
        }
        public static bool collides(double x, double y, double z, double h = double.NaN) { return playerCollisionAt(x, y, z, double.IsNaN(h) ? playerH() : h) != null; }
        static bool onLadder()
        {
            double r = 0.3; int y = JS.floor(player.y);
            for (int x = JS.floor(player.x - r); x <= JS.floor(player.x + r); x++)
                for (int z = JS.floor(player.z - r); z <= JS.floor(player.z + r); z++)
                { var b = bdef(getBlock(x, y, z)); if (b != null && b.special == "ladder") return true; }
            return false;
        }
        static bool recoverPlayerOverlapSource()
        {
            const double FULL = 1.8, SHORT = 0.85;
            double ox = player.x, oy = player.y, oz = player.z;
            if ((player.h != 0 ? player.h : FULL) < FULL && playerCollisionAt(ox, oy, oz, FULL) == null) player.h = FULL;
            double h = player.h != 0 ? player.h : FULL;
            if (playerCollisionAt(ox, oy, oz, h) == null) return false;
            if (playerCollisionAt(ox, oy, oz, SHORT) == null) { player.h = SHORT; return true; }
            Func<double, double, bool> tryNudge = (y, hh) =>
            {
                foreach (var r in new[] { 0.35, 0.7, 1.05 })
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4, nx = ox + Math.Cos(a) * r, nz = oz + Math.Sin(a) * r;
                        if (playerCollisionAt(nx, y, nz, hh) == null) { player.x = nx; player.y = y; player.z = nz; player.h = hh; player.vx = player.vz = 0; return true; }
                    }
                return false;
            };
            if (tryNudge(oy, h)) return true;
            double fy = Math.Floor(oy) + 0.01;
            if (fy < oy - 0.01 && playerCollisionAt(ox, fy, oz, SHORT) == null) { player.y = fy; player.h = SHORT; player.vy = 0; return true; }
            foreach (var hh in new[] { FULL, SHORT })
                for (int d = 1; d <= 4; d++)
                {
                    double up = Math.Floor(oy) + d + 0.01;
                    if (playerCollisionAt(ox, up, oz, hh) == null) { player.y = up; player.h = hh; player.vy = 0; return true; }
                    double dn = Math.Floor(oy) - d + 0.01;
                    if (dn > WORLD_MIN_Y && playerCollisionAt(ox, dn, oz, hh) == null) { player.y = dn; player.h = hh; player.vy = 0; return true; }
                }
            return false;
        }
        public static void movePlayer(double dt)
        {
            if (player.dead || uiOpen || player.riding != null) return;
            const double PLAYER_H = 1.8, PLAYER_SWIM_H = 0.85, PLAYER_SHORE_SCAN = 1.5;
            recoverPlayerOverlapSource();
            double side = 0, forward = 0;
            bool fw = keyDown("KeyW") || keyDown("ArrowUp"), bw = keyDown("KeyS") || keyDown("ArrowDown"), lf = keyDown("KeyA") || keyDown("ArrowLeft"), rt = keyDown("KeyD") || keyDown("ArrowRight");
            if (fw) forward -= 1; if (bw) forward += 1; if (lf) side -= 1; if (rt) side += 1;
            double il = JS.hypot(side, forward);
            if (il > 1) { side /= il; forward /= il; }
            bool moving = il > 0.01, sprint = moving && (keyDown("ControlLeft") || keyDown("ControlRight") || sprintLatch) && (player.creative || player.hunger > 6);
            player.sprinting = sprint;
            bool down = keyDown("ShiftLeft") || keyDown("ShiftRight"), jump = keyDown("Space");
            double speed = player.flying ? (sprint ? 16 : 10) : sprint ? 7.2 : 4.4;
            int groundId = player.onGround && !player.flying ? getBlock(player.x, player.y - 0.01, player.z) : B.AIR;
            bool inWeb = !player.flying && (getBlock(player.x, player.y + 0.2, player.z) == B.COBWEB || getBlock(player.x, player.y + 1.1, player.z) == B.COBWEB);
            if (inWeb) speed *= 0.22;
            double sy = Math.Sin(player.yaw), cy = Math.Cos(player.yaw), wishX = -forward * sy + side * cy, wishZ = -forward * -cy + side * sy;
            double groundAccel = groundId == B.ICE ? 2.2 : 12, accel = Math.Min(1, dt * (player.onGround || player.flying ? (player.flying ? 12 : groundAccel) : 5));
            player.vx += (wishX * speed - player.vx) * accel;
            player.vz += (wishZ * speed - player.vz) * accel;
            double h = player.h != 0 ? player.h : PLAYER_H, headY = player.y + h - 0.4, midY = player.y + 0.5;
            int headId = getBlock(player.x, headY, player.z), midId = getBlock(player.x, midY, player.z), feetId = getBlock(player.x, player.y, player.z);
            bool headWater = pointInWater(player.x, headY, player.z), midWater = pointInWater(player.x, midY, player.z), headFluid = headWater || isLava(headId);
            player.inWater = headWater || midWater;
            player.headInWater = headWater;
            player.inLava = isLava(headId) || isLava(midId) || isLava(feetId);
            player.swimming = false;
            if (player.flying)
            {
                int v = 0; if (jump) v++; if (down) v--;
                player.vy += (v * 8 - player.vy) * Math.Min(1, dt * 10);
            }
            else if (player.inWater)
            {
                bool swimming = sprint && moving && headFluid;
                player.swimming = swimming;
                if (swimming)
                {
                    if ((player.h != 0 ? player.h : PLAYER_H) > PLAYER_SWIM_H) player.h = PLAYER_SWIM_H;
                    double cp = Math.Cos(player.pitch), spp = Math.Sin(player.pitch), tx = (-forward * sy * cp + side * cy) * speed, tz = (-forward * -cy * cp + side * sy) * speed, ty = -forward * spp * speed, k = Math.Min(1, dt * 5);
                    player.vx += (tx - player.vx) * k; player.vz += (tz - player.vz) * k; player.vy += (ty - player.vy) * k;
                    if (jump) player.vy += 10 * dt;
                    if (down) player.vy -= 14 * dt;
                    if (player.vy > speed) player.vy = speed;
                    if (player.vy < -speed) player.vy = -speed;
                }
                else
                {
                    player.vy -= 5.5 * dt;
                    if (jump) player.vy += 16 * dt;
                    if (down) player.vy -= 22 * dt;
                    double minVy = down ? -5 : -3.2;
                    if (player.vy < minVy) player.vy = minVy;
                    if (player.vy > 3.4 && JS.now() > player.boostUntil) player.vy = 3.4;
                    double drag = Math.Min(1, dt * (sprint ? 0.8 : 2));
                    player.vx *= 1 - drag; player.vz *= 1 - drag;
                }
            }
            else if (onLadder())
            {
                bool descend = down || keyDown("KeyS") || keyDown("ArrowDown");
                player.vy = jump ? 4 : descend ? -4 : moving ? 2.6 : -1.4;
            }
            else
            {
                player.vy -= 23 * dt;
                if (inWeb && player.vy < -1.2) player.vy = -1.2;
                if (player.vy < -42) player.vy = -42;
                if (jump && player.onGround) { player.vy = 7.6; player.onGround = false; }
            }
            player.onGround = false;
            player.hitWall = false;
            movePlayerAxisSource(0, player.vx * dt);
            movePlayerAxisSource(2, player.vz * dt);
            movePlayerAxisSource(1, player.vy * dt);
            if (player.inWater && player.hitWall && moving && !player.flying && !down)
            {
                double n = JS.hypot(wishX, wishZ), qx = n != 0 ? player.x + wishX / n * 0.4 : player.x, qz = n != 0 ? player.z + wishZ / n * 0.4 : player.z;
                bool clear = false;
                for (double up = 0.6; up <= PLAYER_SHORE_SCAN + 1e-6 && !clear; up += 0.45)
                    if (playerCollisionAt(qx, player.y + up, qz, player.h != 0 ? player.h : PLAYER_H) == null) clear = true;
                if (clear) { player.vy = Math.Max(player.vy, headFluid ? 4.5 : 7); player.boostUntil = JS.now() + 350; }
            }
            if (player.flying && player.onGround) player.flying = false;
        }
    }
}
