using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Containers.Components;
using Game.Modules.Core.Components;
using Game.Modules.Currency.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>
/// The shared shop shell -- same component set TreasureChest merges (a stationary, ContainerComponent-
/// marked prop with health/currency/immunities) minus the loot-table fill, plus no ShopComponent and
/// no stock: "a shop blueprint does not contain any items by itself." Composed as one part of a
/// CompositeBlueprint alongside a concrete stock part (PotionShopStock/GeneralShopStock) by the
/// PotionShop/GeneralShop wrapper classes, the same race+class shape GoblinEngineerBlueprint uses.
/// 1000 HP -- enough that a shop survives incidental combat splash the way a 100 HP treasure chest
/// wouldn't. Being ContainerComponent-marked, a destroyed shop gets the same "inventory wiped,
/// renamed 'Destroyed'" behavior as a chest for free via ContainerDestructionSystem.
/// </summary>
public sealed class Shop : IBlueprint
{
    private const string Name = "Shop";
    private const string Description = "A place of business.";

    private const float MaximumHealth = 1000;
    private const int StartingGold = 1000;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new DisplayTextComponent(Name, Description));
        componentManager.Merge(entityId, new GlyphComponent("S", Color.DarkBlue));
        if (SpriteManifest.TryGet("Shop-1x1", out var sprite))
        {
            componentManager.Merge(entityId, sprite);
        }
        componentManager.Merge(entityId, new TransformComponent(new Vector3Int(0, 0, (int)MapLayer.Ground), new Vector2Byte(1, 1)));
        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));
        componentManager.Merge(entityId, new ContainerComponent());
        componentManager.Merge(entityId, new CurrencyComponent(StartingGold, credits: 0));

        var immunities = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
        immunities.Add(entityId, new StatusEffectImmunityComponent(StatusEffectType.Poison, remainingDurationFrames: null));
        immunities.Add(entityId, new StatusEffectImmunityComponent(StatusEffectType.Paralysis, remainingDurationFrames: null));
    }
}
