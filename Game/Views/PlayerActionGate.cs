using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.World;

namespace Game.Views;

/// <summary>The player's global action lock, for UI that starts or checks it without an action of its own (the map's "Inspect" option).</summary>
/// <remarks>Reads "now" from the simulation clock, since the lock is a deadline -- see ActionLockGate.</remarks>
public sealed class PlayerActionGate(PackedComponentPool<ActionLockComponent> actionLocks, IPlayerQuery playerQuery, SimulationClock simulationClock)
{
    /// <summary>Whether the player is currently locked out of acting.</summary>
    public bool IsLocked => ActionLockGate.IsBlocked(actionLocks, playerQuery.PlayerEntityId, simulationClock.CurrentFrame);

    /// <summary>Starts the player's standard action lock from the current frame.</summary>
    public void Lock() => ActionLockGate.Lock(actionLocks, playerQuery.PlayerEntityId, simulationClock.CurrentFrame);
}
