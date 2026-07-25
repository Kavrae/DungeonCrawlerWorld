using Engine.ECS.Components;
using Game.World;

namespace Game.Modules.StatusEffects;

/// <summary>
/// Lets a concrete status-effect module (Burning, Poison, or any future effect -- harmful or
/// beneficial, an aura doesn't care which) plug into StatusEffectAuraSystem's generic
/// stack-granting without that system needing to hardcode any one effect's own ApplyStack
/// signature or stack-count storage. Registered via StatusEffectAuraApplierRegistry during
/// IGameModule.Configure (see BurningModule/PoisonModule) -- lives here, in the shared
/// StatusEffects module both effect modules already depend on for stack storage, rather than
/// under StatusEffectAura, so implementing it doesn't require a new compile-time dependency
/// in either direction.
/// </summary>
public interface IStatusEffectAuraApplier
{
    StatusEffectType EffectType { get; }

    /// <summary>This entity's current stack count for EffectType, or 0 if it has none.</summary>
    int GetCurrentStackCount(ComponentManager componentManager, int entityId);

    /// <summary>Applies exactly one more stack, attributed to source.</summary>
    void ApplyStack(ComponentManager componentManager, int entityId, StatusEffectSource source);
}
