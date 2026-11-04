using System;
using System.Collections.Generic;
using GwentGameplay;
using Localization;

namespace GwentAccess
{
    /// <summary>
    /// Turns any Gwent card into speech, in the shape the battle, deck builder, collection, shop and pack
    /// screens all share (Hearthstone Access's): a short summary line spoken on focus, then the lines a sighted
    /// player finds in the game's big card preview, read one by one — each ability, categories, type and look,
    /// flavor, illustrator — and, apart, the preview's keyword and status explanations.
    ///
    /// Speaks only what the game shows (the parity rule): live power with its colour as a word ("boosted" for
    /// the green number), the status icons with the numbers they carry, the order icon's state; base power
    /// only in the details, where the preview's header shows it. A card the game hides from the local player
    /// reads "hidden card" and nothing else. Card text stays in the game's language; the few words the mod
    /// adds are English, like the rest of the mod.
    /// </summary>
    internal static class CardReader
    {
        /// <summary>The line spoken on focus, for every card, yours and the opponent's alike (Hearthstone
        /// Access's short line; the abilities are Down's): "Name, power 9, boosted, armor 2, Bleeding 2, Dominance
        /// met, order recharging, 2 turns, 7 provisions". <paramref name="pm"/> is the match's player manager
        /// (null outside a match): in a match the card is checked against what the local player may see, and
        /// stats read live. <paramref name="preview"/>: the card sits in a panel of cards (a choice, the deck or
        /// a graveyard), checked against what the game shows there (<see cref="Hidden"/>).</summary>
        /// <summary>Set by the battle screen: the name and title on the player panel beside a leader, its last line.</summary>
        internal static Func<Card, string> LeaderPlayer;

