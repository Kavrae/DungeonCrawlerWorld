using Engine.Diagnostics;
using Engine.ECS.Context;
using Engine.Events;
using Game.Bootstrap;
using Game.Modules.Movement.Components;
using Game.Notifications;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.Input.DragDrop;
using Presentation.MapRendering;
using Presentation.UI;
using Presentation.UI.AbilityScores;
using Presentation.UI.Chrome;
using Presentation.UI.Content;
using Presentation.UI.Diagnostics;
using Presentation.UI.Inventory;
using Presentation.UI.Notifications;
using UIEngine.Bootstrap;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace DungeonCrawlerWorld;

/// <summary>Builds the app's specific screen on top of the services UiEngineBootstrapper already constructed.</summary>
/// <cleanupVersion>1</cleanupVersion>>
public static class ShellBootstrapper
{
    /// <summary>Builds the game shell context.</summary>
    /// <param name="uiEngine"></param>
    /// <param name="worldSession">The game session the shell shows, and the app-owned pieces around it.</param>
    /// <param name="screenSize"></param>
    /// <param name="diagnostics">What the Diagnostics window (F3) reads its rates, gauges and leak findings from; null shows only what the window reads from the session itself.</param>
    /// <returns></returns>
    public static ShellContext Build(UiEngineContext uiEngine, WorldSessionContext worldSession, Vector2 screenSize, DiagnosticsEngine? diagnostics = null)
    {
        HudChrome.ResolveLayout(screenSize);

        var gameSession = worldSession.GameSession;
        var world = gameSession.World;
        var ecsContext = gameSession.EcsContext;
        var catalogs = gameSession.Catalogs;
        var views = gameSession.Views;
        var commands = gameSession.Commands;

        var pointerState = new PointerState();
        var shellServices = new ShellServices(
            new UiLayerStack(),
            new MapViewState { ReservedEntityIds = worldSession.ReservedEntityIds },
            new MapCamera(views.MapView),
            new ContextMenuController(new DropdownController(uiEngine.ElementPoolService)),
            new TooltipController(),
            pointerState,
            new CursorTextContent(pointerState, uiEngine.FontService, uiEngine.LabelRenderer));
        var uiLayers = shellServices.Layers;
        var mapViewState = shellServices.MapViewState;
        var contextMenuController = shellServices.ContextMenuController;
        var tooltipController = shellServices.TooltipController;

        var actionTargetingController = new ActionTargetingController(
            world,
            mapViewState,
            shellServices.Camera,
            uiLayers,
            catalogs.ActionCatalog,
            catalogs.ItemCatalog,
            views.TransformView,
            views.HotkeyBindingView,
            views.InventoryView,
            views.ActionStateView,
            views.TargetingView,
            commands.PlayerCommands,
            gameSession.SimulationClock);
        var playerMovementController = new PlayerMovementController(commands.PlayerCommands);
        var dragGhostContent = new DragGhostContent(pointerState, world, catalogs.ActionCatalog, catalogs.ItemCatalog, views.InventoryView, uiEngine.FontService, uiEngine.SpriteSheetService, uiEngine.LabelRenderer);

        ElementFactoryRegistry.RegisterAll(uiEngine, gameSession, worldSession.AdminTools, shellServices, actionTargetingController, playerMovementController);

        contextMenuController.Dropdowns.Initialize(uiLayers);
        tooltipController.Initialize(uiEngine.ElementPoolService, uiLayers);

        var mapWindow = BuildBaseWindows(uiEngine, uiLayers);
        var (questTriggerWindow, hotbarContent, inspectionWindow) = BuildStaticHudWindows(uiEngine, gameSession, screenSize, mapViewState, uiLayers);
        mapWindow.OnInspectionOpened = () => inspectionWindow.SetDisplayMode(ElementDisplayMode.Fixed);
        var (notificationCenter, healthController) = BuildDynamicHudWindows(uiEngine, world, ecsContext.EventBus, contextMenuController, uiLayers);
        var itemWindows = new ItemWindowCoordinator(uiEngine.ElementPoolService, shellServices, world, mapWindow, actionTargetingController, views.InventoryView, catalogs.ItemCatalog, catalogs.LootboxCatalog, commands.LootCommands, commands.LootboxCommands, worldSession.ReservedEntityIds);
        var hotbarController = BuildHotbarController(mapViewState, hotbarContent, actionTargetingController, tooltipController);
        BuildUserWindows(uiEngine, shellServices.CursorTextContent, dragGhostContent, uiLayers);

        var abilityScoreController = BuildAbilityScoreWindowController(uiEngine, world, views.InventoryView, itemWindows.Inventory, mapWindow, contextMenuController, uiLayers, tooltipController);
        var diagnosticsController = BuildDiagnosticsWindowController(uiEngine, ecsContext, diagnostics, uiLayers);

        // A destroyed entity's id is reused straight away, so nothing on screen may keep pointing at
        // it: whatever it was selected in, or open for, lets go before the id means someone else.
        ecsContext.EntityManager.EntityDestroying += entityId =>
        {
            if (mapViewState.InspectedEntityId == entityId)
            {
                mapViewState.InspectedEntityId = -1;
            }

            itemWindows.ReleaseEntity(entityId);
        };

        var targetingModeSwitch = new TargetingModeSwitch(mapViewState, views.ActionStateView, world, shellServices.CursorTextContent);
        IUiInputHandler[] inputHandlers =
        [
            new AdminModeInputHandler(),
            new KeyPressInputHandler(Keys.U, healthController.ToggleHealthWindow),
            new KeyPressInputHandler(Keys.I, itemWindows.Inventory.ToggleInventoryWindow),
            new KeyPressInputHandler(Keys.O, abilityScoreController.ToggleAbilityScoreWindow),
            new KeyPressInputHandler(Keys.F3, diagnosticsController.ToggleDiagnosticsWindow),
            new KeyPressInputHandler(Keys.LeftAlt, targetingModeSwitch.Switch),
            new ItemDetailsOutsideClickInputHandler(itemWindows.ItemDetails),
            new ItemComparisonDisarmInputHandler(itemWindows.ItemComparison),
            new HotbarInputHandler(hotbarController),
            new ActivationRefusalInputHandler(),
        ];
        IContentDropHandler[] contentDropHandlers =
        [
            new InventoryTransferDropHandler(mapViewState, views.ShopView, commands.InventoryCommands, commands.ShopCommands, commands.CurrencyCommands, world),
            new HotbarBindingDropHandler(hotbarContent, world),
        ];
        var inputController = new UiInputController(uiLayers, screenSize, pointerState, inputHandlers, contentDropHandlers, contextMenuController.Dropdowns);
        inputController.SetDefaultFocusElement(mapWindow);
        inputController.FocusElement(mapWindow);

        // See MapWindow.IsTextInputFocused's own doc comment -- Space must not pause the game
        // while a TextBox (search box, Quest Composer, ...) is focused and receiving the space
        // character as ordinary typed text.
        mapWindow.IsTextInputFocused = () => inputController.IsTextBoxFocused;

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
        questTriggerWindow.Clicked += _ => inputController.FocusElement(OpenQuestComposer(uiEngine.ElementPoolService, notificationCenter, uiLayers));

        return new ShellContext(mapWindow, notificationCenter, itemWindows.Inventory, abilityScoreController, uiLayers, inputController, new UiLayerHost(uiLayers, uiEngine.ElementPoolService));
    }

