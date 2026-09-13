# Deadline-Driven Countdowns -- Timer Wheel or Direct Walk

**Status: IN PROGRESS -- steps 0-3 done 2026-09-11 (see "Progress").** The step 3 prototype
chose the **timer wheel** for every ticking countdown (see "Step 3 result"). Decisions recorded
2026-09-11 (see "Decisions"). Follows P2 and P5 of
`PLAN-optimization-priorities.md`. Prerequisite done 2026-09-11: every countdown in the codebase
now goes through one of two Engine helpers (see "Starting point").

Windowed figures are ms per simulation frame, seed 1, frames 600-3600, Debug build, from
`Log/phase-benchmarks/20260911-122953.json` (`EcsContext.Update` = **2.48**). Headless figures are
the A-side medians of `Log/phase-benchmarks/ab-*.json` from the same day (`EcsContext.Update` =
**1.12**). Compare only within one sitting, or with the skill's A/B mode -- see the
phase-performance-testing skill.

## Why

Every countdown today is advanced by *visiting* its owner and subtracting. The measurement below
shows that the visit, not the subtraction, is what costs -- and most visits find nothing to do.

### ActionLock vs ActionCooldown, measured

P2 item 3 asked why ActionLock (70k components) cost as much as ActionCooldown (210k). Temporary
counters over frames 600-3600 answered it:

| | ActionLockSystem | ActionCooldownSystem |
|---|---:|---:|
| Entities visited per frame | 299.6 | 299.7 |
| Component instances read per frame | 299.6 | 899.2 |
| Countdowns actually running (decremented) per frame | 102.3 | **0.1** |
| Cost, ms/frame (windowed, 3 runs) | 0.17-0.19 | 0.09-0.10 |

- **Both visit the same ~300 entities per frame.** Every entity with an action lock also has
  action instances; both stripe sets are driven by the same 70.4k entities (527 Local, 29.4k
  Neighborhood, 0 Borough, 40.4k Beyond). The "3x instances" are 3 entries on one chain per
  entity, which is cheap to walk once the entity is found.
- **ActionCooldown is almost pure scan.** 99.99% of its visits find every cooldown already 0.
- **ActionLock costs more because a third of its visits write.** A write is a second
  entity-to-dense-index lookup, a delegate call, and a version-array write (another cache line).
- **Cost tracks entities visited plus writes, not instance count.** About 300 ns per visited entity
  in the windowed Debug build, dominated by scattered access into 2.6M-entry sparse maps. Headless,
  the same systems cost ~4x less (ActionLock 0.046 ms/frame) -- consistent with cache misses being
  the dominant term, since nothing evicts simulation data between frames there.

So the saving is not "decrement faster" -- it is "stop visiting". ActionCooldown spends
0.1 ms/frame to learn nothing.

## Starting point

Two countdown shapes, each with one Engine helper. **All five types below were deleted at step 9
(2026-09-11); this table records where the plan started, not what exists now.** Their replacements:
the ticking shape became `IScheduledTimer` on a `PackedTimerWheel`/`MultiTimerWheel`, and the
resting shape became a bare `uint` deadline field with a comparison at the reader.

| Shape | What reaching 0 means | Helper | Users |
|---|---|---|---|
| **Resting** (`IRestingCountdown`) | A state. Component stays, field rests at 0, readers compare. | `RestingCountdown` | ActionLock, Movement `FramesToWait`, body-part `RegenLockoutFramesRemaining` (ActionCooldown until step 1, now a deadline) |
| **Ticking** (`ITickCountdown`) | An event. `onTick` fires, then re-arms (periodic) or removes (expiring). | `CountdownTicker` / `MultiCountdownTicker` | Burning, BodyPartBurning, Poison, Paralysis, PotionCooldown, AuraSourceExpiry, ContactDamage exposure, StatusEffectAura exposure, Dodge |

