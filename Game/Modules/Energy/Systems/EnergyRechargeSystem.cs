using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Energy.Components;

namespace Game.Modules.Energy.Systems;

/// <summary>Passively regenerates entity energy, which other systems (e.g. movement) spend.</summary>
public sealed class EnergyRechargeSystem : ISystem
{
    public byte StripeCount => 10;

    private readonly PackedComponentPool<EnergyComponent> _energyComponents;
    private readonly EntityStripeSet _stripeSet;

    public EnergyRechargeSystem(PackedComponentPool<EnergyComponent> energyComponents)
    {
        _energyComponents = energyComponents;
        _stripeSet = new EntityStripeSet(StripeCount, energyComponents.EntityIds);
        energyComponents.EntityAdded += _stripeSet.OnEntityAdded;
        energyComponents.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            // Skipping the whole TryUpdate call (not just the math inside it) when there's
            // nothing to recharge also avoids bumping the component's version for no reason --
            // TryUpdate bumps it unconditionally once the delegate runs, which would otherwise
            // make a never-changing component look like it's being mutated every stripe cycle.
            if (!_energyComponents.TryGetReadonly(entityId, out var currentEnergyComponent) || currentEnergyComponent.EnergyRecharge == 0)
            {
                continue;
            }

            _energyComponents.TryUpdate(entityId, static (ref EnergyComponent energyComponent) =>
            {
                // Widened to int before adding: CurrentEnergy/EnergyRecharge are both short,
                // and a raw short += can silently overflow/underflow before ClampShort ever
                // runs. int can't overflow for any short + short, so it's safe to clamp after.
                var rechargedEnergy = (int)energyComponent.CurrentEnergy + energyComponent.EnergyRecharge;
                energyComponent.CurrentEnergy = (short)MathUtility.ClampInt(rechargedEnergy, 0, energyComponent.MaximumEnergy);
            });
        }
    }
}
