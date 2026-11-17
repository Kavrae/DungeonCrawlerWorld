using Builders.Browsing;
using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Builders.Editing;

/// <summary>One fact a record's browser row is marked with, in the order a row reads them.</summary>
public enum ContentRecordMarker
{
    Unsaved,
    Disabled,
    Replaced,
    Draft,
    Deleted,
    HasErrors,
}

/// <summary>How a marker is drawn and what it says: a glyph (or a sprite once one is drawn for it), its colour, its tooltip, and the label its filter choice reads.</summary>
public sealed record ContentRecordMarkerSymbol(string Glyph, string? SpriteName, Color Color, string Tooltip, string Label)
{
    /// <summary>The glyph in its colour, as a filter menu row and a chip draw it.</summary>
    public GlyphIcon Icon => new(Glyph, Color);
}

/// <summary>How a record's state reads on its browser row: the marker symbols after its name, and whether the row is greyed.</summary>
/// <remarks>SymbolOf is the one place a marker's look and words live; the row and the browser filter's Show only choices both read it.</remarks>
public static class ContentRecordMarkers
{
    private static readonly Color AttentionColor = new(200, 30, 30);
    private static readonly Color UnsavedColor = new(0, 110, 30);
    private static readonly Color NoteColor = Color.Black;

    private static readonly ContentRecordMarkerSymbol UnsavedSymbol = new("✎", null, UnsavedColor, "Unsaved changes", "Unsaved");
    private static readonly ContentRecordMarkerSymbol DisabledSymbol = new("🚫", null, AttentionColor, "Disabled: kept in master, never loaded", "Disabled");
    private static readonly ContentRecordMarkerSymbol ReplacedSymbol = new("↪", null, AttentionColor, "Replaced: disabled, another record loads in its place", "Replaced");
    private static readonly ContentRecordMarkerSymbol DraftSymbol = new("📄", null, NoteColor, "Draft: not published to master", "Draft");
    private static readonly ContentRecordMarkerSymbol DeletedSymbol = new("✕", null, AttentionColor, "Deleted: gone once the deletion is published or saved", "Deleted");
    private static readonly ContentRecordMarkerSymbol HasErrorsSymbol = new("⚠", null, AttentionColor, "Has errors", "Has errors");

    /// <summary>marker's symbol.</summary>
    public static ContentRecordMarkerSymbol SymbolOf(ContentRecordMarker marker) => marker switch
    {
        ContentRecordMarker.Unsaved => UnsavedSymbol,
        ContentRecordMarker.Disabled => DisabledSymbol,
        ContentRecordMarker.Replaced => ReplacedSymbol,
        ContentRecordMarker.Draft => DraftSymbol,
        ContentRecordMarker.Deleted => DeletedSymbol,
        ContentRecordMarker.HasErrors => HasErrorsSymbol,
        _ => throw new ArgumentOutOfRangeException(nameof(marker), marker, null),
    };

    /// <summary>The markers state shows, in reading order.</summary>
    /// <remarks>A marker another one already says is left out: Replaced says Disabled, and Unsaved stands for Draft.</remarks>
    public static IReadOnlyList<ContentRecordMarker> MarkersOf(ContentRecordState state)
    {
        var markers = new List<ContentRecordMarker>(4);
        var isUnsaved = state.HasFlag(ContentRecordState.Unsaved);
        if (isUnsaved)
        {
            markers.Add(ContentRecordMarker.Unsaved);
        }

        if (state.HasFlag(ContentRecordState.Replaced))
        {
            markers.Add(ContentRecordMarker.Replaced);
        }
        else if (state.HasFlag(ContentRecordState.Disabled))
        {
            markers.Add(ContentRecordMarker.Disabled);
        }

        if (state.HasFlag(ContentRecordState.Draft) && !isUnsaved)
        {
            markers.Add(ContentRecordMarker.Draft);
        }

        if (state.HasFlag(ContentRecordState.Deleted))
        {
            markers.Add(ContentRecordMarker.Deleted);
        }

        if (state.HasFlag(ContentRecordState.HasErrors))
        {
            markers.Add(ContentRecordMarker.HasErrors);
        }

        return markers;
    }

    /// <summary>The row symbols for state in reading order, Replaced's tooltip naming replacementName.</summary>
    public static IReadOnlyList<RecordRowMarker> Of(ContentRecordState state, string? replacementName) =>
        [.. MarkersOf(state).Select(marker => RowMarkerOf(marker, replacementName))];

    /// <summary>A disabled or deleted record's row is greyed.</summary>
    public static bool IsDimmed(ContentRecordState state) => (state & (ContentRecordState.Disabled | ContentRecordState.Deleted)) != 0;

    private static RecordRowMarker RowMarkerOf(ContentRecordMarker marker, string? replacementName)
    {
        var symbol = SymbolOf(marker);
        var tooltip = marker == ContentRecordMarker.Replaced && replacementName is not null ? $"Replaced by {replacementName}" : symbol.Tooltip;
        return new RecordRowMarker(symbol.Glyph, symbol.SpriteName, symbol.Color, tooltip);
    }
}
