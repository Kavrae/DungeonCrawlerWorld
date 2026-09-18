namespace Engine.ECS.Systems;

/// <summary>Which entities are currently simulated, for the parts of the engine that act on individual entities outside SystemManager's tier loop -- timer wheels today.</summary>
/// <remarks>
/// SystemManager.SimulatedTierCount stops a tiered system from visiting an unsimulated entity, but
/// a timer wheel fires by deadline, not by visit, so it needs to ask per entity. The game supplies
/// the answer (<see cref="SetPolicy"/>) and says when an entity becomes simulated again
/// (<see cref="RaiseResumed"/>); this layer never learns what makes an entity unsimulated.
///
/// A wheel is handed a scope only when its timers depend on the entity's surroundings. Standing in
/// a hazard or inside an aura is such a timer: the surroundings are not being simulated, so neither
/// is the exposure to them. A wheel driving an affliction the entity already carries -- poison
/// burning through its stacks, a modifier running out -- is given none, and keeps firing wherever
/// the entity is, because nothing an unsimulated entity can see changes the outcome, and leaving it
/// to run keeps the world true for anything that observes it without promoting it.
///
/// Without a policy every entity is simulated, which is what every test and every caller that
/// never opts in gets.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SimulationScope
{
    private Func<int, bool>? _isSimulated;

    /// <summary>Raised when an entity may have become simulated again, so anything that skipped it while it wasn't can pick it back up. May be raised for an entity that was already simulated; subscribers must treat that as a no-op.</summary>
    public event Action<int>? EntityResumed;

    /// <summary>Whether entityId is currently simulated. True for every entity until a policy is set.</summary>
    public bool IsSimulated(int entityId) => _isSimulated?.Invoke(entityId) ?? true;

    /// <summary>Supplies the game's answer to <see cref="IsSimulated"/>.</summary>
    public void SetPolicy(Func<int, bool> isSimulated)
    {
        ArgumentNullException.ThrowIfNull(isSimulated);

        _isSimulated = isSimulated;
    }

    /// <summary>Tells subscribers entityId may have become simulated again -- see <see cref="EntityResumed"/>.</summary>
    public void RaiseResumed(int entityId) => EntityResumed?.Invoke(entityId);
}
