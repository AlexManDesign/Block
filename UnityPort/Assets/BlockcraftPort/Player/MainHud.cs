using UnityEngine;

namespace BlockcraftPort
{
    /// <summary>
    /// main.js in-game HUD: hotbar (L3 "hud"), crosshair, and the survival rows Xa()/V2()
    /// (hearts, food, armour, air bubbles) drawn with the reference 9x9 SVG icon shapes.
    /// </summary>
    public sealed class MainHud : MonoBehaviour
    {
        public MainPlayerController Player;
        public static bool UiRussian => Application.systemLanguage == SystemLanguage.Russian || Application.systemLanguage == SystemLanguage.Ukrainian;

        // main TR(): icon outlines in a 9x9 viewBox.
        static readonly Vector2[] HeartPath = Path("M1 2h2v1h1v1h1V3h1V2h2v3H7v1H6v1H5v1H4V7H3V6H2V5H1z");
        static readonly Vector2[] FoodPath = Path("M5 1h2v1h1v2H7v1H6v1H5v1H4v1H2V7H1V6h1V5h1V4h1V3h1z");
        static readonly Vector2[] ArmorPath = Path("M1 1h2v1h3V1h2v3H7v4H2V4H1z");
        static readonly Color32 HeartStroke = new Color32(0x1a, 0x05, 0x05, 255), FoodStroke = new Color32(0x1a, 0x0e, 0x05, 255),
            ArmorStroke = new Color32(0x10, 0x14, 0x18, 255), BubbleStroke = new Color32(0x0a, 0x2a, 0x3a, 255);
        readonly System.Collections.Generic.Dictionary<long, Texture2D> iconCache = new System.Collections.Generic.Dictionary<long, Texture2D>();

        int lastSel = -1; float nameShownAt = -10f; string shownName = ""; GUIStyle nameStyle;

        void OnGUI()
        {
            if (Player == null || Event.current.type != EventType.Repaint) return;
            var fb = MainWorldFunctionalBlocks.Instance;
            if (fb != null && fb.UiOpen) return;
            float s = Mathf.Clamp(Screen.height / 720f, .75f, 2f);
            DrawCrosshair(s);
            Rect bar = DrawHotbar(s);
            if (!Player.IsCreative) DrawSurvivalRows(bar, s);
            DrawSelectedName(bar, s);
        }

        void DrawCrosshair(float s)
        {
            float cx = Screen.width * .5f, cy = Screen.height * .5f, len = 9f * s, th = Mathf.Max(2f, 2f * s);
            MainIcons.Fill(new Rect(cx - len, cy - th * .5f, len * 2f, th), new Color(1, 1, 1, .85f));
            MainIcons.Fill(new Rect(cx - th * .5f, cy - len, th, len * 2f), new Color(1, 1, 1, .85f));
        }

        Rect DrawHotbar(float s)
        {
            var inv = Player.SurvivalInventory;
            float cell = 44f * s, gap = 2f * s, w = 9 * cell + 8 * gap;
            float x = (Screen.width - w) * .5f, y = Screen.height - cell - 10f * s;
            MainIcons.Fill(new Rect(x - 4 * s, y - 4 * s, w + 8 * s, cell + 8 * s), new Color(0, 0, 0, .35f));
            for (int i = 0; i < 9; i++)
            {
                Rect r = new Rect(x + i * (cell + gap), y, cell, cell);
                MainIcons.Fill(r, new Color(.1f, .1f, .1f, .55f));
                MainIcons.Frame(r, new Color(1, 1, 1, .18f), Mathf.Max(1f, s));
                MainIcons.DrawStack(r, inv.Get(i));
                MainIcons.SlotNumber(r, i + 1);
                if (i == inv.HotbarSelected) MainIcons.Frame(new Rect(r.x - 2 * s, r.y - 2 * s, r.width + 4 * s, r.height + 4 * s), Color.white, Mathf.Max(2f, 3f * s));
            }
            return new Rect(x, y, w, cell);
        }

