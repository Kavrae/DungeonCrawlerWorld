using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Fields;
using UIEngine.UI.Filtering;

namespace Workshop;

/// <summary>A scratch panel holding one of each editor control, wired to the status line -- for trying the controls out before an editor uses them.</summary>
public sealed class ControlGalleryTab : IIdeTab
{
    public const string TabTitle = "Controls";

    private const float Margin = 12f;
    private const float LabelWidth = 150f;
    private const float FieldWidth = 220f;
    private const float RowHeight = 26f;
    private const float RowGap = 10f;

    private static readonly string[] Shapes = ["Single", "Line", "Cone", "Circle", "Square"];
    private static readonly string[] ManyOptions = [.. Enumerable.Range(1, 40).Select(index => $"Option {index}")];

    private WorkshopTabContext _context = null!;
    private Window _window = null!;
    private readonly List<string> _listItems = ["Burning", "Poison", "Holy Ground"];

    public string Title => TabTitle;

    public bool ShowsRelatedRecords => false;

    public void Open(WorkshopTabContext context, Rectangle area)
    {
        _context = context;
        var elementPool = context.UiEngine.ElementPoolService;
        _window = elementPool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(area.X + Margin, area.Y + Margin), Size = WindowSize(area), MaximumSize = new Vector2(4000, 4000), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Controls", ShowBorder = true, CanUserScrollVertical = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        _window.Initialize();
        context.Layers.Add(UiLayer.Base, _window);

        var y = 0f;
        var shape = Add<DropdownField>(ref y, "Dropdown", new Vector2(FieldWidth, RowHeight));
        shape.SetOptions(Shapes, 0);
        shape.SelectionChanged += index => context.Status.Show($"Dropdown: {Shapes[index]}");

        var longList = Add<DropdownField>(ref y, "Long dropdown", new Vector2(FieldWidth, RowHeight));
        longList.Placeholder = "Choose one";
        longList.SetOptions(ManyOptions, -1);
        longList.SelectionChanged += index => context.Status.Show($"Long dropdown: {ManyOptions[index]}");

        var range = Add<NumericField>(ref y, "Whole number 0-20", new Vector2(FieldWidth, RowHeight), field => field.Configure(5, 0, 20));
        range.ValueChanged += value => context.Status.Show($"Whole number: {value}");

        var magnitude = Add<NumericField>(ref y, "Decimal -1-1", new Vector2(FieldWidth, RowHeight), field => field.Configure(0.25, -1, 1, step: 0.05, decimalPlaces: 2));
        magnitude.ValueChanged += value => context.Status.Show($"Decimal: {value}");

        var colour = Add<ColorField>(ref y, "Colour", new Vector2(FieldWidth, RowHeight), field => field.Configure(Color.Orange));
        colour.ValueChanged += value => context.Status.Show($"Colour: {ColorField.Format(value)}");

        var editor = Add<ListEditor>(ref y, "List editor", new Vector2(FieldWidth + 80, 150), list => list.Configure(rowHeight: 24));
        editor.AddRequested += () => { _listItems.Add($"Entry {_listItems.Count + 1}"); FillList(editor); };
        editor.RemoveRequested += index => { _listItems.RemoveAt(index); FillList(editor); };
        editor.MoveRequested += (from, to) => { (_listItems[from], _listItems[to]) = (_listItems[to], _listItems[from]); FillList(editor); };
        FillList(editor);

        var list = Add<ListView>(ref y, "List view", new Vector2(420, 150));
        list.SetColumns([new ListViewColumn("Name", 160), new ListViewColumn("Count", 80), new ListViewColumn("Note", 100, Sortable: false)], showIconColumn: true);
        list.SetRows([.. Enumerable.Range(1, 30).Select(index => new ListViewRow([$"Row {index}", (index * 7 % 31).ToString(), index % 3 == 0 ? "third" : string.Empty], index % 5 == 0 ? "!" : null, Color.DarkGoldenrod))]);
        list.RowActivated += row => context.Status.Show($"List view: activated {row.Cells[0]}");

        var split = Add<SplitPane>(ref y, "Split pane", new Vector2(420, 120), pane => pane.Configure(SplitOrientation.SideBySide, SplitSizedPane.First, 160));
        AddPaneLabel(split.FirstPane, "Left pane");
        AddPaneLabel(split.SecondPane, "Right pane");
        split.SplitChanged += size => context.Status.Show($"Split pane: {size:N0}");

        var dialogButton = Add<Button>(ref y, "Modal dialog", new Vector2(120, RowHeight), button => button.LeftText = "Open dialog");
        dialogButton.Clicked += _ => OpenDialog(area);

        var searchable = Add<SearchableChoiceField>(ref y, "Searchable choice", new Vector2(FieldWidth, RowHeight), field => field.Configure(ManyOptions, 2, allowsFreeText: false));
        searchable.Chosen += (index, text) => context.Status.Show($"Searchable choice: {text} ({index})");

        var freeText = Add<SearchableChoiceField>(ref y, "Searchable, free text", new Vector2(FieldWidth, RowHeight), field => field.Configure(Shapes, -1, allowsFreeText: true, "Hexagon"));
        freeText.Chosen += (index, text) => context.Status.Show($"Searchable, free text: {text} ({index})");

        var multi = Add<MultiChoiceField>(ref y, "Multi-choice", new Vector2(FieldWidth, RowHeight), field => field.Configure(Shapes, [1, 3]));
        multi.ChoicesChanged += indices => context.Status.Show($"Multi-choice: {string.Join(", ", indices.Select(index => Shapes[index]))}");

        AddListFilter(ref y);

        var section = Add<FieldSectionHeader>(ref y, "Section header", new Vector2(FieldWidth + 80, RowHeight), header => header.Configure("Effects (2)", depth: 1, isCollapsible: true, isExpanded: true, ["Add", "^", "v", "X"], [true, false, true, true]));
        section.ExpandToggled += isExpanded => context.Status.Show($"Section header: {(isExpanded ? "open" : "closed")}");
        section.ButtonClicked += index => context.Status.Show($"Section header: button {index}");

        AddFieldRow(ref y, new FieldEditorDescription("Name", FieldValueKind.Text), FieldValue.OfText("Wand of Fireball"));
        AddFieldRow(ref y, new FieldEditorDescription("Gold value (unset)", FieldValueKind.Integer) { Depth = 1, Minimum = 0, IsSet = false }, FieldValue.OfNumber(0));
        AddFieldRow(ref y, new FieldEditorDescription("Can trade", FieldValueKind.Boolean) { Depth = 1 }, FieldValue.OfBoolean(true));
        var problemRow = AddFieldRow(ref y, new FieldEditorDescription("Shape", FieldValueKind.Choice) { Depth = 2, Choices = Shapes }, FieldValue.OfChoice(0));
        problemRow.SetProblem("A problem with this value", isWarning: false);
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

        var button = Add<ListFilterButton>(ref y, "List filter", new Vector2(90, RowHeight), control => control.Configure(new ListFilterMenuSource<GalleryFruit>(filter, () => GalleryBasket)));
        var listed = _context.UiEngine.ElementPoolService.CreateElement<TextWindow>(_window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(LabelWidth + 100, button.RelativePosition.Y), Size = new Vector2(FieldWidth + 120, RowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = ListedFruit(filter), TextColor = WindowPalette.BodyTextColor },
        });
        _window.AddChild(listed);

