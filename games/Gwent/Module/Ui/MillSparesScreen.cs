using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The deck builder's Mill Spares window (<see cref="UIMillSparesPanel"/>, run by DBMillSparesState). The game
    /// opens it by itself on entering the deck builder while spare copies exist, and from the deck builder's
    /// mill button. Two steps share the panel:
    ///
    /// - Strategy pick (the first time, or while no strategy is chosen): the header "Выберите режим
    ///   перемалывания", one element per strategy (title, the "Рекомендуется" tag on one, a description and its
    ///   own "Выбрать" button), then the footer: two info lines and the "show a popup before milling" toggle.
    ///   The strategies read as one list: each item is backed by its element's choose button and reads the
    ///   strategy's title, tag and description, never the bare "Выбрать" the button itself carries.
    /// - Summary: the cards that will be milled, per rarity, the scraps and powder gained, and the confirm
    ///   button. Read from the summary widget's own labels, one line each, then the confirm button.
    ///
    /// The panel's own controls outside those two (the settings toggle, the strategy-examples button) follow as
    /// a stop of their own. Any confirm popup the game raises goes to <see cref="PopupScreen"/> (layer 3).
    /// </summary>
    internal sealed class MillSparesScreen : Screen
    {
        public override string Key => "mill-spares";
        public override int Layer => 1;
        /// <summary>Silent: the panel's title is the context of every node instead (see <see cref="Build"/>), so it is
        /// spoken on the first landing and again when the step changes and the title with it.</summary>
        public override string ScreenName => null;

        public override bool IsActive() => Panel() != null;

        // The panel, when the input layer that has the input holds it.
        private static UIMillSparesPanel Panel() => CrimsonNodes.ActivePanel<UIMillSparesPanel>();

        // The panel's title ("Выберите режим перемалывания" on the strategy pick).
        private static string Header()
        {
            var p = Panel();
            if (p == null || !CrimsonNodes.Shown(p.m_Header)) return null;
            var parts = CrimsonNodes.Texts(p.m_Header);
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var seen = new HashSet<System.IntPtr>();

            // The title names the step ("Выберите режим перемалывания", "Лишние карты для перемалывания"). As the
            // context of every node, a new title is a new context, which the cursor enters and speaks.
            string title = Header();
            if (title != null) b.PushContext(title, positions: true);

            var pick = p.m_MillSparesStrategyPickContainerInstance;
            if (CrimsonNodes.Shown(pick)) BuildStrategies(b, pick, seen);

            var summary = p.m_MillSparesSummaryWidgetInstance;
            if (CrimsonNodes.Shown(summary)) BuildSummary(b, summary, seen);

            BuildControls(b, p, seen);
            if (title != null) b.PopContext();
        }

        // ---- strategy pick ----

        private static void BuildStrategies(GraphBuilder b, MillSparesStrategyPickContainer pick, HashSet<System.IntPtr> seen)
        {
            b.BeginStop("mill:strategies");
            foreach (var e in pick.GetComponentsInChildren<MillSparesStrategyPickElement>(false))
            {
                var element = e;
                var button = element.ChooseButton;
                if (button == null || !button.gameObject.activeInHierarchy) continue;
                seen.Add(button.Pointer);
                var id = CrimsonNodes.Id(button);
                b.AddItem(id, new NodeVtable
                {
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Text(element.m_StrategyName), kind: AnnouncementKinds.Label),
                        CrimsonNodes.RolePart(Loc.T("radio button")), // one strategy of the three, the chosen one selected
                        new NodeAnnouncement(() => CrimsonNodes.Shown(element.m_RecomendedText) ? GoText(element.m_RecomendedText) : null, kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => CrimsonNodes.Shown(element.m_SelectedBackground) ? Loc.T("selected") : null, live: true, kind: AnnouncementKinds.Selected),
                        new NodeAnnouncement(() => Text(element.m_StrategyDescription), kind: AnnouncementKinds.Tooltip),
                    },
                    OnActivate = () => Navigator.ClickControl(button),
                    OnTooltip = () => Speech.Say(Text(element.m_StrategyDescription) ?? "", true),
                });
                if (CrimsonNodes.Shown(element.m_SelectedBackground)) b.SetStart(id);
            }
        }

        // ---- summary ----

        private static void BuildSummary(GraphBuilder b, MillSparesSummaryWidget w, HashSet<System.IntPtr> seen)
        {
            b.BeginStop("mill:summary");
            var confirm = w.m_ConfirmationButton;

            // One line per rarity row, its name and count side by side as on screen ("Обычная x2").
            var counters = w.transform.Find("Counters");
            if (counters != null)
                for (int i = 0; i < counters.childCount; i++)
                {
                    var row = counters.GetChild(i);
                    if (!row.gameObject.activeInHierarchy) continue;
                    var parts = CrimsonNodes.Texts(row);
                    if (parts.Count == 0) continue;
                    string line = string.Join(" ", parts);
                    b.AddLabel(ControlId.Structural("mill:summary:" + row.name), () => line);
                }

            // The gain: "Результат: +20 Обрывки, +0 Порошок". The amounts sit beside currency icons; they are named
            // as the top bar's counters name theirs on hover.
            var gain = Gain(w);
            if (gain != null) b.AddLabel(ControlId.Structural("mill:summary:gain"), () => gain);

            if (confirm != null && confirm.gameObject.activeInHierarchy)
            {
                seen.Add(confirm.Pointer);
                var id = CrimsonNodes.Id(confirm);
                b.AddItem(id, CrimsonNodes.Button(confirm));
                b.SetStart(id);
            }
        }

        private static string Gain(MillSparesSummaryWidget w)
        {
            try
            {
                var amounts = new List<string>();
                AddAmount(amounts, w.m_ScrapsGainText, Currency.Scraps());
                AddAmount(amounts, w.m_PowderGainText, Currency.Powder());
                if (amounts.Count == 0) return null;
                var head = w.transform.Find("CardCount/GainText");
                string title = head != null ? Text(head) : null;
                return (title != null ? title + " " : "") + string.Join(", ", amounts);
            }
            catch (System.Exception e) { Plugin.LogInfo("[mill] gain failed: " + e.Message); return null; }
        }

        private static void AddAmount(List<string> amounts, TMPro.TMP_Text text, string currency)
        {
            if (text == null || !text.gameObject.activeInHierarchy) return;
            string amount = UiText.Clean(text.text);
            if (amount.Length == 0) return;
            amounts.Add(currency == null ? amount : amount + " " + currency);
        }

        // ---- the rest of the panel: the settings toggle, the examples button, the footer lines ----

        private static void BuildControls(GraphBuilder b, UIMillSparesPanel p, HashSet<System.IntPtr> seen)
        {
            b.BeginStop("mill:controls");
            foreach (var c in CrimsonNodes.Leaves(p, seen))
            {
                var control = c;
                var toggle = control.TryCast<ToggleButtonControl>();
                if (toggle == null) { b.AddItem(CrimsonNodes.Id(control), CrimsonNodes.Button(control)); continue; }
                var v = CrimsonNodes.WithRole(CrimsonNodes.Button(control), Loc.T("checkbox")); // says checked or not
                var anns = new List<NodeAnnouncement>(v.Announcements)
                {
                    new NodeAnnouncement(() => CrimsonNodes.Toggled(toggle) ? Loc.T("checked") : Loc.T("not checked"), live: true, kind: AnnouncementKinds.Value),
                };
                v.Announcements = anns;
                b.AddItem(CrimsonNodes.Id(control), v);
            }

            // The footer's two info lines ("Эти настройки можно изменить в любое время", the copy-limit rule),
            // shown only on the strategy pick.
            var footer = p.m_StrategiesFooterFooter;
            if (footer != null && footer.activeInHierarchy)
            {
                int n = 0;
                foreach (var t in footer.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    string s = UiText.Clean(t.text);
                    if (s.Length == 0 || UiText.IsPlaceholder(s)) continue;
                    b.AddLabel(ControlId.Structural("mill:footer:" + n++), () => s);
                }
            }
        }

        /// <summary>Enter is the game's own on its selectable controls (the strategy's choose button, the toggle,
        /// confirm): the game's selection follows the cursor, so its key does what a keyboard player gets.
        /// Elsewhere the mod clicks the control under the cursor.</summary>
        public override bool LeavesToGame(KeyCode key)
        {
            if (key != KeyCode.Return && key != KeyCode.KeypadEnter) return false;
            try { return GraphNavigator.CurrentNode?.Id.Reference is AControl c && c.IsCurrentlySelectable(); }
            catch { return false; }
        }

        // ---- helpers ----

        private static string Text(Component c)
        {
            if (c == null) return null;
            var parts = CrimsonNodes.Texts(c);
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        private static string GoText(GameObject go) => go == null ? null : Text(go.transform);
    }
}
