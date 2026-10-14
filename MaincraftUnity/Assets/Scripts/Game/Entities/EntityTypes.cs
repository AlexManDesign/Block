// Types of entities.js (8915-11748): the mob (one class with every field the JS adds to the mob objects
// anywhere, defaults as in spawnMob 9441 or as the JS reads an absent field), item drops, arrows and
// tridents, falling blocks, the mob save record, mob definitions and models, spawner state, door cracks,
// the A* scratch state and path, and the pose buildModel draws.
using System;
using System.Collections.Generic;

namespace Maincraft
{
    /// <summary>A mob. JS 'undefined' that matters is a nullable field (or NaN / 0 where noted).</summary>
    public sealed class Mob
    {
        // ---- spawnMob (9443-9481)
        public string type;                 // key into MOB_DEFS and MODELS (kept as the string: records and render key by it)
        MobDef def;
        /// <summary>MOB_DEFS[type], looked up once (a mob's type never changes; the JS indexes MOB_DEFS every time).</summary>
        public MobDef Def => def ?? (def = EntityData.MOB_DEFS[type]);
        public double[] pos;
        public double[] vel = { 0, 0, 0 };  // blocks per second
        public double[] wish = { 0, 0, 0 }; // desired horizontal velocity (wish[1]: fish / drowned vertical)
        public double bodyYaw, tgtYaw, headYaw;
        public double hp, w, h;
        public bool onGround = false;
        public double walk = 0, walkAmt = 0, hurtT = 0, invT = 0, lastHurt = 0, deathT = 0, age = 0, fire = 0;
        public double aiT;                  // stroll pause (random 0..2 at spawn)
        public double[] tgt = null;         // random stroll target [x, z]
        public double tgtT;                 // seconds on the stroll target (set with tgt, ai 9985)
        public double[] tgtP;               // position when tgtT last restarted [x, z] (stuck check, ai 9985-9990)
        public double fleeT = 0, angryT = 0;
        public int noJump = 0;
        public bool wantJump = false;
        public double atkT = 0, drawT = -1, bowCd = 0, fuse = -1;
        public double hopT;                 // random 0..0.8 at spawn (slimes 0)
        public double stuckT = 0, detourT = 0, detX = 0, detZ = 0;
        public MobPath path = null;
        public int pathI = 0;
        public double pathT, pathTx = 0, pathTz = 0;   // pathT random 0..0.6 at spawn
        public double tpT = 0, stareT = 0, burnT = 0, wetT = 0, lavaT = 0;
        public double dryT = 0, swimT = 0, huntT = 0;
        /// <summary>Shark target: a mob, or the player when preyIsPlayer (JS 'player' | Mob | null).</summary>
        public Mob prey = null;
        public bool preyIsPlayer = false;
        public double[] esc = null;         // fish escape target [x, y, z]
        public double escT = 0;
        public double[] escD = null;        // escape direction [dx, dz]
        public bool fleeing = false, aggro = false;
        public double seeT;                 // random 0..0.5 at spawn
        public bool sees = false;
        public double unseenT = 0;
        public bool hunting = false, provoked = false;
        public double[] chase = null;       // melee chase point [x, z]
        public double chaseT = 0, seenT = 0, strafeT = -1;
        public double followMul;            // 1 + triangle(0, 0.11485) at spawn
        public bool strafeCw, strafeBack = false;
        public double lookT = 0;
        public bool lookP = false;
        public double lookYaw = 0, loveT = 0, breedCd = 0;
        public bool baby = false;
        public double growT = 0, eatT = 0;

        // ---- set at spawn for some kinds
        public bool persist;
        public int size;                    // slime size 1 | 2 | 4 (0: not a slime; JS undefined)
        public double squish, tsq;
        public double reinf, kbRes;         // zombie reinforcement chance, knockback resistance
        public bool breakDoors, leader;
        public string effect;               // spider effect on hard: speed, strength, regeneration, invisibility (or null)
        public string color;                // sheep wool colour (or null)
        public bool sheared;                // sheep shorn (game.js useEntity 13760, grass eating 10155, record sh)
        public double? swimPitch;           // body pitch: set for aquatic mobs at spawn and by the drowned's swimPose

        // ---- from save records (0: absent)
        public double eggT, underT, convT;

