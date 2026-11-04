using System;
using System.Collections.Generic;
using GwentGameplay;
using GwentGameplay.Taunts;
using GwentUnity.Taunts;
using GwentUnity.Vanity.Taunts;
using GwentVisuals;
using GwentVisuals.Effects;
using HarmonyLib;
using Localization;
using UnityEngine;

namespace GwentAccess
{
    /// <summary>
    /// Speaks the match as it plays out, the way Hearthstone Access does: what the opponent plays, then every
    /// effect grouped by its source — "Alzur's Thunder: damage 5 to opponent's Ducal Guard, power 0." — and the
    /// turn, pass, round and match banners with the scores a sighted player reads beside the board. A card is
    /// named with its side ("your", "opponent's"), as a sighted player sees which half of the board it is on;
    /// a card acting on itself alone is named once, side first: "Your Ard Feainn Heavy Cavalry, armor +1:
    /// power 3, armor 3."
    ///
    /// Everything comes from the game's own handlers, not a poll. Effects come from the visual effects handler,
    /// which the game only binds to the real match and which holds the game's logic while each animation
    /// plays, so a line is spoken when the effect shows on screen. Plays, turns, passes and rounds come from
    /// the match history — but the AI plays out whole imagined turns on copies of the game, each with its own
    /// history, so only the real match's history is listened to (<see cref="Real"/>).
    ///
    /// Words are the game's own wherever it has them (<see cref="GameText"/>): keyword and status names for
    /// effects, its banner texts, row names. Narration queues behind itself; the player's own keypresses cut
    /// it off, as any other speech.
    /// </summary>
    internal static class Narrator
    {
        private static bool _flushing;

        internal static void Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            // What was gathered so far comes first, in the order it happened: an opponent's card that has just
            // turned face up (its play line before its effects), lines gathered over the frame, the draws — but
            // not a round's deal still going on, which is said whole once it is over.
            if (!_flushing)
            {
                _flushing = true;
                try
                {
                    FlushSeen(false);
                    FlushGathered();
                    if (!Dealing) FlushDraws();
                }
                finally { _flushing = false; }
            }
            Speech.Say(text, false);
            Ui.LogScreen.Add(text); // kept for the battle log (O)
        }

        // ---- lines gathered over a frame ----

        private static readonly List<(string label, List<string> items)> _gathered = new List<(string, List<string>)>();

        /// <summary>One item of a line said once the frame is over: several cards whose order came off cooldown
        /// at the turn's start are one line, "Order ready: your Brokvar Hunter, opponent's Ard Feainn Tortoise."
        /// The same item twice in a frame (a card with two views raising the same event) is kept once.</summary>
        internal static void Gather(string label, string item)
        {
            if (string.IsNullOrEmpty(item)) return;
            foreach (var g in _gathered)
                if (g.label == label)
                {
                    if (!g.items.Contains(item)) g.items.Add(item);
                    return;
                }
            _gathered.Add((label, new List<string> { item }));
        }

        private static void FlushGathered()
        {
            if (_gathered.Count == 0) return;
            var lines = new List<(string label, List<string> items)>(_gathered);
            _gathered.Clear();
            foreach (var (label, items) in lines) SayEffect(null, label, null, items);
        }

        // ---- cards waiting to be seen ----

        private sealed class Awaited
        {
            public Card Card;
            public float At;
            public Action<Card> Seen;
            public Func<Card, bool> Settled;
            public Action<Card> Unseen;
        }

        private static readonly List<Awaited> _awaiting = new List<Awaited>();

        /// <summary>
        /// Says something about <paramref name="card"/> once the local player can see its face. The opponent's
        /// special is still face down when the game reports it played, and a flipped Ambush can report its flip
        /// a moment before its face shows; naming it then would only say "hidden card". <paramref name="settled"/>
        /// tells when the card has come to rest still face down (an Ambush on its row), for
        /// <paramref name="unseen"/> to say that much; a card that neither shows nor settles in three seconds
        /// is dropped.
        /// </summary>
        internal static void WhenSeen(Card card, Action<Card> seen, Func<Card, bool> settled = null, Action<Card> unseen = null)
        {
            if (card == null) return;
            _awaiting.Add(new Awaited { Card = card, At = Time.realtimeSinceStartup, Seen = seen, Settled = settled, Unseen = unseen });
        }

        private static void FlushSeen(bool timeouts)
        {
            for (int i = 0; i < _awaiting.Count;)
            {
                var w = _awaiting[i];
                bool hidden = CardReader.Hidden(w.Card, Players());
                bool settled = false;
                if (hidden && w.Settled != null) try { settled = w.Settled(w.Card); } catch { }
                bool stale = timeouts && Time.realtimeSinceStartup - w.At > 3f;
                if (hidden && !settled && !stale) { i++; continue; }
                _awaiting.RemoveAt(i);
                try
                {
                    if (!hidden) w.Seen(w.Card);
                    else if (settled) w.Unseen?.Invoke(w.Card);
                    else Plugin.LogInfo("[narr] card never shown, not said");
                }
                catch (Exception e) { Plugin.LogInfo("[narr] awaited card failed: " + e.Message); }
            }
        }

        /// <summary>A game banner as a sentence: the translation strings mostly carry no closing stop ("Your
        /// opponent passed"), and run into the scores after them they read as one sentence ("Your opponent passed
        /// You 8…" heard as "passed you").</summary>
        internal static string Sentence(string s)
        {
            s = (s ?? "").TrimEnd();
            if (s.Length == 0) return s;
            char last = s[s.Length - 1];
            return last == '.' || last == '!' || last == '?' || last == '…' ? s : s + ".";
        }

        /// <summary>Whether <paramref name="history"/> is the real match's, not one of the AI's imagined games.</summary>
        internal static bool Real(BattleHistory history, out GameController gc)
        {
            gc = Battle.Controller();
            try { return gc != null && history != null && gc.BattleHistory != null && gc.BattleHistory.Pointer == history.Pointer; }
            catch { return false; }
        }

        internal static PlayerManager Players()
        {
            try { return Battle.Controller()?.PlayerManager; } catch { return null; }
        }

