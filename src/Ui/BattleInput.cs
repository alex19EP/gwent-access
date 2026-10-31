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

        /// <summary>Press the coin: the game's own end-turn / pass button.</summary>
        public static bool PressCoin()
        {
            var c = Coin();
            if (c == null) return false;
            try
            {
                string ignore = "?";
                try { ignore = c.IgnoreNextButtonPress.ToString(); } catch { }
                Plugin.LogInfo("[input] coin " + c.m_ButtonMode + ", enabled " + c.m_IsButtonEnabled + ", ignore next " + ignore);
                c.OnPassClicked();
                return true;
            }
            catch (Exception e) { Plugin.LogInfo("[input] coin failed: " + e.Message); return false; }
        }
    }
}
