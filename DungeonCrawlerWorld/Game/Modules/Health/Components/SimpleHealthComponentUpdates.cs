using Engine.ECS.Components;
using Engine.Math;

namespace Game.Modules.Health.Components;

/// <summary>Adds change to the entity's current health (negative for damage), kept between 0 and its modifier-effective maximum.</summary>
internal readonly struct ShiftCurrentHealthUpdate(float change, float effectiveMaximumHealth) : IComponentUpdater<SimpleHealthComponent>
{
    public void Update(ref SimpleHealthComponent health) =>
        health.CurrentHealth = MathUtility.Clamp(health.CurrentHealth + change, 0f, effectiveMaximumHealth);
}
