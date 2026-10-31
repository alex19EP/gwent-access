using System;
using HarmonyLib;
using CrimsonUI;

namespace GwentAccess
{
    /// <summary>
    /// Speaks whichever menu option the game says is selected.
    /// Hooks CrimsonInputLayer.set_SelectedControl, the single place Gwent's UI
    /// records keyboard/controller selection changes.
    /// </summary>
    [HarmonyPatch(typeof(CrimsonInputLayer), "set_SelectedControl")]
    internal static class SelectedControlPatch
    {
        private static IntPtr _lastControl = IntPtr.Zero;
        public static AControl Current;
        private static IntPtr _suppress = IntPtr.Zero;
        private static bool _newView;
        /// <summary>The navigator announces this control itself; don't announce it again when the game selects it.</summary>
        public static void Suppress(AControl c) { _suppress = c.Pointer; }

        static void Postfix(AControl value)
        {
            try
            {
                if (value == null) return;
                if (value.Pointer == _lastControl) return;
                // Deck builder: the game drops the selection onto the card search box whenever the grid refreshes.
                // Put it back where the player was, unless they moved there on purpose.
                if (value.gameObject.name == "TextSearch" && !DeckBuilder.SearchWanted && Current != null && Current.Pointer != value.Pointer)
                {
                    var back = Current;
                    bool ok = false;
                    try { ok = back.gameObject.activeInHierarchy && back.gameObject.name != "TextSearch"; } catch { }
                    if (ok)
                    {
                        Plugin.LogInfo("Search box grabbed the selection; restoring " + UiText.DebugPath(back));
                        DeckBuilder.DeactivateSearch();
                        _lastControl = value.Pointer;
                        MainThread.Later(0.1f, () => { try { if (back.gameObject.activeInHierarchy) { Suppress(back); back.ParentLayer?.RequestControlSelection(back); } } catch { } });
                        return;
                    }
                }
                _lastControl = value.Pointer;
                Current = value;
                if (Ui.GraphNavigator.Owns || Ui.ScreenManager.AnyActive()) return; // a graph screen speaks this UI
                if (value.Pointer == _suppress) { _suppress = IntPtr.Zero; return; }

                if (value.gameObject.name.StartsWith("ShutterView")) { _newView = true; return; }
                string text = UiText.DescribeSelection(value);
                Navigator.NoteShopItem(value, text);
                if (_newView) { _newView = false; text = "New page. " + text + ". F3 to explore, Control Q or Escape to go back"; }

                // Announce the screen when the selection moves onto a different panel.
                string screen = Screens.CheckScreenChange(value);
                if (!string.IsNullOrEmpty(screen)) text = screen + ". " + text;

                Plugin.LogInfo("Selected: " + UiText.DebugPath(value) + " => '" + text + "'");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    bool fresh = (DateTime.UtcNow - Screens.LastAnnounce).TotalSeconds < 2;
                    Speech.Say(text, !fresh);
                }
            }
            catch (Exception e) { Plugin.LogInfo("SelectedControlPatch error: " + e); }
        }
    }
}
