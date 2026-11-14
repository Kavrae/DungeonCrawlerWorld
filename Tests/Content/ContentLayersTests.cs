using Engine.Content;
using Engine.ECS.Components;
using Engine.Modules;
using Engine.Tags;

namespace Tests.Content;

/// <summary>A table read across master, mod and save layers: who wins for a Guid, what disabling and removing do, and what each failure carries.</summary>
[TestClass]
public sealed class ContentLayersTests
{
    private const string TableName = "Named";
    private const string FileName = "Named.jsonl";

    private static readonly Guid FirstId = new("5b0f6f5e-6b8c-4b1e-9a51-000000000001");
    private static readonly Guid SecondId = new("5b0f6f5e-6b8c-4b1e-9a51-000000000002");
    private static readonly Guid ThirdId = new("5b0f6f5e-6b8c-4b1e-9a51-000000000003");

    private sealed record NamedRecord(Guid Id, string Name);

    private sealed class NamedRecordCodec : ContentCodec<NamedRecord>
    {
        public override ContentSchema Schema { get; } = new(nameof(NamedRecord),
        [
            ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
            ContentField.Text("n", "Name", isRequired: true),
        ]);

        public override void WriteProperties(ContentWriter writer, NamedRecord value)
        {
            writer.Guid(ContentTable.IdKey, value.Id);
            writer.String("n", value.Name);
        }

        public override NamedRecord Read(ContentObject element) => new(element.Guid(ContentTable.IdKey), element.String("n"));
    }

    private sealed class NamedRecordModule(int contentVersion) : IModule
    {
        public string Name => "Named";

        public void DeclareContentTypes(ContentTypeDeclarations contentTypes)
        {
            contentTypes.ContentVersion = contentVersion;
            contentTypes.RegisterValue(new NamedRecordCodec());
        }

        public void RegisterComponents(ComponentRegistration registration)
        {
        }
    }

    private static readonly ContentTypeRegistry Types = Declare(new NamedRecordModule(ContentTypeRegistry.DefaultContentVersion));

    private static ContentTypeRegistry Declare(IModule module) => ContentTypeRegistry.Declare([module], GameplayTagRegistry.Declare([module]));

    private static string Line(Guid id, string name) => $"{{\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"{name}\"}}";

    private static string Disabled(Guid id, Guid? replacement = null) =>
        replacement is { } replacementId
            ? $"{{\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"Off\",\"off\":true,\"rep\":\"{ContentGuid.Format(replacementId)}\"}}"
            : $"{{\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"Off\",\"off\":true}}";

    private static ContentLayer Layer(string name, ContentLayerKind kind, params string[] lines) =>
        ContentLayer.FromText(name, kind, new Dictionary<string, string> { [FileName] = string.Join('\n', [ContentTable.Header(TableName), .. lines]) + "\n" });

    private static LayeredRecords<NamedRecord> Read(List<ContentFailure> failures, params ContentLayer[] layers) =>
        new ContentLayers(layers).ReadTable<NamedRecord>(FileName, TableName, Types, NoContentReferences.Instance, failures);

    private static LayeredRecords<NamedRecord> ReadCleanly(params ContentLayer[] layers)
    {
        var failures = new List<ContentFailure>();
        var records = Read(failures, layers);
        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        return records;
    }

    private static string[] Names(LayeredRecords<NamedRecord> records) => [.. records.Records.Select(record => record.Value.Name)];

    [TestMethod]
    public void AModLayer_AddsReplacesAndDisablesByGuid()
    {
        var master = Layer("Master", ContentLayerKind.Master, Line(FirstId, "First"), Line(SecondId, "Second"));
        var mod = Layer("Mod", ContentLayerKind.Mod, Line(ThirdId, "Third"), Line(FirstId, "Modded First"), Disabled(SecondId, replacement: ThirdId));

        var records = ReadCleanly(master, mod);

        CollectionAssert.AreEqual(new[] { "Modded First", "Third" }, Names(records));
        Assert.AreSame(mod, records.Records[0].Layer);
        Assert.AreEqual(ThirdId, records.Redirects[SecondId]);
    }