**Converged at step 6 (2026-09-11):** `StatModifierExpirySystem` and
`StatusEffectImmunityExpirySystem` used to hand-roll an expiring duration with a *nullable*
`RemainingDurationFrames` (null = permanent), plus an event on expiry for stat modifiers. Both now
store an absolute deadline (`FrameDeadline.Never` = permanent, never scheduled) and sit on the same
wheel as everything else: immunities keyed by `StatusEffectType`, stat modifiers through one
per-entity `ExpiringStatModifierComponent` carrying the earliest deadline among them.

### What this costs today

| System | Windowed ms/frame | Headless ms/frame | Shape | After this plan |
|---|---:|---:|---|---|
| ActionLockSystem | 0.210 | 0.046 | resting | **deleted** |
| ActionCooldownSystem | 0.099 | 0.061 | resting | **deleted** |
| DelayedActionSystem | 0.098 | 0.046 | polls ActionLock for 0 | ticking mechanism |
| BurningSystem | 0.109 | 0.064 | ticking | ticking mechanism |
| ContactDamageSystem (exposure part) | of 0.094 | of 0.048 | ticking | ticking mechanism |
| StatusEffectAuraSystem (exposure part) | of 0.268 | of 0.173 | ticking | ticking mechanism |
| PotionCooldown, Poison, BodyPartBurning, Paralysis, AuraSourceExpiry, Dodge, 2 expiry systems | ~0.06 | ~0.03 | ticking | ticking mechanism |
| MovementSystem / ComplexHealthRegenSystem (wait / lockout part) | of 0.243 / 0.141 | of 0.133 / 0.042 | resting | compare, no write |

The three fully removable systems are 0.41 ms/frame windowed, **~16% of simulation**. Adding the
ticking systems' scan cost, the upper bound is roughly **20-25%**. That's an estimate; each step
re-measures.

## Design

### 1. Resting countdowns become deadlines, and their systems disappear

Store the frame at which the countdown reaches 0 instead of the frames left:
`ReadyAtFrame`. Then `remaining = max(0, ReadyAtFrame - now)` and `ready = now >= ReadyAtFrame`.
Nothing advances it, so ActionLockSystem and ActionCooldownSystem are deleted outright, and the
Movement and regen-lockout checks become a compare with no write.

This is the same idea as Quake's and Source's per-entity `nextthink` time: store when something
next matters, not how long is left. It is not a third mechanism beside the wheel/walk choice:
a resting deadline has no mechanism at all -- nothing runs to advance it.

Consequences:
- **Readers need the clock.** Every reader of the remaining count needs `now`. Most readers are
  systems, which have `EngineTime.FrameCount`. The rest (`ActionLockGate`, `ActionInstanceQueries`,
  `MovementCandidates`, `BodyPartSelection`, `HotbarContent`, `ActionLockContent`,
  `PlayerStatusEffectsContent`, `MapWindow`) need a read-only simulation clock. 23 files touch
  these fields today.
- **Tier staleness goes away for these fields.** A Beyond entity's lock used to clear up to 640
  frames late (one bucket visit). A deadline is exact at every tier. The tier still controls how
  often the entity *acts* -- TestCombatBehavior and Movement stay tiered -- just not when its timer
  reads as elapsed.
- **Wider fields.** `ushort` remaining frames becomes a `uint` absolute frame (2.2 years at 60fps),
  2 bytes per field.
- **Merge actions change meaning.** `CoreModule`'s ActionLock merge averages
  `CurrentLockFramesRemaining`; averaging two deadlines is equivalent, but each merge action must be
  re-checked rather than assumed.

### 2. Ticking countdowns: one mechanism, chosen by prototype

Ticking countdowns also become deadlines: `ITickCountdown.FramesUntilNextTick` becomes an absolute
`NextTickFrame`, re-armed by `onTick` as `now + period`. What differs is how a system finds the
ones that are due. Two candidates; **exactly one will be adopted for every ticking countdown**, so
there is one path to learn, test and optimise.

#### 2a. Direct walk

