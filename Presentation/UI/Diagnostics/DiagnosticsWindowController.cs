using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Diagnostics;

/// <summary>Opens and closes the Diagnostics window (F3 -- see UiInputController.HandleWindowToggleHotkeys).</summary>
/// <remarks>
/// Not a menu window, unlike Inventory or Ability Scores: menu mode pauses the simulation, and this
/// window exists to watch it run. It is marked menu-mode-exempt before it's added, so opening it
/// while a menu window is open neither blocks it nor makes it a menu window that keeps the game
/// paused after the others close (see UiLayerStack.Add).
/// </remarks>
public sealed class DiagnosticsWindowController(ElementPoolService elementPoolService, UiLayerStack layers)
{
    public DiagnosticsWindow? Window { get; private set; }

    public void ToggleDiagnosticsWindow()
    {
        if (Window is { } openWindow)
        {
            openWindow.Close();
            return;
        }

        var window = elementPoolService.CreateElement<DiagnosticsWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = DiagnosticsWindowChrome.WindowPosition,
                Size = DiagnosticsWindowChrome.WindowSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                TitleText = "Diagnostics",
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserResize = true,
                CanUserFocus = true,
                CanUserScrollVertical = true,
                ScrollbarVisibility = ScrollbarVisibility.Auto,
            },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Closed += HandleClosed;
        window.Initialize();
        layers.MarkMenuModeExempt(window);
        layers.Add(UiLayer.DynamicHud, window);
        Window = window;
    }

    private void HandleClosed(Element closedWindow)
    {
        layers.Remove(UiLayer.DynamicHud, closedWindow);
        layers.UnmarkMenuModeExempt(closedWindow);
        Window = null;
    }
}
