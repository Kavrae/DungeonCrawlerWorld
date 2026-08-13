using Engine.ECS.Components.Stores;
using Game.Modules.Inventory.Components;
using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded the first time the player has 5 or more Tag.Scroll items bound to hotkeys at once --
/// triggered off ItemHotkeyBoundEvent (HotbarContent.BindItem's real click-and-drag path only,
/// not PlayerBlueprint's spawn-time hardcoded binds -- see that event's own doc comment),
/// recomputing the count fresh each time rather than tracking a running total, the same
/// re-derive-from-live-state approach MinMaxerAchievement uses for its own aggregate check.
/// </summary>
public sealed class ArchivistAchievement : IAchievementDefinition
{
    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000021");

    private const int RequiredScrollCount = 5;

    public string Name => "Archivist";

    public string RequirementText => "Have 5 or more scrolls bound to hotkeys.";

    public string Description =>
        "Five scrolls, five slots, zero originality. I see you've decided that if one arcane " +
        "shortcut is good, five must be a whole toolkit -- never mind that you have no idea " +
        "what half of them actually do yet. Efficient? Maybe. Suspicious? Absolutely. I've " +
        "cross-referenced your inventory against 'someone who read the manual' and the match " +
        "is... not great.";

    /// <summary>Real reward (craft-your-own-scrolls) is blocked on a crafting system that doesn't exist yet -- same treatment as every other TODO.md-flagged future-blocked achievement.</summary>
    public LootboxReward? Lootbox => null;

    public string RewardText =>
        "Let your creative juices flow all over the page! That... sounds kinda gross. Anyway, " +
        "you can now craft your own scrolls.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<ItemHotkeyBoundEvent>(e =>
            e.EntityId == context.PlayerQuery!.PlayerEntityId && CountBoundScrolls(context) >= RequiredScrollCount);

    private static int CountBoundScrolls(AchievementTriggerContext context)
    {
        var bindings = context.ComponentManager.GetMultiPool<ItemHotkeyBindingComponent>();
        var playerEntityId = context.PlayerQuery!.PlayerEntityId;
        var count = 0;

        for (var denseIndex = bindings.GetFirstDenseIndex(playerEntityId); denseIndex != -1; denseIndex = bindings.GetNextDenseIndex(denseIndex))
        {
            var binding = bindings.GetReadonlyByDenseIndex(denseIndex);
            if (context.Items.TryGet(binding.ItemDefinitionId, out var item) && item.Tags.Contains(Tag.Scroll))
            {
                count++;
            }
        }

        return count;
    }
}