    /// <summary>Base tier: the map itself plus the debug stats footer directly beneath it -- see UiInputController's own doc comment for what each of the four tiers means. MapWindow's own factory (and every other pooled type's) is already registered by the time this runs -- see Build's ElementFactoryRegistry.RegisterAll call.</summary>
    private static MapWindow BuildBaseWindows(
        UiEngineContext uiEngine, UiLayerStack layers)
    {
        var mapWindow = uiEngine.ElementPoolService.CreateElement<MapWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HudChrome.MapWindowPosition,
                Size = HudChrome.MapWindowSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowBorder = true,
                ShowTitle = false,
                CanUserScrollHorizontal = true,
                CanUserScrollVertical = true,
                ScrollbarVisibility = ScrollbarVisibility.Hidden,
            },
        });
        mapWindow.Initialize();
        layers.Add(UiLayer.Base, mapWindow);

        return mapWindow;
    }

    /// <summary>StaticHUD tier: the player health bar, action lock, status effects, InspectionWindow, the hotbar, and the quest trigger -- see UiInputController's own doc comment for what each of the four tiers means. questTriggerWindow is returned for Build, which wires its Clicked event once the DynamicHUD tier (needed by OpenQuestComposer) also exists. hotbarContent and inspectionWindow are returned too, for BuildHotbarController and Build's own OnInspectionOpened wiring respectively.</summary>
    private static (TextWindow QuestTriggerWindow, HotbarContent HotbarContent, InspectionWindow InspectionWindow) BuildStaticHudWindows(
        UiEngineContext uiEngine, GameSession gameSession, Vector2 screenSize, MapViewState mapViewState, UiLayerStack layers)
    {
        var world = gameSession.World;
        var ecsContext = gameSession.EcsContext;
        var catalogs = gameSession.Catalogs;
        var mapView = gameSession.Views.MapView;
        var simulationClock = gameSession.SimulationClock;

        var playerHealthBarWindow = uiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HudChrome.PlayerHealthBarPosition,
                Size = PlayerHealthBarContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            // BorderSize left at the default (1,1) -- a thinner outset reads as a subtle bevel rather than a heavy frame.
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
        });
        playerHealthBarWindow.SetContent(new PlayerHealthBarContent(world, gameSession.Views.HealthView, gameSession.Views.EntityBodyParts, gameSession.Views.StatModifierView, uiEngine.FontService, layers));
        playerHealthBarWindow.Initialize();
        layers.Add(UiLayer.StaticHud, playerHealthBarWindow);

        var playerManaBarWindow = uiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HudChrome.PlayerManaBarPosition,
                Size = PlayerManaBarContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
        });
        playerManaBarWindow.SetContent(new PlayerManaBarContent(world, gameSession.Views.ActionStateView, gameSession.Views.StatModifierView, uiEngine.FontService));
        playerManaBarWindow.Initialize();
        layers.Add(UiLayer.StaticHud, playerManaBarWindow);

        var actionLockWindow = uiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HudChrome.ActionLockPosition,
                Size = ActionLockContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
        });
        actionLockWindow.SetContent(new ActionLockContent(world, gameSession.Views.ActionStateView, mapView, uiEngine.FontService, simulationClock));
        actionLockWindow.Initialize();
        layers.Add(UiLayer.StaticHud, actionLockWindow);

        var playerStatusEffectsWindow = uiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HudChrome.PlayerStatusEffectsPosition,
                Size = PlayerStatusEffectsContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = false, CanUserFocus = false },
        });
        playerStatusEffectsWindow.SetContent(new PlayerStatusEffectsContent(world, gameSession.Views.ActionStateView, catalogs.ItemCatalog, uiEngine.FontService, catalogs.StatusEffectDisplays, simulationClock));
        playerStatusEffectsWindow.Initialize();
        layers.Add(UiLayer.StaticHud, playerStatusEffectsWindow);

        // Right-aligned column, directly beneath the status effects row -- see HudChrome.
        // ResolveLayout's own MinimapReserve constant for why headroom is left above the
        // hotbar's worst-case (fully expanded) top edge. Same width as the health bar
        // (PlayerHealthBarContent.Size.X), per the Inspection V2 request.
        var inspectionWindow = uiEngine.ElementPoolService.CreateElement<InspectionWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HudChrome.InspectionWindowPosition,
                Size = HudChrome.InspectionWindowSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                ShowTitleWhenMinimized = true,
                CanUserClose = false,
                CanUserMinimize = true,
                CanUserScrollVertical = true,
            },
        });
        inspectionWindow.SetContent(new InspectionWindowContent(world, mapView, mapViewState, ecsContext.ComponentManager, ecsContext.EntityManager, uiEngine.ElementPoolService, catalogs.Definitions, gameSession.Internals.SpawnRecordRebuilder, gameSession.Internals.Skeletons));
        inspectionWindow.Initialize();
        layers.Add(UiLayer.StaticHud, inspectionWindow);

        // Bottom-center, overlaying the map -- StaticHUD tier draws over Base, the same way
        // selectionWindow/playerHealthBarWindow already do. HotbarContent's Size depends on the
        // player's currently-unlocked Expansion slot count, so it's constructed first and its own
        // Size read to size/position this window -- see HotbarContent.RefreshLayoutIfChanged for
        // how it keeps itself bottom-anchored/horizontally-centered as that Size changes later.
        var hotbarContent = new HotbarContent(world, mapViewState, gameSession.Views.HotkeyBindingView, gameSession.Views.InventoryView, gameSession.Views.ActionStateView, gameSession.Commands.HotkeyBindingCommands, catalogs.ActionCatalog, catalogs.ItemCatalog, uiEngine.FontService, uiEngine.SpriteSheetService, screenSize, simulationClock, gameSession.Views.EntityActions, catalogs.GameplayTags);
        var hotbarSize = hotbarContent.Size;
        var hotbarWindow = uiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = HotbarContent.ComputeBottomCenteredPosition(hotbarSize, screenSize),
                Size = hotbarSize,
                MaximumSize = HotbarContent.MaximumSize,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = false, CanUserFocus = false },
        });
        hotbarWindow.SetContent(hotbarContent);
        hotbarWindow.Initialize();
        layers.Add(UiLayer.StaticHud, hotbarWindow);
        layers.MarkMenuModeExempt(hotbarWindow);

        // TEMPORARY First concrete TextBox consumer (see the Text input TODO) -- a multiline TextBox in
        // a closeable popup that submits into a new Quest notification. "New Quest" is a
        // clickable TextWindow the same way NotificationCenter's own summary-bar entries are
        // (see NotificationCenter.Initialize's countWindow.Clicked wiring). StaticHUD tier --
        // overlays the fullscreen map, same reasoning as selectionWindow above.
        var questTriggerWindow = uiEngine.ElementPoolService.CreateElement<TextWindow>(null, new ElementOptions
        {
            // Left margin matches the notification count window's (HudChrome.Margin.X).
            Layout = new ElementLayoutOptions { RelativePosition = HudChrome.QuestTriggerWindowPosition, Size = HudChrome.QuestTriggerWindowSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.LightGray },
            Text = new TextOptions { Text = "New Quest" },
        });
        questTriggerWindow.Initialize();
        layers.Add(UiLayer.StaticHud, questTriggerWindow);

        return (questTriggerWindow, hotbarContent, inspectionWindow);
    }

    /// <summary>DynamicHUD tier: NotificationCenter owns/populates its own folder+popups, and HealthWindowController its own button+window (both add to UiLayer.DynamicHud specifically) -- see UiLayer's own doc comment for what each tier means. The inventory button follows from ItemWindowCoordinator, built right after, so it keeps its place beneath the health button. Build also passes the same layer stack into OpenQuestComposer later, since that popup belongs in DynamicHud too.</summary>
    private static (NotificationCenter NotificationCenter, HealthWindowController Health) BuildDynamicHudWindows(UiEngineContext uiEngine, World world, EventBus eventBus, ContextMenuController contextMenuController, UiLayerStack layers)
    {
        var notificationCenter = new NotificationCenter(uiEngine.ElementPoolService, eventBus, layers, contextMenuController);
        notificationCenter.Initialize();

        var health = new HealthWindowController(uiEngine.ElementPoolService, world);
        health.Initialize(layers);

        return (notificationCenter, health);
    }

    /// <summary>Built after InventoryWindowController (whose PlayerInventoryWindow accessor this abilityScoreController reads to cascade its own window beside a live Inventory window -- see AbilityScoreWindowController.CreateAbilityScoreWindow) and MapWindow.</summary>
    private static AbilityScoreWindowController BuildAbilityScoreWindowController(
        UiEngineContext uiEngine, World world, InventoryView inventoryView, InventoryWindowController inventory, MapWindow mapWindow, ContextMenuController contextMenuController, UiLayerStack layers, TooltipController tooltipController)
    {
        var abilityScoreController = new AbilityScoreWindowController(uiEngine.ElementPoolService, world, inventoryView, inventory, mapWindow, contextMenuController, tooltipController);
        abilityScoreController.Initialize(layers);
        return abilityScoreController;
    }

    /// <summary>Registers the Diagnostics window's factory here rather than in ElementFactoryRegistry, since only this method has the DiagnosticsEngine it reads.</summary>
    private static DiagnosticsWindowController BuildDiagnosticsWindowController(
        UiEngineContext uiEngine, EcsContext ecsContext, DiagnosticsEngine? diagnostics, UiLayerStack layers)
    {
        var elementPool = uiEngine.ElementPoolService;
        elementPool.RegisterFactory<DiagnosticsWindow>(() => new DiagnosticsWindow(
            uiEngine.FontService, elementPool, uiEngine.LabelRenderer, ecsContext.EntityManager, ecsContext.ComponentManager.GetPackedPool<MovementComponent>(), ecsContext.SystemManager.Clock, diagnostics));
        return new DiagnosticsWindowController(elementPool, layers);
    }

    /// <summary>Constructs HotbarController -- its Armed Hotkey Summary popup is shown/hidden through the shared TooltipController (built once at the top of Build), not a window of its own. Needs mapViewState/hotbarContent (from BuildStaticHudWindows) and actionTargetingController (constructed at the top of Build, shared with MapWindow's own factory).</summary>
    private static HotbarController BuildHotbarController(
        MapViewState mapViewState, HotbarContent hotbarContent, ActionTargetingController actionTargeting, TooltipController tooltipController) =>
        new(mapViewState, hotbarContent, actionTargeting, tooltipController);

    /// <summary>User tier: hosts cursorTextContent/dragGhostContent, both drawn at the pointer through PointerState -- see UiLayer's own doc comment for what this tier is for.</summary>
    private static void BuildUserWindows(UiEngineContext uiEngine, CursorTextContent cursorTextContent, DragGhostContent dragGhostContent, UiLayerStack layers)
    {
        // Zero-size and fully transparent -- DragGhostContent draws directly at the live mouse
        // position (see its own doc comment), not relative to this window's own bounds, so the
        // window itself exists only to host the content and get its DrawContent called.
        var dragGhostWindow = uiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = Vector2.Zero, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        dragGhostWindow.SetContent(dragGhostContent);
        dragGhostWindow.Initialize();
        layers.Add(UiLayer.User, dragGhostWindow);

        // Same hosting shape as dragGhostWindow above -- see CursorTextContent's own doc comment
        // for why it's built the same way DragGhostContent is.
        var cursorTextWindow = uiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = Vector2.Zero, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        cursorTextWindow.SetContent(cursorTextContent);
        cursorTextWindow.Initialize();
        layers.Add(UiLayer.User, cursorTextWindow);
    }

    /// <summary>TEMPORARYOpens a fresh closeable popup with one multiline TextBox; submitting posts a Quest notification and closes the popup. Returns the popup so the caller can focus it.</summary>
    private static Window OpenQuestComposer(ElementPoolService windowService, NotificationCenter notificationCenter, UiLayerStack layers)
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
        layers.Add(UiLayer.DynamicHud, popup);

        // Pooled and reused for the next "New Quest" click (see WindowService) -- must detach
        // itself and remove the closed instance from uiLayers, the same cleanup
        // NotificationCenter.OnActiveNotificationClosed already does for its own popups, or a
        // reopened composer would eventually add the same recycled instance to
        // UiLayer.DynamicHud twice.
        void onClosed(Element closedWindow)
        {
            closedWindow.Closed -= onClosed;
            layers.Remove(UiLayer.DynamicHud, closedWindow);
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

public sealed record ShellContext(
    MapWindow MapWindow,
    NotificationCenter NotificationCenter,
    InventoryWindowController Inventory,
    AbilityScoreWindowController AbilityScore,
    UiLayerStack Layers,
    UiInputController InputController,
    UiLayerHost LayerHost)
{
    /// <summary>
    /// Must run before GameLoop's pause check reads MapWindow.IsPaused/Layers.IsMenuModeActive,
    /// and before EcsContext.Update -- not folded into the (later-running) Update below.
    /// InputController.Update is what processes the Space key that toggles MapWindow.IsPaused;
    /// NotificationCenter.Update is what drains a buffered NotificationRequestedEvent into a real
    /// System notification that calls OpenMenuWindow (see its own doc comment). Either one running
    /// after that pause check instead would mean the trigger and the actual pause landed on
    /// different frames -- one more full EcsContext.Update tick running after the player pressed
    /// Space, or after a blocking notification fired, before the world actually stops.
    /// </summary>
    public void PreSimulationUpdate()
    {
        InputController.Update();
        NotificationCenter.Update();
    }

    /// <summary>
    /// The window tree's own per-frame Update, plus Inventory's/AbilityScore's own (their button's
    /// Enabled state depends on player state EcsContext.Update can change -- see
    /// InventoryWindowController.Update/AbilityScoreWindowController.Update -- so it belongs here,
    /// after simulation, not in PreSimulationUpdate) -- deliberately run after GameLoop's own
    /// EcsContext.Update, so windows reflect this frame's simulation results rather than last
    /// frame's.
    /// </summary>
    public void Update(GameTime gameTime)
    {
        Inventory.Update();
        AbilityScore.Update();

        LayerHost.Update(gameTime);
    }

    /// <summary>Every layer bottom to top, with the menu-mode dim -- see UiLayerHost.</summary>
    public void Draw(GameTime gameTime) => LayerHost.Draw(gameTime);
}
