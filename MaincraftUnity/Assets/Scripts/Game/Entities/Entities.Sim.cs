// Port of entities.js class Entities, part 2 (9700-10699): the land-mob goal system (ai), targeting and
// sight, looking, animal goals and breeding, slime hops, melee knockback, light helpers, steering and A*
// path following, the path finder's cell predicates and findPath, cliff / water avoidance, Minecraft's
// per-tick walking, water and lava physics (walkPhysics, entStep), fluid queries and the fish AI (aiFish).
//
// API skeleton: bodies marked TODO(ent-b) are filled in by that implementer.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    public sealed partial class Entities
    {
        // entities.js ai (9790-9994): land mob goals by priority; sets e.wish and headYaw. h: 3D distance to the player's middle
        public void Ai(Mob e, MobDef d, double dt, double dx, double dz, double h, bool hunt, bool inW) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js followRange (10000)
        public double FollowRange(Mob e, MobDef d) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js targeting (10001-10011): NearestAttackableTargetGoal / TargetGoal
        public bool Targeting(Mob e, double dt, MobDef d, double h) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js chasePoint (10015-10021): MeleeAttackGoal target point; returns e.chase itself ([x, z])
        public double[] ChasePoint(Mob e, MobDef d, double dt) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js lookForWater (10024-10037): PanicGoal.lookForWater, [x + 0.5, z + 0.5] or null
        public double[] LookForWater(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js findShade (10040-10049): FleeSunGoal.getHidePos, [x, z] or null
        public double[] FindShade(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js inReach (10052-10055): Mob.isWithinMeleeAttackRange (the player)
        public bool InReach(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js canSee (10056-10059): line of sight from the mob's eyes to player.Eye()
        public bool CanSee(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js shootArrow (10060-10073): a skeleton arrow at the player
        public void ShootArrow(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js look (10077-10087): LookAtPlayerGoal / RandomLookAroundGoal + LookControl
        public void Look(Mob e, double dt, double dx, double dz, double h, double R) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js bodyFollowsHead (10091-10096): BodyRotationControl when standing
        public void BodyFollowsHead(Mob e, bool moved) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js animalGoals (10098-10166): breed > tempt > follow parent > eat grass; true = busy
        public bool AnimalGoals(Mob e, MobDef d, double dt, double dx, double dz, double h, double speed) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js breed (10170-10177)
        public void Breed(Mob a, Mob b) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js feed (10179-10187): the player feeds an animal (game.js useEntity); true when it took the food
        public bool Feed(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js hop (10190-10195): slime jump
        public void Hop(Mob e, double dx, double dz, double attr) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js slimeSwim (10199-10203): S = SLIME_SIZES entry
        public void SlimeSwim(Mob e, double dx, double dz, double[] S) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js hitPlayer (10204-10211): Game.Hurt plus knockback; true when the hit landed
        public bool HitPlayer(Mob e, double dmg, double dx, double dz) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js eyeH (10213): eye height scaled by e.h / d.h
        public double EyeH(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js lightAt (10216): raw light at the mob's eyes
        public int LightAt(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js rawLight (10217): max(block light, sky light - skyDarken)
        public int RawLight(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js darkEnough (10221-10225): Monster.isDarkEnoughToSpawn (draws 1-2 randoms)
        public bool DarkEnough(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js stared (10228-10236): EnderMan.isLookingAtMe
        public bool Stared(Mob e, double h) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js stop (10239)
        public void Stop(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js steer (10241-10246): straight at a point; true (and stopped) within 0.4
        public bool Steer(Mob e, double tx, double tz, double speed) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js goTo (10248-10276): follows an A* path; true when done
        public bool GoTo(Mob e, double tx, double tz, double speed, double dt) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js pointSolid (10277-10285): a point inside a SOLID block's collision boxes
        public bool PointSolid(double x, double y, double z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js hazard (10286): lava or fire
        public bool Hazard(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js solidIn (10290-10301): pointSolid(bx + 0.5, by + ly, bz + 0.5) without allocation
        public bool SolidIn(int bx, int by, int bz, double ly) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js clear2 (10302): two cells of body room
        public bool Clear2(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js doorShut (10305-10308)
        public bool DoorShut(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js standable (10312-10316)
        public bool Standable(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js floorIn (10320-10332)
        public bool FloorIn(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js standY (10335-10342): walkable height in the column, or null
        public int? StandY(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js pathMalus (10346-10358): 0 | 8 | 16
        public double PathMalus(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js dangerNear (10360-10370): 0 / 1, cached in PS during a search
        public int DangerNear(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js dangerAt (10373-10383): 0 / 1, cached in PS during a search
        public int DangerAt(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js landSpot (10388-10405): LandRandomPos, [x, z] or null
        public double[] LandSpot(Mob e, MobDef d, int r) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js pathStartY (10411-10427): WalkNodeEvaluator.getStart
        public int PathStartY(Mob e, int sx, int sz) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js findPath (10432-10485): A* over columns; null when start column == goal column
        public MobPath FindPath(Mob e, double tx, double tz) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js cliffAhead (10487-10495)
        public bool CliffAhead(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js waterAhead (10497-10503)
        public bool WaterAhead(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js moveAxis (10506-10511): sweep along one axis; true if blocked
        public bool MoveAxis(Mob e, int ax, double d) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js walkPhysics (10519-10584): one tick of Mob movement (ground, water, lava)
        public void WalkPhysics(Mob e, MobDef d, double dt, bool inW) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js entStep (10587-10608): Entity.move for mobs; v in blocks per tick, mutated
        public void EntStep(Mob e, double[] v, bool wasGround) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js lavaDepth (10611-10625): 0 = not in lava
        public double LavaDepth(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js waterAt (10630-10648): sets e.wDepth / e.wPush; true when wet
        public bool WaterAt(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js eyeInWater (10650-10653)
        public bool EyeInWater(Mob e) { throw new NotImplementedException(); } // TODO(ent-b)
        // entities.js aiFish (10656-10744): salmon and shark AI per tick (dx, dz unused; h as for ai)
        public void AiFish(Mob e, MobDef d, double dt, double dx, double dz, double h) { throw new NotImplementedException(); } // TODO(ent-b)
    }
}
