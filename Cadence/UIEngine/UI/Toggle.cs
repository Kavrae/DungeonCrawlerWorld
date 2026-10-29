using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>
/// A clickable checkbox-style bordered square, plus a text label positioned outside it (see
/// LabelPosition) rather than centered as content -- a generic on/off control, not scoped to any
/// one caller (GridControl's toggle row is one). A plain Element (not Window, not
/// TextWindow) -- the same reasoning Button's own doc comment gives for skipping Window's title/
/// content/hierarchy/chrome baggage, except Toggle participates in the ordinary ChildElements
/// hierarchy like any grid cell (AddChild'd, positioned by the normal layout
/// pipeline, drawn via DrawContent) rather than Button's own hand-rolled-geometry, outside-the-
/// hierarchy title-button special case. Owns its own on/off state and visual entirely; a caller
/// wires what happens on flip via onToggled at Configure time, called with the new state --
/// no index, no generic event to route externally by position.
///
/// The whole ContentRectangle (checkbox + gap + label) is the clickable region, the usual larger-
/// hit-target checkbox convention -- only the checkbox square itself gets the Inset/Outset+color
/// treatment; the label is plain text, no background of its own.
/// </summary>
public sealed class Toggle(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService)
{
    /// <summary>Side length of the checkbox square -- public so a caller (e.g. GridControl, sizing its own row layout before this element's ContentSize exists yet) can compute how much total width a given label needs without duplicating the number.</summary>
    public const float CheckboxSize = 14f;

    /// <summary>Gap between the checkbox square and the label -- same visibility/reuse reasoning as CheckboxSize.</summary>
    public const float LabelGap = 4f;

    /// <summary>How far the on-state X's four endpoints sit in from the checkbox square's own edges -- keeps the mark from touching/overlapping the checkbox's own border.</summary>
    private const float CheckMarkInset = 2f;

    private const float CheckMarkThickness = 2f;
    private const float DisabledOpacity = 0.45f;
    private static readonly Color CheckMarkColor = Color.White;

    private string _label = string.Empty;
    private LabelPosition _labelPosition;
    private Color _onColor;
    private Color _offColor;
    private Color _labelColor;
    private FontStashTextMeasurer _fontMeasurer = null!;
    private Action<bool>? _onToggled;

    public bool IsOn { get; private set; }

    /// <summary>A disabled toggle keeps its state, draws its checkbox darkened (WindowPalette.DisabledBackground) with its mark, border and label dimmed, and is never hit-tested, so a click can't flip it.</summary>
    public bool Enabled { get; set; } = true;

    protected override bool IsHitTestable => base.IsHitTestable && Enabled;

    /// <summary>Shows isOn without notifying onToggled -- for a caller that keeps several toggles in step (an "All" toggle and the ones it covers).</summary>
    public void SetIsOn(bool isOn) => IsOn = isOn;

    /// <summary>Must be called after CreateElement but before Initialize -- the usual Configure contract. font should be whatever the caller already measured label's width with (see GridControl's own row-layout comment) -- a mismatch would let the label clip against whatever fixed width the caller chose.</summary>
    public void Configure(string label, LabelPosition labelPosition, bool defaultOn, Color onColor, Color offColor, Color labelColor, SpriteFontBase font, Action<bool> onToggled)
    {
        _label = label;
        _labelPosition = labelPosition;
        _onColor = onColor;
        _offColor = offColor;
        _labelColor = labelColor;
        _fontMeasurer = FontService.GetTextMeasurer(font);
        _onToggled = onToggled;
        IsOn = defaultOn;
        Enabled = true;
    }

    /// <summary>Flips state and notifies onToggled -- fires from OnContentClickAction (before the base Clicked event, see Element's own doc comment on that ordering), the same "subclass owns its own click behavior" mechanism TextWindow already uses rather than this element self-subscribing to its own public event.</summary>
    protected override bool OnContentClickAction(Point mousePosition)
    {
        IsOn = !IsOn;
        _onToggled?.Invoke(IsOn);
        return true;
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        var (checkboxRectangle, labelRectangle) = ComputeLayout();

        var opacity = Enabled ? 1f : DisabledOpacity;
        var checkboxColor = IsOn ? _onColor : _offColor;
        spriteBatch.Draw(unitRectangle, checkboxRectangle, Enabled ? checkboxColor : WindowPalette.DisabledBackground(checkboxColor));

        var borderStyle = IsOn ? BorderStyle.Inset : BorderStyle.Outset;
        var thickness = BorderThickness.Uniform(Vector2.One);
        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(checkboxRectangle, thickness);
        BorderRenderer.Draw(spriteBatch, unitRectangle, borderStyle, Color.White * opacity, top, bottom, left, right);

        if (IsOn)
        {
            DrawCheckMark(spriteBatch, unitRectangle, checkboxRectangle, CheckMarkColor * opacity);
        }

        if (!string.IsNullOrWhiteSpace(_label))
        {
            LabelRenderer.DrawCentered(spriteBatch, _fontMeasurer, _label, new Vector2(labelRectangle.X, labelRectangle.Y), new Vector2(labelRectangle.Width, labelRectangle.Height), _labelColor * opacity);
        }
    }