Walk the component pool's own contiguous storage (`0..Count`, via the existing `GetByDenseIndex`/
`GetEntityIdByDenseIndex`) every frame, and fire any component with `now >= NextTickFrame`. No
entity-ID lookup per visit, so reads are sequential and prefetchable. The industry-standard shape
for entity-heavy games (Unity's ECS, Bevy): make the scan cheap rather than rare.

- **Deadlines are required, not optional.** A removal moves the pool's last component into the
  gap, so a walk by position can skip an entity once or visit it twice in a cycle. With "subtract
  frames per visit" that loses or gains time; with a deadline it cannot -- a late check still
  fires correctly.
- **Timer systems leave the tier machinery.** They become plain `ISystem`s walking everything every
  frame (exact timing). Decision 1's fallback, if that proves too costly: walk 1/N of storage per
  frame, accepting up to N frames' lateness but never lost time.
- **Multi-pool timers need nothing special.** Each instance is visited in storage with its own
  deadline; no instance key.
- **Tick order within a frame becomes storage order** instead of stripe order -- still
  deterministic, but gameplay may shift slightly (the A/B fingerprint shows it).
- Removals are deferred to after the walk, as `CountdownTicker` already does.

#### 2b. Timer wheel

A ring of `W` slots (Varghese & Lauck, 1987 -- the structure behind the Linux kernel's timers,
Netty's `HashedWheelTimer` and Kafka's delayed-operation purgatory); slot `frame % W` holds the
entities due that frame. Per frame the owner processes one slot, so cost is O(timers firing), not
O(population).

- **One wheel per system, drained in `BeginFrame`,** all instances of one Engine `TimerWheel`
  type. Ticks run at the same point in the frame as today, so system order doesn't change, and
  each module still owns its data. (One global wheel was considered and rejected: it either
  changes when ticks run relative to other systems or needs a second hop into per-system queues;
  it buys only one place to manage everything.)
- **Lazy cancellation.** Entries are never removed. When a slot fires, each entry is checked: does
  the entity still have the component, and does its `NextTickFrame` equal this frame? If not
  (removed, re-armed, entity died), the entry is dropped. Re-arming just inserts a new entry. This
  also covers recycled entity IDs (`EntityManager` reuses them via `FreeIdPool`); a generation
  counter is the fallback if that proves insufficient.
- **How entries get scheduled: the pool tells the wheel (decided at step 3).** There are 16
  sites that create or re-arm a ticking timer across 9 component types, several of them adding a
  *second* instance to an entity that already has one (aura exposures, body-part burns) -- which
  raises no EntityAdded, so membership events can't be the source. Requiring every write site to
  also call the wheel would make "forgot to schedule" a silent never-fires bug, the same shape as
  the cadence-compensation defect this codebase hit six times. Instead: both pool types already
  bump a component's version on every mutation (Add, Merge, TrySet, TryUpdate, UpdateByDenseIndex,
  SetByDenseIndex, IncrementVersionByDenseIndex); an opt-in per-pool change observer is invoked at
  those points, and a timer pool's wheel schedules an entry whenever the observed component's
  `NextTickFrame` is a future frame. A write that doesn't change the deadline adds a duplicate
  entry at the same frame; the drain de-duplicates within a slot. Writers only compute the
  absolute deadline (they need the clock for that anyway). The one unobserved path is a raw
  `GetByDenseIndex` ref write without the `IncrementVersionByDenseIndex` its contract already
  requires -- the hook makes that existing contract load-bearing.
- **Horizon.** Single level with `W` = 4096 frames (~68s) covers every periodic tick. Longer
  deadlines (long cooldowns, 18-minute modifiers) go to an overflow list re-bucketed once per
  revolution, or a second, coarser level. Decide from the actual longest durations in data.
- **Multi-pool instance identity.** A MultiComponentPool dense index is not stable, so an entry
  needs `(entityId, stable instance key)`: `PartId` for body parts, `StatusEffectType` for
  exposures, the modifier's own identity for stat modifiers. Each component needs a named key.
- **Determinism.** Slot entries fire in insertion order, which is deterministic under a seed.
  Needs a test.

#### 2c. How the prototype decides

