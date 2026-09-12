using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Burning.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Burning.Systems;

/// <summary>
/// On each burning entity's tick, deals damage equal to the current stack count and removes
/// exactly one stack (not per-stack damage -- 7 stacks deals 7 damage total, not 49), attributed
/// to whichever source first set the entity ablaze (BurningTimerComponent.Source, set once on the
/// 0-to-1 transition -- see BurningEffects.ApplyStack).
/// </summary>
/// <remarks>
/// Driven by a timer wheel (PackedTimerWheel): only burns actually due this frame are touched, on
/// their exact frame at every processing tier (PLAN-timer-wheel.md). BurningEffects.ApplyStack
/// adding the component is all that schedules a new burn; Tick re-arms by writing NextTickFrame.
/// </remarks>
public sealed class BurningSystem : ISystem
{
    /// <summary>Passed as HealthDamage.Apply's damageTags on every tick -- lets a ConditionTag: Tag.Fire-scoped IncomingDamage modifier reduce burning damage specifically. Cached once rather than allocated fresh per tick.</summary>
    private static readonly Tag[] BurningDamageTags = [Tag.Fire];

    /// <summary>Every frame; the wheel only touches burns actually due.</summary>
    public byte StripeCount => 1;

    private readonly PackedComponentPool<BurningTimerComponent> _timers;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly MathUtility _mathUtility;
    private readonly MultiComponentPool<BodyPartComponent>? _bodyParts;
    private readonly PackedTimerWheel<BurningTimerComponent> _wheel;

    // Cached once instead of passing the Tick method group every Update -- an instance method
    // group conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<BurningTimerComponent> _tick;

    public BurningSystem(
        PackedComponentPool<BurningTimerComponent> timers,
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
        _wheel = new PackedTimerWheel<BurningTimerComponent>(timers);
    }

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, _tick);

    /// <summary>Returns whether the timer should be removed entirely (stacks fully decayed) -- see TimerFired's contract.</summary>
    private bool Tick(int entityId, BurningTimerComponent timer, long now)
    {
        var stackCount = timer.StackCount;
        if (stackCount == 0)
        {
            return true;
        }

        HealthDamage.Apply(_health, _eventBus, entityId, stackCount, timer.Source, _playerQuery, StatusEffectDamageType.Describe(StatusEffectType.Burning), now, _statModifiers, _bodyParts, _mathUtility, damageTags: BurningDamageTags);

        var remainingStacks = (byte)(stackCount - 1);
        if (remainingStacks == 0)
        {
            return true;
        }

        _timers.TryUpdate(entityId, remainingStacks, static (ref BurningTimerComponent t, byte stacks) =>
        {
            t.StackCount = stacks;
            t.RepeatEvery(BurningEffects.TickIntervalFrames);
        });

        return false;
    }
}
