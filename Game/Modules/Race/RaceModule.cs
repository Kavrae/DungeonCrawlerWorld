using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Race.Components;

namespace Game.Modules.Race;

public sealed class RaceModule : IModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000005");

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<RaceComponent>();
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No systems of its own -- Race is narrative/display data today, consulted by
        // other systems rather than driving its own per-frame behavior.
    }
}
