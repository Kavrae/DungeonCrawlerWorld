using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Achievements.Components;
using Game.Modules.Lootboxes;

namespace Game.Modules.Achievements;

/// <summary>Grants the loot box an unlocked achievement owes, once the player closes its notification.</summary>
/// <remarks>
/// Unlocking records the box as an UnclaimedAchievementLootboxComponent instead of granting it, so a box
/// that is owed lives in game state rather than in a notification window. Claiming removes that record
/// before granting, so a second claim for the same achievement does nothing.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class AchievementLootboxClaims(ComponentManager componentManager, LootboxCatalog lootboxCatalog, EventBus eventBus)
{
    /// <summary>Grants entityId the box achievementId owes it; false when it owes none.</summary>
    public bool TryClaim(int entityId, Guid achievementId)
    {
        var unclaimedLootboxes = componentManager.GetMultiPool<UnclaimedAchievementLootboxComponent>();

        if (!unclaimedLootboxes.TryGetFirst(entityId, new AchievementMatcher(achievementId), out var unclaimedLootbox))
        {
            return false;
        }

        unclaimedLootboxes.RemoveFirst(entityId, new AchievementMatcher(achievementId));
        LootboxActions.Grant(componentManager, lootboxCatalog, eventBus, entityId, unclaimedLootbox.Reward);
        return true;
    }

    private readonly struct AchievementMatcher(Guid achievementId) : IComponentMatcher<UnclaimedAchievementLootboxComponent>
    {
        public bool Matches(in UnclaimedAchievementLootboxComponent unclaimed) => unclaimed.AchievementId == achievementId;
    }
}