    [TestMethod]
    public void ASaveLayer_AddsAndOverrides_AndItsRemovalLineBringsTheLowerRecordBack()
    {
        var master = Layer("Master", ContentLayerKind.Master, Line(FirstId, "First"), Line(SecondId, "Second"));
        var overriding = Layer("Save", ContentLayerKind.Save, Line(FirstId, "Saved First"), Line(ThirdId, "Saved Third"), Line(SecondId, "Saved Second"));
        var removing = Layer("Save", ContentLayerKind.Save, Line(FirstId, "Saved First"), Line(SecondId, "Saved Second"), ContentTable.WriteRemoval(FirstId));

        CollectionAssert.AreEqual(new[] { "Saved First", "Saved Second", "Saved Third" }, Names(ReadCleanly(master, overriding)));

        var restored = ReadCleanly(master, removing);
        CollectionAssert.AreEqual(new[] { "First", "Saved Second" }, Names(restored));
        Assert.AreSame(master, restored.Records[0].Layer);
    }

    [TestMethod]
    public void WithinOneLayer_TheLastLineForAGuidWins_InTheFirstLinesPlace()
    {
        var master = Layer("Master", ContentLayerKind.Master, Line(FirstId, "First"), Line(SecondId, "Second"), Line(FirstId, "Later First"));

        var records = ReadCleanly(master);

        CollectionAssert.AreEqual(new[] { "Later First", "Second" }, Names(records));
        Assert.AreEqual(4, records.Records[0].Line);
    }

    [TestMethod]
    public void ARedirectSetInALowerLayer_SurvivesAHigherLayersOverrideOfItsTarget()
    {
        var master = Layer("Master", ContentLayerKind.Master, Disabled(FirstId, replacement: SecondId), Line(SecondId, "Second"));
        var mod = Layer("Mod", ContentLayerKind.Mod, Line(SecondId, "Modded Second"));

        var records = ReadCleanly(master, mod);

        Assert.AreEqual(SecondId, records.Redirects[FirstId]);
        CollectionAssert.AreEqual(new[] { "Modded Second" }, Names(records));
    }

    [TestMethod]
    public void AHigherLayersEnabledLine_ClearsALowerLayersRedirectFromItsGuid()
    {
        var master = Layer("Master", ContentLayerKind.Master, Disabled(FirstId, replacement: SecondId), Line(SecondId, "Second"));
        var mod = Layer("Mod", ContentLayerKind.Mod, Line(FirstId, "First Again"));

        var records = ReadCleanly(master, mod);

        Assert.IsFalse(records.Redirects.ContainsKey(FirstId));
        CollectionAssert.AreEqual(new[] { "Second", "First Again" }, Names(records));
    }

    [TestMethod]
    public void AFailingRecord_CarriesItsLayer_AndTheRestOfTheLayerLoads()
    {
        var failures = new List<ContentFailure>();
        var master = Layer("Master", ContentLayerKind.Master, Line(FirstId, "First"));
        var save = Layer("My Save", ContentLayerKind.Save, "{ not json", Line(SecondId, "Saved Second"));

        var records = Read(failures, master, save);

        var failure = Assert.ContainsSingle(failures);
        Assert.AreEqual("My Save", failure.Layer);
        Assert.AreEqual(FileName, failure.File);
        StringAssert.StartsWith(failure.ToString(), "[My Save] ");
        CollectionAssert.AreEqual(new[] { "First", "Saved Second" }, Names(records));
    }

    [TestMethod]
    public void AWinningLineThatFailsItsCodec_BringsBackTheLowerLayersRecord_NotAnEarlierLineOfItsOwnLayer()
    {
        var failures = new List<ContentFailure>();
        var master = Layer("Master", ContentLayerKind.Master, Line(FirstId, "First"));
        var save = Layer("Save", ContentLayerKind.Save, Line(FirstId, "Superseded"), $"{{\"id\":\"{ContentGuid.Format(FirstId)}\",\"n\":\"Broken\",\"unknown\":1}}");

        var records = Read(failures, master, save);

        Assert.AreEqual("Save", Assert.ContainsSingle(failures).Layer);
        CollectionAssert.AreEqual(new[] { "First" }, Names(records));
        Assert.AreEqual("Master", records.Records[0].Layer.Name);
    }

    [TestMethod]
    public void ARemovalLineOutsideASave_AndADisabledLineInASave_AreFailures()
    {
        var failures = new List<ContentFailure>();
        var master = Layer("Master", ContentLayerKind.Master, Line(FirstId, "First"), Line(SecondId, "Second"));
        var mod = Layer("Mod", ContentLayerKind.Mod, ContentTable.WriteRemoval(FirstId));
        var save = Layer("Save", ContentLayerKind.Save, Disabled(SecondId));

        var records = Read(failures, master, mod, save);

        Assert.HasCount(2, failures);
        Assert.AreEqual("Mod", failures[0].Layer);
        Assert.AreEqual("Save", failures[1].Layer);
        CollectionAssert.AreEqual(new[] { "First", "Second" }, Names(records));
    }

