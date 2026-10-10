// Checks VoxelForge.JsMath bit-for-bit against vectors produced by gen.js (stdin or file), and benchmarks it.
//   node gen.js | mono MathParity.exe            -> per-function mismatch counts for JsMath and (for reference) System.Math
//   mono MathParity.exe vectors.txt
//   mono MathParity.exe --bench                  -> ns/call JsMath vs System.Math on worldgen-like arguments
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VoxelForge;

public static class MathParity
{
    static readonly string[] NAMES = { "round", "sin", "cos", "tan", "log", "log10", "atan", "pow", "atan2", "hypot2", "hypot3" };
    static double J(int f, double a, double b, double c)
    {
        switch (f)
        {
            case 0: return JsMath.round(a); case 1: return JsMath.sin(a); case 2: return JsMath.cos(a); case 3: return JsMath.tan(a); case 4: return JsMath.log(a);
            case 5: return JsMath.log10(a); case 6: return JsMath.atan(a); case 7: return JsMath.pow(a, b); case 8: return JsMath.atan2(a, b);
            case 9: return JsMath.hypot(a, b); default: return JsMath.hypot(a, b, c);
        }
    }
    static double M(int f, double a, double b, double c)
    {
        switch (f)
        {
            case 0: return Math.Floor(a + 0.5); case 1: return Math.Sin(a); case 2: return Math.Cos(a); case 3: return Math.Tan(a); case 4: return Math.Log(a);
            case 5: return Math.Log10(a); case 6: return Math.Atan(a); case 7: return Math.Pow(a, b); case 8: return Math.Atan2(a, b);
            case 9: return Math.Sqrt(a * a + b * b); default: return Math.Sqrt(a * a + b * b + c * c);
        }
    }
    static bool Same(double x, long eb) { double e = BitConverter.Int64BitsToDouble(eb); if (double.IsNaN(e)) return double.IsNaN(x); return BitConverter.DoubleToInt64Bits(x) == eb; }
    static string H(double v) { return BitConverter.DoubleToInt64Bits(v).ToString("x16"); }

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--bench") { Bench(); return 0; }
        Stream input = args.Length > 0 ? (Stream)File.OpenRead(args[0]) : Console.OpenStandardInput();
        var rd = new BufferedStream(input, 1 << 20);
        long[] total = new long[NAMES.Length], bad = new long[NAMES.Length], badSys = new long[NAMES.Length];
        int shown = 0; var nm = new System.Text.StringBuilder(); var vals = new long[4];
        var sw = Stopwatch.StartNew();
        for (;;)
        {
            int ch = rd.ReadByte(); if (ch < 0) break; if (ch == '\n' || ch == '\r') continue;
            nm.Length = 0; while (ch != ' ' && ch >= 0) { nm.Append((char)ch); ch = rd.ReadByte(); }
            int nv = 0;
            while (ch == ' ')
            {
                long v = 0; ch = rd.ReadByte();
                while (ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'f') { v = (v << 4) | (uint)(ch <= '9' ? ch - '0' : ch - 'a' + 10); ch = rd.ReadByte(); }
                if (nv < 4) vals[nv] = v; nv++;
            }
            int f = Array.IndexOf(NAMES, nm.ToString()); if (f < 0 || nv < 2) { Console.Error.WriteLine("bad line: " + nm); return 2; }
            double a = BitConverter.Int64BitsToDouble(vals[0]), b = nv > 2 ? BitConverter.Int64BitsToDouble(vals[1]) : 0, c = nv > 3 ? BitConverter.Int64BitsToDouble(vals[2]) : 0;
            long exp = vals[nv - 1];
            total[f]++;
            double r = J(f, a, b, c);
            if (!Same(r, exp)) { bad[f]++; if (shown++ < 40) Console.WriteLine("MISMATCH " + NAMES[f] + "(" + H(a) + (nv > 2 ? "," + H(b) : "") + (nv > 3 ? "," + H(c) : "") + ") = " + H(r) + " expected " + BitConverter.Int64BitsToDouble(exp).ToString("R") + " [" + exp.ToString("x16") + "] got " + r.ToString("R")); }
            if (!Same(M(f, a, b, c), exp)) badSys[f]++;
        }
        long all = 0, allBad = 0;
        Console.WriteLine("fn        samples     JsMath-mismatch  System.Math-mismatch (reference only)");
        for (int f = 0; f < NAMES.Length; f++)
        {
            if (total[f] == 0) continue; all += total[f]; allBad += bad[f];
            Console.WriteLine(NAMES[f].PadRight(8) + total[f].ToString().PadLeft(10) + bad[f].ToString().PadLeft(18) + badSys[f].ToString().PadLeft(20));
        }
        Console.WriteLine("total " + all + " samples, JsMath mismatches: " + allBad + " (" + sw.Elapsed.TotalSeconds.ToString("F1") + " s)");
        Console.WriteLine(allBad == 0 && all > 0 ? "math parity: OK" : "math parity: FAIL");
        return allBad == 0 && all > 0 ? 0 : 1;
    }

    // ---- benchmark: identical argument arrays for both, worldgen-shaped ranges, best of several runs
    static double sink;
    delegate double U1(double a); delegate double U2(double a, double b);
    static void Bench()
    {
        const int L = 1 << 16; var rng = new Random(7);
        double[] ang = new double[L], unit = new double[L], mount = new double[L], big = new double[L], cnt = new double[L], gen = new double[L], ten = new double[L];
        for (int i = 0; i < L; i++)
        {
            ang[i] = rng.NextDouble() * Math.PI * 2 + (rng.NextDouble() - 0.5) * 20; unit[i] = rng.NextDouble(); mount[i] = rng.NextDouble() * 1.5;
            big[i] = (rng.NextDouble() - 0.5) * 2e9; cnt[i] = rng.Next(1, 100000) - 0.01; gen[i] = (rng.NextDouble() - 0.5) * 200; ten[i] = rng.Next(-5, 6);
        }
        Console.WriteLine("ns/call (best of 7, " + L + " args x 40 passes)          JsMath     System.Math");
        Row("(delegate-call overhead, included in all rows)", ang, x => x, x => x);
        Row("sin  ravine/ring angles", ang, x => JsMath.sin(x), x => Math.Sin(x));
        Row("cos  ravine/ring angles", ang, x => JsMath.cos(x), x => Math.Cos(x));
        Row("sin  |x|~1e9 (Payne-Hanek)", big, x => JsMath.sin(x), x => Math.Sin(x));
        Row("tan  ravine angles", ang, x => JsMath.tan(x), x => Math.Tan(x));
        Row("log10 n-0.01", cnt, x => JsMath.log10(x), x => Math.Log10(x));
        Row("log  n-0.01", cnt, x => JsMath.log(x), x => Math.Log(x));
        Row("atan +-100", gen, x => JsMath.atan(x), x => Math.Atan(x));
        Row2("pow  (pv*.5+.5, 1.4)", unit, 1.4, (x, y) => JsMath.pow(x, y), (x, y) => Math.Pow(x, y));
        Row2("pow  (mount, 1.15)", mount, 1.15, (x, y) => JsMath.pow(x, y), (x, y) => Math.Pow(x, y));
        Row2r("pow  (10, int)", ten, (x, y) => JsMath.pow(x, y), (x, y) => Math.Pow(x, y));
        Row2a("atan2 +-100", gen, unit, (x, y) => JsMath.atan2(x, y), (x, y) => Math.Atan2(x, y));
        Row2a("hypot2 +-100", gen, unit, (x, y) => JsMath.hypot(x, y), (x, y) => Math.Sqrt(x * x + y * y));
    }
    static double Time(Func<double> pass, int calls)
    {
        double best = double.MaxValue;
        for (int r = 0; r < 8; r++) { var sw = Stopwatch.StartNew(); double s = 0; for (int p = 0; p < 40; p++) s += pass(); sw.Stop(); sink += s; if (r > 0) best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / (calls * 40.0)); }
        return best;
    }
    static void Print(string n, double j, double m) { Console.WriteLine(n.PadRight(48) + j.ToString("F2").PadLeft(8) + m.ToString("F2").PadLeft(14)); }
    static void Row(string n, double[] a, U1 fj, U1 fm)
    {
        Print(n, Time(() => { double s = 0; for (int i = 0; i < a.Length; i++) s += fj(a[i]); return s; }, a.Length),
                 Time(() => { double s = 0; for (int i = 0; i < a.Length; i++) s += fm(a[i]); return s; }, a.Length));
    }
    static void Row2(string n, double[] a, double y, U2 fj, U2 fm)
    {
        Print(n, Time(() => { double s = 0; for (int i = 0; i < a.Length; i++) s += fj(a[i], y); return s; }, a.Length),
                 Time(() => { double s = 0; for (int i = 0; i < a.Length; i++) s += fm(a[i], y); return s; }, a.Length));
    }
    static void Row2r(string n, double[] e, U2 fj, U2 fm)
    {
        Print(n, Time(() => { double s = 0; for (int i = 0; i < e.Length; i++) s += fj(10, e[i]); return s; }, e.Length),
                 Time(() => { double s = 0; for (int i = 0; i < e.Length; i++) s += fm(10, e[i]); return s; }, e.Length));
    }
    static void Row2a(string n, double[] a, double[] b, U2 fj, U2 fm)
    {
        Print(n, Time(() => { double s = 0; for (int i = 0; i < a.Length; i++) s += fj(a[i], b[i]); return s; }, a.Length),
                 Time(() => { double s = 0; for (int i = 0; i < a.Length; i++) s += fm(a[i], b[i]); return s; }, a.Length));
    }
}