        internal static bool Mine(EPlayerId who)
        {
            try { return who == Players().LocalUserId; } catch { return false; }
        }

        /// <summary>"You 23, opponent 19" — the score counters beside the board.</summary>
        internal static string Scores()
        {
            try
            {
                var me = Players().LocalUser;
                return "You " + me.CurrentScore + ", opponent " + me.OpponentPlayer.CurrentScore;
            }
            catch { return ""; }
        }

        internal static string Opponent() => GameText.Capitalized(GameText.Get("input_go_first_opponent", "Opponent"));

        // ---- names ----

        internal static string CardName(Card card) => CardReader.Name(card, Players());

        /// <summary>The name without the visibility check, for a card the player saw just before it went
        /// somewhere hidden.</summary>
        internal static string PlainName(Card card)
        {
            string name = "";
            try { name = UiText.Clean(LocalizationHelper.GetCardName(card)); } catch { }
            return name.Length > 0 && !name.StartsWith("[MISSING]") ? name : CardName(card);
        }

        /// <summary>Whether a mulligan is open for either player (its swaps are the mulligan screen's to speak).</summary>
        internal static bool InMulligan()
        {
            try
            {
                var reqs = Battle.Controller()?.RequestManager?.Requests;
                for (int i = 0; reqs != null && i < reqs.Count; i++)
                    if (reqs[i] != null && reqs[i].GetRequestType() == ERequestType.Mulligan) return true;
            }
            catch { }
            return false;
        }

        // ---- draws, gathered into one line ----

        private static readonly List<string> _myDraws = new List<string>();
        private static int _theirDraws;
        private static float _drawAt = -1;

        /// <summary>The match whose first turn has started (<see cref="NarrateTurn"/>). Draws before it are the
        /// opening deal, which the mulligan screen reads out card by card straight after.</summary>
        internal static IntPtr TurnsStartedIn;

        private static float _dealAt = -1;

        /// <summary>Whether a round's deal is going on: from the round's start until its draws are said (ten
        /// seconds at most). The game deals the cards one at a time, alternating players, with the round
        /// banner in between; the deal is said as one line once it is over, not cut up by the banner.</summary>
        internal static bool Dealing => _dealAt >= 0 && Time.realtimeSinceStartup - _dealAt < 10f;

        /// <summary>A round started: its deal follows (<see cref="Dealing"/>).</summary>
        internal static void Deal() => _dealAt = Time.realtimeSinceStartup;

        /// <summary>A turn started: any deal is over, and its draws are said before the turn's banner.</summary>
        internal static void EndDeal() => _dealAt = -1;

        /// <summary>A card drawn: yours by name, the opponent's as a count (their hand is face down).</summary>
        internal static void Drawn(Card card, EPlayerId who)
        {
            try { if (Battle.Controller()?.Pointer != TurnsStartedIn) return; } catch { return; }
            if (Mine(who)) _myDraws.Add(CardName(card));
            else _theirDraws++;
            _drawAt = Time.realtimeSinceStartup;
        }

        /// <summary>Per frame: says what was gathered over the last frame, cards that have turned face up, and
        /// the draws once a burst is over (a round's deal waits longer: its cards fly in one by one).</summary>
        public static void Tick()
        {
            FlushSeen(true);
            FlushGathered();
            float wait = Dealing ? 2.5f : 1.2f;
            if (_drawAt >= 0 && Time.realtimeSinceStartup - _drawAt >= wait) FlushDraws();
        }

        /// <summary>The draws as one line: "You draw 3 cards: Wild Hunt Hound, Adda: Striga, Ge'els. Opponent
        /// draws 3 cards." The opponent's are a count: their cards come in face down.</summary>
        private static void FlushDraws()
        {
            if (_drawAt < 0) return;
            _drawAt = -1;
            _dealAt = -1;
            var mine = new List<string>(_myDraws);
            int theirs = _theirDraws;
            _myDraws.Clear();
            _theirDraws = 0;
            var parts = new List<string>();
            if (mine.Count > 0) parts.Add("You draw " + Cards(mine.Count) + ": " + string.Join(", ", mine) + ".");
            if (theirs > 0) parts.Add(Opponent() + " draws " + Cards(theirs) + ".");
            if (parts.Count > 0) Say(string.Join(" ", parts));
        }

        private static string Cards(int n) => n == 1 ? "1 card" : n + " cards";

        internal static bool IsUnit(Card card)
        {
            try { return (card.Template.Type & ECardType.Unit) != 0; } catch { return false; }
        }

        /// <summary>A card the local player can see where it is: on the board, or in their own hand. With
        /// <paramref name="graveyard"/>, also in a graveyard (where a destroyed card has already gone).</summary>
        internal static bool Visible(Card card, bool graveyard = false)
        {
            try
            {
                var pos = card.Position;
                switch (pos.Location)
                {
                    case ELocation.Melee:
                    case ELocation.Ranged:
                    case ELocation.Leader: return true;
                    case ELocation.Hand: return Mine(pos.PlayerId);
                    case ELocation.Graveyard: return graveyard;
                    default: return false;
                }
            }
            catch { return false; }
        }

        internal static bool InOpponentHand(Card card)
        {
            try { return card.Position.Location == ELocation.Hand && !Mine(card.Position.PlayerId); }
            catch { return false; }
        }

        /// <summary>"Вражеский рукопашный ряд": the side as the game words it, then the row.</summary>
        internal static string RowName(EPlayerId side, ELocation row)
        {
            string key = row == ELocation.Melee ? "keyword_melee" : row == ELocation.Ranged ? "keyword_ranged" : null;
            if (key == null) return null;
            string name = GameText.Term(key, row == ELocation.Melee ? "Melee row" : "Ranged row");
            string adjective = Mine(side) ? GameText.Term("keyword_friendly", "Your") : GameText.Term("keyword_enemy", "Enemy");
            return adjective + " " + GameText.Lower(name);
        }

