using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using GwentGameplay;
using GwentVisuals;
using GwentUnity;

namespace GwentAccess
{
    /// <summary>
    /// Ctrl+Left/Right: move the game's battle cursor straight to a card in your hand.
    /// Also announces when the game wants you to pick a row or a target.
    /// </summary>
    internal static class HandNav
    {
        public static BattleSelectionHandlerComponent Selection;
        private static int _index = -1;
        public static Card CurrentCard;

        // Pending mulligan result check
        private static float _mulliganCheckAt = -1;
        private static HashSet<ushort> _handBefore;
        private static string _swappedName;

        private static float _passArmedUntil = -1;
        private static int _oppIndex = -1;

        /// <summary>Ctrl+E: end your turn (needed after playing a card, since Gwent waits in case you want to use an Order).</summary>
        public static void EndTurn()
        {
            var gc = Battle.Controller();
            if (gc == null) { Speech.Say("Not in a match.", true); return; }
            var pm = gc.PlayerManager;
            if (pm.CurrentPlayerId != Battle.Me(gc)) { Speech.Say("It's not your turn.", true); return; }
            var mode = SelectionModePatch.Current;
            if (mode == EBattleNavigationMode.SelectCards) { Speech.Say("The game is waiting for you to choose a target first. Arrows to move, Enter to choose.", true); return; }
            if (mode == EBattleNavigationMode.SelectLocations || mode == EBattleNavigationMode.PlayCard) { Speech.Say("You're holding a card. Choose a row and press Enter first.", true); return; }
            try { pm.LocalUser.AskEndTurn(); PlayTracker.EndAsked(); Plugin.LogInfo("End turn requested."); Speech.Say("Ending turn.", true); }
            catch (Exception e) { Plugin.LogInfo("End turn failed: " + e); Speech.Say("Couldn't end the turn.", true); }
        }

        /// <summary>Ctrl+Up/Down: step through every card on the opponent's side of the board.</summary>
        public static void OpponentCard(int dir)
        {
            var gc = Battle.Controller();
            if (gc == null) { Speech.Say("Not in a match.", true); return; }
            var them = Battle.Them(gc);
            var cards = new List<Card>();
            cards.AddRange(Battle.Cards(gc, them, ELocation.Melee));
            cards.AddRange(Battle.Cards(gc, them, ELocation.Ranged));
            if (cards.Count == 0) { Speech.Say("The opponent has no cards on the board.", true); _oppIndex = -1; return; }
            _oppIndex = (_oppIndex < 0 || _oppIndex >= cards.Count) ? (dir > 0 ? 0 : cards.Count - 1) : (_oppIndex + dir + cards.Count) % cards.Count;
            var c = cards[_oppIndex];
            string row = "";
            try { row = Battle.WhereName(c.Position.Location); } catch { }
            Speech.Say("Opponent " + row + ", " + (_oppIndex + 1) + " of " + cards.Count + ": " + CardText.Describe(c), true);
        }

        /// <summary>Ctrl+P twice: pass the round. The first press says the score and asks to confirm.</summary>
        public static void Pass()
        {
            var gc = Battle.Controller();
            if (gc == null) { Speech.Say("Not in a match.", true); return; }
            var pm = gc.PlayerManager;
            var me = pm.LocalUser;
            var opp = me.OpponentPlayer;
            if (pm.CurrentPlayerId != Battle.Me(gc)) { Speech.Say("It's not your turn.", true); return; }
            if (me.HasPassed) { Speech.Say("You already passed.", true); return; }
            if (Time.realtimeSinceStartup > _passArmedUntil)
            {
                _passArmedUntil = Time.realtimeSinceStartup + 4f;
                string lead = me.CurrentScore > opp.CurrentScore ? "You're ahead" : me.CurrentScore < opp.CurrentScore ? "You're behind" : "You're tied";
                Speech.Say(lead + ", " + me.CurrentScore + " to " + opp.CurrentScore + (opp.HasPassed ? ". Opponent has passed." : ".") + " Press Control P again to pass the round.", true);
                return;
            }
            _passArmedUntil = -1;
            try { me.AskPass(); Plugin.LogInfo("Pass requested."); Speech.Say("Passing.", true); }
            catch (Exception e) { Plugin.LogInfo("Pass failed: " + e); Speech.Say("Pass failed.", true); }
        }

