using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops.Components;

namespace Game.Modules.Shops;

/// <summary>Registers ShopComponent and ShopStockPreferenceComponent (see ShopStockPricing) -- no systems of its own; a shop's destruction (inventory wiped, renamed "Destroyed") is already handled generically by ContainerDestructionSystem via the ContainerComponent every Shop blueprint also merges.</summary>
public sealed class ShopModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000018");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(InventoryModule), typeof(CurrencyModule)];

    public void Configure(GameModuleContext context)
    {
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<ShopComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ShopStockPreferenceComponent>();
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
    }
}
