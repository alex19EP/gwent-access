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
