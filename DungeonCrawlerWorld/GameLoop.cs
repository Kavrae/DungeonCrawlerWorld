using Engine.Diagnostics;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Settings;
using Engine.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Bootstrap;

namespace DungeonCrawlerWorld;

/// <summary>The main game loop for the Dungeon Crawler World.</summary>
/// <remarks>This class should be kept as minimal as possible.</remarks>
/// TODO : Many of these hard coded values should live in a configuration file.
public sealed class GameLoop : Microsoft.Xna.Framework.Game
{
    // Entity capacity is sized for the default 3x3-neighborhood (3072x3072) test map across all three
    // MapLayers -- ~660k NPC entities from TestMapBuilder. Component capacity is only where a pool's
    // dense storage starts; pools grow geometrically, so most start small.
    internal const int InitialEntityCapacity = 720_000;
    internal const int InitialComponentCapacity = 1_024;

    // Floor 1 of (eventually) 18 -- floors are strictly sequential, no skipping or
    // backtracking. There's no advance trigger yet (that needs a win-condition system that
    // doesn't exist), so this stays a constant rather than tracked state until something
    // actually needs to change it.
    internal const int FloorNumber = 1;

    // Range for CrawlerComponent.CrawlerNumber: 1 to 2^24 (16,777,216), rounded up from the source
    // material's 13,000,000 so the allocator's permutation covers the range exactly.
    internal const int MinCrawlerNumber = 1;
    internal const int CrawlerNumberBits = 24;

    private readonly GraphicsDeviceManager _graphics;

    private WorldSessionContext _worldSession = null!;
    private PresentationContext _presentation = null!;
    private ShellContext _shell = null!;
    private readonly DiagnosticsEngine _diagnostics;
    private int _frameCount;

    /// <summary>The seed this session's simulation was generated from.</summary>
    /// <remarks>Used for map layout, blueprint rolls, damage variance, crits, everything drawing on the shared MathUtility.</remarks>
    private readonly int _randomSeed;

    private readonly int? _mapSizeOverride;

    private readonly IReadOnlyList<ISettingsSource> _settingsSources;

    /// <summary>FNA/SDL's own default window title is empty at the point Initialize() runs (nothing else in this codebase sets one), so the OS title bar is set explicitly here rather than captured. See _lastAdminModeOn.</summary>
    private const string BaseWindowTitle = "Dungeon Crawler World";

    /// <summary>Mirrors GlobalState.IsAdminModeOn as of the last frame Window.Title was synced -- Window.Title is only ever written on an actual change, not every frame.</summary>
    private bool _lastAdminModeOn;

