// Port of entities.js class Entities, part 3 (10700-11748): fish targeting, escape and physics, line of
// sight, enderman teleports, natural spawning (fish, sharks, monsters, drowned, chunk-generation and
// periodic animals) and its rules, the populated-column save, monster spawner blocks, the crosshair ray
// against mobs, damage and death (damageMob, reinforcements, loot, slime splits), zombies breaking doors,
// the drowned's goals, swimming and tridents, and the rendering of mobs, flames, models, held items,
// thrown tridents, falling blocks, arrows and dropped items.
//
// API skeleton: bodies marked TODO(ent-c) are filled in by that implementer.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed partial class Entities
    {
        // entities.js findPrey (10745-10755): shark target; isPlayer true for the player (returns null then), else a mob or null
        public Mob FindPrey(Mob e, double h, out bool isPlayer) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js escapeSpot (10757-10794): fish escape target [x, y, z] or null
        public double[] EscapeSpot(Mob e, double px, double pz) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js fishPhysics (10795-10846): FishMoveControl and per-axis moves through water
        public void FishPhysics(Mob e, MobDef d, double dt) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js rayBlocked (10850-10856): line of sight a -> b against collision shapes (game.js explode uses it)
        public bool RayBlocked(double[] a, double[] b) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js teleportNear (10859-10877): enderman teleport within r of (cx, cz)
        public bool TeleportNear(Mob e, double cx, double cz, double r) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js trySpawnFish (10879-10895)
        public void TrySpawnFish() { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js trySpawnShark (10896-10907)
        public void TrySpawnShark() { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js emptySpawn (10912-10915): NaturalSpawner.isValidEmptySpawnBlock
        public bool EmptySpawn(int id) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js floorAt (10916-10919): a monster can stand at the cell
        public bool FloorAt(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js farEnough (10922-10926): away from the player and the world spawn
        public bool FarEnough(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js monsterRules (10930-10936)
        public bool MonsterRules(string type, int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js trySpawnMonsters (10941-10974)
        public void TrySpawnMonsters() { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js surfaceY (10979-10986): MOTION_BLOCKING_NO_LEAVES height, or null
        public int? SurfaceY(int x, int z) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js animalSpot (10989-10994): Animal.checkAnimalSpawnRules
        public bool AnimalSpot(string type, int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js pickAnimal (10995-10999): weighted pick (fallback: the last entry)
        public SpawnEntry PickAnimal(AnimalSpawn S) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js populate (11004-11024): spawnMobsForChunkGeneration
        public void Populate(Column c) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js spawnAnimals (11029-11056): periodic creature spawning
        public void SpawnAnimals() { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js savePopulated (11058): the populated keys in insertion order (meta.popd)
        public uint[] SavePopulated() { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js loadPopulated (11059): replaces populated / populatedList (null: empty)
        public void LoadPopulated(uint[] a) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js tickSpawners (11065-11098): BaseSpawner.serverTick of the generated spawners
        public void TickSpawners() { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js raycastMob (11101-11110): nearest live mob box on the ray within maxD, or null
        public MobHit? RaycastMob(double[] o, double[] dir, double maxD) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js damageMob (11116-11157): LivingEntity.hurt. from: the attacker's position (the player's
        // live pos array; implies the player), extra = [strength, dirX, dirZ] (sprint knockback)
        public void DamageMob(Mob e, double dmg, double[] from = null, double[] extra = null, bool hitsArmor = false, bool calm = false) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js reinforce (11162-11178): zombie reinforcement on hard; the new zombie or null
        public Mob Reinforce(Mob e) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js drownedGoals (11185-11229): true when it set the movement
        public bool DrownedGoals(Mob e, MobDef d, double dt, bool hunting, double speed, bool inW, double dx, double dz) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js findWater (11230-11237): [x, y, z] or null
        public double[] FindWater(Mob e) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js findBeach (11240-11249): [x, y, z] or null
        public double[] FindBeach(Mob e) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js swimPhysics (11255-11271): the drowned swimming toward e.swim
        public void SwimPhysics(Mob e, MobDef d) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js swimPose (11274-11280) (tickMobs passes an unused dt in the JS)
        public void SwimPose(Mob e) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js drownZombie (11283-11291): Mob.convertTo; removes mobs[i]
        public void DrownZombie(Mob z, int i) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js drownedGear (11299-11302): maybe a trident in hand; returns e (game.js spawn eggs)
        public Mob DrownedGear(Mob e) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js throwTridentAt (11305-11313)
        public void ThrowTridentAt(Mob e) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js tridentGoal (11318-11330): RangedAttackGoal of a drowned with a trident; always true
        public bool TridentGoal(Mob e, double dt, double speed, bool inW, double dx, double dz) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js spawnDrowned (11331-11348)
        public void SpawnDrowned(int x, int y, int z, bool river, bool frozen) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js breakDoor (11353-11379): BreakDoorGoal on hard
        public void BreakDoor(Mob e) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js endDoor (11380-11384)
        public void EndDoor(Mob e) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js mobDied (11385-11404): loot, held item, slime split
        public void MobDied(Mob e, MobDef d) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js render (11407-11492): every mob, the spawner mini-mobs, the third-person player, then renderItems
        public void Render(Env env, double[] cam) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js render (11417-11448): buildModel's input for a mob, after render set its r* fields (rpos,
        // ryaw, rhead, rwalk, rpitch, rswim): every field of into written (it is the reused Entities.pose); returns into
        public ModelPose PoseOf(Mob e, ModelPose into) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js flame (11496-11509): camera-facing fire quads on a burning entity
        public void Flame(VBuf v, double[] P, double w, double h, double[] cam) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js buildModel (11510-11619): animate and emit a box model; tint null = none;
        // transOut: translucent parts (null: into outBuf); heldOut: atlas buffer for the bow / held items (null: none)
        public void BuildModel(VBuf outBuf, MobModel m, ModelPose e, double[] cam, double li, double[] tint, VBuf transOut = null, VBuf heldOut = null) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js heldItem3p (11624-11663): ItemInHandLayer at the right hand (XP: the arm's modelXform parameters)
        public void HeldItem3p(VBuf outBuf, double[] XP, ItemStack held, double li) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js drawTrident (11666-11683): a thrown trident
        public void DrawTrident(VBuf v, Arrow a, double li, double[] cam) { throw new NotImplementedException(); } // TODO(ent-c)
        // entities.js renderItems (11684-11746): falling blocks, arrows and tridents, dropped items
        public void RenderItems(Env env, double[] cam) { throw new NotImplementedException(); } // TODO(ent-c)
    }
}
