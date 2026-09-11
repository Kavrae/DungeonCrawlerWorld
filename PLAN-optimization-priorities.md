# Optimization Priorities

(Investigation backlog, not a design. Produced after `PLAN-presentation-data-layer.md` rejected a
presentation-side cache layer on measured grounds -- Presentation is 9% of the busy frame,
simulation is 90%, so everything worth doing is on the simulation side.)

Every figure below is ms per second of wall clock from
`Log/phase-benchmarks/20260910-031425.json` (commit `7f006e4`, seed 1, 8 samples at 5s).
`EcsContext.Update` totalled **438.00 ms/sec** in that run.

**Measurement caveat -- read this before trusting any number here.**

Two effects contaminate these figures, both discovered while doing the work:

1. **Noise floor ~10%** on the large items between consecutive same-process samples.
2. **The first sample after startup is not steady state**, and this is much larger than the noise.
   `StartupProfiler` signals stable at ~12s, but that tracks `EcsContext.Update` cost settling, not
   the *world* settling. Gameplay-dependent systems keep decaying well past it. Measured on the
   final build, three consecutive 50-second samples in one process gave `EcsContext.Update` of
   **200.09, then 122.55, then 135.89** -- the first sample was ~55% high, and samples 2-3 bracket
   the real steady state around 130. `PotionCooldownSystem` read 37.70 in sample 1 and 9.90 in
   sample 2, a 74% drop with no code change at all.

Every benchmark in this document compares *first samples*, so the methodology is at least
consistent -- but the absolute values are inflated and the precise percentages are not
trustworthy. Large effects (a system halving, or falling 66%) are real; anything under ~20% is
not distinguishable from transient decay.

**Fix the harness before the next round of work:** discard the first sampling window, or sample
until two consecutive windows agree within the noise floor, rather than sampling immediately after
`startup-*.json` appears.

## The shape of the problem

Two categories account for **two thirds of all simulation cost**:

| Category | ms/sec | Share of sim |
|---|---:|---:|
| `TestCombatBehaviorSystem` (acknowledged temporary scaffolding) | 146.13 | **33.4%** |
| Countdown/periodic bookkeeping across 10 systems | ~142.8 | **32.6%** |
| Everything else (movement, tiering, auras, contact damage, body parts, actions) | ~149 | 34% |

Neither category is "the game doing something interesting." That is the headline.

---

## P1 -- `TestCombatBehaviorSystem`: 146 ms/sec, and it is scaffolding

**The single largest cost in the entire frame**, larger than all of Presentation by 3x.

Its own doc comment says: *"Temporary, deliberately generic priority-chain decision-maker... This
class is explicitly a stand-in for that future composite-behavior system, not the real thing --
named 'Test' deliberately so nothing mistakes it for a permanent design."*

It is striped at 15 (matched to `MovementSystem`, after a previous incident where it was 1 and
caused a ~2fps slowdown -- see its own comment), so the cadence is already tuned. The cost is what
it does per visit: a priority chain over `MovementComponent`'s ~70,066 entities that reads
`ActionLockComponent`, `SimpleHealthComponent`, `BodyPartComponent`, `InventoryItemStackComponent`,
`ActionInstanceComponent`, `RaceComponent`, plus `IMapQuery` adjacency probes -- seven-plus
scattered pool reads per entity, most of which end in a no-op branch.

**Investigate:**
1. How much of the 146 is spent on entities that take *no* action? Instrument the branch outcomes
   (wander / melee / heal / nothing) and count them. If the overwhelming majority are "nothing,"
   the chain is being evaluated to reach a negative answer.
2. Order the chain by cheapest-rejection-first, and check whether an early bail exists. Adjacency
   (`IMapQuery`) is likely the most expensive test and may be running before cheaper rejections.
3. Whether it should be tiered rather than plain-striped. It is one of only three cost-bearing
   systems wired to `EntityStripeSet` rather than `ProcessingTierWiring` (see P4). A `Beyond`-tier
   goblin deciding whether to melee something is work nobody will ever observe.

