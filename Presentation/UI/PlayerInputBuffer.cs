using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.World;

namespace Presentation.UI;

/// <summary>Holds the player's one pending command until the action lock clears, and is the only writer of the player's step and activation requests.</summary>
/// <remarks>
/// A single slot: queueing a move, action or consumable replaces whatever was there, so the command that resolves
/// when the lock clears is always the newest one. A buffered command expires ExpiryFrames after it was queued,
/// measured on the simulation clock so a pause does not age it. A command queued while the player is already free
/// is written the same frame.
///
/// Writing a command also withdraws whatever the player had asked for earlier and the game hasn't taken yet: an
/// action or consumable clears a step still in NextMapPosition, and a move removes a pending activation request.
/// An action that doesn't wait for the lock (FreeCast) skips the slot and is written at once, but still empties it.
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
public sealed class PlayerInputBuffer
{
    public static readonly ushort ExpiryFrames = GameTiming.FramesForSeconds(0.25f);

    private enum CommandKind : byte
    {
        None,
        Move,
        Action,
        Consumable,
    }

    private readonly World _world;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly PackedComponentPool<MovementComponent> _movementPool;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<PendingActionActivationComponent> _pendingActions;
    private readonly PackedComponentPool<PendingConsumableActivationComponent> _pendingConsumables;
    private readonly SimulationClock _simulationClock;

    private CommandKind _kind;
    private Vector3Int _moveDirection;
    private Guid _activationId;
    private Vector3Int[] _targetTiles = [];
    private Vector3Int? _stepOnActivation;
    private uint _expiresAtFrame;

    private (Guid ActionId, Vector3Int Destination)? _pendingActivationStep;

    public PlayerInputBuffer(
        World world,
        DirectComponentPool<TransformComponent> transformPool,
        PackedComponentPool<MovementComponent> movementPool,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<PendingActionActivationComponent> pendingActions,
        PackedComponentPool<PendingConsumableActivationComponent> pendingConsumables,
        SimulationClock simulationClock,
        EventBus eventBus)
    {
        _world = world;
        _transformPool = transformPool;
        _movementPool = movementPool;
        _actionLocks = actionLocks;
        _pendingActions = pendingActions;
        _pendingConsumables = pendingConsumables;
        _simulationClock = simulationClock;

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

    /// <summary>Buffers an activation of <paramref name="actionId"/> against <paramref name="targetTiles"/>, replacing any buffered command.</summary>
    /// <remarks>An action that doesn't wait for the action lock is written immediately instead of being buffered. <paramref name="stepOnActivation"/> is a tile to step to once the action activates.</remarks>
    public void QueueAction(Guid actionId, Vector3Int[] targetTiles, bool waitsForLock, Vector3Int? stepOnActivation = null)
    {
        if (!waitsForLock)
        {
            Clear();
            WriteAction(_world.PlayerEntityId, actionId, targetTiles, stepOnActivation);
            return;
        }

        Buffer(CommandKind.Action);
        _activationId = actionId;
        _targetTiles = targetTiles;
        _stepOnActivation = stepOnActivation;
        TryWriteBuffered();
    }

    /// <summary>Buffers an activation of the stack <paramref name="stackInstanceId"/> against <paramref name="targetTiles"/>, replacing any buffered command.</summary>
    public void QueueConsumable(Guid stackInstanceId, Vector3Int[] targetTiles)
    {
        Buffer(CommandKind.Consumable);
        _activationId = stackInstanceId;
        _targetTiles = targetTiles;
        TryWriteBuffered();
    }

    /// <summary>Drops the buffered command. Returns whether there was one.</summary>
    /// <remarks>A step waiting on an activation already written is not buffered input, so it is kept.</remarks>
    public bool Clear()
    {
        var hadCommand = _kind != CommandKind.None;
        _kind = CommandKind.None;
        _targetTiles = [];
        _stepOnActivation = null;
        return hadCommand;
    }

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
            _pendingConsumables.Has(playerEntityId) ||
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
        if (ActionLockGate.IsBlocked(_actionLocks, playerEntityId, now))
        {
            return false;
        }

        var kind = _kind;
        var targetTiles = _targetTiles;
        var stepOnActivation = _stepOnActivation;
        Clear();

        switch (kind)
        {
            case CommandKind.Move:
                WriteMove(playerEntityId, _moveDirection);
                break;
            case CommandKind.Action:
                WriteAction(playerEntityId, _activationId, targetTiles, stepOnActivation);
                break;
            case CommandKind.Consumable:
                WriteConsumable(playerEntityId, _activationId, targetTiles);
                break;
        }

        return true;
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

    private void WriteAction(int playerEntityId, Guid actionId, Vector3Int[] targetTiles, Vector3Int? stepOnActivation)
    {
        SetNextMapPosition(playerEntityId, null);
        WithdrawActivationRequests(playerEntityId);
        _pendingActions.Merge(playerEntityId, new PendingActionActivationComponent(actionId, targetTiles));
        _pendingActivationStep = stepOnActivation is { } destination ? (actionId, destination) : null;
    }

    private void WriteConsumable(int playerEntityId, Guid stackInstanceId, Vector3Int[] targetTiles)
    {
        SetNextMapPosition(playerEntityId, null);
        WithdrawActivationRequests(playerEntityId);
        _pendingConsumables.Merge(playerEntityId, new PendingConsumableActivationComponent(stackInstanceId, targetTiles));
    }

    private void WithdrawActivationRequests(int playerEntityId)
    {
        _pendingActions.Remove(playerEntityId);
        _pendingConsumables.Remove(playerEntityId);
        _pendingActivationStep = null;
    }

    private void SetNextMapPosition(int playerEntityId, Vector3Int? target) =>
        _movementPool.TryUpdate(playerEntityId, target, static (ref MovementComponent movement, Vector3Int? next) => movement.NextMapPosition = next);
}
