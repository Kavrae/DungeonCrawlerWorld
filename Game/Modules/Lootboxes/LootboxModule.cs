using Engine.Modules;
using Game.Blueprints;
using Game.Spawning;
using Game.World;

namespace Game.Modules.Lootboxes;

/// <summary>Registers the built-in loot box types into GameModuleContext.Lootboxes, and awards a slain boss's box to the player who killed it.</summary>
/// <remarks>A box is an ordinary inventory item whose definition LootboxCatalog creates the first time its type and rarity is granted, so the module has no components or systems of its own -- only BossLootboxAwarder's EntityDiedEvent subscription.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LootboxModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000022");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [BlueprintsModule.ModuleId];

    public void Configure(GameModuleContext context)
    {
        foreach (var type in LootboxTypes.All)
        {
            context.Lootboxes.Register(type);
        }
    }

    public void RegisterComponents(ComponentRegistration registration)
    {
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var componentManager = registration.ComponentManager;

        var bossLootboxAwarder = new BossLootboxAwarder(
            componentManager,
            context.Lootboxes,
            context.EventBus,
            context.PlayerQuery,
            context.Definitions,
            componentManager.GetDirectPool<SpawnRecordComponent>(),
            componentManager.GetMultiPool<AppliedBlueprintComponent>());
        context.EventBus.Subscribe<EntityDiedEvent>(bossLootboxAwarder.OnEntityDied);
    }
}
