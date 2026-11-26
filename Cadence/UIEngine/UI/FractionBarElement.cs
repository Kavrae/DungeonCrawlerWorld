using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace UIEngine.UI;

/// <summary>A single resource bar at whatever rectangle this Element is sized/positioned to, filled to a caller-supplied fraction.</summary>
/// <remarks>
/// Draws whatever fraction/colors Configure was last given, for callers that already
/// have the number in hand.
/// </remarks>
public sealed class FractionBarElement(ElementServices services)
    : Element(services)
{
    private float _fraction;
    private bool _hasResource;
    private Color _outlineColor;
    private Func<float, Color> _fractionColor = null!;
    private readonly ResourceBarValueLabel _valueLabel = new();

    /// <summary>The "current / maximum" text the bar draws, or null when Configure was given no displayedValues.</summary>
    internal string? ValueText => _valueLabel.Text;

    /// <param name="displayedValues">Already-rounded current and maximum to draw centred on the bar (rounded by the caller's own rules); null draws no text.</param>
    public void Configure(float fraction, bool hasResource, Color outlineColor, Func<float, Color> fractionColor, (int Current, int Maximum)? displayedValues = null)
    {
        _fraction = fraction;
        _hasResource = hasResource;
        _outlineColor = outlineColor;
        _fractionColor = fractionColor;

        if (displayedValues is { } values)
        {
            _valueLabel.Show(values);
            return;
        }

        _valueLabel.Hide();
    }

    public override void DrawContent(GameTime gameTime)
    {
        var bar = LocalContentRectangle;

        ResourceBarRenderer.Draw(Render.Batch, bar, _fraction, _hasResource, _outlineColor, _fractionColor);
        _valueLabel.Draw(Render.Batch, FontService, bar);
    }
}
