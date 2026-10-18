using Builders.Browsing;
using Builders.References;
using Engine.Tags;
using Game.Modules.Actions;
using Presentation.UI.Details;
using Workshop;

namespace Builders.Actions;

/// <summary>The Actions tab: every action in the session's catalog, grouped as abilities, spells and toggles, the selected one in Action Details, and what it grants or is cast by -- read-only.</summary>
public sealed class ActionsTab(ActionCatalog actionCatalog, ContentReferenceIndex references, GameplayTagRegistry gameplayTags) : RecordBrowserTab<ActionDefinition, ActionDetailsWindow>
{
    public const string TabTitle = "Actions";

    public override string Title => TabTitle;

    protected override string BrowserTitle => $"Actions ({actionCatalog.Count})";

    protected override string SearchGhostText => "Search actions";

    protected override string PreviewTitle => "Action Details";

    protected override IEnumerable<ActionDefinition> AllRecords => actionCatalog.Definitions;

    protected override Guid IdOf(ActionDefinition action) => action.Id;

    protected override IReadOnlyList<RecordBrowserSection<ActionDefinition>> BuildSections(string search) =>
        ActionBrowserSections.Build(actionCatalog.Definitions, search, gameplayTags);

    protected override RecordRowAppearance AppearanceOf(ActionDefinition action) => new(action.Name, action.SpriteName, action.Glyph, action.GlyphColor);

    protected override void ShowInPreview(ActionDetailsWindow preview, ActionDefinition action, float contentWidth) =>
        preview.Configure(action, contentWidth);

    protected override IReadOnlyList<RecordLinkSection> LinksOf(ActionDefinition action) =>
        ContentLinks.For(references, ContentRecordKind.Action, action.Id);

    protected override IReadOnlyList<WorkshopDependent> DependentsOf(ActionDefinition action) =>
        ContentLinks.DependentsOf(references, ContentRecordKind.Action, action.Id);
}
