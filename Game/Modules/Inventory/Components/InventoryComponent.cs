namespace Game.Modules.Inventory.Components;

/// <summary>
/// Marks an entity as participating in the inventory system at all -- granted once, the first
/// time InventoryActions.AddItem ever gives it an item (see InventoryGrant), and never removed
/// afterward even once every stack is consumed. Distinct from "has any InventoryItemStackComponent
/// stacks right now" (which fluctuates as items are gained/spent) -- this is the permanent
/// "can this entity be looted/managed at all" signal a future corpse-looting UI needs, so "empty
/// inventory" and "no inventory" stop being the same question.
/// </summary>
public readonly record struct InventoryComponent;
