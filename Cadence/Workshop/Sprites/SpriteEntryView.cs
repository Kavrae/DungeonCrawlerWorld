using System.Security.Cryptography;
using System.Text;
using SpriteFormat;

namespace Workshop.Sprites;

/// <summary>What a manifest entry is: interchangeable options, a linked set, or a wall (a face and a top).</summary>
public enum SpriteEntryKind : byte
{
    Options,
    LinkedSet,
    Wall,
}

/// <summary>One separately authored picture of an entry as the Sprites tab shows it: the entry itself, or a wall's face or top.</summary>
/// <param name="Label">"Face" or "Top" for a wall's parts, the entry's kind otherwise.</param>
/// <param name="Layout">How its slots join, for a linked part; null for options.</param>
/// <param name="RaisedRows">How many rows above its tile it sits: a wall's top, its face's height; otherwise 0.</param>
public sealed record SpriteEntryPartView(string Label, SpriteManifestPart Part, SpriteLinkLayout? Layout, int RaisedRows)
{
    /// <summary>The span the part was authored at (SpriteEntryRules.SpanOf).</summary>
    public (int Columns, int Rows) Span => SpriteEntryRules.SpanOf(Part);

    /// <summary>Its variant sets, each a slot per mask; one set holding every option for options.</summary>
    public IReadOnlyList<SpriteManifestCell?[]> VariantSets =>
        Layout is { } layout
            ? [.. Part.Cells.Chunk(layout.SlotCount)]
            : [[.. Part.Cells]];

    /// <summary>The cell the placement preview draws: a linked part's required piece (a face's standalone one) or fallback (a top's fully joined one) from its first variant set, else its first cell.</summary>
    public SpriteManifestCell? PreviewCell =>
        (Layout is { } layout && Part.Cells.Count >= layout.SlotCount ? layout.PieceFor([.. Part.Cells.Take(layout.SlotCount)], layout.DefaultSelectedMask) : null)
        ?? Part.Cells.FirstOrDefault(cell => cell is not null);
}

/// <summary>One problem with an entry, as the Error List shows it.</summary>
public sealed record SpriteEntryProblem(WorkshopProblemSeverity Severity, string Description);

/// <summary>A manifest entry as the Sprites tab shows it: its kind, its parts, the sheets it draws from, and what is wrong with it.</summary>
/// <remarks>
/// Errors are what the game refuses at load (SpriteManifestValidation): the entry is left out and its name draws its glyph.
/// Warnings are what a tool marks but the game draws anyway: a cell that doesn't fit its part's span, a sheet that isn't there.
/// </remarks>
/// <param name="NameOccurrence">How many earlier entries in the manifest have the same name: 0 for the first.</param>
public sealed record SpriteEntryView(SpriteManifestEntry Entry, SpriteEntryKind Kind, IReadOnlyList<SpriteEntryPartView> Parts, IReadOnlyList<string> SheetPaths, IReadOnlyList<SpriteEntryProblem> Problems, int NameOccurrence = 0)
{
    public string Name => Entry.Name;

    /// <summary>A stable id from the entry's name and, for a repeated name, which occurrence it is, for the Error List to select it by: manifest entries are named, not numbered.</summary>
    public Guid Id => IdOf(Name, NameOccurrence);

