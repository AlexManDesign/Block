// Compiled only by `VF_TRACE_REV=1 sh run.sh`: run.sh copies the game scripts and rewrites every `c.meshRev++` into
// VF.__traceRev(c), so the idle remesh probe can say WHO keeps bumping a chunk's mesh revision (grouped call stacks).
using System;
using System.Collections.Generic;
using System.Linq;
namespace VoxelForge
{
    public static partial class VF
    {
        public static bool __traceOn;
        public static readonly Dictionary<string, Dictionary<string, int>> __traceStacks = new Dictionary<string, Dictionary<string, int>>();
        public static void __traceRev(Chunk c)
        {
            c.meshRev++;
            if (!__traceOn) return;
            var st = new System.Diagnostics.StackTrace(1, false).GetFrames();
            var s = string.Join(" <- ", st.Take(7).Select(f => f.GetMethod().Name));
            Dictionary<string, int> d;
            if (__traceFluidCells.Count > 100000) return;
            if (!__traceStacks.TryGetValue(c.key, out d)) __traceStacks[c.key] = d = new Dictionary<string, int>();
            int n; d.TryGetValue(s, out n); d[s] = n + 1;
        }
        public static readonly Dictionary<string, List<string>> __traceFluidCells = new Dictionary<string, List<string>>();
        public static void __traceFluid(int x, int y, int z, int id)
        {
            if (!__traceOn) return;
            var k = x + "," + y + "," + z; List<string> l;
            if (!__traceFluidCells.TryGetValue(k, out l)) __traceFluidCells[k] = l = new List<string>();
            if (l.Count < 40) l.Add(UnityEngine.Time.frameCount + ":" + getBlock(x, y, z) + ">" + id);
        }
        public static void __traceDump(IEnumerable<string> keys)
        {
            var ks = new HashSet<string>(keys);
            foreach (var kv in __traceFluidCells.Where(c => { var a = c.Key.Split(','); return ks.Contains(ckey(JS.floor(int.Parse(a[0]) / 16.0), JS.floor(int.Parse(a[2]) / 16.0))); }).OrderByDescending(c => c.Value.Count).Take(12))
                Console.WriteLine("    fluid cell " + kv.Key + " x" + kv.Value.Count + ": " + string.Join(" ", kv.Value));
            foreach (var k in keys)
            {
                Dictionary<string, int> d; if (!__traceStacks.TryGetValue(k, out d)) continue;
                Console.WriteLine("    trace " + k + ":");
                foreach (var kv in d.OrderByDescending(x => x.Value).Take(4)) Console.WriteLine("      x" + kv.Value + "  " + kv.Key);
            }
        }
    }
}
