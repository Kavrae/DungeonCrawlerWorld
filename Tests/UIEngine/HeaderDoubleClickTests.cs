using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>Double-clicking a window's title bar minimizes or restores it, as its minimize/restore button does -- driven through a real UiInputController.</summary>
[TestClass]
[DoNotParallelize]
public sealed class HeaderDoubleClickTests
{
    private static readonly KeyboardState NoKeys = new();

    private static MouseState MouseAt(Point position, ButtonState leftButton) =>
        new(position.X, position.Y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static (UiInputController Controller, Window Window) CreateWindow(bool canUserMinimize)
    {
        var elementPoolService = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var window = elementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Header", CanUserMove = true, CanUserClose = true, CanUserMinimize = canUserMinimize },
        });
        window.Initialize();

        var layers = new UiLayerStack();
        layers.Add(UiLayer.Base, window);
        return (new UiInputController(layers, new Vector2(2000, 2000), new PointerState(), [], []), window);
    }

    private static Point TitleBarPoint(Window window) => new(window.HeaderRectangle.Left + 5, window.HeaderRectangle.Center.Y);

    private static Point MinimizeButtonPoint(Window window) => window.TitleButtons[^1].Rectangle.Center;

    private static void Click(UiInputController controller, Point position)
    {
        controller.Update(NoKeys, MouseAt(position, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(position, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(position, ButtonState.Released));
    }

    [TestMethod]
    public void OneClickOnTheTitleBar_ChangesNothing()
    {
        var (controller, window) = CreateWindow(canUserMinimize: true);

        Click(controller, TitleBarPoint(window));

        Assert.AreEqual(ElementDisplayMode.Fixed, window.DisplayMode);
    }

    [TestMethod]
    public void DoubleClickingTheTitleBar_Minimizes_AndDoubleClickingAgainRestores()
    {
        var (controller, window) = CreateWindow(canUserMinimize: true);

        Click(controller, TitleBarPoint(window));
        Click(controller, TitleBarPoint(window));
        Assert.AreEqual(ElementDisplayMode.Minimized, window.DisplayMode);

        Click(controller, TitleBarPoint(window));
        Click(controller, TitleBarPoint(window));
        Assert.AreEqual(ElementDisplayMode.Fixed, window.DisplayMode);
    }

    [TestMethod]
    public void DoubleClickingTheTitleBarOfAWindowThatCannotMinimize_DoesNothing()
    {
        var (controller, window) = CreateWindow(canUserMinimize: false);

        Click(controller, TitleBarPoint(window));
        Click(controller, TitleBarPoint(window));

        Assert.AreEqual(ElementDisplayMode.Fixed, window.DisplayMode);
        Assert.IsTrue(window.IsVisible);
    }

    [TestMethod]
    public void DoubleClickingTheMinimizeButton_IsTwoClicksOfIt()
    {
        var (controller, window) = CreateWindow(canUserMinimize: true);

        Click(controller, MinimizeButtonPoint(window));
        Assert.AreEqual(ElementDisplayMode.Minimized, window.DisplayMode);

        Click(controller, MinimizeButtonPoint(window));
        Assert.AreEqual(ElementDisplayMode.Fixed, window.DisplayMode);
    }

    [TestMethod]
    public void AClickOnTheTitleBarRightAfterTheMinimizeButton_IsNotADoubleClick()
    {
        var (controller, window) = CreateWindow(canUserMinimize: true);

        Click(controller, MinimizeButtonPoint(window));
        Click(controller, TitleBarPoint(window));

        Assert.AreEqual(ElementDisplayMode.Minimized, window.DisplayMode);
    }

    [TestMethod]
    public void AClickOnTheTitleBarRightAfterMovingTheWindow_IsNotADoubleClick()
    {
        var (controller, window) = CreateWindow(canUserMinimize: true);
        var pressPoint = TitleBarPoint(window);
        var releasePoint = pressPoint + new Point(60, 40);

        controller.Update(NoKeys, MouseAt(pressPoint, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(releasePoint, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(releasePoint, ButtonState.Released));
        Assert.AreEqual(new Vector2(160, 140), window.RelativePosition, "Sanity check: the drag moved the window.");

        Click(controller, TitleBarPoint(window));

        Assert.AreEqual(ElementDisplayMode.Fixed, window.DisplayMode);
    }
}
