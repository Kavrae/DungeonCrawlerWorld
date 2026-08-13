using Engine.ECS.Components.Stores;
using Engine.Math;
using Engine.Utilities;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;

namespace Presentation.UI;

/// <summary>
/// Player's moment-to-moment action input
/// </summary>
/// <remarks>
/// Arming/disarming/confirming/auto-targeting actions
/// and consumable items via their hotbar hotkeys. Items go through this same arm/target/confirm
/// rhythm too -- matches ActionHotkeyBindingComponent's own umbrella-term reasoning (see its doc
/// comment). Actions and items share that rhythm because both ultimately reduce to a
/// TargetingSpec (see Engine.Math.TargetingSpec's own doc comment for why that type is shared
/// rather than duplicated), but only one of {action, item} is ever armed at once
/// (MapViewState.ArmedActionId/ArmedItemDefinitionId) and each queues into its own
/// pending-activation component for its own System to consume. Player movement is a separate
/// concern handled by the sibling PlayerMovementController -- MapWindow.OnHotkeysAction calls both
/// every frame, but the two share no state.
/// </remarks>
public sealed class ActionTargetingController(
    World world,
    MapViewState mapViewState,
    MapCamera camera,
    ActionCatalog actionCatalog,
    ItemCatalog itemCatalog,
    DirectComponentPool<TransformComponent> transformPool,
    MultiComponentPool<ActionHotkeyBindingComponent> actionHotkeyBindings,
    MultiComponentPool<ItemHotkeyBindingComponent> itemHotkeyBindings,
    MultiComponentPool<InventoryItemStackComponent> inventoryStacks,
    PackedComponentPool<HotkeyExpansionUnlockComponent> hotkeyExpansionUnlocks,
    PackedComponentPool<PendingActionActivationComponent> pendingActivations,
    PackedComponentPool<PendingConsumableActivationComponent> pendingConsumableActivations,
    PackedComponentPool<PendingDelayedActionComponent> pendingDelayedActions,
    PackedComponentPool<ActionLockComponent> actionLocks,
    PackedComponentPool<ManaComponent>? manaPool = null,
    MultiComponentPool<AbilityScoreComponent>? abilityScores = null)
{
    /// <summary>~300ms -- a second press of the same slot within this many frames of the first is a double-tap (auto-target the closest candidate, see HandleHotkeySlotPress), as opposed to a slower second press (confirm against the cursor, same as a click).</summary>
    private static readonly int DoubleTapWindowFrames = GameTiming.FramesForSeconds(0.3f);

    private int _frameCounter;

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

    /// <summary>The armed action/item's actual hit-footprint at the current hover position, recomputed every Update (see UpdateHoveredTile).</summary>
    private readonly List<Vector3Int> _hoveredFootprintBuffer = [];

    /// <summary>Read-only view of _hoveredFootprintBuffer for tests -- same internal-for-test-visibility pattern as UiInputController.CurrentCursor/DragDelta.</summary>
    internal IReadOnlyList<Vector3Int> HoveredFootprint => _hoveredFootprintBuffer;

    /// <summary>Avoids exposing List&lt;T&gt;.Contains through the IReadOnlyList&lt;T&gt; above via a LINQ extension -- MapWindow's targeting-highlight draw calls this once per visible targetable tile, every frame something is armed.</summary>
    internal bool HoveredFootprintContains(Vector3Int tile) => _hoveredFootprintBuffer.Contains(tile);

    /// <summary>Advances the double-tap frame clock -- called once per MapWindow.Update, before anything else this class does that frame.</summary>
    public void Tick() => _frameCounter++;

    /// <summary>
    /// While an action or item is armed, tracks which map tile the mouse is currently over (on
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
    /// A left-click confirms the armed action or item's activation against whichever tile was
    /// clicked -- see TryConfirmActivationAtTile, the shared implementation this and a same-slot
    /// hotkey re-press (see HandleActionSlotPress/HandleItemSlotPress) both funnel into.
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
    /// Confirms the armed action or item's activation against targetTile, provided it's actually
    /// within TargetableTiles (a miss is a no-op -- whatever's armed stays armed, exactly like
    /// clicking empty space doesn't clear an inspector selection either). Resolves the real Shape
    /// anchored on targetTile (not the fixed candidate-enumeration shape ComputeTargetableTiles
    /// uses) -- for Adjacent this produces the same fixed footprint regardless of which of its
    /// tiles was targeted, since Adjacent ignores the cursor entirely. Reads which of
    /// {action, item} is armed from MapViewState itself rather than taking either id as a
    /// parameter -- mirrors CancelArmedOrPendingAction, which already does the same.
    ///
    /// A Tag.Self item specifically gets one more special case: clicking your own tile confirms
    /// as a self-only activation (see TryActivateItemOnSelf) rather than resolving the real Burst
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
            item.Tags.Contains(Tag.Self))
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
    /// Cancels an armed action/item (right-click tap or Escape), or, if nothing is armed,
    /// cancels a Delayed action's in-progress windup instead: clears PendingDelayedActionComponent
    /// and zeroes the shared ActionLock directly (via ActionLockGate.Lock(..., 0)) so cancelling
    /// frees the entity immediately rather than still waiting out the full wind-up with no
    /// effect at the end -- see PendingDelayedActionComponent's own doc comment.
    /// </summary>
    public void CancelArmedOrPendingAction()
    {
        if (mapViewState.ArmedActionId is not null || mapViewState.ArmedItemDefinitionId is not null)
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

    /// <summary>
    /// One hotkey slot per HotkeySlotLayout.Entries entry -- an unbound slot's press is silently
    /// a no-op (see HandleHotkeySlotPress), which is exactly what a slot with neither an
    /// ActionHotkeyBindingComponent nor an ItemHotkeyBindingComponent instance already produces,
    /// so no separate "is this slot enabled" check is needed here. A Shift-page Expansion slot
    /// (RequiresShift) only fires while Shift is actually held -- e.g. plain "1" and Shift+"1" are
    /// two different slots (Slot1 and Slot11) sharing the same physical key, distinguished only by
    /// current Shift state, not by two separate keys. Public: MapWindow.OnHotkeysAction calls this
    /// directly (alongside, and after, PlayerMovementController.HandleInput -- see that class's
    /// own doc comment for the ordering).
    /// </summary>
    public void HandleHotbarHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState)
    {
        var shiftHeld = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);

        foreach (var entry in HotkeySlotLayout.Entries)
        {
            if (entry.RequiresShift == shiftHeld && Window.WasKeyPressed(keyboardState, previousKeyboardState, entry.Key))
            {
                HandleHotkeySlotPress(entry.Slot);
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
        if (IsSlotLocked(slot))
        {
            return;
        }

        var isDoubleTap = _lastHotkeyPressFrameBySlot.TryGetValue(slot, out var lastPressFrame) &&
            _frameCounter - lastPressFrame <= DoubleTapWindowFrames;
        _lastHotkeyPressFrameBySlot[slot] = _frameCounter;

        if (ActionHotkeyBindingQueries.TryGet(actionHotkeyBindings, world.PlayerEntityId, slot, out var actionId))
        {
            HandleActionSlotPress(slot, actionId, isDoubleTap);
            return;
        }

        if (ItemHotkeyBindingQueries.TryGet(itemHotkeyBindings, world.PlayerEntityId, slot, out var itemDefinitionId))
        {
            HandleItemSlotPress(slot, itemDefinitionId, isDoubleTap);
        }
    }

    /// <summary>
    /// Mirrors HotbarContent's own lock check (HotkeySlotLayout.IsLocked) -- a locked slot must
    /// refuse to activate even if something bound it anyway (e.g. a blueprint grant that writes
    /// an ItemHotkeyBindingComponent directly, bypassing HotbarContent.BindItem's own lock
    /// refusal), not just render dim. Checked first, before any double-tap bookkeeping, so a
    /// press on a locked slot leaves no trace for once it becomes unlocked later.
    /// </summary>
    private bool IsSlotLocked(HotkeySlot slot)
    {
        var unlockedSlots = hotkeyExpansionUnlocks.TryGetReadonly(world.PlayerEntityId, out var unlock) ? unlock.UnlockedSlotCount : (short)0;
        return HotkeySlotLayout.IsLocked(slot, unlockedSlots);
    }

    /// <summary>
    /// Arms the pressed slot's action, or -- if it's already armed -- confirms it instead: a
    /// double-tap within DoubleTapWindowFrames skips arming entirely and immediately activates
    /// against an auto-picked target (see TryActivateWithAutoTarget); a slower re-press confirms
    /// against wherever the cursor currently is (see TryConfirmActivationAtTile), the same as a
    /// click would. Cancelling an armed slot is right-click/Escape's job now (see
    /// CancelArmedOrPendingAction) -- re-pressing the same key always means "go," not "nevermind."
    /// An action the player can't currently afford (see HasEnoughMana) is inert, the same no-op
    /// an unbound slot already is -- HotbarContent greys it out the same way (see its own
    /// isUsable check), so "can't be armed" and "looks unusable" stay in sync, mirroring
    /// HandleItemSlotPress's identical treatment of an unusable item slot.
    /// </summary>
    private void HandleActionSlotPress(HotkeySlot slot, Guid actionId, bool isDoubleTap)
    {
        if (!HasEnoughMana(world.PlayerEntityId, actionId))
        {
            return;
        }

        if (isDoubleTap)
        {
            TryActivateWithAutoTarget(world.PlayerEntityId, actionId);

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

        ArmAction(slot, actionId);
    }

    /// <summary>
    /// A bound item with no Activator (e.g. an Equipment/Tool item with no activated
    /// action yet), or with no remaining stock (the player's stack was fully consumed -- see
    /// InventoryItemStackComponent's "no instance means empty" convention, the same one
    /// InventoryActions.ConsumeItem relies on), is inert, the same no-op an unbound slot already
    /// is -- HotbarContent greys it out the same way, so "can't be armed" and "looks unusable"
    /// stay in sync. Any item tagged Tag.Self (Health/Mana/Hotkey Expansion Potion today) has its
    /// double-tap always activate on the caster's own tile (TryActivateItemOnSelf), skipping
    /// arm/target entirely -- "double-tap always uses it on the user," regardless of what's
    /// currently armed. Keyed off the tag rather than any particular IActionActivator kind, so a
    /// future non-Potion self-only item (e.g. a bandage) gets the same shortcut just by carrying
    /// Tag.Self. A non-double-tap re-press of an already-armed slot confirms against the cursor
    /// instead (see TryConfirmActivationAtTile) -- same rhythm as HandleActionSlotPress,
    /// cancelling is right-click/Escape's job now.
    /// </summary>
    private void HandleItemSlotPress(HotkeySlot slot, Guid itemDefinitionId, bool isDoubleTap)
    {
        if (!itemCatalog.TryGet(itemDefinitionId, out var item) || item.Activator is null)
        {
            return;
        }

        if (!InventoryQueries.TryGetStack(inventoryStacks, world.PlayerEntityId, itemDefinitionId, out var stack) || stack.Quantity <= 0)
        {
            return;
        }

        if (isDoubleTap && item.Tags.Contains(Tag.Self))
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

        ArmItem(slot, itemDefinitionId);
    }

    private void ArmAction(HotkeySlot slot, Guid actionId)
    {
        mapViewState.ArmedActionId = actionId;
        mapViewState.ArmedItemDefinitionId = null;
        mapViewState.ArmedSlot = slot;
        _targetableTilesOrigin = null; // Forces RefreshTargetableTiles below to (re)compute regardless of any stale origin left over from a previous arm.

        if (actionCatalog.TryGet(actionId, out var action) && transformPool.TryGetReadonly(world.PlayerEntityId, out var transform))
        {
            RefreshTargetableTiles(action.Activator.Targeting, transform.Position, transform.Size);
        }
    }

    /// <summary>Resolves targeting via TryGetArmedTargeting (not a parameter of its own) -- called after ArmedItemDefinitionId is already set above, so it reads back the correctly (Scroll-)scaled spec instead of a stale unscaled one, the same single-chokepoint reasoning as ArmAction re-fetching Activator.Targeting itself rather than taking it as a parameter.</summary>
    private void ArmItem(HotkeySlot slot, Guid itemDefinitionId)
    {
        mapViewState.ArmedItemDefinitionId = itemDefinitionId;
        mapViewState.ArmedActionId = null;
        mapViewState.ArmedSlot = slot;
        _targetableTilesOrigin = null;

        if (TryGetArmedTargeting(out var targeting) && transformPool.TryGetReadonly(world.PlayerEntityId, out var transform))
        {
            RefreshTargetableTiles(targeting, transform.Position, transform.Size);
        }
    }

    private void Disarm()
    {
        mapViewState.ArmedActionId = null;
        mapViewState.ArmedItemDefinitionId = null;
        mapViewState.ArmedSlot = null;
        mapViewState.TargetableTiles = null;
        _targetableTilesOrigin = null;
    }

    /// <summary>Mirrors ActionActivationSystem's own gate (see its own doc comment) -- a zero-cost action (or an unknown actionId, left for the actual activation attempt to reject) always passes, even with no ManaComponent pool wired in.</summary>
    private bool HasEnoughMana(int entityId, Guid actionId)
    {
        if (!actionCatalog.TryGet(actionId, out var action))
        {
            return true;
        }

        var manaCost = SpellActivator.ManaCostOf(action.Activator);
        if (manaCost <= 0)
        {
            return true;
        }

        return manaPool is not null && manaPool.TryGetReadonly(entityId, out var mana) && mana.CurrentMana >= manaCost;
    }

    /// <summary>
    /// Resolves whichever of {action, item} is currently armed to its shared TargetingSpec -- the
    /// one piece both kinds need for every targeting computation below, so callers stop caring
    /// which kind they're dealing with past this point. The single chokepoint every hover-preview/
    /// arm-highlight/confirm-click path reads through (ArmItem calls this too, after setting
    /// ArmedItemDefinitionId, rather than taking a targeting parameter of its own), so a
    /// ScrollActivator item's Range/AreaSize scaling (see ScaleScrollTargeting) applies
    /// consistently everywhere instead of only on some call sites.
    /// </summary>
    private bool TryGetArmedTargeting(out TargetingSpec targeting)
    {
        if (mapViewState.ArmedActionId is { } actionId && actionCatalog.TryGet(actionId, out var action))
        {
            targeting = action.Activator.Targeting;
            return true;
        }

        if (mapViewState.ArmedItemDefinitionId is { } itemId && itemCatalog.TryGet(itemId, out var item) && item.Activator is { } activator)
        {
            targeting = activator is ScrollActivator ? ScaleScrollTargeting(activator.Targeting) : activator.Targeting;
            return true;
        }

        targeting = null!;
        return false;
    }

    /// <summary>Scales baseTargeting's Range/AreaSize by the player's own Intelligence -- see ScrollScalingEffects's own doc comment. No-op (returns baseTargeting unchanged) when abilityScores isn't wired or the player has no Intelligence score, the same "1.0 multiplier" fallback ScrollScalingEffects.ComputeScaleMultiplier itself defaults to.</summary>
    private TargetingSpec ScaleScrollTargeting(TargetingSpec baseTargeting)
    {
        if (abilityScores is null || !AbilityScoreQueries.TryGetComponent(abilityScores, world.PlayerEntityId, AbilityScoreType.Intelligence, out var intelligence))
        {
            return baseTargeting;
        }

        return ScrollScalingEffects.ScaleTargeting(baseTargeting, ScrollScalingEffects.ComputeScaleMultiplier(intelligence.Total));
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
    /// attackerPosition -- Adjacent/AdjacentWithSelf's fixed perimeter-around-the-attacker's-
    /// footprint (plus, for AdjacentWithSelf, the footprint itself -- see TargetShapeResolver's
    /// own doc comment), or every tile within Range for every cursor-directed shape
    /// (SingleTarget/Burst/Line/Cone) via a Burst-shaped scatter, not the real Shape -- there's no
    /// single "aim direction" yet at arm time, only a reachable area. Shared by Arm (for
    /// highlighting) and TryActivateWithAutoTarget (for double-tap's candidate pool), so the two
    /// never drift out of sync with each other. Also what makes a manual click on the caster's own
    /// tile resolve at all for an AdjacentWithSelf item (e.g. Scroll of Healing) -- TargetableTiles
    /// has to actually contain that tile before TryConfirmActivationAtTile's Tag.Self special case
    /// (or the general resolve path) is ever reached.
    /// </summary>
    private void ComputeTargetableTiles(Vector3Int attackerPosition, Vector2Byte attackerSize, TargetingSpec targeting, List<Vector3Int> buffer)
    {
        if (targeting.Shape is TargetShape.Adjacent or TargetShape.AdjacentWithSelf)
        {
            TargetShapeResolver.Resolve(targeting.Shape, attackerPosition, attackerSize, attackerPosition, range: 0, areaSize: 0, world.Map.Size, buffer);
            return;
        }

        TargetShapeResolver.Resolve(TargetShape.Burst, attackerPosition, attackerSize, attackerPosition, range: 0, targeting.Range, world.Map.Size, buffer);
    }

    /// <summary>
    /// Resolves and queues a full action activation with no manual click-confirm at all -- the
    /// double-tap path. Adjacent/AdjacentWithSelf's footprint never depends on a target choice
    /// (it's always the caster's own tile plus its 8 surrounding neighbors, for AdjacentWithSelf
    /// including the caster's own tile in the resolved set), so it's queued immediately. Every other
    /// shape needs a target tile chosen first: ComputeTargetableTiles' reachable-area candidates
    /// are filtered down to occupied tiles and handed to TargetPriority.SelectAutoTarget, using
    /// MapViewState.HoveredTile as the cursor bias when one is already tracked (armed-and-then-
    /// double-tapped in one motion means Update hasn't run with the arm in effect yet, so
    /// HoveredTile can still be stale/null on the very first pair -- attackerPosition is the
    /// fallback for exactly that case, which is also what makes "closest to cursor" degenerate
    /// harmlessly into "closest to the caster" rather than picking an arbitrary target).
    /// </summary>
    private void TryActivateWithAutoTarget(int entityId, Guid actionId)
    {
        if (!actionCatalog.TryGet(actionId, out var action) || !transformPool.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        var attackerPosition = transform.Position;
        var attackerSize = transform.Size;
        var mapSize = world.Map.Size;
        var targeting = action.Activator.Targeting;

        if (targeting.Shape is TargetShape.Adjacent or TargetShape.AdjacentWithSelf)
        {
            ComputeTargetableTiles(attackerPosition, attackerSize, targeting, _candidateTilesBuffer);
            QueueActionActivation(entityId, actionId, _candidateTilesBuffer);
            return;
        }

        // Self's only candidate tile is the caster's own position, which the occupied-tile
        // filter below would always exclude (it deliberately drops tiles occupied by the
        // caster itself, meant for every *other* shape) -- so Self needs its own direct
        // resolve/queue, the same as Adjacent above, rather than falling through into that filter.
        if (targeting.Shape == TargetShape.Self)
        {
            QueueActionActivation(entityId, actionId, [attackerPosition]);
            return;
        }

        ComputeTargetableTiles(attackerPosition, attackerSize, targeting, _candidateTilesBuffer);

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

        TargetShapeResolver.Resolve(targeting.Shape, attackerPosition, attackerSize, chosenTile, targeting.Range, targeting.AreaSize, mapSize, _finalTargetTilesBuffer);
        QueueActionActivation(entityId, actionId, _finalTargetTilesBuffer);
    }

    /// <summary>The double-tap path for a Potion -- always the caster's own tile, no candidate search at all (contrast TryActivateWithAutoTarget's action equivalent).</summary>
    private void TryActivateItemOnSelf(int entityId, Guid itemDefinitionId)
    {
        if (!transformPool.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        QueueConsumableActivation(entityId, itemDefinitionId, [transform.Position]);
    }

    /// <summary>Presentation only ever queues an activation request -- ActionActivationSystem is the only thing that applies gameplay effects. Mirrors TryQueuePlayerMove's own queue-and-let-a-system-consume pattern for movement.</summary>
    private void QueueActionActivation(int entityId, Guid actionId, List<Vector3Int> targetTiles)
    {
        if (targetTiles.Count == 0)
        {
            return;
        }

        pendingActivations.Merge(entityId, new PendingActionActivationComponent(actionId, targetTiles.ToArray()));
    }

    /// <summary>Item counterpart to QueueActionActivation -- ConsumableActivationSystem is the only thing that applies its gameplay effects.</summary>
    private void QueueConsumableActivation(int entityId, Guid itemDefinitionId, List<Vector3Int> targetTiles)
    {
        if (targetTiles.Count == 0)
        {
            return;
        }

        pendingConsumableActivations.Merge(entityId, new PendingConsumableActivationComponent(itemDefinitionId, targetTiles.ToArray()));
    }

    /// <summary>Dispatches a confirmed click activation to whichever of {action, item} MapViewState currently has armed -- see TryConfirmActivation, the only caller.</summary>
    private void QueueArmedActivation(int entityId, List<Vector3Int> targetTiles)
    {
        if (mapViewState.ArmedActionId is { } actionId)
        {
            QueueActionActivation(entityId, actionId, targetTiles);
        }
        else if (mapViewState.ArmedItemDefinitionId is { } itemId)
        {
            QueueConsumableActivation(entityId, itemId, targetTiles);
        }
    }

}