        // ---- created by the AI / physics (entities.js 9627-11404)
        public double idleT;                // noActionTime (s)
        public double regT;
        public int? air;                    // air ticks, lazily 300 (undefined until first needed)
        public double angryAge;
        public bool sunlit, climbing, hitWall;
        public double? fallY;               // highest y since leaving the ground (?? pos[1])
        public double[] swim;               // drowned swim target [x, y, z]
        public int[] doorPos;               // door being broken [x, y, z]
        public int doorT;
        public bool aiming, strafe;
        public int aiTick;
        public int? wanderTick;
        public double[] lastSeen;           // last seen player [x, z]
        public double[] panicTgt, panicFrom;
        public double dirT, hopYaw;
        public double[] shade;              // FleeSun destination [x, z]
        public double headStable = double.NaN;   // ++undefined is NaN in the JS: stays NaN until reset to 0
        public double? lastStableHead;
        public double breedT, temptCd;
        public bool tempted;
        public double[] wPush;              // water current push (lazy)
        public double wDepth;
        public bool? inWater;               // only fish set it (render tests === false)
        public double fSpd;
        public double? swimAmt;             // drowned swim pose amount (undefined for the others)
        public double swimSpd;
        public bool searchLand;
        public double[] beach, upTgt, waterTgt;
        public double beachT, beachT2;
        public bool spear;
        public int? throwTk;                // (e.throwTk ?? -1)
        public int seeTk;
        public ItemStack held;              // held item (drowned trident)
        public double? playerHitAge;        // age at the last hit by the player (undefined: never)
        public bool byPlayer;

        // ---- snapshot (9621): previous-tick values for interpolation
        public double[] pp;
        public double pyaw, phead, pwalk;
        public double? pitchP, pswim;

        // ---- render (11417-11424): interpolated pose
        public double[] rpos;
        public double ryaw, rhead, rwalk;
        public double? rpitch, rswim;
    }

    /// <summary>A dropped item {id, count, pos, vel, age, w, h, onGround, dur?, maxDur?}.</summary>
    public sealed class ItemEntity
    {
        public int id, count;
        public double[] pos, vel;
        public double age = 0;
        public double w = 0.25, h = 0.25;
        public bool onGround = false, hitWall;
        public int? dur, maxDur;
    }

    /// <summary>An arrow or trident in flight or stuck.</summary>
    public sealed class Arrow
    {
        public double[] pos, vel;           // vel in blocks per second
        public double age;
        public bool hostile;                // shot by a mob (hits the player)
        public double @base;                // skeleton arrow base damage
        public bool trident;
        public bool byPlayer, creativeOnly;
        public ItemStack item;              // the trident returned on pickup
        public bool stuck, dealt, bounced;
        public double[] dir;                // orientation once stuck (render)
    }

    /// <summary>A falling block (FallingBlockEntity) {id, m, x, y, z, py, vy, t}.</summary>
    public sealed class FallingBlock
    {
        public int id, m;
        public double x, y, z, py, vy;      // x, z: cell centre; py: previous tick's y
        public int t;
    }

    /// <summary>The persisted mob record (dormant animals, meta.mobs). Field names are the JSON keys; null: omitted.</summary>
    public sealed class MobRecord
    {
        public string t;                    // type
        public double[] p;                  // position, toFixed(2)
        public double y;                    // body yaw, toFixed(2)
        public double hp;
        public double baby;                 // growT toFixed(1), 0 for adults
        public string c;                    // colour
        public int sh;                      // sheared 1 / 0
        public double bc;                   // breed cooldown toFixed(1)
        public double? egg;                 // egg timer
        public int ps;                      // persist 1 / 0
        public int? sz;                     // slime size
        public double? iw, dc;              // InWaterTime, DrownedConversionTime
    }

    /// <summary>Mob attributes (MOB_DEFS 9062). Optional numbers are 0 when absent (the JS reads them with || fallbacks).</summary>
    public sealed class MobDef
    {
        public double w, h, eye, hp, attr, speed, tempt, parent, panic, wander, follow, dmg, armor, detect, poison, neutralLight, fishAttr;
        public int[] food;                  // breeding / tempt items (null: none, and no babies)
        public Func<Mob, (int id, int count)[]> drops;   // loot roll (draws randoms); id 0 or count 0 = nothing
        public bool passive, hostile, neutral, burns, sinks, undead, drowned, ranged, creeper, climber, spiderAI, slime, aquatic;
    }

    /// <summary>Animation tag of a model part, parsed from Part.anim.</summary>
    public enum PartAnim { None, Head, Body, LegA, LegB, ArmR, ArmL, WingR, WingL, Tail, SpiderR, SpiderL }

    /// <summary>A model part (MC model space: pixels, y down, ground at 24, front -z).</summary>
    public sealed class Part
    {
        public double[] box;                // x, y, z, w, h, d relative to the pivot
        public double[] pivot;
        public int[] uv;
        public double[] rot;                // rx, ry, rz (null: none)
        public string anim;                 // the JS tag ('head', 'legA', 'spiderR3', ...) or null
        public PartAnim kind;               // anim parsed (EntityData.Init)
        public int leg;                     // spider leg index (the tag's last digit)
        public bool mirror;
        public double inflate;
        public bool creepy;                 // enderman head raised 5 px when aggro
        public bool trans;                  // drawn translucent (slime jelly)
    }

    /// <summary>A mob model (MODELS 8921).</summary>
    public sealed class MobModel
    {
        public string tex;
        public int tw, th;
        public Part[] parts;
        public MobModel fur;                // sheep wool layer
        public string heldItem;             // atlas layer name of the held sprite (skeleton bow)
        public double scale = 1;            // (m.scale || 1)
        public bool halfSwing;
        public double pitchY;
    }