        /// <summary>Who caused an effect: a card, a status ticking (bleeding), or a row effect.</summary>
        internal static string Source(IEntity attacker)
        {
            if (attacker == null) return null;
            try
            {
                var o = attacker.Cast<Il2CppSystem.Object>();
                var card = o.TryCast<Card>();
                if (card != null) return CardName(card);
                var status = o.TryCast<CardStatus>();
                if (status != null) return StatusName(status.Status);
                var row = o.TryCast<Location>();
                if (row != null) return RowName(row.PlayerId, row.Loc);
            }
            catch { }
            return null;
        }

        /// <summary>A status's name, as its icon shows it. A combined token (several statuses at once, as a
        /// purify takes them all off: "card_token_-9", every status but Ambush) has no string of its own and is
        /// named status by status.</summary>
        internal static string StatusName(ECardToken token)
        {
            if (!Single(token))
            {
                var names = new List<string>();
                for (int bit = 0; bit < 16; bit++)
                    if (((int)token & (1 << bit)) != 0) names.Add(StatusName((ECardToken)(1 << bit)));
                return string.Join(", ", names);
            }
            string name = "";
            try { name = UiText.Clean(LocalizationHelper.GetCardStatus(token)); } catch { }
            if (name.StartsWith("[MISSING]")) name = "";
            int colon = name.IndexOf(':');
            if (colon > 0) name = name.Substring(0, colon).Trim();
            return name.Length > 0 ? name : token.ToString();
        }

        /// <summary>Whether <paramref name="token"/> is one status, not several (or All).</summary>
        internal static bool Single(ECardToken token)
        {
            int v = (int)token;
            return v > 0 && (v & (v - 1)) == 0;
        }

        internal static string RowEffectName(ELocationToken token)
        {
            string key = token switch
            {
                ELocationToken.Frost => "keyword_frost",
                ELocationToken.Fog => "keyword_fog",
                ELocationToken.Rain => "keyword_rain",
                ELocationToken.BloodMoon => "keyword_blood_moon",
                ELocationToken.DragonsDream => "keyword_dragons_dream",
                ELocationToken.SkelligeStorm => "keyword_storm",
                _ => "keyword_" + token.ToString().ToLowerInvariant(),
            };
            return GameText.Term(key, token.ToString());
        }

        /// <summary>"Темерский барабанщик 5": a card's name with its power number (and armor) as drawn now, for
        /// the opponent's play line. Effects name their cards with <see cref="Target"/>.</summary>
        internal static string WithPower(Card card)
        {
            string text = CardName(card);
            if (!IsUnit(card) || CardReader.Hidden(card, Players())) return text;
            try
            {
                var power = card.Data.Power;
                text += " " + power.CurrentPower;
                if (power.CurrentArmor > 0) text += ", " + GameText.Term("keyword_armor", "armor") + " " + power.CurrentArmor;
            }
            catch { }
            return text;
        }

        /// <summary>"your" or "opponent's": whose side of the board a card is on (where it is now).</summary>
        internal static string Side(Card card)
        {
            try { return Mine(card.Position.PlayerId) ? "your" : "opponent's"; } catch { return null; }
        }

        /// <summary>"your Ard Feainn Heavy Cavalry": a card named with its side. <paramref name="name"/> overrides
        /// the name (<see cref="PlainName"/> for a card seen just before it went somewhere hidden).</summary>
        internal static string Owned(Card card, string name = null)
        {
            name ??= CardName(card);
            string side = Side(card);
            return side != null ? side + " " + name : name;
        }

        /// <summary>"power 3, armor 3": a unit's power (and armor) as drawn now, worded as the card reader words
        /// them; empty for a card that shows no power, or one hidden from you.</summary>
        internal static string Stats(Card card)
        {
            if (!IsUnit(card) || CardReader.Hidden(card, Players())) return "";
            try
            {
                var power = card.Data.Power;
                string text = "power " + power.CurrentPower;
                if (power.CurrentArmor > 0) text += ", " + GameText.Lower(GameText.Term("keyword_armor", "armor")) + " " + power.CurrentArmor;
                return text;
            }
            catch { return ""; }
        }

        /// <summary>"opponent's Ducal Guard, power 0": a target with its side and what the effect left it at.</summary>
        internal static string Target(Card card)
        {
            string stats = Stats(card);
            return stats.Length > 0 ? Owned(card) + ", " + stats : Owned(card);
        }

