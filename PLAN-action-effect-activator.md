# ActionEffect / ActionActivator: unify Ability and Consumable effect shapes

(Approved plan, saved for later implementation. Addresses TODO.md's "ConsumableEffect effect
shape doesn't scale" and "Scrolls (requires restructuring actions into ActionEffects/
ActionActivators)" items together.)

**Revision 2** adds: an explicit ordering rule, damage variance + crit chance/multiplier
(rare-but-large by design, with a sequential-attack example — "Double Tap"), `ChainedEffectEntry`
(probability-gated triggered effects), multi-`ActionEffect` activators, and a modded-content
fail-hierarchy note for the future save-system TODO item.

**Revision 3** adds: confirms DoT damage (Poison/Burning) deliberately does not roll variance/crit
— they call `HealthDamage.Apply` directly and were never routed through `DamageEffectEntry`, so
this was already true, now stated as a confirmed decision rather than an inferred one; adds
`AuraSourceToggleEntry` (toggles the existing `StatusEffectAuraSourceComponent` on the caster,
unblocking TODO.md's "Toggle poison aura ability"); and records a scoping decision that
potion-cooldown-abuse stays exactly where it is (`ConsumableActivationSystem`'s own kind-uniform
logic), not a composable per-item entry.

All incorporated directly below, not left as open questions.

## Context

`TODO.md`'s "ConsumableEffect effect shape doesn't scale" item: `ConsumableEffect` (`Game/Modules/Inventory/ConsumableEffect.cs`) grows one dedicated field per effect (`HealFraction`, `ManaFraction`, `HotkeySlotGrant`) — fine at 3, won't scale to "dozens or hundreds" of future effects (buffs, debuffs, teleports, summons, ...).

A second TODO item, "Scrolls (requires restructuring actions into ActionEffects/ActionActivators)", names this as *the same generalization*: split the ability/consumable action shape into a shared **ActionEffect** (what an action does) and per-type **ActionActivators** (how/how-often/which costs apply — mana for spells, one-time-use+splash for potions, one-time-use+special-targeting for scrolls, charge-spend for wands). The user asked to merge both items and build the full split now.

Design feedback incorporated (not left as open questions):

1. **Consumables will deal damage too** (explosives) — `Damage` is a first-class, composable entry like everything else, not a special ability-only field/exception.
2. **Each effect's own application logic lives with that effect, not inside a central resolver switch.** `IActionEffectEntry` has an `Apply(ActionEffectContext)` method; every concrete entry implements its own logic. Mirrors the existing `IStatusEffectAuraApplier` precedent (`Game/Modules/StatusEffects/IStatusEffectAuraApplier.cs`) — same idiom, no registry needed since entries are already concrete typed instances in a list, not resolved from a bare enum.
3. **Composition order is an explicit rule, not an implicit accident** (see dedicated section below).
4. **Damage variance, crit chance, crit multiplier** — with a deliberate design lean toward rarer, bigger crits than typical, and a worked example of a stacking, sequential-attack crit-chance buff ("Double Tap") built entirely from existing infrastructure.
5. **`ChainedEffectEntry`** — probability-gated, can trigger multiple `ActionEffect`s, with a depth guard against runaway/self-referential chains (a real failure mode in every shipped proc-chain system — WoW/PoE both explicitly guard against procs triggering themselves).
6. **`IActionActivator`/`ChainedEffectEntry` can each trigger multiple `ActionEffect`s**, not just one — both use the same ordered-list shape, applied by one shared helper.
7. **Future save system must degrade gracefully when saved content references a mod that changed/vanished** — noted as an addendum to the existing "Data storage" TODO item, not designed in full now.

Exploration confirmed the two pipelines (`Game/Modules/Abilities/`, `Game/Modules/Inventory/`) already share `TargetingSpec`/`TargetShape` (`Engine/Math/`) and the `ActionLockComponent` gate (`Game/Modules/Core/`); that `AbilityEffect`/`ConsumableEffect` field access is tightly contained; that `MathUtility` (`Engine/Math/MathUtility.cs`) is already the codebase's shared, constructor-injected RNG (already threaded into at least one System, `TestCombatBehaviorSystem`, so there's a live precedent for `AbilityActivationSystem`/`ConsumableActivationSystem` gaining the same dependency); and that `StatModifierComponent` is a `MultiComponentPool` whose own doc comment states an entity's modifiers "may target the same or different stats and stack freely" — which is exactly the primitive the "Double Tap" example below reuses with zero new engine machinery.

