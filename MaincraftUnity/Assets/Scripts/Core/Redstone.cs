// Port of redstone.js: signals 0-15 between sources, dust and the blocks they power, after Minecraft's rules.
//
// A source sends a signal to its neighbours (getSignal). Some also "strongly" power a block
// (getDirectSignal): a torch the block above it, a lever / button the block it hangs on, a pressure
// plate the block below, repeaters, comparators and observers the block in front. Dust only weakly
// powers the block it points into and the one under it. A conductive (full opaque) block passes a
// strong signal on to dust and components, a weak one only to components (lamps, pistons, doors,
// repeaters, torches). Dust levels drop by one per block along a line of dust.
// Delays are in game ticks (a redstone tick is two): repeater 2-8, torch 2, comparator 2, lamp off
// 4, observer pulse 2 + 2. Pistons push up to 12 blocks; sticky pistons pull one back.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static Maincraft.Blocks;

namespace Maincraft
{
    public static class RK
    {
        public const int NONE = 0, WIRE = 1, TORCH = 2, TORCH_OFF = 3, LEVER = 4, BUTTON = 5, PLATE = 6, BLOCK = 7, REPEATER = 8, COMPARATOR = 9,
            LAMP = 10, LAMP_ON = 11, PISTON = 12, HEAD = 13, OBSERVER = 14, DOOR = 15, TRAPDOOR = 16, GATE = 17, TNT = 18;
    }

    public sealed class Redstone
    {
        public static readonly byte[] RS_KIND = new byte[NBX];
        // blocks a piston can not move, and blocks it breaks instead of moving
        public static readonly byte[] RS_IMMOVABLE = new byte[NBX], RS_FRAGILE = new byte[NBX];

        static Redstone()
        {
            Blocks.Init();
            void K(int id, int v) { if (id != 0) RS_KIND[id] = (byte)v; }
            K(B.REDSTONE_WIRE, RK.WIRE); K(B.REDSTONE_TORCH, RK.TORCH); K(B.REDSTONE_TORCH_OFF, RK.TORCH_OFF); K(B.LEVER, RK.LEVER);
            K(B.REDSTONE_BLOCK, RK.BLOCK); K(B.REPEATER, RK.REPEATER); K(B.COMPARATOR, RK.COMPARATOR); K(B.REDSTONE_LAMP, RK.LAMP);
            K(B.REDSTONE_LAMP_ON, RK.LAMP_ON); K(B.PISTON, RK.PISTON); K(B.STICKY_PISTON, RK.PISTON); K(B.PISTON_HEAD, RK.HEAD);
            K(B.OBSERVER, RK.OBSERVER); K(B.TNT, RK.TNT);
            for (int id = 1; id < NB; id++)
            {
                int sh = SHAPE[id];
                if (sh == SH.BUTTON) RS_KIND[id] = RK.BUTTON;
                else if (sh == SH.PLATE) RS_KIND[id] = RK.PLATE;
                else if (sh == SH.DOOR) RS_KIND[id] = RK.DOOR;
                else if (sh == SH.TRAPDOOR) RS_KIND[id] = RK.TRAPDOOR;
                else if (sh == SH.GATE) RS_KIND[id] = RK.GATE;
            }
            foreach (var k in new[] { "BEDROCK", "OBSIDIAN", "CRYING_OBSIDIAN", "SPAWNER", "CHEST", "FURNACE", "FURNACE_LIT", "BARREL", "PISTON_HEAD", "SHIP_WHEEL", "END_PORTAL_FRAME" })
                if (Blocks.Id(k) != 0) RS_IMMOVABLE[Blocks.Id(k)] = 1;
            for (int id = 1; id < NB; id++)
            {
                int sh = SHAPE[id];
                if (SOLID[id] == 0 || sh == SH.DOOR || sh == SH.BED || sh == SH.WATER || sh == SH.LAVA || id == B.COCOA ||
                    id == B.REPEATER || id == B.COMPARATOR || (FLAGS[id] & BF_REPLACE) != 0) RS_FRAGILE[id] = 1;
            }
            // wet twins share the kinds of their dry blocks
            for (int id = 1; id < NB; id++) { int t = WET[id]; if (t != 0) { RS_KIND[t] = RS_KIND[id]; RS_IMMOVABLE[t] = RS_IMMOVABLE[id]; RS_FRAGILE[t] = RS_FRAGILE[id]; } }
        }

