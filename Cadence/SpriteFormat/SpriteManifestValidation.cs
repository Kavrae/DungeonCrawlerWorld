namespace SpriteFormat;

/// <summary>The rules a manifest entry must keep to be loaded, each as the message that refuses it: the game leaves the entry out at load and reports it, a tool refuses the entry at save.</summary>
/// <remarks>
/// A linked part holds whole sets of cells (a slot may be empty while the set is unfinished, nothing else may); a Horizontal set holds its
/// standalone piece; chances are one per variant set, none negative, adding up to 100; PixelsPerTile is positive; a tint is well formed;
/// a rotation is a quarter turn of a square cell; and every placed cell stays inside the reach box at a 1x1 footprint, a wall's top
/// counted raised by its face's height so the two together stay inside it.
/// </remarks>
public static class SpriteManifestValidation
{
    /// <summary>Why entry can't be loaded, or null when it can.</summary>
    public static string? FindProblem(SpriteManifestEntry entry)
    {
        var face = entry.Face;
        if (FindProblem(entry.Name, null, face, raisedRows: 0) is { } faceProblem)
        {
            return faceProblem;
        }

        return entry.Top is { } top
            ? FindProblem(entry.Name, "top", top, raisedRows: face.HeightRows())
            : null;
    }

    /// <summary>Why one part of an entry can't be loaded, or null when it can.</summary>
    /// <param name="partName">"top" for a wall's top, null for the entry's own cells.</param>
    /// <param name="raisedRows">How many rows above the entry's tile the part sits: a wall's top, its face's height; otherwise 0.</param>
    public static string? FindProblem(string entryName, string? partName, SpriteManifestPart part, int raisedRows)
    {
        var subject = partName is null ? $"Sprite manifest entry '{entryName}'" : $"Sprite manifest entry '{entryName}' ({partName})";

        var cellsPerSet = SpriteLinks.CellsPerSet(part.Linking);
        if (part.Linking != SpriteLinking.None && (part.Cells.Count == 0 || part.Cells.Count % cellsPerSet != 0))
        {
            return $"{subject} is a linked set, so it needs its cells in whole sets of {cellsPerSet} (one per combination of joined sides), but it has {part.Cells.Count}.";
        }

        if (part.Linking == SpriteLinking.Horizontal)
        {
            for (var setStart = 0; setStart < part.Cells.Count; setStart += cellsPerSet)
            {
                if (part.Cells[setStart + SpriteLinks.Standalone] is null)
                {
                    return $"{subject} has no standalone piece in variant set {setStart / cellsPerSet + 1}; it is the one piece every set needs, since the left, right and middle pieces fall back to it.";
                }
            }
        }

        if (part.VariantWeights is { } weights)
        {
            var setCount = part.Linking == SpriteLinking.None ? 0 : part.Cells.Count / cellsPerSet;
            if (part.Linking == SpriteLinking.None || weights.Count != setCount || weights.Any(weight => weight < 0) || weights.Sum() != SpriteLinks.PercentileRange)
            {
                return $"{subject} gives its variant sets the chances [{string.Join(", ", weights)}]; a linked part needs one chance per variant set ({setCount}), none negative, adding up to {SpriteLinks.PercentileRange}.";
            }
        }

        if (part.PixelsPerTile is < 1)
        {
            return $"{subject} has PixelsPerTile {part.PixelsPerTile}; it must be at least 1.";
        }

        if (part.Linking == SpriteLinking.None && part.Cells.Contains(null))
        {
            return $"{subject} has an empty cell; only a linked set may leave a slot empty.";
        }

        foreach (var cell in part.Cells)
        {
            if (cell is null)
            {
                continue;
            }

            if (cell.Tint is { } tint && !SpriteTint.TryParse(tint, out _))
            {
                return $"{subject} has a cell tinted '{tint}'; a tint is written #RRGGBB or #RRGGBBAA.";
            }

            if (cell.Rotation is not (0 or 90 or 180 or 270))
            {
                return $"{subject} has a cell rotated {cell.Rotation} degrees; only 0, 90, 180 and 270 are allowed.";
            }

            if (cell.Rotation is 90 or 270 && cell.SourceWidth != cell.SourceHeight)
            {
                return $"{subject} rotates a {cell.SourceWidth}x{cell.SourceHeight} cell by {cell.Rotation} degrees; a quarter turn needs a square cell.";
            }
        }

        return FindReachProblem(entryName, partName, part, raisedRows);
    }

    /// <summary>The first cell of part outside the reach box at a 1x1 footprint, as its refusal, or null when every cell is inside.</summary>
    public static string? FindReachProblem(string entryName, string? partName, SpriteManifestPart part, int raisedRows)
    {
        foreach (var cell in part.Cells)
        {
            if (cell is null)
            {
                continue;
            }

            var reach = part.PlacementFor(cell) is { } placement
                ? SpriteReach.Of(cell.SourceWidth, cell.SourceHeight, placement, raisedRows)
                : SpriteReach.OfStretched(raisedRows);
            if (!reach.IsWithinReachBox)
            {
                return reach.DescribeOutsideReachBox(entryName, partName);
            }
        }

        return null;
    }
}
