using System.Text.Json;
using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.Sprites;

/// <summary>
/// Named sprite lookup, backed by Content/SpriteManifest.json (authored via
/// DevTools/SpriteManifestBuilder, not hand-edited) rather than hardcoded C# entries -- swapping
/// the data source didn't require touching SpriteComponent or any renderer, exactly as intended
/// when this type was still a hardcoded static registry. Entries with no matching asset (Fairy,
/// Ghost, Lava) are simply absent from the file -- those blueprints stay glyph-only via
/// MapWindow's fallback.
/// </summary>
public static class SpriteManifest
{
    private const string ManifestFileName = "SpriteManifest.json";

    private static readonly IReadOnlyDictionary<string, SpriteManifestEntry> Entries = Load();

    /// <summary>
    /// Looks up name and, if found, picks one of its candidate cells at random. For callers that
    /// want variety across many instances of the same name -- blueprint Build methods, which call
    /// this once per entity so the roll is baked into that entity's own SpriteComponent rather
    /// than re-rolled every frame. Never call this from a draw path.
    /// </summary>
    /// <remarks>
    /// Rolls through the caller's own MathUtility rather than a static Random of this class's own,
    /// which is what puts variant selection under the session seed (see RandomSeed) along with
    /// everything else the map is built from. This deliberately reverses an earlier decision:
    /// variant choice is purely cosmetic, so it was judged to have no determinism stakes worth
    /// threading a dependency for. Shareable map seeds gave it stakes -- two players on the same
    /// seed would have got the same map built out of different-looking tiles -- and the ripple
    /// that argument rested on turned out to be three blueprints (Wall/Dirt/Grass, plus Shop) and
    /// a handful of call sites, since every other caller already held a MathUtility.
    /// </remarks>
    /// <param name="name">The manifest entry to look up.</param>
    /// <param name="mathUtility">The caller's randomizer -- the shared, seeded one in any real build.</param>
    /// <param name="sprite">The chosen cell as a SpriteComponent, or default on a miss.</param>
    public static bool TryGetRandom(string name, MathUtility mathUtility, out SpriteComponent sprite)
    {
        if (FindCells(name) is { } cells)
        {
            // A single-candidate entry short-circuits rather than rolling a one-outcome roll.
            // Random.Next(0, 1) still advances the randomizer's state, and since this now shares
            // the one seeded MathUtility the whole world is built from, a pointless roll here
            // shifts every subsequent draw -- which showed up immediately as a seeded shop stocking
            // a different number of items purely because Shop.Build had learned to pick a sprite.
            // Most manifest entries hold exactly one cell, so this is also the common path.
            return TryResolve(cells.Count == 1 ? cells[0] : cells[mathUtility.Next(0, cells.Count)], out sprite);
        }

        sprite = default;
        return false;
    }

    /// <summary>
    /// Looks up name and, if found, always returns its first candidate cell, ignoring any others.
    /// For callers that need the same name to resolve to the same visual every time: UI icons
    /// (hotbar slots, inventory cells, folder/button chrome, drag ghosts) and map badges, which
    /// re-resolve by name on every frame and for every element showing that name, and so must not
    /// roll anything.
    /// </summary>
    public static bool TryGetFirst(string name, out SpriteComponent sprite)
    {
        var found = TryResolveFirst(name, out var resolved);
        sprite = resolved.Sprite;
        return found;
    }

    /// <inheritdoc cref="TryGetFirst(string, out SpriteComponent)"/>
    /// <param name="sprite">The cell with its placement and turn/flip, for a caller that draws it on the map.</param>
    public static bool TryResolveFirst(string name, out ManifestSprite sprite)
    {
        if (FindEntry(name) is { } entry)
        {
            return TryResolve(entry, entry.Cells[0], out sprite);
        }

        sprite = default;
        return false;
    }

    /// <summary>How many candidate cells name has -- 0 when the name is absent.</summary>
    public static int CountCells(string name) => FindCells(name)?.Count ?? 0;

