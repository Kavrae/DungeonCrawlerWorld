using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions.Systems;

/// <summary>Ticks every dodging entity's own window down toward 0 and removes DodgingComponent once it does -- flat StripeCount = 1 like DelayedActionSystem, acceptable for the same reason (short-lived, rarely-populated; not a measured problem, revisit if it ever becomes one).</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DodgeExpirySystem(PackedComponentPool<DodgingComponent> dodgingEntities) : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<DodgingComponent> _dodgingEntities = dodgingEntities;
    private readonly EntityStripeSet _stripeSet = EntityStripeSet.CreateAndWire(StripeCountValue, dodgingEntities);

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (!_dodgingEntities.TryGetReadonly(entityId, out var dodging))
            {
                continue;
            }

            if (dodging.FramesRemaining <= 1)
            {
                _dodgingEntities.Remove(entityId);
                continue;
            }

            _dodgingEntities.TryUpdate(entityId, static (ref DodgingComponent component) =>
            {
                component.FramesRemaining = MathUtility.DecrementClamped(component.FramesRemaining, 1);
            });
        }
    }
}
