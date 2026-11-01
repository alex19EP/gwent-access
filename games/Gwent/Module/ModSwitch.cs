namespace GwentAccess
{
    /// <summary>
    /// F8: accessibility on/off, as Hearthstone Access does with F8, so a sighted helper can take over. Off,
    /// the mod gives the game every key back (the navigator's claims are released), stops speaking, stops its
    /// screens and older hotkeys, hands the input mode back to the game and lets a forfeit through without its
    /// guard: the game plays exactly as without the mod. On again, the screens re-derive from what the game
    /// shows and the controller mode follows the hardware again. Only the switch's own two lines are spoken.
    /// </summary>
    internal static class ModSwitch
    {
        public static bool On { get; private set; } = true;

        public static void Toggle()
        {
            if (On)
            {
                On = false;
                Ui.ScreenManager.Suspend();
                ControllerMode.Suspend();
                Speech.Say("Accessibility off", true);
                Speech.Muted = true;
            }
            else
            {
                Speech.Muted = false;
                On = true;
                ControllerMode.Resume();
                Speech.Say("Accessibility on", true);
            }
            Plugin.LogInfo("[switch] accessibility " + (On ? "on" : "off"));
        }
    }
}