    /// <summary>name's candidate cell at index, for callers that rolled the variant themselves and stored it (terrain cells, see TerrainCell.Variant).</summary>
    public static bool TryGetCell(string name, int index, out SpriteComponent sprite)
    {
        var found = TryResolveCell(name, index, out var resolved);
        sprite = resolved.Sprite;
        return found;
    }

    /// <inheritdoc cref="TryGetCell(string, int, out SpriteComponent)"/>
    /// <param name="sprite">The cell with its placement and turn/flip, for a caller that draws it on the map.</param>
    public static bool TryResolveCell(string name, int index, out ManifestSprite sprite)
    {
        if (FindEntry(name) is { } entry && (uint)index < (uint)entry.Cells.Count)
        {
            return TryResolve(entry, entry.Cells[index], out sprite);
        }

        sprite = default;
        return false;
    }

    /// <summary>name's whole entry -- its parts, linking and placement -- for a caller that resolves it once and caches what it needs (TerrainRegistry).</summary>
    public static bool TryGetEntry(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SpriteManifestEntry? entry) =>
        Entries.TryGetValue(name, out entry);

    /// <summary>How many cells name's top part has -- 0 when the name is absent or has no top.</summary>
    public static int CountTopCells(string name) => Entries.TryGetValue(name, out var entry) ? entry.Top?.Cells.Count ?? 0 : 0;

    /// <summary>name's top-part cell at index, a miss for an empty slot of an unfinished set.</summary>
    public static bool TryResolveTopCell(string name, int index, out ManifestSprite sprite)
    {
        if (Entries.TryGetValue(name, out var entry) && entry.Top is { } top && (uint)index < (uint)top.Cells.Count)
        {
            return TryResolve(top, top.Cells[index], out sprite);
        }

        sprite = default;
        return false;
    }

    /// <summary>Deserializes and checks a manifest: a linked entry holds whole sets of 16 cells (a slot may be empty while the set is unfinished, nothing else may), PixelsPerTile is positive, and every placed cell stays inside the reach box at a 1x1 footprint (SpriteReach).</summary>
    /// <exception cref="InvalidDataException">An entry breaks one of those rules; the message names it.</exception>
    public static IReadOnlyDictionary<string, SpriteManifestEntry> Parse(string json)
    {
        var entries = JsonSerializer.Deserialize<List<SpriteManifestEntry>>(json) ?? [];
        foreach (var entry in entries)
        {
            Validate(entry);
        }

        return entries.ToDictionary(static entry => entry.Name);
    }

    /// <remarks>A wall's top is checked raised by its face's height, so the two together stay inside the reach box.</remarks>
    private static void Validate(SpriteManifestEntry entry)
    {
        var face = entry.Face;
        ValidatePart(entry.Name, null, face, raisedRows: 0);

        if (entry.Top is { } top)
        {
            ValidatePart(entry.Name, "top", top, raisedRows: face.HeightRows());
        }
    }

