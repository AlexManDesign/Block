// Port of render.js for Unity's built-in pipeline.
//
// A frame is recorded into one CommandBuffer in exactly the original's order (terrain, sky, vessels,
// clouds, particles, entities, water, outline, hand ...) and runs before Unity's own opaque pass. The
// view-projection is the original's own matrix (camera-relative, right-handed, GL clip space), set
// with SetViewProjectionMatrices so Unity applies the platform's conversions; all positions are
// camera-relative game coordinates, so precision does not fall off far from the world origin.
//
// Section meshes stay in the mesher's packed format (3 uints per vertex) in a few big GPU buffers
// (QuadArena pages). The visible sections are found with the original's occlusion walk, their quad runs
// (after directional culling) become 64-quad records, and every pass goes out as one instanced draw per
// page: the vertex shader reads the record and its quads straight from the buffers.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static Maincraft.Blocks;

namespace Maincraft
{
    /// <summary>A section's GPU geometry (the original's per-section mesh object).</summary>
    public sealed class SectionMesh
    {
        public int page = -1, start, cap;            // solid + cut-out quads in an arena page
        public int[] counts = new int[3];
        public int[] gc; public byte[] vis; public bool leafy;
        public int cx, cz, sy;
        public int tStart = -1, tCap;                 // translucent quads in the translucent arena
        public uint[] tData;                          // the translucent quads as meshed (12 uints each)
        public float[] tc; public byte[] tg;          // their centres (section-local blocks) and facing groups
        public long tkey = -1; public int tn;         // last sort key; quads drawn (sorted, facing the camera)
        public bool HasQuads => counts[0] + counts[1] + counts[2] > 0;
    }

    /// <summary>Frame environment (the original's envObj): sky colours, fog, sun.</summary>
    public sealed class Env
    {
        public readonly double[] fogColor = new double[3], zenith = new double[3], sunDir = { 0, 1, 0 }, sunsetColor = { 0.95, 0.5, 0.27 }, cloudColor = { 1, 1, 1 };
        public double day, sunset, stars, fogStart, fogEnd, far, fov, sway;
        public int moonPhase, renderDist;
        public bool underwater;
    }

    public sealed partial class Renderer : IDisposable
    {
        public const int PAGE_QUADS = 1 << 19;       // 24 MB of vertices per arena page
        const int CHUNK = 64;                         // quads per record (instance)
        static readonly int[] DX6 = { 1, -1, 0, 0, 0, 0 }, DY6 = { 0, 0, 1, -1, 0, 0 }, DZ6 = { 0, 0, 0, 0, 1, -1 }, OPP6 = { 1, 0, 3, 2, 5, 4 };

        static readonly int ID_Data = Shader.PropertyToID("_Data"), ID_RecBase = Shader.PropertyToID("_RecBase"), ID_Rec = Shader.PropertyToID("_Rec"),
            ID_Anim = Shader.PropertyToID("_Anim"), ID_Tex = Shader.PropertyToID("_Tex"), ID_LM = Shader.PropertyToID("_LM"),
            ID_CamI = Shader.PropertyToID("_CamI"), ID_CamF = Shader.PropertyToID("_CamF"), ID_MTime = Shader.PropertyToID("_MTime"),
            ID_MTick = Shader.PropertyToID("_MTick"), ID_Fog = Shader.PropertyToID("_Fog"), ID_FogColor = Shader.PropertyToID("_FogColor"),
            ID_Sway = Shader.PropertyToID("_Sway"), ID_AlphaMul = Shader.PropertyToID("_AlphaMul"), ID_Overlay = Shader.PropertyToID("_Overlay"),
            ID_Zenith = Shader.PropertyToID("_Zenith"), ID_Horizon = Shader.PropertyToID("_Horizon"), ID_SunDir = Shader.PropertyToID("_SunDir"),
            ID_Sunset = Shader.PropertyToID("_Sunset"), ID_Stars = Shader.PropertyToID("_Stars"), ID_Void = Shader.PropertyToID("_Void"),
            ID_SeaClip = Shader.PropertyToID("_SeaClip"), ID_MainTex = Shader.PropertyToID("_MainTex"), ID_KeyBlack = Shader.PropertyToID("_KeyBlack"),
            ID_Mul = Shader.PropertyToID("_Mul"), ID_CloudFog = Shader.PropertyToID("_CloudFog"), ID_Cull = Shader.PropertyToID("_Cull"),
            ID_ShipRot = Shader.PropertyToID("_ShipRot"), ID_ShipRel = Shader.PropertyToID("_ShipRel");

        public readonly Camera cam;
        readonly CommandBuffer cb = new CommandBuffer { name = "Maincraft" };
        readonly Material matSolid, matCutout, matTrans, matShip, matSky, matSun, matMoon, matClouds;
        public Texture2DArray texArr;
        Texture2D lmTex, sunTex, moonTex;
        GraphicsBuffer animBuf, quadIdx, recBuf;
        public int layerCount, overlayLayer, whiteLayer;
        public byte[] atlasPixels; public int atlasW, atlasH;    // RGBA, top row first (as a canvas reads it)
        readonly byte[] lmData = new byte[16 * 16 * 4];
        double flicker, flickerT, flickerTarget;
        readonly System.Random rng = new System.Random();
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        public static double Now => clock.Elapsed.TotalMilliseconds;   // performance.now()

        readonly List<QuadArena> pages = new List<QuadArena>();
        QuadArena trans;
        readonly HashSet<SectionMesh> transSecs = new HashSet<SectionMesh>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        // camera (game space, camera-relative, GL conventions)
        public readonly double[] proj = new double[16], view = new double[16], vp = new double[16], invVP = new double[16], frustum = new double[24];
        public double[] bob;                          // view bobbing: x, y offset, roll, nod (set by the game each frame)
        public double[] hurtTilt;                     // hurt tilt: direction, angle (or null)
        Matrix4x4 vpU;                                // vp as a Unity matrix

        // visibility
        SectionMesh[] visList = new SectionMesh[4096];
        public int visCount;
        uint frame;
        int[] bfsQ = new int[1 << 18];
        public bool noOcclusion, dirCull = true, noFogCull;
        double fogCullEnd, fogCullY; bool fogCull;

        // records of the frame: [quadStart | (count - 1) << 26, x, y, z] per 64-quad chunk (xyz: section
        // origin minus the camera block, or a vessel section's own origin)
        int[][] recLists = new int[0][]; int[] recCounts = new int[0];
        int[] recs = new int[4096 * 4]; int recN;
        long[] sortKey = new long[4096];
        uint[] sortOut = new uint[4096 * 12];

