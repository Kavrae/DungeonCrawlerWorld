using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI.Fields;

/// <summary>
/// A control showing which of a list of choices are chosen, joined by commas, that opens the shared Dropdown under itself
/// with a checkbox on every choice. Choosing one turns it on or off and leaves the list open, so several can be
/// turned in one visit; a press outside, Escape or a press on the field closes it.
/// </summary>
/// <remarks>Configure before Initialize, the usual contract.</remarks>
public sealed class MultiChoiceField(ElementServices services, DropdownController dropdowns)
    : Element(services)
{
    private static float HorizontalTextInset => WindowChrome.ControlTextInset;

    private readonly SortedSet<int> _chosen = [];

    private IReadOnlyList<string> _choices = [];
    private string _chosenLabel = string.Empty;
    private string _placeholder = string.Empty;

    /// <summary>What the field shows -- the chosen labels, else the placeholder -- left of the arrow, cut with "..." to the room there.</summary>
    private Label? _shownText;

    /// <summary>Shown when nothing is chosen.</summary>
    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value;
            ShowChosen();
        }
    }

    /// <summary>The chosen choices' indices, in order.</summary>
    public IReadOnlyList<int> ChosenIndices => [.. _chosen];

    /// <summary>Raised with every chosen index, in order, each time the player turns one on or off.</summary>
    public event Action<IReadOnlyList<int>>? ChoicesChanged;

    public bool IsListOpen => dropdowns.IsOpen && ReferenceEquals(dropdowns.Owner, this);

    public override bool IsTabStop => Enabled;

    /// <summary>The text starts past the left inset.</summary>
    protected override float LeadingTileInset => HorizontalTextInset;

    /// <summary>The list arrow's room, as a DropdownField keeps it, which the text never grows into.</summary>
    protected override float TrailingTileInset => DropdownField.ArrowReserve;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _placeholder = string.Empty;
        _shownText = null;
        _canContainChildren = true;
        _childrenTileMode = ChildElementTileMode.Horizontal;
        ContentPadding = Vector2.Zero;
    }

    public void Configure(IReadOnlyList<string> choices, IEnumerable<int> chosenIndices)
    {
        _choices = choices;
        _chosen.Clear();
        _chosen.UnionWith(chosenIndices.Where(index => index >= 0 && index < choices.Count));
        _chosenLabel = BuildChosenLabel();
        Placeholder = string.Empty;
    }

    protected override bool OnContentClickAction(Point mousePosition)
    {
        if (IsListOpen)
        {
            dropdowns.Close();
            return true;
        }

        OpenList();
        return true;
    }

    protected override bool OnKeyPressAction(Keys key)
    {
        if (key is Keys.Space or Keys.Enter or Keys.Down && !IsListOpen)
        {
            OpenList();
            return true;
        }

        return base.OnKeyPressAction(key);
    }

    protected internal override void OnClosed()
    {
        if (IsListOpen)
        {
            dropdowns.Close();
        }

        base.OnClosed();
    }

    private void OpenList()
    {
        if (!Enabled || _choices.Count == 0)
        {
            return;
        }

        var options = new DropdownOption[_choices.Count];
        for (var index = 0; index < _choices.Count; index++)
        {
            var capturedIndex = index;
            options[index] = new DropdownOption(_choices[index], null, Enabled: true, () => Flip(capturedIndex)) { IsChecked = _chosen.Contains(index) };
        }

        dropdowns.OpenBelow(this, options, -1, DropdownField.ListMaximumHeight);
    }

    private void Flip(int index)
    {
        if (!_chosen.Remove(index))
        {
            _chosen.Add(index);
        }

        _chosenLabel = BuildChosenLabel();
        ShowChosen();
        ChoicesChanged?.Invoke(ChosenIndices);
    }

    private string BuildChosenLabel() => string.Join(", ", _chosen.Select(index => _choices[index]));

    /// <summary>Shows the chosen labels, else the placeholder in ghost colour, in the text label -- built the first time there is something to show.</summary>
    private void ShowChosen()
    {
        var text = _chosen.Count == 0 ? _placeholder : _chosenLabel;
        if (_shownText is null)
        {
            if (text.Length == 0)
            {
                return;
            }

            _shownText = ElementPoolService.CreateElement<Label>(this, new ElementOptions
            {
                Layout = new ElementLayoutOptions { DisplayMode = ElementDisplayMode.Fill, FillsRemaining = true },
                Chrome = new ElementChromeOptions { CanUserFocus = false },
                Text = new TextOptions { Text = text },
            });
            AddChild(_shownText);
        }

        _shownText.TextColor = _chosen.Count == 0 ? WindowPalette.GhostTextOnLightColor : WindowPalette.BodyTextColor;
        _shownText.SetText(text);
    }

    /// <summary>Draws the list arrow, as a DropdownField does; the text is the label child's.</summary>
    public override void DrawContent(GameTime gameTime) => DropdownField.DrawArrow(Render.Batch, LocalContentRectangle);

    /// <summary>Draws the focus mark over everything else while focused.</summary>
    public override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);

        if (IsFocused)
        {
            DrawFocusIndicator(Rectangle);
        }
    }
}
