# Presentation / UIEngine Split

(Child 4 of `EPIC-sprites-ui-engine-and-dev-tools.md`. Started 2026-10-14; decisions confirmed 2026-10-14: generic content drag
with an opaque payload, `UIEngine.*` namespaces, all three phases now -- ahead of child 5. Phase 1 landed 2026-10-14; phase 2 landed 2026-10-14; phase 3 landed 2026-10-14. The plan is complete.)

## Context

What exists today:
- **Presentation is one project** (FNA, FontStashSharp, Engine, Game, Content, SpriteFormat; ~200 files) holding both the
  generic window framework and every game window. The epic's target is a **UIEngine** project that references only Engine
  and SpriteFormat, which Presentation and the IDE (child 5) build on.
- **The framework is already almost Game-free.** A transitive reference scan (every Presentation file, following type
  references between Presentation files until one names a `Game.*` type) finds that the whole framework -- `Element`,
  `Window`, `TextWindow`, `TextBox`, `Button`, `Folder`, `GridControl`, `Toggle`, `Tooltip`, `ContextMenu`, `UiLayerStack`,
  `ElementPoolService`, fonts, sprite drawing -- reaches Game through only **four seams**:
  1. `SpriteManifestResolver.Resolve(EntityVisualView)` / `Resolve(TerrainVisualView)` (two methods taking `Game.Views`
     records; everything else in the resolver is manifest-only). `Button` -> `SpriteSheetService` -> the resolver is how
     the whole `Element` tree is tainted.
  2. `SpriteOrGlyphRenderer` has `using Game.Views` / `Game.Modules.Core.Components` it does not need.
  3. `PointerState.ContentDrag` is a `DragGhostState` (item stack id, merged item definition id, action id, `CurrencyType`).
  4. `UiInputController` (2242 lines) -- the scoping note's "hard part" (TODO.md, Presentation, "Split Presentation into
     Presentation + UIEngine projects"). Its generic half (hit-testing, move/resize, scrollbars, focus and Tab, Escape and
     menu mode, click/double-click, right-drag/tap, wheel, text input, cursors) is entangled with:
     - **content drag**: payload fields per kind (`_contentDragItemStackInstanceId`, `...MergedItemDefinitionId`,
       `...ActionId`, `...CurrencyType`, origin hotbar/slot/entity), capture by type test (`InventoryItemStackCell`,
       `CurrencyElement`, `HotbarContent`), shop gating through `MapViewState.OpenShopEntityId`/`ShopView`, drop
       resolution (`FindDropTargetEntityId` over `IInventoryDropTarget`/`IWholeWindowDropTarget`, then the
       `IDragDropResolver` chain it builds itself from `InventoryCommands`/`ShopCommands`/`CurrencyCommands`/`IPlayerQuery`,
       then `HotbarContent.ResolveDroppedBinding`), the hotbar's accepting-drag glow, and the "blocked" cursor;
     - **hotbar**: `CaptureHotbarPressSlot`, `ResolveHotbarSlotClick`, `HandleHotbarHover` (`HotbarController`);
     - **game keys**: U/I/O/F3 window toggles (four controllers), Left Alt (`TargetingModeSwitch`), F12 admin mode;
     - **game interceptions**: Item Details closing on an outside press (consumes the press), Item Comparison disarming on
       every right-press, and the activation-refusal cursor (`HotbarContent.IsSlotActive`, `MapWindow.IsArmedConfirmRefused`).
     Eighteen constructor parameters, ten of them optional game controllers.
- **Mixed static tables.** `WindowPalette` (framework colours next to `HeartGlyphColor`, `AbilityScoreColumnBackground`,
  `Diagnostics*`) and `FontChrome` (`DefaultFontSize`, `WindowTitleFontSize` next to `Map*FontSize`, `ItemDetailsNameFontSize`)
  each serve both sides. Every other palette/chrome class is wholly one side's.
