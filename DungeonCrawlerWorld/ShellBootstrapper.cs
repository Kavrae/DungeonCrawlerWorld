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
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Presentation.UI.Looting;
using Presentation.UI.Notifications;
using System.Diagnostics;

namespace DungeonCrawlerWorld;

/// <summary>Builds the app's specific screen on top of the services PresentationBootstrapper already constructed.</summary>
/// <cleanupVersion>1</cleanupVersion>>
public static class ShellBootstrapper
{
    private const float DebugWindowHeight = 24f;
    private const float ActionLockGap = 8f;
    private const float ManaBarGap = 3f;
    private const float InspectionWindowGap = 8f;

    /// <summary>Empty headroom left between InspectionWindow's bottom edge and the hotbar's worst-case (fully expanded) top edge -- no minimap exists yet, this just keeps the corner free for one, per the Inspection V2 request.</summary>
    private const float MinimapReserve = 140f;

    /// <summary>Builds the game shell context.</summary>
    /// <param name="presentation"></param>
    /// <param name="world"></param>
    /// <param name="ecsContext"></param>
    /// <param name="actionCatalog"></param>
    /// <param name="itemCatalog"></param>
    /// <param name="screenSize"></param>
    /// <param name="diagnostics">Null when no diagnostics feature is enabled -- see DebugWindowContent's own doc comment.</param>
    /// <returns></returns>
    public static ShellContext Build(PresentationContext presentation, World world, EcsContext ecsContext, ActionCatalog actionCatalog, ItemCatalog itemCatalog, Vector2 screenSize, DiagnosticsEngine? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ecsContext);
        ArgumentNullException.ThrowIfNull(actionCatalog);
        ArgumentNullException.ThrowIfNull(itemCatalog);

        var layers = new UiLayerStack();
        var componentManager = ecsContext.ComponentManager;
        var mapViewState = new MapViewState();
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

        //TODO look at pulling these contents into a new context if it continues to grow.
        var cursorTextContent = new CursorTextContent(presentation.FontService, presentation.GlyphRenderer);
        var dragGhostContent = new DragGhostContent(world, actionCatalog, itemCatalog, componentManager.GetMultiPool<InventoryItemStackComponent>(), presentation.FontService, presentation.SpriteSheetService, presentation.SpriteRenderer, presentation.GlyphRenderer);
        var contextMenuController = new ContextMenuController(presentation.ElementPoolService);

        ElementFactoryRegistry.RegisterAll(presentation, ecsContext, actionCatalog, itemCatalog, world, mapViewState, camera, actionTargeting, playerMovement, cursorTextContent, contextMenuController);

        contextMenuController.Initialize(layers);

        var mapWindow = BuildBaseWindows(presentation, ecsContext, screenSize, diagnostics, mapViewState, layers);
        var (questTriggerWindow, hotbarContent, inspectionWindow) = BuildStaticHudWindows(presentation, world, ecsContext, actionCatalog, itemCatalog, screenSize, mapViewState, layers);
        var (notificationCenter, inventory) = BuildDynamicHudWindows(presentation, world, ecsContext, itemCatalog, mapWindow, layers);
        var hotbarController = BuildHotbarController(presentation, mapViewState, hotbarContent, actionTargeting, layers);
        BuildUserWindows(presentation, cursorTextContent, dragGhostContent, layers);

        var secondaryInventory = BuildSecondaryInventoryWindowController(presentation, ecsContext, inventory, layers);
        mapWindow.OnCorpseClicked = secondaryInventory.OpenLoot;
        mapWindow.OnInspectionOpened = () => inspectionWindow.SetDisplayMode(ElementDisplayMode.Fixed);

        var inputController = new UiInputController(layers, screenSize, hotbarController, componentManager, world, contextMenuController);
        inputController.SetDefaultFocusElement(mapWindow);
        inputController.FocusElement(mapWindow);

        // See MapWindow.IsTextInputFocused's own doc comment -- Space must not pause the game
        // while a TextBox (search box, Quest Composer, ...) is focused and receiving the space
        // character as ordinary typed text.
        mapWindow.IsTextInputFocused = () => inputController.IsTextBoxFocused;

