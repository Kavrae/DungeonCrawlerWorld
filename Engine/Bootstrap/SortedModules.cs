using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Modules;
using Engine.Settings;

namespace Engine.Bootstrap;

/// <summary>A module set whose requirements are satisfied, in the order its phases run.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SortedModules<TContext> : EcsBuildStage
{
    private readonly EcsBuildState<TContext> _state;

    internal SortedModules(EcsBuildState<TContext> state) => _state = state;

    public IReadOnlyList<IModule<TContext>> Modules => _state.SortedModules;

    public SettingValues Settings => _state.Settings;

    /// <summary>Creates the component and entity managers and runs every module's RegisterComponents.</summary>
    public RegisteredComponents<TContext> RegisterComponents()
    {
        MarkAdvanced();
        using var stageScope = EngineHooks.DiagnosticScope("RegisterComponents");

        var componentManager = new ComponentManager(_state.InitialEntityCapacity, _state.InitialComponentCapacity);
        var entityManager = new EntityManager(componentManager, _state.InitialEntityCapacity);
        var registration = new ComponentRegistration(componentManager, _state.Settings);

        foreach (var module in _state.SortedModules)
        {
            using var modulePhaseScope = EngineHooks.DiagnosticScope("RegisterComponents", module.Name);
            module.RegisterComponents(registration);
        }

        return new RegisteredComponents<TContext>(_state, componentManager, entityManager);
    }
}
