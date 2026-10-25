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
/// list is open closes it. Focused, Space, Enter or Down opens it too, and the dropdown's own Up/Down/Enter choose.
/// </summary>
/// <remarks>Its choices are set with SetOptions, before or after Initialize.</remarks>
public sealed class DropdownField(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService, DropdownController dropdowns)
    : Element(fontService, elementPoolService, renderService)
{
    /// <summary>How tall the open list may grow before it scrolls.</summary>
    public const float ListMaximumHeight = 300f;

    private const float HorizontalTextInset = 4f;
    private const int ArrowWidth = 9;
    private const int ArrowRightInset = 6;

    private readonly SpriteFontBase _font = fontService.GetFont(FontChrome.DefaultFontSize);

    private IReadOnlyList<string> _labels = [];
    private DropdownOption[] _options = [];

    /// <summary>The chosen option's index into the labels SetOptions was given, or -1 for none.</summary>
    public int SelectedIndex { get; private set; } = -1;

    public string? SelectedLabel => SelectedIndex >= 0 ? _labels[SelectedIndex] : null;

    /// <summary>Shown in place of a label when nothing is chosen.</summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>Raised with the new index when the player chooses a different option. Not raised by SetOptions or SetSelectedIndex.</summary>
    public event Action<int>? SelectionChanged;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _labels = [];
        _options = [];
        SelectedIndex = -1;
        Placeholder = string.Empty;
        _contentState.BackgroundColor = options.Content?.ContentColor ?? Color.White;
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
    }

    /// <summary>Chooses selectedIndex without raising SelectionChanged -- the owner's own data changed.</summary>
    public void SetSelectedIndex(int selectedIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(selectedIndex, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(selectedIndex, _labels.Count);

        SelectedIndex = selectedIndex;
    }

    public bool IsListOpen => dropdowns.IsOpen && ReferenceEquals(dropdowns.Owner, this);

    private void Choose(int optionIndex)
    {
        if (optionIndex == SelectedIndex)
        {
            return;
        }

        SelectedIndex = optionIndex;
        SelectionChanged?.Invoke(optionIndex);
    }

    protected override void OnContentClickAction(Point mousePosition)
    {
        base.OnContentClickAction(mousePosition);

        ToggleList();
    }

    protected override void OnKeyPressAction(Keys key)
    {
        if (key is Keys.Space or Keys.Enter or Keys.Down && !IsListOpen)
        {
            ToggleList();
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

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        var text = SelectedLabel ?? Placeholder;
        var textColor = SelectedLabel is null ? WindowPalette.GhostText : WindowPalette.BodyTextColor;
        var textPosition = new Vector2(HorizontalTextInset, 0);
        var textSize = ContentSize - new Vector2(HorizontalTextInset * 2 + ArrowWidth + ArrowRightInset, 0);
        if (text.Length > 0)
        {
            LabelRenderer.DrawLeftAligned(spriteBatch, _font, text, textPosition, textSize, textColor);
        }

        DrawArrow(spriteBatch, unitRectangle);
    }

    /// <summary>A downward triangle of shrinking rows at the right edge -- drawn, since the font has no arrow glyph.</summary>
    private void DrawArrow(SpriteEffectBatch spriteBatch, Microsoft.Xna.Framework.Graphics.Texture2D unitRectangle)
    {
        var content = LocalContentRectangle;
        var left = content.Right - ArrowRightInset - ArrowWidth;
        var rows = (ArrowWidth + 1) / 2;
        var top = content.Y + (content.Height - rows) / 2;
        for (var row = 0; row < rows; row++)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle(left + row, top + row, ArrowWidth - row * 2, 1), WindowPalette.BodyTextColor);
        }
    }
}
