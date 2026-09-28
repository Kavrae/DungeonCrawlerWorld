namespace Engine.ECS.Systems;

/// <summary>Which entities are currently simulated, for the parts of the engine that act on individual entities outside SystemManager's tier loop -- timer wheels today.</summary>
/// <remarks>
/// SystemManager.SimulatedTierCount stops a tiered system from visiting an unsimulated entity, but
/// a timer wheel fires by deadline, not by visit, so it needs to ask per entity. The game supplies
/// the answer (<see cref="SetPolicy"/>) and says when an entity becomes simulated again
/// (<see cref="RaiseResumed"/>); this layer never learns what makes an entity unsimulated.
///
/// A wheel is scoped by the session's scope only when its timers depend on the entity's surroundings.
/// Standing in a hazard or inside an aura is such a timer: the surroundings are not being simulated,
/// so neither is the exposure to them. A wheel driving an affliction the entity already carries --
/// poison burning through its stacks, a modifier running out -- is given <see cref="Unscoped"/>, and
/// keeps firing wherever the entity is, because nothing an unsimulated entity can see changes the
/// outcome, and leaving it to run keeps the world true for anything that observes it without
/// promoting it.
///
/// Until a policy is set every entity is simulated.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SimulationScope
{
    private static readonly Func<int, bool> EveryEntity = static _ => true;

    /// <summary>The scope that simulates every entity and never changes: for a wheel whose timers fire wherever the entity is.</summary>
    /// <remarks>Shared, so it takes no policy and keeps no subscribers.</remarks>
    public static SimulationScope Unscoped { get; } = new(isUnscoped: true);

    private readonly bool _isUnscoped;
    private Func<int, bool> _isSimulated = EveryEntity;
    private Action<int>? _entityResumed;

    public SimulationScope()
    {
    }

    private SimulationScope(bool isUnscoped) => _isUnscoped = isUnscoped;

    /// <summary>Raised when an entity may have become simulated again, so anything that skipped it while it wasn't can pick it back up. May be raised for an entity that was already simulated; subscribers must treat that as a no-op.</summary>
    /// <remarks>Never raised by <see cref="Unscoped"/>, which ignores subscriptions.</remarks>
    public event Action<int>? EntityResumed
    {
        add
        {
            if (!_isUnscoped)
            {
                _entityResumed += value;
            }
        }
        remove => _entityResumed -= value;
    }

    /// <summary>Whether entityId is currently simulated. True for every entity until a policy is set.</summary>
    public bool IsSimulated(int entityId) => _isSimulated(entityId);

    /// <summary>Supplies the game's answer to <see cref="IsSimulated"/>.</summary>
    /// <exception cref="InvalidOperationException">This is <see cref="Unscoped"/>.</exception>
    public void SetPolicy(Func<int, bool> isSimulated)
    {
        if (_isUnscoped)
        {
            throw new InvalidOperationException($"{nameof(Unscoped)} simulates every entity and takes no policy.");
        }

        _isSimulated = isSimulated;
    }

    /// <summary>Tells subscribers entityId may have become simulated again -- see <see cref="EntityResumed"/>.</summary>
    public void RaiseResumed(int entityId) => _entityResumed?.Invoke(entityId);
}
