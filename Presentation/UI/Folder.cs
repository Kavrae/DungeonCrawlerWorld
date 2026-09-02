using FontStashSharp;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// A clickable header button that shows/hides a stack of child windows tiled vertically
/// beneath it -- e.g. NotificationCenter's single folder replacing its old always-visible
/// category bar. An Element in its own right (not a Window subclass) -- reuses Element's
/// generic header/Minimized/WrapContent machinery directly: the icon lives in the header
/// (drawn regardless of display mode -- see DrawHeader), and the child stack lives in the
/// content area, which Element's own Measure already zeroes out while Minimized -- collapsing
/// already hides *and* zero-sizes the children with no extra bookkeeping.
/// </summary>
public sealed class Folder : Element
{
    private static readonly Vector2 DefaultIconSize = new(32, 32);

    private readonly SpriteSheetService _spriteSheetService;
    private readonly SpriteRenderer _spriteRenderer;

    private Vector2 _iconSize;
    private string? _spriteName;
    private string _fallbackGlyph = string.Empty;
    private SpriteFontBase _fallbackGlyphFont = null!;
    private Color _backgroundColor;

    /// <summary>Gray-tints the icon when true (e.g. InventoryFolderController reflecting the entity's InventoryDisabledComponent) -- purely visual, doesn't affect whether the folder can still be clicked/expanded.</summary>
    public bool IsDisabled { get; set; }

    public Folder(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
        : base(fontService, elementPoolService, labelRenderer)
    {
        ArgumentNullException.ThrowIfNull(spriteSheetService);
        ArgumentNullException.ThrowIfNull(spriteRenderer);

        _spriteSheetService = spriteSheetService;
        _spriteRenderer = spriteRenderer;
    }

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        var folderOptions = options.Folder;
        _iconSize = folderOptions?.IconSize ?? DefaultIconSize;
        _spriteName = folderOptions?.SpriteName;
        _fallbackGlyph = folderOptions?.FallbackGlyph ?? string.Empty;
        _fallbackGlyphFont = FontService.GetFont((int)(_iconSize.Y * FontChrome.FolderFallbackGlyphFontFraction));
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
    /// expanded to." Confirmed bug when this instead derived width from the children's own
    /// already-measured CurrentSize: the folder visibly stayed at its expanded width every time
    /// it was closed after having been opened once, only its height ever actually collapsed.
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

    /// <summary>Background fill, then sprite-else-glyph icon via the shared SpriteOrGlyphRenderer -- IsDisabled gray-tints either form, mirroring MapWindow's dead-entity tint.</summary>
    protected override void DrawHeader(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;

        if (!IsTransparent)
        {
            spriteBatch.Draw(unitRectangle, HeaderRectangle, _backgroundColor);
        }

        // The header grows wider than the icon once expanded (see
        // RecalculateWrapContentWindowSize, which widens _header.Size to match content) -- the
        // icon itself stays fixed at _iconSize regardless, centered within that wider header
        // rect rather than stretched to fill it.
        var iconTopLeft = HeaderAbsolutePosition + (HeaderSize - _iconSize) / 2f;

        SpriteComponent? sprite = _spriteName is not null && SpriteManifest.TryGet(_spriteName, out var spriteComponent) ? spriteComponent : null;
        var spriteTint = IsDisabled ? Color.Gray : Color.White;
        var glyphColor = IsDisabled ? Color.Gray : Color.Black;

        SpriteOrGlyphRenderer.Draw(spriteBatch, _spriteSheetService, _spriteRenderer, LabelRenderer, sprite, _fallbackGlyphFont, _fallbackGlyph, glyphColor, iconTopLeft, _iconSize, spriteTint);
    }
}
