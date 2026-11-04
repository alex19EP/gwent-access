using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentGameplay;
using GwentUnity.Taunts;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The emote wheel (GwentUnity.Taunts.TauntSelectorView): what a sighted player gets by clicking the taunt
    /// zone on their own avatar. M on the battle screen opens it the same way (<see cref="Open"/>: the cursor
    /// path on the local <c>BoardPlayerView.m_TauntHitZone</c>), so the game's own sound and animation run.
    ///
    /// One list, as the wheel is read round: each emote slot (<c>m_SlotViews</c>, in the wheel's order) by the
    /// words drawn on it, "unavailable" while the game greys it out (the cooldown); then the opponent's mute in
    /// the words of the game's own key hint for it. Enter on an emote is the slot's own click (the voice line,
    /// the bubble and the cooldown all follow from the game); Enter on the mute runs the wheel's mute handler.
    /// Backspace is the wheel's cancel button; Escape stays the game's. When the wheel closes the battle screen
    /// gets its cursor back (it was only covered), and the bubble of the emote just sent is read once by the
    /// narrator ("You: Well played.").
    /// </summary>
    internal sealed class EmoteScreen : Screen
    {
        public override string Key => "emotes";
        public override int Layer => 1; // over the battle board, like the mulligan and a choice

        public override string ScreenName => "Emotes";

        public override bool IsActive() => View() != null;

        // ---- finding the game's objects ----

        // The match's taunts manager, cached per match: looking for the board view every frame is too costly.
        private static IntPtr _managerOf;
        private static TauntsManager _manager;

        private static TauntsManager Manager(GameController gc)
        {
            if (gc == null) return null;
            if (_managerOf != gc.Pointer || _manager == null)
            {
                _manager = UnityEngine.Object.FindObjectOfType<BoardView>()?.TauntsManager;
                _managerOf = _manager != null ? gc.Pointer : IntPtr.Zero;
            }
            return _manager;
        }

        /// <summary>The wheel while it is fully open (its slots take clicks only once the show animation is over;
        /// on the way out it is the battle screen's again).</summary>
        private static TauntSelectorView View()
        {
            var gc = Battle.Controller();
            if (gc == null) return null;
            var v = Manager(gc)?.m_TauntSelector;
            if (v == null || !v.gameObject.activeInHierarchy) return null;
            return v.Visibility == CrimsonUIBehaviour.EVisibility.Shown ? v : null;
        }

        /// <summary>The local player's avatar side of the board, which carries the taunt zone.</summary>
        private static BoardPlayerView LocalView(GameController gc)
        {
            var me = Battle.Me(gc);
            foreach (var v in UnityEngine.Object.FindObjectsOfType<BoardPlayerView>())
            {
                try { if (v != null && v.PlayerId == me) return v; } catch { }
            }
            return null;
        }

        // ---- opening (battle key M) ----

        private const string Unavailable = "Emotes unavailable right now.";

        // When M clicked the zone: the wheel should be up shortly after; if not, the game refused silently.
        private static float _openAt = -1;

        /// <summary>
        /// Battle key M: click the taunt zone on your avatar the way the mouse does (ButtonDown, ButtonUp, Click
        /// through <see cref="Navigator.MouseClick(ButtonControl)"/>), so the wheel opens with its own sound. Said
        /// instead when the game won't take an emote now: emotes locked (the opponent or the settings), the
        /// cooldown, or no zone drawn.
        /// </summary>
        public static void Open()
        {
            try
            {
                var gc = Battle.Controller();
                var tm = Manager(gc);
                var view = gc != null ? LocalView(gc) : null;
                var zone = view?.m_TauntHitZone;
                bool locked = tm == null || tm.TauntsLocked;
                bool allowed = true;
                try { allowed = tm?.TimerManager == null || tm.TimerManager.IsTauntAllowed; } catch { }
                bool shown = zone != null && zone.gameObject.activeInHierarchy;
                Plugin.LogInfo("[emotes] open: locked " + locked + ", allowed " + allowed + ", zone shown " + shown);
                if (locked || !allowed || !shown) { Speech.Say(Unavailable, true); return; }
                _openAt = Time.realtimeSinceStartup;
                if (Navigator.MouseClick(zone)) return;
                // The cursor click threw: the manager's own opener, as the gamepad's emote key reaches it.
                Plugin.LogInfo("[emotes] cursor click failed, opening through the manager");
                tm.ShowTauntSelector();
            }
            catch (Exception e)
            {
                Plugin.LogInfo("[emotes] open failed: " + e.Message);
                _openAt = -1;
                Speech.Say(Unavailable, true);
            }
        }

        /// <summary>From the battle screen's Tick: M clicked the zone but no wheel came up (the battle screen is
        /// still the one focused a second later), so the game turned it down without a word.</summary>
        public static void WatchOpen()
        {
            // Two seconds: the wheel counts as open only once its show animation is over.
            if (_openAt < 0 || Time.realtimeSinceStartup - _openAt < 2f) return;
            _openAt = -1;
            Plugin.LogInfo("[emotes] the wheel did not open");
            Speech.Say(Unavailable, true);
        }

        public override void OnFocus() => _openAt = -1;

        // ---- the wheel ----

        public override void Build(GraphBuilder b)
        {
            var v = View();
            if (v == null) return;
            b.BeginStop("emotes");
            ControlId first = default;
            bool any = false;
            var slots = v.m_SlotViews;
            for (int i = 0; slots != null && i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.gameObject.activeInHierarchy) continue;
                var id = CrimsonNodes.Id(slot);
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => SlotLabel(slot), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => Interactable(slot) ? null : "unavailable", live: true, kind: AnnouncementKinds.Enabled),
                    },
                    OnActivate = () => Send(slot),
                });
                if (!any) { first = id; any = true; }
            }
            if (!any) return; // slots not filled yet: try again next frame

            var tm = v.m_TauntsManager ?? _manager;
            b.AddItem(ControlId.Structural("emotes:mute"), new NodeVtable
            {
                Announcements = new[]
                {
                    new NodeAnnouncement(() => MuteLabel(tm), live: true, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Muted(tm) ? "muted" : null, live: true, kind: AnnouncementKinds.Value),
                },
                OnActivate = () => ToggleMute(v),
            });
            b.SetStart(first);
        }

        // The words drawn on the slot ("Well played."), else whatever text it carries.
        private static string SlotLabel(TauntCategorySelectorView slot)
        {
            string s = "";
            try { s = UiText.Clean(slot.Text?.text); } catch { }
            return s.Length > 0 ? s : CrimsonNodes.Label(slot);
        }

        /// <summary>Whether the slot is drawn usable: the wheel greys a slot's words out (its
        /// <c>DisabledTextColor</c>) when an emote can't be sent. The slot's own <c>IsInteractable</c> reads false
        /// on a wheel drawn fully usable (seen in a practice match), so it isn't what the player sees.</summary>
        private static bool Interactable(TauntCategorySelectorView slot)
        {
            try
            {
                var c = slot.Text.color;
                var d = slot.DisabledTextColor;
                return !(Mathf.Abs(c.r - d.r) < 0.01f && Mathf.Abs(c.g - d.g) < 0.01f && Mathf.Abs(c.b - d.b) < 0.01f);
            }
            catch { return true; }
        }

        /// <summary>
        /// Sends the emote the way the wheel takes it: the slot selected (as the pointer or stick points at it),
        /// then the wheel's own press handler, which raises its request to the taunts manager. A cursor click on
        /// the slot doesn't do it: the radial wheel reads clicks from the pointer's angle over the whole view, so
        /// a click sent to the slot only closed the wheel, nothing sent (practice match, taunt count unchanged).
        /// </summary>
        private static void Send(TauntCategorySelectorView slot)
        {
            if (!Interactable(slot)) { Speech.Say(Unavailable, true); return; }
            var v = View();
            if (v == null) return;
            Plugin.LogInfo("[emotes] send: " + SlotLabel(slot));
            NarrateTauntWho.SentAt = Time.realtimeSinceStartup;
            try
            {
                v.SelectSlot(slot);
                v.OnCategoryPressed(slot.Cast<ButtonControl>());
            }
            catch (Exception e) { Plugin.LogInfo("[emotes] send failed: " + e.Message); Speech.Say(Unavailable, true); }
        }

        /// <summary>The wheel's highlight follows the cursor, as it follows the pointer's angle.</summary>
        public override void OnCursorMoved(GraphNode node)
        {
            base.OnCursorMoved(node);
            try
            {
                var v = View();
                if (v == null || node?.Id.Reference is not AControl c) return;
                foreach (var slot in v.m_SlotViews)
                    if (slot != null && slot.Pointer == c.Pointer) { v.SelectSlot(slot); return; }
            }
            catch (Exception e) { Plugin.LogInfo("[emotes] select failed: " + e.Message); }
        }

        private static bool Muted(TauntsManager tm)
        {
            try { return tm != null && tm.IsOpponentMuted; } catch { return false; }
        }

        // The game's key hint for the mute in the battle's input bar: "Mute opponent" / "Unmute opponent".
        private static string MuteLabel(TauntsManager tm)
        {
            bool muted = Muted(tm);
            string key = null;
            try { key = muted ? UIBattleInputWidget.UNMUTE_OPP_KEY : UIBattleInputWidget.MUTE_OPP_KEY; } catch { }
            return GameText.Get(key, muted ? "Unmute opponent" : "Mute opponent");
        }

        // The wheel draws no mute button of its own: the toggle is its mute key's handler, which raises the
        // view's OnToggleMutePressed for the manager to flip the mute.
        private static void ToggleMute(TauntSelectorView v)
        {
            try
            {
                Plugin.LogInfo("[emotes] mute toggle (was " + (Muted(v.m_TauntsManager) ? "muted" : "not muted") + ")");
                v.OnMuteTogglePressed();
            }
            catch (Exception e) { Plugin.LogInfo("[emotes] mute toggle failed: " + e.Message); }
        }

        /// <summary>Backspace: the wheel's cancel button, clicked as the mouse does; its handler if the click fails.</summary>
        public override bool Back()
        {
            var v = View();
            if (v == null) return false;
            var cancel = v.m_CancelButton;
            if (cancel != null && Navigator.MouseClick(cancel)) { Plugin.LogInfo("[emotes] cancel"); return true; }
            try { v.OnCancelPressed(); Plugin.LogInfo("[emotes] cancel (handler)"); return true; }
            catch (Exception e) { Plugin.LogInfo("[emotes] cancel failed: " + e.Message); return false; }
        }

        // The mod owns the cursor here: the real mouse pointer may rest anywhere over the wheel.
        public override AControl GameSelection() => null;
    }
}