        /// <summary>The first letter raised, for a phrase that starts a line ("hidden card", "your …").</summary>
        internal static string First(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>"Source: label join target, target.", as in "Tourney Joust: shield on your Ard Feainn Heavy
        /// Cavalry, power 2, armor 2." <paramref name="join"/> is the word between the effect and its targets
        /// ("to", "on", "from"), or null for an effect that takes them straight ("destroy opponent's …").
        /// Without a source: "Label: target, target." (your draws: "Draw: Wild Hunt Hound, Adda: Striga.").</summary>
        internal static void SayEffect(string source, string label, string join, List<string> targets)
        {
            if (targets.Count == 0) return;
            string list = string.Join(", ", targets);
            if (source == null) { Say(label + ": " + list + "."); return; }
            Say(First(source) + ": " + GameText.Lower(label) + (join != null ? " " + join : "") + " " + list + ".");
        }

        /// <summary>One thing an effect happened to, named once, side first: "Your Ard Feainn Heavy Cavalry,
        /// armor +1: power 3, armor 3." Used for a card acting on itself, and for an effect with no source to
        /// name (a shield used up, a unit moved). <paramref name="stats"/> may be empty: "Opponent's Wild Hunt
        /// Hound, to hand."</summary>
        internal static void SaySelf(string subject, string label, string stats)
        {
            Say(First(subject) + ", " + GameText.Lower(label) + (stats.Length > 0 ? ": " + stats : "") + ".");
        }

        /// <summary>
        /// An effect on cards, each named with its side (<see cref="Target"/>, or just <see cref="Owned"/> without
        /// <paramref name="power"/>): "Alzur's Thunder: damage 5 to opponent's Ducal Guard, power 0." When the
        /// one card hit is the source itself, or there is no source, the card is named once (<see cref="SaySelf"/>).
        /// <paramref name="extra"/> is a target that isn't a named card (cards lit up in the opponent's hand).
        /// </summary>
        internal static void SayOnCards(string source, IEntity attacker, string label, string join, List<Card> cards, bool power, string extra = null)
        {
            if (cards.Count == 0 && extra == null) return;
            if (cards.Count == 1 && extra == null && (source == null || Same(attacker, cards[0])))
            {
                SaySelf(Owned(cards[0]), label, power ? Stats(cards[0]) : "");
                return;
            }
            var targets = new List<string>();
            foreach (var card in cards) targets.Add(power ? Target(card) : Owned(card));
            if (extra != null) targets.Add(extra);
            SayEffect(source, label, join, targets);
        }

        /// <summary>Cards destroyed, each named with its side, then what destroyed them when there is a source
        /// other than the card itself: "Opponent's Deafening Siren destroyed by Geralt: Igni." A unit the damage
        /// took to 0 has no source here: the damage line just before already named it.</summary>
        internal static void SayDestroyed(string source, IEntity attacker, List<Card> cards)
        {
            if (cards.Count == 0) return;
            var names = cards.ConvertAll(c => Owned(c));
            bool self = cards.Count == 1 && Same(attacker, cards[0]);
            Say(First(string.Join(", ", names)) + " destroyed" + (source != null && !self ? " by " + source : "") + ".");
        }

        /// <summary>Whether an effect's source is <paramref name="card"/> itself.</summary>
        internal static bool Same(IEntity attacker, Card card)
        {
            try { return attacker != null && card != null && attacker.Cast<Il2CppSystem.Object>().Pointer == card.Pointer; }
            catch { return false; }
        }

        internal static List<Card> List(Il2CppSystem.Collections.Generic.List<Card> cards)
        {
            var result = new List<Card>();
            try { for (int i = 0; cards != null && i < cards.Count; i++) if (cards[i] != null) result.Add(cards[i]); } catch { }
            return result;
        }

        internal static void Guard(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Plugin.LogInfo("[narr] " + what + " failed: " + e.Message); }
        }
    }

    // ---- effects, as they show on screen ----

    /// <summary>Damage, boost, strengthen, weaken, armor, heal and set: one line per effect, each visible target
    /// with its power after it — and "destroyed" for a unit the damage took to 0.</summary>
    [HarmonyPatch(typeof(AbilityVFXHandler), nameof(AbilityVFXHandler.OnCardPowerAttack))]
    internal static class NarratePowerAttack
    {
        static void Postfix(CardPowerAttack __0) => Narrator.Guard("power", () =>
        {
            var a = __0;
            if (a == null) return;
            string label = Label(a.Operation, a.PowerType);
            // The armor sign sits on its number ("armor +1"); "damage 6", "armor 0" take a space.
            string gap = label.EndsWith("+") || label.EndsWith("−") ? "" : " ";
            if (a.Operation == ECardPowerOp.Add || a.Operation == ECardPowerOp.Remove || a.Operation == ECardPowerOp.Set)
                label += gap + Math.Abs(a.Value);
            else if (Amount(a) is int amount) label += " " + amount; // heal and reset carry no value; the change does
            // "damage 6 to …", "boost 4 to …"; a value set or restored sits on the card ("power 5 on …").
            string join = a.Operation == ECardPowerOp.Add || a.Operation == ECardPowerOp.Remove ? "to" : "on";
            var targets = new List<Card>();
            var killed = new List<Card>();
            int inTheirHand = 0;
            foreach (var card in Narrator.List(a.Targets))
            {
                if (!Narrator.Visible(card))
                {
                    if (Narrator.InOpponentHand(card)) inTheirHand++;
                    continue;
                }
                targets.Add(card);
                try
                {
                    var at = card.Position.Location;
                    if (Narrator.IsUnit(card) && (at == ELocation.Melee || at == ELocation.Ranged) && card.Data.Power.CurrentPower <= 0)
                        killed.Add(card);
                }
                catch { }
            }
            // Face-down cards in the opponent's hand light up as they're hit: a sighted player sees how many.
            string hand = inTheirHand == 0 ? null
                : inTheirHand == 1 ? "1 card in the opponent's hand" : inTheirHand + " cards in the opponent's hand";
            Narrator.SayOnCards(Narrator.Source(a.Attacker), a.Attacker, label, join, targets, power: true, extra: hand);
            Narrator.SayDestroyed(null, null, killed);
        });

        /// <summary>How much the power changed, when it changed by the same amount on every target.</summary>
        private static int? Amount(CardPowerAttack a)
        {
            int? amount = null;
            try
            {
                var deltas = a.PowerType == EPowerType.Armor ? a.ArmorDeltas : a.PowerDeltas;
                for (int i = 0; deltas != null && i < deltas.Count; i++)
                {
                    int d = Math.Abs(deltas[i]);
                    if (d == 0) continue;
                    if (amount != null && amount != d) return null;
                    amount = d;
                }
            }
            catch { return null; }
            return amount;
        }

        private static string Label(ECardPowerOp op, EPowerType type)
        {
            if (type == EPowerType.Armor) return GameText.Term("keyword_armor", "Armor") + (op == ECardPowerOp.Remove ? " −" : op == ECardPowerOp.Add ? " +" : "");
            if (op == ECardPowerOp.Set) return GameText.Get("card_strength", "Power");
            if (type == EPowerType.BasePower)
                return op == ECardPowerOp.Add ? GameText.Term("keyword_strengthen", "Strengthen")
                    : op == ECardPowerOp.Remove ? GameText.Term("keyword_weaken", "Weaken")
                    : GameText.Term("keyword_reset", "Reset");
            return op == ECardPowerOp.Add ? GameText.Term("keyword_boost", "Boost")
                : op == ECardPowerOp.Remove ? GameText.Term("keyword_damage", "Damage")
                : GameText.Term("keyword_heal", "Heal");
        }
    }

    [HarmonyPatch(typeof(AbilityVFXHandler), nameof(AbilityVFXHandler.OnCardDestroyAttack))]
    internal static class NarrateDestroyAttack
    {
        static void Postfix(CardDestroyAttack __0) => Narrator.Guard("destroy", () =>
        {
            if (__0 == null) return;
            var targets = new List<Card>();
            foreach (var card in Narrator.List(__0.Targets))
                if (Narrator.Visible(card, graveyard: true)) targets.Add(card);
            Narrator.SayDestroyed(Narrator.Source(__0.Attacker), __0.Attacker, targets);
        });
    }

    /// <summary>Cards banished. A used special or stratagem banishing itself is the card leaving after it
    /// resolved, not an effect — left out.</summary>
    [HarmonyPatch(typeof(AbilityVFXHandler), nameof(AbilityVFXHandler.OnCardBanishAttack))]
    internal static class NarrateBanishAttack
    {
        static void Postfix(CardBanishAttack __0) => Narrator.Guard("banish", () =>
        {
            if (__0 == null) return;
            var targets = new List<Card>();
            foreach (var card in Narrator.List(__0.Targets))
                if (Narrator.Visible(card) && !Narrator.Same(__0.Attacker, card)) targets.Add(card);
            Narrator.SayOnCards(Narrator.Source(__0.Attacker), __0.Attacker, GameText.Term("keyword_banish", "Banish"), null, targets, power: false);
        });
    }

