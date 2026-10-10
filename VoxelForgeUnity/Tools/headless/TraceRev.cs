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
            if (!__traceStacks.TryGetValue(c.key, out d)) __traceStacks[c.key] = d = new Dictionary<string, int>();
            int n; d.TryGetValue(s, out n); d[s] = n + 1;
        }
        public static void __traceDump(IEnumerable<string> keys)
        {
            foreach (var k in keys)
            {
                Dictionary<string, int> d; if (!__traceStacks.TryGetValue(k, out d)) continue;
                Console.WriteLine("    trace " + k + ":");
                foreach (var kv in d.OrderByDescending(x => x.Value).Take(4)) Console.WriteLine("      x" + kv.Value + "  " + kv.Key);
            }
        }
    }
}
