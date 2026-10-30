using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using GwentGameplay;
using GwentVisuals;
using GwentUnity;

namespace GwentAccess
{
    /// <summary>Reads the battle board: what you select, the score, rows, hand, and what the opponent does.</summary>
    internal static class Battle
    {
        private static BattleHandler _handler;

        public static GameController Controller()
        {
            try
            {
                if (_handler == null || _handler.Pointer == IntPtr.Zero || !_handler.gameObject.activeInHierarchy)
                {
                    _handler = null;
                    var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<BattleHandler>());
                    foreach (var o in found) { _handler = o.TryCast<BattleHandler>(); if (_handler != null) break; }
                }
                var gc = _handler?.GameController;
                if (gc == null || !gc.IsSet) return null;
                return gc;
            }
            catch { return null; }
        }

        public static EPlayerId Me(GameController gc) => gc.PlayerManager.LocalUserId;
        public static EPlayerId Them(GameController gc) => Me(gc) == EPlayerId.P1 ? EPlayerId.P2 : EPlayerId.P1;

        /// <summary>Number of cards in a hand, including the opponent's face-down ones.</summary>
        public static int HandCount(GameController gc, EPlayerId who)
        {
            try { var loc = gc.BoardManager.GetLocation(who, ELocation.Hand); if (loc != null) return loc.NumCards; } catch { }
            try { return gc.BoardManager.GetCardsInLocationCount(who, ELocation.Hand, (ECardType)0xFF, (ECardTier)0xFF, (ECardToken)0, (ECardToken)0, null, false); } catch { }
            return Cards(gc, who, ELocation.Hand).Count;
        }

        public static List<Card> Cards(GameController gc, EPlayerId who, ELocation where)
        {
            var result = new List<Card>();
            try
            {
                var list = gc.BoardManager.GetCardsInLocation(who, where, (ECardType)31, (ECardTier)15, (ECardToken)0, (ECardToken)0, null, false);
                if (list != null) for (int i = 0; i < list.Count; i++) result.Add(list[i]);
            }
            catch (Exception e) { Plugin.LogInfo("Cards(" + who + "," + where + ") failed: " + e.Message); }
            return result;
        }

        public static string WhereName(ELocation loc)
        {
            switch (loc)
            {
                case ELocation.Melee: return "melee row";
                case ELocation.Ranged: return "ranged row";
                case ELocation.Hand: return "hand";
                case ELocation.Deck: return "deck";
                case ELocation.Graveyard: return "graveyard";
                case ELocation.Leader: return "leader";
                default: return loc.ToString();
            }
        }

        /// <summary>"Your melee row" / "Opponent's ranged row" etc. for a card's position.</summary>
        public static string Place(GameController gc, CardPosition pos)
        {
            string side = pos.PlayerId == Me(gc) ? "your" : "opponent's";
            return side + " " + WhereName(pos.Location);
        }

        private static string Short(Card c)
        {
            string n = "";
            try { n = UiText.Clean(Localization.LocalizationHelper.GetCardName(c)); } catch { }
            if (n.Length == 0 || n.StartsWith("[MISSING]")) n = "hidden card";
            try { if (!c.IsSpecial && !c.IsLeader) n += " " + c.Power; } catch { }
            try { if (c.Armor > 0) n += " armor " + c.Armor; } catch { }
            return n;
        }

        private static int Total(List<Card> cards)
        {
            int t = 0;
            foreach (var c in cards) { try { t += c.Power; } catch { } }
            return t;
        }

        public static string RowText(GameController gc, EPlayerId who, ELocation loc)
        {
            var cards = Cards(gc, who, loc);
            string name = (who == Me(gc) ? "Your " : "Opponent's ") + WhereName(loc);
            if (cards.Count == 0) return name + ", empty.";
            return name + ", " + cards.Count + " cards, total power " + Total(cards) + ": " + string.Join(", ", cards.Select(Short)) + ".";
        }

        // ---- Hotkey actions ----

        private static bool Need(out GameController gc)
        {
            gc = Controller();
            if (gc == null) { Speech.Say("Not in a match.", true); return false; }
            return true;
        }

        public static string StatusText(GameController gc)
        {
            var pm = gc.PlayerManager;
            var me = pm.LocalUser;
            var opp = me.OpponentPlayer;
            var parts = new List<string>();
            try { parts.Add("Round " + (gc.RoundManager.CurrentRoundIndex + 1)); } catch { }
            try { parts.Add("Score: you " + me.CurrentScore + ", opponent " + opp.CurrentScore); } catch { }
            try { parts.Add("Rounds won: you " + me.Crowns + ", opponent " + opp.Crowns); } catch { }
            try { parts.Add(pm.CurrentPlayerId == Me(gc) ? "Your turn" : "Opponent's turn"); } catch { }
            try { if (me.HasPassed) parts.Add("You have passed"); } catch { }
            try { if (opp.HasPassed) parts.Add("Opponent has passed"); } catch { }
            try
            {
                var mull = GameObject.Find("UIMulliganPrefab(Clone)");
                if (mull != null && mull.activeInHierarchy) parts.Add("Swaps left: " + me.MulligansLeft);
            }
            catch { }
            try { parts.Add("Cards in hand: you " + HandCount(gc, Me(gc)) + ", opponent " + HandCount(gc, Them(gc))); } catch { }
            return string.Join(". ", parts) + ".";
        }

