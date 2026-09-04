using Engine.ECS.Components;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Rewarded for holding 999 or more of the same item in a single inventory stack. Unlike every
/// other achievement here, "does the player currently have a big enough stack" is a standing state,
/// not a discrete occurrence -- there's no event to Subscribe to (InventoryActions.AddItem, the
/// stack-growing chokepoint, has no EventBus to publish through -- see MaxStackSizeComponent's own
/// doc comment for why the reward below reads that state back out instead), so this is the first
/// achievement to use AchievementTriggerContext.SubscribePolled instead of a Subscribe* call.
/// </summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ObsessiveCollectorAchievement : IAchievementDefinition
{
    private const ushort RequiredQuantity = 999;
    private const ushort RewardMaxStackSize = 1000;

    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000022");

    public string Name => "Obsessive Collector";

    public string RequirementText => "Held 999 of the same item at once.";

    public string Description => "Just... one... more.";

    public Lootbox? Lootbox => null;

    public string RewardText => "Your inventory grudgingly makes room for one more -- every item's stack cap is now 1000.";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribePolled(() => HasQualifyingStack(context));

    /// <summary>Every future stack cap check (InventoryActions.AddItem/AddItemWithOverride/AddDivergentItem, via GetEffectiveMaxStackSize) reads this directly instead of InventoryActions.DefaultMaxStackSize -- see MaxStackSizeComponent's own doc comment.</summary>
    public void ApplyReward(ComponentManager componentManager, int entityId) =>
        componentManager.Merge(entityId, new MaxStackSizeComponent(RewardMaxStackSize));

    private static bool HasQualifyingStack(AchievementTriggerContext context)
    {
        if (context.PlayerQuery is not { } playerQuery)
        {
            return false;
        }

        var stacks = context.ComponentManager.GetMultiPool<InventoryItemStackComponent>();
        var playerEntityId = playerQuery.PlayerEntityId;

        for (var denseIndex = stacks.GetFirstDenseIndex(playerEntityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            if (stacks.GetReadonlyByDenseIndex(denseIndex).Quantity >= RequiredQuantity)
            {
                return true;
            }
        }

        return false;
    }
}
