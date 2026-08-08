using Engine.ECS.Components.Stores;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;

namespace Presentation.UI;

/// <summary>
/// Player's moment-to-moment action input -- arming/disarming/confirming/auto-targeting abilities
/// and consumable items via their hotbar hotkeys, and WASD movement, which is handled here
/// alongside them. Abilities and items share the same arm/target/confirm rhythm (both ultimately
/// reduce to a TargetingSpec -- see Engine.Math.TargetingSpec's own doc comment for why that type
/// is shared rather than duplicated), but only one of {ability, item} is ever armed at once
/// (MapViewState.ArmedAbilityId/ArmedItemDefinitionId) and each queues into its own pending-
/// activation component for its own System to consume.
/// </summary>
public sealed class AbilityTargetingController(
    World world,
    MapViewState mapViewState,
    MapCamera camera,
    AbilityCatalog abilityCatalog,
    ItemCatalog itemCatalog,
    DirectComponentPool<TransformComponent> transformPool,
    PackedComponentPool<MovementComponent> movementPool,
    MultiComponentPool<ActionHotkeyBindingComponent> actionHotkeyBindings,
    MultiComponentPool<ItemHotkeyBindingComponent> itemHotkeyBindings,
    MultiComponentPool<InventoryItemStackComponent> inventoryStacks,
    PackedComponentPool<PendingAbilityActivationComponent> pendingActivations,
    PackedComponentPool<PendingConsumableActivationComponent> pendingConsumableActivations,
    PackedComponentPool<PendingDelayedActionComponent> pendingDelayedActions,
    PackedComponentPool<ActionLockComponent> actionLocks,
    PackedComponentPool<ManaComponent>? manaPool = null)
{
    /// <summary>~300ms -- a second press of the same slot within this many frames of the first is a double-tap (auto-target the closest candidate, see HandleHotkeySlotPress), as opposed to a slower second press (confirm against the cursor, same as a click).</summary>
    private static readonly int DoubleTapWindowFrames = GameTiming.FramesForSeconds(0.3f);

    private static readonly int FramesPerPlayerMove = GameTiming.FramesForSeconds(0.25f);

    private int _frameCounter;
    private int _playerMoveCooldownFrames;

    private readonly Dictionary<HotkeySlot, int> _lastHotkeyPressFrameBySlot = [];

    // Reused across calls (see TargetShapeResolver's own doc comment on why Resolve writes into
    // a caller-owned buffer instead of allocating).
    private readonly List<Vector3Int> _candidateTilesBuffer = [];
    private readonly List<Vector3Int> _occupiedCandidateTilesBuffer = [];
    private readonly List<Vector3Int> _finalTargetTilesBuffer = [];

    /// <summary>
    /// Backs MapViewState.TargetableTiles -- populated by RefreshTargetableTiles (Clear +
    /// repopulate) rather than replaced with a fresh HashSet every arm/move, since a HashSet
    /// allocation here runs against a heap already holding this world's ~2.6M-entity component
    /// arrays (see CLAUDE.md's Scale note); a GC pass triggered at just the wrong moment against
    /// that heap is exactly the kind of one-time stutter a per-call allocation risks causing.
    /// </summary>
    private readonly HashSet<Vector3Int> _targetableTilesSet = [];

    /// <summary>The caster position TargetableTiles was last computed from -- lets RefreshTargetableTiles (called every frame something is armed) cheaply skip recomputation on frames where the caster hasn't moved.</summary>
    private Vector3Int? _targetableTilesOrigin;

    /// <summary>The armed ability/item's actual hit-footprint at the current hover position, recomputed every Update (see UpdateHoveredTile).</summary>
    private readonly List<Vector3Int> _hoveredFootprintBuffer = [];

    /// <summary>Read-only view of _hoveredFootprintBuffer for tests -- same internal-for-test-visibility pattern as GameInputController.CurrentCursor/DragDelta.</summary>
    internal IReadOnlyList<Vector3Int> HoveredFootprint => _hoveredFootprintBuffer;

    /// <summary>Avoids exposing List&lt;T&gt;.Contains through the IReadOnlyList&lt;T&gt; above via a LINQ extension -- MapWindow's targeting-highlight draw calls this once per visible targetable tile, every frame something is armed.</summary>
    internal bool HoveredFootprintContains(Vector3Int tile) => _hoveredFootprintBuffer.Contains(tile);

    /// <summary>Advances the double-tap frame clock -- called once per MapWindow.Update, before anything else this class does that frame.</summary>
    public void Tick() => _frameCounter++;

    /// <summary>
    /// While an ability or item is armed, tracks which map tile the mouse is currently over (on
    /// the player's own Z layer, not necessarily whatever layer the camera happens to be showing)
    /// and, if so, recomputes the armed thing's actual hit-footprint from that hover position via
    /// TargetShapeResolver -- this is what lets Burst/Cone/Line's highlighted tiles move with the
    /// cursor instead of staying fixed at arm time. Also refreshes TargetableTiles (see
    /// RefreshTargetableTiles) every call, independent of whether the mouse currently resolves
    /// to a map tile at all, so the reachable-area highlight keeps following the caster if it
    /// moves while armed even with the cursor off the map. Takes the mouse position and the host
    /// window's content-area origin explicitly rather than reading either itself, the same way
    /// MapWindow.Update reads Mouse.GetState() once and passes it in, so tests can simulate a
    /// mouse position without a real OS cursor or a real Window subclass.
    /// </summary>
    public void UpdateHoveredTile(Point mousePosition, Vector2 contentAbsolutePosition)
    {
        _hoveredFootprintBuffer.Clear();

        if (!TryGetArmedTargeting(out var targeting) || !transformPool.TryGetReadonly(world.PlayerEntityId, out var playerTransform))
        {
            mapViewState.HoveredTile = null;
            return;
        }

        RefreshTargetableTiles(targeting, playerTransform.Position, playerTransform.Size);

        if (!camera.TryGetHoveredMapPosition(mousePosition, contentAbsolutePosition, out var hoveredColumnRow))
        {
            mapViewState.HoveredTile = null;
            return;
        }

        var hoveredTile = new Vector3Int(hoveredColumnRow.X, hoveredColumnRow.Y, playerTransform.Position.Z);
        mapViewState.HoveredTile = hoveredTile;

        TargetShapeResolver.Resolve(targeting.Shape, playerTransform.Position, playerTransform.Size, hoveredTile, targeting.Range, targeting.AreaSize, world.Map.Size, _hoveredFootprintBuffer);
    }

    /// <summary>
    /// A left-click confirms the armed ability or item's activation against whichever tile was
    /// clicked -- see TryConfirmActivationAtTile, the shared implementation this and a same-slot
    /// hotkey re-press (see HandleAbilitySlotPress/HandleItemSlotPress) both funnel into.
    /// </summary>
    public void TryConfirmActivation(Point mousePosition, Vector2 contentAbsolutePosition)
    {
        if (!camera.TryGetHoveredMapPosition(mousePosition, contentAbsolutePosition, out var clickedColumnRow) ||
            !transformPool.TryGetReadonly(world.PlayerEntityId, out var transform))
        {
            return;
        }

        var clickedTile = new Vector3Int(clickedColumnRow.X, clickedColumnRow.Y, transform.Position.Z);
        TryConfirmActivationAtTile(clickedTile);
    }

    /// <summary>
    /// Confirms the armed ability or item's activation against targetTile, provided it's actually
    /// within TargetableTiles (a miss is a no-op -- whatever's armed stays armed, exactly like
    /// clicking empty space doesn't clear an inspector selection either). Resolves the real Shape
    /// anchored on targetTile (not the fixed candidate-enumeration shape ComputeTargetableTiles
    /// uses) -- for Adjacent this produces the same fixed footprint regardless of which of its
    /// tiles was targeted, since Adjacent ignores the cursor entirely. Reads which of
    /// {ability, item} is armed from MapViewState itself rather than taking either id as a
    /// parameter -- mirrors CancelArmedOrPendingAction, which already does the same.
    ///
    /// A Potion specifically gets one more special case: clicking your own tile confirms as a
    /// self-only activation (see TryActivateItemOnSelf) rather than resolving the real Burst
    /// shape centered on yourself -- otherwise a manual click on your own tile would splash onto
    /// your neighbors while double-tapping the same slot (also self-targeted) wouldn't, two
    /// different results for the same intent. Targeting any other tile is untouched -- still the
    /// real Burst/AreaSize splash centered on that tile, which may or may not catch you depending
    /// on distance, same as always.
    /// </summary>
    private void TryConfirmActivationAtTile(Vector3Int targetTile)
    {
        if (!TryGetArmedTargeting(out var targeting) || !transformPool.TryGetReadonly(world.PlayerEntityId, out var transform))
        {
            return;
        }

        if (mapViewState.TargetableTiles is not { } targetableTiles || !targetableTiles.Contains(targetTile))
        {
            return;
        }

        if (targetTile == transform.Position &&
            mapViewState.ArmedItemDefinitionId is { } itemId &&
            itemCatalog.TryGet(itemId, out var item) &&
            item.Consumable is { Kind: ConsumableKind.Potion })
        {
            TryActivateItemOnSelf(world.PlayerEntityId, itemId);
            Disarm();
            return;
        }

        TargetShapeResolver.Resolve(targeting.Shape, transform.Position, transform.Size, targetTile, targeting.Range, targeting.AreaSize, world.Map.Size, _finalTargetTilesBuffer);
        QueueArmedActivation(world.PlayerEntityId, _finalTargetTilesBuffer);
        Disarm();
    }

    /// <summary>
    /// Cancels an armed ability/item (right-click tap or Escape), or, if nothing is armed,
    /// cancels a Delayed ability's in-progress windup instead: clears PendingDelayedActionComponent
    /// and zeroes the shared ActionLock directly (via ActionLockGate.Lock(..., 0)) so cancelling
    /// frees the entity immediately rather than still waiting out the full wind-up with no
    /// effect at the end -- see PendingDelayedActionComponent's own doc comment.
    /// </summary>
    public void CancelArmedOrPendingAction()
    {
        if (mapViewState.ArmedAbilityId is not null || mapViewState.ArmedItemDefinitionId is not null)
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

    /// <summary>Movement first (matches MapWindow's original per-frame hotkey ordering), then hotbar-slot hotkeys -- both are "what does this frame's input do to the player entity," so a single entry point handles them together.</summary>
    public void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        HandlePlayerMovementInput(keyboardState);
        HandleHotbarHotkeys(keyboardState, previousKeyboardState);
    }

    /// <summary>
    /// One hotkey slot per HotkeySlotLayout.PhysicalKeyBySlot entry -- an unbound slot's press
    /// is silently a no-op (see HandleHotkeySlotPress), which is exactly what a slot with neither
    /// an ActionHotkeyBindingComponent nor an ItemHotkeyBindingComponent instance already
    /// produces, so no separate "is this slot enabled" check is needed here.
    /// </summary>
    private void HandleHotbarHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState)
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
    /// Looks up which of {action, item} (if either) is bound to the pressed slot and dispatches
    /// to its own handler -- the two are mutually exclusive per slot (see IHotkeySlotBinding's
    /// own doc comment), so checking action first and falling through to item is safe. Internal
    /// (not private): HandleHotbarHotkeys below is the keyboard entry point, but
    /// HotbarController.OnSlotTapped calls this exact same method for a mouse click on a hotbar
    /// slot, so a click behaves identically to pressing that slot's key -- including sharing this
    /// method's own double-tap-window tracking (_lastHotkeyPressFrameBySlot is keyed by slot and
    /// frame only, never by input source), so a click closely following a key press (or another
    /// click) on the same slot counts as a double-tap exactly the way two key presses would.
    /// </summary>
    internal void HandleHotkeySlotPress(HotkeySlot slot)
    {
        var isDoubleTap = _lastHotkeyPressFrameBySlot.TryGetValue(slot, out var lastPressFrame) &&
            _frameCounter - lastPressFrame <= DoubleTapWindowFrames;
        _lastHotkeyPressFrameBySlot[slot] = _frameCounter;

        if (ActionHotkeyBindingQueries.TryGet(actionHotkeyBindings, world.PlayerEntityId, slot, out var abilityId))
        {
            HandleAbilitySlotPress(slot, abilityId, isDoubleTap);
            return;
        }

        if (ItemHotkeyBindingQueries.TryGet(itemHotkeyBindings, world.PlayerEntityId, slot, out var itemDefinitionId))
        {
            HandleItemSlotPress(slot, itemDefinitionId, isDoubleTap);
        }
    }

    /// <summary>
    /// Arms the pressed slot's ability, or -- if it's already armed -- confirms it instead: a
    /// double-tap within DoubleTapWindowFrames skips arming entirely and immediately activates
    /// against an auto-picked target (see TryActivateWithAutoTarget); a slower re-press confirms
    /// against wherever the cursor currently is (see TryConfirmActivationAtTile), the same as a
    /// click would. Cancelling an armed slot is right-click/Escape's job now (see
    /// CancelArmedOrPendingAction) -- re-pressing the same key always means "go," not "nevermind."
    /// An ability the player can't currently afford (see HasEnoughMana) is inert, the same no-op
    /// an unbound slot already is -- HotbarContent greys it out the same way (see its own
    /// isUsable check), so "can't be armed" and "looks unusable" stay in sync, mirroring
    /// HandleItemSlotPress's identical treatment of an unusable item slot.
    /// </summary>
    private void HandleAbilitySlotPress(HotkeySlot slot, Guid abilityId, bool isDoubleTap)
    {
        if (!HasEnoughMana(world.PlayerEntityId, abilityId))
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
            if (mapViewState.HoveredTile is { } hoveredTile)
            {
                TryConfirmActivationAtTile(hoveredTile);
            }

            return;
        }

        ArmAbility(slot, abilityId);
    }

    /// <summary>
    /// A bound item with no ConsumableEffect (e.g. an Equipment/Tool item with no activated
    /// ability yet), or with no remaining stock (the player's stack was fully consumed -- see
    /// InventoryItemStackComponent's "no instance means empty" convention, the same one
    /// InventoryActions.ConsumeItem relies on), is inert, the same no-op an unbound slot already
    /// is -- HotbarContent greys it out the same way, so "can't be armed" and "looks unusable"
    /// stay in sync. A Potion double-tap always activates on the caster's own tile
    /// (TryActivateItemOnSelf), skipping arm/target entirely -- "double-tap always uses the
    /// potion on the user," regardless of what's currently armed. Not generalized to every
    /// ConsumableKind: a future self-only kind (e.g. a bandage) already resolves to Self via its
    /// own ConsumableEffect.Targeting through the ordinary arm/confirm path below, so it doesn't
    /// need this shortcut to behave the same way. A non-double-tap re-press of an already-armed
    /// slot confirms against the cursor instead (see TryConfirmActivationAtTile) -- same rhythm
    /// as HandleAbilitySlotPress, cancelling is right-click/Escape's job now.
    /// </summary>
    private void HandleItemSlotPress(HotkeySlot slot, Guid itemDefinitionId, bool isDoubleTap)
    {
        if (!itemCatalog.TryGet(itemDefinitionId, out var item) || item.Consumable is not { } consumable)
        {
            return;
        }

        if (!InventoryQueries.TryGetStack(inventoryStacks, world.PlayerEntityId, itemDefinitionId, out var stack) || stack.Quantity <= 0)
        {
            return;
        }

        if (isDoubleTap && consumable.Kind == ConsumableKind.Potion)
        {
            TryActivateItemOnSelf(world.PlayerEntityId, itemDefinitionId);

            if (mapViewState.ArmedSlot == slot)
            {
                Disarm();
            }

            return;
        }

        if (mapViewState.ArmedSlot == slot)
        {
            if (mapViewState.HoveredTile is { } hoveredTile)
            {
                TryConfirmActivationAtTile(hoveredTile);
            }

            return;
        }

        ArmItem(slot, itemDefinitionId, consumable.Targeting);
    }

    private void ArmAbility(HotkeySlot slot, Guid abilityId)
    {
        mapViewState.ArmedAbilityId = abilityId;
        mapViewState.ArmedItemDefinitionId = null;
        mapViewState.ArmedSlot = slot;
        _targetableTilesOrigin = null; // Forces RefreshTargetableTiles below to (re)compute regardless of any stale origin left over from a previous arm.

        if (abilityCatalog.TryGet(abilityId, out var ability) && transformPool.TryGetReadonly(world.PlayerEntityId, out var transform))
        {
            RefreshTargetableTiles(ability.Targeting, transform.Position, transform.Size);
        }
    }

    private void ArmItem(HotkeySlot slot, Guid itemDefinitionId, TargetingSpec targeting)
    {
        mapViewState.ArmedItemDefinitionId = itemDefinitionId;
        mapViewState.ArmedAbilityId = null;
        mapViewState.ArmedSlot = slot;
        _targetableTilesOrigin = null;

        if (transformPool.TryGetReadonly(world.PlayerEntityId, out var transform))
        {
            RefreshTargetableTiles(targeting, transform.Position, transform.Size);
        }
    }

    private void Disarm()
    {
        mapViewState.ArmedAbilityId = null;
        mapViewState.ArmedItemDefinitionId = null;
        mapViewState.ArmedSlot = null;
        mapViewState.TargetableTiles = null;
        _targetableTilesOrigin = null;
    }

    /// <summary>Mirrors AbilityActivationSystem's own gate (see its own doc comment) -- a zero-cost ability (or an unknown abilityId, left for the actual activation attempt to reject) always passes, even with no ManaComponent pool wired in.</summary>
    private bool HasEnoughMana(int entityId, Guid abilityId)
    {
        if (!abilityCatalog.TryGet(abilityId, out var ability) || ability.ManaCost <= 0)
        {
            return true;
        }

        return manaPool is not null && manaPool.TryGetReadonly(entityId, out var mana) && mana.CurrentMana >= ability.ManaCost;
    }

    /// <summary>Resolves whichever of {ability, item} is currently armed to its shared TargetingSpec -- the one piece both kinds need for every targeting computation below, so callers stop caring which kind they're dealing with past this point.</summary>
    private bool TryGetArmedTargeting(out TargetingSpec targeting)
    {
        if (mapViewState.ArmedAbilityId is { } abilityId && abilityCatalog.TryGet(abilityId, out var ability))
        {
            targeting = ability.Targeting;
            return true;
        }

        if (mapViewState.ArmedItemDefinitionId is { } itemId && itemCatalog.TryGet(itemId, out var item) && item.Consumable is { } consumable)
        {
            targeting = consumable.Targeting;
            return true;
        }

        targeting = null!;
        return false;
    }

    /// <summary>
    /// (Re)computes TargetableTiles from currentPosition -- but only if it hasn't already been
    /// computed from that exact position, so an armed-and-stationary caster doesn't redo this
    /// work every single frame. Called both by Arm (first computation) and every UpdateHoveredTile
    /// call thereafter, so the highlighted reachable area re-centers on the caster's new position
    /// if it moves while still armed, instead of staying fixed at wherever it was standing at arm
    /// time. Repopulates the shared _targetableTilesSet in place (Clear + re-add) rather than
    /// assigning a fresh HashSet -- see that field's own doc comment for why.
    /// </summary>
    private void RefreshTargetableTiles(TargetingSpec targeting, Vector3Int currentPosition, Vector2Byte currentSize)
    {
        if (_targetableTilesOrigin == currentPosition)
        {
            return;
        }

        _targetableTilesOrigin = currentPosition;

        ComputeTargetableTiles(currentPosition, currentSize, targeting, _candidateTilesBuffer);

        _targetableTilesSet.Clear();
        foreach (var tile in _candidateTilesBuffer)
        {
            _targetableTilesSet.Add(tile);
        }

        mapViewState.TargetableTiles = _targetableTilesSet;
    }

    /// <summary>
    /// The full universe of tiles the given targeting could possibly be aimed at from
    /// attackerPosition -- Adjacent's fixed perimeter-around-the-attacker's-footprint, or every
    /// tile within Range for every cursor-directed shape (SingleTarget/Burst/Line/Cone) via a
    /// Burst-shaped scatter, not the real Shape -- there's no single "aim direction" yet at arm
    /// time, only a reachable area. Shared by Arm (for highlighting) and
    /// TryActivateWithAutoTarget (for double-tap's candidate pool), so the two never drift out
    /// of sync with each other.
    /// </summary>
    private void ComputeTargetableTiles(Vector3Int attackerPosition, Vector2Byte attackerSize, TargetingSpec targeting, List<Vector3Int> buffer)
    {
        if (targeting.Shape == TargetShape.Adjacent)
        {
            TargetShapeResolver.Resolve(TargetShape.Adjacent, attackerPosition, attackerSize, attackerPosition, range: 0, areaSize: 0, world.Map.Size, buffer);
            return;
        }

        TargetShapeResolver.Resolve(TargetShape.Burst, attackerPosition, attackerSize, attackerPosition, range: 0, targeting.Range, world.Map.Size, buffer);
    }

    /// <summary>
    /// Resolves and queues a full ability activation with no manual click-confirm at all -- the
    /// double-tap path. Adjacent's footprint never depends on a target choice (it's always the
    /// caster's own tile plus its 8 surrounding neighbors), so it's queued immediately. Every other
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
        var attackerSize = transform.Size;
        var mapSize = world.Map.Size;

        if (ability.Targeting.Shape == TargetShape.Adjacent)
        {
            ComputeTargetableTiles(attackerPosition, attackerSize, ability.Targeting, _candidateTilesBuffer);
            QueueAbilityActivation(entityId, abilityId, _candidateTilesBuffer);
            return;
        }

        // Self's only candidate tile is the caster's own position, which the occupied-tile
        // filter below would always exclude (it deliberately drops tiles occupied by the
        // caster itself, meant for every *other* shape) -- so Self needs its own direct
        // resolve/queue, the same as Adjacent above, rather than falling through into that filter.
        if (ability.Targeting.Shape == TargetShape.Self)
        {
            QueueAbilityActivation(entityId, abilityId, [attackerPosition]);
            return;
        }

        ComputeTargetableTiles(attackerPosition, attackerSize, ability.Targeting, _candidateTilesBuffer);

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

        TargetShapeResolver.Resolve(ability.Targeting.Shape, attackerPosition, attackerSize, chosenTile, ability.Targeting.Range, ability.Targeting.AreaSize, mapSize, _finalTargetTilesBuffer);
        QueueAbilityActivation(entityId, abilityId, _finalTargetTilesBuffer);
    }

    /// <summary>The double-tap path for a Potion -- always the caster's own tile, no candidate search at all (contrast TryActivateWithAutoTarget's ability equivalent).</summary>
    private void TryActivateItemOnSelf(int entityId, Guid itemDefinitionId)
    {
        if (!transformPool.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        QueueConsumableActivation(entityId, itemDefinitionId, [transform.Position]);
    }

    /// <summary>Presentation only ever queues an activation request -- AbilityActivationSystem is the only thing that applies gameplay effects. Mirrors TryQueuePlayerMove's own queue-and-let-a-system-consume pattern for movement.</summary>
    private void QueueAbilityActivation(int entityId, Guid abilityId, List<Vector3Int> targetTiles)
    {
        if (targetTiles.Count == 0)
        {
            return;
        }

        pendingActivations.Merge(entityId, new PendingAbilityActivationComponent(abilityId, targetTiles.ToArray()));
    }

    /// <summary>Item counterpart to QueueAbilityActivation -- ConsumableActivationSystem is the only thing that applies its gameplay effects.</summary>
    private void QueueConsumableActivation(int entityId, Guid itemDefinitionId, List<Vector3Int> targetTiles)
    {
        if (targetTiles.Count == 0)
        {
            return;
        }

        pendingConsumableActivations.Merge(entityId, new PendingConsumableActivationComponent(itemDefinitionId, targetTiles.ToArray()));
    }

    /// <summary>Dispatches a confirmed click activation to whichever of {ability, item} MapViewState currently has armed -- see TryConfirmActivation, the only caller.</summary>
    private void QueueArmedActivation(int entityId, List<Vector3Int> targetTiles)
    {
        if (mapViewState.ArmedAbilityId is { } abilityId)
        {
            QueueAbilityActivation(entityId, abilityId, targetTiles);
        }
        else if (mapViewState.ArmedItemDefinitionId is { } itemId)
        {
            QueueConsumableActivation(entityId, itemId, targetTiles);
        }
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
