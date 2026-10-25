using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class ToggleListTests
{
    private static (ControlTestHarness Harness, ToggleList List) BuildList(string? allLabel, params bool[] states)
    {
        var harness = new ControlTestHarness();
        harness.Pool.RegisterFactory(() => new Toggle(TestFonts.Shared, harness.Pool, new UiRenderService()));
        harness.Pool.RegisterFactory(() => new ToggleList(TestFonts.Shared, harness.Pool, new UiRenderService()));
        var items = states.Select((isOn, index) => new ToggleListItem($"Item {index}", isOn)).ToArray();
        var list = harness.AddRoot<ToggleList>(
            new Vector2(100, 100),
            new Vector2(200, ToggleList.HeightFor(items.Length, allLabel is not null)),
            list => list.Configure(items, allLabel, Color.Black, Color.Gold, Color.Gray),
            new ElementChromeOptions { ShowBorder = false, ShowTitle = false });
        harness.Frame();
        return (harness, list);
    }

    private static bool[] StatesOf(ToggleList list) => [.. Enumerable.Range(0, list.ItemCount).Select(list.IsItemOn)];

    [TestMethod]
    public void TheItems_StartAsConfigured_IndentedUnderAll()
    {
        var (_, list) = BuildList("All", true, false, true);

        CollectionAssert.AreEqual(new[] { true, false, true }, StatesOf(list));
        Assert.IsFalse(list.AllToggle!.IsOn);
        Assert.AreEqual(list.AllToggle.AbsolutePosition.X + ToggleList.ItemIndent, list.ItemToggle(0).AbsolutePosition.X, 0.5f);
        Assert.IsGreaterThan(list.AllToggle.AbsolutePosition.Y, list.ItemToggle(0).AbsolutePosition.Y);
    }

    [TestMethod]
    public void TickingAll_TicksEveryItem_AndUntickingItUnticksThem()
    {
        var (harness, list) = BuildList("All", true, false, false);
        var changes = 0;
        list.Changed += () => changes++;

        harness.Click(list.AllToggle!);
        CollectionAssert.AreEqual(new[] { true, true, true }, StatesOf(list));

        harness.Click(list.AllToggle!);
        CollectionAssert.AreEqual(new[] { false, false, false }, StatesOf(list));
        Assert.AreEqual(2, changes);
    }

    [TestMethod]
    public void TheLastItemTicked_TicksAll_AndAnyItemUnticked_UnticksIt()
    {
        var (harness, list) = BuildList("All", true, false);

        harness.Click(list.ItemToggle(1));
        Assert.IsTrue(list.AllToggle!.IsOn);

        harness.Click(list.ItemToggle(0));
        Assert.IsFalse(list.AllToggle.IsOn);
        CollectionAssert.AreEqual(new[] { false, true }, StatesOf(list));
    }

    [TestMethod]
    public void SetItemStates_ShowsThemAndAllToMatch_WithoutRaisingChanged()
    {
        var (_, list) = BuildList("All", false, false);
        var changes = 0;
        list.Changed += () => changes++;

        list.SetItemStates([true, true]);

        Assert.IsTrue(list.AllToggle!.IsOn);
        CollectionAssert.AreEqual(new[] { true, true }, StatesOf(list));
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void WithNoAllLabel_TheItemsAreNotIndented()
    {
        var (_, list) = BuildList(null, true);

        Assert.IsNull(list.AllToggle);
        Assert.AreEqual(list.ContentAbsolutePosition.X, list.ItemToggle(0).AbsolutePosition.X, 0.5f);
    }
}
