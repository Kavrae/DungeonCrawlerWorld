using Engine.Math;
using Engine.Modules;
using Game.Content;
using Game.World;

namespace Game.Areas;

/// <summary>Every area, palette and fixed placement in the build, filled during Configure by AreasModule from the master manifest, and where each was read from.</summary>
public sealed class AreaCatalog
{
    private readonly Dictionary<Guid, ContentRecordLocation> _areaLocations = [];
    private readonly Dictionary<Guid, ContentRecordLocation> _paletteLocations = [];
    private readonly Dictionary<Guid, ContentRecordLocation> _placementLocations = [];

    public Catalog<AreaDefinition> Areas { get; } = new(area => area.Id);

    public Catalog<AreaPalette> Palettes { get; } = new(palette => palette.Id);

    public Catalog<AreaPlacement> Placements { get; } = new(placement => placement.Id);

    /// <param name="location">Where it was read from; null for one made in code.</param>
    public void Register(AreaDefinition area, ContentRecordLocation? location = null)
    {
        Areas.Register(area);
        Remember(_areaLocations, area.Id, location);
    }

    /// <inheritdoc cref="Register(AreaDefinition, ContentRecordLocation?)"/>
    public void Register(AreaPalette palette, ContentRecordLocation? location = null)
    {
        Palettes.Register(palette);
        Remember(_paletteLocations, palette.Id, location);
    }

    /// <inheritdoc cref="Register(AreaDefinition, ContentRecordLocation?)"/>
    public void Register(AreaPlacement placement, ContentRecordLocation? location = null)
    {
        Placements.Register(placement);
        Remember(_placementLocations, placement.Id, location);
    }

    /// <summary>Registers content's palettes, areas and placements in file order, with where each was read from, and each disabled one's replacement as its redirect.</summary>
    public void RegisterAll(MasterContent content)
    {
        foreach (var palette in content.AreaPalettes)
        {
            Register(palette.Value, palette.Location);
        }

        foreach (var area in content.Areas)
        {
            Register(area.Value, area.Location);
        }

        foreach (var placement in content.AreaPlacements)
        {
            Register(placement.Value, placement.Location);
        }

        foreach (var (from, to) in content.AreaPaletteRedirects)
        {
            Palettes.AddRedirect(from, to);
        }

        foreach (var (from, to) in content.AreaRedirects)
        {
            Areas.AddRedirect(from, to);
        }

        foreach (var (from, to) in content.AreaPlacementRedirects)
        {
            Placements.AddRedirect(from, to);
        }
    }

    /// <summary>Removes the area, palette or placement registered under id, whichever it is.</summary>
    public void Remove(Guid id)
    {
        Areas.Remove(id);
        Palettes.Remove(id);
        Placements.Remove(id);
        _areaLocations.Remove(id);
        _paletteLocations.Remove(id);
        _placementLocations.Remove(id);
    }

    /// <summary>The palette registered under id (or redirected to from it), or null.</summary>
    public AreaPalette? FindPalette(Guid id) => Palettes.TryGet(id, out var palette) ? palette : null;

    /// <summary>The world tile of the PlayerSpawn marker in the fixed placements' areas -- exactly one, by AreaContentValidation -- or false when nothing is placed.</summary>
    public bool TryFindPlayerSpawn(out Vector3Int position)
    {
        foreach (var placement in Placements.Definitions)
        {
            if (!Areas.TryGet(placement.Area, out var area))
            {
                continue;
            }

            foreach (var marker in area.Markers)
            {
                if (marker.Kind == AreaMarkerKind.PlayerSpawn)
                {
                    position = new Vector3Int(
                        Neighborhoods.OriginOf(placement.NeighborhoodX) + placement.OffsetX + marker.X,
                        Neighborhoods.OriginOf(placement.NeighborhoodY) + placement.OffsetY + marker.Y,
                        (int)marker.Layer);
                    return true;
                }
            }
        }

        position = default;
        return false;
    }

    public ContentRecordLocation? LocationOfArea(Guid id) => _areaLocations.GetValueOrDefault(id);

    public ContentRecordLocation? LocationOfPalette(Guid id) => _paletteLocations.GetValueOrDefault(id);

    public ContentRecordLocation? LocationOfPlacement(Guid id) => _placementLocations.GetValueOrDefault(id);

    private static void Remember(Dictionary<Guid, ContentRecordLocation> locations, Guid id, ContentRecordLocation? location)
    {
        if (location is null)
        {
            locations.Remove(id);
            return;
        }

        locations[id] = location;
    }
}
