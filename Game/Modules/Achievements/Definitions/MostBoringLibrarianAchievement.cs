using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Achievement for using scrolls with the same effect 200 times.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MostBoringLibrarianAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000020");

    public string Name => "Most Boring Librarian Ever";

    public string RequirementText => "Use scrolls with the same effect 200 times.";

    public string Description =>
        "Talk about a one-track mind. I give you a whole world of options and here you are, " +
        "casting the same thing. Over. And over..... and over..... Sorry, I was starting to " +
        "fall asleep. Wait. Can I fall asleep? What would that even mean? Is that like a pc going idle ... See! Your casting " +
        "history is so boring that it has me pondering the metaphysical ramifications of sleep " +
        "on an artificial intelligence!";

    public Lootbox? Lootbox => null;

    public string RewardText =>
        "I'm just gonna go ahead and give you the spell. Now you can stop carrying around all " +
        "those copy-pasted scrolls and pick up something useful.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<ScrollMasteredEvent>(e => e.EntityId == context.PlayerQuery!.PlayerEntityId);
}
