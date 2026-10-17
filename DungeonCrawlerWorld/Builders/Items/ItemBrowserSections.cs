using Engine.Tags;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;

namespace Builders.Items;

/// <summary>One heading in the Items browser and the items under it, by name.</summary>
public sealed record ItemBrowserSection(string Heading, IReadOnlyList<ItemDefinition> Items);

/// <summary>Groups the Items browser's records by how they're used, sorted by name within each group, keeping only those a search matches.</summary>
public static class ItemBrowserSections
{
    private static readonly string[] HeadingsInOrder = ["Potions", "Scrolls", "Wands", "Toggles", "Other"];

    /// <summary>Sections in a fixed order, empty ones left out; search matches a name or a tag's display name, ignoring case, and an empty search matches everything.</summary>
    public static IReadOnlyList<ItemBrowserSection> Build(IEnumerable<ItemDefinition> items, string search, GameplayTagRegistry gameplayTags)
    {
        var itemsByHeading = HeadingsInOrder.ToDictionary(heading => heading, _ => new List<ItemDefinition>());
        foreach (var item in items)
        {
            if (Matches(item, search, gameplayTags))
            {
                itemsByHeading[HeadingOf(item)].Add(item);
            }
        }

        var sections = new List<ItemBrowserSection>();
        foreach (var heading in HeadingsInOrder)
        {
            var sectionItems = itemsByHeading[heading];
            if (sectionItems.Count == 0)
            {
                continue;
            }

            sectionItems.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            sections.Add(new ItemBrowserSection(heading, sectionItems));
        }

        return sections;
    }

    private static string HeadingOf(ItemDefinition item) => item.Activator switch
    {
        PotionActivator => "Potions",
        ScrollActivator => "Scrolls",
        WandActivator => "Wands",
        ToggleItemActivator => "Toggles",
        _ => "Other",
    };

    private static bool Matches(ItemDefinition item, string search, GameplayTagRegistry gameplayTags)
    {
        if (search.Length == 0 || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var tag in item.Tags)
        {
            if (gameplayTags.GetDisplayName(tag).Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
