// Port of interact.js (11835-12118): block placement rules (placementFor), the survival helpers (tools,
// attack stats, mining speed and harvest tiers, drops), chest loot rolls and structure chest contents.
//
// API skeleton: bodies marked TODO(misc) are filled in by that implementer.
// (Inside Game the method Game.Interact(dt) hides this class's name: Game files use `using static Maincraft.Interact;`.)
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    /// <summary>A cell to set: placementFor's result entries {x, y, z, id, m}.</summary>
    public struct Placement
    {
        public int x, y, z, id, m;
        public Placement(int x, int y, int z, int id, int m) { this.x = x; this.y = y; this.z = z; this.id = id; this.m = m; }
    }

    /// <summary>attackStats result {dmg, speed}.</summary>
    public struct AttackStat
    {
        public double dmg, speed;
        public AttackStat(double dmg, double speed) { this.dmg = dmg; this.speed = speed; }
    }

    public static class Interact
    {
        /// <summary>Horizontal dir of a face index (0 +x, 1 -x, 4 +z, 5 -z) -> 0 S, 1 W, 2 N, 3 E; -1 for up / down.</summary>
        public static readonly int[] FACE_HDIR = { 3, 1, -1, -1, 0, 2 };
        /// <summary>Face normals in the face order +x, -x, +y, -y, +z, -z.</summary>
        public static readonly int[][] FACE_N = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
        /// <summary>Vanilla attack damage and speed per tool kind, index tier - 1 (wood, stone, iron, gold, diamond).</summary>
        public static readonly Dictionary<string, (double[] dmg, double[] speed)> ATK_TABLE = new Dictionary<string, (double[] dmg, double[] speed)>();   // TODO(misc): fill (11983-11989)
        /// <summary>Mining speed by tier (1 wood, 2 stone, 3 iron, 4 gold, 5 diamond).</summary>
        public static readonly double[] TIER_SPEED = { 1, 2, 4, 6, 12, 8 };

        // per-id tables (needTier, regex tests on B_KEY) precomputed once after Blocks.Init (the JS ran regexes per call)
        public static void Init() { throw new NotImplementedException(); } // TODO(misc)
        // interact.js isSoil (11843-11846)
        public static bool IsSoil(int id) { throw new NotImplementedException(); } // TODO(misc)
        // interact.js placementFor (11849-11974): the cells, ids and metas to place from a ray hit, the held
        // block and the player's facing fd (0 S, 1 W, 2 N, 3 E); player may be null (pitch); null when it can not be placed
        public static List<Placement> PlacementFor(World world, int heldId, RayHit hit, int fd, Player player) { throw new NotImplementedException(); } // TODO(misc)
        // interact.js toolInfo (11977-11981): the item's def when it is a tool (ItemDef.Tool set), else null
        public static ItemDef ToolInfo(ItemStack item) { throw new NotImplementedException(); } // TODO(misc)
        // interact.js attackStats (11990-11996)
        public static AttackStat AttackStats(ItemStack item) { throw new NotImplementedException(); } // TODO(misc)
        // interact.js needTier (11998-12006): minimum pickaxe tier for drops (0..5)
        public static int NeedTier(int id) { throw new NotImplementedException(); } // TODO(misc)
        // interact.js breakTime (12008-12020): seconds to break id with item (+Infinity: unbreakable)
        public static double BreakTime(int id, ItemStack item) { throw new NotImplementedException(); } // TODO(misc)
        // interact.js canHarvest (12021-12028)
        public static bool CanHarvest(int id, ItemStack item) { throw new NotImplementedException(); } // TODO(misc)
        // interact.js dropsFor (12030-12085): drops of a broken block ({id, count}); item null = broken by the world
        public static List<ItemStack> DropsFor(int id, int m, ItemStack item) { throw new NotImplementedException(); } // TODO(misc)
        // interact.js rollLoot (12088-12107): LootTable.fill into 27 slots
        public static ItemStack[] RollLoot(int table) { throw new NotImplementedException(); } // TODO(misc)
        // interact.js chestContents (12109-12117): the chest's slots from game.meta.chests (rolling a structure
        // chest's loot the first time), or null; sets game.chests = meta.chests
        public static ItemStack[] ChestContents(Game game, int x, int y, int z) { throw new NotImplementedException(); } // TODO(misc)
    }
}
