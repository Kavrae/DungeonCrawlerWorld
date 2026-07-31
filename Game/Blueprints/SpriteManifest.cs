using System.Text.Json;
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

    /// <summary>
    /// Purely cosmetic variant selection (which visual an entity's sprite happens to be), not
    /// gameplay-affecting -- deliberately a plain static Random rather than an injected
    /// MathUtility (this codebase's usual convention, see Goblin's own doc comment): Wall/Grass
    /// take no constructor parameters today, and threading MathUtility through them just for
    /// this would ripple into every call site that constructs them for something with zero
    /// determinism stakes. Constructed once, not per-call, so it isn't wasteful either.
    /// </summary>
    private static readonly Random Rng = new();

    private static readonly Dictionary<string, SpriteManifestEntry> Entries = Load();

    /// <summary>Looks up name and, if found, picks one of its candidate cells at random and converts it to a SpriteComponent. Picked once per call -- callers (blueprint Build methods) call this once per entity, so the choice is baked into that entity's own SpriteComponent rather than re-rolled every frame.</summary>
    public static bool TryGet(string name, out SpriteComponent sprite)
    {
        if (Entries.TryGetValue(name, out var entry))
        {
            var cell = entry.Cells[Rng.Next(entry.Cells.Count)];
            sprite = SpriteComponent.FromCell(cell.SheetPath, cell.Column, cell.Row, cell.CellWidth, cell.CellHeight);
            return true;
        }

        sprite = default;
        return false;
    }

    private static Dictionary<string, SpriteManifestEntry> Load()
    {
        var resolvedPath = Path.Combine(AppContext.BaseDirectory, ManifestFileName);
        var json = File.ReadAllText(resolvedPath);
        var entries = JsonSerializer.Deserialize<List<SpriteManifestEntry>>(json) ?? [];

        return entries.ToDictionary(static entry => entry.Name);
    }
}
