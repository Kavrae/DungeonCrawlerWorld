using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Modules;
using Engine.Settings;

namespace Engine.Bootstrap;

/// <summary>A build whose every component pool exists, and no module is configured and no system registered yet.</summary>
/// <remarks>What the caller builds the modules' shared context from.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class RegisteredComponents<TContext> : EcsBuildStage
{
    private readonly EcsBuildState<TContext> _state;

    internal RegisteredComponents(EcsBuildState<TContext> state, ComponentManager componentManager, EntityManager entityManager)
    {
        _state = state;
        ComponentManager = componentManager;
        EntityManager = entityManager;
    }

    public ComponentManager ComponentManager { get; }

    public EntityManager EntityManager { get; }

    public EventBus EventBus => _state.EventBus;

    public IReadOnlyList<IModule<TContext>> Modules => _state.SortedModules;

    public SettingValues Settings => _state.Settings;

    /// <summary>Runs every module's Configure with context, which every module's RegisterSystems then receives.</summary>
    public ConfiguredModules<TContext> Configure(TContext context)
    {
        MarkAdvanced();
        using var stageScope = EngineHooks.DiagnosticScope("Configure");

        foreach (var module in _state.SortedModules)
        {
            using var modulePhaseScope = EngineHooks.DiagnosticScope("Configure", module.Name);
            module.Configure(context);
        }

        return new ConfiguredModules<TContext>(_state, ComponentManager, EntityManager, context);
    }
}
