// Voxel Forge — Unity port. World storage: edits/meta layers, chunk lookup, sections, block get/set, fluid rules, sim queues.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VoxelForge
{
    public sealed partial class Chunk
    {
        public int cx, cz;
        public string key;
        public Section[] sections = new Section[VF.SECTION_COUNT];
        public List<Mob> mobs = new List<Mob>();
        public int mapRev, boundaryRev, meshRev;
        public bool dirty, fullMeshDirty;
        public HashSet<int> dirtySections;
        public uint[] fluidBoundary = new uint[0];
    }

    public static partial class VF
    {
        // ---------------- hashing / value noise ----------------
        public static double hash2(int x, int z, int s = 1337)
        {
            int ws = WORLD_SEED ^ JS.imul(s, 0x45d9f3b);
            int n = unchecked(JS.imul(x, 374761393) + JS.imul(z, 668265263) + JS.imul(ws, 1442695041));
            n = n ^ JS.ushr(n, 13);
            n = JS.imul(n, 1274126177);
            n ^= JS.ushr(n, 16);
            return (uint)n / 4294967295.0;
        }
        public static double smooth(double t) { return t * t * (3 - 2 * t); }
        public static double noise2(double x, double z, double scale, int seed = 0)
        {
            x /= scale; z /= scale;
            int ix = JS.floor(x), iz = JS.floor(z);
            double fx = smooth(x - ix), fz = smooth(z - iz);
            double a = hash2(ix, iz, seed), b = hash2(ix + 1, iz, seed), c = hash2(ix, iz + 1, seed), d = hash2(ix + 1, iz + 1, seed);
            double ab = a + (b - a) * fx, cd = c + (d - c) * fx;
            return ab + (cd - ab) * fz;
        }

        // ---------------- fluids ----------------
        public static bool isWater(int id)
        {
            return id == B.WATER || id == B.WATER_FALLING || id == B.FLOW7 || id == B.FLOW6 || id == B.FLOW5 || id == B.FLOW4 || id == B.FLOW3 || id == B.FLOW2 || id == B.FLOW1
                || (id >= 0 && id < 256 && blocks[id] != null && blocks[id].waterPlant);
        }
        public static bool isFluidWater(int id)
        {
            return id == B.WATER || id == B.WATER_FALLING || id == B.FLOW7 || id == B.FLOW6 || id == B.FLOW5 || id == B.FLOW4 || id == B.FLOW3 || id == B.FLOW2 || id == B.FLOW1;
        }
        public static bool hasWaterVolume(int id) { return isWater(id); }
        public static int runtimeWaterVisualLevel(int id)
        {
            if (id == B.WATER || id == B.WATER_FALLING) return 8;
            if (id == B.FLOW7) return 7; if (id == B.FLOW6) return 6; if (id == B.FLOW5) return 5; if (id == B.FLOW4) return 4;
            if (id == B.FLOW3) return 3; if (id == B.FLOW2) return 2; if (id == B.FLOW1) return 1;
            var b = bdef(id); return b != null && b.waterPlant ? 8 : 0;
        }
        public static double runtimeWaterHeightAt(int x, int y, int z)
        {
            int id = getBlock(x, y, z), lev = runtimeWaterVisualLevel(id);
            if (lev == 0) return -1;
            if (id == B.WATER_FALLING || isWater(getBlock(x, y + 1, z))) return 1;
            return lev / 9.0;
        }
        public static double runtimeWaterCornerAverage(double cur, double a, double b, int dx, int y, int dz)
        {
            if (cur >= 0.999 || a >= 0.999 || b >= 0.999) return 1;
            double sum = 0, w = 0;
            if (a > 0 || b > 0)
            {
                double d = runtimeWaterHeightAt(dx, y, dz);
                if (d >= 0.999) return 1;
                if (d >= 0) { double q = d >= 0.8 ? 10 : 1; sum += d * q; w += q; }
            }
            if (cur >= 0) { double q = cur >= 0.8 ? 10 : 1; sum += cur * q; w += q; }
            if (a >= 0) { double q = a >= 0.8 ? 10 : 1; sum += a * q; w += q; }
            if (b >= 0) { double q = b >= 0.8 ? 10 : 1; sum += b * q; w += q; }
            return w != 0 ? sum / w : 0;
        }
        public static bool pointInWater(double x, double y, double z)
        {
            int bx = JS.floor(x), by = JS.floor(y), bz = JS.floor(z), id = getBlock(bx, by, bz);
            if (!isWater(id)) return false;
            if (isWater(getBlock(bx, by + 1, bz))) return true;
            double hc = runtimeWaterHeightAt(bx, by, bz), hn = runtimeWaterHeightAt(bx, by, bz - 1), hs = runtimeWaterHeightAt(bx, by, bz + 1),
                hw = runtimeWaterHeightAt(bx - 1, by, bz), he = runtimeWaterHeightAt(bx + 1, by, bz),
                h00 = runtimeWaterCornerAverage(hc, hn, hw, bx - 1, by, bz - 1), h10 = runtimeWaterCornerAverage(hc, hn, he, bx + 1, by, bz - 1),
                h01 = runtimeWaterCornerAverage(hc, hs, hw, bx - 1, by, bz + 1), h11 = runtimeWaterCornerAverage(hc, hs, he, bx + 1, by, bz + 1),
                fx = x - bx, fz = z - bz, h0 = h00 + (h10 - h00) * fx, h1 = h01 + (h11 - h01) * fx, h = h0 + (h1 - h0) * fz;
            return y < by + h + 0.001;
        }
        public static bool isLava(int id) { return id == B.LAVA || id == B.LAVA_FLOW2 || id == B.LAVA_FLOW1; }
        public static int randInt(int a, int b) { return a + (int)Math.Floor(JS.random() * (b - a + 1)); }

        // ---------------- items ----------------
        public static readonly Dictionary<string, ItemDef> ITEM_DEFS = new Dictionary<string, ItemDef>();
        static void IDEF(string k, string name, int stack = 64, double fuel = 0, double food = 0, string tool = null, int tier = 0, double speed = 0, double damage = double.NaN, int maxDur = 0, string spawnEgg = null)
        {
            ITEM_DEFS[k] = new ItemDef { name = name, stack = stack, fuel = fuel, food = food, tool = tool, tier = tier, speed = speed, damage = double.IsNaN(damage) ? 0 : damage, hasDamage = !double.IsNaN(damage), maxDur = maxDur, spawnEgg = spawnEgg };
        }
        static void InitItemDefs()
        {
            ITEM_DEFS.Clear();
            IDEF("stick", "Палка", fuel: 5); IDEF("coal", "Уголь", fuel: 80); IDEF("raw_iron", "Сырое железо"); IDEF("raw_gold", "Сырое золото");
            IDEF("iron_ingot", "Железный слиток"); IDEF("gold_ingot", "Золотой слиток"); IDEF("diamond", "Алмаз"); IDEF("clay_ball", "Комок глины");
            IDEF("flint", "Кремень"); IDEF("book", "Книга"); IDEF("melon_slice", "Ломтик арбуза", food: 2); IDEF("beef", "Сырая говядина", food: 3);
            IDEF("cooked_beef", "Стейк", food: 8); IDEF("leather", "Кожа"); IDEF("pork", "Сырая свинина", food: 3); IDEF("cooked_pork", "Жареная свинина", food: 8);
            IDEF("wool", "Шерсть"); IDEF("chicken", "Сырая курица", food: 2); IDEF("cooked_chicken", "Жареная курица", food: 6); IDEF("feather", "Перо");
            IDEF("salmon", "Сырой лосось", food: 2); IDEF("cooked_salmon", "Жареный лосось", food: 6); IDEF("fish", "Сырая рыба", food: 2);
            IDEF("cooked_fish", "Жареная рыба", food: 5); IDEF("bone", "Кость"); IDEF("gunpowder", "Порох"); IDEF("rotten_flesh", "Гнилая плоть", food: 2);
            IDEF("arrow", "Стрела"); IDEF("wheat", "Пшеница"); IDEF("wheat_seeds", "Семена пшеницы"); IDEF("bread", "Хлеб", food: 5);
            IDEF("mutton", "Сырая баранина", food: 2); IDEF("cooked_mutton", "Жареная баранина", food: 6); IDEF("carrot", "Морковь", food: 3);
            IDEF("potato", "Картофель", food: 1); IDEF("baked_potato", "Печёный картофель", food: 5);
            IDEF("shears", "Ножницы", 1, tool: "shears", tier: 1, speed: 1, maxDur: 238);
            IDEF("wooden_pickaxe", "Деревянная кирка", 1, tool: "pickaxe", tier: 1, speed: 2, damage: 2, fuel: 10, maxDur: 59);
            IDEF("stone_pickaxe", "Каменная кирка", 1, tool: "pickaxe", tier: 2, speed: 4, damage: 3, maxDur: 131);
            IDEF("iron_pickaxe", "Железная кирка", 1, tool: "pickaxe", tier: 3, speed: 6, damage: 4, maxDur: 250);
            IDEF("diamond_pickaxe", "Алмазная кирка", 1, tool: "pickaxe", tier: 4, speed: 8, damage: 5, maxDur: 1561);
            IDEF("wooden_axe", "Деревянный топор", 1, tool: "axe", tier: 1, speed: 2.5, damage: 4, fuel: 10, maxDur: 59);
            IDEF("stone_axe", "Каменный топор", 1, tool: "axe", tier: 2, speed: 5, damage: 5, maxDur: 131);
            IDEF("iron_axe", "Железный топор", 1, tool: "axe", tier: 3, speed: 7, damage: 6, maxDur: 250);
            IDEF("diamond_axe", "Алмазный топор", 1, tool: "axe", tier: 4, speed: 9, damage: 7, maxDur: 1561);
            IDEF("wooden_shovel", "Деревянная лопата", 1, tool: "shovel", tier: 1, speed: 2.5, damage: 2, fuel: 10, maxDur: 59);
            IDEF("stone_shovel", "Каменная лопата", 1, tool: "shovel", tier: 2, speed: 5, damage: 3, maxDur: 131);
            IDEF("iron_shovel", "Железная лопата", 1, tool: "shovel", tier: 3, speed: 7, damage: 4, maxDur: 250);
            IDEF("diamond_shovel", "Алмазная лопата", 1, tool: "shovel", tier: 4, speed: 9, damage: 5, maxDur: 1561);
            IDEF("wooden_sword", "Деревянный меч", 1, tool: "sword", tier: 1, speed: 1, damage: 4, fuel: 10, maxDur: 59);
            IDEF("stone_sword", "Каменный меч", 1, tool: "sword", tier: 2, speed: 1, damage: 5, maxDur: 131);
            IDEF("iron_sword", "Железный меч", 1, tool: "sword", tier: 3, speed: 1, damage: 6, maxDur: 250);
            IDEF("diamond_sword", "Алмазный меч", 1, tool: "sword", tier: 4, speed: 1, damage: 7, maxDur: 1561);
            IDEF("wooden_hoe", "Деревянная мотыга", 1, tool: "hoe", tier: 1, speed: 1, fuel: 10, maxDur: 59);
            IDEF("stone_hoe", "Каменная мотыга", 1, tool: "hoe", tier: 2, speed: 1, maxDur: 131);
            IDEF("iron_hoe", "Железная мотыга", 1, tool: "hoe", tier: 3, speed: 1, maxDur: 250);
            IDEF("diamond_hoe", "Алмазная мотыга", 1, tool: "hoe", tier: 4, speed: 1, maxDur: 1561);
            IDEF("string", "Нить"); IDEF("spider_eye", "Паучий глаз", food: 2); IDEF("egg", "Яйцо", 16);
            IDEF("bow", "Лук", 1, tool: "bow", tier: 1, speed: 1, maxDur: 384); IDEF("ender_pearl", "Жемчуг Эндера", 16); IDEF("slime_ball", "Сгусток слизи");
            IDEF("minecart", "Вагонетка", 1); IDEF("boat", "Лодка", 1); IDEF("fishing_rod", "Удочка", 1, tool: "rod", tier: 1, speed: 1, maxDur: 64);
            IDEF("bone_meal", "Костная мука"); IDEF("pumpkin_seeds", "Семена тыквы"); IDEF("melon_seeds", "Семена арбуза");
            IDEF("sweet_berries", "Сладкие ягоды", food: 2); IDEF("emerald", "Изумруд"); IDEF("golden_apple", "Золотое яблоко", food: 9); IDEF("golden_carrot", "Золотая морковь", food: 6);
            IDEF("sugar", "Сахар");
            IDEF("pig_spawn_egg", "Яйцо призыва: свинья", spawnEgg: "pig"); IDEF("cow_spawn_egg", "Яйцо призыва: корова", spawnEgg: "cow");
            IDEF("sheep_spawn_egg", "Яйцо призыва: овца", spawnEgg: "sheep"); IDEF("chicken_spawn_egg", "Яйцо призыва: курица", spawnEgg: "chicken");
            IDEF("zombie_spawn_egg", "Яйцо призыва: зомби", spawnEgg: "zombie"); IDEF("skeleton_spawn_egg", "Яйцо призыва: скелет", spawnEgg: "skeleton");
            IDEF("creeper_spawn_egg", "Яйцо призыва: крипер", spawnEgg: "creeper");
            IDEF("golden_pickaxe", "Золотая кирка", 1, tool: "pickaxe", tier: 1, speed: 12.0, maxDur: 32, damage: 2.0);
            IDEF("golden_axe", "Золотой топор", 1, tool: "axe", tier: 1, speed: 12.0, maxDur: 32, damage: 7.0);
            IDEF("golden_shovel", "Золотая лопата", 1, tool: "shovel", tier: 1, speed: 12.0, maxDur: 32, damage: 2.5);
            IDEF("golden_sword", "Золотой меч", 1, tool: "sword", tier: 1, speed: 12.0, maxDur: 32, damage: 4.0);
            IDEF("golden_hoe", "Золотая мотыга", 1, tool: "hoe", tier: 1, speed: 12.0, maxDur: 32, damage: 1.0);
            IDEF("bowl", "Миска", fuel: 5); IDEF("mushroom_stew", "Грибной суп", 1, food: 6);
            IDEF("white_dye", "Краситель: белый"); IDEF("orange_dye", "Краситель: оранжевый"); IDEF("magenta_dye", "Краситель: сиреневый");
            IDEF("light_blue_dye", "Краситель: голубой"); IDEF("yellow_dye", "Краситель: жёлтый"); IDEF("lime_dye", "Краситель: лаймовый");
            IDEF("pink_dye", "Краситель: розовый"); IDEF("gray_dye", "Краситель: серый"); IDEF("light_gray_dye", "Краситель: светло-серый");
            IDEF("cyan_dye", "Краситель: бирюзовый"); IDEF("purple_dye", "Краситель: фиолетовый"); IDEF("blue_dye", "Краситель: синий");
            IDEF("brown_dye", "Краситель: коричневый"); IDEF("red_dye", "Краситель: красный"); IDEF("black_dye", "Краситель: чёрный");
            IDEF("spider_spawn_egg", "Яйцо призыва: паук", spawnEgg: "spider"); IDEF("enderman_spawn_egg", "Яйцо призыва: эндермен", spawnEgg: "enderman");
            IDEF("salmon_spawn_egg", "Яйцо призыва: лосось", spawnEgg: "salmon"); IDEF("slime_big_spawn_egg", "Яйцо призыва: слизень", spawnEgg: "slime_big");
            IDEF("shark_spawn_egg", "Яйцо призыва: акула", spawnEgg: "shark"); IDEF("pumpkin_pie", "Тыквенный пирог", food: 8);
            // main.js combat metadata parity. Mining speed/tier stay independent; these values are only melee combat.
            var mats = new[] { "wooden", "stone", "iron", "golden", "diamond" };
            var stats = new Dictionary<string, double[][]> {
                { "sword", new[] { new double[] { 4, 5, 6, 4, 7 }, new[] { 1.6, 1.6, 1.6, 1.6, 1.6 } } },
                { "axe", new[] { new double[] { 7, 9, 9, 7, 9 }, new[] { 0.8, 0.8, 0.9, 1, 1 } } },
                { "pickaxe", new[] { new double[] { 2, 3, 4, 2, 5 }, new[] { 1.2, 1.2, 1.2, 1.2, 1.2 } } },
                { "shovel", new[] { new[] { 2.5, 3.5, 4.5, 2.5, 5.5 }, new double[] { 1, 1, 1, 1, 1 } } },
                { "hoe", new[] { new double[] { 1, 1, 1, 1, 1 }, new double[] { 1, 2, 3, 1, 4 } } } };
            for (int mi = 0; mi < mats.Length; mi++)
                foreach (var kind in new[] { "sword", "axe", "pickaxe", "shovel", "hoe" })
                {
                    ItemDef d; if (!ITEM_DEFS.TryGetValue(mats[mi] + "_" + kind, out d)) continue;
                    d.damage = stats[kind][0][mi]; d.hasDamage = true; d.attackSpeed = stats[kind][1][mi];
                }
            ItemDef bow; if (ITEM_DEFS.TryGetValue("bow", out bow)) { bow.damage = 1; bow.hasDamage = true; bow.attackSpeed = 1; }
            // Object.assign(ITEM_DEFS, {...}) block evaluated later in the reference source.
            IDEF("apple", "Яблоко", food: 4); IDEF("brick", "Кирпич"); IDEF("bucket", "Ведро", 16); IDEF("water_bucket", "Ведро воды", 1);
            IDEF("lava_bucket", "Ведро лавы", 1, fuel: 1000); IDEF("raw_copper", "Необработанная медь"); IDEF("copper_ingot", "Медный слиток");
            IDEF("green_dye", "Зелёный краситель"); IDEF("flint_and_steel", "Огниво", 1, maxDur: 64);
            Action<string, string, int, int, int> AR = (k, n, dur, slot, pts) => { IDEF(k, n, 1, maxDur: dur); ITEM_DEFS[k].armor = new ArmorInfo { slot = slot, pts = pts }; };
            AR("leather_helmet", "Кожаный шлем", 55, 0, 1); AR("leather_chestplate", "Кожаная куртка", 80, 1, 3); AR("leather_leggings", "Кожаные штаны", 75, 2, 2); AR("leather_boots", "Кожаные ботинки", 65, 3, 1);
            AR("iron_helmet", "Железный шлем", 165, 0, 2); AR("iron_chestplate", "Железный нагрудник", 240, 1, 6); AR("iron_leggings", "Железные поножи", 225, 2, 5); AR("iron_boots", "Железные ботинки", 195, 3, 2);
            AR("golden_helmet", "Золотой шлем", 77, 0, 2); AR("golden_chestplate", "Золотой нагрудник", 112, 1, 5); AR("golden_leggings", "Золотые поножи", 105, 2, 3); AR("golden_boots", "Золотые ботинки", 91, 3, 1);
            AR("diamond_helmet", "Алмазный шлем", 363, 0, 3); AR("diamond_chestplate", "Алмазный нагрудник", 528, 1, 8); AR("diamond_leggings", "Алмазные поножи", 495, 2, 6); AR("diamond_boots", "Алмазные ботинки", 429, 3, 3);
        }
        public static ItemDef itemDefOrNull(string k) { ItemDef d; return k != null && ITEM_DEFS.TryGetValue(k, out d) ? d : null; }

        // ---------------- edits / meta layers ----------------
        public static readonly Dictionary<string, int> edits = new Dictionary<string, int>();
        public static readonly OrderedMap<string, OrderedMap<int, int[]>> editsByChunk = new OrderedMap<string, OrderedMap<int, int[]>>();
        public static readonly OrderedMap<string, JObj> blockMeta = new OrderedMap<string, JObj>();
        public static readonly Dictionary<string, Dictionary<int, JObj>> metaByChunk = new Dictionary<string, Dictionary<int, JObj>>();
        public static readonly Dictionary<string, Dictionary<int, JObj>> generatedMetaByChunk = new Dictionary<string, Dictionary<int, JObj>>();
        public static string key3(int x, int y, int z) { return x + "," + y + "," + z; }
        public static string ckey(int cx, int cz) { return cx + "," + cz; }
        public static int metaLocalKey(int x, int y, int z) { return ((y - WORLD_MIN_Y) << 8) | ((z & 15) << 4) | (x & 15); }
        public static void clearRuntimeWorld()
        {
            edits.Clear(); editsByChunk.Clear(); blockMeta.Clear(); metaByChunk.Clear(); generatedMetaByChunk.Clear();
            killedMobs.Clear(); liveMobs.Clear();
            clearVehicles();
            foreach (var c in chunks.Values) if (c.mobs != null) c.mobs.Clear();
            worldSim.waterUrgentQueue.Clear(); worldSim.waterQueue.Clear(); worldSim.waterSeedQueue.Clear(); worldSim.lavaQueue.Clear(); worldSim.lavaSeedQueue.Clear();
            worldSim.farmland.Clear(); worldSim.crops.Clear(); worldSim.saplings.Clear(); worldSim.growColumns.Clear(); worldSim.leafDecay.Clear(); worldSim.fires.Clear();
            dirtyEditChunks.Clear(); dirtyEditSections.Clear();
            pressurePlateActive.Clear(); pressurePlateAccum = 0;
        }
        public static bool transientFluidEditId(int id)
        {
            // FLOW*/WATER_FALLING/LAVA_FLOW* are solver states, not authored world blocks.
            return id == B.WATER_FALLING || id == B.FLOW7 || id == B.FLOW6 || id == B.FLOW5 || id == B.FLOW4 || id == B.FLOW3 || id == B.FLOW2 || id == B.FLOW1 || id == B.LAVA_FLOW2 || id == B.LAVA_FLOW1;
        }
        public static void loadWorldSave()
        {
            clearRuntimeWorld();
            JObj r = null;
            try { r = Json.TryParse(LocalStorage.getItem(SAVE_KEYS.edits) ?? "null") as JObj; } catch { }
            if (!saveHeaderValid(r) || !Json.IsFinite(r.Get("seed")) || JS.toInt32(r.Num("seed")) != WORLD_SEED) return;
            var ed = r.Arr("edits");
            if (ed != null)
                foreach (var eo in ed)
                {
                    var e = eo as List<object>;
                    if (e == null || e.Count < 4) continue;
                    int x = JS.toInt32(Json.ToNum(e[0])), y = JS.toInt32(Json.ToNum(e[1])), z = JS.toInt32(Json.ToNum(e[2])), id = JS.toInt32(Json.ToNum(e[3]));
                    if (transientFluidEditId(id)) continue;
                    addEditIndex(x, y, z, id, false);
                }
            var meta = r.Arr("meta");
            if (meta != null)
                foreach (var qo in meta)
                {
                    var q = qo as List<object>;
                    if (q == null || q.Count != 2) continue;
                    var k = q[0] as string; var v = q[1] as JObj;
                    if (k == null || v == null) continue;
                    blockMeta.Set(k, v);
                    var p = k.Split(',');
                    if (p.Length == 3)
                    {
                        int x = (int)Json.ToNum(p[0]), y = (int)Json.ToNum(p[1]), z = (int)Json.ToNum(p[2]);
                        var mk = ckey(JS.floor(x / (double)CHUNK), JS.floor(z / (double)CHUNK));
                        Dictionary<int, JObj> mm;
                        if (!metaByChunk.TryGetValue(mk, out mm)) metaByChunk[mk] = mm = new Dictionary<int, JObj>();
                        mm[metaLocalKey(x, y, z)] = v;
                    }
                }
        }
        public static void addEditIndex(int x, int y, int z, int id, bool save = true)
        {
            var k = key3(x, y, z);
            edits[k] = id;
            var ck = ckey(JS.floor(x / (double)CHUNK), JS.floor(z / (double)CHUNK));
            var m = editsByChunk.Get(ck);
            if (m == null) editsByChunk.Set(ck, m = new OrderedMap<int, int[]>());
            m.Set(metaLocalKey(x, y, z), new[] { x, y, z, id });
            if (save) scheduleWorldSave();
        }
        public static List<int[]> chunkEditsArray(int cx, int cz)
        {
            var m = editsByChunk.Get(ckey(cx, cz));
            return m != null ? m.Values.ToList() : new List<int[]>();
        }
        public static List<object> serializeWorldEdits()
        {
            var o = new List<object>(edits.Count);
            foreach (var m in editsByChunk.Values) foreach (var e in m.Values) o.Add(new List<object> { (double)e[0], (double)e[1], (double)e[2], (double)e[3] });
            return o;
        }
        static int saveTimer = 0;
        public static void saveWorldNow()
        {
            if (SAVE_KEYS.edits == null) return;
            try
            {
                var o = saveHeader();
                o["seed"] = (double)WORLD_SEED;
                o["edits"] = serializeWorldEdits();
                o["meta"] = blockMeta.Select(kv => (object)new List<object> { kv.Key, kv.Value }).ToList();
                LocalStorage.setItem(SAVE_KEYS.edits, Json.Stringify(o));
                touchWorldRecord();
            }
            catch { }
        }
        public static void scheduleWorldSave() { Timers.clearTimeout(saveTimer); saveTimer = Timers.setTimeout(saveWorldNow, 300); }

        // ---------------- chunk lookup ----------------
        public static readonly OrderedMap<string, Chunk> chunks = new OrderedMap<string, Chunk>();
        static readonly Dictionary<int, Dictionary<int, Chunk>> chunkRows = new Dictionary<int, Dictionary<int, Chunk>>();
        // Tiny direct-mapped front cache for the hottest chunk lookup path.
        const int CHUNK_LOOKUP_CACHE_SIZE = 256, CHUNK_LOOKUP_CACHE_MASK = CHUNK_LOOKUP_CACHE_SIZE - 1;
        static readonly int[] chunkLookupCX = new int[CHUNK_LOOKUP_CACHE_SIZE], chunkLookupCZ = new int[CHUNK_LOOKUP_CACHE_SIZE];
        static readonly bool[] chunkLookupValid = new bool[CHUNK_LOOKUP_CACHE_SIZE];
        static readonly Chunk[] chunkLookupValue = new Chunk[CHUNK_LOOKUP_CACHE_SIZE];
        static int chunkLookupSlot(int cx, int cz) { return (int)((uint)(JS.imul(cx, 73856093) ^ JS.imul(cz, 19349663)) & CHUNK_LOOKUP_CACHE_MASK); }
        public static Chunk chunkFastGet(int cx, int cz)
        {
            int i = chunkLookupSlot(cx, cz);
            if (chunkLookupValid[i] && chunkLookupCX[i] == cx && chunkLookupCZ[i] == cz) return chunkLookupValue[i];
            Dictionary<int, Chunk> row; Chunk v = null;
            if (chunkRows.TryGetValue(cx, out row)) row.TryGetValue(cz, out v);
            chunkLookupValid[i] = true; chunkLookupCX[i] = cx; chunkLookupCZ[i] = cz; chunkLookupValue[i] = v;
            return v;
        }
        public static Chunk chunkFastSet(Chunk c)
        {
            Dictionary<int, Chunk> row;
            if (!chunkRows.TryGetValue(c.cx, out row)) chunkRows[c.cx] = row = new Dictionary<int, Chunk>();
            row[c.cz] = c;
            int i = chunkLookupSlot(c.cx, c.cz);
            chunkLookupValid[i] = true; chunkLookupCX[i] = c.cx; chunkLookupCZ[i] = c.cz; chunkLookupValue[i] = c;
            return c;
        }
        public static void chunkFastDelete(Chunk c)
        {
            Dictionary<int, Chunk> row;
            if (chunkRows.TryGetValue(c.cx, out row)) { row.Remove(c.cz); if (row.Count == 0) chunkRows.Remove(c.cx); }
            int i = chunkLookupSlot(c.cx, c.cz);
            if (chunkLookupValid[i] && chunkLookupCX[i] == c.cx && chunkLookupCZ[i] == c.cz) { chunkLookupValid[i] = false; chunkLookupValue[i] = null; }
        }
        static void clearChunkLookupCache() { for (int i = 0; i < CHUNK_LOOKUP_CACHE_SIZE; i++) { chunkLookupValid[i] = false; chunkLookupValue[i] = null; } }

        public static readonly WorldSim worldSim = new WorldSim();
        public static double waterSimAt = 0, lavaSimAt = 0, farmlandT = 0, saplingT = 0, columnGrowT = 0, leafT = 0;
        public static Dictionary<string, int[]> CROP_TYPES;
        public static readonly CropInfo[] CROP_INFO = new CropInfo[256];
        static void InitCrops()
        {
            CROP_TYPES = new Dictionary<string, int[]> {
                { "wheat", new[] { B.WHEAT0, B.WHEAT1, B.WHEAT2, B.WHEAT3 } },
                { "carrot", new[] { B.CARROTS0, B.CARROTS1, B.CARROTS2, B.CARROTS3 } },
                { "potato", new[] { B.POTATOES0, B.POTATOES1, B.POTATOES2, B.POTATOES3 } },
                { "pumpkin", new[] { B.PUMPKIN_STEM0, B.PUMPKIN_STEM1, B.PUMPKIN_STEM2, B.PUMPKIN_STEM3 } },
                { "melon", new[] { B.MELON_STEM0, B.MELON_STEM1, B.MELON_STEM2, B.MELON_STEM3 } } };
            Array.Clear(CROP_INFO, 0, CROP_INFO.Length);
            foreach (var kv in CROP_TYPES)
                for (int idx = 0; idx < kv.Value.Length; idx++)
                    CROP_INFO[kv.Value[idx]] = new CropInfo { type = kv.Key, idx = idx, stages = kv.Value, fruit = kv.Key == "pumpkin" ? B.PUMPKIN : kv.Key == "melon" ? B.MELON : 0 };
        }
        public static CropInfo cropInfo(int id) { return id >= 0 && id < 256 ? CROP_INFO[id] : null; }
        public static bool isCropBlock(int id) { return cropInfo(id) != null; }

        // ---------------- sections ----------------
        public static int index(int lx, int iy, int lz) { return (iy * CHUNK + lz) * CHUNK + lx; }
        public static int worldIndex(int lx, int y, int lz) { return index(lx, y - WORLD_MIN_Y, lz); }
        public static bool yInWorld(int y) { return y >= WORLD_MIN_Y && y < WORLD_MAX_Y; }
        public static int sectionSlot(int y) { return (y - WORLD_MIN_Y) >> 4; }
        public static int sectionIndex(int lx, int y, int lz) { int yy = y - WORLD_MIN_Y; return ((yy & 15) * CHUNK + lz) * CHUNK + lx; }
        public static Section[] emptySections() { return new Section[SECTION_COUNT]; }
        public static Section newSection() { return new Section(); }
        public static byte[] ensureSectionLight(Section sec)
        {
            if (sec != null && sec.light == null) { sec.light = new byte[4096]; for (int i = 0; i < 4096; i++) sec.light[i] = 0xf0; }
            return sec != null ? sec.light : null;
        }
        public static int chunkGet(Chunk c, int lx, int y, int lz)
        {
            if (c == null || !yInWorld(y) || lx < 0 || lx >= CHUNK || lz < 0 || lz >= CHUNK) return B.AIR;
            var sec = c.sections[sectionSlot(y)];
            return sec != null ? sec.blocks[sectionIndex(lx, y, lz)] : B.AIR;
        }
        public static void chunkSet(Chunk c, int lx, int y, int lz, int id)
        {
            if (c == null || !yInWorld(y) || lx < 0 || lx >= CHUNK || lz < 0 || lz >= CHUNK) return;
            int si = sectionSlot(y), ii = sectionIndex(lx, y, lz);
            var sec = c.sections[si];
            if (sec == null) { if (id == B.AIR) return; sec = newSection(); c.sections[si] = sec; }
            else ensureSectionLight(sec);
            sec.blocks[ii] = (byte)id;
        }

        /// <summary>Initial chunk light: sky columns + BFS flood, block emitters + BFS flood. Packed sky&lt;&lt;4|block.</summary>
        public static byte[] buildInitialPackedLightDense(byte[] data, List<GenMetaEntry> meta)
        {
            int N = CHUNK * WORLD_H * CHUNK;
            var sky = new byte[N]; var blk = new byte[N]; var q = new List<int>(); var extraEmit = new byte[N];
            if (meta != null)
                foreach (var m in meta)
                {
                    if (m.m == null || !m.m.Bool("berries")) continue;
                    if (m.lx >= 0 && m.lx < CHUNK && m.lz >= 0 && m.lz < CHUNK && m.iy >= 0 && m.iy < WORLD_H) extraEmit[(m.iy * CHUNK + m.lz) * CHUNK + m.lx] = 14;
                }
            for (int z = 0; z < CHUNK; z++)
                for (int x = 0; x < CHUNK; x++)
                {
                    int sv = 15;
                    for (int y = WORLD_H - 1; y >= 0; y--)
                    {
                        int p = (y * CHUNK + z) * CHUNK + x, id = data[p]; var b = blocks[id];
                        if (b != null && b.light != 0) blk[p] = (byte)Math.Min(15, b.light);
                        if (extraEmit[p] > blk[p]) blk[p] = extraEmit[p];
                        if (lightStops(id)) { sv = 0; continue; }
                        if (sv > 0 && lightAttenuation(id)) sv = Math.Max(0, sv - 2);
                        if (sv > 0) { sky[p] = (byte)sv; q.Add(p); }
                    }
                }
            Action<byte[]> flood = a =>
            {
                int h = 0;
                while (h < q.Count)
                {
                    int p = q[h++], lv = a[p];
                    if (lv <= 1) continue;
                    int x = p % CHUNK, z = (p / CHUNK) % CHUNK, y = p / (CHUNK * CHUNK);
                    for (int k = 0; k < 6; k++)
                    {
                        int n;
                        if (k == 0) { if (x + 1 >= CHUNK) continue; n = p + 1; }
                        else if (k == 1) { if (x <= 0) continue; n = p - 1; }
                        else if (k == 2) { if (z + 1 >= CHUNK) continue; n = p + CHUNK; }
                        else if (k == 3) { if (z <= 0) continue; n = p - CHUNK; }
                        else if (k == 4) { if (y + 1 >= WORLD_H) continue; n = p + CHUNK * CHUNK; }
                        else { if (y <= 0) continue; n = p - CHUNK * CHUNK; }
                        int id = data[n];
                        if (lightStops(id)) continue;
                        int nv = lv - (lightAttenuation(id) ? 2 : 1);
                        if (nv > a[n]) { a[n] = (byte)nv; q.Add(n); }
                    }
                }
            };
            flood(sky);
            q.Clear();
            for (int p = 0; p < N; p++) if (blk[p] != 0) q.Add(p);
            flood(blk);
            var packed = new byte[N];
            for (int p = 0; p < N; p++) packed[p] = (byte)((sky[p] << 4) | blk[p]);
            return packed;
        }
        public static Section[] denseToSections(byte[] data, List<GenMetaEntry> meta)
        {
            var packed = buildInitialPackedLightDense(data, meta);
            var outp = emptySections();
            for (int si = 0; si < SECTION_COUNT; si++)
            {
                int off = si * 4096; bool used = false;
                for (int i = 0; i < 4096; i++) if (data[off + i] != B.AIR || packed[off + i] != 0xf0) { used = true; break; }
                if (used)
                {
                    var b = new byte[4096]; var l = new byte[4096];
                    Buffer.BlockCopy(data, off, b, 0, 4096); Buffer.BlockCopy(packed, off, l, 0, 4096);
                    outp[si] = new Section(b, l);
                }
            }
            return outp;
        }
        public static byte[] sectionsToDense(Section[] sections)
        {
            var data = new byte[CHUNK * WORLD_H * CHUNK];
            if (sections == null) return data;
            for (int si = 0; si < SECTION_COUNT; si++) { var sec = sections[si]; if (sec != null && sec.blocks != null) Buffer.BlockCopy(sec.blocks, 0, data, si * 4096, 4096); }
            return data;
        }
        public static void putIfInside(byte[] data, int cx, int cz, int x, int y, int z, int id)
        {
            int lx = x - cx * CHUNK, lz = z - cz * CHUNK;
            if (lx >= 0 && lx < CHUNK && lz >= 0 && lz < CHUNK && yInWorld(y)) { int ii = worldIndex(lx, y, lz); if (data[ii] == B.AIR) data[ii] = (byte)id; }
        }
        public static void putWaterPlantIfInside(byte[] data, int cx, int cz, int x, int y, int z, int id)
        {
            int lx = x - cx * CHUNK, lz = z - cz * CHUNK;
            if (lx >= 0 && lx < CHUNK && lz >= 0 && lz < CHUNK && yInWorld(y)) { int ii = worldIndex(lx, y, lz); if (data[ii] == B.WATER) data[ii] = (byte)id; }
        }
        public static int getBlock(double x, double y, double z) { return getBlock(JS.floor(x), JS.floor(y), JS.floor(z)); }
        public static int getBlock(int x, int y, int z)
        {
            if (y < WORLD_MIN_Y) return B.BEDROCK;
            if (y >= WORLD_MAX_Y) return B.AIR;
            int cx = x >> 4, cz = z >> 4;
            var c = chunkFastGet(cx, cz);
            if (c != null) return chunkGet(c, x - cx * CHUNK, y, z - cz * CHUNK);
            int ed;
            if (edits.TryGetValue(key3(x, y, z), out ed)) return ed;
            return baseBlock(x, y, z);
        }
        public static JObj getBlockMeta(double x, double y, double z) { return getBlockMeta(JS.floor(x), JS.floor(y), JS.floor(z)); }
        public static JObj getBlockMeta(int x, int y, int z)
        {
            if (!yInWorld(y)) return null;
            int cx = x >> 4, cz = z >> 4;
            var c = chunkFastGet(cx, cz);
            var ck = c != null ? c.key : ckey(cx, cz);
            int lk = metaLocalKey(x, y, z);
            Dictionary<int, JObj> mm, gm; OrderedMap<int, int[]> em; JObj v;
            metaByChunk.TryGetValue(ck, out mm);
            em = editsByChunk.Get(ck);
            if (em != null && em.Has(lk)) return mm != null && mm.TryGetValue(lk, out v) ? v : null;
            if (mm != null && mm.TryGetValue(lk, out v) && v != null) return v;
            if (generatedMetaByChunk.TryGetValue(ck, out gm) && gm.TryGetValue(lk, out v)) return v;
            return null;
        }
        public static void installGeneratedMeta(int cx, int cz, List<GenMetaEntry> entries)
        {
            var ck = ckey(cx, cz);
            if (entries == null || entries.Count == 0) { generatedMetaByChunk.Remove(ck); return; }
            var gm = new Dictionary<int, JObj>();
            foreach (var q in entries)
            {
                if (q.lx < 0 || q.lx >= CHUNK || q.lz < 0 || q.lz >= CHUNK || q.iy < 0 || q.iy >= WORLD_H || q.m == null) continue;
                gm[(q.iy << 8) | (q.lz << 4) | q.lx] = q.m;
            }
            if (gm.Count > 0) generatedMetaByChunk[ck] = gm; else generatedMetaByChunk.Remove(ck);
        }

        // ---------------- simulation keys & fluid rules ----------------
        public static string simKey(int x, int y, int z) { return x + "," + y + "," + z; }
        static readonly int[] SIM_DECODE = new int[3];
        public static readonly int[][] FLUID_XZ_DIRS = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
        public static readonly int[][] FLUID_NEIGHBOR_DIRS = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        public static readonly int[][] SIM_AROUND_DIRS = { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        public static int[] decodeSimKey(string s, int[] outp = null)
        {
            if (outp == null) outp = SIM_DECODE;
            int i = 0, n = s.Length, sign = 1, v = 0;
            if (s[i] == '-') { sign = -1; i++; }
            for (; i < n && s[i] != ','; i++) v = v * 10 + s[i] - '0';
            outp[0] = v * sign; i++; sign = 1; v = 0;
            if (s[i] == '-') { sign = -1; i++; }
            for (; i < n && s[i] != ','; i++) v = v * 10 + s[i] - '0';
            outp[1] = v * sign; i++; sign = 1; v = 0;
            if (i < n && s[i] == '-') { sign = -1; i++; }
            for (; i < n; i++) v = v * 10 + s[i] - '0';
            outp[2] = v * sign;
            return outp;
        }
        public static bool chunkLoadedAt(int x, int z) { return chunkFastGet(x >> 4, z >> 4) != null; }
        public static int waterLevel(int id)
        {
            if (id == B.WATER || id == B.WATER_FALLING) return 8;
            if (id == B.FLOW7) return 7; if (id == B.FLOW6) return 6; if (id == B.FLOW5) return 5; if (id == B.FLOW4) return 4;
            if (id == B.FLOW3) return 3; if (id == B.FLOW2) return 2; if (id == B.FLOW1) return 1;
            return 0;
        }
        public static bool waterFalling(int id) { return id == B.WATER_FALLING; }
        // Minecraft has one full falling-water fluid state (level 8 + FALLING).
        public static int waterFallingStrengthAt(int x, int y, int z) { return 8; }
        public static bool waterFallingLandingSupport(int x, int y, int z)
        {
            int below = fluidBlockAt(x, y - 1, z);
            return waterSolidSupport(below) || waterSourceAt(x, y - 1, z, below) || (isFluidWater(below) && below != B.WATER_FALLING);
        }
        public static int waterHorizontalLevelAt(int x, int y, int z) { return waterHorizontalLevelAt(x, y, z, fluidBlockAt(x, y, z)); }
        public static int waterHorizontalLevelAt(int x, int y, int z, int id)
        {
            if (waterSourceAt(x, y, z, id)) return 8;
            if (id == B.WATER_FALLING) return waterFallingLandingSupport(x, y, z) ? waterFallingStrengthAt(x, y, z) : 0;
            return waterLevel(id);
        }
        public static int fluidBlockAt(int x, int y, int z)
        {
            if (y < WORLD_MIN_Y) return B.BEDROCK;
            if (y >= WORLD_MAX_Y) return B.AIR;
            int cx = x >> 4, cz = z >> 4; var c = chunkFastGet(cx, cz);
            if (c == null) return B.AIR;
            return chunkGet(c, x - cx * CHUNK, y, z - cz * CHUNK);
        }
        public static JObj fluidMetaAt(int x, int y, int z)
        {
            if (!yInWorld(y) || !chunkLoadedAt(x, z)) return null;
            return getBlockMeta(x, y, z);
        }
        public static bool mainCrossPlantCell(int id) { var b = bdef(id); return b != null && b.plant && b.special == null; }
        public static bool waterSourceAt(int x, int y, int z) { return waterSourceAt(x, y, z, fluidBlockAt(x, y, z)); }
        public static bool waterSourceAt(int x, int y, int z, int id)
        {
            if (id == B.WATER) return true;
            var b = bdef(id);
            if (b == null || !b.waterPlant) return false;
            if (b.needsWater) return true;
            var m = fluidMetaAt(x, y, z);
            return m != null && m.Bool("waterlogged");
        }
        public static bool waterReplaceableCell(int id) { return id == B.AIR || (mainCrossPlantCell(id) && !(bdef(id) != null && bdef(id).waterPlant)); }
        public static bool waterSolidSupport(int id) { return id != B.AIR && !isFluidWater(id) && !isLava(id) && !mainCrossPlantCell(id) && !(bdef(id) != null && bdef(id).special != null); }
        public static int waterBlockForLevel(int n) { return n >= 7 ? B.FLOW7 : n == 6 ? B.FLOW6 : n == 5 ? B.FLOW5 : n == 4 ? B.FLOW4 : n == 3 ? B.FLOW3 : n == 2 ? B.FLOW2 : B.FLOW1; }
        // Minecraft FlowingFluid-style slope search.
        const int WATER_SLOPE_FIND_DISTANCE = 4, WATER_SLOPE_INF = 99;
        static readonly int[] waterSlopeX = new int[96], waterSlopeZ = new int[96], waterSlopeSeenX = new int[96], waterSlopeSeenZ = new int[96];
        static readonly byte[] waterSlopeD = new byte[96];
        static bool waterHorizontalTargetAllowed(int id, int outLevel)
        {
            if (waterReplaceableCell(id)) return true;
            return isFluidWater(id) && id != B.WATER && id != B.WATER_FALLING && waterLevel(id) < outLevel;
        }
        static bool waterHasDownwardHole(int x, int y, int z) { return y - 1 >= WORLD_MIN_Y && waterReplaceableCell(fluidBlockAt(x, y - 1, z)); }
        static bool waterSlopeSeen(int x, int z, int count)
        {
            for (int i = 0; i < count; i++) if (waterSlopeSeenX[i] == x && waterSlopeSeenZ[i] == z) return true;
            return false;
        }
        static int waterSlopeDistance(int startX, int y, int startZ, int sourceX, int sourceZ, int outLevel)
        {
            if (waterHasDownwardHole(startX, y, startZ)) return 0;
            int head = 0, tail = 0, seen = 0;
            waterSlopeX[tail] = startX; waterSlopeZ[tail] = startZ; waterSlopeD[tail++] = 0;
            waterSlopeSeenX[seen] = startX; waterSlopeSeenZ[seen++] = startZ;
            while (head < tail)
            {
                int x = waterSlopeX[head], z = waterSlopeZ[head], dist = waterSlopeD[head++];
                if (dist >= WATER_SLOPE_FIND_DISTANCE) continue;
                for (int k = 0; k < 4; k++)
                {
                    var d = FLUID_XZ_DIRS[k]; int nx = x + d[0], nz = z + d[1];
                    if (nx == sourceX && nz == sourceZ) continue;
                    if (waterSlopeSeen(nx, nz, seen)) continue;
                    int q = fluidBlockAt(nx, y, nz);
                    if (!waterHorizontalTargetAllowed(q, outLevel)) continue;
                    int nd = dist + 1;
                    if (waterHasDownwardHole(nx, y, nz)) return nd;
                    if (nd < WATER_SLOPE_FIND_DISTANCE && tail < waterSlopeX.Length)
                    {
                        waterSlopeSeenX[seen] = nx; waterSlopeSeenZ[seen++] = nz;
                        waterSlopeX[tail] = nx; waterSlopeZ[tail] = nz; waterSlopeD[tail++] = (byte)nd;
                    }
                }
            }
            return WATER_SLOPE_INF;
        }
        static readonly short[] waterSpreadDirCost = new short[4];
        static readonly byte[] waterSpreadDirAllowed = new byte[4];
        public static int minecraftWaterSpreadMask(int x, int y, int z, int outLevel)
        {
            int best = WATER_SLOPE_INF; bool any = false;
            for (int k = 0; k < 4; k++)
            {
                var d = FLUID_XZ_DIRS[k]; int nx = x + d[0], nz = z + d[1], q = fluidBlockAt(nx, y, nz);
                if (!waterHorizontalTargetAllowed(q, outLevel)) { waterSpreadDirAllowed[k] = 0; waterSpreadDirCost[k] = WATER_SLOPE_INF; continue; }
                waterSpreadDirAllowed[k] = 1; any = true;
                int cost = waterSlopeDistance(nx, y, nz, x, z, outLevel);
                waterSpreadDirCost[k] = (short)cost;
                if (cost < best) best = cost;
            }
            if (!any) return 0;
            int mask = 0;
            for (int k = 0; k < 4; k++) if (waterSpreadDirAllowed[k] != 0 && (best == WATER_SLOPE_INF || waterSpreadDirCost[k] == best)) mask |= 1 << k;
            return mask;
        }
        public static bool waterCanCreateSource(int x, int y, int z)
        {
            int sources = 0;
            if (waterSourceAt(x + 1, y, z)) sources++;
            if (waterSourceAt(x - 1, y, z)) sources++;
            if (waterSourceAt(x, y, z + 1)) sources++;
            if (waterSourceAt(x, y, z - 1)) sources++;
            if (sources < 2) return false;
            int below = fluidBlockAt(x, y - 1, z);
            return waterSourceAt(x, y - 1, z, below) || waterSolidSupport(below);
        }
        public static bool waterInteriorShouldSource(int x, int y, int z) { return waterInteriorShouldSource(x, y, z, fluidBlockAt(x, y, z)); }
        public static bool waterInteriorShouldSource(int x, int y, int z, int id)
        {
            if (id == B.WATER_FALLING || id == B.WATER || !isFluidWater(id)) return false;
            return waterCanCreateSource(x, y, z);
        }
        public static int strongestHorizontalWater(int x, int y, int z)
        {
            int best = 0;
            best = Math.Max(best, waterHorizontalLevelAt(x + 1, y, z));
            best = Math.Max(best, waterHorizontalLevelAt(x - 1, y, z));
            best = Math.Max(best, waterHorizontalLevelAt(x, y, z + 1));
            best = Math.Max(best, waterHorizontalLevelAt(x, y, z - 1));
            return best;
        }
        public static int lavaLevel(int id) { return id == B.LAVA ? 3 : id == B.LAVA_FLOW2 ? 2 : id == B.LAVA_FLOW1 ? 1 : 0; }
        public static int lavaBlockForLevel(int n) { return n >= 2 ? B.LAVA_FLOW2 : B.LAVA_FLOW1; }
        public static void queueWater(int x, int y, int z, bool seed = false, bool urgent = false)
        {
            var q = urgent ? worldSim.waterUrgentQueue : seed ? worldSim.waterSeedQueue : worldSim.waterQueue;
            int cap = urgent ? 2048 : 20000;
            if (q.Count < cap) q.Add(simKey(x, y, z));
        }
        public static void queueLava(int x, int y, int z, bool seed = false)
        {
            var q = seed ? worldSim.lavaSeedQueue : worldSim.lavaQueue;
            if (q.Count < 20000) q.Add(simKey(x, y, z));
        }
        // Immediate fluid mixing: side/top water + source lava -> obsidian, + flowing lava -> cobblestone, lava flowing down into water -> stone.
        static readonly int[][] FLUID_MIX_RING = { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        static readonly int[][] FLUID_SIDE_TOP = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        static int fluidMixGuard = 0;
        public static bool fluidWaterAt(int x, int y, int z) { int id = fluidBlockAt(x, y, z); return waterLevel(id) > 0 || waterSourceAt(x, y, z, id); }
        static bool resolveLavaWaterAt(int x, int y, int z)
        {
            int id = fluidBlockAt(x, y, z), lev = lavaLevel(id);
            if (lev == 0) return false;
            bool sideTopWater = false;
            foreach (var d in FLUID_SIDE_TOP) if (fluidWaterAt(x + d[0], y + d[1], z + d[2])) { sideTopWater = true; break; }
            if (sideTopWater) { setFluidBlock(x, y, z, id == B.LAVA ? B.OBSIDIAN : B.COBBLE); return true; }
            if (fluidWaterAt(x, y - 1, z)) { setFluidBlock(x, y - 1, z, B.STONE); return true; }
            return false;
        }
        public static bool resolveFluidMixAround(int x, int y, int z)
        {
            if (fluidMixGuard != 0) return false;
            fluidMixGuard++;
            bool changed = false;
            try { foreach (var d in FLUID_MIX_RING) changed = resolveLavaWaterAt(x + d[0], y + d[1], z + d[2]) || changed; }
            finally { fluidMixGuard--; }
            return changed;
        }

        // ---------------- trees / flammability ----------------
        static HashSet<int> TREE_LOG_IDS, TREE_LEAF_IDS, NATIVE_FLAMMABLE_IDS;
        static void InitTreeSets()
        {
            TREE_LOG_IDS = new HashSet<int> { B.LOG, B.SPRUCE_LOG, B.BIRCH_LOG, B.JUNGLE_LOG, B.ACACIA_LOG, B.DARK_LOG };
            TREE_LEAF_IDS = new HashSet<int> { B.LEAVES, B.SPRUCE_LEAVES, B.BIRCH_LEAVES, B.JUNGLE_LEAVES, B.ACACIA_LEAVES, B.DARK_LEAVES, B.CHERRY_LEAVES, B.AZALEA_LEAVES, B.FLOWERING_AZALEA_LEAVES };
            NATIVE_FLAMMABLE_IDS = new HashSet<int> { B.PLANKS, B.SPRUCE_PLANKS, B.BIRCH_PLANKS, B.DARK_PLANKS, B.BOOKSHELF, B.CRAFT, B.SAPLING, B.OAK_FENCE, B.SPRUCE_FENCE,
                B.BIRCH_FENCE, B.DARK_FENCE, B.OAK_GATE, B.SPRUCE_GATE, B.BIRCH_GATE, B.OAK_TRAPDOOR, B.SPRUCE_TRAPDOOR, B.BIRCH_TRAPDOOR };
        }
        static readonly Regex OVERWORLD_WOOD_PREFIX = new Regex("^(?:OAK|SPRUCE|BIRCH|JUNGLE|ACACIA|DARK_OAK|CHERRY|MANGROVE|BAMBOO)(?:_|$)");
        static readonly Regex RX_VTREELOG = new Regex("^(?:STRIPPED_)?(?:OAK|SPRUCE|BIRCH|JUNGLE|ACACIA|DARK_OAK)_(?:LOG|WOOD)$");
        public static bool isTreeLog(int id) { return TREE_LOG_IDS.Contains(id); }
        public static bool isTreeLeaves(int id) { return TREE_LEAF_IDS.Contains(id); }
        public static bool virtualTreeLogKey(string k) { return RX_VTREELOG.IsMatch(k ?? ""); }
        public static bool isTreeLogState(int id, JObj meta) { return isTreeLog(id) || (isVirtualId(id) && virtualTreeLogKey(meta != null ? meta.Str("v") : null)); }
        public static bool isTreeLogAt(int x, int y, int z) { int id = getBlock(x, y, z); return isTreeLogState(id, getBlockMeta(x, y, z)); }
        static readonly Regex RX_NETHERWOOD = new Regex("^(?:CRIMSON|WARPED)(?:_|$)"), RX_VSAPL = new Regex("^(?:ACACIA|BIRCH|DARK_OAK|JUNGLE|SPRUCE)_SAPLING$"),
            RX_WOOL = new Regex("^WOOL_"), RX_CARPET = new Regex("_CARPET$"), RX_BEDNH = new Regex("^BED_(?!HEAD)"),
            RX_WOODPART = new Regex("(?:PLANKS|WOOD|LOG|DOOR|TRAPDOOR|FENCE|GATE|BUTTON|PRESSURE_PLATE|SLAB|STAIRS)"), RX_PLANKBTN = new Regex("^PLANKS_(?:BUTTON|PRESSURE_PLATE)$");
        static bool virtualFlammableState(JObj meta, VirtualDef d)
        {
            var k = meta != null ? meta.Str("v") : null;
            if (string.IsNullOrEmpty(k) || d == null) return false;
            if (RX_NETHERWOOD.IsMatch(k)) return false;
            if (virtualTreeLogKey(k)) return true;
            if (k == "CRAFTING_TABLE" || k == "OAK_SAPLING" || RX_VSAPL.IsMatch(k)) return true;
            if (RX_WOOL.IsMatch(k) || RX_CARPET.IsMatch(k) || RX_BEDNH.IsMatch(k)) return true;
            if (OVERWORLD_WOOD_PREFIX.IsMatch(k) && RX_WOODPART.IsMatch(k)) return true;
            if (RX_PLANKBTN.IsMatch(k)) return true;
            return false;
        }
        public static bool flammableState(int id, JObj meta)
        {
            return isTreeLogState(id, meta) || isTreeLeaves(id) || NATIVE_FLAMMABLE_IDS.Contains(id) || isWoolBlock(id) || (isVirtualId(id) && virtualFlammableState(meta, virtualDefFromMeta(meta)));
        }
        public static bool flammable(int id) { return flammableState(id, null); }
        public static bool flammableAt(int x, int y, int z) { int id = getBlock(x, y, z); return flammableState(id, getBlockMeta(x, y, z)); }
        public static bool isWoolBlock(int id) { return id >= B.WOOL_WHITE && id <= B.WOOL_PINK; }
        public static void queueLeaf(int x, int y, int z)
        {
            if (isTreeLeaves(getBlock(x, y, z)))
            {
                var k = simKey(x, y, z); double factor = modEnabled("fastleaves") ? 1 : 2.5;
                if (!worldSim.leafDecay.Has(k)) worldSim.leafDecay.Set(k, (0.6 + JS.random() * 2.4) * factor);
            }
        }
        public static void queueAroundSimulation(int x, int y, int z)
        {
            bool urgentWater = isPlayerEditActive();
            foreach (var d in SIM_AROUND_DIRS)
            {
                int px = x + d[0], py = y + d[1], pz = z + d[2], id = getBlock(px, py, pz);
                // Player edits must not wait behind thousands of background ocean/cave fluid seeds.
                queueWater(px, py, pz, false, urgentWater);
                queueLava(px, py, pz);
                if (id == B.FARMLAND || id == B.FARMLAND_MOIST) { var k = simKey(px, py, pz); if (!worldSim.farmland.Has(k)) worldSim.farmland.Set(k, new FarmState { dryT = 0 }); }
                if (isCropBlock(id)) { var k = simKey(px, py, pz); if (!worldSim.crops.Has(k)) worldSim.crops.Set(k, new TimerState { t = 0 }); }
                if (saplingSpeciesAt(px, py, pz) != null) { var k = simKey(px, py, pz); if (!worldSim.saplings.Has(k)) worldSim.saplings.Set(k, new TimerState { t = 0, delay = 90 + JS.random() * 90 }); }
                if (id == B.CACTUS) { var k = simKey(px, py, pz); if (!worldSim.growColumns.Has(k)) worldSim.growColumns.Set(k, new TimerState { t = JS.random() * 30 }); }
                if (id == B.FIRE) { var k = simKey(px, py, pz); if (!worldSim.fires.Has(k)) worldSim.fires.Set(k, new FireState { t = 0, next = 0.7 + JS.random() * 0.6, age = 0 }); }
                if (isTreeLeaves(id)) queueLeaf(px, py, pz);
            }
        }
        public static bool waterNearbyFarmland(int x, int y, int z)
        {
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = -4; dx <= 4; dx++) for (int dz = -4; dz <= 4; dz++) if (isWater(getBlock(x + dx, y + dy, z + dz))) return true;
            return false;
        }
        public static bool leafConnectedToLog(int x, int y, int z)
        {
            var q = new List<int[]> { new[] { x, y, z, 0 } };
            var seen = new HashSet<string> { simKey(x, y, z) };
            for (int h = 0; h < q.Count; h++)
            {
                var e = q[h]; int px = e[0], py = e[1], pz = e[2], d = e[3];
                if (d >= 6) continue;
                foreach (var dd in FLUID_NEIGHBOR_DIRS)
                {
                    int nx = px + dd[0], ny = py + dd[1], nz = pz + dd[2], id = getBlock(nx, ny, nz);
                    if (isTreeLogState(id, getBlockMeta(nx, ny, nz))) return true;
                    if (isTreeLeaves(id)) { var k = simKey(nx, ny, nz); if (!seen.Contains(k)) { seen.Add(k); q.Add(new[] { nx, ny, nz, d + 1 }); } }
                }
            }
            return false;
        }
        public const int SIM_INDEX_BITS = 17, SIM_INDEX_MASK = (1 << SIM_INDEX_BITS) - 1;
        public const int SIM_WATER = 1, SIM_LAVA = 2, SIM_FARMLAND = 3, SIM_CROP = 4, SIM_SAPLING = 5, SIM_CACTUS = 6, SIM_FIRE = 7;
        public static void simLocalXYZ(uint code, out int lx, out int y, out int lz)
        {
            int p = (int)(code & SIM_INDEX_MASK);
            lx = p & 15; y = (p >> 8) + WORLD_MIN_Y; lz = (p >> 4) & 15;
        }
        public static void installChunkSimulationSeeds(Chunk c, uint[] sim, uint[] boundaryData)
        {
            if (c == null) return;
            c.fluidBoundary = boundaryData ?? new uint[0];
            if (sim != null)
                for (int n = 0; n < sim.Length; n++)
                {
                    uint code = sim[n]; int tag = (int)(code >> SIM_INDEX_BITS), lx, y, lz;
                    simLocalXYZ(code, out lx, out y, out lz);
                    int wx = c.cx * CHUNK + lx, wz = c.cz * CHUNK + lz; var k = simKey(wx, y, wz);
                    if (tag == SIM_WATER) queueWater(wx, y, wz, true);
                    else if (tag == SIM_LAVA) queueLava(wx, y, wz, true);
                    else if (tag == SIM_FARMLAND) worldSim.farmland.Set(k, new FarmState { dryT = 0 });
                    else if (tag == SIM_CROP) { if (!worldSim.crops.Has(k)) worldSim.crops.Set(k, new TimerState { t = 0 }); }
                    else if (tag == SIM_SAPLING) { if (!worldSim.saplings.Has(k)) worldSim.saplings.Set(k, new TimerState { t = 0, delay = 90 + JS.random() * 90 }); }
                    else if (tag == SIM_CACTUS) worldSim.growColumns.Set(k, new TimerState { t = JS.random() * 30 });
                    else if (tag == SIM_FIRE) worldSim.fires.Set(k, new FireState { t = 0, next = 0.7 + JS.random() * 0.6, age = 0 });
                }
            wakeChunkFluidBoundaries(c);
        }
        public static void installMetadataSimulationSeeds(Chunk c)
        {
            Dictionary<int, JObj> mm;
            if (!metaByChunk.TryGetValue(c.key, out mm) || mm.Count == 0) return;
            foreach (var kv in mm.ToList())
            {
                var m = kv.Value; if (m == null) continue;
                int code = kv.Key, lx = code & 15, lz = (code >> 4) & 15, y = (code >> 8) + WORLD_MIN_Y, x = c.cx * CHUNK + lx, z = c.cz * CHUNK + lz;
                var k = simKey(x, y, z); var d = virtualDefFromMeta(m); var mv = m.Str("v");
                if (mv != null && VIRTUAL_SAPLING_SPECIES.ContainsKey(mv) && saplingSpeciesAt(x, y, z) != null && !worldSim.saplings.Has(k))
                    worldSim.saplings.Set(k, new TimerState { t = 0, delay = 90 + JS.random() * 90 });
                if (d != null && d.light != 0) queueLightUpdate(x, y, z);
            }
        }
        static void wakeFluidBoundarySide(Chunk c, int dx, int dz)
        {
            var list = c != null ? c.fluidBoundary : null;
            if (list == null || list.Length == 0) return;
            for (int n = 0; n < list.Length; n++)
            {
                uint code = list[n]; int tag = (int)(code >> SIM_INDEX_BITS), lx, y, lz;
                simLocalXYZ(code, out lx, out y, out lz);
                if ((dx == 1 && lx != CHUNK - 1) || (dx == -1 && lx != 0) || (dz == 1 && lz != CHUNK - 1) || (dz == -1 && lz != 0)) continue;
                int wx = c.cx * CHUNK + lx, wz = c.cz * CHUNK + lz, nx = wx + dx, nz = wz + dz, other = fluidBlockAt(nx, y, nz);
                if (tag == SIM_WATER)
                {
                    int self = fluidBlockAt(wx, y, wz);
                    bool submergedSource = self == B.WATER && isFluidWater(fluidBlockAt(wx, y + 1, wz));
                    if (lavaLevel(other) > 0 || (waterReplaceableCell(other) && !submergedSource)) queueWater(wx, y, wz, true);
                    if (lavaLevel(other) > 0) resolveFluidMixAround(wx, y, wz);
                }
                else if (tag == SIM_LAVA)
                {
                    if (waterReplaceableCell(other) || waterLevel(other) > 0 || waterSourceAt(nx, y, nz, other)) queueLava(wx, y, wz, true);
                    if (waterLevel(other) > 0 || waterSourceAt(nx, y, nz, other)) resolveFluidMixAround(wx, y, wz);
                }
            }
        }
        public static void wakeChunkFluidBoundaries(Chunk c)
        {
            foreach (var d in FLUID_XZ_DIRS)
            {
                var n = chunkFastGet(c.cx + d[0], c.cz + d[1]);
                if (n == null) continue;
                wakeFluidBoundarySide(c, d[0], d[1]);
                wakeFluidBoundarySide(n, -d[0], -d[1]);
            }
        }

        static int fluidMutationDepth = 0;
        public static bool setFluidBlock(int x, int y, int z, int id, JObj meta = null)
        {
            if (!yInWorld(y) || !chunkLoadedAt(x, z)) return false;
            fluidMutationDepth++;
            try
            {
                // Flowing/falling fluid and solver-created AIR are runtime state and are not persisted as edits.
                bool persist = !(id == B.AIR || isFluidWater(id) || isLava(id));
                setBlock(x, y, z, id, meta, persist);
                return true;
            }
            finally { fluidMutationDepth--; }
        }
        static int playerEditDepth = 0, playerEditLightN = 0;
        static bool playerEditFlushActive = false;
        const int PLAYER_EDIT_LIGHT_SYNC_MAX = 6;
        static readonly int[] playerEditLightXYZ = new int[PLAYER_EDIT_LIGHT_SYNC_MAX * 3 + 3], playerEditSkyX = new int[PLAYER_EDIT_LIGHT_SYNC_MAX], playerEditSkyZ = new int[PLAYER_EDIT_LIGHT_SYNC_MAX];
        static readonly Dictionary<string, HashSet<int>> playerEditLightDirty = new Dictionary<string, HashSet<int>>();
        static void notePlayerEditLight(int x, int y, int z)
        {
            if (playerEditDepth <= 0) return;
            for (int i = 0; i < Math.Min(playerEditLightN, PLAYER_EDIT_LIGHT_SYNC_MAX); i++)
                if (playerEditLightXYZ[i * 3] == x && playerEditLightXYZ[i * 3 + 1] == y && playerEditLightXYZ[i * 3 + 2] == z) return;
            if (playerEditLightN <= PLAYER_EDIT_LIGHT_SYNC_MAX) { int o = playerEditLightN * 3; playerEditLightXYZ[o] = x; playerEditLightXYZ[o + 1] = y; playerEditLightXYZ[o + 2] = z; }
            playerEditLightN++;
        }
        static bool flushImmediatePlayerEditLighting()
        {
            int n = playerEditLightN; playerEditLightN = 0;
            if (n == 0) return true;
            // A normal break/place touches only a handful of light sources/occluders. Solve those now
            // so the edit mesh can never be built from stale light. Larger cascades stay budgeted.
            if (n > PLAYER_EDIT_LIGHT_SYNC_MAX) return false;
            var dirty = playerEditLightDirty; dirty.Clear();
            int skyN = 0;
            for (int i = 0; i < n; i++)
            {
                int o = i * 3, x = playerEditLightXYZ[o], y = playerEditLightXYZ[o + 1], z = playerEditLightXYZ[o + 2];
                lightDirtyKeys.Remove(key3(x, y, z)); // queued copy becomes a cheap stale entry and is skipped later
                if (!lightCellLoaded(x, z)) continue;
                updateBlockLightAt(x, y, z, dirty);
                bool seen = false;
                for (int q = 0; q < skyN; q++) if (playerEditSkyX[q] == x && playerEditSkyZ[q] == z) { seen = true; break; }
                if (!seen) { playerEditSkyX[skyN] = x; playerEditSkyZ[skyN++] = z; updateSkyLightAt(x, y, z, dirty); }
            }
            markLightDirtyChunks(dirty);
            dirty.Clear();
            return true;
        }
        static int flushImmediatePlayerEditMeshes()
        {
            if (playerEditFlushActive || dirtyEditChunks.Count == 0 || mainMeshCore == null || !started || !worldReady) return 0;
            playerEditFlushActive = true;
            try
            {
                int pcx = JS.floor(player.x / CHUNK), pcz = JS.floor(player.z / CHUNK);
                return processDirtyEditMeshes(pcx, pcz, double.PositiveInfinity, 6);
            }
            finally { playerEditFlushActive = false; }
        }
        public static T withPlayerEdit<T>(Func<T> fn)
        {
            if (playerEditDepth == 0) playerEditLightN = 0;
            playerEditDepth++;
            try { return fn(); }
            finally
            {
                playerEditDepth--;
                if (playerEditDepth == 0)
                {
                    bool lightReady = flushImmediatePlayerEditLighting();
                    if (lightReady) flushImmediatePlayerEditMeshes();
                }
            }
        }
        public static void withPlayerEdit(Action fn) { withPlayerEdit<bool>(() => { fn(); return true; }); }
        public static bool isPlayerEditActive() { return playerEditDepth > 0; }

        public static void setBlock(int x, int y, int z, int id, JObj meta = null, bool persistEdit = true)
        {
            if (!yInWorld(y) || y == WORLD_MIN_Y) return;
            int oldId = getBlock(x, y, z); var k = key3(x, y, z); var oldMeta = getBlockMeta(x, y, z);
            int oldLightSig = lightSignatureState(oldId, oldMeta); bool oldTreeLog = isTreeLogState(oldId, oldMeta);
            int cx = x >> 4, cz = z >> 4;
            var mk = ckey(cx, cz);
            Dictionary<int, JObj> mm; metaByChunk.TryGetValue(mk, out mm);
            if (meta != null)
            {
                var mv = meta.Clone();
                blockMeta.Set(k, mv);
                if (mm == null) metaByChunk[mk] = mm = new Dictionary<int, JObj>();
                mm[metaLocalKey(x, y, z)] = mv;
            }
            else
            {
                blockMeta.Delete(k);
                if (mm != null) { mm.Remove(metaLocalKey(x, y, z)); if (mm.Count == 0) metaByChunk.Remove(mk); }
            }
            if (persistEdit) addEditIndex(x, y, z, id, true);
            var c = chunkFastGet(cx, cz);
            if (c != null) { chunkSet(c, x - cx * CHUNK, y, z - cz * CHUNK, id); c.mapRev++; }
            int lx = x - cx * CHUNK, lz = z - cz * CHUNK, si = sectionSlot(y), ly = (y - WORLD_MIN_Y) & 15;
            if (c != null && (lx == 0 || lx == CHUNK - 1 || lz == 0 || lz == CHUNK - 1)) c.boundaryRev++;
            bool localFluidMesh = fluidMutationDepth > 0 && c != null;
            if (localFluidMesh)
            {
                var sis = new List<int> { si };
                if (ly == 0) sis.Add(si - 1);
                if (ly == 15) sis.Add(si + 1);
                markDirtySections(cx, cz, sis);
                if (lx == 0) markDirtySections(cx - 1, cz, new List<int> { si });
                if (lx == CHUNK - 1) markDirtySections(cx + 1, cz, new List<int> { si });
                if (lz == 0) markDirtySections(cx, cz - 1, new List<int> { si });
                if (lz == CHUNK - 1) markDirtySections(cx, cz + 1, new List<int> { si });
            }
            else
            {
                markDirty(cx, cz);
                if (lx == 0) markDirty(cx - 1, cz);
                if (lx == CHUNK - 1) markDirty(cx + 1, cz);
                if (lz == 0) markDirty(cx, cz - 1);
                if (lz == CHUNK - 1) markDirty(cx, cz + 1);
            }
            if (isPlayerEditActive())
            {
                markDirtyEdit(cx, cz); markDirtyEditSection(cx, cz, si);
                if (ly == 0) markDirtyEditSection(cx, cz, si - 1);
                if (ly == 15) markDirtyEditSection(cx, cz, si + 1);
                if (lx == 0) { markDirtyEdit(cx - 1, cz); markDirtyEditSection(cx - 1, cz, si); }
                if (lx == CHUNK - 1) { markDirtyEdit(cx + 1, cz); markDirtyEditSection(cx + 1, cz, si); }
                if (lz == 0) { markDirtyEdit(cx, cz - 1); markDirtyEditSection(cx, cz - 1, si); }
                if (lz == CHUNK - 1) { markDirtyEdit(cx, cz + 1); markDirtyEditSection(cx, cz + 1, si); }
            }
            var newMeta = meta;
            int newLightSig = lightSignatureState(id, newMeta); bool newTreeLog = isTreeLogState(id, newMeta);
            if (oldLightSig != newLightSig) { queueLightUpdate(x, y, z); notePlayerEditLight(x, y, z); }
            if (oldId != id || (oldMeta != null ? oldMeta.Str("v") : null) != (newMeta != null ? newMeta.Str("v") : null))
            {
                releaseStuckProjectilesAt(x, y, z);
                queueAroundSimulation(x, y, z);
                validateAttachmentsAround(x, y, z);
                queueFalling(x, y + 1, z);
                queueFalling(x, y, z);
                if ((oldTreeLog && !newTreeLog) || id == B.AIR)
                    foreach (var d in FLUID_NEIGHBOR_DIRS) queueLeaf(x + d[0], y + d[1], z + d[2]);
                if (fluidMixGuard == 0 && (fluidMutationDepth > 0 || isPlayerEditActive() || isFluidWater(oldId) || isFluidWater(id) || isLava(oldId) || isLava(id)))
                    resolveFluidMixAround(x, y, z);
            }
        }
        public static readonly OrderedSet<string> dirtyEditChunks = new OrderedSet<string>();
        public static readonly Dictionary<string, HashSet<int>> dirtyEditSections = new Dictionary<string, HashSet<int>>();
        public static void markDirtyEdit(int cx, int cz) { var c = chunkFastGet(cx, cz); if (c != null) dirtyEditChunks.Add(ckey(cx, cz)); }
        public static void markDirtyEditSection(int cx, int cz, int si)
        {
            if (si < 0 || si >= SECTION_COUNT) return;
            var c = chunkFastGet(cx, cz); if (c == null) return;
            var k = ckey(cx, cz); HashSet<int> s;
            if (!dirtyEditSections.TryGetValue(k, out s)) dirtyEditSections[k] = s = new HashSet<int>();
            s.Add(si);
        }
        public static HashSet<int> takeDirtyEditSections(string k)
        {
            HashSet<int> s;
            if (dirtyEditSections.TryGetValue(k, out s)) { dirtyEditSections.Remove(k); return s; }
            return null;
        }
        public static void clearDirtyEdit(string k) { dirtyEditChunks.Remove(k); dirtyEditSections.Remove(k); }
        public static void markDirtySections(int cx, int cz, IEnumerable<int> sis)
        {
            var c = chunkFastGet(cx, cz); if (c == null) return;
            c.dirty = true; c.meshRev++;
            if (c.sectionGeo == null || c.fullMeshDirty) { c.fullMeshDirty = true; return; }
            var ds = c.dirtySections ?? (c.dirtySections = new HashSet<int>());
            foreach (var si in sis) if (si >= 0 && si < SECTION_COUNT) ds.Add(si);
        }
        public static void markDirty(int cx, int cz)
        {
            var c = chunkFastGet(cx, cz);
            if (c != null) { c.dirty = true; c.fullMeshDirty = true; if (c.dirtySections != null) c.dirtySections.Clear(); c.meshRev++; }
        }
    }
}
