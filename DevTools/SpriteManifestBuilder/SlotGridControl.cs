using System.ComponentModel;

namespace SpriteManifestBuilder;

/// <summary>
/// A linked set's slots laid out as its LinkLayout shows them -- a wall top's 16 as a 4x4 grid, a wall face's 4 in a row --
/// each labelled by a small diagram whose bars show the sides it joins and filled with its piece's thumbnail. A required slot
/// left empty says so. Picks one slot, or with MultiSelect ticks any number (Copy to...).
/// </summary>
public sealed class SlotGridControl : Control
{
    private const int SlotPixels = 64;
    private const int SlotGap = 4;
    private const int DiagramPixels = 18;

    private readonly SheetImageCache _sheets;
    private readonly ToolTip _toolTip = new();
    private LinkLayout _layout = LinkLayout.Cardinal;
    private ManifestCell?[] _slots = LinkLayout.Cardinal.NewVariantSet();
    private int _hoveredMask = -1;

    public SlotGridControl(SheetImageCache sheets, bool multiSelect)
    {
        _sheets = sheets;
        MultiSelect = multiSelect;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(40, 40, 40);
        SelectedMask = _layout.DefaultSelectedMask;
        ResizeToLayout();
    }

    public bool MultiSelect { get; }

    public int SelectedMask { get; private set; }

    public HashSet<int> CheckedMasks { get; } = [];

    /// <summary>The span every slot should hold; a filled slot that doesn't gets a warning icon in its bottom-right corner.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size ExpectedSpan { get; set; } = new(1, 1);

    public event EventHandler? SelectedMaskChanged;

    public void SetSlots(LinkLayout layout, ManifestCell?[] slots)
    {
        if (layout != _layout)
        {
            _layout = layout;
            ResizeToLayout();
        }

        _slots = slots;
        Invalidate();
    }

    public void SelectMask(int mask)
    {
        SelectedMask = mask;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var emptyBrush = new SolidBrush(Color.FromArgb(70, 70, 70));
        using var selectedPen = new Pen(Color.Lime, 3);
        using var checkedPen = new Pen(Color.Gold, 3);
        using var borderPen = new Pen(Color.FromArgb(110, 110, 110));

        for (var row = 0; row < _layout.DisplayLayout.GetLength(0); row++)
        {
            for (var column = 0; column < _layout.DisplayLayout.GetLength(1); column++)
            {
                var mask = _layout.DisplayLayout[row, column];
                var slotRectangle = SlotRectangle(row, column);
                var cell = _slots[mask];

                e.Graphics.FillRectangle(emptyBrush, slotRectangle);
                if (cell is not null)
                {
                    CellDrawing.Draw(e.Graphics, _sheets, cell, CellDrawing.Fit(cell.SourceRectangle.Size, slotRectangle));
                    if (!EntryRules.Fits(cell, ExpectedSpan))
                    {
                        WarningIcon.Draw(e.Graphics, slotRectangle.Right - WarningIcon.Pixels - 2, slotRectangle.Bottom - WarningIcon.Pixels - 2);
                    }
                }
                else
                {
                    var isRequired = mask == _layout.RequiredMask;
                    TextRenderer.DrawText(e.Graphics, isRequired ? "required" : "empty", Font, slotRectangle, isRequired ? Color.OrangeRed : Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
                }

                DrawSidesDiagram(e.Graphics, new Rectangle(slotRectangle.X + 2, slotRectangle.Y + 2, DiagramPixels, DiagramPixels), _layout.SidesOf(mask));

                var highlightPen = MultiSelect
                    ? CheckedMasks.Contains(mask) ? checkedPen : borderPen
                    : mask == SelectedMask ? selectedPen : borderPen;
                e.Graphics.DrawRectangle(highlightPen, slotRectangle);
            }
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (MaskAt(e.Location) is not { } mask)
        {
            return;
        }

        if (MultiSelect)
        {
            if (!CheckedMasks.Remove(mask))
            {
                CheckedMasks.Add(mask);
            }

            Invalidate();
            return;
        }

        SelectedMask = mask;
        Invalidate();
        SelectedMaskChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var mask = MaskAt(e.Location) ?? -1;
        if (mask == _hoveredMask)
        {
            return;
        }

        _hoveredMask = mask;
        _toolTip.SetToolTip(this, mask < 0 ? null : _layout.Describe(mask));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ResizeToLayout() =>
        Size = new Size(
            _layout.DisplayLayout.GetLength(1) * SlotPixels + (_layout.DisplayLayout.GetLength(1) + 1) * SlotGap,
            _layout.DisplayLayout.GetLength(0) * SlotPixels + (_layout.DisplayLayout.GetLength(0) + 1) * SlotGap);

    /// <summary>A small tile with a bar from its centre to each side the piece joins.</summary>
    private static void DrawSidesDiagram(Graphics graphics, Rectangle bounds, (bool North, bool East, bool South, bool West) sides)
    {
        using var backgroundBrush = new SolidBrush(Color.FromArgb(200, 20, 20, 20));
        using var barBrush = new SolidBrush(Color.Orange);
        using var outlinePen = new Pen(Color.FromArgb(160, 160, 160));

        graphics.FillRectangle(backgroundBrush, bounds);
        graphics.DrawRectangle(outlinePen, bounds);

        const int barThickness = 4;
        var centreX = bounds.X + (bounds.Width - barThickness) / 2;
        var centreY = bounds.Y + (bounds.Height - barThickness) / 2;
        graphics.FillRectangle(barBrush, centreX, centreY, barThickness, barThickness);

        if (sides.North)
        {
            graphics.FillRectangle(barBrush, centreX, bounds.Y, barThickness, centreY - bounds.Y);
        }
        if (sides.South)
        {
            graphics.FillRectangle(barBrush, centreX, centreY + barThickness, barThickness, bounds.Bottom - centreY - barThickness);
        }
        if (sides.West)
        {
            graphics.FillRectangle(barBrush, bounds.X, centreY, centreX - bounds.X, barThickness);
        }
        if (sides.East)
        {
            graphics.FillRectangle(barBrush, centreX + barThickness, centreY, bounds.Right - centreX - barThickness, barThickness);
        }
    }

    private static Rectangle SlotRectangle(int row, int column) =>
        new(SlotGap + column * (SlotPixels + SlotGap), SlotGap + row * (SlotPixels + SlotGap), SlotPixels, SlotPixels);

    private int? MaskAt(Point location)
    {
        for (var row = 0; row < _layout.DisplayLayout.GetLength(0); row++)
        {
            for (var column = 0; column < _layout.DisplayLayout.GetLength(1); column++)
            {
                if (SlotRectangle(row, column).Contains(location))
                {
                    return _layout.DisplayLayout[row, column];
                }
            }
        }

        return null;
    }
}
