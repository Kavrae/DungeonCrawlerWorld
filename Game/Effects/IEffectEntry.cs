using Engine.Tags;
using Game.Modules.Auras;
using Game.Modules.StatModifiers;

namespace Game.Effects;

/// <summary>One piece of what an Effect does, owning its own application logic.</summary>
/// <remarks>
/// Adding a new kind of effect means adding one record implementing this, never touching Effect,
/// another entry, or anything that applies effects: an action, an item, a terrain contact and an
/// aura all hold the same lists and apply them through the same call. An entry knows nothing about
/// what applied it -- everything it needs arrives in the EffectContext. The same goes for checking
/// a build's content: an entry says what it names (ReferencedTags, ReferencedAuras,
/// NestedEffects) and the checks never look at what kind of entry it is.
/// </remarks>
public interface IEffectEntry
{
    /// <summary>The gameplay tags this entry names, each of which a module in the build must declare.</summary>
    IEnumerable<GameplayTag> ReferencedTags => [];

    /// <summary>The auras this entry names, which the build registers so each has an id before anything radiates it.</summary>
    IEnumerable<AuraDefinition> ReferencedAuras => [];

    /// <summary>The effects this entry applies in turn, for anything that walks every entry a definition holds.</summary>
    IReadOnlyList<Effect> NestedEffects => [];

    /// <summary>Where this entry lands when an activation reaches several targets or none: on each, or once.</summary>
    /// <remarks>
    /// An applier that reaches targets through tiles (an action, an item, a toggle) applies OnEachTarget
    /// entries per target and the others once (EffectSequence.ApplyOnEachTarget, ApplyOnce). One that
    /// applies to a single entity (an aura's tick, a terrain contact, a chained effect) applies every
    /// entry to that entity (EffectSequence.Apply).
    /// </remarks>
    EffectPlacement Placement => EffectPlacement.OnEachTarget;

    /// <summary>The stat modifier pairs that scale this entry's amounts (EffectModifiers): Outgoing on its cause, Incoming on its target. Empty for an entry with no amount.</summary>
    IReadOnlyList<(StatModifierTarget Outgoing, StatModifierTarget Incoming)> AmountModifiers => [];

    /// <summary>Whether what this entry grants lasts until something takes it back: no duration and no expiry of its own.</summary>
    /// <remarks>A toggle may hold such an entry only if it is an IReversibleEffectEntry; the build refuses one that isn't (ToggleContentValidation).</remarks>
    bool GrantsUntilRevoked => false;

    /// <summary>Why context.TargetEntityId can't take or supply this now, or None when Apply would do all of what the entry asks.</summary>
    /// <remarks>
    /// Asked before effects that are all or nothing -- what turning a toggle on takes, and what it does every
    /// interval -- so nothing is half-paid: if any entry refuses, none is applied. An entry that takes from
    /// its target asks against what is left after reservations and adds its own claim there when it doesn't
    /// refuse. An entry that can always be applied, or whose falling short is no failure (a heal on a full
    /// target), leaves this None.
    /// </remarks>
    EffectRefusal CanApply(in EffectContext context, ref EffectReservations reservations) => EffectRefusal.None;

    EffectOutcome Apply(in EffectContext context);
}

/// <summary>An effect entry that can take back what it granted.</summary>
/// <remarks>
/// What a toggle holds while it is on is applied under EffectContext.HeldGrantKey and reverted under
/// the same key when it goes off, so each toggle's grants stay its own. An entry that grants nothing
/// lasting, or something timed that ends by itself, reverts nothing.
/// </remarks>
public interface IReversibleEffectEntry : IEffectEntry
{
    /// <summary>Takes back what Apply granted context.TargetEntityId under context.HeldGrantKey.</summary>
    void Revert(in EffectContext context);
}
