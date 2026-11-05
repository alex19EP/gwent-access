using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The shop's product preview (<see cref="UIShopProductPreviewPanel"/>), opened by a product's Preview
    /// button: a picture of the product beside its title, its description (what a bundle holds) and a further
    /// note. The fallback read it as loose texts; here the title is the screen's name, the description reads
    /// a line per item (a bundle's contents are drawn one per line), then the note, then the panel's close
    /// button. Backspace closes it as that button does.
    /// </summary>
    internal sealed class ProductPreviewScreen : Screen
    {
        public override string Key => "product-preview";
        public override int Layer => 2; // over the shop, which may share its input layer

        public override string ScreenName
        {
            get
            {
                var v = Panel()?.m_ProductPreviewView;
                string title = v != null ? Drawn(v.m_TitleText) : null;
                return title ?? GameText.Get("input_preview", "Preview");
            }
        }

        public override bool IsActive() => Panel() != null;

        private static UIShopProductPreviewPanel Panel()
        {
            var p = CrimsonNodes.ActivePanel<UIShopProductPreviewPanel>();
            if (p == null) return null;
            var vis = p.Visibility;
            return vis == CrimsonUIBehaviour.EVisibility.Shown || vis == CrimsonUIBehaviour.EVisibility.Showing ? p : null;
        }

        public override AControl GameSelection() => null;

        public override bool Back()
        {
            var close = Close(Panel());
            if (close == null) return false;
            Navigator.ClickControl(close);
            return true;
        }

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            var v = p?.m_ProductPreviewView;
            if (v == null) return;
            b.BeginStop("preview:text");
            int n = 0;
            foreach (var t in new[] { v.m_DescriptionText, v.m_AdditionalText })
            {
                string raw;
                try { raw = t != null && t.isActiveAndEnabled ? t.text : null; } catch { raw = null; }
                if (string.IsNullOrEmpty(raw)) continue;
                foreach (var line in raw.Replace("\\n", "\n").Split('\n'))
                {
                    string s = UiText.Clean(line);
                    if (s.Length == 0) continue;
                    b.AddLabel(ControlId.Structural("preview:line:" + (n++) + ":" + s), () => s);
                }
            }
            var close = Close(p);
            if (close != null)
            {
                b.BeginStop("preview:close");
                b.AddItem(CrimsonNodes.Id(close), CrimsonNodes.Button(close, () =>
                    CrimsonNodes.Texts(close) is var t && t.Count > 0 ? t[0] : GameText.Get("close", "Close")));
            }
        }

        // The panel's shown close button (it has one per input mode).
        private static ButtonControl Close(UIShopProductPreviewPanel p)
        {
            var list = p?.m_CloseButtonControls;
            for (int i = 0; list != null && i < list.Length; i++)
                if (CrimsonNodes.Shown(list[i])) return list[i];
            return null;
        }

        private static string Drawn(TMPro.TMP_Text t)
        {
            try
            {
                if (t == null || !t.isActiveAndEnabled) return null;
                string s = UiText.Clean(t.text);
                return s.Length > 0 ? s : null;
            }
            catch { return null; }
        }
    }
}
