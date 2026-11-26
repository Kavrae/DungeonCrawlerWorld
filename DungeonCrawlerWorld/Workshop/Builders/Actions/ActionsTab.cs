using System.Text.Json.Nodes;
using Builders.Browsing;
using Builders.Editing;
using Game.Modules.Actions;
using Presentation.UI.Details;
using UIEngine.UI;
using UIEngine.UI.Filtering;
using Workshop.Editing;

namespace Builders.Actions;

/// <summary>The Actions tab: every action the Workshop holds, grouped as abilities, spells and toggles, edited in the inspector and shown in Action Details as content gives it (ContentEditingTab).</summary>
/// <remarks>Action Details has no user here, so every value is the definition's own: no ability-score bonus, no Intelligence scaling, no modifiers.</remarks>
public sealed class ActionsTab(ContentEditingTable<ActionDefinition> table, ContentEditing editing, IContentInspectorSource inspectorSource, ElementPoolService elementPool)
    : ContentEditingTab<ActionDefinition, ActionDetailsWindow>(table, editing, inspectorSource, elementPool)
{
    public const string TabTitle = "Actions";

    private const string ActionNameKey = ActivatableContent.NameKey;

    public override string Title => TabTitle;

    protected override string SearchGhostText => "Search actions";

    protected override string PreviewTitle => "Action Details";

    protected override string SingularName => "action";

    protected override string NameKey => ActionNameKey;

    /// <summary>A name, the placeholder glyph and a Direct activator on the caster -- an action's activator is required, so a new one decodes and passes every check at once.</summary>
    protected override JsonObject NewRecordFields(string name) => new()
    {
        [ActionNameKey] = name,
        [ActivatableContent.GlyphKey] = ActivatablePlaceholder.Glyph,
        [ActivatableContent.ActivatorKey] = ActivatablePlaceholder.SelfActivator(),
    };

    protected override IReadOnlyList<RecordBrowserSection<ActionDefinition>> SectionsOf(IEnumerable<ActionDefinition> definitions, string search) =>
        ActionBrowserSections.Build(definitions, search, Workspace.Session.Catalogs.GameplayTags);

    /// <summary>Type (the browser's own headings), Timing (FreeCast, Immediate, Delayed) and Core, each any of.</summary>
    protected override IReadOnlyList<ListFilterGroup<ContentRecordDocument<ActionDefinition>>> TableFilterGroups =>
    [
        RecordFilterGroups.OneOf<ContentRecordDocument<ActionDefinition>>("type", "Type", ActionBrowserSections.HeadingsInOrder, static document => ActionBrowserSections.HeadingOf(document.LastGood)),
        RecordFilterGroups.OneOf<ContentRecordDocument<ActionDefinition>>("timing", "Timing", ActionBrowserSections.TimingsInOrder, static document => ActionBrowserSections.TimingOf(document.LastGood)),
        RecordFilterGroups.OneOf<ContentRecordDocument<ActionDefinition>>("core", "Core", ActionBrowserSections.CoreChoicesInOrder, static document => ActionBrowserSections.CoreChoiceOf(document.LastGood)),
    ];

    protected override RecordRowAppearance LookOf(ActionDefinition action) => new(action.Name, action.SpriteName, action.Glyph, action.GlyphColor);

    protected override void ShowDefinition(ActionDetailsWindow preview, ActionDefinition action, float contentWidth) =>
        preview.Configure(action, contentWidth);

    /// <summary>Action Details' own comparison colouring: each line better or worse than the other versions'.</summary>
    protected override void ShowComparedDefinition(ActionDetailsWindow preview, ActionDefinition action, float contentWidth, IReadOnlyList<ActionDefinition> comparedAgainst) =>
        preview.Configure(action, contentWidth, comparedAgainst);

    /// <summary>A record standing in for one that doesn't decode: its own name and glyph when it has them as text, and an activator, which an action requires.</summary>
    public static string PlaceholderJsonOf(EditableContentRecord record) => ActivatablePlaceholder.JsonOf(record, "Action", withActivator: true);
}
