using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.UI.Chrome;

namespace UIEngine.Rendering;

/// <summary>The "current / maximum" text a resource bar draws centred on itself, with its text, size and font cached.</summary>
/// <remarks>
/// The text is rebuilt only when either displayed number changes, and the font only when the bar's height does, so a
/// bar may show the same values every update. Shared by FractionBarElement and the HUD resource bars.
/// </remarks>
public sealed class ResourceBarValueLabel
{
    private readonly KeyedCache<(int Current, int Maximum), string> _textCache = new();
    private readonly TextSizeCache _textSize = new();
    private readonly KeyedCache<int, SpriteFontBase> _font = new();

    /// <summary>The text drawn, or null while the bar shows none.</summary>
    public string? Text { get; private set; }

    /// <param name="displayedValues">Already-rounded current and maximum, rounded by the caller's own rules.</param>
    public void Show((int Current, int Maximum) displayedValues) =>
        Text = _textCache.Get(displayedValues, static key => ResourceBarValueText.Format(key.Current, key.Maximum));

    public void Hide() => Text = null;

    public void Draw<TTarget>(TTarget target, FontService fontService, Rectangle bar)
        where TTarget : ISpriteDrawTarget
    {
        if (Text is not { } text)
        {
            return;
        }

        var font = _font.Get(bar.Height, fontService, static (barHeight, fonts) => fonts.GetFont((int)(barHeight * FontChrome.ResourceBarValueFontFraction)));
        ResourceBarRenderer.DrawCenteredValueText(target, font, bar, text, _textSize.SizeOf(font, text).X);
    }
}
