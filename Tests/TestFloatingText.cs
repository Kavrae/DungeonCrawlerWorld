using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Tests;

/// <summary>A wired FloatingTextFeed over its own tier and transform pools, recording every FloatingTextEvent it publishes.</summary>
internal sealed class TestFloatingText
{
    public TestFloatingText()
    {
        Feed.Wire(EventBus, ProcessingTiers, Transforms);
        EventBus.Subscribe<FloatingTextEvent>(Published.Add);
    }

    public EventBus EventBus { get; } = new();
    public FloatingTextFeed Feed { get; } = new();
    public List<FloatingTextEvent> Published { get; } = [];
    public Engine.ECS.Components.Stores.DirectComponentPool<ProcessingTierComponent> ProcessingTiers { get; } = EmptyPools.Direct<ProcessingTierComponent>();
    public Engine.ECS.Components.Stores.DirectComponentPool<TransformComponent> Transforms { get; } = EmptyPools.Direct<TransformComponent>();

    public TestFloatingText Place(int entityId, ProcessingTierLevel tier, int x = 0, int y = 0, int layer = 0, byte width = 1, byte height = 1)
    {
        ProcessingTiers.Add(entityId, new ProcessingTierComponent(tier));
        Transforms.Add(entityId, new TransformComponent(new Vector3Int(x, y, layer), new Vector2Byte(width, height)));
        return this;
    }
}
