using Game.Modules.Containers.Components;
using Game.Modules.Currency.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>
/// The shared shop shell -- same component set TreasureChest merges (a stationary, ContainerComponent-
/// marked prop with health/currency/immunities) minus the loot-table fill, plus no ShopComponent and
/// no stock: "a shop blueprint does not contain any items by itself." Included, ahead of a concrete
/// stock part (PotionShopStock/GeneralShopStock), by the PotionShop/GeneralShop definitions.
/// 1000 HP -- enough that a shop survives incidental combat splash the way a 100 HP treasure chest
/// wouldn't. Being ContainerComponent-marked, a destroyed shop gets the same "inventory wiped,
/// renamed 'Destroyed'" behavior as a chest for free via ContainerDestructionSystem.
/// </summary>
public static class Shop
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000111");

    public const string Name = "Shop";

    private const string Description = "A place of business.";

    private const float MaximumHealth = 1000;
    private const int StartingGold = 1000;

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        Appearance = new() { Name = Name, Description = Description, Glyph = "S", GlyphColor = Color.DarkBlue, SpriteName = "Shop-1x1" }
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));
        componentManager.Merge(entityId, new ContainerComponent());
        componentManager.Merge(entityId, new CurrencyComponent(StartingGold, credits: 0));

        var immunities = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
        StatusEffectImmunityEffects.GrantPermanent(immunities, entityId, StatusEffectType.Poison);
        StatusEffectImmunityEffects.GrantPermanent(immunities, entityId, StatusEffectType.Paralysis);
    }
}
