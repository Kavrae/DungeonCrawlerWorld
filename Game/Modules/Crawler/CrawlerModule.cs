using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Crawler.Components;

namespace Game.Modules.Crawler;

public sealed class CrawlerModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000011");

    public Guid Id => ModuleId;

    // Rough estimate of crawler population on startup.
    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<CrawlerComponent>(static (ref existing, incoming) => existing = incoming, initialCapacity: 4_000);
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        // No systems of its own
    }
}
