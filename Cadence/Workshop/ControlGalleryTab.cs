using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Fields;
using UIEngine.UI.Filtering;

namespace Workshop;

/// <summary>A scratch panel holding one of each UIEngine control in headed groups, each wired to the status line -- for trying the controls out before an editor uses them.</summary>
/// <remarks>
/// The panel is the framework's dark panel, as an editor's inspector is: captions read in WindowPalette.ControlLabelTextColor, and an
/// editable control is light (WindowPalette.EditableControlBackground) under WindowPalette.BodyTextColor. Every caption is one width
/// and every one-line control one row tall; a taller control is a whole number of rows.
/// </remarks>
public sealed class ControlGalleryTab : IIdeTab
{
    public const string TabTitle = "Controls";

    private const float Margin = 12f;
    private const float RowHeight = FieldRow.RowHeight;
    private const float CaptionWidth = FieldRow.LabelColumnWidth;
    private const float CaptionInset = FieldSectionHeader.TextInset;
    private const float FieldWidth = 220f;
    private const float WideControlWidth = FieldWidth * 2;
    private const float SmallButtonWidth = RowHeight;
    private const float BesideGap = 6f;
    private const float RowGap = 8f;
    private const float GroupHeadingGap = 14f;
    private const int FractionBarMaximum = 20;
    private const char PasswordMaskCharacter = '*';

    private static readonly string[] Shapes = ["Single", "Line", "Cone", "Circle", "Square"];
    private static readonly string[] ManyOptions = [.. Enumerable.Range(1, 40).Select(index => $"Option {index}")];
    private static readonly string[] SortOrders = ["Name", "Newest", "Value"];
    private static readonly ToggleListItem[] ToggleListElements = [new("Fire", true), new("Frost", false), new("Poison", true)];

    private static readonly GalleryControlLook EditableLook = new(WindowPalette.EditableControlBackground, WindowPalette.BodyTextColor, ShowsBorder: true);
    private static readonly GalleryControlLook FramedLook = new(ContentColor: null, TextColor: null, ShowsBorder: true);
    private static readonly GalleryControlLook BareLook = new(Color.Transparent, TextColor: null, ShowsBorder: false);

    private WorkshopTabContext _context = null!;
    private Window _window = null!;
    private Rectangle _tabArea;
    private readonly List<string> _listEditorEntries = ["Burning", "Poison", "Holy Ground"];
    private int _fractionBarValue = 14;
    private bool _contextMenuWrapsLines = true;

    public string Title => TabTitle;

    public bool ShowsRelatedRecords => false;

