// Voxel Forge — Unity port. Box-model entities (Minecraft 64px texture layout), CPU skinning into one dynamic batch.
using System;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class ModelPart
    {
        public double[] b, at, uv, piv, rot, tb;
        public string anim;
        public bool rotUV;
        public int leg = -1;
        // compiled
        public float[] _corners, _faceUV;
        public double[] _pivot;
    }
    public sealed class EntityModel
    {
        public List<ModelPart> parts = new List<ModelPart>();
        public bool aquatic;
        public double pitchY, scale = 1;
    }

    public static partial class VF
    {
        public static readonly Dictionary<string, EntityModel> ENTITY_MODELS = new Dictionary<string, EntityModel>();
        static ModelPart P(double[] b, double[] at, double[] uv, string anim = null, bool rotUV = false, double[] piv = null, double[] rot = null)
        { return new ModelPart { b = b, at = at, uv = uv, anim = anim, rotUV = rotUV, piv = piv, rot = rot }; }
        static double[] V(params double[] a) { return a; }

        // Entity batch (CPU side): 7 floats per vertex: pos3, uv2, tile, shade.
        public static float[] entityVertUpload = new float[8192];
        public static int entityVertCount = 0, entityIndexCount = 0, entityBoxCount = 0;
        public static double entityBuildSun = 1, avgMobBuildMs = 0, avgMobUploadMs = 0;
        public static readonly FloatList mobHeldV = new FloatList();
        public static readonly List<int> mobHeldI = new List<int>();
        static readonly double[] entityAngles = new double[3];
        static readonly float[] entityCornerWorld = new float[24];
        public static int renderedMobCount = 0;

        static void ensureEntityVertexBatch(int addFloats)
        {
            int need = entityVertCount + addFloats;
            if (need <= entityVertUpload.Length) return;
            int n = entityVertUpload.Length > 0 ? entityVertUpload.Length : 8192;
            while (n < need) n <<= 1;
            var a = new float[n];
            Array.Copy(entityVertUpload, a, entityVertCount);
            entityVertUpload = a;
        }

        static void InitEntityModels()
        {
            ENTITY_MODELS.Clear();
            var cow = new EntityModel(); cow.parts.AddRange(new[] {
                P(V(4,12,4),V(-4,0,7),V(0,16),"leg0"), P(V(4,12,4),V(4,0,7),V(0,16),"leg1"), P(V(4,12,4),V(-4,0,-7),V(0,16),"leg2"), P(V(4,12,4),V(4,0,-7),V(0,16),"leg3"),
                P(V(12,10,18),V(0,12,0),V(18,4),null,true), P(V(8,8,6),V(0,14,12),V(0,0),"head") });
            ENTITY_MODELS["cow"] = cow;
            var pig = new EntityModel(); pig.parts.AddRange(new[] {
                P(V(4,6,4),V(-3,0,6),V(0,16),"leg0"), P(V(4,6,4),V(3,0,6),V(0,16),"leg1"), P(V(4,6,4),V(-3,0,-6),V(0,16),"leg2"), P(V(4,6,4),V(3,0,-6),V(0,16),"leg3"),
                P(V(10,8,16),V(0,6,0),V(28,8),null,true), P(V(8,8,8),V(0,7,11),V(0,0),"head") });
            ENTITY_MODELS["pig"] = pig;
            var sheep = new EntityModel(); sheep.parts.AddRange(new[] {
                P(V(4,12,4),V(-2,0,6),V(0,16),"leg0"), P(V(4,12,4),V(2,0,6),V(0,16),"leg1"), P(V(4,12,4),V(-2,0,-6),V(0,16),"leg2"), P(V(4,12,4),V(2,0,-6),V(0,16),"leg3"),
                P(V(8,6,16),V(0,12,0),V(28,8),null,true), P(V(6,6,8),V(0,14,11),V(0,0),"head") });
            ENTITY_MODELS["sheep"] = sheep;
            var chicken = new EntityModel(); chicken.parts.AddRange(new[] {
                P(V(3,5,3),V(-1.5,0,0),V(26,0),"leg0"), P(V(3,5,3),V(1.5,0,0),V(26,0),"leg1"), P(V(6,6,8),V(0,4,0),V(0,9),null,true),
                P(V(4,6,3),V(0,8,4),V(0,0),"head"), P(V(4,2,2),V(0,10,6.5),V(14,0)), P(V(1,4,6),V(-3.5,6,0),V(24,13),"wingL"), P(V(1,4,6),V(3.5,6,0),V(24,13),"wingR") });
            ENTITY_MODELS["chicken"] = chicken;
            var salmon = new EntityModel { aquatic = true, pitchY = 2.5, scale = 1 }; salmon.parts.AddRange(new[] {
                P(V(2,4,3),V(0,0.5,1.5),V(22,0)), P(V(3,5,8),V(0,0,-4),V(0,0)), P(V(3,5,8),V(0,0,-12),V(0,13),"tail",false,V(0,2.5,-8)),
                P(V(0,5,6),V(0,0,-19),V(20,10),"tail",false,V(0,2.5,-8)) });
            ENTITY_MODELS["salmon"] = salmon;
            var shark = new EntityModel { aquatic = true, pitchY = 5.5, scale = 1.25 }; shark.parts.AddRange(new[] {
                P(V(8,7,13),V(0,2,-2),V(0,0)), P(V(7,6,6),V(0,2.5,6),V(30,20)), P(V(4,3,4),V(0,3.5,10.5),V(0,36)), P(V(1,5,5),V(0,9,-1),V(16,36)),
                P(V(5,1,4),V(-5.5,3,2),V(42,36),null,false,null,V(0,0,-0.25)), P(V(5,1,4),V(5.5,3,2),V(42,41),null,false,null,V(0,0,0.25)),
                P(V(4,5,11),V(0,3.5,-13),V(0,20),"tail",false,V(0,5.5,-8)), P(V(1,10,6),V(0,1,-21),V(28,36),"tail",false,V(0,5.5,-8)) });
            ENTITY_MODELS["shark"] = shark;
            // --- entity model additions ---
            var player = new EntityModel(); player.parts.AddRange(new[] {
                P(V(4,12,4),V(-2,0,0),V(0,16),"leg0"), P(V(4,12,4),V(2,0,0),V(16,48),"leg1"), P(V(8,12,4),V(0,12,0),V(16,16)),
                P(V(4,12,4),V(-6,12,0),V(32,48),"armL"), P(V(4,12,4),V(6,12,0),V(40,16),"armR"), P(V(8,8,8),V(0,24,0),V(0,0),"head") });
            ENTITY_MODELS["player"] = player;
            var zombie = new EntityModel(); zombie.parts.AddRange(new[] {
                P(V(4,12,4),V(-2,0,0),V(0,16),"leg0"), P(V(4,12,4),V(2,0,0),V(0,16),"leg1"), P(V(8,12,4),V(0,12,0),V(16,16)),
                P(V(4,12,4),V(-6,12,0),V(40,16),"armL"), P(V(4,12,4),V(6,12,0),V(40,16),"armR"), P(V(8,8,8),V(0,24,0),V(0,0),"head") });
            ENTITY_MODELS["zombie"] = zombie;
            var skeleton = new EntityModel(); skeleton.parts.AddRange(new[] {
                P(V(2,12,2),V(-2,0,0),V(0,16),"leg0"), P(V(2,12,2),V(2,0,0),V(0,16),"leg1"), P(V(8,12,4),V(0,12,0),V(16,16)),
                P(V(2,12,2),V(-5,12,0),V(40,16),"armL"), P(V(2,12,2),V(5,12,0),V(40,16),"armR"), P(V(8,8,8),V(0,24,0),V(0,0),"head") });
            ENTITY_MODELS["skeleton"] = skeleton;
            var creeper = new EntityModel(); creeper.parts.AddRange(new[] {
                P(V(4,6,4),V(-2,0,3),V(0,16),"leg0"), P(V(4,6,4),V(2,0,3),V(0,16),"leg1"), P(V(4,6,4),V(-2,0,-3),V(0,16),"leg2"), P(V(4,6,4),V(2,0,-3),V(0,16),"leg3"),
                P(V(8,12,4),V(0,6,0),V(16,16)), P(V(8,8,8),V(0,18,0),V(0,0),"head") });
            ENTITY_MODELS["creeper"] = creeper;
            var spider = new EntityModel(); spider.parts.AddRange(sourceSpiderParts());
            ENTITY_MODELS["spider"] = spider;
            var ender = new EntityModel { scale = 2.9 / (50.0 / 16) }; ender.parts.AddRange(new[] {
                P(V(2,30,2),V(-2,0,0),V(56,0),"leg0"), P(V(2,30,2),V(2,0,0),V(56,0),"leg1"), P(V(8,12,4),V(0,30,0),V(32,16)),
                P(V(2,30,2),V(-5,12,0),V(56,0),"armL"), P(V(2,30,2),V(5,12,0),V(56,0),"armR"), P(V(8,8,8),V(0,42,0),V(0,0),"head") });
            ENTITY_MODELS["enderman"] = ender;
            Func<ModelPart[]> slimeParts = () => new[] { P(V(8,8,8),V(0,0,0),V(0,0)), P(V(2,2,2),V(-2.25,4,3.1),V(32,0)), P(V(2,2,2),V(2.25,4,3.1),V(32,4)), P(V(2,1,1),V(0,2,3.6),V(32,8)) };
            var sb = new EntityModel { scale = 4 }; sb.parts.AddRange(slimeParts()); ENTITY_MODELS["slime_big"] = sb;
            var sm = new EntityModel { scale = 2 }; sm.parts.AddRange(slimeParts()); ENTITY_MODELS["slime_med"] = sm;
            var ss = new EntityModel { scale = 1 }; ss.parts.AddRange(slimeParts()); ENTITY_MODELS["slime_small"] = ss;
            compileEntityModelParts();
        }
        static readonly double[][] SPIDER_LEG_ROWS = { new[] { 1, 0.7854 }, new[] { 0, 0.5812 }, new[] { -1, -0.5812 }, new[] { -2, -0.7854 } };
        static List<ModelPart> sourceSpiderParts()
        {
            var a = new List<ModelPart> { P(V(8,8,8),V(0,5,7),V(32,4),"head"), P(V(6,6,6),V(0,6,0),V(0,0)), P(V(10,8,12),V(0,5,-9),V(0,12)) };
            for (int i = 0; i < 8; i++)
            {
                var r = SPIDER_LEG_ROWS[i >> 1]; int side = i % 2 == 0 ? -1 : 1;
                a.Add(new ModelPart { b = V(16, 3, 1), at = V(side * 11, 7.5, r[0]), piv = V(side * 4, 9, r[0]), uv = V(18, 0), rot = V(0, r[1] * -side, 0.7854 * -side), leg = i });
            }
            return a;
        }
        static double[][] eRectPx(double u, double v, double w, double h)
        {
            return new[] { new[] { u / 64, v / 64 }, new[] { (u + w) / 64, v / 64 }, new[] { u / 64, (v + h) / 64 }, new[] { (u + w) / 64, (v + h) / 64 } };
        }
        static Dictionary<string, double[][]> ePartUV(ModelPart p)
        {
            double J = p.b[0], O = p.b[1], G = p.b[2], N = p.uv[0], Y = p.uv[1];
            if (p.rotUV)
            {
                double ue = J, ce = G, OA = O;
                return new Dictionary<string, double[][]> {
                    { "front", eRectPx(N + OA, Y, ue, OA) },
                    { "back", eRectPx(N + OA + ue, Y, ue, OA) },
                    { "top", eRectPx(N + 2 * OA + ue, Y + OA, ue, ce) },
                    { "bottom", new[] { new[] { (N + OA) / 64, (Y + OA + ce) / 64 }, new[] { (N + OA + ue) / 64, (Y + OA + ce) / 64 }, new[] { (N + OA) / 64, (Y + OA) / 64 }, new[] { (N + OA + ue) / 64, (Y + OA) / 64 } } },
                    { "left", new[] { new[] { (N + OA) / 64, (Y + OA) / 64 }, new[] { (N + OA) / 64, (Y + OA + ce) / 64 }, new[] { N / 64, (Y + OA) / 64 }, new[] { N / 64, (Y + OA + ce) / 64 } } },
                    { "right", new[] { new[] { (N + 2 * OA + ue) / 64, (Y + OA + ce) / 64 }, new[] { (N + 2 * OA + ue) / 64, (Y + OA) / 64 }, new[] { (N + OA + ue) / 64, (Y + OA + ce) / 64 }, new[] { (N + OA + ue) / 64, (Y + OA) / 64 } } },
                };
            }
            var tb = p.tb ?? p.b; double e = tb[0], fr = tb[1], Se = tb[2];
            return new Dictionary<string, double[][]> {
                { "top", eRectPx(N + Se, Y, e, Se) }, { "bottom", eRectPx(N + Se + e, Y, e, Se) }, { "front", eRectPx(N + Se, Y + Se, e, fr) },
                { "back", eRectPx(N + 2 * Se + e, Y + Se, e, fr) }, { "right", eRectPx(N, Y + Se, Se, fr) }, { "left", eRectPx(N + Se + e, Y + Se, Se, fr) },
            };
        }
        // Compile immutable box topology/UVs once. Animation still changes per frame, but a part now
        // transforms only its 8 unique box corners instead of recomputing the same corners per face.
        static readonly int[][] ENTITY_FACE_CORNERS = { new[] { 6, 7, 2, 3 }, new[] { 0, 1, 4, 5 }, new[] { 6, 7, 4, 5 }, new[] { 3, 2, 1, 0 }, new[] { 7, 3, 5, 1 }, new[] { 2, 6, 0, 4 } };
        static readonly string[] ENTITY_FACE_NAMES = { "top", "bottom", "front", "back", "left", "right" };
        static readonly double[] ENTITY_FACE_SHADE = { 1, 0.55, 0.85, 0.85, 0.72, 0.72 };
        static void compileEntityModelParts()
        {
            foreach (var model in ENTITY_MODELS.Values)
                foreach (var p in model.parts)
                {
                    double J = p.b[0], O = p.b[1], G = p.b[2], mnx = p.at[0] - J / 2, mxx = p.at[0] + J / 2, mny = p.at[1], mxy = p.at[1] + O, mnz = p.at[2] - G / 2, mxz = p.at[2] + G / 2;
                    var uv = ePartUV(p);
                    p._corners = new float[] { (float)mnx, (float)mny, (float)mnz, (float)mxx, (float)mny, (float)mnz, (float)mnx, (float)mxy, (float)mnz, (float)mxx, (float)mxy, (float)mnz,
                        (float)mnx, (float)mny, (float)mxz, (float)mxx, (float)mny, (float)mxz, (float)mnx, (float)mxy, (float)mxz, (float)mxx, (float)mxy, (float)mxz };
                    var fu = new float[6 * 8];
                    for (int f = 0; f < 6; f++)
                    {
                        var q = uv[ENTITY_FACE_NAMES[f]]; int o = f * 8;
                        for (int k = 0; k < 4; k++) { fu[o + k * 2] = (float)q[k][0]; fu[o + k * 2 + 1] = (float)q[k][1]; }
                    }
                    p._faceUV = fu;
                    p._pivot = p.piv ?? new[] { p.at[0], p.at[1] + p.b[1], p.at[2] };
                }
        }

        public static double[] getWorldLight(double wx, double y, double wz)
        {
            int p = getPackedLightWorld(JS.floor(wx), JS.floor(y), JS.floor(wz));
            return new[] { ((p >> 4) & 15) / 15.0, (p & 15) / 15.0 };
        }
        public static double entityLight(Mob m)
        {
            var l = getWorldLight(m.x, m.y + (mobInfo(m).aquatic ? Math.Min(0.2, mobHeight(m) * 0.5) : Math.Min(0.8, mobHeight(m) * 0.65)), m.z);
            double sky = (0.1 + 0.9 * entityBuildSun) * l[0];
            return Math.Max(0.08, Math.Min(1, Math.Max(sky, l[1]) * 0.94 + 0.06));
        }
        static double[] entityPartAngles(Mob m, ModelPart p)
        {
            double rx = p.rot != null ? p.rot[0] : 0, ry = p.rot != null ? p.rot[1] : 0, rz = p.rot != null ? p.rot[2] : 0;
            double walk = m.moving ? Math.Sin(m.walkPhase * 3) * 0.6 : 0;
            if (m.type == "player")
            {
                if (m.renderRiding && (p.anim == "leg0" || p.anim == "leg1")) rx = -1.35;
                if (m.renderSwimming)
                {
                    if (p.anim == "armL") rx = -1.35 + Math.Sin(m.walkPhase * 2.4) * 0.25;
                    if (p.anim == "armR") rx = -1.35 - Math.Sin(m.walkPhase * 2.4) * 0.25;
                    if (p.anim == "leg0" || p.anim == "leg1") rx = Math.Sin(m.walkPhase * 2.4 + (p.anim == "leg1" ? Math.PI : 0)) * 0.25;
                }
                else if (m.renderSneaking && (p.anim == "armL" || p.anim == "armR")) rx += 0.22;
            }
            if (p.anim == "leg0" || p.anim == "leg3" || p.anim == "armR") rx += walk;
            else if (p.anim == "leg1" || p.anim == "leg2" || p.anim == "armL") rx -= walk;
            if (m.type == "skeleton" && (p.anim == "armL" || p.anim == "armR")) rx = (p.rot != null ? p.rot[0] : 0) - 1.25 + walk * 0.08;
            if (p.leg >= 0)
            {
                int side = p.leg % 2 == 0 ? -1 : 1; double phase = m.walkPhase * 3 + (p.leg >> 1) * 1.57, mv = m.moving ? 1 : 0;
                ry += Math.Sin(phase) * 0.38 * mv * side;
                rz += Math.Abs(Math.Cos(phase)) * 0.32 * mv * side;
            }
            if (p.anim == "tail") ry += Math.Sin(m.walkPhase * 6) * 0.45;
            entityAngles[0] = rx; entityAngles[1] = ry; entityAngles[2] = rz;
            return entityAngles;
        }
        static EntityModel modelFor(string type) { EntityModel md; return type != null && ENTITY_MODELS.TryGetValue(type, out md) ? md : null; }
        static void addEntityPart(Mob m, ModelPart p, int tile)
        {
            var model = modelFor(m.type);
            var pv = p._pivot;
            var ang = entityPartAngles(m, p);
            double rx = ang[0], ry = ang[1], rz = ang[2], cx = Math.Cos(rx), sx = Math.Sin(rx), cy = Math.Cos(ry), sy = Math.Sin(ry), cz = Math.Cos(rz), sz = Math.Sin(rz),
                pitch = m.pitch, cp = Math.Cos(pitch), sp = Math.Sin(pitch), py0 = model != null ? model.pitchY : 0,
                ra = m._renderAngle ?? m.angle, ca = Math.Cos(ra), sa = Math.Sin(ra), rx0 = m._renderX ?? m.x, ry0 = m._renderY ?? m.y, rz0 = m._renderZ ?? m.z,
                sq = m.sqY != 0 ? m.sqY : 1, flat = 1 / Math.Sqrt(sq);
            double sc = ((model != null && model.scale != 0 ? model.scale : 1) / 16) * (m.baby ? 0.5 : 1);
            if (m.fuse >= 0) sc = (1.0 / 16) * (1 + m.fuse * 0.15 + 0.06 * Math.Sin(m.fuse * 25));
            var c = p._corners;
            bool aquatic = model != null && model.aquatic;
            for (int i = 0; i < 8; i++)
            {
                double x = c[i * 3] - pv[0], y = c[i * 3 + 1] - pv[1], z = c[i * 3 + 2] - pv[2], q;
                if (rx != 0) { q = y * cx - z * sx; z = y * sx + z * cx; y = q; }
                if (ry != 0) { q = x * cy + z * sy; z = -x * sy + z * cy; x = q; }
                if (rz != 0) { q = x * cz - y * sz; y = x * sz + y * cz; x = q; }
                x += pv[0]; y += pv[1]; z += pv[2];
                if (aquatic && pitch != 0) { double yy = y - py0; q = yy * cp - z * sp; z = yy * sp + z * cp; y = q + py0; }
                if (m.type == "player" && m.bodyPitch != 0)
                {
                    double pyv = 12, yy = y - pyv, c2 = Math.Cos(m.bodyPitch), ss = Math.Sin(m.bodyPitch);
                    q = yy * c2 - z * ss; z = yy * ss + z * c2; y = q + pyv;
                }
                if (m.type == "player" && m.bodyYOffset != 0) y += m.bodyYOffset;
                double lx = x * sc * flat, lz = z * sc * flat; int o = i * 3;
                entityCornerWorld[o] = (float)(rx0 + lx * ca + lz * sa);
                entityCornerWorld[o + 1] = (float)(ry0 + y * sc * sq);
                entityCornerWorld[o + 2] = (float)(rz0 + lx * sa - lz * ca);
            }
            double lit = (m._frameLight ?? entityLight(m)) * (m.hurtT > 0 ? 0.55 : 1);
            ensureEntityVertexBatch(24 * 7);
            var uv = p._faceUV;
            for (int f = 0; f < 6; f++)
            {
                var face = ENTITY_FACE_CORNERS[f]; float shade = (float)(ENTITY_FACE_SHADE[f] * lit); int uo = f * 8;
                for (int k = 0; k < 4; k++)
                {
                    int ci = face[k] * 3, o = entityVertCount;
                    entityVertUpload[o] = entityCornerWorld[ci];
                    entityVertUpload[o + 1] = entityCornerWorld[ci + 1];
                    entityVertUpload[o + 2] = entityCornerWorld[ci + 2];
                    entityVertUpload[o + 3] = uv[uo + k * 2];
                    entityVertUpload[o + 4] = uv[uo + k * 2 + 1];
                    entityVertUpload[o + 5] = tile;
                    entityVertUpload[o + 6] = shade;
                    entityVertCount = o + 7;
                }
            }
            entityBoxCount++;
            entityIndexCount = entityBoxCount * 36;
        }
        static double[] skeletonHandAt(Mob m, double x, double y, double z, double angle)
        {
            double walk = m.moving ? Math.Sin(m.walkPhase * 3) * 0.6 : 0, arm = -1.25 + walk * 0.08, sc = (m.baby ? 0.5 : 1) / 16.0, w = 5, l = 13, p = 24,
                c = Math.Cos(arm), sn = Math.Sin(arm), q = l - p, hy = q * c + p, hz = q * sn, D = -Math.Cos(angle), Tt = Math.Sin(angle);
            return new[] { x + (w * D + hz * Tt) * sc + Tt * 0.1, y + hy * sc, z + (-w * Tt + hz * D) * sc + D * 0.1 };
        }
        static void addSkeletonBow(Mob m)
        {
            int tile = ItemTile("bow"); if (tile < 0) return;
            double a = m._renderAngle ?? m.angle, x = m._renderX ?? m.x, y = m._renderY ?? m.y, z = m._renderZ ?? m.z;
            var hand = skeletonHandAt(m, x, y, z, a);
            double sa = Math.Sin(a), ca = Math.Cos(a), hx = 0.95 * sa + 0.31 * ca, hz = -0.95 * ca + 0.31 * sa, d = 0.34, rc = Math.Cos(-2.356), rs = Math.Sin(-2.356);
            var pts = new double[4][]; int n = 0;
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 2; col++)
                {
                    double u = (col - 0.5) * 2 * d, v = (0.5 - row) * 2 * d, W = u * rc - v * rs, Y = u * rs + v * rc;
                    pts[n++] = new[] { hand[0] + hx * W, hand[1] + Y, hand[2] + hz * W };
                }
            dropPushQuad(mobHeldV, mobHeldI, pts, tile, 0.9 * (m._frameLight ?? entityLight(m)));
        }
        static int entityTileForMob(Mob m)
        {
            int t;
            if (m.type == "sheep")
            {
                var c = m.color != null && m.color != "white" ? "_" + m.color : "";
                return ENTITY_TILE.TryGetValue((m.sheared ? "sheep_sheared" : "sheep") + c, out t) ? t : -1;
            }
            if (m.type.StartsWith("slime_", StringComparison.Ordinal)) return ENTITY_TILE["slime"];
            return ENTITY_TILE.TryGetValue(m.type, out t) ? t : -1;
        }
        static void addAnimalModel(Mob m)
        {
            var model = modelFor(m.type); int tile = entityTileForMob(m);
            if (model == null || tile < 0) return;
            double a = mobRenderAlpha;
            m._renderX = m.ox == null ? m.x : m.ox + (m.x - m.ox) * a;
            m._renderY = m.oy == null ? m.y : m.oy + (m.y - m.oy) * a;
            m._renderZ = m.oz == null ? m.z : m.oz + (m.z - m.oz) * a;
            m._renderAngle = m.oangle == null ? m.angle : mobLerpAngle(m.oangle.Value, m.angle, a);
            m._frameLight = entityLight(m);
            foreach (var p in model.parts) addEntityPart(m, p, tile);
            if (m.type == "skeleton") addSkeletonBow(m);
        }

        static double playerRenderWalk = 0;
        static readonly Mob thirdPersonMob = new Mob();
        static void addThirdPersonHeld(Mob m)
        {
            var st = selectedStack();
            if (st == null) return;
            int tile = ItemTile(st.key);
            if (tile < 0) return;
            double a = player.yaw, sa = Math.Sin(a), ca = Math.Cos(a), cx = player.x + sa * 0.42 + ca * 0.18, cy = player.y + (m.renderSneaking ? 1.0 : 1.18), cz = player.z - ca * 0.42 + sa * 0.18, u = 0.23;
            dropPushQuad(mobHeldV, mobHeldI, new[] {
                new[] { cx - u * ca, cy + u, cz - u * sa }, new[] { cx + u * ca, cy + u, cz + u * sa },
                new[] { cx - u * ca, cy - u, cz - u * sa }, new[] { cx + u * ca, cy - u, cz + u * sa } }, tile, 0.9 * (m._frameLight ?? 1));
        }
        static void addThirdPersonPlayer()
        {
            double speed = JS.hypot(player.vx, player.vz); bool moving = speed > 0.25;
            if (moving) playerRenderWalk += MOB_FIXED_DT * speed * 1.6;
            bool renderSwimming = player.swimming, renderRiding = player.riding != null,
                renderSneaking = !renderSwimming && !renderRiding && !player.flying && !player.inWater && (keyDown("ShiftLeft") || keyDown("ShiftRight"));
            var m = thirdPersonMob;
            m.type = "player"; m.x = player.x; m.y = player.y; m.z = player.z; m.angle = player.yaw;
            m._renderX = player.x; m._renderY = player.y; m._renderZ = player.z; m._renderAngle = player.yaw;
            m.walkPhase = playerRenderWalk; m.moving = renderRiding ? false : moving; m.baby = false; m.hurtT = player.invuln > 0 ? 0.15 : 0;
            m.sqY = 1; m.pitch = 0; m.fuse = -1; m.renderSwimming = renderSwimming; m.renderRiding = renderRiding; m.renderSneaking = renderSneaking;
            m.bodyPitch = renderSwimming ? -Math.PI / 2 : renderSneaking ? 0.28 : 0;
            m.bodyYOffset = renderSneaking ? -0.18 : renderRiding ? 0.08 : 0;
            m._frameLight = null;
            m._frameLight = entityLight(m);
            int tile = ENTITY_TILE["player"];
            foreach (var part in ENTITY_MODELS["player"].parts) addEntityPart(m, part, tile);
            addThirdPersonHeld(m);
        }
        /// <summary>Builds the per-frame entity batch; the renderer then submits it (see VF.Renderer).</summary>
        public static void buildEntityBatch()
        {
            entityVertCount = 0; entityIndexCount = 0; entityBoxCount = 0;
            mobHeldV.Clear(); mobHeldI.Clear();
            renderedMobCount = 0;
            double buildStart = JS.now();
            int pcx = JS.floor(player.x / CHUNK), pcz = JS.floor(player.z / CHUNK);
            foreach (var m in liveMobs)
            {
                if (m.dead) continue;
                var c = m._owner;
                if (c == null || Math.Abs(c.cx - pcx) > renderDistance || Math.Abs(c.cz - pcz) > renderDistance) continue;
                double r = mobWidth(m) + 0.35;
                if (!aabbVisible(m.x - r, m.y - 0.1, m.z - r, m.x + r, m.y + mobHeight(m) + 0.2, m.z + r)) continue;
                addAnimalModel(m);
                renderedMobCount++;
            }
            if (player.camMode != 0) addThirdPersonPlayer();
            double buildMs = JS.now() - buildStart;
            avgMobBuildMs += (buildMs - avgMobBuildMs) * 0.08;
        }
    }
}
