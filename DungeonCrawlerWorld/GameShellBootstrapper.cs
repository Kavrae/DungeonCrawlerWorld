using Engine.Diagnostics;
using Engine.ECS.Context;
using Game.Notifications;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Bootstrap;
using Presentation.Input;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Notifications;

namespace DungeonCrawlerWorld;

/// <summary>
/// Builds the app's specific screen -- the map/debug/selection windows and the notification
/// center -- on top of the services PresentationBootstrapper already constructed, and wires up
/// input focus for that screen (default/initial focus, and what gains focus when a notification
/// opens or the quest composer opens). Kept separate from PresentationBootstrapper (which only
/// builds reusable Presentation services and knows nothing about what windows this particular
/// game has) the same way GameBootstrapper is kept separate from Engine's Bootstrapper. Owning
/// the input wiring here too, not just the windows, keeps GameLoop down to per-frame
/// orchestration -- everything about this specific screen (what it contains AND how focus moves
/// around it) is a single composition step rather than split across two files for no reason
/// beyond construction order.
/// </summary>
public static class GameShellBootstrapper
{
    private const float ScreenMargin = 12f;

    private const float DebugWindowHeight = 24f;
    private const float SelectionWindowWidth = 300f;

    public static GameShellContext Build(PresentationContext presentation, World world, EcsContext ecsContext, Vector2 screenSize)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ecsContext);

        var (rootWindows, mapWindow, mapViewState, mapSize) = BuildRootWindows(presentation, world, ecsContext, screenSize);
        var (hudWindows, questTriggerWindow) = BuildHudWindows(presentation, world, ecsContext, screenSize, mapViewState, mapSize);
        var (alwaysOnTopWindows, notificationCenter) = BuildAlwaysOnTopWindows(presentation, ecsContext);

        // Constructed last, once every window/list it needs to wire already exists
        var inputController = new GameInputController(rootWindows, hudWindows, alwaysOnTopWindows, screenSize);
        inputController.SetDefaultFocusWindow(mapWindow);
        inputController.FocusWindow(mapWindow);

        // A notification popping up (fresh, or promoted from the unread queue) takes focus --
        // see NotificationCenter.ActiveNotificationOpened.
        notificationCenter.ActiveNotificationOpened += notificationWindow => inputController.FocusWindow(notificationWindow);

        // Opening the quest composer focuses its TextBox (via GameInputController.SetFocus's
        // own NextTextBoxAfter redirect) immediately -- OpenQuestComposer returns the popup
        // synchronously, so this can call FocusWindow directly instead of needing an event.
        // The composer popup overlaps the fullscreen map like any other popup, and (unlike the
        // always-visible HUD panels) isn't guaranteed to stay above a map click while it's open
        // -- AlwaysOnTop tier, the same tier NotificationCenter's own popups already use, not
        // Base/HUD.
        questTriggerWindow.Clicked += _ => inputController.FocusWindow(OpenQuestComposer(presentation.WindowService, notificationCenter, alwaysOnTopWindows));

        return new GameShellContext(mapWindow, notificationCenter, rootWindows, hudWindows, alwaysOnTopWindows, inputController);
    }

    /// <summary>Base tier: the map itself plus the debug stats footer directly beneath it -- see GameShellContext's doc comment for what "Base" means. mapViewState/mapSize are returned for BuildHudWindows, whose selection window needs both (mapViewState to scope the inspector, mapSize to dock against the map's actual bottom edge).</summary>
    private static (List<Window> RootWindows, MapWindow MapWindow, MapViewState MapViewState, Vector2 MapSize) BuildRootWindows(
        PresentationContext presentation, World world, EcsContext ecsContext, Vector2 screenSize)
    {
        var rootWindows = new List<Window>();

        var mapSize = new Vector2(screenSize.X - ScreenMargin * 2, screenSize.Y - ScreenMargin * 3 - DebugWindowHeight);

        // Single MapViewState instance for the session shared between
        // MapWindow (the only writer, via click-to-select and Page Up/Down) and
        // SelectionWindowContent (which reads it to scope the inspector to what's on screen).
        var mapViewState = new MapViewState();

        // MapWindow's dependencies (World/ComponentManager/renderers) come from Engine/Game
        // and Presentation both, so it can't be registered inside WindowService's own
        // constructor the way Window/TextWindow are -- this is exactly what
        // WindowService.RegisterFactory exists for.
        presentation.WindowService.RegisterFactory<MapWindow>((_, _) => new MapWindow(
            presentation.FontService,
            presentation.WindowService,
            world,
            mapViewState,
            ecsContext.ComponentManager,
            presentation.TileRenderer,
            presentation.GlyphRenderer));

        var mapWindow = presentation.WindowService.CreateWindow<MapWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions
            {
                RelativePosition = new Vector2(ScreenMargin, ScreenMargin),
                Size = mapSize,
                DisplayMode = WindowDisplayMode.Fixed,
            },
            Chrome = new WindowChromeOptions
            {
                ShowBorder = true,
                ShowTitle = true,
                TitleText = "Dungeon Crawler World",
                CanUserScrollHorizontal = true,
                CanUserScrollVertical = true,
            },
        });
        mapWindow.Initialize();
        rootWindows.Add(mapWindow);

        var debugWindow = presentation.WindowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions
            {
                RelativePosition = new Vector2(ScreenMargin, ScreenMargin + mapSize.Y + ScreenMargin),
                Size = new Vector2(mapSize.X, DebugWindowHeight),
                DisplayMode = WindowDisplayMode.Fixed,
            },
            Chrome = new WindowChromeOptions { ShowBorder = true, CanUserFocus = false },
        });
        debugWindow.SetContent(new DebugWindowContent(presentation.FontService, ecsContext.EntityManager, ecsContext.ComponentManager));
        debugWindow.Initialize();
        rootWindows.Add(debugWindow);

        return (rootWindows, mapWindow, mapViewState, mapSize);
    }

    /// <summary>HUD tier: the selection/inspector panel, the player health bar, and the quest trigger -- see GameShellContext's doc comment for what "HUD" means. questTriggerWindow is returned for Build, which wires its Clicked event once the always-on-top tier (needed by OpenQuestComposer) also exists.</summary>
    private static (List<Window> HudWindows, TextWindow QuestTriggerWindow) BuildHudWindows(
        PresentationContext presentation, World world, EcsContext ecsContext, Vector2 screenSize, MapViewState mapViewState, Vector2 mapSize)
    {
        var hudWindows = new List<Window>();

        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var selectionWindowHeight = screenSize.Y * 0.75f;
        var selectionWindow = presentation.WindowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
            Layout = new WindowLayoutOptions
            {
                RelativePosition = new Vector2(screenSize.X - HudMetrics.Margin.X - SelectionWindowWidth, ScreenMargin + mapSize.Y - selectionWindowHeight),
                Size = new Vector2(SelectionWindowWidth, selectionWindowHeight),
                DisplayMode = WindowDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new WindowChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true },
        });
        selectionWindow.SetContent(new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, presentation.WindowService));
        selectionWindow.Initialize();
        hudWindows.Add(selectionWindow);

        var playerHealthBarWindow = presentation.WindowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions
            {
                RelativePosition = new Vector2(screenSize.X - PlayerHealthBarContent.Size.X - HudMetrics.Margin.X, HudMetrics.Margin.Y),
                Size = PlayerHealthBarContent.Size,
                DisplayMode = WindowDisplayMode.Fixed,
                IsTransparent = true,
            },
            // BorderSize left at the default (1,1) -- a thinner outset reads as a subtle bevel rather than a heavy frame.
            Chrome = new WindowChromeOptions { ShowTitle = false, ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
        });
        playerHealthBarWindow.SetContent(new PlayerHealthBarContent(world, ecsContext.ComponentManager));
        playerHealthBarWindow.Initialize();
        hudWindows.Add(playerHealthBarWindow);

        // TEMPORARY First concrete TextBox consumer (see the Text input TODO) -- a multiline TextBox in
        // a closeable popup that submits into a new Quest notification. "New Quest" is a
        // clickable TextWindow the same way NotificationCenter's own summary-bar entries are
        // (see NotificationCenter.Initialize's countWindow.Clicked wiring). HUD tier -- overlays
        // the fullscreen map, same reasoning as selectionWindow above.
        var questTriggerWindow = presentation.WindowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            // Left margin matches the notification count window's (HudMetrics.Margin.X).
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(HudMetrics.Margin.X, 800), Size = new Vector2(120, 30), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowBorder = true, CanUserFocus = false },
            Content = new WindowContentOptions { ContentColor = Color.LightGray },
            Text = new TextOptions { Text = "New Quest" },
        });
        questTriggerWindow.Initialize();
        hudWindows.Add(questTriggerWindow);

        return (hudWindows, questTriggerWindow);
    }

    /// <summary>AlwaysOnTop tier: NotificationCenter owns/populates this list itself (summary bar + popups) -- see GameShellContext's doc comment for what "AlwaysOnTop" means. Build also passes this same list into OpenQuestComposer later, since that popup belongs in this tier too.</summary>
    private static (List<Window> AlwaysOnTopWindows, NotificationCenter NotificationCenter) BuildAlwaysOnTopWindows(PresentationContext presentation, EcsContext ecsContext)
    {
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = new NotificationCenter(presentation.WindowService, ecsContext.EventBus, alwaysOnTopWindows);
        notificationCenter.Initialize();

        return (alwaysOnTopWindows, notificationCenter);
    }

    /// <summary>TEMPORARYOpens a fresh closeable popup with one multiline TextBox; submitting posts a Quest notification and closes the popup. Returns the popup so the caller can focus it.</summary>
    private static Window OpenQuestComposer(WindowService windowService, NotificationCenter notificationCenter, List<Window> alwaysOnTopWindows)
    {
        // Deliberately Fixed, not WrapContent: a WrapContent parent's ContentSize starts at
        // ~(0,0) before it's ever measured a child, and Window.Measure overwrites a child's own
        // MaximumSize with _parentWindow.ContentSize on every pass -- so a WrapContent popup
        // and a TextBox whose growth cap is itself derived from that popup's ContentSize
        // collapse each other down to ~0 instead of settling on a real size (confirmed by a
        // failing test before this comment existed). Fixed has no such circularity: popupSize
        // is stable and known before textBoxMaximumSize's own TextBox is ever measured. The
        // popup still shrinks/grows with the TextBox -- just explicitly, below, off the
        // TextBox's own Resized event, rather than through WrapContent's automatic fit-to-
        // children pass.
        var popupSize = new Vector2(420, 220);
        var textBoxMaximumSize = new Vector2(400, 190);
        var popupChromeHeight = popupSize.Y - textBoxMaximumSize.Y;

        var popup = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true },
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(200, 250), Size = popupSize, DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "New Quest (Enter to submit)", CanUserClose = true, CanUserMove = true },
        });
        popup.Initialize();
        alwaysOnTopWindows.Add(popup);

        // Pooled and reused for the next "New Quest" click (see WindowService) -- must detach
        // itself and remove the closed instance from alwaysOnTopWindows, the same cleanup
        // NotificationCenter.OnActiveNotificationClosed already does for its own popups, or a
        // reopened composer would eventually add the same recycled instance to
        // alwaysOnTopWindows twice.
        void onClosed(Window closedWindow)
        {
            closedWindow.Closed -= onClosed;
            alwaysOnTopWindows.Remove(closedWindow);
        }

        popup.Closed += onClosed;

        // Size.Y is only a starting point -- TextBox.AutoSizeToContent immediately shrinks it
        // to a 2-line minimum on Initialize, then grows it back up as text is typed, capped at
        // MaximumSize.Y; CanUserScrollVertical covers anything typed beyond that cap.
        var textBox = windowService.CreateWindow<TextBox>(popup, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(0, 0), Size = textBoxMaximumSize, MaximumSize = textBoxMaximumSize, DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowBorder = true, CanUserScrollVertical = true },
            Text = new TextOptions { Multiline = true },
        });
        // Subscribed before AddChildWindow -- Initialize (called from within AddChildWindow) is
        // what fires the first Resized, shrinking the popup down from popupSize to match the
        // TextBox's own initial 2-line height, not just later growth.
        textBox.Resized += _ => popup.SetSize(new Vector2(popup.WindowCurrentSize.X, textBox.WindowCurrentSize.Y + popupChromeHeight));
        textBox.TextSubmitted += text =>
        {
            // showImmediately: false -- created already minimized (queued in the Quest summary
            // count, opened later by clicking it), rather than popping up as an active window.
            notificationCenter.AddNotification(NotificationCategory.Quest, text, showImmediately: false, title: "New Quest");
            popup.Close();
        };
        popup.AddChildWindow(textBox);

        return popup;
    }
}

public sealed record GameShellContext(MapWindow MapWindow, NotificationCenter NotificationCenter, List<Window> RootWindows, List<Window> HudWindows, List<Window> AlwaysOnTopWindows, GameInputController InputController)
{
    public void LoadContent()
    {
        foreach (var window in RootWindows)
        {
            window.LoadContent();
        }

        foreach (var window in HudWindows)
        {
            window.LoadContent();
        }

        foreach (var window in AlwaysOnTopWindows)
        {
            window.LoadContent();
        }
    }

    public void Update(GameTime gameTime)
    {
        foreach (var window in RootWindows)
        {
            window.Update(gameTime);
        }

        foreach (var window in HudWindows)
        {
            window.Update(gameTime);
        }

        foreach (var window in AlwaysOnTopWindows)
        {
            window.Update(gameTime);
        }
    }

    public void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        foreach (var window in RootWindows)
        {
            window.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
        }

        foreach (var window in HudWindows)
        {
            window.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
        }

        foreach (var window in AlwaysOnTopWindows)
        {
            window.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
        }
    }
}