using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>
/// An action: a pressable control with a raised bevel that sinks while pressed and darkens while hovered, showing
/// whatever its children are. SetText, SetIcon and ShowKeyLabel add the common children in one call -- a label
/// centred in the button, a sprite-or-glyph picture filling it, a key label in its top-left corner.
/// </summary>
/// <remarks>
/// Attached as an ordinary child (AddChild, laid out like any element) or to a window's title bar
/// (Window.AddTitleButton, built with no parent and placed by PositionInHeader). Children may be laid out by its tiling
/// as any element's are: a Dropdown row is a Horizontal button holding an icon, a label that fills and a hotkey.
/// </remarks>
public sealed class Button(ElementServices services)
    : PressableElement(services)
{
    /// <summary>What ContentFont is reset to on every Build, so a pooled button never keeps a font a previous user set.</summary>
    private readonly SpriteFontBase _defaultContentFont = services.FontService.GetFont(FontChrome.DefaultFontSize);

    /// <summary>The label SetText added, updated by later calls; null until then.</summary>
    private Label? _textLabel;

    /// <summary>The picture SetIcon added, with what it shows, so a font change can redraw its glyph; null until then.</summary>
    private (IconElement Icon, string? SpriteName, string Glyph)? _icon;

    private BorderStyle _restingBorderStyle;

    /// <summary>The font SetText's label and SetIcon's glyph are drawn in.</summary>
    public SpriteFontBase ContentFont
    {
        get;
        set
        {
            field = value;
            _textLabel?.SetFont(value);
            if (_icon is { } icon)
            {
                icon.Icon.Configure(icon.SpriteName, icon.Glyph, TextColor, value);
            }
        }
    } = services.FontService.GetFont(FontChrome.DefaultFontSize);

    /// <summary>The colour SetText's label and SetIcon's glyph are drawn in: Text.TextColor, else WindowPalette.BodyTextColor.</summary>
    public Color TextColor
    {
        get;
        set
        {
            field = value;
            _textLabel?.TextColor = value;
            if (_icon is { } icon)
            {
                icon.Icon.Configure(icon.SpriteName, icon.Glyph, value, ContentFont);
            }
        }
    }

    /// <summary>Whether the button darkens itself while hovered; true unless its holder draws which of its buttons is current itself (a Dropdown).</summary>
    public bool ShowsHoverHighlight { get; set; }

    /// <summary>Holds the button sunken (Inset) while on, as a switch that is on -- the Map tab's grid switch, its chosen layer and neighborhood.</summary>
    public bool IsSelected
    {
        get;
        set
        {
            field = value;
            ApplyBorderStyle();
        }
    }

    /// <summary>The bevel the button shows while neither pressed nor selected: Chrome.BorderStyle, else Outset. A tab strip raises its open tab by setting it.</summary>
    public BorderStyle RestingBorderStyle
    {
        get => _restingBorderStyle;
        set
        {
            _restingBorderStyle = value;
            ApplyBorderStyle();
        }
    }

    /// <summary>The text SetText shows, or empty when it hasn't been called.</summary>
    public string Text => _textLabel?.Text ?? string.Empty;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        CanUserFocus = false;

        _border.Show = options.Chrome?.ShowBorder ?? true;

        _contentState.BackgroundColor = options.Content?.ContentColor ?? WindowPalette.PanelContentColor;

        _textLabel = null;
        _icon = null;
        ContentFont = _defaultContentFont;
        TextColor = options.Text?.TextColor ?? WindowPalette.BodyTextColor;
        Enabled = true;
        ShowsHoverHighlight = true;
        IsSelected = false;

        _restingBorderStyle = options.Chrome?.BorderStyle ?? BorderStyle.Outset;
        ApplyBorderStyle();
    }

    /// <summary>Shows text centred in the button, in ContentFont and TextColor (grey while disabled); a later call changes the text shown.</summary>
    public Label SetText(string text)
    {
        if (_textLabel is { } label)
        {
            label.SetText(text);
            return label;
        }

        label = ElementPoolService.CreateElement<Label>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, DisplayMode = ElementDisplayMode.Fill },
            Text = new TextOptions { Text = text, TextColor = TextColor },
        });
        label.IsCentered = true;
        label.SetFont(ContentFont);
        _textLabel = label;
        AddChild(label);
        return label;
    }

    /// <summary>Shows spriteName's sprite filling the button, or glyph centred in ContentFont and TextColor when the manifest has no such sprite; greyed while disabled.</summary>
    public IconElement SetIcon(string? spriteName, string glyph)
    {
        var icon = ElementPoolService.CreateElement<IconElement>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, DisplayMode = ElementDisplayMode.Fill, IsTransparent = true },
        });
        icon.Configure(spriteName, glyph, TextColor, ContentFont);
        _icon = (icon, spriteName, glyph);
        AddChild(icon);
        return icon;
    }

    /// <summary>Shows key, outlined, in the button's top-left corner over whatever else it shows -- the key that also opens what the button opens; faded while disabled.</summary>
    public Label ShowKeyLabel(string key)
    {
        var label = ElementPoolService.CreateElement<Label>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = KeyLabelPadding - BorderInset },
            Text = new TextOptions { Text = key, TextColor = ContrastTextRenderer.FillColor },
        });
        label.IsOutlined = true;
        label.SetFont(FontService.GetFont((int)(OriginalSize.Y * FontChrome.ButtonHotkeyLabelFontFraction)));
        AddChild(label);
        return label;
    }

    /// <summary>Where a key label sits from the button's outer top-left corner.</summary>
    private static readonly Vector2 KeyLabelPadding = new(4f, 2f);

    protected override void OnPressedChanged() => ApplyBorderStyle();

    /// <summary>Inset (sunken) while pressed or selected, the resting style otherwise -- a button already resting sunken stays sunken while held, never popping up as it is pressed.</summary>
    private void ApplyBorderStyle() =>
        BorderStyle = IsPressed || IsSelected ? BorderStyle.Inset : _restingBorderStyle;

    /// <summary>
    /// Places this title button relative to its window's header rather than a parent's content area, then lays out what it
    /// shows; the only caller is Window.RepositionTitleButtons (see Window.AddTitleButton).
    /// </summary>
    internal void PositionInHeader(Vector2 relativePosition, Vector2 headerHostAbsolutePosition)
    {
        _geometry.RelativePosition = relativePosition;
        RecalculateFixedSize();

        _geometry.AbsolutePosition = headerHostAbsolutePosition + relativePosition;
        _contentState.BackgroundAbsolutePosition = _geometry.AbsolutePosition + BorderInset;
        _contentState.AbsolutePosition = _contentState.BackgroundAbsolutePosition + ChildContentPadding;

        RecalculateRectangles();
        LayOutChildrenInPlace();
    }

    /// <summary>The hover highlight over the button's own fill, under what it shows.</summary>
    public override void DrawContent(GameTime gameTime)
    {
        if (IsHovered && ShowsHoverHighlight)
        {
            Render.Batch.FillRectangle(ToContentLocal(_contentState.BackgroundRectangle), WindowPalette.HoverDark);
        }
    }
}
