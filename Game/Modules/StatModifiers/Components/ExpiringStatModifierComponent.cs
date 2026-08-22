namespace Game.Modules.StatModifiers.Components;

/// <summary>
/// Fieldless marker, one instance per currently-active non-permanent StatModifierComponent an
/// entity holds -- StatModifierEffects.Apply adds one alongside any grant whose duration isn't
/// null, and StatModifierExpirySystem removes exactly one whenever it
/// removes an actually-expired modifier. Its own MultiComponentPool -- not a field on
/// StatModifierComponent itself -- exists purely so StatModifierExpirySystem can drive its
/// TieredEntityStripeSet off "has a modifier that can ever expire" instead of "has any modifier
/// at all": a permanent modifier's RemainingDurationFrames never reaches 0, so an entity holding
/// only permanent modifiers (e.g. every Goblin's racial damage reduction, see Goblin.Build) has
/// no business being visited by the expiry system every tiered cycle forever. Deliberately kept
/// separate from StatModifierComponent's own storage rather than splitting that pool by
/// permanence -- see StatModifierExpirySystem's own doc comment for why.
/// </summary>
public readonly record struct ExpiringStatModifierComponent
{
    public override readonly string ToString() => nameof(ExpiringStatModifierComponent);
}
