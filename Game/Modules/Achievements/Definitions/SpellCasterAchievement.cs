using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for activating your first spell.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SpellCasterAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000009");

    public string Name => "You're a wizard, <copyright warning>";

    public string RequirementText => "Activated your first spell.";

    public string Description => "You cast your first spell! Lets hope it's not your last.";

    public Lootbox? Lootbox => null;

    public string RewardText => "";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<ActionActivatedEvent>(activated =>
            activated.EntityId == context.PlayerQuery!.PlayerEntityId
            && context.Actions.TryGet(activated.ActionId, out var action)
            && action.Tags.Contains(Tag.Spell));
}
