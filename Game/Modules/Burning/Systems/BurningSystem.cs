using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Burning.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Burning.Systems;

/// <summary>
/// Ticks down each burning entity's countdown and, once it reaches 0, deals damage equal to
/// the current stack count and removes exactly one stack (not per-stack damage -- 7 stacks
/// deals 7 damage total, not 49), attributed to whichever source first set the entity ablaze
/// (BurningTimerComponent.Source, set once on the 0-to-1 transition -- see BurningEffects.ApplyStack).
/// The decrement-or-fire loop itself is Engine.ECS.Systems.CountdownTicker.Tick, shared with
/// PoisonSystem/ContactDamageSystem/StatusEffectAuraSystem -- this class only supplies the
/// entity-id source and what "ticking" actually does.
/// </summary>
public sealed class BurningSystem : ISystem
{
    /// <summary>Passed as HealthDamage.Apply's damageTags on every tick -- lets a ConditionTag: Tag.Fire-scoped IncomingDamage modifier reduce burning damage specifically. Cached once rather than allocated fresh per tick.</summary>
    private static readonly Tag[] BurningDamageTags = [Tag.Fire];

    private const byte StripeCountValue = 15;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<BurningTimerComponent> _timers;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly MathUtility _mathUtility;
    private readonly MultiComponentPool<BodyPartComponent>? _bodyParts;
    private readonly TieredEntityStripeSet _tieredStripeSet;
    private readonly List<int> _pendingTimerRemovals = [];

    // Cached once instead of passing the Tick method group at the CountdownTicker.Tick call
    // site every Update -- see ContactDamageSystem's own field for why this matters (an
    // instance method group conversion allocates a fresh delegate every evaluation).
    private readonly Func<int, BurningTimerComponent, bool> _tick;

    public BurningSystem(
        PackedComponentPool<BurningTimerComponent> timers,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        IPlayerQuery? playerQuery,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
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

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, timers, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        for (var tierIndex = 0; tierIndex < _tieredStripeSet.TierCount; tierIndex++)
        {
            CountdownTicker.Tick(_timers, _tieredStripeSet.GetTierBucket(tierIndex, time.FrameCount), _pendingTimerRemovals, _tick, _tieredStripeSet.GetTierFramesPerVisit(tierIndex));
        }
    }

    /// <summary>Returns whether the timer should be removed entirely (stacks fully decayed) -- see CountdownTicker.Tick's own doc comment for the contract.</summary>
    private bool Tick(int entityId, BurningTimerComponent timer)
    {
        var stackCount = timer.StackCount;
        if (stackCount == 0)
        {
            return true;
        }

        HealthDamage.Apply(_health, _eventBus, entityId, stackCount, timer.Source, _playerQuery, StatusEffectDamageType.Describe(StatusEffectType.Burning), _statModifiers, _bodyParts, _mathUtility, damageTags: BurningDamageTags);

        var remainingStacks = (byte)(stackCount - 1);
        if (remainingStacks == 0)
        {
            return true;
        }

        _timers.TryUpdate(entityId, remainingStacks, static (ref BurningTimerComponent t, byte remainingStacks) =>
        {
            t.StackCount = remainingStacks;
            t.FramesUntilNextTick = BurningEffects.TickIntervalFrames;
        });

        return false;
    }
}
