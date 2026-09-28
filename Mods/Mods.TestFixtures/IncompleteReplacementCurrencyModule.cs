using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Currency;

namespace Mods.TestFixtures;

/// <summary>
/// Replaces the built-in CurrencyModule by its Id but registers none of its components -- a fixture
/// for the replacement contract: GameBootstrapper's dry run must exclude it, naming the missing
/// CurrencyComponent, and keep the built-in in its place.
/// </summary>
public sealed class IncompleteReplacementCurrencyModule : IModule
{
    public Guid Id => CurrencyModule.ModuleId;

    public void RegisterComponents(ComponentManager componentManager) { }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager) { }
}
