using Engine.Tags;
using Engine.Tiles;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Microsoft.Xna.Framework;

namespace Tests;

/// <summary>An action definition for a test that applies effects through EffectContext.FromEntity and needs no content of its own.</summary>
internal static class TestActivatables
{
    public static ActionDefinition Action(string name = "Test", GameplayTagSet tags = default) =>
        new(Guid.NewGuid(), name, null, "t", Color.White, tags, [Effect.None],
            new DirectAction(new TargetingSpec(TargetShape.Self, MaximumRange: 0, Anchor: TargetAnchor.Caster), new ActionTiming(ActionTimingCategory.Immediate)));
}
