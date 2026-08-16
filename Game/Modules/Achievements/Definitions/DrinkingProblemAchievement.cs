using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for drinking a potion while the cooldown is still active.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DrinkingProblemAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000008");

    public string Name => "Drinking Problem";

    public string RequirementText => "Drank a potion while your potion cooldown was still active.";

    public string Description =>
        "All you had to do was wait a few more seconds. But nooooo you just had to have one. More. Drink. And now you have to suffer for it.";

    /// <summary>Intended contents: 5 Health Potions, matching RewardText -- not delivered yet, see TODO.md's "Achievement lootbox delivery" entry.</summary>
    public Lootbox? Lootbox => new(LootboxRarity.Bronze, "Potion");

    public string RewardText => "If you want to drink so badly, here, have some more.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<PotionCooldownAbusedEvent>(potionCooldownAbusedEvent => potionCooldownAbusedEvent.EntityId == context.PlayerQuery!.PlayerEntityId);
}
