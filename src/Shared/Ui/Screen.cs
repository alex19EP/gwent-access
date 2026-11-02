using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>A key a screen handles itself (spec §7.4): the game's own per-screen verbs, offered under the
    /// mod's keyboard. Claimed from the game while the screen is focused.</summary>
    internal sealed class ScreenKey
    {
        public KeyCode Key { get; init; }
        public bool Ctrl { get; init; }
        public bool Shift { get; init; }
        public Action Run { get; init; }

        /// <summary>Spoken by the key help, e.g. "D, deck details".</summary>
        public string Hint { get; init; }
        /// <summary>Only while this holds (null: always); otherwise the key does what it would without it, e.g.
        /// Up/Down read the focused card's lines only while a card is focused.</summary>
        public Func<bool> When { get; init; }
    }

    /// <summary>
    /// A navigable screen (spec §9): an <see cref="IsActive"/> predicate over live game state, a stacking
    /// <see cref="Layer"/>, and an immediate-mode <see cref="Build"/> the navigator calls on every render.
    /// Screens hold no tree and no focus: the navigator keeps a cursor per live screen and reconciles it by
    /// <see cref="ControlId"/> after each rebuild.
    ///
    /// Lifecycle from <see cref="ScreenManager"/>: OnPush (entered the stack) → OnFocus (became the top);
    /// OnUnfocus → OnPop on the way out. A screen covered by a higher one gets OnUnfocus, keeps its cursor, and
    /// gets OnFocus again when uncovered.
    /// </summary>
    internal abstract class Screen
    {
        /// <summary>Stable identity, for logs and the dev tools.</summary>
        public abstract string Key { get; }

        /// <summary>Spoken (queued) when the screen gains focus. Null = silent.</summary>
        public virtual string ScreenName => null;

        /// <summary>Stacking order: higher sits on top and gets the keyboard.</summary>
        public virtual int Layer => 0;

        /// <summary>Re-derived from live game state on every poll; never a flag set by an open event.</summary>
        public abstract bool IsActive();

        /// <summary>Declare the controls, fresh from live game state. Declaring nothing means "no content yet":
        /// the navigator keeps the cursor and tries again next frame.</summary>
        public virtual void Build(GraphBuilder b) { }

        /// <summary>A landing nobody keyed (the differ's: content changed under the cursor) on
        /// <paramref name="node"/> stays silent. The cursor still moves; a key move onto it still speaks. E.g. a
        /// dialogue line the game's voice acting is already speaking.</summary>
        public virtual bool QuietLanding(GraphNode node) => false;

        /// <summary>A list with a single item still says "1 of 1" (a menu whose size is news: Gwent's game modes
        /// and deck while a new player has only one of each).</summary>
        public virtual bool PositionOfOne => false;
        /// <summary>Tab past the last stop returns to the first.</summary>
        public virtual bool Wrap => true;

        /// <summary>When a control the player activated leaves the graph (a picker or list took its place) and
        /// later comes back, the cursor returns to it instead of following the game's selection (see
        /// <see cref="GraphNavigator"/>). Off for screens whose controls come back as different things under
        /// the same path (a hand slot refilled by the next card).</summary>
        public virtual bool ReturnsToOpener => true;

        /// <summary>Say <see cref="ScreenName"/> even when the screen comes back from under an overlay as the same
        /// screen (a loading screen: each showing is news, not the place the player left).</summary>
        public virtual bool NamedOnReturn => false;

        /// <summary>A screen opened over another as a window of it (the fallback panel screen: the journey, the
        /// leaderboards… opened from the main menu), though on a lower layer: the screen under it keeps its cursor
        /// while it is up, as under an overlay of a higher layer.</summary>
        public virtual bool Overlays => false;

        /// <summary>Backspace when the focused item has no Backspace action of its own (Hearthstone Access's Back):
        /// leave the screen the way the game does (its Back/Exit/Close button, or the toggle that opened it). True
        /// when something was pressed; false says so to the player instead of a dead key. The default runs the
        /// screen's bar entry bound to the game's Back key (see <see cref="PressBack"/>).</summary>
        public virtual bool Back() => PressBack();

        /// <summary>The game's Back on screen: a shown bar entry (InputButtonContainer) whose key glyph is the
        /// gamepad's back key, on the input layer that has the input. Its button is clicked (the mouse's path, so
        /// sounds and the UX event run); the glyph and the button are separate objects, paired by the callback
        /// they share. Without a button the callback itself runs, as the key does.</summary>
        public static bool PressBack()
        {
#if !GWENT
            return false; // Thronebreaker's older engine: its bar buttons are matched by its own screens
#else
            try
            {
                var layer = CrimsonInputManager.Instance?.ActiveLayer;
                if (layer == null) return false;
                var attached = layer.GetAttachedControls();
                for (int i = 0; i < attached.Count; i++)
                {
                    var root = attached[i];
                    if (root == null || !root.gameObject.activeInHierarchy) continue;
                    foreach (var bar in root.GetComponentsInChildren<InputButtonContainer>(false))
                    {
                        var labels = bar.m_Labels;
                        for (int j = 0; labels != null && j < labels.Count; j++)
                        {
                            var g = labels[j];
                            if (g == null || !g.gameObject.activeInHierarchy || !BackKeys.Contains(g.InputKey.ToString())) continue;
                            var callback = g.Callback;
                            if (callback == null) continue;
                            foreach (var kv in bar.m_Buttons)
                                if (kv.Value != null && kv.Value.Pointer == callback.Pointer && kv.Key != null && kv.Key.gameObject.activeInHierarchy)
                                {
                                    Plugin.LogInfo("[back] " + kv.Key.gameObject.name + " (" + string.Join("|", CrimsonNodes.Texts(kv.Key)) + ")");
                                    Navigator.ClickControl(kv.Key);
                                    return true;
                                }
                            Plugin.LogInfo("[back] callback of " + g.gameObject.name);
                            callback.Invoke();
                            return true;
                        }
                    }
                }
            }
            catch (Exception e) { Plugin.LogInfo("[back] " + e.Message); }
            return false;
#endif
        }

        private static readonly HashSet<string> BackKeys = new HashSet<string> { "B", "Back", "Cancel", "Escape" };

        /// <summary>The screen's own keys, checked before the navigator's.</summary>
        public virtual IReadOnlyList<ScreenKey> ExtraKeys => Array.Empty<ScreenKey>();

        /// <summary>A navigator key this screen leaves to the game (not blocked, and not acted on unless the
        /// focused node has a handler for it), e.g. Enter while the game's own flow still owns the action.</summary>
        public virtual bool LeavesToGame(KeyCode key) => false;

        /// <summary>
        /// The cursor landed on <paramref name="node"/> (spec P11, cursor write-back). Default: when the node is
        /// backed by a Crimson control, ask its input layer to select it, so the game's highlight, its hover
        /// reactions (a play-mode card raising its deck selector) and its own Enter target follow the mod's
        /// cursor. Then raise the control's own OnHover event, as the layer does when the mouse moves onto it:
        /// that is what the UI sound handlers hear, so landing plays the hover sound a mouse player gets.
        /// </summary>
        public virtual void OnCursorMoved(GraphNode node)
        {
            if (node?.Id.Reference is AControl c)
            {
                if (!c.gameObject.activeInHierarchy) return;
                var layer = c.ParentLayer;
                // Only a control the layer can select: a button-bar button (settings' Применить, a popup's ОК)
                // never is. Gwent ignores the request; Thronebreaker's older layer drops the selection and puts
                // it back on the previous control, which the navigator then follows, pulling the cursor off the
                // button the player just tabbed to.
                if (layer != null && c.IsCurrentlySelectable()
                    && (layer.SelectedControl == null || layer.SelectedControl.Pointer != c.Pointer))
                    layer.RequestControlSelection(c);
                try { c.OnHover?.Invoke(c); }
                catch (Exception e) { Plugin.LogInfo("[screens] hover failed: " + e.Message); }
            }
        }

        /// <summary>
        /// The control the game itself has selected, if this screen's nodes are Crimson controls keyed by
        /// <see cref="CrimsonNodes.Id"/>. When the game moves its selection on its own (a panel opening onto the
        /// current deck, the mouse), the cursor follows it; moves the mod wrote back are recognised and ignored.
        /// </summary>
        public virtual AControl GameSelection() => CrimsonInputManager.Instance?.ActiveLayer?.SelectedControl;

        /// <summary>Every frame while the screen is focused and the keyboard is read, after its keys were handled:
        /// for what spans frames, such as a key held down.</summary>
        public virtual void Tick() { }

        public virtual void OnPush() { }
        public virtual void OnFocus() { }
        public virtual void OnUnfocus() { }
        public virtual void OnPop() { }
    }
}
