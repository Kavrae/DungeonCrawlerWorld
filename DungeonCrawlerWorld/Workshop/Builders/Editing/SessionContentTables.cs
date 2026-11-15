using Builders.References;
using Game.Bootstrap;
using Game.Content;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>One table as the session holds it, for what no editing table holds open: its kind of record, whether a Guid is one of its records and what it is called, and every record as a picker lists it.</summary>
/// <param name="NameOf">The record's name in the session; null when the session has no such record.</param>
public sealed record SessionContentTable(string TableName, ContentRecordKind Kind, Func<GameSession, Guid, string?> NameOf, Func<GameSession, IEnumerable<ContentReferenceChoice>> Records);

/// <summary>Every table a record can name (a Reference field's table), read from the session: the one place a table name meets the catalog holding it.</summary>
public static class SessionContentTables
{
    public static IReadOnlyList<SessionContentTable> All { get; } =
    [
        new(GameContent.ActionsTable, ContentRecordKind.Action,
            (session, id) => session.Catalogs.ActionCatalog.TryGet(id, out var action) ? action.Name : null,
            session => session.Catalogs.ActionCatalog.Definitions.Select(action => new ContentReferenceChoice(action.Id, action.Name))),
        new(GameContent.ItemsTable, ContentRecordKind.Item,
            (session, id) => session.Catalogs.ItemCatalog.TryGet(id, out var item) ? item.Name : null,
            session => session.Catalogs.ItemCatalog.Definitions.Select(item => new ContentReferenceChoice(item.Id, item.Name))),
        new(GameContent.AurasTable, ContentRecordKind.Aura,
            (session, id) => session.Catalogs.Auras.TryGetId(id, out var auraId) ? session.Catalogs.Auras.Get(auraId).Name
                : session.Content.TryLoadAura(id, out var aura) ? aura.Name : null,
            session => Enumerable.Range(0, session.Catalogs.Auras.Count).Select(id => session.Catalogs.Auras.Get((byte)id)).Select(aura => new ContentReferenceChoice(aura.Id, aura.Name))),
        new(GameContent.AreasTable, ContentRecordKind.Area,
            (session, id) => session.Catalogs.Areas.Areas.TryGet(id, out var area) ? area.Name : null,
            session => session.Catalogs.Areas.Areas.Definitions.Select(area => new ContentReferenceChoice(area.Id, area.Name))),
    ];

    private static readonly Dictionary<string, SessionContentTable> ByName = All.ToDictionary(table => table.TableName, StringComparer.Ordinal);

    private static readonly Dictionary<ContentRecordKind, SessionContentTable> ByKind = All.ToDictionary(table => table.Kind);

    public static SessionContentTable? Named(string tableName) => ByName.GetValueOrDefault(tableName);

    public static SessionContentTable? Of(ContentRecordKind kind) => ByKind.GetValueOrDefault(kind);
}
