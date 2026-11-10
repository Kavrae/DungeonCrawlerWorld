using Engine.Modules;
using Game.Blueprints;
using Game.Floors;
using Game.Modules.Actions;
using Game.Modules.Containers;
using Game.Modules.Core;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Death;
using Game.Modules.Inventory;
using Game.Modules.ItemPiles.Components;
using Game.Modules.ItemPiles.Systems;
using Game.Modules.Shops;

namespace Game.Modules.ItemPiles;

/// <summary>Item piles: items and currency put down on a tile, and the drops that put them there.</summary>
public sealed class ItemPilesModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000023");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } =
    [
        CoreModule.ModuleId,
        InventoryModule.ModuleId,
        CurrencyModule.ModuleId,
        ContainersModule.ModuleId,
        DeathModule.ModuleId,
        ActionsModule.ModuleId,
        ShopModule.ModuleId,
        BlueprintsModule.ModuleId,
    ];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<ItemPileComponent>(static (ref existing, incoming) => existing = incoming, initialCapacity: 16);

        componentManager.ObserveChanges<CurrencyComponent>();
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        registration.SystemManager.Register(new ItemPileEndingSystem(new ItemPileEndings(registration.ComponentManager, context.EntityManager)));

        var itemDrops = ItemDropsFor(context);
        context.ToggleItemGroundPlacement.RegisterPlacer(new ItemDropGroundPlacer(itemDrops));

        var keeping = new ItemPileKeeping(registration.ComponentManager, context.EntityManager, context.MapQuery, itemDrops);
        context.EventBus.Subscribe<NeighborhoodUnloadingEvent>(keeping.BeginKeeping);
        context.EventBus.Subscribe<NeighborhoodUnloadedEvent>(keeping.EndKeeping);
        context.EventBus.Subscribe<NeighborhoodPopulatedEvent>(keeping.Restore);
    }

    /// <summary>The drop rule over context's session.</summary>
    public static ItemDrops ItemDropsFor(GameModuleContext context) =>
        new(context.ComponentManager, context.EntityManager, context.Items, context.PlayerQuery, context.MapQuery, context.TargetResolution, context.EntityFactory, context.Definitions, context.SimulationClock);
}
