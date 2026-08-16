using Game.Modules.Inventory.Components;
using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>Rewarded for binding 5 or more different scrolls (by item definition id) to hotkeys.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ArchivistAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000021");

    private const int RequiredScrollCount = 5;

    public string Name => "Archivist";

    public string RequirementText => "Have 5 or more different scrolls bound to hotkeys.";

    public string Description =>
        "I see you've decided that if one arcane shortcut is good, five must be a whole toolkit. Efficient? Maybe. Suspicious? Absolutely. " +
        "Just don't get too close to any fire elementals.";

    /// <summary>Real reward (craft-your-own-scrolls) is blocked on a crafting system that doesn't exist yet -- same treatment as every other TODO.md-flagged future-blocked achievement.</summary>
    public Lootbox? Lootbox => null;

    public string RewardText =>
        "Let your creative juices flow all over the page! That... sounds kinda gross. Anyway, you can now craft your own scrolls.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<ItemHotkeyBoundEvent>(e =>
            e.EntityId == context.PlayerQuery!.PlayerEntityId && CountDistinctBoundScrolls(context) >= RequiredScrollCount);

    private static int CountDistinctBoundScrolls(AchievementTriggerContext context)
    {
        var bindings = context.ComponentManager.GetMultiPool<ItemHotkeyBindingComponent>();
        var playerEntityId = context.PlayerQuery!.PlayerEntityId;
        var distinctScrollIds = new HashSet<Guid>();

        for (var denseIndex = bindings.GetFirstDenseIndex(playerEntityId); denseIndex != -1; denseIndex = bindings.GetNextDenseIndex(denseIndex))
        {
            var binding = bindings.GetReadonlyByDenseIndex(denseIndex);
            if (context.Items.TryGet(binding.ItemDefinitionId, out var item) && item.Tags.Contains(Tag.Scroll))
            {
                distinctScrollIds.Add(binding.ItemDefinitionId);
            }
        }

        return distinctScrollIds.Count;
    }
}
