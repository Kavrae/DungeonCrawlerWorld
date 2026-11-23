using System.Text.Json.Nodes;
using Engine.Content;
using Workshop.Editing;
using static Workshop.Tests.Editing.EditingTestSchema;

namespace Workshop.Tests.Editing;

/// <summary>The editor's record, schema check and undo log, over a schema of their own: values kept as written, edited in schema order, checked at their paths, and undone in steps.</summary>
[TestClass]
public sealed class ContentEditingCoreTests
{
    private static ContentSchema BoxSchema => Types.GetValueCodec(typeof(Box)).Schema;

    private static EditableContentRecord NewBox(string? json = null) => new(TableName, typeof(Box), json ?? BoxJson);

    private static IReadOnlyList<ContentFieldProblem> Validate(EditableContentRecord record) =>
        ContentSchemaValidation.Validate(record, Types, AllReferencesExist.Instance);

    [TestMethod]
    [DataRow("Action lock in frames (none: the entity's own)", "Action lock in frames", "None: the entity's own")]
    [DataRow("Projectile hit count (65535: every entity on the way)", "Projectile hit count", "65535: every entity on the way")]
    [DataRow("Gold value", "Gold value", null)]
    [DataRow("Odd (", "Odd (", null)]
    [DataRow("()", "()", null)]
    public void ADisplayName_SplitsIntoItsLabel_AndATrailingHint(string displayName, string label, string? hint)
    {
        var split = ContentFieldLabels.Split(displayName);

        Assert.AreEqual(label, split.Label);
        Assert.AreEqual(hint, split.Hint);
    }

    [TestMethod]
    public void ANewRecord_IsChanged_UntilMarkedUnchanged()
    {
        var record = EditableContentRecord.CreateNew(TableName, typeof(Box), BoxJson);

        Assert.IsTrue(record.IsNew);
        Assert.IsTrue(record.IsChanged);

        record.MarkUnchanged();
        Assert.IsFalse(record.IsChanged);
    }

    [TestMethod]
    public void AProblemMessage_IsLocatedAtTheLongestLabelItNamesAsWholeWords()
    {
        var record = NewBox($"{{\"id\":\"{ContentGuid.Format(BoxId)}\",\"n\":\"Crate\",\"c\":3,\"w\":2,\"pr\":[{{\"$\":\"Hook\",\"ln\":2,\"op\":true}}]}}");

        Assert.AreEqual("w", ContentProblemLocator.Locate(record, Types, "Box 'Crate' has a weight (2) too heavy.")?.Text);
        Assert.AreEqual("pr[0].ln", ContentProblemLocator.Locate(record, Types, "Its length is wrong.")?.Text);
        Assert.IsNull(ContentProblemLocator.Locate(record, Types, "Box 'Crate' is weightless."));
    }

    [TestMethod]
    public void ARecord_KeepsItsJson_AndKnowsWhenItChanged()
    {
        var record = NewBox();

        Assert.AreEqual(BoxId, record.Id);
        Assert.IsFalse(record.IsChanged);

        record.SetField(ContentPath.Root, BoxSchema, "c", JsonValue.Create(4L));
        Assert.IsTrue(record.IsChanged);

        record.SetField(ContentPath.Root, BoxSchema, "c", JsonValue.Create(3L));
        Assert.IsFalse(record.IsChanged);
    }

