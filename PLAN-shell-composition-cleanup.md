# Shell Composition Cleanup

(Pre-implementation. Folds five findings into one change: a Game-owned session object, the
achievement loot-box claim moving into its module, the Admin Mode tool collection, per-session
command services replacing Presentation's calls into static Game APIs, and a Presentation-side
coordinator for the item windows. Replaces TODO.md's "Consolidate Admin Mode features out of the
bootstrappers" entry, which is deleted when Phase 3 lands. Takes over Stages 1 and 4 of
PLAN-presentation-data-layer.md for every consumer this plan touches.)

## Context

`ShellBootstrapper.Build` is ~250 lines, and most of it is no longer composition. What is in it today:

**Game rules.**
- The rule "an achievement's loot box is granted when the player closes its notification" is a
  lambda on `NotificationCenter.NotificationDismissed` that builds `AchievementLootboxClaims` itself.
  A mod replacing Achievements can't take it over, and nothing but a live run exercises it.
- `MapViewQuery`, `EntityBodyParts.For` (twice -- lines 93 and 335), `EntityActions.For`,
  `PlayerActionGate` (in `ElementFactoryRegistry`), `BlueprintAdminCommands` and
  `LootboxAdminCommands` are Game objects constructed in the exe. None depends on Presentation.
- `PlayerInputBuffer` lives in `Presentation/UI` but is the only writer of the player's step and
  activation requests: expiry, stagger dropping and dodge steps are gameplay rules. It uses no
  Presentation type; the shell hands it six pools, the clock and the bus.

**Window-to-window wiring (~130 lines, 113-287).** Shop and loot windows closing each other,
`GetSecondaryTargetEntityId`, shop/trade open-close pairing, item clicks routed to comparison or
Item Details, four `Get...Rectangle` fallbacks, Compare entry points, loot-box results and reward
clicks, the `EntityDestroying` UI cleanup, and the late-bound `CursorTextContent`/`DragGhostContent`
getters. It is Presentation behaviour, and because it lives in the exe no test can construct it.

**Admin wiring.** Four settable nullable properties on `MapWindow` (`NeighborhoodStreamer`,
`BlueprintAdmin`, `LootboxAdmin`, `Teleporter`), each added by its own feature.

**Why the parameter lists grow.**
- The same fields are repeated in three bundles: `GameModuleContext` → `GameBootstrapResult`
  (19 positional fields) → `WorldSessionContext` (22 positional fields). Five of
  `WorldSessionContext`'s fields are never read (`MathUtility`, `MovedEntities`,
  `CrawlerNumberAllocator`, `NeighborhoodRecords`, `TierResolver`); `PlayerActivityLog` is only
  disposed.
- Game's gameplay API is static functions taking raw ingredients --
  `ShopActions.TrySellToShop(componentManager, itemCatalog, player, shop, stack, playerQuery)`,
  `CurrencyActions.TryTransfer(componentManager, ...)`, `InventoryQueries.TryResolveEffectiveItem(...)`.
  Every UI element that reads or acts carries `ComponentManager` plus catalogs, bus, clock and
  `IPlayerQuery` to pass them along. 33 Presentation files name `ComponentManager` or a pool type;
  `UiInputController` takes 15 parameters, `ActionTargetingController` 18, `HotbarContent` 12,
  `ElementFactoryRegistry.RegisterAll` 16.

Presentation's calls into static Game APIs (counted by call site):

| API | Calls | Kind |
|---|---:|---|
| `InventoryQueries.TryResolveEffectiveItem` / `TryFindByStackInstanceId` / `IsInventoryDisabled` / `CopyStacksForEntity` | 37 | read |
| `ShopStockPricing.*`, `ShopMarginPricing.ResolveEffectiveShop` | 26 | read |
| `InventoryActions.TryTransferStack` / `TryTransferAllStacksOfItem` | 13 | write |
| `CurrencyActions.TryTransfer` / `TryTransferAll` | 9 | write |
| `ShopActions.TryBuyFromShop` / `TrySellToShop` / `TryGiveCurrencyToShop` / `CanTrade` | 12 | write (CanTrade read) |
| `ItemHotkeyBindingQueries.*`, `ActionHotkeyBindingQueries.*`, `HotkeyBindings.Add` | 18 | both |
| `StatusEffectQueries.*`, `HealthQueries.TryGetTotals`, `AbilityScoreQueries.TryGetComponent` | 10 | read |
| `PotionCooldownEffects.*`, `ScrollScalingEffects.*` | 8 | read |
| `InventoryTagQueries.GetTagCounts` | 2 | read |

