# Charge attack fill indicator

(Design plan, saved for implementation per this repo's convention -- see `PLAN-body-parts.md`/
`PLAN-player-health-hover.md`. Addresses TODO.md's top-priority "Enemy Attack Indicator" entry --
the timing indicator for a charging Delayed action, generalized to both player and enemy, that the
Combat Overhaul: Dodge work called out as still missing when it landed the flat-wash telegraph.)

## Context

`TODO.md`'s original note (now reworded, but visible in the file's own uncommitted diff) asked for
"the tile equivalent of the radial fill that counts it down" -- i.e. the same countdown-fill visual
`ActionLockContent`'s HUD wheel already gives a charging *player*, but per-tile on the map, and
visible for every entity (enemies included), not just the player's own HUD.

Today's telegraph (landed under Combat Overhaul: Dodge, `IMPLEMENTATION-NOTES.md`) already draws
*which* tiles a Delayed action will hit, the whole time it's charging, but not *how close* it is to
landing:

- `MapWindow.DrawTargetingHighlights` (`Presentation/UI/MapWindow.cs:419-463`) draws a flat,
  uniform-alpha wash per target tile via `DrawMaskedTileHighlight` (line 549) -- `PlayerTargetColor`
  (dark green) for the player's own queued action (`ActionTargetingController
  .PendingDelayedActionTargetTiles`), `EnemyUndodgeableColor`/`EnemyDodgeableColor` (red/yellow, by
  `Tag.Dodgeable`) for every other entity's (`ActionTargetingController
  .AllPendingDelayedActionTargets()`). Same wash from the first frame of the windup to the last.
- `MapWindow.DrawChargingBadge` (line 862) separately badges the charging entity's action
  sprite/glyph above its own footprint -- also unchanged for the whole windup.

Neither conveys progress. A player watching a goblin's red tile has no way to judge "I have a full
second to react" vs. "this is about to land" without watching the badge and doing frame-counting in
their head.

The progress data already exists, just not read by `MapWindow` yet: `ActionLockComponent`
(`Game/Modules/Core/Components/ActionLockComponent.cs`) -- `StandardLockFrames`/
`CurrentLockTotalFrames`/`CurrentLockFramesRemaining`, all `ushort` -- is set once by
`ActionLockGate.Lock` the same tick `ActionActivationSystem.TryActivateDelayed`
(`Game/Modules/Actions/Systems/ActionActivationSystem.cs:189-199`) creates the entity's
`PendingDelayedActionComponent`, and `ActionLockSystem` decrements `CurrentLockFramesRemaining`
every tick until `DelayedActionSystem.Update` (`Game/Modules/Actions/Systems/DelayedActionSystem.cs:95-120`)
fires the action and removes `PendingDelayedActionComponent` in the same tick
`CurrentLockFramesRemaining` hits 0. So for the entire lifetime of a `PendingDelayedActionComponent`,
that entity's `ActionLockComponent` unambiguously brackets the windup (nothing else re-locks it
mid-charge) -- exactly the fraction `ActionLockContent.Update` (`Presentation/UI/Content/
ActionLockContent.cs:50-52`) already computes for the player's own HUD wheel:
```csharp
remainingFraction = CurrentLockTotalFrames > 0 ? (float)CurrentLockFramesRemaining / CurrentLockTotalFrames : 0f;
```
That's a *depletion* fraction (1.0 at charge start -> 0.0 at activation, matching the radial mask's
own "shrinks to reveal" behavior). This feature wants the inverse framing (0% at start -> 100% at
activation, per the TODO's own wording), so `chargeFraction = 1f - remainingFraction`.

`MapWindow` already holds the two component pools needed to read this per-entity -- `_pendingDelayedActions`
(`PackedComponentPool<PendingDelayedActionComponent>`) and `_actionLockPool`
(`PackedComponentPool<ActionLockComponent>`) are both existing fields, no new constructor
dependency required.

Side note, not part of this feature: `git status` shows `CombatTargetPalette.cs`'s doc comment was
just trimmed (uncommitted). This plan is written against the file's current (trimmed) state; nothing
here depends on the removed text.

## Naming investigation (TODO.md's own open question)

TODO.md asks whether this is still a "glow effect" or something else, and whether glow effects
should be split out from whatever this becomes. Answer, based on what's actually in the codebase:

- **Not a shader.** There is no `Effect`/HLSL pipeline anywhere in `Presentation` -- every existing
  "mask"/"glow"/"radial fill" (`GlowRenderer`, `DrawMaskedTileHighlight`, `RadialFillRenderer`) is
  CPU-side `spriteBatch.Draw` calls against a single 1x1 white `unitRectangle` texture. This feature
  should follow the same convention -- no new rendering technology needed or wanted.
- **Not a glow.** `GlowRenderer` (`Presentation/UI/GlowRenderer.cs`) is specifically ring-fade/flat-wash
  translucency (`GlowMode.InteriorFull/InteriorFade/ExteriorFade`) -- its own doc comment notes it
  exists *because* there's no gradient support, approximating a fade with concentric 1px rings. A
  bottom-up rectangular sweep shares none of that geometry; shoehorning it into `GlowMode` as a new
  case would be a false abstraction (the two would share a name and nothing else).
- **It's a fill effect** -- the same family as `Presentation/Rendering/ResourceBarRenderer.cs`
  (horizontal inset-rect-scaled-by-fraction, used by HUD health/mana bars) and `Presentation/Rendering/
  RadialFillRenderer.cs` (radial sweep-by-fraction, used by `ActionLockContent`'s cooldown wheel) --
  both already live in `Presentation/Rendering/` as "draw N% of a shape" primitives, distinct from
  `Presentation/UI/GlowRenderer.cs`'s ring-fade family. `MapWindow.DrawHealthBar`'s own inline
  inset-rect-by-fraction math (line 900-927) is the closest *tile-context* precedent for the actual
  geometry (swap width-from-left for height-from-bottom).

**Recommendation:** new `Presentation/Rendering/TileFillRenderer.cs`, a third member of the existing
fill-primitive family (beside `ResourceBarRenderer`/`RadialFillRenderer`) -- not a `GlowMode`
addition, not a `DrawMaskedTileHighlight` variant. Keep `GlowRenderer` and the new fill renderer as
two clearly separate, sibling concepts (per TODO's own "should there be a separation" question --
yes, and they're already separate today by construction; no existing code needs renaming).

## Design

### Progress computation (`MapWindow`)

New private helper, mirroring `ActionLockContent.Update`'s formula but inverted:

```csharp
private float TryGetChargeFraction(int entityId) =>
    _actionLockPool.TryGetReadonly(entityId, out var actionLock) && actionLock.CurrentLockTotalFrames > 0
        ? 1f - (float)actionLock.CurrentLockFramesRemaining / actionLock.CurrentLockTotalFrames
        : 0f;
