// Port of save.js WorldFile (12412-12496): a world's record and all its stored column edits in one
// gzip-compressed JSON file (.mcworld), to keep a world outside the game and to bring it into any build
// (browser files import here and the other way round). Typed arrays are written as base64
// ({"$ta": "Int32Array", "b": data}, the raw little-endian bytes of the array).
//
// Browser -> Unity: there is no runtime save dialog, so Export writes the file into
// Application.persistentDataPath/exports/<FileName(name)> (the 'cancel' result stays for a platform
// file-dialog plugin); Import reads a file path, which the world screen's import button gets from
// UI.PickImportFile (by default a pick list of ListImportFiles, the files put into
// persistentDataPath/imports; a platform file picker can replace it). CompressionStream is GZipStream.
// The JSON goes through MiniJson: Replace turns typed arrays into {$ta, b} objects before writing,
// Revive turns them back after parsing (JSON.stringify's replacer / JSON.parse's reviver).
//
// Typed arrays are told apart by their exact type (v.GetType() == typeof(uint[]), ...), never with `is` /
// `case int[]`: the CLR treats uint[] as an int[] (and byte[] / sbyte[], ushort[] / short[]) in type
// tests, so meta.popd would be written as an "Int32Array" and come back as int[].
//
// API skeleton: bodies marked TODO(misc) are filled in by that implementer.
using System;
using System.Collections.Generic;

namespace Maincraft
{
    /// <summary>WorldFile.export's result: true, false (no such world), 'cancel' (the dialog was closed; truthy for the UI).</summary>
    public enum ExportResult { Failed, Done, Cancelled }

    public static class WorldFile
    {
        /// <summary>Typed-array constructor names -> the C# array types they revive as (Int8Array sbyte[], Uint8Array byte[],
        /// Int16Array short[], Uint16Array ushort[], Int32Array int[], Uint32Array uint[], Float32Array float[], Float64Array double[]).</summary>
        public static readonly Dictionary<string, Type> TYPES = new Dictionary<string, Type>
        {
            ["Int8Array"] = typeof(sbyte[]), ["Uint8Array"] = typeof(byte[]), ["Int16Array"] = typeof(short[]), ["Uint16Array"] = typeof(ushort[]),
            ["Int32Array"] = typeof(int[]), ["Uint32Array"] = typeof(uint[]), ["Float32Array"] = typeof(float[]), ["Float64Array"] = typeof(double[]),
        };
        /// <summary>The folder Export writes into (persistentDataPath/exports).</summary>
        public static string ExportDir => throw new NotImplementedException(); // TODO(misc)
        /// <summary>The folder the import pick list reads (persistentDataPath/imports; created when missing).</summary>
        public static string ImportDir => throw new NotImplementedException(); // TODO(misc)
        // the input's accept list (.mcworld, .json, application/json, application/gzip): the *.mcworld, *.json and
        // *.gz files of ImportDir, full paths sorted by name (ordinal); empty when the folder cannot be read
        public static string[] ListImportFiles() { throw new NotImplementedException(); } // TODO(misc)

        // save.js b64 (12417): base64 of bytes
        public static string B64(byte[] u8) { throw new NotImplementedException(); } // TODO(misc)
        // save.js unb64 (12418)
        public static byte[] Unb64(string s) { throw new NotImplementedException(); } // TODO(misc)
        // save.js replacer (12420-12423): a typed array (sbyte[] .. double[] as in TYPES, matched by exact type) ->
        // {$ta: name, b: base64 of its little-endian bytes}; anything else unchanged
        public static object Replacer(object v) { throw new NotImplementedException(); } // TODO(misc)
        // save.js reviver (12424-12430): {$ta: string, b: string} -> exactly TYPES[$ta] over the decoded bytes (an
        // unknown $ta gives byte[]; a byte count that is not a whole number of elements drops the rest, like the JS)
        public static object Reviver(object v) { throw new NotImplementedException(); } // TODO(misc)
        // JSON.stringify(o, replacer): a copy of the MiniJson tree with Replacer applied bottom-up
        public static object Replace(object o) { throw new NotImplementedException(); } // TODO(misc)
        // JSON.parse(text, reviver): Reviver applied bottom-up over a parsed MiniJson tree (in place where possible)
        public static object Revive(object o) { throw new NotImplementedException(); } // TODO(misc)
        // save.js gzip (12431-12434): compress (on) or decompress
        public static byte[] Gzip(byte[] data, bool on) { throw new NotImplementedException(); } // TODO(misc)
        // save.js export (12439-12471): the world (by id) written to a file; before runs first (saving the open
        // world). Failed when there is no such world; path: the file written (null otherwise)
        public static ExportResult Export(string id, string name, Action before, out string path) { throw new NotImplementedException(); } // TODO(misc)
        // save.js fileName (12472): (name || 'world') with runs of \/:*?"<>| replaced by '_', plus '.mcworld'
        public static string FileName(string name) { throw new NotImplementedException(); } // TODO(misc)
        // save.js import (12474-12495): a world file read back as a new world (new id 'w' + base36(now) +
        // base36(floor(rnd * 1e6)), lastPlayed now); returns its record. Throws Exception("gzip" | "format" | "db").
        // Each cols key is parsed with long.TryParse(k, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
        // and skipped when that fails: the keys name files (WorldDb.ImportWorld), and the JS kept such keys only
        // as entries loadColKeys turned into NaN, which no column has. Values: an int[] as it is, any other array
        // (plain or typed) converted like new Int32Array(v), element by element with ToInt32.
        public static WorldMeta Import(string path) { throw new NotImplementedException(); } // TODO(misc)
        /// <summary>Number.prototype.toString(36) for a non-negative integer (lowercase digits), used for world ids.</summary>
        public static string Base36(long v) { throw new NotImplementedException(); } // TODO(misc)
        /// <summary>A new world id: 'w' + base36(Date.now()) + base36(floor(Math.random() * 1e6)) (menus.js 15312, save.js 12483).</summary>
        public static string NewWorldId() { throw new NotImplementedException(); } // TODO(misc)
    }
}