        var chipsWidth = FieldWidth;
        var chips = _context.UiEngine.ElementPoolService.CreateElement<ListFilterChips>(_window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(LabelWidth, y), Size = new Vector2(chipsWidth, 0), MaximumSize = new Vector2(chipsWidth, ChipsSpace), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        chips.ContentPadding = Vector2.Zero;
        chips.Configure(new ListFilterMenuSource<GalleryFruit>(filter, () => GalleryBasket), chipsWidth);
        _window.AddChild(chips);
        y += ChipsSpace + RowGap;

        filter.Selection.Changed += selection =>
        {
            listed.UpdateText(ListedFruit(filter));
            _context.Status.Show($"List filter: {string.Join(", ", selection.CheckedIds())}");
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

    private FieldRow AddFieldRow(ref float y, FieldEditorDescription description, FieldValue value)
    {
        var row = _context.UiEngine.ElementPoolService.CreateElement<FieldRow>(_window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = new Vector2(LabelWidth + FieldWidth + 120, FieldRow.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        row.Configure(description, value);
        row.Committed += committed => _context.Status.Show($"Field row {description.Label}: {Describe(committed)}");
        _window.AddChild(row);
        y += FieldRow.RowHeight + RowGap;
        return row;
    }

    private static string Describe(FieldValue value) =>
        value.ChoiceIndices is { } indices ? string.Join(", ", indices)
        : value.ChoiceIndex >= 0 ? value.ChoiceIndex.ToString()
        : value.Text.Length > 0 ? value.Text
        : $"{value.Number} / {value.Boolean}";

    private T Add<T>(ref float y, string label, Vector2 size, Action<T>? configure = null)
        where T : Element
    {
        var elementPool = _context.UiEngine.ElementPoolService;
        var caption = elementPool.CreateElement<TextWindow>(_window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = new Vector2(LabelWidth, RowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = label, TextColor = WindowPalette.BodyTextColor },
        });
        _window.AddChild(caption);

        var control = elementPool.CreateElement<T>(_window, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(LabelWidth, y), Size = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
        });
        configure?.Invoke(control);
        _window.AddChild(control);
        y += size.Y + RowGap;
        return control;
    }

    private void FillList(ListEditor editor) => editor.SetRows(_listItems.Count, (content, index) =>
    {
        var field = _context.UiEngine.ElementPoolService.CreateElement<TextBox>(content, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = content.ContentSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
        });
        content.AddChild(field);
        field.SetText(_listItems[index]);
    });

    private void AddPaneLabel(Window pane, string text)
    {
        var label = _context.UiEngine.ElementPoolService.CreateElement<TextWindow>(pane, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(4, 4), Size = new Vector2(120, RowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.TitleTextColor },
        });
        pane.AddChild(label);
    }

    private void OpenDialog(Rectangle area) =>
        ModalDialog.Show(_context.UiEngine.ElementPoolService, _context.Layers, area, new ModalDialogRequest(
            "Disable Fireball",
            [
                new DialogButton("Replace", () => _context.Status.Show("Dialog: Replace")),
                new DialogButton("Cascade", () => _context.Status.Show("Dialog: Cascade")),
                new DialogButton("Cancel", () => _context.Status.Show("Dialog: Cancel")),
            ],
            CancelButtonIndex: 2)
        {
            Message = "Fireball is used by the Scroll of Fireball and the Wand of Fireball. Replace it, disable them too, or cancel.",
        });

    public void Resize(Rectangle area) => _window.SetSize(WindowSize(area));

    public void Close()
    {
        _context.Layers.Remove(UiLayer.Base, _window);
        _window.Close();
    }

    private static Vector2 WindowSize(Rectangle area) => new(System.Math.Max(200, area.Width - Margin * 2), System.Math.Max(100, area.Height - Margin * 2));
}
