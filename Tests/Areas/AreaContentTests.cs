using System.Text;
using Engine.Content;
using Engine.Math;
using Engine.Tags;
using Game.Areas;
using Game.Blueprints.Composites;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Core.Components;
using Game.Tags;
using Game.Terrain;

namespace Tests.Areas;

/// <summary>Areas, palettes and placements are written canonically and read back to the same bytes, an area's rows one per line, and the build's checks name the row and column of every problem.</summary>
[TestClass]
public sealed class AreaContentTests
{
    private const string AreaFileName = "Areas/test-area.json";

    private static readonly Lazy<GameBuildPassResult> Build = new(() => BuiltInTestModules.Build(GameBuildPass.CreatePlaceholderMap()));

    private static readonly Guid AreaId = new("3c1e8f62-41a9-4d0b-9a77-5b2f0c6e9d11");
    private static readonly Guid StonePaletteId = new("3c1e8f62-41a9-4d0b-9a77-5b2f0c6e9d12");
    private static readonly Guid GrassPaletteId = new("3c1e8f62-41a9-4d0b-9a77-5b2f0c6e9d13");
    private static readonly Guid PlacementId = new("3c1e8f62-41a9-4d0b-9a77-5b2f0c6e9d14");

    private static ContentTypeRegistry Types => Build.Value.Context.ContentTypes;

    private static AreaCell Wall => new('#', Terrain: BuiltInTerrain.StoneFloorKey, Structure: BuiltInTerrain.StoneWallKey);

    private static AreaCell Floor => new('.', Terrain: BuiltInTerrain.StoneFloorKey, ClearsStructure: true);

    private static AreaDefinition Area(
        IReadOnlyList<string>? ground = null,
        IReadOnlyList<AreaCell>? legend = null,
        IReadOnlyList<Guid>? palettes = null,
        IReadOnlyList<AreaEntityPlacement>? entities = null,
        IReadOnlyList<AreaMarker>? markers = null,
        IReadOnlyList<string>? flying = null,
        GameplayTagSet? tags = null,
        int width = 4,
        int height = 3) =>
        new(
            AreaId,
            "Test area",
            string.Empty,
            tags ?? GameplayTagSet.Create([GameTags.AreaTesting]),
            width,
            height,
            new AreaLayers(ground ?? ["####", "#..#", "####"], Flying: flying),
            legend ?? [Wall, Floor],
            palettes ?? [],
            entities ?? [],
            markers ?? []);

    private static string Write(AreaDefinition area) => ContentDocument.Write(MasterContent.AreasTable, area, Types);

    private static ContentDocumentRecord<AreaDefinition> Read(string text)
    {
        var failures = new List<ContentFailure>();
        var record = ContentDocument.Read<AreaDefinition>(text, AreaFileName, MasterContent.AreasTable, Types, NoContentReferences.Instance, failures);
        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        return record!;
    }

    private static IReadOnlyList<ContentFailure> Validate(string areaText, params AreaPalette[] palettes) => Validate(areaText, palettes, []);

    private static IReadOnlyList<ContentFailure> Validate(string areaText, IReadOnlyList<AreaPalette> palettes, IReadOnlyList<AreaPlacement> placements)
    {
        var record = Read(areaText);
        var catalog = new AreaCatalog();
        foreach (var palette in palettes)
        {
            catalog.Register(palette);
        }

        catalog.Register(record.Value!, new ContentRecordLocation(AreaFileName, 1, record.Source));
        foreach (var placement in placements)
        {
            catalog.Register(placement);
        }

        var context = Build.Value.Context;
        return AreaContentValidation.Validate(catalog, context.Terrain, context.Definitions, context.GameplayTags);
    }

    /// <summary>The 1-based line and column of the first occurrence of needle in text.</summary>
    private static (int Line, int Column) PositionOf(string text, string needle, int skip = 0)
    {
        var start = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.AreNotEqual(-1, start, $"'{needle}' isn't in the text.");
        var index = start + skip;
        var line = text[..index].Count(character => character == '\n') + 1;
        var column = index - (text.LastIndexOf('\n', System.Math.Max(index - 1, 0)) + 1) + 1;
        return (line, column);
    }

