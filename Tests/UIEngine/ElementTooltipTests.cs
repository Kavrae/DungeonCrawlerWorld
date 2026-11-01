using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Chrome;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class ElementTooltipTests
{
    private static readonly Vector2 ScreenSize = new(1920, 1080);

    private ElementPoolService _pool = null!;
    private UiLayerStack _layers = null!;
    private UiInputController _input = null!;
    private Tooltip _tooltip = null!;

    [TestInitialize]
    public void Initialize()
    {
        _pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        _pool.RegisterFactory(() => new Tooltip(TestFonts.Shared, _pool, new UiRenderService()));
        _layers = new UiLayerStack();
        var tooltipController = new TooltipController { ScreenBoundsOverrideForTests = new Rectangle(0, 0, (int)ScreenSize.X, (int)ScreenSize.Y) };
        tooltipController.Initialize(_pool, _layers);
        _tooltip = _layers[UiLayer.Tooltip].OfType<Tooltip>().Single();
        _input = new UiInputController(_layers, ScreenSize, new PointerState(), [], [], tooltipController: tooltipController);
    }

    [TestMethod]
    public void RestingOnAButtonWithTooltipText_ShowsItOnlyOnceTheDelayHasPassed()
    {
        var button = AddButton("Inventory");

        Hover(button.Rectangle.Center, PopupChrome.HoverTooltipDelayFrames - 1);
        Assert.IsFalse(_tooltip.IsVisible);

        Hover(button.Rectangle.Center, 1);
        Assert.IsTrue(_tooltip.IsVisible);
        Assert.AreEqual("Inventory", _tooltip.OriginalText);
    }

    [TestMethod]
    public void LeavingTheButton_HidesTheTooltip()
    {
        var button = AddButton("Inventory");
        Hover(button.Rectangle.Center, PopupChrome.HoverTooltipDelayFrames);

        Hover(new Point(1000, 1000), 1);

        Assert.IsFalse(_tooltip.IsVisible);
    }

    [TestMethod]
    public void PressingTheButton_HidesTheTooltipAndRestartsTheWait()
    {
        var button = AddButton("Inventory");
        var center = button.Rectangle.Center;
        Hover(center, PopupChrome.HoverTooltipDelayFrames);

        _input.Update(new KeyboardState(), new MouseState(center.X, center.Y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        Assert.IsFalse(_tooltip.IsVisible);

        Hover(center, 1);
        Assert.IsFalse(_tooltip.IsVisible);
    }

    [TestMethod]
    public void AButtonWithNoTooltipText_ShowsNothing()
    {
        var button = AddButton(null);

        Hover(button.Rectangle.Center, PopupChrome.HoverTooltipDelayFrames * 2);

        Assert.IsFalse(_tooltip.IsVisible);
    }

    [TestMethod]
    public void ATitleButtonWithTooltipText_ShowsIt()
    {
        var window = _pool.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(300, 200), MaximumSize = ScreenSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserClose = true },
        });
        window.Initialize();
        _layers.Add(UiLayer.DynamicHud, window);
        var closeButton = window.TitleButtons[0];

        Hover(closeButton.Rectangle.Center, PopupChrome.HoverTooltipDelayFrames);

        Assert.IsTrue(_tooltip.IsVisible);
        Assert.AreEqual("Close", _tooltip.OriginalText);
    }

    [TestMethod]
    public void TheMinimizeTitleButton_NamesWhatItWillDoInEitherState()
    {
        var window = _pool.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(300, 200), MaximumSize = ScreenSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, ShowTitleWhenMinimized = true, CanUserMinimize = true },
        });
        window.Initialize();
        _layers.Add(UiLayer.DynamicHud, window);
        var minimizeButton = window.TitleButtons[0];

        Hover(minimizeButton.Rectangle.Center, PopupChrome.HoverTooltipDelayFrames);
        Assert.AreEqual("Minimize", _tooltip.OriginalText);

        window.SetDisplayMode(ElementDisplayMode.Minimized);
        Hover(minimizeButton.Rectangle.Center, 1);
        Assert.AreEqual("Restore", _tooltip.OriginalText);
    }

    private Button AddButton(string? tooltipText)
    {
        var button = _pool.CreateElement<Button>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(40, 40), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
        });
        button.Initialize();
        button.TooltipText = tooltipText;
        _layers.Add(UiLayer.DynamicHud, button);
        return button;
    }

    private void Hover(Point position, int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            _input.Update(new KeyboardState(), new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        }
    }
}
