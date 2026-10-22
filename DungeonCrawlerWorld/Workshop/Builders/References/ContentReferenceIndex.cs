using Game.Bootstrap;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Auras;

namespace Builders.References;

/// <summary>The kinds of content a reference can come from or point at.</summary>
public enum ContentRecordKind : byte
{
    Action,
    Item,
    Aura,
    Terrain,
    Blueprint,
    Area,
    AreaPalette,
    AreaPlacement,
}

/// <summary>What a reference means, read from the side that holds it.</summary>
public enum ContentReferenceKind : byte
{
    /// <summary>A scroll casts an action.</summary>
    CastsSpell,

    /// <summary>An effect entry names an aura -- an aura source granted by an action or item, or one aura's effects naming another.</summary>
    GrantsAura,

    /// <summary>Terrain or a blueprint radiates an aura of its own.</summary>
    RadiatesAura,

    /// <summary>An area or palette lays a terrain as a floor or a wall.</summary>
    UsesTerrain,

    /// <summary>An area or palette spawns a blueprint -- a placement, a symbol's spawn, a choice or a fallback.</summary>
    PlacesBlueprint,

    /// <summary>An area takes symbols from a palette.</summary>
    UsesPalette,

    /// <summary>A fixed placement puts an area in the world.</summary>
    PlacesArea,
}

/// <summary>One record of the session's content: what kind, its Guid (Guid.Empty for terrain, which is keyed by name), and its name.</summary>
public readonly record struct ContentRecord(ContentRecordKind Kind, Guid Id, string Name);

/// <summary>From names To, in the way Kind says.</summary>
public readonly record struct ContentReference(ContentRecord From, ContentRecord To, ContentReferenceKind Kind);

/// <summary>Every reference between the session's actions, items, auras, terrain and blueprints, asked from either end.</summary>
/// <remarks>
/// Built once from a GameSession's catalogs. Effect entries are walked with EffectEntries, the walk Game's own content
/// checks use, so every entry that names an aura (IEffectEntry.ReferencedAuras) is found wherever it is held. A scroll's
/// spell is its activator's SpellId. Aura identity is the aura's Guid -- two sources of one aura name the same record.
/// </remarks>
public sealed class ContentReferenceIndex
{
    private readonly Dictionary<(ContentRecordKind, Guid), List<ContentReference>> _usesByRecord = [];
    private readonly Dictionary<(ContentRecordKind, Guid), List<ContentReference>> _usedByByRecord = [];

    public ContentReferenceIndex(GameCatalogs catalogs)
    {
        foreach (var action in catalogs.ActionCatalog.Definitions)
        {
            AddActivatableReferences(new ContentRecord(ContentRecordKind.Action, action.Id, action.Name), action, catalogs);
        }

        foreach (var item in catalogs.ItemCatalog.Definitions)
        {
            AddActivatableReferences(new ContentRecord(ContentRecordKind.Item, item.Id, item.Name), item, catalogs);
        }

        for (var auraId = 0; auraId < catalogs.Auras.Count; auraId++)
        {
            var aura = catalogs.Auras.Get((byte)auraId);
            var from = AuraRecord(aura);
            EffectEntries.ForEach(aura.Effects, entry => AddAuraReferences(from, entry, ContentReferenceKind.GrantsAura));
        }

        var terrain = catalogs.Terrain;
        for (var terrainTypeId = 1; terrainTypeId <= terrain.Count; terrainTypeId++)
        {
            if (!terrain.TryGet((ushort)terrainTypeId, out var terrainDefinition))
            {
                continue;
            }

            var from = new ContentRecord(ContentRecordKind.Terrain, Guid.Empty, terrainDefinition.Name);
            if (terrainDefinition.Aura is { } terrainAura)
            {
                Add(new ContentReference(from, AuraRecord(terrainAura.Aura), ContentReferenceKind.RadiatesAura));
            }

            if (terrainDefinition.Contact is { } contact)
            {
                EffectEntries.ForEach(contact.Effects, entry => AddAuraReferences(from, entry, ContentReferenceKind.GrantsAura));
            }
        }

        var blueprints = catalogs.Definitions;
        for (var blueprintId = 1; blueprintId <= blueprints.Count; blueprintId++)
        {
            if (!blueprints.TryGet((ushort)blueprintId, out var blueprint))
            {
                continue;
            }

            var from = new ContentRecord(ContentRecordKind.Blueprint, blueprint.Id, blueprint.Name);
            foreach (var auraGrant in blueprint.Auras)
            {
                Add(new ContentReference(from, AuraRecord(auraGrant.Aura), ContentReferenceKind.RadiatesAura));
            }
        }

        AddAreaReferences(catalogs);
    }

    /// <summary>What the record names, in the order found.</summary>
    public IReadOnlyList<ContentReference> Uses(ContentRecordKind kind, Guid id) =>
        _usesByRecord.TryGetValue((kind, id), out var references) ? references : [];

    /// <summary>What names the record, in the order found.</summary>
    public IReadOnlyList<ContentReference> UsedBy(ContentRecordKind kind, Guid id) =>
        _usedByByRecord.TryGetValue((kind, id), out var references) ? references : [];

