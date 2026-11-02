using System;
using System.Collections.Generic;

namespace GwentAccess
{
    /// <summary>
    /// Names the input glyphs the game draws inside its text ("Take a look by using &lt;sprite="[PC]"
    /// name=Action_Preview&gt;"), which <see cref="UiText.Clean"/> would otherwise drop, leaving the sentence
    /// without its key.
    ///
    /// - Keyboard (the game's mouse-and-keyboard interface): the mod's own key for what the glyph asks, since a
    ///   blind player presses the mod's keys, not the mouse the picture shows. Left click / A is Enter, Escape
    ///   / B is Escape, Preview is Space (card details) — and in the tutorial line about the deck and graveyard,
    ///   D and Y, the mod's pile keys.
    /// - Controller mode: the button the game itself shows, read from the glyph sprite the active sprite asset
    ///   draws for that name — the player presses the controller.
    /// - A glyph the mod has no key for is named by the game's own key and logged, so it can be given one.
    ///
    /// Glyph names come from the game's TextMeshPro sprite asset "[PC]" (keycode_LC, keycode_RC, keycode_Esc,
    /// keycode_Space, A, B, X, Y, Action_Preview → keycode_RC, Action_Select → keycode_LC); a controller swaps the
    /// pictures behind the same names.
    /// </summary>
    internal static class KeyGlyphs
    {
        public static void Install() => UiText.SpriteWords = Words;
        public static void Uninstall() { if (UiText.SpriteWords == (Func<string, string, string>)Words) UiText.SpriteWords = null; }

        // The localization key of the text being cleaned, when the caller knows it: some glyphs mean a
        // different mod key in a particular sentence.
        [ThreadStatic] private static string _context;

        /// <summary>Clean <paramref name="raw"/> knowing it is the string <paramref name="locKey"/>.</summary>
        public static string Clean(string raw, string locKey) => InContext(locKey, () => UiText.Clean(raw));

        /// <summary>Run <paramref name="read"/> (which cleans text) knowing the text is <paramref name="locKey"/>.</summary>
        public static T InContext<T>(string locKey, Func<T> read)
        {
            var was = _context;
            _context = locKey;
            try { return read(); }
            finally { _context = was; }
        }

        // Sentence-specific keys: localization key → (glyph name → the mod's key in words).
        private static readonly Dictionary<string, Dictionary<string, string>> BySentence = new Dictionary<string, Dictionary<string, string>>
        {
            // "You can see your unplayed cards in your deck and destroyed cards in your graveyard. Take a look by
            // using [Preview]": the deck and graveyard on the board, which the mod opens with D and Y.
            ["TEXT.tut001_firstbattle_flowchart.6"] = new Dictionary<string, string> { ["Action_Preview"] = "D for your deck, or Y for your graveyard" },
        };

        // Sentences that name an on-screen button instead of drawing a key glyph ("Hold "Pass""): in keyboard mode
        // the mod's key is added after them. Localization key → words appended.
        private static readonly Dictionary<string, string> AddAfter = new Dictionary<string, string>
        {
            // "Your opponent used strong cards. Hold "Pass" to pass this round now and save your strength for later."
            ["TEXT.tut001_fourthbattle_flowchart.2"] = "Hold E to pass.",
        };

        /// <summary>The mod's key for a sentence that names a game button rather than a key, or null.</summary>
        public static string KeyNote(string locKey)
        {
            if (ControllerMode.Force || string.IsNullOrEmpty(locKey)) return null;
            return AddAfter.TryGetValue(locKey, out var note) ? note : null;
        }

        /// <summary>A tutorial text, cleaned with its glyphs named, plus the mod's key when it names a game
        /// button. Callers that speak tutorial lines use this.</summary>
        public static string Tutorial(string raw, string locKey)
        {
            string text = Clean(raw, locKey);
            string note = KeyNote(locKey);
            return note == null || text.Length == 0 ? text : text + " " + note;
        }

        // The mod's keyboard key for a glyph, wherever it appears.
        private static readonly Dictionary<string, string> ModKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Action_Select"] = "Enter",
            ["keycode_LC"] = "Enter",
            ["A"] = "Enter",
            ["B"] = "Escape",
            ["keycode_Esc"] = "Escape",
            ["Action_Preview"] = "Space",
            ["keycode_RC"] = "Space",
        };

        private static string Words(string asset, string name)
        {
            // Only input glyphs: they come from the bracketed per-platform assets ("[PC]"). Currency and other
            // icons stay decoration here (Currency.cs names those where they matter).
            if (string.IsNullOrEmpty(asset) || asset[0] != '[' || string.IsNullOrEmpty(name)) return null;
            if (!ControllerMode.Force)
            {
                if (_context != null && BySentence.TryGetValue(_context, out var keys) && keys.TryGetValue(name, out var k)) return k;
                if (ModKeys.TryGetValue(name, out var mod)) return mod;
            }
            string game = GameKey(asset, name);
            if (!ControllerMode.Force) Plugin.LogInfo("[glyph] no mod key for " + name + (_context != null ? " in " + _context : "") + "; said the game's: " + game);
            return game;
        }

        // ---- the game's own key ----

        private static readonly Dictionary<string, TMPro.TMP_SpriteAsset> _assets = new Dictionary<string, TMPro.TMP_SpriteAsset>();

        /// <summary>The key the game draws for <paramref name="name"/> in <paramref name="asset"/>, in words.</summary>
        private static string GameKey(string asset, string name)
        {
            string sprite = null;
            try
            {
                var a = Asset(asset);
                var list = a?.spriteInfoList;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] != null && list[i].name == name) { sprite = list[i].sprite != null ? list[i].sprite.name : null; break; }
            }
            catch (Exception e) { Plugin.LogInfo("[glyph] lookup failed: " + e.Message); }
            return Humanize(sprite ?? name);
        }

        private static TMPro.TMP_SpriteAsset Asset(string name)
        {
            if (_assets.TryGetValue(name, out var cached))
            {
                try { if (cached != null && cached.Pointer != IntPtr.Zero && cached.name == name) return cached; } catch { }
                _assets.Remove(name);
            }
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<TMPro.TMP_SpriteAsset>();
            foreach (var a in all) if (a != null && a.name == name) { _assets[name] = a; return a; }
            return null;
        }

        // keycode_RC → "right click", keycode_Esc → "Escape", A → "A button", some_pad_name → "some pad name".
        private static string Humanize(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.StartsWith("keycode_", StringComparison.OrdinalIgnoreCase))
            {
                string k = s.Substring(8);
                switch (k.ToUpperInvariant())
                {
                    case "LC": return "left click";
                    case "RC": return "right click";
                    case "MC": return "middle click";
                    case "ESC": return "Escape";
                }
                return k.Replace('_', ' ');
            }
            if (s.Length <= 2 || s == "LB" || s == "RB" || s == "LT" || s == "RT") return s + " button";
            if (s.StartsWith("Action_", StringComparison.Ordinal)) s = s.Substring(7);
            return s.Replace('_', ' ');
        }
    }
}