    /// <summary>ANIMAL_SPAWNS entry: [kind, weight, group min, max]; kind null stands for animals this game lacks.</summary>
    public struct SpawnEntry
    {
        public string kind;
        public int weight, min, max;
        public SpawnEntry(string kind, int weight, int min, int max) { this.kind = kind; this.weight = weight; this.min = min; this.max = max; }
    }

    /// <summary>A biome's creature spawns {list (weights > 0), total (all weights), prob}.</summary>
    public sealed class AnimalSpawn
    {
        public List<SpawnEntry> list;
        public int total;
        public double prob;
    }

    /// <summary>A monster spawner block's state (tickSpawners).</summary>
    public sealed class SpawnerState
    {
        public int x, y, z;
        public int delay;
        public double spin, ospin;          // mini-mob turn (degrees) now / at the previous tick
    }

    /// <summary>A block a mob is breaking (zombie at a door).</summary>
    public sealed class Crack
    {
        public int x, y, z, stage;
    }

    /// <summary>
    /// The A* scratch state PS (9257), reused across searches: a column's entries count only while its
    /// stamp is the current gen. Index (dx + 32) * 64 + (dz + 32) from the search start column.
    /// </summary>
    public sealed class PathState
    {
        public uint gen;
        public readonly uint[] stamp = new uint[EntityUtil.PSZ];
        public readonly double[] g = new double[EntityUtil.PSZ], f = new double[EntityUtil.PSZ];
        public readonly int[] y = new int[EntityUtil.PSZ], from = new int[EntityUtil.PSZ];
        public readonly byte[] done = new byte[EntityUtil.PSZ];
        public readonly double[] wd = new double[EntityUtil.PSZ];
        public readonly uint[] mStamp = new uint[EntityUtil.PSZ];      // last standY query per column
        public readonly int[] mY = new int[EntityUtil.PSZ], mV = new int[EntityUtil.PSZ];
        public readonly double[] mM = new double[EntityUtil.PSZ];      // and the path malus of the cell it found
        public readonly uint[] dStamp = new uint[EntityUtil.PSZ * 32];  // dangerAt, per cell
        public readonly byte[] dVal = new byte[EntityUtil.PSZ * 32];
        public readonly uint[] nStamp = new uint[EntityUtil.PSZ * 32];  // dangerNear, per cell
        public readonly byte[] nVal = new byte[EntityUtil.PSZ * 32];
        public readonly int[] heap = new int[EntityUtil.PATH_NODES * 8 + 8];
        public int hn;
    }

    /// <summary>An A* path: waypoints [x + 0.5, z + 0.5, floor y] in order; full: it reaches the goal.</summary>
    public sealed class MobPath
    {
        public readonly List<double> x = new List<double>(), z = new List<double>();
        public readonly List<int> y = new List<int>();
        public bool full;
        public int Count => x.Count;
    }

    /// <summary>Entities.raycastMob result {e, t}.</summary>
    public struct MobHit
    {
        public Mob e;
        public double t;
        public MobHit(Mob e, double t) { this.e = e; this.t = t; }
    }

    /// <summary>
    /// What buildModel draws: a mob, the spawner's mini-mob (11456) or the third-person player (11464), with
    /// the JS fallbacks resolved: pos = rpos || pos, yaw = ryaw ?? bodyYaw, head = rhead ?? headYaw ?? yaw,
    /// walk = rwalk ?? walk, swimPitch = (rpitch ?? swimPitch) || 0, swimAmt = rswim (drowned).
    /// </summary>
    public sealed class ModelPose
    {
        public string type;
        public double[] pos;
        public double yaw, head, walk, walkAmt;
        public double swimPitch, swimAmt;
        public double dispScale = 1;        // (e.dispScale || 1)
        public double pitch;                // head pitch (the player; 0 for mobs)
        public bool sneak;
        public double attack;               // attack swing 0..1 (the player)
        public ItemStack held;
        public bool riding;
        public int size;                    // slime size (0: none)
        public double squish;
        public bool baby;
        public bool? inWater;               // only === false matters (fish out of water)
        public bool? onGround;              // only === false matters (chicken wings)
        public double deathT, fuse, eatT;
        public bool aggro, aiming, spear;

        /// <summary>Every field to its default, as on a fresh object literal (render's spawner mini-mob and player poses).</summary>
        public ModelPose Reset()
        {
            type = null; pos = null; yaw = head = walk = walkAmt = swimPitch = swimAmt = 0; dispScale = 1; pitch = 0;
            sneak = false; attack = 0; held = null; riding = false; size = 0; squish = 0; baby = false;
            inWater = null; onGround = null; deathT = fuse = eatT = 0; aggro = aiming = spear = false;
            return this;
        }
    }
}
