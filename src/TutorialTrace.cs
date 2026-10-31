#if DEBUG
using System;
using GwentVisuals.Flowchart;
using HarmonyLib;

namespace GwentAccess
{
    /// <summary>
    /// Dev aid (Debug builds only): logs each flowchart command as it becomes the running one, so a tutorial
    /// battle's steering (spotlights, highlights, input locks, tooltips) can be mapped from the log while a
    /// player plays through it. Logs only; speaks nothing.
    /// </summary>
    // ACommandNode.Tick_Impl inlines ExecuteCurrentCommand and calls each command's Execute through the vtable;
    // nearly every command keeps the base Execute, so that is where to look.
    [HarmonyPatch(typeof(ACommand), nameof(ACommand.Execute))]
    internal static class TutorialTracePatch
    {
        // Each command instance is logged once: waits run every frame, and parallel ones alternate.
        private static readonly System.Collections.Generic.HashSet<IntPtr> Seen = new System.Collections.Generic.HashSet<IntPtr>();

        static void Prefix(ACommand __instance)
        {
            try
            {
                var cmd = __instance;
                if (cmd == null || !Seen.Add(cmd.Pointer)) return;
                Plugin.LogInfo("[flow] " + cmd.GetIl2CppType().Name + " " + Detail(cmd));
            }
            catch (Exception e) { Plugin.LogInfo("[flow] trace error: " + e.Message); }
        }

        private static string Detail(ACommand cmd)
        {
            var leader = cmd.TryCast<SetLeaderHighlightCommand>();
            if (leader != null) return "on=" + leader.On + " player=" + leader.Player;
            var token = cmd.TryCast<SetLocationTokenHighlightCommand>();
            if (token != null) return "on=" + token.On + " player=" + token.Player + " melee=" + token.Melee + " ranged=" + token.Ranged;
            var spot = cmd.TryCast<CardAbilityIndicatorSpotlightCommand>();
            if (spot != null) return "intensity=" + spot.IntensityMode + " side=" + spot.PlayerSide;
            var light = cmd.TryCast<ToggleSpotlightCommand>();
            if (light != null) return "on=" + light.On + " spotlight=" + light.Spotlight + " side=" + light.Side;
            var arrows = cmd.TryCast<SetCardPreviewArrowsCommand>();
            if (arrows != null) return "arrows=" + arrows.ArrowsEnabled;
            var nav = cmd.TryCast<NavigateToCardCommand>();
            if (nav != null) return "side=" + nav.TargetSide + " location=" + nav.TargetLocation;
            return "";
        }
    }
}
#endif
