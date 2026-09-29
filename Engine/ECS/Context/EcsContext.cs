using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;

namespace Engine.ECS.Context;

/// <summary>Composition root bundling the three ECS managers plus the shared EventBus for a running game.</summary>
/// <remarks>
/// Every build produces one, including builds that are never simulated (a mod's trial build, a staging
/// rebuild). The host marks the one it simulates with BeginSession, and ends it by disposing it; only
/// those two emit EngineHooks.Sessions.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EcsContext(EntityManager entityManager, ComponentManager componentManager, SystemManager systemManager, EventBus eventBus) : IDisposable
{
    private bool _isSessionStarted;
    private bool _isDisposed;

    public EntityManager EntityManager { get; } = entityManager;
    public ComponentManager ComponentManager { get; } = componentManager;
    public SystemManager SystemManager { get; } = systemManager;
    public EventBus EventBus { get; } = eventBus;

    /// <summary>Updates the ECS context via the system manager.</summary>
    /// <param name="time">The current engine time.</param>
    public void Update(EngineTime time) => SystemManager.Update(time);

    /// <summary>Marks this context as the simulated session, once the host has finished building it.</summary>
    /// <exception cref="InvalidOperationException">The session already began, or this context was disposed.</exception>
    public void BeginSession()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isSessionStarted)
        {
            throw new InvalidOperationException("This EcsContext's session already began.");
        }

        EngineHooks.Sessions.Listener?.SessionStarted(this);
        _isSessionStarted = true;
    }

    /// <summary>Ends the session, if it began, while every pool is still intact.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_isSessionStarted)
        {
            EngineHooks.Sessions.Listener?.SessionEnding(this);
        }
    }
}
