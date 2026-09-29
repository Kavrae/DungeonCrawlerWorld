using Engine.ECS.Components;
using Game.Modules.Lootboxes;

namespace Game.Modules.Achievements;

/// <summary>Represents the definition of an achievement, including its core properties and trigger logic.</summary>
/// <cleanupVersion>1</cleanupVersion>
public interface IAchievementDefinition
{
    /// <summary>The unique identifier for the achievement.</summary>
    Guid Id { get; }

    /// <summary>The name of the achievement.</summary>
    string Name { get; }

    /// <summary>The description of the achievement.</summary>
    string Description { get; }

    /// <summary>The requirement that was fulfilled.</summary>
    string RequirementText { get; }

    /// <summary>The loot box this achievement grants, or null for none -- an achievement always comes with 0 or 1, never more.</summary>
    /// <remarks>Granted when the player closes the achievement's notification, not when it unlocks -- see AchievementLootboxClaims.</remarks>
    LootboxReward? Lootbox { get; }

    /// <summary>Explains an unusual reward, why a particular reward was given, or why nothing was -- empty for an achievement that just grants its loot box.</summary>
    /// <remarks>The notification shows a Reward line only when this isn't empty or whitespace.</remarks>
    string RewardText { get; }

    /// <summary>Registers the trigger for the achievement with the EventBus.</summary>
    /// <param name="context">The achievement trigger context.</param>
    void RegisterTrigger(AchievementTriggerContext context);

    /// <summary>
    /// Applies this achievement's mechanical reward (if any) to the entity that just earned it --
    /// called once, immediately after the achievement is recorded as unlocked. Most achievements
    /// have no mechanical reward yet (RewardText is flavor/notification text only) and rely on this
    /// default no-op rather than overriding it.
    /// </summary>
    void ApplyReward(ComponentManager componentManager, int entityId)
    {
    }
}