Both approaches pay the same cost for the ticks themselves: under exact timing, every tick fires
on its frame either way. They differ only in how due timers are *found*:

- **Walk:** every timer is touched every frame. Cost per frame = timer population x per-touch cost.
- **Wheel:** only firing timers are touched, but each fire costs a scattered lookup to validate the
  entry, plus an insert to re-arm. Cost per frame = fires per frame x per-fire overhead.

For a periodic timer, fires per frame = population / period, so the walk touches `period` times
more timers than the wheel fires -- but each touch is far cheaper. **Map growth does not change
the winner**: both costs scale with timer population, so their ratio is fixed by periods and
per-operation costs. Map growth only changes the total. So the prototype measures the per-operation
costs directly and computes the choice:

1. **Direct walk for BurningSystem** (Packed pool, ~20k, 60-frame period, the largest periodic cost)
   **and the StatusEffectAura exposure timer** (Multi pool, ~20k). Full walk every frame. Headless
   A/B against the current systems. Measure the walk's own cost separately from the `onTick` cost
   (temporary probe), giving ns per touch.
2. **Wheel per-fire overhead** from a microbenchmark: one validating lookup into a real-size pool
   plus a slot insert, in ns per fire. No full wheel is built for this.
3. **Compute both totals across all ticking timers** from their populations and periods
   (populations from the headless runs, periods and durations from data). Long-period and
   long-duration timers (potion cooldowns, modifiers, immunities) favour the wheel;
   short-period ones (burning, contact damage) favour the walk.
4. **Decide on Release figures,** since Release is what ships -- the Debug build doesn't inline
   the tight loop the walk depends on, and would under-rate it. Debug figures are recorded too.
5. **Adopt the lower total.** Ties, and anything within measurement noise, go to the direct walk:
   it is the simpler of the two.

### 3. DelayedActionSystem

It currently visits every entity with a pending delayed action to ask "is the lock 0 yet?". Once
the lock is a deadline, the pending action copies the lock's `ReadyAtFrame` when it is queued, and
then:
- **walk:** DelayedActionSystem walks the pending-action pool and fires `now >= ReadyAtFrame`;
- **wheel:** queuing the action schedules a wheel entry at `ReadyAtFrame`.

Either way, the "same cadence as ActionLockSystem" invariant its remarks defend (and MapWindow's
charge-fill telegraph depends on) is replaced by something stronger: both read the same deadline,
so they cannot drift.

## Step 3 result: timer wheel (2026-09-11)

**Live inputs** -- temporary probes in `CountdownTicker`/`MultiCountdownTicker` and the three
hand-rolled expiry systems, headless Release, seed 1, frames 600-3600 (fingerprint unchanged by
the probes):

| Timer | Pool | Population | Fires/frame |
|---|---|---:|---:|
| StatusEffectAuraExposure | Multi | 19,664 | 330.3 |
| Burning | Packed | 19,625 | 97.0 |
| ContactDamageExposure | Packed | 1,614 | 96.9 |
| Poison | Packed | 793 | 12.6 |
| PendingDelayedAction (lock reaching 0) | Packed | 1,066 | 9.1 |
| PotionCooldown | Packed | 3,551 | 2.8 |
| BodyPartBurning | Multi | 138 | 2.3 |
| Paralysis, Dodge, AuraSourceExpiry, StatModifier, Immunity | -- | ~0 | ~0 |
| **Total** | | **~46,450** | **~551** |

A full walk touches ~84 timers for every one a wheel fires, so the walk wins only if a touch
costs under 1/84 of a fire.

**Per-operation costs.** A standalone microbenchmark on the real Engine pools at game scale
(2.6M capacity, ids spread across it; "cold" evicts caches between frames), then an in-situ probe
inside BurningSystem timing a read-only walk of its real 19.6k-entry pool and 551 wheel-style
validations (entity-id lookup + deadline compare) of real entity ids per frame:

