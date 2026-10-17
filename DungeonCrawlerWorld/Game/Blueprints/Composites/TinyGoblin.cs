using Game.Blueprints.Parts;
using Game.Blueprints.Races;

namespace Game.Blueprints.Composites;

/// <summary>A goblin small enough to share its cell -- the starting neighborhood's tiny-grid fixture.</summary>
public static class TinyGoblin
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000118");

    public const string Name = "Tiny Goblin";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [Goblin.Id, Tiny.Id]
    };
}
