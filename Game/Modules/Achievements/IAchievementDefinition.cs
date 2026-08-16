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

    /// <summary>Null when this achievement carries no lootbox -- an achievement always comes with 0 or 1, never more.</summary>
    Lootbox? Lootbox { get; }

    /// <summary>The text describing the reward for earning the achievement.</summary>
    string RewardText { get; }

    /// <summary>Registers the trigger for the achievement with the EventBus.</summary>
    /// <param name="context">The achievement trigger context.</param>
    void RegisterTrigger(AchievementTriggerContext context);
}
