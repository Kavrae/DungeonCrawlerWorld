using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;

namespace Engine.ECS.Context;

/// <summary>
/// Mechanical assembly of an <see cref="EcsContext"/>. Deciding the correct module
/// registration order is Bootstrapper's job; EcsContextBuilder just exposes the managers to
/// register into and produces the finished EcsContext.
/// </summary>
public sealed class EcsContextBuilder
{
    public ComponentManager ComponentManager { get; }
    public SystemManager SystemManager { get; } = new();
    public EventBus EventBus { get; }

    private readonly EntityManager _entityManager;

    public EcsContextBuilder(int initialEntityCapacity, int initialComponentCapacity, EventBus? eventBus = null)
    {
        ComponentManager = new ComponentManager(initialEntityCapacity, initialComponentCapacity);
        _entityManager = new EntityManager(ComponentManager, initialEntityCapacity);
        EventBus = eventBus ?? new EventBus();
    }

    public EcsContext Build() => new(_entityManager, ComponentManager, SystemManager, EventBus);
}