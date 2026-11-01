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
        /// <summary>Run a control's own click path: a ButtonControl's left click or a Button's Click. Shared by the
        /// F4 navigator and the graph screens' Enter.
        ///
        /// A bottom-bar button's callback must run through the button, not be invoked from the container's
        /// <c>m_Buttons</c> map: <c>InvokeOnLeftClick</c> runs it (the container binds it to <c>OnLeftClick</c>) and
        /// then raises the button's UX interaction event, and the new-player flow waits on that event. Its
        /// ShowTutorialInfo step only moves on to the first tutorial battle when content id 20, the intro panel's
        /// Continue button, reports a click; calling the callback alone leaves the panel up for good.</summary>
        public static void ClickControl(AControl c)
        {
            try
            {
                var bc = c.TryCast<ButtonControl>();
                if (bc != null)
                {
#if THRONEBREAKER
                    // The older engine's ButtonControl has LeftClick, not InvokeOnLeftClick, and its bottom-bar
                    // buttons are still driven through the container's stored callback.
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
                    Plugin.LogInfo("Navigator click (ButtonControl): " + UiText.DebugPath(c));
                    bc.LeftClick();
#else
                    Plugin.LogInfo("Navigator click (ButtonControl): " + UiText.DebugPath(c));
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
