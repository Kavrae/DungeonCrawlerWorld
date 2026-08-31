using Engine.Diagnostics;
using Engine.ECS.Systems;
using Engine.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Bootstrap;
using System.Diagnostics;

namespace DungeonCrawlerWorld;

public sealed class GameLoop : Microsoft.Xna.Framework.Game
{
    // Sized for the 1000x1000 test map across all three MapLayers: Ground (~1.06M terrain/
    // wall entities, ~49k GoblinEngineers plus a denser ~108k-entity secondary plain-Goblin
    // population), UnderGround (~1M terrain entities plus ~4k border walls), and Flying
    // (~21k scattered Fairies) from TestMapBuilder -- rather than left at a small default and
    // grown via doubling. EntityManager/ComponentManager both grow automatically on demand,
    // but at this scale that's dozens of full-array reallocate-and-copy passes during
    // Populate instead of (close to) none.
    private const int InitialEntityCapacity = 2_600_000;
    private const int InitialComponentCapacity = 220_000;

    // Floor 1 of (eventually) 18 -- floors are strictly sequential, no skipping or
    // backtracking. There's no advance trigger yet (that needs a win-condition system that
    // doesn't exist), so this stays a constant rather than tracked state until something
    // actually needs to change it.
    private const int FloorNumber = 1;

    // Range for CrawlerComponent.CrawlerNumber -- GameLoop's choice, not UniqueNumberAllocator's
    // own (a generic Engine.Math utility), since that range is Crawler-specific.
    private const int MinCrawlerNumber = 1;
    private const int MaxCrawlerNumber = 13_000_000;

    private readonly GraphicsDeviceManager _graphics;

    private WorldSessionContext _worldSession = null!;
    private PresentationContext _presentation = null!;
    private ShellContext _shell = null!;
    private readonly DiagnosticsEngine _diagnostics;
    private int _frameCount;

    /// <summary>FNA/SDL's own default window title is empty at the point Initialize() runs (nothing else in this codebase sets one), so the OS title bar is set explicitly here rather than captured. See _lastAdminModeOn.</summary>
    private const string BaseWindowTitle = "Dungeon Crawler World";

    /// <summary>Mirrors GlobalState.IsAdminModeOn as of the last frame Window.Title was synced -- Window.Title is only ever written on an actual change, not every frame.</summary>
    private bool _lastAdminModeOn;

    /// <param name="diagnosticsFeatures">Which Diagnostics engine features to enable -- opt-in, defaults to None. See DiagnosticsFeaturesParser (Program.cs passes --diagnostics= here).</param>
    public GameLoop(DiagnosticsFeatures diagnosticsFeatures = DiagnosticsFeatures.None)
    {
        // Constructed here, not in Initialize(), so its FrameBudget/Startup trackers' clocks
        // (and Startup's Phase("Module Load") wrap around WorldSessionBootstrapper.Build below)
        // start as close to process start as this class can observe -- Initialize() itself is
        // one of the things being timed. Memory/LeakDetection can't start this early (they need
        // ComponentManager/EntityManager, which don't exist yet) -- see AttachEcsContext, called
        // from within WorldSessionBootstrapper.Build.
        _diagnostics = new DiagnosticsEngine(diagnosticsFeatures);

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
        using (_diagnostics.StartupProfiler?.Phase("World Session Setup"))
        {
            _worldSession = WorldSessionBootstrapper.Build(FloorNumber, modsDirectory, InitialEntityCapacity, InitialComponentCapacity, MinCrawlerNumber, MaxCrawlerNumber, playerActivityLogFilePath, _diagnostics);
        }

        using (_diagnostics.StartupProfiler?.Phase("Presentation Bootstrap"))
        {
            _presentation = PresentationBootstrapper.Build(GraphicsDevice, "Fonts", "Spritesheets");
        }

        var screenSize = new Vector2(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
        using (_diagnostics.StartupProfiler?.Phase("Window/Shell Setup"))
        {
            _shell = ShellBootstrapper.Build(_presentation, _worldSession, screenSize, _diagnostics);
        }

        Window.Title = BaseWindowTitle;

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

        _shell.LoadContent(GraphicsDevice, _presentation.SpriteBatchRenderer.GetSpriteBatch(), unitRectangle, _diagnostics.FrameCostRecorder);

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        _diagnostics.Tick();

        _shell.PreSimulationUpdate(gameTime);

        if (!(_shell.MapWindow.IsPaused || _shell.Layers.IsMenuModeActive))
        {
            _frameCount++;
            _worldSession.PlayerActivityLog.BeginFrame(_frameCount, DateTime.Now);

            var ecsUpdateStart = Stopwatch.GetTimestamp();
            _worldSession.EcsContext.Update(new EngineTime(gameTime.TotalGameTime, gameTime.ElapsedGameTime, gameTime.IsRunningSlowly, _frameCount));
            _diagnostics.RecordSimulationTick("GameLoop", "EcsContext.Update (all systems)", Stopwatch.GetElapsedTime(ecsUpdateStart));
        }

        var shellUpdateStart = Stopwatch.GetTimestamp();
        _shell.Update(gameTime);
        _diagnostics.FrameCostRecorder?.Record(FrameCostCategory.Update, "GameLoop", "Shell.Update", Stopwatch.GetElapsedTime(shellUpdateStart));

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
        Window.Title = _lastAdminModeOn ? $"{BaseWindowTitle} - ADMIN" : BaseWindowTitle;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.LightGray);

        _presentation.SpriteBatchRenderer.StartSpriteBatch();
        _presentation.ElementPoolService.ResetRenderState();

        var shellDrawStart = Stopwatch.GetTimestamp();
        _shell.Draw(gameTime);
        _diagnostics.FrameCostRecorder?.Record(FrameCostCategory.Draw, "GameLoop", "Shell.Draw", Stopwatch.GetElapsedTime(shellDrawStart));

        _presentation.SpriteBatchRenderer.EndSpriteBatch();

        base.Draw(gameTime);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DungeonCrawlerWorld.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
