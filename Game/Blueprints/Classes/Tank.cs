using Engine.ECS.Systems;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.World;

namespace Game.Blueprints.Classes;

/// <summary>Tanks have 10% more max health than their race baseline, plus 10% more health regeneration.</summary>
/// <remarks>
/// Order-independent: if a race blueprint already ran, Tank boosts whichever health it established;
/// if not (Tank built standalone, or composed before a race), Tank merges in its own Simple baseline
/// first, so the class's mechanic still functions rather than silently doing nothing because of
/// composition order. Merging that baseline onto an entity that already has body parts would be
/// actively wrong -- HealthDamage dispatches Simple-first, so it would take the entity's parts out
/// of play entirely -- which is why it is conditional and the modifiers are not.
/// </remarks>
public static class Tank
{
    public static readonly Guid Id = new("45ddf671-3f76-4e23-9ac3-7a588282ec35");
    public const string Name = "Tank";

    private const short BaselineMaximumHealth = 100;
    private const float MaximumHealthBonusMultiplier = 0.10f;
    private const float HealthRegenBonusMultiplier = 0.10f;

    public static readonly BlueprintDefinition Definition = new(Id, Name) { Build = Build, Class = new ClassFacet() };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        var source = ActionSource.FromEntity(componentManager, context.EntityKeys, entityId, context.Definitions);

        if (!componentManager.GetPackedPool<SimpleHealthComponent>().Has(entityId) && !EntityBodyParts.For(componentManager, context.Definitions).Has(entityId))
        {
            componentManager.Merge(entityId, new SimpleHealthComponent(BaselineMaximumHealth, BaselineMaximumHealth));
        }

        MaximumHealthShift.ApplyModifier(componentManager, context.Definitions, entityId, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: MaximumHealthBonusMultiplier, expiresAtFrame: FrameDeadline.Never, source);

        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.HealthRegen, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: HealthRegenBonusMultiplier, expiresAtFrame: FrameDeadline.Never, source);
    }
}
