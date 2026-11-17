using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI.Filtering;

/// <summary>A filter's checked choices as a wrapping row of chips, Clear last: a click on a chip unchecks its choice, on Clear every one.</summary>
/// <remarks>
/// <para>
/// A chip is one button holding the choice's icon if it has one, its label, then an "X" at its end, as wide as those need; its tooltip is the
/// choice's, else "Remove filter". There is no chip that stays shown
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
    public const string RemoveMarker = "X";
    public const string ClearLabel = "Clear";
    public const string RemoveTooltip = "Remove filter";
    public const string ClearTooltip = "Remove every filter";

    /// <summary>Room either side of a chip's content; a chip of one label (Clear) has more, so the label sits centred in a chip as wide as one with a marker would be around it.</summary>
    private const float ChipContentInset = 4f;

    private const float LoneLabelInset = 9f;

    /// <summary>A choice icon's box and the gap after it.</summary>
    private const float IconSide = 16f;

    private const float IconGap = 4f;

    /// <summary>Between a chip's label and its remove marker.</summary>
    private const float MarkerGap = 18f;
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
                position = AddChip(source.ChoiceLabel(choiceIndex), source.ChoiceIcon(choiceIndex), RemoveMarker, source.ChoiceTooltip(choiceIndex) ?? RemoveTooltip, choiceIndex, position);
                ChipCount++;
            }
        }

        if (ChipCount > 0)
        {
            position = AddChip(ClearLabel, null, null, ClearTooltip, ClearAll, position);
        }

        var height = ChipCount == 0 ? 0f : position.Y + ChipHeight;
        IsVisible = height > 0f;
        SetSize(new Vector2(_width, height));
    }

    /// <summary>A chip at position, or at the start of the next line when it doesn't fit what is left of this one; returns where the next chip starts.</summary>
    /// <remarks>A chip is a button as wide as what it holds -- the choice's icon, its label, then the remove marker -- and ChipHeight tall.</remarks>
    private Vector2 AddChip(string label, GlyphIcon? icon, string? marker, string tooltip, int removal, Vector2 position)
    {
        var chip = ElementPoolService.CreateElement<Button>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { ChildrenTileMode = ChildElementTileMode.Horizontal, ChildCrossAlignment = ChildCrossAlignment.Center },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0f, position.Y), DisplayMode = ElementDisplayMode.WrapContent },
        });
        chip.ContentPadding = new Vector2(marker is null ? LoneLabelInset : ChipContentInset, 0f);
        chip.TooltipText = tooltip;
        chip.Clicked += _ => _pendingRemoval = removal;
        AddChild(chip);

        if (icon is { } glyphIcon)
        {
            var iconElement = ElementPoolService.CreateElement<IconElement>(chip, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(IconSide, ChipHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true, SpacingAfter = IconGap },
            });
            iconElement.Configure(null, glyphIcon.Glyph, glyphIcon.Color, _font);
            chip.AddChild(iconElement);
        }

        AddChipLabel(chip, label, marker is null ? 0f : MarkerGap);
        if (marker is not null)
        {
            AddChipLabel(chip, marker, 0f);
        }

        var width = chip.CurrentSize.X;
        if (position.X > 0f && position.X + width > _width)
        {
            position = new Vector2(0f, position.Y + ChipHeight + ChipGap);
        }

        chip.SetRelativePosition(position);
        chip.SetFixedSize(new Vector2(width, ChipHeight));
        return position + new Vector2(width + ChipGap, 0f);
    }

    private void AddChipLabel(Button chip, string text, float spacingAfter)
    {
        var label = ElementPoolService.CreateElement<Label>(chip, new ElementOptions
        {
            Layout = new ElementLayoutOptions { SpacingAfter = spacingAfter },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.BodyTextColor },
        });
        label.SetFont(_font);
        chip.AddChild(label);
    }
}
