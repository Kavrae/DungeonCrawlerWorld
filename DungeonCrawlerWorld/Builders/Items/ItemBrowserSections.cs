using Builders.Browsing;
using Engine.Tags;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;

namespace Builders.Items;

/// <summary>The Items browser's groups: by how an item is used.</summary>
public static class ItemBrowserSections
{
    public static IReadOnlyList<string> HeadingsInOrder { get; } = ["Potions", "Scrolls", "Wands", "Toggles", "Other"];

    public static IReadOnlyList<RecordBrowserSection<ItemDefinition>> Build(IEnumerable<ItemDefinition> items, string search, GameplayTagRegistry gameplayTags) =>
        RecordBrowserSections.Build(items, HeadingsInOrder, HeadingOf, static item => item.Name, static item => item.Tags, search, gameplayTags);

    private static string HeadingOf(ItemDefinition item) => item.Activator switch
    {
        PotionActivator => "Potions",
        ScrollActivator => "Scrolls",
        WandActivator => "Wands",
        ToggleItemActivator => "Toggles",
        _ => "Other",
    };
}
