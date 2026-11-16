using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.UI.Chrome;

namespace UIEngine.UI.Filtering;

/// <summary>A filter's checked choices as a wrapping row of chips, Clear last: a click on a chip unchecks its choice, on Clear every one.</summary>
/// <remarks>
/// <para>
/// A chip is one button, its label then an "x" at its end, tooltip "Remove filter"; there is no chip that stays shown
/// while switched off. The row follows the selection: rebuilt whenever it changes, and laid out within the width the
/// host gives it (SetWidth), wrapping onto as many lines as it needs. With nothing checked it has no height and is
/// hidden. Its height changes through SetSize, so the host hears of it through Resized (or, in a tiling parent, through
/// the parent's own layout) and gives the rest of its space to what is under the row.
/// </para>
/// <para>
/// A chip's click takes effect on the next Update, so no chip is closed inside its own click; any other change to the
/// selection lays the row out at once. Configure before Initialize, the usual contract; closing it lets go of the
/// selection.
/// </para>
/// </remarks>
public sealed class ListFilterChips(ElementServices services)
    : Element(services)
{
    public const float ChipHeight = 20f;
    public const float ChipGap = 4f;
    public const string RemoveMarker = "x";
    public const string ClearLabel = "Clear";
    public const string RemoveTooltip = "Remove filter";
    public const string ClearTooltip = "Remove every filter";

    private const float ChipTextPadding = 18f;
    private const float RemoveMarkerGap = 8f;
    private const int ClearAll = -1;
    private const int NoRemoval = -2;

    private readonly SpriteFontBase _font = services.FontService.GetFont(FontChrome.DefaultFontSize);

    private IListFilterMenuSource? _source;
    private float _width;
    private int _pendingRemoval = NoRemoval;
    private bool _isRebuildRequested;

    /// <summary>How many chips show, Clear not counted.</summary>
    public int ChipCount { get; private set; }

    public void Configure(IListFilterMenuSource source, float width)
    {
        Release();
        _source = source;
        _width = width;
        source.Selection.Changed += OnSelectionChanged;
        _isRebuildRequested = true;
    }

    /// <summary>Lays the chips out again within width; nothing when it is the width they have.</summary>
    public void SetWidth(float width)
    {
        if (width == _width)
        {
            return;
        }

        _width = width;
        Rebuild();
    }

    /// <summary>The height the chips need within width, each wrapped line ChipHeight tall with ChipGap between; 0 with nothing checked.</summary>
    public float MeasureHeight(float width)
    {
        if (_source is not { } source)
        {
            return 0f;
        }

        var lines = LineCountWithin(source, width);
        return lines == 0 ? 0f : lines * ChipHeight + (lines - 1) * ChipGap;
    }

    public override void Initialize()
    {
        base.Initialize();
        Rebuild();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_pendingRemoval != NoRemoval && _source is { } source)
        {
            var removal = _pendingRemoval;
            _pendingRemoval = NoRemoval;
            if (removal == ClearAll)
            {
                source.Selection.Clear();
            }
            else
            {
                source.Selection.SetChecked(removal, false);
            }
        }

        if (_isRebuildRequested)
        {
            Rebuild();
        }
    }

    protected internal override void OnClosed()
    {
        Release();
        _width = 0f;
        _pendingRemoval = NoRemoval;
        _isRebuildRequested = false;
        ChipCount = 0;
        base.OnClosed();
    }

    private void Release()
    {
        if (_source is { } source)
        {
            source.Selection.Changed -= OnSelectionChanged;
            _source = null;
        }
    }

    private void OnSelectionChanged(ListFilterSelection selection) => Rebuild();

    private void Rebuild()
    {
        _isRebuildRequested = false;
        ElementPoolService.CloseAllChildren(this);
        ChipCount = 0;
        if (_source is not { } source)
        {
            return;
        }

        var position = Vector2.Zero;
        var selection = source.Selection;
        for (var choiceIndex = 0; choiceIndex < selection.ChoiceCount; choiceIndex++)
        {
            if (selection.IsChecked(choiceIndex))
            {
                position = AddChip(source.ChoiceLabel(choiceIndex), RemoveMarker, RemoveTooltip, choiceIndex, position);
                ChipCount++;
            }
        }

        if (ChipCount > 0)
        {
            AddChip(ClearLabel, null, ClearTooltip, ClearAll, position);
        }

        var height = MeasureHeight(_width);
        IsVisible = height > 0f;
        SetSize(new Vector2(_width, height));
    }

    /// <summary>A chip at position, or at the start of the next line when it doesn't fit what is left of this one; returns where the next chip starts.</summary>
    private Vector2 AddChip(string label, string? marker, string tooltip, int removal, Vector2 position)
    {
        var size = new Vector2(ChipWidth(label, marker), ChipHeight);
        if (position.X > 0f && position.X + size.X > _width)
        {
            position = new Vector2(0f, position.Y + ChipHeight + ChipGap);
        }

        var chip = ElementPoolService.CreateElement<Button>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Text = new TextOptions { Text = label },
        });
        chip.ContentFont = _font;
        chip.RightText = marker;
        chip.LeftAlign = marker is not null;
        chip.TooltipText = tooltip;
        chip.Clicked += _ => _pendingRemoval = removal;
        AddChild(chip);
        return position + new Vector2(size.X + ChipGap, 0f);
    }

    private float ChipWidth(string label, string? marker) =>
        _font.MeasureString(label).X + (marker is null ? 0f : RemoveMarkerGap + _font.MeasureString(marker).X) + ChipTextPadding;

    private int LineCountWithin(IListFilterMenuSource source, float width)
    {
        var selection = source.Selection;
        if (selection.IsEmpty)
        {
            return 0;
        }

        var lines = 1;
        var x = 0f;
        for (var choiceIndex = 0; choiceIndex <= selection.ChoiceCount; choiceIndex++)
        {
            var isClear = choiceIndex == selection.ChoiceCount;
            if (!isClear && !selection.IsChecked(choiceIndex))
            {
                continue;
            }

            var chipWidth = isClear ? ChipWidth(ClearLabel, null) : ChipWidth(source.ChoiceLabel(choiceIndex), RemoveMarker);
            if (x > 0f && x + chipWidth > width)
            {
                lines++;
                x = 0f;
            }

            x += chipWidth + ChipGap;
        }

        return lines;
    }
}
