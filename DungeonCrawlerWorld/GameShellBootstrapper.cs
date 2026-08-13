using Engine.Diagnostics;
using Engine.ECS.Context;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.Notifications;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Bootstrap;
using Presentation.Input;
using Presentation.UI;
using Presentation.UI.AbilityScores;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Presentation.UI.Notifications;

namespace DungeonCrawlerWorld;

/// <summary>
/// Builds the app's specific screen on top of the services PresentationBootstrapper already constructed.
/// Wires up input focus for those screens.
/// Kept separate from PresentationBootstrapper (which only
/// builds reusable Presentation services and knows nothing about what windows this particular
/// game has) the same way GameBootstrapper is kept separate from Engine's Bootstrapper.
/// </summary>
public static class GameShellBootstrapper
{
    private const float ScreenMargin = 12f;

    private const float DebugWindowHeight = 24f;
    private const float SelectionWindowWidth = 300f;
    private const float ActionLockGap = 8f;
    private const float ManaBarGap = 3f;

    public static GameShellContext Build(PresentationContext presentation, World world, EcsContext ecsContext, ActionCatalog actionCatalog, ItemCatalog itemCatalog, Vector2 screenSize)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ecsContext);
        ArgumentNullException.ThrowIfNull(actionCatalog);
        ArgumentNullException.ThrowIfNull(itemCatalog);

        var (baseWindows, mapWindow, mapViewState, mapSize, actionTargeting) = BuildBaseWindows(presentation, world, ecsContext, actionCatalog, itemCatalog, screenSize);
        var (staticHudWindows, questTriggerWindow, hotbarContent) = BuildStaticHudWindows(presentation, world, ecsContext, actionCatalog, itemCatalog, screenSize, mapViewState, mapSize);
        var (dynamicHudWindows, notificationCenter, inventory) = BuildDynamicHudWindows(presentation, world, ecsContext, itemCatalog, mapWindow);
        var hotbarController = BuildHotbarController(presentation, mapViewState, hotbarContent, actionTargeting, dynamicHudWindows);

        // Empty list for BuildUserWindows to populate
        var userWindows = new List<Element>();

        // Constructed after every other tier's windows exist, but before User's own content is
        // built -- DragGhostContent (User tier) needs a real UiInputController reference,
        // which can't exist before this point.
        var inputController = new UiInputController(baseWindows, staticHudWindows, dynamicHudWindows, userWindows, screenSize, hotbarController);
        inputController.SetDefaultFocusElement(mapWindow);
        inputController.FocusElement(mapWindow);

        // A notification popping up (fresh, or promoted from the unread queue) takes focus --
        // see NotificationCenter.ActiveNotificationOpened.
        notificationCenter.ActiveNotificationOpened += notificationWindow => inputController.FocusElement(notificationWindow);

        // Opening the quest composer focuses its TextBox (via UiInputController.SetFocus's
        // own NextTextBoxAfter redirect) immediately -- OpenQuestComposer returns the popup
        // synchronously, so this can call FocusWindow directly instead of needing an event.
        // The composer popup overlaps the fullscreen map like any other popup, and (unlike the
        // always-visible StaticHUD panels) isn't guaranteed to stay above a map click while it's
        // open -- DynamicHUD tier, the same tier NotificationCenter's own popups already use,
        // not Base/StaticHUD.
        questTriggerWindow.Clicked += _ => inputController.FocusElement(OpenQuestComposer(presentation.ElementPoolService, notificationCenter, dynamicHudWindows));

        BuildUserWindows(presentation, inputController, itemCatalog, userWindows);

        return new GameShellContext(mapWindow, notificationCenter, inventory, baseWindows, staticHudWindows, dynamicHudWindows, userWindows, inputController);
    }

    /// <summary>Base tier: the map itself plus the debug stats footer directly beneath it -- see UiInputController's own doc comment for what each of the four tiers means. mapViewState/mapSize are returned for BuildStaticHudWindows, whose selection window needs both (mapViewState to scope the inspector, mapSize to dock against the map's actual bottom edge). actionTargeting is returned too, promoted here (rather than built privately inside MapWindow's own constructor) so BuildHotbarController can share the same instance instead of forwarding through MapWindow. playerMovement isn't returned -- nothing outside MapWindow's own factory closure needs it, unlike actionTargeting.</summary>
    private static (List<Element> BaseWindows, MapWindow MapWindow, MapViewState MapViewState, Vector2 MapSize, ActionTargetingController ActionTargeting) BuildBaseWindows(
        PresentationContext presentation, World world, EcsContext ecsContext, ActionCatalog actionCatalog, ItemCatalog itemCatalog, Vector2 screenSize)
    {
        var baseWindows = new List<Element>();

        var mapSize = new Vector2(screenSize.X - ScreenMargin * 2, screenSize.Y - ScreenMargin * 3 - DebugWindowHeight);

        // Single MapViewState instance for the session shared between
        // MapWindow (the only writer, via click-to-select and Page Up/Down) and
        // SelectionWindowContent (which reads it to scope the inspector to what's on screen).
        var mapViewState = new MapViewState();

        var componentManager = ecsContext.ComponentManager;
        var camera = new MapCamera(world);
        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            camera,
            actionCatalog,
            itemCatalog,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetMultiPool<ActionHotkeyBindingComponent>(),
            componentManager.GetMultiPool<ItemHotkeyBindingComponent>(),
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<ManaComponent>(),
            componentManager.GetMultiPool<AbilityScoreComponent>());
        var playerMovement = new PlayerMovementController(
            world,
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<MovementComponent>());

        // MapWindow's dependencies (World/ComponentManager/renderers) come from Engine/Game
        // and Presentation both, so it can't be registered inside WindowService's own
        // constructor the way Window/TextWindow are -- this is exactly what
        // WindowService.RegisterFactory exists for.
        presentation.ElementPoolService.RegisterFactory<MapWindow>((_, _) => new MapWindow(
            presentation.FontService,
            presentation.ElementPoolService,
            world,
            mapViewState,
            componentManager,
            ecsContext.EventBus,
            actionCatalog,
            itemCatalog,
            presentation.TileRenderer,
            presentation.GlyphRenderer,
            presentation.SpriteSheetService,
            presentation.SpriteRenderer,
            camera,
            actionTargeting,
            playerMovement));

        var mapWindow = presentation.ElementPoolService.CreateElement<MapWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(ScreenMargin, ScreenMargin),
                Size = mapSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowBorder = true,
                ShowTitle = true,
                TitleText = "Dungeon Crawler World",
                CanUserScrollHorizontal = true,
                CanUserScrollVertical = true,
            },
        });
        mapWindow.Initialize();
        baseWindows.Add(mapWindow);

        var debugWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(ScreenMargin, ScreenMargin + mapSize.Y + ScreenMargin),
                Size = new Vector2(mapSize.X, DebugWindowHeight),
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
        });
        debugWindow.SetContent(new DebugWindowContent(presentation.FontService, ecsContext.EntityManager, ecsContext.ComponentManager, ecsContext.SystemManager));
        debugWindow.Initialize();
        baseWindows.Add(debugWindow);

        return (baseWindows, mapWindow, mapViewState, mapSize, actionTargeting);
    }

    /// <summary>StaticHUD tier: the selection/inspector panel, the player health bar, action lock, status effects, the hotbar, and the quest trigger -- see UiInputController's own doc comment for what each of the four tiers means. questTriggerWindow is returned for Build, which wires its Clicked event once the DynamicHUD tier (needed by OpenQuestComposer) also exists. hotbarContent is returned too, for BuildHotbarController.</summary>
    private static (List<Element> StaticHudWindows, TextWindow QuestTriggerWindow, HotbarContent HotbarContent) BuildStaticHudWindows(
        PresentationContext presentation, World world, EcsContext ecsContext, ActionCatalog actionCatalog, ItemCatalog itemCatalog, Vector2 screenSize, MapViewState mapViewState, Vector2 mapSize)
    {
        var staticHudWindows = new List<Element>();

        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var selectionWindowHeight = screenSize.Y * 0.75f;
        var selectionWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(screenSize.X - HudMetrics.Margin.X - SelectionWindowWidth, ScreenMargin + mapSize.Y - selectionWindowHeight),
                Size = new Vector2(SelectionWindowWidth, selectionWindowHeight),
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true },
        });
        selectionWindow.SetContent(new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, presentation.ElementPoolService));
        selectionWindow.Initialize();
        staticHudWindows.Add(selectionWindow);

        var playerHealthBarWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(screenSize.X - PlayerHealthBarContent.Size.X - HudMetrics.Margin.X, HudMetrics.Margin.Y),
                Size = PlayerHealthBarContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            // BorderSize left at the default (1,1) -- a thinner outset reads as a subtle bevel rather than a heavy frame.
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
        });
        playerHealthBarWindow.SetContent(new PlayerHealthBarContent(world, ecsContext.ComponentManager));
        playerHealthBarWindow.Initialize();
        staticHudWindows.Add(playerHealthBarWindow);

        var playerManaBarWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(screenSize.X - PlayerManaBarContent.Size.X - HudMetrics.Margin.X, HudMetrics.Margin.Y + PlayerHealthBarContent.Size.Y + ManaBarGap),
                Size = PlayerManaBarContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
        });
        playerManaBarWindow.SetContent(new PlayerManaBarContent(world, ecsContext.ComponentManager));
        playerManaBarWindow.Initialize();
        staticHudWindows.Add(playerManaBarWindow);

        var actionLockWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(screenSize.X - PlayerHealthBarContent.Size.X - HudMetrics.Margin.X - ActionLockContent.Size.X - ActionLockGap, HudMetrics.Margin.Y),
                Size = ActionLockContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
        });
        actionLockWindow.SetContent(new ActionLockContent(world, ecsContext.ComponentManager, presentation.FontService));
        actionLockWindow.Initialize();
        staticHudWindows.Add(actionLockWindow);

        var playerStatusEffectsWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(screenSize.X - PlayerHealthBarContent.Size.X - HudMetrics.Margin.X, HudMetrics.Margin.Y + PlayerHealthBarContent.Size.Y + ManaBarGap + PlayerManaBarContent.Size.Y),
                Size = PlayerStatusEffectsContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = false, CanUserFocus = false },
        });
        playerStatusEffectsWindow.SetContent(new PlayerStatusEffectsContent(world, ecsContext.ComponentManager, itemCatalog, presentation.FontService));
        playerStatusEffectsWindow.Initialize();
        staticHudWindows.Add(playerStatusEffectsWindow);

        // Bottom-center, overlaying the map -- StaticHUD tier draws over Base, the same way
        // selectionWindow/playerHealthBarWindow already do. HotbarContent's Size depends on the
        // player's currently-unlocked Expansion slot count, so it's constructed first and its own
        // Size read to size/position this window -- see HotbarContent.RefreshLayoutIfChanged for
        // how it keeps itself bottom-anchored/horizontally-centered as that Size changes later.
        var hotbarContent = new HotbarContent(world, mapViewState, ecsContext.ComponentManager, ecsContext.EventBus, actionCatalog, itemCatalog, presentation.FontService, presentation.SpriteSheetService, presentation.SpriteRenderer, screenSize);
        var hotbarSize = hotbarContent.Size;
        var hotbarWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HotbarContent.ComputeBottomCenteredPosition(hotbarSize, screenSize),
                Size = hotbarSize,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = false, CanUserFocus = false },
        });
        hotbarWindow.SetContent(hotbarContent);
        hotbarWindow.Initialize();
        staticHudWindows.Add(hotbarWindow);

        // TEMPORARY First concrete TextBox consumer (see the Text input TODO) -- a multiline TextBox in
        // a closeable popup that submits into a new Quest notification. "New Quest" is a
        // clickable TextWindow the same way NotificationCenter's own summary-bar entries are
        // (see NotificationCenter.Initialize's countWindow.Clicked wiring). StaticHUD tier --
        // overlays the fullscreen map, same reasoning as selectionWindow above.
        var questTriggerWindow = presentation.ElementPoolService.CreateElement<TextWindow>(null, new ElementOptions
        {
            // Left margin matches the notification count window's (HudMetrics.Margin.X).
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(HudMetrics.Margin.X, 800), Size = new Vector2(120, 30), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.LightGray },
            Text = new TextOptions { Text = "New Quest" },
        });
        questTriggerWindow.Initialize();
        staticHudWindows.Add(questTriggerWindow);

        return (staticHudWindows, questTriggerWindow, hotbarContent);
    }

    /// <summary>DynamicHUD tier: NotificationCenter owns/populates this list itself (summary bar + popups), and InventoryFolderController does the same for its own folder+window -- see UiInputController's own doc comment for what each of the four tiers means. Build also passes this same list into OpenQuestComposer later, since that popup belongs in this tier too.</summary>
    private static (List<Element> DynamicHudWindows, NotificationCenter NotificationCenter, InventoryFolderController Inventory) BuildDynamicHudWindows(PresentationContext presentation, World world, EcsContext ecsContext, ItemCatalog itemCatalog, MapWindow mapWindow)
    {
        var dynamicHudWindows = new List<Element>();

        // Folder's dependencies (SpriteSheetService/SpriteRenderer) come from Presentation the
        // same way MapWindow's do (see BuildBaseWindows) -- registered here, not inside
        // WindowService's own constructor, so window types that don't render sprites
        // (Window/TextWindow/TextBox) don't have to thread those dependencies through too.
        presentation.ElementPoolService.RegisterFactory<Folder>((_, _) => new Folder(
            presentation.FontService, presentation.ElementPoolService, presentation.GlyphRenderer, presentation.SpriteSheetService, presentation.SpriteRenderer));

        var notificationCenter = new NotificationCenter(presentation.ElementPoolService, ecsContext.EventBus, dynamicHudWindows);
        notificationCenter.Initialize();

        presentation.ElementPoolService.RegisterFactory<InventoryManagementWindow>((_, _) => new InventoryManagementWindow(
            presentation.FontService, presentation.ElementPoolService, presentation.GlyphRenderer, presentation.SpriteSheetService, presentation.SpriteRenderer,
            ecsContext.ComponentManager, itemCatalog));
        presentation.ElementPoolService.RegisterFactory<InventoryItemStackCell>((_, _) => new InventoryItemStackCell(
            presentation.FontService, presentation.ElementPoolService, presentation.GlyphRenderer, presentation.SpriteSheetService, presentation.SpriteRenderer));

        presentation.ElementPoolService.RegisterFactory<AbilityScoreWindow>((_, _) => new AbilityScoreWindow(
            presentation.FontService, presentation.ElementPoolService, presentation.GlyphRenderer, ecsContext.ComponentManager));
        presentation.ElementPoolService.RegisterFactory<AbilityScoreColumnHeader>((_, _) => new AbilityScoreColumnHeader(
            presentation.FontService, presentation.ElementPoolService, presentation.GlyphRenderer));
        presentation.ElementPoolService.RegisterFactory<AbilityScoreModifierRow>((_, _) => new AbilityScoreModifierRow(
            presentation.FontService, presentation.ElementPoolService, presentation.GlyphRenderer));

        var inventory = new InventoryFolderController(
            presentation.ElementPoolService, world, ecsContext.ComponentManager, presentation.FontService, presentation.GlyphRenderer,
            presentation.SpriteSheetService, presentation.SpriteRenderer, itemCatalog, mapWindow);
        inventory.Initialize(dynamicHudWindows);

        return (dynamicHudWindows, notificationCenter, inventory);
    }

    /// <summary>Constructs HotbarController and lets it add the Armed Hotkey Summary window into the DynamicHUD tier -- mirrors NotificationCenter/InventoryFolderController's own Initialize(dynamicHudWindows) call shape above. Needs mapViewState/hotbarContent (from BuildBaseWindows/BuildStaticHudWindows) and actionTargeting (from BuildBaseWindows, promoted there rather than built privately inside MapWindow so this can share the same instance).</summary>
    private static HotbarController BuildHotbarController(
        PresentationContext presentation, MapViewState mapViewState, HotbarContent hotbarContent, ActionTargetingController actionTargeting, List<Element> dynamicHudWindows)
    {
        var hotbarController = new HotbarController(mapViewState, hotbarContent, actionTargeting);
        hotbarController.Initialize(presentation.ElementPoolService, presentation.FontService, presentation.GlyphRenderer, dynamicHudWindows);
        return hotbarController;
    }

    /// <summary>User tier: today, just DragGhostContent's host window -- see UiInputController's own doc comment for what this tier is for. Split out from the other three Build* methods since it needs a real UiInputController reference (see Build), which doesn't exist yet while those run.</summary>
    private static void BuildUserWindows(PresentationContext presentation, UiInputController inputController, ItemCatalog itemCatalog, List<Element> userWindows)
    {
        // Zero-size and fully transparent -- DragGhostContent draws directly at the live mouse
        // position (see its own doc comment), not relative to this window's own bounds, so the
        // window itself exists only to host the content and get its DrawContent called.
        var dragGhostWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = Vector2.Zero, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        dragGhostWindow.SetContent(new DragGhostContent(
            inputController, itemCatalog, presentation.FontService, presentation.SpriteSheetService, presentation.SpriteRenderer, presentation.GlyphRenderer));
        dragGhostWindow.Initialize();
        userWindows.Add(dragGhostWindow);
    }

    /// <summary>TEMPORARYOpens a fresh closeable popup with one multiline TextBox; submitting posts a Quest notification and closes the popup. Returns the popup so the caller can focus it.</summary>
    private static Window OpenQuestComposer(ElementPoolService windowService, NotificationCenter notificationCenter, List<Element> dynamicHudWindows)
    {
        // Deliberately Fixed, not WrapContent: a WrapContent parent's ContentSize starts at
        // ~(0,0) before it's ever measured a child, and Window.Measure overwrites a child's own
        // MaximumSize with _parentElement.ContentSize on every pass -- so a WrapContent popup
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

        var popup = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(200, 250), Size = popupSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "New Quest (Enter to submit)", CanUserClose = true, CanUserMove = true },
        });
        popup.Initialize();
        dynamicHudWindows.Add(popup);

        // Pooled and reused for the next "New Quest" click (see WindowService) -- must detach
        // itself and remove the closed instance from dynamicHudWindows, the same cleanup
        // NotificationCenter.OnActiveNotificationClosed already does for its own popups, or a
        // reopened composer would eventually add the same recycled instance to
        // dynamicHudWindows twice.
        void onClosed(Element closedWindow)
        {
            closedWindow.Closed -= onClosed;
            dynamicHudWindows.Remove(closedWindow);
        }

        popup.Closed += onClosed;

        // Size.Y is only a starting point -- TextBox.AutoSizeToContent immediately shrinks it
        // to a 2-line minimum on Initialize, then grows it back up as text is typed, capped at
        // MaximumSize.Y; CanUserScrollVertical covers anything typed beyond that cap.
        var textBox = windowService.CreateElement<TextBox>(popup, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = textBoxMaximumSize, MaximumSize = textBoxMaximumSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserScrollVertical = true },
            Text = new TextOptions { Multiline = true },
        });
        // Subscribed before AddChildWindow -- Initialize (called from within AddChildWindow) is
        // what fires the first Resized, shrinking the popup down from popupSize to match the
        // TextBox's own initial 2-line height, not just later growth.
        textBox.Resized += _ => popup.SetSize(new Vector2(popup.CurrentSize.X, textBox.CurrentSize.Y + popupChromeHeight));
        textBox.TextSubmitted += text =>
        {
            // showImmediately: false -- created already minimized (queued in the Quest summary
            // count, opened later by clicking it), rather than popping up as an active window.
            notificationCenter.AddNotification(NotificationCategory.Quest, text, showImmediately: false, title: "New Quest");
            popup.Close();
        };
        popup.AddChild(textBox);

        return popup;
    }
}

