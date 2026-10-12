using System.Text.Json;
using SpriteFormat;

namespace Tests.SpriteManifestFormat;

[TestClass]
public sealed class SpriteManifestFormatTests
{
    private const string Sheet = "sheet.png";

    private static string ManifestJson(params SpriteManifestEntry[] entries) => JsonSerializer.Serialize(entries);

    private static SpriteManifestEntry PlacedEntry(string name, int sourceWidth, int sourceHeight, int pixelsPerTile, int? pivotX = null, int? pivotY = null) =>
        new(name, [new SpriteManifestCell(Sheet, 0, 0, sourceWidth, sourceHeight)], pixelsPerTile, pivotX, pivotY);

    private static List<SpriteManifestCell?> Cells(int count) =>
        [.. Enumerable.Range(0, count).Select(index => new SpriteManifestCell(Sheet, index, 0, 16, 16))];

    private static SpriteManifestEntry ParsedEntry(string json, string name) =>
        SpriteManifest.Parse(json).TryGetEntry(name, out var entry) ? entry : throw new AssertFailedException($"No entry '{name}' was parsed.");

    [TestMethod]
    public void Deserialize_EntryWithoutNewFields_ReadsAsOneByOneUnplacedUnlinked()
    {
        const string json = """[{"Name":"Old","Cells":[{"SheetPath":"sheet.png","Column":2,"Row":3,"CellWidth":16,"CellHeight":16}]}]""";

        var entry = ParsedEntry(json, "Old");
        var cell = entry.Cells.Single()!;

        Assert.AreEqual(1, cell.EffectiveColumnSpan);
        Assert.AreEqual(1, cell.EffectiveRowSpan);
        Assert.AreEqual(new SpriteSourceRectangle(32, 48, 16, 16), cell.SourceRectangle);
        Assert.IsNull(entry.PlacementFor(cell));
        Assert.AreEqual(SpriteLinking.None, entry.Linking);
    }

    [TestMethod]
    public void Serialize_EntryWithoutNewFields_WritesOnlyTheOriginalProperties()
    {
        var json = JsonSerializer.Serialize(new SpriteManifestEntry("Old", [new SpriteManifestCell(Sheet, 2, 3, 16, 16)]));

        Assert.AreEqual("""{"Name":"Old","Cells":[{"SheetPath":"sheet.png","Column":2,"Row":3,"CellWidth":16,"CellHeight":16}]}""", json);
    }

    [TestMethod]
    public void Serialize_PlacementSpansAndLinking_RoundTrip()
    {
        var original = new SpriteManifestEntry(
            "Linked",
            [.. Cells(16).Select(cell => cell! with { ColumnSpan = 2, RowSpan = 3 })],
            PixelsPerTile: 32,
            PivotX: 16,
            PivotY: 48,
            Linking: SpriteLinking.Cardinal,
            ColumnSpan: 2,
            RowSpan: 3);

        var json = JsonSerializer.Serialize(new[] { original });
        var roundTripped = ParsedEntry(json, "Linked");

        StringAssert.Contains(json, "\"Linking\":\"Cardinal\"");
        Assert.AreEqual(32, roundTripped.PixelsPerTile);
        Assert.AreEqual(16, roundTripped.PivotX);
        Assert.AreEqual(48, roundTripped.PivotY);
        Assert.AreEqual(SpriteLinking.Cardinal, roundTripped.Linking);
        Assert.AreEqual(2, roundTripped.ColumnSpan);
        Assert.AreEqual(3, roundTripped.RowSpan);
        CollectionAssert.AreEqual(original.Cells, roundTripped.Cells);
    }

    [TestMethod]
    public void Parse_AnOldLinkGroup_IsIgnored()
    {
        const string json = """[{"Name":"Top","Cells":[{"SheetPath":"sheet.png","Column":0,"Row":0,"CellWidth":16,"CellHeight":16}],"LinkGroup":"stone-wall"}]""";

        var entry = ParsedEntry(json, "Top");

        Assert.DoesNotContain("LinkGroup", JsonSerializer.Serialize(entry));
    }

