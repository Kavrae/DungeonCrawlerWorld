using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory;
using Game.Tags;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.MapRendering;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.UI;

/// <summary>  Player's moment-to-moment action input </summary>
/// <remarks>
/// Arming/disarming/confirming/auto-targeting actions and usable items via their hotbar hotkeys.
/// Player movement is a separate concern handled by the sibling PlayerMovementController.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
/// <param name="simulationClock">"CurrentFrame" for clearing the shared action lock on cancellation -- the lock is a deadline (see ActionLockGate).</param>
public sealed class ActionTargetingController(
    World world,
    MapViewState mapViewState,
    MapCamera camera,
    UiLayerStack uiLayers,
    ItemCatalog itemCatalog,
    TransformView transformView,
    HotkeyBindingView hotkeyBindingView,
    InventoryView inventoryView,
    ActionStateView actionStateView,
    TargetingView targetingView,
    PlayerCommands playerCommands,
    SimulationClock simulationClock)
{

    // Reused across calls (see TargetShapeResolver's own doc comment on why Resolve writes into
    // a caller-owned buffer instead of allocating).
    private readonly List<Vector3Int> _candidateTilesBuffer = [];
    private readonly List<Vector3Int> _targetCandidateTilesBuffer = [];

    /// <summary>
    /// Backs MapViewState.TargetableTiles -- populated by RefreshTargetableTiles (Clear +
    /// repopulate) rather than replaced with a fresh HashSet every arm/move, since a HashSet
    /// allocation here runs against a heap already holding this world's entity-indexed component
    /// arrays (see CLAUDE.md's Scale note); a GC pass triggered at just the wrong moment against
    /// that heap is exactly the kind of one-time stutter a per-call allocation risks causing.
    /// </summary>
    private readonly HashSet<Vector3Int> _targetableTilesSet = [];

    /// <summary>The caster position TargetableTiles was last computed from -- lets RefreshTargetableTiles (called every frame something is armed) cheaply skip recomputation on frames where the caster hasn't moved.</summary>
    private Vector3Int? _targetableTilesOrigin;

    /// <summary>The armed action/item's actual hit-footprint at the current hover position, recomputed every Update (see UpdateHoveredTile).</summary>
    private readonly List<Vector3Int> _hoveredFootprintBuffer = [];

    /// <summary>
    /// Companion to _hoveredFootprintBuffer for O(1) membership checks (see
    /// HoveredFootprintContains) -- rebuilt from the buffer in lockstep every UpdateHoveredTile
    /// call, the same "list for order/indexing, parallel set for membership" split
    /// _targetableTilesSet already uses relative to _candidateTilesBuffer.
    /// </summary>
    private readonly HashSet<Vector3Int> _hoveredFootprintSet = [];

    /// <summary>Read-only view of _hoveredFootprintBuffer for tests -- same internal-for-test-visibility pattern as UiInputController.CurrentCursor/DragDelta.</summary>
    internal IReadOnlyList<Vector3Int> HoveredFootprint => _hoveredFootprintBuffer;

    /// <summary>O(1) via _hoveredFootprintSet -- MapWindow's targeting-highlight draw calls this once per visible targetable tile, every frame something is armed, so a linear List.Contains here would cost O(TargetableTiles.Count * HoveredFootprint.Count) per frame instead.</summary>
    internal bool HoveredFootprintContains(Vector3Int tile) => _hoveredFootprintSet.Contains(tile);

    private readonly List<Vector3Int> _playerWindupTilesBuffer = [];

    /// <summary>Where the player's own windup would land if it ended now, or null if there is none.</summary>
    /// <remarks>
    /// Resolved every read through TargetingView -- the resolution the windup itself ends with -- so a
    /// Target-mode windup's highlight follows its target. See MapWindow.DrawTargetingHighlights' own
    /// doc comment for why this is the fallback highlight once TargetableTiles/HoveredFootprint
    /// themselves are cleared: once a Delayed ability is actually queued, Disarm already clears both
    /// (there's nothing left to aim), but the player benefits from still seeing exactly which tiles
    /// are about to be hit once the windup ends.
    /// </remarks>
    internal IReadOnlyList<Vector3Int>? PendingWindupTargetTiles =>
        actionStateView.TryGetPendingWindup(world.PlayerEntityId, out var pending) && targetingView.TryResolveWindup(world.PlayerEntityId, in pending, _playerWindupTilesBuffer)
            ? _playerWindupTilesBuffer
            : null;

    private readonly List<(int EntityId, PendingWindupComponent Pending)> _localPendingWindupsScratch = [];

    private readonly List<(int EntityId, IReadOnlyList<Vector3Int> TargetTiles, bool IsDodgeable)> _pendingWindupTargetsBuffer = [];

    /// <summary>One reused tile list per windup drawn, grown to the most windups seen in a frame.</summary>
    private readonly List<List<Vector3Int>> _windupTileListPool = [];

    /// <summary>
    /// Every entity mid-windup within the Local tier (the player always), with where its windup would land if it ended
    /// now and whether that action is Dodgeable -- so MapWindow can telegraph an enemy's incoming attack
    /// (red/yellow, see CombatTargetPalette) as well as the player's own (dark green), following a Target-mode target.
    /// </summary>
    /// <remarks>Which windups count as Local is ActionStateView.CopyLocalPendingWindups'.</remarks>
    public IReadOnlyList<(int EntityId, IReadOnlyList<Vector3Int> TargetTiles, bool IsDodgeable)> AllPendingWindupTargets()
    {
        actionStateView.CopyLocalPendingWindups(world.PlayerEntityId, _localPendingWindupsScratch);

        _pendingWindupTargetsBuffer.Clear();
        foreach (var (entityId, pending) in _localPendingWindupsScratch)
        {
            if (_windupTileListPool.Count <= _pendingWindupTargetsBuffer.Count)
            {
                _windupTileListPool.Add([]);
            }

            var tiles = _windupTileListPool[_pendingWindupTargetsBuffer.Count];
            if (!targetingView.TryResolveWindup(entityId, in pending, tiles))
            {
                continue;
            }

            var isDodgeable = targetingView.IsWindupDodgeable(entityId, in pending);
            _pendingWindupTargetsBuffer.Add((entityId, tiles, isDodgeable));
        }

        return _pendingWindupTargetsBuffer;
    }

    /// <summary>Disarms whatever is armed once the player can no longer use it -- called once per MapWindow.Update, before anything else this class does that frame.</summary>
    /// <remarks>An armed action whose arms are destroyed mid-aim, or whose mana a hit drains, would otherwise stay armed with nothing a confirm could do.</remarks>
    public void Tick()
    {
        if (IsArmedBlocked())
        {
            Disarm();
        }
    }

    private bool IsArmedBlocked()
    {
        if (mapViewState.ArmedActionId is { } armedActionId)
        {
            return actionStateView.GetActionBlocker(world.PlayerEntityId, armedActionId) != ActivationBlocker.None;
        }

        if (mapViewState.ArmedItemStackInstanceId is { } armedStackInstanceId)
        {
            return actionStateView.GetItemBlocker(world.PlayerEntityId, armedStackInstanceId) != ActivationBlocker.None;
        }

        return false;
    }

    /// <summary>Something is armed and a confirm of it would be refused right now (see CanConfirmArmed) -- what UiInputController shows the disabled cursor over the map for.</summary>
    internal bool IsArmedConfirmRefused =>
        (mapViewState.ArmedActionId is not null || mapViewState.ArmedItemStackInstanceId is not null) && !CanConfirmArmed();

    /// <summary>Whether a confirm of whatever is armed would be accepted now: it isn't blocked, and PlayerCommands would queue it (ready before the input buffer expires).</summary>
    /// <remarks>A refused confirm leaves the action armed, so the player can confirm again once it's ready.</remarks>
    private bool CanConfirmArmed()
    {
        if (IsArmedBlocked())
        {
            return false;
        }

        if (mapViewState.ArmedActionId is { } armedActionId)
        {
            return playerCommands.CanQueueAction(armedActionId);
        }

        return mapViewState.ArmedItemStackInstanceId is not { } armedStackInstanceId || playerCommands.CanQueueItemActivation(armedStackInstanceId);
    }

    /// <summary>
    /// While an action or item is armed, tracks which map tile the mouse is currently over (on
    /// the player's own Z layer, not necessarily whatever layer the camera happens to be showing)
    /// and, if so, recomputes the armed thing's actual hit-footprint for a click there -- the same
    /// selection a click makes (TrySelectActivationTarget), so the highlight is what lands, moving
    /// with the cursor or onto the target Target mode would pick. Also refreshes TargetableTiles (see
    /// RefreshTargetableTiles) every call, independent of whether the mouse currently resolves
    /// to a map tile at all, so the reachable-area highlight keeps following the caster if it
    /// moves while armed even with the cursor off the map. mousePosition is null when the cursor
    /// isn't over the map window (it is over another window, such as the hotbar). Takes the mouse
    /// position and the host window's content-area origin explicitly rather than reading either
    /// itself, so tests can simulate a mouse position without a real OS cursor or a real Window subclass.
    /// </summary>
    public void UpdateHoveredTile(Point? mousePosition, Vector2 contentAbsolutePosition)
    {
        _hoveredFootprintBuffer.Clear();
        _hoveredFootprintSet.Clear();

        if (!TryGetArmedActivator(out var activator) || !transformView.TryGetTransform(world.PlayerEntityId, out var playerTransform))
        {
            mapViewState.HoveredTile = null;
            return;
        }

        RefreshTargetableTiles(targetingView.EffectiveSpec(world.PlayerEntityId, activator), playerTransform.Position, playerTransform.Size);

        if (mousePosition is not { } mouse || !camera.TryGetHoveredMapPosition(mouse, contentAbsolutePosition, out var hoveredColumnRow))
        {
            mapViewState.HoveredTile = null;
            return;
        }

        var hoveredTile = new Vector3Int(hoveredColumnRow.X, hoveredColumnRow.Y, playerTransform.Position.Z);
        mapViewState.HoveredTile = hoveredTile;

        if (TrySelectActivationTarget(activator, hoveredTile, isMapClick: true, out _, out var selection))
        {
            targetingView.Resolve(world.PlayerEntityId, activator, selection, _hoveredFootprintBuffer);
        }

        foreach (var tile in _hoveredFootprintBuffer)
        {
            _hoveredFootprintSet.Add(tile);
        }
    }

    /// <summary>
    /// A left-click activates the armed action or item against whichever tile was clicked -- see
    /// TryConfirmActivationAtTile, the shared implementation this and Dodge's WASD confirm (see
    /// TryClaimDodgeDirectionalKey) both funnel into.
    /// </summary>
    public void TryConfirmActivation(Point mousePosition, Vector2 contentAbsolutePosition)
    {
        if (!camera.TryGetHoveredMapPosition(mousePosition, contentAbsolutePosition, out var clickedColumnRow) ||
            !transformView.TryGetTransform(world.PlayerEntityId, out var transform))
        {
            return;
        }

        var clickedTile = new Vector3Int(clickedColumnRow.X, clickedColumnRow.Y, transform.Position.Z);
        TryConfirmActivationAtTile(clickedTile);
    }

    /// <summary>Activates the armed action or item as a map click on targetTile (see TrySelectActivationTarget); with nothing to land on it stays armed, exactly like clicking empty space doesn't clear an inspector selection either.</summary>
    private void TryConfirmActivationAtTile(Vector3Int targetTile) => TryActivateArmed(targetTile, isMapClick: true);

    /// <summary>
    /// Cancels an armed action/item (right-click tap or Escape), or, if nothing is armed, drops
    /// the command PlayerCommands is holding, or, if there is none, cancels a Delayed action's
    /// in-progress windup instead (WindupCancel, releasing the shared ActionLock) so cancelling
    /// frees the entity immediately rather than still waiting out the full wind-up with no
    /// effect at the end -- see PendingWindupComponent's own doc comment. Returns whether
    /// there was actually anything to cancel -- MapWindow's own right-click-tap handler uses this
    /// to decide whether a corpse context menu should open instead (a no-op cancel means the
    /// right-click wasn't "cancel," so it falls through to whatever else is under the cursor).
    /// </summary>
    public bool CancelArmedOrPendingAction()
    {
        if (mapViewState.ArmedActionId is not null || mapViewState.ArmedItemStackInstanceId is not null)
        {
            Disarm();
            return true;
        }

        if (playerCommands.Clear())
        {
            return true;
        }

        return playerCommands.TryCancelWindup(simulationClock.CurrentFrame);
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
    /// <summary>WASD-to-direction offsets Dodge confirms toward -- diagonals aren't reachable by a single key today (see MapWindow's own WASD hint glyph, which only ever marks these four cardinal tiles), matching this same set.</summary>
    private static readonly (Keys Key, Vector3Int Direction)[] DodgeDirectionalKeys =
    [
        (Keys.W, new Vector3Int(0, -1, 0)),
        (Keys.S, new Vector3Int(0, 1, 0)),
        (Keys.A, new Vector3Int(-1, 0, 0)),
        (Keys.D, new Vector3Int(1, 0, 0)),
    ];

    /// <summary>
    /// While Dodge is armed, a freshly-pressed WASD key confirms Dodge toward that direction
    /// instead of moving normally -- adds the key to claimedKeys so PlayerMovementController.
    /// HandleInput skips it this frame (see that class's own doc comment on the claimed-keys
    /// mechanism). Called by MapWindow.OnHotkeysAction before PlayerMovementController.HandleInput,
    /// every frame, whether or not Dodge is actually armed -- a no-op the rest of the time.
    /// </summary>
    public void TryClaimDodgeDirectionalKey(KeyboardState keyboardState, KeyboardState previousKeyboardState, HashSet<Keys> claimedKeys)
    {
        if (mapViewState.ArmedActionId != BuiltInActionIds.Dodge || !transformView.TryGetTransform(world.PlayerEntityId, out var transform))
        {
            return;
        }

        foreach (var (key, direction) in DodgeDirectionalKeys)
        {
            if (!Window.WasKeyPressed(keyboardState, previousKeyboardState, key))
            {
                continue;
            }

            claimedKeys.Add(key);
            TryConfirmActivationAtTile(transform.Position + direction);
            return;
        }
    }

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
    /// slot, so a click behaves identically to pressing that slot's key.
    /// </summary>
    internal void HandleHotkeySlotPress(HotkeySlot slot)
    {
        if (IsSlotLocked(slot))
        {
            return;
        }

        if (hotkeyBindingView.TryGetBoundAction(world.PlayerEntityId, slot, out var actionId))
        {
            HandleActionSlotPress(slot, actionId);
            return;
        }

        if (hotkeyBindingView.TryGetBoundItem(world.PlayerEntityId, slot, out var stackInstanceId))
        {
            HandleItemSlotPress(slot, stackInstanceId);
        }
    }

    /// <summary>
    /// Mirrors HotbarContent's own lock check (HotkeySlotLayout.IsLocked) -- a locked slot must
    /// refuse to activate even if something bound it anyway (e.g. a blueprint grant that writes
    /// an ItemHotkeyBindingComponent directly, bypassing HotbarContent.BindItem's own lock
    /// refusal), not just render dim.
    /// </summary>
    private bool IsSlotLocked(HotkeySlot slot)
    {
        hotkeyBindingView.TryGetUnlockedExpansionSlots(world.PlayerEntityId, out var unlockedSlots);
        return HotkeySlotLayout.IsLocked(slot, unlockedSlots);
    }

    /// <summary>
    /// Arms the pressed slot's action, or -- if it's already armed -- activates it (see
    /// TryActivateArmedFromHotkey), however quickly the press follows the one that armed it.
    /// Cancelling an armed slot is right-click/Escape's job (see CancelArmedOrPendingAction) --
    /// re-pressing the same key always means "go," not "nevermind."
    /// An action the player can't use right now (ActionStateView.GetActionBlocker -- not enough
    /// mana, melee disabled) is inert, the same no-op an unbound slot already is -- HotbarContent
    /// greys it out through the same query, so "can't be armed" and "looks unusable" stay in sync,
    /// mirroring HandleItemSlotPress's identical treatment of an unusable item slot.
    /// </summary>
    private void HandleActionSlotPress(HotkeySlot slot, Guid actionId)
    {
        if (actionStateView.GetActionBlocker(world.PlayerEntityId, actionId) != ActivationBlocker.None)
        {
            return;
        }

        // A toggle action is never armed: it applies to the player, so every press queues it at
        // once on the player's own tile, in the player's mode (Ground anchors what it places there), and no window closes.
        if (actionStateView.TryGetEffectiveAction(world.PlayerEntityId, actionId, out var pressedAction) && pressedAction.Toggle is not null)
        {
            if (transformView.TryGetTransform(world.PlayerEntityId, out var playerTransform))
            {
                playerCommands.QueueAction(actionId, targetingView.Select(world.PlayerEntityId, pressedAction.Activator, mapViewState.TargetingMode, playerTransform.Position, simulationClock.CurrentFrame));
            }

            return;
        }

        if (mapViewState.ArmedSlot == slot)
        {
            TryActivateArmedFromHotkey();
            return;
        }

        ArmAction(slot, actionId);
    }

    /// <summary>
    /// A bound item the player can't use right now (ActionStateView.GetItemBlocker -- e.g. an
    /// Equipment/Tool item with no activated action yet), or with no remaining stock (the player's stack was fully consumed -- see
    /// InventoryItemStackComponent's "no instance means empty" convention, the same one
    /// InventoryActions.RemoveOneUnit relies on), is inert, the same no-op an unbound slot already
    /// is -- HotbarContent greys it out the same way, so "can't be armed" and "looks unusable"
    /// stay in sync. Otherwise the same rhythm as HandleActionSlotPress: the first press arms, a
    /// re-press of the armed slot activates (see TryActivateArmedFromHotkey). A toggle item is
    /// never armed: every press queues it at once (see QueueToggleItem).
    /// </summary>
    private void HandleItemSlotPress(HotkeySlot slot, uint stackInstanceId)
    {
        if (!inventoryView.TryGetStack(world.PlayerEntityId, stackInstanceId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) ||
            actionStateView.GetItemBlocker(world.PlayerEntityId, stackInstanceId) != ActivationBlocker.None)
        {
            return;
        }

        if (item.Toggle is not null)
        {
            QueueToggleItem(stackInstanceId, slot);
            return;
        }

        if (mapViewState.ArmedSlot == slot)
        {
            TryActivateArmedFromHotkey();
            return;
        }

        ArmItem(slot, stackInstanceId);
    }

    /// <summary>Closes every closable window before arming -- the player is committing to targeting on the map, which a window left open (Inventory, Ability Scores, a future Magic Menu, ...) would otherwise block. See UiLayerStack.CloseAllClosableWindows's own doc comment for why this is a separate sweep from Escape-hold's.</summary>
    private void ArmAction(HotkeySlot slot, Guid actionId)
    {
        uiLayers.CloseAllClosableWindows();

        mapViewState.ArmedActionId = actionId;
        mapViewState.ArmedItemStackInstanceId = null;
        mapViewState.ArmedSlot = slot;
        _targetableTilesOrigin = null; // Forces RefreshTargetableTiles below to (re)compute regardless of any stale origin left over from a previous arm.

        if (actionStateView.TryGetEffectiveAction(world.PlayerEntityId, actionId, out var action) && transformView.TryGetTransform(world.PlayerEntityId, out var transform))
        {
            RefreshTargetableTiles(action.Activator.Targeting, transform.Position, transform.Size);
        }
    }

    /// <summary>Resolves targeting via TryGetArmedTargeting (not a parameter of its own) -- called after ArmedItemStackInstanceId is already set above, so it reads back the correctly (Scroll-)scaled spec instead of a stale unscaled one, the same single-chokepoint reasoning as ArmAction re-fetching Activator.Targeting itself rather than taking it as a parameter. Also, critically, what makes a diverged stack's own Override targeting (e.g. a wand with non-default Targeting) actually apply -- TryGetArmedTargeting resolves through the bound stack itself, not a bare catalog lookup by item id. slot is null for a menu-driven arm with no originating HotkeySlot (see ArmItemFromStack) -- every ArmedSlot consumer already treats null as "no slot to highlight/reuse for a same-slot re-press confirm," which is exactly correct there: a menu-armed item can only ever be confirmed via a map-tile click.</summary>
    private void ArmItem(HotkeySlot? slot, uint stackInstanceId)
    {
        uiLayers.CloseAllClosableWindows(); // See ArmAction's own doc comment -- same reasoning, covers ArmItemFromStack (Inventory's Activate/double-click) too, since it delegates here.

        mapViewState.ArmedItemStackInstanceId = stackInstanceId;
        mapViewState.ArmedActionId = null;
        mapViewState.ArmedSlot = slot;
        _targetableTilesOrigin = null;

        if (TryGetArmedTargeting(out var targeting) && transformView.TryGetTransform(world.PlayerEntityId, out var transform))
        {
            RefreshTargetableTiles(targeting, transform.Position, transform.Size);
        }
    }

    /// <summary>
    /// Arms an item directly by StackInstanceId, with no originating HotkeySlot -- the entry point
    /// for InventoryGridContent's "Activate" context-menu option and double-click gesture (see their
    /// own doc comments). Reuses the exact eligibility guard HandleItemSlotPress applies before
    /// arming from a hotbar press, and ArmItem's own targeting-refresh logic unchanged -- no separate
    /// activation path -- arming here matches the first press of an unarmed hotbar slot exactly.
    /// That includes a toggle item, which a hotbar press queues at once rather than arming (see
    /// QueueToggleItem). With no slot, the armed item is confirmed only by a map-tile click.
    /// </summary>
    public void ArmItemFromStack(uint stackInstanceId)
    {
        if (!inventoryView.TryGetStack(world.PlayerEntityId, stackInstanceId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) ||
            actionStateView.GetItemBlocker(world.PlayerEntityId, stackInstanceId) != ActivationBlocker.None)
        {
            return;
        }

        if (item.Toggle is not null)
        {
            QueueToggleItem(stackInstanceId, activatedFromSlot: null);
            return;
        }

        ArmItem(null, stackInstanceId);
    }

    /// <summary>Queues a toggle item's activation on the player's own tile, with nothing armed and no window closed.</summary>
    /// <remarks>A toggle applies to its holder, so there is no target to pick and nothing on the map to make room for. Whatever was armed stays armed.</remarks>
    private void QueueToggleItem(uint stackInstanceId, HotkeySlot? activatedFromSlot)
    {
        if (transformView.TryGetTransform(world.PlayerEntityId, out var transform))
        {
            playerCommands.QueueItemActivation(stackInstanceId, TargetSelection.Ground(transform.Position), activatedFromSlot);
        }
    }

    private void Disarm()
    {
        mapViewState.ArmedActionId = null;
        mapViewState.ArmedItemStackInstanceId = null;
        mapViewState.ArmedSlot = null;
        mapViewState.TargetableTiles = null;
        _targetableTilesOrigin = null;
    }

    /// <summary>
    /// The activator of whichever of {action, item} is currently armed -- the one piece both kinds
    /// need for every targeting computation below, so callers stop caring which kind they're
    /// dealing with past this point. Resolves the armed item through its bound *stack*
    /// (InventoryQueries.TryResolveEffectiveItem), not a bare catalog lookup by item id -- a
    /// diverged stack's own Override (e.g. a wand carrying non-default Targeting) is what actually
    /// gets read, not always the catalog original.
    /// </summary>
    private bool TryGetArmedActivator(out IActionActivator activator)
    {
        if (mapViewState.ArmedActionId is { } actionId && actionStateView.TryGetEffectiveAction(world.PlayerEntityId, actionId, out var action))
        {
            activator = action.Activator;
            return true;
        }

        if (mapViewState.ArmedItemStackInstanceId is { } stackInstanceId &&
            inventoryView.TryGetStack(world.PlayerEntityId, stackInstanceId, out var stack) &&
            InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) &&
            item.Activator is { } itemActivator)
        {
            activator = itemActivator;
            return true;
        }

        activator = null!;
        return false;
    }

    /// <summary>
    /// The armed activator's targeting as it applies to the player now (TargetingView.EffectiveSpec:
    /// a scroll's Range/AreaSize scaled by the player's Intelligence in Game, the same scaling the
    /// activation is resolved with). The single chokepoint every arm-highlight path reads through
    /// (ArmItem calls this too, after setting ArmedItemStackInstanceId, rather than taking a
    /// targeting parameter of its own).
    /// </summary>
    private bool TryGetArmedTargeting(out TargetingSpec targeting)
    {
        if (TryGetArmedActivator(out var activator))
        {
            targeting = targetingView.EffectiveSpec(world.PlayerEntityId, activator);
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

    /// <summary>Whether shape needs no cursor position at all to resolve its real footprint -- true exactly when it's built entirely from Adjacent/Self bits (both always caster-centered, regardless of what else is combined with them). Scales automatically as TargetShape gains flags later, instead of a hand-maintained "these specific shapes" list.</summary>
    private static bool IsCursorIndependent(TargetShape shape) => (shape & ~(TargetShape.Adjacent | TargetShape.Self)) == 0;

    /// <summary>
    /// The full universe of tiles the given targeting could possibly be aimed at from
    /// attackerPosition -- a cursor-independent shape's (Adjacent and/or Self) fixed perimeter/
    /// footprint around the attacker (see TargetShapeResolver's own doc comment), or every tile
    /// within Range for every cursor-directed shape (SingleTarget/Burst/Line/Cone) via a
    /// Burst-shaped scatter, not the real Shape -- there's no single "aim direction" yet at arm
    /// time, only a reachable area. Through RefreshTargetableTiles, both the highlight and
    /// NearestTargetTile's candidate pool, so the two never drift out of sync with each other.
    /// </summary>
    private void ComputeTargetableTiles(Vector3Int attackerPosition, Vector2Byte attackerSize, TargetingSpec targeting, List<Vector3Int> buffer)
    {
        if (IsCursorIndependent(targeting.Shape))
        {
            TargetShapeResolver.Resolve(targeting.Shape, attackerPosition, attackerSize, attackerPosition, range: 0, areaSize: 0, world.Map.Bounds, buffer);
            return;
        }

        // SingleTarget + Chebyshev + Range 1 (Dodge's own targeting) is exactly Adjacent | Self's
        // resolved footprint -- the self+8-neighbor block -- so reuse that exact resolve for the
        // arm-time preview instead of the generic Manhattan-Burst approximation below, which would
        // otherwise show the wrong 5-tile diamond (cardinal neighbors only) rather than the real
        // 9-tile block. Not worth a general arbitrary-radius Chebyshev scatter until something
        // besides Dodge needs Range > 1 here.
        if (targeting.Shape == TargetShape.SingleTarget && targeting.Metric == DistanceMetric.Chebyshev && targeting.Range == 1)
        {
            TargetShapeResolver.Resolve(TargetShape.Adjacent | TargetShape.Self, attackerPosition, attackerSize, attackerPosition, range: 0, areaSize: 0, world.Map.Bounds, buffer);
            return;
        }

        TargetShapeResolver.Resolve(TargetShape.Burst, attackerPosition, attackerSize, attackerPosition, range: 0, targeting.Range, world.Map.Bounds, buffer);
    }

    /// <summary>Activates whatever is armed without a map click -- a re-press of its hotbar slot, by key or by clicking the slot -- against the hovered tile, if the cursor is on the map.</summary>
    private void TryActivateArmedFromHotkey() => TryActivateArmed(mapViewState.HoveredTile, isMapClick: false);

    /// <summary>Queues and disarms whatever is armed, aimed by TrySelectActivationTarget; nothing to land on, or a confirm that would be refused, leaves it armed with nothing paid.</summary>
    private void TryActivateArmed(Vector3Int? cursorTile, bool isMapClick)
    {
        if (!TryGetArmedActivator(out var activator) || !CanConfirmArmed())
        {
            return;
        }

        if (!TrySelectActivationTarget(activator, cursorTile, isMapClick, out var aimedTile, out var selection))
        {
            return;
        }

        QueueArmedActivation(world.PlayerEntityId, aimedTile, selection);
        Disarm();
    }

    /// <summary>Where an activation of the armed activator lands, with the cursor on cursorTile (null when it isn't on the map) -- one rule for every way to activate, and for the hover preview.</summary>
    /// <remarks>
    /// In order: an Adjacent/Self shape lands around the caster, whatever the mode. A GameTags.TargetingSelf
    /// action or item used from the hotbar lands on the player (a potion is drunk, Dodge dodges in
    /// place); a map click follows the mode instead. Ground mode (the player's, or an activator that
    /// offers no other) lands on cursorTile when it is in reach -- a potion splashes there. Target mode
    /// marks the target on cursorTile when it is in reach; else, for a TargetingSelf map click, the
    /// player; else the target in reach nearest cursorTile, or nearest the player with the cursor off
    /// the map, within MapViewState.TargetSearchDistanceTiles of it. False when there is nothing to land on.
    /// </remarks>
    /// <param name="aimedTile">The tile aimed at -- for Dodge, the tile it steps to.</param>
    private bool TrySelectActivationTarget(IActionActivator activator, Vector3Int? cursorTile, bool isMapClick, out Vector3Int aimedTile, out TargetSelection selection)
    {
        aimedTile = default;
        selection = default;

        var playerEntityId = world.PlayerEntityId;
        if (!transformView.TryGetTransform(playerEntityId, out var playerTransform))
        {
            return false;
        }

        var playerPosition = playerTransform.Position;
        var now = simulationClock.CurrentFrame;
        var targeting = targetingView.EffectiveSpec(playerEntityId, activator);

        if (IsCursorIndependent(targeting.Shape))
        {
            aimedTile = playerPosition;
            selection = targetingView.Select(playerEntityId, activator, TargetingMode.Ground, playerPosition, now);
            return true;
        }

        var isSelfTargeting = IsArmedSelfTargeting();
        if (isSelfTargeting && !isMapClick)
        {
            aimedTile = playerPosition;
            selection = targetingView.SelectEntity(playerEntityId, activator, playerEntityId);
            return true;
        }

        RefreshTargetableTiles(targeting, playerPosition, playerTransform.Size);
        var cursorTileInReach = cursorTile is { } tile && _targetableTilesSet.Contains(tile) ? cursorTile : null;
        var mode = targeting.Modes == TargetingModes.GroundOnly ? TargetingMode.Ground : mapViewState.TargetingMode;

        if (mode == TargetingMode.Ground)
        {
            if (cursorTileInReach is not { } groundTile)
            {
                return false;
            }

            aimedTile = groundTile;
            selection = targetingView.Select(playerEntityId, activator, mode, groundTile, now);
            return true;
        }

        if (cursorTileInReach is { } hoveredTile && IsTargetAt(activator, hoveredTile, now))
        {
            aimedTile = hoveredTile;
            selection = targetingView.Select(playerEntityId, activator, mode, hoveredTile, now);
            return true;
        }

        if (isSelfTargeting)
        {
            aimedTile = playerPosition;
            selection = targetingView.SelectEntity(playerEntityId, activator, playerEntityId);
            return true;
        }

        if (NearestTargetTile(activator, nearestTo: cursorTile ?? playerPosition, tiebreakTile: playerPosition, now) is not { } nearestTargetTile)
        {
            return false;
        }

        aimedTile = nearestTargetTile;
        selection = targetingView.Select(playerEntityId, activator, mode, nearestTargetTile, now);
        return true;
    }

    /// <summary>The tile in TargetableTiles holding a target nearest nearestTo, no further from it than MapViewState.TargetSearchDistanceTiles, ties going to the one nearer tiebreakTile; null when there is none.</summary>
    /// <remarks>Reads _candidateTilesBuffer, which RefreshTargetableTiles has just brought up to date.</remarks>
    private Vector3Int? NearestTargetTile(IActionActivator activator, Vector3Int nearestTo, Vector3Int tiebreakTile, long now)
    {
        _targetCandidateTilesBuffer.Clear();
        foreach (var tile in _candidateTilesBuffer)
        {
            if (GridDistance.ManhattanDistance(nearestTo, tile) <= mapViewState.TargetSearchDistanceTiles && IsTargetAt(activator, tile, now))
            {
                _targetCandidateTilesBuffer.Add(tile);
            }
        }

        return ClosestPointSelector.SelectClosest(nearestTo, tiebreakTile, _targetCandidateTilesBuffer);
    }

    /// <summary>Whether something other than the player stands on tile to be targeted: its blocking occupant, else an entity Target mode would mark there.</summary>
    private bool IsTargetAt(IActionActivator activator, Vector3Int tile, long now)
    {
        var occupantEntityId = world.GetEntityIdAt(tile);
        if (occupantEntityId != -1)
        {
            return occupantEntityId != world.PlayerEntityId;
        }

        if (world.GetOccupantEntityIdSpanAt(tile).IsEmpty)
        {
            return false;
        }

        return targetingView.Select(world.PlayerEntityId, activator, TargetingMode.Target, tile, now).HasMarkedEntity;
    }

    private bool IsArmedSelfTargeting()
    {
        if (mapViewState.ArmedActionId is { } actionId)
        {
            return actionStateView.TryGetEffectiveAction(world.PlayerEntityId, actionId, out var action) && action.Tags.Has(GameTags.TargetingSelf);
        }

        return mapViewState.ArmedItemStackInstanceId is { } stackInstanceId &&
            inventoryView.TryGetStack(world.PlayerEntityId, stackInstanceId, out var stack) &&
            InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) &&
            item.Tags.Has(GameTags.TargetingSelf);
    }

    /// <summary>Presentation only ever queues an activation request -- ActionActivationSystem is the only thing that applies gameplay effects. Mirrors PlayerCommands's own queue-and-let-a-system-consume pattern for movement. Closes every closable window here too (not just in ArmAction), so every path to an activation closes them.</summary>
    /// <param name="aimedTile">The tile confirmed -- for Dodge, the tile it steps to.</param>
    private void QueueActionActivation(int entityId, Guid actionId, Vector3Int aimedTile, TargetSelection selection)
    {
        Vector3Int? stepOnActivation = null;

        if (actionId == BuiltInActionIds.Dodge && transformView.TryGetTransform(entityId, out var casterTransform))
        {
            // DodgeActivation's own effect (DodgingComponent) always applies to the caster, not to
            // "whoever occupies the resolved target tile" -- for a directional dodge that tile is the
            // destination, where nobody stands yet when ActionEffectResolver.Apply looks for
            // occupants, so the effect would never run. Aiming the effect at the caster's own
            // current tile guarantees an occupant is found there -- see DodgeActivation's own doc
            // comment for why it also reads SourceEntityId rather than TargetEntityId, in case
            // something else shares that tile. Dodge is Ground only, so this is all its selection is.
            selection = TargetSelection.Ground(casterTransform.Position) with { Range = selection.Range };
            stepOnActivation = DodgeStep(aimedTile, casterTransform.Position);
        }

        uiLayers.CloseAllClosableWindows();

        playerCommands.QueueAction(actionId, selection, stepOnActivation);
    }

    /// <summary>The tile a Dodge aimed at <paramref name="aimedTile"/> steps to, or null for a Dodge in place.</summary>
    /// <remarks>
    /// Dodge's own targeting (SingleTarget + Metric.Chebyshev, Range 1) aims at exactly one tile -- self, or one
    /// adjacent tile. The step goes through PlayerCommands to MovementComponent.NextMapPosition, the same path
    /// ordinary movement uses, never World.MoveEntity: that only updates Map's occupancy index, not
    /// TransformComponent.Position, and would leave the two out of step. MovementSystem's own occupancy/wall/diagonal
    /// validation keeps the caster in place if the tile turns out occupied.
    /// </remarks>
    private static Vector3Int? DodgeStep(Vector3Int aimedTile, Vector3Int casterPosition) =>
        aimedTile != casterPosition ? aimedTile : null;

    /// <summary>Item counterpart to QueueActionActivation -- ItemActivationSystem is the only thing that applies its gameplay effects. See QueueActionActivation's own doc comment for why it also closes every closable window here.</summary>
    private void QueueItemActivation(uint stackInstanceId, TargetSelection selection, HotkeySlot? activatedFromSlot)
    {
        uiLayers.CloseAllClosableWindows();

        playerCommands.QueueItemActivation(stackInstanceId, selection, activatedFromSlot);
    }

    /// <summary>Dispatches a confirmed activation to whichever of {action, item} MapViewState currently has armed -- a click (TryConfirmActivationAtTile) or a hotkey re-press (TryActivateArmedFromHotkey).</summary>
    private void QueueArmedActivation(int entityId, Vector3Int aimedTile, TargetSelection selection)
    {
        if (mapViewState.ArmedActionId is { } actionId)
        {
            QueueActionActivation(entityId, actionId, aimedTile, selection);
        }
        else if (mapViewState.ArmedItemStackInstanceId is { } stackInstanceId)
        {
            QueueItemActivation(stackInstanceId, selection, mapViewState.ArmedSlot);
        }
    }
}
