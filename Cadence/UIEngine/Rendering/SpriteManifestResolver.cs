using Microsoft.Xna.Framework;
using SpriteFormat;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace UIEngine.Rendering;

/// <summary>The loaded sprite manifest, resolved for drawing: a sprite name -- with an entity's or a cell's seed -- to the cell it draws as.</summary>
/// <remarks>
/// Built once from the manifest Presentation loads at startup, every entry resolved here into the drawing types. Game only names sprites
/// and hands out seeds; which cell, placement, turn and tint they mean is decided here. An absent name, or a slot an unfinished set leaves
/// empty, is a miss, and the caller draws its glyph. The one place mod entries will merge into, by name, once content-only mods exist.
///
/// Callers pass the same name instances every frame (a definition's SpriteName), so a name is looked up first by reference -- hashing an
/// object address, not the string -- and only a new instance is looked up by value and remembered. Safe to read from any thread. Every sprite on one sheet shares one
/// SpriteSheet, which holds the sheet's texture once loaded.
/// </remarks>
public sealed class SpriteManifestResolver
{
    /// <summary>Mixed into an entity's seed before it picks a variant, so the pick doesn't follow the name's (EntityAppearance).</summary>
    private const uint EntityVariantSalt = 0x85EBCA6B;

    /// <summary>How many name instances are remembered by reference; past it, a caller that builds a new string per lookup is looked up by value rather than growing the table.</summary>
    private const int MaximumNameReferences = 4096;

    private readonly Dictionary<string, ResolvedEntry> _entriesByName = new(StringComparer.Ordinal);
    /// <summary>Name instances already looked up, by reference. Never written once published: a miss publishes a copy, so readers on any thread see a complete table.</summary>
    private Dictionary<string, ResolvedEntry?> _entriesByNameReference = new(NameReferenceComparer.Instance);

    private readonly Lock _nameReferencesLock = new();
    private readonly Dictionary<string, SpriteSheet> _sheetsByPath = new(StringComparer.Ordinal);

    public SpriteManifestResolver(SpriteManifest manifest)
    {
        ManifestProblems = manifest.Problems;

        foreach (var entry in manifest.Entries)
        {
            if (entry.Cells.Count == 0)
            {
                continue;
            }

            var cells = new SpriteView?[entry.Cells.Count];
            for (var index = 0; index < cells.Length; index++)
            {
                if (entry.Cells[index] is { } cell)
                {
                    cells[index] = ToSpriteView(cell, entry.PlacementFor(cell), SheetFor(cell.SheetPath));
                }
            }

            _entriesByName[entry.Name] = new ResolvedEntry(cells, new TerrainSpriteSet(entry, SheetFor));
        }
    }

    /// <summary>Loads and checks the manifest file at path; an entry that breaks a rule is left out and listed in ManifestProblems.</summary>
    /// <exception cref="System.Text.Json.JsonException">The file isn't a manifest's JSON at all.</exception>
    public static SpriteManifestResolver Load(string path) => new(SpriteManifest.Load(path));

    /// <summary>Why each manifest entry left out was left out (SpriteManifest.Problems); a host reports them at startup.</summary>
    public IReadOnlyList<string> ManifestProblems { get; }

    /// <summary>Writes one line per entry the manifest left out, and returns how many there were.</summary>
    public int ReportManifestProblems(TextWriter output)
    {
        foreach (var problem in ManifestProblems)
        {
            output.WriteLine($"Sprite manifest: {problem}");
        }

        return ManifestProblems.Count;
    }

    /// <summary>Whether the manifest has an entry by name with any cells.</summary>
    public bool Contains(string name) => Find(name) is not null;

    /// <summary>The first cell listed under name -- the stable choice, for a sprite that must look the same everywhere it's drawn: icons, badges and chrome.</summary>
    public bool TryGetFirst(string name, out SpriteView sprite)
    {
        if (Find(name) is { } entry && entry.Cells[0] is { } first)
        {
            sprite = first;
            return true;
        }

        sprite = default;
        return false;
    }

