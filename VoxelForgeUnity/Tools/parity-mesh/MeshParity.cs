// C# side of the mesher parity harness: boots the real VF data/block tables (InitData, InitBlocks, buildMesherConfig),
// runs MeshCore.run on every job written by gen-jobs.js (same order, one shared core instance) and writes
//   <out>/cs/<name>.bin  normalized output, same format as gen-jobs.js dumpResult()
//   <out>/cfg_cs.json    the MDef config MeshCore receives
// Usage: mono MeshParity.exe <outDir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using VoxelForge;

public static class MeshParity
{
    static void Call(string name) { typeof(VF).GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null); }

    static double ToUint(double d, double mod) { if (double.IsNaN(d) || double.IsInfinity(d)) return 0; d = Math.Truncate(d) % mod; return d < 0 ? d + mod : d; }
    static uint Bits(float f) { return BitConverter.ToUInt32(BitConverter.GetBytes(f), 0); }
    static void Bucket(List<uint> w, GeoBucket g)
    {
        g = g ?? GeoBucket.Empty; w.Add((uint)g.VCount); w.Add((uint)g.ICount);
        for (int k = 0; k < g.VCount; k++)
        {
            var v = g.v[k];
            w.Add(Bits(v.x)); w.Add(Bits(v.y)); w.Add(Bits(v.z)); w.Add(v.u); w.Add(v.v);
            w.Add((uint)ToUint(Half.ToFloat(v.tile), 65536)); w.Add(Bits(v.sa)); w.Add((uint)ToUint(Half.ToFloat(v.light), 256));
        }
        for (int k = 0; k < g.ICount; k++) w.Add((uint)g.i[k]);
    }
    static byte[] Dump(MeshResult r)
    {
        var w = new List<uint> { 0x4d564631, r.full ? 1u : 0u, (uint)r.sis.Length }; foreach (var s in r.sis) w.Add((uint)s);
        if (r.full && r.sis.Length > 0)
        {
            w.Add(1);
            Bucket(w, r.aggO); Bucket(w, r.aggC); Bucket(w, r.aggW); Bucket(w, r.aggT);
            for (int si = 0; si < 24; si++)
            {
                var g = r.full24[si];
                if (g == null) { for (int k = 0; k < 8; k++) w.Add(0); continue; }
                for (int b = 0; b < 4; b++) { var q = g.Get(b) ?? GeoBucket.Empty; w.Add((uint)(q.VCount * 8)); w.Add((uint)q.ICount); }
            }
            // structural self-check: aggregate == concatenation of per-section buckets (local indices + running base)
            for (int b = 0; b < 4; b++)
            {
                var agg = b == 0 ? r.aggO : b == 1 ? r.aggC : b == 2 ? r.aggW : r.aggT; int vo = 0, io = 0;
                for (int si = 0; si < 24; si++)
                {
                    var g = r.full24[si]; if (g == null) continue; var q = g.Get(b);
                    for (int k = 0; k < q.VCount; k++) if (!q.v[k].Equals(agg.v[vo + k])) throw new Exception("full24/agg vertex mismatch");
                    for (int k = 0; k < q.ICount; k++) if (q.i[k] + vo != agg.i[io + k]) throw new Exception("full24/agg index mismatch");
                    vo += q.VCount; io += q.ICount;
                }
                if (vo != agg.VCount || io != agg.ICount) throw new Exception("full24/agg size mismatch");
            }
        }
        else
        {
            w.Add(0); var parts = r.parts ?? new List<KeyValuePair<int, SectionGeo>>(); w.Add((uint)parts.Count);
            foreach (var p in parts) { w.Add((uint)p.Key); Bucket(w, p.Value.o); Bucket(w, p.Value.c); Bucket(w, p.Value.w); Bucket(w, p.Value.t); }
        }
        var bytes = new byte[w.Count * 4]; Buffer.BlockCopy(w.ToArray(), 0, bytes, 0, bytes.Length); return bytes;
    }
    static string J(string s) { return s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
    static string N(int v) { return v < 0 ? "null" : v.ToString(); }
    static string MDefJson(MDef d)
    {
        if (d == null) return "null";
        return "{\"side\":" + N(d.side) + ",\"top\":" + N(d.top) + ",\"bottom\":" + N(d.bottom) + ",\"front\":" + N(d.front) + ",\"frontL\":" + N(d.frontL) + ",\"frontR\":" + N(d.frontR) +
            ",\"topL\":" + N(d.topL) + ",\"topR\":" + N(d.topR) + ",\"corner\":" + N(d.corner) + ",\"partTop\":" + N(d.partTop) + ",\"partBottom\":" + N(d.partBottom) +
            ",\"solid\":" + (d.solid ? "true" : "false") + ",\"transparent\":" + (d.transparent ? "true" : "false") + ",\"cutout\":" + (d.cutout ? "true" : "false") +
            ",\"plant\":" + (d.plant ? "true" : "false") + ",\"waterPlant\":" + (d.waterPlant ? "true" : "false") + ",\"needsWater\":" + (d.needsWater ? "true" : "false") +
            ",\"axislog\":" + (d.axislog ? "true" : "false") + ",\"alpha\":" + JS.ToStr(d.alpha) + ",\"special\":" + J(d.special) + ",\"shape\":" + J(d.shape) + ",\"light\":" + d.light + "}";
    }

    public static int Main(string[] a)
    {
        string dir = a[0];
        Call("InitData"); Call("InitBlocks"); VF.buildMesherConfig();
        var cfg = VF.workerBlockCfg; var vcfg = VF.workerVirtualCfg;
        var sb = new StringBuilder("{\"blocks\":["); for (int i = 0; i < cfg.Length; i++) { if (i > 0) sb.Append(","); sb.Append(MDefJson(cfg[i])); }
        sb.Append("],\"vblocks\":{"); bool first = true;
        foreach (var kv in vcfg) { if (!first) sb.Append(","); first = false; sb.Append(J(kv.Key) + ":" + MDefJson(kv.Value)); }
        sb.Append("}}"); File.WriteAllText(Path.Combine(dir, "cfg_cs.json"), sb.ToString());

        Directory.CreateDirectory(Path.Combine(dir, "cs"));
        var core = new MeshCore(cfg, vcfg); int id = 0;
        foreach (var raw in File.ReadAllLines(Path.Combine(dir, "jobs", "index.txt")))
        {
            var name = raw.Trim(); if (name.Length == 0) continue;
            var o = (JObj)Json.Parse(File.ReadAllText(Path.Combine(dir, "jobs", name + ".json")));
            var job = new MeshJob { id = ++id, cx = o.Int("cx"), cz = o.Int("cz"), full = o.Bool("full"), key = name, sections = new List<SecPayload>(), meta = new List<MetaEntry>() };
            var sis = (List<object>)o.Get("sis"); job.sis = new int[sis.Count]; for (int k = 0; k < sis.Count; k++) job.sis[k] = (int)Json.ToNum(sis[k]);
            foreach (JObj s in (List<object>)o.Get("sections"))
                job.sections.Add(new SecPayload { dx = s.Int("dx"), dz = s.Int("dz"), si = s.Int("si"), blocks = Convert.FromBase64String(s.Str("b")), light = s.Str("l") != null ? Convert.FromBase64String(s.Str("l")) : null });
            foreach (List<object> q in (List<object>)o.Get("meta"))
                job.meta.Add(new MetaEntry((int)Json.ToNum(q[0]), (int)Json.ToNum(q[1]), (int)Json.ToNum(q[2]), q[3] as JObj));
            var t0 = DateTime.UtcNow;
            var r = core.run(job);
            File.WriteAllBytes(Path.Combine(dir, "cs", name + ".bin"), Dump(r));
            Console.WriteLine("job " + name + " " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + "ms");
        }
        return 0;
    }
}
