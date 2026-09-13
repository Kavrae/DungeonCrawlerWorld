using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Poison.Systems;

/// <summary>
/// On each poisoned entity's tick, deals damage equal to the current stack count.
/// RemainingDurationTicks (independent of stack count) counts ticks down, and the whole effect is
/// removed in one go once that reaches 0.
/// </summary>
/// <remarks>
/// Driven by a timer wheel (PackedTimerWheel), the same shape as BurningSystem: only poisonings due
/// this frame are touched, on their exact frame at every processing tier (PLAN-timer-wheel.md).
/// </remarks>
public sealed class PoisonSystem : ISystem
{
    /// <summary>Every frame; the wheel only touches poisonings actually due.</summary>
    public byte StripeCount => 1;

    /// <summary>Passed as HealthDamage.Apply's damageTags on every tick -- lets a ConditionTag: Tag.Poison-scoped IncomingDamage modifier reduce poison damage specifically. Cached once rather than allocated fresh per tick.</summary>
    private static readonly Tag[] PoisonDamageTags = [Tag.Poison];

    private readonly PackedComponentPool<PoisonTimerComponent> _timers;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly MathUtility _mathUtility;
    private readonly MultiComponentPool<BodyPartComponent>? _bodyParts;
    private readonly PackedTimerWheel<PoisonTimerComponent> _wheel;

    // Cached once instead of passing the Tick method group every Update -- an instance method
    // group conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<PoisonTimerComponent> _tick;

    public PoisonSystem(
        PackedComponentPool<PoisonTimerComponent> timers,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        IPlayerQuery? playerQuery,
        MathUtility mathUtility,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        MultiComponentPool<BodyPartComponent>? bodyParts = null)
    {
        _timers = timers;
        _health = health;
        _statModifiers = statModifiers;
        _eventBus = eventBus;
        _playerQuery = playerQuery;
        _mathUtility = mathUtility;
        _bodyParts = bodyParts;
        _tick = Tick;
        _wheel = new PackedTimerWheel<PoisonTimerComponent>(timers);
    }

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, _tick);

    /// <summary>Returns whether the timer should be removed entirely (duration expired) -- see TimerFired's contract. Removing the timer component alone is enough to end the effect (StackCount lives on it, not a separate pool).</summary>
    private bool Tick(int entityId, PoisonTimerComponent timer, long now)
    {
        if (timer.RemainingDurationTicks == 0)
        {
            return true;
        }

        HealthDamage.Apply(_health, _eventBus, entityId, timer.StackCount, timer.Source, _playerQuery, StatusEffectDamageType.Describe(StatusEffectType.Poison), now, _statModifiers, _bodyParts, _mathUtility,
            targetRule: new BodyPartTargetRule(BodyPartType.Internal, BodyPartFallback.Random), damageTags: PoisonDamageTags);

        var remainingDuration = (ushort)(timer.RemainingDurationTicks - 1);
        if (remainingDuration == 0)
        {
            return true;
        }

        _timers.TryUpdate(entityId, remainingDuration, static (ref PoisonTimerComponent t, ushort remaining) =>
        {
            t.RemainingDurationTicks = remaining;
            t.RepeatEvery(PoisonEffects.TickIntervalFrames);
        });

        return false;
    }
}
