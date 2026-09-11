# Presentation Data Layer

(Investigation, pre-implementation. Follow-up to the `MapWindow` optimization in `7f006e4`.
Stage 0 has been run -- see "What the profile actually says". Its result cut Stage 3 from the
plan; sections written before it are marked where superseded.)

## Question

Should the per-window caches that have started appearing in `Presentation` (`MapTileLayerCache`,
`MapBackgroundCache`, `MapTintGrid`) be replaced by a single runtime cache layer -- a reactive
event queue populating presentation-shaped view models -- sitting between `Game` and
`Presentation`? And should there be a symmetric query layer between `Engine` and `Game`?

## Verdict

**Not in that form.** A push-based, event-driven view-model store is the wrong mechanism for this
codebase, for four independently sufficient reasons (measured/verified below):

1. **It targets the wrong 9%.** (Measured post-`7f006e4`; see below.) Presentation is 42.95 ms/sec
   of a 468 ms/sec busy budget, and 37 of that 43 is `MapWindow` alone. The caches the layer would
   "replace" are worth **5.94 ms/sec combined** -- 1.3% of the busy frame.
2. **Push feeds far more than pull consumes.** The map draws ~1,650 tiles at Team zoom. A
   push cache would be fed by every one of ~70,000 `MovementComponent` entities. Pull is already
   filtered by visibility; push is not.
3. **The event substrate does not exist.** `Game` defines 20 event types. `Presentation` reads
   ~35 distinct component types. `DirectComponentPool.Get` hands out a mutable `ref T` with no
   notification hook, so push correctness is not even expressible without removing that.
4. **The synchronisation payoff isn't available.** Bevy's extract step -- the closest industry
   analogue -- exists to overlap render work with next-frame simulation on another thread. This
   game is a single-threaded `Update` → `Draw` inside one `GameLoop.Update`/`Draw` pair. Without
   the thread boundary, extract is copy cost with no compensating win.

**What is worth doing:** a *pull-based read/query layer* (`Game.Views`) for coupling and
testability -- option **D** below. It is incremental, has no staleness class, and does not touch
the 90% of the frame that is simulation. It carries **no performance claim**; it is an
architecture change and should be justified as one.

**What Stage 0 removed from the plan:** the viewport-caching work (option E / Stage 3). Its
estimated ceiling was ~85 ms/sec; the measured total for all of `MapWindow` is **37 ms/sec idle
and 35 while panning**, much of it irreducible draw submission. Scrolling -- the one case the
first sample could not see, and the one the Factorio precedent is about -- adds ~1 ms/sec. Cut.

---

## What the profile actually says

**Stage 0 has been run** (`Log/phase-benchmarks/20260910-023910.json`, commit `7f006e4`, 8 samples
at 5s, idle camera). Current figures, in ms per second of wall clock, against the pre-optimization
baseline (`20260908-233652.json`, commit `796ae3d`):

| Region | Now | Was | Share of busy now |
|---|---:|---:|---:|
| `EcsContext.Update` (all systems) | 421.44 | 619.34 | **90.1%** |
| `Shell.Draw` (all of Presentation) | 42.95 | 121.47 | **9.2%** |
| ...of which `MapWindow` | 37.01 | 109.51 | 7.9% |
| ...of which everything else | 5.94 | ~11.96 | **1.3%** |
| `Shell.Update` | 3.58 | 5.46 | 0.8% |
| **Total busy** | **467.97** | 746.27 | |

At 60 fps that is 7.8 ms of a 16.67 ms frame, with `MapWindow` costing 0.62 ms/frame.

### The cross-run comparison is confounded -- read ratios, not deltas

Every system fell ~30%, including ones `7f006e4` never touched: `PotionCooldownSystem` -69%,
`TestCombatBehaviorSystem` -38%, `ActionCooldownSystem` -15.5%, `StatusEffectAuraSystem` -14%.
`Draw.Tooltip.Tooltip` fell 98.9% and `Draw.StaticHud.InspectionWindow` 89.5%, which means the two
runs had different UI state open. And `7f006e4` itself introduced deterministic seeding, so the two
runs are different worlds with different populations near the player.

So absolute deltas are not attributable to the optimization. What survives:

- `MapWindow` fell 66.2% against a ~32% baseline drift, i.e. roughly a further **50% reduction**
  plausibly attributable to `MapTileLayerCache`. `MapTileLayerCache`'s own doc predicted ~23%
  (24.9 of 109.51 ms/sec). It did at least as well as claimed; the exact figure can't be pinned
  from a confounded pair.
