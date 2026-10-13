// The world record (WorldMeta) <-> JSON in the schema of the JS record (save.js 'worlds' store, menus.js
// create-world, game.js startWorld / saveWorld), so world files of the browser version import and the
// other way round. Keys are the JS keys; absent / undefined keys are left out on writing and read as the
// WorldMeta defaults (null / 0). Typed arrays of the record (inhab Float64Array, popd Uint32Array) are
// written in the world-file form {$ta, b} (WorldFile.Replacer) and read from that form or from plain
// number arrays. Keys this port does not know are kept in WorldMeta.extra and written back.
//
// API skeleton: bodies marked TODO(misc) are filled in by that implementer.
using System;
using System.Collections.Generic;

namespace Maincraft
{
    public static class WorldMetaJson
    {
        // the record as a JSON text (WorldDb's record files)
        public static string ToJson(WorldMeta m) { throw new NotImplementedException(); } // TODO(misc)
        // a record from JSON text (typed arrays revived); null when it is not a record (no string id)
        public static WorldMeta FromJson(string s) { throw new NotImplementedException(); } // TODO(misc)
        // the record as a MiniJson tree (Dictionary / List / double / string / bool / typed arrays as C# arrays)
        public static Dictionary<string, object> ToObject(WorldMeta m) { throw new NotImplementedException(); } // TODO(misc)
        // a record from a parsed (and revived) MiniJson tree; seed and sel are read with ToInt32, not MiniJson.I
        public static WorldMeta FromObject(Dictionary<string, object> o) { throw new NotImplementedException(); } // TODO(misc)
        // JS ToInt32 (`x | 0`) of a parsed value: a number truncated and wrapped mod 2^32 into the int range
        // (t = Math.Truncate(d); m = t % 4294967296.0; if (m < 0) m += 4294967296.0; if (m >= 2147483648.0)
        // m -= 4294967296.0); 0 for NaN, infinities and non-numbers. For seed (import accepts any number; the
        // generator uses seed | 0 and seed >>> 0, equal mod 2^32) and sel (startWorld: meta.sel | 0). A plain
        // (int) cast of an out-of-range double is unspecified in C#. A seed outside the int range (never written by
        // the game) is kept wrapped, so the world list shows and a save writes the wrapped number.
        public static int ToInt32(object o) { throw new NotImplementedException(); } // TODO(misc)

        // {id, count, dur?, maxDur?} (null for an empty slot)
        public static object StackToObject(ItemStack s) { throw new NotImplementedException(); } // TODO(misc)
        // a stack from a parsed value; null for null / non-objects
        public static ItemStack StackFromObject(object o) { throw new NotImplementedException(); } // TODO(misc)
        // an array of slots (inventory, chest)
        public static List<object> SlotsToObject(ItemStack[] slots) { throw new NotImplementedException(); } // TODO(misc)
        // slots from a parsed array (its own length), or null when o is not an array
        public static ItemStack[] SlotsFromObject(object o) { throw new NotImplementedException(); } // TODO(misc)
        // {slots, burn, burnMax, cook, lit?}
        public static Dictionary<string, object> FurnaceToObject(FurnaceRec f) { throw new NotImplementedException(); } // TODO(misc)
        // a furnace record; the JS comparator also accepted a bare slot array (game.js 12890: Array.isArray(v) ? v : v.slots)
        public static FurnaceRec FurnaceFromObject(object o) { throw new NotImplementedException(); } // TODO(misc)
        // {t, p, y, hp, baby, c, sh, bc, egg?, ps, sz?, iw?, dc?} (undefined keys left out)
        public static Dictionary<string, object> MobToObject(MobRecord r) { throw new NotImplementedException(); } // TODO(misc)
        // a mob record (null when o is not an object)
        public static MobRecord MobFromObject(object o) { throw new NotImplementedException(); } // TODO(misc)
        // {b: [[x, y, z, key, m], ...], pos, yaw, face, store: {chests, furnaces}}
        public static Dictionary<string, object> ShipToObject(ShipRec s) { throw new NotImplementedException(); } // TODO(misc)
        // a ship record (null when o is not an object)
        public static ShipRec ShipFromObject(object o) { throw new NotImplementedException(); } // TODO(misc)
    }
}
