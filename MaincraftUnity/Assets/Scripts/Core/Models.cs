// Port of models.js: block models of this game (own designs): boxes and planes in 1/16 block units, built in code.
// MODEL_LIST[i] = elements [from, to, rotation [origin, axis 0 x / 1 y / 2 z, angle, rescale] or null,
//   faces (+x -x +y -y +z -z) each [texture, u0, v0, u1, v1, uv rotation, cullface (face index or -1), shade] or null]
// MODEL_VAR[key][meta] = parts [model, x rotation, y rotation]; MODEL_BOX[key][meta] = selection box.
// Models face north (-z); a facing meta (0 south, 1 west, 2 north, 3 east) turns them about y.
using System;
using System.Collections.Generic;

namespace Maincraft
{
    public sealed class MFace
    {
        public string Tex;
        public double U0, V0, U1, V1;
        public int UvRot;      // degrees
        public int Cull;       // face index or -1
        public int Shade;      // 1 shaded, 0 full bright
        public int Layer;      // atlas layer, resolved by Blocks.ResolveBlockTextures
    }

    public sealed class MRot
    {
        public double Ox, Oy, Oz;
        public int Axis;       // 0 x, 1 y, 2 z
        public double Angle;
        public int Rescale;
        public MRot(double ox, double oy, double oz, int axis, double angle, int rescale)
        { Ox = ox; Oy = oy; Oz = oz; Axis = axis; Angle = angle; Rescale = rescale; }
    }

    public sealed class MElem
    {
        public double[] From, To;
        public MRot Rot;
        public MFace[] Faces = new MFace[6];
    }

    public struct MPart
    {
        public int Model, Rx, Ry;
        public MPart(int model, int rx, int ry) { Model = model; Rx = rx; Ry = ry; }
    }

    public static class Models
    {
        public static readonly List<MElem[]> MODEL_LIST = new List<MElem[]>();
        public static readonly Dictionary<string, MPart[][]> MODEL_VAR = new Dictionary<string, MPart[][]>();
        public static readonly Dictionary<string, double[][]> MODEL_BOX = new Dictionary<string, double[][]>();

        // texture of a box: one texture for every face, or one per face (e w u d s n), side for the four
        // sides, all for the rest
        sealed class Tx
        {
            public string e, w, u, d, s, n, side, all;
            public static implicit operator Tx(string t) => new Tx { all = t, single = true };
            public bool single;
        }

        sealed class Opt
        {
            public Dictionary<char, double[]> uv;
            public Dictionary<char, int> rot;
            public string skip = "";
            public MRot r;
        }

        static double[] V(double a, double b, double c) => new[] { a, b, c };

        // planar uv of a face from the element's coordinates (u along the face, v downward)
        static double[] UvOf(int f, double[] a, double[] b)
        {
            switch (f)
            {
                case 0: return new[] { 16 - b[2], 16 - b[1], 16 - a[2], 16 - a[1] };   // +x
                case 1: return new[] { a[2], 16 - b[1], b[2], 16 - a[1] };             // -x
                case 2: return new[] { a[0], a[2], b[0], b[2] };                       // +y
                case 3: return new[] { a[0], 16 - b[2], b[0], 16 - a[2] };             // -y
                case 4: return new[] { a[0], 16 - b[1], b[0], 16 - a[1] };             // +z
                default: return new[] { 16 - b[0], 16 - b[1], 16 - a[0], 16 - a[1] };  // -z
            }
        }

        static bool OnEdge(int f, double[] a, double[] b) =>
            (f == 0 && b[0] >= 16) || (f == 1 && a[0] <= 0) || (f == 2 && b[1] >= 16) || (f == 3 && a[1] <= 0) || (f == 4 && b[2] >= 16) || (f == 5 && a[2] <= 0);

        static readonly char[] FACE_KEYS = { 'e', 'w', 'u', 'd', 's', 'n' };

        static string TexFor(Tx tex, char k, int f)
        {
            if (tex.single) return tex.all;
            string t = k switch { 'e' => tex.e, 'w' => tex.w, 'u' => tex.u, 'd' => tex.d, 's' => tex.s, _ => tex.n };
            if (string.IsNullOrEmpty(t)) t = (f != 2 && f != 3) ? tex.side : null;
            if (string.IsNullOrEmpty(t)) t = tex.all;
            return string.IsNullOrEmpty(t) ? null : t;
        }

