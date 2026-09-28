using Engine.Diagnostics;
using Engine.ECS.Context;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Modules;

namespace Engine.Bootstrap;

/// <summary> Registers built-in and modded modules by their components and systems. </summary>
/// <remarks>
/// Checks every module's Requires and sorts the set by RunsAfter/RunsBefore, then registers all
/// components before any systems (a system may need a component pool owned by a different module)
/// and produces the finished <see cref="EcsContext"/>.
///
/// Throws, registering nothing, on a missing requirement, a circular ordering, or two modules sharing a type or a non-empty Id.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class Bootstrapper
{
    public static EcsContext Build(IReadOnlyList<IModule> modules, int initialEntityCapacity, int initialComponentCapacity, EventBus eventBus, StartupProfiler? startupProfiler = null, EntityKeys? entityKeys = null)
    {
        var sortedModules = TopologicalSort(modules);

        var builder = new EcsContextBuilder(initialEntityCapacity, initialComponentCapacity, eventBus, entityKeys);

        RegisterAllComponents(sortedModules, builder, startupProfiler);
        RegisterAllSystems(sortedModules, builder, startupProfiler);

        return builder.Build();
    }

    private static void RegisterAllComponents(IReadOnlyList<IModule> sortedModules, EcsContextBuilder builder, StartupProfiler? startupProfiler)
    {
        foreach (var module in sortedModules)
        {
            using var _ = startupProfiler?.Phase($"RegisterComponents:{module.Name}");
            module.RegisterComponents(builder.ComponentManager);
        }
    }

    private static void RegisterAllSystems(IReadOnlyList<IModule> sortedModules, EcsContextBuilder builder, StartupProfiler? startupProfiler)
    {
        foreach (var module in sortedModules)
        {
            using var _ = startupProfiler?.Phase($"RegisterSystems:{module.Name}");
            module.RegisterSystems(builder.SystemManager, builder.ComponentManager);
        }
    }

    /// <summary>Orders modules so every RunsAfter/RunsBefore constraint holds, keeping the caller's order wherever nothing constrains it.</summary>
    /// <remarks>Depth-first in input order, visiting a module's predecessors (in input order) before it, so a list that already satisfies every constraint comes back unchanged.</remarks>
    private static List<IModule> TopologicalSort(IReadOnlyList<IModule> modules)
    {
        var modulesById = IndexById(modules);
        ValidateRequirements(modules, modulesById);

        var predecessorsByModule = FindPredecessors(modules, modulesById);
        var sortedModules = new List<IModule>(modules.Count);
        var visitStatesByModule = new Dictionary<IModule, VisitState>(ReferenceEqualityComparer.Instance);
        var visitPath = new List<(IModule Module, string? ReachedBy)>();

        foreach (var module in modules)
        {
            Visit(module, reachedBy: null, predecessorsByModule, visitStatesByModule, visitPath, sortedModules);
        }

        return sortedModules;
    }

    /// <summary>Every module with a non-empty Id, by that Id.</summary>
    /// <remarks>Guid.Empty is no identity at all, so any number of such modules coexist and none can be named by another.</remarks>
    private static Dictionary<Guid, IModule> IndexById(IReadOnlyList<IModule> modules)
    {
        var moduleTypes = new HashSet<Type>();
        var modulesById = new Dictionary<Guid, IModule>();

        foreach (var module in modules)
        {
            if (!moduleTypes.Add(module.GetType()))
            {
                throw new InvalidOperationException($"Module type {module.GetType().Name} is registered more than once.");
            }

            if (module.Id != Guid.Empty && !modulesById.TryAdd(module.Id, module))
            {
                throw new InvalidOperationException($"Modules {modulesById[module.Id].Name} and {module.Name} share the Id {module.Id}.");
            }
        }

        return modulesById;
    }

    private static void ValidateRequirements(IReadOnlyList<IModule> modules, Dictionary<Guid, IModule> modulesById)
    {
        var missingRequirements = new List<string>();

        foreach (var module in modules)
        {
            foreach (var requiredId in module.Requires)
            {
                if (!modulesById.ContainsKey(requiredId))
                {
                    missingRequirements.Add($"{module.Name} requires module {requiredId}, which is not in the module set.");
                }
            }
        }

        if (missingRequirements.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, missingRequirements));
        }
    }

    /// <summary>One module that must run before another, and the declaration that says so -- "Movement.RunsAfter" or "NpcBehavior.RunsBefore" -- for the cycle message.</summary>
    private readonly record struct OrderingEdge(IModule Predecessor, string DeclaredBy);

    /// <summary>For each module, the modules that must run before it -- its RunsAfter targets and every module naming it in RunsBefore -- in input order.</summary>
    private static Dictionary<IModule, List<OrderingEdge>> FindPredecessors(IReadOnlyList<IModule> modules, Dictionary<Guid, IModule> modulesById)
    {
        var predecessorsByModule = new Dictionary<IModule, List<OrderingEdge>>(ReferenceEqualityComparer.Instance);
        foreach (var module in modules)
        {
            predecessorsByModule[module] = [];
        }

        foreach (var module in modules)
        {
            foreach (var runsAfterId in module.RunsAfter)
            {
                if (modulesById.TryGetValue(runsAfterId, out var predecessor) && !ReferenceEquals(predecessor, module))
                {
                    predecessorsByModule[module].Add(new OrderingEdge(predecessor, $"{module.Name}.{nameof(IModule.RunsAfter)}"));
                }
            }

            foreach (var runsBeforeId in module.RunsBefore)
            {
                if (modulesById.TryGetValue(runsBeforeId, out var successor) && !ReferenceEquals(successor, module))
                {
                    predecessorsByModule[successor].Add(new OrderingEdge(module, $"{module.Name}.{nameof(IModule.RunsBefore)}"));
                }
            }
        }

        var inputIndexByModule = new Dictionary<IModule, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < modules.Count; index++)
        {
            inputIndexByModule[modules[index]] = index;
        }

        foreach (var predecessors in predecessorsByModule.Values)
        {
            predecessors.Sort((left, right) => inputIndexByModule[left.Predecessor].CompareTo(inputIndexByModule[right.Predecessor]));
        }

        return predecessorsByModule;
    }

    private enum VisitState
    {
        Visiting,
        Visited,
    }

    /// <param name="reachedBy">The declaration of the edge that led here from the previous module on visitPath; null for a module the sort starts from.</param>
    private static void Visit(
        IModule module,
        string? reachedBy,
        Dictionary<IModule, List<OrderingEdge>> predecessorsByModule,
        Dictionary<IModule, VisitState> visitStatesByModule,
        List<(IModule Module, string? ReachedBy)> visitPath,
        List<IModule> sortedModules)
    {
        if (visitStatesByModule.TryGetValue(module, out var visitState))
        {
            if (visitState == VisitState.Visiting)
            {
                throw new InvalidOperationException(DescribeCycle(visitPath, module, reachedBy));
            }

            return;
        }

        visitStatesByModule[module] = VisitState.Visiting;
        visitPath.Add((module, reachedBy));

        foreach (var edge in predecessorsByModule[module])
        {
            Visit(edge.Predecessor, edge.DeclaredBy, predecessorsByModule, visitStatesByModule, visitPath, sortedModules);
        }

        visitPath.RemoveAt(visitPath.Count - 1);
        visitStatesByModule[module] = VisitState.Visited;
        sortedModules.Add(module);
    }

    /// <summary>Each link of the cycle closing at module, as "A runs after B (declared by)".</summary>
    private static string DescribeCycle(List<(IModule Module, string? ReachedBy)> visitPath, IModule module, string? closingDeclaration)
    {
        var cycleStart = visitPath.FindIndex(step => ReferenceEquals(step.Module, module));
        var links = new List<string>();

        for (var index = cycleStart; index < visitPath.Count; index++)
        {
            var (next, declaredBy) = index + 1 < visitPath.Count ? visitPath[index + 1] : (module, closingDeclaration);
            links.Add($"{visitPath[index].Module.Name} runs after {next.Name} ({declaredBy})");
        }

        return $"Circular module ordering: {string.Join(", ", links)}.";
    }
}