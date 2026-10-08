using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>Holds the player's one pending command until it can be carried out, and is the only writer of the player's step and activation requests.</summary>
/// <remarks>
/// A single slot: queueing a move, action or item activation replaces whatever was there, so the command that resolves
/// is always the newest one. A move waits for the action lock to clear, and so does an item activation unless its item is
/// FreeCast (a toggle item can be); an action waits until it is
/// ready (ActivationQueries.FramesUntilReady: its cooldown and, unless FreeCast, the lock). A buffered command
/// expires ExpiryFrames after it was queued, measured on the simulation clock so a pause does not age it. A command
/// queued while it is already ready is written the same frame.
///
/// An action or item activation that couldn't be ready before it expired is refused rather than buffered: nothing is
/// queued, whatever was buffered stays, and the caller is told, so a confirm is never silently dropped.
///
/// Writing a command also withdraws whatever the player had asked for earlier and the game hasn't taken yet: an
/// action or item activation clears a step still in NextMapPosition, and a move removes a pending activation request.
///
/// A move is buffered as a direction, not a tile, and resolved against the player's position when it is written. A
/// move whose tile can't be occupied clears NextMapPosition rather than leaving an older step in place.
///
/// An action can carry a step to take once it activates (Dodge's destination). The step is written to
/// NextMapPosition only when the player's ActionActivatedEvent for that action arrives, so an activation that fails
/// (on cooldown, say) never moves the player. A request the game consumed without that event drops its step.
///
/// A Stagger (EntityStaggeredEvent on the player) drops the buffered command. A Dodge's step waiting on its activation
/// is kept: a Staggering hit can't land on a dodging player, and the step is taken at once.
///
/// With nothing buffered, the held direction is written instead, but only while the player is at rest and has no
/// activation request pending -- holding a key is not a new command, so it never replaces one.
/// </remarks>
public sealed class PlayerCommands
{
    public static readonly ushort ExpiryFrames = GameTiming.FramesForSeconds(0.25f);

    private enum CommandKind : byte
    {
        None,
        Move,
        Action,
        Item,
    }

    private readonly World.World _world;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly PackedComponentPool<MovementComponent> _movementPool;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<PendingActionActivationComponent> _pendingActions;
    private readonly PackedComponentPool<PendingItemActivationComponent> _pendingItemActivations;
    private readonly PackedComponentPool<PendingWindupComponent> _pendingWindups;
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks;
    private readonly ItemCatalog _itemCatalog;
    private readonly SimulationClock _simulationClock;
    private readonly EntityActions _entityActions;

    private CommandKind _kind;
    private Vector3Int _moveDirection;
    private Guid _actionId;
    private uint _stackInstanceId;
    private HotkeySlot? _activatedFromSlot;
    private TargetSelection _selection;
    private Vector3Int? _stepOnActivation;
    private uint _expiresAtFrame;

    private (Guid ActionId, Vector3Int Destination)? _pendingActivationStep;

    public PlayerCommands(
        World.World world,
        DirectComponentPool<TransformComponent> transformPool,
        PackedComponentPool<MovementComponent> movementPool,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<PendingActionActivationComponent> pendingActions,
        PackedComponentPool<PendingItemActivationComponent> pendingItemActivations,
        PackedComponentPool<PendingWindupComponent> pendingWindups,
        MultiComponentPool<InventoryItemStackComponent> inventoryStacks,
        ItemCatalog itemCatalog,
        SimulationClock simulationClock,
        EntityActions entityActions,
        EventBus eventBus)
    {
        _world = world;
        _transformPool = transformPool;
        _movementPool = movementPool;
        _actionLocks = actionLocks;
        _pendingActions = pendingActions;
        _pendingItemActivations = pendingItemActivations;
        _pendingWindups = pendingWindups;
        _inventoryStacks = inventoryStacks;
        _itemCatalog = itemCatalog;
        _simulationClock = simulationClock;
        _entityActions = entityActions;

        eventBus.Subscribe<ActionActivatedEvent>(OnActionActivated);
        eventBus.Subscribe<EntityStaggeredEvent>(OnEntityStaggered);
    }

    /// <summary>Buffers a one-tile move in <paramref name="direction"/>, replacing any buffered command.</summary>
    /// <remarks>A zero direction is a request to stand still: it clears any step already written once it is written itself.</remarks>
    public void QueueMove(Vector3Int direction)
    {
        Buffer(CommandKind.Move);
        _moveDirection = direction;
        TryWriteBuffered();
    }

