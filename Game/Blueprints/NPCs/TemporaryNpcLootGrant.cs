using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Definitions;

namespace Game.Blueprints.NPCs;

/// <summary>
/// TEMPORARY: no real loot table exists yet, so NPCs get a random starting inventory instead so
/// corpses actually have something in them to loot -- replace with a real loot table once one
/// exists (see TODO.md's Corpse looting entry), at which point this whole file goes away.
/// Generalizes the ad-hoc random-potion-count precedent Goblin.Build used to have inline.
/// </summary>
public static class TemporaryNpcLootGrant
{
    private const int MaxStackCount = 20;

    /// <summary>Flat random range, deliberately not Intelligence-scaled the way WandGrantEffects.Grant's real grant path is -- this is throwaway random loot, not a stat-driven grant.</summary>
    private const int MinWandMaxCharges = 1;
    private const int MaxWandMaxCharges = 20;

    /// <summary>
    /// Built once via each item's own pure, side-effect-free Build() factory -- the same one
    /// CoreItemsModule.Configure calls to populate ItemCatalog -- so this needs no ItemCatalog
    /// injection just to read each item's MaxStackSize.
    /// </summary>
    private static readonly ItemDefinition[] AllCoreItems =
    [
        HealthPotion.Build(),
        ManaPotion.Build(),
        HotkeyExpansionPotion.Build(),
        DamagePotion.Build(),
        ToxicPotion.Build(),
        ToxicIdol.Build(),
        ScrollOfHealing.Build(),
        ScrollOfTorch.Build(),
        WandOfFireball.Build(),
    ];

    /// <summary>
    /// Rolls 0-20 stacks of a randomly selected item each, quantity 1-MaxStackSize. AddItem merges
    /// same-item rolls into one stack rather than stacking distinct entries, so the actual
    /// resulting distinct-stack count usually lands well under the roll with only 9 possible items
    /// to pick from -- acceptable for throwaway test content. A rolled wand instead gets a random
    /// MaxCharges (1-20) with Charges set to match (full charge), via AddItemWithOverride -- the
    /// same "freshly granted identical batch, not yet divergent" primitive WandGrantEffects.Grant
    /// itself uses, just with a flat random MaxCharges instead of one scaled off Intelligence.
    /// </summary>
    public static void GrantRandomStartingLoot(ComponentManager componentManager, int entityId, MathUtility mathUtility)
    {
        var stackCount = mathUtility.Next(0, MaxStackCount + 1);
        for (var i = 0; i < stackCount; i++)
        {
            var item = AllCoreItems[mathUtility.Next(0, AllCoreItems.Length)];
            var quantity = (ushort)mathUtility.Next(1, (item.MaxStackSize ?? MaxStackCount) + 1);

            if (item.Activator is WandActivator wandActivator)
            {
                var maxCharges = (ushort)mathUtility.Next(MinWandMaxCharges, MaxWandMaxCharges + 1);
                var grantedDefinition = item with { Activator = wandActivator with { Charges = maxCharges, MaxCharges = maxCharges } };
                InventoryActions.AddItemWithOverride(componentManager, entityId, grantedDefinition, quantity);
            }
            else
            {
                InventoryActions.AddItem(componentManager, entityId, item.Id, quantity);
            }
        }
    }
}