    [TestMethod]
    public void SourceRectangle_TwoByOneSpan_IsTwoCellsWideFromItsOriginCell()
    {
        var cell = new SpriteManifestCell(Sheet, 3, 2, 16, 16, ColumnSpan: 2, RowSpan: 1);

        Assert.AreEqual(new SpriteSourceRectangle(48, 32, 32, 16), cell.SourceRectangle);
    }

    [TestMethod]
    public void PlacementFor_NoPivot_DefaultsToTheCellsBottomCentre()
    {
        var entry = PlacedEntry("Tall", 32, 64, 32);

        Assert.AreEqual(new SpritePlacement(32, 16, 64), entry.PlacementFor(entry.Cells[0]!));
    }

    [TestMethod]
    [DataRow(15)]
    [DataRow(17)]
    [DataRow(0)]
    public void Parse_LinkedEntryNotAMultipleOfSixteen_ThrowsNamingTheEntry(int cellCount)
    {
        var json = ManifestJson(new SpriteManifestEntry("Broken-Top", Cells(cellCount), Linking: SpriteLinking.Cardinal));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Broken-Top'");
    }

    [TestMethod]
    [DataRow(16)]
    [DataRow(32)]
    public void Parse_LinkedEntryInWholeSetsOfSixteen_Loads(int cellCount)
    {
        var json = ManifestJson(new SpriteManifestEntry("Top", Cells(cellCount), Linking: SpriteLinking.Cardinal));

        Assert.HasCount(cellCount, ParsedEntry(json, "Top").Cells);
    }

    [TestMethod]
    public void Serialize_RotationAndFlips_RoundTripAndAreOmittedWhenUnset()
    {
        var transformed = new SpriteManifestCell(Sheet, 1, 1, 16, 16, Rotation: 90, FlipHorizontal: true);
        var plainJson = JsonSerializer.Serialize(new SpriteManifestCell(Sheet, 1, 1, 16, 16));

        var json = ManifestJson(new SpriteManifestEntry("Turned", [transformed]));
        var roundTripped = ParsedEntry(json, "Turned").Cells.Single();

        Assert.AreEqual(transformed, roundTripped);
        Assert.DoesNotContain("Rotation", plainJson);
        Assert.DoesNotContain("Flip", plainJson);
    }

    [TestMethod]
    [DataRow(45)]
    [DataRow(360)]
    [DataRow(-90)]
    public void Parse_RotationNotAQuarterTurn_ThrowsNamingTheEntry(int rotation)
    {
        var json = ManifestJson(new SpriteManifestEntry("Askew", [new SpriteManifestCell(Sheet, 0, 0, 16, 16, Rotation: rotation)]));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Askew'");
    }

    [TestMethod]
    [DataRow(90)]
    [DataRow(270)]
    public void Parse_QuarterTurnOfANonSquareCell_ThrowsNamingTheEntry(int rotation)
    {
        var json = ManifestJson(new SpriteManifestEntry("Lanky", [new SpriteManifestCell(Sheet, 0, 0, 16, 16, RowSpan: 2, Rotation: rotation)]));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Lanky'");
    }

    [TestMethod]
    public void Parse_HalfTurnOfANonSquareCell_Loads()
    {
        var json = ManifestJson(new SpriteManifestEntry("Upended", [new SpriteManifestCell(Sheet, 0, 0, 16, 16, RowSpan: 2, Rotation: 180, FlipVertical: true)]));

        Assert.IsTrue(SpriteManifest.Parse(json).TryGetEntry("Upended", out _));
    }

    private static SpriteManifestEntry Wall(string name, int faceHeightPixels, int topHeightPixels, int pixelsPerTile = 16) =>
        new(
            name,
            [new SpriteManifestCell(Sheet, 0, 0, 16, faceHeightPixels)],
            PixelsPerTile: pixelsPerTile,
            Top: new SpriteManifestPart([.. Cells(16).Select(cell => cell! with { CellHeight = topHeightPixels })], PixelsPerTile: pixelsPerTile, Linking: SpriteLinking.Cardinal));

