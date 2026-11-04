using System;
using System.Collections.Generic;
using GwentGameplay;
using GwentUnity.Audio;
using GwentVisuals;
using GwentVisuals.Effects;
using HarmonyLib;
using UnityEngine;

namespace GwentAccess
{
    // The board's counters and markers changing, beside the effect lines in Narrator.cs: the Syndicate's coins,
    // an order's charges and readiness, Zeal, an Ambush turning over. Each is something a sighted player sees
    // drawn (the coin counter on each leader, the order icon and its number on a card, the card turning over),
    // and each line is worded as the effect lines are: side first, the game's keyword, the count after.

    /// <summary>
    /// Coins gained and spent, for both players: both coin counters sit on the leaders, and the number ticks up
    /// or down as a sighted player watches. "You gain 3 coins: 7." "Opponent spends 4 coins: 2." Paying a Fee or
    /// a Tribute spends; anything else that takes coins (the round's halving, an opponent's effect) loses them.
    /// The total is the one the counter draws after the change. The widget is on each leader (and the game may
    /// bind more than one to the same wallet), so one change heard twice in a frame is said once.
    /// </summary>
    [HarmonyPatch(typeof(UICardMoneyWidget), nameof(UICardMoneyWidget.HandleOnMoneyChanged))]
    internal static class NarrateCoins
    {
        private static int _frame = -1;
        private static string _last;

        static void Postfix(EPlayerId __0, MoneyChangedTriggerInfo __1) => Narrator.Guard("coins", () =>
        {
            var info = __1;
            if (info == null) return;
            // Value reads as the new total beside OldValue; should it be the change instead, the wallet (already
            // holding old + change) gives it away.
            int old = info.OldValue, now = info.Value;
            try
            {
                var me = Narrator.Players().LocalUser;
                int wallet = (Narrator.Mine(__0) ? me : me.OpponentPlayer).Wallet.Money;
                if (wallet == old + info.Value && wallet != info.Value) now = wallet;
            }
            catch { }
            int delta = now - old;
            if (delta == 0) return;
            string key = __0 + ":" + old + ":" + now;
            if (Time.frameCount == _frame && key == _last) return;
            _frame = Time.frameCount;
            _last = key;

            bool mine = Narrator.Mine(__0);
            var reason = info.Reason;
            bool paid = (reason & (EMoneyChangeReason.Fee | EMoneyChangeReason.Tribute)) != 0;
            string verb = delta > 0 ? (mine ? "gain" : "gains")
                : paid ? (mine ? "spend" : "spends")
                : (mine ? "lose" : "loses");
            int n = Math.Abs(delta);
            Narrator.Say((mine ? "You" : Narrator.Opponent()) + " " + verb + " " + n + (n == 1 ? " coin" : " coins") + ": " + now + ".");
        });
    }

    /// <summary>
    /// Charges given to an order by an effect: "Sigrdrifa's Rite: charge +1 to your Brokvar Archer, 2
    /// charges." / "Your Brokvar Archer, charge +1: 2 charges." The card's own number goes up where the order
    /// icon is drawn. Charges taken by using the order are the order's own line, not repeated here.
    /// </summary>
    [HarmonyPatch(typeof(AbilityVFXHandler), nameof(AbilityVFXHandler.OnCardTapChargesAttack))]
    internal static class NarrateCharges
    {
        static void Postfix(CardTapChargesAttack __0) => Narrator.Guard("charges", () =>
        {
            var a = __0;
            if (a == null || a.Operation == ETapChargesOperation.Remove) return;
            string label = GameText.Term("keyword_charge", "Charge") + (a.Operation == ETapChargesOperation.Add ? " +" : " ") + a.Value;
            var cards = new List<Card>();
            foreach (var card in Narrator.List(a.Targets))
                if (Narrator.Visible(card)) cards.Add(card);
            if (cards.Count == 0) return;
            string source = Narrator.Source(a.Attacker);
            if (cards.Count == 1 && (source == null || Narrator.Same(a.Attacker, cards[0])))
            {
                Narrator.SaySelf(Narrator.Owned(cards[0]), label, Charges(cards[0]));
                return;
            }
            var targets = cards.ConvertAll(c =>
            {
                string n = Charges(c);
                return n.Length > 0 ? Narrator.Owned(c) + ", " + n : Narrator.Owned(c);
            });
            Narrator.SayEffect(source, label, a.Operation == ETapChargesOperation.Add ? "to" : "on", targets);
        });

