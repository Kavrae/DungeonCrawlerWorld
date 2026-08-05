using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

public sealed class InventoryModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000010");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    public void Configure(GameModuleContext context)
    {
        // No runtime context needed -- see class doc comment.
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterDirectPool<InventoryDisabledComponent>(static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No systems of its own -- this pass is storage + viewing only, nothing ticks per-frame.
    }
}
