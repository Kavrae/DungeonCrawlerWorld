using Game.Modules.StatusEffects;

namespace Game.World;

/// <summary>
/// Published by StatusEffectImmunity.IsImmune each time it blocks a grant (a stack that would
/// otherwise have been added to entityId) -- mirrors StatusEffectAppliedEvent's shape, one entity
/// away from the effect actually landing rather than landing. Only published when the player is
/// involved -- either as the entity that was immune or as the source that attempted the grant --
/// the same player-only scope EntityDamagedEvent/EntityHealedEvent already use.
/// </summary>
public readonly record struct StatusEffectImmunityBlockedEvent(int EntityId, StatusEffectType EffectType, StatusEffectSource Source);
