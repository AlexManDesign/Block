// Port of entities.js class Entities, part 1 (9317-9787): every field of the class; dormant animals and
// save records, falling blocks, spawnMob, item drops, the per-frame update (spawning, the 20 Hz mob
// loop, items and arrows), the interpolation snapshot and the per-tick server logic of every mob
// (tickMobs). Part 2 (AI, targeting, path finding, physics) is Entities.Sim.cs, part 3 (fish, spawning,
// spawners, combat, drowned, rendering) Entities.Render.cs.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;
using static Maincraft.EntityData;
using static Maincraft.EntityUtil;

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
        // animals of the columns not simulated now (left behind or not yet loaded): column key -> records.
        // As in Minecraft, animals never despawn: they wait with their chunk and come back with it
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
        /// <summary>buildModel's input, reused for every mob (PoseOf), spawner mini-mob and the player model (Reset, then
        /// the JS literal's fields); each BuildModel call finishes with it before the next fill.</summary>
        public readonly ModelPose pose = new ModelPose();
        public double[] xp;                 // modelXform parameters (28)
        public double[] tpM;                // heldItem3p pose matrix (16)
        public double[] tpQ, tpO, tpT3;     // heldItem3p scratch points (3)
        public ushort[] tpL;                // held block face layers (6)
        public double[] tpC;                // held block colour (4)
        public VBuf tpTmp;
        public VBuf itemVB, itemTmp;        // renderItems
        public ushort[] itemL;              // renderItems face layers (6)
        public double[] itemC;              // renderItems colour (4)

        // wake's entries to revive (reused; the JS deletes from the Map while iterating it)
        readonly List<KeyValuePair<long, List<MobRecord>>> wakeList = new List<KeyValuePair<long, List<MobRecord>>>();

        // x || d for a number: d when x is 0 or NaN
        static double Or(double x, double d) => x != 0 && !double.IsNaN(x) ? x : d;
        // a number's truthiness (not 0, not NaN, not undefined)
        static bool Truthy(double? x) => x.HasValue && x.Value != 0 && !double.IsNaN(x.Value);
        // Math.hypot(a, b) / Math.hypot(a, b, c)
        static double Hyp(double a, double b) => Math.Sqrt(a * a + b * b);
        static double Hyp(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);
        static int Fl(double x) => (int)Math.Floor(x);
        // ToInt32 of a number (x | 0, x >> n): NaN and infinities 0, others wrapped modulo 2^32
        static int ToInt32(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
            double t = Math.Truncate(x);
            if (t >= int.MinValue && t <= int.MaxValue) return (int)t;
            t %= 4294967296.0; if (t < 0) t += 4294967296.0;
            return unchecked((int)(uint)t);
        }
        // r.p[k]: undefined (NaN in arithmetic) past the end
        static double At(double[] a, int k) => k < a.Length ? a[k] : double.NaN;

        // ================================================================== 9317-9787
        // entities.js constructor (9318-9331)
        public Entities(Game game)
        {
            this.game = game;
            // (mobs, items, arrows, falling, spawnT 0, dormant, populated, creatureT 20, cracks: the field initializers)
        }

        // entities.js clear (9332): every list and map (spawnT and creatureT stay)
        public void Clear()
        {
            mobs.Clear(); items.Clear(); arrows.Clear(); falling.Clear(); dormant.Clear(); populated.Clear(); populatedList.Clear(); cracks.Clear();
        }

        // ------------------------------------------------------------------ keeping animals
        // entities.js keeps (9335): whether the mob is saved / stashed
        public bool Keeps(Mob e) { var d = MOB_DEFS[e.type]; return e.deathT <= 0 && ((!d.hostile && !d.aquatic) || e.persist); }

        // entities.js record (9336-9340): the save record (toFixed rounding, undefined fields left out)
        public MobRecord Record(Mob e)
        {
            return new MobRecord
            {
                t = e.type, p = new[] { ToFixed(e.pos[0], 2), ToFixed(e.pos[1], 2), ToFixed(e.pos[2], 2) }, y = ToFixed(e.bodyYaw, 2), hp = e.hp,
                baby = e.baby ? ToFixed(e.growT, 1) : 0, c = e.color, sh = e.sheared ? 1 : 0, bc = ToFixed(Or(e.breedCd, 0), 1),
                egg = Truthy(e.eggT) ? ToFixed(e.eggT, 1) : (double?)null, ps = e.persist ? 1 : 0, sz = e.size != 0 ? e.size : (int?)null,
                iw = Truthy(e.underT) ? ToFixed(e.underT, 2) : (double?)null, dc = Truthy(e.convT) ? ToFixed(e.convT, 2) : (double?)null,   // (InWaterTime, DrownedConversionTime)
            };
        }

        // entities.js revive (9341-9351): spawnMob from a record; null for an unknown type
        public Mob Revive(MobRecord r)
        {
            if (r.t == null || !MOB_DEFS.ContainsKey(r.t)) return null;
            var e = SpawnMob(r.t, At(r.p, 0), At(r.p, 1), At(r.p, 2), r.baby > 0, r.sz ?? 0);
            e.bodyYaw = e.headYaw = e.tgtYaw = Or(r.y, 0); e.hp = Or(r.hp, e.hp);
            if (r.baby > 0) e.growT = r.baby;
            if (!string.IsNullOrEmpty(r.c)) e.color = r.c;
            e.sheared = r.sh != 0; e.breedCd = Or(r.bc, 0); if (Truthy(r.egg)) e.eggT = r.egg.Value; if (r.ps != 0) e.persist = true;
            if (Truthy(r.iw)) e.underT = r.iw.Value; if (Truthy(r.dc)) e.convT = r.dc.Value;
            Snapshot(e);
            return e;
        }

        // entities.js stash (9352-9357): the mob's record into dormant (the caller removes the mob)
        public void Stash(Mob e)
        {
            long k = World.ColKey(Fl(e.pos[0]) >> 4, Fl(e.pos[2]) >> 4);
            var L = dormant.Get(k);
            if (L == null) dormant.Set(k, L = new List<MobRecord>());
            L.Add(Record(e));
        }

        // entities.js wake (9359-9367): dormant animals of loaded columns within 120 come back
        // dormant animals whose column is loaded again within reach come back (checked once a second)
        public void Wake()
        {
            var g = game; var w = g.world; var p = g.player;
            // (the JS deletes each entry it revives while iterating the Map; the tests draw no randoms and
            // reviving touches neither the map nor the tests, so the entries are collected first)
            wakeList.Clear();
            foreach (var kv in dormant)
            {
                long k = kv.Key;
                int cx = (int)(k / 65536 - (k % 65536 < 0 ? 1 : 0)) - 32768, cz = (int)(k % 65536) - 32768;   // Math.floor(k / 65536), k % 65536
                if (Hyp(cx * 16 + 8 - p.pos[0], cz * 16 + 8 - p.pos[2]) > 120 || !w.IsLoaded(cx * 16 + 8, cz * 16 + 8)) continue;
                wakeList.Add(kv);
            }
            foreach (var kv in wakeList)
            {
                dormant.Remove(kv.Key);
                foreach (var r in kv.Value) Revive(r);
            }
            wakeList.Clear();
        }

        // all kept animals (simulated and waiting) for the save, and back from it
        // entities.js saveMobs (9369-9374)
        public List<MobRecord> SaveMobs()
        {
            var output = new List<MobRecord>();
            foreach (var e in mobs) if (Keeps(e)) output.Add(Record(e));
            foreach (var kv in dormant) foreach (var r in kv.Value) output.Add(r);
            return output;
        }

        // entities.js loadMobs (9375-9385): records filed under their column keys (null list: none)
        public void LoadMobs(List<MobRecord> list)
        {
            dormant.Clear();
            if (list == null) return;
            foreach (var r in list)
            {
                if (r == null || r.p == null) continue;
                long k = World.ColKey(ToInt32(Math.Floor(At(r.p, 0))) >> 4, ToInt32(Math.Floor(At(r.p, 2))) >> 4);
                var L = dormant.Get(k);
                if (L == null) dormant.Set(k, L = new List<MobRecord>());
                L.Add(r);
            }
        }

        // ------------------------------------------------------------------ falling blocks
        // Minecraft's FallingBlockEntity, a 0.98 box: per tick motion.y -= 0.04, move, motion *= 0.98.
        // It stops on the collision boxes of blocks and on boats; mobs and the player it passes through
        // (they end up inside it). On the ground it turns into a block in the cell it is in
        // (blockPosition) when that cell can be replaced and the cell below cannot be fallen into
        // (FallingBlock.isFree); otherwise, on a slab, a path, a torch or a boat afloat, it drops as an
        // item. Still falling after 600 ticks it drops as an item too.
        // entities.js spawnFalling (9394-9396)
        public void SpawnFalling(int id, int m, int x, int y, int z)
        {
            falling.Add(new FallingBlock { id = id, m = m, x = x + 0.5, z = z + 0.5, y = y, py = y, vy = 0, t = 0 });
        }

        // entities.js fallMove (9398-9407): moves a falling block d down; true when something stopped it
        // moves a falling block d down (Entity.move); true when something stopped it
        public bool FallMove(FallingBlock f, double d)
        {
            double dy = EntSweep(game.world, f.x, f.y, f.z, 0.98, 0.98, 1, d);
            var bs = game.boats; double r = Boats.BOAT_W / 2 + 0.49 - 1e-7;
            if (bs != null) foreach (var b in bs.list)
                {
                    double top = b.pos[1] + Boats.BOAT_H;
                    if (Math.Abs(b.pos[0] - f.x) < r && Math.Abs(b.pos[2] - f.z) < r && top <= f.y + 1e-7 && top - f.y > dy) dy = top - f.y;
                }
            f.y += dy;
            return Math.Abs(dy - d) > 1e-5;
        }

        // entities.js tickFalling (9408-9424)
        public void TickFalling()
        {
            var w = game.world; var F = falling;
            for (int i = F.Count - 1; i >= 0; i--)
            {
                var f = F[i];
                f.py = f.y;
                int bx = Fl(f.x), bz = Fl(f.z);
                if (!w.IsLoaded(bx, bz)) continue;
                f.t++;
                f.vy -= 0.04;
                bool ground = FallMove(f, f.vy); int by = Fl(f.y);
                // concrete powder sets as soon as it falls into water (FallingBlockEntity / ConcretePowderBlock)
                if (CONCRETE_OF[f.id] != 0 && IsWaterId(w.GetBlock(bx, by, bz))) { w.SetBlock(bx, by, bz, CONCRETE_OF[f.id], 0); F.RemoveAt(i); continue; }
                if (ground) { LandFalling(f, bx, by, bz); F.RemoveAt(i); continue; }
                if ((f.t > 100 && by <= WORLD_MIN_Y) || f.t > 600) { DropItem(f.id, 1, f.x, f.y, f.z); F.RemoveAt(i); continue; }
                f.vy *= 0.98;
            }
        }

        // landFalling's free(id): air, a fluid or a replaceable block
        static bool FallFree(int id) => id == 0 || SHAPE[id] == SH.WATER || SHAPE[id] == SH.LAVA || (FLAGS[id] & BF_REPLACE) != 0;

        // entities.js landFalling (9425-9432)
        public void LandFalling(FallingBlock f, int x, int y, int z)
        {
            var w = game.world; int cur = w.GetBlock(x, y, z);
            if (FallFree(cur) && !FallFree(w.GetBlock(x, y - 1, z)))
            {
                if (cur != 0 && (FLAGS[cur] & BF_REPLACE) != 0 && SHAPE[cur] != SH.WATER && SHAPE[cur] != SH.LAVA) w.BreakBlock(x, y, z, true);
                w.SetBlock(x, y, z, f.id, f.m);
            }
            else DropItem(f.id, 1, f.x, f.y, f.z);
        }

        // entities.js settleFalling (9434-9439): before a save every falling block lands
        // before a save: every falling block lands at once, so none is lost with the world
        public void SettleFalling()
        {
            // (for...of over the array: a block a landing sets falling is visited too)
            for (int i = 0; i < falling.Count; i++)
            {
                var f = falling[i];
                if (FallMove(f, WORLD_MIN_Y - 1 - f.y)) LandFalling(f, Fl(f.x), Fl(f.y), Fl(f.z));
            }
            falling.Clear();
        }

        // entities.js spawnMob (9441-9483): opts {baby, size} as parameters (size 0: random for slimes)
        public Mob SpawnMob(string type, double x, double y, double z, bool baby = false, int size = 0)
        {
            var d = MOB_DEFS[type];
            double yaw = Rand.Next() * 6.2832;
            var e = new Mob { type = type, pos = new[] { x, y, z }, bodyYaw = yaw, tgtYaw = yaw, headYaw = yaw, hp = d.hp, w = d.w, h = d.h };
            // (the object literal's other fields are the Mob field defaults; its randoms in literal order)
            // behaviour state (see ai / aiFish)
            e.aiT = Rand.Next() * 2;
            e.hopT = Rand.Next() * 0.8;
            e.pathT = Rand.Next() * 0.6;
            e.seeT = Rand.Next() * 0.5;
            // Mob.finalizeSpawn: follow range x (1 + triangle(0, 0.11485)), the "random spawn bonus"
            e.followMul = 1 + (Rand.Next() - Rand.Next()) * 0.11485; e.strafeCw = Rand.Next() < 0.5;
            if (baby && d.food != null) { e.baby = true; e.growT = 1200; e.w = d.w * 0.5; e.h = d.h * 0.5; e.persist = true; }
            if (d.slime)
            {
                int sz = size != 0 ? size : SLIME_SIZE_PICK[(int)(Rand.Next() * 3)]; var S = SLIME_SIZES[sz];
                e.size = sz; e.hp = S[0]; e.w = e.h = 0.52 * sz; e.squish = 0; e.tsq = 0; e.hopT = 0;   // SlimeMoveControl: the first jump at once
            }
            var g = game;
            bool ld = type == "zombie" || type == "drowned" || type == "spider";   // (g.localDifficulty always exists: ld is an object)
            DifficultyInstance L = ld ? g.LocalDifficulty(Fl(x), Fl(z)) : default;
            if ((type == "zombie" || type == "drowned") && ld)
            {
                // Zombie.finalizeSpawn / handleAttributes, by the local difficulty's special part f: door
                // breaking for f / 10 of them (on hard only it is used), a follow range bonus, knockback
                // resistance 0..0.05, a reinforcement chance 0..0.1; a leader one time in 20 x f: 2..5 x the
                // health, +0.5..0.75 reinforcement chance, breaks doors
                double f = L.special;
                e.reinf = Rand.Next() * 0.1; e.kbRes = Rand.Next() * 0.05;
                double fb = Rand.Next() * 1.5 * f; if (fb > 1) e.followMul *= 1 + fb;
                e.breakDoors = Rand.Next() < f * 0.1 && type == "zombie";
                if (Rand.Next() < f * 0.05) { e.reinf += Rand.Next() * 0.25 + 0.5; e.hp = d.hp * (2 + Rand.Next() * 3); e.breakDoors = type == "zombie"; e.leader = true; }
            }
            if (type == "spider" && ld && g.difficulty == Difficulty.Hard && Rand.Next() < 0.1 * L.special)
            {
                // Spider.finalizeSpawn on hard: one of speed (2 in 5), strength, regeneration, invisibility
                e.effect = SPIDER_EFFECTS[(int)(Rand.Next() * 5)];
            }
            if (type == "sheep")
            {
                double r = Rand.Next(); e.color = "white";
                foreach (var c in SHEEP_COLORS) if (r < c.cum) { e.color = c.name; break; }
            }
            if (d.aquatic) e.swimPitch = 0;
            mobs.Add(e);
            return e;
        }
        static readonly int[] SLIME_SIZE_PICK = { 1, 2, 4 };
        static readonly string[] SPIDER_EFFECTS = { "speed", "speed", "strength", "regeneration", "invisibility" };

        // entities.js dropItem (9484-9487): vel null = random spread; dur / maxDur null = unworn
        public void DropItem(int id, int count, double x, double y, double z, double[] vel = null, int? dur = null, int? maxDur = null)
        {
            if (id == 0 || count <= 0) return;
            items.Add(new ItemEntity { id = id, count = count, pos = new[] { x, y, z }, vel = vel ?? new[] { (Rand.Next() - 0.5) * 2, 3, (Rand.Next() - 0.5) * 2 },
                age = 0, w = 0.25, h = 0.25, onGround = false, dur = dur, maxDur = maxDur });
        }

        // entities.js update (9489-9620): spawning, the 20 Hz mob loop, items and arrows on frame dt
        public void Update(double dt)
        {
            var g = game; var w = g.world; var p = g.player;
            if (w == null) return;
            // ---- spawning
            spawnT -= dt;
            if (spawnT <= 0)
            {
                spawnT = 1.0;
                Wake();
                // LevelChunk.inhabitedTime: the columns within 8 chunks of the player count the time (ticks)
                if (g.inhabited != null)
                {
                    var ih = g.inhabited; int pcx = Fl(p.pos[0]) >> 4, pcz = Fl(p.pos[2]) >> 4;
                    for (int a = -8; a <= 8; a++) for (int b = -8; b <= 8; b++) { long k = World.ColKey(pcx + a, pcz + b); ih.Set(k, Or(ih.Get(k), 0) + 20); }
                }
                // newly generated columns get their animals, once ever (lit, so the light rule can be read)
                // (World.cols is a Dictionary: insertion order only while no column was unloaded, see the port notes)
                foreach (var c in w.cols.Values) if (c.state == 2 && !populated.Contains(c.key)) { populated.Add(c.key); populatedList.Add(c.key); Populate(c); }
                if ((creatureT -= 1) <= 0) { creatureT = 20; SpawnAnimals(); }
                int hostile = 0, fish = 0, sharks = 0;
                foreach (var m in mobs) { var md = MOB_DEFS[m.type]; if (md.aquatic) { fish++; if (m.type == "shark") sharks++; } else if (md.hostile) hostile++; }
                // hostile mobs spawn in creative too (as in Minecraft); they just never target a creative player
                if (hostile < 22 && g.difficulty != Difficulty.Peaceful) TrySpawnMonsters();   // one monster cap day and night, as in Minecraft
                fishT = Or(fishT, 0) - 1;
                if (fishT <= 0) { fishT = 5; if (fish < 10) TrySpawnFish(); }
                sharkT = Or(sharkT, 0) - 1;
                if (sharkT <= 0) { sharkT = 12; if (sharks < 2 && g.difficulty != Difficulty.Peaceful) TrySpawnShark(); }
            }
            // ---- mobs: fixed 20 Hz simulation (as in the original); rendering interpolates between ticks
            acc = Math.Min(Or(acc, 0) + dt, MOB_DT * 8);
            int steps = 0;
            while (acc >= MOB_DT && steps < 3)
            {
                foreach (var e in mobs) Snapshot(e);
                TickSpawners();
                TickMobs(MOB_DT);
                TickFalling();
                acc -= MOB_DT; steps++;
            }
            if (acc > MOB_DT) acc = MOB_DT;
            alpha = acc / MOB_DT;
            // ---- item entities
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                it.age += dt;
                if (it.age > 300) { items.RemoveAt(i); continue; }
                if (!w.IsLoaded(Fl(it.pos[0]), Fl(it.pos[2]))) continue;
                double dx = p.pos[0] - it.pos[0], dy = p.pos[1] + 0.8 - it.pos[1], dz = p.pos[2] - it.pos[2];
                double d2 = dx * dx + dy * dy + dz * dz;
                if (it.age > 0.6 && d2 < 9 && !g.surv.dead)
                {
                    if (d2 < 1.6)
                    {
                        int rest = g.GiveItem(it.id, it.count, it.dur, it.maxDur);
                        if (rest > 0) it.count = rest; else { items.RemoveAt(i); continue; }
                    }
                    else { double f = 12 / Math.Sqrt(d2); it.vel[0] = dx * f * 0.3; it.vel[1] = dy * f * 0.3; it.vel[2] = dz * f * 0.3; }
                }
                bool inW = IsWaterId(w.GetBlock(Fl(it.pos[0]), Fl(it.pos[1]), Fl(it.pos[2])));
                if (inW) it.vel[1] += (1 - it.vel[1]) * Math.Min(1, dt * 4); else it.vel[1] -= 16 * dt;
                EntMove(w, it, dt);
                if (it.onGround) { it.vel[0] *= Math.Max(0, 1 - dt * 8); it.vel[2] *= Math.Max(0, 1 - dt * 8); }
                if (EntCollides(w, it.pos[0], it.pos[1], it.pos[2], it.w, it.h)) it.pos[1] += dt * 3;
                // merge stacks
                if ((i & 7) == (g.frameN & 7)) for (int j = 0; j < items.Count; j++)
                    {
                        var o = items[j];
                        if (o == it || o.id != it.id || o.dur != null) continue;
                        if (Math.Abs(o.pos[0] - it.pos[0]) + Math.Abs(o.pos[1] - it.pos[1]) + Math.Abs(o.pos[2] - it.pos[2]) < 0.8 && o.count + it.count <= Lang.MaxStack(it.id))
                        {
                            o.count += it.count; items.RemoveAt(i); break;
                        }
                    }
            }
            // ---- arrows: sub-stepped, swept against the player so fast arrows can't pass through
            // (the JS gives a.pos / a.vel / a.dir new arrays; nothing else holds them, so they change in place)
            for (int i = arrows.Count - 1; i >= 0; i--)
            {
                var a = arrows[i];
                a.age += dt;
                if (a.stuck)
                {
                    // AbstractArrow.playerTouch: only what the player threw or shot can be picked up (a
                    // creative player's trident by a creative player, for nothing); a minute in the ground and
                    // it is gone (tickDespawn)
                    if (a.byPlayer && !g.surv.dead && Hyp(p.pos[0] - a.pos[0], p.pos[1] + 0.9 - a.pos[1], p.pos[2] - a.pos[2]) < 1.5)
                    {
                        if (a.creativeOnly) { if (g.mode != GameMode.Survival) { arrows.RemoveAt(i); continue; } }
                        else if (a.item != null ? g.GiveItem(a.item.id, 1, a.item.dur, a.item.maxDur) == 0 : g.GiveItem(Items.IT["ARROW"], 1) == 0) { arrows.RemoveAt(i); Sfx.Pop(); continue; }
                    }
                    if (a.age > 60) arrows.RemoveAt(i);
                    continue;
                }
                if (a.age > 8) { arrows.RemoveAt(i); continue; }
                a.vel[1] -= 20 * dt;
                // AbstractArrow.tick: x0.99 a tick in the air, x0.6 in water (getWaterInertia; a trident 0.99)
                { double dr = Math.Pow(IsWaterId(w.GetBlock(Fl(a.pos[0]), Fl(a.pos[1]), Fl(a.pos[2]))) && !a.trident ? 0.6 : 0.99, dt * 20); a.vel[0] *= dr; a.vel[1] *= dr; a.vel[2] *= dr; }
                double n = Math.Max(1, Math.Ceiling(Hyp(a.vel[0], a.vel[1], a.vel[2]) * dt / 0.3)), s = dt / n;
                for (int k = 0; k < n; k++)
                {
                    double ox = a.pos[0], oy = a.pos[1], oz = a.pos[2], mx = a.vel[0] * s, my = a.vel[1] * s, mz = a.vel[2] * s;
                    // AbstractArrow.tick: the move clipped against the blocks' collision shapes (a thin pane
                    // or a door stops it), then the entity on the clipped path
                    double ml = Hyp(mx, my, mz); RayHit bh = ml > 0 ? Shapes.Raycast(w, ox, oy, oz, mx / ml, my / ml, mz / ml, ml, false, true) : null;
                    double bt = bh != null ? bh.t / ml : 2;
                    // ThrownTrident.onHitEntity: 8 damage, then it falls back (x -0.01 across, x -0.1 up and
                    // down) and harms nothing more
                    if (a.trident && !a.dealt)
                    {
                        bool hitP = false; Mob hit = null; double ht = bt;
                        if (a.hostile && !g.surv.dead)
                        {
                            if (Shapes.RayBox(ox, oy, oz, mx, my, mz, p.pos[0] - 0.3, p.pos[1], p.pos[2] - 0.3, p.pos[0] + 0.3, p.pos[1] + p.h, p.pos[2] + 0.3, out double t, out _) && t <= 1 && t < ht) { hitP = true; ht = t; }
                        }
                        if (!a.hostile) foreach (var m in mobs)
                            {
                                if (m.deathT > 0) continue;
                                double r = m.w / 2;
                                if (Shapes.RayBox(ox, oy, oz, mx, my, mz, m.pos[0] - r, m.pos[1], m.pos[2] - r, m.pos[0] + r, m.pos[1] + m.h, m.pos[2] + r, out double t, out _) && t <= 1 && t < ht) { hit = m; hitP = false; ht = t; }
                            }
                        if (hitP || hit != null)
                        {
                            a.pos[0] = ox + mx * ht; a.pos[1] = oy + my * ht; a.pos[2] = oz + mz * ht;
                            if (hitP) { if (g.Hurt(8, true, a.pos)) { p.vel[0] += a.vel[0] * 0.15; p.vel[2] += a.vel[2] * 0.15; } }
                            else DamageMob(hit, 8, a.pos);
                            a.vel[0] *= -0.01; a.vel[1] *= -0.1; a.vel[2] *= -0.01; a.dealt = true;
                            Sfx.Trident(TridentSound.Hit);
                            break;
                        }
                    }
                    if (a.hostile && !a.trident && !g.surv.dead)
                    {
                        if (Shapes.RayBox(ox, oy, oz, mx, my, mz, p.pos[0] - 0.3, p.pos[1], p.pos[2] - 0.3, p.pos[0] + 0.3, p.pos[1] + p.h, p.pos[2] + 0.3, out double t, out _) && t <= 1 && t < bt && !a.bounced)
                        {
                            if (g.Hurt(Math.Ceiling(Hyp(a.vel[0], a.vel[1], a.vel[2]) / 20 * a.@base), true, a.pos)) { p.vel[0] += a.vel[0] * 0.15; p.vel[2] += a.vel[2] * 0.15; arrows.RemoveAt(i); break; }
                            // AbstractArrow.onHitEntity: a hit that does no harm (creative, or just hurt) turns the
                            // arrow back at a tenth of its speed
                            a.vel[0] *= -0.1; a.vel[1] *= -0.1; a.vel[2] *= -0.1; a.bounced = true;
                            break;
                        }
                    }
                    if (bh != null)
                    {
                        double l = Or(Hyp(a.vel[0], a.vel[1], a.vel[2]), 1);
                        if (a.dir == null) a.dir = new double[3];
                        a.dir[0] = a.vel[0] / l; a.dir[1] = a.vel[1] / l; a.dir[2] = a.vel[2] / l;
                        a.pos[0] = bh.px + a.dir[0] * 0.05; a.pos[1] = bh.py + a.dir[1] * 0.05; a.pos[2] = bh.pz + a.dir[2] * 0.05;
                        a.vel[0] = a.vel[1] = a.vel[2] = 0; a.stuck = true; a.age = 0;
                        if (a.trident) Sfx.Trident(TridentSound.Ground);
                        break;
                    }
                    a.pos[0] = ox + mx; a.pos[1] = oy + my; a.pos[2] = oz + mz;
                }
            }
        }

        // entities.js snapshot (9621-9625): previous-tick values for interpolation
        public void Snapshot(Mob e)
        {
            if (e.pp == null) e.pp = new double[3];
            e.pp[0] = e.pos[0]; e.pp[1] = e.pos[1]; e.pp[2] = e.pos[2];
            e.pyaw = e.bodyYaw; e.phead = e.headYaw; e.pwalk = e.walk; e.pitchP = e.swimPitch; e.pswim = e.swimAmt;
        }

        // entities.js tickMobs (9627-9787): one 20 Hz tick of every mob (dt is MOB_DT)
        public void TickMobs(double dt)
        {
            var g = game; var w = g.world; var p = g.player;
            bool hunt = g.mode == GameMode.Survival && !g.surv.dead;
            // Level.isDay (sky darkening under 4) and the light of the open sky now (raw brightness 15 -
            // darkening), for the undead burning in it
            int sd = g.SkyDarken(); bool sunUp = sd < 4; double sunL = (15 - sd) / 15.0, sunF = sunL / (4 - 3 * sunL);
            double ignite = Math.Max(0, (sunF - 0.4) * 2 / 30);
            // Drowned.okTarget: by day the drowned go only for a player in the water (one in a boat is not),
            // at night for any
            bool huntWet = hunt && (!sunUp || (p.inWater && !(g.boats != null && g.boats.riding != null)));
            for (int i = mobs.Count - 1; i >= 0; i--)
            {
                var e = mobs[i]; var d = MOB_DEFS[e.type];
                e.age += dt;
                double dx = p.pos[0] - e.pos[0], dz = p.pos[2] - e.pos[2], dist = Hyp(dx, dz);
                double h = Hyp(dx, p.pos[1] + 0.9 - (e.pos[1] + e.h * 0.5), dz);
                if (e.pos[1] < WORLD_MIN_Y - 8) { mobs.RemoveAt(i); continue; }
                // Mob.checkDespawn: on peaceful every monster is gone at once
                if (d.hostile && g.difficulty == Difficulty.Peaceful) { EndDoor(e); mobs.RemoveAt(i); continue; }
                // animals out of reach wait with their column (and come back with it)
                if (Keeps(e))
                {
                    if (dist > 140 || !w.IsLoaded(Fl(e.pos[0]), Fl(e.pos[2]))) { Stash(e); mobs.RemoveAt(i); continue; }
                    // (Mob.checkDespawn: the idle time of an animal counts too, beyond 32 blocks; not of one kept)
                    e.idleT = h < 32 || e.persist ? 0 : Or(e.idleT, 0) + dt;
                }
                else
                {
                    // Mob.checkDespawn: gone beyond 128 blocks (fish 64), and beyond 32 now and then (1 in 800 a
                    // tick) once idle for 600 ticks; within 32 blocks the idle time starts over. Monsters idle
                    // three times as fast in light brighter than half (Monster.updateNoActionTime)
                    double far = d.aquatic && !d.hostile ? 64 : 128;
                    if (h > far || !w.IsLoaded(Fl(e.pos[0]), Fl(e.pos[2]))) { mobs.RemoveAt(i); continue; }
                    if (h < 32) e.idleT = 0;
                    else
                    {
                        e.idleT = Or(e.idleT, 0) + dt * (d.hostile && !d.aquatic && LightAt(e) > 12 ? 3 : 1);
                        if (e.idleT > 30 && Rand.Next() < 1 - Math.Pow(1 - 1 / 800.0, dt * 20)) { mobs.RemoveAt(i); continue; }
                    }
                }
                if (e.deathT > 0) { e.deathT += dt; if (e.deathT > 1.0) mobs.RemoveAt(i); continue; }
                if (e.hurtT > 0) e.hurtT -= dt;
                if (e.invT > 0) e.invT -= dt;
                if (e.angryT > 0) e.angryT -= dt;
                if (e.tpT > 0) e.tpT -= dt;
                e.atkT += dt;
                // MobEffects.REGENERATION I (a spider's on hard): 1 health every 50 ticks
                if (e.effect == "regeneration" && (e.regT = Or(e.regT, 0) + dt) >= 2.5) { e.regT = 0; e.hp = Math.Min(d.hp, e.hp + 1); }
                if (e.breedCd > 0) e.breedCd -= dt;
                // lava and fire
                // Entity.lavaHurt: 15 s of fire every tick in lava and 4 damage; a fire block 8 s and 1 damage
                // (a hurt every 10 ticks); MagmaBlock.stepOn: 1 from the block it stands on (Entity.getOnPos)
                int fb = w.GetBlock(Fl(e.pos[0]), Fl(e.pos[1] + 0.3), Fl(e.pos[2]));
                if (fb == B.LAVA || fb == B.FIRE) e.fire = Math.Max(e.fire, fb == B.LAVA ? 15 : 8);
                if (e.lavaT > 1e-6) e.lavaT -= dt;   // (10 ticks, whatever the rounding of the steps)
                else if (fb == B.LAVA || fb == B.FIRE) { e.lavaT = 0.5; DamageMob(e, fb == B.LAVA ? 4 : 1, null, null, true); if (e.hp <= 0) continue; }
                else if (e.onGround && !d.aquatic && Shapes.OnPosBlock(w, e.pos[0], e.pos[1], e.pos[2], e.w) == B.MAGMA_BLOCK) { e.lavaT = 0.5; DamageMob(e, 1, null, null, true); if (e.hp <= 0) continue; }
                if (e.fire > 0) e.fire -= dt;
                // LivingEntity.baseTick: 1 damage while the eyes are inside a suffocating block (isInWall)
                if (EyeInWall(w, e.pos[0], e.pos[1] + EyeH(e), e.pos[2], e.w)) { DamageMob(e, 1, null, null, false, true); if (e.hp <= 0) continue; }
                if (d.aquatic) { AiFish(e, d, dt, dx, dz, h); continue; }
                bool inW = WaterAt(e);
                if (inW) e.fire = 0;
                // LivingEntity.baseTick: with the eyes in water the air (300 ticks) runs out, 1 a tick; at -20
                // it is 0 again and the mob takes 2 (drowning, no cause for panic); out of the water it comes
                // back 4 a tick. The undead breathe under water
                if (!d.undead)
                {
                    if (e.air == null) e.air = 300;
                    if (EyeInWater(e)) { if (--e.air <= -20) { e.air = 0; DamageMob(e, 2, null, null, false, true); if (e.hp <= 0) continue; } }
                    else if (e.air < 300) e.air = Math.Min(300, e.air.Value + 4);
                }
                // enderman: provoked by a stare, hates water
                if (e.type == "enderman")
                {
                    if (e.angryT <= 0 && hunt)
                    {
                        if (Stared(e, h)) { e.stareT += dt; if (e.stareT > 0.25) { e.angryT = 20 + Rand.Next() * 19; e.stareT = 0; e.angryAge = e.age; } } else e.stareT = 0;
                    }
                    // Enderman.customServerAiStep: in daylight under the open sky, 30 s after it last took a
                    // target, now and then it lets go and teleports away (same odds as the undead catching fire)
                    if (sunUp && e.age - Or(e.angryAge, 0) > 30 && (w.GetLight(Fl(e.pos[0]), Fl(e.pos[1] + EyeH(e)), Fl(e.pos[2])) >> 4) >= 15 &&
                        Rand.Next() < 1 - Math.Pow(1 - ignite, dt * 20))
                    {
                        e.angryT = 0; e.hunting = false; e.provoked = false;
                        if (TeleportNear(e, e.pos[0], e.pos[2], 32)) e.tpT = 1;
                    }
                    int body = w.GetBlock(Fl(e.pos[0]), Fl(e.pos[1] + e.h * 0.5), Fl(e.pos[2]));
                    if (IsWaterId(body) || inW)
                    {
                        e.wetT -= dt;
                        if (e.wetT <= 0) { e.wetT = 0.5; DamageMob(e, 1, null); if (e.hp <= 0) continue; }
                        if (e.tpT <= 0 && TeleportNear(e, e.pos[0], e.pos[2], 24)) e.tpT = 1;
                    }
                }
                e.aggro = e.angryT > 0;
                // undead in daylight (Mob.isSunBurnTick): with the open sky over the eyes (sky light 15: a
                // leaf or any block above shades it) they catch fire now and then (random x 30 < (light - 0.4)
                // x 2 a tick: 4% in full sun) and burn 8 s, also on into the shade; water puts it out
                e.sunlit = sunUp && (w.GetLight(Fl(e.pos[0]), Fl(e.pos[1] + EyeH(e)), Fl(e.pos[2])) >> 4) >= 15;
                if (d.burns && e.sunlit && !inW && Rand.Next() < 1 - Math.Pow(1 - ignite, dt * 20)) e.fire = Math.Max(e.fire, 8);
                // burning: 1 a second (Entity.baseTick, onFire)
                if (e.fire > 0 && !inW)
                {
                    e.burnT += dt;
                    if (e.burnT >= 1) { e.burnT -= 1; DamageMob(e, 1, null); if (e.hp <= 0) continue; }
                }
                else e.burnT = 0;
                // Zombie.tick (ConversionTracker): a zombie with its eyes under water for 600 ticks starts
                // turning into a drowned (shaking); 300 ticks more with them still under and it is one, a new
                // drowned in its place at full health. Eyes out of the water: the count starts again and a
                // conversion under way is called off
                if (e.type == "zombie")
                {
                    if (IsWaterId(w.GetBlock(Fl(e.pos[0]), Fl(e.pos[1] + EyeH(e)), Fl(e.pos[2]))))
                    {
                        if (e.convT > 0) { if ((e.convT -= dt) <= 0) { DrownZombie(e, i); continue; } }
                        else if ((e.underT = Or(e.underT, 0) + dt) >= 30) e.convT = 15.05;
                    }
                    else { e.underT = 0; e.convT = 0; }
                }
                Ai(e, d, dt, dx, dz, h, d.drowned ? huntWet : hunt, inW);
                if (e.deathT > 0 || i >= mobs.Count || mobs[i] != e) continue;
                // don't walk off cliffs (drops over 3 blocks; a slime has no path finding: it hops where it
                // faces), and an enderman not into water (its water path malus is -1)
                if (e.onGround && !d.slime && CliffAhead(e))
                {
                    double l = Or(Hyp(e.wish[0], e.wish[2]), 1), sgn = Rand.Next() < 0.5 ? 1 : -1;
                    double px = e.wish[2] / l, pz = -e.wish[0] / l;
                    Stop(e); e.path = null; e.stuckT = 0; e.detourT = 0.5;
                    e.detX = e.pos[0] + px * sgn * 3; e.detZ = e.pos[2] + pz * sgn * 3;
                }
                if (e.type == "enderman" && !inW && WaterAhead(e))
                {
                    Stop(e); e.detourT = 0; e.tgt = null; e.aiT = Math.Min(e.aiT, 0.5);
                }
                // soft collisions with other mobs and the player
                for (int j = 0; j < mobs.Count; j++)
                {
                    var o = mobs[j];
                    if (o == e || o.deathT > 0 || MOB_DEFS[o.type].aquatic) continue;
                    double sx = e.pos[0] - o.pos[0], sz = e.pos[2] - o.pos[2], r = (e.w + o.w) * 0.5;
                    if (Math.Abs(sx) < r && Math.Abs(sz) < r && Math.Abs(e.pos[1] - o.pos[1]) < 1.5)
                    {
                        double l = Or(Hyp(sx, sz), 0.01), push = (r - l) * 4;
                        e.vel[0] += sx / l * push; e.vel[2] += sz / l * push;
                    }
                }
                {
                    double r = (e.w + Player.PLAYER_W) * 0.5, l = Or(dist, 0.01);
                    if (l < r && Math.Abs(p.pos[1] - e.pos[1]) < 1.8) { e.vel[0] -= dx / l * (r - l) * 6; e.vel[2] -= dz / l * (r - l) * 6; }
                }
                double wl = Hyp(e.wish[0], e.wish[2]), ox = e.pos[0], oz = e.pos[2]; bool wasGround = e.onGround;
                double fallStart = e.onGround ? e.pos[1] : e.fallY ?? e.pos[1];
                e.fallY = Math.Max(fallStart, e.pos[1]);
                if (d.drowned) SwimPose(e);
                if (e.swim != null && IsWaterId(w.GetBlock(Fl(e.pos[0]), Fl(e.pos[1] + EyeH(e)), Fl(e.pos[2])))) SwimPhysics(e, d);
                else WalkPhysics(e, d, dt, inW);
                if (e.breakDoors || e.doorPos != null) BreakDoor(e);
                if (d.slime && e.onGround && !wasGround) { e.tsq = -0.5; Stop(e); }
                if (d.slime) { e.squish += (e.tsq - e.squish) * 0.5; e.tsq *= 0.6; }
                if (e.onGround) { double fh = (double)e.fallY - e.pos[1]; if (fh > 3 && e.type != "chicken" && !d.slime && !inW && fb != B.LAVA) DamageMob(e, Math.Ceiling(fh - 3), null); e.fallY = e.pos[1]; }
                // stuck against something: take a short detour sideways
                if (wl > 0.5 && !d.slime && e.doorPos == null)
                {
                    double moved = Hyp(e.pos[0] - ox, e.pos[2] - oz);
                    // the pace it should make: walking, or in water a fraction of it (acceleration 0.02 x the
                    // input a tick, drag 0.8: settles at 0.1 x the input a tick, 2 x the input blocks/s)
                    double pace = inW ? Math.Min(1, Math.Sqrt(wl / MOB_SPEED_K)) * 0.98 * 2 : wl;
                    if (moved < pace * dt * 0.3) e.stuckT += dt; else e.stuckT = Math.Max(0, e.stuckT - dt * 2);
                    if (e.stuckT > 0.5 && e.detourT <= 0)
                    {
                        e.stuckT = 0; e.detourT = 0.7; e.path = null;
                        double sgn = Rand.Next() < 0.5 ? 1 : -1;
                        e.detX = e.pos[0] + e.wish[2] / wl * 5 * sgn; e.detZ = e.pos[2] - e.wish[0] / wl * 5 * sgn;
                    }
                }
                else e.stuckT = 0;
                if (!d.aquatic && !d.slime) BodyFollowsHead(e, Hyp(e.pos[0] - ox, e.pos[2] - oz) > 0.0005);
                // limb swing (LivingEntity.calculateEntityAnimation): amount eases 0.4 a tick toward
                // min(4 x distance moved, 1), the swing position advances by the amount
                e.walkAmt += (Math.Min(1, Hyp(e.pos[0] - ox, e.pos[2] - oz) * 4) - e.walkAmt) * 0.4;
                e.walk += e.walkAmt;
            }
        }
    }
}
