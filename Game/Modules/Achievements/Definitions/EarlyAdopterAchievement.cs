using Game.Modules.Crawler.Components;
using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Rewarded for entering the dungeon as one of the first 5,000 Crawlers.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EarlyAdopterAchievement : IAchievementDefinition
{
    private const int MaxQualifyingCrawlerNumber = 5000;

    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000004");

    public string Name => "Early Adopter";

    public string RequirementText => "Entered a new World Dungeon as one of the first 5,000 Crawlers.";

    public string Description => "You are one of the first 5,000 Crawlers to enter a new World Dungeon. Sucker.";

    public Lootbox? Lootbox => new(LootboxRarity.Silver, "Adventurer");

    public string RewardText => "";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilTriggered<EnteredDungeonEvent>(_ =>
            context.ComponentManager.GetPackedPool<CrawlerComponent>().TryGetReadonly(context.PlayerQuery!.PlayerEntityId, out var crawler)
            && crawler.CrawlerNumber <= MaxQualifyingCrawlerNumber);
}
