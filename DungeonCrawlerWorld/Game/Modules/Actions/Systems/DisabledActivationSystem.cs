using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;

namespace Game.Modules.Actions.Systems;

/// <summary>Ends Disabled rules on the frame they expire, and ends what a rule disables the moment it is granted or extended.</summary>
/// <remarks>
/// A rule landing on an entity (DisabledActivations.Grant) cancels a windup whose captured definition it matches, keeping
/// the lock the windup set, as a Stagger does; and switches off each toggle it matches through the feature owning it
/// (Toggles.SwitchOff), as a toggle whose upkeep fails is switched off -- not a use, so nothing is taken, locked or put
/// on cooldown. Both happen on every grant and refresh, which finds nothing to end once the first has. Expiry is a timer
/// wheel over the rules (something happens: the rule goes away); a holder that isn't simulated keeps its rules until it
/// resumes, as every wheel does.
/// </remarks>
public sealed class DisabledActivationSystem : ISystem
{
    /// <summary>Every frame; the wheel only touches rules actually ending.</summary>
    public byte StripeCount => 1;

    private readonly MultiComponentPool<DisabledActivationComponent> _rules;
    private readonly PackedComponentPool<PendingWindupComponent> _pendingWindups;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly MultiComponentPool<ActiveToggleComponent> _activeToggles;
    private readonly Toggles _toggles;
    private readonly SimulationClock _clock;
    private readonly MultiTimerWheel<DisabledActivationComponent> _wheel;
    private readonly List<uint> _toggleKeysToSwitchOffScratch = [];

    // Cached once: an instance method group conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<DisabledActivationComponent> _expire = static (_, _, _) => true;

    public DisabledActivationSystem(
        MultiComponentPool<DisabledActivationComponent> rules,
        PackedComponentPool<PendingWindupComponent> pendingWindups,
        PackedComponentPool<ActionLockComponent> actionLocks,
        MultiComponentPool<ActiveToggleComponent> activeToggles,
        Toggles toggles,
        SimulationClock clock,
        SimulationScope simulationScope)
    {
        _rules = rules;
        _pendingWindups = pendingWindups;
        _actionLocks = actionLocks;
        _activeToggles = activeToggles;
        _toggles = toggles;
        _clock = clock;
        _wheel = new MultiTimerWheel<DisabledActivationComponent>(rules, simulationScope);

        rules.ComponentChanged += EndWhatTheRuleDisables;
    }

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, _expire);

    private void EndWhatTheRuleDisables(int entityId, int denseIndex)
    {
        var query = _rules.GetReadonlyByDenseIndex(denseIndex).Query;
        var now = _clock.CurrentFrame;

        if (_pendingWindups.TryGetReadonly(entityId, out var windup) && query.Matches(windup.Definition.Tags))
        {
            WindupCancel.TryCancel(_pendingWindups, _actionLocks, entityId, now, releaseLock: false);
        }

        _toggleKeysToSwitchOffScratch.Clear();
        for (var toggleIndex = _activeToggles.GetFirstDenseIndex(entityId); toggleIndex != -1; toggleIndex = _activeToggles.GetNextDenseIndex(toggleIndex))
        {
            ref readonly var toggle = ref _activeToggles.GetReadonlyByDenseIndex(toggleIndex);
            if (_toggles.TryResolveDefinition(entityId, in toggle, out var definition) && query.Matches(definition.Tags))
            {
                _toggleKeysToSwitchOffScratch.Add(toggle.Key);
            }
        }

        foreach (var key in _toggleKeysToSwitchOffScratch)
        {
            _toggles.SwitchOff(entityId, key, now);
        }
    }
}
