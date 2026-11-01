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
                    // The cursor path first (press, release, click, as the mouse sends them): in play, a settings
                    // category opened from the starter-deck screen and a reward popup's reward ignored
                    // InvokeOnLeftClick and reacted to the cursor click, which also runs OnLeftClick (so a bar
                    // button's callback and the UX interaction event, below). InvokeOnLeftClick is the fallback
                    // for when the cursor click throws.
                    if (MouseClick(bc))
                    {
                        Plugin.LogInfo("Navigator click (cursor" + (bc.UXId > 0 ? ", ux " + bc.UXId : "") + "): " + UiText.DebugPath(c));
                        return;
                    }
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

#if !THRONEBREAKER
        /// <summary>A left click the way the mouse makes one: ButtonDown, ButtonUp, then the Click the layer sends
        /// for a release over the pressed control. Press sounds, click handlers, a bar button's callback and the
        /// UX interaction event all run. False when the button is gone or the game threw.</summary>
        public static bool MouseClick(ButtonControl c)
        {
            if (c == null || !c.gameObject.activeInHierarchy) return false;
            try
            {
                foreach (var state in new[] { CursorButtonEventArgs.EState.ButtonDown, CursorButtonEventArgs.EState.ButtonUp, CursorButtonEventArgs.EState.Click })
                    c.HandleCursorButtonEvent(new CursorButtonEventArgs(c.ParentLayer, CursorInputEventArgs.EMouseButton.Left, UnityEngine.Camera.main,
                        state, c, c, new Il2CppSystem.Collections.Generic.List<CrimsonUI.EventSystems.RaycastResult>()));
                return true;
            }
            catch (Exception e) { Plugin.LogInfo("Cursor click failed: " + e.Message); return false; }
        }
#endif
    }
}
