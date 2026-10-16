using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

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
    /// <summary>The "current / maximum" text drawn, rebuilt only when either number changes.</summary>
    private readonly KeyedCache<(int Current, int Maximum), string> _valueTextCache = new();
    private string _valueText = string.Empty;
    private readonly TextSizeCache _valueTextSize = new();
    private bool _showValueText;

    /// <summary>The "current / maximum" text the bar draws, or null when Configure was given no displayedValues.</summary>
    internal string? ValueText => _showValueText ? _valueText : null;

    /// <param name="displayedValues">Already-rounded current and maximum to draw centred on the bar (rounded by the caller's own rules); null draws no text.</param>
    public void Configure(float fraction, bool hasResource, Color outlineColor, Func<float, Color> fractionColor, (int Current, int Maximum)? displayedValues = null)
    {
        _fraction = fraction;
        _hasResource = hasResource;
        _outlineColor = outlineColor;
        _fractionColor = fractionColor;

        _showValueText = displayedValues is not null;
        if (displayedValues is { } values)
        {
            _valueText = _valueTextCache.Get(values, static key => ResourceBarValueText.Format(key.Current, key.Maximum));
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
            var valueFont = FontService.GetFont((int)(bar.Height * FontChrome.ResourceBarValueFontFraction));
            ResourceBarRenderer.DrawCenteredValueText(ElementPoolService.SpriteBatch, valueFont, bar, _valueText, _valueTextSize.SizeOf(valueFont, _valueText).X);
        }
    }
}