    [TestMethod]
    public void ARecordsVersion_AdvancesWithEveryChange_AndNeverWithAReading()
    {
        var record = NewBox();
        var version = record.Version;

        _ = record.Json;
        _ = record.IsChanged;
        Assert.AreEqual(version, record.Version);

        record.SetField(ContentPath.Root, BoxSchema, "c", JsonValue.Create(4L));
        Assert.IsGreaterThan(version, record.Version);
        version = record.Version;

        Assert.IsFalse(record.RemoveField(ContentPath.Root, "nothing"));
        Assert.AreEqual(version, record.Version);

        record.RemoveField(ContentPath.Root, "c");
        Assert.IsGreaterThan(version, record.Version);
        Assert.IsFalse(record.Json.Contains("\"c\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ANewKey_GoesInAtItsPlaceInSchemaOrder()
    {
        var record = NewBox();

        record.SetField(ContentPath.Root, BoxSchema, "s", JsonValue.Create(true));
        record.SetField(ContentPath.Root, BoxSchema, "w", JsonValue.Create(1.5f));

        CollectionAssert.AreEqual(new[] { "id", "n", "c", "w", "s", "pr" }, record.Root.Select(property => property.Key).ToArray());
    }

    [TestMethod]
    public void AnUnknownKeyAndAWrongKind_SurviveEditsElsewhere_AndAreFlaggedAtTheirPaths()
    {
        var record = NewBox($"{{\"id\":\"{ContentGuid.Format(BoxId)}\",\"n\":\"Crate\",\"c\":\"lots\",\"zz\":7}}");

        record.SetField(ContentPath.Root, BoxSchema, "s", JsonValue.Create(true));
        var problems = Validate(record);

        StringAssert.Contains(record.Json, "\"c\":\"lots\"");
        StringAssert.Contains(record.Json, "\"zz\":7");
        CollectionAssert.AreEquivalent(new[] { "c", "zz" }, problems.Select(problem => problem.Path.Text).ToArray());
        Assert.IsTrue(problems.All(problem => !problem.IsWarning));
    }

    [TestMethod]
    public void TheSchemaCheck_FindsEachProblemAtItsPath()
    {
        var record = NewBox($"{{\"id\":\"{ContentGuid.Format(BoxId)}\",\"n\":\"\",\"c\":11,\"sh\":\"Purple\",\"lid\":{{\"h\":true}},\"pt\":{{\"$\":\"Nail\"}},\"pr\":[{{\"$\":\"Peg\",\"ln\":1,\"op\":true}}]}}");

        var paths = Validate(record).Select(problem => problem.Path.Text).ToArray();

        CollectionAssert.AreEquivalent(new[] { "n", "c", "sh", "lid.h", "pt", "pr[0].op" }, paths);
    }

    [TestMethod]
    public void AValidBox_HasNoProblems_AndDecodes()
    {
        var record = NewBox();

        Assert.IsEmpty(Validate(record));
        using var document = System.Text.Json.JsonDocument.Parse(record.Json);
        Assert.AreEqual("Crate", ContentObject.Read<Box>(document.RootElement, Types, NoContentReferences.Instance).Name);
    }

    [TestMethod]
    public void ListEdits_InsertMoveAndRemove_AndAnEmptiedListLeavesNoKey()
    {
        var record = NewBox();

        record.InsertElement(ContentPath.Root, BoxSchema, "pr", -1, new JsonObject { ["$"] = "Hook", ["ln"] = 5 });
        record.MoveElement(ContentPath.Root, "pr", 1, 0);
        Assert.AreEqual("Hook", record.Find(ContentPath.Root.Key("pr").Element(0).Key("$"))!.GetValue<string>());

        record.RemoveElement(ContentPath.Root, "pr", 0);
        record.RemoveElement(ContentPath.Root, "pr", 0);
        Assert.IsFalse(record.Root.ContainsKey("pr"));
    }

    [TestMethod]
    public void ChangingAPolymorphicType_KeepsTheKeysBothSchemasDeclareAlike()
    {
        var record = NewBox();
        var pegSchema = ContentSchemas.MemberSchema(Types, typeof(IPart), System.Text.Json.JsonDocument.Parse("{\"$\":\"Peg\"}").RootElement)!;
        Assert.IsTrue(Types.TryGetMember(typeof(IPart), "Hook", out var hook));

        record.ChangeElementType(ContentPath.Root, "pr", 0, "Hook", pegSchema, hook.Schema);

        Assert.AreEqual("{\"$\":\"Hook\",\"ln\":2}", record.Find(ContentPath.Root.Key("pr").Element(0))!.ToJsonString());
    }

    [TestMethod]
    public void APath_ReadsBackFromItsText()
    {
        var path = ContentPath.Root.Key("fx").Element(0).Key("e").Element(12).Key("c");

        Assert.AreEqual("fx[0].e[12].c", path.Text);
        Assert.IsTrue(ContentPath.TryParse(path.Text, out var parsed));
        Assert.AreEqual(path, parsed);
        Assert.IsTrue(path.IsWithin(ContentPath.Root.Key("fx")));
        Assert.IsFalse(ContentPath.TryParse("fx[x]", out _));
    }

    [TestMethod]
    public void TheLog_UndoesAndRedoesExactJson_AndMergesARunOfOneField()
    {
        var log = new ContentEditLog();
        var id = Guid.NewGuid();
        ContentEditStep Step(string before, string after, string? key) => new("Change", TableName, id, [new ContentEditChange(TableName, id, before, after)], key);

        log.Record(Step("a", "b", "c"));
        log.Record(Step("b", "c", "c"));
        log.Record(Step("c", "d", "c"));
        Assert.AreEqual("a", log.NextUndo!.Changes[0].Before);
        Assert.AreEqual("d", log.NextUndo!.Changes[0].After);

        log.SealMerging();
        log.Record(Step("d", "e", "c"));

        Assert.AreEqual("e", log.Undo()!.Changes[0].After);
        Assert.AreEqual("a", log.Undo()!.Changes[0].Before);
        Assert.IsFalse(log.CanUndo);
        Assert.AreEqual("d", log.Redo()!.Changes[0].After);
        Assert.IsTrue(log.CanRedo);

        log.Record(Step("d", "x", null));
        Assert.IsFalse(log.CanRedo);
    }

    [TestMethod]
    public void AMergedRunThatEndsWhereItBegan_IsNoStep()
    {
        var log = new ContentEditLog();
        var id = Guid.NewGuid();

        log.Record(new ContentEditStep("Change", TableName, id, [new ContentEditChange(TableName, id, "a", "b")], "c"));
        log.Record(new ContentEditStep("Change", TableName, id, [new ContentEditChange(TableName, id, "b", "a")], "c"));

        Assert.IsFalse(log.CanUndo);
    }
}