- **Presentation's share of the busy frame fell from 16.3% to 9.2%.** That direction is robust.

### Scrolling was measured separately, and costs almost nothing

The first sample was taken with a static camera -- `MapTileLayerCache`'s best case, warm and never
invalidated. A follow-up pair of samples was taken in **one process with a pinned `--seed=1`**
(same world, same warm state, only the camera differing), the second while a right-drag pan was
held continuously for the sampling window:

| | Idle | Scrolling | Delta |
|---|---:|---:|---:|
| `Draw.Base.MapWindow` | 35.56 | 34.38 | **-3.3%** |
| `Update.Base.MapWindow` (where `EnsureRendered` runs) | 8.18 | 10.37 | **+2.19 ms/sec** |
| **MapWindow total** | **43.74** | **44.75** | **+1.01 ms/sec (+2.3%)** |

Continuous scrolling costs about **1 ms/sec** -- 0.017 ms/frame at 60 fps. Not multiples of idle,
not anything. The scroll-shift optimization (option E item 1) would target at most the 2.19 ms/sec
of extra `EnsureRendered` work and would recover only a fraction of that. **Stage 3 is closed.**

That pair also calibrates the noise floor: within the same process and seed, `EcsContext.Update`
varied 8.7% and `ActionLockSystem` 24% between two consecutive 40-second samples. So **anything
under ~10% on the large items is unresolvable by this harness**, which retroactively confirms that
the cross-session "everything fell 30%" comparison above was mostly confounding.

### The glow cache does NOT thrash -- an earlier claim in this document, now disproven

An earlier revision of this section claimed the glow cache was rebuilding repeatedly, on the basis
that `Update.Base.MapWindow` measured 0.70 ms/sec in one session's world and 8.18 in another. That
inference was wrong, and direct instrumentation disproved it.

`MapWindow.Update` was temporarily instrumented with per-region `IFrameCostRecorder` probes plus
counters on the glow-invalidation branch, and sampled idle on the seed-1 world:

| Probe | ms/sec |
|---|---:|
| `Update.Base.MapWindow` (total) | 0.90 |
| — `UpdateHoveredTile` | 0.64 |
| — `TerrainEnsureRendered` | 0.06 |
| — `GlowEnsureRendered` | 0.01 |
| — `CameraFollow` | 0.01 |
| `GlowInvalidations/sec` | **0 -- never fired** |
| `TintSplats/sec` | **0 -- never fired** |

`MapTintGrid.Version` did not move once across a 40-second sample, and both `EnsureRendered` calls
together cost 0.07 ms/sec. **`MapTileLayerCache`'s stated assumption is correct as written.** Only
`Game/Blueprints/Terrain/Lava.cs` grants `StatusEffectAuraSourceComponent` at blueprint time, and
`MovementSystem` publishes `EntityMovedEvent` on the shared `EventBus` only for the player or for
movers that themselves carry an aura source (`MovementSystem.cs:198`) -- so with lava as the only
source, nothing ever re-splats.

Note also that `Version++` in `MapTintGrid.Splat` is unconditional and viewport-unaware: a splat
anywhere on the 1000x1000 map invalidates the whole glow texture. That is a latent sharp edge if
a moving aura source is ever introduced (a torch, a fire elemental), but it is not live today.

### The residual anomaly, unexplained and bounded

Re-running the same seed produced 0.90 ms/sec where the earlier session measured 8.18, so the
elevated figure was not a property of the world. Two hypotheses were tested and both failed:
the glow cache (above), and the mouse cursor resting over the map (`UpdateHoveredTile` 0.76 with
the cursor parked over the viewport vs 0.64 without -- inside noise).

What does correlate, across all three sessions, is `Draw.StaticHud.InspectionWindow`:

| Session | `Draw.StaticHud.InspectionWindow` | `Update.Base.MapWindow` |
|---|---:|---:|
| 1 (`023910`) | 0.23 | 0.70 |
| 2 (`025409`) | 1.79 | 8.18 |
| 3 (`031203`) | 0.24 | 0.90 |