Reads outnumber writes about three to one. Command services alone would take `ComponentManager` out
of roughly 9 files; the other ~24 only read. That is why this plan includes the read side (Phase 5)
for every consumer it touches.

## Design

### The rule

**Game builds and owns every per-session service; Presentation receives the specific service it
uses; the exe only chooses and arranges.** Concretely:
- Anything that reads or writes game state from outside a system is a method on a per-session Game
  service that already holds its pools. Presentation never names `ComponentManager`, a pool, or a
  `*Component` type in the files this plan migrates.
- A game rule triggered by UI (a notification closed, a button pressed) reaches Game as an event or a
  command call, and the rule lives in the module that owns it.
- Cross-window behaviour lives in Presentation, where tests can drive it.
- `ShellBootstrapper` constructs, lays out, and hands over. No lambdas encoding behaviour.

### `GameSession`: one Game-owned session object (Phase 1)

`GameBootstrapper.Build` returns a `GameSession` (`Game.Bootstrap`), replacing `GameBootstrapResult`:

```csharp
public sealed class GameSession
{
    public EcsContext EcsContext { get; }
    public World.World World { get; }
    public GameCatalogs Catalogs { get; }       // Actions, Items, Lootboxes, Achievements, StatusEffectDisplays, Definitions, Terrain
    public SimulationClock SimulationClock { get; }
    public GameViews Views { get; }             // Phase 1: MapView, BodyParts, EntityActions, PlayerActionGate; Phase 5 adds the rest
    public GameCommands Commands { get; }       // Phase 4
    public GameSessionInternals Internals { get; } // Factory, Skeletons, SpawnRecordRebuilder, Teleporter, TierResolver, LocalTierRoster, MovedEntities
    public IReadOnlyList<ModuleFailure> ModuleFailures { get; }
    public SettingValues Settings { get; }
    public IReadOnlyList<SettingsFailure> SettingsFailures { get; }
}
```

- Built by name from `GameModuleContext` in one place, never re-listed positionally.
  `GameModuleContext` stays the module-facing build API; `GameSession` is what the outside of Game
  sees. Presentation is not handed `GameModuleContext` (it exposes registries still being filled in
  `Configure`, `MathUtility`, `EntityMoveSync` -- build-phase surface).
- `GameCatalogs`, `GameViews`, `GameCommands` are plain classes with named properties, so a new
  service is one property and one assignment, not an edit to three positional constructors.
- `Internals` holds what bootstrap, streaming, admin tools and inspection need but ordinary UI must
  not reach (factory, skeletons, rebuilder, teleporter, tier resolver). It is a grouping for
  readability, not an access control.
- `WorldSessionContext` shrinks to `GameSession` plus what the exe really owns: `PlayerActivityLog`,
  `NeighborhoodStreamer`, `ReservedEntityIds`, `AdminTools` (Phase 3). The five unread fields go.
  Its `Dispose` is unchanged (`EcsContext` first, then the log).
- `GameViews` in Phase 1 holds the view objects the shell builds today: `MapViewQuery`,
  `EntityBodyParts` (built once), `EntityActions`, `PlayerActionGate`. `ElementFactoryRegistry` and
  `ShellBootstrapper` read them from there.

### Achievement claim moves into `AchievementModule` (Phase 2)

- New immediate event `AchievementNotificationDismissedEvent(Guid AchievementId)` in
  `Game.Notifications`, next to `AchievementNotificationDetails`.
- `NotificationCenter` already holds the `EventBus`. In `OnActiveNotificationClosed`, where it raises
  `NotificationDismissed` today, it also publishes that event when the notification carries
  `Achievement` details. Only dismissals publish: minimizing is excluded by the check that already
  exists there.
