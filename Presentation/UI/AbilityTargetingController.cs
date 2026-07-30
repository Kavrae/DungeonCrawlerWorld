using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;

namespace Presentation.UI;

/// <summary>
/// Player's  moment-to-moment action input --
/// arming/disarming/confirming/auto-targeting abilities via their hotbar hotkeys, and WASD
/// movement, which is handled here alongside abilities.
/// </summary>
public sealed class AbilityTargetingController(
    World world,
    MapViewState mapViewState,
    MapCamera camera,
    AbilityCatalog abilityCatalog,
    DirectComponentPool<TransformComponent> transformPool,
    PackedComponentPool<MovementComponent> movementPool,
    MultiComponentPool<HotkeyBindingComponent> hotkeyBindings,
    PackedComponentPool<PendingAbilityActivationComponent> pendingActivations,
    PackedComponentPool<PendingDelayedActionComponent> pendingDelayedActions,
    PackedComponentPool<ActionLockComponent> actionLocks)
{
    /// <summary>~300ms at 60fps -- a second press of the same slot within this many frames of the first is a double-tap (see HandleHotkeySlotPress), not two independent arm/disarm presses.</summary>
    private const int DoubleTapWindowFrames = 18;

    private const int FramesPerPlayerMove = 15;

    private int _frameCounter;
    private int _playerMoveCooldownFrames;

    private readonly Dictionary<HotkeySlot, int> _lastHotkeyPressFrameBySlot = [];

    // Reused across calls (see TargetShapeResolver's own doc comment on why Resolve writes into
    // a caller-owned buffer instead of allocating).
    private readonly List<Vector3Int> _candidateTilesBuffer = [];
    private readonly List<Vector3Int> _occupiedCandidateTilesBuffer = [];
    private readonly List<Vector3Int> _finalTargetTilesBuffer = [];

    /// <summary>The armed ability's actual hit-footprint at the current hover position, recomputed every Update (see UpdateHoveredTile).</summary>
    private readonly List<Vector3Int> _hoveredFootprintBuffer = [];

    /// <summary>Read-only view of _hoveredFootprintBuffer for tests -- same internal-for-test-visibility pattern as GameInputController.CurrentCursor/DragDelta.</summary>
    internal IReadOnlyList<Vector3Int> HoveredFootprint => _hoveredFootprintBuffer;

    /// <summary>Avoids exposing List&lt;T&gt;.Contains through the IReadOnlyList&lt;T&gt; above via a LINQ extension -- MapWindow's targeting-highlight draw calls this once per visible targetable tile, every frame an ability is armed.</summary>
    internal bool HoveredFootprintContains(Vector3Int tile) => _hoveredFootprintBuffer.Contains(tile);

    /// <summary>Advances the double-tap frame clock -- called once per MapWindow.Update, before anything else this class does that frame.</summary>
    public void Tick() => _frameCounter++;

    /// <summary>
    /// While an ability is armed, tracks which map tile the mouse is currently over (on the
    /// player's own Z layer, not necessarily whatever layer the camera happens to be showing)
    /// and, if so, recomputes the armed ability's actual hit-footprint from that hover position
    /// via TargetShapeResolver -- this is what lets Burst/Cone/Line's highlighted tiles move
    /// with the cursor instead of staying fixed at arm time. Takes the mouse position and the
    /// host window's content-area origin explicitly rather than reading either itself, the same
    /// way MapWindow.Update reads Mouse.GetState() once and passes it in, so tests can simulate
    /// a mouse position without a real OS cursor or a real Window subclass.
    /// </summary>
    public void UpdateHoveredTile(Point mousePosition, Vector2 contentAbsolutePosition)
    {
        _hoveredFootprintBuffer.Clear();

        if (mapViewState.ArmedAbilityId is not { } abilityId)
        {
            mapViewState.HoveredTile = null;
            return;
        }

        if (!camera.TryGetHoveredMapPosition(mousePosition, contentAbsolutePosition, out var hoveredColumnRow) ||
            !transformPool.TryGetReadonly(world.PlayerEntityId, out var playerTransform))
        {
            mapViewState.HoveredTile = null;
            return;
        }

        var hoveredTile = new Vector3Int(hoveredColumnRow.X, hoveredColumnRow.Y, playerTransform.Position.Z);
        mapViewState.HoveredTile = hoveredTile;

        if (abilityCatalog.TryGet(abilityId, out var ability))
        {
            TargetShapeResolver.Resolve(ability.Targeting.Shape, playerTransform.Position, hoveredTile, ability.Targeting.Range, ability.Targeting.AreaSize, world.Map.Size, _hoveredFootprintBuffer);
        }
    }

    /// <summary>
    /// A left-click confirms the armed ability's activation against whichever tile was clicked,
    /// provided that tile is actually within TargetableTiles (clicking outside the highlighted
    /// area is a no-op -- the ability stays armed, exactly like clicking empty space doesn't
    /// clear an inspector selection either). Resolves the ability's real Shape anchored on the
    /// clicked tile (not the fixed candidate-enumeration shape ComputeTargetableTiles uses) --
    /// for Adjacent this produces the same fixed footprint regardless of which of its tiles was
    /// clicked, since Adjacent ignores the cursor entirely.
    /// </summary>
    public void TryConfirmActivation(Point mousePosition, Vector2 contentAbsolutePosition, Guid abilityId)
    {
        if (!camera.TryGetHoveredMapPosition(mousePosition, contentAbsolutePosition, out var clickedColumnRow) ||
            !abilityCatalog.TryGet(abilityId, out var ability) ||
            !transformPool.TryGetReadonly(world.PlayerEntityId, out var transform))
        {
            return;
        }

        var attackerPosition = transform.Position;
        var clickedTile = new Vector3Int(clickedColumnRow.X, clickedColumnRow.Y, attackerPosition.Z);

        if (mapViewState.TargetableTiles is not { } targetableTiles || !targetableTiles.Contains(clickedTile))
        {
            return;
        }

        TargetShapeResolver.Resolve(ability.Targeting.Shape, attackerPosition, clickedTile, ability.Targeting.Range, ability.Targeting.AreaSize, world.Map.Size, _finalTargetTilesBuffer);
        QueueActivation(world.PlayerEntityId, abilityId, _finalTargetTilesBuffer);
        Disarm();
    }

    /// <summary>
    /// Cancels an armed ability (right-click tap or Escape), or, if nothing is armed, cancels a
    /// Delayed ability's in-progress windup instead: clears PendingDelayedActionComponent and
    /// zeroes the shared ActionLock directly (via ActionLockGate.Lock(..., 0)) so cancelling
    /// frees the entity immediately rather than still waiting out the full wind-up with no
    /// effect at the end -- see PendingDelayedActionComponent's own doc comment.
    /// </summary>
    public void CancelArmedOrPendingAction()
    {
        if (mapViewState.ArmedAbilityId is not null)
        {
            Disarm();
            return;
        }

        var playerEntityId = world.PlayerEntityId;
        if (pendingDelayedActions.Remove(playerEntityId))
        {
            ActionLockGate.Lock(actionLocks, playerEntityId, framesToWait: 0);
        }
    }

    /// <summary>Movement first (matches MapWindow's original per-frame hotkey ordering), then ability-slot hotkeys -- both are "what does this frame's input do to the player entity," so a single entry point handles them together.</summary>
    public void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        HandlePlayerMovementInput(keyboardState);
        HandleAbilityHotkeys(keyboardState, previousKeyboardState);
    }

    /// <summary>
    /// One hotkey slot per HotkeySlotLayout.PhysicalKeyBySlot entry -- an unbound slot's press
    /// is silently a no-op (see HandleHotkeySlotPress), which is exactly what a slot with no
    /// HotkeyBindingComponent instance already produces, so no separate "is this slot enabled"
    /// check is needed here.
    /// </summary>
    private void HandleAbilityHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        foreach (var (slot, physicalKey) in HotkeySlotLayout.PhysicalKeyBySlot)
        {
            if (Window.WasKeyPressed(keyboardState, previousKeyboardState, physicalKey))
            {
                HandleHotkeySlotPress(slot);
            }
        }
    }

    /// <summary>
    /// Arms/disarms the pressed slot's ability, or -- on a double-tap within
    /// DoubleTapWindowFrames -- skips arming entirely and immediately activates against an
    /// auto-picked target (see TryActivateWithAutoTarget). An unbound slot does nothing either
    /// way, per the outline's requirement that unused hotkeys be inert.
    /// </summary>
    private void HandleHotkeySlotPress(HotkeySlot slot)
    {
        var isDoubleTap = _lastHotkeyPressFrameBySlot.TryGetValue(slot, out var lastPressFrame) &&
            _frameCounter - lastPressFrame <= DoubleTapWindowFrames;
        _lastHotkeyPressFrameBySlot[slot] = _frameCounter;

        if (!HotkeyBindingQueries.TryGet(hotkeyBindings, world.PlayerEntityId, slot, out var abilityId))
        {
            return;
        }

        if (isDoubleTap)
        {
            TryActivateWithAutoTarget(world.PlayerEntityId, abilityId);

            // The pair's first press (a moment ago, within the double-tap window) armed this
            // same slot -- now that it's fired, leaving it visually armed would be stale/
            // misleading, so clear it rather than requiring a third press to tidy up.
            if (mapViewState.ArmedSlot == slot)
            {
                Disarm();
            }

            return;
        }

        if (mapViewState.ArmedSlot == slot)
        {
            // Pressing the already-armed slot again disarms it. A no-target ability activating
            // on this same press is a later Presentation phase's concern (it needs to know the
            // ability's Shape doesn't require a target tile at all, not just that its slot was
            // pressed again) -- every ability granted so far requires a target, so disarm-only
            // is the complete, correct behavior today.
            Disarm();
            return;
        }

        Arm(slot, abilityId);
    }

    private void Arm(HotkeySlot slot, Guid abilityId)
    {
        mapViewState.ArmedAbilityId = abilityId;
        mapViewState.ArmedSlot = slot;

        if (abilityCatalog.TryGet(abilityId, out var ability) && transformPool.TryGetReadonly(world.PlayerEntityId, out var transform))
        {
            ComputeTargetableTiles(transform.Position, ability, _candidateTilesBuffer);
            mapViewState.TargetableTiles = _candidateTilesBuffer.ToHashSet();
        }
    }

    private void Disarm()
    {
        mapViewState.ArmedAbilityId = null;
        mapViewState.ArmedSlot = null;
        mapViewState.TargetableTiles = null;
    }

    /// <summary>
    /// The full universe of tiles the given ability could possibly be aimed at from
    /// attackerPosition -- Adjacent's fixed self-plus-4-neighbors footprint, or every tile
    /// within the ability's own Range for every cursor-directed shape (SingleTarget/Burst/Line/
    /// Cone) via a Burst-shaped scatter, not the ability's real Shape -- there's no single
    /// "aim direction" yet at arm time, only a reachable area. Shared by Arm (for highlighting)
    /// and TryActivateWithAutoTarget (for double-tap's candidate pool), so the two never drift
    /// out of sync with each other.
    /// </summary>
    private void ComputeTargetableTiles(Vector3Int attackerPosition, AbilityDefinition ability, List<Vector3Int> buffer)
    {
        if (ability.Targeting.Shape == TargetShape.Adjacent)
        {
            TargetShapeResolver.Resolve(TargetShape.Adjacent, attackerPosition, attackerPosition, range: 0, areaSize: 0, world.Map.Size, buffer);
            return;
        }

        TargetShapeResolver.Resolve(TargetShape.Burst, attackerPosition, attackerPosition, range: 0, ability.Targeting.Range, world.Map.Size, buffer);
    }

    /// <summary>
    /// Resolves and queues a full activation with no manual click-confirm at all -- the
    /// double-tap path. Adjacent's footprint never depends on a target choice (it's always the
    /// caster's own tile plus its 4 cardinal neighbors), so it's queued immediately. Every other
    /// shape needs a target tile chosen first: ComputeTargetableTiles' reachable-area candidates
    /// are filtered down to occupied tiles and handed to TargetPriority.SelectAutoTarget, using
    /// MapViewState.HoveredTile as the cursor bias when one is already tracked (armed-and-then-
    /// double-tapped in one motion means Update hasn't run with the arm in effect yet, so
    /// HoveredTile can still be stale/null on the very first pair -- attackerPosition is the
    /// fallback for exactly that case, which is also what makes "closest to cursor" degenerate
    /// harmlessly into "closest to the caster" rather than picking an arbitrary target).
    /// </summary>
    private void TryActivateWithAutoTarget(int entityId, Guid abilityId)
    {
        if (!abilityCatalog.TryGet(abilityId, out var ability) || !transformPool.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        var attackerPosition = transform.Position;
        var mapSize = world.Map.Size;

        if (ability.Targeting.Shape == TargetShape.Adjacent)
        {
            ComputeTargetableTiles(attackerPosition, ability, _candidateTilesBuffer);
            QueueActivation(entityId, abilityId, _candidateTilesBuffer);
            return;
        }

        ComputeTargetableTiles(attackerPosition, ability, _candidateTilesBuffer);

        _occupiedCandidateTilesBuffer.Clear();
        foreach (var tile in _candidateTilesBuffer)
        {
            var occupantEntityId = world.GetEntityIdAt(tile);
            if (occupantEntityId != -1 && occupantEntityId != entityId)
            {
                _occupiedCandidateTilesBuffer.Add(tile);
            }
        }

        var cursorTile = mapViewState.HoveredTile ?? attackerPosition;
        if (TargetPriority.SelectAutoTarget(attackerPosition, cursorTile, _occupiedCandidateTilesBuffer) is not { } chosenTile)
        {
            return;
        }

        TargetShapeResolver.Resolve(ability.Targeting.Shape, attackerPosition, chosenTile, ability.Targeting.Range, ability.Targeting.AreaSize, mapSize, _finalTargetTilesBuffer);
        QueueActivation(entityId, abilityId, _finalTargetTilesBuffer);
    }

    /// <summary>Presentation only ever queues an activation request -- AbilityActivationSystem is the only thing that applies gameplay effects. Mirrors TryQueuePlayerMove's own queue-and-let-a-system-consume pattern for movement.</summary>
    private void QueueActivation(int entityId, Guid abilityId, List<Vector3Int> targetTiles)
    {
        if (targetTiles.Count == 0)
        {
            return;
        }

        pendingActivations.Merge(entityId, new PendingAbilityActivationComponent(abilityId, targetTiles.ToArray()));
    }

    private void HandlePlayerMovementInput(KeyboardState keyboardState)
    {
        if (_playerMoveCooldownFrames > 0)
        {
            _playerMoveCooldownFrames--;
        }

        var delta = new Vector3Int();
        if (keyboardState.IsKeyDown(Keys.W))
        {
            delta.Y -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.S))
        {
            delta.Y += 1;
        }
        if (keyboardState.IsKeyDown(Keys.A))
        {
            delta.X -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.D))
        {
            delta.X += 1;
        }

        if (delta == new Vector3Int() || _playerMoveCooldownFrames > 0)
        {
            return;
        }

        _playerMoveCooldownFrames = FramesPerPlayerMove;
        TryQueuePlayerMove(delta);
    }

    private void TryQueuePlayerMove(Vector3Int delta)
    {
        var playerEntityId = world.PlayerEntityId;
        if (!transformPool.TryGetReadonly(playerEntityId, out var transformComponent) ||
            !movementPool.TryGetReadonly(playerEntityId, out var movementComponent))
        {
            return;
        }

        // Only queue a new move while at rest -- avoids redirecting a move that's already
        // pending (e.g. still waiting on MovementSystem's action lock).
        var isAtRest = movementComponent.NextMapPosition is null || movementComponent.NextMapPosition.Value == transformComponent.Position;
        if (!isAtRest)
        {
            return;
        }

        var candidate = transformComponent.Position + delta;
        var occupyingEntityId = world.GetEntityIdAt(candidate);
        if (!world.IsOnMap(candidate) || (occupyingEntityId != -1 && occupyingEntityId != playerEntityId))
        {
            return;
        }

        movementPool.TryUpdate(playerEntityId, candidate, static (ref MovementComponent movement, Vector3Int target) =>
        {
            movement.NextMapPosition = target;
        });
    }
}
