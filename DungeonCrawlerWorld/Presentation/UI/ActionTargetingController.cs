using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory;
using Game.Modules.ItemPiles;
using Game.Tags;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.MapRendering;
using Presentation.UI.Inventory;
using System.Runtime.InteropServices;
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
    SimulationClock simulationClock,
    IItemDropPlacement dropPlacement)
{

    /// <summary>What TargetingView resolves into, copied out at once into _targetableTiles or _hoveredFootprint; reused so a resolve allocates nothing.</summary>
    private readonly List<Vector3Int> _resolvedTilesBuffer = [];

    private readonly List<Vector3Int> _targetCandidateTilesBuffer = [];

    /// <summary>What TargetingView.ResolveReachable fills with the tiles the armed thing can reach, copied out at once into _targetableTiles.</summary>
    private readonly List<Vector3Int> _reachedTilesBuffer = [];

    /// <summary>Backs MapViewState.TargetableTiles, every tile the armed thing could hit: refilled in place by RefreshTargetableTiles, never replaced, so arming or moving allocates nothing.</summary>
    private readonly TileSet _targetableTiles = new();

    /// <summary>The tiles the cursor can aim the armed thing at, and NearestTargetTile picks from: TargetableTiles less what only a Projectile's path passes on its way. Refilled with it.</summary>
    private readonly TileSet _aimableTiles = new();

    /// <summary>What TargetableTiles was last resolved from; RefreshTargetableTiles, called every frame something is armed, resolves again only when it changes.</summary>
    private ReachableAreaKey? _targetableTilesKey;

    /// <summary>The armed action/item's actual hit-footprint at the current hover position, brought up to date every Update (see UpdateHoveredTile).</summary>
    private readonly TileSet _hoveredFootprint = new();

    /// <summary>What HoveredFootprint was last resolved from, when everything it depends on is in the key; null resolves it again next Update.</summary>
    private HoveredFootprintKey? _hoveredFootprintKey;

    /// <summary>The hovered footprint, in order -- for MapGameOverlay's second pass and for tests.</summary>
    internal TileSet HoveredFootprint => _hoveredFootprint;

    /// <summary>Constant time -- MapGameOverlay calls this once per targetable tile, every frame something is armed.</summary>
    internal bool HoveredFootprintContains(Vector3Int tile) => _hoveredFootprint.Contains(tile);

    /// <summary>Everything TargetingView.ResolveReachable's tiles depend on: its arguments, and the walls and map edge (TargetingView.SightVersion).</summary>
    private readonly record struct ReachableAreaKey(TargetingSpec Targeting, Vector3Int Position, Vector2Byte Size, int SightVersion);

    /// <summary>Everything a Placed Ground-mode resolve's tiles depend on: the targeting, the selection, the caster's footprint, and the walls and map edge (TargetingView.SightVersion).</summary>
    /// <remarks>A path meets whoever stands on it and a marked entity moves, so neither is ever keyed: they resolve every Update.</remarks>
    private readonly record struct HoveredFootprintKey(TargetingSpec Targeting, TargetSelection Selection, Vector3Int Position, Vector2Byte Size, int SightVersion);

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

        if (mapViewState.ArmedDrop is { } armedDrop)
        {
            return actionStateView.GetDropRefusal(world.PlayerEntityId, in armedDrop) != ItemDropRefusal.None;
        }

        return false;
    }

    /// <summary>Something is armed and a confirm of it would be refused right now (see CanConfirmArmed) -- what UiInputController shows the disabled cursor over the map for.</summary>
    /// <remarks>An armed drop is refused over a tile that wouldn't take it (IItemDropPlacement.CanDropAt), as a drag of it would be: a click there does nothing.</remarks>
    internal bool IsArmedConfirmRefused => mapViewState.ArmedDrop is { } drop
        ? IsArmedBlocked() || mapViewState.HoveredTile is not { } hoveredTile || !dropPlacement.CanDropAt(drop, hoveredTile)
        : mapViewState.IsAnythingArmed && !CanConfirmArmed();

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
    /// While a drop is armed, then tells ArmedDropHovered where the cursor is.
    /// </summary>
    public void UpdateHoveredTile(Point? mousePosition, Vector2 contentAbsolutePosition)
    {
        RefreshHoveredTile(mousePosition, contentAbsolutePosition);
        if (mapViewState.ArmedDrop is not null)
        {
            ArmedDropHovered?.Invoke(mapViewState.HoveredTile);
        }
    }

    private void RefreshHoveredTile(Point? mousePosition, Vector2 contentAbsolutePosition)
    {
        if (!TryGetArmedActivator(out var activator) || !transformView.TryGetTransform(world.PlayerEntityId, out var playerTransform))
        {
            ClearHoveredFootprint();
            mapViewState.HoveredTile = null;
            return;
        }

        RefreshTargetableTiles(targetingView.EffectiveSpec(world.PlayerEntityId, activator), playerTransform.Position, playerTransform.Size);

        if (mousePosition is not { } mouse || !camera.TryGetHoveredMapPosition(mouse, contentAbsolutePosition, out var hoveredColumnRow))
        {
            ClearHoveredFootprint();
            mapViewState.HoveredTile = null;
            return;
        }

        var hoveredTile = new Vector3Int(hoveredColumnRow.X, hoveredColumnRow.Y, playerTransform.Position.Z);
        mapViewState.HoveredTile = hoveredTile;

        if (TrySelectActivationTarget(activator, hoveredTile, isMapClick: true, fallsBackFromUntargetableTile: true, out _, out var selection))
        {
            RefreshHoveredFootprint(activator, selection, playerTransform.Position, playerTransform.Size);
            return;
        }

        ClearHoveredFootprint();
    }

    private void ClearHoveredFootprint()
    {
        _hoveredFootprint.Clear();
        _hoveredFootprintKey = null;
    }

    /// <summary>Brings HoveredFootprint up to date for selection, resolving again only when something a keyed resolve depends on changed (HoveredFootprintKey).</summary>
    private void RefreshHoveredFootprint(IActionActivator activator, TargetSelection selection, Vector3Int casterPosition, Vector2Byte casterSize)
    {
        HoveredFootprintKey? key = activator.Targeting.Path == TargetPath.Placed && !selection.HasMarkedEntity
            ? new HoveredFootprintKey(activator.Targeting, selection, casterPosition, casterSize, targetingView.SightVersion)
            : null;
        if (key is not null && _hoveredFootprintKey == key)
        {
            return;
        }

        _hoveredFootprintKey = key;
        _resolvedTilesBuffer.Clear();
        targetingView.Resolve(world.PlayerEntityId, activator, selection, _resolvedTilesBuffer);
        _hoveredFootprint.SetTo(CollectionsMarshal.AsSpan(_resolvedTilesBuffer));
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
    private void TryConfirmActivationAtTile(Vector3Int targetTile) => TryActivateArmed(targetTile, isMapClick: true, fallsBackFromUntargetableTile: true);

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
        if (mapViewState.IsAnythingArmed)
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
            TryActivateArmed(transform.Position + direction, isMapClick: true, fallsBackFromUntargetableTile: false);
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

        if (mapViewState.ArmedDrop is not null && mapViewState.ArmedSlot == slot)
        {
            TryActivateArmedFromHotkey();
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
        SetArmedDrop(null);
        mapViewState.ArmedSlot = slot;
        _targetableTilesKey = null;

        if (TryGetArmedTargeting(out var targeting) && transformView.TryGetTransform(world.PlayerEntityId, out var transform))
        {
            RefreshTargetableTiles(targeting, transform.Position, transform.Size);
        }
    }

    /// <summary>Resolves targeting via TryGetArmedTargeting (not a parameter of its own) -- called after ArmedItemStackInstanceId is already set above, so it reads back the correctly (Scroll-)scaled spec instead of a stale unscaled one, the same single-chokepoint reasoning as ArmAction re-fetching Activator.Targeting itself rather than taking it as a parameter. Also, critically, what makes a diverged stack's own Override targeting (e.g. a wand with non-default Targeting) actually apply -- TryGetArmedTargeting resolves through the bound stack itself, not a bare catalog lookup by item id. slot is null for a menu-driven arm with no originating HotkeySlot (see ArmItemFromStack) -- every ArmedSlot consumer already treats null as "no slot to highlight/reuse for a same-slot re-press confirm," which is exactly correct there: a menu-armed item can only ever be confirmed via a map-tile click.</summary>
    private void ArmItem(HotkeySlot? slot, uint stackInstanceId)
    {
        uiLayers.CloseAllClosableWindows(); // See ArmAction's own doc comment -- same reasoning, covers ArmItemFromStack (Inventory's Activate/double-click) too, since it delegates here.

        mapViewState.ArmedItemStackInstanceId = stackInstanceId;
        mapViewState.ArmedActionId = null;
        SetArmedDrop(null);
        mapViewState.ArmedSlot = slot;
        _targetableTilesKey = null;

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

    /// <summary>Arms a drop of drop, awaiting the tile it lands on: the player's own or one beside it (ItemDrops.Targeting), confirmed by a map click or, from a hotbar slot, a re-press of it.</summary>
    /// <remarks>A drop that would be refused now (ActionStateView.GetDropRefusal) arms nothing. Closes every closable window, as arming anything does.</remarks>
    /// <param name="slot">The hotbar slot the drop came from, if any.</param>
    public void ArmDrop(ItemDrop drop, HotkeySlot? slot = null)
    {
        if (!CanArmDrop(drop))
        {
            return;
        }

        uiLayers.CloseAllClosableWindows();

        SetArmedDrop(drop);
        mapViewState.ArmedActionId = null;
        mapViewState.ArmedItemStackInstanceId = null;
        mapViewState.ArmedSlot = slot;
        _targetableTilesKey = null;

        if (transformView.TryGetTransform(world.PlayerEntityId, out var transform))
        {
            RefreshTargetableTiles(ItemDrops.Targeting, transform.Position, transform.Size);
        }
    }

    /// <summary>Whether drop can be armed now: Game wouldn't refuse it (ActionStateView.GetDropRefusal), and no shop is open, since a trade decides where items go until it closes.</summary>
    public bool CanArmDrop(ItemDrop drop) =>
        mapViewState.OpenShopEntityId is null &&
        actionStateView.GetDropRefusal(world.PlayerEntityId, in drop) == ItemDropRefusal.None;

    /// <summary>Raised every update while a drop is armed, with the tile the cursor is over on the map, or null off it.</summary>
    /// <remarks>For what the hovered tile does to the drop -- a container it would go into (ArmedDropHover).</remarks>
    public event Action<Vector3Int?>? ArmedDropHovered;

    /// <summary>Raised whenever MapViewState.ArmedDrop changes: a drop armed, replaced or disarmed by any path.</summary>
    public event Action? ArmedDropChanged;

    /// <summary>Disarms an armed drop; does nothing while anything else, or nothing, is armed.</summary>
    /// <remarks>For a drag that armed one (DragDropArming), when it ends.</remarks>
    public void DisarmDrop()
    {
        if (mapViewState.ArmedDrop is not null)
        {
            Disarm();
        }
    }

    private void SetArmedDrop(ItemDrop? drop)
    {
        if (mapViewState.ArmedDrop == drop)
        {
            return;
        }

        mapViewState.ArmedDrop = drop;
        ArmedDropChanged?.Invoke();
    }

    /// <summary>The drop a hotbar slot's "Drop" arms: one unit of the stack bound to it -- the unit an activation from that slot would use. False for a slot with no item.</summary>
    public bool TryGetSlotDrop(HotkeySlot slot, out ItemDrop drop)
    {
        if (hotkeyBindingView.TryGetBoundItem(world.PlayerEntityId, slot, out var stackInstanceId))
        {
            drop = ItemDrop.OneUnitOf(stackInstanceId);
            return true;
        }

        drop = default;
        return false;
    }

    private void Disarm()
    {
        mapViewState.ArmedActionId = null;
        mapViewState.ArmedItemStackInstanceId = null;
        SetArmedDrop(null);
        mapViewState.ArmedSlot = null;
        mapViewState.TargetableTiles = null;
        _targetableTilesKey = null;
        _targetableTiles.Clear();
        _aimableTiles.Clear();
    }

    /// <summary>
    /// The activator of whichever of {action, item, drop} is currently armed -- the one piece every kind
    /// needs for every targeting computation below, so callers stop caring which kind they're
    /// dealing with past this point. Resolves the armed item through its bound *stack*
    /// (InventoryQueries.TryResolveEffectiveItem), not a bare catalog lookup by item id -- a
    /// diverged stack's own Override (e.g. a wand carrying non-default Targeting) is what actually
    /// gets read, not always the catalog original. A drop's is DropTargeting.
    /// </summary>
    private bool TryGetArmedActivator(out IActionActivator activator)
    {
        if (mapViewState.ArmedDrop is not null)
        {
            activator = DropTargeting.Instance;
            return true;
        }

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
    /// Brings TargetableTiles and the aimable tiles up to date from one resolve (TargetingView.ResolveReachable), whatever
    /// the cursor: the highlight is every tile targeting could hit from the caster, and the aimable tiles -- what the cursor
    /// and NearestTargetTile pick from -- are those it can be aimed at. They differ only by what a Projectile's path passes
    /// on its way.
    /// </summary>
    /// <remarks>
    /// Called on arming and every UpdateHoveredTile after, so the area follows a caster that moves while armed. It resolves
    /// again only when something the tiles depend on changed (ReachableAreaKey): the caster's position or size, the
    /// effective targeting (a range modifier or scroll scaling that starts or ends mid-arm), or the walls and map edge.
    /// </remarks>
    private void RefreshTargetableTiles(TargetingSpec targeting, Vector3Int currentPosition, Vector2Byte currentSize)
    {
        var key = new ReachableAreaKey(targeting, currentPosition, currentSize, targetingView.SightVersion);
        if (_targetableTilesKey == key)
        {
            return;
        }

        _targetableTilesKey = key;
        targetingView.ResolveReachable(targeting, currentPosition, currentSize, _resolvedTilesBuffer, _reachedTilesBuffer);
        _aimableTiles.SetTo(CollectionsMarshal.AsSpan(_resolvedTilesBuffer));
        _targetableTiles.SetTo(CollectionsMarshal.AsSpan(_reachedTilesBuffer));
        mapViewState.TargetableTiles = _targetableTiles;
    }

    /// <summary>Activates whatever is armed without a map click -- a re-press of its hotbar slot, by key or by clicking the slot -- against the hovered tile, if the cursor is on the map.</summary>
    private void TryActivateArmedFromHotkey() => TryActivateArmed(mapViewState.HoveredTile, isMapClick: false, fallsBackFromUntargetableTile: true);

    /// <summary>Queues and disarms whatever is armed, aimed by TrySelectActivationTarget; nothing to land on, or a confirm that would be refused, leaves it armed with nothing paid.</summary>
    /// <remarks>An armed drop is put down on cursorTile itself (IItemDropPlacement.TryDropAt), by the same rule a drag onto that tile follows -- into a container there, or onto the ground -- and stays armed if it can't go there.</remarks>
    private void TryActivateArmed(Vector3Int? cursorTile, bool isMapClick, bool fallsBackFromUntargetableTile)
    {
        if (mapViewState.ArmedDrop is { } drop)
        {
            if (!IsArmedBlocked() && cursorTile is { } dropTile && dropPlacement.TryDropAt(drop, dropTile))
            {
                Disarm();
            }

            return;
        }

        if (!TryGetArmedActivator(out var activator) || !CanConfirmArmed())
        {
            return;
        }

        if (!TrySelectActivationTarget(activator, cursorTile, isMapClick, fallsBackFromUntargetableTile, out var aimedTile, out var selection))
        {
            return;
        }

        QueueArmedActivation(world.PlayerEntityId, aimedTile, selection);
        Disarm();
    }

    /// <summary>Where an activation of the armed activator lands, with the cursor on cursorTile (null when it isn't on the map) -- one rule for every way to activate, and for the hover preview.</summary>
    /// <remarks>
    /// In order: a shape that is the same wherever it is aimed (TargetShapeResolver.IsAimIndependent) lands around the caster, whatever the mode. A GameTags.TargetingSelf
    /// action or item used from the hotbar lands on the player (a potion is drunk, Dodge dodges in
    /// place); a map click follows the mode instead. Ground mode (the player's, or an activator that
    /// offers no other) lands on cursorTile when it is in reach -- a potion splashes there -- or, with
    /// fallsBackFromUntargetableTile, when cursorTile is one nothing may target (TargetingView.IsTargetableTile: a wall),
    /// on the tile in reach nearest it within MapViewState.TargetSearchDistanceTiles, ties going to the one nearer the
    /// player. Target mode marks the target on cursorTile when it is in reach; else, for a TargetingSelf map click, the
    /// player; else the target in reach nearest cursorTile, or nearest the player with the cursor off
    /// the map, within MapViewState.TargetSearchDistanceTiles of it. False when there is nothing to land on.
    /// </remarks>
    /// <param name="fallsBackFromUntargetableTile">False for a tile that is a direction rather than the cursor (Dodge's WASD step), which lands there or nowhere.</param>
    /// <param name="aimedTile">The tile aimed at -- for Dodge, the tile it steps to.</param>
    private bool TrySelectActivationTarget(IActionActivator activator, Vector3Int? cursorTile, bool isMapClick, bool fallsBackFromUntargetableTile, out Vector3Int aimedTile, out TargetSelection selection)
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

        if (TargetShapeResolver.IsAimIndependent(targeting.Shape, targeting.Anchor))
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
        var cursorTileInReach = cursorTile is { } tile && _aimableTiles.Contains(tile) ? cursorTile : null;
        var mode = targeting.Modes == TargetingModes.GroundOnly ? TargetingMode.Ground : mapViewState.TargetingMode;

        if (mode == TargetingMode.Ground)
        {
            var groundTileInReach = cursorTileInReach;
            if (groundTileInReach is null && fallsBackFromUntargetableTile && cursorTile is { } untargetableTile && !targetingView.IsTargetableTile(untargetableTile))
            {
                groundTileInReach = NearestAimableTile(nearestTo: untargetableTile, tiebreakTile: playerPosition);
            }

            if (groundTileInReach is not { } groundTile)
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

    /// <summary>The tile the armed thing can be aimed at holding a target nearest nearestTo, no further from it than MapViewState.TargetSearchDistanceTiles, ties going to the one nearer tiebreakTile; null when there is none.</summary>
    /// <remarks>Reads _aimableTiles, which RefreshTargetableTiles has just brought up to date.</remarks>
    private Vector3Int? NearestTargetTile(IActionActivator activator, Vector3Int nearestTo, Vector3Int tiebreakTile, long now)
    {
        _targetCandidateTilesBuffer.Clear();
        foreach (var tile in _aimableTiles)
        {
            if (GridDistance.IsWithinRoundedRadius(nearestTo, tile, mapViewState.TargetSearchDistanceTiles) && IsTargetAt(activator, tile, now))
            {
                _targetCandidateTilesBuffer.Add(tile);
            }
        }

        return ClosestPointSelector.SelectClosest(nearestTo, tiebreakTile, CollectionsMarshal.AsSpan(_targetCandidateTilesBuffer));
    }

    /// <summary>The tile the armed thing can be aimed at nearest nearestTo, no further from it than MapViewState.TargetSearchDistanceTiles, ties going to the one nearer tiebreakTile; null when there is none.</summary>
    /// <remarks>Reads _aimableTiles, which RefreshTargetableTiles has just brought up to date.</remarks>
    private Vector3Int? NearestAimableTile(Vector3Int nearestTo, Vector3Int tiebreakTile)
    {
        _targetCandidateTilesBuffer.Clear();
        foreach (var tile in _aimableTiles)
        {
            if (GridDistance.IsWithinRoundedRadius(nearestTo, tile, mapViewState.TargetSearchDistanceTiles))
            {
                _targetCandidateTilesBuffer.Add(tile);
            }
        }

        return ClosestPointSelector.SelectClosest(nearestTo, tiebreakTile, CollectionsMarshal.AsSpan(_targetCandidateTilesBuffer));
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
            selection = TargetSelection.Ground(casterTransform.Position) with { MaximumRange = selection.MaximumRange };
            stepOnActivation = DodgeStep(aimedTile, casterTransform.Position);
        }

        uiLayers.CloseAllClosableWindows();

        playerCommands.QueueAction(actionId, selection, stepOnActivation);
    }

    /// <summary>The tile a Dodge aimed at <paramref name="aimedTile"/> steps to, or null for a Dodge in place.</summary>
    /// <remarks>
    /// Dodge's own targeting (SingleTarget, Range 1, the 3x3 block) aims at exactly one tile -- self, or one
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

    /// <summary>A drop's targeting as an activator, so an armed drop is previewed by the same rules as an action or item.</summary>
    /// <remarks>It lives here, not in Game: it is only a way to aim, and Game's activators are content, each with its codec.</remarks>
    private sealed class DropTargeting : IActionActivator
    {
        public static DropTargeting Instance { get; } = new();

        public TargetingSpec Targeting => ItemDrops.Targeting;

        public ActionTiming Timing { get; } = new(ActionTimingCategory.Immediate);
    }
}
