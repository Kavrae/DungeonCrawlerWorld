using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>
/// A clickable header button that shows/hides a stack of child windows tiled vertically
/// beneath it -- a notification tray, say. An Element in its own right (not a Window subclass) -- reuses Element's
/// generic header/Minimized/WrapContent machinery directly: the icon lives in the header
/// (drawn regardless of display mode -- see DrawHeader), and the child stack lives in the
/// content area, which Element's own Measure already zeroes out while Minimized -- collapsing
/// already hides *and* zero-sizes the children with no extra bookkeeping.
/// </summary>
public sealed class Folder : Element
{
    private static readonly Vector2 DefaultIconSize = new(32, 32);

    private readonly SpriteSheetService _spriteSheetService;

    private Vector2 _iconSize;
    /// <summary>The icon FolderOptions.SpriteName names, resolved when the folder is built; null draws the fallback glyph.</summary>
    private SpriteView? _sprite;
    private string _fallbackGlyph = string.Empty;
    private FontStashTextMeasurer _fallbackGlyphFontMeasurer = null!;
    private Color _backgroundColor;

    public Folder(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService, SpriteSheetService spriteSheetService)
        : base(fontService, elementPoolService, renderService)
    {
        _spriteSheetService = spriteSheetService;
    }

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        var folderOptions = options.Folder;
        _iconSize = folderOptions?.IconSize ?? DefaultIconSize;
        _sprite = _spriteSheetService.Sprites.FirstOrNull(folderOptions?.SpriteName);
        _fallbackGlyph = folderOptions?.FallbackGlyph ?? string.Empty;
        _fallbackGlyphFontMeasurer = FontService.GetTextMeasurer(FontService.GetFont((int)(_iconSize.Y * FontChrome.FolderFallbackGlyphFontFraction)));
        _backgroundColor = folderOptions?.BackgroundColor ?? WindowPalette.HeaderBackground;

        _canContainChildren = true;
        _childrenTileMode = ChildElementTileMode.Vertical;

        // The header is this control's icon button, always shown (including while Minimized)
        // so the icon stays visible whether the folder is expanded or collapsed.
        // BorderStyle.Outset (set via Chrome options by callers, same as Button's own default)
        // gives it the "standard button border" look.
        _headerState.ShowHeader = true;
        _headerState.ShowHeaderWhenMinimized = true;
        _headerState.OriginalSize = _iconSize;
        _headerState.Size = _iconSize;
    }

    public override void Initialize()
    {
        base.Initialize();

        // Starts collapsed -- a HUD-persistent control defaults to its smallest footprint,
        // expanding only once the user actually asks to see its contents.
        SetDisplayMode(ElementDisplayMode.Minimized);
    }

    /// <summary>
    /// Always collapses to exactly _iconSize, in both dimensions, regardless of how wide the
    /// folder had expanded to fit its children -- a minimized Folder should read as "collapsed to
    /// its smallest footprint" unconditionally, the same shape it has before it's ever been
    /// opened for the first time, not "collapsed height only, still as wide as whatever it last
    /// expanded to." Deriving the width from the children's measured size would leave it at its expanded width.
    /// </summary>
    protected override void RecalculateMinimizedSize()
    {
        _headerState.Size = _iconSize;
        _contentState.Size = Vector2.Zero;
        _contentState.BackgroundSize = Vector2.Zero;
        _geometry.CurrentSize = _iconSize + BorderInsetDoubled;
    }

    /// <summary>
    /// The header must never shrink narrower than the icon it always shows (see DrawHeader) --
    /// without this, a childless expanded Folder's header width comes entirely from
    /// RecalculateWrapContentSize's own ContentPadding floor (see Element's own doc comment on
    /// that), which is far narrower than a real icon and leaves the icon unclickable.
    /// </summary>
    protected override float MinimumHeaderWidth() => _iconSize.X;

    /// <summary>
    /// The icon lives in the header rect, not the content rect, so toggling here (not
    /// OnContentClickAction/Clicked, which only fire for content-rect clicks) is what makes
    /// clicking the folder itself -- in either display mode -- expand/collapse it.
    /// </summary>
    protected override void OnHeaderClickAction(Point mousePosition)
    {
        SetDisplayMode(DisplayMode == ElementDisplayMode.Minimized
            ? ElementDisplayMode.WrapContent
            : ElementDisplayMode.Minimized);
    }

    /// <summary>Background fill, then sprite-else-glyph icon via the shared SpriteOrGlyphRenderer.</summary>
    protected override void DrawHeader(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var unitRectangle = Render.UnitRectangle;

        if (!IsTransparent)
        {
            spriteBatch.Draw(unitRectangle, HeaderRectangle, _backgroundColor);
        }

        // The header grows wider than the icon once expanded (see
        // RecalculateWrapContentWindowSize, which widens _header.Size to match content) -- the
        // icon itself stays fixed at _iconSize regardless, centered within that wider header
        // rect rather than stretched to fill it.
        var iconTopLeft = HeaderAbsolutePosition + (HeaderSize - _iconSize) / 2f;


        SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _sprite, _fallbackGlyphFontMeasurer, _fallbackGlyph, Color.Black, iconTopLeft, _iconSize, Color.White);
    }
}
