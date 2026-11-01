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
