using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDL3;
using UIEngine.Bootstrap;

namespace Workshop;

/// <summary>Where a game's IDE executable says the UI's files are, and how it builds its tabs once the framework exists.</summary>
/// <param name="CreateTabs">Called once, after the framework's services exist and before the shell is built: registers the game's own element factories and returns its tabs, with what went wrong loading their content for the Error List.</param>
public sealed record WorkshopHostOptions(
    string WindowTitle,
    string FontsDirectory,
    string SpritesheetsDirectory,
    string SpriteManifestFileName,
    GameLaunchTarget GameLaunchTarget,
    Func<UiEngineContext, WorkshopTabSet> CreateTabs)
{
    /// <summary>The IDE window's client size when it opens; the user can resize it from there.</summary>
    public Point InitialClientSize { get; init; } = new(1600, 900);

    /// <summary>Shell commands shown after Play (and Build and Play, when the launch target can build).</summary>
    public IReadOnlyList<WorkshopCommand> Commands { get; init; } = [];

    /// <summary>Where the game's captured output is appended while it runs; null keeps it in the IDE's own standard output only.</summary>
    public string? GameOutputLogPath { get; init; }
}

/// <summary>Cadence Workshop's host loop: the framework, the shell and its tabs, in a resizable window with no simulation.</summary>
/// <remarks>
/// While the user drags the window's edge, Windows runs its own resize loop and no frame runs, so on its own the last frame
/// would be stretched until the mouse is released. An SDL event watch draws from inside that loop instead: SDL raises
/// "window exposed" there and allows drawing from a watcher for it, so each one after a resize applies the new size, lays the
/// shell out and draws -- the layout follows the edge, the way a window inside the UI does.
/// </remarks>
public sealed class WorkshopHost : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly WorkshopHostOptions _options;

    /// <summary>Held so the delegate SDL calls back into is never collected while it is registered.</summary>
    private readonly SDL.SDL_EventFilter _liveResizeWatch;

    private UiEngineContext _uiEngine = null!;
    private WorkshopShell _shell = null!;
    private GameTime _lastGameTime = new();

    /// <summary>Set by the watch when the window is resized, cleared by the next ordinary frame: while set, each "exposed" event draws.</summary>
    private bool _isResizeAwaitingFrame;

    private bool _isDrawingFromWatch;

    public WorkshopHost(WorkshopHostOptions options)
    {
        _options = options;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = options.InitialClientSize.X,
            PreferredBackBufferHeight = options.InitialClientSize.Y,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        unsafe
        {
            _liveResizeWatch = WatchForLiveResize;
        }
    }

    protected override void Initialize()
    {
        _uiEngine = UiEngineBootstrapper.Build(GraphicsDevice, _options.FontsDirectory, _options.SpritesheetsDirectory, _options.SpriteManifestFileName);
        var tabSet = _options.CreateTabs(_uiEngine);
        _shell = new WorkshopShell(_uiEngine, tabSet, new GameLauncher(_options.GameLaunchTarget), _options.Commands, _options.GameOutputLogPath, ClientSize());

        Window.Title = _options.WindowTitle;
        Window.ClientSizeChanged += OnClientSizeChanged;
        SDL.SDL_AddEventWatch(_liveResizeWatch, IntPtr.Zero);

        base.Initialize();
    }

    protected override void LoadContent()
    {
        var unitRectangle = new Texture2D(GraphicsDevice, 1, 1);
        unitRectangle.SetData([Color.White]);

        _uiEngine.LoadContent(GraphicsDevice, unitRectangle);

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        _lastGameTime = gameTime;
        _isResizeAwaitingFrame = false;
        _shell.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.DimGray);

        _uiEngine.SpriteBatchRenderer.StartSpriteBatch();
        _uiEngine.ElementPoolService.ResetRenderState();
        _shell.Draw(gameTime);
        _uiEngine.SpriteBatchRenderer.EndSpriteBatch();

        base.Draw(gameTime);
    }

    private void OnClientSizeChanged(object? sender, EventArgs eventArgs) => ApplyClientSize();

    /// <summary>Sizes the back buffer to the window's client area and lays the shell out for it, when they differ.</summary>
    private void ApplyClientSize()
    {
        var clientBounds = Window.ClientBounds;
        if (clientBounds.Width <= 0 || clientBounds.Height <= 0)
        {
            return;
        }

        if (_graphics.PreferredBackBufferWidth == clientBounds.Width && _graphics.PreferredBackBufferHeight == clientBounds.Height)
        {
            return;
        }

        _graphics.PreferredBackBufferWidth = clientBounds.Width;
        _graphics.PreferredBackBufferHeight = clientBounds.Height;
        _graphics.ApplyChanges();
        _shell.Resize(ClientSize());
    }

    /// <summary>SDL's event watch: notes a resize, and draws a frame for each "exposed" event while one awaits a frame.</summary>
    /// <remarks>Called from native code, so nothing may escape it: a failure is written to standard error and the event passes on.</remarks>
    private unsafe bool WatchForLiveResize(IntPtr userdata, SDL.SDL_Event* sdlEvent)
    {
        try
        {
            var eventType = (SDL.SDL_EventType)sdlEvent->type;
            if (eventType is SDL.SDL_EventType.SDL_EVENT_WINDOW_RESIZED or SDL.SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED)
            {
                _isResizeAwaitingFrame = true;
            }
            else if (eventType == SDL.SDL_EventType.SDL_EVENT_WINDOW_EXPOSED && _isResizeAwaitingFrame && !_isDrawingFromWatch && _shell is not null)
            {
                DrawDuringResize();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Workshop: drawing while resizing failed: {exception}");
        }

        return true;
    }

    private void DrawDuringResize()
    {
        _isDrawingFromWatch = true;
        try
        {
            ApplyClientSize();
            if (BeginDraw())
            {
                Draw(_lastGameTime);
                EndDraw();
            }
        }
        finally
        {
            _isDrawingFromWatch = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        SDL.SDL_RemoveEventWatch(_liveResizeWatch, IntPtr.Zero);

        if (disposing && _shell is not null)
        {
            _shell.Dispose();
        }

        base.Dispose(disposing);
    }

    private Vector2 ClientSize() => new(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
}
