using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// First dedicated coverage of UiLayerStack's "menu mode" contract -- see
/// IMPLEMENTATION-NOTES.md's "Pause modality" section for the full design record this backs.
/// Before this file, exactly one test anywhere in the suite touched any of this API
/// (UiInputControllerTests.RightClickTextBoxInsideMenuWindowThenClickContextMenuOption_
/// InvokesTheOption), and only incidentally.
/// </summary>
[TestClass]
public sealed class UiLayerStackTests
{
    private static ElementPoolService CreateWindowService() => TestElementPoolServiceFactory.Create(new FontService("Fonts"), new LabelRenderer());

    private static Window CreateWindow(ElementPoolService windowService) =>
        windowService.CreateElement<Window>(null, new ElementOptions());

    [TestMethod]
    public void OpenMenuWindow_MarksAsMenuWindow_AndActivatesMenuMode()
    {
        var layers = new UiLayerStack();
        var window = CreateWindow(CreateWindowService());

        layers.OpenMenuWindow(window);

        Assert.IsTrue(layers.IsMenuWindow(window));
        Assert.IsTrue(layers.IsMenuModeActive);
    }

    /// <summary>Regression guard for the exact hazard OpenMenuWindow's own doc comment calls out: a double-add (e.g. a window already promoted by Add's own auto-promotion, then explicitly opened again) must not need two CloseMenuWindow calls to fully close.</summary>
    [TestMethod]
    public void OpenMenuWindow_AlreadyOpen_IsIdempotent()
    {
        var layers = new UiLayerStack();
        var window = CreateWindow(CreateWindowService());

        layers.OpenMenuWindow(window);
        layers.OpenMenuWindow(window);
        layers.CloseMenuWindow(window);

        Assert.IsFalse(layers.IsMenuWindow(window), "A single CloseMenuWindow must fully close a window that was opened twice.");
    }

    [TestMethod]
    public void CloseMenuWindow_LastOne_DeactivatesMenuMode()
    {
        var layers = new UiLayerStack();
        var window = CreateWindow(CreateWindowService());
        layers.OpenMenuWindow(window);

        var closed = layers.CloseMenuWindow(window);

        Assert.IsTrue(closed);
        Assert.IsFalse(layers.IsMenuWindow(window));
        Assert.IsFalse(layers.IsMenuModeActive);
    }

    [TestMethod]
    public void CloseMenuWindow_NeverOpened_ReturnsFalseAndDoesNotThrow()
    {
        var layers = new UiLayerStack();
        var window = CreateWindow(CreateWindowService());

        var closed = layers.CloseMenuWindow(window);

        Assert.IsFalse(closed);
    }

    [TestMethod]
    public void MarkMenuModeExempt_ThenIsMenuModeExempt_ReturnsTrue()
    {
        var layers = new UiLayerStack();
        var element = CreateWindow(CreateWindowService());

        layers.MarkMenuModeExempt(element);

        Assert.IsTrue(layers.IsMenuModeExempt(element));
    }

    [TestMethod]
    public void IsMenuModeExempt_UnmarkedElement_ReturnsFalse()
    {
        var layers = new UiLayerStack();
        var element = CreateWindow(CreateWindowService());

        Assert.IsFalse(layers.IsMenuModeExempt(element));
    }

    [TestMethod]
    public void TopmostAndBottommostMenuWindow_NoneOpen_ReturnNull()
    {
        var layers = new UiLayerStack();

        Assert.IsNull(layers.TopmostMenuWindow);
        Assert.IsNull(layers.BottommostMenuWindow);
    }

    /// <summary>Two menu windows sharing one layer -- Topmost/Bottommost read actual draw order (last-in-list = frontmost), not open order, and RaiseToFront must be reflected immediately.</summary>
    [TestMethod]
    public void TopmostAndBottommostMenuWindow_TwoOpenOnSameLayer_ReflectDrawOrder()
    {
        var windowService = CreateWindowService();
        var layers = new UiLayerStack();
        var first = CreateWindow(windowService);
        var second = CreateWindow(windowService);
        layers.Add(UiLayer.DynamicHud, first);
        layers.Add(UiLayer.DynamicHud, second);
        layers.OpenMenuWindow(first);
        layers.OpenMenuWindow(second);

        Assert.AreSame(second, layers.TopmostMenuWindow, "Added last -- drawn frontmost.");
        Assert.AreSame(first, layers.BottommostMenuWindow, "Added first -- drawn backmost.");

        layers.RaiseToFront(first);

        Assert.AreSame(first, layers.TopmostMenuWindow, "RaiseToFront must move it to the front of its own layer's draw order.");
        Assert.AreSame(second, layers.BottommostMenuWindow);
    }

    /// <summary>A higher UiLayer's own menu window always wins Topmost, regardless of same-layer list position -- LayersDescending/LayersAscending walk tiers before positions within a tier.</summary>
    [TestMethod]
    public void TopmostMenuWindow_HigherLayerWinsOverLowerLayer()
    {
        var windowService = CreateWindowService();
        var layers = new UiLayerStack();
        var lowerLayerWindow = CreateWindow(windowService);
        var higherLayerWindow = CreateWindow(windowService);
        layers.Add(UiLayer.Base, lowerLayerWindow);
        layers.Add(UiLayer.DynamicHud, higherLayerWindow);
        layers.OpenMenuWindow(lowerLayerWindow);
        layers.OpenMenuWindow(higherLayerWindow);

        Assert.AreSame(higherLayerWindow, layers.TopmostMenuWindow);
        Assert.AreSame(lowerLayerWindow, layers.BottommostMenuWindow);
    }

    /// <summary>The exact scenario Add's own doc comment describes: opening a second notification from the (exempt) folder while menu mode is already active must make that new window part of the same interaction, not something menu mode then blocks.</summary>
    [TestMethod]
    public void Add_WhileMenuModeActive_AutoPromotesNewElementToMenuWindow()
    {
        var windowService = CreateWindowService();
        var layers = new UiLayerStack();
        var alreadyOpen = CreateWindow(windowService);
        layers.Add(UiLayer.DynamicHud, alreadyOpen);
        layers.OpenMenuWindow(alreadyOpen);

        var newlyAdded = CreateWindow(windowService);
        layers.Add(UiLayer.DynamicHud, newlyAdded);

        Assert.IsTrue(layers.IsMenuWindow(newlyAdded), "An element added while menu mode is already active must be auto-promoted into the open menu-window set.");
    }

    [TestMethod]
    public void Add_WhileMenuModeInactive_DoesNotPromoteElementToMenuWindow()
    {
        var windowService = CreateWindowService();
        var layers = new UiLayerStack();
        var element = CreateWindow(windowService);

        layers.Add(UiLayer.Base, element);

        Assert.IsFalse(layers.IsMenuWindow(element));
        Assert.IsFalse(layers.IsMenuModeActive);
    }
}
