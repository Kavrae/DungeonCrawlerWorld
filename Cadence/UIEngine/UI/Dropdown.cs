using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>
/// A vertically-stacked list of clickable options in a popup -- a right-click context menu at the cursor (a corpse's
/// "Loot", a TextBox's Cut/Copy/Paste/Select All), or a DropdownField's choices under the field. Always top-level
/// (parent null), same reasoning as Tooltip (see its own doc comment): a child is measured within its parent's
/// content area, fine for content meant to stay inside its parent but
/// wrong for a popup meant to float outside it. One persistent, pooled instance per menu level shared across
/// every caller -- the root list and each submenu beside it (see DropdownController, the only thing that ever calls
/// Show/Hide) -- each
/// Show rebuilds its rows fresh from whatever option list this call was given, the same
/// "shared mechanics, distributed content" split Tooltip's own ShowNear already uses (positioning/
/// layering mechanics live here; what the options actually are is entirely the caller's business).
/// </summary>
/// <remarks>
/// Each option is an ordinary control: a Button, or for a checkbox option (DropdownOption.IsChecked) a Toggle with a
/// flat checkbox, holding the option's icon (when any option has one), its label taking the row's width, and its
/// hotkey or count at the right end. A row is as wide as its content needs, and every row is then fixed at the widest.
/// The rows draw no hover of their own: which row is current is the menu's to know, so it draws the row fills, the
/// highlight and the mark behind them. A list taller than the height Show is given scrolls. At most one row at a time is
/// highlighted -- the hovered one, gone again once the pointer is over no option, or the one Up/Down moved to (see
/// MoveHighlight), which stays -- and Enter chooses it. Choosing a
/// checkbox row turns it and leaves the list open. A marked row (a DropdownField's current value) draws an accent at
/// its left edge.
/// </remarks>
public sealed class Dropdown(ElementServices services)
    : Window(services)
{
    private const float RowHeight = 22f;

    /// <summary>Shorter than an ordinary option row -- a header is a label, not a target the player aims a click at, so it doesn't need the same tap-friendly height.</summary>
    private const float HeaderRowHeight = 18f;

    /// <summary>Gap between an option's Label and its HotkeyText column, when present -- e.g. "Copy" and "Ctrl+C" need visible daylight between them.</summary>
    private const float HotkeyGap = 24f;

    /// <summary>Room either side of a row's content, so neither column sits against the row's edges.</summary>
    private const float RowTextInset = 4f;

    /// <summary>How much further in an option under a heading starts than the heading, so which heading it belongs to reads from its place.</summary>
    public const float HeadingIndent = 12f;

    /// <summary>An option icon's box, and the gap after it.</summary>
    private const float LeadingIconSide = 16f;

    private const float LeadingIconGap = 4f;

    /// <summary>How wide the marked row's accent is -- inside RowTextInset, so it never reaches the text.</summary>
    private const int MarkerWidth = 3;

    /// <summary>What an option that opens a submenu shows in its HotkeyText column.</summary>
    public const string SubmenuMarker = ">";

    private readonly SpriteFontBase _font = services.FontService.GetFont(FontChrome.DefaultFontSize);

    /// <summary>The row control per option, null for a header -- indexed like the options Show was given.</summary>
    private readonly List<PressableElement?> _optionRows = [];

    private readonly List<DropdownOption> _options = [];

    private int _highlightedIndex = -1;

    /// <summary>Options to replace the open list's with on the next Update (ReplaceOptions), so no row is rebuilt inside the click that asked.</summary>
    private IReadOnlyList<DropdownOption>? _pendingOptions;

    private float _shownMinimumWidth;
    private float _shownMaximumHeight = float.MaxValue;
    private int _shownMarkedIndex = -1;
    private bool _shownIndentsUnderHeadings;

    private int _openSubmenuIndex = -1;

    /// <summary>Where this menu was scrolled when its submenu opened, so scrolling it afterwards can close the submenu.</summary>
    private Vector2 _scrollOffsetAtSubmenuOpen;

    /// <summary>Opens a submenu beside the option at the given index (DropdownController) -- true when a key chose it. A menu with none set ignores submenu options.</summary>
    internal Action<int, IReadOnlyList<DropdownOption>, bool>? SubmenuChosen { get; set; }

    /// <summary>Closes the menu after an ordinary option ran -- with its submenus and parents (DropdownController); hides only this menu while unset.</summary>
    internal Action? OptionChosen { get; set; }

    /// <summary>Closes the menus below this one (DropdownController) -- choosing the row whose submenu is open again, a press on this menu off its options (a heading, empty space), or this menu scrolling.</summary>
    internal Action? CloseSubmenus { get; set; }

    /// <summary>The option whose submenu is open beside this menu, drawn highlighted while it is; -1 for none.</summary>
    public int OpenSubmenuIndex
    {
        get => _openSubmenuIndex;
        internal set
        {
            _openSubmenuIndex = value;
            _scrollOffsetAtSubmenuOpen = ScrollOffset;
        }
    }

    /// <summary>The option Up/Down or the pointer last moved to, or -1 for none.</summary>
    public int HighlightedIndex => _highlightedIndex;

    /// <summary>The option drawn with the marker, or -1 for none.</summary>
    public int MarkedIndex => _shownMarkedIndex;

    /// <summary>The row control per option shown, null for a header -- indexed like the options.</summary>
    public IReadOnlyList<PressableElement?> OptionRows => _optionRows;

    /// <summary>
    /// Rebuilds and shows this menu with the given options at topLeft -- called fresh every open, never incrementally
    /// updated (an option list is small and short-lived, so "just rebuild it" is simpler than an add/remove API). Closes
    /// (pool-returns) whatever rows a previous Show left behind first. Its size comes from the rows it builds, so a
    /// caller placing it edge-aware shows it first and then moves it (DropdownController).
    /// </summary>
    /// <param name="minimumWidth">The narrowest the menu may be -- a DropdownField's own width, so its list is never narrower than the field.</param>
    /// <param name="maximumHeight">The tallest the list may be before it scrolls.</param>
    /// <param name="markedIndex">The option drawn with the marker and highlighted first, or -1 for none.</param>
    /// <param name="indentsUnderHeadings">Whether every option after a heading is indented (HeadingIndent), so which heading it belongs to reads from its place.</param>
    public void Show(Vector2 topLeft, IReadOnlyList<DropdownOption> options, float minimumWidth = 0f, float maximumHeight = float.MaxValue, int markedIndex = -1, bool indentsUnderHeadings = false)
    {
        BuildRows(topLeft, options, minimumWidth, maximumHeight, markedIndex, indentsUnderHeadings);
        ScrollTo(Vector2.Zero);
        SetHighlight(IsChoosable(markedIndex) ? markedIndex : -1);
        IsVisible = true;
    }

    /// <summary>Replaces the open list's options on the next Update, keeping its place, its scroll and its highlight -- options reread after one was chosen (a count beside each). Nothing while it is hidden.</summary>
    public void ReplaceOptions(IReadOnlyList<DropdownOption> options)
    {
        if (IsVisible)
        {
            _pendingOptions = options;
        }
    }

    private void ApplyReplacedOptions(IReadOnlyList<DropdownOption> options)
    {
        var scroll = ScrollOffset;
        var highlightedIndex = _highlightedIndex;
        BuildRows(AbsolutePosition, options, _shownMinimumWidth, _shownMaximumHeight, _shownMarkedIndex, _shownIndentsUnderHeadings);
        ScrollTo(scroll);
        SetHighlight(IsChoosable(highlightedIndex) ? highlightedIndex : -1);
    }

    private void BuildRows(Vector2 topLeft, IReadOnlyList<DropdownOption> options, float minimumWidth, float maximumHeight, int markedIndex, bool indentsUnderHeadings)
    {
        _pendingOptions = null;
        _shownMinimumWidth = minimumWidth;
        _shownMaximumHeight = maximumHeight;
        _shownMarkedIndex = markedIndex;
        _shownIndentsUnderHeadings = indentsUnderHeadings;
        _openSubmenuIndex = -1;
        ElementPoolService.CloseAllChildren(this);
        _optionRows.Clear();
        _options.Clear();
        _options.AddRange(options);

        var rowsHeight = MeasureRowsHeight(options);
        var scrolls = rowsHeight > maximumHeight;
        CanUserScrollVertical = scrolls;

        // Each row is first built as wide as its content needs, inside the widest this menu may be, then every row is
        // fixed at the widest (or what minimumWidth leaves), so the hotkey column lines up.
        SetBounds(topLeft, MaximumSize);
        var rows = new List<Element>(options.Count);
        var hasIcons = HasIcons(options);
        var isUnderHeading = false;
        var y = 0f;
        for (var optionIndex = 0; optionIndex < options.Count; optionIndex++)
        {
            var option = options[optionIndex];
            if (option.IsHeader)
            {
                rows.Add(AddHeaderRow(option, y));
                _optionRows.Add(null);
                isUnderHeading = indentsUnderHeadings;
                y += HeaderRowHeight;
                continue;
            }

            var row = AddOptionRow(option, optionIndex, y, hasIcons, isUnderHeading);
            rows.Add(row);
            _optionRows.Add(row);
            y += RowHeight;
        }

        var widestRow = 0f;
        foreach (var row in rows)
        {
            widestRow = System.Math.Max(widestRow, row.CurrentSize.X);
        }

        var width = System.Math.Max(widestRow, minimumWidth - BorderInsetDoubled.X - ChildContentPaddingDoubled.X - (scrolls ? WindowChrome.ScrollbarThickness : 0f));
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            rows[rowIndex].SetFixedSize(new Vector2(width, options[rowIndex].IsHeader ? HeaderRowHeight : RowHeight));
        }

        // The outer window's own border AND ContentPadding (this Window has children) both eat into its ContentSize
        // (see RecalculateFixedSize), so both are added back here to make the content area come out to exactly the
        // rows' width by their height.
        SetBounds(topLeft, MeasureOuterSize(width, System.Math.Min(rowsHeight, maximumHeight), scrolls));
    }

    /// <summary>A heading: its label inset like an option's, on the panel colour; the options under it are indented when the menu indents.</summary>
    private Window AddHeaderRow(DropdownOption option, float y)
    {
        var header = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal, ChildCrossAlignment = ChildCrossAlignment.Center },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        header.ContentPadding = new Vector2(RowTextInset, 0);
        AddChild(header);

        AddRowLabel(header, option.Label, fillsRemaining: false, spacingAfter: 0f);
        return header;
    }

    /// <summary>An option's row: a Button, or a Toggle with a flat checkbox for a checkbox option, holding its icon (when any option has one), its label taking the row's width, and its hotkey or count at the right end -- everything in it indented when it is under a heading in a menu that indents.</summary>
    private PressableElement AddOptionRow(DropdownOption option, int optionIndex, float y, bool hasIcons, bool isUnderHeading)
    {
        var rowOptions = new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { ChildrenTileMode = ChildElementTileMode.Horizontal, ChildCrossAlignment = ChildCrossAlignment.Center, ContentIndent = isUnderHeading ? HeadingIndent : 0f },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), DisplayMode = ElementDisplayMode.WrapContent, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, CanUserFocus = false },
        };

        PressableElement row;
        if (option.IsChecked is { } isChecked)
        {
            var toggle = ElementPoolService.CreateElement<Toggle>(this, rowOptions);
            toggle.CheckboxStyle = ToggleCheckboxStyle.Flat;
            toggle.Configure(isChecked, Color.White, Color.White, _ => Choose(optionIndex));
            row = toggle;
        }
        else
        {
            var button = ElementPoolService.CreateElement<Button>(this, rowOptions);
            button.ShowsHoverHighlight = false;
            button.Clicked += _ => Choose(optionIndex);
            row = button;
        }

        row.ContentPadding = new Vector2(RowTextInset, 0);
        row.Enabled = option.Enabled;
        row.TooltipText = option.TooltipText;
        AddChild(row);

        if (hasIcons)
        {
            var icon = option.Icon ?? GlyphIcon.Blank;
            var iconElement = ElementPoolService.CreateElement<IconElement>(row, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(LeadingIconSide, RowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true, SpacingAfter = LeadingIconGap },
            });
            iconElement.Configure(null, icon.Glyph, icon.Color, _font);
            row.AddChild(iconElement);
        }

        AddRowLabel(row, option.Label, fillsRemaining: true, spacingAfter: option.HotkeyText is null ? 0f : HotkeyGap);
        if (option.HotkeyText is { } hotkeyText)
        {
            AddRowLabel(row, hotkeyText, fillsRemaining: false, spacingAfter: 0f);
        }

        return row;
    }

    private Label AddRowLabel(Element row, string text, bool fillsRemaining, float spacingAfter)
    {
        var label = ElementPoolService.CreateElement<Label>(row, new ElementOptions
        {
            Layout = new ElementLayoutOptions { FillsRemaining = fillsRemaining, SpacingAfter = spacingAfter },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.BodyTextColor },
        });
        label.SetFont(_font);
        row.AddChild(label);
        return label;
    }

    /// <summary>Runs the option at optionIndex: opens its submenu beside this menu (closes it when it is already open), or turns a checkbox row and leaves the list open with its scroll and highlight where they are, or runs it and closes.</summary>
    private void Choose(int optionIndex, bool fromKey = false)
    {
        var option = _options[optionIndex];
        if (option.Submenu is { } submenu)
        {
            if (optionIndex == _openSubmenuIndex)
            {
                CloseSubmenus?.Invoke();
                return;
            }

            SubmenuChosen?.Invoke(optionIndex, submenu, fromKey);
            return;
        }

        if (option.IsChecked is { } isChecked)
        {
            _options[optionIndex] = option with { IsChecked = !isChecked };
            if (_optionRows[optionIndex] is Toggle toggle)
            {
                toggle.SetIsOn(!isChecked);
            }

            option.OnSelect();
            return;
        }

        option.OnSelect();
        if (OptionChosen is { } optionChosen)
        {
            optionChosen();
            return;
        }

        Hide();
    }

    /// <summary>Moves the highlight to the next choosable option in direction (+1 down, -1 up), wrapping at either end, and scrolls it into view.</summary>
    public void MoveHighlight(int direction)
    {
        if (_options.Count == 0)
        {
            return;
        }

        var start = _highlightedIndex < 0 ? (direction > 0 ? -1 : _options.Count) : _highlightedIndex;
        for (var step = 1; step <= _options.Count; step++)
        {
            var candidate = ((start + direction * step) % _options.Count + _options.Count) % _options.Count;
            if (IsChoosable(candidate))
            {
                SetHighlight(candidate);
                ScrollOptionIntoView(candidate);
                return;
            }
        }
    }

    /// <summary>Chooses the highlighted option as a click on it would. Returns whether one was highlighted.</summary>
    public bool ChooseHighlighted()
    {
        if (!IsChoosable(_highlightedIndex))
        {
            return false;
        }

        Choose(_highlightedIndex, fromKey: true);
        return true;
    }

    /// <summary>Opens the highlighted option's submenu as Enter on it would. Returns whether the highlighted option has one.</summary>
    public bool OpenHighlightedSubmenu()
    {
        if (!IsChoosable(_highlightedIndex) || _options[_highlightedIndex].Submenu is null)
        {
            return false;
        }

        Choose(_highlightedIndex, fromKey: true);
        return true;
    }

    /// <summary>Its submenu closed: the row that had opened it is highlighted, as if Up/Down had moved there.</summary>
    internal void ReturnFromSubmenu()
    {
        if (_openSubmenuIndex >= 0)
        {
            SetHighlight(_openSubmenuIndex);
        }

        _openSubmenuIndex = -1;
    }

    private bool IsChoosable(int optionIndex) => optionIndex >= 0 && optionIndex < _options.Count && _options[optionIndex] is { IsHeader: false, Enabled: true };

    /// <summary>Highlights optionIndex as Up/Down, a replaced list or the marked value do; the pointer leaving the list leaves it highlighted.</summary>
    private void SetHighlight(int optionIndex)
    {
        _highlightedIndex = optionIndex;
        _isHighlightFromPointer = false;
    }

    /// <summary>Whether the highlight is where the pointer put it, and so goes once the pointer is over no option.</summary>
    private bool _isHighlightFromPointer;

    /// <summary>Highlights the hovered option; once the pointer is over no option (off the list, on a heading or a disabled row), clears a highlight the pointer gave.</summary>
    private void FollowPointer()
    {
        for (var rowIndex = 0; rowIndex < _optionRows.Count; rowIndex++)
        {
            if (_optionRows[rowIndex] is { IsHovered: true })
            {
                _highlightedIndex = rowIndex;
                _isHighlightFromPointer = true;
                return;
            }
        }

        if (_isHighlightFromPointer)
        {
            _highlightedIndex = -1;
            _isHighlightFromPointer = false;
        }
    }

    private void ScrollOptionIntoView(int optionIndex)
    {
        if (_optionRows[optionIndex] is not { } row)
        {
            return;
        }

        ScrollIntoView(row.RelativePosition.Y, row.RelativePosition.Y + RowHeight);
    }

    public void Hide()
    {
        IsVisible = false;
        _openSubmenuIndex = -1;
        _pendingOptions = null;
    }

    /// <summary>A click that reached the menu itself landed off its options (a heading, empty space): it closes any submenu open beside this menu.</summary>
    protected override bool OnContentClickAction(Point mousePosition)
    {
        if (_openSubmenuIndex >= 0)
        {
            CloseSubmenus?.Invoke();
        }

        return true;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_pendingOptions is { } replacedOptions)
        {
            ApplyReplacedOptions(replacedOptions);
        }

        FollowPointer();

        if (_openSubmenuIndex >= 0 && ScrollOffset != _scrollOffsetAtSubmenuOpen)
        {
            CloseSubmenus?.Invoke();
        }
    }

    /// <summary>Behind the rows: each option row's fill, the hover highlight on the highlighted row and on the row whose submenu is open, and the accent at the marked row's left edge.</summary>
    public override void DrawContent(GameTime gameTime)
    {
        base.DrawContent(gameTime);

        var spriteBatch = Render.Batch;
        for (var rowIndex = 0; rowIndex < _optionRows.Count; rowIndex++)
        {
            if (_optionRows[rowIndex] is not { } row)
            {
                continue;
            }

            var rowRectangle = new Rectangle((int)row.RelativePosition.X, (int)row.RelativePosition.Y, (int)row.CurrentSize.X, (int)row.CurrentSize.Y);
            spriteBatch.FillRectangle(rowRectangle, WindowPalette.PanelContentColor);
            if (rowIndex == _highlightedIndex || rowIndex == _openSubmenuIndex)
            {
                spriteBatch.FillRectangle(rowRectangle, WindowPalette.HoverDark);
            }

            if (rowIndex == _shownMarkedIndex)
            {
                spriteBatch.FillRectangle(new Rectangle(rowRectangle.X, rowRectangle.Y, MarkerWidth, rowRectangle.Height), WindowPalette.FocusAccentColor);
            }
        }
    }

    private Vector2 MeasureOuterSize(float rowWidth, float visibleHeight, bool scrolls) =>
        new Vector2(rowWidth + (scrolls ? WindowChrome.ScrollbarThickness : 0f), visibleHeight) + BorderInsetDoubled + ChildContentPaddingDoubled;

    private static float MeasureRowsHeight(IReadOnlyList<DropdownOption> options)
    {
        var totalHeight = 0f;
        foreach (var option in options)
        {
            totalHeight += option.IsHeader ? HeaderRowHeight : RowHeight;
        }

        return totalHeight;
    }

    /// <summary>Whether any row has an icon, so every row gives one its place.</summary>
    private static bool HasIcons(IReadOnlyList<DropdownOption> options) => options.Any(option => option.Icon is not null);
}
