using System;
using System.Collections.Generic;
using GwentGameplay;
using Localization;

namespace GwentAccess
{
    /// <summary>
    /// Turns any Gwent card into speech, in the shape the battle, deck builder, collection, shop and pack
    /// screens all share: a summary line spoken on focus, then the lines a sighted player finds in the game's
    /// big card preview — categories, each ability, type and look, flavor, illustrator — and, apart, the
    /// preview's keyword and status explanations.
    ///
    /// Speaks only what the game shows (the parity rule): live power with its colour as a word ("boosted" for
    /// the green number), the status icons with the numbers they carry, the order icon's state; base power
    /// only in the details, where the preview's header shows it. A card the game hides from the local player
    /// reads "hidden card" and nothing else. Card text stays in the game's language; the few words the mod
    /// adds are English, like the rest of the mod.
    /// </summary>
    internal static class CardReader
    {
        /// <summary>The line spoken on focus: "Name, power 9, boosted, armor 2, Bleeding 2, order ready".
        /// <paramref name="pm"/> is the match's player manager (null outside a match): in a match the card is
        /// checked against what the local player may see, and stats read live.</summary>
        public static string Summary(Card card, PlayerManager pm = null)
        {
            if (card == null) return "";
            if (Hidden(card, pm)) return "hidden card";
            var parts = new List<string> { Name(card) };
            var type = Type(card);
            if (type == ECardType.Unit) AddPower(parts, card);
            else parts.Add(TypeWord(type));
            // A leader shows its faction on the board (the deck's colours and crest); other cards only in the preview.
            if (type == ECardType.Leader) Add(parts, Faction(card));
            AddStatuses(parts, card);
            if (pm != null) AddOrder(parts, card);
            AddTimer(parts, card);
            if (type != ECardType.Leader && (pm == null || At(card) == ELocation.Hand))
                Try(() => { int p = card.Template.Provision; if (p > 0) parts.Add(p + " provisions"); });
            return string.Join(", ", parts);
        }

        /// <summary>The summary followed by the card's abilities: "Name, power 9, 7 provisions. Deploy: …". The
        /// focus line for your own cards (hand, rows, mulligan, choices), where what a card does decides the
        /// play — as the first version of the mod read them; the opponent's board keeps the short
        /// <see cref="Summary"/> so scanning it stays quick.</summary>
        public static string WithAbilities(Card card, PlayerManager pm = null)
        {
            string summary = Summary(card, pm);
            if (card == null || Hidden(card, pm)) return summary;
            var abilities = new List<string>();
            foreach (var ability in Text(() => LocalizationHelper.GetTooltipDescription(card), clean: false).Split('\n'))
                Add(abilities, UiText.Clean(ability));
            return abilities.Count == 0 ? summary : summary + ". " + string.Join(" ", abilities);
        }

        /// <summary>Only the card's ability text ("Deploy: Boost adjacent units by 2."), or "" when it has none or
        /// is hidden from you.</summary>
        public static string Abilities(Card card, PlayerManager pm = null)
        {
            if (card == null || Hidden(card, pm)) return "";
            var abilities = new List<string>();
            foreach (var ability in Text(() => LocalizationHelper.GetTooltipDescription(card), clean: false).Split('\n'))
                Add(abilities, UiText.Clean(ability));
            return string.Join(" ", abilities);
        }

        /// <summary>One line of a card, with the keywords it mentions (the game's keys, e.g. "order", "veil").</summary>
        public sealed class CardLine
        {
            public string Text;
            public readonly List<string> Keywords = new List<string>();
        }

        /// <summary>The card itself, line by line, the summary first: what Ctrl+Up/Down step through. Keyword
        /// explanations are separate (<see cref="Explanations"/>), as the game keeps them beside the card.</summary>
        public static List<CardLine> Lines(Card card, PlayerManager pm = null)
        {
            var lines = new List<CardLine> { new CardLine { Text = Summary(card, pm) } };
            if (card == null || Hidden(card, pm)) return lines;
            Add(lines, Text(() => LocalizationHelper.LocalizeCategories(card)));
            // Each keyword in the ability text is a <link=key> tag: that is how a line knows its keywords.
            foreach (var ability in Text(() => LocalizationHelper.GetTooltipDescription(card), clean: false).Split('\n'))
            {
                var line = Add(lines, UiText.Clean(ability));
                if (line == null) continue;
                foreach (System.Text.RegularExpressions.Match m in LinkTag.Matches(ability))
                    if (!line.Keywords.Contains(m.Groups[1].Value)) line.Keywords.Add(m.Groups[1].Value);
            }
            Add(lines, Stats(card));
            Add(lines, Text(() => LocalizationHelper.GetCardFlavor(card)));
            Add(lines, Text(() => LocalizationHelper.GetArtistDescription(card.Template)));
            return lines;
        }

