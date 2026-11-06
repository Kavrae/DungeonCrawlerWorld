using Engine.Content;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Inventory;

namespace Game.Content;

/// <summary>Loads a by-id build's actions and items into its catalogs as they are needed: a record is decoded from the content, checked, and registered with its auras the first time its Guid is asked for.</summary>
/// <remarks>
/// <para>
/// The catalogs' definition source (IActionDefinitionSource, IItemDefinitionSource), so whatever resolves an id through
/// a catalog finds it whether or not it was loaded. A load the game knew it would need is decoded ahead -- on the worker
/// planning a neighborhood (Prefetch), and for the player's kit during startup -- and only registered on the main thread
/// (Register); a lookup that has to decode on the spot is a load that missed the plan, counted in MissedPlanLoads.
/// </para>
/// <para>
/// A record is checked as it is registered, by the per-record forms of the build's content checks (ContentChecks): one
/// from a save that fails is reported and left out, as a save's record that fails to decode is, and the lower layer's
/// record it overrode is loaded in its place; one from master or a
/// mod can't fail here, since ModValidation's trial build checked all of them whole, and throws if it does.
/// </para>
/// <para>
/// Registering is main-thread work, as every catalog write is; decoding (GameContent) is safe from any thread.
/// </para>
/// </remarks>
public sealed class ContentCatalogLoader : IActionDefinitionSource, IItemDefinitionSource
{
    private readonly Func<GameContent> _content;
    private readonly ActionCatalog _actions;
    private readonly ItemCatalog _items;
    private readonly AuraCatalog _auras;
    private readonly Lazy<ContentChecks> _checks;
    private readonly Action<ContentFailure> _reportFailure;
    private long _missedPlanLoads;

    /// <param name="content">The build's content, opened the first time it is asked for.</param>
    /// <param name="checks">The build's per-record checks, made the first time a record is checked -- after every module is configured.</param>
    /// <param name="reportFailure">Where a save's record that fails its checks is reported.</param>
    public ContentCatalogLoader(Func<GameContent> content, ActionCatalog actions, ItemCatalog items, AuraCatalog auras, Func<ContentChecks> checks, Action<ContentFailure> reportFailure)
    {
        _content = content;
        _actions = actions;
        _items = items;
        _auras = auras;
        _checks = new Lazy<ContentChecks>(checks);
        _reportFailure = reportFailure;
    }

    /// <summary>How many records had to be decoded on the spot, because nothing loaded them ahead: what a plan or the startup kit should have named.</summary>
    public long MissedPlanLoads => Interlocked.Read(ref _missedPlanLoads);

    /// <summary>Starts serving the action catalog: it asks this for every id it doesn't hold, follows the content's redirects, and reads its summaries from the content.</summary>
    public void ServeActions()
    {
        _actions.AddDefinitionSource(this);
        _actions.UseSummaries(() => _content().ActionSummaries);
        foreach (var (from, to) in _content().ActionRedirects)
        {
            _actions.AddRedirect(from, to);
        }
    }

    /// <summary>Starts serving the item catalog, as ServeActions.</summary>
    public void ServeItems()
    {
        _items.AddDefinitionSource(this);
        _items.UseSummaries(() => _content().ItemSummaries);
        foreach (var (from, to) in _content().ItemRedirects)
        {
            _items.AddRedirect(from, to);
        }
    }

    /// <summary>Decodes and checks every record a save layer wins -- auras first, then actions and items -- leaving out each one that fails for the lower layer's record it replaced, so the session's definitions are settled before anything uses one. Main thread, once every module is configured and before the build registers its auras.</summary>
    /// <remarks>
    /// A master or mod record was checked by ModValidation's trial builds; a save's never was, and would otherwise be found
    /// failing only when first used, mid-session. Auras go first, so an action or item naming a failed one decodes with
    /// the record left in its place. The actions and items are registered (they are few); the auras are not, so a save's
    /// crafted auras take a catalog id only when something radiates them.
    /// </remarks>
    public void LoadSaveLayers()
    {
        var content = _content();
        foreach (var layer in content.Layers.Layers)
        {
            if (layer.Kind != ContentLayerKind.Save)
            {
                continue;
            }

            foreach (var auraId in content.RecordIdsIn(layer, GameContent.AurasTable))
            {
                CheckAura(content, auraId);
            }

            var actionIds = content.RecordIdsIn(layer, GameContent.ActionsTable);
            var itemIds = content.RecordIdsIn(layer, GameContent.ItemsTable);
            content.Prefetch(actionIds, itemIds);
            Register(actionIds, itemIds);
        }
    }

