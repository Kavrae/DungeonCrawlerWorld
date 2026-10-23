using System.Drawing.Drawing2D;

namespace SpriteManifestBuilder;

/// <summary>
/// The whole SpriteManifestBuilder workflow: open a sheet, pick a cell size, view it divided into a grid, and build an entry
/// from it, then save it to Content/SpriteManifest.json. An entry is options the game picks between, a linked set (16 slots,
/// one per combination of joined sides), or a wall: a face (options) and a top (a linked set) selected separately, each with
/// its own span and placement, saved as one entry. A part's span is chosen up front: a click places a span-sized option (or
/// fills the selected slot) from the clicked cell, a drag places exactly the dragged cells, and a click inside an option
/// removes it. Cells that don't fit the span are marked, not refused, so unfinished work can be saved. Placement -- pixels
/// per tile and pivot -- is previewed against the reach box the game enforces. Hand-built (no .Designer.cs split) -- a small
/// internal tool, not worth the extra file for this much UI.
/// </summary>
public sealed class MainForm : Form
{
    private const int MaxDisplayPixels = 800;
    private const int MaxZoom = 16;
    private const int OptionsKindIndex = 0;
    private const int LinkedSetKindIndex = 1;
    private const int WallKindIndex = 2;
    private const int SectionWidth = 340;

    private readonly SheetImageCache _sheets = new();
    private readonly ToolTip _toolTip = new();
    private readonly OpenFileDialog _openFileDialog = new() { Filter = "PNG files (*.png)|*.png" };
    private readonly NumericUpDown _cellWidthUpDown = new() { Minimum = 1, Maximum = 512, Value = 16 };
    private readonly NumericUpDown _cellHeightUpDown = new() { Minimum = 1, Maximum = 512, Value = 16 };
    private readonly Label _sheetPathLabel = new() { AutoSize = true, Text = "No spritesheet loaded.", MaximumSize = new Size(SectionWidth, 0) };
    private readonly Label _warningLabel = new() { AutoSize = true, ForeColor = Color.DarkOrange, MaximumSize = new Size(SectionWidth, 0) };
    private readonly TextBox _nameTextBox = new() { Width = 300 };
    private readonly ComboBox _kindComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly RadioButton _faceRadioButton = new() { Text = "Face", Appearance = Appearance.Button, AutoSize = true, Checked = true };
    private readonly RadioButton _topRadioButton = new() { Text = "Top", Appearance = Appearance.Button, AutoSize = true };
    private readonly Control _wallPartRow;
    private readonly NumericUpDown _spanColumnsUpDown = new() { Minimum = 1, Maximum = 16, Value = 1, Width = 50 };
    private readonly NumericUpDown _spanRowsUpDown = new() { Minimum = 1, Maximum = 16, Value = 1, Width = 50 };
    private readonly Label _legendLabel = new() { AutoSize = true, MaximumSize = new Size(SectionWidth, 0) };
    private readonly ListBox _optionsListBox = new() { Height = 120, Width = SectionWidth, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 18 };
    private readonly ListBox _entriesListBox = new() { Height = 200, Width = SectionWidth, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 18 };
    private readonly PictureBox _pictureBox = new() { SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Black };
    private readonly Panel _pictureScrollPanel = new() { AutoScroll = true, BorderStyle = BorderStyle.Fixed3D };

