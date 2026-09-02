namespace Game.Modules.Death.Components;

/// <summary>
/// Marks an entity (a corpse, or a container such as a treasure chest) as having had its loot
/// window opened at least once -- set the moment the window opens, regardless of whether anything
/// is actually taken. Distinct from "has items" (see InventoryItemStackComponent's own per-entity
/// count): drives whether MapWindow draws the LootBag-Red badge above or below a corpse's own
/// grey tint.
/// </summary>
public readonly record struct LootedComponent
{
    public override readonly string ToString() => nameof(LootedComponent);
}