public sealed record GameShellContext(
    MapWindow MapWindow,
    NotificationCenter NotificationCenter,
    InventoryFolderController Inventory,
    List<Element> BaseWindows,
    List<Element> StaticHudWindows,
    List<Element> DynamicHudWindows,
    List<Element> UserWindows,
    UiInputController InputController)
{
    public void LoadContent()
    {
        foreach (var window in BaseWindows)
        {
            window.LoadContent();
        }

        foreach (var window in StaticHudWindows)
        {
            window.LoadContent();
        }

        foreach (var window in DynamicHudWindows)
        {
            window.LoadContent();
        }

        foreach (var window in UserWindows)
        {
            window.LoadContent();
        }
    }

    public void Update(GameTime gameTime)
    {
        foreach (var window in BaseWindows)
        {
            window.Update(gameTime);
        }

        foreach (var window in StaticHudWindows)
        {
            window.Update(gameTime);
        }

        foreach (var window in DynamicHudWindows)
        {
            window.Update(gameTime);
        }

        foreach (var window in UserWindows)
        {
            window.Update(gameTime);
        }
    }

    /// <summary>Drawn bottom-to-top: Base, StaticHUD, DynamicHUD, User -- see UiInputController's own doc comment for what each tier holds. User last and unconditionally, so drag feedback is never occluded by whatever it's passing over on its way to a drop target.</summary>
    public void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        foreach (var window in BaseWindows)
        {
            window.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
        }

        foreach (var window in StaticHudWindows)
        {
            window.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
        }

        foreach (var window in DynamicHudWindows)
        {
            window.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
        }

        foreach (var window in UserWindows)
        {
            window.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
        }
    }
}
