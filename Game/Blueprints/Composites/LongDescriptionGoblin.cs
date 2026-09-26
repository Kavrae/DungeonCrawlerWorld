using Game.Blueprints.Parts;
using Game.Blueprints.Races;

namespace Game.Blueprints.Composites;

/// <summary>A goblin whose description is long enough to word-wrap -- see LongDescriptionPart.</summary>
public static class LongDescriptionGoblin
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000123");

    public const string Name = "Long Description Goblin";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [Goblin.Id, LongDescriptionPart.Id]
    };
}
