using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using GwentUnity.MainMenu;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Thronebreaker's main menu: the card carousel (Сетевая игра, the Новая игра stack…), or, once a stack is
    /// opened, the cards of the deepest opened one (Новая игра → the difficulties → a difficulty's save slots)
    /// and the stack panel's own bar (confirm, back); then the menu bar
    /// (settings, quit). One Tab stop each.
    ///
    /// The engine is Gwent's, but the carousel is not under the menu's content panel: it lives in its own
    /// render-to-texture canvas, so the panel's attached-control tree never reaches it. The panel keeps
    /// references to every part (m_Carousel, m_StackPanel, m_ControlsSection), and the screen walks those.
    /// Each card reads its title and subtitle; Space adds its description; Enter clicks its button, as a mouse
    /// click on the card does.
    /// </summary>
    internal sealed class MainMenuScreen : Screen
    {
        public override string Key => "main-menu";
        public override int Layer => 1;

        public override bool IsActive()
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            if (layer == null || layer.Priority != CrimsonInputLayer.EPriority.Base) return false;
            return ContentPanel(layer) != null;
        }

        private static UIMainMenuContentPanel ContentPanel(CrimsonInputLayer layer)
        {
            var attached = layer.GetAttachedControls();
            for (int i = 0; i < attached.Count; i++)
            {
                var p = attached[i]?.TryCast<UIMainMenuContentPanel>();
                if (p != null && p.gameObject.activeInHierarchy) return p;
            }
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var layer = CrimsonInputManager.Instance?.ActiveLayer;
            var panel = layer != null ? ContentPanel(layer) : null;
            if (panel == null) return;

            var carousel = panel.m_Carousel;
            var stack = Innermost(carousel?.m_ExpandedStack);
            if (stack != null && stack.m_IsExpanded) AddStack(b, stack, panel.m_StackPanel);
            else if (carousel != null) AddCarousel(b, carousel);

            var bar = panel.m_ControlsSection;
            if (bar != null && bar.gameObject.activeInHierarchy)
            {
                var controls = WithBarButtons(bar, CrimsonNodes.Leaves(bar));
                if (controls.Count > 0)
                {
                    b.BeginStop("menubar");
                    foreach (var c in controls) b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
                }
            }
        }

        // The carousel's cards and stacks, in its order.
        private static void AddCarousel(GraphBuilder b, UIMainMenuCardsCarousel carousel)
        {
            var items = new List<UIMainMenuBaseItem>();
            var children = carousel.GetChildren();
            for (int i = 0; children != null && i < children.Count; i++)
            {
                var item = children[i]?.TryCast<UIMainMenuBaseItem>();
                if (item != null && item.gameObject.activeInHierarchy) items.Add(item);
            }
            if (items.Count == 0) return;
            b.BeginStop("cards");
            foreach (var item in items) AddCard(b, item);
        }

        /// <summary>Stacks nest: Новая игра opens the difficulties, and a difficulty opens its save slots, the
        /// opened stack inside the carousel's. The deepest opened one is what the player is choosing from.</summary>
        private static UIMainMenuCardStack Innermost(UIMainMenuCardStack stack)
        {
            for (int depth = 0; stack != null && depth < 8; depth++)
            {
                UIMainMenuCardStack inner = null;
                var items = stack.m_Items;
                for (int i = 0; items != null && i < items.Count; i++)
                {
                    var s = items[i]?.TryCast<UIMainMenuCardStack>();
                    if (s != null && s.gameObject.activeInHierarchy && s.m_IsExpanded) { inner = s; break; }
                }
                if (inner == null) break;
                stack = inner;
            }
            return stack;
        }

        /// <summary>An opened stack: its cards (the stack's items, front first as the game orders them), then the
        /// stack panel's message, if it shows one, and its bar (confirm, back).</summary>
        private static void AddStack(GraphBuilder b, UIMainMenuCardStack stack, UIMainMenuCardStackPanel stackPanel)
        {
            b.BeginStop("stack");
            string title = Joined(stack.m_TitleLabel);
            if (title.Length == 0) title = HiddenTitle(stack); // an opened inner stack hides its own header
            if (title.Length > 0) b.PushContext(title);
            var items = stack.m_Items;
            for (int i = 0; items != null && i < items.Count; i++)
            {
                var item = items[i];
                if (item != null && item.gameObject.activeInHierarchy) AddCard(b, item);
            }
            if (stackPanel != null && stackPanel.gameObject.activeInHierarchy)
            {
                string message = string.Join(" ", new[] { Joined(stackPanel.m_MessageTitle), Joined(stackPanel.m_MessageDescription) }).Trim();
                if (message.Length > 0)
                    b.AddItem(ControlId.Structural("main-menu:stack-message:" + message), new NodeVtable
                    {
                        Announcements = new[] { new NodeAnnouncement(() => message, kind: AnnouncementKinds.Label) },
                        OnTooltip = () => Speech.Say(message, true),
                    });
                var bar = stackPanel.m_InputButtonContainer;
                if (bar != null)
                    foreach (var c in WithBarButtons(bar, new List<AControl>()))
                        b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c));
            }
            if (title.Length > 0) b.PopContext();
        }

        // A card or stack: its button, read as title and subtitle; Space adds the description.
        private static void AddCard(GraphBuilder b, UIMainMenuBaseItem item)
        {
            var button = item.m_Button;
            if (button == null || !button.gameObject.activeInHierarchy) return;
            var v = CrimsonNodes.Button(button, () => CardLabel(item));
            v.OnTooltip = () => Speech.Say(CardDetails(item), true);
            b.AddItem(CrimsonNodes.Id(button), v);
        }

        private static string CardLabel(UIMainMenuBaseItem item)
        {
            var parts = new List<string>();
            Add(parts, Joined(item.m_TitleLabel));
            Add(parts, Joined(item.m_SubTitleLabel));
            if (parts.Count == 0) foreach (var s in CrimsonNodes.Texts(item)) Add(parts, s);
            if (parts.Count == 0) Add(parts, HiddenTitle(item)); // an empty save slot: its title is "Новая игра"
            return string.Join(", ", parts);
        }

        /// <summary>The card's title text even while the game hides it: an empty save slot keeps "Новая игра" in
        /// its hidden header, and an opened inner stack hides its difficulty name.</summary>
        private static string HiddenTitle(UIMainMenuBaseItem item)
        {
            var label = item.m_TitleLabel;
            if (label == null) return "";
            foreach (var t in label.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                string s = UiText.Clean(t.text);
                if (s.Length > 0) return s;
            }
            return "";
        }

        private static string CardDetails(UIMainMenuBaseItem item)
        {
            var parts = new List<string> { CardLabel(item) };
            Add(parts, Joined(item.m_DescriptionLabel));
            return string.Join(". ", parts);
        }

        private static void Add(List<string> parts, string s)
        {
            if (!string.IsNullOrEmpty(s) && !parts.Contains(s)) parts.Add(s);
        }

        private static string Joined(UnityEngine.Component c)
        {
            if (c == null || !c.gameObject.activeInHierarchy) return "";
            return string.Join(" ", CrimsonNodes.Texts(c));
        }

        // Adds the captioned buttons of the button bars under root (never selectable: the game reaches them by
        // key glyph; clicked through the bar's callback).
        private static List<AControl> WithBarButtons(UnityEngine.Component root, List<AControl> list)
        {
            foreach (var bar in root.GetComponentsInChildren<InputButtonContainer>(false))
            {
                var children = bar.GetChildren();
                for (int i = 0; children != null && i < children.Count; i++)
                {
                    var c = children[i];
                    if (c == null || !c.gameObject.activeInHierarchy || c.TryCast<ButtonControl>() == null) continue;
                    if (CrimsonNodes.Texts(c).Count == 0) continue;
                    if (!list.Exists(x => x.Pointer == c.Pointer)) list.Add(c);
                }
            }
            return list;
        }
    }
}
