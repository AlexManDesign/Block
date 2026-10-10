// C# side of the light parity harness: replays <out>/<win>/scenario.txt (written by light-js.js) on the real port
// (GenWorker for generation + initial light, VF.Light.cs / VF.LightRules.cs for stitching, incremental relight,
// the dirty queue and the player-edit light path) and writes <out>/<win>/cs.txt in exactly the format of js.txt,
// plus <out>/rules_cs.txt. Compiled together with all project scripts, as a part of the VF partial class so the
// private light entry points (directSkyColumn, notePlayerEditLight, flushImmediatePlayerEditLighting) are reachable.
// Usage: mono LightParity.exe <outDir> <window> <seed> [--dump=<step>]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VoxelForge
{
    public static partial class VF
    {
        static readonly List<string> lpOut = new List<string>();
        static int lpStep, lpDump = -1, lpSeed, lpGeoChunks;
        static string lpDir;
        static GenWorker lpGen;
        // time spent inside the port's light code (stitch on chunk load / dirty queue + player flush + repair)
        static readonly System.Diagnostics.Stopwatch lpStitchSw = new System.Diagnostics.Stopwatch(), lpRelightSw = new System.Diagnostics.Stopwatch();
        static int lpT(Func<int> f) { lpRelightSw.Start(); try { return f(); } finally { lpRelightSw.Stop(); } }

        static uint lpFnv(byte[] a, uint h) { for (int i = 0; i < a.Length; i++) h = unchecked((h ^ a[i]) * 16777619u); return h; }
        static uint lpFnv1(int v, uint h) { return unchecked((h ^ (uint)v) * 16777619u); }
        static string lpHex(uint h) { return h.ToString("x"); }

        static void lpState()
        {
            var keys = chunks.Keys.ToList(); keys.Sort(string.CompareOrdinal);
            lpOut.Add("S " + lpStep + " q=" + lightDirtyCount() + " k=" + lightDirtyKeys.Count + " n=" + keys.Count);
            foreach (var k in keys)
            {
                var c = chunks.Get(k); var ds = c.dirtySections != null ? c.dirtySections.ToList() : new List<int>(); ds.Sort();
                var sb = new StringBuilder(k + " d=" + (c.dirty ? 1 : 0) + " f=" + (c.fullMeshDirty ? 1 : 0) + " r=" + c.meshRev + " ds=" + string.Join(",", ds));
                for (int si = 0; si < 24; si++)
                {
                    var sec = c.sections[si]; if (sec == null) continue;
                    sb.Append(" " + si + ":" + lpHex(lpFnv(sec.blocks, 2166136261u)) + ":" + (sec.light != null ? lpHex(lpFnv(sec.light, 2166136261u)) : "-"));
                }
                lpOut.Add(sb.ToString());
            }
            if (lpStep == lpDump)
            {
                using (var f = new BinaryWriter(File.Create(Path.Combine(lpDir, "cs_dump_" + lpStep + ".bin"))))
                    foreach (var k in keys)
                    {
                        var c = chunks.Get(k); f.Write(c.cx); f.Write(c.cz);
                        for (int si = 0; si < 24; si++)
                        {
                            var sec = c.sections[si]; f.Write((byte)(sec != null ? 1 : 0));
                            if (sec != null) { f.Write(sec.blocks); if (sec.light != null) f.Write(sec.light); else for (int i = 0; i < 4096; i++) f.Write((byte)0xf0); }
                        }
                    }
            }
        }

        static void lpGenChunk(int cx, int cz)
        {
            var k = ckey(cx, cz);
            if (chunks.Has(k)) { lpOut.Add("G " + k + " skip"); return; }
            var es = editsByChunk.Get(k); var delta = new List<int[]>();
            if (es != null) foreach (var q in es.Values) delta.Add((int[])q.Clone());
            var m = lpGen.Run(new GenJob { id = 1, cx = cx, cz = cz, seed = lpSeed, key = k, delta = delta });
            var dense = new byte[CHUNK * WORLD_H * CHUNK]; uint gh = 2166136261u;
            foreach (var q in m.sections) { Buffer.BlockCopy(q.blocks, 0, dense, q.si * 4096, 4096); gh = lpFnv1(q.si, gh); gh = lpFnv(q.light, gh); }
            var ip = buildInitialPackedLightDense(dense, m.meta);
            lpOut.Add("G " + k + " sec=" + m.sections.Count + " meta=" + m.meta.Count + " sim=" + m.sim.Length + " fb=" + m.fluidBoundary.Length + " genlight=" + lpHex(gh) + " initdense=" + lpHex(lpFnv(ip, 2166136261u)));
            // integrateGenResult
            installGeneratedMeta(m.cx, m.cz, m.meta ?? new List<GenMetaEntry>());
            var sections = emptySections();
            foreach (var q in m.sections)
                if (q != null && q.si >= 0 && q.si < SECTION_COUNT)
                {
                    var light = q.light; if (light == null) { light = new byte[4096]; for (int i = 0; i < 4096; i++) light[i] = 0xf0; }
                    sections[q.si] = new Section(q.blocks, light);
                }
            // createChunkFromData (light-relevant part), same geometry toggling as light-js.js
            bool geo = (lpGeoChunks++ & 1) == 1;
            var c = new Chunk { cx = cx, cz = cz, key = k, sections = sections, dirty = true, fullMeshDirty = !geo, dirtySections = new HashSet<int>(), sectionGeo = geo ? new SectionGeo[SECTION_COUNT] : null, meshRev = 0, boundaryRev = 1, mapRev = 0 };
            chunks.Set(k, c);
            chunkFastSet(c);
            lpStitchSw.Start(); stitchChunkLight(c); lpStitchSw.Stop();
            // installMetadataSimulationSeeds: light part, same iteration as the port
            Dictionary<int, JObj> mm;
            if (metaByChunk.TryGetValue(c.key, out mm) && mm.Count > 0)
                foreach (var kv in mm.ToList())
                {
                    var mt = kv.Value; if (mt == null) continue;
                    int code = kv.Key, lx = code & 15, lz = (code >> 4) & 15, y = (code >> 8) + WORLD_MIN_Y, x = c.cx * CHUNK + lx, z = c.cz * CHUNK + lz;
                    var d = virtualDefFromMeta(mt);
                    if (d != null && d.light != 0) queueLightUpdate(x, y, z);
                }
        }
        static void lpUnload(int cx, int cz)
        {
            var k = ckey(cx, cz); var c = chunks.Get(k); if (c == null) return;
            chunks.Delete(k); generatedMetaByChunk.Remove(k); chunkFastDelete(c);
        }
        // setBlock(): the light-relevant steps, mirrored from light-js.js setB()
        static void lpSet(int x, int y, int z, int id, JObj meta)
        {
            if (!yInWorld(y) || y == WORLD_MIN_Y) return;
            int oldId = getBlock(x, y, z); var k = key3(x, y, z); var oldMeta = getBlockMeta(x, y, z); int oldLightSig = lightSignatureState(oldId, oldMeta);
            int cx = x >> 4, cz = z >> 4; var mk = ckey(cx, cz);
            Dictionary<int, JObj> mm; metaByChunk.TryGetValue(mk, out mm);
            if (meta != null)
            {
                var mv = meta.Clone(); blockMeta.Set(k, mv);
                if (mm == null) metaByChunk[mk] = mm = new Dictionary<int, JObj>();
                mm[metaLocalKey(x, y, z)] = mv;
            }
            else { blockMeta.Delete(k); if (mm != null) { mm.Remove(metaLocalKey(x, y, z)); if (mm.Count == 0) metaByChunk.Remove(mk); } }
            addEditIndex(x, y, z, id, false);
            var c = chunkFastGet(cx, cz);
            if (c != null) { chunkSet(c, x - cx * CHUNK, y, z - cz * CHUNK, id); c.mapRev++; }
            int newLightSig = lightSignatureState(id, meta);
            if (oldLightSig != newLightSig) { queueLightUpdate(x, y, z); notePlayerEditLight(x, y, z); }
        }

        static void lpExec(string line)
        {
            lpStep++;
            var a = line.Split(' ');
            Func<int, int> n = i => int.Parse(a[i]);
            lpOut.Add("C " + lpStep + " " + line);
            switch (a[0])
            {
                case "gen": lpGenChunk(n(1), n(2)); lpState(); break;
                case "unload": lpUnload(n(1), n(2)); lpState(); break;
                case "set": lpSet(n(1), n(2), n(3), n(4), a[5] == "-" ? null : (JObj)Json.Parse(a[5])); lpOut.Add("Q " + lightDirtyCount() + " " + lightDirtyKeys.Count); break;
                case "light": lpOut.Add("L " + lpT(() => processLightDirty(double.PositiveInfinity))); lpState(); break;
                case "lightpast": lpOut.Add("L " + lpT(() => processLightDirty(0))); lpState(); break;
                case "lightfuture": lpOut.Add("L " + lpT(() => processLightDirty(1e15))); lpState(); break;
                case "pbegin": if (playerEditDepth == 0) playerEditLightN = 0; playerEditDepth++; break;
                case "pend": playerEditDepth--; if (playerEditDepth == 0) lpOut.Add("P " + (lpT(() => flushImmediatePlayerEditLighting() ? 1 : 0) == 1 ? "true" : "false")); lpState(); break;
                case "repair": lpT(() => { repairLightAt(n(1), n(2), n(3)); return 0; }); lpState(); break;
                case "probe":
                    {
                        int x = n(1), y = n(2), z = n(3);
                        if (chunkFastGet(x >> 4, z >> 4) == null) { lpOut.Add("R " + getPackedLightWorld(x, y, z)); break; }
                        lpOut.Add("R " + getPackedLightWorld(x, y, z) + " " + blockEmissionAt(x, y, z) + " " + (lightStopsAt(x, y, z) ? 1 : 0) + " " + lightCostAt(x, y, z) + " " + lpHex(lpFnv(directSkyColumn(x, z, null), 2166136261u)));
                        break;
                    }
                default: throw new Exception("bad command " + line);
            }
        }

        static string lpRule(int id, JObj m) { return (lightStopsState(id, m) ? "1" : "0") + (lightAttenuationState(id, m) ? "1" : "0") + ":" + lightSignatureState(id, m); }
        static void lpRules(string outDir)
        {
            var rl = new List<string>();
            var metas = new JObj[] { null, new JObj(), (JObj)Json.Parse("{\"waterlogged\":true}"), (JObj)Json.Parse("{\"waterlogged\":1}"), (JObj)Json.Parse("{\"waterlogged\":0}"),
                (JObj)Json.Parse("{\"waterlogged\":\"\"}"), (JObj)Json.Parse("{\"waterlogged\":\"x\"}"), (JObj)Json.Parse("{\"berries\":true}") };
            for (int id = 0; id < 256; id++)
            {
                var s = new StringBuilder(id + " " + (isOpaque(id) ? 1 : 0) + (lightStops(id) ? 1 : 0) + (lightAttenuation(id) ? 1 : 0) + " " + lightSignature(id) + " " + lightCost(id));
                foreach (var m in metas) s.Append(" " + lpRule(id, m));
                rl.Add(s.ToString());
            }
            var vkeys = VIRTUAL_BLOCKS.Keys.ToList(); vkeys.Add("NO_SUCH_KEY");
            foreach (var vid in new[] { B.VIRTUAL_OPAQUE, B.VIRTUAL_TRANSPARENT, B.VIRTUAL_CUTOUT, B.VIRTUAL_PLANT })
                foreach (var k in vkeys)
                {
                    var s = new StringBuilder(vid + " " + k);
                    s.Append(" " + lpRule(vid, new JObj().Set("v", k)));
                    s.Append(" " + lpRule(vid, new JObj().Set("v", k).Set("waterlogged", true)));
                    s.Append(" " + lpRule(vid, new JObj().Set("v", k).Set("waterlogged", 0.0)));
                    rl.Add(s.ToString());
                }
            File.WriteAllText(Path.Combine(outDir, "rules_cs.txt"), string.Join("\n", rl) + "\n");
        }

        public static int LightParityRun(string[] a)
        {
            string outDir = a[0], win = a[1]; lpSeed = int.Parse(a[2]);
            foreach (var s in a) if (s.StartsWith("--dump=")) lpDump = int.Parse(s.Substring(7));
            lpDir = Path.Combine(outDir, win);
            InitData(); InitBlocks();
            lpGen = new GenWorker(lpSeed, blocks);
            var t0 = DateTime.UtcNow;
            var lines = File.ReadAllLines(Path.Combine(lpDir, "scenario.txt"));
            foreach (var raw in lines) { var l = raw.Trim(); if (l.Length > 0) lpExec(l); }
            File.WriteAllText(Path.Combine(lpDir, "cs.txt"), string.Join("\n", lpOut) + "\n");
            lpRules(outDir);
            Console.WriteLine("cs " + win + ": " + lpStep + " commands, " + chunks.Count + " chunks, " + lpOut.Count + " state lines, " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + "ms (light code: stitch " +
                lpStitchSw.Elapsed.TotalMilliseconds.ToString("0") + "ms, relight " + lpRelightSw.Elapsed.TotalMilliseconds.ToString("0") + "ms)");
            return 0;
        }
    }
}

public static class LightParity
{
    public static int Main(string[] a) { return VoxelForge.VF.LightParityRun(a); }
}