- `AchievementModule.RegisterSystems` subscribes and calls
  `AchievementLootboxClaims.TryClaim(context.PlayerQuery.PlayerEntityId, achievementId)`.
- **Immediate, not `IBufferedEvent`.** Today's claim is synchronous. Buffered events drain in the
  simulation, which doesn't run while paused or in menu mode, so a buffered claim would leave the box
  missing from an inventory the player might open straight away.
- `NotificationCenter.NotificationDismissed` loses its only subscriber and is deleted.
- The shell's lambda and `AchievementLootboxClaims` construction go.

### `AdminTools`: one collection for Admin Mode's commands (Phase 3)

- `AdminTools` (`Game.Admin`), built once in `WorldSessionBootstrapper` after the streamer exists,
  holding `BlueprintAdminCommands`, `LootboxAdminCommands`, a `NeighborhoodAdminCommands` wrapping
  the streamer's regenerate (`CanRegenerate` and its job), and a `TeleportAdminCommands` wrapping `EntityTeleporter`. Each admin
  command class moves under `Game.Admin`.
- `MapWindow` takes `AdminTools` as a required constructor dependency (through its factory), not
  four nullable settable properties. Admin tools always exist in a game, so under the nullable rule a
  non-nullable parameter is the right statement. Its admin menu groups move to a Presentation
  `AdminContextMenuOptions` built from `AdminTools`, which `MapWindow` calls for tile and entity
  options while `GlobalState.IsAdminModeOn`.
- Out of scope, as the TODO says: `GlobalState.IsAdminModeOn` display reads, the F12 toggle, and
  `GameLoop.SyncAdminModeWindowTitle`. Also out of scope: the console-command registration shape
  ("Console variables and console commands"). `AdminTools` is shaped so that item can register each
  command later without another rewiring.
- The admin blueprint menus and "Teleport here" stay; only their wiring changes.

### `GameCommands`: per-session write services (Phase 4)

One instance class per feature, built once by `GameBootstrapper` from built-in pools (always
present), each holding what it needs, so a caller passes only entity ids and values:

| Service | Methods (Presentation-facing) | Replaces |
|---|---|---|
| `InventoryCommands` | `TryTransferStack`, `TryTransferAllStacksOfItem` | `InventoryActions.*` calls |
| `CurrencyCommands` | `TryTransfer` (both overloads), `TryTransferAll` | `CurrencyActions.*` calls |
| `ShopCommands` | `TryBuyFromShop`, `TrySellToShop`, `TryGiveCurrencyToShop` | `ShopActions.*` calls |
| `HotkeyBindingCommands` | bind/unbind item and action slots, publishing `ItemHotkeyBoundEvent`/`ActionHotkeyBoundEvent` | `HotkeyBindings.Add`, `*HotkeyBindingQueries.Unbind`, and the two `EventBus.Publish` calls in `HotbarContent` |
| `LootboxCommands` | `OpenAll` | `LootboxOpener` reached through the shell |
| `PlayerCommands` | today's `PlayerInputBuffer` API | `PlayerInputBuffer`, moved from `Presentation/UI` into Game |

- **The static functions stay as the implementation.** Game systems call `InventoryActions`,
  `CurrencyActions` and `LootboxActions` too, with pools they already hold. Each service method is
  the static call with its pools bound. Both callers are real, so this is not a compatibility shim.
  Tests of the rules keep calling the static functions; the services need only a wiring test each.
- **`PlayerInputBuffer` becomes `PlayerCommands`** in Game (`Game.Modules.Actions` next to the
  pending-activation components it writes). Its logic, tests and doc comment move unchanged; only
  the namespace and construction site change. `ActionTargetingController` and
  `PlayerMovementController` take it from `GameSession.Commands`.
- `IPlayerQuery` is bound inside the services, so no Presentation caller passes it any more.

### `GameViews`: the read side for the same consumers (Phase 5)

This is PLAN-presentation-data-layer.md option D, applied to every consumer Phases 4 and 6 touch.
It follows that plan's rules: pull, no caching, and input resolves against live state (a view is a
live pull, so that holds automatically).

