// Port of ui.js i18n and item helpers (12498-12560): the two-language string table STR (en / ru), the
// language picked from the system language (Russian or Ukrainian give 'ru'), the mutable current
// language CUR_LANG (Settings.Load and the settings language button write it; game.js reads it for biome
// names and the debug text), the lookup T(key) with the en / key fallback, and the item name and stack
// size helpers over the block and item tables. The mode and difficulty strings ('creative', 'survival',
// 'peaceful', 'easy', 'normal', 'hard') double as keys; importDone holds '%s'.
//
// API skeleton: bodies marked TODO(ui) are filled in by that implementer.
using System;
using System.Collections.Generic;
using UnityEngine;
using static Maincraft.Blocks;

namespace Maincraft
{
    public static class Lang
    {
        /// <summary>LANG (12502): the initial language, 'ru' when the system language is Russian or Ukrainian, else 'en'.</summary>
        public static readonly string LANG = Application.systemLanguage == SystemLanguage.Russian || Application.systemLanguage == SystemLanguage.Ukrainian ? "ru" : "en";
        /// <summary>STR (12503-12552): language -> key -> text (about 110 keys, the same in both languages; port verbatim, typography included).</summary>
        public static readonly Dictionary<string, Dictionary<string, string>> STR = new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string>(),   // TODO(ui): fill (12504-12527)
            ["ru"] = new Dictionary<string, string>(),   // TODO(ui): fill (12528-12551)
        };
        /// <summary>CUR_LANG (12553): 'ru' | 'en'.</summary>
        public static string CurLang = LANG;

        // ui.js T (12554): STR[CUR_LANG][k] || STR.en[k] || k (empty strings fall through)
        public static string T(string k) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js itemName (12555-12559): '' for 0; items: the def's ru / en name ('?' without a def); blocks: B_RU / B_EN
        public static string ItemName(int id) { throw new NotImplementedException(); } // TODO(ui)
        // ui.js maxStack (12560): items: the def's stack size (64 without a def); every block 64
        public static int MaxStack(int id) { throw new NotImplementedException(); } // TODO(ui)
    }
}
