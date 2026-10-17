using Game.Modules.Core.Components;

namespace Game.Blueprints.Parts;

/// <summary>Makes an entity phase through whatever else shares its cell.</summary>
/// <remarks>No blueprint of its own, for the same reason as <see cref="Tiny"/>.</remarks>
public static class Phasing
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000110");

    public const string Name = "Phasing";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        NonBlocking = NonBlockingKind.Phasing
    };
}