- **The layer loop lives in the exe.** `ShellContext` (`DungeonCrawlerWorld/ShellBootstrapper.cs`) updates and draws
  `UiLayerStack` bottom to top with the menu-mode dim and exempt elements, and keeps its own
  `GraphicsDevice`/`SpriteBatch`/`UnitRectangle` -- the same triple `ElementPoolService` already holds (the duplicate
  deferred "until the split is reopened"). The IDE needs exactly this loop.
- **`PresentationBootstrapper`/`PresentationContext`** build only generic services (fonts, sprite batch, label renderer,
  manifest + sheets, sprite renderer, element pool) -- nothing game-specific.
- Engine is already FNA-referencing; `GameTiming` and `GlobalState` (used by `TextBox`, `Window`) are Engine's.
- Tests: one `Tests` project; `Tests/Presentation` mixes framework tests (`TextBoxTests`, `UiLayerStackTests`,
  `ScrollbarInputTests`, `Window*Tests`, `TooltipTests`, ...) with game-window tests. `TestUiInputController` builds the
  controller with game services. Architecture tests exist for the same kind of rule (`Game_DoesNotReferenceSpriteFormat`,
  `SpriteFormat_ReferencesOnlyTheRuntime`, `PresentationStoreAccessArchitectureTests`).

## Design

### What goes where

**The rule: a type goes to UIEngine if a non-game tool would want it unchanged** (a window, a control, a font, a sprite
drawn by name, input routing). A type that names a game concept stays in Presentation even when it touches no Game type
today (`InventoryItemStackCell`, `HotkeySlotLayout`, `ToggleActiveMarker`, `TargetShapePreviewElement`, `InspectionWindow`,
`TargetedWindowLifecycle`, `NotificationMinimizeBehavior`, map rendering). The IDE reaches those through Presentation, as
the epic intends (it reuses Item Details and the target shape preview from there).