        private static readonly System.Text.RegularExpressions.Regex LinkTag = new System.Text.RegularExpressions.Regex("<link=\"?([^\">]+)\"?>");

        /// <summary>
        /// The card's keywords explained, "Order: activates manually…": the preview's keyword list, then its
        /// status list (a status's explanation sits with the keywords, under the status's name). With
        /// <paramref name="only"/>, just those keywords, in that order — the words of one line.
        /// </summary>
        public static List<string> Explanations(Card card, PlayerManager pm = null, IReadOnlyList<string> only = null)
        {
            var found = new List<string>();
            if (card == null || Hidden(card, pm)) return found;
            var known = new Dictionary<string, string>();
            var order = new List<string>();
            Try(() =>
            {
                var keywords = LocalizationHelper.GetKeywordsDescriptionWithConnectedCardsAsList(card);
                for (int i = 0; keywords != null && i < keywords.Count; i++)
                {
                    string key = keywords[i].key, text = UiText.Clean(keywords[i].description);
                    if (key == null || text.Length == 0 || known.ContainsKey(key)) continue;
                    known[key] = text;
                    order.Add(key);
                }
            });
            foreach (var (token, _) in Statuses(card))
            {
                string key = token.ToString().ToLowerInvariant();
                if (known.ContainsKey(key)) continue;
                string text = Explain(key);
                if (text.Length == 0) continue;
                known[key] = text;
                order.Add(key);
            }
            foreach (var key in only ?? order)
            {
                string text = known.TryGetValue(key, out var t) ? t : Explain(key);
                if (text.Length > 0 && !found.Contains(text)) found.Add(text);
            }
            return found;
        }

        /// <summary>The whole card for Space: its lines, then its keywords explained.</summary>
        public static string Whole(Card card, PlayerManager pm = null)
        {
            var parts = new List<string>();
            foreach (var line in Lines(card, pm)) parts.Add(line.Text);
            parts.AddRange(Explanations(card, pm));
            return string.Join(". ", parts);
        }

        /// <summary>The card's kind and look, with base power for a unit: "gold unit, base power 4, Northern
        /// Realms, epic". What K reads.</summary>
        public static string Stats(Card card)
        {
            if (card == null) return "";
            var parts = new List<string>();
            var type = Type(card);
            string tier = "";
            Try(() => tier = card.Template.Tier == ECardTier.Leader ? "" : card.Template.Tier.ToString().ToLowerInvariant() + " ");
            parts.Add(tier + TypeWord(type));
            if (type == ECardType.Unit)
                Try(() => parts.Add("base power " + card.Data.Power.BasePower));
            Add(parts, Faction(card));
            Try(() => parts.Add(card.Template.Rarity.ToString().ToLowerInvariant()));
            return string.Join(", ", parts);
        }

        /// <summary>The card's faction in the game's words ("Нильфгаард"), or "".</summary>
        public static string Faction(Card card) =>
            card == null ? "" : Text(() => LocalizationHelper.GetFactionName(card.Template.FactionId));

        /// <summary>A leader as the match opens: "Name, Faction".</summary>
        public static string Leader(Card card)
        {
            if (card == null) return "";
            var parts = new List<string> { Name(card) };
            Add(parts, Faction(card));
            return string.Join(", ", parts);
        }

        public static bool Hidden(Card card, PlayerManager pm)
        {
            if (pm == null) return false;
            try { return !card.IsVisibleToLocal(pm); } catch { return true; }
        }

        /// <summary>Just the name, or "hidden card" for one the local player can't see.</summary>
        public static string Name(Card card, PlayerManager pm) => Hidden(card, pm) ? "hidden card" : Name(card);

        private static string Name(Card card)
        {
            string name = Text(() => LocalizationHelper.GetCardName(card));
            return name.Length > 0 ? name : "unknown card";
        }

        // A card's main type: the template's type flags, leader first (a leader is never also a unit).
        private static ECardType Type(Card card)
        {
            ECardType t = 0;
            Try(() => t = card.Template.Type);
            foreach (var one in new[] { ECardType.Leader, ECardType.Unit, ECardType.Special, ECardType.Artifact, ECardType.Stratagem })
                if ((t & one) != 0) return one;
            return t;
        }

