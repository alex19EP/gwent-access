using System;
using CrimsonUI;
using GwentGameplay;
using GwentUnity;
using GwentVisuals;
using Il2CppInterop.Runtime;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The game's own battle input, driven the way a gamepad player drives it: the selection (the hover the
    /// opponent sees too) follows the mod's cursor, the ghost card shows where a picked-up card would land, and
    /// the buttons run the game's own handlers — B on the component of the current mode, the coin for ending
    /// the turn or passing. Enter itself stays the game's A (the screen leaves the key to it), so playing,
    /// targeting and placing go through the game's flow with its animations, sounds and warnings.
    /// </summary>
    internal static class BattleInput
    {
        private static BattleHandler _handler;

        public static BattleHandler Handler()
        {
            try
            {
                if (_handler != null && _handler.Pointer != IntPtr.Zero && _handler.gameObject != null) return _handler;
            }
            catch { }
            _handler = UnityEngine.Object.FindObjectOfType<BattleHandler>();
            return _handler;
        }

        public static BattleSelectionHandlerComponent Selection()
        {
            try { return Handler()?.SelectionHandler; } catch { return null; }
        }

        public static EBattleNavigationMode Mode
        {
            get
            {
                try { return Selection()?.SelectionMode ?? EBattleNavigationMode.Normal; }
                catch { return EBattleNavigationMode.Normal; }
            }
        }

        public static CardBattleView ViewOf(Card card)
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<CardBattleView>()))
            {
                var v = o.TryCast<CardBattleView>();
                try { if (v?.Card != null && v.Card.Id == card.Id) return v; } catch { }
            }
            return null;
        }

        public static ListLocationView RowView(Location row)
        {
            try { return UnityEngine.Object.FindObjectOfType<BoardView>()?.GetBoardLocationView(row); }
            catch { return null; }
        }

        // ---- the hover ----

        /// <summary>Move the game's selection onto a card (no-op when it's already there).</summary>
        public static void Hover(Card card)
        {
            var view = ViewOf(card);
            if (view != null) Select(view.Cast<ISelectable>(), "card " + card.Id);
        }

        /// <summary>Move the game's selection onto a row.</summary>
        public static void Hover(Location row)
        {
            var view = RowView(row);
            if (view != null) Select(view.Cast<ISelectable>(), "row " + row.PlayerId + "/" + row.Loc);
        }

        /// <summary>While placing: select the row and show the ghost card at <paramref name="index"/> in it,
        /// which is where the game's A puts the card.</summary>
        public static void Ghost(Location row, int index)
        {
            var view = RowView(row);
            if (view == null) return;
            Select(view.Cast<ISelectable>(), "row " + row.PlayerId + "/" + row.Loc);
            try { Handler()?.m_PlayCardHandlerComponent?.AddGhostCard(view, index); }
            catch (Exception e) { Plugin.LogInfo("[input] ghost failed: " + e.Message); }
        }

        private static void Select(ISelectable target, string what)
        {
            try
            {
                var sel = Selection();
                if (sel == null) return;
                var current = sel.LocalUserSelection;
                if (current != null && current.Pointer == target.Pointer) return;
                if (!sel.Select(target, sel.LocalUserId, true)) Plugin.LogInfo("[input] select refused: " + what);
            }
            catch (Exception e) { Plugin.LogInfo("[input] select " + what + " failed: " + e.Message); }
        }

        // ---- the buttons ----

        /// <summary>The game's B in the current mode: put the held card back, or cancel the targeting.</summary>
        public static bool Back()
        {
            var h = Handler();
            if (h == null) return false;
            try
            {
                switch (Mode)
                {
                    case EBattleNavigationMode.PlayCard: h.m_PlayCardHandlerComponent.OnBPressed(); return true;
                    case EBattleNavigationMode.SelectCards: h.m_SelectTargetsHandlerComponent.OnBPressed(); return true;
                    case EBattleNavigationMode.SelectLocations: h.m_SelectLocationsHandlerComponent.OnBPressed(); return true;
                }
            }
            catch (Exception e) { Plugin.LogInfo("[input] back failed: " + e.Message); }
            return false;
        }

        /// <summary>Whether a picked-up special waits for A to confirm its play.</summary>
        public static bool CanConfirmSpecial()
        {
            try
            {
                var h = Handler();
                return h != null && Mode == EBattleNavigationMode.PlayCard && h.m_PlayCardHandlerComponent.m_CanConfirmSpecial;
            }
            catch { return false; }
        }

        /// <summary>Done choosing several targets or rows (the game's finish-early).</summary>
        public static bool FinishTargets()
        {
            var h = Handler();
            if (h == null) return false;
            try
            {
                switch (Mode)
                {
                    case EBattleNavigationMode.SelectCards:
                        var t = h.m_SelectTargetsHandlerComponent;
                        return t.CanPrematurelyFinishTargeting() && t.ForceEndTargeting();
                    case EBattleNavigationMode.SelectLocations:
                        h.m_SelectLocationsHandlerComponent.ForceEndTargeting();
                        return true;
                }
            }
            catch (Exception e) { Plugin.LogInfo("[input] finish failed: " + e.Message); }
            return false;
        }

        private static UIBoardPassWidget Coin()
        {
            try { return UnityEngine.Object.FindObjectOfType<UIBoardPassWidget>(); } catch { return null; }
        }

        /// <summary>What the coin button does now: pass, end the turn, or nothing (disabled).</summary>
        public static UIBoardPassWidget.EButtonMode CoinMode
        {
            get
            {
                var c = Coin();
                try
                {
                    if (c == null || !c.m_IsButtonEnabled) return UIBoardPassWidget.EButtonMode.Invalid;
                    return c.m_ButtonMode;
                }
                catch { return UIBoardPassWidget.EButtonMode.Invalid; }
            }
        }

        /// <summary>
        /// Put the mouse button down on the coin, the game's own end-turn / pass button. Both of the coin's modes
        /// are holds (<c>m_PassHoldSettings</c>, <c>m_EndTurnHoldSettings</c>, set by <c>RefreshPassButton</c>):
        /// the button fills while held, draws the progress on the coin, plays its sounds, and only a completed
        /// hold reaches <c>OnPassClicked</c> (AskPass / AskEndTurn). Clicking it directly would skip all of that
        /// — and the hold that guards against a pass made by accident. <see cref="ReleaseCoin"/> lets go; early
        /// is a cancel, as for a mouse player.
        /// </summary>
        public static bool PressCoin() => SendCoin(CursorButtonEventArgs.EState.ButtonDown);

        /// <summary>Keep the coin pressed: called every frame while the key is held (see <see cref="HoldButton"/>).</summary>
        public static void HoldCoin() => HoldButton(Coin()?.m_PassButton);

        /// <summary>Let go of the coin pressed with <see cref="PressCoin"/>.</summary>
        public static void ReleaseCoin()
        {
            SendCoin(CursorButtonEventArgs.EState.ButtonUp);
            SendButton(Coin()?.m_PassButton, CursorButtonEventArgs.EState.Click);
        }

        /// <summary>How far the coin's hold has filled, 0 to 1 (for the log).</summary>
        public static float CoinHold
        {
            get { try { return Coin()?.m_PassButton?.HoldValue ?? 0f; } catch { return 0f; } }
        }

        private static bool SendCoin(CursorButtonEventArgs.EState state)
        {
            var c = Coin();
            try
            {
                var button = c?.m_PassButton;
                if (button == null) return false;
                string ignore = "?";
                try { ignore = c.IgnoreNextButtonPress.ToString(); } catch { }
                Plugin.LogInfo("[input] coin " + state + ", " + c.m_ButtonMode + ", enabled " + c.m_IsButtonEnabled
                    + ", ignore next " + ignore + ", hold " + button.HoldValue.ToString("0.00"));
            }
            catch { }
            return SendButton(c?.m_PassButton, state);
        }

        /// <summary>
        /// Keep a pressed button down, every frame the key is held. A button's hold fills in its own Update only
        /// while its state stays Down (ButtonControl.Update → HoldButtonController.Update), and completing it
        /// invokes the button's left click; the layer's pointer, not over the button, can set the state back, so
        /// the press is re-sent (a no-op while the state is still Down).
        /// </summary>
        public static void HoldButton(ButtonControl button)
        {
            try { if (button != null && button.ButtonState != ButtonControl.EButtonState.Down) SendButton(button, CursorButtonEventArgs.EState.ButtonDown); }
            catch { }
        }

        /// <summary>Let go of a pressed button as the pointer does: ButtonUp ends the press, then Click — the event
        /// that runs the button's left click (a hold button ignores it unless it reacts to clicks).</summary>
        public static void ReleaseButton(ButtonControl button)
        {
            SendButton(button, CursorButtonEventArgs.EState.ButtonUp);
            SendButton(button, CursorButtonEventArgs.EState.Click);
        }

        /// <summary>
        /// A left mouse button event on a game button, as the layer's pointer sends it (ButtonDown, ButtonUp,
        /// Click): the button's press state, its hold (when it has one: it fills while down and completes or is
        /// interrupted) and its click all run, with the sounds that listen to them.
        /// </summary>
        public static bool SendButton(ButtonControl button, CursorButtonEventArgs.EState state)
        {
            try
            {
                if (button == null || !button.gameObject.activeInHierarchy) return false;
                var args = new CursorButtonEventArgs(button.ParentLayer, CursorInputEventArgs.EMouseButton.Left, UnityEngine.Camera.main,
                    state, button, button, new Il2CppSystem.Collections.Generic.List<CrimsonUI.EventSystems.RaycastResult>());
                button.HandleCursorButtonEvent(args);
                return true;
            }
            catch (Exception e) { Plugin.LogInfo("[input] button " + state + " failed: " + e.Message); return false; }
        }
    }
}
