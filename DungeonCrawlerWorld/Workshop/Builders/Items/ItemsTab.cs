using Builders.Browsing;
using Builders.Editing;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Presentation.UI.Inventory;
using System.Text.Json.Nodes;
using UIEngine.UI;
using UIEngine.UI.Filtering;
using Workshop.Editing;

namespace Builders.Items;

/// <summary>The Items tab: every item the Workshop holds, grouped by how it's used, edited in the inspector and shown in Item Details as it reads now (ContentEditingTab).</summary>
/// <remarks>Item Details shows a definition with no stack behind it (ItemDetailsWindow.NoStackInstanceId) and no player (NoWorkshopPlayer), so it has no blocker and no Compare, and every value is the definition's own.</remarks>
public sealed class ItemsTab(ContentEditingTable<ItemDefinition> table, ContentEditing editing, IContentInspectorSource inspectorSource, ElementPoolService elementPool)
    : ContentEditingTab<ItemDefinition, ItemDetailsWindow>(table, editing, inspectorSource, elementPool)
{
    public const string TabTitle = "Items";

    private const string ItemNameKey = ActivatableContent.NameKey;
    private const string GlyphKey = ActivatableContent.GlyphKey;

    public override string Title => TabTitle;

    protected override string SearchGhostText => "Search items";

    protected override string PreviewTitle => "Item Details";

    protected override string SingularName => "item";

    protected override string NameKey => ItemNameKey;

    protected override JsonObject NewRecordFields(string name) => new() { [ItemNameKey] = name, [GlyphKey] = ActivatablePlaceholder.Glyph };

    protected override IReadOnlyList<RecordBrowserSection<ItemDefinition>> SectionsOf(IEnumerable<ItemDefinition> definitions, string search) =>
        ItemBrowserSections.Build(definitions, search, Workspace.Session.Catalogs.GameplayTags);

    /// <summary>Type: the browser's own headings (ItemBrowserSections.HeadingOf), any of them.</summary>
    protected override IReadOnlyList<ListFilterGroup<ContentRecordDocument<ItemDefinition>>> TableFilterGroups =>
        [RecordFilterGroups.OneOf<ContentRecordDocument<ItemDefinition>>("type", "Type", ItemBrowserSections.HeadingsInOrder, static document => ItemBrowserSections.HeadingOf(document.LastGood))];

    protected override RecordRowAppearance LookOf(ItemDefinition item) => new(item.Name, item.SpriteName, item.Glyph, item.GlyphColor);

    protected override void ShowDefinition(ItemDetailsWindow preview, ItemDefinition item, float contentWidth) =>
        preview.Configure(NoWorkshopPlayer.NoPlayerEntityId, ItemDetailsWindow.NoStackInstanceId, item, contentWidth);

    /// <summary>Item Details' own comparison colouring: each line better or worse than the other versions'.</summary>
    protected override void ShowComparedDefinition(ItemDetailsWindow preview, ItemDefinition item, float contentWidth, IReadOnlyList<ItemDefinition> comparedAgainst) =>
        preview.Configure(NoWorkshopPlayer.NoPlayerEntityId, ItemDetailsWindow.NoStackInstanceId, item, contentWidth, comparedAgainst);

    /// <summary>A record standing in for one that doesn't decode: its own name and glyph when it has them as text.</summary>
    public static string PlaceholderJsonOf(EditableContentRecord record) => ActivatablePlaceholder.JsonOf(record, "Item", withActivator: false);
}
