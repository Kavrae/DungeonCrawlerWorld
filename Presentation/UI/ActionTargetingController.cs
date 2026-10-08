using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Inventory;
using Game.Tags;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;

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
    ActionCatalog actionCatalog,
    ItemCatalog itemCatalog,
    TransformView transformView,
    HotkeyBindingView hotkeyBindingView,
    InventoryView inventoryView,
    ActionStateView actionStateView,
    TargetingView targetingView,
    PlayerCommands playerCommands,
    SimulationClock simulationClock)
{

    /// <summary>A second press of the same slot within this many frames of the first is a double-tap (auto-target the closest candidate, see HandleHotkeySlotPress), as opposed to a slower second press (confirm against the cursor, same as a click). Reads UiInputController's own shared click/double-click window rather than an independently tuned value, so mouse double-click and keyboard double-tap always agree.</summary>
    private static readonly int DoubleTapWindowFrames = UiInputController.DoubleClickWindowFrames;

    private int _frameCounter;

    private readonly Dictionary<HotkeySlot, int> _lastHotkeyPressFrameBySlot = [];

    // Reused across calls (see TargetShapeResolver's own doc comment on why Resolve writes into
    // a caller-owned buffer instead of allocating).
    private readonly List<Vector3Int> _candidateTilesBuffer = [];
    private readonly List<Vector3Int> _occupiedCandidateTilesBuffer = [];

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
    /// <remarks>
    /// Which windups count as Local is ActionStateView.CopyLocalPendingWindups'. Reads the catalog definition's Tags
    /// directly rather than resolving a per-instance Override: ActionOverrideEffects.OverrideFlatDamage (the only
    /// Override producer today) never touches Tags, so the catalog's own Tags are always correct here.
    /// </remarks>
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

    /// <summary>Advances the double-tap frame clock, and disarms whatever is armed once the player can no longer use it -- called once per MapWindow.Update, before anything else this class does that frame.</summary>
    /// <remarks>An armed action whose arms are destroyed mid-aim, or whose mana a hit drains, would otherwise stay armed with nothing a confirm could do.</remarks>
    public void Tick()
    {
        _frameCounter++;

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
        _hoveredFootprintSet.Clear();

        if (!TryGetArmedTargeting(out var targeting) || !transformView.TryGetTransform(world.PlayerEntityId, out var playerTransform))
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

        if (TryGetArmedActivator(out var activator))
        {
            var selection = targetingView.Select(world.PlayerEntityId, activator, mapViewState.TargetingMode, hoveredTile, simulationClock.CurrentFrame);
            targetingView.Resolve(world.PlayerEntityId, activator, selection, _hoveredFootprintBuffer);
        }

        foreach (var tile in _hoveredFootprintBuffer)
        {
            _hoveredFootprintSet.Add(tile);
        }
    }

    /// <summary>
    /// A left-click confirms the armed action or item's activation against whichever tile was
    /// clicked -- see TryConfirmActivationAtTile, the shared implementation this and a same-slot
    /// hotkey re-press (see HandleActionSlotPress/HandleItemSlotPress) both funnel into.
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

    /// <summary>
    /// Confirms the armed action or item's activation against targetTile, provided it's actually
    /// within TargetableTiles (a miss is a no-op -- whatever's armed stays armed, exactly like
    /// clicking empty space doesn't clear an inspector selection either). Queues the selection
    /// TargetingView builds for the player's targeting mode: in Target mode it marks whoever stands
    /// on targetTile, and Game resolves it into tiles -- now for Immediate and FreeCast, when the
    /// windup ends for Delayed. Reads which of {action, item} is armed from MapViewState itself
    /// rather than taking either id as a parameter -- mirrors CancelArmedOrPendingAction, which
    /// already does the same.
    /// </summary>
    private void TryConfirmActivationAtTile(Vector3Int targetTile)
    {
        if (!TryGetArmedActivator(out var activator))
        {
            return;
        }

        if (mapViewState.TargetableTiles is not { } targetableTiles || !targetableTiles.Contains(targetTile))
        {
            return;
        }

        if (!CanConfirmArmed())
        {
            return;
        }

        var selection = targetingView.Select(world.PlayerEntityId, activator, mapViewState.TargetingMode, targetTile, simulationClock.CurrentFrame);
        QueueArmedActivation(world.PlayerEntityId, targetTile, selection);
        Disarm();
    }

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
        if (mapViewState.ArmedActionId != DodgeAction.Id || !transformView.TryGetTransform(world.PlayerEntityId, out var transform))
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

        if (hotkeyBindingView.TryGetBoundAction(world.PlayerEntityId, slot, out var actionId))
        {
            HandleActionSlotPress(slot, actionId, isDoubleTap);
            return;
        }

        if (hotkeyBindingView.TryGetBoundItem(world.PlayerEntityId, slot, out var stackInstanceId))
        {
            HandleItemSlotPress(slot, stackInstanceId, isDoubleTap);
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
        hotkeyBindingView.TryGetUnlockedExpansionSlots(world.PlayerEntityId, out var unlockedSlots);
        return HotkeySlotLayout.IsLocked(slot, unlockedSlots);
    }

    /// <summary>
    /// Arms the pressed slot's action, or -- if it's already armed -- confirms it instead: a
    /// double-tap within DoubleTapWindowFrames skips arming entirely and immediately activates
    /// against an auto-picked target (see TryActivateWithAutoTarget); a slower re-press confirms
    /// against wherever the cursor currently is (see TryConfirmActivationAtTile), the same as a
    /// click would -- except a GameTags.TargetingSelf action (Heal, Dodge) always confirms on the caster's own
    /// tile via the same key instead, regardless of where the cursor happens to be hovering (the
    /// same "same key always means self" shortcut HandleItemSlotPress already gives a GameTags.TargetingSelf
    /// item's double-tap; a plain re-press of a Self-shaped action's own TargetableTiles would
    /// otherwise only ever contain its own tile, so a re-press only ever confirmed by coincidence
    /// of the cursor already hovering exactly there). Cancelling an armed slot is right-click/
    /// Escape's job now (see CancelArmedOrPendingAction) -- re-pressing the same key always means
    /// "go," not "nevermind."
    /// An action the player can't use right now (ActionStateView.GetActionBlocker -- not enough
    /// mana, melee disabled) is inert, the same no-op an unbound slot already is -- HotbarContent
    /// greys it out through the same query, so "can't be armed" and "looks unusable" stay in sync,
    /// mirroring HandleItemSlotPress's identical treatment of an unusable item slot. A double-tap
    /// that PlayerCommands wouldn't queue (not ready before the input buffer expires) fires nothing
    /// and leaves the slot armed from its first press.
    /// </summary>
    private void HandleActionSlotPress(HotkeySlot slot, Guid actionId, bool isDoubleTap)
    {
        if (actionStateView.GetActionBlocker(world.PlayerEntityId, actionId) != ActivationBlocker.None)
        {
            return;
        }

        // A toggle action is never armed: it applies to the player, so every press queues it at
        // once on the player's own tile, in the player's mode (Ground anchors what it places there), and no window closes.
        if (actionCatalog.TryGet(actionId, out var pressedAction) && pressedAction.Toggle is not null)
        {
            if (transformView.TryGetTransform(world.PlayerEntityId, out var playerTransform))
            {
                playerCommands.QueueAction(actionId, targetingView.Select(world.PlayerEntityId, pressedAction.Activator, mapViewState.TargetingMode, playerTransform.Position, simulationClock.CurrentFrame));
            }

            return;
        }

        if (isDoubleTap)
        {
            if (!playerCommands.CanQueueAction(actionId))
            {
                return;
            }

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
            if (actionCatalog.TryGet(actionId, out var armedAction) && armedAction.Tags.Has(GameTags.TargetingSelf) &&
                transformView.TryGetTransform(world.PlayerEntityId, out var selfTransform))
            {
                TryConfirmActivationAtTile(selfTransform.Position);
                return;
            }

            if (mapViewState.HoveredTile is { } hoveredTile)
            {
                TryConfirmActivationAtTile(hoveredTile);
            }

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
    /// stay in sync. Any item tagged GameTags.TargetingSelf (Health/Mana/Hotkey Expansion Potion today) has its
    /// double-tap always activate on the caster's own tile (TryActivateItemOnSelf), skipping
    /// arm/target entirely -- "double-tap always uses it on the user," regardless of what's
    /// currently armed. Keyed off the tag rather than any particular IActionActivator kind, so a
    /// future non-Potion self-only item (e.g. a bandage) gets the same shortcut just by carrying
    /// GameTags.TargetingSelf. A non-double-tap re-press of an already-armed slot confirms against the cursor
    /// instead (see TryConfirmActivationAtTile) -- same rhythm as HandleActionSlotPress,
    /// cancelling is right-click/Escape's job now. A toggle item is never armed: every press queues
    /// it at once (see QueueToggleItem).
    /// </summary>
    private void HandleItemSlotPress(HotkeySlot slot, uint stackInstanceId, bool isDoubleTap)
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

        if (isDoubleTap && item.Tags.Has(GameTags.TargetingSelf))
        {
            if (!playerCommands.CanQueueItemActivation(stackInstanceId))
            {
                return;
            }

            TryActivateItemOnSelf(world.PlayerEntityId, stackInstanceId, slot);

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

        if (actionCatalog.TryGet(actionId, out var action) && transformView.TryGetTransform(world.PlayerEntityId, out var transform))
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
    /// activation path. Deliberately skips HandleItemSlotPress's double-tap/self-cast shortcut: a
    /// menu click or double-click has no natural "double-tap" gesture of its own, and a plain single
    /// press of an unarmed hotbar slot always arms too (the self-cast shortcut is an addition on top
    /// of that base behavior, not a replacement for it) -- so arming here matches an ordinary,
    /// non-double-tap hotbar press exactly. That includes a toggle item, which a hotbar press queues at
    /// once rather than arming (see QueueToggleItem).
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
        if (mapViewState.ArmedActionId is { } actionId && actionCatalog.TryGet(actionId, out var action))
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
    /// time, only a reachable area. Shared by Arm (for highlighting) and TryActivateWithAutoTarget
    /// (for double-tap's candidate pool), so the two never drift out of sync with each other. Also
    /// what makes a manual click on the caster's own tile resolve at all for an Adjacent | Self item
    /// (e.g. Scroll of Healing) -- TargetableTiles has to actually contain that tile before
    /// TryConfirmActivationAtTile's GameTags.TargetingSelf special case (or the general resolve path) is ever
    /// reached.
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

    /// <summary>
    /// Resolves and queues a full action activation with no manual click-confirm at all -- the
    /// double-tap path. A GameTags.TargetingSelf action (Heal, Dodge) always confirms on the caster's own tile,
    /// full stop -- same rule HandleActionSlotPress's single-press re-confirm already gives them,
    /// applied here too. This matters for an action like Dodge whose Shape is SingleTarget (not
    /// cursor-independent): without this check it fell through to the occupied-tile hunt below,
    /// which QuickAttack/PowerAttack/ToxicStrike/MagicMissile actually want (double-tap = auto-
    /// attack the nearest enemy) but Dodge never does -- Dodge's own reachable block is normally
    /// all-empty (nothing to occupy an adjacent tile), so the hunt found no candidate, silently did
    /// nothing, and the caller's own "now that it fired, disarm" cleanup then made a double-tap of
    /// the Dodge key read as if it had just cancelled instead of activated (confirmed live). A
    /// cursor-independent shape's footprint never depends on a target choice either (Adjacent is
    /// always the caster's own tile's 8 surrounding neighbors; Self is always the caster's own
    /// footprint; either combination resolves the same way regardless of cursor), so it's queued
    /// immediately too. Every other shape needs a target tile chosen first: ComputeTargetableTiles'
    /// reachable-area candidates are filtered down to occupied tiles and handed to
    /// ClosestPointSelector.SelectClosest (cursor as the primary point, attacker as the tiebreaker),
    /// using MapViewState.HoveredTile as the cursor bias when one is already tracked (armed-and-then-
    /// double-tapped in one motion means Update hasn't run with the arm in effect yet, so
    /// HoveredTile can still be stale/null on the very first pair -- attackerPosition is the
    /// fallback for exactly that case, which is also what makes "closest to cursor" degenerate
    /// harmlessly into "closest to the caster" rather than picking an arbitrary target).
    /// </summary>
    private void TryActivateWithAutoTarget(int entityId, Guid actionId)
    {
        if (!actionCatalog.TryGet(actionId, out var action) || !transformView.TryGetTransform(entityId, out var transform))
        {
            return;
        }

        var attackerPosition = transform.Position;
        var attackerSize = transform.Size;
        var activator = action.Activator;
        var targeting = targetingView.EffectiveSpec(entityId, activator);
        var now = simulationClock.CurrentFrame;

        if (action.Tags.Has(GameTags.TargetingSelf) || IsCursorIndependent(targeting.Shape))
        {
            QueueActionActivation(entityId, actionId, attackerPosition, targetingView.Select(entityId, activator, mapViewState.TargetingMode, attackerPosition, now));
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
        if (ClosestPointSelector.SelectClosest(cursorTile, attackerPosition, _occupiedCandidateTilesBuffer) is not { } chosenTile)
        {
            return;
        }

        QueueActionActivation(entityId, actionId, chosenTile, targetingView.Select(entityId, activator, mapViewState.TargetingMode, chosenTile, now));
    }

    /// <summary>The double-tap path for a self-targeting item -- Target mode on the caster itself, whatever the player's mode, with no candidate search at all (contrast TryActivateWithAutoTarget's action equivalent): a potion double-tapped is drunk, not splashed.</summary>
    private void TryActivateItemOnSelf(int entityId, uint stackInstanceId, HotkeySlot? activatedFromSlot)
    {
        if (!inventoryView.TryGetStack(entityId, stackInstanceId, out var stack) ||
            !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) ||
            item.Activator is not { } activator)
        {
            return;
        }

        QueueItemActivation(stackInstanceId, targetingView.SelectEntity(entityId, activator, entityId), activatedFromSlot);
    }

    /// <summary>Presentation only ever queues an activation request -- ActionActivationSystem is the only thing that applies gameplay effects. Mirrors PlayerCommands's own queue-and-let-a-system-consume pattern for movement. Closes every closable window here too (not just in ArmAction) -- this is also reachable straight from a double-tap auto-target (TryActivateWithAutoTarget), which skips arming entirely, so it's the only chokepoint that catches that path.</summary>
    /// <param name="aimedTile">The tile confirmed -- for Dodge, the tile it steps to.</param>
    private void QueueActionActivation(int entityId, Guid actionId, Vector3Int aimedTile, TargetSelection selection)
    {
        Vector3Int? stepOnActivation = null;

        if (actionId == DodgeAction.Id && transformView.TryGetTransform(entityId, out var casterTransform))
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

    /// <summary>Item counterpart to QueueActionActivation -- ItemActivationSystem is the only thing that applies its gameplay effects. See QueueActionActivation's own doc comment for why it also closes every closable window here (catches TryActivateItemOnSelf's double-tap self-cast, which skips arming).</summary>
    private void QueueItemActivation(uint stackInstanceId, TargetSelection selection, HotkeySlot? activatedFromSlot)
    {
        uiLayers.CloseAllClosableWindows();

        playerCommands.QueueItemActivation(stackInstanceId, selection, activatedFromSlot);
    }

    /// <summary>Dispatches a confirmed click activation to whichever of {action, item} MapViewState currently has armed -- see TryConfirmActivation, the only caller.</summary>
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
