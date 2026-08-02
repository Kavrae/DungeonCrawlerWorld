using Game.Modules.Crawler.Components;
using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded for entering the dungeon as one of the first 5,000 Crawlers. EnteredDungeon carries
/// no data (see its own doc comment), so the condition reads CrawlerComponent off the player
/// entity directly via AchievementTriggerContext.ComponentManager -- safe because
/// FloorBuilder.CreatePlayer (and PlayerBlueprint.Build within it) always runs before GameLoop
/// publishes EnteredDungeon, so the player's CrawlerComponent already exists by the time this
/// fires. A player entity always has CrawlerComponent (PlayerBlueprint merges it unconditionally),
/// so TryGetReadonly failing here would indicate a real bug, not an expected case.
/// </summary>
public sealed class EarlyAdopterAchievement : IAchievementDefinition
{
    private const int MaxQualifyingCrawlerNumber = 5000;

    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000004");

    public string Name => "Early Adopter";

    public string RequirementText => "Entered a new World Dungeon as one of the first 5,000 Crawlers.";

    public string Description =>
        "You are one of the first 5,000 Crawlers to enter a new World Dungeon. Sucker.";

    public LootboxReward? Lootbox => new(LootboxRarity.Silver, "Adventurer");

    public string RewardText => "";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<EnteredDungeon>(_ =>
            context.ComponentManager.GetPackedPool<CrawlerComponent>().TryGetReadonly(context.PlayerQuery!.PlayerEntityId, out var crawler)
            && crawler.CrawlerNumber <= MaxQualifyingCrawlerNumber);
}
