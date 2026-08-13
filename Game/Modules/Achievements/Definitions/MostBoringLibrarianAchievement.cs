using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded the first time any scroll's SpellId crosses ScrollMasteryEffects.MasteryThreshold
/// uses -- purely observational (no counter of its own; ScrollMasteryComponent already tracks
/// the count). Unlike every other achievement's Lootbox, the "reward" here isn't delivered by
/// this achievement at all: ScrollMasteryEffects.RecordUsage already granted the spell by the
/// time ScrollMasteredEvent fires, so this carries flavor text only.
/// </summary>
public sealed class MostBoringLibrarianAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000020");

    public string Name => "Most Boring Librarian Ever";

    public string RequirementText => "Use scrolls with the same effect 200 times.";

    public string Description =>
        "Talk about a one-track mind. I give you a whole world of options and here you are, " +
        "casting the same thing. Over. And over..... and over..... Sorry, I was starting to " +
        "fall asleep. Wait. Can I fall asleep? What would that even mean? ... See! Your casting " +
        "history is so boring that it has me pondering the metaphysical ramifications of sleep " +
        "on an artificial intelligence!";

    public LootboxReward? Lootbox => null;

    public string RewardText =>
        "I'm just gonna go ahead and give you the spell. Now you can stop carrying around all " +
        "those copy-pasted scrolls and carry something useful.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<ScrollMasteredEvent>(e => e.EntityId == context.PlayerQuery!.PlayerEntityId);
}
