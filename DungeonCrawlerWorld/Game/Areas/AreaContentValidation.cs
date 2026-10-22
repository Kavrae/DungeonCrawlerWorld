using Engine.Content;
using Engine.Math;
using Engine.Tags;
using Game.Blueprints;
using Game.Content;
using Game.Modules;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.World;

namespace Game.Areas;

/// <summary>Checks every area, palette and fixed placement in the build against the build's terrain, blueprints and tags, naming the file, line and column of each problem.</summary>
/// <remarks>
/// Run once every module is configured, beside ContentTagValidation, so a broken area fails the load (or a mod's dry
/// run) rather than the neighborhood it is first stamped into. Validate collects every failure, for a tool to list;
/// EnsureValid throws with all of them. A symbol that is undefined, or a floor on Flying, is reported once per symbol
/// and layer, at its first cell, with how many cells share it.
/// </remarks>
public static class AreaContentValidation
{
    private const char FirstSymbol = '!';
    private const char LastSymbol = '~';

    /// <summary>Throws on any problem -- or, in a build that CollectsContentFailures, leaves every record with one out of the catalog and reports it.</summary>
    /// <remarks>Leaving a record out can break what names it (a placement of a left-out area), so a collecting build checks again until nothing more fails.</remarks>
    /// <exception cref="InvalidOperationException">An area, palette or placement has a problem, listing every one.</exception>
    public static void EnsureValid(GameModuleContext context)
    {
        var failures = Validate(context.Areas, context.Terrain, context.Definitions, context.GameplayTags);
        if (failures.Count == 0)
        {
            return;
        }

        if (!context.CollectsContentFailures)
        {
            throw new InvalidOperationException($"The build's areas have problems:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
        }

        while (failures.Count > 0)
        {
            context.ReportContentFailures(failures);
            var failedRecordIds = failures.Select(failure => failure.RecordId).OfType<Guid>().Distinct().ToList();
            if (failedRecordIds.Count == 0)
            {
                return;
            }

            foreach (var recordId in failedRecordIds)
            {
                context.Areas.Remove(recordId);
            }

            failures = Validate(context.Areas, context.Terrain, context.Definitions, context.GameplayTags);
        }
    }

    /// <summary>Every problem with areas' areas, palettes and placements, in catalog order.</summary>
    public static IReadOnlyList<ContentFailure> Validate(AreaCatalog areas, TerrainRegistry terrain, BlueprintRegistry blueprints, GameplayTagRegistry tags)
    {
        var failures = new List<ContentFailure>();
        foreach (var palette in areas.Palettes.Definitions)
        {
            var report = new FailureReport(failures, areas.LocationOfPalette(palette.Id), $"Area palette '{palette.Name}'", palette.Id);
            CheckLegend(palette.Legend, "leg", terrain, blueprints, report);
        }

        foreach (var area in areas.Areas.Definitions)
        {
            var report = new FailureReport(failures, areas.LocationOfArea(area.Id), $"Area '{area.Name}'", area.Id);
            CheckArea(area, areas, terrain, blueprints, tags, report);
        }

        CheckPlacements(areas, failures);
        return failures;
    }

    private static void CheckArea(AreaDefinition area, AreaCatalog areas, TerrainRegistry terrain, BlueprintRegistry blueprints, GameplayTagRegistry tags, FailureReport report)
    {
        foreach (var tag in area.Tags)
        {
            if (!tags.IsDeclared(tag))
            {
                report.Add("tg", $"Tag '{tag}' isn't declared by any module in this build.");
            }
        }

        for (var index = 0; index < area.Palettes.Count; index++)
        {
            if (areas.FindPalette(area.Palettes[index]) is null)
            {
                report.Add($"pal/{index}", $"Palette {ContentGuid.Format(area.Palettes[index])} doesn't exist.");
            }
        }

        CheckLegend(area.Legend, "leg", terrain, blueprints, report);

        if (area.Width < 1 || area.Height < 1)
        {
            report.Add(area.Width < 1 ? "w" : "h", $"The area is {area.Width}x{area.Height}; both must be at least 1.");
            return;
        }

        var legend = AreaLegend.Resolve(area, areas.FindPalette);
        var occupancy = new AreaOccupancy(area.Width, area.Height);
        MarkWalls(area, legend, occupancy);
        foreach (var layer in Enum.GetValues<MapLayer>())
        {
            if (area.Layers.RowsOf(layer) is { } rows)
            {
                CheckRows(area, layer, rows, legend, blueprints, occupancy, report);
            }
        }

        CheckEntities(area, blueprints, occupancy, report);
        CheckMarkers(area, report);
    }

    private static void CheckLegend(IReadOnlyList<AreaCell> legend, string path, TerrainRegistry terrain, BlueprintRegistry blueprints, FailureReport report)
    {
        var symbols = new HashSet<char>();
        for (var index = 0; index < legend.Count; index++)
        {
            var cell = legend[index];
            var cellPath = $"{path}/{index}";
            if (!IsUsableSymbol(cell.Symbol))
            {
                report.Add(cellPath, $"'{cell.Symbol}' can't be a symbol: symbols are printable ASCII, other than space (transparent), '\"' and '\\'.");
            }

            if (!symbols.Add(cell.Symbol))
            {
                report.Add(cellPath, $"Symbol '{cell.Symbol}' is defined twice.");
            }

            if (cell.Terrain is { } terrainKey && !terrain.TryGetId(terrainKey, out _))
            {
                report.Add($"{cellPath}/ter", $"Symbol '{cell.Symbol}' names terrain '{terrainKey}', which isn't registered.");
            }

            if (cell.Structure is { } structureKey && !terrain.TryGetId(structureKey, out _))
            {
                report.Add($"{cellPath}/st", $"Symbol '{cell.Symbol}' names wall '{structureKey}', which isn't registered.");
            }

            if (cell.Structure is not null && cell.ClearsStructure)
            {
                report.Add(cellPath, $"Symbol '{cell.Symbol}' both places a wall and clears one.");
            }

            for (var spawnIndex = 0; spawnIndex < cell.SpawnList.Count; spawnIndex++)
            {
                CheckSpawn(cell.Symbol, cell.SpawnList[spawnIndex], $"{cellPath}/sp/{spawnIndex}", blueprints, report);
            }
        }
    }

    private static void CheckSpawn(char symbol, AreaSpawn spawn, string path, BlueprintRegistry blueprints, FailureReport report)
    {
        var subject = $"Symbol '{symbol}'s spawn";
        var namesBlueprint = spawn.Blueprint is not null;
        var namesChoices = spawn.OneOfList.Count > 0;
        if (namesBlueprint == namesChoices)
        {
            report.Add(path, $"{subject} names a blueprint or a list to choose one of, not both or neither.");
        }

        if (spawn.Blueprint is { } blueprint)
        {
            CheckBlueprint(blueprint, subject, path, blueprints, report);
        }

        for (var index = 0; index < spawn.OneOfList.Count; index++)
        {
            var choice = spawn.OneOfList[index];
            CheckBlueprint(choice.Blueprint, subject, $"{path}/of/{index}", blueprints, report);
            if (choice.Weight < 1)
            {
                report.Add($"{path}/of/{index}", $"{subject} has a choice weighted {choice.Weight}; weights are at least 1.");
            }
        }

        CheckChance(spawn.Chance, subject, path, report);
        CheckSize(spawn.Size, subject, path, report);
    }

    private static void CheckRows(AreaDefinition area, MapLayer layer, IReadOnlyList<string> rows, Dictionary<char, AreaCell> legend, BlueprintRegistry blueprints, AreaOccupancy occupancy, FailureReport report)
    {
        var layerPath = $"lay/{layer}";
        if (rows.Count != area.Height)
        {
            report.Add(layerPath, $"{layer} has {rows.Count} rows; the area is {area.Height} high.");
        }

        var firstProblemCells = new Dictionary<(char Symbol, bool IsFloorOnFlying), (int Row, int X, int Count)>();
        for (var row = 0; row < rows.Count && row < area.Height; row++)
        {
            var text = rows[row];
            var rowPath = $"{layerPath}/{row}";
            if (text.Length != area.Width)
            {
                report.Add(rowPath, $"{layer} row {row} is {text.Length} wide; the area is {area.Width} wide.");
            }

            for (var x = 0; x < text.Length && x < area.Width; x++)
            {
                var symbol = text[x];
                if (symbol == AreaDefinition.TransparentSymbol)
                {
                    continue;
                }

                if (!legend.TryGetValue(symbol, out var cell))
                {
                    CountProblem(firstProblemCells, (symbol, false), row, x);
                    continue;
                }

                if (layer == MapLayer.Flying && cell.Terrain is not null)
                {
                    CountProblem(firstProblemCells, (symbol, true), row, x);
                }

                foreach (var spawn in cell.SpawnList)
                {
                    CheckCellSpawn(area, layer, symbol, spawn, x, row, blueprints, occupancy, report, rowPath);
                }
            }
        }

        foreach (var ((symbol, isFloorOnFlying), (row, x, count)) in firstProblemCells)
        {
            var others = count > 1 ? $" (and {count - 1} more cells)" : string.Empty;
            report.AddAtSymbol($"{layerPath}/{row}", x, isFloorOnFlying
                ? $"Symbol '{symbol}' gives a floor to Flying at ({x}, {row}){others}; Flying has no floor."
                : $"Symbol '{symbol}' at ({x}, {row}) on {layer} isn't in the legend or its palettes{others}.");
        }
    }

    /// <summary>Every cell of every layer whose symbol places a wall, before any footprint is claimed.</summary>
    private static void MarkWalls(AreaDefinition area, Dictionary<char, AreaCell> legend, AreaOccupancy occupancy)
    {
        foreach (var layer in Enum.GetValues<MapLayer>())
        {
            if (area.Layers.RowsOf(layer) is not { } rows)
            {
                continue;
            }

            for (var row = 0; row < rows.Count && row < area.Height; row++)
            {
                var text = rows[row];
                for (var x = 0; x < text.Length && x < area.Width; x++)
                {
                    if (legend.TryGetValue(text[x], out var cell) && cell.Structure is not null)
                    {
                        occupancy.MarkWall(layer, x, row);
                    }
                }
            }
        }
    }

    private static void CountProblem(Dictionary<(char, bool), (int Row, int X, int Count)> firstProblemCells, (char, bool) problem, int row, int x)
    {
        if (firstProblemCells.TryGetValue(problem, out var first))
        {
            firstProblemCells[problem] = first with { Count = first.Count + 1 };
            return;
        }

        firstProblemCells[problem] = (row, x, 1);
    }

    private static void CheckCellSpawn(AreaDefinition area, MapLayer layer, char symbol, AreaSpawn spawn, int x, int row, BlueprintRegistry blueprints, AreaOccupancy occupancy, FailureReport report, string rowPath)
    {
        var choices = spawn.Blueprint is { } blueprint ? new[] { blueprint } : spawn.OneOfList.Select(choice => choice.Blueprint).ToArray();
        foreach (var choice in choices)
        {
            if (!TryResolve(choice, blueprints, out var resolved))
            {
                continue;
            }

            var size = spawn.Size ?? resolved.Size;
            var subject = $"Symbol '{symbol}' at ({x}, {row}) on {layer}";
            if (!Fits(area, x, row, size))
            {
                report.AddAtSymbol(rowPath, x, $"{subject} spawns a {size.X}x{size.Y} footprint that leaves the area.");
                continue;
            }

            if (resolved.NonBlocking.Count == 0 && occupancy.Claim(layer, x, row, size, subject) is { } conflict)
            {
                report.AddAtSymbol(rowPath, x, $"{subject} spawns a blocking footprint over {conflict}.");
            }
        }
    }

    private static void CheckEntities(AreaDefinition area, BlueprintRegistry blueprints, AreaOccupancy occupancy, FailureReport report)
    {
        var placementIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < area.Entities.Count; index++)
        {
            var placement = area.Entities[index];
            var path = $"ent/{index}";
            var subject = $"Placement '{placement.PlacementId}'";
            if (placement.PlacementId.Length == 0)
            {
                report.Add(path, "A placement needs an id.");
            }
            else if (!placementIds.Add(placement.PlacementId))
            {
                report.Add(path, $"{subject} has the same id as another placement in the area.");
            }

            CheckChance(placement.Chance, subject, path, report);
            CheckSize(placement.Size, subject, path, report);

            if (placement.Fallback is { } fallback)
            {
                if (!placement.Unique)
                {
                    report.Add($"{path}/fb", $"{subject} has a fallback but isn't Unique; only a unique placement falls back.");
                }

                CheckBlueprint(fallback, $"{subject}'s fallback", $"{path}/fb", blueprints, report);
            }

            if (!CheckBlueprint(placement.Blueprint, subject, $"{path}/bp", blueprints, report))
            {
                continue;
            }

            var resolved = blueprints.Resolve(blueprints.GetId(placement.Blueprint));
            var size = placement.Size ?? resolved.Size;
            if (!Fits(area, placement.X, placement.Y, size))
            {
                report.Add(path, $"{subject} at ({placement.X}, {placement.Y}) has a {size.X}x{size.Y} footprint that leaves the {area.Width}x{area.Height} area.");
                continue;
            }

            if (resolved.NonBlocking.Count == 0 && occupancy.Claim(placement.Layer, placement.X, placement.Y, size, subject) is { } conflict)
            {
                report.Add(path, $"{subject} has a blocking footprint over {conflict}.");
            }
        }
    }

