using Builders.Actions;
using Builders.Auras;
using Builders.Browsing;
using Builders.Items;
using Workshop;

namespace Builders.References;

/// <summary>A record's Related window from the reference index: "Uses" (what it names) and "Used by" (what names it), each line opening its record's tab when one shows that kind.</summary>
/// <remarks>Lines read "Kind: Name (how)" -- "Action: Fireball (casts)" under Uses, "Item: Scroll of Fireball (casts it)" under Used by.</remarks>
public static class ContentLinks
{
    public const string UsesHeading = "Uses";
    public const string UsedByHeading = "Used by";

    public static IReadOnlyList<RecordLinkSection> For(ContentReferenceIndex index, ContentRecordKind kind, Guid id) =>
    [
        new RecordLinkSection(UsesHeading, [.. index.Uses(kind, id).Select(reference => LinkTo(reference.To, VerbFor(reference.Kind)))]),
        new RecordLinkSection(UsedByHeading, [.. index.UsedBy(kind, id).Select(reference => LinkTo(reference.From, $"{VerbFor(reference.Kind)} it"))]),
    ];

    /// <summary>The bottom panel's Dependents for a record: everything that names it, with its kind and how -- "casts it", "grants it".</summary>
    public static IReadOnlyList<WorkshopDependent> DependentsOf(ContentReferenceIndex index, ContentRecordKind kind, Guid id) =>
        [.. index.UsedBy(kind, id).Select(reference => new WorkshopDependent(reference.From.Name, reference.From.Kind.ToString(), $"{VerbFor(reference.Kind)} it")
        {
            TabTitle = TabTitleFor(reference.From.Kind),
            RecordId = TabTitleFor(reference.From.Kind) is null ? null : reference.From.Id,
        })];

    /// <summary>The tab that shows records of kind, or null for kinds no tab shows.</summary>
    public static string? TabTitleFor(ContentRecordKind kind) => kind switch
    {
        ContentRecordKind.Action => ActionsTab.TabTitle,
        ContentRecordKind.Item => ItemsTab.TabTitle,
        ContentRecordKind.Aura => AurasTab.TabTitle,
        _ => null,
    };

    private static RecordLink LinkTo(ContentRecord record, string how) =>
        new($"{record.Kind}: {record.Name} ({how})", TabTitleFor(record.Kind), record.Id);

    private static string VerbFor(ContentReferenceKind kind) => kind switch
    {
        ContentReferenceKind.CastsSpell => "casts",
        ContentReferenceKind.GrantsAura => "grants",
        ContentReferenceKind.RadiatesAura => "radiates",
        _ => kind.ToString(),
    };
}
