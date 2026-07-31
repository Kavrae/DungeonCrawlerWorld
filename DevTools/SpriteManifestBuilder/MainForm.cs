using System.Drawing.Drawing2D;

namespace SpriteManifestBuilder;

/// <summary>
/// The whole SpriteManifestBuilder workflow: open a sheet, pick a cell size, view it divided
/// into a grid, click one or more cells (accumulating into a working list -- cells can come
/// from more than one sheet/resolution across a session), name them, save to
/// Content/SpriteManifest.json. Hand-built (no .Designer.cs split) -- a small internal tool,
/// not worth the extra file for this much UI.
/// </summary>
public sealed class MainForm : Form
{
    private const int MaxDisplayPixels = 800;
    private const int MaxZoom = 16;

    private readonly OpenFileDialog _openFileDialog = new() { Filter = "PNG files (*.png)|*.png" };
    private readonly NumericUpDown _cellWidthUpDown = new() { Minimum = 1, Maximum = 512, Value = 16 };
    private readonly NumericUpDown _cellHeightUpDown = new() { Minimum = 1, Maximum = 512, Value = 16 };
    private readonly Label _sheetPathLabel = new() { AutoSize = true, Text = "No spritesheet loaded." };
    private readonly Label _warningLabel = new() { AutoSize = true, ForeColor = Color.DarkOrange };
    private readonly TextBox _nameTextBox = new();
    private readonly ListBox _selectedCellsListBox = new() { Height = 120 };
    private readonly ListBox _entriesListBox = new() { Height = 160 };
    private readonly PictureBox _pictureBox = new() { SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Black };
    private readonly Panel _pictureScrollPanel = new() { AutoScroll = true, BorderStyle = BorderStyle.Fixed3D };

    private Image? _sheetImage;
    private string? _currentRelativeSheetPath;
    private int _zoom = 1;

    private readonly List<ManifestCell> _workingCells = [];
    private List<ManifestEntry> _allEntries;