#if GWENT // Thronebreaker's AbilityVFXHandler has no OnCardStatusAttack (nor a CardStatusAttack type); its hook is still to be found
    /// <summary>A status put on or taken off: "Tourney Joust: shield on your Ard Feainn Heavy Cavalry, power 2,
    /// armor 2." Removal is worded with the game's "purify": "… purify shield from …"; a purify that takes
    /// every status off (one combined token) is just the game's word: "Imperial Diviner: purify opponent's
    /// Ducal Guard, power 4." An Ambush coming off is its card flipping face up, said by
    /// <see cref="NarrateAmbushFlip"/>.</summary>
    [HarmonyPatch(typeof(AbilityVFXHandler), nameof(AbilityVFXHandler.OnCardStatusAttack))]
    internal static class NarrateStatusAttack
    {
        static void Postfix(CardStatusAttack __0) => Narrator.Guard("status", () =>
        {
            var a = __0;
            if (a == null) return;
            var token = a.DeltaStatus;
            if (token == 0) try { if (a.ChangedStatuses != null && a.ChangedStatuses.Count > 0) token = a.ChangedStatuses[0]; } catch { }
            if (token == ECardToken.Ambush) return;
            bool remove = a.DeltaOperation == ETokenOp.Remove;
            string label = Narrator.StatusName(token);
            string source = Narrator.Source(a.Attacker);
            string join = "on";
            if (remove && !Narrator.Single(token))
            {
                label = GameText.Term("keyword_purify", "Purify");
                join = null;
            }
            else if (remove)
            {
                // A status ending itself — a shield used up — has no source but the status already named.
                if (source == label) source = null;
                label = GameText.Term("keyword_purify", "Remove") + " " + GameText.Lower(label);
                join = "from";
            }
            var targets = new List<Card>();
            foreach (var card in Narrator.List(a.Targets))
                if (Narrator.Visible(card)) targets.Add(card);
            Narrator.SayOnCards(source, a.Attacker, label, join, targets, power: true);
        });
    }
#endif

    [HarmonyPatch(typeof(AbilityVFXHandler), nameof(AbilityVFXHandler.OnCardTransformAttack))]
    internal static class NarrateTransformAttack
    {
        static void Postfix(CardTransformAttack __0) => Narrator.Guard("transform", () =>
        {
            if (__0 == null) return;
            var targets = new List<Card>();
            foreach (var card in Narrator.List(__0.Targets))
                if (Narrator.Visible(card)) targets.Add(card);
            Narrator.SayOnCards(Narrator.Source(__0.Attacker), __0.Attacker, GameText.Term("keyword_transform", "Transform"), null, targets, power: true);
        });
    }

    [HarmonyPatch(typeof(AbilityVFXHandler), nameof(AbilityVFXHandler.OnCardResetAttack))]
    internal static class NarrateResetAttack
    {
        static void Postfix(CardResetAttack __0) => Narrator.Guard("reset", () =>
        {
            if (__0 == null) return;
#if GWENT
            // A purify comes with a reset of the statuses only (EResetMode.Tokens), already said as "purify";
            // a reset is the keyword's only when the power or armor goes back (seen in a practice match: Imperial
            // Diviner's purify read "reset your Naglfar Taskmaster, power 5" with nothing changed).
            var mode = __0.ResetMode;
            if ((mode & (EResetMode.Power | EResetMode.TemplatePower | EResetMode.Armor)) == 0)
            {
                Plugin.LogInfo("[narr] reset not said, mode " + mode);
                return;
            }
#endif
            var targets = new List<Card>();
            foreach (var card in Narrator.List(__0.Targets))
                if (Narrator.Visible(card)) targets.Add(card);
            Narrator.SayOnCards(Narrator.Source(__0.Attacker), __0.Attacker, GameText.Term("keyword_reset", "Reset"), null, targets, power: true);
        });
    }

