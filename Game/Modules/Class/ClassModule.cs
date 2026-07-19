using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Class.Components;

namespace Game.Modules.Class;

public sealed class ClassModule : IModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000006");

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<ClassComponent>();
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No systems of its own -- see RaceModule for the same reasoning.
    }
}
