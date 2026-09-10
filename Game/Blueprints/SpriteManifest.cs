using System.Text.Json;
using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.Blueprints;

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

    private static readonly Dictionary<string, SpriteManifestEntry> Entries = Load();

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
        ArgumentNullException.ThrowIfNull(mathUtility);

        if (FindCells(name) is { } cells)
        {
            // A single-candidate entry short-circuits rather than rolling a one-outcome roll.
            // Random.Next(0, 1) still advances the randomizer's state, and since this now shares
            // the one seeded MathUtility the whole world is built from, a pointless roll here
            // shifts every subsequent draw -- which showed up immediately as a seeded shop stocking
            // a different number of items purely because Shop.Build had learned to pick a sprite.
            // Most manifest entries hold exactly one cell, so this is also the common path.
            sprite = ToSprite(cells.Count == 1 ? cells[0] : cells[mathUtility.Next(0, cells.Count)]);
            return true;
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
        if (FindCells(name) is { } cells)
        {
            sprite = ToSprite(cells[0]);
            return true;
        }

        sprite = default;
        return false;
    }

    /// <summary>Cells for name, or null if the name is absent or its entry holds no cells -- an entry with an empty Cells list is treated as a miss rather than allowed to throw at the indexer.</summary>
    private static List<SpriteManifestCell>? FindCells(string name) =>
        Entries.TryGetValue(name, out var entry) && entry.Cells.Count > 0 ? entry.Cells : null;

    private static SpriteComponent ToSprite(SpriteManifestCell cell) =>
        SpriteComponent.FromCell(cell.SheetPath, cell.Column, cell.Row, cell.CellWidth, cell.CellHeight);

    private static Dictionary<string, SpriteManifestEntry> Load()
    {
        var resolvedPath = Path.Combine(AppContext.BaseDirectory, ManifestFileName);
        var json = File.ReadAllText(resolvedPath);
        var entries = JsonSerializer.Deserialize<List<SpriteManifestEntry>>(json) ?? [];

        return entries.ToDictionary(static entry => entry.Name);
    }
}
