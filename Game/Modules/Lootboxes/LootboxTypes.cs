namespace Game.Modules.Lootboxes;

/// <summary>The built-in loot box types, registered by LootboxModule.</summary>
/// <cleanupVersion>1</cleanupVersion>
public static class LootboxTypes
{
    public static readonly LootboxTypeDefinition Adventurer = new(new("6e2a9d41-7b3c-4f15-8a2e-000000000001"), "Adventurer");
    public static readonly LootboxTypeDefinition Alchemist = new(new("6e2a9d41-7b3c-4f15-8a2e-000000000002"), "Alchemist");
    public static readonly LootboxTypeDefinition Exorcist = new(new("6e2a9d41-7b3c-4f15-8a2e-000000000003"), "Exorcist");
    public static readonly LootboxTypeDefinition Investor = new(new("6e2a9d41-7b3c-4f15-8a2e-000000000004"), "Investor");
    public static readonly LootboxTypeDefinition Librarian = new(new("6e2a9d41-7b3c-4f15-8a2e-000000000005"), "Librarian");
    public static readonly LootboxTypeDefinition Weapon = new(new("6e2a9d41-7b3c-4f15-8a2e-000000000006"), "Weapon");
    public static readonly LootboxTypeDefinition Boss = new(new("6e2a9d41-7b3c-4f15-8a2e-000000000007"), "Boss");
    public static readonly LootboxTypeDefinition Quest = new(new("6e2a9d41-7b3c-4f15-8a2e-000000000008"), "Quest");
    public static readonly LootboxTypeDefinition ViewerGift = new(new("6e2a9d41-7b3c-4f15-8a2e-000000000009"), "Viewer Gift");
    public static readonly LootboxTypeDefinition SponsorGift = new(new("6e2a9d41-7b3c-4f15-8a2e-00000000000a"), "Sponsor Gift");

    public static IReadOnlyList<LootboxTypeDefinition> All { get; } = [Adventurer, Alchemist, Exorcist, Investor, Librarian, Weapon, Boss, Quest, ViewerGift, SponsorGift];
}
