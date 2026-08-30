using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Burning.Systems;

/// <summary>
/// Body-part-scoped counterpart to BurningSystem: ticks down each currently-burning body part's
/// own countdown and, once it reaches 0, deals damage equal to the current stack count to the
/// exact part its timer names (BodyPartSelection.FindByPartId, not a fresh targeting resolution)
/// and removes exactly one stack -- same "not per-stack damage" rule BurningSystem's own Tick
/// uses. Ticks a MultiComponentPool (several concurrently-burning parts per entity possible), so
/// the shared loop here is Engine.ECS.Systems.MultiCountdownTicker.Tick rather than
/// CountdownTicker.Tick (BurningSystem's own PackedComponentPool-only version) -- mirrors
/// StatusEffectAuraSystem's own TickExposures for the same "several due entries per entity in one
/// visit" reason.
/// </summary>
public sealed class BodyPartBurningSystem : ISystem
{
    private const byte StripeCountValue = 15;

    public byte StripeCount => StripeCountValue;

    private readonly MultiComponentPool<BodyPartBurningTimerComponent> _timers;
    private readonly MultiComponentPool<BodyPartStatusEffectStack> _stacks;
    private readonly MultiComponentPool<BodyPartComponent> _bodyParts;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly EventBus _eventBus;
    private readonly IPlayerQuery? _playerQuery;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly TieredEntityStripeSet _tieredStripeSet;
    private readonly List<(int EntityId, BodyPartBurningTimerComponent Component)> _pendingTimerRemovals = [];

    // Cached once instead of passing the Tick method group at the MultiCountdownTicker.Tick call
    // site every Update -- see BurningSystem's own field for why this matters (an instance method
    // group conversion allocates a fresh delegate every evaluation).
    private readonly Func<int, BodyPartBurningTimerComponent, bool> _tick;

    public BodyPartBurningSystem(
        MultiComponentPool<BodyPartBurningTimerComponent> timers,
        MultiComponentPool<BodyPartStatusEffectStack> stacks,
        MultiComponentPool<BodyPartComponent> bodyParts,
        PackedComponentPool<SimpleHealthComponent> health,
        EventBus eventBus,
        IPlayerQuery? playerQuery,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        PackedComponentPool<DeadComponent>? deadEntities = null)
    {
        _timers = timers;
        _stacks = stacks;
        _bodyParts = bodyParts;
        _health = health;
        _statModifiers = statModifiers;
        _eventBus = eventBus;
        _playerQuery = playerQuery;
        _deadEntities = deadEntities;
        _tick = Tick;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, timers, processingTiers, processingTierEvents);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        for (var tierIndex = 0; tierIndex < _tieredStripeSet.TierCount; tierIndex++)
        {
            MultiCountdownTicker.Tick(
                _timers,
                _tieredStripeSet.GetTierBucket(tierIndex, time.FrameCount),
                _pendingTimerRemovals,
                _tick,
                _tieredStripeSet.GetTierFramesPerVisit(tierIndex));
        }
    }

    /// <summary>Returns whether this specific part's timer entry should be removed entirely (stacks fully decayed) -- see MultiCountdownTicker.Tick's own doc comment for the contract. Only mutates the entry (via TryUpdateFirst) on the non-removal path, so a removal's own equality-based match still finds the original, untouched snapshot.</summary>
    private bool Tick(int entityId, BodyPartBurningTimerComponent timer)
    {
        var stackCount = timer.StackCount;
        if (stackCount == 0)
        {
            return true;
        }

        var source = default(StatusEffectSource);
        var foundStackDenseIndex = -1;

        for (var denseIndex = _stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _stacks.GetNextDenseIndex(denseIndex))
        {
            var stack = _stacks.GetReadonlyByDenseIndex(denseIndex);
            if (stack.EffectType == StatusEffectType.Burning && stack.PartId == timer.PartId)
            {
                foundStackDenseIndex = denseIndex;
                source = stack.Source;
                break;
            }
        }

        var bodyPartDenseIndex = BodyPartSelection.FindByPartId(_bodyParts, entityId, timer.PartId);
        if (bodyPartDenseIndex != -1)
        {
            var effectiveAmount = MathUtility.ClampUShort(
                StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.IncomingDamage, stackCount),
                0,
                ushort.MaxValue);

            BodyPartDamageEffects.ApplyToPart(_bodyParts, bodyPartDenseIndex, _statModifiers, entityId, effectiveAmount);
            // Refreshed unconditionally, not only when ApplyToPart's own 0-only lockout fires --
            // a part that gets singed but never fully disabled (a small part like a Foot against
            // a lightly-stacked burn) would otherwise have zero regen protection once the fire's
            // stacks run out, since BodyPartSelection.PickLowestPercentage's separate "is
            // currently burning" exclusion stops applying the instant the last stack ticks off.
            BodyPartDamageEffects.ResetRegenLockout(_bodyParts, bodyPartDenseIndex);
            BodyPartDamageEffects.PublishDamageEvents(_health, _bodyParts, _eventBus, bodyPartDenseIndex, entityId, effectiveAmount, source, _playerQuery, StatusEffectDamageType.Describe(StatusEffectType.Burning), _statModifiers, _deadEntities);
        }

        if (foundStackDenseIndex != -1)
        {
            _stacks.RemoveByDenseIndex(foundStackDenseIndex);
        }

        var remainingStacks = (byte)(stackCount - 1);
        if (remainingStacks == 0)
        {
            return true;
        }

        _timers.TryUpdateFirst(entityId, (timer.PartId, remainingStacks), static (ref readonly BodyPartBurningTimerComponent t, (byte PartId, byte RemainingStacks) state) => t.PartId == state.PartId,
            static (ref BodyPartBurningTimerComponent t, (byte PartId, byte RemainingStacks) state) =>
            {
                t.StackCount = state.RemainingStacks;
                t.FramesUntilNextTick = BurningEffects.TickIntervalFrames;
            });

        return false;
    }
}
