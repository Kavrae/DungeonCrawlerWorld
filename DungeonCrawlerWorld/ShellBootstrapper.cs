using Engine.Diagnostics;
using Engine.ECS.Context;
using Engine.Events;
using Game.Bootstrap;
using Game.Modules.Movement.Components;
using Game.Notifications;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Bootstrap;
using Presentation.Input;
using Presentation.UI;
using Presentation.UI.AbilityScores;
using Presentation.UI.Chrome;
using Presentation.UI.Content;
using Presentation.UI.Diagnostics;
using Presentation.UI.Inventory;
using Presentation.UI.Notifications;

namespace DungeonCrawlerWorld;

/// <summary>Builds the app's specific screen on top of the services PresentationBootstrapper already constructed.</summary>
/// <cleanupVersion>1</cleanupVersion>>
public static class ShellBootstrapper
{
    /// <summary>Builds the game shell context.</summary>
    /// <param name="presentation"></param>
    /// <param name="worldSession">The game session the shell shows, and the app-owned pieces around it.</param>
    /// <param name="screenSize"></param>
    /// <param name="diagnostics">What the Diagnostics window (F3) reads its rates, gauges and leak findings from; null shows only what the window reads from the session itself.</param>
    /// <returns></returns>
    public static ShellContext Build(PresentationContext presentation, WorldSessionContext worldSession, Vector2 screenSize, DiagnosticsEngine? diagnostics = null)
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
            new MapCamera(world),
            new ContextMenuController(presentation.ElementPoolService),
            new TooltipController(),
            pointerState,
            new CursorTextContent(pointerState, presentation.FontService, presentation.LabelRenderer));
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
            views.AbilityScoreView,
            commands.PlayerCommands,
            gameSession.SimulationClock);
        var playerMovementController = new PlayerMovementController(commands.PlayerCommands);
        var dragGhostContent = new DragGhostContent(pointerState, world, catalogs.ActionCatalog, catalogs.ItemCatalog, views.InventoryView, presentation.FontService, presentation.SpriteSheetService, presentation.SpriteRenderer, presentation.LabelRenderer);

        ElementFactoryRegistry.RegisterAll(presentation, gameSession, worldSession.AdminTools, shellServices, actionTargetingController, playerMovementController);

        contextMenuController.Initialize(uiLayers);
        tooltipController.Initialize(presentation.ElementPoolService, uiLayers);

        var mapWindow = BuildBaseWindows(presentation, uiLayers);
        var (questTriggerWindow, hotbarContent, inspectionWindow) = BuildStaticHudWindows(presentation, gameSession, screenSize, mapViewState, uiLayers);
        mapWindow.OnInspectionOpened = () => inspectionWindow.SetDisplayMode(ElementDisplayMode.Fixed);
        var (notificationCenter, healthController) = BuildDynamicHudWindows(presentation, world, ecsContext.EventBus, contextMenuController, uiLayers);
        var itemWindows = new ItemWindowCoordinator(presentation.ElementPoolService, shellServices, world, mapWindow, actionTargetingController, views.InventoryView, catalogs.ItemCatalog, catalogs.LootboxCatalog, commands.LootCommands, commands.LootboxCommands, worldSession.ReservedEntityIds);
        var hotbarController = BuildHotbarController(mapViewState, hotbarContent, actionTargetingController, tooltipController);
        BuildUserWindows(presentation, shellServices.CursorTextContent, dragGhostContent, uiLayers);

        var abilityScoreController = BuildAbilityScoreWindowController(presentation, world, views.InventoryView, itemWindows.Inventory, mapWindow, contextMenuController, uiLayers, tooltipController);
        var diagnosticsController = BuildDiagnosticsWindowController(presentation, ecsContext, diagnostics, uiLayers);

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

        var inputController = new UiInputController(uiLayers, screenSize, pointerState, views.ShopView, world, commands.InventoryCommands, commands.ShopCommands, commands.CurrencyCommands, hotbarController, contextMenuController, itemWindows.ItemDetails, itemWindows.ItemComparison, mapViewState, healthController, itemWindows.Inventory, abilityScoreController, diagnosticsController);
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
        questTriggerWindow.Clicked += _ => inputController.FocusElement(OpenQuestComposer(presentation.ElementPoolService, notificationCenter, uiLayers));

        return new ShellContext(mapWindow, notificationCenter, itemWindows.Inventory, abilityScoreController, uiLayers, inputController);
    }

    /// <summary>Base tier: the map itself plus the debug stats footer directly beneath it -- see UiInputController's own doc comment for what each of the four tiers means. MapWindow's own factory (and every other pooled type's) is already registered by the time this runs -- see Build's ElementFactoryRegistry.RegisterAll call.</summary>
    private static MapWindow BuildBaseWindows(
        PresentationContext presentation, UiLayerStack layers)
    {
        var mapWindow = presentation.ElementPoolService.CreateElement<MapWindow>(null, new ElementOptions
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
        PresentationContext presentation, GameSession gameSession, Vector2 screenSize, MapViewState mapViewState, UiLayerStack layers)
    {
        var world = gameSession.World;
        var ecsContext = gameSession.EcsContext;
        var catalogs = gameSession.Catalogs;
        var mapView = gameSession.Views.MapView;
        var simulationClock = gameSession.SimulationClock;

        var playerHealthBarWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
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
        playerHealthBarWindow.SetContent(new PlayerHealthBarContent(world, gameSession.Views.HealthView, gameSession.Views.EntityBodyParts, gameSession.Views.StatModifierView, presentation.FontService, layers));
        playerHealthBarWindow.Initialize();
        layers.Add(UiLayer.StaticHud, playerHealthBarWindow);

        var playerManaBarWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
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
        playerManaBarWindow.SetContent(new PlayerManaBarContent(world, gameSession.Views.ActionStateView, gameSession.Views.StatModifierView, presentation.FontService));
        playerManaBarWindow.Initialize();
        layers.Add(UiLayer.StaticHud, playerManaBarWindow);

        var actionLockWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
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
        actionLockWindow.SetContent(new ActionLockContent(world, gameSession.Views.ActionStateView, mapView, presentation.FontService, simulationClock));
        actionLockWindow.Initialize();
        layers.Add(UiLayer.StaticHud, actionLockWindow);

        var playerStatusEffectsWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
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
        playerStatusEffectsWindow.SetContent(new PlayerStatusEffectsContent(world, gameSession.Views.ActionStateView, catalogs.ItemCatalog, presentation.FontService, catalogs.StatusEffectDisplays, simulationClock));
        playerStatusEffectsWindow.Initialize();
        layers.Add(UiLayer.StaticHud, playerStatusEffectsWindow);

        // Right-aligned column, directly beneath the status effects row -- see HudChrome.
        // ResolveLayout's own MinimapReserve constant for why headroom is left above the
        // hotbar's worst-case (fully expanded) top edge. Same width as the health bar
        // (PlayerHealthBarContent.Size.X), per the Inspection V2 request.
        var inspectionWindow = presentation.ElementPoolService.CreateElement<InspectionWindow>(null, new ElementOptions
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
        inspectionWindow.SetContent(new InspectionWindowContent(world, mapView, mapViewState, ecsContext.ComponentManager, ecsContext.EntityManager, presentation.ElementPoolService, catalogs.Definitions, gameSession.Internals.SpawnRecordRebuilder, gameSession.Internals.Skeletons));
        inspectionWindow.Initialize();
        layers.Add(UiLayer.StaticHud, inspectionWindow);

        // Bottom-center, overlaying the map -- StaticHUD tier draws over Base, the same way
        // selectionWindow/playerHealthBarWindow already do. HotbarContent's Size depends on the
        // player's currently-unlocked Expansion slot count, so it's constructed first and its own
        // Size read to size/position this window -- see HotbarContent.RefreshLayoutIfChanged for
        // how it keeps itself bottom-anchored/horizontally-centered as that Size changes later.
        var hotbarContent = new HotbarContent(world, mapViewState, gameSession.Views.HotkeyBindingView, gameSession.Views.InventoryView, gameSession.Views.ActionStateView, gameSession.Commands.HotkeyBindingCommands, catalogs.ActionCatalog, catalogs.ItemCatalog, presentation.FontService, presentation.SpriteSheetService, presentation.SpriteRenderer, screenSize, simulationClock, gameSession.Views.EntityActions);
        var hotbarSize = hotbarContent.Size;
        var hotbarWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
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
        var questTriggerWindow = presentation.ElementPoolService.CreateElement<TextWindow>(null, new ElementOptions
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
    private static (NotificationCenter NotificationCenter, HealthWindowController Health) BuildDynamicHudWindows(PresentationContext presentation, World world, EventBus eventBus, ContextMenuController contextMenuController, UiLayerStack layers)
    {
        var notificationCenter = new NotificationCenter(presentation.ElementPoolService, eventBus, layers, contextMenuController);
        notificationCenter.Initialize();

        var health = new HealthWindowController(presentation.ElementPoolService, world);
        health.Initialize(layers);

        return (notificationCenter, health);
    }

    /// <summary>Built after InventoryWindowController (whose PlayerInventoryWindow accessor this abilityScoreController reads to cascade its own window beside a live Inventory window -- see AbilityScoreWindowController.CreateAbilityScoreWindow) and MapWindow.</summary>
    private static AbilityScoreWindowController BuildAbilityScoreWindowController(
        PresentationContext presentation, World world, InventoryView inventoryView, InventoryWindowController inventory, MapWindow mapWindow, ContextMenuController contextMenuController, UiLayerStack layers, TooltipController tooltipController)
    {
        var abilityScoreController = new AbilityScoreWindowController(presentation.ElementPoolService, world, inventoryView, inventory, mapWindow, contextMenuController, tooltipController);
        abilityScoreController.Initialize(layers);
        return abilityScoreController;
    }

    /// <summary>Registers the Diagnostics window's factory here rather than in ElementFactoryRegistry, since only this method has the DiagnosticsEngine it reads.</summary>
    private static DiagnosticsWindowController BuildDiagnosticsWindowController(
        PresentationContext presentation, EcsContext ecsContext, DiagnosticsEngine? diagnostics, UiLayerStack layers)
    {
        var elementPool = presentation.ElementPoolService;
        elementPool.RegisterFactory<DiagnosticsWindow>(() => new DiagnosticsWindow(
            presentation.FontService, elementPool, presentation.LabelRenderer, ecsContext.EntityManager, ecsContext.ComponentManager.GetPackedPool<MovementComponent>(), ecsContext.SystemManager.Clock, diagnostics));
        return new DiagnosticsWindowController(elementPool, layers);
    }

    /// <summary>Constructs HotbarController -- its Armed Hotkey Summary popup is shown/hidden through the shared TooltipController (built once at the top of Build), not a window of its own. Needs mapViewState/hotbarContent (from BuildStaticHudWindows) and actionTargetingController (constructed at the top of Build, shared with MapWindow's own factory).</summary>
    private static HotbarController BuildHotbarController(
        MapViewState mapViewState, HotbarContent hotbarContent, ActionTargetingController actionTargeting, TooltipController tooltipController) =>
        new(mapViewState, hotbarContent, actionTargeting, tooltipController);

    /// <summary>User tier: hosts cursorTextContent/dragGhostContent, both drawn at the pointer through PointerState -- see UiLayer's own doc comment for what this tier is for.</summary>
    private static void BuildUserWindows(PresentationContext presentation, CursorTextContent cursorTextContent, DragGhostContent dragGhostContent, UiLayerStack layers)
    {
        // Zero-size and fully transparent -- DragGhostContent draws directly at the live mouse
        // position (see its own doc comment), not relative to this window's own bounds, so the
        // window itself exists only to host the content and get its DrawContent called.
        var dragGhostWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = Vector2.Zero, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        dragGhostWindow.SetContent(dragGhostContent);
        dragGhostWindow.Initialize();
        layers.Add(UiLayer.User, dragGhostWindow);

        // Same hosting shape as dragGhostWindow above -- see CursorTextContent's own doc comment
        // for why it's built the same way DragGhostContent is.
        var cursorTextWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
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
    UiInputController InputController)
{
    /// <summary>
    /// Per-Draw-call scratch state for the dim-overlay pass -- recomputed/reset at the top of
    /// every Draw, read (and, for _dimDrawn, mutated) by DrawWindowLayer/DrawWindow across that
    /// same call's layer loop. Fields rather than values threaded through DrawWindowLayer's own
    /// parameters/return value: _dimDrawn in particular used to be passed in and returned back
    /// out on every call, purely so the next layer's call could see whether a previous one had
    /// already drawn the quad -- an accumulator, just expressed awkwardly as a threaded return
    /// value instead of the single flag it actually is.
    /// </summary>
    private Element? _bottommostMenuWindow;

    private List<Element> _menuModeExemptElements = [];

    private bool _dimDrawn;

    /// <summary>
    /// The render services every Update/Draw call needs -- captured once by
    /// LoadContent (see its own doc comment for why that's the right hook) rather than threaded
    /// through every Update/Draw call, since all three are session-lifetime-stable in real usage:
    /// GraphicsDevice never changes reference for this app; SpriteBatchRenderer hands back the
    /// same single SpriteBatch instance on every call (see its own doc comment); and the unit-pixel
    /// Texture2D is created once in GameLoop.LoadContent. None of the three are ever expected to vary
    /// call-to-call the way e.g. GameTime or which layer is being drawn do.
    /// </summary>
    private GraphicsDevice _graphicsDevice = null!;

    private SpriteBatch _spriteBatch = null!;

    private Texture2D _unitRectangle = null!;

    /// <summary>
    /// Captures the session-stable render services (see their own field doc comment)
    /// and lets every window LoadContent, in that order -- called once, from GameLoop.LoadContent,
    /// after GraphicsDevice/unitRectangle/the shared SpriteBatch all already exist.
    /// </summary>
    public void LoadContent(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        _graphicsDevice = graphicsDevice;
        _spriteBatch = spriteBatch;
        _unitRectangle = unitRectangle;

        foreach (var layer in UiLayerStack.LayersAscending())
        {
            foreach (var window in Layers[layer])
            {
                window.LoadContent();
            }
        }
    }

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

        foreach (var layer in UiLayerStack.LayersAscending())
        {
            UpdateWindowLayer(Layers[layer], layer.ToString(), gameTime);
        }
    }

    /// <summary>Drawn bottom-to-top, UiLayer's own declaration order -- see its doc comment for what each tier holds. User (topmost) draws last and unconditionally, so drag feedback is never occluded by whatever it's passing over on its way to a drop target. A dim overlay is drawn immediately beneath Layers.BottommostMenuWindow, if menu mode is currently active -- see UiLayerStack's own doc comment on OpenMenuWindow/CloseMenuWindow. Every element UiLayerStack.IsMenuModeExempt opted in (the hotbar, the Notification folder, the Health/Inventory/Ability Score buttons) is pulled out of its ordinary draw slot and redrawn immediately above that same dim quad instead (see FindMenuModeExemptElements) -- they stay reachable for input while menu mode is active (UiInputController.TryHitTestInteraction), so they need to read as visually usable too, not look identical to the rest of the dimmed HUD.</summary>
    public void Draw(GameTime gameTime)
    {
        _bottommostMenuWindow = Layers.BottommostMenuWindow;
        _menuModeExemptElements = _bottommostMenuWindow is null ? [] : FindMenuModeExemptElements();
        _dimDrawn = _bottommostMenuWindow is null;

        foreach (var layer in UiLayerStack.LayersAscending())
        {
            DrawWindowLayer(Layers[layer], layer.ToString(), gameTime);
        }
    }

    /// <summary>Every element UiLayerStack.IsMenuModeExempt currently considers exempt, across every layer, preserving normal ascending draw order among themselves (so e.g. the hotbar and a folder tile keep whatever relative stacking they'd otherwise have). Expected to stay a short list -- see UiLayerStack's own field doc comment on why the exempt set is meant to stay small and deliberately curated.</summary>
    private List<Element> FindMenuModeExemptElements()
    {
        List<Element> exempt = [];
        foreach (var layer in UiLayerStack.LayersAscending())
        {
            foreach (var element in Layers[layer])
            {
                if (Layers.IsMenuModeExempt(element))
                {
                    exempt.Add(element);
                }
            }
        }

        return exempt;
    }

    private void UpdateWindowLayer(IReadOnlyList<Element> windows, string tierName, GameTime gameTime)
    {
        foreach (var window in windows.ToArray())
        {
            using (EngineHooks.FrameCost(FrameCostCategory.Update, tierName, window.GetType().Name))
            {
                window.Update(gameTime);
            }
        }
    }

    /// <summary>Draws one layer's windows, consulting/mutating the per-Draw-call _dimDrawn/_bottommostMenuWindow/_menuModeExemptElements fields (reset at the top of Draw) instead of threading them through parameters and a return value. _menuModeExemptElements (empty unless menu mode is active) are skipped here in their ordinary draw slot -- drawing one there, before the dim quad, would just get it covered by that same quad -- and instead drawn once each, immediately after the dim, the moment this loop reaches _bottommostMenuWindow's own layer.</summary>
    private void DrawWindowLayer(IReadOnlyList<Element> windows, string tierName, GameTime gameTime)
    {
        foreach (var window in windows)
        {
            if (_menuModeExemptElements.Contains(window))
            {
                continue;
            }

            if (!_dimDrawn && window == _bottommostMenuWindow)
            {
                MenuModeDimRenderer.Draw(_spriteBatch, _unitRectangle, _graphicsDevice);
                _dimDrawn = true;

                foreach (var exemptElement in _menuModeExemptElements)
                {
                    DrawWindow(exemptElement, gameTime, "MenuModeExempt");
                }
            }

            DrawWindow(window, gameTime, tierName);
        }
    }

    private void DrawWindow(Element window, GameTime gameTime, string tierName)
    {
        using (EngineHooks.FrameCost(FrameCostCategory.Draw, tierName, window.GetType().Name))
        {
            window.Draw(gameTime);
        }
    }
}
