using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Modules;

namespace Engine.Bootstrap;

/// <summary>What the engine created for one build, handed to EcsBuildRequest.CreateContext once every component is registered.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EcsBuildServices
{
    internal EcsBuildServices(EcsBuildPurpose purpose, ComponentManager componentManager, EntityManager entityManager, EventBus eventBus, SimulationClock clock, ModuleDeclarations declarations)
    {
        Purpose = purpose;
        ComponentManager = componentManager;
        EntityManager = entityManager;
        EventBus = eventBus;
        Clock = clock;
        Declarations = declarations;
    }

    /// <summary>What the build is for.</summary>
    public EcsBuildPurpose Purpose { get; }

    public ComponentManager ComponentManager { get; }

    public EntityManager EntityManager { get; }

    public EntityKeys EntityKeys => ComponentManager.Keys;

    public EventBus EventBus { get; }

    /// <summary>The simulation's "now": the clock this build's SystemManager advances.</summary>
    public SimulationClock Clock { get; }

    /// <summary>Everything the build's modules declared.</summary>
    public ModuleDeclarations Declarations { get; }
}
