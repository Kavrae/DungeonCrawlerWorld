using FontStashSharp;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// A vertically-stacked list of clickable options next to the cursor -- e.g. a corpse's
/// right-click "Loot", or a TextBox's Cut/Copy/Paste/Select All. Always top-level (parent null),
/// same reasoning as Tooltip (see its own doc comment): a nested child's own MaximumSize gets
/// silently overwritten every layout pass, fine for content meant to stay inside its parent but
/// wrong for a popup meant to float outside it. One persistent, pooled instance shared across
/// every caller (see ContextMenuController, the only thing that ever calls Show/Hide) -- each
/// Show rebuilds its row Buttons fresh from whatever option list this call was given, the same
/// "shared mechanics, distributed content" split Tooltip's own ShowNear already uses (positioning/
/// layering mechanics live here; what the options actually are is entirely the caller's business).
/// </summary>
public sealed class ContextMenu(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    : Window(fontService, elementPoolService, glyphRenderer)
{
    private const float RowHeight = 22f;

    /// <summary>Shorter than an ordinary option row -- a header is a label, not a target the player aims a click at, so it doesn't need the same tap-friendly height.</summary>
    private const float HeaderRowHeight = 18f;

    /// <summary>Slightly darker than the menu's own white body -- see ContextMenu.Show's header-row branch. Same "light content area reads as a distinct panel" idiom WindowPalette.PanelContentColor already uses, just one step subtler here (a label row, not a whole separate panel).</summary>
    private static readonly Color HeaderBackgroundColor = Color.LightGray;

    /// <summary>Gap between an option's Label and its HotkeyText column, when present -- e.g. "Copy" and "Ctrl+C" need visible daylight between them, not just whatever's left over once both are right/left-aligned within the same row.</summary>
    private const float HotkeyGap = 24f;

    private readonly SpriteFontBase _font = fontService.GetFont(12);

    /// <summary>
    /// Repositions, rebuilds, and shows this menu with the given options next to topLeft --
    /// called fresh every open, never incrementally updated (a context menu's option list is
    /// small and short-lived, so "just rebuild it" is simpler than an add/remove API). Closes
    /// (pool-returns) whatever rows a previous Show left behind first, the same defensive
    /// clear Window.SetContent already does before attaching new content. Row height varies by
    /// kind (see RowHeight/HeaderRowHeight), so total height is an explicit sum, not a flat
    /// RowHeight * options.Count.
    /// </summary>
    public void Show(Vector2 topLeft, IReadOnlyList<ContextMenuOption> options)
    {
        ElementPoolService.CloseAllChildren(this);

        var width = MeasureWidth(options);
        var totalHeight = 0f;
        foreach (var option in options)
        {
            totalHeight += option.IsHeader ? HeaderRowHeight : RowHeight;
        }

        // The outer window's own border eats into its ContentSize (see RecalculateFixedSize) --
        // sizing the window to exactly width by totalHeight would leave a content area a couple
        // pixels shorter/narrower than that, which then clamps the first row's own Fixed size
        // down below its MinimumSize during Measure (parent-relative Measure always overwrites a
        // child's MaximumSize to the parent's real available content size -- see Tooltip's own
        // doc comment on the same fact). Adding BorderInsetDoubled back here is what makes the
        // *content* area actually come out to width by totalHeight, matching what every row
        // below is itself sized to.
        SetBounds(topLeft, new Vector2(width, totalHeight) + BorderInsetDoubled);

        var y = 0f;
        foreach (var option in options)
        {
            if (option.IsHeader)
            {
                AddHeaderRow(option, width, y);
                y += HeaderRowHeight;
                continue;
            }

            AddOptionRow(option, width, y);
            y += RowHeight;
        }

        IsVisible = true;
    }

    private void AddHeaderRow(ContextMenuOption option, float width, float y)
    {
        var rowSize = new Vector2(width, HeaderRowHeight);

        var header = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = rowSize, MinimumSize = rowSize, MaximumSize = rowSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = HeaderBackgroundColor },
            Text = new TextOptions { Text = option.Label, TextColor = WindowPalette.BodyTextColor, Bold = true },
        });
        header.ContentFont = _font; // Must match the font MeasureWidth used, or labels can clip against the row's own fixed width.

        AddChild(header);
    }

    private void AddOptionRow(ContextMenuOption option, float width, float y)
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
        button.LeftAlign = true; // A context-menu row always reads left-aligned, even without a HotkeyText column -- not the ink-centered look Button otherwise defaults to.
        button.Clicked += _ =>
        {
            option.OnSelect();
            Hide();
        };

        AddChild(button);
    }

    public void Hide() => IsVisible = false;

    /// <summary>Widest Label+HotkeyText pairing across every row (headers included, HotkeyText-less), so every row shares one width and the hotkey column lines up -- the same "measure the widest, pin every row to it" idiom GridControl's own sort tile uses.</summary>
    private float MeasureWidth(IReadOnlyList<ContextMenuOption> options)
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
