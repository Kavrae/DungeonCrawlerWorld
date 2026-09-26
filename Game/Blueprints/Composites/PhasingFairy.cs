using Game.Blueprints.Parts;
using Game.Blueprints.Races;

namespace Game.Blueprints.Composites;

/// <summary>A fairy that phases through what shares its cell.</summary>
public static class PhasingFairy
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000119");

    public const string Name = "Phasing Fairy";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [Fairy.Id, Phasing.Id]
    };
}