| UIEngine | Stays in Presentation |
|---|---|
| **UI**: `Element` + its option/state types, `Window`, `TextWindow`, `TextBox`, `Button`, `Folder`, `GridControl`, `Toggle`, `Tooltip`, `TooltipController`, `ContextMenu`, `ContextMenuController`, `ContextMenuOption`, `DynamicHudContextMenus`, `UiLayer`, `UiLayerStack`, `UiLayerNameParser`, `WindowLifecycle`, `WindowCascadePlacement`, `PopupAnchor`, `ScreenBoundsClamp`, scrollbars, `SeparatorBar`, `TextDivider`, `FractionBarElement`, `TabbedContent`, `CursorTextContent`, `IElementContent`, `IChromeBehavior`, `ChromeBehaviors/*`, borders, `GlowRenderer`, `GridSquareRenderer`, `MenuModeDimRenderer`, `DebouncedTextFilter`, `ElementPoolService`, `VersionWatcher` | Every game window, content and controller (Inventory, Item Details, Health, Ability Scores, Hotbar, Shops, Trade, Loot, Loot boxes, Notifications, Diagnostics window, floating text), `MapWindow`, `MapViewState`, `ShellServices`, `ActionTargetingController`, `PlayerMovementController`, `TargetingModeSwitch`, the text formatters (`EffectFormatting`, `CostText`, `ToggleText`, ...) |
| **Input**: `UiInputController` (generic half), `ElementInteraction`, `ResizeEdges`, `PointerState`, the new hooks below | `HotkeySlotLayout`, `DragDrop/*` (resolvers become a drop handler's), the game input handler (new) |
| **Rendering**: `SpriteBatchRenderer`, `LabelRenderer`, `ContrastTextRenderer`, `BorderedBoxRenderer`, `TileFillRenderer`, `ResourceBarRenderer`, `SparklineRenderer`/`Style`, `SpriteManifestResolver`, `SpriteSheetService`, `SpriteRenderer`, `SpriteOrGlyphRenderer`, `SpriteView`, `SpritePlacementMath`, `TerrainSpriteSet` | `MapVisual` (+ the two `Resolve` overloads), `MissingSpriteCheck`, `ItemIconRenderer`, `PotionCooldownTextRenderer`, `RadialFillRenderer`, `DragGhostRenderer`, `ResourceBarValueText` (HUD rounding rules), `MapRendering/*` |
| **Fonts**: `FontService`, `FontStashTextMeasurer` | -- |
| **Chrome/palettes**: `WindowChrome`, `PopupChrome`, the framework half of `WindowPalette` and `FontChrome` | Every other `*Chrome`/`*Palette`, and the game half of `WindowPalette`/`FontChrome` |
| **Hosting**: `UiEngineBootstrapper`/`UiEngineContext` (today's `PresentationBootstrapper`/`Context`), `UiLayerHost` (new, from `ShellContext`'s loop) | -- |

Namespaces follow the project: `UIEngine.UI`, `UIEngine.Input`, `UIEngine.Rendering`, `UIEngine.Fonts`, `UIEngine.UI.Chrome`,
`UIEngine.Bootstrap`. Files keep their folder layout inside the new project.

### Seams 1-3: small cuts

- **Visual resolution stays in Presentation.** `Resolve(EntityVisualView)`/`Resolve(TerrainVisualView)` leave
  `SpriteManifestResolver` as extension methods on it (`MapVisualResolution`, beside `MapVisual` in `Presentation.Rendering`;
  both stay in Presentation), built on the resolver's public `TryGetForEntity`/`TryGetTerrainSprites`, so callers are unchanged.
- `SpriteOrGlyphRenderer` drops its unused Game usings.
- **Palettes split by reader.** A field read by any UIEngine type stays in `WindowPalette`/`FontChrome` (UIEngine); the rest
  move to Presentation-side classes (`GameWindowPalette`, `GameFontChrome` -- names to confirm) with the same "plain mutable
  field, one per widget" convention. Nothing changes value.
- **`PointerState.ContentDrag` becomes generic** -- see the content drag below.

### Seam 4: UiInputController's game half behind hooks

UIEngine keeps the whole gesture machinery and learns no game type. Presentation plugs in through two hooks.

**1. Content drag becomes a UIEngine mechanism with an opaque payload.** The state machine is already generic in shape --
capture on press, tap threshold (`ContentDragTapThresholdPixels`), ghost delay (`ContentDragGhostDelayFrames`), held
frames, suppressing the origin click once past the threshold, clearing on release whatever happens. Only *what* is dragged
and *what a drop means* are the game's.
- `IContentDragSource` (an `Element`, or a window's `IElementContent`): `bool TryBeginContentDrag(Point pressPosition, out
  ContentDragStart start)` -- `start` carries the payload (`object`, never inspected by UIEngine) and the ghost's source size.
  The controller asks the pressed element, then the pressed window's content (no ancestor walk -- today's checks never
  walked), so `HotbarContent`, `InventoryItemStackCell` and `CurrencyElement` answer for themselves with their own static
  gates ("can neither trade nor bind", `IsDragSource`).
- `IContentDropHandler` (registered with the controller, tried in order like today's resolver chain; every member but
  `TryDrop` defaults to nothing): `bool AllowsDragStart(object payload)` (any handler may refuse a drag before it starts --
  this is where the gates that need world state live: the shop-open stage check and a shop's own Gold),
  `bool TryDrop(object payload, Element? dropTarget, Point position)`, `bool IsDropRefusedAt(object payload, Point position)`
  (the "No" cursor), and `OnDragGhostShown` / `OnDragEnded` (the hotbar's accepting glow). Presentation registers two:
  **`InventoryTransferDropHandler`** (`FindDropTargetEntityId` + the `IDragDropResolver` chain -- Trade, Shop, Plain --
  unchanged inside it) and **`HotbarBindingDropHandler`** (`ResolveDroppedBinding`, origin-slot unbind, the
  merged/currency/non-player refusals).
- The payload is one Presentation record, `DragPayload` (`Presentation.Input.DragDrop`: origin kind, item stack / merged
  item / action / currency, origin entity, the cell's `CanStageInTrade`, origin hotbar and slot), so the handlers and
  `DragGhostContent` share one shape.
- `PointerState.ContentDrag` becomes `ContentDragState(bool Visible, object? Payload, Vector2 SourceSize, Point
  CursorPosition)`; `DragGhostContent` pattern-matches its payload.

**2. `IUiInputHandler` for everything else the game does with raw input.** One interface, a list of them on the
controller, each member a no-op by default, called at the point today's inline code runs. `hitTest` is the controller's
own hit test as a delegate, for a handler that must ask only sometimes:

| Member | Today's code it replaces |
|---|---|
| `HandleKeys(KeyboardState current, KeyboardState previous, bool isTextBoxFocused)` | U/I/O/F3 toggles, Left Alt, F12 |
| `bool TryConsumePress(Point position)` (before the hit test; true consumes the press) | Item Details outside-click close |
| `OnPress(Element? pressedElement, Point position)` / `OnRelease(Point position, Func<Point, Element?> hitTest)` | `CaptureHotbarPressSlot`, `ResolveHotbarSlotClick` |
| `OnRightPress(Point position)` | `ItemComparisonController.Disarm` |
| `OnPointerUpdate(Point position, bool isInteractionInProgress)` | `HandleHotbarHover` |
| `bool IsInputRefusedAt(Point position, Func<Point, Element?> hitTest)` | `IsActivationRefusedAt` (hotbar slot inactive, armed confirm refused) |

Presentation implements one `GameInputHandler` holding the controllers it needs (hotbar, item details, comparison, the four
window controllers, targeting switch). The constructor shrinks to `UiInputController(UiLayerStack, Vector2 screenSize,
PointerState, IReadOnlyList<IUiInputHandler>, IReadOnlyList<IContentDropHandler>, ContextMenuController?)` -- the context
menu stays generic (Escape and outside-click priority are framework rules). Handlers take `Element`, never
`ElementInteraction`, which stays internal.

F12 (admin mode) moves into `GameInputHandler`: admin mode is the game's tool even though its flag sits in Engine's
`GlobalState`; `Window`'s admin title (reads `GlobalState`) is unaffected.

### The layer host comes out of the exe

`UiLayerHost` (UIEngine) takes `ShellContext`'s per-frame window loop: `LoadContent` every window, `Update` every layer
bottom to top with the per-window `EngineHooks.FrameCost` rows (names unchanged -- the benchmark reads them), and `Draw`
with the menu-mode dim and exempt elements. It reads `SpriteBatch`, `GraphicsDevice` and the unit rectangle from
`ElementPoolService`, which closes the duplicate triple deferred since 2026-08-20. `ShellContext` keeps the game's own
ordering (`PreSimulationUpdate` with `NotificationCenter`, `Inventory.Update`/`AbilityScore.Update`) and delegates the loop.

### Rules and their tests

- UIEngine references Engine, SpriteFormat, FNA and FontStashSharp only -- never Game, Presentation or Content.
  `UIEngine_DoesNotReferenceGame`, `UIEngine_DoesNotReferencePresentation` (assembly references, as the SpriteFormat tests).
- Presentation keeps `PresentationStoreAccessArchitectureTests` and `MapWindowArchitectureTests` as they are.
- Tests stay one project (`Tests.csproj` adds the UIEngine reference); framework tests move to `Tests/UIEngine`, game-window
  tests stay in `Tests/Presentation`. UIEngine gets `InternalsVisibleTo Tests` (the controller's test `Update` overload).
- Fonts: UIEngine's `FontService` takes a directory as it does now; the executables reference `Content` for the files.

## Phases

Each phase builds, passes tests and is checked in-game before the next.

1. **Seams inside Presentation, no new project.** Move the two `Resolve` overloads to `MapVisualResolution`; drop
   `SpriteOrGlyphRenderer`'s Game usings; split `WindowPalette`/`FontChrome` by reader; add `UiLayerHost` (still in
   Presentation) and have `ShellContext` delegate to it, dropping its render triple. In-game: map, HUD, every window, menu
   mode dim with exempt hotbar/folders, F3 frame-cost rows unchanged.
2. **Input hooks.** `IUiInputHandler`, `IContentDragSource`, `IContentDropHandler`, `ContentDragState`; `GameInputHandler`,
   the inventory-transfer and hotbar-binding drop handlers, `DragPayload`; sources on `InventoryItemStackCell`,
   `CurrencyElement`, `HotbarContent`. `UiInputController` loses every game type and its game constructor parameters;
   `TestUiInputController` builds the game handlers. In-game: every drag (cell to hotbar, hotbar to hotbar, hotbar off,
   cell to corpse/shop/trade columns, currency give/stage, merged stack refused, shop Gold), hotbar tap/arm/hover summary,
   U/I/O/F3/Left Alt/F12, Item Details outside-click, comparison disarm, refusal cursor.
3. **The project.** Create `UIEngine/UIEngine.csproj`; move the UIEngine set with `mv` (no `git mv`), renaming namespaces;
   widen what Presentation uses across the new boundary from `internal` to `public` (`Element.WasKeyPressed`,
   `WindowPalette`, `WindowChrome`, and whatever else the build names);
   Presentation, the exe and Tests reference it; move the bootstrapper/context to `UiEngineBootstrapper`/`UiEngineContext`;
   move framework tests to `Tests/UIEngine`; add the two architecture tests. Update CLAUDE.md (Layers: a UIEngine bullet;
   Presentation's), the epic (child 4 landed), IMPLEMENTATION-NOTES (where the line is and why, the input hooks), and delete
   TODO.md's "Split Presentation into Presentation + UIEngine projects". In-game: a full pass -- nothing should look or behave
   differently.

## Open questions

- Names: `GameWindowPalette`/`GameFontChrome`, `GameInputHandler`, `UiLayerHost`, `MapVisualResolution`.

## Follow-ups after landing (tested in-game 2026-10-15)

- 2026-10-15: `InventoryTransferDropHandler` takes a non-nullable `MapViewState` (it was nullable only for tests) and
  always registers `TradeDragDropResolver`.
- 2026-10-15: `GameInputHandler` (every controller optional, for tests) is split into one handler per concern with only
  required dependencies: `KeyPressInputHandler` (UIEngine) per hotkey, `AdminModeInputHandler`, `HotbarInputHandler`,
  `ItemDetailsOutsideClickInputHandler`, `ItemComparisonDisarmInputHandler`, `ActivationRefusalInputHandler`.
  `TestUiInputController` registers only what a test supplies.
- 2026-10-15: handlers ask the hovered element instead of scanning layers. `UiInputController` hit-tests the cursor once a
  frame (only when it moved) and passes it to `OnPointerUpdate`, `IsInputRefusedAt` and `IsDropRefusedAt`, so what is
  drawn over the hotbar or map now wins. `HotbarBindingDropHandler` holds its `HotbarContent` (`DragPayload.OriginHotbar`
  is gone). `HotbarContent` keeps its slot rectangles (rebuilt when the origin or unlocked slots change) and remembers
  its last slot lookup, so neither allocates. `IsSlotBound`/`TryGetSlotBlocker` answer "is it bound" and "why is it
  blocked" without building summary text; `HotbarController.UpdateSummary` builds that text only when the popup changes.
  `InputHookImplementationTests` fails a handler whose method no longer matches its hook member.
