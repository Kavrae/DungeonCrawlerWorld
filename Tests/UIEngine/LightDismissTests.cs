using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>
/// A press outside the topmost open light-dismissable closes it and goes no further, inside meaning what was hit, not
/// where (PLAN-unified-pointer-picking.md, Phase 4) -- driven through a real UiInputController with no game handlers.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LightDismissTests
{
    private static readonly KeyboardState NoKeys = new();

    private sealed class RecordingLightDismissable(params Element[] insideElements) : ILightDismissable
    {
        public int DismissCount { get; private set; }

        public bool IsOpen { get; set; } = true;

        public bool CountsAsInside(Element element) => insideElements.Contains(element);

        public void Dismiss()
        {
            DismissCount++;
            IsOpen = false;
        }
    }

    private sealed class RecordingRightPressHandler : IUiInputHandler
    {
        public List<Element?> PressedElements { get; } = [];

        public void OnRightPress(Element? pressedElement, Point position) => PressedElements.Add(pressedElement);
    }

    private static MouseState Mouse(Point position, ButtonState leftButton = ButtonState.Released, ButtonState rightButton = ButtonState.Released) =>
        new(position.X, position.Y, 0, leftButton, ButtonState.Released, rightButton, ButtonState.Released, ButtonState.Released);

    private static void Click(UiInputController controller, Point position)
    {
        controller.Update(NoKeys, Mouse(position));
        controller.Update(NoKeys, Mouse(position, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(position));
    }

    private static void RightClick(UiInputController controller, Point position)
    {
        controller.Update(NoKeys, Mouse(position));
        controller.Update(NoKeys, Mouse(position, rightButton: ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(position));
    }

    private static Window AddWindow(ElementPoolService pool, UiLayerStack layers, Vector2 position, Vector2 size)
    {
        var window = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, DisplayMode = ElementDisplayMode.Fixed },
        });
        window.Initialize();
        layers.Add(UiLayer.DynamicHud, window);
        return window;
    }

    private static UiInputController Controller(UiLayerStack layers, params IUiInputHandler[] inputHandlers) =>
        new(layers, new Vector2(2000, 2000), new PointerState(), inputHandlers, []);

    [TestMethod]
    public void APressOutside_DismissesIt_AndReachesNothingUnderneath()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var layers = new UiLayerStack();
        var inside = AddWindow(pool, layers, Vector2.Zero, new Vector2(100, 100));
        var underneath = AddWindow(pool, layers, new Vector2(500, 0), new Vector2(100, 100));
        var underneathClicks = 0;
        underneath.Clicked += _ => underneathClicks++;
        var lightDismissable = new RecordingLightDismissable(inside);
        layers.OpenLightDismiss(lightDismissable);

        Click(Controller(layers), underneath.Rectangle.Center);

        Assert.AreEqual(1, lightDismissable.DismissCount);
        Assert.AreEqual(0, underneathClicks);
        Assert.IsNull(layers.TopmostLightDismiss);
    }

    [TestMethod]
    public void APressOnAChildOfAnInsideElement_LeavesItOpen_AndClicksAsUsual()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var layers = new UiLayerStack();
        var inside = AddWindow(pool, layers, Vector2.Zero, new Vector2(300, 200));
        var child = pool.CreateElement<Window>(inside, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(50, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        inside.AddChild(child);
        var childClicks = 0;
        child.Clicked += _ => childClicks++;
        var lightDismissable = new RecordingLightDismissable(inside);
        layers.OpenLightDismiss(lightDismissable);

        Click(Controller(layers), child.Rectangle.Center);

        Assert.AreEqual(0, lightDismissable.DismissCount);
        Assert.AreEqual(1, childClicks);
    }

    [TestMethod]
    public void APressOnAWindowDrawnOverAnInsideOne_IsOutside()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var layers = new UiLayerStack();
        var inside = AddWindow(pool, layers, Vector2.Zero, new Vector2(300, 300));
        var covering = AddWindow(pool, layers, new Vector2(100, 100), new Vector2(100, 100));
        var lightDismissable = new RecordingLightDismissable(inside);
        layers.OpenLightDismiss(lightDismissable);

        Assert.IsTrue(inside.Rectangle.Contains(covering.Rectangle.Center), "Sanity check: the press lands within the inside window's rectangle.");
        Click(Controller(layers), covering.Rectangle.Center);

        Assert.AreEqual(1, lightDismissable.DismissCount);
    }

    [TestMethod]
    public void APressOutsideTwoOpenOnes_DismissesOnlyTheTopmost()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var layers = new UiLayerStack();
        var inside = AddWindow(pool, layers, Vector2.Zero, new Vector2(100, 100));
        var lower = new RecordingLightDismissable(inside);
        var upper = new RecordingLightDismissable(inside);
        layers.OpenLightDismiss(lower);
        layers.OpenLightDismiss(upper);

        Click(Controller(layers), new Point(1500, 1500));

        Assert.AreEqual(1, upper.DismissCount);
        Assert.AreEqual(0, lower.DismissCount);
        Assert.AreSame(lower, layers.TopmostLightDismiss);
    }

    [TestMethod]
    public void OneThatClosedWithoutSaying_IsSkipped()
    {
        var layers = new UiLayerStack();
        var lower = new RecordingLightDismissable();
        var upper = new RecordingLightDismissable();
        layers.OpenLightDismiss(lower);
        layers.OpenLightDismiss(upper);

        upper.IsOpen = false;

        Assert.AreSame(lower, layers.TopmostLightDismiss);
    }

    [TestMethod]
    public void ARightPressOutside_DismissesIt_AndReachesNothingUnderneath()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var layers = new UiLayerStack();
        var inside = AddWindow(pool, layers, Vector2.Zero, new Vector2(100, 100));
        var underneath = AddWindow(pool, layers, new Vector2(500, 0), new Vector2(100, 100));
        var underneathTaps = 0;
        underneath.OnRightClicked = _ => underneathTaps++;
        var rightPresses = new RecordingRightPressHandler();
        var lightDismissable = new RecordingLightDismissable(inside);
        layers.OpenLightDismiss(lightDismissable);

        RightClick(Controller(layers, rightPresses), underneath.Rectangle.Center);

        Assert.AreEqual(1, lightDismissable.DismissCount);
        Assert.AreEqual(0, underneathTaps);
        Assert.IsEmpty(rightPresses.PressedElements);
        Assert.IsNull(layers.TopmostLightDismiss);
    }

    [TestMethod]
    public void ARightPressInside_LeavesItOpen_AndTapsAsUsual()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var layers = new UiLayerStack();
        var inside = AddWindow(pool, layers, Vector2.Zero, new Vector2(100, 100));
        var insideTaps = 0;
        inside.OnRightClicked = _ => insideTaps++;
        var lightDismissable = new RecordingLightDismissable(inside);
        layers.OpenLightDismiss(lightDismissable);

        RightClick(Controller(layers), inside.Rectangle.Center);

        Assert.AreEqual(0, lightDismissable.DismissCount);
        Assert.AreEqual(1, insideTaps);
        Assert.AreSame(lightDismissable, layers.TopmostLightDismiss);
    }

    [TestMethod]
    public void ARightPress_IsHeardWithTheElementDrawnOnTop()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var layers = new UiLayerStack();
        AddWindow(pool, layers, Vector2.Zero, new Vector2(100, 100));
        var over = AddWindow(pool, layers, new Vector2(50, 50), new Vector2(100, 100));
        var rightPresses = new RecordingRightPressHandler();

        RightClick(Controller(layers, rightPresses), new Point(75, 75));

        Assert.HasCount(1, rightPresses.PressedElements);
        Assert.AreSame(over, rightPresses.PressedElements[0]);
    }
}
