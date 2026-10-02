using System.Diagnostics.CodeAnalysis;
using Game.Modules.Core.Components;
using Game.Sprites;

namespace Game.Terrain;

/// <summary>Every terrain definition in the session, by runtime id and by key.</summary>
/// <remarks>
/// Filled during IGameModule.Configure (TerrainModule for the built-ins, and any mod's own), before
/// population and before any system reads it -- the same ordering StatusEffectApplierRegistry
/// relies on. Id 0 is reserved for "no terrain", so a zeroed cell array means an empty map.
///
/// Sprite variants are resolved once per definition, on first use, rather than per cell per frame:
/// the draw path reads a cached array instead of a string-keyed manifest lookup.
/// </remarks>
public sealed class TerrainRegistry
{
    /// <summary>The TypeId of a cell with no terrain.</summary>
    public const ushort None = 0;

    private readonly List<TerrainDefinition?> _definitions = [null];
    private readonly Dictionary<string, ushort> _idsByKey = [];
    private readonly List<SpriteComponent[]?> _spriteVariants = [null];

    /// <summary>Each id's BlocksMovement, flattened out of its definition for the movement hot path.</summary>
    private readonly List<bool> _blocksMovement = [false];

    /// <summary>A registered key was registered again: its id, the definition it held, and the one that replaced it.</summary>
    /// <remarks>For what is derived from a definition and held outside it -- the aura field's reach, an entity's exposure to a contact -- to follow a definition replaced during a session. Nothing subscribes during Configure, when a mod overriding a built-in is the usual cause.</remarks>
    public event Action<ushort, TerrainDefinition, TerrainDefinition>? DefinitionChanged;

    /// <summary>Registers definition and returns its runtime id. Registering a key again replaces that definition in place and keeps its id, so a mod can override a built-in, and raises DefinitionChanged.</summary>
    public ushort Register(TerrainDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (_idsByKey.TryGetValue(definition.Key, out var existingId))
        {
            var previous = _definitions[existingId]!;
            _definitions[existingId] = definition;
            _spriteVariants[existingId] = null;
            _blocksMovement[existingId] = definition.BlocksMovement;
            DefinitionChanged?.Invoke(existingId, previous, definition);
            return existingId;
        }

        if (_definitions.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException($"More than {ushort.MaxValue} terrain definitions registered.");
        }

        var id = (ushort)_definitions.Count;
        _definitions.Add(definition);
        _spriteVariants.Add(null);
        _blocksMovement.Add(definition.BlocksMovement);
        _idsByKey.Add(definition.Key, id);
        return id;
    }

    /// <summary>Whether typeId's definition blocks movement; false for None or an unknown id.</summary>
    public bool BlocksMovement(ushort typeId) => typeId < _blocksMovement.Count && _blocksMovement[typeId];

    /// <summary>typeId's contact, if its definition has one; false for None, an unknown id, or terrain that does nothing to what stands on it.</summary>
    public bool TryGetContact(ushort typeId, [NotNullWhen(true)] out TerrainContact? contact)
    {
        contact = TryGet(typeId, out var definition) ? definition.Contact : null;
        return contact is not null;
    }

    /// <summary>The number of registered definitions, not counting None.</summary>
    public int Count => _definitions.Count - 1;

    public bool TryGet(ushort typeId, out TerrainDefinition definition)
    {
        if (typeId < _definitions.Count && _definitions[typeId] is { } found)
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    public bool TryGetId(string key, out ushort typeId) => _idsByKey.TryGetValue(key, out typeId);

    /// <summary>The id registered under key; throws if nothing is.</summary>
    public ushort GetId(string key) =>
        _idsByKey.TryGetValue(key, out var typeId) ? typeId : throw new KeyNotFoundException($"No terrain registered under '{key}'.");

    /// <summary>A cell of typeId with its sprite variant rolled, the way SpriteManifest.TryGetRandom rolls a blueprint's sprite: a single-variant or glyph-only terrain doesn't draw from mathUtility at all, so seeded maps don't shift.</summary>
    public TerrainCell CreateCell(ushort typeId, Engine.Math.MathUtility mathUtility)
    {
        var variantCount = GetVariantCount(typeId);
        return new TerrainCell(typeId, variantCount > 1 ? (byte)mathUtility.Next(0, System.Math.Min(variantCount, byte.MaxValue + 1)) : (byte)0);
    }

    /// <summary>How many sprite variants typeId has -- 0 for glyph-only terrain or an unknown id.</summary>
    public int GetVariantCount(ushort typeId) => GetSpriteVariants(typeId)?.Length ?? 0;

    /// <summary>The sprite for typeId's variant, if it has sprites. An out-of-range variant (a manifest that shrank) wraps rather than failing.</summary>
    public bool TryGetSprite(TerrainCell cell, out SpriteComponent sprite)
    {
        if (GetSpriteVariants(cell.TypeId) is { Length: > 0 } variants)
        {
            sprite = variants[cell.Variant % variants.Length];
            return true;
        }

        sprite = default;
        return false;
    }

    private SpriteComponent[]? GetSpriteVariants(ushort typeId)
    {
        if (!TryGet(typeId, out var definition) || definition.SpriteName is not { } spriteName)
        {
            return null;
        }

        return _spriteVariants[typeId] ??= ResolveVariants(spriteName);
    }

    private static SpriteComponent[] ResolveVariants(string spriteName)
    {
        var count = SpriteManifest.CountCells(spriteName);
        var variants = new SpriteComponent[count];
        for (var index = 0; index < count; index++)
        {
            SpriteManifest.TryGetCell(spriteName, index, out variants[index]);
        }

        return variants;
    }
}
