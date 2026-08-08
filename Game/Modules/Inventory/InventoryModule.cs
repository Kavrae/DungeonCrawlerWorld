using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Systems;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Inventory;

public sealed class InventoryModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000010");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private ItemCatalog _itemCatalog = null!;
    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;

    public void Configure(GameModuleContext context)
    {
        _itemCatalog = context.Items;
        _mapQuery = context.MapQuery;
        _eventBus = context.EventBus;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterDirectPool<InventoryDisabledComponent>(static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new PotionCooldownSystem(componentManager.GetPackedPool<PotionCooldownComponent>()));

        if (!componentManager.IsRegistered<HealthComponent>())
        {
            return;
        }

        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;
        var mana = componentManager.IsRegistered<ManaComponent>()
            ? componentManager.GetPackedPool<ManaComponent>()
            : null;

        systemManager.Register(new ConsumableActivationSystem(
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PotionCooldownComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _itemCatalog,
            _mapQuery,
            _eventBus,
            componentManager,
            statModifiers,
            deadEntities,
            mana));
    }
}
