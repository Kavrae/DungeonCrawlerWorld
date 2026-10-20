using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Modules;
using Engine.Settings;
using Engine.Utilities;

namespace Engine.Bootstrap;

/// <summary>Builds an EcsContext from a module set, running every phase in the only order that works.</summary>
/// <remarks>
/// Build runs, each phase for every module before the next: apply the frame rate; instantiate every factory; combine
/// mods into the modules by Id; validate and sort the set; declare settings, tags and content types; register
/// components; check that every replacing mod kept the replaced module's components (ReplacementContract); create
/// the context; Configure, then AfterConfigure; RegisterBehavior, then AfterBehavior. The engine
/// creates everything the modules share -- managers, EventBus, SimulationClock -- so nothing is swapped in afterwards.
/// No entity can be created until Build returns: before then, blueprints may be unresolved and systems and handlers
/// missing, so an entity made early would be one nothing has seen.
/// </remarks>
/// <cleanupVersion>2</cleanupVersion>
public static class EcsBuilder
{
    /// <summary>Builds request's modules into a complete EcsContext.</summary>
    /// <exception cref="InvalidOperationException">A requirement is missing, the ordering is circular, two modules share a type or a non-empty Id, a mod that replaces a module doesn't register its components, or a phase throws.</exception>
    public static EcsBuild<TContext> Build<TContext>(EcsBuildRequest<TContext> request)
    {
        ArgumentNullException.ThrowIfNull(request);

        GameTiming.SetFramesPerSecond(request.FramesPerSecond);

        var (modules, replacements, declarations) = DeclareModules(request.Modules, request.Mods, request.SettingsSources, request.RequiredModuleIds);

        var componentManager = new ComponentManager(request.InitialEntityCapacity, request.InitialComponentCapacity);
        var entityManager = new EntityManager(componentManager, request.InitialEntityCapacity) { IsEntityCreationHeld = true };
        RegisterComponents(modules, new ComponentRegistration(componentManager, declarations));
        CheckReplacements(replacements, declarations, request.SettingsSources);

        var eventBus = new EventBus();
        var clock = new SimulationClock();
        var context = request.CreateContext(new EcsBuildServices(request.Purpose, componentManager, entityManager, eventBus, clock, declarations));

        Configure(modules, context);
        request.AfterConfigure?.Invoke(context);

        var systemManager = new SystemManager(clock, request.SimulatedTierCount);
        RegisterBehavior(modules, new BehaviorRegistration<TContext>(systemManager, componentManager, eventBus, declarations, context));

        var ecsContext = new EcsContext(entityManager, componentManager, systemManager, eventBus, request.Purpose);
        request.AfterBehavior?.Invoke(context, ecsContext);

        entityManager.IsEntityCreationHeld = false;
        return new EcsBuild<TContext>(ecsContext, context, modules, declarations);
    }

    /// <summary>What a module set declares -- its settings as resolved, its tags and its content codecs -- without building it.</summary>
    /// <remarks>Instantiates, combines, validates and sorts the set exactly as Build does, so the declarations are the ones a build of the same set would see.</remarks>
    /// <exception cref="InvalidOperationException">A requirement is missing, the ordering is circular, or two modules share a type or a non-empty Id.</exception>
    public static ModuleDeclarations Declare<TContext>(IReadOnlyList<ModuleFactory<TContext>> modules, IReadOnlyList<ModuleFactory<TContext>>? mods = null, IReadOnlyList<ISettingsSource>? settingsSources = null) =>
        DeclareModules(modules, mods ?? [], settingsSources ?? [], []).Declarations;

    private static (List<IModule<TContext>> Modules, List<(IModule<TContext> Replaced, IModule<TContext> Replacement)> Replacements, ModuleDeclarations Declarations) DeclareModules<TContext>(
        IReadOnlyList<ModuleFactory<TContext>> modules,
        IReadOnlyList<ModuleFactory<TContext>> mods,
        IReadOnlyList<ISettingsSource> settingsSources,
        IReadOnlyList<Guid> requiredModuleIds)
    {
        var gameModules = modules.CreateAll();
        var modModules = mods.CreateAll();
        var replacements = ReplacementContract.FindReplacements(gameModules, modModules);
        var sortedModules = ModuleOrder.Sort(ModuleSet.Combine(gameModules, modModules), requiredModuleIds);

        using var stageScope = EngineHooks.DiagnosticScope("DeclareModules");
        return (sortedModules, replacements, ModuleDeclarations.Declare(sortedModules, settingsSources));
    }

    private static void CheckReplacements<TContext>(List<(IModule<TContext> Replaced, IModule<TContext> Replacement)> replacements, ModuleDeclarations declarations, IReadOnlyList<ISettingsSource> settingsSources)
    {
        if (replacements.Count == 0)
        {
            return;
        }

        using var stageScope = EngineHooks.DiagnosticScope("CheckReplacements");
        ReplacementContract.ThrowIfBroken(replacements, declarations, settingsSources);
    }

    private static void RegisterComponents<TContext>(List<IModule<TContext>> modules, ComponentRegistration registration)
    {
        using var stageScope = EngineHooks.DiagnosticScope("RegisterComponents");
        foreach (var module in modules)
        {
            using var modulePhaseScope = EngineHooks.DiagnosticScope("RegisterComponents", module.Name);
            module.RegisterComponents(registration);
        }
    }

    private static void Configure<TContext>(List<IModule<TContext>> modules, TContext context)
    {
        using var stageScope = EngineHooks.DiagnosticScope("Configure");
        foreach (var module in modules)
        {
            using var modulePhaseScope = EngineHooks.DiagnosticScope("Configure", module.Name);
            module.Configure(context);
        }
    }

    private static void RegisterBehavior<TContext>(List<IModule<TContext>> modules, BehaviorRegistration<TContext> registration)
    {
        using var stageScope = EngineHooks.DiagnosticScope("RegisterBehavior");
        foreach (var module in modules)
        {
            using var modulePhaseScope = EngineHooks.DiagnosticScope("RegisterBehavior", module.Name);
            module.RegisterBehavior(registration);
        }
    }
}
