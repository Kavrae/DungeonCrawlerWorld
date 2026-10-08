using Engine.ECS.Systems;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Auras.Components;
using Game.Modules.StatModifiers;

namespace Game.Modules.Auras;

/// <summary>Makes the marked entity radiate Aura -- or, with nothing marked, an anchor at the tile aimed at -- for a time or until it is taken back.</summary>
/// <remarks>
/// <para>
/// Placed once per activation (EffectPlacement.OncePerActivation), however many entities the
/// activation reaches: on the marked entity (Target mode, or a toggle's holder), or, with no target
/// entity, on an AuraAnchor spawned at context.TargetLocation and owned by the source entity
/// (AuraAnchors). So a light cast at someone follows them, and one cast at the ground stays there.
/// This entry only adds and removes the source; radiating, exposing nearby entities and keeping the
/// AuraField in step are AuraSourceEffects' and AuraSystem's.
/// </para>
/// <para>
/// Timed (DurationFrames set): the target holds one unkeyed source of the aura, renewed on re-apply,
/// and an AuraSourceExpiryComponent ends it once the duration runs out. The duration is scaled as a
/// Buff's StatModifierGrant duration is: context.DurationScaleMultiplier, then Outgoing/IncomingBuffDuration.
/// An anchor ends with its source.
/// Scroll of Torch is the concrete user.
/// </para>
/// <para>
/// Permanent (DurationFrames null): held by a toggle (context.HeldGrantKey set), it adds a source of
/// its own under that key -- on the holder, or on an anchor the holder owns -- beside any other source
/// of the aura, and Revert removes exactly that one. Outside a toggle it ensures the target's one
/// unkeyed source, so applying it twice leaves one.
/// </para>
/// </remarks>
/// <param name="Aura">The aura's definition, declared with whatever grants it: what the aura does and the colour it glows.</param>
/// <param name="Power">The source's value at its own tile (see AuraSourceComponent).</param>
/// <param name="Size">How many tiles the source reaches.</param>
public sealed record AuraSourceGrant(
    AuraDefinition Aura,
    ushort Power,
    byte Size,
    ushort? DurationFrames = null) : IReversibleEffectEntry
{
    private static readonly (StatModifierTarget, StatModifierTarget)[] PermanentModifiers =
    [
        (StatModifierTarget.OutgoingAuraPower, StatModifierTarget.IncomingAuraPower),
        (StatModifierTarget.OutgoingAuraSize, StatModifierTarget.IncomingAuraSize),
    ];

    private static readonly (StatModifierTarget, StatModifierTarget)[] TimedModifiers =
    [
        .. PermanentModifiers,
        (StatModifierTarget.OutgoingBuffDuration, StatModifierTarget.IncomingBuffDuration),
    ];

    public IEnumerable<AuraDefinition> ReferencedAuras => [Aura];

    public EffectPlacement Placement => EffectPlacement.OncePerActivation;

    /// <remarks>Power and size, and a timed grant's duration as a Buff's (StatModifierGrant.ScaleDurationFrames). Incoming is skipped for an anchor, which has no target entity.</remarks>
    public IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers => DurationFrames is null ? PermanentModifiers : TimedModifiers;

    public bool GrantsUntilRevoked => DurationFrames is null;

    public EffectOutcome Apply(in EffectContext context)
    {
        var power = EffectModifiers.ScaleToUShort(in context, Power, StatModifierTarget.OutgoingAuraPower, StatModifierTarget.IncomingAuraPower);
        var size = EffectModifiers.ScaleToByte(in context, Size, StatModifierTarget.OutgoingAuraSize, StatModifierTarget.IncomingAuraSize);

        var expiresAtFrame = StatModifierGrant.ScaleDurationFrames(in context, DurationFrames, StatModifierPolarity.Buff) is { } durationFrames
            ? FrameDeadline.After(context.Now, durationFrames)
            : (uint?)null;
        var heldGrantKey = DurationFrames is null ? context.HeldGrantKey : null;

        if (context.TargetEntityId == EffectContext.NoTargetEntity)
        {
            return context.TargetLocation is { } tile &&
                context.Services.AuraAnchors.Place(tile, Aura, power, size, context.Source, context.SourceEntityId, heldGrantKey, expiresAtFrame) >= 0
                ? EffectOutcome.Applied
                : EffectOutcome.NoEffect;
        }

        if (heldGrantKey is { } key)
        {
            context.Services.AuraSources.AddHeld(context.TargetEntityId, Aura, power, size, key);
        }
        else
        {
            context.Services.AuraSources.Apply(context.TargetEntityId, Aura, power, size);
        }

        if (expiresAtFrame is { } expires)
        {
            context.Services.ComponentManager.Merge(context.TargetEntityId, new AuraSourceExpiryComponent(context.Services.AuraSources.GetId(Aura), expires));
        }

        return EffectOutcome.Applied;
    }

    /// <remarks>Takes back the holder's own source under the key and any anchor it placed under it. A timed grant ends by its own expiry, and a permanent one applied outside a toggle has no key to be reverted under.</remarks>
    public void Revert(in EffectContext context)
    {
        if (DurationFrames is null && context.HeldGrantKey is { } heldGrantKey)
        {
            context.Services.AuraSources.RemoveHeld(context.TargetEntityId, Aura, heldGrantKey);
            context.Services.AuraAnchors.EndHeld(context.TargetEntityId, heldGrantKey);
        }
    }
}