        /// <summary>Ctrl+M: swap (mulligan) the current hand card.</summary>
        public static void Mulligan()
        {
            var gc = Battle.Controller();
            if (gc == null) { Speech.Say("Not in a match.", true); return; }
            var me = gc.PlayerManager.LocalUser;
            bool can = false;
            try { can = me.CanMulligan; } catch { }
            int left = 0;
            try { left = me.MulligansLeft; } catch { }
            if (!can || left <= 0) { Speech.Say(left <= 0 ? "No swaps left." : "You can't swap cards right now.", true); return; }
            var card = CurrentCard;
            var hand = Battle.Cards(gc, Battle.Me(gc), ELocation.Hand);
            if (card == null || !hand.Exists(c => c.Id == card.Id))
            {
                Speech.Say("Pick a card first with Control Right or Left.", true);
                return;
            }
            _handBefore = new HashSet<ushort>(hand.ConvertAll(c => c.Id));
            _swappedName = UiText.Clean(Localization.LocalizationHelper.GetCardName(card));
            try
            {
                me.AskMulliganCard(card.Id);
                Plugin.LogInfo("Mulligan requested for " + _swappedName + " (" + card.Id + ")");
                Speech.Say("Swapping " + _swappedName + ".", true);
                _mulliganCheckAt = Time.realtimeSinceStartup + 1.5f;
                _index = -1; CurrentCard = null;
            }
            catch (Exception e) { Plugin.LogInfo("Mulligan failed: " + e); Speech.Say("Swap failed.", true); }
        }

        /// <summary>Called from the battle tick: report what the swap gave us.</summary>
        public static void CheckMulligan(GameController gc)
        {
            if (_mulliganCheckAt < 0 || Time.realtimeSinceStartup < _mulliganCheckAt) return;
            _mulliganCheckAt = -1;
            try
            {
                var hand = Battle.Cards(gc, Battle.Me(gc), ELocation.Hand);
                var fresh = hand.Find(c => !_handBefore.Contains(c.Id));
                int left = 0;
                try { left = gc.PlayerManager.LocalUser.MulligansLeft; } catch { }
                string got = fresh != null ? "Got " + CardText.Describe(fresh) : "Swap done.";
                Speech.Say("Swapped " + _swappedName + ". " + got + " " + left + (left == 1 ? " swap left." : " swaps left."), false);
            }
            catch (Exception e) { Plugin.LogInfo("Mulligan check failed: " + e.Message); }
        }