#if GWENT // Thronebreaker has no LocationStatusAttack, and its CardSpawnVFXHandler no HandleOnCardsSpawned
    /// <summary>A row effect laid or cleared: "White Frost: frost on enemy ranged.", "Naglfar Taskmaster: purify
    /// frost from enemy melee." A row clearing its own effect, or an effect with no source, names the row
    /// once: "Enemy melee, purify frost."</summary>
    [HarmonyPatch(typeof(AbilityVFXHandler), nameof(AbilityVFXHandler.OnLocationTokensAttack))]
    internal static class NarrateRowAttack
    {
        static void Postfix(LocationStatusAttack __0) => Narrator.Guard("row", () =>
        {
            var a = __0;
            if (a == null) return;
            var token = a.DeltaStatus;
            if (token == 0) try { if (a.ChangedStatuses != null && a.ChangedStatuses.Count > 0) token = a.ChangedStatuses[0]; } catch { }
            string label = Narrator.RowEffectName(token);
            bool remove = a.DeltaOperation == ETokenOp.Remove;
            if (remove) label = GameText.Term("keyword_purify", "Remove") + " " + GameText.Lower(label);
            var rows = new List<string>();
            try
            {
                for (int i = 0; a.Targets != null && i < a.Targets.Count; i++)
                {
                    var row = a.Targets[i];
                    string name = row != null ? Narrator.RowName(row.PlayerId, row.Loc) : null;
                    if (name != null) rows.Add(name);
                }
            }
            catch { }
            if (rows.Count == 0) return;
            string source = Narrator.Source(a.Attacker);
            if (source == null || (rows.Count == 1 && source == rows[0]))
            {
                Narrator.SaySelf(string.Join(", ", rows), label, "");
                return;
            }
            Narrator.SayEffect(source, label, remove ? "from" : "on", rows.ConvertAll(GameText.Lower));
        });
    }

    /// <summary>Cards created on the board or in your hand: "Patricidal Fury: spawn opponent's Deafening Siren
    /// on enemy ranged, power 2."</summary>
    [HarmonyPatch(typeof(CardSpawnVFXHandler), nameof(CardSpawnVFXHandler.HandleOnCardsSpawned))]
    internal static class NarrateSpawn
    {
        static void Postfix(Il2CppSystem.Collections.Generic.List<SpawnData> __0) => Narrator.Guard("spawn", () =>
        {
            if (__0 == null) return;
            string label = GameText.Term("keyword_spawn", "Spawn");
            for (int i = 0; i < __0.Count; i++)
            {
                var spawn = __0[i];
                var card = spawn?.SpawnedCard;
                if (card == null || !Narrator.Visible(card)) continue;
                string where = "";
                try
                {
                    string row = Narrator.RowName(card.Position.PlayerId, card.Position.Location);
                    if (row != null) where = " on " + GameText.Lower(row);
                }
                catch { }
                string stats = Narrator.Stats(card);
                if (spawn.SpawnerCard == null) { Narrator.SaySelf(Narrator.Owned(card), label + where, stats); continue; }
                string what = Narrator.Owned(card) + where + (stats.Length > 0 ? ", " + stats : "");
                Narrator.SayEffect(Narrator.CardName(spawn.SpawnerCard), label, null, new List<string> { what });
            }
        });
    }
