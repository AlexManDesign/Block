// Port of menus.js (15206-15728) and the DOM helpers of ui.js (12650-12698): the whole user interface.
// Title, world list (select, double-click to play, delete with a confirmation, export / import of world
// files), create world (seed parsing / hashing, mode, difficulty), pause, death, benchmark screens, the
// settings screen (sliders and toggles written straight into Settings, a column remesh on leaves / sway
// changes, a live language switch), the HUD (hotbar, held-item name fade, heart / food / air rows), the
// inventories (player 2x2 craft, crafting table, chests, furnaces, the creative catalog with search and
// trash; Minecraft click semantics) and the touch controls. UI is also the API game.js calls.
//
// DOM -> UI Toolkit: the static page of game.html (ids and classes kept; BuildDom) is built in code under
// one UIDocument, styled by a USS converted from the CSS (Resources/UI). $(id) is ById, show(id, v)
// toggles the 'hidden' class (display: none) like the JS. Events: clicks and PointerDownEvent (inventory
// slots act on pointer down with button and shift like the JS mousedown handlers). The document-level
// handlers (mousemove for the cursor item, the touch handlers and the 50 ms hold poller of buildTouch)
// become Tick, called once per frame by the bootstrap. setTimeout is Game.Later; confirm / alert are a
// modal panel (Confirm / Alert); the hidden file input of the import button is replaced by a file path
// (a platform picker plugin or the persistentDataPath/imports folder).
//
// Pointer lock: Resume only locks the cursor (Cursor.lockState); Game.PollInput sees the lock change and
// runs the pointerlockchange branch (unpause, hide pause) as the browser event did.
//
// API skeleton: bodies marked TODO(ui) are filled in by that implementer.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Maincraft.Blocks;

namespace Maincraft
{
    /// <summary>UI.invKind: the JS strings 'inv' | 'craft3' | 'chest' | 'furnace' | 'creative'; None is null (also: the default by game mode in OpenInventory).</summary>
    public enum InvKind { None, Inv, Craft3, Chest, Furnace, Creative }

    /// <summary>UI.furnaceEls: the open furnace's flame and progress bars.</summary>
    public sealed class FurnaceEls
    {
        public VisualElement flame, prog;
    }

    /// <summary>UI.icons: the 9x9 pixel-art HUD icons (buildHud).</summary>
    public sealed class HudIcons
    {
        public Texture2D heart, heartE, food, foodE, air;
        /// <summary>The hearts with the CSS poison tint (hue-rotate(75deg) saturate(.8)) prebuilt.</summary>
        public Texture2D heartPoison, heartEPoison;
    }

    /// <summary>A look touch of buildTouch (the JS looks map entries).</summary>
    public sealed class TouchLook
    {
        public double x, y, sx, sy;         // last and start position (CSS pixels, y down)
        public double t;                    // start time (performance.now ms)
        public bool moved, held;
    }

    public static class UI
    {
        // ================================================================== state (menus.js 15210-15216 and the fields set later)
        public static Game game = null;
        /// <summary>The inventory window is open.</summary>
        public static bool invOpen = false;
        /// <summary>The stack held by the mouse in inventory screens (moved by reference like the JS).</summary>
        public static ItemStack cursor = null;
        public static InvKind invKind = InvKind.None;
        /// <summary>The crafting grid: 9 cells for craft3, 4 for every other kind; null while closed.</summary>
        public static ItemStack[] craftGrid = null;
        public static WorldMeta selectedWorld = null;
        /// <summary>Mode and difficulty of the world being created (the JS strings; T(GameEnums.ModeName(newMode)) etc.).</summary>
        public static GameMode newMode = GameMode.Creative;
        public static Difficulty newDiff = Difficulty.Normal;
        /// <summary>Where the settings' Done returns to: 'title' | 'pause'.</summary>
        public static string settingsFrom;
        /// <summary>The Later id of the timer hiding #hint after 20 s (0: none).</summary>
        public static int hintT;
        /// <summary>The 9 hotbar slot elements (null before BuildHud: UpdateHotbar returns early).</summary>
        public static VisualElement[] hotSlots;
        public static HudIcons icons;
        /// <summary>`${ceil(hp)}|${food}|${ceil(air/30)}|${poisonT>0}` of the last survival-bar rebuild.</summary>
        public static string lastSurvKey;
        /// <summary>The creative tab 'b' | 'c' | 'n' | 'f' | 't' | 's' ('\0' before the first opening; persists).</summary>
        public static char tab;
        /// <summary>The creative search text (persists).</summary>
        public static string searchText;
        /// <summary>creativeCatalog(), built lazily: tab -> ids.</summary>
        public static Dictionary<char, List<int>> catalog;
        public static VisualElement catalogEl;
        /// <summary>The open chest's key "x,y,z" in game.chests (null: none).</summary>
        public static string chestPos;
        /// <summary>The open chest's slots (usually the live array of game.chests[chestPos]).</summary>
        public static ItemStack[] chestItems;
        /// <summary>The open furnace's key in meta.furnaces (furnace itself stays set after closing).</summary>
        public static string furnacePos;
        /// <summary>The open furnace: the live meta.furnaces record (Game.TickFurnaces compares it by reference).</summary>
        public static FurnaceRec furnace;
        public static FurnaceEls furnaceEls;
        /// <summary>Touch mode: a tap attacks / breaks (true) or uses / places (false).</summary>
        public static bool touchBreak;
        /// <summary>performance.now() of the last jump tap (double tap toggles flying in creative).</summary>
        public static double lastJumpTap;
        // buildTouch's handler state (the JS closure variables), kept for the per-frame touch polling
        public static int joyId = -1;       // the joystick's finger (null: -1)
        public static double jx, jy;        // joystick centre
        public static readonly Dictionary<int, TouchLook> looks = new Dictionary<int, TouchLook>();

