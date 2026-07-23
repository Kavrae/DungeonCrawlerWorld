using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Health.Components;

namespace Game.Modules.Health.Systems;

/// <summary>
/// Passively regenerates entity health, bounded between 0 and MaximumHealth.
/// TODO Health v2: split into per-body-part health once a real damage/status-effect system
/// exists to justify the added complexity.
/// </summary>
public sealed class HealthRegenSystem : ISystem
{
    public byte StripeCount => 10;

    private readonly PackedComponentPool<HealthComponent> _healthComponents;
    private readonly EntityStripeSet _stripeSet;

    public HealthRegenSystem(PackedComponentPool<HealthComponent> healthComponents)
    {
        _healthComponents = healthComponents;
        _stripeSet = new EntityStripeSet(StripeCount, healthComponents.EntityIds);
        healthComponents.EntityAdded += _stripeSet.OnEntityAdded;
        healthComponents.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            // Skipping the whole TryUpdate call (not just the math inside it) when there's
            // nothing to regenerate also avoids bumping the component's version for no reason
            // -- TryUpdate bumps it unconditionally once the delegate runs, which would
            // otherwise make a never-changing component look like it's being mutated every
            // stripe cycle.
            if (!_healthComponents.TryGetReadonly(entityId, out var currentHealthComponent) || currentHealthComponent.HealthRegen == 0)
            {
                continue;
            }

            _healthComponents.TryUpdate(entityId, static (ref healthComponent) =>
            {
                // Widened to int before adding: CurrentHealth/HealthRegen are both short, and
                // a raw short += can silently overflow/underflow before ClampShort ever runs.
                // int can't overflow for any short + short, so it's safe to clamp after.
                var regeneratedHealth = (int)healthComponent.CurrentHealth + healthComponent.HealthRegen;
                healthComponent.CurrentHealth = (short)MathUtility.ClampInt(regeneratedHealth, 0, healthComponent.MaximumHealth);
            });
        }
    }
}