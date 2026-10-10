// C# side of the worldgen parity check (same modes/outputs as wg.js, file prefix cs_ instead of js_).
//   mono WorldgenParity.exe <resDir> names  <B.json>
//   mono WorldgenParity.exe <resDir> chunks <seed> <chunkList> <outDir>
//   mono WorldgenParity.exe <resDir> an     <seed> <ptsFile> <outFile>
//   mono WorldgenParity.exe <resDir> rav    <seed> <cells> <outFile>
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using VoxelForge;

public static class WorldgenParity
{
    static string hx(double v) { return BitConverter.DoubleToInt64Bits(v).ToString("x16"); }
    public static int Main(string[] a)
    {
        string R = a[0].EndsWith("/") ? a[0] : a[0] + "/", mode = a[1];
        GenData.Build(File.ReadAllBytes(R + "main_climate.bytes"), File.ReadAllText(R + "main_ships.json"), File.ReadAllText(R + "main_pyramid_blocks.json"), File.ReadAllText(R + "main_outpost_blocks.json"));
        typeof(GenData).GetField("inited", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
        if (mode == "names")
        {
            var sb = new StringBuilder("{"); bool first = true;
            foreach (var kv in B.NAMES) { if (!first) sb.Append(","); first = false; sb.Append("\"" + kv.Key + "\":" + kv.Value); }
            File.WriteAllText(a[2], sb.Append("}").ToString()); return 0;
        }
        int seed = int.Parse(a[2]); var K = new WorldGenKernel(seed);
        if (mode == "chunks")
        {
            string outDir = a[4];
            foreach (var line in File.ReadAllLines(a[3]))
            {
                if (line.Trim().Length == 0) continue;
                var p = line.Split(' '); int cx = int.Parse(p[0]), cz = int.Parse(p[1]);
                K.setSeed(seed);
                var d = K.generateDense(cx, cz);
                File.WriteAllBytes(outDir + "/cs_" + seed + "_" + cx + "_" + cz + ".bin", d.data);
                var meta = K.structureMetaForChunk(cx, cz); meta.AddRange(K.seaPickleMetaForDense(d.data, d.meta, cx, cz)); meta.AddRange(K.caveVineMetaForDense(d.data, d.meta, cx, cz));
                var ms = new StringBuilder();
                foreach (var m in meta)
                {
                    ms.Append(m.lx + "," + m.iy + "," + m.lz + ":"); var keys = new List<string>(m.m.Keys); keys.Sort(StringComparer.Ordinal);
                    foreach (var k in keys) { var v = m.m[k]; ms.Append(k + "=" + (v is bool ? ((bool)v ? "true" : "false") : JS.ToStr(v)) + ";"); }
                    ms.Append("\n");
                }
                File.WriteAllText(outDir + "/cs_" + seed + "_" + cx + "_" + cz + ".meta", ms.ToString());
                var bi = new StringBuilder();
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
                {
                    int X = cx * 16 + x, Z = cz * 16 + z; var c = K.columnInfo(X, Z);
                    bi.Append(c.biome + " " + JS.ToStr(c.origH) + " " + K.heightAt(X, Z) + " " + K.generatedSurfaceAt(X, Z) + " " + hx(c.origH) + " " + hx(c.factor) + " " + hx(c.mAmp) + " " + hx(c.peak) + "\n");
                }
                File.WriteAllText(outDir + "/cs_" + seed + "_" + cx + "_" + cz + ".bio", bi.ToString());
            }
        }
        else if (mode == "an")
        {
            var sb = new StringBuilder();
            foreach (var line in File.ReadAllLines(a[3]))
            {
                if (line.Trim().Length == 0) continue;
                var p = line.Split(' '); int x = int.Parse(p[0]), y = int.Parse(p[1]), z = int.Parse(p[2]);
                var f = K.featureInfoAt(x, z); var lp = K.landPlantInfoAt(x, z); var c = K.columnInfo(x, z);
                sb.Append(K.baseBlock(x, y, z) + " " + K.terrainBlock(x, y, z) + " " + K.landPlantAt(x, z) + " " + (K.treeAt(x, z) ? 1 : 0) + " " + K.aquaPlantAt(x, y, z) + " " + (K.structureChestKindAt(x, y, z) ?? "null") + " "
                    + K.caveBiomeAt(x, y, z) + " " + (f == null ? "null" : f.kind + "/" + f.height) + " " + (lp == null ? "null" : lp.id + "/" + lp.height) + " " + K.iceSpikeInfoAt(x, z) + " " + K.bambooInfoAt(x, z) + " " + (K.cactusAt(x, z) ? 1 : 0) + " "
                    + JS.ToStr(K.dryAt(x, z)) + " " + JS.ToStr(K.terrainDensity(x, y, z)) + " " + K.mineshaftsForChunk(x >> 4, z >> 4).Count + " "
                    + hx(K.dryAt(x, z)) + " " + hx(K.terrainDensity(x, y, z)) + " " + c.biome + " " + hx(c.origH) + " " + hx(c.temp) + " " + hx(c.moist) + " " + hx(c.continental) + " " + hx(c.erosion) + " " + hx(c.weird) + " " + hx(c.peak) + " " + hx(c.factor) + " " + hx(c.mAmp) + " "
                    + K.heightAt(x, z) + " " + JS.ToStr(K.heightAtRaw(x, z)) + "\n");
            }
            File.WriteAllText(a[4], sb.ToString());
        }
        else if (mode == "rav")
        {
            int C = int.Parse(a[3]);
            var valley = (double[][])typeof(WorldGenKernel).GetField("MAIN_VALLEY_OFFSETS", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var sb = new StringBuilder("valley");
            foreach (var q in valley) { sb.Append(' '); for (int i = 0; i < q.Length; i++) sb.Append((i > 0 ? "," : "") + hx(q[i])); }
            sb.Append('\n');
            var plan = typeof(WorldGenKernel).GetMethod("planRavine", BindingFlags.NonPublic | BindingFlags.Instance);
            var args = new object[2];
            for (int x = -C; x < C; x++) for (int z = -C; z < C; z++)
            {
                args[0] = x; args[1] = z; object pl = plan.Invoke(K, args); if (pl == null) continue;
                var t = pl.GetType(); Func<string, object> g = n => t.GetField(n).GetValue(pl);
                sb.Append(x + " " + z + " " + g("depth") + " " + hx((double)g("waterFrac")) + " " + hx((double)g("minx")) + " " + hx((double)g("maxx")) + " " + hx((double)g("minz")) + " " + hx((double)g("maxz")));
                foreach (var n in (List<double[]>)g("nodes")) sb.Append(" " + hx(n[0]) + "," + hx(n[1]) + "," + hx(n[2]));
                sb.Append('\n');
            }
            File.WriteAllText(a[4], sb.ToString());
        }
        else { Console.Error.WriteLine("unknown mode " + mode); return 2; }
        return 0;
    }
}