Session 2 was also ~14% hotter overall (1309.8 vs 1150.3 ms/sec summed), but `Update.Base.MapWindow`
fell 89% against that drift, so the correlation is not just general noise. The plausible mechanism
is that something was armed or selected in session 2: `ActionTargetingController.RefreshTargetableTiles`
runs "every frame something is armed" and re-scatters when the caster moves, which is the only
path in `UpdateHoveredTile` that can scale. **This is a correlation across three points, not a
proven mechanism** -- reproducing it needs an armed action plus a moving caster, which was not
attempted.

Bounded at ~7 ms/sec, or 1.5% of the busy frame, on a path that only runs while targeting. Not
worth further pursuit at that size; recorded so the next person who sees `Update.Base.MapWindow`
jump has the three data points and the two dead ends already.

### The shape of the number is the point

- Presentation is **not** the bottleneck. Simulation is, by 10x now (was 5x).
- Within Presentation, one class is **86%** of the cost.
- The entire rest of the UI -- every HUD window, inspection pane, hotbar, tooltip, inventory grid
  -- costs **5.94 ms/sec together**, 1.3% of the busy frame.

A general cache layer spanning all of Presentation would be built to reclaim ~6 ms/sec. A reactive
push cache would add cost to a 421 ms/sec simulation path in order to do it.

## What caches actually exist today

The premise ("multiple window and controller specific caches") is thinner than it looks. Grepping
Presentation for cache/dirty/invalidate machinery returns 10 files. Classified:

**Game-data caches -- 3, all `MapWindow` collaborators:**
- `MapTileLayerCache` -- rendered terrain/glow textures
- `MapBackgroundCache` -- per-visible-tile background colour, scroll-shifted
- `MapTintGrid` -- per-cell aura glow, incrementally splatted

