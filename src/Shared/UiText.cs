using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;
using CrimsonUI;

namespace GwentAccess
{
    /// <summary>
    /// Words on screen: the visible text under a control, cleaned of rich-text markup, and the fallbacks for
    /// controls with none (icon names, prettified object names). Shared by every game on Gwent's engine; the
    /// Gwent-only cases of <see cref="DescribeSelection"/> (deck buttons, cards, the reward book, the social
    /// panel) are under #if GWENT.
    /// </summary>
    internal static class UiText
    {
        /// <summary>Collect visible text under a control (TextMeshPro + legacy UI Text).</summary>
        public static string Describe(Component c) => Describe(c, 12);

        /// <summary>What to say for a selected control. Switcher rows become "Setting name: value".</summary>
        private static readonly Dictionary<string, string> ValuePrefixes = new Dictionary<string, string>
        {
            { "Gold", Loc.Mark("Ore") },
            { "Gems", Loc.Mark("Meteorite powder") },
            { "Scraps", Loc.Mark("Scraps") },
            { "RewardPoints", Loc.Mark("Reward points") },
            { "KegAndPacks", Loc.Mark("Kegs") },
            { "CardCount", Loc.Mark("Cards in deck") },
            { "UnitCount", Loc.Mark("Units in deck") },
            { "ProvisionCount", Loc.Mark("Provisions") },
        };

        public static string DescribeSelection(AControl c)
        {
            string objName = c.gameObject.name.Replace("(Clone)", "");
            string typeName = "";
            try { typeName = c.GetIl2CppType().Name; } catch { }
            if (ValuePrefixes.TryGetValue(objName, out var prefix)) return Loc.T(prefix) + ": " + Describe(c);
            if (typeName == "UIPlayerLevel") return Loc.T("Level {0}", Describe(c));
            if (typeName == "UIJourneyLevel") return Loc.T("Journey level {0}", Describe(c));
            if (typeName == "UIOuroborosLevelView") return Loc.T("Season cycle level {0}", Describe(c));
            // Shop: say which currency a price is in.
            if (objName == "Button_ShopBuy")
            {
                string t = Describe(c);
                if (t.Contains("$") || t.Contains("€") || t.Contains("£") || t.ToLowerInvariant().Contains("steam")) return Loc.T("{0}. Real money", t);
                return t;
            }
            if ((objName == "ButtonControlLeft" || objName == "ButtonControlRight") && UiText.DebugPath(c).Contains("QuantityControl"))
                return objName == "ButtonControlLeft" ? Loc.T("Fewer") : Loc.T("More");
            if (objName == "ProfileButton" && c.transform.parent != null && c.transform.parent.gameObject.name.StartsWith("PlayerRecordRow"))
            {
                var parts = new List<string>();
                var seen = new HashSet<string>();
                foreach (var t in c.transform.parent.GetComponentsInChildren<TMPro.TMP_Text>(false)) Add(parts, seen, t.text);
                parts.Remove("Profile");
                if (parts.Count >= 4) return Loc.T("Rank {0}, {1}, MMR {2}, level {3}. Enter for profile", parts[0], string.Join(", ", parts.Skip(3)), parts[1], parts[2]);
                return string.Join(", ", parts);
            }
            if (KnownNames.TryGetValue(objName, out var known)) return Loc.T(known);
            string setting = SettingText(c);
            if (setting != null) return setting;
#if GWENT
            try
            {
                var deckBtn = c.TryCast<GwentVisuals.UIDeckButtonControl>();
                if (deckBtn != null && DeckBuilder.List != null)
                    return Describe(c);
            }
            catch { }
            var card = CardText.CardOf(c);
            if (card != null)
            {
                string extra = "";
                try
                {
                    var dc = c.TryCast<GwentVisuals.UIDeckCardButtonControl>() ?? c.GetComponentInParent<GwentVisuals.UIDeckCardButtonControl>();
                    if (dc != null) { extra = dc.Count > 1 ? ", " + Loc.P(dc.Count, "{0} copy in deck", "{0} copies in deck") : ", " + Loc.T("in deck"); }
                    else if (DeckBuilder.Active)
                    {
                        var cc = DeckBuilder.CollectionCardOf(c, out _);
                        var d = DeckBuilder.Deck();
                        if (cc != null && d != null) { int n = d.GetCardCount(cc); int own = cc.GetInstanceCount(); extra = (n > 0 ? ", " + Loc.T("{0} in deck", n) : "") + ", " + Loc.T("you own {0}", own); }
                    }
                }
                catch { }
                return CardText.Describe(card) + extra;
            }
            string node = RewardBookText.Describe(c);
            if (node != null) return node;

            if (typeName == "SocialButton")
                return SocialIconsPatch.Names.TryGetValue(c.Pointer, out var sn) ? sn : Loc.T("Social panel button {0}", c.transform.GetSiblingIndex() + 1);
#endif
            string value = Describe(c);
            if (value == "Button Control" || value == "Toggle Button Control")
            {
                // Unlabeled button: try the parent object's name for a hint.
                string pn = c.transform.parent != null ? FriendlyName(c.transform.parent.gameObject.name) : "";
                value = pn.Length > 0 ? Loc.T("{0} button", pn) : Loc.T("Unlabeled button");
            }
            Switcher sw = null;
            try { sw = c.GetComponentInParent<Switcher>(); } catch { }
            if (sw != null)
            {
                string title = RowTitle(sw);
                if (title.Length > 0 && title != value) return title + ": " + value;
            }
            return value;
        }


