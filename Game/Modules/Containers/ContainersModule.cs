using Engine.Modules;
using Game.Modules.Containers.Components;
using Game.Modules.Containers.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Containers;

public sealed class ContainersModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000001d");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [InventoryModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<ContainerComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        systemManager.Register(new ContainerDestructionSystem(
            componentManager.GetPackedPool<ContainerComponent>(),
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            componentManager.GetPackedPool<DisplayTextComponent>(),
            context.EventBus));
    }
}