        static bool RS_STICK(int id) => id == B.STICKY_PISTON;
        public static long RsKey(int x, int y, int z) => ((long)(x + 1048576) * 2097152 + (z + 1048576)) * 512 + (y - WORLD_MIN_Y);
        static void FromKey(long k, out int x, out int y, out int z) { y = (int)(k % 512) + WORLD_MIN_Y; long xz = k / 512; z = (int)(xz % 2097152) - 1048576; x = (int)(xz / 2097152) - 1048576; }

        readonly World w;
        List<int> queue = new List<int>(); readonly HashSet<long> queued = new HashSet<long>();     // cells to re-evaluate
        readonly OrderedMap<long, long> timers = new OrderedMap<long, long>();                       // key -> due tick of a pending state change
        readonly HashSet<long> solved = new HashSet<long>();                                         // dust whose network was solved this tick
        readonly Dictionary<long, bool> powered = new Dictionary<long, bool>();                      // doors, trapdoors, gates, TNT: last powered state (act on edges)
        public Func<List<int>> entityCells;                // cells entities stand in (pressure plates): x, y, z, ...
        public Func<int, int, int, int> container;         // (x, y, z) -> comparator level of a container, or -1
        public Action<int, int, int> onTnt;                // (x, y, z) primed TNT
        public Action<string, int, int, int, bool> sound;  // (kind, x, y, z, on)
        readonly int[] L4 = new int[4];

        public Redstone(World world) { w = world; }

        // ---------------------------------------------------------------- bookkeeping
        public void Mark(int x, int y, int z)
        {
            long k = RsKey(x, y, z);
            if (!queued.Add(k)) return;
            queue.Add(x); queue.Add(y); queue.Add(z);
        }

        void MarkAround(int x, int y, int z, bool deep)
        {
            Mark(x, y, z);
            for (int d = 0; d < 6; d++)
            {
                int nx = x + RS_DX[d], ny = y + RS_DY[d], nz = z + RS_DZ[d];
                Mark(nx, ny, nz);
                if (deep) for (int e = 0; e < 6; e++) Mark(nx + RS_DX[e], ny + RS_DY[e], nz + RS_DZ[e]);
            }
        }

        void Later(int x, int y, int z, int delay)
        {
            long k = RsKey(x, y, z);
            if (!timers.ContainsKey(k)) timers.Set(k, w.tick + delay);
        }

        // every block change: what redstone cares about around it re-evaluates; observers watching it fire
        public void BlockChanged(int x, int y, int z, int oldId, int newId)
        {
            bool near = RS_KIND[oldId] != 0 || RS_KIND[newId] != 0;
            for (int d = 0; d < 6 && !near; d++) if (RS_KIND[w.GetBlock(x + RS_DX[d], y + RS_DY[d], z + RS_DZ[d])] != 0) near = true;
            if (!near) return;
            // a source, dust or conductive block changing reaches components two blocks away
            bool deep = RS_KIND[oldId] != 0 || RS_KIND[newId] != 0 || OPAQUE[oldId] != 0 || OPAQUE[newId] != 0;
            MarkAround(x, y, z, deep);
            for (int d = 0; d < 6; d++)
            {
                int ox = x + RS_DX[d], oy = y + RS_DY[d], oz = z + RS_DZ[d];
                if (w.GetBlock(ox, oy, oz) == B.OBSERVER && (w.GetMeta(ox, oy, oz) & 7) == RS_OPP[d]) Observe(ox, oy, oz);
            }
        }

        void Observe(int x, int y, int z)
        {
            int m = w.GetMeta(x, y, z);
            if ((m & 8) == 0) Later(x, y, z, 2);
        }

