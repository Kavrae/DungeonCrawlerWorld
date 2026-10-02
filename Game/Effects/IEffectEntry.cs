using Engine.Tags;
using Game.Modules.Auras;

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

    EffectOutcome Apply(in EffectContext context);
}