        // cursorTextContent/dragGhostContent were built before inputController existed (see
        // above) -- these two delegate assignments are what actually connects them to live input
        // state, the same late-binding shape IsTextInputFocused above already uses.
        cursorTextContent.GetCursorPosition = () => inputController.CurrentMousePosition;
        dragGhostContent.GetState = () => new DragGhostState(
            inputController.ContentDragGhostVisible,
            inputController.ContentDragItemStackInstanceId,
            inputController.ContentDragMergedItemDefinitionId,
            inputController.ContentDragActionId,
            inputController.ContentDragOriginEntityId,
            inputController.ContentDragSourceSize,
            inputController.CurrentMousePosition);

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
        questTriggerWindow.Clicked += _ => inputController.FocusElement(OpenQuestComposer(presentation.ElementPoolService, notificationCenter, layers));

        return new ShellContext(mapWindow, notificationCenter, inventory, layers, inputController);
    }

    /// <summary>Base tier: the map itself plus the debug stats footer directly beneath it -- see UiInputController's own doc comment for what each of the four tiers means. MapWindow's own factory (and every other pooled type's) is already registered by the time this runs -- see Build's ElementFactoryRegistry.RegisterAll call.</summary>
    private static MapWindow BuildBaseWindows(
        PresentationContext presentation, EcsContext ecsContext, Vector2 screenSize, DiagnosticsEngine? diagnostics, MapViewState mapViewState, UiLayerStack layers)
    {
        var mapSize = new Vector2(screenSize.X, screenSize.Y - DebugWindowHeight);

        var mapWindow = presentation.ElementPoolService.CreateElement<MapWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = Vector2.Zero,
                Size = mapSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowBorder = true,
                ShowTitle = false,
                CanUserScrollHorizontal = true,
                CanUserScrollVertical = true,
            },
        });
        mapWindow.Initialize();
        layers.Add(UiLayer.Base, mapWindow);

        var debugWindow = presentation.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(0, mapSize.Y),
                Size = new Vector2(mapSize.X, DebugWindowHeight),
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
        });
        debugWindow.SetContent(new DebugWindowContent(presentation.FontService, ecsContext.EntityManager, ecsContext.ComponentManager, diagnostics));
        debugWindow.Initialize();
        layers.Add(UiLayer.Base, debugWindow);

        return mapWindow;
    }

    /// <summary>StaticHUD tier: the player health bar, action lock, status effects, InspectionWindow, the hotbar, and the quest trigger -- see UiInputController's own doc comment for what each of the four tiers means. questTriggerWindow is returned for Build, which wires its Clicked event once the DynamicHUD tier (needed by OpenQuestComposer) also exists. hotbarContent and inspectionWindow are returned too, for BuildHotbarController and Build's own OnInspectionOpened wiring respectively.</summary>
    private static (TextWindow QuestTriggerWindow, HotbarContent HotbarContent, InspectionWindow InspectionWindow) BuildStaticHudWindows(
        PresentationContext presentation, World world, EcsContext ecsContext, ActionCatalog actionCatalog, ItemCatalog itemCatalog, Vector2 screenSize, MapViewState mapViewState, UiLayerStack layers)
    {
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
        layers.Add(UiLayer.StaticHud, playerHealthBarWindow);

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
        layers.Add(UiLayer.StaticHud, playerManaBarWindow);

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
        layers.Add(UiLayer.StaticHud, actionLockWindow);

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
        layers.Add(UiLayer.StaticHud, playerStatusEffectsWindow);

        // Right-aligned column, directly beneath the status effects row, leaving MinimapReserve
        // of empty headroom above the hotbar's own worst-case (fully expanded) top edge -- see
        // MinimapReserve's own doc comment. Same width as the health bar (PlayerHealthBarContent.
        // Size.X), per the Inspection V2 request.
        var inspectionWindowTop = HudMetrics.Margin.Y + PlayerHealthBarContent.Size.Y + ManaBarGap + PlayerManaBarContent.Size.Y + PlayerStatusEffectsContent.Size.Y + InspectionWindowGap;
        var hotbarClearanceTop = screenSize.Y - HotbarContent.MaximumSize.Y - HudMetrics.Margin.Y * 1.5f;
        var inspectionWindowBottom = hotbarClearanceTop - InspectionWindowGap - MinimapReserve;
        var inspectionWindowSize = new Vector2(PlayerHealthBarContent.Size.X, System.Math.Max(0f, inspectionWindowBottom - inspectionWindowTop));

        var inspectionWindow = presentation.ElementPoolService.CreateElement<InspectionWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(screenSize.X - HudMetrics.Margin.X - inspectionWindowSize.X, inspectionWindowTop),
                Size = inspectionWindowSize,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                ShowTitleWhenMinimized = true,
                TitleText = InspectionWindow.MinimizedTitle,
                CanUserClose = false,
                CanUserMinimize = true,
                CanUserScrollVertical = true,
            },
        });
        inspectionWindow.SetContent(new InspectionWindowContent(world, mapViewState, ecsContext.ComponentManager, ecsContext.EntityManager, presentation.ElementPoolService));
        inspectionWindow.Initialize();
        layers.Add(UiLayer.StaticHud, inspectionWindow);

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
            // Left margin matches the notification count window's (HudMetrics.Margin.X).
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(HudMetrics.Margin.X, 800), Size = new Vector2(120, 30), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.LightGray },
            Text = new TextOptions { Text = "New Quest" },
        });
        questTriggerWindow.Initialize();
        layers.Add(UiLayer.StaticHud, questTriggerWindow);

        return (questTriggerWindow, hotbarContent, inspectionWindow);
    }

    /// <summary>DynamicHUD tier: NotificationCenter owns/populates its own folder+popups, and InventoryFolderController does the same for its own folder+window (both add to UiLayer.DynamicHud specifically; their two Tooltip-family hover popups go to UiLayer.Tooltip instead) -- see UiLayer's own doc comment for what each tier means. Build also passes the same layer stack into OpenQuestComposer later, since that popup belongs in DynamicHud too. Every pooled type either of these creates is already registered by the time this runs -- see Build's ElementFactoryRegistry.RegisterAll call.</summary>
    private static (NotificationCenter NotificationCenter, InventoryFolderController Inventory) BuildDynamicHudWindows(PresentationContext presentation, World world, EcsContext ecsContext, ItemCatalog itemCatalog, MapWindow mapWindow, UiLayerStack layers)
    {
        var notificationCenter = new NotificationCenter(presentation.ElementPoolService, ecsContext.EventBus, layers);
        notificationCenter.Initialize();

        var inventory = new InventoryFolderController(
            presentation.ElementPoolService, world, ecsContext.ComponentManager, presentation.FontService, presentation.GlyphRenderer,
            presentation.SpriteSheetService, presentation.SpriteRenderer, itemCatalog, mapWindow);
        inventory.Initialize(layers);

        return (notificationCenter, inventory);
    }

    /// <summary>Constructs HotbarController and lets it add the Armed Hotkey Summary window into UiLayer.Tooltip -- mirrors NotificationCenter/InventoryFolderController's own Initialize(layers) call shape above. Needs mapViewState/hotbarContent (from BuildStaticHudWindows) and actionTargeting (constructed at the top of Build, shared with MapWindow's own factory).</summary>
    private static HotbarController BuildHotbarController(
        PresentationContext presentation, MapViewState mapViewState, HotbarContent hotbarContent, ActionTargetingController actionTargeting, UiLayerStack layers)
    {
        var hotbarController = new HotbarController(mapViewState, hotbarContent, actionTargeting);
        hotbarController.Initialize(presentation.ElementPoolService, layers);
        return hotbarController;
    }

    /// <summary>Built after InventoryFolderController (which it reuses the player's own inventory window through, see PlayerInventoryWindow/OpenInventoryWindow) and after MapWindow exists (whose OnCorpseClicked Build wires to this controller's OpenLoot right after this call returns).</summary>
    private static SecondaryInventoryWindowController BuildSecondaryInventoryWindowController(
        PresentationContext presentation, EcsContext ecsContext, InventoryFolderController inventory, UiLayerStack layers)
    {
        var controller = new SecondaryInventoryWindowController(presentation.ElementPoolService, ecsContext.ComponentManager, inventory);
        controller.Initialize(layers);
        return controller;
    }

    /// <summary>User tier: hosts cursorTextContent/dragGhostContent (built at the top of Build, before UiInputController exists -- see Build's own comment) -- see UiLayer's own doc comment for what this tier is for.</summary>
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
        // itself and remove the closed instance from layers, the same cleanup
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
    InventoryFolderController Inventory,
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
    /// The render/diagnostics services every Update/Draw call needs -- captured once by
    /// LoadContent (see its own doc comment for why that's the right hook) rather than threaded
    /// through every Update/Draw call, since all four are session-lifetime-stable in real usage:
    /// GraphicsDevice never changes reference for this app; SpriteBatchRenderer hands back the
    /// same single SpriteBatch instance on every call (see its own doc comment); the unit-pixel
    /// Texture2D is created once in GameLoop.LoadContent; and DiagnosticsEngine.FrameCostRecorder
    /// is backed by a field DiagnosticsEngine's own constructor sets once from the process's fixed
    /// --diagnostics= flag and never reassigns. None of the four are ever expected to vary
    /// call-to-call the way e.g. GameTime or which layer is being drawn do.
    /// </summary>
    private GraphicsDevice _graphicsDevice = null!;

    private SpriteBatch _spriteBatch = null!;

    private Texture2D _unitRectangle = null!;

    private IFrameCostRecorder? _frameCostRecorder;

    /// <summary>
    /// Captures the session-stable render/diagnostics services (see their own field doc comment)
    /// and lets every window LoadContent, in that order -- called once, from GameLoop.LoadContent,
    /// after GraphicsDevice/unitRectangle/the shared SpriteBatch all already exist.
    /// </summary>
    public void LoadContent(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle, IFrameCostRecorder? frameCostRecorder)
    {
        _graphicsDevice = graphicsDevice;
        _spriteBatch = spriteBatch;
        _unitRectangle = unitRectangle;
        _frameCostRecorder = frameCostRecorder;

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
    public void PreSimulationUpdate(GameTime gameTime)
    {
        InputController.Update(gameTime);
        NotificationCenter.Update(gameTime);
    }

    /// <summary>
    /// The window tree's own per-frame Update, plus Inventory's (its folder's IsDisabled flag
    /// depends on player state EcsContext.Update can change -- see InventoryFolderController.Update
    /// -- so it belongs here, after simulation, not in PreSimulationUpdate) -- deliberately run
    /// after GameLoop's own EcsContext.Update, so windows reflect this frame's simulation results
    /// rather than last frame's.
    /// </summary>
    public void Update(GameTime gameTime)
    {
        Inventory.Update(gameTime);

        foreach (var layer in UiLayerStack.LayersAscending())
        {
            UpdateWindowLayer(Layers[layer], layer.ToString(), gameTime);
        }
    }

    /// <summary>Drawn bottom-to-top, UiLayer's own declaration order -- see its doc comment for what each tier holds. User (topmost) draws last and unconditionally, so drag feedback is never occluded by whatever it's passing over on its way to a drop target. A dim overlay is drawn immediately beneath Layers.BottommostMenuWindow, if menu mode is currently active -- see UiLayerStack's own doc comment on OpenMenuWindow/CloseMenuWindow. Every element UiLayerStack.IsMenuModeExempt opted in (the hotbar, the Notification/Inventory folder tiles) is pulled out of its ordinary draw slot and redrawn immediately above that same dim quad instead (see FindMenuModeExemptElements) -- they stay reachable for input while menu mode is active (UiInputController.TryHitTestInteraction), so they need to read as visually usable too, not look identical to the rest of the dimmed HUD.</summary>
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
            if (_frameCostRecorder is { } recorder)
            {
                var start = Stopwatch.GetTimestamp();
                window.Update(gameTime);
                recorder.Record(FrameCostCategory.Update, tierName, window.GetType().Name, Stopwatch.GetElapsedTime(start));
            }
            else
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
        if (_frameCostRecorder is { } recorder)
        {
            var start = Stopwatch.GetTimestamp();
            window.Draw(gameTime);
            recorder.Record(FrameCostCategory.Draw, tierName, window.GetType().Name, Stopwatch.GetElapsedTime(start));
        }
        else
        {
            window.Draw(gameTime);
        }
    }
}
