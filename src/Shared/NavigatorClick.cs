using System;
using CrimsonUI;

namespace GwentAccess
{
    /// <summary>
    /// The click every game's graph screens use on Enter. The rest of <see cref="Navigator"/> (Gwent's legacy
    /// F2/F3 object navigation) lives with the Gwent module.
    /// </summary>
    internal static partial class Navigator
    {
        /// <summary>Run a control's own click path: a bottom-bar button's container callback, a ButtonControl's
        /// left click, or a Button's Click. Shared by the F4 navigator and the graph screens' Enter.</summary>
        public static void ClickControl(AControl c)
        {
            try
            {
                var bc = c.TryCast<ButtonControl>();
                // Bottom-bar buttons (Exit, Save and exit...) run a callback stored by their container, not a click handler.
                if (bc != null)
                {
                    try
                    {
                        var ibc = c.GetComponentInParent<InputButtonContainer>();
                        if (ibc != null)
                        {
                            var map = ibc.m_Buttons;
                            if (map != null && map.ContainsKey(bc))
                            {
                                var act = map[bc];
                                if (act != null)
                                {
                                    Plugin.LogInfo("Navigator click (container action): " + UiText.DebugPath(c));
                                    act.Invoke();
                                    return;
                                }
                            }
                        }
                    }
                    catch (Exception e) { Plugin.LogInfo("Container action failed: " + e.Message); }
                }
                if (bc != null)
                {
                    Plugin.LogInfo("Navigator click (ButtonControl): " + UiText.DebugPath(c));
#if THRONEBREAKER // the older engine's ButtonControl has LeftClick, not InvokeOnLeftClick
                    bc.LeftClick();
#else
                    bc.InvokeOnLeftClick();
#endif
                    return;
                }
                var b = c.TryCast<CrimsonUI.Button>();
                if (b != null)
                {
                    Plugin.LogInfo("Navigator click (Button): " + UiText.DebugPath(c));
                    b.Click();
                    return;
                }
                Speech.Say("Can't click this item. Try Enter.", true);
            }
            catch (Exception e)
            {
                Plugin.LogInfo("Navigator click failed: " + e);
                Speech.Say("Click failed.", true);
            }
        }
    }
}
