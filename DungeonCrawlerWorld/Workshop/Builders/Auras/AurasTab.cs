using Builders.Browsing;
using Builders.Editing;
using Builders.References;
using Game.Content;
using Game.Modules.Auras;
using Presentation.UI.Details;
using Workshop;

namespace Builders.Auras;

/// <summary>The Auras tab: every aura the session's content names, grouped by what puts it in the world, the selected one in Aura Details, and what grants or radiates it.</summary>
/// <remarks>The catalog holds every aura the build's content names (AuraContentRegistration), terrain and blueprints included. An aura has no sprite, so its row shows a swatch of its glow colour. Reads the workspace's session, and reopens on it whenever it is rebuilt.</remarks>
public sealed class AurasTab : RecordBrowserTab<AuraDefinition, AuraDetailsWindow>
{
    public const string TabTitle = "Auras";

    private const string GlowSwatchGlyph = "●";

    private readonly ContentWorkspace _workspace;

    public AurasTab(ContentWorkspace workspace)
    {
        _workspace = workspace;
        workspace.SessionReplaced += Reopen;
    }

    private AuraCatalog AuraCatalog => _workspace.Session.Catalogs.Auras;

    public override string Title => TabTitle;

    protected override string BrowserTitle => $"Auras ({AuraCatalog.Count})";

    protected override string SearchGhostText => "Search auras";

    protected override string PreviewTitle => "Aura Details";

    protected override IEnumerable<AuraDefinition> AllRecords
    {
        get
        {
            var auraCatalog = AuraCatalog;
            for (var auraId = 0; auraId < auraCatalog.Count; auraId++)
            {
                yield return auraCatalog.Get((byte)auraId);
            }
        }
    }

    protected override Guid IdOf(AuraDefinition aura) => aura.Id;

    protected override IReadOnlyList<RecordBrowserSection<AuraDefinition>> BuildSections(string search) =>
        AuraBrowserSections.Build(AllRecords, _workspace.References, search, _workspace.Session.Catalogs.GameplayTags);

    protected override RecordRowAppearance AppearanceOf(AuraDefinition aura) => new(_workspace.Origins.Label(GameContent.AurasTable, aura.Id, aura.Name), null, GlowSwatchGlyph, aura.GlowColor);

    protected override void ShowInPreview(AuraDetailsWindow preview, AuraDefinition aura, float contentWidth) =>
        preview.Configure(aura, contentWidth);

    protected override IReadOnlyList<WorkshopRelatedRecord> UsesOf(AuraDefinition aura) =>
        ContentLinks.UsesOf(_workspace.References, ContentRecordKind.Aura, aura.Id);

    protected override IReadOnlyList<WorkshopRelatedRecord> UsedByOf(AuraDefinition aura) =>
        ContentLinks.UsedByOf(_workspace.References, ContentRecordKind.Aura, aura.Id);
}