        public static string Summary(Card card, PlayerManager pm = null, bool preview = false)
        {
            if (card == null) return "";
            if (Hidden(card, pm, preview)) return "hidden card";
            var parts = new List<string> { Name(card) };
            var type = Type(card);
            if (type == ECardType.Unit) AddPower(parts, card);
            // In a match a leader's row already says "your leader" and its faction is a line below (Stats): the
            // short line is its name, ability and order. Elsewhere (the deck builder) the leader says what it is.
            else if (type != ECardType.Leader || pm == null) parts.Add(TypeWord(type));
            if (type == ECardType.Leader && pm == null) Add(parts, Faction(card));
            AddStatuses(parts, card);
            if (pm != null) AddCondition(parts, card);
            bool cooldown = pm != null && AddOrder(parts, card);
            if (!cooldown) AddTimer(parts, card);
            if (type != ECardType.Leader && (pm == null || At(card) == ELocation.Hand))
                Try(() => { int p = card.Template.Provision; if (p > 0) parts.Add(p + " provisions"); });
            return string.Join(", ", parts);
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

        /// <summary>The card itself, line by line, the summary first: what Up/Down step through. Then, as
        /// Hearthstone Access reads a card, what it does before what it is: each ability line, the categories,
        /// the kind and base power (<see cref="Stats"/>), the flavor, the illustrator. Keyword explanations are
        /// separate (<see cref="Explanations"/>), as the game keeps them beside the card.</summary>
        public static List<CardLine> Lines(Card card, PlayerManager pm = null, bool preview = false)
        {
            var lines = new List<CardLine> { new CardLine { Text = Summary(card, pm, preview) } };
            if (card == null || Hidden(card, pm, preview)) return lines;
            // Each keyword in the ability text is a <link=key> tag: that is how a line knows its keywords.
            foreach (var ability in Text(() => LocalizationHelper.GetTooltipDescription(card), clean: false).Split('\n'))
            {
                var line = Add(lines, UiText.Clean(ability));
                if (line == null) continue;
                foreach (System.Text.RegularExpressions.Match m in LinkTag.Matches(ability))
                    if (!line.Keywords.Contains(m.Groups[1].Value)) line.Keywords.Add(m.Groups[1].Value);
            }
            Add(lines, Text(() => LocalizationHelper.LocalizeCategories(card)));
            Add(lines, Stats(card));
            Add(lines, Text(() => LocalizationHelper.GetCardFlavor(card)));
            Add(lines, Text(() => LocalizationHelper.GetArtistDescription(card.Template)));
            // A leader in a match: the player beside it, name and title, as their panel shows.
            if (pm != null && Type(card) == ECardType.Leader && LeaderPlayer != null)
                Try(() => Add(lines, LeaderPlayer(card)));
            return lines;
        }

        private static readonly System.Text.RegularExpressions.Regex LinkTag = new System.Text.RegularExpressions.Regex("<link=\"?([^\">]+)\"?>");

        /// <summary>
        /// The card's keywords explained, "Order: activates manually…": the preview's keyword list, then its
        /// status list (a status's explanation sits with the keywords, under the status's name). With
        /// <paramref name="only"/>, just those keywords, in that order — the words of one line.
        /// </summary>
        public static List<string> Explanations(Card card, PlayerManager pm = null, IReadOnlyList<string> only = null, bool preview = false)
        {
            var found = new List<string>();
            if (card == null || Hidden(card, pm, preview)) return found;
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
        public static string Whole(Card card, PlayerManager pm = null, bool preview = false)
        {
            var parts = new List<string>();
            foreach (var line in Lines(card, pm, preview)) parts.Add(line.Text);
            parts.AddRange(Explanations(card, pm, preview: preview));
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

        /// <summary>Whether the local player sees this card's back. On the board and in hands that is the
        /// match's own visibility. With <paramref name="preview"/>, the card sits in a panel of cards (a choice,
        /// your deck, a graveyard): the cards a panel offers are made for it and sit where the board's rule
        /// reports them hidden, yet the panel shows them face up, so the game's preview rule
        /// (<c>IsVisibleToLocalPreview</c>, the one meant for such panels) decides; a card either rule shows is
        /// read. A check that throws reads a panel's card as shown, a board card as hidden.</summary>
        public static bool Hidden(Card card, PlayerManager pm, bool preview = false)
        {
            if (pm == null) return false;
            try
            {
                if (card.IsVisibleToLocal(pm)) return false;
                return !preview || !card.IsVisibleToLocalPreview(pm);
            }
            catch { return !preview; }
        }

        /// <summary>Just the name, or "hidden card" for one the local player can't see.</summary>
        public static string Name(Card card, PlayerManager pm, bool preview = false) => Hidden(card, pm, preview) ? "hidden card" : Name(card);

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

        // The order icon on a card in play: ready, waiting out the turn it arrived, recharging (with the turns
        // its hourglass counts down: a Cooldown runs on the card's timer, which the order's charges follow —
        // TapCardCharges.OnTimerChangedHandler), used up; with its charges when the card shows them. True when
        // the timer was said as the cooldown, so it isn't said again.
        private static bool AddOrder(List<string> parts, Card card)
        {
            var at = At(card);
            if (at != ELocation.Melee && at != ELocation.Ranged && at != ELocation.Leader) return false;
            bool cooldown = false;
            Try(() =>
            {
                if (!card.HasTapAbility()) return;
                var charges = card.Data.TapCharges;
                switch (charges.State)
                {
                    case ETapCardState.Available: parts.Add("order ready"); break;
                    case ETapCardState.SummoningSickness: parts.Add("order next turn"); break;
                    case ETapCardState.Recharging:
                        parts.Add("order recharging");
                        int t = TimerOf(card);
                        if (t > 0) { parts.Add(t == 1 ? "1 turn" : t + " turns"); cooldown = true; }
                        break;
                    case ETapCardState.Depleted: parts.Add("order used"); break;
                    case ETapCardState.Unavailable: parts.Add("order unavailable"); break;
                }
                if (card.Template.VisibleTapCharges || at == ELocation.Leader)
                    parts.Add(charges.Value == 1 ? "1 charge" : charges.Value + " charges");
            });
            return cooldown;
        }

        // A conditional keyword the game shows as met. Adrenaline, Initiative, Dominance and Bloodthirst cards
        // glow while their condition holds (the game's own Adrenaline text: "highlighted when their effect is
        // stronger"); the glow is the keyword handler's constant effect kept alive on the card, one keyword
        // per card. So a card is "Dominance met" exactly while that effect is on it: what is drawn, not the
        // rule worked out again.
        private static void AddCondition(List<string> parts, Card card)
        {
            Try(() =>
            {
                var glowing = KeywordGlow()?.m_CardsWithActiveConstantVFXs;
                if (glowing == null || !glowing.ContainsKey(card)) return;
                var vfx = glowing[card];
                if (vfx == null) return;
                string key = vfx.Keyword switch
                {
                    ECardKeyword.Adrenaline => "adrenaline",
                    ECardKeyword.Initiative => "initiative",
                    ECardKeyword.Dominance => "dominance",
                    ECardKeyword.Bloodthrist => "bloodthirst",
                    _ => null,
                };
                if (key == null) return;
                string fallback = char.ToUpperInvariant(key[0]) + key.Substring(1);
                parts.Add(GameText.Term(LocalizationHelper.KEYWORD_LOCALIZATION_KEY_PREFIX + key, fallback) + " met");
            });
        }

        private static GwentVisuals.Effects.CardKeywordAttackVFXHandler _glow;
        private static float _glowLookedAt = -100f;

        // The battle's keyword effect handler, kept while it lives; looked for at most once a second while there
        // is none (before a battle's board has loaded).
        private static GwentVisuals.Effects.CardKeywordAttackVFXHandler KeywordGlow()
        {
            try { if (_glow != null && _glow.Pointer != IntPtr.Zero && _glow.isActiveAndEnabled) return _glow; } catch { }
            _glow = null;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _glowLookedAt < 1f) return null;
            _glowLookedAt = now;
            try { _glow = UnityEngine.Object.FindObjectOfType<GwentVisuals.Effects.CardKeywordAttackVFXHandler>(); } catch { }
            return _glow;
        }

        // The card's timer, or -1 when it has none.
        private static int TimerOf(Card card)
        {
            try
            {
                int t = card.AbilityData.Timer.Value;
                return t >= 0 && t != AEntityTimer.NO_TIMER ? t : -1;
            }
            catch { return -1; }
        }

        // The number in the card's corner, named as the card's own text names it. One timer runs a Timer, a
        // Counter and a Cooldown alike (Elven Scribe's template starts its Counter as InitialTimer 3), so the
        // ability text tells which: "counter 3", "timer 1", "cooldown 2".
        private static void AddTimer(List<string> parts, Card card)
        {
            int t = TimerOf(card);
            if (t < 0) return;
            string word = TimerWord(card, out bool isCooldown);
            if (isCooldown && t == 0) return; // a cooldown run out is the order being ready, said as such
            parts.Add(GameText.Lower(word) + " " + t);
        }

        private static string TimerWord(Card card, out bool isCooldown)
        {
            string text = Text(() => LocalizationHelper.GetTooltipDescription(card), clean: false);
            string lower = text.ToLowerInvariant();
            bool Linked(string key) => lower.Contains("<link=" + key + ">") || lower.Contains("<link=\"" + key + "\"");
            isCooldown = Linked("cooldown");
            if (isCooldown) return GameText.Term("keyword_cooldown", "Cooldown");
            if (Linked("timer")) return GameText.Term("keyword_timer", "Timer");
            // A Counter line is plain text in the ability ("Counter: 3"), not a keyword link.
            string counter = GameText.Term("keyword_counter", "Counter");
            if (Linked("counter") || UiText.Clean(text).IndexOf(counter, StringComparison.OrdinalIgnoreCase) >= 0) return counter;
            return GameText.Term("keyword_timer", "Timer");
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
