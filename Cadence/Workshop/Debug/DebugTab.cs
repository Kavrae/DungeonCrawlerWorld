using Microsoft.Xna.Framework;
using SDL3;
using UIEngine.Bootstrap;

namespace Workshop.Debug;

/// <summary>The Debug tab: how Play launches the game for debugging (Launch Settings), what the running game reports over the diagnostics link (Live), and the reports and benchmark runs already written, opened and compared (Reports).</summary>
/// <remarks>
/// Engine-side, like the Sprites tab: it knows launch flags, gauges and frames, nothing about the game. The shell adds it
/// to every Workshop and pins its tile at the strip's right edge beside Play, since it configures and shows that launch.
/// Its windows are DebugTabView's; this adapts the shell's tab contract to it.
/// </remarks>
public sealed class DebugTab : IIdeTab
{
    public const string TabTitle = "Debug";

    private readonly UiEngineContext _uiEngine;
    private readonly WorkshopDebugging _debugging;
    private DebugTabView? _view;

    /// <summary>Registers the tab's elements with uiEngine's pool -- once per IDE run.</summary>
    public DebugTab(UiEngineContext uiEngine, WorkshopDebugging debugging)
    {
        _uiEngine = uiEngine;
        _debugging = debugging;
        DebugTabServices.RegisterElementFactories(uiEngine.ElementPoolService, uiEngine.FontService, uiEngine.RenderService);
    }

    public string Title => TabTitle;

    public bool ShowsDependents => false;

    public void Open(WorkshopTabContext context, Rectangle area)
    {
        _view ??= new DebugTabView(new DebugTabServices(_uiEngine.ElementPoolService, _uiEngine.FontService, _uiEngine.RenderService, context.Layers, context.AddInputHandler, context.Status, static text => SDL.SDL_SetClipboardText(text), context.ContextMenuController), _debugging);
        _view.Open(area);
    }

    public void Resize(Rectangle area) => _view?.Resize(area);

    public void Update(GameTime gameTime) => _view?.Update();

    public void Close() => _view?.Close();
}
