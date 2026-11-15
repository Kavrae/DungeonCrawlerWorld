using Builders.Browsing;
using Builders.Editing;
using Builders.References;
using Game.Content;
using Game.Modules.Actions;
using Presentation.UI.Details;
using Workshop;

namespace Builders.Actions;

/// <summary>The Actions tab: every action in the session's catalog, grouped as abilities, spells and toggles, the selected one in Action Details, and what it grants or is cast by -- read-only.</summary>
/// <remarks>Reads the workspace's session, and reopens on it whenever it is rebuilt.</remarks>
public sealed class ActionsTab : RecordBrowserTab<ActionDefinition, ActionDetailsWindow>
{
    public const string TabTitle = "Actions";

    private readonly ContentWorkspace _workspace;

    public ActionsTab(ContentWorkspace workspace)
    {
        _workspace = workspace;
        workspace.SessionReplaced += Reopen;
    }

    private ActionCatalog ActionCatalog => _workspace.Session.Catalogs.ActionCatalog;

    public override string Title => TabTitle;

    protected override string BrowserTitle => $"Actions ({ActionCatalog.Count})";

    protected override string SearchGhostText => "Search actions";

    protected override string PreviewTitle => "Action Details";

    protected override IEnumerable<ActionDefinition> AllRecords => ActionCatalog.Definitions;

    protected override Guid IdOf(ActionDefinition action) => action.Id;

    protected override IReadOnlyList<RecordBrowserSection<ActionDefinition>> BuildSections(string search) =>
        ActionBrowserSections.Build(ActionCatalog.Definitions, search, _workspace.Session.Catalogs.GameplayTags);

    protected override RecordRowAppearance AppearanceOf(ActionDefinition action) => new(_workspace.Origins.Label(GameContent.ActionsTable, action.Id, action.Name), action.SpriteName, action.Glyph, action.GlyphColor);

    protected override void ShowInPreview(ActionDetailsWindow preview, ActionDefinition action, float contentWidth) =>
        preview.Configure(action, contentWidth);

    protected override IReadOnlyList<WorkshopRelatedRecord> UsesOf(ActionDefinition action) =>
        ContentLinks.UsesOf(_workspace.References, ContentRecordKind.Action, action.Id);

    protected override IReadOnlyList<WorkshopRelatedRecord> UsedByOf(ActionDefinition action) =>
        ContentLinks.UsedByOf(_workspace.References, ContentRecordKind.Action, action.Id);
}