    [TestMethod]
    public void Serialize_WallTop_RoundTripsAndIsOmittedWithoutOne()
    {
        var wall = Wall("Stone", 32, 16);

        var parsed = ParsedEntry(ManifestJson(wall), "Stone");

        Assert.AreEqual(SpriteLinking.Cardinal, parsed.Top!.Linking);
        CollectionAssert.AreEqual(wall.Top!.Cells, parsed.Top.Cells);
        Assert.DoesNotContain("Top", JsonSerializer.Serialize(new SpriteManifestEntry("Plain", Cells(1))));
    }

    [TestMethod]
    [DataRow(16, 1)]
    [DataRow(32, 2)]
    [DataRow(24, 2)]
    public void HeightRows_PlacedFace_CountsEveryRowItReaches(int faceHeightPixels, int expectedRows)
    {
        Assert.AreEqual(expectedRows, Wall("Stone", faceHeightPixels, 16).Face.HeightRows());
    }

    [TestMethod]
    public void HeightRows_StretchedFace_IsOneRow()
    {
        Assert.AreEqual(1, new SpriteManifestPart([new SpriteManifestCell(Sheet, 0, 0, 16, 16, RowSpan: 3)]).HeightRows());
    }

    [TestMethod]
    [DataRow(16, 16)]
    [DataRow(32, 16)]
    [DataRow(16, 32)]
    public void Parse_WallWhoseFaceAndTopFitThreeRows_Loads(int faceHeightPixels, int topHeightPixels)
    {
        var json = ManifestJson(Wall("Stone", faceHeightPixels, topHeightPixels));

        Assert.IsNotNull(ParsedEntry(json, "Stone").Top);
    }

    [TestMethod]
    [DataRow(48, 16)]
    [DataRow(32, 32)]
    public void Parse_WallWhoseFaceAndTopPassThreeRows_ThrowsNamingTheTop(int faceHeightPixels, int topHeightPixels)
    {
        var json = ManifestJson(Wall("Tower", faceHeightPixels, topHeightPixels));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Tower' (top");
    }

    [TestMethod]
    public void Parse_StretchedTopOnAThreeRowFace_Throws()
    {
        var wall = new SpriteManifestEntry(
            "Tall",
            [new SpriteManifestCell(Sheet, 0, 0, 16, 48)],
            PixelsPerTile: 16,
            Top: new SpriteManifestPart([new SpriteManifestCell(Sheet, 1, 0, 16, 16)]));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(ManifestJson(wall)));

