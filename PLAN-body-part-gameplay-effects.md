# Body part gameplay effects: Legs affect movement, Arms affect melee damage

**Landed.** All decisions below were confirmed by the user and implemented as described --
`Game/Modules/BodyPartEffects/` (`BodyPartEffectsSystem`, `MovementDisabledComponent`,
`MeleeDisabledComponent`), the two new `StatModifierTarget` values, `BodyPartType.Wing`, and the
`MovementSystem`/`DirectDamage`/`ActionActivationSystem` consumer changes. See `TODO.md`'s
"BodyPartType categorization and gameplay effects" and "Limb-specific gameplay penalties beyond
disable" entries for the landed summary, and the new "Melee actions should declare which body
parts perform them" entry for the one deliberately-deferred follow-up.

(Design plan, saved for implementation per this repo's convention -- see `PLAN-body-parts.md` for
the Complex-health machinery this builds on. Combines items 2 and 4 of the current body-parts
follow-up work, per `TODO.md`'s "BodyPartType categorization and gameplay effects" and
"Limb-specific gameplay penalties beyond disable" entries -- the user explicitly asked for this to
be planned together and carefully, plus asked directly: which system should own the connection
between body parts and gameplay actions.)

## The core question, answered

**A new, dedicated system should own this -- not `MovementSystem`, not `DirectDamage`/
`ActionEffectResolver`.** Neither of those should learn anything about `BodyPartComponent` at all.
Instead, a new system watches body-part condition and translates it into ordinary
`StatModifierComponent` grants against *existing* stat targets (or two new ones, see below) --
`MovementSystem` and `DirectDamage` just keep consuming `StatModifierMath.GetEffectiveValue` the
exact same generic way they already do for every other buff/debuff in the game. This keeps
body-part-awareness contained to one new place, and everything downstream stays exactly as
ignorant of body parts as it is today.

