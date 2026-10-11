// Voxel Forge — Unity port. Shared mesh core (one instance per mesher thread + one for the synchronous edit path).
// Faithful port of createSharedMeshCore(): greedy opaque sections, cutout/transparent special shapes,
// Minecraft-style water (corner averaging, flat-top merging), lava, stairs corners, virtual catalog blocks.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class MeshCore
    {
        const int C = 16, H = 384, PAD = 16, RW = 48, RN = RW * H * RW;
        static readonly double[] SHADE_POS = { 0.74, 1, 0.84 }, SHADE_NEG = { 0.66, 0.56, 0.78 };
        readonly byte[] region = new byte[RN], lightRegion = new byte[RN];
        readonly MDef[] defs;
        readonly Dictionary<string, MDef> vdefs;
        static readonly JObj EMPTY = new JObj();
        Dictionary<int, JObj> metaMap = new Dictionary<int, JObj>();

        public MeshCore(MDef[] nativeDefs, Dictionary<string, MDef> virtualDefs) { defs = nativeDefs; vdefs = virtualDefs; }

        static int rIndex(int x, int y, int z) { return (y * RW + z) * RW + x; }
        static bool inside(int x, int y, int z) { return x >= 0 && x < RW && z >= 0 && z < RW && y >= 0 && y < H; }
        int at(int x, int y, int z)
        {
            if (y < 0 || y >= H) return B.AIR;
            int rx = x + PAD, rz = z + PAD;
            return inside(rx, y, rz) ? region[rIndex(rx, y, rz)] : B.AIR;
        }
        MDef def(int id) { return id >= 0 && id < defs.Length ? defs[id] : null; }
        static bool isVirtual(int id) { return id == B.VIRTUAL_OPAQUE || id == B.VIRTUAL_TRANSPARENT || id == B.VIRTUAL_CUTOUT || id == B.VIRTUAL_PLANT; }
        static int mkey(int x, int y, int z) { return (y * 48 + (z + 16)) * 48 + (x + 16); }
        JObj vmeta(int x, int y, int z)
        {
            if (x < -16 || x > 31 || z < -16 || z > 31 || y < 0 || y >= H) return EMPTY;
            JObj m; return metaMap.TryGetValue(mkey(x, y, z), out m) && m != null ? m : EMPTY;
        }
        MDef vdef(int x, int y, int z)
        {
            var v = vmeta(x, y, z).Str("v"); MDef d;
            return v != null && vdefs != null && vdefs.TryGetValue(v, out d) ? d : null;
        }
        MDef actualDefAt(int x, int y, int z) { int id = at(x, y, z); return isVirtual(id) ? vdef(x, y, z) : def(id); }
        bool virtualConnects(int x, int y, int z)
        {
            int id = at(x, y, z); var q = actualDefAt(x, y, z);
            if (q == null || id == B.AIR || isWater(id) || isLava(id) || q.plant || q.waterPlant) return false;
            var s = q.sp;
            return q.solid || s == "fence" || s == "wall" || s == "pane" || s == "gate";
        }
        /// <summary>JS `m.k || d` for a value only compared against names: falsy -> d, a truthy non-string -> a sentinel matching no name.</summary>
        static string orStr(JObj m, string k, string d) { object o; if (m == null || !m.TryGetValue(k, out o) || !Json.Truthy(o)) return d; return o as string ?? "\u0000"; }
        static int vTile(MDef b, int axis, int sign, JObj m)
        {
            var part = m != null ? m.Str("part") : null;
            if ((b.shape == "door" || b.shape == "tallplant") && part == "top" && b.partTop >= 0) return b.partTop;
            if ((b.shape == "door" || b.shape == "tallplant") && part != "top" && b.partBottom >= 0) return b.partBottom;
            return axis == 1 ? (sign > 0 ? b.top : b.bottom) : b.side;
        }
        static int partTileOr(MDef b, JObj m, int fallback)
        {
            var part = m != null ? m.Str("part") : null;
            return part == "top" && b.partTop >= 0 ? b.partTop : part != "top" && b.partBottom >= 0 ? b.partBottom : fallback;
        }

        // ---- face geometry tables (f: 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z) ----
        static readonly int[][] DIRS = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        static readonly int[] FACE_AXIS = { 0, 0, 1, 1, 2, 2 }, FACE_SIGN = { 1, -1, 1, -1, 1, -1 };
        // p offset, du, dv per face (unit block)
        static readonly double[][] FACE_P = { new double[] { 1, 0, 0 }, new double[] { 0, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 0 }, new double[] { 0, 0, 1 }, new double[] { 0, 0, 0 } };
        static readonly double[][] FACE_DU = { new double[] { 0, 1, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 }, new double[] { 0, 0, 1 }, new double[] { 1, 0, 0 }, new double[] { 1, 0, 0 } };
        static readonly double[][] FACE_DV = { new double[] { 0, 0, 1 }, new double[] { 0, 0, 1 }, new double[] { 1, 0, 0 }, new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 1, 0 } };

        void vQuadFace(VBuf v, IBuf i, MDef b, JObj m, int x, int y, int z, int f, double alpha = 1, int tileOverride = -1)
        {
            int axis = FACE_AXIS[f], sign = FACE_SIGN[f]; var d = DIRS[f];
            int L = packedLight(x + d[0], y + d[1], z + d[2]);
            var p = FACE_P[f]; var du = FACE_DU[f]; var dv = FACE_DV[f];
            quad(v, i, x + p[0], y + p[1], z + p[2], du[0], du[1], du[2], dv[0], dv[1], dv[2], tileOverride < 0 ? vTile(b, axis, sign, m) : tileOverride,
                sign > 0 ? SHADE_POS[axis] : SHADE_NEG[axis], alpha, sign < 0, faceUV(axis, sign, 1, 1), sky(L), blk(L), 0, false);
        }
        void vBox(VBuf v, IBuf i, MDef b, JObj m, int x, int y, int z, double[] q, double alpha = 1)
        {
            double x0 = (x + q[0]), y0 = (y + q[1]), z0 = (z + q[2]), x1 = (x + q[3]), y1 = (y + q[4]), z1 = (z + q[5]),
                sx = x1 - x0, sy = y1 - y0, sz = z1 - z0;
            int L = packedLight(x, y, z), side = vTile(b, 0, 1, m), top = vTile(b, 1, 1, m), bottom = vTile(b, 1, -1, m);
            double ls = sky(L), lb = blk(L);
            quad(v, i, x1, y0, z0, 0, sy, 0, 0, 0, sz, side, 0.74, alpha, false, null, ls, lb, 0, false);
            quad(v, i, x0, y0, z0, 0, 0, sz, 0, sy, 0, side, 0.66, alpha, false, null, ls, lb, 0, false);
            quad(v, i, x0, y1, z0, 0, 0, sz, sx, 0, 0, top, 1, alpha, false, null, ls, lb, 0, false);
            quad(v, i, x0, y0, z0, sx, 0, 0, 0, 0, sz, bottom, 0.56, alpha, false, null, ls, lb, 0, false);
            quad(v, i, x0, y0, z1, sx, 0, 0, 0, sy, 0, side, 0.84, alpha, false, null, ls, lb, 0, false);
            quad(v, i, x0, y0, z0, 0, sy, 0, sx, 0, 0, side, 0.78, alpha, false, null, ls, lb, 0, false);
        }
        void vCross(VBuf v, IBuf i, MDef b, JObj m, int x, int y, int z, double alpha = 1)
        {
            int L = packedLight(x, y, z), t = partTileOr(b, m, b.top >= 0 ? b.top : b.side);
            quad(v, i, x + 0.15, y, z + 0.15, 0.7, 0, 0.7, 0, 1, 0, t, 0.95, alpha, false, null, sky(L), blk(L), 0, true);
            quad(v, i, x + 0.85, y, z + 0.15, -0.7, 0, 0.7, 0, 1, 0, t, 0.95, alpha, false, null, sky(L), blk(L), 0, true);
        }
        void thinQuad(VBuf v, IBuf i, int x, int y, int z, string facing, int t, double alpha, int cls)
        {
            int L = packedLight(x, y, z); const double e = 0.03;
            if (facing == "north") quad(v, i, x, y, z + e, 1, 0, 0, 0, 1, 0, t, 0.82, alpha, false, null, sky(L), blk(L), cls, true);
            else if (facing == "south") quad(v, i, x + 1, y, z + 1 - e, -1, 0, 0, 0, 1, 0, t, 0.82, alpha, false, null, sky(L), blk(L), cls, true);
            else if (facing == "west") quad(v, i, x + e, y, z + 1, 0, 0, -1, 0, 1, 0, t, 0.82, alpha, false, null, sky(L), blk(L), cls, true);
            else quad(v, i, x + 1 - e, y, z, 0, 0, 1, 0, 1, 0, t, 0.82, alpha, false, null, sky(L), blk(L), cls, true);
        }
        void vThin(VBuf v, IBuf i, MDef b, JObj m, int x, int y, int z, string facing, double alpha = 1) { thinQuad(v, i, x, y, z, facing, partTileOr(b, m, b.side), alpha, 0); }
        static string workerMount(JObj m, string fallback = "south")
        {
            var q = m != null ? m.Str("mount") : null;
            if (q == "floor" || q == "ceiling" || q == "west" || q == "east" || q == "north" || q == "south") return q;
            var f = m != null ? m.Str("facing") : null;
            return f == "west" || f == "east" || f == "north" || f == "south" ? f : fallback;
        }
        static int workerBit(JObj m)
        {
            var q = workerMount(m);
            return q == "south" ? 1 : q == "west" ? 2 : q == "north" ? 4 : q == "east" ? 8 : q == "ceiling" ? 16 : q == "floor" ? 32 : 0;
        }
        static int workerFaceMask(JObj m, int allowed = 63)
        {
            return (m != null && m.IsNum("faces") && JS.isFinite(m.Num("faces")) ? JS.toInt32(m.Num("faces")) : workerBit(m)) & allowed;
        }
        static double[] workerButtonBox(JObj m)
        {
            var q = workerMount(m);
            if (q == "floor") return new[] { 0.3125, 0, 0.375, 0.6875, 0.125, 0.625 };
            if (q == "ceiling") return new[] { 0.3125, 0.875, 0.375, 0.6875, 1, 0.625 };
            if (q == "west") return new[] { 0, 0.375, 0.3125, 0.125, 0.625, 0.6875 };
            if (q == "east") return new[] { 0.875, 0.375, 0.3125, 1, 0.625, 0.6875 };
            if (q == "north") return new[] { 0.3125, 0.375, 0, 0.6875, 0.625, 0.125 };
            return new[] { 0.3125, 0.375, 0.875, 0.6875, 0.625, 1 };
        }
        void emitAttachmentPlanes(VBuf v, IBuf i, int tile, int x, int y, int z, int mask, double alpha = 1, int cls = 0)
        {
            int L = packedLight(x, y, z); double ls = sky(L), lb = blk(L); const double e = 0.03;
            if ((mask & 1) != 0) quad(v, i, x + 1, y, z + 1 - e, -1, 0, 0, 0, 1, 0, tile, 0.82, alpha, false, null, ls, lb, cls, true);
            if ((mask & 2) != 0) quad(v, i, x + e, y, z + 1, 0, 0, -1, 0, 1, 0, tile, 0.82, alpha, false, null, ls, lb, cls, true);
            if ((mask & 4) != 0) quad(v, i, x, y, z + e, 1, 0, 0, 0, 1, 0, tile, 0.82, alpha, false, null, ls, lb, cls, true);
            if ((mask & 8) != 0) quad(v, i, x + 1 - e, y, z, 0, 0, 1, 0, 1, 0, tile, 0.82, alpha, false, null, ls, lb, cls, true);
            if ((mask & 16) != 0) quad(v, i, x, y + 1 - e, z, 1, 0, 0, 0, 0, 1, tile, 0.82, alpha, false, null, ls, lb, cls, true);
            if ((mask & 32) != 0) quad(v, i, x, y + e, z, 1, 0, 0, 0, 0, 1, tile, 0.82, alpha, false, null, ls, lb, cls, true);
        }
        void torchSprite(VBuf v, IBuf i, int id, int x, int y, int z, JObj m)
        {
            var b = def(id); int L = packedLight(x, y, z), tile = b.front >= 0 ? b.front : b.side; var q = workerMount(m, "floor");
            double cx = 0.5, cz = 0.5, y0 = 0;
            if (q != "floor") y0 = 0.15;
            if (q == "west") cx = 0.18; else if (q == "east") cx = 0.82; else if (q == "north") cz = 0.18; else if (q == "south") cz = 0.82;
            const double r = 0.12, h = 0.7; int cls = xrayClass(id);
            quad(v, i, x + cx - r, y + y0, z + cz - r, r * 2, 0, r * 2, 0, h, 0, tile, 0.95, 1, false, null, sky(L), blk(L), cls, true);
            quad(v, i, x + cx + r, y + y0, z + cz - r, -r * 2, 0, r * 2, 0, h, 0, tile, 0.95, 1, false, null, sky(L), blk(L), cls, true);
        }
        List<double[]> vStairBoxes(JObj m)
        {
            var f = orStr(m, "facing", "south"); bool up = m != null && m.Bool("upper");
            return boxes(up ? BOX_SLAB_TOP : BOX_SLAB_BOT, STAIR_STEP[up ? 1 : 0][0][faceIndexForFacing(f)]);
        }
        static readonly double[][][] FENCE_ARMS = {
            new[] { new[] { 1.0, 0 }, new[] { 0.625, 0.375, 0.4375, 1, 0.5625, 0.5625 }, new[] { 0.625, 0.75, 0.4375, 1, 0.9375, 0.5625 } },
            new[] { new[] { -1.0, 0 }, new[] { 0, 0.375, 0.4375, 0.375, 0.5625, 0.5625 }, new[] { 0, 0.75, 0.4375, 0.375, 0.9375, 0.5625 } },
            new[] { new[] { 0.0, 1 }, new[] { 0.4375, 0.375, 0.625, 0.5625, 0.5625, 1 }, new[] { 0.4375, 0.75, 0.625, 0.5625, 0.9375, 1 } },
            new[] { new[] { 0.0, -1 }, new[] { 0.4375, 0.375, 0, 0.5625, 0.5625, 0.375 }, new[] { 0.4375, 0.75, 0, 0.5625, 0.9375, 0.375 } } };
        static readonly double[][][] WALL_ARMS = {
            new[] { new[] { 1.0, 0 }, new[] { 0.6875, 0, 0.3125, 1, 1, 0.6875 } }, new[] { new[] { -1.0, 0 }, new[] { 0, 0, 0.3125, 0.3125, 1, 0.6875 } },
            new[] { new[] { 0.0, 1 }, new[] { 0.3125, 0, 0.6875, 0.6875, 1, 1 } }, new[] { new[] { 0.0, -1 }, new[] { 0.3125, 0, 0, 0.6875, 1, 0.3125 } } };
        static readonly double[][][] PANE_ARMS = {
            new[] { new[] { 1.0, 0 }, new[] { 0.5625, 0, 0.4375, 1, 1, 0.5625 } }, new[] { new[] { -1.0, 0 }, new[] { 0, 0, 0.4375, 0.4375, 1, 0.5625 } },
            new[] { new[] { 0.0, 1 }, new[] { 0.4375, 0, 0.5625, 0.5625, 1, 1 } }, new[] { new[] { 0.0, -1 }, new[] { 0.4375, 0, 0, 0.5625, 1, 0.4375 } } };
        List<double[]> connectBoxes(string sp, int x, int y, int z)
        {
            if (sp == "fence")
            {
                var a = boxes(BOX_FENCE_POST);
                foreach (var q in FENCE_ARMS) if (virtualConnects(x + (int)q[0][0], y, z + (int)q[0][1])) { a.Add(q[1]); a.Add(q[2]); }
                return a;
            }
            if (sp == "wall")
            {
                var a = boxes(BOX_WALL_POST);
                foreach (var q in WALL_ARMS) if (virtualConnects(x + (int)q[0][0], y, z + (int)q[0][1])) a.Add(q[1]);
                return a;
            }
            {
                var a = boxes(BOX_PANE_POST);
                foreach (var q in PANE_ARMS) if (virtualConnects(x + (int)q[0][0], y, z + (int)q[0][1])) a.Add(q[1]);
                return a;
            }
        }
        List<double[]> vShapeBoxes(MDef b, JObj m, int x, int y, int z)
        {
            var sp = b.sp;
            if (m != null && m.Bool("bedPart")) return boxes(BOX_BED);
            if (sp == "slab") return boxes(m != null && m.Bool("upper") ? BOX_SLAB_TOP : BOX_SLAB_BOT);
            if (sp == "stairs") return vStairBoxes(m);
            if (sp == "fence" || sp == "wall" || sp == "pane") return connectBoxes(sp, x, y, z);
            if (sp == "gate")
            {
                var f = orStr(m, "facing", "south"); bool axis = f == "east" || f == "west";
                var posts = boxes(axis ? GATE_POSTS_X[0] : GATE_POSTS_Z[0], axis ? GATE_POSTS_X[1] : GATE_POSTS_Z[1]);
                if (m != null && m.Bool("open")) return posts;
                var rails = axis ? GATE_RAILS_X : GATE_RAILS_Z; posts.Add(rails[0]); posts.Add(rails[1]);
                return posts;
            }
            if (sp == "trapdoor")
            {
                if (m != null && m.Bool("open")) return boxes(TRAP_OPEN[faceIndexForFacing(orStr(m, "facing", "south"))]);
                return boxes(m != null && m.Bool("upper") ? BOX_TRAP_TOP : BOX_TRAP_BOT);
            }
            if (sp == "carpet" || sp == "lilypad" || sp == "rail") return boxes(BOX_CARPET);
            if (sp == "plate") return boxes(m != null && m.Bool("pressed") ? BOX_PLATE_DOWN : BOX_PLATE);
            if (sp == "button") return boxes(workerButtonBox(m));
            if (sp == "seapickle") return boxes(SEA_PICKLES[0]);
            if (sp == "bamboo") return boxes(BOX_BAMBOO);
            if (sp == "pot") return boxes(BOX_POT);
            return null;
        }
        static string rotateDoorOpen(string f) { return f == "north" ? "east" : f == "east" ? "south" : f == "south" ? "west" : f == "west" ? "north" : f; }
        void emitVirtualBlock(VBuf ov, IBuf oi, VBuf cv, IBuf ci, VBuf tv, IBuf ti, int id, MDef b, JObj m, int x, int y, int z)
        {
            VBuf v; IBuf i;
            if (b.cutout || b.plant || b.waterPlant) { v = cv; i = ci; } else if (b.transparent) { v = tv; i = ti; } else { v = ov; i = oi; }
            var sp = b.sp; double alpha = b.alpha;
            if (sp == "cross" || sp == "tallplant" || b.plant) { vCross(v, i, b, m, x, y, z, alpha); return; }
            if (sp == "door")
            {
                var f = orStr(m, "facing", "south");
                if (m.Bool("open")) f = rotateDoorOpen(f);
                vThin(v, i, b, m, x, y, z, f, alpha); return;
            }
            if (sp == "vine" || sp == "lichen")
            {
                int t = partTileOr(b, m, b.side);
                emitAttachmentPlanes(v, i, t, x, y, z, workerFaceMask(m, sp == "vine" ? 15 : 63), alpha, 0); return;
            }
            if (sp == "rail")
            {
                int L = packedLight(x, y, z), t = b.top >= 0 ? b.top : b.side;
                quadPoints(v, i, x, y + 0.03, z, x + 1, y + 0.03, z, x, y + 0.03, z + 1, x + 1, y + 0.03, z + 1, t, 0.96, alpha, false, null, sky(L), blk(L), 0);
                return;
            }
            if (sp == "pot")
            {
                var bx = vShapeBoxes(b, m, x, y, z);
                if (bx != null) foreach (var q in bx) vBox(v, i, b, m, x, y, z, q, alpha);
                var pk = m.Str("potKey"); if (!string.IsNullOrEmpty(pk)) emitPottedPlant(cv, ci, pk, x, y, z);
                return;
            }
            var boxes = vShapeBoxes(b, m, x, y, z);
            if (boxes != null) { foreach (var q in boxes) vBox(v, i, b, m, x, y, z, q, alpha); return; }
            if (b.axislog)
            {
                var axis = orStr(m, "axis", "y");
                for (int f = 0; f < 6; f++)
                {
                    var d = DIRS[f]; var nd = actualDefAt(x + d[0], y + d[1], z + d[2]);
                    if (nd != null && nd.solid && !nd.transparent && !nd.cutout && !nd.plant && !nd.waterPlant && nd.special == null && nd.shape == null) continue;
                    string fa = f < 2 ? "x" : f < 4 ? "y" : "z";
                    vQuadFace(v, i, b, m, x, y, z, f, alpha, fa == axis ? b.top : b.side);
                }
                return;
            }
            var mv = m.Str("v");
            for (int f = 0; f < 6; f++)
            {
                var d = DIRS[f]; int nid = at(x + d[0], y + d[1], z + d[2]); var nm = vmeta(x + d[0], y + d[1], z + d[2]); var nd = actualDefAt(x + d[0], y + d[1], z + d[2]);
                if (isVirtual(nid) && nm.Str("v") == mv && (b.transparent || b.cutout)) continue;
                if (nd != null && nd.solid && !nd.transparent && !nd.cutout && !nd.plant && !nd.waterPlant && nd.special == null && nd.shape == null) continue;
                vQuadFace(v, i, b, m, x, y, z, f, alpha);
            }
        }

        bool isWater(int id)
        {
            if (id == B.WATER || id == B.WATER_FALLING || id == B.FLOW7 || id == B.FLOW6 || id == B.FLOW5 || id == B.FLOW4 || id == B.FLOW3 || id == B.FLOW2 || id == B.FLOW1) return true;
            var b = def(id); return b != null && b.waterPlant;
        }
        static bool isFluidWater(int id) { return id == B.WATER || id == B.WATER_FALLING || id == B.FLOW7 || id == B.FLOW6 || id == B.FLOW5 || id == B.FLOW4 || id == B.FLOW3 || id == B.FLOW2 || id == B.FLOW1; }
        static bool isLava(int id) { return id == B.LAVA || id == B.LAVA_FLOW2 || id == B.LAVA_FLOW1; }
        int packedLight(int x, int y, int z)
        {
            if (y < 0 || y >= H) return 15 << 4;
            int rx = x + PAD, rz = z + PAD;
            if (!inside(rx, y, rz)) return 15 << 4;
            return lightRegion[rIndex(rx, y, rz)];
        }
        static double sky(int L) { return ((L >> 4) & 15) / 15.0; }
        static double blk(int L) { return (L & 15) / 15.0; }
        int faceTile(int id, int axis, int sign)
        {
            var b = def(id);
            if (b == null) return 0;
            return axis == 1 ? (sign > 0 ? b.top : b.bottom) : b.side;
        }
        readonly double[] uvScratch = new double[8];
        double[] faceUV(int axis, int sign, double u, double v)
        {
            var o = uvScratch;
            if (axis == 0)
            {
                if (sign > 0) { o[0] = 0; o[1] = 0; o[2] = 0; o[3] = u; o[4] = v; o[5] = 0; o[6] = v; o[7] = u; }
                else { o[0] = v; o[1] = 0; o[2] = v; o[3] = u; o[4] = 0; o[5] = 0; o[6] = 0; o[7] = u; }
                return o;
            }
            if (axis == 1)
            {
                if (sign > 0) { o[0] = 0; o[1] = u; o[2] = 0; o[3] = 0; o[4] = v; o[5] = u; o[6] = v; o[7] = 0; }
                else { o[0] = 0; o[1] = 0; o[2] = 0; o[3] = u; o[4] = v; o[5] = 0; o[6] = v; o[7] = u; }
                return o;
            }
            if (sign > 0) { o[0] = 0; o[1] = 0; o[2] = u; o[3] = 0; o[4] = 0; o[5] = v; o[6] = u; o[7] = v; }
            else { o[0] = u; o[1] = 0; o[2] = 0; o[3] = 0; o[4] = u; o[5] = v; o[6] = 0; o[7] = v; }
            return o;
        }
        static readonly double[] DEFAULT_UV = { 0, 0, 1, 0, 0, 1, 1, 1 };
        static int round15(double x) { int r = (int)Math.Floor(x * 15 + 0.5); return r < 0 ? 0 : r > 15 ? 15 : r; }
        static double packSA(double shade, double alpha, int cls)
        {
            int a8 = (int)Math.Max(0, Math.Min(255, Math.Floor(alpha * 255 + 0.5)));
            return (float)(shade + a8 * 2 + (cls != 0 ? 512 : 0));
        }
        static void putV(VBuf v, double x, double y, double z, double u, double w, int tile, double sa, int light)
        {
            var a = v.a; int n = v.n;
            a[n].x = (float)x; a[n].y = (float)y; a[n].z = (float)z; a[n].sa = (float)sa; a[n].u = Half.FromFloat((float)u); a[n].v = Half.FromFloat((float)w); a[n].tile = Half.FromInt(tile); a[n].light = Half.FromInt(light);
            v.n = n + 1;
        }
        void quad(VBuf v, IBuf i, double px, double py, double pz, double dux, double duy, double duz, double dvx, double dvy, double dvz, int tile, double shade, double alpha, bool flip, double[] uv, double ls, double lb, int cls, bool two)
        {
            int n = v.n; var tc = uv ?? DEFAULT_UV; double sa = packSA(shade, alpha, cls); int light = round15(ls) * 16 + round15(lb);
            v.Ensure(4);
            putV(v, px, py, pz, tc[0], tc[1], tile, sa, light);
            putV(v, px + dux, py + duy, pz + duz, tc[2], tc[3], tile, sa, light);
            putV(v, px + dvx, py + dvy, pz + dvz, tc[4], tc[5], tile, sa, light);
            putV(v, px + dux + dvx, py + duy + dvy, pz + duz + dvz, tc[6], tc[7], tile, sa, light);
            if (flip) i.Add6(n, n + 2, n + 1, n + 1, n + 2, n + 3); else i.Add6(n, n + 1, n + 2, n + 2, n + 1, n + 3);
            if (two) { if (flip) i.Add6(n, n + 1, n + 2, n + 2, n + 1, n + 3); else i.Add6(n, n + 2, n + 1, n + 1, n + 2, n + 3); }
        }
        /// <summary>quad with per-corner sky/block light (greedy faces).</summary>
        void quadL(VBuf v, IBuf i, double px, double py, double pz, double dux, double duy, double duz, double dvx, double dvy, double dvz, int tile, double shade, double alpha, bool flip, double[] uv, double[] ls, double[] lb, int cls)
        {
            int n = v.n; var tc = uv ?? DEFAULT_UV; double sa = packSA(shade, alpha, cls);
            v.Ensure(4);
            putV(v, px, py, pz, tc[0], tc[1], tile, sa, round15(ls[0]) * 16 + round15(lb[0]));
            putV(v, px + dux, py + duy, pz + duz, tc[2], tc[3], tile, sa, round15(ls[1]) * 16 + round15(lb[1]));
            putV(v, px + dvx, py + dvy, pz + dvz, tc[4], tc[5], tile, sa, round15(ls[2]) * 16 + round15(lb[2]));
            putV(v, px + dux + dvx, py + duy + dvy, pz + duz + dvz, tc[6], tc[7], tile, sa, round15(ls[3]) * 16 + round15(lb[3]));
            if (flip) i.Add6(n, n + 2, n + 1, n + 1, n + 2, n + 3); else i.Add6(n, n + 1, n + 2, n + 2, n + 1, n + 3);
        }
        void quadPoints(VBuf v, IBuf i, double x0, double y0, double z0, double x1, double y1, double z1, double x2, double y2, double z2, double x3, double y3, double z3,
            int tile, double shade, double alpha, bool flip, double[] uv, double ls, double lb, int cls)
        {
            int n = v.n; var tc = uv ?? DEFAULT_UV; double sa = packSA(shade, alpha, cls); int light = round15(ls) * 16 + round15(lb);
            v.Ensure(4);
            putV(v, x0, y0, z0, tc[0], tc[1], tile, sa, light);
            putV(v, x1, y1, z1, tc[2], tc[3], tile, sa, light);
            putV(v, x2, y2, z2, tc[4], tc[5], tile, sa, light);
            putV(v, x3, y3, z3, tc[6], tc[7], tile, sa, light);
            if (flip) i.Add6(n, n + 2, n + 1, n + 1, n + 2, n + 3); else i.Add6(n, n + 1, n + 2, n + 2, n + 1, n + 3);
        }
        readonly double[] flSS = new double[4], flBB = new double[4];
        readonly int[] flP = new int[3];
        void faceLightSection(int axis, int sign, int[] x, int u, int v, int w, int h, int y0)
        {
            int dd = sign > 0 ? x[axis] : x[axis] - 1;
            for (int k = 0; k < 4; k++)
            {
                var p = flP; p[0] = x[0]; p[1] = x[1]; p[2] = x[2];
                p[axis] = dd; p[u] = (k & 1) == 0 ? x[u] : x[u] + w - 1; p[v] = k < 2 ? x[v] : x[v] + h - 1; p[1] += y0;
                int L = packedLight(p[0], p[1], p[2]);
                flSS[k] = ((L >> 4) & 15) / 15.0; flBB[k] = (L & 15) / 15.0;
            }
        }
        static int xrayClass(int id)
        {
            if (id == B.COAL || id == B.IRON || id == B.GOLD || id == B.DIAMOND || id == B.COPPER || id == B.REDSTONE_ORE || id == B.LAPIS_ORE || id == B.EMERALD_ORE ||
                id == B.DEEPSLATE_COAL_ORE || id == B.DEEPSLATE_IRON_ORE || id == B.DEEPSLATE_GOLD_ORE || id == B.DEEPSLATE_DIAMOND_ORE || id == B.DEEPSLATE_REDSTONE_ORE ||
                id == B.DEEPSLATE_LAPIS_ORE || id == B.DEEPSLATE_EMERALD_ORE || id == B.DEEPSLATE_COPPER_ORE) return 2;
            if (id == B.LAVA || id == B.LAVA_FLOW2 || id == B.LAVA_FLOW1 || id == B.RAIL || id == B.COBWEB || id == B.CHEST || id == B.PLANKS || id == B.OAK_FENCE) return 1;
            return 0;
        }
        static readonly int[] XRAY = BuildXray();
        static int[] BuildXray() { var a = new int[256]; for (int i = 0; i < 256; i++) a[i] = xrayClass(i); return a; }
        bool[] greedySolidTable;
        bool isGreedySolid(int id)
        {
            if (greedySolidTable == null)
            {
                var t = new bool[256];
                for (int k = 0; k < 256; k++)
                {
                    if (isVirtual(k)) continue;
                    var b = def(k);
                    t[k] = k != B.AIR && b != null && b.solid && !b.transparent && !b.cutout && !b.plant && !b.waterPlant && b.special == null && !axisLogId(k);
                }
                greedySolidTable = t;
            }
            return greedySolidTable[id];
        }
        readonly int[] greedyMask = new int[C * C];
        readonly int[] gx = new int[3], gq = new int[3];
        void greedySection(VBuf verts, IBuf inds, int si)
        {
            int y0 = si * 16; int[] dims = { C, 16, C }; var mask = greedyMask;
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                var q = gq; q[0] = q[1] = q[2] = 0; q[axis] = 1;
                var x = gx; x[0] = x[1] = x[2] = 0;
                for (x[axis] = -1; x[axis] < dims[axis];)
                {
                    int n = 0;
                    for (x[v] = 0; x[v] < dims[v]; x[v]++)
                        for (x[u] = 0; x[u] < dims[u]; x[u]++)
                        {
                            int ay = x[1] + y0, by = x[1] + q[1] + y0, a = at(x[0], ay, x[2]), b = at(x[0] + q[0], by, x[2] + q[2]);
                            int id = 0, sign = 0, lpx = 0, lpy = 0, lpz = 0;
                            int ca = XRAY[a], cb = XRAY[b]; bool sa = isGreedySolid(a), sb = isGreedySolid(b);
                            if (sa && (!sb || (ca == 2 && a != b)) && x[axis] >= 0) { id = a; sign = 1; lpx = x[0] + q[0]; lpy = x[1] + q[1]; lpz = x[2] + q[2]; }
                            else if (sb && (!sa || (cb == 2 && a != b)) && x[axis] < dims[axis] - 1) { id = b; sign = -1; lpx = x[0]; lpy = x[1]; lpz = x[2]; }
                            if (id != 0)
                            {
                                int L = packedLight(lpx, lpy + y0, lpz), key = id | ((((L >> 4) & 15) >> 2) << 8) | (((L & 15) >> 2) << 10);
                                mask[n++] = sign * key;
                            }
                            else mask[n++] = 0;
                        }
                    x[axis]++;
                    n = 0;
                    for (int j = 0; j < dims[v]; j++)
                        for (int ii = 0; ii < dims[u];)
                        {
                            int m = mask[n];
                            if (m == 0) { ii++; n++; continue; }
                            int w = 1;
                            while (ii + w < dims[u] && mask[n + w] == m) w++;
                            int h = 1;
                            for (; j + h < dims[v]; h++)
                            {
                                bool ok = true;
                                for (int k = 0; k < w; k++) if (mask[n + k + h * dims[u]] != m) { ok = false; break; }
                                if (!ok) break;
                            }
                            x[u] = ii; x[v] = j;
                            double dux = 0, duy = 0, duz = 0, dvx = 0, dvy = 0, dvz = 0;
                            if (u == 0) dux = w; else if (u == 1) duy = w; else duz = w;
                            if (v == 0) dvx = h; else if (v == 1) dvy = h; else dvz = h;
                            int key = Math.Abs(m), id = key & 255, sign = m > 0 ? 1 : -1;
                            faceLightSection(axis, sign, x, u, v, w, h, y0);
                            quadL(verts, inds, x[0], x[1] + y0, x[2], dux, duy, duz, dvx, dvy, dvz, faceTile(id, axis, sign), sign > 0 ? SHADE_POS[axis] : SHADE_NEG[axis], 1, sign < 0,
                                faceUV(axis, sign, w, h), flSS, flBB, XRAY[id]);
                            for (int hh = 0; hh < h; hh++) for (int k = 0; k < w; k++) mask[n + k + hh * dims[u]] = 0;
                            ii += w; n += w;
                        }
                }
            }
        }
        void emitFace(VBuf v, IBuf i, int id, int x, int y, int z, int f, double alpha = 1, int tileOverride = -1, double[] uvOverride = null)
        {
            int axis = FACE_AXIS[f], sign = FACE_SIGN[f]; var d = DIRS[f];
            int L = packedLight(x + d[0], y + d[1], z + d[2]);
            var p = FACE_P[f]; var du = FACE_DU[f]; var dv = FACE_DV[f];
            quad(v, i, x + p[0], y + p[1], z + p[2], du[0], du[1], du[2], dv[0], dv[1], dv[2], tileOverride < 0 ? faceTile(id, axis, sign) : tileOverride,
                sign > 0 ? SHADE_POS[axis] : SHADE_NEG[axis], alpha, sign < 0, uvOverride ?? faceUV(axis, sign, 1, 1), sky(L), blk(L), XRAY[id], false);
        }
        readonly double[] rotUV = new double[8];
        double[] rotateFaceUV(double[] uv, int turns)
        {
            turns &= 3;
            if (turns == 0) return uv;
            for (int k = 0; k < 4; k++)
            {
                double u = uv[k * 2], w = uv[k * 2 + 1];
                for (int r = 0; r < turns; r++) { double q = u; u = 1 - w; w = q; }
                rotUV[k * 2] = u; rotUV[k * 2 + 1] = w;
            }
            return rotUV;
        }
        static readonly string[] CHEST_NAMES = { "east", "west", "up", "down", "south", "north" };
        void emitChest(VBuf v, IBuf i, int id, int x, int y, int z, MDef b, JObj m)
        {
            bool paired = m.Bool("chestDouble"); var facing = orStr(m, "facing", "south"); bool axisOdd = facing == "west" || facing == "east";
            int facingIndex = facing == "west" ? 1 : facing == "north" ? 2 : facing == "east" ? 3 : 0;
            int pdx = JS.toInt32(m.Num("pairDX")), pdz = JS.toInt32(m.Num("pairDZ"));
            for (int f = 0; f < 6; f++)
            {
                var d = DIRS[f];
                if (paired && d[1] == 0 && d[0] == pdx && d[2] == pdz) continue;
                int tile = -1; double[] uv = null;
                if (f == 2 && paired)
                {
                    tile = m.Bool("chestSide") ? (b.topL >= 0 ? b.topL : b.top) : (b.topR >= 0 ? b.topR : b.top);
                    uv = rotateFaceUV(faceUV(1, 1, 1, 1), (2 - facingIndex) & 3);
                }
                else if (CHEST_NAMES[f] == facing)
                {
                    if (paired) tile = m.Bool("chestSide") != axisOdd ? (b.frontL >= 0 ? b.frontL : b.front) : (b.frontR >= 0 ? b.frontR : b.front);
                    else tile = b.front;
                }
                emitFace(v, i, id, x, y, z, f, b.alpha, tile, uv);
            }
        }
        void crossPlant(VBuf v, IBuf i, int id, int x, int y, int z, double alpha = 1)
        {
            var b = def(id); int L = packedLight(x, y, z), t = b.top >= 0 ? b.top : b.side;
            int cropStage = id >= B.WHEAT0 && id <= B.WHEAT3 ? id - B.WHEAT0 : id >= B.CARROTS0 && id <= B.CARROTS3 ? id - B.CARROTS0 : id >= B.POTATOES0 && id <= B.POTATOES3 ? id - B.POTATOES0
                : id >= B.PUMPKIN_STEM0 && id <= B.PUMPKIN_STEM3 ? id - B.PUMPKIN_STEM0 : id >= B.MELON_STEM0 && id <= B.MELON_STEM3 ? id - B.MELON_STEM0 : -1;
            double h = cropStage == 0 ? 0.35 : cropStage == 1 ? 0.55 : cropStage == 2 ? 0.76 : 1;
            int cls = XRAY[id];
            quad(v, i, x + 0.15, y, z + 0.15, 0.7, 0, 0.7, 0, h, 0, t, 0.95, alpha, false, null, sky(L), blk(L), cls, true);
            quad(v, i, x + 0.85, y, z + 0.15, -0.7, 0, 0.7, 0, h, 0, t, 0.95, alpha, false, null, sky(L), blk(L), cls, true);
        }
        static readonly double[] WATER_VIS_H = { 0, 1 / 9.0, 2 / 9.0, 3 / 9.0, 4 / 9.0, 5 / 9.0, 6 / 9.0, 7 / 9.0, 8 / 9.0 };
        static readonly double[] LAVA_VIS_H = { 0, 0.3, 0.55, 0.875 };
        static readonly double[] FLUID_FACE_SHADE = { 0.72, 0.72, 1, 0.55, 0.82, 0.82 };
        static int waterVisLevel(int id)
        {
            if (id == B.WATER || id == B.WATER_FALLING) return 8;
            if (id == B.FLOW7) return 7; if (id == B.FLOW6) return 6; if (id == B.FLOW5) return 5; if (id == B.FLOW4) return 4;
            if (id == B.FLOW3) return 3; if (id == B.FLOW2) return 2; if (id == B.FLOW1) return 1;
            return 0;
        }
        static int lavaVisLevel(int id) { return id == B.LAVA ? 3 : id == B.LAVA_FLOW2 ? 2 : id == B.LAVA_FLOW1 ? 1 : 0; }
        static bool mainLeaf(int id) { return id == B.LEAVES || id == B.BIRCH_LEAVES || id == B.SPRUCE_LEAVES || id == B.DARK_LEAVES || id == B.JUNGLE_LEAVES || id == B.ACACIA_LEAVES; }
        // Same face-occlusion classes as main's fluid mesher.
        bool mainFaceOpaque(int id)
        {
            var b = def(id);
            return b != null && id != B.AIR && id != B.GLASS && !mainLeaf(id) && id != B.CACTUS && !isFluidWater(id) && !b.plant && !b.waterPlant && b.special == null && !b.transparent && !b.cutout;
        }
        bool mainFluidFaceVisible(int cur, int nb)
        {
            var bd = def(nb);
            if (isFluidWater(cur)) return nb == B.AIR || (!mainFaceOpaque(nb) && !isFluidWater(nb) && !(bd != null && bd.waterPlant));
            if (mainFaceOpaque(cur)) return !mainFaceOpaque(nb);
            return !mainFaceOpaque(nb) && nb != cur;
        }
        bool connectedWaterAt(int x, int y, int z) { int id = at(x, y, z); var bd = def(id); return isFluidWater(id) || (bd != null && bd.waterPlant); }
        bool aquaticWaterVolumeActive(int x, int y, int z, MDef b, JObj m)
        {
            return b.needsWater || m.Bool("waterlogged") || connectedWaterAt(x, y + 1, z) || connectedWaterAt(x + 1, y, z) || connectedWaterAt(x - 1, y, z) || connectedWaterAt(x, y, z + 1) || connectedWaterAt(x, y, z - 1);
        }
        bool minecraftWaterCellAt(int x, int y, int z) { int id = at(x, y, z); var b = def(id); return isFluidWater(id) || (b != null && b.waterPlant); }
        double minecraftWaterHeightAt(int x, int y, int z)
        {
            int id = at(x, y, z); var b = def(id);
            if (!isFluidWater(id) && !(b != null && b.waterPlant)) return -1;
            if (id == B.WATER_FALLING || minecraftWaterCellAt(x, y + 1, z)) return 1;
            return isFluidWater(id) ? WATER_VIS_H[waterVisLevel(id)] : WATER_VIS_H[8];
        }
        static void minecraftWaterAddHeight(double h, ref double s, ref double w)
        {
            if (h < 0) return;
            double q = h >= 0.8 ? 10 : 1;
            s += h * q; w += q;
        }
        double minecraftWaterCornerAverage(double cur, double a, double b, int x, int y, int z)
        {
            // Vanilla-style calculateAverageHeight: diagonal is queried only if one orthogonal neighbour contains fluid/open space.
            if (cur >= 0.999 || a >= 0.999 || b >= 0.999) return 1;
            double s = 0, w = 0;
            if (a > 0 || b > 0)
            {
                double d = minecraftWaterHeightAt(x, y, z);
                if (d >= 0.999) return 1;
                minecraftWaterAddHeight(d, ref s, ref w);
            }
            minecraftWaterAddHeight(cur, ref s, ref w);
            minecraftWaterAddHeight(a, ref s, ref w);
            minecraftWaterAddHeight(b, ref s, ref w);
            return w != 0 ? (s / w) : 0;
        }
        bool minecraftWaterSideOccluded(int id) { var b = def(id); return b != null && b.solid && !b.transparent && !b.cutout && !b.plant && !b.waterPlant && b.special == null; }
        // Flat water tops are collected per section and greedily merged; UVs repeat per block via fract() in the water shader.
        struct FlatCell { public int x, y, z; public double h; public int lp; }
        readonly List<FlatCell> flatCells = new List<FlatCell>();
        VBuf flatCollectV;
        readonly int[] flatGrid = new int[C * C];
        readonly List<List<FlatCell>> flatGroups = new List<List<FlatCell>>(); readonly Dictionary<FlatKey, int> flatGroupIdx = new Dictionary<FlatKey, int>();
        // JS groups by the string q.y + "|" + h + "|" + lp; h is never -0/NaN, so the double's bits are an exact equivalent.
        struct FlatKey : IEquatable<FlatKey>
        {
            public int y, lp; public long hb;
            public bool Equals(FlatKey o) { return y == o.y && lp == o.lp && hb == o.hb; }
            public override bool Equals(object o) { return o is FlatKey && Equals((FlatKey)o); }
            public override int GetHashCode() { return (y * 397 ^ lp) * 397 ^ hb.GetHashCode(); }
        }
        void flushFlatWaterTops(VBuf v, IBuf i)
        {
            var collectV = flatCollectV; flatCollectV = null;
            if (collectV == null || flatCells.Count == 0) { flatCells.Clear(); return; }
            var wb = def(B.WATER); double alpha = wb != null ? wb.alpha : 0.62; int tile = faceTile(B.WATER, 1, 1);
            int a8 = (int)Math.Max(0, Math.Min(255, Math.Floor(alpha * 255 + 0.5))); double packed = FLUID_FACE_SHADE[2] + a8 * 2;
            // group by (y, h, lp) preserving first-seen order
            var groups = flatGroups; var gidx = flatGroupIdx; gidx.Clear(); int gn = 0; // pooled per instance
            foreach (var q in flatCells)
            {
                var k = new FlatKey { y = q.y, lp = q.lp, hb = BitConverter.DoubleToInt64Bits(q.h) };
                int gi; if (!gidx.TryGetValue(k, out gi)) { gi = gn++; gidx[k] = gi; if (gi == groups.Count) groups.Add(new List<FlatCell>()); else groups[gi].Clear(); }
                groups[gi].Add(q);
            }
            flatCells.Clear(); gidx.Clear();
            var grid = flatGrid; Array.Clear(grid, 0, grid.Length); int stamp = 0;
            for (int gix = 0; gix < gn; gix++)
            {
                var g = groups[gix]; stamp++;
                int y = g[0].y; double h = g[0].h; int lp = g[0].lp; double yy = y + h - 0.001;
                foreach (var q in g) grid[q.z * C + q.x] = stamp;
                for (int z = 0; z < C; z++)
                    for (int x = 0; x < C; x++)
                    {
                        if (grid[z * C + x] != stamp) continue;
                        int w = 1;
                        while (x + w < C && grid[z * C + x + w] == stamp) w++;
                        int d = 1;
                        while (z + d < C)
                        {
                            bool ok = true;
                            for (int k = 0; k < w; k++) if (grid[(z + d) * C + x + k] != stamp) { ok = false; break; }
                            if (!ok) break;
                            d++;
                        }
                        for (int dz = 0; dz < d; dz++) for (int k = 0; k < w; k++) grid[(z + dz) * C + x + k] = 0;
                        int n = v.n; var uv = faceUV(1, 1, d, w);
                        v.Ensure(4);
                        putV(v, x, yy, z, uv[0], uv[1], tile, packed, lp);
                        putV(v, x, yy, z + d, uv[2], uv[3], tile, packed, lp);
                        putV(v, x + w, yy, z, uv[4], uv[5], tile, packed, lp);
                        putV(v, x + w, yy, z + d, uv[6], uv[7], tile, packed, lp);
                        i.Add6(n, n + 1, n + 2, n + 2, n + 1, n + 3);
                    }
            }
        }
        void waterTopQuad(VBuf v, IBuf i, int x, int y, int z, double h00, double h10, double h01, double h11)
        {
            var wb = def(B.WATER); double alpha = wb != null ? wb.alpha : 0.62; int L = packedLight(x, y + 1, z);
            int tile = faceTile(B.WATER, 1, 1); var uv = faceUV(1, 1, 1, 1);
            int a8 = (int)Math.Max(0, Math.Min(255, Math.Floor(alpha * 255 + 0.5))); double packed = FLUID_FACE_SHADE[2] + a8 * 2;
            int sk = round15(sky(L)), bk = round15(blk(L));
            if (flatCollectV == v && h00 == h10 && h00 == h01 && h00 == h11 && x >= 0 && x < C && z >= 0 && z < C)
            {
                flatCells.Add(new FlatCell { x = x, y = y, z = z, h = h00, lp = sk * 16 + bk });
                return;
            }
            int n = v.n; v.Ensure(4);
            putV(v, x, y + h00 - 0.001, z, uv[0], uv[1], tile, packed, sk * 16 + bk);
            putV(v, x, y + h01 - 0.001, z + 1, uv[2], uv[3], tile, packed, sk * 16 + bk);
            putV(v, x + 1, y + h10 - 0.001, z, uv[4], uv[5], tile, packed, sk * 16 + bk);
            putV(v, x + 1, y + h11 - 0.001, z + 1, uv[6], uv[7], tile, packed, sk * 16 + bk);
            // Stable quad split, like Minecraft's fixed vertex order.
            i.Add6(n, n + 1, n + 2, n + 2, n + 1, n + 3);
        }
        void waterSideQuad(VBuf v, IBuf i, int x, int y, int z, int f, double h0, double h1)
        {
            var d = DIRS[f]; int nb = at(x + d[0], y + d[1], z + d[2]);
            if (minecraftWaterCellAt(x + d[0], y + d[1], z + d[2]) || minecraftWaterSideOccluded(nb)) return;
            const double eps = 0.001; double b0 = Math.Max(0, h0 - eps), b1 = Math.Max(0, h1 - eps);
            int axis, sign;
            var wb = def(B.WATER); double alpha = wb != null ? wb.alpha : 0.62; int L = packedLight(x + d[0], y, z + d[2]);
            if (f == 0) { axis = 0; sign = 1; quadPoints(v, i, x + 1, y, z, x + 1, y + b0, z, x + 1, y, z + 1, x + 1, y + b1, z + 1, faceTile(B.WATER, axis, sign), FLUID_FACE_SHADE[f], alpha, sign < 0, faceUV(axis, sign, 1, 1), sky(L), blk(L), XRAY[B.WATER]); }
            else if (f == 1) { axis = 0; sign = -1; quadPoints(v, i, x, y, z, x, y + b0, z, x, y, z + 1, x, y + b1, z + 1, faceTile(B.WATER, axis, sign), FLUID_FACE_SHADE[f], alpha, sign < 0, faceUV(axis, sign, 1, 1), sky(L), blk(L), XRAY[B.WATER]); }
            else if (f == 4) { axis = 2; sign = 1; quadPoints(v, i, x, y, z + 1, x + 1, y, z + 1, x, y + b0, z + 1, x + 1, y + b1, z + 1, faceTile(B.WATER, axis, sign), FLUID_FACE_SHADE[f], alpha, sign < 0, faceUV(axis, sign, 1, 1), sky(L), blk(L), XRAY[B.WATER]); }
            else { axis = 2; sign = -1; quadPoints(v, i, x, y, z, x + 1, y, z, x, y + b0, z, x + 1, y + b1, z, faceTile(B.WATER, axis, sign), FLUID_FACE_SHADE[f], alpha, sign < 0, faceUV(axis, sign, 1, 1), sky(L), blk(L), XRAY[B.WATER]); }
        }
        void emitMinecraftWater(VBuf v, IBuf i, int x, int y, int z, int id)
        {
            bool aboveWater = minecraftWaterCellAt(x, y + 1, z);
            if (aboveWater)
            {
                // Fully submerged cell: top is hidden; only exposed outer sides can survive the side cull.
                waterSideQuad(v, i, x, y, z, 0, 1, 1); waterSideQuad(v, i, x, y, z, 1, 1, 1); waterSideQuad(v, i, x, y, z, 4, 1, 1); waterSideQuad(v, i, x, y, z, 5, 1, 1);
                return;
            }
            double hc = minecraftWaterHeightAt(x, y, z), hn = minecraftWaterHeightAt(x, y, z - 1), hs = minecraftWaterHeightAt(x, y, z + 1),
                hw = minecraftWaterHeightAt(x - 1, y, z), he = minecraftWaterHeightAt(x + 1, y, z),
                h00 = minecraftWaterCornerAverage(hc, hn, hw, x - 1, y, z - 1), h10 = minecraftWaterCornerAverage(hc, hn, he, x + 1, y, z - 1),
                h01 = minecraftWaterCornerAverage(hc, hs, hw, x - 1, y, z + 1), h11 = minecraftWaterCornerAverage(hc, hs, he, x + 1, y, z + 1);
            waterTopQuad(v, i, x, y, z, h00, h10, h01, h11);
            waterSideQuad(v, i, x, y, z, 0, h10, h11);
            waterSideQuad(v, i, x, y, z, 1, h00, h01);
            waterSideQuad(v, i, x, y, z, 4, h01, h11);
            waterSideQuad(v, i, x, y, z, 5, h00, h10);
        }
        void emitWaterVolume(VBuf v, IBuf i, int x, int y, int z) { emitMinecraftWater(v, i, x, y, z, B.WATER); }
        void emitFluidWater(VBuf v, IBuf i, int id, int x, int y, int z) { emitMinecraftWater(v, i, x, y, z, id); }
        void emitLavaQuad(VBuf v, IBuf i, int id, int x, int y, int z, int f, double lo, double hi, double inset = 0)
        {
            if (hi <= lo + 0.0001) return;
            int axis = FACE_AXIS[f], sign = FACE_SIGN[f]; var d = DIRS[f];
            double px, py, pz, dux = 0, duy = 0, duz = 0, dvx = 0, dvy = 0, dvz = 0;
            if (f == 0) { px = x + 1; py = y + lo; pz = z; duy = hi - lo; dvz = 1; }
            else if (f == 1) { px = x; py = y + lo; pz = z; duy = hi - lo; dvz = 1; }
            else if (f == 2) { px = x; py = y + hi; pz = z; duz = 1; dvx = 1; }
            else if (f == 3) { px = x; py = y; pz = z; duz = 1; dvx = 1; }
            else if (f == 4) { px = x; py = y + lo; pz = z + 1; dux = 1; dvy = hi - lo; }
            else { px = x; py = y + lo; pz = z; dux = 1; dvy = hi - lo; }
            if (inset != 0) { px -= d[0] * inset; py -= d[1] * inset; pz -= d[2] * inset; }
            int L = packedLight(x + d[0], y + d[1], z + d[2]);
            quad(v, i, px, py, pz, dux, duy, duz, dvx, dvy, dvz, faceTile(id, axis, sign), FLUID_FACE_SHADE[f], 1, sign < 0, faceUV(axis, sign, 1, 1), sky(L), Math.Max(blk(L), 1), XRAY[id], false);
        }
        void emitFluidLava(VBuf v, IBuf i, int id, int x, int y, int z)
        {
            int lev = lavaVisLevel(id);
            if (lev == 0) return;
            int above = at(x, y + 1, z); double selfH = isLava(above) ? 1 : LAVA_VIS_H[lev];
            for (int f = 0; f < 6; f++)
            {
                var d = DIRS[f]; int nb = at(x + d[0], y + d[1], z + d[2]); double lo = 0;
                if (!mainFluidFaceVisible(id, nb))
                {
                    bool same = isLava(nb);
                    if (!same || f == 2 || f == 3 || selfH >= 1) continue;
                    int nab = at(x + d[0], y + 1, z + d[2]); double nh = isLava(nab) ? 1 : LAVA_VIS_H[lavaVisLevel(nb)];
                    if (selfH <= nh + 0.001) continue;
                    lo = nh;
                }
                double inset = nb != B.AIR && !isFluidWater(nb) && !isLava(nb) ? 0.004 : 0;
                emitLavaQuad(v, i, id, x, y, z, f, lo, selfH, inset);
            }
        }
        void thinPlane(VBuf v, IBuf i, int id, int x, int y, int z, string facing, double alpha = 1)
        {
            var b = def(id);
            thinQuad(v, i, x, y, z, facing, b.front >= 0 ? b.front : b.side, alpha, XRAY[id]);
        }
        public static double[] rotateBox(double[] b, string f)
        {
            var r = (double[])b.Clone(); int n = f == "west" ? 1 : f == "north" ? 2 : f == "east" ? 3 : 0;
            while (n-- > 0) r = new[] { 1 - r[5], r[1], r[0], 1 - r[2], r[4], r[3] };
            return r;
        }
        static bool axisLogId(int id) { return id == B.LOG || id == B.SPRUCE_LOG || id == B.BIRCH_LOG || id == B.JUNGLE_LOG || id == B.ACACIA_LOG || id == B.DARK_LOG; }
        static int faceIndexForFacing(string f) { return f == "west" ? 1 : f == "north" ? 2 : f == "east" ? 3 : 0; }
        static readonly int[][] MAIN_STAIR_DIR = { new[] { 0, 1 }, new[] { -1, 0 }, new[] { 0, -1 }, new[] { 1, 0 } };
        // Fixed box shapes are cached (read-only) and box lists are written into one reusable per-instance list: callers only iterate it immediately.
        static readonly double[] BOX_SLAB_TOP = { 0, 0.5, 0, 1, 1, 1.0 }, BOX_SLAB_BOT = { 0, 0, 0, 1, 0.5, 1.0 }, BOX_TRAP_TOP = { 0, 0.8125, 0, 1, 1, 1.0 }, BOX_TRAP_BOT = { 0, 0, 0, 1, 0.1875, 1.0 },
            BOX_CARPET = { 0, 0, 0, 1, 0.0625, 1.0 }, BOX_PLATE = { 0.0625, 0, 0.0625, 0.9375, 0.0625, 0.9375 }, BOX_PLATE_DOWN = { 0.0625, 0, 0.0625, 0.9375, 0.03125, 0.9375 },
            BOX_BAMBOO = { 0.4375, 0, 0.4375, 0.5625, 1, 0.5625 }, BOX_POT = { 0.3125, 0, 0.3125, 0.6875, 0.375, 0.6875 }, BOX_BED = { 0, 0, 0, 1, 0.56, 1 },
            BOX_FENCE_POST = { 0.375, 0, 0.375, 0.625, 1, 0.625 }, BOX_WALL_POST = { 0.25, 0, 0.25, 0.75, 1, 0.75 }, BOX_PANE_POST = { 0.4375, 0, 0.4375, 0.5625, 1, 0.5625 };
        static readonly double[][] SEA_PICKLES = { new[] { 0.375, 0, 0.375, 0.625, 0.375, 0.625 }, new[] { 0.18, 0, 0.22, 0.43, 0.3125, 0.47 }, new[] { 0.6, 0, 0.5, 0.85, 0.4375, 0.75 }, new[] { 0.32, 0, 0.62, 0.57, 0.3125, 0.87 } };
        static readonly double[][] GATE_POSTS_X = { new[] { 0.05, 0, 0.36, 0.18, 1, 0.64 }, new[] { 0.82, 0, 0.36, 0.95, 1, 0.64 } }, GATE_POSTS_Z = { new[] { 0.36, 0, 0.05, 0.64, 1, 0.18 }, new[] { 0.36, 0, 0.82, 0.64, 1, 0.95 } },
            GATE_OPEN_X = { new[] { 0.05, 0.34, 0.18, 0.42, 0.9, 0.34 }, new[] { 0.58, 0.34, 0.66, 0.95, 0.9, 0.82 } }, GATE_OPEN_Z = { new[] { 0.18, 0.34, 0.05, 0.34, 0.9, 0.42 }, new[] { 0.66, 0.34, 0.58, 0.82, 0.9, 0.95 } },
            GATE_RAILS_X = { new[] { 0.18, 0.34, 0.43, 0.82, 0.5, 0.57 }, new[] { 0.18, 0.7, 0.43, 0.82, 0.86, 0.57 } }, GATE_RAILS_Z = { new[] { 0.43, 0.34, 0.18, 0.57, 0.5, 0.82 }, new[] { 0.43, 0.7, 0.18, 0.57, 0.86, 0.82 } };
        static readonly string[] ROT_FACINGS = { "south", "west", "north", "east" };
        static double[][] rot4(double[] b) { var r = new double[4][]; for (int k = 0; k < 4; k++) r[k] = rotateBox(b, ROT_FACINGS[k]); return r; }
        static readonly double[][] TRAP_OPEN = rot4(new[] { 0, 0, 0.8125, 1, 1, 1.0 });
        // STAIR_STEP[up][kind][dir]: kind 0 straight step, 1/2 outer corner left/right, 3/4 inner corner extra left/right (rotateBox(q, facing) for dir = faceIndexForFacing(facing)).
        static readonly double[][][][] STAIR_STEP = { stairSteps(0.5, 1), stairSteps(0, 0.5) };
        static double[][][] stairSteps(double lo, double hi)
        {
            return new[] { rot4(new[] { 0, lo, 0.5, 1, hi, 1 }), rot4(new[] { 0, lo, 0.5, 0.5, hi, 1 }), rot4(new[] { 0.5, lo, 0.5, 1, hi, 1 }), rot4(new[] { 0, lo, 0, 0.5, hi, 0.5 }), rot4(new[] { 0.5, lo, 0, 1, hi, 0.5 }) };
        }
        readonly List<double[]> boxScratch = new List<double[]>(16);
        List<double[]> boxes(double[] a) { var o = boxScratch; o.Clear(); o.Add(a); return o; }
        List<double[]> boxes(double[] a, double[] b) { var o = boxScratch; o.Clear(); o.Add(a); o.Add(b); return o; }
        bool stairAt(int xx, int y, int zz) { var q = def(at(xx, y, zz)); return q != null && q.special == "stairs"; }
        bool stairGuard(int x, int y, int z, int wantDir, bool wantUp, int testDir)
        {
            var dd = MAIN_STAIR_DIR[testDir]; int xx = x + dd[0], zz = z + dd[1]; var mm = vmeta(xx, y, zz);
            return !stairAt(xx, y, zz) || faceIndexForFacing(orStr(mm, "facing", "south")) != wantDir || mm.Bool("upper") != wantUp;
        }
        List<double[]> stairBoxesMain(int id, int x, int y, int z, JObj m)
        {
            var facing = orStr(m, "facing", "south"); int dir = faceIndexForFacing(facing); bool up = m.Bool("upper");
            var parts = boxes(up ? BOX_SLAB_TOP : BOX_SLAB_BOT); var st = STAIR_STEP[up ? 1 : 0];
            int cornerType = 0; bool cornerLeft = false; // 0 none, 1 outer, 2 inner
            var d = MAIN_STAIR_DIR[dir]; int x1 = x + d[0], z1 = z + d[1]; var nm = vmeta(x1, y, z1);
            if (stairAt(x1, y, z1) && nm.Bool("upper") == up)
            {
                int nd = faceIndexForFacing(orStr(nm, "facing", "south"));
                if ((nd & 1) != (dir & 1) && stairGuard(x, y, z, dir, up, (nd + 2) & 3)) { cornerType = 1; cornerLeft = nd == ((dir + 1) & 3); }
            }
            if (cornerType == 0)
            {
                d = MAIN_STAIR_DIR[(dir + 2) & 3]; x1 = x + d[0]; z1 = z + d[1]; nm = vmeta(x1, y, z1);
                if (stairAt(x1, y, z1) && nm.Bool("upper") == up)
                {
                    int nd = faceIndexForFacing(orStr(nm, "facing", "south"));
                    if ((nd & 1) != (dir & 1) && stairGuard(x, y, z, dir, up, nd)) { cornerType = 2; cornerLeft = nd == ((dir + 1) & 3); }
                }
            }
            if (cornerType == 1) parts.Add(st[cornerLeft ? 1 : 2][dir]);
            else if (cornerType == 2) { parts.Add(st[0][dir]); parts.Add(st[cornerLeft ? 3 : 4][dir]); }
            else parts.Add(st[0][dir]);
            return parts;
        }
        sealed class WheelMask { public int[][] hub, disc, grip; }
        static WheelMask SHIP_WHEEL_MASK;
        static WheelMask shipWheelMask()
        {
            if (SHIP_WHEEL_MASK != null) return SHIP_WHEEL_MASK;
            const int size = 31; double c = (size - 1) / 2.0;
            var hub = new int[size][]; var disc = new int[size][]; var grip = new int[size][];
            Func<double, double, bool> spoke = (x, y) =>
            {
                for (int r = 0; r < 8; r++) { double a = (r * Math.PI) / 4, cs = Math.Cos(a), sn = Math.Sin(a); if (x * cs + y * sn > 0 && Math.Abs(y * cs - x * sn) <= 1.5) return true; }
                return false;
            };
            for (int y = 0; y < size; y++)
            {
                hub[y] = new int[size]; disc[y] = new int[size]; grip[y] = new int[size];
                for (int x = 0; x < size; x++)
                {
                    double dx = x - c, dy = y - c, ax = Math.Abs(dx), ay = Math.Abs(dy), rad = Math.Sqrt(dx * dx + dy * dy); bool sp = spoke(dx, dy);
                    hub[y][x] = ax <= 3 && ay <= 3 ? 1 : 0;
                    disc[y][x] = (rad >= 8.4 && rad <= 11.6) || (rad > 3 && rad < 8.8 && sp) ? 1 : 0;
                    grip[y][x] = rad > 11.6 && rad <= 15.5 && sp ? 1 : 0;
                }
            }
            return SHIP_WHEEL_MASK = new WheelMask { hub = hub, disc = disc, grip = grip };
        }
        static List<double[]> mergeWheelMask(int[][] mask)
        {
            int n = mask.Length; double c = (n - 1) / 2.0; var seen = new int[n, n]; var o = new List<double[]>();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (mask[y][x] != 0 && seen[y, x] == 0)
                    {
                        int x1 = x;
                        while (x1 + 1 < n && mask[y][x1 + 1] != 0 && seen[y, x1 + 1] == 0) x1++;
                        int y1 = y;
                        for (int yy = y + 1; yy < n; yy++)
                        {
                            bool ok = true;
                            for (int xx = x; xx <= x1; xx++) if (mask[yy][xx] == 0 || seen[yy, xx] != 0) { ok = false; break; }
                            if (!ok) break;
                            y1 = yy;
                        }
                        for (int yy = y; yy <= y1; yy++) for (int xx = x; xx <= x1; xx++) seen[yy, xx] = 1;
                        o.Add(new[] { x - c - 0.5, y - c - 0.5, x1 - c + 0.5, y1 - c + 0.5 });
                    }
            return o;
        }
        static readonly Dictionary<string, List<KeyValuePair<double[], int>>> wheelCache = new Dictionary<string, List<KeyValuePair<double[], int>>>();
        static List<KeyValuePair<double[], int>> shipWheelParts(JObj m)
        {
            var f = orStr(m, "facing", "south");
            lock (wheelCache)
            {
                List<KeyValuePair<double[], int>> cached;
                if (wheelCache.TryGetValue(f, out cached)) return cached;
                var parts = new List<KeyValuePair<double[], int>>(); double E = 0.379 / 15.5, j = 0.615;
                Action<double[], int> add = (b, id) => parts.Add(new KeyValuePair<double[], int>(rotateBox(b, f), id));
                add(new[] { 0.3, 0, 0.3, 0.7, 0.085, 0.7 }, B.SPRUCE_PLANKS);
                add(new[] { 0.35, 0.085, 0.35, 0.65, 0.15, 0.65 }, B.SPRUCE_PLANKS);
                add(new[] { 0.4, 0.15, 0.445, 0.6, 0.42, 0.555 }, B.SPRUCE_PLANKS);
                var q = shipWheelMask();
                foreach (var t in new[] { Tuple.Create(q.hub, 0.38, 0.62), Tuple.Create(q.disc, 0.42, 0.58), Tuple.Create(q.grip, 0.4, 0.6) })
                    foreach (var r in mergeWheelMask(t.Item1)) add(new[] { 0.5 + r[0] * E, j + r[1] * E, t.Item2, 0.5 + r[2] * E, j + r[3] * E, t.Item3 }, B.PLANKS);
                add(new[] { 0.5 - 2.5 * E, j - 2.5 * E, 0.37, 0.5 + 2.5 * E, j + 2.5 * E, 0.63 }, B.BIRCH_PLANKS);
                wheelCache[f] = parts;
                return parts;
            }
        }
        List<double[]> shapeBoxes(int id, int x, int y, int z, JObj m)
        {
            var b = def(id); var sp = b != null ? b.special : null;
            if (sp == "slab") return boxes(m.Bool("upper") ? BOX_SLAB_TOP : BOX_SLAB_BOT);
            if (sp == "stairs") return stairBoxesMain(id, x, y, z, m);
            if (sp == "fence" || sp == "wall" || sp == "pane") return connectBoxes(sp, x, y, z);
            if (sp == "gate")
            {
                var f = orStr(m, "facing", "south"); bool axis = f == "east" || f == "west";
                var posts = boxes(axis ? GATE_POSTS_X[0] : GATE_POSTS_Z[0], axis ? GATE_POSTS_X[1] : GATE_POSTS_Z[1]);
                var rails = m.Bool("open") ? (axis ? GATE_OPEN_X : GATE_OPEN_Z) : (axis ? GATE_RAILS_X : GATE_RAILS_Z); posts.Add(rails[0]); posts.Add(rails[1]);
                return posts;
            }
            if (sp == "trapdoor")
            {
                if (m.Bool("open")) return boxes(TRAP_OPEN[faceIndexForFacing(orStr(m, "facing", "south"))]);
                return boxes(m.Bool("upper") ? BOX_TRAP_TOP : BOX_TRAP_BOT);
            }
            if (sp == "carpet" || sp == "lilypad") return boxes(BOX_CARPET);
            if (sp == "plate") return boxes(m.Bool("pressed") ? BOX_PLATE_DOWN : BOX_PLATE);
            if (sp == "button") return boxes(workerButtonBox(m));
            if (sp == "seapickle")
            {
                object cv = m.Get("count"); double cn = Math.Max(1, Math.Min(4, Json.Truthy(cv) ? Json.ToNum(cv) : 1)); int n = double.IsNaN(cn) ? 0 : (int)cn; // P.slice(0, n)
                var o = boxScratch; o.Clear(); for (int k = 0; k < n; k++) o.Add(SEA_PICKLES[k]); return o;
            }
            if (sp == "bamboo") return boxes(BOX_BAMBOO);
            if (sp == "pot") return boxes(BOX_POT);
            return null;
        }
        void boxPart(VBuf v, IBuf i, int id, int x, int y, int z, double[] bx, double alpha = 1)
        {
            var b = def(id); int cls = XRAY[id];
            double x0 = (x + bx[0]), y0 = (y + bx[1]), z0 = (z + bx[2]), x1 = (x + bx[3]), y1 = (y + bx[4]), z1 = (z + bx[5]), sx = x1 - x0, sy = y1 - y0, sz = z1 - z0;
            int L = packedLight(x, y, z); double ls = sky(L), lb = blk(L);
            quad(v, i, x1, y0, z0, 0, sy, 0, 0, 0, sz, b.side, 0.74, alpha, false, null, ls, lb, cls, false);
            quad(v, i, x0, y0, z0, 0, 0, sz, 0, sy, 0, b.side, 0.66, alpha, false, null, ls, lb, cls, false);
            quad(v, i, x0, y1, z0, 0, 0, sz, sx, 0, 0, b.top, 1, alpha, false, null, ls, lb, cls, false);
            quad(v, i, x0, y0, z0, sx, 0, 0, 0, 0, sz, b.bottom, 0.56, alpha, false, null, ls, lb, cls, false);
            quad(v, i, x0, y0, z1, sx, 0, 0, 0, sy, 0, b.side, 0.84, alpha, false, null, ls, lb, cls, false);
            quad(v, i, x0, y0, z0, 0, sy, 0, sx, 0, 0, b.side, 0.78, alpha, false, null, ls, lb, cls, false);
        }
        void halfBox(VBuf v, IBuf i, int id, int x, int y, int z, double height = 0.56)
        {
            int cls = XRAY[id];
            for (int f = 0; f < 6; f++)
            {
                if (f == 2) { int L = packedLight(x, y + 1, z); quad(v, i, x, y + height, z, 0, 0, 1, 1, 0, 0, faceTile(id, 1, 1), 1, 1, false, null, sky(L), blk(L), cls, false); }
                else if (f == 3)
                {
                    if (at(x, y - 1, z) != B.AIR) continue;
                    int L = packedLight(x, y - 1, z); quad(v, i, x, y, z, 1, 0, 0, 0, 0, 1, faceTile(id, 1, -1), 0.56, 1, false, null, sky(L), blk(L), cls, false);
                }
                else
                {
                    var d = DIRS[f]; int L = packedLight(x + d[0], y + d[1], z + d[2]);
                    if (f == 0) quad(v, i, x + 1, y, z, 0, height, 0, 0, 0, 1, faceTile(id, 0, 1), 0.74, 1, false, null, sky(L), blk(L), cls, false);
                    if (f == 1) quad(v, i, x, y, z, 0, 0, 1, 0, height, 0, faceTile(id, 0, -1), 0.66, 1, false, null, sky(L), blk(L), cls, false);
                    if (f == 4) quad(v, i, x, y, z + 1, 1, 0, 0, 0, height, 0, faceTile(id, 2, 1), 0.84, 1, false, null, sky(L), blk(L), cls, false);
                    if (f == 5) quad(v, i, x, y, z, 0, height, 0, 1, 0, 0, faceTile(id, 2, -1), 0.78, 1, false, null, sky(L), blk(L), cls, false);
                }
            }
        }
        static readonly int[][] RAIL_DIRS = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
        static readonly double[][] RAIL_UVS = { new double[] { 0, 0, 1, 0, 0, 1, 1, 1 }, new double[] { 0, 1, 0, 0, 1, 1, 1, 0 }, new double[] { 1, 1, 0, 1, 1, 0, 0, 0 }, new double[] { 1, 0, 1, 1, 0, 0, 0, 1 } };
        static double railUp(int d, double px, double pz) { const double eps = 0.0625; return d == 0 ? (px > 0.5 ? 1 : eps) : d == 1 ? (px < 0.5 ? 1 : eps) : d == 2 ? (pz > 0.5 ? 1 : eps) : pz < 0.5 ? 1 : eps; }
        static int railCurveUV(int xd, int zd) { return xd == 0 && zd == 2 ? 0 : xd == 0 && zd == 3 ? 3 : xd == 1 && zd == 2 ? 1 : 2; }
        void emitRail(VBuf v, IBuf i, int id, int x, int y, int z, MDef b, JObj m)
        {
            int mask = 0, slope = -1;
            for (int d = 0; d < 4; d++)
            {
                var q = RAIL_DIRS[d]; int nx = x + q[0], nz = z + q[1];
                if (at(nx, y, nz) == B.RAIL) mask |= 1 << d;
                else if (at(nx, y + 1, nz) == B.RAIL) { mask |= 1 << d; if (slope < 0) slope = d; }
            }
            int L = packedLight(x, y, z), cls = XRAY[id]; const double eps = 0.0625;
            if (slope >= 0)
            {
                int d = slope;
                var uv = d == 0 || d == 1 ? RAIL_UVS[1] : RAIL_UVS[0];
                quadPoints(v, i, x, y + railUp(d, 0, 0), z, x + 1, y + railUp(d, 1, 0), z, x, y + railUp(d, 0, 1), z + 1, x + 1, y + railUp(d, 1, 1), z + 1, b.top, 0.96, b.alpha, false, uv, sky(L), blk(L), cls);
                return;
            }
            string kind; int axis = 0, xd = 0, zd = 0;
            if ((mask & 1) != 0 && (mask & 2) != 0) { kind = "straight"; axis = 1; }
            else if ((mask & 4) != 0 && (mask & 8) != 0) { kind = "straight"; axis = 0; }
            else
            {
                int xm = mask & 3, zm = mask & 12;
                if (xm != 0 && zm != 0) { kind = "curve"; xd = (mask & 1) != 0 ? 0 : 1; zd = (mask & 4) != 0 ? 2 : 3; }
                else if (xm != 0) { kind = "straight"; axis = 1; }
                else if (zm != 0) { kind = "straight"; axis = 0; }
                else { kind = "straight"; axis = m != null && m.Str("axis") == "x" ? 1 : 0; }
            }
            if (kind == "curve")
            {
                quadPoints(v, i, x, y + eps, z, x + 1, y + eps, z, x, y + eps, z + 1, x + 1, y + eps, z + 1, b.corner >= 0 ? b.corner : b.top, 0.96, b.alpha, false, RAIL_UVS[railCurveUV(xd, zd)], sky(L), blk(L), cls);
                return;
            }
            quadPoints(v, i, x, y + eps, z, x + 1, y + eps, z, x, y + eps, z + 1, x + 1, y + eps, z + 1, b.top, 0.96, b.alpha, false, axis == 1 ? RAIL_UVS[1] : RAIL_UVS[0], sky(L), blk(L), cls);
        }
        static readonly System.Text.RegularExpressions.Regex JS_DEC = new System.Text.RegularExpressions.Regex(@"^[+-]?(\d+\.?\d*([eE][+-]?\d+)?|\.\d+([eE][+-]?\d+)?)$"),
            JS_RADIX = new System.Text.RegularExpressions.Regex(@"^0([xX][0-9a-fA-F]+|[oO][0-7]+|[bB][01]+)$");
        /// <summary>JS Number(string) (StringToNumber); only finite results matter to the callers.</summary>
        static double jsNumber(string s)
        {
            s = s.Trim(' ', '\t', '\n', '\v', '\f', '\r', '\u00a0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200a', '\u2028', '\u2029', '\u202f', '\u205f', '\u3000', '\ufeff');
            if (s.Length == 0) return 0;
            if (JS_DEC.IsMatch(s)) return double.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
            if (JS_RADIX.IsMatch(s)) { char r = char.ToLowerInvariant(s[1]); int b = r == 'x' ? 16 : r == 'o' ? 8 : 2; double v = 0; for (int k = 2; k < s.Length; k++) v = v * b + Convert.ToInt32(s[k].ToString(), 16); return v; }
            return double.NaN;
        }
        MDef pottedDef(string key)
        {
            if (key == null) return null;
            if (key.StartsWith("b:", StringComparison.Ordinal)) { double n = jsNumber(key.Substring(2)); return n == Math.Floor(n) && n >= 0 && n < defs.Length ? def((int)n) : null; } // def(+key.slice(2))
            if (key.StartsWith("v:", StringComparison.Ordinal)) { MDef d; return vdefs != null && vdefs.TryGetValue(key.Substring(2), out d) ? d : null; }
            return null;
        }
        void emitPottedPlant(VBuf v, IBuf i, string key, int x, int y, int z)
        {
            var b = pottedDef(key);
            if (b == null) return;
            int t = b.partBottom >= 0 ? b.partBottom : b.top >= 0 ? b.top : b.side;
            if (t < 0) return;
            int L = packedLight(x, y, z); double y0 = y + 0.26, h = 0.62, r = 0.23;
            quad(v, i, x + 0.5 - r, y0, z + 0.5 - r, r * 2, 0, r * 2, 0, h, 0, t, 0.95, 1, false, null, sky(L), blk(L), 0, true);
            quad(v, i, x + 0.5 + r, y0, z + 0.5 - r, -r * 2, 0, r * 2, 0, h, 0, t, 0.95, 1, false, null, sky(L), blk(L), 0, true);
        }
        static readonly string[] FACE_FACING = { "east", "west", "up", "down", "south", "north" };

        // Section output buffers (reused)
        readonly VBuf ov = new VBuf(), cv = new VBuf(), wv = new VBuf(), tv = new VBuf();
        readonly IBuf oi = new IBuf(), ci = new IBuf(), wi = new IBuf(), ti = new IBuf();

        void buildSection(int si)
        {
            int y0 = si * 16, y1 = y0 + 16;
            greedySection(ov, oi, si);
            flatCollectV = wv; flatCells.Clear();
            for (int x = 0; x < C; x++)
                for (int z = 0; z < C; z++)
                    for (int y = y0; y < y1; y++)
                    {
                        int id = at(x, y, z);
                        if (id == B.AIR || isGreedySolid(id)) continue;
                        var m = vmeta(x, y, z);
                        var vb = isVirtual(id) ? vdef(x, y, z) : null;
                        var b = vb ?? def(id);
                        if (b == null) continue;
                        if (vb != null) { emitVirtualBlock(ov, oi, cv, ci, tv, ti, id, vb, m, x, y, z); continue; }
                        if (b.waterPlant)
                        {
                            if (aquaticWaterVolumeActive(x, y, z, b, m)) emitWaterVolume(wv, wi, x, y, z);
                            if (b.plant) { crossPlant(cv, ci, id, x, y, z, b.alpha); continue; }
                        }
                        if (b.plant) { crossPlant(cv, ci, id, x, y, z, b.alpha); continue; }
                        if (isFluidWater(id)) { emitFluidWater(wv, wi, id, x, y, z); continue; }
                        if (isLava(id)) { emitFluidLava(ov, oi, id, x, y, z); continue; }
                        VBuf v; IBuf i;
                        if (b.cutout || b.plant || b.waterPlant) { v = cv; i = ci; } else if (b.transparent) { v = tv; i = ti; } else { v = ov; i = oi; }
                        if (axisLogId(id))
                        {
                            var axis = orStr(m, "axis", "y");
                            for (int f = 0; f < 6; f++)
                            {
                                var d = DIRS[f]; var nd = def(at(x + d[0], y + d[1], z + d[2]));
                                if (nd != null && nd.solid && !nd.transparent && !nd.cutout && !nd.plant && !nd.waterPlant && nd.special == null) continue;
                                string fa = f < 2 ? "x" : f < 4 ? "y" : "z";
                                emitFace(v, i, id, x, y, z, f, b.alpha, fa == axis ? b.top : b.side);
                            }
                            continue;
                        }
                        if (id == B.CHEST) { emitChest(v, i, id, x, y, z, b, m); continue; }
                        if (b.special == "shipwheel") { foreach (var p in shipWheelParts(m)) boxPart(v, i, p.Value, x, y, z, p.Key, 1); continue; }
                        if (b.special == "torch") { torchSprite(v, i, id, x, y, z, m); continue; }
                        if (b.special == "ladder" || b.special == "door" || b.special == "doorOpen")
                        {
                            var f = orStr(m, "facing", "south");
                            if (b.special == "doorOpen") f = rotateDoorOpen(f);
                            thinPlane(v, i, id, x, y, z, f, b.alpha); continue;
                        }
                        if (b.special == "bed") { halfBox(v, i, id, x, y, z, 0.56); continue; }
                        if (b.special == "vine" || b.special == "lichen")
                        {
                            emitAttachmentPlanes(v, i, b.front >= 0 ? b.front : b.side, x, y, z, workerFaceMask(m, b.special == "vine" ? 15 : 63), b.alpha, XRAY[id]);
                            continue;
                        }
                        if (b.special == "pot")
                        {
                            var pb = shapeBoxes(id, x, y, z, m);
                            if (pb != null) foreach (var q in pb) boxPart(v, i, id, x, y, z, q, b.alpha);
                            var pk = m.Str("potKey"); if (!string.IsNullOrEmpty(pk)) emitPottedPlant(cv, ci, pk, x, y, z);
                            continue;
                        }
                        var sb = shapeBoxes(id, x, y, z, m);
                        if (sb != null) { foreach (var q in sb) boxPart(v, i, id, x, y, z, q, b.alpha); continue; }
                        if (b.special == "rail") { emitRail(v, i, id, x, y, z, b, m); continue; }
                        var facing = orStr(m, "facing", "south");
                        for (int f = 0; f < 6; f++)
                        {
                            var d = DIRS[f]; int nb = at(x + d[0], y + d[1], z + d[2]); var nd = def(nb);
                            if (nb == id && (b.transparent || b.cutout)) continue;
                            if (nd != null && nd.solid && !nd.transparent && !nd.cutout && !nd.plant && !nd.waterPlant && nd.special == null) continue;
                            int tile = -1;
                            if (b.front >= 0 && FACE_FACING[f] == facing) tile = b.front;
                            emitFace(v, i, id, x, y, z, f, b.alpha, tile);
                        }
                    }
            flushFlatWaterTops(wv, wi);
        }
        void loadNeighborhood(List<SecPayload> list, int minSi, int maxSi)
        {
            int y0 = Math.Max(0, minSi * 16), y1 = Math.Min(H, (maxSi + 1) * 16), a0 = y0 * RW * RW, a1 = y1 * RW * RW;
            for (int k = a0; k < a1; k++) { region[k] = (byte)B.AIR; lightRegion[k] = 0xf0; }
            if (list == null) return;
            foreach (var q in list)
            {
                if (q.dx < -1 || q.dx > 1 || q.dz < -1 || q.dz > 1 || q.si < minSi || q.si > maxSi || q.si < 0 || q.si >= 24) continue;
                var a = q.blocks; var l = q.light; int baseY = q.si * 16, baseX = (q.dx + 1) * 16, baseZ = (q.dz + 1) * 16;
                for (int y = 0; y < 16; y++)
                    for (int z = 0; z < 16; z++)
                    {
                        int dst = rIndex(baseX, baseY + y, baseZ + z), src = (y * 16 + z) * 16;
                        Buffer.BlockCopy(a, src, region, dst, 16);
                        if (l != null) Buffer.BlockCopy(l, src, lightRegion, dst, 16);
                    }
            }
        }
        static GeoBucket take(VBuf v, IBuf i, int v0, int i0, float ox, float oz)
        {
            int vn = v.n - v0, inn = i.n - i0;
            if (inn == 0) return GeoBucket.Empty;
            var va = new TVert[vn]; Array.Copy(v.a, v0, va, 0, vn);
            for (int k = 0; k < vn; k++) { va[k].x += ox; va[k].y -= 64; va[k].z += oz; } // worldify
            var ia = new int[inn];
            for (int k = 0; k < inn; k++) ia[k] = i.a[i0 + k] - v0;
            return new GeoBucket { v = va, i = ia };
        }
        public static GeoBucket concat(IList<GeoBucket> parts)
        {
            int vn = 0, inn = 0;
            foreach (var p in parts) if (p != null) { vn += p.VCount; inn += p.ICount; }
            if (inn == 0) return GeoBucket.Empty;
            var v = new TVert[vn]; var ind = new int[inn]; int vo = 0, io = 0;
            foreach (var p in parts)
            {
                if (p == null || p.ICount == 0) continue;
                Array.Copy(p.v, 0, v, vo, p.VCount);
                for (int j = 0; j < p.ICount; j++) ind[io + j] = p.i[j] + vo;
                vo += p.VCount; io += p.ICount;
            }
            return new GeoBucket { v = v, i = ind };
        }

        public MeshResult run(MeshJob m)
        {
            double t0 = JS.now();
            var sisL = new List<int>(); if (m.sis != null) foreach (var s in m.sis) if (s >= 0 && s < 24) sisL.Add(s);
            var res = new MeshResult { id = m.id, cx = m.cx, cz = m.cz, rev = m.rev, full = m.full, sis = sisL.ToArray(), ctx = m.ctx, geoEpoch = m.geoEpoch, chunkToken = m.chunkToken, initial = m.initial };
            if (sisL.Count == 0) { res.ms = JS.now() - t0; res.parts = new List<KeyValuePair<int, SectionGeo>>(); if (m.full) { res.full24 = new SectionGeo[24]; res.aggO = res.aggC = res.aggW = res.aggT = GeoBucket.Empty; } return res; }
            int minSi = 23, maxSi = 0;
            foreach (var si in sisL) { if (si < minSi) minSi = si; if (si > maxSi) maxSi = si; }
            loadNeighborhood(m.sections, Math.Max(0, minSi - 1), Math.Min(23, maxSi + 1));
            metaMap.Clear();
            if (m.meta != null) foreach (var q in m.meta) if (q.x >= -16 && q.x <= 31 && q.z >= -16 && q.z <= 31 && q.iy >= 0 && q.iy < H) metaMap[mkey(q.x, q.iy, q.z)] = q.m ?? EMPTY;
            float ox = m.cx * C, oz = m.cz * C;
            if (m.full) res.full24 = new SectionGeo[24]; else res.parts = new List<KeyValuePair<int, SectionGeo>>();
            foreach (var si in sisL)
            {
                ov.Clear(); oi.Clear(); cv.Clear(); ci.Clear(); wv.Clear(); wi.Clear(); tv.Clear(); ti.Clear();
                buildSection(si);
                var g = new SectionGeo { o = take(ov, oi, 0, 0, ox, oz), c = take(cv, ci, 0, 0, ox, oz), w = take(wv, wi, 0, 0, ox, oz), t = take(tv, ti, 0, 0, ox, oz) };
                if (m.full) res.full24[si] = g.Any ? g : null;
                else res.parts.Add(new KeyValuePair<int, SectionGeo>(si, g));
            }
            if (m.full)
            {
                var lo = new List<GeoBucket>(); var lc = new List<GeoBucket>(); var lw = new List<GeoBucket>(); var lt = new List<GeoBucket>();
                for (int si = 0; si < 24; si++) { var g = res.full24[si]; if (g == null) continue; lo.Add(g.o); lc.Add(g.c); lw.Add(g.w); lt.Add(g.t); }
                res.aggO = concat(lo); res.aggC = concat(lc); res.aggW = concat(lw); res.aggT = concat(lt);
            }
            res.ms = JS.now() - t0;
            return res;
        }
    }

}
