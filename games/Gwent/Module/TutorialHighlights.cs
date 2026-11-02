using System;
using GwentVisuals;
using GwentVisuals.Flowchart;
using HarmonyLib;

namespace GwentAccess
{
    /// <summary>
    /// The tutorial battles point at what they want by lighting it up: a spotlight on the leader, a row's
    /// glyph, the deck, the coin, the cards with abilities. A sighted player sees the light; this says it, queued
    /// after the tutorial's own line, with the mod's key that goes there in keyboard mode ("Highlighted: your
    /// leader, V."). Each command is hooked where it starts (its OnEnter, once per command), not per frame.
    /// </summary>
    internal static class TutorialHighlights
    {
        private static string _last;
        private static DateTime _lastAt;

        /// <summary>Queue "Highlighted: …" once (the flowchart re-lights the same thing as it steps).</summary>
        internal static void Say(string what, string key)
        {
            if (string.IsNullOrEmpty(what)) return;
            string line = "Highlighted: " + what + (key != null && !ControllerMode.Force ? ", " + key : "") + ".";
            if (line == _last && (DateTime.UtcNow - _lastAt).TotalSeconds < 5) return;
            _last = line;
            _lastAt = DateTime.UtcNow;
            Plugin.LogInfo("[highlight] " + line);
            Speech.Say(line, false);
            Speech.Remember(line);
        }

        // The side a command names: "Bottom"/"Same" is the player's, "Top"/"Other" the opponent's.
        internal static bool Mine(object side)
        {
            string s = side?.ToString() ?? "";
            return s.IndexOf("Bottom", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("Same", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Any", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void Spotlight(EHighlightType type, bool mine)
        {
            switch (type)
            {
                case EHighlightType.Spotlight_Leader: Say(mine ? "your leader" : "the opponent's leader", mine ? "V" : "F"); break;
                case EHighlightType.Spotlight_MeleeGlyph: Say(mine ? "your melee row" : "the opponent's melee row", mine ? "B" : "G"); break;
                case EHighlightType.Spotlight_RangedGlyph: Say(mine ? "your ranged row" : "the opponent's ranged row", mine ? "Shift B" : "Shift G"); break;
                case EHighlightType.Spotlight_Deck: Say(mine ? "your deck" : "the opponent's deck", mine ? "D" : null); break;
                case EHighlightType.Spotlight_Graveyard: Say(mine ? "your graveyard" : "the opponent's graveyard", mine ? "Y" : "Shift Y"); break;
                case EHighlightType.Interactible_AbilityIcon:
                case EHighlightType.Interactible_AbilityIcon_Scale: Say("the order ability", "Enter on the card to use it"); break;
                case EHighlightType.Interactible_Coin: Say("the pass coin", "hold E"); break;
                case EHighlightType.CardViewHighlight_AvailableTargets: Say("the valid targets", "Tab cycles them"); break;
                case EHighlightType.CardViewHighlight_Hand: Say("your hand", "C"); break;
                case EHighlightType.CardViewHighlight_PlayStack: Say("the card being played", null); break;
            }
        }
    }

    [HarmonyPatch(typeof(ToggleSpotlightCommand), nameof(ToggleSpotlightCommand.OnEnter))]
    internal static class SpotlightPatch
    {
        static void Postfix(ToggleSpotlightCommand __instance)
        {
            try { if (__instance.On) TutorialHighlights.Spotlight(__instance.Spotlight, TutorialHighlights.Mine(__instance.Side)); }
            catch (Exception e) { Plugin.LogInfo("[highlight] spotlight failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SetLeaderHighlightCommand), nameof(SetLeaderHighlightCommand.OnEnter))]
    internal static class LeaderHighlightPatch
    {
        static void Postfix(SetLeaderHighlightCommand __instance)
        {
            try
            {
                if (!__instance.On) return;
                var gc = Battle.Controller();
                bool mine = gc == null || __instance.Player == Battle.Me(gc);
                TutorialHighlights.Say(mine ? "your leader" : "the opponent's leader", mine ? "V" : "F");
            }
            catch (Exception e) { Plugin.LogInfo("[highlight] leader failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SetLocationTokenHighlightCommand), nameof(SetLocationTokenHighlightCommand.OnEnter))]
    internal static class RowHighlightPatch
    {
        static void Postfix(SetLocationTokenHighlightCommand __instance)
        {
            try
            {
                if (!__instance.On) return;
                var gc = Battle.Controller();
                bool mine = gc == null || __instance.Player == Battle.Me(gc);
                string who = mine ? "your " : "the opponent's ";
                if (__instance.Melee && __instance.Ranged) TutorialHighlights.Say(who + "melee and ranged rows", mine ? "B and Shift B" : "G and Shift G");
                else if (__instance.Melee) TutorialHighlights.Say(who + "melee row", mine ? "B" : "G");
                else if (__instance.Ranged) TutorialHighlights.Say(who + "ranged row", mine ? "Shift B" : "Shift G");
            }
            catch (Exception e) { Plugin.LogInfo("[highlight] row failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CardAbilityIndicatorSpotlightCommand), nameof(CardAbilityIndicatorSpotlightCommand.OnEnter))]
    internal static class AbilityIndicatorPatch
    {
        static void Postfix(CardAbilityIndicatorSpotlightCommand __instance)
        {
            try
            {
                bool mine = TutorialHighlights.Mine(__instance.PlayerSide);
                TutorialHighlights.Say(mine ? "the abilities on your cards" : "the abilities on the opponent's cards", "I on a card explains them");
            }
            catch (Exception e) { Plugin.LogInfo("[highlight] ability indicator failed: " + e.Message); }
        }
    }
}
