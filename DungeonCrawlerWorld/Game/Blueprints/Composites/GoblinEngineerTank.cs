using Game.Blueprints.Classes;
using Game.Blueprints.Races;

namespace Game.Blueprints.Composites;

/// <summary>A goblin with two classes, Engineer and Tank.</summary>
public static class GoblinEngineerTank
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000122");

    public const string Name = "Goblin Engineer Tank";

    public static readonly BlueprintDefinition Definition = new(Id, Name) { Includes = [Goblin.Id, Engineer.Id, Tank.Id] };
}
