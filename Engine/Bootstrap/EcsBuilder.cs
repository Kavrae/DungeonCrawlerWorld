using Engine.Events;
using Engine.Modules;
using Engine.Settings;

namespace Engine.Bootstrap;

/// <summary>Builds an EcsContext from a module set, one phase at a time.</summary>
/// <remarks>
/// Each phase is a method on the stage before it and returns the next stage, so the phases can only run
/// in order: <see cref="Begin"/> → RegisterComponents → Configure → RegisterSystems → Complete. Each
/// runs the phase for every module, in sorted order, before returning. A stage can be advanced once.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class EcsBuilder
{
    /// <summary>Validates every module's Requires and sorts the set by RunsAfter/RunsBefore, keeping the caller's order wherever nothing constrains it.</summary>
    /// <exception cref="InvalidOperationException">A requirement is missing, the ordering is circular, or two modules share a type or a non-empty Id.</exception>
    public static SortedModules<TContext> Begin<TContext>(
        IReadOnlyList<IModule<TContext>> modules,
        SettingValues settings,
        int initialEntityCapacity,
        int initialComponentCapacity,
        EventBus eventBus) =>
        new(new EcsBuildState<TContext>(ModuleOrder.Sort(modules), settings, initialEntityCapacity, initialComponentCapacity, eventBus));
}

/// <summary>What every stage of one build carries forward.</summary>
internal sealed record EcsBuildState<TContext>(
    IReadOnlyList<IModule<TContext>> SortedModules,
    SettingValues Settings,
    int InitialEntityCapacity,
    int InitialComponentCapacity,
    EventBus EventBus);

/// <summary>A stage of an EcsBuilder build, which can be advanced to the next stage once.</summary>
/// <cleanupVersion>1</cleanupVersion>
public abstract class EcsBuildStage
{
    private bool _isAdvanced;

    /// <exception cref="InvalidOperationException">This stage was already advanced.</exception>
    private protected void MarkAdvanced()
    {
        if (_isAdvanced)
        {
            throw new InvalidOperationException($"{GetType().Name} was already advanced; each build stage advances once.");
        }

        _isAdvanced = true;
    }
}
