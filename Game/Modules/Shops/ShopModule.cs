using Engine.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops.Components;

namespace Game.Modules.Shops;

/// <summary>Registers ShopComponent and ShopStockPreferenceComponent (see ShopStockPricing) -- no systems of its own; a shop's destruction (inventory wiped, renamed "Destroyed") is already handled generically by ContainerDestructionSystem via the ContainerComponent every Shop blueprint also merges.</summary>
public sealed class ShopModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000001e");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [InventoryModule.ModuleId, CurrencyModule.ModuleId, AbilityScoresModule.ModuleId];

    public void Configure(GameModuleContext context)
    {
    }

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<ShopComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ShopStockPreferenceComponent>();
    }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {
    }
}
