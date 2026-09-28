using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Blueprints;
using Game.Modules.Health;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests;

/// <summary>Fresh, empty stand-ins for the pools and queries a system requires but a test doesn't exercise.</summary>
internal static class EmptyPools
{
    private const int Capacity = 16;

    public static DirectComponentPool<T> Direct<T>() where T : struct =>
        new(Capacity, static (ref existing, incoming) => existing = incoming);

    public static PackedComponentPool<T> Packed<T>() where T : struct =>
        new(Capacity, Capacity, static (ref existing, incoming) => existing = incoming);

    public static MultiComponentPool<T> Multi<T>() where T : struct =>
        new(Capacity, Capacity);

    public static EntityBodyParts BodyParts() =>
        EntityBodyParts.For(BuiltInTestComponents.RegisterAll(new ComponentManager(Capacity, Capacity)), new BlueprintRegistry());

    public static ProcessingTierQuery Tiers() =>
        new(Direct<ProcessingTierComponent>());

    /// <summary>A wired feed over empty pools: no entity is Local, so it publishes nothing.</summary>
    public static FloatingTextFeed FloatingTextFeed()
    {
        var feed = new FloatingTextFeed();
        feed.Wire(new EventBus(), Direct<ProcessingTierComponent>(), Direct<TransformComponent>());
        return feed;
    }
}