        StringAssert.Contains(exception.Message, "on a 3-row face");
    }

    [TestMethod]
    public void Parse_UnfinishedWallTop_LoadsWithEmptySlots()
    {
        var wall = Wall("Stone", 16, 16);
        wall.Top!.Cells[5] = null;

        var parsed = ParsedEntry(ManifestJson(wall), "Stone");

        Assert.IsNull(parsed.Top!.Cells[5]);
    }

    private static SpriteManifestEntry WallWithFace(List<SpriteManifestCell?> faceCells) =>
        new("Stone", faceCells, Linking: SpriteLinking.Horizontal, Top: Wall("Stone", 16, 16).Top);

    [TestMethod]
    public void Parse_HorizontalFaceWithOnlyItsStandalonePiece_Loads()
    {
        var faceCells = Cells(4);
        faceCells[SpriteLinks.HorizontalEast] = null;
        faceCells[SpriteLinks.HorizontalWest] = null;
        faceCells[SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest] = null;

        var entry = ParsedEntry(ManifestJson(WallWithFace(faceCells)), "Stone");

        Assert.AreEqual(SpriteLinking.Horizontal, entry.Linking);
    }

    [TestMethod]
    public void Parse_HorizontalFaceMissingAStandalonePiece_ThrowsNamingTheSet()
    {
        var faceCells = Cells(8);
        faceCells[4 + SpriteLinks.Standalone] = null;

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(ManifestJson(WallWithFace(faceCells))));

        StringAssert.Contains(exception.Message, "'Stone'");
        StringAssert.Contains(exception.Message, "variant set 2");
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(5)]
    public void Parse_HorizontalFaceNotInWholeSetsOfFour_Throws(int cellCount)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(ManifestJson(WallWithFace(Cells(cellCount)))));
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(SpriteLinks.HorizontalEast, SpriteLinks.HorizontalEast)]
    [DataRow(SpriteLinks.HorizontalWest, SpriteLinks.HorizontalWest)]
    [DataRow(SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest, SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest)]
    public void CellIndexFor_HorizontalWithEveryPiece_PicksThePieceForTheMask(int mask, int expectedIndex)
    {
        var part = new SpriteManifestPart(Cells(4), Linking: SpriteLinking.Horizontal);

        Assert.AreEqual(expectedIndex, SpriteLinks.CellIndexFor(part, 0, mask));
    }

    [TestMethod]
    [DataRow(SpriteLinks.HorizontalEast)]
    [DataRow(SpriteLinks.HorizontalWest)]
    [DataRow(SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest)]
    public void CellIndexFor_HorizontalPieceMissing_FallsBackToStandalone(int mask)
    {
        var cells = Cells(8);
        cells[4 + mask] = null;
        var part = new SpriteManifestPart(cells, Linking: SpriteLinking.Horizontal);

        Assert.AreEqual(4 + SpriteLinks.Standalone, SpriteLinks.CellIndexFor(part, 1, mask));
    }

    [TestMethod]
    public void CellIndexFor_CardinalPieceMissing_FallsBackToFullyJoined()
    {
        var cells = Cells(16);
        cells[SpriteLinks.North | SpriteLinks.East] = null;
        var part = new SpriteManifestPart(cells, Linking: SpriteLinking.Cardinal);

        Assert.AreEqual(15, SpriteLinks.CellIndexFor(part, 0, SpriteLinks.North | SpriteLinks.East));
    }

    [TestMethod]
    public void CellIndexFor_VariantSetWrapsByTheSetCount()
    {
        var part = new SpriteManifestPart(Cells(8), Linking: SpriteLinking.Horizontal);

        Assert.AreEqual(4 + SpriteLinks.HorizontalWest, SpriteLinks.CellIndexFor(part, 3, SpriteLinks.HorizontalWest));
    }

    [TestMethod]
    [DataRow("#FF8000", 255, 128, 0, 255)]
    [DataRow("#ff800040", 255, 128, 0, 64)]
    public void SpriteTint_TryParse_ReadsRgbAndRgba(string text, int red, int green, int blue, int alpha)
    {
        Assert.IsTrue(SpriteTint.TryParse(text, out var tint));
        Assert.AreEqual(new SpriteColor((byte)red, (byte)green, (byte)blue, (byte)alpha), tint);
    }

    [TestMethod]
    [DataRow("FF8000")]
    [DataRow("#FF80")]
    [DataRow("#GG8000")]
    [DataRow("#FF80001")]
    public void SpriteTint_TryParse_RefusesAnythingElse(string text)
    {
        Assert.IsFalse(SpriteTint.TryParse(text, out _));
    }

    [TestMethod]
    public void SpriteTint_Format_RoundTripsThroughTryParse()
    {
        foreach (var tint in new[] { new SpriteColor(18, 52, 86), new SpriteColor(18, 52, 86, 120) })
        {
            Assert.IsTrue(SpriteTint.TryParse(SpriteTint.Format(tint), out var parsed));
            Assert.AreEqual(tint, parsed);
        }
    }

    [TestMethod]
    public void SpriteTint_Multiply_MultipliesEachChannel_AndWhiteChangesNothing()
    {
        var tint = new SpriteColor(200, 100, 50, 255);

        Assert.AreEqual(tint, SpriteTint.Multiply(SpriteColor.White, tint));
        Assert.AreEqual(new SpriteColor(100, 50, 25, 255), SpriteTint.Multiply(new SpriteColor(128, 128, 128, 255), tint));
        Assert.AreEqual(new SpriteColor(0, 0, 0, 0), SpriteTint.Multiply(new SpriteColor(0, 0, 0, 0), tint));
    }

    [TestMethod]
    public void Parse_ATintedCell_RoundTripsItsTint_AndAnUntintedOneWritesNone()
    {
        var tinted = new SpriteManifestCell(Sheet, 0, 0, 16, 16, Tint: "#80C0FF");

        var parsed = SpriteManifest.Parse(ManifestJson(new SpriteManifestEntry("Mossy", [tinted]))).Entries.Single().Cells.Single()!;

        Assert.AreEqual("#80C0FF", parsed.Tint);
        Assert.AreEqual(new SpriteColor(128, 192, 255), parsed.TintColor);
        Assert.DoesNotContain("Tint", JsonSerializer.Serialize(new SpriteManifestCell(Sheet, 0, 0, 16, 16)));
    }

    [TestMethod]
    [DataRow("blue")]
    [DataRow("#12345")]
    public void Parse_AMalformedTint_ThrowsNamingTheEntry(string tint)
    {
        var json = ManifestJson(new SpriteManifestEntry("Smudged", [new SpriteManifestCell(Sheet, 0, 0, 16, 16, Tint: tint)]));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Smudged'");
    }

    [TestMethod]
    public void SetIndexFor_WithChances_PicksTheSetWhoseCumulativeChanceThePercentileFallsUnder()
    {
        var part = new SpriteManifestPart(Cells(12), Linking: SpriteLinking.Horizontal, VariantWeights: [70, 20, 10]);

        Assert.AreEqual(0, SpriteLinks.SetIndexFor(part, 0, 3));
        Assert.AreEqual(0, SpriteLinks.SetIndexFor(part, 69, 3));
        Assert.AreEqual(1, SpriteLinks.SetIndexFor(part, 70, 3));
        Assert.AreEqual(1, SpriteLinks.SetIndexFor(part, 89, 3));
        Assert.AreEqual(2, SpriteLinks.SetIndexFor(part, 90, 3));
        Assert.AreEqual(2, SpriteLinks.SetIndexFor(part, 99, 3));
    }

    [TestMethod]
    public void SetIndexFor_AZeroChanceSet_IsNeverPicked()
    {
        var part = new SpriteManifestPart(Cells(12), Linking: SpriteLinking.Horizontal, VariantWeights: [50, 0, 50]);

        var picked = Enumerable.Range(0, SpriteLinks.PercentileRange).Select(percentile => SpriteLinks.SetIndexFor(part, percentile, 3)).ToHashSet();

        CollectionAssert.AreEquivalent(new[] { 0, 2 }, picked.ToArray());
    }

    [TestMethod]
    public void SetIndexFor_WithoutChances_SpreadsEvenly()
    {
        var part = new SpriteManifestPart(Cells(12), Linking: SpriteLinking.Horizontal);

        Assert.AreEqual(2, SpriteLinks.SetIndexFor(part, 5, 3));
    }

    [TestMethod]
    public void Parse_VariantChancesThatAddUpTo100_Load_AndRoundTrip()
    {
        var json = ManifestJson(new SpriteManifestEntry("Mossy", Cells(8), Linking: SpriteLinking.Horizontal, VariantWeights: [75, 25]));

        CollectionAssert.AreEqual(new[] { 75, 25 }, ParsedEntry(json, "Mossy").VariantWeights!.ToArray());
        Assert.DoesNotContain("VariantWeights", ManifestJson(new SpriteManifestEntry("Plain", Cells(8), Linking: SpriteLinking.Horizontal)));
    }

    [TestMethod]
    [DataRow(new[] { 60, 30 })]
    [DataRow(new[] { 100 })]
    [DataRow(new[] { 110, -10 })]
    public void Parse_VariantChancesNotOnePerSetAddingTo100_ThrowsNamingTheEntry(int[] chances)
    {
        var json = ManifestJson(new SpriteManifestEntry("Lopsided", Cells(8), Linking: SpriteLinking.Horizontal, VariantWeights: [.. chances]));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Lopsided'");
    }

    [TestMethod]
    public void Parse_VariantChancesOnAPlainOptionsEntry_Throws()
    {
        var json = ManifestJson(new SpriteManifestEntry("Options", Cells(2), VariantWeights: [50, 50]));

        Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));
    }

    [TestMethod]
    public void Parse_UnfinishedLinkedSetWithEmptySlots_Loads()
    {
        var cells = Cells(16);
        cells[3] = null;
        cells[15] = null;
        var json = ManifestJson(new SpriteManifestEntry("Unfinished-Top", cells, Linking: SpriteLinking.Cardinal));

        var entry = ParsedEntry(json, "Unfinished-Top");

        Assert.IsNull(entry.Cells[3]);
        Assert.IsNotNull(entry.Cells[4]);
    }

    [TestMethod]
    public void Parse_OptionsEntryWithAnEmptyCell_ThrowsNamingTheEntry()
    {
        var cells = Cells(2);
        cells[1] = null;
        var json = ManifestJson(new SpriteManifestEntry("Gappy", cells));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Gappy'");
    }

    [TestMethod]
    public void Parse_EntrySpan_DoesNotChangeItsCells()
    {
        const string json = """[{"Name":"Tall","Cells":[{"SheetPath":"sheet.png","Column":0,"Row":0,"CellWidth":16,"CellHeight":16,"RowSpan":2}],"ColumnSpan":1,"RowSpan":2}]""";

        var cell = ParsedEntry(json, "Tall").Cells.Single()!;

        Assert.AreEqual(new SpriteSourceRectangle(0, 0, 16, 32), cell.SourceRectangle);
    }

    [TestMethod]
    public void Parse_ThreeRowsAbove_ThrowsNamingTheEntryAndItsReach()
    {
        var json = ManifestJson(PlacedEntry("Tower", 16, 64, 16));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Tower'");
        StringAssert.Contains(exception.Message, "reaches 3 rows above");
    }

    [TestMethod]
    public void Parse_ThreeColumnsToOneSide_ThrowsNamingTheEntryAndItsReach()
    {
        var json = ManifestJson(PlacedEntry("Banner", 16, 16, 16, pivotX: 56));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Banner'");
        StringAssert.Contains(exception.Message, "3 columns left");
    }

    [TestMethod]
    public void Parse_BelowTheFootprint_ThrowsNamingTheEntryAndItsReach()
    {
        var json = ManifestJson(PlacedEntry("Sunken", 16, 16, 16, pivotY: 8));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Sunken'");
        StringAssert.Contains(exception.Message, "0.5 rows below");
    }

    [TestMethod]
    public void Parse_ExactlyTwoRowsAboveAndTwoColumnsEitherSide_Loads()
    {
        var json = ManifestJson(PlacedEntry("Giant", 80, 48, 16));

        var reach = SpriteReach.Of(80, 48, new SpritePlacement(16, 40, 48));

        Assert.IsTrue(SpriteManifest.Parse(json).TryGetEntry("Giant", out _));
        Assert.AreEqual(new SpriteReach(2, 2, 2, 0, RaisedRows: 0, IsWithinReachBox: true), reach);
    }

    [TestMethod]
    public void Parse_ZeroPixelsPerTile_ThrowsNamingTheEntry()
    {
        var json = ManifestJson(PlacedEntry("Flat", 16, 16, 0));

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => SpriteManifest.Parse(json));

        StringAssert.Contains(exception.Message, "'Flat'");
    }

    /// <summary>Catches a builder that wrote a shape the game can't read, or an entry outside the reach box.</summary>
    [TestMethod]
    public void Parse_CheckedInManifest_LoadsWithEveryEntryInsideTheReachBox()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SpriteManifest.json"));

        var entries = SpriteManifest.Parse(json);

        Assert.IsNotEmpty(entries.Entries);
    }
}
