# Implementation Notes

Durable facts about landed work that don't belong in `TODO.md` (open items only) or a `PLAN-*.md`
(pre-execution design). One section per topic, terse bullets, file/type names over prose. Append new
topics; don't duplicate what a `PLAN-*.md` already records in full (link to it instead).

## Game

### Actions/ActionEffect framework, Scrolls, Wands

- `Game/Modules/Actions/`: `ActionEffect` (composable `IActionEffectEntry` list -- `DirectDamage`,
  `DirectHeal`, `DirectManaRestore`, `HotkeyExpansionGrant`, `StatusEffectGrant`, `StatModifierGrant`,
  `ChainedEffect`, `AuraSourceGrant`) + `IActionActivator` (`PotionActivator`/`ScrollActivator`/
  `WandActivator`/`SpellActivator`) replaced the old `AbilityEffect`/`ConsumableEffect` split. Full
  design: `PLAN-action-effect-activator.md`.
- `TargetShape.AdjacentWithSelf` = Adjacent's ring + the caster's own tile, added so a `Tag.Self`
  scroll/spell can resolve a manual click on the caster's own tile (plain `Adjacent` excludes it).
- `ScrollScalingEffects` scales Range/AreaSize/duration by caster Intelligence (100% @1 -> 400% @300).
- `ScrollMasteryEffects`: 200 uses of scrolls sharing a `SpellId` permanently grants that spell
  (`ActionCatalog` lookup, else synthesized from the scroll's `ItemDefinition`).
- `AuraSourceGrant`: permanent flip-toggle (`DurationFrames: null`, `AuraSourceEffects.Toggle`) or
  timed grant-refresh (`AuraSourceEffects.Apply` + `AuraSourceExpiryComponent`/System). Always targets
  `context.TargetEntityId` -- no separate Source/Target field (tried, removed); self-targeting is a
  Self-shaped `TargetingSpec` instead.
- `WandActivator`: per-instance `Charges`/`MaxCharges` fixed at grant off Intelligence
  (`WandGrantEffects`), ticks down via `InventoryActions.PeelOneIntoDivergentStack` -- first item ever
  granted as a diverged stack. Forced item-hotkey binding to key by `StackInstanceId`, not
  `ItemDefinitionId`. No Equipment gate (doesn't exist yet).

### Move inventory items to hotbar

`ItemHotkeyBindingComponent` + `ConsumableActivationSystem`/`ActionTargetingController` arm/target/
confirm/double-tap path, keyed by `StackInstanceId`. Click-and-drag assignment from the grid landed too.

### Melee attack

Shares `ActionLockComponent` with movement (tactical move-vs-attack tradeoff). Targets any entity in
Adjacent's footprint including non-Blocking (Tiny/Phasing, no-health) -- enables status effects on
otherwise-immortal entities. Punch is the concrete case; Goblins use it via `TestCombatBehaviorSystem`.

### Body parts / Complex health

Full record: `PLAN-body-parts.md`.
- `SimpleHealthComponent`/`SimpleHealthRegenSystem` (single pool, most races) vs opt-in
  `BodyPartComponent` (`MultiComponentPool`, Complex -- Goblin only today). No marker component --
  decided by which components a blueprint grants.
- `HealthQueries.TryGetTotals` is the one chokepoint for current/max HP (Simple first, else sums
  parts). Doesn't fold in the `MaximumHealth` stat modifier -- callers apply that themselves.
- Regen: Simple ticks its pool; Complex regens the single lowest-%-HP part per due entity.
- Death: Simple at 0 HP. Complex the instant any Vital part hits 0 (Goblin: Head+Torso), independent
  of summed total.
- Non-vital part at 0 -> `IsDisabled` + 10s regen lockout (not death). Nothing reads `IsDisabled` for
  gameplay beyond the Limb-specific-penalties note below.
- `HealthDamage.Apply`/`HealthHeal.Apply` dispatch Simple/Complex; targeting mode (single part --
  random/most-damaged/a specific type -- or every part at once) is now shared by damage and heal, see
  "Damage/heal modifier & targeting consistency" below.

### Limb-specific gameplay penalties

Full record: `PLAN-body-part-gameplay-effects.md`. `Game/Modules/BodyPartEffects/BodyPartEffectsSystem`:
Leg/Foot condition -> `StatModifierTarget.MovementLockFrames` (MovementSystem); Arm/Hand condition ->
`StatModifierTarget.OutgoingDamage` scoped to `ConditionTag: Tag.Melee` (DirectDamage -- see below).
Each damaged part's own linear-lerp penalty (1x-2x lock, 1x-0x damage by HP%) compounds multiplicatively
across however many parts the entity has. All-parts-disabled -> hard block
(`MovementDisabledComponent`/`MeleeDisabledComponent`) replaces the multiplier. `BodyPartType.Wing`
suppresses the penalty entirely (unused by any race yet). Movement/melee consumption landed;
lifting/pickup gating still open.

### Damage/heal modifier & targeting consistency

Full record: this session. `StatModifierComponent.ConditionTag` (a `Tag?`) generalizes "this modifier
only applies to e.g. melee/healing/potion activations" -- `StatModifierMath.GetEffectiveValue(s)` take
an optional `activeTags` and skip a modifier whose `ConditionTag` isn't in that list. Replaced the old
one-off `StatModifierTarget.MeleeOutgoingDamage` special case entirely (now `OutgoingDamage` +
`ConditionTag: Tag.Melee`); `BodyPartEffectsSystem`'s own grant/find/remove now key off
`(Target, ConditionTag)` together, not `Target` alone, since a player-granted modifier can now share a
target with a body-part-condition-granted one.
- `StatModifierTarget.OutgoingHealing`/`IncomingHealing` mirror `OutgoingDamage`/`IncomingDamage`;
  `HealthHeal.ComputeAmount` is the shared flat+percent-then-modifier-chain calculation both the Simple
  path and every `ComplexHealthHeal` path call.
- `DirectDamage`/`DirectHeal` both take `PercentOfMaxHealth` (of the modifier-effective max health,
  `HealthQueries.TryGetEffectiveMaximum`) alongside their existing flat amount (`MinAmount`/`MaxAmount`
  roll, `FlatAmount`) -- combined into one base amount before any other modifier runs.
- `BodyPartTargetMode` (`SingleTarget`/`LowestPercentage`/`All`) is shared by both effects.
  `SingleTarget` (default for damage) keeps today's random-or-specific-type-with-fallback behavior.
  `LowestPercentage` targets the single most-damaged part (`BodyPartSelection.PickLowestPercentage`,
  previously only reachable by passive regen). `All` (default for heal, preserving every existing
  potion/scroll's "heals everyone" behavior) computes the total **once** against the entity's overall
  effective max, then splits it evenly across however many parts exist -- deliberately not a per-part
  percentage, so a flat amount (or an additive modifier) isn't multiplied by body-part count.
  `ComplexHealthDamage.ApplyToAllParts`/`ComplexHealthHeal.ApplyToAllParts` are the respective
  implementations; damage publishes one aggregate `EntityDamagedEvent`/`EntityDiedEvent` pair for the
  whole hit rather than one per part (`BodyPartDamageEffects.PublishAggregateDamageEvents`).
- **Status effect version** (prevention/effectiveness/duration for Poison/Burning and any timed
  `StatModifierComponent` grant): `StatusEffectImmunityComponent` (`Game/Modules/StatusEffects/`) is
  a hard on/off gate, not a StatModifier scale -- timed (`RemainingDurationFrames`, null =
  permanent, ticked by the new `StatusEffectImmunityExpirySystem`) or permanent, checked by
  `StatusEffectImmunity.IsImmune` at the true chokepoint each effect already funnels every grant
  through (`PoisonEffects.ApplyStack`, `BurningEffects.ApplyStack`,
  `BurningAuraApplier.ApplyBodyPartScopedStack`, `ParalysisEffects.Apply`), each of which now also
  takes optional `EventBus?`/`IPlayerQuery?` params (threaded from each module's own `Configure`-captured
  fields) so a blocked grant publishes `StatusEffectImmunityBlockedEvent` -- player-involved-only,
  mirrors `EntityDamagedEvent`/`EntityHealedEvent` -- logged by `PlayerActivityLog` as a `BLOCKED` line.
  `StatusEffectsModule` is now an `IGameModule` (was
  a plain `IModule`) purely to reach `ProcessingTierEvents` in `Configure` for that expiry system --
  any test building an `EcsContext` via the raw `Engine.Bootstrapper.Build` (not `GameBootstrapper`)
  must now call `.Configure(context)` on it too, same as every other `IGameModule` in that list
  (see `FloorBuilderTests.BuildEcsContext`). `Tag.Poison` (new) and `Tag.Fire` (already existed) are
  now threaded as `damageTags`/`activeTags` into Poison/Burning's own `HealthDamage.Apply`/
  `StatModifierMath.GetEffectiveValue` calls, so a `ConditionTag`-scoped `IncomingDamage` modifier
  reduces one damage type specifically -- no new `StatModifierTarget` needed for that pillar.
  `StatModifierTarget.Outgoing/IncomingBuffDuration` and `Outgoing/IncomingDebuffDuration` (4, split
  by the granted modifier's own `Polarity`) scale a `StatModifierGrant`'s `DurationFrames` and
  `PoisonEffects.ApplyStack`'s own `durationInTicks` (unconditional there -- an aura-refreshed grant
  has no real activator to scope a `ConditionTag` against). Burning has no independent duration to
  scale (a stack's own decay -- one removed per tick -- is its only duration signal, and that same
  `StackCount` also drives its damage) -- deliberately not attempted. Two real, catalog-registered
  test potions (`ImmunityTestPotion`/`ResistanceTestPotion`, granted `quantity: 5` in
  `PlayerBlueprint` like every other starting potion) exercise all three pillars end-to-end.
- `EntityHealedEvent` mirrors `EntityDamagedEvent` (player-involved-only, consumed by
  `PlayerActivityLog`'s new `HEAL` log line) -- published by `HealthHeal.Apply`/`ComplexHealthHeal` when
  both an `EventBus` and `IPlayerQuery` are supplied (both optional, unlike `HealthDamage.Apply`'s
  required `eventBus`, since most low-level heal callers/tests have no need to observe one landing).
- `SimpleHealthRegenSystem`/`ComplexHealthRegenSystem` now route their own periodic tick through
  `HealthHeal.Apply` (`flatAmount`: the live Constitution-derived amount, `sourceEntityId`: the
  entity itself -- a self-heal) instead of mutating health inline, so a regen tick also carries
  `OutgoingHealing`/`IncomingHealing` and logs a `HEAL type=Regeneration` line. `ComplexHealthRegenSystem`
  now takes a `PackedComponentPool<SimpleHealthComponent>` purely to satisfy `HealthHeal.Apply`'s
  Simple-vs-Complex dispatch check (always resolves Complex for the body-parts-only entities it
  drives) -- mirrors `ComplexHealthDamage.Apply`'s identical requirement.

### Corpse looting

- Right-click "Loot" (disabled if not adjacent) opens `InventoryManagementWindow` +
  `CorpseInventoryWindow` (`Presentation/UI/Looting/`), both menu-mode windows.
  `SecondaryInventoryWindowController` owns open/close/replace, written generically for chest/shop
  reuse.
- Items drag both directions via `InventoryActions.TryTransferStack`/`TryTransferAllStacksOfItem` (no
  auto-merge into destination). `UiInputController` locates the drop target's grid via `Element.Tag`,
  not `Window.Content` (some grids never set Content -- was a real bug source, fixed).
- Non-player inventories capped at 20 distinct stacks (`InventoryCapacity.MaxNonPlayerStackCount`);
  player unlimited.
- No real loot table yet -- Goblins/Fairies/Ghosts get a **temporary** random 0-20-stack inventory
  (`TemporaryNpcLootGrant`).

### Toggle poison aura ability -- item side

Toxic Idol (`Game/Modules/Inventory/Definitions/ToxicIdol.cs`) is the first user of `AuraSourceGrant`'s
permanent flip-toggle. Built the aura-sync fix (`AuraSourceAddedEvent`/`RemovedEvent`) and
multi-aura-per-entity support. FreeCast ability version still open (see TODO.md).

### Goblins attack adjacent targets (temporary stand-in)

`TestCombatBehaviorSystem`: self-heal -> melee-adjacent-threat -> wander chain, generic to any
`MovementMode.Random` entity with the right components (not Goblin-specific). Non-Blocking targets
attackable too. Known gap: a Fairy attacks other Fairies (no same-race exclusion) -- left for the
behavior-composition follow-up (see TODO.md).

### Stats / AbilityScores infra

`Game/Modules/AbilityScores/`: `AbilityScoreComponent` (1-300, precomputed `Total`) for 5 Core
(Str/Int/Con/Dex/Cha) + 2 Hidden (Luck/Wisdom, never shown/level-up). Grant modifiers via
`AbilityScoreEffects.GrantModifier`, not raw `StatModifierEffects.Apply` -- keeps `Total` in sync
(precomputed eagerly, unlike other stats). Player rolls 2-10 (cluster 3-7); every other race flat 5
(placeholder). Consumer wiring still open (see TODO.md).

## Presentation

### FontService lifetime, and a test-only FreeType finalizer crash

- `FontService` (`Presentation/Fonts/FontService.cs`) is now `IDisposable`, disposing its owned
  `FontStashSharp.FontSystem` -- that type owns real native FreeType face/library handles
  (`FNA.NET.FontStashSharp` bundles FreeType as its built-in rasterizer, needed for
  `DroidSansJapanese.ttf`/`Symbola-Emoji.ttf` coverage the pure-managed StbTrueType path can't
  provide). Nothing calls `Dispose` in production (`PresentationBootstrapper.Build` creates exactly
  one `FontService` for the whole game process, harmlessly left for the finalizer at exit).
- The test suite used to construct 45+ independent `FontService` instances across ~21
  `Tests/Presentation/*` files, none ever disposed -- each undisposed native FreeType context only
  ever got cleaned up by its own finalizer, and enough of them competing for finalization near
  test-host shutdown caused an intermittent, unrecoverable `0xC0000005` access violation inside
  `FreeTypeSharp.FT.FT_Done_Face`. Fixed by `Tests/Presentation/TestFonts.cs`: exactly one shared
  `FontService` for the whole test run (matching production's own proven-safe single-instance
  case), combined with `[DoNotParallelize]` on every one of those 21 test classes -- a single
  mutable `FontSystem`'s dynamic glyph atlas is not thread-safe, and `MSTestSettings.cs` runs tests
  in parallel by default, so a genuinely shared instance without that attribute corrupted glyph
  measurements under concurrent access instead (a different, correctness-not-crash bug, ruled out
  before landing this). See `TestFonts.cs`'s own doc comment for the full reasoning, including why
  a `[ThreadStatic]`-per-worker instance (tried in between) still crashed intermittently and wasn't
  enough on its own.
- While chasing this, found (and fixed) an unrelated pre-existing flake:
  `MapWindowTests.HandleHotkeys_PressingArmedItemSlotAgainWithNoHoveredTile_DoesNothingAndStaysArmed`
  (and its Action-side twin, `...PressingArmedSlotAgainWithNoHoveredTile_DoesNothingAndStaysArmed`)
  failed independent of any of the above (reproduced against the file at `HEAD`, before any font
  work). Root cause: `MapWindow.Update` reads the real OS mouse cursor (`Mouse.GetState()`) every
  call to drive `UpdateHoveredTile` -- both tests looped `Update` 20 times expecting "no hovered
  tile" afterward without ever resetting it, so they were actually asserting against wherever the
  physical mouse cursor sat on the machine running them (confirmed via a diagnostic assert: a real
  run produced `HoveredTile={101,101,0}`, one tile from the player, inside the failing test's own
  Potion Burst/Range-3 footprint -- big enough to often catch a real cursor position, unlike the
  Action test's much smaller Adjacent footprint, explaining why only the Item one flaked
  reliably). Fixed by calling `mapWindow.UpdateHoveredTile(new Point(-1, -1))` (deterministically
  off-map) right before the final `HandleHotkeys` press in both tests.

### Selected item details window + Item Details Comparison

- `ItemDetailsWindow`/`ItemDetailsWindowController`: click a single-stack cell -> persistent details
  pane (sprite/name, Effects, Activation w/ targeting-shape preview, Description, Tags). Closes via own
  button or outside-click. `DisplayMode.WrapContent` (not `Fixed`) required for correct re-measure
  across rebuilds -- a shrinking `Fixed` window re-measures against its own stale small size (was a
  real bug: Tags rendered over Description).
- `ItemComparisonController`: gated to same-`Activator`-type items only (cross-type diff judged
  meaningless). Each compared item opens its own `ItemDetailsWindow` column, not a shared table; lines
  colored green/red by `ItemComparisonStatExtraction`/`ItemComparisonHighlighting` -- whole-line color
  only, `TextWindow` has no per-substring styling. Columns anchor to a fixed point +
  `WindowCascadePlacement` (fixed a bug where a 3rd+ column could spawn off-screen).
- Equipped-item comparison still open (blocked on Equipment).

### Action/item ToString formatting

`ActionEffectFormatting.FormatEntry`, `ActionActivatorFormatting.BuildLines`,
`TargetShapePreviewGeometry`/`Element` (`Presentation/UI/`) -- all take plain `Game.Modules.Actions`
types, so a future Magic Menu gets them free. Frame counts always shown as seconds.

### Inventory tabs/search/sort/GridControl/Toggle

- Auto-generated per-tag tabs (`InventoryTagQueries`), sorted by stack count then alphabetical.
  `TabbedContent` supports scrollable, runtime-rebuildable tabs. User-reordering and a custom-tag
  trailing tab still open (see TODO.md).
- `InventoryGridContent.SortOrder`/`NameFilter`/`HideDisabled`, driven by `GridControl` -- a fully
  generic (non-Inventory-specific) row of grid controls (count, sort, `DebouncedTextFilter` search,
  toggle list) via `InventoryTabContent`. Full design: `PLAN-inventory-item-filtering-and-tab-stats.md`.
- Tab search: debounced (300ms) ghost-text box (`TextBox.GhostText`); shared logic lives in
  `DebouncedTextFilter`.
- `Toggle` (`Presentation/UI/Toggle.cs`): generic checkbox widget -- bordered square +
  `LabelPosition`-placed label, `Action<bool>` callback. Not Inventory-specific despite landing there
  first.

### Context menu

- `ContextMenu`/`ContextMenuController`: shared popup; each right-click source supplies its own
  `ContextMenuOption` list (shared mechanics, distributed content).
- `AdvancedMapContextMenu`: right-clicking a tile stacks every occupant's + terrain's own option group
  under a header row; the same generic `Element.OnRightClicked` hook drives 4 more menus (Window
  Close/Close All, Notification popup, NotificationSummary, Inventory item Give/Take).
- Still open: TextBox context menu wiring, "Bind To..." sub-menu (needs cascading sub-menus) -- see
  TODO.md.

### Inspection V2 / Player selection menu

`InspectionWindow`/`InspectionWindowContent` replaced debug-only `SelectionWindowContent`. Basic mode
(click tile): curated view. Detail mode (right-click -> Inspect): single-target follow, shared
cooldown, always appends the old raw `ComponentInspector` dump as an Admin section. Skill-gated content
depth still open (blocked on Skills).

### HealthWindow

Full record: `PLAN-health-window.md`. Red-heart `Button` (`HealthWindowController`) opens
`HealthWindow`: one row per body part (modifier-effective current/max), one Status Effects section
above (each effect has its own duration formula -- no shared "remaining" field exists).
`WindowLifecycle<T>` (renamed from `WindowSlot<T>`) now shared by 3 window-toggle consumers. Not done:
Vital/disabled state per part not shown; parts list in pool order, not anatomical position.

### Ability Score window

`AbilityScoreWindow` (`Presentation/UI/AbilityScores/`) exists alongside Inventory (same
Folder+pooled-Window pattern). Displays the 5 Core scores' `Total` + a buff/debuff origin popup
filtered from `MultiComponentPool<StatModifierComponent>`. Stat-point assignment on level-up blocked on
level-up existing.

### Text input + Text Input Enhanced Features

- `TextBox : TextWindow` (reuses wrap/scroll/draw). Three input hooks: `OnTextInputAction` (typed
  chars, via FNA `TextInputEXT.TextInput`), `OnKeyPressAction` (Backspace), `OnHotkeysAction` (Enter,
  Shift+Enter for multiline). `TextSubmitted` event; auto-focus-redirect into a window's first TextBox
  (`Window.NextTextBoxAfter`).
- Enhanced: cursor-addressable editing, arrow-nav (incl. word/line jump), click/double/triple-click
  select + drag-select, blinking caret, full selection (Ctrl+A, word-delete), Ctrl+C/X/V clipboard,
  key-repeat, I-beam cursor, single-line horizontal clip-scroll.
- Not landed: undo/redo, TextBox context menu wiring -- see TODO.md.
- First consumer: quest-composer popup (`GameShellBootstrapper.OpenQuestComposer`) -- **TEMPORARY**,
  keep until a real second TextBox consumer exists (per project memory).

### Text copy to clipboard

Resolved via Ctrl+C/X (Text Input Enhanced Features), not click-to-copy (rejected -- too easy to
trigger by accident, conflicts with `TextWindow.OnContentClickAction` firing before `Clicked`).

### Investigate TextWindow draw cost

Root cause: every `NotificationCenter` popup built `CanUserScrollVertical = true` unconditionally ->
`RequiresContentViewport` true -> 2x SpriteBatch End/Begin + Viewport/Scissor swap per popup per frame
regardless of overflow, flushing the whole frame's batch early. Fixed: (1) `Element.Draw`'s
child-scissor pass only runs when `_children.Count > 0`; (2) `NotificationCenter.ShowActive` turns
scrolling back off post-`Initialize()` if `MaxScrollOffset` is 0.

## Pause modality

`UiLayerStack`'s "menu mode" already implements the generalized modal concept TODO.md's old "Pause
modality" item asked for (input-block + dim for any window that opts in, reusable with zero new
GameLoop code) -- built in a past session, just never reflected back into the TODO text.

- `IsMenuModeActive` (`_menuWindows.Count > 0`) is what `GameLoop.Update` reads:
  `!(MapWindow.IsPaused || Layers.IsMenuModeActive)`. `NotificationCenter.HasBlockingNotification` and
  the old `Inventory.IsAnyWindowOpen` still exist but neither is read by `GameLoop` anymore.
- `OpenMenuWindow(window)`/`CloseMenuWindow(window)` -- any window opts in with one call each way.
  4 consumers: `NotificationCenter` (System notifications), `WindowLifecycle<T>.Open` (Inventory,
  Ability Score). A future Equipment/Options menu needs zero new modality code.
- Input blocking, gated by `IsMenuModeActive`: mouse (`TryHitTestInteraction` -- only ContextMenu tier,
  open menu windows, `User`/`Tooltip`, and `MarkMenuModeExempt` elements are reachable), keyboard
  (`RouteHotkeysToFocusedElement` via `IsReachableDuringMenuMode`), Escape (`BroadcastToElements` sweep
  skipped).
- Dim: `MenuModeDimRenderer.Draw` -- one `Color.Black * 0.55f` full-viewport quad, drawn by
  `ShellContext.Draw` right before `Layers.BottommostMenuWindow`.
- `MarkMenuModeExempt(element)`: 4 call sites -- hotbar window, Notification folder tile, Inventory
  folder tile, Health window's opening button (persistent HUD entry points that must stay reachable).

**Design decisions (not gaps):**
1. Menu mode is menu-vs-everything-else, not menu-window-vs-menu-window -- multiple menu windows (e.g.
   Inventory + Ability Scores) stay independently clickable. Deliberate; a future window needing true
   exclusivity needs its own mechanism.
2. Escape only dismisses the topmost menu window (`TopmostMenuWindow`, raise-to-front order) -- the one
   place menu-window exclusivity does apply.
3. `MapWindow.IsPaused` (Space) stays separate and non-dimming -- a tactical freeze, not "a panel is
   open"; conflating the two would block using the hotbar/inventory while paused.
4. No `UiLayer` tier above `ContextMenu` is needed for menu mode -- every menu window is treated
   equally.

**Test coverage**: `Tests/Presentation/UiLayerStackTests.cs` + additions to `UiInputControllerTests.cs`
are the first dedicated coverage (previously one incidental test touched any of this).

## Global

### Clean up unit tests

`dotnet test` had drifted to 25 failures across 6 independent root causes, all fixed:
- `MapWindowTests.cs` camera math: `MapCamera.BaseTileSizePixels` is 36, test file assumed stale 18px.
  Added `TileSizePixels`/`ViewportColumns`/`ViewportRows`/`ScreenCenterColumn`/`ScreenCenterRow`
  constants, verified against the real camera.
- `ContextMenuController.Open` NRE: `ElementPoolService.GraphicsDevice` never wired in tests. Fixed via
  internal `ScreenBoundsOverrideForTests` + `TestElementPoolServiceFactory.CreateContextMenuController`.
- `FreeIdPool`/`EntityManager`: `Release` on an unissued/already-released id is a deliberate no-op, not
  a bug -- tests rewritten to assert the no-op contract.
- `PlayerActivityLog.DescribeEntity`: doc comment and output never agreed (doc said `"id (Name)"`, code
  produced `"Name (#id)"`) -- fixed the doc comment + tests to match the real output.
- `Fairy.PunchDamage` balance edit (5->3) had left a stale test literal.
- `SimpleHealthComponent.ToString()` casing typo in test assertion ("invalid" vs "Invalid").
