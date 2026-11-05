using System;
using System.Globalization;

namespace GwentAccess
{
    /// <summary>
    /// The pure rules behind <see cref="Loc"/>: filling a line's placeholders, picking a plural form by the
    /// language's CLDR integer rule, and falling back to the English when a translation's placeholders are
    /// broken. No game or Unity types, so the kernel's test project compiles this file too.
    /// </summary>
    public static class LocRules
    {
        /// <summary><paramref name="text"/> with its {0}, {1}… filled (invariant culture: numbers read as
        /// digits). A translation with a broken placeholder gives the English line filled instead, and an English
        /// line that can't be filled is said as written, never nothing.</summary>
        public static string Format(string text, string english, object[] args)
        {
            try { return string.Format(CultureInfo.InvariantCulture, text, args); }
            catch (FormatException)
            {
                try { return string.Format(CultureInfo.InvariantCulture, english, args); }
                catch (FormatException) { return english; }
            }
        }

        /// <summary>
        /// A count with its noun. <paramref name="translated"/> is the language's forms separated by "|" in CLDR
        /// order, or null when the language has none: then the English <paramref name="one"/> or
        /// <paramref name="other"/>. {0} is <paramref name="n"/>; <paramref name="args"/> follow as {1}, {2}….
        /// A form with a broken placeholder falls back to the English.
        /// </summary>
        public static string Plural(string translated, string lang, long n, string one, string other, object[] args)
        {
            var all = new object[1 + (args?.Length ?? 0)];
            all[0] = n;
            if (args != null) Array.Copy(args, 0, all, 1, args.Length);
            string english = n == 1 ? one : other;
            if (translated == null) return Format(english, other, all);
            string[] forms = translated.Split('|');
            string form = forms[Math.Min(PluralIndex(lang, n), forms.Length - 1)];
            return Format(form, english, all);
        }

        /// <summary>The form of a plural for <paramref name="n"/> in <paramref name="lang"/> ("ru-ru", "pl"…), an
        /// index into the language's CLDR integer categories in order: one|few|many for Russian, Ukrainian,
        /// Belarusian and Polish; one|few|other for Czech and Slovak; one|other for most, with 0 as one for French
        /// and Portuguese; a single form for Japanese, Korean and Chinese.</summary>
        public static int PluralIndex(string lang, long n)
        {
            n = Math.Abs(n);
            long d10 = n % 10, d100 = n % 100;
            switch (Base(lang))
            {
                case "ru": case "uk": case "be":
                    if (d10 == 1 && d100 != 11) return 0;
                    if (d10 >= 2 && d10 <= 4 && (d100 < 12 || d100 > 14)) return 1;
                    return 2;
                case "pl":
                    if (n == 1) return 0;
                    if (d10 >= 2 && d10 <= 4 && (d100 < 12 || d100 > 14)) return 1;
                    return 2;
                case "cs": case "sk":
                    return n == 1 ? 0 : n >= 2 && n <= 4 ? 1 : 2;
                case "fr": case "pt":
                    return n <= 1 ? 0 : 1;
                case "ja": case "ko": case "zh":
                    return 0;
                default:
                    return n == 1 ? 0 : 1;
            }
        }

        /// <summary>The language without its region: "ru-ru" → "ru"; null or empty → "en".</summary>
        public static string Base(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return "en";
            int dash = lang.IndexOf('-');
            return dash > 0 ? lang.Substring(0, dash) : lang;
        }
    }
}