    /// <summary>Two diagonals corner-to-corner across checkboxRectangle, inset so the mark's own endpoints don't touch the checkbox's border.</summary>
    private static void DrawCheckMark(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Rectangle checkboxRectangle, Color color)
    {
        var topLeft = new Vector2(checkboxRectangle.X + CheckMarkInset, checkboxRectangle.Y + CheckMarkInset);
        var topRight = new Vector2(checkboxRectangle.Right - CheckMarkInset, checkboxRectangle.Y + CheckMarkInset);
        var bottomLeft = new Vector2(checkboxRectangle.X + CheckMarkInset, checkboxRectangle.Bottom - CheckMarkInset);
        var bottomRight = new Vector2(checkboxRectangle.Right - CheckMarkInset, checkboxRectangle.Bottom - CheckMarkInset);

        DrawLine(spriteBatch, unitRectangle, topLeft, bottomRight, color);
        DrawLine(spriteBatch, unitRectangle, topRight, bottomLeft, color);
    }

    /// <summary>unitRectangle (a 1x1 white pixel) stretched into a thickness-tall strip spanning start-to-end and rotated to match -- the standard "line as a rotated rectangle" trick this renderer has no dedicated line primitive for yet.</summary>
    private static void DrawLine(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Vector2 start, Vector2 end, Color color)
    {
        var delta = end - start;
        var length = delta.Length();
        var rotation = MathF.Atan2(delta.Y, delta.X);
        spriteBatch.Draw(unitRectangle, start, null, color, rotation, Vector2.Zero, new Vector2(length, CheckMarkThickness), SpriteEffects.None);
    }

    /// <summary>
    /// Splits ContentRectangle into the checkbox square (fixed CheckboxSize, on the edge
    /// LabelPosition names -- e.g. East puts it on the left edge, since the label reads to its
    /// east) and whatever's left over for the label, vertically/horizontally centered against
    /// the checkbox on the cross axis.
    /// </summary>
    private (Rectangle Checkbox, Rectangle Label) ComputeLayout()
    {
        var origin = Vector2.Zero;
        var size = ContentSize;
        var checkboxSize = (int)CheckboxSize;

        return _labelPosition switch
        {
            LabelPosition.East => (
                new Rectangle((int)origin.X, (int)(origin.Y + (size.Y - CheckboxSize) / 2f), checkboxSize, checkboxSize),
                new Rectangle((int)(origin.X + CheckboxSize + LabelGap), (int)origin.Y, (int)(size.X - CheckboxSize - LabelGap), (int)size.Y)),
            LabelPosition.West => (
                new Rectangle((int)(origin.X + size.X - CheckboxSize), (int)(origin.Y + (size.Y - CheckboxSize) / 2f), checkboxSize, checkboxSize),
                new Rectangle((int)origin.X, (int)origin.Y, (int)(size.X - CheckboxSize - LabelGap), (int)size.Y)),
            LabelPosition.South => (
                new Rectangle((int)(origin.X + (size.X - CheckboxSize) / 2f), (int)origin.Y, checkboxSize, checkboxSize),
                new Rectangle((int)origin.X, (int)(origin.Y + CheckboxSize + LabelGap), (int)size.X, (int)(size.Y - CheckboxSize - LabelGap))),
            _ => ( // North
                new Rectangle((int)(origin.X + (size.X - CheckboxSize) / 2f), (int)(origin.Y + size.Y - CheckboxSize), checkboxSize, checkboxSize),
                new Rectangle((int)origin.X, (int)origin.Y, (int)size.X, (int)(size.Y - CheckboxSize - LabelGap))),
        };
    }
}