    /// <summary>Buffers an activation of <paramref name="actionId"/> against <paramref name="selection"/>, replacing any buffered command. Returns false, buffering nothing, when <see cref="CanQueueAction"/> is false.</summary>
    /// <remarks>An action that is ready now is written immediately. <paramref name="stepOnActivation"/> is a tile to step to once the action activates.</remarks>
    public bool QueueAction(Guid actionId, TargetSelection selection, Vector3Int? stepOnActivation = null)
    {
        if (!CanQueueAction(actionId))
        {
            return false;
        }

        Buffer(CommandKind.Action);
        _actionId = actionId;
        _selection = selection;
        _stepOnActivation = stepOnActivation;
        TryWriteBuffered();
        return true;
    }

    /// <summary>Whether the player has <paramref name="actionId"/> and it will be ready (cooldown and, unless FreeCast, the action lock) before a command buffered now would expire.</summary>
    public bool CanQueueAction(Guid actionId) =>
        _entityActions.TryGetEffectiveAction(_world.PlayerEntityId, actionId, out var action) &&
        ActivationQueries.FramesUntilReady(_world.PlayerEntityId, action, _entityActions, _actionLocks, _simulationClock.CurrentFrame) < ExpiryFrames;

    /// <summary>Buffers an activation of the stack <paramref name="stackInstanceId"/> against <paramref name="selection"/>, replacing any buffered command. Returns false, buffering nothing, when <see cref="CanQueueItemActivation"/> is false.</summary>
    /// <param name="activatedFromSlot">The hotkey slot the activation came from, if any: the slot that follows the unit when activating it moves it to another stack.</param>
    public bool QueueItemActivation(uint stackInstanceId, TargetSelection selection, HotkeySlot? activatedFromSlot = null)
    {
        if (!CanQueueItemActivation(stackInstanceId))
        {
            return false;
        }

        Buffer(CommandKind.Item);
        _stackInstanceId = stackInstanceId;
        _activatedFromSlot = activatedFromSlot;
        _selection = selection;
        TryWriteBuffered();
        return true;
    }

    /// <summary>Whether an activation of the stack <paramref name="stackInstanceId"/> buffered now could be written before it expires: its item is FreeCast, or the player's action lock clears in time.</summary>
    public bool CanQueueItemActivation(uint stackInstanceId) =>
        IgnoresActionLock(stackInstanceId) ||
        (_actionLocks.TryGetReadonly(_world.PlayerEntityId, out var actionLock) &&
            ActionLockGate.FramesRemaining(actionLock, _simulationClock.CurrentFrame) < ExpiryFrames);

    /// <summary>Whether the item in the player's stack <paramref name="stackInstanceId"/> is activated FreeCast, so it never waits for the action lock.</summary>
    private bool IgnoresActionLock(uint stackInstanceId) =>
        InventoryQueries.TryFindByStackInstanceId(_inventoryStacks, _world.PlayerEntityId, stackInstanceId, out var stack) &&
        InventoryQueries.TryResolveEffectiveItem(_itemCatalog, in stack, out var item) &&
        item.Activator?.Timing.Category == ActionTimingCategory.FreeCast;

    /// <summary>Drops the buffered command. Returns whether there was one.</summary>
    /// <remarks>A step waiting on an activation already written is not buffered input, so it is kept.</remarks>
    public bool Clear()
    {
        var hadCommand = _kind != CommandKind.None;
        _kind = CommandKind.None;
        _selection = default;
        _stepOnActivation = null;
        return hadCommand;
    }

    /// <summary>Cancels the player's windup and releases the action lock it held. Returns whether the player was winding up.</summary>
    public bool TryCancelWindup(long now) =>
        WindupCancel.TryCancel(_pendingWindups, _actionLocks, _world.PlayerEntityId, now, releaseLock: true);

    /// <summary>Writes the buffered command, or else the held direction, into the game once the player's action lock has cleared.</summary>
    public void Flush(Vector3Int heldDirection)
    {
        var playerEntityId = _world.PlayerEntityId;
        if (_pendingActivationStep is not null && !_pendingActions.Has(playerEntityId))
        {
            _pendingActivationStep = null;
        }

        if (TryWriteBuffered() || heldDirection == new Vector3Int())
        {
            return;
        }

        if (ActionLockGate.IsBlocked(_actionLocks, playerEntityId, _simulationClock.CurrentFrame) ||
            _pendingActions.Has(playerEntityId) ||
            _pendingItemActivations.Has(playerEntityId) ||
            !_transformPool.TryGetReadonly(playerEntityId, out var transform) ||
            !_movementPool.TryGetReadonly(playerEntityId, out var movement))
        {
            return;
        }

        var isAtRest = movement.NextMapPosition is null || movement.NextMapPosition.Value == transform.Position;
        if (isAtRest)
        {
            WriteMove(playerEntityId, heldDirection);
        }
    }