    private static ContentFailure Single(IReadOnlyList<ContentFailure> failures, string containing)
    {
        var matching = failures.Where(failure => failure.Reason.Contains(containing, StringComparison.Ordinal)).ToList();
        Assert.HasCount(1, matching, $"Expected one failure containing '{containing}', got:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
        return matching[0];
    }

    [TestMethod]
    public void AnArea_RoundTripsToTheSameBytes_WithEveryFieldWritten()
    {
        var area = Area(
            ground: ["#g #", "#..#", "####"],
            legend:
            [
                Wall,
                Floor,
                new AreaCell('g', Spawns: [new AreaSpawn(Goblin.Id, new Vector2Byte(1, 1), Chance: 50), new AreaSpawn(OneOf: [new AreaWeightedBlueprint(TinyGoblin.Id, 3), new AreaWeightedBlueprint(Goblin.Id)])]),
            ],
            palettes: [StonePaletteId],
            entities: [new AreaEntityPlacement("foreman", 1, 1, GoblinForeman.Id, Unique: true, Fallback: Goblin.Id, Description: "The boss of the room.")],
            markers: [new AreaMarker("spawn", 2, 1, AreaMarkerKind.PlayerSpawn)],
            flying: ["    ", " .  ", "    "]);

        var written = Write(area);
        var rewritten = Write(Read(written).Value!);

        Assert.AreEqual(written, rewritten);
    }

    [TestMethod]
    public void AnAreaFile_HasOneRowPerLine_AndOnePlacementPerLine()
    {
        var area = Area(entities: [new AreaEntityPlacement("foreman", 1, 1, GoblinForeman.Id)]);

        var lines = Write(area).Split('\n');

        CollectionAssert.Contains(lines, "    \"Ground\": [");
        CollectionAssert.Contains(lines, "      \"####\",");
        CollectionAssert.Contains(lines, "      \"#..#\",");
        CollectionAssert.Contains(lines, "      \"####\"");
        Assert.AreEqual(1, lines.Count(line => line.Contains("\"pid\":\"foreman\"", StringComparison.Ordinal)));
        Assert.AreEqual("{", lines[0]);
        Assert.AreEqual("  \"fmt\": 1,", lines[1]);
        Assert.AreEqual($"  \"table\": \"{MasterContent.AreasTable}\",", lines[2]);
    }

    [TestMethod]
    public void PalettesAndPlacements_RoundTripToTheSameBytes()
    {
        var palette = new AreaPalette(StonePaletteId, "Stone", [Wall, Floor]);
        var placement = new AreaPlacement(PlacementId, AreaId, -1, 2, 10, 20);

        var paletteText = ContentTable.Write(MasterContent.AreaPalettesTable, [palette], Types);
        var placementText = ContentTable.Write(MasterContent.AreaPlacementsTable, [placement], Types);
        var failures = new List<ContentFailure>();
        var paletteBack = ContentTable.Read<AreaPalette>(paletteText, "p", MasterContent.AreaPalettesTable, Types, NoContentReferences.Instance, failures).Single().Value!;
        var placementBack = ContentTable.Read<AreaPlacement>(placementText, "q", MasterContent.AreaPlacementsTable, Types, NoContentReferences.Instance, failures).Single().Value!;

        Assert.IsEmpty(failures);
        Assert.AreEqual(paletteText, ContentTable.Write(MasterContent.AreaPalettesTable, [paletteBack], Types));
        Assert.AreEqual(placement, placementBack);
    }

    [TestMethod]
    public void AValidArea_HasNoFailures()
    {
        var failures = Validate(Write(Area(ground: ["####", "#..#", "#..#", "####"], height: 4, entities: [new AreaEntityPlacement("foreman", 1, 1, GoblinForeman.Id)], markers: [new AreaMarker("spawn", 2, 1, AreaMarkerKind.PlayerSpawn)])));

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void TransparentCells_NeedNoLegendEntry()
    {
        var failures = Validate(Write(Area(ground: ["#  #", "    ", "#  #"], legend: [Wall])));

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void AnUndefinedSymbol_FailsAtItsRowAndColumn()
    {
        var text = Write(Area(ground: ["####", "#.q#", "####"]));

        var failure = Single(Validate(text), "Symbol 'q'");

        var (line, column) = PositionOf(text, "\"#.q#\"", skip: 3);
        Assert.AreEqual(AreaFileName, failure.File);
        Assert.AreEqual(AreaId, failure.RecordId);
        Assert.AreEqual(line, failure.Line);
        Assert.AreEqual(column, failure.Column);
        StringAssert.Contains(failure.Reason, "(2, 1)");
    }

    [TestMethod]
    public void AnUndefinedSymbol_IsReportedOnce_WithHowManyCellsShareIt()
    {
        var failure = Single(Validate(Write(Area(ground: ["qqqq", "#..#", "####"]))), "Symbol 'q'");

        StringAssert.Contains(failure.Reason, "and 3 more cells");
    }

    [TestMethod]
    public void ARowOfTheWrongWidth_FailsAtThatRow()
    {
        var text = Write(Area(ground: ["####", "#..", "####"]));

        var failure = Single(Validate(text), "row 1 is 3 wide");

        Assert.AreEqual(PositionOf(text, "\"#..\"").Line, failure.Line);
    }

    [TestMethod]
    public void TheWrongNumberOfRows_FailsAtTheLayer()
    {
        var text = Write(Area(ground: ["####", "####"]));

        var failure = Single(Validate(text), "has 2 rows");

        Assert.AreEqual(PositionOf(text, "\"Ground\"").Line, failure.Line);
    }

    [TestMethod]
    public void AFloorOnFlying_Fails()
    {
        var text = Write(Area(flying: ["    ", " .  ", "    "]));

        var failure = Single(Validate(text), "Flying has no floor");

        var (line, column) = PositionOf(text, "\" .  \"", skip: 2);
        Assert.AreEqual(line, failure.Line);
        Assert.AreEqual(column, failure.Column);
    }

    [TestMethod]
    public void UnknownTerrainAndBlueprints_Fail()
    {
        var unknownBlueprint = new Guid("3c1e8f62-41a9-4d0b-9a77-5b2f0c6e9dff");
        var failures = Validate(Write(Area(
            legend: [Wall, new AreaCell('.', Terrain: "core:nothing", Spawns: [new AreaSpawn(unknownBlueprint)])],
            entities: [new AreaEntityPlacement("ghost", 1, 1, unknownBlueprint)])));

        Single(failures, "terrain 'core:nothing'");
        Single(failures, "Symbol '.'s spawn names blueprint");
        Single(failures, "Placement 'ghost' names blueprint");
    }

    [TestMethod]
    public void ASpawnNamingBothABlueprintAndChoices_Fails()
    {
        var failures = Validate(Write(Area(legend: [Wall, new AreaCell('.', Spawns: [new AreaSpawn(Goblin.Id, OneOf: [new AreaWeightedBlueprint(Goblin.Id)])])])));

        Single(failures, "not both or neither");
    }

    [TestMethod]
    public void AFootprintLeavingTheArea_Fails()
    {
        var failures = Validate(Write(Area(entities: [new AreaEntityPlacement("big", 2, 1, Goblin.Id, Size: new Vector2Byte(3, 3))])));

        Single(failures, "leaves the 4x3 area");
    }

    [TestMethod]
    public void ABlockingFootprintOnAWall_Fails_AndOnAnotherBlockingFootprint()
    {
        var failures = Validate(Write(Area(entities:
        [
            new AreaEntityPlacement("on-wall", 0, 0, Goblin.Id),
            new AreaEntityPlacement("first", 1, 1, Goblin.Id),
            new AreaEntityPlacement("second", 1, 1, Goblin.Id),
        ])));

        Single(failures, "Placement 'on-wall' has a blocking footprint over the wall at (0, 0)");
        Single(failures, "Placement 'second' has a blocking footprint over Placement 'first'");
    }

    [TestMethod]
    public void NonBlockingFootprints_MayShareACell()
    {
        var failures = Validate(Write(Area(entities: [new AreaEntityPlacement("a", 1, 1, TinyGoblin.Id), new AreaEntityPlacement("b", 1, 1, TinyGoblin.Id)])));

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void DuplicateIds_AndAMarkerOutsideTheArea_AndAFallbackWithoutUnique_Fail()
    {
        var text = Write(Area(
            entities: [new AreaEntityPlacement("a", 1, 1, TinyGoblin.Id, Fallback: Goblin.Id), new AreaEntityPlacement("a", 2, 1, TinyGoblin.Id)],
            markers: [new AreaMarker("spawn", 9, 9, AreaMarkerKind.PlayerSpawn)]));

        var failures = Validate(text);

        var duplicate = Single(failures, "same id as another placement");
        Assert.AreEqual(PositionOf(text, "\"pid\":\"a\",\"x\":2").Line, duplicate.Line);
        Single(failures, "is outside the 4x3 area");
        Single(failures, "isn't Unique");
    }

    [TestMethod]
    public void AnUndeclaredTag_Fails()
    {
        var failures = Validate(Write(Area(tags: GameplayTagSet.Create([GameplayTag.Get("Area.NoSuchThing")]))));

        Single(failures, "Tag 'Area.NoSuchThing'");
    }

    [TestMethod]
    public void LaterPalettesOverrideEarlierOnes_AndTheAreasOwnLegendOverridesBoth()
    {
        var stone = new AreaPalette(StonePaletteId, "Stone", [Wall, new AreaCell('x', Terrain: BuiltInTerrain.StoneFloorKey), new AreaCell('y', Terrain: BuiltInTerrain.StoneFloorKey)]);
        var grass = new AreaPalette(GrassPaletteId, "Grass", [new AreaCell('x', Terrain: BuiltInTerrain.GrassKey), new AreaCell('y', Terrain: BuiltInTerrain.GrassKey)]);
        var area = Area(palettes: [StonePaletteId, GrassPaletteId], legend: [new AreaCell('y', Terrain: BuiltInTerrain.DirtKey)]);

        var legend = AreaLegend.Resolve(area, id => id == StonePaletteId ? stone : id == GrassPaletteId ? grass : null);

        Assert.AreEqual(BuiltInTerrain.StoneWallKey, legend['#'].Structure);
        Assert.AreEqual(BuiltInTerrain.GrassKey, legend['x'].Terrain);
        Assert.AreEqual(BuiltInTerrain.DirtKey, legend['y'].Terrain);
    }

    [TestMethod]
    public void ASymbolFromAPalette_IsDefined_AndAMissingPalette_Fails()
    {
        var stone = new AreaPalette(StonePaletteId, "Stone", [Wall, Floor]);

        Assert.AreEqual(0, Validate(Write(Area(legend: [], palettes: [StonePaletteId])), stone).Count);
        Single(Validate(Write(Area(palettes: [GrassPaletteId]))), "Palette");
    }

    [TestMethod]
    public void Placements_NeedTheirArea_ExactlyOnePlayerSpawn_AndNoOverlap()
    {
        var text = Write(Area(markers: [new AreaMarker("spawn", 1, 1, AreaMarkerKind.PlayerSpawn), new AreaMarker("again", 2, 1, AreaMarkerKind.PlayerSpawn)]));
        var placements = new[]
        {
            new AreaPlacement(PlacementId, AreaId, 0, 0, 0, 0),
            new AreaPlacement(new Guid("3c1e8f62-41a9-4d0b-9a77-5b2f0c6e9d15"), AreaId, 0, 0, 2, 2),
            new AreaPlacement(new Guid("3c1e8f62-41a9-4d0b-9a77-5b2f0c6e9d16"), GrassPaletteId, 0, 0, 100, 100),
            new AreaPlacement(new Guid("3c1e8f62-41a9-4d0b-9a77-5b2f0c6e9d17"), AreaId, 0, 0, 1022, 0),
        };

        var failures = Validate(text, [], placements);

        Single(failures, "overlaps 'Test area'");
        Single(failures, "which doesn't exist");
        Single(failures, "doesn't fit its 1024x1024 neighborhood");
        Single(failures, "PlayerSpawn markers; exactly one");
    }

    [TestMethod]
    public void AnUnknownKey_FailsTheRead()
    {
        var text = Write(Area()).Replace("\"n\": \"Test area\",", "\"n\": \"Test area\",\n  \"colour\": 3,", StringComparison.Ordinal);
        var failures = new List<ContentFailure>();

        var record = ContentDocument.Read<AreaDefinition>(text, AreaFileName, MasterContent.AreasTable, Types, NoContentReferences.Instance, failures);

        Assert.IsNull(record);
        StringAssert.Contains(failures.Single().Reason, "\"colour\"");
    }

    [TestMethod]
    public void TheManifest_ReadsEveryAreaFile_WithItsLocation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"area-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, MasterContent.AreasDirectoryName));
        try
        {
            File.WriteAllText(Path.Combine(directory, MasterContent.AreasDirectoryName, "test-area.json"), Write(Area()), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, MasterContent.AreaPlacementsFileName), ContentTable.Write(MasterContent.AreaPlacementsTable, [new AreaPlacement(PlacementId, AreaId, 0, 0, 0, 0)], Types));

            var content = MasterContent.Load(directory, Types);

            Assert.IsEmpty(content.Failures, string.Join(Environment.NewLine, content.Failures));
            Assert.AreEqual(AreaId, content.Areas.Single().Value.Id);
            Assert.AreEqual(AreaFileName, content.Areas.Single().Location.File);
            Assert.AreEqual(2, content.AreaPlacements.Single().Location.Line);
            Assert.IsEmpty(content.AreaPalettes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
