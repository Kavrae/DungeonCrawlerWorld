using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Rendering;
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
    private const float HorizontalTextInset = 4f;

    private readonly SpriteFontBase _font = services.FontService.GetFont(FontChrome.DefaultFontSize);
    private readonly SortedSet<int> _chosen = [];

    private IReadOnlyList<string> _choices = [];
    private string _chosenLabel = string.Empty;

    /// <summary>Shown when nothing is chosen.</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>The chosen choices' indices, in order.</summary>
    public IReadOnlyList<int> ChosenIndices => [.. _chosen];

    /// <summary>Raised with every chosen index, in order, each time the player turns one on or off.</summary>
    public event Action<IReadOnlyList<int>>? ChoicesChanged;

    public bool IsListOpen => dropdowns.IsOpen && ReferenceEquals(dropdowns.Owner, this);

    public override bool IsTabStop => Enabled;

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
        ChoicesChanged?.Invoke(ChosenIndices);
    }

    private string BuildChosenLabel() => string.Join(", ", _chosen.Select(index => _choices[index]));

    /// <summary>Draws the focus mark over everything else while focused.</summary>
    public override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);

        if (IsFocused)
        {
            DrawFocusIndicator(Rectangle);
        }
    }

    public override void DrawContent(GameTime gameTime)
    {
        var text = _chosen.Count == 0 ? Placeholder : _chosenLabel;
        if (text.Length == 0)
        {
            return;
        }

        var color = _chosen.Count == 0 ? WindowPalette.GhostTextOnLightColor : WindowPalette.BodyTextColor;
        LabelRenderer.DrawLeftAligned(Render.Batch, _font, text, new Vector2(HorizontalTextInset, 0), ContentSize - new Vector2(HorizontalTextInset * 2, 0), color);
    }
}