    /// <param name="settingsSources">Where setting overrides come from, in order -- Program.cs passes the command line.</param>
    /// <param name="diagnosticsFeatures">Which Diagnostics engine features to enable -- opt-in, defaults to None. See DiagnosticsFeaturesParser (Program.cs passes --diagnostics= here).</param>
    /// <param name="randomSeed">Seed for the shared MathUtility every system and blueprint draws from -- see RandomSeed. Defaults to a generated one so a caller that doesn't care (tests constructing a GameLoop directly) still gets a reproducible, reportable session rather than an unseeded one.</param>
    /// <param name="benchmarkFrameRange">Simulation frames to benchmark, or null -- see FrameRangeBenchmark (Program.cs passes --benchmark-frames= here).</param>
    /// <param name="mapSizeOverride">Square map width and height, or null for the default -- see MapSizeArgument (Program.cs passes --map-size= here).</param>
    public GameLoop(IReadOnlyList<ISettingsSource> settingsSources, DiagnosticsFeatures diagnosticsFeatures = DiagnosticsFeatures.None, int? randomSeed = null, BenchmarkFrameRange? benchmarkFrameRange = null, int? mapSizeOverride = null)
    {
        _randomSeed = randomSeed ?? RandomSeed.Generate();
        _mapSizeOverride = mapSizeOverride;
        _settingsSources = settingsSources;

        // Constructed and started here, not in Initialize(), so its FrameBudget/Startup trackers'
        // clocks -- and the startup scopes it records, from Initialize's own down to each module's
        // build phases -- start as close to process start as this class can observe; Initialize()
        // itself is one of the things being timed. Memory/LeakDetection attach when the session
        // begins.
        _diagnostics = new DiagnosticsEngine(diagnosticsFeatures, _randomSeed, benchmarkFrameRange);
        _diagnostics.Start();

        //TODO : Make this configurable as a set size OR full screen calculation.
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        var modsDirectory = Path.Combine(AppContext.BaseDirectory, "Mods");
        var playerActivityLogFilePath = Path.Combine(FindProjectRoot(), "Log", "player-activity.log");
        using (EngineHooks.DiagnosticScope("World Session Setup"))
        {
            _worldSession = WorldSessionBootstrapper.Build(FloorNumber, modsDirectory, InitialEntityCapacity, InitialComponentCapacity, MinCrawlerNumber, CrawlerNumberBits, playerActivityLogFilePath, _randomSeed, _settingsSources, _mapSizeOverride);
        }

        using (EngineHooks.DiagnosticScope("Presentation Bootstrap"))
        {
            _presentation = PresentationBootstrapper.Build(GraphicsDevice, "Fonts", "Spritesheets");
        }

        var screenSize = new Vector2(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
        using (EngineHooks.DiagnosticScope("Window/Shell Setup"))
        {
            _shell = ShellBootstrapper.Build(_presentation, _worldSession, screenSize, _diagnostics);
        }

        Window.Title = TitleWithSeed();

        base.Initialize();
    }

    protected override void LoadContent()
    {
        // Local, not a field -- nothing outside this method reads it anymore. _presentation and
        // _shell each cache their own copy (see PresentationContext.LoadContent/
        // ShellContext.LoadContent) rather than needing it passed into Update/Draw.
        var unitRectangle = new Texture2D(GraphicsDevice, 1, 1);
        unitRectangle.SetData([Color.White]);

        _presentation.LoadContent(GraphicsDevice, unitRectangle);

        _shell.LoadContent(GraphicsDevice, _presentation.SpriteBatchRenderer.GetSpriteBatch(), unitRectangle);

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        _shell.PreSimulationUpdate();

        if (!(_shell.MapWindow.IsPaused || _shell.Layers.IsMenuModeActive))
        {
            _frameCount++;
            _worldSession.EcsContext.Update(new EngineTime(gameTime.TotalGameTime, gameTime.ElapsedGameTime, gameTime.IsRunningSlowly, _frameCount));
        }

        using (EngineHooks.FrameCost(FrameCostCategory.Update, "GameLoop", "Shell.Update"))
        {
            _shell.Update(gameTime);
        }

        SyncAdminModeWindowTitle();

        base.Update(gameTime);
    }

    /// <summary>Runs after _shell.Update (UiInputController's own Update, where F12 is handled) so this frame's toggle is already reflected -- only writes Window.Title on an actual change, not every frame.</summary>
    private void SyncAdminModeWindowTitle()
    {
        if (GlobalState.IsAdminModeOn == _lastAdminModeOn)
        {
            return;
        }

        _lastAdminModeOn = GlobalState.IsAdminModeOn;
        Window.Title = _lastAdminModeOn ? $"{TitleWithSeed()} - ADMIN" : TitleWithSeed();
    }

    /// <summary>The window title with this session's seed appended -- see _randomSeed for why it is shown rather than only logged.</summary>
    private string TitleWithSeed() => $"{BaseWindowTitle} [seed {_randomSeed}]";

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.LightGray);

        _presentation.SpriteBatchRenderer.StartSpriteBatch();
        _presentation.ElementPoolService.ResetRenderState();

        using (EngineHooks.FrameCost(FrameCostCategory.Draw, "GameLoop", "Shell.Draw"))
        {
            _shell.Draw(gameTime);
        }

        using (EngineHooks.FrameCost(FrameCostCategory.Draw, "GameLoop", "SpriteBatch.End"))
        {
            _presentation.SpriteBatchRenderer.EndSpriteBatch();
        }

        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _worldSession?.Dispose();
            _diagnostics.Dispose();
        }

        base.Dispose(disposing);
    }

    internal static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DungeonCrawlerWorld.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