    public void Open(WorkshopTabContext context, Rectangle area)
    {
        _context = context;
        _tabArea = area;
        var elementPool = context.UiEngine.ElementPoolService;
        _window = elementPool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(area.X + Margin, area.Y + Margin), Size = WindowSize(area), MaximumSize = new Vector2(4000, 4000), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Controls", ShowBorder = true, CanUserScrollVertical = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelBackgroundColor },
        });
        _window.Initialize();
        context.Layers.Add(UiLayer.Base, _window);

        var y = 0f;
        AddTextEntryGroup(ref y);
        AddChoiceGroup(ref y);
        AddListsGroup(ref y);
        AddLayoutGroup(ref y);
        AddFeedbackGroup(ref y);
        AddFilteringGroup(ref y);
    }

    private void AddTextEntryGroup(ref float y)
    {
        AddGroupHeading(ref y, "Text entry");

        var textBox = Add<TextBox>(ref y, "Text box", new Vector2(FieldWidth, RowHeight), EditableLook, box =>
        {
            box.GhostText = "Type, then Enter";
            box.GhostTextColor = WindowPalette.GhostTextOnLightColor;
        });
        textBox.TextChanged += text => _context.Status.Show($"Text box: {text}");
        textBox.TextSubmitted += text => _context.Status.Show($"Text box submitted: {text}");

        var multilineTextBox = Add<TextBox>(ref y, "Multiline text box", new Vector2(FieldWidth, RowHeight * 3), EditableLook, box => box.SetText("Line one\nLine two"), multiline: true);
        multilineTextBox.TextChanged += text => _context.Status.Show($"Multiline text box: {text.Length} characters, {text.Count(character => character == '\n') + 1} lines");

        Add<TextBox>(ref y, "Read-only text box", new Vector2(FieldWidth, RowHeight), EditableLook, box =>
        {
            box.SetText("Select and copy me");
            box.IsReadOnly = true;
        });

        var limitedTextBox = Add<TextBox>(ref y, "Limited to 12", new Vector2(FieldWidth, RowHeight), EditableLook, box => box.MaximumLength = 12);
        limitedTextBox.TextChanged += text => _context.Status.Show($"Limited text box: {text.Length} of 12");

        var passwordTextBox = Add<TextBox>(ref y, "Password", new Vector2(FieldWidth, RowHeight), EditableLook, box => box.MaskCharacter = PasswordMaskCharacter);
        passwordTextBox.TextSubmitted += text => _context.Status.Show($"Password submitted: {text.Length} characters");

        var wholeNumber = Add<NumericField>(ref y, "Whole number 0-20", new Vector2(FieldWidth, RowHeight), EditableLook, field => field.Configure(5, 0, 20));
        wholeNumber.ValueChanged += value => _context.Status.Show($"Whole number: {value}");

        var decimalNumber = Add<NumericField>(ref y, "Decimal -1-1", new Vector2(FieldWidth, RowHeight), EditableLook, field => field.Configure(0.25, -1, 1, step: 0.05, decimalPlaces: 2));
        decimalNumber.ValueChanged += value => _context.Status.Show($"Decimal: {value}");

        var spinnerNumber = Add<NumericField>(ref y, "With spinner 0-100", new Vector2(FieldWidth, RowHeight), EditableLook, field => field.Configure(50, 0, 100, step: 5, showsSpinnerButtons: true));
        spinnerNumber.ValueChanged += value => _context.Status.Show($"Spinner number: {value}");

        var colour = Add<ColorField>(ref y, "Colour", new Vector2(FieldWidth, RowHeight), EditableLook, field => field.Configure(Color.Orange));
        colour.ValueChanged += value => _context.Status.Show($"Colour: {ColorField.Format(value)}");

        var freeText = Add<SearchableChoiceField>(ref y, "Searchable, free text", new Vector2(FieldWidth, RowHeight), EditableLook, field =>
        {
            field.Configure(Shapes, -1, allowsFreeText: true, "Hexagon");
            field.GhostTextColor = WindowPalette.GhostTextOnLightColor;
        });
        freeText.Chosen += (index, text) => _context.Status.Show($"Searchable, free text: {text} ({index})");
    }

    private void AddChoiceGroup(ref float y)
    {
        AddGroupHeading(ref y, "Choice");

        var button = Add<Button>(ref y, "Button", new Vector2(FieldWidth / 2, RowHeight), EditableLook, control =>
        {
            control.SetText("Press me");
            control.TooltipText = "Reach this button with Tab or Shift+Tab and this tooltip shows after the hover delay.";
        });
        button.Clicked += _ => _context.Status.Show("Button: pressed");

        var iconButton = Add<Button>(ref y, "Icon button", new Vector2(SmallButtonWidth, RowHeight), EditableLook, control => control.SetIcon(null, "+"));
        iconButton.Clicked += _ => _context.Status.Show("Icon button: pressed");

        Add<Button>(ref y, "Disabled button", new Vector2(FieldWidth / 2, RowHeight), EditableLook, control =>
        {
            control.SetText("Can't press");
            control.Enabled = false;
        });

        var shape = Add<DropdownField>(ref y, "Dropdown", new Vector2(FieldWidth, RowHeight), EditableLook);
        shape.SetOptions(Shapes, 0);
        shape.SelectionChanged += index => _context.Status.Show($"Dropdown: {Shapes[index]}");

        var longDropdown = Add<DropdownField>(ref y, "Long dropdown", new Vector2(FieldWidth, RowHeight), EditableLook);
        longDropdown.Placeholder = "Choose one";
        longDropdown.SetOptions(ManyOptions, -1);
        longDropdown.SelectionChanged += index => _context.Status.Show($"Long dropdown: {ManyOptions[index]}");

        var searchable = Add<SearchableChoiceField>(ref y, "Searchable choice", new Vector2(FieldWidth, RowHeight), EditableLook, field => field.Configure(ManyOptions, 2, allowsFreeText: false));
        searchable.Chosen += (index, text) => _context.Status.Show($"Searchable choice: {text} ({index})");

        var multiChoice = Add<MultiChoiceField>(ref y, "Multi-choice", new Vector2(FieldWidth, RowHeight), EditableLook, field => field.Configure(Shapes, [1, 3]));
        multiChoice.ChoicesChanged += indices => _context.Status.Show($"Multi-choice: {string.Join(", ", indices.Select(index => Shapes[index]))}");

        var labelFont = _context.UiEngine.FontService.GetFont(FontChrome.DefaultFontSize);
        Add<Toggle>(ref y, "Toggle", new Vector2(FieldWidth, RowHeight), BareLook, toggle =>
        {
            toggle.Configure(true, FieldRow.ToggleOnColor, WindowPalette.EditableControlBackground, isOn => _context.Status.Show($"Toggle: {(isOn ? "on" : "off")}"));
            toggle.SetLabel("Show hidden records", labelFont, WindowPalette.ControlLabelTextColor);
        });

        Add<Toggle>(ref y, "Flat toggle", new Vector2(FieldWidth, RowHeight), BareLook, toggle =>
        {
            toggle.Configure(false, FieldRow.ToggleOnColor, WindowPalette.EditableControlBackground, isOn => _context.Status.Show($"Flat toggle: {(isOn ? "on" : "off")}"));
            toggle.CheckboxStyle = ToggleCheckboxStyle.Flat;
            toggle.SetLabel("A menu's checkbox row", labelFont, WindowPalette.ControlLabelTextColor);
        });

        Add<Toggle>(ref y, "Disabled toggle", new Vector2(FieldWidth, RowHeight), BareLook, toggle =>
        {
            toggle.Configure(true, FieldRow.ToggleOnColor, WindowPalette.EditableControlBackground, _ => { });
            toggle.SetLabel("Can't change", labelFont, WindowPalette.ControlLabelTextColor);
            toggle.Enabled = false;
        });

        var toggleListHeight = ToggleList.HeightFor(ToggleListElements.Length, hasAll: true, RowHeight);
        var toggleList = Add<ToggleList>(ref y, "Toggle list", new Vector2(FieldWidth, toggleListHeight), BareLook, list => list.Configure(ToggleListElements, "All", WindowPalette.ControlLabelTextColor, FieldRow.ToggleOnColor, WindowPalette.EditableControlBackground, RowHeight));
        toggleList.Changed += () => _context.Status.Show($"Toggle list: {DescribeTickedItems(toggleList)}");
    }

    private void AddListsGroup(ref float y)
    {
        AddGroupHeading(ref y, "Lists");

        var listView = Add<ListView>(ref y, "List view", new Vector2(WideControlWidth, RowHeight * 6), FramedLook);
        listView.SetColumns([new ListViewColumn("Name", 160), new ListViewColumn("Count", 80), new ListViewColumn("Note", 100, Sortable: false)], showIconColumn: true);
        listView.SetRows([.. Enumerable.Range(1, 30).Select(index => new ListViewRow([$"Row {index}", (index * 7 % 31).ToString(), index % 3 == 0 ? "third" : string.Empty], index % 5 == 0 ? "!" : null, Color.DarkGoldenrod))]);
        listView.SelectionChanged += row => _context.Status.Show($"List view: selected {row?.Cells[0] ?? "nothing"}");
        listView.RowActivated += row => _context.Status.Show($"List view: activated {row.Cells[0]}");

        var listEditor = Add<ListEditor>(ref y, "List editor", new Vector2(FieldWidth + 80, RowHeight * 6), FramedLook, editor => editor.Configure(rowHeight: RowHeight));
        listEditor.AddRequested += () =>
        {
            _listEditorEntries.Add($"Entry {_listEditorEntries.Count + 1}");
            FillListEditor(listEditor);
            _context.Status.Show($"List editor: added, {_listEditorEntries.Count} entries");
        };
        listEditor.RemoveRequested += index =>
        {
            var removed = _listEditorEntries[index];
            _listEditorEntries.RemoveAt(index);
            FillListEditor(listEditor);
            _context.Status.Show($"List editor: removed {removed}");
        };
        listEditor.MoveRequested += (from, to) =>
        {
            (_listEditorEntries[from], _listEditorEntries[to]) = (_listEditorEntries[to], _listEditorEntries[from]);
            FillListEditor(listEditor);
            _context.Status.Show($"List editor: moved {_listEditorEntries[to]} from {from + 1} to {to + 1}");
        };
        FillListEditor(listEditor);
    }

    private void AddLayoutGroup(ref float y)
    {
        AddGroupHeading(ref y, "Layout");

        var splitPane = Add<SplitPane>(ref y, "Split pane", new Vector2(WideControlWidth, RowHeight * 4), FramedLook, pane => pane.Configure(SplitOrientation.SideBySide, SplitSizedPane.First, 160));
        AddPaneLabel(splitPane.FirstPane, "Left pane");
        AddPaneLabel(splitPane.SecondPane, "Right pane");
        splitPane.SplitChanged += size => _context.Status.Show($"Split pane: {size:N0}");

        AddTiledRow(ref y);

        var divider = Add<TextDivider>(ref y, "Text divider", new Vector2(WideControlWidth, RowHeight), BareLook);
        divider.ConfigureAsLine(WindowPalette.ScrollbarThumbHoverColor, widthFraction: 1f);

        AddFolder(ref y);

        var sectionHeader = Add<FieldSectionHeader>(ref y, "Section header", new Vector2(FieldWidth + 80, RowHeight), FramedLook, header => header.Configure("Effects (2)", depth: 1, isCollapsible: true, isExpanded: true, ["Add", "^", "v", "X"], [true, false, true, true]));
        sectionHeader.ExpandToggled += isExpanded => _context.Status.Show($"Section header: {(isExpanded ? "open" : "closed")}");
        sectionHeader.ButtonClicked += index => _context.Status.Show($"Section header: button {index}");

        AddFieldRow(ref y, new FieldEditorDescription("Field row: text", FieldValueKind.Text), FieldValue.OfText("Wand of Fireball"));
        AddFieldRow(ref y, new FieldEditorDescription("Field row: unset", FieldValueKind.Integer) { Depth = 1, Minimum = 0, IsSet = false }, FieldValue.OfNumber(0));
        AddFieldRow(ref y, new FieldEditorDescription("Field row: yes or no", FieldValueKind.Boolean) { Depth = 1 }, FieldValue.OfBoolean(true));
        var problemRow = AddFieldRow(ref y, new FieldEditorDescription("Field row: problem", FieldValueKind.Choice) { Depth = 2, Choices = Shapes }, FieldValue.OfChoice(0));
        problemRow.SetProblem("A problem with this value", isWarning: false);
    }

    /// <summary>A row tiled left to right with a gap between its children, centred across it, the middle child taking whatever width the others leave.</summary>
    private void AddTiledRow(ref float y)
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        var rowTop = y;
        AddCaption(rowTop, "Tiled row");
        var tiledRow = elementPool.CreateElement<Window>(_window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal, ChildGap = BesideGap, ChildCrossAlignment = ChildCrossAlignment.Center },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(CaptionWidth, rowTop), Size = new Vector2(WideControlWidth, RowHeight), MaximumSize = new Vector2(WideControlWidth, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.ControlBackground },
        });
        tiledRow.ContentPadding = new Vector2(2, 0);
        _window.AddChild(tiledRow);

        AddTiledRowButton(tiledRow, "Start", fillsRemaining: false);
        AddTiledRowButton(tiledRow, "Fills remaining", fillsRemaining: true);
        AddTiledRowButton(tiledRow, "End", fillsRemaining: false);
        y += RowHeight + RowGap;
    }

    private void AddTiledRowButton(Window tiledRow, string text, bool fillsRemaining)
    {
        var size = new Vector2(64, RowHeight - 6);
        var button = _context.UiEngine.ElementPoolService.CreateElement<Button>(tiledRow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = size, DisplayMode = ElementDisplayMode.Fixed, FillsRemaining = fillsRemaining },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.EditableControlBackground },
            Text = new TextOptions { TextColor = WindowPalette.BodyTextColor },
        });
        button.SetText(text);
        button.Clicked += _ => _context.Status.Show($"Tiled row: {text}, {button.CurrentSize.X:N0} wide");
        tiledRow.AddChild(button);
    }

    /// <summary>A folder holding three entries: its icon is one row, and the rows under it are kept free for what it shows when opened.</summary>
    private void AddFolder(ref float y)
    {
        const int FolderEntryCount = 3;
        var elementPool = _context.UiEngine.ElementPoolService;
        var rowTop = y;
        AddCaption(rowTop, "Folder");
        var openedHeight = RowHeight * (FolderEntryCount + 1);
        var folder = elementPool.CreateElement<Folder>(_window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(CaptionWidth, rowTop), MaximumSize = new Vector2(FieldWidth, openedHeight + RowHeight), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.EditableControlBackground },
            Folder = new FolderOptions { FallbackGlyph = "★", IconSize = new Vector2(RowHeight, RowHeight), BackgroundColor = WindowPalette.ControlBackground },
        });
        folder.Initialize();
        _window.AddChild(folder);

        for (var index = 1; index <= FolderEntryCount; index++)
        {
            var entry = elementPool.CreateElement<Label>(folder, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(FieldWidth / 2, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
                Text = new TextOptions { Text = $"Notification {index}", TextColor = WindowPalette.BodyTextColor },
            });
            folder.AddChild(entry);
        }

        folder.DisplayModeChanged += element => _context.Status.Show($"Folder: {(element.DisplayMode == ElementDisplayMode.Minimized ? "closed" : "open")}");
        y += openedHeight + RowHeight + RowGap;
    }

    private void AddFeedbackGroup(ref float y)
    {
        AddGroupHeading(ref y, "Feedback");

        var tooltipButton = Add<Button>(ref y, "Tooltip on hover", new Vector2(FieldWidth / 2, RowHeight), EditableLook, control =>
        {
            control.SetText("Hover me");
            control.TooltipText = "A tooltip: shown once the pointer rests on the button, gone when it leaves.";
        });
        tooltipButton.Clicked += _ => _context.Status.Show("Tooltip on hover: the tooltip shows while the pointer rests here");

        var contextMenuTarget = Add<Button>(ref y, "Context menu", new Vector2(FieldWidth / 2, RowHeight), EditableLook, control => control.SetText("Right-click me"));
        contextMenuTarget.Clicked += _ => _context.Status.Show("Context menu: right-click to open it");
        contextMenuTarget.OnRightClicked = position => _context.ContextMenuController.Open(new Vector2(position.X, position.Y), ContextMenuOptions());

        AddFractionBar(ref y);

        var dialogButton = Add<Button>(ref y, "Modal dialog", new Vector2(FieldWidth / 2, RowHeight), EditableLook, control => control.SetText("Open dialog"));
        dialogButton.Clicked += _ => OpenDialog();
    }

    private IReadOnlyList<DropdownOption> ContextMenuOptions() =>
    [
        DropdownOption.Header("Edit"),
        new DropdownOption("Cut", "Ctrl+X", Enabled: true, () => _context.Status.Show("Context menu: Cut")),
        new DropdownOption("Copy", "Ctrl+C", Enabled: true, () => _context.Status.Show("Context menu: Copy")),
        new DropdownOption("Paste", "Ctrl+V", Enabled: false, () => { }) { TooltipText = "Nothing to paste" },
        DropdownOption.Header("View"),
        new DropdownOption("Wrap lines", null, Enabled: true, () =>
        {
            _contextMenuWrapsLines = !_contextMenuWrapsLines;
            _context.Status.Show($"Context menu: Wrap lines {(_contextMenuWrapsLines ? "on" : "off")}");
        }) { IsChecked = _contextMenuWrapsLines },
        DropdownOption.Opening("Insert",
        [
            new DropdownOption("Row above", null, Enabled: true, () => _context.Status.Show("Context menu: Insert > Row above")),
            new DropdownOption("Row below", null, Enabled: true, () => _context.Status.Show("Context menu: Insert > Row below")),
        ]),
    ];

    /// <summary>A bar filled to a value out of FractionBarMaximum, with a button either side of it to lower and raise the value.</summary>
    private void AddFractionBar(ref float y)
    {
        var rowTop = y;
        var fractionBar = Add<FractionBarElement>(ref y, "Fraction bar", new Vector2(FieldWidth, RowHeight), BareLook);
        ShowFractionBarValue(fractionBar);

        var lowerButton = CreateControl<Button>(new Vector2(CaptionWidth + FieldWidth + BesideGap, rowTop), new Vector2(SmallButtonWidth, RowHeight), EditableLook);
        lowerButton.SetText("-");
        lowerButton.Clicked += _ => ChangeFractionBarValue(fractionBar, -1);

        var raiseButton = CreateControl<Button>(new Vector2(CaptionWidth + FieldWidth + BesideGap * 2 + SmallButtonWidth, rowTop), new Vector2(SmallButtonWidth, RowHeight), EditableLook);
        raiseButton.SetText("+");
        raiseButton.Clicked += _ => ChangeFractionBarValue(fractionBar, 1);
    }

    private void ChangeFractionBarValue(FractionBarElement fractionBar, int change)
    {
        _fractionBarValue = System.Math.Clamp(_fractionBarValue + change, 0, FractionBarMaximum);
        ShowFractionBarValue(fractionBar);
        _context.Status.Show($"Fraction bar: {_fractionBarValue} / {FractionBarMaximum}");
    }

    private void ShowFractionBarValue(FractionBarElement fractionBar) =>
        fractionBar.Configure((float)_fractionBarValue / FractionBarMaximum, hasResource: true, WindowPalette.BorderColor, static fraction => Color.Lerp(Color.IndianRed, Color.SeaGreen, fraction), (_fractionBarValue, FractionBarMaximum));

    private void AddFilteringGroup(ref float y)
    {
        AddGroupHeading(ref y, "Filtering");
        AddListFilter(ref y);
        AddGridControl(ref y);
    }

    /// <summary>Room under the filter button for its chips, wrapped onto three lines at most.</summary>
    private const float ChipsSpace = ListFilterChips.ChipHeight * 3 + ListFilterChips.ChipGap * 2;

    /// <summary>A filter button over a plain list of fruit, the way a game's inventory would host one: the matching fruit listed beside it and the checked choices as chips under it.</summary>
    private void AddListFilter(ref float y)
    {
        var filter = new ListFilter<GalleryFruit>(
        [
            new ListFilterGroup<GalleryFruit>("show", "Show only", ListFilterGroupMatch.AllOf,
            [
                new ListFilterChoice<GalleryFruit>("show:ripe", "Ripe", static fruit => fruit.IsRipe),
                new ListFilterChoice<GalleryFruit>("show:bruised", "Bruised", static fruit => fruit.IsBruised),
            ]),
            new ListFilterGroup<GalleryFruit>("kind", "Kind: any of", ListFilterGroupMatch.AnyOf,
            [
                new ListFilterChoice<GalleryFruit>("kind:apple", "Apple", static fruit => fruit.Kind == "Apple"),
                new ListFilterChoice<GalleryFruit>("kind:pear", "Pear", static fruit => fruit.Kind == "Pear"),
                new ListFilterChoice<GalleryFruit>("kind:plum", "Plum", static fruit => fruit.Kind == "Plum"),
            ]),
        ]);

        var rowTop = y;
        const float FilterButtonWidth = 90f;
        Add<ListFilterButton>(ref y, "List filter", new Vector2(FilterButtonWidth, RowHeight), EditableLook, control => control.Configure(new ListFilterMenuSource<GalleryFruit>(filter, () => GalleryBasket)));
        var listedFruit = CreateLabel(_window, new Vector2(CaptionWidth + FilterButtonWidth + BesideGap, rowTop), new Vector2(WideControlWidth - FilterButtonWidth - BesideGap, RowHeight), ListedFruit(filter), WindowPalette.ControlLabelTextColor);
        listedFruit.ShowsFullTextWhenCut = true;

        AddCaption(y, "List filter chips");
        var chips = _context.UiEngine.ElementPoolService.CreateElement<ListFilterChips>(_window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(CaptionWidth, y), Size = new Vector2(FieldWidth, 0), MaximumSize = new Vector2(FieldWidth, ChipsSpace), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        chips.ContentPadding = Vector2.Zero;
        chips.Configure(new ListFilterMenuSource<GalleryFruit>(filter, () => GalleryBasket), FieldWidth);
        _window.AddChild(chips);
        y += System.Math.Max(ChipsSpace, RowHeight) + RowGap;

        filter.Selection.Changed += selection =>
        {
            listedFruit.SetText(ListedFruit(filter));
            _context.Status.Show($"List filter: {string.Join(", ", selection.CheckedIds())}");
        };
    }

    /// <summary>A grid's toolbar: the matching fruit's count, a sort button that cycles, two toggles and a search box.</summary>
    private void AddGridControl(ref float y)
    {
        var gridControl = Add<GridControl>(ref y, "Grid control", new Vector2(WideControlWidth, RowHeight), FramedLook, control => control.Configure(
            SortOrders,
            [
                ("Ripe", false, isOn => _context.Status.Show($"Grid control: Ripe {(isOn ? "on" : "off")}")),
                ("Bruised", true, isOn => _context.Status.Show($"Grid control: Bruised {(isOn ? "on" : "off")}")),
            ],
            "Search"));
        gridControl.SetItemCount(GalleryBasket.Length);
        gridControl.SortOptionCycled += index => _context.Status.Show($"Grid control: sorted by {SortOrders[index]}");
        gridControl.SearchFilterChanged += text =>
        {
            var matchingCount = GalleryBasket.Count(fruit => fruit.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
            gridControl.SetItemCount(matchingCount);
            _context.Status.Show($"Grid control: \"{text}\" matches {matchingCount}");
        };
    }

    private static string ListedFruit(ListFilter<GalleryFruit> filter) =>
        $"{filter.CountMatching(GalleryBasket)} of {GalleryBasket.Length}: {string.Join(", ", GalleryBasket.Where(filter.Matches).Select(fruit => fruit.Name))}";

    private readonly record struct GalleryFruit(string Name, string Kind, bool IsRipe, bool IsBruised);

    private static readonly GalleryFruit[] GalleryBasket =
    [
        new("Red apple", "Apple", IsRipe: true, IsBruised: false),
        new("Dented apple", "Apple", IsRipe: true, IsBruised: true),
        new("Green apple", "Apple", IsRipe: false, IsBruised: false),
        new("Soft pear", "Pear", IsRipe: true, IsBruised: true),
        new("Hard pear", "Pear", IsRipe: false, IsBruised: false),
    ];

    /// <summary>The colours and border a gallery control is created with: null leaves the control's own default.</summary>
    private readonly record struct GalleryControlLook(Color? ContentColor, Color? TextColor, bool ShowsBorder);

    private static string DescribeTickedItems(ToggleList toggleList)
    {
        var tickedLabels = Enumerable.Range(0, toggleList.ItemCount).Where(toggleList.IsItemOn).Select(index => ToggleListElements[index].Label).ToList();
        return tickedLabels.Count == 0 ? "none" : string.Join(", ", tickedLabels);
    }

    private FieldRow AddFieldRow(ref float y, FieldEditorDescription description, FieldValue value)
    {
        var row = _context.UiEngine.ElementPoolService.CreateElement<FieldRow>(_window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = new Vector2(CaptionWidth + FieldWidth + 120, FieldRow.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        row.Configure(description, value);
        row.Committed += committed => _context.Status.Show($"{description.Label}: {Describe(committed)}");
        _window.AddChild(row);
        y += FieldRow.RowHeight + RowGap;
        return row;
    }

    private static string Describe(FieldValue value) =>
        value.ChoiceIndices is { } indices ? string.Join(", ", indices)
        : value.ChoiceIndex >= 0 ? value.ChoiceIndex.ToString()
        : value.Text.Length > 0 ? value.Text
        : $"{value.Number} / {value.Boolean}";

    /// <summary>A labelled line across the panel opening a group of rows.</summary>
    private void AddGroupHeading(ref float y, string text)
    {
        if (y > 0)
        {
            y += GroupHeadingGap - RowGap;
        }

        var heading = _context.UiEngine.ElementPoolService.CreateElement<TextDivider>(_window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = new Vector2(CaptionWidth + WideControlWidth, RowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        heading.Configure(text, WindowPalette.FocusAccentColor, widthFraction: 1f, textPosition: 0f);
        _window.AddChild(heading);
        y += RowHeight + RowGap;
    }

    /// <summary>A caption in the caption column and a control of size beside it, then y moved past the taller of the two.</summary>
    private T Add<T>(ref float y, string caption, Vector2 size, GalleryControlLook look, Action<T>? configure = null, bool multiline = false)
        where T : Element
    {
        AddCaption(y, caption);
        var control = CreateControl(new Vector2(CaptionWidth, y), size, look, configure, multiline);
        y += System.Math.Max(size.Y, RowHeight) + RowGap;
        return control;
    }

    private T CreateControl<T>(Vector2 position, Vector2 size, GalleryControlLook look, Action<T>? configure = null, bool multiline = false)
        where T : Element
    {
        var control = _context.UiEngine.ElementPoolService.CreateElement<T>(_window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = look.ShowsBorder },
            Content = look.ContentColor is { } contentColor ? new ElementContentOptions { ContentColor = contentColor } : null,
            Text = new TextOptions { TextColor = look.TextColor, Multiline = multiline ? true : null },
        });
        configure?.Invoke(control);
        _window.AddChild(control);
        return control;
    }

    private void AddCaption(float y, string caption)
    {
        var label = CreateLabel(_window, new Vector2(CaptionInset, y), new Vector2(CaptionWidth - CaptionInset * 2, RowHeight), caption, WindowPalette.ControlLabelTextColor);
        label.ShowsFullTextWhenCut = true;
    }

    private Label CreateLabel(Element parent, Vector2 position, Vector2 size, string text, Color textColor)
    {
        var label = _context.UiEngine.ElementPoolService.CreateElement<Label>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Text = new TextOptions { Text = text, TextColor = textColor },
        });
        parent.AddChild(label);
        return label;
    }

    private void FillListEditor(ListEditor editor) => editor.SetRows(_listEditorEntries.Count, (content, index) =>
    {
        var field = _context.UiEngine.ElementPoolService.CreateElement<TextBox>(content, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = content.ContentSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.EditableControlBackground },
            Text = new TextOptions { TextColor = WindowPalette.BodyTextColor },
        });
        content.AddChild(field);
        field.SetText(_listEditorEntries[index]);
        field.TextSubmitted += text =>
        {
            _listEditorEntries[index] = text;
            _context.Status.Show($"List editor: entry {index + 1} is {text}");
        };
    });

    private void AddPaneLabel(Window pane, string text) =>
        CreateLabel(pane, new Vector2(4, 4), new Vector2(120, RowHeight), text, WindowPalette.ControlLabelTextColor);

    private void OpenDialog() =>
        ModalDialog.Show(_context.UiEngine.ElementPoolService, _context.Layers, _tabArea, new ModalDialogRequest(
            "Disable Fireball",
            [
                new DialogButton("Replace", () => _context.Status.Show("Modal dialog: Replace")),
                new DialogButton("Cascade", () => _context.Status.Show("Modal dialog: Cascade")),
                new DialogButton("Cancel", () => _context.Status.Show("Modal dialog: Cancel")),
            ],
            CancelButtonIndex: 2)
        {
            DefaultButtonIndex = 0,
            Message = "Fireball is used by the Scroll of Fireball and the Wand of Fireball. Replace it, disable them too, or cancel.",
        });

    public void Resize(Rectangle area)
    {
        _tabArea = area;
        _window.SetSize(WindowSize(area));
    }

    public void Close()
    {
        _context.Layers.Remove(UiLayer.Base, _window);
        _window.Close();
    }

    private static Vector2 WindowSize(Rectangle area) => new(System.Math.Max(200, area.Width - Margin * 2), System.Math.Max(100, area.Height - Margin * 2));
}
