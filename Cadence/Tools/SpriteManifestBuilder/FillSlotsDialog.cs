namespace SpriteManifestBuilder;

/// <summary>Chooses how SlotFiller may derive empty slots from filled ones; steps the set's layout can't use are disabled.</summary>
public sealed class FillSlotsDialog : Form
{
    private readonly CheckBox _mirrorLeftRightCheckBox = new() { Text = "Mirror left-right ↔", AutoSize = true, Checked = true };
    private readonly CheckBox _mirrorTopBottomCheckBox = new() { Text = "Mirror top-bottom ↕", AutoSize = true };
    private readonly CheckBox _turnsCheckBox = new() { Text = "Turn by quarters", AutoSize = true };

    public FillSlotsDialog(SpriteLinkLayout layout)
    {
        Text = "Fill empty slots";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _mirrorTopBottomCheckBox.Enabled = layout.MirrorTopBottom is not null;
        _turnsCheckBox.Enabled = layout.TurnClockwise is not null;

        var okButton = new Button { Text = "Fill", DialogResult = DialogResult.OK, AutoSize = true };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var buttonRow = new FlowLayoutPanel { AutoSize = true };
        buttonRow.Controls.Add(okButton);
        buttonRow.Controls.Add(cancelButton);

        var layoutPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(8) };
        layoutPanel.Controls.Add(new Label
        {
            Text = "Fill every empty slot from a filled one, flipped or turned to join the right sides. Filled slots are left alone. " +
                "Art lit from above usually survives a left-right mirror but not a turn or a top-bottom mirror.",
            AutoSize = true,
            MaximumSize = new Size(320, 0),
        });
        layoutPanel.Controls.Add(_mirrorLeftRightCheckBox);
        layoutPanel.Controls.Add(_mirrorTopBottomCheckBox);
        layoutPanel.Controls.Add(_turnsCheckBox);
        layoutPanel.Controls.Add(buttonRow);
        Controls.Add(layoutPanel);
        StandardControlBehaviors.Apply(this);
    }

    public bool MirrorLeftRight => _mirrorLeftRightCheckBox.Checked;

    public bool MirrorTopBottom => _mirrorTopBottomCheckBox.Enabled && _mirrorTopBottomCheckBox.Checked;

    public bool Turns => _turnsCheckBox.Enabled && _turnsCheckBox.Checked;
}
