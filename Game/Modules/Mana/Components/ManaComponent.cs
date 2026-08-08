namespace Game.Modules.Mana.Components;

/// <summary>
/// An entity's mana bounds -- mirrors HealthComponent's shape (including its float storage, for
/// the same exact-fractional-regen reason -- see that struct's own doc comment; Mana is in fact
/// the case that made float storage necessary, since MaximumMana is typically only 2-12 for a
/// starting roll, where a rounded regen tick either stalls for several seconds or never lands at
/// all). No regen field here either, ManaRegenSystem computes it live each tick from the entity's
/// Intelligence AbilityScoreComponent.Total. Not granted to every entity -- only entities that
/// have gained an ability with a nonzero ManaCost get one, via ManaGrant.EnsureManaComponentExists,
/// with MaximumMana snapshotting that Intelligence total at grant time (the same
/// one-time-bake-then-layer-modifiers-on-top pattern HealthComponent.MaximumHealth uses).
/// </summary>
public struct ManaComponent(float currentMana, float maximumMana)
{
    public float CurrentMana { get; set; } = currentMana;
    public float MaximumMana { get; set; } = maximumMana;

    public override readonly string ToString() => $"CurrentMana : {CurrentMana}\nMaximumMana : {MaximumMana}";
}
