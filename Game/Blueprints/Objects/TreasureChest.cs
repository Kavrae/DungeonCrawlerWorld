using Game.Modules.Containers.Components;
using Game.Modules.Currency.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Definitions;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>
/// A lootable storage container, the concrete implementation of TODO.md's "Shops and storage
/// containers" item. A stationary prop (Transform
/// + object-specific components, no creature identity), but marked ContainerComponent so it's
/// lootable via the map's "Loot" context menu option even while alive, unlike a corpse. 100
/// starting health -- high enough that a stray AOE hit won't randomly destroy one. Immune to
/// Poison and Paralysis (permanent StatusEffectImmunityComponent grants, the same mechanism every
/// status effect's own ApplyStack already checks) but not to Burning, so it can still be destroyed
/// by fire. Starts with 1-10 random items (stack sizes 1-5, see LootTable) and 0-5 Gold, 0-1
/// Credits -- a much smaller Gold roll than a creature's own 1-10 starting Gold
/// (StartingCurrencyGrant), since a chest's Gold is found loot, not a personal purse. If destroyed, ContainerDestructionSystem
/// clears its inventory and renames it "Destroyed" -- see that system's own doc comment.
/// </summary>
public static class TreasureChest
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000102");

    public const string Name = "Treasure Chest";

    private const string Description = "A sturdy chest that might hold treasure.";

    private const float MaximumHealth = 100;

    private const int MinimumItemCount = 1;
    private const int MaximumItemCount = 10;
    private const int MinimumStackQuantity = 1;
    private const int MaximumStackQuantity = 5;

    private const int MinimumStartingGold = 0;
    private const int MaximumStartingGold = 5;
    private const int MinimumStartingCredits = 0;
    private const int MaximumStartingCredits = 1;

    /// <summary>Built once via each item's own pure, side-effect-free Build() factory, the same set TemporaryNpcLootGrant draws from -- no ItemCatalog injection needed just to read each item's Id.</summary>
    private static readonly ItemDefinition[] LootTable =
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

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        Appearance = new() { Name = Name, Description = Description, Glyph = "T", GlyphColor = Color.Gold, SpriteName = "Inventory" }
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));
        componentManager.Merge(entityId, new ContainerComponent());
        componentManager.Merge(entityId, new CurrencyComponent(
            context.Rolls.Next(MinimumStartingGold, MaximumStartingGold + 1),
            context.Rolls.Next(MinimumStartingCredits, MaximumStartingCredits + 1)));

        var immunities = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
        StatusEffectImmunityEffects.GrantPermanent(immunities, entityId, StatusEffectType.Poison);
        StatusEffectImmunityEffects.GrantPermanent(immunities, entityId, StatusEffectType.Paralysis);

        var itemCount = context.Rolls.Next(MinimumItemCount, MaximumItemCount + 1);
        for (var i = 0; i < itemCount; i++)
        {
            var item = LootTable[context.Rolls.Next(0, LootTable.Length)];
            var maximumQuantity = Math.Min(MaximumStackQuantity, (int)InventoryActions.GetEffectiveMaxStackSize(componentManager, entityId));
            var quantity = (ushort)context.Rolls.Next(MinimumStackQuantity, maximumQuantity + 1);
            InventoryActions.AddItem(componentManager, entityId, item.Id, quantity);
        }
    }
}
