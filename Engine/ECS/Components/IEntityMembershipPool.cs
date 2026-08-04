namespace Engine.ECS.Components;

/// <summary>
/// Pools whose entity membership can be tracked externally -- the current entity set plus
/// add/remove notifications. Implemented by PackedComponentPool&lt;T&gt; and
/// MultiComponentPool&lt;T&gt;; DirectComponentPool&lt;T&gt; is entity-indexed storage with no
/// membership tracking, so it doesn't implement this. Lets a caller (e.g.
/// Game.Modules.ProcessingTier.ProcessingTierWiring) wire an Engine.ECS.Systems.
/// TieredEntityStripeSet against whichever concrete pool type actually drives a system's
/// population, without that caller needing to know which one it is.
/// </summary>
public interface IEntityMembershipPool : IComponentPool
{
    ReadOnlySpan<int> EntityIds { get; }
    event Action<int>? EntityAdded;
    event Action<int>? EntityRemoved;
}
