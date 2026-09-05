using FontStashSharp;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
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
///
/// spriteSheetService/spriteRenderer are optional -- null for the overwhelming majority of
/// Buttons (title-bar chrome, context-menu rows), which never set ElementOptions.Button and so
/// never reach the sprite-drawing branch of DrawContent below. Set only by the one shared
/// ElementFactoryRegistry registration, so a caller that does want an icon (e.g.
/// InventoryWindowController/AbilityScoreWindowController's own HUD-trigger buttons, mirroring
/// Folder's own sprite-or-glyph icon) can opt in via ElementOptions.Button.SpriteName without
/// every other Button consumer needing to thread two services it will never use.
/// </summary>
public sealed class Button(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, SpriteSheetService? spriteSheetService = null, SpriteRenderer? spriteRenderer = null)
    : Element(fontService, elementPoolService, labelRenderer)
{
    public SpriteFontBase ContentFont { get; set; } = fontService.GetFont(FontChrome.DefaultFontSize);

    public string LeftText { get; set; } = string.Empty;

    public string? RightText { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Defaults to WindowPalette.BodyTextColor (black) -- overridable via Text.TextColor, e.g. GridControl's sort tile needs white text against its own dark ContentColor.</summary>
    public Color TextColor { get; set; }

    /// <summary>
    /// Forces left alignment even when RightText is empty -- ContextMenu's own option rows set
    /// this so they always read as a native left-aligned menu command, regardless of whether a
    /// hotkey column happens to be present, rather than falling into the single-glyph
    /// ink-centered look DrawContent otherwise uses for a title button ("X", "_", "O") when
    /// RightText is empty. False by default (that ink-centered look), reset on every Build since
    /// pooled Buttons are shared across both title-button and context-menu uses.
    /// </summary>
    public bool LeftAlign { get; set; }

    public bool IsHovered { get; set; }

    /// <summary>True while the mouse is held down over this button -- DrawContent swaps Outset/Inset while true, giving the pressed-in look. Set by UiInputController on press/release (see PressedButton).</summary>
    public bool IsPressed { get; private set; }

    /// <summary>A disabled button is excluded from hit-testing entirely -- never hovered, pressed, or clicked -- rather than every caller having to remember to check Enabled itself.</summary>
    protected override bool IsHitTestable => base.IsHitTestable && Enabled;

    private BorderStyle _restingBorderStyle;

    /// <summary>Set via ElementOptions.Button.SpriteName -- null (the default) means this button draws its ordinary Left/RightText, never the sprite-or-glyph icon branch below. Reset on every Build, same as every other field here, so a pooled-and-reused Button can't carry a stale icon from its previous consumer.</summary>
    private string? _spriteName;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        CanUserFocus = false;

        _border.Show = options.Chrome?.ShowBorder ?? true;

        _contentState.BackgroundColor = options.Content?.ContentColor ?? WindowPalette.PanelContentColor;

        LeftText = options.Text?.Text ?? string.Empty;
        RightText = null;
        TextColor = options.Text?.TextColor ?? WindowPalette.BodyTextColor;
        Enabled = true;
        IsHovered = false;
        IsPressed = false;
        LeftAlign = false;
        _spriteName = options.Button?.SpriteName;

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
        // No header of its own (HeaderInsetHeight is 0), unlike RecalculateAbsolutePositions' general
        // case. ChildContentPadding is always Vector2.Zero here too (a title button has no
        // children), so BackgroundAbsolutePosition/AbsolutePosition coincide -- still both set
        // explicitly, matching RecalculateAbsolutePositions' own split, since HandleClick's content
        // hit-test reads BackgroundRectangle (derived from BackgroundAbsolutePosition), not Rectangle.
        _contentState.BackgroundAbsolutePosition = _geometry.AbsolutePosition + BorderInset;
        _contentState.AbsolutePosition = _contentState.BackgroundAbsolutePosition + ChildContentPadding;

        RecalculateRectangles();
    }

    /// <summary>Translucent dark overlay for the hover highlight -- darkens whatever ContentColor this button actually has (WindowPalette.PanelContentColor by default) rather than a fixed replacement color, so it still reads correctly if a caller ever sets a custom ContentColor. Deliberately not WindowPalette.HighlightColor (a gold tint meant for content rows sitting on a light background) -- a button's own resting look is already a mid-gray raised bevel, where a gold tint reads oddly; a straightforward darkening matches how a pressed/hovered physical button looks.</summary>
    private static readonly Color HoverOverlayColor = WindowPalette.HoverDark;

    /// <summary>
    /// Horizontal breathing room around LeftText/RightText when both are present -- DrawLeftAligned/
    /// DrawRightAligned otherwise sit flush against the row's own edges, fine for a single
    /// ink-centered glyph (see the RightText-empty branch below) but visibly cramped for a
    /// two-column context-menu row. Internal, not private: ContextMenu.MeasureWidth sizes its
    /// rows off this same constant, rather than guessing its own separate padding value that
    /// could silently drift out of sync with what actually gets drawn.
    /// </summary>
    internal const float HorizontalTextInset = 4f;

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        if (IsHovered)
        {
            spriteBatch.Draw(unitRectangle, ContentRectangle, HoverOverlayColor);
        }

        var textColor = Enabled ? TextColor : Color.Gray;

        // Icon mode -- LeftText/RightText's own draw below is for a plain text-only Button
        // (title-bar chrome, context-menu rows); a caller that opted into ElementOptions.Button.
        // SpriteName instead gets the same sprite-or-glyph icon Folder's own header draws,
        // centered across the whole content area, with LeftText read as the fallback glyph if
        // the sprite name isn't found in the manifest (or spriteSheetService/spriteRenderer were
        // never wired -- see this class's own doc comment on why they're optional).
        if (_spriteName is not null && spriteSheetService is not null && spriteRenderer is not null)
        {
            SpriteComponent? sprite = SpriteManifest.TryGet(_spriteName, out var spriteComponent) ? spriteComponent : null;
            var spriteTint = Enabled ? Color.White : Color.Gray;
            SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, spriteRenderer, LabelRenderer, sprite, ContentFont, LeftText, textColor, ContentAbsolutePosition, ContentSize, spriteTint);
            return;
        }

        if (string.IsNullOrEmpty(RightText) && !LeftAlign)
        {
            // No hotkey column and no explicit left-align request -- ink-centered, the same
            // look every title button ("X", "_", "O") has always had. DrawLeftAligned's
            // box-based (not ink-based) vertical centering reads visibly low for a single short
            // glyph in a small square button -- see LabelRenderer's own doc comment on why
            // MeasureString's line box is a poor stand-in for a glyph's actual rendered ink.
            if (!string.IsNullOrEmpty(LeftText))
            {
                LabelRenderer.DrawCentered(spriteBatch, ContentFont, LeftText, ContentAbsolutePosition, ContentSize, textColor);
            }
        }
        else
        {
            // A hotkey column is present, or LeftAlign was explicitly requested (a context-menu
            // option row) -- left/right split instead, inset by HorizontalTextInset so neither
            // column sits flush against the row's edges.
            var textFootprintPosition = ContentAbsolutePosition + new Vector2(HorizontalTextInset, 0);
            var textFootprintSize = ContentSize - new Vector2(HorizontalTextInset * 2, 0);

            if (!string.IsNullOrEmpty(LeftText))
            {
                LabelRenderer.DrawLeftAligned(spriteBatch, ContentFont, LeftText, textFootprintPosition, textFootprintSize, textColor);
            }

            if (!string.IsNullOrEmpty(RightText))
            {
                LabelRenderer.DrawRightAligned(spriteBatch, ContentFont, RightText, textFootprintPosition, textFootprintSize, textColor);
            }
        }
    }
}
