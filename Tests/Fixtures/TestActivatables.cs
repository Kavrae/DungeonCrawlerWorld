using Engine.Tags;
using Engine.Tiles;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Microsoft.Xna.Framework;

namespace Tests.Fixtures;

/// <summary>Action definitions and activators for a test that needs no content of its own: an action to apply effects through EffectContext.FromEntity, a spell, and a scroll of one.</summary>
internal static class TestActivatables
{
    public static ActionDefinition Action(string name = "Test", GameplayTagSet tags = default) =>
        new(Guid.NewGuid(), name, null, "t", Color.White, tags, [Effect.None],
            new DirectAction(new TargetingSpec(TargetShape.Self, MaximumRange: 0, Anchor: TargetAnchor.Caster), new ActionTiming(ActionTimingCategory.Immediate)));

    /// <summary>A spell with targeting and timing, holding effects (Effect.None by default), under a new Guid unless id is given.</summary>
    public static ActionDefinition Spell(TargetingSpec targeting, ActionTiming? timing = null, IReadOnlyList<Effect>? effects = null, Guid? id = null, string name = "Test Spell") =>
        new(id ?? Guid.NewGuid(), name, null, "s", Color.White, [], effects ?? [Effect.None],
            new SpellActivator(targeting, timing ?? new ActionTiming(ActionTimingCategory.Immediate)));

    /// <summary>A scroll of spell, naming it by its own Guid.</summary>
    public static ScrollActivator Scroll(ActionDefinition spell) => new(spell.Id, spell);

    /// <summary>A scroll of a new spell with targeting and timing.</summary>
    public static ScrollActivator Scroll(TargetingSpec targeting, ActionTiming? timing = null) => Scroll(Spell(targeting, timing));
}