```

Called once per charging entity (not per tile) inside `DrawTargetingHighlights`, since a multi-tile
`TargetShape` (Cone/Line/Burst) shares one `ActionLockComponent` across every tile it covers.

### Rendering (`Presentation/Rendering/TileFillRenderer.cs`)

```csharp
public static class TileFillRenderer
{
    public static void DrawBottomUpFill(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle tileRectangle, float fillFraction, Color color)
    {
        var clampedFraction = Math.Clamp(fillFraction, 0f, 1f);
        if (clampedFraction <= 0f)
        {
            return;
        }

        var fillHeight = (int)(tileRectangle.Height * clampedFraction);
        if (fillHeight <= 0)
        {
            return;
        }

        var fillRectangle = new Rectangle(tileRectangle.X, tileRectangle.Bottom - fillHeight, tileRectangle.Width, fillHeight);
        spriteBatch.Draw(unitRectangle, fillRectangle, color);
    }
}
```

No inset (unlike `DrawHealthBar`'s 1px outline inset) -- at `fillFraction: 1`, this exactly covers
the same full-tile rectangle `DrawMaskedTileHighlight` already washes, so there's no visual seam
between "fully charged" and today's existing flat-wash appearance.

### `MapWindow` call site

New sibling to `DrawMaskedTileHighlight`/`DrawChargingBadge`, drawing two layers per charging tile
in one `TryGetTileRectangle` resolution: an always-visible, low-alpha full-tile backdrop (so a
multi-tile `TargetShape`'s entire danger zone reads at a glance from the first frame of the windup,
same reason `DrawMaskedTileHighlight`'s flat wash exists today) plus the bottom-up fill on top of it
at the existing, brighter alpha:

```csharp
/// <summary>Fraction of TargetSelectionMaskAlpha used for the always-visible full-tile backdrop drawn under the growing charge fill -- keeps a multi-tile Delayed action's whole target shape legible from the first frame of the windup, not just once each tile's own fill has grown enough to be seen.</summary>
private const float ChargeBackdropAlphaFraction = 0.3f;

