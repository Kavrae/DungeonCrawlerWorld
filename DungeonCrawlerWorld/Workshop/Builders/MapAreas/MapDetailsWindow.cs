using Engine.Content;
using Game.Areas;
using Game.Bootstrap;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Presentation.UI.Details;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Builders.MapAreas;

/// <summary>The Map tab's details pane: an area's size, layers, counts, placements, description and tags; a palette's legend and the areas using it; or a placement's neighborhood, offset and the world cells it covers.</summary>
/// <remarks>The host is WrapContent and tiles its children vertically; every row is built against the width Configure is given.</remarks>
public sealed class MapDetailsWindow(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, GameCatalogs catalogs)
    : Window(fontService, elementPoolService, labelRenderer)
{
    private const float RowHeight = ActivatableDetailsSections.RowHeight;

    private readonly ActivatableDetailsSections _sections = new(elementPoolService, fontService, catalogs.ActionCatalog, catalogs.GameplayTags);

    private MapContentRecord? _record;
    private float _contentWidth;
    private bool _childrenReady;
    private TextWindow? _spawnLine;
    private int? _spawnCount;

    /// <summary>Shows record with every row built to contentWidth -- safe before the first Initialize (the build waits for it) and after (rebuilds at once).</summary>
    public void Configure(MapContentRecord record, float contentWidth)
    {
        _record = record;
        _contentWidth = contentWidth;

        if (_childrenReady)
        {
            Rebuild();
        }
    }

    /// <summary>How many spawns the neighborhood the preview shows planned -- null while none is shown (still building).</summary>
    public void ShowSpawnCount(int? spawnCount)
    {
        _spawnCount = spawnCount;
        _spawnLine?.UpdateText(SpawnText());
    }

    private string SpawnText() => _spawnCount is { } count ? $"Spawns: {count:N0} in the neighborhood shown" : "Spawns: building the neighborhood...";

    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();
        _childrenReady = true;

        Rebuild();
    }

    private void Rebuild()
    {
        ElementPoolService.CloseAllChildren(this);
        _spawnLine = null;
        if (_record is null)
        {
            return;
        }

        var width = _contentWidth - ContentPadding.X * 2;
        if (_record.Area is { } area)
        {
            BuildArea(area, width);
            return;
        }

        if (_record.Palette is { } palette)
        {
            BuildPalette(palette, width);
            return;
        }

        if (_record.Placement is { } placement)
        {
            BuildPlacement(placement, width);
        }
    }

    private void BuildArea(AreaDefinition area, float width)
    {
        var bodyColor = ActivatableDetailsSections.BodyTextColor;
        _sections.BuildNameRow(this, null, "#", Color.LightGray, area.Name, width);
        _sections.BuildDivider(this, width, "Area", 0.125f);
        CreateLine(width, $"{area.Width} x {area.Height}", bodyColor);
        CreateLine(width, $"Layers: {string.Join(", ", Enum.GetValues<MapLayer>().Where(layer => area.Layers.RowsOf(layer) is not null))}", bodyColor);
        CreateLine(width, $"{area.Entities.Count} placements, {area.Markers.Count} markers", bodyColor);
        CreateLine(width, $"{area.Palettes.Count} palettes, {area.Legend.Count} symbols of its own", bodyColor);
        foreach (var placement in catalogs.Areas.Placements.Definitions.Where(placement => placement.Area == area.Id))
        {
            _sections.BuildWrappingTextLine(this, width, $"Placed: {MapContentRecord.PlacementName(placement, catalogs.Areas)}");
        }

        if (!string.IsNullOrWhiteSpace(area.Description))
        {
            _sections.BuildDivider(this, width);
            _sections.BuildWrappingTextLine(this, width, area.Description);
        }

        _sections.BuildTagsSection(this, area.Tags, width);
    }

    private void BuildPalette(AreaPalette palette, float width)
    {
        var bodyColor = ActivatableDetailsSections.BodyTextColor;
        _sections.BuildNameRow(this, null, "%", Color.LightGray, palette.Name, width);
        _sections.BuildDivider(this, width, "Legend", 0.125f);
        foreach (var cell in palette.Legend)
        {
            _sections.BuildWrappingTextLine(this, width, DescribeCell(cell));
        }

        var users = catalogs.Areas.Areas.Definitions.Where(area => area.Palettes.Contains(palette.Id)).Select(area => area.Name).ToList();
        _sections.BuildDivider(this, width, "Used by", 0.125f);
        CreateLine(width, users.Count == 0 ? "No area" : string.Join(", ", users), bodyColor);
    }

    private void BuildPlacement(AreaPlacement placement, float width)
    {
        var bodyColor = ActivatableDetailsSections.BodyTextColor;
        _sections.BuildNameRow(this, null, "+", Color.LightGray, MapContentRecord.PlacementName(placement, catalogs.Areas), width);
        _sections.BuildDivider(this, width, "Placement", 0.125f);
        CreateLine(width, $"Neighborhood ({placement.NeighborhoodX}, {placement.NeighborhoodY})", bodyColor);
        CreateLine(width, $"Offset ({placement.OffsetX}, {placement.OffsetY})", bodyColor);
        if (catalogs.Areas.Areas.TryGet(placement.Area, out var area))
        {
            var originX = Game.World.Neighborhoods.OriginOf(placement.NeighborhoodX) + placement.OffsetX;
            var originY = Game.World.Neighborhoods.OriginOf(placement.NeighborhoodY) + placement.OffsetY;
            _sections.BuildWrappingTextLine(this, width, $"Covers world x {originX} to {originX + area.Width - 1}, y {originY} to {originY + area.Height - 1}");
        }

        _spawnLine = CreateLine(width, SpawnText(), bodyColor);
        CreateLine(width, $"Id {ContentGuid.Format(placement.Id)}", bodyColor);
    }

    /// <summary>"'#': floor Stone floor, wall Stone wall, spawns Goblin (50%)".</summary>
    private string DescribeCell(AreaCell cell)
    {
        var parts = new List<string>();
        if (cell.Terrain is { } terrainKey)
        {
            parts.Add($"floor {TerrainName(terrainKey)}");
        }

        if (cell.Structure is { } structureKey)
        {
            parts.Add($"wall {TerrainName(structureKey)}");
        }

        if (cell.ClearsStructure)
        {
            parts.Add("clears the wall");
        }

        foreach (var spawn in cell.SpawnList)
        {
            var what = spawn.Blueprint is { } blueprint
                ? BlueprintName(blueprint)
                : $"one of {string.Join(" / ", spawn.OneOfList.Select(choice => $"{BlueprintName(choice.Blueprint)} x{choice.Weight}"))}";
            parts.Add(spawn.Chance == AreaSpawn.AlwaysChance ? $"spawns {what}" : $"spawns {what} ({spawn.Chance}%)");
        }

        return $"'{cell.Symbol}': {(parts.Count == 0 ? "keeps what generation made" : string.Join(", ", parts))}";
    }

    private string TerrainName(string key) =>
        catalogs.Terrain.TryGetId(key, out var typeId) && catalogs.Terrain.TryGet(typeId, out var definition) ? definition.Name : key;

    private string BlueprintName(Guid blueprint) =>
        catalogs.Definitions.TryGetId(blueprint, out var id) ? catalogs.Definitions.Get(id).Name ?? ContentGuid.Format(blueprint) : ContentGuid.Format(blueprint);

    private TextWindow CreateLine(float width, string text, Color color)
    {
        var line = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), MinimumSize = new Vector2(width, RowHeight), MaximumSize = new Vector2(width, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
            Text = new TextOptions { Text = text, TextColor = color },
        });
        line.ContentFont = _sections.BodyFont;
        AddChild(line);
        return line;
    }
}
