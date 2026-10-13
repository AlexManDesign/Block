// Port of craft.js (12119-12331): the ordered recipe list (shaped and shapeless; the first match wins),
// ingredient tags, crafting-grid matching with cropping and mirroring, furnace smelting and fuel values.
//
// API skeleton: bodies marked TODO(misc) are filled in by that implementer.
using System;
using System.Collections.Generic;
using static Maincraft.Blocks;

namespace Maincraft
{
    /// <summary>A recipe: shaped (pattern + keys) or shapeless (ingredients). Ingredients are ids or tags (negative).</summary>
    public sealed class Recipe
    {
        public string[] pattern;            // shaped rows (null for shapeless); missing chars count as ' '
        public int pw, ph;                  // pattern size (max row length, rows), precomputed
        public Dictionary<char, int> keys;  // pattern char -> id or tag
        public int[] shapeless;             // ids or tags (null for shaped); order matters for the greedy match
        public int @out;                    // result id
        public int count;                   // result count (count || 1)
    }

    public static class Craft
    {
        // ingredient tags (the JS strings 'planks', 'log', 'wool', 'stonetool', 'sand', 'coal', 'woodslab'); 0 never matches
        public const int ANY_PLANKS = -1, ANY_LOG = -2, TAG_WOOL = -3, TAG_STONETOOL = -4, TAG_SAND = -5, TAG_COAL = -6, TAG_WOODSLAB = -7;
        /// <summary>The recipes in registration order (471: 351 shaped, 120 shapeless).</summary>
        public static readonly List<Recipe> RECIPES = new List<Recipe>();     // TODO(misc): filled by Init
        /// <summary>Furnace input id -> output id (62 entries).</summary>
        public static readonly Dictionary<int, int> SMELT = new Dictionary<int, int>();   // TODO(misc): filled by Init

        // craft.js recipe registration IIFE (12137-12271) and the SMELT IIFE (12312-12322); after Blocks.Init
        public static void Init() { throw new NotImplementedException(); } // TODO(misc)
        // craft.js R (12124): a shaped recipe
        public static void R(string[] pattern, Dictionary<char, int> keys, int @out, int count = 1) { throw new NotImplementedException(); } // TODO(misc)
        // craft.js RS (12125): a shapeless recipe
        public static void RS(int[] ings, int @out, int count = 1) { throw new NotImplementedException(); } // TODO(misc)
        // craft.js matches (12127-12136): an ingredient (id or tag) against an id
        public static bool Matches(int tag, int id) { throw new NotImplementedException(); } // TODO(misc)
        // craft.js craftResult (12274-12309): grid of ids (0 empty), w x h; {id, count} or null
        public static ItemStack CraftResult(int[] grid, int w, int h) { throw new NotImplementedException(); } // TODO(misc)
        // craft.js fuelValue (12323-12330): burn value; the furnace's burn time is a float division,
        // Game.TickFurnaces: FuelValue(id) / 8.0 (a stick gives 0.625, not 0)
        public static int FuelValue(int id) { throw new NotImplementedException(); } // TODO(misc)
    }
}
