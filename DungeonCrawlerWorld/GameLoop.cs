using Engine.ECS.Systems;
using Engine.ECS.Context;
using Engine.Math;
using Game.Bootstrap;
using Game.Floors;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Bootstrap;
using Presentation.Input;

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

    private readonly GraphicsDeviceManager _graphics;

    private EcsContext _ecsContext = null!;
    private PresentationContext _presentation = null!;
    private GameShellContext _shell = null!;
    private GameInputController _inputController = null!;

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
        var mathUtility = new MathUtility();

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
        var world = new Game.World.World(FloorBuilder.CreateMap(floorNumber));

        var modsDirectory = Path.Combine(AppContext.BaseDirectory, "Mods");
        var bootstrapResult = GameBootstrapper.Build(world, mathUtility, modsDirectory, InitialEntityCapacity, InitialComponentCapacity);
        _ecsContext = bootstrapResult.EcsContext;

        foreach (var failure in bootstrapResult.Failures)
        {
            Console.Error.WriteLine($"[ModuleLoad] {failure.Source}: {failure.Exception}");
        }

        FloorBuilder.PopulateFloor(world, _ecsContext, mathUtility);

        _presentation = PresentationBootstrapper.Build(GraphicsDevice, "Fonts");
        var screenSize = new Vector2(_graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
        _shell = GameShellBootstrapper.Build(_presentation, world, _ecsContext, screenSize);
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
            _ecsContext.Update(new EngineTime(gameTime.TotalGameTime, gameTime.ElapsedGameTime, gameTime.IsRunningSlowly));
        }

        _shell.Update(gameTime);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.LightGray);

        var spriteBatch = _presentation.SpriteBatchRenderer.StartSpriteBatch();

        _shell.Draw(gameTime, GraphicsDevice, spriteBatch, _unitRectangle);

        _presentation.SpriteBatchRenderer.EndSpriteBatch();

        base.Draw(gameTime);
    }
}