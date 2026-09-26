namespace Game.Blueprints.Objects;

/// <summary>The Shop shell stocked by PotionShopStock, under its own name.</summary>
public static class PotionShop
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000104");

    public const string Name = "Potion Shop";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [Shop.Id, PotionShopStock.Id],
        Appearance = new() { Name = Name }
    };
}
