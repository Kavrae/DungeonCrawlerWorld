using DungeonCrawlerWorld.Workshop;
using Microsoft.Xna.Framework;

namespace Workshop.Tests;

[TestClass]
public sealed class TabArgumentTests
{
    private sealed class NamedTab(string title) : IIdeTab
    {
        public string Title { get; } = title;

        public void Open(WorkshopTabContext context, Rectangle area) { }

        public void Resize(Rectangle area) { }

        public void Close() { }
    }

    private static readonly IReadOnlyList<IIdeTab> Tabs = [new NamedTab("Items"), new NamedTab("Actions"), new NamedTab("Auras")];

    [TestMethod]
    public void Parse_WithNoTabArgument_IsNull()
    {
        Assert.IsNull(TabArgument.Parse(["--seed=4"]));
    }

    [TestMethod]
    public void Parse_ReadsTheNameAfterTheEqualsSign()
    {
        Assert.AreEqual("Items", TabArgument.Parse(["--seed=4", "--tab=Items"]));
    }

    [TestMethod]
    public void Select_WithNoName_KeepsEveryTabInOrder()
    {
        CollectionAssert.AreEqual(Tabs.ToList(), TabArgument.Select(Tabs, null).ToList());
    }

    [TestMethod]
    public void Select_WithAName_KeepsOnlyThatTab_IgnoringCase()
    {
        var selected = TabArgument.Select(Tabs, "actions");

        Assert.HasCount(1, selected);
        Assert.AreEqual("Actions", selected[0].Title);
    }

    [TestMethod]
    public void Select_WithAnUnknownName_KeepsNone()
    {
        Assert.IsEmpty(TabArgument.Select(Tabs, "Maps"));
    }
}
