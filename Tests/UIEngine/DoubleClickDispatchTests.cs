using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>An element listening for double-clicks gets its first click at once, and a second within the window as DoubleClicked in its place -- driven through a real UiInputController.</summary>
[TestClass]
[DoNotParallelize]
public sealed class DoubleClickDispatchTests
{
    private static readonly KeyboardState NoKeys = new();
    private static readonly Point CellPosition = new(25, 25);

    private sealed record Harness(UiInputController Controller, List<string> Events);

    private static Harness Build()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var cell = pool.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(40, 40), DisplayMode = ElementDisplayMode.Fixed },
        });
        cell.Initialize();
        var events = new List<string>();
        cell.Clicked += _ => events.Add("Clicked");
        cell.DoubleClicked += _ => events.Add("DoubleClicked");

        var layers = new UiLayerStack();
        layers.Add(UiLayer.DynamicHud, cell);
        return new Harness(new UiInputController(layers, new Vector2(2000, 2000), new PointerState(), [], []), events);
    }

    private static MouseState Mouse(ButtonState leftButton = ButtonState.Released) =>
        new(CellPosition.X, CellPosition.Y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static void Click(UiInputController controller)
    {
        controller.Update(NoKeys, Mouse());
        controller.Update(NoKeys, Mouse(ButtonState.Pressed));
        controller.Update(NoKeys, Mouse());
    }

    [TestMethod]
    public void FirstClick_RaisesClickedAtOnce()
    {
        var harness = Build();

        Click(harness.Controller);

        CollectionAssert.AreEqual(new[] { "Clicked" }, harness.Events);
    }

    [TestMethod]
    public void SecondClickWithinTheWindow_RaisesDoubleClickedInPlaceOfClicked()
    {
        var harness = Build();

        Click(harness.Controller);
        Click(harness.Controller);

        CollectionAssert.AreEqual(new[] { "Clicked", "DoubleClicked" }, harness.Events);
    }

    [TestMethod]
    public void SecondClickAfterTheWindow_IsAClickOfItsOwn()
    {
        var harness = Build();

        Click(harness.Controller);
        for (var frame = 0; frame <= UiInputController.DoubleClickWindowFrames; frame++)
        {
            harness.Controller.Update(NoKeys, Mouse());
        }

        Click(harness.Controller);

        CollectionAssert.AreEqual(new[] { "Clicked", "Clicked" }, harness.Events);
    }

    [TestMethod]
    public void ThirdQuickClick_StartsANewPair()
    {
        var harness = Build();

        Click(harness.Controller);
        Click(harness.Controller);
        Click(harness.Controller);

        CollectionAssert.AreEqual(new[] { "Clicked", "DoubleClicked", "Clicked" }, harness.Events);
    }

    [TestMethod]
    public void ACellPooledWithItsParentsChildrenAndReusedBetweenTheClicks_TakesTheSecondAsAClickOfItsOwn()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var host = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(200, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        host.ContentPadding = Vector2.Zero;
        host.Initialize();
        var events = new List<string>();
        Window AddCell()
        {
            var cell = pool.CreateElement<Window>(host, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(40, 40), DisplayMode = ElementDisplayMode.Fixed },
            });
            host.AddChild(cell);
            cell.Clicked += _ => events.Add("Clicked");
            cell.DoubleClicked += _ => events.Add("DoubleClicked");
            return cell;
        }

        var firstCell = AddCell();
        var layers = new UiLayerStack();
        layers.Add(UiLayer.DynamicHud, host);
        var controller = new UiInputController(layers, new Vector2(2000, 2000), new PointerState(), [], []);

        Click(controller);
        pool.CloseAllChildren(host);
        var reusedCell = AddCell();
        Assert.AreSame(firstCell, reusedCell, "Sanity check: the pool hands the same instance back.");
        Click(controller);

        CollectionAssert.AreEqual(new[] { "Clicked", "Clicked" }, events);
    }
}
