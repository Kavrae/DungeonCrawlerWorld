using Builders.Browsing;
using Builders.References;
using Engine.Tags;
using Game.Modules.Auras;
using Presentation.UI.Details;
using Workshop;

namespace Builders.Auras;

/// <summary>The Auras tab: every aura the session's content names, grouped by what puts it in the world, the selected one in Aura Details, and what grants or radiates it.</summary>
/// <remarks>The catalog holds every aura the build's content names (AuraContentRegistration), terrain and blueprints included. An aura has no sprite, so its row shows a swatch of its glow colour.</remarks>
public sealed class AurasTab(AuraCatalog auraCatalog, ContentReferenceIndex references, GameplayTagRegistry gameplayTags) : RecordBrowserTab<AuraDefinition, AuraDetailsWindow>
{
    public const string TabTitle = "Auras";

    private const string GlowSwatchGlyph = "●";

    public override string Title => TabTitle;

    protected override string BrowserTitle => $"Auras ({auraCatalog.Count})";

    protected override string SearchGhostText => "Search auras";

    protected override string PreviewTitle => "Aura Details";

    protected override IEnumerable<AuraDefinition> AllRecords
    {
        get
        {
            for (var auraId = 0; auraId < auraCatalog.Count; auraId++)
            {
                yield return auraCatalog.Get((byte)auraId);
            }
        }
    }

    protected override Guid IdOf(AuraDefinition aura) => aura.Id;

    protected override IReadOnlyList<RecordBrowserSection<AuraDefinition>> BuildSections(string search) =>
        AuraBrowserSections.Build(AllRecords, references, search, gameplayTags);

    protected override RecordRowAppearance AppearanceOf(AuraDefinition aura) => new(aura.Name, null, GlowSwatchGlyph, aura.GlowColor);

    protected override void ShowInPreview(AuraDetailsWindow preview, AuraDefinition aura, float contentWidth) =>
        preview.Configure(aura, contentWidth);

    protected override IReadOnlyList<RecordLinkSection> LinksOf(AuraDefinition aura) =>
        ContentLinks.For(references, ContentRecordKind.Aura, aura.Id);

    protected override IReadOnlyList<WorkshopDependent> DependentsOf(AuraDefinition aura) =>
        ContentLinks.DependentsOf(references, ContentRecordKind.Aura, aura.Id);
}
