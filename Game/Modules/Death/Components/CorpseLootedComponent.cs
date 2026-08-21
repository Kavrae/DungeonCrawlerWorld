namespace Game.Modules.Death.Components;

/// <summary>
/// Marks a corpse as having had its loot window opened at least once -- set the moment the window
/// opens, regardless of whether anything is actually taken. Distinct from "has items" (see
/// InventoryItemStackComponent's own per-entity count): drives whether MapWindow draws the
/// LootBag-Red badge above or below the corpse's own grey tint.
/// </summary>
public readonly record struct CorpseLootedComponent
{
    public override readonly string ToString() => nameof(CorpseLootedComponent);
}
