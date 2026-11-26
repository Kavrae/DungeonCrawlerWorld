using System.Text.Json;
using Engine.Content;
using Game.Content;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>
/// What the game adds to the Workshop's generated inspectors: records to pick by name, sprite names and terrain keys to
/// pick from, the tags an activator implies under the declared ones, and an icon drawn among the rows over a definition's
/// appearance (sprite, glyph and colour).
/// </summary>
public sealed class GameInspectorSource(ContentWorkspace workspace, ElementPoolService elementPool, SpriteManifestResolver sprites) : IContentInspectorSource
{
    private const string SpriteNameKey = ActivatableContent.SpriteNameKey;
    private const string GlyphKey = ActivatableContent.GlyphKey;
    private const string GlyphColorKey = ActivatableContent.GlyphColorKey;
    private const string TagsKey = ActivatableContent.TagsKey;
    private const string TerrainKey = TerrainChangeContent.TerrainKey;
    private const string ActivatorKey = ActivatableContent.ActivatorKey;
    private const string ActivationEffectsKey = ActivatableContent.ActivationEffectsKey;
    private const string ScrollSpellKey = ActivatableContent.ScrollSpellKey;
    private const float IconSize = 32f;

    private static readonly HashSet<string> ActivatableTypeNames = new(StringComparer.Ordinal) { nameof(ActionDefinition), nameof(Game.Modules.Inventory.ItemDefinition) };

    private IReadOnlyList<string>? _spriteNames;
    private IReadOnlyList<string>? _terrainKeys;

    /// <summary>A held table's published, enabled records but recordId; for a table not held, every record but it.</summary>
    public IReadOnlyList<ContentReferenceChoice> ReplacementChoices(string tableName, Guid recordId) =>
        workspace.TableNamed(tableName) is { } table
            ? table.ReplacementChoices(recordId)
            : [.. ReferenceChoices(tableName).Where(choice => choice.Id != recordId)];

