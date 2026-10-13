// Port of the module level of entities.js (8915-9315): the mob models and definitions, spawn tables, sheep
// colours (EntityData), and the shared entity physics and math helpers that boats.js and game.js use too:
// collision, water height and flow, sweeps, angle helpers, the A* scratch state and heap, the slime-chunk
// hash and the per-vertex model transform (EntityUtil).
// Mobs, item drops, arrows: models (Minecraft Java geometry), AI, spawning, physics, rendering.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    public static class EntityData
    {
        // Part: { box:[x,y,z,w,h,d] (MC y-down pixel space, relative to pivot), pivot:[x,y,z], uv:[u,v], rot:[rx,ry,rz], anim, mirror, inflate }
        // anim: 'head', 'legA', 'legB', 'armA', 'armB', 'body90', 'zarmR','zarmL','wingL','wingR', 'spiderL'...
        /// <summary>Mob models by type (8921-9041); cave_spider shares the spider's parts array; MODELS["player"] is compared by identity.</summary>
        public static readonly Dictionary<string, MobModel> MODELS = new Dictionary<string, MobModel>();
        /// <summary>Mob definitions by type (9062-9082), built after Items / Blocks init.</summary>
        public static readonly Dictionary<string, MobDef> MOB_DEFS = new Dictionary<string, MobDef>();
        /// <summary>Object.keys(MOB_DEFS): pig, cow, sheep, chicken, zombie, drowned, skeleton, creeper, spider, cave_spider, enderman, slime, salmon, shark.</summary>
        public static string[] MOB_TYPES;
        /// <summary>Creature spawns per biome id (9107-9128); null for biomes without any.</summary>
        public static AnimalSpawn[] ANIMAL_SPAWNS;
        /// <summary>Slime size -> [health, contact damage, movement attribute] (9093).</summary>
        public static readonly Dictionary<int, double[]> SLIME_SIZES = new Dictionary<int, double[]>();
        /// <summary>Natural sheep colours: name, cumulative odds, fur tint rgb (9095).</summary>
        public static (string name, double cum, int[] rgb)[] SHEEP_COLORS;
        /// <summary>Sorted colour pair 'a+b' -> lamb colour (9098).</summary>
        public static readonly Dictionary<string, string> DYE_MIX = new Dictionary<string, string>();
        // Ground mobs move like Minecraft's: MoveControl sets both the speed and the forward input to
        // modifier * movement-speed attribute, ground drag is 0.546 per tick, so the steady speed is
        // (modifier * attribute)^2 * 0.98 / (1 - 0.546) * 20 = 43.17 (modifier * attribute)^2 blocks/s.
        // speed = that at modifier 1; a goal's modifier m therefore scales it by m * m (panic, tempt,
        // wander, follow parent).
        /// <summary>Converts (modifier * attribute)^2 to the steady ground speed in blocks/s.</summary>
        public const double MOB_SPEED_K = 0.98 / (1 - 0.546) * 20;

        // sheepTint's results (c[2].map(v => v / 255)), one shared array per colour (read only)
        static double[][] sheepTints;
        // sheepWool's B['WOOL_' + colour.toUpperCase()] lookups by colour
        static readonly Dictionary<string, int> woolOf = new Dictionary<string, int>();
        static bool _inited;

        static Part P(double[] box, double[] pivot, int[] uv, string anim = null, double[] rot = null, bool mirror = false,
            double inflate = 0, bool creepy = false, bool trans = false)
            => new Part { box = box, pivot = pivot, uv = uv, anim = anim, rot = rot, mirror = mirror, inflate = inflate, creepy = creepy, trans = trans };
        static double[] D(params double[] v) => v;
        static int[] U(int u, int v) => new[] { u, v };

        // entities.js module init: MODELS, MOB_DEFS (+ the speed derivation loop 9089), MOB_TYPES,
        // ANIMAL_SPAWNS IIFE (9107-9128), the tables above and Part.kind / Part.leg; after Blocks.Init
        public static void Init()
        {
            if (_inited) return;
            _inited = true;
            Blocks.Init();
            InitModels();
            InitMobDefs();
            // slime sizes (Slime.setSize): [health size^2, contact damage size (none when tiny), movement
            // speed attribute 0.2 + 0.1 x size] (integer keys: the JS object lists them ascending)
            SLIME_SIZES[1] = new double[] { 1, 0, 0.3 };
            SLIME_SIZES[2] = new double[] { 4, 2, 0.4 };
            SLIME_SIZES[4] = new double[] { 16, 4, 0.6 };
            // sheep colours with the original's odds, wool block and fur tint
            SHEEP_COLORS = new (string, double, int[])[]
            {
                ("black", 0.05, new[] { 30, 30, 34 }), ("gray", 0.1, new[] { 125, 125, 125 }), ("light_gray", 0.15, new[] { 190, 190, 185 }),
                ("brown", 0.18, new[] { 114, 71, 40 }), ("pink", 0.1816, new[] { 237, 141, 172 }),
            };
            sheepTints = new double[SHEEP_COLORS.Length][];
            for (int i = 0; i < SHEEP_COLORS.Length; i++)
            {
                var c = SHEEP_COLORS[i].rgb;
                sheepTints[i] = new[] { c[0] / 255.0, c[1] / 255.0, c[2] / 255.0 };
            }
            DYE_MIX["black+white"] = "gray"; DYE_MIX["gray+white"] = "light_gray"; DYE_MIX["red+white"] = "pink"; DYE_MIX["red+yellow"] = "orange";
            DYE_MIX["blue+white"] = "light_blue"; DYE_MIX["green+white"] = "lime"; DYE_MIX["blue+green"] = "cyan"; DYE_MIX["blue+red"] = "purple";
            DYE_MIX["pink+purple"] = "magenta";
            MOB_TYPES = new[] { "pig", "cow", "sheep", "chicken", "zombie", "drowned", "skeleton", "creeper", "spider", "cave_spider", "enderman", "slime", "salmon", "shark" };
            InitAnimalSpawns();
        }

        static void InitModels()
        {
            double PI2 = Math.PI / 2;
            MODELS["pig"] = new MobModel { tex = "pig", tw = 64, th = 32, parts = new[] {
                P(D(-4, -4, -8, 8, 8, 8), D(0, 12, -6), U(0, 0), "head"),
                P(D(-2, 0, -9, 4, 3, 1), D(0, 12, -6), U(16, 16), "head"),
                P(D(-5, -10, -7, 10, 16, 8), D(0, 11, 2), U(28, 8), rot: D(PI2, 0, 0)),
                P(D(-2, 0, -2, 4, 6, 4), D(-3, 18, 7), U(0, 16), "legA"),
                P(D(-2, 0, -2, 4, 6, 4), D(3, 18, 7), U(0, 16), "legB", mirror: true),
                P(D(-2, 0, -2, 4, 6, 4), D(-3, 18, -5), U(0, 16), "legB"),
                P(D(-2, 0, -2, 4, 6, 4), D(3, 18, -5), U(0, 16), "legA", mirror: true),
            } };
            MODELS["cow"] = new MobModel { tex = "cow", tw = 64, th = 32, parts = new[] {
                P(D(-4, -4, -6, 8, 8, 6), D(0, 4, -8), U(0, 0), "head"),
                P(D(-5, -5, -4, 1, 3, 1), D(0, 4, -8), U(22, 0), "head"),
                P(D(4, -5, -4, 1, 3, 1), D(0, 4, -8), U(22, 0), "head"),
                P(D(-6, -10, -7, 12, 18, 10), D(0, 5, 2), U(18, 4), rot: D(PI2, 0, 0)),
                P(D(-2, 2, -8, 4, 6, 1), D(0, 5, 2), U(52, 0), rot: D(PI2, 0, 0)),
                P(D(-2, 0, -2, 4, 12, 4), D(-4, 12, 7), U(0, 16), "legA"),
                P(D(-2, 0, -2, 4, 12, 4), D(4, 12, 7), U(0, 16), "legB", mirror: true),
                P(D(-2, 0, -2, 4, 12, 4), D(-4, 12, -6), U(0, 16), "legB"),
                P(D(-2, 0, -2, 4, 12, 4), D(4, 12, -6), U(0, 16), "legA", mirror: true),
            } };
            MODELS["sheep"] = new MobModel { tex = "sheep", tw = 64, th = 32, parts = new[] {
                P(D(-3, -4, -6, 6, 6, 8), D(0, 6, -8), U(0, 0), "head"),
                P(D(-4, -10, -7, 8, 16, 6), D(0, 5, 2), U(28, 8), rot: D(PI2, 0, 0)),
                P(D(-2, 0, -2, 4, 12, 4), D(-3, 12, 7), U(0, 16), "legA"),
                P(D(-2, 0, -2, 4, 12, 4), D(3, 12, 7), U(0, 16), "legB", mirror: true),
                P(D(-2, 0, -2, 4, 12, 4), D(-3, 12, -5), U(0, 16), "legB"),
                P(D(-2, 0, -2, 4, 12, 4), D(3, 12, -5), U(0, 16), "legA", mirror: true),
            }, fur = new MobModel { tex = "sheep_fur", tw = 64, th = 32, parts = new[] {
                P(D(-3, -4, -4, 6, 6, 6), D(0, 6, -8), U(0, 0), "head", inflate: 0.6),
                P(D(-4, -10, -7, 8, 16, 6), D(0, 5, 2), U(28, 8), rot: D(PI2, 0, 0), inflate: 1.75),
                P(D(-2, 0, -2, 4, 6, 4), D(-3, 12, 7), U(0, 16), "legA", inflate: 0.5),
                P(D(-2, 0, -2, 4, 6, 4), D(3, 12, 7), U(0, 16), "legB", inflate: 0.5, mirror: true),
                P(D(-2, 0, -2, 4, 6, 4), D(-3, 12, -5), U(0, 16), "legB", inflate: 0.5),
                P(D(-2, 0, -2, 4, 6, 4), D(3, 12, -5), U(0, 16), "legA", inflate: 0.5, mirror: true),
            } } };
            MODELS["chicken"] = new MobModel { tex = "chicken", tw = 64, th = 32, parts = new[] {
                P(D(-2, -6, -2, 4, 6, 3), D(0, 15, -4), U(0, 0), "head"),
                P(D(-2, -4, -4, 4, 2, 2), D(0, 15, -4), U(14, 0), "head"),
                P(D(-1, -2, -3, 2, 2, 2), D(0, 15, -4), U(14, 4), "head"),
                P(D(-3, -4, -3, 6, 8, 6), D(0, 16, 0), U(0, 9), rot: D(PI2, 0, 0)),
                P(D(-1, 0, -3, 3, 5, 3), D(-2, 19, 1), U(26, 0), "legA"),
                P(D(-1, 0, -3, 3, 5, 3), D(1, 19, 1), U(26, 0), "legB", mirror: true),
                P(D(0, 0, -3, 1, 4, 6), D(-4, 13, 0), U(24, 13), "wingR"),
                P(D(-1, 0, -3, 1, 4, 6), D(4, 13, 0), U(24, 13), "wingL", mirror: true),
            } };
            // zombie: Minecraft's HumanoidModel, left limbs mirrored from the right ones (only the player's
            // model has separate left limbs on its skin)
            MODELS["zombie"] = new MobModel { tex = "zombie", tw = 64, th = 64, parts = Humanoid(4, false, true) };
            MODELS["drowned"] = new MobModel { tex = "drowned", tw = 64, th = 64, parts = Humanoid(4, false, true) };
            MODELS["skeleton"] = new MobModel { tex = "skeleton", tw = 64, th = 32, parts = Humanoid(2, false), heldItem = "item_bow" };
            MODELS["player"] = new MobModel { tex = "player", tw = 64, th = 64, parts = Humanoid(4, true, true) };
            MODELS["creeper"] = new MobModel { tex = "creeper", tw = 64, th = 32, parts = new[] {
                P(D(-4, -8, -4, 8, 8, 8), D(0, 6, 0), U(0, 0), "head"),
                P(D(-4, 0, -2, 8, 12, 4), D(0, 6, 0), U(16, 16)),
                P(D(-2, 0, -2, 4, 6, 4), D(-2, 18, 4), U(0, 16), "legA"),
                P(D(-2, 0, -2, 4, 6, 4), D(2, 18, 4), U(0, 16), "legB"),
                P(D(-2, 0, -2, 4, 6, 4), D(-2, 18, -4), U(0, 16), "legB"),
                P(D(-2, 0, -2, 4, 6, 4), D(2, 18, -4), U(0, 16), "legA"),
            } };
            {
                var p = new List<Part> {
                    P(D(-4, -4, -8, 8, 8, 8), D(0, 15, -3), U(32, 4), "head"),
                    P(D(-3, -3, -3, 6, 6, 6), D(0, 15, 0), U(0, 0)),
                    P(D(-5, -4, -6, 10, 8, 12), D(0, 15, 9), U(0, 12)),
                };
                double[] ys = { 0.785, 0.393, -0.393, -0.785 }, zs = { 2, 1, 0, -1 };
                for (int i = 0; i < 4; i++)
                {
                    p.Add(P(D(-15, -1, -1, 16, 2, 2), D(-4, 15, zs[i]), U(18, 0), "spiderR" + i, rot: D(0, ys[i], -0.785)));
                    p.Add(P(D(-1, -1, -1, 16, 2, 2), D(4, 15, zs[i]), U(18, 0), "spiderL" + i, rot: D(0, -ys[i], 0.785), mirror: true));
                }
                MODELS["spider"] = new MobModel { tex = "spider", tw = 64, th = 32, parts = p.ToArray() };
            }
            // cave spider: the spider model at 0.7 (CaveSpiderRenderer) with its own skin
            MODELS["cave_spider"] = new MobModel { tex = "cave_spider", tw = 64, th = 32, scale = 0.7, parts = MODELS["spider"].parts };
            MODELS["enderman"] = new MobModel { tex = "enderman", tw = 64, th = 32, halfSwing = true, parts = new[] {
                P(D(-4, -8, -4, 8, 8, 8), D(0, -14, 0), U(0, 0), "head", creepy: true),
                P(D(-4, -8, -4, 8, 8, 8), D(0, -14, 0), U(0, 16), "head", inflate: -0.5),
                P(D(-4, 0, -2, 8, 12, 4), D(0, -15, 0), U(32, 16)),
                P(D(-1, -2, -1, 2, 30, 2), D(-5, -13, 0), U(56, 0), "armR"),
                P(D(-1, -2, -1, 2, 30, 2), D(5, -13, 0), U(56, 0), "armL", mirror: true),
                P(D(-1, 0, -1, 2, 30, 2), D(-2, -6, 0), U(56, 0), "legA"),
                P(D(-1, 0, -1, 2, 30, 2), D(2, -6, 0), U(56, 0), "legB", mirror: true),
            } };
            // slime: opaque core, eyes and mouth; the outer jelly cube is drawn translucent afterwards
            MODELS["slime"] = new MobModel { tex = "slime", tw = 64, th = 32, parts = new[] {
                P(D(-3, 17, -3, 6, 6, 6), D(0, 0, 0), U(0, 16)),
                P(D(-3.25, 18, -3.5, 2, 2, 2), D(0, 0, 0), U(32, 0)),
                P(D(1.25, 18, -3.5, 2, 2, 2), D(0, 0, 0), U(32, 4)),
                P(D(0, 21, -3.5, 1, 1, 1), D(0, 0, 0), U(32, 8)),
                P(D(-4, 16, -4, 8, 8, 8), D(0, 0, 0), U(0, 0), trans: true),
            } };
            MODELS["salmon"] = new MobModel { tex = "salmon", tw = 32, th = 32, pitchY = 2.5, parts = new[] {
                Opart(D(2, 4, 3), D(0, 0.5, 1.5), U(22, 0)),
                Opart(D(3, 5, 8), D(0, 0, -4), U(0, 0)),
                Opart(D(3, 5, 8), D(0, 0, -12), U(0, 13), piv: D(0, 2.5, -8), anim: "tail"),
                Opart(D(0, 5, 6), D(0, 0, -19), U(20, 10), piv: D(0, 2.5, -8), anim: "tail"),
            } };
            MODELS["shark"] = new MobModel { tex = "shark", tw = 64, th = 64, scale = 1.25, pitchY = 5.5, parts = new[] {
                Opart(D(8, 7, 13), D(0, 2, -2), U(0, 0)),
                Opart(D(7, 6, 6), D(0, 2.5, 6), U(30, 20)),
                Opart(D(4, 3, 4), D(0, 3.5, 10.5), U(0, 36)),
                Opart(D(1, 5, 5), D(0, 9, -1), U(16, 36)),
                Opart(D(5, 1, 4), D(-5.5, 3, 2), U(42, 36), rot: D(0, 0, -0.25)),
                Opart(D(5, 1, 4), D(5.5, 3, 2), U(42, 41), rot: D(0, 0, 0.25)),
                Opart(D(4, 5, 11), D(0, 3.5, -13), U(0, 20), piv: D(0, 5.5, -8), anim: "tail"),
                Opart(D(1, 10, 6), D(0, 1, -21), U(28, 36), piv: D(0, 5.5, -8), anim: "tail"),
            } };
            // the render-time parse of Part.anim ('spiderR3': side R, leg 3), done once here
            foreach (var m in MODELS.Values)
            {
                ParseAnims(m.parts);
                if (m.fur != null) ParseAnims(m.fur.parts);
            }
        }

        static void ParseAnims(Part[] parts)
        {
            foreach (var part in parts)
            {
                string an = part.anim;
                part.leg = 0;
                switch (an)
                {
                    case null: part.kind = PartAnim.None; break;
                    case "head": part.kind = PartAnim.Head; break;
                    case "body": part.kind = PartAnim.Body; break;
                    case "legA": part.kind = PartAnim.LegA; break;
                    case "legB": part.kind = PartAnim.LegB; break;
                    case "armR": part.kind = PartAnim.ArmR; break;
                    case "armL": part.kind = PartAnim.ArmL; break;
                    case "wingR": part.kind = PartAnim.WingR; break;
                    case "wingL": part.kind = PartAnim.WingL; break;
                    case "tail": part.kind = PartAnim.Tail; break;
                    default:
                        // an.startsWith('spider'): k = +an.slice(-1), side an[6] === 'R' ? 1 : -1
                        if (an.StartsWith("spider", StringComparison.Ordinal))
                        {
                            part.kind = an.Length > 6 && an[6] == 'R' ? PartAnim.SpiderR : PartAnim.SpiderL;
                            part.leg = an[an.Length - 1] - '0';
                        }
                        else part.kind = PartAnim.None;
                        break;
                }
            }
        }

        // eye: the eye height (EntityType eyeHeight; 0.85 x the height when not given), where a mob sees
        // from, reads the light and is lit for drawing (getLightProbePosition), and suffocates (isInWall)
        static void InitMobDefs()
        {
            int IT(string k) => Items.IT[k];
            int CARROT = IT("CARROT"), POTATO = IT("POTATO"), BEETROOT = IT("BEETROOT"), WHEAT = IT("WHEAT"), WHEAT_SEEDS = IT("WHEAT_SEEDS"),
                PUMPKIN_SEEDS = IT("PUMPKIN_SEEDS"), MELON_SEEDS = IT("MELON_SEEDS"), BEETROOT_SEEDS = IT("BEETROOT_SEEDS"),
                PORKCHOP = IT("PORKCHOP"), COOKED_PORKCHOP = IT("COOKED_PORKCHOP"), BEEF = IT("BEEF"), COOKED_BEEF = IT("COOKED_BEEF"),
                LEATHER = IT("LEATHER"), MUTTON = IT("MUTTON"), COOKED_MUTTON = IT("COOKED_MUTTON"), CHICKEN = IT("CHICKEN"),
                COOKED_CHICKEN = IT("COOKED_CHICKEN"), FEATHER = IT("FEATHER"), ROTTEN_FLESH = IT("ROTTEN_FLESH"), IRON_INGOT = IT("IRON_INGOT"),
                COPPER_INGOT = IT("COPPER_INGOT"), BONE = IT("BONE"), ARROW = IT("ARROW"), GUNPOWDER = IT("GUNPOWDER"), STRING = IT("STRING"),
                SPIDER_EYE = IT("SPIDER_EYE"), ENDER_PEARL = IT("ENDER_PEARL"), SLIME_BALL = IT("SLIME_BALL"), RAW_SALMON = IT("RAW_SALMON"),
                COOKED_SALMON = IT("COOKED_SALMON"), BONE_MEAL = IT("BONE_MEAL"), RAW_COD = IT("RAW_COD"), COOKED_COD = IT("COOKED_COD");
            int[] ZOMBIE_RARE = { IRON_INGOT, CARROT, POTATO };
            // (Math.random() * n | 0)
            int R(int n) => (int)(Rand.Next() * n);
            // meat comes cooked from an animal that died burning (the loot tables' furnace_smelt when on fire)
            MOB_DEFS["pig"] = new MobDef { w = 0.9, h = 0.9, hp = 10, attr = 0.25, tempt = 1.2, food = new[] { CARROT, POTATO, BEETROOT }, panic = 1.25,
                drops = e => new[] { (e.fire > 0 ? COOKED_PORKCHOP : PORKCHOP, 1 + R(3)) }, passive = true };
            MOB_DEFS["cow"] = new MobDef { w = 0.9, h = 1.4, eye = 1.3, hp = 10, attr = 0.2, tempt = 1.25, parent = 1.25, food = new[] { WHEAT }, panic = 2.0,
                drops = e => new[] { (e.fire > 0 ? COOKED_BEEF : BEEF, 1 + R(3)), (LEATHER, R(3)) }, passive = true };
            MOB_DEFS["sheep"] = new MobDef { w = 0.9, h = 1.3, eye = 1.235, hp = 8, attr = 0.23, tempt = 1.1, food = new[] { WHEAT }, panic = 1.25,
                drops = e => new[] { (e.fire > 0 ? COOKED_MUTTON : MUTTON, 1 + R(2)), (e.sheared ? 0 : SheepWool(e), 1) }, passive = true };
            MOB_DEFS["chicken"] = new MobDef { w = 0.4, h = 0.7, eye = 0.644, hp = 4, attr = 0.25, tempt = 1.0, food = new[] { WHEAT_SEEDS, PUMPKIN_SEEDS, MELON_SEEDS, BEETROOT_SEEDS }, panic = 1.4,
                drops = e => new[] { (e.fire > 0 ? COOKED_CHICKEN : CHICKEN, 1), (FEATHER, R(3)) }, passive = true };
            // zombie loot: 0-2 rotten flesh, and when the player killed it one in 40 an iron ingot, a carrot
            // or a potato (killed_by_player, random_chance 0.025)
            MOB_DEFS["zombie"] = new MobDef { w = 0.6, h = 1.95, eye = 1.74, hp = 20, attr = 0.23, dmg = 3, armor = 2, follow = 35,
                drops = e => new[] { (ROTTEN_FLESH, R(3)), (e.byPlayer && Rand.Next() < 0.025 ? ZOMBIE_RARE[R(3)] : 0, 1) }, hostile = true, burns = true, sinks = true, undead = true };
            // drowned (Drowned, a zombie of the sea): the zombie's attributes; loot 0-2 rotten flesh and,
            // killed by the player, a copper ingot one time in 9 (random_chance 0.11)
            MOB_DEFS["drowned"] = new MobDef { w = 0.6, h = 1.95, eye = 1.74, hp = 20, attr = 0.23, dmg = 3, armor = 2, follow = 35,
                drops = e => new[] { (ROTTEN_FLESH, R(3)), (e.byPlayer && Rand.Next() < 0.11 ? COPPER_INGOT : 0, 1) }, hostile = true, burns = true, sinks = true, drowned = true, undead = true };
            MOB_DEFS["skeleton"] = new MobDef { w = 0.6, h = 1.99, eye = 1.74, hp = 20, attr = 0.25, dmg = 3,
                drops = e => new[] { (BONE, R(3)), (ARROW, R(3)) }, hostile = true, burns = true, ranged = true, sinks = true, undead = true };
            MOB_DEFS["creeper"] = new MobDef { w = 0.6, h = 1.7, hp = 20, attr = 0.25, wander = 0.8,
                drops = e => new[] { (GUNPOWDER, R(3)) }, hostile = true, creeper = true };
            MOB_DEFS["spider"] = new MobDef { w = 1.4, h = 0.9, eye = 0.65, hp = 16, attr = 0.3, wander = 0.8, dmg = 2, climber = true, neutralLight = 12, spiderAI = true,
                drops = e => new[] { (STRING, R(3)), (SPIDER_EYE, e.byPlayer && Rand.Next() < 1 / 3.0 ? 1 : 0) }, hostile = true };
            MOB_DEFS["cave_spider"] = new MobDef { w = 0.7, h = 0.5, eye = 0.45, hp = 12, attr = 0.3, wander = 0.8, dmg = 2, poison = 7, climber = true, neutralLight = 12, spiderAI = true,
                drops = e => new[] { (STRING, R(3)), (SPIDER_EYE, e.byPlayer && Rand.Next() < 1 / 3.0 ? 1 : 0) }, hostile = true };
            MOB_DEFS["enderman"] = new MobDef { w = 0.6, h = 2.9, eye = 2.55, hp = 40, attr = 0.3, dmg = 7, detect = 64,
                drops = e => new[] { (ENDER_PEARL, R(2)) }, hostile = true, neutral = true };
            MOB_DEFS["slime"] = new MobDef { w = 1.02, h = 1.02, eye = 0.6375, hp = 4, speed = 2.7, dmg = 2,
                drops = e => new[] { (SLIME_BALL, e.size == 1 ? R(3) : 0) }, hostile = true, slime = true };
            MOB_DEFS["salmon"] = new MobDef { w = 0.7, h = 0.4, eye = 0.26, hp = 3, speed = 1.6, fishAttr = 0.7,
                drops = e => new[] { (e.fire > 0 ? COOKED_SALMON : RAW_SALMON, 1), (Rand.Next() < 0.05 ? BONE_MEAL : 0, 1) }, passive = true, aquatic = true };
            MOB_DEFS["shark"] = new MobDef { w = 0.9, h = 0.8, hp = 24, speed = 4.2, fishAttr = 2.1, dmg = 6,
                drops = e => new[] { (e.fire > 0 ? COOKED_COD : RAW_COD, 1 + R(2)), (BONE, Rand.Next() < 0.35 ? 1 : 0) }, hostile = true, aquatic = true };
            foreach (var d in MOB_DEFS.Values) if (d.attr != 0) d.speed = MOB_SPEED_K * d.attr * d.attr;
        }

        // Minecraft's animals per biome (MobSpawnSettings, the creature category) for the kinds this game
        // has: [kind, weight, group min, max]; a null kind stands for the weight of those it lacks (horses,
        // wolves, rabbits, foxes, llamas, parrots, pandas, frogs, ...), so ours come as often as there; prob:
        // the chance a new chunk gets a group (creatureGenerationProbability). Biomes not listed have none.
        static void InitAnimalSpawns()
        {
            ANIMAL_SPAWNS = new AnimalSpawn[WG.BIOME_LIST.Length];
            SpawnEntry E(string kind, int weight, int min, int max) => new SpawnEntry(kind, weight, min, max);
            SpawnEntry[] farm(int other, params SpawnEntry[] extra)
            {
                var l = new List<SpawnEntry> { E("sheep", 12, 4, 4), E("pig", 10, 4, 4), E("chicken", 10, 4, 4), E("cow", 8, 4, 4) };
                l.AddRange(extra);
                l.Add(E(null, other, 1, 1));
                return l.ToArray();
            }
            // BI[n] (the biome names in BIOME_LIST order, OLD_GROWTH_TAIGA an alias), undefined: -1
            int bi(string n)
            {
                if (n == "OLD_GROWTH_TAIGA") return BI.OLD_GROWTH_SPRUCE_TAIGA;
                for (int i = 0; i < WG.BIOME_LIST.Length; i++) if (WG.BIOME_LIST[i][0] == n) return i;
                return -1;
            }
            void set(string names, SpawnEntry[] list, double prob = 0)
            {
                int total = 0;
                foreach (var e in list) total += e.weight;
                foreach (var n in names.Split(' '))
                {
                    int b = bi(n);
                    if (b < 0) continue;
                    var kept = new List<SpawnEntry>();
                    foreach (var e in list) if (e.weight > 0) kept.Add(e);
                    ANIMAL_SPAWNS[b] = new AnimalSpawn { list = kept, total = total, prob = prob != 0 ? prob : 0.1 };
                }
            }
            set("PLAINS SUNFLOWER_PLAINS", farm(6));                                   // + horses 5, donkeys 1
            set("FOREST", farm(5));                                                     // + wolves 5
            set("FLOWER_FOREST", farm(4));                                              // + rabbits 4
            set("BIRCH_FOREST OLD_GROWTH_BIRCH_FOREST DARK_FOREST", farm(0));
            set("TAIGA OLD_GROWTH_SPRUCE_TAIGA OLD_GROWTH_PINE_TAIGA", farm(20));       // + wolves 8, rabbits 4, foxes 8
            set("SNOWY_TAIGA", farm(20), 0.07);
            set("SAVANNA WINDSWEPT_SAVANNA", farm(12));                                 // + horses 1, donkeys 1, armadillos 10
            set("SAVANNA_PLATEAU", farm(20));                                           // + llamas 8 as well
            set("JUNGLE SPARSE_JUNGLE", farm(41, E("chicken", 10, 4, 4)));             // + parrots 40, pandas 1
            set("BAMBOO_JUNGLE", farm(120, E("chicken", 10, 4, 4)));                   // + parrots 40, pandas 80
            set("SWAMP", farm(10));                                                     // + frogs 10
            set("WINDSWEPT_HILLS WINDSWEPT_GRAVELLY_HILLS WINDSWEPT_FOREST", farm(5));  // + llamas 5
            set("MEADOW", new[] { E("sheep", 2, 2, 4), E(null, 3, 1, 1) });           // + donkeys 1, rabbits 2
            set("CHERRY_GROVE", new[] { E("pig", 1, 1, 2), E(null, 2, 1, 1) });       // + rabbits 2
        }

        // entities.js goalSpeed (9090)
        public static double GoalSpeed(double speed, double m) => speed * m * m;

        // entities.js sheepWool (9097): wool block id of the sheep's colour
        // B['WOOL_' + (e.color || 'white').toUpperCase()] || B.WOOL_WHITE (cached per colour)
        public static int SheepWool(Mob e)
        {
            string c = string.IsNullOrEmpty(e.color) ? "white" : e.color;
            if (!woolOf.TryGetValue(c, out int id))
            {
                id = Blocks.Id("WOOL_" + c.ToUpperInvariant());
                if (id == 0) id = B.WOOL_WHITE;
                woolOf[c] = id;
            }
            return id;
        }

        // entities.js sheepMix (9100): a lamb's colour (draws a random when the pair does not mix)
        // DYE_MIX[[a, b].sort().join('+')]: the default sort compares UTF-16 code units (ordinal) and puts
        // undefined last, join writes it as ''
        public static string SheepMix(string a, string b)
        {
            string lo = a, hi = b;
            if (lo == null || (hi != null && string.CompareOrdinal(lo, hi) > 0)) { lo = b; hi = a; }
            if (DYE_MIX.TryGetValue((lo ?? "") + "+" + (hi ?? ""), out var mix) && !string.IsNullOrEmpty(mix)) return mix;
            return Rand.Next() < 0.5 ? a : b;
        }

        // entities.js sheepTint (9101): fur tint rgb / 255 of a natural colour, or null
        // (the returned array is shared per colour: callers only read it)
        public static double[] SheepTint(Mob e)
        {
            for (int i = 0; i < SHEEP_COLORS.Length; i++) if (SHEEP_COLORS[i].name == e.color) return sheepTints[i];
            return null;
        }

        static Part HPart(double[] box, double[] pivot, int[] uv, string anim, bool mirror = false, double inflate = 0)
            => new Part { box = box, pivot = pivot, uv = uv, anim = anim, mirror = mirror, inflate = inflate };

        // entities.js humanoid (9043-9058): HumanoidModel parts
        public static Part[] Humanoid(int armW, bool wide, bool hat = false)
        {
            bool a4 = armW == 4;
            double[] arm = a4 ? D(-3, -2, -2, 4, 12, 4) : D(-1, -2, -1, 2, 12, 2);
            double[] armL = a4 ? D(-1, -2, -2, 4, 12, 4) : D(-1, -2, -1, 2, 12, 2);
            double[] leg = a4 ? D(-2, 0, -2, 4, 12, 4) : D(-1, 0, -1, 2, 12, 2);
            var parts = new List<Part> {
                HPart(D(-4, -8, -4, 8, 8, 8), D(0, 0, 0), U(0, 0), "head"),
                HPart(D(-4, 0, -2, 8, 12, 4), D(0, 0, 0), U(16, 16), "body"),
                HPart(arm, D(-5, 2, 0), U(40, 16), "armR"),
                HPart(armL, D(5, 2, 0), wide ? U(32, 48) : U(40, 16), "armL", mirror: !wide),
                HPart(leg, D(-1.9, 12, 0), U(0, 16), "legA"),
                HPart(leg, D(1.9, 12, 0), wide ? U(16, 48) : U(0, 16), "legB", mirror: !wide),
            };
            if (hat) parts.Add(HPart(D(-4, -8, -4, 8, 8, 8), D(0, 0, 0), U(32, 0), "head", inflate: 0.5));
            return parts.ToArray();
        }

        // entities.js opart (8999-9008): an old maincraft-layout part in MC model space; piv / anim / rot optional
        // Converts a part in the original maincraft layout ({b:[w,h,d], at: bottom-centre, y up, +z = front})
        // into the MC model space used here (y down, ground at 24, front = -z).
        public static Part Opart(double[] b, double[] at, int[] uv, double[] piv = null, string anim = null, double[] rot = null)
        {
            double w = b[0], h = b[1], d = b[2], ax = at[0], ay = at[1], az = at[2];
            double[] pv = piv != null ? new[] { piv[0], 24 - piv[1], -piv[2] } : new[] { ax, 24 - (ay + h / 2), -az };
            double x0 = ax - w / 2, y0 = 24 - (ay + h), z0 = -(az + d / 2);
            var p = new Part { box = new[] { x0 - pv[0], y0 - pv[1], z0 - pv[2], w, h, d }, pivot = pv, uv = uv };
            if (!string.IsNullOrEmpty(anim)) p.anim = anim;
            if (rot != null) p.rot = new[] { rot[0], -rot[1], -rot[2] };
            return p;
        }
    }

    public static class EntityUtil
    {
        public const double MOB_DT = 0.05;                     // mob simulation step (20 Hz, as in the original)
        public const int PATH_NODES = 1024, PATH_RANGE = 31;
        /// <summary>8-neighbour (dx, dz) pairs: +x, -x, +z, -z, then the diagonals.</summary>
        public static readonly int[] NB8 = { 1, 0, -1, 0, 0, 1, 0, -1, 1, 1, 1, -1, -1, 1, -1, -1 };
        // binary heap on node.f for A*
        // A* state in reused typed arrays over the columns within PATH_RANGE of the start (index
        // (dx + 32) * 64 + dz + 32); a cell's entries count only when its stamp is the current search's
        public const int PG = 64, PO = 32, PSZ = PG * PG, PNONE = -2147483648;
        /// <summary>The shared A* scratch (main thread only).</summary>
        public static readonly PathState PS = new PathState();
        // face shades of falling blocks and of dropped block items (+x, -x, top, bottom, +z, -z)
        /// <summary>Face shades (+x, -x, top, bottom, +z, -z) of falling blocks and of dropped block items.</summary>
        public static readonly double[] FALL_SHADE = { 0.6, 0.6, 1, 0.5, 0.8, 0.8 }, ITEM_SHADE = { 0.72, 0.72, 1, 0.55, 0.82, 0.82 };
        /// <summary>waterFlow's shared result (not reentrant).</summary>
        public static readonly double[] FLOW_V = new double[3];
        /// <summary>entSweep scratch (not reentrant).</summary>
        public static readonly double[] SW_LO = new double[3], SW_HI = new double[3], SW_A0 = new double[3], SW_A1 = new double[3], SW_O = new double[3];

        // entities.js entCollides (9131-9144): an entity box (feet centre, width w, height h) against block collision shapes
        // entity AABB collision with the world
        public static bool EntCollides(IBlockAccess world, double x, double y, double z, double w, double h)
        {
            double hw = w / 2;
            double x0 = x - hw, x1 = x + hw, y0 = y, y1 = y + h, z0 = z - hw, z1 = z + hw;
            int byE = (int)Math.Floor(y1 - 1e-7), bzS = (int)Math.Floor(z0), bzE = (int)Math.Floor(z1 - 1e-7), bxS = (int)Math.Floor(x0), bxE = (int)Math.Floor(x1 - 1e-7);
            for (int by = (int)Math.Floor(y0 - 0.5); by <= byE; by++)
                for (int bz = bzS; bz <= bzE; bz++)
                    for (int bx = bxS; bx <= bxE; bx++)
                    {
                        int id = world.GetBlock(bx, by, bz);
                        if (id == 0 || COLLIDE[id] == 0) continue;
                        var boxes = Shapes.CollisionBoxes(world, id, world.GetMeta(bx, by, bz), bx, by, bz);
                        if (boxes == null) continue;
                        foreach (var b in boxes) if (bx + b[0] < x1 && bx + b[3] > x0 && by + b[1] < y1 && by + b[4] > y0 && bz + b[2] < z1 && bz + b[5] > z0) return true;
                    }
            return false;
        }

        // entities.js waterOwnHeight (9146-9152): FlowingFluid.getOwnHeight
        // FlowingFluid.getOwnHeight of water at a cell: amount / 9 (a source, falling water 8 / 9), 0 for none
        public static double WaterOwnHeight(IBlockAccess w, int x, int y, int z)
        {
            int id = w.GetBlock(x, y, z);
            if (!IsWaterId(id)) return 0;
            if (id != B.WATER) return 8 / 9.0;
            int m = w.GetMeta(x, y, z);
            return (m & 8) != 0 ? 8 / 9.0 : (8 - (m & 7)) / 9.0;
        }

        // entities.js waterHeight (9154): FluidState.getHeight
        // FluidState.getHeight: 1 with water over it, else the own height
        public static double WaterHeight(IBlockAccess w, int x, int y, int z) => IsWaterId(w.GetBlock(x, y + 1, z)) ? 1 : WaterOwnHeight(w, x, y, z);

        // entities.js waterFlow (9159-9186): FlowingFluid.getFlow; returns FLOW_V
        // FlowingFluid.getFlow of the water at a cell (unit vector, or zeros): the pull of each side by the
        // height difference (over an open side by the water one below it, less 8/9); falling water against
        // a solid face is pulled down
        public static double[] WaterFlow(IBlockAccess w, int x, int y, int z)
        {
            int[] DX = World.DIRX_W, DZ = World.DIRZ_W;
            double own = WaterOwnHeight(w, x, y, z);
            double fx = 0, fy = 0, fz = 0;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DX[d], nz = z + DZ[d], n = w.GetBlock(nx, y, nz);
                if (IsLiquidId(n) && !IsWaterId(n)) continue;
                double h = WaterOwnHeight(w, nx, y, nz);
                double k = 0;
                if (h == 0) { if (COLLIDE[n] == 0) { double hb = WaterOwnHeight(w, nx, y - 1, nz); if (hb > 0) k = own - (hb - 8 / 9.0); } }
                else k = own - h;
                fx += DX[d] * k; fz += DZ[d] * k;
            }
            int id = w.GetBlock(x, y, z);
            if (id == B.WATER && (w.GetMeta(x, y, z) & 8) != 0)
            {
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], nz = z + DZ[d], a = w.GetBlock(nx, y, nz), b = w.GetBlock(nx, y + 1, nz);
                    if ((OPAQUE[a] != 0 && a != B.ICE && !IsWaterId(a)) || (OPAQUE[b] != 0 && b != B.ICE && !IsWaterId(b)))
                    {
                        double l2 = Math.Sqrt(fx * fx + fz * fz);   // Math.hypot(fx, fz)
                        if (l2 > 1e-5) { fx /= l2; fz /= l2; } else { fx = fz = 0; }
                        fy = -6;
                        break;
                    }
                }
            }
            double l = Math.Sqrt(fx * fx + fy * fy + fz * fz);   // Math.hypot(fx, fy, fz)
            if (l < 1e-5) { FLOW_V[0] = FLOW_V[1] = FLOW_V[2] = 0; } else { FLOW_V[0] = fx / l; FLOW_V[1] = fy / l; FLOW_V[2] = fz / l; }
            return FLOW_V;
        }

        // entities.js eyeInWall (9189-9196): LivingEntity.isInWall
        // LivingEntity.isInWall: a suffocating block (a full solid cube one cannot see through) at the eyes,
        // over a square 0.8 x the body's width
        public static bool EyeInWall(IBlockAccess world, double x, double ey, double z, double w)
        {
            double r = w * 0.4; int y = (int)Math.Floor(ey);
            for (int i = 0; i < 4; i++)
            {
                int id = world.GetBlock((int)Math.Floor((i & 1) != 0 ? x + r - 1e-7 : x - r), y, (int)Math.Floor((i & 2) != 0 ? z + r - 1e-7 : z - r));
                if (SOLID[id] != 0 && OPAQUE[id] != 0) return true;
            }
            return false;
        }

        // entities.js entSweep (9201-9223): VoxelShape.collide along axis ax; the allowed move up to d
        // VoxelShape.collide along one axis: how far (up to d) the body can go before the collision box of
        // a block ahead of it. A box it already overlaps is no obstacle, so a body caught inside a block can
        // walk out of it, as in Minecraft
        public static double EntSweep(IBlockAccess world, double x, double y, double z, double w, double h, int ax, double d)
        {
            double hw = w / 2; double[] lo = SW_LO, hi = SW_HI, a0 = SW_A0, a1 = SW_A1, o = SW_O; const double E = 1e-7;
            lo[0] = a0[0] = x - hw; lo[1] = a0[1] = y; lo[2] = a0[2] = z - hw;
            hi[0] = a1[0] = x + hw; hi[1] = a1[1] = y + h; hi[2] = a1[2] = z + hw;
            if (d > 0) a1[ax] += d; else a0[ax] += d;
            int byE = (int)Math.Floor(a1[1] - E), bzS = (int)Math.Floor(a0[2]), bzE = (int)Math.Floor(a1[2] - E), bxS = (int)Math.Floor(a0[0]), bxE = (int)Math.Floor(a1[0] - E);
            for (int by = (int)Math.Floor(a0[1] - 0.5); by <= byE; by++)
                for (int bz = bzS; bz <= bzE; bz++)
                    for (int bx = bxS; bx <= bxE; bx++)
                    {
                        int id = world.GetBlock(bx, by, bz);
                        if (id == 0 || COLLIDE[id] == 0) continue;
                        var boxes = Shapes.CollisionBoxes(world, id, world.GetMeta(bx, by, bz), bx, by, bz);
                        if (boxes == null) continue;
                        o[0] = bx; o[1] = by; o[2] = bz;
                        foreach (var b in boxes)
                        {
                            int k = 0;
                            for (; k < 3; k++) if (k != ax && !(o[k] + b[k] < hi[k] - E && o[k] + b[k + 3] > lo[k] + E)) break;
                            if (k < 3) continue;
                            if (d > 0) { double t = o[ax] + b[ax] - hi[ax]; if (t >= -E && t < d) d = t; }
                            else { double t = o[ax] + b[ax + 3] - lo[ax]; if (t <= E && t > d) d = t; }
                        }
                    }
            return Math.Abs(d) < E ? 0 : d;
        }

        // entMove's moveAx: steps of at most 0.4 along axis ax; true when the next step is blocked (no refinement)
        static bool MoveAx(IBlockAccess world, ItemEntity e, int ax, double d)
        {
            if (d == 0 || double.IsNaN(d)) return false;       // if (!d)
            double n = Math.Max(1, Math.Ceiling(Math.Abs(d) / 0.4)), s = d / n;
            for (int i = 0; i < n; i++)
            {
                // const p = e.pos.slice(); p[ax] += s
                double px = e.pos[0], py = e.pos[1], pz = e.pos[2];
                if (ax == 0) px += s; else if (ax == 1) py += s; else pz += s;
                if (EntCollides(world, px, py, pz, e.w, e.h)) return true;
                e.pos[ax] = ax == 0 ? px : ax == 1 ? py : pz;
            }
            return false;
        }

        // entities.js entMove (9224-9242): the simple stepper of item entities (X, Z, then Y)
        public static void EntMove(IBlockAccess world, ItemEntity e, double dt)
        {
            e.hitWall = false;
            bool hx = MoveAx(world, e, 0, e.vel[0] * dt), hz = MoveAx(world, e, 2, e.vel[2] * dt);
            if (hx) { e.vel[0] = 0; e.hitWall = true; }
            if (hz) { e.vel[2] = 0; e.hitWall = true; }
            bool hy = MoveAx(world, e, 1, e.vel[1] * dt);
            if (hy) { if (e.vel[1] < 0) e.onGround = true; e.vel[1] = 0; }
            else e.onGround = EntCollides(world, e.pos[0], e.pos[1] - 0.03, e.pos[2], e.w, e.h) && e.vel[1] <= 0;
        }

        // entities.js angDiff (9246): a - b wrapped into -pi..pi
        public static double AngDiff(double a, double b)
        {
            double d = (a - b) % (Math.PI * 2);
            if (d > Math.PI) d -= Math.PI * 2; else if (d < -Math.PI) d += Math.PI * 2;
            return d;
        }

        // entities.js turnTo (9248): LookControl.rotateTowards
        // from a toward b by at most step (LookControl.rotateTowards)
        public static double TurnTo(double a, double b, double step) { double d = AngDiff(b, a); return a + Math.Max(-step, Math.Min(step, d)); }

        // entities.js keepWithin (9250): Mth.rotateIfNecessary
        // a kept within max of b (Mth.rotateIfNecessary)
        public static double KeepWithin(double a, double b, double max) => b + Math.Max(-max, Math.Min(max, AngDiff(a, b)));

        // entities.js heapPush (9268-9273): PS heap keyed by PS.f
        // binary heap of cell indices ordered by f (an entry is pushed again when its cost drops; stale
        // entries are skipped by their done flag)
        public static void HeapPush(int i)
        {
            int[] h = PS.heap; double[] f = PS.f;
            int k = PS.hn++;
            h[k] = i;
            while (k > 0) { int p = (k - 1) >> 1; if (f[h[p]] <= f[h[k]]) break; int t = h[p]; h[p] = h[k]; h[k] = t; k = p; }
        }

        // entities.js heapPop (9274-9287)
        public static int HeapPop()
        {
            int[] h = PS.heap; double[] f = PS.f; int top = h[0], n = --PS.hn;
            if (n == 0) return top;
            h[0] = n > 0 ? h[n] : 0;   // (popping an empty heap: h[-1] is undefined, stored as 0)
            for (int i = 0; ;)
            {
                int a = i * 2 + 1, b = a + 1;
                int m = i;
                if (a < n && f[h[a]] < f[h[m]]) m = a;
                if (b < n && f[h[b]] < f[h[m]]) m = b;
                if (m == i) break;
                int t = h[m]; h[m] = h[i]; h[i] = t; i = m;
            }
            return top;
        }

        const double SQRT2 = 1.4142135623730951;   // Math.SQRT2
        // entities.js octDist (9289): octile distance
        public static double OctDist(double ax, double az)
        {
            ax = ax < 0 ? -ax : ax; az = az < 0 ? -az : az;
            return ax > az ? ax - az + SQRT2 * az : az - ax + SQRT2 * ax;
        }

        // entities.js slimeChunk (9291-9295)
        public static bool SlimeChunk(int cx, int cz, int seed)
        {
            unchecked
            {
                int t = (cx * 522133279) ^ (cz * 668265261) ^ seed;
                t = (t ^ (int)((uint)t >> 15)) * (int)2246822507u; t ^= (int)((uint)t >> 13);
                return (uint)t % 10 == 0;
            }
        }

        // entities.js modelXform (9303-9315): a model vertex through the part parameters P (25 used) to camera-relative o
        // A model part's vertex, local -> camera relative (written into o). P holds the part's parameters
        // (see buildModel): rotation Z, Y, X as cos/sin pairs (MC order: Z*Y*X applied to the vertex, X
        // first), the pivot, the model scale, the swim pitch, the sneak drop, the death tilt, the body
        // yaw and the entity and camera positions.
        public static void ModelXform(double[] P, double x, double y, double z, double[] o)
        {
            double y1 = y * P[0] - z * P[1], z1 = y * P[1] + z * P[0]; y = y1; z = z1;
            double x1 = x * P[2] + z * P[3]; z1 = -x * P[3] + z * P[2]; x = x1; z = z1;
            x1 = x * P[4] - y * P[5]; y1 = x * P[5] + y * P[4]; x = x1; y = y1;
            x += P[6]; y += P[7]; z += P[8];
            double wy = (24 + y) / 16 * P[9], wx = x / 16 * P[10], wz = z / 16 * P[10];
            if (P[11] != 0) { double yy = wy - P[14]; double y2 = yy * P[12] - wz * P[13]; wz = yy * P[13] + wz * P[12]; wy = y2 + P[14]; }
            if (P[15] != 0) wy -= 0.12;
            double death = P[16];
            if (death != 0) { double yy = wy * Math.Cos(death) - wx * Math.Sin(death); wx = wy * Math.Sin(death) + wx * Math.Cos(death); wy = yy; }
            double rx2 = wx * P[17] - wz * P[18], rz2 = wx * P[18] + wz * P[17];
            o[0] = P[19] + rx2 - P[22]; o[1] = P[20] + wy - P[23]; o[2] = P[21] + rz2 - P[24];
        }
        /// <summary>ModelXform as a PoseFn for Game.PushModelBox (one cached delegate).</summary>
        public static readonly PoseFn ModelXformFn = ModelXform;

        /// <summary>
        /// +x.toFixed(digits): x rounded to a multiple of 10^-digits by its exact binary value (a tie goes to the
        /// larger magnitude, as toFixed picks the larger n), read back as the nearest double (Entities.record).
        /// Exact while |x| * 10^digits stays below 2^51 (coordinates, angles, timers).
        /// </summary>
        public static double ToFixed(double x, int digits)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) >= 1e21) return x;
            double f = 1; for (int i = 0; i < digits; i++) f *= 10;
            double a = x < 0 ? -x : x;
            // a * f exactly = p + err (Dekker's product with Veltkamp splitting)
            double p = a * f;
            const double SPLIT = 134217729.0;   // 2^27 + 1
            double c = SPLIT * a, ah = c - (c - a), al = a - ah;
            c = SPLIT * f; double fh = c - (c - f), fl = f - fh;
            double err = ((ah * fh - p) + ah * fl + al * fh) + al * fl;
            double n = Math.Floor(p), r = p - n;
            // r and 0.5 are multiples of p's ulp, so only an exact half is decided by err
            if (r > 0.5 || (r == 0.5 && err >= 0)) n += 1;
            double v = n / f;
            return x < 0 ? -v : v;
        }
    }
}
