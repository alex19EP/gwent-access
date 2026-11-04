using System;
using GwentGameplay;
using GwentVisuals;
using HarmonyLib;
using UnityEngine;

namespace GwentAccess
{
    /// <summary>
    /// The match clock: your turn's timer, the opponent's, and the mulligan's countdown, all one state timer
    /// in the game (the one running for the current game state). Warns once at 20 seconds left and once at 8,
    /// in your turn and while you are still swapping in the mulligan, as a sighted player watches the clock
    /// run down. Ticked from the module a few times a second.
    /// </summary>
    internal static class TurnTimer
    {
        private static float _next;
        private static int _warned;
        private static int _lastPhase = int.MinValue;

        // The state timer running now, and whether it is the mulligan's; null when none runs (Practice has none).
        private static StateTimer Running(GameController gc, out bool mulligan)
        {
            mulligan = false;
            var t = gc?.TimeManager?.GetCurrentStateTimer();
            if (t == null || !t.IsRunning) return null;
            mulligan = t.GameStateID == EGameStateId.Mulligan;
            return t;
        }

        private static float Seconds(StateTimer t)
        {
            int dur = t.Duration, rem = t.TimeRemaining;
            return dur > 1000 ? rem / 1000f : rem;
        }

        /// <summary>Seconds left in your turn, or -1 when no timer runs for you (Practice has none).</summary>
        public static float SecondsLeft(GameController gc)
        {
            try
            {
                if (gc == null || gc.PlayerManager.CurrentPlayerId != Battle.Me(gc)) return -1;
                var t = Running(gc, out bool mulligan);
                return t == null || mulligan ? -1 : Seconds(t);
            }
            catch { return -1; }
        }

        /// <summary>Seconds left in the opponent's turn (the clock on their side), or -1 when none runs.</summary>
        public static float OpponentSecondsLeft(GameController gc)
        {
            try
            {
                if (gc == null || gc.PlayerManager.CurrentPlayerId == Battle.Me(gc)) return -1;
                var t = Running(gc, out bool mulligan);
                return t == null || mulligan ? -1 : Seconds(t);
            }
            catch { return -1; }
        }

        /// <summary>Seconds left to swap in the mulligan, or -1 when no mulligan timer runs.</summary>
        public static float MulliganSecondsLeft(GameController gc)
        {
            try
            {
                var t = Running(gc, out bool mulligan);
                return t != null && mulligan ? Seconds(t) : -1;
            }
            catch { return -1; }
        }

        public static void Tick()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 0.4f;
            var gc = Battle.Controller();
            if (gc == null) return;
            try
            {
                float mull = MulliganSecondsLeft(gc);
                // The warnings start over with each turn and each mulligan.
                int phase = mull >= 0 ? -1 : (int)gc.PlayerManager.CurrentPlayerId;
                if (phase != _lastPhase) { _lastPhase = phase; _warned = 0; }
                if (!ModConfig.TimerWarnings) return;
                if (mull >= 0)
                {
                    bool swapping = false;
                    try { swapping = gc.PlayerManager.LocalUser.CanMulligan; } catch { }
                    if (swapping) Warn(mull, "20 seconds left to swap.", "Hurry, 8 seconds left to swap!");
                    return;
                }
                Warn(SecondsLeft(gc), "20 seconds left in your turn.", "Hurry, 8 seconds left!");
            }
            catch (Exception e) { Plugin.LogInfo("[timer] " + e.Message); }
        }

        private static void Warn(float secs, string at20, string at8)
        {
            if (secs <= 0) return;
            if (secs <= 20 && secs > 8 && _warned < 1) { _warned = 1; Speech.Say(at20, false); }
            else if (secs <= 8 && _warned < 2) { _warned = 2; Speech.Say(at8, true); }
        }
    }

    /// <summary>
    /// Forfeit safety: the forfeit popup opens on Escape and Enter accepts it, so forfeiting by accident is easy
    /// (Escape to cancel a warning, Escape again, then Enter). The first forfeit is blocked; doing it again
    /// within 15 seconds goes through. Off while accessibility is switched off (F8): a sighted helper has the game.
    /// </summary>
    [HarmonyPatch(typeof(Player), "AskForfeit")]
    internal static class ForfeitGuardPatch
    {
        private static float _armedUntil = -1;

        static bool Prefix()
        {
            if (!ModSwitch.On) return true;
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
}
