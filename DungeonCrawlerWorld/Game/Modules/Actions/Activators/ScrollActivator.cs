using Engine.Tags;
using Game.Tags;

namespace Game.Modules.Actions.Activators;

/// <summary>Item-triggered activator for a one-time-use scroll: it casts its spell as the spell is, without the spell's costs.</summary>
/// <remarks>
/// The scroll's targeting and timing are its spell's, and its item's effects are the spell's (ItemDefinition takes them
/// from here). The spell's cooldown is left out, since a scroll is used up, and so are its activation effects: a scroll
/// costs nothing but itself. The tags it implies are a scroll's plus every tag the spell has, so what a use of the spell
/// is (Spell, Trait.Dodgeable, Effect.Healing) a use of the scroll is too.
/// </remarks>
/// <param name="SpellId">The spell the scroll names, as its record holds it: the Guid written back, whatever it resolves to.</param>
/// <param name="Spell">The spell SpellId resolves to this session -- itself, or the end of its replacements.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ScrollActivator(Guid SpellId, ActionDefinition Spell) : IActionActivator
{
    private static readonly GameplayTagSet ScrollTags = [GameTags.ItemConsumableScroll, GameTags.Spell, GameTags.Magic];

    public TargetingSpec Targeting { get; } = Spell.Activator.Targeting;

    public ActionTiming Timing { get; } = Spell.Activator.Timing with { CooldownFrames = null, CooldownMatchesActionLock = false };

    public GameplayTagSet ImpliedTags { get; } = ScrollTags.Union(Spell.Tags);
}
