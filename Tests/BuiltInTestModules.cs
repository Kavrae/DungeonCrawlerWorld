using Engine.Bootstrap;
using Engine.ECS.Context;
using Game.Bootstrap;
using Game.Modules;

namespace Tests;

/// <summary>Configures and builds every built-in module against a test's own GameModuleContext -- the game's module set, without GameBootstrapper's mods or factory.</summary>
/// <remarks>The context's FloatingTextFeed, and a World behind the context's occupancy pools, get wired as GameBootstrapper.Build does.</remarks>
internal static class BuiltInTestModules
{
    public static EcsContext Build(GameModuleContext context, int initialEntityCapacity = 100, int initialComponentCapacity = 50)
    {
        var modules = GameBootstrapper.BuiltInModules();

        foreach (var gameModule in modules.OfType<IGameModule>())
        {
            gameModule.Configure(context);
        }

        context.Definitions.ResolveAll();

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity, initialComponentCapacity, context.EventBus, entityKeys: context.EntityKeys);
        context.FloatingTextFeed.Wire(
            context.EventBus,
            ecsContext.ComponentManager.GetDirectPool<Game.Modules.ProcessingTier.Components.ProcessingTierComponent>(),
            ecsContext.ComponentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>());

        if (context.MapQuery is Game.World.World world)
        {
            TestWorlds.WireOccupancy(world, ecsContext.ComponentManager);
        }

        return ecsContext;
    }
}
