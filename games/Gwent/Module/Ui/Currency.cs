using System;
using System.Text.RegularExpressions;
using CrimsonUI;
using Modules.Common;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Currency names for amounts the game shows beside an icon (a sprite or an image): named as the top bar's
    /// currency counters name theirs on hover ("Обрывки", "Метеоритная пыль"), so the words are the game's own.
    /// </summary>
    internal static class Currency
    {
        public static string Scraps() => Name(o => o.m_ScrapsButton);
        public static string Powder() => Name(o => o.m_PowderButton);
        public static string Ore() => Name(o => o.m_GoldButton);
        public static string RewardPoints() => Name(o => o.m_RewardButton);
        public static string Kegs() => Name(o => o.m_KegsButton);

        /// <summary>The running event's currency ("Капли крови"): each event has its own translation key, but
        /// the counter's tooltip already carries the current one's name.</summary>
        public static string Event() => Name(o => o.m_EventButton);

        /// <summary>A currency by the game's type, as its counter names it; the game's plain name for it if the
        /// counter has none to give. Null for a type the game has no words for.</summary>
        public static string Of(EVirtualCurrencyType type) => type switch
        {
            EVirtualCurrencyType.Gold => Ore() ?? GameText.Get("gold_currency", null),
            EVirtualCurrencyType.Scraps => Scraps() ?? GameText.Get("card_scraps_currency", null),
            EVirtualCurrencyType.Powder => Powder() ?? GameText.Get("powder_currency", null),
            EVirtualCurrencyType.RewardPoints => RewardPoints() ?? GameText.Get("reward_point_currency", null),
            EVirtualCurrencyType.EventCurrency => Event(),
            _ => null,
        };

        private static string Name(Func<GwentUnity.UIPlayerInfoOverlay, AControl> counter)
        {
            try
            {
                var overlay = UnityEngine.Object.FindObjectOfType<GwentUnity.UIPlayerInfoOverlay>();
                var c = overlay != null ? counter(overlay) : null;
                string s = c != null ? CrimsonNodes.TooltipText(c) : null;
                return string.IsNullOrEmpty(s) ? null : s;
            }
            catch (Exception e) { Plugin.LogInfo("[currency] name failed: " + e.Message); return null; }
        }

        private static readonly Regex SpriteTag = new Regex("<sprite[^>]*name=\"([^\"]+)\"[^>]*>", RegexOptions.IgnoreCase);

        // A popup-icon sprite (<sprite="PopupIcons" name="Scraps">, name quoted or not), with the "5 x " drawn
        // before it when there is one.
        private static readonly Regex IconTag = new Regex(
            @"(?:(?<n>\d+)\s*x\s*)?<sprite[^>]*?\bname=""?(?<name>[^"" >]+)""?[^>]*>", RegexOptions.IgnoreCase);

        /// <summary>
        /// Rich text with its currency icons named where they are drawn, the tags still in it for
        /// <see cref="UiText.Clean"/>: "{1} x {2}" with an Ore icon reads "5 Ore". Sprites that aren't a currency
        /// (input glyphs, the well-rested icon) are left as they are.
        /// </summary>
        public static string Inline(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) < 0) return raw;
            return IconTag.Replace(raw, m =>
            {
                string name = OfIcon(m.Groups["name"].Value);
                if (name == null) return m.Value;
                return m.Groups["n"].Success ? m.Groups["n"].Value + " " + name : " " + name + " ";
            });
        }

        /// <summary>What a reward icon's picture stands for, by its sprite name ("ui_icon_crown_half(Clone)" is
        /// Journey XP, "symbol_icon_RP(Clone)" reward points), in the game's words; null when unknown.</summary>
        public static string OfRewardSprite(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return null;
            string s = sprite.ToLowerInvariant();
            if (s.Contains("crown")) return GameText.Get("experience_value", "XP");
            if (s.Contains("_rp") || s.Contains("rewardpoint")) return RewardPoints() ?? Of(EVirtualCurrencyType.RewardPoints);
            if (s.Contains("scrap")) return Scraps();
            if (s.Contains("powder")) return Powder();
            if (s.Contains("ore") || s.Contains("gold")) return Ore();
            if (s.Contains("keg")) return Kegs();
            return null;
        }

        /// <summary>The currency a popup icon (a sprite name: "Ore", "Scraps") stands for, in the game's words;
        /// null for any other icon.</summary>
        public static string OfIcon(string sprite)
        {
            switch (sprite.ToLowerInvariant())
            {
                case "ore": case "gold": return Of(EVirtualCurrencyType.Gold);
                case "scraps": return Of(EVirtualCurrencyType.Scraps);
                case "powder": return Of(EVirtualCurrencyType.Powder);
                case "rewardpoint": case "rewardpoints": return Of(EVirtualCurrencyType.RewardPoints);
                case "keg": return Kegs() ?? GameText.Get("panel_currency_keg", null);
                default: return null;
            }
        }

        /// <summary>
        /// A rich-text caption with an inline currency sprite, said with the currency named after the amount:
        /// "Создать: &lt;sprite name="ScrapsNeg"&gt; -800" → "Создать: -800 Обрывки". Captions without a known
        /// currency sprite come back cleaned and otherwise unchanged.
        /// </summary>
        public static string Caption(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var m = SpriteTag.Match(raw);
            string text = UiText.Clean(raw);
            if (!m.Success) return text;
            string sprite = m.Groups[1].Value;
            string name = sprite.IndexOf("scrap", StringComparison.OrdinalIgnoreCase) >= 0 ? Scraps()
                : sprite.IndexOf("powder", StringComparison.OrdinalIgnoreCase) >= 0 ? Powder()
                : sprite.IndexOf("ore", StringComparison.OrdinalIgnoreCase) >= 0 ? Ore()
                : null;
            return name == null ? text : text + " " + name;
        }
    }
}