    private static void CheckMarkers(AreaDefinition area, FailureReport report)
    {
        var markerIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < area.Markers.Count; index++)
        {
            var marker = area.Markers[index];
            var path = $"mk/{index}";
            if (marker.MarkerId.Length == 0)
            {
                report.Add(path, "A marker needs an id.");
            }
            else if (!markerIds.Add(marker.MarkerId))
            {
                report.Add(path, $"Marker '{marker.MarkerId}' has the same id as another marker in the area.");
            }

            if (marker.Kind == AreaMarkerKind.None)
            {
                report.Add(path, $"Marker '{marker.MarkerId}' needs a kind.");
            }

            if (!Fits(area, marker.X, marker.Y, new Vector2Byte(1)))
            {
                report.Add(path, $"Marker '{marker.MarkerId}' at ({marker.X}, {marker.Y}) is outside the {area.Width}x{area.Height} area.");
            }
        }
    }

    private static void CheckPlacements(AreaCatalog areas, List<ContentFailure> failures)
    {
        var placed = new List<(AreaPlacement Placement, AreaDefinition Area, FailureReport Report)>();
        foreach (var placement in areas.Placements.Definitions)
        {
            var report = new FailureReport(failures, areas.LocationOfPlacement(placement.Id), "Area placement", placement.Id);
            if (!areas.Areas.TryGet(placement.Area, out var area))
            {
                report.Add("ar", $"The placement names area {ContentGuid.Format(placement.Area)}, which doesn't exist.");
                continue;
            }

            if (placement.OffsetX < 0 || placement.OffsetY < 0 || placement.OffsetX + area.Width > Neighborhoods.SizeTiles || placement.OffsetY + area.Height > Neighborhoods.SizeTiles)
            {
                report.Add(string.Empty, $"'{area.Name}' ({area.Width}x{area.Height}) at offset ({placement.OffsetX}, {placement.OffsetY}) doesn't fit its {Neighborhoods.SizeTiles}x{Neighborhoods.SizeTiles} neighborhood.");
            }

            foreach (var (other, otherArea, _) in placed)
            {
                if (other.NeighborhoodX == placement.NeighborhoodX && other.NeighborhoodY == placement.NeighborhoodY
                    && placement.OffsetX < other.OffsetX + otherArea.Width && other.OffsetX < placement.OffsetX + area.Width
                    && placement.OffsetY < other.OffsetY + otherArea.Height && other.OffsetY < placement.OffsetY + area.Height)
                {
                    report.Add(string.Empty, $"'{area.Name}' overlaps '{otherArea.Name}', placed in the same neighborhood by {ContentGuid.Format(other.Id)}.");
                }
            }

            placed.Add((placement, area, report));
        }

        if (placed.Count == 0)
        {
            return;
        }

        var playerSpawns = placed.Sum(entry => entry.Area.Markers.Count(marker => marker.Kind == AreaMarkerKind.PlayerSpawn));
        if (playerSpawns == 1)
        {
            return;
        }

        placed[0].Report.Add(string.Empty, $"The placed areas hold {playerSpawns} {nameof(AreaMarkerKind.PlayerSpawn)} markers; exactly one is needed.");
    }

    /// <summary>False, with a failure, when blueprint isn't registered or can't be spawned.</summary>
    private static bool CheckBlueprint(Guid blueprint, string subject, string path, BlueprintRegistry blueprints, FailureReport report)
    {
        if (!blueprints.TryGetId(blueprint, out var id))
        {
            report.Add(path, $"{subject} names blueprint {ContentGuid.Format(blueprint)}, which isn't registered.");
            return false;
        }

        var resolved = blueprints.Resolve(id);
        if (!resolved.IsSpawnable)
        {
            report.Add(path, $"{subject} names blueprint '{resolved.Definition.Name}', which can't be spawned on its own (it has no {string.Join(", ", resolved.Appearance.Missing)}).");
            return false;
        }

        return true;
    }

    private static void CheckChance(byte chance, string subject, string path, FailureReport report)
    {
        if (chance is < 1 or > AreaSpawn.AlwaysChance)
        {
            report.Add(path, $"{subject} has a {chance}% chance; a chance is 1 to {AreaSpawn.AlwaysChance}.");
        }
    }

    private static void CheckSize(Vector2Byte? size, string subject, string path, FailureReport report)
    {
        if (size is { } present && (present.X < 1 || present.Y < 1 || present.X > ResolvedBlueprint.MaximumFootprintTiles || present.Y > ResolvedBlueprint.MaximumFootprintTiles))
        {
            report.Add(path, $"{subject} is {present.X}x{present.Y}; a footprint is 1 to {ResolvedBlueprint.MaximumFootprintTiles} tiles each way.");
        }
    }

    private static bool TryResolve(Guid blueprint, BlueprintRegistry blueprints, out ResolvedBlueprint resolved)
    {
        resolved = null!;
        return blueprints.TryGetId(blueprint, out var id) && blueprints.TryResolve(id, out resolved) && resolved.IsSpawnable;
    }

    private static bool Fits(AreaDefinition area, int x, int y, Vector2Byte size) =>
        x >= 0 && y >= 0 && x + size.X <= area.Width && y + size.Y <= area.Height;

    private static bool IsUsableSymbol(char symbol) => symbol is >= FirstSymbol and <= LastSymbol and not '"' and not '\\';

    /// <summary>Adds failures for one record, at a path within it.</summary>
    private sealed class FailureReport(List<ContentFailure> failures, ContentRecordLocation? location, string label, Guid recordId)
    {
        public void Add(string path, string reason) => AddAt(path, reason, symbolX: null);

        /// <summary>A failure at the symbol x characters into the row at path: the column after the row's opening quote, plus x.</summary>
        public void AddAtSymbol(string path, int x, string reason) => AddAt(path, reason, x);

        private void AddAt(string path, string reason, int? symbolX)
        {
            if (location is null)
            {
                failures.Add(new ContentFailure(label, 0, recordId, reason));
                return;
            }

            var (line, column) = location.PositionOf(path);
            if (column > 0 && symbolX is { } x)
            {
                column += 1 + x;
            }

            failures.Add(new ContentFailure(location.File, line, recordId, reason, column));
        }
    }

    /// <summary>Which blocking footprint or wall holds each cell of each layer, to refuse two in one place.</summary>
    private sealed class AreaOccupancy(int width, int height)
    {
        private readonly Dictionary<MapLayer, string?[]> _holdersByLayer = [];

        public void MarkWall(MapLayer layer, int x, int y) => Cells(layer)[y * width + x] = $"the wall at ({x}, {y})";

        /// <summary>Claims the footprint for holder; what already held one of its cells, or null when none did.</summary>
        public string? Claim(MapLayer layer, int x, int y, Vector2Byte size, string holder)
        {
            var cells = Cells(layer);
            for (var row = y; row < y + size.Y; row++)
            {
                for (var column = x; column < x + size.X; column++)
                {
                    if (cells[row * width + column] is { } existing)
                    {
                        return existing;
                    }
                }
            }

            for (var row = y; row < y + size.Y; row++)
            {
                for (var column = x; column < x + size.X; column++)
                {
                    cells[row * width + column] = holder;
                }
            }

            return null;
        }

        private string?[] Cells(MapLayer layer)
        {
            if (!_holdersByLayer.TryGetValue(layer, out var cells))
            {
                cells = new string?[width * height];
                _holdersByLayer[layer] = cells;
            }

            return cells;
        }
    }
}
