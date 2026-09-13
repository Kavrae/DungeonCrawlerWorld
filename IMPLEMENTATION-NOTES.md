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
- `TargetShape.Adjacent | TargetShape.Self` = Adjacent's ring + the caster's own tile, used so a
  `Tag.Self` scroll/spell can resolve a manual click on the caster's own tile (plain `Adjacent`
  excludes it). `TargetShape` is a `[Flags]` enum specifically so this composes instead of needing
  a dedicated `AdjacentWithSelf` value (retired) -- see `TargetShapeResolver`'s own doc comment.
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
- `ActionInstanceComponent.Override: ActionDefinition?` replaced the old bare `DamageAmount: ushort`,
  mirroring `InventoryItemStackComponent.Override`'s shape/resolution pattern -- a full nullable clone
  of the catalog definition (any field, not just damage), resolved via
  `ActionInstanceQueries.TryResolveEffectiveAction` (Override if set, else `ActionCatalog.TryGet`).
  `ActionActivationSystem`/`DelayedActionSystem` resolve `instance` first, then the effective `action`
  off it -- `ActionEffectResolver.Apply` no longer takes `instance` at all, and
  `ActionEffectContext.DamageOverride` is gone (a fixed value is now just `DirectDamage` with
  `MinFlatDamage == MaxFlatDamage` baked into the granted `Override`, built via
  `ActionOverrideEffects.OverrideFlatDamage` so a targeted `with` on the matched entry can't silently
  drop unrelated fields, e.g. Magic Missile's `TargetBodyPartType: BodyPartType.Head`).
  `PendingActionActivationComponent` needed no change -- same as Items, it only ever carried an
  identity reference.

### Move inventory items to hotbar

`ItemHotkeyBindingComponent` + `ConsumableActivationSystem`/`ActionTargetingController` arm/target/
confirm/double-tap path, keyed by `StackInstanceId`. Click-and-drag assignment from the grid landed too.

### Melee attack

Shares `ActionLockComponent` with movement (tactical move-vs-attack tradeoff). Targets any entity in
Adjacent's footprint including non-Blocking (Tiny/Phasing, no-health) -- enables status effects on
otherwise-immortal entities. `QuickAttackAction` (renamed from `PunchAction`) is the concrete case;
every race uses it via `TestCombatBehaviorSystem`, which now also randomly picks `PowerAttackAction`
instead (see Combat Overhaul: Dodge below).

### Combat Overhaul: Dodge

Core mechanic landed per `TODO.md`'s own section (still open there: Block/Counterspell, the
AdvancedDodge buff, and independently validating the 0.5s-1.0s window against Dark Souls 3/other
games' own dodge timings -- the numbers used are TODO.md's own, not independently researched).

- Three default actions every race grants (`Game/Modules/Actions/Definitions/DirectActions/`):
  `QuickAttackAction` (Immediate, replaces `PunchAction` in place, same catalog `Id`, default R key),
  `PowerAttackAction` (new, `Delayed`, 1s windup, default Q key), `DodgeAction` (new, `FreeCast`, own
  flat 4s `CooldownFrames`, default F key). All three tagged `Dodgeable` except Magic Missile/AOE-style
  effects, which stay untagged (undodgeable) -- `Tag.Dodgeable` gates `ActionEffectResolver.Apply`'s
  per-occupant skip against a target holding `DodgingComponent`. Dodge is gated by its own cooldown, not
  an increased shared-lock multiplier -- a `FreeCastLockMultiplier` seam on `ActionTiming` was tried and
  removed; `ActionTiming.CooldownFrames` (already shared by every other `FreeCast` action) covers this
  with no new mechanism needed.
