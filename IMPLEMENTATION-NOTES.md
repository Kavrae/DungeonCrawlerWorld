# Implementation Notes

Durable facts about landed work that don't belong in `TODO.md` (open items only) or a `PLAN-*.md`
(pre-execution design). One section per topic, terse bullets, file/type names over prose. Append new
topics; don't duplicate what a `PLAN-*.md` already records in full (link to it instead).

## Game

### Actions/Effect framework, Scrolls, Wands

- `Game/Effects/` (entries in `Entries/`; the action-only ones stay in `Game/Modules/Actions/Effects/`): `Effect` (composable `IEffectEntry` list -- `DirectDamage`,
  `DirectHeal`, `DirectManaRestore`, `HotkeyExpansionGrant`, `StatusEffectGrant`, `StatModifierGrant`,
  `ChainedEffect`, `AuraSourceGrant`) + `IActionActivator` (`PotionActivator`/`ScrollActivator`/
  `WandActivator`/`SpellActivator`) replaced the old `AbilityEffect`/`ConsumableEffect` split.
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
  (`WandGrantEffects`), ticks down via `InventoryActions.MoveOneUnit` -- first item ever
  granted as a diverged stack. Forced item-hotkey binding to key by `StackInstanceId`, not
  `ItemDefinitionId`. No Equipment gate (doesn't exist yet).
- `ActionInstanceComponent.Override: ActionDefinition?` replaced the old bare `DamageAmount: ushort`,
  mirroring `InventoryItemStackComponent.Override`'s shape/resolution pattern -- a full nullable clone
  of the catalog definition (any field, not just damage), resolved via
  `ActionInstanceQueries.TryResolveEffectiveAction` (Override if set, else `ActionCatalog.TryGet`).
  `ActionActivationSystem`/`DelayedActionSystem` resolve `instance` first, then the effective `action`
  off it -- `ActionEffectResolver.Apply` no longer takes `instance` at all, and
  `EffectContext.DamageOverride` is gone (a fixed value is now just `DirectDamage` with
  `MinFlatDamage == MaxFlatDamage` baked into the granted `Override`, built via
  `ActionOverrideEffects.OverrideFlatDamage` so a targeted `with` on the matched entry can't silently
  drop unrelated fields, e.g. Magic Missile's `BodyPart: BodyPartTargeting.Of(BodyPartType.Head)`).
  `PendingActionActivationComponent` needed no change -- same as Items, it only ever carried an
  identity reference.

### Move inventory items to hotbar

`ItemHotkeyBindingComponent` + `ItemActivationSystem`/`ActionTargetingController` arm/target/
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
  occupying the *resolved target tile* -- but the caster's step only goes through
  `MovementComponent.NextMapPosition` (see below), so at the moment `ActionActivationSystem` processes
  the FreeCast activation, that destination tile is still empty; the occupant loop found nobody there
  and `DodgeActivation` never ran, so `DodgingComponent` was never granted (self-dodge in place was
  unaffected, since its target tile is always the caster's own current, occupied tile). Fixed with two
  changes: `ActionTargetingController.QueueActionActivation` now stores the caster's own *current*
  tile as `PendingActionActivationComponent.TargetTiles` for Dodge specifically (guaranteed occupied),
  while the step's destination travels separately (`PlayerCommands`, see "Input buffering, instant
  Dodge, and Stagger"); and
  `DodgeActivation.Apply` now reads/writes `context.SourceEntityId` instead of `context.TargetEntityId`
  so the grant always lands on the actual caster even if another entity happens to share that tile
  (e.g. a co-located Tiny/Phasing occupant).
- `Engine/Math/TargetShape` is now `[Flags]` (`AdjacentWithSelf` retired -> `Adjacent | Self`, see the
  Actions section above) and `TargetingSpec` gained `Metric: DistanceMetric` (Manhattan/Chebyshev,
  `SingleTarget`-only). `DodgeAction`'s own targeting -- `SingleTarget` + `Metric.Chebyshev` + `Range: 1`
  -- is "pick exactly one tile out of the caster's own 3x3 block," resolved at confirm time by
  `ActionTargetingController`, whose step goes through `MovementComponent.NextMapPosition` -- the exact
  same path ordinary WASD movement uses, written by `PlayerCommands` -- rather than being applied
  directly. Two confirmed bugs both came from an earlier version that called
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
  if occupied" for free. The step does not wait for the shared `ActionLock`: a successful Dodge releases
  it (`ActionTiming.ReleasesActionLock`, see "Input buffering, instant Dodge, and Stagger"). `TargetShapeResolver
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

The charge-fill indicator: TODO.md's own top-priority
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
  race" the ordinary way, by being Human, no special-casing needed.
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
  **Superseded 2026-09-11:** both systems are off tiers entirely.
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
  raw target" clamp existed to guard against. (Since the timer-wheel rewrite the stepped source
  itself is gone -- the lock is a deadline and nothing decrements it -- so no tier can lag its
  nominal duration any more; the elapsed-time fraction is kept regardless, since it reads no
  component per frame.)
- The elapsed-time fraction was then replaced by `IMapViewQuery.GetChargeFraction`, which reads the
  pending action's `ReadyAtFrame` against the simulation clock. Its per-entity state only reset on a
  Draw that saw the entity not charging, so an NPC queuing its next attack on its resolve frame
  (every time with a 60-frame windup on `TestCombatBehaviorSystem`'s 15-frame stripe) inherited the
  previous charge's full fill and sat at 100% for its whole windup. The simulation-derived fraction
  has no state to carry over, freezes while the simulation is paused, and resumes at the true
  progress for an entity that enters Local tier mid-charge.

### Body parts / Complex health

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

`Game/Modules/BodyPartEffects/BodyPartEffectsSystem`:
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
  `BurningApplier.ApplyBodyPartScopedStack`, `ParalysisEffects.Apply`), each of which now also
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
  `PlayerKit` like every other starting potion) exercise all three pillars end-to-end.
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
  containers landed), both menu-mode windows.
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

Builds directly on the Currency/container work above.

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

Builds on the Currency/container and loot currency work above.

- `ItemDefinition.Value` (Gold worth) + `Game/Modules/Shops/` (`ShopComponent`, `ShopActions.
  TryBuyFromShop`/`TrySellToShop`, check-then-commit). `Shop`/`PotionShop`/`GeneralShop`
  (`Game/Blueprints/Objects/`) composed via `CompositeBlueprint`, not inheritance -- same
  shell+stock-part shape `GoblinEngineerPart` established for race+class.
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
- A completed purchase merges: `ShopActions.TryBuyFromShop` and a completed trade's shop column run
  `InventoryActions.MergeIntoEquivalentStack` on what the player receives, topping up an equivalent
  stack (same item, Override, divergence and disabled state, up to the player's cap) instead of
  leaving a second one. Found live: buying a potion the starting kit already had made a Merged Stack
  cell the hotbar couldn't bind. The stack merged into keeps its `StackInstanceId` (hotkeys stay
  bound) and `AcquiredSequence` (so topping up doesn't read as "New"). Staging into or out of the
  trade window never merges -- a staged stack needs its own id to move back.

### Toggles: items and actions (aura stage 2, 2026-10-03)

`PLAN-aura-stage-2.md` (superseding `PLAN-toggle-items.md`), built in five phases, each checked in game.
The rules themselves are in CLAUDE.md ("Toggles and windups"); this is what was decided and found.

Decisions (confirmed in the plan's review, not to be reopened):
- A toggle is a field on the shared definition (`ToggleSpec`), not an activator kind. An item still gets
  `ToggleItemActivator`, because an item's activator says how units are spent: never, lit state per unit.
- Toggles stack and are independent. Each lit unit is its own toggle; an entity may hold several
  sources of one aura, one per toggle, keyed.
- Everything a toggle does is an `Effect` list: held, activation, periodic. Effects are asked first
  (`CanApply`) and are all or nothing, which is also what lets an unaffordable turn-on show disabled.
- Turning on needs only the activation effects; periodic effects that can't apply switch the toggle off.
- Death: a toggle action goes off; a lit item with periodic effects goes off; one without keeps working
  on the corpse. Any holder can hold a lit item.
- An item toggle activates like an action, by its timing, through the pending path (reverses the old
  plan's "direct call, never gated by the lock"). Delayed toggles share the action windup: one
  component for actions and items.
- Toxic Idol is a Delayed toggle item; Toxic Aura a FreeCast toggle action draining 1 mana a second.
  Potions, scrolls and wands stay Immediate. Stances wait on a reversible `StatModifierGrant`.

Choices made while building (reported at each phase):
- `Toggles.TurnOff` takes the definition from its caller; the owner registry (`IToggleOwner`) arrived
  with `ToggleUpkeepSystem`, which is what needs it.
- A holder off the map is not ticked. The trade-offer entities count as simulated, and without this a
  staged lit item with upkeep would be asked to pay and go out.
- A lit item with upkeep that arrives at a dead holder is due at once and goes out on the next upkeep
  update, not inside the stack event: flipping a stack from inside its own change event risked a
  transfer or merge in progress.
- No item cooldowns exist (cooldowns are keyed by action id), so "the cooldown starts both ways" is
  built for actions only.
- No UI binds an action to a slot, so `PlayerKit` binds Toxic Aura to Slot 7 (marked TEMPORARY) rather
  than leaving it unbound. Slot 6 and 7 are locked expansion slots until a Hotkey Expansion Potion.
- `MultiComponentPool.ComponentRemoving` and `EntityManager.IsDestroying` were added for the item
  reconcile: a removal the holder's destruction causes is ignored, so nothing is re-added to a
  recycled id.

Found in game, not by review:
- **Several hotbar slots bound to one stack.** A bought idol merges into the one already held, so two
  slots named one stack of two; lighting a unit moved "the first binding on the stack", not the slot
  pressed, and one slot cycled on, both on, one off, both off. The pressed slot now travels with the
  activation (`ActivatedFromSlot`, through `PlayerCommands`, the request and the windup), and
  `ItemHotkeyBindingActions.RepointAfterUnitMoved` moves only that slot, plus any slot left on a stack
  that is gone. Wands had the same latent fault and use the same rule.
- **A 1-2 s freeze the first time Toxic Aura was used.** Its aura definition had Scroll of Torch's
  Guid. A definition is re-registered whenever its source radiates, so the first use replaced Torch's
  definition, and a definition change rescans every aura source in the world (~370 ms headless; the
  second use, the idol and a first attack were normal). It would also have swapped back on the next
  Torch cast. Found by timing the frame in a full-size headless world, then per system, then inside
  the flip. Fixed with Toxic Aura's own Guid; `AuraContentRegistration` now fails a build with two
  different aura definitions sharing a Guid.

The windup rename (`PendingDelayedActionComponent` -> `PendingWindupComponent`, and the identifiers
built on it) changes the pool's name in diagnostics output, so a benchmark baseline saved before it
lists that pool under the old name.

### Aura stage 3: power and size, targeting modes, anchors, attribution, modifier checks (2026-10-06)

`PLAN-aura-stage-3.md`, built in seven phases, each checked in game. The rules are in CLAUDE.md
("Auras and terrain contact", "Targeting"); this is what was decided, found and measured.

Decisions (confirmed in the plan's reviews, not to be reopened):
- **Totals are dense chunks**, 32x32 `int`, per aura per neighborhood, allocated on first write and
  freed at zero. `int`, not `ushort`: overlapping `ushort` powers pass 65535, and a total wrapping to
  exactly 0 would read as empty and free a chunk still in reach.
- **A source is a power (`ushort`) and a size (`byte`)**; `AuraSourceComponent` stays 8 bytes. Halving
  per tile is gone. Falloff is per aura: `Linear`, `power * (size + 1 - distance) / (size + 1)` rounded
  up, so the edge fades to about 1, or `None`, full power to the edge. Content kept its numbers as
  power and size, so the middle of each aura got stronger (the shrine heals 16, 13, 10, 7, 4).
- **Targeting modes, Target (default) and Ground**, the player's choice (Left Alt), kept in
  Presentation for the session. Game resolves targets: a request carries a `TargetSelection`, and one
  function (`TargetResolution.Resolve`) turns it into tiles for the activation, the preview, the
  telegraph and NPCs, so what is drawn is what lands. Which activations have a mode and what Target
  affects are the targeting spec's (`Modes`, `TargetModeAffects`); melee, Adjacent shapes and Dodge
  are Ground only.
- **An effect entry says where it lands** (`IEffectEntry.Placement`: `OnEachTarget`,
  `OncePerActivation`, `AtLocation`), so Torch's "attachment mode" is the targeting mode: Target lights
  the entity, Ground anchors the light on the tile.
- **Anchors are ordinary entities** (`AuraAnchor`), ended with their last source, their placer's
  destruction or their neighborhood's unloading; a toggle holding one switches off with it. A maximum
  distance from the caster is a TODO.
- **Attribution is credit only**, to the strongest single contributor at the cell: an entity source's
  entity (an anchor's placer), else the terrain's share, else the aura when several terrain types
  radiate it. The effect still has no source entity. Kill credit follows it.
- **An anchor reaches its placer.** Only sources an entity carries leave it alone; an aura that
  shouldn't hit its placer grants an immunity first -- what makes droppable healing auras and
  explosives work. Toxic Aura holds a Poison immunity on its user while on.
- **Modifiers are read live, at application**, not snapshotted at the start of a windup. CPU is the
  same either way (one chain walk per amount); snapshotting would add bytes or an allocation to every
  windup -- NPC combat has over 10,000 in flight -- and a capture step beside every entry's apply.
  Incoming modifiers are the target's, unknown until resolution in Target mode, so they are live
  regardless.

Choices made while building:
- `NeighborhoodBits` and `AuraTotals` keep freed bitmaps, chunks and neighborhoods for reuse: freeing
  and reallocating a 131 KB-per-layer bitmap on every step across a seam cost 54 MB over the test walk.
  Found by the zero-allocation test.
- The field fixes an aura's falloff while it holds any source of it; a replaced falloff takes effect
  once they have gone. Rewriting on `AuraCatalog.DefinitionChanged` didn't hold: a terrain still holding
  the old definition re-registers it during the rewrite.
- A Ground windup is resolved from its aimed tile when it ends, like a Target one: the same tiles unless
  the caster moved. A Target selection with nothing on the tile marks nothing and resolves as Ground.
- An emptied anchor is destroyed by `AuraAnchorEndingSystem` the next frame: removals happen inside
  other systems' timer callbacks. Anchors are `NonBlockingKind.None`, so Target never marks one.
- A toggle action switched on in Ground mode anchors what it places on the holder's tile; toggle items
  always place on the holder.
- `AuraSourceIndex` (entity sources by 32x32 chunk, attribution fixed at first placement) lives in
  `AuraField`; an entity source wins a tie with the terrain share; ties between entities go to the
  lowest key. `StatusEffectImmunityGrant` became reversible under a toggle's key: a held immunity is its
  own instance (`StatusEffectImmunityEffects.GrantHeld`/`RevokeHeld`).
- `EffectModifiers.Scale` runs Outgoing on the source entity when there is one and Incoming on the target
  when there is one; damage's Incoming stays at `HealthDamage` and healing's both passes at
  `HealthHeal`, the chokepoints DoTs and regeneration share. Twelve targets were appended to
  `StatModifierTarget` (mana restore and drain, status stacks, proc chance, aura power and size,
  Outgoing and Incoming), so existing values kept their numbers. `IEffectEntry.AmountModifiers` declares
  each entry's pairs.
- **A spell's mana cost goes through the ManaDrain modifiers**: the caster drains itself, so Outgoing
  and Incoming both apply. Before, a spell's cost bypassed every modifier, which the Thrift check found.
  The cost has since become a `ManaDrain` activation effect (see "Costs are activation effects").
- TEMPORARY test content: Fireball (Slot8), the Lantern toggle (Slot9, 4 expansion potions), the Fairy
  Magic Missile override (Delayed, range 8, mode rolled per cast) and 15 mana, the "Radiant" trait,
  30 starting mana, and the four `ModifierTestPotions`.

Measured (Release, headless, seed 1, frames 600-3600, A/B against the commit before the stage; each
phase's run is a different world, since content changed):

| Phase | `EcsContext.Update` ms/frame | Notes |
|---|---|---|
| 1 storage | +7.3% (A's spread 4.3%) | heap 1,225.6 -> 847.6 MB; totals 26,297 chunks, 103.8 MB |
| 2 power/size | 1.764 -> 1.687 (-4.4%) | `AuraSystem` -11.4% |
| 3 targeting | 1.726 -> 1.610 | all within the run's 22% spread |
| 4 placement | 1.790 -> 1.662 | `AuraAnchorEndingSystem` 0.0002 ms |
| 5 attribution | 1.619 -> 1.617 | attribution about 0.04 ms/frame in `AuraSystem` |
| 6 modifiers | 1.555 -> 1.549 | no system flagged |

Storage variants measured in phase 1: 16x16 `int` 91.5 MB, 32x32 `ushort` 52.4 MB, 16x16 `ushort`
47.8 MB, all within noise on frame cost and 11-13 ns a cell. 32x32 `int` was chosen for the `int`
reason above, and because a 16x16 neighborhood's slot array is 96 KB, on the large object heap.

### Costs are activation effects (2026-10-06)

Came out of a review of `EffectModifiers`: a spell's mana cost and a toggle's activation `ManaDrain`
were checked separately against the same mana, so a toggle spell costing 10 with a 10-mana activation
effect passed with 15 and was charged 20. Built in five phases, each checked in game. The rules are in
CLAUDE.md ("Effects", "Toggles and windups"); this is what was decided and found.

Decisions (confirmed, not to be reopened):
- **A cost and an activation effect are the same thing.** `SpellActivator.ManaCost`, `ActionGrant.ManaCost`,
  `ActivationQueries.ManaCostOf` and the separate spell-mana spend are gone; a spell declares a
  `ManaDrain` in `ActivatableDefinition.ActivationEffects`, which every action and item has, not only
  toggles.
- **The name stays `ActivationEffects`, not `Costs`**: it leaves room for effects on the user that aren't
  costs, such as stunning the caster in place.
- **Every refusal has its own text** -- no generic "can't be used" bucket. `ActivationEffectsRefused` is
  gone; each `EffectRefusal` maps to its own `ActivationBlocker`.
- **The hotbar badge shows every cost**, one number per resource in its colour (mana sky blue, health
  red), bottom-left on action and item slots alike. Revisit only if costs outgrow a badge and a tooltip.
- **Health and mana are never rounded** -- damage, healing, mana drained and restored, costs or not.
  Rounding to the nearest made a halved 1-mana upkeep free (0.5 rounds to 0); rounding up would have made
  a -50% modifier do nothing on it. Both pools were already floats, so exact amounts cost nothing.
- **A health cost (`HealthDrain`) is refused rather than killing its payer, split evenly across a body
  plan's parts, and never reduced by damage reduction.** It has its own modifier pair
  (`Outgoing`/`IncomingHealthDrain`). A cost isn't a hit: no damage event, no floating text, no
  inflicted-damage achievement credit.

Choices made while building:
- `ActivationEffects` is an `init` property on `ActivatableDefinition`, not a positional parameter: a
  list has no constant default, and a required parameter would have touched every definition.
- `EffectSequence.CanApply` asks a whole list against one `EffectReservations`, so costs in one list are
  checked together, and returns the first refusal's reason instead of a bool.
- The action-lock check moved to one place in `ActionActivationSystem`, before anything is taken: a use
  waiting on the lock takes nothing. A spell's mana is now taken before its effects land, not after;
  no outcome changes.
- Whether a grant gives an entity a mana pool is read from the definition it will use
  (`ManaUse.DrainsUsersMana`: an activation effect or a toggle's upkeep). `EntityBuilder`,
  `EntityFactory` and `BlueprintContext` gained the action catalog, as they had the aura catalog, and
  `PlayerKit`'s `manaCost: 1` stand-in for Toxic Aura's upkeep went.
- `ManaDrain` and `HealthDrain` implement `IResourceDrain`, so the badge totals (`ActivationCosts`) and
  the mana-pool rule find costs without listing entry types.
- A toggle that is on shows no cost on its slot: turning it off takes nothing.
- NPCs check activation effects too (`TestCombatBehaviorSystem` takes `EffectServices`).
- Entries placed once per activation follow the marked entity: one that dodged or isn't simulated gets
  none of them, and only `AtLocation` entries are placed. Found in the same review.

Found in game, not by review:
- **A halved Toxic Aura upkeep drained nothing.** 1 mana x 0.5 rounded to 0 every second. This is what
  turned "round to the nearest" into "never round" for every health and mana amount.

Found while building:
- Damage was cut to a whole number at five points, and a whole-body hit lost up to (parts - 1) points
  to integer division before being spread across the parts. Both went with the no-rounding rule.

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
tightened to `where T : struct, IStatusEffectStackCount` (mirrors `TimerBasedStatusEffectApplier<T>`) so it
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
`IStatusEffectApplier`/`StatusEffectApplierRegistry` already read each timer's own
`StackCount` directly (never the deleted pool); reusing that registry for querying was considered
and rejected since `BurningApplier.GetCurrentStackCount` is deliberately scoped to whichever
single mode (entity- vs body-part-burning) is currently hazard-relevant, which would have changed
`HealthWindow`'s "Status Effects" list to flicker based on hazard exposure.

### World scaling: the 3x3 neighborhood window

Landed 2026-09-15 to 2026-09-16 in seven phases, each checked in-game. Replaced a fixed 4000x4000 map
that measured 29.6 GB, 47.7 s startup and 12 ms/frame.

- **Shape:** 1024x1024 neighborhoods (`Game.World.Neighborhoods`). Local = Chebyshev radius 80 around
  the player, every frame. The window centre's neighborhood = Neighborhood tier (divisor 8). The 8
  around it = Borough, loaded but frozen (`SimulatedTierCount = 2`, `SimulationScope`). Further =
  Beyond, unloaded. Terrain and walls are flyweight `TerrainCell`s in `Map`, not entities.
- **Transitions:** `NeighborhoodMembershipIndex`, `ProcessingTierTransitionQueue` (256 entities per
  frame, thaw first), `ProcessingTierQuery` for the seam. Promotion out of Borough runs
  `TimerCatchUp` over each module's `ICatchUpTimers`. Periodic first deadlines are staggered by entity
  id (`FrameDeadline.AfterStaggered`) so timers created together don't fire together.
- **Streaming:** `Map.Unbounded` + `NeighborhoodStreamer` (first system in the frame, 256 units per
  frame). `NeighborhoodRecords` keeps a seed per coordinate; `TestMapBuilder.GenerateNeighborhood` is
  the stand-in generator. Stable `EntityKey`s and `EntityIdentities` let references outlive an unload.
- **Startup reservation:** `WorldSessionBootstrapper` reserves (9 + 3) / 9 of the startup population
  plus 10% before the first frame (136 ms spikes without it, when dense pools still grew linearly).

Decisions (don't re-litigate):
1. Borough is the 8 neighborhoods around the middle one, and it is frozen.
2. Every tier transition is smeared across frames through one budgeted queue.
3. Promotion out of Borough, by any route including teleport, catches up every effect with an active
   timer before the entity rejoins the simulation.
4. No targeting across the simulated/frozen boundary; the player only interacts with Local. NPCs
   can reach the seam, so every tile-targeted effect skips frozen occupants: actions
   (`ActionEffectResolver`), NPC target choice (`TestCombatBehaviorSystem`), and potions, scrolls and
   wands (`ItemActivationSystem`, added 2026-09-26 after a potion reached an unbuilt skeleton).
5. Exact timers at every simulated tier. Neighborhood speed 1/8, settled in-game.
6. Other MapLayers take the (x, y) neighborhood's tier but are never Local.
7. Population is defined per MOB population template (dense or sparse); neighborhood placement is
   randomized, with relationships between neighbors generated dynamically.
8. Chunk = neighborhood = 1024x1024 tiles; the loaded window is 3072x3072.
9. Catch-up: aura and contact-damage exposures don't accrue while frozen (only effects already
   active catch up); deaths are allowed during catch-up; pending windups are cancelled at freeze;
   regen interleaves between owed ticks, matching live play. No player-only "Local only" targeting
   rule: Local's radius exceeds every action range, so the seam holds by construction.
10. Neighborhood records are kept after their contents are deleted, so a return regenerates the
    same layout with a fresh population, until save/load replaces it.
11. Window shift hysteresis: 64 tiles past the centre's edge. One centre drives both loading and
    tiers.
12. Cache of the 3 most recently dropped neighborhoods, oldest evicted.
13. Stable 64-bit entity keys, never reused. Long-lived references (status-effect sources, killers,
    parties, logs) hold keys; runtime ids recycle immediately (no generations, no quarantine).
    Short-lived references (AI targets, windups, trade, the inspector's selection) stay runtime ids.
    Names and crawler numbers live in an interned identity table addressed by a handle.
    `ActionSource` stays 8 bytes (4-bit kind, 24-bit identity handle or terrain type, 36-bit
    key); a 16-byte source measured ~50% slower `BurningSystem`. Parties are session records of
    member keys, so a member unloaded by distance stays a member.
14. Crawler numbers never recycle or change for an entity, including across unload.
15. Stand-in generation: random terrain and creatures from each neighborhood's own record seed. The
    hallway cross, fixtures, shops and spawn exist only in neighborhood (0, 0). No border walls.
16. Debug-build pacing during streaming (~3 fps while a neighborhood regenerates) is accepted: the
    budget is a fixed, deterministic unit count tuned for Release.

Measured at the end (headless Release, seed 12345, 3072², teleporting 70 tiles into the next column
of neighborhoods at frames 700 and 1400): first shift worst frame 32-35 ms (the teleport frame itself),
second shift worst 12-15 ms with none over 16.67 ms; a shift takes 8-17 s. Steady state
`EcsContext.Update` 1.34 ms/frame. Remaining: occasional ~17-19 ms gen-1 GC frames (`TODO.md`). A
further 17-24 ms frame near a background gen-2 GC was a runtime wait rounded up to Windows' 15.6 ms
timer tick; SDL3 sets a 1 ms timer in the windowed game, and the headless benchmark now does too.

### Input buffering, instant Dodge, and Stagger

Replaced the "New user input cancels buffered input" TODO. Model follows action-game convention (single-slot
buffer with a short expiry; held movement sampled, taps buffered; dodge cancels windups; hitstun clears the
buffer). Decisions, so they aren't re-asked:

- `Game.Modules.Actions.PlayerCommands` is the only writer of the player's `NextMapPosition`,
  `PendingActionActivationComponent` and `PendingItemActivationComponent`. One slot (move / action /
  consumable), newest wins, `ExpiryFrames` = 0.25s on the simulation clock (pausing doesn't age it; tune after
  more play). A move or consumable is written once `ActionLockGate` reads the player as free, an action once
  it's ready (cooldown and, unless FreeCast, the lock -- see "Disabled actions"); a command queued while
  ready is written the same frame. The Game-side requests stay one-shot -- buffering lives entirely in Presentation.
- Writing any command withdraws what the game hasn't taken yet: an action/consumable clears an untaken step, a
  move removes an unconsumed activation request. A move is buffered as a direction, resolved at write time; a
  blocked newest direction clears the step rather than falling back to an older one.
- Held movement is not a command: with the slot empty, the held direction is written only while the player is at
  rest and has no activation request pending. Releasing keys cancels nothing. The old 0.25s repeat cooldown and
  the "only while at rest" queue gate in `PlayerMovementController` are gone; the action lock alone paces
  steps. Opposing keys still sum to neutral, and a fresh neutral press clears a buffered step.
- FreeCast actions (only Dodge today) ignore the lock, so one that's off cooldown is written at once, still
  emptying the slot.
  Escape/right-click cancel order: disarm -> clear the buffer -> cancel the windup; each reports "cancelled" so
  a no-op still falls through to the corpse context menu.
- `ActionTiming.ReleasesActionLock` (Dodge only): a successful activation cancels the caster's windup and
  releases its lock, whatever set it, for NPCs too. No penalty beyond Dodge's own cooldown. The Dodge's step is
  written by the buffer only on the player's `ActionActivatedEvent` for that Dodge, so a failed Dodge (on
  cooldown) no longer gives a free step.
- `MovementSystem` moves the player every frame (`BeginFrame`) and skips them in its tiered buckets --
  otherwise the player was only visited every 15 frames and a step (Dodge's included) landed up to 0.25s late.
- Stagger = cancelling a buffered action. `Tag.Staggering` (PowerAttack only; keep it rare) makes
  `ActionEffectResolver.Apply` publish `EntityStaggeredEvent` per target hit, after the dodge skip and never
  for the source. `ActionsModule` cancels the target's windup and **keeps** its lock (the windup's time is
  lost -- that cost is what justifies windup attacks' power); `PlayerCommands` clears its slot. A pending
  Dodge step survives a Stagger, and held movement isn't staggered (a deliberate choice for now: a key held
  through a Stagger still steps once the lock clears). `WindupCancel.TryCancel(..., releaseLock)`
  is the one cancel path for Escape (release), Dodge (release) and Stagger (keep). Unrelated to
  `FrameDeadline.AfterStaggered`.

### Disabled actions

Replaced the "Disable actions that destroyed body parts make unusable, and show why" TODO. Decisions, so
they aren't re-asked:

- **One rule in Game.** `ActivationQueries.GetBlocker` returns an `ActivationBlocker` -- `NotActivatable` (no
  activator), `MeleeDisabled` (`Delivery.Melee` + `MeleeDisabledComponent`), `NotEnoughMana`, checked in that
  order (structural before transient). `ActionActivationSystem`, `ItemActivationSystem` and
  `TestCombatBehaviorSystem` refuse through it; Presentation reads it through `ActionStateView.GetActionBlocker`/
  `GetItemBlocker`, which resolve the entity's effective action/item (an override's mana cost counts). The
  Presentation-side mana checks are gone.
- **Unavailable vs not ready.** Only a blocker disables: the hotbar dims the slot, arming refuses (key, click,
  double-tap, Inventory "Activate"/double-click), and `ActionTargetingController.Tick` disarms whatever becomes
  blocked while armed. Cooldown and action lock are timers, not blockers: the radial wedge shows them, the slot
  still arms.
- **No silent confirm.** `ActivationQueries.FramesUntilReady` = later of cooldown and (unless FreeCast) lock.
  `PlayerCommands` holds a buffered action until it's 0, and `QueueAction`/`QueueItemActivation` refuse (return
  false, keep whatever was buffered) a command that couldn't be ready before `ExpiryFrames`; `CanQueueAction`/
  `CanQueueItemActivation` ask without queuing. A refused confirm leaves the action armed.
- **Showing why.** `ActivationBlockerText` owns the words and tooltip rows: the hotbar summary (rebuilt when the
  blocker changes), the inventory hover (player's own activatable stacks only -- a sword isn't "deactivated"),
  and an Item Details line (player's own stack, rebuilt when the blocker changes). `UiInputController` shows
  `MouseCursor.No` over a disabled hotbar slot and over the map while the armed action can't be confirmed
  (`MapWindow.IsArmedConfirmRefused`); both are checked ahead of the stationary-mouse shortcut and re-run the
  hover hit test the frame they end.
- **Melee wording is generic** ("No usable arms or hands") until actions declare the parts that perform them.
  Known weakness: regen lifts a 0 HP part within frames, and `BodyPartEffectsSystem` scores once a second, so
  the melee block is rarely seen -- TODO "Body part disablement that lasts".

### Pool sizing and the memory report

Landed 2026-09-18 as phases 0-1 of "Deferred-build NPCs and shared definition data" (`TODO.md`).

- **Memory report:** `--diagnostics=memory` with a benchmark range (headless or windowed) writes
  `Log/diagnostics/memory-<timestamp>-<pid>.json|txt` through `PoolMemoryReport`: per pool its count,
  estimated MB, distinct values when the range opens, and the share of surviving holders whose values
  changed by its close; plus allocation and collections before the range, live heap and peak working
  set. `Invoke-MemoryReport.ps1 [-Compare]` in the phase-performance-testing skill drives it. Values
  are compared field by field: a bitwise comparison read padding bytes as changes
  (`AbilityScoreComponent` showed 98.7% changed instead of 0%).
- **Paged entity index:** Packed and Multi pools index entities through `EntityPages` (1024 ids per
  page, allocated on first write, never freed so a Multi pool's entity version keeps counting).
  `maximumEntityCount` registration overrides were removed; `ResizeEntityCapacity` only grows page
  tables for them. Direct pools stay flat.
- **Dense growth:** x1.5 (`DenseCapacityGrowth`, minimum 16) from `InitialComponentCapacity` (1024) or
  a registration's own `initialCapacity`, instead of a fixed 220k initial size and step.
- `BackgroundComponent` moved from Direct to Packed (no writers today).

Measured (seed 1, 3072², Release, same world both sides): pools 1,498 -> 1,236 MB, live heap 2,424 ->
2,162 MB, peak working set 5.34 -> 3.69 GB, allocated building the world 9.7 -> 5.0 GB. Frame cost
headless A/B: `EcsContext.Update` +2.3%, inside the baseline's 3.9% spread; lookup-heavy systems
(`MovementSystem`, `TestCombatBehaviorSystem`, `BurningSystem`) read 3-5% higher, each inside its own
noise -- the paged lookup's extra load, worth re-checking as later phases add lookups.

### Creature definitions, spawn records and BlueprintContext

Landed 2026-09-18 as phase 2 of "Deferred-build NPCs and shared definition data" (`TODO.md`).

- `Game.Creatures`: `EntityDefinitions` (race and class tables, `ushort` ids from 1, 0 = empty slot,
  keyed by Guid, a re-registered Guid replaces in place) filled by `EntityPartsModule`;
  `SpawnRecipe` (two race and two class slots); `SpawnRecordComponent` (recipe + `uint` seed,
  12 B, Direct pool).
- `EntityFactory.Build(recipe, seed)` reseeds one `SeededRandom` (xoshiro256**, reseedable without
  allocating; `System.Random(seed)` allocates its state per construction) and runs race parts, then
  class parts, then writes the spawn record. The main population spawns through it with a seed drawn
  from the neighborhood's population sequence. Every other entity went through this too once the
  one entity factory landed -- see "One entity factory: every entity is parts plus a seed".
- `IBlueprint.Build(BlueprintContext)`: component manager, entity id, `Rolls`, `EntityKeys`. Blueprints
  take nothing a build depends on through their constructors, so definitions hold one shared,
  stateless instance each. Replaced blueprints that captured a `MathUtility`/`EntityKeys` at
  construction, which made "roll only from the creature's sequence" a convention instead of a type
  guarantee.
- `CreatureDefaults` rebuilds a spawn record in a staging world: every module configured and built a
  second time before the real configuration (the dry-run pattern), with its own random sequence and
  key table, systems never run. `CreatureDefaultsTests` proves populated creatures rebuild exactly,
  apart from what population writes after the build (position, action-lock stagger, tier, crawler
  number), with a negative control so the comparison can fail.

Decisions: composition archetypes were rejected (dozens of races and classes, 1-2 each, and ability
scores 1-300 explode any shared race+class key); data is classified by what it varies with -- see the
`TODO.md` entry. The class-slot model (`ClassSlots`/`ClassMembership`/`ClassQueries`, the class-grant
`ActionSource` kind, advancement rules) moved to phase 4 because nothing consumes it before then.

Changing how creatures roll changed the world once: seed 1's headless fingerprint went from
`454135E5C67D1588` to `E2D7F9B469DCAB34`. Pools +13 MB (the spawn-record pool); frame cost unchanged.

### Window-shift tier drain: chunk-bucketed stripe sets rejected

Investigated 2026-09-26: should each `TieredEntityStripeSet` bucket its entities by neighborhood,
making a tier a property of the neighborhood so a window shift costs work per neighborhood rather
than per entity? No.

Measured (Release, seed 1, 3072², a temporary headless probe that teleports the player across a
border, counters on every tier change):

| | Diagonal shift | Straight shift |
|---|---|---|
| Entities dequeued | 815k | 508k |
| Tier changes | 439k | 364k |
| Drain settles after | ~1,590 frames (26 s) | ~990 frames (16.5 s) |
| Drain total | 930 ms | 711 ms |
| Skeleton builds (to Neighborhood) | 271 ms | 211 ms |
| Stripe-set `TierChanged` calls / real migrations | 3.07M / 0.2M | 2.55M / 0.2M |

- **~40% of dequeues are wasted:** neighborhoods queued while unloaded are walked after the streamer
  has loaded them, and their entities were born with the right tier.
- **60-67% of tier changes are Borough <-> Beyond,** unsimulated on both sides.
- **The changes that matter** are ~72k entering Neighborhood (builds, per entity by nature) and ~73k
  leaving it.
- **93% of stripe-set `TierChanged` calls are no-ops,** mostly skeletons, which no tiered set holds.
- **The drain isn't the worst frame.** It is budgeted at ~0.6 ms/frame; the spikes were the teleport
  frame itself and streamer/GC frames.

Why the idea was dropped: bucketing would remove only the stripe-set share of the drain. Thawing
still reaches every entity (builds, `SimulationScope` resume), Local still needs a per-entity
overlay, and the two queue fixes below remove most of the drain without restructuring anything.
Steady-state and move costs were not a concern (only one neighborhood is ever at Neighborhood tier;
movers only live in simulated tiers).

Decisions (don't re-litigate):
1. **Four tiers, each with its own job:** Local fully simulated, Neighborhood simulated slowly,
   Borough updated by end-of-day estimates (TODO "Borough end-of-day summaries"), Beyond frozen.
   Borough and Beyond are therefore not merged, even though nothing tells them apart yet.
2. **All four values stay exact on every entity.** Borough/Beyond is not moved to a
   neighborhood-level property: one rule for every tier is worth more than the saved work.
3. **Borough <-> Beyond transitions drain last,** after thawing and freezing. End-of-day summaries
   allow a time buffer so the labels have settled before they run.
4. **Neighborhoods not loaded at the shift are not queued;** whatever loads them tiers them at birth.

Landed 2026-09-26: `ProcessingTierTransitionBand` (Thawing, Freezing, Unsimulated) and the
`IMapQuery.IsNeighborhoodLoaded` check in `ProcessingTierSystem.QueueIfTierChanged`. The queue moved
onto `ProcessingTierResolver.Transitions` so anything waiting on a shift can ask
`HasPendingSimulatedChanges` (thaw and freeze bands done). Measured with scripted headless teleports
(a benchmark flag since removed; Release, seed 1, 3072², frames settled after the teleport):

| Teleport | All tiers, before -> after | Simulation settled |
|---|---|---|
| East into (1, 0) | 1,137 -> 710 | 284 |
| Diagonal into (1, -1) | 1,417 -> 708 | 283 |
| Back to (0, 0) | 1,137 -> 1,136 | 283 |

A teleport back only reorders the work: nothing it touches is unloaded. Frame cost across the run
unchanged (-4.4%, inside the noise), and the world differs because freezing now lands earlier.

### Teleports

`Game.World.EntityTeleporter` is the one teleport path (gameplay, Admin Mode's "Teleport here"). It
refuses anything but a free cell of the loaded map, builds a skeleton first, drops the movement destination, cancels a windup, and records the move
through `SpawnMoves` so it is safe between frames, mid-frame or from Presentation.

Cost of the teleport frame, measured with a probe (Release, straight shift): ~10 ms of Local square
walks (the promote walk 4.3-4.7 ms, 1.2-1.4 ms of it skeleton builds; the demote walk 5.2-6.4 ms),
since a teleport walks both full squares where a step walks only their edges. The first window shift
of a session adds ~20 ms of JIT compilation in the streamer's shift handler; later ones cost
0.3-0.5 ms. Hiding the ~9.5 s thaw of a Borough destination is the "Teleport countdown with a window
anchor" TODO.

### Asynchronous neighborhood generation

Landed 2026-09-26 in three phases. A window shift in the Debug build dropped the game below 60 fps
for ~6 s (90 ms frames), most of it the streamer; a probe showed ~55% of its work was decidable
without touching the world.

- **Plan on a worker, apply on the main thread.** `TestMapBuilder.Plan` decides a neighborhood as
  a `NeighborhoodPlan` -- its layout written into detached stores (`Map.CreateLayout` ->
  `NeighborhoodLayout`, which reads only the map's fixed depth and bounds), each row's aura cells
  (`TerrainAuraSources.ByRow`), and the ordered spawn list -- from the record alone. The streamer then
  loads the stores whole (`Map.LoadNeighborhood(layout)`; an already loaded neighborhood copies the
  terrain in and keeps its occupants, for startup), announces each row with `TerrainLoadedEvent`
  carrying its aura cells (both aura grids splat from the list, nobody scans), and spawns the list
  under the same unit budget. `TerrainRegistry`'s sprite-variant cache is filled at builder
  construction so the worker only reads it.
- **Fixed start.** A load is planned from the moment it is queued and may start `StartDelayFrames` (30)
  streamer updates later; if the worker isn't done then, the main thread waits. So streaming depends on
  frames only: a seeded run is identical every time, and Debug and Release now simulate the same world.
  Readiness counts the streamer's own updates, not `EngineTime.FrameCount`, which is the same in the game
  and lets tests pump the streamer directly.
- **Workers start at the next streamer update**, not in the shift handler: starting them mid-frame put
  them alongside the teleport frame's ~10 ms Local walk and cost it ~7 ms (Release).
- **Cancellation.** A load dropped before it starts cancels its worker; the plan uses the record's
  `PendingPopulationSeed` and counts it (`NextPopulationSeed`) only when the load starts, so turning back
  leaves the neighborhood's next population unchanged, as before async.

Decisions (don't re-litigate):
1. The budget stays in deterministic units, not wall-clock time (a time budget makes shifts
   non-reproducible).
2. The delay is 30 updates, ~500 ms at 60 fps (~483 ms of it for the worker, which starts an update
   late). Slowest plans measured: 317-325 ms windowed Debug, up to 390 ms headless Debug, 110-131 ms
   Release; 20 updates (317 ms) would stall windowed Debug. A longer delay only makes the frozen ring
   appear later, so err long. It counts frames, and a headless run goes faster than real time, so a
   headless Debug run can occasionally wait (once in three runs, 14 ms); the paced game doesn't.
3. Unloading still scans rows for aura terrain on the main thread (only evictions pay it).
4. The aura splats stay on the main thread. Precomputing each neighborhood's aura-grid contribution on
   the worker and merging it was considered (~2.2 ms/frame in Debug after a shift) and rejected.
5. Both per-frame caps are halved, `NeighborhoodStreamer.DefaultBudgetPerFrame` and
   `ProcessingTierSystem.DefaultTransitionsPerFrame` 512 -> 256: what remained after async was main-thread
   work only (spawns, builds, splats), so the lever left was spreading it thinner. The same work over
   twice the frames: the simulation settles ~568 frames after a shift instead of ~284, loads finish at
   ~980 instead of ~520. Hiding the longer settle is the countdown TODO, which waits on a teleport visual
   effect.

Windowed Debug, after a teleport, simulation ms/frame average / worst per 2 s, caps 512 -> 256:

| Seconds after | 512 | 256 |
|---|---|---|
| 0-2 (includes the teleport frame) | 14.9 / 40.0 | 11.3 / 38.2 |
| 2-4 | 11.2 / 30.9 | 9.3 / 13.4 |
| 4-6 | 6.4 / 13.3 | 9.6 / 15.0 |
| 6-8 | 9.0 / 23.2 | 9.0 / 20.8 |
| 8-10 | 5.5 / 11.5 | 6.3 / 15.5 |
| 10-12 | 4.9 / 7.9 | 8.2 / 21.6 |
| 12-14 | 5.0 / 8.4 | 6.8 / 11.4 |

Frame cost across a whole range is unchanged (headless A/B, Release, nothing flagged).

Measured (headless, seed 1, 3072², teleports east and back, A/B against the pre-async build): streamer
-58% Release / -62% Debug, `TerrainLoadedEvent` -41% / -51%, other systems flat, worst frame unchanged
(the teleport frame). Windowed Debug: worst frame 90 -> 40 ms, and the run keeps real time. Streaming
settles 31 frames later (the delay); the tier drain is unchanged. The world differs from before (loads
start later), identically every run.

### Creature skeletons: building an NPC when it is first simulated

Landed 2026-09-22 as phase 3 of "Deferred-build NPCs and shared definition data" (`TODO.md`). What a
skeleton holds, when it is built and what may touch one is in `CLAUDE.md`'s Blueprints section.

- **Appearance moved into the race definition** (`CreatureAppearance`): glyph, sprite set, names and
  description, with the per-creature variant hashed from its seed rather than drawn from its rolls.
  That is what lets a skeleton draw and name itself exactly as it will once built, and why the race
  blueprints no longer roll a name or sprite. A race's intrinsic `NonBlockingComponent` (Ghost's
  phasing) moved there too: occupancy can't wait for a build.
- **The build hook is `TierChanging`, a new event ahead of `TierChanged`.** Found by the Debug guard on
  its first run: `LocalTierRoster` handles `TierChanged` and reads the movement pool, so building
  inside a `TierChanged` handler was already too late for whoever subscribed earlier.
- **Spawn moves are recorded a frame later.** `CreatureSkeletons` is registered first in the frame and
  replays each build's spawn move then; recording during the tier drain throws, because that runs
  after the consumers have read this frame's moves. Each pending move carries its `EntityKey`, so one
  whose creature was destroyed in between is skipped instead of searched for on every destruction.
- **Aura exposures:** `AuraSystem`'s occupant scan now skips unsimulated occupants, which
  is what decision 9 of the world-scaling work already said; a promoted creature is granted when its
  build records its spawn.
- **Evictions before promotions** (`PromotionsHeld` / `IsEvictingBuiltCreatures`): without it, a shift
  built the new centre before the old one was destroyed, peaking at four built neighborhoods instead of
  three and needing a bigger startup reserve to avoid resizing the largest pools mid-drain (one 30-60
  ms frame on each of the first three shifts). Holding for the *whole* eviction cost 11 s before the
  new centre came alive, so an eviction destroys its built creatures first, ahead of its other work,
  and only that part holds promotions.

Measured (Release, seed 1, 3072², against the pre-phase-1 baseline): pools 1,498 -> 398 MB, live heap
2.42 -> 1.19 GB, peak working set 5.34 -> 1.72 GB, allocation building the world 9.7 -> 1.9 GB. Six
teleport-driven window shifts: worst frame after the teleport 14-15 ms (baseline 14-25), p99 9.5-10 ms
(baseline 9.5-12.7), promotions complete ~5.4 s after a shift (2.4 s without the eviction hold).
Steady-state `EcsContext.Update` +2.6%, nothing flagged, different worlds either side.

### Compact per-instance layouts (phase 4)

Phase 4 of "Deferred-build NPCs and shared definition data" (`TODO.md`), landing one component group
at a time. Each step is A/B'd for pool memory and frame cost; the pool kinds themselves turned out to
cost more than the values in them.

- **Appearance is packed, not direct** (2026-09-22). `SpriteComponent`, `DisplayTextComponent` and
  `GlyphComponent` were Direct pools, sized by entity capacity (654k) while only the ~73k built
  entities hold them: 65 -> 15 MB with no change to what is stored or to naming semantics. A Direct
  pool is only right for a component most entities have, and since skeletons landed almost nothing is
  in that category.
- **Ability scores are one struct per entity** (2026-09-22). `AbilityScoreComponent` was a
  MultiComponentPool entry per `AbilityScoreType`, so an entity's seven scores cost seven chain
  entries and their per-entity bookkeeping: 36.6 MB for 3 MB of values. `AbilityScoresComponent` holds
  every score as two `[InlineArray]`s of `ushort` (bases and precomputed totals) plus a `granted`
  bitmask, in a packed pool -- 9.1 MB. The bitmask is what keeps "never granted" distinct from zero,
  which readers like `DodgeEffects` and `PotionCooldownEffects` fall back on. Reads still go through
  `AbilityScoreQueries.TryGetComponent`, now a pool lookup plus an index instead of a chain walk, and
  hand back an `AbilityScoreValue` so call sites were untouched. `SimpleHealthRegenSystem` and
  `ComplexHealthRegenSystem`, which read Constitution on every visit, came down 24% and 13%.
- **Race and class are slots of definition ids** (2026-09-22). `RaceComponent`/`ClassComponent` each
  repeated a Guid, a name and a description per entity -- 12.1 MB for four distinct races.
  `RaceSlotsComponent`/`ClassSlotsComponent` hold two `ushort` `EntityDefinitions` ids instead (a
  creature has one or two of each; a third is dropped rather than growing the component for a case
  that doesn't exist), and names come from the definition. A blueprint writes its own id, so
  `BlueprintContext` carries the session's `EntityDefinitions` -- which also means a race blueprint
  built against definitions that never registered it now throws instead of silently producing a
  raceless creature. `TestCombatBehaviorSystem` compares slot ids rather than walking a chain for a
  Guid (-5%), and `InspectionWindowContent` resolves both names through the definitions.
  The membership pool the player's class exceptions need is not here: nothing grants a class at
  runtime yet, so two slots hold everything that exists (see `TODO.md`).
- **Actions come from race and class definitions** (2026-09-22). Every Goblin held three
  `ActionInstanceComponent`s naming the same three actions with the same shared overrides: 219,619
  entries, 13 distinct values, 34.3 MB. Those grants moved onto the definition as `ActionGrant`
  (`RaceDefinition.Actions`/`ClassDefinition.Actions`), so a creature holds nothing for them --
  `ActionInstanceComponent` survives only for an action granted to one entity alone (a wand, a
  learned scroll: 4 entries in a full world). `EntityActions` is the single read surface, resolving
  an entity's own grant first, then its races', then its classes', and falling back to the catalog
  definition; `ActionInstanceQueries` is gone. Cooldowns left the action entirely: nothing holds a
  deadline until an entity actually uses an action that has one, and then it is an
  `ActionCooldownComponent`. `TestCombatBehaviorSystem` -16%, the whole `EcsContext.Update` -9%.
  Two consequences worth remembering: a definition-granted action is not a component, so it does not
  appear in the admin component dump (its race does), and `EntityFactory` grants mana for a
  definition's mana-costing action the way `ActionGrantEffects` does for an explicit one.
- **Class membership is storage ahead of its consumer** (2026-09-22). `ClassMembershipComponent`
  (class id, grant kind, acquisition order, expiry floor) sits beside `ClassSlotsComponent` for the
  player's rules that two slots cannot express, with `ClassQueries` reading slots and memberships as
  one ordered set and `ClassEffects` routing a grant to a slot when it is permanent and one is free.
  Nothing grants a class at runtime yet; it is unit-tested rather than exercised, at the user's
  request, so the floor-end event and class-selection UI have something to build on.
- **Body parts are a race's templates plus what a fight changed** (2026-09-22). `BodyPartComponent`
  held a name, type, vertical position, maximum health and vitality per part per creature -- 167,937
  components, 26.4 MB, for 3,439 distinct values. Those fields are the race's body plan, so they moved
  to `RaceDefinition.BodyParts` (`BodyPartTemplate`, which the race blueprints already authored), and
  a creature holds only `BodyPartStateComponent`: current health, a disabled bitmask and a regen
  lockout per part, in `[InlineArray]`s capped at 16 parts, created the first time anything happens to
  it. An untouched creature holds nothing and reads as every part at full health.
  `EntityBodyParts` is the one read and write surface (`Parts` walks templates and state together
  without allocating), and a part's handle is now its id -- its index in its own body plan, stable for
  its lifetime and already what `BodyPartBurningTimerComponent` names -- rather than a pool dense
  index. `BodyPartSelection` returns part ids; `ComplexHealthDamage`/`ComplexHealthHeal`/
  `BodyPartDamageEffects`/`MaximumHealthShift`/`ComplexHealthRegenSystem`/`BodyPartEffectsSystem`/
  `BodyPartBurningSystem`/`PoisonSystem` and the health UI all follow.
  Two consequences: `ComplexHealthRegenSystem` and `BodyPartEffectsSystem` are now driven by the state
  pool rather than "has a body plan", so their per-frame population is the creatures something has
  actually happened to, not every Complex creature; and `MaximumHealthShift` takes the session's
  `EntityDefinitions`, because a shift has to know the body plan to move each part with its own
  maximum.

  A first cut of this regressed the whole simulation 20%: reading a part went through TryGetReadonly,
  which copies the 132-byte state component, once per part, and the templates were indexed through
  IReadOnlyList. Reading state by its dense slot (a ref, no copy), resolved once per enumeration, and
  holding the templates as an array put it back -- worth remembering for any other flyweight read on a
  hot path.
- **A creature is named and drawn by its race** (2026-09-23). Every creature carried a
  `DisplayTextComponent`, `GlyphComponent` and often a `SpriteComponent` holding what its
  `CreatureAppearance` already said -- 65 MB of pools for a dozen distinct values. `CreatureAppearance`
  is now only read, never applied: `CreatureNaming` resolves a name and description (own component,
  else race appearance plus class names) and `MapViewQuery`'s existing skeleton fallback became the
  general path for drawing. Holding one of the three now means "called or drawn differently from its
  race" -- the player, a shop, a chest, a renamed corpse, a test fixture: 73,207 holders became 7.
  `ActionSource.FromEntity` takes the session's `EntityDefinitions` so a killer is still named, and
  it resolves through a static, allocation-free path because it runs on every hit.
- **The memory report had to learn about inline arrays.** Its fieldwise comparer used
  `EqualityComparer<TField>.Default` per field, and every built-in equality on an `[InlineArray]`
  struct throws; those fields are compared and hashed over their bytes instead, which is exact for
  them (contiguous elements, no padding) while ordinary fields stay field-by-field so struct padding
  still can't read as a change.

Phase 4 complete: pools 397.7 -> 238.7 MB, peak working set 1,717 -> 1,508 MB, allocation building the
world 1,922 -> 1,602 MB, one collection fewer at every generation, and steady-state `EcsContext.Update`
down 9-12% across runs, with `TestCombatBehaviorSystem` -15% and no regression above threshold.

The last two steps each cost a round of measurement before they were clean, in the same way: body
parts made every melee hit and burning tick build a BodyPartView per part to pick one (BurningSystem
+20% once the machine was quiet enough to see it), fixed by selecting over a disabled-bits mask; and
the first naming cut built a resolver object per damage source, fixed by a static resolve. A flyweight
read is only free if reading it allocates nothing and copies nothing.

Body parts also changed the order parts are visited in -- a race's template order rather than the
pool's chain order -- so random and tie-broken part picks land differently and the seed-1 world
diverges slightly: 0.9% fewer deaths over 50 s, every other pool within a few percent. Beyond that,
the headless fingerprint covers each pool's name and count, so a change of pool kind or shape changes
it even when the simulation is identical -- compare the per-pool counts in the two memory reports to
tell those two cases apart.

### Per-instance shrink (phase 5)

Phase 5 of the same entry: the components that stayed per-entity, made smaller rather than shared.

- **`InventoryItemStackComponent`, 56 -> 24 B** (2026-09-23), the largest pool in the game at 584,429
  stacks: 133.5 -> 74.6 MB. Its two `Guid`s were 32 of those bytes. The item definition id is interned
  to a `ushort` handle (`ItemIds`, the same append-only shape as `EntityIdentities`) and exposed as the
  same `ItemDefinitionId` property, so no caller changed. The stack instance id became a `uint`
  counter, which did ripple: it is the id a hotkey binding, a pending activation, a drag payload and a
  shop transfer all carry, and `IHotkeySlotBinding` had to become generic in its id type because an
  action is still a catalog `Guid` while a stack is now a counter. `FirstAcquiredUtcTicks` became a
  4-byte `AcquiredSequence` counter -- the "recently acquired" sort only ever needed the order, and
  building a stack no longer reads the clock.
- **`MovementComponent`, 40 -> 32 B**: `TargetMapPosition`/`NextMapPosition` are stored as sentinel
  positions (`TransformComponent.UnplacedCoordinate`) and still read as `Vector3Int?`, so the 4 bytes
  of padding each `Vector3Int?` spent on its flag are gone and no call site changed.

Pools 397.7 -> 178.0 MB against the pre-phase-4 baseline, peak working set 1,717 -> 1,389 MB,
allocation building the world 1,922 -> 1,477 MB, `EcsContext.Update` -8.5% with no regression above
threshold.

What is left in that pool is the `ItemDefinition? Override` reference (8 of the remaining 24 bytes):
moving it out needs a side table with a real owner, since a stack can be removed by its entity being
destroyed. See `TODO.md`'s "Component size audit".

### One entity factory: every entity is parts plus a seed

The last phase of the same entry (2026-09-23). Population creatures spawned through `EntityFactory`
with a spawn record; fixtures, shops, the chest, the test dummy and the player called blueprints
directly and got none, so they could never be deferred, rebuilt or saved as "definitions + seed".
Now there is one path for all of them.

- **One id space for every kind of part.** `EntityDefinitions` (was `EntityDefinitions`) still holds
  a typed `Races`/`Classes` table and gained `Objects`, but all three draw ids from one shared
  allocator, so a recipe slot can hold any of them without saying which kind it is. Each table keeps
  its own lookup array over that shared space, holding null where another kind's definition sits, so
  reading a race by id is still an array index and an id of the wrong kind reads as "not registered
  here" with no type check. `ICreatureDefinition` became `IEntityPartDefinition`, with `NonBlocking`
  on the interface (an object part can make an entity phase or share a cell too) and a nullable
  `Blueprint` for a part that only declares shared data.
- **`ObjectDefinition`** is anything that is neither a race nor a class: a whole entity with no race
  (`GeneralShop`, `PotionShop`, `TreasureChest`, `TestDummyBlueprint`), or a modifier layered on top
  of race and class parts (`PlayerKit`, `StationaryPart`, `LongDescriptionPart`, `GoblinEngineerPart`,
  and the blueprint-less `OccupancyParts` Tiny/Phasing, which are nothing but a `NonBlockingKind`).
  An object part grants no definition-level actions: those are reached through the race and class
  slots an entity holds, and an object part is in neither.
- **`SpawnRecipe` became `SpawnRecipe`**: the same four `ushort` slots, now generic parts in build
  order rather than two races and two classes, so `SpawnRecordComponent` stays 12 bytes for every
  entity in the world. A shop is one part; the player is Human, Tank, `PlayerKit`; a tiny goblin is
  Goblin plus Tiny. `EntityDefinitions.TryGetAppearance`/`NameFor` scan the slots for the first race
  (or, for a name, the first object) instead of reading fixed positions.
- **`EntityFactory` became `EntityFactory`** and grew the spawning half: `Spawn` creates the entity
  through `ProcessingTierResolver.CreateEntityAt` (so it is born tiered), builds it or defers it to a
  skeleton, places it, records the spawn move and destroys it again if it landed off the map;
  `SpawnInto` does the same into an id the caller already reserved (the player). The build half is
  unchanged in shape and still works with only a `ComponentManager`, which is what lets
  `CreatureDefaults` rebuild a record in its staging world. `GameBootstrapper` builds the one instance
  and wires `Skeletons` onto it afterwards, since `CreatureSkeletons` is built around the same factory.
  Only a recipe with a race can be deferred -- a skeleton draws and names itself from a race's
  appearance -- so shops and the dummy are always built at spawn.
- **`TestMapBuilder` and `FloorBuilder` stopped touching blueprints.** Every fixture is a recipe
  spawned like the bulk population; the ~90 lines that hand-built entities and then removed a
  component, retyped a description or added a `NonBlockingComponent` are gone, along with
  `StaggerActionLock`, `PlaceAt`, `CreateEntityAt` and `ContextFor` (the factory owns all of them).
  A fixture's layer is now named at its call site rather than inherited from whatever its parts
  merged, because the position has to be known before the entity exists for it to be born tiered.
- **The player is a recipe too.** `PlayerKit` became `PlayerKit`, the last part of
  `FloorBuilder.PlayerRecipe`, and no longer allocates the crawler number -- `CreatePlayer` merges
  `CrawlerComponent` after the spawn, the way `TestMapBuilder` already did for a rolled NPC crawler,
  since a session's number range is a property of the run rather than of what the player is made of.
  That also leaves `PlayerKit` stateless like every other blueprint.
- **`BlueprintVariantSet` is gone** (no production caller since it was written). `CompositeBlueprint`
  stays, narrowed to composition *within* one definition's blueprint -- a shop's shell plus its stock;
  composing definitions is the recipe's job now. `GoblinEngineerPart` became `GoblinEngineerPart`,
  holding only the name and the extra cooldown reduction that the Goblin and Engineer parts don't.

The starting neighborhood's fixtures now draw their rolls from their own per-entity seed rather than
from the neighborhood's shared population sequence, so a seeded world differs slightly from before in
that neighborhood. Pools, working set and allocation are unchanged (178.0 MB, 1,391 MB, 1,477 MB) and
the frame A/B showed no regression (`EcsContext.Update` -3.9%, inside the baseline's own spread).

The spawn record is also the save format for an untouched Beyond neighborhood (part ids + seed), with
a built entity saving its per-instance deltas on top. See `TODO.md`'s "Save and load Beyond
neighborhoods".

Decisions from the entry that still govern this area: a frozen entity holds only what it needs to
exist until it is promoted or interacted with (interaction = any gameplay read or write; presentation
never builds one); a built entity is never returned to a skeleton; admin inspection shows a frozen
entity's defaults rather than building it; and data is classified by where its variation comes from --
definition-level data in the definition tables, seed-derivable instance data regenerated rather than
stored, and only genuinely mutable instance data in compact per-entity structs.

### Blueprint composition: one definition kind, includes, one-id spawn records

Landed 2026-09-25, replacing the "Blueprints, SpawnRecipe and EntityFactory are too rigid to build on"
`TODO.md` entry. The four-slot `SpawnRecipe`, the race/class/object tables and `CompositeBlueprint` are
gone; what replaced them is in `CLAUDE.md`'s Blueprints section. Six phases, each tested in game.

- **One kind of definition.** A race is a `BlueprintDefinition` with a race facet, a class one with a
  class facet; everything else -- a whole entity, a shop's stock, a trait -- is a plain definition.
  Composition is `Includes`, to any depth: `GoblinEngineer` = Goblin + Engineer + its own step,
  `GoblinForeman` = GoblinEngineer + Boss. That also absorbed `CompositeBlueprint` (a shop is its shell
  plus its stock by include) and ended "first race wins / append every class" as conventions spread over
  readers: `ResolvedBlueprint` computes them once.
- **Interned records, not inline slots.** `SpawnRecordComponent` is (blueprint id, seed) = 8 B, down from
  12, and has no part limit. The alternatives priced in the TODO entry (inline slots plus overflow,
  composite ids in slots) were dropped once composites existed: one id already stands for any list.
  Runtime combinations are interned by `BlueprintRegistry.Compose` under a SHA-256-derived v8 Guid of
  the ordered include Guids, so the same combination is the same blueprint in every session and a save
  can record its includes.
- **Decisions confirmed by the user before it started:** "required components" (Transform, DisplayText,
  Glyph, Sprite, ProcessingTier) means *resolvable*, not stored -- appearance lives on the definition and
  is checked at spawn (`IsSpawnable`), and the per-entity display components mean per-instance overrides
  only; applying a blueprint at runtime never rewrites the spawn record (a built entity saves its deltas
  on top of it anyway); deferral stays "includes a race" (a skeleton shop would have no `ShopComponent`
  while visible in the frozen Borough).
- **Appearance rules.** Each field an `AppearanceFacet` sets replaces what came before, except that a
  second race is ignored, so a hybrid looks, is named, spawns on the layer and at the size of its first
  race -- chosen over blending glyph colours, which the plan had proposed, for one predictable rule. An
  explicit `Name` replaces the composed one outright (the player, the shops); otherwise the seeded
  display name is followed by each class and `NameSuffix`. The player's and the shops' sprite variants
  moved from `Rolls` to the seed hash, which shifted later rolls: seed 1's headless fingerprint went
  `DA8D46E71253AA29` -> `C7216D808D8BF25F` (phase 2) and stayed there through phase 5.
- **Actions from any definition.** `ResolvedBlueprint.Actions` merges every part's grants, later
  replacing earlier by action id, so a composite can override its race's attack. `EntityActions` reads
  the entity's own grants, then its blueprint's (through the spawn record, no race slots), then classes
  gained at runtime that its blueprint didn't build -- a class slot also holds spawned classes, and a
  composite's override of a class action must not come back un-overridden through the slot.
- **The call side.** `SpawnRequest` (blueprint + x/y; layer, size and seed default from the blueprint and
  the factory's own runtime sequence; `Crawler`; a reserved id). The crawler allocator and a runtime seed
  reach the factory through `GameBootstrapper.Build`; a Crawler request without an allocator is refused,
  not silently left uncrawlered. Population still passes its own seed, then its crawler roll, in the same
  draw order as before, so worlds were unchanged by this.
- **Runtime.** A `FrameEventBuffer` throws on a write after its read, so a spawn from a system mid-frame
  used to be impossible. `SpawnMoves` (first in the frame, over `FrameEventBuffer.TryRecord`) records a
  spawn move now if the frame's moves are unread, else first thing next frame; it replaced
  `CreatureSkeletons`' own replay. `EntityFactory.Apply` layers a blueprint onto a live entity -- see
  `CLAUDE.md` for what it skips and what takes effect. `GameModuleContext.EntityFactory` is set after
  every `Configure`. Adding to a stripe set mid-iteration is safe (a system iterates a span over the
  bucket's current array), so no deferral was needed there.
- **Admin tools, kept.** Admin Mode's map context menu has "Spawn here >" and "Apply >"
  (`Game.Admin.BlueprintAdminCommands`, one of `AdminTools`' command sets). `ContextMenu` gained submenus for them (`ContextMenuOption.Opening`),
  swapped in on the menu's next update rather than inside the click, which would recycle the row being
  clicked. The user asked to keep these as a standing test tool.
- **`IBlueprint` is gone.** Every blueprint is a static class holding `Id`, `Name` and its `Definition`;
  a build step is its own `private static void Build(BlueprintContext)`, referenced as `Build = Build`
  (`BlueprintDefinition.Build` is an `Action<BlueprintContext>`), and a pure composition leaves it null.
  Before this, a blueprint with a step was a sealed class implementing `IBlueprint` and one without was a
  static class, so a file's shape depended on whether it had a step. A static method also makes "a
  blueprint holds no state" a compiler guarantee rather than a convention.
- **Moved while here:** the definition types from `Game/Creatures` to `Game/Blueprints`; `SpriteManifest`
  from `Game/Blueprints` to `Game/Sprites` (a sprite catalog shared by Game and Presentation, not a
  blueprint); `EntityPartsModule` became `BlueprintsModule` (same module Guid).

Measured (Release, seed 1, 3072², frames 600-3600). Memory against the one-entity-factory figures above:
pools 178.0 -> 174.3 MB (the spawn-record pool 15.6 -> 11.9 MB), peak working set 1,391 -> 1,372 MB,
allocation before the range 1,477 -> 1,479 MB; `DisplayTextComponent`/`GlyphComponent`/`SpriteComponent`
hold nothing in a populated world. Frame A/B, headless, 3 runs per side against the saved 2026-09-22
baseline (which predates the skeleton and one-factory work too, and simulates a different world):
`EcsContext.Update` 1.84 -> 1.68 ms/frame (-9%), no system flagged as a regression. The action-lookup
systems (`TestCombatBehaviorSystem` -16%, `ActionActivationSystem` -16%) got cheaper, not dearer.

### Skeleton data and applied blueprints

Landed 2026-09-26, one phase, following the composition work above.

- **One skeleton list, in the factory** (`EntityFactory.SkeletonComponentTypes`), read by the access
  guard and startup's pool reservation. A type is on it only if its value is declared on the definition
  or set by the spawn, and something needs it on unbuilt entities. `BuildSkeleton` writes those from
  definitions -- now including an unplaced transform on the blueprint's layer and size -- and no build
  step writes one: the seven blueprints that merged a `TransformComponent` stopped, `EnsureBuilt` no
  longer puts the skeleton's transform back, and `BlueprintSkeletonTests` fails if a build step ever
  changes a skeleton component (with a negative control, and an unusual footprint so even a same-sized
  transform merge shows). `BuildBody` became `BuildComplete`.
- **Race and class stayed off the skeleton.** Considered and dropped: no reader needs them on an
  unbuilt entity (drawing and naming go through the spawn record; everything else only sees built
  entities), and they would have cost ~4-5 MB of race slots.
- **Applied list.** `AppliedBlueprintComponent` (Multi pool: part id + application order) records every
  part `Apply` builds; `Apply` skips what the entity spawned with or already had applied, so a part is
  never built twice -- merge policies that aren't idempotent (averaging, concatenation) would otherwise
  merge a part into itself. Different parts still merge as before. Applied parts' actions, class names
  and name suffixes are read through the list, so `Apply` no longer grants per-entity actions and
  `EntityActions`/`CreatureNaming` (now `EntityNaming`) no longer read class slots or memberships. Action order is now own
  grant -> applied parts (latest first) -> spawn blueprint: an applied part is later than everything
  the entity spawned with, so it wins, which reverses the earlier "runtime classes after the blueprint".
- **Crawler numbers wait for simulation.** `SpawnRecordComponent` became (blueprint id, `SpawnFlags`,
  seed), still 8 B with the flags byte in former padding; `BuildComplete` assigns the number on first
  build. The allocator got its own sequence (session seed, salted): assigning at promotion from the
  session's shared sequence would have made later neighborhoods depend on when the player walked where.
  Admin inspection of an unbuilt crawler (the staging rebuild) shows no number.
- **`Game.Creatures` became `Game.Spawning`** afterwards: everything in it (the factory, spawn requests
  and records, `SpawnMoves`, the applied list, admin commands) serves every entity, not only creatures.
  `CreatureNaming` became `EntityNaming` and `CreatureDefaults` became `SpawnRecordRebuilder`;
  `CreatureSkeletons` and `SkeletonAccessGuard` kept their names, since only creatures are deferred.
  Earlier entries in this file use the old names. The fingerprint hashes each pool's full type name, so
  the move alone changed it (to `75A930BD5397AB52`) with the world unchanged.
- **Fingerprints.** Adding the applied pool alone changed the headless fingerprint, because it hashes
  every pool's type and count; with the pool left unregistered the world reproduced `C7216D808D8BF25F`
  exactly, so the step changed nothing else. With the pool: `C2C11D769611C48C`. After the crawler
  change (its own allocator sequence): `67F682054CEECEA2`.

Measured (Release, seed 1, 3072², frames 600-3600) against a baseline saved immediately before: frame A/B
`EcsContext.Update` 1.448 -> 1.439 ms/frame (-0.6%), nothing flagged; memory: `CrawlerComponent` holders
13,063 -> 1,451, pools 174.3 -> 171.9 MB, allocation before the range 1,478.8 -> 1,475.3 MB, collections
30/18/6 -> 28/17/5.

### Crawler number selection

- **A seeded permutation, not rejection sampling.** `UniqueNumberAllocator` runs a counter through a
  balanced 4-round Feistel network keyed from the seed (SplitMix64). Each allocation is O(1), nothing is
  stored beyond the counter, and no number repeats. The old version drew random numbers against a
  `HashSet` of every number issued: slower as the range filled, unbounded memory, and no exit once full.
- **The range is 1 to 2^24 (16,777,216),** rounded up from the source material's 13,000,000 so the
  permutation covers it exactly and needs no cycle-walking. `valueBits` must be even (balanced halves).
- **Running out.** `TryAllocate` returns false once every number is issued. From then on the factory
  spawns a Crawler request without `SpawnFlags.Crawler`, and a skeleton flagged as a crawler before that
  has the flag cleared when it is built (`AssignCrawlerNumber`) and becomes a plain NPC. Crawlers already
  numbered keep theirs. The flag is cleared lazily at build rather than by a sweep at exhaustion: nothing
  else reads it on an unbuilt entity.
- **Save/load** will need to persist the counter alongside the seed; nothing restores it yet.
- The same session seed now gives different crawler numbers than before this change.

### Loot boxes

A loot box is an untradeable inventory item that, when activated, opens every box the player holds and
grants their contents straight into the inventory. Module: `Game/Modules/Lootboxes/` (`LootboxModule`,
requires `BlueprintsModule`; `AchievementModule` requires it). Replaced the old `Lootbox` placeholder
record in Achievements.

**Types, rarities and item definitions**
- `LootboxTypeDefinition` (Id, Name, optional sprite/glyph) registers into `LootboxCatalog`
  (`GameModuleContext.Lootboxes`), so a mod adds or replaces a type by Id. Built-in types
  (`LootboxTypes`): Adventurer, Alchemist, Exorcist, Investor, Librarian, Weapon, Boss, Quest, Viewer Gift,
  Sponsor Gift. `LootboxRarity` (Bronze..Celestial) ordinal order is the rarity order.
- **Item definitions are created on demand**, never up front: there will be dozens of types and most
  (type, rarity) pairs will never be granted. `LootboxCatalog.GetOrCreateItem(LootboxKind)` builds the
  pair's `ItemDefinition` on first grant and registers it into `ItemCatalog`. Its Id is a SHA-256
  name-based Guid of (TypeId, Rarity) (`ItemIdFor`), the same in every session and save.
- `ItemCatalog.TryGet` is now overridable (`Catalog<T>.TryGet` virtual) and falls back to
  `IItemDefinitionSource`s: `LootboxCatalog.TryResolveItem` recognizes any pair's id (it indexes every
  registered type x 6 rarities, ids only, re-indexed when types were added) and creates the definition.
  Only a loaded save needs this today; it keeps the id scheme save-safe.
- A box's definition: "{Rarity} {Type} Box", `[Tag.Lootbox]` (the inventory tab), `CanTrade: false`,
  `GoldValue` 0, the placeholder contents, the type's sprite (the chest, "Inventory", by default) tinted
  by the new generic `ItemDefinition.SpriteTint` in the rarity's color (`LootboxRarityColors`, also the
  glyph color), and no `Activator`.
- Same type and rarity stack through plain `InventoryActions.AddItem`.

**Contents, and overriding them per box**
- `IItemContents.Roll(SeededRandom, ItemCatalog)` lives in Inventory, beside `ItemDefinition.Contents`:
  "an item that grants items when opened" isn't loot-box-specific. Implementations compare by value.
- `RandomSingleStackContents` is the placeholder every box uses: one stack of 1-10 of one item picked
  uniformly (candidates sorted by Id) from every tradeable item that isn't itself opened. Type and rarity
  don't affect it -- TODO.md's "Lootbox drop tables" replaces it.
- `SetItemContents` is fixed rewards; equal when its entries are equal in order.
- `LootboxReward(TypeId, Rarity, Contents?)` is what every award declares. A reward with `Contents`
  is granted as a per-stack `Override` of its kind's definition (`AddItemWithOverride`), cached per
  (kind, contents) so every grant shares one definition. It keeps the kind's id, name, sprite and tab,
  stacks only with boxes carrying equal contents, and never stacks with the usual box of its kind.
  Opening reads each stack's effective definition, so overridden boxes need no special path.
- Side fix: plain `AddItem` now only merges into a stack with no `Override` (it used to merge into an
  overridden stack of the same item id). `AreEquivalentOverrides` compares `Contents`, `CanTrade` and
  `SpriteTint` too.

**Untradeable items and the hotbar**
- `ItemDefinition.CanTrade` (default true), not the tag, is the "can't be traded, looted, sold, bought,
  dropped or destroyed" rule, so a future quest item can reuse it. Named to match `CanBindToHotbar`.
- `InventoryActions.TryTransferStack`/`TryTransferAllStacksOfItem` now take the `ItemCatalog` and refuse
  an untradeable stack with no state changed -- the one check behind give, take, corpse looting, shop
  buy/sell, trade staging and plain drag-drop. An item the catalog doesn't know carries no restriction.
  To pass the catalog, `UiInputController`'s `ItemCatalog` became required (it was optional for tests)
  and the shop/trade drag-drop resolvers lost their no-catalog branches.
- `ShopActions.CanTrade` is false for an untradeable item, so shop and trade UI grey it out. The item
  context menu offers no Give/Take/Sell All/Buy All/Add to trade for it.
- Not hotbar-bindable is separate from `CanTrade` (an untradeable quest item may still be usable):
  `ItemHotkeyBindingQueries.CanBind` is false for `Tag.Lootbox`, read by the cell's `CanBindToHotbar`
  and `HotbarContent.BindItem`.
- A cell that can neither be traded nor bound never starts a drag -- no drop could send it anywhere.

**Granting and sources**
- `LootboxActions.Grant` is the one grant path; it publishes `LootboxGrantedEvent`. Admin Mode's map
  context menu has "Grant loot box >" (type, then rarity) on any entity -- a permanent test tool.
- **Achievements** (`IAchievementDefinition.Lootbox`): unlocking records an
  `UnclaimedAchievementLootboxComponent` (achievement id + reward as declared) -- game state, not UI.
  The box is granted only when the player closes that notification for good:
  `NotificationCenter` publishes `AchievementNotificationDismissedEvent` (immediate, not buffered -- the
  simulation doesn't drain while paused or in menu mode) for Close, "Close" and "Close All", never for
  minimize (the Closed handler tells them apart because minimizing re-queues the notification as unread
  first). `AchievementModule` subscribes and claims for the player; `AchievementLootboxClaims.TryClaim`
  removes the record then grants, so it can't grant twice.
  A minimized or never-opened notification never pays out -- no timeout, no fallback.
  Boxes: AngelInvestor Bronze Investor, Archivist Bronze Librarian, DrinkingProblem Bronze Alchemist
  (set contents: 2 Health Potions, 2 Cure Poison Potions), EarlyAdopter Silver Adventurer, EmptyPockets
  Bronze Adventurer, InertGas Bronze Exorcist, SpellCaster Bronze Adventurer (set contents: 5 Mana
  Potions), UnarmedCombat Bronze Weapon. `RewardText` explains only an unusual reward, why a particular
  one was given, or why none was; the popup omits the Reward line when it's blank, so an achievement
  that just grants its box has none.
- **Bosses**: `BlueprintDefinition.Lootbox` is a facet, resolved like the others (the last part in
  build order to declare one wins; `ResolvedBlueprint.Lootbox`). The `Boss` trait declares Bronze Boss,
  so the Goblin Foreman and anything Boss is applied to pay out. `BossLootboxAwarder` (subscribed to
  `EntityDiedEvent` in `LootboxModule.RegisterBehavior`) grants the box when the killing blow's source is
  the player: the most recently applied part with a box wins, else the spawn blueprint's. It reads
  definitions, never components, so a boss that died unbuilt still pays out. One box per death, player
  killing blow only -- confirmed as intended. Kill credit for a status-effect tick goes to whoever
  started that run of the status (poison and burning keep their first applier's source); left as-is.
  Credit by contribution is TODO.md's "Advanced boss loot box awards".
- Quests, viewer gifts and sponsor gifts don't exist yet; their types do, and each will call
  `LootboxActions.Grant` with a `LootboxReward` on its own award declaration.

**Opening**
- "Open All" (the context menu's only option on a box) or double-clicking any box opens
  every box the player holds. Never blocked: no action lock and no location check (safe rooms are a TODO).
- **A direct call, not a System.** `ItemActivationSystem` is a system because consumables need the
  action lock, map targeting, system ordering and the skeleton guard; opening needs none of that and only
  touches the player's own built inventory, which Presentation already mutates between frames. A system
  would poll an empty queue every frame.
- `LootboxOpener` (`GameModuleContext.LootboxOpener`, handed to Presentation through
  `GameBootstrapResult`/`WorldSessionContext`) groups the boxes by kind -- every stack of one kind, split
  or overridden, is one group -- orders groups by rarity then type name (ordinal), and for each unit
  consumes it, rolls its effective contents and grants the rewards. It returns `OpenedLootboxGroup`s:
  kind, count, and items combined by id (`GrantedItem`: id, summed quantity, the stack the last one
  landed in). It draws from its own `SeededRandom` (runtime spawn seed xor a salt), so opening shifts no
  other roll and seeded runs reproduce.
- Rewards go through `ItemGrants.Grant`, which bakes a wand's charges from the recipient's Intelligence
  (`WandGrantEffects.Grant`) and adds anything else plainly; `AddItemWithOverride` and
  `WandGrantEffects.Grant` now return the stack they granted into. `PlayerKit`'s wand grant uses it;
  `TreasureChest` deliberately doesn't (its wands would bake from the chest's absent Intelligence).
- Rewards are always items. A non-item reward is authored as a one-time consumable whose effects do it.

**Results window** (`Presentation/UI/Lootboxes/`)
- `LootboxResultsWindowController.Show` opens one "Loot Boxes" window beside the player's Inventory
  window (a menu window: pauses, Escape closes), replacing its contents if already open. Sized from
  `LootboxChrome` (8 columns at open, up to 75% of the map's height; beyond that it scrolls).
- `LootboxResultsContent` (an `IElementContent`) lays out one section per group: a header in the rarity
  color, "Bronze Adventurer Box" or "... x3" when more than one box of that kind opened, then the
  group's combined rewards as `InventoryItemStackCell`s showing what the group granted. Re-flows on
  resize with the same reentrancy guard as `InventoryGridContent`.
- Reward cells set the new `IsDragSource = false` (reset true by every `Configure`), so they never
  start a drag and offer no context menu. Hover shows the inventory's tooltip (the summary text is now
  shared as `ItemHoverSummary`).
- Click opens Item Details docked beside the results window: `ItemDetailsWindowController.Open` takes
  an optional anchor window, and a reward whose stack has since been used up opens read-only through
  `OpenDefinition` (stack id `ItemDetailsWindow.NoStackInstanceId`; Compare does nothing).
  `GetLootboxResultsWindowRectangle` is its own outside-click hook, never folded into another window's.

## Presentation

### Floating Text

Short alerts drawn in the world above the entity they concern: damage taken, heals, regen, status stacks
added, Dodge, Immune. Notifications keep carrying the full details. Named generically so XP gained,
Level Up and Skill Up join later as new `FloatingTextKind` values.

- **One publisher, `FloatingTextFeed` (Game.World), through the EventBus.** A new immediate
  `FloatingTextEvent`, separate from the player-only logging events (`EntityDamagedEvent` and so on):
  floating text is for anyone the player can see, needs an enum kind rather than free text, the health
  actually gained rather than the pre-clamp amount, and the crit flag. With no subscriber (headless) a
  publish costs one dictionary miss. A buffered event was rejected: nothing drains it headless.
- **Visibility is the Local tier.** The feed publishes only for `ProcessingTierLevel.Local` entities, which
  bounds regen publishing to the Local population. A camera panned more than 80 tiles from the player shows
  no text, and Borough zoom draws none. Local is still far wider than the viewport, so
  `FloatingTextRenderer` skips a text outside the visible tiles (plus a 2-tile margin) before measuring it:
  without that, `MapWindow` draw doubled (0.38 -> 0.73-0.90 ms/frame windowed, Release, seed 1); with it,
  it is within run-to-run spread.
- **Only damage taken, above the entity taking it** (the player included). Nothing is drawn above an
  attacker for damage it dealt. A corpse shows nothing for damage it takes; the killing hit still shows.
- **Heals show the change in the HUD's rounded-up health**, `ceil(after) - ceil(before)`: a heal at full
  health shows nothing, and regen shows "+1" only on the visits that move the displayed number. No
  accumulator state. `ResourceGainCategory`/`ResourceLossCategory` (`Game.Resources`, shared with mana) are required parameters on `HealthHeal`/`HealthDamage`,
  so no caller can default a DoT to direct damage.
- **Status stacks are the count after a grant minus the count before**, at the two grant sites
  (`StatusEffectGrant`, whatever applied it), so a stack stopped by the cap or immunity is
  never shown. "Immune" is published there once per grant, not from `StatusEffectImmunity.IsImmune`,
  which runs once per stack. Standing immune in an aura repeats "Immune" on each re-grant, accepted.
- **Waterfall.** Each entity has two lanes, numbers (damage, heals, Dodge) on the left half of the entity
  and statuses (stacks, Immune) on the right, each releasing one text every 0.1 s -- one per frame put
  consecutive numbers ~1.5 px apart. Past 8 waiting, a text of the same kind, effect and flags is added
  into the newest waiting one, so totals stay exact. At most 256 texts at once; the oldest retires early.
- **Motion.** Rise 1 tile over 0.4 s easing out while fading 50% -> 100%, then drift at 0.4 tile/s while
  fading out over the remaining 1.6 s. Horizontal drift at 0.3 tile/s, random direction per text. Dodge and
  Immune fall and start at the bottom of the footprint; everything else rises from the top. Time is
  simulation frames, so text freezes while the game is paused. Constants in `FloatingTextChrome`.
- **Look.** Bordered (`ContrastTextRenderer`, fading the outline with the fill). Red damage, orange status
  damage, green "+N" heals, light green "+N" regen, white Dodge/Immune. A crit gets a yellow "!" and a
  1.5x -> 1x pop over 0.1 s. Stacks and Immune show the effect's icon: its `StatusEffect-<Type>` sprite if
  the manifest has one, else its registered glyph. Colours are floating text's own
  (`FloatingTextPalette`): `IStatusEffectDisplay` deliberately leaves colour to each consumer.
- **Not a particle system.** There is no second consumer; `FloatingTextMotion` and the pooled instance
  array are the pieces one would take over.
- **Found along the way: multi-tile targets were hit once per covered cell.** A multi-tile entity is in
  the occupant index of every cell of its footprint, and `ActionEffectResolver` plus potion, scroll and
  wand activation applied their effects once per cell the shape covered. Each now resolves a target once
  per activation. This changed gameplay: the seed-1 headless fingerprint moved, and disabling only the
  resolver fix restores the old one exactly.

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

`EffectFormatting.FormatEntry`, `ActionActivatorFormatting.BuildLines`,
`TargetShapePreviewGeometry`/`Element` (`Presentation/UI/`) -- all take plain `Game.Modules.Actions`
types, so a future Magic Menu gets them free. Frame counts always shown as seconds.

### Inventory tabs/search/sort/GridControl/Toggle

- Auto-generated per-category tabs (`InventoryTagQueries.GetItemCategoryCounts`): one per `Item.*` tag
  a held stack has, parents included (a potion is under Consumable and Potion), sorted by stack count
  then display name. Other tags (Self, Healing, Fire, Magic) are never tabs; see "Gameplay tags".
  `TabbedContent` supports scrollable, runtime-rebuildable tabs. User-reordering and a custom-tag
  trailing tab still open (see TODO.md).
- `InventoryGridContent.SortOrder`/`NameFilter`/`HideDisabled`, driven by `GridControl` -- a fully
  generic (non-Inventory-specific) row of grid controls (count, sort, `DebouncedTextFilter` search,
  toggle list) via `InventoryTabContent`.
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

Red-heart `Button` (`HealthWindowController`) opens
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

### Scrollbars

- **Visibility**: `ScrollbarVisibility` on `ElementChromeOptions`, `Auto` by default. A bar shows on an
  axis only while that axis is scrollable and overflowing (`MaxScrollOffset` > 0.5 px). `Hidden` still
  scrolls with the wheel but never draws or reserves anything: MapWindow (its camera is its own
  scroll), Tooltip (forces `CanUserScrollVertical` as a render-path workaround) and TabbedContent's
  tab strip (too short for a bar; see TODO.md "Scroll buttons for narrow content").
- **Gutter, only while shown**: a shown bar takes `WindowChrome.ScrollbarThickness` (8 px) out of
  `ContentSize` along the right/bottom of the content background, so content reflows instead of
  being covered. Rejected: overlay (covers the rightmost grid cells/text) and an always-reserved
  gutter (wastes space on windows that fit, doubles up on the nested Inventory body/grid pair).
- **Settle loop** in `Element.Measure`: size, measure children, recompute scroll bounds, and
  re-measure if `Auto` now wants a different bar state -- at most once more per axis (Debug.Fail
  past that). It converges because content only grows as its width shrinks.
  - Changes found outside `Measure` (`AddChild`/`RemoveChild`, child `Resized`,
    `TextWindow.UpdateText`) go through `SettleScrollbarsOutsideMeasure`. It does nothing inside the
    element's own `Measure` (`_measureDepth`), and defers to the end of an open `BeginLayoutBatch`.
  - WrapContent: only `TextWindow`'s capped WrapContent scrolls. It adds the gutter within its own
    `MaximumSize`.
- **`ContentResized`**: raised at the end of `Measure` whenever `ContentSize` changed. A gutter
  changes `ContentSize` without `CurrentSize`, so `Resized` misses it. Anything laying children out
  from `ContentSize` should listen to it: `InventoryGridContent` does. Its `RebuildCells` runs in a
  layout batch and loops, rather than re-entering, when a rebuild is requested during one.
- **Input**:
  - `ElementDragInteractionKind.ScrollThumb`/`ScrollTrack` carry an `ElementInteraction.ScrollbarPart`.
  - Thumb drag is measured from the offset at press (`Element.DragScrollbarThumb`).
  - Track press pages one visible length toward the cursor. Held, it repeats after 0.4 s every
    0.05 s, only in the press's own direction, so rounding can't make it page back and forth.
  - Precedence: an element's own bars before its children. A bar inside a resizable ancestor's
    10 px resize band wins over resize (`TryHitTestScrollbar`), except in the corners.
  - A bar press raises the window but never changes focus or fires a click.
  - Hover and pressed thumb colours are in `WindowPalette`. The cursor over a bar is Arrow, TextBox
    included.
- **Wheel**: vertical by default. Horizontal when the element only scrolls horizontally, when Shift
  is held, or over its horizontal bar -- each only if horizontal scrolling is allowed at all.
- **Not planned**: keyboard scrolling (PageUp/PageDown/Home/End), arrow buttons on bars.

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
  `QueueActionActivation`/`QueueItemActivation` for the double-tap instant-fire paths that skip
  arming). This replaced `UiInputController`'s own bespoke Escape-hold sweep entirely -- Escape-hold and
  item/action activation now share the one implementation (single-tap Escape's
  `CloseTopmostClosableWindow` is unrelated and untouched). A future Magic Menu cast goes through the
  same `ArmAction`/`QueueActionActivation` chokepoints and gets this behavior for free.

### AuraExposureComponent growth: not a leak (leak detector fix)

Investigated 2026-09-26. The leak detector reported `AuraExposureComponent` growing
0 -> ~20k with a flat entity count.

- **Not a leak.** A 36,000-frame headless run (seed 12345) sampled the pool every 600 frames: it
  peaks at ~22.2k on frame 600 and settles around ~17k, one exposure per entity (one effect type in
  use), zero simulated exposures with an overdue deadline (the symptom a missed removal or
  reschedule would leave), and dead owners only transiently (dropped on their next tick).
  `EntityManager.DestroyEntity` clears Multi pools through `RemoveAllComponents`.
- Exposures held by frozen Borough entities rise slowly (45 -> 528 over the run): exposed creatures
  wandering out of the centre neighborhood freeze with their exposure. Bounded, and
  `SkipOwedExposureTicks` handles the resume. It does show creatures drifting one way into Borough.
- **The detector's false positive:** `GameLoop.Update` ticks diagnostics before the first simulated
  frame, so the first sample has every gameplay pool empty or nearly so, and oldest-to-newest
  growth flags each one as it fills (a session under a minute flagged a dozen, including this one
  and `BurningTimerComponent`). `LeakDetector` now also requires growth in the second half of the
  window (middle sample to newest, `RecentPoolGrowthThreshold`, and any growth for the heap
  finding). With it, a 150 s session flags only `MovementDisabledComponent` (385 -> 592).
- **`MovementDisabledComponent` is not a leak either: it's corpses.** Probed over 36,000 headless
  frames (seed 12345), it grew 24 -> 1,114 alongside `DeadComponent` (3,116 -> 22,736), and every
  sample had 0-3 living holders -- the rest were dead. `MeleeDisabledComponent` is the same
  (48 -> 1,333, 0-2 living). A creature killed with every leg disabled keeps the marker: corpses
  don't regenerate, `BodyPartEffectsSystem` still visits them (its driving pool,
  `BodyPartStateComponent`, outlives death), and corpses are only destroyed when their neighborhood
  unloads. So the count is bounded by the corpse population and grows at the death rate, slowing as
  it does -- the event-marker false-positive shape `LeakDetector`'s remarks already describe. The
  markers on a corpse are inert (`MovementSystem`/`ActionActivationSystem` already refuse a dead
  entity).

### Module dependencies: Requires / RunsAfter / RunsBefore, and no optional built-in pools

Landed 2026-09-27. `IModule.Dependencies` (a list of `Type`s that meant both "requires" and "runs
after") became three Id-keyed lists; see CLAUDE.md's Modding section for the rules.

- **Why by Id.** `ModuleSet.Combine` replaces a built-in by `Id`, so a `typeof` dependency on a
  replaced module stopped resolving. That is why modules had avoided real dependencies and read
  each other's pools through `IsRegistered`/`GetOptional*Pool` instead (about 110 places).
- **Why requiring doesn't imply order.** Every component is registered before any system, so pool
  availability never depends on order. Keeping them separate removed the Movement <->
  StatusEffectAura "cycle", which was only an ordering constraint dressed up as a requirement.
- **Bugs found on the way.** Six pairs of built-in modules shared an `Id` (a mod replacing
  Inventory would have replaced Achievements). The mod dry run tried a replacement *alongside* the
  module it replaced, so a real replacement always failed with "already registered".
- **The audit's outcome: every check was hard.** Mods can only add modules or replace them, and a
  replacement must register every component the built-in did (checked in the dry run), so in a game
  every built-in pool always exists. The only ways one was ever missing were tests with hand-picked
  module sets or bare `ComponentManager`s, and per "tests don't drive design" those tests now build
  the full set (`BuiltInTestComponents`, `BuiltInTestModules`) instead. The lazily resolved and
  late-wired pools left over at the time went with the register-first bootstrap (below); the one
  that remains is null for a real reason: no component manager at all (`UiInputController`'s shop
  pool in UI tests).
- **Health never reads a status effect.** Health's regen used to skip a part holding a
  `BodyPartBurningTimerComponent`, a pool Burning registers. That check was already redundant apart
  from the ~1 s before a burn's first tick, since every tick resets the part's regen lockout for
  10 s. Burning now resets the lockout at ignition too, and Health's check is gone.
- **A queued activation clears the step.** Whatever queues an action or consumable activation also
  clears the entity's `NextMapPosition` in the same write (`PlayerCommands`,
  `TestCombatBehaviorSystem.ClearStep`), so `MovementSystem` no longer reads either activation pool.
  NpcBehavior still runs before Movement, or the step would be taken before it is cleared.
- **Latent bugs the stricter signatures exposed.** `BurningSystem`/`PoisonSystem` passed no dead
  pool to `HealthDamage`, so a dead complex entity that kept burning republished `EntityDiedEvent`
  on each tick that hit a zeroed vital part. Actions resolved through `ActionEffectResolver` had no
  mana pool, so a mana-restoring ability would have done nothing (only the Mana Potion restores
  mana today, through `ItemActivationSystem`).
- **Checking behavior stayed the same.** `--headless --seed=1 --benchmark-frames=60-120` prints a
  world fingerprint; a `git archive HEAD` copy in the scratchpad gives the baseline. The fingerprint
  hashes pool type *full names*, so moving a component to another namespace changes it without any
  behavior change -- compare with short names when that happens.
- **Performance: neutral.** Headless Debug A/B against `e85de88` (seed 1, frames 600-3600, map
  3072, 5 runs a side interleaved): `EcsContext.Update` 3.98 -> 3.93 ms/frame (-1.3%, within the
  baseline's own spread), no system outside its run-to-run range, worst frames overlapping.
  Same world over the whole range (short-name fingerprint `4185967D5DB33537` on both).

### Register-first bootstrap, self-contained build passes, settings

Landed 2026-09-28. Every object that read pools used to be created empty with `GameModuleContext`
(before any pool existed) and wired later, each its own way (`Wire`, setters, `??=`, a resolved
flag); tests had to repeat the wiring by hand, and missing a step only failed at runtime. See
CLAUDE.md's ECS and Modding sections for the resulting rules.

- **Register first.** The sequence is DeclareSettings → RegisterComponents → Configure →
  RegisterBehavior. Chosen over a post-registration bind hook and over handles (Forge's
  `RegistryObject` shape) because it removes the gap instead of bridging it -- the shape of Forge's
  registry events, Factorio's data stage and Bevy's `build()`. No built-in `RegisterComponents`
  depended on `Configure`, so nothing had to move for it.
- **Staged Engine builder, named for states.** `EcsBuilder.Begin` → `SortedModules` →
  `RegisteredComponents` → `ConfiguredModules` → `RegisteredBehavior`: each type is what is already
  true, its methods the transitions; each stage advances once. Chosen over one `Build` call with a
  callback in the middle.
- **Engine owns every phase, generic over the context.** `IModule<TContext>`; `RegisterBehavior`
  gets the context in `BehaviorRegistration<TContext>`, which removed ~100 `= null!` fields modules
  carried from `Configure`. `Configure` still exists as its own phase: catalogs and registries
  another module's `RegisterBehavior` reads (aura appliers, actions, blueprints before `ResolveAll`)
  must be complete before any system is built.
- **Foundation modules.** The context is built from Core's, ProcessingTier's and Blueprints' pools,
  so every build contains them (`GameModuleContext.FoundationModuleIds`). ProcessingTier's
  `Requires` on Movement existed only for `LocalTierRoster`'s wiring; the roster moved out of the
  context into the pass result, so ProcessingTier now requires only Core (still runs after
  Movement) -- which keeps the foundation small enough for test builds of a few modules.
- **Self-contained passes.** Before, one `GameBootstrapper.Build` reconfigured the *same* module
  instances N+2 times (each mod's dry run, staging, real) against the *real* World, and was correct
  only because the real build ran last. Now every pass instantiates from `ModuleFactory`s and gets
  its own World (dry runs and staging over `CreatePlaceholderMap()`), EventBus, keys and
  MathUtility. `ModValidation` and `GameBootstrapper.Build` are separate entry points for the
  future menu: validation at game start / mods changed, the build at new game / load. Found on the
  way: the staging and dry-run builds had never wired their `FloatingTextFeed`, and dry runs drew
  from the session's own MathUtility.
- **`EntityFactory` split.** `EntityBuilder` (definitions, key table, rolls) builds;
  `EntityFactory` spawns through one. The build-only constructor and its nullable fields are gone;
  `SpawnRecordRebuilder` builds into the staging pools with an `EntityBuilder` over the *session's*
  registry, so a blueprint registered after staging was built still rebuilds.
- **Settings (phase 0).** Built with no consumers: typed keys owned by a module `Id`, validated
  declarations, frozen values, failures reported rather than dropped; command-line source only
  until something needs a file. Settings may size pools, never remove a built-in one.
- **Verified unchanged.** Same headless world fingerprint as before on seeds 1, 7 and 11; headless
  A/B vs the pre-change build: `EcsContext.Update` 3.81 vs 3.81 ms/frame, no system beyond run
  spread. Subscription order moved in two places, neither observable: `LocalTierRoster` now
  subscribes after every system; the factory's `EntityDestroying`/`TierChanging` handlers before
  every system (no system subscribes to either).

### Diagnostics wired through named engine hooks

Landed 2026-09-28 (planned in six phases; the plan file was deleted once this section held it). `DiagnosticsEngine` used to reach the engine
by a separate hand-wired path per feature: a `StartupProfiler?` parameter through five build entry
points, `SystemManager.Profiler`/`EventBus.Profiler` setters, a recorder passed to
`ShellContext.LoadContent`, `AttachEcsContext` after the build, and a four-call frame protocol copied
into `GameLoop` and `HeadlessBenchmark`. See CLAUDE.md's Diagnostics section for the resulting rules.

- **Static channels, one listener each.** Unreal Insights' model: engine code emits unconditionally,
  a channel nobody listens to costs a read and a branch. An injected hooks object was rejected because
  it would have to be threaded to `EcsBuilder`, `SystemManager` and `EventBus` -- the thing being
  removed. One listener per channel (no multicast on the hot path); `DiagnosticsEngine` fans out.
  Subscribing tests are `[DoNotParallelize]` rather than the channels being `AsyncLocal`.
- **`EngineHookChannel<TListener>`** replaced phase 1's property-plus-subscribe-method as soon as a
  second channel arrived. Disposing a subscription clears the channel only if it still holds that
  listener, so a stale subscription can't unhook its successor.
- **Two ways to record a frame cost.** `EngineHooks.FrameCost(...)`, a disposable struct like
  `DiagnosticScope`, at sites that run a few times a frame (`GameLoop`'s Shell.Update, Shell.Draw and
  SpriteBatch.End rows; `ShellContext`'s per-window update and draw); before it, `GameLoop` took timestamps even with nothing listening. An
  explicit branch on `FrameCosts.Listener` in `SystemManager.Update` and `EventBus.Publish`: a wrapper
  there would enter a try/finally and evaluate its arguments (the event's cached type name, a
  dictionary lookup) on every system and every publish, even with diagnostics off.
- **The session is declared, not inferred.** Every build makes an `EcsContext`, including mod trial
  builds and the staging rebuild, so "an EcsContext was built" can't mean "a session started", and
  "first `Update`" was rejected as an inference. The host calls `BeginSession()`. It marks the context
  started only after the listener accepts it: the first version marked it before, so a rejected start
  still emitted an ending on dispose (caught by a test).
- **Benchmark closes as frame `EndFrame - 1` ends,** not as `EndFrame` begins, so a headless run that
  updates until complete simulates the same frames as before and fingerprints still match older
  builds. Windowed, the range loses the one shell Update/Draw after its last frame.
- **Things that now pause with the simulation:** memory/leak sampling and the periodic console and
  `latest.json` report (paused or menu mode), since they run off simulation frames. Headless passes
  `writesPeriodicReports: false`, so it never overwrites a windowed `latest.json`.
- **`PlayerActivityLog.BeginFrame` is gone.** The log stamps each line from the session's
  `SimulationClock` and `DateTime.Now` when it writes it.
- **New Draw row "GameLoop" / "SpriteBatch.End".** The frame's batch is `SpriteSortMode.Deferred`, so
  the final `End()` flushes everything queued since the last render-state change (usually the top
  layers). That flush was in no row before. Kept separate from Shell.Draw so Shell.Draw stays
  comparable with older runs; the draw total is the two together.
- **Startup report is nested.** `PhaseRecord.Depth`, phases in start order. `EcsBuilder` stages and
  each module's phase, `ResolveBlueprints`, the staging build and each mod's `Trial:<type>` now show
  up, where trial builds used to be one opaque "DryRunValidateMods" number. `StartupProfiler` stops
  listening at the first simulation frame, so a runtime staging rebuild isn't counted as startup.
- **A pool memory report whose range never closed is dropped** at session end with a console line,
  not written -- it would claim the whole range.
- **Out of scope, own TODO entries (both landed -- see "Named diagnostics gauges, population-aware leak detection, the Diagnostics window"):** named gauges; leak detection that compares built-only pools
  against built entities rather than every living entity.
- **Verified unchanged (phase 6).** Baseline: `93075a5` (the commit before phase 1) exported with
  `git archive` into gitignored `Log/b93` -- the scratchpad path broke Windows' 260-character limit on
  the sprite folders -- and saved as `Log/phase-benchmarks/baseline-93075a5-{debug,release}`.
  Seed 1, frames 600-3600, map 3072.
  - Headless A/B, 5 runs a side interleaved: same world on both sides (fingerprint
    `4A5360E840EEA660`), so the benchmark still simulates exactly the same frames. Debug
    `EcsContext.Update` 3.510 -> 3.511 ms/frame (0%), Release 1.267 -> 1.248 (-1.5%, inside A's
    spread); no system or `EventBus` row flagged in either. The cost with nothing listening can't be
    measured this way (a benchmark is itself the listener); the emit sites swapped an instance
    property read for a static field read, and the with-listener rows didn't move.
  - Memory A/B (Release): identical -- pools 171.8 MB, live heap 969.5 MB, same allocation and
    collection counts, no pool differing.
  - Windowed (Debug): report rows and names unchanged apart from the new `SpriteBatch.End` Draw row
    (0.026-0.048 ms/frame). Back-to-back pair: Update 4.75 vs 4.79, Shell.Draw 0.770 vs 0.760. Single
    windowed runs swung 4.2-6.7 ms on Update across four runs, so only back-to-back pairs mean
    anything; use the headless A/B for decisions.
  - Startup: same top-level phases, all within ±4%; time to stable 14.4 s vs 12.6 s (one run each).
    The report went from 100 flat to 195 nested phases. The first build to run (the staging build,
    ~123 ms) pays the JIT warm-up; the session build's stages then take ~3 ms.
  - **Found on the way:** both skill scripts matched a report by process id alone, and the memory
    A/B's baseline run reused the pid of a six-day-old run, silently comparing against that stale
    report. Both now accept only a file written after their own run started.

### Named diagnostics gauges, population-aware leak detection, the Diagnostics window

Landed 2026-09-29 (planned in six phases; the plan file was deleted once this section held it). Frame
costs said what a frame spent; nothing recorded what the world *was* while it spent it, so the "Gen-1
GC frames during a window shift" investigation had to infer GC timing and streamer depth from which
systems got expensive. See CLAUDE.md's Diagnostics section for the resulting rules.

- **Registry on `EcsContext`, frozen at `BeginSession`, cleared on dispose.** Every build has one;
  only a session reaches a listener, so a trial or staging build's gauges are never sampled. Frozen
  so the tracker sizes its arrays once; cleared so closures over the streamer and pools don't outlive
  the session. Registration is done by the composition site (`GameBuildPass.Run`,
  `WorldSessionBootstrapper`) from read-only counters the owner exposes -- the streamer knows nothing
  about diagnostics. Modules can't register gauges yet: `EcsContext` doesn't exist during
  `RegisterBehavior`; add a `BehaviorRegistration` hook with the first module that has one worth it.
- **Two kinds.** `Cumulative` gauges are stored as their change per frame (from the value when the
  tracker was built, not since launch), so "a gen-1 on frame 1843" reads directly; a mean of a raw
  counter is meaningless. `Func<double>` for every value -- all are integers below 2^53 today.
- **Frame-end sampling only**, after the frame's cost is recorded, so it never lands in a benchmark
  row. Mid-frame sampling was rejected: it needs a second emit mechanism, and what it would show
  (work done inside the frame) the streamer's cumulative gauges already give per frame. An
  intra-frame peak, if ever needed, is an owner-kept high-water mark sampled at frame end.
- **Aggregate pool gauges, not one per pool.** ~150 per-pool series would drown the report;
  `PoolMemoryReport` has per-pool detail. A step in `Pools/EstimatedBytes` marks a resize.
- **Zero allocation per sample** (`GaugeTracker`, `GaugeSeriesReport`, `GaugeHistory` -- tests assert
  it): allocating inside the range would show in the `AllocatedBytes`/`Gen0` gauges being sampled.
  Series and the 300-frame live ring are allocated in `SessionStarted`. The live ring exists
  whenever Gauges is on, headless too (a few array writes per frame).
- **Reports.** `gauges-<utc>-<pid>.json` is compact (192 KB for 3000 frames x 25 rows; indented
  would put every value on its own line) plus a `.txt` of summaries; values rounded to 3 decimals
  (`DiagnosticsReportRounding`) so float noise stays out of files. `latest.json` gets last/min/max
  per 5 s interval -- the last value alone hides a spike between reports.
- **`EntityPopulationPolicy`** is the game's split of living entities (the
  `SystemManager.SimulatedTierCount` shape): "Built" = living minus skeletons, with skeleton
  component types held by every entity. One count function backs both the policy and the
  `Entities/Built` gauge. `LeakEvaluation` (extracted from `LeakDetector` so tests build histories
  directly) compares a skeleton-type pool against living entities and every other pool against built
  entities; the heap finding needs both flat, since the heap holds both. Without it, every walk
  across a neighborhood edge flagged the built-only pools (a promotion grows them while the living
  count stays flat) and a real leak in one hid inside that.
- **Diagnostics window (F3)** replaces the 24 px strip under the map. Not a menu window: menu mode
  pauses the simulation. That exposed a `UiLayerStack.Add` bug -- an element added during menu mode
  was promoted to a menu window even when exempt, so it would have kept the game paused after the
  menu windows it opened beside had closed. Exempt elements are no longer promoted (shared fix, not a
  special case), and `UnmarkMenuModeExempt` gives a pooled window's exemption back on close. Text
  refreshes 4x a second and the layout keeps its cursor in fields (a local-function closure
  allocated per draw), so the window adds little to the allocation it shows.
- **Found on the way: the benchmark's first frame was always its slowest** (8.8-12.4 ms vs a
  4.0 ms median, Debug). The spike followed the range's start frame, not the simulation:
  `FrameRangeBenchmark.Record` returned early before the range, so its first in-range call JIT-ed
  the tuple-keyed dictionary path and grew both dictionaries from empty inside a measured frame.
  `Record` now adds each entry from the first frame and adds time only inside the range, one
  struct entry per row (one hash lookup instead of four); rows never recorded in range are left out.
  First frame now 3.7-4.5 ms. Recording is slightly cheaper, so compare against baselines saved
  after this change.
- **Don't combine `memory` and `gauges` when reading allocation:** `PoolMemoryReport`'s baseline copy
  happens at the range's first frame and lands in that frame's `AllocatedBytes`.
- **Verified unchanged.** Baseline `e78544a` (the commit before phase 1) via `git archive` into
  gitignored `Log/bhead`, saved as `Log/phase-benchmarks/baseline-e78544a-{debug,release}`. Seed 1,
  frames 600-3600, map 3072, 5 runs a side, gauges off: same fingerprint (`4A5360E840EEA660`) on both
  sides. Debug `EcsContext.Update` 4.309 -> 4.255 ms/frame (-1.2%); Release 1.447 -> 1.497 (+3.4%)
  and on a repeat 1.452 -> 1.428 (-1.7%), so noise; no system flagged in any. Gauges on vs off
  (Release, current build, 2 x 3 runs each, interleaved): `EcsContext.Update` 1.414-1.419 vs
  1.435-1.452, wall clock ~4.3 s both, same fingerprint -- sampling cost is below what these runs
  resolve. The windowed open-vs-closed Draw cost of the Diagnostics window was not measured (needs
  F3 in a live window).

### Shell composition cleanup: GameSession, views, commands, and the item-window coordinator

Landed 2026-09-29 in six phases (PLAN-shell-composition-cleanup.md). `ShellBootstrapper.Build` had
grown to ~250 lines of game rules, window-to-window wiring and admin setters, and constructor lists
kept growing because Game's API was static functions taking `ComponentManager` plus catalogs, bus and
player, so every UI element that read or acted carried all of them. See CLAUDE.md's Layers and
Presentation sections for the resulting rules. Headless seed-1 fingerprint unchanged
(`93B326D0E3582C70`) across every phase.

- **`GameSession` replaces `GameBootstrapResult`**, built by name from the build's `GameModuleContext`
  -- three positional bundles (context, bootstrap result, world session) had re-listed the same fields.
  `GameModuleContext` isn't exposed: it carries build-phase surface (registries still being filled,
  `MathUtility`, `EntityMoveSync`). `WorldSessionContext` dropped from 22 fields to 4.
- **Achievement loot-box claims live in `AchievementModule`**, fired by an immediate (not buffered)
  `AchievementNotificationDismissedEvent`: buffered events drain in the simulation, which doesn't run
  while paused or in menu mode, so a buffered claim could miss an inventory opened straight away.
- **`AdminTools`** (`Game.Admin`) holds every Admin Mode command set; `MapWindow` takes
  `AdminContextMenuOptions` as a required dependency instead of four nullable setters. The F12 toggle,
  the title sync and `GlobalState.IsAdminModeOn` display reads were left where they were.
- **Commands and views are per-session instance services over the static rules**, which stay the
  implementation because systems call them with their own pools; a service method is the static call
  with its pools bound, not a shim. Services are built by `GameBootstrapper` from built-in pools, not
  module-registered -- a module-registered service (a mod replacing `ShopCommands`) can come later
  without changing callers. `PlayerInputBuffer` moved into Game as `PlayerCommands` (it holds gameplay
  input rules: expiry, stagger drop, dodge steps); `HotbarContent`'s binding rules moved into
  `HotkeyBindingCommands`, except the Expansion-slot lock, which stays a Presentation drop-target rule.
- **Presentation types take the specific services they use**, except composition points: the item
  windows, grid and currency row share `InventoryServices` (seven services each otherwise), and
  `ShellServices` goes only to `ElementFactoryRegistry` and `ItemWindowCoordinator`. Tests build them
  with `TestInventoryServices.Over` and `TestUiInputController.Create` (the old argument lists).
- **The architecture test bans the store, not component values.** Banning `*Component` values would
  have needed stand-in structs across most of Presentation; views return component copies (a stack,
  a modifier). It scans the whole Presentation assembly, not a list, so new code can't regress;
  `InspectionWindowContent` and `DiagnosticsWindow` are exempt by name (`MapTintGrid` was until the aura field replaced it).
- **Two writes found while moving reads:** the loot window wrote `LootedComponent` directly (now
  `LootCommands.MarkLooted`), and Escape cancelled a windup with raw pools (now
  `PlayerCommands.TryCancelWindup(now)`).
- **`ItemWindowCoordinator`** (Presentation) builds the item-window controllers and holds the rules
  between them; it had no tests while it lived in the exe. `ItemWindowCoordinatorTests` drive it
  through `MapWindow`'s loot/shop click entry points, a grid's item click and `ReleaseEntity`, using
  `TestMapWindows` (the `MapWindow` builder, moved out of `MapWindowTests`). A `PresentationContext`
  needs a GraphicsDevice (`SpriteBatch`), so tests can't reuse `ElementFactoryRegistry`; they register
  the element factories they need.
- **`PointerState`** replaced the late-bound `Func`s that fed cursor text and the drag ghost, and the
  public drag properties `UiInputController` exposed only for them.
- **Found live in between: a bought item the player already carried couldn't be put on the hotbar.**
  A purchase moved the shop's stack verbatim, the grid folded the two stacks into a Merged Stack cell
  with no single `StackInstanceId`, and the hotbar refuses those. See "Shops" for the fix (completed
  purchases merge).

### Gameplay tags

Replaced the flat `Tag` enum (the plan, now deleted, landed in four phases on 2026-09-30).

- **Engine (`Engine/Tags/`):** `GameplayTag` is a 2-byte handle to a dot-separated name; the parent is
  the name minus its last segment. `GameplayTagNames` is the process-wide intern table (names and
  parents only -- which tags a build accepts is its `GameplayTagRegistry`). **Ids follow intern order,
  which follows static-initialization order, so they differ between runs: never order, save or persist
  by id.** Sort by name; save by name.
- **`GameplayTagSet`** wraps one immutable `ushort[]` (8 B), keeps the order tags were given (never id
  order), de-duplicates, and has no size cap. Build sets once (a definition, a static field), never per
  frame; queries allocate nothing. `Has` is parent-aware, `HasExact` isn't. `GameplayTagQuery` (all /
  any / none) is a shared immutable instance on definitions and shared data, never a per-entity value.
- **Registration:** `IModule.DeclareTags`, run by `GameBuildPass.BuildModules` before
  `RegisterComponents`; declaring a tag declares its parents. The built-in vocabulary is
  `Game/Tags/GameTags.cs`, declared by `CoreModule` so a tag shared between modules never forces a
  `Requires`. `ContentTagValidation` (after Configure, beside `ResolveAll`) throws on an action or item
  -- or one of its `StatModifierGrant` conditions -- using an undeclared tag, so a mod with a typo fails
  its dry run (`UndeclaredTagItemModule` fixture).
- **Combine, don't nest:** a tag has a parent only if every child always is that parent
  (`Delivery.Melee.Unarmed`). A property that applies to unrelated things is top-level and combined:
  `Magic` goes with anything, `Damage.*` is only the damage type. A fireball hit is `Damage.Fire` +
  `Magic`, a torch hit `Damage.Fire`, Magic Missile `Damage.Energy` + `Magic`; "magical fire only" is a
  query, not a tag.
- **Implied tags:** `IActionActivator.ImpliedTags` (Spell → `Action.Spell` + `Magic`, Scroll →
  `Item.Consumable.Scroll` + `Magic`, Potion → `Item.Consumable.Potion`, Wand → `Item.Wand` + `Magic`)
  are unioned in by `ActionDefinition`/`ItemDefinition` at construction, so every copy of a definition
  carries them, including ones never registered in a catalog. A `with` that swaps the activator kind
  keeps the old kind's implied tags. A mastered scroll's spell drops the scroll's `Item.*` tags.
- **Consumers:** `StatModifierComponent.ConditionTag` is a `GameplayTag` (`None` = unconditional),
  matched parent-aware -- a `Damage.Fire` resistance covers any `Damage.Fire.*`, the melee penalty and
  lockout cover Unarmed. `ShopComponent.AcceptedItems` is a shared `GameplayTagQuery?` (null = any).
  Presentation shows `GameplayTagRegistry.GetDisplayName` (declared name, else last segment); the
  registry reaches it through `GameCatalogs.GameplayTags`, `InventoryServices`, and the Health / Item
  Details windows' constructors.
- **Cost:** headless A/B against the enum build (2026-09-30, seed 1, frames 600-3600): same world
  fingerprint, no system beyond noise; `ComplexHealthRegenSystem` -0.3%, `SimpleHealthRegenSystem`
  -0.4%, `EcsContext.Update` -0.3%.

### Auras, terrain contact, Healing Shrine, Holy Ground

Landed 2026-10-01. The healing aura was the second aura effect after lava's Burning; the work made
auras, terrain contact and the glow generic enough that it is content.

What was built:
- **Auras are their own definitions**, not status effects. `AuraDefinition` (Guid, name, glow colour,
  its `Effects`) in `AuraCatalog`, registered by the module that owns each: Burning and
  Poison (`StatusEffectGrant`, topping up), Healing (`DirectHeal`), Light (glow only; the fake
  `StatusEffectType.Light` is gone). A source is an aura id and a strength.
- **One aura field.** `AuraField` holds where every aura reaches and derives the glow from the same
  totals; `MapTintGrid`, its store-access exemption and `AuraSourceAddedEvent` are deleted.
  `AuraSystem` observes the source pool for adds and keeps where each entity source is in the field.
- **Terrain contact is a list of effects** (`TerrainContact`), applied on stepping on and at an
  optional repeat. `ContactDamage*` became `TerrainContact*`. The effects are the shared `Effect`
  lists (`Game.Effects`), not a vocabulary of their own: the first version had `ContactDamage` and
  `ContactStatModifier`, hand-written copies of `DirectDamage` and `StatModifierGrant` that a new
  terrain behaviour would have had to keep adding to.
- **`StatModifierEffects.ApplyOrRefresh`**: one modifier per source, its expiry replaced.
- **One effect vocabulary** (`Game.Effects`): auras and terrain contacts hold
  the same `Effect` lists actions and items do. Decisions: no source entity means no crit, no
  ability bonus and no Outgoing modifiers; an aura's effects are attributed to the aura
  (`ActionSourceKind.Aura`), not to a source of it; an aura's strength scales amounts only, chosen
  per aura (`AuraMagnitude`); nothing caches whether an entity can be affected -- every tick tries
  again, and a refused tick is reported once per stay; stacks land through
  `IStatusEffectApplier.ApplyStacks` in one call. The aura grid no longer clips a source's reach on
  an unbounded map, so adding and removing one always mirror each other across window shifts. The
  entity-level Burning display shows the highest stack among the entity and its burning parts.
- **Body-part targeting is explicit** (`BodyPartTargeting`). A burn used to land on a body part because
  the entity happened to hold a terrain contact exposure, which tied what an aura did to where the
  entity stood and to the contact dealing damage. Now the granting entry names the part and
  `BurningApplier` knows nothing about terrain. Lava's contact holds 8 stacks on the ground-contact
  part and deals its damage there; lava's aura tops a random part up to its strength each tick, so
  an entity near lava accumulates burning parts -- deliberately far more dangerous than before (a
  goblin standing in lava dies in two to four seconds).
- **Content:** `HealingShrine` (100 health, strength-16 healing aura, half damage, no fire damage,
  immune to Burning, Poison and Paralysis), one per 1,000 Ground cells plus one by the spawn; Holy
  Ground (strength-8 healing aura, 10% damage reduction for five minutes), one cell by the spawn.
- `IStatusEffectAuraApplier` and its registry lost "Aura" from their names: actions use them too.

Decisions:
1. **Glow colour belongs to the aura, not the source.** It is what lets the glow be read from the
   gameplay grid: a cell's tint is the auras' colours weighted by their strength there. One source may
   carry several auras, each with its own reach and colour.
2. **Every aura applies on its one-second tick only**, never on entering range -- Healing, Burning and
   Poison alike. Decided for healing (stepping in and out would heal faster than once a second) and
   applied to all for one rule. A fast or lucky entity crosses an aura untouched. This reversed
   grant-on-entry.
3. **Heal amount is the strength at the cell, flat, and overlapping healing auras add.** On a
   creature with body parts the whole amount goes to its most damaged part (regen's rule). Split
   evenly across parts, most of it landed on full parts and was lost, and the floating text -- the
   change in displayed health -- almost never showed.
4. **Holy Ground's blessing is granted on stepping on and every second while standing.** That is the
   pattern for any buff or protection tied to an area or terrain: a repeating contact refreshing a
   timed modifier.
5. **Shrines never move, may stand on lava, never on walls**, and are fixed per neighborhood: rolled
   from the layout seed with their own sequence, so a revisit finds them where they were and no
   creature's roll shifts.
6. **Every contact hit lands on the terrain's body part or the bottommost one.** Lava's repeat hits
   used to land wherever `HealthDamage` picks by default.
7. **A status-effect immunity does not stop contact damage**: the Vial of Warding blocks the burn,
   not lava's hit. Fire immunity that covers both is a 100% `IncomingDamage` reduction conditioned on
   `Damage.Fire` plus Burning immunity, which is what the shrine has.
8. **An aura definition lives with its source; no module registers one.** The first version had
   `BurningAura`, `PoisonAura`, `HealingAura` and `LightAura` classes, each registered by a module,
   named by Guid from the content that radiated them and checked by `AuraContentValidation`, with a
   `HealingAurasModule` existing only to register an aura and the shrine together. That doesn't
   scale to the number of auras real content will have, and it stops an aura being made at runtime.
   Now lava, Holy Ground, the shrine, the Toxic Idol and the torch scroll each declare their own,
   sources hold the definition itself, and the build fills the catalog from the content
   (`AuraContentRegistration`). Holy Ground and the shrine have separate healing auras, so standing
   in both is two ticks a second rather than one tick of their sum -- the same healing.
9. **`AuraField` holds no component pool**, so `GameModuleContext` can build it without making Auras
   a foundation module. Terrain auras are the field's; entity sources are `AuraSystem`'s to place.
10. **Nothing unsimulated gains an exposure**; an entity is exposed when it resumes. Before, a
    stationary entity whose neighborhood became simulated was never exposed.

What went wrong, and what it taught:
- **Spawn order decides what pools cost.** Random shrines planned a row at a time had ids interleaved
  with the creatures', so their components touched every page of four pools' entity indexes: +31 MB
  of pools for 9,000 shrines and the aura system 18% slower. The slowdown was cache pressure, not
  aura logic -- two experiments with healing disabled still showed it. Planning a neighborhood's
  shrines first, in batches of 64, took it to +7.5 MB and no measurable frame cost. Recorded as a
  rule for the real generator in TODO.md "Real map generation".
- **A second aura costs every move a lookup.** With Healing in the field beside Burning, each move
  checked two auras. A coverage bit per cell (`NeighborhoodBits`) settles a move that is in no aura;
  a move inside one still pays a hash lookup per aura.
- **The world fingerprint hashes component type names**, so a rename changes it with no change in
  behaviour. Compare every pool's count and distinct values between builds instead
  (`Invoke-MemoryReport.ps1`) when a phase renames a component.
- **Building the field before the startup heap compaction** raised peak working set by 210 MB. It is
  built after it, under its own startup scope ("Aura Field Build").
- **The skeleton guard caught a real bug**: a source placed in a frozen neighborhood re-checked the
  exposures of the unbuilt creatures around it.
- **Shrine planning took 125-210 ms on the worker** until its rolls moved from `new Random(seed)` to
  `SeededRandom` (4.8 ms). The first walk on that build had a 54 ms frame with a gen-2 collection;
  the walk after the fix had neither. Not proven to be cause and effect.

Measured, final build against the build before the work (2026-10-01, seed 1, frames 600-3600, map 3072):

| | Before | After |
|---|---|---|
| Headless Release, whole update (`ab-20261001-153812.json`, 5 runs, spread 3.9%) | 1.4473 | 1.4759 ms/frame (+2.0%) |
| ... aura system | 0.2901 | 0.3141 (+8.3%) |
| ... terrain contact system | 0.0308 | 0.0330 (+7.1%) |
| ... MovementSystem / BurningSystem | 0.1953 / 0.1491 | 0.1999 / 0.1362 |
| ... worst frame | 4.6-6.0 ms | 6.5-8.8 ms |
| Headless Debug, whole update (`ab-20261001-153450.json`, 3 runs) | 4.1099 | 4.1755 (+1.6%) |
| ... aura system | 0.7290 | 0.8013 (+9.9%) |
| Windowed Release: update / `MapWindow` draw / worst frame | 2.55 / 0.61 / 21.1 ms | 2.11 / 0.41 / 7.3 ms |
| Memory: pools / live heap / peak working set | 171.8 / 969 / 1,419 MB | 179.4 / 1,002 / 1,464 MB |
| Entities at start / built | 654,259 / 73,210 | 663,205 / 82,524 |
| Startup: session setup + presentation + shell | 2,561 ms | about 2,350 ms |
| Window shift walk: mid-load worst frame | 21.31 ms | 20.83 ms |
| ... loads done, worst frame | 14.37 ms | 8.37 ms |

Nothing is flagged by the benchmark's rule. The refactor alone (through Holy Ground) was within a few
percent everywhere and removed a grid, a terrain scan and 190 ms of startup. The aura system's 8% is
the second aura's lookup on moves inside an aura; the memory is 9,300 shrines and their aura's reach.
The windowed figures are one run each, hours apart, so they show direction only. Open items are in
TODO.md "Aura follow-ups".

#### Aura stage 1: source-move scans, source names, planning rolls (2026-10-02)

What changed:
- **A cell's occupants are read as a span** (`IMapQuery.GetOccupantEntityIdSpanAt`), valid until that
  cell's occupants change. A reader that runs open-ended code per occupant copies first
  (`ActionEffectResolver`, `ProcessingTierSystem`, `TerrainContactSystem`): a stale span fails
  silently where the old list threw. The frame's moves are read the same way
  (`FrameEventBuffer.ItemSpan`).
- **A source is scanned by its own reach, and a move scans only the cells that changed sides.** An
  exposure can end only where the source's old diamond covered and its new one doesn't, and start
  only in the reverse: about `2R + 1` cells each way for a one-tile step instead of the whole
  diamond. The whole diamond is scanned when a source is added, removed or first placed, and when its
  last placement exposed nobody because it wasn't simulated then (`SourcePlacement.ExposedOccupants`).
  `AuraField.MaxScanRadius` and `SourceSplatting` are deleted.
- **A resync adds each source at its new position before removing it at the old one.**
  `NeighborhoodBits` drops a neighborhood's coverage bitmap when its last bit clears, so the other
  order freed and reallocated 393 KB on every step of a neighborhood's only source. Found by the
  zero-allocation test, not by review.
- **Sources have names** (`ActionSourceNaming`, `GameViews.ActionSourceNaming`): an entity's recorded
  name, a terrain's or an aura's `Name` read by id at display time, or the kind. The Health window
  shows the Ability Score window's source popup for stat-modifier and status-effect rows
  (`IStatusEffectDisplay.GetSource`); a row whose effect records no source has no popup. Both windows
  read the cursor from `PointerState`.
- **Layout and population roll from `SeededRandom`.** Planning one 1024x1024 neighborhood went from
  109 to 87 ms (Release, tiered compilation off, median of 9); a roll is 4.2 ns against 6.8. The
  saving is per roll, about 8 million of them a neighborhood. **Every world for a given seed changed**
  (seed 1 ends frames 600-3600 at fingerprint `A66729EC8E9FEBC4`), so benchmarks saved before this
  are a different world; the default A/B baselines were re-recorded. The 110 ns-a-roll anomaly once
  seen in the shrine loop did not appear in either pass.

Measured (headless, seed 1, frames 600-3600, 5 runs a side, against the commit before the work, before
the planning-roll switch):

| | Before | After |
|---|---|---|
| Release, whole update (`ab-20261002-161607.json`) | 1.9065 | 1.8001 ms/frame (-5.6%) |
| ... `TestCombatBehaviorSystem` | 0.6014 | 0.5211 (-13.3%) |
| ... `ProcessingTierSystem` / `TerrainContactSystem` | 0.0211 / 0.0575 | 0.0185 / 0.0536 |
| ... `AuraSystem` / `DeathSystem` | 0.5075 / 0.0071 | 0.4991 / 0.0069 |
| ... worst frame | 7.1-10.2 ms | 7.3-8.9 ms |
| Debug, whole update (`ab-20261002-161332.json`) | 5.3308 | 5.3125 (-0.3%) |
| Allocated over the range (one run each) | 229.7 MB | 204.2 MB |

The aura system is flat because the benchmark world has almost no moving sources; the scan work shows
only where one moves. The two builds end in different worlds, cause not established. `DeathSystem`
did not fall, so the removal scan a dying source pays is not what raised it, and neither is the death
count (TODO.md "Aura follow-ups"). Unloading a neighborhood with its shrines is covered by a
teleported walk through the whole session (`AuraEvictionWalkTests`): the field afterwards matches one
built from scratch across the border.