        // box: tex is one texture or {e, w, u, d, s, n, side, all}; opt.uv overrides per face; opt.skip faces
        static MElem Box(double[] a, double[] b, Tx tex, Opt opt = null)
        {
            opt = opt ?? new Opt();
            var el = new MElem { From = a, To = b, Rot = opt.r };
            for (int f = 0; f < 6; f++)
            {
                char k = FACE_KEYS[f];
                if (opt.skip.IndexOf(k) >= 0) continue;
                string t = TexFor(tex, k, f);
                if (t == null) continue;
                double[] uv = (opt.uv != null && opt.uv.TryGetValue(k, out var o)) ? o : UvOf(f, a, b);
                int rot = (opt.rot != null && opt.rot.TryGetValue(k, out var r)) ? r : 0;
                el.Faces[f] = new MFace { Tex = t, U0 = uv[0], V0 = uv[1], U1 = uv[2], V1 = uv[3], UvRot = rot, Cull = OnEdge(f, a, b) ? f : -1, Shade = 1 };
            }
            return el;
        }

        // a flat plane at z = 8 seen from both sides, turned by `angle` about y around the block centre
        static MElem Plane(double x0, double y0, double x1, double y1, string tex, double angle, double[] uv = null)
        {
            double[] u = uv ?? new[] { x0, 16 - y1, x1, 16 - y0 };
            var el = new MElem { From = V(x0, y0, 8), To = V(x1, y1, 8), Rot = angle != 0 ? new MRot(8, 8, 8, 1, angle, 0) : null };
            el.Faces[4] = new MFace { Tex = tex, U0 = u[0], V0 = u[1], U1 = u[2], V1 = u[3], UvRot = 0, Cull = -1, Shade = 1 };
            el.Faces[5] = new MFace { Tex = tex, U0 = u[2], V0 = u[1], U1 = u[0], V1 = u[3], UvRot = 0, Cull = -1, Shade = 1 };
            return el;
        }

        static MElem[] Cross(double x0, double y0, double x1, double y1, string tex, double[] uv = null) =>
            new[] { Plane(x0, y0, x1, y1, tex, 45, uv), Plane(x0, y0, x1, y1, tex, -45, uv) };

        static int Add(params MElem[] els) { MODEL_LIST.Add(els); return MODEL_LIST.Count - 1; }
        static int Add(List<MElem> els) => Add(els.ToArray());

        static readonly int[] FACING_Y = { 180, 270, 0, 90 };

        static void Facing(string key, int model, int metas = 4)
        {
            var v = new MPart[metas][];
            for (int m = 0; m < metas; m++) v[m] = new[] { new MPart(model, 0, FACING_Y[m & 3]) };
            MODEL_VAR[key] = v;
        }

        static void Single(string key, int model) { MODEL_VAR[key] = new[] { new[] { new MPart(model, 0, 0) } }; }

        static Dictionary<char, double[]> Uv(string faces, double[] uv)
        {
            var d = new Dictionary<char, double[]>();
            foreach (char c in faces) d[c] = uv;
            return d;
        }

        static List<MElem> L(params object[] parts)
        {
            var l = new List<MElem>();
            foreach (var p in parts)
            {
                if (p is MElem e) l.Add(e);
                else if (p is IEnumerable<MElem> es) l.AddRange(es);
            }
            return l;
        }

        static bool _built;

