using System;
using System.Collections.Generic;
using Access.Core.Graph;
using CrimsonUI;
using GwentUnity;
using UnityEngine;

namespace GwentAccess.Ui
{
    /// <summary>
    /// Gifting a shop product to a friend (<see cref="UIShopGiftPanel"/>), opened by a product's Gift button.
    /// The window shows the product (its details panel, with the button that buys it as a gift), a friend
    /// search box over the list of friends who can receive it, the user agreement link, and Back. The fallback
    /// read it as loose texts and its search box took no typing. Tab stops, as drawn:
    ///
    /// - Friends: the search box, an edit box with typing echo (<see cref="EditBox"/>; Enter to type), then each
    ///   friend drawn in the list, read as the list draws them (name, status); the chosen one says "selected".
    ///   Enter picks a friend, the mouse's click.
    /// - The product: its details, line by line, and its buttons, as in the shop (<see cref="ShopScreen"/>).
    /// - The user agreement link, and the bar (Back).
    ///
    /// Above the shop it opens over. Backspace is the window's Back while not typing.
    /// </summary>
    internal sealed class GiftScreen : Screen
    {
        public override string Key => "gift";
        public override int Layer => 2;

        public override string ScreenName => GameText.Get("ui_shop_gifting_btn", "Buy as a Gift");

        public override bool IsActive() => Panel() != null;

        private static UIShopGiftPanel Panel()
        {
            var p = CrimsonNodes.ActivePanel<UIShopGiftPanel>();
            if (p == null) return null;
            var v = p.Visibility;
            return v == CrimsonUIBehaviour.EVisibility.Shown || v == CrimsonUIBehaviour.EVisibility.Showing ? p : null;
        }

        public override AControl GameSelection() => null;

        // ---- the search box ----

        private static readonly EditBox _search = new EditBox(SearchField, Loc.Mark("Search"));

        private static InputFieldControl SearchField()
        {
            var f = Panel()?.m_FriendGiftingListAndSearch?.m_InputFieldControl;
            return f != null && f.gameObject.activeInHierarchy ? f : null;
        }

        public override bool LeavesToGame(KeyCode key) => _search.LeavesToGame(key);

        public override void OnCursorMoved(GraphNode node)
        {
            _search.OnCursorMoved(node);
            base.OnCursorMoved(node);
        }

        public override void Tick() => _search.Tick();

        public override void OnPop() => _search.Reset();

        // ---- the window ----

        public override void Build(GraphBuilder b)
        {
            var p = Panel();
            if (p == null) return;
            var used = new HashSet<IntPtr>();

            var friends = p.m_FriendGiftingListAndSearch;
            if (CrimsonNodes.Shown(friends))
            {
                b.BeginStop("gift:friends");
                var field = SearchField();
                if (field != null && used.Add(field.Pointer)) b.AddItem(CrimsonNodes.Id(field), _search.Vtable(field));
                var items = new List<UIFriendGiftingListItem>(friends.GetComponentsInChildren<UIFriendGiftingListItem>(false));
                items.Sort((x, y) => y.transform.position.y.CompareTo(x.transform.position.y));
                foreach (var it in items)
                {
                    var item = it;
                    if (!used.Add(item.Pointer)) continue;
                    var v = CrimsonNodes.Button(item, () => UiText.Join(CrimsonNodes.Text(item.m_NameLabel), CrimsonNodes.Text(item.m_StatusLabel)));
                    v.Announcements = new List<NodeAnnouncement>(v.Announcements)
                    {
                        new NodeAnnouncement(() => CrimsonNodes.Toggled(item) ? Loc.T("selected") : null, live: true, kind: AnnouncementKinds.Selected),
                    };
                    v.OnActivate = () => Navigator.MouseClick(item);
                    b.AddItem(CrimsonNodes.Id(item), v);
                }
                if (items.Count == 0 && field != null)
                    b.AddLabel(ControlId.Structural("gift:no-friends"), () => Loc.T("No friends listed"));
            }

            var d = p.m_GeneralDescriptionView;
            if (CrimsonNodes.Shown(d))
            {
                foreach (var c in d.GetComponentsInChildren<AControl>(true)) used.Add(c.Pointer);
                ShopScreen.AddDetails(b, d);
            }

            var rest = new List<AControl>();
            var eula = p.m_EulaLink;
            if (CrimsonNodes.Shown(eula) && used.Add(eula.Pointer)) rest.Add(eula);
            rest.AddRange(CrimsonNodes.BarButtons(p, used));
            foreach (var c in CrimsonNodes.Leaves(p, used)) rest.Add(c);
            if (rest.Count > 0)
            {
                b.BeginStop("gift:bar");
                foreach (var c in rest)
                {
                    var control = c;
                    b.AddItem(CrimsonNodes.Id(c), CrimsonNodes.Button(c, control.Pointer == eula?.Pointer
                        ? () => UiText.Join(CrimsonNodes.Text(eula.m_TitleLabel), CrimsonNodes.Text(eula.m_TextLabel)) is string s && s.Length > 0 ? s : CrimsonNodes.Label(control)
                        : null));
                }
            }
        }
    }
}