        // ---------------------------------------------------------------- signals
        // signal the source at (x, y, z) sends to its neighbour in direction d; strong: only a direct one
        int Emit(int x, int y, int z, int d, bool strong)
        {
            int id = w.GetBlock(x, y, z), k = RS_KIND[id];
            if (k == 0) return 0;
            int m = w.GetMeta(x, y, z);
            switch (k)
            {
                case RK.TORCH:
                {
                    int attach = m == 0 || m > 4 ? 5 : m - 1;
                    if (d == attach) return 0;
                    return strong ? (d == 4 ? 15 : 0) : 15;
                }
                case RK.LEVER:
                {
                    if ((m & 16) == 0) return 0;
                    int face = (m >> 2) & 3, attach = face == 0 ? 5 : face == 2 ? 4 : RS_OPP[m & 3];
                    return strong && d != attach ? 0 : 15;
                }
                case RK.BUTTON:
                {
                    if ((m & 8) == 0) return 0;
                    return strong && d != (((m & 3) + 2) & 3) ? 0 : 15;
                }
                case RK.PLATE: return (m & 1) != 0 && (!strong || d == 5) ? 15 : 0;
                case RK.BLOCK: return strong ? 0 : 15;
                case RK.REPEATER: return (m & 16) != 0 && d == (m & 3) ? 15 : 0;
                case RK.COMPARATOR: return d == (m & 3) ? (m >> 3) & 15 : 0;
                case RK.OBSERVER: return (m & 8) != 0 && d == RS_OPP[m & 7] ? 15 : 0;
                case RK.WIRE:
                {
                    if (strong || d == 4 || m == 0) return 0;
                    if (d == 5) return m & 15;
                    return WirePoints(x, y, z, d) ? m & 15 : 0;
                }
            }
            return 0;
        }

        // dust points into its neighbour in horizontal direction d: it links that way, links nowhere (a
        // dot reaches all sides), or links only the opposite way (a line runs on)
        bool WirePoints(int x, int y, int z, int d)
        {
            var L = Links(x, y, z);
            if (L[d] != 0) return true;
            int n = 0; for (int i = 0; i < 4; i++) if (L[i] != 0) n++;
            return n == 0 || (n == 1 && L[RS_OPP[d]] != 0);
        }

        int[] Links(int x, int y, int z)
        {
            var L = L4;
            int Get(int dx, int dy, int dz) => w.GetBlock(x + dx, y + dy, z + dz);
            int GetM(int dx, int dy, int dz) => w.GetMeta(x + dx, y + dy, z + dz);
            for (int d = 0; d < 4; d++) L[d] = WireLink(Get, GetM, d);
            return L;
        }

        // signal going into the block at (x, y, z) from its neighbours (strong: direct signals only)
        int Into(int x, int y, int z, bool strong, int skip)
        {
            int best = 0;
            for (int d = 0; d < 6 && best < 15; d++)
            {
                if (d == skip) continue;
                int s = Emit(x + RS_DX[d], y + RS_DY[d], z + RS_DZ[d], RS_OPP[d], strong);
                if (s > best) best = s;
            }
            return best;
        }

        // signal a component at (x, y, z) receives from its neighbour in direction d
        int From(int x, int y, int z, int d)
        {
            int nx = x + RS_DX[d], ny = y + RS_DY[d], nz = z + RS_DZ[d], id = w.GetBlock(nx, ny, nz);
            if (RS_KIND[id] != 0) return Emit(nx, ny, nz, RS_OPP[d], false);
            if (OPAQUE[id] != 0) return Into(nx, ny, nz, false, RS_OPP[d]);
            return 0;
        }

        // strongest signal a component at (x, y, z) receives (skip: a side that does not count)
        int PowerAt(int x, int y, int z, int skip = -1)
        {
            int best = 0;
            for (int d = 0; d < 6 && best < 15; d++) { if (d == skip) continue; int s = From(x, y, z, d); if (s > best) best = s; }
            return best;
        }

