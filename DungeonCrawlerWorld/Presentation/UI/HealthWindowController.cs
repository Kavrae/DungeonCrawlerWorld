using Game.World;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>Owns the HealthWindow: opened and closed by the Health menu command (BuiltInMenuCommands), from whichever hotkey slot holds it.</summary>
public sealed class HealthWindowController(
    ElementPoolService elementPoolService,
    World world,
    ContextMenuController contextMenuController)
{
    private WindowLifecycle<HealthWindow> _slot = null!;
    private UiLayerStack _layers = null!;

    /// <summary>Opens the Health window, or closes it if it is open -- the Health menu command.</summary>
    public void ToggleHealthWindow() => _slot.Toggle();

    public void Initialize(UiLayerStack layers)
    {
        _layers = layers;
        _slot = new WindowLifecycle<HealthWindow>(CreateHealthWindow, () => false, layers, () => { });
    }

    private HealthWindow CreateHealthWindow()
    {
        var window = elementPoolService.CreateElement<HealthWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HealthWindowChrome.WindowPosition,
                Size = HealthWindowChrome.WindowSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = "Health",
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Configure(world.PlayerEntityId);
        DynamicHudContextMenus.WireCloseContextMenu(window, contextMenuController, _layers);
        return window;
    }
}