    public IReadOnlyList<ContentReferenceChoice> ReferenceChoices(string tableName)
    {
        if (workspace.TableNamed(tableName) is { } table)
        {
            return table.ReferenceChoices;
        }

        return SessionContentTables.Named(tableName) is { } sessionTable
            ? [.. sessionTable.Records(workspace.Session).OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase)]
            : [];
    }

    public IReadOnlyList<string>? TextChoices(ContentSchema schema, ContentField field)
    {
        if (field.Key == SpriteNameKey && ActivatableTypeNames.Contains(schema.TypeName))
        {
            return _spriteNames ??= sprites.Names;
        }

        if (field.Key == TerrainKey && schema.TypeName == nameof(TerrainChange))
        {
            return _terrainKeys ??= TerrainKeys();
        }

        return null;
    }

    private IReadOnlyList<string> TerrainKeys()
    {
        var terrain = workspace.Session.Catalogs.Terrain;
        var keys = new List<string>();
        for (var typeId = 1; typeId <= terrain.Count; typeId++)
        {
            if (terrain.TryGet((ushort)typeId, out var definition))
            {
                keys.Add(definition.Key);
            }
        }

        return [.. keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Under an action's or item's Tags, the tags its activator implies; under its Activation effects, what they cost as
    /// declared ("Costs 2 mana"); under a scroll's Spell, what reading it casts ("Casts Heal as it is: Self, Immediate,
    /// without its 2 mana"). Each read from the record as it last decoded.
    /// </summary>
    public string? NoteFor(ContentSchema schema, ContentField field, EditableContentRecord record)
    {
        if (workspace.TableNamed(record.TableName)?.DefinitionOf(record.Id) is not ActivatableDefinition definition)
        {
            return null;
        }

        if (ActivatableTypeNames.Contains(schema.TypeName))
        {
            return field.Key switch
            {
                TagsKey => ImpliedTagsOf(definition) is { Count: > 0 } implied ? $"Implied by the activator: {string.Join(", ", implied)}" : null,
                ActivationEffectsKey => CostText(ActivationCosts.Declared(definition)) is { } cost ? $"Costs {cost}" : null,
                _ => null,
            };
        }

        return schema.TypeName == nameof(ScrollActivator) && field.Key == ScrollSpellKey && definition.ActivatorOrNone is ScrollActivator scroll
            ? CastsText(scroll.Spell)
            : null;
    }

    /// <summary>"Casts Heal as it is: Self, Immediate, without its 2 mana" -- the spell's shape and timing, and the cost a scroll waives.</summary>
    private static string CastsText(ActionDefinition spell)
    {
        var casts = $"Casts {spell.Name} as it is: {spell.Activator.Targeting.Shape}, {spell.Activator.Timing.Category}";
        return CostText(ActivationCosts.Declared(spell)) is { } cost ? $"{casts}, without its {cost}" : casts;
    }

    /// <summary>"2 mana", "1 health", "2 mana and 1 health" -- amounts as declared, never rounded; null for nothing.</summary>
    private static string? CostText(ActivationCostTotals totals)
    {
        if (totals.IsFree)
        {
            return null;
        }

        var parts = new List<string>(2);
        if (totals.Mana > 0)
        {
            parts.Add($"{totals.Mana:0.##} mana");
        }

        if (totals.Health > 0)
        {
            parts.Add($"{totals.Health:0.##} health");
        }

        return string.Join(" and ", parts);
    }

    private static IReadOnlyList<string> ImpliedTagsOf(ActivatableDefinition definition)
    {
        IActionActivator? activator = definition switch
        {
            ActionDefinition action => action.Activator,
            Game.Modules.Inventory.ItemDefinition item => item.Activator,
            _ => null,
        };

        if (activator is null)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var tag in activator.ImpliedTags)
        {
            names.Add(tag.Name);
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>An action's or item's activator right after its glyph colour, before the tags: the tags it implies are noted under them, so it reads first.</summary>
    public IReadOnlyList<ContentField> FieldsInDisplayOrder(ContentSchema schema)
    {
        if (!ActivatableTypeNames.Contains(schema.TypeName) || schema.FieldOf(ActivatorKey) is not { } activator)
        {
            return schema.Fields;
        }

        var fields = schema.Fields.Where(field => field.Key != ActivatorKey).ToList();
        var glyphColor = fields.FindIndex(field => field.Key == GlyphColorKey);
        fields.Insert(glyphColor < 0 ? fields.Count : glyphColor + 1, activator);
        return fields;
    }

    /// <summary>An element of a list of effects is a group ("Group 0"): entries applied together, which later content gives conditions of their own (on a critical hit, on a hit landing).</summary>
    public string ElementTitle(ContentField field, int index, string? typeName) =>
        field.ValueType == typeof(Game.Effects.Effect) ? $"Group {index}"
        : typeName is null ? $"[{index}]" : $"[{index}] {typeName}";

    /// <summary>Changing an action's or item's activator asks first: it changes how the thing is used, and the settings the new activator has no field for go.</summary>
    public string? TypeChangeConfirmation(ContentSchema schema, ContentField field) =>
        field.Key == ActivatorKey && ActivatableTypeNames.Contains(schema.TypeName)
            ? "Changing the activator changes how this is used. Settings the new activator has no field for are dropped (targeting and timing are kept where it has them). Undo puts them back. Change it?"
            : null;

    public ContentInspectorDecoration? DecorationFor(ContentSchema schema) =>
        ActivatableTypeNames.Contains(schema.TypeName)
            ? new ContentInspectorDecoration("Appearance", SpriteNameKey, IconSize, FillAppearancePreview)
            : null;

    /// <summary>The icon the record's sprite name, glyph and glyph colour draw.</summary>
    private void FillAppearancePreview(Window host, JsonElement json, float width)
    {
        var spriteName = json.TryGetProperty(SpriteNameKey, out var sprite) && sprite.ValueKind == JsonValueKind.String ? sprite.GetString() : null;
        var glyph = json.TryGetProperty(GlyphKey, out var glyphJson) && glyphJson.ValueKind == JsonValueKind.String ? glyphJson.GetString()! : string.Empty;
        var color = json.TryGetProperty(GlyphColorKey, out var colorJson) && colorJson.TryGetUInt32(out var packed) ? ContentColors.Unpack(packed) : Color.White;
        var iconSize = new Vector2(IconSize, IconSize);
        var icon = elementPool.CreateElement<IconElement>(host, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = iconSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        icon.Configure(spriteName, glyph, color, iconSize);
        host.AddChild(icon);
    }
}
