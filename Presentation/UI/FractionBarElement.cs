using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;

namespace Presentation.UI;

/// <summary>A single resource bar at whatever rectangle this Element is sized/positioned to, filled to a caller-supplied fraction.</summary>
/// <remarks>
/// The entity-agnostic counterpart to HealthBarElement -- that one resolves its own fraction from
/// an entityId plus Health/BodyPart/StatModifier pools (a whole entity's summed total);
/// this one just draws whatever fraction/colors Configure was last given, for callers that already
/// have the number in hand (e.g. HealthWindow's own per-body-part rows) and would otherwise need a
/// bespoke entity-resolving Element for a value that isn't really "one entity's health" at all.
/// </remarks>
public sealed class FractionBarElement(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer)
    : Element(fontService, elementPoolService, labelRenderer)
{
    private float _fraction;
    private bool _hasResource;
    private Color _outlineColor;
    private Func<float, Color> _fractionColor = null!;
    private readonly ResourceBarValueText _valueText = new();
    private bool _showValueText;

    /// <summary>The "current / maximum" text the bar draws, or null when Configure was given no displayedValues.</summary>
    internal string? ValueText => _showValueText ? _valueText.Text : null;

    /// <param name="displayedValues">Already-rounded current and maximum to draw centred on the bar (see ResourceBarValueText's rounding helpers); null draws no text.</param>
    public void Configure(float fraction, bool hasResource, Color outlineColor, Func<float, Color> fractionColor, (int Current, int Maximum)? displayedValues = null)
    {
        _fraction = fraction;
        _hasResource = hasResource;
        _outlineColor = outlineColor;
        _fractionColor = fractionColor;

        _showValueText = displayedValues is not null;
        if (displayedValues is { } values)
        {
            _valueText.Update(values.Current, values.Maximum);
        }
    }

    public override void DrawContent(GameTime gameTime)
    {
        var origin = ContentAbsolutePosition;
        var size = ContentSize;
        var bar = new Rectangle((int)origin.X, (int)origin.Y, (int)size.X, (int)size.Y);

        ResourceBarRenderer.Draw(ElementPoolService.SpriteBatch, ElementPoolService.UnitRectangle, bar, _fraction, _hasResource, _outlineColor, _fractionColor);

        if (_showValueText)
        {
            ResourceBarRenderer.DrawCenteredValueText(ElementPoolService.SpriteBatch, FontService.GetFont((int)(bar.Height * FontChrome.ResourceBarValueFontFraction)), bar, _valueText.Text);
        }
    }
}
