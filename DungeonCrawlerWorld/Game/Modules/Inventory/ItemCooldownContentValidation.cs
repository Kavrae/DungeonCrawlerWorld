using Game.Modules.Actions.Activators;

namespace Game.Modules.Inventory;

/// <summary>Checks that no item declares a cooldown it could never show: one on an item whose unit is used up when it is used.</summary>
/// <remarks>
/// An item's cooldown is its unit's own (InventoryItemStackComponent.CooldownReadyAtFrame), so a potion, scroll or
/// consumable -- each used up on use -- would start a cooldown on a unit that is already gone. A wand or a toggle item
/// keeps its unit and may declare one. ProblemsOf is the same rule for one definition, for one made at runtime. Run
/// beside TargetingContentValidation, so a mod breaking it fails its dry run.
/// </remarks>
internal static class ItemCooldownContentValidation
{
    /// <exception cref="InvalidOperationException">An item used up on use declares a cooldown, naming the item.</exception>
    public static void EnsureValid(GameModuleContext context)
    {
        foreach (var item in context.Items.Definitions)
        {
            var problems = ProblemsOf(item);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(problems[0]);
            }
        }
    }

    /// <summary>The cooldown rule item breaks, as a sentence naming it; none for an item with no activator.</summary>
    public static IReadOnlyList<string> ProblemsOf(ItemDefinition item) =>
        item.Activator is PotionActivator or ScrollActivator or ConsumableActivator && item.Activator.Timing.CooldownFrames is { } cooldownFrames
            ? [$"Item '{item.Name}' declares a cooldown ({cooldownFrames} frames) but is used up when it is used: its cooldown belongs to the used unit, which is gone. Leave the cooldown out."]
            : [];
}
