namespace SpriteManifestBuilder;

/// <summary>
/// The behaviour every control of a kind gets in this tool, whichever window it is in: a list box selects with Shift+click
/// (the run from the last clicked row) and Ctrl+click (one more row), and a number box selects its whole value when focus
/// lands on it, so tabbing between fields lets a new number be typed straight over the old one.
/// </summary>
public static class StandardControlBehaviors
{
    /// <summary>Applies the behaviours to root and every control under it, including controls added after this call.</summary>
    public static void Apply(Control root)
    {
        Attach(root);
    }

    private static void Attach(Control control)
    {
        switch (control)
        {
            case NumericUpDown numberBox:
                numberBox.Enter += (_, _) => numberBox.BeginInvoke(() => numberBox.Select(0, numberBox.Text.Length));
                break;

            case ListBox listBox when listBox.SelectionMode == SelectionMode.One:
                listBox.SelectionMode = SelectionMode.MultiExtended;
                break;
        }

        control.ControlAdded += (_, e) =>
        {
            if (e.Control is { } added)
            {
                Attach(added);
            }
        };

        foreach (Control child in control.Controls)
        {
            Attach(child);
        }
    }
}
