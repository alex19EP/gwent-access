using System;
using GwentVisuals;
using HarmonyLib;

namespace GwentAccess
{
    /// <summary>
    /// Speaks tutorial hints: the floating tips a new player gets in the reward book ("Welcome to the reward
    /// book! First choose a faction tree."), the deck selector and keg opening. They are tooltips, not panels —
    /// they take no input and the player keeps navigating underneath — so each is queued once when the game sets
    /// it, in the game's own words, and kept for the repeat key.
    /// </summary>
    [HarmonyPatch(typeof(UITutorialHint), "SetHint")]
    internal static class TutorialHintPatch
    {
        private static string _lastKey;
        private static DateTime _lastAt;

        static void Postfix(UITutorialHint __instance, string locKey)
        {
            try
            {
                // The game sets the same hint again as panels refresh; a hint is news once.
                if (locKey == _lastKey && (DateTime.UtcNow - _lastAt).TotalSeconds < 30) return;
                _lastKey = locKey;
                _lastAt = DateTime.UtcNow;

                string text = UiText.Clean(__instance.HintText?.Text?.text);
                if (text.Length == 0) text = GameText.Get(locKey, "");
                if (text.Length == 0) return;
                Plugin.LogInfo("Tutorial hint " + locKey + ": " + text);
                Speech.Say(text, false);
                Speech.Remember(text);
            }
            catch (Exception e) { Plugin.LogInfo("Tutorial hint patch error: " + e); }
        }
    }
}