**Not game-data caches -- 7:**
- `LabelRenderer._inkCenterCache` -- font ink metrics, keyed by (font, glyph). Pure render resource.
- `ElementPoolService` -- element instance pooling.
- `MapCamera` -- viewport geometry, not cached data.
- `MapWindow._chargeFillElapsedFrames` -- presentation-*derived* animation smoothing state that has
  no source in the ECS at all (see its own doc comment: it counts real elapsed frames precisely
  *because* `ActionLockComponent`'s stepped countdown is the wrong signal). A view-model layer
  cannot subsume this; it isn't a view of anything.
- `InspectionWindowContent._lastDetailEntityId` / `_lastBasicPosition` -- rebuild guards ("has the
  selection changed since last frame"), not data caches.
- `HealthWindow._bodyFont`, `ItemDetailsWindow._nameFont` -- captured font sizes, defending against
  `ElementPoolService` recycling. Unrelated.

So there is no sprawl to consolidate. There is **one hot class with three collaborators**, all
scoped to the same viewport, all invalidated from the same handful of points that `MapWindow.
InvalidateTerrainCaches` already centralises.

---

## Options

### A. Status quo -- direct pull, targeted caches where measured

What `7f006e4` did. Presentation reads `ComponentManager` pools directly; caches appear where a
profile says they should.

- **Cost:** nothing. **Risk:** nothing.
- **Weakness:** the coupling. 57 of 159 Presentation files `using Game.*`; ~35 distinct component
  pool types referenced across them. Every gameplay component-shape change ripples into UI code
  and its ~517 Presentation tests.
- **Verdict:** correct on performance, unsatisfying on architecture. The architecture complaint is
  the real one, and it is not solved by caching.

### B. Reactive push cache → view models (the proposal)

`Game` raises an event on every state change; a cache layer between Game and Presentation
materialises view models; Presentation reads only those.

Blockers, in order of severity:

1. **Event coverage.** 20 event types exist (`Game/**/*Event.cs`). Presentation reads ~35
   component types. You would author ~40 new event types and find every mutation site for each.
2. **No write chokepoint.** `DirectComponentPool.Get(int)` returns `ref T`. `TryUpdate`/`TrySet`
   are used ~50 times in `Game` and *could* notify; the `ref` escape hatch cannot, and is used
   (`DeathSystem`, `FloorBuilder`, `TestMapBuilder`). Removing it means rewriting the mutation
   API used by the 62%-of-frame simulation path, to benefit the 12% draw path. Nystrom's own
   guidance on this pattern is that you must "encapsulate modifications to the primary data behind
   a single narrow API so that the dirty flag gets set there and won't be missed" -- this codebase
   deliberately does not have that, for performance reasons.
3. **Write amplification into the hot path.** `MovementSystem` is 65.9 ms/sec across ~70k movers.
   `EntityMovedEvent` dispatch currently costs 0.19 ms/sec because its subscriber set is trivial.
   A subscriber that maintains per-entity view models turns that into real per-move work, on every
   mover, whether or not it is anywhere near the camera. You would spend simulation budget to save
   draw budget, at an unfavourable ratio.
4. **Fan-in ratio is backwards.** Visible tiles at Team zoom: ~1,650. `LocalTierRoster` membership:
   ~1,000. `MovementComponent` population: ~70,000. `TransformComponent` population: 2,082,027.
   Pull is filtered by the camera for free. Push has to be filtered explicitly, and the only
   available filter (tier) does not match the camera -- see the striping section.
5. **Staleness is a new bug class.** Every missed publish becomes a UI that silently shows the
   wrong thing, with no failing test and no crash. The standard mitigation in event-driven state
   replication is versioned snapshots with periodic full resync -- i.e. you end up re-adding the
   pull path you were trying to remove, as a repair mechanism.

- **Verdict:** rejected. Not "expensive"; structurally mismatched.

### C. Pull-based per-frame extract/snapshot (Bevy's model)

Once per frame, walk what's visible and build view models; everything draws from those. No events,
no invalidation -- rebuilt wholesale each frame, so it cannot go stale.

- **Honest appraisal:** this is the *good* version of the idea, and it is much closer to industry
  practice than B. It has no invalidation risk at all.
- **But:** Bevy's render world exists to enable pipelined rendering -- "rendering that frame
  happens at the same time simulation of the next frame is happening." The cost (an explicit copy,
  plus per-type extraction boilerplate, which is the one tradeoff the Bevy community consistently
  names) is paid for by thread overlap.
- **Here:** `GameLoop.Update` runs `EcsContext.Update` then `Shell.Update`; `GameLoop.Draw` runs
  `Shell.Draw`. Same thread, same frame, strictly ordered. Nothing overlaps. An extract step buys
  decoupling only, and pays a full copy of the visible set every frame for it.
- **Verdict:** viable but currently unprofitable. **Revisit if simulation is ever moved off the
  render thread** -- at that point this becomes the right answer and B still isn't.

### D. Query layer -- pull, no caching, presentation-shaped

A named read API over the pools that returns presentation-shaped structs assembled on demand:

```csharp
// Game/Views (new namespace)
public readonly record struct TileView(int BlockingEntityId, ReadOnlySpan<int> Occupants, ...);
public readonly record struct EntityVisualView(SpriteRef? Sprite, char Glyph, Color GlyphColor,
                                               bool IsDead, float? HealthFraction, ...);

public interface IMapViewQuery
{
    TileView GetTile(int x, int y, int layer);
    bool TryGetVisual(int entityId, out EntityVisualView view);
}
```

- Fixes the coupling: Presentation stops naming `SimpleHealthComponent`, `DeadComponent`,
  `LootedComponent`, `ContainerComponent` and so on -- it names `EntityVisualView`.
- No cache, so no invalidation, no staleness, no memory.
- Composes the 5-7 scattered per-entity pool reads `TryDrawEntityVisual`/`DrawEntityIcons` do
  today into one call, which is *also* where a future struct-of-arrays or hot-field-packing
  optimization would land. The current comment in `TryDrawEntityVisual` about avoiding "one wasted
  scattered read into an entity-indexed array for every sprite-backed entity" is exactly the kind
  of micro-optimization that becomes systematic once there is one place that resolves a visual.
- **Cost:** indirection. Struct returns must stay `readonly record struct` / `ref readonly` to
  avoid allocating on the draw path. `ReadOnlySpan<int>` for occupants can be returned directly
  from `Map`'s own array, as `GetOccupantEntityIdSpanAt` already does.
- **Verdict: recommended.**

### E. Viewport-scoped caching in MapWindow -- extend what `7f006e4` started

**Superseded by Stage 0, except for item 1.** This section was written against an estimated ~85
ms/sec of remaining `MapWindow` cost. The measured figure is **37 ms/sec on an idle camera**, so
items 2-4 below no longer clear the bar and are retained only as a record of what was considered.

The remaining cost is `DrawOccupants` and `DrawTargetingHighlights`, neither of which
`MapTileLayerCache` can cover (occupants move; highlights follow the cursor).

The industry precedent here is Factorio's terrain pipeline, and it points somewhere specific:
**cache draw orders, not textures.** Their reasoning is that a draw-order cache survives a change
of render scale, so zooming does not invalidate the whole view -- whereas a texture cache does.
`MapTileLayerCache` today invalidates completely on zoom (`gridChanged` reallocates the
`RenderTarget2D`). Factorio also scrolls the cached buffer by tracking its top-left offset so that
pixel work is "proportional to how much the terrain scrolled" -- which is precisely what
`MapBackgroundCache.ApplyScroll` already does for colours but `MapTileLayerCache` does not do for
the texture (a whole-tile scroll fully invalidates it).

Concrete candidates, in expected value order:

1. **Scroll the terrain render target instead of rebuilding it.** Blit the retained region to its
   shifted position and re-render only the newly exposed columns/rows. Directly mirrors
   `MapBackgroundCache.ApplyScroll`, which is already proven in this codebase.
2. **Chunk the terrain cache.** Per-chunk render targets keyed by map coords, kept across scroll
   and reused on scroll-back. Makes `TerrainChangedEvent` invalidate one chunk instead of the view.
3. **Occupant draw-order cache gated on the visible set.** Only entities that moved, changed
   sprite/glyph, died, or gained/lost a badge need re-resolution. This is a dirty-flag cache, and
   it is the one place where the event-driven idea *does* earn its keep -- because the invalidation
   set is small, positional, and already has an event (`EntityMovedEvent`).
4. **Verify first.** `DrawOccupants`' cost may be dominated by `ContrastTextRenderer`'s
   nine-`DrawString` outlined glyph path rather than by pool lookups. If so, a data cache saves
   nothing and the fix is a glyph atlas.

   **This question is now moot.** It existed to choose between a data cache and a glyph atlas.
   At 37 ms/sec for the whole of `MapWindow`, neither is worth building, so instrumenting
   `DrawOccupants` to decide between them would itself be wasted work. Revisit only if the
   scrolling measurement (item 1) comes back expensive.

- **Verdict: dropped, except item 1 pending a scrolling measurement.**

### F. Recommended: D (was D + E)

Query layer for the architecture problem. The performance problem it was paired with turned out
not to exist at a scale worth addressing -- which is itself the useful outcome of Stage 0, and a
reminder that the two were always separate problems. Conflating them is what makes the
reactive-view-model proposal attractive in the first place.

### Comparison

Perf-win column revised against the Stage 0 measurement:

| | Fixes coupling | Perf win | Staleness risk | Cost to build | Cost to sim path |
|---|---|---|---|---|---|
| A status quo | no | n/a | none | none | none |
| B reactive push | yes | ≤6 ms/sec | **high** | very high | **adds cost** |
| C per-frame extract | yes | none today | none | high | adds copy |
| **D query layer** | **yes** | **neutral** | **none** | **medium** | **none** |
| E viewport cache | no | ≤37, idle-case | low, local | low-medium | none |

---

## Should it be a separate project?

**Eventually yes; not on day one.**

The goal of the layer is that Presentation cannot reach past it. That is only *enforced* if
Presentation stops referencing `Game` entirely -- which means the view types and query interfaces
must live in an assembly that Presentation can see and `Game` cannot be reached through:

```
Engine → Game → Game.Views → Presentation → DungeonCrawlerWorld
```

with `Presentation.csproj` referencing `Game.Views` and `Engine`, but **not** `Game`.

But creating that project on day one forces a big-bang migration of 57 Presentation files and
~517 Presentation tests, all at once, with no intermediate state that builds. That is the
"massive rewrite" failure mode.

**Staging:**

1. `Game/Views/` namespace inside the existing `Game` project. Presentation still references
   `Game`, so migration is file-by-file and the build is green throughout.
2. Migrate consumers incrementally, starting with the ones with the fewest pool types.
3. Add an architecture test asserting no `Presentation` type names a `*Component` type -- this is
   the enforcement mechanism during the transition, and it can be turned on per-namespace.
4. When the last consumer is migrated, move `Game/Views/` into `Game.Views.csproj` and drop
   Presentation's `Game` reference. Mechanical, one commit.

Do **not** put the layer in `Presentation` -- that leaves Presentation referencing `Game` forever
and achieves nothing but relocation. Do **not** put it in `Engine` -- it is game-specific by
definition, and `Engine` has "no game-specific knowledge" per `CLAUDE.md`.

## Should there be a query layer between Engine and Game?

**No.** The symmetry is superficial.

- The Game→Presentation boundary needs *shape translation*: the ECS's storage layout (one array
  per component type, entity-indexed) is nothing like what a UI consumes (one bundle per visible
  thing). That mismatch is what a view layer resolves.
