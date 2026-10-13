// Port of game.js (12700-13599): the module-level tables and helpers, every field of class Game, and the
// methods from the constructor to interact(): world lifecycle (start / save / quit), column streaming,
// mesh submission and upload, the environment (sky, fog, underwater), the frame loop with dynamic
// resolution, stutter attribution and the F3+B benchmark, update() and the camera, and block targeting /
// breaking / using. The rest of Game is in Game.Actions.cs (13600-14449) and Game.View.cs (14450-15205).
//
// API skeleton: bodies marked TODO(game-a) are filled in by that implementer.
//
// Note: inside Game the method Interact(dt) hides the class name Interact (interact.js); call those
// functions unqualified through `using static Maincraft.Interact;` or as Maincraft.Interact.X.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed partial class Game
    {
        // ================================================================== game.js module level
        static bool? isTouch;
        /// <summary>IS_TOUCH (12705): a touch device ('ontouchstart' / pointer: coarse); picks the mobile defaults.</summary>
        public static bool IS_TOUCH => isTouch ?? (isTouch = Application.isMobilePlatform || Input.touchSupported).Value;

        /// <summary>Seconds per full day (time += dt / DAY_LEN).</summary>
        public const double DAY_LEN = 1200;
        /// <summary>Minecraft's difficulties (Difficulty.getId: peaceful 0 .. hard 3); a world starts on normal.</summary>
        public static readonly string[] DIFFICULTIES = { "peaceful", "easy", "normal", "hard" };

        // Minecraft's PoseStack as one 4x4 matrix (column-major), each step multiplied on the right
        // game.js pmId (12723)
        public static double[] PmId(double[] m) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js pmT (12724)
        public static void PmT(double[] m, double x, double y, double z) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js pmS (12725)
        public static void PmS(double[] m, double x, double y, double z) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js pmX (12726): returns at once when !a (0 or NaN)
        public static void PmX(double[] m, double a) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js pmY (12727): returns at once when !a
        public static void PmY(double[] m, double a) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js pmZ (12728): returns at once when !a
        public static void PmZ(double[] m, double a) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js pmApply (12730-12732): a point through the pose P (the w row ignored), into o
        public static void PmApply(double[] P, double x, double y, double z, double[] o) { throw new NotImplementedException(); } // TODO(game-a)
        /// <summary>PmApply as a PoseFn for PushModelBox (one cached delegate, no allocation per call).</summary>
        public static readonly PoseFn PmApplyFn = PmApply;
        public const double DEG = Math.PI / 180;
        // game.js tridentPoint (12738-12741): a trident sprite point -> the long held / thrown trident model (blocks), into o
        public static void TridentPoint(double x, double y, double z, double[] o) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js sameStack (12743): the same object, or both set with equal id and count
        public static bool SameStack(ItemStack a, ItemStack b) { throw new NotImplementedException(); } // TODO(game-a)
        /// <summary>Cache of HandBlockBoxes (null results cached too).</summary>
        public static readonly Dictionary<int, double[][]> HAND_BOXES = new Dictionary<int, double[][]>();
        // game.js handBlockBoxes (12747-12758): the boxes of a block held as a 3D model (Shapes.FULL_BOX by
        // reference for cubes), or null for a flat sprite
        public static double[][] HandBlockBoxes(int id) { throw new NotImplementedException(); } // TODO(game-a)
        /// <summary>Fineness rank of the leaf mesh modes (0 fast, 1 optimized, 2 fancy, 3 shell).</summary>
        public static readonly byte[] LEAF_RANK = { 0, 2, 3, 1 };
        // game.js leafLod (12764-12769): adaptive leaf mode by squared chunk distance, with hysteresis
        public static int LeafLod(int d2, int cur) { throw new NotImplementedException(); } // TODO(game-a)
        public const string BUILD_ID = "1b2b887";
        /// <summary>Respawn spread round the world spawn (Bedrock's spawnradius).</summary>
        public const int SPAWN_RADIUS = 5;
        /// <summary>Biome water fog colours (RGB; the rest use 0x050533).</summary>
        public static readonly Dictionary<string, int> WATER_FOG = new Dictionary<string, int>();   // TODO(game-a): fill (12772)
        /// <summary>Model box faces (+x, -x, top, bottom, back, front) as corner indices (bit0 x1, bit1 y1, bit2 z1).</summary>
        public static readonly sbyte[] MB_FACES = { 5, 1, 3, 7, 0, 4, 6, 2, 6, 7, 3, 2, 5, 4, 0, 1, 4, 5, 7, 6, 1, 0, 2, 3 };
        public static readonly double[] MB_SHADE = { 0.72, 0.72, 1.0, 0.55, 0.8, 0.9 };
        public static readonly int[] MB_TRI = { 0, 1, 2, 0, 2, 3 };
        /// <summary>pushBox faces (+x, -x, top, bottom, +z, -z): per corner whether x, y, z take the max side (72).</summary>
        public static readonly byte[] BOX_C = null;   // TODO(game-a): fill (12777-12779)
        public static readonly double[] WHITE4 = { 1, 1, 1, 1 };
        public static readonly float[] SEL_COLOR = { 0, 0, 0, 0.55f };
        public static readonly byte[] BOX_TRI = { 0, 1, 2, 0, 2, 3 }, BOX_U = { 0, 1, 1, 0 }, BOX_V = { 1, 1, 0, 0 };

        /// <summary>RT (12785-12798): random-tick behaviour per block id, derived once from the block names.</summary>
        public sealed class RandomTickTable
        {
            public byte[] kind;     // 1 crop 0-2, 2 ripe stem, 3 sapling, 4 dirt, 5 grass, 6 fire, 7 farmland, 8 cocoa
            public ushort[] next;   // crop: next stage id; stem: the fruit
            public byte[] flam;     // flammable (leaves, planks, logs, wool, wood, bookshelves)
        }
        /// <summary>Built by InitStatics (after Blocks.Init), sized B_KEY.length.</summary>
        public static RandomTickTable RT;
        // game.js RT IIFE (12785-12798), run once after Blocks.Init (the JS ran it at script load)
        public static void InitStatics() { throw new NotImplementedException(); } // TODO(game-a)

        // ================================================================== nested state types
        /// <summary>this.bench: the F3+B benchmark run.</summary>
        public sealed class Bench
        {
            public string phase;                // 'wait' | 'run'
            public double t, ready;
            public double[] pos;
            public double yaw, pitch;
            public bool flying;
            public List<double> frames = new List<double>();
            public Dictionary<string, double> cpu = new Dictionary<string, double>();
            public double sec, draws, quads, fog;
            public int n;
            public bool loadedAll;
            public double waited;
        }
        /// <summary>this.autoS: dynamic-resolution window state.</summary>
        public sealed class AutoScaleState
        {
            public List<double> iv = new List<double>();
            public double js, t;
            public int good;
            public double failAt, failScale = 2;
        }
        /// <summary>One stutter of this.spikes.list.</summary>
        public sealed class Spike { public int i; public double ms; public string why; public double js; }
        /// <summary>this.spikes.</summary>
        public sealed class SpikeLog { public List<Spike> list = new List<Spike>(); }
        /// <summary>this.upS: GPU upload rate sampled every second (perfReport).</summary>
        public sealed class UploadRate { public double t; public int n; public long b; public double rate, bRate; }
        /// <summary>this.wfog: underwater fog colour blend.</summary>
        public sealed class WaterFogBlend { public int from, to; public double t; }
        /// <summary>A Later() timer.</summary>
        public sealed class Timer { public int id; public double due; public Action fn; }

        // ================================================================== fields (every chunk)
        public GameAssets assets;
        public Camera canvas;                       // the WebGL canvas: the camera whose target size is the render size
        public Renderer r;
        public World world = null;
        public Player player = new Player();
        /// <summary>Raw keys held, by KeyboardEvent.code name ('KeyW', 'ShiftLeft', 'Space', ...), filled by PollInput.</summary>
        public HashSet<string> keys = new HashSet<string>();
        public PlayerInput input = new PlayerInput();
        public ItemStack[] inv = new ItemStack[36];
        public int sel = 0;
        public GameMode mode = GameMode.Creative;
        public double time = 0.25;
        public bool playing = false, paused = true;
        public double lastT = 0, acc = 0;
        public double fpsAcc = 0; public int fpsN = 0, fps = 0;
        public List<Particle> particles = new List<Particle>();
        public BreakState breaking = null;
        public MouseState mouse = new MouseState();
        public double[] cam = { 0, 0, 0 };
        public int camMode = 0;                     // 0 first person, 1 behind, 2 front
        public bool debug = false, hideHud = false;
        public Survival surv = new Survival();
        public double atkT = 10; public int atkBarV = -1; public ItemStack lastHeld = null; public int missT = 0; public double breakDelay = 0;
        public HandState hand = new HandState();
        /// <summary>Column offsets within +-40, nearest first (stable sort by d2): dx, dz, dx² + dz² per entry.</summary>
        public int[] spiral;
        public long litIdleAt = -1;                 // stream(): World.statGen at the last light pass that lit nothing
        public double lastSpace = 0, lastW = 0;     // double-tap timers (ms)
        public double saveTimer = 0;
        public Env envObj = new Env();

        public WorldMeta meta;
        public Difficulty difficulty = Difficulty.Normal;
        /// <summary>LevelChunk.inhabitedTime per column: colKey -> ticks (null before startWorld).</summary>
        public OrderedMap<long, double> inhabited;
        public Queue<(Column c, int sy, MeshResult d)> meshQueue;
        public OrderedMap<long, Dictionary<int, int>> pendingSave;
        public Entities ents;
        public Ships ships;                         // kept across worlds
        public Boats boats;                         // new per world
        public double[] spawn;
        public int[] bedSpawn;
        public bool needSpawnDrop;
        public ShipRec pendingShip;
        public double[][] pendingBoats;
        public bool loadingWorld;
        public int frameN;
        public float[] ftHist; public int ftI;      // frame-time ring buffer (ms), monotonic index
        public double prevTime = double.NaN;        // undefined in the JS: the first comparison is false
        public int days;                            // this.days || 0
        public double dynScale;                     // 0: unset (this.dynScale || 1)
        public AutoScaleState autoS;
        public SpikeLog spikes;
        public Dictionary<string, double> frameStages, perf;
        public Bench bench;
        public string benchLast, benchText;
        public double fovCur;                       // 0: unset
        public WaterFogBlend wfog;
        public int? wfogNow;
        public double pAcc, stepDist, waterVisT;
        /// <summary>View bob (x, y, roll, nod), shared by reference with r.bob. A Float32Array in the JS: store (float) values.</summary>
        public double[] bobView;
        public RayHit target;
        public SleepState sleeping;
        public double sleepFade;
        public EatState eating;
        public ChargeState charging;
        public TouchInput touch;                    // created by UI.BuildTouch on touch devices
        public InputKeys touchKeys;                 // read by update() but never set anywhere in the original
        public RespawnState respawning;
        /// <summary>Alias of meta.chests ("x,y,z" -> 27 slots).</summary>
        public OrderedMap<string, ItemStack[]> chests;
        public bool breakingPlayer;                 // set around breakBlock in doBreak, never read
        public Label msgEl; public int msgT;        // say(): the action-bar message and its fade timer (Later id)
        public VisualElement sleepEl;
        public int[] rtSeed;                        // random-tick xorshift state (Int32Array(1)), seeded lazily
        public double partAcc;
        public double? partAlpha;                   // this.partAlpha ?? 1
        public double[] inWallVP;
        public float[] inWallV;
        public double[] fireProj;
        public float[] fireOV;
        public float[] selPts;
        public VBuf crackVB, mobCrackVB, partVB, handVB, heldVB, heldTmp;
        public double[] handProj, handM;
        public double[] heldQ, heldT3;
        public ushort[] heldL;
        public double[] heldC;
        public Dictionary<int, float[]> spriteCache;
        public double[] _mbC, _mbR, _mbQ;
        public UploadRate upS;
        public double copiedT;
        /// <summary>Pending Later() timers (setTimeout), run in due order by RunTimers.</summary>
        public List<Timer> timers = new List<Timer>();
        public int timerSeq;

        // ================================================================== timers (setTimeout)
        /// <summary>setTimeout(fn, ms): runs fn on the main thread once ms (wall clock, Renderer.Now) have passed. Returns an id for CancelLater.</summary>
        public int Later(double ms, Action fn) { throw new NotImplementedException(); } // TODO(game-a)
        /// <summary>clearTimeout(id).</summary>
        public void CancelLater(int id) { throw new NotImplementedException(); } // TODO(game-a)
        /// <summary>Runs the due timers in due order; called every frame at the start of Loop (also while not playing).</summary>
        public void RunTimers() { throw new NotImplementedException(); } // TODO(game-a)

        // ================================================================== Game
        // game.js constructor (12801-12842): renderer, player, inventory, state, spiral, env, clouds, input
        // (the RAF loop is the MonoBehaviour's Update calling Loop)
        public Game(GameAssets assets, Camera camera) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js resize (12844-12848): render size from the screen, Settings.scale and dynScale
        public void Resize() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js startWorld (12851-12929; async in the JS): load a world record, new World and its hooks,
        // Redstone hooks, Entities / Ships / Boats, player position or findSpawn
        public void StartWorld(WorldMeta meta) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js defaultInv (12931-12938)
        public ItemStack[] DefaultInv() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js findSpawn (12940-12965; a Promise in the JS): the first candidate column with dry land, [x, z];
        // done is called (possibly frames later) with the result
        public void FindSpawn(Action<int[]> done) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js saveWorld (12967-12998; async in the JS): the meta record and the column edits
        public void SaveWorld(bool quitting) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js quitToTitle (13000-13008)
        public void QuitToTitle() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js stream (13011-13061): column requests, unloads, light passes, meshPass
        public void Stream() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js meshPass (13064-13106)
        public void MeshPass(World w, int pcx, int pcz, int RR) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js uploadMeshes (13109-13120)
        public void UploadMeshes(double budgetMs) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js environment (13123-13182): fills envObj in place
        public Env Environment() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js loop (13185-13276): one frame; t = timestamp in ms (Renderer.Now)
        public void Loop(double t) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js autoScaleTick (13282-13301)
        public void AutoScaleTick(double ms, double js) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js noteSpike (13305-13314)
        public void NoteSpike(double ms) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js spikeReport (13315-13323)
        public string SpikeReport() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js perfT (13325-13332)
        public void PerfT(string name, double ms) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js startBenchmark (13339-13348)
        public void StartBenchmark() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js worldReady (13349-13360)
        public bool WorldReady() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js benchTick (13361-13378)
        public void BenchTick(double dt) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js benchProgress (13379-13381)
        public void BenchProgress(string text) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js endBenchmark (13382-13419)
        public void EndBenchmark(bool cancelled) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js update (13421-13504)
        public void Update(double dt) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js waterVision (13507-13511)
        public double WaterVision() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js updateCamera (13512-13532)
        public void UpdateCamera() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js interact (13535-13600)
        public void Interact(double dt) { throw new NotImplementedException(); } // TODO(game-a)
    }
}
