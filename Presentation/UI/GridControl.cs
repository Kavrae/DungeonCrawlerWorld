using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// A reusable row of grid-scoped controls -- an item count display, a click-to-cycle sort
/// button, a click-to-toggle filter button, and a debounced search box -- entirely generic: no
/// reference to items, tags, or any specific grid content anywhere in this class. A caller
/// supplies the sort option labels and toggle label at Configure time and reacts to
/// SortOptionCycled/ToggleChanged/SearchFilterChanged; GridControl itself never knows what any of
/// those mean (see InventoryTabContent for the Inventory-specific translation, the first real
/// consumer). A Window subclass, the same "subclass warranted" reasoning as
/// MapWindow/AbilityScoreWindow: it composes several children and owns its own event/update
/// logic. Controls are click-to-cycle/click-to-toggle only -- a context-menu-driven sort picker
/// and a checkbox in place of the toggle button are both deliberately deferred, see TODO.md's
/// "Advanced sort control" and "Checkbox widget to replace the Hide Disabled toggle button".
/// </summary>
public sealed class GridControl(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    : Window(fontService, elementPoolService, glyphRenderer)
{
    public const float RowHeight = 24f;

    private const float ControlGap = 4f;
    private const float HorizontalPadding = 6f;
    private const float SearchBoxWidth = 112f;

    /// <summary>Widest count text this control ever needs room for without visibly resizing as digits come and go -- four digits comfortably covers any realistic inventory tab.</summary>
    private const string WidestCountText = "9999 items";

    /// <summary>How long the search box's text must sit unchanged before it's applied -- see DebouncedTextFilter.</summary>
    private static readonly int SearchDebounceFrames = GameTiming.FramesForSeconds(0.3f);

    private static readonly Color ControlColor = new(48, 48, 48);
    private static readonly Color LabelColor = Color.White;

    /// <summary>Fires with the newly-selected option's index into the labels Configure was given -- GridControl never knows what any index means.</summary>
    public event Action<int>? SortOptionCycled;

    /// <summary>Fires with the toggle's new on/off state.</summary>
    public event Action<bool>? ToggleChanged;

    /// <summary>Fires with the debounced, applied search text -- see DebouncedTextFilter.</summary>
    public event Action<string>? SearchFilterChanged;

    private readonly DebouncedTextFilter _searchFilter = new(SearchDebounceFrames);

    private IReadOnlyList<string> _sortOptionLabels = [];
    private string _toggleLabel = string.Empty;
    private string _searchGhostText = string.Empty;
    private int _sortOptionIndex;
    private bool _isToggleOn;

    private SpriteFontBase _font = null!;
    private TextWindow _countLabel = null!;
    private TextWindow _sortButton = null!;
    private TextWindow _toggleButton = null!;
    private TextBox _searchBox = null!;

    /// <summary>Must be called after CreateElement but before Initialize (same contract as InventoryManagementWindow.Configure/AbilityScoreWindow.Configure) -- sortOptionLabels must be non-empty, since the sort button always shows whichever one is currently selected.</summary>
    public void Configure(IReadOnlyList<string> sortOptionLabels, string toggleLabel, string searchGhostText)
    {
        if (sortOptionLabels.Count == 0)
        {
            throw new ArgumentException("GridControl needs at least one sort option label.", nameof(sortOptionLabels));
        }

        _sortOptionLabels = sortOptionLabels;
        _sortOptionIndex = 0;
        _isToggleOn = false;
        _toggleLabel = toggleLabel;
        _searchGhostText = searchGhostText;
    }

    public override void Initialize()
    {
        base.Initialize();

        _font = fontService.GetFont((int)(RowHeight * 0.6f));

        var x = HorizontalPadding;

        _countLabel = CreateTile(WidestCountText, MeasureTileWidth(WidestCountText), x, showBorder: false);
        AddChild(_countLabel);
        x += _countLabel.CurrentSize.X + ControlGap;

        var sortButtonWidth = MeasureWidest(_sortOptionLabels);
        _sortButton = CreateTile(_sortOptionLabels[_sortOptionIndex], sortButtonWidth, x, showBorder: true);
        _sortButton.Clicked += _ => CycleSortOption();
        AddChild(_sortButton);
        x += _sortButton.CurrentSize.X + ControlGap;

        _toggleButton = CreateTile(_toggleLabel, MeasureTileWidth(_toggleLabel), x, showBorder: true);
        _toggleButton.BorderStyle = BorderStyle.Inset;
        _toggleButton.Clicked += _ => ToggleOnOff();
        AddChild(_toggleButton);

        _searchBox = elementPoolService.CreateElement<TextBox>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = SearchBoxPosition(), Size = new Vector2(SearchBoxWidth, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Content = new ElementContentOptions { ContentColor = ControlColor },
            Text = new TextOptions { TextColor = LabelColor },
        });
        _searchBox.ContentFont = _font;
        _searchBox.GhostText = _searchGhostText;
        _searchBox.GhostTextColor = Color.LightGray;
        AddChild(_searchBox); // Already initializes _searchBox -- see Element.AddChild's own doc comment.

        Resized += OnSelfResized;

        UpdateCountLabelText(0);
    }

    /// <summary>Keeps the search box right-aligned to this control's own right edge as it resizes -- mirrors TabbedContent's own OnHostWindowResized repositioning its search box the same way. The left-aligned count/sort/toggle tiles need no equivalent -- their own widths/positions are fixed regardless of this control's overall width.</summary>
    private void OnSelfResized(Element _) => _searchBox.SetRelativePosition(SearchBoxPosition());

    private Vector2 SearchBoxPosition() => new(ContentSize.X - SearchBoxWidth, 0);

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_searchFilter.Update(_searchBox.OriginalText))
        {
            SearchFilterChanged?.Invoke(_searchFilter.AppliedText);
        }
    }

    /// <summary>Updates the count display in place -- fixed-width (see WidestCountText), so this never shifts the sort/toggle/search controls to its right.</summary>
    public void SetItemCount(int count) => UpdateCountLabelText(count);

    private void UpdateCountLabelText(int count) => _countLabel.UpdateText($"{count} items");

    private void CycleSortOption()
    {
        _sortOptionIndex = (_sortOptionIndex + 1) % _sortOptionLabels.Count;
        _sortButton.UpdateText(_sortOptionLabels[_sortOptionIndex]);
        SortOptionCycled?.Invoke(_sortOptionIndex);
    }

    private void ToggleOnOff()
    {
        _isToggleOn = !_isToggleOn;
        _toggleButton.BorderStyle = _isToggleOn ? BorderStyle.Outset : BorderStyle.Inset;
        ToggleChanged?.Invoke(_isToggleOn);
    }

    /// <summary>Widest of a set of labels, in the same units MeasureTileWidth returns -- used to size the sort button once for its widest possible label, so cycling through shorter/longer labels never resizes it (see CreateTile's own MinimumSize == MaximumSize pinning).</summary>
    private float MeasureWidest(IReadOnlyList<string> labels)
    {
        var widest = 0f;
        foreach (var label in labels)
        {
            widest = System.Math.Max(widest, MeasureTileWidth(label));
        }

        return widest;
    }

    private float MeasureTileWidth(string text) => _font.MeasureString(text).X + HorizontalPadding * 2;

    /// <summary>
    /// A plain TextWindow tile, the same shape TabbedContent's own tab tiles use -- MinimumSize
    /// == MaximumSize == its own Size pins it immune to any ambient resize/rearrange cascade
    /// (see TabbedContent's own doc comment on why that matters). relativePositionX is baked
    /// into the creation options directly (not set via a follow-up SetRelativePosition call) --
    /// the same order every other tile-creation site in this codebase already uses
    /// (TabbedContent's tab tiles, InventoryGridContent's cells).
    /// </summary>
    private TextWindow CreateTile(string text, float width, float relativePositionX, bool showBorder)
    {
        var tileSize = new Vector2(width, RowHeight);
        var tile = elementPoolService.CreateElement<TextWindow>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(relativePositionX, 0), Size = tileSize, MinimumSize = tileSize, MaximumSize = tileSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = showBorder, BorderStyle = BorderStyle.Inset, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = ControlColor },
            Text = new TextOptions { Text = text, TextColor = LabelColor },
        });
        tile.ContentFont = _font; // Must match the font width was measured with, or the label can wrap/clip against the tile's own fixed width.
        return tile;
    }
}
