using Engine.ECS.Components;
using Engine.Math;

namespace Game.Modules.Mana.Components;

/// <summary>Adds change to the entity's current mana (negative to take it), kept between 0 and its modifier-effective maximum.</summary>
internal readonly struct ShiftCurrentManaUpdate(float change, float effectiveMaximumMana) : IComponentUpdater<ManaComponent>
{
    public void Update(ref ManaComponent mana) =>
        mana.CurrentMana = MathUtility.Clamp(mana.CurrentMana + change, 0f, effectiveMaximumMana);
}
