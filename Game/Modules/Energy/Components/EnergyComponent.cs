using Engine.Utilities;

namespace Game.Modules.Energy.Components;

/// <summary>
/// An entity's energy bounds and passive regeneration. Energy restricts action count/speed
/// -- an entity trying to move twice as fast is unlikely to have enough energy to also attack.
/// Contains only base energy statistics; additional energy types should be separate components.
/// </summary>
public struct EnergyComponent(short currentEnergy, short energyRecharge, short maximumEnergy)
{
    public short CurrentEnergy { get; set; } = currentEnergy;
    public short EnergyRecharge { get; set; } = energyRecharge;
    public short MaximumEnergy { get; set; } = maximumEnergy;

    // BuildPercentageBar throws for maximumValue <= 0 (a caller bug, by its own contract) --
    // guarded here rather than there, since a stray/default-valued component must not crash
    // the debug inspector that calls ToString() on whatever an entity actually has.
    public override readonly string ToString() =>
        MaximumEnergy > 0
            ? StringUtility.BuildPercentageBar("E", CurrentEnergy, MaximumEnergy, 20)
            : $"E : [invalid MaximumEnergy: {MaximumEnergy}]";
}
