# Heal Over Time and Minor Restoration

(From TODO.md "Defensive buff spell -- damage reduction + healing over time". Investigated 2026-11-07, decisions settled the same
day. Three phases.)

## Verdict in one paragraph

A heal over time is a new status effect, **HealOverTime** ("Heal Over Time"), shaped like Poison: each stack heals 1 per tick, for a
duration the grant sets. Building it exposes four places where the status-effect layer silently assumes that every effect is a debuff
shaped like Poison or Burning. **A status effect has no polarity**: its duration scales by the debuff pair, and the Health window always
lists it under Debuffs. **A grant can't say how long an effect lasts**: Poison's duration is fixed at registration
(`PoisonModule.AuraDurationTicks = 1`), so Toxic Strike's 10 Poison stacks deal 10 once and end. **Poison's timed-stack bookkeeping is
private to Poison**. **The UI picks effects out by name** (`is Poison or Burning`). Phase 1 fixes these four, makes every heal do nothing on a corpse,
and gives Toxic Strike and Toxic Flask a 6 s poison. Phase 2 adds Heal Over Time. Phase 3 is the spell **Minor Restoration** (10 healing a second
for 20 s), as content only. The TODO's damage-reduction half is already supported and tested, and is out of scope.

## Decisions (2026-11-07)

- **Name:** `StatusEffectType.HealOverTime`, shown as "Heal Over Time". This is the industry-standard term over a flavour name.
- **Shape:** Poison's. A stack is 1 health per tick, and the duration is separate and set by the grant.
- **Death ends nothing.** A heal over time, like every status effect, keeps running on a corpse, so a resurrection resumes whatever is
  left. Its ticks heal nothing on a corpse; they are wasted, never ended early. Ending buffs at death would be a rule debuffs don't have.