**Payoff:** up to ~146 ms/sec, i.e. up to a third of simulation. **Risk:** it is scaffolding, so
any effort spent optimizing it is thrown away when the real composite-behavior system lands (see
`TODO.md`'s entry on composing entity behavior). That argues for option 3 (tier it -- a few lines,
carries forward) over options 1-2 (restructure the chain -- discarded later).

### Option 3 DONE: TestCombatBehaviorSystem is now tiered

Swapped `EntityStripeSet` for `ProcessingTierWiring`, keyed off `FrameCount` (matching
`MovementSystem`, so the two still bucket identically and an entity is still decided and moved on
the same frame -- which is what makes this system's ordering ahead of `MovementSystem` meaningful).
Its `FramesToWait` decrement now uses the tier's frames-per-visit rather than the base
`StripeCountValue`, the same Defect A correction applied elsewhere.

`Log/phase-benchmarks/20260910-042658.json`, first-sample-to-first-sample against `042040`:

| | Before | After | Delta |
|---|---:|---:|---:|
| `TestCombatBehaviorSystem` | 137.92 | **46.42** | **−66.3%** |
| `EcsContext.Update` | 242.27 | 200.09 | −17.4% |

Options 1 and 2 (instrumenting the branch outcomes, reordering the chain for cheapest-rejection-
first) remain unexplored and are still the discardable ones -- worth doing only if this system
outlives its "temporary" label.

**Resolved: `FramesToWait` had two owners and now has one.** Both this system and `MovementSystem`
decremented the same `MovementComponent.FramesToWait`; both are wired to the same pool with the
same stripe count and divisors, and `SystemManager` derives `stripeIndex` as
`FrameCount % StripeCount`, so they visited the same entity on the same frame and each took a full
span off -- every wait elapsed at twice its intended rate. **`MovementSystem` is now the sole
owner**; `TestCombatBehaviorSystem` still reads the field as a gate (it refuses to decide while a
wait is running) but no longer decrements it. Waits now last their authored duration, which means
wandering NPCs pause roughly twice as long between failed move attempts as they did before.

---

## P2 -- Countdown bookkeeping is ~33% of simulation and is O(population) per frame

`Engine/ECS/Systems/CountdownTicker.Tick` walks the *entire* entity span every call and does two
pool operations per entity -- a `TryGetReadonly` plus a `TryUpdate` decrement -- for every entity
whose countdown has not yet expired. The `onTick` callback only fires at zero. So the steady-state
cost is a full scan whose only product is "everyone is one frame closer."

Systems in this shape, with their component populations from the memory dump:

| System | ms/sec | Instances | Tiered? |
|---|---:|---:|---|
| `ActionLockSystem` | 37.44 | 70,069 | tiered, stripe 10 |
| `ActionCooldownSystem` | 37.11 | 210,211 | tiered, stripe 10 |
| `SimpleHealthRegenSystem` | 19.66 | 35,004 | tiered, stripe 60 |
| `PotionCooldownSystem` | 13.55 | 9,110 | **untiered, stripe 1** |
| `BurningSystem` | 12.96 | 20,455 | tiered, stripe 15 |
| `PoisonSystem` | 10.80 | 650 | **untiered, stripe 1** |
| `ComplexHealthRegenSystem` | 10.00 | 161,843 body parts | tiered, stripe 60 |
| `BodyPartBurningSystem`, `ManaRegenSystem`, 4 expiry systems | ~1.3 | small | mixed |
| **Total** | **~142.8** | | |

**The structural fix is a deadline structure, not a decrement.** Store an absolute expiry frame
and bucket entities by it (a timer wheel, or a per-frame bucket array indexed by
`expiryFrame % wheelSize`). Per-frame cost becomes O(entities actually expiring this frame)
instead of O(population). At these populations that is the difference between ~500k
scan-and-decrement operations per second and a few dozen.

This is a well-understood pattern and the payoff is large, but it touches a shared Engine utility
used by 8+ systems and both `PackedComponentPool` and `MultiComponentPool` variants. It is the
biggest single piece of work on this list.

**Investigate first, cheaply, before committing to the wheel:**
1. **`PoisonSystem` at 650 instances costing 10.80 ms/sec is anomalous** -- that is ~0.3µs per
   entity-visit, orders of magnitude more than a decrement should cost. Either the instance count
   is far higher in the benchmarked world than the memory dump suggested, or `HealthDamage.Apply`
   is firing much more often than "once per countdown expiry." Settle this one first; it is small,
   self-contained, and the answer likely generalises.
2. **`PotionCooldownSystem` is untiered on a stated assumption that measurement contradicts.** Its
   doc says *"only entities that have actually consumed a potion carry this component at all, so
   the population visited is already small."* The memory dump shows **9,110 instances**. Tiering
   it, or striping it above 1, is a one-line change worth trying before anything structural.
3. Check whether `ActionCooldownSystem` at 210,211 instances and `ActionLockSystem` at 70,069
   really cost the same (37.11 vs 37.44). If so, cost is not proportional to population and the
   scan is not the dominant term -- which would change the whole diagnosis.

**Payoff:** plausibly 100+ ms/sec. **Risk:** medium-high; it is a correctness-sensitive rewrite of
shared timing infrastructure, and the tests that cover it are behavioural rather than structural.

---

## P3 status: BLOCKED, then unblocked -- three tier-cadence defects fixed first

Attempting P3 surfaced three defects in the tier machinery, all of which raising the divisors
would have amplified. All three are now fixed (`dotnet test`: 1602 passed).

**Defect A -- missing cadence compensation.** `TieredEntityStripeSet` gives each tier a bucket of
`StripeCount * divisor` and exposes `GetTierFramesPerVisit` for exactly this. Four systems used it;
three did not, decrementing by the base `StripeCountValue` regardless of tier:
`MovementSystem` (`FramesToWait`), `ActionLockSystem`, `ActionCooldownSystem`. A Beyond-tier
entity was visited every `StripeCount * 8` frames but only had `StripeCount` deducted, so its
locks, cooldowns and move-waits all ran at **one eighth of real time**. Their own doc comments
stated the intent ("to account for the number of ticks between updates"); the comments predated
tiering. Fixed by walking per-tier buckets and decrementing by `GetTierFramesPerVisit`.

**Defect B -- silent byte truncation.** `TieredEntityStripeSet` built each bucket with
`(byte)(baseStripeCount * tierDivisors[i])`. For the four systems using
`StripeCount => (byte)GameTiming.FramesPerSecond` (60), the Beyond bucket computed
`(byte)480 = 224`. `EntityStripeSet`'s stripe count is now `ushort` (byte * byte tops out at
65,025, so it always fits).