| | Walk, ns/touch | Wheel validation, ns/fire |
|---|---:|---:|
| Microbenchmark, Release, warm / cold | 1.2 / 2.4 | 38 / 167 (Multi: 85 / 216) |
| In situ, headless Release | 0.72 | 21 |
| In situ, **windowed Release (ships)** | **4.9** | **49** |
| In situ, headless Debug | 7.7 | 105 |

**Totals across all ticking timers** (population x touch vs fires x validation, Multi share of
fires ~60% at the microbenchmark's Multi/Packed ratio):
- **Windowed Release:** walk ~228 us/frame, wheel ~31 us/frame -- **wheel ~7x cheaper.**
- Headless Release: walk ~33 us/frame, wheel ~20 us/frame -- wheel ~1.6x cheaper.

Not a tie by any measure, so the tie-break toward the walk doesn't apply. **Decision: timer wheel
for every ticking countdown.**

What decided it: the direct walk's cheapness depends on warm caches, and the shipped game doesn't
give it any -- Draw runs between simulation frames, and a walk touch cost 7x more windowed than
headless. The "make the scan cheap" industry default assumes a dense archetype layout that keeps
hot data resident; here the wheel's O(fires) is what scales.

Deviation from the step as written: the prototype did not convert BurningSystem to a full direct
walk in-game. It measured the walk's scan directly in situ instead -- exactly the cost a walk
adds -- which needed no deadline conversion the wheel didn't also need. All probe code was
reverted; nothing from the prototype stays.

## Decisions (2026-09-11)

1. **Distant tick fidelity: exact timing preferred, not a hard rule.** Ticking countdowns fire on
   their exact frame at every tier, with either mechanism. This also fixes the "bulk DoT lands
   before regen can offset it" problem (`TODO.md`, "Distant simulation fidelity") for
   tick-vs-tick effects. Regen is continuous, not a countdown, so it isn't covered either way.
   **Fallback if measurement shows a problem:** coarser timing for coarse tiers -- deadlines rounded
   up to the tier's granularity (wheel), or 1/N of storage walked per frame (walk).
2. **Unsimulated tiers: freeze, temporarily.** An entity in a tier at or past `SimulatedTierCount`
   does not advance at all -- its timers stop, and resume from where they were when it is next
   simulated. Better approaches are to be researched later (`TODO.md`, "Unsimulated-tier time --
   research alternatives to freezing"). Consequence for this design: **absolute deadlines don't
   freeze on their own.** A deadline keeps approaching while the entity is frozen, so it would
   expire unobserved. On a tier change into an unsimulated tier, each of the entity's deadlines is
   converted to "frames remaining" and parked -- for a walk, by setting the deadline to a sentinel
   (`uint.MaxValue`) that never fires, keeping the walk free of per-entity tier lookups; for a
   wheel, by also dropping its entries. On the change back out, it is rebased as
   `now + remaining`. `ProcessingTierEvents.TierChanged` already carries exactly this transition.
   Moot until P2 lowers `SimulatedTierCount` below 4, but whichever mechanism is chosen must
   support park/unpark from the start.
3. **Clock ownership: one clock.** A single simulation clock (the existing frame counter, which
   already stops while paused), passed explicitly to whatever reads it rather than read from a
   static, so a second clock later is a wiring change rather than a redesign. Separate per-map
   clocks are investigated when a second map exists (`TODO.md`, "Third Pause modality").
4. **One ticking mechanism, not a mix.** Every ticking countdown uses the mechanism the prototype
   picks -- no per-system choice between wheel and walk. A mixed codebase would mean two paths to
   learn, test and keep correct, for a saving the prototype can quantify. If the chosen mechanism
   turns out badly wrong for one system later, that is a reason to revisit the choice for all of
   them, not to add a second path.

## Status: complete (2026-09-11)

All nine steps landed. 1695 tests pass, and every timer-driven UI was visually verified in-game by
the user afterwards -- the HUD action-lock wheel, hotbar slot fills, the map charge-fill telegraph,
potion cooldown text and HealthWindow's duration rows all read correctly off deadlines.

One item stays open by design: park/unpark for frozen tiers (decision 2), which is moot until
`SystemManager.SimulatedTierCount` actually drops below 4 in P2.

## Progress

| Step | Status | Result |
|---|---|---|
| 0. Simulation clock | **done** | `SimulationClock` + `FrameDeadline` (Engine). Advanced by `SystemManager.Update` rather than by each game loop -- one place covers GameLoop and HeadlessBenchmark. Reaches modules via `GameModuleContext.SimulationClock`, Presentation via `EcsContext.SystemManager.Clock`. |
| 1. ActionCooldown to a deadline | **done** | `ActionInstanceComponent.CooldownReadyAtFrame`; ActionCooldownSystem and its tests deleted; `cooldownFramesRemaining` constructor/grant parameter removed (every real grant passed 0). Headless Debug A/B: ActionCooldownSystem 0.067 ms/frame -> 0, total -3.5%. Different world fingerprint -- expected, distant cooldowns now clear on the exact frame instead of at their tier's next visit. |
| 2. Release benchmarking | **done** | `-Configuration Release` in the skill script; per-configuration baselines (`baseline-build-debug`/`-release`). Release headless: `EcsContext.Update` 0.68 ms/frame vs Debug 1.23. |
| 3. Prototype and decision | **done** | Timer wheel -- see "Step 3 result". |
| 4. Timer wheel in Engine | **done** | `TimerWheel` (4096 slots + overflow), `PackedTimerWheel`/`MultiTimerWheel`, `IScheduledTimer`/`IKeyedScheduledTimer`, opt-in `ComponentChanged` on Packed and Multi pools (fired at every version bump, so scheduling is automatic -- no consumer calls Schedule). Park/unpark not built: moot until P2 lowers `SimulatedTierCount` (decision 2). |
| 5. Every ticking system onto it | **done, cost open** | Burning, Poison, BodyPartBurning, Paralysis, PotionCooldown, AuraSourceExpiry, Dodge, ContactDamage exposure, StatusEffectAura exposure. All now plain `ISystem`, StripeCount 1. Every timer write takes a required `now`. 1690 tests pass. **Headless Release A/B vs the pre-step-4 baseline: `EcsContext.Update` 0.62 -> 0.88 ms/frame (+42%)** -- StatusEffectAura +0.19, Burning +0.08, Poison +0.004; the small expiry systems went down as expected. Cause is fidelity, not the wheel: under tiers an exposure re-granted at most once per visit (Beyond = 15 x 64 = 960 frames) while its interval is 60, so a distant creature in lava was topped up once every 16 s and burned out between. A probe giving only non-Local exposures their old per-tier cadence brought the total back to baseline (+0.7%, noise) with StatusEffectAura 11% cheaper than before. Decision 1's fallback (coarser timing for coarse tiers) was the lever. **Decided 2026-09-11: keep exact timing everywhere** -- the added cost is the simulation distant creatures were always owed. |
| 5b. Fixed-rate re-arm | **done** | A periodic timer re-armed from the frame its firing was *handled* on (`After(now, period)`), so a late firing slid every later tick with it and swallowed whatever periods a gap covered. `FrameDeadline.Repeat(previousDeadline, period)` counts from the deadline instead -- exact cadence, and owed firings catch up one per frame. Applied to all five periodic re-arms (Burning, Poison, BodyPartBurning, aura exposure, ContactDamage). No behaviour change while the clock advances a frame at a time, which it always does in the real loop. |
| 9. Remove the countdown helpers | **done** | Deleted `RestingCountdown`, `IRestingCountdown`, `CountdownTicker`, `MultiCountdownTicker`, `ITickCountdown` and `RestingCountdownTests` -- six files, nothing referencing them outside plan prose. The codebase now has exactly one way to express "later": an absolute `uint` frame, fired by a timer wheel when reaching it means something happens, or compared at the reader when it only stops gating. CLAUDE.md's framesPerVisit rule was narrowed to match (it no longer applies to timers at all -- that whole class of tier-cadence bug is gone with the decrements), and `EntityDiedEvent`'s deferred-removal note now points at `PackedTimerWheel`. |
| 8. Movement wait and regen lockout to deadlines | **done** | `MovementComponent.FramesToWait` -> `WaitUntilFrame`, `BodyPartComponent.RegenLockoutFramesRemaining` -> `RegenLockedUntilFrame`. Neither is on a wheel and neither should be: nothing fires when they elapse, they just stop gating, so each is a plain comparison at the one place that reads it (`IsWaiting(now)`, `IsRegenLockedOut(now)`). That deleted ComplexHealthRegenSystem's per-visit walk of every body part of every due entity -- the lockout now costs that system nothing at all -- and removed the last reason MovementSystem and TestCombatBehaviorSystem needed a single-owner rule for the backoff (the "both decremented it, so it elapsed twice as fast" bug has nothing left to happen to). `now` is threaded through the health facades to reach them: `HealthDamage.Apply`/`HealthHeal.Apply`/`ComplexHealthDamage`/`ComplexHealthHeal`/`BodyPartDamageEffects`/`BodyPartSelection.PickLowestPercentage` all take it required, so no caller can quietly pass frame 0 and hand a part a lockout that expired before it began. **This retires `IRestingCountdown`/`RestingCountdown` entirely -- no production users remain**, only the Engine helper and its own test file, which step 9 deletes. Headless Release A/B vs the same pre-step-4 baseline: MovementSystem 0.0580 -> 0.0568 and ComplexHealthRegenSystem 0.0164 -> 0.0161, both flat inside a 6-7% run spread -- expected, since a walk of a handful of parts per visit was never the cost; the win is that a lockout or backoff at any tier now ends on the frame it should. 1701 tests pass. Total unchanged by this step (0.52 -> 0.76 cumulative, still step 5's exact-timing cost). |
| 7. ActionLock to a deadline, DelayedActionSystem on the wheel | **done** | `ActionLockComponent.UnlockedAtFrame` replaces the ticked `CurrentLockFramesRemaining`; `CurrentLockTotalFrames` stays purely as the UI fraction's denominator. `ActionLockGate.IsBlocked`/`Lock` take `now`, with `FramesRemaining(lock, now)` for the HUD fills. **ActionLockSystem deleted: 0.0176 -> 0 ms/frame** (headless Release A/B vs the pre-step-4 baseline). `PendingDelayedActionComponent` is now an `IScheduledTimer` carrying `ReadyAtFrame` -- copied from that same lock deadline when the action is queued -- and DelayedActionSystem is a plain `ISystem` on a `PackedTimerWheel`: 0.0320 -> 0.0304, inside A's own 7.7% spread, so flat. That is expected: it had already been tiered down to ~1/6 of its original cost, so the win here is correctness rather than ms. The old "these two systems must share a tier cadence or the lock and the resolution drift apart" invariant (which MapWindow's charge telegraph depends on) is now structural: one deadline, two readers, nothing ticking either. Cancellation still just removes the component -- its wheel entry is dropped as stale. Readers updated: ActionLockContent, HotbarContent (x2), MapWindow (which gained a `SimulationClock`), InventoryGridContent (plus the four windows that build it), ActionTargetingController. Stale claims corrected in PLAN-charge-attack-fill-indicator.md (design + two addenda), IMPLEMENTATION-NOTES.md and this repo's tiered-system inventory in PLAN-optimization-priorities.md. 1701 tests pass. The total still carries step 5's exact-timing cost (0.55 -> 0.79 cumulative), unchanged by this step. |
| 6. StatModifier and StatusEffectImmunity expiry | **done** | Both store absolute deadlines now (`FrameDeadline.Never` = permanent, never scheduled) and both are plain `ISystem`, StripeCount 1. Immunities: one `MultiTimerWheel` keyed by `StatusEffectType`, with `StatusEffectImmunityEffects.Grant` the single write surface -- re-granting a type extends the one instance to the later deadline rather than adding a second, which is what keeps that key unique. Stat modifiers: one wheel entry per *entity* (`ExpiringStatModifierComponent` = the earliest deadline among its modifiers; the pool's merge keeps the earlier), maintained from the `StatModifierComponent` pool's own ComponentChanged. That choke point also fixes a real bug: `StatModifierGrant` wrote the pool directly and never added the old membership marker, so a timed modifier granted by an action (a potion, a scroll) was never visited by the expiry system and never expired. Two wheel edges found by tests: re-arming to an already-reached deadline left a timer resting forever (the system now pushes it to the next frame), and a subscriber granting during an expiry sweep must be accounted for after the events are published, not before. Headless Release A/B vs the same pre-step-4 baseline: StatModifierExpirySystem 0.0002 -> 0.0001, StatusEffectImmunityExpirySystem 0.0003 -> 0.0001 ms/frame; the totals still carry step 5's fidelity cost (0.52 -> 0.79 cumulative), unchanged by this step. 1703 tests pass. |

## Steps

Each step lands separately, with tests and a headless A/B benchmark (`-SaveBaseline` before,
`-Compare` after).

0. **Simulation clock.** Engine `SimulationClock` (current simulation frame), advanced by
   `GameLoop` and `HeadlessBenchmark` alongside their frame counters, exposed read-only through
   `GameModuleContext` and to Presentation. No behaviour change.
1. **ActionCooldown to a deadline.** The cleanest first target: almost never running, so the
   system is pure overhead, and few readers (activation checks, queries, TestCombatBehavior,
   hotbar). Delete ActionCooldownSystem. Expected: -0.1 ms/frame windowed.
2. **Release benchmarking.** Add `-Configuration Release` to the phase-performance-testing script
   (a Release exe path and a Release baseline build), and record Release figures for today's
   systems. Needed because step 3 decides on Release numbers.
3. **Prototype and decision: direct walk vs wheel** (design 2c). Convert `ITickCountdown` to
   `NextTickFrame` for the two prototype timers, build the direct walk for them, microbenchmark the
   wheel's per-fire overhead, compute both totals, and record the decision and its numbers in this
   document. The prototype code stays if the walk wins, and is reverted if the wheel wins.
4. **Build the timer wheel in Engine** (chosen at step 3). The `TimerWheel` type with lazy
   cancellation, in-slot de-duplication, overflow, park/unpark (decision 2), and the opt-in pool
   change observer that schedules entries (design 2b); a determinism test, and stale-entry,
   ID-reuse, second-instance and raw-ref-write tests.
5. **Every ticking system onto it,** Burning first (largest), then the rest -- including Dodge.
   Timer systems stop being `ITieredSystem`. For the wheel, multi-pool users first get a stable
   instance key.
6. **StatModifier and StatusEffectImmunity expiry** onto the same mechanism. Permanent (null)
   durations are never due (walk: sentinel deadline; wheel: never scheduled).
7. **ActionLock to a deadline, with DelayedActionSystem on the chosen mechanism** (design 3).
   Together, because the latter polls the former. Delete ActionLockSystem. Update the four
   Presentation readers and `PLAN-charge-attack-fill-indicator.md`'s invariant. Expected: -0.2 to
   -0.3 ms/frame windowed.
8. **Movement wait and regen lockout to deadlines.** Small, and last because they live inside
   systems that stay regardless.
9. **Remove `RestingCountdown` and whichever of `CountdownTicker`/`MultiCountdownTicker` remain**
   once nothing uses them.

## Risks

- **Correctness-sensitive shared timing.** Existing tests are behavioural (after N updates, value
  is X). Many assume "call Update, the countdown drops by StripeCount"; with deadlines those tests
  change shape, not just numbers. Budget for rewriting them rather than patching.
- **The prototype can mislead if it is too narrow.** Two timers stand in for nine-plus. The totals
  in 2c use every timer's real population and period, so the choice rests on measured
  per-operation costs, not on extrapolating two systems' totals.
- **Debug vs Release.** The decision uses Release; every other step's A/B uses whatever the skill's
  default is. Record which, every time.
- **UI timing.** Fill bars currently show the value as of the last tiered visit. Deadlines make
  them exact, which is more correct but visibly different for anything the player watches.
