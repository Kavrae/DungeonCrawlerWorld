using Engine.Diagnostics;
using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace UIEngine.UI;

/// <summary>Runs a UiLayerStack's windows each frame: updates and draws every layer bottom to top.</summary>
/// <remarks>
/// While menu mode is active, a dim quad is drawn immediately beneath Layers.BottommostMenuWindow, and every element
/// UiLayerStack.IsMenuModeExempt opted in is pulled out of its ordinary draw slot and redrawn just above that quad --
/// they stay reachable for input (UiInputController.TryHitTestInteraction), so they must read as usable too. Each
/// window's update and draw is a frame-cost row named after its layer and type, timed only while something listens. The
/// dim is drawn through the UiRenderService every Element draws with.
/// </remarks>
public sealed class UiLayerHost(UiLayerStack layers, UiRenderService renderService)
{
    private const string MenuModeExemptRowName = "MenuModeExempt";

    /// <summary>Per-Draw scratch state for the dim pass, reset at the top of every Draw and read by DrawWindowLayer across that call's layer loop.</summary>
    private Element? _bottommostMenuWindow;

    /// <summary>The exempt elements in ascending draw order, refilled each Draw while menu mode is active and empty otherwise.</summary>
    private readonly List<Element> _menuModeExemptElements = [];

    private bool _dimDrawn;

    /// <summary>A layer's windows copied before updating them, since a window's update can open or close windows in the same layer; reused every frame.</summary>
    private readonly List<Element> _updateSnapshot = [];

    /// <summary>Each window type's frame-cost row per category and layer (MenuModeExempt draws keyed under a null layer), issued the first time it is measured.</summary>
    private readonly Dictionary<(FrameCostCategory Category, UiLayer? Layer, Type WindowType), FrameCostRowId> _frameCostRows = [];

    public void Update(GameTime gameTime)
    {
        foreach (var layer in UiLayerStack.LayersAscending())
        {
            UpdateWindowLayer(layer, gameTime);
        }
    }

    public void Draw(GameTime gameTime)
    {
        _bottommostMenuWindow = layers.BottommostMenuWindow;
        _dimDrawn = _bottommostMenuWindow is null;
        FindMenuModeExemptElements();

        foreach (var layer in UiLayerStack.LayersAscending())
        {
            DrawWindowLayer(layer, gameTime);
        }
    }

    /// <summary>Fills _menuModeExemptElements with every element UiLayerStack.IsMenuModeExempt currently considers exempt, across every layer, in ascending draw order -- none while menu mode is inactive.</summary>
    private void FindMenuModeExemptElements()
    {
        _menuModeExemptElements.Clear();
        if (_bottommostMenuWindow is null)
        {
            return;
        }

        foreach (var layer in UiLayerStack.LayersAscending())
        {
            var elements = layers[layer];
            for (var index = 0; index < elements.Count; index++)
            {
                if (layers.IsMenuModeExempt(elements[index]))
                {
                    _menuModeExemptElements.Add(elements[index]);
                }
            }
        }
    }

    private void UpdateWindowLayer(UiLayer layer, GameTime gameTime)
    {
        _updateSnapshot.Clear();
        _updateSnapshot.AddRange(layers[layer]);

        var frameCostRecorder = EngineHooks.FrameCosts.Listener;
        for (var index = 0; index < _updateSnapshot.Count; index++)
        {
            var window = _updateSnapshot[index];
            if (frameCostRecorder is null)
            {
                window.Update(gameTime);
                continue;
            }

            using (EngineHooks.FrameCost(FrameCostRowOf(FrameCostCategory.Update, layer, window)))
            {
                window.Update(gameTime);
            }
        }

        _updateSnapshot.Clear();
    }

    /// <summary>Draws one layer's windows, skipping menu-mode-exempt ones in their ordinary slot and drawing them once, just above the dim, when the loop reaches the bottommost menu window.</summary>
    private void DrawWindowLayer(UiLayer layer, GameTime gameTime)
    {
        var isMenuModeActive = _bottommostMenuWindow is not null;
        var windows = layers[layer];
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            if (isMenuModeActive && layers.IsMenuModeExempt(window))
            {
                continue;
            }

            if (!_dimDrawn && window == _bottommostMenuWindow)
            {
                MenuModeDimRenderer.Draw(renderService.Batch, renderService.GraphicsDevice);
                _dimDrawn = true;

                for (var exemptIndex = 0; exemptIndex < _menuModeExemptElements.Count; exemptIndex++)
                {
                    DrawWindow(_menuModeExemptElements[exemptIndex], gameTime, null);
                }
            }

            DrawWindow(window, gameTime, layer);
        }
    }

    /// <param name="layer">The layer whose row it is drawn under; null for a menu-mode-exempt element drawn above the dim.</param>
    private void DrawWindow(Element window, GameTime gameTime, UiLayer? layer)
    {
        if (EngineHooks.FrameCosts.Listener is null)
        {
            window.Draw(gameTime);
            return;
        }

        using (EngineHooks.FrameCost(FrameCostRowOf(FrameCostCategory.Draw, layer, window)))
        {
            window.Draw(gameTime);
        }
    }

    private FrameCostRowId FrameCostRowOf(FrameCostCategory category, UiLayer? layer, Element window)
    {
        var windowType = window.GetType();
        var key = (category, layer, windowType);
        if (!_frameCostRows.TryGetValue(key, out var row))
        {
            row = FrameCostRowIds.Of(category, layer?.ToString() ?? MenuModeExemptRowName, windowType.Name);
            _frameCostRows.Add(key, row);
        }

        return row;
    }
}