- The Engine→Game boundary has no such mismatch. `Game` *is* the schema owner -- it defines every
  component type and registers every pool. `ComponentManager.GetDirectPool<T>()` already is the
  query layer, and it is a typed, zero-indirection one.
- A generic query layer over `Engine` would be either (a) the pool API with an extra hop, or
  (b) a dynamic archetype/view system in the style of Flecs or EnTT. This ECS deliberately isn't
  that: it uses fixed registered pools plus `EntityStripeSet`/`TieredEntityStripeSet` for
  scheduling. Retrofitting archetype queries would cost in the 619 ms/sec simulation budget to
  serve reads that are already O(1).

If something is wanted at that boundary, it is not a query layer -- it is *narrowing*: pass
`IReadOnlyComponentPool<T>` / `IEntityMembershipPool` (both already exist) instead of the whole
`ComponentManager`. Note that `ShellBootstrapper` currently hands `ComponentManager` wholesale to
`DebugWindowContent`, `PlayerHealthBarContent`, `InspectionWindowContent`, `HotbarContent`,
`HealthWindowController`, `UiInputController` and others. Tightening those signatures is cheap and
independently worthwhile.

## How does tier-based striping interact?

This is the sharpest constraint, and it produces a hard rule.

**Striping is a cadence mechanism. Caching presentation data on a cadence means showing stale
data for that cadence.** `TieredEntityStripeSet` visits `Count/StripeCount` entities per call, per
tier, with each tier's divisor multiplying its stripe count. An entity's view model refreshed on
its tier's cadence would lag its true position by that many frames. For anything positionally
rendered, that is a glyph frozen at the wrong tile. Unacceptable.

