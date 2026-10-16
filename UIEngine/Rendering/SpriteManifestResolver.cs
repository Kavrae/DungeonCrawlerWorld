using Microsoft.Xna.Framework;
using SpriteFormat;
using System.Diagnostics.CodeAnalysis;

namespace UIEngine.Rendering;

/// <summary>The loaded sprite manifest, resolved for drawing: a sprite name -- with an entity's or a cell's seed -- to the cell it draws as.</summary>
/// <remarks>
/// Built once from the manifest Presentation loads at startup, every entry resolved here into the drawing types, so a lookup is one
/// dictionary read. Game only names sprites and hands out seeds; which cell, placement, turn and tint they mean is decided here. An
/// absent name, or a slot an unfinished set leaves empty, is a miss, and the caller draws its glyph. The one place mod entries will
/// merge into, by name, once content-only mods exist.
/// </remarks>
public sealed class SpriteManifestResolver
{
    /// <summary>Mixed into an entity's seed before it picks a variant, so the pick doesn't follow the name's (EntityAppearance).</summary>
    private const uint EntityVariantSalt = 0x85EBCA6B;

    private readonly Dictionary<string, SpriteView?[]> _cellsByName = [];
    private readonly Dictionary<string, TerrainSpriteSet> _terrainSpritesByName = [];

    public SpriteManifestResolver(SpriteManifest manifest)
    {
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
                    cells[index] = ToSpriteView(cell, entry.PlacementFor(cell));
                }
            }

            _cellsByName[entry.Name] = cells;
            _terrainSpritesByName[entry.Name] = new TerrainSpriteSet(entry);
        }
    }

    /// <summary>Loads and checks the manifest file at path.</summary>
    /// <exception cref="InvalidDataException">The file breaks a manifest rule; the message names the entry.</exception>
    public static SpriteManifestResolver Load(string path) => new(SpriteManifest.Load(path));

    /// <summary>Whether the manifest has an entry by name with any cells.</summary>
    public bool Contains(string name) => _cellsByName.ContainsKey(name);

    /// <summary>The first cell listed under name -- the stable choice, for a sprite that must look the same everywhere it's drawn: icons, badges and chrome.</summary>
    public bool TryGetFirst(string name, out SpriteView sprite)
    {
        if (_cellsByName.TryGetValue(name, out var cells) && cells[0] is { } first)
        {
            sprite = first;
            return true;
        }

        sprite = default;
        return false;
    }

    /// <summary>The cell an entity of seed draws name as: one of the entry's cells, picked by hashing the seed, so every entity of a blueprint keeps its look whether built or not.</summary>
    public bool TryGetForEntity(string name, uint seed, out SpriteView sprite)
    {
        if (_cellsByName.TryGetValue(name, out var cells) && cells[EntityVariant(seed, cells.Length)] is { } picked)
        {
            sprite = picked;
            return true;
        }

        sprite = default;
        return false;
    }

    /// <summary>name's sprites as terrain draws them: a floor's options, a wall's face and top.</summary>
    public bool TryGetTerrainSprites(string name, [NotNullWhen(true)] out TerrainSpriteSet? sprites) => _terrainSpritesByName.TryGetValue(name, out sprites);

    /// <summary>cell in the drawing types, placed by placement (null stretches it to whatever it is drawn into).</summary>
    /// <remarks>The sheet path is interned: SpriteSheetService caches textures by reference, and the manifest's JSON gives every cell its own copy of the string.</remarks>
    public static SpriteView ToSpriteView(SpriteManifestCell cell, SpritePlacement? placement)
    {
        var source = cell.SourceRectangle;
        Color? tint = cell.TintColor is { } color ? new Color(color.R, color.G, color.B, color.A) : null;
        return new SpriteView(string.Intern(cell.SheetPath), new Rectangle(source.X, source.Y, source.Width, source.Height), placement, SpriteCellTransform.Of(cell), tint);
    }

    private static int EntityVariant(uint seed, int count)
    {
        var mixed = (seed ^ EntityVariantSalt) * 0x9E3779B97F4A7C15UL;
        mixed ^= mixed >> 31;
        return (int)(mixed % (uint)count);
    }
}
