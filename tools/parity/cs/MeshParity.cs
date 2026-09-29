using System;
using System.Collections.Generic;
using System.IO;
using BlockcraftPort;

/// Feeds reference-generated chunks (genWorker blocks+light) into the port's ChunkMesher and writes the
/// result in meshWorker's layout (source space) so tools/parity/compare_mesh.py can diff them.
static class MeshParity
{
    const int H = 384;
    static ushort[] sourceToPort;

    static ushort ToPort(ushort src)
    {
        if (src >= SourceBlockData.FromSource.Length) throw new Exception("unknown source block " + src);
        return SourceBlockData.FromSource[src];
    }

    public static ChunkColumn LoadJsChunk(string dir, int cx, int cz)
    {
        string p = Path.Combine(dir, $"js_{cx}_{cz}.bin");
        if (!File.Exists(p)) return null;
        byte[] b = File.ReadAllBytes(p); int n = 16 * 16 * H;
        var col = new ChunkColumn(SourceCoords.SourceChunkToUnity(new ChunkCoord(cx, cz)));
        for (int s = 0; s < 24; s++)
        {
            var sec = col.EnsureSection(s);
            for (int x = 0; x < 16; x++) for (int sz = 0; sz < 16; sz++) for (int y = 0; y < 16; y++)
            {
                int o = (x * 16 + sz) * H + s * 16 + y, uz = 15 - sz;
                var id = (BlockId)ToPort((ushort)(b[2 * o] | (b[2 * o + 1] << 8)));
                if (id != BlockId.Air) sec.Set(x, y, uz, id, SourceCoords.SourceMetaToUnity(id, b[2 * n + o]));
                sec.SetLight(x, y, uz, b[3 * n + o]);
            }
        }
        return col;
    }

    public static int Run(string[] args)
    {
        string dir = args[1];
        for (int a = 2; a < args.Length; a++)
        {
            var p = args[a].Split(','); int cx = int.Parse(p[0]), cz = int.Parse(p[1]);
            var nb = new ChunkColumn[9];
            // Unity neighbourhood index (dx+1)+(dz+1)*3 in UNITY chunk space; source dz maps to -dz.
            for (int dx = -1; dx <= 1; dx++) for (int dzs = -1; dzs <= 1; dzs++)
                nb[(dx + 1) + (-dzs + 1) * 3] = LoadJsChunk(dir, cx + dx, cz + dzs);
            var center = nb[4];
            var layers = new[] { new List<float>(), new List<float>(), new List<float>() };
            var idx = new[] { new List<uint>(), new List<uint>(), new List<uint>() };
            for (int s = 0; s < 24; s++)
            {
                var snap = PipelineBench.Capture(new SectionKey(center.Coord, s), 0, nb);
                if (!snap.Valid) continue;
                var r = ChunkMesher.Build(snap);
                // Each layer gets its own compacted vertex list, like meshWorker ov/wv/tv.
                AppendLayer(r, r.Opaque, r.OpaqueCount, layers[0], idx[0]);
                // meshWorker keeps hidden ore faces in the opaque list; the port stores them separately.
                AppendLayer(r, r.XrayHidden, r.XrayHiddenCount, layers[0], idx[0]);
                AppendLayer(r, r.Water, r.WaterCount, layers[1], idx[1]);
                AppendLayer(r, r.Transparent, r.TransparentCount, layers[2], idx[2]);
                snap.Release(); r.ReleaseBuffers();
            }
            using var f = new BinaryWriter(File.Create(Path.Combine(dir, $"csmesh_{cx}_{cz}.bin")));
            for (int l = 0; l < 3; l++) { f.Write((uint)layers[l].Count); f.Write((uint)idx[l].Count); }
            for (int l = 0; l < 3; l++) { foreach (var v in layers[l]) f.Write(v); foreach (var i in idx[l]) f.Write(i); }
        }
        return 0;
    }

    static void AppendLayer(MeshBuildResult r, int[] ind, int count, List<float> vs, List<uint> outIdx)
    {
        var remap = new Dictionary<int, uint>();
        for (int i = 0; i < count; i++)
        {
            int vi = ind[i];
            if (!remap.TryGetValue(vi, out uint ni))
            {
                ni = (uint)(vs.Count / 8); remap[vi] = ni;
                var v = r.Vertices[vi];
                vs.Add(v.Position.x); vs.Add(v.Position.y); vs.Add(-v.Position.z);
                vs.Add(v.UV.x); vs.Add(1f - v.UV.y); /* atlas image space like meshWorker */ vs.Add(v.ShadeSkyBlock.x); vs.Add(v.ShadeSkyBlock.y); vs.Add(v.ShadeSkyBlock.z);
            }
            outIdx.Add(ni);
        }
    }
}
