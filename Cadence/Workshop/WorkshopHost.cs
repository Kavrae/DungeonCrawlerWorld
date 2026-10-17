using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Bootstrap;

namespace Workshop;

/// <summary>Where a game's IDE executable says the UI's files are, and how it builds its tabs once the framework exists.</summary>
/// <param name="CreateTabs">Called once, after the framework's services exist and before the shell is built: registers the game's own element factories and returns its tabs.</param>
public sealed record WorkshopHostOptions(
    string WindowTitle,
    string FontsDirectory,
    string SpritesheetsDirectory,
    string SpriteManifestFileName,
    GameLaunchTarget GameLaunchTarget,
    Func<UiEngineContext, IReadOnlyList<IIdeTab>> CreateTabs);

/// <summary>Cadence Workshop's host loop: the framework, the shell and its tabs, in a resizable window with no simulation.</summary>
public sealed class WorkshopHost : Microsoft.Xna.Framework.Game
{
    private const int InitialWidth = 1600;
    private const int InitialHeight = 900;

    private readonly GraphicsDeviceManager _graphics;
    private readonly WorkshopHostOptions _options;

    private UiEngineContext _uiEngine = null!;
    private WorkshopShell _shell = null!;

    public WorkshopHost(WorkshopHostOptions options)
    {
        _options = options;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = InitialWidth,
            PreferredBackBufferHeight = InitialHeight,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        _uiEngine = UiEngineBootstrapper.Build(GraphicsDevice, _options.FontsDirectory, _options.SpritesheetsDirectory, _options.SpriteManifestFileName);
        var tabs = _options.CreateTabs(_uiEngine);
        _shell = new WorkshopShell(_uiEngine, tabs, new GameLauncher(_options.GameLaunchTarget), ClientSize());

        Window.Title = _options.WindowTitle;
        Window.ClientSizeChanged += OnClientSizeChanged;

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

    private void OnClientSizeChanged(object? sender, EventArgs eventArgs)
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

    private Vector2 ClientSize() => new(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
}
