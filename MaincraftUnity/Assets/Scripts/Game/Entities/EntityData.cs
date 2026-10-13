// Port of the module level of entities.js (8915-9315): the mob models and definitions, spawn tables, sheep
// colours (EntityData), and the shared entity physics and math helpers that boats.js and game.js use too:
// collision, water height and flow, sweeps, angle helpers, the A* scratch state and heap, the slime-chunk
// hash and the per-vertex model transform (EntityUtil).
//
// API skeleton: bodies marked TODO(ent-a) are filled in by that implementer.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    public static class EntityData
    {
        /// <summary>Mob models by type (8921-9041); cave_spider shares the spider's parts array; MODELS["player"] is compared by identity.</summary>
        public static readonly Dictionary<string, MobModel> MODELS = new Dictionary<string, MobModel>();   // TODO(ent-a): fill in Init
        /// <summary>Mob definitions by type (9062-9082), built after Items / Blocks init.</summary>
        public static readonly Dictionary<string, MobDef> MOB_DEFS = new Dictionary<string, MobDef>();     // TODO(ent-a): fill in Init
        /// <summary>Object.keys(MOB_DEFS): pig, cow, sheep, chicken, zombie, drowned, skeleton, creeper, spider, cave_spider, enderman, slime, salmon, shark.</summary>
        public static string[] MOB_TYPES;                                                                   // TODO(ent-a): fill in Init
        /// <summary>Creature spawns per biome id (9107-9128); null for biomes without any.</summary>
        public static AnimalSpawn[] ANIMAL_SPAWNS;                                                          // TODO(ent-a): fill in Init
        /// <summary>Slime size -> [health, contact damage, movement attribute] (9093).</summary>
        public static readonly Dictionary<int, double[]> SLIME_SIZES = new Dictionary<int, double[]>();     // TODO(ent-a): fill
        /// <summary>Natural sheep colours: name, cumulative odds, fur tint rgb (9095).</summary>
        public static (string name, double cum, int[] rgb)[] SHEEP_COLORS;                                  // TODO(ent-a): fill
        /// <summary>Sorted colour pair 'a+b' -> lamb colour (9098).</summary>
        public static readonly Dictionary<string, string> DYE_MIX = new Dictionary<string, string>();       // TODO(ent-a): fill
        /// <summary>Converts (modifier * attribute)^2 to the steady ground speed in blocks/s.</summary>
        public const double MOB_SPEED_K = 0.98 / (1 - 0.546) * 20;

        // entities.js module init: MODELS, MOB_DEFS (+ the speed derivation loop 9089), MOB_TYPES,
        // ANIMAL_SPAWNS IIFE (9107-9128), the tables above and Part.kind / Part.leg; after Blocks.Init
        public static void Init() { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js goalSpeed (9090)
        public static double GoalSpeed(double speed, double m) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js sheepWool (9097): wool block id of the sheep's colour
        public static int SheepWool(Mob e) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js sheepMix (9100): a lamb's colour (draws a random when the pair does not mix)
        public static string SheepMix(string a, string b) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js sheepTint (9101): fur tint rgb / 255 of a natural colour, or null
        public static double[] SheepTint(Mob e) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js humanoid (9043-9058): HumanoidModel parts
        public static Part[] Humanoid(int armW, bool wide, bool hat = false) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js opart (8999-9008): an old maincraft-layout part in MC model space; piv / anim / rot optional
        public static Part Opart(double[] b, double[] at, int[] uv, double[] piv = null, string anim = null, double[] rot = null) { throw new NotImplementedException(); } // TODO(ent-a)
    }

    public static class EntityUtil
    {
        public const double MOB_DT = 0.05;                     // mob simulation step (20 Hz)
        public const int PATH_NODES = 1024, PATH_RANGE = 31;
        /// <summary>8-neighbour (dx, dz) pairs: +x, -x, +z, -z, then the diagonals.</summary>
        public static readonly int[] NB8 = { 1, 0, -1, 0, 0, 1, 0, -1, 1, 1, 1, -1, -1, 1, -1, -1 };
        public const int PG = 64, PO = 32, PSZ = PG * PG, PNONE = -2147483648;
        /// <summary>The shared A* scratch (main thread only).</summary>
        public static readonly PathState PS = new PathState();
        /// <summary>Face shades (+x, -x, top, bottom, +z, -z) of falling blocks and of dropped block items.</summary>
        public static readonly double[] FALL_SHADE = { 0.6, 0.6, 1, 0.5, 0.8, 0.8 }, ITEM_SHADE = { 0.72, 0.72, 1, 0.55, 0.82, 0.82 };
        /// <summary>waterFlow's shared result (not reentrant).</summary>
        public static readonly double[] FLOW_V = new double[3];
        /// <summary>entSweep scratch (not reentrant).</summary>
        public static readonly double[] SW_LO = new double[3], SW_HI = new double[3], SW_A0 = new double[3], SW_A1 = new double[3], SW_O = new double[3];

        // entities.js entCollides (9131-9144): an entity box (feet centre, width w, height h) against block collision shapes
        public static bool EntCollides(IBlockAccess world, double x, double y, double z, double w, double h) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js waterOwnHeight (9146-9152): FlowingFluid.getOwnHeight
        public static double WaterOwnHeight(IBlockAccess w, int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js waterHeight (9154): FluidState.getHeight
        public static double WaterHeight(IBlockAccess w, int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js waterFlow (9159-9186): FlowingFluid.getFlow; returns FLOW_V
        public static double[] WaterFlow(IBlockAccess w, int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js eyeInWall (9189-9196): LivingEntity.isInWall
        public static bool EyeInWall(IBlockAccess world, double x, double ey, double z, double w) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js entSweep (9201-9223): VoxelShape.collide along axis ax; the allowed move up to d
        public static double EntSweep(IBlockAccess world, double x, double y, double z, double w, double h, int ax, double d) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js entMove (9224-9242): the simple stepper of item entities (X, Z, then Y)
        public static void EntMove(IBlockAccess world, ItemEntity e, double dt) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js angDiff (9246): a - b wrapped into -pi..pi
        public static double AngDiff(double a, double b) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js turnTo (9248): LookControl.rotateTowards
        public static double TurnTo(double a, double b, double step) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js keepWithin (9250): Mth.rotateIfNecessary
        public static double KeepWithin(double a, double b, double max) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js heapPush (9268-9273): PS heap keyed by PS.f
        public static void HeapPush(int i) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js heapPop (9274-9287)
        public static int HeapPop() { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js octDist (9289): octile distance
        public static double OctDist(double ax, double az) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js slimeChunk (9291-9295)
        public static bool SlimeChunk(int cx, int cz, int seed) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js modelXform (9303-9315): a model vertex through the part parameters P (25 used) to camera-relative o
        public static void ModelXform(double[] P, double x, double y, double z, double[] o) { throw new NotImplementedException(); } // TODO(ent-a)
        /// <summary>ModelXform as a PoseFn for Game.PushModelBox (one cached delegate).</summary>
        public static readonly PoseFn ModelXformFn = ModelXform;
    }
}
