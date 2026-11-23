using System.Text.Json.Nodes;
using DungeonCrawlerWorld.Workshop;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Inventory;
using Workshop.Editing;
using static Workshop.Tests.Editing.EditingTestSchema;

namespace Workshop.Tests.Editing;

/// <summary>The field diff two versions of a record are compared by: nothing a reader reads the same is a difference, and each difference is named at its deepest value, on both sides.</summary>
[TestClass]
public sealed class ContentRecordDiffTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), WorkshopTestContent.Master, []), WorkshopTestContent.Master, [], TextWriter.Null));

    private static string Id => ContentGuid.Format(BoxId);

    private static IReadOnlyList<ContentFieldDifference> CompareBoxes(string baseline, string current) =>
        ContentRecordDiff.Compare(baseline, current, typeof(Box), Types);

    private static string BoxWith(string fields) => $"{{\"id\":\"{Id}\",\"n\":\"Crate\"{fields}}}";

    private static string Peg(int length) => $"{{\"$\":\"Peg\",\"ln\":{length}}}";

    private static string Hook(int length) => $"{{\"$\":\"Hook\",\"ln\":{length},\"op\":true}}";

    private static string Parts(params string[] parts) => $",\"pr\":[{string.Join(",", parts)}]";

    private static IReadOnlyList<string> MasterItemLines() =>
        [.. File.ReadAllLines(Path.Combine(GameContent.DefaultMasterDirectory, GameContent.ItemsFileName)).Skip(1).Where(line => line.Length > 0)];

    private static IReadOnlyList<ContentFieldDifference> CompareItems(string baseline, string current) =>
        ContentRecordDiff.Compare(baseline, current, typeof(ItemDefinition), Session.Value.Catalogs.ContentTypes);

    [TestMethod]
    public void IdenticalRecords_HaveNoDifference()
    {
        var box = BoxWith(",\"c\":3,\"w\":2.5,\"lid\":{\"h\":4},\"pt\":" + Hook(1) + Parts(Peg(2), Hook(3)) + ",\"zz\":[1,2]");

        Assert.IsEmpty(CompareBoxes(box, box));
    }

    [TestMethod]
    public void AValueWrittenAsItsDefault_EqualsTheValueLeftOut()
    {
        Assert.IsEmpty(CompareBoxes(BoxWith(string.Empty), BoxWith(",\"c\":1,\"w\":0,\"s\":false,\"sh\":\"Plain\",\"nt\":\"\"")));
    }

    [TestMethod]
    public void KeyOrderAndNumberSpelling_AreNoDifference()
    {
        Assert.IsEmpty(CompareBoxes($"{{\"id\":\"{Id}\",\"n\":\"Crate\",\"w\":2.5,\"c\":3}}", $"{{\"c\":3,\"w\":2.50,\"n\":\"Crate\",\"id\":\"{Id}\"}}"));
    }

    [TestMethod]
    public void AChangedScalar_IsNamedAtItsField_WithBothSidesAsWritten()
    {
        var difference = CompareBoxes(BoxWith(",\"c\":3"), BoxWith(",\"c\":5")).Single();

        Assert.AreEqual(ContentDifferenceKind.Changed, difference.Kind);
        Assert.AreEqual("c", difference.BaselinePath!.Text);
        Assert.AreEqual("c", difference.CurrentPath!.Text);
        Assert.AreEqual("Count", difference.DisplayPath);
        Assert.AreEqual("3", difference.BaselineJson);
        Assert.AreEqual("5", difference.CurrentJson);
    }

    [TestMethod]
    public void AValueChangedFromItsDefault_IsChanged_WithTheOmittedSideNull()
    {
        var difference = CompareBoxes(BoxWith(string.Empty), BoxWith(",\"sh\":\"Dark\"")).Single();

        Assert.AreEqual(ContentDifferenceKind.Changed, difference.Kind);
        Assert.AreEqual("sh", difference.Path.Text);
        Assert.IsNull(difference.BaselineJson);
        Assert.AreEqual("\"Dark\"", difference.CurrentJson);
    }

    [TestMethod]
    public void ANestedObject_IsComparedInside()
    {
        var difference = CompareBoxes(BoxWith(",\"lid\":{\"h\":4}"), BoxWith(",\"lid\":{\"h\":6}")).Single();

        Assert.AreEqual("lid.h", difference.Path.Text);
        Assert.AreEqual("Lid > Height", difference.DisplayPath);
    }

    [TestMethod]
    public void AnObjectOnlyOneSideHolds_IsAddedOrRemoved()
    {
        var added = CompareBoxes(BoxWith(string.Empty), BoxWith(",\"lid\":{}")).Single();
        var removed = CompareBoxes(BoxWith(",\"lid\":{}"), BoxWith(string.Empty)).Single();

        Assert.AreEqual(ContentDifferenceKind.Added, added.Kind);
        Assert.IsNull(added.BaselinePath);
        Assert.AreEqual("lid", added.CurrentPath!.Text);
        Assert.AreEqual(ContentDifferenceKind.Removed, removed.Kind);
        Assert.AreEqual("lid", removed.BaselinePath!.Text);
        Assert.IsNull(removed.CurrentPath);
    }

    [TestMethod]
    public void APolymorphicValueOfTheSameType_IsComparedInside()
    {
        var difference = CompareBoxes(BoxWith(",\"pt\":" + Hook(1)), BoxWith(",\"pt\":" + Hook(2))).Single();

        Assert.AreEqual("pt.ln", difference.Path.Text);
        Assert.AreEqual("Part (Hook) > Length", difference.DisplayPath);
    }

    [TestMethod]
    public void APolymorphicValueOfAnotherType_IsOneDifference_TheWholeValue()
    {
        var difference = CompareBoxes(BoxWith(",\"pt\":" + Peg(1)), BoxWith(",\"pt\":" + Hook(1))).Single();

        Assert.AreEqual(ContentDifferenceKind.Changed, difference.Kind);
        Assert.AreEqual("pt", difference.Path.Text);
        Assert.AreEqual(Peg(1), difference.BaselineJson);
        Assert.AreEqual(Hook(1), difference.CurrentJson);
    }

    [TestMethod]
    public void AKeyNoSchemaDeclares_ComparesAsRawJson()
    {
        Assert.IsEmpty(CompareBoxes(BoxWith(",\"zz\":{\"a\":1}"), BoxWith(",\"zz\":{\"a\":1}")));
        Assert.AreEqual(ContentDifferenceKind.Changed, CompareBoxes(BoxWith(",\"zz\":{\"a\":1}"), BoxWith(",\"zz\":{\"a\":2}")).Single().Kind);
        Assert.AreEqual(ContentDifferenceKind.Added, CompareBoxes(BoxWith(string.Empty), BoxWith(",\"zz\":7")).Single().Kind);
    }

    [TestMethod]
    public void AValueOfTheWrongKind_ComparesAsRawJson()
    {
        Assert.IsEmpty(CompareBoxes(BoxWith(",\"c\":\"lots\""), BoxWith(",\"c\":\"lots\"")));
        Assert.AreEqual("c", CompareBoxes(BoxWith(",\"c\":\"lots\""), BoxWith(",\"c\":3")).Single().Path.Text);
    }

    [TestMethod]
    public void DisablingARecord_IsAChange()
    {
        var difference = CompareBoxes(BoxWith(string.Empty), BoxWith(",\"off\":true")).Single();

        Assert.AreEqual(ContentDifferenceKind.Changed, difference.Kind);
        Assert.AreEqual(ContentTable.DisabledKey, difference.Path.Text);
    }

    [TestMethod]
    public void AnElementAdded_IsAdded_AndTheElementsItShiftsAreNot()
    {
        var difference = CompareBoxes(BoxWith(Parts(Peg(1), Peg(2))), BoxWith(Parts(Hook(9), Peg(1), Peg(2)))).Single();

        Assert.AreEqual(ContentDifferenceKind.Added, difference.Kind);
        Assert.AreEqual("pr[0]", difference.CurrentPath!.Text);
        Assert.AreEqual("Parts[0]", difference.DisplayPath);
    }

    [TestMethod]
    public void AnElementRemoved_IsRemoved_AtItsPlaceInTheBaseline()
    {
        var difference = CompareBoxes(BoxWith(Parts(Peg(1), Hook(9), Peg(2))), BoxWith(Parts(Peg(1), Peg(2)))).Single();

        Assert.AreEqual(ContentDifferenceKind.Removed, difference.Kind);
        Assert.AreEqual("pr[1]", difference.BaselinePath!.Text);
        Assert.IsNull(difference.CurrentPath);
    }

    [TestMethod]
    public void TheLastElementRemoved_TakingTheListWithIt_IsRemoved()
    {
        var difference = CompareBoxes(BoxWith(Parts(Peg(1))), BoxWith(string.Empty)).Single();

        Assert.AreEqual(ContentDifferenceKind.Removed, difference.Kind);
        Assert.AreEqual("pr[0]", difference.BaselinePath!.Text);
    }

    [TestMethod]
    public void AnElementMoved_IsOneMove_FromItsOldPlaceToItsNew()
    {
        var difference = CompareBoxes(BoxWith(Parts(Peg(1), Peg(2), Peg(3))), BoxWith(Parts(Peg(3), Peg(1), Peg(2)))).Single();

        Assert.AreEqual(ContentDifferenceKind.Moved, difference.Kind);
        Assert.AreEqual("pr[2]", difference.BaselinePath!.Text);
        Assert.AreEqual("pr[0]", difference.CurrentPath!.Text);
    }

    [TestMethod]
    public void AnElementChangedInPlace_IsComparedInside_NotRemovedAndAdded()
    {
        var difference = CompareBoxes(BoxWith(Parts(Peg(1), Peg(2), Peg(3))), BoxWith(Parts(Peg(1), Peg(7), Peg(3)))).Single();

        Assert.AreEqual(ContentDifferenceKind.Changed, difference.Kind);
        Assert.AreEqual("pr[1].ln", difference.Path.Text);
        Assert.AreEqual("Parts[1] (Peg) > Length", difference.DisplayPath);
    }

    [TestMethod]
    public void AnElementChanged_AfterAnInsertion_KeepsEachSidesOwnIndex()
    {
        var difference = CompareBoxes(BoxWith(Parts(Peg(1), Hook(2))), BoxWith(Parts(Peg(1), Peg(5), Hook(3)))).ToList();

        Assert.HasCount(2, difference);
        Assert.AreEqual(ContentDifferenceKind.Added, difference[0].Kind);
        Assert.AreEqual("pr[1]", difference[0].CurrentPath!.Text);
        Assert.AreEqual(ContentDifferenceKind.Changed, difference[1].Kind);
        Assert.AreEqual("pr[1].ln", difference[1].BaselinePath!.Text);
        Assert.AreEqual("pr[2].ln", difference[1].CurrentPath!.Text);
    }

    [TestMethod]
    public void AnElementReplacedByOneOfAnotherType_IsRemovedAndAdded_NotChanged()
    {
        var differences = CompareBoxes(BoxWith(Parts(Peg(1))), BoxWith(Parts(Hook(1)))).ToList();

        Assert.HasCount(2, differences);
        Assert.AreEqual(ContentDifferenceKind.Added, differences[0].Kind);
        Assert.AreEqual("pr[0]", differences[0].CurrentPath!.Text);
        Assert.AreEqual(ContentDifferenceKind.Removed, differences[1].Kind);
        Assert.AreEqual("pr[0]", differences[1].BaselinePath!.Text);
    }

    [TestMethod]
    public void AHealthPotionGivenADirectDamage_ThenLosingItsDirectHeal_HasTheHealRemoved_AndTheDamageAdded()
    {
        var potion = MasterItemLines().Single(line => line.Contains("\"n\":\"Health Potion\"", StringComparison.Ordinal));
        var record = new EditableContentRecord(GameContent.ItemsTable, typeof(ItemDefinition), potion);
        var entries = ContentPath.Root.Key("fx").Element(0);
        var effectSchema = Session.Value.Catalogs.ContentTypes.GetValueCodec(typeof(Game.Effects.Effect)).Schema;
        record.InsertElement(entries, effectSchema, "e", -1, new JsonObject { ["$"] = "DirectDamage" });
        record.RemoveElement(entries, "e", 0);

        var differences = CompareItems(potion, record.Json).ToList();

        Assert.HasCount(2, differences, record.Json);
        Assert.AreEqual(ContentDifferenceKind.Added, differences[0].Kind);
        Assert.AreEqual("fx[0].e[0]", differences[0].CurrentPath!.Text);
        Assert.AreEqual(ContentDifferenceKind.Removed, differences[1].Kind);
        Assert.AreEqual("fx[0].e[0]", differences[1].BaselinePath!.Text);
        Assert.AreEqual("fx[0].e", differences[1].RemovedFrom!.Text);
    }

    [TestMethod]
    [DataRow(",\"c\":3", ",\"c\":5")]
    [DataRow("", ",\"sh\":\"Dark\"")]
    [DataRow(",\"sh\":\"Dark\"", "")]
    [DataRow("", ",\"lid\":{\"h\":2}")]
    [DataRow(",\"lid\":{\"h\":2}", "")]
    [DataRow(",\"lid\":{\"h\":2}", ",\"lid\":{\"h\":5}")]
    [DataRow(",\"pt\":{\"$\":\"Peg\",\"ln\":1}", ",\"pt\":{\"$\":\"Hook\",\"ln\":1,\"op\":true}")]
    [DataRow(",\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Peg\",\"ln\":2}]", ",\"pr\":[{\"$\":\"Hook\",\"ln\":9,\"op\":true},{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Peg\",\"ln\":2}]")]
    [DataRow(",\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Hook\",\"ln\":9,\"op\":true},{\"$\":\"Peg\",\"ln\":2}]", ",\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Peg\",\"ln\":2}]")]
    [DataRow(",\"pr\":[{\"$\":\"Peg\",\"ln\":1}]", "")]
    [DataRow(",\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Peg\",\"ln\":2},{\"$\":\"Peg\",\"ln\":3}]", ",\"pr\":[{\"$\":\"Peg\",\"ln\":3},{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Peg\",\"ln\":2}]")]
    [DataRow(",\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Peg\",\"ln\":2},{\"$\":\"Peg\",\"ln\":3}]", ",\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Peg\",\"ln\":7},{\"$\":\"Peg\",\"ln\":3}]")]
    [DataRow("", ",\"zz\":7")]
    [DataRow(",\"zz\":7", "")]
    [DataRow("", ",\"off\":true")]
    [DataRow(",\"off\":true", "")]
    [DataRow(",\"c\":3,\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Hook\",\"ln\":2}]", ",\"c\":4,\"lid\":{},\"pr\":[{\"$\":\"Hook\",\"ln\":3},{\"$\":\"Peg\",\"ln\":5}]")]
    public void RevertingEachDifference_InTurn_TakesItAway_UntilNoneIsLeft(string baselineFields, string currentFields)
    {
        var baseline = BoxWith(baselineFields);
        var record = new EditableContentRecord(TableName, typeof(Box), BoxWith(currentFields));

        RevertOneByOne(record, baseline, Types);
    }

    [TestMethod]
    public void AMasterItem_EditedInsideItsEffects_RevertsDifferenceByDifference_BackToItsLine()
    {
        var types = Session.Value.Catalogs.ContentTypes;
        var potion = MasterItemLines().Single(line => line.Contains("\"n\":\"Health Potion\"", StringComparison.Ordinal));
        var edited = potion
            .Replace("\"pct\":0.5", "\"pct\":0.75", StringComparison.Ordinal)
            .Replace("\"gv\":5", "\"gv\":9", StringComparison.Ordinal)
            .Replace("[{\"$\":\"DirectHeal\"", "[{\"$\":\"DirectHeal\",\"pct\":0.1},{\"$\":\"DirectHeal\"", StringComparison.Ordinal);
        var record = new EditableContentRecord(GameContent.ItemsTable, typeof(ItemDefinition), edited);

        Assert.HasCount(3, CompareItems(potion, edited));
        RevertOneByOne(record, potion, types);
    }

    [TestMethod]
    public void AHealthPotionsOnlyEffectEntryDeleted_IsOneRemovedElement_TakenFromItsEffectsEntryList()
    {
        var potion = MasterItemLines().Single(line => line.Contains("\"n\":\"Health Potion\"", StringComparison.Ordinal));
        var record = new EditableContentRecord(GameContent.ItemsTable, typeof(ItemDefinition), potion);
        record.RemoveElement(ContentPath.Root.Key("fx").Element(0), "e", 0);

        var difference = CompareItems(potion, record.Json).Single();

        Assert.AreEqual(ContentDifferenceKind.Removed, difference.Kind, record.Json);
        Assert.AreEqual("fx[0].e[0]", difference.BaselinePath!.Text);
        Assert.AreEqual("fx[0].e", difference.RemovedFrom!.Text);
    }

    [TestMethod]
    public void EachSidesValue_ReadsAsAReaderReadsIt()
    {
        static string? NoNames(string table, Guid id) => null;
        var schema = Types.GetValueCodec(typeof(Box)).Schema;
        var count = (schema.FieldOf("c"), ContentFieldKind.Integer);

        Assert.AreEqual("1 (default)", ContentDifferenceText.Value(null, count, NoNames));
        Assert.AreEqual("none", ContentDifferenceText.Value(null, null, NoNames));
        Assert.AreEqual("Dark", ContentDifferenceText.Value("\"Dark\"", (schema.FieldOf("sh"), ContentFieldKind.Enum), NoNames));
        Assert.AreEqual("Hook", ContentDifferenceText.Value(Hook(2), (schema.FieldOf("pt"), ContentFieldKind.Polymorphic), NoNames));
        Assert.AreEqual("Damage.Fire, Magic", ContentDifferenceText.Value("[\"Damage.Fire\",\"Magic\"]", null, NoNames));
        Assert.AreEqual("4", ContentDifferenceText.Value("4", count, NoNames));

        var reference = (ContentField.Reference("r", "Replacement", "Items"), ContentFieldKind.Reference);
        var id = Guid.NewGuid();
        Assert.AreEqual("Health Potion", ContentDifferenceText.Value($"\"{ContentGuid.Format(id)}\"", reference, (table, named) => table == "Items" && named == id ? "Health Potion" : null));

        var moved = CompareBoxes(BoxWith(Parts(Peg(1), Peg(2), Peg(3))), BoxWith(Parts(Peg(3), Peg(1), Peg(2)))).Single();
        Assert.AreEqual("at [2]", ContentDifferenceText.Baseline(moved, null, NoNames));
        Assert.AreEqual("at [0]", ContentDifferenceText.Current(moved, null, NoNames));
        var added = CompareBoxes(BoxWith(string.Empty), BoxWith(",\"lid\":{\"h\":2}")).Single();
        Assert.AreEqual(ContentDifferenceText.Absent, ContentDifferenceText.Baseline(added, null, NoNames));
    }

    [TestMethod]
    public void TheFieldAtAPath_IsFoundThroughObjectsPolymorphicMembersAndListElements()
    {
        var root = JsonNode.Parse(BoxWith(",\"lid\":{\"h\":2},\"pt\":" + Hook(1) + Parts(Peg(1))));

        Assert.AreEqual("h", ContentSchemas.FieldAt(Types, typeof(Box), root, ContentPath.Root.Key("lid").Key("h"))!.Value.Field.Key);
        Assert.AreEqual("op", ContentSchemas.FieldAt(Types, typeof(Box), root, ContentPath.Root.Key("pt").Key("op"))!.Value.Field.Key);
        var element = ContentSchemas.FieldAt(Types, typeof(Box), root, ContentPath.Root.Key("pr").Element(0))!.Value;
        Assert.AreEqual("pr", element.Field.Key);
        Assert.AreEqual(ContentFieldKind.Polymorphic, element.Kind);
        Assert.AreEqual("ln", ContentSchemas.FieldAt(Types, typeof(Box), root, ContentPath.Root.Key("pr").Element(0).Key("ln"))!.Value.Field.Key);
        Assert.IsNull(ContentSchemas.FieldAt(Types, typeof(Box), root, ContentPath.Root.Key("zz")));
    }

    [TestMethod]
    public void ReplacingAListElement_ChangesTheRecordsJson()
    {
        var record = new EditableContentRecord(TableName, typeof(Box), BoxWith(Parts(Peg(1))));

        record.ReplaceElement(ContentPath.Root, "pr", 0, JsonNode.Parse(Peg(4))!);

        Assert.IsTrue(record.IsChanged);
        Assert.AreEqual(BoxWith(Parts(Peg(4))), record.Json);
    }

    private static void RevertOneByOne(EditableContentRecord record, string baseline, ContentTypeRegistry types)
    {
        for (var step = 0; step < 20; step++)
        {
            var differences = ContentRecordDiff.Compare(baseline, record.Json, record.ValueType, types);
            if (differences.Count == 0)
            {
                return;
            }

            ContentRecordRevert.Revert(record, types, differences[0]);
            Assert.IsLessThan(differences.Count, ContentRecordDiff.Compare(baseline, record.Json, record.ValueType, types).Count, $"Reverting {differences[0]} left {record.Json}");
        }

        Assert.Fail($"Still differs after 20 reverts: {record.Json}");
    }

    [TestMethod]
    public void EveryMasterItem_ComparedWithItself_OrRewrittenWithItsKeysReversed_HasNoDifference()
    {
        foreach (var line in MasterItemLines())
        {
            var reversed = new JsonObject(((JsonObject)JsonNode.Parse(line)!).Reverse().Select(property => KeyValuePair.Create(property.Key, property.Value?.DeepClone()))).ToJsonString();

            Assert.IsEmpty(CompareItems(line, line), line);
            Assert.IsEmpty(CompareItems(line, reversed), line);
        }
    }

    [TestMethod]
    public void AHealthPotionsHealChanged_IsNamedInsideItsEffectEntry()
    {
        var potion = MasterItemLines().Single(line => line.Contains("\"n\":\"Health Potion\"", StringComparison.Ordinal));
        var stronger = potion.Replace("\"pct\":0.5", "\"pct\":0.75", StringComparison.Ordinal);

        var difference = CompareItems(potion, stronger).Single();

        Assert.AreEqual(ContentDifferenceKind.Changed, difference.Kind);
        Assert.AreEqual("fx[0].e[0].pct", difference.Path.Text);
        Assert.AreEqual("0.5", difference.BaselineJson);
        Assert.AreEqual("0.75", difference.CurrentJson);
    }
}
