using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded the first time the player activates an action that reads as a spell (a buff, debuff,
/// or other magic effect) rather than a mundane physical attack -- a real ActionDefinition.Tags
/// check via AchievementTriggerContext.Actions, so every Spell-tagged action qualifies
/// automatically (Heal and Magic Missile today).
/// </summary>
public sealed class SpellCasterAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000009");

    public string Name => "You're a wizard, <copyright warning>";

    public string RequirementText => "Activated your first spell.";

    public string Description =>
        "You cast your first spell! Lets hope it's not your last.";

    public LootboxReward? Lootbox => null;

    public string RewardText => "";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<ActionActivatedEvent>(activated =>
            activated.EntityId == context.PlayerQuery!.PlayerEntityId
            && context.Actions.TryGet(activated.ActionId, out var action)
            && action.Tags.Contains(Tag.Spell));
}
