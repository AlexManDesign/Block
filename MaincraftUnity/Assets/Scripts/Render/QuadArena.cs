// GPU storage for section quads: one big structured buffer of packed vertices (12 uints per quad,
// as the mesher writes them) with a first-fit allocator over quad ranges. Sections live in a few such
// pages instead of a buffer each, so a whole pass of all visible sections is one instanced draw per
// page (see Renderer.DrawTerrain).
using System.Collections.Generic;
using UnityEngine;

namespace Maincraft
{
    public sealed class QuadArena
    {
        public const int GRAIN = 16;          // allocation unit in quads
        public readonly GraphicsBuffer buf;
        public readonly int capQuads;
        public int usedQuads;
        // free ranges sorted by start (quads)
        readonly List<int> fs = new List<int>(), fl = new List<int>();

        public QuadArena(int capQuads)
        {
            this.capQuads = capQuads;
            buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capQuads * 12, 4);
            fs.Add(0); fl.Add(capQuads);
        }

        public static int Round(int quads) => (quads + GRAIN - 1) / GRAIN * GRAIN;

        /// <summary>First free range of n quads (n a multiple of GRAIN); -1 when none is left.</summary>
        public int Alloc(int n)
        {
            for (int i = 0; i < fs.Count; i++)
            {
                if (fl[i] < n) continue;
                int s = fs[i];
                if (fl[i] == n) { fs.RemoveAt(i); fl.RemoveAt(i); }
                else { fs[i] = s + n; fl[i] -= n; }
                usedQuads += n;
                return s;
            }
            return -1;
        }

        public void Free(int start, int n)
        {
            usedQuads -= n;
            // binary search for the first free range after start
            int lo = 0, hi = fs.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (fs[mid] < start) lo = mid + 1; else hi = mid; }
            bool joinPrev = lo > 0 && fs[lo - 1] + fl[lo - 1] == start;
            bool joinNext = lo < fs.Count && start + n == fs[lo];
            if (joinPrev && joinNext) { fl[lo - 1] += n + fl[lo]; fs.RemoveAt(lo); fl.RemoveAt(lo); }
            else if (joinPrev) fl[lo - 1] += n;
            else if (joinNext) { fs[lo] = start; fl[lo] += n; }
            else { fs.Insert(lo, start); fl.Insert(lo, n); }
        }

        public void Release() => buf.Release();
    }
}