        // ---------------------------------------------------------------- dust networks
        // the dust connected to (x, y, z) settles at once: each takes the strongest input around it (not
        // counting other dust; conductive blocks only pass on strong signals), and the levels spread
        // along the dust dropping by one per block
        void SolveWire(int x, int y, int z)
        {
            var keys = new List<long>(); var pos = new List<int>(); var index = new Dictionary<long, int>();
            void Add(int a, int b, int c) { long k = RsKey(a, b, c); if (index.ContainsKey(k)) return; index[k] = keys.Count; keys.Add(k); pos.Add(a); pos.Add(b); pos.Add(c); }
            Add(x, y, z);
            var nb = new List<List<int>>();
            int[] DOWN = { 0, -1 }, UP = { 1 };
            for (int i = 0; i < keys.Count && keys.Count < 8192; i++)
            {
                int a = pos[i * 3], b = pos[i * 3 + 1], c = pos[i * 3 + 2]; var L = (int[])Links(a, b, c).Clone(); var list = new List<int>();
                for (int d = 0; d < 4; d++)
                {
                    if (L[d] == 0) continue;
                    int nx = a + RS_DX[d], nz = c + RS_DZ[d];
                    foreach (int dy in L[d] == 2 ? UP : DOWN) if (w.GetBlock(nx, b + dy, nz) == B.REDSTONE_WIRE) { Add(nx, b + dy, nz); list.Add(index[RsKey(nx, b + dy, nz)]); break; }
                }
                // dust a step up that links down to this one
                for (int d = 0; d < 4; d++)
                {
                    int nx = a + RS_DX[d], nz = c + RS_DZ[d];
                    if (w.GetBlock(nx, b + 1, nz) == B.REDSTONE_WIRE && OPAQUE[w.GetBlock(a, b + 1, c)] == 0) { Add(nx, b + 1, nz); list.Add(index[RsKey(nx, b + 1, nz)]); }
                }
                while (nb.Count <= i) nb.Add(null);
                nb[i] = list;
            }
            int n = keys.Count; var lvl = new int[n];
            for (int i = 0; i < n; i++)
            {
                solved.Add(keys[i]);
                int a = pos[i * 3], b = pos[i * 3 + 1], c = pos[i * 3 + 2];
                int s = 0;
                for (int d = 0; d < 6 && s < 15; d++)
                {
                    int ox = a + RS_DX[d], oy = b + RS_DY[d], oz = c + RS_DZ[d], id = w.GetBlock(ox, oy, oz);
                    int v = 0;
                    if (id == B.REDSTONE_WIRE) continue;
                    if (RS_KIND[id] != 0) v = Emit(ox, oy, oz, RS_OPP[d], false);
                    else if (OPAQUE[id] != 0) v = Into(ox, oy, oz, true, RS_OPP[d]);
                    if (v > s) s = v;
                }
                lvl[i] = s;
            }
            // spread: strongest first
            var buckets = new List<int>[16]; for (int l = 0; l <= 15; l++) buckets[l] = new List<int>();
            for (int i = 0; i < n; i++) if (lvl[i] > 0) buckets[lvl[i]].Add(i);
            for (int l = 15; l > 1; l--)
                for (int bi = 0; bi < buckets[l].Count; bi++)
                {
                    int i = buckets[l][bi];
                    if (lvl[i] != l) continue;
                    if (i >= nb.Count || nb[i] == null) continue;
                    foreach (int j in nb[i]) if (lvl[j] < l - 1) { lvl[j] = l - 1; buckets[l - 1].Add(j); }
                }
            for (int i = 0; i < n; i++)
            {
                int a = pos[i * 3], b = pos[i * 3 + 1], c = pos[i * 3 + 2];
                if ((w.GetMeta(a, b, c) & 15) == lvl[i]) continue;
                w.SetBlock(a, b, c, B.REDSTONE_WIRE, lvl[i], noUpdate: true, quiet: true);
                MarkAround(a, b, c, true);
            }
        }

