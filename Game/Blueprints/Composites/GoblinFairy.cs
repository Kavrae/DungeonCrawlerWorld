using Game.Blueprints.Parts;
using Game.Blueprints.Races;

namespace Game.Blueprints.Composites;

/// <summary>Goblin with Fairy layered on top, stationary since the hybrid has no single coherent movement mode.</summary>
public static class GoblinFairy
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000121");

    public const string Name = "Goblin Fairy";

    public static readonly BlueprintDefinition Definition = new(Id, Name) { Includes = [Goblin.Id, Fairy.Id, StationaryPart.Id] };
}
