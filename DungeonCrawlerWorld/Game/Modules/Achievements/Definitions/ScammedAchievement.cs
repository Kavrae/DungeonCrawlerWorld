using Game.Modules.Lootboxes;
using Game.Modules.Shops;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for completing a trade with a shop where the player got something, but less than they gave.</summary>
/// <remarks>Getting nothing back is Angel Investor's, so the two never unlock from the same deal.</remarks>
public sealed class ScammedAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000023");

    public string Name => "Scammed";

    public string RequirementText => "Traded with a shop and got back less than you gave.";

    public string Description => "The shopkeeper seems oddly happy about this trade...";

    public LootboxReward? Lootbox => new(LootboxTypes.Investor.Id, LootboxRarity.Bronze);

    public string RewardText => "";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<ShopDealCompletedEvent>(deal => !deal.ShopOfferIsEmpty && deal.PlayerOfferValue > deal.ShopOfferValue);
}
