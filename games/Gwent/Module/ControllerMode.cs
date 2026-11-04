using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using CrimsonUI;
using RedInput;

namespace GwentAccess
{
    /// <summary>
    /// Keeps Gwent in its console/controller interface even while using the keyboard.
    /// In controller mode every screen is navigable by selection and shows key hints;
    /// mouse-and-keyboard mode is mouse-centric and many things can't be reached with arrows.
    /// The mode follows the hardware: on when Rewired sees a game controller, off (the game's mouse-and-keyboard
    /// interface) when it doesn't. Plugging one in or pulling it out switches and says so: the game's own
    /// connect/disconnect handlers (<see cref="GamepadHandler"/>, which the game subscribes to Rewired's events)
    /// carry a postfix that re-checks (<see cref="ControllerEventsPatch"/>), so nothing is polled. Should that
    /// patch be missing (a game update renamed the handlers), the hardware is re-checked once a second instead.
    /// F5 still toggles by hand; that choice holds until the next connect or disconnect.
    /// </summary>
    internal static class ControllerMode
    {
        public static bool Force = ControllerConnected() ?? true;
        public static CrimsonInputModeManager Manager;

        // Whether a controller was connected at the last check; null until the first check of this module.
        private static bool? _lastConnected;
        private static float _nextCheck;

        // Whether the game's connect/disconnect handlers carry this module's postfix; null until the first Tick.
        private static bool? _hooked;

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
            Speech.Say(Force ? Loc.T("Controller mode on.") : Loc.T("Controller mode off. Mouse and keyboard interface."), true);
            Plugin.LogInfo("Controller mode forced: " + Force);
        }

        /// <summary>Per frame: the first check after load (or after <see cref="Resume"/>), once a second until
        /// Rewired is up; then nothing while the game's connect/disconnect handlers are patched, else a check once
        /// a second.</summary>
        public static void Tick()
        {
            if (_hooked == null)
            {
                _hooked = ControllerEventsPatch.Applied();
                Plugin.LogInfo(_hooked.Value
                    ? "Controller detection: the game's connect/disconnect events."
                    : "Controller detection: checking once a second (the connect/disconnect patch is not applied).");
            }
            if (_hooked == true && _lastConnected != null) return;
            if (UnityEngine.Time.realtimeSinceStartup < _nextCheck) return;
            _nextCheck = UnityEngine.Time.realtimeSinceStartup + 1f;
            Check();
        }

        /// <summary>Follow a controller being connected or disconnected. The first check after load sets the mode
        /// silently (the game starts in it); later changes are announced.</summary>
        public static void Check()
        {
            var connected = ControllerConnected();
            if (connected == null || connected == _lastConnected) return;
            bool first = _lastConnected == null;
            _lastConnected = connected;
            if (Force == connected.Value && first) return;
            Force = connected.Value;
            Apply();
            Plugin.LogInfo("Controller " + (connected.Value ? "connected" : "not connected") + "; controller mode forced: " + Force);
            if (!first) Speech.Say(Force ? Loc.T("Controller connected. Controller mode on.") : Loc.T("Controller disconnected. Mouse and keyboard interface."), true);
        }

        /// <summary>The mod switched off: stop forcing the controller interface; the game picks its own mode.</summary>
        public static void Suspend()
        {
            Force = false;
            Apply();
        }

        /// <summary>The mod switched on: follow the hardware again from the next check.</summary>
        public static void Resume()
        {
            _lastConnected = null;
            _nextCheck = 0;
            Force = ControllerConnected() ?? false;
            Apply();
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

    /// <summary>
    /// The game's own handlers for Rewired's controller connected / disconnected events: after each, the mode
    /// follows the hardware (<see cref="ControllerMode.Check"/>). Patched rather than subscribed to: a managed
    /// delegate handed to Rewired's IL2CPP events would go through the delegate conversion that crashes Gwent.
    /// </summary>
    [HarmonyPatch]
    internal static class ControllerEventsPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GamepadHandler), "HandleControllerConnectedEvent");
            yield return AccessTools.Method(typeof(GamepadHandler), "HandleControllerDisconnectedEvent");
        }

        static void Postfix()
        {
            // F8 switched the mod off: the game keeps its own mode until Resume reads the hardware again.
            if (!ModSwitch.On) return;
            try { ControllerMode.Check(); }
            catch (Exception e) { Plugin.LogInfo("Controller check failed: " + e.Message); }
        }

        /// <summary>Whether both handlers carry this generation's postfix.</summary>
        public static bool Applied()
        {
            try
            {
                foreach (var m in TargetMethods())
                {
                    var info = m == null ? null : Harmony.GetPatchInfo(m);
                    if (info == null || !info.Postfixes.Any(p => p.PatchMethod.DeclaringType == typeof(ControllerEventsPatch))) return false;
                }
                return true;
            }
            catch { return false; }
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
