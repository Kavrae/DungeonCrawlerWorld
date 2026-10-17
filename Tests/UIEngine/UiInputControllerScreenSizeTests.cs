using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>A host whose client area is resized (the IDE) moves the bound a top-level window is dragged within.</summary>
[TestClass]
[DoNotParallelize]
public sealed class UiInputControllerScreenSizeTests
{
    private static readonly KeyboardState NoKeys = new();

    private static MouseState MouseAt(Point position, ButtonState leftButton) =>
        new(position.X, position.Y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static (UiInputController Controller, Window Window) CreateMovableWindowAt(Vector2 relativePosition, Vector2 screenSize)
    {
        var elementPoolService = TestElementPoolServiceFactory.Create(TestFonts.Shared, new LabelRenderer());
        var window = elementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Movable", CanUserMove = true },
        });
        window.Initialize();

        var layers = new UiLayerStack();
        layers.Add(UiLayer.Base, window);
        return (new UiInputController(layers, screenSize, new PointerState(), [], []), window);
    }

    private static void DragHeaderBy(UiInputController controller, Window window, Point delta)
    {
        var pressPoint = window.HeaderRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(pressPoint + delta, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(pressPoint + delta, ButtonState.Released));
    }

    [TestMethod]
    public void DraggingAWindowPastTheRightEdge_AfterTheScreenGrows_StopsAtTheNewEdge()
    {
        var (controller, window) = CreateMovableWindowAt(new Vector2(100, 100), new Vector2(400, 400));

        controller.SetScreenSize(new Vector2(800, 400));
        DragHeaderBy(controller, window, new Point(1000, 0));

        Assert.AreEqual(800f - 200f, window.RelativePosition.X);
    }

    [TestMethod]
    public void DraggingAWindowPastTheRightEdge_AfterTheScreenShrinks_StopsAtTheNewEdge()
    {
        var (controller, window) = CreateMovableWindowAt(new Vector2(100, 100), new Vector2(800, 400));

        controller.SetScreenSize(new Vector2(500, 400));
        DragHeaderBy(controller, window, new Point(1000, 0));

        Assert.AreEqual(500f - 200f, window.RelativePosition.X);
    }
}
