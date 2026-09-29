using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using BlockcraftPort;

/// Regression fingerprint of the port's pure pipeline (gen -> stitch -> mesh). Optimisations must keep
/// every hash identical: `golden <seed> <cx> <cz>` prints one hash per stage.
static class Golden
{
    public static int Run(string[] args)
    {
        int seed = int.Parse(args[1]), ox = int.Parse(args[2]), oz = int.Parse(args[3]);
        var gen = new ChunkGenerator(seed);
        var world = new Dictionary<ChunkCoord, ChunkColumn>();
        var cols = new List<ChunkColumn>();
        for (int d = 0; d <= 2; d++) for (int x = -d; x <= d; x++) for (int z = -d; z <= d; z++)
            if (Math.Max(Math.Abs(x), Math.Abs(z)) == d) cols.Add(gen.Generate(new ChunkCoord(ox + x, oz + z)));
        using (var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            foreach (var c in cols) HashColumn(h, c, true);
            Console.WriteLine("gen    " + Convert.ToHexString(h.GetHashAndReset())[..16]);
        }
        var dirty = new HashSet<ChunkCoord>();
        foreach (var col in cols) { world[col.Coord] = col; VoxelLighting.StitchNeighbors(world, col.Coord, dirty); }
        using (var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            foreach (var c in cols) HashColumn(h, c, false);
            Console.WriteLine("stitch " + Convert.ToHexString(h.GetHashAndReset())[..16]);
        }
        var nb = new ChunkColumn[9];
        using (var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                var c = new ChunkCoord(ox + x, oz + z);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                { world.TryGetValue(new ChunkCoord(c.X + dx, c.Z + dz), out var col); nb[(dx + 1) + (dz + 1) * 3] = col; }
                for (int s = 0; s < VoxelConstants.SectionCount; s++)
                {
                    var snap = PipelineBench.Capture(new SectionKey(c, s), 0, nb);
                    if (!snap.Valid) continue;
                    var r = ChunkMesher.Build(snap);
                    var buf = new byte[r.VertexCount * 32];
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(r.Vertices.AsSpan(0, r.VertexCount)).CopyTo(buf);
                    h.AppendData(buf);
                    h.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(r.Opaque.AsSpan(0, r.OpaqueCount)));
                    h.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(r.Water.AsSpan(0, r.WaterCount)));
                    h.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(r.Transparent.AsSpan(0, r.TransparentCount)));
                    snap.Release(); r.ReleaseBuffers();
                }
            }
            Console.WriteLine("mesh   " + Convert.ToHexString(h.GetHashAndReset())[..16]);
        }
        return 0;
    }

    static void HashColumn(IncrementalHash h, ChunkColumn c, bool blocks)
    {
        for (int s = 0; s < c.Sections.Length; s++)
        {
            var sec = c.Sections[s];
            h.AppendData(new byte[] { (byte)(sec == null ? 0 : 1) });
            if (sec == null) continue;
            if (blocks) { h.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(sec.Blocks.AsSpan())); h.AppendData(sec.Meta); }
            h.AppendData(sec.Light);
        }
        h.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(c.LightCeilings.AsSpan()));
    }
}
