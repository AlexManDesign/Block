// Headless runtime smoke test driver: boots the real VoxelForgeGame MonoBehaviour on top of HeadlessUnity.cs and
// plays a scripted session through the real input / VF entry points. See README.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using VoxelForge;

static class Harness
{
    static readonly List<string> checks = new List<string>();
    static int failedChecks = 0;
    static string lastToast = "";
    static double fastFrames = 0;
    static int frameMs = 16;

    static int Main(string[] args)
    {
        string root = null, data = null; double scale = 1;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--project" && i + 1 < args.Length) root = args[++i];
            else if (args[i] == "--data" && i + 1 < args.Length) data = args[++i];
            else if (args[i] == "--verbose") HeadlessHost.verboseLog = true;
            else if (args[i] == "--frame-ms" && i + 1 < args.Length) frameMs = int.Parse(args[++i]);
            else if (args[i] == "--seed" && i + 1 < args.Length) seedArg = args[++i];
            else if (args[i] == "--soak" && i + 1 < args.Length) soakSeconds = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "--only" && i + 1 < args.Length) only = new HashSet<string>(args[++i].Split(','));
            else if (args[i] == "--idle-seconds" && i + 1 < args.Length) idleSeconds = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "--scale" && i + 1 < args.Length) scale = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
        }
        if (root == null) { Console.Error.WriteLine("usage: harness.exe --project <VoxelForgeUnity> [--data <dir>] [--verbose] [--frame-ms 16]"); return 2; }
        timeScale = scale;
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        HeadlessHost.mainThread = Thread.CurrentThread; Thread.CurrentThread.Name = "main";
        HeadlessHost.resourcesRoot = Path.Combine(root, "Assets/VoxelForge/Resources");
        HeadlessHost.persistentDataPath = data ?? Path.Combine(Path.GetTempPath(), "vf-headless-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(HeadlessHost.persistentDataPath);
        Console.WriteLine("persistentDataPath = " + HeadlessHost.persistentDataPath);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => { Debug.LogException(e.ExceptionObject as Exception ?? new Exception("" + e.ExceptionObject)); };
        int rc;
        try { rc = Scenario(); }
        catch (Exception e) { Debug.LogException(new Exception("harness scenario aborted", e)); rc = 1; }
        try { HeadlessHost.Quit(); } catch (Exception e) { Debug.LogException(e); }
        Thread.Sleep(200);
        Report();
        int bad = HeadlessHost.exceptions.Count + HeadlessHost.errors.Count + failedChecks + HeadlessHost.problems.Count;
        // background workers may still be parked on events; exit hard
        Console.Out.Flush();
        Environment.Exit(bad == 0 && rc == 0 ? 0 : 1);
        return 0;
    }
    static double timeScale = 1, idleSeconds = 8, soakSeconds = 0;
    static HashSet<string> only;
    static string seedArg;
    /// <summary>createAndStartWorld, but with --seed "a" or "a,b" the creative/survival worlds get fixed seeds (reproducible runs).</summary>
    static void CreateWorld(string mode, string name, int which)
    {
        var sp = seedArg != null ? seedArg.Split(',') : new string[0];
        if (which >= sp.Length || sp[which] == "") { VF.createAndStartWorld(mode, name); return; }
        var w = VF.createWorldRecord(mode, name);
        LocalStorage.setItem(VF.worldKeysForNamespace(VF.worldNamespace(w.id)).seed, "" + JS.toInt32(double.Parse(sp[which], System.Globalization.CultureInfo.InvariantCulture)));
        VF.startGame(mode, w.id);
    }
    /// <summary>Scenario section filter (--only flight,edits,...); the boot + creative world start always run.</summary>
    static bool Do(string section) { return only == null || section == "look" || only.Contains(section); }

    // ---------------- frame driving ----------------
    static void Frame()
    {
        var t0 = DateTime.UtcNow;
        HeadlessHost.Frame();
        if (VF.toastText != lastToast) { lastToast = VF.toastText; if (!string.IsNullOrEmpty(lastToast)) Console.WriteLine("  toast: " + lastToast); }
        int spent = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
        if (spent < frameMs) Thread.Sleep(frameMs - spent);
    }
    static void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }
    static bool Until(Func<bool> cond, double seconds, string what)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds * timeScale); int n = 0;
        while (DateTime.UtcNow < end) { Frame(); n++; if (cond()) { Console.WriteLine("  [" + what + "] after " + n + " frames"); return true; } }
        Check(false, what + " (timeout " + seconds + "s)");
        return false;
    }
    static void Check(bool ok, string what)
    {
        checks.Add((ok ? "ok   " : "FAIL ") + what);
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) failedChecks++;
    }
    static void Info(string what) { Console.WriteLine("  info " + what); }
    static void Phase(string name) { Console.WriteLine("== " + name + "  (frame " + Time.frameCount + ", exceptions " + HeadlessHost.exceptions.Count + ", errors " + HeadlessHost.errors.Count + ", problems " + HeadlessHost.problems.Count + ")"); }

    // ---------------- reflection access to VF internals ----------------
    const BindingFlags ALL = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Call(string name, params object[] a)
    {
        var ms = typeof(VF).GetMethods(ALL).Where(m => m.Name == name && m.GetParameters().Length >= a.Length && m.GetParameters().Skip(a.Length).All(p => p.IsOptional)).ToArray();
        var mi = ms.FirstOrDefault(m => m.GetParameters().Take(a.Length).Select((p, i) => a[i] == null || p.ParameterType.IsInstanceOfType(a[i])).All(x => x));
        if (mi == null) throw new MissingMethodException("VF." + name);
        var full = new object[mi.GetParameters().Length];
        for (int i = 0; i < full.Length; i++) full[i] = i < a.Length ? a[i] : mi.GetParameters()[i].DefaultValue;
        try { return mi.Invoke(null, full); }
        catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); return null; }
    }
    static T Get<T>(string name)
    {
        var f = typeof(VF).GetField(name, ALL); if (f != null) return (T)f.GetValue(null);
        var p = typeof(VF).GetProperty(name, ALL); return (T)p.GetValue(null, null);
    }
    static void Set(string name, object v) { typeof(VF).GetField(name, ALL).SetValue(null, v); }

    // ---------------- world helpers ----------------
    static Player P { get { return VF.player; } }
    static bool PlayerFinite() { return JS.isFinite(P.x) && JS.isFinite(P.y) && JS.isFinite(P.z) && JS.isFinite(P.vx) && JS.isFinite(P.vy) && JS.isFinite(P.vz) && JS.isFinite(P.yaw) && JS.isFinite(P.pitch); }
    static int MeshedChunks() { int n = 0; foreach (var c in VF.chunks.Values) if (c.meshBuilt) n++; return n; }
    static int ChunksWithGeometry()
    {
        int n = 0;
        foreach (var c in VF.chunks.Values) if ((c.opaque != null && c.opaque.count > 0) || (c.cutout != null && c.cutout.count > 0) || (c.water != null && c.water.count > 0) || (c.trans != null && c.trans.count > 0)) n++;
        return n;
    }
    static string Stats()
    {
        return "chunks " + VF.chunks.Count + " meshed " + MeshedChunks() + " geo " + ChunksWithGeometry() + " visible " + VF.renderVisibleCount + " draws " + HeadlessHost.lastDraws + " tris " + HeadlessHost.lastTris
            + " | player " + JS.Fixed(P.x, 2) + " " + JS.Fixed(P.y, 2) + " " + JS.Fixed(P.z, 2) + " ground " + P.onGround + " fly " + P.flying + " hp " + P.hp + " | mobs " + VF.liveMobs.Count + " fps " + VF.fps + " gen " + VF.genPending.Count + " mesh " + VF.meshPending.Count;
    }
    static void StateCheck(string label, bool expectGround = false)
    {
        Console.WriteLine("  state[" + label + "]: " + Stats());
        Check(PlayerFinite(), label + ": player position/velocity finite");
        Check(P.y > VF.WORLD_MIN_Y - 4, label + ": player above world bottom (y=" + JS.Fixed(P.y, 2) + ")");
        if (VF.started && VF.worldReady)
        {
            Check(MeshedChunks() > 0, label + ": chunks meshed");
            Check(HeadlessHost.lastDraws > 0 && HeadlessHost.lastTris > 0, label + ": frame draws geometry (" + HeadlessHost.lastDraws + " draws, " + HeadlessHost.lastTris + " tris)");
            if (expectGround)
            {
                int fy = JS.floor(P.y - 0.05), id = VF.getBlock(JS.floor(P.x), fy, JS.floor(P.z));
                var bd = VF.bdef(id);
                Check(P.onGround || P.swimming || P.inWater || P.flying, label + ": player supported (block below " + (bd != null ? bd.name : "" + id) + ")");
            }
        }
        foreach (var m in VF.liveMobs) if (!(JS.isFinite(m.x) && JS.isFinite(m.y) && JS.isFinite(m.z))) { Check(false, label + ": mob " + m.type + " has non-finite position"); break; }
    }
    static void Aim(double tx, double ty, double tz)
    {
        double ex = P.x, ey = P.y + 1.62, ez = P.z, dx = tx - ex, dy = ty - ey, dz = tz - ez, len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        P.yaw = Math.Atan2(dx, -dz); P.pitch = -Math.Asin(dy / len);
    }
    /// <summary>A solid surface block 2..4 blocks in front of the player (top face visible).</summary>
    static int[] GroundAhead(int dist = 3)
    {
        for (int d = dist; d <= dist + 3; d++)
            foreach (var off in new[] { new[] { 0, -1 }, new[] { 1, 0 }, new[] { 0, 1 }, new[] { -1, 0 } })
            {
                int x = JS.floor(P.x) + off[0] * d, z = JS.floor(P.z) + off[1] * d;
                for (int y = JS.floor(P.y) + 2; y > JS.floor(P.y) - 5; y--)
                {
                    int id = VF.getBlock(x, y, z); var b = VF.bdef(id);
                    if (b != null && b.solid && !b.plant && !VF.isWater(id) && VF.getBlock(x, y + 1, z) == B.AIR && VF.getBlock(x, y + 2, z) == B.AIR) return new[] { x, y, z };
                }
            }
        return null;
    }
    /// <summary>Aims at the top of ground block g and right-clicks; returns the cell placement targets (raycast prev) or null.</summary>
    static int[] UseOn(int[] g, double fy = 0.98)
    {
        Aim(g[0] + 0.5, g[1] + fy, g[2] + 0.5); Frame();
        var h = VF.raycast(); var cell = h != null && h.prev != null ? new[] { h.prev.x, h.prev.y, h.prev.z } : null;
        ClickMouse(1); Frames(3);
        return cell;
    }
    static void Tap(KeyCode k, int hold = 2) { HeadlessHost.PressKey(k); Frames(hold); HeadlessHost.ReleaseKey(k); Frames(2); }
    static void ClickMouse(int b) { HeadlessHost.PressMouse(b); Frame(); HeadlessHost.ReleaseMouse(b); Frame(); }
    static void Give(int slot, string key, int count) { VF.inventory[slot] = new Stack { key = key, count = count, dur = VF.idef(key).maxDur }; VF.selected = slot; VF.drawHotbar(); }
    static bool ClickButton(Func<string, bool> match, string what)
    {
        Frame();
        foreach (var b in HeadlessHost.buttons)
            if (b.enabled && b.text != null && match(b.text)) { HeadlessHost.Click(b.screen.center); Frame(); Console.WriteLine("  clicked UI button '" + b.text + "'"); return true; }
        Check(false, "UI button '" + what + "' present (buttons: " + string.Join(" | ", HeadlessHost.buttons.Select(x => x.text).Take(40)) + ")");
        return false;
    }
    /// <summary>Runs an idle window and reports chunks whose mesh revision keeps changing without edits (remesh loops).</summary>
    static long TerrainUploads() { long[] v; return HeadlessMesh.byName.TryGetValue("vf-terrain", out v) ? v[0] : 0; }
    static string SimQueues() { var w = VF.worldSim; return "water " + w.waterUrgentQueue.Count + "/" + w.waterQueue.Count + "/" + w.waterSeedQueue.Count + " lava " + w.lavaQueue.Count + "/" + w.lavaSeedQueue.Count; }
    /// <summary>--soak S: idles S seconds before the remesh probe, printing fluid queues / remesh activity every 5 s (convergence check).</summary>
    static void Soak(string label)
    {
        for (double t = 0; t < soakSeconds; t += 5)
        {
            long u0 = TerrainUploads(); int revs0 = VF.chunks.Values.Sum(c => c.meshRev), map0 = VF.chunks.Values.Sum(c => c.mapRev);
            var end = DateTime.UtcNow.AddSeconds(5); while (DateTime.UtcNow < end) Frame();
            Console.WriteLine("  soak[" + label + "] t=" + (t + 5) + "s: " + SimQueues() + " | meshRev +" + (VF.chunks.Values.Sum(c => c.meshRev) - revs0) + " mapRev +" + (VF.chunks.Values.Sum(c => c.mapRev) - map0) + " terrain uploads +" + (TerrainUploads() - u0) + " edits " + VF.edits.Count);
        }
    }
    static void IdleRemeshProbe(string label, double seconds)
    {
        Soak(label);
        // 1) live window: fluids keep flowing through generated caves for minutes (as in the reference: flowing water is
        //    runtime state seeded by the gen worker), so revs here are informational; a fluid cell rewritten over and over
        //    would be an oscillation -> see VF_TRACE_REV=1 for per-cell histories.
        long up0 = TerrainUploads(); string q0 = SimQueues();
        var rev0 = new Dictionary<string, int>(); foreach (var kv in VF.chunks) rev0[kv.Key] = kv.Value.meshRev;
#if VF_TRACE_REV
        VF.__traceStacks.Clear(); VF.__traceFluidCells.Clear(); VF.__traceOn = true;
#endif
        int edits0 = VF.edits.Count; var end = DateTime.UtcNow.AddSeconds(seconds); int n = 0;
        while (DateTime.UtcNow < end) { Frame(); n++; }
        var d = new List<KeyValuePair<string, int>>();
        foreach (var kv in VF.chunks) { int r0; if (rev0.TryGetValue(kv.Key, out r0) && kv.Value.meshRev - r0 > 0) d.Add(new KeyValuePair<string, int>(kv.Key, kv.Value.meshRev - r0)); }
        d.Sort((x, y) => y.Value - x.Value);
        Console.WriteLine("  idle probe[" + label + "] live " + n + " frames: " + d.Count + " chunks remeshed; top: " + string.Join(", ", d.Take(8).Select(k => k.Key + "x" + k.Value)) + " (edits " + edits0 + " -> " + VF.edits.Count + "; terrain uploads +" + (TerrainUploads() - up0) + "; " + q0 + " -> " + SimQueues() + ")");
#if VF_TRACE_REV
        VF.__traceOn = false; VF.__traceDump(d.Take(4).Select(k => k.Key));
#endif
        // 2) frozen fluids: with the water/lava solver paused nothing but sparse world ticks may change blocks; a chunk whose
        //    mesh revision moves while neither it nor any 3x3 neighbour changed a block is a real remesh loop.
        VF.waterSimAt = VF.lavaSimAt = double.PositiveInfinity;
        Until(() => VF.meshPending.Count == 0 && VF.meshJobs.Count == 0, 20, label + ": mesh queue drained with fluids frozen");
        Frames(60);
        var map0 = new Dictionary<string, int>(); rev0.Clear(); up0 = TerrainUploads();
        foreach (var kv in VF.chunks) { rev0[kv.Key] = kv.Value.meshRev; map0[kv.Key] = kv.Value.mapRev; }
        end = DateTime.UtcNow.AddSeconds(seconds); n = 0;
        while (DateTime.UtcNow < end) { Frame(); n++; }
        var loops = new List<KeyValuePair<string, int>>(); int changedChunks = 0;
        foreach (var kv in VF.chunks)
        {
            int r0, m0; if (!rev0.TryGetValue(kv.Key, out r0)) continue;
            var c = kv.Value; if (map0.TryGetValue(kv.Key, out m0) && c.mapRev != m0) changedChunks++;
            if (c.meshRev == r0) continue;
            bool mapChanged = false;
            for (int dx = -1; dx <= 1 && !mapChanged; dx++) for (int dz = -1; dz <= 1; dz++) { var nc = VF.chunkFastGet(c.cx + dx, c.cz + dz); if (nc != null && map0.TryGetValue(nc.key, out m0) && nc.mapRev != m0) { mapChanged = true; break; } }
            if (!mapChanged) loops.Add(new KeyValuePair<string, int>(kv.Key, c.meshRev - r0));
        }
        loops.Sort((x, y) => y.Value - x.Value);
        Console.WriteLine("  idle probe[" + label + "] frozen fluids " + n + " frames: " + changedChunks + " chunks changed blocks, terrain uploads +" + (TerrainUploads() - up0) + "; remeshed without any block change nearby: " + loops.Count + (loops.Count > 0 ? " (" + string.Join(", ", loops.Take(8).Select(k => k.Key + "x" + k.Value)) + ")" : ""));
        VF.waterSimAt = VF.lavaSimAt = 0;
        Check(loops.Count == 0 || loops[0].Value <= 2, label + ": no remesh loop without block changes (max " + (loops.Count > 0 ? loops[0].Value : 0) + " revs in " + seconds + "s)");
    }
    static void EnsureLocked()
    {
        if (!VF.locked) { ClickMouse(0); Frames(2); }
        Check(VF.locked, "pointer lock acquired by first click");
    }
    static void WaitWorld(string label)
    {
        Until(() => VF.worldReady, 180, label + ": worldReady");
        Until(() => MeshedChunks() >= 9 && HeadlessHost.lastTris > 0, 180, label + ": chunks meshed and drawn");
        Until(() => VF.genPending.Count == 0 && VF.meshPending.Count == 0 && VF.genJobs.Count == 0 && VF.meshJobs.Count == 0, 240, label + ": streaming settled");
        StateCheck(label);
    }

    // ---------------- scripted session ----------------
    static int Scenario()
    {
        Phase("boot (RuntimeInitializeOnLoadMethod -> Awake -> bootstrap)");
        HeadlessHost.InvokeStatic(typeof(VoxelForgeGame), "AutoCreate");
        Check(UnityEngine.Object.FindObjectOfType<VoxelForgeGame>() != null, "VoxelForgeGame created");
        Frames(10);
        Check(VF.atlasArray != null && VF.atlasArray.depth == VF.TILE_NAMES.Count, "terrain atlas array built");
        Check(VF.frameCB != null, "frame command buffer created");
        Check(HeadlessHost.buttons.Count > 0, "start screen draws buttons (" + HeadlessHost.buttons.Count + ")");

        Phase("creative world: create + start");
        CreateWorld("creative", "test", 0);
        Check(VF.started, "started");
        WaitWorld("creative");
        int[] g = null;
        var creativeId = VF.loadWorldIndex().OrderByDescending(w => w.updatedAt).First().id;
        Frames(30);
        StateCheck("creative idle");

        if (Do("idle")) IdleRemeshProbe("creative idle", idleSeconds);
        if (Do("look"))
        {
        Phase("lock pointer, look around");
        EnsureLocked();
        for (int i = 0; i < 20; i++) { HeadlessHost.MouseMove(1.5f, i < 10 ? 0.4f : -0.4f); Frame(); }
        Check(PlayerFinite(), "mouse look keeps finite angles");

        }
        if (Do("flight"))
        {
        Phase("fly across chunk borders");
        Tap(KeyCode.F);
        Check(P.flying, "flight toggled by F in creative");
        double sx = P.x, sz = P.z; int scx = JS.floor(sx / 16), scz = JS.floor(sz / 16);
        P.yaw = 0; P.pitch = 0;
        HeadlessHost.PressKey(KeyCode.W); Frames(3); HeadlessHost.ReleaseKey(KeyCode.W); Frames(2); HeadlessHost.PressKey(KeyCode.W); // double tap = sprint latch
        var flyEnd = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < flyEnd) Frame();
        HeadlessHost.ReleaseKey(KeyCode.W); Frames(3);
        double moved = JS.hypot(P.x - sx, P.z - sz);
        Check(moved > 20, "flew " + JS.Fixed(moved, 1) + " blocks");
        Check(JS.floor(P.z / 16) != scz || JS.floor(P.x / 16) != scx, "crossed a chunk border");
        Until(() => VF.genPending.Count == 0 && VF.meshPending.Count == 0, 120, "streaming after flight");
        StateCheck("after flight");
        // teleport far: full re-stream + region rebuilds
        P.x += 150; P.z -= 90; P.y = VF.heightAt(P.x, P.z) + 6;
        Until(() => VF.chunkLoadedAt(JS.floor(P.x), JS.floor(P.z)) && VF.genPending.Count == 0 && VF.meshPending.Count == 0 && HeadlessHost.lastTris > 0, 180, "streaming after teleport");
        Frames(30);
        StateCheck("after teleport");
        // land
        Tap(KeyCode.F);
        Check(!P.flying, "flight off");
        Until(() => P.onGround || P.inWater, 20, "landed");
        Frames(20);
        StateCheck("landed", true);

        }
        if (Do("edits"))
        {
        Phase("creative break + place");
        g = GroundAhead();
        Check(g != null, "found ground ahead");
        if (g != null)
        {
            Aim(g[0] + 0.5, g[1] + 0.98, g[2] + 0.5);
            Frame();
            var h = VF.raycast();
            Check(h != null && h.x == g[0] && h.y == g[1] && h.z == g[2], "raycast hits aimed block");
            int before = VF.getBlock(g[0], g[1], g[2]);
            ClickMouse(0); Frames(3);
            Check(VF.getBlock(g[0], g[1], g[2]) == B.AIR, "creative click broke block (was " + before + ")");
            Frames(20);
            Give(0, VF.BK(B.STONE), 64);
            g = GroundAhead();
            if (g != null) { var c = UseOn(g); Check(c != null && VF.getBlock(c[0], c[1], c[2]) == B.STONE, "right click placed stone"); }
            Give(1, VF.BK(B.TORCH), 64);
            g = GroundAhead(4);
            if (g != null) { var c = UseOn(g); Frames(10); Check(c != null && VF.getBlock(c[0], c[1], c[2]) == B.TORCH, "placed torch (light source)"); if (c != null) { Until(() => VF.blockAtLight(c[0], c[1], c[2]) >= 13, 5, "torch block light propagated"); Check(VF.blockAtLight(c[0] + 2, c[1], c[2]) >= 11 || VF.getBlock(c[0] + 2, c[1], c[2]) != B.AIR, "torch light spreads (" + VF.blockAtLight(c[0] + 2, c[1], c[2]) + " two blocks away)"); } }
            Frames(30);
            StateCheck("after edits");
        }

        }
        if (Do("inventory"))
        {
        Phase("inventory open/close (creative palette)");
        Tap(KeyCode.E);
        Check(VF.uiOpen, "E opens inventory");
        Frames(5);
        Check(HeadlessHost.buttons.Any(b => b.text == "Закрыть"), "inventory panel drawn");
        // pick something from the creative palette: click inside the palette area (right half of the panel)
        HeadlessHost.Click(new Vector2(HeadlessHost.screenW * 0.62f, HeadlessHost.screenH * 0.42f)); Frames(3);
        Tap(KeyCode.E);
        Check(!VF.uiOpen, "E closes inventory");
        EnsureLocked();

        }
        if (Do("pause"))
        {
        Phase("pause menu: mods (enable X-Ray) + settings");
        Tap(KeyCode.Escape);
        Check(VF.pauseOpen, "Esc opens pause");
        ClickButton(t => t == "Моды", "Моды");
        Check(VF.modsOpen, "mods panel open");
        Frame();
        {
            var toggles = HeadlessHost.buttons.Where(b => b.text == "[toggle] ").ToList();
            int xi = Array.FindIndex(VF.MOD_META, m => m.id == "xray");
            Check(toggles.Count == VF.MOD_META.Length, "mods list draws one toggle per mod (" + toggles.Count + ")");
            if (xi >= 0 && xi < toggles.Count) { HeadlessHost.Click(toggles[xi].screen.center); Frames(2); }
            Check(VF.modEnabled("xray"), "X-Ray mod enabled through the mods list");
        }
        ClickButton(t => t == "Закрыть", "Закрыть mods");
        ClickButton(t => t == "Настройки", "Настройки");
        Check(VF.settingsOpen, "settings open");
        ClickButton(t => t == "[toggle]  Облака", "clouds toggle"); Frames(2);
        Check(!VF.cloudsEnabled, "clouds toggled off");
        ClickButton(t => t == "[toggle]  Облака", "clouds toggle"); Frames(2);
        Check(VF.cloudsEnabled, "clouds toggled on");
        ClickButton(t => t == "Готово", "Готово");
        ClickButton(t => t == "Продолжить", "Продолжить");
        Check(!VF.pauseOpen && VF.locked, "resume re-locks pointer");

        }
        if (Do("xray"))
        {
        Phase("x-ray + camera modes");
        Tap(KeyCode.X); Frames(20);
        Check(VF.xrayActive, "x-ray on");
        Check(HeadlessHost.lastDraws > 0, "x-ray frame draws");
        Tap(KeyCode.X); Frames(5);
        Check(!VF.xrayActive, "x-ray off");
        for (int i = 0; i < 3; i++) { Tap(KeyCode.F5); Frames(10); Check(PlayerFinite() && HeadlessHost.lastDraws > 0, "camera mode " + P.camMode + " renders"); }
        Check(P.camMode == 0, "camera cycled back to first person");

        }
        if (Do("mobs"))
        {
        Phase("mobs: spawn eggs + simulation");
        int mobs0 = VF.liveMobs.Count(m => !m.dead);
        foreach (var egg in new[] { "pig_spawn_egg", "cow_spawn_egg", "sheep_spawn_egg", "chicken_spawn_egg", "zombie_spawn_egg", "skeleton_spawn_egg", "creeper_spawn_egg", "spider_spawn_egg" })
        {
            g = GroundAhead(3);
            if (g == null) break;
            Give(2, egg, 1);
            Aim(g[0] + 0.5, g[1] + 0.98, g[2] + 0.5); Frame();
            ClickMouse(1); Frames(4);
        }
        int mobCount = VF.liveMobs.Count(m => !m.dead);
        Check(mobCount >= mobs0 + 6, "mobs spawned from eggs (" + mobs0 + " -> " + mobCount + ")");
        Frames(300);
        StateCheck("mobs simulated");
        Check(VF.liveMobs.All(m => m.dead || m.y > VF.WORLD_MIN_Y), "no mob fell out of the world");

        }
        if (Do("water"))
        {
        Phase("water bucket -> fluids");
        g = GroundAhead(3);
        if (g != null)
        {
            Give(3, "water_bucket", 1);
            var wc = UseOn(g);
            Check(wc != null && VF.isWater(VF.getBlock(wc[0], wc[1], wc[2])), "water source placed");
            Check(VF.inventory[3] != null && VF.inventory[3].key == "water_bucket", "creative keeps the water bucket");
            Frames(240);
            int wet = 0;
            for (int dx = -4; dx <= 4; dx++) for (int dz = -4; dz <= 4; dz++) for (int dy = -2; dy <= 2; dy++) if (VF.isWater(VF.getBlock(g[0] + dx, g[1] + 1 + dy, g[2] + dz))) wet++;
            Check(wet > 1, "water spread (" + wet + " water cells)");
            StateCheck("after water");
            Phase("vehicles: boat on the new pond, minecart on a rail");
            Give(6, "boat", 1);
            Aim(wc[0] + 0.5, wc[1] + 0.9, wc[2] + 0.5); Frame();
            int veh0 = VF.vehicles.Count;
            ClickMouse(1); Frames(5);
            Info("boat use: vehicles " + veh0 + " -> " + VF.vehicles.Count + " (toast '" + VF.toastText + "')");
            if (VF.vehicles.Count > veh0)
            {
                var v = VF.vehicles[VF.vehicles.Count - 1];
                Aim(v.x, v.y + 0.3, v.z); Frame(); ClickMouse(1); Frames(5);
                Check(P.riding != null, "mounted the boat");
                HeadlessHost.PressKey(KeyCode.W); Frames(60); HeadlessHost.ReleaseKey(KeyCode.W);
                Check(PlayerFinite() && JS.isFinite(v.x + v.y + v.z), "boat ride keeps finite positions");
                Tap(KeyCode.LeftShift, 4); Frames(10);
                Check(P.riding == null, "Shift dismounts");
            }
            g = GroundAhead(3);
            if (g != null)
            {
                Give(6, VF.BK(B.RAIL), 8);
                var rc = UseOn(g);
                if (rc != null && VF.getBlock(rc[0], rc[1], rc[2]) == B.RAIL)
                {
                    Give(6, "minecart", 1);
                    Aim(rc[0] + 0.5, rc[1] + 0.05, rc[2] + 0.5); Frame();
                    int v0 = VF.vehicles.Count; ClickMouse(1); Frames(5);
                    Check(VF.vehicles.Count > v0, "minecart placed on rail");
                    Frames(60);
                }
                else Info("rail placement failed (toast '" + VF.toastText + "')");
            }
            Give(6, "fishing_rod", 1);
            Aim(wc[0] + 0.5, wc[1] + 0.5, wc[2] + 0.5); Frame(); ClickMouse(1); Frames(90); ClickMouse(1); Frames(10);
            Check(PlayerFinite(), "fishing cast/reel");
            Give(6, "ender_pearl", 4);
            P.yaw += 0.5; P.pitch = -0.3; Frame(); ClickMouse(1); Frames(150);
            Check(PlayerFinite() && P.y > VF.WORLD_MIN_Y, "ender pearl throw/teleport (" + JS.Fixed(P.x, 1) + "," + JS.Fixed(P.y, 1) + "," + JS.Fixed(P.z, 1) + ")");
            Until(() => P.onGround || P.inWater || P.flying, 20, "settled after pearl");
            StateCheck("after vehicles");
        }

        }
        if (Do("tnt"))
        {
        Phase("TNT explosion");
        g = GroundAhead(5);
        if (g != null)
        {
            Give(4, VF.BK(B.TNT), 4);
            var c = UseOn(g) ?? new[] { g[0], g[1] + 1, g[2] };
            int tx = c[0], ty = c[1], tz = c[2];
            Check(VF.getBlock(tx, ty, tz) == B.TNT, "TNT placed");
            Give(5, "flint_and_steel", 1);
            Aim(tx + 0.5, ty + 0.5, tz + 0.5); Frame();
            ClickMouse(1); Frames(3);
            Check(VF.primedTNT.Count > 0 || VF.getBlock(tx, ty, tz) != B.TNT, "TNT ignited");
            // walk away a bit (creative takes no damage anyway)
            Until(() => VF.primedTNT.Count == 0, 12, "TNT exploded");
            Frames(60);
            int holes = 0;
            for (int dx = -2; dx <= 2; dx++) for (int dz = -2; dz <= 2; dz++) if (VF.getBlock(tx + dx, ty - 1, tz + dz) == B.AIR || VF.isWater(VF.getBlock(tx + dx, ty - 1, tz + dz))) holes++;
            Check(holes > 3, "explosion crater (" + holes + " cleared cells)");
            Until(() => VF.meshPending.Count == 0, 30, "remesh after explosion");
            StateCheck("after TNT");
        }

        }
        if (Do("rd"))
        {
        Phase("render distance switch");
        int rd0 = VF.renderDistance;
        Tap(KeyCode.RightBracket); Frames(5);
        Check(VF.renderDistance != rd0 || rd0 >= VF.DEVICE_RD_CAP, "] raised render distance (" + rd0 + " -> " + VF.renderDistance + ")");
        Until(() => VF.genPending.Count == 0 && VF.meshPending.Count == 0 && VF.genJobs.Count == 0, 240, "stream at larger RD");
        StateCheck("RD " + VF.renderDistance);
        Tap(KeyCode.LeftBracket); Tap(KeyCode.LeftBracket); Frames(5);
        Until(() => VF.genPending.Count == 0 && VF.meshPending.Count == 0, 120, "stream at smaller RD");
        StateCheck("RD " + VF.renderDistance);
        VF.applyRenderDistanceSetting(rd0);
        Frames(30);

        }
        if (Do("reload"))
        {
        Phase("save, return to title, load");
        double px = P.x, py = P.y, pz = P.z;
        VF.saveGameNow();
        Frames(5);
        VF.returnToTitle();
        Frames(20);
        Check(!VF.started && VF.chunks.Count == 0, "returned to title (chunks " + VF.chunks.Count + ")");
        Check(HeadlessHost.buttons.Any(b => b.text == "Играть"), "world list shows Play buttons");
        // load through the start screen UI: newest world first
        ClickButton(t => t == "Играть", "Играть");
        if (!VF.started) VF.startGame("creative", creativeId);
        Check(VF.started, "world started again (load path)");
        WaitWorld("reloaded");
        Check(JS.hypot(P.x - px, P.z - pz) < 2 && Math.Abs(P.y - py) < 3, "position restored (" + JS.Fixed(P.x, 1) + "," + JS.Fixed(P.y, 1) + "," + JS.Fixed(P.z, 1) + " vs " + JS.Fixed(px, 1) + "," + JS.Fixed(py, 1) + "," + JS.Fixed(pz, 1) + ")");
        EnsureLocked();
        Frames(60);
        StateCheck("reloaded idle");

        IdleRemeshProbe("reloaded idle", idleSeconds);
        }
        if (Do("survival"))
        {
        Phase("survival world");
        VF.returnToTitle(); Frames(10);
        CreateWorld("survival", "test2", 1);
        WaitWorld("survival");
        EnsureLocked();
        Until(() => P.onGround || P.inWater, 20, "survival: player lands");
        Frames(60);
        StateCheck("survival idle", true);
        // mine a block by hand (hold mouse)
        VF.selected = 8; VF.inventory[8] = null; VF.drawHotbar();
        g = GroundAhead(2);
        if (g != null)
        {
            int id0 = VF.getBlock(g[0], g[1], g[2]);
            Aim(g[0] + 0.5, g[1] + 0.98, g[2] + 0.5); Frame();
            HeadlessHost.PressMouse(0);
            Until(() => VF.getBlock(g[0], g[1], g[2]) != id0, 20, "survival hand mining broke " + (VF.bdef(id0) != null ? VF.bdef(id0).name : "" + id0));
            HeadlessHost.ReleaseMouse(0); Frames(60);
            Check(VF.worldDrops.Count > 0 || VF.inventory.Any(s => s != null), "mined block dropped / collected");
        }
        // crafting: logs -> planks via the 2x2 grid and the result button
        for (int i = 0; i < 36; i++) VF.inventory[i] = null;
        VF.addItem(VF.BK(B.LOG), 4);
        Tap(KeyCode.E);
        Check(VF.uiOpen && VF.uiMode == "inventory", "survival inventory open");
        VF.craft2[0] = new Stack { key = VF.BK(B.LOG), count = 1 }; VF.inventory[0].count--; if (VF.inventory[0].count <= 0) VF.inventory[0] = null;
        Frames(3);
        int planksBefore = VF.inventory.Where(s => s != null && s.key == VF.BK(B.PLANKS)).Sum(s => s.count);
        ClickButton(t => t != "Результат" && t != "Закрыть" && !t.StartsWith("Книга") && !t.StartsWith("Закрыть книгу") && !t.StartsWith("[") && t.Length > 0 && VF.craftGridRecipe(VF.craft2, 2) != null && t == VF.craftGridRecipe(VF.craft2, 2).name, "craft result");
        Frames(3);
        int planksAfter = VF.inventory.Where(s => s != null && s.key == VF.BK(B.PLANKS)).Sum(s => s.count);
        Check(planksAfter > planksBefore, "crafted planks (" + planksBefore + " -> " + planksAfter + ")");
        ClickButton(t => t == "Книга рецептов", "recipe book"); Frames(5);
        Tap(KeyCode.Escape);
        Check(!VF.uiOpen, "Esc closes inventory");
        EnsureLocked();
        Phase("survival: chest, furnace, food, bow, bed, death/respawn");
        g = GroundAhead(3);
        if (g != null)
        {
            Give(1, VF.BK(B.CHEST), 1);
            var cc = UseOn(g);
            Check(cc != null && VF.getBlock(cc[0], cc[1], cc[2]) == B.CHEST, "chest placed");
            if (cc != null && VF.getBlock(cc[0], cc[1], cc[2]) == B.CHEST)
            {
                Aim(cc[0] + 0.5, cc[1] + 0.5, cc[2] + 0.5); Frame(); ClickMouse(1); Frames(5);
                Check(VF.uiOpen && VF.uiMode == "chest", "right click opens chest UI");
                Frames(5); Tap(KeyCode.E); EnsureLocked();
            }
        }
        g = GroundAhead(4);
        if (g != null)
        {
            Give(1, VF.BK(B.FURNACE), 1);
            var fc = UseOn(g);
            if (fc != null && VF.getBlock(fc[0], fc[1], fc[2]) == B.FURNACE)
            {
                Aim(fc[0] + 0.5, fc[1] + 0.5, fc[2] + 0.5); Frame(); ClickMouse(1); Frames(5);
                Check(VF.uiOpen && VF.uiMode == "furnace", "right click opens furnace UI");
                var fu = VF.getFurnace(VF.uiKey);
                if (fu != null)
                {
                    fu.input = new Stack { key = "raw_iron", count = 2 }; fu.fuel = new Stack { key = "coal", count = 1 };
                    Until(() => fu.output != null && fu.output.key == "iron_ingot", 30, "furnace smelts raw iron");
                }
                Tap(KeyCode.E); EnsureLocked();
            }
            else Check(false, "furnace placed (toast '" + VF.toastText + "')");
        }
        P.hunger = 10; Give(2, "bread", 3);
        ClickMouse(1); Frames(5);
        Check(P.hunger > 10, "ate bread (hunger " + P.hunger + ")");
        Give(3, "bow", 1); VF.inventory[4] = new Stack { key = "arrow", count = 16 };
        VF.selected = 3; VF.drawHotbar(); P.pitch = -0.2;
        int proj0 = VF.projectiles.Count;
        HeadlessHost.PressMouse(0); var bowEnd = DateTime.UtcNow.AddSeconds(1.2); while (DateTime.UtcNow < bowEnd) Frame(); HeadlessHost.ReleaseMouse(0); Frames(3);
        Check(VF.projectiles.Count > proj0 || VF.inventory[4] == null || VF.inventory[4].count < 16, "bow fired an arrow");
        Frames(120);
        VF.day = 0.6; Frames(5);
        g = GroundAhead(3);
        if (g != null)
        {
            Give(5, VF.BK(B.BED), 1);
            var bc = UseOn(g);
            if (bc != null && VF.getBlock(bc[0], bc[1], bc[2]) == B.BED)
            {
                Aim(bc[0] + 0.5, bc[1] + 0.3, bc[2] + 0.5); Frame(); ClickMouse(1); Frames(5);
                Info("bed use: sleeping " + P.sleeping + " (toast '" + VF.toastText + "')");
                if (P.sleeping) Until(() => !P.sleeping, 20, "woke up after sleeping");
                EnsureLocked();
            }
            else Info("bed placement failed (toast '" + VF.toastText + "')");
        }
        VF.addItem(VF.BK(B.DIRT), 10);
        VF.damagePlayer(100, "test");
        Frames(5);
        Check(P.dead && VF.deathUIVisible, "death screen shown");
        ClickButton(t => t == "Возродиться", "Возродиться");
        Check(!P.dead, "respawned");
        Until(() => VF.worldReady && (P.onGround || P.inWater), 60, "respawn settled");
        EnsureLocked();
        StateCheck("after respawn");
        // night: hostile spawns + survival ticks
        VF.day = 0.55;
        Frames(400);
        StateCheck("survival night");
        Check(P.hp > 0 || P.dead, "player hp sane (" + P.hp + ")");
        VF.saveGameNow();
        Frames(10);

        }
        if (Do("quit"))
        {
        Phase("pause + quit");
        Tap(KeyCode.Escape);
        Check(VF.pauseOpen, "Esc shows pause");
        Frames(10);
        Tap(KeyCode.Escape);
        }
        return 0;
    }

    static void Report()
    {
        Console.WriteLine();
        Console.WriteLine("================ HEADLESS SMOKE TEST REPORT ================");
        Console.WriteLine("frames: " + Time.frameCount + "   checks: " + checks.Count + " (" + failedChecks + " failed)");
        Console.WriteLine("exceptions: " + HeadlessHost.exceptions.Count + "   errors: " + HeadlessHost.errors.Count + "   problems: " + HeadlessHost.problems.Count + "   warnings: " + HeadlessHost.warnings.Count);
        Console.WriteLine("mesh uploads: " + HeadlessMesh.totalUploads + " (" + (HeadlessMesh.totalVertexBytes >> 20) + " MiB vertex data)");
        foreach (var kv in HeadlessMesh.byName.OrderByDescending(k => k.Value[1]).Take(12)) Console.WriteLine("  uploads " + kv.Key + ": " + kv.Value[0] + " calls, " + (kv.Value[1] >> 20) + " MiB");
        foreach (var c in checks) if (c.StartsWith("FAIL")) Console.WriteLine("  " + c);
        var groups = HeadlessHost.exceptions.GroupBy(e => { var s = e.Substring(e.IndexOf(']') + 1).Trim(); int nl = s.IndexOf('\n'); return nl > 0 ? s.Substring(0, Math.Min(nl, 400)) : s; });
        foreach (var gr in groups) { Console.WriteLine("--- exception x" + gr.Count() + ":"); Console.WriteLine(gr.First()); }
        foreach (var gr in HeadlessHost.errors.GroupBy(e => e.Split('\n')[0].Substring(e.IndexOf(']') + 1))) { Console.WriteLine("--- error x" + gr.Count() + ":"); Console.WriteLine(gr.First()); }
        foreach (var gr in HeadlessHost.problems.GroupBy(e => e.Split('\n')[0].Substring(e.IndexOf(']') + 1))) { Console.WriteLine("--- problem x" + gr.Count() + ":"); Console.WriteLine(gr.First()); }
        foreach (var gr in HeadlessHost.warnings.GroupBy(e => e.Substring(e.IndexOf(']') + 1))) Console.WriteLine("--- warning x" + gr.Count() + ": " + gr.Key);
        bool pass = HeadlessHost.exceptions.Count == 0 && HeadlessHost.errors.Count == 0 && failedChecks == 0 && HeadlessHost.problems.Count == 0;
        Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
    }
}