        /// <summary>A settings row (e.g. "Music volume: 80. Left or right arrow to change"), or null if c isn't in one.</summary>
        public static string SettingText(AControl c)
        {
            GwentUnity.UISettingsEntry entry = null;
            try { entry = c.GetComponentInParent<GwentUnity.UISettingsEntry>(); } catch { }
            if (entry == null) return null;
            string title = "";
            try { title = Describe(entry.m_Title, 2); } catch { }
            Switcher sw = null;
            try { sw = entry.Switcher; } catch { }
            if (title.Length == 0 && sw != null) title = RowTitle(sw);
            string value = "";
            try { if (sw != null) value = Describe(sw, 3); } catch { }
            if (value.Length == 0) try { value = entry.GetCurrentOption() ?? ""; } catch { }
            return Loc.T("{0}: {1}. Left or right arrow to change", title, value);
        }

        /// <summary>Text in a switcher's row that isn't part of the switcher itself (the setting's name).</summary>
        public static string RowTitle(Switcher sw)
        {
            var row = sw.transform.parent;
            if (row == null) return "";
            var parts = new List<string>();
            var seen = new HashSet<string>();
            foreach (var t in row.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (t.transform.IsChildOf(sw.transform)) continue;
                Add(parts, seen, t.text);
            }
            if (parts.Count == 0) return FriendlyName(row.gameObject.name);
            return string.Join(", ", parts);
        }

        public static string Describe(Component c, int maxParts)
        {
            var parts = new List<string>();
            var seen = new HashSet<string>();
            try
            {
                foreach (var t in c.GetComponentsInChildren<TMPro.TMP_Text>(false))
                    Add(parts, seen, t.text);
            }
            catch (Exception e) { Plugin.LogInfo("TMP read error: " + e.Message); }
            try
            {
                foreach (var t in c.GetComponentsInChildren<UnityEngine.UI.Text>(false))
                    Add(parts, seen, t.text);
            }
            catch { }
            if (parts.Count > maxParts) parts.RemoveRange(maxParts, parts.Count - maxParts);
            if (parts.Count == 0)
            {
                string icon = IconName(c);
                parts.Add(icon ?? FriendlyName(c.gameObject.name));
            }
            return string.Join(", ", parts);
        }

        private static void Add(List<string> parts, HashSet<string> seen, string raw)
        {
            string s = Clean(raw);
            if (s.Length == 0 || seen.Contains(s) || s.StartsWith("[MISSING]") || IsPlaceholder(s)) return;
            seen.Add(s);
            parts.Add(s);
        }

        /// <summary>Layout filler the game keeps in hidden-but-active text objects to size a box, e.g. the
        /// "WWWWWWWW…" in the deck list's new-deck button: one character repeated.</summary>
        public static bool IsPlaceholder(string s)
        {
            if (s.Length < 4) return false;
            foreach (char ch in s) if (ch != s[0]) return false;
            return true;
        }

        private static readonly System.Text.RegularExpressions.Regex Spaces = new System.Text.RegularExpressions.Regex(@"\s+");
        private static readonly System.Text.RegularExpressions.Regex Counter = new System.Text.RegularExpressions.Regex(@"(?<![\d/])(\d+)\s*/\s*(\d+)(?![\d/])");