    /// <summary>Decodes actionIds and itemIds now, registering nothing. Safe from any thread.</summary>
    public void Prefetch(IEnumerable<Guid> actionIds, IEnumerable<Guid> itemIds) => _content().Prefetch(actionIds, itemIds);

    /// <summary>Registers each of actionIds and itemIds not yet in its catalog, decoding any that aren't yet. Main thread only.</summary>
    public void Register(IEnumerable<Guid> actionIds, IEnumerable<Guid> itemIds)
    {
        foreach (var id in actionIds)
        {
            _actions.TryGet(id, out _);
        }

        foreach (var id in itemIds)
        {
            _items.TryGet(id, out _);
        }
    }

    public bool TryResolveAction(Guid actionId, out ActionDefinition definition)
    {
        var content = _content();
        CountMiss(content, GameContent.ActionsTable, actionId);
        if (!content.TryLoadAction(actionId, out definition))
        {
            return false;
        }

        while (!PassesChecks(content, GameContent.ActionsTable, definition.Id, definition, _checks.Value.ProblemsOf))
        {
            if (!content.TryLoadActionBeneath(definition.Id, out definition))
            {
                definition = null!;
                return false;
            }
        }

        var action = definition;
        RegisterAuras(entry => EffectEntries.ForEach(action, entry));
        _actions.Register(action);
        return true;
    }

    public bool TryResolveItem(Guid itemDefinitionId, out ItemDefinition definition)
    {
        var content = _content();
        CountMiss(content, GameContent.ItemsTable, itemDefinitionId);
        if (!content.TryLoadItem(itemDefinitionId, out definition))
        {
            return false;
        }

        while (!PassesChecks(content, GameContent.ItemsTable, definition.Id, definition, _checks.Value.ProblemsOf))
        {
            if (!content.TryLoadItemBeneath(definition.Id, out definition))
            {
                definition = null!;
                return false;
            }
        }

        var item = definition;
        RegisterAuras(entry => EffectEntries.ForEach(item, entry));
        _items.Register(item);
        return true;
    }

    /// <summary>Decodes aura auraId and checks it, falling back to the lower layer's record while it fails.</summary>
    private void CheckAura(GameContent content, Guid auraId)
    {
        if (!content.TryLoadAura(auraId, out var aura))
        {
            return;
        }

        while (!PassesChecks(content, GameContent.AurasTable, aura.Id, aura, _checks.Value.ProblemsOf))
        {
            if (!content.TryLoadAuraBeneath(aura.Id, out aura))
            {
                return;
            }
        }
    }

    private void CountMiss(GameContent content, string tableName, Guid id)
    {
        if (!content.IsDecoded(tableName, id) && (tableName == GameContent.ActionsTable ? content.HasAction(id) : content.HasItem(id)))
        {
            Interlocked.Increment(ref _missedPlanLoads);
        }
    }

    /// <summary>Whether definition passes its checks; one from a save that doesn't is reported, for its caller to fall back to the record beneath it, and one from master or a mod throws.</summary>
    private bool PassesChecks<T>(GameContent content, string tableName, Guid id, T definition, Func<T, IReadOnlyList<string>> problemsOf)
    {
        var problems = problemsOf(definition);
        if (problems.Count == 0)
        {
            return true;
        }

        content.TryGetLocation(tableName, id, out var location);
        if (location?.Layer.Kind is not ContentLayerKind.Save)
        {
            throw new InvalidOperationException($"{tableName} record {id} fails its checks:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
        }

        foreach (var problem in problems)
        {
            _reportFailure(new ContentFailure(location.File, location.Line, id, problem, Layer: location.Layer.Name));
        }

        return false;
    }

    private void RegisterAuras(Action<Action<IEffectEntry>> forEachEntry) =>
        forEachEntry(entry =>
        {
            foreach (var aura in entry.ReferencedAuras)
            {
                _auras.Register(aura);
            }
        });
}
