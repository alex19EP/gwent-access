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
