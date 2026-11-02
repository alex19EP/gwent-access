using Access.Core.Graph;
using Xunit;

namespace Access.Core.Tests
{
    /// <summary>
    /// An item that says its own place. A quest row (the quest, then its Change Quest button) or a journey step
    /// (the Standard reward, then the Premium one) is a row of two, so the kernel stamps each item "1 of 2" or
    /// "2 of 2". The screen wants "2 of 3" among the quests instead, and nothing on the button beside it. A
    /// Position part replaces the stamp, and <see cref="NodeVtable.SpeaksOwnPosition"/> drops it.
    /// </summary>
    public class OwnPositionTests
    {
        private static GraphRender Rows(bool own) => new GraphBuilder()
            .BeginStop()
            .StartRow("q").AddItem(ControlId.Structural("quest1"), Quest("First", "1 of 2", own)).AddItem(ControlId.Structural("button1"), Button(own)).EndRow()
            .StartRow("q").AddItem(ControlId.Structural("quest2"), Quest("Second", "2 of 2", own)).AddItem(ControlId.Structural("button2"), Button(own)).EndRow()
            .Build();

        private static NodeVtable Quest(string text, string position, bool own) => new NodeVtable
        {
            Announcements = own
                ? new[] { NodeAnnouncement.Static(text), new NodeAnnouncement(() => position, kind: AnnouncementKinds.Position) }
                : new[] { NodeAnnouncement.Static(text) },
            SpeaksOwnPosition = own,
        };

        private static NodeVtable Button(bool own) => new NodeVtable
        {
            Announcements = new[] { NodeAnnouncement.Static("Change Quest") },
            SpeaksOwnPosition = own,
        };

        private static string Say(GraphRender r, string id) => GraphAnnouncer.ComposeFull(r.Nodes[ControlId.Structural(id)]);

        private static void With(System.Action test)
        {
            var text = GraphAnnouncer.PositionText;
            GraphAnnouncer.PositionText = (i, n) => i + " of " + n;
            try { test(); }
            finally { GraphAnnouncer.PositionText = text; }
        }

        [Fact]
        public void RowItemsAreStampedByTheirRow() => With(() =>
        {
            var r = Rows(own: false);
            Assert.Equal("Second, 1 of 2", Say(r, "quest2"));
            Assert.Equal("Change Quest, 2 of 2", Say(r, "button2"));
        });

        [Fact]
        public void PositionPartReplacesTheRowStamp() => With(() =>
        {
            var r = Rows(own: true);
            Assert.Equal("Second, 2 of 2", Say(r, "quest2"));
            Assert.Equal("First, 1 of 2", Say(r, "quest1"));
        });

        [Fact]
        public void SpeaksOwnPositionDropsTheStamp() => With(() =>
        {
            var r = Rows(own: true);
            Assert.Equal("Change Quest", Say(r, "button1"));
        });
    }
}
