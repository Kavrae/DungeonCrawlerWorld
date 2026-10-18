using Builders.Browsing;
using Builders.References;
using Engine.Tags;
using Game.Modules.Inventory;
using Presentation.UI.Inventory;
using Workshop;

namespace Builders.Items;

/// <summary>The Items tab: every item in the session's catalog, grouped by how it's used, the selected one in Item Details, and what it casts, grants or is used by -- read-only.</summary>
/// <remarks>Item Details shows a definition with no stack behind it (ItemDetailsWindow.NoStackInstanceId), so it has no blocker and no Compare.</remarks>
public sealed class ItemsTab(ItemCatalog itemCatalog, ContentReferenceIndex references, GameplayTagRegistry gameplayTags, int previewEntityId) : RecordBrowserTab<ItemDefinition, ItemDetailsWindow>
{
    public const string TabTitle = "Items";

    public override string Title => TabTitle;

    protected override string BrowserTitle => $"Items ({itemCatalog.Count})";

    protected override string SearchGhostText => "Search items";

    protected override string PreviewTitle => "Item Details";

    protected override IEnumerable<ItemDefinition> AllRecords => itemCatalog.Definitions;

    protected override Guid IdOf(ItemDefinition item) => item.Id;

    protected override IReadOnlyList<RecordBrowserSection<ItemDefinition>> BuildSections(string search) =>
        ItemBrowserSections.Build(itemCatalog.Definitions, search, gameplayTags);

    protected override RecordRowAppearance AppearanceOf(ItemDefinition item) => new(item.Name, item.SpriteName, item.Glyph, item.GlyphColor);

    protected override void ShowInPreview(ItemDetailsWindow preview, ItemDefinition item, float contentWidth) =>
        preview.Configure(previewEntityId, ItemDetailsWindow.NoStackInstanceId, item, contentWidth);

    protected override IReadOnlyList<RecordLinkSection> LinksOf(ItemDefinition item) =>
        ContentLinks.For(references, ContentRecordKind.Item, item.Id);

    protected override IReadOnlyList<WorkshopDependent> DependentsOf(ItemDefinition item) =>
        ContentLinks.DependentsOf(references, ContentRecordKind.Item, item.Id);
}
