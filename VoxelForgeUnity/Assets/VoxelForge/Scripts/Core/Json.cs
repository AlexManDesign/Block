// Voxel Forge — Unity port. Minimal JSON + dynamic object model (JS object semantics).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VoxelForge
{
    /// <summary>JS-like plain object: string keys, values are double/string/bool/null/JObj/List&lt;object&gt;.</summary>
    public sealed class JObj : Dictionary<string, object>
    {
        public JObj() : base() { }
        public JObj(int cap) : base(cap) { }
        public JObj(IDictionary<string, object> src) : base(src) { }

        public bool Has(string k) { return ContainsKey(k); }
        public object Get(string k) { object v; return TryGetValue(k, out v) ? v : null; }
        public string Str(string k, string def = null) { object v; if (TryGetValue(k, out v) && v is string s) return s; return def; }
        public double Num(string k, double def = 0) { object v; if (TryGetValue(k, out v)) { var d = Json.ToNum(v); if (!double.IsNaN(d)) return d; } return def; }
        public int Int(string k, int def = 0) { object v; if (TryGetValue(k, out v)) { var d = Json.ToNum(v); if (!double.IsNaN(d) && !double.IsInfinity(d)) return (int)(long)d; } return def; }
        public bool Bool(string k) { object v; return TryGetValue(k, out v) && Json.Truthy(v); }
        public bool IsNum(string k) { object v; return TryGetValue(k, out v) && (v is double || v is int || v is float || v is long); }
        public JObj Obj(string k) { object v; return TryGetValue(k, out v) ? v as JObj : null; }
        public List<object> Arr(string k) { object v; return TryGetValue(k, out v) ? v as List<object> : null; }
        public JObj Set(string k, object v) { this[k] = v; return this; }
        public JObj Clone() { return new JObj(this); }
    }

    public static class Json
    {
        public static bool Truthy(object v)
        {
            if (v == null) return false;
            if (v is bool b) return b;
            if (v is double d) return d != 0 && !double.IsNaN(d);
            if (v is int i) return i != 0;
            if (v is float f) return f != 0 && !float.IsNaN(f);
            if (v is long l) return l != 0;
            if (v is string s) return s.Length > 0;
            return true;
        }
        public static double ToNum(object v)
        {
            if (v is double d) return d;
            if (v is int i) return i;
            if (v is float f) return f;
            if (v is long l) return l;
            if (v is bool b) return b ? 1 : 0;
            if (v is string s) { double r; if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r; return double.NaN; }
            if (v == null) return 0;
            return double.NaN;
        }
        public static bool IsFinite(object v)
        {
            if (v is double d) return !double.IsNaN(d) && !double.IsInfinity(d);
            if (v is int || v is long) return true;
            if (v is float f) return !float.IsNaN(f) && !float.IsInfinity(f);
            return false;
        }

        // ---------------- parse ----------------
        public static object Parse(string s)
        {
            if (s == null) return null;
            int i = 0;
            var v = ParseValue(s, ref i);
            return v;
        }
        public static object TryParse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return Parse(s); } catch { return null; }
        }
        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }
        static object ParseValue(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("json eof");
            char c = s[i];
            if (c == '{')
            {
                i++; var o = new JObj();
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = ParseString(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("json :");
                    i++;
                    o[k] = ParseValue(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw new FormatException("json obj");
                }
            }
            if (c == '[')
            {
                i++; var a = new List<object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(ParseValue(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw new FormatException("json arr");
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            if (s[i] == '-' || s[i] == '+') i++;
            while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+')) i++;
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("json str");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("json unterminated string");
        }

        // ---------------- stringify ----------------
        public static string Stringify(object v)
        {
            var sb = new StringBuilder(256);
            Write(sb, v);
            return sb.ToString();
        }
        static void Write(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteStr(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double) { WriteNum(sb, (double)v); return; }
            if (v is float) { WriteNum(sb, (float)v); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is long) { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is uint) { sb.Append(((uint)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is byte) { sb.Append(((byte)v).ToString(CultureInfo.InvariantCulture)); return; }
            var o = v as IDictionary<string, object>;
            if (o != null)
            {
                sb.Append('{'); bool first = true;
                foreach (var kv in o)
                {
                    if (kv.Value is Delegate) continue;
                    if (!first) sb.Append(','); first = false;
                    WriteStr(sb, kv.Key); sb.Append(':'); Write(sb, kv.Value);
                }
                sb.Append('}'); return;
            }
            var a = v as System.Collections.IList;
            if (a != null)
            {
                sb.Append('['); for (int k = 0; k < a.Count; k++) { if (k > 0) sb.Append(','); Write(sb, a[k]); }
                sb.Append(']'); return;
            }
            var w = v as IJsonWritable;
            if (w != null) { Write(sb, w.ToJson()); return; }
            WriteStr(sb, v.ToString());
        }
        static void WriteNum(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            if (d == Math.Floor(d) && Math.Abs(d) < 9e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }
        static void WriteStr(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    public interface IJsonWritable { object ToJson(); }
}
