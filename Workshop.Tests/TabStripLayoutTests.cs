namespace Workshop.Tests;

[TestClass]
public sealed class TabStripLayoutTests
{
    [TestMethod]
    public void EveryTabFits_AllAreShown_WithNoOverflowTile()
    {
        Assert.AreEqual(3, TabStripLayout.VisibleTabCount([50, 50, 50], availableWidth: 150, overflowTileWidth: 30));
    }

    [TestMethod]
    public void TooManyTabs_ShowsTheLeadingOnesThatFitBesideTheOverflowTile()
    {
        Assert.AreEqual(2, TabStripLayout.VisibleTabCount([50, 50, 50], availableWidth: 140, overflowTileWidth: 30));
    }

    [TestMethod]
    public void NoRoomEvenForOne_ShowsNone()
    {
        Assert.AreEqual(0, TabStripLayout.VisibleTabCount([50, 50], availableWidth: 60, overflowTileWidth: 30));
    }
}
