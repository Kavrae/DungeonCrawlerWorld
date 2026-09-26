using Game.Blueprints.Classes;
using Game.Blueprints.Parts;
using Game.Blueprints.Races;

namespace Game.Blueprints.Composites;

/// <summary>A fairy engineer that holds its cell despite Fairy's own wandering.</summary>
public static class StationaryFairyEngineer
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000120");

    public const string Name = "Stationary Fairy Engineer";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [Fairy.Id, Engineer.Id, StationaryPart.Id]
    };
}
