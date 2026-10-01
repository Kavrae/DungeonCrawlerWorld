using Engine.ECS.Systems;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Auras;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.Tags;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>A stationary shrine that heals whatever stands near it.</summary>
/// <remarks>
/// A prop like TreasureChest: simple health, no race, no movement, so it never moves once spawned
/// and is always built at spawn. It declares its own healing aura (Aura) and radiates it (Auras). It takes
/// half damage from everything, none from fire -- so it can stand in lava -- and is immune to
/// Burning, Poison and Paralysis. It can be destroyed, and DeathSystem ends its aura when it is.
/// </remarks>
public static class HealingShrine
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000124");

    public const string Name = "Healing Shrine";

    private const string Description = "A weathered shrine. The air around it eases wounds.";

    private const float MaximumHealth = 100;

    /// <summary>Halves with each tile, so it reaches four tiles: 8 beside the shrine, then 4, 2, 1.</summary>
    private const byte HealingAuraStrength = 16;

    private const float DamageResistance = 0.5f;

    /// <summary>The shrine's aura: each tick heals one point of health per point of strength at the entity.</summary>
    /// <remarks>
    /// As a flat amount, so overlapping shrines add. On a creature with body parts the whole amount
    /// follows its healing priority -- the part missing the largest share of its health, the rule
    /// passive regeneration uses -- rather than being split evenly: an even split gives most of it to
    /// parts that are already full, where it is lost.
    /// </remarks>
    public static readonly AuraDefinition Aura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000304"), "Healing", Color.White,
        [new Effect([new DirectHeal(PercentOfMaxHealth: 0f, FlatAmount: 1f, BodyPartTargetMode: BodyPartTargetMode.LowestPercentage)])]);

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        Auras = [new AuraGrant(Aura, HealingAuraStrength)],
        Appearance = new() { Name = Name, Description = Description, Glyph = "+", GlyphColor = Color.White, SpriteName = "Shrine" }
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));

        var actionSource = ActionSource.FromEntity(componentManager, context.EntityKeys, entityId, context.Definitions);
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
