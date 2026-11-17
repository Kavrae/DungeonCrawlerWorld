using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>One line of text in a font and colour, cut short with "..." when its width can't hold it.</summary>
/// <remarks>
/// Not a Window: no header, scrolling or wrapping, so a row of labels stays cheap; TextWindow is for text over several
/// lines. Transparent and WrapContent unless built otherwise: as wide as its text within what its parent offers, one line tall. The
/// text's width is measured once each time the text or font changes, and the cut made once each time they or the
/// label's width change, never per frame or per layout pass. In a taller content area the line is centred vertically.
/// </remarks>
public sealed class Label(ElementServices services) : Element(services)
{
    private string _text = string.Empty;
    private SpriteFontBase _font = services.FontService.GetFont(FontChrome.DefaultFontSize);

    /// <summary>The text's full width in its font, kept until either changes.</summary>
    private readonly KeyedCache<(string Text, SpriteFontBase Font), float> _textWidth = new();

    /// <summary>The text as drawn, kept until the text, the font or the width changes.</summary>
    private readonly KeyedCache<(string Text, SpriteFontBase Font, float Width), string> _shownText = new();

    public string Text => _text;

    public SpriteFontBase Font => _font;

    public Color TextColor { get; set; }

    /// <summary>The colour the text is drawn in while the control holding the label is disabled; grey unless set.</summary>
    public Color DisabledTextColor { get; set; }

    /// <summary>Whether the text's ink is centred in the label both ways (a button's or a toggle's label filling its space), rather than its line drawn from the left.</summary>
    public bool IsCentered { get; set; }

    /// <summary>Whether the text is outlined to read over any background (a key label over an icon); faded rather than recoloured while its control is disabled.</summary>
    public bool IsOutlined { get; set; }

    /// <summary>Whether the text is drawn bold, as a second copy a pixel to the right (a menu's heading); the label is a pixel wider for it.</summary>
    public bool IsBold { get; set; }

    /// <summary>Whether the label shows its whole text as its own tooltip while it cuts it short, and no tooltip otherwise (a browser row's name).</summary>
    public bool ShowsFullTextWhenCut { get; set; }

    /// <summary>The text as drawn at the label's current width: Text itself, or its longest start that fits followed by "...".</summary>
    public string ShownText => _shownText.Get(
        (_text, _font, ContentSize.X),
        FontService,
        static (key, fontService) => StringUtility.TruncateWithEllipsis(fontService.GetTextMeasurer(key.Font), key.Text, key.Width));

    /// <summary>Whether the label cuts its text short at its current width.</summary>
    public bool IsCut => !ReferenceEquals(ShownText, _text);

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        if (options.Layout?.DisplayMode is null)
        {
            _geometry.DisplayMode = ElementDisplayMode.WrapContent;
        }

        _canContainChildren = false;
        _isTransparent = options.Layout?.IsTransparent ?? true;
        _text = options.Text?.Text ?? string.Empty;
        TextColor = options.Text?.TextColor ?? WindowPalette.BodyTextColor;
        DisabledTextColor = Color.Gray;
        IsCentered = false;
        IsOutlined = false;
        IsBold = false;
        ShowsFullTextWhenCut = false;
        _font = FontService.GetFont(FontChrome.DefaultFontSize);
    }

    /// <summary>Shows text, re-laying out the label; the text already shown does nothing, so a caller may push the same text every refresh.</summary>
    public void SetText(string text)
    {
        if (string.Equals(text, _text, StringComparison.Ordinal))
        {
            return;
        }

        _text = text;
        MeasureAndArrange();
    }

    /// <summary>Draws the text in font, re-laying out the label; the font already used does nothing.</summary>
    public void SetFont(SpriteFontBase font)
    {
        if (ReferenceEquals(font, _font))
        {
            return;
        }

        _font = font;
        MeasureAndArrange();
    }

    protected override void RecalculateWrapContentSize()
    {
        var textWidth = _textWidth.Get(
            (_text, _font),
            FontService,
            static (key, fontService) => fontService.GetTextMeasurer(key.Font).MeasureWidth(key.Text));

        var naturalSize = new Vector2(MathF.Ceiling(textWidth) + (IsBold ? 1f : 0f), MathF.Ceiling(_font.LineHeight)) + BorderInsetDoubled;
        _geometry.CurrentSize = Vector2.Max(Vector2.Zero, Vector2.Min(Vector2.Max(naturalSize, _geometry.MinimumSize), EffectiveMaximumSize));
        _contentState.BackgroundSize = Vector2.Max(Vector2.Zero, _geometry.CurrentSize - BorderInsetDoubled);
        _contentState.Size = _contentState.BackgroundSize;
        ShowFullTextWhenCut();
    }

    protected override void RecalculateFixedSize()
    {
        base.RecalculateFixedSize();
        ShowFullTextWhenCut();
    }

    protected override void RecalculateFillSize()
    {
        base.RecalculateFillSize();
        ShowFullTextWhenCut();
    }

    /// <summary>Sets the tooltip to the whole text while it is cut at the size just measured, when ShowsFullTextWhenCut asks for it.</summary>
    private void ShowFullTextWhenCut()
    {
        if (ShowsFullTextWhenCut)
        {
            TooltipText = IsCut ? _text : null;
        }
    }

    public override void DrawContent(GameTime gameTime)
    {
        var shownText = ShownText;
        if (shownText.Length == 0)
        {
            return;
        }

        var isDisabled = PressableElement.IsHeldByDisabledControl(this);
        var color = isDisabled && !IsOutlined ? DisabledTextColor : TextColor;
        var alphaMultiplier = isDisabled && IsOutlined ? DisabledOutlineOpacity : 1f;
        if (IsCentered)
        {
            LabelRenderer.DrawCentered(Render.Batch, FontService.GetTextMeasurer(_font), shownText, Vector2.Zero, ContentSize, color, IsOutlined, alphaMultiplier);
            return;
        }

        var position = new Vector2(0, System.Math.Max(0f, MathF.Floor((ContentSize.Y - _font.LineHeight) / 2f)));
        if (IsBold)
        {
            LabelRenderer.Draw(Render.Batch, _font, shownText, position + new Vector2(1, 0), color, IsOutlined, alphaMultiplier);
        }

        LabelRenderer.Draw(Render.Batch, _font, shownText, position, color, IsOutlined, alphaMultiplier);
    }

    /// <summary>How opaque an outlined label is while its control is disabled.</summary>
    private const float DisabledOutlineOpacity = 0.5f;
}
