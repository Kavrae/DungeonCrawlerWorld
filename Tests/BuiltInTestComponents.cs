using Engine.ECS.Components;
using Game.Bootstrap;

namespace Tests;

/// <summary>Registers every component the game's built-in modules register, for a test that builds its own ComponentManager.</summary>
internal static class BuiltInTestComponents
{
    public static ComponentManager RegisterAll(ComponentManager componentManager)
    {
        foreach (var module in GameBootstrapper.BuiltInModules())
        {
            module.RegisterComponents(componentManager);
        }

        return componentManager;
    }
}
