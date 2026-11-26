using System.Text.Json.Nodes;
using Engine.Content;
using Game.Modules.Actions;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>A record that decodes standing in for an action or item that doesn't -- its own name and glyph where it has them as text -- what the browser and preview show of it until it decodes.</summary>
public static class ActivatablePlaceholder
{
    /// <summary>The glyph a record with none of its own is shown with.</summary>
    public const string Glyph = "?";

    /// <summary>The activator a new action starts with and an action's stand-in holds: Direct, Self on the caster, the timing's defaults -- what decodes and passes every check.</summary>
    public static JsonObject SelfActivator() => new()
    {
        ["$"] = "Direct",
        ["tg"] = new JsonObject { ["sh"] = "Self", ["an"] = "Caster" },
        ["tm"] = new JsonObject(),
    };

    /// <summary>record's stand-in: its Guid, its name ("{kindName} {Guid}" when it has none) and its glyph, plus withActivator's activator when the kind requires one (an action).</summary>
    public static string JsonOf(EditableContentRecord record, string kindName, bool withActivator)
    {
        var placeholder = new JsonObject
        {
            [ContentTable.IdKey] = ContentGuid.Format(record.Id),
            [ActivatableContent.NameKey] = TextOf(record, ActivatableContent.NameKey) ?? $"{kindName} {ContentGuid.Format(record.Id)}",
            [ActivatableContent.GlyphKey] = TextOf(record, ActivatableContent.GlyphKey) ?? Glyph,
        };

        if (withActivator)
        {
            placeholder[ActivatableContent.ActivatorKey] = SelfActivator();
        }

        return placeholder.ToJsonString();
    }

    private static string? TextOf(EditableContentRecord record, string key) =>
        record.Root[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
}