    public MainForm()
    {
        Text = "Sprite Manifest Builder";
        Width = 1200;
        Height = 800;

        _allEntries = ManifestFile.Load(RepoPaths.ManifestFilePath);

        BuildLayout();
        RefreshEntriesListBox();
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
        _pictureBox.MouseClick += PictureBox_MouseClick;
        _pictureScrollPanel.Controls.Add(_pictureBox);

        var removeCellButton = new Button { Text = "Remove selected cell", AutoSize = true };
        removeCellButton.Click += (_, _) => RemoveSelectedWorkingCell();

        var saveButton = new Button { Text = "Save", AutoSize = true };
        saveButton.Click += (_, _) => Save();

        var newButton = new Button { Text = "New", AutoSize = true };
        newButton.Click += (_, _) => ClearWorkingEntry();

        _entriesListBox.SelectedIndexChanged += (_, _) => LoadSelectedEntry();

        var sidePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
        };
        sidePanel.Controls.Add(openButton);
        sidePanel.Controls.Add(_sheetPathLabel);
        sidePanel.Controls.Add(resolutionPanel);
        sidePanel.Controls.Add(new Label { Text = "Name:", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
        sidePanel.Controls.Add(_nameTextBox);
        sidePanel.Controls.Add(_warningLabel);
        sidePanel.Controls.Add(new Label { Text = "Selected cells for this entry:", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
        sidePanel.Controls.Add(_selectedCellsListBox);
        sidePanel.Controls.Add(removeCellButton);
        sidePanel.Controls.Add(new Label { Text = "Existing entries (click to edit):", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
        sidePanel.Controls.Add(_entriesListBox);
        var buttonRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        buttonRow.Controls.Add(saveButton);
        buttonRow.Controls.Add(newButton);
        sidePanel.Controls.Add(buttonRow);

        var sidePanelHost = new Panel { Dock = DockStyle.Left, Width = 340, Padding = new Padding(8), AutoScroll = true };
        sidePanelHost.Controls.Add(sidePanel);

        Controls.Add(_pictureScrollPanel);
        Controls.Add(sidePanelHost);
        _pictureScrollPanel.Dock = DockStyle.Fill;
    }

    private void OpenSpritesheet()
    {
        _openFileDialog.InitialDirectory = RepoPaths.SpritesheetsRoot;
        if (_openFileDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(RepoPaths.SpritesheetsRoot, _openFileDialog.FileName).Replace('\\', '/');
        LoadSheet(_openFileDialog.FileName, relativePath);
    }

    /// <summary>Shared by OpenSpritesheet (user-picked file, guaranteed to exist) and LoadSelectedEntry (a path reconstructed from saved manifest data, which callers must guard for a possibly-missing file).</summary>
    private void LoadSheet(string absolutePath, string relativePath)
    {
        _sheetImage?.Dispose();
        _sheetImage = Image.FromFile(absolutePath);
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

        using var highlightPen = new Pen(Color.Lime, 2);
        foreach (var cell in _workingCells)
        {
            if (cell.SheetPath != _currentRelativeSheetPath || cell.CellWidth != cellWidth || cell.CellHeight != cellHeight)
            {
                continue;
            }

            var rect = new Rectangle(cell.Column * cellWidth * _zoom, cell.Row * cellHeight * _zoom, cellWidth * _zoom, cellHeight * _zoom);
            e.Graphics.DrawRectangle(highlightPen, rect);
        }
    }

    private void PictureBox_MouseClick(object? sender, MouseEventArgs e)
    {
        if (_sheetImage is null || _currentRelativeSheetPath is null)
        {
            return;
        }

        var cellWidth = (int)_cellWidthUpDown.Value;
        var cellHeight = (int)_cellHeightUpDown.Value;
        var column = e.X / (cellWidth * _zoom);
        var row = e.Y / (cellHeight * _zoom);
        var maxColumn = (_sheetImage.Width - 1) / cellWidth;
        var maxRow = (_sheetImage.Height - 1) / cellHeight;

        if (column < 0 || column > maxColumn || row < 0 || row > maxRow)
        {
            return;
        }

        var clicked = new ManifestCell(_currentRelativeSheetPath, column, row, cellWidth, cellHeight);
        var existingIndex = _workingCells.FindIndex(cell => cell == clicked);

        if (existingIndex >= 0)
        {
            _workingCells.RemoveAt(existingIndex);
            _warningLabel.Text = "";
        }
        else
        {
            _workingCells.Add(clicked);
            ShowAlreadyUsedWarningIfAny(clicked);
        }

        RefreshSelectedCellsListBox();
        _pictureBox.Invalidate();
    }

    /// <summary>Checked against the manifest as loaded at startup (refreshed after each Save) -- informational only, per the explicit instruction that duplicate cell usage across names is allowed, never blocked.</summary>
    private void ShowAlreadyUsedWarningIfAny(ManifestCell cell)
    {
        var usedByNames = _allEntries
            .Where(entry => entry.Cells.Any(existingCell => existingCell == cell))
            .Select(entry => entry.Name)
            .ToList();

        _warningLabel.Text = usedByNames.Count > 0
            ? $"Also used by: {string.Join(", ", usedByNames)}"
            : "";
    }

    private void RemoveSelectedWorkingCell()
    {
        if (_selectedCellsListBox.SelectedIndex < 0)
        {
            return;
        }

        _workingCells.RemoveAt(_selectedCellsListBox.SelectedIndex);
        RefreshSelectedCellsListBox();
        _pictureBox.Invalidate();
    }

    private void RefreshSelectedCellsListBox()
    {
        _selectedCellsListBox.Items.Clear();
        foreach (var cell in _workingCells)
        {
            _selectedCellsListBox.Items.Add($"{Path.GetFileName(cell.SheetPath)} ({cell.Column},{cell.Row}) {cell.CellWidth}x{cell.CellHeight}");
        }
    }

    private void RefreshEntriesListBox()
    {
        _entriesListBox.Items.Clear();
        foreach (var entry in _allEntries.OrderBy(static entry => entry.Name))
        {
            _entriesListBox.Items.Add(entry.Name);
        }
    }

    private void LoadSelectedEntry()
    {
        if (_entriesListBox.SelectedItem is not string name)
        {
            return;
        }

        var entry = _allEntries.First(entry => entry.Name == name);
        _nameTextBox.Text = entry.Name;
        _workingCells.Clear();
        _workingCells.AddRange(entry.Cells);
        _warningLabel.Text = "";
        RefreshSelectedCellsListBox();

        // Auto-load the first cell's sheet/resolution so its cells show highlighted right
        // away, the same as if the player had just clicked them -- without this, an entry's
        // saved cells sit in the working list but nothing highlights until the player happens
        // to manually reopen the matching sheet themselves.
        if (entry.Cells.Count == 0)
        {
            _pictureBox.Invalidate();
            return;
        }

        var firstCell = entry.Cells[0];
        _cellWidthUpDown.Value = Math.Clamp((decimal)firstCell.CellWidth, _cellWidthUpDown.Minimum, _cellWidthUpDown.Maximum);
        _cellHeightUpDown.Value = Math.Clamp((decimal)firstCell.CellHeight, _cellHeightUpDown.Minimum, _cellHeightUpDown.Maximum);

        var absolutePath = Path.Combine(RepoPaths.SpritesheetsRoot, firstCell.SheetPath);
        try
        {
            LoadSheet(absolutePath, firstCell.SheetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            MessageBox.Show(this, $"Could not load '{firstCell.SheetPath}' for preview: {ex.Message}", "Preview unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ClearWorkingEntry()
    {
        _nameTextBox.Text = "";
        _workingCells.Clear();
        _warningLabel.Text = "";
        _entriesListBox.ClearSelected();
        RefreshSelectedCellsListBox();
        _pictureBox.Invalidate();
    }

    private void Save()
    {
        var name = _nameTextBox.Text.Trim();
        if (name.Length == 0 || _workingCells.Count == 0)
        {
            MessageBox.Show(this, "Enter a name and select at least one cell first.", "Cannot save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var newEntry = new ManifestEntry(name, [.. _workingCells]);
        var currentEntries = ManifestFile.Load(RepoPaths.ManifestFilePath);
        var updatedEntries = ManifestFile.Upsert(currentEntries, newEntry);
        ManifestFile.Save(RepoPaths.ManifestFilePath, updatedEntries);

        _allEntries = updatedEntries;
        RefreshEntriesListBox();

        MessageBox.Show(this, $"Saved '{name}' ({_workingCells.Count} cell(s)) to SpriteManifest.json.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
