using System;
using GwentGameplay;
using GwentVisuals;
using HarmonyLib;
using UnityEngine;

namespace GwentAccess
{
    /// <summary>
    /// Your turn's timer: warns once at 20 seconds left and once at 8, as a sighted player watches the clock
    /// run down. Ticked from the module a few times a second; reads the game's own state timer.
    /// </summary>
    internal static class TurnTimer
    {
        private static float _next;
        private static int _warned;
        private static EPlayerId _lastTurn;

        /// <summary>Seconds left in your turn, or -1 when no timer runs for you (Practice has none).</summary>
        public static float SecondsLeft(GameController gc)
        {
            try
            {
                if (gc == null || gc.PlayerManager.CurrentPlayerId != Battle.Me(gc)) return -1;
                var t = gc.TimeManager.GetCurrentStateTimer();
                if (t == null || !t.IsRunning) return -1;
                int dur = t.Duration, rem = t.TimeRemaining;
                return dur > 1000 ? rem / 1000f : rem;
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
                var turn = gc.PlayerManager.CurrentPlayerId;
                if (turn != _lastTurn) { _lastTurn = turn; _warned = 0; }
                float secs = SecondsLeft(gc);
                if (secs <= 0) return;
                if (secs <= 20 && secs > 8 && _warned < 1) { _warned = 1; Speech.Say("20 seconds left in your turn.", false); }
                else if (secs <= 8 && _warned < 2) { _warned = 2; Speech.Say("Hurry, 8 seconds left!", true); }
            }
            catch (Exception e) { Plugin.LogInfo("[timer] " + e.Message); }
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
