using Engine.Math;

namespace Game.Modules.Inventory;

/// <summary>One item and how many of it an item's contents grant.</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct ItemContentsEntry(Guid ItemDefinitionId, ushort Quantity);

/// <summary>What an item grants when it's opened, such as a loot box's rewards.</summary>
/// <remarks>
/// Implementations are immutable and compare by value: two stacks whose definitions carry equal
/// contents are the same item and stack together (see InventoryActions.AddItemWithOverride).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IItemContents
{
    /// <summary>Decides the items one opening grants.</summary>
    /// <remarks>Every random choice is drawn from rolls, so the same sequence always grants the same items.</remarks>
    IReadOnlyList<ItemContentsEntry> Roll(SeededRandom rolls, ItemCatalog itemCatalog);
}