        private static string TypeWord(ECardType t) => t switch
        {
            ECardType.Leader => "leader",
            ECardType.Unit => "unit",
            ECardType.Special => "special",
            ECardType.Artifact => "artifact",
            ECardType.Stratagem => "stratagem",
            _ => "card",
        };

        private static ELocation At(Card card)
        {
            try { return card.Position.Location; } catch { return 0; }
        }

        // The power number as drawn, with its colour as a word: green when boosted, red when damaged; the base
        // power's own colour when it was raised or lowered.
        private static void AddPower(List<string> parts, Card card)
        {
            Try(() =>
            {
                var power = card.Data.Power;
                parts.Add("power " + power.CurrentPower);
                switch (power.GetPowerState())
                {
                    case EPowerState.Wounded: parts.Add("damaged"); break;
                    case EPowerState.Boosted: parts.Add("boosted"); break;
                    case EPowerState.Weakened: parts.Add("weakened"); break;
                    case EPowerState.Strengthened: parts.Add("strengthened"); break;
                }
                if (power.CurrentArmor > 0) parts.Add("armor " + power.CurrentArmor);
            });
        }

        // The status icons. Only Vitality and Bleeding draw their turns left on the icon.
        private static void AddStatuses(List<string> parts, Card card)
        {
            foreach (var (token, timer) in Statuses(card))
            {
                // The game's status text is "Name: explanation"; the icon is just the name.
                string name = Text(() => LocalizationHelper.GetCardStatus(token));
                int colon = name.IndexOf(':');
                if (colon > 0) name = name.Substring(0, colon);
                if (name.Length == 0) name = token.ToString();
                if (timer > 0 && (token == ECardToken.Vitality || token == ECardToken.Bleeding)) name += " " + timer;
                parts.Add(name);
            }
        }

        private static List<(ECardToken token, int timer)> Statuses(Card card)
        {
            var found = new List<(ECardToken, int)>();
            Try(() =>
            {
                var data = card.Data.Statuses.StatusData;
                for (int i = 0; data != null && i < data.Count; i++)
                {
                    var s = data[i];
                    int timer = 0;
                    try { timer = s.AbilityData.Timer.Value; } catch { }
                    found.Add((s.Status, timer));
                }
            });
            return found;
        }

        // The order icon on a card in play: ready, waiting out the turn it arrived, recharging, used up;
        // with its charges when the card shows them.
        private static void AddOrder(List<string> parts, Card card)
        {
            var at = At(card);
            if (at != ELocation.Melee && at != ELocation.Ranged && at != ELocation.Leader) return;
            Try(() =>
            {
                if (!card.HasTapAbility()) return;
                var charges = card.Data.TapCharges;
                switch (charges.State)
                {
                    case ETapCardState.Available: parts.Add("order ready"); break;
                    case ETapCardState.SummoningSickness: parts.Add("order next turn"); break;
                    case ETapCardState.Recharging: parts.Add("order recharging"); break;
                    case ETapCardState.Depleted: parts.Add("order used"); break;
                    case ETapCardState.Unavailable: parts.Add("order unavailable"); break;
                }
                if (card.Template.VisibleTapCharges || at == ELocation.Leader)
                    parts.Add(charges.Value == 1 ? "1 charge" : charges.Value + " charges");
            });
        }

        private static void AddTimer(List<string> parts, Card card)
        {
            Try(() =>
            {
                int t = card.AbilityData.Timer.Value;
                if (t >= 0 && t != AEntityTimer.NO_TIMER) parts.Add("timer " + t);
            });
        }

        // One keyword's explanation from the game's table ("keyword_order", "keyword_veil"), "" when it has none.
        private static string Explain(string key)
        {
            try
            {
                var table = GameText.Table;
                string id = LocalizationHelper.KEYWORD_LOCALIZATION_KEY_PREFIX + key;
                return table != null && table.ContainsKey(id) ? UiText.Clean(table[id]) : "";
            }
            catch { return ""; }
        }

        private static string Text(Func<string> get, bool clean = true)
        {
            try
            {
                string s = get() ?? "";
                if (s.StartsWith("[MISSING]")) return "";
                return clean ? UiText.Clean(s) : s;
            }
            catch { return ""; }
        }

        private static void Add(List<string> lines, string line)
        {
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("[MISSING]")) lines.Add(line);
        }

        private static CardLine Add(List<CardLine> lines, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("[MISSING]")) return null;
            var line = new CardLine { Text = text };
            lines.Add(line);
            return line;
        }

        private static void Try(Action a)
        {
            try { a(); } catch { }
        }
    }
}