| View | Serves | Wraps |
|---|---|---|
| `InventoryView` | stacks of an entity, resolve effective item, find by stack id, disabled, tag counts | `InventoryQueries.*`, `InventoryTagQueries` |
| `ShopView` | effective shop, stock, band pricing, bulk buy/sell price and breakdown, `CanTrade` | `ShopStockPricing.*`, `ShopMarginPricing`, `ShopActions.CanTrade` |
| `CurrencyView` | an entity's currency amounts | direct `CurrencyComponent` reads |
| `HotkeyBindingView` | bound item/action per slot, `CanBind` | `*HotkeyBindingQueries.TryGet`/`CanBind` |
| `PlayerStatusView` | health totals, active status effects and stacks, ability scores, mana, action lock, potion cooldowns | `HealthQueries`, `StatusEffectQueries`, `AbilityScoreQueries`, `PotionCooldownEffects` |
| `ItemActivationView` | scroll scaling for targeting previews | `ScrollScalingEffects` |

- Return types are existing Game types (`ItemDefinition`, `InventoryItemStackComponent` copies, etc.)
  where Presentation already uses them. New view structs appear only where a consumer assembles
  several component reads into one bundle. Views stay allocation-free on per-frame paths
  (`readonly record struct`, spans, `out` parameters) -- the map-view rule.
