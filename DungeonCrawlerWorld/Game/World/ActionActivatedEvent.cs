namespace Game.World;

/// <summary>
/// Published by ActionEffectResolver.Apply once per successful action activation -- Immediate/
/// FreeCast resolve synchronously through ActionActivationSystem, Delayed resolves later through
/// DelayedActionSystem once its windup ends, but both paths funnel through this same method, so
/// this fires exactly once per activation regardless of category. Immediate, not IBufferedEvent,
/// same reasoning as EntityDamagedEvent/StatusEffectAppliedEvent (published from this same method): a
/// consumer here only ever writes to a different component pool (e.g. AchievementUnlockedComponent)
/// than whatever system is mid-scan when this fires, so there's no reentrant-mutation hazard the
/// way EntityDiedEvent's own IBufferedEvent exists to avoid.
/// </summary>
public readonly record struct ActionActivatedEvent(int EntityId, Guid ActionId);
