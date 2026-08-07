using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded the first time the player activates an ability that reads as a spell (a buff, debuff,
/// or other magic effect) rather than a mundane physical attack -- a real AbilityDefinition.Tags
/// check via AchievementTriggerContext.Abilities, so every Spell-tagged ability qualifies
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
        context.SubscribeUntilUnlocked<AbilityActivatedEvent>(activated =>
            activated.EntityId == context.PlayerQuery!.PlayerEntityId
            && context.Abilities.TryGet(activated.AbilityId, out var ability)
            && ability.Tags.Contains(Tag.Spell));
}