        public struct Stats { public int draws, quads, sections, fogCulled, upN; public long upBytes; }
        public Stats stats;
        /// <summary>CPU ms of the frame's visibility walk, terrain draw recording (solid + cut-out) and translucent sort.</summary>
        public double cpuVis, cpuTerrain, cpuSort;

        public Renderer(Camera cam, byte[] atlasPng, string atlasMetaJson, byte[] sunPng, byte[] moonPng, Func<string, byte[]> entityImage)
        {
            this.cam = cam;
            this.entityImage = entityImage;
            matSolid = NewMat("Shaders/TerrainSolid"); matCutout = NewMat("Shaders/TerrainCutout"); matTrans = NewMat("Shaders/TerrainTrans");
            matShip = NewMat("Shaders/TerrainShip");
            matSky = NewMat("Shaders/Sky"); matSun = NewMat("Shaders/Celestial"); matMoon = NewMat("Shaders/Celestial"); matClouds = NewMat("Shaders/Clouds");
            InitTextures(atlasPng, atlasMetaJson, sunPng, moonPng);
            matSun.SetTexture(ID_MainTex, sunTex); matMoon.SetTexture(ID_MainTex, moonTex);
            matSun.SetFloat(ID_KeyBlack, 1); matMoon.SetFloat(ID_KeyBlack, 1);
            // shared quad index pattern of one 64-quad chunk: 0,1,2,0,2,3 + 4q
            quadIdx = new GraphicsBuffer(GraphicsBuffer.Target.Index, CHUNK * 6, 2);
            var idx = new ushort[CHUNK * 6];
            for (int q = 0; q < CHUNK; q++)
            {
                int v = q * 4, o = q * 6;
                idx[o] = (ushort)v; idx[o + 1] = (ushort)(v + 1); idx[o + 2] = (ushort)(v + 2);
                idx[o + 3] = (ushort)v; idx[o + 4] = (ushort)(v + 2); idx[o + 5] = (ushort)(v + 3);
            }
            quadIdx.SetData(idx);
            recBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4096, 16);
            trans = new QuadArena(1 << 16);
            lmTex = new Texture2D(16, 16, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            InitSky();
            InitDyn();
            cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, cb);
        }

        static Material NewMat(string shader)
        {
            var s = Resources.Load<Shader>(shader);
            if (s == null) throw new Exception("shader not found: " + shader);
            return new Material(s) { hideFlags = HideFlags.DontSave };
        }

        /// <summary>Culls the other side of terrain and cloud faces (a check of the winding on a new platform).</summary>
        public bool cullFlip;
        public void SetCullFlip(bool flip)
        {
            cullFlip = flip;
            float mode = flip ? (float)CullMode.Back : (float)CullMode.Front;
            matSolid.SetFloat(ID_Cull, mode); matCutout.SetFloat(ID_Cull, mode); matTrans.SetFloat(ID_Cull, mode); matShip.SetFloat(ID_Cull, mode); matClouds.SetFloat(ID_Cull, mode);
            dynMats.Clear();
        }

