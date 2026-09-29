using System.Buffers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using BlockcraftPort;

/// Measures the port's chunk pipeline stages (generate -> light stitch -> mesh) outside Unity.
static class PipelineBench
{
    public static int Run(string[] args)
    {
        int seed = args.Length > 1 ? int.Parse(args[1]) : 56;
        int R = args.Length > 2 ? int.Parse(args[2]) : 3;          // world radius in chunks
        int ox = args.Length > 3 ? int.Parse(args[3]) : -67, oz = args.Length > 4 ? int.Parse(args[4]) : 177;
        var gen = new ChunkGenerator(seed);
        var world = new Dictionary<ChunkCoord, ChunkColumn>();
        var order = new List<ChunkCoord>();
        for (int d = 0; d <= R; d++) for (int x = -d; x <= d; x++) for (int z = -d; z <= d; z++)
            if (Math.Max(Math.Abs(x), Math.Abs(z)) == d) order.Add(new ChunkCoord(ox + x, oz + z));
        var sw = Stopwatch.StartNew();
        var cols = new List<ChunkColumn>();
        foreach (var c in order) cols.Add(gen.Generate(c));
        double genMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var dirty = new HashSet<ChunkCoord>();
        foreach (var col in cols) { world[col.Coord] = col; VoxelLighting.StitchNeighbors(world, col.Coord, dirty); }
        double stitchMs = sw.Elapsed.TotalMilliseconds;

        int meshR = Math.Max(0, R - 1); int meshed = 0; long verts = 0, tris = 0; int sections = 0;
        var nb = new ChunkColumn[9];
        // warm up JIT once
        MeshChunk(world, nb, new ChunkCoord(ox, oz), ref verts, ref tris, ref sections);
        verts = tris = 0; sections = 0;
        sw.Restart();
        for (int x = -meshR; x <= meshR; x++) for (int z = -meshR; z <= meshR; z++)
        { MeshChunk(world, nb, new ChunkCoord(ox + x, oz + z), ref verts, ref tris, ref sections); meshed++; }
        double meshMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"gen    {genMs / cols.Count,7:F2} ms/chunk  ({cols.Count} chunks)");
        for (int i = 0; i < ChunkGenerator.StageMs.Length; i++)
            Console.WriteLine($"   {ChunkGenerator.StageNames[i],-16} {ChunkGenerator.StageMs[i] / cols.Count,7:F2} ms/chunk");
        Console.WriteLine($"stitch {stitchMs / cols.Count,7:F2} ms/chunk");
        Console.WriteLine($"mesh   {meshMs / meshed,7:F2} ms/chunk  ({meshed} chunks, {sections} sections, {verts / meshed} verts/chunk, {tris / meshed} tris/chunk)");
        return 0;
    }

    static void MeshChunk(Dictionary<ChunkCoord, ChunkColumn> world, ChunkColumn[] nb, ChunkCoord c, ref long verts, ref long tris, ref int sections)
    {
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
        { world.TryGetValue(new ChunkCoord(c.X + dx, c.Z + dz), out var col); nb[(dx + 1) + (dz + 1) * 3] = col; }
        for (int s = 0; s < VoxelConstants.SectionCount; s++)
        {
            var snap = Capture(new SectionKey(c, s), 0, nb);
            if (!snap.Valid) continue;
            var r = ChunkMesher.Build(snap);
            verts += r.VertexCount; tris += (r.OpaqueCount + r.WaterCount + r.TransparentCount) / 3; sections++;
            snap.Release(); r.ReleaseBuffers();
        }
    }
        // Copy of VoxelWorld.CaptureFromNeighborhood (VoxelWorld cannot be type-loaded outside Unity).
    public static SectionSnapshot Capture(SectionKey k,int stamp,ChunkColumn[] nb)
        {
            ChunkColumn center=nb!=null&&nb.Length>=9?nb[4]:null;
            if(center==null)return default;
            var sec=center.Sections[k.Section];
            if(sec==null||sec.NonAir==0)return default;

            const int S=18;
            int sampleCount=S*S*S;
            ushort[] voxels=ArrayPool<ushort>.Shared.Rent(sampleCount);
            byte[] meta=ArrayPool<byte>.Shared.Rent(sampleCount);
            byte[] lights=ArrayPool<byte>.Shared.Rent(sampleCount);
            int baseY=VoxelConstants.MinY+k.Section*16;

            for(int x=0;x<S;x++)
            {
                int cxOff=x==0?-1:(x==17?1:0);
                int lx=x==0?15:(x==17?0:x-1);
                for(int z=0;z<S;z++)
                {
                    int czOff=z==0?-1:(z==17?1:0);
                    int lz=z==0?15:(z==17?0:z-1);
                    ChunkColumn col=nb[(cxOff+1)+(czOff+1)*3];
                    int row=(x*S+z)*S;
                    for(int y=0;y<S;y++)
                    {
                        int wy=baseY+y-1,idx=row+y;
                        if(wy<VoxelConstants.MinY)
                        {
                            voxels[idx]=(ushort)BlockId.Bedrock;meta[idx]=0;lights[idx]=0;
                        }
                        else if(wy>VoxelConstants.MaxY)
                        {
                            voxels[idx]=(ushort)BlockId.Air;meta[idx]=0;lights[idx]=0xF0;
                        }
                        else if(col==null)
                        {
                            voxels[idx]=(ushort)BlockId.Air;meta[idx]=0;lights[idx]=0xF0;
                        }
                        else
                        {
                            int yo=wy-VoxelConstants.MinY,secIndex=yo>>4,ly=yo&15;
                            var sourceSec=col.Sections[secIndex];
                            if(sourceSec==null)
                            {
                                voxels[idx]=(ushort)BlockId.Air;meta[idx]=0;
                                lights[idx]=wy>col.LightCeilings[lx*16+lz]?(byte)0xF0:(byte)0;
                            }
                            else
                            {
                                int si=((lx*16)+lz)*16+ly;
                                voxels[idx]=sourceSec.Blocks[si];
                                meta[idx]=sourceSec.Meta[si];
                                lights[idx]=sourceSec.Light[si];
                            }
                        }
                    }
                }
            }
            return new SectionSnapshot(k,stamp,voxels,meta,lights);
        }

}
