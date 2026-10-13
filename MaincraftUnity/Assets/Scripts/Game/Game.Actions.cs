// Port of game.js (13600-14449): the player's actions and survival. Throwing a trident, melee attacks on
// mobs and boats, left click and the hand's swing clock, block breaking and drops, the right-click chain
// (use -> useEntity -> useBlock -> useOn -> useItem), bone meal and saplings, explosions, the inventory
// helpers, the survival simulation (air, hazards, food), difficulty, damage intake, the action-bar
// message, beds and sleeping, the world respawn spot, death and respawn.
//
// API skeleton: bodies marked TODO(game-b) are filled in by that implementer.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;
using Cursor = UnityEngine.Cursor;

namespace Maincraft
{
    public sealed partial class Game
    {
        // game.js throwTrident (13604-13614): TridentItem.releaseUsing
        public void ThrowTrident() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js pointerFree (13615): paused or an inventory open
        public bool PointerFree() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js attack (13617-13653): melee on the mob or boat under the crosshair; true when something was hit
        public bool Attack() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js attackStrength (13655): Player.getAttackStrengthScale(0.5)
        public double AttackStrength(double speed) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js swing (13658): LivingEntity.swing
        public void Swing() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js swingAnim (13660): the swing's progress 0..1 between ticks
        public double SwingAnim() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js leftClick (13664-13671): Minecraft.startAttack
        public void LeftClick() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js handTick (13677-13696): one 20 Hz tick of the first-person hand
        public void HandTick() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js doBreak (13698-13710): the player breaks a block
        public void DoBreak(int x, int y, int z) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js onBlockBroken (13711-13725): World.onBreak hook (chest spill, drops, exhaustion)
        public void OnBlockBroken(int x, int y, int z, int id, int m, bool byUpdate) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js use (13732-13742): the right-click chain
        public void Use() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js useEntity (13743-13764): board a boat, feed / breed / shear a mob; tg may be null
        public bool UseEntity(int hid, RayHit tg) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js useBlock (13766-13799): doors, gates, beds, crafting table, wheel, chests, furnaces, redstone, TNT, pumpkin
        public bool UseBlock(RayHit tg, int hid) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js useOn (13801-13884): Item.useOn (eggs, buckets, flint and steel, bone meal, tools, seeds, placement)
        public bool UseOn(RayHit tg, ItemStack held, int hid) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js useItem (13886-13913): Item.use (eat, draw a trident, put down a boat); tg may be null
        public void UseItem(int hid, RayHit tg) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js obstructed (13917-13933): Level.isUnobstructed for a block about to be placed
        public bool Obstructed(int id, int m, int x, int y, int z) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js boneMeal (13935-13953)
        public void BoneMeal(RayHit tg) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js growSapling (13954-13973)
        public bool GrowSapling(int x, int y, int z, int id) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js explode (13982-14064): Minecraft Explosion. tg: the primed TNT cell {x, y, z} cleared first
        // (null: none); byMob: a mob's blast (damage scaled by the difficulty)
        public void Explode(double cx, double cy, double cz, double r, int[] tg = null, bool byMob = false) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js consumeHeld (14067-14073)
        public void ConsumeHeld() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js replaceHeld (14074-14080)
        public void ReplaceHeld(int id) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js damageTool (14081-14088)
        public void DamageTool(int n = 1) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js giveItem (14089-14109): into slots 0..35; returns the count that did not fit. dur == null: unworn
        public int GiveItem(int id, int count, int? dur = null, int? maxDur = null) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js pickBlock (14110-14129): middle click
        public void PickBlock() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js eatNow (14130-14141)
        public void EatNow() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js survivalTick (14144-14167): per frame
        public void SurvivalTick(double dt) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js hazardTick (14175-14198): one game tick of fire, lava, cactus, campfire and magma
        public void HazardTick() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js foodTick (14200-14220): FoodData.tick
        public void FoodTick() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js setDifficulty (14221-14225): also writes meta.difficulty (the JS validated the string; the enum is always valid)
        public void SetDifficulty(Difficulty d) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js localDifficulty (14230-14241): DifficultyInstance at a column
        public DifficultyInstance LocalDifficulty(int x, int z) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js hurt (14245-14274): player damage; byMob scales by the difficulty; from: the attacker's
        // position (hurt tilt direction), or null. True only for a full new hit. The #hurt flash restart (14270) is
        // UI.RestartAnim("hurt")
        public bool Hurt(double n, bool byMob = false, double[] from = null) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js poison (14275)
        public void Poison(double sec) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js say (14278-14289): the action-bar message (msgEl), faded out after 4 s
        public void Say(string text) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js skyDarken (14292-14297): 0 at noon .. 11 at midnight
        public int SkyDarken() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js standUpSpot (14300-14317): BedBlock.findStandUpPosition, or null
        public double[] StandUpSpot(int hx, int hy, int hz, int f) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js hurtsToStand (14320-14324)
        public bool HurtsToStand(int x, int y, int z) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js worldRespawnSpot (14330-14358): PlayerSpawnFinder around the world spawn s
        public double[] WorldRespawnSpot(double[] s) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js useBed (14360-14393)
        public void UseBed(int x, int y, int z) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js sleepTick (14397-14419); keys may be null
        public void SleepTick(double dt, InputKeys keys) { throw new NotImplementedException(); } // TODO(game-b)
        // game.js wake (14420-14429)
        public void Wake() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js die (14430-14434)
        public void Die() { throw new NotImplementedException(); } // TODO(game-b)
        // game.js respawn (14435-14454)
        public void Respawn() { throw new NotImplementedException(); } // TODO(game-b)
    }
}
