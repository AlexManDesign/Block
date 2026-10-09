// Voxel Forge — Unity port. Versions, settings, mods, world index & save namespaces (localStorage model).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VoxelForge
{
    public sealed class GameSettings
    {
        public int renderDist = 6, simDist = 6;
        public double sensitivity = 1;
        public bool sound = true, clouds = true, dynRes = true;
        public JObj ToJson() { return new JObj { { "renderDist", (double)renderDist }, { "simDist", (double)simDist }, { "sensitivity", sensitivity }, { "sound", sound }, { "clouds", clouds }, { "dynRes", dynRes } }; }
    }

    public sealed class WorldRecord
    {
        public string id, name, mode;
        public bool legacy;
        public double createdAt, updatedAt;
        public double? seed;
        public double[] spawn;
        public JObj ToJson()
        {
            var o = new JObj { { "id", id }, { "name", name }, { "mode", mode }, { "legacy", legacy }, { "createdAt", createdAt }, { "updatedAt", updatedAt } };
            if (seed.HasValue) o["seed"] = seed.Value;
            if (spawn != null) o["spawn"] = new List<object> { spawn[0], spawn[1], spawn[2] };
            return o;
        }
        public static WorldRecord From(JObj o)
        {
            if (o == null) return null;
            var w = new WorldRecord { id = o.Str("id"), name = o.Get("name") == null ? "" : Convert.ToString(o.Get("name") is double d ? (object)JS.ToStr(d) : o.Get("name")), mode = o.Str("mode"), legacy = o.Bool("legacy"), createdAt = o.Num("createdAt"), updatedAt = o.Num("updatedAt") };
            if (Json.IsFinite(o.Get("seed"))) w.seed = o.Num("seed");
            w.spawn = VF.finiteSpawn(o.Get("spawn"));
            return w;
        }
    }

    public sealed class SaveKeys { public string killed, edits, state, seed; public IEnumerable<string> All() { yield return killed; yield return edits; yield return state; yield return seed; } }

    public static partial class VF
    {
        // ---- strict versioned saves: incompatible worlds are ignored, never migrated ----
        public const string GAME_VERSION = "0.36.6";
        public const int SAVE_SCHEMA_VERSION = 1, SAVE_WORLD_VERSION = 18;
        public const string SAVE_VERSION_KEY = "vf_active_save_format";
        public const string WORLDS_KEY = "vf_world_index_v1";
        public static string GAME_MODE = null, CURRENT_WORLD_ID = "", SAVE_NAMESPACE = "";
        public static SaveKeys SAVE_KEYS = new SaveKeys();
        public const string GENERATOR_VERSION = "deepslate-384-main-cave-fluid-34";
        public static readonly HashSet<string> GENERATOR_COMPAT = new HashSet<string> { GENERATOR_VERSION, "deepslate-384-aquifer-pressure-heightmap-33" };
        public const string ENGINE_BUILD = "0.93.59-water67-complete-mesh-guards";
        public const string MODS_KEY = "vf_engine_mods_v1", XRAY_STATE_KEY = "vf_engine_xray_active_v1";
        public const string RENDER_SETTINGS_KEY = "vf_render_settings_v1", GAME_SETTINGS_KEY = "vf_settings_main_v1";
        public static readonly int[] MAIN_RD_VALUES = { 4, 6, 8, 12, 16, 20, 25, 30, 40, 50 }, MAIN_SIM_VALUES = { 4, 6, 8, 12, 16, 20 };
        public static int DEVICE_RD_CAP = 20;
        public static readonly GameSettings gameSettings = new GameSettings();
        public static bool cloudsEnabled = true;

        static void InitSettings()
        {
            try { LocalStorage.setItem(SAVE_VERSION_KEY, SAVE_SCHEMA_VERSION + ":" + SAVE_WORLD_VERSION); } catch { }
            // DEVICE_RD_CAP: deviceMemory (GB) & mobile heuristics.
            double dm = 0; bool mob = false;
            try { dm = SystemInfo.systemMemorySize / 1024.0; mob = Application.isMobilePlatform; } catch { }
            DEVICE_RD_CAP = dm > 0 && dm <= 2 ? 8 : (dm > 0 && dm <= 4) || mob ? 12 : 20;
            try
            {
                var a = Json.TryParse(LocalStorage.getItem(GAME_SETTINGS_KEY) ?? "{}") as JObj ?? new JObj();
                if (a.IsNum("renderDist") && MAIN_RD_VALUES.Contains(a.Int("renderDist")) && a.Num("renderDist") == a.Int("renderDist")) gameSettings.renderDist = Math.Min(a.Int("renderDist"), DEVICE_RD_CAP);
                if (a.IsNum("simDist") && MAIN_SIM_VALUES.Contains(a.Int("simDist")) && a.Num("simDist") == a.Int("simDist")) gameSettings.simDist = Math.Min(a.Int("simDist"), DEVICE_RD_CAP);
                if (a.IsNum("sensitivity") && JS.isFinite(a.Num("sensitivity")) && a.Num("sensitivity") >= 0.3 && a.Num("sensitivity") <= 2) gameSettings.sensitivity = a.Num("sensitivity");
                if (a.Get("sound") is bool s1) gameSettings.sound = s1;
                if (a.Get("dynRes") is bool s2) gameSettings.dynRes = s2;
                if (a.Get("clouds") is bool s3) gameSettings.clouds = s3;
                else
                {
                    var old = Json.TryParse(LocalStorage.getItem(RENDER_SETTINGS_KEY) ?? "{}") as JObj;
                    if (old != null && old.Get("clouds") is bool cb && cb == false) gameSettings.clouds = false;
                }
            }
            catch { }
            cloudsEnabled = gameSettings.clouds;
            InitMods();
        }
        public static void persistGameSettings()
        {
            try
            {
                LocalStorage.setItem(GAME_SETTINGS_KEY, Json.Stringify(gameSettings.ToJson()));
                LocalStorage.setItem(RENDER_SETTINGS_KEY, Json.Stringify(new JObj { { "clouds", gameSettings.clouds } }));
            }
            catch { }
        }
        public static void applyRenderDistanceSetting(int v, bool notify = true)
        {
            var vals = MAIN_RD_VALUES.Where(x => x <= DEVICE_RD_CAP).ToArray();
            int n = vals.Contains(v) ? v : Math.Min(6, DEVICE_RD_CAP);
            gameSettings.renderDist = n;
            if (renderDistance != n) { renderDistance = n; lastStreamCx = 999; lastStreamCz = 999; queue.Clear(); }
            persistGameSettings();
            if (notify) toast("Дальность: " + n + " чанков");
        }
        public static void applySimulationDistanceSetting(int v, bool notify = true)
        {
            var vals = MAIN_SIM_VALUES.Where(x => x <= DEVICE_RD_CAP).ToArray();
            int n = vals.Contains(v) ? v : Math.Min(6, DEVICE_RD_CAP);
            gameSettings.simDist = n;
            simulationDistance = n;
            persistGameSettings();
            if (notify) toast("Симуляция: " + activeSimulationDistance() + " чанков");
        }
        public static void toggleClouds()
        {
            gameSettings.clouds = !gameSettings.clouds;
            cloudsEnabled = gameSettings.clouds;
            persistGameSettings();
            renderSettingsUI();
            toast("Облака " + (cloudsEnabled ? "включены" : "выключены"));
        }

        // ---- mods ----
        public sealed class ModMeta { public string id, name, desc; }
        public static readonly Dictionary<string, bool> MOD_DEFAULTS = new Dictionary<string, bool> { { "torch", false }, { "keepinv", false }, { "minimap", false }, { "fastleaves", true }, { "xray", false }, { "sharks", true } };
        public static readonly ModMeta[] MOD_META = {
            new ModMeta{ id="torch", name="Свет факела в руке", desc="Факел в основной или левой руке динамически освещает мир. Свет считается в шейдере — чанки не перемешиваются каждый кадр." },
            new ModMeta{ id="keepinv", name="Сохранять инвентарь", desc="Экран смерти остаётся, но инвентарь, броня и левая рука не выпадают." },
            new ModMeta{ id="minimap", name="Миникарта", desc="Кэшированная карта чанков вокруг игрока; обновляется только при изменении чанка и с ограниченным бюджетом на кадр." },
            new ModMeta{ id="fastleaves", name="Быстрое опадание листвы", desc="После рубки ствола листва распадается в 2.5 раза быстрее." },
            new ModMeta{ id="xray", name="X-Ray", desc="X включает/выключает режим: камень становится сеткой, руды, лава и полезные структуры остаются видимыми." },
            new ModMeta{ id="sharks", name="Акулы", desc="Враждебные акулы появляются только в глубоких океанах и остаются водными мобами." },
        };
        public static Dictionary<string, bool> MODS = new Dictionary<string, bool>();
        public static bool xrayActive = false;
        static Dictionary<string, bool> loadMods()
        {
            var r = new Dictionary<string, bool>(MOD_DEFAULTS);
            try
            {
                var o = Json.TryParse(LocalStorage.getItem(MODS_KEY) ?? "{}") as JObj;
                if (o != null) foreach (var kv in o) r[kv.Key] = Json.Truthy(kv.Value);
            }
            catch { }
            return r;
        }
        static void InitMods()
        {
            MODS = loadMods();
            xrayActive = false;
            try { xrayActive = modEnabled("xray") && LocalStorage.getItem(XRAY_STATE_KEY) == "1"; } catch { }
        }
        public static bool modEnabled(string id) { bool v; return MODS.TryGetValue(id, out v) && v; }
        public static void persistMods()
        {
            try { var o = new JObj(); foreach (var kv in MODS) o[kv.Key] = kv.Value; LocalStorage.setItem(MODS_KEY, Json.Stringify(o)); } catch { }
        }
        public static void setMod(string id, bool on)
        {
            if (!MODS.ContainsKey(id)) return;
            bool was = MODS[id];
            MODS[id] = on;
            persistMods();
            if (id == "xray" && !MODS["xray"])
            {
                xrayActive = false;
                try { LocalStorage.setItem(XRAY_STATE_KEY, "0"); } catch { }
            }
            if (id == "fastleaves" && was != MODS["fastleaves"] && worldSim != null)
            {
                double ratio = MODS["fastleaves"] ? 0.4 : 2.5;
                foreach (var k in worldSim.leafDecay.Keys.ToList()) worldSim.leafDecay[k] = Math.Max(0.05, worldSim.leafDecay[k] * ratio);
            }
            if (id == "sharks" && !MODS["sharks"])
            {
                for (int i = liveMobs.Count - 1; i >= 0; i--)
                {
                    var m = liveMobs[i];
                    if (m.type == "shark") { m.dead = true; removeLiveMob(m, false); }
                }
            }
            updateModHUD();
            renderModsUI();
        }
        public static void toggleXray()
        {
            if (!modEnabled("xray")) { toast("Сначала включите мод X-Ray в меню модов"); return; }
            xrayActive = !xrayActive;
            try { LocalStorage.setItem(XRAY_STATE_KEY, xrayActive ? "1" : "0"); } catch { }
            updateModHUD();
            toast("X-Ray " + (xrayActive ? "включён" : "выключен"));
        }
        public static void updateModHUD() { /* HUD visibility is evaluated every OnGUI frame from modEnabled("minimap") / xrayActive. */ }

        // ---- worlds ----
        public static int WORLD_SEED = 0;
        public static string legacyNamespaceForMode(string mode)
        {
            string m = mode == "creative" ? "creator" : "survival";
            return "vf_s" + SAVE_SCHEMA_VERSION + "_w" + SAVE_WORLD_VERSION + "_" + m + "_";
        }
        static readonly Regex RX_NS = new Regex("[^a-zA-Z0-9_-]");
        public static string worldNamespace(string id) { return "vf_s" + SAVE_SCHEMA_VERSION + "_w" + SAVE_WORLD_VERSION + "_world_" + RX_NS.Replace(id ?? "", "_") + "_"; }
        public static SaveKeys worldKeysForNamespace(string ns) { return new SaveKeys { killed = ns + "killed_mobs", edits = ns + "world", state = ns + "state", seed = ns + "seed" }; }
        public static List<WorldRecord> loadWorldIndex()
        {
            var a = new List<WorldRecord>();
            try
            {
                var q = Json.TryParse(LocalStorage.getItem(WORLDS_KEY) ?? "[]") as List<object>;
                if (q != null)
                    foreach (var o in q)
                    {
                        var j = o as JObj; if (j == null || !(j.Get("id") is string)) continue;
                        var m = j.Str("mode"); if (m != "creative" && m != "survival") continue;
                        a.Add(WorldRecord.From(j));
                    }
            }
            catch { }
            return a;
        }
        public static void persistWorldIndex(List<WorldRecord> a)
        {
            try { LocalStorage.setItem(WORLDS_KEY, Json.Stringify(a.Select(w => (object)w.ToJson()).ToList())); } catch { }
        }
        static bool legacyWorldExists(string mode)
        {
            var k = worldKeysForNamespace(legacyNamespaceForMode(mode));
            try { return k.All().Any(x => LocalStorage.getItem(x) != null); } catch { return false; }
        }
        public static List<WorldRecord> ensureLegacyWorldIndex()
        {
            var a = loadWorldIndex(); bool changed = false;
            foreach (var mode in new[] { "creative", "survival" })
            {
                var id = "legacy_" + mode;
                if (legacyWorldExists(mode) && !a.Any(w => w.id == id))
                {
                    a.Add(new WorldRecord { id = id, name = mode == "creative" ? "Legacy Creator" : "Legacy Survival", mode = mode, legacy = true, createdAt = 0, updatedAt = JS.dateNow() });
                    changed = true;
                }
            }
            if (changed) persistWorldIndex(a);
            return a;
        }
        public static string makeWorldId()
        {
            Func<string> r = () => { var s = JS.base36((long)(JS.random() * 2176782336.0)); return s.Length > 6 ? s.Substring(0, 6) : s; };
            return JS.base36((long)JS.dateNow()) + "_" + r();
        }
        public static string defaultWorldName(string mode)
        {
            var a = loadWorldIndex(); var bs = mode == "creative" ? "Creator" : "Survival";
            int n = 1; string name = bs + " " + n;
            var used = new HashSet<string>(a.Select(w => (w.name ?? "").ToLowerInvariant()));
            while (used.Contains(name.ToLowerInvariant())) name = bs + " " + (++n);
            return name;
        }
        public static WorldRecord createWorldRecord(string mode, string name)
        {
            mode = mode == "creative" ? "creative" : "survival";
            var nm = (name ?? "").Trim(); if (nm.Length > 40) nm = nm.Substring(0, 40);
            var w = new WorldRecord { id = makeWorldId(), name = nm.Length > 0 ? nm : defaultWorldName(mode), mode = mode, legacy = false, createdAt = JS.dateNow(), updatedAt = JS.dateNow() };
            var a = loadWorldIndex(); a.Insert(0, w); persistWorldIndex(a);
            return w;
        }
        public static string worldRecordNamespace(WorldRecord w) { return w != null && w.legacy ? legacyNamespaceForMode(w.mode) : worldNamespace(w != null ? w.id ?? "" : ""); }
        public static SaveKeys worldRecordKeys(WorldRecord w) { return worldKeysForNamespace(worldRecordNamespace(w)); }
        public static double[] finiteSpawn(object q)
        {
            var l = q as List<object>;
            if (l == null || l.Count < 3) return null;
            for (int i = 0; i < 3; i++) if (!Json.IsFinite(l[i])) return null;
            return new[] { Json.ToNum(l[0]), Json.ToNum(l[1]), Json.ToNum(l[2]) };
        }
        public static int? seedForWorldRecord(WorldRecord w)
        {
            if (w != null && w.seed.HasValue && JS.isFinite(w.seed.Value)) return JS.toInt32(w.seed.Value);
            try
            {
                var r = LocalStorage.getItem(worldRecordKeys(w).seed);
                double n = r == null ? double.NaN : Json.ToNum(r);
                if (JS.isFinite(n)) return JS.toInt32(n);
            }
            catch { }
            return null;
        }
        public static double[] spawnForWorldRecord(WorldRecord w)
        {
            try
            {
                var r = Json.TryParse(LocalStorage.getItem(worldRecordKeys(w).state) ?? "null") as JObj;
                var q = finiteSpawn(r?.Obj("player")?.Get("spawn"));
                if (q != null && saveHeaderValid(r)) return q;
            }
            catch { }
            return w?.spawn;
        }
        static string uniqueCopyWorldName(string srcName, List<WorldRecord> a)
        {
            var b0 = (srcName ?? "Мир").Trim(); if (b0.Length == 0) b0 = "Мир";
            var bs = b0 + " — копия";
            var used = new HashSet<string>(a.Select(w => (w.name ?? "").ToLowerInvariant()));
            if (!used.Contains(bs.ToLowerInvariant())) return bs.Length > 40 ? bs.Substring(0, 40) : bs;
            int n = 2;
            while (used.Contains((bs + " " + n).ToLowerInvariant())) n++;
            var r = bs + " " + n; return r.Length > 40 ? r.Substring(0, 40) : r;
        }
        public static WorldRecord copyWorldRecord(string id)
        {
            var a = loadWorldIndex(); var src = a.FirstOrDefault(q => q.id == id);
            if (src == null) return null;
            var seed = seedForWorldRecord(src);
            if (!seed.HasValue) { alert("Не удалось прочитать seed этого мира. Сначала один раз запусти его."); return null; }
            var spawn = spawnForWorldRecord(src); double now = JS.dateNow();
            var w = new WorldRecord { id = makeWorldId(), name = uniqueCopyWorldName(src.name, a), mode = src.mode, legacy = false, createdAt = now, updatedAt = now, seed = seed.Value };
            if (spawn != null) w.spawn = spawn;
            a.Insert(0, w); persistWorldIndex(a);
            // Deliberately initialise ONLY the seed key. No world edits, state, mobs, containers or player data are copied.
            try { LocalStorage.setItem(worldKeysForNamespace(worldNamespace(w.id)).seed, seed.Value.ToString()); } catch { }
            return w;
        }
        static double worldIndexTouchAt = 0;
        public static void touchWorldRecord(bool force = false)
        {
            if (string.IsNullOrEmpty(CURRENT_WORLD_ID)) return;
            double now = JS.dateNow();
            if (!force && now - worldIndexTouchAt < 5000) return;
            worldIndexTouchAt = now;
            var a = loadWorldIndex(); var w = a.FirstOrDefault(q => q.id == CURRENT_WORLD_ID);
            if (w != null) { w.updatedAt = now; persistWorldIndex(a); }
        }
        public static void configureSaveMode(string mode, string worldId = "")
        {
            GAME_MODE = mode == "creative" ? "creative" : "survival";
            CURRENT_WORLD_ID = worldId ?? "";
            var rec = loadWorldIndex().FirstOrDefault(q => q.id == CURRENT_WORLD_ID);
            SAVE_NAMESPACE = rec != null && rec.legacy ? legacyNamespaceForMode(GAME_MODE) : worldNamespace(CURRENT_WORLD_ID.Length > 0 ? CURRENT_WORLD_ID : "session_" + GAME_MODE);
            SAVE_KEYS = worldKeysForNamespace(SAVE_NAMESPACE);
        }
        public static int randomWorldSeed()
        {
            int n = 0;
            try { var b = new byte[4]; using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(b); n = BitConverter.ToInt32(b, 0); } catch { }
            if (n == 0) n = JS.toInt32(JS.random() * 4294967296.0) ^ unchecked((int)((long)JS.dateNow() & 0xffffffff));
            if (n == 0) n = 0x13579bdf;
            return n;
        }
        public static int loadOrCreateWorldSeed()
        {
            double n = double.NaN;
            try { var r = LocalStorage.getItem(SAVE_KEYS.seed); if (r != null && r != "") n = Json.ToNum(r); } catch { }
            if (!JS.isFinite(n))
            {
                n = randomWorldSeed();
                try { LocalStorage.setItem(SAVE_KEYS.seed, JS.toInt32(n).ToString()); } catch { }
            }
            WORLD_SEED = JS.toInt32(n);
            var a = loadWorldIndex(); var w = a.FirstOrDefault(q => q.id == CURRENT_WORLD_ID);
            if (w != null && (!w.seed.HasValue || w.seed.Value != WORLD_SEED)) { w.seed = WORLD_SEED; w.updatedAt = JS.dateNow(); persistWorldIndex(a); }
            return WORLD_SEED;
        }
        public static void clearCurrentSave()
        {
            if (GAME_MODE == null) return;
            try { foreach (var k in SAVE_KEYS.All()) LocalStorage.removeItem(k); } catch { }
        }
        public static bool deleteWorldRecord(string id)
        {
            var a = loadWorldIndex(); var w = a.FirstOrDefault(q => q.id == id);
            if (w == null) return false;
            var ns = w.legacy ? legacyNamespaceForMode(w.mode) : worldNamespace(w.id);
            var keys = worldKeysForNamespace(ns);
            try { foreach (var k in keys.All()) LocalStorage.removeItem(k); } catch { }
            persistWorldIndex(a.Where(q => q.id != id).ToList());
            return true;
        }
        public static string formatWorldTime(double t)
        {
            if (t == 0) return "старое сохранение";
            try { return DateTimeOffset.FromUnixTimeMilliseconds((long)t).LocalDateTime.ToString(); } catch { return ""; }
        }
        public static bool saveHeaderValid(JObj r)
        {
            return r != null && r.Num("saveSchema", -1) == SAVE_SCHEMA_VERSION && r.Num("worldVersion", -1) == SAVE_WORLD_VERSION && r.Get("generator") is string g && GENERATOR_COMPAT.Contains(g);
        }
        public static JObj saveHeader() { return new JObj { { "saveSchema", (double)SAVE_SCHEMA_VERSION }, { "worldVersion", (double)SAVE_WORLD_VERSION }, { "generator", GENERATOR_VERSION } }; }

        public static readonly HashSet<string> killedMobs = new HashSet<string>();
        public static void loadKilledMobs()
        {
            killedMobs.Clear();
            try
            {
                var r = Json.TryParse(LocalStorage.getItem(SAVE_KEYS.killed) ?? "null") as JObj;
                if (!saveHeaderValid(r) || r.Arr("ids") == null) return;
                foreach (var id in r.Arr("ids")) killedMobs.Add(id is double d ? JS.ToStr(d) : Convert.ToString(id));
            }
            catch { }
        }
        public static void saveKilledMobs()
        {
            if (SAVE_KEYS.killed == null) return;
            try { var o = saveHeader(); o["ids"] = killedMobs.Select(x => (object)x).ToList(); LocalStorage.setItem(SAVE_KEYS.killed, Json.Stringify(o)); } catch { }
        }
    }
}
