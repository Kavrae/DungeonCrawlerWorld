using Game.Blueprints.Classes;
using Game.Blueprints.Races;

namespace Game.Blueprints.Composites;

/// <summary>The player: the Human race, the Tank class, then everything only the player has (see PlayerKit).</summary>
public static class Player
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000117");

    public const string Name = "Player";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [Human.Id, Tank.Id, PlayerKit.Id]
    };
}