        // ---------------------------------------------------------------- evaluation
        void Evaluate(int x, int y, int z)
        {
            int id = w.GetBlock(x, y, z), k = RS_KIND[id];
            if (k == 0) return;
            int m = w.GetMeta(x, y, z);
            switch (k)
            {
                case RK.WIRE: if (!solved.Contains(RsKey(x, y, z))) SolveWire(x, y, z); return;
                case RK.TORCH: case RK.TORCH_OFF:
                {
                    int a = m == 0 || m > 4 ? 5 : m - 1, ax = x + RS_DX[a], ay = y + RS_DY[a], az = z + RS_DZ[a];
                    bool want = !(OPAQUE[w.GetBlock(ax, ay, az)] != 0 && Into(ax, ay, az, false, RS_OPP[a]) > 0);
                    if (want != (k == RK.TORCH)) Later(x, y, z, 2);
                    return;
                }
                case RK.REPEATER:
                {
                    int f = m & 3, back = RS_OPP[f];
                    bool locked = false;
                    foreach (int s in new[] { (f + 1) & 3, (f + 3) & 3 })
                    {
                        int sx = x + RS_DX[s], sz = z + RS_DZ[s], sid = w.GetBlock(sx, y, sz);
                        if ((sid == B.REPEATER || sid == B.COMPARATOR) && Emit(sx, y, sz, RS_OPP[s], false) > 0) locked = true;
                    }
                    if (locked != ((m & 32) != 0)) w.SetBlock(x, y, z, id, (m & ~32) | (locked ? 32 : 0), noUpdate: true, quiet: true);
                    if (locked) return;
                    bool on = From(x, y, z, back) > 0;
                    if (on != ((m & 16) != 0)) Later(x, y, z, (((m >> 2) & 3) + 1) * 2);
                    return;
                }
                case RK.COMPARATOR: if (ComparatorOut(x, y, z, m) != ((m >> 3) & 15)) Later(x, y, z, 2); return;
                case RK.LAMP: if (PowerAt(x, y, z) > 0) { w.SetBlock(x, y, z, B.REDSTONE_LAMP_ON, 0); } return;
                case RK.LAMP_ON: if (PowerAt(x, y, z) == 0) Later(x, y, z, 4); return;
                case RK.PISTON: Piston(x, y, z, id, m); return;
                case RK.HEAD:
                {
                    int f = m & 7, bx = x - RS_DX[f], by = y - RS_DY[f], bz = z - RS_DZ[f], b = w.GetBlock(bx, by, bz);
                    if (RS_KIND[b] != RK.PISTON || (w.GetMeta(bx, by, bz) & 15) != (f | 8)) w.SetBlock(x, y, z, 0, 0);
                    return;
                }
                case RK.DOOR:
                {
                    int lower = ((m >> 2) & 1) != 0 ? y - 1 : y;
                    if (w.GetBlock(x, lower, z) != id) return;
                    bool p = PowerAt(x, lower, z) > 0 || PowerAt(x, lower + 1, z) > 0;
                    if (!Edge(x, lower, z, p)) return;
                    foreach (int yy in new[] { lower, lower + 1 }) if (w.GetBlock(x, yy, z) == id) { int mm = w.GetMeta(x, yy, z); w.SetBlock(x, yy, z, id, p ? mm | 8 : mm & ~8); }
                    sound?.Invoke("door", x, lower, z, p);
                    return;
                }
                case RK.TRAPDOOR: case RK.GATE:
                {
                    bool p = PowerAt(x, y, z) > 0; int bit = k == RK.GATE ? 4 : 8;
                    if (!Edge(x, y, z, p)) return;
                    w.SetBlock(x, y, z, id, p ? m | bit : m & ~bit);
                    sound?.Invoke("door", x, y, z, p);
                    return;
                }
                case RK.TNT:
                    if (PowerAt(x, y, z) > 0) { w.SetBlock(x, y, z, 0, 0); onTnt?.Invoke(x, y, z); }
                    return;
            }
        }

        // doors, trapdoors and gates follow a change of power, not the power itself (they stay usable by hand)
        bool Edge(int x, int y, int z, bool p)
        {
            long k = RsKey(x, y, z); bool was = powered.ContainsKey(k);
            if (p) powered[k] = true; else powered.Remove(k);
            return p != was;
        }

        int ComparatorOut(int x, int y, int z, int m)
        {
            int f = m & 3, back = RS_OPP[f], bx = x + RS_DX[back], bz = z + RS_DZ[back];
            int rear = From(x, y, z, back);
            if (container != null) { int c = container(bx, y, bz); if (c >= 0) rear = Math.Max(rear, c); }
            int side = 0;
            foreach (int s in new[] { (f + 1) & 3, (f + 3) & 3 })
            {
                int sx = x + RS_DX[s], sz = z + RS_DZ[s], sid = w.GetBlock(sx, y, sz);
                if (sid == B.REDSTONE_WIRE || sid == B.REPEATER || sid == B.COMPARATOR || sid == B.REDSTONE_BLOCK) side = Math.Max(side, Emit(sx, y, sz, RS_OPP[s], false));
            }
            return (m & 4) != 0 ? Math.Max(0, rear - side) : rear >= side ? rear : 0;
        }