    [TestMethod]
    public void ARemovalLine_HoldsOnlyItsIdAndTheFlag()
    {
        var failures = new List<ContentFailure>();
        var save = Layer("Save", ContentLayerKind.Save, $"{{\"id\":\"{ContentGuid.Format(FirstId)}\",\"n\":\"First\",\"rm\":true}}");

        Read(failures, save);

        Assert.AreEqual(FirstId, Assert.ContainsSingle(failures).RecordId);
    }

    [TestMethod]
    public void LayersStackMasterModsSaveDraftsInThatOrder()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ContentLayers([Layer("Save", ContentLayerKind.Save), Layer("Master", ContentLayerKind.Master)]));
        Assert.ThrowsExactly<ArgumentException>(() => new ContentLayers([Layer("Master", ContentLayerKind.Master), Layer("Master", ContentLayerKind.Mod)]));
    }

    [TestMethod]
    public void AHeaderWithNoVersions_ReadsEveryModuleAsTheDefault()
    {
        var master = Layer("Master", ContentLayerKind.Master, Line(FirstId, "First"));

        var records = ReadCleanly(master);

        Assert.AreEqual(ContentTypeRegistry.DefaultContentVersion, records.ModuleVersionIn(master, "Named"));
    }

    [TestMethod]
    public void TheWriter_PutsEachModuleAboveTheDefaultVersionInTheHeader_AndTheReaderGivesItBack()
    {
        var versionedTypes = Declare(new NamedRecordModule(contentVersion: 3));

        var text = ContentTable.Write(TableName, [new NamedRecord(FirstId, "First")], versionedTypes);
        var file = ContentTable.Read<NamedRecord>(text, FileName, TableName, versionedTypes, NoContentReferences.Instance, []);

        Assert.AreEqual("{\"fmt\":1,\"table\":\"Named\",\"ver\":{\"Named\":3}}", text.Split('\n')[0]);
        Assert.AreEqual(3, file.VersionOf("Named"));
        Assert.AreEqual(ContentTable.Header(TableName), ContentTable.Write(TableName, [new NamedRecord(FirstId, "First")], Types).Split('\n')[0]);
    }

    [TestMethod]
    public void ALayerOfDocuments_IsResolvedTheSameWay_InFileNameOrderWithinALayer()
    {
        static string Document(Guid id, string name) => $"{{\"fmt\":1,\"table\":\"{TableName}\",\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"{name}\"}}\n";
        var master = ContentLayer.FromText("Master", ContentLayerKind.Master, new Dictionary<string, string>
        {
            ["Docs/b.json"] = Document(SecondId, "Second"),
            ["Docs/a.json"] = Document(FirstId, "First"),
            ["Docs/nested/c.json"] = Document(ThirdId, "Nested"),
        });
        var mod = ContentLayer.FromText("Mod", ContentLayerKind.Mod, new Dictionary<string, string> { ["Docs/z.json"] = Document(FirstId, "Modded First") });
        var failures = new List<ContentFailure>();

        var records = new ContentLayers([master, mod]).ReadDocuments<NamedRecord>("Docs", ".json", TableName, Types, NoContentReferences.Instance, failures);

        Assert.IsEmpty(failures);
        CollectionAssert.AreEqual(new[] { "Modded First", "Second" }, Names(records));
        Assert.AreEqual("Docs/z.json", records.Records[0].File);
        Assert.IsNotNull(records.Records[0].Source);
    }

    [TestMethod]
    public void AnIndexOverALayerHeldInMemory_LoadsEachRecordFromItsOwnBytes_AsReadingTheWholeTableDoes()
    {
        var master = Layer("Master", ContentLayerKind.Master, Line(FirstId, "First"), Line(SecondId, "Sécond"), Line(ThirdId, "Third"));
        var failures = new List<ContentFailure>();
        var layers = new ContentLayers([master]);

        var index = layers.IndexTable(FileName, TableName, static record => record.GetProperty("n").GetString(), failures);
        var loaded = index.Entries.Select(entry => ContentLayers.Load<NamedRecord, string?>(entry, Types, NoContentReferences.Instance, failures)!.Name).ToArray();

        Assert.IsEmpty(failures);
        CollectionAssert.AreEqual(Names(ReadCleanly(master)), loaded);
        CollectionAssert.AreEqual(loaded, index.Entries.Select(entry => entry.Summary).ToArray());
    }
}
