using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentVisuals;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// The collection's full card preview (<see cref="UICardPreview"/>, run by DBPreviewState): opened from the
    /// deck builder's grid (Enter on a card), it shows the card large with its text, and the crafting bar
    /// ("Создать: -800", "В жернова: +200", "Преобразовать: -800", "Назад"). Its arrows walk the grid's cards.
    ///
    /// - Card: the card's lines, one node each, as the preview shows them: the card reader's summary (name,
    ///   power, provisions), then the categories, the ability text, each keyword explanation, the flavour text
    ///   and the artist.
    /// - Actions: the bar's buttons, each said with its currency named ("Создать: -800 Обрывки") — the game
    ///   draws the currency as an icon inside the caption. Clicked through the bar's own callbacks; crafting
    ///   and milling then ask in the game's confirm popup (<see cref="PopupScreen"/>).
    /// - Left/Right: the previous / next card, through the preview's own arrow handlers (silent at the ends,
    ///   where the game shows no arrow). The new card's first line is spoken.
    ///
    /// Only the collection's preview (the one with a crafting component) for now: the battle's preview shares
    /// the class but not the bar, and the battle screen reads cards its own way.
    /// </summary>
    internal sealed class CardPreviewScreen : Screen
    {
        public override string Key => "card-preview";
        public override int Layer => 1;
        public override bool ReturnsToOpener => false;

        public override bool IsActive() => Preview() != null;

        // The preview, when the input layer that has the input holds it and it carries the crafting component.
        private static UICardPreview Preview()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null) return null;
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var c = attached[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                var p = c.TryCast<UICardPreview>();
                if (p == null) continue;
                var crafter = p.CraftingComponent;
                return crafter != null && crafter.gameObject.activeInHierarchy ? p : null;
            }
            return null;
        }

        private static GwentGameplay.Card CurrentCard(UICardPreview p)
        {
            try { return p.CurrentCardInstance?.Card; } catch { return null; }
        }

        // The shown card's name, as the header shows it: the key its lines are built under, so a new card gives
        // new nodes.
        private static string CardKey(UICardPreview p)
        {
            var t = p.transform.Find(Details + "/Header/Layout/Name");
            return t != null ? UiText.Clean(t.GetComponent<TMPro.TMP_Text>()?.text) : "";
        }

        private const string Details = "Content/SafeArea/CardInformation/Details/CardDetails";

        public override void Build(GraphBuilder b)
        {
            var p = Preview();
            if (p == null) return;
            string key = CardKey(p);
            var lines = Lines(p);
            if (lines.Count > 0)
            {
                b.BeginStop("preview:card");
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i];
                    b.AddLabel(LineId(key, i), () => line);
                }
            }

            var buttons = BarButtons(p);
            if (buttons.Count > 0)
            {
                b.BeginStop("preview:actions");
                foreach (var c in buttons)
                {
                    var control = c;
                    b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, () => Caption(control)));
                }
            }
        }

        private static ControlId LineId(string cardKey, int i) => ControlId.Structural("preview:" + cardKey + ":" + i);

        // The card's lines: the reader's summary, then the preview's own texts in order (categories, ability,
        // keywords, flavour), the artist last.
        private static List<string> Lines(UICardPreview p)
        {
            var lines = new List<string>();
            var card = CurrentCard(p);
            if (card != null) Add(lines, CardReader.Summary(card));
            else Add(lines, CardKey(p));

            var details = p.transform.Find(Details);
            if (details != null)
                foreach (var t in details.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    string n = t.gameObject.name;
                    if (n == "Name" || n == "Value") continue; // in the summary
                    Add(lines, UiText.Clean(t.text));
                }
            var keywords = p.transform.Find("Content/SafeArea/CardInformation/Keywords");
            if (keywords != null)
                foreach (var t in keywords.GetComponentsInChildren<TMPro.TMP_Text>(false)) Add(lines, UiText.Clean(t.text));
            var artist = p.transform.Find("Content/SafeArea/CardInformation/CardHolder/CardRoot/CardArtist");
            if (artist != null && artist.gameObject.activeInHierarchy) Add(lines, UiText.Clean(artist.GetComponent<TMPro.TMP_Text>()?.text));
            return lines;
        }

        private static void Add(List<string> lines, string s)
        {
            if (!string.IsNullOrEmpty(s) && !UiText.IsPlaceholder(s) && !lines.Contains(s)) lines.Add(s);
        }

        // The bar's captioned buttons, in order: never selectable (the game reaches them by key), clicked through
        // the container's own callback. Glyph labels are key hints.
        private static List<AControl> BarButtons(UICardPreview p)
        {
            return CrimsonNodes.BarButtons(p);
        }

        private static string Caption(AControl c)
        {
            // The caption is the first text with words in it: the button's first text component is an empty one.
            foreach (var t in c.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                string s = Currency.Caption(t.text);
                if (s.Length > 0) return s;
            }
            return CrimsonNodes.Label(c);
        }

        // ---- previous / next card ----

        public override IReadOnlyList<ScreenKey> ExtraKeys => _keys;

        private static readonly ScreenKey[] _keys =
        {
            new ScreenKey { Key = KeyCode.LeftArrow, Run = () => Step(-1), Hint = "Left, previous card" },
            new ScreenKey { Key = KeyCode.RightArrow, Run = () => Step(1), Hint = "Right, next card" },
        };

        private static void Step(int dir)
        {
            var p = Preview();
            if (p == null) return;
            try
            {
                var arrow = (dir < 0 ? p.m_LeftArrow : p.m_RightArrow)?.TryCast<ButtonControl>();
                if (arrow == null || !arrow.gameObject.activeInHierarchy) return; // the first / last card
                if (dir < 0) p.HandleLeftArrowButtonPressed(arrow);
                else p.HandleRightArrowButtonPressed(arrow);
            }
            catch (Exception e) { Plugin.LogInfo("[preview] step failed: " + e.Message); }
        }

        // The card shown as last seen: when it changes (an arrow, or the game's own navigation), the cursor goes
        // to the new card's first line, which speaks it.
        private string _shown;

        public override void Tick()
        {
            var p = Preview();
            if (p == null) return;
            string key = CardKey(p);
            if (string.IsNullOrEmpty(key) || key == _shown) return;
            // The new card's nodes may not be rendered yet this frame; try again next frame until they are.
            if (_shown != null && !GraphNavigator.FocusNode(LineId(key, 0))) return;
            _shown = key;
        }

        public override void OnPop() => _shown = null;
    }
}