- **Scope**: every Presentation file in the item-window cluster (inventory, secondary/loot, shop,
  trade, item details, comparison, currency row, hotbar, drag-drop resolvers, `UiInputController`,
  `ActionTargetingController`) and the HUD contents the shell builds (health, mana, action lock,
  status effects, ability scores, health window). **Not in scope**: `MapTintGrid` (reads aura
  components for rendering; the data-layer plan's map work covers it), `InspectionWindowContent`'s
  admin/rebuild paths (it reads the rebuilder's staging `ComponentManager` by design), and
  `DiagnosticsWindow`.
- **Enforcement**: extend `MapWindowArchitectureTests` into a Presentation architecture test that
  fails if a migrated type names `ComponentManager`, a pool type, or a `*Component` type. It lists
  exempt types explicitly, so an exemption is a visible decision.

### Presentation coordinator and pointer state (Phase 6)

- **`ItemWindowCoordinator`** (`Presentation/UI/Inventory`) constructs and owns the item-window
  group: `InventoryWindowController`, `SecondaryInventoryWindowController`, `ShopWindowController`,
  `TradeWindowController`, `ItemDetailsWindowController`, `ItemComparisonController`,
  `LootboxResultsWindowController`. It holds every rule now in the shell:
  - loot and shop windows are mutually exclusive, and it owns `OpenLoot`/`OpenShop`, which
    `MapWindow.OnCorpseClicked`/`OnShopClicked` call;
  - `GetSecondaryTargetEntityId`, the shop→trade open/close pairing, the rectangle fallbacks;
  - item-click routing (comparison armed → add/toggle; otherwise clear the anchor if it's changing,
    then open details), Compare entry points, Activate → targeting;
  - loot-box results and reward clicks (open the stack while it exists, otherwise the read-only
    definition);
  - `EntityDestroying` cleanup for its windows and `MapViewState.OpenShopEntityId`. The shell keeps
    only the `InspectedEntityId` reset, or it moves to `InspectionWindow` -- whichever reads better
    once written.
  It exposes what the shell, `UiInputController` and `ShellContext` use: the inventory, item details
  and comparison controllers, and `Update`.
- **`PointerState`** (`Presentation/Input`) is a small object `UiInputController` writes each frame:
  mouse position plus the content-drag fields that `DragGhostState` is built from today.
  `CursorTextContent` and `DragGhostContent` take it in their constructors. It is built before all
  three, which removes the two late-bound `Func` assignments and the ordering comment explaining
  them.
- **`ShellServices`** is a record of the shared UI singletons built at the top of `Build`:
  `UiLayerStack`, `MapViewState`, `MapCamera`, `ContextMenuController`, `TooltipController`,
  `PointerState`, `CursorTextContent`. It is passed only to `ElementFactoryRegistry` and to the
  coordinator's constructor, which are composition points that genuinely need most of it. Individual
  elements still take the specific services they use; a bag passed everywhere would be a service
  locator.
- **`ShellBootstrapper` afterwards**: build `ShellServices`, register factories, build base/static/
  dynamic/user windows, build the coordinator, build `UiInputController`, hook focus
  (`IsTextInputFocused`, notification focus, quest composer), and return `ShellContext`. The TEMPORARY
  quest composer stays where it is (memory: keep until a real second consumer exists).

## Phases

Each phase builds, passes tests, and stops for in-game testing before the next begins.

1. **`GameSession`.** Replace `GameBootstrapResult` with `GameSession` (`GameCatalogs`, `GameViews`
   with the four existing views, `GameSessionInternals`). Shrink `WorldSessionContext`, delete its
   five unread fields. `ShellBootstrapper`/`ElementFactoryRegistry` read `GameSession`; build
   `EntityBodyParts` once. `HeadlessBenchmark` follows. Test churn: `GameBootstrapperTests`,
   `EntityKeyWiringTests`, `BossLootboxAwarderTests`.
   *In-game check*: normal session start, Admin Mode spawn/apply/regenerate/teleport, inspection of
   an unsimulated creature (rebuilder path).
   **Done 2026-09-29.** `LootboxOpener` sits in `Internals` until
   Phase 4 wraps it in `LootboxCommands`.
2. **Achievement claim.** Event, `NotificationCenter` publish, `AchievementModule` subscription, delete
   `NotificationDismissed` and the shell lambda.
   *In-game check*: unlock an achievement with a box, close its notification, box arrives once;
   minimize then reopen and close, still exactly once.
   **Done 2026-09-29.**
3. **`AdminTools`.** `Game.Admin`, four command classes, `AdminContextMenuOptions`, `MapWindow`
   constructor dependency. Delete the TODO.md entry.
   *In-game check*: every Admin Mode context-menu entry (Regenerate, Spawn here >, Apply >, Grant loot
   box, Teleport here), and none of them showing with Admin Mode off.
   **Done 2026-09-29.** `WorldSessionContext` no longer holds
   `NeighborhoodStreamer` (only `AdminTools` read it). `MapWindowTests` builds real admin tools
   through a `TestAdminTools` helper rather than `MapWindow` taking a nullable dependency.
4. **`GameCommands`.** Six services, `PlayerInputBuffer` → `PlayerCommands`, migrate every
   Presentation write call site and the `HotbarContent` publishes.
   *In-game check*: drag-drop give/take/loot, shop buy/sell, currency give (including the Angel
   Investor path through `TryGiveCurrencyToShop`), trade confirm, hotbar bind/unbind by drag, movement
   and buffered actions (Dodge, delayed windup, stagger drop).
   **Done 2026-09-29.** Presentation takes the specific command services it
   uses; `GameCommands` is only the holder on `GameSession`. `HotkeySlotLayout.IsLocked` stays in the
   hotbar (a drop-target rule); every other binding rule moved into `HotkeyBindingCommands`. Tests build
   `UiInputController` through `TestUiInputController.Create`, which keeps the old argument list.
5. **`GameViews` read side.** Views in the table, migrate reads in scope, add the architecture test.
   This phase is the largest by file count (~24 files plus their tests).
   *In-game check*: every window in scope opened and exercised -- inventory tabs/search/sort,
   Item Details, Compare across shop/trade, shop price bands, HUD bars, status effects, ability
   scores, hotbar cooldowns.
   **Done 2026-09-29.** Differences from the design above:
   - The architecture test bans the *store* -- `ComponentManager`, `EntityManager`, any pool -- not
     component values, which the views hand back (the design allowed both, which contradicted). It
     scans all of Presentation, exempting only `MapTintGrid`, `InspectionWindowContent` and
     `DiagnosticsWindow` by name, so new code can't regress.
   - Views: `InventoryView`, `ShopView`, `CurrencyView`, `HotkeyBindingView`, `HealthView`,
     `StatModifierView`, `AbilityScoreView`, `ActionStateView` (action lock, mana, potion cooldown,
     windups, including the Local-tier windup scan moved out of `ActionTargetingController`),
     `TransformView`, plus the existing `EntityNaming`. No `ItemActivationView`: scroll scaling reads
     no pool, so callers use `ScrollScalingEffects` with the score from `AbilityScoreView`. Pure
     arithmetic (ShopStockPricing's bulk and band prices, `InventoryQueries.TryResolveEffectiveItem`)
     stays a direct static call.
   - The item windows, grid and currency row take one `InventoryServices` record (item catalog, four
     views, three commands) instead of seven parameters each.
   - Two writes Phase 4 missed: `LootCommands.MarkLooted` (the loot window's `LootedComponent`) and
     `PlayerCommands.TryCancelWindup` (Escape cancelling a windup).
   - `HealthWindow`'s row builders take lists copied from the views, not pools.
6. **Coordinator, `PointerState`, `ShellServices`.** Move the wiring, slim `ShellBootstrapper`,
   `ElementFactoryRegistry` takes `PresentationContext`, `GameSession`, `ShellServices`, `AdminTools`.
   *In-game check*: corpse/shop mutual exclusion, trade opening with shop, item click vs Compare armed,
   loot-box open and reward clicks, a looted corpse or open shop entity destroyed while its window is
   open, drag ghost and cursor text.

   Phase 6 **implemented 2026-09-29, awaiting in-game check.** `Build` is 94 lines (file 784 → 596).
   Tests: `ItemWindowCoordinatorTests` (6), `PointerStateTests`; `MapWindowTests`' builder moved into a
   shared `TestMapWindows` helper. The "Afterwards" doc updates below are done: CLAUDE.md, the
   data-layer plan's Stage 1 and 4 notes, and IMPLEMENTATION-NOTES.md "Shell composition cleanup".

Afterwards: CLAUDE.md's Layers/Modding sections name `GameSession`, `GameCommands`/`GameViews` and the
rule above; PLAN-presentation-data-layer.md's Stage 1 and 4 notes point here for what was done; an
IMPLEMENTATION-NOTES.md entry records the decisions below.

## Tests

- **Phase 1**: `GameBootstrapperTests` assert `GameSession` members are the context's own instances
  (same catalog, same clock) rather than copies.
- **Phase 2**: an `AchievementModule` test over `BuiltInTestModules.Build`: unlock, publish
  `AchievementNotificationDismissedEvent`, box granted once; publish twice, still once; a minimized
  notification (NotificationCenter test) publishes nothing.
- **Phase 3**: `AdminContextMenuOptions` produces the expected groups with Admin Mode on and none
  with it off.
- **Phase 4**: one wiring test per service (the service reaches the same pools the static function is
  given); existing rule tests stay on the static functions. `PlayerInputBufferTests` move with the
  class and are renamed.
- **Phase 5**: the architecture test; view tests only where a view assembles something new.
  Presentation tests that built pools to feed constructors now build a session through
  `BuiltInTestModules.Build` or a `TestGameSession` helper over a bare `ComponentManager`
  (following `TestSystems`/`EmptyPools`). ~16 test files construct affected types today.
- **Phase 6**: `ItemWindowCoordinator` tests that drive the real `UiInputController`/Arrange pipeline
  (memory: live-testing-catches-what-review-misses) for mutual exclusion, click routing with
  comparison armed, and entity-destroyed cleanup.

## Open decisions (recommendation first)

1. **Read side in this plan (Phase 5)** -- recommended. Without it, most of the 33 files keep
   `ComponentManager` and the constructor lists barely shrink. Alternative: stop after Phase 4 and
   leave reads to the data-layer plan.
2. **Static functions stay as the implementation behind the services** -- recommended. Converting
   Game systems to the services too would put an extra hop on simulation paths for no gain.
3. **`PlayerInputBuffer` moves into Game as `PlayerCommands`** -- recommended; it is gameplay input
   rules. Alternative: leave it in Presentation and only have it constructed from `GameSession`.
4. **Services built by `GameBootstrapper` from built-in pools**, not registered by modules in
   `Configure` -- recommended while no mod needs to replace a command. A module-registered service
   (so a mod replacing Shops can replace `ShopCommands`) can come later without changing callers.