        // a scheduled change comes due
        void Fire(int x, int y, int z)
        {
            int id = w.GetBlock(x, y, z), k = RS_KIND[id], m = w.GetMeta(x, y, z);
            switch (k)
            {
                case RK.TORCH: case RK.TORCH_OFF:
                {
                    int a = m == 0 || m > 4 ? 5 : m - 1, ax = x + RS_DX[a], ay = y + RS_DY[a], az = z + RS_DZ[a];
                    bool want = !(OPAQUE[w.GetBlock(ax, ay, az)] != 0 && Into(ax, ay, az, false, RS_OPP[a]) > 0);
                    if (want != (k == RK.TORCH)) w.SetBlock(x, y, z, want ? B.REDSTONE_TORCH : B.REDSTONE_TORCH_OFF, m);
                    return;
                }
                // a repeater makes the change it scheduled, then looks again (so short pulses last its delay)
                case RK.REPEATER: if ((m & 32) == 0) { w.SetBlock(x, y, z, id, m ^ 16); Mark(x, y, z); } return;
                case RK.COMPARATOR: w.SetBlock(x, y, z, id, (m & 7) | (ComparatorOut(x, y, z, m) << 3)); return;
                case RK.LAMP_ON: if (PowerAt(x, y, z) == 0) w.SetBlock(x, y, z, B.REDSTONE_LAMP, 0); return;
                case RK.OBSERVER:
                    w.SetBlock(x, y, z, id, m ^ 8);
                    if ((m & 8) == 0) Later(x, y, z, 2);       // the pulse ends two ticks later
                    return;
                case RK.BUTTON: if ((m & 8) != 0) { w.SetBlock(x, y, z, id, m & ~8); sound?.Invoke("click", x, y, z, false); } return;
                case RK.PLATE:
                    if ((m & 1) == 0) return;
                    if (StoodOn(x, y, z)) Later(x, y, z, 20);
                    else { w.SetBlock(x, y, z, id, m & ~1); sound?.Invoke("click", x, y, z, false); }
                    return;
            }
        }

        bool StoodOn(int x, int y, int z)
        {
            var c = entityCells?.Invoke();
            if (c == null) return false;
            for (int i = 0; i < c.Count; i += 3) if (c[i] == x && c[i + 1] == y && c[i + 2] == z) return true;
            return false;
        }

        // ---------------------------------------------------------------- pistons
        void Piston(int x, int y, int z, int id, int m)
        {
            int f = m & 7; bool ext = (m & 8) != 0;
            // powered from any side but the front, or (quasi-connectivity) at the block above it
            bool p = PowerAt(x, y, z, f) > 0;
            if (!p && f != 4) p = PowerAt(x, y + 1, z, 5) > 0;
            int hx = x + RS_DX[f], hy = y + RS_DY[f], hz = z + RS_DZ[f];
            if (ext && w.GetBlock(hx, hy, hz) != B.PISTON_HEAD) { w.SetBlock(x, y, z, id, f); return; }   // head gone
            if (p && !ext) Extend(x, y, z, id, f);
            else if (!p && ext) Retract(x, y, z, id, f);
        }