        public static void SayStatus() { if (Need(out var gc)) Speech.Say(StatusText(gc), true); }

        public static void SayHand()
        {
            if (!Need(out var gc)) return;
            var cards = Cards(gc, Me(gc), ELocation.Hand);
            Speech.Say(cards.Count == 0 ? "Your hand is empty." :
                "Your hand, " + cards.Count + " cards: " + string.Join(", ", cards.Select((c, i) => (i + 1) + ": " + Short(c))) + ".", true);
        }

        public static void SayRow(bool mine, ELocation loc)
        {
            if (!Need(out var gc)) return;
            Speech.Say(RowText(gc, mine ? Me(gc) : Them(gc), loc), true);
        }

        public static void SayPiles()
        {
            if (!Need(out var gc)) return;
            Speech.Say("Your deck " + Cards(gc, Me(gc), ELocation.Deck).Count + " cards, graveyard " + Cards(gc, Me(gc), ELocation.Graveyard).Count +
                ". Opponent's deck " + Cards(gc, Them(gc), ELocation.Deck).Count + ", graveyard " + Cards(gc, Them(gc), ELocation.Graveyard).Count + ".", true);
        }

        public static void SayLeaders()
        {
            if (!Need(out var gc)) return;
            var mine = Cards(gc, Me(gc), ELocation.Leader);
            var theirs = Cards(gc, Them(gc), ELocation.Leader);
            string a = mine.Count > 0 ? CardText.Describe(mine[0]) : "none";
            string b = theirs.Count > 0 ? CardText.Describe(theirs[0]) : "none";
            Speech.Say("Your leader: " + a + " Opponent's leader: " + b, true);
        }
    }

    /// <summary>Speak whatever the local player selects on the battle board (hand cards, board cards, rows).</summary>
    [HarmonyPatch(typeof(BattleSelectionHandlerComponent), "Select")]
    internal static class BattleSelectPatch
    {
        private static IntPtr _last = IntPtr.Zero;
        private static IntPtr _suppress = IntPtr.Zero;
        /// <summary>HandNav announces this one itself.</summary>
        public static void Suppress(CardBattleView v) { _suppress = v != null ? v.Pointer : IntPtr.Zero; }

        static void Postfix(BattleSelectionHandlerComponent __instance, ISelectable selectable, EPlayerId playerId)
        {
            try
            {
                HandNav.Selection = __instance;
                if (Ui.GraphNavigator.Owns) return; // the battle screen speaks its own cursor
                if (selectable == null) return;
                if (playerId != __instance.LocalUserId) return;
                if (selectable.Pointer == _last) return;
                _last = selectable.Pointer;
                if (selectable.Pointer == _suppress) { _suppress = IntPtr.Zero; return; }
                var gc = Battle.Controller();
                if (Time.realtimeSinceStartup < PlayTracker.QuietUntil) return;

                var cbv = selectable.TryCast<CardBattleView>();
                if (cbv != null && cbv.Card != null)
                {
                    var card = cbv.Card;
                    if (PlayTracker.IsHeld(card.Id) && card.Position.Location != ELocation.Hand) return;
                    string where = "";
                    try { if (gc != null) where = ", " + Battle.Place(gc, card.Position); } catch { }
                    string text = CardText.Describe(card) + where;
                    Plugin.LogInfo("Battle select card: " + text);
                    Speech.Say(text, true);
                    return;
                }
                var row = selectable.TryCast<ListLocationView>();
                if (row != null && gc != null)
                {
                    string text = Battle.RowText(gc, row.PlayerId, row.LocationId);
                    Plugin.LogInfo("Battle select row: " + text);
                    Speech.Say(text, true);
                    return;
                }
                Plugin.LogInfo("Battle select other: " + selectable.Cast<Il2CppSystem.Object>().GetIl2CppType().Name);
            }
            catch (Exception e) { Plugin.LogInfo("BattleSelectPatch error: " + e); }
        }
    }

    /// <summary>Polls the match a few times a second for the legacy mulligan and play tracking. Turns, passes,
    /// rounds and plays are spoken by the <see cref="Narrator"/>, from the game's own events.</summary>
    internal static class BattleWatcher
    {
        private static float _next;

        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 0.4f;
            try
            {
                var gc = Battle.Controller();
                if (gc == null) return;
                HandNav.CheckMulligan(gc);
                PlayTracker.Tick(gc);
            }
            catch (Exception e) { Plugin.LogInfo("BattleWatcher error: " + e.Message); }
        }
    }
}