- `DodgingComponent`/`DodgeExpirySystem` (`Game/Modules/Actions/Components|Systems/`): a short,
  Dexterity-scaled immunity window (`DodgeEffects.ComputeWindowFrames`, 0.5s @ Dex 1 -> 1.0s @ Dex 300,
  `AbilityScoreMath.Lerp` low-to-high since more Dexterity is the benefit here) granted by
  `DodgeActivation` (`DodgeAction`'s sole effect). Rendered as a light green inner-fade glow framing
  the entity's own footprint (`MapWindow`, `GlowRenderer.Draw(..., GlowMode.InteriorFade,
  alphaMultiplier: 2)` -- boosted past the default ring alpha, which read as too subtle for a
  status cue this brief -- the same ring technique `DrawSelectedTileGlow` already uses) while active
  -- a sprite-opacity fade was tried first and read as the entity vanishing outright rather than as a
  status cue (confirmed live). A third, deeper bug briefly made the glow not appear *at all* even
  after the opacity->glow switch, for directional dodges specifically: `ActionEffectResolver.Apply`
  only ever invokes an action's effects against entities `IMapQuery.GetOccupantEntityIdsAt` finds
  occupying the *resolved target tile* -- but `TryRelocateForDodge` only queues the caster's move via
  `MovementComponent.NextMapPosition` (see below), so at the moment `ActionActivationSystem` processes
  the FreeCast activation, that destination tile is still empty; the occupant loop found nobody there
  and `DodgeActivation` never ran, so `DodgingComponent` was never granted (self-dodge in place was
  unaffected, since its target tile is always the caster's own current, occupied tile). Fixed with two
  changes: `ActionTargetingController.QueueActionActivation` now stores the caster's own *current*
  tile as `PendingActionActivationComponent.TargetTiles` for Dodge specifically (guaranteed occupied),
  while `TryRelocateForDodge` still separately decides the actual movement destination; and
  `DodgeActivation.Apply` now reads/writes `context.SourceEntityId` instead of `context.TargetEntityId`
  so the grant always lands on the actual caster even if another entity happens to share that tile
  (e.g. a co-located Tiny/Phasing occupant).
- `Engine/Math/TargetShape` is now `[Flags]` (`AdjacentWithSelf` retired -> `Adjacent | Self`, see the
  Actions section above) and `TargetingSpec` gained `Metric: DistanceMetric` (Manhattan/Chebyshev,
  `SingleTarget`-only). `DodgeAction`'s own targeting -- `SingleTarget` + `Metric.Chebyshev` + `Range: 1`
  -- is "pick exactly one tile out of the caster's own 3x3 block," resolved at confirm time by
  `ActionTargetingController.TryRelocateForDodge`, which *queues* the move via `MovementComponent.
  NextMapPosition` -- the exact same path `PlayerMovementController` uses for ordinary WASD movement --
  rather than applying it directly. Two confirmed bugs both came from an earlier version that called
  `World.MoveEntity` directly instead: (1) `World.MoveEntity`/`MoveEntityUnchecked` only ever update
  `Map`'s own occupancy index, never the mover's `TransformComponent.Position` (that's the caller's own
  job; `MovementSystem.TryMoveToNextMapPosition` does this itself, separately) -- skipping it desynced
  the two and made the player's sprite vanish entirely after a directional Dodge (`MapWindow.
  DrawPrimaryOccupant` only draws from the tile `TransformComponent.Position` still names). (2) calling
  `World.MoveEntity` directly bypassed `NextMapPosition` entirely, so any *already*-queued ordinary-
  movement destination (e.g. mid-stride from rapid WASD just before dodging) went stale and unresolved --
  `MovementSystem` would later "catch up" on it and move the player right back, reading as the dodge
  silently reverting. Routing through the one shared `NextMapPosition` queue instead of a second,
  uncoordinated move path fixes both: there is only ever one pending destination, whichever was set most
  recently, and `MovementSystem`'s own occupancy/wall/diagonal-corner validation covers "dodge in place
  if occupied" for free. Trade-off: the actual relocation, like any other queued move, waits for the
  shared `ActionLock` to clear if the entity is already locked -- `DodgeActivation`'s own immunity grant
  still applies instantly regardless, since `FreeCast` never gates on the lock. `TargetShapeResolver
  .Resolve` also gained a verified redundant-resolve shortcut (`SingleTarget <= Line <= Cone` for the
  same origin/cursorTile/Range, once `ResolveCone`'s extent check moved from Euclidean to Chebyshev) and
  de-duplicates combined-flag results.
- Dodge confirms via the same hotkey (self, in place -- generalized off `Tag.Self`, which also fixed a
  latent bug where re-pressing any Self-shaped action's hotkey only worked if the cursor happened to be
  hovering the caster), a click (self or an adjacent tile), or a fresh WASD press while armed
  (`ActionTargetingController.TryClaimDodgeDirectionalKey`) -- the last of which claims the key in a
  small per-frame set (`MapWindow._claimedKeysThisFrame`) so `PlayerMovementController.HandleInput`
  skips it that frame instead of also moving normally.
- A confirmed bug: double-tapping Dodge's hotkey read as it silently cancelling instead of activating.
  `TryActivateWithAutoTarget` (the double-tap path) filters the reachable set down to *occupied* tiles
  before picking one via `ClosestPointSelector` -- exactly what QuickAttack/PowerAttack/ToxicStrike/
  MagicMissile want (double-tap = auto-attack the nearest enemy), but Dodge's own reachable 3x3 block is
  normally all-empty, so the filter found no candidate, queued nothing, and the caller's own "now that
  it fired, disarm" cleanup then read as a cancel. Fixed by giving `TryActivateWithAutoTarget` the same
  `Tag.Self` special case the single-press re-confirm path (`HandleActionSlotPress`) already has: a
  `Tag.Self` action (Heal, Dodge) always auto-targets the caster's own tile directly, bypassing the
  occupied-tile hunt entirely rather than needing an occupant to exist.
- `MapWindow` telegraphs every entity's (not just the player's) in-flight `PendingDelayedActionComponent`
  every frame (`ActionTargetingController.AllPendingDelayedActionTargets`, a small `PackedComponentPool`
  -- dense iteration, no spatial index needed): dark green for the player's own, red/yellow for an
  enemy's depending on `Tag.Dodgeable` (`CombatTargetPalette`). A charging entity (player included) also
  gets its action's sprite/glyph badged above its footprint (`MapWindow.DrawChargingBadge`) -- item
  windups are out of scope, nothing today has a delayed/charging item activation. While Dodge is armed,
  its four cardinal reachable tiles are labeled with their WASD key (`DrawDodgeDirectionalHints`).
- `TestDummyBlueprint`/`TestDummyComponent`/`TestDummyAttackSystem` (`Game/Blueprints/NPCs/Generic/`,
  `Game/Modules/NpcBehavior/`): a stationary, high-regen (`Constitution` 300) practice target spawned a
  few tiles from the player, unconditionally re-firing `PowerAttackAction` against its own Adjacent ring
  whenever its `ActionLockComponent` clears -- no engage/chase logic, unlike `TestCombatBehaviorSystem`.
  Grants its own `PowerAttackAction` override adding a flat `CooldownFrames` (windup + 3s) on top of the
  base action's, so it idles 3s after the attack actually *lands*, not from windup start (`ActionInstanceComponent
  .CooldownFramesRemaining` starts counting the instant activation begins, not once `DelayedActionSystem`
  later applies the effect). Also explicitly grants `ProcessingTierComponent(Local)` -- a real, confirmed
  bug otherwise: `ProcessingTierSystem`'s own membership is driven off `MovementComponent` (see its own
  doc comment), so a `MovementComponent`-less entity like this one is never visited by it and never gets
  a real tier computed, permanently reading as the `Beyond` fallback to every *other* tiered consumer
  (`ProcessingTierWiring`'s "fail open to Beyond" default for "no component yet"). `ActionLockSystem`/
  `ActionCooldownSystem`/`SimpleHealthRegenSystem` are all tiered off this component and each decrements
  its own countdown by a flat per-visit amount sized for `Local`'s cadence -- at `Beyond`'s 8x-less-frequent
  cadence (`ProcessingTierDivisors.ByTierIndex`), that same flat decrement ran every one of those
  countdowns (this dummy's own windup/cooldown/regen) roughly 8x slower than intended. Hardcoding `Local`
  (this dummy always spawns beside the player and never moves, so it's never actually wrong) sidesteps
  the whole bug class without granting a real `MovementComponent` purely to be tracked.
- `TestMapBuilder`'s population percentages (`GroundPopulationPercent`/`UnderGroundGhostPercent`/
  `FlyingFairyPercent`) halved again (5/3/3 -> 3/2/2) for the new deliberate, telegraphed combat pace --
  on top of, not instead of, the earlier FPS-driven halving already there.

### Enemy Attack Indicator + follow-up combat/performance work

Full record: `PLAN-charge-attack-fill-indicator.md` (four addenda -- smoothing, an O(D*A) prune bug,
Local-tier scoping, and moving that scoping upstream into `ActionTargetingController
.AllPendingDelayedActionTargets`). The charge-fill indicator itself: TODO.md's own top-priority
"Enemy Attack Indicator" -- a bottom-up per-tile fill (`Presentation/Rendering/TileFillRenderer.cs`)
showing a Delayed action's windup progress (0% at charge start -> 100% at activation), layered over
the existing flat telegraph wash from Combat Overhaul: Dodge above. Two further fixes landed
alongside it, found while chasing the framerate regressions that surfaced during that work:

- **`TestCombatBehaviorSystem`'s `IsAttackable`** (`Game/Modules/NpcBehavior/Systems/`) no longer
  allowlists "the player or a Fairy" -- it now compares real `RaceComponent` values, attackable iff
  the candidate carries a race different from the attacker's own (and isn't dead, checked first). A
  raceless candidate (a shop, a container, any non-creature prop) is never attackable -- nothing to
  compare. This also fixes two real bugs the old allowlist had: a dead Fairy's corpse (stays fully
  populated and occupying its tile for future looting, `DeathSystem` never destroys it) used to
  still read as a valid target; and a Fairy adjacent to another Fairy used to attack it too, since
  nothing excluded "an entity of my own race." Both are gone now as a natural consequence of the
  real comparison, not a special case. `_playerQuery`/`IsFairy` became dead code and were removed
  (from `TestCombatBehaviorSystem` and its owning `NpcBehaviorModule`) -- the player is "a different
  race" the ordinary way, by being Human (`PLAN-human-race.md`), no special-casing needed.
- **`DelayedActionSystem`** (`Game/Modules/Actions/Systems/`) was the single biggest framerate
  contributor found via the `phase-performance-testing` skill: a live diagnostics capture showed
  `PendingDelayedActionComponent.Count` over 10,000 map-wide during ordinary NPC-vs-NPC combat, and
  this system -- alone -- costing ~79ms of a 1000ms/sec budget, because unlike every sibling
  countdown system (`ActionLockSystem`/`ActionCooldownSystem`/`StatModifierExpirySystem`/
  `BurningSystem`, all `TieredEntityStripeSet`) it used a flat, untiered `StripeCount = 1` -- visiting
  every single pending entity, every single frame, forever. Now tiered off `ProcessingTierComponent`
  via `ProcessingTierWiring.CreateAndWire`, `StripeCountValue = 10` matching `ActionLockSystem`'s own
  (the two need to visit a given entity on the same cadence -- see below). Measured result: 78.95ms/sec
  -> 13.53ms/sec, -82.9%. Deliberately did *not* also adopt the older, independent `ITickCountdown`/
  `CountdownTicker` proposal TODO.md used to carry for this: `DelayedActionSystem` still reads
  `ActionLockComponent.CurrentLockFramesRemaining` directly rather than owning a second, separate
  timer, specifically because two independently-tiered clocks for the same entity could drift out of
  sync (one system's own tiered cadence lagging or leading the other's), delaying -- or worse, racing
  ahead of -- exactly when the lock actually clears. That exact invariant (the lock reaching 0 and the
  action resolving happen in the same tick, always) is what `MapWindow`'s own charge-fill telegraph
  depends on for correctness. Sharing one clock, tiered identically, keeps both the resolution timing
  and the UI's own progress reading consistent, at the cost of the same bounded, self-correcting
  staleness every other tiered consumer here already accepts.
  **Superseded 2026-09-11 (`PLAN-timer-wheel.md` step 7):** both systems are off tiers entirely.
  `ActionLockSystem` is deleted (the lock is a deadline, `ActionLockComponent.UnlockedAtFrame`), and
  `DelayedActionSystem` is a plain `ISystem` driven by a timer wheel over
  `PendingDelayedActionComponent.ReadyAtFrame` -- a copy of that same lock deadline, taken when the
  action is queued. The drift this paragraph guards against is now impossible by construction rather
  than by keeping two cadences aligned, and the system touches only the windups ending this frame.
- **`TestDummyBlueprint`'s own `ProcessingTierComponent(Local)` grant was in the wrong order.**
  Reported live: the charge-fill indicator drew smoothly for every entity except the TestDummy,
  visibly choppy (catch-up-then-pause) there specifically. Root cause: `Build` merged
  `ActionLockComponent`/`SimpleHealthComponent` *before* `ProcessingTierComponent(Local)` --
  `ActionLockSystem`'s/`SimpleHealthRegenSystem`'s own `TieredEntityStripeSet` each read this
  entity's tier exactly once, the instant their own driving component was merged, and cache it
  permanently (`ProcessingTierSystem` never revisits a `MovementComponent`-less entity to correct
  it later). Both read no `ProcessingTierComponent` yet at that point and defaulted to Beyond --
  the exact misclassification the Local grant exists to prevent, just still happening for those two
  systems specifically, because it landed a few lines too late to matter. Fixed by moving the
  `ProcessingTierComponent(Local)` merge to be the first line in `Build`, before anything else --
  see that blueprint's own doc comment for the full mechanism. Confirms this class of bug is about
  grant *order*, not just grant *presence*, for any future stationary fixture following the same
  pattern.
- **Every indicator completed at ~80-90%, never full -- the fraction source itself, not the
  smoothing.** Reported after the TestDummy fix above: consistent for every entity, not just the
  previously-broken dummy. `DelayedActionSystem` resolves an action and removes
  `PendingDelayedActionComponent` in the exact same tiered visit that finally observes
  `ActionLockComponent.CurrentLockFramesRemaining == 0` -- so that raw value is *never actually
  observable at 0* from `MapWindow`; the last frame an entity is ever seen pending, it's frozen at
  whatever `CurrentLockFramesRemaining` held after the second-to-last decrement (up to
  `ActionLockSystem`'s own `StripeCountValue - 1` frames short of true completion), then the entity
  vanishes. For `PowerAttackAction`'s 60-frame windup (decrementing by 10 each visit), the last
  observable state is `remaining=10`, i.e. `50/60 ≈ 83%` -- matching the reported figure closely.
  Fixed by dropping `CurrentLockFramesRemaining` from the fraction entirely: `MapWindow
  .TrackChargeElapsedFraction` (renamed from `SmoothChargeFraction`) now accumulates real elapsed
  frames since each charge was first observed and divides by `CurrentLockTotalFrames` directly,
  reaching exactly 1 at `totalFrames` elapsed regardless of the stepped countdown's own granularity
  -- safe only because the Local-tier filtering above already excludes anything whose real
  resolution could meaningfully lag its nominal duration, the exact case the old "never exceed the
  raw target" clamp existed to guard against. (As of `PLAN-timer-wheel.md` step 7 the stepped source
  itself is gone -- the lock is a deadline and nothing decrements it -- so no tier can lag its
  nominal duration any more; the elapsed-time fraction is kept regardless, since it reads no
  component per frame.) Full record: `PLAN-charge-attack-fill-indicator.md`'s
  own Addendum 7.

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
  `SecondaryInventoryWindow` (`Presentation/UI/Looting/`, renamed from `CorpseInventoryWindow` once
  containers landed -- see `PLAN-storage-containers.md`), both menu-mode windows.
  `SecondaryInventoryWindowController` owns open/close/replace, written generically for chest/shop
  reuse.
- Items drag both directions via `InventoryActions.TryTransferStack`/`TryTransferAllStacksOfItem` (no
  auto-merge into destination). `UiInputController` locates the drop target's grid via `Element.Tag`,
  not `Window.Content` (some grids never set Content -- was a real bug source, fixed).
- Non-player inventories capped at 20 distinct stacks (`InventoryCapacity.MaxNonPlayerStackCount`);
  player unlimited.
- No real loot table yet -- Goblins/Fairies/Ghosts get a **temporary** random 0-20-stack inventory
  (`TemporaryNpcLootGrant`).

### Storage containers and Currency

Full design/rationale: `PLAN-storage-containers.md`.

- `TreasureChest` (`Game/Blueprints/Objects/`): a `Wall`/`Lava`-style stationary prop (no creature
  identity) marked `ContainerComponent` (`Game/Modules/Containers/`) -- lootable via the map's "Loot"
  context menu option even while alive, unlike a corpse (`MapWindow.AddEntityGroup` now gates on
  `_deadPool` OR `_containerPool`). 100 starting health, immune to Poison/Paralysis
  (`StatusEffectImmunityComponent`, permanent) but not Burning. Starts with 1-10 random items
  (stacks of 1-5, `CoreItemsModule`'s definitions) and 0-5 Gold. Uses the existing global
  `InventoryCapacity.MaxNonPlayerStackCount` (20), not a bespoke per-container cap.
- `ContainerDestructionSystem` reacts to the same `EntityDiedEvent` `DeathSystem` does (both
  subscribe independently, no ordering dependency): clears the container's inventory stacks and
  overwrites its `DisplayTextComponent` to "Destroyed" -- a creature's corpse keeps its name/items
  intact, a destroyed container does not. See TODO.md's Destroyed items entry for the eventual
  "mark destroyed instead of delete" follow-up.
- `CorpseLootedComponent`/`CorpseInventoryWindow` renamed to `LootedComponent`/
  `SecondaryInventoryWindow` -- both now apply to containers (and, later, shops), not just corpses.
  `SecondaryInventoryWindowController` (unrenamed, already generically named) is unaffected.
- `Game/Modules/Currency/`: `CurrencyComponent` (`Gold`/`Credits` ints, packed pool, overwrite merge).
  Player grants 1-10 starting Gold only (`StartingCurrencyGrant.GrantRandomStartingGold`);
  Goblin/Fairy grant 1-10 Gold + 0-1 Credits (`GrantRandomStartingGoldAndCredits` -- one `Merge`
  call for both fields together, never two sequential grants, since the overwrite merge policy
  would let the second silently zero what the first just set); `TreasureChest` grants 0-5 Gold +
  0-1 Credits inline. `ToString()` follows `DeadComponent`/`ManaComponent`'s own "Label : Value"
  per-line convention for the admin inspection dump.
- Live-testing find: `TextWindow.Build` reset `OriginalText`/`TextColor`/`Bold` on every pooled
  reuse but never `ContentFont` -- a size set by one consumer (e.g. `HealthWindow`'s bigger buff
  font) leaked into whatever next reused that pooled `TextWindow` (confirmed via
  `InspectionWindowContent`'s admin dump rows rendering at a stale size). Fixed at the same
  pool-reset choke point, not per call site.

### Loot currency

Full design/rationale: `PLAN-loot-currency.md` -- builds directly on the Currency/container work in
`PLAN-storage-containers.md`.

- `CurrencyActions` (`Game/Modules/Currency/`): `TryTransfer(componentManager, source,
  destination, CurrencyType type)` and `TryTransferAll`, mirroring `InventoryActions.
  TryTransferStack`'s shape -- always moves the source's *entire* current balance of that currency
  (no partial amounts yet, see TODO.md's Context menu amount picker entry), reading then writing
  the whole `CurrencyComponent` on each side (never a partial-field `Merge`, since the overwrite
  merge policy would zero the untouched field). No capacity concept -- `InventoryCapacity` is
  purely about distinct stack count, meaningless for a single packed value. `CurrencyType`
  (`Gold`/`Credits`) is a real enum, not an `isGold` bool -- a bool hard-limits to exactly two
  currencies; `TryTransferAll` iterates `Enum.GetValues<CurrencyType>()` rather than naming
  Gold/Credits individually, so a future third currency needs only a new enum case.
- The old static `CurrencyRow` (one read-only `TextWindow` per window) is gone, replaced by
  `CurrencyElement` (`Presentation/UI/Content/`, one per currency -- sprite + "{Label} : {n}" text,
  hover/drag/right-click, mirrors `InventoryItemStackCell`'s shape) and `CurrencyRowContent` (owns
  both elements, hover polling, the Give/Give All/Take/Take All context menu -- mirrors
  `InventoryGridContent.BuildItemContextMenu`'s exact Give/Take decision logic). Icon sits
  `IconGap` (4px) past the text's own measured width, not pinned to the row's far edge.
- `IInventoryDropTarget` (`Presentation/UI/Content/`, `{ int EntityId { get; } }`) -- implemented
  by both `InventoryGridContent` and `CurrencyRowContent`, so `UiInputController`'s drop-target walk
  (renamed `FindHostingGrid` -> `FindDropTargetEntityId`) finds either one via the same `Window {
  Tag: IInventoryDropTarget }` match: an item dropped on a currency row, and a currency element
  dropped on a grid, both transfer correctly now ("for consistency," per the original ask).
  Currency drags never bind to a hotbar slot (same treatment as a Merged Stack drag -- blocked
  cursor over hotbar slots, same `_contentDragCurrencyType`-gated early return in
  `ResolveContentDrag` that a Merged Stack's own refusal already used).
- `DragGhostContent`/`DragGhostState` gained a `CurrencyType` field, drawing the Gold/Credits
  sprite while dragging. Live-testing find: the drag ghost initially used
  `CurrencyElement.CurrentSize` (the whole "Gold : 10 [sprite]" bounds, much wider than tall) as
  its source size, stretching the sprite horizontally -- fixed by adding
  `CurrencyElement.IconSize` (just the square icon) and reading that instead.

### Shops

Full design/rationale: `PLAN-shops.md` -- builds on the Currency/container work in
`PLAN-storage-containers.md`/`PLAN-loot-currency.md`.

- `ItemDefinition.Value` (Gold worth) + `Game/Modules/Shops/` (`ShopComponent`, `ShopActions.
  TryBuyFromShop`/`TrySellToShop`, check-then-commit). `Shop`/`PotionShop`/`GeneralShop`
  (`Game/Blueprints/Objects/`) composed via `CompositeBlueprint`, not inheritance -- same
  shell+stock-part shape `GoblinEngineerBlueprint` established for race+class.
  `ShopWindow`/`ShopWindowController` (`Presentation/UI/Shops/`) mirror `SecondaryInventoryWindow`/
  `SecondaryInventoryWindowController` as a template, not a shared base -- a shop's summary/close
  behavior differs enough (no `LootedComponent`, drives `MapViewState.OpenShopEntityId` instead)
  that extending would have cost more than a focused sibling.
- Player can Give Gold to a shop, never Take it back (`CurrencyRowContent`/`UiInputController` both
  gate on `ShopComponent`). `InventoryGridContent.CellSize` bumped 24->36; a new `ShopItemStackCell`
  (`InventoryItemStackCell` un-sealed for this) swaps in while a shop is open, reusing
  `CellCompareState`'s existing grey-out/green-glow language for trade eligibility (tag + Gold)
  instead of Item Details Comparison.
- Live-testing find worth flagging generically: a display grid's own default same-item stack
  grouping (`InventoryGridContent.GroupDivergedStacks`) can silently produce an untradeable "Merged
  Stack" cell (no single `StackInstanceId`) whenever two physical stacks of one item coexist --
  routine here since a shop's own random stock draws from the same catalog the player's starting
  kit does. Any future UI built on top of grouped grid cells should check whether its own
  per-cell action needs a real `StackInstanceId` before assuming a merged cell is just a cosmetic
  concern.

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

### Status effect stack representation: StatusEffectStack pool deleted

Full record: this session. Resolved the TODO.md "Burning/Poison stack representation" item:
`MultiComponentPool<StatusEffectStack>`/`BodyPartStatusEffectStack` (N chain-linked pool entries
added 1:1 per `ApplyStack`, existing only so `StatusEffectQueries` could ask "what effects are
active, how many stacks" across effect types) are gone entirely -- the magnitude already lived
exactly once, as `StackCount` on each effect's own timer component. `IStatusEffectDisplay` gained
`GetStackCount(ComponentManager, int)`; `TimerBasedStatusEffectDisplay<T>`'s generic constraint
tightened to `where T : struct, IStatusEffectStackCount` (mirrors `TimerBasedAuraApplier<T>`) so it
implements `GetStackCount` generically off the same timer pool it already reads for duration.
`StatusEffectQueries.HasStack/CountStacks/GetActiveEffectTypes` now take
`(StatusEffectDisplayRegistry, ComponentManager, int entityId, ...)` instead of the old pool --
same 3 method names/call sites in `HealthWindow`/`PlayerStatusEffectsContent`, both of which already
had a `StatusEffectDisplayRegistry` on hand (no new plumbing). `BurningTimerComponent`/
`BodyPartBurningTimerComponent` each gained their own `Source` field (mirroring
`PoisonTimerComponent`'s, which already had one) so `BurningSystem`/`BodyPartBurningSystem` no
longer need to walk a separate pool to attribute tick damage -- set once on the 0-to-1 transition,
never overwritten by a later top-off, matching Poison's existing "first applier wins" rule (a
minor, deliberate behavior change from the old arbitrary chain-order pick across multiple
simultaneous sources). `StatModifiers`/`StatusEffectAura`/`StatusEffectImmunity` were reviewed and
found unaffected -- `StatModifierComponent` never referenced `StatusEffectStack`, and
`IStatusEffectAuraApplier`/`StatusEffectAuraApplierRegistry` already read each timer's own
`StackCount` directly (never the deleted pool); reusing that registry for querying was considered
and rejected since `BurningAuraApplier.GetCurrentStackCount` is deliberately scoped to whichever
single mode (entity- vs body-part-burning) is currently hazard-relevant, which would have changed
`HealthWindow`'s "Status Effects" list to flicker based on hazard exposure.

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
- Comparison now works in shop mode and across the trade window: `InventoryItemStackCell.CompareState`
  (comparison eligibility) and the new `ShopTradeEligible` bool (shop trade eligibility) are computed
  independently every frame (`InventoryGridContent.UpdateCompareState`/`UpdateShopEligibilityState`)
  instead of one overwriting the other -- the green eligible-glow is comparison-only now, a
  shop-eligible item shows no glow, just its own price-line coloring. `TradeWindow`'s two columns are
  wired into the same comparison-aware click dispatch (`onItemSelected`) every other inventory grid
  uses -- `ItemDetailsWindowController` gained a `GetTradeWindowRectangle` hook, checked as its own
  independent `if` in `IsOutsideClick`, NOT folded into the pre-existing
  `GetSecondaryInventoryWindowRectangle` fallback chain -- confirmed live as a real bug: the shop and
  trade windows are open *simultaneously* (unlike corpse-vs-shop, which really are mutually
  exclusive), so a single "pick whichever one's open" `Func<Rectangle>` made the shop window's own
  rectangle invisible the moment a trade session started, reading every click on the shop's own item
  grid as "outside" and closing/clearing the anchor (or the whole comparison) instead of selecting the
  shop item.
- `UiInputController.HandleMouseRelease` also gained `ExceededContentDragTapThreshold`, mirrored off
  `ResolveContentDrag`'s own existing tap-vs-drag distance check: a genuine content-drag (release past
  `ContentDragTapThresholdPixels` from press) now skips `DispatchClick` for the origin element --
  confirmed live as a second real bug, unrelated to the one above: `_activeInteraction.Element` still
  refers to the pressed cell for the whole gesture, so dragging an item cell anywhere used to ALSO
  fire that cell's own ordinary click on release (opening/toggling Item Details, or silently adding
  the dragged item to an armed comparison) purely as a side effect of the drop, not because the player
  clicked it.

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
- `InventoryItemStackComponent.FirstAcquiredUtcTicks`: stamped inline at construction
  (`= DateTime.UtcNow.Ticks`), the same "assigned in the property initializer, not a ctor param"
  shape as `StackInstanceId` -- every genuinely-new-stack call site in `InventoryActions` gets a
  fresh timestamp for free, while merging into an existing stack (plain or already-divergent) only
  ever mutates `Quantity` in place, leaving it untouched; `TryTransferStack` copies the whole struct,
  so a transferred stack normally keeps its original timestamp -- except when the destination is the
  player (has a setter for exactly this one case), where it's re-stamped to now: since
  `TryTransferStack` never merges, a transfer onto the player is always a new stack there, so looting
  something (e.g. "Take" from a corpse/loot window) should read as freshly acquired regardless of how
  long it sat wherever it came from. Powers the "New" sort option
  (`InventorySortOrder.RecentlyAcquiredDescending`); a merged-badge cell in `InventoryGridContent`
  sorts by the newest `FirstAcquiredUtcTicks` among its member stacks, not any single member's own.

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

### Drag-drop resolution extracted into per-feature resolvers

`UiInputController.ResolveContentDrag` had grown a branch per drop-target-aware feature (plain
transfer, shop buy/sell, trade window). Replaced with `Presentation/Input/DragDrop/IDragDropResolver`
+ `DragDropContext`: each feature owns its own resolver (`TradeDragDropResolver`,
`ShopDragDropResolver`, `PlainInventoryDragDropResolver` as the always-claiming fallback), tried in a
fixed priority list (Trade -> Shop -> Plain) built once in `UiInputController`'s constructor --
`ResolveContentDrag` itself is back to gesture recognition + hit-testing + dispatch only. `TryResolve`
returning `true` means "this resolver owns the drag based on who the endpoints are," not "the
underlying action succeeded" -- an ineligible shop/trade drop is claimed-and-refused, never falls
through to a plain transfer. A future drop-target feature (Magic Menu, Equipment slots) adds its own
resolver to the list without `UiInputController` learning anything new. `UiInputControllerTests.cs`
needed no changes -- its 35 `Drag_*` tests drive the real `Update` input pipeline black-box.

### Global U/I/O window-toggle hotkeys landed

`UiInputController.HandleWindowToggleHotkeys` -- U/I/O toggle Health/Inventory/Ability Score,
unconditional/edge-triggered like Tab/Escape/F12, gated on `!IsTextBoxFocused` since (unlike those)
they're printable characters. Each controller (`HealthWindowController`/`InventoryWindowController`/
`AbilityScoreWindowController`) got a new public `Toggle*Window()` wrapping its existing private
`WindowLifecycle.Toggle()`. The three HUD-trigger buttons were resized to match `HotbarContent.SlotSize`
(`HealthWindowChrome`/`InventoryChrome`/`AbilityScoreChrome.ButtonSize`, all now one shared constant
instead of three independent `HudChrome.EntrySize.Y` copies) and each grew a hotbar-style corner
`HotkeyLabel` overlay (`ButtonOptions.HotkeyLabel`, drawn via the same `ContrastTextRenderer` hotbar
slots already use, sized off a new `FontChrome.ButtonHotkeyLabelFontFraction`).

### Inventory "Activate" (context menu + double-click) landed

Click-to-activate an inventory item, matching the hotbar's own arm/target/confirm flow -- see
`ActionTargetingController.ArmItemFromStack` (a slot-less entry point into the existing `ArmItem`,
reusing `HandleItemSlotPress`'s own eligibility guard) and `InventoryGridContent.BuildItemContextMenu`'s
"Activate" option (first in the list, before Compare; visible whenever `CanActivate` is true, but
disabled -- not hidden -- while `IsPlayerActionLocked`, mirroring `MapWindow`'s own "Inspect" option's
`ActionLockGate.IsBlocked` gate). Double-click is a second entry point to the same call.

Landed alongside two generalizations prompted by this feature recurring elsewhere:
- **Click/double-click gesture recognition moved into `UiInputController`** (`Element.DoubleClicked`,
  opt-in via `WantsDoubleClickDetection`; `DispatchClick`/`HandleDoubleClickAwareClick`/
  `FlushExpiredPendingClicks` defer a single click and cancel it on a real second click within
  `UiInputController.DoubleClickWindowFrames`) -- `InventoryGridContent` no longer hand-rolls its own
  frame counter/pending-click buffer, it just reacts to `Clicked`/`DoubleClicked` like any other
  consumer. `ActionTargetingController.DoubleTapWindowFrames` (keyboard hotbar double-tap) now reads
  this same shared constant instead of an independently-tuned one -- settled on double-tap's original
  0.3s value, not double-click's briefly-tried +25% tuning.
- **Activating anything closes every closable window** -- `UiLayerStack.CloseAllClosableWindows()` (a
  new, unconditional sweep across every layer, no menu-mode short-circuit) is called from
  `ActionTargetingController`'s four real commit points (`ArmAction`/`ArmItem` for arming,
  `QueueActionActivation`/`QueueConsumableActivation` for the double-tap instant-fire paths that skip
  arming). This replaced `UiInputController`'s own bespoke Escape-hold sweep entirely -- Escape-hold and
  item/action activation now share the one implementation (single-tap Escape's
  `CloseTopmostClosableWindow` is unrelated and untouched). A future Magic Menu cast goes through the
  same `ArmAction`/`QueueActionActivation` chokepoints and gets this behavior for free.
