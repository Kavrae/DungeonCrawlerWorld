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
            if (_healthComponents.TryGetReadonly(entityId, out var currentHealthComponent) && currentHealthComponent.HealthRegen != 0)
            {
                _healthComponents.TryUpdate(entityId, static (ref healthComponent) =>
                {
                    var regeneratedHealth = (int)healthComponent.CurrentHealth + healthComponent.HealthRegen;
                    healthComponent.CurrentHealth = (short)MathUtility.ClampInt(regeneratedHealth, 0, healthComponent.MaximumHealth);
                });
            }
        }
    }
}