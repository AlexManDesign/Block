// Voxel Forge — Unity port. Keyboard/mouse/pointer-lock and touch input mapped onto the reference
// DOM event handlers (keys are tracked by KeyboardEvent.code names).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelForge
{
    public static partial class VF
    {
        static readonly HashSet<string> keys = new HashSet<string>();
        public static bool keyDown(string code) { return keys.Contains(code); }
        public static bool locked = false, sprintLatch = false;
        public static int selected = 0;
        static double lastWAt = 0;
        public static bool settingsOpen = false, modsOpen = false;
        public const double MOUSE_SENS = 0.00215;
        static readonly KeyValuePair<KeyCode, string>[] KEYMAP =
        {
            new KeyValuePair<KeyCode, string>(KeyCode.W, "KeyW"), new KeyValuePair<KeyCode, string>(KeyCode.A, "KeyA"), new KeyValuePair<KeyCode, string>(KeyCode.S, "KeyS"),
            new KeyValuePair<KeyCode, string>(KeyCode.D, "KeyD"), new KeyValuePair<KeyCode, string>(KeyCode.E, "KeyE"), new KeyValuePair<KeyCode, string>(KeyCode.Q, "KeyQ"),
            new KeyValuePair<KeyCode, string>(KeyCode.F, "KeyF"), new KeyValuePair<KeyCode, string>(KeyCode.X, "KeyX"), new KeyValuePair<KeyCode, string>(KeyCode.Space, "Space"),
            new KeyValuePair<KeyCode, string>(KeyCode.LeftShift, "ShiftLeft"), new KeyValuePair<KeyCode, string>(KeyCode.RightShift, "ShiftRight"),
            new KeyValuePair<KeyCode, string>(KeyCode.LeftControl, "ControlLeft"), new KeyValuePair<KeyCode, string>(KeyCode.RightControl, "ControlRight"),
            new KeyValuePair<KeyCode, string>(KeyCode.Escape, "Escape"), new KeyValuePair<KeyCode, string>(KeyCode.F5, "F5"),
            new KeyValuePair<KeyCode, string>(KeyCode.LeftBracket, "BracketLeft"), new KeyValuePair<KeyCode, string>(KeyCode.RightBracket, "BracketRight"),
            new KeyValuePair<KeyCode, string>(KeyCode.UpArrow, "ArrowUp"), new KeyValuePair<KeyCode, string>(KeyCode.DownArrow, "ArrowDown"),
            new KeyValuePair<KeyCode, string>(KeyCode.LeftArrow, "ArrowLeft"), new KeyValuePair<KeyCode, string>(KeyCode.RightArrow, "ArrowRight"),
            new KeyValuePair<KeyCode, string>(KeyCode.Alpha1, "Digit1"), new KeyValuePair<KeyCode, string>(KeyCode.Alpha2, "Digit2"), new KeyValuePair<KeyCode, string>(KeyCode.Alpha3, "Digit3"),
            new KeyValuePair<KeyCode, string>(KeyCode.Alpha4, "Digit4"), new KeyValuePair<KeyCode, string>(KeyCode.Alpha5, "Digit5"), new KeyValuePair<KeyCode, string>(KeyCode.Alpha6, "Digit6"),
            new KeyValuePair<KeyCode, string>(KeyCode.Alpha7, "Digit7"), new KeyValuePair<KeyCode, string>(KeyCode.Alpha8, "Digit8"), new KeyValuePair<KeyCode, string>(KeyCode.Alpha9, "Digit9"),
        };

        public static void clearInputState()
        {
            keys.Clear();
            sprintLatch = false;
            mouseL = false;
            cancelBowCharge();
            resetMining();
            resetTouchMove();
        }
        // Same contract as original main.js nc(): pointer-lock failure is intentionally swallowed.
        public static bool requestGamePointerLock()
        {
            if (TOUCH_DEVICE) return false;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            if (!locked) { locked = true; onPointerLockChange(); }
            return true;
        }
        public static void releasePointerLock(bool raise = true)
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (locked) { locked = false; if (raise) onPointerLockChange(); }
        }
        static void onPointerLockChange()
        {
            if (!locked) clearInputState();
            if (!TOUCH_DEVICE && started && worldReady && !locked && !uiOpen && !player.dead && !pauseOpen && !player.sleeping) showPause();
            else if (locked) hidePause();
        }
        public static void showPause()
        {
            if (!started || uiOpen || player.dead || player.sleeping) return;
            clearInputState();
            pauseOpen = true;
            if (locked) releasePointerLock(false);
            syncAudioState();
            syncTouchControls();
        }
        public static void hidePause() { pauseOpen = false; syncAudioState(); syncTouchControls(); }
        public static void openGameUI(string mode, string key = null)
        {
            clearInputState();
            uiOpen = true; pauseOpen = false; uiMode = mode; uiKey = key;
            GUIUtility.keyboardControl = 0; // rebuilt DOM: no input keeps focus
            releasePointerLock();
            syncTouchControls();
            renderCurrentUI();
        }
        public static void closeGameUI()
        {
            if (uiCursor != null)
            {
                int left = addItem(uiCursor.key, uiCursor.count, uiCursor.dur);
                if (left != 0) spawnWorldDrop(uiCursor.key, left, player.x, player.y + 1, player.z);
                uiCursor = null;
            }
            uiOpen = false; GUIUtility.keyboardControl = 0;
            saveGameSoon();
            drawHotbar();
            if (started && !player.dead)
            {
                toast(TOUCH_DEVICE ? "Управление возвращено" : "Кликни по миру, чтобы вернуть управление");
                syncTouchControls();
            }
        }
        /// <summary>IMGUI redraws every frame; kept for call-site parity.</summary>
        public static void renderCurrentUI() { }
        public static void renderSettingsUI() { }
        public static void renderModsUI() { }
        public static void drawHotbar() { syncAttackSelection(JS.now()); updateResourceHUD(); updateVitals(); }

        static void onKeyDown(string code)
        {
            initAudio();
            if (player.sleeping)
            {
                if (code == "Escape") wakeFromSleep();
                return;
            }
            if (code == "Escape" && started)
            {
                clearInputState();
                if (settingsOpen) settingsOpen = false;
                else if (modsOpen) modsOpen = false;
                else if (uiOpen) closeGameUI();
                else showPause();
                return;
            }
            keys.Add(code);
            if (code == "KeyW" || code == "ArrowUp")
            {
                double t = JS.now();
                if (t - lastWAt < 300) sprintLatch = true;
                lastWAt = t;
            }
            if (code.StartsWith("Digit", StringComparison.Ordinal))
            {
                int n = code[5] - '0';
                if (n >= 1 && n <= 9) { cancelBowCharge(); selected = n - 1; drawHotbar(); }
            }
            if (code == "F5" && started && !pauseOpen && !uiOpen && !player.dead) { cycleMainCamera(); return; }
            if (code == "KeyF" && started && !pauseOpen && !uiOpen && !player.dead && player.creative)
            {
                player.flying = !player.flying; player.vy = 0;
                toast(player.flying ? "Полёт включён" : "Полёт выключен");
            }
            if (code == "KeyX" && started && !pauseOpen && !uiOpen && !player.dead) toggleXray();
            if (code == "BracketLeft" || code == "BracketRight")
            {
                var vals = new List<int>(Array.FindAll(MAIN_RD_VALUES, x => x <= DEVICE_RD_CAP));
                int i = Math.Max(0, vals.IndexOf(renderDistance)), ni = Math.Max(0, Math.Min(vals.Count - 1, i + (code == "BracketRight" ? 1 : -1)));
                applyRenderDistanceSetting(vals[ni]);
            }
            if (code == "KeyE" && started)
            {
                if (player.dead || pauseOpen) return;
                if (uiOpen) closeGameUI(); else openGameUI("inventory");
            }
            if (code == "KeyQ" && started && !pauseOpen && !uiOpen && !player.dead && selectedStack() != null)
            {
                var st = selectedStack();
                toast("Выброшено: " + idef(st.key).name);
                throwWorldDrop(st.key, 1, st.dur);
                if (!player.creative) { st.count--; if (st.count <= 0) inventory[selected] = null; }
                inventoryDirty = true;
                drawHotbar();
                saveGameSoon();
            }
        }
        static void onKeyUp(string code)
        {
            keys.Remove(code);
            if (code == "KeyW" || code == "ArrowUp") sprintLatch = false;
        }
        /// <summary>Polls Unity input and dispatches the reference handlers. Called once per frame before simulation.</summary>
        public static void pollInput(bool textFieldFocused)
        {
            if (locked && Cursor.lockState != CursorLockMode.Locked) { locked = false; onPointerLockChange(); }
            // While a text field has focus only typing is suppressed: keyup and Escape always reach the window handlers.
            foreach (var kv in KEYMAP)
            {
                if (Input.GetKeyDown(kv.Key) && (!textFieldFocused || kv.Key == KeyCode.Escape))
                {
                    if (textFieldFocused) { GUIUtility.keyboardControl = 0; uiTextFocused = false; }
                    onKeyDown(kv.Value);
                }
                if (Input.GetKeyUp(kv.Key)) onKeyUp(kv.Value);
            }
            if (TOUCH_DEVICE) { pollTouch(); return; }
            if (locked && !player.sleeping)
            {
                double ms = MOUSE_SENS * gameSettings.sensitivity;
                double mx = Input.GetAxisRaw("Mouse X") / 0.1, my = -Input.GetAxisRaw("Mouse Y") / 0.1;
                player.yaw += mx * ms;
                player.pitch = Math.Max(-1.52, Math.Min(1.52, player.pitch + my * ms));
            }
            bool canvasEvents = started && worldReady && !uiOpen && !pauseOpen && !player.dead && !player.sleeping;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                initAudio();
                if (canvasEvents)
                {
                    // Original desktop flow: first press only enters pointer lock. Do not mine on that same press.
                    if (!locked) requestGamePointerLock();
                    else if (Input.GetMouseButtonDown(0)) { mouseL = true; primaryAction(); }
                    else withPlayerEdit(() => placeBlock());
                }
            }
            if (Input.GetMouseButtonUp(0))
            {
                if (bowCharging) releaseBowShot();
                mouseL = false;
                resetMining();
            }
            float wheel = Input.mouseScrollDelta.y;
            if (locked && wheel != 0)
            {
                cancelBowCharge();
                selected = (selected + (wheel < 0 ? 1 : 8)) % 9;
                drawHotbar();
            }
        }
        public static void onFocusLost() { clearInputState(); }

        // ---------------- touch ----------------
        public static readonly bool TOUCH_DEVICE = Application.isMobilePlatform && Input.touchSupported;
        public static bool touchActive = false;
        static int touchLookId = -1, touchStickId = -1;
        static Vector2 touchLookPos;
        public static double touchStickX = 0, touchStickY = 0;
        static bool touchGameplayEnabled() { return TOUCH_DEVICE && started && worldReady && !uiOpen && !pauseOpen && !player.dead && !player.sleeping; }
        public static void syncTouchControls()
        {
            bool on = TOUCH_DEVICE && started && worldReady && !uiOpen && !pauseOpen && !player.dead && !player.sleeping;
            touchActive = on;
            if (!on) resetTouchMove();
        }
        static void resetTouchMove()
        {
            touchStickId = -1; touchStickX = touchStickY = 0;
            keys.Remove("KeyW"); keys.Remove("KeyA"); keys.Remove("KeyS"); keys.Remove("KeyD"); keys.Remove("Space"); keys.Remove("ShiftLeft");
            sprintLatch = false;
        }
        // Layout (screen pixels, y down): stick bottom-left, buttons bottom-right; computed by the UI.
        public static Rect touchStickRect, touchAttackRect, touchUseRect, touchJumpRect, touchSneakRect, touchInvRect, touchPauseRect, touchPrevRect, touchNextRect;
        static readonly Dictionary<int, string> touchHold = new Dictionary<int, string>();
        // set when a touch opened the inventory/pause (pointerdown preventDefault): IMGUI drops the simulated mouse events until all touches end
        public static bool touchSwallowGUI = false;
        static void setTouchStick(Vector2 p)
        {
            var r = touchStickRect; double cx = r.center.x, cy = r.center.y, rad = r.width * 0.38, dx = p.x - cx, dy = p.y - cy, d = JS.hypot(dx, dy); if (d == 0) d = 1;
            double m = Math.Min(1, rad / d); dx *= m; dy *= m;
            touchStickX = dx / rad; touchStickY = dy / rad;
            Action<string, bool> k = (c, on) => { if (on) keys.Add(c); else keys.Remove(c); };
            k("KeyW", touchStickY < -0.22); k("KeyS", touchStickY > 0.22); k("KeyA", touchStickX < -0.22); k("KeyD", touchStickX > 0.22);
            sprintLatch = touchStickY < -0.88;
        }
        static void pollTouch()
        {
            if (Input.touchCount == 0) touchSwallowGUI = false;
            for (int ti = 0; ti < Input.touchCount; ti++)
            {
                var t = Input.GetTouch(ti);
                var p = new Vector2(t.position.x, Screen.height - t.position.y);
                if (t.phase == TouchPhase.Began)
                {
                    if (!touchGameplayEnabled()) continue;
                    if (touchStickRect.Contains(p)) { touchStickId = t.fingerId; setTouchStick(p); }
                    else if (touchAttackRect.Contains(p)) { touchHold[t.fingerId] = "attack"; mouseL = true; primaryAction(); }
                    else if (touchUseRect.Contains(p)) withPlayerEdit(() => placeBlock());
                    else if (touchJumpRect.Contains(p)) { touchHold[t.fingerId] = "Space"; keys.Add("Space"); }
                    else if (touchSneakRect.Contains(p)) { touchHold[t.fingerId] = "ShiftLeft"; keys.Add("ShiftLeft"); }
                    else if (touchInvRect.Contains(p)) { touchSwallowGUI = true; openGameUI("inventory"); syncTouchControls(); }
                    else if (touchPauseRect.Contains(p)) { touchSwallowGUI = true; showPause(); syncTouchControls(); }
                    else if (touchPrevRect.Contains(p)) { cancelBowCharge(); selected = (selected + 8) % 9; drawHotbar(); }
                    else if (touchNextRect.Contains(p)) { cancelBowCharge(); selected = (selected + 1) % 9; drawHotbar(); }
                    else if (p.x >= Screen.width * 0.38f) { touchLookId = t.fingerId; touchLookPos = p; }
                }
                else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                {
                    if (t.fingerId == touchStickId) setTouchStick(p);
                    else if (t.fingerId == touchLookId && touchGameplayEnabled())
                    {
                        double dx = p.x - touchLookPos.x, dy = p.y - touchLookPos.y; touchLookPos = p;
                        double ms = 0.006 * gameSettings.sensitivity;
                        player.yaw += dx * ms;
                        player.pitch = Math.Max(-1.52, Math.Min(1.52, player.pitch + dy * ms));
                    }
                }
                else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    if (t.fingerId == touchStickId) resetTouchMove();
                    if (t.fingerId == touchLookId) touchLookId = -1;
                    string h;
                    if (touchHold.TryGetValue(t.fingerId, out h))
                    {
                        touchHold.Remove(t.fingerId);
                        if (h == "attack") { if (bowCharging) releaseBowShot(); mouseL = false; resetMining(); }
                        else keys.Remove(h);
                    }
                }
            }
        }
    }
}
