// Voxel Forge — Unity port. Item keys, inventory arrays, state save/load, recipes & crafting rules,
// structure loot, chest pairing, furnaces / smelting.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VoxelForge
{
    public sealed class Furnace
    {
        public readonly Stack[] slots = new Stack[3]; // 0 input, 1 fuel, 2 output
        public double progress, burn, burnMax;
        public Stack input { get { return slots[0]; } set { slots[0] = value; } }
        public Stack fuel { get { return slots[1]; } set { slots[1] = value; } }
        public Stack output { get { return slots[2]; } set { slots[2] = value; } }
    }
    public sealed class Recipe
    {
        public string name, outKey; public int outCount; public bool basic;
        public List<KeyValuePair<string, int>> need = new List<KeyValuePair<string, int>>();
    }
    public sealed class CraftRule
    {
        public string[] shape; public Dictionary<char, string> keys = new Dictionary<char, string>(); public bool mirror; public List<string> items; public bool sameWool;
    }
    public sealed class SlotOpts
    {
        public bool single, extractOnly; public string label; public Func<string, bool> accept;
    }

    public static partial class VF
    {
        public static string BK(int id) { return "b:" + id; }
        public static bool isBK(string k) { return k != null && k.StartsWith("b:", StringComparison.Ordinal); }
        public static int bid(string k) { int v; return isBK(k) && int.TryParse(k.Substring(2), out v) ? v : 0; }
        static readonly Dictionary<string, ItemDef> idefCache = new Dictionary<string, ItemDef>();
        public static ItemDef idef(string k)
        {
            if (k == null) return new ItemDef { name = "", stack = 64 };
            ItemDef d;
            if (ITEM_DEFS.TryGetValue(k, out d) && !isVK(k) && !isBK(k)) return d;
            if (idefCache.TryGetValue(k, out d)) return d;
            if (isVK(k)) d = virtualItemDef(k) ?? new ItemDef { name = vkey(k), stack = 64 };
            else if (isBK(k)) { var b = bdef(bid(k)); d = new ItemDef { name = b != null ? b.name : "Блок", stack = 64, block = bid(k), armor = b != null ? b.armor : null }; }
            else d = new ItemDef { name = k, stack = 64 };
            idefCache[k] = d;
            return d;
        }

        public static readonly Stack[] inventory = new Stack[36], armor = new Stack[4], offhand = new Stack[1], craft2 = new Stack[4], craft3 = new Stack[9];
        public static readonly OrderedMap<string, Stack[]> chests = new OrderedMap<string, Stack[]>();
        public static readonly OrderedMap<string, Furnace> furnaces = new OrderedMap<string, Furnace>();
        public static bool inventoryDirty = false, uiOpen = false, loadedGameState = false;
        public static Stack uiCursor = null, pendingSavedDrop = null;
        public static string uiMode = "inventory", uiKey = null;

        public static int stackLightLevel(Stack st)
        {
            if (st == null) return 0;
            if (isVK(st.key)) { var v = VirtualByKey(vkey(st.key)); return Math.Min(15, v != null ? v.light : 0); }
            if (!isBK(st.key)) return 0;
            return bid(st.key) == B.TORCH ? 14 : 0;
        }
        public static int handLightLevelNow() { return modEnabled("torch") ? Math.Max(stackLightLevel(inventory[selected]), stackLightLevel(offhand[0])) : 0; }
        public static double[] handLightUniform()
        {
            int level = handLightLevelNow();
            return level != 0 ? new double[] { player.x, player.y + 1.62, player.z, level } : new double[] { 0, 0, 0, 0 };
        }
        public static Stack normStack(Stack s)
        {
            if (s == null || s.key == null || s.count == 0) return null;
            if (!isVK(s.key) && !isBK(s.key) && !ITEM_DEFS.ContainsKey(s.key)) return null;
            var d = idef(s.key);
            var x = new Stack(s.key, Math.Max(1, Math.Min(d.stack != 0 ? d.stack : 64, s.count)));
            if (d.maxDur != 0) x.dur = Math.Max(1, Math.Min(d.maxDur, s.dur != 0 ? s.dur : d.maxDur));
            return x;
        }
        public static Stack normStack(object o) { return normStack(Stack.FromJson(o)); }
        public static int addItem(string key, int n = 1, int dur = 0, Stack[] arr = null)
        {
            if (arr == null) arr = inventory;
            var d = idef(key); int mx = d.stack != 0 ? d.stack : 64, left = n;
            if (left <= 0) return 0;
            if (mx > 1)
                foreach (var s in arr)
                    if (s != null && s.key == key && s.count < mx)
                    {
                        int q = Math.Min(left, mx - s.count);
                        s.count += q; left -= q;
                        if (left == 0) break;
                    }
            for (int i = 0; i < arr.Length && left > 0; i++)
                if (arr[i] == null)
                {
                    int q = Math.Min(left, mx);
                    arr[i] = new Stack(key, q);
                    if (d.maxDur != 0) arr[i].dur = dur != 0 ? dur : d.maxDur;
                    left -= q;
                }
            if (left < n) inventoryDirty = true;
            return left;
        }
        static string[] NATURAL_WOOL_KEYS() { return new[] { BK(B.WOOL_WHITE), BK(B.WOOL_BLACK), BK(B.WOOL_GRAY), BK(B.WOOL_LIGHT_GRAY), BK(B.WOOL_BROWN), BK(B.WOOL_PINK) }; }
        public static bool isWoolInventoryKey(string k) { return k == "wool" || NATURAL_WOOL_KEYS().Contains(k) || (isVK(k) && vkey(k).StartsWith("WOOL_", StringComparison.Ordinal)); }
        static readonly string[] BLOCK_SYMBOL_BY_ID = new string[256];
        static void InitBlockSymbols() { foreach (var n in B.ORDER) { int id = B.NAMES[n]; if (BLOCK_SYMBOL_BY_ID[id] == null) BLOCK_SYMBOL_BY_ID[id] = n; } }
        public static string inventoryBlockSymbol(string k) { return isVK(k) ? vkey(k) : isBK(k) ? (BLOCK_SYMBOL_BY_ID[bid(k) & 255] ?? "") : ""; }
        static readonly Regex RX_PLANKS = new Regex("(^|_)PLANKS$"), RX_OAKFAM = new Regex("(^|_)OAK_|^(STRIPPED_)?LOG$|^LOG$");
        public static bool isPlankInventoryKey(string k) { return RX_PLANKS.IsMatch(inventoryBlockSymbol(k)); }
        public static string woodFamilyForInventoryKey(string k)
        {
            var n = inventoryBlockSymbol(k);
            if (n.Length == 0) return "";
            if (n.Contains("BIRCH")) return "birch";
            if (n.Contains("SPRUCE")) return "spruce";
            if (n.Contains("DARK")) return "dark";
            if (n.Contains("JUNGLE")) return "jungle";
            if (n.Contains("ACACIA")) return "acacia";
            if (n.Contains("CRIMSON")) return "crimson";
            if (n.Contains("WARPED")) return "warped";
            if (n.Contains("BAMBOO_BLOCK")) return "bamboo";
            if (RX_OAKFAM.IsMatch(n)) return "oak";
            return "";
        }
        public static bool ingredientMatches(string spec, string key)
        {
            if (spec == key) return true;
            if (spec == "wool" || spec == "@wool") return isWoolInventoryKey(key);
            if (spec == "@log") return (isBK(key) && isTreeLog(bid(key))) || isVirtualLogKey(key);
            if (spec == "@planks") return isPlankInventoryKey(key);
            if (spec == "@stonecraft") { var n = inventoryBlockSymbol(key); return n == "COBBLE" || n == "COBBLED_DEEPSLATE" || n == "BLACKSTONE"; }
            if (spec.StartsWith("@wood:", StringComparison.Ordinal)) return woodFamilyForInventoryKey(key) == spec.Substring(6);
            if (spec == "@red_dye_source") return key == BK(B.POPPY) || key == BK(B.RED_TULIP);
            if (spec == "@light_gray_dye_source") return key == BK(B.AZURE_BLUET) || key == BK(B.WHITE_TULIP) || key == BK(B.OXEYE_DAISY);
            if (spec == "@white_dye_source") return key == "bone_meal" || key == BK(B.LILY_OF_THE_VALLEY);
            return false;
        }
        public static int countItem(string key, Stack[] arr = null)
        {
            if (arr == null) arr = inventory;
            int n = 0;
            foreach (var s in arr) if (s != null && ingredientMatches(key, s.key)) n += s.count;
            return n;
        }
        public static bool removeItem(string key, int n = 1, Stack[] arr = null)
        {
            if (arr == null) arr = inventory;
            int need = n;
            for (int i = arr.Length - 1; i >= 0 && need > 0; i--)
            {
                var s = arr[i];
                if (s != null && ingredientMatches(key, s.key))
                {
                    int q = Math.Min(need, s.count);
                    s.count -= q; need -= q;
                    if (s.count <= 0) arr[i] = null;
                }
            }
            if (need < n) inventoryDirty = true;
            return need == 0;
        }
        public static Stack selectedStack() { return inventory[selected]; }
        public static ItemDef selectedDef() { var s = selectedStack(); return s != null ? idef(s.key) : null; }
        public static void damageHeldTool(int n = 1)
        {
            if (player.creative) return;
            var s = selectedStack();
            if (s == null) return;
            var d = idef(s.key);
            if (d.maxDur == 0) return;
            s.dur = (s.dur != 0 ? s.dur : d.maxDur) - n;
            if (s.dur <= 0) { toast("Инструмент сломался"); inventory[selected] = null; }
            else if (s.dur <= Math.Max(5, d.maxDur * 0.08)) toast(d.name + ": прочность " + s.dur);
            inventoryDirty = true;
            saveGameSoon();
            drawHotbar();
        }
        public static Stack[] cloneInventory(Stack[] a = null) { if (a == null) a = inventory; return a.Select(s => s != null ? s.Clone() : null).ToArray(); }
        public static bool canFitItem(string key, int n, Stack[] arr = null)
        {
            if (arr == null) arr = inventory;
            var d = idef(key); int mx = d.stack != 0 ? d.stack : 64, cap = 0;
            foreach (var s in arr)
            {
                if (s == null) cap += mx;
                else if (mx > 1 && s.key == key) cap += mx - s.count;
                if (cap >= n) return true;
            }
            return cap >= n;
        }
        static List<object> stacksJson(Stack[] a) { return a.Select(s => s != null ? (object)s.ToJson() : null).ToList(); }

        public static void saveStateNow()
        {
            if (SAVE_KEYS.state == null) return;
            try
            {
                var o = saveHeader();
                o["mode"] = GAME_MODE;
                o["worldSeed"] = (double)WORLD_SEED;
                o["inventory"] = stacksJson(inventory);
                var ch = new JObj(); foreach (var kv in chests) ch[kv.Key] = stacksJson(kv.Value); o["chests"] = ch;
                var fu = new JObj();
                foreach (var kv in furnaces)
                {
                    var f = kv.Value;
                    fu[kv.Key] = new JObj { { "input", f.input != null ? f.input.ToJson() : null }, { "fuel", f.fuel != null ? f.fuel.ToJson() : null }, { "output", f.output != null ? f.output.ToJson() : null },
                        { "progress", f.progress }, { "burn", f.burn }, { "burnMax", f.burnMax } };
                }
                o["furnaces"] = fu;
                o["worldDrops"] = worldDrops.Select(q =>
                {
                    var j = new JObj { { "key", q.key }, { "count", (double)q.count }, { "x", q.x }, { "y", q.y }, { "z", q.z }, { "vx", q.vx }, { "vy", q.vy }, { "vz", q.vz }, { "age", q.age }, { "pickAfter", q.pickAfter } };
                    if (q.dur != 0) j["dur"] = (double)q.dur;
                    return (object)j;
                }).ToList();
                o["mobs"] = serializeLiveMobs();
                o["storedMobs"] = serializeStoredMobs();
                o["seededMobChunks"] = seededMobChunks.Select(x => (object)x).ToList();
                o["vehicles"] = serializeVehicles();
                o["ridingVehicle"] = (double)(player.riding != null ? vehicles.IndexOf(player.riding) : -1);
                o["cursor"] = uiCursor != null ? uiCursor.ToJson() : null;
                o["armor"] = stacksJson(armor);
                o["offhand"] = offhand[0] != null ? offhand[0].ToJson() : null;
                o["player"] = new JObj {
                    { "x", player.x }, { "y", player.y }, { "z", player.z }, { "hp", player.hp }, { "hunger", player.hunger }, { "air", player.air }, { "exh", player.exh },
                    { "spawn", player.spawn != null ? new List<object> { player.spawn[0], player.spawn[1], player.spawn[2] } : null }, { "flying", player.flying },
                    { "h", player.h != 0 ? player.h : 1.8 }, { "dead", player.dead }, { "deathReason", player.deathReason ?? "" } };
                o["cropSim"] = worldSim.crops.Select(kv => (object)new List<object> { kv.Key, Math.Max(0, kv.Value.t) }).ToList();
                o["saplingSim"] = worldSim.saplings.Select(kv => (object)new List<object> { kv.Key, Math.Max(0, kv.Value.t), Math.Max(1, kv.Value.delay != 0 ? kv.Value.delay : 90) }).ToList();
                o["day"] = day;
                LocalStorage.setItem(SAVE_KEYS.state, Json.Stringify(o));
                touchWorldRecord();
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("saveStateNow: " + e.Message); }
            inventoryDirty = false;
        }
        public static void saveGameNow() { saveStateNow(); saveWorldNow(); saveKilledMobs(); }
        static int saveGameT = 0;
        public static void saveGameSoon()
        {
            if (SAVE_KEYS.state == null) return;
            Timers.clearTimeout(saveGameT);
            saveGameT = Timers.setTimeout(saveStateNow, 300);
        }
        public static void resetContainers() { chests.Clear(); furnaces.Clear(); }
        static int[] CREATIVE_DEFAULT { get { return new[] { B.GRASS, B.STONE, B.PLANKS, B.GLASS, B.BRICKS, B.TNT, B.TORCH, B.LADDER, B.CHEST }; } }
        public static void loadGame()
        {
            Array.Clear(inventory, 0, inventory.Length); Array.Clear(armor, 0, 4); offhand[0] = null; Array.Clear(craft2, 0, 4); Array.Clear(craft3, 0, 9);
            resetContainers();
            worldDrops.Clear();
            storedMobs.Clear(); seededMobChunks.Clear();
            clearVehicles();
            resetActiveItems();
            uiCursor = null; pendingSavedDrop = null; loadedGameState = false;
            player.x = SPAWN[0]; player.y = SPAWN[1]; player.z = SPAWN[2]; player.hp = 20; player.hunger = 20; player.air = 10; player.dead = false; player.deathReason = "";
            player.spawn = (double[])SPAWN.Clone(); player.fallDist = 0; player.invuln = 0; player.regenT = 0; player.starveT = 0; player.hungerT = 0; player.drownT = 0; player.fireT = 0;
            player.exh = 0; player.sprinting = false; player.headInWater = false; player.inLava = false; player.voidT = 0; player.fireDamageT = 0; player.cactusT = 0; player.lastSurvY = null;
            player.flying = false; player.h = 1.8; player.riding = null; player.sleeping = false;
            JObj r = null;
            try { r = Json.TryParse(LocalStorage.getItem(SAVE_KEYS.state) ?? "null") as JObj; } catch { }
            if (saveHeaderValid(r) && r.Str("mode") == GAME_MODE && Json.IsFinite(r.Get("worldSeed")) && JS.toInt32(r.Num("worldSeed")) == WORLD_SEED)
            {
                var inv = r.Arr("inventory");
                if (inv != null) for (int i = 0; i < 36; i++) inventory[i] = i < inv.Count ? normStack(inv[i]) : null;
                var ar = r.Arr("armor");
                if (ar != null) for (int i = 0; i < 4; i++) { var q = i < ar.Count ? normStack(ar[i]) : null; var a = q != null ? idef(q.key).armor : null; armor[i] = q != null && a != null && a.slot == i ? q : null; }
                offhand[0] = normStack(r.Get("offhand"));
                var ch = r.Obj("chests");
                if (ch != null) foreach (var kv in ch) { var a = kv.Value as List<object>; if (a != null) { var s = new Stack[27]; for (int i = 0; i < 27; i++) s[i] = i < a.Count ? normStack(a[i]) : null; chests.Set(kv.Key, s); } }
                var fu = r.Obj("furnaces");
                if (fu != null)
                    foreach (var kv in fu)
                    {
                        var f = kv.Value as JObj; if (f == null) continue;
                        var nf = new Furnace();
                        nf.input = normStack(f.Get("input")); nf.fuel = normStack(f.Get("fuel")); nf.output = normStack(f.Get("output"));
                        nf.progress = Math.Max(0, Math.Min(1, f.Num("progress")));
                        nf.burn = Math.Max(0, f.Num("burn"));
                        nf.burnMax = Math.Max(0, f.Num("burnMax") != 0 ? f.Num("burnMax") : f.Num("burn"));
                        furnaces.Set(kv.Key, nf);
                    }
                var wd = r.Arr("worldDrops");
                if (wd != null)
                    foreach (var qo in wd)
                    {
                        var q = qo as JObj; var st = normStack(qo);
                        if (st == null || q == null || !JS.isFinite(q.Num("x", double.NaN)) || !JS.isFinite(q.Num("y", double.NaN)) || !JS.isFinite(q.Num("z", double.NaN))) continue;
                        worldDrops.Add(new WorldDrop
                        {
                            key = st.key, count = st.count, dur = st.dur, x = q.Num("x"), y = q.Num("y"), z = q.Num("z"),
                            vx = JS.isFinite(q.Num("vx", double.NaN)) ? q.Num("vx") : 0, vy = JS.isFinite(q.Num("vy", double.NaN)) ? q.Num("vy") : 0, vz = JS.isFinite(q.Num("vz", double.NaN)) ? q.Num("vz") : 0,
                            age = Math.Max(0, Math.Min(300, q.Num("age"))), pickAfter = JS.isFinite(q.Num("pickAfter", double.NaN)) ? Math.Max(0, q.Num("pickAfter")) : 0.5,
                        });
                    }
                var rp = r.Obj("player");
                if (rp != null)
                {
                    if (rp.Has("x")) player.x = rp.Num("x"); if (rp.Has("y")) player.y = rp.Num("y"); if (rp.Has("z")) player.z = rp.Num("z");
                    if (rp.Has("hp")) player.hp = rp.Num("hp"); if (rp.Has("hunger")) player.hunger = rp.Num("hunger"); if (rp.Has("air")) player.air = rp.Num("air");
                    if (rp.Has("exh")) player.exh = rp.Num("exh"); if (rp.Has("flying")) player.flying = rp.Bool("flying");
                    if (rp.Has("dead")) player.dead = rp.Bool("dead"); if (rp.Has("deathReason")) player.deathReason = rp.Str("deathReason") ?? "";
                    double hh = rp.Num("h"); if (hh == 0) hh = 1.8;
                    player.h = Math.Abs(hh - 0.85) < 0.2 ? 0.85 : 1.8;
                    player.riding = null;
                    var sp = rp.Arr("spawn");
                    player.spawn = sp != null && sp.Count >= 3 ? new[] { Json.ToNum(sp[0]), Json.ToNum(sp[1]), Json.ToNum(sp[2]) } : (double[])SPAWN.Clone();
                }
                var cs = r.Arr("cropSim");
                if (cs != null) foreach (var qo in cs) { var q = qo as List<object>; if (q != null && q.Count >= 2 && q[0] is string && Json.IsFinite(q[1])) worldSim.crops.Set((string)q[0], new TimerState { t = Math.Max(0, Json.ToNum(q[1])) }); }
                var ss = r.Arr("saplingSim");
                if (ss != null) foreach (var qo in ss) { var q = qo as List<object>; if (q != null && q.Count >= 2 && q[0] is string && Json.IsFinite(q[1])) worldSim.saplings.Set((string)q[0], new TimerState { t = Math.Max(0, Json.ToNum(q[1])), delay = q.Count > 2 && Json.IsFinite(q[2]) ? Math.Max(1, Json.ToNum(q[2])) : 90 + JS.random() * 90 }); }
                if (r.Get("day") is double) { double dd = r.Num("day"); day = ((dd % 1) + 1) % 1; }
                loadMobPersistence(r.Arr("mobs"), r.Get("storedMobs"), r.Arr("seededMobChunks"));
                restoreVehicles(r.Arr("vehicles"));
                var rv = r.Get("ridingVehicle");
                if (rv is double && (double)rv == Math.Floor((double)rv) && (double)rv >= 0 && (double)rv < vehicles.Count)
                {
                    player.riding = vehicles[(int)(double)rv];
                    player.x = player.riding.x; player.z = player.riding.z;
                    player.y = player.riding.y + (player.riding.kind == "minecart" ? -0.15 : -0.45);
                }
                var cur = normStack(r.Get("cursor"));
                if (cur != null) { int left = addItem(cur.key, cur.count, cur.dur); if (left != 0) pendingSavedDrop = new Stack(cur.key, left, cur.dur); }
                loadedGameState = true; inventoryDirty = false;
                return;
            }
            if (player.creative) { var cd = CREATIVE_DEFAULT; for (int i = 0; i < cd.Length; i++) inventory[i] = new Stack(BK(cd[i]), 64); }
            else { addItem("wooden_pickaxe", 1); addItem("wooden_axe", 1); addItem("wooden_shovel", 1); addItem("wooden_sword", 1); addItem("wooden_hoe", 1); addItem("wheat_seeds", 4); }
            inventoryDirty = false;
        }
        static double periodicSaveAt = 0;
        /// <summary>setInterval(saveStateNow, 10000) equivalent (called each frame).</summary>
        public static void tickPeriodicSave()
        {
            double now = JS.now();
            if (now - periodicSaveAt < 10000) return;
            periodicSaveAt = now;
            if (started && worldReady && !pauseOpen) saveStateNow();
        }
        public static string resourceHUDText = "";
        public static void updateResourceHUD()
        {
            var s = selectedStack(); var lines = new List<string> { player.creative ? "CREATOR" : "SURVIVAL" };
            if (s != null) lines.Add("В руке: " + idef(s.key).name + (player.creative ? " · ∞" : " ×" + s.count));
            if (player.creative) { lines.Add("Бесконечные блоки"); lines.Add("Мгновенная добыча · F полёт"); }
            else
            {
                lines.Add("HP " + Math.Ceiling(player.hp) + "/20 · Еда " + Math.Ceiling(player.hunger) + "/20 · Броня " + armorPoints());
                if (offhand[0] != null) lines.Add("Левая рука: " + idef(offhand[0].key).name);
                int c = 0;
                foreach (var st in inventory) if (st != null && c < 5 && !isBK(st.key)) { lines.Add(idef(st.key).name + " ×" + st.count); c++; }
            }
            resourceHUDText = string.Join("\n", lines);
        }

        // ---------------- recipes ----------------
        public static readonly List<Recipe> RECIPES = new List<Recipe>();
        public static readonly Dictionary<string, CraftRule> CRAFT_RULES = new Dictionary<string, CraftRule>();
        static Recipe R(string name, string outKey, int count, params object[] need)
        {
            var r = new Recipe { name = name, outKey = outKey, outCount = count };
            for (int i = 0; i + 1 < need.Length; i += 2) r.need.Add(new KeyValuePair<string, int>((string)need[i], Convert.ToInt32(need[i + 1])));
            return r;
        }
        static Dictionary<char, string> K(params string[] kv) { var d = new Dictionary<char, string>(); for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i][0]] = kv[i + 1]; return d; }
        static void craftRule(string outKey, string[] shape, Dictionary<char, string> keys = null, bool mirror = false, List<string> items = null, bool sameWool = false)
        { CRAFT_RULES[outKey] = new CraftRule { shape = shape, keys = keys ?? new Dictionary<char, string>(), mirror = mirror, items = items, sameWool = sameWool }; }
        static void replaceRecipe(string outKey, string name, int count, List<KeyValuePair<string, int>> need, CraftRule rule)
        {
            for (int i = RECIPES.Count - 1; i >= 0; i--) if (RECIPES[i].outKey == outKey) RECIPES.RemoveAt(i);
            RECIPES.Add(new Recipe { name = name, outKey = outKey, outCount = count, need = need });
            if (rule != null) CRAFT_RULES[outKey] = rule;
        }
        static List<KeyValuePair<string, int>> N(params object[] need) { var l = new List<KeyValuePair<string, int>>(); for (int i = 0; i + 1 < need.Length; i += 2) l.Add(new KeyValuePair<string, int>((string)need[i], Convert.ToInt32(need[i + 1]))); return l; }
        static void shaped(string outKey, string name, int count, string[] shape, Dictionary<char, string> keys, List<KeyValuePair<string, int>> need, bool mirror = false)
        { replaceRecipe(outKey, name, count, need, new CraftRule { shape = shape, keys = keys, mirror = mirror, items = null, sameWool = false }); }
        static void shapeless(string outKey, string name, int count, List<string> items, List<KeyValuePair<string, int>> need)
        { replaceRecipe(outKey, name, count, need, new CraftRule { shape = null, keys = new Dictionary<char, string>(), mirror = false, items = items, sameWool = false }); }
        static string mb(string k) { return mainBlockItemKey(k); }
        static List<KeyValuePair<string, int>> needFromShape(string[] shape, Dictionary<char, string> keys)
        {
            var need = new List<KeyValuePair<string, int>>();
            foreach (var row in shape)
                foreach (var ch in row)
                    if (ch != '.')
                    {
                        var spec = keys[ch]; int idx = need.FindIndex(p => p.Key == spec);
                        if (idx >= 0) need[idx] = new KeyValuePair<string, int>(spec, need[idx].Value + 1); else need.Add(new KeyValuePair<string, int>(spec, 1));
                    }
            return need;
        }

        static void InitRecipes()
        {
            RECIPES.Clear(); CRAFT_RULES.Clear();
            string PL = BK(B.PLANKS), CO = BK(B.COBBLE);
            RECIPES.AddRange(new[] {
                R("Доски ×4", PL, 4, "@log", 1), R("Палки ×4", "stick", 4, PL, 2), R("Верстак", BK(B.CRAFT), 1, PL, 4),
                R("Деревянная кирка", "wooden_pickaxe", 1, PL, 3, "stick", 2), R("Деревянный топор", "wooden_axe", 1, PL, 3, "stick", 2),
                R("Деревянная лопата", "wooden_shovel", 1, PL, 1, "stick", 2), R("Деревянный меч", "wooden_sword", 1, PL, 2, "stick", 1),
                R("Деревянная мотыга", "wooden_hoe", 1, PL, 2, "stick", 2), R("Каменная кирка", "stone_pickaxe", 1, CO, 3, "stick", 2),
                R("Каменный топор", "stone_axe", 1, CO, 3, "stick", 2), R("Каменная лопата", "stone_shovel", 1, CO, 1, "stick", 2),
                R("Каменный меч", "stone_sword", 1, CO, 2, "stick", 1), R("Каменная мотыга", "stone_hoe", 1, CO, 2, "stick", 2),
                R("Железная кирка", "iron_pickaxe", 1, "iron_ingot", 3, "stick", 2), R("Железный топор", "iron_axe", 1, "iron_ingot", 3, "stick", 2),
                R("Железная лопата", "iron_shovel", 1, "iron_ingot", 1, "stick", 2), R("Железный меч", "iron_sword", 1, "iron_ingot", 2, "stick", 1),
                R("Железная мотыга", "iron_hoe", 1, "iron_ingot", 2, "stick", 2), R("Алмазная кирка", "diamond_pickaxe", 1, "diamond", 3, "stick", 2),
                R("Алмазный топор", "diamond_axe", 1, "diamond", 3, "stick", 2), R("Алмазная лопата", "diamond_shovel", 1, "diamond", 1, "stick", 2),
                R("Алмазный меч", "diamond_sword", 1, "diamond", 2, "stick", 1), R("Алмазная мотыга", "diamond_hoe", 1, "diamond", 2, "stick", 2),
                R("Печь", BK(B.FURNACE), 1, CO, 8), R("Сундук", BK(B.CHEST), 1, PL, 8), R("Кровать", BK(B.BED), 1, "wool", 3, PL, 3),
                R("Факелы ×4", BK(B.TORCH), 4, "coal", 1, "stick", 1), R("Лестницы ×3", BK(B.LADDER), 3, "stick", 7), R("Двери ×3", BK(B.DOOR), 3, PL, 6),
                R("TNT", BK(B.TNT), 1, BK(B.SAND), 4, "gunpowder", 5), R("Хлеб", "bread", 1, "wheat", 3), R("Ножницы", "shears", 1, "iron_ingot", 2),
                R("Кожаный шлем", "leather_helmet", 1, "leather", 5), R("Кожаная куртка", "leather_chestplate", 1, "leather", 8),
                R("Кожаные штаны", "leather_leggings", 1, "leather", 7), R("Кожаные ботинки", "leather_boots", 1, "leather", 4),
                R("Железный шлем", "iron_helmet", 1, "iron_ingot", 5), R("Железный нагрудник", "iron_chestplate", 1, "iron_ingot", 8),
                R("Железные поножи", "iron_leggings", 1, "iron_ingot", 7), R("Железные ботинки", "iron_boots", 1, "iron_ingot", 4),
                R("Золотой шлем", "golden_helmet", 1, "gold_ingot", 5), R("Золотой нагрудник", "golden_chestplate", 1, "gold_ingot", 8),
                R("Золотые поножи", "golden_leggings", 1, "gold_ingot", 7), R("Золотые ботинки", "golden_boots", 1, "gold_ingot", 4),
                R("Алмазный шлем", "diamond_helmet", 1, "diamond", 5), R("Алмазный нагрудник", "diamond_chestplate", 1, "diamond", 8),
                R("Алмазные поножи", "diamond_leggings", 1, "diamond", 7), R("Алмазные ботинки", "diamond_boots", 1, "diamond", 4),
                R("Огниво", "flint_and_steel", 1, "flint", 1, "iron_ingot", 1), R("Угольный блок", BK(B.COAL_BLOCK), 1, "coal", 9),
                R("Рельсы ×16", BK(B.RAIL), 16, "iron_ingot", 6, "stick", 1), R("Вагонетка", "minecart", 1, "iron_ingot", 5), R("Лодка", "boat", 1, PL, 5),
                R("Удочка", "fishing_rod", 1, "stick", 3, "string", 2), R("Лук", "bow", 1, "stick", 3, "feather", 3), R("Стрелы ×4", "arrow", 4, "flint", 1, "stick", 1, "feather", 1),
                R("Костная мука ×3", "bone_meal", 3, "bone", 1), R("Семена тыквы ×4", "pumpkin_seeds", 4, BK(B.PUMPKIN), 1), R("Семена арбуза", "melon_seeds", 1, "melon_slice", 1),
                R("Золотое яблоко", "golden_apple", 1, "gold_ingot", 8, "apple", 1),
            });
            foreach (var i in new[] { 0, 1, 2 }) RECIPES[i].basic = true;

            craftRule(PL, null, null, false, new List<string> { "@log" });
            craftRule("stick", new[] { "P", "P" }, K("P", PL));
            craftRule(BK(B.CRAFT), new[] { "PP", "PP" }, K("P", PL));
            craftRule(BK(B.FURNACE), new[] { "CCC", "C.C", "CCC" }, K("C", CO));
            craftRule(BK(B.CHEST), new[] { "PPP", "P.P", "PPP" }, K("P", PL));
            craftRule(BK(B.BED), new[] { "WWW", "PPP" }, K("W", "@wool", "P", PL), false, null, true);
            craftRule(BK(B.TORCH), new[] { "C", "S" }, K("C", "coal", "S", "stick"));
            craftRule(BK(B.LADDER), new[] { "S.S", "SSS", "S.S" }, K("S", "stick"));
            craftRule(BK(B.DOOR), new[] { "MM", "MM", "MM" }, K("M", PL));
            craftRule(BK(B.TNT), new[] { "GSG", "SGS", "GSG" }, K("G", "gunpowder", "S", BK(B.SAND)));
            craftRule("bread", new[] { "WWW" }, K("W", "wheat"));
            craftRule("shears", new[] { ".M", "M." }, K("M", "iron_ingot"), true);
            craftRule("flint_and_steel", null, null, false, new List<string> { "flint", "iron_ingot" });
            craftRule(BK(B.COAL_BLOCK), new[] { "CCC", "CCC", "CCC" }, K("C", "coal"));
            craftRule(BK(B.RAIL), new[] { "M.M", "MSM", "M.M" }, K("M", "iron_ingot", "S", "stick"));
            craftRule("minecart", new[] { "M.M", "MMM" }, K("M", "iron_ingot"));
            craftRule("boat", new[] { "P.P", "PPP" }, K("P", PL));
            craftRule("fishing_rod", new[] { "..S", ".ST", "S.T" }, K("S", "stick", "T", "string"), true);
            craftRule("bow", new[] { ".SF", "S.F", ".SF" }, K("S", "stick", "F", "feather"), true);
            craftRule("arrow", new[] { "F", "S", "E" }, K("F", "flint", "S", "stick", "E", "feather"));
            craftRule("bone_meal", null, null, false, new List<string> { "bone" });
            craftRule("pumpkin_seeds", null, null, false, new List<string> { BK(B.PUMPKIN) });
            craftRule("melon_seeds", null, null, false, new List<string> { "melon_slice" });
            craftRule("golden_apple", new[] { "GGG", "GAG", "GGG" }, K("G", "gold_ingot", "A", "apple"));
            craftRule("golden_carrot", new[] { "GGG", "GCG", "GGG" }, K("G", "gold_ingot", "C", "carrot"));
            foreach (var mk in new[] { new[] { PL, "wooden" }, new[] { CO, "stone" }, new[] { "iron_ingot", "iron" }, new[] { "diamond", "diamond" } })
            {
                string mat = mk[0], key = mk[1];
                craftRule(key + "_pickaxe", new[] { "MMM", ".S.", ".S." }, K("M", mat, "S", "stick"));
                craftRule(key + "_axe", new[] { "MM", "MS", ".S" }, K("M", mat, "S", "stick"), true);
                craftRule(key + "_shovel", new[] { "M", "S", "S" }, K("M", mat, "S", "stick"));
                craftRule(key + "_sword", new[] { "M", "M", "S" }, K("M", mat, "S", "stick"));
                craftRule(key + "_hoe", new[] { "MM", ".S", ".S" }, K("M", mat, "S", "stick"), true);
            }
            foreach (var mk in new[] { new[] { "leather", "leather" }, new[] { "iron_ingot", "iron" }, new[] { "gold_ingot", "golden" }, new[] { "diamond", "diamond" } })
            {
                string mat = mk[0], key = mk[1];
                craftRule(key + "_helmet", new[] { "MMM", "M.M" }, K("M", mat));
                craftRule(key + "_chestplate", new[] { "M.M", "MMM", "MMM" }, K("M", mat));
                craftRule(key + "_leggings", new[] { "MMM", "M.M", "M.M" }, K("M", mat));
                craftRule(key + "_boots", new[] { "M.M", "M.M" }, K("M", mat));
            }
            // v0.92.62 content recipes (ship deliberately excluded).
            {
                string mat = "gold_ingot", key = "golden";
                RECIPES.Add(R("Золотая кирка", key + "_pickaxe", 1, mat, 3, "stick", 2));
                RECIPES.Add(R("Золотой топор", key + "_axe", 1, mat, 3, "stick", 2));
                RECIPES.Add(R("Золотая лопата", key + "_shovel", 1, mat, 1, "stick", 2));
                RECIPES.Add(R("Золотой меч", key + "_sword", 1, mat, 2, "stick", 1));
                RECIPES.Add(R("Золотая мотыга", key + "_hoe", 1, mat, 2, "stick", 2));
                craftRule(key + "_pickaxe", new[] { "MMM", ".S.", ".S." }, K("M", mat, "S", "stick"));
                craftRule(key + "_axe", new[] { "MM", "MS", ".S" }, K("M", mat, "S", "stick"), true);
                craftRule(key + "_shovel", new[] { "M", "S", "S" }, K("M", mat, "S", "stick"));
                craftRule(key + "_sword", new[] { "M", "M", "S" }, K("M", mat, "S", "stick"));
                craftRule(key + "_hoe", new[] { "MM", ".S", ".S" }, K("M", mat, "S", "stick"), true);
            }
            RECIPES.Add(R("Сахар", "sugar", 1, BK(B.SUGAR_CANE), 1));
            RECIPES.Add(R("Миска ×4", "bowl", 4, PL, 3));
            RECIPES.Add(R("Грибной суп", "mushroom_stew", 1, "bowl", 1, BK(B.RED_MUSHROOM), 1, BK(B.BROWN_MUSHROOM), 1));
            RECIPES.Add(R("Тыквенный пирог", "pumpkin_pie", 1, BK(B.PUMPKIN), 1, "sugar", 1, "egg", 1));
            craftRule("sugar", null, null, false, new List<string> { BK(B.SUGAR_CANE) });
            craftRule("bowl", new[] { "P.P", ".P." }, K("P", PL));
            craftRule("mushroom_stew", null, null, false, new List<string> { "bowl", BK(B.RED_MUSHROOM), BK(B.BROWN_MUSHROOM) });
            craftRule("pumpkin_pie", null, null, false, new List<string> { BK(B.PUMPKIN), "sugar", "egg" });

            // v0.92.68: recipe parity with main (ship recipe deliberately excluded).
            var woodRecipes = new[] { new[] { "oak", PL }, new[] { "spruce", BK(B.SPRUCE_PLANKS) }, new[] { "birch", BK(B.BIRCH_PLANKS) }, new[] { "dark", BK(B.DARK_PLANKS) },
                new[] { "jungle", mb("JUNGLE_PLANKS") }, new[] { "acacia", mb("ACACIA_PLANKS") }, new[] { "crimson", mb("CRIMSON_PLANKS") }, new[] { "warped", mb("WARPED_PLANKS") } };
            foreach (var w in woodRecipes) if (w[1] != null) shapeless(w[1], idef(w[1]).name + " ×4", 4, new List<string> { "@wood:" + w[0] }, N("@wood:" + w[0], 1));
            shaped("stick", "Палки ×4", 4, new[] { "P", "P" }, K("P", "@planks"), N("@planks", 2));
            shaped(BK(B.CRAFT), "Верстак", 1, new[] { "PP", "PP" }, K("P", "@planks"), N("@planks", 4));
            shaped(BK(B.FURNACE), "Печь", 1, new[] { "CCC", "C.C", "CCC" }, K("C", "@stonecraft"), N("@stonecraft", 8));
            shaped(BK(B.CHEST), "Сундук", 1, new[] { "PPP", "P.P", "PPP" }, K("P", "@planks"), N("@planks", 8));
            shaped("boat", "Лодка", 1, new[] { "P.P", "PPP" }, K("P", "@planks"), N("@planks", 5));
            shaped("bowl", "Миска ×4", 4, new[] { "P.P", ".P." }, K("P", "@planks"), N("@planks", 3));
            shaped(BK(B.BOOKSHELF), "Книжная полка", 1, new[] { "PPP", "PPP", "PPP" }, K("P", "@planks"), N("@planks", 9));
            shaped("bucket", "Ведро", 1, new[] { "M.M", ".M." }, K("M", "iron_ingot"), N("iron_ingot", 3));
            shapeless(mb("BONE_BLOCK"), "Костяной блок", 1, new List<string> { "bone", "bone", "bone" }, N("bone", 3));
            shaped(BK(B.MELON), "Арбуз", 1, new[] { "MMM", "MMM", "MMM" }, K("M", "melon_slice"), N("melon_slice", 9));
            shapeless(mb("JACK_O_LANTERN"), "Светильник Джека", 1, new List<string> { BK(B.CARVED_PUMPKIN), BK(B.TORCH) }, N(BK(B.CARVED_PUMPKIN), 1, BK(B.TORCH), 1));
            shaped(BK(B.SANDSTONE), "Песчаник", 1, new[] { "MM", "MM" }, K("M", BK(B.SAND)), N(BK(B.SAND), 4));
            shaped(mb("RED_SANDSTONE"), "Красный песчаник", 1, new[] { "MM", "MM" }, K("M", BK(B.RED_SAND)), N(BK(B.RED_SAND), 4));
            shaped(BK(B.CUT_SANDSTONE), "Резной песчаник ×4", 4, new[] { "MM", "MM" }, K("M", BK(B.SANDSTONE)), N(BK(B.SANDSTONE), 4));
            shaped(mb("CUT_RED_SANDSTONE"), "Резной красный песчаник ×4", 4, new[] { "MM", "MM" }, K("M", mb("RED_SANDSTONE")), N(mb("RED_SANDSTONE"), 4));
            shaped(BK(B.CHISELED_SANDSTONE), "Резной песчаник", 1, new[] { "M", "M" }, K("M", BK(B.SANDSTONE)), N(BK(B.SANDSTONE), 2));
            shaped(mb("CHISELED_RED_SANDSTONE"), "Резной красный песчаник", 1, new[] { "M", "M" }, K("M", mb("RED_SANDSTONE")), N(mb("RED_SANDSTONE"), 2));
            shaped(mb("HAY_BLOCK"), "Сноп сена", 1, new[] { "MMM", "MMM", "MMM" }, K("M", "wheat"), N("wheat", 9));
            shapeless("wheat", "Пшеница ×9", 9, new List<string> { mb("HAY_BLOCK") }, N(mb("HAY_BLOCK"), 1));
            shaped(BK(B.CLAY), "Глиняный блок", 1, new[] { "CC", "CC" }, K("C", "clay_ball"), N("clay_ball", 4));
            shaped(BK(B.BRICKS), "Кирпичный блок", 1, new[] { "BB", "BB" }, K("B", "brick"), N("brick", 4));
            shaped(BK(B.FLOWER_POT), "Цветочный горшок", 1, new[] { "B.B", ".B." }, K("B", "brick"), N("brick", 3));
            shaped(mb("COPPER_BLOCK"), "Медный блок", 1, new[] { "XXX", "XXX", "XXX" }, K("X", "copper_ingot"), N("copper_ingot", 9));
            shapeless("copper_ingot", "Медный слиток ×9", 9, new List<string> { mb("COPPER_BLOCK") }, N(mb("COPPER_BLOCK"), 1));
            shaped(mb("CUT_COPPER"), "Резная медь ×4", 4, new[] { "MM", "MM" }, K("M", mb("COPPER_BLOCK")), N(mb("COPPER_BLOCK"), 4));
            foreach (var ib in new[] { new[] { "coal", BK(B.COAL_BLOCK) }, new[] { "iron_ingot", mb("IRON_BLOCK") }, new[] { "gold_ingot", BK(B.GOLD_BLOCK) }, new[] { "diamond", mb("DIAMOND_BLOCK") }, new[] { "emerald", mb("EMERALD_BLOCK") } })
                if (ib[1] != null)
                {
                    shaped(ib[1], idef(ib[1]).name, 1, new[] { "XXX", "XXX", "XXX" }, K("X", ib[0]), N(ib[0], 9));
                    shapeless(ib[0], idef(ib[0]).name + " ×9", 9, new List<string> { ib[1] }, N(ib[1], 1));
                }
            var dyeSource = new[] { new[] { "yellow_dye", BK(B.DANDELION) }, new[] { "red_dye", "@red_dye_source" }, new[] { "light_blue_dye", BK(B.BLUE_ORCHID) },
                new[] { "magenta_dye", BK(B.ALLIUM) }, new[] { "orange_dye", BK(B.ORANGE_TULIP) }, new[] { "pink_dye", BK(B.PINK_TULIP) }, new[] { "light_gray_dye", "@light_gray_dye_source" },
                new[] { "blue_dye", BK(B.CORNFLOWER) }, new[] { "white_dye", "@white_dye_source" }, new[] { "black_dye", mb("WITHER_ROSE") }, new[] { "brown_dye", BK(B.BROWN_MUSHROOM) } };
            foreach (var ds in dyeSource) if (ds[1] != null) shapeless(ds[0], idef(ds[0]).name, 1, new List<string> { ds[1] }, N(ds[1], 1));
            foreach (var t in new[] { new[] { "cyan_dye", "green_dye", "blue_dye" }, new[] { "gray_dye", "black_dye", "white_dye" }, new[] { "lime_dye", "green_dye", "white_dye" }, new[] { "purple_dye", "red_dye", "blue_dye" } })
                shapeless(t[0], idef(t[0]).name + " ×2", 2, new List<string> { t[1], t[2] }, N(t[1], 1, t[2], 1));
            var colors = new[] { "WHITE", "ORANGE", "MAGENTA", "LIGHT_BLUE", "YELLOW", "LIME", "PINK", "GRAY", "LIGHT_GRAY", "CYAN", "PURPLE", "BLUE", "BROWN", "GREEN", "RED", "BLACK" };
            foreach (var C in colors)
            {
                string dye = C.ToLowerInvariant() + "_dye", wool = mb("WOOL_" + C), glass = mb(C + "_STAINED_GLASS"), powder = mb(C + "_CONCRETE_POWDER"),
                    terra = C == "WHITE" ? BK(B.TERRA_WHITE) : C == "ORANGE" ? BK(B.TERRA_ORANGE) : mb(C + "_TERRACOTTA");
                if (C != "WHITE" && wool != null) shapeless(wool, idef(wool).name, 1, new List<string> { BK(B.WOOL_WHITE), dye }, N(BK(B.WOOL_WHITE), 1, dye, 1));
                if (glass != null) shaped(glass, idef(glass).name + " ×8", 8, new[] { "GGG", "GDG", "GGG" }, K("G", BK(B.GLASS), "D", dye), N(BK(B.GLASS), 8, dye, 1));
                if (powder != null) shapeless(powder, idef(powder).name + " ×8", 8, new List<string> { BK(B.SAND), BK(B.SAND), BK(B.SAND), BK(B.SAND), BK(B.GRAVEL), BK(B.GRAVEL), BK(B.GRAVEL), BK(B.GRAVEL), dye }, N(BK(B.SAND), 4, BK(B.GRAVEL), 4, dye, 1));
                if (terra != null) shaped(terra, idef(terra).name + " ×8", 8, new[] { "TTT", "TDT", "TTT" }, K("T", BK(B.TERRACOTTA), "D", dye), N(BK(B.TERRACOTTA), 8, dye, 1));
            }
            foreach (var pm in new[] { new[] { "wooden", "@planks" }, new[] { "stone", "@stonecraft" } })
                foreach (var ks in new[] { new object[] { "pickaxe", new[] { "MMM", ".S.", ".S." }, 3 }, new object[] { "axe", new[] { "MM", "MS", ".S" }, 3 }, new object[] { "shovel", new[] { "M", "S", "S" }, 1 },
                    new object[] { "sword", new[] { "M", "M", "S" }, 2 }, new object[] { "hoe", new[] { "MM", ".S", ".S" }, 2 } })
                {
                    string kind = (string)ks[0], outKey = pm[0] + "_" + kind;
                    shaped(outKey, idef(outKey).name, 1, (string[])ks[1], K("M", pm[1], "S", "stick"), N(pm[1], (int)ks[2], "stick", kind == "sword" ? 1 : 2), kind == "axe" || kind == "hoe");
                }
            foreach (var C in colors)
            {
                string bed = C == "RED" ? BK(B.BED) : mb("BED_" + C), wool = mb("WOOL_" + C);
                if (bed != null && wool != null) shaped(bed, idef(bed).name, 1, new[] { "WWW", "PPP" }, K("W", wool, "P", "@planks"), N(wool, 3, "@planks", 3));
            }
            foreach (var pdt in new[] { new[] { PL, BK(B.DOOR), BK(B.OAK_TRAPDOOR) }, new[] { BK(B.SPRUCE_PLANKS), mb("SPRUCE_DOOR"), BK(B.SPRUCE_TRAPDOOR) }, new[] { BK(B.BIRCH_PLANKS), mb("BIRCH_DOOR"), BK(B.BIRCH_TRAPDOOR) } })
            {
                if (pdt[1] != null) shaped(pdt[1], idef(pdt[1]).name + " ×3", 3, new[] { "MM", "MM", "MM" }, K("M", pdt[0]), N(pdt[0], 6));
                if (pdt[2] != null) shaped(pdt[2], idef(pdt[2]).name + " ×2", 2, new[] { "MMM", "MMM" }, K("M", pdt[0]), N(pdt[0], 6));
            }
            // Native shape recipes that already existed as real 8-bit blocks.
            Action<int, string, string[], int, bool> nativeShapeRecipe = (o, bs, shape, count, mirror) =>
            {
                if (bs == null) return;
                var keys = K("M", bs, "S", "stick");
                var need = needFromShape(shape, keys);
                shaped(BK(o), blocks[o] != null ? blocks[o].name : idef(BK(o)).name, count, shape, keys, need, mirror);
            };
            foreach (var ob in new[] { new[] { B.OAK_SLAB, B.PLANKS }, new[] { B.SPRUCE_SLAB, B.SPRUCE_PLANKS }, new[] { B.BIRCH_SLAB, B.BIRCH_PLANKS }, new[] { B.STONE_SLAB, B.STONE }, new[] { B.COBBLE_SLAB, B.COBBLE }, new[] { B.STONEBRICK_SLAB, B.STONEBRICK }, new[] { B.SANDSTONE_SLAB, B.SANDSTONE }, new[] { B.DARK_SLAB, B.DARK_PLANKS } })
                nativeShapeRecipe(ob[0], BK(ob[1]), new[] { "MMM" }, 6, false);
            foreach (var ob in new[] { new[] { B.OAK_STAIRS, B.PLANKS }, new[] { B.SPRUCE_STAIRS, B.SPRUCE_PLANKS }, new[] { B.BIRCH_STAIRS, B.BIRCH_PLANKS }, new[] { B.STONE_STAIRS, B.STONE }, new[] { B.COBBLE_STAIRS, B.COBBLE }, new[] { B.STONEBRICK_STAIRS, B.STONEBRICK }, new[] { B.SANDSTONE_STAIRS, B.SANDSTONE }, new[] { B.DARK_STAIRS, B.DARK_PLANKS } })
                nativeShapeRecipe(ob[0], BK(ob[1]), new[] { "M..", "MM.", "MMM" }, 4, true);
            foreach (var ob in new[] { new[] { B.OAK_FENCE, B.PLANKS }, new[] { B.SPRUCE_FENCE, B.SPRUCE_PLANKS }, new[] { B.BIRCH_FENCE, B.BIRCH_PLANKS }, new[] { B.DARK_FENCE, B.DARK_PLANKS } })
                nativeShapeRecipe(ob[0], BK(ob[1]), new[] { "MSM", "MSM" }, 3, false);
            foreach (var ob in new[] { new[] { B.OAK_GATE, B.PLANKS }, new[] { B.SPRUCE_GATE, B.SPRUCE_PLANKS }, new[] { B.BIRCH_GATE, B.BIRCH_PLANKS } })
                nativeShapeRecipe(ob[0], BK(ob[1]), new[] { "SMS", "SMS" }, 1, false);
            nativeShapeRecipe(B.COBBLE_WALL, BK(B.COBBLE), new[] { "MMM", "MMM" }, 6, false);
            nativeShapeRecipe(B.GLASS_PANE, BK(B.GLASS), new[] { "MMM", "MMM" }, 16, false);
            nativeShapeRecipe(B.WHITE_CARPET, BK(B.WOOL_WHITE), new[] { "MM" }, 3, false);
            nativeShapeRecipe(B.STONE_PLATE, BK(B.STONE), new[] { "MM" }, 1, false);
            nativeShapeRecipe(B.STONE_BUTTON, BK(B.STONE), new[] { "M" }, 1, false);

            var seen = new HashSet<string>();
            foreach (var k in VIRTUAL_CREATIVE_KEYS)
            {
                var d = VirtualByKey(k); var bs = d != null && d.@base != null ? mainBlockItemKey(d.@base) : null;
                if (bs == null || d.shape == null) continue;
                string[] shape = null; Dictionary<char, string> keys = null; int n = 0;
                switch (d.shape)
                {
                    case "slab": shape = new[] { "MMM" }; keys = K("M", bs); n = 6; break;
                    case "stairs": shape = new[] { "M..", "MM.", "MMM" }; keys = K("M", bs); n = 4; break;
                    case "wall": shape = new[] { "MMM", "MMM" }; keys = K("M", bs); n = 6; break;
                    case "fence": shape = new[] { "MSM", "MSM" }; keys = K("M", bs, "S", "stick"); n = 3; break;
                    case "gate": shape = new[] { "SMS", "SMS" }; keys = K("M", bs, "S", "stick"); n = 1; break;
                    case "button": shape = new[] { "M" }; keys = K("M", bs); n = 1; break;
                    case "plate": shape = new[] { "MM" }; keys = K("M", bs); n = 1; break;
                    case "pane": shape = new[] { "MMM", "MMM" }; keys = K("M", bs); n = 16; break;
                    case "carpet": shape = new[] { "MM" }; keys = K("M", bs); n = 3; break;
                }
                if (shape == null) continue;
                var outKey = VK(k);
                if (!seen.Add(outKey)) continue;
                RECIPES.Add(new Recipe { name = d.name, outKey = outKey, outCount = n, need = needFromShape(shape, keys) });
                craftRule(outKey, shape, keys, true);
            }
        }

        public static bool recipeFits(Recipe r)
        {
            var sim = cloneInventory();
            foreach (var kv in r.need) if (!removeItem(kv.Key, kv.Value, sim)) return false;
            return canFitItem(r.outKey, r.outCount, sim);
        }
        public static bool canCraft(Recipe r)
        {
            if (player.creative) return true;
            foreach (var kv in r.need) if (countItem(kv.Key) < kv.Value) return false;
            return recipeFits(r);
        }
        public static void craft(Recipe r)
        {
            if (player.creative)
            {
                var d = idef(r.outKey);
                inventory[selected] = normStack(new Stack(r.outKey, d.stack != 0 ? d.stack : 1));
                if (d.maxDur != 0 && inventory[selected] != null) inventory[selected].dur = d.maxDur;
                drawHotbar(); renderCurrentUI();
                return;
            }
            if (!canCraft(r)) { toast("Не хватает ресурсов или места"); return; }
            var before = cloneInventory();
            foreach (var kv in r.need)
                if (!removeItem(kv.Key, kv.Value))
                {
                    for (int i = 0; i < inventory.Length; i++) inventory[i] = before[i];
                    toast("Не хватает ресурсов"); return;
                }
            int left = addItem(r.outKey, r.outCount);
            if (left != 0)
            {
                for (int i = 0; i < inventory.Length; i++) inventory[i] = before[i];
                inventoryDirty = true;
                toast("Не хватает места в инвентаре"); return;
            }
            toast("Создано: " + r.name);
            sfxCraft(); saveGameSoon(); renderCurrentUI(); drawHotbar();
        }

        // ---------------- slot operations (UI-agnostic) ----------------
        public static bool moveIntoSlot(Stack[] src, int si, Stack[] dst, int di, Func<string, bool> accept = null, bool extractOnly = false)
        {
            if (src == dst && si == di) return false;
            var s = src[si];
            if (s == null || extractOnly || (accept != null && !accept(s.key))) return false;
            var t = dst[di];
            if (t == null) { dst[di] = s; src[si] = null; return true; }
            if (t.key == s.key && (idef(s.key).stack != 0 ? idef(s.key).stack : 64) > 1)
            {
                int mx = idef(s.key).stack != 0 ? idef(s.key).stack : 64, q = Math.Min(s.count, mx - t.count);
                if (q <= 0) return false;
                t.count += q; s.count -= q;
                if (s.count == 0) src[si] = null;
                return true;
            }
            return false;
        }
        public static Stack cloneStackPart(Stack s, int count) { return new Stack(s.key, count, s.dur); }
        public static List<Stack[]> openInventoryArrays()
        {
            var o = new List<Stack[]> { inventory };
            if (uiMode == "chest" && uiKey != null) foreach (var k in chestGroupKeys(uiKey)) o.Add(getChest(k));
            else if (uiMode == "furnace" && uiKey != null) o.Add(getFurnace(uiKey).slots);
            else if (uiMode == "inventory" || uiMode == "craft") { o.Add(craft2); o.Add(craft3); o.Add(armor); o.Add(offhand); }
            return o;
        }
        public static int addStackToArray(Stack[] arr, Stack stack, Func<string, bool> accept = null)
        {
            if (stack == null || stack.count <= 0) return 0;
            var d = idef(stack.key); int mx = d.stack != 0 ? d.stack : 64, left = stack.count;
            if (mx > 1)
                for (int i = 0; i < arr.Length && left > 0; i++)
                {
                    var t = arr[i];
                    if (t != null && t.key == stack.key && (accept == null || accept(stack.key))) { int q = Math.Min(left, mx - t.count); if (q > 0) { t.count += q; left -= q; } }
                }
            for (int i = 0; i < arr.Length && left > 0; i++)
                if (arr[i] == null && (accept == null || accept(stack.key))) { int q = Math.Min(left, mx); arr[i] = cloneStackPart(stack, q); left -= q; }
            return left;
        }
        public static bool quickMoveSlot(Stack[] arr, int i, SlotOpts opts)
        {
            opts = opts ?? new SlotOpts();
            var s = arr[i];
            if (s == null || (opts.extractOnly && arr == inventory)) return false;
            int left = s.count;
            if (arr != inventory)
            {
                left = addStackToArray(inventory, s);
                if (left != s.count) { if (left != 0) arr[i].count = left; else arr[i] = null; return true; }
                return false;
            }
            if (uiMode == "chest" && uiKey != null)
            {
                foreach (var k in chestGroupKeys(uiKey))
                {
                    var a = getChest(k); int n = addStackToArray(a, new Stack(s.key, left, s.dur));
                    if (n != left) { left = n; if (left == 0) break; }
                }
            }
            else if (uiMode == "furnace" && uiKey != null)
            {
                var f = getFurnace(uiKey);
                var targets = new List<int>();
                if (SMELT.ContainsKey(s.key)) targets.Add(0);
                if (fuelValue(s) > 0) targets.Add(1);
                foreach (var k in targets)
                {
                    var t = f.slots[k]; int mx = idef(s.key).stack != 0 ? idef(s.key).stack : 64;
                    if (t == null) { f.slots[k] = cloneStackPart(s, left); left = 0; break; }
                    if (t.key == s.key && t.count < mx) { int q = Math.Min(left, mx - t.count); t.count += q; left -= q; if (left == 0) break; }
                }
            }
            else if ((uiMode == "inventory" || uiMode == "craft") && !player.creative)
            {
                var a = idef(s.key).armor;
                if (a != null && armor[a.slot] == null) { armor[a.slot] = cloneStackPart(s, 1); left--; }
                else if (offhand[0] == null && (s.key == BK(B.TORCH) || s.key == "bow")) { offhand[0] = cloneStackPart(s, 1); left--; }
            }
            if (left != s.count) { if (left != 0) arr[i].count = left; else arr[i] = null; return true; }
            return false;
        }
        public static bool collectMatchingToCursor(string key)
        {
            if (uiCursor == null || uiCursor.key != key) return false;
            int mx = idef(key).stack != 0 ? idef(key).stack : 64;
            if (mx <= 1 || uiCursor.count >= mx) return false;
            bool changed = false;
            foreach (var a in openInventoryArrays())
                for (int i = 0; i < a.Length; i++)
                {
                    var s = a[i];
                    if (s == null || s == uiCursor || s.key != key) continue;
                    int q = Math.Min(mx - uiCursor.count, s.count);
                    if (q <= 0) continue;
                    uiCursor.count += q; s.count -= q;
                    if (s.count <= 0) a[i] = null;
                    changed = true;
                    if (uiCursor.count >= mx) return changed;
                }
            return changed;
        }
        public static bool slotRightClick(Stack[] arr, int i, SlotOpts opts)
        {
            opts = opts ?? new SlotOpts();
            var cur = arr[i];
            if (uiCursor == null)
            {
                if (cur == null) return false;
                int take = opts.single ? cur.count : (int)Math.Ceiling(cur.count / 2.0);
                uiCursor = cloneStackPart(cur, take);
                cur.count -= take;
                if (cur.count <= 0) arr[i] = null;
                return true;
            }
            if (opts.extractOnly || (opts.accept != null && !opts.accept(uiCursor.key))) return false;
            if (opts.single)
            {
                if (cur != null) return false;
                arr[i] = cloneStackPart(uiCursor, 1);
                if (--uiCursor.count <= 0) uiCursor = null;
                return true;
            }
            if (cur == null) { arr[i] = cloneStackPart(uiCursor, 1); if (--uiCursor.count <= 0) uiCursor = null; return true; }
            if (cur.key == uiCursor.key)
            {
                int mx = idef(cur.key).stack != 0 ? idef(cur.key).stack : 64;
                if (cur.count < mx) { cur.count++; if (--uiCursor.count <= 0) uiCursor = null; return true; }
            }
            return false;
        }
        /// <summary>Left click on a slot (el.onclick in slotEl). Returns true when state changed.</summary>
        public static bool slotLeftClick(Stack[] arr, int i, SlotOpts opts, bool shift)
        {
            opts = opts ?? new SlotOpts();
            if (shift && quickMoveSlot(arr, i, opts)) { inventoryDirty = true; saveGameSoon(); return true; }
            var cur = arr[i];
            if (uiCursor == null && cur != null) { uiCursor = cur; arr[i] = null; }
            else if (uiCursor != null)
            {
                if (opts.extractOnly)
                {
                    if (cur != null && cur.key == uiCursor.key && (idef(cur.key).stack != 0 ? idef(cur.key).stack : 64) > 1)
                    {
                        int mx = idef(cur.key).stack != 0 ? idef(cur.key).stack : 64, q = Math.Min(cur.count, mx - uiCursor.count);
                        uiCursor.count += q; cur.count -= q;
                        if (cur.count == 0) arr[i] = null;
                    }
                    else return false;
                }
                else if (opts.accept != null && !opts.accept(uiCursor.key)) return false;
                else if (opts.single)
                {
                    if (cur == null) { arr[i] = cloneStackPart(uiCursor, 1); if (--uiCursor.count <= 0) uiCursor = null; }
                    else if (uiCursor.count == 1) { arr[i] = uiCursor; uiCursor = cur; }
                    else return false;
                }
                else if (cur == null) { arr[i] = uiCursor; uiCursor = null; }
                else if (cur.key == uiCursor.key && (idef(cur.key).stack != 0 ? idef(cur.key).stack : 64) > 1)
                {
                    int mx = idef(cur.key).stack != 0 ? idef(cur.key).stack : 64, q = Math.Min(uiCursor.count, mx - cur.count);
                    cur.count += q; uiCursor.count -= q;
                    if (uiCursor.count == 0) uiCursor = null;
                }
                else { var q = arr[i]; arr[i] = uiCursor; uiCursor = q; }
            }
            inventoryDirty = true; saveGameSoon();
            return true;
        }
        public static bool slotDoubleClick(Stack[] arr, int i)
        {
            var q = arr[i] ?? uiCursor;
            if (q == null) return false;
            if (uiCursor == null) { uiCursor = q; arr[i] = null; }
            if (collectMatchingToCursor(uiCursor.key)) { inventoryDirty = true; saveGameSoon(); }
            return true;
        }

        public static readonly List<int> CREATIVE_BLOCK_IDS = new List<int>();
        static void InitCreativeIds()
        {
            CREATIVE_BLOCK_IDS.Clear();
            CREATIVE_BLOCK_IDS.AddRange(new[] { B.GRASS, B.DIRT, B.STONE, B.SAND, B.LOG, B.LEAVES, B.PLANKS, B.COBBLE, B.GLASS, B.BRICKS, B.COAL, B.IRON, B.GOLD, B.DIAMOND, B.GRAVEL,
                B.SNOW, B.CLAY, B.OBSIDIAN, B.STONEBRICK, B.MOSSY, B.CRAFT, B.TNT, B.PUMPKIN, B.MELON, B.BOOKSHELF, B.DANDELION, B.POPPY, B.BLUE_ORCHID, B.ALLIUM, B.AZURE_BLUET,
                B.CORNFLOWER, B.FERN, B.DEAD_BUSH, B.SEAGRASS, B.KELP, B.RED_MUSHROOM, B.BROWN_MUSHROOM, B.FURNACE, B.CHEST, B.TORCH, B.LADDER, B.DOOR, B.BED, B.SAPLING, B.CACTUS,
                B.SANDSTONE, B.ICE, B.WOOL_WHITE, B.WOOL_BLACK, B.WOOL_GRAY, B.WOOL_LIGHT_GRAY, B.WOOL_BROWN, B.WOOL_PINK, B.FARMLAND_MOIST, B.FIRE, B.COBWEB, B.RAIL, B.NETHERRACK,
                B.GOLD_BLOCK, B.CRYING_OBSIDIAN, B.SMOOTH_STONE, B.RED_SAND, B.CARVED_PUMPKIN, B.COAL_BLOCK, B.COPPER, B.GRANITE, B.DIORITE, B.ANDESITE, B.REDSTONE_ORE, B.LAPIS_ORE,
                B.EMERALD_ORE, B.DEEPSLATE_COAL_ORE, B.DEEPSLATE_IRON_ORE, B.DEEPSLATE_GOLD_ORE, B.DEEPSLATE_DIAMOND_ORE, B.DEEPSLATE_REDSTONE_ORE, B.DEEPSLATE_LAPIS_ORE,
                B.DEEPSLATE_EMERALD_ORE, B.DEEPSLATE_COPPER_ORE, B.SPRUCE_PLANKS, B.BIRCH_PLANKS, B.OAK_SLAB, B.SPRUCE_SLAB, B.BIRCH_SLAB, B.STONE_SLAB, B.COBBLE_SLAB,
                B.STONEBRICK_SLAB, B.OAK_STAIRS, B.SPRUCE_STAIRS, B.BIRCH_STAIRS, B.STONE_STAIRS, B.COBBLE_STAIRS, B.STONEBRICK_STAIRS, B.OAK_FENCE, B.SPRUCE_FENCE, B.BIRCH_FENCE,
                B.OAK_GATE, B.SPRUCE_GATE, B.BIRCH_GATE, B.OAK_TRAPDOOR, B.SPRUCE_TRAPDOOR, B.BIRCH_TRAPDOOR, B.GLASS_PANE, B.WHITE_CARPET, B.STONE_PLATE, B.STONE_BUTTON, B.VINE,
                B.GLOW_LICHEN, B.LILY_PAD, B.SEA_PICKLE, B.BAMBOO, B.COBBLE_WALL, B.FLOWER_POT, B.SPRUCE_LOG, B.SPRUCE_LEAVES, B.BIRCH_LOG, B.BIRCH_LEAVES, B.JUNGLE_LOG, B.JUNGLE_LEAVES,
                B.ACACIA_LOG, B.ACACIA_LEAVES, B.DARK_LOG, B.DARK_LEAVES, B.CHERRY_LEAVES, B.PODZOL, B.TERRACOTTA, B.TERRA_ORANGE, B.TERRA_WHITE, B.PACKED_ICE, B.MYCELIUM, B.MUD,
                B.TUBE_CORAL_BLOCK, B.TUBE_CORAL_FAN, B.BRAIN_CORAL_BLOCK, B.BRAIN_CORAL_FAN, B.BUBBLE_CORAL_BLOCK, B.BUBBLE_CORAL_FAN, B.FIRE_CORAL_BLOCK, B.FIRE_CORAL_FAN,
                B.HORN_CORAL_BLOCK, B.HORN_CORAL_FAN, B.MOSS_BLOCK, B.ROOTED_DIRT, B.DRIPSTONE_BLOCK, B.CAVE_VINES, B.DEEPSLATE, B.TUFF, B.MOSS_CARPET, B.CALCITE, B.AMETHYST_BLOCK,
                B.BUDDING_AMETHYST, B.AZALEA_LEAVES, B.FLOWERING_AZALEA_LEAVES, B.SHORT_GRASS, B.OXEYE_DAISY, B.LILY_OF_THE_VALLEY, B.ORANGE_TULIP, B.PINK_TULIP, B.RED_TULIP,
                B.WHITE_TULIP, B.SWEET_BERRY_BUSH, B.SUGAR_CANE, B.LARGE_FERN, B.SUNFLOWER, B.LILAC, B.ROSE_BUSH, B.PEONY, B.MUSHROOM_STEM, B.RED_MUSHROOM_BLOCK, B.BROWN_MUSHROOM_BLOCK,
                B.CARROTS0, B.CARROTS1, B.CARROTS2, B.CARROTS3, B.POTATOES0, B.POTATOES1, B.POTATOES2, B.POTATOES3, B.PUMPKIN_STEM0, B.PUMPKIN_STEM1, B.PUMPKIN_STEM2, B.PUMPKIN_STEM3,
                B.MELON_STEM0, B.MELON_STEM1, B.MELON_STEM2, B.MELON_STEM3 });
        }
        static List<string> _creativeKeys;
        public static List<string> creativeKeys()
        {
            if (_creativeKeys == null) { _creativeKeys = CREATIVE_BLOCK_IDS.Select(BK).Concat(VIRTUAL_CREATIVE_KEYS.Select(VK)).Concat(ITEM_DEFS.Keys).ToList(); }
            return _creativeKeys;
        }

        public static Recipe craftGridRecipe(Stack[] arr, int n)
        {
            int minX = n, minY = n, maxX = -1, maxY = -1, used = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (arr[y * n + x] != null) { used++; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            if (used == 0) return null;
            foreach (var rec in RECIPES)
            {
                CraftRule rule;
                if (!CRAFT_RULES.TryGetValue(rec.outKey, out rule)) continue;
                if (rule.items != null)
                {
                    if (rule.items.Count != used) continue;
                    var taken = new bool[arr.Length]; bool ok = true;
                    foreach (var spec in rule.items)
                    {
                        int hit = -1;
                        for (int i = 0; i < arr.Length; i++) if (!taken[i] && arr[i] != null && ingredientMatches(spec, arr[i].key)) { hit = i; break; }
                        if (hit < 0) { ok = false; break; }
                        taken[hit] = true;
                    }
                    if (ok) return rec;
                    continue;
                }
                int h = rule.shape.Length, w = rule.shape[0].Length;
                if (maxX - minX + 1 != w || maxY - minY + 1 != h || w > n || h > n) continue;
                foreach (var mirrored in rule.mirror ? new[] { false, true } : new[] { false })
                {
                    bool ok = true; string woolKey = null;
                    for (int y = 0; y < h && ok; y++)
                        for (int x = 0; x < w; x++)
                        {
                            char ch = rule.shape[y][mirrored ? w - 1 - x : x]; var q = arr[(minY + y) * n + (minX + x)];
                            if (ch == '.') { if (q != null) { ok = false; break; } }
                            else
                            {
                                string spec; rule.keys.TryGetValue(ch, out spec);
                                if (q == null || spec == null || !ingredientMatches(spec, q.key)) { ok = false; break; }
                                if (rule.sameWool && ch == 'W') { if (woolKey == null) woolKey = q.key; else if (q.key != woolKey) { ok = false; break; } }
                            }
                        }
                    if (ok) return rec;
                }
            }
            return null;
        }
        public static void consumeCraftGrid(Stack[] arr, Recipe rec)
        {
            foreach (var kv in rec.need)
            {
                int need = kv.Value;
                for (int i = arr.Length - 1; i >= 0 && need > 0; i--)
                {
                    var q = arr[i];
                    if (q != null && ingredientMatches(kv.Key, q.key)) { int take = Math.Min(need, q.count); q.count -= take; need -= take; if (q.count <= 0) arr[i] = null; }
                }
                if (need != 0) throw new Exception("craft grid desync: " + kv.Key);
            }
        }
        public static string recipeNeedText(Recipe rec)
        {
            Func<string, string> label = k =>
                k == "@log" ? "Любое бревно" : k == "@planks" ? "Любые доски" : k == "@stonecraft" ? "Булыжник / глубинный булыжник / чернит"
                : k.StartsWith("@wood:", StringComparison.Ordinal) ? "Бревно: " + k.Substring(6) : k == "@red_dye_source" ? "Мак или красный тюльпан"
                : k == "@light_gray_dye_source" ? "Светло-серый цветок" : k == "@white_dye_source" ? "Костная мука или ландыш" : k == "wool" || k == "@wool" ? "Шерсть" : idef(k).name;
            return string.Join(", ", rec.need.Select(kv => label(kv.Key) + " ×" + kv.Value));
        }

        // ---------------- structure loot ----------------
        sealed class LootDef { public int[] rolls; public object[][] pool; }
        static Dictionary<string, LootDef> STRUCT_LOOT;
        static void InitLoot()
        {
            Func<int, string, int, int, object[]> E = (w, k, a, b) => new object[] { w, k, a, b };
            STRUCT_LOOT = new Dictionary<string, LootDef> {
                { "pyramid", new LootDef { rolls = new[] { 2, 4 }, pool = new[] { E(25,"bone",4,6), E(25,"rotten_flesh",3,7), E(10,"gunpowder",1,8), E(10,"iron_ingot",1,5), E(10,"gold_ingot",2,7), E(5,"emerald",1,3), E(3,"diamond",1,3), E(2,"golden_apple",1,1) } } },
                { "portal", new LootDef { rolls = new[] { 4, 8 }, pool = new[] { E(40,BK(B.OBSIDIAN),1,2), E(40,"flint",1,4), E(15,"gold_ingot",2,8), E(5,"golden_carrot",4,12), E(5,"golden_apple",1,1), E(4,BK(B.GOLD_BLOCK),1,2) } } },
                { "outpost", new LootDef { rolls = new[] { 3, 4 }, pool = new[] { E(10,BK(B.DARK_LOG),2,3), E(7,"wheat",3,5), E(5,"carrot",3,5), E(5,"potato",2,5), E(4,"arrow",2,7), E(3,"iron_ingot",1,3), E(2,"bow",1,1) } } },
                { "ship_supply", new LootDef { rolls = new[] { 3, 6 }, pool = new[] { E(20,"rotten_flesh",2,6), E(12,"wheat",2,6), E(10,"potato",2,6), E(10,"carrot",2,6), E(8,"coal",2,8), E(8,"wheat_seeds",1,4), E(6,BK(B.PUMPKIN),1,3), E(6,"leather",1,4), E(5,BK(B.TNT),1,2), E(5,"gunpowder",1,5), E(4,BK(B.MOSS_BLOCK),1,4), E(3,"bread",1,3) } } },
                { "ship_treasure", new LootDef { rolls = new[] { 3, 6 }, pool = new[] { E(25,"iron_ingot",1,5), E(22,"gold_ingot",1,5), E(12,"emerald",1,4), E(8,"coal",2,6), E(4,"diamond",1,2), E(2,"golden_apple",1,1) } } },
                { "ship_map", new LootDef { rolls = new[] { 2, 4 }, pool = new[] { E(18,"feather",1,4), E(14,"coal",1,5), E(12,"wheat_seeds",1,3), E(10,"iron_ingot",1,3), E(6,BK(B.TNT),1,2), E(5,"gunpowder",1,4), E(3,"emerald",1,2) } } },
                { "mineshaft", new LootDef { rolls = new[] { 3, 5 }, pool = new[] { E(20,BK(B.RAIL),4,8), E(15,BK(B.PLANKS),3,6), E(12,"coal",3,8), E(10,"bread",1,3), E(8,"iron_ingot",1,5), E(6,"gold_ingot",1,3), E(5,BK(B.TORCH),2,6), E(3,"emerald",1,2), E(1,"diamond",1,2), E(1,"golden_apple",1,1) } } },
            };
        }
        static double lootSeedHash2(double x, double z)
        {
            // main re()/zC semantics: apply world seed before ToInt32; keep the initial multiply/add in double arithmetic.
            int xi = JS.toInt32(x + WORLD_SEED), zi = JS.toInt32(z - WORLD_SEED);
            double nd = (double)xi * 374761393.0 + (double)zi * 668265263.0 + 527595518.0;
            int n = JS.toInt32(nd);
            n = n ^ JS.ushr(n, 13);
            n = JS.imul(n, 1274126177);
            return (uint)(n ^ JS.ushr(n, 16)) / 4294967296.0;
        }
        static Func<double> lootRngMain(double a, double b, double salt = 777001)
        {
            int n = JS.toInt32(lootSeedHash2(a * 3 + salt * 17 + 0.31, b * 5 - salt * 7 + 0.77) * 4294967296.0);
            return () =>
            {
                n = unchecked(n + 1831565813);
                int t = JS.imul(n ^ JS.ushr(n, 15), 1 | n);
                t = unchecked(t + JS.imul(t ^ JS.ushr(t, 7), 61 | t)) ^ t;
                return (uint)(t ^ JS.ushr(t, 14)) / 4294967296.0;
            };
        }
        public static Stack[] makeStructureLoot(string kind, int x, int y, int z)
        {
            var slots = new Stack[27]; LootDef def;
            if (!STRUCT_LOOT.TryGetValue(kind, out def)) return slots;
            var rng = lootRngMain(x * 31 + y * 7, z * 31 - y * 5, 777001);
            int total = 0;
            foreach (var e in def.pool) total += (int)e[0];
            int rolls = def.rolls[0] + (int)Math.Floor(rng() * (def.rolls[1] - def.rolls[0] + 1));
            for (int roll = 0; roll < rolls; roll++)
            {
                double pick = rng() * total; var entry = def.pool[0];
                foreach (var cand in def.pool) { pick -= (int)cand[0]; if (pick < 0) { entry = cand; break; } }
                int count = (int)entry[2] + (int)Math.Floor(rng() * ((int)entry[3] - (int)entry[2] + 1));
                int slot = (int)Math.Floor(rng() * 27);
                for (int step = 0; step < 27 && slots[slot] != null; step++) slot = (slot + 1) % 27;
                if (slots[slot] != null) continue;
                var stack = new Stack((string)entry[1], count);
                if (stack.key == "bow") stack.dur = 384;
                slots[slot] = stack;
            }
            return slots;
        }

        // ---------------- chests ----------------
        static readonly int[][] CHEST_FACING_DIRS = { new[] { 0, 1 }, new[] { -1, 0 }, new[] { 0, -1 }, new[] { 1, 0 } };
        public static int chestFacingIndex(string f) { return f == "west" ? 1 : f == "north" ? 2 : f == "east" ? 3 : 0; }
        static int[][] chestPairDirs(string f) { return (chestFacingIndex(f) & 1) != 0 ? new[] { new[] { 0, 1 }, new[] { 0, -1 } } : new[] { new[] { 1, 0 }, new[] { -1, 0 } }; }
        static bool chestSideFlag(string f, int dx, int dz) { var d = CHEST_FACING_DIRS[chestFacingIndex(f)]; return dx == d[1] && dz == -d[0]; }
        public static JObj clearChestPairMeta(JObj m)
        {
            var q = m != null ? m.Clone() : new JObj();
            q.Remove("pairX"); q.Remove("pairZ"); q.Remove("pairDX"); q.Remove("pairDZ"); q.Remove("chestDouble"); q.Remove("chestSide");
            return q;
        }
        public static int[] pairedChestCoords(int x, int y, int z)
        {
            if (getBlock(x, y, z) != B.CHEST) return null;
            var m = getBlockMeta(x, y, z) ?? new JObj();
            if (!m.Bool("chestDouble") || !Json.IsFinite(m.Get("pairX")) || !Json.IsFinite(m.Get("pairZ"))) return null;
            int px = JS.toInt32(m.Num("pairX")), pz = JS.toInt32(m.Num("pairZ"));
            if (getBlock(px, y, pz) != B.CHEST) return null;
            var n = getBlockMeta(px, y, pz) ?? new JObj();
            if (!n.Bool("chestDouble") || n.Num("pairX", double.NaN) != x || n.Num("pairZ", double.NaN) != z) return null;
            return new[] { px, pz };
        }
        static string chestNeighborFacing(int x, int y, int z) { var m = getBlockMeta(x, y, z); return (m != null ? m.Str("facing") : null) ?? "south"; }
        public sealed class ChestPairResult { public string error; public int[] target; }
        public static ChestPairResult findChestPairTarget(int x, int y, int z, string facing)
        {
            var candidates = new List<int[]>();
            foreach (var d in chestPairDirs(facing))
            {
                int nx = x + d[0], nz = z + d[1];
                if (getBlock(nx, y, nz) != B.CHEST || chestNeighborFacing(nx, y, nz) != facing) continue;
                if (pairedChestCoords(nx, y, nz) != null) return new ChestPairResult { error = "Этот сундук уже двойной" };
                candidates.Add(new[] { nx, nz, d[0], d[1] });
            }
            if (candidates.Count > 1) return new ChestPairResult { error = "Нельзя соединить три сундука" };
            if (candidates.Count == 0) return new ChestPairResult { target = null };
            var c0 = candidates[0];
            foreach (var a in chestPairDirs(facing))
            {
                int xx = c0[0] + a[0], zz = c0[1] + a[1];
                if (xx == x && zz == z) continue;
                if (getBlock(xx, y, zz) == B.CHEST && chestNeighborFacing(xx, y, zz) == facing) return new ChestPairResult { error = "Нельзя соединить три сундука" };
            }
            return new ChestPairResult { target = c0 };
        }
        public static bool linkChestPair(int x, int y, int z, int nx, int nz, string facing)
        {
            if (getBlock(x, y, z) != B.CHEST || getBlock(nx, y, nz) != B.CHEST) return false;
            var a = getBlockMeta(x, y, z) ?? new JObj(); var b = getBlockMeta(nx, y, nz) ?? new JObj();
            int dx = nx - x, dz = nz - z;
            var ma = clearChestPairMeta(a); ma["facing"] = facing; ma["pairX"] = (double)nx; ma["pairZ"] = (double)nz; ma["pairDX"] = (double)dx; ma["pairDZ"] = (double)dz; ma["chestDouble"] = true; ma["chestSide"] = chestSideFlag(facing, dx, dz);
            setBlock(x, y, z, B.CHEST, ma);
            var mb2 = clearChestPairMeta(b); mb2["facing"] = facing; mb2["pairX"] = (double)x; mb2["pairZ"] = (double)z; mb2["pairDX"] = (double)-dx; mb2["pairDZ"] = (double)-dz; mb2["chestDouble"] = true; mb2["chestSide"] = chestSideFlag(facing, -dx, -dz);
            setBlock(nx, y, nz, B.CHEST, mb2);
            return true;
        }
        public static int[] ensureChestPairAt(int x, int y, int z)
        {
            var q = pairedChestCoords(x, y, z);
            if (q != null) return q;
            if (getBlock(x, y, z) != B.CHEST) return null;
            var facing = chestNeighborFacing(x, y, z); var r = findChestPairTarget(x, y, z, facing);
            if (r.error == null && r.target != null) { linkChestPair(x, y, z, r.target[0], r.target[1], facing); return new[] { r.target[0], r.target[1] }; }
            return null;
        }
        public static void unpairChestNeighbor(int x, int y, int z)
        {
            var q = pairedChestCoords(x, y, z);
            if (q == null) return;
            var nm = getBlockMeta(q[0], y, q[1]) ?? new JObj();
            setBlock(q[0], y, q[1], B.CHEST, clearChestPairMeta(nm));
        }
        static bool parseXYZ(string k, out int x, out int y, out int z)
        {
            x = y = z = 0;
            var a = (k ?? "").Split(',');
            if (a.Length != 3) return false;
            double dx = Json.ToNum(a[0]), dy = Json.ToNum(a[1]), dz = Json.ToNum(a[2]);
            if (!JS.isFinite(dx) || !JS.isFinite(dy) || !JS.isFinite(dz)) return false;
            x = (int)dx; y = (int)dy; z = (int)dz; return true;
        }
        public static List<string> chestGroupKeys(string k)
        {
            int x, y, z;
            if (!parseXYZ(k, out x, out y, out z)) return new List<string> { k };
            ensureChestPairAt(x, y, z);
            var q = pairedChestCoords(x, y, z);
            if (q == null) return new List<string> { k };
            var l = new List<string> { k, key3(q[0], y, q[1]) };
            l.Sort(string.CompareOrdinal);
            return l;
        }
        public static Stack[] getChest(string k)
        {
            Stack[] a;
            if (!chests.TryGetValue(k, out a))
            {
                a = new Stack[27];
                int x, y, z;
                if (parseXYZ(k, out x, out y, out z))
                {
                    var kind = structureChestKindAt(x, y, z);
                    if (kind != null) a = makeStructureLoot(kind, x, y, z);
                }
                chests.Set(k, a);
            }
            return a;
        }

        // ---------------- smelting ----------------
        public static readonly Dictionary<string, string> SMELT = new Dictionary<string, string>();
        static void InitSmelt()
        {
            SMELT.Clear();
            SMELT["raw_iron"] = "iron_ingot"; SMELT["raw_gold"] = "gold_ingot"; SMELT["raw_copper"] = "copper_ingot";
            SMELT[BK(B.IRON)] = "iron_ingot"; SMELT[BK(B.GOLD)] = "gold_ingot"; SMELT[BK(B.COPPER)] = "copper_ingot";
            SMELT[BK(B.SAND)] = BK(B.GLASS); SMELT[BK(B.RED_SAND)] = BK(B.GLASS); SMELT[BK(B.COBBLE)] = BK(B.STONE); SMELT[BK(B.STONE)] = BK(B.SMOOTH_STONE);
            SMELT[BK(B.CACTUS)] = "green_dye"; SMELT["clay_ball"] = "brick"; SMELT["beef"] = "cooked_beef"; SMELT["pork"] = "cooked_pork"; SMELT["mutton"] = "cooked_mutton";
            SMELT["chicken"] = "cooked_chicken"; SMELT["salmon"] = "cooked_salmon"; SMELT["fish"] = "cooked_fish"; SMELT["potato"] = "baked_potato";
            SMELT[VK("COBBLED_DEEPSLATE")] = BK(B.DEEPSLATE); SMELT[BK(B.SANDSTONE)] = VK("SMOOTH_SANDSTONE"); SMELT[VK("RED_SANDSTONE")] = VK("SMOOTH_RED_SANDSTONE");
            SMELT[VK("WET_SPONGE")] = VK("SPONGE"); SMELT[BK(B.CLAY)] = BK(B.TERRACOTTA); SMELT[BK(B.COAL)] = "coal"; SMELT[BK(B.DEEPSLATE_COAL_ORE)] = "coal";
            SMELT[BK(B.DEEPSLATE_IRON_ORE)] = "iron_ingot"; SMELT[BK(B.DEEPSLATE_GOLD_ORE)] = "gold_ingot"; SMELT[BK(B.DEEPSLATE_COPPER_ORE)] = "copper_ingot";
            foreach (var C in new[] { "WHITE", "ORANGE", "MAGENTA", "LIGHT_BLUE", "YELLOW", "LIME", "PINK", "GRAY", "LIGHT_GRAY", "CYAN", "PURPLE", "BLUE", "BROWN", "GREEN", "RED", "BLACK" })
            {
                string terra = C == "WHITE" ? BK(B.TERRA_WHITE) : C == "ORANGE" ? BK(B.TERRA_ORANGE) : mainBlockItemKey(C + "_TERRACOTTA"), glazed = mainBlockItemKey(C + "_GLAZED");
                if (terra != null && glazed != null) SMELT[terra] = glazed;
            }
        }
        public static Furnace getFurnace(string k)
        {
            Furnace f;
            if (!furnaces.TryGetValue(k, out f)) { f = new Furnace(); furnaces.Set(k, f); }
            return f;
        }
        static readonly Regex RX_FUELV = new Regex("PLANKS|WOOD|LOG|STEM|FENCE|GATE|SLAB|STAIRS");
        public static double fuelValue(Stack s)
        {
            if (s == null) return 0;
            var d = idef(s.key);
            if (d.fuel != 0) return d.fuel;
            if (isVK(s.key)) { var v = VirtualByKey(vkey(s.key)); if (v != null && RX_FUELV.IsMatch(vkey(s.key))) return 15; }
            if (s.key == BK(B.COAL_BLOCK)) return 720;
            if ((isBK(s.key) && isTreeLog(bid(s.key))) || s.key == BK(B.PLANKS) || s.key == BK(B.SPRUCE_PLANKS) || s.key == BK(B.BIRCH_PLANKS) || s.key == BK(B.BOOKSHELF) || s.key == BK(B.CRAFT)) return 15;
            return 0;
        }

        // ---------------- 3D value noise (main thread) ----------------
        public static double hash3(int x, int y, int z, int s = 0)
        {
            int ws = WORLD_SEED ^ JS.imul(s, 0x27d4eb2d);
            int n = JS.imul(x, 73856093) ^ JS.imul(y, 19349663) ^ JS.imul(z, 83492791) ^ JS.imul(ws, unchecked((int)2654435761u));
            n ^= JS.ushr(n, 13);
            n = JS.imul(n, 1274126177);
            n ^= JS.ushr(n, 16);
            return (uint)n / 4294967295.0;
        }
        public static double noise3(double x, double y, double z, double scale, int seed = 0)
        {
            x /= scale; y /= scale; z /= scale;
            int ix = JS.floor(x), iy = JS.floor(y), iz = JS.floor(z);
            double fx = smooth(x - ix), fy = smooth(y - iy), fz = smooth(z - iz);
            Func<int, int, int, double> v = (dx, dy, dz) => hash3(ix + dx, iy + dy, iz + dz, seed);
            Func<double, double, double, double> l = (a, b, t) => a + (b - a) * t;
            double x00 = l(v(0, 0, 0), v(1, 0, 0), fx), x10 = l(v(0, 1, 0), v(1, 1, 0), fx), x01 = l(v(0, 0, 1), v(1, 0, 1), fx), x11 = l(v(0, 1, 1), v(1, 1, 1), fx);
            return l(l(x00, x10, fy), l(x01, x11, fy), fz);
        }
    }
}
