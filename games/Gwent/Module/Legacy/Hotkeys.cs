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
    /// F1 = help (Shift+F1 = card details on the older screens). F2/F3 = previous/next item, F4 = click it. F5 = toggle controller mode. F6 = what can I do here (key hints). F8 = accessibility on/off. F9 = read the current screen. F11 (Debug builds) = load a newer module build.  F7 = repeat the last thing spoken. (F10 avoided: Windows uses it for menus and it steals focus.)
    /// </summary>
    internal static class Hotkeys
    {
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private const int VK_F1 = 0x70, VK_SHIFT = 0x10, VK_F2 = 0x71, VK_F3 = 0x72, VK_F4 = 0x73, VK_MENU = 0x12, VK_F5 = 0x74, VK_F6 = 0x75, VK_F8 = 0x77, VK_F9 = 0x78, VK_F7 = 0x76;
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

        private const int VK_CONTROL = 0x11;
        private static readonly Dictionary<int, Action> CtrlKeys = new Dictionary<int, Action>
        {
            { 0x48, () => { if (DeckBuilder.Active) DeckBuilder.SayList(); } },  // Ctrl+H deck list
            { 0x41, DeckBuilder.Add },                                    // Ctrl+A add card to deck
            { 0x52, DeckBuilder.Remove },                                 // Ctrl+R remove card from deck
            { 0x44, DeckBuilder.SayStats },                               // Ctrl+D deck stats
            { 0x51, Navigator.ClickExit },                                // Ctrl+Q press Exit/Back
            { 0x4E, DeckBuilder.Rename },                                 // Ctrl+N rename deck
            { 0x46, DeckBuilder.Search },                                 // Ctrl+F card search
            { 0x55, DeckBuilder.Duplicate },                              // Ctrl+U duplicate deck
            { 0x42, Navigator.Buy },                                      // Ctrl+B buy with Ore (asks twice)
            { 0x31, () => { if ((DeckBuilder.Edit != null || DeckBuilder.List != null) && Battle.Controller() == null) Navigator.ClickExit(); } },   // Ctrl+1 (in the deck builder: same as Ctrl+Q, the keys are next to each other)
        };
        private static readonly HashSet<int> CtrlDown = new HashSet<int>();

        private static bool _enterWas;

        /// <summary>The object navigator's keys (F2/F3 move, F4 clicks) stand down while a graph screen owns the
        /// UI: it moves the game's selection, the graph cursor follows, and every press would be spoken twice.</summary>
        private static void PostObjectNav(Action a) => MainThread.Post(() =>
        {
            if (Ui.GraphNavigator.Owns || Ui.ScreenManager.AnyActive()) return;
            a();
        });

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
            bool f1Was = false, f2Was = false, f3Was = false, f4Was = false, f5Was = false, f6Was = false, f8Was = false, f9Was = false, f7Was = false;
            while (!_stop)
            {
                try
                {
                    // F8: accessibility on/off (ModSwitch). While off, no other mod key does anything.
                    bool f8 = (GetAsyncKeyState(VK_F8) & 0x8000) != 0;
                    if (f8 && !f8Was && GameInFront()) MainThread.Post(ModSwitch.Toggle);
                    f8Was = f8;
                    if (!ModSwitch.On) { Thread.Sleep(30); continue; }
                    bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    foreach (var kv in CtrlKeys)
                    {
                        bool down = ctrl && (GetAsyncKeyState(kv.Key) & 0x8000) != 0;
                        if (down && !CtrlDown.Contains(kv.Key) && GameInFront())
                        {
                            var a = kv.Value; int k = kv.Key;
                            MainThread.Post(() =>
                            {
                                // Ctrl+arrows belong to a mod screen when one is up (the battle screen reads card lines).
                                if (k >= 0x25 && k <= 0x28 && Ui.GraphNavigator.Owns) return;
                                Plugin.LogInfo("Hotkey Ctrl+" + (k >= 0x30 && k <= 0x5A ? ((char)k).ToString() : "0x" + k.ToString("X2")));
                                a();
                            });
                        }
                        if (down) CtrlDown.Add(kv.Key); else CtrlDown.Remove(kv.Key);
                    }
                    bool f1 = (GetAsyncKeyState(VK_F1) & 0x8000) != 0;
                    // F1 is the help (Ui.HelpScreen); the older screens' card details moved to Shift+F1.
                    if (f1 && !f1Was && GameInFront())
                    {
                        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) MainThread.Post(CardText.ReadDetails);
                        else MainThread.Post(Ui.HelpScreen.Toggle);
                    }
                    f1Was = f1;
                    bool f2 = (GetAsyncKeyState(VK_F2) & 0x8000) != 0;
                    bool f3 = (GetAsyncKeyState(VK_F3) & 0x8000) != 0;
                    bool f4 = (GetAsyncKeyState(VK_F4) & 0x8000) != 0;
                    bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                    bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                    if (f2 && !f2Was && GameInFront()) { if (shift) PostObjectNav(() => Navigator.MoveSection(-1)); else PostObjectNav(() => Navigator.Move(-1)); }
                    if (f3 && !f3Was && GameInFront()) { if (shift) PostObjectNav(() => Navigator.MoveSection(1)); else PostObjectNav(() => Navigator.Move(1)); }
                    if (f4 && !f4Was && !alt && GameInFront()) PostObjectNav(Navigator.Click);
                    f2Was = f2; f3Was = f3; f4Was = f4;
                    bool enter = (GetAsyncKeyState(0x0D) & 0x8000) != 0;
                    if (enter && !_enterWas && !ctrl && !alt && GameInFront()) MainThread.Post(Navigator.EnterOnBottomButton);
                    _enterWas = enter;
                    bool f5 = (GetAsyncKeyState(VK_F5) & 0x8000) != 0;
                    if (f5 && !f5Was && GameInFront()) MainThread.Post(ControllerMode.Toggle);
                    f5Was = f5;
                    bool f6 = (GetAsyncKeyState(VK_F6) & 0x8000) != 0;
                    if (f6 && !f6Was && GameInFront()) MainThread.Post(KeyHints.ReadHints);
                    f6Was = f6;
                    // F9: read the current screen (was F8, now the on/off switch).
                    bool f9 = (GetAsyncKeyState(VK_F9) & 0x8000) != 0;
                    bool f7 = (GetAsyncKeyState(VK_F7) & 0x8000) != 0;
                    if (f9 && !f9Was && GameInFront()) MainThread.Post(Screens.ReadCurrent);
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
