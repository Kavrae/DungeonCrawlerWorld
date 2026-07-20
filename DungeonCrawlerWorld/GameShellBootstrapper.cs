using Engine.Diagnostics;
using Engine.ECS.World;
using Game.Notifications;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Bootstrap;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Notifications;

namespace DungeonCrawlerWorld;

/// <summary>
/// Builds the app's specific screen -- the map/debug/selection windows and the notification
/// center -- on top of the services PresentationBootstrapper already constructed. Kept separate
/// from PresentationBootstrapper (which only builds reusable Presentation services and knows
/// nothing about what windows this particular game has) the same way GameBootstrapper is kept
/// separate from Engine's Bootstrapper.
/// </summary>
public static class GameShellBootstrapper
{
    public static GameShellContext Build(PresentationContext presentation, World world, EcsContext ecsContext)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ecsContext);

        var rootWindows = new List<Window>();

        // MapWindow's dependencies (World/ComponentManager/renderers) come from Engine/Game
        // and Presentation both, so it can't be registered inside WindowService's own
        // constructor the way Window/TextWindow are -- this is exactly what
        // WindowService.RegisterFactory exists for.
        presentation.WindowService.RegisterFactory<MapWindow>((_, _) => new MapWindow(
            presentation.FontService,
            presentation.WindowService,
            world,
            ecsContext.ComponentManager,
            presentation.TileRenderer,
            presentation.GlyphRenderer));

        var mapWindow = presentation.WindowService.CreateWindow<MapWindow>(null, new WindowOptions
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
        mapWindow.Initialize();
        rootWindows.Add(mapWindow);

        var debugWindow = presentation.WindowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions
            {
                RelativePosition = new Vector2(1280, 12),
                Size = new Vector2(300, 24),
                DisplayMode = WindowDisplayMode.Static,
            },
            Chrome = new WindowChromeOptions { ShowBorder = true },
        });
        debugWindow.SetContent(new DebugWindowContent(presentation.FontService, ecsContext.EntityManager, ecsContext.ComponentManager));
        debugWindow.Initialize();
        rootWindows.Add(debugWindow);

        // Admin-only debug windows -- see the plan's Phase 4 UI decomposition section. Both
        // are validated against IWindowContent instead of being Window subclasses.
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var selectionWindow = presentation.WindowService.CreateWindow<Window>(null, new WindowOptions
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
        selectionWindow.SetContent(new SelectionWindowContent(world, ecsContext.ComponentManager, componentInspector, presentation.WindowService));
        selectionWindow.Initialize();
        rootWindows.Add(selectionWindow);

        var notificationCenter = new NotificationCenter(presentation.WindowService, ecsContext.EventBus);
        notificationCenter.Initialize();

        // Published through the buffered NotificationRequested event rather than calling
        // notificationCenter.AddNotification directly -- the shell could call it directly (it's
        // still part of the composition root), but publishing is what actually proves the
        // buffered pipeline works end to end, the same path a Game-layer system (which can't
        // reference Presentation.NotificationCenter at all) would have to use. Both
        // ShowImmediately: true so both categories are visibly on screen at once: System pauses
        // the game and can't be minimized, Quest does neither.
        ecsContext.EventBus.Publish(new NotificationRequested(NotificationCategory.System, "You have entered the dungeon", ShowImmediately: true));
        ecsContext.EventBus.Publish(new NotificationRequested(NotificationCategory.Quest, "Take your first steps!", ShowImmediately: true));

        return new GameShellContext(mapWindow, notificationCenter, rootWindows);
    }
}

/// <summary>
/// Bundles the app's constructed root windows and notification center, produced by
/// GameShellBootstrapper. Owns LoadContent/Draw itself -- both are self-contained fan-outs
/// over RootWindows/NotificationCenter with no dependency on anything outside the shell.
/// Update is deliberately not here: GameLoop needs to run EcsContext.Update and check the
/// pause state in between updating NotificationCenter and updating the windows, an ordering
/// constraint that belongs to the composition root, not the shell.
/// </summary>
public sealed record GameShellContext(MapWindow MapWindow, NotificationCenter NotificationCenter, IReadOnlyList<Window> RootWindows)
{
    public void LoadContent()
    {
        foreach (var window in RootWindows)
        {
            window.LoadContent();
        }

        NotificationCenter.LoadContent();
    }

    public void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        foreach (var window in RootWindows)
        {
            window.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
        }

        NotificationCenter.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
    }
}