        /// <summary>"2 charges": the order's charges now, as its icon numbers them.</summary>
        internal static string Charges(Card card)
        {
            try
            {
                int n = card.Data.TapCharges.Value;
                return n == 1 ? "1 charge" : n + " charges";
            }
            catch { return ""; }
        }
    }

    /// <summary>
    /// Zeal given: the order can be used on the turn the card arrived, so its "next turn" marker clears. The
    /// game lifts the card's summoning sickness for it. "Bran an Tuirseach: zeal on your Brokvar Archer." /
    /// "Your Brokvar Archer, zeal." Sickness put back on is not said: nothing a player would act on shows.
    /// </summary>
    [HarmonyPatch(typeof(AbilityVFXHandler), nameof(AbilityVFXHandler.OnCardTapSummoningSicknessAttack))]
    internal static class NarrateZeal
    {
        static void Postfix(TapCardSummoningSicknessAttack __0) => Narrator.Guard("zeal", () =>
        {
            var a = __0;
            if (a == null || a.Enable) return;
            var cards = new List<Card>();
            foreach (var card in Narrator.List(a.Targets))
                if (Narrator.Visible(card)) cards.Add(card);
            Narrator.SayOnCards(Narrator.Source(a.Attacker), a.Attacker, GameText.Term("keyword_zeal", "Zeal"), "on", cards, power: false);
        });
    }

    /// <summary>
    /// An order off its cooldown: the hourglass on the card goes and the order icon lights up again, usually as
    /// a turn starts. Cards that come off cooldown together are one line: "Order ready: your Brokvar Hunter,
    /// opponent's Ard Feainn Tortoise." A unit's order becoming usable the turn after it arrived is not said:
    /// that happens to every unit, every turn.
    ///
    /// Heard through the card's sound handler, which the game binds to the real cards on screen only (not the
    /// AI's imagined games). Its event carries one state (the old one, as the game's other handlers name it);
    /// the last state seen per card covers it carrying the new one instead.
    /// </summary>
    [HarmonyPatch(typeof(CardViewSfxHandler), nameof(CardViewSfxHandler.OnTapStateChanged))]
    internal static class NarrateOrderReady
    {
        private static readonly Dictionary<IntPtr, ETapCardState> _last = new Dictionary<IntPtr, ETapCardState>();

        static void Postfix(Card __0, ETapCardState __1) => Narrator.Guard("order ready", () =>
        {
            var card = __0;
            if (card == null) return;
            var now = card.Data.TapCharges.State;
            var before = __1;
            if (before == now && _last.TryGetValue(card.Pointer, out var seen)) before = seen;
            if (_last.Count > 200) _last.Clear(); // one match's cards; cleared rather than tracked per match
            _last[card.Pointer] = now;
            if (now != ETapCardState.Available || before != ETapCardState.Recharging) return;
            var at = card.Position.Location;
            if (at != ELocation.Melee && at != ELocation.Ranged && at != ELocation.Leader) return;
            Narrator.Gather(GameText.Term("keyword_order", "Order") + " ready", Narrator.Owned(card));
        });
    }

    /// <summary>
    /// An Ambush turning face up on the board: "Ambush: opponent's Sweers, power 4." The card's Ambush status
    /// comes off as it flips; if its face is not shown yet at that moment, the line waits for it
    /// (<see cref="Narrator.WhenSeen"/>). The card has a view sound handler per view, so a flip heard twice
    /// is said once.
    /// </summary>
    [HarmonyPatch(typeof(CardViewSfxHandler), nameof(CardViewSfxHandler.OnCardStatusChanged))]
    internal static class NarrateAmbushFlip
    {
        private static IntPtr _lastCard;
        private static float _lastAt = -1;

        static void Postfix(Card __0, ECardToken __1) => Narrator.Guard("ambush", () =>
        {
            var card = __0;
            if (card == null || (__1 & ECardToken.Ambush) == 0) return;
            if (card.Data.Statuses.Contains(ECardToken.Ambush)) return;
            var at = card.Position.Location;
            if (at != ELocation.Melee && at != ELocation.Ranged) return;
            if (card.Pointer == _lastCard && Time.realtimeSinceStartup - _lastAt < 3f) return;
            _lastCard = card.Pointer;
            _lastAt = Time.realtimeSinceStartup;
            Narrator.WhenSeen(card, Say);
        });

        private static void Say(Card card) =>
            Narrator.SayEffect(null, GameText.Term("keyword_ambush", "Ambush"), null, new List<string> { Narrator.Target(card) });
    }
}
