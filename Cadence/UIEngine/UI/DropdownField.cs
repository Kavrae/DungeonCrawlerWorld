using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>
/// A control showing the chosen one of a list of choices, which opens the shared Dropdown under itself to choose another
/// -- an enum, a type picker or a reference to another record. A press on it opens the list, and a press on it while its
/// list is open closes it. Focused, Space or Enter opens it too and Up/Down choose the previous or next option without
/// opening it; in the open list, Up/Down/Home/End move the highlight and Enter chooses.
/// </summary>
/// <remarks>Its choices are set with SetOptions, before or after Initialize.</remarks>
public sealed class DropdownField(ElementServices services, DropdownController dropdowns)
    : Element(services)
{
    /// <summary>How tall the open list may grow before it scrolls.</summary>
    public const float ListMaximumHeight = 300f;

    private static float HorizontalTextInset => WindowChrome.ControlTextInset;
    private const int ArrowWidth = 9;
    private const int ArrowRightInset = 6;

    /// <summary>The room a field's text leaves at its right for the list arrow (DrawArrow): the text's inset, the arrow and the arrow's inset. Shared by every field that opens a list.</summary>
    internal static float ArrowReserve => HorizontalTextInset + ArrowWidth + ArrowRightInset;

    private IReadOnlyList<string> _labels = [];
    private DropdownOption[] _options = [];
    private string _placeholder = string.Empty;

    /// <summary>What the field shows -- the chosen label, else the placeholder -- left of the arrow, cut with "..." to the room there.</summary>
    private Label? _shownText;

    /// <summary>The chosen option's index into the labels SetOptions was given, or -1 for none.</summary>
    public int SelectedIndex { get; private set; } = -1;

    public string? SelectedLabel => SelectedIndex >= 0 ? _labels[SelectedIndex] : null;

    /// <summary>Shown in place of a label when nothing is chosen.</summary>
    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value;
            ShowSelection();
        }
    }

    /// <summary>The text starts past the left inset.</summary>
    protected override float LeadingTileInset => HorizontalTextInset;

    /// <summary>The arrow's room, and the inset before it, which the text never grows into.</summary>
    protected override float TrailingTileInset => ArrowReserve;

    /// <summary>Raised with the new index when the player chooses a different option. Not raised by SetOptions or SetSelectedIndex.</summary>
    public event Action<int>? SelectionChanged;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _labels = [];
        _options = [];
        SelectedIndex = -1;
        _placeholder = string.Empty;
        _shownText = null;
        _contentState.BackgroundColor = options.Content?.ContentColor ?? Color.White;
        _canContainChildren = true;
        _childrenTileMode = ChildElementTileMode.Horizontal;
        ContentPadding = Vector2.Zero;
    }

    /// <summary>Shows the chosen label, else the placeholder in ghost colour, in the text label -- built the first time there is something to show.</summary>
    private void ShowSelection()
    {
        var text = SelectedLabel ?? _placeholder;
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

        _shownText.TextColor = SelectedLabel is null ? WindowPalette.GhostTextOnLightColor : WindowPalette.BodyTextColor;
        _shownText.SetText(text);
    }

    /// <summary>Replaces the choices, with selectedIndex chosen (-1 for none).</summary>
    public void SetOptions(IReadOnlyList<string> labels, int selectedIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(selectedIndex, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(selectedIndex, labels.Count);

        _labels = labels;
        _options = new DropdownOption[labels.Count];
        for (var optionIndex = 0; optionIndex < labels.Count; optionIndex++)
        {
            var capturedIndex = optionIndex;
            _options[optionIndex] = new DropdownOption(labels[optionIndex], null, Enabled: true, () => Choose(capturedIndex));
        }

        SelectedIndex = selectedIndex;
        ShowSelection();
    }

    /// <summary>Chooses selectedIndex without raising SelectionChanged -- the owner's own data changed.</summary>
    public void SetSelectedIndex(int selectedIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(selectedIndex, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(selectedIndex, _labels.Count);

        SelectedIndex = selectedIndex;
        ShowSelection();
    }

    public bool IsListOpen => dropdowns.IsOpen && ReferenceEquals(dropdowns.Owner, this);

    public override bool IsTabStop => Enabled;

    private void Choose(int optionIndex)
    {
        if (optionIndex == SelectedIndex)
        {
            return;
        }

        SelectedIndex = optionIndex;
        ShowSelection();
        SelectionChanged?.Invoke(optionIndex);
    }

    protected override bool OnContentClickAction(Point mousePosition)
    {
        ToggleList();
        return true;
    }

    protected override bool OnKeyPressAction(Keys key)
    {
        if (IsListOpen)
        {
            return base.OnKeyPressAction(key);
        }

        switch (key)
        {
            case Keys.Space or Keys.Enter:
                ToggleList();
                return true;
            case Keys.Up or Keys.Down:
                if (_options.Length > 0)
                {
                    Choose(System.Math.Clamp(SelectedIndex + (key == Keys.Down ? 1 : -1), 0, _options.Length - 1));
                }

                return true;
            default:
                return base.OnKeyPressAction(key);
        }
    }

    private void ToggleList()
    {
        if (IsListOpen)
        {
            dropdowns.Close();
            return;
        }

        if (_options.Length == 0)
        {
            return;
        }

        dropdowns.OpenBelow(this, _options, SelectedIndex, ListMaximumHeight);
    }

    /// <summary>Draws the focus mark over everything else while focused.</summary>
    public override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);

        if (IsFocused)
        {
            DrawFocusIndicator(Rectangle);
        }
    }

    /// <summary>Draws the arrow; the text is the label child's.</summary>
    public override void DrawContent(GameTime gameTime) => DrawArrow(Render.Batch, LocalContentRectangle);

    /// <summary>A downward triangle of shrinking rows at the right edge of content -- drawn, since the font has no arrow glyph. Every field that opens a list draws it, in ArrowReserve.</summary>
    internal static void DrawArrow(SpriteEffectBatch spriteBatch, Rectangle content)
    {
        var left = content.Right - ArrowRightInset - ArrowWidth;
        var rows = (ArrowWidth + 1) / 2;
        var top = content.Y + (content.Height - rows) / 2;
        for (var row = 0; row < rows; row++)
        {
            spriteBatch.FillRectangle(new Rectangle(left + row, top + row, ArrowWidth - row * 2, 1), WindowPalette.BodyTextColor);
        }
    }
}