        void Extend(int x, int y, int z, int id, int f)
        {
            int dx = RS_DX[f], dy = RS_DY[f], dz = RS_DZ[f]; var line = new List<int>();
            int cx = x + dx, cy = y + dy, cz = z + dz; bool brk = false; int bx = 0, by = 0, bz = 0;
            for (; ; )
            {
                if (cy < WORLD_MIN_Y || cy >= WORLD_MAX_Y || !w.IsLoaded(cx, cz)) return;
                int b = w.GetBlock(cx, cy, cz);
                if (b == 0) break;
                if (RS_FRAGILE[b] != 0) { brk = true; bx = cx; by = cy; bz = cz; break; }
                if (RS_IMMOVABLE[b] != 0 || HARD[b] < 0 || (RS_KIND[b] == RK.PISTON && (w.GetMeta(cx, cy, cz) & 8) != 0)) return;
                if (line.Count >= 12 * 5) return;
                line.Add(cx); line.Add(cy); line.Add(cz); line.Add(b); line.Add(w.GetMeta(cx, cy, cz));
                cx += dx; cy += dy; cz += dz;
            }
            if (brk) w.BreakBlock(bx, by, bz, true);
            for (int i = line.Count - 5; i >= 0; i -= 5) w.SetBlock(line[i] + dx, line[i + 1] + dy, line[i + 2] + dz, line[i + 3], line[i + 4]);
            w.SetBlock(x + dx, y + dy, z + dz, B.PISTON_HEAD, f | (RS_STICK(id) ? 8 : 0));
            w.SetBlock(x, y, z, id, f | 8);
            sound?.Invoke("piston", x, y, z, true);
        }

        void Retract(int x, int y, int z, int id, int f)
        {
            int dx = RS_DX[f], dy = RS_DY[f], dz = RS_DZ[f];
            w.SetBlock(x, y, z, id, f);
            w.SetBlock(x + dx, y + dy, z + dz, 0, 0);
            if (RS_STICK(id))
            {
                int px = x + 2 * dx, py = y + 2 * dy, pz = z + 2 * dz, b = w.GetBlock(px, py, pz);
                if (b != 0 && RS_FRAGILE[b] == 0 && RS_IMMOVABLE[b] == 0 && HARD[b] >= 0 && !(RS_KIND[b] == RK.PISTON && (w.GetMeta(px, py, pz) & 8) != 0))
                {
                    int bm = w.GetMeta(px, py, pz);
                    w.SetBlock(px, py, pz, 0, 0);
                    w.SetBlock(x + dx, y + dy, z + dz, b, bm);
                }
            }
            sound?.Invoke("piston", x, y, z, false);
        }

        // ---------------------------------------------------------------- per game tick
        public void Tick()
        {
            long now = w.tick;
            solved.Clear();
            // due changes
            if (timers.Count > 0)
            {
                var due = new List<long>();
                foreach (var kv in timers) if (kv.Value <= now) due.Add(kv.Key);
                foreach (long k in due)
                {
                    timers.Remove(k);
                    FromKey(k, out int x, out int y, out int z);
                    if (w.IsLoaded(x, z)) Fire(x, y, z);
                }
            }
            // pressure plates under entities
            if (entityCells != null)
            {
                var c = entityCells();
                for (int i = 0; i < c.Count; i += 3)
                {
                    int x = c[i], y = c[i + 1], z = c[i + 2], id = w.GetBlock(x, y, z);
                    if (RS_KIND[id] != RK.PLATE) continue;
                    int m = w.GetMeta(x, y, z);
                    if ((m & 1) == 0) { w.SetBlock(x, y, z, id, m | 1); Later(x, y, z, 20); sound?.Invoke("click", x, y, z, true); }
                }
            }
            // everything marked settles (instant changes cascade within the tick)
            int n = 0;
            while (queue.Count > 0 && n < 65536)
            {
                var q = queue; queue = new List<int>(); queued.Clear();
                for (int i = 0; i < q.Count; i += 3, n++) if (w.IsLoaded(q[i], q[i + 2])) Evaluate(q[i], q[i + 1], q[i + 2]);
            }
        }

        static readonly Regex WOODEN = new Regex("PLANKS|WOOD|BAMBOO");

        // a button pressed by the player: on for 1 s (stone) or 1.5 s (wood)
        public void Press(int x, int y, int z)
        {
            int id = w.GetBlock(x, y, z), m = w.GetMeta(x, y, z);
            if ((m & 8) != 0) return;
            w.SetBlock(x, y, z, id, m | 8);
            long k = RsKey(x, y, z);
            timers.Remove(k);
            Later(x, y, z, WOODEN.IsMatch(B_KEY[id] ?? "") ? 30 : 20);
        }
    }
}
