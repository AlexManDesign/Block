// Port of game.js Settings (12704-12716): the user settings with their defaults (touch devices get lighter
// ones), persisted as JSON (localStorage key 'maincraft.settings.v1' in the original; a file under
// Application.persistentDataPath here).
using System;
using System.Collections.Generic;

namespace Maincraft
{
    public static class Settings
    {
        public const string SETTINGS_KEY = "maincraft.settings.v1";

        public static int renderDist = Game.IS_TOUCH ? 5 : 8;   // chunks (clamped to 27 in use, 24 at boot)
        public static double fov = 70, sens = 1, bright = 0.5;
        public static bool clouds = true;
        public static int leaves = Game.IS_TOUCH ? 0 : 3;       // 0 fast, 1 optimized, 2 fancy, 3 adaptive (leafLod)
        public static bool sway = true, bobView = true;
        public static double scale = Game.IS_TOUCH ? 0.75 : 1;
        public static bool autoScale = Game.IS_TOUCH, fps = false, sound = true;
        public static string lang = "";                          // '' = automatic (navigator language)
        /// <summary>Legacy on/off switch of older saves; migrated by Load (false -> leaves 0) and dropped.</summary>
        public static bool? fancyLeaves;
        /// <summary>Keys of the stored JSON this port does not know: Save writes every key it loaded (Object.assign).</summary>
        public static Dictionary<string, object> extra;

        // game.js Settings.load (12709-12714): Object.assign from the stored JSON (errors swallowed), the
        // fancyLeaves migration, and a set lang overrides Lang.CurLang
        public static void Load() { throw new NotImplementedException(); } // TODO(game-a)
        // game.js Settings.save (12715): every setting (and the unknown keys) to the stored JSON (errors swallowed)
        public static void Save() { throw new NotImplementedException(); } // TODO(game-a)
    }
}