Concretely: new `Game/Modules/BodyPartEffects/` module, `BodyPartEffectsSystem` -- a striped,
polling system (mirrors `ComplexHealthRegenSystem`'s own shape) that, per due entity, computes the
*current* desired penalty from that entity's own body parts and keeps a small set of
system-owned `StatModifierComponent` grants in sync with it (granted/updated/removed as the
underlying body part condition changes). Nothing else needs to poll or react to body-part health at
all.

## Investigated: movement "speed" and melee damage have no existing per-source scoping

**Movement speed doesn't exist as a stat today.** `MovementSystem.TryMoveToNextMapPosition` reads
`ActionLockComponent.StandardLockFrames` *directly*, with no `StatModifierMath` layer at all -- the
same lock movement and every action already share (attacking sets it too, per `TODO.md`'s own
Goblins entry). There's no seam to slow movement specifically without also slowing every other
locked action, unless a new stat target is added and `MovementSystem` starts consuming it.

**Melee damage isn't a distinguishable slice of `OutgoingDamage` today.** `DirectDamage.Apply`
scales *every* damage source -- Punch and Magic Missile alike -- through the same
`StatModifierTarget.OutgoingDamage` uniformly. A modifier reducing `OutgoingDamage` because of Arm
damage would also weaken spells, which doesn't match "reduce melee attack damage" specifically.
`Tag.Melee` already exists and Punch already carries it (`Tags: [Tag.Melee, Tag.Unarmed, Tag.Attack,
Tag.Strength]`) -- confirmed as the right scoping signal to use.

## Design

### Two new `StatModifierTarget` values

- **`MovementLockFrames`** -- consumed by `MovementSystem.TryMoveToNextMapPosition` (currently reads
  `standardLockFrames` raw; changes to run it through `StatModifierMath.GetEffectiveValue` first,
  the same one-line addition every other stat consumer already has). A Leg/Foot penalty grants a
  *multiplicative* debuff here (longer lock = slower), the same operation shape
  `PotionCooldownEffects`'s own Constitution-scaled duration already uses for "bigger number =
  worse," just via a stored modifier instead of a live formula.
- **`MeleeOutgoingDamage`** -- consumed by `DirectDamage.Apply` *in addition to* (not instead of)
  today's unconditional `OutgoingDamage` step, applied only when `context.ActivatorTags.Contains(Tag.Melee)`.
  An Arm/Hand penalty grants a multiplicative debuff here, leaving spells (no `Tag.Melee`) untouched.

Both stay ordinary `StatModifierTarget` members, so nothing about `StatModifierMath`/
`StatModifierExpirySystem`/equipment's own future use of the same seam needs to change -- body
parts are just one more source of modifiers among many, not a parallel system.

### Wings exception

`BodyPartType` gains `Wing` (cheap addition, matches `TODO.md`'s own long-standing Fairy-wings
example -- not granted to any current race; no race has wings yet, this only builds the *rule*).
`BodyPartEffectsSystem`'s own Leg/Foot-penalty computation checks first whether the entity has any
non-disabled `Wing` part -- if so, the movement penalty is skipped entirely regardless of Leg/Foot
condition (a winged entity flies, it doesn't limp). This is the entity's *own* wings, checked fresh
each visit (a Wing that later gets disabled turns leg penalties back on).

### Graduated curve, per the confirmed body-part-type-to-effect mapping (decided)

- **Legs and Feet -> `MovementLockFrames`.** Each of an entity's own Leg/Foot parts independently
  contributes a penalty, linearly lerped from 1x (no penalty) at 100% HP up to 2x lock frames at 0%
  HP for *that part alone*. Penalties **compound multiplicatively across every Leg/Foot the entity
  owns**, generic to however many the entity has (2 for Goblin/Human, more or fewer for a future
  race) -- each part's own per-part multiplier (`Lerp(1, 2, 1 - hpFraction)`) is multiplied together
  across all Leg/Foot parts to get the entity's final `MovementLockFrames` modifier, so two
  half-damaged legs compound worse than one, and losing both legs entirely is worse than losing one.
  **If every Leg/Foot part is simultaneously at 0 HP/disabled, movement is hard-blocked outright**
  (a boolean gate in `MovementSystem`, not just a very large multiplier) -- checked *after* the wings
  exception below, so a winged entity with both legs gone still isn't blocked.
- **Arms and Hands -> `MeleeOutgoingDamage`.** Same shape: each Arm/Hand part lerps its own
  multiplier from 1x at 100% HP down to 0x (zero melee damage from that part's own contribution) at
  0% HP, multiplied together across every Arm/Hand the entity owns. **If every Arm/Hand is
  simultaneously at 0 HP/disabled, melee attacks are hard-blocked outright** (the activation itself
  refused, not just a 0x damage hit that still consumes the swing) -- mirrors the movement gate
  above.
- Deliberately **not** touching carry capacity/lifting (`TODO.md`'s own "Arms disabled block
  melee/lifting" bullet) -- that's gated on Strength/carry-capacity infrastructure that doesn't
  exist yet (a separate, still-open TODO item), not something to build a placeholder for here.
- Deliberately **not** touching Equipment slot counts (`TODO.md`'s own Equipment item) -- Equipment
  itself doesn't exist yet either. A new TODO item (`Melee actions should declare which body parts
  perform them`) has been logged as the Equipment-era follow-up: today's penalty is a blanket
  aggregate across every Arm/Hand the entity has, since no action yet declares which specific
  part(s) actually perform it -- correct only because nothing equips a weapon to one specific limb
  yet.

### How the system keeps its own modifiers in sync

Each visit, `BodyPartEffectsSystem` computes the entity's *current* desired
`MovementLockFrames`/`MeleeOutgoingDamage` magnitudes from its body parts, then finds-or-grants a
permanent (`durationFrames: null`) `StatModifierComponent` for each target, attributed to a fixed
`StatusEffectSource` this system alone uses (so it can find and update its own prior grant without
touching a player's other buffs/debuffs on the same target) -- updates the magnitude in place via
`StatModifierComponent`'s own `TryUpdate`-shaped idiom if it already granted one, removes it
entirely if the computed penalty is now zero (fully healed), grants fresh if this is the first time
the entity has taken relevant damage. **Open question below: exact source/lookup mechanics --
proposing this shape, not fully pinned down.**

## Open questions -- resolved

- **Stacking**: compounding, generic to however many Leg/Foot or Arm/Hand parts the entity has (not
  hardcoded to a pair).
- **Curve**: linear lerp; movement up to 2x lock frames at 0% HP per leg; melee down to 0% damage
  contribution at 0% HP per arm.
- **Hard block**: yes -- all Legs/Feet at 0 HP blocks movement outright; all Arms/Hands at 0 HP
  blocks melee outright. Both on top of (not instead of) the graduated multiplier below full loss.

## Test plan

- `Tests/Modules/BodyPartEffects/BodyPartEffectsSystemTests.cs` (new): a damaged (not disabled) Leg
  grants a proportional `MovementLockFrames` modifier; a fully healed Leg removes it; multiple
  damaged legs per the stacking decision above; a non-disabled Wing part suppresses the Leg penalty
  entirely even with both Legs disabled; same coverage mirrored for Arm/Hand -> `MeleeOutgoingDamage`.
- `Tests/Modules/Movement/MovementSystemTests.cs`: confirms `MovementLockFrames` is now consumed
  (a granted modifier measurably changes lock duration).
- `Tests/Modules/Actions/ActionEffectTests.cs`: confirms `MeleeOutgoingDamage` only applies when
  `Tag.Melee` is present (a Punch-shaped effect entry is affected, a Magic-Missile-shaped one isn't).
- Full `dotnet build`/`dotnet test`, matching the existing pre-existing-failure baseline.

## Execution phases

1. **`StatModifierTarget` additions + `MovementSystem`/`DirectDamage` consuming them, `BodyPartType.Wing`.**
   No live behavior change yet (nothing grants these modifiers). Verify: build/test only.
2. **`BodyPartEffectsSystem` itself**, wired into a new `BodyPartEffectsModule`. Verify: build/test,
   then manual in-game test -- damage a Goblin/Human's Leg, confirm movement visibly slows; damage
   an Arm, confirm Punch damage visibly drops; heal back up, confirm both clear; confirm nothing
   changes for a Simple-health entity (Fairy/Ghost -- no body parts to penalize).
