namespace Game.Effects;

/// <summary>What the entries already asked in one EffectSequence.CanApply have claimed from its target, so each later entry is asked against what is left.</summary>
/// <remarks>Two costs in one list are checked together: 10 mana and 10 mana is 20, whatever each would be alone. The same holds for health.</remarks>
public struct EffectReservations
{
    /// <summary>The mana claimed so far.</summary>
    public float ReservedMana { get; private set; }

    public void ReserveMana(float amount) => ReservedMana += amount;

    /// <summary>The health claimed so far.</summary>
    public float ReservedHealth { get; private set; }

    public void ReserveHealth(float amount) => ReservedHealth += amount;
}
