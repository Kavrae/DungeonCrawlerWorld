using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;

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

    /// <summary>The kind ItemCatalog.GetOrCreateDerived keeps each fully charged wand under; its variant is ChargedWandVariant(the wand's index in AllCoreItemIds, its charges).</summary>
    private const string ChargedWandKind = "TemporaryNpcLootGrant.ChargedWand";

    /// <summary>The items an NPC can be granted, by their BuiltInItemIds Guid.</summary>
    private static readonly Guid[] AllCoreItemIds =
    [
        BuiltInItemIds.HealthPotion,
        BuiltInItemIds.ManaPotion,
        BuiltInItemIds.HotkeyExpansionPotion,
        BuiltInItemIds.DamagePotion,
        BuiltInItemIds.ToxicPotion,
        BuiltInItemIds.ToxicIdol,
        BuiltInItemIds.ScrollOfHealing,
        BuiltInItemIds.ScrollOfTorch,
        BuiltInItemIds.WandOfFireball,
    ];

    /// <summary>
    /// Rolls 0-20 stacks of a randomly selected item each, quantity 1-entityId's own effective max
    /// stack size (see InventoryActions.GetEffectiveMaxStackSize). AddItem merges same-item rolls
    /// into one stack rather than stacking distinct entries, so the actual resulting distinct-stack
    /// count usually lands well under the roll with only 9 possible items to pick from --
    /// acceptable for throwaway test content. A rolled wand instead gets a random MaxCharges (1-20)
    /// with Charges set to match (full charge), via AddItemWithOverride -- the same "freshly
    /// granted identical batch, not yet divergent" primitive WandGrantEffects.Grant itself uses,
    /// just with a flat random MaxCharges instead of one scaled off Intelligence. Every NPC granted
    /// the same roll shares one charged definition (ItemCatalog.GetOrCreateDerived) instead of
    /// building its own.
    /// </summary>
    public static void GrantRandomStartingLoot(ComponentManager componentManager, int entityId, MathUtility mathUtility, ItemCatalog itemCatalog)
    {
        var stackCount = mathUtility.Next(0, MaxStackCount + 1);
        for (var i = 0; i < stackCount; i++)
        {
            var itemIndex = mathUtility.Next(0, AllCoreItemIds.Length);
            var itemId = AllCoreItemIds[itemIndex];
            var quantity = (ushort)mathUtility.Next(1, InventoryActions.GetEffectiveMaxStackSize(componentManager, entityId) + 1);

            if (itemCatalog.Get(itemId).Activator is WandActivator)
            {
                var maxCharges = mathUtility.Next(MinWandMaxCharges, MaxWandMaxCharges + 1);
                var chargedWand = itemCatalog.GetOrCreateDerived(ChargedWandKind, ChargedWandVariant(itemIndex, maxCharges), static (catalog, variant) => ChargedWand(catalog, variant));
                InventoryActions.AddItemWithOverride(componentManager, entityId, chargedWand, quantity);
            }
            else
            {
                InventoryActions.AddItem(componentManager, entityId, itemId, quantity);
            }
        }
    }

    private static int ChargedWandVariant(int itemIndex, int charges) => itemIndex * (MaxWandMaxCharges + 1) + charges;

    private static ItemDefinition ChargedWand(ItemCatalog itemCatalog, int variant)
    {
        var wand = itemCatalog.Get(AllCoreItemIds[variant / (MaxWandMaxCharges + 1)]);
        var charges = (ushort)(variant % (MaxWandMaxCharges + 1));
        return wand with { Activator = ((WandActivator)wand.Activator!) with { Charges = charges, MaxCharges = charges } };
    }
}
