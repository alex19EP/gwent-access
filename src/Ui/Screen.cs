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

        /// <summary>Tab past the last stop returns to the first.</summary>
        public virtual bool Wrap => true;

        /// <summary>The screen's own keys, checked before the navigator's.</summary>
        public virtual IReadOnlyList<ScreenKey> ExtraKeys => Array.Empty<ScreenKey>();

        /// <summary>A navigator key this screen leaves to the game (not blocked, and not acted on unless the
        /// focused node has a handler for it), e.g. Enter while the game's own flow still owns the action.</summary>
        public virtual bool LeavesToGame(KeyCode key) => false;

        /// <summary>
        /// The cursor landed on <paramref name="node"/> (spec P11, cursor write-back). Default: when the node is
        /// backed by a Crimson control, ask its input layer to select it, so the game's highlight, its hover
        /// reactions (a play-mode card raising its deck selector) and its own Enter target follow the mod's
        /// cursor.
        /// </summary>
        public virtual void OnCursorMoved(GraphNode node)
        {
            if (node?.Id.Reference is AControl c)
            {
                if (!c.gameObject.activeInHierarchy) return;
                var layer = c.ParentLayer;
                if (layer != null && (layer.SelectedControl == null || layer.SelectedControl.Pointer != c.Pointer))
                    layer.RequestControlSelection(c);
            }
        }

        /// <summary>
        /// The control the game itself has selected, if this screen's nodes are Crimson controls keyed by
        /// <see cref="CrimsonNodes.Id"/>. When the game moves its selection on its own (a panel opening onto the
        /// current deck, the mouse), the cursor follows it; moves the mod wrote back are recognised and ignored.
        /// </summary>
        public virtual AControl GameSelection() => CrimsonInputManager.Instance?.ActiveLayer?.SelectedControl;

        public virtual void OnPush() { }
        public virtual void OnFocus() { }
        public virtual void OnUnfocus() { }
        public virtual void OnPop() { }
    }
}
