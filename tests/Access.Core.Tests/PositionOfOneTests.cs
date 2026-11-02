using Access.Core.Graph;
using Xunit;

// These tests set GraphAnnouncer's static wording hooks; the suite is small, so run it serially rather than
// risk another class composing a line while a hook is set.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Access.Core.Tests
{
    /// <summary>
    /// GraphAnnouncer.MinPositionCount: a lone item says no position by default (the stamp leaves it at 0), and a
    /// host that lowers the minimum to 1 hears "1 of 1" on lone focusable items (Gwent's main menu, where a list
    /// holding one entry is news), while real sibling runs read as before.
    /// </summary>
    public class PositionOfOneTests
    {
        private static NodeVtable Vt(string label) => new NodeVtable { Announcements = new[] { NodeAnnouncement.Static(label) } };

        private static GraphRender Render() => new GraphBuilder()
            .AddItem(ControlId.Structural("lone"), Vt("Lone"))
            .BeginStop()
            .AddItem(ControlId.Structural("a"), Vt("A"))
            .AddItem(ControlId.Structural("b"), Vt("B"))
            .Build();

        private static string Say(GraphRender r, string id) => GraphAnnouncer.ComposeFull(r.Nodes[ControlId.Structural(id)]);

        private static void With(int min, System.Action test)
        {
            var text = GraphAnnouncer.PositionText;
            int was = GraphAnnouncer.MinPositionCount;
            GraphAnnouncer.PositionText = (i, n) => i + " of " + n;
            GraphAnnouncer.MinPositionCount = min;
            try { test(); }
            finally { GraphAnnouncer.PositionText = text; GraphAnnouncer.MinPositionCount = was; }
        }

        [Fact]
        public void LoneItemSaysNoPositionByDefault() => With(2, () =>
        {
            var r = Render();
            Assert.Equal("Lone", Say(r, "lone"));
            Assert.Equal("B, 2 of 2", Say(r, "b"));
        });

        [Fact]
        public void LoweredMinimumSaysOneOfOne() => With(1, () =>
        {
            var r = Render();
            Assert.Equal("Lone, 1 of 1", Say(r, "lone"));
            Assert.Equal("A, 1 of 2", Say(r, "a"));
        });

        [Fact]
        public void StampLeavesLoneItemsUnpositioned()
        {
            // The kernel's stamping contract is unchanged; the "1 of 1" is the announcer's.
            var r = Render();
            Assert.Equal(0, r.Nodes[ControlId.Structural("lone")].PositionCount);
        }
    }
}
