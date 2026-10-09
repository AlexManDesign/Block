// Voxel Forge — Unity port. Browser runtime equivalents: localStorage, timers, performance.now, Math.random, int math.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace VoxelForge
{
    /// <summary>localStorage equivalent: one file per key under Application.persistentDataPath/vf_storage.</summary>
    public static class LocalStorage
    {
        static string _dir;
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        static readonly HashSet<string> missing = new HashSet<string>();
        public static string Dir
        {
            get
            {
                if (_dir == null)
                {
                    string root;
                    try { root = UnityEngine.Application.persistentDataPath; } catch { root = Path.GetTempPath(); }
                    _dir = Path.Combine(root, "vf_storage");
                    try { Directory.CreateDirectory(_dir); } catch { }
                }
                return _dir;
            }
        }
        static string FileFor(string key)
        {
            var sb = new StringBuilder(key.Length + 8);
            foreach (char c in key) sb.Append((char.IsLetterOrDigit(c) || c == '_' || c == '-') && c < 128 ? c : '_');
            return Path.Combine(Dir, sb.ToString() + "_" + ((uint)key.GetHashCode()).ToString("x8") + ".json");
        }
        public static string getItem(string key)
        {
            if (key == null) return null;
            lock (cache)
            {
                string v;
                if (cache.TryGetValue(key, out v)) return v;
                if (missing.Contains(key)) return null;
                try
                {
                    var f = FileFor(key);
                    if (File.Exists(f)) { v = File.ReadAllText(f, Encoding.UTF8); cache[key] = v; return v; }
                }
                catch { }
                missing.Add(key);
                return null;
            }
        }
        public static void setItem(string key, string value)
        {
            if (key == null) return;
            lock (cache)
            {
                cache[key] = value; missing.Remove(key);
                try
                {
                    var f = FileFor(key); var tmp = f + ".tmp";
                    File.WriteAllText(tmp, value ?? "", Encoding.UTF8);
                    if (File.Exists(f)) File.Delete(f);
                    File.Move(tmp, f);
                }
                catch (Exception e) { UnityEngine.Debug.LogWarning("localStorage write failed: " + e.Message); }
            }
        }
        public static void removeItem(string key)
        {
            if (key == null) return;
            lock (cache)
            {
                cache.Remove(key); missing.Add(key);
                try { var f = FileFor(key); if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }
    }

    /// <summary>setTimeout/clearTimeout equivalent, ticked once per frame on the main thread.</summary>
    public static class Timers
    {
        struct T { public int id; public double at; public Action fn; }
        static readonly List<T> list = new List<T>();
        static int nextId = 1;
        public static int setTimeout(Action fn, double ms)
        {
            int id = nextId++;
            list.Add(new T { id = id, at = JS.now() + Math.Max(0, ms), fn = fn });
            return id;
        }
        public static void clearTimeout(int id)
        {
            if (id == 0) return;
            for (int i = list.Count - 1; i >= 0; i--) if (list[i].id == id) list.RemoveAt(i);
        }
        public static void Tick()
        {
            if (list.Count == 0) return;
            double now = JS.now();
            for (int pass = 0; pass < 4; pass++)
            {
                bool any = false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].at <= now)
                    {
                        var t = list[i]; list.RemoveAt(i); i--; any = true;
                        try { t.fn(); } catch (Exception e) { UnityEngine.Debug.LogException(e); }
                    }
                }
                if (!any) break;
            }
        }
        public static void Clear() { list.Clear(); }
    }

    /// <summary>JS numeric semantics helpers.</summary>
    public static class JS
    {
        static readonly Stopwatch sw = Stopwatch.StartNew();
        /// <summary>performance.now() in milliseconds.</summary>
        public static double now() { return sw.Elapsed.TotalMilliseconds; }
        /// <summary>Date.now() in ms since epoch.</summary>
        public static double dateNow() { return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

        [ThreadStatic] static Random _rng;
        static int seedCounter = Environment.TickCount;
        static Random Rng { get { if (_rng == null) _rng = new Random(System.Threading.Interlocked.Increment(ref seedCounter) * 7919 ^ Environment.TickCount); return _rng; } }
        /// <summary>Math.random()</summary>
        public static double random() { return Rng.NextDouble(); }

        public static int imul(int a, int b) { unchecked { return a * b; } }
        /// <summary>x &gt;&gt;&gt; n (result as int, like JS when re-used in int ops)</summary>
        public static int ushr(int x, int n) { return (int)((uint)x >> n); }
        /// <summary>x &gt;&gt;&gt; 0 as double (unsigned)</summary>
        public static double u32(int x) { return (uint)x; }
        public static int floor(double v) { return (int)Math.Floor(v); }
        /// <summary>JS "x | 0" for doubles (ToInt32).</summary>
        public static int toInt32(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            if (v >= -2147483648.0 && v <= 2147483647.0) return (int)v;
            double m = Math.Truncate(v) % 4294967296.0; if (m < 0) m += 4294967296.0;
            return unchecked((int)(uint)m);
        }
        public static bool isFinite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        public static double hypot(double a, double b) { return Math.Sqrt(a * a + b * b); }
        public static double hypot(double a, double b, double c) { return Math.Sqrt(a * a + b * b + c * c); }
        public static double clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        public static int clampi(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
        /// <summary>Math.round (JS rounds .5 up)</summary>
        public static double round(double v) { return Math.Floor(v + 0.5); }
        public static double sign(double v) { return v > 0 ? 1 : v < 0 ? -1 : 0; }
        /// <summary>JS % on doubles (truncated remainder) is C# % — kept for clarity.</summary>
        public static double mod(double a, double b) { return a % b; }
        public static string base36(long v)
        {
            const string d = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (v == 0) return "0";
            var sb = new StringBuilder(); bool neg = v < 0; if (neg) v = -v;
            while (v > 0) { sb.Insert(0, d[(int)(v % 36)]); v /= 36; }
            if (neg) sb.Insert(0, '-');
            return sb.ToString();
        }
        public static string ToStr(double v)
        {
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }
        public static string Fixed(double v, int digits) { return v.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture); }
    }
}