        // <sprite="[PC]" name=Action_Preview>, <sprite name="ScrapsNeg">: the sprite asset (quoted or not, may be
        // absent) and the sprite's name.
        private static readonly System.Text.RegularExpressions.Regex SpriteTag = new System.Text.RegularExpressions.Regex(
            @"<sprite(?:=""?(?<asset>[^"" >]*)""?)?[^>]*?\bname=""?(?<name>[^"" >]+)""?[^>]*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Names an inline sprite (asset, sprite name) in words, or null to drop it as decoration. Set by
        /// a game module (Gwent: <c>KeyGlyphs</c>, which names input glyphs by the mod's or the controller's key).</summary>
        public static Func<string, string, string> SpriteWords = null;
        private static readonly System.Text.RegularExpressions.Regex SpaceBeforeClose = new System.Text.RegularExpressions.Regex(@" ([.,:;!?)»])");
        private static readonly System.Text.RegularExpressions.Regex SpaceAfterOpen = new System.Text.RegularExpressions.Regex(@"([(«]) ");

        /// <summary>The parts that have words, joined with commas: "Level 2, 40 Ore".</summary>
        public static string Join(params string[] parts)
        {
            var list = new List<string>();
            foreach (var s in parts) if (!string.IsNullOrEmpty(s)) list.Add(s);
            return string.Join(", ", list);
        }

        public static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        /// <summary>Strip rich-text tags like &lt;color&gt; and sprite markers. A tag becomes a space (it may
        /// separate two words), which is then dropped again before closing punctuation and after an opening
        /// bracket: "&lt;b&gt;Order&lt;/b&gt;: (&lt;b&gt;ranged&lt;/b&gt;)" reads "Order: (ranged)".</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Some translation strings carry escaped line breaks as the two characters '\' 'n' (the new-player
            // intro's "minutes.\n\nIf you..."); TextMeshPro draws them as breaks, a screen reader would say them.
            if (s.IndexOf('\\') >= 0) s = s.Replace("\\r", " ").Replace("\\n", " ");
            // An input glyph drawn inline ("Take a look by using <sprite="[PC]" name=Action_Preview>") is a key the
            // sentence depends on: the game's resolver names it, before the tags are stripped.
            if (SpriteWords != null && s.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) >= 0)
                s = SpriteTag.Replace(s, m =>
                {
                    string words = null;
                    try { words = SpriteWords(m.Groups["asset"].Value, m.Groups["name"].Value); } catch { }
                    return string.IsNullOrEmpty(words) ? m.Value : " " + words + " ";
                });
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char ch in s)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>') { inTag = false; sb.Append(' '); continue; }
                if (ch == '​' || ch == '‌' || ch == '‍' || ch == '﻿' || ch == '­') continue;
                if (!inTag) sb.Append(ch);
            }
            string t = Spaces.Replace(sb.ToString(), " ").Trim();
            // A counter drawn as "0/3" (quests, hand size) reads "0 of 3": a screen reader says "slash" otherwise.
            // Not inside longer runs like a date ("1/2/2026").
            t = Counter.Replace(t, "$1 of $2");
            t = SpaceBeforeClose.Replace(t, "$1");
            return SpaceAfterOpen.Replace(t, "$1");
        }

        /// <summary>For icon-only buttons, use the icon picture's name (e.g. "icon_close" -> "close").</summary>
        public static string IconName(Component c)
        {
            try
            {
                foreach (var img in c.GetComponentsInChildren<UnityEngine.UI.Image>(false))
                {
                    if (img.sprite == null) continue;
                    string n = img.sprite.name;
                    Plugin.LogInfo("  icon candidate: " + n + " on " + img.gameObject.name);
                    string l = n.ToLowerInvariant();
                    if (l.Contains("button-") || l.Contains("idle") || l.Contains("hovered") || l.Contains("frame") || l.Contains("bg") || l.Contains("background") || l.Contains("glow") || l.Contains("shadow") || l.Contains("highlight") || l.Contains("mask") || l.StartsWith("ui_"))
                        continue;
                    string pretty = Prettify(n.Replace("icon", "").Replace("Icon", "").Replace("ico_", ""));
                    if (pretty.StartsWith("reward page tab")) pretty = Loc.T("Reward book tab: {0}", pretty.Substring("reward page tab".Length).Trim());
                    return pretty;
                }
            }
            catch { }
            return null;
        }

        private static readonly Dictionary<string, string> KnownNames = new Dictionary<string, string>
        {
            { "FTUXSettings", Loc.Mark("Settings") },
            { "ButtonLeaderboards", Loc.Mark("Leaderboards") },
            { "ButtonRewards", Loc.Mark("Rewards") },
            { "WelcomeScreen", Loc.Mark("Welcome screen and tutorials") },
            { "ButtonQuit", Loc.Mark("Quit game") },
            { "ButtonFactionInfo", Loc.Mark("Faction info") },
            { "ButtonSettings", Loc.Mark("Settings") },
            { "ProfileButton", Loc.Mark("Profile") },
            { "CosmeticsButton", Loc.Mark("Cosmetics") },
            { "PlayerTitleButton", Loc.Mark("Player title") },
            { "Collider", Loc.Mark("News") },
            { "SkinImage", Loc.Mark("Leader skin preview") },
        };

        public static string FriendlyName(string name)
        {
            if (name != null && KnownNames.TryGetValue(name.Replace("(Clone)", ""), out var n)) return Loc.T(n);
            string p = Prettify(name);
            if (p.StartsWith("FTUX")) p = p.Substring(4).Trim();
            return p;
        }


        /// <summary>Turn object names like "Btn_PlayButton" into "Play Button" as a fallback.</summary>
        public static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            name = name.Replace("(Clone)", "").Replace("_", " ");
            foreach (var p in new[] { "Btn ", "Button ", "UI " }) if (name.StartsWith(p)) name = name.Substring(p.Length);
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if (i > 0 && char.IsUpper(ch) && char.IsLower(name[i - 1])) sb.Append(' ');
                sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        public static string DebugPath(Component c)
        {
            var names = new List<string>();
            Transform t = c.transform;
            for (int i = 0; t != null && i < 6; i++, t = t.parent) names.Insert(0, t.gameObject.name);
            return c.GetIl2CppType().Name + " @ " + string.Join("/", names);
        }
    }
}
