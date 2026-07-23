using Engine.ECS.Context;
using Engine.Events;
using Engine.Modules;

namespace Engine.Bootstrap;

/// <summary>
/// Validates and topologically sorts a set of modules by their declared dependencies,
/// then registers all components before any systems (a system may need a component pool
/// owned by a different module) and produces the finished <see cref="EcsContext"/>.
/// </summary>
public static class Bootstrapper
{
    /// <param name="eventBus">
    /// Shared EventBus for the built EcsContext to expose. Optional and defaults to
    /// constructing a private one, exactly as this method already does internally for
    /// ComponentManager/SystemManager -- only a caller that needs the same EventBus instance
    /// available before this call returns (e.g. to run a module's Configure step first) needs
    /// to pass one explicitly. Passing null changes nothing for any existing caller.
    /// </param>
    public static EcsContext Build(IReadOnlyList<IModule> modules, int initialEntityCapacity, int initialComponentCapacity, EventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var sortedModules = TopologicalSort(modules);

        var builder = new EcsContextBuilder(initialEntityCapacity, initialComponentCapacity, eventBus);

        RegisterAllComponents(sortedModules, builder);
        RegisterAllSystems(sortedModules, builder);

        return builder.Build();
    }

    private static void RegisterAllComponents(IReadOnlyList<IModule> sortedModules, EcsContextBuilder builder)
    {
        foreach (var module in sortedModules)
        {
            module.RegisterComponents(builder.ComponentManager);
        }
    }

    private static void RegisterAllSystems(IReadOnlyList<IModule> sortedModules, EcsContextBuilder builder)
    {
        foreach (var module in sortedModules)
        {
            module.RegisterSystems(builder.SystemManager, builder.ComponentManager);
        }
    }

    private static List<IModule> TopologicalSort(IReadOnlyList<IModule> modules)
    {
        var modulesByType = new Dictionary<Type, IModule>();
        foreach (var module in modules)
        {
            if (!modulesByType.TryAdd(module.GetType(), module))
            {
                throw new InvalidOperationException($"Module type {module.GetType().Name} is registered more than once.");
            }
        }

        var topologicallySortedModules = new List<IModule>(modules.Count);
        var visitStatesByModuleType = new Dictionary<Type, VisitState>();

        foreach (var module in modules)
        {
            Visit(module, modulesByType, visitStatesByModuleType, topologicallySortedModules);
        }

        return topologicallySortedModules;
    }

    private enum VisitState
    {
        Visiting,
        Visited,
    }

    private static void Visit(
        IModule module,
        Dictionary<Type, IModule> modulesByType,
        Dictionary<Type, VisitState> visitStatesByModuleType,
        List<IModule> topologicallySortedModules)
    {
        var moduleType = module.GetType();

        if (visitStatesByModuleType.TryGetValue(moduleType, out var visitState))
        {
            if (visitState == VisitState.Visiting)
            {
                throw new InvalidOperationException($"Circular module dependency detected involving {module.Name}.");
            }

            return;
        }

        visitStatesByModuleType[moduleType] = VisitState.Visiting;

        foreach (var dependencyType in module.Dependencies)
        {
            if (!modulesByType.TryGetValue(dependencyType, out var dependencyModule))
            {
                throw new InvalidOperationException($"Module {module.Name} requires {dependencyType.Name}, which is not registered.");
            }

            Visit(dependencyModule, modulesByType, visitStatesByModuleType, topologicallySortedModules);
        }

        visitStatesByModuleType[moduleType] = VisitState.Visited;
        topologicallySortedModules.Add(module);
    }
}