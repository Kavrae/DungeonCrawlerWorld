using Game.Modules.Inventory;
using Game.Modules.Lootboxes;

namespace Presentation.UI;

/// <summary>What Item Details says about what an item is, beyond what it does: the restrictions it carries and what opening it gives.</summary>
public static class ItemFactsText
{
    /// <summary>Every contents kind Contents describes; a kind missing from it is a gap (ItemFactsTextTests).</summary>
    public static readonly IReadOnlySet<Type> DescribedContentsKinds = new HashSet<Type> { typeof(RandomSingleStackContents), typeof(SetItemContents) };

    /// <summary>The restriction's name, in negative wording ("Untradable").</summary>
    public static string NameOf(ItemRestriction restriction) => restriction switch
    {
        ItemRestriction.Untradable => "Untradable",
        _ => restriction.ToString(),
    };

    /// <summary>What the restriction means, for its tooltip.</summary>
    public static string DescriptionOf(ItemRestriction restriction) => restriction switch
    {
        ItemRestriction.Untradable => "Never leaves its owner: it can't be traded, sold, dropped or destroyed.",
        _ => restriction.ToString(),
    };

    /// <summary>The "Opens into" lines for contents: what one opening gives, item names through names.</summary>
    public static IReadOnlyList<string> Contents(IItemContents contents, EffectTextNames names) => contents switch
    {
        RandomSingleStackContents => [$"{RandomSingleStackContents.MinimumQuantity}-{RandomSingleStackContents.MaximumQuantity} of one random item"],
        SetItemContents set => [.. set.Entries.Select(entry => $"{names.ItemNameOf(entry.ItemDefinitionId)} x{entry.Quantity}")],
        _ => [contents.GetType().Name],
    };
}