    public static Guid IdOf(string entryName, int nameOccurrence = 0) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes(nameOccurrence == 0 ? entryName : $"{entryName}\n{nameOccurrence}")));

    /// <summary>The views of entries, in manifest order; an entry repeating the name of an earlier one the game loads is an error, as the game leaves it out (SpriteManifest.Parse).</summary>
    /// <param name="sheetExists">Whether a sheet path (relative to the spritesheets folder) is a file there.</param>
    public static IReadOnlyList<SpriteEntryView> Read(IReadOnlyList<SpriteManifestEntry> entries, Func<string, bool> sheetExists)
    {
        var views = new List<SpriteEntryView>(entries.Count);
        var occurrencesByName = new Dictionary<string, int>();
        var loadedNames = new HashSet<string>();
        foreach (var entry in entries)
        {
            var nameOccurrence = occurrencesByName.GetValueOrDefault(entry.Name);
            occurrencesByName[entry.Name] = nameOccurrence + 1;
            var repeatsLoadedName = SpriteManifestValidation.FindProblem(entry) is null && !loadedNames.Add(entry.Name);
            views.Add(Of(entry, sheetExists, nameOccurrence, repeatsLoadedName));
        }

        return views;
    }

    /// <param name="nameOccurrence">See NameOccurrence.</param>
    /// <param name="repeatsLoadedName">Whether an earlier entry the game loads has the same name, so the game leaves this one out.</param>
    public static SpriteEntryView Of(SpriteManifestEntry entry, Func<string, bool> sheetExists, int nameOccurrence = 0, bool repeatsLoadedName = false)
    {
        var face = entry.Face;
        var kind = entry.Top is not null ? SpriteEntryKind.Wall : entry.Linking != SpriteLinking.None ? SpriteEntryKind.LinkedSet : SpriteEntryKind.Options;
        List<SpriteEntryPartView> parts = kind == SpriteEntryKind.Wall
            ? [new("Face", face, LayoutOf(face.Linking), 0), new("Top", entry.Top!, LayoutOf(entry.Top!.Linking), face.HeightRows())]
            : [new(kind == SpriteEntryKind.LinkedSet ? "Linked set" : "Options", face, LayoutOf(face.Linking), 0)];

        var sheetPaths = parts.SelectMany(part => part.Part.Cells).OfType<SpriteManifestCell>().Select(cell => cell.SheetPath).Distinct(StringComparer.Ordinal).ToList();
        return new SpriteEntryView(entry, kind, parts, sheetPaths, ProblemsOf(entry, parts, sheetPaths, sheetExists, repeatsLoadedName), nameOccurrence);
    }

    private static SpriteLinkLayout? LayoutOf(SpriteLinking linking) => linking switch
    {
        SpriteLinking.Cardinal => SpriteLinkLayout.Cardinal,
        SpriteLinking.Horizontal => SpriteLinkLayout.Horizontal,
        _ => null,
    };

    private static List<SpriteEntryProblem> ProblemsOf(SpriteManifestEntry entry, IReadOnlyList<SpriteEntryPartView> parts, IReadOnlyList<string> sheetPaths, Func<string, bool> sheetExists, bool repeatsLoadedName)
    {
        var problems = new List<SpriteEntryProblem>();
        if (SpriteManifestValidation.FindProblem(entry) is { } refusal)
        {
            problems.Add(new SpriteEntryProblem(WorkshopProblemSeverity.Error, $"{refusal} The game leaves it out, so its name draws its glyph."));
        }

        if (repeatsLoadedName)
        {
            problems.Add(new SpriteEntryProblem(WorkshopProblemSeverity.Error, $"Sprite '{entry.Name}' repeats the name of an earlier entry. Each name is one sprite, so the game leaves this one out and draws the earlier one."));
        }

        foreach (var part in parts)
        {
            if (SpriteEntryRules.CountMisfits(part.Part) is var misfits and > 0)
            {
                var (columns, rows) = part.Span;
                var subject = parts.Count > 1 ? $"'{entry.Name}' ({part.Label.ToLowerInvariant()})" : $"'{entry.Name}'";
                problems.Add(new SpriteEntryProblem(WorkshopProblemSeverity.Warning, $"Sprite {subject} has {misfits} cell(s) that don't fit its {columns}x{rows} span."));
            }
        }

        foreach (var sheetPath in sheetPaths.Where(path => !sheetExists(path)))
        {
            problems.Add(new SpriteEntryProblem(WorkshopProblemSeverity.Warning, $"Sprite '{entry.Name}' draws from {sheetPath}, which isn't in the spritesheets folder."));
        }

        return problems;
    }
}