        void DrawSelectedName(Rect bar, float s)
        {
            var inv = Player.SurvivalInventory; int sel = inv.HotbarSelected;
            var st = inv.Get(sel);
            string name = MainIcons.Name(st, UiRussian);
            if (sel != lastSel || name != shownName) { lastSel = sel; shownName = name; nameShownAt = Time.unscaledTime; }
            float age = Time.unscaledTime - nameShownAt;
            if (string.IsNullOrEmpty(shownName) || age > 2f) return;
            float a = age < 1.5f ? 1f : 1f - (age - 1.5f) / .5f;
            if (nameStyle == null) nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            var style = nameStyle; style.fontSize = Mathf.RoundToInt(15 * s);
            float rows = Player.IsCreative ? 0f : 26f * s * (Player.SourceArmorPoints > 0 ? 2 : 1) + 8f * s;
            Rect r = new Rect(bar.x, bar.y - 30f * s - rows, bar.width, 24f * s);
            style.normal.textColor = new Color(0, 0, 0, .7f * a); GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), shownName, style);
            style.normal.textColor = new Color(1, 1, 1, a); GUI.Label(r, shownName, style);
        }

        void DrawSurvivalRows(Rect bar, float s)
        {
            float ico = 18f * s, step = ico + 1f * s, rowY = bar.y - ico - 8f * s;
            // main Xa(): hearts (left) and food (right); armour above hearts; air above food while submerged.
            Row(new Vector2(bar.x, rowY), step, ico, 0, Mathf.RoundToInt(Player.Health), new Color32(0xe0, 0x20, 0x20, 255), new Color32(0xb0, 0x6a, 0x5a, 255), new Color32(0x3a, 0x10, 0x10, 255));
            Row(new Vector2(bar.xMax - 10 * step + 1f * s, rowY), step, ico, 1, Player.Hunger, new Color32(0xc8, 0x86, 0x2a, 255), new Color32(0x8a, 0x5a, 0x30, 255), new Color32(0x3a, 0x2a, 0x10, 255));
            int armor = Player.SourceArmorPoints;
            if (armor > 0) Row(new Vector2(bar.x, rowY - step - 2f * s), step, ico, 2, armor, new Color32(0xc8, 0xcc, 0xd2, 255), new Color32(0x7d, 0x81, 0x88, 255), new Color32(0, 0, 0, 0));
            const float MaxAir = 10f;
            if (Player.Air < MaxAir - .01f)
            {
                int bubbles = Mathf.CeilToInt(Player.Air / MaxAir * 20f);
                Row(new Vector2(bar.xMax - 10 * step + 1f * s, rowY - step - 2f * s), step, ico, 3, bubbles, new Color32(0x6f, 0xc3, 0xe8, 255), new Color32(0x55, 0x90, 0xb0, 255), new Color32(0, 0, 0, 0));
            }
        }

        /// <summary>main V2(): ten icons, each worth 2 points (full / half / empty colours).</summary>
        void Row(Vector2 at, float step, float ico, int kind, int value, Color32 full, Color32 half, Color32 empty)
        {
            for (int i = 0; i < 10; i++)
            {
                int u = value - i * 2;
                Color32 c = u >= 2 ? full : u == 1 ? half : empty;
                if (c.a == 0) continue;
                Texture2D t = Icon(kind, c);
                GUI.DrawTexture(new Rect(at.x + i * step, at.y, ico, ico), t);
            }
        }

        Texture2D Icon(int kind, Color32 fill)
        {
            long key = ((long)kind << 32) | ((long)fill.r << 24) | ((long)fill.g << 16) | ((long)fill.b << 8) | fill.a;
            if (iconCache.TryGetValue(key, out var t)) return t;
            const int S = 36; const float k = S / 9f;
            var px = new Color32[S * S];
            Vector2[] poly = kind == 0 ? HeartPath : kind == 1 ? FoodPath : kind == 2 ? ArmorPath : null;
            Color32 stroke = kind == 0 ? HeartStroke : kind == 1 ? FoodStroke : kind == 2 ? ArmorStroke : BubbleStroke;
            float sw = kind == 3 ? .6f : .5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2((x + .5f) / k, (y + .5f) / k);
                    bool inside; float d;
                    if (poly == null) { float r = Vector2.Distance(p, new Vector2(4.5f, 4.5f)); inside = r <= 3f; d = Mathf.Abs(r - 3f); }
                    else { inside = Inside(poly, p); d = EdgeDistance(poly, p); }
                    Color32 c = default;
                    if (inside) c = fill;
                    if (d <= sw * .5f) c = stroke;
                    px[(S - 1 - y) * S + x] = c;
                }
            t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            t.SetPixels32(px); t.Apply(false, true);
            iconCache[key] = t; return t;
        }

        static Vector2[] Path(string d)
        {
            var pts = new System.Collections.Generic.List<Vector2>();
            float x = 0, y = 0; int i = 0;
            while (i < d.Length)
            {
                char c = d[i++];
                if (c == 'z' || c == 'Z') break;
                float Num() { int st = i; while (i < d.Length && (char.IsDigit(d[i]) || d[i] == '.')) i++; return float.Parse(d.Substring(st, i - st), System.Globalization.CultureInfo.InvariantCulture); }
                switch (c)
                {
                    case 'M': x = Num(); i++; y = Num(); break;
                    case 'h': x += Num(); break;
                    case 'v': y += Num(); break;
                    case 'H': x = Num(); break;
                    case 'V': y = Num(); break;
                    default: continue;
                }
                pts.Add(new Vector2(x, y));
            }
            return pts.ToArray();
        }
        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }
        static float EdgeDistance(Vector2[] poly, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}
