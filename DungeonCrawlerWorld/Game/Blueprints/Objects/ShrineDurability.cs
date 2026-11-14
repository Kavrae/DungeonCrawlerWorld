using Engine.ECS.Systems;
using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.Tags;

namespace Game.Blueprints.Objects;

/// <summary>What every shrine is built with: simple health, half damage from everything and none from fire -- so it can stand in lava -- and immunity to Burning, Poison and Paralysis.</summary>
/// <remarks>A shrine can be destroyed, and DeathSystem ends its aura when it is.</remarks>
public static class ShrineDurability
{
    private const float MaximumHealth = 100;

    private const float DamageResistance = 0.5f;

    public static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        MaximumHealthShift.MergeBaseMaximum(componentManager, entityId, MaximumHealth);

        var actionSource = context.PartSource();
        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, -DamageResistance, FrameDeadline.Never, actionSource);
        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, -1f, FrameDeadline.Never, actionSource, GameTags.DamageFire);

        var immunities = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
        StatusEffectImmunityEffects.GrantPermanent(immunities, entityId, StatusEffectType.Burning);
        StatusEffectImmunityEffects.GrantPermanent(immunities, entityId, StatusEffectType.Poison);
        StatusEffectImmunityEffects.GrantPermanent(immunities, entityId, StatusEffectType.Paralysis);
    }
}
