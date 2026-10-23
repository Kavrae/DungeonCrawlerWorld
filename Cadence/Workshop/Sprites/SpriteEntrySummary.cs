using System.Text;
using SpriteFormat;

namespace Workshop.Sprites;

/// <summary>The details pane's text for an entry: what it is, each part's cells, span, placement, chances and tints, and what is wrong with it.</summary>
public static class SpriteEntrySummary
{
    public static string Of(SpriteEntryView entry)
    {
        var text = new StringBuilder();
        text.AppendLine(entry.Kind switch
        {
            SpriteEntryKind.Wall => "A wall: a face and a top.",
            SpriteEntryKind.LinkedSet => $"A linked set ({entry.Entry.Linking}): one piece per combination of joined sides.",
            _ => "Options: the game picks one per entity or cell, by its seed.",
        });

        foreach (var part in entry.Parts)
        {
            text.AppendLine();
            text.AppendLine(PartLine(part));
            text.AppendLine($"  {PlacementLine(part.Part)}");
            if (part.Layout is not null && part.VariantSets.Count > 1)
            {
                var chances = part.Part.VariantWeights is { } weights ? string.Join(" / ", weights.Select(weight => $"{weight}%")) : "equal";
                text.AppendLine($"  Chances: {chances}");
            }

            var tints = part.Part.Cells.OfType<SpriteManifestCell>().Select(cell => cell.Tint).OfType<string>().Distinct().ToList();
            if (tints.Count > 0)
            {
                text.AppendLine($"  Tinted {string.Join(", ", tints)}");
            }
        }

        foreach (var problem in entry.Problems)
        {
            text.AppendLine();
            text.AppendLine($"{problem.Severity}: {problem.Description}");
        }

        return text.ToString().TrimEnd();
    }

    private static string PartLine(SpriteEntryPartView part)
    {
        var cellCount = part.Part.Cells.Count(cell => cell is not null);
        var (columns, rows) = part.Span;
        var cells = part.Layout is { } layout
            ? $"{cellCount} of {part.VariantSets.Count * layout.SlotCount} slots in {part.VariantSets.Count} variant set(s)"
            : $"{cellCount} option(s)";
        var raised = part.RaisedRows > 0 ? $", {part.RaisedRows} row(s) up" : string.Empty;
        return $"{part.Label}: {cells}, span {columns}x{rows}{raised}";
    }

    private static string PlacementLine(SpriteManifestPart part)
    {
        if (part.PixelsPerTile is not { } pixelsPerTile)
        {
            return "Stretched to its footprint";
        }

        var pivot = part.PivotX is null && part.PivotY is null
            ? "pivot at each cell's bottom-centre"
            : $"pivot at ({part.PivotX?.ToString() ?? "centre"}, {part.PivotY?.ToString() ?? "bottom"})";
        return $"{pixelsPerTile} px per tile, {pivot}";
    }
}
