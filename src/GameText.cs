using Localization;

namespace GwentAccess
{
    /// <summary>
    /// The game's own words, looked up by translation key at runtime, so the mod speaks the player's language
    /// and the game's wording. Mod-authored English is only the fallback for a key the game doesn't have.
    /// </summary>
    internal static class GameText
    {
        /// <summary>The translation for <paramref name="key"/>, markup stripped; <paramref name="fallback"/>
        /// when the game has none (or only a placeholder).</summary>
        public static string Get(string key, string fallback)
        {
            try
            {
                var table = LocalizationManager.Instance?.Translations;
                if (key != null && table != null && table.ContainsKey(key))
                {
                    string text = UiText.Clean(table[key]);
                    if (text.Length > 0 && text != "Placeholder" && !text.StartsWith("[MISSING]")) return text;
                }
            }
            catch { }
            return fallback;
        }

        /// <summary>A keyword or status by its name alone: the game writes those "Name: explanation", and an
        /// icon or label shows just the name.</summary>
        public static string Term(string key, string fallback)
        {
            string text = Get(key, null);
            if (text == null) return fallback;
            int colon = text.IndexOf(':');
            return (colon > 0 ? text.Substring(0, colon) : text).Trim();
        }

        /// <summary>"ПРОТИВНИК" → "Противник": a label from an all-caps button, used inside a sentence.</summary>
        public static string Capitalized(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1).ToLowerInvariant();
        }

        /// <summary>"Рукопашный ряд" → "рукопашный ряд", for a word that follows another in a phrase.</summary>
        public static string Lower(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToLowerInvariant(text[0]) + text.Substring(1);
    }
}
