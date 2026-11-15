using System.Text.Json;
using Engine.Content;
using Game.Content;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Fields;
using Workshop.Editing;
using static Workshop.Tests.EditingTestSchema;

namespace Workshop.Tests;

/// <summary>The generated inspector over a real layer stack and UiInputController: a row for every field, values shown as written when they can't be edited, and every edit handed to its owner.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ContentInspectorTests
{
    private static readonly FontService SharedFonts = new("Fonts");
    private static readonly Vector2 ScreenSize = new(1600, 900);

    private sealed class NoAdditions : IContentInspectorSource
    {
        public IReadOnlyList<ContentReferenceChoice> ReferenceChoices(string tableName) => [];

        public IReadOnlyList<string>? TextChoices(ContentSchema schema, ContentField field) => null;

        public string? NoteFor(ContentSchema schema, ContentField field, EditableContentRecord record) => null;

        public ContentInspectorDecoration? DecorationFor(ContentSchema schema) => null;
    }

    private sealed class Harness
    {
        private KeyboardState _keyboard;

        public Harness(ContentTypeRegistry? types = null)
        {
            var render = new UiRenderService();
            Pool = new ElementPoolService();
            Layers = new UiLayerStack();
            var services = new ElementServices(SharedFonts, Pool, render);
            var dropdowns = new DropdownController(Pool, render) { ScreenBoundsOverrideForTests = new Rectangle(0, 0, (int)ScreenSize.X, (int)ScreenSize.Y) };
            Pool.RegisterFactory(() => new Window(services));
            Pool.RegisterFactory(() => new TextWindow(services));
            Pool.RegisterFactory(() => new Button(services));
            Pool.RegisterFactory(() => new Dropdown(services));
            Pool.RegisterFactory(() => new Toggle(services));
            Pool.RegisterFactory(() => new TextBox(services));
            Pool.RegisterFactory(() => new NumericField(services));
            Pool.RegisterFactory(() => new ColorField(services));
            Pool.RegisterFactory(() => new DropdownField(services, dropdowns));
            Pool.RegisterFactory(() => new SearchableChoiceField(services, dropdowns));
            Pool.RegisterFactory(() => new MultiChoiceField(services, dropdowns));
            Pool.RegisterFactory(() => new FieldRow(services));
            Pool.RegisterFactory(() => new FieldSectionHeader(services));
            dropdowns.Initialize(Layers);
            Input = new UiInputController(Layers, ScreenSize, new PointerState(), [], [], dropdowns) { StartTextInput = static () => { }, StopTextInput = static () => { } };

            Host = Pool.CreateElement<Window>(null, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(20, 20), Size = new Vector2(700, 800), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserScrollVertical = true },
            });
            Host.Initialize();
            Layers.Add(UiLayer.Base, Host);

            Inspector = new ContentInspector(Pool, types ?? Types, new NoAdditions(), element => Input.FocusElement(element));
            Inspector.Attach(Host);
            Inspector.EditRequested += edit => Edits.Add(edit);
        }

        public ElementPoolService Pool { get; }

        public UiLayerStack Layers { get; }

        public UiInputController Input { get; }

        public Window Host { get; }

        public ContentInspector Inspector { get; }

        public List<ContentInspectorEdit> Edits { get; } = [];

        public void Frame() => Frame(new MouseState());

        public void Press(params Keys[] keys)
        {
            _keyboard = new KeyboardState(keys);
            Frame();
            _keyboard = new KeyboardState();
            Frame();
        }

        public void Click(Point position)
        {
            Frame(Mouse(position, ButtonState.Released));
            Frame(Mouse(position, ButtonState.Pressed));
            Frame(Mouse(position, ButtonState.Released));
            Frame(Mouse(position, ButtonState.Released));
        }

        private static MouseState Mouse(Point position, ButtonState left) =>
            new(position.X, position.Y, 0, left, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

        private void Frame(MouseState mouse)
        {
            Input.Update(_keyboard, mouse);
            foreach (var layer in UiLayerStack.LayersAscending())
            {
                foreach (var element in Layers[layer].ToArray())
                {
                    element.Update(new GameTime());
                }
            }

            Inspector.Update();
        }

        public IReadOnlyList<FieldRow> FieldRows => [.. Host.ChildElements.OfType<FieldRow>()];

        public IReadOnlyList<FieldSectionHeader> Headers => [.. Host.ChildElements.OfType<FieldSectionHeader>()];

        public FieldRow Row(string label) => FieldRows.First(row => row.Description.Label == label);
    }

    private static string HeaderTitle(FieldSectionHeader header) => header.Title;

    private static string BoxWith(string fields) => $"{{\"id\":\"{ContentGuid.Format(BoxId)}\",\"n\":\"Crate\"{fields}}}";

    private const string ThreeParts = ",\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Hook\",\"ln\":2,\"op\":true},{\"$\":\"Peg\",\"ln\":3}]";

    /// <summary>Shows current compared with baseline as "Master", every edit applied at once and the rows rebuilt, as a tab does.</summary>
    private static (Harness Harness, EditableContentRecord Record) ShowCompared(string baseline, string current)
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), current);
        harness.Inspector.ComparisonOf = shown => new ContentComparison("Master", ContentRecordDiff.Compare(baseline, shown.Json, typeof(Box), Types));
        harness.Inspector.EditRequested += edit =>
        {
            edit.Apply(record);
            harness.Inspector.RequestRebuild([]);
        };
        harness.Inspector.Show(record, []);
        return (harness, record);
    }

    [TestMethod]
    public void EveryFieldOfTheSchema_HasARow_InSchemaOrder()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);

        harness.Inspector.Show(record, []);

        CollectionAssert.AreEqual(new[] { "Id", "Name", "Count", "Weight", "Sealed", "Shade", "Part", "Type", "Length", "Note" }, harness.FieldRows.Select(row => row.Description.Label).ToArray());
        CollectionAssert.AreEqual(new[] { "Lid (none)", "Parts (1)", "[0] Peg" }, harness.Headers.Select(HeaderTitle).ToArray());
        Assert.AreEqual(FieldValueKind.ReadOnlyText, harness.Row("Id").Description.Kind);
        Assert.AreEqual(FieldValueKind.Integer, harness.Row("Count").Description.Kind);
        Assert.AreEqual(FieldValueKind.Choice, harness.Row("Shade").Description.Kind);
        Assert.IsTrue(harness.Row("Count").Description.IsSet);
        Assert.IsFalse(harness.Row("Weight").Description.IsSet);
    }

    [TestMethod]
    public void AWrongKindAndAnUnknownKey_AreShownAsWritten_ReadOnly()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), $"{{\"id\":\"{ContentGuid.Format(BoxId)}\",\"n\":\"Crate\",\"c\":\"lots\",\"zz\":7}}");

        harness.Inspector.Show(record, ContentSchemaValidation.Validate(record, Types, AllReferencesExist.Instance));

        Assert.AreEqual(FieldValueKind.ReadOnlyText, harness.Row("Count").Description.Kind);
        Assert.AreEqual(FieldValueKind.ReadOnlyText, harness.Row("zz (unknown field)").Description.Kind);
        Assert.IsNotNull(harness.Row("Count").TooltipText);
    }

    [TestMethod]
    public void ANumberTypedAndSubmitted_IsOneEdit_NamingItsField()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);
        harness.Inspector.Show(record, []);
        harness.Frame();
        var field = (NumericField)harness.Row("Count").Editor;

        harness.Input.FocusElement(field);
        field.SetText("7");
        harness.Press(Keys.Enter);

        var edit = Assert.ContainsSingle(harness.Edits);
        Assert.AreEqual("c", edit.MergeKey);
        Assert.IsFalse(edit.ChangesShape);
        edit.Apply(record);
        Assert.AreEqual(7, record.Find(ContentPath.Root.Key("c"))!.GetValue<long>());
    }

    [TestMethod]
    public void AddingAnObject_IsAStructuralEdit_ThatRebuildsTheRows_AndNoRowOffersAButtonOfItsOwn()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);
        harness.Inspector.Show(record, []);
        harness.Inspector.EditRequested += edit =>
        {
            edit.Apply(record);
            harness.Inspector.RequestRebuild([]);
        };

        harness.Click(harness.Headers.First(header => header.Title == "Lid (none)").Buttons[0].Rectangle.Center);

        Assert.IsTrue(Assert.ContainsSingle(harness.Edits).ChangesShape);
        CollectionAssert.Contains(harness.Headers.Select(header => header.Title).ToArray(), "Lid");
        CollectionAssert.Contains(harness.FieldRows.Select(row => row.Description.Label).ToArray(), "Height");
        Assert.IsFalse(harness.FieldRows.Any(row => row.ChildElements.OfType<Button>().Any(button => button.IsVisible)));
    }

    [TestMethod]
    public void FocusingAField_OpensWhatHidesIt_AndFocusesItsControl()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);
        harness.Inspector.Show(record, []);
        var parts = harness.Headers.First(header => header.Title == "Parts (1)");

        harness.Click(new Point(parts.Rectangle.Left + 30, parts.Rectangle.Center.Y));
        Assert.IsFalse(harness.FieldRows.Any(row => row.Description.Label == "Length"));

        harness.Inspector.FocusField(ContentPath.Root.Key("pr").Element(0).Key("ln"));
        harness.Frame();

        Assert.IsTrue(harness.Row("Length").Editor.IsFocused);
    }

    [TestMethod]
    public void AProblem_ColoursTheNearestRowShown()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);
        var problem = new ContentFieldProblem(ContentPath.Root.Key("pr").Element(0).Key("missing"), "Parts[0] > missing", "Something is wrong.");

        harness.Inspector.Show(record, [problem]);

        Assert.AreEqual("Something is wrong.", harness.Headers.First(header => HeaderTitle(header) == "[0] Peg").TooltipText);
    }

    [TestMethod]
    public void AHintInTheDisplayName_GoesToTheTooltip_AndAMultilineFieldGetsATallRow()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);

        harness.Inspector.Show(record, []);

        Assert.AreEqual("How many in the box", harness.Row("Count").Description.Hint);
        StringAssert.StartsWith(harness.Row("Count").TooltipText, "How many in the box");
        Assert.AreEqual(FieldValueKind.MultilineText, harness.Row("Note").Description.Kind);
        Assert.AreEqual(FieldRow.MultilineRowHeight, harness.Row("Note").CurrentSize.Y);
    }

    [TestMethod]
    public void APressOnADisabledButton_IsTaken_AndNeverReachesTheHeadingBehindIt()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);
        harness.Inspector.Show(record, []);
        var element = harness.Headers.First(header => header.Title == "[0] Peg");
        var moveUp = element.Buttons[0];
        Assert.IsFalse(moveUp.Enabled);

        harness.Click(moveUp.Rectangle.Center);

        Assert.IsEmpty(harness.Edits);
        Assert.IsTrue(harness.Headers.First(header => header.Title == "[0] Peg").IsExpanded);
        Assert.IsTrue(harness.FieldRows.Any(row => row.Description.Label == "Length"));
    }

    [TestMethod]
    public void Tab_MovesToTheNextEditableControl_SkippingReadOnlyOnes_AndShiftTabGoesBack()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);
        harness.Inspector.Show(record, []);
        harness.Frame();

        harness.Input.FocusElement(harness.Row("Note").Editor);
        harness.Press(Keys.Tab);
        Assert.IsTrue(harness.Row("Name").Editor.IsFocused, "Tab wraps past the read-only Id");

        harness.Press(Keys.Tab);
        Assert.IsTrue(harness.Row("Count").Editor.IsFocused);

        harness.Press(Keys.Tab, Keys.LeftShift);
        Assert.IsTrue(harness.Row("Name").Editor.IsFocused);
    }

    [TestMethod]
    public void ARebuild_AndShowingTheRecordAgainInANewHost_KeepTheScroll()
    {
        var harness = new Harness();
        var parts = string.Join(",", Enumerable.Range(0, 40).Select(index => $"{{\"$\":\"Hook\",\"ln\":{index}}}"));
        var record = new EditableContentRecord(TableName, typeof(Box), $"{{\"id\":\"{ContentGuid.Format(BoxId)}\",\"n\":\"Crate\",\"pr\":[{parts}]}}");
        harness.Inspector.Show(record, []);
        harness.Frame();
        Assert.IsGreaterThan(300f, harness.Host.MaxScrollOffset.Y);

        harness.Host.ScrollTo(new Vector2(0, 300));
        harness.Inspector.RequestRebuild([]);
        harness.Frame();
        Assert.AreEqual(300f, harness.Host.ScrollOffset.Y);

        harness.Inspector.RememberScroll();
        harness.Pool.CloseAllChildren(harness.Host);
        harness.Host.ScrollTo(Vector2.Zero);
        harness.Inspector.Attach(harness.Host);
        harness.Inspector.Show(record, []);

        Assert.IsNotEmpty(harness.FieldRows);
        Assert.AreEqual(300f, harness.Host.ScrollOffset.Y);
    }

    [TestMethod]
    public void AMultiChoiceList_DrawsACheckboxOnEveryChoice()
    {
        var harness = new Harness();
        var field = harness.Pool.CreateElement<MultiChoiceField>(harness.Host, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(200, 24), DisplayMode = ElementDisplayMode.Fixed },
        });
        field.Configure(["Adjacent", "Self"], [1]);
        field.Initialize();
        harness.Host.AddChild(field);

        harness.Click(field.Rectangle.Center);
        var rows = harness.Layers[UiLayer.ContextMenu].OfType<Dropdown>().Single(dropdown => dropdown.IsVisible).ChildElements.OfType<Button>().ToList();

        CollectionAssert.AreEqual(new bool?[] { false, true }, rows.Select(row => row.CheckBoxState).ToArray());
        CollectionAssert.AreEqual(new[] { "Adjacent", "Self" }, rows.Select(row => row.LeftText).ToArray());
    }

    [TestMethod]
    public void ACheckboxIsItsOwnSquare_AndANumberOnlyAsWideAsItsLimitsNeed()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);

        harness.Inspector.Show(record, []);

        Assert.AreEqual(Toggle.CheckboxSize, harness.Row("Sealed").Editor.CurrentSize.X);
        Assert.IsLessThan(80f, harness.Row("Count").Editor.CurrentSize.X);
        Assert.IsGreaterThan(300f, harness.Row("Name").Editor.CurrentSize.X);
    }

    [TestMethod]
    public void AListElementsMoveAndRemoveButtons_FollowItsTitle_AndAddStaysAtTheRight()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);

        harness.Inspector.Show(record, []);

        var element = harness.Headers.First(header => header.Title == "[0] Peg");
        Assert.IsLessThan(element.Rectangle.Center.X, element.Buttons[^1].Rectangle.Right);
        var parts = harness.Headers.First(header => header.Title == "Parts (1)");
        Assert.IsGreaterThan(parts.Rectangle.Right - 80, parts.Buttons[0].Rectangle.Left);
    }

    [TestMethod]
    public void TabWithAListOpen_ClosesItKeepingWhatWasChosen_AndMovesOn()
    {
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxJson);
        harness.Inspector.Show(record, []);
        harness.Frame();
        var shade = (DropdownField)harness.Row("Shade").Editor;

        harness.Click(shade.Rectangle.Center);
        Assert.IsTrue(shade.IsListOpen);
        harness.Press(Keys.Down);
        harness.Press(Keys.Tab);

        Assert.IsFalse(shade.IsListOpen);
        Assert.IsEmpty(harness.Edits);
        Assert.IsTrue(harness.Row("Part").Editor.IsFocused);
    }

    private static TElement AddField<TElement>(Harness harness, Action<TElement> configure)
        where TElement : Element
    {
        var field = harness.Pool.CreateElement<TElement>(harness.Host, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(400, 24), DisplayMode = ElementDisplayMode.Fixed },
        });
        configure(field);
        harness.Host.AddChild(field);
        harness.Frame();
        return field;
    }

    private static Dropdown OpenList(Harness harness) => harness.Layers[UiLayer.ContextMenu].OfType<Dropdown>().Single(dropdown => dropdown.IsVisible);

    [TestMethod]
    public void TickingAMultiChoiceRow_KeepsTheListOpen_WithTheRowTurned()
    {
        var harness = new Harness();
        var chosen = new List<IReadOnlyList<int>>();
        var field = AddField<MultiChoiceField>(harness, multi =>
        {
            multi.Configure(["Adjacent", "Self", "Line"], [1]);
            multi.ChoicesChanged += chosen.Add;
        });

        harness.Click(field.Rectangle.Center);
        var adjacent = OpenList(harness).ChildElements.OfType<Button>().First();
        harness.Click(adjacent.Rectangle.Center);

        Assert.IsTrue(field.IsListOpen);
        Assert.IsTrue(adjacent.CheckBoxState);
        CollectionAssert.AreEqual(new[] { 0, 1 }, chosen.Single().ToArray());

        harness.Click(adjacent.Rectangle.Center);

        Assert.IsTrue(field.IsListOpen);
        Assert.IsFalse(adjacent.CheckBoxState);
        CollectionAssert.AreEqual(new[] { 1 }, field.ChosenIndices.ToArray());
    }

    [TestMethod]
    public void EnterInASearchableField_WithNoChoiceNamedExactly_CommitsWhatWasTyped()
    {
        var harness = new Harness();
        var chosen = new List<(int Index, string Text)>();
        var field = AddField<SearchableChoiceField>(harness, searchable =>
        {
            searchable.Configure(["Goblin", "Goblin Foreman", "Orc"], 0, allowsFreeText: true);
            searchable.Chosen += (index, text) => chosen.Add((index, text));
        });

        harness.Input.FocusElement(field);
        field.SetText("gob");
        Assert.IsTrue(field.IsListOpen);
        harness.Press(Keys.Enter);

        Assert.IsFalse(field.IsListOpen);
        Assert.AreEqual((-1, "gob"), chosen.Single());
    }

    [TestMethod]
    public void EnterInASearchableField_ChoosesTheChoiceTypedExactly()
    {
        var harness = new Harness();
        var field = AddField<SearchableChoiceField>(harness, searchable => searchable.Configure(["Goblin", "Goblin Foreman", "Orc"], 0, allowsFreeText: false));

        harness.Input.FocusElement(field);
        field.SetText("goblin foreman");
        harness.Press(Keys.Enter);

        Assert.AreEqual(1, field.SelectedIndex);
        Assert.AreEqual("Goblin Foreman", field.CommittedText);
    }

    [TestMethod]
    public void CommitUncommitted_TakesAColourAndASearchableFieldsTypedText()
    {
        var harness = new Harness();
        var committed = new List<FieldValue>();
        var colour = AddField<FieldRow>(harness, row =>
        {
            row.Configure(new FieldEditorDescription("Glow", FieldValueKind.Color), FieldValue.OfColor(Color.Red));
            row.Committed += committed.Add;
        });
        var sprite = AddField<FieldRow>(harness, row =>
        {
            row.Configure(new FieldEditorDescription("Sprite", FieldValueKind.SearchableChoice) { Choices = ["goblin", "orc"], AllowsFreeText = true }, FieldValue.OfChoice(0, "goblin"));
            row.Committed += committed.Add;
        });

        ((ColorField)colour.Editor).SetText("#00FF00");
        harness.Input.FocusElement(sprite.Editor);
        ((TextBox)sprite.Editor).SetText("troll");

        Assert.IsTrue(colour.CommitUncommitted());
        Assert.IsTrue(sprite.CommitUncommitted());
        Assert.IsFalse(colour.CommitUncommitted());

        Assert.AreEqual(new Color(0, 255, 0), committed[0].Color);
        Assert.AreEqual((-1, "troll"), (committed[1].ChoiceIndex, committed[1].Text));
    }

    [TestMethod]
    public void TabFromAWindowsOnlyTabStop_CyclesWindows()
    {
        var harness = new Harness();
        var other = harness.Pool.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(900, 20), Size = new Vector2(200, 200), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
        });
        other.Initialize();
        harness.Layers.Add(UiLayer.Base, other);
        var box = AddField<TextBox>(harness, _ => { });

        harness.Input.FocusElement(box);
        harness.Press(Keys.Tab);

        Assert.IsFalse(box.IsFocused);
    }

    [TestMethod]
    public void EveryValueKind_RoundTripsThroughItsRowValue()
    {
        var schema = Types.GetValueCodec(typeof(Box)).Schema;
        using var document = JsonDocument.Parse($"{{\"n\":\"Crate\",\"c\":4,\"w\":2.5,\"s\":true,\"sh\":\"Dark\"}}");
        foreach (var key in new[] { "n", "c", "w", "s", "sh" })
        {
            Assert.IsTrue(schema.TryGetField(key, out var field));
            var values = ContentFieldValues.For(field, field.Kind, null, [], []);
            var json = document.RootElement.GetProperty(key);

            Assert.IsTrue(values.CanShow(json), key);
            Assert.AreEqual(json.GetRawText(), values.JsonOf(values.ValueOf(json))!.ToJsonString(), key);
        }
    }

    [TestMethod]
    public void TheHealthPotionsOnlyEffectEntry_Deleted_LeavesADimmedRowInItsPlace()
    {
        var types = ItemSession.Value.Catalogs.ContentTypes;
        var potion = File.ReadAllLines(Path.Combine(GameContent.DefaultMasterDirectory, GameContent.ItemsFileName)).Single(line => line.Contains("\"n\":\"Health Potion\"", StringComparison.Ordinal));
        var harness = new Harness(types);
        var record = new EditableContentRecord(GameContent.ItemsTable, typeof(ItemDefinition), potion);
        harness.Inspector.ComparisonOf = shown => new ContentComparison("Master", ContentRecordDiff.Compare(potion, shown.Json, typeof(ItemDefinition), types));
        harness.Inspector.EditRequested += edit =>
        {
            edit.Apply(record);
            harness.Inspector.RequestRebuild([]);
        };
        harness.Inspector.Show(record, []);
        var directHeal = harness.Headers.Single(header => header.Title == "[0] DirectHeal");

        harness.Click(directHeal.Buttons[2].Rectangle.Center);

        Assert.AreEqual("Remove Entries[0]", harness.Edits[^1].Description, record.Json);
        var titles = string.Join(" | ", harness.Host.ChildElements.Select(child => child switch { FieldSectionHeader header => header.DrawnTitle, FieldRow row => row.Description.Label, _ => child.GetType().Name }));
        Assert.IsTrue(harness.FieldRows.Any(row => row.Description.Label == "[0] DirectHeal (removed)"), titles);
    }

    [TestMethod]
    public void TheHealthPotionGivenASecondEntry_ThenLosingItsFirst_ShowsTheFirstDimmed_AndTheSecondAdded()
    {
        var types = ItemSession.Value.Catalogs.ContentTypes;
        var potion = File.ReadAllLines(Path.Combine(GameContent.DefaultMasterDirectory, GameContent.ItemsFileName)).Single(line => line.Contains("\"n\":\"Health Potion\"", StringComparison.Ordinal));
        var harness = new Harness(types);
        var record = new EditableContentRecord(GameContent.ItemsTable, typeof(ItemDefinition), potion);
        harness.Inspector.ComparisonOf = shown => new ContentComparison("Master", ContentRecordDiff.Compare(potion, shown.Json, typeof(ItemDefinition), types));
        harness.Inspector.EditRequested += edit =>
        {
            edit.Apply(record);
            harness.Inspector.RequestRebuild([]);
        };
        harness.Inspector.Show(record, []);

        harness.Click(harness.Headers.Single(header => header.Title == "Entries (1)").Buttons[0].Rectangle.Center);
        var added = harness.Headers.Single(header => header.Title.StartsWith("[1] ", StringComparison.Ordinal));
        Assert.AreNotEqual("[1] DirectHeal", added.Title);
        harness.Click(harness.Headers.Single(header => header.Title == "[0] DirectHeal").Buttons[2].Rectangle.Center);

        var removed = harness.Row("[0] DirectHeal (removed)");
        Assert.IsTrue(removed.IsChangeMarked);
        var remaining = harness.Headers.Single(header => header.Title.StartsWith("[0] ", StringComparison.Ordinal));
        StringAssert.Contains(remaining.TooltipText, "Added: Master doesn't have it");
        var order = harness.Host.ChildElements.ToList();
        Assert.IsLessThan(order.IndexOf(remaining), order.IndexOf(removed));
    }

    private static readonly Lazy<Game.Bootstrap.GameSession> ItemSession = new(() => DungeonCrawlerWorld.Workshop.ContentSession.Build(DungeonCrawlerWorld.Workshop.ContentSession.ValidateMods(Path.Combine(AppContext.BaseDirectory, "NoMods"), WorkshopTestContent.Master, []), WorkshopTestContent.Master, [], TextWriter.Null));

    [TestMethod]
    public void ALabelLongerThanItsColumn_IsDrawnCut_WithTheWholeLabelInItsTooltip()
    {
        var harness = new Harness();
        const string longLabel = "[0] StatusEffectImmunityGrant with a label far too long for its column (removed)";
        var row = harness.Pool.CreateElement<FieldRow>(harness.Host, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(600, FieldRow.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
        });
        row.Configure(new FieldEditorDescription(longLabel, FieldValueKind.ReadOnlyText) { Depth = 3 }, FieldValue.OfText("{}"));
        harness.Host.AddChild(row);
        var shortRow = harness.Pool.CreateElement<FieldRow>(harness.Host, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(600, FieldRow.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
        });
        shortRow.Configure(new FieldEditorDescription("Name", FieldValueKind.Text), FieldValue.OfText("Crate"));
        harness.Host.AddChild(shortRow);

        StringAssert.EndsWith(row.DrawnLabel, "...");
        Assert.IsLessThan(longLabel.Length, row.DrawnLabel.Length);
        StringAssert.StartsWith(row.TooltipText, longLabel);
        Assert.AreEqual("Name", shortRow.DrawnLabel);
        Assert.IsNull(shortRow.TooltipText);
    }

    [TestMethod]
    public void ALabelOnlyRow_TakesTheRowsWidth_WithItsButtonRightAfterTheText_AndCutsOnlyPastIt()
    {
        var harness = new Harness();
        const string label = "[1] StatusEffectImmunityGrant (removed)";
        FieldRow LabelOnlyRow(string text, float width)
        {
            var row = harness.Pool.CreateElement<FieldRow>(harness.Host, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(width, FieldRow.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            });
            row.Configure(new FieldEditorDescription(text, FieldValueKind.ReadOnlyText) { Depth = 3, IsSet = false, IsLabelOnly = true }, FieldValue.OfText(string.Empty));
            harness.Host.AddChild(row);
            row.SetChangeMarker(true, "Removed", "Put it back");
            return row;
        }

        var wide = LabelOnlyRow(label, 600);
        Assert.AreEqual(label, wide.DrawnLabel);
        Assert.IsFalse(wide.Editor.IsVisible);
        Assert.IsTrue(wide.RevertButton.IsVisible);
        Assert.IsLessThan(wide.Rectangle.Left + 450, wide.RevertButton.Rectangle.Left);

        var narrow = LabelOnlyRow(label + " with far more text than a row this narrow can hold", 300);
        StringAssert.EndsWith(narrow.DrawnLabel, "...");
        Assert.IsLessThanOrEqualTo(narrow.Rectangle.Right, narrow.RevertButton.Rectangle.Right);
    }

    [TestMethod]
    public void ChangedOnly_ShowsEachChangedValue_ItsHeadingsAndRemovedRows_AndHidesTheRest()
    {
        var baseline = BoxWith(",\"c\":3" + ThreeParts);
        var (harness, _) = ShowCompared(baseline, BoxWith(",\"c\":5,\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Peg\",\"ln\":7}]"));

        harness.Inspector.ShowsChangedOnly = true;
        harness.Frame();

        string[] Shown() => [.. harness.Host.ChildElements.Where(child => child.IsVisible).Select(child => child switch { FieldSectionHeader header => header.Title, FieldRow row => row.Description.Label, _ => child.GetType().Name })];
        CollectionAssert.AreEqual(new[] { "Count", "Parts (2)", "[1] Hook (removed)", "[1] Peg", "Length" }, Shown(), string.Join(" | ", Shown()));

        harness.Inspector.ShowsChangedOnly = false;
        harness.Frame();
        CollectionAssert.Contains(Shown(), "Name");
    }

    [TestMethod]
    public void ChangedOnly_WithNothingChanged_SaysSo()
    {
        var (harness, _) = ShowCompared(BoxJson, BoxJson);

        harness.Inspector.ShowsChangedOnly = true;
        harness.Frame();

        Assert.IsFalse(harness.FieldRows.Any(row => row.IsVisible));
        Assert.IsTrue(harness.Host.ChildElements.OfType<TextWindow>().Any(note => note.IsVisible && note.OriginalText.Contains("Nothing differs from Master.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void WithNothingToCompareWith_NoRowIsMarked()
    {
        var harness = new Harness();
        harness.Inspector.Show(new EditableContentRecord(TableName, typeof(Box), BoxJson), []);

        Assert.IsFalse(harness.FieldRows.Any(row => row.IsChangeMarked));
        Assert.IsFalse(harness.Headers.Any(header => header.IsChangeMarked));
        Assert.IsFalse(harness.FieldRows.Any(row => row.RevertButton.IsVisible));
    }

    [TestMethod]
    public void AChangedValue_IsMarked_WithTheBaselineInItsTooltip_AndEveryHeadingAboveIt_WithoutARevertOfTheirOwn()
    {
        var (harness, _) = ShowCompared(BoxWith(",\"c\":3" + ThreeParts), BoxWith(",\"c\":5" + ThreeParts.Replace("\"ln\":2", "\"ln\":8", StringComparison.Ordinal)));

        var count = harness.Row("Count");
        Assert.IsTrue(count.IsChangeMarked);
        StringAssert.Contains(count.TooltipText, "Master: 3");
        Assert.IsTrue(count.RevertButton.IsVisible);
        Assert.IsFalse(harness.Row("Name").IsChangeMarked);

        var hookLength = harness.FieldRows.Where(row => row.Description.Label == "Length").ElementAt(1);
        Assert.IsTrue(hookLength.IsChangeMarked);
        StringAssert.Contains(hookLength.TooltipText, "Master: 2");

        var parts = harness.Headers.Single(header => header.Title == "Parts (3)");
        var hook = harness.Headers.Single(header => header.Title == "[1] Hook");
        Assert.IsTrue(parts.IsChangeMarked);
        Assert.IsTrue(hook.IsChangeMarked);
        Assert.IsFalse(parts.RevertButton.IsVisible);
        Assert.IsFalse(harness.Headers.Single(header => header.Title == "[0] Peg").IsChangeMarked);
    }

    [TestMethod]
    public void AValueTypedBackToTheBaseline_IsNoLongerMarked()
    {
        var baseline = BoxWith(",\"c\":3");
        var harness = new Harness();
        var record = new EditableContentRecord(TableName, typeof(Box), BoxWith(",\"c\":5"));
        harness.Inspector.ComparisonOf = shown => new ContentComparison("Master", ContentRecordDiff.Compare(baseline, shown.Json, typeof(Box), Types));
        harness.Inspector.EditRequested += edit =>
        {
            edit.Apply(record);
            harness.Inspector.Refresh([]);
        };
        harness.Inspector.Show(record, []);
        harness.Frame();
        Assert.IsTrue(harness.Row("Count").IsChangeMarked);

        var field = (NumericField)harness.Row("Count").Editor;
        harness.Input.FocusElement(field);
        field.SetText("3");
        harness.Press(Keys.Enter);

        Assert.IsFalse(harness.Row("Count").IsChangeMarked);
        Assert.IsFalse(harness.Row("Count").RevertButton.IsVisible);
    }

    [TestMethod]
    public void AClosedHeading_CountsTheChangesItHides()
    {
        var (harness, _) = ShowCompared(BoxWith(ThreeParts), BoxWith(ThreeParts.Replace("\"ln\":2", "\"ln\":8", StringComparison.Ordinal).Replace("\"ln\":3", "\"ln\":9", StringComparison.Ordinal)));
        var parts = harness.Headers.Single(header => header.Title == "Parts (3)");
        Assert.AreEqual("Parts (3)", parts.DrawnTitle);

        harness.Click(new Point(parts.Rectangle.Left + 30, parts.Rectangle.Center.Y));

        Assert.AreEqual("Parts (3) - 2 changed", harness.Headers.Single(header => header.Title == "Parts (3)").DrawnTitle);
    }

    [TestMethod]
    public void ARemovedElement_HasADimmedRowInItsPlace_WhoseRevertPutsItBack()
    {
        var baseline = BoxWith(ThreeParts);
        var (harness, record) = ShowCompared(baseline, BoxWith(",\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Peg\",\"ln\":3}]"));

        var removed = harness.Row("[1] Hook (removed)");
        Assert.IsFalse(removed.Description.IsSet);
        Assert.AreEqual(FieldValueKind.ReadOnlyText, removed.Description.Kind);
        Assert.IsTrue(removed.IsChangeMarked);
        var order = harness.Host.ChildElements.ToList();
        Assert.IsGreaterThan(order.IndexOf(harness.Headers.Single(header => header.Title == "[0] Peg")), order.IndexOf(removed));
        Assert.IsLessThan(order.IndexOf(harness.Headers.Single(header => header.Title == "[1] Peg")), order.IndexOf(removed));

        harness.Click(removed.RevertButton.Rectangle.Center);

        Assert.AreEqual("Revert Parts[1] to Master", harness.Edits[^1].Description);
        Assert.IsEmpty(ContentRecordDiff.Compare(baseline, record.Json, typeof(Box), Types));
        Assert.IsFalse(harness.FieldRows.Any(row => row.Description.Label.EndsWith("(removed)", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AnAddedElement_IsMarkedAdded_AndItsRevertTakesItAway()
    {
        var baseline = BoxWith(",\"pr\":[{\"$\":\"Peg\",\"ln\":1}]");
        var (harness, record) = ShowCompared(baseline, BoxWith(",\"pr\":[{\"$\":\"Peg\",\"ln\":1},{\"$\":\"Hook\",\"ln\":4}]"));
        var added = harness.Headers.Single(header => header.Title == "[1] Hook");

        StringAssert.Contains(added.TooltipText, "Added: Master doesn't have it");
        harness.Click(added.RevertButton.Rectangle.Center);

        Assert.AreEqual(baseline, record.Json);
        Assert.IsFalse(harness.Headers.Any(header => header.IsChangeMarked));
    }

    [TestMethod]
    public void RevertingAField_IsOneStructuralEdit_ThatPutsTheBaselineValueBack()
    {
        var baseline = BoxWith(",\"c\":3,\"sh\":\"Dark\"");
        var (harness, record) = ShowCompared(baseline, BoxWith(",\"c\":5"));
        var shade = harness.Row("Shade");
        StringAssert.Contains(shade.TooltipText, "Master: Dark");

        harness.Click(harness.Row("Count").RevertButton.Rectangle.Center);
        harness.Click(harness.Row("Shade").RevertButton.Rectangle.Center);

        Assert.HasCount(2, harness.Edits);
        Assert.IsTrue(harness.Edits.All(edit => edit.ChangesShape));
        Assert.IsEmpty(ContentRecordDiff.Compare(baseline, record.Json, typeof(Box), Types));
        Assert.IsFalse(harness.FieldRows.Any(row => row.IsChangeMarked));
    }
}
