// Entry point of the Unity build (the parts of game.js that run the world view): resources and data
// tables, the world with its worker threads, column streaming, light passes, mesh submission and upload,
// the day cycle and the environment, and the frame. Started automatically in any scene.
//
// Until player.js is ported the camera flies freely: WASD / Space / Shift, Ctrl faster, mouse to look
// (click to capture the mouse, Esc to release). F3 debug info, +/- render distance, T skips an hour
// of day time, C clouds, L leaf mode, O occlusion culling, F6 culls the other face side.
using System;
using System.Collections.Generic;
using UnityEngine;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed class GameMain : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindObjectOfType<GameMain>() != null) return;
            new GameObject("Maincraft").AddComponent<GameMain>();
        }

        const double DAY_LEN = 1200;
        // leaf mesh modes (mesher: 0 fast, 1 optimized, 2 fancy, 3 shell) from coarse to fine
        static readonly byte[] LEAF_RANK = { 0, 2, 3, 1 };
        static readonly Dictionary<string, int> WATER_FOG = new Dictionary<string, int>
        {
            ["WARM_OCEAN"] = 0x041F33, ["LUKEWARM_OCEAN"] = 0x041633, ["DEEP_LUKEWARM_OCEAN"] = 0x041633, ["SWAMP"] = 0x232317, ["MANGROVE_SWAMP"] = 0x4D7A60,
        };

        public int seed = 12345;
        World world;
        Renderer r;
        Camera cam;
        readonly Env env = new Env();
        int[] spiral;                               // dx, dz, dx² + dz² per entry, nearest first
        readonly Queue<(Column c, int sy, MeshResult d)> meshQueue = new Queue<(Column, int, MeshResult)>();
        readonly List<Column> tmpCols = new List<Column>();
        readonly List<Column> litList = new List<Column>();
        long litIdleAt = -1;
        int frameN;
        double time = 0.25, prevTime = 0.25, acc, fovCur;
        int days;
        bool loadingWorld = true, needSpawnDrop = true, cullFlip;
        string error;

        // free camera (feet position; the eye is 1.62 above)
        double px = 0.5, py = 100, pz = 0.5, yaw, pitch;
        const double EYE = 1.62;
        int waterVisT;

        // fps / debug
        bool debug = true;
        double fpsAcc; int fpsN, fps;
        double tStream, tUpload, tRender;

        void Start()
        {
            try { Init(); }
            catch (Exception e) { error = e.ToString(); Debug.LogException(e); }
        }

        void Init()
        {
            Application.targetFrameRate = -1;
            cam = Camera.main;
            if (cam == null) { var go = new GameObject("Camera"); cam = go.AddComponent<Camera>(); go.tag = "MainCamera"; }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.allowHDR = false; cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.depthTextureMode = DepthTextureMode.None;
            foreach (var l in FindObjectsOfType<Light>()) l.enabled = false;
            RenderSettings.fog = false;
            RenderSettings.ambientLight = Color.black;

            // data tables of the shared modules, then the textures they refer to by name
            Blocks.Init();
            StructData.Load(Text("Data/struct_tpl"), Text("Data/jig_str"), Text("Data/jig_pools"), Text("Data/jig_tpl"));
            string metaJson = Text("Data/atlas_meta");
            var meta = (Dictionary<string, object>)MiniJson.Parse(metaJson);
            var layers = new Dictionary<string, int>();
            foreach (var kv in (Dictionary<string, object>)meta["layers"]) layers[kv.Key] = MiniJson.I(kv.Value);
            Blocks.ResolveBlockTextures(layers);
            Mesher.InitMesherTextures(layers);

            r = new Renderer(cam, Bytes("Textures/atlas.png"), metaJson, Bytes("Textures/sun.png"), Bytes("Textures/moon.png"),
                name => Resources.Load<TextAsset>("Textures/Entities/" + name + ".png")?.bytes);
            r.BuildClouds(seed);

            // column offsets around the player, nearest first
            var sp = new List<(int dx, int dz, int d2)>();
            for (int dz = -40; dz <= 40; dz++) for (int dx = -40; dx <= 40; dx++) sp.Add((dx, dz, dx * dx + dz * dz));
            sp.Sort((a, b) => a.d2.CompareTo(b.d2));   // stable enough: equal distances keep no particular order in the original either
            spiral = new int[sp.Count * 3];
            for (int i = 0; i < sp.Count; i++) { spiral[i * 3] = sp[i].dx; spiral[i * 3 + 1] = sp[i].dz; spiral[i * 3 + 2] = sp[i].d2; }

            int workers = Math.Max(1, Math.Min(SystemInfo.processorCount - 1, 8));
            world = new World(seed, workers);
            world.onMeshResult = (c, sy, d) => meshQueue.Enqueue((c, sy, d));
            world.onColumnUnload = c => r.FreeColumn(c);
        }

        static string Text(string path)
        {
            var t = Resources.Load<TextAsset>(path);
            if (t == null) throw new Exception("missing resource " + path);
            return t.text;
        }
        static byte[] Bytes(string path)
        {
            var t = Resources.Load<TextAsset>(path);
            if (t == null) throw new Exception("missing resource " + path);
            return t.bytes;
        }

        void OnDestroy()
        {
            world?.Dispose();
            r?.Dispose();
        }

        // ------------------------------------------------------------------ streaming
        void Stream()
        {
            var w = world;
            if (!w.ready) return;
            int pcx = (int)Math.Floor(px) >> 4, pcz = (int)Math.Floor(pz) >> 4;
            int R = Math.Min(Settings.renderDist, 27), L = R + 3; // +3: lit needs sky-init neighbours, meshing needs lit neighbours
            int maxGen = w.pool.count * 3;
            int LL = (int)Math.Floor((L + 0.5) * (L + 0.5)), RR = (int)Math.Floor((R + 0.5) * (R + 0.5)), UL = (int)Math.Floor((L + 2.5) * (L + 2.5));
            // request columns
            var SP = spiral;
            for (int i = 0; i < SP.Length; i += 3)
            {
                if (SP[i + 2] > LL) break;
                if (w.genInFlight >= maxGen) break;
                int cx = pcx + SP[i], cz = pcz + SP[i + 1];
                if (w.Col(cx, cz) == null) w.RequestColumn(cx, cz);
            }
            // unload far columns
            if ((frameN & 31) == 0)
            {
                tmpCols.Clear();
                foreach (var c in w.cols.Values)
                {
                    int dx = c.cx - pcx, dz = c.cz - pcz;
                    if (dx * dx + dz * dz > UL) tmpCols.Add(c);
                }
                foreach (var c in tmpCols) w.UnloadColumn(c);
            }
            // light passes (budgeted). Columns wait here until their neighbours exist; a full pass that lit
            // nothing is not repeated until another column has arrived.
            double t0 = Renderer.Now;
            if (w.pendingLit.Count > 0 && litIdleAt != w.statGen)
            {
                litList.Clear(); litList.AddRange(w.pendingLit);
                litList.Sort((a, b) => ((a.cx - pcx) * (a.cx - pcx) + (a.cz - pcz) * (a.cz - pcz)).CompareTo((b.cx - pcx) * (b.cx - pcx) + (b.cz - pcz) * (b.cz - pcz)));
                bool lit = false, cut = false;
                foreach (var c in litList)
                {
                    if (Renderer.Now - t0 > 3.5) { cut = true; break; }
                    if (c.state != 1) { w.pendingLit.Remove(c); continue; }
                    if (w.LitPass(c))
                    {
                        lit = true;
                        w.pendingLit.Remove(c);
                        int top = -1; for (int s = SECTIONS - 1; s >= 0; s--) if (c.secs[s] != null) { top = s; break; }
                        c.dirty = top >= 0 ? ((1 << (top + 1)) - 1) : 0;
                    }
                }
                litIdleAt = lit || cut ? -1 : w.statGen;
            }
            MeshPass(w, pcx, pcz, RR);
        }

        // adaptive leaves for a column at squared chunk distance d2, now built with mode cur: fancy within
        // 2 chunks, optimized within 6, shell beyond; a column keeps a finer mode one chunk further
        static int LeafLod(int d2, int cur)
        {
            int t = d2 <= 4 ? 2 : d2 <= 36 ? 1 : 3;
            if (cur == 2 && t != 2 && d2 <= 9) return 2;
            if (cur == 1 && t == 3 && d2 <= 49) return 1;
            return t;
        }

        // send dirty sections of columns within RR whose neighbours are all lit to the mesh workers, nearest first
        void MeshPass(World w, int pcx, int pcz, int RR)
        {
            var SP = spiral;
            int maxMesh = w.pool.count * 4, LV = Settings.leaves;
            bool lowered = false;
            for (int i = 0; i < SP.Length; i += 3)
            {
                if (SP[i + 2] > RR) break;
                if (w.meshInFlight >= maxMesh) break;
                var c = w.Col(pcx + SP[i], pcz + SP[i + 1]);
                if (c == null || c.state != 2) continue;
                int want = LV == 3 ? LeafLod(SP[i + 2], c.leafMode) : LV;
                if (c.leafMode < 0) c.leafMode = want;
                else if (want != c.leafMode)
                {
                    // finer leaves at once; coarser ones one column a frame. Only sections with leaves rebuild.
                    bool finer = LEAF_RANK[want] > LEAF_RANK[c.leafMode];
                    if (finer || !lowered)
                    {
                        if (!finer) lowered = true;
                        c.leafMode = want;
                        for (int s = 0; s < SECTIONS; s++)
                        {
                            var m = c.meshes[s] as SectionMesh;
                            if ((m != null && m.leafy) || (c.meshBusy & (1 << s)) != 0) { c.meshVer[s]++; c.dirty |= 1 << s; }
                        }
                    }
                }
                if (c.dirty == 0) continue;
                bool ok = true;
                for (int k = 0; k < 9 && ok; k++) { var n = w.Col(c.cx + (k % 3) - 1, c.cz + k / 3 - 1); if (n == null || n.state != 2) ok = false; }
                if (!ok) continue;
                // nearest sections first (vertical distance from the player; on a tie the lower one)
                int psy = ((int)Math.Floor(py) - WORLD_MIN_Y) >> 4;
                int lim = Math.Max(psy, SECTIONS - 1 - psy);
                for (int d = 0; d <= lim; d++)
                    for (int t = d != 0 ? -1 : 1; t <= 1 && w.meshInFlight < maxMesh; t += 2)
                    {
                        int s = psy + t * d;
                        if (s < 0 || s >= SECTIONS || (c.dirty & (1 << s)) == 0 || (c.meshBusy & (1 << s)) != 0) continue;
                        if (w.SectionEmpty(c, s))
                        {
                            c.dirty &= ~(1 << s);
                            if (c.meshes[s] is SectionMesh m) r.FreeSectionMesh(m);
                            c.meshes[s] = null; c.meshed |= 1 << s;
                            continue;
                        }
                        w.SubmitMesh(c, s, c.leafMode, Settings.sway);
                    }
            }
        }

        // upload finished section meshes to the GPU within a time budget
        void UploadMeshes(double budgetMs)
        {
            double t0 = Renderer.Now;
            while (meshQueue.Count > 0)
            {
                var (c, sy, d) = meshQueue.Dequeue();
                if (c.state >= 0) r.UploadSection(c, sy, d);
                if (Renderer.Now - t0 > budgetMs) break;
            }
        }

        // ------------------------------------------------------------------ environment
        int wfogFrom = -1, wfogTo = -1, wfogNow = -1; double wfogT;

        Env Environment()
        {
            var e = env;
            double a = time * Math.PI * 2;
            double s = Math.Sin(a);
            double day = Math.Max(0, Math.Min(1, (s + 0.14) / 0.3));
            double sunset = Math.Max(0, 1 - Math.Abs(s) * 3.2) * (0.3 + 0.7 * day);
            var sd = e.sunDir; double ca = Math.Cos(a), l = Math.Sqrt(ca * ca + s * s + 0.18 * 0.18);
            sd[0] = ca / l; sd[1] = s / l; sd[2] = 0.18 / l;
            e.day = day; e.sunset = sunset;
            var Z = e.zenith; var FC = e.fogColor;
            Z[0] = 0.02 + 0.44 * day; Z[1] = 0.03 + 0.62 * day; Z[2] = 0.08 + 0.92 * day;
            double f0 = 0.03 + 0.7 * day, f1 = 0.04 + 0.8 * day, f2 = 0.09 + 0.91 * day;
            // sunset tints the horizon when looking towards the sun
            double lookX = Math.Sin(yaw) * Math.Cos(pitch);
            double towards = Math.Max(0, lookX * Math.Sign(sd[0]));
            double k = sunset * (0.25 + 0.55 * towards);
            f0 += (0.98 - f0) * k; f1 += (0.55 - f1) * k; f2 += (0.3 - f2) * k;
            FC[0] = (float)f0; FC[1] = (float)f1; FC[2] = (float)f2;   // a Float32Array in the original
            var SC = e.sunsetColor; var CC = e.cloudColor;
            SC[0] = 1.0; SC[1] = 0.45 + 0.1 * day; SC[2] = 0.2;
            e.stars = Math.Max(0, 1 - day * 1.6);
            e.moonPhase = days % 8;
            CC[0] = 0.25 + 0.75 * day; CC[1] = 0.25 + 0.75 * day; CC[2] = 0.3 + 0.7 * day;
            int R = Math.Min(Settings.renderDist, 27);
            e.renderDist = R;
            // the rendered area is a circle of whole chunks: fog must be complete before the nearest missing chunk
            e.fogEnd = Math.Max(24, (R - 1) * 16); e.fogStart = Math.Max(e.fogEnd * 0.55, e.fogEnd - 40);
            e.far = Math.Max(420, R * 16 * 1.5);
            e.fov = Settings.fov;
            if (fovCur == 0) fovCur = e.fov;
            fovCur += (e.fov - fovCur) * 0.2;
            e.fov = fovCur;
            e.sway = Settings.sway ? 1 : 0;
            // underwater / lava: the camera is in a fluid only below its surface
            double ey = py + EYE;
            int ex = (int)Math.Floor(px), eyi = (int)Math.Floor(ey), ez = (int)Math.Floor(pz);
            double fh = world.FluidHeight(ex, eyi, ez);
            int ib = fh >= 0 && ey < eyi + fh ? world.GetBlock(ex, eyi, ez) : 0;
            e.underwater = false;
            if (IsWaterId(ib))
            {
                // Minecraft's underwater fog: the biome's water fog colour (blended over 5 s when it changes),
                // brightened toward full by the water vision, which grows over 30 s under water
                e.underwater = true;
                double v = WaterVision();
                string bn = WG.BIOME_LIST[world.BiomeAt(ex, ez)][0];
                int fc = WATER_FOG.TryGetValue(bn, out var wf) ? wf : 0x050533;
                if (wfogTo < 0) { wfogFrom = wfogTo = fc; wfogT = 0; }
                if (fc != wfogTo) { wfogFrom = wfogNow >= 0 ? wfogNow : wfogTo; wfogTo = fc; wfogT = Renderer.Now; }
                double kk = Math.Min(1, (Renderer.Now - wfogT) / 5000);
                double c0 = 0, c1 = 0, c2 = 0;
                for (int i = 0; i < 3; i++)
                {
                    int sh = 16 - i * 8, fa = (wfogFrom >> sh) & 255, fb = (wfogTo >> sh) & 255;
                    double cv = (fa + (fb - fa) * kk) / 255;
                    if (i == 0) c0 = cv; else if (i == 1) c1 = cv; else c2 = cv;
                }
                wfogNow = ((int)Math.Floor(c0 * 255 + 0.5) << 16) | ((int)Math.Floor(c1 * 255 + 0.5) << 8) | (int)Math.Floor(c2 * 255 + 0.5);
                double m = Math.Min(1 / Math.Max(c0, 1e-3), Math.Min(1 / Math.Max(c1, 1e-3), 1 / Math.Max(c2, 1e-3)));
                FC[0] = (float)(c0 * (1 - v) + c0 * m * v); FC[1] = (float)(c1 * (1 - v) + c1 * m * v); FC[2] = (float)(c2 * (1 - v) + c2 * m * v);
                e.fogStart = -8; e.fogEnd = Math.Min(e.fogEnd, 96 * Math.Max(0.25, v) * (bn == "SWAMP" || bn == "MANGROVE_SWAMP" ? 0.85 : 1));
            }
            else if (ib == B.LAVA)
            {
                e.underwater = true; FC[0] = 0.6f; FC[1] = 0.12f; FC[2] = 0.0f; e.fogStart = 0; e.fogEnd = 2.5;
            }
            return e;
        }

        double WaterVision()
        {
            int t = waterVisT;
            if (t >= 600) return 1;
            return Math.Min(1, t / 100.0) * 0.6 + (t < 100 ? 0 : Math.Min(1, (t - 100) / 500.0)) * 0.4;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (error != null || world == null) return;
            try { Frame(); }
            catch (Exception e) { error = e.ToString(); Debug.LogException(e); }
        }

        void Frame()
        {
            frameN++;
            double dt = Math.Min(0.1, Time.unscaledDeltaTime);
            fpsAcc += dt; fpsN++;
            if (fpsAcc >= 0.5) { fps = (int)Math.Round(fpsN / fpsAcc); fpsAcc = 0; fpsN = 0; }
            world.Poll(20);
            double t0 = Renderer.Now;
            Stream();
            double t1 = Renderer.Now;
            UploadMeshes(loadingWorld ? 50 : 3);
            double t2 = Renderer.Now;
            tStream = t1 - t0; tUpload = t2 - t1;
            if (loadingWorld) CheckLoaded();
            HandleInput(dt);
            if (!loadingWorld)
            {
                acc += dt;
                int n = 0;
                while (acc >= 0.05 && n < 5)
                {
                    acc -= 0.05; world.GameTick(); n++;
                    double ey = py + EYE; int bx = (int)Math.Floor(px), by = (int)Math.Floor(ey), bz = (int)Math.Floor(pz);
                    if (IsWaterId(world.GetBlock(bx, by, bz))) waterVisT++; else waterVisT = 0;
                }
                if (n >= 5) acc = 0;
                time = (time + dt / DAY_LEN) % 1;
                if (time < prevTime) days++;
                prevTime = time;
            }
            double tr = Renderer.Now;
            RenderFrame();
            tRender = Renderer.Now - tr;
        }

        void CheckLoaded()
        {
            var w = world;
            int pcx = (int)Math.Floor(px) >> 4, pcz = (int)Math.Floor(pz) >> 4;
            for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++)
            {
                var c = w.Col(pcx + dx, pcz + dz);
                if (c == null || c.state != 2 || c.dirty != 0 || c.meshBusy != 0) return;
            }
            if (needSpawnDrop)
            {
                int sx = (int)Math.Floor(px), sz = (int)Math.Floor(pz);
                // nearest column whose top solid block is natural ground under open sky
                bool Ground(int g) => g == B.GRASS || g == B.SNOWY_GRASS || g == B.SAND || g == B.SNOW || g == B.DIRT || g == B.PODZOL ||
                    g == B.MYCELIUM || g == B.RED_SAND || g == B.STONE || g == B.COARSE_DIRT || g == B.GRAVEL || g == B.MUD;
                int bx = sx, by = -999, bz = sz;
                for (int rr = 0; rr <= 32 && by == -999; rr++)
                    for (int dx = -rr; dx <= rr && by == -999; dx++)
                        for (int dz = -rr; dz <= rr && by == -999; dz++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != rr) continue;
                            int x = sx + dx, z = sz + dz;
                            if (!w.IsLoaded(x, z)) continue;
                            int y = WORLD_MAX_Y - 1;
                            while (y > WORLD_MIN_Y && SOLID[w.GetBlock(x, y, z)] == 0 && !IsWaterId(w.GetBlock(x, y, z))) y--;
                            if (!Ground(w.GetBlock(x, y, z)) || (w.GetLight(x, y + 1, z) >> 4) != 15) continue;
                            if (SOLID[w.GetBlock(x, y + 1, z)] != 0 || SOLID[w.GetBlock(x, y + 2, z)] != 0) continue;
                            bx = x; by = y + 1; bz = z;
                        }
                if (by == -999)
                {
                    int y = WORLD_MAX_Y - 1;
                    while (y > WORLD_MIN_Y && SOLID[w.GetBlock(sx, y, sz)] == 0 && !IsWaterId(w.GetBlock(sx, y, sz))) y--;
                    by = y + 1;
                }
                px = bx + 0.5; py = by + 0.01; pz = bz + 0.5;
                needSpawnDrop = false;
            }
            loadingWorld = false;
        }

        void HandleInput(double dt)
        {
            if (Input.GetMouseButtonDown(0)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                // the original turns 0.0022 rad per pixel; Unity's mouse axes are pixels x 0.1
                double s = 0.0022 * Settings.sens * 10;
                yaw += Input.GetAxisRaw("Mouse X") * s;
                pitch += Input.GetAxisRaw("Mouse Y") * s;
                pitch = Math.Max(-Math.PI / 2 * 0.999, Math.Min(Math.PI / 2 * 0.999, pitch));
            }
            double speed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ? 40 : 11;
            double fx = Math.Sin(yaw), fz = -Math.Cos(yaw), rx = Math.Cos(yaw), rz = Math.Sin(yaw);
            double mx = 0, my = 0, mz = 0;
            if (Input.GetKey(KeyCode.W)) { mx += fx; mz += fz; }
            if (Input.GetKey(KeyCode.S)) { mx -= fx; mz -= fz; }
            if (Input.GetKey(KeyCode.D)) { mx += rx; mz += rz; }
            if (Input.GetKey(KeyCode.A)) { mx -= rx; mz -= rz; }
            if (Input.GetKey(KeyCode.Space)) my += 1;
            if (Input.GetKey(KeyCode.LeftShift)) my -= 1;
            double l = Math.Sqrt(mx * mx + mz * mz);
            if (l > 0) { mx /= l; mz /= l; }
            px += mx * speed * dt; py += my * speed * dt; pz += mz * speed * dt;
            py = Math.Max(WORLD_MIN_Y - 32, Math.Min(WORLD_MAX_Y + 64, py));

            if (Input.GetKeyDown(KeyCode.F3)) debug = !debug;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) Settings.renderDist = Math.Min(27, Settings.renderDist + 1);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) Settings.renderDist = Math.Max(2, Settings.renderDist - 1);
            if (Input.GetKeyDown(KeyCode.T)) time = (time + 1.0 / 24) % 1;
            if (Input.GetKeyDown(KeyCode.C)) Settings.clouds = !Settings.clouds;
            if (Input.GetKeyDown(KeyCode.L)) Settings.leaves = (Settings.leaves + 1) % 4;
            if (Input.GetKeyDown(KeyCode.O)) r.noOcclusion = !r.noOcclusion;
            if (Input.GetKeyDown(KeyCode.F6)) { cullFlip = !cullFlip; r.SetCullFlip(cullFlip); }
        }

        void RenderFrame()
        {
            var e = Environment();
            r.UpdateLightmap(e, Settings.bright, false);
            double now = Renderer.Now;
            double t = now / 1000;
            uint tick = (uint)Math.Floor(now / 50);
            double ey = py + EYE;
            r.BeginFrame();
            r.RenderWorld(world, px, ey, pz, yaw, pitch, e, t, tick);
            if (Settings.clouds && !e.underwater) r.DrawClouds(px, ey, pz, e, t + time * DAY_LEN);
            r.RenderTranslucent(px, ey, pz, e, t, tick);
            r.EndFrame();
        }

        // ------------------------------------------------------------------ debug overlay
        GUIStyle style;
        readonly System.Text.StringBuilder sb = new System.Text.StringBuilder();

        void OnGUI()
        {
            if (style == null) { style = new GUIStyle(GUI.skin.label) { fontSize = 14 }; style.normal.textColor = Color.white; }
            if (error != null)
            {
                GUI.Label(new Rect(10, 10, Screen.width - 20, Screen.height - 20), "Error:\n" + error, style);
                return;
            }
            if (world == null) return;
            if (loadingWorld)
            {
                GUI.Label(new Rect(10, 10, 600, 30), "Загрузка мира… колонок " + world.cols.Count + ", генерация " + world.genInFlight + ", меши " + world.meshInFlight, style);
            }
            if (!debug) return;
            var st = r.stats;
            sb.Clear();
            sb.Append("Maincraft (Unity) ").Append(fps).Append(" fps\n");
            sb.Append("XYZ ").Append(px.ToString("0.00")).Append(" / ").Append(py.ToString("0.00")).Append(" / ").Append(pz.ToString("0.00"));
            sb.Append("  yaw ").Append((yaw * 180 / Math.PI % 360).ToString("0")).Append(" pitch ").Append((pitch * 180 / Math.PI).ToString("0")).Append('\n');
            int bx = (int)Math.Floor(px), bz = (int)Math.Floor(pz);
            sb.Append("биом ").Append(WG.BIOME_LIST[world.BiomeAt(bx, bz)][0]).Append("  время ").Append((time * 24).ToString("0.0")).Append(" ч, день ").Append(days).Append('\n');
            sb.Append("колонок ").Append(world.cols.Count).Append(" · генерация ").Append(world.genInFlight).Append(" · меши ").Append(world.meshInFlight)
              .Append(" (очередь ").Append(meshQueue.Count).Append(") · свет ").Append(world.pendingLit.Count).Append('\n');
            sb.Append("секций ").Append(st.sections).Append(" · квадов ").Append(st.quads).Append(" · вызовов ").Append(st.draws).Append(" · туман ").Append(st.fogCulled).Append('\n');
            sb.Append("GPU ").Append((r.GpuBytes / 1048576.0).ToString("0.0")).Append(" МБ, занято ").Append((r.UsedQuads * 48 / 1048576.0).ToString("0.0")).Append(" МБ\n");
            sb.Append("кадр: поток ").Append(tStream.ToString("0.00")).Append(" загрузка ").Append(tUpload.ToString("0.00")).Append(" рендер ").Append(tRender.ToString("0.00"))
              .Append(" (видимость ").Append(r.cpuVis.ToString("0.00")).Append(", сортировка ").Append(r.cpuSort.ToString("0.00")).Append(") мс\n");
            sb.Append("дальность ").Append(Settings.renderDist).Append(" (+/-) · листва ").Append(Settings.leaves).Append(" (L) · облака ").Append(Settings.clouds ? "вкл" : "выкл").Append(" (C)")
              .Append(" · окклюзия ").Append(r.noOcclusion ? "выкл" : "вкл").Append(" (O) · T +1 ч\n");
            sb.Append("WASD, Space/Shift, Ctrl быстрее, клик — захват мыши, Esc — отпустить, F3 — скрыть");
            GUI.Label(new Rect(10, 10, 1200, 400), sb.ToString(), style);
        }
    }
}
