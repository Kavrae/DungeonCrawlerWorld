using System.Text.Json;
using System.Text.Json.Nodes;
using Builders.Editing;
using DungeonCrawlerWorld.Workshop;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Inventory;
using Workshop.Editing;

namespace Workshop.Tests;

/// <summary>Every built-in item as the Items tab holds it: read into a tree that decodes back to its own line, checked and decoded as one, and edited and undone through the workspace.</summary>
[TestClass]
public sealed class ItemsEditingTests
{
    private static readonly Lazy<GameSession> Session = new(() => ContentSession.Build(ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), WorkshopTestContent.Master, []), WorkshopTestContent.Master, [], TextWriter.Null));

    private static (ContentEditing Editing, ContentEditingTable<ItemDefinition> Items) Open()
    {
        var session = Session.Value;
        var workspace = new ContentWorkspace(session);
        var items = new ContentEditingTable<ItemDefinition>(workspace, GameContent.ItemsTable, "Items", session.Catalogs.ItemCatalog.Definitions, item => item.Name, _ => null, session.ContentChecks.ProblemsOf, ActivatableAppearance.Of, ActivatableAppearance.Fields);
        items.EvaluateAll();
        return (new ContentEditing(workspace), items);
    }

    private static ContentRecordDocument<ItemDefinition> Named(ContentEditingTable<ItemDefinition> items, string name) =>
        items.Documents.Single(document => document.Original.Name == name);

    private static IReadOnlyDictionary<Guid, string> MasterLines() =>
        File.ReadAllLines(Path.Combine(GameContent.DefaultMasterDirectory, GameContent.ItemsFileName)).Skip(1)
            .Where(line => line.Length > 0 && !line.Contains("\"off\":true", StringComparison.Ordinal))
            .ToDictionary(line => ContentTable.TryReadId(line, out var id) ? id : Guid.Empty);

    private static ContentSchema SchemaOf(Type type) => Session.Value.Catalogs.ContentTypes.GetValueCodec(type).Schema;

    [TestMethod]
    public void EveryBuiltInItem_DecodesFromItsTree_AndWritesBackToItsOwnLine()
    {
        var (editing, items) = Open();
        var master = MasterLines();

        Assert.HasCount(master.Count, items.Documents);
        foreach (var document in items.Documents)
        {
            Assert.IsNull(document.DecodeFailure, document.Original.Name);
            Assert.AreEqual(master[document.Id], ContentTable.WriteLine(document.LastGood, editing.Workspace.Types), document.Original.Name);
        }
    }

    [TestMethod]
    public void EveryBuiltInItem_HasNoError_AtAnyLayer()
    {
        var (_, items) = Open();

        foreach (var document in items.Documents)
        {
            var errors = document.FieldProblems.Where(problem => !problem.IsWarning).ToList();
            Assert.IsEmpty(errors, $"{document.Original.Name}: {string.Join("; ", errors.Select(problem => $"{problem.Path}: {problem.Message}"))}");
            Assert.IsEmpty(document.CheckProblems, document.Original.Name);
            Assert.IsFalse(document.HasErrors, document.Original.Name);
        }

        Assert.IsEmpty(items.Problems().Where(problem => problem.Severity == WorkshopProblemSeverity.Error).ToList());
    }

    [TestMethod]
    public void AScrollNamingASpellNoActionHas_IsOnlyAWarning()
    {
        var (_, items) = Open();

        var torch = Named(items, "Scroll of Torch");

        Assert.IsTrue(Assert.ContainsSingle(torch.SchemaProblems).IsWarning);
        Assert.IsFalse(torch.HasErrors);
    }

    [TestMethod]
    public void AProblemAtAFieldChangedFromMaster_SaysSoInTheErrorList_AndOneAtAnUnchangedFieldDoesNot()
    {
        var (_, items) = Open();
        var potion = Named(items, "Health Potion");
        var torch = Named(items, "Scroll of Torch");

        items.Apply(potion, new ContentInspectorEdit("Change Gold value", record => record.SetField(ContentPath.Root, SchemaOf(typeof(ItemDefinition)), "gv", JsonValue.Create("lots")), "gv", ChangesShape: false));

        var potionProblem = items.Problems().First(problem => problem.RecordId == potion.Id && problem.FieldPath == "gv");
        StringAssert.EndsWith(potionProblem.Description, "(changed from master)");
        Assert.IsTrue(items.Problems().Where(problem => problem.RecordId == torch.Id).All(problem => !problem.Description.EndsWith("(changed from master)", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AVersionOfARecord_DecodesThroughTheTable_OrSaysWhyItDoesNot()
    {
        var (_, items) = Open();
        var potion = Named(items, "Health Potion");

        Assert.AreEqual("Health Potion", items.Decode(potion.MasterLine!, out var noFailure)!.Name);
        Assert.IsNull(noFailure);
        Assert.IsNull(items.Decode(potion.MasterLine!.Replace("\"gv\":5", "\"gv\":\"lots\"", StringComparison.Ordinal), out var failure));
        Assert.IsFalse(string.IsNullOrEmpty(failure));
    }

    [TestMethod]
    public void WhatTheSchemaCheckRefuses_TheDecodeRefusesToo()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        (string Key, string Value)[] mutations =
        [
            ("zz", "1"),
            ("gv", "\"lots\""),
            ("t", "[\"No.Such.Tag\"]"),
            ("tr", "3"),
            ("ac", "{\"$\":\"NoSuchActivator\"}"),
        ];

        foreach (var (key, value) in mutations)
        {
            var mutation = $"{key}: {value}";
            var record = new EditableContentRecord(GameContent.ItemsTable, typeof(ItemDefinition), wand.Record.Json);
            record.SetField(ContentPath.Root, SchemaOf(typeof(ItemDefinition)), key, JsonNode.Parse(value));

            Assert.IsNotEmpty(ContentSchemaValidation.Validate(record, editing.Workspace.Types, editing.Workspace), mutation);
            using var document = JsonDocument.Parse(record.Json);
            Assert.ThrowsExactly<ContentFormatException>(() => ContentObject.Read<ItemDefinition>(document.RootElement, editing.Workspace.Types, editing.Workspace), mutation);
        }
    }

    [TestMethod]
    public void MakingAPotionAScroll_KeepsItsTargetingAndTiming_AndWantsASpell_UntilOneIsPicked()
    {
        var (editing, items) = Open();
        var potion = Named(items, "Health Potion");
        var itemSchema = SchemaOf(typeof(ItemDefinition));
        Assert.IsTrue(editing.Workspace.Types.TryGetMember(typeof(Game.Modules.Actions.IActionActivator), "Potion", out var potionCodec));
        Assert.IsTrue(editing.Workspace.Types.TryGetMember(typeof(Game.Modules.Actions.IActionActivator), "Scroll", out var scrollCodec));

        items.Apply(potion, new ContentInspectorEdit("Make Activator Scroll", record => record.ChangeType(ContentPath.Root, itemSchema, "ac", "Scroll", potionCodec.Schema, scrollCodec.Schema), null, ChangesShape: true));

        var activator = (JsonObject)potion.Record.Find(ContentPath.Root.Key("ac"))!;
        CollectionAssert.AreEqual(new[] { "$", "tg", "tm" }, activator.Select(property => property.Key).ToArray());
        Assert.AreEqual("ac.sp", potion.SchemaProblems.Single(problem => !problem.IsWarning).Path.Text);
        Assert.Contains("ac.sp", potion.NotFilledInYet);
        Assert.IsFalse(potion.FieldProblems.Any(problem => !problem.IsWarning));
        Assert.IsNotNull(potion.DecodeFailure);
        Assert.IsTrue(potion.HasErrors);
        Assert.AreEqual("Health Potion", potion.LastGood.Name);
        Assert.IsFalse(items.Problems().Any(problem => problem.RecordId == potion.Id && problem.Severity == WorkshopProblemSeverity.Error));

        var heal = Session.Value.Catalogs.ActionCatalog.Definitions.Single(action => action.Name == "Heal");
        items.Apply(potion, new ContentInspectorEdit("Change Spell", record => record.SetField(ContentPath.Root.Key("ac"), scrollCodec.Schema, "sp", JsonValue.Create(ContentGuid.Format(heal.Id))), "ac.sp", ChangesShape: false));

        Assert.IsFalse(potion.HasErrors, string.Join("; ", potion.FieldProblems.Select(problem => problem.Message)) + potion.DecodeFailure);
        Assert.IsInstanceOfType<Game.Modules.Actions.Activators.ScrollActivator>(potion.LastGood.Activator);
    }

    [TestMethod]
    public void UndoAndRedo_PutTheExactJsonBack_AndARunOfOneFieldIsOneStep()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        var original = wand.Record.Json;
        var itemSchema = SchemaOf(typeof(ItemDefinition));
        ContentInspectorEdit SetGold(long value) => new("Change Gold value", record => record.SetField(ContentPath.Root, itemSchema, "gv", JsonValue.Create(value)), "gv", ChangesShape: false);

        items.Apply(wand, SetGold(101));
        items.Apply(wand, SetGold(102));
        items.Apply(wand, SetGold(103));
        var edited = wand.Record.Json;

        Assert.IsTrue(items.HasChanges);
        Assert.AreEqual(103, wand.LastGood.GoldValue);

        StringAssert.StartsWith(editing.Undo(null), "Undid Change Gold value: 1 item");
        Assert.AreEqual(original, wand.Record.Json);
        Assert.AreEqual(wand.Original.GoldValue, wand.LastGood.GoldValue);
        Assert.IsFalse(items.HasChanges);
        Assert.AreEqual("Nothing to undo.", editing.Undo(null));

        editing.Redo(null);
        Assert.AreEqual(edited, wand.Record.Json);
    }

    [TestMethod]
    public void UndoAndRedo_InTheOpenTab_FocusTheFieldTheStepChanged()
    {
        var (editing, items) = Open();
        var view = new RecordingTableView(GameContent.ItemsTable, isOpen: true);
        editing.RegisterView(view);
        var wand = Named(items, "Wand of Fireball");
        items.Apply(wand, new ContentInspectorEdit("Change Gold value", record => record.SetField(ContentPath.Root, SchemaOf(typeof(ItemDefinition)), "gv", JsonValue.Create(5L)), "gv", ChangesShape: false));
        items.Apply(wand, new ContentInspectorEdit("Unset Summary", record => record.RemoveField(ContentPath.Root, "su"), null, ChangesShape: true) { FieldPath = "su" });

        editing.Undo(null);
        Assert.AreEqual((wand.Id, "su"), (view.ShownRecordId, view.FocusedFieldPath));

        editing.Undo(null);
        Assert.AreEqual("gv", view.FocusedFieldPath);

        editing.Redo(null);
        Assert.AreEqual("gv", view.FocusedFieldPath);

        editing.Disable(items, wand.Id, replacement: null);
        editing.Undo(null);
        Assert.AreEqual("off", view.FocusedFieldPath);
    }

    [TestMethod]
    public void UndoOnAnotherTab_OpensTheItemsTab_AndFocusesTheFieldTheStepChanged()
    {
        var (editing, items) = Open();
        editing.RegisterView(new RecordingTableView(GameContent.ItemsTable, isOpen: false));
        var wand = Named(items, "Wand of Fireball");
        items.Apply(wand, new ContentInspectorEdit("Change Gold value", record => record.SetField(ContentPath.Root, SchemaOf(typeof(ItemDefinition)), "gv", JsonValue.Create(5L)), "gv", ChangesShape: false));
        var itemsTab = new RecordingEditingTab("Items");
        var navigator = new TabNavigator([itemsTab], _ => { }, new SilentStatus());

        editing.Undo(navigator);
        Assert.IsTrue(navigator.ProcessPendingRequest());

        Assert.AreEqual((wand.Id, "gv"), (itemsTab.SelectedRecordId, itemsTab.FocusedFieldPath));
    }

    [TestMethod]
    public void UndoFirstPutsBackTypedText_AndTakesNothingFromTheLog()
    {
        var (editing, items) = Open();
        var wand = Named(items, "Wand of Fireball");
        items.Apply(wand, new ContentInspectorEdit("Change Name", record => record.SetField(ContentPath.Root, SchemaOf(typeof(ItemDefinition)), "n", JsonValue.Create("Wand")), "n", ChangesShape: false));
        editing.AttachFieldText(new TypedText());

        Assert.AreEqual("Put back what was typed.", editing.Undo(null));
        Assert.IsTrue(editing.Workspace.Log.CanUndo);
    }

    [TestMethod]
    public void ClearingARequiredName_IsAnErrorAtTheName()
    {
        var (_, items) = Open();
        var wand = Named(items, "Wand of Fireball");

        items.Apply(wand, new ContentInspectorEdit("Change Name", record => record.SetField(ContentPath.Root, SchemaOf(typeof(ItemDefinition)), "n", JsonValue.Create(string.Empty)), "n", ChangesShape: false));

        Assert.AreEqual("n", Assert.ContainsSingle(wand.SchemaProblems).Path.Text);
        Assert.IsTrue(wand.HasErrors);
    }

    [TestMethod]
    public void AFailedCheck_IsLocatedAtTheFieldItsMessageNames()
    {
        var (_, items) = Open();
        var potion = Named(items, "Health Potion");
        var targeting = SchemaOf(typeof(Game.Modules.Actions.TargetingSpec));

        items.Apply(potion, new ContentInspectorEdit("Change Wall bounce count", record => record.SetField(ContentPath.Root.Key("ac").Key("tg"), targeting, "wb", JsonValue.Create(8L)), "ac.tg.wb", ChangesShape: false));

        var check = Assert.ContainsSingle(items.Problems().Where(problem => problem.RecordId == potion.Id && problem.Severity == WorkshopProblemSeverity.Error));
        StringAssert.Contains(check.Description, "wall bounce count");
        Assert.AreEqual("ac.tg.wb", check.FieldPath);
    }

    [TestMethod]
    public void ANewItem_IsChangedFromTheStart_AndUndoTakesItAway_AndRedoBringsItBack()
    {
        var (editing, items) = Open();
        var count = items.Documents.Count;
        var id = Guid.NewGuid();

        var created = items.CreateNew($"{{\"id\":\"{ContentGuid.Format(id)}\",\"n\":\"New item\",\"g\":\"?\"}}", "New item New item");

        Assert.IsNotNull(created);
        Assert.IsTrue(created.IsChanged);
        Assert.IsFalse(created.HasErrors);
        Assert.HasCount(count + 1, items.Documents);

        StringAssert.StartsWith(editing.Undo(null), "Undid New item");
        Assert.HasCount(count, items.Documents);
        Assert.IsNull(items.Find(id));

        editing.Redo(null);
        Assert.AreEqual("New item", items.Find(id)!.LastGood.Name);
    }

    [TestMethod]
    public void TwoItemsSharingASprite_AreBothWarned_AtTheSprite_AndAChangeClearsBoth()
    {
        var (_, items) = Open();
        var health = Named(items, "Health Potion");
        var mana = Named(items, "Regular Mana Potion");
        Assert.AreEqual(health.LastGood.SpriteName, mana.LastGood.SpriteName);

        Assert.IsTrue(health.AppearanceProblems.Any(problem => problem.Path.Text == "sp" && problem.IsWarning));
        Assert.IsTrue(mana.AppearanceProblems.Any(problem => problem.Path.Text == "sp" && problem.IsWarning));
        Assert.IsFalse(health.HasErrors);

        var sharers = items.Documents.Where(document => document != mana && document.LastGood.SpriteName == mana.LastGood.SpriteName).ToList();
        items.Apply(mana, new ContentInspectorEdit("Change Sprite name", record => record.SetField(ContentPath.Root, SchemaOf(typeof(ItemDefinition)), "sp", JsonValue.Create("NoOtherItemUsesThis")), "sp", ChangesShape: false));

        Assert.IsFalse(mana.AppearanceProblems.Any(problem => problem.Path.Text == "sp"));
        if (sharers.Count == 1)
        {
            Assert.IsFalse(health.AppearanceProblems.Any(problem => problem.Path.Text == "sp"));
        }
    }

    [TestMethod]
    public void ChangingAnItemsActivator_AsksFirst_WhileOtherTypeChangesDoNot()
    {
        var itemSchema = SchemaOf(typeof(ItemDefinition));
        var source = new GameInspectorSource(new ContentWorkspace(Session.Value), new UIEngine.UI.ElementPoolService(), null!);
        Assert.IsTrue(itemSchema.TryGetField("ac", out var activator));
        Assert.IsTrue(itemSchema.TryGetField("ct", out var contents));

        Assert.IsNotNull(source.TypeChangeConfirmation(itemSchema, activator));
        Assert.IsNull(source.TypeChangeConfirmation(itemSchema, contents));
    }

    [TestMethod]
    public void ARequiredEnumLeftOut_HasNoValue_WhileAnOptionalOneShowsItsDefault()
    {
        var types = Session.Value.Catalogs.ContentTypes;
        Assert.IsTrue(types.TryGetMember(typeof(Game.Effects.IEffectEntry), "StatusEffectRemoval", out var removal));
        Assert.IsTrue(removal.Schema.TryGetField("ty", out var statusEffect));
        Assert.IsTrue(SchemaOf(typeof(Game.Modules.Actions.ActionTiming)).TryGetField("c", out var category));

        Assert.AreEqual(-1, ContentFieldValues.For(statusEffect, statusEffect.Kind, null, [], []).ValueOf(null).ChoiceIndex);
        Assert.AreEqual(0, ContentFieldValues.For(category, category.Kind, null, [], []).ValueOf(null).ChoiceIndex);
    }

    [TestMethod]
    public void EveryFieldOfEveryRegisteredSchema_HasAnEditor()
    {
        var types = Session.Value.Catalogs.ContentTypes;
        var schemas = types.ValueTypes.Select(type => types.GetValueCodec(type).Schema)
            .Concat(types.PolymorphicBaseTypes.SelectMany(baseType => types.MembersOf(baseType).Select(member => member.Codec.Schema)));

        foreach (var schema in schemas)
        {
            foreach (var field in schema.Fields)
            {
                var kind = field.Kind == ContentFieldKind.List ? field.ElementKind!.Value : field.Kind;
                if (kind is ContentFieldKind.Object or ContentFieldKind.Polymorphic)
                {
                    Assert.IsNotNull(field.ValueType, $"{schema.TypeName}.{field.Key}");
                    continue;
                }

                var editor = ContentFieldValues.For(field, kind, null, [], []).Kind;
                Assert.IsTrue(editor != UIEngine.UI.Fields.FieldValueKind.ReadOnlyText || kind == ContentFieldKind.Guid, $"{schema.TypeName}.{field.Key} has no editor for {kind}");
            }
        }
    }
}

/// <summary>An inspector holding text typed and not yet committed.</summary>
internal sealed class TypedText : IUncommittedFieldText
{
    public bool RevertUncommitted() => true;

    public bool CommitUncommitted() => true;
}

/// <summary>A table's tab, recording which record it was asked to show and which field to focus.</summary>
internal sealed class RecordingTableView(string tableName, bool isOpen) : IContentTableView
{
    public string TableName { get; } = tableName;

    public bool IsOpen { get; } = isOpen;

    public Guid? ShownRecordId { get; private set; }

    public string? FocusedFieldPath { get; private set; }

    public void ShowRecord(Guid id, bool rebuildInspector) => ShownRecordId = id;

    public void FocusField(string fieldPath) => FocusedFieldPath = fieldPath;
}

/// <summary>An editing tab the navigator opens, recording the record it selects and the field it focuses.</summary>
internal sealed class RecordingEditingTab(string title) : IWorkshopEditingTab
{
    public string Title { get; } = title;

    public Guid? SelectedRecordId { get; private set; }

    public string? FocusedFieldPath { get; private set; }

    public bool HasUnsavedChanges => false;

    public IReadOnlyList<WorkshopProblem> Problems => [];

    public int ProblemsVersion => 0;

    public IReadOnlyList<WorkshopPendingRecord> PendingPublish => [];

    public void Open(WorkshopTabContext context, Microsoft.Xna.Framework.Rectangle area) { }

    public void Resize(Microsoft.Xna.Framework.Rectangle area) { }

    public void Close() { }

    public bool TrySelectRecord(Guid recordId)
    {
        SelectedRecordId = recordId;
        return true;
    }

    public void FocusField(string fieldPath) => FocusedFieldPath = fieldPath;
}

internal sealed class SilentStatus : IWorkshopStatus
{
    public void Show(string message) { }
}