## Scoping decisions (explicit, so they can be vetoed)

- **`AbilityDefinition`/`AbilityCatalog`/`AbilityActivationSystem` keep their names.** Only their *effect/timing payload types* change. A full rename to "Spell*" everywhere would touch 30+ files for zero behavioral gain.
- **`AbilityInstanceComponent`'s per-instance damage override stays**, as an optional `DamageOverride` on `ActionEffectContext` — see `DamageEffectEntry` below. It stays a single flat `short` (not a range) for this pass: every current damage-dealing grant (Punch per race, Magic Missile) already sets an explicit override, so today's exact per-race balance is unchanged; a future per-race *range* override is a separate, deliberately deferred extension.
- **`PendingAbilityActivationComponent`/`PendingConsumableActivationComponent` and their two systems stay separate.** Mana-cost-check/spend and stock-consumption/`PotionCooldown`-abuse are genuinely different per-activator-kind mechanics — that's what "ActionActivators" are *for*. Only the actually-duplicated pieces (tile→target resolution, effect application, and now RNG-driven rolls) get shared.
- **No behavior changes to existing content beyond the refactor itself and what's explicitly listed above** — e.g. `ConsumableActivationSystem`'s existing "a potion target must have a `HealthComponent`" gate is preserved as-is; crit/variance is fully wired but won't visibly change Punch/Magic Missile's numbers today, since both are always instance-overridden.
- **DoT damage (Poison/Burning) does not roll variance/crit, by design, confirmed.** `PoisonSystem`/`BurningSystem` call `HealthDamage.Apply` directly on their own tick timers and were never routed through `AbilityEffectResolver`/`ConsumableActivationSystem` at all — so `DamageEffectEntry`'s variance/crit never applied to them before this refactor and doesn't start now. This matches common practice in shipped ARPGs (Diablo, PoE), which typically exclude or special-case DoT crit rather than rolling it every tick. If DoT crit is ever wanted, that's a deliberate, separate decision for `PoisonSystem`/`BurningSystem` to opt into `CritMath`/`StatModifierTarget.CritChance` themselves — not an implicit side effect of this plan.
- **Potion-cooldown-abuse stays exactly where it is today — `ConsumableActivationSystem`'s own kind-uniform logic, not a composable `IActionEffectEntry`.** It doesn't vary per potion (Constitution, the only varying input, is caster-side, not item-side), so every current potion already gets identical behavior automatically. Making it an entry that must be explicitly attached to every `PotionActivator.Effects` list (including mod-defined potions) would turn a currently-impossible-to-forget mechanic into a silently-omittable one, for zero expressive gain. It's the same category of thing `ManaCost`/mana-spend already is for spells — a per-activator-*kind* activation rule, not a per-item *effect* — which is exactly the line "ActionActivators" exist to draw. Consistent with this plan's own extraction discipline (`TargetResolution`/`ActionEffectSequence`/`AbilityScoreTagBonus` only became shared because two real call sites needed them): revisit only if a future Scroll activator needs the identical mechanic, and extract a shared helper *then*, not preemptively.
- **No new `IGameModule`.** The new `Game/Modules/Actions/` folder holds pure shared value types, no ECS components/systems of its own.

## Explicit rule: composition order is meaningful

Every ordered list in this design — `ActionEffect.Entries`, `IActionActivator.Effects`, `ChainedEffectEntry.TriggeredEffects` — applies **strictly in list order**, and later entries observe the live component state left behind by earlier ones. Concretely: a `StatModifierGrantEntry` targeting `OutgoingDamage`/`CritChance` listed *before* a `DamageEffectEntry` in the same `Entries` list *will* affect that very damage roll, in that same activation, because `DamageEffectEntry` reads current stat-modifier state at the moment it runs. This is deliberate and is the mechanism the "Double Tap" example below quietly relies on for self-buffs granted earlier in an activation — but it means whoever composes an entry list is responsible for ordering it correctly, the same way MTG's stack and WoW's aura-layering rules exist precisely because unordered/implicit resolution order is a recurring source of shipped-game bugs. `ActionEffect`'s and `ActionEffectSequence`'s doc comments state this rule explicitly; it is not left to be discovered by surprise.

