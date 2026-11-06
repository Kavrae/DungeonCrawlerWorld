using System.Text;
using Engine.Content;
using Engine.Persistence;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Inventory;

namespace Game.Content;

/// <summary>The definitions a game session creates or overrides, kept in its save: actions, items and auras, each a ContentStoreTable over the session's catalog.</summary>
/// <remarks>
/// The save's Definitions folder is the top layer the session was built over, so everything it already holds is in the
/// catalogs from the start. Every call writes through the save folder's one queue. A new definition (a crafted spell or
/// item, and its aura) is an addition and is registered in the same session; an override, or its removal, takes effect
/// when the next session loads the save, since no definition changes during a session. The master and every mod are
/// read again, without the save, the first time something needs a lower layer's record: an override's target, or what a
/// removed override brings back.
/// </remarks>
public sealed class ContentStore
{
    private static readonly Guid ActionIdNamespace = new("1f6d2b8a-4c3e-4a71-9e5d-2b8c6f1a7d40");
    private static readonly Guid ItemIdNamespace = new("6a2e9c41-7b5d-4f08-8c3a-1d9e4b6f2a51");
    private static readonly Guid AuraIdNamespace = new("c3b8f1d2-5e6a-4b97-a0c4-7f2d1e8b9a62");

    private readonly Lazy<GameContent> _lowerContent;

    /// <param name="context">The session the save's layer was built into: its content, catalogs and checks.</param>
    public ContentStore(GameModuleContext context, SaveFolder save)
    {
        Save = save;
        var content = context.Content;
        var types = context.ContentTypes;
        var lowerLayers = new ContentLayers([.. content.Layers.Layers.Where(layer => layer.Kind is ContentLayerKind.Master or ContentLayerKind.Mod)]);
        _lowerContent = new Lazy<GameContent>(() => GameContent.OpenCollectingFailures(lowerLayers, types));
        Checks = new ContentChecks(context.GameplayTags, context.Auras, context.Terrain);
        var references = new CatalogAuraReferences(context.Auras, content);

        var actionCatalog = context.Actions;
        Actions = new ContentStoreTable<ActionDefinition>(
            new ContentStoreTableRules<ActionDefinition>(GameContent.ActionsTable, GameContent.ActionsFileName, "action", ActionIdNamespace,
                static action => action.Id, static (action, id) => action with { Id = id }, actionCatalog.TryGet, actionCatalog.Register, Checks.ProblemsOf),
            save, types, references, (Guid id, out ActionDefinition action) => _lowerContent.Value.TryLoadAction(id, out action), content.OverridingRecordIdsIn(save.DefinitionsLayer, GameContent.ActionsTable));

        var itemCatalog = context.Items;
        Items = new ContentStoreTable<ItemDefinition>(
            new ContentStoreTableRules<ItemDefinition>(GameContent.ItemsTable, GameContent.ItemsFileName, "item", ItemIdNamespace,
                static item => item.Id, static (item, id) => item with { Id = id }, itemCatalog.TryGet, itemCatalog.Register, Checks.ProblemsOf),
            save, types, references, (Guid id, out ItemDefinition item) => _lowerContent.Value.TryLoadItem(id, out item), content.OverridingRecordIdsIn(save.DefinitionsLayer, GameContent.ItemsTable));

        var auraCatalog = context.Auras;
        Auras = new ContentStoreTable<AuraDefinition>(
            new ContentStoreTableRules<AuraDefinition>(GameContent.AurasTable, GameContent.AurasFileName, "aura", AuraIdNamespace,
                static aura => aura.Id, static (aura, id) => aura with { Id = id }, (Guid id, out AuraDefinition aura) => TryGetRegisteredAura(auraCatalog, content, id, out aura), aura => auraCatalog.Register(aura), Checks.ProblemsOf),
            save, types, references, (Guid id, out AuraDefinition aura) => _lowerContent.Value.TryLoadAura(id, out aura), content.OverridingRecordIdsIn(save.DefinitionsLayer, GameContent.AurasTable));
    }

    public SaveFolder Save { get; }

    /// <summary>The checks every definition is put through before it is saved.</summary>
    public ContentChecks Checks { get; }

    public ContentStoreTable<ActionDefinition> Actions { get; }

    public ContentStoreTable<ItemDefinition> Items { get; }

    public ContentStoreTable<AuraDefinition> Auras { get; }

    /// <summary>How many drafts the save's Drafts table fileName holds: each distinct Guid among its lines.</summary>
    public static int DraftCountIn(SaveFolder save, string fileName)
    {
        var path = save.DraftsPathOf(fileName);
        if (!File.Exists(path))
        {
            return 0;
        }

        var ids = new HashSet<Guid>();
        foreach (var line in File.ReadAllText(path, Encoding.UTF8).Split('\n').Skip(1))
        {
            if (ContentTable.TryReadId(line.TrimEnd('\r'), out var id))
            {
                ids.Add(id);
            }
        }

        return ids.Count;
    }

    /// <summary>The aura id names in the session: the catalog's, else the session's content's (one an earlier session saved that nothing has loaded yet), which is then registered.</summary>
    private static bool TryGetRegisteredAura(AuraCatalog auras, GameContent content, Guid id, out AuraDefinition aura)
    {
        if (TryGetAura(auras, id, out aura))
        {
            return true;
        }

        if (content.TryLoadAura(id, out aura))
        {
            auras.Register(aura);
            return true;
        }

        return false;
    }

    private static bool TryGetAura(AuraCatalog auras, Guid id, out AuraDefinition aura)
    {
        if (auras.TryGetId(id, out var auraId))
        {
            aura = auras.Get(auraId);
            return true;
        }

        aura = null!;
        return false;
    }

    /// <summary>A draft's auras, resolved by Guid against the session's catalog, else its content (an aura nothing loaded yet).</summary>
    private sealed class CatalogAuraReferences(AuraCatalog auras, GameContent content) : IContentReferences
    {
        public bool TryResolve<T>(Guid id, out T value)
            where T : class
        {
            if (typeof(T) == typeof(AuraDefinition) && (TryGetAura(auras, id, out var aura) || content.TryLoadAura(id, out aura)))
            {
                value = (T)(object)aura;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
