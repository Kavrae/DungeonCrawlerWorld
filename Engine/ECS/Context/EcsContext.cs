using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;

namespace Engine.ECS.Context;

/// <summary>Composition root bundling the three ECS managers plus the shared EventBus for a running game.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EcsContext(EntityManager entityManager, ComponentManager componentManager, SystemManager systemManager, EventBus eventBus)
{
    public EntityManager EntityManager { get; } = entityManager;
    public ComponentManager ComponentManager { get; } = componentManager;
    public SystemManager SystemManager { get; } = systemManager;
    public EventBus EventBus { get; } = eventBus;

    /// <summary>Updates the ECS context via the system manager.</summary>
    /// <param name="time">The current engine time.</param>
    public void Update(EngineTime time) => SystemManager.Update(time);
}