    private readonly SlotGridControl _slotGrid;
    private readonly Label _selectedSlotLabel = new() { AutoSize = true, MaximumSize = new Size(SectionWidth, 0) };
    private readonly FlowLayoutPanel _variantSetList = new() { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
    private readonly List<(FlowLayoutPanel Container, RadioButton Choice, NumericUpDown Chance, Panel Swatch)> _variantSetRows = [];
    private readonly Label _variantChanceTotalLabel = new() { AutoSize = true };
    private readonly PictureBox _linkPreviewBox = new() { BackColor = Color.Black, Size = LinkPreviewRenderer.PreferredSize(15) };
    private readonly Label _linkPreviewNoteLabel = new() { AutoSize = true, ForeColor = Color.DarkOrange, MaximumSize = new Size(SectionWidth, 0) };

    private readonly CheckBox _stretchCheckBox = new() { Text = "Stretch to footprint", AutoSize = true, Checked = true };
    private readonly NumericUpDown _pixelsPerTileUpDown = new() { Minimum = 1, Maximum = 1024, Value = 16, Width = 70 };
    private readonly CheckBox _defaultPivotCheckBox = new() { Text = "Pivot at bottom-centre", AutoSize = true, Checked = true };
    private readonly NumericUpDown _pivotXUpDown = new() { Minimum = 0, Maximum = 4096, Width = 70 };
    private readonly NumericUpDown _pivotYUpDown = new() { Minimum = 0, Maximum = 4096, Width = 70 };
    private readonly CheckBox _setPivotToggle = new() { Text = "Set pivot", Appearance = Appearance.Button, AutoSize = true };
    private readonly ComboBox _footprintComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly Label _placementHeaderLabel = new() { AutoSize = true };
    private readonly PictureBox _placementPreviewBox = new() { BackColor = Color.Black, Size = new Size(350, 250) };
    private readonly Label _placementStatusLabel = new() { AutoSize = true, MaximumSize = new Size(350, 0) };

    private readonly Control _optionsSection;
    private readonly Control _linkedSetSection;
    private readonly Control _linkPreviewSection;

    private readonly EntryPart _mainPart = new(SpriteLinkLayout.Cardinal);
    private readonly EntryPart _facePart = new(SpriteLinkLayout.Horizontal);
    private readonly EntryPart _topPart = new(SpriteLinkLayout.Cardinal);
    private List<SpriteManifestEntry> _allEntries;

    private Image? _sheetImage;
    private string? _currentRelativeSheetPath;
    private int _zoom = 1;
    private Point? _dragStartCell;
    private Point _dragCurrentCell;
    private bool _isSyncingControls;

    public MainForm()
    {
        Text = "Sprite Manifest Builder";
        Width = 1500;
        Height = 900;

        _allEntries = SpriteManifest.ReadEntries(RepoPaths.ManifestFilePath);
        _slotGrid = new SlotGridControl(_sheets, multiSelect: false);
        _wallPartRow = BuildWallPartRow();
        _optionsSection = BuildOptionsSection();
        _linkedSetSection = BuildLinkedSetSection();
        _linkPreviewSection = BuildLinkPreviewSection();

        BuildLayout();
        StandardControlBehaviors.Apply(this);
        RefreshEntriesListBox();
        LoadActivePartIntoControls();
    }

    private bool IsWall => _kindComboBox.SelectedIndex == WallKindIndex;

    private bool IsEditingTop => IsWall && _topRadioButton.Checked;

    /// <summary>The part the sheet, the slot grid, the span, the placement panel and the transform buttons act on.</summary>
    private EntryPart ActivePart => IsWall ? IsEditingTop ? _topPart : _facePart : _mainPart;

    private bool IsMainPartLinked => _kindComboBox.SelectedIndex == LinkedSetKindIndex;

    private bool IsActivePartLinked => IsWall || IsMainPartLinked;

    /// <summary>The part whose slots the link preview draws joined: a wall's top (its face drawn under it), or the entry itself.</summary>
    private EntryPart LinkPreviewPart => IsWall ? _topPart : _mainPart;

    private Size SelectedSpan => new((int)_spanColumnsUpDown.Value, (int)_spanRowsUpDown.Value);

    private int FootprintTiles => _footprintComboBox.SelectedIndex + 1;

    private int FaceHeightRows => _facePart.ToManifestPart(linked: true).HeightRows();

    private int ActiveRaisedRows => IsEditingTop ? FaceHeightRows : 0;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sheets.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var openButton = new Button { Text = "Open Spritesheet...", AutoSize = true };
        openButton.Click += (_, _) => OpenSpritesheet();

        var resolutionPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        resolutionPanel.Controls.Add(new Label { Text = "Cell width:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        resolutionPanel.Controls.Add(_cellWidthUpDown);
        resolutionPanel.Controls.Add(new Label { Text = "Cell height:", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        resolutionPanel.Controls.Add(_cellHeightUpDown);
        _cellWidthUpDown.ValueChanged += (_, _) => _pictureBox.Invalidate();
        _cellHeightUpDown.ValueChanged += (_, _) => _pictureBox.Invalidate();

        _pictureBox.Paint += PictureBox_Paint;
        _pictureBox.MouseDown += PictureBox_MouseDown;
        _pictureBox.MouseMove += PictureBox_MouseMove;
        _pictureBox.MouseUp += PictureBox_MouseUp;
        _pictureScrollPanel.Controls.Add(_pictureBox);

        _kindComboBox.Items.AddRange(["Options", "Linked set", "Wall (face + top)"]);
        _kindComboBox.SelectedIndex = OptionsKindIndex;
        _kindComboBox.SelectedIndexChanged += (_, _) => OnKindChanged();

        var kindRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        kindRow.Controls.Add(new Label { Text = "Kind:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        kindRow.Controls.Add(_kindComboBox);

        _spanColumnsUpDown.ValueChanged += (_, _) => OnSpanChanged();
        _spanRowsUpDown.ValueChanged += (_, _) => OnSpanChanged();
        var spanRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        spanRow.Controls.Add(new Label { Text = "Span (cells):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        spanRow.Controls.Add(_spanColumnsUpDown);
        spanRow.Controls.Add(new Label { Text = "x", AutoSize = true, Padding = new Padding(2, 6, 2, 0) });
        spanRow.Controls.Add(_spanRowsUpDown);

        var saveButton = new Button { Text = "Save", AutoSize = true };
        saveButton.Click += (_, _) => Save();

        var newButton = new Button { Text = "New", AutoSize = true };
        newButton.Click += (_, _) => ClearWorkingEntry();

        var deleteButton = new Button { Text = "Delete selected...", AutoSize = true };
        deleteButton.Click += (_, _) => DeleteSelectedEntries();

        _entriesListBox.SelectedIndexChanged += (_, _) => LoadSelectedEntry();
        _entriesListBox.DrawItem += EntriesListBox_DrawItem;
        _entriesListBox.KeyDown += (_, e) => OnDeleteKey(e, DeleteSelectedEntries);

        var sidePanel = NewColumn();
        sidePanel.Controls.Add(openButton);
        sidePanel.Controls.Add(_sheetPathLabel);
        sidePanel.Controls.Add(resolutionPanel);
        sidePanel.Controls.Add(NewSeparator());
        sidePanel.Controls.Add(new Label { Text = "Name:", AutoSize = true });
        sidePanel.Controls.Add(_nameTextBox);
        sidePanel.Controls.Add(kindRow);
        sidePanel.Controls.Add(_wallPartRow);
        sidePanel.Controls.Add(spanRow);
        sidePanel.Controls.Add(_legendLabel);
        sidePanel.Controls.Add(_warningLabel);
        sidePanel.Controls.Add(NewSeparator());
        sidePanel.Controls.Add(_optionsSection);
        sidePanel.Controls.Add(_linkedSetSection);
        sidePanel.Controls.Add(BuildTransformRow());
        sidePanel.Controls.Add(NewSeparator());
        sidePanel.Controls.Add(new Label { Text = "Existing entries (click to edit; Shift/Ctrl+click to select several, Delete to delete):", AutoSize = true, MaximumSize = new Size(SectionWidth, 0) });
        sidePanel.Controls.Add(_entriesListBox);
        var buttonRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        buttonRow.Controls.Add(saveButton);
        buttonRow.Controls.Add(newButton);
        buttonRow.Controls.Add(deleteButton);
        sidePanel.Controls.Add(buttonRow);

        var sidePanelHost = new Panel { Dock = DockStyle.Left, Width = 390, Padding = new Padding(8), AutoScroll = true };
        sidePanelHost.Controls.Add(sidePanel);

        var placementPanelHost = new Panel { Dock = DockStyle.Right, Width = 390, Padding = new Padding(8), AutoScroll = true };
        var rightColumn = NewColumn();
        rightColumn.Controls.Add(BuildPlacementSection());
        rightColumn.Controls.Add(_linkPreviewSection);
        placementPanelHost.Controls.Add(rightColumn);

        Controls.Add(_pictureScrollPanel);
        Controls.Add(placementPanelHost);
        Controls.Add(sidePanelHost);
        _pictureScrollPanel.Dock = DockStyle.Fill;
    }

    private Control BuildWallPartRow()
    {
        _faceRadioButton.CheckedChanged += (_, _) => OnWallPartChanged();
        _toolTip.SetToolTip(_faceRadioButton, "The wall's face: standalone (required), left end, right end and middle pieces, drawn on the wall's own tile wherever nothing walls it in from the south. An empty end or middle falls back to the standalone piece.");
        _toolTip.SetToolTip(_topRadioButton, "The wall's top: a linked set drawn on the tile above the face, on every wall tile.");

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = "Editing:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        row.Controls.Add(_faceRadioButton);
        row.Controls.Add(_topRadioButton);
        return row;
    }

    private Control BuildOptionsSection()
    {
        var removeOptionButton = new Button { Text = "Remove selected", AutoSize = true };
        removeOptionButton.Click += (_, _) => RemoveSelectedOptions();
        _optionsListBox.SelectedIndexChanged += (_, _) => RefreshPlacement();
        _optionsListBox.DrawItem += OptionsListBox_DrawItem;
        _optionsListBox.KeyDown += (_, e) => OnDeleteKey(e, RemoveSelectedOptions);

        var section = NewColumn();
        section.Controls.Add(new Label { Text = "Options (the game picks one at random; Shift/Ctrl+click to select several, Delete to remove):", AutoSize = true, MaximumSize = new Size(SectionWidth, 0) });
        section.Controls.Add(_optionsListBox);
        section.Controls.Add(removeOptionButton);
        return section;
    }

    private Control BuildLinkedSetSection()
    {
        _slotGrid.SelectedMaskChanged += (_, _) =>
        {
            ActivePart.SelectedMask = _slotGrid.SelectedMask;
            RefreshAll();
        };

        var addVariantSetButton = new Button { Text = "Add variant set", AutoSize = true };
        addVariantSetButton.Click += (_, _) => AddVariantSet();
        var removeVariantSetButton = new Button { Text = "Remove this set", AutoSize = true };
        removeVariantSetButton.Click += (_, _) => RemoveCurrentVariantSet();

        var variantSetRow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        var variantSetHeader = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        variantSetHeader.Controls.Add(new Label { Text = "Variant sets (chance of each):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        variantSetHeader.Controls.Add(addVariantSetButton);
        variantSetRow.Controls.Add(variantSetHeader);
        variantSetRow.Controls.Add(_variantSetList);
        variantSetRow.Controls.Add(_variantChanceTotalLabel);

        var copyToButton = new Button { Text = "Copy to...", AutoSize = true };
        copyToButton.Click += (_, _) => CopySelectedSlot();
        var fillButton = new Button { Text = "Fill empty...", AutoSize = true };
        fillButton.Click += (_, _) => FillEmptySlots();
        _toolTip.SetToolTip(fillButton, "Fill every empty slot by mirroring or turning a filled one.");
        var clearSlotButton = new Button { Text = "Clear slot", AutoSize = true };
        clearSlotButton.Click += (_, _) => ClearSelectedSlot();
        var slotButtonRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(SectionWidth + 10, 0) };
        slotButtonRow.Controls.Add(copyToButton);
        slotButtonRow.Controls.Add(fillButton);
        slotButtonRow.Controls.Add(clearSlotButton);
        slotButtonRow.Controls.Add(removeVariantSetButton);

        var section = NewColumn();
        section.Controls.Add(new Label { Text = "Slots (pick one, then click or drag on the sheet to fill it):", AutoSize = true, MaximumSize = new Size(SectionWidth, 0) });
        section.Controls.Add(variantSetRow);
        section.Controls.Add(_slotGrid);
        section.Controls.Add(_selectedSlotLabel);
        section.Controls.Add(slotButtonRow);
        return section;
    }

    private Control BuildLinkPreviewSection()
    {
        _linkPreviewBox.Paint += (_, e) => LinkPreviewRenderer.Paint(
            e.Graphics,
            _linkPreviewBox.ClientSize,
            LinkPreviewPart.CurrentVariantSet,
            LinkPreviewPart.PixelsPerTile,
            LinkPreviewPart.PivotFor,
            IsWall ? new WallFacePreview(_facePart.CurrentVariantSet, _facePart.PixelsPerTile, _facePart.PivotFor, FaceHeightRows) : null,
            _sheets);

        var section = NewColumn();
        section.Controls.Add(NewSeparator());
        section.Controls.Add(new Label { Text = "Link preview:", AutoSize = true });
        section.Controls.Add(_linkPreviewBox);
        section.Controls.Add(_linkPreviewNoteLabel);
        return section;
    }

    private Control BuildPlacementSection()
    {
        _stretchCheckBox.CheckedChanged += (_, _) => OnStretchChanged();
        _pixelsPerTileUpDown.ValueChanged += (_, _) => OnPlacementControlChanged();
        _defaultPivotCheckBox.CheckedChanged += (_, _) => OnPlacementControlChanged();
        _pivotXUpDown.ValueChanged += (_, _) => OnPlacementControlChanged();
        _pivotYUpDown.ValueChanged += (_, _) => OnPlacementControlChanged();
        _footprintComboBox.Items.AddRange(["1x1", "2x2", "3x3"]);
        _footprintComboBox.SelectedIndex = 0;
        _footprintComboBox.SelectedIndexChanged += (_, _) => RefreshPlacement();
        _placementPreviewBox.Paint += PlacementPreview_Paint;
        _placementPreviewBox.MouseClick += PlacementPreview_MouseClick;

        var pixelsPerTileRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        pixelsPerTileRow.Controls.Add(new Label { Text = "Pixels per tile:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        pixelsPerTileRow.Controls.Add(_pixelsPerTileUpDown);
        pixelsPerTileRow.Controls.Add(_stretchCheckBox);

        var pivotRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        pivotRow.Controls.Add(new Label { Text = "Pivot X:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        pivotRow.Controls.Add(_pivotXUpDown);
        pivotRow.Controls.Add(new Label { Text = "Y:", AutoSize = true, Padding = new Padding(4, 6, 4, 0) });
        pivotRow.Controls.Add(_pivotYUpDown);
        pivotRow.Controls.Add(_setPivotToggle);

        var footprintRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        footprintRow.Controls.Add(new Label { Text = "Preview footprint:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        footprintRow.Controls.Add(_footprintComboBox);

        _placementHeaderLabel.Font = new Font(Font, FontStyle.Bold);

        var section = NewColumn();
        section.Controls.Add(_placementHeaderLabel);
        section.Controls.Add(pixelsPerTileRow);
        section.Controls.Add(_defaultPivotCheckBox);
        section.Controls.Add(pivotRow);
        section.Controls.Add(NewSeparator());
        section.Controls.Add(footprintRow);
        section.Controls.Add(new Label
        {
            Text = "Shaded: footprint. Dashed: reach box (2 rows above, 2 columns either side). Cyan: pivot. A wall's top is shown on its face, drawn faintly. With Set pivot on, click the sprite to put the pivot there.",
            AutoSize = true,
            MaximumSize = new Size(350, 0),
        });
        section.Controls.Add(_placementPreviewBox);
        section.Controls.Add(_placementStatusLabel);
        return section;
    }

    private Control BuildTransformRow()
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(SectionWidth + 10, 0) };
        row.Controls.Add(new Label { Text = "Turn, flip or tint the selected one:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        AddTransformButton(row, "⟲ 90°", "Turn a quarter anticlockwise", static cell => cell.RotatedCounterClockwise(), isQuarterTurn: true);
        AddTransformButton(row, "⟳ 90°", "Turn a quarter clockwise", static cell => cell.RotatedClockwise(), isQuarterTurn: true);
        AddTransformButton(row, "Flip ↔", "Mirror left to right", static cell => cell.MirroredHorizontally(), isQuarterTurn: false);
        AddTransformButton(row, "Flip ↕", "Mirror top to bottom", static cell => cell.MirroredVertically(), isQuarterTurn: false);
        AddTransformButton(row, "Reset", "Undo every turn and flip", static cell => cell.WithoutTransform(), isQuarterTurn: false);

        var tintButton = new Button { Text = "Tint...", AutoSize = true };
        _toolTip.SetToolTip(tintButton, "Tint the selected options, or every piece of the current variant set");
        tintButton.Click += (_, _) => ChooseTint();
        row.Controls.Add(tintButton);

        var clearTintButton = new Button { Text = "Clear tint", AutoSize = true };
        clearTintButton.Click += (_, _) => ApplyTint(null);
        row.Controls.Add(clearTintButton);
        return row;
    }

    private void AddTransformButton(FlowLayoutPanel row, string text, string toolTip, Func<SpriteManifestCell, SpriteManifestCell> transform, bool isQuarterTurn)
    {
        var button = new Button { Text = text, AutoSize = true };
        _toolTip.SetToolTip(button, toolTip);
        button.Click += (_, _) => TransformSelected(transform, isQuarterTurn);
        row.Controls.Add(button);
    }

    private static TableLayoutPanel NewColumn() => new() { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };

    private static Label NewSeparator() => new() { AutoSize = false, Height = 2, Width = SectionWidth, BorderStyle = BorderStyle.Fixed3D, Margin = new Padding(0, 10, 0, 10) };

    private void OpenSpritesheet()
    {
        _openFileDialog.InitialDirectory = RepoPaths.SpritesheetsRoot;
        if (_openFileDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(RepoPaths.SpritesheetsRoot, _openFileDialog.FileName).Replace('\\', '/');
        LoadSheet(relativePath);
    }

    /// <summary>Shared by OpenSpritesheet (user-picked file) and LoadSelectedEntry (a path from saved manifest data, which may name a missing file).</summary>
    private void LoadSheet(string relativePath)
    {
        if (_sheets.Get(relativePath) is not { } image)
        {
            MessageBox.Show(this, $"Could not load '{relativePath}'.", "Sheet unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _sheetImage = image;
        _currentRelativeSheetPath = relativePath;
        _sheetPathLabel.Text = _currentRelativeSheetPath;

        _zoom = Math.Max(1, Math.Min(MaxZoom, MaxDisplayPixels / Math.Max(_sheetImage.Width, _sheetImage.Height)));
        _pictureBox.Size = new Size(_sheetImage.Width * _zoom, _sheetImage.Height * _zoom);
        _pictureBox.Invalidate();
    }

    private void PictureBox_Paint(object? sender, PaintEventArgs e)
    {
        if (_sheetImage is null)
        {
            return;
        }

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_sheetImage, new Rectangle(0, 0, _sheetImage.Width * _zoom, _sheetImage.Height * _zoom));
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Default;

        var cellWidth = (int)_cellWidthUpDown.Value;
        var cellHeight = (int)_cellHeightUpDown.Value;

        using var gridPen = new Pen(Color.FromArgb(200, 255, 0, 255));
        for (var x = 0; x <= _sheetImage.Width; x += cellWidth)
        {
            e.Graphics.DrawLine(gridPen, x * _zoom, 0, x * _zoom, _sheetImage.Height * _zoom);
        }
        for (var y = 0; y <= _sheetImage.Height; y += cellHeight)
        {
            e.Graphics.DrawLine(gridPen, 0, y * _zoom, _sheetImage.Width * _zoom, y * _zoom);
        }

        if (IsActivePartLinked)
        {
            PaintLinkedSlotsOnSheet(e.Graphics);
        }
        else
        {
            PaintOptionsOnSheet(e.Graphics);
        }

        if (_dragStartCell is not null && PendingCell() is { } pending)
        {
            using var dragPen = new Pen(Color.Yellow, 2) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(dragPen, ToDisplay(pending.SourceRectangle.ToDrawingRectangle()));
        }
    }

    private void PaintOptionsOnSheet(Graphics graphics)
    {
        using var optionPen = new Pen(Color.Lime, 2);
        using var labelBackground = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        var options = ActivePart.Options;
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (option.SheetPath != _currentRelativeSheetPath)
            {
                continue;
            }

            var displayRectangle = ToDisplay(option.SourceRectangle.ToDrawingRectangle());
            graphics.DrawRectangle(optionPen, displayRectangle);

            var label = $"#{index + 1}";
            var labelSize = TextRenderer.MeasureText(graphics, label, Font);
            var labelRectangle = new Rectangle(displayRectangle.X + 2, displayRectangle.Y + 2, labelSize.Width, labelSize.Height);
            graphics.FillRectangle(labelBackground, labelRectangle);
            TextRenderer.DrawText(graphics, label, Font, labelRectangle, Color.Lime);
        }
    }

    private void PaintLinkedSlotsOnSheet(Graphics graphics)
    {
        using var filledPen = new Pen(Color.FromArgb(160, 0, 200, 255), 1);
        using var selectedPen = new Pen(Color.Lime, 3);
        var part = ActivePart;
        var variantSet = part.CurrentVariantSet;
        for (var mask = 0; mask < part.Layout.SlotCount; mask++)
        {
            if (variantSet[mask] is { } cell && cell.SheetPath == _currentRelativeSheetPath && mask != part.SelectedMask)
            {
                graphics.DrawRectangle(filledPen, ToDisplay(cell.SourceRectangle.ToDrawingRectangle()));
            }
        }

        if (variantSet[part.SelectedMask] is { } selected && selected.SheetPath == _currentRelativeSheetPath)
        {
            graphics.DrawRectangle(selectedPen, ToDisplay(selected.SourceRectangle.ToDrawingRectangle()));
        }
    }

    private Rectangle ToDisplay(Rectangle sourceRectangle) =>
        new(sourceRectangle.X * _zoom, sourceRectangle.Y * _zoom, sourceRectangle.Width * _zoom, sourceRectangle.Height * _zoom);

    private Point? CellAt(Point displayLocation, bool clampToSheet)
    {
        if (_sheetImage is null)
        {
            return null;
        }

        var cellWidth = (int)_cellWidthUpDown.Value;
        var cellHeight = (int)_cellHeightUpDown.Value;
        var column = displayLocation.X / (cellWidth * _zoom);
        var row = displayLocation.Y / (cellHeight * _zoom);
        var maxColumn = (_sheetImage.Width - 1) / cellWidth;
        var maxRow = (_sheetImage.Height - 1) / cellHeight;

        if (clampToSheet)
        {
            return new Point(Math.Clamp(column, 0, maxColumn), Math.Clamp(row, 0, maxRow));
        }

        return displayLocation.X < 0 || displayLocation.Y < 0 || column > maxColumn || row > maxRow ? null : new Point(column, row);
    }

    /// <summary>What releasing now would place: a span-sized cell from the pressed cell for a click, exactly the dragged cells for a drag. Null when a click's span would run off the sheet.</summary>
    private SpriteManifestCell? PendingCell()
    {
        if (_dragStartCell is not { } dragStartCell || _currentRelativeSheetPath is null || _sheetImage is null)
        {
            return null;
        }

        var cellWidth = (int)_cellWidthUpDown.Value;
        var cellHeight = (int)_cellHeightUpDown.Value;
        return dragStartCell == _dragCurrentCell
            ? EntryRules.SpanningFrom(_currentRelativeSheetPath, dragStartCell, SelectedSpan, cellWidth, cellHeight, _sheetImage.Size)
            : SpriteManifestCell.Spanning(_currentRelativeSheetPath, dragStartCell.X, dragStartCell.Y, _dragCurrentCell.X, _dragCurrentCell.Y, cellWidth, cellHeight);
    }

    private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _currentRelativeSheetPath is null || CellAt(e.Location, clampToSheet: false) is not { } cell)
        {
            return;
        }

        _dragStartCell = cell;
        _dragCurrentCell = cell;
        _pictureBox.Invalidate();
    }

    private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragStartCell is null || CellAt(e.Location, clampToSheet: true) is not { } cell || cell == _dragCurrentCell)
        {
            return;
        }

        _dragCurrentCell = cell;
        _pictureBox.Invalidate();
    }

    /// <summary>A press and release on one grid cell is a click; anything wider is a drag, which only ever adds. Either goes to the part being edited.</summary>
    private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
    {
        if (_dragStartCell is not { } dragStartCell)
        {
            return;
        }

        var isClick = dragStartCell == _dragCurrentCell;
        var pending = PendingCell();
        _dragStartCell = null;
        var part = ActivePart;

        if (!IsActivePartLinked && isClick && EntryRules.FindOptionAt(part.Options, _currentRelativeSheetPath!, new Point(e.X / _zoom, e.Y / _zoom)) is var existingIndex and >= 0)
        {
            part.Options.RemoveAt(existingIndex);
            _warningLabel.Text = "";
        }
        else if (pending is null)
        {
            _warningLabel.Text = $"A {SpanText(SelectedSpan)} span from that cell runs off the sheet.";
        }
        else if (IsActivePartLinked)
        {
            part.CurrentVariantSet[part.SelectedMask] = part.WithCurrentSetTint(pending);
            ShowAlreadyUsedWarningIfAny(pending);
        }
        else
        {
            AddOption(part, pending);
        }

        RefreshAll();
    }

    private void AddOption(EntryPart part, SpriteManifestCell option)
    {
        if (EntryRules.FindIdenticalOption(part.Options, option) is var existingIndex and >= 0)
        {
            _warningLabel.Text = $"That exact option is already #{existingIndex + 1}; adding it again would double its odds.";
            return;
        }

        part.Options.Add(option);
        _optionsListBox.ClearSelected();
        ShowAlreadyUsedWarningIfAny(option);
    }

    /// <summary>Checked against the manifest as loaded at startup (refreshed after each Save) -- informational only: the same pixels may be used by more than one entry.</summary>
    private void ShowAlreadyUsedWarningIfAny(SpriteManifestCell cell)
    {
        var usedByNames = _allEntries
            .Where(entry => entry.Name != _nameTextBox.Text.Trim() && entry.Cells.Concat(entry.Top?.Cells ?? []).Any(existing =>
                existing is not null && existing.SheetPath == cell.SheetPath && existing.SourceRectangle.ToDrawingRectangle().IntersectsWith(cell.SourceRectangle.ToDrawingRectangle())))
            .Select(entry => entry.Name)
            .ToList();

        _warningLabel.Text = usedByNames.Count > 0
            ? $"Also used by: {string.Join(", ", usedByNames)}"
            : "";
    }

    /// <summary>Indices of the options selected in the list, ascending, that are still in the part being edited.</summary>
    private List<int> SelectedOptionIndices() =>
        [.. _optionsListBox.SelectedIndices.Cast<int>().Where(index => index < ActivePart.Options.Count).Order()];

    private void RemoveSelectedOptions()
    {
        var selected = SelectedOptionIndices();
        if (selected.Count == 0)
        {
            return;
        }

        for (var position = selected.Count - 1; position >= 0; position--)
        {
            ActivePart.Options.RemoveAt(selected[position]);
        }

        _optionsListBox.ClearSelected();
        RefreshAll();
    }

    /// <summary>Runs action for the Delete key, and swallows the key so the list doesn't also act on it.</summary>
    private static void OnDeleteKey(KeyEventArgs e, Action action)
    {
        if (e.KeyCode != Keys.Delete || e.Modifiers != Keys.None)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        action();
    }

    private void OnKindChanged()
    {
        if (_isSyncingControls)
        {
            return;
        }

        if (IsWall)
        {
            SyncControls(() => _faceRadioButton.Checked = true);
        }

        _warningLabel.Text = "";
        LoadActivePartIntoControls();
    }

    private void OnWallPartChanged()
    {
        if (_isSyncingControls)
        {
            return;
        }

        _warningLabel.Text = "";
        SyncControls(() => _setPivotToggle.Checked = false);
        LoadActivePartIntoControls();
    }

    private void OnSpanChanged()
    {
        if (_isSyncingControls)
        {
            return;
        }

        ActivePart.Span = SelectedSpan;
        RefreshAll();
    }

    private void OnStretchChanged()
    {
        if (_isSyncingControls)
        {
            return;
        }

        if (!_stretchCheckBox.Checked && SelectedCellForPlacement() is { } cell)
        {
            SyncControls(() => _pixelsPerTileUpDown.Value = Math.Clamp(cell.CellWidth, (int)_pixelsPerTileUpDown.Minimum, (int)_pixelsPerTileUpDown.Maximum));
        }

        OnPlacementControlChanged();
    }

    /// <summary>Copies the placement controls into the part being edited.</summary>
    private void OnPlacementControlChanged()
    {
        if (_isSyncingControls)
        {
            return;
        }

        var part = ActivePart;
        part.PixelsPerTile = _stretchCheckBox.Checked ? null : (int)_pixelsPerTileUpDown.Value;
        var hasExplicitPivot = !_stretchCheckBox.Checked && !_defaultPivotCheckBox.Checked;
        part.PivotX = hasExplicitPivot ? (int)_pivotXUpDown.Value : null;
        part.PivotY = hasExplicitPivot ? (int)_pivotYUpDown.Value : null;
        RefreshAll();
    }

    private void SyncControls(Action update)
    {
        var wasSyncing = _isSyncingControls;
        _isSyncingControls = true;
        try
        {
            update();
        }
        finally
        {
            _isSyncingControls = wasSyncing;
        }
    }

    /// <summary>Shows the part being edited -- its span, placement, link group and variant set -- in the shared controls.</summary>
    private void LoadActivePartIntoControls()
    {
        var part = ActivePart;
        SyncControls(() =>
        {
            _spanColumnsUpDown.Value = Math.Clamp(part.Span.Width, (int)_spanColumnsUpDown.Minimum, (int)_spanColumnsUpDown.Maximum);
            _spanRowsUpDown.Value = Math.Clamp(part.Span.Height, (int)_spanRowsUpDown.Minimum, (int)_spanRowsUpDown.Maximum);
            _stretchCheckBox.Checked = part.PixelsPerTile is null;
            if (part.PixelsPerTile is { } pixelsPerTile)
            {
                _pixelsPerTileUpDown.Value = Math.Clamp(pixelsPerTile, (int)_pixelsPerTileUpDown.Minimum, (int)_pixelsPerTileUpDown.Maximum);
            }

            _defaultPivotCheckBox.Checked = !part.HasExplicitPivot;
            if (part.HasExplicitPivot)
            {
                _pivotXUpDown.Value = Math.Clamp(part.PivotX ?? 0, 0, (int)_pivotXUpDown.Maximum);
                _pivotYUpDown.Value = Math.Clamp(part.PivotY ?? 0, 0, (int)_pivotYUpDown.Maximum);
            }

            _optionsListBox.ClearSelected();
        });
        SyncVariantSetControls();

        RefreshAll();
    }

    private void SyncVariantSetControls()
    {
        var linkedPart = ActivePart;
        SyncControls(() =>
        {
            RebuildVariantSetList(linkedPart);
            _slotGrid.SelectMask(linkedPart.SelectedMask);
        });
    }

    /// <summary>One row per variant set of part: a choice to edit it, its chance in percent, and its tint swatch.</summary>
    private void RebuildVariantSetList(EntryPart part)
    {
        _variantSetList.SuspendLayout();
        foreach (var row in _variantSetRows)
        {
            row.Container.Dispose();
        }

        _variantSetRows.Clear();
        _variantSetList.Controls.Clear();

        for (var setIndex = 0; setIndex < part.VariantSets.Count; setIndex++)
        {
            var index = setIndex;
            var choice = new RadioButton { Text = $"Set {index + 1}", AutoSize = true, Checked = index == part.CurrentVariantSetIndex, Margin = new Padding(0, 4, 4, 0) };
            choice.CheckedChanged += (_, _) =>
            {
                if (!_isSyncingControls && choice.Checked)
                {
                    ActivePart.CurrentVariantSetIndex = index;
                    RefreshAll();
                }
            };

            var chance = new NumericUpDown { Minimum = 0, Maximum = 100, Value = Math.Clamp(part.VariantWeights[index], 0, 100), Width = 55 };
            chance.ValueChanged += (_, _) =>
            {
                if (!_isSyncingControls)
                {
                    ActivePart.VariantWeights[index] = (int)chance.Value;
                    ShowVariantChanceTotal(ActivePart);
                }
            };

            var swatch = new Panel { Size = new Size(18, 18), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 5, 4, 0) };

            var container = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            container.Controls.Add(choice);
            container.Controls.Add(chance);
            container.Controls.Add(new Label { Text = "%", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            container.Controls.Add(swatch);
            _variantSetList.Controls.Add(container);
            _variantSetRows.Add((container, choice, chance, swatch));
        }

        _variantSetList.ResumeLayout();
        ShowVariantSetRows(part);
    }

    /// <summary>Shows part's current set, chances and tints in the existing rows, without rebuilding them.</summary>
    private void ShowVariantSetRows(EntryPart part)
    {
        if (_variantSetRows.Count != part.VariantSets.Count)
        {
            SyncControls(() => RebuildVariantSetList(part));
            return;
        }

        SyncControls(() =>
        {
            for (var index = 0; index < _variantSetRows.Count; index++)
            {
                var (_, choice, chance, swatch) = _variantSetRows[index];
                choice.Checked = index == Math.Clamp(part.CurrentVariantSetIndex, 0, part.VariantSets.Count - 1);
                chance.Value = Math.Clamp(part.VariantWeights[index], 0, 100);
                ShowTintSwatch(swatch, part.VariantSetTints[index]);
            }
        });

        ShowVariantChanceTotal(part);
    }

    private void ShowVariantChanceTotal(EntryPart part)
    {
        var total = part.VariantWeightsTotal;
        _variantChanceTotalLabel.Text = total == 100 ? "Total: 100%" : $"Total: {total}% -- must be 100% to save";
        _variantChanceTotalLabel.ForeColor = total == 100 ? SystemColors.ControlText : Color.Red;
    }

    private void AddVariantSet()
    {
        ActivePart.AddVariantSet();
        SyncVariantSetControls();
        RefreshAll();
    }

    private void RemoveCurrentVariantSet()
    {
        ActivePart.RemoveCurrentVariantSet();
        SyncVariantSetControls();
        RefreshAll();
    }

    /// <summary>Asks for a colour and tints the selected options (each option, all its cells alike) or the current variant set (every piece in it, now and later).</summary>
    private void ChooseTint()
    {
        var part = ActivePart;
        var current = IsActivePartLinked
            ? part.CurrentSetTint
            : SelectedOptionIndices() is [var firstSelected, ..] ? part.Options[firstSelected].Tint : null;

        using var colorDialog = new ColorDialog { FullOpen = true, AnyColor = true };
        if (current is { } currentText && SpriteTint.TryParse(currentText, out var currentColor))
        {
            colorDialog.Color = currentColor.ToDrawingColor();
        }

        if (!IsActivePartLinked && SelectedOptionIndices().Count == 0)
        {
            _warningLabel.Text = "Select the options to tint in the list first.";
            return;
        }

        if (colorDialog.ShowDialog(this) == DialogResult.OK)
        {
            ApplyTint(SpriteTint.Format(Color.FromArgb(255, colorDialog.Color).ToSpriteColor()));
        }
    }

    private void ApplyTint(string? tint)
    {
        var part = ActivePart;
        if (IsActivePartLinked)
        {
            part.SetCurrentSetTint(tint);
        }
        else
        {
            var selected = SelectedOptionIndices();
            if (selected.Count == 0)
            {
                _warningLabel.Text = "Select the options to tint in the list first.";
                return;
            }

            foreach (var index in selected)
            {
                part.Options[index] = part.Options[index] with { Tint = tint };
            }
        }

        _warningLabel.Text = "";
        RefreshAll();
    }

    private void CopySelectedSlot()
    {
        var part = ActivePart;
        if (part.CurrentVariantSet[part.SelectedMask] is null)
        {
            MessageBox.Show(this, "The selected slot is empty; fill it first.", "Nothing to copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new CopyToSlotsDialog(_sheets, part.Layout, part.CurrentVariantSet, part.SelectedMask, part.Span);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        foreach (var targetMask in dialog.TargetMasks)
        {
            part.CurrentVariantSet[targetMask] = dialog.CopiedPiece;
        }

        RefreshAll();
    }

    private void FillEmptySlots()
    {
        using var dialog = new FillSlotsDialog(ActivePart.Layout);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var slots = ActivePart.CurrentVariantSet;
        var filledCount = SlotFiller.FillEmptySlots(ActivePart.Layout, slots, dialog.MirrorLeftRight, dialog.MirrorTopBottom, dialog.Turns);
        var stillEmpty = slots.Count(slot => slot is null);
        _warningLabel.Text = $"Filled {filledCount} slot(s)." + (stillEmpty == 0 ? "" : $" {stillEmpty} still empty: no filled piece mirrors or turns into them.");
        RefreshAll();
    }

    private void ClearSelectedSlot()
    {
        var part = ActivePart;
        part.CurrentVariantSet[part.SelectedMask] = null;
        RefreshAll();
    }

    /// <summary>Applies transform to the selected slot, or to every selected option, of the part being edited; a quarter turn of a non-square cell is refused (the game would refuse to load it), and a refused option is left as it was while the others turn.</summary>
    private void TransformSelected(Func<SpriteManifestCell, SpriteManifestCell> transform, bool isQuarterTurn)
    {
        var part = ActivePart;
        if (IsActivePartLinked)
        {
            if (part.CurrentVariantSet[part.SelectedMask] is not { } slotCell)
            {
                _warningLabel.Text = "The selected slot is empty; fill it first.";
                return;
            }

            if (isQuarterTurn && !slotCell.CanQuarterTurn)
            {
                _warningLabel.Text = $"Only a square cell can be turned a quarter; this one is {slotCell.SourceWidth}x{slotCell.SourceHeight} pixels.";
                return;
            }

            part.CurrentVariantSet[part.SelectedMask] = transform(slotCell);
            _warningLabel.Text = "";
            RefreshAll();
            return;
        }

        var selected = SelectedOptionIndices();
        if (selected.Count == 0)
        {
            _warningLabel.Text = "Select an option in the list first.";
            return;
        }

        var refused = new List<string>();
        foreach (var index in selected)
        {
            if (isQuarterTurn && !part.Options[index].CanQuarterTurn)
            {
                refused.Add($"#{index + 1}");
                continue;
            }

            part.Options[index] = transform(part.Options[index]);
        }

        _warningLabel.Text = refused.Count == 0 ? "" : $"Only a square cell can be turned a quarter; left {string.Join(", ", refused)} as they were.";
        RefreshAll();
    }

    /// <summary>The option or slot the placement preview shows: the selected one (the first, when several are), else the first there is.</summary>
    private SpriteManifestCell? SelectedCellForPlacement()
    {
        var part = ActivePart;
        if (IsActivePartLinked)
        {
            return part.CurrentVariantSet[part.SelectedMask] ?? part.CurrentVariantSet.FirstOrDefault(cell => cell is not null);
        }

        return SelectedOptionIndices() is [var firstSelected, ..]
            ? part.Options[firstSelected]
            : part.Options.FirstOrDefault();
    }

    private PlacedCell? PlacedForPreview()
    {
        var part = ActivePart;
        return SelectedCellForPlacement() is { } cell ? new PlacedCell(cell, part.PixelsPerTile, part.PivotFor(cell), ActiveRaisedRows) : null;
    }

    private PlacedCell? FaceBeneathForPreview() =>
        IsEditingTop && SpriteLinkLayout.Horizontal.PieceFor(_facePart.CurrentVariantSet, SpriteLinks.Standalone) is { } face
            ? new PlacedCell(face, _facePart.PixelsPerTile, _facePart.PivotFor(face), 0)
            : null;

    private void PlacementPreview_Paint(object? sender, PaintEventArgs e) =>
        PlacementPreviewRenderer.Paint(e.Graphics, _placementPreviewBox.ClientSize, PlacedForPreview(), FaceBeneathForPreview(), FootprintTiles, _sheets);

    private void PlacementPreview_MouseClick(object? sender, MouseEventArgs e)
    {
        if (!_setPivotToggle.Checked || PlacedForPreview() is not { PixelsPerTile: not null } placed)
        {
            return;
        }

        var drawn = PlacementPreviewRenderer.DrawnRectangle(_placementPreviewBox.ClientSize, placed, FootprintTiles);
        if (!drawn.Contains(e.Location))
        {
            return;
        }

        var source = placed.Cell.SourceSize();
        var pivotX = (int)Math.Round((e.X - drawn.Left) / drawn.Width * source.Width);
        var pivotY = (int)Math.Round((e.Y - drawn.Top) / drawn.Height * source.Height);

        SyncControls(() =>
        {
            _defaultPivotCheckBox.Checked = false;
            _pivotXUpDown.Value = Math.Clamp(pivotX, 0, source.Width);
            _pivotYUpDown.Value = Math.Clamp(pivotY, 0, source.Height);
            _setPivotToggle.Checked = false;
        });
        OnPlacementControlChanged();
    }

    private void OptionsListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var options = ActivePart.Options;
        if (e.Index < 0 || e.Index >= options.Count)
        {
            return;
        }

        DrawListItem(e, $"#{e.Index + 1} {options[e.Index].Describe()}", !EntryRules.Fits(options[e.Index], ActivePart.Span), options[e.Index].TintColor?.ToDrawingColor());
    }

    private void EntriesListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _entriesListBox.Items[e.Index] is not string name)
        {
            return;
        }

        var entry = _allEntries.FirstOrDefault(entry => entry.Name == name);
        DrawListItem(e, entry?.Top is null ? name : $"{name}  (wall)", entry is not null && SpriteEntryRules.HasMisfit(entry));
    }

    /// <summary>Fills swatch with tint ("#RRGGBB"), or leaves it empty when there is none, with a tooltip saying which.</summary>
    private void ShowTintSwatch(Panel swatch, string? tint)
    {
        swatch.BackColor = tint is { } text && SpriteTint.TryParse(text, out var color) ? color.ToDrawingColor() : SystemColors.Control;
        _toolTip.SetToolTip(swatch, tint is null ? "This variant set is untinted" : $"This variant set is tinted {tint}");
    }

    /// <summary>A list row with a warning icon in front of it when something in it doesn't fit its span.</summary>
    /// <param name="swatch">A tint to show as a colour square at the row's right end; null for none.</param>
    private static void DrawListItem(DrawItemEventArgs e, string text, bool showWarning, Color? swatch = null)
    {
        e.DrawBackground();
        var textLeft = e.Bounds.Left + WarningIcon.Pixels + 4;
        if (showWarning)
        {
            WarningIcon.Draw(e.Graphics, e.Bounds.Left + 1, e.Bounds.Top + (e.Bounds.Height - WarningIcon.Pixels) / 2);
        }

        var textRight = e.Bounds.Right;
        if (swatch is { } swatchColor)
        {
            var swatchSize = e.Bounds.Height - 4;
            var swatchBounds = new Rectangle(e.Bounds.Right - swatchSize - 2, e.Bounds.Top + 2, swatchSize, swatchSize);
            using var swatchBrush = new SolidBrush(swatchColor);
            e.Graphics.FillRectangle(swatchBrush, swatchBounds);
            e.Graphics.DrawRectangle(Pens.Black, swatchBounds);
            textRight = swatchBounds.Left - 2;
        }

        var textBounds = Rectangle.FromLTRB(textLeft, e.Bounds.Top, textRight, e.Bounds.Bottom);
        TextRenderer.DrawText(e.Graphics, text, e.Font, textBounds, e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }

    private void RefreshAll()
    {
        _wallPartRow.Visible = IsWall;
        _optionsSection.Visible = !IsActivePartLinked;
        _linkedSetSection.Visible = IsActivePartLinked;
        _linkPreviewSection.Visible = IsWall || IsMainPartLinked;

        RefreshOptionsListBox();
        var linkedPart = ActivePart;
        _slotGrid.ExpectedSpan = linkedPart.Span;
        _slotGrid.SetSlots(linkedPart.Layout, linkedPart.CurrentVariantSet);
        ShowVariantSetRows(linkedPart);
        _selectedSlotLabel.Text = $"Selected: {linkedPart.Layout.Describe(linkedPart.SelectedMask)}" +
            (linkedPart.CurrentVariantSet[linkedPart.SelectedMask] is { } slotCell ? $" -- {slotCell.Describe()}" : " -- empty");
        _legendLabel.Text = Legend();
        _linkPreviewBox.Invalidate();
        _pictureBox.Invalidate();
        RefreshPlacement();
    }

    private string Legend()
    {
        if (IsWall)
        {
            var faceMisfits = EntryRules.CountMisfits(_facePart.Cells(linked: true), _facePart.Span);
            var topMisfits = EntryRules.CountMisfits(_topPart.Cells(linked: true), _topPart.Span);
            var missingStandalone = _facePart.SetsMissingRequiredPiece();
            var standaloneText = missingStandalone.Count == 0 ? "" : $"; standalone piece missing in set {string.Join(", ", missingStandalone)} (required)";
            return $"Face: {FilledText(_facePart)}, {SpanText(_facePart.Span)}, {FaceHeightRows} row(s) tall{standaloneText}{MisfitText(faceMisfits, _facePart.Span)}. " +
                $"Top: {FilledText(_topPart)}, {SpanText(_topPart.Span)}{MisfitText(topMisfits, _topPart.Span)}.";
        }

        var misfits = EntryRules.CountMisfits(_mainPart.Cells(IsMainPartLinked), _mainPart.Span);
        if (IsMainPartLinked)
        {
            return $"Linked set: {_mainPart.VariantSets.Count} variant set(s), {FilledText(_mainPart)} filled{MisfitText(misfits, _mainPart.Span)}";
        }

        return _mainPart.Options.Count == 0
            ? $"Options: none yet. Click a cell for a {SpanText(_mainPart.Span)} option, or drag across cells."
            : $"Options: {_mainPart.Options.Count}{MisfitText(misfits, _mainPart.Span)}";
    }

    private static string FilledText(EntryPart part) =>
        $"{part.VariantSets.Sum(set => set.Count(cell => cell is not null))} of {part.VariantSets.Count * part.Layout.SlotCount} slots";

    private static string MisfitText(int misfits, Size span) => misfits == 0 ? "" : $"; {misfits} don't fit {SpanText(span)} (marked)";

    private static string SpanText(Size span) => $"{span.Width}x{span.Height}";

    private void RefreshOptionsListBox()
    {
        var options = ActivePart.Options;
        var selectedIndices = _optionsListBox.SelectedIndices.Cast<int>().ToList();
        _optionsListBox.BeginUpdate();
        _optionsListBox.Items.Clear();
        for (var index = 0; index < options.Count; index++)
        {
            _optionsListBox.Items.Add($"#{index + 1}");
        }

        foreach (var index in selectedIndices.Where(index => index < options.Count))
        {
            _optionsListBox.SetSelected(index, true);
        }

        _optionsListBox.EndUpdate();
    }

    private void RefreshPlacement()
    {
        _placementHeaderLabel.Text = IsWall ? $"Placement: wall {(IsEditingTop ? "top" : "face")}" : "Placement";
        _pixelsPerTileUpDown.Enabled = !_stretchCheckBox.Checked;
        _defaultPivotCheckBox.Enabled = !_stretchCheckBox.Checked;
        _pivotXUpDown.Enabled = !_stretchCheckBox.Checked && !_defaultPivotCheckBox.Checked;
        _pivotYUpDown.Enabled = _pivotXUpDown.Enabled;
        _setPivotToggle.Enabled = !_stretchCheckBox.Checked;

        var cell = SelectedCellForPlacement();
        if (cell is not null && !ActivePart.HasExplicitPivot)
        {
            var pivot = ActivePart.PivotFor(cell);
            SyncControls(() =>
            {
                _pivotXUpDown.Value = Math.Clamp(pivot.X, 0, (int)_pivotXUpDown.Maximum);
                _pivotYUpDown.Value = Math.Clamp(pivot.Y, 0, (int)_pivotYUpDown.Maximum);
            });
        }

        var reachProblem = DescribeReachProblem();
        _placementStatusLabel.Text = reachProblem
            ?? (cell is null ? "Nothing to preview yet."
                : ActivePart.PixelsPerTile is null ? IsEditingTop ? $"Stretched to the tile above a {FaceHeightRows}-row face." : "Stretched to its footprint."
                : "Inside the reach box.");

        _linkPreviewNoteLabel.Text = LinkPreviewPart.PixelsPerTile is null && LinkPreviewPart.CurrentVariantSet.Any(slot => slot is not null && slot.SourceWidth != slot.SourceHeight)
            ? "Stretch to footprint squeezes each piece into one tile, as the game would. Set Pixels per tile to see pieces at their own size."
            : "";
        _placementPreviewBox.Invalidate();
    }

    /// <summary>The first cell the game would refuse at load, with its message: any part outside the reach box, a wall's top counted on top of its face.</summary>
    private string? DescribeReachProblem()
    {
        var name = _nameTextBox.Text.Trim();
        return IsWall
            ? DescribeReachProblem(name, null, _facePart, linked: true, raisedRows: 0) ?? DescribeReachProblem(name, "top", _topPart, linked: true, raisedRows: FaceHeightRows)
            : DescribeReachProblem(name, null, _mainPart, IsMainPartLinked, raisedRows: 0);
    }

    /// <summary>Why Save refuses the variant set chances, or null when every linked part's chances add up to 100% (the game refuses anything else at load).</summary>
    private string? DescribeChancesProblem()
    {
        IEnumerable<(string Name, EntryPart Part)> linkedParts = IsWall ? [("face", _facePart), ("top", _topPart)]
            : IsMainPartLinked ? [("linked set", _mainPart)]
            : [];

        foreach (var (partName, part) in linkedParts)
        {
            if (part.VariantWeightsTotal != 100)
            {
                return $"The {partName}'s variant set chances must add up to 100% to save; they add up to {part.VariantWeightsTotal}% ({string.Join(", ", part.VariantWeights.Select(weight => $"{weight}%"))}).";
            }
        }

        return null;
    }

    private static string? DescribeReachProblem(string name, string? partName, EntryPart part, bool linked, int raisedRows) =>
        SpriteManifestValidation.FindReachProblem(name, partName, part.ToManifestPart(linked), raisedRows);

    private void RefreshEntriesListBox()
    {
        _entriesListBox.Items.Clear();
        foreach (var entry in _allEntries.OrderBy(static entry => entry.Name))
        {
            _entriesListBox.Items.Add(entry.Name);
        }
    }

    /// <summary>Opens the selected entry for editing -- only while exactly one is selected, so extending a selection with Shift or Ctrl (to delete several) doesn't replace the entry being edited.</summary>
    private void LoadSelectedEntry()
    {
        if (_isSyncingControls || _entriesListBox.SelectedItems.Count != 1 || _entriesListBox.SelectedItem is not string name)
        {
            return;
        }

        var entry = _allEntries.First(entry => entry.Name == name);
        var kindIndex = entry.Top is not null ? WallKindIndex : entry.Linking != SpriteLinking.None ? LinkedSetKindIndex : OptionsKindIndex;

        var span = EntryRules.SpanOf(entry.Cells, entry.ColumnSpan, entry.RowSpan);
        if (entry.Top is { } top)
        {
            _mainPart.Clear();
            _facePart.Load(entry.Cells, linked: true, savedAsOptions: entry.Linking == SpriteLinking.None, entry.PixelsPerTile, entry.PivotX, entry.PivotY, span, entry.VariantWeights);
            _topPart.Load(top.Cells, linked: true, savedAsOptions: false, top.PixelsPerTile, top.PivotX, top.PivotY, EntryRules.SpanOf(top.Cells, top.ColumnSpan, top.RowSpan), top.VariantWeights);
        }
        else
        {
            _mainPart.Load(entry.Cells, linked: kindIndex == LinkedSetKindIndex, savedAsOptions: false, entry.PixelsPerTile, entry.PivotX, entry.PivotY, span, entry.VariantWeights);
            _facePart.Clear();
            _topPart.Clear();
        }

        SyncControls(() =>
        {
            _nameTextBox.Text = entry.Name;
            _kindComboBox.SelectedIndex = kindIndex;
            _faceRadioButton.Checked = true;
            _setPivotToggle.Checked = false;
        });
        _warningLabel.Text = "";

        // Auto-load the first cell's sheet/resolution so its cells show highlighted right away, the
        // same as if they had just been clicked -- without this, an entry's saved cells sit in the
        // working list but nothing highlights until the matching sheet is reopened by hand.
        if (entry.Cells.Concat(entry.Top?.Cells ?? []).OfType<SpriteManifestCell>().FirstOrDefault() is { } firstCell)
        {
            _cellWidthUpDown.Value = Math.Clamp((decimal)firstCell.CellWidth, _cellWidthUpDown.Minimum, _cellWidthUpDown.Maximum);
            _cellHeightUpDown.Value = Math.Clamp((decimal)firstCell.CellHeight, _cellHeightUpDown.Minimum, _cellHeightUpDown.Maximum);
            LoadSheet(firstCell.SheetPath);
        }

        LoadActivePartIntoControls();
    }

    private void ClearWorkingEntry()
    {
        _mainPart.Clear();
        _facePart.Clear();
        _topPart.Clear();
        SyncControls(() =>
        {
            _nameTextBox.Text = "";
            _kindComboBox.SelectedIndex = OptionsKindIndex;
            _faceRadioButton.Checked = true;
            _setPivotToggle.Checked = false;
            _entriesListBox.ClearSelected();
        });
        _warningLabel.Text = "";
        LoadActivePartIntoControls();
    }

    private void DeleteSelectedEntries()
    {
        var names = _entriesListBox.SelectedItems.OfType<string>().ToHashSet();
        if (names.Count == 0)
        {
            MessageBox.Show(this, "Select an entry in the Existing entries list first.", "Nothing to delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var subject = names.Count == 1 ? $"'{names.Single()}'" : $"these {names.Count} entries ({string.Join(", ", names.Order())})";
        var confirmation = MessageBox.Show(
            this,
            $"Delete {subject} from SpriteManifest.json? Anything in the game that names them will fall back to its glyph.",
            names.Count == 1 ? "Delete entry" : "Delete entries",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        var updatedEntries = SpriteManifest.ReadEntries(RepoPaths.ManifestFilePath).Where(entry => !names.Contains(entry.Name)).ToList();
        SpriteManifest.Write(RepoPaths.ManifestFilePath, updatedEntries);

        _allEntries = updatedEntries;
        RefreshEntriesListBox();
        ClearWorkingEntry();
    }

    private void Save()
    {
        if (BuildEntry(out var refusal) is not { } newEntry)
        {
            MessageBox.Show(this, refusal, "Cannot save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var currentEntries = SpriteManifest.ReadEntries(RepoPaths.ManifestFilePath);
        var updatedEntries = Upsert(currentEntries, newEntry);
        SpriteManifest.Write(RepoPaths.ManifestFilePath, updatedEntries);

        _allEntries = updatedEntries;
        RefreshEntriesListBox();

        var misfits = EntryRules.CountMisfits(newEntry.Cells, (IsWall ? _facePart : _mainPart).Span) + (newEntry.Top is { } top ? EntryRules.CountMisfits(top.Cells, _topPart.Span) : 0);
        var emptySlots = newEntry.Cells.Concat(newEntry.Top?.Cells ?? []).Count(cell => cell is null);
        var unfinished = misfits == 0 && emptySlots == 0 ? "" : $" Still to do: {emptySlots} empty slot(s), {misfits} cell(s) that don't fit their span.";
        MessageBox.Show(this, $"Saved '{newEntry.Name}' to SpriteManifest.json.{unfinished}", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// The entry Save writes, or null with the reason it refuses: no name, nothing selected at all, a wall face without its
    /// standalone piece in every variant set, chances that don't add up, a cell outside the reach box, or anything else the game would refuse
    /// to load (SpriteManifestValidation, the game's own check). Other empty slots and cells that don't fit the span are saved as they are, so unfinished work isn't lost.
    /// </summary>
    private SpriteManifestEntry? BuildEntry(out string refusal)
    {
        var name = _nameTextBox.Text.Trim();
        if (name.Length == 0)
        {
            refusal = "Enter a name first.";
            return null;
        }

        if (!IsActivePartLinked && _mainPart.Options.Count == 0)
        {
            refusal = "Add at least one option first: click a cell, or drag across cells.";
            return null;
        }

        if (IsWall && _facePart.SetsMissingRequiredPiece() is { Count: > 0 } setsMissingStandalone)
        {
            refusal = $"The wall's face needs its standalone piece in every variant set (missing in set {string.Join(", ", setsMissingStandalone)}). " +
                "It is the one required piece: the left end, right end and middle fall back to it when they are empty.";
            return null;
        }

        if (DescribeChancesProblem() is { } chancesProblem)
        {
            refusal = chancesProblem;
            return null;
        }

        if (DescribeReachProblem() is { } reachProblem)
        {
            refusal = reachProblem;
            return null;
        }

        var face = (IsWall ? _facePart : _mainPart).ToManifestPart(IsActivePartLinked);
        var entry = new SpriteManifestEntry(
            name,
            face.Cells,
            face.PixelsPerTile,
            face.PivotX,
            face.PivotY,
            face.Linking,
            IsWall ? _topPart.ToManifestPart(linked: true) : null,
            face.ColumnSpan,
            face.RowSpan,
            face.VariantWeights);

        if (SpriteManifestValidation.FindProblem(entry) is { } loadProblem)
        {
            refusal = loadProblem;
            return null;
        }

        refusal = "";
        return entry;
    }

    /// <summary>Replaces any existing entry with the same Name where it stands, otherwise appends -- so re-saving an unchanged entry rewrites the file byte for byte.</summary>
    private static List<SpriteManifestEntry> Upsert(IReadOnlyList<SpriteManifestEntry> entries, SpriteManifestEntry newEntry)
    {
        var result = entries.ToList();
        var existingIndex = result.FindIndex(entry => entry.Name == newEntry.Name);
        if (existingIndex >= 0)
        {
            result[existingIndex] = newEntry;
            return result;
        }

        result.Add(newEntry);
        return result;
    }
}
