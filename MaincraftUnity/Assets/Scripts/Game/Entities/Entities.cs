// Port of entities.js class Entities, part 1 (9317-9699): every field of the class; dormant animals and
// save records, falling blocks, spawnMob, item drops, the per-frame update (spawning, the 20 Hz mob
// loop, items and arrows), the interpolation snapshot and the per-tick server logic of every mob
// (tickMobs). Part 2 (AI, targeting, path finding, physics) is Entities.Sim.cs, part 3 (fish, spawning,
// spawners, combat, drowned, rendering) Entities.Render.cs.
//
// API skeleton: bodies marked TODO(ent-a) are filled in by that implementer.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed partial class Entities
    {
        // ================================================================== fields (all parts)
        public Game game;
        public List<Mob> mobs = new List<Mob>();
        public List<ItemEntity> items = new List<ItemEntity>();
        public List<Arrow> arrows = new List<Arrow>();
        public List<FallingBlock> falling = new List<FallingBlock>();
        public double spawnT = 0;
        /// <summary>Animals of the columns not simulated now: colKey -> records (insertion order matters).</summary>
        public OrderedMap<long, List<MobRecord>> dormant = new OrderedMap<long, List<MobRecord>>();
        /// <summary>Columns that had their animals at generation; populatedList keeps the insertion order (saved as meta.popd).</summary>
        public HashSet<long> populated = new HashSet<long>();
        public List<long> populatedList = new List<long>();
        public double creatureT = 20;       // seconds until the next spawnAnimals pass
        /// <summary>Blocks mobs are breaking: "x,y,z" -> crack (read by Game.drawMobCracks).</summary>
        public OrderedMap<string, Crack> cracks = new OrderedMap<string, Crack>();
        public double fishT, sharkT;        // fish / shark spawn timers (lazily 0)
        public double acc;                  // 20 Hz mob tick accumulator
        public double? alpha;               // render interpolation acc / MOB_DT (this.alpha ?? 1)
        // findPath's hidden parameters and state (they stay set after a search)
        public bool pathBreaker, pathSwim, pathDry, pathSink, pathFloat, inPath;
        public int psx, psy, psz;           // search start cell
        /// <summary>Spawner block states by "x,y,z" (null until the first tickSpawners; render reads it).</summary>
        public OrderedMap<string, SpawnerState> spawnerState;
        // rendering buffers (reused every frame)
        public OrderedMap<string, VBuf> vbOpaque, vbTrans;   // per entity texture, insertion order = draw order
        public VBuf vbFire;                 // atlas-textured: flames, the bow, held items
        public double[] xp;                 // modelXform parameters (28)
        public double[] tpM;                // heldItem3p pose matrix (16)
        public double[] tpQ, tpO, tpT3;     // heldItem3p scratch points (3)
        public ushort[] tpL;                // held block face layers (6)
        public double[] tpC;                // held block colour (4)
        public VBuf tpTmp;
        public VBuf itemVB, itemTmp;        // renderItems
        public ushort[] itemL;              // renderItems face layers (6)
        public double[] itemC;              // renderItems colour (4)

        // ================================================================== 9317-9699
        // entities.js constructor (9318-9331)
        public Entities(Game game) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js clear (9332): every list and map (spawnT and creatureT stay)
        public void Clear() { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js keeps (9335): whether the mob is saved / stashed
        public bool Keeps(Mob e) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js record (9336-9340): the save record (toFixed rounding, undefined fields left out)
        public MobRecord Record(Mob e) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js revive (9341-9351): spawnMob from a record; null for an unknown type
        public Mob Revive(MobRecord r) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js stash (9352-9357): the mob's record into dormant (the caller removes the mob)
        public void Stash(Mob e) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js wake (9359-9367): dormant animals of loaded columns within 120 come back
        public void Wake() { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js saveMobs (9369-9374)
        public List<MobRecord> SaveMobs() { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js loadMobs (9375-9385): records filed under their column keys (null list: none)
        public void LoadMobs(List<MobRecord> list) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js spawnFalling (9394-9396)
        public void SpawnFalling(int id, int m, int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js fallMove (9398-9407): moves a falling block d down; true when something stopped it
        public bool FallMove(FallingBlock f, double d) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js tickFalling (9408-9424)
        public void TickFalling() { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js landFalling (9425-9432)
        public void LandFalling(FallingBlock f, int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js settleFalling (9434-9439): before a save every falling block lands
        public void SettleFalling() { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js spawnMob (9441-9483): opts {baby, size} as parameters (size 0: random for slimes)
        public Mob SpawnMob(string type, double x, double y, double z, bool baby = false, int size = 0) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js dropItem (9484-9487): vel null = random spread; dur / maxDur null = unworn
        public void DropItem(int id, int count, double x, double y, double z, double[] vel = null, int? dur = null, int? maxDur = null) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js update (9489-9620): spawning, the 20 Hz mob loop, items and arrows on frame dt
        public void Update(double dt) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js snapshot (9621-9625): previous-tick values for interpolation
        public void Snapshot(Mob e) { throw new NotImplementedException(); } // TODO(ent-a)
        // entities.js tickMobs (9627-9787): one 20 Hz tick of every mob (dt is MOB_DT)
        public void TickMobs(double dt) { throw new NotImplementedException(); } // TODO(ent-a)
    }
}