        public static void Build()
        {
            if (_built) return;
            _built = true;

            // barrel: a full block turned toward its facing (meta 0-3 horizontal, 4 up, 5 down; +8 open)
            {
                int shut = Add(Box(V(0, 0, 0), V(16, 16, 16), new Tx { u = "barrel_top", d = "barrel_bottom", side = "barrel_side" }));
                int open = Add(Box(V(0, 0, 0), V(16, 16, 16), new Tx { u = "barrel_top_open", d = "barrel_bottom", side = "barrel_side" }));
                var v = new MPart[16][];
                for (int m = 0; m < 16; m++)
                {
                    int d = m & 7, M = (m & 8) != 0 ? open : shut;
                    v[m] = new[] { d == 4 || d > 5 ? new MPart(M, 0, 0) : d == 5 ? new MPart(M, 180, 0) : new MPart(M, 90, FACING_Y[d]) };
                }
                MODEL_VAR["BARREL"] = v;
            }
            // lectern: base, post and a sloped reading board
            Facing("LECTERN", Add(
                Box(V(0, 0, 0), V(16, 2, 16), new Tx { u = "lectern_base", d = "oak_planks", side = "lectern_base" }),
                Box(V(4, 2, 4), V(12, 13, 12), new Tx { side = "lectern_sides", n = "lectern_front" }, new Opt { skip = "ud" }),
                Box(V(0.5, 12, 2), V(15.5, 15, 15), new Tx { u = "lectern_top", d = "oak_planks", side = "lectern_sides" }, new Opt { r = new MRot(8, 13, 8, 0, -22.5, 0) })));
            // grindstone: two legs with pivots holding the wheel (meta: facing, +4 wall, +8 ceiling)
            {
                Func<double, MElem[]> parts = (y) => new[] {
                    Box(V(2, y, 6), V(4, y + 7, 10), "grindstone_pivot"), Box(V(12, y, 6), V(14, y + 7, 10), "grindstone_pivot"),
                    Box(V(2, y + 7, 5), V(4, y + 13, 11), "grindstone_pivot"), Box(V(12, y + 7, 5), V(14, y + 13, 11), "grindstone_pivot"),
                    Box(V(4, y + 4, 2), V(12, y + 16, 14), new Tx { side = "grindstone_round", e = "grindstone_side", w = "grindstone_side", u = "grindstone_round", d = "grindstone_round" }),
                };
                int floor = Add(parts(0));
                var v = new MPart[12][];
                for (int m = 0; m < 12; m++)
                {
                    int face = m >> 2, y = FACING_Y[m & 3];
                    v[m] = new[] { face == 0 ? new MPart(floor, 0, y) : face == 2 ? new MPart(floor, 180, (y + 180) % 360) : new MPart(floor, 90, y) };
                }
                MODEL_VAR["GRINDSTONE"] = v;
            }
            // stonecutter: a low block with the saw blade standing in the middle
            Facing("STONECUTTER", Add(
                Box(V(0, 0, 0), V(16, 9, 16), new Tx { u = "stonecutter_top", d = "stonecutter_bottom", side = "stonecutter_side" }),
                Plane(1, 9, 15, 16, "stonecutter_saw", 0, new double[] { 1, 0, 15, 7 })));
            // bell: stone posts and a beam (floor), a short hanger (ceiling) or a wall bracket, and the bell
            {
                var bell = new[] { Box(V(5, 6, 5), V(11, 13, 11), "bell_body"), Box(V(4, 4, 4), V(12, 6, 12), "bell_body") };
                int floor = Add(L(Box(V(0, 0, 6), V(2, 16, 10), "stone"), Box(V(14, 0, 6), V(16, 16, 10), "stone"),
                    Box(V(2, 13, 7), V(14, 15, 9), "dark_oak_planks"), bell));
                int ceiling = Add(L(Box(V(7, 13, 7), V(9, 16, 9), "dark_oak_planks"), bell));
                int wall = Add(L(Box(V(7, 13, 3), V(9, 15, 16), "dark_oak_planks"), bell));
                int both = Add(L(Box(V(7, 13, 0), V(9, 15, 16), "dark_oak_planks"), bell));
                int[] at4 = { floor, ceiling, wall, both };
                var v = new MPart[16][];
                for (int m = 0; m < 16; m++)
                {
                    int at = m >> 2, y = FACING_Y[m & 3];
                    v[m] = new[] { new MPart(at4[at], 0, at == 0 ? (y + 90) % 360 : y) };
                }
                MODEL_VAR["BELL"] = v;
            }
            // lantern: iron cage with a cap and a handle; hanging (meta 1) one block higher on a chain
            {
                Func<double, bool, List<MElem>> Lt = (y, chain) =>
                {
                    var els = new List<MElem> {
                        Box(V(5, y, 5), V(11, y + 7, 11), new Tx { side = "lantern", u = "lantern", d = "lantern" },
                            new Opt { uv = new Dictionary<char, double[]> { ['e'] = new double[] { 0, 2, 6, 9 }, ['w'] = new double[] { 0, 2, 6, 9 }, ['s'] = new double[] { 0, 2, 6, 9 }, ['n'] = new double[] { 0, 2, 6, 9 }, ['u'] = new double[] { 0, 9, 6, 15 }, ['d'] = new double[] { 0, 9, 6, 15 } } }),
                        Box(V(6, y + 7, 6), V(10, y + 9, 10), "lantern",
                            new Opt { uv = new Dictionary<char, double[]> { ['e'] = new double[] { 0, 0, 4, 2 }, ['w'] = new double[] { 0, 0, 4, 2 }, ['s'] = new double[] { 0, 0, 4, 2 }, ['n'] = new double[] { 0, 0, 4, 2 }, ['u'] = new double[] { 0, 9, 4, 13 }, ['d'] = new double[] { 0, 9, 4, 13 } } }),
                    };
                    els.AddRange(Cross(6.5, y + 9, 9.5, y + (chain ? 16 - y : 11), "lantern", new double[] { 11, 1, 14, 3 }));
                    return els;
                };
                MODEL_VAR["LANTERN"] = new[] { new[] { new MPart(Add(Lt(0, false)), 0, 0) }, new[] { new MPart(Add(Lt(1, true)), 0, 0) } };
            }
            // campfire: two logs on the ground, two crossing on top, embers and flames (meta: facing, +4 out)
            {
                Func<bool, List<MElem>> logs = (lit) => new List<MElem> {
                    Box(V(1, 0, 0), V(5, 4, 16), new Tx { side = "campfire_log", u = "campfire_log" }), Box(V(11, 0, 0), V(15, 4, 16), new Tx { side = "campfire_log", u = "campfire_log" }),
                    Box(V(0, 3, 1), V(16, 7, 5), new Tx { side = lit ? "campfire_log_lit" : "campfire_log", u = "campfire_log" }),
                    Box(V(0, 3, 11), V(16, 7, 15), new Tx { side = lit ? "campfire_log_lit" : "campfire_log", u = "campfire_log" }),
                    Box(V(5, 0, 0), V(11, 1, 16), lit ? "campfire_log_lit" : "campfire_log"),
                };
                int lit1 = Add(L(logs(true), Cross(1, 1, 15, 16, "campfire_fire", new double[] { 0, 0, 16, 15 })));
                int out1 = Add(logs(false));
                var v = new MPart[8][];
                for (int m = 0; m < 8; m++) v[m] = new[] { new MPart((m & 4) != 0 ? out1 : lit1, 0, FACING_Y[m & 3]) };
                MODEL_VAR["CAMPFIRE"] = v;
            }
            // brewing stand: rod on three feet with up to three bottles (meta bits)
            {
                int bas = Add(
                    Box(V(7, 0, 7), V(9, 14, 9), "brewing_stand", new Opt { uv = new Dictionary<char, double[]> { ['e'] = new double[] { 7, 2, 9, 16 }, ['w'] = new double[] { 7, 2, 9, 16 }, ['s'] = new double[] { 7, 2, 9, 16 }, ['n'] = new double[] { 7, 2, 9, 16 }, ['u'] = new double[] { 7, 2, 9, 4 }, ['d'] = new double[] { 7, 2, 9, 4 } } }),
                    Box(V(9, 0, 5), V(15, 2, 11), "brewing_stand_base"), Box(V(1, 0, 1), V(7, 2, 7), "brewing_stand_base"), Box(V(1, 0, 9), V(7, 2, 15), "brewing_stand_base"));
                Func<double, int> bottle = (a) => Add(Plane(8, 0, 16, 16, "brewing_stand", a, new double[] { 8, 0, 16, 16 }));
                int[] bb = { bottle(0), bottle(120), bottle(240) };
                var v = new MPart[8][];
                for (int m = 0; m < 8; m++)
                {
                    var parts = new List<MPart> { new MPart(bas, 0, 0) };
                    for (int i = 0; i < 3; i++) if ((m & (1 << i)) != 0) parts.Add(new MPart(bb[i], 0, 0));
                    v[m] = parts.ToArray();
                }
                MODEL_VAR["BREWING_STAND"] = v;
            }
            // cauldron: four walls on corner legs; water at three levels (meta 1-3)
            {
                var S = new Tx { side = "cauldron_side", u = "cauldron_top", d = "cauldron_bottom" };
                var shell = new List<MElem> {
                    Box(V(0, 3, 0), V(2, 16, 16), S), Box(V(14, 3, 0), V(16, 16, 16), S), Box(V(2, 3, 0), V(14, 16, 2), S), Box(V(2, 3, 14), V(14, 16, 16), S),
                    Box(V(2, 3, 2), V(14, 4, 14), new Tx { u = "cauldron_inner", d = "cauldron_bottom" }, new Opt { skip = "ewsn" }),
                    Box(V(0, 0, 0), V(4, 3, 2), S), Box(V(0, 0, 2), V(2, 3, 4), S), Box(V(12, 0, 0), V(16, 3, 2), S), Box(V(14, 0, 2), V(16, 3, 4), S),
                    Box(V(0, 0, 14), V(4, 3, 16), S), Box(V(0, 0, 12), V(2, 3, 14), S), Box(V(12, 0, 14), V(16, 3, 16), S), Box(V(14, 0, 12), V(16, 3, 14), S),
                };
                int[] hs = { 0, 9, 12, 15 };
                var v = new MPart[4][];
                for (int i = 0; i < 4; i++)
                {
                    int h = hs[i];
                    v[i] = new[] { new MPart(Add(h != 0 ? L(shell, Box(V(2, 4, 2), V(14, h, 14), new Tx { u = "water_still" }, new Opt { skip = "ewsnd" })) : shell), 0, 0) };
                }
                MODEL_VAR["CAULDRON"] = v;
            }
            // composter: a box of planks, filled by level (meta 0-8)
            {
                var W = new Tx { side = "composter_side", u = "composter_top", d = "composter_bottom" };
                var shell = new List<MElem> { Box(V(0, 0, 0), V(16, 2, 16), W, new Opt { skip = "ewsn" }), Box(V(0, 0, 0), V(2, 16, 16), W), Box(V(14, 0, 0), V(16, 16, 16), W),
                    Box(V(2, 0, 0), V(14, 16, 2), W), Box(V(2, 0, 14), V(14, 16, 16), W) };
                var v = new MPart[9][];
                for (int m = 0; m < 9; m++)
                {
                    int h = m == 0 ? 0 : Math.Min(15, 1 + m * 2);
                    v[m] = new[] { new MPart(Add(h != 0 ? L(shell, Box(V(2, 2, 2), V(14, h, 14), new Tx { u = m == 8 ? "composter_ready" : "composter_compost" }, new Opt { skip = "ewsnd" })) : shell), 0, 0) };
                }
                MODEL_VAR["COMPOSTER"] = v;
            }
            // pointed dripstone: crossed planes, a sprite per thickness (meta 0-4) and direction (+8 up)
            {
                string[] T = { "tip_merge", "tip", "frustum", "middle", "base" };
                var v = new MPart[16][];
                for (int m = 0; m < 16; m++) v[m] = new[] { new MPart(Add(Cross(0, 0, 16, 16, "pointed_dripstone_" + ((m & 8) != 0 ? "up_" : "down_") + T[Math.Min(4, m & 7)])), 0, 0) };
                MODEL_VAR["POINTED_DRIPSTONE"] = v;
            }
            // azaleas: a leafy cap on a woody stem
            foreach (var (k, t) in new[] { ("AZALEA", "azalea"), ("FLOWERING_AZALEA", "flowering_azalea") })
                Single(k, Add(L(Box(V(0, 8, 0), V(16, 16, 16), new Tx { u = t + "_top", side = t + "_side", d = t + "_top" }), Cross(2, 0, 14, 8, "azalea_plant", new double[] { 2, 8, 14, 16 }))));
            // dripleaves: a flat leaf on a stem (big), a pair of small crossed leaves (small, meta: facing, +4 upper)
            Facing("BIG_DRIPLEAF", Add(L(Box(V(0, 15, 0), V(16, 15.5, 16), new Tx { u = "big_dripleaf_top", d = "big_dripleaf_top", side = "big_dripleaf_side" }), Cross(5, 0, 11, 15, "big_dripleaf_stem"))));
            Facing("BIG_DRIPLEAF_STEM", Add(Cross(5, 0, 11, 16, "big_dripleaf_stem")));
            {
                int lower = Add(Cross(2, 0, 14, 16, "small_dripleaf_stem_bottom")), upper = Add(L(Cross(2, 0, 14, 12, "small_dripleaf_stem_top"),
                    Box(V(2, 11, 2), V(14, 11.5, 14), new Tx { u = "small_dripleaf_top", d = "small_dripleaf_top" }, new Opt { skip = "ewsn" })));
                var v = new MPart[8][];
                for (int m = 0; m < 8; m++) v[m] = new[] { new MPart((m & 4) != 0 ? upper : lower, 0, FACING_Y[m & 3]) };
                MODEL_VAR["SMALL_DRIPLEAF"] = v;
            }
            // spore blossom: petals spread just under the ceiling around a green base
            Single("SPORE_BLOSSOM", Add(Box(V(1, 15, 1), V(15, 15.2, 15), new Tx { d = "spore_blossom", u = "spore_blossom" }, new Opt { skip = "ewsn" }),
                Box(V(5, 13, 5), V(11, 16, 11), "spore_blossom_base")));
            // sculk sensor: a half slab with four tendrils; shrieker: a slab with a bone jaw on top
            Single("SCULK_SENSOR", Add(Box(V(0, 0, 0), V(16, 8, 16), new Tx { u = "sculk_sensor_top", d = "sculk_sensor_bottom", side = "sculk_sensor_side" }),
                Plane(3, 8, 7, 16, "sculk_sensor_tendril_inactive", 45, new double[] { 4, 0, 12, 16 }), Plane(9, 8, 13, 16, "sculk_sensor_tendril_inactive", -45, new double[] { 4, 0, 12, 16 })));
            Single("SCULK_SHRIEKER", Add(Box(V(0, 0, 0), V(16, 8, 16), new Tx { u = "sculk_shrieker_inner_top", d = "sculk_shrieker_bottom", side = "sculk_shrieker_side" }),
                Box(V(1, 8, 1), V(15, 15, 15), new Tx { u = "sculk_shrieker_top", side = "sculk_shrieker_side" }, new Opt { skip = "d" })));

            // ship wheel: a turned post with a spoked wheel on its north face, handles past the rim
            {
                const string W = "spruce_planks", P = "stripped_spruce_log";
                Facing("SHIP_WHEEL", Add(
                    Box(V(6, 0, 8), V(10, 11, 12), P),
                    Box(V(7, 9, 5), V(9, 11, 8), W),
                    Box(V(3, 14, 6), V(13, 15, 7), W), Box(V(3, 5, 6), V(13, 6, 7), W),
                    Box(V(3, 6, 6), V(4, 14, 7), W), Box(V(12, 6, 6), V(13, 14, 7), W),
                    Box(V(7.5, 6, 6), V(8.5, 14, 7), W), Box(V(4, 9.5, 6), V(12, 10.5, 7), W),
                    Box(V(7.5, 15, 6), V(8.5, 16, 7), P), Box(V(7.5, 4, 6), V(8.5, 5, 7), P),
                    Box(V(1, 9.5, 6), V(3, 10.5, 7), P), Box(V(13, 9.5, 6), V(15, 10.5, 7), P)));
            }

            // cocoa: a ribbed pod hanging from the log on its north side by a short stalk, bigger with age
            // (meta: the log's direction 0-3 | age 0-2 << 2)
            {
                int[][] wh = { new[] { 4, 5 }, new[] { 6, 7 }, new[] { 8, 9 } };
                int[] pods = new int[3];
                for (int age = 0; age < 3; age++)
                {
                    double w = wh[age][0], h = wh[age][1];
                    string t = "cocoa_stage" + age;
                    double x0 = 8 - w / 2, y1 = 12, y0 = y1 - h, z0 = 1;
                    pods[age] = Add(
                        Box(V(x0, y0, z0), V(x0 + w, y1, z0 + w), t),
                        Box(V(7.5, y1, 0), V(8.5, 15, 2), t, new Opt
                        {
                            uv = new Dictionary<char, double[]>
                            {
                                ['e'] = new double[] { 0, 0, 2, 3 }, ['w'] = new double[] { 0, 0, 2, 3 }, ['u'] = new double[] { 0, 0, 1, 2 },
                                ['d'] = new double[] { 0, 0, 1, 2 }, ['s'] = new double[] { 0, 0, 1, 3 }, ['n'] = new double[] { 0, 0, 1, 3 }
                            }
                        }));
                }
                var v = new MPart[12][];
                for (int m = 0; m < 12; m++) v[m] = new[] { new MPart(pods[Math.Min(2, m >> 2)], 0, FACING_Y[m & 3]) };
                MODEL_VAR["COCOA"] = v;
            }

            // ---- redstone ----
            // the stub of a redstone torch standing on a base at height y0 (top 5 pixels of the torch texture)
            Func<double, double, double, string, MElem> stub = (x, z, y0, tex) => Box(V(x, y0, z), V(x + 2, y0 + 5, z + 2), tex,
                new Opt
                {
                    uv = new Dictionary<char, double[]>
                    {
                        ['e'] = new double[] { 7, 6, 9, 11 }, ['w'] = new double[] { 7, 6, 9, 11 }, ['s'] = new double[] { 7, 6, 9, 11 },
                        ['n'] = new double[] { 7, 6, 9, 11 }, ['u'] = new double[] { 7, 6, 9, 8 }, ['d'] = new double[] { 7, 13, 9, 15 }
                    }
                });
            // lever (meta: face 0 floor / 1 wall / 2 ceiling << 2 | facing, +16 on): a stone base and a handle
            // tilted one way or the other; built on the floor, turned onto walls and ceilings
            {
                int[] lever = new int[2];
                for (int on = 0; on < 2; on++)
                    lever[on] = Add(
                        Box(V(5, 0, 4), V(11, 3, 12), "cobblestone"),
                        Box(V(7, 1, 7), V(9, 11, 9), "lever_handle", new Opt { r = new MRot(8, 1, 8, 0, on != 0 ? -40 : 40, 0) }));
                var v = new MPart[32][];
                for (int m = 0; m < 32; m++)
                {
                    int face = (m >> 2) & 3, f = m & 3, M = lever[(m >> 4) & 1];
                    v[m] = new[] { new MPart(M, face == 1 ? 90 : face == 2 ? 180 : 0, FACING_Y[f]) };
                }
                MODEL_VAR["LEVER"] = v;
            }
            // repeater (meta: output facing | delay << 2 | on << 4 | locked << 5): a stone slab, a torch at
            // the input end and one that sits further back the longer the delay
            {
                var rep = new List<int>();
                for (int k = 0; k < 8; k++)
                {
                    int delay = k & 3, on = k >> 2;
                    string t = on != 0 ? "redstone_torch" : "redstone_torch_off";
                    rep.Add(Add(Box(V(0, 0, 0), V(16, 2, 16), new Tx { u = on != 0 ? "repeater_on" : "repeater", d = "smooth_stone", side = "smooth_stone" }),
                        stub(7, 11, 2, t), stub(7, 2 + delay * 2, 2, t)));
                }
                var v = new MPart[64][];
                for (int m = 0; m < 64; m++) v[m] = new[] { new MPart(rep[((m >> 2) & 3) | (((m >> 4) & 1) << 2)], 0, FACING_Y[m & 3]) };
                MODEL_VAR["REPEATER"] = v;
            }
            // comparator (meta: output facing | subtract << 2 | output 0-15 << 3): two rear torches lit while
            // it outputs, a front torch lit in subtract mode
            {
                var cmp = new List<int>();
                for (int k = 0; k < 4; k++)
                {
                    int sub = k & 1, on = k >> 1;
                    cmp.Add(Add(Box(V(0, 0, 0), V(16, 2, 16), new Tx { u = on != 0 ? "comparator_on" : "comparator", d = "smooth_stone", side = "smooth_stone" }),
                        stub(3, 11, 2, on != 0 ? "redstone_torch" : "redstone_torch_off"), stub(11, 11, 2, on != 0 ? "redstone_torch" : "redstone_torch_off"),
                        stub(7, 2, 0, sub != 0 ? "redstone_torch" : "redstone_torch_off")));
                }
                var v = new MPart[128][];
                for (int m = 0; m < 128; m++) v[m] = new[] { new MPart(cmp[((m >> 2) & 1) | ((m >> 3) != 0 ? 2 : 0)], 0, FACING_Y[m & 3]) };
                MODEL_VAR["COMPARATOR"] = v;
            }
            // six-way blocks built facing up, turned like the barrel (0-3 horizontal, 4 up, 5 down)
            // (metas 6 and 7 are unused; the original reads FACING_Y past its end there, i.e. no y turn)
            Func<int, int, MPart> six = (M, d) => d == 4 ? new MPart(M, 0, 0) : d == 5 ? new MPart(M, 180, 0) : new MPart(M, 90, d < 4 ? FACING_Y[d] : 0);
            // observer (meta: facing toward the watched block | on << 3)
            {
                int[] obs = new int[2];
                for (int on = 0; on < 2; on++)
                    obs[on] = Add(Box(V(0, 0, 0), V(16, 16, 16), new Tx { u = "observer_front", d = on != 0 ? "observer_back_on" : "observer_back", side = "observer_side" }));
                var v = new MPart[16][];
                for (int m = 0; m < 16; m++) v[m] = new[] { six(obs[(m >> 3) & 1], m & 7) };
                MODEL_VAR["OBSERVER"] = v;
            }
            // pistons (meta: facing | extended << 3) and the head (meta: facing | sticky << 3)
            {
                Func<int, int> bse = st => Add(Box(V(0, 0, 0), V(16, 16, 16), new Tx { u = st != 0 ? "piston_top_sticky" : "piston_top", d = "piston_bottom", side = "piston_side" }));
                int ext = Add(Box(V(0, 0, 0), V(16, 12, 16), new Tx { u = "piston_inner", d = "piston_bottom", side = "piston_side" }));
                Func<int, int> head = st => Add(
                    Box(V(0, 12, 0), V(16, 16, 16), new Tx { u = st != 0 ? "piston_top_sticky" : "piston_top", d = "piston_top", side = "piston_side" }),
                    Box(V(6, 0, 6), V(10, 12, 10), "piston_side", new Opt { uv = Uv("ewsn", new double[] { 0, 0, 4, 12 }), skip = "ud" }));
                int[] B0 = { bse(0), bse(1) }, H = { head(0), head(1) };
                foreach (var (key, st) in new[] { ("PISTON", 0), ("STICKY_PISTON", 1) })
                {
                    var v = new MPart[16][];
                    for (int m = 0; m < 16; m++) v[m] = new[] { six(((m >> 3) & 1) != 0 ? ext : B0[st], m & 7) };
                    MODEL_VAR[key] = v;
                }
                var hv = new MPart[16][];
                for (int m = 0; m < 16; m++) hv[m] = new[] { six(H[(m >> 3) & 1], m & 7) };
                MODEL_VAR["PISTON_HEAD"] = hv;
            }

            // selection boxes: the bounds of each variant's elements, turned like the parts
            foreach (var kv in MODEL_VAR)
            {
                var vars = kv.Value;
                var boxes = new double[vars.Length][];
                for (int vi = 0; vi < vars.Length; vi++)
                {
                    double[] lo = { 16, 16, 16 }, hi = { 0, 0, 0 };
                    foreach (var part in vars[vi])
                        foreach (var E in MODEL_LIST[part.Model])
                            for (int c = 0; c < 8; c++)
                            {
                                var q = Turn((c & 1) != 0 ? E.To[0] : E.From[0], (c & 2) != 0 ? E.To[1] : E.From[1], (c & 4) != 0 ? E.To[2] : E.From[2], part.Rx, part.Ry);
                                for (int i = 0; i < 3; i++) { lo[i] = Math.Min(lo[i], Math.Max(0, q[i])); hi[i] = Math.Max(hi[i], Math.Min(16, q[i])); }
                            }
                    var bx = new[] { lo[0] / 16, lo[1] / 16, lo[2] / 16, hi[0] / 16, hi[1] / 16, hi[2] / 16 };
                    for (int i = 0; i < 6; i++) bx[i] = Math.Floor(bx[i] * 1e4 + 0.5) / 1e4;
                    boxes[vi] = bx;
                }
                MODEL_BOX[kv.Key] = boxes;
            }
        }

        static double[] Turn(double px, double py, double pz, int rx, int ry)
        {
            double x = px - 8, y = py - 8, z = pz - 8, t;
            double cx = Math.Cos(rx * Math.PI / 180), sx = Math.Sin(rx * Math.PI / 180), cy = Math.Cos(ry * Math.PI / 180), sy = Math.Sin(ry * Math.PI / 180);
            t = y * cx + z * sx; z = -y * sx + z * cx; y = t;
            t = x * cy - z * sy; z = x * sy + z * cy; x = t;
            return new[] { x + 8, y + 8, z + 8 };
        }
    }
}