**Defect C -- over-application, downstream of B.** `SimpleHealthRegenSystem`/
`ComplexHealthRegenSystem` computed `framesPerVisit` in *int* (480) while actually being visited
every 224 frames -- Beyond-tier entities regenerated ~2.14x too fast. Fixed by B, and the three
regen systems now take `framesPerVisit` from the stripe set rather than re-deriving it per entity
(which also drops a scattered `ProcessingTierComponent` read per entity per visit).

**Defect D, found via the tests -- contradictory defaults.** `ProcessingTierWiring` resolves an
entity with no `ProcessingTierComponent` to **Beyond** (deliberate, see its doc), but the regen
systems' own per-entity lookup defaulted the same entity to **Local**. Such entities were
*bucketed* as Beyond and *scaled* as Local. Now there is one default. Nine tests were relying on
this contradiction without stating it -- they created entities with no tier component while
reading as if they tested Local cadence (one comment even asserted "no ProcessingTierComponent =
Local-tiered"). Those tests now seed their tier explicitly.

### Measured result: simulation cost went UP 33%

`Log/phase-benchmarks/20260910-035410.json` (seed 1, 10 samples) against `20260910-031425.json`:

| | Before | After | Delta |
|---|---:|---:|---:|
| `EcsContext.Update` | 438.00 | **583.99** | **+33.3%** |
| `TestCombatBehaviorSystem` | 146.13 | 226.77 | +55.2% |
| `MovementSystem` | 47.49 | 77.92 | +64.1% |
| `PotionCooldownSystem` | 13.55 | 38.17 | +181.7% |
| `ActionActivationSystem` | 3.31 | 9.30 | +181.0% |
| `DelayedActionSystem` | 5.09 | 10.65 | +109.2% |
| `ActionLockSystem` | 37.44 | 29.74 | −20.6% |
| `SimpleHealthRegenSystem` | 19.66 | 13.09 | −33.4% |
| `ContactDamageSystem` | 17.52 | 13.35 | −23.8% |
| `BodyPartEffectsSystem` | 11.04 | 8.82 | −20.1% |

**This is the correct outcome, not a regression.** The decreases are B and C working (Beyond
buckets now visited every 480 frames instead of 224; one fewer pool read per entity). The
increases are A working: distant entities were in eight-times slow motion and are now acting at
their intended rate, so every downstream system sees the actions that were previously suppressed.

The game was cheap because it was broken. **Every prior benchmark in this document, and in
`PLAN-presentation-data-layer.md`, measured a simulation that was silently skipping most of its
distant work.** They remain valid as relative comparisons but understate true simulation cost.

### P3 landed: divisors raised to [1, 16, 32, 64], plus a fifth and sixth defect

Two more defects surfaced while doing it:

**Defect E -- a fourth system missing cadence compensation.** `StatModifierExpirySystem` did a bare
`RemainingDurationFrames--` per visit regardless of tier, so a Neighborhood-tiered modifier lasted
`divisor` times longer in real time. Fixed the same way as A.

**Defect F -- `CountdownTicker` fired once per visit regardless of elapsed periods.** A 60-frame
burning tick on an entity visited every 960 frames owed 16 ticks and delivered one, so
damage-over-time was under-applied by roughly the tier divisor -- how much total damage a burning
entity took depended on how far it was standing from the player. Both `CountdownTicker` and
`MultiCountdownTicker` now loop until the visit's span is consumed. `onTick`'s signature is
unchanged (each call still means exactly one period), so no consumer needed to learn about
catch-up.

The loop fixes *magnitude* but not *interleaving* -- bulk ticks still resolve in isolation, so an
entity can die to a lump of DoT that fine-grained simulation would have offset with regen. That is
filed as a HIGH PRIORITY item in `TODO.md` ("Distant simulation fidelity"), and it should be
designed together with P2, since both touch the same code.

### Measured result of the full sequence

| | Baseline `031425` | After A-D | After divisors | After ticker fix `042040` | Net |
|---|---:|---:|---:|---:|---:|
| `EcsContext.Update` | 438.00 | 583.99 | 348.41 | **242.27** | **−44.7%** |
| `TestCombatBehaviorSystem` | 146.13 | 226.77 | 179.40 | 137.92 | −5.6% |
| `PotionCooldownSystem` | 13.55 | 38.17 | 34.93 | 9.82 | −27.5% |
| `MovementSystem` | 47.49 | 77.92 | 26.30 | 17.84 | −62.4% |
| `ActionLockSystem` | 37.44 | 29.74 | 12.66 | 8.84 | −76.4% |
| `ActionCooldownSystem` | 37.11 | 35.15 | 7.71 | 6.38 | −82.8% |
| `StatusEffectAuraSystem` | 32.40 | 36.52 | 13.11 | 13.22 | −59.2% |
| `PoisonSystem` | 10.80 | 16.58 | 15.50 | 7.70 | −28.7% |
| `ProcessingTierSystem` | 27.77 | 28.34 | 6.52 | 5.31 | −80.9% |

Simulation is **down 45% while now behaving correctly** -- distant entities act at their intended
rate, their cooldowns and durations run in real time, and their DoT totals are right.

Note the ticker fix *reduced* cost by a further 30% rather than adding any. That is the tell that
it was a real bug: timers were expiring `divisor` times too slowly, so stale cooldown, poison and
modifier components accumulated and inflated the very pools those systems scan every frame.
`PotionCooldownSystem` fell 72% on that change alone.

**Still the top two items, and both untouched by any of this:** `TestCombatBehaviorSystem` at
137.92 (57% of simulation, and still acknowledged scaffolding -- P1) and `PotionCooldownSystem`
at 9.82, still untiered with `StripeCount => 1` (P4).

### P3's original framing was wrong in one respect

Raising the divisors is now safe: with the compensation correct, a larger divisor genuinely
reduces visit frequency instead of freezing distant entities, because each visit accounts for the
frames skipped. The byte ceiling is gone, so divisors are no longer capped at `255 / baseStripeCount`.

Expect the saving to be smaller than proportional: bookkeeping cost falls with visit count, but
the *rate* of actions is now cadence-invariant, so action-execution systems will see the same
total work, just clumped into coarser bursts. Coarser bursts are themselves a gameplay-visible
change for anything the player can see -- and note P6: at Borough zoom the viewport extends
beyond the Local radius.

## P3 -- The processing-tier divisors are far shallower than the population distribution

`ProcessingTierDivisors.ByTierIndex = [1, 2, 4, 8]`. A `Beyond` entity is visited **8x** less
often than a `Local` one.

But `LocalTierRoster`'s own doc puts Local membership at "on the order of a thousand entities,"
against pools holding tens of thousands to 2.08M. The population ratio is on the order of
1:1000-1:2000; the cadence ratio is 1:8.

So the tier system, which exists precisely to stop distant entities from costing anything, is
currently reducing their cost by less than an order of magnitude.

**Investigate:** raise the `Beyond` divisor (and probably `Borough`) substantially -- 32, 64, or
higher -- and re-benchmark. This is a **one-line change to a single array** and every
`TieredEntityStripeSet` consumer picks it up automatically, which makes it by far the highest
payoff-to-effort item on this list.

**What to watch for, because this is not free:**
- Systems that compensate for cadence in their arithmetic (`SimpleHealthRegenSystem` and
  `ComplexHealthRegenSystem` both compute `framesPerVisit = StripeCount * ProcessingTierDivisors[tier]`,
  and `ActionLockSystem`/`ActionCooldownSystem` hand-roll `DecrementClamped(..., StripeCountValue)`)
  must stay correct at the new divisor. Note the hand-rolled pair decrement by `StripeCountValue`
  only, *not* by the tier divisor -- confirm whether that is deliberate or a latent bug, because it
  means a `Beyond` entity's lock ticks down 8x slower in wall-clock terms than a `Local` one's.
- `StripeCount * divisor` must not overflow the `ushort` countdown fields.
- Visible staleness: `Beyond` is far outside the viewport at Team zoom, but see P6.

**Payoff:** unknown until measured, but it scales every tiered system at once -- that is 8 of the
top 12 entries. **Risk:** low to make, moderate to validate.

---

## P4 -- Untiered systems that should be tiered

Six systems use neither `ProcessingTierWiring` nor `EntityStripeSet`:
`AchievementPollingSystem`, `AuraSourceExpirySystem`, `ContainerDestructionSystem`, `DeathSystem`,
`ParalysisSystem`, `PoisonSystem`, `PotionCooldownSystem`.

Most are genuinely trivial (`DeathSystem` 0.51, `AchievementPollingSystem` 0.35). Two are not:

- **`PotionCooldownSystem`** -- 13.55 ms/sec, `StripeCount => 1`, 9,110 instances. See P2 item 2.
- **`PoisonSystem`** -- 10.80 ms/sec, `StripeCount => 1`. See P2 item 1.

A further three cost-bearing systems use plain `EntityStripeSet` rather than tiering:
`TestCombatBehaviorSystem` (146.13), `ActionActivationSystem` (3.31), `ConsumableActivationSystem`
(2.07). Only the first matters, and it is P1.

**Investigate:** whether each untiered system is untiered *deliberately* (some genuinely must run
at full cadence for correctness -- `DeathSystem` plausibly, since deferring a death by 8 frames is
observable) or by omission. Document the answer at each site either way, so the next person does
not have to re-derive it.

### DONE for the two that mattered

Both are now tiered -- `PotionCooldownSystem` at base StripeCount 10 (matching `ActionLockSystem`,
the system it already mirrors in shape), `PoisonSystem` at 15 (matching `BurningSystem`, which is
the same stacking-DoT-through-`CountdownTicker` shape and had no reason to differ).

Measured on the *second* sampling window of each run, per the caveat at the top of this document
(two consecutive windows agreeing within noise), so these are steady-state figures rather than the
inflated first-sample ones used earlier:

| | Before | After | Delta |
|---|---:|---:|---:|
| `PotionCooldownSystem` | 8.57 | **0.81** | **−90.5%** |
| `PoisonSystem` | 4.73 | **0.80** | **−83.1%** |
| `EcsContext.Update` | 135.89 | 137.72 | +1.3% (noise) |

The ~11.7 ms/sec the two gave back is real but does not show in the total: it is smaller than the
run-to-run swing of `TestCombatBehaviorSystem` alone (37.64 → 42.24 across the same pair). Worth
doing -- these were the 2nd and 5th largest systems before the divisor work and are now negligible
-- but not worth claiming a headline number for.

The remaining untiered systems (`AchievementPollingSystem`, `AuraSourceExpirySystem`,
`ContainerDestructionSystem`, `DeathSystem`, `ParalysisSystem`) are all under 0.5 ms/sec and were
left alone. Whether that is deliberate per-site is still undocumented.

**Payoff: realised, ~12 ms/sec.** **Risk:** low, as expected.

---

## Scheduling audit -- all 27 systems, and the rule they follow

A full sweep found three more defects of the same family as A/E and closed them. Current state:

**Tiered with correct frames-per-visit compensation (18):** `ActionCooldownSystem`,
`ActionLockSystem`, `AuraSourceExpirySystem`, `BodyPartBurningSystem`, `BurningSystem`,
`ComplexHealthRegenSystem`, `ContactDamageSystem`, `ManaRegenSystem`, `MovementSystem`,
`ParalysisSystem`, `PoisonSystem`, `PotionCooldownSystem`, `SimpleHealthRegenSystem`,
`StatModifierExpirySystem`, `StatusEffectAuraSystem`, `StatusEffectImmunityExpirySystem`,
`TestCombatBehaviorSystem`, plus `BodyPartEffectsSystem`.

**Tiered, using `GetDueEntities` -- correct, because they hold no countdown of their own (3):**
- `BodyPartEffectsSystem` -- recomputes derived penalties from current body-part state. Cadence
  controls staleness of a flag, nothing accumulates.
- `DelayedActionSystem` -- *reads* `ActionLockComponent.CurrentLockFramesRemaining`, which
  `ActionLockSystem` owns and decrements. Reading a countdown is not owning one.
- `ProcessingTierSystem` -- pure tier recompute.

**Deliberately not tiered (6), each now documented at its own site:**
- `DodgeExpirySystem` -- flat StripeCount 1. Dodge is a 0.5-1s precision window; coarse steps
  would blur the thing it measures. The one timed system left untiered.
- `ActionActivationSystem`, `ConsumableActivationSystem` -- drain a queue of activations already
  committed this frame. Deferring one leaves a player input unresolved.
- `DeathSystem`, `ContainerDestructionSystem` -- event-driven queues, no per-entity population.
- `AchievementPollingSystem` -- iterates a handful of polled achievements, not entities.

### The rule, for anything new

1. Does the system iterate a per-entity population? No -> neither stripe nor tier it.
2. Must it react in the frame something was queued (player input, a death)? Yes -> plain
   `EntityStripeSet`, do not tier.
3. Otherwise -> implement **`ITieredSystem`**: build the stripe set with
   `ProcessingTierWiring.CreateAndWire`, expose it as `Tiers`, put per-entity work in
   `UpdateBucket(time, entityIds, framesPerVisit)` and **scale any time-based field by
   `framesPerVisit`**, put per-frame work in `BeginFrame`, and make `Update` exactly
   `=> TieredSystemRunner.Run(this, time)`. `SystemManager` owns the tier loop and the
   which-tiers-are-simulated policy (see `PLAN-processing-tier-rework.md`). This superseded the
   earlier form of this rule ("walk `GetTierBucket` per tier yourself, never `GetDueEntities`"):
   hand-written loops are how Defects A, E and the two found in this sweep all happened, so the loop
   no longer lives in systems at all.

### Found and fixed in this sweep

- **`StatusEffectImmunityExpirySystem`** -- tiered but decrementing `RemainingDurationFrames--` by
  1 regardless of tier, via `GetDueEntities`. Identical to Defect E. A coarse-tier immunity
  outlasted its authored duration by its divisor.
- **`ParalysisSystem`**, **`AuraSourceExpirySystem`** -- untiered whole-pool scans every frame,
  both justified in their own comments by "this population stays small". That is the exact
  justification `PotionCooldownSystem` carried until it turned out to be a top-five cost, so both
  are now tiered. No measurable saving today (both pools are near-empty), done for consistency and
  because the map is growing.

## P5 -- Architectural: two countdown mechanisms, three scheduling mechanisms

Not a performance item on its own, but it is what makes P2 and P3 hard to reason about.

**Two countdown mechanisms.** `CountdownTicker`/`MultiCountdownTicker` is used by 8 systems.
Five others hand-roll the same logic with `MathUtility.DecrementClamped`: `ActionCooldownSystem`,
`ActionLockSystem`, `DodgeExpirySystem`, `MovementSystem`, `TestCombatBehaviorSystem`. The two
hand-rolled ones that matter (`ActionLockSystem` 37.44, `ActionCooldownSystem` 37.11) are 53% of
the countdown-bookkeeping total -- so **a timer-wheel rewrite of `CountdownTicker` alone would
miss over half the cost it is aimed at.** Converging these is a precondition for P2, not a
follow-up to it.

**Three scheduling mechanisms.** `ISystem.StripeCount` alone, `EntityStripeSet`,
`TieredEntityStripeSet` via `ProcessingTierWiring`, plus `LocalTierRoster` for filtering. Each is
well documented individually and the distinction between the last two is real and deliberate (see
`LocalTierRoster`'s own doc). But there is no single place stating which a new system should pick.

**Investigate:** write down the decision rule -- one short doc, or a comment on `ISystem` -- then
audit the 27 systems against it. The table produced while researching this document (system,
`StripeCount`, tiered/striped/none) took one command to generate and immediately surfaced P4;
having it maintained rather than re-derived is most of the value.

**Payoff:** none directly; unblocks P2 and P3. **Risk:** none.

---

## P6 -- Correctness-adjacent: Local tier radius vs. maximum viewport

`ProcessingTierSystem.LocalRadiusTiles = 80` (Chebyshev). At `ZoomLevel.Borough` the tile is 9px
and `HudChrome.MapWindowSize.X` is the full backbuffer width, so `MapCamera.UpdateTileSizes` yields
`floor(1600/9) + 2 = 179` columns -- **±89 tiles from centre against a Local radius of 80.** On a
2560-wide backbuffer it is ±142.

So at maximum zoom-out the player can see entities that are being simulated at `Neighborhood`
cadence or slower -- they will visibly move in steps. P3 (raising the divisors) makes this
*worse*, which is why it is listed here rather than filed separately.

**Investigate:** whether Local's radius should derive from the maximum possible viewport extent,
or whether the outer band is simply accepted as low-fidelity. Either is defensible; the current
state is neither, because the mismatch is undocumented and the constant reads as if it were
comfortably larger than the view.

**Payoff:** none in ms. **Risk:** raising `LocalRadiusTiles` grows Local membership quadratically,
which directly increases the cost of every tiered system -- the opposite of P3. These two interact
and should be measured together.

---

## P7 -- `MovementSystem` at 47.49 ms/sec

Second-largest genuine (non-scaffolding) system. Striped at 15, tiered, over 70,066
`MovementComponent` entities. Not obviously wrong -- listed for completeness because after P1-P3
land it becomes the largest remaining item.

**Investigate:** only after P1-P3, and re-benchmark first. Its cost may be substantially different
once `TestCombatBehaviorSystem` (which runs immediately before it, on the same pool, and queues
the activations `MovementSystem` then checks for) is tiered.

---

## P8 -- Presentation coupling

From `PLAN-presentation-data-layer.md`: 57 of 159 Presentation files import `Game`, naming ~35
component types; ~517 Presentation tests are coupled to that shape. Stage 1 of that document
(narrowing the wholesale `ComponentManager` parameters in `ShellBootstrapper` to specific
`IReadOnlyComponentPool<T>`/`IEntityMembershipPool` arguments) is cheap, has no behavioural risk,
and is worth doing regardless.

**Explicitly not a performance item.** Presentation is 9.2% of the busy frame and `MapWindow` is
86% of that. There is nothing to win here in ms.

---

## Suggested order

1. **P3** -- one-line divisor change, re-benchmark. Highest payoff-to-effort by a wide margin.
2. **P1 option 3** -- tier `TestCombatBehaviorSystem`. Few lines, survives the eventual rewrite.
3. **P4** -- tier/stripe `PotionCooldownSystem` and `PoisonSystem`. One line each.
4. **P2 item 1** -- diagnose the `PoisonSystem` per-visit anomaly. Small, self-contained, and the
   answer probably generalises to the whole countdown family.
5. **P5** -- write the scheduling decision rule and converge the two countdown mechanisms.
6. **P2 proper** -- the timer-wheel rewrite, only if steps 1-5 leave it justified. They may not:
   raising the divisors alone could take a large bite out of the same cost.
7. **P6**, **P7**, **P8** -- after the above, and re-measure before each.

Steps 1-4 are all one-to-few-line changes with an immediate benchmark. **Run
`/phase-performance-testing` between each one, not after all of them** -- with a ~10% noise floor,
batched changes cannot be attributed.

## A caution drawn from this investigation

Two doc comments in this codebase have now been found asserting performance characteristics that
measurement contradicted or failed to support:

- `MapTileLayerCache` on the glow grid changing "essentially never" -- this one turned out
  **correct**, but only after instrumentation; the inference that it was wrong (recorded and
  retracted in `PLAN-presentation-data-layer.md`) came from trusting a cost number over a
  mechanism.
- `PotionCooldownSystem` on its population being "already small" against 9,110 instances -- not
  yet re-measured, but the stated premise does not match the memory dump.

The codebase's doc comments are unusually rich and mostly load-bearing, which is a strength. But
performance claims in them age differently from design rationale: the world composition changes
underneath them. **Where a comment justifies a design choice with a claim about scale, it is worth
citing the measurement and its date.** Several already do this well (`MapTileLayerCache` quotes
its own before-figures; `ActionTargetingController` cites a live diagnostics capture and the
incident write-up). That convention is worth applying to the rest.