- **Toxic Strike and Toxic Flask:** a 6 s poison duration each (`"d":6`).
- **Storage:** one Packed component per effect, with a stack count, not a merged pool (see "Storage" below).
- **Minor Restoration:** 10 healing a second for 20 seconds.
- **Floating text:** the same "+N" style as a heal (`FloatingTextKind.Healed`'s look), in a lighter green.
- **Balance rule:** an effect over time is clearly weaker per tick than an immediate one, and stronger over its full duration.

## How it works today

- `StatusEffectGrant(Type, StackCount, Mode, BodyPart)` (`Game/Effects/Entries/StatusEffectGrant.cs`) skips a dead target, checks immunity, scales
  stacks (Magnitude, Outgoing/IncomingStatusStacks), applies TopUpTo, and calls `IStatusEffectApplier.ApplyStacks`. It carries no duration.
- **Burning**: the stacks are both the damage per tick and the duration, decaying by one per tick. It is held on the entity or per body part,
  with its own applier, display and two systems.
- **Poison**: the stacks are the damage per tick, and `RemainingDurationTicks` is independent of them. A reapplication sets the duration to
  `Max(existing, new)`. The duration is fixed at registration (`AuraDurationTicks = 1`, chosen for the aura's TopUpTo refresh), and
  `PoisonEffects.ScaleDebuffDuration` scales it unconditionally (no activator tags, no scroll `DurationScaleMultiplier`). Every other
  duration goes through `StatModifierGrant.ScaleDurationFrames`, by polarity.
- **Paralysis**: one stack, with a fixed duration in `ParalysisEffects`.
- `TimerBasedStatusEffectApplier<T>`/`TimerBasedStatusEffectDisplay<T>` are generic over the pool. `ApplyStacks`, `AddStacksUpdate`, the timer
  component and the system's countdown are written out separately for each effect.
- Ticks: `PoisonSystem`/`BurningSystem` are `PackedTimerWheel` `ISystem`s calling `HealthDamage.Apply`. They keep ticking on a corpse.
- Healing: `HealthHeal.Apply` is the chokepoint (Outgoing then IncomingHealing, Simple/Complex dispatch). `BodyPartTargetMode.All` splits one
  total across the parts and ignores the regen lockout. **It does not check `DeadComponent`**: the regen systems skip the dead themselves,
  but a `DirectHeal` on a corpse heals it today.
- UI: `HealthWindow` puts every status effect under Debuffs and colours it from a per-type switch. `PlayerStatusEffectsContent` shows stacks only for
  `Poison or Burning`. `FloatingTextPalette` has a per-type switch. The colour switches are deliberate (each consumer has its own contrast).

### Why not a timed `StatModifierGrant(HealthRegen, ...)`?

Regeneration on a Complex entity heals one part a visit, and skips every part inside its 10 s regen lockout. That is the part that was
just hit, so a heal cast mid-fight would land almost nowhere useful. A heal over time is an active heal: all parts, no lockout.

## What becomes generic (and what doesn't)

| Piece | Today | After |
|---|---|---|
| Polarity | none (every effect is a debuff) | `StatusEffectTypeFacts.PolarityOf(type)` (`StatModifierPolarity`, reused): one table every reader shares |
| Duration | Poison's fixed at registration; Burning has none | `StatusEffectGrant.DurationTicks` (`ushort?`, key `"d"`), scaled in the grant by polarity, passed to `ApplyStacks`; the applier declares `TakesDuration` |
| Duration scaling | `PoisonEffects.ScaleDebuffDuration`, unconditional | `StatModifierGrant.ScaleDurationFrames` moved to a shared `EffectDurations.Scale(in context, value, polarity)`, for all three grants |
| Timed-stack bookkeeping | written out in Poison | `TimedStatusEffectStacks` helpers over an `ITimedStatusEffectTimer` shape: Poison and HealOverTime |
| Remaining-frames formula | a lambda in PoisonModule and two tests | `TimedStatusEffectStacks.RemainingFrames` |
| A heal on a corpse | heals it (`DirectHeal`) | does nothing: `HealthHeal.Apply` checks death, as the chokepoint |
| UI stack display | `is Poison or Burning` | `display.MaxStacks > 1` |
| Health window column | always Debuffs | by `StatusEffectTypeFacts.PolarityOf` |
| Tick label | `StatusEffectDamageType.Describe` | `StatusEffectLabel.Describe`, used by heals too, reading `StatusEffectTypeFacts.DisplayNameOf` |

**Burning stays as it is** (a decaying burn and per-part storage; forcing the timed-stack shape would change how fire plays), and only gains
`TakesDuration => false`. **Paralysis** gains the same and keeps its fixed duration.

## Phase 1 -- generic status-effect pieces, corpse heals, Toxic Strike and Flask (built 2026-11-07)

Built as below, except where a step says "As built". Every game test passes (4120). Workshop.Tests weren't run, because the running game locked GameShell's output; the Workshop Builders project compiles.

1. **Polarity.** As built: `StatusEffectTypeFacts.PolarityOf(type)` (`Modules/StatusEffects`), a single table rather than a member on each applier and display,
   so the grant, its `AmountModifiers` (read by the inspector, which has no registry) and the Health window can't disagree. Burning, Poison and Paralysis are `Debuff`.
2. **Shared duration scaling.** Move `StatModifierGrant.ScaleDurationFrames` to `Game.Effects.EffectDurations.Scale`, and call it from `StatModifierGrant`,
   `StatusEffectImmunityGrant` and `StatusEffectGrant`. The body is unchanged. It works in ticks as well as frames, because it only multiplies.
3. **Duration on the grant.** `StatusEffectGrant.DurationTicks` (`ushort?`, codec key `"d"`, omitted when null, so no existing line changes). The grant
   scales it with `EffectDurations.Scale(..., polarity)`, and passes it to `IStatusEffectApplier.ApplyStacks(..., ushort? durationTicks = null)` and the
   `StatusEffectStackApplication` delegate. Magnitude never scales it (durations aren't amounts). `AmountModifiers` is per instance: the stacks pair, plus
   the duration pair for the effect's polarity when `DurationTicks` is set. The polarity comes from `StatusEffectTypeFacts.PolarityOf`.
4. **`IStatusEffectApplier.TakesDuration`.** `StatusEffectContentValidation` (beside `ContentTagValidation`, through `EffectContent.ForEachEntry`) fails a
   build where a grant names `DurationTicks` for an effect that takes none.
5. **Poison on the grant's duration.** `PoisonEffects.ApplyStacks` takes an already-scaled `durationTicks`. When the grant names none, it uses
   `PoisonEffects.DefaultDurationTicks` (1), unscaled, so the aura's refresh behaves as today. Delete `ScaleDebuffDuration`.
6. **Timed-stack helpers.** `ITimedStatusEffectTimer : IScheduledTimer, IStatusEffectStackCount`, with settable `StackCount`/`RemainingDurationTicks`
   and `static abstract T Create(uint nextTickFrame, byte stacks, ushort durationTicks, ActionSource source)`. It has three helpers:
   - `TimedStatusEffectStacks.Apply<T>(pool, entityId, count, maxStacks, durationTicks, source, now, tickIntervalFrames)` (the cap, the add-or-refresh `Max` duration, the staggered first tick).
   - `AfterTick<T>(pool, entityId, timer, tickIntervalFrames)` (the countdown, which returns the wheel's remove flag).
   - `RemainingFrames(timer, now, tickIntervalFrames)`.

   As built: the interface is `ITimedStatusEffectTimer<TSelf>`, and the countdown is `IsSpent` plus `CountDownAfterTick`. Poison moves onto them, and its `ApplyStacks`
   and `PoisonSystem.Tick` shrink to the immunity check, the damage call and these helpers. `PoisonEffects.ApplyStack` (one stack) is gone.
   Its callers now call `ApplyStacks` once: the test poison in `FloorBuilder.CreatePlayer`. The potion-abuse poison in `ItemActivationSystem` is a
   `StatusEffectGrant` with its duration. It scales the same way as every other poison, and also shows "+1" and publishes `StatusEffectAppliedEvent`, which the direct call didn't.
7. **A heal on a corpse does nothing.** `HealthHeal.Apply` takes the `DeadComponent` pool and returns false for a dead entity before reading anything,
   for every heal: `DirectHeal`, regeneration, and Heal Over Time's ticks. As built: the regen systems keep their own dead check,
   because it skips computing the per-second rate.
8. **UI by polarity, not by name.** `HealthWindow` builds status-effect rows in their polarity's column (as built, first in either column: Buffs, then the buff modifiers and immunities; Debuffs, then the potion cooldown and the debuff modifiers).
   `PlayerStatusEffectsContent` shows a stack count when `display.MaxStacks > 1` (add `MaxStacks` to `IStatusEffectDisplay`).
9. **Labels.** Rename `StatusEffectDamageType` to `StatusEffectLabel`. `Describe` reads `StatusEffectTypeFacts.DisplayNameOf` (as built:
   the same table as polarity, so a system with no registry can name the effect). The Health window's rows read it too.
10. **Toxic Strike and Toxic Flask:** `"d":6` on each one's Poison grant.
    - Toxic Strike: 10 stacks tick 10 a second for 6 s (60 total), against Magic Missile's 20-25 at once.
    - Toxic Flask: 5 stacks tick 5 a second for 6 s (30 total), against Volatile Concoction's 20-30 at once.

    Both are now weaker per tick and stronger in total. The Flask's total is close: it is only a little above the Concoction's best roll.
    Their descriptions already read correctly.

Tests:
- Poison's duration-scaling cases move to `StatusEffectGrant` (scaled once, tag-conditional, with the scroll multiplier).
- A grant's `DurationTicks` reaches Poison, and the aura refresh's default 1 is unchanged.
- A duration on Burning fails the content check.
- `HealthHeal.Apply` on a corpse returns false and changes nothing (Simple and Complex).
- HealthWindow row placement by polarity. As built, no Buff effect exists yet, so the test checks that the Buff rows leave out every debuff. Phase 2 adds a Buff case.

**In-game check:**
- Toxic Strike a goblin: 10 poison damage a second for 6 seconds, then it ends.
- The Toxic Aura and lava behave as before.
- Poison and Burning are still under Debuffs.
- Heal on a corpse (Admin Mode, a potion thrown on it) does nothing.

## Phase 2 -- Heal Over Time

- `StatusEffectType.HealOverTime`, appended, displayed as "Heal Over Time".
- `Modules/HealOverTime`: `HealOverTimeModule` (Requires StatusEffects, Health, StatModifiers, Death). `HealOverTimeEffects` holds the glyph,
  `MaxStacks` (255, as Poison), `TickIntervalFrames` (1 s) and `DefaultDurationTicks` (1, for a grant naming none: an aura topping it up works as
  Poison's does). `HealOverTimeTimerComponent : ITimedStatusEffectTimer` is a Packed pool, and `HealOverTimeSystem` is a `PackedTimerWheel` `ISystem`.
- Configure registers a `TimerBasedStatusEffectApplier<HealOverTimeTimerComponent>` (`TakesDuration`) and a display (`RemainingFrames`).
  `StatusEffectTypeFacts` gets the `HealOverTime` cases: Buff, "Heal Over Time".
- **Tick:** `HealthHeal.Apply(flatAmount: StackCount, sourceEntityId: the source's live entity (EntityKeys; null once gone), targetMode: All,
  healCategory: ResourceGainCategory.StatusEffect (new), healType: StatusEffectLabel.Describe(HealOverTime))`, then `AfterTick`. It runs the same
  on a corpse or at full health: `HealthHeal.Apply` does nothing and the countdown goes on. **Nothing ends it at death**, so a resurrected entity
  resumes what remains. `All` ignores the regen lockout and resets nothing.
- Amounts: the stacks follow Magnitude and Outgoing/IncomingStatusStacks at the grant. Each tick's heal follows Outgoing/IncomingHealing live
  at the tick (the source's while it exists). There is no ability-score bonus.
- **Floating text:** a new `FloatingTextKind.HealedOverTime`, reached from `ResourceGainCategory.StatusEffect` in `HealthHeal.PublishHealed`. It is drawn as
  `Healed` is, in a lighter green (`FloatingTextPalette`). The HUD, `HealthWindow.GetColor` and `PlayerStatusEffectsContent` each get a colour case.
- `BuiltInModules()` adds it. `BuiltInModulesTests` pins its system after `PoisonSystem`.

Tests:
- Each tick heals the stack count, for the duration, and then ends.
- A reapplication adds stacks up to the cap and refreshes to the longer duration.
- On a corpse, ticks heal nothing but count down; the timer is still there with the right remainder, so a resurrection would resume it.
- On Complex health it splits across parts, and heals a locked-out part.
- Incoming/OutgoingHealing apply at the tick.
- An immunity refuses it, and `StatusEffectRemoval` ends it.
- Buff polarity scales the duration by the Buff pair.

**In-game check:**
- Admin "Apply >" or a test grant gives Heal Over Time.
- The HUD icon shows stacks and seconds.
- Buffs lists "Heal Over Time" in green.
- A light-green "+N" appears each second while you're damaged, and it ends on time.
- Kill the holder mid-heal and inspect the corpse: the effect is still listed, and its seconds keep counting down.

## Phase 3 -- Minor Restoration (content)

- An `Actions.jsonl` line, `BuiltInActionIds.MinorRestoration`, and `PlayerKit` (`UsesActions`, a grant, a hotbar slot).
- The line: Spell, `Effect.Healing` + `Targeting.Self`, Self at the Caster, Immediate, with one effect: `StatusEffectGrant` `HealOverTime`, `c` 10, `d` 20.
  The sprite is `Spell-Weak`, as Heal's.
- Cost and cooldown aren't decided yet. As a placeholder, use a `ManaDrain` of 2 (Heal's) and no cooldown; Heal has none either.
- Description, for example: "A gentle restorative that knits the caster's wounds over twenty seconds, ten health every second." Summary: "Heals 10 per second for 20 seconds."

Balance against the rule: the player (Human) has 255 maximum health. Heal restores 20% at once (51), and Minor Restoration restores 10 a tick and 200
over its duration.

**In-game check:**
- Take damage, then cast it. You heal 10 a second for 20 seconds, the text is a light-green "+10", and the HUD counts 20 seconds down.
- Recast during it: the stacks go to 20 (20 a second), and the duration goes back to 20.
- At full health, nothing is shown, but the timer still runs.
- A scroll's duration scaling lengthens it.

## Storage: one Packed component per effect (decided 2026-11-07)

Poison already holds one `PoisonTimerComponent` per entity in a Packed pool, with `StackCount` on it. A tick does damage equal to the stacks: one wheel
firing and one write, however many stacks there are. Heal Over Time copies that shape in its own Packed pool. The alternative is to merge both into a single
pool with a type field. An entity can hold both at once, so that pool would have to be a Multi pool keyed by type, and that is slower on every path:
- **Lookups:** a grant, the display and the HUD each walk the entity's chain, instead of making one paged array read.
- **Ticks:** every tick branches between damage and heal (`HealthDamage` vs `HealthHeal`, two different modules' APIs).
- **Memory:** pools page by id, so a merged pool pays for both effects' pages.

The code they share comes from the `TimedStatusEffectStacks` helpers instead (Phase 1, step 6). Each effect keeps its own pool, system and
`StatusEffectType`.

## Noted, not in this plan

- Paralysis could take the grant's duration later (`TakesDuration => true`), once something wants a paralysis of varying length.
