using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions.Systems;

/// <summary>
/// Ticks every dodging entity's own window down toward 0 and removes DodgingComponent once it
/// does.
/// </summary>
/// <remarks>
/// The one timed system left deliberately untiered, at a flat StripeCount 1 (so it visits every
/// dodging entity every frame and a decrement of 1 is the correct real-time rate). Dodge is a
/// deliberately brief immunity window -- as little as 0.5s at low Dexterity, see Combat Overhaul:
/// Dodge -- and its whole point is precise timing against an incoming attack, so resolving it in
/// coarse StripeCount * divisor steps would blur exactly the thing it exists to measure. The
/// population is bounded by "entities mid-dodge right now", which is tiny.
///
/// This used to cite DelayedActionSystem as a fellow flat-StripeCount system; that one has since
/// been tiered, so the comparison no longer holds. The justification here is the timing precision
/// above, not the company it keeps -- but note that "the population is small so untiered is fine"
/// is the same reasoning PotionCooldownSystem and PoisonSystem carried right up until measurement
/// showed otherwise, so this deserves a look if dodge ever becomes common.
/// </remarks>
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