    private static ContentRecord AuraRecord(AuraDefinition aura) => new(ContentRecordKind.Aura, aura.Id, aura.Name);

    /// <summary>What each palette and area lays and spawns, the palettes each area takes symbols from, and the area each fixed placement places.</summary>
    private void AddAreaReferences(GameCatalogs catalogs)
    {
        var areas = catalogs.Areas;
        foreach (var palette in areas.Palettes.Definitions)
        {
            AddLegendReferences(new ContentRecord(ContentRecordKind.AreaPalette, palette.Id, palette.Name), palette.Legend, catalogs);
        }

        foreach (var area in areas.Areas.Definitions)
        {
            var from = new ContentRecord(ContentRecordKind.Area, area.Id, area.Name);
            foreach (var paletteId in area.Palettes)
            {
                if (areas.FindPalette(paletteId) is { } palette)
                {
                    Add(new ContentReference(from, new ContentRecord(ContentRecordKind.AreaPalette, palette.Id, palette.Name), ContentReferenceKind.UsesPalette));
                }
            }

            AddLegendReferences(from, area.Legend, catalogs);
            foreach (var placement in area.Entities)
            {
                AddBlueprintReference(from, placement.Blueprint, catalogs);
                if (placement.Fallback is { } fallback)
                {
                    AddBlueprintReference(from, fallback, catalogs);
                }
            }
        }

        foreach (var placement in areas.Placements.Definitions)
        {
            if (areas.Areas.TryGet(placement.Area, out var area))
            {
                var from = new ContentRecord(ContentRecordKind.AreaPlacement, placement.Id, Builders.MapAreas.MapContentRecord.PlacementName(placement, areas));
                Add(new ContentReference(from, new ContentRecord(ContentRecordKind.Area, area.Id, area.Name), ContentReferenceKind.PlacesArea));
            }
        }
    }

    private void AddLegendReferences(ContentRecord from, IReadOnlyList<Game.Areas.AreaCell> legend, GameCatalogs catalogs)
    {
        foreach (var cell in legend)
        {
            AddTerrainReference(from, cell.Terrain, catalogs);
            AddTerrainReference(from, cell.Structure, catalogs);
            foreach (var spawn in cell.SpawnList)
            {
                if (spawn.Blueprint is { } blueprint)
                {
                    AddBlueprintReference(from, blueprint, catalogs);
                }

                foreach (var choice in spawn.OneOfList)
                {
                    AddBlueprintReference(from, choice.Blueprint, catalogs);
                }
            }
        }
    }

    private void AddTerrainReference(ContentRecord from, string? terrainKey, GameCatalogs catalogs)
    {
        if (terrainKey is not null && catalogs.Terrain.TryGetId(terrainKey, out var typeId) && catalogs.Terrain.TryGet(typeId, out var terrain))
        {
            Add(new ContentReference(from, new ContentRecord(ContentRecordKind.Terrain, Guid.Empty, terrain.Name), ContentReferenceKind.UsesTerrain));
        }
    }

    private void AddBlueprintReference(ContentRecord from, Guid blueprintId, GameCatalogs catalogs)
    {
        if (catalogs.Definitions.TryGetId(blueprintId, out var id))
        {
            Add(new ContentReference(from, new ContentRecord(ContentRecordKind.Blueprint, blueprintId, catalogs.Definitions.Get(id).Name), ContentReferenceKind.PlacesBlueprint));
        }
    }

    private void AddActivatableReferences(ContentRecord from, ActivatableDefinition definition, GameCatalogs catalogs)
    {
        if (ActivatorOf(definition) is ScrollActivator scroll && catalogs.ActionCatalog.TryGet(scroll.SpellId, out var spell))
        {
            Add(new ContentReference(from, new ContentRecord(ContentRecordKind.Action, spell.Id, spell.Name), ContentReferenceKind.CastsSpell));
        }

        EffectEntries.ForEach(definition, entry => AddAuraReferences(from, entry, ContentReferenceKind.GrantsAura));
    }

    private static IActionActivator? ActivatorOf(ActivatableDefinition definition) => definition switch
    {
        ActionDefinition action => action.Activator,
        Game.Modules.Inventory.ItemDefinition item => item.Activator,
        _ => null,
    };

    private void AddAuraReferences(ContentRecord from, IEffectEntry entry, ContentReferenceKind kind)
    {
        foreach (var aura in entry.ReferencedAuras)
        {
            Add(new ContentReference(from, AuraRecord(aura), kind));
        }
    }

    /// <summary>Records the reference once, however many entries repeat it.</summary>
    private void Add(ContentReference reference)
    {
        var uses = ListFor(_usesByRecord, reference.From);
        if (uses.Contains(reference))
        {
            return;
        }

        uses.Add(reference);
        ListFor(_usedByByRecord, reference.To).Add(reference);
    }

    private static List<ContentReference> ListFor(Dictionary<(ContentRecordKind, Guid), List<ContentReference>> referencesByRecord, ContentRecord record)
    {
        var key = (record.Kind, record.Id);
        if (!referencesByRecord.TryGetValue(key, out var references))
        {
            references = [];
            referencesByRecord[key] = references;
        }

        return references;
    }
}