    private void OnActionActivated(ActionActivatedEvent activated)
    {
        if (activated.EntityId != _world.PlayerEntityId ||
            _pendingActivationStep is not { } step ||
            step.ActionId != activated.ActionId)
        {
            return;
        }

        _pendingActivationStep = null;
        SetNextMapPosition(activated.EntityId, step.Destination);
    }

    private void OnEntityStaggered(EntityStaggeredEvent staggered)
    {
        if (staggered.EntityId == _world.PlayerEntityId)
        {
            Clear();
        }
    }

    private void Buffer(CommandKind kind)
    {
        Clear();
        _kind = kind;
        _expiresAtFrame = FrameDeadline.After(_simulationClock.CurrentFrame, ExpiryFrames);
    }

    private bool TryWriteBuffered()
    {
        if (_kind == CommandKind.None)
        {
            return false;
        }

        var now = _simulationClock.CurrentFrame;
        if (FrameDeadline.IsReached(_expiresAtFrame, now))
        {
            Clear();
            return false;
        }

        var playerEntityId = _world.PlayerEntityId;
        if (!IsBufferedCommandReady(playerEntityId, now))
        {
            return false;
        }

        var kind = _kind;
        var selection = _selection;
        var stepOnActivation = _stepOnActivation;
        Clear();

        switch (kind)
        {
            case CommandKind.Move:
                WriteMove(playerEntityId, _moveDirection);
                break;
            case CommandKind.Action:
                WriteAction(playerEntityId, _actionId, selection, stepOnActivation);
                break;
            case CommandKind.Item:
                WriteItemActivation(playerEntityId, _stackInstanceId, selection);
                break;
        }

        return true;
    }

    private bool IsBufferedCommandReady(int playerEntityId, long now)
    {
        if (_kind != CommandKind.Action)
        {
            return (_kind == CommandKind.Item && IgnoresActionLock(_stackInstanceId)) ||
                !ActionLockGate.IsBlocked(_actionLocks, playerEntityId, now);
        }

        return !_entityActions.TryGetEffectiveAction(playerEntityId, _actionId, out var action) ||
            ActivationQueries.FramesUntilReady(playerEntityId, action, _entityActions, _actionLocks, now) == 0;
    }

    private void WriteMove(int playerEntityId, Vector3Int direction)
    {
        if (!_transformPool.TryGetReadonly(playerEntityId, out var transform))
        {
            return;
        }

        WithdrawActivationRequests(playerEntityId);

        Vector3Int? target = transform.Position + direction;
        if (direction == new Vector3Int() ||
            !MovementCandidates.CanOccupy(_world, target.Value, transform.Size, playerEntityId, _world.IsBlocking(playerEntityId)))
        {
            target = null;
        }

        SetNextMapPosition(playerEntityId, target);
    }

    private void WriteAction(int playerEntityId, Guid actionId, TargetSelection selection, Vector3Int? stepOnActivation)
    {
        SetNextMapPosition(playerEntityId, null);
        WithdrawActivationRequests(playerEntityId);
        _pendingActions.Merge(playerEntityId, new PendingActionActivationComponent(actionId, selection));
        _pendingActivationStep = stepOnActivation is { } destination ? (actionId, destination) : null;
    }

    private void WriteItemActivation(int playerEntityId, uint stackInstanceId, TargetSelection selection)
    {
        SetNextMapPosition(playerEntityId, null);
        WithdrawActivationRequests(playerEntityId);
        _pendingItemActivations.Merge(playerEntityId, new PendingItemActivationComponent(stackInstanceId, selection, _activatedFromSlot));
    }

    private void WithdrawActivationRequests(int playerEntityId)
    {
        _pendingActions.Remove(playerEntityId);
        _pendingItemActivations.Remove(playerEntityId);
        _pendingActivationStep = null;
    }

    private void SetNextMapPosition(int playerEntityId, Vector3Int? target) =>
        _movementPool.TryUpdate(playerEntityId, target, static (ref MovementComponent movement, Vector3Int? next) => movement.NextMapPosition = next);
}
