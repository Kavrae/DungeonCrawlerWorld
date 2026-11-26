using Builders.Actions;
using Builders.Auras;
using Builders.Items;
using Builders.MapAreas;
using Workshop;

namespace Builders.References;

/// <summary>A record's lines in the bottom panel's Uses (what it names) and Used By (what names it), from the reference index, each opening its record's tab when one shows that kind.</summary>
/// <remarks>Each line is the other record's name, kind and how -- "Fireball, Action, casts" under a scroll's Uses; "Scroll of Fireball, Item, casts it" under the spell's Used By.</remarks>
public static class ContentLinks
{
    /// <summary>Everything the record names, with its kind and how -- "casts", "grants".</summary>
    public static IReadOnlyList<WorkshopRelatedRecord> UsesOf(ContentReferenceIndex index, ContentRecordKind kind, Guid id) =>
        [.. index.Uses(kind, id).Select(reference => RowOf(reference.To, UsesText(reference.Kind)))];

    /// <summary>Everything that names the record, with its kind and how -- "casts it", "grants it".</summary>
    public static IReadOnlyList<WorkshopRelatedRecord> UsedByOf(ContentReferenceIndex index, ContentRecordKind kind, Guid id) =>
        [.. index.UsedBy(kind, id).Select(reference => RowOf(reference.From, UsedByText(reference.Kind)))];

    private static WorkshopRelatedRecord RowOf(ContentRecord record, string how) =>
        new(record.Name, record.Kind.ToString(), how)
        {
            TabTitle = TabTitleFor(record.Kind),
            RecordId = TabTitleFor(record.Kind) is null ? null : record.Id,
        };

    /// <summary>The tab that shows records of kind, or null for kinds no tab shows.</summary>
    public static string? TabTitleFor(ContentRecordKind kind) => kind switch
    {
        ContentRecordKind.Action => ActionsTab.TabTitle,
        ContentRecordKind.Item => ItemsTab.TabTitle,
        ContentRecordKind.Aura => AurasTab.TabTitle,
        ContentRecordKind.Area or ContentRecordKind.AreaPalette or ContentRecordKind.AreaPlacement => MapTab.TabTitle,
        _ => null,
    };

    /// <summary>How the record that names one does so, read from the named side: "casts it"; "builds with it, in code" for what only code names.</summary>
    public static string UsedByText(ContentReferenceKind kind) => kind switch
    {
        ContentReferenceKind.BuildsWith => "builds with it, in code",
        ContentReferenceKind.GrantsAction => "grants it, in code",
        ContentReferenceKind.DerivesAction => "derives its own from it, in code",
        _ => $"{VerbFor(kind)} it",
    };

    /// <summary>How a record names another, read from its own side: "casts"; "builds with, in code" for what only code names.</summary>
    public static string UsesText(ContentReferenceKind kind) => kind switch
    {
        ContentReferenceKind.BuildsWith => "builds with, in code",
        ContentReferenceKind.GrantsAction => "grants, in code",
        ContentReferenceKind.DerivesAction => "derives its own from, in code",
        _ => VerbFor(kind),
    };

    private static string VerbFor(ContentReferenceKind kind) => kind switch
    {
        ContentReferenceKind.CastsSpell => "casts",
        ContentReferenceKind.GrantsAura => "grants",
        ContentReferenceKind.RadiatesAura => "radiates",
        ContentReferenceKind.UsesTerrain => "lays",
        ContentReferenceKind.PlacesBlueprint => "places",
        ContentReferenceKind.UsesPalette => "uses",
        ContentReferenceKind.PlacesArea => "places",
        ContentReferenceKind.BuildsWith => "builds with",
        ContentReferenceKind.GrantsAction => "grants",
        ContentReferenceKind.DerivesAction => "derives its own from",
        _ => kind.ToString(),
    };
}
