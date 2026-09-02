using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Containers.Components;
using Game.Modules.Containers.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Containers;

public sealed class ContainersModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000017");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(InventoryModule)];

    private EventBus _eventBus = null!;

    public void Configure(GameModuleContext context) => _eventBus = context.EventBus;

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<ContainerComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new ContainerDestructionSystem(
            componentManager.GetPackedPool<ContainerComponent>(),
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            componentManager.GetDirectPool<DisplayTextComponent>(),
            _eventBus));
    }
}
