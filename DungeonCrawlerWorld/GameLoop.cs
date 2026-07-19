using Engine.Diagnostics;
using Engine.ECS.Systems;
using Engine.ECS.World;
using Engine.Math;
using Game.Bootstrap;
using Game.Notifications;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Bootstrap;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Notifications;

namespace DungeonCrawlerWorld;

/// <summary>
/// Composition root: wires Engine's module Bootstrapper, Presentation's window service,
/// and a minimal test map into a running game. Replaces the Phase 0 placeholder.
/// </summary>
public sealed class GameLoop : Microsoft.Xna.Framework.Game
{
    // Sized for the 1000x1000 test map (~1.06M terrain/wall entities, ~49k GoblinEngineers
    // plus a denser ~108k-entity secondary plain-Goblin population from TestMapBuilder)
    // rather than left at a small default and grown via doubling --
    // EntityManager/ComponentManager both grow automatically on demand, but at this scale
    // that's dozens of full-array reallocate-and-copy passes during Populate instead of
    // (close to) none.
    private const int InitialEntityCapacity = 1_300_000;
    private const int InitialComponentCapacity = 180_000;
    private static readonly Vector3Int TestMapSize = new(1000, 1000, 6);

    private readonly GraphicsDeviceManager _graphics;

    private PresentationContext _presentation = null!;
    private EcsContext _ecsContext = null!;
    private Game.World.World _world = null!;
    private MapWindow _mapWindow = null!;
    private Window _debugWindow = null!;
    private Window _selectionWindow = null!;
    private NotificationCenter _notificationCenter = null!;
    private readonly List<Window> _rootWindows = [];

    private Texture2D _unitRectangle = null!;
    private KeyboardState _previousKeyboardState;
    private MouseState _previousMouseState;
    private ZoomLevel _currentZoomLevel = ZoomLevel.Team;
    private bool _isPaused;

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

        // World must exist before GameBootstrapper.Build: MovementModule's Configure step
        // needs an IMapQuery (World implements it), but GameBootstrapper.Build is what
        // produces the EntityManager/ComponentManager the test map needs to populate that
        // world.
        var map = new Game.World.Map(TestMapSize);
        _world = new Game.World.World(map);

        var modsDirectory = Path.Combine(AppContext.BaseDirectory, "Mods");
        var bootstrapResult = GameBootstrapper.Build(_world, mathUtility, modsDirectory, InitialEntityCapacity, InitialComponentCapacity);
        _ecsContext = bootstrapResult.EcsContext;

        foreach (var failure in bootstrapResult.Failures)
        {
            Console.Error.WriteLine($"[ModuleLoad] {failure.Source}: {failure.Exception}");
        }

        new Game.TestMapBuilder(_ecsContext.EntityManager, _ecsContext.ComponentManager, mathUtility).Populate(_world);

        _presentation = PresentationBootstrapper.Build(GraphicsDevice, "Fonts");

        // MapWindow's dependencies (World/ComponentManager/renderers) come from Engine/Game
        // and Presentation both, so it can't be registered inside WindowService's own
        // constructor the way Window/TextWindow are -- this is exactly what
        // WindowService.RegisterFactory exists for.
        _presentation.WindowService.RegisterFactory<MapWindow>((_, _) => new MapWindow(
            _presentation.FontService,
            _presentation.WindowService,
            _world,
            _ecsContext.ComponentManager,
            _presentation.TileRenderer,
            _presentation.GlyphRenderer));

