using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.Currency.Components;

namespace Game.Modules.Currency;

public sealed class CurrencyModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000016");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    public void Configure(GameModuleContext context)
    {
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<CurrencyComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
    }
}
