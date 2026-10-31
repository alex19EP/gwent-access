using System;
using HarmonyLib;
using CrimsonUI;

namespace GwentAccess
{
    /// <summary>
    /// Keeps Gwent in its console/controller interface even while using the keyboard.
    /// In controller mode every screen is navigable by selection and shows key hints;
    /// mouse-and-keyboard mode is mouse-centric and many things can't be reached with arrows.
    /// F5 toggles this.
    /// </summary>
    internal static class ControllerMode
    {
        public static bool Force = true;
        public static CrimsonInputModeManager Manager;

        public static void Toggle()
        {
            Force = !Force;
            try
            {
                // A freshly hot-loaded module hasn't seen the manager through the patches yet.
                Manager ??= CrimsonInputManager.Instance?.InputMode;
                if (Manager != null)
                    Manager.DeviceMode = Force ? CrimsonInputModeManager.EDeviceMode.Gamepad : CrimsonInputModeManager.EDeviceMode.MouseAndKeyboard;
            }
            catch (Exception e) { Plugin.LogInfo("Toggle device mode failed: " + e); }
            Speech.Say(Force ? "Controller mode on." : "Controller mode off. Mouse and keyboard interface.", true);
            Plugin.LogInfo("Controller mode forced: " + Force);
        }
    }

    [HarmonyPatch(typeof(CrimsonInputModeManager), "set_DeviceMode")]
    internal static class SetDeviceModePatch
    {
        static void Prefix(CrimsonInputModeManager __instance, ref CrimsonInputModeManager.EDeviceMode value)
        {
            ControllerMode.Manager = __instance;
            if (ControllerMode.Force && value == CrimsonInputModeManager.EDeviceMode.MouseAndKeyboard)
            {
                Plugin.LogInfo("Device mode change to MouseAndKeyboard blocked (controller mode forced).");
                value = CrimsonInputModeManager.EDeviceMode.Gamepad;
            }
        }
    }

    [HarmonyPatch(typeof(CrimsonInputModeManager), "get_DeviceMode")]
    internal static class GetDeviceModePatch
    {
        static void Postfix(CrimsonInputModeManager __instance, ref CrimsonInputModeManager.EDeviceMode __result)
        {
            ControllerMode.Manager = __instance;
            if (ControllerMode.Force && __result == CrimsonInputModeManager.EDeviceMode.MouseAndKeyboard)
                __result = CrimsonInputModeManager.EDeviceMode.Gamepad;
        }
    }

    [HarmonyPatch(typeof(CrimsonInputModeManager), "Initialize")]
    internal static class InputModeInitPatch
    {
        static void Postfix(CrimsonInputModeManager __instance)
        {
            ControllerMode.Manager = __instance;
            Plugin.LogInfo("Input mode manager initialized; current mode " + __instance.DeviceMode);
            if (ControllerMode.Force)
            {
                try { __instance.DeviceMode = CrimsonInputModeManager.EDeviceMode.Gamepad; } catch (Exception e) { Plugin.LogInfo("Force gamepad at init failed: " + e); }
            }
        }
    }
}