        private static BattleSelectionHandlerComponent FindSelection()
        {
            try
            {
                if (Selection != null && Selection.Pointer != IntPtr.Zero && Selection.gameObject != null) return Selection;
            }
            catch { }
            Selection = null;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<BattleSelectionHandlerComponent>()))
            {
                Selection = o.TryCast<BattleSelectionHandlerComponent>();
                if (Selection != null) break;
            }
            return Selection;
        }

        private static CardBattleView ViewFor(Card card)
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<CardBattleView>()))
            {
                var v = o.TryCast<CardBattleView>();
                try { if (v != null && v.Card != null && v.Card.Id == card.Id) return v; } catch { }
            }
            return null;
        }

        public static void Move(int dir)
        {
            var gc = Battle.Controller();
            if (gc == null) { Speech.Say("Not in a match.", true); return; }
            var hand = Battle.Cards(gc, Battle.Me(gc), ELocation.Hand);
            if (hand.Count == 0) { Speech.Say("Your hand is empty.", true); return; }
            _index = (_index < 0 || _index >= hand.Count) ? (dir > 0 ? 0 : hand.Count - 1) : (_index + dir + hand.Count) % hand.Count;
            var card = hand[_index];
            CurrentCard = card;
            var sel = FindSelection();
            var view = ViewFor(card);
            bool moved = false;
            if (sel != null && view != null)
            {
                try { moved = sel.Select(view.Cast<ISelectable>(), sel.LocalUserId, true); }
                catch (Exception e) { Plugin.LogInfo("Hand select failed: " + e.Message); }
            }
            Plugin.LogInfo("HandNav " + _index + " moved=" + moved + " view=" + (view != null));
            // Always say it ourselves (with position), even if the game didn't move its cursor.
            BattleSelectPatch.Suppress(view);
            Speech.Say((_index + 1) + " of " + hand.Count + ": " + CardText.Describe(card) + (moved ? "" : " (couldn't move the game cursor here)"), true);
        }
    }

    /// <summary>Tell the player what kind of choice the game wants.</summary>
    /// <summary>
    /// Forfeit safety: the forfeit popup opens on Escape, and Enter accepts it, so it's easy to forfeit by accident
    /// (Escape to cancel a warning, Escape again, then Enter). The first forfeit is blocked; doing it again within 15 seconds goes through.
    /// </summary>
    [HarmonyPatch(typeof(Player), "AskForfeit")]
    internal static class ForfeitGuardPatch
    {
        private static float _armedUntil = -1;
        static bool Prefix()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now < _armedUntil) { _armedUntil = -1; Plugin.LogInfo("Forfeit confirmed"); Speech.Say("Forfeiting.", true); return true; }
                _armedUntil = now + 15f;
                Plugin.LogInfo("Forfeit blocked (first attempt)");
                Speech.Say("Forfeit stopped, you're still in the match. If you really want to forfeit, choose forfeit again in the next 15 seconds.", true);
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(BattleSelectionHandlerComponent), "SetSelectionMode")]
    internal static class SelectionModePatch
    {
        public static EBattleNavigationMode Current = EBattleNavigationMode.Normal;
        static void Postfix(BattleSelectionHandlerComponent __instance, EBattleNavigationMode selectionMode)
        {
            try
            {
                HandNav.Selection = __instance;
                if (selectionMode == Current) return;
                var prev = Current;
                Current = selectionMode;
                Plugin.LogInfo("Selection mode: " + selectionMode);
                if (Ui.GraphNavigator.Owns)
                {
                    // The battle screen prompts for rows and targets itself; only the play tracking stays.
                    if (selectionMode == EBattleNavigationMode.SelectCards) PlayTracker.Cancel();
                    else if (selectionMode == EBattleNavigationMode.PlayCard) PlayTracker.Begin();
                    else if (selectionMode == EBattleNavigationMode.Normal && prev != EBattleNavigationMode.Normal) PlayTracker.Finish(prev);
                    return;
                }
                switch (selectionMode)
                {
                    case EBattleNavigationMode.SelectLocations: Speech.Say("Choose a row. Arrows to move, Enter to place.", false); break;
                    case EBattleNavigationMode.SelectCards:
                        PlayTracker.Cancel();
                        Speech.Say("The ability needs a target. Arrows to move, Enter to choose. Pick a target before your turn runs out.", true); break;
                    case EBattleNavigationMode.PlayCard: PlayTracker.Begin(); Speech.Say("Card picked up.", false); break;
                    case EBattleNavigationMode.Normal:
                        if (prev != EBattleNavigationMode.Normal) PlayTracker.Finish(prev);
                        break;
                }
            }
            catch (Exception e) { Plugin.LogInfo("SelectionModePatch error: " + e.Message); }
        }
    }

    /// <summary>
    /// Works out what actually happened after the game leaves card-play mode, then says it once:
    /// which card landed where, or that it went back to the hand. Also warns when the turn timer runs low
    /// and says "your turn again" when the opponent has passed.
    /// </summary>
    internal static class PlayTracker
    {
        private static HashSet<ushort> _handAtStart;
        private static float _checkAt = -1;
        private static bool _afterTarget;
        public static float QuietUntil = -1;
        private static float _endAskedAt = -1;
        private static int _warned;
        private static EPlayerId _lastTurn;
        private static bool _loggedTimer;

        public static bool Holding => _handAtStart != null;
        public static bool IsHeld(ushort id) => _handAtStart != null && _handAtStart.Contains(id);

        private static HashSet<ushort> HandIds(GameController gc)
        {
            var s = new HashSet<ushort>();
            foreach (var c in Battle.Cards(gc, Battle.Me(gc), ELocation.Hand)) s.Add(c.Id);
            return s;
        }

        public static void Begin()
        {
            var gc = Battle.Controller();
            if (gc == null) return;
            if (_handAtStart == null) _handAtStart = HandIds(gc);
            _checkAt = -1;
        }

        public static void Cancel() { _checkAt = -1; }

        public static void Finish(EBattleNavigationMode prev)
        {
            _afterTarget = prev == EBattleNavigationMode.SelectCards;
            _checkAt = Time.realtimeSinceStartup + 0.8f;
            QuietUntil = _checkAt + 0.3f;
        }

        private static int _endRound = -1;
        public static void EndAsked()
        {
            _endAskedAt = Time.realtimeSinceStartup;
            try { _endRound = Battle.Controller().RoundManager.CurrentRoundIndex; } catch { _endRound = -1; }
        }

        public static void Tick(GameController gc)
        {
            float now = Time.realtimeSinceStartup;
            var me = Battle.Me(gc);
            var pm = gc.PlayerManager;
            var opp = pm.LocalUser.OpponentPlayer;

            if (_checkAt > 0 && now >= _checkAt && SelectionModePatch.Current == EBattleNavigationMode.Normal)
            {
                _checkAt = -1;
                string score = " Score: you " + pm.LocalUser.CurrentScore + ", opponent " + opp.CurrentScore + ".";
                if (_handAtStart != null)
                {
                    var hand = HandIds(gc);
                    Card played = null;
                    foreach (var id in _handAtStart) if (!hand.Contains(id)) { played = gc.CardManager.GetCard(id); break; }
                    _handAtStart = null;
                    if (played != null)
                    {
                        string where = "";
                        try { where = " on " + Battle.Place(gc, played.Position); } catch { }
                        string name = "";
                        try { name = Localization.LocalizationHelper.GetCardName(played); } catch { }
                        if (string.IsNullOrEmpty(name)) name = "Card";
                        Speech.Say("Played " + name + where + "." + score + (Ui.GraphNavigator.Owns ? "" : " Control E to end your turn."), true);
                    }
                    else Speech.Say("Card not played. It's back in your hand.", true);
                }
                else if (_afterTarget) Speech.Say("Done." + score + (Ui.GraphNavigator.Owns ? "" : " Control E to end your turn."), true);
            }

            var turn = pm.CurrentPlayerId;
            if (turn != _lastTurn) { _lastTurn = turn; _warned = 0; _loggedTimer = false; _endAskedAt = -1; _handAtStart = null; }

            // Opponent passed: the turn comes straight back to you, so nothing else would announce it.
            if (_endAskedAt > 0 && now - _endAskedAt > 6f) _endAskedAt = -1;
            if (_endAskedAt > 0 && now - _endAskedAt > 2.5f && turn == me && opp.HasPassed && gc.RoundManager.CurrentRoundIndex == _endRound && GameObject.Find("UIMulliganPrefab(Clone)") == null)
            {
                _endAskedAt = -1; _warned = 0; _loggedTimer = false;
                Speech.Say("Your turn again. Opponent has passed." + " Score: you " + pm.LocalUser.CurrentScore + ", opponent " + opp.CurrentScore + ".", false);
            }

            if (turn == me)
            {
                try
                {
                    var t = gc.TimeManager.GetCurrentStateTimer();
                    if (t != null && t.IsRunning)
                    {
                        int dur = t.Duration, rem = t.TimeRemaining;
                        if (!_loggedTimer) { _loggedTimer = true; Plugin.LogInfo("Turn timer: duration=" + dur + " remaining=" + rem); }
                        float secs = dur > 1000 ? rem / 1000f : rem;
                        if (secs <= 20 && secs > 8 && _warned < 1) { _warned = 1; Speech.Say("20 seconds left in your turn.", false); }
                        else if (secs <= 8 && secs > 0 && _warned < 2) { _warned = 2; Speech.Say("Hurry, 8 seconds left!", true); }
                    }
                }
                catch (Exception e) { if (!_loggedTimer) { _loggedTimer = true; Plugin.LogInfo("Timer read failed: " + e.Message); } }
            }
        }
    }
}