        // ---------------------------------------------------------------- textures
        void InitTextures(byte[] atlasPng, string metaJson, byte[] sunPng, byte[] moonPng)
        {
            var meta = (Dictionary<string, object>)MiniJson.Parse(metaJson);
            int N = MiniJson.I(meta["count"]), cols = MiniJson.I(meta["cols"]);
            var layers = (Dictionary<string, object>)meta["layers"];
            layerCount = N;
            overlayLayer = layers.TryGetValue("grass_block_side_overlay", out var ov) ? MiniJson.I(ov) : 0;
            whiteLayer = layers.TryGetValue("white", out var wl) ? MiniJson.I(wl) : 0;
            var px = DecodePng(atlasPng, out int W, out int H);
            atlasPixels = px; atlasW = W; atlasH = H;
            const int levels = 5;
            texArr = new Texture2DArray(16, 16, N, TextureFormat.RGBA32, levels, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, anisoLevel = 0 };
            var lvl = new byte[16 * 16 * 4 * N];
            for (int l = 0; l < N; l++)
            {
                int ox = (l % cols) * 16, oy = l / cols * 16;
                for (int y = 0; y < 16; y++) Buffer.BlockCopy(px, ((oy + y) * W + ox) * 4, lvl, (l * 256 + y * 16) * 4, 64);
            }
            // Unity's row 0 is v = 0 like GL's first row: the tiles go in exactly as the original uploads them
            for (int l = 0; l < N; l++) texArr.SetPixelData(lvl, 0, l, l * 1024);
            int size = 16;
            for (int m = 1; m < levels; m++)
            {
                int ns = size >> 1;
                var nl = new byte[ns * ns * 4 * N];
                for (int l = 0; l < N; l++)
                {
                    int sb = l * size * size * 4, db = l * ns * ns * 4;
                    for (int y = 0; y < ns; y++) for (int x = 0; x < ns; x++)
                    {
                        int r = 0, g = 0, b = 0, a = 0, wsum = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int i = sb + (((y * 2 + (k >> 1)) * size) + x * 2 + (k & 1)) * 4;
                            int al = lvl[i + 3], w = al + 1;
                            r += lvl[i] * w; g += lvl[i + 1] * w; b += lvl[i + 2] * w; a += al; wsum += w;
                        }
                        int o = db + (y * ns + x) * 4;
                        nl[o] = (byte)(r / wsum); nl[o + 1] = (byte)(g / wsum); nl[o + 2] = (byte)(b / wsum);
                        // keep coverage for cut-out textures (alpha test at 0.5)
                        double av = a / 4.0;
                        nl[o + 3] = (byte)(av > 0 && av < 255 ? Math.Min(255, av * 1.25) : av);
                    }
                }
                int per = ns * ns * 4;
                for (int l = 0; l < N; l++) texArr.SetPixelData(nl, m, l, l * per);
                lvl = nl; size = ns;
            }
            texArr.Apply(false, true);
            // animation table: frames | ticks << 8 per layer (first frame of an animated strip)
            int an = Math.Max(N, 2048);
            var anim = new uint[an];
            for (int l = 0; l < an; l++) anim[l] = 1 | (1 << 8);
            if (meta.TryGetValue("anim", out var ao) && ao is Dictionary<string, object> A)
                foreach (var kv in A)
                {
                    var e = (List<object>)kv.Value;
                    int first = MiniJson.I(e[0]), frames = MiniJson.I(e[1]), ticks = MiniJson.I(e[2]);
                    anim[first] = (uint)(frames & 255) | (uint)(ticks & 255) << 8;
                }
            animBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, an, 4);
            animBuf.SetData(anim);
            sunTex = Tex2D(DecodePng(sunPng, out W, out H), W, H);
            moonTex = Tex2D(DecodePng(moonPng, out W, out H), W, H);
        }

        /// <summary>
        /// A PNG as a canvas reads it: RGBA rows from the top; the colour of fully transparent pixels is
        /// lost and that of half transparent ones rounded through premultiplied storage.
        /// </summary>
        public static byte[] DecodePng(byte[] png, out int W, out int H)
        {
            var img = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            img.LoadImage(png);
            W = img.width; H = img.height;
            var px32 = img.GetPixels32();
            UnityEngine.Object.Destroy(img);
            var px = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
            {
                int srow = (H - 1 - y) * W;
                for (int x = 0; x < W; x++)
                {
                    var c = px32[srow + x]; int o = (y * W + x) * 4, a = c.a;
                    if (a == 0) { px[o] = 0; px[o + 1] = 0; px[o + 2] = 0; }
                    else if (a == 255) { px[o] = c.r; px[o + 1] = c.g; px[o + 2] = c.b; }
                    else { px[o] = Unpremul(c.r, a); px[o + 1] = Unpremul(c.g, a); px[o + 2] = Unpremul(c.b, a); }
                    px[o + 3] = (byte)a;
                }
            }
            return px;
        }

        static byte Unpremul(int c, int a)
        {
            int p = (c * a + 127) / 255;
            return (byte)Math.Min(255, (p * 255 + a / 2) / a);
        }

        /// <summary>A 2D texture of top-first RGBA rows uploaded like GL does (first row at v = 0), nearest filtering.</summary>
        public static Texture2D Tex2D(byte[] rgbaTopFirst, int W, int H)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.SetPixelData(rgbaTopFirst, 0);
            t.Apply(false, true);
            return t;
        }

        // ---------------------------------------------------------------- section meshes
        public void UploadSection(Column c, int sy, MeshResult d)
        {
            stats.upN++; stats.upBytes += (long)d.data.Length * 4;
            var m = c.meshes[sy] as SectionMesh;
            int total = d.counts[0] + d.counts[1] + d.counts[2];
            if (total == 0)
            {
                if (m != null) FreeSectionMesh(m);
                var e = new SectionMesh { vis = d.vis, cx = c.cx, cz = c.cz, sy = sy };
                c.meshes[sy] = e;
                return;
            }
            if (m == null) { m = new SectionMesh(); c.meshes[sy] = m; }
            m.cx = c.cx; m.cz = c.cz; m.sy = sy;
            int opq = d.counts[0] + d.counts[1];
            if (m.page >= 0) { pages[m.page].Free(m.start, m.cap); m.page = -1; }
            if (opq > 0)
            {
                int need = QuadArena.Round(opq);
                AllocPage(need, out m.page, out m.start);
                m.cap = need;
                pages[m.page].buf.SetData(d.data, 0, m.start * 12, opq * 12);
            }
            Array.Copy(d.counts, m.counts, 3);
            m.gc = d.gc; m.vis = d.vis; m.leafy = d.leafy;
            PrepareTrans(m, d);
        }

        void AllocPage(int need, out int page, out int start)
        {
            for (int p = 0; p < pages.Count; p++)
            {
                int s = pages[p].Alloc(need);
                if (s >= 0) { page = p; start = s; return; }
            }
            var a = new QuadArena(Math.Max(PAGE_QUADS, need));
            pages.Add(a);
            page = pages.Count - 1; start = a.Alloc(need);
        }

        // Translucent quads are drawn back to front: their centres and facing groups are kept for the
        // sort, and the quads themselves are rewritten in sorted order when the camera moves on.
        void PrepareTrans(SectionMesh m, MeshResult d)
        {
            int n = d.counts[2];
            if (m.tStart >= 0) { trans.Free(m.tStart, m.tCap); m.tStart = -1; }
            if (n == 0) { ClearTrans(m); return; }
            int first = d.counts[0] + d.counts[1];
            var w = d.data;
            var tc = new float[n * 3]; var tg = new byte[n];
            for (int q = 0; q < n; q++)
            {
                int sx = 0, sy = 0, sz = 0;
                for (int v = 0, o = (first + q) * 12; v < 4; v++, o += 3)
                {
                    uint w0 = w[o];
                    sx += (int)(w0 & 1023); sy += (int)((w0 >> 10) & 1023); sz += (int)((w0 >> 20) & 1023);
                }
                tc[q * 3] = (float)(sx * 0.0078125); tc[q * 3 + 1] = (float)(sy * 0.0078125); tc[q * 3 + 2] = (float)(sz * 0.0078125);
            }
            if (d.gc != null) { int q = 0; for (int g = 0; g < 7; g++) for (int k = d.gc[14 + g]; k > 0; k--) tg[q++] = (byte)g; }
            else for (int q = 0; q < n; q++) tg[q] = 6;
            var td = new uint[n * 12];
            Array.Copy(w, first * 12, td, 0, n * 12);
            m.tc = tc; m.tg = tg; m.tData = td;
            int need = QuadArena.Round(n);
            m.tStart = AllocTrans(need); m.tCap = need;
            transSecs.Add(m);
            m.tkey = -1; m.tn = 0;
        }

        int AllocTrans(int need)
        {
            int s = trans.Alloc(need);
            if (s >= 0) return s;
            // grow: a new arena twice the size; every translucent section is placed again and re-sorted
            var old = trans;
            int cap = old.capQuads * 2;
            while (cap < old.usedQuads + need * 2) cap *= 2;
            trans = new QuadArena(cap);
            foreach (var m in transSecs)
            {
                if (m.tStart < 0) continue;
                m.tStart = trans.Alloc(m.tCap); m.tkey = -1; m.tn = 0;
            }
            old.Release();
            return trans.Alloc(need);
        }

        void ClearTrans(SectionMesh m)
        {
            m.tData = null; m.tc = null; m.tg = null; m.tn = 0; m.tkey = -1;
            transSecs.Remove(m);
        }

        public void FreeSectionMesh(SectionMesh m)
        {
            if (m.page >= 0) { pages[m.page].Free(m.start, m.cap); m.page = -1; }
            if (m.tStart >= 0) { trans.Free(m.tStart, m.tCap); m.tStart = -1; }
            ClearTrans(m);
            m.counts[0] = m.counts[1] = m.counts[2] = 0;
        }

        public void FreeColumn(Column c)
        {
            for (int s = 0; s < SECTIONS; s++)
            {
                if (c.meshes[s] is SectionMesh m) FreeSectionMesh(m);
                c.meshes[s] = null;
            }
        }

        public long GpuBytes
        {
            get { long b = (long)trans.capQuads * 48; foreach (var p in pages) b += (long)p.capQuads * 48; return b; }
        }
        public long UsedQuads
        {
            get { long b = trans.usedQuads; foreach (var p in pages) b += p.usedQuads; return b; }
        }

        // ---------------------------------------------------------------- profiling (F3)
        // render.js initProfiler / gpuBegin / gpuEnd / gpuPoll (7212-7256): GPU time per render phase from one
        // timer query per phase, read back frames later and smoothed per phase. WebGL's
        // EXT_disjoint_timer_query_webgl2 has no direct Unity counterpart, so tq is false and these run the
        // original's no-extension branches exactly (gpuBegin / gpuPoll return at once; perfReport prints its
        // 'timer unavailable' line). A real timer (CommandBuffer.BeginSample / EndSample per phase read back
        // with Profiling.Recorder.gpuElapsedNanoseconds where SystemInfo.supportsGpuRecorder) would set tq,
        // record in GpuBegin / GpuEnd and fill gpuMs / gpuAcc / gpuBad in GpuPoll as the JS does.
        /// <summary>The timer query extension is available (render.js this.tq, truthy when present).</summary>
        public bool tq;
        /// <summary>Timing wanted this frame (game.js renderFrame: debug || bench).</summary>
        public bool gpuOn;
        /// <summary>Smoothed GPU ms per phase name ('solid', 'cutout', 'sky', 'clouds', 'particles', 'entities', 'water', 'hand').</summary>
        public readonly Dictionary<string, double> gpuMs = new Dictionary<string, double>();
        public double gpuFrameMs;
        /// <summary>A benchmark's GPU time sum of one phase.</summary>
        public sealed class GpuAccum { public double s; public int n; }
        /// <summary>Per-phase sums while a benchmark runs (game.js sets a new map at the start of the run, reads it at the end and sets null).</summary>
        public Dictionary<string, GpuAccum> gpuAcc;
        /// <summary>Implausible query results dropped (this.gpuBad || 0).</summary>
        public int gpuBad;
        /// <summary>The GPU's name (WEBGL_debug_renderer_info's UNMASKED_RENDERER_WEBGL).</summary>
        public readonly string gpuName = SystemInfo.graphicsDeviceName;
        /// <summary>WEBGL_multi_draw (the report's 'multiDraw' line): the sections of a pass always go out as one instanced draw per arena page here.</summary>
        public readonly bool multiDraw = true;
        /// <summary>render.js pstat: one pass's sections drawn, draw calls, quads drawn and quads skipped by directional culling.</summary>
        public sealed class PassStat { public int sec, draws, quads, skipped; }
        /// <summary>Per pass (0 solid, 1 cut-out, 2 translucent), reset by RenderWorld every frame.</summary>
        public readonly PassStat[] pstat = { new PassStat(), new PassStat(), new PassStat() };
        string qCur;

        // render.js resetPassStats (7223-7227)
        public void ResetPassStats()
        {
            foreach (var p in pstat) { p.sec = 0; p.draws = 0; p.quads = 0; p.skipped = 0; }
            stats.fogCulled = 0;
        }
        // render.js gpuBegin (7228-7234): ends the open phase and starts timing the next
        public void GpuBegin(string name)
        {
            if (!gpuOn || !tq) return;
            if (qCur != null) GpuEnd();
            qCur = name;
        }
        // render.js gpuEnd (7235-7239)
        public void GpuEnd()
        {
            if (qCur == null) return;
            qCur = null;
        }
        // render.js gpuPoll (7240-7256): the finished queries into gpuMs (and gpuAcc)
        public void GpuPoll()
        {
            if (!tq) return;
        }
        // render.js meshMemory (7257-7261): GPU bytes held by the section meshes (48 bytes a quad), and their number
        public (long bytes, int n) MeshMemory(World world)
        {
            long bytes = 0; int n = 0;
            foreach (var c in world.cols.Values)
                foreach (var o in c.meshes)
                {
                    if (!(o is SectionMesh m) || (m.page < 0 && m.tStart < 0)) continue;
                    bytes += ((m.page >= 0 ? m.cap : 0) + (m.tStart >= 0 ? m.tCap : 0)) * 48L;
                    n++;
                }
            return (bytes, n);
        }

        // ---------------------------------------------------------------- per-frame
        public void UpdateLightmap(Env sky, double gamma, bool nightVision)
        {
            // sky: day (0..1), sunset (0..1); block light warm with flicker
            double now = Now;
            if (now - flickerT > 90) { flickerT = now; flickerTarget = (rng.NextDouble() - 0.5) * 0.2; }
            flicker += (flickerTarget - flicker) * 0.25;
            var d = lmData;
            double day = sky.day;
            double skyB = 0.08 + 0.92 * day;
            // moonlight is blue-ish, daylight neutral, sunset warm
            double sr = 0.55 + 0.45 * day, sg = 0.6 + 0.4 * day, sb = 0.85 + 0.15 * day;
            if (sky.sunset > 0) { sr += (1.1 - sr) * sky.sunset * 0.45; sg += (0.85 - sg) * sky.sunset * 0.45; sb += (0.62 - sb) * sky.sunset * 0.45; }
            double fl = 1 + flicker;
            for (int s = 0; s < 16; s++)
            {
                double sl = s / 15.0;
                double sv = (sl / (4 - 3 * sl)) * skyB;
                for (int b = 0; b < 16; b++)
                {
                    double bl = Math.Min(1, (b / 15.0) * fl);
                    double bv = bl / (4 - 3 * bl);
                    double r = sv * sr + bv * 1.0, g = sv * sg + bv * (0.78 + bv * 0.2), bb = sv * sb + bv * (0.55 + bv * 0.3);
                    // ambient floor (caves are dark but not pitch black)
                    r = 0.045 + r * 0.955; g = 0.045 + g * 0.955; bb = 0.05 + bb * 0.95;
                    if (nightVision) { r = Math.Max(r, 0.8); g = Math.Max(g, 0.8); bb = Math.Max(bb, 0.8); }
                    r = Math.Min(1, r); g = Math.Min(1, g); bb = Math.Min(1, bb);
                    // brightness (gamma) setting like MC
                    r += ((1 - Math.Pow(1 - r, 4)) - r) * gamma; g += ((1 - Math.Pow(1 - g, 4)) - g) * gamma; bb += ((1 - Math.Pow(1 - bb, 4)) - bb) * gamma;
                    int o = (s * 16 + b) * 4;
                    d[o] = (byte)(r * 255); d[o + 1] = (byte)(g * 255); d[o + 2] = (byte)(bb * 255); d[o + 3] = 255;
                }
            }
            lmTex.SetPixelData(d, 0);
            lmTex.Apply(false);
        }

        // ---------------------------------------------------------------- camera
        public static void Persp(double[] o, double fovy, double aspect, double near, double far)
        {
            double f = 1 / Math.Tan(fovy / 2), nf = 1 / (near - far);
            Array.Clear(o, 0, 16); o[0] = f / aspect; o[5] = f; o[10] = (far + near) * nf; o[11] = -1; o[14] = 2 * far * near * nf;
        }
        public static void Mul(double[] o, double[] a, double[] b)
        {
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++)
                o[j * 4 + i] = a[i] * b[j * 4] + a[4 + i] * b[j * 4 + 1] + a[8 + i] * b[j * 4 + 2] + a[12 + i] * b[j * 4 + 3];
        }
        // rotation-only view matrix from yaw/pitch (camera looks along -z at yaw=0)
        public static void View(double[] o, double yaw, double pitch)
        {
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            double fx = sy * cp, fy = sp, fz = -cy * cp;
            double rx = cy, ry = 0, rz = sy;
            double ux = ry * fz - rz * fy, uy = rz * fx - rx * fz, uz = rx * fy - ry * fx;
            o[0] = rx; o[4] = ry; o[8] = rz; o[12] = 0;
            o[1] = ux; o[5] = uy; o[9] = uz; o[13] = 0;
            o[2] = -fx; o[6] = -fy; o[10] = -fz; o[14] = 0;
            o[3] = 0; o[7] = 0; o[11] = 0; o[15] = 1;
        }
        public static void Invert(double[] o, double[] m)
        {
            double a00 = m[0], a01 = m[1], a02 = m[2], a03 = m[3], a10 = m[4], a11 = m[5], a12 = m[6], a13 = m[7];
            double a20 = m[8], a21 = m[9], a22 = m[10], a23 = m[11], a30 = m[12], a31 = m[13], a32 = m[14], a33 = m[15];
            double b00 = a00 * a11 - a01 * a10, b01 = a00 * a12 - a02 * a10, b02 = a00 * a13 - a03 * a10, b03 = a01 * a12 - a02 * a11;
            double b04 = a01 * a13 - a03 * a11, b05 = a02 * a13 - a03 * a12, b06 = a20 * a31 - a21 * a30, b07 = a20 * a32 - a22 * a30;
            double b08 = a20 * a33 - a23 * a30, b09 = a21 * a32 - a22 * a31, b10 = a21 * a33 - a23 * a31, b11 = a22 * a33 - a23 * a32;
            double det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
            if (det == 0) return;
            det = 1 / det;
            o[0] = (a11 * b11 - a12 * b10 + a13 * b09) * det; o[1] = (a02 * b10 - a01 * b11 - a03 * b09) * det;
            o[2] = (a31 * b05 - a32 * b04 + a33 * b03) * det; o[3] = (a22 * b04 - a21 * b05 - a23 * b03) * det;
            o[4] = (a12 * b08 - a10 * b11 - a13 * b07) * det; o[5] = (a00 * b11 - a02 * b08 + a03 * b07) * det;
            o[6] = (a32 * b02 - a30 * b05 - a33 * b01) * det; o[7] = (a20 * b05 - a22 * b02 + a23 * b01) * det;
            o[8] = (a10 * b10 - a11 * b08 + a13 * b06) * det; o[9] = (a01 * b08 - a00 * b10 - a03 * b06) * det;
            o[10] = (a30 * b04 - a31 * b02 + a33 * b00) * det; o[11] = (a21 * b02 - a20 * b04 - a23 * b00) * det;
            o[12] = (a11 * b07 - a10 * b09 - a12 * b06) * det; o[13] = (a00 * b09 - a01 * b07 + a02 * b06) * det;
            o[14] = (a31 * b01 - a30 * b03 - a32 * b00) * det; o[15] = (a20 * b03 - a21 * b01 + a22 * b00) * det;
        }
        /// <summary>A column-major GL matrix (double[16]) as a Unity matrix.</summary>
        public static Matrix4x4 ToMatrix(double[] m)
        {
            var M = new Matrix4x4();
            for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) M[r, c] = (float)m[c * 4 + r];
            return M;
        }

        // PoseStack helpers of game.js (column-major, each step multiplied on the right)
        public static void PmId(double[] m) { Array.Clear(m, 0, 16); m[0] = m[5] = m[10] = m[15] = 1; }
        public static void PmY(double[] m, double a)
        {
            if (a == 0) return;
            double c = Math.Cos(a), s = Math.Sin(a);
            for (int i = 0; i < 4; i++) { double x = m[i], z = m[8 + i]; m[i] = x * c - z * s; m[8 + i] = x * s + z * c; }
        }
        public static void PmZ(double[] m, double a)
        {
            if (a == 0) return;
            double c = Math.Cos(a), s = Math.Sin(a);
            for (int i = 0; i < 4; i++) { double x = m[i], y = m[4 + i]; m[i] = x * c + y * s; m[4 + i] = y * c - x * s; }
        }

        readonly double[] tmpM = new double[16], bobM = new double[16], hurtM = new double[16];
        static readonly int[] PL_W = { 3, 3, 3, 3, 3, 3 }, PL_R = { 0, 0, 1, 1, 2, 2 }, PL_S = { 1, -1, 1, -1, 1, -1 };
        public double Aspect => cam.pixelHeight > 0 ? (double)cam.pixelWidth / cam.pixelHeight : 1;

        /// <summary>Sets the view (the original's setCamera): projection, view with bobbing and hurt tilt, frustum.</summary>
        public void SetCamera(double fovDeg, double yaw, double pitch, double far)
        {
            Persp(proj, fovDeg * Math.PI / 180, Aspect, 0.05, far);
            View(view, yaw, pitch);
            var b = bob;
            if (b != null && (b[0] != 0 || b[1] != 0 || b[2] != 0 || b[3] != 0))
            {
                // view bobbing in view space: translate, then roll (z) and nod (x)
                double cz = Math.Cos(b[2]), sz = Math.Sin(b[2]), cx = Math.Cos(b[3]), sx = Math.Sin(b[3]);
                var B = bobM;
                B[0] = cz; B[1] = sz; B[2] = 0; B[3] = 0;
                B[4] = -sz * cx; B[5] = cz * cx; B[6] = sx; B[7] = 0;
                B[8] = sz * sx; B[9] = -cz * sx; B[10] = cx; B[11] = 0;
                B[12] = b[0]; B[13] = b[1]; B[14] = 0; B[15] = 1;
                Mul(tmpM, B, view); Array.Copy(tmpM, view, 16);
            }
            // hurt: the view leans across the way the blow came from (GameRenderer.bobHurt, before the bob)
            var ht = hurtTilt;
            if (ht != null)
            {
                var Hm = hurtM;
                PmId(Hm); PmY(Hm, -ht[0]); PmZ(Hm, ht[1]); PmY(Hm, ht[0]);
                Mul(tmpM, Hm, view); Array.Copy(tmpM, view, 16);
            }
            Mul(vp, proj, view);
            Invert(invVP, vp);
            // frustum planes from vp (camera relative)
            var m = vp; var f = frustum;
            for (int i = 0; i < 6; i++)
            {
                int w = PL_W[i], r = PL_R[i], s = PL_S[i];
                double a = m[w] + s * m[r], bb = m[4 + w] + s * m[4 + r], c = m[8 + w] + s * m[8 + r], dd = m[12 + w] + s * m[12 + r];
                double l = Math.Sqrt(a * a + bb * bb + c * c);
                f[i * 4] = a / l; f[i * 4 + 1] = bb / l; f[i * 4 + 2] = c / l; f[i * 4 + 3] = dd / l;
            }
            vpU = ToMatrix(vp);
            // the Unity camera only frames the picture; the CommandBuffer draws with the matrix above
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.Euler(-(float)(pitch * 180 / Math.PI), (float)(yaw * 180 / Math.PI), 0);
            cam.fieldOfView = (float)fovDeg;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = (float)far;
        }

        public bool BoxVisible(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var f = frustum;
            for (int i = 0; i < 24; i += 4)
            {
                double a = f[i], b = f[i + 1], c = f[i + 2], d = f[i + 3];
                if (a * (a > 0 ? x1 : x0) + b * (b > 0 ? y1 : y0) + c * (c > 0 ? z1 : z0) + d < 0) return false;
            }
            return true;
        }

        // occlusion-culled BFS over sections from the camera section
        public void CollectVisible(World world, double camX, double camY, double camZ, int renderDist)
        {
            frame++;
            uint fr = frame;
            var list = visList;
            int n = 0;
            int ccx = (int)Math.Floor(camX) >> 4, ccz = (int)Math.Floor(camZ) >> 4;
            int csy = ((int)Math.Floor(camY) - WORLD_MIN_Y) >> 4;
            if (csy < 0) csy = 0; if (csy >= SECTIONS) csy = SECTIONS - 1;
            var Q = bfsQ;
            int qh = 0, qt = 0;
            var startCol = world.Col(ccx, ccz);
            if (startCol == null) { visCount = 0; return; }
            startCol.visFrame[csy] = fr;
            Q[qt++] = ccx; Q[qt++] = ccz; Q[qt++] = csy; Q[qt++] = -1; Q[qt++] = 0;
            double R2 = (renderDist + 0.5) * (renderDist + 0.5);
            bool noCull = noOcclusion;
            var F = frustum;
            while (qh < qt)
            {
                int cx = Q[qh], cz = Q[qh + 1], sy = Q[qh + 2], from = Q[qh + 3], dirs = Q[qh + 4];
                qh += 5;
                var c = world.Col(cx, cz);
                var m = c.meshes[sy] as SectionMesh;
                if (m != null && m.HasQuads)
                {
                    if (fogCull && FullyFogged(cx, sy, cz, camX, camZ)) stats.fogCulled++;
                    else
                    {
                        if (n == list.Length) { Array.Resize(ref visList, n * 2); list = visList; }
                        list[n++] = m; m.cx = cx; m.cz = cz; m.sy = sy;
                    }
                }
                var vis = m?.vis;
                for (int d = 0; d < 6; d++)
                {
                    if ((dirs & (1 << OPP6[d])) != 0) continue;
                    if (from >= 0 && vis != null && !noCull && (vis[from] & (1 << d)) == 0) continue;
                    int nx = cx + DX6[d], ny = sy + DY6[d], nz = cz + DZ6[d];
                    if (ny < 0 || ny >= SECTIONS) continue;
                    int ddx = nx - ccx, ddz = nz - ccz;
                    if (ddx * ddx + ddz * ddz > R2) continue;
                    var nc = world.Col(nx, nz);
                    if (nc == null || nc.state < 2) continue;
                    if (nc.visFrame[ny] == fr) continue;
                    // frustum test in camera-relative coords
                    double x0 = nx * 16 - camX, y0 = WORLD_MIN_Y + ny * 16 - camY, z0 = nz * 16 - camZ, x1 = x0 + 16, y1 = y0 + 16, z1 = z0 + 16;
                    bool outside = false;
                    for (int i = 0; i < 24; i += 4)
                    {
                        double a = F[i], b = F[i + 1], e = F[i + 2];
                        if (a * (a > 0 ? x1 : x0) + b * (b > 0 ? y1 : y0) + e * (e > 0 ? z1 : z0) + F[i + 3] < 0) { outside = true; break; }
                    }
                    if (outside) continue;
                    nc.visFrame[ny] = fr;
                    if (qt + 5 > Q.Length) break;
                    Q[qt++] = nx; Q[qt++] = nz; Q[qt++] = ny; Q[qt++] = OPP6[d]; Q[qt++] = dirs | (1 << d);
                }
            }
            visCount = n;
        }

        // A section is skipped when every point of it is in full fog (the terrain fog distance is
        // max(horizontal distance, |dy| / 2)) and it lies wholly below the eye: every ray to it points
        // down, where the sky shader draws exactly the fog colour, so the pixel is the same without it.
        bool FullyFogged(int cx, int sy, int cz, double camX, double camZ)
        {
            double fogEnd = fogCullEnd, eyeY = fogCullY;
            int y0 = WORLD_MIN_Y + sy * 16;
            if (y0 + 17 > eyeY) return false;
            // seen from above the clouds, terrain reaching into the cloud layer would hide cloud behind it
            if (eyeY > 191 && y0 + 16 > 191) return false;
            double x0 = cx * 16, z0 = cz * 16;
            double dx = camX < x0 ? x0 - camX : camX > x0 + 16 ? camX - x0 - 16 : 0;
            double dz = camZ < z0 ? z0 - camZ : camZ > z0 + 16 ? camZ - z0 - 16 : 0;
            double dy = eyeY - (y0 + 17);
            return Math.Max(Math.Sqrt(dx * dx + dz * dz), dy * 0.5) >= fogEnd + 1;
        }

        // ---------------------------------------------------------------- frame
        /// <summary>Starts recording a frame: clears the command list, the frame's records and dynamic vertices.</summary>
        public void BeginFrame()
        {
            cb.Clear();
            recN = 0;
            dynN = 0;
            ResetState();
            stats.draws = 0; stats.quads = 0; stats.sections = 0; stats.fogCulled = 0;
        }

        /// <summary>Uploads what the frame's commands read (records, dynamic vertices); call after the last draw.</summary>
        public void EndFrame()
        {
            if (recBuf.count < recN)
            {
                recBuf.Release();
                recBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(recN * 2, 4096), 16);
            }
            if (recN > 0) recBuf.SetData(recs, 0, 0, recN * 4);
            Shader.SetGlobalBuffer(ID_Rec, recBuf);
            UploadDyn();
        }

        int AddRecs(int[] src, int n)
        {
            int at = recN;
            if ((recN + n) * 4 > recs.Length) Array.Resize(ref recs, Math.Max(recs.Length * 2, (recN + n) * 4));
            Array.Copy(src, 0, recs, recN * 4, n * 4);
            recN += n;
            return at;
        }

        void BindTerrain(Env env, double camX, double camY, double camZ, double time, uint tick, double sway)
        {
            int ix = (int)Math.Floor(camX), iy = (int)Math.Floor(camY), iz = (int)Math.Floor(camZ);
            cb.SetGlobalVector(ID_CamI, new Vector4(ix, iy, iz, 0));
            cb.SetGlobalVector(ID_CamF, new Vector4((float)(camX - ix), (float)(camY - iy), (float)(camZ - iz), 0));
            cb.SetGlobalFloat(ID_MTime, (float)time);
            cb.SetGlobalFloat(ID_MTick, tick & 0xFFFFFF);
            cb.SetGlobalVector(ID_Fog, new Vector4((float)env.fogStart, (float)env.fogEnd, 0, 0));
            cb.SetGlobalVector(ID_FogColor, new Vector4((float)env.fogColor[0], (float)env.fogColor[1], (float)env.fogColor[2], 1));
            cb.SetGlobalFloat(ID_Sway, (float)sway);
            cb.SetGlobalFloat(ID_AlphaMul, 1);
            cb.SetGlobalFloat(ID_Overlay, overlayLayer);
            cb.SetGlobalBuffer(ID_Anim, animBuf);
            cb.SetGlobalTexture(ID_Tex, texArr);
            cb.SetGlobalTexture(ID_LM, lmTex);
        }

        void DrawRecords(Material mat, GraphicsBuffer data, int recBase, int count)
        {
            mpb.Clear();
            mpb.SetBuffer(ID_Data, data);
            mpb.SetFloat(ID_RecBase, recBase);
            cb.DrawProcedural(quadIdx, Matrix4x4.identity, mat, 0, MeshTopology.Triangles, CHUNK * 6, count, mpb);
            stats.draws++;
        }

        void EnsureRecLists()
        {
            int need = 2 * pages.Count;
            if (recLists.Length >= need) return;
            int old = recLists.Length;
            Array.Resize(ref recLists, need); Array.Resize(ref recCounts, need);
            for (int i = old; i < need; i++) recLists[i] = new int[1024];
        }

        void Emit(int list, int quadStart, int n, int rx, int ry, int rz)
        {
            var L = recLists[list]; int c = recCounts[list];
            int chunks = (n + CHUNK - 1) / CHUNK;
            if ((c + chunks) * 4 > L.Length) { Array.Resize(ref recLists[list], Math.Max(L.Length * 2, (c + chunks) * 4)); L = recLists[list]; }
            for (int k = 0; k < n; k += CHUNK)
            {
                int cnt = Math.Min(CHUNK, n - k), o = c * 4;
                L[o] = (quadStart + k) | ((cnt - 1) << 26); L[o + 1] = rx; L[o + 2] = ry; L[o + 3] = rz;
                c++;
            }
            recCounts[list] = c;
        }

        // Quads are grouped by facing (-X,+X,-Y,+Y,-Z,+Z,other); groups facing away from the camera for the
        // whole section are skipped (directional culling), the rest become records of their runs.
        void BuildPass(int pass, double camX, double camY, double camZ)
        {
            int ix = (int)Math.Floor(camX), iy = (int)Math.Floor(camY), iz = (int)Math.Floor(camZ);
            var list = visList; int n = visCount;
            var ps = pstat[pass];
            for (int k = 0; k < n; k++)
            {
                var m = list[k];
                int cnt = m.counts[pass];
                if (cnt == 0 || m.page < 0) continue;
                ps.sec++;
                int first = pass == 0 ? 0 : m.counts[0];
                int li = pass * pages.Count + m.page;
                int bx = m.cx * 16, by = WORLD_MIN_Y + m.sy * 16, bz = m.cz * 16;
                int rx = bx - ix, ry = by - iy, rz = bz - iz;
                stats.sections++;
                var gc = m.gc;
                if (gc == null || !dirCull) { Emit(li, m.start + first, cnt, rx, ry, rz); stats.quads += cnt; ps.quads += cnt; continue; }
                double ox = bx - camX, oy = by - camY, oz = bz - camZ;
                // camera below / above / beside the section box on each axis
                int vis = (ox > 0 ? 1 : ox + 16 < 0 ? 2 : 3) | (oy > 0 ? 4 : oy + 16 < 0 ? 8 : 12) | (oz > 0 ? 16 : oz + 16 < 0 ? 32 : 48) | 64;
                int at = first, runStart = -1, runLen = 0, g0 = pass * 7, q0 = stats.quads;
                for (int g = 0; g < 7; g++)
                {
                    int c = gc[g0 + g];
                    if (c == 0) continue;
                    if ((vis & (1 << g)) != 0)
                    {
                        if (runStart >= 0 && runStart + runLen == at) runLen += c;
                        else
                        {
                            if (runStart >= 0) Emit(li, m.start + runStart, runLen, rx, ry, rz);
                            runStart = at; runLen = c;
                        }
                        stats.quads += c;
                    }
                    at += c;
                }
                if (runStart >= 0) Emit(li, m.start + runStart, runLen, rx, ry, rz);
                ps.quads += stats.quads - q0; ps.skipped += cnt - (stats.quads - q0);
            }
        }

        /// <summary>
        /// The opaque world (renderWorld): clears to the fog colour, finds the visible sections, draws their
        /// solid and cut-out quads, then the sky with sun and moon where the terrain left the picture empty.
        /// </summary>
        public void RenderWorld(World world, double camX, double camY, double camZ, double yaw, double pitch, Env env, double time, uint tick)
        {
            ResetPassStats();
            GpuBegin("solid");
            SetCamera(env.fov, yaw, pitch, env.far);
            cb.SetViewProjectionMatrices(Matrix4x4.identity, vpU);
            cb.ClearRenderTarget(true, true, new Color((float)env.fogColor[0], (float)env.fogColor[1], (float)env.fogColor[2], 1));
            cam.backgroundColor = new Color((float)env.fogColor[0], (float)env.fogColor[1], (float)env.fogColor[2], 1);
            // fog occlusion only when the sky below the horizon is plain fog colour
            fogCull = !env.underwater && env.sunset < 0.001 && !noFogCull;
            fogCullEnd = env.fogEnd; fogCullY = camY;
            double tc = Now;
            CollectVisible(world, camX, camY, camZ, env.renderDist);
            cpuVis = Now - tc;

            tc = Now;
            EnsureRecLists();
            int nl = 2 * pages.Count;
            for (int i = 0; i < nl; i++) recCounts[i] = 0;
            BuildPass(0, camX, camY, camZ);
            BuildPass(1, camX, camY, camZ);
            BindTerrain(env, camX, camY, camZ, time, tick, env.sway);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) GpuBegin("cutout");
                for (int p = 0; p < pages.Count; p++)
                {
                    int li = pass * pages.Count + p, cnt = recCounts[li];
                    if (cnt == 0) continue;
                    int at = AddRecs(recLists[li], cnt);
                    DrawRecords(pass == 0 ? matSolid : matCutout, pages[p].buf, at, cnt);
                    pstat[pass].draws++;
                }
            }
            cpuTerrain = Now - tc;
            // sky after the opaque terrain: it only shades pixels the terrain left empty
            GpuBegin("sky");
            if (!env.underwater) DrawSky(env, camY);
        }

        // Re-sorts translucent quads of sections whose camera block changed, nearest sections first,
        // within a time budget; the rest keep their previous order until a later frame.
        void SortTranslucent(double camX, double camY, double camZ)
        {
            var list = visList; int n = visCount; double t0 = Now;
            int csx = (int)Math.Floor(camX) >> 4, csy = ((int)Math.Floor(camY) - WORLD_MIN_Y) >> 4, csz = (int)Math.Floor(camZ) >> 4;
            for (int k = 0; k < n; k++)
            {
                var m = list[k];
                if (m.tData == null) continue;
                double px = camX - m.cx * 16, py = camY - WORLD_MIN_Y - m.sy * 16, pz = camZ - m.cz * 16;
                int vis = !dirCull ? 127 : (px < 0 ? 1 : px > 16 ? 2 : 3) | (py < 0 ? 4 : py > 16 ? 8 : 12) | (pz < 0 ? 16 : pz > 16 ? 32 : 48) | 64;
                // re-sort once the camera moved a step: one block near the section, coarser further away
                int sd = Math.Max(Math.Abs(m.cx - csx), Math.Max(Math.Abs(m.sy - csy), Math.Abs(m.cz - csz)));
                int st = sd <= 1 ? 1 : sd <= 3 ? 2 : sd <= 7 ? 4 : 8;
                long key = ((((long)Math.Floor(px / st) + 512) * 1024 + (long)Math.Floor(py / st) + 512) * 1024 + (long)Math.Floor(pz / st) + 512) * 128 + vis;
                if (key == m.tkey) continue;
                if (m.tkey != -1 && Now - t0 > 1.5) continue;
                SortTrans(m, px, py, pz, vis);
                m.tkey = key;
            }
        }

        // camera at (px,py,pz) in section-local blocks; vis: facing groups that can face the camera
        void SortTrans(SectionMesh m, double px, double py, double pz, int vis)
        {
            int n = m.tg.Length; var tc = m.tc; var tg = m.tg;
            if (sortKey.Length < n) sortKey = new long[n * 2];
            var key = sortKey;
            // key = squared distance (1/256 steps) * 65536 + quad
            int k = 0;
            for (int q = 0; q < n; q++)
            {
                if ((vis & (1 << tg[q])) == 0) continue;
                double dx = tc[q * 3] - px, dy = tc[q * 3 + 1] - py, dz = tc[q * 3 + 2] - pz;
                key[k++] = (long)Math.Floor((dx * dx + dy * dy + dz * dz) * 256) * 65536 + q;
            }
            Array.Sort(key, 0, k);
            if (sortOut.Length < k * 12) sortOut = new uint[k * 24];
            var o = sortOut; var src = m.tData;
            for (int j = k - 1, e = 0; j >= 0; j--, e += 12)
            {
                int s = (int)(key[j] % 65536) * 12;
                for (int i = 0; i < 12; i++) o[e + i] = src[s + i];
            }
            if (k > 0) trans.buf.SetData(o, 0, m.tStart * 12, k * 12);
            m.tn = k;
        }

        int[] transRecs = new int[1024];

        /// <summary>Translucent terrain (renderTranslucent): sections back to front, each one's quads sorted.</summary>
        public void RenderTranslucent(double camX, double camY, double camZ, Env env, double time, uint tick)
        {
            double tc = Now;
            SortTranslucent(camX, camY, camZ);
            cpuSort = Now - tc;
            GpuBegin("water");
            int ix = (int)Math.Floor(camX), iy = (int)Math.Floor(camY), iz = (int)Math.Floor(camZ);
            int n = 0;
            var ps = pstat[2];
            for (int k = visCount - 1; k >= 0; k--)
            {
                var m = visList[k];
                if (m.counts[2] != 0) { ps.sec++; ps.skipped += m.counts[2] - m.tn; }
                if (m.tn == 0 || m.tStart < 0) continue;
                int rx = m.cx * 16 - ix, ry = WORLD_MIN_Y + m.sy * 16 - iy, rz = m.cz * 16 - iz;
                int chunks = (m.tn + CHUNK - 1) / CHUNK;
                if ((n + chunks) * 4 > transRecs.Length) Array.Resize(ref transRecs, Math.Max(transRecs.Length * 2, (n + chunks) * 4));
                for (int q = 0; q < m.tn; q += CHUNK)
                {
                    int cnt = Math.Min(CHUNK, m.tn - q), o = n * 4;
                    transRecs[o] = (m.tStart + q) | ((cnt - 1) << 26); transRecs[o + 1] = rx; transRecs[o + 2] = ry; transRecs[o + 3] = rz;
                    n++;
                }
                stats.quads += m.tn; ps.quads += m.tn;
            }
            if (n == 0) return;
            BindTerrain(env, camX, camY, camZ, time, tick, env.sway);
            int at = AddRecs(transRecs, n);
            DrawRecords(matTrans, trans.buf, at, n);
            ps.draws++;
        }

        public void Dispose()
        {
            if (cam != null) cam.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, cb);
            cb.Release();
            foreach (var p in pages) p.Release();
            pages.Clear();
            trans?.Release(); recBuf?.Release(); quadIdx?.Release(); animBuf?.Release();
            DisposeDyn();
        }
    }
}
