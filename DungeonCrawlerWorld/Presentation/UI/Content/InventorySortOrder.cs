namespace Presentation.UI.Content;

/// <summary>Item-stack sort orders InventoryGridContent.SortOrder supports. GridControl's sort-cycle button drives this by index (see InventoryTabContent, which owns the translation) -- GridControl itself never references this type.</summary>
public enum InventorySortOrder
{
    NameAscending,
    NameDescending,
    QuantityDescending,
    QuantityAscending,
    RecentlyAcquiredDescending,
}