## New shared types — `Game/Modules/Actions/` (new folder, no `IGameModule`)

- **`ActionEffectContext`** — one record carrying everything any entry might need to apply itself to one target, built once per activation (source-side fields fixed) and varied per target via `context with { TargetEntityId = id }`:
  ```csharp
  public sealed record ActionEffectContext(
      int SourceEntityId, int TargetEntityId,
      PackedComponentPool<HealthComponent> Health, EventBus EventBus, MathUtility MathUtility,
      string ActivatorName, IReadOnlyList<Tag> ActivatorTags,
      MultiComponentPool<StatModifierComponent>? StatModifiers = null,
      MultiComponentPool<AbilityScoreComponent>? AbilityScores = null,
      PackedComponentPool<ManaComponent>? Mana = null,
      PackedComponentPool<HotkeyExpansionUnlockComponent>? HotkeyExpansionUnlocks = null,
      StatusEffectAuraApplierRegistry? StatusEffectAppliers = null,
      ComponentManager? ComponentManager = null,
      PackedComponentPool<DeadComponent>? DeadEntities = null,
      PackedComponentPool<StatusEffectAuraSourceComponent>? AuraSources = null,
      IPlayerQuery? PlayerQuery = null,
      short? DamageOverride = null,
      int ChainDepth = 0);
  ```
  `MathUtility` is required (not optional) — unlike the feature-gated pools, it's a base Engine utility always available at composition time, the same way `Health`/`EventBus` already are. `ChainDepth` defaults to 0 and is only ever incremented by `ChainedEffectEntry` (see below). `AuraSources` is new this revision, for `AuraSourceToggleEntry` below.
