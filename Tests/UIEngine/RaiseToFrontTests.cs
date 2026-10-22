using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>A press raises what it hits to the front of its parent -- except in a tiled parent, where the child list's order is the tiling order.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RaiseToFrontTests
{
    private static readonly Vector2 RowSize = new(200, 24);

    private static (Window List, List<TextWindow> Rows) BuildList(ControlTestHarness harness, ChildElementTileMode tileMode)
    {
        var list = harness.Pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = tileMode },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(50, 50), Size = new Vector2(220, 200), MaximumSize = ControlTestHarness.ScreenSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false },
        });
        list.Initialize();
        harness.Layers.Add(UiLayer.DynamicHud, list);

        var rows = new List<TextWindow>();
        for (var index = 0; index < 3; index++)
        {
            var row = harness.Pool.CreateElement<TextWindow>(list, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, index * RowSize.Y), Size = RowSize, MinimumSize = RowSize, MaximumSize = RowSize, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Text = new TextOptions { Text = $"Row {index}" },
            });
            list.AddChild(row);
            rows.Add(row);
        }

        return (list, rows);
    }

    [TestMethod]
    public void PressingARowOfAVerticalList_KeepsItWhereItIs_WhenTheListIsArrangedAgain()
    {
        var harness = new ControlTestHarness();
        var (list, rows) = BuildList(harness, ChildElementTileMode.Vertical);

        harness.Click(rows[0]);
        list.SetSize(new Vector2(220, 210));

        CollectionAssert.AreEqual(rows, list.ChildElements.ToList());
        Assert.IsLessThan(rows[1].RelativePosition.Y, rows[0].RelativePosition.Y);
    }

    [TestMethod]
    public void PressingAFloatingChild_RaisesItToTheFront()
    {
        var harness = new ControlTestHarness();
        var (list, rows) = BuildList(harness, ChildElementTileMode.Floating);

        harness.Click(rows[0]);

        Assert.AreSame(rows[0], list.ChildElements[^1]);
    }
}
