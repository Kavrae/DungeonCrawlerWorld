using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Blueprints;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Health;

/// <summary>
/// Keeps current health in step with an entity's modifier-effective maximum: a MaximumHealth buff
/// heals by exactly what it added, and the same buff wearing off takes exactly that much back.
/// </summary>
/// <remarks>
/// <para>
/// The rule is that missing health is what survives a change of maximum. Gaining 50 maximum while
/// 30 short of it leaves you 30 short of the new one, and losing that maximum again leaves you 30
/// short of the old one -- so a buff cannot be cycled for free healing, and a buff applied while
/// hurt does not quietly top you up.
/// </para>
/// <para>
/// Neither direction is damage or healing: both write the health pools directly rather than going
/// through HealthDamage/HealthHeal, so no EntityDamagedEvent or EntityHealedEvent is published, no
/// IncomingDamage/IncomingHealing modifier scales the shift, and nothing attributes it to a killer.
/// A buff running out is bookkeeping, not a hit. For the same reason a decrease never takes a part
/// below 1: losing a buff is not a way to die or to have a limb disabled.
/// </para>
/// <para>
/// Capture, mutate, apply. <see cref="Capture"/> snapshots the entity's MaximumHealth sums, the
/// caller changes the modifiers, and <see cref="Apply"/> moves current health by however far each
/// maximum actually travelled. Two floats cover an entity's whole body part list because of how
/// the modifier formula composes -- see StatModifierMath.GetSums.
/// </para>
/// </remarks>
public static class MaximumHealthShift
{
    /// <summary>Snapshots the entity's MaximumHealth modifier state, to be handed to <see cref="Apply"/> once that state has changed.</summary>
    public static void Capture(ComponentManager componentManager, int entityId, out float additiveSum, out float multiplicativeSum) =>
        StatModifierMath.GetSums(StatModifiers(componentManager), entityId, StatModifierTarget.MaximumHealth, out additiveSum, out multiplicativeSum);

    /// <summary>Moves current health by the distance the effective maximum travelled since <see cref="Capture"/>.</summary>
    public static void Apply(ComponentManager componentManager, BlueprintRegistry creatures, int entityId, float additiveBefore, float multiplicativeBefore)
    {
        var statModifiers = StatModifiers(componentManager);
        StatModifierMath.GetSums(statModifiers, entityId, StatModifierTarget.MaximumHealth, out var additiveAfter, out var multiplicativeAfter);

        if (additiveBefore == additiveAfter && multiplicativeBefore == multiplicativeAfter)
        {
            return;
        }

        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        if (health.Has(entityId))
        {
            var storedMaximum = health.GetReadonly(entityId).BaseMaximumHealth;
            var shift = StatModifierMath.CalculateTotal(storedMaximum, additiveAfter, multiplicativeAfter)
                - StatModifierMath.CalculateTotal(storedMaximum, additiveBefore, multiplicativeBefore);

            health.TryUpdate(entityId, new ShiftedHealthUpdate(shift));
            return;
        }

        var bodyParts = BodyPartsOf(componentManager, creatures);

        foreach (var part in bodyParts.Parts(entityId))
        {
            var shift = StatModifierMath.CalculateTotal(part.BaseMaximumHealth, additiveAfter, multiplicativeAfter)
                - StatModifierMath.CalculateTotal(part.BaseMaximumHealth, additiveBefore, multiplicativeBefore);

            bodyParts.SetCurrentHealth(entityId, part.PartId, Shifted(part.CurrentHealth, shift));
        }
    }

    /// <summary>Grants a MaximumHealth modifier and moves current health with it, the one call a caller should use for this target.</summary>
    /// <remarks>StatModifierEffects.Apply on its own raises the cap and leaves current health where it was, which is what this exists to prevent. Every MaximumHealth grant, at blueprint build time or mid-game, goes through here.</remarks>
    public static void ApplyModifier(
        ComponentManager componentManager,
        BlueprintRegistry creatures,
        int entityId,
        StatModifierOperation operation,
        StatModifierPolarity polarity,
        bool canModify,
        float magnitude,
        uint expiresAtFrame,
        ActionSource source)
    {
        Capture(componentManager, entityId, out var additiveBefore, out var multiplicativeBefore);
        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.MaximumHealth, operation, polarity, canModify, magnitude, expiresAtFrame, source);
        Apply(componentManager, creatures, entityId, additiveBefore, multiplicativeBefore);
    }

    /// <summary>Merges a source's base maximum into entityId's simple health -- added at full health when it has none -- and moves current health by as far as its modified maximum travelled.</summary>
    /// <remarks>
    /// For what originates health (a race, a class, an object's blueprint), whenever it is built: the bases merge by the
    /// pool's policy and the entity's MaximumHealth modifiers apply on top of the result, so a race merged onto a boss
    /// raises the base the boss's multiplier scales. Missing health is kept, as for any change of maximum.
    /// </remarks>
    public static void MergeBaseMaximum(ComponentManager componentManager, int entityId, float baseMaximumHealth)
    {
        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        Capture(componentManager, entityId, out var additiveSum, out var multiplicativeSum);
        var hadHealth = health.TryGetReadonly(entityId, out var before);

        componentManager.Merge(entityId, new SimpleHealthComponent(baseMaximumHealth, baseMaximumHealth));

        var baseAfter = health.GetReadonly(entityId).BaseMaximumHealth;
        var baseBefore = hadHealth ? before.BaseMaximumHealth : 0f;
        var modifiedBefore = hadHealth ? StatModifierMath.CalculateTotal(baseBefore, additiveSum, multiplicativeSum) : 0f;
        var modifiedTravel = StatModifierMath.CalculateTotal(baseAfter, additiveSum, multiplicativeSum) - modifiedBefore;
        var baseTravel = baseAfter - baseBefore;
        if (modifiedTravel != baseTravel)
        {
            health.TryUpdate(entityId, new ShiftedHealthUpdate(modifiedTravel - baseTravel));
        }
    }

    /// <summary>A decrease stops at 1 rather than disabling a part or killing outright -- see this class's own remarks.</summary>
    private static float Shifted(float current, float shift) =>
        shift >= 0f ? current + shift : System.Math.Max(1f, current + shift);

    private static EntityBodyParts BodyPartsOf(ComponentManager componentManager, BlueprintRegistry creatures) =>
        EntityBodyParts.For(componentManager, creatures);

    private static MultiComponentPool<StatModifierComponent> StatModifiers(ComponentManager componentManager) =>
        componentManager.GetMultiPool<StatModifierComponent>();

    private readonly struct ShiftedHealthUpdate(float shift) : IComponentUpdater<SimpleHealthComponent>
    {
        public void Update(ref SimpleHealthComponent health) => health.CurrentHealth = Shifted(health.CurrentHealth, shift);
    }
}
