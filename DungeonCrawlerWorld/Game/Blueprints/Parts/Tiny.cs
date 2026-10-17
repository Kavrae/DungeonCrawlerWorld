using Game.Modules.Core.Components;

namespace Game.Blueprints.Parts;

/// <summary>Makes an entity small enough to share its cell, drawn in the map's tiny grid.</summary>
/// <remarks>
/// No blueprint of its own: a definition's NonBlockingKind is applied by EntityFactory when it builds
/// the entity's skeleton, before anything else it holds, because map occupancy can't wait for a
/// build -- so declaring the kind is the whole part (see NonBlockingComponent).
/// </remarks>
public static class Tiny
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000109");

    public const string Name = "Tiny";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        NonBlocking = NonBlockingKind.Tiny
    };
}
