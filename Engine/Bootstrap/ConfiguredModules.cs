using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Modules;
using Engine.Settings;

namespace Engine.Bootstrap;

/// <summary>A build whose every module is configured, and no behavior registered yet.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class ConfiguredModules<TContext> : EcsBuildStage
{
    private readonly EcsBuildState<TContext> _state;

    internal ConfiguredModules(EcsBuildState<TContext> state, ComponentManager componentManager, EntityManager entityManager, TContext context)
    {
        _state = state;
        ComponentManager = componentManager;
        EntityManager = entityManager;
        Context = context;
    }

    public ComponentManager ComponentManager { get; }

    public EntityManager EntityManager { get; }

    public EventBus EventBus => _state.EventBus;

    public SettingValues Settings => _state.Settings;

    public TContext Context { get; }

    /// <summary>Creates the system manager and runs every module's RegisterBehavior, in sorted order -- the order the systems run each frame and handlers receive events.</summary>
    public RegisteredBehavior RegisterBehavior()
    {
        MarkAdvanced();
        using var stageScope = EngineHooks.DiagnosticScope("RegisterBehavior");

        var systemManager = new SystemManager();
        var registration = new BehaviorRegistration<TContext>(systemManager, ComponentManager, _state.Settings, Context);

        foreach (var module in _state.SortedModules)
        {
            using var modulePhaseScope = EngineHooks.DiagnosticScope("RegisterBehavior", module.Name);
            module.RegisterBehavior(registration);
        }

        return new RegisteredBehavior(EntityManager, ComponentManager, systemManager, _state.EventBus);
    }
}