- **`IActionEffectEntry`** — `void Apply(ActionEffectContext context);`
- **Entry records**, each owning its own logic:
  - **`DamageEffectEntry(short MinAmount, short MaxAmount)`** — internal order (itself an application of the "order is explicit" rule, one level down):
    1. `baseAmount = context.DamageOverride ?? (short)context.MathUtility.Next(MinAmount, MaxAmount + 1)` (inclusive of `MaxAmount`); no-op if `<= 0`.
    2. Add the ability-score tag bonus (`AbilityScoreTagBonus.Compute`, see below) — today's existing computation, relocated.
    3. Scale through the caster's `OutgoingDamage` via `StatModifierMath.GetEffectiveValue` — today's existing step, relocated.
    4. Roll a crit: `context.MathUtility.NextDouble() < StatModifierMath.GetEffectiveValue(context.StatModifiers, context.SourceEntityId, StatModifierTarget.CritChance, CritMath.BaseCritChance)`. On success, multiply the *fully-scaled* damage from step 3 by `StatModifierMath.GetEffectiveValue(context.StatModifiers, context.SourceEntityId, StatModifierTarget.CritMultiplier, CritMath.BaseCritMultiplier)` — crit is the last multiplier applied (matches Diablo/PoE's dominant convention: a crit amplifies the fully-modified number, not a pre-buff base).
    5. `HealthDamage.Apply(context.Health, context.EventBus, context.TargetEntityId, (short)result, StatusEffectSource.FromEntity(context.SourceEntityId), context.PlayerQuery, context.ActivatorName, context.StatModifiers)`.
  - **`HealEffectEntry(float Fraction)`**, **`ManaRestoreEffectEntry(float Fraction)`**, **`HotkeySlotGrantEntry(short Slots)`**, **`StatusEffectGrantEntry(StatusEffectType Type)`** — unchanged from the prior revision, each relocating today's equivalent inline/private-method logic verbatim.
  - **`StatModifierGrantEntry(StatModifierTarget Target, StatModifierOperation Operation, StatModifierPolarity Polarity, bool CanModify, float Magnitude, int DurationFrames, GrantRecipient Recipient = GrantRecipient.Target)`** — relocated/renamed from `Game/Modules/Abilities/StatModifierGrant.cs`, with one new field: `GrantRecipient` (`Target` default — unchanged behavior — or `Source`). `Recipient: Source` is what lets an entry buff the *caster* instead of whoever the action resolved against — the mechanism the "Double Tap" example below needs and today's design had no way to express (every existing grant always targeted the resolved target).
  - **`ChainedEffectEntry(float TriggerChance, IReadOnlyList<ActionEffect> TriggeredEffects)`** — new:
    ```csharp
    public sealed record ChainedEffectEntry(float TriggerChance, IReadOnlyList<ActionEffect> TriggeredEffects) : IActionEffectEntry
    {
        public const int MaxChainDepth = 5;

        public void Apply(ActionEffectContext context)
        {
            if (context.ChainDepth >= MaxChainDepth || context.MathUtility.NextDouble() >= TriggerChance)
            {
                return;
            }
            ActionEffectSequence.Apply(TriggeredEffects, context with { ChainDepth = context.ChainDepth + 1 });
        }
    }
    ```
    `TriggeredEffects` is a *list* (per point 6 above) so one successful trigger can fire more than one `ActionEffect`, in order. `MaxChainDepth` guards the same failure mode WoW/PoE explicitly design around: a proc that (directly or via a longer cycle) triggers itself. Since a `ChainedEffectEntry` can itself appear inside a `TriggeredEffects` entry's own `ActionEffect`, arbitrary-depth chaining falls out for free from ordinary composition — the depth guard is the only extra safety needed, and it's cheap (an int compare) precisely because activation isn't a hot per-frame path.
  - **`AuraSourceToggleEntry(StatusEffectType Type, int AuraAndGlowStrength, Color GlowColor)`** — new, unblocks TODO.md's "Toggle poison aura ability" (Low Priority, Game section: "a FreeCast-style ability that turns an existing Poison/StatusEffectAura source on/off around the caster"). The component it toggles, `StatusEffectAuraSourceComponent` (`Game/Modules/StatusEffectAura/Components/StatusEffectAuraSourceComponent.cs`), already exists and is already fully wired into a working system — Lava uses it today (`Game/Blueprints/Terrain/Lava.cs`) via `componentManager.Merge(entityId, new StatusEffectAuraSourceComponent(...))`; only the ability to add/remove it on a *creature* was missing. Confirmed a `PackedComponentPool<StatusEffectAuraSourceComponent>` (at most one aura source per entity), so toggling is a simple presence check:
    ```csharp
    public sealed record AuraSourceToggleEntry(StatusEffectType Type, int AuraAndGlowStrength, Color GlowColor) : IActionEffectEntry
    {
        /// <summary>Always toggles the SOURCE entity (the caster), never the resolved target -- an aura radiates from whoever cast it, not from whoever/whatever it happened to resolve against. Only well-behaved on a Self-targeted (single-resolution) activator; a multi-target activator would call Apply once per resolved target and toggle on/off/on/off within one activation, almost certainly not intended -- the ability author's responsibility, per the "composition order is meaningful" rule above.</summary>
        public void Apply(ActionEffectContext context)
        {
            if (context.AuraSources is null) return;
            if (context.AuraSources.Has(context.SourceEntityId))
            {
                context.AuraSources.Remove(context.SourceEntityId);
            }
            else
            {
                context.AuraSources.Merge(context.SourceEntityId, new StatusEffectAuraSourceComponent(Type, AuraAndGlowStrength, GlowColor));
            }
        }
    }
    ```
    Everything downstream of the toggle (the actual radiating/re-granting to nearby entities) stays entirely inside the existing `AuraGrid`/`StatusEffectAuraSystem` — this entry is only the on/off switch feeding it, not a reimplementation of any aura behavior. Building the actual "Toggle Poison Aura" ability content (a `CoreAbilitiesModule` registration with `TargetShape.Self`, `ActionTimingCategory.FreeCast`) stays out of scope for this plan, same as it's out of scope for TODO.md today — this entry just removes the one missing piece blocking it.

  Adding a future effect kind (teleport, summon, ...) means adding one more record here — nothing else changes.
- **`ActionEffect(IReadOnlyList<IActionEffectEntry> Entries)`** — owns its own application loop directly, in list order:
  ```csharp
  public sealed record ActionEffect(IReadOnlyList<IActionEffectEntry> Entries)
  {
      public static readonly ActionEffect None = new([]);
      /// <summary>Applies Entries in list order -- later entries observe state earlier ones left behind. See the "composition order is meaningful" design note.</summary>
      public void Apply(ActionEffectContext context)
      {
          foreach (var entry in Entries) entry.Apply(context);
      }
  }
  ```
  There is deliberately no separate `ActionEffectResolver` class — `ActionEffect.Apply` *is* the resolver, and it contains no per-kind knowledge at all.
- **`ActionEffectSequence.Apply(IReadOnlyList<ActionEffect> effects, ActionEffectContext context)`** — new tiny static helper: `foreach (var effect in effects) effect.Apply(context);`, in list order. Shared by both `IActionActivator`-orchestration code (an activator now triggers a *list* of `ActionEffect`s, point 6 above) and `ChainedEffectEntry.Apply` — the two "trigger multiple ActionEffects" call sites the user asked for share the exact same shape and the exact same helper rather than each re-implementing the loop.
- **`TargetResolution.EnumerateTargets(Vector3Int tile, IMapQuery mapQuery)`** — unchanged from prior revision.
- **`ActionTiming`/`ActionTimingCategory`** — unchanged from prior revision, relocated verbatim from Abilities.
- **`IActionActivator`** — `Guid Id`, `string Name`, `string Glyph`, `TargetingSpec Targeting`, `ActionTiming Timing`, **`IReadOnlyList<ActionEffect> Effects`** (was singular `Effect` in the prior revision — now a list, applied via `ActionEffectSequence.Apply`, per point 6), `string Description`, `string Summary`, `string? SpriteName`, `Color GlyphColor`, `IReadOnlyList<Tag> Tags`.

**`AbilityScoreTagBonus`** — unchanged from prior revision: new small static helper in `Game/Modules/AbilityScores/`, relocated from `AbilityEffectResolver`'s private `ComputeAbilityScoreBonus`/`MapTagToAbilityScore`.

**`CritMath`** — new small static class (e.g. `Game/Modules/Actions/CritMath.cs`, mirroring `PotionCooldownEffects`' constant-holding style): `BaseCritChance`/`BaseCritMultiplier` constants, the global fallback used when no `StatModifierComponent` targets `CritChance`/`CritMultiplier` for the caster. Design intent, stated explicitly in its doc comment rather than left implicit in a bare number: crits should be **rarer but hit harder** than the ~15-25%-chance/~1.5-2x-multiplier norm in games like Diablo/PoE — a low base chance paired with a noticeably larger base multiplier. Exact tuning is a balance pass, not an architecture decision; the doc comment records the *intent* so whoever tunes it later doesn't accidentally regress toward the generic-RPG norm.

**Two new `StatModifierTarget` members** (`Game/Modules/StatModifiers/StatModifierTarget.cs`) — `CritChance`, `CritMultiplier`, added the same way every existing member was: "new stats add new members here as something needs to modify them" (the enum's own doc comment). This is what lets equipment, buffs, and the "Double Tap" example below modify crit the same generic way anything already modifies `OutgoingDamage`.

**`MathUtility` gains one passthrough method** (`Engine/Math/MathUtility.cs`): `public double NextDouble() => _randomizer.NextDouble();` — same "passthrough to the wrapped Random instance" rationale its existing `Next(int, int)` already documents, needed for probability rolls (`CritChance`, `TriggerChance`) that `Next(int,int)` can't express directly.

### Worked example: "Double Tap" (sequential-attack crit-chance stacking), as ordinary content, not new engine machinery

An attack ability's `Effects` includes an `ActionEffect` whose `Entries` are, in this order:
```
[ DamageEffectEntry(...),
  StatModifierGrantEntry(Target: CritChance, Operation: Add, Polarity: Positive,
                          CanModify: true, Magnitude: 0.05f /* tuning TBD */,
                          DurationFrames: shortWindow, Recipient: GrantRecipient.Source) ]
```
Each attack lands its damage, then grants the *attacker* (`Recipient: Source`) a short-lived, stacking `+CritChance` modifier. Because `StatModifierComponent` is a `MultiComponentPool` whose own doc comment already states modifiers "stack freely" with independent expiry, consecutive attacks within the window keep adding stacks — crit chance climbs the longer the sequence continues — and the bonus decays on its own via the existing `StatModifierExpirySystem` the moment the attacker stops attacking. No new state-tracking component, no "consecutive attack counter" field anywhere — this is 100% a consequence of `Recipient: Source` existing and the ordering rule above (the grant from *this* attack doesn't affect *this* attack's own already-applied damage, since `DamageEffectEntry` runs first in the list — only the *next* attack sees the stack). Genuinely free from already-existing infrastructure once `GrantRecipient` exists.

## Changes to `Game/Modules/Abilities/`

- Delete `AbilityEffect.cs`, `StatModifierGrant.cs`, `AbilityTiming.cs`, `ActionTimingCategory.cs` (superseded/relocated above).
- `AbilityDefinition.cs`: `Effect: AbilityEffect` → `Effects: IReadOnlyList<ActionEffect>`; `Timing: AbilityTiming` → `ActionTiming`; `: IActionActivator`.
- `AbilityEffectResolver.cs`: shrinks to pure per-activation orchestration (unchanged in spirit from the prior revision, now also gains a `MathUtility` constructor dependency and loops `Effects` instead of a single `Effect`): publish `AbilityActivatedEvent`, build the source-fixed half of `ActionEffectContext` (`DamageOverride: instance.DamageAmount`, `ActivatorTags: ability.Tags`, `MathUtility: mathUtility`), walk target tiles via `TargetResolution.EnumerateTargets`, call `ActionEffectSequence.Apply(ability.Effects, context with { TargetEntityId = targetId })` per target.
- `AbilityActivationSystem.cs`/`DelayedActionSystem.cs`: constructors gain a `MathUtility mathUtility` param (mirrors the existing `TestCombatBehaviorSystem` precedent) and an optional `PackedComponentPool<StatusEffectAuraSourceComponent>? auraSources = null` param, both threaded down to `AbilityEffectResolver`/`ActionEffectContext`. Composition root (`GameLoop.cs`) already constructs one shared `MathUtility` for the game — reuse it, don't construct a second one. Since both systems call the same `AbilityEffectResolver`, wiring `auraSources` once there covers both — this is what makes a future FreeCast "Toggle Poison Aura" ability work without touching either system again.
- `CoreAbilitiesModule.cs`: e.g. Heal's `new AbilityEffect(DamageAmount: 0, StatusEffects: [], HealFraction: 0.2f)` → `Effects: [new ActionEffect([new HealEffectEntry(0.2f)])]`; Punch's `new AbilityEffect(DamageAmount: 10, StatusEffects: [])` → `Effects: [new ActionEffect([new DamageEffectEntry(MinAmount: 10, MaxAmount: 10)])]` (zero-variance range — matches today exactly, and is always instance-overridden anyway per the scoping decision above).

## Changes to `Game/Modules/Inventory/`

- Delete `ConsumableEffect.cs`, `ConsumableKind.cs`.
- New `PotionActivator.cs`: `sealed record PotionActivator(Guid Id, string Name, string Glyph, TargetingSpec Targeting, ActionTiming Timing, IReadOnlyList<ActionEffect> Effects, string Description = "", string Summary = "", string? SpriteName = null, Color GlyphColor = default, IReadOnlyList<Tag> Tags = null!) : IActionActivator`. Retires `ConsumableKind`.
- `ItemDefinition.cs`: `Consumable: ConsumableEffect?` → `Activator: IActionActivator?`.
- `CoreItemsModule.cs`: rebuild the three potion registrations as `PotionActivator`, e.g. Health Potion's old `Consumable: new ConsumableEffect(...)` → `Activator: new PotionActivator(HealthPotionId, "Health Potion", "h", t, new ActionTiming(ActionTimingCategory.Immediate, f, null), [new ActionEffect([new HealEffectEntry(0.5f)])])`.
- `Systems/ConsumableActivationSystem.cs`: `switch (consumable.Kind) { case ConsumableKind.Potion: ... }` → `if (item.Activator is PotionActivator potionActivator) { ActivatePotion(potionActivator, ...); }`. `ActivatePotion`'s tile loop uses `TargetResolution.EnumerateTargets`. `ApplyPotionToTarget`'s inline blocks collapse into building an `ActionEffectContext` (`DamageOverride: null` always) and calling `ActionEffectSequence.Apply(potionActivator.Effects, context)` — health-required-target gate, dead-check, cooldown-abuse Poison stack, and `PotionCooldownEffects.Reset` bookkeeping around it are untouched. Constructor gains `MathUtility mathUtility` (required, same composition-root instance), plus the previously-planned optional `StatusEffectAuraApplierRegistry?`/`IPlayerQuery?` params — together these are what let a future damaging/status-inflicting/chained-effect consumable (an explosive, a poison flask, a "grenade that sometimes also ignites") work without touching this system again, only its own item registration.

## Presentation touches (mechanical — no behavior change)

Unchanged from the prior revision: `HotbarContent.cs` (`item.Consumable` → `item.Activator`, `{ Kind: ConsumableKind.Potion }` → `is PotionActivator`), `ActionTargetingController.cs` (`consumable.Targeting` → `activator.Targeting`). Neither reads `.Effect`/`.Effects`, so the singular→plural change doesn't touch Presentation at all.

## Test updates (pattern repeats; representative files, not exhaustive)

Same files as the prior revision (`ConsumableActivationSystemTests.cs`, ability/inventory/Presentation tests), plus new focused coverage this revision specifically needs:
- `DamageEffectEntry`: variance range (seeded `MathUtility`, assert result lands in `[MinAmount, MaxAmount]`), `DamageOverride` bypassing the roll, crit trigger/no-trigger via seeded RNG, crit-multiplier applied *after* `OutgoingDamage` scaling (order-of-operations assertion).
- `StatModifierGrantEntry`: `Recipient: Source` lands on the caster, not the resolved target (regression-guards the "Double Tap" mechanism specifically).
- `ChainedEffectEntry`: triggers/doesn't trigger per seeded roll, applies *all* listed `TriggeredEffects` in order on success, and a `MaxChainDepth`-exceeding self-referential chain terminates instead of recursing forever (construct a `ChainedEffectEntry` whose own `TriggeredEffects` eventually loops back to itself, assert it returns rather than stack-overflows).
- `AuraSourceToggleEntry`: absent → present on first `Apply` (on `SourceEntityId`, not `TargetEntityId`), present → absent on the second; a no-op when `AuraSources` isn't wired.
- `ActionEffectSequence`/`IActionActivator.Effects`: an activator with more than one `ActionEffect` applies both, in order.

## Execution phases (stop for manual in-game testing after each, per established working style)

1. **Add `Game/Modules/Actions/`** (all types above, including `CritMath`, the `StatModifierTarget` additions, and the `MathUtility.NextDouble()` passthrough) — purely additive. Verify: `dotnet build`, new unit tests (including the crit/variance/chain-depth cases above) pass.
2. **Migrate Abilities module** onto the new types, including threading `MathUtility` into `AbilityActivationSystem`/`DelayedActionSystem`. Verify: `dotnet build`, `dotnet test`, then manually play Heal/Punch/Magic Missile in-game (damage still scales correctly per-race, occasional crits are visible/sensible once wired to feedback — note: floating combat text itself is still blocked on the separate, already-tracked "User feedback for actions is missing entirely" TODO item, so a crit today is only confirmable via logs/inspector, not a visual cue).
3. **Migrate Inventory module + Presentation**, including threading `MathUtility` into `ConsumableActivationSystem`. Verify: `dotnet build`, `dotnet test`, then manually drink/throw the Health, Mana, and Hotkey Expansion potions in-game.
4. **Cleanup**: grep for leftover references to deleted types; update `TODO.md` — remove the "ConsumableEffect effect shape doesn't scale" item, rewrite the "Scrolls" item's framing now that its foundational split has landed, and append the modded-content note below to the existing "Data storage" item under Global.

### TODO.md addendum (Phase 4) — append to the existing "Data storage, starting with window locations and sizes" item under Global

> **Modded content must degrade gracefully, not corrupt a save.** Once entity/world save state (inventory items, granted abilities, and `IActionActivator`/`ActionEffect`-bearing catalog entries — see `PLAN-action-effect-activator.md`) starts getting serialized, a saved reference (by `Guid`) to a mod-defined item/ability/effect can go stale if that mod is updated, disabled, or removed before the save is loaded again — a well-known failure mode in every moddable game with real save compatibility (RimWorld, Path of Exile). Worth a fail hierarchy decided up front rather than crashing or silently corrupting state on a missing id: (1) prefer a mod-supplied replacement/migration for a renamed or updated id, (2) fall back to dropping just the affected reference (the one item stack, granted ability, or effect entry) while the rest of the save loads normally, (3) as a last resort, when the missing content is load-bearing for the entity itself, drop the whole entity. Consider letting a mod register its own fallback id (a vanilla or generic substitute) per content id it defines, so an update or removal degrades a save gracefully instead of just vanishing content outright.
