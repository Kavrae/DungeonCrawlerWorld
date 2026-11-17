using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>
/// A choice: a pressable control holding IsOn, flipped by a click anywhere on it or Space while focused, drawing its
/// checkbox first in its content and showing its children after it. SetLabel adds the common child, a label filling
/// the rest.
/// </summary>
/// <remarks>
/// Owns its state and look; a caller wires what happens on a flip through Configure's onToggled, called with the new
/// state. A disabled toggle keeps its state, draws its checkbox darkened (WindowPalette.DisabledBackground) with its
/// mark, border and label dimmed, and takes a click without flipping.
/// </remarks>
public sealed class Toggle(ElementServices services)
    : PressableElement(services)
{
    /// <summary>Side length of the checkbox square -- public so a caller sizing its own row can compute how much width a label needs beside it.</summary>
    public const float CheckboxSize = 14f;

    /// <summary>Gap between the checkbox square and what follows it.</summary>
    public const float LabelGap = 4f;

    /// <summary>How far the on-state X's four endpoints sit in from the checkbox square's own edges -- keeps the mark from touching/overlapping the checkbox's own border.</summary>
    private const float CheckMarkInset = 2f;

    private const float CheckMarkThickness = 2f;
    private const float DisabledOpacity = 0.45f;
    private static readonly Color CheckMarkColor = Color.White;

    private Color _onColor;
    private Color _offColor;
    private Action<bool>? _onToggled;
    private Label? _label;

    public bool IsOn { get; private set; }

    public override bool IsTabStop => Enabled;

    /// <summary>How the checkbox is drawn; Raised unless set.</summary>
    public ToggleCheckboxStyle CheckboxStyle { get; set; }

    /// <summary>The flat checkbox's side.</summary>
    public const float FlatCheckboxSize = 12f;

    /// <summary>The flat checkbox's side and the gap after it.</summary>
    private const float FlatCheckboxReserve = FlatCheckboxSize + 6f;

    /// <summary>How opaque a flat checkbox is while disabled.</summary>
    private const float FlatDisabledOpacity = 0.5f;

    /// <summary>Tiled children start after the checkbox and its gap, so a menu row's icon and text follow it.</summary>
    protected override float LeadingTileInset =>
        CheckboxStyle == ToggleCheckboxStyle.Flat ? FlatCheckboxReserve : (int)(CheckboxSize + LabelGap);

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        _label = null;
        CheckboxStyle = ToggleCheckboxStyle.Raised;
    }

    /// <summary>Shows isOn without notifying onToggled -- for a caller that keeps several toggles in step (an "All" toggle and the ones it covers).</summary>
    public void SetIsOn(bool isOn) => IsOn = isOn;

    /// <summary>Sets the state, the checkbox's colours and what a flip calls; called after CreateElement, before the toggle is added.</summary>
    public void Configure(bool defaultOn, Color onColor, Color offColor, Action<bool> onToggled)
    {
        _onColor = onColor;
        _offColor = offColor;
        _onToggled = onToggled;
        IsOn = defaultOn;
        Enabled = true;
    }

    /// <summary>Shows text centred in the space after the checkbox, in font and color (dimmed while disabled); a later call changes the text shown.</summary>
    public Label SetLabel(string text, SpriteFontBase font, Color color)
    {
        if (_label is { } label)
        {
            label.TextColor = color;
            label.DisabledTextColor = color * DisabledOpacity;
            label.SetFont(font);
            label.SetText(text);
            return label;
        }

        label = ElementPoolService.CreateElement<Label>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2((int)(ContentIndent + CheckboxSize + LabelGap), 0), DisplayMode = ElementDisplayMode.Fill },
            Text = new TextOptions { Text = text, TextColor = color },
        });
        label.DisabledTextColor = color * DisabledOpacity;
        label.IsCentered = true;
        label.SetFont(font);
        _label = label;
        AddChild(label);
        return label;
    }

    /// <summary>Flips the state and notifies onToggled.</summary>
    protected override bool OnContentClickAction(Point mousePosition)
    {
        Flip();
        return true;
    }

    protected override void OnActivated() => Flip();

    private void Flip()
    {
        IsOn = !IsOn;
        _onToggled?.Invoke(IsOn);
    }

    /// <summary>Draws the focus mark around the checkbox square while focused.</summary>
    public override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);

        if (!IsFocused)
        {
            return;
        }

        var checkbox = CheckboxRectangle();
        var origin = ContentDrawOrigin;
        var onScreen = new Rectangle(checkbox.X + (int)origin.X, checkbox.Y + (int)origin.Y, checkbox.Width, checkbox.Height);
        onScreen.Inflate(2, 2);
        DrawFocusIndicator(onScreen);
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var checkboxRectangle = CheckboxRectangle();
        var thickness = BorderThickness.Uniform(Vector2.One);
        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(checkboxRectangle, thickness);
        var checkboxCorner = new Vector2(checkboxRectangle.X, checkboxRectangle.Y);
        var checkboxSize = new Vector2(checkboxRectangle.Width, checkboxRectangle.Height);

        if (CheckboxStyle == ToggleCheckboxStyle.Flat)
        {
            var flatOpacity = Enabled ? 1f : FlatDisabledOpacity;
            spriteBatch.FillRectangle(checkboxRectangle, Color.White * flatOpacity);
            BorderRenderer.Draw(spriteBatch, BorderStyle.Flat, WindowPalette.BorderColor * flatOpacity, top, bottom, left, right);
            if (IsOn)
            {
                CrossRenderer.Draw(spriteBatch, checkboxCorner, checkboxSize, CheckMarkInset, CheckMarkThickness, WindowPalette.BodyTextColor * flatOpacity);
            }

            return;
        }

        var opacity = Enabled ? 1f : DisabledOpacity;
        var checkboxColor = IsOn ? _onColor : _offColor;
        spriteBatch.FillRectangle(checkboxRectangle, Enabled ? checkboxColor : WindowPalette.DisabledBackground(checkboxColor));
        BorderRenderer.Draw(spriteBatch, IsOn ? BorderStyle.Inset : BorderStyle.Outset, Color.White * opacity, top, bottom, left, right);
        if (IsOn)
        {
            CrossRenderer.Draw(spriteBatch, checkboxCorner, checkboxSize, CheckMarkInset, CheckMarkThickness, CheckMarkColor * opacity);
        }
    }

    /// <summary>The checkbox square at the content's left edge past ContentIndent, centred vertically, its side set by CheckboxStyle.</summary>
    internal Rectangle CheckboxRectangle()
    {
        var side = CheckboxStyle == ToggleCheckboxStyle.Flat ? FlatCheckboxSize : CheckboxSize;
        return new Rectangle((int)ContentIndent, (int)((ContentSize.Y - side) / 2f), (int)side, (int)side);
    }
}
