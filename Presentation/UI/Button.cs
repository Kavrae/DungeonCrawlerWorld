using FontStashSharp;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// A clickable, hoverable rectangle with left- and (optionally) right-aligned text -- e.g. a
/// context-menu option's "Copy    Ctrl+C", or a title-bar chrome button. A plain pooled Element,
/// attached either as a normal child (AddChild, for content-area use -- the ordinary Measure/
/// Arrange pipeline positions it exactly like any other child) or via Window.AddTitleButton (for
/// title-bar use -- constructed with parent: null and repositioned via PositionInHeader instead;
/// see AddTitleButton's and PositionInHeader's own doc comments for why title buttons need that
/// separate path). Both the hover highlight and the Outset/Inset press-bevel always apply,
/// everywhere -- there is no per-instance toggle for either; IsHovered/IsPressed are both driven
/// externally by UiInputController (see PressedButton/HoveredButton there), not self-polled.
/// </summary>
public sealed class Button(FontService fontService, ElementPoolService elementPoolService, GlyphRenderer glyphRenderer)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    public SpriteFontBase ContentFont { get; set; } = fontService.GetFont(12);

    public string LeftText { get; set; } = string.Empty;

    public string? RightText { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Defaults to WindowPalette.BodyTextColor (black) -- overridable via Text.TextColor, e.g. GridControl's sort tile needs white text against its own dark ContentColor.</summary>
    public Color TextColor { get; set; }

    public bool IsHovered { get; set; }

    /// <summary>True while the mouse is held down over this button -- DrawContent swaps Outset/Inset while true, giving the pressed-in look. Set by UiInputController on press/release (see PressedButton).</summary>
    public bool IsPressed { get; private set; }

    /// <summary>A disabled button is excluded from hit-testing entirely -- never hovered, pressed, or clicked -- rather than every caller having to remember to check Enabled itself.</summary>
    protected override bool IsHitTestable => base.IsHitTestable && Enabled;

    private BorderStyle _restingBorderStyle;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        CanUserFocus = false;

        _border.Show = options.Chrome?.ShowBorder ?? true;

        _contentState.BackgroundColor = options.Content?.ContentColor ?? Color.LightGray;

        LeftText = options.Text?.Text ?? string.Empty;
        RightText = null;
        TextColor = options.Text?.TextColor ?? WindowPalette.BodyTextColor;
        Enabled = true;
        IsHovered = false;
        IsPressed = false;

        _restingBorderStyle = options.Chrome?.BorderStyle ?? BorderStyle.Outset;
        BorderStyle = _restingBorderStyle;
    }

    /// <summary>
    /// Always shows Inset (sunken) while pressed, restoring the resting style on release --
    /// Element.Draw reads BorderStyle directly for its border pass, so this is the only hook
    /// needed for the pressed-in look without Button overriding Draw itself. Pressed always
    /// means Inset specifically, not "whatever the resting style isn't": a raised (Outset)
    /// button pressing inward to Inset is the standard convention, but a button whose resting
    /// style is already Inset (e.g. GridControl's sort tile, sunken like the row's other
    /// tiles) must stay Inset while held too -- swapping it to Outset would read as popping
    /// up/releasing at exactly the moment it's being pressed down, backwards from what the
    /// gesture means.
    /// </summary>
    public void SetPressed(bool isPressed)
    {
        IsPressed = isPressed;
        BorderStyle = isPressed ? BorderStyle.Inset : _restingBorderStyle;
    }

    /// <summary>
    /// Positions this button relative to a header host's own AbsolutePosition rather than the
    /// generic parent-content-relative Arrange formula -- the header-relative equivalent of what
    /// SetRelativePosition/MeasureAndArrange do for an ordinary parented child (see
    /// Window.AddTitleButton's own doc comment for why title buttons need this separate path).
    /// The only caller is Window.RepositionTitleButtons; a content-area button (added via
    /// AddChild) is positioned by the ordinary pipeline instead and never calls this. Only the
    /// AbsolutePosition source genuinely differs from the generic path (a header host position
    /// instead of a parent) -- everything downstream of that (content size, every Rectangle) is
    /// identical work, so it's delegated to the same RecalculateFixedSize/RecalculateRectangles
    /// the generic Measure/Arrange pipeline itself uses, rather than re-deriving that math here.
    /// </summary>
    internal void PositionInHeader(Vector2 relativePosition, Vector2 headerHostAbsolutePosition)
    {
        _geometry.RelativePosition = relativePosition;
        RecalculateFixedSize(); // Keeps CurrentSize/ContentSize current -- idempotent, since a title button's own OriginalSize never changes after Build.

        _geometry.AbsolutePosition = headerHostAbsolutePosition + relativePosition;
        _contentState.AbsolutePosition = _geometry.AbsolutePosition + BorderInset; // No header of its own (HeaderInsetHeight is 0), unlike RecalculateAbsolutePositions' general case.

        RecalculateRectangles();
    }

    /// <summary>Translucent dark overlay for the hover highlight -- darkens whatever ContentColor this button actually has (LightGray by default) rather than a fixed replacement color, so it still reads correctly if a caller ever sets a custom ContentColor. Deliberately not WindowPalette.HighlightColor (a gold tint meant for content rows sitting on a light background) -- a button's own resting look is already a mid-gray raised bevel, where a gold tint reads oddly; a straightforward darkening matches how a pressed/hovered physical button looks.</summary>
    private static readonly Color HoverOverlayColor = Color.Black * 0.15f;

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        if (IsHovered)
        {
            spriteBatch.Draw(unitRectangle, ContentRectangle, HoverOverlayColor);
        }

        var textColor = Enabled ? TextColor : Color.Gray;

        if (string.IsNullOrEmpty(RightText))
        {
            // No hotkey column -- ink-centered (not left-aligned), the same look every title
            // button ("X", "_", "O") has always had. DrawLeftAligned's box-based (not
            // ink-based) vertical centering reads visibly low for a single short glyph in a
            // small square button -- see GlyphRenderer's own doc comment on why MeasureString's
            // line box is a poor stand-in for a glyph's actual rendered ink.
            if (!string.IsNullOrEmpty(LeftText))
            {
                GlyphRenderer.DrawCentered(spriteBatch, ContentFont, LeftText, ContentAbsolutePosition, ContentSize, textColor);
            }
        }
        else
        {
            // A hotkey column is present (context-menu option row) -- left/right split instead.
            if (!string.IsNullOrEmpty(LeftText))
            {
                GlyphRenderer.DrawLeftAligned(spriteBatch, ContentFont, LeftText, ContentAbsolutePosition, ContentSize, textColor);
            }

            GlyphRenderer.DrawRightAligned(spriteBatch, ContentFont, RightText, ContentAbsolutePosition, ContentSize, textColor);
        }
    }
}