    private static void ValidatePart(string entryName, string? partName, SpriteManifestPart part, int raisedRows)
    {
        var subject = partName is null ? $"Sprite manifest entry '{entryName}'" : $"Sprite manifest entry '{entryName}' ({partName})";

        var cellsPerSet = SpriteLinks.CellsPerSet(part.Linking);
        if (part.Linking != SpriteLinking.None && (part.Cells.Count == 0 || part.Cells.Count % cellsPerSet != 0))
        {
            throw new InvalidDataException(
                $"{subject} is a linked set, so it needs its cells in whole sets of {cellsPerSet} (one per combination of joined sides), but it has {part.Cells.Count}.");
        }

        if (part.Linking == SpriteLinking.Horizontal)
        {
            for (var setStart = 0; setStart < part.Cells.Count; setStart += cellsPerSet)
            {
                if (part.Cells[setStart + SpriteLinks.Standalone] is null)
                {
                    throw new InvalidDataException(
                        $"{subject} has no standalone piece in variant set {setStart / cellsPerSet + 1}; it is the one piece every set needs, since the left, right and middle pieces fall back to it.");
                }
            }
        }

        if (part.VariantWeights is { } weights)
        {
            var setCount = part.Linking == SpriteLinking.None ? 0 : part.Cells.Count / cellsPerSet;
            if (part.Linking == SpriteLinking.None || weights.Count != setCount || weights.Any(weight => weight < 0) || weights.Sum() != SpriteLinks.PercentileRange)
            {
                throw new InvalidDataException(
                    $"{subject} gives its variant sets the chances [{string.Join(", ", weights)}]; a linked part needs one chance per variant set ({setCount}), none negative, adding up to {SpriteLinks.PercentileRange}.");
            }
        }

        if (part.PixelsPerTile is < 1)
        {
            throw new InvalidDataException($"{subject} has PixelsPerTile {part.PixelsPerTile}; it must be at least 1.");
        }

        if (part.Linking == SpriteLinking.None && part.Cells.Contains(null))
        {
            throw new InvalidDataException($"{subject} has an empty cell; only a linked set may leave a slot empty.");
        }

        foreach (var cell in part.Cells)
        {
            if (cell is null)
            {
                continue;
            }

            if (cell.Tint is { } tint && !SpriteTint.TryParse(tint, out _))
            {
                throw new InvalidDataException($"{subject} has a cell tinted '{tint}'; a tint is written #RRGGBB or #RRGGBBAA.");
            }

            if (cell.Rotation is not (0 or 90 or 180 or 270))
            {
                throw new InvalidDataException($"{subject} has a cell rotated {cell.Rotation} degrees; only 0, 90, 180 and 270 are allowed.");
            }

            if (cell.Rotation is 90 or 270 && cell.SourceWidth != cell.SourceHeight)
            {
                throw new InvalidDataException(
                    $"{subject} rotates a {cell.SourceWidth}x{cell.SourceHeight} cell by {cell.Rotation} degrees; a quarter turn needs a square cell.");
            }

            var reach = part.PlacementFor(cell) is { } placement
                ? SpriteReach.Of(cell.SourceWidth, cell.SourceHeight, placement, raisedRows)
                : SpriteReach.OfStretched(raisedRows);
            if (!reach.IsWithinReachBox)
            {
                throw new InvalidDataException(reach.DescribeOutsideReachBox(entryName, partName));
            }
        }
    }

    private static List<SpriteManifestCell?>? FindCells(string name) => FindEntry(name)?.Cells;

    /// <summary>An empty slot of an unfinished linked set is a miss, the same as an absent name.</summary>
    private static bool TryResolve(SpriteManifestCell? cell, out SpriteComponent sprite)
    {
        if (cell is not null)
        {
            sprite = ToSprite(cell);
            return true;
        }

        sprite = default;
        return false;
    }

    /// <remarks>Reads the entry's own placement rather than its Face part, which would allocate on every map draw.</remarks>
    private static bool TryResolve(SpriteManifestEntry entry, SpriteManifestCell? cell, out ManifestSprite sprite)
    {
        if (cell is not null)
        {
            sprite = new ManifestSprite(ToSprite(cell), entry.PlacementFor(cell), SpriteCellTransform.Of(cell), cell.TintColor);
            return true;
        }

        sprite = default;
        return false;
    }

    private static bool TryResolve(SpriteManifestPart part, SpriteManifestCell? cell, out ManifestSprite sprite)
    {
        if (cell is not null)
        {
            sprite = new ManifestSprite(ToSprite(cell), part.PlacementFor(cell), SpriteCellTransform.Of(cell), cell.TintColor);
            return true;
        }

        sprite = default;
        return false;
    }

    /// <summary>The entry for name, or null if the name is absent or its entry holds no cells -- an entry with an empty Cells list is treated as a miss rather than allowed to throw at the indexer.</summary>
    private static SpriteManifestEntry? FindEntry(string name) =>
        Entries.TryGetValue(name, out var entry) && entry.Cells.Count > 0 ? entry : null;

    private static SpriteComponent ToSprite(SpriteManifestCell cell) =>
        new(cell.SheetPath, cell.SourceRectangle);

    private static IReadOnlyDictionary<string, SpriteManifestEntry> Load() =>
        Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ManifestFileName)));
}
