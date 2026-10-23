using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>
/// A vertically-stacked list of clickable options in a popup -- a right-click context menu at the cursor (a corpse's
/// "Loot", a TextBox's Cut/Copy/Paste/Select All), or a DropdownField's choices under the field. Always top-level
/// (parent null), same reasoning as Tooltip (see its own doc comment): a nested child's own MaximumSize gets
/// silently overwritten every layout pass, fine for content meant to stay inside its parent but
/// wrong for a popup meant to float outside it. One persistent, pooled instance shared across
/// every caller (see DropdownController, the only thing that ever calls Show/Hide) -- each
/// Show rebuilds its row Buttons fresh from whatever option list this call was given, the same
/// "shared mechanics, distributed content" split Tooltip's own ShowNear already uses (positioning/
/// layering mechanics live here; what the options actually are is entirely the caller's business).
/// </summary>
/// <remarks>
/// A list taller than the height Show is given scrolls. One row at a time is highlighted -- the hovered one, or the one
/// Up/Down moved to (see MoveHighlight) -- and Enter chooses it. A marked row (a DropdownField's current value) draws an
/// accent at its left edge.
/// </remarks>
public sealed class Dropdown(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Window(fontService, elementPoolService, renderService)
{
    private const float RowHeight = 22f;

    /// <summary>Shorter than an ordinary option row -- a header is a label, not a target the player aims a click at, so it doesn't need the same tap-friendly height.</summary>
    private const float HeaderRowHeight = 18f;

    /// <summary>Gap between an option's Label and its HotkeyText column, when present -- e.g. "Copy" and "Ctrl+C" need visible daylight between them, not just whatever's left over once both are right/left-aligned within the same row.</summary>
    private const float HotkeyGap = 24f;

    /// <summary>What an option that opens a submenu shows in its HotkeyText column.</summary>
    public const string SubmenuMarker = ">";

    private readonly SpriteFontBase _font = fontService.GetFont(FontChrome.DefaultFontSize);

    /// <summary>A submenu chosen this frame, shown on the next Update rather than from inside the click that chose it -- showing rebuilds every row, including the one whose click is still being handled.</summary>
    private IReadOnlyList<DropdownOption>? _pendingSubmenu;

    /// <summary>The row Button per option, null for a header -- indexed like the options Show was given.</summary>
    private readonly List<Button?> _optionRows = [];

    private readonly List<DropdownOption> _options = [];

    private int _highlightedIndex = -1;

    /// <summary>Shows a submenu the way the menu itself was opened (DropdownController), so it lands edge-aware at the same place. Set by the controller; while unset, a submenu opens at this menu's own top-left.</summary>
    public Action<IReadOnlyList<DropdownOption>>? OpenSubmenu { get; set; }

    /// <summary>The option Up/Down or the pointer last moved to, or -1 for none.</summary>
    public int HighlightedIndex => _highlightedIndex;

    /// <summary>
    /// Repositions, rebuilds, and shows this menu with the given options at topLeft --
    /// called fresh every open, never incrementally updated (an option list is
    /// small and short-lived, so "just rebuild it" is simpler than an add/remove API). Closes
    /// (pool-returns) whatever rows a previous Show left behind first, the same defensive
    /// clear Window.SetContent already does before attaching new content. Row height varies by
    /// kind (see RowHeight/HeaderRowHeight), so total height is an explicit sum, not a flat
    /// RowHeight * options.Count.
    /// </summary>
    /// <param name="minimumWidth">The narrowest the rows may be -- a DropdownField's own width, so its list is never narrower than the field.</param>
    /// <param name="maximumHeight">The tallest the list may be before it scrolls.</param>
    /// <param name="markedIndex">The option drawn with the marker and highlighted first, or -1 for none.</param>
    public void Show(Vector2 topLeft, IReadOnlyList<DropdownOption> options, float minimumWidth = 0f, float maximumHeight = float.MaxValue, int markedIndex = -1)
    {
        ElementPoolService.CloseAllChildren(this);
        _optionRows.Clear();
        _options.Clear();
        _options.AddRange(options);

        var rowsHeight = MeasureRowsHeight(options);
        var scrolls = rowsHeight > maximumHeight;
        CanUserScrollVertical = scrolls;
        var width = System.Math.Max(MeasureWidth(options), minimumWidth - BorderInsetDoubled.X - ChildContentPaddingDoubled.X - (scrolls ? WindowChrome.ScrollbarThickness : 0f));

        // The outer window's own border AND ContentPadding (this Window has children) both eat
        // into its ContentSize (see RecalculateFixedSize) -- sizing the window to exactly width
        // by totalHeight would leave a content area shorter/narrower than that, which then clamps
        // the first row's own Fixed size down below its MinimumSize during Measure (parent-relative
        // Measure always overwrites a child's MaximumSize to the parent's real available content
        // size -- see Tooltip's own doc comment on the same fact). Adding both insets back here is
        // what makes the *content* area actually come out to width by totalHeight, matching what
        // every row below is itself sized to.
        SetBounds(topLeft, MeasureOuterSize(width, System.Math.Min(rowsHeight, maximumHeight), scrolls));

        var y = 0f;
        for (var optionIndex = 0; optionIndex < options.Count; optionIndex++)
        {
            var option = options[optionIndex];
            if (option.IsHeader)
            {
                AddHeaderRow(option, width, y);
                _optionRows.Add(null);
                y += HeaderRowHeight;
                continue;
            }

            _optionRows.Add(AddOptionRow(option, width, y, isMarked: optionIndex == markedIndex));
            y += RowHeight;
        }

        ScrollTo(Vector2.Zero);
        SetHighlight(IsChoosable(markedIndex) ? markedIndex : -1);
        IsVisible = true;
    }

    private void AddHeaderRow(DropdownOption option, float width, float y)
    {
        var rowSize = new Vector2(width, HeaderRowHeight);

        var header = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = rowSize, MinimumSize = rowSize, MaximumSize = rowSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
            Text = new TextOptions { Text = option.Label, TextColor = WindowPalette.BodyTextColor, Bold = true },
        });
        header.ContentFont = _font; // Must match the font MeasureWidth used, or labels can clip against the row's own fixed width.

        AddChild(header);
    }

    private Button AddOptionRow(DropdownOption option, float width, float y, bool isMarked)
    {
        var rowSize = new Vector2(width, RowHeight);

        var button = ElementPoolService.CreateElement<Button>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = rowSize, MinimumSize = rowSize, MaximumSize = rowSize, DisplayMode = ElementDisplayMode.Fixed },
            // Flat, borderless rows -- a native context menu's look, not a toolbar of beveled
            // buttons. The hover highlight alone (see Button.DrawContent) is enough feedback.
            Chrome = new ElementChromeOptions { ShowBorder = false },
            Text = new TextOptions { Text = option.Label },
        });
        button.ContentFont = _font; // Must match the font MeasureWidth used, or labels can clip against the row's own fixed width.
        button.RightText = option.HotkeyText;
        button.Enabled = option.Enabled;
        button.LeftAlign = true; // A menu row always reads left-aligned, even without a HotkeyText column -- not the ink-centered look Button otherwise defaults to.
        button.IsMarked = isMarked;
        button.Clicked += _ => Choose(option);

        AddChild(button);
        return button;
    }

    private void Choose(DropdownOption option)
    {
        if (option.Submenu is { } submenu)
        {
            _pendingSubmenu = submenu;
            return;
        }

        option.OnSelect();
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
                ScrollIntoView(candidate);
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

        Choose(_options[_highlightedIndex]);
        return true;
    }

    private bool IsChoosable(int optionIndex) => optionIndex >= 0 && optionIndex < _options.Count && _options[optionIndex] is { IsHeader: false, Enabled: true };

    private void SetHighlight(int optionIndex)
    {
        _highlightedIndex = optionIndex;
        for (var rowIndex = 0; rowIndex < _optionRows.Count; rowIndex++)
        {
            if (_optionRows[rowIndex] is { } row)
            {
                row.IsHighlighted = rowIndex == optionIndex;
            }
        }
    }

    private void ScrollIntoView(int optionIndex)
    {
        if (_optionRows[optionIndex] is not { } row)
        {
            return;
        }

        var rowTop = row.RelativePosition.Y;
        var rowBottom = rowTop + RowHeight;
        if (rowTop < ScrollOffset.Y)
        {
            ScrollTo(new Vector2(0, rowTop));
            return;
        }

        if (rowBottom > ScrollOffset.Y + ContentSize.Y)
        {
            ScrollTo(new Vector2(0, rowBottom - ContentSize.Y));
        }
    }

    public void Hide()
    {
        IsVisible = false;
        _pendingSubmenu = null;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        for (var rowIndex = 0; rowIndex < _optionRows.Count; rowIndex++)
        {
            if (_optionRows[rowIndex] is { IsHovered: true } && rowIndex != _highlightedIndex)
            {
                SetHighlight(rowIndex);
                break;
            }
        }

        if (_pendingSubmenu is { } submenu)
        {
            _pendingSubmenu = null;
            if (OpenSubmenu is { } open)
            {
                open(submenu);
            }
            else
            {
                Show(AbsolutePosition, submenu);
            }
        }
    }

    /// <summary>The size Show will set this menu's own bounds to, given the same arguments -- exposed so DropdownController can position this menu edge-aware (PopupPositioning.GetPositionWithinBounds) before calling Show, since Show is also what first computes/applies this size.</summary>
    public Vector2 MeasureSize(IReadOnlyList<DropdownOption> options, float minimumWidth = 0f, float maximumHeight = float.MaxValue)
    {
        var rowsHeight = MeasureRowsHeight(options);
        var scrolls = rowsHeight > maximumHeight;
        var width = System.Math.Max(MeasureWidth(options), minimumWidth - BorderInsetDoubled.X - ChildContentPaddingDoubled.X - (scrolls ? WindowChrome.ScrollbarThickness : 0f));
        return MeasureOuterSize(width, System.Math.Min(rowsHeight, maximumHeight), scrolls);
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

    /// <summary>Widest Label+HotkeyText pairing across every row (headers included, HotkeyText-less), so every row shares one width and the hotkey column lines up -- the same "measure the widest, pin every row to it" idiom GridControl's own sort tile uses.</summary>
    private float MeasureWidth(IReadOnlyList<DropdownOption> options)
    {
        var widest = 0f;
        foreach (var option in options)
        {
            var labelWidth = _font.MeasureString(option.Label).X;
            var hotkeyWidth = option.HotkeyText is null ? 0f : HotkeyGap + _font.MeasureString(option.HotkeyText).X;
            widest = System.Math.Max(widest, labelWidth + hotkeyWidth);
        }

        return widest + Button.HorizontalTextInset * 2;
    }
}