#endif

    // ---- cards moving: draws, summons, row moves, returns ----

    /// <summary>
    /// Every card movement the board animates, after the game moved the card (its position is the new one):
    /// summoned or resurrected onto the board, moved to another row, seized, returned to hand or deck,
    /// discarded, drawn, revealed ("Revealed from hand: opponent's Nekker Warrior.", gathered over the frame
    /// so a reveal of several cards is one line). Plays (from the hand or the play stack), spawns, mulligan swaps and deaths are
    /// spoken elsewhere or by the screen in front, so they're left out here. Draws come in bursts — two or
    /// three at a round start — and are gathered into one line (<see cref="Narrator.Tick"/>).
    /// </summary>
    [HarmonyPatch(typeof(BattleMovementManager), "HandleCardMoved")]
    internal static class NarrateMove
    {
        private const ELocation Board = ELocation.Melee | ELocation.Ranged;

        static void Postfix(Card __0, CardPosition __1) => Narrator.Guard("move", () =>
        {
            var card = __0;
            if (card == null) return;
            var from = __1;
            var to = card.Position;
            ELocation a = from.Location, b = to.Location;
            if (a == b && from.PlayerId == to.PlayerId) return;
            if (Narrator.InMulligan() || a == ELocation.MulliganStack || b == ELocation.MulliganStack) return;
            // A card shown to both players flies to the reveal spot over the board, from a deck or a hand.
            if (b == ELocation.RevealStack)
            {
                if (CardReader.Hidden(card, Narrator.Players())) return;
                string shown = "Revealed" + (a == ELocation.Hand ? " from hand" : a == ELocation.Deck ? " from deck" : "");
                Narrator.Gather(shown, Narrator.Owned(card));
                return;
            }
            // Plays, specials resolving, spawns and coming back from the reveal spot have their own lines or none.
            if ((a & (ELocation.Hand | ELocation.PlayStack | ELocation.EventExecuting | ELocation.SpawningPool | ELocation.RevealStack | ELocation.Void)) != 0
                && !(a == ELocation.Hand && (b == ELocation.Graveyard || b == ELocation.Deck))) return;
            if ((b & (ELocation.PlayStack | ELocation.EventExecuting | ELocation.Void | ELocation.Leader)) != 0) return;

            if (a == ELocation.Deck && b == ELocation.Hand) { Narrator.Drawn(card, to.PlayerId); return; }

            string label = null;
            bool row = false;
            if ((b & Board) != 0)
            {
                row = true;
                if (a == ELocation.Deck) label = GameText.Term("keyword_summon", "Summon");
                else if (a == ELocation.Graveyard) label = GameText.Term("keyword_resurrect", "Resurrect");
                else if ((a & Board) != 0 && from.PlayerId != to.PlayerId) label = GameText.Term("keyword_seize", "Seize");
                else if ((a & Board) != 0) label = GameText.Get("arena_card_set_mechanical_move", "Move");
            }
            else if (b == ELocation.Hand) label = "to hand";
            else if (b == ELocation.Deck) label = "to " + GameText.Get("panel_deck_name", "deck").ToLowerInvariant();
            else if (b == ELocation.Graveyard && a == ELocation.Hand) label = GameText.Term("keyword_discard", "Discard");
            else if (b == ELocation.Graveyard && a == ELocation.Deck) label = "to " + GameText.Get("panel_graveyard_name", "graveyard").ToLowerInvariant();
            if (label == null) return; // a death: the damage or destroy line already said it

            // The card is the subject, named once with its side (the one it is on now): "Your Ard Feainn Heavy
            // Cavalry, move to ally melee: power 3, armor 6." / "Opponent's Wild Hunt Hound, to hand."
            if (row)
            {
                string name = Narrator.RowName(to.PlayerId, b);
                if (name != null) label += " to " + GameText.Lower(name);
                Narrator.SaySelf(Narrator.Owned(card), label, Narrator.Stats(card));
            }
            else
            {
                // Going somewhere hidden (a hand, a deck): named as it was seen leaving, if it was seen at all.
                bool seen = (a & Board) != 0 || a == ELocation.Graveyard || (a == ELocation.Hand && Narrator.Mine(from.PlayerId));
                Narrator.SaySelf(Narrator.Owned(card, seen ? Narrator.PlainName(card) : Narrator.CardName(card)), label, "");
            }
        });
    }

    /// <summary>
    /// The banner over a player's portrait while they act: "Противник размышляет", "Противник меняет
    /// карты". Pass banners are left to the pass line, which carries the scores. The game re-shows a banner
    /// while the wait goes on (the opponent's mulligan, a long think); it is said once until the turn changes
    /// (<see cref="Forget"/>) or another banner replaces it.
    /// </summary>
    [HarmonyPatch(typeof(UIPlayerPassPanel), nameof(UIPlayerPassPanel.ShowMessage))]
    internal static class NarrateStatusBanner
    {
        private static string _last;

        internal static void Forget() => _last = null;

        static void Postfix(string __2, bool __3, bool __4) => Narrator.Guard("banner", () =>
        {
            if (__3 || string.IsNullOrEmpty(__2)) return;
            if (__2.Contains("pass")) return;
            string text = __4 ? GameText.Get(__2, null) : UiText.Clean(__2);
            if (string.IsNullOrEmpty(text) || text.Contains("{{")) return;
            if (text == _last) return;
            _last = text;
            Narrator.Say(text + ".");
        });
    }

    // ---- plays, turns, passes, rounds: the real match's history ----

    /// <summary>
    /// The opponent's play, as the card lands: "Противник: Ледяной великан 7, Вражеский рукопашный ряд." Your
    /// own plays aren't repeated — you just made them — but their effects are spoken.
    ///
    /// A special is still face down when the game reports it played, and turns over a moment later as it
    /// resolves: its line waits until its face shows (<see cref="Narrator.WhenSeen"/>), so it is never just
    /// "hidden card". An Ambush stays face down on its row; a sighted player sees a card back land there, and
    /// that is what is said: "Opponent: Ambush, enemy melee row."
    /// </summary>
    [HarmonyPatch(typeof(BattleHistory), nameof(BattleHistory.OnPlayedCardHandler))]
    internal static class NarratePlay
    {
        static void Postfix(BattleHistory __instance, EPlayerId __0, Card __1) => Narrator.Guard("play", () =>
        {
            if (!Narrator.Real(__instance, out _) || Narrator.Mine(__0) || __1 == null) return;
            if (!CardReader.Hidden(__1, Narrator.Players())) { Say(__1); return; }
            Narrator.WhenSeen(__1, Say, settled: FaceDownOnRow, unseen: SayAmbush);
        });

        private static void Say(Card card)
        {
            string what = Narrator.WithPower(card) + Row(card);
            // What the card does, as the game shows it in the card's tooltip when the opponent plays it (its
            // gameplay option "Display tooltips for cards played by opponent"); Hearthstone Access and Accessible
            // Arena read it too. Off with the mod's option Battle.OpponentCardText.
            string text = "";
#if GWENT
            if (ModConfig.OpponentCardText)
#endif
                try { text = CardReader.Abilities(card, Battle.Controller()?.PlayerManager); } catch { }
            Narrator.Say(Narrator.Opponent() + ": " + what + "." + (text.Length > 0 ? " " + text : ""));
        }

        /// <summary>", Вражеский рукопашный ряд": the row the card is on, if it is on one.</summary>
        private static string Row(Card card)
        {
            try
            {
                string row = Narrator.RowName(card.Position.PlayerId, card.Position.Location);
                if (row != null) return ", " + row;
            }
            catch { }
            return "";
        }

        /// <summary>A face-down card come to rest on a row: an Ambush.</summary>
        private static bool FaceDownOnRow(Card card)
        {
            var at = card.Position.Location;
            return (at == ELocation.Melee || at == ELocation.Ranged) && card.Data.Statuses.Contains(ECardToken.Ambush);
        }

        private static void SayAmbush(Card card) =>
            Narrator.Say(Narrator.Opponent() + ": " + GameText.Term("keyword_ambush", "Ambush") + Row(card) + ".");
    }

    /// <summary>The opponent using an order — a unit's, a stratagem's or the leader's: "Противник: Приказ:
    /// Бодрость." The order icon flashes and the charges drop, so a sighted player sees it; its effects follow
    /// in their own lines. The game calls activating an order "tapping" the card.</summary>
    [HarmonyPatch(typeof(BattleHistory), nameof(BattleHistory.OnCardTapEndedHandler))]
    internal static class NarrateOrder
    {
        static void Postfix(BattleHistory __instance, EPlayerId __0, Card __1, ERequestConfirmStatus __2) => Narrator.Guard("order", () =>
        {
            if (!Narrator.Real(__instance, out _) || Narrator.Mine(__0) || __1 == null) return;
            if (__2 == ERequestConfirmStatus.Cancelled) return;
            Plugin.LogInfo("[narr] order " + __2);
            Narrator.Say(Narrator.Opponent() + ": " + GameText.Term("keyword_order", "Order") + ": " + Narrator.CardName(__1) + ".");
        });
    }

    [HarmonyPatch(typeof(BattleHistory), nameof(BattleHistory.OnTurnStartedHandler))]
    internal static class NarrateTurn
    {
        static void Postfix(BattleHistory __instance, EPlayerId __0) => Narrator.Guard("turn", () =>
        {
            if (!Narrator.Real(__instance, out var gc)) return;
            Narrator.TurnsStartedIn = gc.Pointer;
            Narrator.EndDeal();
            NarrateStatusBanner.Forget();
            // A player who passed gets their turns skipped straight away; there's no turn to announce.
            try
            {
                var me = gc.PlayerManager.LocalUser;
                if ((Narrator.Mine(__0) ? me : me.OpponentPlayer).HasPassed) return;
            }
            catch { }
            string banner = Narrator.Mine(__0)
                ? GameText.Get("gwent_message_your_turn_main", "Your turn")
                : GameText.Get("gwent_message_opponent_turn_main", "Opponent's turn");
            Narrator.Say(banner + ". " + Narrator.Scores() + ".");
        });
    }

    [HarmonyPatch(typeof(BattleHistory), nameof(BattleHistory.OnPlayerPassedHandler))]
    internal static class NarratePass
    {
        /// <summary>When you last passed, for the battle screen to confirm its pass button took.</summary>
        internal static float MineAt = -1;

        static void Postfix(BattleHistory __instance, EPlayerId __0, bool __1) => Narrator.Guard("pass", () =>
        {
            if (!Narrator.Real(__instance, out _) || !__1) return;
            if (Narrator.Mine(__0)) MineAt = Time.realtimeSinceStartup;
            string banner = Narrator.Mine(__0)
                ? GameText.Get("gwent_message_player_pass_main", "You passed.")
                : GameText.Get("gwent_opponent_passed_turn", "Opponent passed.");
            Narrator.Say(Narrator.Sentence(banner) + " " + Narrator.Scores() + ".");
        });
    }

    /// <summary>A round started: the deal that follows is gathered whole (<see cref="Narrator.Dealing"/>).</summary>
    [HarmonyPatch(typeof(BattleHistory), nameof(BattleHistory.OnRoundStartedHandler))]
    internal static class NarrateRoundDeal
    {
        static void Postfix(BattleHistory __instance) => Narrator.Guard("round deal", () =>
        {
            if (Narrator.Real(__instance, out _)) Narrator.Deal();
        });
    }

    /// <summary>The round-end panel's title — won, lost or draw — with the final scores.</summary>
    [HarmonyPatch(typeof(BattleHistory), nameof(BattleHistory.OnRoundEndedHandler))]
    internal static class NarrateRoundEnd
    {
        static void Postfix(BattleHistory __instance, RoundInfo __0) => Narrator.Guard("round end", () =>
        {
            if (!Narrator.Real(__instance, out var gc) || __0 == null) return;
            var winner = __0.WinnerId;
            string title = Narrator.Mine(winner) ? GameText.Get("gwent_message_round_won_main", "Round won.")
                : winner == Battle.Them(gc) ? GameText.Get("gwent_message_round_lost_main", "Round lost.")
                : GameText.Get("gwent_message_round_draw_main", "Round drawn.");
            Narrator.Say(Narrator.Sentence(title) + " " + Narrator.Scores() + ".");
        });
    }

    // The game's end ("Победа!" / "Поражение") is not narrated here: the battle results screen
    // (Ui.BattleResultsScreen) opens right after with that banner as its name, so it would be read twice.

