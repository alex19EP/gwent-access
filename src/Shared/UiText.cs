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
            { "Gold", "Ore" },
            { "Gems", "Meteorite powder" },
            { "Scraps", "Scraps" },
            { "RewardPoints", "Reward points" },
            { "KegAndPacks", "Kegs" },
            { "CardCount", "Cards in deck" },
            { "UnitCount", "Units in deck" },
            { "ProvisionCount", "Provisions" },
        };

        public static string DescribeSelection(AControl c)
        {
            string objName = c.gameObject.name.Replace("(Clone)", "");
            string typeName = "";
            try { typeName = c.GetIl2CppType().Name; } catch { }
            if (ValuePrefixes.TryGetValue(objName, out var prefix)) return prefix + ": " + Describe(c);
            if (typeName == "UIPlayerLevel") return "Level " + Describe(c);
            if (typeName == "UIJourneyLevel") return "Journey level " + Describe(c);
            if (typeName == "UIOuroborosLevelView") return "Season cycle level " + Describe(c);
            // Shop: say which currency a price is in.
            if (objName == "Button_ShopBuy")
            {
                string t = Describe(c);
                if (t.Contains("$") || t.Contains("€") || t.Contains("£") || t.ToLowerInvariant().Contains("steam")) return t + ". Real money";
                return t + " Ore. Control B buys";
            }
            if (objName == "ProductTile")
            {
                string t = Describe(c);
                if (!t.Contains("$")) t = t.Replace(", ", " for ") + " Ore";
                return t + ". Then Control B to buy";
            }
            if ((objName == "ButtonControlLeft" || objName == "ButtonControlRight") && UiText.DebugPath(c).Contains("QuantityControl"))
                return objName == "ButtonControlLeft" ? "Fewer" : "More";
            if (objName == "ProfileButton" && c.transform.parent != null && c.transform.parent.gameObject.name.StartsWith("PlayerRecordRow"))
            {
                var parts = new List<string>();
                var seen = new HashSet<string>();
                foreach (var t in c.transform.parent.GetComponentsInChildren<TMPro.TMP_Text>(false)) Add(parts, seen, t.text);
                parts.Remove("Profile");
                if (parts.Count >= 4) return "Rank " + parts[0] + ", " + string.Join(", ", parts.Skip(3)) + ", MMR " + parts[1] + ", level " + parts[2] + ". Enter for profile";
                return string.Join(", ", parts);
            }
            if (KnownNames.TryGetValue(objName, out var known)) return known;
            string setting = SettingText(c);
            if (setting != null) return setting;
#if GWENT
            try
            {
                var deckBtn = c.TryCast<GwentVisuals.UIDeckButtonControl>();
                if (deckBtn != null && DeckBuilder.List != null)
                    return Describe(c) + ". Enter opens it, Control U duplicates, Control N renames";
            }
            catch { }
            var card = CardText.CardOf(c);
            if (card != null)
            {
                string extra = "";
                try
                {
                    var dc = c.TryCast<GwentVisuals.UIDeckCardButtonControl>() ?? c.GetComponentInParent<GwentVisuals.UIDeckCardButtonControl>();
                    if (dc != null) { extra = dc.Count > 1 ? ", " + dc.Count + " copies in deck" : ", in deck"; }
                    else if (DeckBuilder.Active)
                    {
                        var cc = DeckBuilder.CollectionCardOf(c, out _);
                        var d = DeckBuilder.Deck();
                        if (cc != null && d != null) { int n = d.GetCardCount(cc); int own = cc.GetInstanceCount(); extra = (n > 0 ? ", " + n + " in deck" : "") + ", you own " + own; }
                    }
                }
                catch { }
                return CardText.Describe(card) + extra;
            }
            string node = RewardBookText.Describe(c);
            if (node != null) return node;

            if (typeName == "SocialButton")
                return SocialIconsPatch.Names.TryGetValue(c.Pointer, out var sn) ? sn : "Social panel button " + (c.transform.GetSiblingIndex() + 1);
#endif
            string value = Describe(c);
            if (value == "Button Control" || value == "Toggle Button Control")
            {
                // Unlabeled button: try the parent object's name for a hint.
                string pn = c.transform.parent != null ? FriendlyName(c.transform.parent.gameObject.name) : "";
                value = pn.Length > 0 ? pn + " button" : "Unlabeled button";
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

        /// <summary>
        /// The "body" text of a screen: text that isn't a button or a label sitting next to a control.
        /// E.g. the tutorial's "Welcome to Gwent! During three short battles...".
        /// </summary>
        public static string BodyText(Component panel, int maxChars)
        {
            var parts = new List<string>();
            var seen = new HashSet<string>();
            foreach (var t in panel.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                try
                {
                    var ctl = t.GetComponentInParent<AControl>();
                    if (ctl != null && ctl.IsSelectable) continue;
                    string c = Clean(t.text);
                    if (c.Contains("support ID") || c.StartsWith("Game version")) continue;
                    Add(parts, seen, t.text);
                }
                catch { }
            }
            string all = string.Join(". ", parts);
            if (all.Length > maxChars) all = all.Substring(0, maxChars);
            return all;
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
            return title + ": " + value + ". Left or right arrow to change";
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
        private static readonly System.Text.RegularExpressions.Regex SpaceBeforeClose = new System.Text.RegularExpressions.Regex(@" ([.,:;!?)»])");
        private static readonly System.Text.RegularExpressions.Regex SpaceAfterOpen = new System.Text.RegularExpressions.Regex(@"([(«]) ");

        /// <summary>Strip rich-text tags like &lt;color&gt; and sprite markers. A tag becomes a space (it may
        /// separate two words), which is then dropped again before closing punctuation and after an opening
        /// bracket: "&lt;b&gt;Order&lt;/b&gt;: (&lt;b&gt;ranged&lt;/b&gt;)" reads "Order: (ranged)".</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Some translation strings carry escaped line breaks as the two characters '\' 'n' (the new-player
            // intro's "minutes.\n\nIf you..."); TextMeshPro draws them as breaks, a screen reader would say them.
            if (s.IndexOf('\\') >= 0) s = s.Replace("\\r", " ").Replace("\\n", " ");
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
                    if (pretty.StartsWith("reward page tab")) pretty = "Reward book tab: " + pretty.Substring("reward page tab".Length).Trim();
                    return pretty;
                }
            }
            catch { }
            return null;
        }

        private static readonly Dictionary<string, string> KnownNames = new Dictionary<string, string>
        {
            { "FTUXSettings", "Settings" },
            { "ButtonLeaderboards", "Leaderboards" },
            { "ButtonRewards", "Rewards" },
            { "WelcomeScreen", "Welcome screen and tutorials" },
            { "ButtonQuit", "Quit game" },
            { "ButtonFactionInfo", "Faction info" },
            { "ButtonSettings", "Settings" },
            { "ProfileButton", "Profile" },
            { "CosmeticsButton", "Cosmetics" },
            { "PlayerTitleButton", "Player title" },
            { "Collider", "News" },
            { "SkinImage", "Leader skin preview" },
        };

        public static string FriendlyName(string name)
        {
            if (name != null && KnownNames.TryGetValue(name.Replace("(Clone)", ""), out var n)) return n;
            string p = Prettify(name);
            if (p.StartsWith("FTUX")) p = p.Substring(4).Trim();
            return p;
        }

        /// <summary>Panels that are just decoration/overlays and shouldn't be announced.</summary>
        public static bool LooksLikeNoise(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string n = name.ToLowerInvariant();
            return n.Contains("tooltip") || n.Contains("loading") || n.Contains("cursor") || n.Contains("overlay") || n.Contains("background");
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
