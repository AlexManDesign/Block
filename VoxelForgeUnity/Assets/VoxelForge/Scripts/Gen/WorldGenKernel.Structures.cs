// Voxel Forge — Unity port. World generator: structures (pyramid/outpost/portal/ship/well), mineshafts,
// structure metadata, dense chunk generation pipeline.
using System;
using System.Collections.Generic;
using System.Linq;

namespace VoxelForge
{
    public sealed partial class WorldGenKernel
    {
        sealed class StructCfg { public int salt, spacing, sep, half, flat, flatR, depth, clearHalf; public double freq; public bool water; }
        static readonly Dictionary<string, StructCfg> STRUCT_CFG = new Dictionary<string, StructCfg> {
            { "pyramid", new StructCfg { salt = 14357617, spacing = 32, sep = 8, half = 10, flat = 8, depth = 15 } },
            { "outpost", new StructCfg { salt = 165745296, spacing = 32, sep = 8, half = 7, flat = 6, freq = 0.2, depth = 2 } },
            { "portal", new StructCfg { salt = 34222645, spacing = 40, sep = 15, half = 5, flat = 0, depth = 2 } },
            { "ship", new StructCfg { salt = 165745295, spacing = 24, sep = 4, half = 14, flat = 6, flatR = 4, depth = 2, clearHalf = 4, water = true } } };
        static readonly string[] STRUCT_TYPES = { "pyramid", "outpost", "portal", "ship" };
        public sealed class Structure { public string type; public int wx, wz, baseY, rot, acx, acz, variant; public bool beached; }
        readonly Dictionary<string, Structure> structureCache = new Dictionary<string, Structure>();
        static readonly HashSet<string> OUTPOST_BIOMES = new HashSet<string> { "PLAINS", "DESERT", "SAVANNA", "TAIGA", "SNOWY", "SNOWY_TAIGA", "MEADOW", "GROVE", "SNOWY_SLOPES", "CHERRY_GROVE", "FROZEN_PEAKS", "JAGGED_PEAKS", "STONY_PEAKS" };
        static readonly HashSet<string> SHIP_BIOMES = new HashSet<string> { "OCEAN", "COLD_OCEAN", "LUKEWARM_OCEAN", "WARM_OCEAN", "FROZEN_OCEAN", "BEACH", "SNOWY_BEACH" };
        static readonly object[][] MAIN_OUTPOST_PAL = {
            new object[] { "DARK_LOG", 0, 2 }, new object[] { "COBBLE", 0, 0 }, new object[] { "BIRCH_PLANKS", 0, 0 }, new object[] { "DARK_PLANKS", 0, 0 }, new object[] { "DARK_LOG", 1, 2 },
            new object[] { "DARK_LOG", 2, 2 }, new object[] { "DARK_PLANKS_SLAB", 1, 0 }, new object[] { "COBBLE_STAIRS", 3, 1 }, new object[] { "COBBLE_STAIRS", 0, 1 },
            new object[] { "DARK_PLANKS_SLAB", 0, 0 }, new object[] { "DARK_PLANKS_FENCE", 0, 0 }, new object[] { "DARK_PLANKS_STAIRS", 6, 1 }, new object[] { "DARK_PLANKS_STAIRS", 4, 1 },
            new object[] { "DARK_PLANKS_STAIRS", 5, 1 }, new object[] { "DARK_PLANKS_STAIRS", 7, 1 }, new object[] { "COBBLE_STAIRS", 7, 1 }, new object[] { "COBBLE_SLAB", 1, 0 },
            new object[] { "COBBLE_STAIRS", 4, 1 }, new object[] { "COBBLE_STAIRS", 6, 1 }, new object[] { "COBBLE_STAIRS", 5, 1 }, new object[] { "COBBLE_STAIRS", 1, 1 },
            new object[] { "COBBLE_STAIRS", 2, 1 }, new object[] { "COBBLE_WALL", 0, 0 }, new object[] { "TORCH", 0, 0 }, new object[] { "CHEST", 1, 1 } };
        static readonly Dictionary<string, string> MAIN_KEY_ALIAS = new Dictionary<string, string> { { "DARK_PLANKS_SLAB", "DARK_SLAB" }, { "DARK_PLANKS_FENCE", "DARK_FENCE" }, { "DARK_PLANKS_STAIRS", "DARK_STAIRS" }, { "STONE_PRESSURE_PLATE", "STONE_PLATE" } };
        static int[] SHIP_ALL, SHIP_BEACHABLE; static double[] SHIP_ALL_WEIGHT, SHIP_BEACH_WEIGHT;
        static void ensureShipTables()
        {
            if (SHIP_ALL != null) return;
            var ships = GenData.ships;
            var all = Enumerable.Range(0, ships.Length).ToArray();
            var beach = all.Where(i => ships[i].beachable).ToArray();
            Func<int, double> shipWeight = i => ships[i].name.StartsWith("with_mast", StringComparison.Ordinal) ? 5 : ships[i].beachable ? 2 : 1;
            Func<int[], double[]> prefix = list => { double sum = 0; return list.Select(i => sum += shipWeight(i)).ToArray(); };
            SHIP_ALL_WEIGHT = prefix(all); SHIP_BEACH_WEIGHT = prefix(beach); SHIP_BEACHABLE = beach; SHIP_ALL = all;
        }
        static int weightedShip(int[] list, double[] prefix, double q)
        {
            double t = q * prefix[prefix.Length - 1];
            for (int i = 0; i < list.Length; i++) if (t < prefix[i]) return list[i];
            return list[list.Length - 1];
        }
        static bool structureBiomeOk(string type, string bi)
        {
            return type == "pyramid" ? bi == "DESERT" : type == "outpost" ? OUTPOST_BIOMES.Contains(bi) : type == "ship" ? SHIP_BIOMES.Contains(bi) : true;
        }
        double structSeedHash(double x, double z) { return hash2Main(x, z); }
        Func<double> structRand(int rx, int rz, int salt)
        {
            int n = toInt32(structSeedHash(rx * 3 + salt * 17.0 + 0.31, rz * 5 - salt * 7.0 + 0.77) * 4294967296.0);
            return () =>
            {
                n = unchecked(n + 1831565813);
                int t = imul(n ^ ushr(n, 15), 1 | n);
                t = unchecked(t + imul(t ^ ushr(t, 7), 61 | t)) ^ t;
                return (uint)(t ^ ushr(t, 14)) / 4294967296.0;
            };
        }
        Structure structureCandidate(string type, int rx, int rz)
        {
            ensureShipTables();
            var cfg = STRUCT_CFG[type]; var key = seed + ":" + cfg.salt + ":" + rx + ":" + rz; Structure cached;
            if (structureCache.TryGetValue(key, out cached)) return cached;
            var r = structRand(rx, rz, cfg.salt);
            int span = cfg.spacing - cfg.sep, acx = rx * cfg.spacing + (int)Math.Floor(r() * span), acz = rz * cfg.spacing + (int)Math.Floor(r() * span);
            double freqRoll = r(); int rot = (int)Math.Floor(r() * 4); double waterRoll = cfg.water ? r() : 0;
            int wx = acx * CHUNK + 8, wz = acz * CHUNK + 8;
            Structure o = null;
            do
            {
                if (cfg.freq != 0 && freqRoll >= cfg.freq) break;
                var bi = biomeAt(wx, wz);
                if (!structureBiomeOk(type, bi)) break;
                int baseY = generatedSurfaceAt(wx, wz); bool beached = false; int variant = 0;
                if (cfg.water)
                {
                    beached = bi == "BEACH" || bi == "SNOWY_BEACH";
                    if (beached) { if (baseY < SEA - 1 || baseY > SEA + 3) break; }
                    else { int depth = SEA - baseY; if (depth < 5 || depth > 30) break; }
                    variant = beached ? weightedShip(SHIP_BEACHABLE, SHIP_BEACH_WEIGHT, waterRoll) : weightedShip(SHIP_ALL, SHIP_ALL_WEIGHT, waterRoll);
                }
                else if (baseY <= SEA + 1) break;
                if (baseY + 24 >= WORLD_MAX_Y || baseY - cfg.depth - 2 <= WORLD_MIN_Y) break;
                if (cfg.flat != 0)
                {
                    int R = cfg.flatR != 0 ? cfg.flatR : cfg.half;
                    double h1 = columnInfo(wx - R, wz - R).origH, h2 = columnInfo(wx + R, wz - R).origH, h3 = columnInfo(wx - R, wz + R).origH, h4 = columnInfo(wx + R, wz + R).origH;
                    double mn = Math.Min(baseY, Math.Min(Math.Min(h1, h2), Math.Min(h3, h4))), mx = Math.Max(baseY, Math.Max(Math.Max(h1, h2), Math.Max(h3, h4)));
                    if (mx - mn > cfg.flat) break;
                }
                o = new Structure { type = type, wx = wx, wz = wz, baseY = baseY, rot = rot, acx = acx, acz = acz, beached = beached, variant = variant };
            } while (false);
            structureCache[key] = o;
            return o;
        }
        List<Structure> nearbyStructures(string type, int cx, int cz)
        {
            var cfg = STRUCT_CFG[type]; var o = new List<Structure>();
            int a = ffloor((cx - 2) / (double)cfg.spacing), b = ffloor((cx + 2) / (double)cfg.spacing), c = ffloor((cz - 2) / (double)cfg.spacing), d = ffloor((cz + 2) / (double)cfg.spacing);
            for (int rx = a; rx <= b; rx++)
                for (int rz = c; rz <= d; rz++)
                {
                    var s = structureCandidate(type, rx, rz);
                    if (s != null && Math.Abs(s.acx - cx) <= 2 && Math.Abs(s.acz - cz) <= 2) o.Add(s);
                }
            return o;
        }
        static void rotXZ(int x, int z, int r, out int ox, out int oz)
        {
            r &= 3;
            if (r == 0) { ox = x; oz = z; } else if (r == 1) { ox = -z; oz = x; } else if (r == 2) { ox = -x; oz = -z; } else { ox = z; oz = -x; }
        }
        static int rotateMainMeta(int meta, int kind, int rot)
        {
            if (kind == 1) return (meta & ~3) | (((meta & 3) + (rot & 3)) & 3);
            if (kind == 2 && (rot & 1) != 0 && meta != 0) return meta == 1 ? 2 : 1;
            return meta;
        }
        static int inferMainTransform(string key)
        {
            return key == "CHEST" || key == "OAK_DOOR" || key == "OAK_TRAPDOOR" || key.EndsWith("_STAIRS", StringComparison.Ordinal) ? 1 : key.EndsWith("_LOG", StringComparison.Ordinal) || key == "LOG" ? 2 : 0;
        }
        static int resolveMainBlock(string key, int rawMeta = 0)
        {
            if (key == "OAK_DOOR")
            {
                bool top = (rawMeta & 4) != 0, open = (rawMeta & 8) != 0;
                return open ? (top ? B.DOOR_OPEN_TOP : B.DOOR_OPEN) : top ? B.DOOR_TOP : B.DOOR;
            }
            string k; if (!MAIN_KEY_ALIAS.TryGetValue(key, out k)) k = key;
            int id; return B.NAMES.TryGetValue(k, out id) ? id : B.AIR;
        }
        static readonly string[] FACINGS = { "south", "west", "north", "east" };
        static JObj mainMetaObject(string key, int raw)
        {
            if (key.EndsWith("_STAIRS", StringComparison.Ordinal)) return new JObj { { "facing", FACINGS[raw & 3] }, { "upper", (raw & 4) != 0 } };
            if (key.EndsWith("_SLAB", StringComparison.Ordinal)) return new JObj { { "upper", (raw & 1) != 0 } };
            if (key == "CHEST") return new JObj { { "facing", FACINGS[raw & 3] } };
            if (key == "OAK_DOOR") return new JObj { { "facing", FACINGS[raw & 3] }, { "open", (raw & 8) != 0 }, { "hinge", (raw & 16) != 0 }, { "top", (raw & 4) != 0 } };
            if (key == "OAK_TRAPDOOR") return new JObj { { "facing", FACINGS[raw & 3] }, { "upper", (raw & 4) != 0 }, { "open", (raw & 8) != 0 } };
            if (key == "LOG" || key.EndsWith("_LOG", StringComparison.Ordinal)) return new JObj { { "axis", raw == 1 ? "x" : raw == 2 ? "z" : "y" } };
            if (key == "SHIP_WHEEL") return new JObj { { "facing", FACINGS[raw & 3] } };
            return null;
        }
        static void sPut(byte[] data, int cx, int cz, Structure s, int lx, int dy, int lz, int id, bool onlyReplace = false)
        {
            int qx, qz; rotXZ(lx, lz, s.rot, out qx, out qz);
            int x = s.wx + qx, z = s.wz + qz, y = s.baseY + dy;
            if (y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return;
            int px = x - cx * CHUNK, pz = z - cz * CHUNK;
            if (px < 0 || px >= CHUNK || pz < 0 || pz >= CHUNK) return;
            int ii = idx(px, y, pz);
            if (!onlyReplace || data[ii] == B.AIR || data[ii] == B.WATER) data[ii] = (byte)id;
        }
        static void sFillDown(byte[] data, int cx, int cz, Structure s, int lx, int lz, int id, int count, int start = -1)
        {
            int qx, qz; rotXZ(lx, lz, s.rot, out qx, out qz);
            int x = s.wx + qx, z = s.wz + qz, px = x - cx * CHUNK, pz = z - cz * CHUNK;
            if (px < 0 || px >= CHUNK || pz < 0 || pz >= CHUNK) return;
            for (int k = 0; k < count; k++)
            {
                int y = s.baseY + start - k;
                if (y <= WORLD_MIN_Y) return;
                int ii = idx(px, y, pz), old = data[ii];
                if (old != B.AIR && old != B.WATER) return;
                data[ii] = (byte)id;
            }
        }
        static void placeMainEntry(byte[] data, int cx, int cz, Structure s, int lx, int dy, int lz, string key, int meta, int transform)
        {
            int raw = rotateMainMeta(meta, transform, s.rot);
            sPut(data, cx, cz, s, lx, dy, lz, resolveMainBlock(key, raw));
        }
        static void buildPyramid(byte[] data, int cx, int cz, Structure s)
        {
            for (int x = -10; x <= 10; x++) for (int z = -10; z <= 10; z++) sFillDown(data, cx, cz, s, x, z, B.SANDSTONE, 12, -5);
            foreach (var e in GenData.pyramid) placeMainEntry(data, cx, cz, s, e.x, e.y, e.z, e.key, e.meta, inferMainTransform(e.key));
        }
        static void buildOutpost(byte[] data, int cx, int cz, Structure s)
        {
            for (int x = -7; x <= 7; x++) for (int z = -7; z <= 7; z++) for (int y = 1; y < 21; y++) sPut(data, cx, cz, s, x, y, z, B.AIR);
            var ob = GenData.outpost;
            for (int i = 0; i < ob.Length; i += 4)
            {
                var p = MAIN_OUTPOST_PAL[ob[i + 3]];
                int raw = rotateMainMeta((int)p[1], (int)p[2], s.rot), lx = ob[i] - 7, dy = ob[i + 1], lz = ob[i + 2] - 7;
                sPut(data, cx, cz, s, lx, dy, lz, resolveMainBlock((string)p[0], raw));
                if (dy == 0) sFillDown(data, cx, cz, s, lx, lz, B.COBBLE, 8);
            }
        }
        void buildPortal(byte[] data, int cx, int cz, Structure s)
        {
            for (int z = -3; z <= 3; z++)
                for (int x = -3; x <= 3; x++)
                {
                    double n = hash3Main(s.wx + x * 7, 11, s.wz + z * 7);
                    if (Math.Abs(x) + Math.Abs(z) > 4 && n > 0.5) continue;
                    sPut(data, cx, cz, s, x, 0, z, n < 0.15 ? B.MAGMA_BLOCK : B.NETHERRACK);
                    sFillDown(data, cx, cz, s, x, z, B.NETHERRACK, 3);
                    for (int y = 1; y <= 6; y++) sPut(data, cx, cz, s, x, y, z, B.AIR);
                }
            for (int x = -2; x <= 1; x++)
                for (int y = 1; y <= 5; y++)
                {
                    bool edge = x == -2 || x == 1 || y == 1 || y == 5;
                    if (!edge) { sPut(data, cx, cz, s, x, y, 0, B.AIR); continue; }
                    if (y <= 2 || hash3Main(s.wx + x * 13, y * 5, s.wz - x * 3) < 0.6)
                        sPut(data, cx, cz, s, x, y, 0, hash3Main(s.wx - x * 5, y * 9 + 4, s.wz + x * 11) < 0.25 ? B.CRYING_OBSIDIAN : B.OBSIDIAN);
                }
            sPut(data, cx, cz, s, -3, 1, 1, B.GOLD_BLOCK);
            if (hash3Main(s.wx, 3, s.wz) < 0.5) sPut(data, cx, cz, s, 2, 1, -2, B.GOLD_BLOCK);
            sPut(data, cx, cz, s, 2, 1, 2, B.CHEST);
        }
        static void buildShip(byte[] data, int cx, int cz, Structure s)
        {
            var t = GenData.ships[s.variant];
            for (int i = 0; i < t.blocks.Length; i += 4)
            {
                int pi = t.blocks[i + 3]; int raw = rotateMainMeta(t.palMeta[pi], t.palTransform[pi], s.rot);
                sPut(data, cx, cz, s, t.blocks[i] - t.axo, t.blocks[i + 1], t.blocks[i + 2] - t.azo, resolveMainBlock(t.palKey[pi], raw));
            }
            if (!s.beached && t.beachable) { var m = t.mount; sPut(data, cx, cz, s, m[0] - t.axo, m[1], m[2] - t.azo, B.SHIP_WHEEL); }
        }
        // NOTE: the reference declares desertWellAt/buildWell/applyStructures twice; JS hoisting keeps the later versions (ported here).
        Structure desertWellAt(int cx, int cz)
        {
            if (structSeedHash(cx * 13 + 917.3, cz * 13 - 411.7) >= 0.001) return null;
            int x = cx * CHUNK + 4 + (int)Math.Floor(structSeedHash(cx * 5 + 77, cz * 5 - 33) * 8), z = cz * CHUNK + 4 + (int)Math.Floor(structSeedHash(cx * 5 - 51, cz * 5 + 29) * 8);
            if (biomeAt(x, z) != "DESERT") return null;
            int h = generatedSurfaceAt(x, z);
            return h <= SEA + 1 || h + 6 >= WORLD_MAX_Y ? null : new Structure { type = "well", wx = x, wz = z, baseY = h, rot = 0 };
        }
        static void buildWell(byte[] data, int cx, int cz, Structure s)
        {
            for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    sPut(data, cx, cz, s, x, 0, z, B.SANDSTONE);
                    sFillDown(data, cx, cz, s, x, z, B.SANDSTONE, 4);
                    int d = Math.Max(Math.Abs(x), Math.Abs(z));
                    sPut(data, cx, cz, s, x, 1, z, d == 2 ? B.SANDSTONE_SLAB : d == 1 ? B.SANDSTONE : B.AIR);
                }
            sPut(data, cx, cz, s, 0, 0, 0, B.WATER); sPut(data, cx, cz, s, 0, -1, 0, B.WATER); sPut(data, cx, cz, s, 0, -2, 0, B.SANDSTONE);
            foreach (var x in new[] { -1, 1 }) foreach (var z in new[] { -1, 1 }) { sPut(data, cx, cz, s, x, 2, z, B.SANDSTONE); sPut(data, cx, cz, s, x, 3, z, B.SANDSTONE); }
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) sPut(data, cx, cz, s, x, 4, z, B.SANDSTONE_SLAB);
        }
        bool surfaceStructureReservedAt(int x, int z)
        {
            int cx = x >> 4, cz = z >> 4;
            foreach (var type in STRUCT_TYPES)
            {
                var cfg = STRUCT_CFG[type]; int R = (cfg.clearHalf != 0 ? cfg.clearHalf : cfg.half) + 1;
                foreach (var s in nearbyStructures(type, cx, cz)) if (Math.Abs(x - s.wx) <= R && Math.Abs(z - s.wz) <= R) return true;
            }
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var w = desertWellAt(cx + dx, cz + dz);
                    if (w != null && Math.Abs(x - w.wx) <= 3 && Math.Abs(z - w.wz) <= 3) return true;
                }
            return false;
        }
        void applyStructures(byte[] data, int cx, int cz)
        {
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) { var w = desertWellAt(cx + dx, cz + dz); if (w != null) buildWell(data, cx, cz, w); }
            foreach (var type in STRUCT_TYPES)
                foreach (var s in nearbyStructures(type, cx, cz))
                {
                    if (type == "pyramid") buildPyramid(data, cx, cz, s);
                    else if (type == "outpost") buildOutpost(data, cx, cz, s);
                    else if (type == "portal") buildPortal(data, cx, cz, s);
                    else buildShip(data, cx, cz, s);
                }
        }
        public List<GenMetaEntry> structureMetaForChunk(int cx, int cz)
        {
            var o = new List<GenMetaEntry>(); int x0 = cx * CHUNK, z0 = cz * CHUNK;
            Action<Structure, int, int, int, string, int, int> add = (s, lx, dy, lz, key, meta, transform) =>
            {
                int raw = rotateMainMeta(meta, transform, s.rot); var m = mainMetaObject(key, raw);
                if (m == null) return;
                int qx, qz; rotXZ(lx, lz, s.rot, out qx, out qz);
                int x = s.wx + qx, z = s.wz + qz, y = s.baseY + dy, px = x - x0, pz = z - z0;
                if (px >= 0 && px < CHUNK && pz >= 0 && pz < CHUNK && y >= WORLD_MIN_Y && y < WORLD_MAX_Y) o.Add(new GenMetaEntry(px, y - WORLD_MIN_Y, pz, m));
            };
            foreach (var type in STRUCT_TYPES)
                foreach (var s in nearbyStructures(type, cx, cz))
                {
                    if (type == "pyramid") { foreach (var e in GenData.pyramid) add(s, e.x, e.y, e.z, e.key, e.meta, inferMainTransform(e.key)); }
                    else if (type == "outpost")
                    {
                        var ob = GenData.outpost;
                        for (int i = 0; i < ob.Length; i += 4) { var p = MAIN_OUTPOST_PAL[ob[i + 3]]; add(s, ob[i] - 7, ob[i + 1], ob[i + 2] - 7, (string)p[0], (int)p[1], (int)p[2]); }
                    }
                    else if (type == "portal") add(s, 2, 1, 2, "CHEST", 0, 1);
                    else
                    {
                        var t = GenData.ships[s.variant];
                        for (int i = 0; i < t.blocks.Length; i += 4) { int pi = t.blocks[i + 3]; add(s, t.blocks[i] - t.axo, t.blocks[i + 1], t.blocks[i + 2] - t.azo, t.palKey[pi], t.palMeta[pi], t.palTransform[pi]); }
                        if (!s.beached && t.beachable) { var m = t.mount; add(s, m[0] - t.axo, m[1], m[2] - t.azo, "SHIP_WHEEL", 2, 1); }
                    }
                }
            return o;
        }
        public string structureChestKindAt(int x, int y, int z)
        {
            int cx = x >> 4, cz = z >> 4;
            var mineKind = mineshaftChestKindAt(x, y, z);
            if (mineKind != null) return mineKind;
            foreach (var type in STRUCT_TYPES)
                foreach (var s in nearbyStructures(type, cx, cz))
                {
                    var tests = new List<object[]>();
                    if (type == "pyramid") { tests.Add(new object[] { 2, -11, 0, "pyramid" }); tests.Add(new object[] { -2, -11, 0, "pyramid" }); tests.Add(new object[] { 0, -11, 2, "pyramid" }); tests.Add(new object[] { 0, -11, -2, "pyramid" }); }
                    else if (type == "outpost") tests.Add(new object[] { 2, 14, 3, "outpost" });
                    else if (type == "portal") tests.Add(new object[] { 2, 1, 2, "portal" });
                    else
                    {
                        var t = GenData.ships[s.variant];
                        for (int i = 0; i < t.chestPos.Count; i++) tests.Add(new object[] { t.chestPos[i][0] - t.axo, t.chestPos[i][1], t.chestPos[i][2] - t.azo, t.chestKind[i] });
                    }
                    foreach (var a in tests)
                    {
                        int qx, qz; rotXZ((int)a[0], (int)a[2], s.rot, out qx, out qz);
                        if (s.wx + qx == x && s.baseY + (int)a[1] == y && s.wz + qz == z) return (string)a[3];
                    }
                }
            return null;
        }

        // ---------------- mineshafts ----------------
        const double MINESHAFT_CHANCE = 1.0 / 260; const int MINESHAFT_NEAR = 9, MINESHAFT_RADIUS = 7, MINESHAFT_MAX_PIECES = 46;
        public sealed class MinePiece { public int k, cx, cz, y, ax, dir, sx, sz, sy, len, chestI = -1, yd; public bool rails, webs; }
        public sealed class MinePlan { public List<MinePiece> pieces; public List<int[]> chests; public int minx, maxx, minz, maxz; }
        Func<double> mineshaftRng(int A, int e)
        {
            int r = imul(unchecked(A + seed), 668265263) ^ imul(unchecked(e - seed), 374761393) ^ unchecked((int)2654435769u);
            return () =>
            {
                r = unchecked(r + 1831565813);
                int t = imul(r ^ ushr(r, 15), 1 | r);
                t = unchecked(t + imul(t ^ ushr(t, 7), 61 | t)) ^ t;
                return (uint)(t ^ ushr(t, 14)) / 4294967296.0;
            };
        }
        double mineshaftHash(int A, int e, int r)
        {
            int t = imul(unchecked(A + seed), unchecked((int)2246822507u)) ^ imul(unchecked(e - 1640531527), unchecked((int)3266489909u)) ^ imul(unchecked(r + 668265263), unchecked((int)2654435761u));
            t ^= ushr(t, 15); t = imul(t, 739982445); t ^= ushr(t, 13);
            return ((uint)t % 100000) / 100000.0;
        }
        static int mineDX(int ax, int dir) { return ax == 0 ? dir : 0; }
        static int mineDZ(int ax, int dir) { return ax == 1 ? dir : 0; }
        sealed class MineQ { public int x, z, y, ax, dir; }
        MinePlan planMineshaft(int cellX, int cellZ)
        {
            var key = seed + ":" + cellX + "," + cellZ; MinePlan cached;
            if (mineshaftPlanCache.TryGetValue(key, out cached)) return cached;
            if (mineshaftPlanCache.Count > 20000) mineshaftPlanCache.Clear();
            var rng = mineshaftRng(cellX, cellZ);
            if (rng() >= MINESHAFT_CHANCE) { mineshaftPlanCache[key] = null; return null; }
            int cx = cellX * CHUNK + 4 + (int)Math.Floor(rng() * 8), cz = cellZ * CHUNK + 4 + (int)Math.Floor(rng() * 8), surface = generatedSurfaceAt(cx, cz);
            bool wet = rng() < 0.22 && surface > SEA + 5;
            int y;
            if (wet) y = surface - 6;
            else
            {
                int lo = WORLD_MIN_Y + 16, hi = SEA - 14;
                y = lo + (int)Math.Floor(rng() * (hi - lo));
                if (surface < y + 12 || generatedSurfaceAt(cx - 4, cz - 4) < y + 8 || generatedSurfaceAt(cx + 4, cz + 4) < y + 8 || generatedSurfaceAt(cx - 4, cz + 4) < y + 8 || generatedSurfaceAt(cx + 4, cz - 4) < y + 8)
                { mineshaftPlanCache[key] = null; return null; }
            }
            var pieces = new List<MinePiece>(); var chests = new List<int[]>();
            int minx = cx, maxx = cx, minz = cz, maxz = cz;
            Action<int, int> bounds = (x, z) => { if (x < minx) minx = x; if (x > maxx) maxx = x; if (z < minz) minz = z; if (z > maxz) maxz = z; };
            Func<int, int, bool> inside = (x, z) => Math.Abs((x >> 4) - cellX) <= MINESHAFT_RADIUS && Math.Abs((z >> 4) - cellZ) <= MINESHAFT_RADIUS;
            int margin = wet ? -3 : 2;
            Func<int, int, int, int, int, bool> clearance = (x0, z0, x1, z1, yy) =>
            {
                int need = yy + margin;
                return generatedSurfaceAt(x0, z0) >= need && generatedSurfaceAt(x1, z1) >= need && generatedSurfaceAt(x0, z1) >= need && generatedSurfaceAt(x1, z0) >= need && generatedSurfaceAt((x0 + x1) >> 1, (z0 + z1) >> 1) >= need;
            };
            pieces.Add(new MinePiece { k = 0, cx = cx, cz = cz, y = y });
            bounds(cx - 3, cz - 3); bounds(cx + 3, cz + 3);
            var queue = new List<MineQ>();
            foreach (var st in new[] { new[] { 0, 1 }, new[] { 0, -1 }, new[] { 1, 1 }, new[] { 1, -1 } })
                if (rng() < 0.82) queue.Add(new MineQ { x = cx + mineDX(st[0], st[1]) * 4, z = cz + mineDZ(st[0], st[1]) * 4, y = y, ax = st[0], dir = st[1] });
            for (int loops = 0; queue.Count > 0 && pieces.Count < MINESHAFT_MAX_PIECES && loops++ < 500;)
            {
                var q = queue[0]; queue.RemoveAt(0);
                if (!inside(q.x, q.z)) continue;
                int dx = mineDX(q.ax, q.dir), dz = mineDZ(q.ax, q.dir);
                int len = 5 * (2 + (int)Math.Floor(rng() * 3)), ex = q.x + dx * (len - 1), ez = q.z + dz * (len - 1);
                if (!inside(ex, ez)) { len = Math.Max(6, len - 5); ex = q.x + dx * (len - 1); ez = q.z + dz * (len - 1); }
                if (!clearance(Math.Min(q.x, ex) - 1, Math.Min(q.z, ez) - 1, Math.Max(q.x, ex) + 1, Math.Max(q.z, ez) + 1, q.y + 3)) continue;
                var p = new MinePiece { k = 1, ax = q.ax, dir = q.dir, sx = q.x, sz = q.z, sy = q.y, len = len, rails = rng() < 0.34, webs = rng() < 0.2, chestI = -1 };
                if (rng() < 0.14) { p.chestI = 2 + (int)Math.Floor(rng() * (len - 4)); chests.Add(new[] { q.x + dx * p.chestI, q.y + 1, q.z + dz * p.chestI }); }
                pieces.Add(p);
                bounds(q.x - 1, q.z - 1); bounds(q.x + 1, q.z + 1); bounds(ex - 1, ez - 1); bounds(ex + 1, ez + 1);
                int nx = ex + dx, nz = ez + dz; double branch = rng();
                if (branch < 0.3) continue;
                if (branch < 0.64)
                {
                    pieces.Add(new MinePiece { k = 2, cx = ex, cz = ez, y = q.y });
                    bounds(ex - 1, ez - 1); bounds(ex + 1, ez + 1);
                    if (rng() < 0.85) queue.Add(new MineQ { x = nx, z = nz, y = q.y, ax = q.ax, dir = q.dir });
                    int side = q.ax ^ 1;
                    if (rng() < 0.6) queue.Add(new MineQ { x = ex + mineDX(side, 1) * 2, z = ez + mineDZ(side, 1) * 2, y = q.y, ax = side, dir = 1 });
                    if (rng() < 0.6) queue.Add(new MineQ { x = ex + mineDX(side, -1) * 2, z = ez + mineDZ(side, -1) * 2, y = q.y, ax = side, dir = -1 });
                }
                else if (branch < 0.82)
                {
                    int yd = rng() < 0.5 ? 1 : -1, ny = q.y + yd * 5;
                    if (ny >= WORLD_MIN_Y + 12 && ny <= SEA + 30)
                    {
                        pieces.Add(new MinePiece { k = 3, ax = q.ax, dir = q.dir, sx = nx, sz = nz, sy = q.y, yd = yd, len = 5 });
                        bounds(nx + dx * 4 - 1, nz + dz * 4 - 1); bounds(nx + dx * 4 + 1, nz + dz * 4 + 1);
                        queue.Add(new MineQ { x = nx + dx * 5, z = nz + dz * 5, y = ny, ax = q.ax, dir = q.dir });
                    }
                    else queue.Add(new MineQ { x = nx, z = nz, y = q.y, ax = q.ax, dir = q.dir });
                }
                else queue.Add(new MineQ { x = nx, z = nz, y = q.y, ax = q.ax, dir = q.dir });
            }
            var plan = new MinePlan { pieces = pieces, chests = chests, minx = minx, maxx = maxx, minz = minz, maxz = maxz };
            mineshaftPlanCache[key] = plan;
            return plan;
        }
        public List<MinePlan> mineshaftsForChunk(int cx, int cz)
        {
            var key = seed + ":" + cx + "," + cz; List<MinePlan> hit;
            if (mineshaftChunkCache.TryGetValue(key, out hit) && hit != null) return hit;
            if (mineshaftChunkCache.Count > 6000) mineshaftChunkCache.Clear();
            var o = new List<MinePlan>(); int x0 = cx * CHUNK, z0 = cz * CHUNK;
            for (int dz = -MINESHAFT_NEAR; dz <= MINESHAFT_NEAR; dz++)
                for (int dx = -MINESHAFT_NEAR; dx <= MINESHAFT_NEAR; dx++)
                {
                    var p = planMineshaft(cx + dx, cz + dz);
                    if (p != null && !(p.maxx < x0 - 2 || p.minx > x0 + CHUNK + 2 || p.maxz < z0 - 2 || p.minz > z0 + CHUNK + 2)) o.Add(p);
                }
            mineshaftChunkCache[key] = o;
            return o;
        }
        static bool mineInside(int cx, int cz, int x, int z) { return x >= cx * CHUNK && x < cx * CHUNK + CHUNK && z >= cz * CHUNK && z < cz * CHUNK + CHUNK; }
        static int mineGet(byte[] data, int cx, int cz, int x, int y, int z)
        {
            if (!mineInside(cx, cz, x, z) || y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return B.AIR;
            return data[idx(x - cx * CHUNK, y, z - cz * CHUNK)];
        }
        static bool mineSolid(int id) { return id != B.AIR && id != B.WATER; }
        static void mineClear(byte[] data, int cx, int cz, int x, int y, int z)
        {
            if (!mineInside(cx, cz, x, z) || y <= WORLD_MIN_Y + 1 || y >= WORLD_MAX_Y) return;
            int i = idx(x - cx * CHUNK, y, z - cz * CHUNK), id = data[i];
            if (id != B.BEDROCK && id != B.AIR && id != B.WATER) data[i] = B.AIR;
        }
        static void minePut(byte[] data, int cx, int cz, int x, int y, int z, int id, bool onlyAir = false)
        {
            if (!mineInside(cx, cz, x, z) || y <= WORLD_MIN_Y || y >= WORLD_MAX_Y) return;
            int i = idx(x - cx * CHUNK, y, z - cz * CHUNK);
            if (!onlyAir || data[i] == B.AIR) data[i] = (byte)id;
        }
        static void mineFence(byte[] data, int cx, int cz, int x, int y0, int y1, int z)
        {
            for (int y = y0; y <= y1; y++) minePut(data, cx, cz, x, y, z, B.OAK_FENCE, true);
            for (int d = 1; d <= 6; d++)
            {
                int y = y0 - d;
                if (y <= WORLD_MIN_Y + 1 || !mineInside(cx, cz, x, z)) break;
                if (mineSolid(mineGet(data, cx, cz, x, y, z))) break;
                minePut(data, cx, cz, x, y, z, B.OAK_FENCE, false);
            }
        }
        void buildMineCorridor(byte[] data, int cx, int cz, MinePiece p)
        {
            int dx = mineDX(p.ax, p.dir), dz = mineDZ(p.ax, p.dir), y = p.sy;
            for (int a = 0; a < p.len; a++)
            {
                int x = p.sx + dx * a, z = p.sz + dz * a;
                for (int w = -1; w <= 1; w++)
                {
                    int wx = x + (p.ax == 1 ? w : 0), wz = z + (p.ax == 0 ? w : 0);
                    mineClear(data, cx, cz, wx, y + 1, wz); mineClear(data, cx, cz, wx, y + 2, wz); mineClear(data, cx, cz, wx, y + 3, wz);
                    if (mineInside(cx, cz, wx, wz))
                    {
                        int floor = mineGet(data, cx, cz, wx, y, wz);
                        if ((floor == B.AIR || floor == B.WATER) && mineshaftHash(wx, y, wz) < 0.72) minePut(data, cx, cz, wx, y, wz, B.PLANKS, false);
                    }
                }
                if (a % 5 == 0 && mineshaftHash(p.sx * 7 + a, y, p.sz * 7) > 0.16)
                {
                    int x1 = x + (p.ax == 1 ? -1 : 0), z1 = z + (p.ax == 0 ? -1 : 0), x2 = x + (p.ax == 1 ? 1 : 0), z2 = z + (p.ax == 0 ? 1 : 0);
                    mineFence(data, cx, cz, x1, y + 1, y + 2, z1);
                    mineFence(data, cx, cz, x2, y + 1, y + 2, z2);
                    for (int w = -1; w <= 1; w++) minePut(data, cx, cz, x + (p.ax == 1 ? w : 0), y + 3, z + (p.ax == 0 ? w : 0), B.PLANKS, true);
                }
                if (p.rails && mineInside(cx, cz, x, z) && mineSolid(mineGet(data, cx, cz, x, y, z)) && mineGet(data, cx, cz, x, y + 1, z) == B.AIR) minePut(data, cx, cz, x, y + 1, z, B.RAIL, true);
                if (p.webs)
                    for (int w = -1; w <= 1; w++)
                    {
                        int wx = x + (p.ax == 1 ? w : 0), wz = z + (p.ax == 0 ? w : 0);
                        for (int yy = y + 1; yy <= y + 2; yy++) if (mineshaftHash(wx, yy, wz) < 0.03) minePut(data, cx, cz, wx, yy, wz, B.COBWEB, true);
                    }
                if (a == p.chestI && mineInside(cx, cz, x, z) && mineSolid(mineGet(data, cx, cz, x, y, z))) minePut(data, cx, cz, x, y + 1, z, B.CHEST, true);
            }
        }
        static void buildMineJunction(byte[] data, int cx, int cz, MinePiece p)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int x = p.cx + dx, z = p.cz + dz;
                    mineClear(data, cx, cz, x, p.y + 1, z); mineClear(data, cx, cz, x, p.y + 2, z); mineClear(data, cx, cz, x, p.y + 3, z);
                    if (mineInside(cx, cz, x, z)) { int floor = mineGet(data, cx, cz, x, p.y, z); if (floor == B.AIR || floor == B.WATER) minePut(data, cx, cz, x, p.y, z, B.PLANKS, false); }
                    minePut(data, cx, cz, x, p.y + 3, z, B.PLANKS, true);
                    if (Math.Abs(dx) == 1 && Math.Abs(dz) == 1) mineFence(data, cx, cz, x, p.y + 1, p.y + 2, z);
                }
        }
        static void buildMineRoom(byte[] data, int cx, int cz, MinePiece p)
        {
            for (int dx = -3; dx <= 3; dx++)
                for (int dz = -3; dz <= 3; dz++)
                {
                    int x = p.cx + dx, z = p.cz + dz;
                    for (int y = p.y + 1; y <= p.y + 3; y++) mineClear(data, cx, cz, x, y, z);
                    if (mineInside(cx, cz, x, z)) { int floor = mineGet(data, cx, cz, x, p.y, z); if (floor == B.AIR || floor == B.WATER || floor == B.STONE) minePut(data, cx, cz, x, p.y, z, B.DIRT, false); }
                }
            mineFence(data, cx, cz, p.cx - 2, p.y + 1, p.y + 3, p.cz - 2);
            mineFence(data, cx, cz, p.cx + 2, p.y + 1, p.y + 3, p.cz + 2);
            minePut(data, cx, cz, p.cx, p.y + 1, p.cz, B.CHEST, true);
        }
        static void buildMineRamp(byte[] data, int cx, int cz, MinePiece p)
        {
            int dx = mineDX(p.ax, p.dir), dz = mineDZ(p.ax, p.dir);
            for (int n = 0; n < p.len; n++)
            {
                int x = p.sx + dx * n, z = p.sz + dz * n, y = p.sy + p.yd * n;
                for (int w = -1; w <= 1; w++)
                {
                    int wx = x + (p.ax == 1 ? w : 0), wz = z + (p.ax == 0 ? w : 0);
                    mineClear(data, cx, cz, wx, y + 1, wz); mineClear(data, cx, cz, wx, y + 2, wz); mineClear(data, cx, cz, wx, y + 3, wz); mineClear(data, cx, cz, wx, y + 4, wz);
                    minePut(data, cx, cz, wx, y, wz, B.PLANKS, false);
                }
            }
        }
        void applyMineshafts(byte[] data, int cx, int cz)
        {
            foreach (var ms in mineshaftsForChunk(cx, cz))
                foreach (var p in ms.pieces)
                {
                    if (p.k == 0) buildMineRoom(data, cx, cz, p);
                    else if (p.k == 1) buildMineCorridor(data, cx, cz, p);
                    else if (p.k == 2) buildMineJunction(data, cx, cz, p);
                    else if (p.k == 3) buildMineRamp(data, cx, cz, p);
                }
        }
        string mineshaftChestKindAt(int x, int y, int z)
        {
            foreach (var ms in mineshaftsForChunk(x >> 4, z >> 4))
            {
                foreach (var c in ms.chests) if (c[0] == x && c[1] == y && c[2] == z) return "mineshaft";
                foreach (var p in ms.pieces) if (p.k == 0 && p.cx == x && p.y + 1 == y && p.cz == z) return "mineshaft";
            }
            return null;
        }
        public static string rotateFacing(string f, int r)
        {
            int vx = f == "north" ? 0 : f == "east" ? 1 : f == "south" ? 0 : -1, vz = f == "north" ? -1 : f == "east" ? 0 : f == "south" ? 1 : 0, qx, qz;
            rotXZ(vx, vz, r, out qx, out qz);
            return qx == 1 ? "east" : qx == -1 ? "west" : qz == 1 ? "south" : "north";
        }
        public List<GenMetaEntry> caveVineMetaForDense(byte[] data, WgMeta m, int cx, int cz)
        {
            var o = new List<GenMetaEntry>();
            if (m != null && !m.dirty && m.cx == cx && m.cz == cz)
            {
                if (!m.caveSorted) { m.cave.Sort(); m.caveSorted = true; }
                int last = -1;
                foreach (var p in m.cave)
                {
                    if (p == last) continue;
                    last = p;
                    int lx = p & 15, lz = (p >> 4) & 15, iy = p >> 8;
                    if (data[(iy * CHUNK + lz) * CHUNK + lx] == B.CAVE_VINES) o.Add(new GenMetaEntry(lx, iy, lz, new JObj { { "berries", true } }));
                }
                return o;
            }
            int x0 = cx * CHUNK, z0 = cz * CHUNK;
            for (int iy = 0; iy < WORLD_H; iy++)
                for (int lz = 0; lz < CHUNK; lz++)
                    for (int lx = 0; lx < CHUNK; lx++)
                    {
                        if (data[(iy * CHUNK + lz) * CHUNK + lx] != B.CAVE_VINES) continue;
                        int x = x0 + lx, y = WORLD_MIN_Y + iy, z = z0 + lz;
                        if (hash3Main(x, y, z + 7) < 0.13) o.Add(new GenMetaEntry(lx, iy, lz, new JObj { { "berries", true } }));
                    }
            return o;
        }
        public List<GenMetaEntry> seaPickleMetaForDense(byte[] data, WgMeta m, int cx, int cz)
        {
            var o = new List<GenMetaEntry>();
            if (m != null && !m.dirty && m.cx == cx && m.cz == cz)
            {
                if (!m.seaSorted) { m.sea.Sort(); m.seaSorted = true; }
                foreach (var p in m.sea)
                {
                    int count = (p & 3) + 1, lx = (p >> 2) & 15, lz = (p >> 6) & 15, iy = p >> 10;
                    if (data[(iy * CHUNK + lz) * CHUNK + lx] == B.SEA_PICKLE) o.Add(new GenMetaEntry(lx, iy, lz, new JObj { { "count", (double)count } }));
                }
                return o;
            }
            int x0 = cx * CHUNK, z0 = cz * CHUNK;
            for (int y = WORLD_MIN_Y + 1; y <= SEA; y++)
                for (int lz = 0; lz < CHUNK; lz++)
                    for (int lx = 0; lx < CHUNK; lx++)
                    {
                        int ii = idx(lx, y, lz);
                        if (data[ii] != B.SEA_PICKLE) continue;
                        int x = x0 + lx, z = z0 + lz; bool reef = CORAL_BLOCK_MASK[data[idx(lx, y - 1, lz)]];
                        double r = reef ? hash2Main(x * 5 + 41, z * 5 - 23) : hash2Main(x * 9 - 17, z * 9 + 31);
                        int count = 1 + ((int)Math.Floor(r * 4) & 3);
                        o.Add(new GenMetaEntry(lx, y - WORLD_MIN_Y, lz, new JObj { { "count", (double)count } }));
                    }
            return o;
        }
        void decorateFrozenColumn(byte[] data, int lx, int lz, int x, int z, int floor, string bi)
        {
            int stride = CHUNK * CHUNK, col = lz * CHUNK + lx, depth = SEA - floor, seaI = (SEA - WORLD_MIN_Y) * stride + col;
            if (bi == "FROZEN_OCEAN")
            {
                double shape = valueFbm4Main(x * 0.02 + 911.1, z * 0.02 - 333.3);
                if (shape > 0.72 && depth >= 3)
                {
                    double strength = Math.Min(1, (shape - 0.72) / 0.28);
                    int top = SEA + (int)round(strength * 6), bottom = SEA - 1 - (int)round(strength * 5);
                    if (bottom < floor + 1) bottom = floor + 1;
                    int i = (bottom - WORLD_MIN_Y) * stride + col;
                    for (int y = bottom; y <= top && y < WORLD_MAX_Y - 1; y++, i += stride) data[i] = (byte)B.PACKED_ICE;
                    if (top > SEA + 2 && top + 1 < WORLD_MAX_Y) { int ti = (top + 1 - WORLD_MIN_Y) * stride + col; if (data[ti] == B.AIR) data[ti] = (byte)B.SNOW; }
                }
                else if (data[seaI] == B.WATER) data[seaI] = (byte)B.ICE;
                return;
            }
            if (bi == "FROZEN_RIVER") { if (data[seaI] == B.WATER) data[seaI] = (byte)B.ICE; }
        }
        void decorateAquaticColumn(byte[] data, int lx, int lz, int x, int z, int floor, string bi, List<int> seaMeta)
        {
            int stride = CHUNK * CHUNK, col = lz * CHUNK + lx, depth = SEA - floor, seaI = (SEA - WORLD_MIN_Y) * stride + col;
            double f = hash2Main(x * 5 + 41, z * 5 - 23), w = hash2Main(x * 9 - 17, z * 9 + 31);
            if (bi == "FROZEN_OCEAN" || bi == "FROZEN_RIVER") { decorateFrozenColumn(data, lx, lz, x, z, floor, bi); return; }
            int first = floor + 1;
            if (first >= WORLD_MAX_Y) return;
            int firstI = (first - WORLD_MIN_Y) * stride + col;
            if (data[firstI] != B.WATER) return;
            if (bi == "WARM_OCEAN")
            {
                double reef = valueFbm4Main(x * 0.03 + 123.4, z * 0.03 - 567.8);
                if (depth >= 4 && reef > 0.72)
                {
                    int count = 1 + (int)Math.Floor((reef - 0.72) * 14);
                    if (count > 3) count = 3;
                    int d = 0, i = firstI;
                    for (; d < count && first + d < WORLD_MAX_Y && data[i] == B.WATER && floor + 2 + d < SEA; d++, i += stride)
                        data[i] = (byte)CORAL_BLOCKS[(int)Math.Floor(hash2Main(x * 3 + d * 7, z * 3 - d * 5) * CORAL_BLOCKS.Length) % CORAL_BLOCKS.Length];
                    if (first + d < WORLD_MAX_Y && data[i] == B.WATER)
                    {
                        if (w < 0.4) data[i] = (byte)CORAL_FANS[(int)Math.Floor(w * 12) % CORAL_FANS.Length];
                        else if (w < 0.55)
                        {
                            data[i] = (byte)B.SEA_PICKLE;
                            if (seaMeta != null) { int iy = first + d - WORLD_MIN_Y, cnt = 1 + ((int)Math.Floor(f * 4) & 3); seaMeta.Add((iy << 10) | (lz << 6) | (lx << 2) | (cnt - 1)); }
                        }
                    }
                    return;
                }
                if (f < 0.06)
                {
                    data[firstI] = (byte)B.SEA_PICKLE;
                    if (seaMeta != null) { int iy = first - WORLD_MIN_Y, cnt = 1 + ((int)Math.Floor(w * 4) & 3); seaMeta.Add((iy << 10) | (lz << 6) | (lx << 2) | (cnt - 1)); }
                }
                else if (f < 0.17) data[firstI] = (byte)CORAL_FANS[(int)Math.Floor(w * CORAL_FANS.Length) % CORAL_FANS.Length];
                else if (f < 0.34) data[firstI] = (byte)B.SEAGRASS;
                return;
            }
            if (bi == "COLD_OCEAN" || bi == "OCEAN" || bi == "LUKEWARM_OCEAN")
            {
                if (depth >= 3)
                {
                    int top = kelpTopAt(x, z, floor);
                    if (top > floor) { int i = firstI; for (int y = first; y <= top && data[i] == B.WATER; y++, i += stride) data[i] = (byte)B.KELP; return; }
                }
                double chance = bi == "LUKEWARM_OCEAN" ? 0.3 : bi == "OCEAN" ? 0.24 : 0.14;
                if (f < chance) data[firstI] = (byte)B.SEAGRASS;
                return;
            }
            if (bi == "RIVER") { if (f < 0.12) data[firstI] = (byte)B.SEAGRASS; return; }
            if (bi == "SWAMP" || bi == "MANGROVE_SWAMP")
            {
                if (f < 0.1) data[firstI] = (byte)B.SEAGRASS;
                int aboveSeaI = seaI + stride;
                if (SEA + 1 < WORLD_MAX_Y && data[seaI] == B.WATER && data[aboveSeaI] == B.AIR && hash2Main(x * 7 - 9, z * 7 + 19) < 0.05) data[aboveSeaI] = (byte)B.LILY_PAD;
            }
        }

        // ---------------- dense chunk generation ----------------
        int gx0, gz0; byte[] gdata;
        void forcePut(int x, int y, int z, int id)
        {
            if (y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return;
            int lx = x - gx0, lz = z - gz0;
            if (lx < 0 || lx >= CHUNK || lz < 0 || lz >= CHUNK) return;
            gdata[idx(lx, y, lz)] = (byte)id;
        }
        void leafPut(int x, int y, int z, int id)
        {
            if (y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return;
            int lx = x - gx0, lz = z - gz0;
            if (lx < 0 || lx >= CHUNK || lz < 0 || lz >= CHUNK) return;
            int ii = idx(lx, y, lz), old = gdata[ii];
            if (old == B.AIR || LEAF_REPLACEABLE[old]) gdata[ii] = (byte)id;
        }
        void leafDisc(int x, int y, int z, int r, int id, bool ragged)
        {
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int ax = Math.Abs(dx), az = Math.Abs(dz);
                    if (r >= 2 && ax == r && az == r && (!ragged || hash3Main(x + dx, y, z + dz) < 0.6)) continue;
                    if (r >= 3 && ax + az > r + 1) continue;
                    leafPut(x + dx, y, z + dz, id);
                }
        }
        void placeFeature(int x, int z, int h, FeatureInfo f)
        {
            var k = f.kind;
            if (k == "cactus") { for (int y = h + 1; y <= h + f.height; y++) forcePut(x, y, z, B.CACTUS); return; }
            if (k == "ice_spike")
            {
                for (int y = h + 1; y <= h + f.height; y++) forcePut(x, y, z, B.PACKED_ICE);
                if (f.height > 5) { forcePut(x + 1, h + 1, z, B.PACKED_ICE); forcePut(x, h + 1, z + 1, B.PACKED_ICE); }
                return;
            }
            if (k == "giant_red_mushroom")
            {
                for (int y = h + 1; y <= h + f.height; y++) forcePut(x, y, z, B.MUSHROOM_STEM);
                leafDisc(x, h + f.height + 1, z, 1, B.RED_MUSHROOM_BLOCK, false);
                leafDisc(x, h + f.height, z, 2, B.RED_MUSHROOM_BLOCK, true);
                return;
            }
            if (k == "giant_brown_mushroom")
            {
                for (int y = h + 1; y <= h + f.height; y++) forcePut(x, y, z, B.MUSHROOM_STEM);
                leafDisc(x, h + f.height + 1, z, 3, B.BROWN_MUSHROOM_BLOCK, false);
                return;
            }
            if (k == "spruce" || k == "tall_spruce")
            {
                int top = h + f.height;
                leafPut(x, top + 1, z, B.SPRUCE_LEAVES);
                int n = k == "tall_spruce" ? 8 : 5;
                for (int c = 0; c < n; c++) { int y = top - c; if (y <= h + 2) break; leafDisc(x, y, z, c == 0 || c % 2 == 1 ? 1 : 2, B.SPRUCE_LEAVES, false); }
                for (int y = h + 1; y <= top; y++) forcePut(x, y, z, B.SPRUCE_LOG);
                return;
            }
            if (k == "dark_oak")
            {
                int top = h + f.height;
                for (int c = -2; c <= 1; c++) { int y = top + c; if (y > h) leafDisc(x, y, z, c <= 0 ? 3 : 2, B.DARK_LEAVES, true); }
                for (int y = h + 1; y <= top; y++) { forcePut(x, y, z, B.DARK_LOG); forcePut(x + 1, y, z, B.DARK_LOG); forcePut(x, y, z + 1, B.DARK_LOG); forcePut(x + 1, y, z + 1, B.DARK_LOG); }
                return;
            }
            if (k == "jungle")
            {
                int top = h + f.height;
                leafDisc(x, top + 1, z, 1, B.JUNGLE_LEAVES, false); leafDisc(x, top, z, 2, B.JUNGLE_LEAVES, true); leafDisc(x, top - 1, z, 2, B.JUNGLE_LEAVES, true);
                for (int y = h + 1; y <= top; y++) forcePut(x, y, z, B.JUNGLE_LOG);
                return;
            }
            if (k == "acacia")
            {
                int d = (int)Math.Floor(hash2Main(x - 5, z + 5) * 4), sx = DIR4X[d], sz = DIR4Z[d], tx = x, tz = z;
                for (int y = h + 1; y <= h + f.height; y++) { if (y > h + 2 && (y - h) % 2 == 1) { tx += sx; tz += sz; } forcePut(tx, y, tz, B.ACACIA_LOG); }
                leafDisc(tx, h + f.height, tz, 3, B.ACACIA_LEAVES, false);
                leafDisc(tx, h + f.height + 1, tz, 2, B.ACACIA_LEAVES, true);
                return;
            }
            if (k == "fancy_oak")
            {
                int top = h + f.height; int[] R = { 2, 3, 3, 2, 1 };
                for (int c = 0; c < R.Length; c++) { int y = top - 3 + c; if (y > h) leafDisc(x, y, z, R[c], B.LEAVES, true); }
                int d = (int)Math.Floor(hash2Main(x, z + 77) * 4), bx = x + FANCY_BX[d], bz = z + FANCY_BZ[d];
                leafDisc(bx, top - 3, bz, 1, B.LEAVES, false);
                for (int y = h + 1; y <= top; y++) forcePut(x, y, z, B.LOG);
                return;
            }
            if (k == "cherry")
            {
                int top = h + f.height;
                leafDisc(x, top + 1, z, 1, B.FLOWERING_AZALEA_LEAVES, false);
                int[] R = { 2, 3, 2 };
                for (int c = 0; c < R.Length; c++) { int y = top - c; if (y <= h + 1) break; leafDisc(x, y, z, R[c], hash3Main(x, y, z) < 0.25 ? B.AZALEA_LEAVES : B.FLOWERING_AZALEA_LEAVES, true); }
                for (int y = h + 1; y <= top; y++) forcePut(x, y, z, B.LOG);
                return;
            }
            {
                int log = k == "birch" ? B.BIRCH_LOG : B.LOG, leaves = k == "birch" ? B.BIRCH_LEAVES : B.LEAVES, top = h + f.height;
                for (int d = -2; d <= 1; d++) { int y = top + d; if (y > h && y < WORLD_MAX_Y) leafDisc(x, y, z, d <= -1 ? 2 : 1, leaves, true); }
                for (int y = h + 1; y <= top; y++) forcePut(x, y, z, log);
            }
        }

        public DenseChunk generateDense(int cx, int cz)
        {
            var data = new byte[CHUNK * WORLD_H * CHUNK];
            var columnCols = new ColumnInfo[CHUNK * CHUNK];
            var seaMeta = new List<int>(); var caveMeta = new List<int>();
            int x0 = cx * CHUNK, z0 = cz * CHUNK;
            const int SXZ = 4, SY = 8, GX = 5, GY = WORLD_H / SY + 1;
            var grid = new float[GX * GX * GY];
            for (int lz = 0; lz < CHUNK; lz++) for (int lx = 0; lx < CHUNK; lx++) columnCols[lz * CHUNK + lx] = columnInfo(x0 + lx, z0 + lz);
            // R5 density lattice: 4x4 horizontal and 8 vertical, trilinearly interpolated (float32 storage like the reference).
            for (int gx = 0; gx < GX; gx++)
                for (int gz = 0; gz < GX; gz++)
                {
                    int off = (gx * GX + gz) * GY, x = x0 + gx * SXZ, z = z0 + gz * SXZ;
                    for (int gy = 0; gy < GY; gy++) grid[off + gy] = (float)terrainDensity(x, WORLD_MIN_Y + gy * SY, z);
                }
            for (int gx = 0; gx < 4; gx++)
                for (int gz = 0; gz < 4; gz++)
                    for (int gy = 0; gy < GY - 1; gy++)
                    {
                        int o = (gx * GX + gz) * GY + gy, ox = ((gx + 1) * GX + gz) * GY + gy, oz = (gx * GX + gz + 1) * GY + gy, oxz = ((gx + 1) * GX + gz + 1) * GY + gy;
                        for (int dx = 0; dx < SXZ; dx++)
                        {
                            double fx = dx / (double)SXZ;
                            for (int dz = 0; dz < SXZ; dz++)
                            {
                                double fz = dz / (double)SXZ; int lx = gx * SXZ + dx, lz = gz * SXZ + dz;
                                double a = lerp(grid[o], grid[ox], fx), b = lerp(grid[oz], grid[oxz], fx), a1 = lerp(grid[o + 1], grid[ox + 1], fx), b1 = lerp(grid[oz + 1], grid[oxz + 1], fx),
                                    lo = lerp(a, b, fz), hi = lerp(a1, b1, fz);
                                int y0 = WORLD_MIN_Y + gy * SY;
                                for (int dy = 0; dy < SY; dy++)
                                {
                                    int y = y0 + dy; double dens = lerp(lo, hi, dy / (double)SY);
                                    data[idx(lx, y, lz)] = (byte)(dens > 0 ? B.STONE : y <= SEA ? B.WATER : B.AIR);
                                }
                            }
                        }
                    }
            // main always establishes the absolute bottom bedrock before surface work.
            for (int lx = 0; lx < CHUNK; lx++) for (int lz = 0; lz < CHUNK; lz++) data[idx(lx, WORLD_MIN_Y, lz)] = (byte)B.BEDROCK;

            // SURFACE stage: material/topsoil before CARVERS; vegetation itself is deferred to FEATURES.
            for (int lz = 0; lz < CHUNK; lz++)
                for (int lx = 0; lx < CHUNK; lx++)
                {
                    int x = x0 + lx, z = z0 + lz, col = lz * CHUNK + lx; var ci = columnCols[col]; var bi = ci.biome;
                    int top = WORLD_MIN_Y;
                    for (int y = WORLD_MAX_Y - 1; y > WORLD_MIN_Y; y--) { int id = data[idx(lx, y, lz)]; if (id != B.AIR && id != B.WATER) { top = y; break; } }
                    if (top > WORLD_MIN_Y && data[idx(lx, top, lz)] == B.STONE)
                    {
                        int topId = surfaceBlockForBiome(bi, x, z, top, ci.origH);
                        data[idx(lx, top, lz)] = (byte)topId;
                        for (int d = 1; d <= 3; d++)
                        {
                            int y = top - d;
                            if (y <= WORLD_MIN_Y || data[idx(lx, y, lz)] != B.STONE) break;
                            int q = subsurfaceBlockForBiome(bi, topId, d, y);
                            if (q >= 0) data[idx(lx, y, lz)] = (byte)q;
                        }
                    }
                    // main R5 ordering: ocean decoration and surface vegetation are emitted before CARVERS.
                    if (top > WORLD_MIN_Y && top + 1 < WORLD_MAX_Y && top < SEA) decorateAquaticColumn(data, lx, lz, x, z, top, bi, seaMeta);
                    else if (top >= SEA && top + 1 < WORLD_MAX_Y)
                    {
                        int gi = idx(lx, top, lz), ai = idx(lx, top + 1, lz), ground = data[gi];
                        if (data[ai] == B.AIR)
                        {
                            int crop = rareCropAt(x, z, top, ground);
                            var p = crop != 0 ? null : landPlantInfoAt(x, z, top, ground);
                            if (crop != 0) data[ai] = (byte)crop;
                            else if (p != null)
                            {
                                data[ai] = (byte)p.id;
                                if (p.top != 0 && top + 2 < WORLD_MAX_Y && data[idx(lx, top + 2, lz)] == B.AIR) data[idx(lx, top + 2, lz)] = (byte)p.top;
                                else if (p.height > 1)
                                    for (int d = 2; d <= p.height; d++) { int yy = top + d; if (yy >= WORLD_MAX_Y || data[idx(lx, yy, lz)] != B.AIR) break; data[idx(lx, yy, lz)] = (byte)p.id; }
                            }
                        }
                    }
                }

            // main places cell-based trees/cacti/mushrooms/ice-spikes before CARVERS too.
            gx0 = x0; gz0 = z0; gdata = data;
            for (int x = x0 - 5; x < x0 + CHUNK + 5; x++)
                for (int z = z0 - 5; z < z0 + CHUNK + 5; z++)
                {
                    var kind = featureKindAt(x, z);
                    if (kind == null || surfaceStructureReservedAt(x, z)) continue;
                    int h = generatedSurfaceAt(x, z);
                    placeFeature(x, z, h, featureInfoFromKind(x, z, kind));
                }
            gdata = null;

            // HQ/zQ/bQ pass: only main's x0 carvable family participates.
            for (int lz = 0; lz < CHUNK; lz++)
                for (int lx = 0; lx < CHUNK; lx++)
                {
                    int x = x0 + lx, z = z0 + lz, col = lz * CHUNK + lx; var ci = columnCols[col];
                    int top = WORLD_MIN_Y;
                    for (int y = WORLD_MAX_Y - 1; y > WORLD_MIN_Y; y--) if (MAIN_CARVABLE[data[idx(lx, y, lz)]]) { top = y; break; }
                    for (int y = WORLD_MIN_Y; y <= top; y++)
                    {
                        int ii = idx(lx, y, lz);
                        if (!MAIN_CARVABLE[data[ii]]) continue;
                        if (bedrockAt(x, y, z)) { data[ii] = (byte)B.BEDROCK; continue; }
                        int ck = caveKind(x, y, z, top, ci);
                        if (ck != 0)
                        {
                            if (ck == 1) data[ii] = (byte)B.AIR;
                            else
                            {
                                int fluid = ck == 2 ? B.WATER : B.LAVA;
                                data[ii] = (byte)fluid;
                                for (int yy = y - 1; yy > WORLD_MIN_Y; yy--) { int jj = idx(lx, yy, lz); if (data[jj] != B.AIR) break; data[jj] = (byte)fluid; }
                            }
                            continue;
                        }
                        if (data[ii] == B.STONE) data[ii] = (byte)baseStoneAt(x, y, z);
                    }
                }

            carveRavines(data, cx, cz);

            // Main-style connected sub-sea cavity flood (6-neighbour, bounded to [WORLD_MIN_Y+1, SEA-1] and this chunk).
            {
                int seaTop = OCEAN_FLOOD_TOP, minY = OCEAN_FLOOD_MIN_Y, plane = CHUNK * CHUNK, sp = 0; var stack = oceanFloodStack;
                for (int lz = 0; lz < CHUNK; lz++)
                    for (int lx = 0; lx < CHUNK; lx++)
                    {
                        bool oceanColumn = columnCols[lz * CHUNK + lx].origH <= SEA;
                        for (int y = minY; y <= seaTop; y++)
                        {
                            int ii = idx(lx, y, lz), id = data[ii];
                            if (id == B.WATER) { stack[sp++] = ii; continue; }
                            if (oceanColumn && id == B.AIR) { data[ii] = (byte)B.WATER; stack[sp++] = ii; }
                        }
                    }
                while (sp > 0)
                {
                    int ii = stack[--sp], lx = ii & 15, lz = (ii >> 4) & 15, iy = ii >> 8, y = WORLD_MIN_Y + iy;
                    if (y < seaTop) { int q = ii + plane; if (data[q] == B.AIR) { data[q] = (byte)B.WATER; stack[sp++] = q; } }
                    if (y > minY) { int q = ii - plane; if (data[q] == B.AIR) { data[q] = (byte)B.WATER; stack[sp++] = q; } }
                    if (lx < 15) { int q = ii + 1; if (data[q] == B.AIR) { data[q] = (byte)B.WATER; stack[sp++] = q; } }
                    if (lx > 0) { int q = ii - 1; if (data[q] == B.AIR) { data[q] = (byte)B.WATER; stack[sp++] = q; } }
                    if (lz < 15) { int q = ii + CHUNK; if (data[q] == B.AIR) { data[q] = (byte)B.WATER; stack[sp++] = q; } }
                    if (lz > 0) { int q = ii - CHUNK; if (data[q] == B.AIR) { data[q] = (byte)B.WATER; stack[sp++] = q; } }
                }
            }
            // main freezes exposed surface water by the climate temperature band after carvers.
            {
                int stride = CHUNK * CHUNK;
                for (int lz = 0; lz < CHUNK; lz++)
                    for (int lx = 0; lx < CHUNK; lx++)
                    {
                        var ci = columnCols[lz * CHUNK + lx];
                        if (tempBand(ci.temp) != 0) continue;
                        for (int y = SEA; y >= SEA - 2; y--) { int ii = idx(lx, y, lz), up = ii + stride; if (data[ii] == B.WATER && data[up] == B.AIR) { data[ii] = (byte)B.ICE; break; } }
                    }
            }

            placeMainOres(data, cx, cz);

            // YQ/B4 cave-biome decorator after ores.
            for (int lz = 0; lz < CHUNK; lz++)
                for (int lx = 0; lx < CHUNK; lx++)
                {
                    int x = x0 + lx, z = z0 + lz, top = WORLD_MIN_Y;
                    for (int y = WORLD_MAX_Y - 1; y > WORLD_MIN_Y; y--) if (MAIN_CARVABLE[data[idx(lx, y, lz)]]) { top = y; break; }
                    if (top - WORLD_MIN_Y < 33) continue;
                    for (int y = top - 2; y > WORLD_MIN_Y + 6; y--)
                    {
                        int ii = idx(lx, y, lz);
                        if (data[ii] != B.AIR) continue;
                        int below = idx(lx, y - 1, lz), above = idx(lx, y + 1, lz);
                        bool floorSolid = caveDecorSolid(data[below]), ceilSolid = caveDecorSolid(data[above]);
                        if (!floorSolid && !ceilSolid) continue;
                        int cb = caveBiomeAt(x, y, z, top);
                        if (cb == CAVE_NONE) continue;
                        double r = hash3Main(x, y, z);
                        if (cb == CAVE_LUSH)
                        {
                            if (floorSolid)
                            {
                                if (r < 0.72) data[below] = (byte)(r < 0.04 ? B.CLAY : B.MOSS_BLOCK);
                                if (r < 0.06) data[ii] = (byte)B.FLOWERING_AZALEA_LEAVES;
                                else if (r < 0.16) data[ii] = (byte)B.MOSS_CARPET;
                                else if (r < 0.3) data[ii] = (byte)(r < 0.23 ? B.FERN : B.SHORT_GRASS);
                            }
                            if (ceilSolid)
                            {
                                if (r < 0.6) data[above] = (byte)B.MOSS_BLOCK;
                                if (r < 0.22 && data[ii] == B.AIR)
                                {
                                    int len = 2 + (int)Math.Floor(hash2Main(x * 3 + 1, z * 3 - 2) * 4);
                                    for (int k = 0; k < len; k++)
                                    {
                                        int yy = y - k;
                                        if (yy <= WORLD_MIN_Y + 6) break;
                                        int vi = idx(lx, yy, lz);
                                        if (data[vi] != B.AIR) break;
                                        data[vi] = (byte)B.CAVE_VINES;
                                        if (hash3Main(x, yy, z + 7) < 0.13) caveMeta.Add(((yy - WORLD_MIN_Y) << 8) | (lz << 4) | lx);
                                    }
                                }
                            }
                        }
                        else if (cb == CAVE_DRIPSTONE)
                        {
                            if (floorSolid)
                            {
                                data[below] = (byte)(r < 0.05 ? B.CALCITE : r < 0.4 ? B.DRIPSTONE_BLOCK : data[below]);
                                if (r < 0.06)
                                {
                                    int len = 1 + (int)Math.Floor(hash2Main(x + 4, z - 9) * 3);
                                    for (int k = 0; k < len; k++) { int yy = y + k; if (yy >= top) break; int vi = idx(lx, yy, lz); if (data[vi] != B.AIR) break; data[vi] = (byte)B.DRIPSTONE_BLOCK; }
                                }
                            }
                            if (ceilSolid)
                            {
                                if (r < 0.4) data[above] = (byte)B.DRIPSTONE_BLOCK;
                                if (r < 0.06)
                                {
                                    int len = 1 + (int)Math.Floor(hash2Main(x - 6, z + 3) * 3);
                                    for (int k = 0; k < len; k++) { int yy = y - k; if (yy <= WORLD_MIN_Y + 6) break; int vi = idx(lx, yy, lz); if (data[vi] != B.AIR) break; data[vi] = (byte)B.DRIPSTONE_BLOCK; }
                                }
                            }
                        }
                        else if (cb == CAVE_DEEP_DARK)
                        {
                            if (floorSolid) { if (r < 0.02) data[below] = (byte)B.BUDDING_AMETHYST; else if (r < 0.05) data[below] = (byte)B.AMETHYST_BLOCK; }
                        }
                    }
                }

            applyStructures(data, cx, cz);
            applyMineshafts(data, cx, cz);
            return new DenseChunk { data = data, meta = new WgMeta { cx = cx, cz = cz, sea = seaMeta, cave = caveMeta, dirty = false } };
        }
    }
}
