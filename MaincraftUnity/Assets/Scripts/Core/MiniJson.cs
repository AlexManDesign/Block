using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Maincraft
{
    /// <summary>
    /// Small JSON reader / writer (objects -> Dictionary&lt;string, object&gt;, arrays -> List&lt;object&gt;,
    /// numbers -> double). Used for the structure templates, world saves and settings.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = ParseValue(s, ref i);
            return v;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        static object ParseValue(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("json: unexpected end");
            char c = s[i];
            switch (c)
            {
                case '{':
                {
                    var d = new Dictionary<string, object>();
                    i++; Ws(s, ref i);
                    if (s[i] == '}') { i++; return d; }
                    while (true)
                    {
                        Ws(s, ref i);
                        string k = ParseString(s, ref i);
                        Ws(s, ref i);
                        if (s[i] != ':') throw new FormatException("json: ':' expected at " + i);
                        i++;
                        d[k] = ParseValue(s, ref i);
                        Ws(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == '}') { i++; return d; }
                        throw new FormatException("json: ',' or '}' expected at " + i);
                    }
                }
                case '[':
                {
                    var l = new List<object>();
                    i++; Ws(s, ref i);
                    if (s[i] == ']') { i++; return l; }
                    while (true)
                    {
                        l.Add(ParseValue(s, ref i));
                        Ws(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == ']') { i++; return l; }
                        throw new FormatException("json: ',' or ']' expected at " + i);
                    }
                }
                case '"': return ParseString(s, ref i);
                case 't': i += 4; return true;
                case 'f': i += 5; return false;
                case 'n': i += 4; return null;
                default:
                {
                    int st = i;
                    while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                    return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
                }
            }
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("json: string expected at " + i);
            i++;
            int st = i;
            // fast path: no escapes
            while (i < s.Length && s[i] != '"' && s[i] != '\\') i++;
            if (i < s.Length && s[i] == '"') { string r = s.Substring(st, i - st); i++; return r; }
            var sb = new StringBuilder(s, st, i - st, (i - st) + 16);
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("json: unterminated string");
        }

        // ---------------------------------------------------------------- writer
        public static string Serialize(object o)
        {
            var sb = new StringBuilder();
            Write(sb, o);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: WriteNum(sb, d); break;
                case float f: WriteNum(sb, f); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case uint n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case short n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case ushort n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case byte n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> d:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in d)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key); sb.Append(':'); Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                }
                case System.Collections.IEnumerable e:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var v in e)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, v);
                    }
                    sb.Append(']');
                    break;
                }
                default: WriteString(sb, o.ToString()); break;
            }
        }

        static void WriteNum(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
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
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------------------------------------------------------- helpers for reading parsed data
        public static double D(object o) => o is double d ? d : o is bool b ? (b ? 1 : 0) : 0;
        public static int I(object o) => (int)D(o);
        public static string S(object o) => o as string;
        public static List<object> L(object o) => o as List<object>;
        public static Dictionary<string, object> O(object o) => o as Dictionary<string, object>;
        public static object Get(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : null;
    }
}