        // ================================================================== DOM equivalents (ui.js 12650-12698)
        public static UIDocument doc;
        /// <summary>document.body: the root of the page (game.js appends its toast to it, toggles the 'nohud' / 'touch' classes on it).</summary>
        public static VisualElement root;

        // game.html (lines 7-176): the static page (ids, classes, data-t / data-ph keys) under root and the USS;
        // called by Init before anything looks an element up
        public static void BuildDom() { throw new NotImplementedException(); } // TODO(ui)
        // ui.js $ (12663): document.getElementById (null when missing)
        public static VisualElement ById(string id) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js show (12664): toggles the 'hidden' class; a missing element is ignored
        public static void Show(string id, bool v) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js show (12664) for an element
        public static void Show(VisualElement e, bool v) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js el (12652-12662): an element of the tag's equivalent ('div' / 'i' VisualElement, 'span' / 'b' /
        // 'small' / 'label' Label, 'button' Button, 'input' TextField, 'img' Image) with class names; kids are
        // elements or strings / numbers (text), null skipped
        public static VisualElement El(string tag, string cls, params object[] kids) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js slotEl (12666-12670): a 'slot' (+ cls) element filled by SetSlot
        public static VisualElement SlotEl(ItemStack item, string cls = null) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js setSlot (12671-12683): icon, count label (count > 1), durability bar (dur set and maxDur), tooltip name
        public static void SetSlot(VisualElement s, ItemStack item) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js creativeCatalog (12686-12698): ids per creative tab b / c / n / f / t
        public static Dictionary<char, List<int>> CreativeCatalog() { throw new NotImplementedException(); } // TODO(ui)
        // window.confirm: a modal question; onYes runs when it is accepted
        public static void Confirm(string text, Action onYes) { throw new NotImplementedException(); } // TODO(ui)
        // window.alert: a modal message
        public static void Alert(string text) { throw new NotImplementedException(); } // TODO(ui)

        // ================================================================== menus.js
        // menus.js init (15218-15228): BuildDom, buildHud, bindMenus, buildTouch on touch devices, applyLang
        public static void Init(Game g) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js document mousemove (15224-15227), the touch handlers and the hold poller of buildTouch
        // (15666-15710), the #heldName fade: once per frame
        public static void Tick() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js applyLang (15229-15233): every data-t text and data-ph placeholder through T; #hint
        public static void ApplyLang() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js showTitle (15236-15240)
        public static void ShowTitle() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js showWorlds (15241-15261): the world list from WorldDb.ListWorlds
        public static void ShowWorlds() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js play (15262-15267): Sfx.Init, Sfx.enabled = Settings.sound, the HUD, game.StartWorld(w)
        public static void Play(WorldMeta w) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js bindMenus (15268-15334): every static button's handler
        public static void BindMenus() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js updateModeBtn (15335)
        public static void UpdateModeBtn() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js updateDiffBtn (15336-15339)
        public static void UpdateDiffBtn() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js resume (15340-15344): touch: unpause; desktop: lock the cursor (Game.PollInput runs the
        // pointerlockchange branch) and hide #clickToPlay
        public static void Resume() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js onWorldReady (15345-15350)
        public static void OnWorldReady() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js showPause (15351)
        public static void ShowPause() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js hidePause (15352)
        public static void HidePause() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js benchStatus (15354): the benchmark progress line, shown while text is not empty (null hides)
        public static void BenchStatus(string text) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js showBench (15355): the benchmark result window
        public static void ShowBench(string text) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js showDeath (15356)
        public static void ShowDeath() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js hideDeath (15357)
        public static void HideDeath() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js showSettings (15360-15396): from 'title' | 'pause'
        public static void ShowSettings(string from) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js buildHud (15399-15415): the 9 hotbar slots and the pixel icons
        public static void BuildHud() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js pixelIcon (15416-15421): 9x9, '#' pixels in col (hi where y < 3 and x < 4); col / hi CSS colours
        public static Texture2D PixelIcon(string[] rows, Color32 col, Color32 hi) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js updateHotbar (15422-15427)
        public static void UpdateHotbar() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js showHeldName (15428-15433): the selected item's name, its fade restarted
        public static void ShowHeldName() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js updateSurvival (15434-15460): hearts, food and air rows (rebuilt only when lastSurvKey changes)
        public static void UpdateSurvival() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js openInventory (15463-15474): kind None = creative or inv by game mode
        public static void OpenInventory(InvKind kind = InvKind.None) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js closeInventory (15475-15489)
        public static void CloseInventory(bool silent = false) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js openChest (15490-15495)
        public static void OpenChest(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js openFurnace (15496-15502): gets or creates (and stores) meta.furnaces["x,y,z"]
        public static void OpenFurnace(int x, int y, int z) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js saveChest (15503-15506)
        public static void SaveChest() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js updateCursor (15507-15510)
        public static void UpdateCursor() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js clickSlot (15512-15540): Minecraft slot click; button 0 left, 2 right
        public static void ClickSlot(ItemStack[] arr, int i, int button, bool shift) { throw new NotImplementedException(); } // TODO(ui)
        // menus.js refreshInventory (15541-15627): rebuilds #invBody for invKind
        public static void RefreshInventory() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js fillCatalog (15628-15655)
        public static void FillCatalog() { throw new NotImplementedException(); } // TODO(ui)
        // menus.js buildTouch (15658-15727): the 'touch' class, game.touch, the joystick and buttons (their
        // per-frame handling is in Tick)
        public static void BuildTouch() { throw new NotImplementedException(); } // TODO(ui)
    }
}
