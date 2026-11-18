using System.Text.Json.Nodes;
using Builders.Browsing;
using Builders.References;
using Engine.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Fields;
using UIEngine.UI.Filtering;
using Workshop;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>A tab that edits one table: every record the Workshop holds in a browser, the selected one's fields in the inspector, generated from its schema, its preview as it reads now, and what it uses and is used by.</summary>
/// <remarks>
/// <para>
/// Everything but what is the table's own: a tab for a table is its title, nouns, browser sections, row look, preview and
/// a new record's starting fields (ItemsTab). Each edit is applied through the workspace's undo log, and the record is
/// evaluated again -- its values checked, decoded through the real codec and the definition checked -- before the preview
/// shows it. A record that doesn't decode keeps its last good preview, with why above the inspector. A mod's record is
/// shown, never edited.
/// </para>
/// <para>
/// Over the inspector, and on a row's right-click menu, are what can be done to the record: Publish (to master, or to
/// Drafts with its reasons when anything keeps it out), Save as Draft, Disable or Enable (a published record; asking, when
/// anything names it, between a replacement and a cascade), Duplicate (an unsaved copy), Delete (a draft freely; a master
/// record only while nothing names it). A row reads "*" while unsaved, then Disabled (greyed, with what replaces it),
/// Draft and "(!)" for errors. The tab reopens on the workspace's session whenever it is rebuilt.
/// </para>
/// <para>
/// A deleted record stays listed, greyed and Deleted, until its deletion is final -- a master record's once published, a
/// draft's once saved -- and is then gone. Until then it is shown but not edited, and Restore, in Delete's place, brings
/// it back.
/// </para>
/// </remarks>
public abstract class ContentEditingTab<T, TPreview> : RecordBrowserTab<ContentRecordDocument<T>, TPreview>, IWorkshopEditingTab, IContentTableView
    where T : class
    where TPreview : Window
{
    private const string LastGoodSuffix = " (last good)";
    private const string CopySuffix = " copy";
    private const float DisableDialogWidth = 560f;
    private const float DisableDialogHeight = 280f;

    private readonly ContentInspector _inspector;

    private Window? _inspectorHost;
    private IReadOnlyList<WorkshopProblem> _problems = [];
    private int _problemsBuiltForVersion = -1;
    private bool _isRefreshRequested;

    protected ContentEditingTab(ContentEditingTable<T> table, ContentEditing editing, IContentInspectorSource inspectorSource, ElementPoolService elementPool)
    {
        Table = table;
        Editing = editing;
        _inspector = new ContentInspector(elementPool, editing.Workspace.Types, inspectorSource, element => Context.FocusElement(element));
        _inspector.EditRequested += OnEditRequested;
        _inspector.ComparisonOf = record => SelectedRecord is { } document && ReferenceEquals(document.Record, record) ? MasterComparisonOf(document) : null;
        Table.DocumentChanged += OnDocumentChanged;
        Table.DocumentRemoved += OnDocumentRemoved;
        editing.RegisterView(this);
        editing.Workspace.SessionReplaced += Reopen;
    }

    protected ContentEditingTable<T> Table { get; }

    protected ContentEditing Editing { get; }

    protected ContentWorkspace Workspace => Editing.Workspace;

    /// <summary>One record of the table, lower case ("item").</summary>
    protected abstract string SingularName { get; }

    /// <summary>The key of a record's name, which a duplicate's is written to.</summary>
    protected abstract string NameKey { get; }

    /// <summary>A new record's fields besides its id, named name: what it needs to decode.</summary>
    protected abstract JsonObject NewRecordFields(string name);

    /// <summary>The browser's sections over definitions, filtered by search.</summary>
    protected abstract IReadOnlyList<RecordBrowserSection<T>> SectionsOf(IEnumerable<T> definitions, string search);

    /// <summary>How definition's row reads, before the markers.</summary>
    protected abstract RecordRowAppearance LookOf(T definition);

    /// <summary>Shows definition in the preview, every row built to contentWidth.</summary>
    protected abstract void ShowDefinition(TPreview preview, T definition, float contentWidth);

    protected override string BrowserTitle => $"{Title} ({Table.Documents.Count})";

    protected override bool HasInspector => true;

    protected override string? NewRecordLabel => "New";

    protected override string? NewRecordTooltip => $"Make a new {SingularName}: Ctrl+S saves it as a draft, Publish puts it in master";

    protected override IEnumerable<ContentRecordDocument<T>> AllRecords => [.. Table.Documents, .. Table.DeletedDocuments];

    /// <summary>Show only, the table's own groups (TableFilterGroups), then Source: master and each mod that supplied a record of the table.</summary>
    protected override IReadOnlyList<ListFilterGroup<ContentRecordDocument<T>>> FilterGroups =>
    [
        ContentRecordFilters.ShowOnly(Table),
        .. TableFilterGroups,
        RecordFilterGroups.Source(AllRecords, static document => document.ModName),
    ];

    /// <summary>The table's own filter groups, between Show only and Source -- none by default.</summary>
    protected virtual IReadOnlyList<ListFilterGroup<ContentRecordDocument<T>>> TableFilterGroups => [];

    protected override Guid IdOf(ContentRecordDocument<T> document) => document.Id;

    public bool HasUnsavedChanges => Table.HasChanges;

    public int ProblemsVersion => Table.ProblemsVersion;

    public IReadOnlyList<WorkshopProblem> Problems
    {
        get
        {
            if (_problemsBuiltForVersion != Table.ProblemsVersion)
            {
                _problemsBuiltForVersion = Table.ProblemsVersion;
                _problems = [.. Table.Problems()];
            }

            return _problems;
        }
    }

    public IReadOnlyList<WorkshopPendingRecord> PendingPublish
    {
        get
        {
            if (_pendingBuiltForVersion != Table.ProblemsVersion)
            {
                _pendingBuiltForVersion = Table.ProblemsVersion;
                _pendingPublish = [.. Table.PendingPublish()];
            }

            return _pendingPublish;
        }
    }

    private IReadOnlyList<WorkshopPendingRecord> _pendingPublish = [];
    private int _pendingBuiltForVersion = -1;

    bool IContentTableView.IsOpen => IsOpen;

    string IContentTableView.TableName => Table.TableName;

    /// <summary>A record with only its starting fields, named "New {singular}", numbered past any already called that -- made through the workspace's log, so undo takes it away.</summary>
    protected override ContentRecordDocument<T>? CreateNewRecord()
    {
        var name = UnusedName($"New {SingularName}");
        var record = new JsonObject { [ContentTable.IdKey] = ContentGuid.Format(Guid.NewGuid()) };
        foreach (var (key, value) in NewRecordFields(name))
        {
            record[key] = value?.DeepClone();
        }

        return Table.CreateNew(record.ToJsonString(), $"New {SingularName} {name}");
    }

    /// <summary>name, or name numbered past every record already called it.</summary>
    private string UnusedName(string name)
    {
        var names = Table.Documents.Select(document => Table.NameOf(document.Id)).ToHashSet(StringComparer.Ordinal);
        var unused = name;
        for (var number = 2; names.Contains(unused); number++)
        {
            unused = $"{name} {number}";
        }

        return unused;
    }

    protected override IReadOnlyList<RecordBrowserSection<ContentRecordDocument<T>>> BuildSections(string search)
    {
        var documentsByDefinition = new Dictionary<T, ContentRecordDocument<T>>(ReferenceEqualityComparer.Instance);
        foreach (var document in AllRecords)
        {
            documentsByDefinition.TryAdd(document.LastGood, document);
        }

        return [.. SectionsOf(documentsByDefinition.Keys, search)
            .Select(section => new RecordBrowserSection<ContentRecordDocument<T>>(section.Heading, [.. section.Records.Select(definition => documentsByDefinition[definition])]))];
    }

    protected override RecordRowAppearance AppearanceOf(ContentRecordDocument<T> document)
    {
        var look = LookOf(document.LastGood);
        var state = Table.StateOf(document);
        var replacementName = document.Replacement is { } replacement ? Table.NameOf(replacement) : null;
        return look with
        {
            Name = LabelOf(document, look.Name),
            Markers = ContentRecordMarkers.Of(state, replacementName),
            IsDimmed = ContentRecordMarkers.IsDimmed(state),
        };
    }

    /// <summary>name, then the mod that supplied the record ("[ExampleMod]"), or the mod the game loads in its place ("[replaced by ExampleMod]").</summary>
    private string LabelOf(ContentRecordDocument<T> document, string name) =>
        document.ReplacedInGameBy is { } mod ? $"{name} [replaced by {mod}]" : Workspace.Origins.Label(Table.TableName, document.Id, name);

    protected override void ShowInPreview(TPreview preview, ContentRecordDocument<T> document, float contentWidth) =>
        ShowDefinition(preview, document.LastGood, contentWidth);

    protected override string PreviewTitleFor(ContentRecordDocument<T> document) =>
        document.DecodeFailure is null ? PreviewTitle : PreviewTitle + LastGoodSuffix;

    protected override IReadOnlyList<WorkshopRelatedRecord> UsesOf(ContentRecordDocument<T> document) =>
        Workspace.UsesOf(Table, document.Id);

    protected override IReadOnlyList<WorkshopRelatedRecord> UsedByOf(ContentRecordDocument<T> document) =>
        Workspace.UsedByOf(Table.TableName, document.Id);

    protected override IReadOnlyList<RecordCommand> CommandsFor(ContentRecordDocument<T> document)
    {
        if (document.IsReadOnly)
        {
            return [];
        }

        if (Table.IsDeleted(document.Id))
        {
            return DeletedCommandsFor(document);
        }

        var isPublishedAsIs = document.IsInMaster && !document.IsDraft && !document.IsChanged;
        var isSavedAsIs = !document.IsChanged;
        var disableRefusal = Workspace.DisableRefusal(Table, document.Id);
        var deleteRefusal = Workspace.DeleteRefusal(Table, document.Id);
        RecordCommand[] reloadMaster = document.MasterMovedTo is null
            ? []
            : [new RecordCommand("Reload Master", "Master changed on disk since this was opened: compare with master as it is now, keeping your edits. Publishing then writes over it.", true, () => ReloadMaster(document))];
        return
        [
            .. reloadMaster,
            new RecordCommand("Publish", isPublishedAsIs ? "Already published." : "Attempt to update the master file. Failures will save as drafts.", !isPublishedAsIs, () => Context.Status.Show(Editing.Publish(Table, document.Id))),
            new RecordCommand("Save as Draft", isSavedAsIs ? (document.IsDraft ? "Already saved in drafts." : "Nothing to save: it is as published.") : "Save to a draft file. Master will not be updated.", !isSavedAsIs, () => Context.Status.Show(Editing.SaveAsDraft(Table, document.Id))),
            RevertAllCommand(document),
            document.IsDisabled
                ? new RecordCommand("Enable", "Load it again: the game sees it on the next publish; Ctrl+Z disables it again.", true, () => Context.Status.Show(Editing.Enable(Table, document.Id)))
                : new RecordCommand("Disable", disableRefusal ?? "Keep it whole in master but never load it -- asking first what to do about whatever names it.", disableRefusal is null, () => Disable(document)),
            new RecordCommand("Duplicate", "Make a copy under a new Guid.", true, () => Duplicate(document)),
            CompareCommand,
            new RecordCommand("Delete", deleteRefusal is { } refusal ? $"Can't delete: {refusal}" : document.IsInMaster ? "Take it out of master on the next publish; Ctrl+Z brings it back." : "Take the draft away; Ctrl+Z brings it back.", deleteRefusal is null, () => Delete(document)) { Key = Keys.Delete },
        ];
    }

    private RecordCommand CompareCommand => new(CompareLabel, CompareTooltip, true, OpenComparePreviews);

    private const string CompareLabel = "Compare";
    private const string CompareTooltip = "Master, the saved draft and the record as it is now, previewed side by side";
    private const string ComparePreviewsTitle = "Compare Previews";
    private const float CompareColumnGap = 8f;
    private const float CompareMessageHeight = 64f;

    /// <summary>How tall a preview in Compare Previews may grow before it scrolls -- past any item's details, so the window's own scroll is the one used.</summary>
    private const float ComparePreviewMaximumHeight = 4096f;
    private const float CompareWindowScreenMargin = 40f;
    private const float TitleButtonTextPadding = 12f;

    /// <summary>The Compare Previews window while it is open; null otherwise.</summary>
    private Window? _compareWindow;

    /// <summary>Whether Compare Previews is open for the tab -- kept while the tab's windows are rebuilt (a session swapped, the tab reopened), which closes and reopens it.</summary>
    private bool _comparesPreviews;

    /// <summary>Whether the tab is closing its windows, which closes Compare Previews without the player having closed it.</summary>
    private bool _isClosingWindows;

    /// <summary>
    /// Opens Compare Previews over the tab, or leaves it as it is when it is open: a window of its own -- moved, resized
    /// and closed (its X, Escape) like any, never modal -- showing the selected record's preview three times across,
    /// Master, Draft and Unsaved, following the selection and every edit.
    /// </summary>
    private void OpenComparePreviews()
    {
        _comparesPreviews = true;
        if (_compareWindow is not null)
        {
            return;
        }

        var screen = Context.UiEngine.RenderService.ScreenSize;
        var window = Context.UiEngine.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Floating },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = screen - new Vector2(CompareWindowScreenMargin * 2), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = ComparePreviewsTitle, ShowBorder = true, CanUserClose = true, CanUserMove = true, CanUserScrollVertical = true, CanUserScrollHorizontal = true, CanUserFocus = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        window.Closed += _ =>
        {
            Context.Layers.Remove(UiLayer.DynamicHud, window);
            _compareWindow = null;
            _comparesPreviews &= _isClosingWindows;
        };
        window.Initialize();
        Context.Layers.Add(UiLayer.DynamicHud, window);
        _compareWindow = window;
        if (SelectedRecord is { } document)
        {
            ShowComparePreviews(document);
        }

        window.SetRelativePosition((screen - window.CurrentSize) / 2f);
    }

    /// <summary>
    /// Fills Compare Previews with document: its preview as master has it, as the draft holds it and as it is now, left
    /// to right, each column always there and headed with its name. A version that doesn't exist, doesn't decode, or is
    /// the same as one shown to its left says so in its place -- so every preview drawn differs from the others -- and
    /// the draft's and unsaved preview's lines are coloured better or worse against master's, as Item Details colours a
    /// comparison; master, the baseline, is never coloured.
    /// </summary>
    private void ShowComparePreviews(ContentRecordDocument<T> document)
    {
        if (_compareWindow is not { } window)
        {
            return;
        }

        var elementPool = Context.UiEngine.ElementPoolService;
        elementPool.CloseAllChildren(window);
        window.TitleText = $"{ComparePreviewsTitle}: {Table.NameOf(document.Id)}";

        var master = document.MasterLine is { } masterLine ? VersionColumn(MasterBaselineName, masterLine) : (MasterBaselineName, null, "Not in master.");
        var draft = document.DraftJson is not { } draftJson ? ("Draft", null, "No saved draft.")
            : document.MasterLine is { } draftMaster && IsSameRecord(draftMaster, draftJson) ? ("Draft", null, "Same as Master.")
            : VersionColumn("Draft", draftJson);
        var nowTitle = document.DecodeFailure is null ? UnsavedColumnTitle : UnsavedColumnTitle + LastGoodSuffix;
        var now = document.DraftJson is { } nowDraft && IsSameRecord(nowDraft, document.Record.Json) ? (nowTitle, null, "Same as Draft.")
            : document.MasterLine is { } nowMaster && IsSameRecord(nowMaster, document.Record.Json) ? (nowTitle, null, "Same as Master.")
            : (nowTitle, document.LastGood, (string?)null);

        IReadOnlyList<T> againstMaster = master.Definition is { } masterDefinition ? [masterDefinition] : [];
        AddCompareColumn(window, 0, master, []);
        AddCompareColumn(window, 1, draft, againstMaster);
        AddCompareColumn(window, 2, now, againstMaster);
    }

    private const string UnsavedColumnTitle = "Unsaved";

    private bool IsSameRecord(string baselineJson, string currentJson) =>
        ContentRecordDiff.Compare(baselineJson, currentJson, typeof(T), Workspace.Types).Count == 0;

    /// <summary>A version's column: its definition decoded from json, or why it doesn't decode.</summary>
    private (string Title, T? Definition, string? Message) VersionColumn(string name, string json) =>
        Table.Decode(json, out var failure) is { } definition ? (name, definition, null) : (name, null, $"Doesn't decode: {failure}");

    /// <summary>Shows definition in a Compare Previews column, its lines coloured against comparedAgainst (master's version; none for master's own) where the preview can; ShowDefinition by default.</summary>
    protected virtual void ShowComparedDefinition(TPreview preview, T definition, float contentWidth, IReadOnlyList<T> comparedAgainst) =>
        ShowDefinition(preview, definition, contentWidth);

    /// <summary>Column index of Compare Previews: the version's preview, its lines coloured against comparedAgainst (master, for the other two; nothing, for master itself), or the line it shows instead.</summary>
    private void AddCompareColumn(Window window, int index, (string Title, T? Definition, string? Message) column, IReadOnlyList<T> comparedAgainst)
    {
        var elementPool = Context.UiEngine.ElementPoolService;
        var position = new Vector2(index * (PreviewWidth + CompareColumnGap), 0f);
        if (column.Definition is { } definition)
        {
            var preview = elementPool.CreateElement<TPreview>(window, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
                Layout = new ElementLayoutOptions { RelativePosition = position, MaximumSize = new Vector2(PreviewWidth, ComparePreviewMaximumHeight), DisplayMode = ElementDisplayMode.WrapContent },
                Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = column.Title, ShowBorder = true, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
            });
            ShowComparedDefinition(preview, definition, PreviewWidth, comparedAgainst);
            window.AddChild(preview);
            return;
        }

        var size = new Vector2(PreviewWidth, CompareMessageHeight);
        var message = elementPool.CreateElement<TextWindow>(window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = column.Title, ShowBorder = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
            Text = new TextOptions { Text = column.Message ?? string.Empty, TextColor = WindowPalette.DimmedLabelTextColor, Multiline = true },
        });
        window.AddChild(message);
    }

    /// <summary>"Compare" on the preview's title bar, the size of a button among the rows rather than the bar's own: what the record command of that name does.</summary>
    private void AddCompareTitleButton()
    {
        var font = Context.UiEngine.FontService.GetFont(UIEngine.UI.Chrome.FontChrome.DefaultFontSize);
        var button = Context.UiEngine.ElementPoolService.CreateElement<Button>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(font.MeasureString(CompareLabel).X + TitleButtonTextPadding, FieldSectionHeader.ButtonHeight) },
        });
        button.SetText(CompareLabel);
        button.TooltipText = CompareTooltip;
        button.Clicked += _ => OpenComparePreviews();
        PreviewWindow.AddTitleButton(button);
    }

    /// <summary>What the inspector compares document with: master's line, when master holds it.</summary>
    private ContentComparison? MasterComparisonOf(ContentRecordDocument<T> document) =>
        document.DifferencesFromMaster(Workspace.Types) is { } differences ? new ContentComparison(MasterBaselineName, differences) : null;

    private const string MasterBaselineName = "Master";

    /// <summary>Puts every field back as master has it, as one step -- refused for a record master doesn't hold or one already as master has it.</summary>
    private RecordCommand RevertAllCommand(ContentRecordDocument<T> document)
    {
        if (document.MasterLine is not { } masterLine)
        {
            return new RecordCommand("Revert All", $"Not in master: there is nothing to revert this {SingularName} to.", false, static () => { });
        }

        var differenceCount = document.DifferencesFromMaster(Workspace.Types)?.Count ?? 0;
        return differenceCount == 0
            ? new RecordCommand("Revert All", "Nothing to revert: it is as master has it.", false, static () => { })
            : new RecordCommand("Revert All", $"Put all {differenceCount} changed values back as master has them; Ctrl+Z undoes it.", true, () => RevertAllToMaster(document, masterLine));
    }

    private void ReloadMaster(ContentRecordDocument<T> document)
    {
        if (Table.ReloadMaster(document.Id))
        {
            Context.Status.Show($"{Table.NameOf(document.Id)} is compared with master as it is on disk now; Changes shows what publishing would write over.");
        }
    }

    private void RevertAllToMaster(ContentRecordDocument<T> document, string masterLine)
    {
        var name = Table.NameOf(document.Id);
        Table.Apply(document, new ContentInspectorEdit($"Revert {name} to master", record => record.Restore(masterLine), null, ChangesShape: true));
        Context.Status.Show($"Reverted {name} to master: save to keep it.");
    }

    /// <summary>A deleted record's commands, in the places of the others': Publish and Save as Draft write the deletion, Disable is off, Duplicate copies it, and Restore stands where Delete does.</summary>
    private IReadOnlyList<RecordCommand> DeletedCommandsFor(ContentRecordDocument<T> document)
    {
        var where = document.IsInMaster ? "master" : "Drafts";
        var isDeletionUnsaved = Table.IsDeletionUnsaved(document);
        return
        [
            new RecordCommand("Publish", $"Take it out of {where} for good: it can't be restored after.", true, () => Context.Status.Show(Editing.Publish(Table, document.Id))),
            new RecordCommand("Save as Draft", !isDeletionUnsaved ? "The deletion is already saved in drafts." : document.IsInMaster ? "Save the deletion to a draft file. Master keeps it until a publish." : $"Take it out of Drafts for good: it can't be restored after.", isDeletionUnsaved, () => Context.Status.Show(Editing.SaveAsDraft(Table, document.Id))),
            new RecordCommand("Disable", $"A deleted {SingularName} can't be disabled; restore it first.", false, () => { }),
            new RecordCommand("Duplicate", "Make a copy under a new Guid.", true, () => Duplicate(document)),
            new RecordCommand("Restore", "Bring it back as it was; Ctrl+Z deletes it again.", true, () => RestoreDeleted(document)),
        ];
    }

    /// <summary>Disables document at once when nothing names it; otherwise asks, listing what does: Confirm With Replacement (one picked from the published, enabled records), Confirm With Cascade (refused while code names any of it), Cancel.</summary>
    private void Disable(ContentRecordDocument<T> document)
    {
        var plan = Workspace.PlanDisable(Table, document.Id);
        if (plan.Direct.Count == 0)
        {
            Context.Status.Show(Editing.Disable(Table, document.Id, replacement: null));
            return;
        }

        var choices = Table.ReplacementChoices(document.Id);
        Guid? replacement = null;
        ModalDialog? dialog = null;
        var screen = Context.UiEngine.RenderService.ScreenSize;
        var bodySize = new Vector2(DisableDialogWidth, DisableDialogHeight);
        dialog = ModalDialog.Show(Context.UiEngine.ElementPoolService, Context.Layers, new Rectangle(0, 0, (int)screen.X, (int)screen.Y), new ModalDialogRequest($"Disable {Table.NameOf(document.Id)}",
        [
            new DialogButton("Confirm With Replacement", () =>
            {
                if (replacement is { } replacementId)
                {
                    Context.Status.Show(Editing.Disable(Table, document.Id, replacementId));
                }
            }),
            new DialogButton("Confirm With Cascade", () => Context.Status.Show(Editing.DisableWithCascade(Table, document.Id))),
            new DialogButton("Cancel", () => { }),
        ], CancelButtonIndex: 2)
        {
            BodySize = bodySize,
            FillBody = body => FillDisableBody(body, plan, choices, bodySize, chosen =>
            {
                replacement = chosen;
                if (dialog is { } shown)
                {
                    shown.Buttons[0].Enabled = chosen is not null;
                }
            }),
        });
        dialog.Buttons[0].Enabled = false;
        dialog.Buttons[0].TooltipText = $"Pick the {SingularName} that takes its place first.";
        dialog.Buttons[1].Enabled = plan.CascadeRefusal is null;
        dialog.Buttons[1].TooltipText = plan.CascadeRefusal ?? $"Disable it and the {plan.Cascaded.Count} published record(s) that name it, in turn, as one step.";
    }

    /// <summary>What names the record -- and what Cascade would disable, what it can't, and the drafts it leaves -- over the replacement picker.</summary>
    private void FillDisableBody(Window body, ContentDisablePlan plan, IReadOnlyList<ContentReferenceChoice> choices, Vector2 size, Action<Guid?> chosen)
    {
        var elementPool = Context.UiEngine.ElementPoolService;
        var lines = new List<string>();
        AddDependentSection(lines, "Cascade also disables:", plan.Cascaded);
        AddDependentSection(lines, "Can't be disabled here, so Cascade can't run:", plan.NotEditable);
        AddDependentSection(lines, "Drafts, left as they are:", plan.Drafts);

        var description = new FieldEditorDescription("Replacement", FieldValueKind.SearchableChoice) { Choices = [.. choices.Select(choice => choice.Label)], IsSet = false };
        var rowHeight = FieldRow.HeightOf(description);
        var listSize = new Vector2(size.X, size.Y - rowHeight - 6f);
        var list = elementPool.CreateElement<TextWindow>(body, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = listSize, MaximumSize = listSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false, CanUserScrollVertical = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
            Text = new TextOptions { Text = string.Join("\n", lines), TextColor = WindowPalette.TitleTextColor, Multiline = true },
        });
        body.AddChild(list);

        var rowSize = new Vector2(size.X, rowHeight);
        var row = elementPool.CreateElement<FieldRow>(body, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, size.Y - rowHeight), Size = rowSize, MinimumSize = rowSize, MaximumSize = rowSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        row.Configure(description, FieldValue.OfChoice(-1));
        row.Committed += value => chosen(value.ChoiceIndex >= 0 && value.ChoiceIndex < choices.Count ? choices[value.ChoiceIndex].Id : null);
        body.AddChild(row);
    }

    /// <summary>heading, then dependents grouped by kind and how they name the record -- "Blueprint (builds with it, in code)" -- each by name alone under its group; nothing for none.</summary>
    private static void AddDependentSection(List<string> lines, string heading, IReadOnlyList<ContentDependent> dependents)
    {
        if (dependents.Count == 0)
        {
            return;
        }

        if (lines.Count > 0)
        {
            lines.Add(string.Empty);
        }

        lines.Add(heading);
        foreach (var group in dependents.GroupBy(dependent => (dependent.Kind, dependent.Relationship)))
        {
            lines.Add($"  {group.Key.Kind} ({group.Key.Relationship})");
            lines.AddRange(group.Select(dependent => $"      {dependent.Name}").Distinct());
        }
    }

    private void Duplicate(ContentRecordDocument<T> document)
    {
        if (JsonNode.Parse(document.Record.Json) is not JsonObject copy)
        {
            return;
        }

        var original = Table.NameOf(document.Id);
        var name = UnusedName(original + CopySuffix);
        copy[ContentTable.IdKey] = ContentGuid.Format(Guid.NewGuid());
        copy[NameKey] = name;
        copy.Remove(ContentTable.DisabledKey);
        copy.Remove(ContentTable.ReplacementKey);
        if (Table.CreateNew(copy.ToJsonString(), $"Duplicate {original}") is not { } duplicate)
        {
            Context.Status.Show($"Can't duplicate {original}: it doesn't decode.");
            return;
        }

        RefreshRows();
        TrySelectRecord(duplicate.Id);
        Context.Status.Show($"Made {name}.");
    }

    private void Delete(ContentRecordDocument<T> document) =>
        Context.Status.Show(Editing.Delete(Table, document.Id));

    private void RestoreDeleted(ContentRecordDocument<T> document)
    {
        var name = Table.NameOf(document.Id);
        if (!Table.RestoreDeleted(document, $"Restore {name}"))
        {
            return;
        }

        RefreshRows();
        TrySelectRecord(document.Id);
        Context.Status.Show($"Restored {name}.");
    }

    protected override void OnOpened(WorkshopTabContext context)
    {
        Editing.AttachFieldText(_inspector);
        AddCompareTitleButton();
        if (_comparesPreviews)
        {
            OpenComparePreviews();
        }
    }

    protected override void OnClosing()
    {
        _isClosingWindows = true;
        _compareWindow?.Close();
        _isClosingWindows = false;
        Editing.DetachFieldText(_inspector);
        _inspector.RememberScroll();
        _inspectorHost = null;
        _isRefreshRequested = false;
    }

    protected override void OnUpdate(GameTime gameTime)
    {
        _inspector.Update();
        if (_isRefreshRequested)
        {
            _isRefreshRequested = false;
            RefreshRows();
            RefreshCommands();
        }
    }

    protected override void ShowInInspector(Window inspector, ContentRecordDocument<T> document)
    {
        if (!ReferenceEquals(_inspectorHost, inspector))
        {
            _inspectorHost = inspector;
            _inspector.Attach(inspector);
        }

        Context.ShowChanges(ChangesOf(document));
        ShowComparePreviews(document);
        inspector.TitleText = document.ReplacedInGameBy is { } mod
            ? $"Inspector: {Table.NameOf(document.Id)} (replaced in the game by {mod})"
            : $"Inspector: {Table.NameOf(document.Id)}";
        ShowRecordProblems(RecordProblemsText(document));
        if (document.ModName is { } modName)
        {
            _inspector.Clear($"Supplied by the mod {modName}: shown here, edited in the mod.");
            return;
        }

        if (Table.IsDeleted(document.Id))
        {
            _inspector.Clear(document.IsInMaster
                ? $"Deleted: publishing takes it out of master for good. Restore it to edit it."
                : $"Deleted: saving takes it out of Drafts for good. Restore it to edit it.");
            return;
        }

        if (ReferenceEquals(_inspector.Record, document.Record))
        {
            _inspector.Refresh(document.FieldProblems);
            return;
        }

        _inspector.Show(document.Record, document.FieldProblems);
    }

    /// <summary>"Changes only": the inspector shows only the fields that differ from master -- a view of the inspector, kept while the tab is open, never a record command.</summary>
    protected override IReadOnlyList<RecordViewOption> ViewOptions =>
    [
        new RecordViewOption("Changes only", "Show only the fields that differ from master", () => _inspector.ShowsChangedOnly, isOn => _inspector.ShowsChangedOnly = isOn),
    ];

    /// <summary>
    /// What Changes lists for document: each field that differs from its master line, master's value and the value now,
    /// read as the inspector reads them; a click goes to the field, Revert does what the field's own revert button does.
    /// A record with nothing to compare says why.
    /// </summary>
    private WorkshopChanges ChangesOf(ContentRecordDocument<T> document)
    {
        if (document.ModName is { } modName)
        {
            return new WorkshopChanges(MasterBaselineName, [], $"Supplied by the mod {modName}: nothing here to compare with master.");
        }

        if (Table.IsDeleted(document.Id))
        {
            return new WorkshopChanges(MasterBaselineName, [], document.IsInMaster ? "Deleted: publishing takes master's line away." : "Deleted: it was never in master.");
        }

        if (document.DifferencesFromMaster(Workspace.Types) is not { } differences)
        {
            return new WorkshopChanges(MasterBaselineName, [], $"New: master doesn't have this {SingularName}, so there is nothing to compare it with.");
        }

        if (differences.Count == 0)
        {
            return new WorkshopChanges(MasterBaselineName, [], "As published: nothing differs from master.");
        }

        var types = Workspace.Types;
        var masterRoot = JsonNode.Parse(document.MasterLine!);
        var changes = new List<WorkshopFieldChange>(differences.Count);
        foreach (var difference in differences)
        {
            var currentField = difference.CurrentPlace is { } place ? ContentSchemas.FieldAt(types, typeof(T), document.Record.Root, place) : null;
            var baselineField = difference.BaselinePath is { } baselinePath ? ContentSchemas.FieldAt(types, typeof(T), masterRoot, baselinePath) : null;
            changes.Add(new WorkshopFieldChange(
                difference.DisplayPath,
                difference.Kind.ToString(),
                ContentDifferenceText.Baseline(difference, baselineField ?? currentField, _inspector.RecordName),
                ContentDifferenceText.Current(difference, currentField ?? baselineField, _inspector.RecordName))
            {
                TabTitle = Table.TabTitle,
                RecordId = document.Id,
                FieldPath = (difference.CurrentPlace ?? difference.RemovedFrom)?.Text,
                Revert = () => _inspector.RequestRevert(difference),
            });
        }

        return new WorkshopChanges(MasterBaselineName, changes, string.Empty);
    }

    /// <summary>Why the record doesn't decode -- when no field's error already says why (a required value not yet chosen) --, what its checks refuse, and why the last save kept it in Drafts: what no single field can show.</summary>
    private string? RecordProblemsText(ContentRecordDocument<T> document)
    {
        var lines = new List<string>();
        if (document.DecodeFailure is { } failure && !document.ExplainsDecodeFailure)
        {
            lines.Add($"Doesn't decode, so {PreviewTitle} shows it as it last did: {failure}");
        }

        lines.AddRange(document.CheckProblems);
        lines.AddRange(document.SaveProblems.Select(reason => $"Kept in Drafts: {reason}"));
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private void OnEditRequested(ContentInspectorEdit edit)
    {
        if (SelectedRecord is not { IsReadOnly: false } document || Table.IsDeleted(document.Id))
        {
            return;
        }

        if (edit.Confirmation is { } question)
        {
            Confirm(edit.Description, question, "Change", () => Table.Apply(document, edit), document);
            return;
        }

        Table.Apply(document, edit);
    }

    /// <summary>Asks question in a dialog: confirm runs apply, Cancel (or closing it) shows document's rows again as they were.</summary>
    private void Confirm(string title, string question, string confirmLabel, Action apply, ContentRecordDocument<T> document)
    {
        var screen = Context.UiEngine.RenderService.ScreenSize;
        ModalDialog.Show(Context.UiEngine.ElementPoolService, Context.Layers, new Rectangle(0, 0, (int)screen.X, (int)screen.Y), new ModalDialogRequest(title,
        [
            new DialogButton(confirmLabel, apply),
            new DialogButton("Cancel", () => _inspector.RequestRebuild(document.FieldProblems)),
        ], CancelButtonIndex: 1)
        {
            Message = question,
            BodySize = new Vector2(420, 90),
        });
    }

    /// <summary>The selected record shown again at once; any other's row, and the buttons, on the next Update -- once, however many records a save or an undo changed.</summary>
    private void OnDocumentChanged(ContentRecordDocument<T> document, bool changesShape)
    {
        if (!IsOpen)
        {
            return;
        }

        if (!ReferenceEquals(document, SelectedRecord))
        {
            _isRefreshRequested = true;
            return;
        }

        if (changesShape)
        {
            _inspector.RequestRebuild(document.FieldProblems);
        }

        RefreshRows();
        RefreshSelected();
    }

    /// <summary>A record taken away (a New undone, a Delete) or its deletion saved: its row reads Deleted, and stays selected while it is listed; once it is gone (a New undone, a deletion made final), the first record is selected instead.</summary>
    private void OnDocumentRemoved(ContentRecordDocument<T> document)
    {
        if (!IsOpen)
        {
            return;
        }

        RefreshRows();
        if (!ReferenceEquals(document, SelectedRecord))
        {
            return;
        }

        if (Table.IsDeleted(document.Id))
        {
            RefreshSelected();
            return;
        }

        if (Table.Documents.Count > 0)
        {
            TrySelectRecord(Table.Documents[0].Id);
        }
    }

    public void ShowRecord(Guid id, bool rebuildInspector)
    {
        if (!TrySelectRecord(id) || !rebuildInspector || Table.Find(id) is not { } document)
        {
            return;
        }

        _inspector.RequestRebuild(document.FieldProblems);
    }

    public void FocusField(string fieldPath)
    {
        if (ContentPath.TryParse(fieldPath, out var path))
        {
            _inspector.FocusField(path);
        }
    }
}
