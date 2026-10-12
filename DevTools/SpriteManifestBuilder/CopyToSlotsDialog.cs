namespace SpriteManifestBuilder;

/// <summary>Ticks the slots a piece is copied into -- as it is, turned or mirrored -- for combinations of sides the pack has no piece for.</summary>
public sealed class CopyToSlotsDialog : Form
{
    private static readonly (string Label, Func<SpriteManifestCell, SpriteManifestCell> Apply, bool IsQuarterTurn, bool IsVertical)[] CopyTransforms =
    [
        ("As it is", static cell => cell, false, false),
        ("Turned 90° clockwise", static cell => cell.RotatedClockwise(), true, true),
        ("Turned 180°", static cell => cell.RotatedClockwise().RotatedClockwise(), false, true),
        ("Turned 90° anticlockwise", static cell => cell.RotatedCounterClockwise(), true, true),
        ("Mirrored left-right ↔", static cell => cell.MirroredHorizontally(), false, false),
        ("Mirrored top-bottom ↕", static cell => cell.MirroredVertically(), false, true),
    ];

    private readonly SlotGridControl _slotGrid;
    private readonly ComboBox _copyAsComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly SpriteManifestCell _piece;

    public CopyToSlotsDialog(SheetImageCache sheets, LinkLayout layout, SpriteManifestCell?[] slots, int sourceMask, Size expectedSpan)
    {
        _piece = slots[sourceMask] ?? throw new ArgumentException("The source slot is empty.", nameof(slots));

        Text = $"Copy the {layout.Describe(sourceMask)} piece to...";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _slotGrid = new SlotGridControl(sheets, multiSelect: true);
        _slotGrid.ExpectedSpan = expectedSpan;
        _slotGrid.SetSlots(layout, slots);

        foreach (var (label, _, isQuarterTurn, isVertical) in CopyTransforms)
        {
            if ((!isQuarterTurn || _piece.CanQuarterTurn && layout.TurnClockwise is not null) && (!isVertical || layout.MirrorTopBottom is not null))
            {
                _copyAsComboBox.Items.Add(label);
            }
        }

        _copyAsComboBox.SelectedIndex = 0;

        var copyAsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        copyAsRow.Controls.Add(new Label { Text = "Copy as:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        copyAsRow.Controls.Add(_copyAsComboBox);

        var okButton = new Button { Text = "Copy", DialogResult = DialogResult.OK, AutoSize = true };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var buttonRow = new FlowLayoutPanel { AutoSize = true };
        buttonRow.Controls.Add(okButton);
        buttonRow.Controls.Add(cancelButton);

        var layoutPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(8) };
        layoutPanel.Controls.Add(new Label { Text = "Click the slots to copy into, then Copy.", AutoSize = true });
        layoutPanel.Controls.Add(copyAsRow);
        layoutPanel.Controls.Add(_slotGrid);
        layoutPanel.Controls.Add(buttonRow);
        Controls.Add(layoutPanel);
        StandardControlBehaviors.Apply(this);
    }

    public IReadOnlyCollection<int> TargetMasks => _slotGrid.CheckedMasks;

    /// <summary>The piece as it lands in each target slot, with the chosen turn or mirror applied.</summary>
    public SpriteManifestCell CopiedPiece
    {
        get
        {
            var label = (string)_copyAsComboBox.SelectedItem!;
            return CopyTransforms.First(transform => transform.Label == label).Apply(_piece);
        }
    }
}
