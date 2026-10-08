using Game.Effects;

namespace Game.Modules.Actions;

/// <summary>Asks and applies a definition's ActivationEffects: the one place any action or item use takes them.</summary>
/// <remarks>Source and target are both the user, and the activator's tags condition the modifiers, as for anything a use applies to itself.</remarks>
public static class ActivationEffectsApplier
{
    /// <summary>The first reason definition's activation effects can't all be applied to entityId now, or None.</summary>
    public static EffectRefusal GetRefusal(EffectServices effectServices, int entityId, ActivatableDefinition definition, long now) =>
        definition.ActivationEffects.Count == 0
            ? EffectRefusal.None
            : EffectSequence.CanApply(definition.ActivationEffects, UserContext(effectServices, entityId, definition, now));

    /// <summary>Applies definition's activation effects to entityId. The caller has asked GetRefusal.</summary>
    public static void Apply(EffectServices effectServices, int entityId, ActivatableDefinition definition, long now)
    {
        if (definition.ActivationEffects.Count > 0)
        {
            EffectSequence.Apply(definition.ActivationEffects, UserContext(effectServices, entityId, definition, now));
        }
    }

    /// <summary>The context a definition's activation effects apply in: source and target both entityId.</summary>
    internal static EffectContext UserContext(EffectServices effectServices, int entityId, ActivatableDefinition definition, long now) =>
        EffectContext.FromEntity(effectServices, entityId, entityId, definition.Name, definition.Tags, now);
}