private void DrawChargeFillHighlight(SpriteBatch spriteBatch, Texture2D unitRectangle, int mapNodeX, int mapNodeY, Color fillColor, float fillFraction)
{
    if (!TryGetTileRectangle(mapNodeX, mapNodeY, out var tileRectangle))
    {
        return;
    }

    spriteBatch.Draw(unitRectangle, tileRectangle, fillColor * TargetSelectionMaskAlpha * ChargeBackdropAlphaFraction);
    TileFillRenderer.DrawBottomUpFill(spriteBatch, unitRectangle, tileRectangle, fillFraction, fillColor * TargetSelectionMaskAlpha);
}
```

Reuses the existing `TargetSelectionMaskAlpha` (0.5) constant and `CombatTargetPalette` colors --
no new palette entries needed. The backdrop draw is the exact same `spriteBatch.Draw(unitRectangle,
tileRectangle, color * alpha)` shape `DrawMaskedTileHighlight` already uses, just at a dimmer alpha
-- `DrawChargeFillHighlight` effectively subsumes `DrawMaskedTileHighlight` for charging tiles rather
than calling it separately, since both now resolve the same `tileRectangle` once.

`DrawTargetingHighlights` (`MapWindow.cs:419-463`) changes in exactly the two branches that already
represent an actually-charging (not merely armed/aimed) action -- both already have progress data
via `PendingDelayedActionComponent`:

```csharp
else if (_actionTargeting.PendingDelayedActionTargetTiles is { } pendingTargetTiles)
{
    var fillFraction = TryGetChargeFraction(_world.PlayerEntityId);
    foreach (var tile in pendingTargetTiles)
    {
        DrawChargeFillHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, CombatTargetPalette.PlayerTargetColor, fillFraction);
    }
}
...
foreach (var (entityId, targetTiles, isDodgeable) in _actionTargeting.AllPendingDelayedActionTargets())
{
    if (entityId == _world.PlayerEntityId)
    {
        continue;
    }

    var borderColor = isDodgeable ? CombatTargetPalette.EnemyDodgeableColor : CombatTargetPalette.EnemyUndodgeableColor;
    var fillFraction = TryGetChargeFraction(entityId);
    foreach (var tile in targetTiles)
    {
        DrawChargeFillHighlight(spriteBatch, unitRectangle, tile.X, tile.Y, borderColor, fillFraction);
    }
}
```

`DrawMaskedTileHighlight`'s call in the *first* branch (`_mapViewState.TargetableTiles`, the
still-arming/aiming-cursor phase, before the action is even queued) is untouched -- there is no
`PendingDelayedActionComponent`/`ActionLockComponent` windup to show progress for yet, so it keeps
today's flat wash exactly as-is.

### Decision: dim backdrop, plus the growing fill on top

Considered replacing `DrawMaskedTileHighlight`'s flat wash outright with the growing fill (nothing
visible at 0%, matching the TODO's literal wording and the HUD wheel's own no-backdrop behavior).
Rejected: for a multi-tile Cone/Line/Burst Delayed action, that would lose today's "see the whole
danger zone at a glance from frame one" signal -- a not-yet-charged tile in that shape would be
nearly invisible until its own fill grew enough to read, tile by tile, rather than the shape reading
as one coherent zone immediately. No current Delayed action is multi-tile (`PowerAttackAction`/enemy
equivalents are single-adjacent-tile), but the telegraph system itself (`DrawTargetingHighlights`,
`AllPendingDelayedActionTargets`) is already fully shape-agnostic -- it's cheap to make this specific
piece shape-agnostic too, rather than building something that would need revisiting the moment a
Cone/Line/Burst Delayed action is added.

Built instead (see `DrawChargeFillHighlight` above): every charging tile always shows a dim,
`ChargeBackdropAlphaFraction`-scaled full-tile wash from the first frame of the windup (so a
multi-tile shape's whole extent is legible immediately, same purpose `DrawMaskedTileHighlight`'s
flat wash already serves today), with the brighter bottom-up fill growing on top of it as the charge
progresses. At `fillFraction: 0` a tile reads as "targeted, not yet urgent" (dim wash only); at
`fillFraction: 1` it reads exactly like today's flat wash (dim wash fully covered by the equally
bright fill) -- no seam at either end of the range.

### Naming inside the code

`DrawChargeFillHighlight`/`TryGetChargeFraction`/`TileFillRenderer` -- "charge"/"fill" throughout,
deliberately not "glow," per the naming investigation above.

## Test plan

This codebase doesn't unit-test raw per-pixel draw geometry for its existing visual-fill primitives
either (`GlowRenderer`, `DrawHealthBar`, `RadialFillRenderer`, `ResourceBarRenderer` all have no
dedicated geometry tests) -- verified visually in-game per `CLAUDE.md`'s "Visually verify any
Presentation/UI change by running the game" convention, not pixel-asserted.

- `dotnet build`, `dotnet test` (existing baseline, no regressions expected -- this only adds a new
  static class and a couple of new private `MapWindow` methods/call-site edits, no new component or
  system).
- Manual in-game verification (`dotnet run --project DungeonCrawlerWorld/DungeonCrawlerWorld.csproj`):
  1. Trigger the player's own `PowerAttackAction` (Q) against an adjacent enemy -- confirm the dark
     green target tile shows a faint wash immediately on confirm, then fills from the bottom up over
     the 1s windup, fully covering the tile (indistinguishable from today's flat wash) exactly as
     activation happens.
  2. Let an enemy (Goblin/`TestDummyBlueprint`) windup its own attack against the player or another
     entity -- confirm the same fill behavior in red (undodgeable) or yellow (Dodgeable, if any
     enemy action is tagged so).
  3. Confirm `DrawChargingBadge`'s sprite/glyph badge above the charging entity still renders
     unchanged, alongside the new tile fill.
  4. Confirm the *arming* phase (pressing Q, before confirming a target) still shows the existing
     flat `PlayerArmColor`/`PlayerTargetColor` wash with no fill applied -- fill only starts once the
     action is actually queued/charging.
  5. Confirm no visible seam/flicker at 100% fill vs. the moment the action resolves and the
     highlight disappears.
  6. Confirm frame cost is unaffected at a glance (no new per-frame allocations -- `TileFillRenderer.
     DrawBottomUpFill` takes only value types/an existing `Texture2D`, same shape as every other
     per-tile draw call in this loop).

## Execution phases

1. Implementation (single phase -- small enough not to split): add `Presentation/Rendering/
   TileFillRenderer.cs`, add `MapWindow.TryGetChargeFraction`/`DrawChargeFillHighlight`, wire both
   into `DrawTargetingHighlights`'s two charging-tile branches. Verify per the Test plan above.

## Addendum: smoothing the raw ActionLockComponent-derived fraction

Landed and visually confirmed working per the Test plan above. Follow-up found by playing it:
`ActionLockComponent.CurrentLockFramesRemaining` -- the source `TryGetChargeFraction` reads every
frame -- is itself a step function, not a smooth countdown. `ActionLockSystem` (`Game/Modules/Core/
Systems/ActionLockSystem.cs`) only decrements it when the entity's own `TieredEntityStripeSet`
bucket comes due, by a flat `StripeCountValue` (10) each time -- correct and deliberate for
simulation cost (CLAUDE.md's own "entity striping, flat per-frame cost" note), but it means the
raw fraction holds flat for several frames, then jumps -- reading as a visibly staggered fill
rather than a continuous one, worse the more entities share a stripe cycle.

Fixing this in `ActionLockSystem` itself (e.g. finer striping) would trade away the striping
system's whole reason to exist, for every consumer, just to make one UI element read smoother --
not worth it. This is a Presentation-only problem (the simulation's own countdown is fine; only its
*rendering* looks chunky), so the fix stays in `MapWindow`.

**New `SmoothChargeFraction(entityId, targetFraction, maxAdvancePerFrame)`**: a per-entity
displayed value that climbs toward the raw `targetFraction` at a smooth, constant rate --
`elapsedSimulationFrames / CurrentLockTotalFrames` per Draw call, where `elapsedSimulationFrames`
comes from real elapsed time (`gameTime.ElapsedGameTime.TotalSeconds * GameTiming.FramesPerSecond`,
the same frames-per-second conversion `ActionEffectFormatting`/`HealthWindow`/`ModifierDisplayLine`
already use elsewhere in Presentation) rather than a flat "1 per Draw call" assumption -- robust to
Draw running at a different cadence than Update. Clamped with `Math.Min(displayed + maxAdvance,
target)`, so the displayed value **can never exceed the real raw fraction** -- it only ever climbs
toward it, never past it. This matters beyond smoothness: an entity on a slower `ProcessingTier`
(Regional/Beyond) gets visited by `ActionLockSystem` less often, so its windup can genuinely take
longer than `CurrentLockTotalFrames` frames of real time would suggest (the same tier-cadence
mismatch `IMPLEMENTATION-NOTES.md`'s `TestDummyBlueprint` note already flags, worked around there
by hardcoding `Local` tier). A naive fixed-duration smoothing (assume the windup always finishes in
exactly `CurrentLockTotalFrames`) would show 100% early for such an entity, then sit there falsely
"done" until the real attack actually lands. The clamp-to-target approach instead just catches up
to wherever the real countdown actually is and holds -- smooth when the real countdown is smooth
enough to catch, honest (pauses rather than lying) when it isn't.

State: `Dictionary<int, float> _chargeFillDisplayedFraction`, one entry per currently-charging
entity. Reset (jumps straight to the new `targetFraction` rather than continuing to climb from the
old one) whenever `targetFraction < displayedFraction` -- the only way that can happen mid-session
is a brand new windup starting (a single charge's own fraction only ever rises), so this doubles as
new-charge detection without needing to track `ActionId` separately. Pruned every
`DrawTargetingHighlights` call (`PruneChargeFillSmoothingState`) against
`AllPendingDelayedActionTargets()`'s own already-computed result (no second pool scan) so an entity
that stops charging -- action fired, or the entity died mid-windup -- doesn't leave a stale entry
sitting in the dictionary for the rest of the session.

## Addendum 2: pruning was accidentally O(D * A), the same anti-pattern this codebase already fixed once

Reported after landing the smoothing addendum above: framerate dropped dramatically (~2fps). The
first version of `PruneChargeFillSmoothingState` linear-scanned the entire
`activeChargingEntities` list once per stale-candidate in `_chargeFillDisplayedFraction` --
`O(D * A)`, run unconditionally every single `Draw` call, where `D`/`A` both scale with however
many entities across the whole map are simultaneously mid-windup at once (not just on-screen ones
-- `AllPendingDelayedActionTargets` is a dense pool scan with no viewport filtering, by design, see
its own doc comment). `TestCombatBehaviorSystem.cs` (`Game/Modules/NpcBehavior/Systems/`) already
carries a doc comment describing this exact codebase's own prior incident: an unstriped,
full-population scan running every frame instead of amortized across stripes, previously the
"actual cause of [a] ~2fps slowdown." The new prune method reintroduced the same class of bug in
a different spot.

Fixed by building a reused `HashSet<int> _activeChargingEntityIdsBuffer` from
`activeChargingEntities` once per call (`O(A)`), then testing membership against it (`O(1)`
average) instead of re-scanning the list per candidate -- `O(D + A)` overall. Behavior unchanged;
this is a pure algorithmic-complexity fix. `dotnet build`/`dotnet test` clean (the one intermittent
failure seen across these runs, in unrelated `Tests/Modules/Inventory/ConsumableActivationSystemTests.cs`
Wand tests, reproduces on a plain rerun with no code changes -- pre-existing flakiness, not a
regression from this work).

## Addendum 3: bounding A itself -- Local processing tier, not the whole map

Follow-up request after Addendum 2 landed: bound `A` itself (currently every entity anywhere on
the map with a `PendingDelayedActionComponent`, since `AllPendingDelayedActionTargets` is a
deliberately unfiltered dense pool scan -- see its own doc comment) down to entities that could
actually be visible, rather than just making the per-frame bookkeeping over that full set cheaper.

`Game/Modules/ProcessingTier/Components/ProcessingTierLevel.cs` already ranks every
movement-capable entity into one of four rings relative to the player, coarsest last: `Local`,
`Neighborhood`, `Borough`, `Beyond`. `Local`'s own entry radius (`ProcessingTierSystem
.LocalRadiusTiles`, 80 tiles Chebyshev) is well outside the camera's own viewport half-extent
(~18x11 tiles, `MapWindowTests`' own `ViewportColumns`/`ViewportRows` constants) -- so "Local tier
or closer" is a safe, conservative superset of "on screen": filtering to it can never clip an
entity the player could actually see, modulo the same bounded one-tier-period promotion lag every
other Local-tier consumer in this codebase already accepts (`ProcessingTierSystem`'s own doc
comment). Local is also definitionally the *closest* ring -- nothing is closer -- so "Local tier or
closer" is exactly `tier == ProcessingTierLevel.Local`, not a range check.

New `MapWindow.IsLocalTier(entityId)` (reads the entity's own `ProcessingTierComponent`, already a
`DirectComponentPool` fetched the same unguarded way `_actionLockPool`/`_pendingDelayedActions` are
-- `ProcessingTierModule` is unconditionally in `GameBootstrapper`'s built-in module list, same as
those). An entity with no `ProcessingTierComponent` yet reads as not-Local (fails closed, matching
every other tiered consumer's own "absent means Beyond" convention) -- safe direction to be wrong
in, self-corrects the moment `ProcessingTierSystem` visits it.

Applied in `DrawTargetingHighlights`'s "every other entity" loop: an entity failing the check is
`continue`d past *before* `TryGetChargeFraction`/`DrawChargeFillHighlight` run at all, so it never
gets a `_chargeFillDisplayedFraction` entry in the first place -- not just excluded from drawing.
The player's own branch is exempt from this check entirely (always eligible, it's the camera
anchor). `_activeChargingEntityIdsBuffer` -- previously rebuilt from scratch inside
`PruneChargeFillSmoothingState` every call -- is now built once, inline, as
`DrawTargetingHighlights` decides eligibility for drawing anyway, and handed to
`PruneChargeFillSmoothingState` (now parameterless) directly; one pass serves both drawing
eligibility and the prune reference set, rather than computing the same Local-tier-filtered
membership twice.

**Test fixture fallout**: `Tests/Presentation/MapWindowTests.cs`'s `BuildMapWindowCore` builds a
hand-rolled `ComponentManager` with only the specific pools each test needs, not the full
`GameBootstrapper` module list -- it had no `ProcessingTierComponent` registration, so the new
unguarded `componentManager.GetDirectPool<ProcessingTierComponent>()` fetch in `MapWindow`'s
constructor threw immediately, failing every test that builds a `MapWindow` (67 failures). Fixed by
registering the pool there too (`componentManager.RegisterDirectPool<ProcessingTierComponent>(...)`,
same line shape as the adjacent `ActionLockComponent`/`PendingDelayedActionComponent` registrations
already there) -- the correct parallel fix, since `ProcessingTierComponent` is an always-registered
built-in in the real game (unlike the genuinely-optional pools this same constructor guards with
`IsRegistered<T>()`, e.g. `StatModifierComponent`/`DeadComponent`). No test asserts on the enemy
telegraph's specific colored draw output today (this codebase doesn't unit-test raw per-pixel draw
geometry, per this plan's own Test plan section), so leaving test entities without a real
`ProcessingTierComponent` value (no `ProcessingTierSystem` running in this isolated fixture to
compute one) doesn't silently change any assertion -- confirmed by a full clean test run after the
fix, `1590/1590` passing.

## Addendum 4: the filter needed to move upstream, into ActionTargetingController itself

Reported after Addendum 3 landed: framerate now fluctuates randomly between 20 and 50fps (an
improvement over Addendum 2's ~2fps, but still well under the 60fps target). Used the
`phase-performance-testing` skill (`Engine/Diagnostics/DiagnosticsEngine.cs`, opt-in via
`--diagnostics=all`) to get real per-system cost instead of guessing further. Two runs, ~40s each,
averaged and diffed:

**Before this addendum** (first run, captured to establish a baseline for this specific
investigation): `Draw.Base.MapWindow` at 193.42ms/sec, `Draw.GameLoop.Shell.Draw` at 204.6ms/sec,
`Update.SystemManager.DelayedActionSystem` at 113.62ms/sec -- and, from the raw `latest.json`
memory section underneath it, the actual cause: **`PendingDelayedActionComponent.Count` = 10,497**.
Ten and a half thousand entities map-wide simultaneously mid-windup, not the "small and bounded"
population `ActionTargetingController.AllPendingDelayedActionTargets`'s own pre-existing doc comment
assumed (confirmed stale by this measurement, not by inspection alone). Addendum 3's Local-tier
filter, living in `MapWindow.DrawTargetingHighlights`, discarded non-Local entities only **after**
`AllPendingDelayedActionTargets` had already built a tuple for all ~10,500 of them -- including an
`actionCatalog.TryGet(...).Tags.Contains(Tag.Dodgeable)` lookup per entity -- so the expensive part
of the pre-existing whole-map scan was still paid in full every frame; only the cheaper downstream
smoothing/draw work had actually been bounded.

**Fix**: moved the Local-tier check into `AllPendingDelayedActionTargets` itself
(`Presentation/UI/ActionTargetingController.cs`), immediately after reading `entityId` off the dense
pool's arrays and before the catalog/Tag lookup -- a `continue` there costs one
`ProcessingTierComponent` read and nothing else. `ActionTargetingController` gained a new optional
trailing constructor parameter, `DirectComponentPool<ProcessingTierComponent>? processingTiers =
null` (matching the existing `manaPool`/`abilityScores` optional-trailing-parameter pattern) -- null
means "no tier data available," so every pending entity passes, exactly matching this method's own
pre-tier-filtering behavior, so the three test call sites that don't pass it
(`MapWindowTests`/`ActionTargetingControllerDodgeTests`/`HotbarControllerTests`) keep working
unchanged. `DungeonCrawlerWorld/ShellBootstrapper.cs` (the one production call site) now passes the
real pool. The player is still always included regardless of tier (checked by entity id against
`world.PlayerEntityId`, available to the method already), matching this method's documented "player
included" contract.

With the check now living upstream, `MapWindow`'s own `IsLocalTier`/`_processingTiers`
field/`GetDirectPool<ProcessingTierComponent>()` fetch (all added in Addendum 3) became dead code --
removed, along with the now-redundant `!IsLocalTier(entityId)` check in `DrawTargetingHighlights`'s
loop (the list `AllPendingDelayedActionTargets` returns is already scoped). Single authoritative
filter point instead of filtering twice.

**Result**, re-measured the same way: `Draw.Base.MapWindow` 106.11ms/sec (was 193.42, **-45%**),
`Draw.GameLoop.Shell.Draw` 114ms/sec (was 204.6, **-44%**). `Update.SystemManager.DelayedActionSystem`
also dropped (113.62 -> 78.95) alongside a drop in total pending-entity count between the two
sampling windows (this system is pure Game-layer, untouched by this change -- its own improvement
reflects the same organic combat-load swing responsible for the original 10,497 figure, not a
side-effect of this fix). Two small, unrelated deltas got flagged (`PotionCooldownSystem` +88%,
`Draw.StaticHud.InspectionWindow` +294% but at a ~1ms absolute scale) -- neither system was touched
by this work; most plausibly the same session-to-session combat-intensity variance, not a
regression, and not chased further here.

**Not fully resolved**: total `Update.GameLoop.EcsContext.Update (all systems)` still sat around
664ms/sec of a real-time-second budget during this same heavy-combat scenario, dominated by
`TestCombatBehaviorSystem` (182ms/sec) and `DelayedActionSystem` (79ms/sec) -- both pre-existing
Game-layer combat-AI/simulation cost, nothing to do with the charge-fill telegraph feature this plan
covers. The specific regression this feature introduced is fixed and measured; if 20-50fps under a
map-wide combat storm (thousands of entities simultaneously fighting) is still unacceptable, that's
a separate, pre-existing performance question about `TestCombatBehaviorSystem`/general combat-AI
cost at this population scale, outside this plan's own scope.

`dotnet build`/`dotnet test` clean after this addendum, `1590/1590`.

## Addendum 5: NPC targeting fixed to "different race," and DelayedActionSystem tiered

Follow-up requests after Addendum 4 landed, not directly about the fill indicator itself but found
while chasing its own performance work -- full record moved to `IMPLEMENTATION-NOTES.md`'s "Enemy
Attack Indicator + follow-up combat/performance work" section (the addendum trail for this specific
plan file stops here; further changes in this area belong there instead). Summary:

- **`TestCombatBehaviorSystem.IsAttackable`** changed from a "the player or a Fairy" allowlist to a
  real `RaceComponent` comparison -- attackable iff not dead and the candidate's race differs from
  the attacker's own. Subsumes the earlier standalone dead-entity fix (landed just before this
  addendum) in a more general form, and also fixes the class's own previously-documented "a Fairy
  attacks another Fairy" quirk. `_playerQuery`/`IsFairy` removed as dead code from both
  `TestCombatBehaviorSystem` and `NpcBehaviorModule`.
- **`DelayedActionSystem`** re-tiered off `ProcessingTierComponent` (`ProcessingTierWiring
  .CreateAndWire`, `StripeCountValue = 10` matching `ActionLockSystem`) instead of a flat, untiered
  `StripeCount = 1` -- it was visiting all ~10,000+ pending entities every single frame, unlike
  every sibling countdown system. Measured: 78.95ms/sec -> 13.53ms/sec (`phase-performance-testing`
  skill), a -82.9% drop. Deliberately kept reading `ActionLockComponent.CurrentLockFramesRemaining`
  directly rather than adopting the older independent-`ITickCountdown` proposal TODO.md used to
  carry for this system -- a second, separately-tiered clock could drift from `ActionLockSystem`'s
  own, and this plan's own charge-fill fraction (`TryGetChargeFraction`) depends on the lock
  reaching 0 and the action actually resolving happening in the same tick.

`dotnet build`/`dotnet test` clean, `1594/1594`.

## Addendum 6: TestDummy's own indicator was choppy -- a blueprint grant-ordering bug, not this feature

Reported after performance settled at a steady 60fps: every entity's charge-fill drew smoothly
except `TestDummyBlueprint`'s, visibly choppy there specifically. Not a bug in the fill/smoothing
code this plan built -- `TestDummyBlueprint.Build` merged `ActionLockComponent`/
`SimpleHealthComponent` *before* its own `ProcessingTierComponent(Local)` grant, so
`ActionLockSystem`'s `TieredEntityStripeSet` (whose membership-add read happens the instant
`ActionLockComponent` is merged, and caches permanently) saw no tier yet and defaulted to Beyond --
the dummy's own lock countdown was genuinely only advancing at Beyond's 8x-slower cadence, which
`SmoothChargeFraction`'s own target-clamp (Addendum 1) correctly refused to race ahead of, producing
exactly the "catches up, then holds" pattern that was designed for and now visibly hit. Fixed at the
source (`TestDummyBlueprint.Build`, reordered to grant the tier first) rather than in `MapWindow` --
full record in `IMPLEMENTATION-NOTES.md`'s "Enemy Attack Indicator + follow-up combat/performance
work". At the time, this read as confirmation that Addendum 1's never-exceed-the-real-target design
was correct -- see Addendum 7 immediately below for why that clamp turned out to have a real,
separate cost of its own once combined with a different bug.

`dotnet build`/`dotnet test` clean, `1594/1594`.

## Addendum 7: every indicator completed at ~80-90%, never full -- the stepped source itself was the bug

Reported after the TestDummy fix (Addendum 6) landed and was confirmed: every Delayed action
completed while its own indicator was only partially full, roughly 80-90% by visual check --
consistent for every entity, not just the previously-broken TestDummy.

Root cause traced to the fraction *source*, not the smoothing on top of it. `DelayedActionSystem`
resolves an action and removes `PendingDelayedActionComponent` in the exact same tiered visit that
finally observes `ActionLockComponent.CurrentLockFramesRemaining == 0` -- so that raw value is
*never actually observable at 0* from `MapWindow`. The last frame an entity is ever seen pending,
`CurrentLockFramesRemaining` is frozen at whatever it held after the second-to-last decrement (up
to `ActionLockSystem`'s own `StripeCountValue - 1` = 9 frames short of true completion), then the
entity simply vanishes. For `PowerAttackAction`'s 60-frame windup, decrementing by 10 each visit
(60->50->...->10->0), the last observable state is `remaining=10`, i.e. `50/60 ≈ 83%` -- matching
the reported ~80-90% almost exactly. Addendum 1's own "climb toward the raw stepped value, never
exceed it" smoothing design was working exactly as built; the raw value it was climbing toward
simply never reached 1.

Fixed by dropping `CurrentLockFramesRemaining` from the fraction entirely.
`TrackChargeElapsedFraction` (renamed from `SmoothChargeFraction`) now accumulates real elapsed
frames since each charge was first observed (`_chargeFillElapsedFrames`, renamed from
`_chargeFillDisplayedFraction`) and divides by `CurrentLockTotalFrames` directly -- reaching exactly
1 at `totalFrames` elapsed, independent of the stepped countdown's own granularity. This is only
safe because Addendum 3/4's Local-tier filtering already excludes anything whose real resolution
could meaningfully lag its nominal duration -- the original motivation for "never exceed the raw
target" (a slower-than-Local entity racing ahead of its real completion) is now handled earlier and
more precisely, by not rendering such an entity's fill at all, making the clamp pure downside once
combined with the due-visit-granularity bug above. Trade-off accepted: the fill can now read "full"
up to `StripeCountValue - 1` frames before the actual hit lands (a barely-perceptible pause, not a
perpetual shortfall), and a charge first observed already partway through (an entity crossing into
Local tier mid-windup) restarts its visible fill from 0% rather than resuming correctly -- both
documented inline on `TrackChargeElapsedFraction`.

`dotnet build`/`dotnet test` clean, `1594/1594` (one pre-existing, unrelated intermittent failure in
`Tests/Modules/Inventory/ConsumableActivationSystemTests.cs`'s Wand tests, reproduced multiple times
this session on unrelated code with no changes -- not a regression from this work).
