using Engine.Math;
using Game.Blueprints.Parts;

namespace Game.Blueprints.Composites;

/// <summary>A goblin engineer made a boss -- a composite of a composite.</summary>
public static class GoblinForeman
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000116");

    public const string Name = "Goblin Foreman";

    private const string Description = "A goblin engineer who has worked its way up to shouting at the others. Twice as hard to put down, and it knows it.";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [GoblinEngineer.Id, Boss.Id],
        Appearance = new() { Description = Description },
        Size = new Vector2Byte(2, 2)
    };
}
