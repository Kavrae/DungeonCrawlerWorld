using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.World;

namespace Game.Modules.Actions.Systems;

/// <summary>Applies each active toggle's periodic effects every interval, and switches off a toggle whose upkeep can't be met or whose holder died.</summary>
/// <remarks>
/// <para>
/// Driven by a timer wheel over ActiveToggleComponent: only the toggles due this frame are touched,
/// each on its own timer, so two toggles on one entity tick separately. When one is due, its periodic
/// effects are applied if every one of them can be (all or nothing), and it is re-armed one interval
/// on. Otherwise its owner switches it off: its grants go and nothing is applied.
/// </para>
/// <para>
/// A holder that isn't simulated is not ticked, and owes nothing for that time when it resumes (as
/// an aura exposure doesn't). Neither is a holder that isn't on the map: something staged on a
/// trade-offer entity is on its way between holders, not in use.
/// </para>
/// <para>
/// A holder's death switches off every toggle its owner says ends with it, at once. A toggle switched
/// on for a holder that is already dead is due at once (Toggles.TurnOn), and is ended here the same
/// way by that first tick.
/// </para>
/// <para>This system knows no kind of toggle: what one is and how it goes off are its owner's (IToggleOwner).</para>
/// </remarks>
public sealed class ToggleUpkeepSystem : ISystem
{
    private readonly MultiComponentPool<ActiveToggleComponent> _activeToggles;
    private readonly Toggles _toggles;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly IMapQuery _mapQuery;
    private readonly EventBus _eventBus;
    private readonly SimulationClock _simulationClock;
    private readonly MultiTimerWheel<ActiveToggleComponent> _wheel;
    private readonly TimerFired<ActiveToggleComponent> _tick;
    private readonly List<ActiveToggleComponent> _togglesScratch = [];

    public ToggleUpkeepSystem(
        MultiComponentPool<ActiveToggleComponent> activeToggles,
        Toggles toggles,
        PackedComponentPool<DeadComponent> deadEntities,
        DirectComponentPool<TransformComponent> transforms,
        IMapQuery mapQuery,
        EventBus eventBus,
        SimulationClock simulationClock,
        SimulationScope simulationScope)
    {
        _activeToggles = activeToggles;
        _toggles = toggles;
        _deadEntities = deadEntities;
        _transforms = transforms;
        _mapQuery = mapQuery;
        _eventBus = eventBus;
        _simulationClock = simulationClock;
        _tick = Tick;
        _wheel = new MultiTimerWheel<ActiveToggleComponent>(activeToggles, simulationScope);

        // After the wheel's own resume handler, so the deadlines it just rescheduled are moved past what was owed.
        simulationScope.EntityResumed += OnEntityResumed;
        deadEntities.EntityAdded += OnEntityDied;
    }

    /// <summary>Every frame; the wheel only touches toggles actually due.</summary>
    public byte StripeCount => 1;

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, _tick);

    /// <summary>One due toggle. Never asks the wheel to remove it: a toggle is removed only by being switched off, which reverts what it holds.</summary>
    private bool Tick(int entityId, ActiveToggleComponent toggle, long now)
    {
        if (!_toggles.TryGetOwner(toggle.Owner.Kind, out var owner) ||
            !owner.TryResolveDefinition(entityId, in toggle, out var definition) ||
            definition.Toggle?.Periodic is not { } periodic)
        {
            return false;
        }

        if (_deadEntities.Has(entityId))
        {
            if (owner.EndsWhenHolderDies(definition))
            {
                SwitchOff(owner, entityId, in toggle, definition, ToggleEndReason.HolderDied, now);
            }

            return false;
        }

        if (!IsOnMap(entityId))
        {
            Rearm(entityId, toggle.Key, FrameDeadline.After(now, periodic.IntervalFrames));
            return false;
        }

        if (_toggles.TryApplyPeriodicEffects(entityId, definition, periodic, now))
        {
            Rearm(entityId, toggle.Key, FrameDeadline.Repeat(toggle.NextTickFrame, periodic.IntervalFrames));
            return false;
        }

        SwitchOff(owner, entityId, in toggle, definition, ToggleEndReason.PeriodicEffectsRefused, now);
        return false;
    }

    private bool IsOnMap(int entityId) =>
        _transforms.TryGetReadonly(entityId, out var transform) && _mapQuery.IsOnMap(transform.Position);

    private void Rearm(int entityId, uint key, uint nextTickFrame) =>
        _activeToggles.TryUpdateFirst(
            entityId,
            (Key: key, NextTickFrame: nextTickFrame),
            static (ref readonly ActiveToggleComponent toggle, (uint Key, uint NextTickFrame) state) => toggle.Key == state.Key,
            static (ref ActiveToggleComponent toggle, (uint Key, uint NextTickFrame) state) => toggle.NextTickFrame = state.NextTickFrame);

    private void SwitchOff(IToggleOwner owner, int entityId, in ActiveToggleComponent toggle, ActivatableDefinition definition, ToggleEndReason reason, long now)
    {
        owner.SwitchOff(entityId, in toggle, definition, now);
        _eventBus.Publish(new ToggleEndedEvent(entityId, definition.Name, reason));
    }

    /// <summary>Switches off every toggle of the entity that ends with its holder.</summary>
    /// <remarks>Copies the entity's toggles first: switching one off removes it, which reorders the chain being walked.</remarks>
    private void OnEntityDied(int entityId)
    {
        if (!_activeToggles.Has(entityId))
        {
            return;
        }

        var now = _simulationClock.CurrentFrame;
        _activeToggles.CopyAll(entityId, _togglesScratch);

        foreach (var toggle in _togglesScratch)
        {
            if (_toggles.TryGetOwner(toggle.Owner.Kind, out var owner) &&
                owner.TryResolveDefinition(entityId, in toggle, out var definition) &&
                owner.EndsWhenHolderDies(definition))
            {
                SwitchOff(owner, entityId, in toggle, definition, ToggleEndReason.HolderDied, now);
            }
        }
    }

    /// <summary>Moves each of a resumed entity's toggles past every interval owed while it was frozen, applying none.</summary>
    private void OnEntityResumed(int entityId)
    {
        if (!_activeToggles.Has(entityId))
        {
            return;
        }

        var now = _simulationClock.CurrentFrame;
        for (var denseIndex = _activeToggles.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _activeToggles.GetNextDenseIndex(denseIndex))
        {
            var toggle = _activeToggles.GetReadonlyByDenseIndex(denseIndex);
            if (toggle.NextTickFrame == FrameDeadline.Never || toggle.NextTickFrame > now ||
                !_toggles.TryGetOwner(toggle.Owner.Kind, out var owner) ||
                !owner.TryResolveDefinition(entityId, in toggle, out var definition) ||
                definition.Toggle?.Periodic is not { } periodic)
            {
                continue;
            }

            _activeToggles.UpdateByDenseIndex(denseIndex, (Interval: (int)periodic.IntervalFrames, Now: now),
                static (ref ActiveToggleComponent active, (int Interval, long Now) state) => active.SkipOwedPeriods(state.Interval, state.Now));
        }
    }
}
