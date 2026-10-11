// Voxel Forge — Unity port. Per-frame phase timings of the main thread and a "worst frame of the last 2 s" record,
// shown as one line of the stats overlay so a hitch can be attributed to a phase in a real player/Editor.
using System;
using System.Text;

namespace VoxelForge
{
    public static partial class VF
    {
        public const int PERF_DRAIN = 0, PERF_CONTROL = 1, PERF_SIM = 2, PERF_GEN = 3, PERF_LIGHT = 4, PERF_MESH = 5, PERF_EDIT = 6,
            PERF_SUBMIT = 7, PERF_CLEANUP = 8, PERF_REGION = 9, PERF_RENDER = 10, PERF_GUI = 11, PERF_COUNT = 12;
        static readonly string[] PERF_NAMES = { "drain", "ввод", "сим", "ген", "свет", "меш", "правки", "очередь", "выгрузка", "регионы", "рендер", "UI" };
        /// <summary>Milliseconds spent per phase in the current frame (reset at frame start).</summary>
        public static readonly double[] perfFrame = new double[PERF_COUNT];
        static readonly double[] perfPeak = new double[PERF_COUNT];
        public static double perfFrameTotal, perfPeakTotal, perfPeakAt = -1e9;
        public static int perfGcCount;
        static int perfGcAtFrameStart, perfPeakGc;
        public static string perfPeakText = "";

        public static void perfBeginFrame()
        {
            Array.Clear(perfFrame, 0, PERF_COUNT); Array.Clear(perfAllocFrame, 0, PERF_COUNT);
            perfGcAtFrameStart = GC.CollectionCount(0);
        }
        public static void perfAdd(int phase, double ms) { perfFrame[phase] += ms; }
        /// <summary>Main-thread bytes allocated per phase in the current frame (only counted in the headless perf build,
        /// VF_PERF_ALLOC; zero in Unity).</summary>
        public static readonly long[] perfAllocFrame = new long[PERF_COUNT];
        public struct PerfMark { public double t; public long a; }
        public static PerfMark perfMark()
        {
            PerfMark m; m.t = JS.now();
#if VF_PERF_ALLOC
            m.a = GC.GetAllocatedBytesForCurrentThread();
#else
            m.a = 0;
#endif
            return m;
        }
        public static void perfEnd(int phase, PerfMark m)
        {
            perfFrame[phase] += JS.now() - m.t;
#if VF_PERF_ALLOC
            perfAllocFrame[phase] += GC.GetAllocatedBytesForCurrentThread() - m.a;
#endif
        }
        /// <summary>Per-call breakdown of the simulation phase (headless perf build only).</summary>
        public static readonly double[] perfSimMs = new double[12]; public static readonly long[] perfSimAlloc = new long[12];
        public static void perfSub(int i, PerfMark m)
        {
            perfSimMs[i] += JS.now() - m.t;
#if VF_PERF_ALLOC
            perfSimAlloc[i] += GC.GetAllocatedBytesForCurrentThread() - m.a;
#endif
        }
        /// <summary>Closes the frame: keeps the worst frame of the last 2 s (by measured main-thread total).</summary>
        public static void perfEndFrame(double now, double totalMs)
        {
            perfFrameTotal = totalMs;
            int gc = GC.CollectionCount(0) - perfGcAtFrameStart; perfGcCount += gc;
            if (totalMs >= perfPeakTotal || now - perfPeakAt > 2000)
            {
                perfPeakTotal = totalMs; perfPeakAt = now; perfPeakGc = gc;
                Array.Copy(perfFrame, perfPeak, PERF_COUNT);
            }
        }
        /// <summary>"пик 2с 182 мс: регионы 120 · свет 31 · меш 18 · GC 1" — the three largest phases of the worst frame.</summary>
        static string perfPeakLine()
        {
            var sb = new StringBuilder("пик 2с ").Append(JS.Fixed(perfPeakTotal, 1)).Append(" мс:");
            var used = new bool[PERF_COUNT];
            for (int k = 0; k < 3; k++)
            {
                int best = -1; for (int i = 0; i < PERF_COUNT; i++) if (!used[i] && (best < 0 || perfPeak[i] > perfPeak[best])) best = i;
                if (best < 0 || perfPeak[best] < 0.5) break;
                used[best] = true; sb.Append(k == 0 ? " " : " · ").Append(PERF_NAMES[best]).Append(' ').Append(JS.Fixed(perfPeak[best], 1));
            }
            if (perfPeakGc > 0) sb.Append(" · GC ").Append(perfPeakGc);
            return sb.ToString();
        }
    }
}
