// Voxel Forge — Unity port. IMGUI replacement for the reference DOM UI: start screen / world list, HUD,
// hotbar, inventory / crafting / chest / furnace panels, recipe book, creative palette, pause, settings,
// mods, sleep overlay, death screen and touch controls.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VoxelForge
{
    public static partial class VF
    {
        static bool uiStylesReady = false;
        static GUIStyle stLabel, stSmall, stTitle, stH2, stH3, stButton, stDanger, stPanel, stCount, stCenter, stToast, stStats, stInput, stWrap, stSlot;
        static Texture2D texWhite;
        static float uiScale = 1;
        public static bool uiTextFocused = false;
        static string worldNameInput = "";
        static Vector2 worldScroll, uiScroll, recipeScroll, creativeScroll, modsScroll;
        // confirm / alert dialog
        static string dialogText = null; static Action dialogYes = null; static bool dialogIsAlert = false;
        // recipe book
        static bool recipeBookOpen = false, recipeBookAvailableOnly = true; static string recipeBookSearch = "";
        // slot drag state
        static Stack[] dragArr; static int dragIdx = -1; static Vector2 dragStart; static bool dragMoved; static SlotOpts dragOpts;

        static Color C(int hex, float a = 1) { return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, a); }
        static GUIStyle mkStyle(GUIStyle bse, int size, Color col, FontStyle fs = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var s = new GUIStyle(bse) { fontSize = size, fontStyle = fs, alignment = anchor, richText = true };
            s.normal.textColor = col; s.hover.textColor = col; s.active.textColor = col; s.focused.textColor = col;
            return s;
        }
        static void initUIStyles()
        {
            if (uiStylesReady) return;
            texWhite = Texture2D.whiteTexture;
            stLabel = mkStyle(GUI.skin.label, 14, C(0xe8eef4));
            stSmall = mkStyle(GUI.skin.label, 12, C(0xaab7c4)); stSmall.wordWrap = true;
            stWrap = mkStyle(GUI.skin.label, 13, C(0xd8e2ea)); stWrap.wordWrap = true;
            stTitle = mkStyle(GUI.skin.label, 34, Color.white, FontStyle.Bold);
            stH2 = mkStyle(GUI.skin.label, 20, Color.white, FontStyle.Bold);
            stH3 = mkStyle(GUI.skin.label, 15, C(0xffe9a8), FontStyle.Bold);
            stButton = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = true, wordWrap = true };
            stDanger = new GUIStyle(stButton); stDanger.normal.textColor = C(0xff8a80); stDanger.hover.textColor = C(0xffb0a8);
            stPanel = new GUIStyle(GUI.skin.box);
            stCount = mkStyle(GUI.skin.label, 12, Color.white, FontStyle.Bold, TextAnchor.LowerRight);
            stCenter = mkStyle(GUI.skin.label, 14, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
            stToast = mkStyle(GUI.skin.label, 15, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            stStats = mkStyle(GUI.skin.label, 11, C(0xdfe8ef)); stStats.wordWrap = false;
            stInput = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
            stSlot = new GUIStyle(GUI.skin.box);
            uiStylesReady = true;
        }
        static void fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, texWhite); GUI.color = o; }
        static void frame(Rect r, Color c, float w = 1) { fill(new Rect(r.x, r.y, r.width, w), c); fill(new Rect(r.x, r.yMax - w, r.width, w), c); fill(new Rect(r.x, r.y, w, r.height), c); fill(new Rect(r.xMax - w, r.y, w, r.height), c); }
        static void shadowLabel(Rect r, string t, GUIStyle s)
        {
            var c = s.normal.textColor; s.normal.textColor = new Color(0, 0, 0, 0.85f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), t, s);
            s.normal.textColor = c; GUI.Label(r, t, s);
        }

        // ---------------- item icons (drawItemIcon) ----------------
        static void drawItemIcon(Rect r, Stack s) { if (s != null) drawItemIcon(r, s.key); }
        static void drawItemIcon(Rect r, string key)
        {
            if (key == null) return;
            if (isBK(key) || isVK(key))
            {
                int t;
                if (isVK(key)) { var v = VirtualByKey(vkey(key)); if (v == null) return; t = v.side; }
                else { var b = bdef(bid(key)); if (b == null) return; t = b.side; }
                if (t < 0 || atlasImg == null) return;
                float col = t % 32, row = t / 32;
                GUI.DrawTextureWithTexCoords(new Rect(r.x + r.width / 32f, r.y + r.height / 32f, r.width * 30 / 32f, r.height * 30 / 32f), atlasImg, new Rect(col / 32f, 1 - (row + 1) / 32f, 1 / 32f, 1 / 32f));
            }
            else
            {
                int t = ItemTile(key);
                if (t < 0 || itemTex == null) return;
                float col = t % 16, row = t / 16;
                GUI.DrawTextureWithTexCoords(new Rect(r.x + r.width / 32f, r.y + r.height / 32f, r.width * 30 / 32f, r.height * 30 / 32f), itemTex, new Rect(col / 16f, 1 - (row + 1) / 16f, 1 / 16f, 1 / 16f));
            }
        }

        // ---------------- entry point ----------------
        public static void drawUI()
        {
            initUIStyles();
            uiScale = Mathf.Max(1f, Screen.height / 900f);
            var m = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1));
            float W = Screen.width / uiScale, H = Screen.height / uiScale;
            uiTextFocused = GUIUtility.keyboardControl != 0 && (startScreenVisible || (uiOpen && recipeBookOpen));
            if (startScreenVisible) drawStartScreen(W, H);
            if (hudVisible && started) drawHUD(W, H);
            if (touchActive) drawTouchControls(W, H);
            if (sleepOverlayState != 0) drawSleepOverlay(W, H);
            if (pauseOpen) drawPause(W, H);
            if (uiOpen) drawGameUI(W, H);
            if (settingsOpen) drawSettings(W, H);
            if (modsOpen) drawMods(W, H);
            if (deathUIVisible) drawDeath(W, H);
            if (dialogText != null) drawDialog(W, H);
            GUI.matrix = m;
        }
        static void confirm(string text, Action yes) { dialogText = text; dialogYes = yes; dialogIsAlert = false; }
        static void alert(string text) { dialogText = text; dialogYes = null; dialogIsAlert = true; }
        static void drawDialog(float W, float H)
        {
            fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.55f));
            var r = new Rect(W / 2 - 220, H / 2 - 80, 440, 160);
            fill(r, C(0x111a22, 0.97f)); frame(r, C(0xffffff, 0.25f));
            GUI.Label(new Rect(r.x + 18, r.y + 16, r.width - 36, 80), dialogText, stWrap);
            if (dialogIsAlert) { if (GUI.Button(new Rect(r.xMax - 118, r.yMax - 48, 100, 32), "OK", stButton)) dialogText = null; return; }
            if (GUI.Button(new Rect(r.xMax - 228, r.yMax - 48, 100, 32), "OK", stButton)) { var y = dialogYes; dialogText = null; if (y != null) y(); }
            if (GUI.Button(new Rect(r.xMax - 118, r.yMax - 48, 100, 32), "Отмена", stButton)) dialogText = null;
        }

        // ---------------- start screen / world list ----------------
        static readonly string[] FEATURES = { "чанковый мир 16×16", "greedy meshing + voxel light", "животные и враждебные мобы", "крафт, печь и контейнеры", "3D density -64…320 и sparse-секции", "несколько именованных миров" };
        static void drawStartScreen(float W, float H)
        {
            fill(new Rect(0, 0, W, H), C(0x0b1218));
            float cw = Mathf.Min(760, W - 32), ch = Mathf.Min(H - 32, 720);
            var card = new Rect((W - cw) / 2, (H - ch) / 2, cw, ch);
            fill(card, C(0x101c26, 0.96f)); frame(card, C(0xffffff, 0.18f));
            float x = card.x + 24, y = card.y + 18, w = cw - 48;
            GUI.Label(new Rect(x, y, w, 44), "Voxel Forge", stTitle); y += 46;
            GUI.Label(new Rect(x, y, w, 20), "<b>v" + GAME_VERSION + "</b> · свой движок · Unity порт", stSmall); y += 24;
            float fx = x;
            foreach (var f in FEATURES)
            {
                var sz = stSmall.CalcSize(new GUIContent(f)); float fw = sz.x + 16;
                if (fx + fw > x + w) { fx = x; y += 26; }
                var r = new Rect(fx, y, fw, 22); fill(r, C(0x1d2c38)); GUI.Label(new Rect(r.x + 8, r.y + 3, r.width, r.height), f, stSmall); fx += fw + 6;
            }
            y += 34;
            GUI.Label(new Rect(x, y, 80, 22), "<b>Миры</b>", stLabel);
            GUI.Label(new Rect(x + 60, y + 2, w - 60, 20), "Создавай несколько Creator/Survival миров; каждый имеет свой seed и сохранение.", stSmall); y += 26;
            var list = ensureLegacyWorldIndex().OrderByDescending(q => q.updatedAt).ToList();
            float listH = ch - (y - card.y) - 190;
            var outer = new Rect(x, y, w, listH);
            fill(outer, C(0x0c151d));
            float rowH = 54, innerH = Mathf.Max(listH, list.Count * (rowH + 6));
            worldScroll = GUI.BeginScrollView(outer, worldScroll, new Rect(0, 0, w - 20, innerH));
            if (list.Count == 0) GUI.Label(new Rect(10, 10, w - 40, 22), "Миров пока нет — создай первый.", stSmall);
            float ry = 4;
            foreach (var wr in list)
            {
                var row = new Rect(4, ry, w - 28, rowH); fill(row, C(0x15232f));
                GUI.Label(new Rect(row.x + 10, row.y + 6, row.width - 300, 22), "<b>" + wr.name + "</b>", stLabel);
                var sp = wr.spawn;
                string meta = (wr.mode == "creative" ? "Creator" : "Survival") + " · " + (wr.seed.HasValue && JS.isFinite(wr.seed.Value) ? "seed " + JS.toInt32(wr.seed.Value) + " · " : "")
                    + (sp != null ? "spawn " + string.Join(" ", sp.Select(v => JS.ToStr(JS.round(v * 10) / 10))) + " · " : "") + formatWorldTime(wr.updatedAt != 0 ? wr.updatedAt : wr.createdAt);
                GUI.Label(new Rect(row.x + 10, row.y + 28, row.width - 300, 22), meta, stSmall);
                GUI.enabled = !startButtonsDisabled;
                var id = wr.id; var mode = wr.mode; var nm = wr.name;
                if (GUI.Button(new Rect(row.xMax - 286, row.y + 11, 90, 32), "Играть", stButton)) startGame(mode, id);
                if (GUI.Button(new Rect(row.xMax - 190, row.y + 11, 84, 32), "Копия", stButton))
                {
                    var q = copyWorldRecord(id);
                    if (q != null) alert("Создана чистая копия «" + q.name + "». Перенесены только seed" + (q.spawn != null ? " и точка появления." : "."));
                }
                if (GUI.Button(new Rect(row.xMax - 100, row.y + 11, 92, 32), "Удалить", stDanger)) confirm("Удалить мир «" + nm + "»?", () => deleteWorldRecord(id));
                GUI.enabled = true;
                ry += rowH + 6;
            }
            GUI.EndScrollView();
            y += listH + 12;
            GUI.SetNextControlName("worldName");
            worldNameInput = GUI.TextField(new Rect(x, y, w, 30), worldNameInput, 40, stInput);
            if (string.IsNullOrEmpty(worldNameInput) && GUI.GetNameOfFocusedControl() != "worldName") GUI.Label(new Rect(x + 6, y + 6, w, 20), "Название нового мира", stSmall);
            y += 38;
            GUI.enabled = !startButtonsDisabled;
            if (GUI.Button(new Rect(x, y, w / 2 - 6, 64), "<b>Новый Creator</b>\n<size=12>Бесконечные блоки, мгновенная добыча, полёт.</size>", stButton)) { var n = worldNameInput; worldNameInput = ""; createAndStartWorld("creative", n); }
            if (GUI.Button(new Rect(x + w / 2 + 6, y, w / 2 - 6, 64), "<b>Новый Survival</b>\n<size=12>HP, голод, инструменты, крафт и мобы.</size>", stButton)) { var n = worldNameInput; worldNameInput = ""; createAndStartWorld("survival", n); }
            GUI.enabled = true;
            y += 72;
            GUI.Label(new Rect(x, y, w, 40), startNote, stSmall);
        }

        // ---------------- HUD ----------------
        static void drawHUD(float W, float H)
        {
            // stats / mode
            GUI.Label(new Rect(10, 8, 900, 140), statsText, stStats);
            GUI.Label(new Rect(10, 146, 900, 18), modeText, stStats);
            if (!string.IsNullOrEmpty(resourceHUDText)) GUI.Label(new Rect(W - 330, 8, 320, 200), resourceHUDText, mkStyleCached(ref stResCache, () => mkStyle(GUI.skin.label, 12, C(0xe0e8ee), FontStyle.Normal, TextAnchor.UpperRight)));
            if (xrayActive) { var r = new Rect(W / 2 - 50, 10, 100, 22); fill(r, C(0x6a1b9a, 0.8f)); GUI.Label(r, "X-RAY · X", stCenter); }
            if (minimapVisible && minimapTex != null)
            {
                bool small = W <= 620; float sz = small ? 124 : 156, inner = small ? 112 : 144;
                var wrap = new Rect(W - 12 - sz, H - (small ? 76 : 86) - sz, sz, sz);
                fill(wrap, C(0x05090d, 0.8f)); frame(wrap, C(0xffffff, 0.27f));
                GUI.DrawTexture(new Rect(wrap.x + (sz - inner) / 2, wrap.y + (sz - inner) / 2, inner, inner), minimapTex);
            }
            if (!TOUCH_DEVICE) GUI.Label(new Rect(10, H - 26, W - 20, 20), "WASD · мышь · удерживай ЛКМ: добывать/атаковать · ПКМ использовать/ставить · E инвентарь · Q выбросить · 1–9/колесо · F полёт в Creator · [ ] дальность · Esc", stSmall);
            // toast
            if (JS.now() < toastUntil + 300 && !string.IsNullOrEmpty(toastText))
            {
                float a = JS.now() < toastUntil ? 1 : (float)Math.Max(0, 1 - (JS.now() - toastUntil) / 300);
                var sz = stToast.CalcSize(new GUIContent(toastText)); var r = new Rect(W / 2 - sz.x / 2 - 12, H * 0.28f, sz.x + 24, 30);
                fill(r, new Color(0, 0, 0, 0.55f * a)); var oc = GUI.color; GUI.color = new Color(1, 1, 1, a); GUI.Label(r, toastText, stToast); GUI.color = oc;
            }
            // crosshair
            if (player.camMode == 0)
            {
                fill(new Rect(W / 2 - 9, H / 2 - 1, 18, 2), new Color(1, 1, 1, 0.9f));
                fill(new Rect(W / 2 - 1, H / 2 - 9, 2, 18), new Color(1, 1, 1, 0.9f));
            }
            // hotbar
            float sl = 46, hbW = sl * 9 + 8 * 2, hbX = W / 2 - hbW / 2, hbY = H - sl - 12;
            for (int i = 0; i < 9; i++)
            {
                var r = new Rect(hbX + i * (sl + 2), hbY, sl, sl);
                fill(r, C(0x0b1218, 0.78f)); frame(r, i == selected ? C(0xffe36a) : C(0xffffff, 0.25f), i == selected ? 2 : 1);
                GUI.Label(new Rect(r.x + 3, r.y + 1, 12, 12), "<size=9>" + (i + 1) + "</size>", stSmall);
                var st = inventory[i];
                drawItemIcon(new Rect(r.x + 7, r.y + 7, 32, 32), st);
                if (st != null)
                {
                    string cnt = player.creative ? "∞" : st.count > 1 ? st.count.ToString() : idef(st.key).maxDur != 0 ? (st.dur != 0 ? st.dur : idef(st.key).maxDur).ToString() : "";
                    if (cnt.Length > 0) shadowLabel(new Rect(r.x, r.y, r.width - 3, r.height - 1), cnt, stCount);
                }
                if (TOUCH_DEVICE && Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { cancelBowCharge(); selected = i; drawHotbar(); Event.current.Use(); }
            }
            var ss = selectedStack();
            string bn = ss != null ? idef(ss.key).name + (player.creative ? " · ∞" : ss.count > 1 ? " ×" + ss.count : "") : "Пустая рука";
            shadowLabel(new Rect(0, hbY - 50, W, 20), bn, stCenter);
            // vitals
            if (!player.creative)
            {
                float vx = hbX, vy = hbY - 26, vw = 150;
                drawBar(new Rect(vx, vy, vw, 16), player.hp / 20, C(0xd84343), "❤ " + Math.Ceiling(player.hp) + "/20");
                drawBar(new Rect(vx + vw + 8, vy, vw, 16), player.hunger / 20, C(0xc8892d), "Еда " + Math.Ceiling(player.hunger) + "/20");
                if (player.air < 9.95) drawBar(new Rect(vx + (vw + 8) * 2, vy, vw - 30, 16), player.air / 10, C(0x3d8fd8), "Воздух " + Math.Ceiling(player.air));
            }
            // mining / bow bar
            if (mineBarVisible) drawBar(new Rect(W / 2 - 80, H / 2 + 22, 160, 6), mineBarFrac, C(0xffffff), null);
            if (attackMeterVisible && !player.creative)
            {
                double f = Math.Min(1, (JS.now() - attackMeterStart) / 1000 / Math.Max(0.001, attackMeterDur));
                drawBar(new Rect(W / 2 - 40, H / 2 + 32, 80, 4), f, C(0xffe36a), null);
            }
        }
        static GUIStyle stResCache;
        static GUIStyle mkStyleCached(ref GUIStyle s, Func<GUIStyle> make) { return s ?? (s = make()); }
        static void drawBar(Rect r, double frac, Color c, string text)
        {
            fill(r, new Color(0, 0, 0, 0.55f));
            fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01((float)frac), r.height), c);
            if (text != null) GUI.Label(new Rect(r.x + 4, r.y - 1, r.width, r.height + 2), "<size=11>" + text + "</size>", stLabel);
        }

        // ---------------- touch ----------------
        static void drawTouchControls(float W, float H)
        {
            float s = uiScale;
            touchStickRect = new Rect(24 * s, Screen.height - 190 * s, 150 * s, 150 * s);
            float bx = Screen.width - 80 * s, by = Screen.height - 260 * s, bs = 62 * s;
            touchAttackRect = new Rect(bx - bs - 10 * s, by + bs + 10 * s, bs, bs); touchUseRect = new Rect(bx, by + bs + 10 * s, bs, bs);
            touchJumpRect = new Rect(bx, by + 2 * (bs + 10 * s), bs, bs); touchSneakRect = new Rect(bx - bs - 10 * s, by + 2 * (bs + 10 * s), bs, bs);
            touchInvRect = new Rect(bx, by, bs, bs); touchPauseRect = new Rect(Screen.width - 70 * s, 12 * s, 56 * s, 44 * s);
            touchPrevRect = new Rect(Screen.width / 2f - 300 * s, Screen.height - 64 * s, 44 * s, 44 * s); touchNextRect = new Rect(Screen.width / 2f + 256 * s, Screen.height - 64 * s, 44 * s, 44 * s);
            Func<Rect, Rect> ui = r => new Rect(r.x / s, r.y / s, r.width / s, r.height / s);
            var st = ui(touchStickRect); fill(st, new Color(1, 1, 1, 0.12f)); frame(st, new Color(1, 1, 1, 0.3f));
            var kc = new Vector2(st.center.x + (float)(touchStickX * st.width * 0.38f), st.center.y + (float)(touchStickY * st.width * 0.38f));
            fill(new Rect(kc.x - 24, kc.y - 24, 48, 48), new Color(1, 1, 1, 0.35f));
            Action<Rect, string> btn = (r, t) => { var q = ui(r); fill(q, new Color(0, 0, 0, 0.4f)); frame(q, new Color(1, 1, 1, 0.35f)); GUI.Label(q, t, stCenter); };
            btn(touchAttackRect, "⛏"); btn(touchUseRect, "◉"); btn(touchJumpRect, "▲"); btn(touchSneakRect, "▼"); btn(touchInvRect, "▦"); btn(touchPauseRect, "Ⅱ"); btn(touchPrevRect, "‹"); btn(touchNextRect, "›");
        }

        // ---------------- pause / settings / mods / sleep / death ----------------
        static Rect centerBox(float W, float H, float w, float h) { return new Rect((W - w) / 2, (H - h) / 2, w, h); }
        static void drawPause(float W, float H)
        {
            fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.55f));
            var r = centerBox(W, H, Mathf.Min(560, W - 32), 250);
            fill(r, C(0x101c26, 0.97f)); frame(r, C(0xffffff, 0.2f));
            GUI.Label(new Rect(r.x + 20, r.y + 14, r.width - 40, 30), "Пауза", stH2);
            GUI.Label(new Rect(r.x + 20, r.y + 50, r.width - 40, 44), "Кликни «Продолжить», чтобы снова захватить мышь.\nИзменения мира сохраняются локально.", stWrap);
            float bw = (r.width - 40 - 8) / 2, y = r.y + 104;
            if (GUI.Button(new Rect(r.x + 20, y, bw, 34), "Продолжить", stButton)) { hidePause(); initAudio(); if (!TOUCH_DEVICE) requestGamePointerLock(); }
            if (GUI.Button(new Rect(r.x + 28 + bw, y, bw, 34), "Настройки", stButton)) settingsOpen = true;
            y += 42;
            if (GUI.Button(new Rect(r.x + 20, y, bw, 34), "Моды", stButton)) modsOpen = true;
            if (GUI.Button(new Rect(r.x + 28 + bw, y, bw, 34), "Выбор режима", stButton)) returnToTitle();
            y += 42;
            if (GUI.Button(new Rect(r.x + 20, y, r.width - 40, 34), "Сбросить мир", stDanger))
            {
                var w = loadWorldIndex().FirstOrDefault(q => q.id == CURRENT_WORLD_ID);
                confirm("Сбросить текущий мир «" + (w != null ? w.name : "") + "»?", () => { var id = CURRENT_WORLD_ID; var mode = GAME_MODE; returnToTitle(); configureSaveMode(mode, id); clearCurrentSave(); });
            }
        }
        static void drawSettings(float W, float H)
        {
            fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f));
            var r = centerBox(W, H, Mathf.Min(700, W - 32), 470);
            fill(r, C(0x101c26, 0.98f)); frame(r, C(0xffffff, 0.2f));
            GUI.Label(new Rect(r.x + 20, r.y + 14, 300, 30), "Настройки", stH2);
            if (GUI.Button(new Rect(r.xMax - 110, r.y + 14, 90, 30), "Готово", stButton)) settingsOpen = false;
            float x = r.x + 20, y = r.y + 60, lw = 210, cw = r.width - lw - 50;
            GUI.Label(new Rect(x, y, lw, 22), "Дальность прорисовки", stLabel);
            var rdv = Array.FindAll(MAIN_RD_VALUES, v => v <= DEVICE_RD_CAP);
            int rdi = Math.Max(0, Array.IndexOf(rdv, renderDistance));
            int nrd = GUI.SelectionGrid(new Rect(x + lw, y, cw, 26), rdi, rdv.Select(v => v.ToString()).ToArray(), rdv.Length, stButton);
            if (nrd != rdi) applyRenderDistanceSetting(rdv[nrd]);
            GUI.Label(new Rect(x + lw, y + 28, cw, 18), "чанков · Ограничение устройства: " + DEVICE_RD_CAP, stSmall);
            y += 56;
            GUI.Label(new Rect(x, y, lw, 22), "Дальность симуляции", stLabel);
            var sdv = Array.FindAll(MAIN_SIM_VALUES, v => v <= DEVICE_RD_CAP);
            int sdi = Math.Max(0, Array.IndexOf(sdv, simulationDistance));
            int nsd = GUI.SelectionGrid(new Rect(x + lw, y, cw, 26), sdi, sdv.Select(v => v.ToString()).ToArray(), sdv.Length, stButton);
            if (nsd != sdi) applySimulationDistanceSetting(sdv[nsd]);
            GUI.Label(new Rect(x + lw, y + 28, cw, 18), "Мобы и жидкости · эффективно " + activeSimulationDistance() + " чанков (не больше дальности прорисовки)", stSmall);
            y += 56;
            GUI.Label(new Rect(x, y, lw, 22), "Чувствительность мыши", stLabel);
            double ns = Math.Round(GUI.HorizontalSlider(new Rect(x + lw, y + 6, cw - 60, 20), (float)gameSettings.sensitivity, 0.3f, 2f) * 10) / 10;
            GUI.Label(new Rect(x + lw + cw - 50, y, 50, 22), "×" + JS.Fixed(ns, 1), stLabel);
            if (Math.Abs(ns - gameSettings.sensitivity) > 1e-9) { gameSettings.sensitivity = ns; persistGameSettings(); }
            y += 40;
            GUI.Label(new Rect(x, y, lw, 22), "Sound", stLabel);
            bool snd = GUI.Toggle(new Rect(x + lw, y, cw, 22), gameSettings.sound, " Звук");
            if (snd != gameSettings.sound) setSoundEnabled(snd);
            y += 32;
            GUI.Label(new Rect(x, y, lw, 22), "Clouds", stLabel);
            bool cl = GUI.Toggle(new Rect(x + lw, y, cw, 22), cloudsEnabled, " Облака");
            if (cl != cloudsEnabled) { gameSettings.clouds = cl; cloudsEnabled = cl; persistGameSettings(); toast("Облака " + (cloudsEnabled ? "включены" : "выключены")); }
            y += 32;
            GUI.Label(new Rect(x, y, lw, 22), "Resolution", stLabel);
            bool dr = GUI.Toggle(new Rect(x + lw, y, cw, 22), gameSettings.dynRes, " Динамическое разрешение");
            if (dr != gameSettings.dynRes) { gameSettings.dynRes = dr; persistGameSettings(); toast("Динамическое разрешение " + (dr ? "включено" : "выключено")); }
            GUI.Label(new Rect(x + lw, y + 22, cw, 18), "Снижает разрешение рендера, когда видеокарта не успевает · сейчас ×" + JS.ToStr(renderScale), stSmall);
            y += 54;
            GUI.Label(new Rect(x, y, r.width - 40, 60), "Render Distance отвечает за видимые чанки. Simulation Distance ограничивает тики мобов и жидкостей по X/Z; высота Y не отключает симуляцию. Настройки сохраняются отдельно от мира.", stSmall);
        }
        static void drawMods(float W, float H)
        {
            fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f));
            var r = centerBox(W, H, Mathf.Min(820, W - 32), Mathf.Min(560, H - 32));
            fill(r, C(0x101c26, 0.98f)); frame(r, C(0xffffff, 0.2f));
            GUI.Label(new Rect(r.x + 20, r.y + 14, r.width - 160, 30), "Моды — перенос алгоритмов main", stH2);
            if (GUI.Button(new Rect(r.xMax - 110, r.y + 14, 90, 30), "Закрыть", stButton)) modsOpen = false;
            var inner = new Rect(r.x + 16, r.y + 56, r.width - 32, r.height - 72);
            float rowH = 58, total = MOD_META.Length * rowH + 70;
            modsScroll = GUI.BeginScrollView(inner, modsScroll, new Rect(0, 0, inner.width - 20, total));
            float y = 0;
            foreach (var mm in MOD_META)
            {
                bool on = modEnabled(mm.id);
                bool non = GUI.Toggle(new Rect(4, y + 4, 24, 24), on, "");
                if (non != on) setMod(mm.id, non);
                GUI.Label(new Rect(32, y, inner.width - 60, 22), "<b>" + mm.name + "</b>", stLabel);
                GUI.Label(new Rect(32, y + 20, inner.width - 60, 36), mm.desc + (mm.id == "xray" ? " Горячая клавиша: X." : ""), stSmall);
                y += rowH;
            }
            GUI.Label(new Rect(4, y + 6, inner.width - 30, 60), "Управление кораблём из main здесь намеренно не имитируется заглушкой: это следующий отдельный слой — связный корпус, локальные координаты, контейнеры, вода внутри и плавучесть.", stSmall);
            GUI.EndScrollView();
        }
        static void drawSleepOverlay(float W, float H)
        {
            float a = sleepOverlayState == 2 ? 0.92f : 0.35f;
            fill(new Rect(0, 0, W, H), new Color(0.02f, 0.03f, 0.08f, a));
            GUI.Label(new Rect(0, H / 2 - 40, W, 30), sleepLabel, stToast);
            if (player.sleeping && GUI.Button(new Rect(W / 2 - 70, H / 2, 140, 34), "Проснуться", stButton)) wakeFromSleep();
        }
        static void drawDeath(float W, float H)
        {
            fill(new Rect(0, 0, W, H), new Color(0.35f, 0, 0, 0.55f));
            var r = centerBox(W, H, 420, 190);
            fill(r, C(0x1a0d0d, 0.95f)); frame(r, C(0xff6b6b, 0.4f));
            GUI.Label(new Rect(r.x, r.y + 18, r.width, 40), "Вы погибли", mkStyleCached(ref stDeathTitle, () => mkStyle(GUI.skin.label, 30, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter)));
            GUI.Label(new Rect(r.x + 16, r.y + 70, r.width - 32, 40), deathReasonText, stCenter);
            if (GUI.Button(new Rect(r.x + r.width / 2 - 80, r.y + 124, 160, 38), "Возродиться", stButton)) respawn();
        }
        static GUIStyle stDeathTitle;

        // ---------------- game UI (inventory / craft / chest / furnace) ----------------
        const float SLOT = 42;
        static readonly string[] ARMOR_LABELS = { "Шлем", "Нагрудник", "Поножи", "Ботинки" };
        static void drawGameUI(float W, float H)
        {
            fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.5f));
            float ww = Mathf.Min(1100, W - 24), wh = Mathf.Min(720, H - 24);
            var r = centerBox(W, H, ww, wh);
            fill(r, C(0x101c26, 0.98f)); frame(r, C(0xffffff, 0.2f));
            string title = uiMode == "chest" ? (chestGroupKeys(uiKey).Count > 1 ? "Большой сундук" : "Сундук") : uiMode == "furnace" ? "Печь" : player.creative ? "Creator — каталог" : uiMode == "craft" ? "Верстак 3×3" : "Инвентарь 2×2";
            GUI.Label(new Rect(r.x + 18, r.y + 12, r.width - 160, 30), title, stH2);
            if (GUI.Button(new Rect(r.xMax - 110, r.y + 12, 92, 30), "Закрыть", stButton)) { closeGameUI(); return; }
            var body = new Rect(r.x + 16, r.y + 52, r.width - 32, r.height - 66);
            if (uiMode == "chest") drawChestPanel(body);
            else if (uiMode == "furnace") drawFurnacePanel(body);
            else drawInventoryPanel(body, uiMode == "craft");
            handleSlotDragEnd();
            // cursor stack follows the mouse
            if (uiCursor != null)
            {
                var mp = Event.current.mousePosition;
                drawItemIcon(new Rect(mp.x - 16, mp.y - 16, 32, 32), uiCursor);
                if (uiCursor.count > 1) shadowLabel(new Rect(mp.x - 16, mp.y - 16, 34, 34), uiCursor.count.ToString(), stCount);
            }
        }
        static float inventoryGrid(Stack[] arr, float x, float y, int cols, bool hotRow, SlotOpts opts = null)
        {
            int rows = (arr.Length + cols - 1) / cols;
            for (int i = 0; i < arr.Length; i++)
            {
                int cx = i % cols, cy = i / cols;
                slotEl(arr, i, new Rect(x + cx * (SLOT + 4), y + cy * (SLOT + 4), SLOT, SLOT), hotRow && arr == inventory && i < 9, opts);
            }
            return rows * (SLOT + 4);
        }
        static void slotEl(Stack[] arr, int i, Rect r, bool hot, SlotOpts opts)
        {
            opts = opts ?? new SlotOpts();
            var e = Event.current;
            bool over = r.Contains(e.mousePosition);
            fill(r, hot ? C(0x203040) : C(0x0b1218)); frame(r, over ? C(0xffe36a, 0.8f) : C(0xffffff, 0.22f));
            var s = arr[i];
            drawItemIcon(new Rect(r.x + 5, r.y + 5, 32, 32), s);
            if (s != null && s.count > 1) shadowLabel(new Rect(r.x, r.y, r.width - 3, r.height - 1), s.count.ToString(), stCount);
            if (s != null && idef(s.key).maxDur != 0)
            {
                var d = idef(s.key); double frac = Math.Max(0, (s.dur != 0 ? s.dur : d.maxDur) / (double)d.maxDur);
                fill(new Rect(r.x + 4, r.yMax - 5, r.width - 8, 3), new Color(0, 0, 0, 0.6f));
                fill(new Rect(r.x + 4, r.yMax - 5, (r.width - 8) * (float)frac, 3), Color.Lerp(C(0xd84343), C(0x56d364), (float)frac));
            }
            if (over && s != null && uiCursor == null) GUI.Label(new Rect(r.x, r.y - 20, 300, 20), idef(s.key).name, stSmall);
            else if (over && s == null && opts.label != null && uiCursor == null) GUI.Label(new Rect(r.x, r.y - 20, 300, 20), opts.label, stSmall);
            if (!over) return;
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                if (slotRightClick(arr, i, opts)) { inventoryDirty = true; saveGameSoon(); }
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                dragArr = arr; dragIdx = i; dragStart = e.mousePosition; dragMoved = false; dragOpts = opts;
                if (e.clickCount == 2) { slotDoubleClick(arr, i); dragArr = null; }
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && dragArr != null)
            {
                if ((e.mousePosition - dragStart).sqrMagnitude > 36) dragMoved = true;
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && dragArr != null)
            {
                if (dragArr == arr && dragIdx == i && !dragMoved) slotLeftClick(arr, i, opts, e.shift);
                else if (dragMoved && !opts.single && !dragOpts.single && !(dragArr == arr && dragIdx == i))
                {
                    if (moveIntoSlot(dragArr, dragIdx, arr, i, opts.accept, opts.extractOnly)) { inventoryDirty = true; saveGameSoon(); }
                }
                dragArr = null; dragIdx = -1;
                e.Use();
            }
        }
        static void handleSlotDragEnd()
        {
            var e = Event.current;
            if (e.type == EventType.MouseUp && dragArr != null) { dragArr = null; dragIdx = -1; }
            if (e.type == EventType.MouseDrag && dragArr != null && (e.mousePosition - dragStart).sqrMagnitude > 36) dragMoved = true;
        }
        static void drawInventoryPanel(Rect body, bool advanced)
        {
            float half = body.width / 2 - 8;
            var a = new Rect(body.x, body.y, half, body.height);
            fill(a, C(0x0d1720, 0.9f));
            float x = a.x + 10, y = a.y + 8;
            GUI.Label(new Rect(x, y, a.width - 20, 22), "Инвентарь " + (uiCursor != null ? "· в курсоре: " + idef(uiCursor.key).name + " ×" + uiCursor.count : ""), stH3);
            y += 30;
            if (!player.creative)
            {
                for (int i = 0; i < 4; i++)
                {
                    int slot = i;
                    GUI.Label(new Rect(x + i * (SLOT + 26), y, SLOT + 20, 16), "<size=10>" + ARMOR_LABELS[i] + "</size>", stSmall);
                    slotEl(armor, i, new Rect(x + i * (SLOT + 26), y + 16, SLOT, SLOT), false, new SlotOpts { single = true, label = ARMOR_LABELS[i], accept = k => { var ai = idef(k).armor; return ai != null && ai.slot == slot; } });
                }
                GUI.Label(new Rect(x + 4 * (SLOT + 26), y, SLOT + 40, 16), "<size=10>Левая рука</size>", stSmall);
                slotEl(offhand, 0, new Rect(x + 4 * (SLOT + 26), y + 16, SLOT, SLOT), false, new SlotOpts { single = true, label = "Левая рука" });
                y += SLOT + 26;
            }
            inventoryGrid(inventory, x, y, 9, true);
            var b = new Rect(body.x + half + 16, body.y, half, body.height);
            fill(b, C(0x0d1720, 0.9f));
            x = b.x + 10; y = b.y + 8;
            if (player.creative)
            {
                GUI.Label(new Rect(x, y, b.width - 20, 22), "Все блоки и предметы", stH3); y += 24;
                GUI.Label(new Rect(x, y, b.width - 20, 20), "Клик заменяет выбранный слот хотбара. Предметы в Creator не расходуются.", stSmall); y += 24;
                var keysList = creativeKeys();
                int cols = Math.Max(1, (int)((b.width - 36) / 40));
                var view = new Rect(x, y, b.width - 20, b.yMax - y - 8);
                creativeScroll = GUI.BeginScrollView(view, creativeScroll, new Rect(0, 0, view.width - 20, ((keysList.Count + cols - 1) / cols) * 40));
                for (int i = 0; i < keysList.Count; i++)
                {
                    var k = keysList[i]; var rr = new Rect((i % cols) * 40, (i / cols) * 40, 36, 36);
                    fill(rr, C(0x0b1218)); drawItemIcon(new Rect(rr.x + 2, rr.y + 2, 32, 32), k);
                    if (rr.Contains(Event.current.mousePosition))
                    {
                        frame(rr, C(0xffe36a, 0.8f));
                        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                        {
                            var d = idef(k);
                            inventory[selected] = new Stack { key = k, count = d.stack != 0 ? d.stack : 1, dur = d.maxDur };
                            inventoryDirty = true; drawHotbar(); toast("Выбрано: " + d.name);
                            Event.current.Use();
                        }
                    }
                }
                GUI.EndScrollView();
                return;
            }
            GUI.Label(new Rect(x, y, b.width - 20, 22), advanced ? "Сетка 3×3 + книга рецептов" : "Сетка 2×2 + базовые рецепты", stH3); y += 30;
            int size = advanced ? 3 : 2; var arr = size == 3 ? craft3 : craft2;
            inventoryGrid(arr, x, y, size, false);
            float gx = x + size * (SLOT + 4) + 10;
            GUI.Label(new Rect(gx, y + size * (SLOT + 4) / 2 - 16, 30, 30), "<size=24>→</size>", stLabel);
            var rec = craftGridRecipe(arr, size);
            GUI.enabled = rec != null;
            if (GUI.Button(new Rect(gx + 34, y + size * (SLOT + 4) / 2 - 22, Mathf.Min(220, b.xMax - gx - 50), 44), rec != null ? rec.name : "Результат", stButton) && rec != null)
            {
                int left = addItem(rec.outKey, rec.outCount);
                if (left != 0) toast("Не хватает места");
                else { consumeCraftGrid(arr, rec); saveGameSoon(); drawHotbar(); }
            }
            GUI.enabled = true;
            y += size * (SLOT + 4) + 12;
            if (GUI.Button(new Rect(x, y, 240, 30), recipeBookOpen ? "Закрыть книгу рецептов" : "Книга рецептов", stButton)) recipeBookOpen = !recipeBookOpen;
            y += 36;
            if (recipeBookOpen) drawRecipeBook(new Rect(x, y, b.width - 20, b.yMax - y - 8), advanced);
        }
        static string recipeKeyLabel(string k)
        {
            return k == "@log" ? "Любое бревно" : k == "@planks" ? "Любые доски" : k == "@stonecraft" ? "Булыжник / глубинный булыжник / чернит" : k.StartsWith("@wood:", StringComparison.Ordinal) ? "Бревно: " + k.Substring(6)
                : k == "@red_dye_source" ? "Мак или красный тюльпан" : k == "@light_gray_dye_source" ? "Светло-серый цветок" : k == "@white_dye_source" ? "Костная мука или ландыш" : k == "wool" || k == "@wool" ? "Шерсть" : idef(k).name;
        }
        static string recipeNeedTextUI(Recipe rec) { return string.Join(", ", rec.need.Select(kv => recipeKeyLabel(kv.Key) + " ×" + kv.Value)); }
        static void drawRecipeBook(Rect area, bool advanced)
        {
            GUI.SetNextControlName("recipeSearch");
            recipeBookSearch = GUI.TextField(new Rect(area.x, area.y, area.width - 190, 28), recipeBookSearch, stInput);
            if (string.IsNullOrEmpty(recipeBookSearch) && GUI.GetNameOfFocusedControl() != "recipeSearch") GUI.Label(new Rect(area.x + 6, area.y + 5, 200, 20), "Поиск рецепта…", stSmall);
            if (GUI.Button(new Rect(area.xMax - 182, area.y, 182, 28), recipeBookAvailableOnly ? "Показаны: доступные" : "Показаны: все", stButton)) recipeBookAvailableOnly = !recipeBookAvailableOnly;
            var view = new Rect(area.x, area.y + 34, area.width, area.height - 34);
            string query = recipeBookSearch.Trim().ToLowerInvariant();
            var shown = new List<Recipe>();
            foreach (var rec in RECIPES)
            {
                if (!advanced && !rec.basic) continue;
                var d = idef(rec.outKey);
                string hay = (rec.name + " " + d.name + " " + recipeNeedTextUI(rec)).ToLowerInvariant();
                if (query.Length > 0 && !hay.Contains(query)) continue;
                bool ok = canCraft(rec);
                if (recipeBookAvailableOnly && !ok && !player.creative) continue;
                shown.Add(rec);
            }
            float rowH = 52;
            recipeScroll = GUI.BeginScrollView(view, recipeScroll, new Rect(0, 0, view.width - 20, Math.Max(1, shown.Count) * (rowH + 4)));
            if (shown.Count == 0) GUI.Label(new Rect(6, 6, view.width - 30, 22), "Нет подходящих рецептов", stSmall);
            float y = 0;
            foreach (var rec in shown)
            {
                bool ok = canCraft(rec);
                var row = new Rect(0, y, view.width - 24, rowH); fill(row, ok || player.creative ? C(0x15232f) : C(0x12191f));
                drawItemIcon(new Rect(row.x + 8, row.y + 10, 32, 32), rec.outKey);
                GUI.Label(new Rect(row.x + 48, row.y + 4, row.width - 160, 22), "<b>" + rec.name + "</b>", stLabel);
                GUI.Label(new Rect(row.x + 48, row.y + 24, row.width - 160, 26), recipeNeedTextUI(rec), stSmall);
                GUI.enabled = ok || player.creative;
                if (GUI.Button(new Rect(row.xMax - 100, row.y + 10, 92, 32), player.creative ? "Взять" : "Создать", stButton)) craft(rec);
                GUI.enabled = true;
                y += rowH + 4;
            }
            GUI.EndScrollView();
        }
        static void drawChestPanel(Rect body)
        {
            var keysList = chestGroupKeys(uiKey);
            float half = body.width / 2 - 8;
            var a = new Rect(body.x, body.y, half, body.height); fill(a, C(0x0d1720, 0.9f));
            float x = a.x + 10, y = a.y + 8;
            GUI.Label(new Rect(x, y, a.width, 22), keysList.Count > 1 ? "Большой сундук · 54" : "Сундук · 27", stH3); y += 30;
            foreach (var k in keysList) y += inventoryGrid(getChest(k), x, y, 9, false) + 6;
            var b = new Rect(body.x + half + 16, body.y, half, body.height); fill(b, C(0x0d1720, 0.9f));
            GUI.Label(new Rect(b.x + 10, b.y + 8, b.width, 22), "Инвентарь", stH3);
            inventoryGrid(inventory, b.x + 10, b.y + 38, 9, true);
        }
        static void drawFurnacePanel(Rect body)
        {
            var f = getFurnace(uiKey);
            float half = body.width / 2 - 8;
            var a = new Rect(body.x, body.y, half, body.height); fill(a, C(0x0d1720, 0.9f));
            float x = a.x + 10, y = a.y + 8;
            GUI.Label(new Rect(x, y, a.width, 22), "Плавка", stH3); y += 26;
            GUI.Label(new Rect(x, y, a.width - 20, 40), "Сырьё принимает только рецепты плавки, топливо — только горючее, результат нельзя заполнить вручную.", stSmall); y += 46;
            slotEl(f.slots, 0, new Rect(x, y, SLOT, SLOT), false, new SlotOpts { accept = k => SMELT.ContainsKey(k) });
            GUI.Label(new Rect(x + SLOT + 6, y, 50, SLOT), "🔥\n<size=11>" + Math.Ceiling(f.burn) + "с</size>", stCenter);
            slotEl(f.slots, 1, new Rect(x + SLOT + 60, y, SLOT, SLOT), false, new SlotOpts { accept = k => fuelValue(new Stack { key = k, count = 1 }) > 0 });
            GUI.Label(new Rect(x + 2 * SLOT + 66, y, 30, SLOT), "<size=22>→</size>", stCenter);
            slotEl(f.slots, 2, new Rect(x + 2 * SLOT + 100, y, SLOT, SLOT), false, new SlotOpts { extractOnly = true });
            y += SLOT + 12;
            drawBar(new Rect(x, y, 3 * SLOT + 100, 8), Math.Min(1, f.progress), C(0xffa53d), null);
            var b = new Rect(body.x + half + 16, body.y, half, body.height); fill(b, C(0x0d1720, 0.9f));
            GUI.Label(new Rect(b.x + 10, b.y + 8, b.width, 22), "Инвентарь", stH3);
            inventoryGrid(inventory, b.x + 10, b.y + 38, 9, true);
        }
    }
}
