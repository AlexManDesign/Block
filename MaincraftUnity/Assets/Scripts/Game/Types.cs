// Shared gameplay types of the port (game.js, menus.js, save.js and entities.js use them across modules):
// item stacks, game mode and difficulty, the world record, the player-side state objects of Game, block
// particles, furnace records and the shared random generator. The JS kept these as plain objects; here
// each is a class (reference semantics, mutated in place like the original) or a small struct.
//
// API skeleton: bodies marked TODO(owner) are filled in by that implementer.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Maincraft
{
    /// <summary>
    /// An inventory stack {id, count, dur?, maxDur?}. A null slot is an empty slot. Stacks are mutated in
    /// place and moved by reference (cursor / slot swaps, chest arrays), and the JS compares them by
    /// identity (handTick, updateHotbar), so this is a class. dur === undefined (an unworn, stackable
    /// item) is dur == null.
    /// </summary>
    public sealed class ItemStack
    {
        public int id, count;
        public int? dur, maxDur;

        public ItemStack() { }
        public ItemStack(int id, int count) { this.id = id; this.count = count; }
        public ItemStack(int id, int count, int? dur, int? maxDur) { this.id = id; this.count = count; this.dur = dur; this.maxDur = maxDur; }
        /// <summary>{...s}: a copy with every field.</summary>
        public ItemStack Clone() => (ItemStack)MemberwiseClone();
    }

    /// <summary>Game mode; the save format and the STR keys keep the JS strings 'creative' / 'survival'.</summary>
    public enum GameMode { Creative, Survival }

    /// <summary>Minecraft's difficulty; the value is the index in Game.DIFFICULTIES (Difficulty.getId).</summary>
    public enum Difficulty { Peaceful = 0, Easy = 1, Normal = 2, Hard = 3 }

    /// <summary>JS string &lt;-&gt; enum helpers (the strings double as STR keys and are saved in the world record).</summary>
    public static class GameEnums
    {
        /// <summary>'creative' | 'survival'.</summary>
        public static string ModeName(GameMode m) => m == GameMode.Survival ? "survival" : "creative";
        /// <summary>meta.mode || 'creative': only 'survival' gives Survival (the JS compares with === 'survival' / 'creative').</summary>
        public static GameMode ParseMode(string s) => s == "survival" ? GameMode.Survival : GameMode.Creative;
        /// <summary>'peaceful' | 'easy' | 'normal' | 'hard' (DIFFICULTIES[d]).</summary>
        public static string DifficultyName(Difficulty d) => Game.DIFFICULTIES[(int)d];
        /// <summary>DIFFICULTIES.includes(s).</summary>
        public static bool TryParseDifficulty(string s, out Difficulty d)
        {
            int i = s == null ? -1 : Array.IndexOf(Game.DIFFICULTIES, s);
            d = i < 0 ? Difficulty.Normal : (Difficulty)i;
            return i >= 0;
        }
        /// <summary>DIFFICULTIES.includes(s) ? s : dflt ('normal' in startWorld and the create-world screen).</summary>
        public static Difficulty ParseDifficulty(string s, Difficulty dflt = Difficulty.Normal) => TryParseDifficulty(s, out var d) ? d : dflt;
    }

    /// <summary>
    /// The world record (save.js 'worlds' store, menus.js create-world, game.js startWorld / saveWorld). Field
    /// names are the JSON keys; null / 0 mean the key is absent where the JS tests for that. Converted to and
    /// from JSON by WorldMetaJson (the JS schema, so browser saves import).
    /// </summary>
    public sealed class WorldMeta
    {
        public string id, name;
        public int seed;
        public string mode;                 // 'creative' | 'survival' (null: absent -> 'creative')
        public string difficulty;           // DIFFICULTIES string (null / unknown: 'normal')
        public double created, lastPlayed;  // Date.now() ms (0: absent)
        public double? time;                // day time 0..1 (typeof meta.time === 'number')
        public ItemStack[] inv;             // 36 slots (null: absent; another length means a default inventory)
        public int sel;
        public PlayerRec player;            // null for a world never played (findSpawn)
        public double[] spawn;              // world spawn (player position), or null
        public int[] bedSpawn;              // bed head cell, or null
        /// <summary>The survival record as saved ({...game.surv}); applied with Object.assign semantics by Survival.Assign.</summary>
        public Dictionary<string, object> surv;
        public ShipRec ship;                // vessel under way (Ships.serialize), or null
        public double[][] boats;            // [[x, y, z, yaw], ...] (Boats.serialize), or null
        public List<MobRecord> mobs;        // Entities.saveMobs (records without an array p are dropped on load)
        public double[] inhab;              // Float64Array of colKey, ticks pairs (Game.inhabited)
        public uint[] popd;                 // Uint32Array of populated column keys (Entities.savePopulated)
        public OrderedMap<string, ItemStack[]> chests;    // "x,y,z" -> 27 slots (Game.chests aliases this)
        public OrderedMap<string, FurnaceRec> furnaces;   // "x,y,z" -> furnace
        /// <summary>Keys this port does not know, kept so a record round-trips unchanged.</summary>
        public Dictionary<string, object> extra;
    }

    /// <summary>meta.player: {pos, yaw, pitch, flying}.</summary>
    public sealed class PlayerRec
    {
        public double[] pos;
        public double yaw, pitch;
        public bool flying;
    }

    /// <summary>Game.surv: the survival state (saved as meta.surv). Initial values are the Game constructor's.</summary>
    public sealed class Survival
    {
        public double hp = 20;
        public int food = 20;
        public double sat = 5;
        public double air = 300;            // ticks (fractional: dt * 20), max 300, down to -20
        public double exh = 0;
        public int regenT = 0;
        public double starveT = 0;          // set by the constructor, never used
        public bool dead = false;
        public double hurtT = 0;            // HUD hurt flash seconds
        public int fire = -20;              // remainingFireTicks (rests at -20)
        public double invT = 0, lastHurt = 0;
        public double poisonT = 0, poisonAcc = 0;
        // Created lazily by survivalTick / foodTick (14157, 14202): absent from a record until survival ran
        // once in this session, and like every key carried over from world to world (this.surv lives as
        // long as the Game; Object.assign overwrites only the keys a record has).
        public double? tickAcc;             // s.tickAcc = (s.tickAcc ?? 0) + dt (|| 0 in the JS; never NaN)
        public int? ticks;                  // s.ticks = ((s.ticks | 0) + 1) % 2000000: its phase drives peaceful regeneration
        /// <summary>meta.surv keys this port does not know: Object.assign copies them in, {...this.surv} saves them again.</summary>
        public Dictionary<string, object> extra;

        // game.js 12863: Object.assign(this.surv, meta.surv) - only the keys d has are written (tickAcc / ticks
        // too, ticks through ToInt32 like `| 0`), unknown keys go into extra (overwriting by key)
        public void Assign(Dictionary<string, object> d) { throw new NotImplementedException(); } // TODO(game-a)
        // game.js 12979: {...this.surv} as a JSON record: the 14 constructor keys, then tickAcc and ticks only when
        // set, then the extra keys
        public Dictionary<string, object> ToRecord() { throw new NotImplementedException(); } // TODO(game-a)
    }

    /// <summary>Game.hand: the first-person hand animation on its own 20 Hz clock (game.js 12827).</summary>
    public sealed class HandState
    {
        public bool swinging = false;
        public int swingTime = 0;           // -1..5
        public double anim = 0, oAnim = 0, height = 0, oHeight = 0;
        public ItemStack item = null;       // the shown item (lags the held one while lowering; compared by reference)
        public double? xBob = null;         // null: not initialised yet
        public double yBob = 0, oxBob = 0, oyBob = 0;
        public double acc = 0, alpha = 0;
        public int hurtTime = 0;            // hurt tilt ticks left (10)
        public double hurtDir = 0;          // radians
    }

    /// <summary>input.keys: the logical movement keys (true / undefined / false in the JS; bool here).</summary>
    public sealed class InputKeys
    {
        public bool forward, back, left, right, jump, sneak, sprint;
    }

    /// <summary>Game.input, passed to Player.tick: {keys, joy, hunger, using}.</summary>
    public sealed class PlayerInput
    {
        public InputKeys keys = new InputKeys();
        public double[] joy = null;         // touch joystick [x, y] (y &lt; 0 forward), or null
        public int hunger;                  // surv.food
        public bool @using;                 // eating or drawing a trident
    }

    /// <summary>Game.mouse: buttons held and their repeat timers (performance.now ms).</summary>
    public sealed class MouseState
    {
        public bool l, r, rFirst;
        public double lT, rT;
    }

    /// <summary>Game.breaking: survival block breaking progress.</summary>
    public sealed class BreakState
    {
        public int x, y, z, id;
        public double p, time, snd, pt;
    }

    /// <summary>Game.eating.</summary>
    public sealed class EatState { public double t; }

    /// <summary>Game.charging: a trident drawn back.</summary>
    public sealed class ChargeState { public int slot; public double t; }

    /// <summary>Game.sleeping: in bed.</summary>
    public sealed class SleepState
    {
        public int[] head;                  // bed head cell
        public int f;                       // bed facing
        public int t;                       // ticks asleep
        public double acc;
        public double[] back;               // position before lying down
        public double yaw, pitch;
    }

    /// <summary>Game.respawning: a respawn waiting for its ground to load.</summary>
    public sealed class RespawnState { public int[] bed; }

    /// <summary>Game.touch: the touch controls' state (created by UI.buildTouch; null on desktop).</summary>
    public sealed class TouchInput
    {
        public double[] joy = null;
        public bool jump, sneak, sprint;
    }

    /// <summary>Game.localDifficulty result (DifficultyInstance): {eff, special}.</summary>
    public struct DifficultyInstance
    {
        public double eff, special;
        public DifficultyInstance(double eff, double special) { this.eff = eff; this.special = special; }
    }

    /// <summary>A block particle (MC TerrainParticle), game.js 14589.</summary>
    public sealed class Particle
    {
        public double x, y, z, px, py, pz, vx, vy, vz;
        public int age, life, layer;
        public double u, v, s;
        public bool ground;
    }

    /// <summary>A furnace record (meta.furnaces["x,y,z"]); shared by reference with UI.furnace and Ship stores.</summary>
    public sealed class FurnaceRec
    {
        public ItemStack[] slots = new ItemStack[3];   // input, fuel, output
        public double burn, burnMax, cook;
        public bool? lit;                   // missing in records created by UI.openFurnace
    }

    /// <summary>
    /// The decoded assets (main.js boot / game.js constructor): what the Renderer is built from plus the
    /// texture-name -> atlas layer map and the CPU atlas (spriteModel, Icons).
    /// </summary>
    public sealed class GameAssets
    {
        public byte[] atlasPng, sunPng, moonPng;
        public string atlasMetaJson;
        public Func<string, byte[]> entityImage;      // entity skin PNG by name
        public Dictionary<string, int> layers;        // texture name -> atlas layer
        public int count, cols;                       // layers, tiles per atlas row
        public byte[] atlasPixels;                    // RGBA, top row first (= Renderer.atlasPixels)
        public int atlasW;                            // atlas width in pixels (atlasImage.width)
    }

    /// <summary>A pose function (pmApply, modelXform): point (x, y, z) through parameters P, written into o.</summary>
    public delegate void PoseFn(double[] P, double x, double y, double z, double[] o);

    /// <summary>Math.random(): the shared main-thread generator, a double in [0, 1).</summary>
    public static class Rand
    {
        static readonly System.Random rng = new System.Random();
        public static double Next() => rng.NextDouble();
    }
}
