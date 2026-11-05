using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace GwentAccess
{
    /// <summary>
    /// The mod's own words in the player's language (gettext style, as Hearthstone Access and Accessible Arena do):
    /// every English line the mod authors goes through <see cref="T"/>, keyed by the English text itself, and is
    /// looked up in <c>lang\&lt;code&gt;.json</c> beside the module (BepInEx\plugins\&lt;Mod&gt;\lang, deployed from
    /// assets/lang). The language is the game's own (<c>LocalizationManager.CurrentLanguage</c>, "ru-ru", "pt-br"…)
    /// unless the config's Speech.Language names another. A missing file or key reads the English.
    ///
    /// The game's own words never come through here: they already arrive translated through
    /// <see cref="GameText.Get"/>, whose fallback argument stays plain English. Log lines are not translated.
    ///
    /// scripts/extract-strings.py finds the keys (Loc.T, Loc.P and Loc.Mark with a literal first string), writes
    /// assets/lang/en.json and reports what each language file lacks. Thread-safe for readers (the hotkey thread
    /// speaks too); the language is (re)read on the game thread only (<see cref="Tick"/>).
    /// </summary>
    internal static class Loc
    {
        // The current table (english -> translated), swapped whole; null = English.
        private static volatile Dictionary<string, string> _table;
        private static volatile string _lang = "en";
        private static string _wanted;
        private static float _nextCheck;
#if DEBUG
        private static readonly HashSet<string> _missing = new HashSet<string>();
#endif

        /// <summary>The language the mod speaks: the file's code ("ru-ru"), or "en".</summary>
        public static string Language => _lang;

        /// <summary>The English line <paramref name="english"/> in the player's language, its {0}, {1}… filled
        /// from <paramref name="args"/> (invariant culture: numbers read as digits). A placeholder keeps the
        /// sentence whole for the translator, who may move it ("You draw {0} cards" rather than "You draw " + n).</summary>
        public static string T(string english, params object[] args)
        {
            if (english == null) return null;
            string text = Lookup(english);
            if (args == null || args.Length == 0) return text;
            return LocRules.Format(text, english, args);
        }

        /// <summary>A count with its noun, "1 card" / "{0} cards": the English singular and plural, keyed as
        /// "one|other". {0} is <paramref name="n"/>; <paramref name="args"/> follow as {1}, {2}…. A translation
        /// lists its own forms separated by "|" in CLDR order (one|few|many for Russian, Polish, Ukrainian;
        /// one|few|other for Czech; one|other for most; a single form for Japanese, Korean, Chinese), and the
        /// form for <paramref name="n"/> is picked by that language's plural rule (<see cref="PluralIndex"/>).</summary>
        public static string P(long n, string one, string other, params object[] args)
        {
            string key = one + "|" + other;
            string text = Lookup(key);
            return LocRules.Plural(ReferenceEquals(text, key) ? null : text, _lang, n, one, other, args);
        }

        /// <summary>Marks a literal kept in a table (a static dictionary of English words) as a key for the
        /// extractor without translating it: the table holds English and its use site calls
        /// <see cref="T"/> on the value, so the language in force when it is spoken applies.</summary>
        public static string Mark(string english) => english;

        /// <summary>The form of a plural for <paramref name="n"/> in <paramref name="lang"/> (<see cref="LocRules.PluralIndex"/>).</summary>
        public static int PluralIndex(string lang, long n) => LocRules.PluralIndex(lang, n);

        private static string Lookup(string english)
        {
            var table = _table;
            if (table == null) return english;
            if (table.TryGetValue(english, out var text) && !string.IsNullOrEmpty(text)) return text;
#if DEBUG
            bool first;
            lock (_missing) first = _missing.Add(english);
            if (first) Plugin.LogInfo("[loc] no " + _lang + " for: " + english);
#endif
            return english;
        }

        /// <summary>Per frame on the game thread: once a second, follow the game's language (or the config's
        /// override) and load its file when it changed.</summary>
        public static void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextCheck) return;
            _nextCheck = now + 1f;
            Refresh();
        }

        /// <summary>Read the wanted language now and load its table if it changed (module load, then
        /// <see cref="Tick"/>). Game thread only: it reads the game's LocalizationManager.</summary>
        public static void Refresh()
        {
            string wanted = Normalize(ModConfig.Language);
            if (wanted == null || wanted == "auto")
            {
                try { wanted = Normalize(Localization.LocalizationManager.Instance?.CurrentLanguage); }
                catch { wanted = null; }
            }
            if (string.IsNullOrEmpty(wanted)) wanted = "en";
            if (wanted == _wanted) return;
            _wanted = wanted;
            Load(wanted);
        }

        private static string Normalize(string code) =>
            string.IsNullOrWhiteSpace(code) ? null : code.Trim().Replace('_', '-').ToLowerInvariant();

        private static void Load(string wanted)
        {
            if (LocRules.Base(wanted) == "en") { Set("en", null); return; }
            string dir = Path.Combine(Plugin.Dir ?? "", "lang");
            string file = FindFile(dir, wanted);
            if (file == null)
            {
                Plugin.LogInfo("[loc] no translation for " + wanted + " in " + dir + ", speaking English");
                Set(wanted, null);
                return;
            }
            try
            {
                var table = new Dictionary<string, string>(StringComparer.Ordinal);
                using (var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String && !p.Name.StartsWith("_")) table[p.Name] = p.Value.GetString();
                string code = Path.GetFileNameWithoutExtension(file);
                Set(code, table);
                Plugin.LogInfo("[loc] " + code + ": " + table.Count + " lines from " + file);
            }
            catch (Exception e)
            {
                Plugin.LogError("[loc] " + file + " unreadable, speaking English: " + e.Message);
                Set(wanted, null);
            }
        }

        // "es-mx.json", else another file of the same language ("es-es.json"), else a bare "es.json".
        private static string FindFile(string dir, string code)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                string exact = Path.Combine(dir, code + ".json");
                if (File.Exists(exact)) return exact;
                string lang = LocRules.Base(code);
                string bare = Path.Combine(dir, lang + ".json");
                if (File.Exists(bare)) return bare;
                foreach (var f in Directory.GetFiles(dir, lang + "-*.json")) return f;
            }
            catch { }
            return null;
        }

        private static void Set(string lang, Dictionary<string, string> table)
        {
            _table = table;
            _lang = table == null ? "en" : lang;
#if DEBUG
            lock (_missing) _missing.Clear();
#endif
        }
    }
}
