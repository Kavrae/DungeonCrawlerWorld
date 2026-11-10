using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>A second click within the window on the element that handled the first is offered as a double-click, and is a click when nothing takes it -- driven through a real UiInputController.</summary>
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
    public void AnElementTakingNoDoubleClick_GetsTheSecondClickAsAClick()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var cell = AddRoot(pool, new Vector2(10, 10), out var layers);
        var events = new List<string>();
        cell.Clicked += _ => events.Add("Clicked");
        var controller = new UiInputController(layers, new Vector2(2000, 2000), new PointerState(), [], []);

        Click(controller);
        Click(controller);

        CollectionAssert.AreEqual(new[] { "Clicked", "Clicked" }, events);
    }

    [TestMethod]
    public void AnOverride_TakesTheDoubleClickAtItsPosition_OrDeclinesItIntoAClick()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var services = TestElementServices.For(pool, TestFonts.Shared);
        pool.RegisterFactory(() => new PositionRecordingElement(services));
        var element = pool.CreateElement<PositionRecordingElement>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(40, 40), DisplayMode = ElementDisplayMode.Fixed },
        });
        element.Initialize();
        var layers = new UiLayerStack();
        layers.Add(UiLayer.DynamicHud, element);
        var controller = new UiInputController(layers, new Vector2(2000, 2000), new PointerState(), [], []);

        Click(controller);
        Click(controller);
        element.TakesDoubleClicks = false;
        Click(controller);
        Click(controller);

        CollectionAssert.AreEqual(new[] { "Click (25, 25)", "DoubleClick (25, 25)", "Click (25, 25)", "Click (25, 25)" }, element.Events);
    }

    [TestMethod]
    public void ASecondClickOnAnotherElement_IsAClickOfItsOwn()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var first = AddRoot(pool, new Vector2(10, 10), out var layers);
        var second = pool.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 10), Size = new Vector2(40, 40), DisplayMode = ElementDisplayMode.Fixed },
        });
        second.Initialize();
        layers.Add(UiLayer.DynamicHud, second);
        var events = new List<string>();
        first.Clicked += _ => events.Add("First clicked");
        second.Clicked += _ => events.Add("Second clicked");
        second.DoubleClicked += _ => events.Add("Second double-clicked");
        var controller = new UiInputController(layers, new Vector2(2000, 2000), new PointerState(), [], []);

        Click(controller);
        Click(controller, new Point(115, 25));

        CollectionAssert.AreEqual(new[] { "First clicked", "Second clicked" }, events);
    }

    private static Window AddRoot(ElementPoolService pool, Vector2 position, out UiLayerStack layers)
    {
        var element = pool.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = new Vector2(40, 40), DisplayMode = ElementDisplayMode.Fixed },
        });
        element.Initialize();
        layers = new UiLayerStack();
        layers.Add(UiLayer.DynamicHud, element);
        return element;
    }

    private static void Click(UiInputController controller, Point position)
    {
        MouseState At(ButtonState leftButton) => new(position.X, position.Y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        controller.Update(NoKeys, At(ButtonState.Released));
        controller.Update(NoKeys, At(ButtonState.Pressed));
        controller.Update(NoKeys, At(ButtonState.Released));
    }

    private sealed class PositionRecordingElement(ElementServices services) : Window(services)
    {
        public List<string> Events { get; } = [];

        public bool TakesDoubleClicks { get; set; } = true;

        protected override bool OnContentClickAction(Point mousePosition)
        {
            Events.Add($"Click ({mousePosition.X}, {mousePosition.Y})");
            return true;
        }

        protected override bool OnContentDoubleClickAction(Point mousePosition)
        {
            if (!TakesDoubleClicks)
            {
                return false;
            }

            Events.Add($"DoubleClick ({mousePosition.X}, {mousePosition.Y})");
            return true;
        }
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
