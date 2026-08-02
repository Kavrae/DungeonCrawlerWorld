using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Crawler.Components;

namespace Game.Modules.Crawler;

public sealed class CrawlerModule : IModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000011");

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterPackedPool<CrawlerComponent>(static (ref existing, incoming) => existing = incoming);

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No systems of its own
    }
}
