namespace Game.Blueprints.Objects;

/// <summary>The Shop shell stocked by GeneralShopStock, under its own name.</summary>
public static class GeneralShop
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000103");

    public const string Name = "General Shop";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [Shop.Id, GeneralShopStock.Id],
        Appearance = new() { Name = Name }
    };
}
