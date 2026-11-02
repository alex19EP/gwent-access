using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GwentAccess
{
    /// <summary>
    /// Watches the keyboard on a background thread using Windows directly.
    /// Only reacts while a window from this game process is in front.
    /// The keys that work everywhere, even while the graph navigator holds no screen: F1 help, F5 controller mode,
    /// F7 repeat (Shift+F7 older messages), F8 accessibility on/off, F9 where am I (the focused item with its
    /// context), F11 (Debug builds) load a newer module build. (F10 avoided: Windows uses it for menus and it
    /// steals focus.) Everything else is the graph screens' keys, read through Rewired (<see cref="Ui.Keys"/>).
    /// </summary>
    internal static class Hotkeys
    {
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private const int VK_F1 = 0x70, VK_SHIFT = 0x10, VK_F5 = 0x74, VK_F8 = 0x77, VK_F9 = 0x78, VK_F7 = 0x76;
        private static readonly uint MyPid = (uint)Process.GetCurrentProcess().Id;

        private static volatile bool _stop;
        private static Thread _thread;

        public static void Start()
        {
            _stop = false;
            _thread = new Thread(Loop) { IsBackground = true, Name = "GwentAccessHotkeys" };
            _thread.Start();
        }

        /// <summary>End the polling thread (module reload) and wait for it, so two generations never both read keys.</summary>
        public static void Stop()
        {
            _stop = true;
            _thread?.Join(1000);
            _thread = null;
        }

        private static bool GameInFront()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == MyPid;
        }

#if DEBUG
        private const int VK_F11 = 0x7A;
        private static bool f11Was;

        private static void ReloadModule()
        {
            string result = ModuleLoader.Reload();
            Plugin.LogInfo("[F11] " + result.Trim());
            if (result.Contains("unchanged")) Speech.Say("No new build to load.", true);
            else if (result.Contains("aborted") || result.Contains("failed")) Speech.Say("Reload failed. Restart the game.", true);
            // A successful reload is announced by the new generation ("Gwent Access reloaded").
        }
#endif

        private static void Loop()
        {
            bool f1Was = false, f5Was = false, f8Was = false, f9Was = false, f7Was = false;
            while (!_stop)
            {
                try
                {
                    // F8: accessibility on/off (ModSwitch). While off, no other mod key does anything.
                    bool f8 = (GetAsyncKeyState(VK_F8) & 0x8000) != 0;
                    if (f8 && !f8Was && GameInFront()) MainThread.Post(ModSwitch.Toggle);
                    f8Was = f8;
                    if (!ModSwitch.On) { Thread.Sleep(30); continue; }
                    bool f1 = (GetAsyncKeyState(VK_F1) & 0x8000) != 0;
                    if (f1 && !f1Was && GameInFront()) MainThread.Post(Ui.HelpScreen.Toggle);
                    f1Was = f1;
                    bool f5 = (GetAsyncKeyState(VK_F5) & 0x8000) != 0;
                    if (f5 && !f5Was && GameInFront()) MainThread.Post(ControllerMode.Toggle);
                    f5Was = f5;
                    bool f9 = (GetAsyncKeyState(VK_F9) & 0x8000) != 0;
                    bool f7 = (GetAsyncKeyState(VK_F7) & 0x8000) != 0;
                    if (f9 && !f9Was && GameInFront()) MainThread.Post(() => Ui.GraphNavigator.SayWhere(true));
                    if (f7 && !f7Was && GameInFront()) { if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) Speech.RepeatMessage(); else Speech.Repeat(); }
                    f9Was = f9; f7Was = f7;
#if DEBUG
                    // F11 (Debug builds): load the module file in the plugin folder, as `just reload` does, when a
                    // newer build has been copied there. Says why when there is nothing to load.
                    bool f11 = (GetAsyncKeyState(VK_F11) & 0x8000) != 0;
                    if (f11 && !f11Was && GameInFront()) MainThread.Post(ReloadModule);
                    f11Was = f11;
#endif
                }
                catch { }
                Thread.Sleep(30);
            }
        }
    }
}
