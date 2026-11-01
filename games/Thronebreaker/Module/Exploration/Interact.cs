using System;
using GwentVisuals.Campaign;
using HarmonyLib;

namespace GwentAccess.Exploration
{
    /// <summary>
    /// Enter holds a hold interaction (loot, recruits, shrines, message boards). The game binds a zone's interact to
    /// the A action (Enter on the keyboard) and to the right mouse button, but feeds its hold bar
    /// (HoldButtonController) only from the button of the current device: in mouse-and-keyboard mode, the right
    /// mouse button (AInteractionZone.CampaignUpdate reads m_MouseButtonPressed; Enter sets
    /// m_GamepadButtonPressed, which only counts on a gamepad). So Enter starts nothing that has to be held.
    /// While Enter is down on a zone, the right button is held for it, and let go with it; the hold, its
    /// progress sound and the interaction itself are the game's.
    /// </summary>
    [HarmonyPatch(typeof(AInteractionZone), nameof(AInteractionZone.CampaignUpdate))]
    internal static class EnterHolds
    {
        // The zone whose right button we are holding, so a real right click is never released by us.
        private static IntPtr _holding;

        private static void Prefix(AInteractionZone __instance)
        {
            if (__instance.m_CurrentActiveDevice != CrimsonUI.CrimsonInputModeManager.EDeviceMode.MouseAndKeyboard) return;
            var ptr = __instance.Pointer;
            if (__instance.m_GamepadButtonPressed)
            {
                if (!__instance.m_MouseButtonPressed) _holding = ptr;
                __instance.m_MouseButtonPressed = true;
            }
            else if (_holding == ptr)
            {
                __instance.m_MouseButtonPressed = false;
                _holding = IntPtr.Zero;
            }
        }
    }
}
