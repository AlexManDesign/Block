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
        // Keys whose file exists but could not be read this session: never overwritten, so a transient lock
        // (antivirus / cloud sync) cannot make the game regenerate and clobber the real save.
        static readonly HashSet<string> readFailed = new HashSet<string>();
        public static string getItem(string key)
        {
            if (key == null) return null;
            lock (cache)
            {
                string v;
                if (cache.TryGetValue(key, out v)) return v;
                if (missing.Contains(key)) return null;
                var f = FileFor(key); var tmp = f + ".tmp"; Exception err = null;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        // crash between "delete old" and "move tmp" (older builds / fallback path): recover the tmp copy
                        if (!File.Exists(f) && File.Exists(tmp)) File.Move(tmp, f);
                        if (!File.Exists(f)) { missing.Add(key); readFailed.Remove(key); return null; }
                        v = File.ReadAllText(f, Encoding.UTF8); cache[key] = v; readFailed.Remove(key); return v;
                    }
                    catch (Exception e) { err = e; if (attempt < 3) System.Threading.Thread.Sleep(10 + attempt * 20); }
                }
                readFailed.Add(key);
                UnityEngine.Debug.LogError("localStorage read failed for '" + key + "' (writes to it are blocked this session): " + (err != null ? err.Message : ""));
                return null;
            }
        }
        public static void setItem(string key, string value)
        {
            if (key == null) return;
            lock (cache)
            {
                var f = FileFor(key); var tmp = f + ".tmp";
                if (readFailed.Contains(key))
                {
                    bool exists = true; try { exists = File.Exists(f); } catch { }
                    if (exists) { UnityEngine.Debug.LogWarning("localStorage write skipped for unreadable key '" + key + "'"); return; }
                    readFailed.Remove(key);
                }
                cache[key] = value; missing.Remove(key);
                try
                {
                    File.WriteAllText(tmp, value ?? "", Encoding.UTF8);
                    if (!File.Exists(f)) File.Move(tmp, f);
                    else
                    {
                        try { File.Replace(tmp, f, null); }
                        catch (Exception) { if (File.Exists(tmp)) { File.Delete(f); File.Move(tmp, f); } }
                    }
                }
                catch (Exception e) { UnityEngine.Debug.LogWarning("localStorage write failed: " + e.Message); }
            }
        }
        public static void removeItem(string key)
        {
            if (key == null) return;
            lock (cache)
            {
                cache.Remove(key); missing.Add(key); readFailed.Remove(key);
                try { var f = FileFor(key); if (File.Exists(f)) File.Delete(f); if (File.Exists(f + ".tmp")) File.Delete(f + ".tmp"); } catch { }
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
        /// <summary>JS String(v) for JSON-model values.</summary>
        public static string ToStr(object v)
        {
            if (v == null) return "null";
            if (v is string) return (string)v;
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is double || v is int || v is float || v is long) return ToStr(Json.ToNum(v));
            return v.ToString();
        }
        /// <summary>JS Number.prototype.toString() (shortest round-trip digits, JS fixed/exponent layout).</summary>
        public static string ToStr(double v)
        {
            if (v == Math.Floor(v) && Math.Abs(v) < 9007199254740992.0) return ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (double.IsNaN(v)) return "NaN";
            if (double.IsInfinity(v)) return v > 0 ? "Infinity" : "-Infinity";
            if (v < 0) return "-" + ToStr(-v);
            int exp; string s = shortestDigits(v, out exp);
            int k = s.Length, n = exp + 1;
            if (k <= n && n <= 21) return s + new string('0', n - k);
            if (0 < n && n <= 21) return s.Substring(0, n) + "." + s.Substring(n);
            if (-6 < n && n <= 0) return "0." + new string('0', -n) + s;
            int e = n - 1; string es = (e < 0 ? "e-" : "e+") + Math.Abs(e).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return k == 1 ? s + es : s.Substring(0, 1) + "." + s.Substring(1) + es;
        }
        /// <summary>Shortest decimal digits (no leading/trailing zeros) that round-trip to v (v finite, &gt; 0); exp = decimal exponent of the first digit.
        /// Exact: a candidate is accepted when it lies inside v's rounding interval (computed with big integers, no double.Parse).</summary>
        static string shortestDigits(double v, out int exp)
        {
            if (v >= 2.2250738585072014e-308)
            {
                // fast path: the 15-digit rounding (trimmed) is the shortest form whenever it round-trips, and N * 10^q with
                // N < 2^53, |q| <= 22 is a single correctly rounded IEEE operation, so the check below is exact.
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                string str = v.ToString("E14", ci); int ei = str.IndexOf('E');
                int e10 = int.Parse(str.Substring(ei + 1), ci);
                var sb = new StringBuilder(16); long N = 0;
                for (int i = 0; i < ei; i++) if (str[i] >= '0' && str[i] <= '9') sb.Append(str[i]);
                int t = sb.Length; while (t > 1 && sb[t - 1] == '0') t--;
                for (int i = 0; i < t; i++) N = N * 10 + (sb[i] - '0');
                int q = e10 - (t - 1);
                if (q >= -22 && q <= 22 && (q >= 0 ? N * POW10_22[q] : N / POW10_22[-q]) == v) { exp = e10; return sb.ToString(0, t); }
            }
            long bits = BitConverter.DoubleToInt64Bits(v); int be = (int)((bits >> 52) & 0x7FF); long mant = bits & 0xFFFFFFFFFFFFFL;
            bool lowerCloser = mant == 0 && be > 1;
            if (be == 0) be = 1; else mant |= 1L << 52;
            bool even = (mant & 1) == 0;
            int E = be - 1075 - 2, S = E < 0 ? -E : 0; // values below are in units of 2^E, scaled to integers in units of 10^-S
            string H = bigDec(4 * mant + 2, E + S, S), V = bigDec(4 * mant, E + S, S), L = bigDec(lowerCloser ? 4 * mant - 1 : 4 * mant - 2, E + S, S);
            int len = H.Length; V = V.PadLeft(len, '0'); L = L.PadLeft(len, '0');
            int lead = 0; while (lead < len - 1 && V[lead] == '0') lead++;
            for (int k = 1; ; k++)
            {
                int cut = lead + k; string D;
                if (cut >= len) D = V;
                else
                {
                    var c = V.Substring(0, cut).ToCharArray(); bool up = V[cut] > '5';
                    if (V[cut] == '5') { up = false; for (int q = cut + 1; q < len; q++) if (V[q] != '0') { up = true; break; } if (!up) up = ((c[cut - 1] - '0') & 1) == 1; }
                    string pre = new string(c);
                    if (up) { int q = cut - 1; while (q >= 0 && c[q] == '9') { c[q] = '0'; q--; } if (q >= 0) { c[q]++; pre = new string(c); } else pre = "1" + new string(c); }
                    D = pre + new string('0', len - cut);
                }
                int cl = bigCmp(L, D), ch = bigCmp(D, H);
                if (cut >= len || ((even ? cl <= 0 : cl < 0) && (even ? ch <= 0 : ch < 0)))
                {
                    int dl = 0; while (dl < D.Length - 1 && D[dl] == '0') dl++;
                    int end = D.Length; while (end > dl + 1 && D[end - 1] == '0') end--;
                    exp = (D.Length - 1 - dl) - S;
                    return D.Substring(dl, end - dl);
                }
            }
        }
        static int bigCmp(string a, string b)
        {
            int ia = 0, ib = 0; while (ia < a.Length - 1 && a[ia] == '0') ia++; while (ib < b.Length - 1 && b[ib] == '0') ib++;
            int la = a.Length - ia, lb = b.Length - ib; if (la != lb) return la < lb ? -1 : 1;
            return Math.Sign(string.CompareOrdinal(a, ia, b, ib, la));
        }
        /// <summary>m * 2^p2 * 5^p5 (m &gt;= 0, p2, p5 &gt;= 0) as a decimal integer string.</summary>
        static string bigDec(long m, int p2, int p5)
        {
            var limbs = new List<uint> { (uint)(m % 1000000000L), (uint)(m / 1000000000L % 1000000000L), (uint)(m / 1000000000000000000L) };
            for (int i = 0; i < p2; ) { int sh = Math.Min(29, p2 - i); bigMul(limbs, 1u << sh); i += sh; }
            for (int i = 0; i < p5; ) { int c = Math.Min(13, p5 - i); uint f5 = 1; for (int j = 0; j < c; j++) f5 *= 5; bigMul(limbs, f5); i += c; }
            var sb = new StringBuilder();
            int top = limbs.Count - 1; while (top > 0 && limbs[top] == 0) top--;
            sb.Append(limbs[top].ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int i = top - 1; i >= 0; i--) sb.Append(limbs[i].ToString("D9", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }
        static readonly double[] POW10 = { 1, 10, 100, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10 };
        static readonly double[] POW10_22 = { 1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22 };
        /// <summary>JS Number.prototype.toFixed(digits) (exact value, ties round up, keeps '-' for small negatives).</summary>
        public static string Fixed(double v, int digits)
        {
            if (double.IsNaN(v)) return "NaN";
            if (digits < 0) digits = 0; if (digits > 100) digits = 100;
            if (Math.Abs(v) >= 1e21 || double.IsInfinity(v)) return ToStr(v);
            bool neg = v < 0; double x = neg ? -v : v; string m = null;
            if (digits <= 10)
            {
                double y = x * POW10[digits];
                if (y < 1e9) { double fl = Math.Floor(y), fr = y - fl; if (Math.Abs(fr - 0.5) > 1e-6) m = ((long)(fr > 0.5 ? fl + 1 : fl)).ToString(System.Globalization.CultureInfo.InvariantCulture); }
            }
            if (m == null) m = exactRoundedDigits(x, digits);
            if (digits != 0)
            {
                int k = m.Length;
                if (k <= digits) { m = new string('0', digits + 1 - k) + m; k = digits + 1; }
                m = m.Substring(0, k - digits) + "." + m.Substring(k - digits);
            }
            return neg ? "-" + m : m;
        }
        /// <summary>round-half-up(x * 10^f) as a decimal integer string, from the exact binary value of x (x finite, 0 &lt;= x &lt; 1e21).</summary>
        static string exactRoundedDigits(double x, int f)
        {
            if (x == 0) return "0";
            long bits = BitConverter.DoubleToInt64Bits(x); int be = (int)((bits >> 52) & 0x7FF); long mant = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) be = 1; else mant |= 1L << 52;
            int e = be - 1075; // x = mant * 2^e
            int scale = e < 0 ? -e : 0;
            string d = e < 0 ? bigDec(mant, 0, scale) : bigDec(mant, e, 0); // x = d * 10^-scale
            if (scale <= f) return (d + new string('0', f - scale)).TrimStart('0').PadLeft(1, '0');
            int keep = d.Length - (scale - f); // digits of the integer part of x*10^f
            string ip = keep > 0 ? d.Substring(0, keep) : "0";
            char next = keep >= 0 ? d[keep] : '0';
            if (next >= '5')
            {
                var c = ip.ToCharArray(); int i = c.Length - 1;
                while (i >= 0 && c[i] == '9') { c[i] = '0'; i--; }
                if (i >= 0) { c[i]++; ip = new string(c); } else ip = "1" + new string(c);
            }
            ip = ip.TrimStart('0'); return ip.Length == 0 ? "0" : ip;
        }
        static void bigMul(List<uint> a, uint m)
        {
            ulong carry = 0;
            for (int i = 0; i < a.Count; i++) { ulong p = (ulong)a[i] * m + carry; a[i] = (uint)(p % 1000000000UL); carry = p / 1000000000UL; }
            while (carry > 0) { a.Add((uint)(carry % 1000000000UL)); carry /= 1000000000UL; }
        }
    }
}
