using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Containers.Components;
using Game.Modules.Core.Components;
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
/// containers" item -- see PLAN-storage-containers.md. A stationary prop like Wall/Lava (Transform
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
public sealed class TreasureChest(MathUtility mathUtility) : IBlueprint
{
    private const string Name = "Treasure Chest";
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

    /// <summary>Built once via each item's own pure, side-effect-free Build() factory, the same set TemporaryNpcLootGrant draws from -- no ItemCatalog injection needed just to read each item's MaxStackSize.</summary>
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

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new DisplayTextComponent(Name, Description));
        componentManager.Merge(entityId, new GlyphComponent("T", Color.Gold));
        if (SpriteManifest.TryGet("Inventory", out var sprite))
        {
            componentManager.Merge(entityId, sprite);
        }
        componentManager.Merge(entityId, new TransformComponent(new Vector3Int(0, 0, (int)MapLayer.Ground), new Vector2Byte(1, 1)));
        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));
        componentManager.Merge(entityId, new ContainerComponent());
        componentManager.Merge(entityId, new CurrencyComponent(
            mathUtility.Next(MinimumStartingGold, MaximumStartingGold + 1),
            mathUtility.Next(MinimumStartingCredits, MaximumStartingCredits + 1)));

        var immunities = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
        immunities.Add(entityId, new StatusEffectImmunityComponent(StatusEffectType.Poison, remainingDurationFrames: null));
        immunities.Add(entityId, new StatusEffectImmunityComponent(StatusEffectType.Paralysis, remainingDurationFrames: null));

        var itemCount = mathUtility.Next(MinimumItemCount, MaximumItemCount + 1);
        for (var i = 0; i < itemCount; i++)
        {
            var item = LootTable[mathUtility.Next(0, LootTable.Length)];
            var maximumQuantity = System.Math.Min(MaximumStackQuantity, item.MaxStackSize ?? MaximumStackQuantity);
            var quantity = (ushort)mathUtility.Next(MinimumStackQuantity, maximumQuantity + 1);
            InventoryActions.AddItem(componentManager, entityId, item.Id, quantity);
        }
    }
}