        _mapWindow = _presentation.WindowService.CreateWindow<MapWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions
            {
                RelativePosition = new Vector2(12, 12),
                Size = new Vector2(1256, 776),
                DisplayMode = WindowDisplayMode.Static,
            },
            Chrome = new WindowChromeOptions
            {
                ShowBorder = true,
                ShowTitle = true,
                TitleText = "Dungeon Crawler World",
            },
        });
        _mapWindow.Initialize();
        _rootWindows.Add(_mapWindow);

        _debugWindow = _presentation.WindowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions
            {
                RelativePosition = new Vector2(1280, 12),
                Size = new Vector2(300, 24),
                DisplayMode = WindowDisplayMode.Static,
            },
            Chrome = new WindowChromeOptions { ShowBorder = true },
        });
        _debugWindow.SetContent(new DebugWindowContent(_presentation.FontService, _ecsContext.EntityManager, _ecsContext.ComponentManager));
        _debugWindow.Initialize();
        _rootWindows.Add(_debugWindow);

        // Admin-only debug windows -- see the plan's Phase 4 UI decomposition section. Both
        // are validated against IWindowContent instead of being Window subclasses.
        var componentInspector = new ComponentInspector(_ecsContext.ComponentManager);
        _selectionWindow = _presentation.WindowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
            Layout = new WindowLayoutOptions
            {
                RelativePosition = new Vector2(1280, 44),
                Size = new Vector2(300, 744),
                DisplayMode = WindowDisplayMode.Static,
            },
            Chrome = new WindowChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "No map nodes selected" },
        });
        _selectionWindow.SetContent(new SelectionWindowContent(_world, _ecsContext.ComponentManager, componentInspector, _presentation.WindowService));
        _selectionWindow.Initialize();
        _rootWindows.Add(_selectionWindow);

        _notificationCenter = new NotificationCenter(_presentation.WindowService, _ecsContext.EventBus);
        _notificationCenter.Initialize();

        // Published through the buffered NotificationRequested event rather than calling
        // _notificationCenter.AddNotification directly -- GameLoop could call it directly
        // (it's the composition root), but publishing is what actually proves the buffered
        // pipeline works end to end, the same path a Game-layer system (which can't reference
        // Presentation.NotificationCenter at all) would have to use. Both ShowImmediately:
        // true so both categories are visibly on screen at once: System pauses the game and
        // can't be minimized, Quest does neither.
        _ecsContext.EventBus.Publish(new NotificationRequested(NotificationCategory.System, "You have entered the dungeon", ShowImmediately: true));
        _ecsContext.EventBus.Publish(new NotificationRequested(NotificationCategory.Quest, "Take your first steps!", ShowImmediately: true));

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _unitRectangle = new Texture2D(GraphicsDevice, 1, 1);
        _unitRectangle.SetData([Color.White]);

        foreach (var window in _rootWindows)
        {
            window.LoadContent();
        }

        _notificationCenter.LoadContent();

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        HandleInput();

        // Runs before the pause check (and before _ecsContext.Update) so a notification
        // published this same frame -- e.g. via NotificationRequested -- is reflected in
        // HasBlockingNotification before deciding whether to advance gameplay this frame.
        // Notifications update even while paused, deliberately.
        _notificationCenter.Update(gameTime);

        if (!(_isPaused || _notificationCenter.HasBlockingNotification))
        {
            _ecsContext.Update(new EngineTime(gameTime.TotalGameTime, gameTime.ElapsedGameTime, gameTime.IsRunningSlowly));
        }

        foreach (var window in _rootWindows)
        {
            window.Update(gameTime);
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        var spriteBatch = _presentation.SpriteBatchRenderer.StartSpriteBatch();

        foreach (var window in _rootWindows)
        {
            window.Draw(gameTime, GraphicsDevice, spriteBatch, _unitRectangle);
        }

        _notificationCenter.Draw(gameTime, GraphicsDevice, spriteBatch, _unitRectangle);

        _presentation.SpriteBatchRenderer.EndSpriteBatch();

        base.Draw(gameTime);
    }

    private void HandleInput()
    {
        var keyboardState = Keyboard.GetState();

        if (IsKeyPressed(keyboardState, Keys.Space))
        {
            _isPaused = !_isPaused;
        }

        var scrollChange = Point.Zero;
        if (keyboardState.IsKeyDown(Keys.W))
        {
            scrollChange.Y -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.S))
        {
            scrollChange.Y += 1;
        }
        if (keyboardState.IsKeyDown(Keys.A))
        {
            scrollChange.X -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.D))
        {
            scrollChange.X += 1;
        }
        if (scrollChange != Point.Zero)
        {
            _mapWindow.UpdateScrollPosition(scrollChange);
        }

        if (IsKeyPressed(keyboardState, Keys.OemPlus) || IsKeyPressed(keyboardState, Keys.Add))
        {
            CycleZoom(-1);
        }
        if (IsKeyPressed(keyboardState, Keys.OemMinus) || IsKeyPressed(keyboardState, Keys.Subtract))
        {
            CycleZoom(1);
        }

        var mouseState = Mouse.GetState();
        if (mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released)
        {
            var clickPosition = new Point(mouseState.X, mouseState.Y);

            // Notification popups float above the rest of the UI, so they get first claim on a click.
            if (!_notificationCenter.HandleClick(clickPosition))
            {
                foreach (var window in _rootWindows)
                {
                    if (window.WindowRectangle.Contains(clickPosition))
                    {
                        window.HandleClick(clickPosition);
                        break;
                    }
                }
            }
        }

        _previousKeyboardState = keyboardState;
        _previousMouseState = mouseState;
    }

    private bool IsKeyPressed(KeyboardState current, Keys key) => current.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);

    private void CycleZoom(int direction)
    {
        var zoomLevels = Enum.GetValues<ZoomLevel>();
        var currentIndex = Array.IndexOf(zoomLevels, _currentZoomLevel);
        var newIndex = MathUtility.ClampInt(currentIndex + direction, 0, zoomLevels.Length - 1);
        _currentZoomLevel = zoomLevels[newIndex];
        _mapWindow.UpdateZoomLevel(_currentZoomLevel);
    }
}
