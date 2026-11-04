using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentVisuals;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Paying for a season-cycle quest change (<see cref="UIOuroborosRerollPanel"/>): once the free changes
    /// are used, Change Quest opens this window, a header, a description above and below, and a button per way
    /// to pay ("50" beside an ore icon). The fallback read the prices as bare numbers. Here the header names
    /// the screen, the descriptions are read, and each choice says its price with the currency named (the
    /// game draws the currency as a picture); Enter pays that way, the mouse's click. Then the window's bar
    /// (Cancel). Backspace is its Back.
    /// </summary>
    internal sealed class CycleRerollScreen : Screen
    {
        public override string Key => "cycle-reroll";
        public override int Layer => 2; // over the cycle panel that opened it

        public override string ScreenName
        {
            get
            {
                var p = Panel();
                string h = p != null ? CrimsonNodes.Text(p.m_Header) : "";
                return h.Length > 0 ? h : GameText.Get("ouroboros_reroll_title", "Change Quest");
            }
        }

        public override bool IsActive() => Panel() != null;

        private static UIOuroborosRerollPanel Panel()
        {
            var p = CrimsonNodes.ActivePanel<UIOuroborosRerollPanel>();
            if (p == null) return null;
            var v = p.Visibility;
            return v == CrimsonUIBehaviour.EVisibility.Shown || v == CrimsonUIBehaviour.EVisibility.Showing ? p : null;
        }

        public override AControl GameSelection() => null;

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();
            b.BeginStop("reroll:choices");
            string top = CrimsonNodes.Text(p.m_DescriptionTop);
            if (top.Length > 0) b.AddLabel(ControlId.Structural("reroll:top"), () => CrimsonNodes.Text(p.m_DescriptionTop));
            var choices = p.m_Choices;
            ControlId first = null;
            for (int i = 0; choices != null && i < choices.Count; i++)
            {
                var choice = choices[i];
                var button = choice?.m_RerollChoiceButton;
                if (!CrimsonNodes.Shown(choice) || !CrimsonNodes.Shown(button) || !used.Add(button.Pointer)) continue;
                var v = CrimsonNodes.Button(button, () => Price(choice));
                v.OnActivate = () =>
                {
                    if (button.IsEnabled) Navigator.MouseClick(button);
                    else Speech.Say("Not available right now.", true);
                };
                var id = CrimsonNodes.Id(button);
                first ??= id;
                b.AddItem(id, v);
            }
            // The window opens on the choices: the description is a line above.
            if (first != null) b.SetStart(first);
            string bottom = CrimsonNodes.Text(p.m_DescriptionBottom);
            if (bottom.Length > 0) b.AddLabel(ControlId.Structural("reroll:bottom"), () => CrimsonNodes.Text(p.m_DescriptionBottom));

            var bar = CrimsonNodes.BarButtons(p, used);
            if (bar.Count > 0)
            {
                b.BeginStop("reroll:bar");
                foreach (var c in bar) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
        }

        // "50 Ore": the caption with its currency named, from an inline sprite or else the choice's picture.
        private static string Price(UIVirtualCurrencyReroll choice)
        {
            string raw = null;
            try { raw = choice.m_ButtonText?.Text?.text; } catch { }
            string text = Currency.Caption(raw ?? "");
            if (raw != null && raw.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) >= 0) return text;
            string currency = null;
            try
            {
                foreach (var img in choice.m_Image.GetComponentsInChildren<UnityEngine.UI.Image>(false))
                {
                    string s = (img.sprite?.name ?? "").ToLowerInvariant();
                    currency = s.Contains("scrap") ? Currency.Scraps()
                        : s.Contains("powder") ? Currency.Powder()
                        : s.Contains("ore") || s.Contains("gold") || s.Contains("stone") ? Currency.Ore()
                        : s.Contains("reward") || s.Contains("rp") ? Currency.RewardPoints()
                        : null;
                    if (currency != null) break;
                }
            }
            catch { }
            return UiText.Join(text, currency);
        }
    }
}
