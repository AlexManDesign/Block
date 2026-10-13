// Port of save.js DB (12332-12411): the world list and the per-column edit maps. The IndexedDB database
// 'maincraft' (stores 'worlds' keyed by id, and 'cols' keyed "worldId:colKey") becomes files under
// Application.persistentDataPath, read and written synchronously on the main thread (World.editStore is
// synchronous already):
//   worlds/<id>.json             the world record (WorldMetaJson, the JS schema)
//   worlds/<id>/cols/<colKey>    one column's edits: little-endian Int32 pairs [localIndex, id | meta << 16]
// The JS promises resolve with null / false on errors: here the methods catch IO errors and return
// null / false / empty. Writes go to a temp file and are then moved over the old one.
//
// API skeleton: bodies marked TODO(misc) are filled in by that implementer.
using System;
using System.Collections.Generic;

namespace Maincraft
{
    public static class WorldDb
    {
        /// <summary>The database (IDBDatabase handle): the worlds directory, null until Open succeeded.</summary>
        public static string db;

        // save.js open (12338-12351): creates the directories once; the root, or null on failure
        public static string Open() { throw new NotImplementedException(); } // TODO(misc)
        /// <summary>Record file of a world (worlds/&lt;id&gt;.json).</summary>
        public static string WorldPath(string id) { throw new NotImplementedException(); } // TODO(misc)
        /// <summary>Directory of a world's column edits (worlds/&lt;id&gt;/cols).</summary>
        public static string ColDir(string id) { throw new NotImplementedException(); } // TODO(misc)
        // save.js listWorlds (12366): every record (in id order, as getAll returns them), stable-sorted by
        // lastPlayed descending; unreadable records are skipped
        public static List<WorldMeta> ListWorlds() { throw new NotImplementedException(); } // TODO(misc)
        // save.js putWorld (12367): upserts the record by id; false on failure
        public static bool PutWorld(WorldMeta w) { throw new NotImplementedException(); } // TODO(misc)
        // save.js deleteWorld (12368-12378): the record, then every column of the world
        public static void DeleteWorld(string id) { throw new NotImplementedException(); } // TODO(misc)
        // save.js loadColKeys (12381-12386): the column keys with stored edits
        public static HashSet<long> LoadColKeys(string id) { throw new NotImplementedException(); } // TODO(misc)
        // save.js loadCol (12388-12394): one column's edits in stored order (localIndex -> packed), or null
        public static Dictionary<int, int> LoadCol(string id, long key) { throw new NotImplementedException(); } // TODO(misc)
        // save.js saveCols (12396-12410): each column's map as Int32 pairs in the map's order; false without a
        // database or on failure, true at once when entries is empty
        public static bool SaveCols(string id, List<KeyValuePair<long, Dictionary<int, int>>> entries) { throw new NotImplementedException(); } // TODO(misc)
        // save.js export's read transaction (12452-12456): every stored column of a world as (key suffix
        // after "id:", raw pairs), in IndexedDB key order (ordinal order of the "id:key" strings)
        public static List<KeyValuePair<string, int[]>> ReadAllCols(string id) { throw new NotImplementedException(); } // TODO(misc)
        // save.js import's write transaction (12487-12493): the columns and the record of a new world
        // together; throws (the JS rejected with t.error) on failure
        public static void ImportWorld(WorldMeta meta, List<KeyValuePair<string, int[]>> cols) { throw new NotImplementedException(); } // TODO(misc)
    }
}
