using Engine.Modules;

namespace Engine.Bootstrap;

/// <summary>Validates a module set and orders it by RunsAfter/RunsBefore.</summary>
/// <remarks>Throws, ordering nothing, on a missing requirement, a circular ordering, or two modules sharing a type or a non-empty Id.</remarks>
/// <cleanupVersion>1</cleanupVersion>
internal static class ModuleOrder
{
    /// <summary>Orders modules so every RunsAfter/RunsBefore constraint holds, keeping the caller's order wherever nothing constrains it.</summary>
    /// <remarks>Depth-first in input order, visiting a module's predecessors (in input order) before it, so a list that already satisfies every constraint comes back unchanged.</remarks>
    public static List<TModule> Sort<TModule>(IReadOnlyList<TModule> modules) where TModule : class, IModule
    {
        var modulesById = IndexById(modules);
        ValidateRequirements(modules, modulesById);

        var predecessorsByModule = FindPredecessors(modules, modulesById);
        var sortedModules = new List<TModule>(modules.Count);
        var visitStatesByModule = new Dictionary<TModule, VisitState>(ReferenceEqualityComparer.Instance);
        var visitPath = new List<(TModule Module, string? ReachedBy)>();

        foreach (var module in modules)
        {
            Visit(module, reachedBy: null, predecessorsByModule, visitStatesByModule, visitPath, sortedModules);
        }

        return sortedModules;
    }

    /// <summary>Every module with a non-empty Id, by that Id.</summary>
    /// <remarks>Guid.Empty is no identity at all, so any number of such modules coexist and none can be named by another.</remarks>
    private static Dictionary<Guid, TModule> IndexById<TModule>(IReadOnlyList<TModule> modules) where TModule : class, IModule
    {
        var moduleTypes = new HashSet<Type>();
        var modulesById = new Dictionary<Guid, TModule>();

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

    private static void ValidateRequirements<TModule>(IReadOnlyList<TModule> modules, Dictionary<Guid, TModule> modulesById) where TModule : class, IModule
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
    private readonly record struct OrderingEdge<TModule>(TModule Predecessor, string DeclaredBy);

    /// <summary>For each module, the modules that must run before it -- its RunsAfter targets and every module naming it in RunsBefore -- in input order.</summary>
    private static Dictionary<TModule, List<OrderingEdge<TModule>>> FindPredecessors<TModule>(IReadOnlyList<TModule> modules, Dictionary<Guid, TModule> modulesById) where TModule : class, IModule
    {
        var predecessorsByModule = new Dictionary<TModule, List<OrderingEdge<TModule>>>(ReferenceEqualityComparer.Instance);
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
                    predecessorsByModule[module].Add(new OrderingEdge<TModule>(predecessor, $"{module.Name}.{nameof(IModule.RunsAfter)}"));
                }
            }

            foreach (var runsBeforeId in module.RunsBefore)
            {
                if (modulesById.TryGetValue(runsBeforeId, out var successor) && !ReferenceEquals(successor, module))
                {
                    predecessorsByModule[successor].Add(new OrderingEdge<TModule>(module, $"{module.Name}.{nameof(IModule.RunsBefore)}"));
                }
            }
        }

        var inputIndexByModule = new Dictionary<TModule, int>(ReferenceEqualityComparer.Instance);
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
    private static void Visit<TModule>(
        TModule module,
        string? reachedBy,
        Dictionary<TModule, List<OrderingEdge<TModule>>> predecessorsByModule,
        Dictionary<TModule, VisitState> visitStatesByModule,
        List<(TModule Module, string? ReachedBy)> visitPath,
        List<TModule> sortedModules) where TModule : class, IModule
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
    private static string DescribeCycle<TModule>(List<(TModule Module, string? ReachedBy)> visitPath, TModule module, string? closingDeclaration) where TModule : class, IModule
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