**Rule: use the tier system as a *filter*, never as a *cadence*, for presentation data.**

`LocalTierRoster` is exactly the filter form, and its own doc comment already prescribes this:
"a purely visual consumer should use this only to shrink its candidate set and then reject on the
camera's own bounds." `ActionTargetingController.AllPendingDelayedActionTargets` is the existing
precedent. Membership is ~1,000 against pools of tens of thousands.

**But there is a verified bug in that plan, and it exists today.**

`ProcessingTierSystem.LocalRadiusTiles = 80` (Chebyshev, player's Z only). Viewport width:
`HudChrome.MapWindowSize.X = screenSize.X` = 1600 at the default backbuffer. At `ZoomLevel.Borough`
the tile is 9 px, so `MapCamera.UpdateTileSizes` yields `floor(1600/9) + 2 = 179` columns -- **±89
tiles from centre, against a Local radius of 80.** Local does *not* contain the viewport at max
zoom-out on the default window, and the gap widens on any wider monitor (at 2560 px it is ±142
against 80).

Consequences:

- Any cache gated on `LocalTierRoster` silently drops the outer ~9 tile columns at Borough zoom.
  Entities there would not render.
- `ProcessingTierSystem` is itself striped (28.45 ms/sec), so tier membership *lags* reality even
  inside the radius. The 16-tile `LocalExitBufferTiles` hysteresis absorbs some of that, but the
  margin is a load-bearing invariant, not a comfortable one.

So if tier-gating is used at all, it needs one of:

- **(a)** Derive the Local radius from the maximum possible viewport half-extent rather than
  hardcoding 80 -- i.e. make the gameplay constant depend on a presentation concern, which is a
  layering violation and would raise `ProcessingTierSystem`'s cost.
- **(b)** Don't tier-gate. Gate on the camera directly. The camera already gives an exact
  rectangle, and iterating it is ~1,650 tiles at Team zoom / ~11,800 at Borough -- both cheap, and
  both *exactly* the right set. This is what `DrawOccupants` does today.
- **(c)** Tier-gate only non-positional consumers (a party panel, a threat list, a "nearby
  entities" pane) where a bounded lag is invisible.

**(b) is correct for the map.** The camera rectangle is a better filter than the tier system for
every visual purpose, because it is the actual question being asked. The tier system's value is to
the simulation, and it should stay there.

Independent of any of this: **the Borough-zoom/Local-radius mismatch is worth filing on its own.**
It is a real gameplay-adjacent inconsistency (entities visible on screen but at Neighborhood
processing cadence) that this investigation surfaced.

## Effect on input capture and propagation?

Three real effects, one of which is a design-principle violation.

### 1. Input must never resolve against the cache

`MapWindow.SelectMapNodes` / `TryOpenEntityContextMenuAt` / `ActionTargetingController.
UpdateHoveredTile` resolve screen coords → tile → entity id against live state (`_world.Map.
GetBlockingEntityId`). If they instead resolved against a view cache that lagged by even one frame,
a click could land on an entity that has already moved -- the player clicks a goblin and attacks
an empty tile.

`CLAUDE.md`'s stated design principle is explicit about this: "remove ambiguity, remove unexpected
actions... don't guess -- either resolve it to something concrete first, or refuse it outright."
Acting on a stale target is precisely guessing.

**Rule: the view layer is for drawing. Input resolves against `World`/`Map` directly, always.**

This is not a limitation of the design -- it is how it should be stated. Draw is the only consumer
that tolerates a frame of derived data; input and gameplay are not.

### 2. Ordering constraints multiply

`GameLoop.Update` already has a precise order: `Shell.PreSimulationUpdate` → `EcsContext.Update`
(skipped when paused) → `Shell.Update`. Inside `MapWindow.Update` the ordering is already delicate
enough to need a doc comment -- camera-follow must run *before* `EnsureRendered` so the cache
rebuilds against the new scroll position, and `EnsureRendered` must run in `Update` rather than
`Draw` because `GameLoop.Draw` has already begun the shared `SpriteBatch` pass.

Every cache added multiplies these. A cache populated in `Shell.Update` alongside input handling
means the answer to "does input see this frame's data or last frame's" depends on statement order
within one method. That is a maintenance hazard, and an argument for keeping the number of caches
small and their population points few -- i.e. for option E's *scoped* caching over option B's
*global* store.

Note also that the simulation is skipped entirely while paused or in menu mode, but `Shell.Update`
is not. Any cache-population step has to be correct on frames where the world did not advance.

### 3. Write-back and optimistic display

Presentation writes back through `EventBus` and the drag-drop resolvers (`PlainInventoryDragDropResolver`,
`ShopDragDropResolver`, `TradeDragDropResolver` → `InventoryActions`). Today a drop mutates
component state and the next draw reads it directly, so the change appears immediately.

With a push-populated cache, the round trip becomes: UI writes → system processes → event fires →
cache updates → UI reads. That is at least one frame of lag on every drag, drop, equip, and trade
-- visible, and exactly the class of "did that work?" ambiguity the design principles reject. The
alternatives are optimistic local updates (which reintroduce a second source of truth) or
synchronous write-through (which defeats the decoupling).

A pull-based query layer (D) has none of this: the write lands, and the next read sees it.

---

## Staged plan

Each stage is independently shippable and independently abandonable.

**Stage 0 -- Re-measure. DONE** (`20260910-023910.json` idle; `20260910-025409.json` /
`20260910-025505.json` the seed-pinned idle-vs-scrolling pair, all under
`Log/phase-benchmarks/`). Result:
`MapWindow` 37.01 ms/sec, all other Presentation 5.94, simulation 421.44. Presentation is 9.2% of
the busy frame. **This kills Stage 3** and moots the `DrawOccupants` sub-question. It does not
affect Stages 1, 2, 4 or 5, which were never justified on performance -- but it does mean they now
have to stand entirely on the coupling argument, with no performance story attached at all.

**Stage 1 -- Narrow the bootstrap signatures.** Replace wholesale `ComponentManager` parameters in
`ShellBootstrapper` with the specific `IReadOnlyComponentPool<T>` / `IEntityMembershipPool` each
consumer needs. Pure refactor, no behaviour change, immediately reduces the coupling surface and
makes the next stage's scope visible. ~10 call sites.

**Stage 2 -- `Game/Views/` for the map draw path only.** `IMapViewQuery` + `TileView` +
`EntityVisualView`, consumed by `MapWindow`/`MapBackgroundCache`. Chosen first because it is the
densest coupling (14 pool types in one class) and the hot path, so it validates both the ergonomics
and the zero-allocation constraint at once. Add the architecture test, scoped to `MapWindow`.

**Stage 3 -- Viewport caching. CUT.** The open question (was scrolling expensive?) has been
measured and answered: continuous panning costs ~1 ms/sec against a 468 ms/sec busy frame. The
scroll-shift idea from Factorio's FFF-333 has nothing left to recover here. Closed permanently.

**Follow-up: chased and closed.** The `Update.Base.MapWindow` variance (0.70 vs 8.18 ms/sec) was
investigated with temporary instrumentation. The glow cache was not the cause -- it never
invalidated at all -- and neither was cursor hover. See "The glow cache does NOT thrash" and "The
residual anomaly" above. Nothing actionable came out of it; no code changed.

**Stage 4 -- Migrate remaining consumers.** HUD windows, inspection, hotbar, inventory. No
performance motivation; do this only for the coupling, and only if Stages 1-2 proved the pattern
pleasant to work with. ~517 Presentation tests are in scope here; expect this stage to dominate
the total cost.

**Stage 5 -- `Game.Views.csproj`.** Move the namespace out, drop Presentation's `Game` reference,
delete the architecture test (the compiler now enforces it).

**Explicitly not planned:** an event-driven view-model store, a per-frame full extract, a query
layer between Engine and Game, and any presentation cache driven by tier cadence.

## What would change this verdict

- **Simulation moves off the render thread.** Then option C (extract) becomes correct and its copy
  cost is paid for by overlap. Design Stage 2's view types so they *could* be snapshotted -- keep
  them value types with no back-references into pools -- so this stays open.
- **A second view of the same world appears** (minimap, overview map, replay, spectator). Two
  consumers of the same derived data is the classic threshold where materialising it pays.
- **Simulation optimization succeeds far enough to invert the ratio.** Presentation is 9.2% of
  the busy frame today. `EcsContext.Update` would have to fall from 421 to roughly 130 ms/sec --
  a further 70% cut on top of what has already landed -- before Presentation reached a quarter of
  the budget. Not impossible (`TestCombatBehaviorSystem` alone is 134 ms/sec and is test scaffolding),
  but it is the precondition, and it should be re-measured rather than assumed.
*(The scrolling measurement, previously listed here, has been taken -- it came back at ~1 ms/sec
and revives nothing.)*
- **Multiplayer or a networked/authoritative split.** Then a replicated read model is not an
  optimization, it is the architecture, and the event-sourced version becomes right for reasons
  that have nothing to do with frame cost.

## References

- [Render Architecture Overview -- Unofficial Bevy Cheat Book](https://bevy-cheatbook.github.io/gpu/intro.html) -- the extract step as a fixed synchronisation point between two Worlds.
- [What's the render world and why does it exist? (Bevy discussion #13494)](https://github.com/bevyengine/bevy/discussions/13494) -- extract exists for pipelining; the boilerplate cost is the acknowledged tradeoff.
- [Factorio FFF-333: Terrain scrolling](https://www.factorio.com/blog/post/fff-333) -- scrolling a cached buffer by tracking its offset so pixel work is proportional to scroll distance.
- [Factorio FFF-323: Animated water](https://factorio.com/blog/post/fff-323) -- caching per-chunk draw orders rather than textures, so render scale changes don't invalidate.
- [Dirty Flag -- Game Programming Patterns](https://gameprogrammingpatterns.com/dirty-flag.html) -- when the pattern pays, and the missed-invalidation failure mode.
- [UMG Viewmodel for Unreal Engine](https://dev.epicgames.com/documentation/en-us/unreal-engine/umg-viewmodel-for-unreal-engine) -- push-model MVVM for game UI; note the manual `UE_MVVM_BROADCAST_FIELD_VALUE_CHANGED` requirement, the same "forget to publish and the UI silently lies" hazard.
- [Design decisions when building games using ECS -- Ariel Coppes](https://arielcoppes.dev/2023/07/13/design-decisions-when-building-games-using-ecs.html) -- pull for hot loops where many entities change per frame; events for sparse or bursty change.
- [Proving Immediate Mode GUIs are Performant -- Forrest Smith](https://www.forrestthewoods.com/blog/proving-immediate-mode-guis-are-performant/) -- immediate-mode UI reads game state directly and needs no synchronisation layer, at competitive cost.
- [Why real-time frontends break at scale -- LogRocket](https://blog.logrocket.com/real-time-frontends-break-scale/) -- missed-event detection and periodic full resync as the standard repair for event-driven read models.
