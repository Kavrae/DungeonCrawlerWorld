using Game.Modules.Shops;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for giving Gold to a shop. Reward: None (temporary -- see this achievement's own request note).</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class AngelInvestorAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000018");

    public string Name => "Angel Investor";

    public string RequirementText => "Gave Gold to a shop.";

    public string Description => "You believe in this shop's business model. Or you just wanted the inventory space back.";

    public Lootbox? Lootbox => null;

    public string RewardText => "";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilTriggered<GoldGivenToShopEvent>();
}
