using System;
using HarmonyLib;
using CrimsonUI;

namespace GwentAccess
{
    /// <summary>
    /// Keeps Gwent in its console/controller interface even while using the keyboard.
    /// In controller mode every screen is navigable by selection and shows key hints;
    /// mouse-and-keyboard mode is mouse-centric and many things can't be reached with arrows.
    /// The mode follows the hardware: on when Rewired sees a game controller, off (the game's mouse-and-keyboard
    /// interface) when it doesn't, re-checked each second so plugging one in or pulling it out switches and says
    /// so. F5 still toggles by hand; that choice holds until the next connect or disconnect.
    /// </summary>
    internal static class ControllerMode
    {
        public static bool Force = ControllerConnected() ?? true;
        public static CrimsonInputModeManager Manager;

        // Whether a controller was connected at the last check; null until the first check of this module.
        private static bool? _lastConnected;
        private static float _nextCheck;

        /// <summary>Whether Rewired sees at least one joystick (any game controller); null before Rewired is up.</summary>
        public static bool? ControllerConnected()
        {
            try
            {
                if (!Rewired.ReInput.isReady) return null;
                return Rewired.ReInput.controllers.joystickCount > 0;
            }
            catch { return null; }
        }

        public static void Toggle()
        {
            Force = !Force;
            Apply();
            Speech.Say(Force ? "Controller mode on." : "Controller mode off. Mouse and keyboard interface.", true);
            Plugin.LogInfo("Controller mode forced: " + Force);
        }

        /// <summary>Per frame: once a second, follow a controller being connected or disconnected. The first check
        /// after load sets the mode silently (the game starts in it); later changes are announced.</summary>
        public static void Tick()
        {
            if (UnityEngine.Time.realtimeSinceStartup < _nextCheck) return;
            _nextCheck = UnityEngine.Time.realtimeSinceStartup + 1f;
            var connected = ControllerConnected();
            if (connected == null || connected == _lastConnected) return;
            bool first = _lastConnected == null;
            _lastConnected = connected;
            if (Force == connected.Value && first) return;
            Force = connected.Value;
            Apply();
            Plugin.LogInfo("Controller " + (connected.Value ? "connected" : "not connected") + "; controller mode forced: " + Force);
            if (!first) Speech.Say(Force ? "Controller connected. Controller mode on." : "Controller disconnected. Mouse and keyboard interface.", true);
        }

        // Push the mode into the game's input mode manager.
        private static void Apply()
        {
            try
            {
                // A freshly hot-loaded module hasn't seen the manager through the patches yet.
                Manager ??= CrimsonInputManager.Instance?.InputMode;
                if (Manager != null)
                    Manager.DeviceMode = Force ? CrimsonInputModeManager.EDeviceMode.Gamepad : CrimsonInputModeManager.EDeviceMode.MouseAndKeyboard;
            }
            catch (Exception e) { Plugin.LogInfo("Set device mode failed: " + e); }
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