#if GWENT // Thronebreaker has no UIChangeTurnOrRoundPanel; its round banner is still to be found
    /// <summary>The round-start banner, read off the banner itself: "Начало финального раунда. Противник ходит
    /// первым."</summary>
    [HarmonyPatch(typeof(UIChangeTurnOrRoundPanel), nameof(UIChangeTurnOrRoundPanel.SetTexts))]
    internal static class NarrateRoundStart
    {
        static void Postfix(UIChangeTurnOrRoundPanel __instance, string __0, bool __1) => Narrator.Guard("round start", () =>
        {
            string head = "", who = "";
            try { head = UiText.Clean(__instance.m_RoundEngagement.text); } catch { }
            try { who = UiText.Clean(__instance.m_WhoStarts.text); } catch { }
            if (head.Length == 0) head = GameText.Get(__1 ? "ui_final_round" : "gwent_round_start", "Round start");
            if (who.Length == 0) who = GameText.Get(__0, __0 ?? "");
            Narrator.Deal(); // the banner shows over the deal, which is said whole after it
            Narrator.Say(head.TrimEnd('.', '!') + ". " + who);
        });
    }
#endif

    // ---- emotes ----

    /// <summary>
    /// The opponent's emote, in its speech bubble's words: "Противник: Молодец". Muted emotes stay silent —
    /// the game's "mute opponent's emotes" setting and the in-match mute both set the manager's mute flag.
    /// The notification says who emoted; the bubble that follows carries the text. Your own emote is read
    /// once when it was just sent from the emote wheel (<see cref="SentAt"/>), as the bubble over your avatar
    /// confirms it went: "You: Well played."
    /// </summary>
    [HarmonyPatch(typeof(TauntsManager), nameof(TauntsManager.OnTauntNotification))]
    internal static class NarrateTauntWho
    {
        internal static float At = -1;
        internal static bool Opponent, Muted;

        /// <summary>When the emote wheel's screen sent an emote (Gwent's EmoteScreen); -1 when none is awaited.</summary>
        internal static float SentAt = -1;

        static void Prefix(TauntsManager __instance, TauntNotificationData __0) => Narrator.Guard("taunt", () =>
        {
            if (__0 == null) return;
            At = Time.realtimeSinceStartup;
            Opponent = !Narrator.Mine(__0.TauntingPlayerId);
            Muted = false;
            try { Muted = Opponent && __instance.IsOpponentMuted; } catch { }
        });
    }

    [HarmonyPatch(typeof(BattleDialogView), nameof(BattleDialogView.Show))]
    internal static class NarrateTauntText
    {
        static void Postfix(string __0) => Narrator.Guard("taunt text", () =>
        {
            bool recent = NarrateTauntWho.At > 0 && Time.realtimeSinceStartup - NarrateTauntWho.At < 5f;
            // The mute silences only the opponent's emotes; yours still show their bubble.
            if (!recent || NarrateTauntWho.Opponent && NarrateTauntWho.Muted) return;
            // Your own emote only when just sent from the wheel, once: its bubble says it went.
            bool sent = NarrateTauntWho.SentAt > 0 && Time.realtimeSinceStartup - NarrateTauntWho.SentAt < 5f;
            if (!NarrateTauntWho.Opponent && !sent) return;
            NarrateTauntWho.At = -1;
            if (!NarrateTauntWho.Opponent) NarrateTauntWho.SentAt = -1;
            string text = UiText.Clean(__0);
            if (text.Length > 0) Narrator.Say((NarrateTauntWho.Opponent ? Narrator.Opponent() : "You") + ": " + text);
        });
    }
}
