using Engine.ECS.Components;
using Engine.Math;

namespace Game.Blueprints;

/// <summary>
/// Builds a base blueprint, then layers exactly one randomly-chosen variant blueprint on
/// top -- e.g. base "GoblinEngineer" with "explosives" vs. "vehicle" as variants, so an
/// EntityFactory can spawn the same recurring, named archetype with different flavor/loadout
/// each time rather than authoring every combination as its own blueprint. Implements
/// IBlueprint itself, so a variant set can be nested as a part of a CompositeBlueprint or
/// another variant set.
/// </summary>
public sealed class BlueprintVariantSet(IBlueprint baseBlueprint, IReadOnlyList<IBlueprint> variants, MathUtility mathUtility) : IBlueprint
{
    public void Build(ComponentManager componentManager, int entityId)
    {
        if (variants.Count == 0)
        {
            throw new InvalidOperationException("BlueprintVariantSet requires at least one variant.");
        }

        baseBlueprint.Build(componentManager, entityId);

        var variant = variants[mathUtility.Next(0, variants.Count)];
        variant.Build(componentManager, entityId);
    }
}
