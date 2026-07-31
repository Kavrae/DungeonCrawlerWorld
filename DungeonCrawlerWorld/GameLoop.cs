using Engine.Diagnostics;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Bootstrap;
using Game.Diagnostics;
using Game.Floors;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Bootstrap;
using Presentation.Input;
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

    // ~5 seconds at 60fps -- how often ReportTopPhases dumps the full per-phase ranking to the
    // console. PhaseProfiler.TopPhases itself refreshes every real second regardless; this only
    // paces how often that snapshot gets printed, so a gameplay demo's console log doesn't fill
    // with a duplicate ranking every single frame.
    private const int ProfileReportIntervalFrames = 300;

    private readonly GraphicsDeviceManager _graphics;

    private Game.World.World _world = null!;
    private MathUtility _mathUtility = null!;
    private EcsContext _ecsContext = null!;
    private PresentationContext _presentation = null!;
    private GameShellContext _shell = null!;
    private GameInputController _inputController = null!;
    private PlayerActivityLog _playerActivityLog = null!;
    private PhaseProfiler _profiler = null!;
    private FrameEventBuffer<EntityMoved> _movedEntities = null!;
    private bool _playerSpawned;
    private int _frameCount;

    private Texture2D _unitRectangle = null!;

    public GameLoop()
    {
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
        _mathUtility = new MathUtility();

        // Floor 1 of (eventually) 18 -- floors are strictly sequential, no skipping or
        // backtracking. There's no advance trigger yet (that needs a win-condition system
        // that doesn't exist), so this stays a local constant rather than tracked state until
        // something actually needs to change it.
        const int floorNumber = 1;

        // World must exist before GameBootstrapper.Build: MovementModule's Configure step
        // needs an IMapQuery (World implements it), but GameBootstrapper.Build is what
        // produces the EntityManager/ComponentManager FloorBuilder.PopulateFloor needs to
        // populate that world. World itself is session-long-lived, not rebuilt per floor --
        // see FloorBuilder -- so the IMapQuery every module captures here stays valid across
        // future floor transitions, which will replace world.Map rather than World itself.
        _world = new Game.World.World(FloorBuilder.CreateMap(floorNumber));

        var modsDirectory = Path.Combine(AppContext.BaseDirectory, "Mods");
        var bootstrapResult = GameBootstrapper.Build(_world, _mathUtility, modsDirectory, InitialEntityCapacity, InitialComponentCapacity);
        _ecsContext = bootstrapResult.EcsContext;
        _movedEntities = bootstrapResult.MovedEntities;

        _profiler = new PhaseProfiler();
        _ecsContext.SystemManager.Profiler = _profiler;
        _ecsContext.EventBus.Profiler = _profiler;

        foreach (var failure in bootstrapResult.Failures)
        {
            Console.Error.WriteLine($"[ModuleLoad] {failure.Source}: {failure.Exception}");
        }

        FloorBuilder.PopulateFloor(_world, _ecsContext, _mathUtility);

        var logFilePath = Path.Combine(FindProjectRoot(), "Log", "player-activity.log");
        _playerActivityLog = new PlayerActivityLog(_world, _ecsContext.EventBus, logFilePath);
        Console.WriteLine($"[PlayerActivityLog] Writing to {logFilePath}");

        _presentation = PresentationBootstrapper.Build(GraphicsDevice, "Fonts");
        var screenSize = new Vector2(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
        _shell = GameShellBootstrapper.Build(_presentation, _world, _ecsContext, bootstrapResult.AbilityCatalog, screenSize);
        _inputController = _shell.InputController;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _unitRectangle = new Texture2D(GraphicsDevice, 1, 1);
        _unitRectangle.SetData([Color.White]);

        _shell.LoadContent();

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        _inputController.Update(gameTime);

        _shell.NotificationCenter.Update(gameTime);

        if (!(_shell.MapWindow.IsPaused || _shell.NotificationCenter.HasBlockingNotification))
        {
            _frameCount++;
            _playerActivityLog.BeginFrame(_frameCount, DateTime.Now);

            // TEMPORARY Once, on this class's first live tick -- not during Initialize() -- see
            // FloorBuilder's own doc comment for why. Runs before EcsContext.Update below so
            // the spawn's buffered EntityMoved (see FloorBuilder.CreatePlayer) is picked up by
            // ContactDamageSystem/StatusEffectAuraSystem in this same cycle.
            if (!_playerSpawned)
            {
                _world.PlayerEntityId = FloorBuilder.CreatePlayer(_world, _ecsContext, _mathUtility, _movedEntities);
                _playerSpawned = true;
            }

            var ecsUpdateStart = Stopwatch.GetTimestamp();
            _ecsContext.Update(new EngineTime(gameTime.TotalGameTime, gameTime.ElapsedGameTime, gameTime.IsRunningSlowly));
            _profiler.Record("EcsContext.Update (all systems)", Stopwatch.GetElapsedTime(ecsUpdateStart));

            if (_frameCount % ProfileReportIntervalFrames == 0)
            {
                ReportTopPhases();
            }
        }

        var shellUpdateStart = Stopwatch.GetTimestamp();
        _shell.Update(gameTime);
        _profiler.Record("Shell.Update", Stopwatch.GetElapsedTime(shellUpdateStart));

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.LightGray);

        var spriteBatch = _presentation.SpriteBatchRenderer.StartSpriteBatch();

        var shellDrawStart = Stopwatch.GetTimestamp();
        _shell.Draw(gameTime, GraphicsDevice, spriteBatch, _unitRectangle);
        _profiler.Record("Shell.Draw", Stopwatch.GetElapsedTime(shellDrawStart));

        _presentation.SpriteBatchRenderer.EndSpriteBatch();

        base.Draw(gameTime);
    }

    /// <summary>Dumps PhaseProfiler's full last-second ranking to the console every ProfileReportIntervalFrames -- a single on-screen "Top: X" readout (see DebugWindowContent) is enough to notice a hotspot while playing, but this keeps a fuller trail (the #2, #3, ... contributors too) for after the demo ends.</summary>
    private void ReportTopPhases()
    {
        if (_profiler.TopPhases.Count == 0)
        {
            return;
        }

        Console.WriteLine("[PerformanceProfile] Top phases (ms spent in the last second):");
        foreach (var (name, milliseconds) in _profiler.TopPhases)
        {
            Console.WriteLine($"[PerformanceProfile]   {name}: {milliseconds:N1}ms");
        }
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