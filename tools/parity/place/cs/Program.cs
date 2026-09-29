using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using UnityEngine;
using BlockcraftPort;

static class PlaceParityProgram
{
    static int Main(string[] args)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(args[0]));
        MainBlockUpdates.BindMainThread();
        var w = new VoxelWorld();
        int[] O = { 1000, 200, 1000 };
        const int LO = -3, HI = 10;
        var sb = new StringBuilder("{");
        bool firstTest = true;
        foreach (var t in doc.RootElement.GetProperty("tests").EnumerateArray())
        {
            w.Cells.Clear();
            // Source (x,y,z) relative to the cell origin -> Unity cell.
            Vector3Int U(int x, int y, int z) => new Vector3Int(O[0] + x, O[1] + y, -(O[2] + z) - 1);
            void Raw(int x, int y, int z, string key, int meta)
            {
                BlockId id = Id(key); var u = U(x, y, z);
                w.Raw(u.x, u.y, u.z, id, SourceCoords.SourceMetaToUnity(id, (byte)meta));
            }
            bool noFloor = t.TryGetProperty("noFloor", out var nf) && nf.GetBoolean();
            if (!noFloor) for (int x = 0; x < 4; x++) for (int z = 0; z < 4; z++) Raw(x, 0, z, "STONE", 0);
            foreach (var f in t.GetProperty("fixture").EnumerateArray())
                Raw(f.GetProperty("x").GetInt32(), f.GetProperty("y").GetInt32(), f.GetProperty("z").GetInt32(), f.GetProperty("id").GetString(), f.GetProperty("meta").GetInt32());
            float[] pl = { -30, 0, -30 };
            if (t.TryGetProperty("player", out var pe)) { int i = 0; foreach (var v in pe.EnumerateArray()) pl[i++] = (float)v.GetDouble(); }
            Vector3 feet = new Vector3(O[0] + pl[0], O[1] + pl[1], -(O[2] + pl[2]));
            string err = null;
            try
            {
                foreach (var a in t.GetProperty("actions").EnumerateArray())
                {
                    string op = a.GetProperty("op").GetString();
                    if (op == "raw") { Raw(a.GetProperty("x").GetInt32(), a.GetProperty("y").GetInt32(), a.GetProperty("z").GetInt32(), a.GetProperty("id").GetString(), a.GetProperty("meta").GetInt32()); continue; }
                    if (op == "edit")
                    {
                        BlockId id = Id(a.GetProperty("id").GetString()); var u = U(a.GetProperty("x").GetInt32(), a.GetProperty("y").GetInt32(), a.GetProperty("z").GetInt32());
                        int meta = a.TryGetProperty("meta", out var me) ? me.GetInt32() : 0;
                        w.SetBlock(u.x, u.y, u.z, id, SourceCoords.SourceMetaToUnity(id, (byte)meta), true); continue;
                    }
                    var hit = Arr(a.GetProperty("hit"));
                    var ub = U((int)hit[0], (int)hit[1], (int)hit[2]);
                    BlockId hid = w.GetBlock(ub.x, ub.y, ub.z);
                    if (op == "break")
                    {
                        BlockId after = MainBlockPlacement.BreakCompanions(w, ub, hid, false);
                        w.SetBlock(ub.x, ub.y, ub.z, after, 0, true); continue;
                    }
                    var k = Arr(a.GetProperty("k")); var h = Arr(a.GetProperty("h"));
                    var vh = new VoxelHit
                    {
                        Block = ub, Id = hid, Normal = new Vector3Int((int)k[0], (int)k[1], -(int)k[2]),
                        Point = new Vector3(O[0] + (float)h[0], O[1] + (float)h[1], -(O[2] + (float)h[2]))
                    };
                    string heldKey = a.GetProperty("held").ValueKind == JsonValueKind.Null ? null : a.GetProperty("held").GetString();
                    BlockId held = heldKey == null ? BlockId.Air : Id(heldKey);
                    var ctx = new MainBlockPlacement.Context { World = w, Yaw = (float)a.GetProperty("yaw").GetDouble(), Survival = false, PlayerFeet = feet };
                    // MainPlayerController order: UseTarget (pot / doors) before placement.
                    if (MainBlockPlacement.UseTarget(ctx, vh, held, out _) != MainBlockPlacement.Result.None) continue;
                    MainBlockPlacement.Place(ctx, vh, held);
                }
            }
            catch (Exception e) { err = e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace?.Split('\n')[0]; }
            var cells = new List<string>();
            for (int x = LO; x < HI; x++) for (int y = LO; y < HI; y++) for (int z = LO; z < HI; z++)
            {
                var u = U(x, y, z); BlockId id = w.GetBlock(u.x, u.y, u.z);
                if (id == BlockId.Air) continue;
                int meta = SourceCoords.UnityMetaToSource(id, w.GetMeta(u.x, u.y, u.z));
                cells.Add($"[{x},{y},{z},\"{BlockRegistry.Key(id)}\",{meta}]");
            }
            if (!firstTest) sb.Append(','); firstTest = false;
            sb.Append(JsonSerializer.Serialize(t.GetProperty("name").GetString())).Append(':');
            string arr = "[" + string.Join(",", cells) + "]";
            if (err != null) sb.Append("{\"err\":").Append(JsonSerializer.Serialize(err)).Append(",\"cells\":").Append(arr).Append('}');
            else sb.Append(arr);
        }
        sb.Append('}');
        File.WriteAllText(args[1], sb.ToString());
        return 0;
    }
    static BlockId Id(string key)
    {
        if (key == "AIR") return BlockId.Air;
        BlockId id = BlockRegistry.FromKey(key);
        if (id == BlockId.Air) throw new Exception("unknown key " + key);
        return id;
    }
    static double[] Arr(JsonElement e) { var l = new List<double>(); foreach (var v in e.EnumerateArray()) l.Add(v.GetDouble()); return l.ToArray(); }
}
