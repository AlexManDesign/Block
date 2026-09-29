using System;
using System.Diagnostics;
using System.IO;
using BlockcraftPort;

static class Program
{
    public static string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
    static int Main(string[] args)
    {
        MainBiomeLookup.Initialize(File.ReadAllBytes(Path.Combine(Root, "UnityPort/Assets/Resources/Voxel/biome_multinoise.bytes")));
        SourceIds.Load(Path.Combine(Root, "tools/parity/source_block_ids.json"));
        switch (args[0])
        {
            case "gen": return GenDump(args);
            case "genbench": return GenBench(args);
            case "pipeline": return PipelineBench.Run(args);
            case "meshparity": return MeshParity.Run(args);
#if NET5_0_OR_GREATER
            case "golden": return Golden.Run(args);
#endif
        }
        return 1;
    }

    // gen <seed> <outdir> cx,cz ... (source chunk coords) -> cs_cx_cz.bin in reference layout
    static int GenDump(string[] args)
    {
        int seed = int.Parse(args[1]); string outdir = args[2];
        var gen = new ChunkGenerator(seed);
        const int H = 384;
        for (int a = 3; a < args.Length; a++)
        {
            var p = args[a].Split(','); int cx = int.Parse(p[0]), cz = int.Parse(p[1]);
            var col = gen.Generate(SourceCoords.SourceChunkToUnity(new ChunkCoord(cx, cz)));
            var blocks = new ushort[16 * 16 * H]; var meta = new byte[16 * 16 * H]; var light = new byte[16 * 16 * H];
            for (int x = 0; x < 16; x++) for (int sz = 0; sz < 16; sz++) for (int y = 0; y < H; y++)
            {
                int uz = 15 - sz; int wy = y + VoxelConstants.MinY;
                var id = col.GetLocal(x, wy, uz);
                int o = (x * 16 + sz) * H + y;
                blocks[o] = SourceIds.PortToSource[(int)id];
                meta[o] = SourceCoords.UnityMetaToSource(id, col.GetMetaLocal(x, wy, uz));
                light[o] = col.GetLightLocal(x, wy, uz);
            }
            using var f = File.Create(Path.Combine(outdir, $"cs_{cx}_{cz}.bin"));
            var bb = new byte[blocks.Length * 2]; Buffer.BlockCopy(blocks, 0, bb, 0, bb.Length);
            f.Write(bb, 0, bb.Length); f.Write(meta, 0, meta.Length); f.Write(light, 0, light.Length);
        }
        return 0;
    }

    static int GenBench(string[] args)
    {
        int seed = args.Length > 1 ? int.Parse(args[1]) : 56;
        var gen = new ChunkGenerator(seed);
        var sw = Stopwatch.StartNew();
        gen.Generate(new ChunkCoord(0, -1));
        Console.WriteLine($"first chunk {sw.Elapsed.TotalMilliseconds:F1} ms");
        sw.Restart(); int n = 0;
        for (int x = -4; x < 4; x++) for (int z = -4; z < 4; z++) { gen.Generate(SourceCoords.SourceChunkToUnity(new ChunkCoord(x, z))); n++; }
        Console.WriteLine($"{n} chunks {sw.Elapsed.TotalMilliseconds / n:F2} ms/chunk");
        return 0;
    }
}
