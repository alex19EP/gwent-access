using System;
using HarmonyLib;
using CrimsonUI;

namespace GwentAccess
{
    /// <summary>Reads popups (confirmations like "Quit game?") with their choices and keys.</summary>
    [HarmonyPatch(typeof(PopupControl), "Configure")]
    internal static class PopupConfigurePatch
    {
        static void Postfix(PopupControl __instance)
        {
            try
            {
                string title = "", message = "";
                try { title = UiText.Clean(__instance.m_TitleLabel?.Text?.text); } catch { }
                try { message = UiText.Clean(__instance.m_MessageLabel?.Text?.text); } catch { }
                var hints = KeyHints.HintLines(__instance);
                for (int i = 0; i < hints.Count; i++) hints[i] = hints[i].Replace(": ESC", ": Escape").Replace(": Return", ": Enter");
                string text = "Popup. " + (title.Length > 0 ? title + ". " : "") + (message.Length > 0 ? message + ". " : "") + string.Join(". ", hints);
                if (title.StartsWith("Forfeit", StringComparison.OrdinalIgnoreCase))
                    text = "Warning! Forfeit question. Enter would give up the match. Press Escape to keep playing.";
                else if ((title + " " + message).IndexOf("placement not ideal", StringComparison.OrdinalIgnoreCase) >= 0)
                    text = "Placement warning: this card's ability won't work on that row. Enter places it anyway. Escape once cancels, and you keep holding the card.";
                Plugin.LogInfo("Popup: " + UiText.DebugPath(__instance) + " => " + text);
                // A popup in the popup panel's container is the graph popup screen's (Ui.PopupScreen): it reads the
                // popup once it takes the input layer, a frame later, so speaking here too would read it twice.
                if (__instance.GetComponentInParent<UIPopupContainer>() != null) return;
                Speech.Say(text, true);
                Speech.Remember(text);
                Screens.LastAnnounce = DateTime.UtcNow;
            }
            catch (Exception e) { Plugin.LogInfo("Popup patch error: " + e); }
        }
    }
}
