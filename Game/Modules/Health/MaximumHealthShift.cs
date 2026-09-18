using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
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
    public static void Apply(ComponentManager componentManager, int entityId, float additiveBefore, float multiplicativeBefore)
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
            var storedMaximum = health.GetReadonly(entityId).MaximumHealth;
            var shift = StatModifierMath.CalculateTotal(storedMaximum, additiveAfter, multiplicativeAfter)
                - StatModifierMath.CalculateTotal(storedMaximum, additiveBefore, multiplicativeBefore);

            health.TryUpdate(entityId, shift, static (ref SimpleHealthComponent healthComponent, float amount) =>
                healthComponent.CurrentHealth = Shifted(healthComponent.CurrentHealth, amount));
            return;
        }

        var bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            var storedMaximum = bodyParts.GetReadonlyByDenseIndex(denseIndex).MaximumHealth;
            var shift = StatModifierMath.CalculateTotal(storedMaximum, additiveAfter, multiplicativeAfter)
                - StatModifierMath.CalculateTotal(storedMaximum, additiveBefore, multiplicativeBefore);

            bodyParts.UpdateByDenseIndex(denseIndex, shift, static (ref BodyPartComponent part, float amount) =>
                part.CurrentHealth = Shifted(part.CurrentHealth, amount));
        }
    }

    /// <summary>Grants a MaximumHealth modifier and moves current health with it, the one call a caller should use for this target.</summary>
    /// <remarks>StatModifierEffects.Apply on its own raises the cap and leaves current health where it was, which is what this exists to prevent. Every MaximumHealth grant, at blueprint build time or mid-game, goes through here.</remarks>
    public static void ApplyModifier(
        ComponentManager componentManager,
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
        Apply(componentManager, entityId, additiveBefore, multiplicativeBefore);
    }

    /// <summary>A decrease stops at 1 rather than disabling a part or killing outright -- see this class's own remarks.</summary>
    private static float Shifted(float current, float shift) =>
        shift >= 0f ? current + shift : System.Math.Max(1f, current + shift);

    private static MultiComponentPool<StatModifierComponent>? StatModifiers(ComponentManager componentManager) =>
        componentManager.IsRegistered<StatModifierComponent>() ? componentManager.GetMultiPool<StatModifierComponent>() : null;
}