    /// <summary>The first cell listed under name, or null when there is none -- TryGetFirst's shape for a caller that keeps the result.</summary>
    public SpriteView? FirstOrNull(string? name) => name is not null && TryGetFirst(name, out var sprite) ? sprite : null;

    /// <summary>The cell an entity of seed draws name as: one of the entry's cells, picked by hashing the seed, so every entity of a blueprint keeps its look whether built or not.</summary>
    public bool TryGetForEntity(string name, uint seed, out SpriteView sprite)
    {
        if (Find(name) is { } entry && entry.Cells[EntityVariant(seed, entry.Cells.Length)] is { } picked)
        {
            sprite = picked;
            return true;
        }

        sprite = default;
        return false;
    }

    /// <summary>name's sprites as terrain draws them: a floor's options, a wall's face and top.</summary>
    public bool TryGetTerrainSprites(string name, [NotNullWhen(true)] out TerrainSpriteSet? sprites)
    {
        sprites = Find(name)?.Terrain;
        return sprites is not null;
    }

    /// <summary>cell in the drawing types, on a sheet of its own, placed by placement (null stretches it to whatever it is drawn into).</summary>
    /// <remarks>For a cell outside a resolver; the resolver's own sprites share one SpriteSheet per path.</remarks>
    public static SpriteView ToSpriteView(SpriteManifestCell cell, SpritePlacement? placement) =>
        ToSpriteView(cell, placement, new SpriteSheet(cell.SheetPath));

    internal static SpriteView ToSpriteView(SpriteManifestCell cell, SpritePlacement? placement, SpriteSheet sheet)
    {
        var source = cell.SourceRectangle;
        Color? tint = cell.TintColor is { } color ? new Color(color.R, color.G, color.B, color.A) : null;
        return new SpriteView(sheet, new Rectangle(source.X, source.Y, source.Width, source.Height), placement, SpriteCellTransform.Of(cell), tint);
    }

    private SpriteSheet SheetFor(string sheetPath)
    {
        if (!_sheetsByPath.TryGetValue(sheetPath, out var sheet))
        {
            sheet = new SpriteSheet(sheetPath);
            _sheetsByPath.Add(sheetPath, sheet);
        }

        return sheet;
    }

    private ResolvedEntry? Find(string name)
    {
        if (Volatile.Read(ref _entriesByNameReference).TryGetValue(name, out var remembered))
        {
            return remembered;
        }

        var entry = _entriesByName.GetValueOrDefault(name);
        RememberNameReference(name, entry);
        return entry;
    }

    private void RememberNameReference(string name, ResolvedEntry? entry)
    {
        lock (_nameReferencesLock)
        {
            var current = _entriesByNameReference;
            if (current.Count >= MaximumNameReferences || current.ContainsKey(name))
            {
                return;
            }

            var grown = new Dictionary<string, ResolvedEntry?>(current, NameReferenceComparer.Instance) { [name] = entry };
            Volatile.Write(ref _entriesByNameReference, grown);
        }
    }

    private static int EntityVariant(uint seed, int count)
    {
        var mixed = (seed ^ EntityVariantSalt) * 0x9E3779B97F4A7C15UL;
        mixed ^= mixed >> 31;
        return (int)(mixed % (uint)count);
    }

    /// <summary>One manifest entry, resolved: its cells for entities and icons, and its sprites as terrain draws them.</summary>
    private sealed record ResolvedEntry(SpriteView?[] Cells, TerrainSpriteSet Terrain);

    private sealed class NameReferenceComparer : IEqualityComparer<string>
    {
        public static readonly NameReferenceComparer Instance = new();

        public bool Equals(string? first, string? second) => ReferenceEquals(first, second);

        public int GetHashCode(string name) => RuntimeHelpers.GetHashCode(name);
    }
}
